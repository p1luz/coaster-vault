#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."
command -v docker >/dev/null || { echo 'Installa Docker Engine/Desktop con Compose v2.'; exit 1; }
docker info >/dev/null
if [ ! -f .env ]; then
  db_password=$(od -An -N32 -tx1 /dev/urandom | tr -d ' \n')
  app_password=$(od -An -N16 -tx1 /dev/urandom | tr -d ' \n')
  (umask 077; printf 'DB_PASSWORD=%s\nAPP_PASSWORD=%s\nBIND_ADDRESS=127.0.0.1\nAPP_PORT=8080\nSECURE_COOKIE=false\n' "$db_password" "$app_password" > .env)
  printf 'Password di accesso: %s\nConservala: e salvata in .env.\n' "$app_password"
fi
printf 'Primo avvio: download immagini Docker e modello AI. Attendi alcuni minuti.\n'
docker compose up -d --build --wait --wait-timeout 1800
printf 'App disponibile sulla porta APP_PORT del file .env (default http://localhost:8080).\n'
