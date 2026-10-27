<script setup>
import { computed, nextTick, ref, watch } from 'vue';
import { useRoute } from 'vue-router';
import WorkbenchIcon from './WorkbenchIcon.vue';
import { useToolCatalog } from '../composables/useToolCatalog.js';
import { toolCategories } from '../utils/toolPresentation.js';
import { useToolSearch } from '../composables/useToolSearch.js';
import { searchTools } from '../utils/toolSearch.js';

/**
 * 桌面侧栏与手机抽屉复用同一份菜单内容。
 * collapsed 由父布局控制；mobile 模式始终显示完整文字，并由抽屉提供标题和关闭入口。
 */
const props = defineProps({ collapsed: Boolean, mobile: Boolean });
// 内容组件只通知导航或收起意图，抽屉关闭和偏好保存交给父布局处理。
const emit = defineEmits(['navigate', 'toggle-collapse']);
const route = useRoute();
const { tools, presentedTools, loading, errorMessage, loadTools } = useToolCatalog();
const { query: search, clearSearch } = useToolSearch();
const navigation = ref(null);
/**
 * 按名称、简介与关键词过滤，再依照统一分类顺序分组。
 * 侧栏不重复展示首页的“全部工具”分类，空分类在搜索时自动隐藏。
 */
const groups = computed(() => {
    const matching = searchTools(presentedTools.value, search.value);
    return toolCategories.filter(category => category.id !== 'all')
        .map(category => ({ ...category, tools: matching.filter(tool => tool.category === category.id) }))
        .filter(group => group.tools.length);
});

// 路由切换、异步目录返回和收起状态变化都可能改变选中项的位置。
// 等待 Vue 更新 DOM 后，仅将导航容器中的当前项滚动至可见位置，避免强制滚到列表顶部。
watch([() => route.path, () => tools.value.length, () => props.collapsed], async () => {
    await nextTick();
    navigation.value?.querySelector('[aria-current="page"]')?.scrollIntoView({ block: 'nearest' });
}, { immediate: true });
</script>

<template>
    <!-- 手机抽屉始终使用完整菜单，桌面收起偏好不会令手机菜单只剩图标。 -->
    <div class="sidebar-content" :class="{ 'is-collapsed': collapsed && !mobile }">
        <div v-if="!mobile" class="sidebar-brand">
            <RouterLink class="brand" to="/" aria-label="Personal Workbench 工具首页" @click="emit('navigate')">
                <span class="brand-mark"><WorkbenchIcon name="grid" /></span>
                <span class="brand-wordmark">Workbench<span>你的个人效率空间</span></span>
            </RouterLink>
        </div>
        <div class="sidebar-overview">
            <RouterLink class="sidebar-link sidebar-home" to="/" :class="{ 'is-active': route.path === '/' }" :aria-current="route.path === '/' ? 'page' : undefined" aria-label="工具总览" :title="collapsed ? '工具总览' : undefined" @click="emit('navigate')">
                <WorkbenchIcon name="grid" /><span class="sidebar-link-label">工具总览</span><span class="sidebar-tool-count">{{ tools.length }}</span>
            </RouterLink>
        </div>
        <!-- 中间导航区独立滚动，品牌和底部收起按钮保持可访问；搜索统一放在顶栏。 -->
        <nav ref="navigation" class="sidebar-navigation" aria-label="分类工具导航" :aria-busy="loading">
            <div v-if="loading" class="sidebar-status" role="status"><span>正在加载工具…</span><span v-for="index in 5" :key="index" class="sidebar-skeleton"></span></div>
            <div v-else-if="errorMessage" class="sidebar-status" role="alert"><span>{{ errorMessage }}</span><button type="button" aria-label="重新加载工具目录" @click="loadTools"><WorkbenchIcon name="back" /><span>重新加载</span></button></div>
            <div v-else-if="!groups.length" class="sidebar-status" role="status"><WorkbenchIcon name="search" /><span>{{ tools.length ? '没有匹配的工具' : '暂无可用工具' }}</span><button v-if="search" type="button" @click="clearSearch">清空搜索</button></div>
            <section v-for="group in groups" :key="group.id" class="sidebar-group" :aria-label="group.name">
                <h2 class="sidebar-group-heading"><span>{{ group.name }}</span><span>{{ group.tools.length }}</span></h2>
                <!-- 文字在图标栏隐藏时，仍通过 aria-label 和 title 提供完整工具名称。 -->
                <RouterLink v-for="tool in group.tools" :key="tool.route" class="sidebar-link" :to="tool.route" :class="{ 'is-active': route.path === tool.route }" :aria-current="route.path === tool.route ? 'page' : undefined" :aria-label="tool.name" :title="tool.name" @click="emit('navigate')">
                    <WorkbenchIcon :name="tool.icon" /><span class="sidebar-link-label">{{ tool.name }}</span><span class="sidebar-active-dot" aria-hidden="true"></span>
                </RouterLink>
            </section>
        </nav>
        <div v-if="!mobile" class="sidebar-footer">
            <button class="sidebar-collapse-button" type="button" :aria-label="collapsed ? '展开左侧菜单' : '收起左侧菜单'" :title="collapsed ? '展开左侧菜单' : '收起左侧菜单'" :aria-expanded="!collapsed" @click="emit('toggle-collapse')"><WorkbenchIcon :name="collapsed ? 'expand' : 'collapse'" /><span>收起菜单</span><kbd>‹</kbd></button>
        </div>
    </div>
</template>
