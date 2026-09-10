$ErrorActionPreference = 'Stop'
Set-Location (Join-Path $PSScriptRoot '..')
if (-not (Get-Command docker -ErrorAction SilentlyContinue)) { throw 'Installa e avvia Docker Desktop con container Linux.' }
docker info *> $null
if ($LASTEXITCODE -ne 0) { throw 'Docker non risponde. Avvia Docker Desktop e riprova.' }
if (-not (Test-Path '.env')) {
    $db = [Guid]::NewGuid().ToString('N') + [Guid]::NewGuid().ToString('N')
    $password = [Guid]::NewGuid().ToString('N')
    @("DB_PASSWORD=$db", "APP_PASSWORD=$password", 'BIND_ADDRESS=127.0.0.1', 'APP_PORT=8080', 'SECURE_COOKIE=false') | Set-Content '.env' -Encoding ascii
    Write-Host "Password di accesso: $password" -ForegroundColor Yellow
    Write-Host 'Password salvata nel file .env. Conservala.'
}
Write-Host 'Primo avvio: download delle immagini Docker e del modello AI. Puo richiedere diversi minuti.'
docker compose up -d --build --wait --wait-timeout 1800
if ($LASTEXITCODE -ne 0) { throw 'Avvio non completato. Controlla: docker compose logs --tail 100' }
$port = ((Get-Content '.env' | Where-Object { $_ -match '^APP_PORT=' }) -replace '^APP_PORT=', '')
if (-not $port) { $port = '8080' }
Write-Host "Apri http://localhost:$port - password nel file .env" -ForegroundColor Green
