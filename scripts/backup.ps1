$ErrorActionPreference = 'Stop'
Set-Location (Join-Path $PSScriptRoot '..')
$name = 'backup-' + (Get-Date -Format 'yyyyMMdd-HHmmss')
$backup = Join-Path (Get-Location) "backups/$name"
New-Item -ItemType Directory -Path $backup -Force | Out-Null
docker compose stop api
if ($LASTEXITCODE -ne 0) { throw 'Impossibile fermare API per il backup.' }
try {
    # Write the binary dump inside the container: PowerShell 5 redirection corrupts binary streams.
    docker compose exec -T db pg_dump -U coasters -d coasters -Fc -f /tmp/coaster-backup.dump
    if ($LASTEXITCODE -ne 0) { throw 'pg_dump fallito.' }
    docker compose cp db:/tmp/coaster-backup.dump "$backup/catalog.dump"
    if ($LASTEXITCODE -ne 0) { throw 'Copia dump fallita.' }
    docker compose exec -T db rm /tmp/coaster-backup.dump
    docker compose run --rm --no-deps -v "${backup}:/backup" --entrypoint sh api -c 'tar -czf /backup/media.tar.gz -C /media .'
    if ($LASTEXITCODE -ne 0) { throw 'Backup foto fallito.' }
    Copy-Item '.env' "$backup/config.env"
    Write-Host "Backup completato: $backup" -ForegroundColor Green
    Write-Host 'Contiene password: conservarlo in un luogo protetto.'
} finally {
    docker compose start api
}
