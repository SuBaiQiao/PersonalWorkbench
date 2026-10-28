<script setup>
import { computed, nextTick, onBeforeUnmount, onMounted, ref, watch } from 'vue';
import { useRoute } from 'vue-router';
import { Modal, message } from 'ant-design-vue';
import { api, getErrorMessage } from '../api/client.js';
import WorkbenchIcon from '../components/WorkbenchIcon.vue';
import WorkbenchSidebar from '../components/WorkbenchSidebar.vue';
import MenuThemePicker from '../components/MenuThemePicker.vue';
import ToolSearch from '../components/ToolSearch.vue';
import { isDark, menuTheme, sidebarCollapsed, toggleAppearance } from '../styles/theme.js';
import { useToolCatalog } from '../composables/useToolCatalog.js';
import { downloadBlob } from '../utils/files.js';

// 数据库导入与导出共用忙碌状态，传输期间禁用入口，防止重复提交或互相覆盖。
const transferring = ref(false);
const importInput = ref(null);
const mainContent = ref(null);
const workspaceScroll = ref(null);
const fullscreenAvailable = ref(false);
const isContentFullscreen = ref(false);
const fullscreenPending = ref(false);
let ownsContentFullscreen = false;
const route = useRoute();
const { tools } = useToolCatalog();
const currentMenu = computed(() => tools.value.find(tool => tool.route === route.path));
const pageTitle = computed(() => currentMenu.value?.name || route.meta.title);
watch(pageTitle, value => { document.title = value + ' · Personal Workbench'; });
const isHome = computed(() => route.path === '/');
const isSidebar = computed(() => menuTheme.value === 'sidebar');
// 手机抽屉是临时界面状态，不与桌面的收起偏好绑定，也不跨刷新保存。
const mobileMenuOpen = ref(false);
const mobileMenuTrigger = ref(null);
// 记录打开菜单时的路由，用于区分“关闭菜单”与“通过菜单进入另一工具”的焦点去向。
let mobileMenuOrigin = route.path;
const pageIcon = computed(() => isHome.value ? 'grid' : currentMenu.value?.icon || route.meta.icon || 'grid');

// 顶部紧凑态仅随当前滚动位置变化，不保存到主题偏好中。
const isHeaderCompact = ref(false);

/**
 * 滚动后收紧顶部区域，接近页顶时恢复正常留白。
 * 使用不同的进入 / 退出阈值，避免滚动停在边界附近时反复切换、产生视觉抖动。
 * 监听器只读取滚动位置并更新布尔状态，不在每次滚动时测量布局。
 */
function updateHeaderCompact() {
    const scrollTop = Math.max(0, workspaceScroll.value?.scrollTop || 0);
    if (scrollTop > 48) isHeaderCompact.value = true;
    else if (scrollTop <= 16) isHeaderCompact.value = false;
}

onMounted(() => {
    // 兼容浏览器恢复滚动位置后直接进入页面的情况。
    updateHeaderCompact();
    fullscreenAvailable.value = Boolean(document.fullscreenEnabled && document.documentElement.requestFullscreen);
    document.addEventListener('fullscreenchange', syncContentFullscreen);
});
onBeforeUnmount(() => {
    document.removeEventListener('fullscreenchange', syncContentFullscreen);
    if (ownsContentFullscreen && document.fullscreenElement === document.documentElement)
        document.exitFullscreen().catch(() => {});
});

/** 同步 Esc、浏览器菜单及播放器自身全屏引起的状态变化，不把视频全屏误当成内容全屏。 */
function syncContentFullscreen() {
    isContentFullscreen.value = ownsContentFullscreen && document.fullscreenElement === document.documentElement;
    if (!document.fullscreenElement) ownsContentFullscreen = false;
}

/**
 * 对文档根节点请求原生全屏，再仅展示中间内容。
 * 保留 body 内的弹窗、下拉框和消息，使全屏中的文件预览、编辑及保存提示仍可正常使用。
 */
async function toggleContentFullscreen() {
    if (fullscreenPending.value) return;
    fullscreenPending.value = true;
    try {
        if (isContentFullscreen.value) await document.exitFullscreen();
        else {
            ownsContentFullscreen = true;
            await document.documentElement.requestFullscreen();
        }
        syncContentFullscreen();
    } catch {
        if (!document.fullscreenElement) ownsContentFullscreen = false;
        message.error('无法切换全屏，请检查浏览器是否允许此页面使用全屏功能。');
    } finally { fullscreenPending.value = false; }
}

/** 打开手机菜单，并记录本次打开来源，供关闭动画结束后恢复键盘焦点。 */
function openMobileMenu() {
    mobileMenuOrigin = route.path;
    mobileMenuOpen.value = true;
}

/**
 * 在抽屉动画结束后安置焦点，避免焦点停在已经隐藏的抽屉内部。
 * 未发生导航时返回菜单按钮，进入其他页面时落到新页面的主内容区。
 * @param {boolean} open 抽屉动画完成后的打开状态，由 after-open-change 事件提供。
 */
function restoreMobileMenuFocus(open) {
    if (open) return;
    const target = route.path === mobileMenuOrigin ? mobileMenuTrigger.value : mainContent.value;
    target?.focus({ preventScroll: true });
}

// 切换为其他菜单布局时不保留手机抽屉的打开状态。
watch(menuTheme, () => { mobileMenuOpen.value = false; });

// 路由变化后先关闭抽屉，再在下一次 DOM 更新后聚焦主内容；preventScroll 保留路由滚动行为。
watch(() => route.path, async () => {
    mobileMenuOpen.value = false;
    await nextTick();
    // 主滚动容器已从 window 移入工作区，路由切换时也要重置该容器的位置。
    if (workspaceScroll.value) {
        workspaceScroll.value.scrollTop = 0;
        workspaceScroll.value.scrollLeft = 0;
    }
    updateHeaderCompact();
    mainContent.value?.focus({ preventScroll: true });
});

/** 下载后端返回的数据库二进制备份，并在成功、失败后统一释放传输状态。 */
async function exportDatabase() {
    transferring.value = true;
    try {
        const response = await api.get('/api/database/export', { responseType: 'blob' });
        downloadBlob(response.data, `personal-workbench-${new Date().toISOString().replace(/[:.]/g, '-')}.db`);
        message.success('数据库备份已导出。');
    } catch (error) {
        message.error(await getErrorMessage(error, '数据库导出失败。'));
    } finally {
        transferring.value = false;
    }
}

/** 通过原生文件选择框获取备份文件，选择结果由 importDatabase 处理。 */
function selectImportFile() {
    importInput.value?.click();
}

/**
 * 读取备份文件，经用户确认后上传并重新加载工作台。
 * 数据库导入会覆盖当前内容，因此必须保留确认对话框；上传失败时抛回异常，
 * 让对话框保持可重试状态，而不是将失败误当作确认操作已完成。
 * @param {Event} event 原生文件输入框的 change 事件。
 */
function importDatabase(event) {
    const file = event.target.files?.[0];
    // 清空原生输入值，使下一次再次选择同一文件时也能触发 change。
    event.target.value = '';
    if (!file) return;

    Modal.confirm({
        title: '导入数据库备份？',
        content: '导入会覆盖当前数据库中的全部数据，且无法撤销。建议先导出当前数据库备份。',
        okText: '覆盖并导入',
        cancelText: '取消',
        okButtonProps: { danger: true },
        async onOk() {
            transferring.value = true;
            try {
                const data = new FormData();
                data.append('file', file);
                const response = await api.post('/api/database/import', data, {
                    headers: { 'Content-Type': 'multipart/form-data' }
                });
                message.success(`已导入包含 ${response.data.importedTables} 张表的数据库。`);
                window.location.reload();
            } catch (error) {
                message.error(await getErrorMessage(error, '数据库导入失败。'));
                throw error;
            } finally {
                transferring.value = false;
            }
        }
    });
}
</script>

<template>
    <!-- 主题仅改变导航和容器样式，主内容插槽始终挂在同一位置，保留工具组件的编辑状态。 -->
    <div class="workbench-layout" :data-menu-theme="menuTheme" :class="{ 'sidebar-collapsed': sidebarCollapsed, 'has-breadcrumb': !isHome, 'is-scrolled': isHeaderCompact, 'is-content-fullscreen': isContentFullscreen }">
    <a class="skip-link" href="#main-content">跳至主要内容</a>
    <!-- 桌面固定侧栏与手机抽屉共享内容组件，通过 CSS 断点决定可见的导航入口。 -->
    <aside v-if="isSidebar" class="workbench-sidebar" aria-label="左侧工具菜单"><WorkbenchSidebar :collapsed="sidebarCollapsed" @toggle-collapse="sidebarCollapsed = !sidebarCollapsed" /></aside>
    <a-drawer v-if="isSidebar" v-model:open="mobileMenuOpen" title="工具导航" placement="left" :width="284" root-class-name="workbench-mobile-drawer" :body-style="{ padding: 0 }" @after-open-change="restoreMobileMenuFocus">
        <WorkbenchSidebar mobile @navigate="mobileMenuOpen = false" />
    </a-drawer>
    <!-- 页脚位于滚动容器之外；导航与正文在容器中配合吸附，页面本身不再滚动。 -->
    <div ref="workspaceScroll" class="workspace-scroll" @scroll.passive="updateHeaderCompact">
    <!-- App 主题显示品牌导航；侧栏主题显示当前工具标题，两种布局共用右侧操作区。 -->
    <header class="app-header">
        <div class="header-inner">
            <RouterLink v-if="!isSidebar" class="brand" to="/" aria-label="Personal Workbench 工具首页">
                <span class="brand-mark"><WorkbenchIcon name="grid" /></span>
                <span class="brand-wordmark">Workbench<span>让日常，轻盈一点。</span></span>
            </RouterLink>
            <div v-else class="workspace-heading">
                <button ref="mobileMenuTrigger" class="mobile-menu-button" type="button" aria-label="打开左侧菜单" :aria-expanded="mobileMenuOpen" @click="openMobileMenu"><WorkbenchIcon name="menu" /></button>
                <span class="workspace-page-icon"><WorkbenchIcon :name="pageIcon" /></span>
                <div><span class="workspace-label">PERSONAL WORKSPACE</span><strong>{{ isHome ? '工具总览' : pageTitle }}</strong></div>
            </div>
            <ToolSearch />
            <div class="header-actions">
                <nav v-if="!isSidebar" class="primary-nav" aria-label="工具导航">
                    <RouterLink to="/" :class="{ 'is-active': isHome }"><WorkbenchIcon name="grid" /><span>工具台</span></RouterLink>
                </nav>
                <MenuThemePicker />
                <div class="database-actions" aria-label="数据库操作">
                    <a-dropdown :trigger="['click']" placement="bottomRight" :disabled="transferring">
                        <a-button class="data-menu-button" :loading="transferring" :disabled="transferring" aria-label="数据管理"><WorkbenchIcon name="database" /><span>数据管理</span><WorkbenchIcon name="chevron" /></a-button>
                        <template #overlay>
                            <a-menu class="data-menu" aria-label="数据管理菜单">
                                <a-menu-item key="menus"><RouterLink to="/settings/menus"><WorkbenchIcon name="menu" /> 菜单管理</RouterLink></a-menu-item>
                                <a-menu-item key="database"><RouterLink to="/settings/database"><WorkbenchIcon name="database" /> 数据库管理</RouterLink></a-menu-item>
                                <a-menu-item key="export" @click="exportDatabase"><WorkbenchIcon name="download" /> 导出数据库</a-menu-item>
                                <a-menu-item key="import" @click="selectImportFile"><WorkbenchIcon name="upload" /> 导入备份</a-menu-item>
                            </a-menu>
                        </template>
                    </a-dropdown>
                    <input ref="importInput" class="database-file-input" type="file" accept=".db,.sqlite,application/vnd.sqlite3,application/x-sqlite3" @change="importDatabase" />
                </div>
                <button class="appearance-button" type="button" :aria-label="isDark ? '切换到浅色外观' : '切换到深色外观'" :title="isDark ? '切换到浅色外观' : '切换到深色外观'" @click="toggleAppearance"><WorkbenchIcon :name="isDark ? 'moon' : 'sun'" /></button>
            </div>
        </div>
        <!-- 面包屑与导航共用吸附容器，避免分别计算定位高度后互相遮挡。 -->
        <nav v-if="!isHome" class="tool-breadcrumb" aria-label="当前位置"><RouterLink to="/"><WorkbenchIcon name="back" />所有工具</RouterLink><span aria-hidden="true">/</span><span aria-current="page" :title="pageTitle">{{ pageTitle }}</span></nav>
    </header>
    <!-- tabindex=-1 允许程序聚焦和“跳至主要内容”定位，不额外占用正常的 Tab 顺序。 -->
    <main id="main-content" ref="mainContent" class="app-shell" :class="{ 'is-home': isHome }" tabindex="-1">
        <slot />
    </main>
    </div>
    <footer class="app-footer">
        <span class="footer-brand"><WorkbenchIcon name="grid" />Personal Workbench</span>
        <span class="footer-copy">为日常而造 · 测试数据请勿用于真实业务</span>
        <button class="workspace-fullscreen-button" type="button" aria-label="内容全屏" :title="fullscreenAvailable ? '全屏显示中间内容，按 Esc 退出' : '当前浏览器不支持网页全屏，可使用浏览器自身的全屏功能'" :disabled="!fullscreenAvailable || fullscreenPending" @click="toggleContentFullscreen"><WorkbenchIcon name="fullscreen" /><span>内容全屏</span></button>
    </footer>
    <button v-if="isContentFullscreen" class="workspace-fullscreen-button fullscreen-exit-button" type="button" aria-label="退出内容全屏" :disabled="fullscreenPending" @click="toggleContentFullscreen"><WorkbenchIcon name="fullscreen-exit" /><span>退出全屏</span><kbd>Esc</kbd></button>
    </div>
</template>
