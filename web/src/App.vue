<script setup>
import { computed, onMounted, ref } from 'vue'
import PhotoInput from './PhotoInput.vue'
import MetadataForm from './MetadataForm.vue'
import CopyForm from './CopyForm.vue'
const logged=ref(false), checking=ref(true), password=ref(''), busy=ref(false), error=ref(''), notice=ref(''), view=ref('search')
const catalog=ref([]), query=ref(''), page=ref(1), front=ref(null), back=ref(null), scan=ref(null), photoKey=ref(0), showNew=ref(false)
const detail=ref(null), editing=ref(false), addingCopy=ref(false), editingCopyId=ref(null), zoom=ref(null)
const emptyMetadata=()=>({title:'',brewery:'',country:'',shape:'',dimensions:'',notes:''})
const emptyCopy=()=>({binder:'',page:'',position:'',condition:'',acquiredOn:null})
const metadata=ref(emptyMetadata()),copy=ref(emptyCopy()),edited=ref(emptyMetadata()),copyDraft=ref(emptyCopy())
const filtered=computed(()=>{const q=query.value.trim().toLocaleLowerCase('it');return catalog.value.filter(c=>!q||[c.title,c.brewery,c.country,c.notes,...c.copies.map(x=>x.binder)].join(' ').toLocaleLowerCase('it').includes(q))})
const pages=computed(()=>Math.max(1,Math.ceil(filtered.value.length/24)))
const visible=computed(()=>filtered.value.slice((page.value-1)*24,page.value*24))
const totalCopies=computed(()=>catalog.value.reduce((n,c)=>n+c.copies.length,0))
const binders=computed(()=>new Set(catalog.value.flatMap(c=>c.copies.map(x=>x.binder.trim()).filter(Boolean))).size)
const location=c=>[c.binder&&`Faldone ${c.binder}`,c.page&&`pag. ${c.page}`,c.position&&`pos. ${c.position}`].filter(Boolean).join(' · ')||'Posizione da assegnare'
async function api(path,options={}){
 const headers={'X-Coaster-Request':'1',...(options.headers||{})};if(options.body && !(options.body instanceof FormData))headers['Content-Type']='application/json'
 const response=await fetch('/api'+path,{...options,headers,credentials:'same-origin'})
 if(!response.ok){if(response.status===401)logged.value=false;let data={};try{data=await response.json()}catch{};throw new Error(data.error||(response.status===429?'Servizio occupato o troppi tentativi. Riprova fra poco.':`Operazione non riuscita (${response.status}).`))}
 if(response.status===204)return null;return response.json()
}
async function action(fn){if(busy.value)return;busy.value=true;error.value='';notice.value='';try{await fn()}catch(e){error.value=e.message}finally{busy.value=false}}
async function refresh(){catalog.value=await api('/coasters');if(detail.value)detail.value=catalog.value.find(c=>c.id===detail.value.id)||null;if(page.value>pages.value)page.value=pages.value}
async function login(){await action(async()=>{await api('/login',{method:'POST',body:JSON.stringify({password:password.value})});logged.value=true;password.value='';await refresh()})}
async function logout(){await action(async()=>{await api('/logout',{method:'POST'});logged.value=false;catalog.value=[];scan.value=null;detail.value=null;front.value=null;back.value=null;photoKey.value++})}
onMounted(async()=>{try{await api('/me');logged.value=true;await refresh()}catch(e){if(logged.value)error.value=e.message}finally{checking.value=false}})
function photoChanged(side,file){if(side==='front')front.value=file;else back.value=file;scan.value=null;showNew.value=false}
async function search(){await action(async()=>{const data=new FormData();data.append('front',front.value);data.append('back',back.value);scan.value=null;showNew.value=false;scan.value=await api('/search',{method:'POST',body:data})})}
function reset(){front.value=null;back.value=null;scan.value=null;showNew.value=false;photoKey.value++;metadata.value=emptyMetadata();copy.value={...emptyCopy(),binder:copy.value.binder,page:copy.value.page};error.value=''}
function newEntry(){showNew.value=true;setTimeout(()=>document.querySelector('#new-entry')?.scrollIntoView({behavior:'smooth'}),30)}
async function saveNew(){await action(async()=>{await api('/coasters',{method:'POST',body:JSON.stringify({scanId:scan.value.scanId,coaster:metadata.value,copy:{...copy.value,acquiredOn:copy.value.acquiredOn||null}})});await refresh();reset();notice.value='Sottobicchiere aggiunto. Puoi fotografare il prossimo.'})}
function open(c){detail.value=c;edited.value={...c};editing.value=false;addingCopy.value=false;editingCopyId.value=null}
async function saveMetadata(){await action(async()=>{await api(`/coasters/${detail.value.id}`,{method:'PUT',body:JSON.stringify(edited.value)});await refresh();editing.value=false;notice.value='Scheda aggiornata.'})}
function editCopy(c){copyDraft.value={...c};editingCopyId.value=c.id;addingCopy.value=true}
function addCopy(){copyDraft.value=emptyCopy();editingCopyId.value=null;addingCopy.value=true}
async function saveCopy(){await action(async()=>{const suffix=editingCopyId.value?'/'+editingCopyId.value:'';await api(`/coasters/${detail.value.id}/copies${suffix}`,{method:editingCopyId.value?'PUT':'POST',body:JSON.stringify({...copyDraft.value,acquiredOn:copyDraft.value.acquiredOn||null})});await refresh();addingCopy.value=false;notice.value='Esemplare salvato.'})}
async function removeCopy(c){if(!confirm('Eliminare questo esemplare e la sua posizione? La variante resta nel catalogo.'))return;await action(async()=>{await api(`/coasters/${detail.value.id}/copies/${c.id}`,{method:'DELETE'});await refresh()})}
async function remove(){if(!confirm('Eliminare questa variante e tutti i suoi esemplari?'))return;await action(async()=>{const id=detail.value.id;await api(`/coasters/${id}`,{method:'DELETE'});detail.value=null;scan.value=null;await refresh();notice.value='Scheda eliminata.'})}
async function exportCatalog(){await action(async()=>{const data=await api('/export');const url=URL.createObjectURL(new Blob([JSON.stringify(data,null,2)],{type:'application/json'}));const a=document.createElement('a');a.href=url;a.download='coaster-catalog.json';a.click();setTimeout(()=>URL.revokeObjectURL(url),1000)})}
</script>
<template>
<div v-if="checking" class="loading">Apertura della collezione…</div>
<main v-else-if="!logged" class="login-page"><div class="login-art"><span class="eyebrow">PICCOLI OGGETTI. GRANDI STORIE.</span><div class="brand-disc">C<span>COASTER VAULT</span></div><h1>Ogni birra,<br>un ricordo.</h1><p>Un posto per tutta la tua collezione.<br>E per il prossimo pezzo da trovare.</p></div><form class="login-form" @submit.prevent="login"><img src="/icon.svg" alt="" width="48"><h2>La tua collezione,<br>a portata di foto.</h2><p>Accedi al tuo catalogo personale.</p><label>Password<input v-model="password" type="password" autocomplete="current-password" required autofocus></label><p v-if="error" class="error" role="alert">{{error}}</p><button :disabled="busy">{{busy?'Accesso…':'Apri la collezione →'}}</button><small>La password si trova nel file .env generato al primo avvio.</small></form></main>
<div v-else class="app-shell">
<header><a href="#" class="brand" @click.prevent="view='search'"><img src="/icon.svg" alt="" width="38"><span>Coaster <b>Vault</b></span></a><nav aria-label="Navigazione"><button :class="{active:view==='search'}" @click="view='search'">Cerca da foto</button><button :class="{active:view==='catalog'}" @click="view='catalog'">La collezione <span>{{catalog.length}}</span></button></nav><button class="quiet logout" :disabled="busy" @click="logout">Esci</button></header>
<main class="main-content">
<div class="page-top"><div><span class="eyebrow">IL TUO ARCHIVIO PERSONALE</span><h1>{{view==='search'?'Questo ce l’ho già?':'La tua collezione.'}}</h1><p>{{view==='search'?'Fotografa le due facce. Ritrova il pezzo e il suo posto.':'Mille ricordi, ognuno al posto giusto.'}}</p></div><div class="stats"><div><b>{{catalog.length}}</b><span>varianti</span></div><div><b>{{totalCopies}}</b><span>esemplari</span></div><div><b>{{binders}}</b><span>faldoni</span></div></div></div>
<p v-if="error" class="error banner" role="alert">{{error}}</p><p v-if="notice" class="success banner" role="status">{{notice}}</p>
<section v-if="view==='search'">
<div class="section-line"><h2><span class="step">01</span> Le due facce</h2><button class="quiet" :disabled="busy" @click="reset">Nuova scansione</button></div>
<p class="hint intro">Appoggia il sottobicchiere fuori dalla busta, su uno sfondo uniforme. Fotografa dall’alto senza riflessi e ritaglia il bordo. L’ordine fronte/retro non conta.</p>
<div class="photo-grid"><PhotoInput :key="'f'+photoKey" label="Fronte" :disabled="busy" @change="file=>photoChanged('front',file)"/><PhotoInput :key="'b'+photoKey" label="Retro" :disabled="busy" @change="file=>photoChanged('back',file)"/></div>
<div class="search-bar"><p>Il confronto considera entrambe le facce,<br>anche quando sono invertite.</p><button class="primary big" :disabled="busy||!front||!back" @click="search">{{busy?'Operazione in corso…':'Cerca nella collezione →'}}</button></div>
<div v-if="busy" class="progress" role="status">Attendi: l’analisi delle immagini può richiedere alcuni secondi o più su CPU.</div>
<section v-if="scan" class="results"><div class="section-line"><h2><span class="step">02</span> Il confronto</h2><span class="pill">{{scan.candidates.length}} candidati</span></div><p>{{scan.message}}</p><div v-for="warning in scan.warnings" :key="warning" class="warning">{{warning}}</div>
<div class="query-strip"><strong>Le tue foto</strong><button class="image-button" @click="zoom=scan.frontUrl"><img :src="scan.frontUrl" alt="Fronte fotografato"></button><button class="image-button" @click="zoom=scan.backUrl"><img :src="scan.backUrl" alt="Retro fotografato"></button><span>Tocca una foto per ingrandirla.</span></div>
<p v-if="scan.candidates.length" class="hint">Indici di confronto, non probabilità. Anche una variante diversa può avere un punteggio elevato. Controlla scritte, bordo, retro e dimensioni.</p>
<div class="candidate-grid"><article v-for="(candidate,i) in scan.candidates" :key="candidate.coaster.id" class="candidate"><div class="candidate-top"><span class="pill">Candidato {{i+1}}</span><span v-if="candidate.swapped" class="hint">Facce invertite</span></div><div class="pair"><button class="image-button" @click="zoom=candidate.swapped?candidate.coaster.backUrl:candidate.coaster.frontUrl"><img :src="candidate.swapped?candidate.coaster.backUrl:candidate.coaster.frontUrl" alt="Faccia abbinata alla prima foto"></button><button class="image-button" @click="zoom=candidate.swapped?candidate.coaster.frontUrl:candidate.coaster.backUrl"><img :src="candidate.swapped?candidate.coaster.frontUrl:candidate.coaster.backUrl" alt="Faccia abbinata alla seconda foto"></button></div><h3>{{candidate.coaster.title}}</h3><p class="location">{{candidate.coaster.copies.length ? location(candidate.coaster.copies[0]) : 'Nessun esemplare posseduto'}}</p><p class="hint">Somiglianza: {{candidate.visualScore.toFixed(3)}} · Dettagli: {{candidate.detailScore===null?'non valutabili':candidate.detailScore.toFixed(3)}}</p><button class="secondary" @click="open(catalog.find(c=>c.id===candidate.coaster.id)||candidate.coaster)">Confronta e gestisci esemplari</button></article></div>
<div class="new-callout"><div><h3>È una nuova variante?</h3><p>Salvala nel catalogo con le foto appena scattate.</p></div><button :disabled="busy" @click="newEntry">+ Aggiungi al catalogo</button></div>
<form v-if="showNew" id="new-entry" class="panel" @submit.prevent="saveNew"><h2>Nuovo sottobicchiere</h2><MetadataForm :model="metadata"/><h3>Dove lo conservi?</h3><CopyForm :model="copy"/><div class="form-actions"><button :disabled="busy">Salva e passa al prossimo →</button><button type="button" class="quiet" @click="showNew=false">Annulla</button></div></form>
</section>
<div v-else-if="!catalog.length" class="empty-tip"><b>Il primo pezzo è l’inizio.</b><p>Fotografa fronte e retro, premi Cerca e registra il primo sottobicchiere. Il catalogo cresce a ogni inserimento.</p></div>
</section>
<section v-else>
<div class="catalog-tools"><input v-model="query" aria-label="Cerca nel catalogo" placeholder="Cerca titolo, birrificio, paese, note o faldone…" @input="page=1"><button class="secondary" :disabled="busy" @click="exportCatalog">Esporta dati JSON</button></div>
<div v-if="!filtered.length" class="empty-tip"><h2>{{catalog.length?'Nessun risultato.':'Il catalogo è ancora vuoto.'}}</h2><p>{{catalog.length?'Prova un’altra ricerca.':'Inizia dalle foto del tuo primo sottobicchiere.'}}</p><button v-if="!catalog.length" @click="view='search'">Fotografa il primo →</button></div>
<div class="catalog-grid"><button v-for="c in visible" :key="c.id" class="catalog-card" @click="open(c)"><div class="card-image"><img :src="c.frontUrl" :alt="c.title" loading="lazy"><img :src="c.backUrl" alt="Retro" class="back-mini" loading="lazy"></div><div class="card-text"><span class="eyebrow">{{c.brewery||'COLLEZIONE PERSONALE'}}</span><h3>{{c.title}}</h3><p>{{c.copies.length?location(c.copies[0]):'Nessun esemplare'}}</p><span class="hint">{{c.copies.length}} {{c.copies.length===1?'esemplare':'esemplari'}}</span></div></button></div>
<div v-if="filtered.length" class="pagination"><button class="quiet" :disabled="page<=1" @click="page--">← Precedente</button><span>Pagina {{page}} / {{pages}} · {{filtered.length}} varianti</span><button class="quiet" :disabled="page>=pages" @click="page++">Successiva →</button></div>
</section>
<footer>Coaster Vault <span>Una collezione. Tante storie.</span></footer>
</main>
<div v-if="detail" class="modal-backdrop" @click.self="!busy&&(detail=null)"><section class="modal" role="dialog" aria-modal="true" aria-labelledby="detail-title"><div class="section-line"><span class="eyebrow">SCHEDA COLLEZIONE</span><button class="quiet" :disabled="busy" aria-label="Chiudi scheda" @click="detail=null">Chiudi ✕</button></div><h2 id="detail-title">{{detail.title}}</h2><p v-if="error" class="error" role="alert">{{error}}</p><div class="pair detail-pair"><button class="image-button" @click="zoom=detail.frontUrl"><img :src="detail.frontUrl" alt="Fronte"></button><button class="image-button" @click="zoom=detail.backUrl"><img :src="detail.backUrl" alt="Retro"></button></div>
<template v-if="!editing"><p>{{[detail.brewery,detail.country,detail.shape,detail.dimensions].filter(Boolean).join(' · ')}}</p><p class="notes">{{detail.notes}}</p><button class="secondary" :disabled="busy" @click="edited={...detail};editing=true">Modifica scheda</button></template>
<form v-else @submit.prevent="saveMetadata"><MetadataForm :model="edited"/><div class="form-actions"><button :disabled="busy">Salva modifiche</button><button type="button" class="quiet" @click="editing=false">Annulla</button></div></form>
<div class="section-line copies-heading"><h3>Esemplari posseduti ({{detail.copies.length}})</h3><button class="secondary" :disabled="busy" @click="addCopy">+ Esemplare</button></div><p class="hint">Aggiungi un esemplare solo se possiedi un’altra copia fisica. Se hai ritrovato lo stesso pezzo, consulta semplicemente la posizione.</p>
<div v-for="c in detail.copies" :key="c.id" class="copy-row"><div><b>{{location(c)}}</b><p>{{[c.condition,c.acquiredOn].filter(Boolean).join(' · ')}}</p></div><div class="row-actions"><button class="quiet" :disabled="busy" @click="editCopy(c)">Modifica</button><button class="quiet danger" :disabled="busy" @click="removeCopy(c)">Rimuovi</button></div></div>
<form v-if="addingCopy" class="panel copy-edit" @submit.prevent="saveCopy"><h3>{{editingCopyId?'Modifica esemplare':'Nuovo esemplare / doppione'}}</h3><CopyForm :model="copyDraft"/><div class="form-actions"><button :disabled="busy">Salva esemplare</button><button type="button" class="quiet" @click="addingCopy=false">Annulla</button></div></form>
<div class="delete-row"><button class="quiet danger" :disabled="busy" @click="remove">Elimina variante e tutti gli esemplari</button></div></section></div>
<div v-if="zoom" class="zoom-backdrop" role="dialog" aria-modal="true" aria-label="Foto ingrandita" @click="zoom=null"><button class="close-zoom" @click="zoom=null">Chiudi ✕</button><img :src="zoom" alt="Foto ingrandita" @click.stop></div>
</div>
</template>
