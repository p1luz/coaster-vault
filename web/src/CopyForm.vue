<script setup>
defineProps({model:Object})
</script>
<template><div class="form-grid copies-form">
<label>Faldone<input v-model="model.binder" maxlength="200" placeholder="Es. 3"></label><label>Pagina<input v-model="model.page" maxlength="200" placeholder="Es. 12"></label>
<label>Posizione<input v-model="model.position" maxlength="200" placeholder="Es. 4 / alto a destra"></label><label>Conservazione<input v-model="model.condition" maxlength="200" placeholder="Es. Ottimo"></label>
<label>Data acquisizione<input v-model="model.acquiredOn" type="date"></label>
</div></template>
