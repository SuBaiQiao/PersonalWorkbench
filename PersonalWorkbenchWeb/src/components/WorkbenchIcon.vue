<script setup>
import { computed } from 'vue';
const props = defineProps({ name: { type: String, default: 'grid' } });
const paths = {
    grid: 'M3 3h7v7H3z M14 3h7v7h-7z M3 14h7v7H3z M14 14h7v7h-7z',
    search: 'M20.5 20.5 16 16 M18 10.5a7.5 7.5 0 1 1-15 0 7.5 7.5 0 0 1 15 0',
    arrow: 'M6 18 18 6 M6 6h12v12',
    back: 'm10 5-7 7 7 7 M3 12h18',
    chevron: 'm7 10 5 5 5-5',
    database: 'M20 6c0 2.2-16 2.2-16 0s16-2.2 16 0 M4 6v12c0 4 16 4 16 0V6 M4 12c0 4 16 4 16 0',
    download: 'M12 3v12 m-5-5 5 5 5-5 M4 16v4h16v-4',
    upload: 'M12 16V4 m-5 5 5-5 5 5 M4 16v4h16v-4',
    sun: 'M16 12a4 4 0 1 1-8 0 4 4 0 0 1 8 0 M12 2v2 M12 20v2 M2 12h2 M20 12h2 M5 5l1.4 1.4 M17.6 17.6 19 19 M5 19l1.4-1.4 M17.6 6.4 19 5',
    moon: 'M20.8 13A9 9 0 0 1 11 3.2 9 9 0 1 0 20.8 13Z',
    sparkles: 'm12 3 2.4 6.6L21 12l-6.6 2.4L12 21l-2.4-6.6L3 12l6.6-2.4Z M20 2v4 M18 4h4',
    identity: 'M3 5h18v14H3z M9 10a2 2 0 1 1-4 0 2 2 0 0 1 4 0 M5 16c0-3 5-3 5 0 M14 9h4 M14 13h4',
    code: 'm8 7-5 5 5 5 m8-10 5 5-5 5 M14 4l-4 16',
    currency: 'm6 4 6 7 6-7 M5 11h14 M5 15h14 M12 11v10',
    files: 'M8 3h8l5 5v13H8z M16 3v5h5 M4 7H3v14h1 M12 12h5 M12 16h5',
    image: 'M3 3h18v18H3z M9 8a1.5 1.5 0 1 1-3 0 1.5 1.5 0 0 1 3 0 M3 17l5-5 4 4 4-6 5 7',
    clock: 'M21 12a9 9 0 1 1-18 0 9 9 0 0 1 18 0 M12 6v6l4 2',
    phone: 'M6 2h12v20H6z M10 18h4 M10 5h4',
    network: 'M9 3h6v6H9z M2 16h6v6H2z M16 16h6v6h-6z M12 9v4 M5 16v-3h14v3',
    edit: 'm15 4 5 5 M4 15 16 3l5 5L9 20l-6 1Z M14 21h7',
    activity: 'M2 12h5l3-9 4 18 3-9h5',
    cards: 'm8 2 13 4-5 16-13-4Z M8 7l8 2 M7 11l7 2 M6 15l5 1.5',
    lock: 'M5 10h14v12H5z M8 10V6a4 4 0 0 1 8 0v4 M12 15v3',
    qr: 'M3 3h6v6H3z M15 3h6v6h-6z M3 15h6v6H3z M15 15h3v3h3v3h-6z M12 3v3 M12 9v3H3 M15 12h6 M12 15v6',
    braces: 'M8 3H6v6l-3 3 3 3v6h2 M16 3h2v6l3 3-3 3v6h-2 M10 9h4 M10 15h4',
    diff: 'M4 3h6v18H4z M14 3h6v18h-6z M6 8h2 M16 8h2 M17 7v2 M6 15h2 M16 15h2',
    regex: 'M3 15h6 M6 12v6 M14 4v8 M10 6l8 4 M18 6l-8 4 M17 17h3 M18.5 15.5v3',
    shuffle: 'M3 6h3l12 12h3 M18 15l3 3-3 3 M3 18h3l4-4 M14 10l4-4h3 M18 3l3 3-3 3',
    crop: 'M6 2v16h16 M2 6h16v16 M10 2v4 M2 10h4 M18 14h4 M14 18v4',
    check: 'm5 12 4 4L19 6',
    close: 'm6 6 12 12 M6 18 18 6'
};
const path = computed(() => paths[props.name] || paths.grid);
</script>

<template>
    <svg class="workbench-icon" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.65" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true" focusable="false"><path :d="path" /></svg>
</template>

<style scoped>
.workbench-icon { display: inline-block; width: 1.25em; height: 1.25em; flex-shrink: 0; vertical-align: -.2em; }
</style>
