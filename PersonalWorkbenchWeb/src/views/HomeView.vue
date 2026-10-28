<script setup>
import { computed, ref, watch } from 'vue';
import WorkbenchIcon from '../components/WorkbenchIcon.vue';
import { useToolCatalog } from '../composables/useToolCatalog.js';
import { useToolSearch } from '../composables/useToolSearch.js';
import { searchTools } from '../utils/toolSearch.js';

// 顶部搜索与侧栏共用搜索词及规则，分类仅影响首页卡片。
const { tools, presentedTools, toolCategories, loading, errorMessage, loadTools } = useToolCatalog();
const { query: search, clearSearch } = useToolSearch();
const activeCategory = ref('all');

// 分类与关键词采用交集筛选，搜索简介及别名可覆盖用户习惯的功能叫法。
const filteredTools = computed(() => {
    return searchTools(presentedTools.value, search.value).filter(tool => activeCategory.value === 'all' || tool.category === activeCategory.value);
});
// 开始新搜索时恢复全部分类，避免上一次分类把相关工具隐藏。
watch(search, value => { if (value.trim()) activeCategory.value = 'all'; });
// 始终保留“全部工具”，其余分类只在后端目录确实包含对应工具时显示。
const categories = computed(() => toolCategories.value.filter(category => category.id === 'all' || presentedTools.value.some(tool => tool.category === category.id)));
watch(categories, value => { if (!value.some(category => category.id === activeCategory.value)) activeCategory.value = 'all'; });

/** 清空首页的两个筛选条件，供无搜索结果时恢复完整目录。 */
function resetFilters() { clearSearch(); activeCategory.value = 'all'; }
</script>

<template>
    <section v-if="!search.trim()" class="home-hero" aria-labelledby="home-title">
        <div class="home-hero-copy">
            <div class="hero-label"><span class="signal-dot" aria-hidden="true"></span>你的个人效率空间</div>
            <h1 id="home-title">把时间，<br />留给<span>更好的创造。</span></h1>
            <p class="intro-copy">那些琐碎的转换、整理与调试，交给趁手的工具。<br class="desktop-break" />在一个轻盈的工作台里，让日常井然有序。</p>
            <div class="hero-meta"><span class="hero-tool-count">{{ loading ? '—' : tools.length }}<small>款实用工具</small></span><span class="hero-meta-divider"></span><span>触手可及，随时就绪。</span></div>
        </div>
        <div class="hero-launchpad" aria-label="常用工具快捷入口">
            <div class="launchpad-orbit" aria-hidden="true"></div><div class="launchpad-orbit orbit-inner" aria-hidden="true"></div>
            <div class="launchpad-caption"><WorkbenchIcon name="sparkles" />A LITTLE MORE POSSIBILITY</div>
            <RouterLink v-for="(tool, index) in presentedTools.slice(0, 3)" :key="tool.id" class="launch-tile" :class="['tile-qr', 'tile-crypto', 'tile-edit'][index]" :to="tool.route"><span class="launch-icon"><WorkbenchIcon :name="tool.icon" /></span><span>{{ tool.name }}<small>{{ toolCategories.find(category => category.id === tool.category)?.name }}</small></span><WorkbenchIcon name="arrow" /></RouterLink>
            <span class="launchpad-footnote">简单的工具，流畅的每一天。</span>
        </div>
    </section>
    <section id="tools" class="home-tools" aria-labelledby="tools-title">
        <div class="section-heading home-section-heading">
            <div><p class="panel-kicker">YOUR EVERYDAY ESSENTIALS</p><h1 v-if="search.trim()" id="tools-title" class="tool-search-title">搜索结果<span class="collection-count">{{ filteredTools.length }}</span></h1><h2 v-else id="tools-title">好工具，一触即达<span class="collection-count">{{ tools.length }}</span></h2></div>
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

<style scoped>
.home-section-heading .tool-search-title { margin: 0; font-size: 22px; letter-spacing: -.02em; }
</style>
