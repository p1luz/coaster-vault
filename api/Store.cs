using Npgsql;
using NpgsqlTypes;
using System.Globalization;

public sealed class Store(NpgsqlDataSource db)
{
    public static string Vector(float[] values)
    {
        if (values.Length != 384 || values.Any(v => !float.IsFinite(v)))
        {
            throw new InvalidOperationException("Invalid embedding");
        }

        return "[" + string.Join(",", values.Select(v => v.ToString("R", CultureInfo.InvariantCulture))) + "]";
    }
    static void Param(NpgsqlCommand cmd, string key, object value) => cmd.Parameters.AddWithValue(key, value);
    public async Task Init()
    {
        await using var cmd = db.CreateCommand(await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "schema.sql")));
        await cmd.ExecuteNonQueryAsync();
    }
    public async Task<List<CoasterDto>> List(string? query = null)
    {
        var result = new List<CoasterDto>();
        await using (var cmd = db.CreateCommand("""
            SELECT id,title,brewery,country,shape,dimensions,notes,created_at FROM coasters
            WHERE @q = '' OR title ILIKE @like OR brewery ILIKE @like OR country ILIKE @like OR notes ILIKE @like
            ORDER BY created_at DESC,id
            """))
        {
            Param(cmd, "q", query ?? "");
            Param(cmd, "like", "%" + (query ?? "") + "%");
            await using var r = await cmd.ExecuteReaderAsync();
            while (await r.ReadAsync())
            {
                result.Add(new(r.GetGuid(0), r.GetString(1), r.GetString(2), r.GetString(3), r.GetString(4), r.GetString(5), r.GetString(6),
                $"/api/coasters/{r.GetGuid(0)}/front", $"/api/coasters/{r.GetGuid(0)}/back", r.GetDateTime(7), new()));
            }
        }
        if (result.Count == 0)
        {
            return result;
        }

        var lookup = result.ToDictionary(x => x.Id);
        await using var copies = db.CreateCommand("SELECT id,coaster_id,binder,page,position,condition,acquired_on FROM copies WHERE coaster_id=ANY(@ids) ORDER BY created_at,id");
        Param(copies, "ids", result.Select(x => x.Id).ToArray());
        await using var cr = await copies.ExecuteReaderAsync();
        while (await cr.ReadAsync())
        {
            lookup[cr.GetGuid(1)].Copies.Add(new(cr.GetGuid(0), cr.GetString(2), cr.GetString(3), cr.GetString(4), cr.GetString(5), cr.IsDBNull(6) ? null : cr.GetFieldValue<DateOnly>(6)));
        }

        return result;
    }
    public async Task AddScan(ScanDto s)
    {
        await using var cmd = db.CreateCommand("INSERT INTO scans(id,front_path,back_path,front_vector,back_vector,front_blank,back_blank,model_version) VALUES(@id,@f,@b,CAST(@fv AS vector),CAST(@bv AS vector),@fb,@bb,@m)");
        ScanParams(cmd, s);
        await cmd.ExecuteNonQueryAsync();
    }
    static void ScanParams(NpgsqlCommand cmd, ScanDto s)
    {
        Param(cmd, "id", s.Id);
        Param(cmd, "f", s.FrontPath);
        Param(cmd, "b", s.BackPath);
        Param(cmd, "fv", s.FrontVector);
        Param(cmd, "bv", s.BackVector);
        Param(cmd, "fb", s.FrontBlank);
        Param(cmd, "bb", s.BackBlank);
        Param(cmd, "m", s.ModelVersion);
    }
    public async Task<ScanDto?> Scan(Guid id)
    {
        await using var cmd = db.CreateCommand("SELECT id,front_path,back_path,front_vector::text,back_vector::text,front_blank,back_blank,model_version FROM scans WHERE id=@id AND created_at>now()-interval '24 hours'");
        Param(cmd, "id", id);
        await using var r = await cmd.ExecuteReaderAsync();
        return await r.ReadAsync() ? new(r.GetGuid(0), r.GetString(1), r.GetString(2), r.GetString(3), r.GetString(4), r.GetBoolean(5), r.GetBoolean(6), r.GetString(7)) : null;
    }
    public async Task<List<(Guid Id, double Score, bool Swapped, string Front, string Back)>> Search(ScanDto s)
    {
        // Blank backs contribute little; a blank/nonblank mismatch is explicitly penalized.
        const string sql = """
        WITH similarities AS (
          SELECT id,front_path,back_path,
            1-(front_vector <=> CAST(@fv AS vector)) AS ff, 1-(back_vector <=> CAST(@bv AS vector)) AS bb,
            1-(front_vector <=> CAST(@bv AS vector)) AS bf, 1-(back_vector <=> CAST(@fv AS vector)) AS fb,
            front_blank AS fblank, back_blank AS bblank
          FROM coasters WHERE model_version=@m
        ), scored AS (
          SELECT *,
            CASE WHEN @qf AND fblank AND @qb AND bblank THEN 0.0
                 WHEN @qf AND fblank THEN bb
                 WHEN @qb AND bblank THEN ff
                 ELSE 0.65*LEAST(ff,bb)+0.35*GREATEST(ff,bb) END
                 -CASE WHEN @qf<>fblank OR @qb<>bblank THEN 0.2 ELSE 0 END AS direct,
            CASE WHEN @qf AND bblank AND @qb AND fblank THEN 0.0
                 WHEN @qf AND bblank THEN bf
                 WHEN @qb AND fblank THEN fb
                 ELSE 0.65*LEAST(fb,bf)+0.35*GREATEST(fb,bf) END
                 -CASE WHEN @qf<>bblank OR @qb<>fblank THEN 0.2 ELSE 0 END AS swapped
          FROM similarities
        ) SELECT id,GREATEST(direct,swapped),swapped>direct,front_path,back_path FROM scored
          ORDER BY GREATEST(direct,swapped) DESC,id LIMIT 8
        """;
        await using var cmd = db.CreateCommand(sql);
        Param(cmd, "fv", s.FrontVector);
        Param(cmd, "bv", s.BackVector);
        Param(cmd, "qf", s.FrontBlank);
        Param(cmd, "qb", s.BackBlank);
        Param(cmd, "m", s.ModelVersion);
        await using var r = await cmd.ExecuteReaderAsync();
        var result = new List<(Guid, double, bool, string, string)>();
        while (await r.ReadAsync())
        {
            result.Add((r.GetGuid(0), r.GetDouble(1), r.GetBoolean(2), r.GetString(3), r.GetString(4)));
        }

        return result;
    }
    public async Task<Guid> Create(CreateInput input)
    {
        await using var conn = await db.OpenConnectionAsync();
        await using var tx = await conn.BeginTransactionAsync();
        // Lock the scan, making double-click/replayed submissions unable to create duplicate records.
        await using var cmd = new NpgsqlCommand("""
        INSERT INTO coasters(id,title,brewery,country,shape,dimensions,notes,front_path,back_path,front_vector,back_vector,front_blank,back_blank,model_version)
        SELECT @newid,@title,@brewery,@country,@shape,@dimensions,@notes,front_path,back_path,front_vector,back_vector,front_blank,back_blank,model_version
        FROM scans WHERE id=@scan AND created_at>now()-interval '24 hours' FOR UPDATE
        """, conn, tx);
        var id = Guid.NewGuid();
        Param(cmd, "newid", id);
        Param(cmd, "scan", input.ScanId);
        Metadata(cmd, input.Coaster);
        if (await cmd.ExecuteNonQueryAsync() != 1)
        {
            throw new ArgumentException("Scansione scaduta o già utilizzata: ripeti la ricerca.");
        }

        await InsertCopy(conn, tx, id, input.Copy);
        await using var del = new NpgsqlCommand("DELETE FROM scans WHERE id=@id", conn, tx);
        Param(del, "id", input.ScanId);
        await del.ExecuteNonQueryAsync();
        await tx.CommitAsync();
        return id;
    }
    static void Metadata(NpgsqlCommand cmd, CoasterInput x)
    {
        Param(cmd, "title", x.Title.Trim());
        Param(cmd, "brewery", x.Brewery ?? "");
        Param(cmd, "country", x.Country ?? "");
        Param(cmd, "shape", x.Shape ?? "");
        Param(cmd, "dimensions", x.Dimensions ?? "");
        Param(cmd, "notes", x.Notes ?? "");
    }
    public async Task<bool> Update(Guid id, CoasterInput input)
    {
        await using var cmd = db.CreateCommand("UPDATE coasters SET title=@title,brewery=@brewery,country=@country,shape=@shape,dimensions=@dimensions,notes=@notes WHERE id=@id");
        Param(cmd, "id", id);
        Metadata(cmd, input);
        return await cmd.ExecuteNonQueryAsync() == 1;
    }
    static async Task<Guid> InsertCopy(NpgsqlConnection conn, NpgsqlTransaction? tx, Guid coaster, CopyInput x)
    {
        var id = Guid.NewGuid();
        await using var cmd = new NpgsqlCommand("INSERT INTO copies(id,coaster_id,binder,page,position,condition,acquired_on) VALUES(@id,@coaster,@binder,@page,@position,@condition,@date)", conn, tx);
        Param(cmd, "id", id);
        Param(cmd, "coaster", coaster);
        Param(cmd, "binder", x.Binder ?? "");
        Param(cmd, "page", x.Page ?? "");
        Param(cmd, "position", x.Position ?? "");
        Param(cmd, "condition", x.Condition ?? "");
        cmd.Parameters.Add("date", NpgsqlDbType.Date).Value = x.AcquiredOn.HasValue ? x.AcquiredOn.Value : DBNull.Value;
        await cmd.ExecuteNonQueryAsync();
        return id;
    }
    public async Task<Guid> AddCopy(Guid coaster, CopyInput x)
    { await using var conn = await db.OpenConnectionAsync(); return await InsertCopy(conn, null, coaster, x); }
    public async Task<bool> UpdateCopy(Guid coaster, Guid id, CopyInput x)
    {
        await using var cmd = db.CreateCommand("UPDATE copies SET binder=@b,page=@p,position=@s,condition=@c,acquired_on=@d WHERE id=@id AND coaster_id=@coaster");
        Param(cmd, "id", id);
        Param(cmd, "coaster", coaster);
        Param(cmd, "b", x.Binder ?? "");
        Param(cmd, "p", x.Page ?? "");
        Param(cmd, "s", x.Position ?? "");
        Param(cmd, "c", x.Condition ?? "");
        cmd.Parameters.Add("d", NpgsqlDbType.Date).Value = x.AcquiredOn.HasValue ? x.AcquiredOn.Value : DBNull.Value;
        return await cmd.ExecuteNonQueryAsync() == 1;
    }
    public async Task<bool> DeleteCopy(Guid coaster, Guid id)
    { await using var cmd = db.CreateCommand("DELETE FROM copies WHERE id=@id AND coaster_id=@coaster"); Param(cmd, "id", id); Param(cmd, "coaster", coaster); return await cmd.ExecuteNonQueryAsync() == 1; }
    public async Task<string?> ImagePath(Guid id, string side, bool scan)
    {
        if (side is not ("front" or "back"))
        {
            return null;
        }

        var table = scan ? "scans" : "coasters";
        var column = side == "front" ? "front_path" : "back_path";
        await using var cmd = db.CreateCommand($"SELECT {column} FROM {table} WHERE id=@id");
        Param(cmd, "id", id);
        return await cmd.ExecuteScalarAsync() as string;
    }
    public async Task<bool> Delete(Guid id)
    { await using var cmd = db.CreateCommand("DELETE FROM coasters WHERE id=@id"); Param(cmd, "id", id); return await cmd.ExecuteNonQueryAsync() == 1; }
    public async Task<HashSet<string>> LivePaths()
    {
        await using var clean = db.CreateCommand("DELETE FROM scans WHERE created_at<now()-interval '24 hours'");
        await clean.ExecuteNonQueryAsync();
        await using var cmd = db.CreateCommand("SELECT front_path FROM coasters UNION SELECT back_path FROM coasters UNION SELECT front_path FROM scans UNION SELECT back_path FROM scans");
        await using var r = await cmd.ExecuteReaderAsync();
        var paths = new HashSet<string>();
        while (await r.ReadAsync())
        {
            paths.Add(r.GetString(0));
        }

        return paths;
    }
}
