<script setup>
import { onMounted, ref } from 'vue';
import { api, getErrorMessage } from '../api/client.js';

const tools = ref([]);
const loading = ref(true);
const errorMessage = ref('');

onMounted(async () => {
    try {
        const response = await api.get('/api/tools');
        tools.value = response.data;
    } catch (error) {
        errorMessage.value = await getErrorMessage(error, '工具目录加载失败。');
    } finally {
        loading.value = false;
    }
});
</script>

<template>
    <section class="home-hero" aria-labelledby="home-title">
        <div class="home-hero-copy">
            <p class="eyebrow">PERSONAL DEVELOPER WORKBENCH</p>
            <h1 id="home-title">把常用的小工具，放在同一个工作台。</h1>
            <p class="intro-copy">一个面向日常开发的轻量工具集合。需要什么，就从这里直接打开；工具目录由配置文件统一维护。</p>
        </div>
        <div class="home-signal" aria-label="工具台运行状态">
            <span class="signal-dot" aria-hidden="true"></span>
            <span>本地运行</span>
            <span class="signal-divider" aria-hidden="true"></span>
            <strong>{{ String(tools.length).padStart(2, '0') }}</strong>
            <span>项工具</span>
        </div>
    </section>
    <section class="home-tools" aria-labelledby="tools-title">
        <div class="section-heading">
            <div>
                <p class="panel-kicker">QUICK ACCESS</p>
                <h2 id="tools-title">快捷工具</h2>
            </div>
        </div>
        <div v-if="loading" class="home-empty" role="status">正在加载工具目录…</div>
        <a-alert v-else-if="errorMessage" :message="errorMessage" type="error" show-icon />
        <div v-else-if="tools.length === 0" class="home-empty" role="status">
            <strong>暂时没有可用工具</strong>
            <span>工具目录暂未配置。</span>
        </div>
        <div v-else class="tool-card-grid">
            <RouterLink v-for="tool in tools" :key="tool.route" class="tool-card" :to="tool.route">
                <div class="tool-card-top">
                    <span class="tool-icon" aria-hidden="true">{{ tool.displayIcon }}</span>
                    <span class="tool-order">{{ String(tool.order).padStart(2, '0') }}</span>
                </div>
                <div class="tool-card-content">
                    <h3>{{ tool.name }}</h3>
                    <p>打开工具</p>
                </div>
                <span class="tool-arrow" aria-hidden="true">→</span>
            </RouterLink>
        </div>
    </section>
</template>
