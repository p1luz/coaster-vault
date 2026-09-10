# Architettura e decisioni

## Confini dei servizi

- Il browser comunica esclusivamente con Nginx sullo stesso origin.
- Nginx serve Vue e inoltra `/api/*` all'API .NET.
- Solo .NET legge/scrive il database e i metadati del catalogo.
- .NET inoltra le foto al servizio visivo interno. Python non ha credenziali PostgreSQL.
- Python legge il volume foto in sola lettura per verificare i candidati; i percorsi sono controllati contro traversal e provengono dal catalogo.

## Flusso di ricerca

1. Il browser decodifica le due foto, consente ritaglio e rotazione, ridimensiona a massimo 1.600 px ed esporta JPEG.
2. Il backend valida dimensioni dell'upload; Python decodifica, verifica formato/dimensioni e normalizza a massimo 1.400 px.
3. Il modello riceve l'intera immagine con padding a 224 × 224, senza tagliare il bordo. I vettori delle quattro rotazioni sono normalizzati, mediati e normalizzati di nuovo.
4. .NET salva i JPEG normalizzati e una scansione temporanea nel database.
5. pgvector calcola quattro distanze coseno per scheda, usando entrambe le possibili associazioni delle facce.
6. Per un abbinamento con due facce informative, il punteggio è `0.65 × min(sim1,sim2) + 0.35 × max(sim1,sim2)`. Un lato debole non è completamente compensato da uno forte.
7. Una coppia di facce entrambe uniformi non contribuisce. Una discrepanza uniforme/non uniforme sottrae 0.2. Entrambe le facce uniformi danno punteggio neutro 0 prima di eventuali penalità.
8. Per ciascun candidato si sceglie l'associazione con punteggio più alto; sui primi 8 si esegue verifica ORB/RANSAC.
9. L'ordine finale usa `punteggio_visivo + 0.12 × evidenza_geometrica` (0 se non valutabile). Il frontend mostra separatamente gli indici.

I pesi sono scelte iniziali, **non calibrate su sottobicchieri reali**. ORB verifica punti locali coerenti con una trasformazione prospettica; non produce una correzione prospettica della foto archiviata. Nessuna decisione automatica di duplicato, nessuna percentuale di confidenza.

## Catalogazione e coerenza

La creazione della scheda, del primo esemplare e il consumo della scansione avvengono nella stessa transazione SQL. La scansione viene bloccata e consumata per evitare riutilizzi. Le foto sono già state scritte prima della transazione: in caso di errore, i file non referenziati vengono rimossi dalla pulizia differita.

La variante e gli esemplari fisici sono separati. Rimuovere un esemplare non rimuove la variante; rimuovere la variante elimina in cascata tutti gli esemplari. Una variante senza esemplari rimane consultabile e viene indicata come non posseduta nelle schede.

`schema.sql` è un bootstrap idempotente: non costituisce un sistema di migrazioni per futuri cambi di schema. Prima di modificare lo schema su dati reali, introdurre migrazioni versionate e backup.

## Autenticazione e distribuzione

Un unico collezionista, password condivisa configurata fuori dai sorgenti, cookie HttpOnly SameSite Strict. Nessuna registrazione, recupero password via email o gestione multiutente. Le operazioni di scrittura richiedono un header personalizzato, senza CORS cross-origin, per impedire form POST da altri siti. Login limitato a 10 tentativi/minuto sull'istanza, inferenza a 1 richiesta concorrente + 2 in coda.

Il file `.env` e il backup sono sensibili. Per ruotare la password cambia `APP_PASSWORD` e ricrea l'API (`docker compose up -d --force-recreate api`). I cookie esistenti restano validi fino alla scadenza; per revocare tutte le sessioni occorre rimuovere le chiavi nel volume `keys` con API ferma e poi riavviare. Non confondere questo volume con quello dei dati.

Le immagini Docker di .NET/Python usano root per consentire l'inizializzazione immediata dei volumi: per un deployment pubblico strutturato aggiungere UID/GID dedicati, gestione segreti, TLS, monitoraggio e aggiornamenti. La configurazione consegnata è orientata all'uso personale locale/LAN.

## Validazione sulla collezione

Inizia con 100–200 varianti; includi coppie quasi identiche, stesso fronte con retro diverso, forme e dimensioni diverse e retro bianco. Scatta nuove foto indipendenti anche con rotazioni non multiple di 90°, luce diversa e moderata prospettiva. Conserva un gruppo di esemplari assenti dal catalogo.

Misura quante volte la variante corretta compare nei primi 5/8 risultati, il tempo di risposta e i casi in cui una variante nuova sembra un duplicato. Separa le foto usate per scegliere i pesi da quelle usate per misurare l'esito. Solo dopo questa verifica considera soglie automatiche o OCR.
