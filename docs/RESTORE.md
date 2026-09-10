# Ripristino su una nuova istanza

Usa una directory progetto nuova e una destinazione senza una collezione da conservare. La procedura seguente svuota e ricrea le tabelle nel database di destinazione. Non avviarla su dati importanti senza un backup separato.

1. Copia `config.env` del backup nella root del progetto con nome `.env`.
2. Avvia lo stack con `docker compose up -d --build --wait --wait-timeout 1800`.
3. Ferma l'API: `docker compose stop api`.
4. Copia il dump nel container e ripristinalo (sostituisci il percorso):

```bash
docker compose cp ./backups/backup-DATA-ORA/catalog.dump db:/tmp/restore.dump
docker compose exec -T db pg_restore -U coasters -d coasters --clean --if-exists --no-owner --exit-on-error /tmp/restore.dump
```

5. Estrai le foto. Su PowerShell:

```powershell
$backup = (Resolve-Path './backups/backup-DATA-ORA').Path
docker compose run --rm --no-deps -v "${backup}:/backup:ro" --entrypoint sh api -c 'tar -xzf /backup/media.tar.gz -C /media'
```

Su bash:

```bash
backup_dir="$(pwd)/backups/backup-DATA-ORA"
docker compose run --rm --no-deps -v "$backup_dir:/backup:ro" --entrypoint sh api -c 'tar -xzf /backup/media.tar.gz -C /media'
```

6. Esegui `docker compose start api` e accedi di nuovo. Controlla numero di schede/esemplari, alcune foto e una ricerca.
7. Rimuovi il dump temporaneo: `docker compose exec -T db rm /tmp/restore.dump`.

I pesi del modello sono riscaricabili; il backup non li contiene. Le chiavi cookie non sono incluse: su un'istanza nuova devi rifare il login. Non cambiare `DB_PASSWORD` in `.env` su un database esistente senza cambiare anche la password del ruolo PostgreSQL: la variabile inizializza solo un volume vuoto.

Se un comando fallisce, controlla il codice di uscita e risolvi l'errore prima di riavviare l'API. Conserva sempre il backup originale.
