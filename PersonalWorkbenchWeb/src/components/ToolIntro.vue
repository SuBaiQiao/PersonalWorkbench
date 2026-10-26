<script setup>
defineProps({ title: String, eyebrow: String, description: String });
</script>
<template><header class="page-intro"><p class="eyebrow">{{ eyebrow }}</p><h1>{{ title }}</h1><p class="intro-copy">{{ description }}</p></header></template>
