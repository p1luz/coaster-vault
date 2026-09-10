<script setup>
import { ref, nextTick } from 'vue'
const props = defineProps({label:String,disabled:Boolean})
const emit = defineEmits(['change'])
const canvas=ref(null), loaded=ref(false), cropped=ref(false), error=ref('')
let source=null, selection=null, start=null
function redraw() {
  if(!source || !canvas.value)return
  const c=canvas.value,ctx=c.getContext('2d');c.width=source.width;c.height=source.height
  ctx.drawImage(source,0,0)
  if(selection){const {x,y,w,h}=selection;ctx.fillStyle='rgba(10,25,19,.42)';ctx.fillRect(0,0,c.width,c.height);ctx.drawImage(source,x,y,w,h,x,y,w,h);ctx.strokeStyle='#efbd75';ctx.lineWidth=Math.max(c.width/180,2);ctx.strokeRect(x,y,w,h)}
}
async function publish(){const c=document.createElement('canvas');c.width=source.width;c.height=source.height;c.getContext('2d').drawImage(source,0,0);const blob=await new Promise(r=>c.toBlob(r,'image/jpeg',.92));if(blob)emit('change',new File([blob],'coaster.jpg',{type:'image/jpeg'}))}
async function choose(event){
  error.value='';const file=event.target.files?.[0];if(!file)return
  if(file.size>12*1024*1024){error.value='Massimo 12 MB per foto.';return}
  let bitmap
  try{
    bitmap=await createImageBitmap(file)
    if(bitmap.width*bitmap.height>30000000 || Math.min(bitmap.width,bitmap.height)<100)throw new Error('Dimensioni non valide (minimo 100 px, massimo 30 megapixel).')
    const scale=Math.min(1,1600/Math.max(bitmap.width,bitmap.height));source=document.createElement('canvas');source.width=Math.round(bitmap.width*scale);source.height=Math.round(bitmap.height*scale)
    const ctx=source.getContext('2d');ctx.fillStyle='white';ctx.fillRect(0,0,source.width,source.height);ctx.drawImage(bitmap,0,0,source.width,source.height)
    selection=null;loaded.value=true;cropped.value=false;await nextTick();redraw();await publish()
  }catch(e){error.value=e.message.includes('Dimensioni')?e.message:'Formato non leggibile. Scegli JPEG, PNG o WebP.'}finally{bitmap?.close();event.target.value=''}
}
function point(e){const r=canvas.value.getBoundingClientRect();return {x:Math.max(0,Math.min(canvas.value.width,(e.clientX-r.left)*canvas.value.width/r.width)),y:Math.max(0,Math.min(canvas.value.height,(e.clientY-r.top)*canvas.value.height/r.height))}}
function down(e){if(props.disabled)return;start=point(e);canvas.value.setPointerCapture(e.pointerId);selection=null;redraw()}
function move(e){if(!start)return;const p=point(e);selection={x:Math.min(start.x,p.x),y:Math.min(start.y,p.y),w:Math.abs(start.x-p.x),h:Math.abs(start.y-p.y)};redraw()}
function up(){start=null;cropped.value=!!selection&&selection.w>=100&&selection.h>=100;if(!cropped.value){selection=null;redraw()}}
async function crop(){if(!selection)return;const {x,y,w,h}=selection;const next=document.createElement('canvas');next.width=Math.round(w);next.height=Math.round(h);next.getContext('2d').drawImage(source,x,y,w,h,0,0,next.width,next.height);source=next;selection=null;cropped.value=false;redraw();await publish()}
function cancel(){selection=null;cropped.value=false;redraw()}
async function rotate(){const next=document.createElement('canvas');next.width=source.height;next.height=source.width;const ctx=next.getContext('2d');ctx.translate(next.width,0);ctx.rotate(Math.PI/2);ctx.drawImage(source,0,0);source=next;selection=null;cropped.value=false;redraw();await publish()}
</script>
<template>
  <div class="photo-input">
    <div class="photo-title"><strong>{{ label }}</strong><span>{{ loaded ? 'Foto pronta' : 'Da fotografare' }}</span></div>
    <label class="upload-zone" :class="{compact:loaded}"><span>{{ loaded ? 'Sostituisci foto' : '+ Scatta o scegli una foto' }}</span><input :disabled="disabled" type="file" accept="image/jpeg,image/png,image/webp" capture="environment" @change="choose"></label>
    <template v-if="loaded"><canvas ref="canvas" class="crop-canvas" aria-label="Trascina sulla foto per selezionare un ritaglio" @pointerdown="down" @pointermove="move" @pointerup="up" @pointercancel="up"></canvas><div class="photo-tools"><button type="button" class="quiet" :disabled="disabled" @click="rotate">↻ Ruota</button><button v-if="cropped" type="button" :disabled="disabled" @click="crop">Applica ritaglio</button><button v-if="cropped" type="button" class="quiet" @click="cancel">Annulla</button></div><p class="hint">Trascina sulla foto per ritagliare. Lascia visibile tutto il bordo.</p></template>
    <p v-if="error" class="error" role="alert">{{error}}</p>
  </div>
</template>
