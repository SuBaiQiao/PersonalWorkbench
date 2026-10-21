<script setup>
import { computed, nextTick, ref, watch } from 'vue';
import { useRoute } from 'vue-router';
import { Modal, message } from 'ant-design-vue';
import { api, getErrorMessage } from '../api/client.js';
import WorkbenchIcon from '../components/WorkbenchIcon.vue';
import { isDark, toggleAppearance } from '../styles/theme.js';
import { downloadBlob } from '../utils/files.js';

const transferring = ref(false);
const importInput = ref(null);
const mainContent = ref(null);
const route = useRoute();
const isHome = computed(() => route.path === '/');

watch(() => route.path, async () => {
    await nextTick();
    mainContent.value?.focus({ preventScroll: true });
});

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

function selectImportFile() {
    importInput.value?.click();
}

function importDatabase(event) {
    const file = event.target.files?.[0];
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
    <a class="skip-link" href="#main-content">跳至主要内容</a>
    <header class="app-header">
        <div class="header-inner">
            <RouterLink class="brand" to="/" aria-label="Personal Workbench 工具首页">
                <span class="brand-mark"><WorkbenchIcon name="grid" /></span>
                <span class="brand-wordmark">Workbench<span>让日常，轻盈一点。</span></span>
            </RouterLink>
            <div class="header-actions">
                <nav class="primary-nav" aria-label="工具导航">
                    <RouterLink to="/" :class="{ 'is-active': isHome }"><WorkbenchIcon name="grid" /><span>工具台</span></RouterLink>
                </nav>
                <div class="database-actions" aria-label="数据库操作">
                    <a-dropdown :trigger="['click']" placement="bottomRight" :disabled="transferring">
                        <a-button class="data-menu-button" :loading="transferring" :disabled="transferring" aria-label="数据管理"><WorkbenchIcon name="database" /><span>数据管理</span><WorkbenchIcon name="chevron" /></a-button>
                        <template #overlay>
                            <a-menu class="data-menu" aria-label="数据管理菜单">
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
    </header>
    <main id="main-content" ref="mainContent" class="app-shell" :class="{ 'is-home': isHome }" tabindex="-1">
        <nav v-if="!isHome" class="tool-breadcrumb" aria-label="当前位置"><RouterLink to="/"><WorkbenchIcon name="back" />所有工具</RouterLink><span aria-hidden="true">/</span><span aria-current="page">{{ route.meta.title }}</span></nav>
        <slot />
    </main>
    <footer class="app-footer">
        <span class="footer-brand"><WorkbenchIcon name="grid" />Personal Workbench</span>
        <span>为日常而造 · 测试数据请勿用于真实业务</span>
    </footer>
</template>
