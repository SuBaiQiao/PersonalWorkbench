<script setup>
import { ref } from 'vue';
import { Modal, message } from 'ant-design-vue';
import { api, getErrorMessage } from '../api/client.js';

const transferring = ref(false);
const importInput = ref(null);

async function exportDatabase() {
    transferring.value = true;
    try {
        const response = await api.get('/api/database/export', { responseType: 'blob' });
        const url = URL.createObjectURL(response.data);
        const link = document.createElement('a');
        link.href = url;
        link.download = `personal-workbench-${new Date().toISOString().replace(/[:.]/g, '-')}.db`;
        link.click();
        URL.revokeObjectURL(url);
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
    <header class="app-header">
        <div class="header-inner">
            <RouterLink class="brand" to="/" aria-label="Personal Workbench 工具首页">
                <span class="brand-mark" aria-hidden="true">PW</span>
                <span>Personal Workbench</span>
            </RouterLink>
            <div class="header-actions">
                <nav class="primary-nav" aria-label="工具导航">
                    <RouterLink to="/">工具台</RouterLink>
                </nav>
                <div class="database-actions" aria-label="数据库操作">
                    <a-button size="small" :loading="transferring" :disabled="transferring" @click="exportDatabase">导出数据库</a-button>
                    <a-button size="small" :loading="transferring" :disabled="transferring" @click="selectImportFile">导入备份</a-button>
                    <input ref="importInput" class="database-file-input" type="file" accept=".db,.sqlite,application/vnd.sqlite3,application/x-sqlite3" @change="importDatabase" />
                </div>
            </div>
        </div>
    </header>
    <main class="app-shell">
        <slot />
    </main>
    <footer class="app-footer">
        <span>本地开发工具</span>
        <span>·</span>
        <span>测试数据请勿用于真实业务</span>
    </footer>
</template>
