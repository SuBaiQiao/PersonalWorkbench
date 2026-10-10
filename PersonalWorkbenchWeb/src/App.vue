<script setup>
import WorkbenchLayout from './layouts/WorkbenchLayout.vue';
import { workbenchTheme } from './styles/theme.js';
</script>

<template>
    <a-config-provider :theme="workbenchTheme">
        <WorkbenchLayout>
            <RouterView />
        </WorkbenchLayout>
    </a-config-provider>
</template>
