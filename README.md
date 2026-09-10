# Coaster Vault

Catalogo personale di sottobicchieri della birra: **Vue 3 + ASP.NET Core 10 + PostgreSQL 17/pgvector + DINOv2**, con Docker Compose. Interfaccia in italiano, utilizzabile da PC e telefono. Progettato come prima versione per una collezione di circa 1.000 varianti.

## Avvio rapido su Windows

1. Installa e avvia **Docker Desktop**, in modalità **container Linux**, con Compose v2. Non servono .NET, Python, Node o PostgreSQL installati sul PC.
2. Estrai l'intero ZIP in una directory, preferibilmente fuori da OneDrive, per esempio `C:\Progetti\coaster-vault`.
3. Apri PowerShell nella directory che contiene `compose.yaml` ed esegui:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\start.ps1
```

Lo script genera `.env` solo se non esiste, stampa la password iniziale e avvia i servizi. Il bypass vale per quel processo; in un ambiente aziendale segui le policy IT.

4. Apri **http://localhost:8080**. La password è il valore `APP_PASSWORD` nel file `.env`.

Il primo avvio scarica immagini, dipendenze e pesi del modello AI; può richiedere diversi minuti. Il modello viene conservato in un volume e riutilizzato. È necessario Internet durante la prima installazione; non servono API key né servizi AI a pagamento. Le foto vengono elaborate sul tuo server.

## Linux / macOS

```bash
bash scripts/start.sh
```

Per avvii successivi:

```bash
docker compose up -d
```

Per fermare senza cancellare i dati:

```bash
docker compose down
```

**Non usare `docker compose down -v` sulla tua collezione:** elimina i volumi dei dati.

## Dal telefono sulla stessa rete

Nel file `.env`, imposta:

```dotenv
BIND_ADDRESS=0.0.0.0
APP_PORT=8080
```

Poi esegui `docker compose up -d` e apri `http://IP-DEL-PC:8080` dal telefono. Su Windows `ipconfig` mostra l'indirizzo IPv4. Se necessario consenti la porta 8080 nel firewall della rete privata. Il PC/server deve restare acceso.

L'acquisizione usa un normale selettore foto con `capture=environment`: il comportamento (fotocamera o galleria) dipende dal telefono/browser. Funziona anche senza anteprima video dal vivo. Per installazione PWA e accesso da fuori rete usa **HTTPS**; per accesso remoto personale è consigliabile una VPN. Non pubblicare direttamente la porta HTTP su Internet. Imposta `SECURE_COOKIE=true` quando il sito è servito esclusivamente via HTTPS. Manifest e shell sono inclusi, ma il riconoscimento e il catalogo richiedono connessione al server: **nessuna modalità offline**.

## Primo utilizzo

1. Fotografa un sottobicchiere **fuori dalla busta di plastica**, dall'alto, con luce diffusa e sfondo uniforme.
2. Carica fronte e retro. Se il retro è bianco, fotografalo comunque.
3. Trascina sulla foto per selezionare un rettangolo e premi **Applica ritaglio**. Mantieni visibile tutto il bordo. Puoi ruotare a passi di 90°.
4. Premi **Cerca nella collezione**. Un catalogo vuoto non restituisce candidati.
5. Controlla i candidati e ingrandisci entrambe le facce. L'ordine fronte/retro è gestito automaticamente.
6. Se è nuovo, premi **Aggiungi al catalogo**, inserisci almeno il titolo e salva la posizione. Faldone e pagina rimangono predisposti per il prossimo inserimento.
7. Se è già presente, apri la scheda e consulta la posizione. Usa **+ Esemplare** solo per un'altra copia fisica: non aggiungere un doppione quando stai semplicemente riconoscendo il pezzo già archiviato.

La scheda permette modifica dei metadati e delle posizioni, gestione dei doppioni ed eliminazione esplicita. La collezione offre filtro locale e paginazione. L'esportazione JSON contiene **metadati**, non foto o vettori, e non sostituisce un backup.

## Riconoscimento implementato

- Decodifica JPEG/PNG/WebP, orientamento EXIF, ridimensionamento e controllo basilare di qualità.
- Ritaglio manuale nell'interfaccia, con anteprima. **Non è implementata una segmentazione automatica o una correzione prospettica automatica.**
- Embedding **DINOv2-small**, 384 dimensioni. Quattro rotazioni a 90°, media normalizzata; stesso preprocessing per catalogo e ricerca.
- PostgreSQL/pgvector confronta esattamente l'intero catalogo compatibile con la versione del modello, valutando entrambi gli abbinamenti delle facce.
- Il lato peggiore pesa maggiormente per ridurre i falsi duplicati con retro diverso; le facce uniformi sono trattate come poco informative. L'identificazione automatica di facce uniformi è euristica e può sbagliare.
- Sui primi 8 candidati: verifica ORB + RANSAC dei dettagli locali e della loro distribuzione nell'immagine. Può non essere valutabile se la grafica è troppo uniforme.
- Riordinamento con un contributo limitato della verifica geometrica.

**I punteggi non sono probabilità. Non viene dichiarato automaticamente “già presente” o “nuovo”.** Per una collezione personale il confronto umano dei candidati è il controllo finale. Il sistema trova sempre i più vicini se il catalogo contiene elementi compatibili, anche se il sottobicchiere fotografato è nuovo. Piccole scritte, dimensioni, usura, riflessi e varianti quasi identiche richiedono verifica. L'OCR non è incluso in questa versione.

Il codice include un'identità completa del modello/preprocessing. Se cambi modello, revision o preprocessing, i vecchi vettori vanno rigenerati: non modificarli direttamente senza una procedura di reindicizzazione. Il cambio modello e la migrazione automatica dei vettori non fanno parte della prima versione.

## Dati e struttura

```text
api/       API C#, accesso PostgreSQL, autenticazione e schema SQL
web/       Vue 3, componenti foto/catalogo, Nginx
vision/    FastAPI, DINOv2, verifica geometrica e test
scripts/   avvio, backup e test d'integrazione
docs/      architettura, ripristino e verifiche
```

- `coasters`: una variante collezionistica, due foto, due vettori, metadati e versione del modello.
- `copies`: gli esemplari posseduti e la posizione fisica.
- `scans`: ricerche temporanee, riutilizzabili per l'inserimento entro 24 ore. L'inserimento consuma la scansione e previene doppi invii.
- Foto: JPEG normalizzati, massimo 1.400 pixel sul lato lungo. **Non si conserva il file originale a piena risoluzione del telefono**: conserva gli originali altrove se ti servono come archivio fotografico.
- Pulizia ogni ora delle scansioni scadute e delle cartelle non più referenziate con più di 25 ore. Le foto delle schede archiviate restano conservate.

Volumi Docker: `postgres` (database), `media` (foto), `models` (pesi), `keys` (cookie). Nessuna porta del DB o del servizio visivo è pubblicata. Per default l'app è accessibile solo dal computer locale.

## Risorse e operatività

Configurazione iniziale suggerita: CPU x86-64 recente, **8 GB di RAM disponibili per Docker**, alcuni GB liberi per immagini, cache e foto. È una stima prudenziale, non un requisito misurato. Non serve una GPU; il tempo di ricerca dipende dalla CPU. La combinazione indicata è destinata anzitutto a Docker Linux x86-64; ARM non è stato validato.

La ricerca viene serializzata per limitare il carico del modello (massimo 2 richieste in attesa). Upload fino a 12 MB per faccia, JPEG/PNG/WebP; HEIC non supportato direttamente. Il frontend converte i formati decodificabili dal browser in JPEG. Le foto devono avere almeno 100 pixel per lato, massimo 30 megapixel.

```bash
docker compose ps
docker compose logs --tail 100 vision
docker compose logs --tail 100 api
docker compose logs -f vision
```

Se il modello non viene scaricato, verifica l'accesso a Hugging Face e ai relativi CDN dal tuo Docker/proxy. Poi riavvia `docker compose restart vision`. Se l'avvio supera il timeout dello script, controlla i log e ripeti `docker compose up -d --wait --wait-timeout 1800`.

Se vedi un errore 502 subito dopo l'avvio, attendi l'inizializzazione dell'API e aggiorna la pagina. Se persiste, controlla i log API. Un errore 429 indica troppe richieste o tentativi di login; attendi e riprova.

Le versioni dirette delle dipendenze sono fissate e `package-lock.json` è incluso. I tag delle immagini di runtime seguono la linea indicata, non sono digest immutabili. Prevedi aggiornamenti e verifica delle dipendenze prima di un'esposizione pubblica.

## Backup

Windows:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\backup.ps1
```

Linux/macOS:

```bash
bash scripts/backup.sh
```

Gli script fermano temporaneamente l'API e salvano un dump PostgreSQL, foto e configurazione in `backups/backup-DATA-ORA`. Il backup contiene anche password. Copialo su un altro dispositivo. Ripristino: [docs/RESTORE.md](docs/RESTORE.md).

## Sviluppo e test

Frontend: `cd web`, `npm ci`, `npm run dev` (il proxy di sviluppo punta a un'API locale sulla porta 8080). Il browser non comunica direttamente con il DB né con il servizio visivo.

API: SDK .NET 10, `dotnet build api/CoasterVault.Api.csproj`. Per eseguirla localmente configura `ConnectionStrings__Db`, `APP_PASSWORD`, `VISION_URL`, `MEDIA_ROOT` assoluto e `KEYS_ROOT` assoluto. Servono PostgreSQL/pgvector e il servizio visivo. La configurazione Docker rimane il percorso consigliato.

Test imaging (senza scaricare il modello):

```bash
pip install -r vision/requirements.txt pytest==8.3.5
PYTHONPATH=vision python -m pytest vision/tests -q
```

Test d'integrazione su un'istanza di prova avviata:

```bash
pip install requests Pillow
python scripts/smoke-test.py
```

Il test crea ed elimina due varianti sintetiche: verifica il percorso applicativo, non l'accuratezza su una collezione reale. Stato delle verifiche eseguite: [docs/VERIFICATION.md](docs/VERIFICATION.md).

## Fonti tecniche

- https://github.com/pgvector/pgvector
- https://www.npgsql.org/
- https://huggingface.co/facebook/dinov2-small
- https://huggingface.co/docs/transformers/model_doc/dinov2
- https://docs.opencv.org/4.x/d1/de0/tutorial_py_feature_homography.html
- https://vuejs.org/

Il progetto non include pesi del modello, immagini Docker o dipendenze nello ZIP; vengono scaricati al primo avvio. Le dipendenze e il modello mantengono le rispettive licenze. Vedi [THIRD_PARTY.md](THIRD_PARTY.md).
