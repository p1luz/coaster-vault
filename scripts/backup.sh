#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."
backup_name="backup-$(date +%Y%m%d-%H%M%S)"
mkdir -p "backups/$backup_name"
backup_dir="$(pwd)/backups/$backup_name"
# Quiesce writes to keep SQL and photos from the same point in time.
docker compose stop api
trap 'docker compose start api >/dev/null' EXIT
docker compose exec -T db pg_dump -U coasters -d coasters -Fc > "$backup_dir/catalog.dump"
docker compose run --rm --no-deps -v "$backup_dir:/backup" --entrypoint sh api -c 'tar -czf /backup/media.tar.gz -C /media .'
cp .env "$backup_dir/config.env"
printf 'Backup completato: %s\nContiene password: conservarlo in un luogo protetto.\n' "$backup_dir"
