using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.RateLimiting;
using Npgsql;
using System.Net;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Threading.RateLimiting;

if (args.Contains("--healthcheck"))
{
    try
    { using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(3) }; using var response = await client.GetAsync("http://127.0.0.1:8080/api/health"); Environment.ExitCode = response.IsSuccessStatusCode ? 0 : 1; }
    catch { Environment.ExitCode = 1; }
    return;
}
var builder = WebApplication.CreateBuilder(args);
var password = builder.Configuration["APP_PASSWORD"] ?? throw new InvalidOperationException("APP_PASSWORD missing");
if (password.Length < 12 || password.StartsWith("replace-"))
{
    throw new InvalidOperationException("Set a real APP_PASSWORD of at least 12 characters");
}

var media = builder.Configuration["MEDIA_ROOT"] ?? "./media";
Directory.CreateDirectory(media);
var keys = builder.Configuration["KEYS_ROOT"] ?? "./keys";
Directory.CreateDirectory(keys);
builder.Services.AddDataProtection().PersistKeysToFileSystem(new DirectoryInfo(keys));
builder.Services.AddSingleton(NpgsqlDataSource.Create(builder.Configuration.GetConnectionString("Db") ?? throw new InvalidOperationException("Db missing")));
builder.Services.AddSingleton<Store>();
builder.Services.AddHttpClient("vision", c => { c.BaseAddress = new Uri(builder.Configuration["VISION_URL"] ?? "http://localhost:8000"); c.Timeout = TimeSpan.FromMinutes(5); });
builder.Services.Configure<FormOptions>(x => x.MultipartBodyLengthLimit = 28 * 1024 * 1024);
builder.WebHost.ConfigureKestrel(x => x.Limits.MaxRequestBodySize = 28 * 1024 * 1024);
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme).AddCookie(o =>
{
    o.Cookie.Name = "coaster_session";
    o.Cookie.HttpOnly = true;
    o.Cookie.SameSite = SameSiteMode.Strict;
    o.Cookie.SecurePolicy = builder.Configuration["SECURE_COOKIE"] == "true" ? CookieSecurePolicy.Always : CookieSecurePolicy.SameAsRequest;
    o.ExpireTimeSpan = TimeSpan.FromDays(7);
    o.SlidingExpiration = true;
    o.Events.OnRedirectToLogin = c => { c.Response.StatusCode = 401; return Task.CompletedTask; };
    o.Events.OnRedirectToAccessDenied = c => { c.Response.StatusCode = 403; return Task.CompletedTask; };
});
builder.Services.AddAuthorization();
builder.Services.AddRateLimiter(o =>
{
    o.RejectionStatusCode = 429;
    o.AddFixedWindowLimiter("login", x => { x.PermitLimit = 10; x.Window = TimeSpan.FromMinutes(1); x.QueueLimit = 0; });
    o.AddConcurrencyLimiter("vision", x => { x.PermitLimit = 1; x.QueueLimit = 2; x.QueueProcessingOrder = QueueProcessingOrder.OldestFirst; });
});
builder.Services.AddHostedService<MediaCleanup>();
var app = builder.Build();
await app.Services.GetRequiredService<Store>().Init();
app.Use(async (ctx, next) =>
{
    ctx.Response.Headers["X-Content-Type-Options"] = "nosniff";
    if (ctx.Request.Method is not ("GET" or "HEAD" or "OPTIONS") && ctx.Request.Headers["X-Coaster-Request"] != "1")
    { ctx.Response.StatusCode = 403; await ctx.Response.WriteAsJsonAsync(new { error = "Richiesta non consentita." }); return; }
    try
    { await next(); }
    catch (ArgumentException ex) { ctx.Response.StatusCode = 400; await ctx.Response.WriteAsJsonAsync(new { error = ex.Message }); }
    catch (BadHttpRequestException) { ctx.Response.StatusCode = 400; await ctx.Response.WriteAsJsonAsync(new { error = "Richiesta non valida o foto troppo grandi." }); }
    catch (Exception ex) { app.Logger.LogError(ex, "Request failed"); ctx.Response.StatusCode = 503; await ctx.Response.WriteAsJsonAsync(new { error = "Operazione non riuscita. Controlla che i servizi siano pronti e riprova." }); }
});
app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();
app.MapGet("/api/health", () => Results.Ok(new { status = "ok" }));
app.MapPost("/api/login", async (LoginRequest input, HttpContext ctx) =>
{
    var valid = CryptographicOperations.FixedTimeEquals(SHA256.HashData(Encoding.UTF8.GetBytes(input.Password ?? "")), SHA256.HashData(Encoding.UTF8.GetBytes(password)));
    if (!valid)
    {
        return Results.Json(new { error = "Password non corretta." }, statusCode: 401);
    }

    await ctx.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.Name, "collector") }, CookieAuthenticationDefaults.AuthenticationScheme)), new AuthenticationProperties { IsPersistent = true });
    return Results.Ok(new { ok = true });
}).RequireRateLimiting("login");
var api = app.MapGroup("/api").RequireAuthorization();
api.MapGet("/me", () => Results.Ok(new { authenticated = true }));
api.MapPost("/logout", async (HttpContext ctx) => { await ctx.SignOutAsync(); return Results.NoContent(); });
api.MapGet("/coasters", async (string? q, Store store) => Results.Ok(await store.List(q)));
api.MapGet("/export", async (Store store) => Results.File(System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(await store.List(), new System.Text.Json.JsonSerializerOptions { WriteIndented = true, PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase }), "application/json", "coaster-catalog.json"));
api.MapGet("/coasters/{id:guid}/{side}", async (Guid id, string side, Store store) => await Image(id, side, false, store));
api.MapGet("/scans/{id:guid}/{side}", async (Guid id, string side, Store store) => await Image(id, side, true, store));
async Task<IResult> Image(Guid id, string side, bool scan, Store store)
{
    var relative = await store.ImagePath(id, side, scan);
    if (relative is null)
    {
        return Results.NotFound();
    }

    var path = Path.Combine(media, relative);
    return File.Exists(path) ? Results.File(path, "image/jpeg") : Results.NotFound();
}
api.MapPost("/search", async (HttpRequest req, Store store, IHttpClientFactory clients) =>
{
    var form = await req.ReadFormAsync();
    var front = form.Files.GetFile("front");
    var back = form.Files.GetFile("back");
    if (front is null || back is null)
    {
        throw new ArgumentException("Servono entrambe le foto, fronte e retro.");
    }

    async Task<Analysis> Analyze(IFormFile file)
    {
        if (file.Length == 0 || file.Length > 12 * 1024 * 1024)
        {
            throw new ArgumentException("Ogni foto deve essere tra 1 byte e 12 MB.");
        }

        using var content = new MultipartFormDataContent();
        await using var stream = file.OpenReadStream();
        content.Add(new StreamContent(stream), "file", "photo");
        using var response = await clients.CreateClient("vision").PostAsync("/analyze", content);
        if (response.StatusCode == HttpStatusCode.BadRequest)
        {
            throw new ArgumentException("Foto non leggibile. Usa JPEG, PNG o WebP; ritaglia il sottobicchiere e riprova.");
        }

        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<Analysis>() ?? throw new InvalidOperationException("Empty analysis");
    }
    var a = await Analyze(front);
    var b = await Analyze(back);
    if (a.ModelVersion != b.ModelVersion)
    {
        throw new InvalidOperationException("Model mismatch");
    }

    var id = Guid.NewGuid();
    var dir = id.ToString("N");
    Directory.CreateDirectory(Path.Combine(media, dir));
    var fp = $"{dir}/front.jpg";
    var bp = $"{dir}/back.jpg";
    await File.WriteAllBytesAsync(Path.Combine(media, fp), Convert.FromBase64String(a.Jpeg));
    await File.WriteAllBytesAsync(Path.Combine(media, bp), Convert.FromBase64String(b.Jpeg));
    var scan = new ScanDto(id, fp, bp, Store.Vector(a.Embedding), Store.Vector(b.Embedding), a.Blank, b.Blank, a.ModelVersion);
    await store.AddScan(scan);
    var rows = await store.Search(scan);
    var details = new Dictionary<string, double?>();
    var warnings = a.Warnings.Select(x => "Fronte: " + x).Concat(b.Warnings.Select(x => "Retro: " + x)).ToList();
    if (rows.Count > 0)
    {
        try
        {
            using var verification = await clients.CreateClient("vision").PostAsJsonAsync("/verify", new VerifyRequest(fp, bp, rows.Select(x => new VerifyItem(x.Id.ToString(), x.Front, x.Back, x.Swapped)).ToList()));
            verification.EnsureSuccessStatusCode();
            details = (await verification.Content.ReadFromJsonAsync<List<VerifyResult>>() ?? new()).ToDictionary(x => x.Id, x => x.DetailScore);
        }
        catch (Exception ex) { app.Logger.LogWarning(ex, "Detail comparison unavailable"); warnings.Add("Confronto dei dettagli non disponibile: risultati basati sulla sola somiglianza visiva."); }
    }
    var catalog = (await store.List()).ToDictionary(x => x.Id);
    var ranked = rows.Where(x => catalog.ContainsKey(x.Id)).Select(x => new RankedCandidate(catalog[x.Id], x.Score, x.Swapped, details.GetValueOrDefault(x.Id.ToString())))
        .OrderByDescending(x => x.VisualScore + 0.12 * (x.DetailScore ?? 0)).ToList();
    return Results.Ok(new
    {
        scanId = id,
        frontUrl = $"/api/scans/{id}/front",
        backUrl = $"/api/scans/{id}/back",
        candidates = ranked,
        warnings,
        message = rows.Count == 0 ? "Nessun elemento compatibile nel catalogo. Puoi registrare questo sottobicchiere." : "Confronta entrambe le facce: i risultati sono candidati, non conferme automatiche."
    });
}).RequireRateLimiting("vision");
api.MapPost("/coasters", async (CreateInput input, Store store) => { Validate(input.Coaster); ValidateCopy(input.Copy); return Results.Ok(new { id = await store.Create(input) }); });
api.MapPut("/coasters/{id:guid}", async (Guid id, CoasterInput input, Store store) => { Validate(input); return await store.Update(id, input) ? Results.NoContent() : Results.NotFound(); });
api.MapDelete("/coasters/{id:guid}", async (Guid id, Store store) => await store.Delete(id) ? Results.NoContent() : Results.NotFound());
api.MapPost("/coasters/{id:guid}/copies", async (Guid id, CopyInput input, Store store) =>
{
    ValidateCopy(input);
    if (await store.ImagePath(id, "front", false) is null)
    {
        return Results.NotFound();
    }

    return Results.Ok(new { id = await store.AddCopy(id, input) });
});
api.MapPut("/coasters/{id:guid}/copies/{copyId:guid}", async (Guid id, Guid copyId, CopyInput input, Store store) => { ValidateCopy(input); return await store.UpdateCopy(id, copyId, input) ? Results.NoContent() : Results.NotFound(); });
api.MapDelete("/coasters/{id:guid}/copies/{copyId:guid}", async (Guid id, Guid copyId, Store store) => await store.DeleteCopy(id, copyId) ? Results.NoContent() : Results.NotFound());
static void Validate(CoasterInput? x)
{
    if (x is null || string.IsNullOrWhiteSpace(x.Title))
    {
        throw new ArgumentException("Inserisci un titolo.");
    }

    if (new[] { x.Title, x.Brewery, x.Country, x.Shape, x.Dimensions }.Any(s => s?.Length > 200) || x.Notes?.Length > 5000)
    {
        throw new ArgumentException("Testo troppo lungo (campi 200 caratteri, note 5000).");
    }
}
static void ValidateCopy(CopyInput? x)
{
    if (x is null || new[] { x.Binder, x.Page, x.Position, x.Condition }.Any(s => s?.Length > 200))
    {
        throw new ArgumentException("Dati esemplare non validi (massimo 200 caratteri per campo).");
    }
}
app.Run();

sealed class MediaCleanup(Store store, IConfiguration config, ILogger<MediaCleanup> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromHours(1));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                var live = await store.LivePaths();
                var root = config["MEDIA_ROOT"] ?? "./media";
                foreach (var directory in Directory.EnumerateDirectories(root))
                {
                    // Never touch recent uploads; committed records and active scans share this root.
                    if (Directory.GetCreationTimeUtc(directory) > DateTime.UtcNow.AddHours(-25))
                    {
                        continue;
                    }

                    var files = Directory.GetFiles(directory);
                    if (files.All(f => !live.Contains(Path.GetRelativePath(root, f).Replace('\\', '/'))))
                    {
                        Directory.Delete(directory, true);
                    }
                }
            }
            catch (Exception ex) { logger.LogWarning(ex, "Media cleanup failed"); }
        }
    }
}
