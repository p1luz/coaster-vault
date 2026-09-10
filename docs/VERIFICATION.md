# Verifiche della consegna

## Eseguite

- Compilazione e pubblicazione Release dell'API con SDK .NET 10: nessun errore o warning.
- Installazione con `npm ci` e build di produzione Vue/Vite completate; parsing/compilazione dei quattro componenti Vue riusciti.
- 5 test automatici Python: immagini uniformi e limiti, alpha/ridimensionamento, protezione percorsi, corrispondenza geometrica di immagini ruotate contro una non correlata, aggregazione delle due facce.
- Caricamento effettivo dei pesi DINOv2-small alla revisione fissata e inferenza del servizio tramite FastAPI TestClient: vettori finiti, 384 dimensioni, norma unitaria; immagine sintetica ruotata identificata come più simile rispetto a un'immagine non correlata. Verificati gli endpoint di confronto, rifiuto upload invalido e percorso non consentito.
- Schema e query effettive eseguiti su PostgreSQL WebAssembly **PGlite con pgvector**: bootstrap ripetibile, inserimento da scansione con `FOR UPDATE`, consumo scansione, corrispondenza diretta/invertita, retro diverso, isolamento della versione del modello e cancellazione in cascata degli esemplari.
- Sintassi degli script bash e compilazione sintattica Python verificate.

## Non eseguite qui

- Avvio integrale tramite Docker Compose, rete fra container e backup/ripristino reali: Docker non è disponibile nell'ambiente di preparazione.
- Integrazione runtime completa .NET/Npgsql/PostgreSQL nativo. La verifica SQL PGlite e la compilazione C# coprono parti diverse, non sostituiscono questo test.
- Prova interattiva su browser e telefono reali; il browser di test non è stato disponibile. La build frontend non costituisce collaudo dell'interazione o della fotocamera.
- Accuratezza sui sottobicchieri reali, carico con 1.000 schede, tempi sul PC dell'utente, ARM e procedura PowerShell eseguita su Windows.

Lo script `scripts/smoke-test.py` permette la verifica integrata dopo l'avvio su un'istanza di prova. Non considerare i risultati sintetici una misura di accuratezza sul catalogo.
