<script setup>
defineProps({model:Object})
</script>
<template><div class="form-grid">
<label class="wide">Titolo *<input v-model="model.title" required maxlength="200" placeholder="Es. Birrificio · Edizione 2024"></label>
<label>Birrificio / marchio<input v-model="model.brewery" maxlength="200"></label><label>Paese<input v-model="model.country" maxlength="200"></label>
<label>Forma<select v-model="model.shape"><option value="">Non specificata</option><option>Rotondo</option><option>Quadrato</option><option>Rettangolare</option><option>Ovale</option><option>Sagomato</option></select></label>
<label>Dimensioni<input v-model="model.dimensions" maxlength="200" placeholder="Es. Ø 107 mm"></label>
<label class="wide">Note e differenze della variante<textarea v-model="model.notes" maxlength="5000" rows="3" placeholder="Scritte sul retro, bordo, anno, dettagli distintivi…"></textarea></label>
</div></template>
