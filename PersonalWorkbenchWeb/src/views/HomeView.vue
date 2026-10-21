<script setup>
import { computed, onMounted, ref } from 'vue';
import { api, getErrorMessage } from '../api/client.js';
import WorkbenchIcon from '../components/WorkbenchIcon.vue';
import { getToolPresentation, toolCategories } from '../utils/toolPresentation.js';

const tools = ref([]);
const loading = ref(true);
const errorMessage = ref('');
const search = ref('');
const activeCategory = ref('all');

const presentedTools = computed(() => tools.value.map(tool => ({ ...tool, ...getToolPresentation(tool.route) })));
const filteredTools = computed(() => {
    const query = search.value.trim().toLocaleLowerCase();
    return presentedTools.value.filter(tool =>
        (activeCategory.value === 'all' || tool.category === activeCategory.value)
        && (!query || `${tool.name} ${tool.description} ${tool.keywords}`.toLocaleLowerCase().includes(query))
    );
});
const categories = computed(() => toolCategories.filter(category => category.id === 'all' || presentedTools.value.some(tool => tool.category === category.id)));

function resetFilters() { search.value = ''; activeCategory.value = 'all'; }
async function loadTools() {
    loading.value = true;
    errorMessage.value = '';
    try {
        const response = await api.get('/api/tools');
        tools.value = response.data;
    } catch (error) {
        errorMessage.value = await getErrorMessage(error, '工具目录加载失败。');
    } finally {
        loading.value = false;
    }
}
onMounted(loadTools);
</script>

<template>
    <section class="home-hero" aria-labelledby="home-title">
        <div class="home-hero-copy">
            <div class="hero-label"><span class="signal-dot" aria-hidden="true"></span>你的个人效率空间</div>
            <h1 id="home-title">把时间，<br />留给<span>更好的创造。</span></h1>
            <p class="intro-copy">那些琐碎的转换、整理与调试，交给趁手的工具。<br class="desktop-break" />在一个轻盈的工作台里，让日常井然有序。</p>
            <div class="hero-meta"><span class="hero-tool-count">{{ loading ? '—' : tools.length }}<small>款实用工具</small></span><span class="hero-meta-divider"></span><span>触手可及，随时就绪。</span></div>
        </div>
        <div class="hero-launchpad" aria-label="常用工具快捷入口">
            <div class="launchpad-orbit" aria-hidden="true"></div><div class="launchpad-orbit orbit-inner" aria-hidden="true"></div>
            <div class="launchpad-caption"><WorkbenchIcon name="sparkles" />A LITTLE MORE POSSIBILITY</div>
            <RouterLink class="launch-tile tile-qr" to="/tools/code-generator"><span class="launch-icon"><WorkbenchIcon name="qr" /></span><span>一扫，即达<small>二维码与条形码</small></span><WorkbenchIcon name="arrow" /></RouterLink>
            <RouterLink class="launch-tile tile-crypto" to="/tools/crypto"><span class="launch-icon"><WorkbenchIcon name="lock" /></span><span>转换，自如<small>加密与编解码</small></span><WorkbenchIcon name="arrow" /></RouterLink>
            <RouterLink class="launch-tile tile-edit" to="/tools/collaborative-editor"><span class="launch-icon"><WorkbenchIcon name="edit" /></span><span>灵感，同步<small>协同文本编辑</small></span><WorkbenchIcon name="arrow" /></RouterLink>
            <span class="launchpad-footnote">简单的工具，流畅的每一天。</span>
        </div>
    </section>
    <section id="tools" class="home-tools" aria-labelledby="tools-title">
        <div class="section-heading home-section-heading">
            <div><p class="panel-kicker">YOUR EVERYDAY ESSENTIALS</p><h2 id="tools-title">好工具，一触即达<span class="collection-count">{{ tools.length }}</span></h2></div>
            <div class="tool-search"><WorkbenchIcon name="search" /><input v-model="search" type="search" aria-label="搜索工具" placeholder="搜索工具、功能或关键词" /><button v-if="search" type="button" aria-label="清空搜索" @click="search = ''"><WorkbenchIcon name="close" /></button></div>
        </div>
        <div class="tool-collection-toolbar">
            <div class="category-filters" role="group" aria-label="工具分类"><button v-for="category in categories" :key="category.id" type="button" :class="{ selected: activeCategory === category.id }" :aria-pressed="activeCategory === category.id" @click="activeCategory = category.id">{{ category.name }}</button></div>
            <span class="collection-summary" role="status" aria-live="polite">{{ loading ? '正在准备工具…' : `共 ${filteredTools.length} 项工具` }}</span>
        </div>
        <div v-if="loading" class="tool-card-grid" aria-label="正在加载工具" aria-busy="true"><div v-for="index in 6" :key="index" class="tool-card tool-skeleton"><span></span><span></span><span></span></div></div>
        <div v-else-if="errorMessage" class="home-empty" role="alert"><strong>工具目录暂时未能加载</strong><span>{{ errorMessage }}</span><a-button @click="loadTools">重新加载</a-button></div>
        <div v-else-if="!filteredTools.length" class="home-empty"><WorkbenchIcon name="search" /><strong>{{ tools.length ? '没有找到匹配的工具' : '工具目录还在准备中' }}</strong><span>试试其他关键词，或查看全部工具。</span><a-button v-if="tools.length" @click="resetFilters">查看全部工具</a-button></div>
        <div v-else class="tool-card-grid">
            <RouterLink v-for="tool in filteredTools" :key="tool.route" class="tool-card" :data-tone="tool.tone" :to="tool.route">
                <div class="tool-card-top"><span class="tool-icon"><WorkbenchIcon :name="tool.icon" /></span><span class="tool-category">{{ toolCategories.find(category => category.id === tool.category)?.name }}</span></div>
                <div class="tool-card-content"><h3>{{ tool.name }}</h3><p>{{ tool.description }}</p></div>
                <span class="tool-arrow"><WorkbenchIcon name="arrow" /></span>
            </RouterLink>
        </div>
        <p class="home-closing-note"><WorkbenchIcon name="sparkles" />一点小工具，释放更多可能。</p>
    </section>
</template>
