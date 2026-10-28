<script setup>
import { computed, onMounted, reactive, ref } from 'vue';
import { message, Modal } from 'ant-design-vue';
import { api, getErrorMessage } from '../api/client.js';
import { useToolCatalog } from '../composables/useToolCatalog.js';
import { toolComponents } from '../router/index.js';
import WorkbenchIcon from '../components/WorkbenchIcon.vue';

const { loadTools } = useToolCatalog();
const menus = ref([]), categories = ref([]), loading = ref(false), saving = ref(false), error = ref('');
const query = ref(''), tab = ref('menus'), modalOpen = ref(false), editingId = ref(null), kind = ref('menu'), formRef = ref();
const form = reactive({});
const isMenu = computed(() => kind.value === 'menu');
const filteredMenus = computed(() => menus.value.filter(menu => [menu.name, menu.route, menu.description].join(' ').toLowerCase().includes(query.value.toLowerCase().trim())));
const categoryOptions = computed(() => categories.value.map(category => ({ value: category.id, label: category.name })));
const componentOptions = computed(() => Object.keys(toolComponents).sort().map(key => ({ value: key, label: menus.value.find(menu => menu.componentKey === key)?.name || key })));
const iconOptions = ['grid', 'folder', 'video', 'braces', 'diff', 'regex', 'shuffle', 'lock', 'code', 'network', 'crop', 'identity', 'currency', 'files', 'image', 'clock', 'phone', 'edit', 'activity', 'cards', 'qr', 'database', 'menu'].map(value => ({ value, label: value }));
const toneOptions = [{ value: 'blue', label: '蓝色' }, { value: 'teal', label: '青色' }, { value: 'amber', label: '琥珀' }, { value: 'rose', label: '玫红' }, { value: 'indigo', label: '靛蓝' }];
const menuColumns = [
    { title: '菜单', key: 'menu', width: 280 }, { title: '分类', key: 'category', width: 140 },
    { title: '排序', dataIndex: 'order', width: 80 }, { title: '状态', key: 'status', width: 100 }, { title: '操作', key: 'actions', width: 160 }
];
const categoryColumns = [
    { title: '分类名称', dataIndex: 'name' }, { title: '标识', dataIndex: 'id' },
    { title: '排序', dataIndex: 'order', width: 80 }, { title: '菜单数', key: 'count', width: 100 }, { title: '操作', key: 'actions', width: 160 }
];
const rules = computed(() => isMenu.value ? {
    name: [{ required: true, whitespace: true, message: '请输入菜单名称' }],
    route: [{ required: true, pattern: /^\/tools\/[a-z0-9]+(?:-[a-z0-9]+)*$/, message: '请使用 /tools/example 格式的小写站内路径' }],
    componentKey: [{ required: true, message: '请选择功能页面' }], category: [{ required: true, message: '请选择分类' }],
    features: [{ validator: (_, value) => Array.isArray(value) && value.length <= 30 && value.every(item => item.trim() && item.length <= 100) ? Promise.resolve() : Promise.reject('标签最多 30 项，每项为 1–100 字符') }],
    order: [{ required: true, type: 'number', message: '请输入排序值' }]
} : {
    name: [{ required: true, whitespace: true, message: '请输入分类名称' }],
    id: [{ required: true, pattern: /^[a-z][a-z0-9-]*$/, message: '使用小写字母、数字或连字符，以字母开头' }],
    order: [{ required: true, type: 'number', message: '请输入排序值' }]
});

async function loadManagement() {
    loading.value = true; error.value = '';
    try {
        const { data } = await api.get('/api/tools/manage');
        menus.value = data.tools; categories.value = data.categories;
    } catch (failure) { error.value = await getErrorMessage(failure, '菜单配置加载失败。'); }
    finally { loading.value = false; }
}
async function refresh() { await loadManagement(); await loadTools(); }
function edit(type, item) {
    kind.value = type; editingId.value = item?.id ?? null;
    for (const key of Object.keys(form)) delete form[key];
    Object.assign(form, type === 'menu'
        ? { name: '', route: '', componentKey: undefined, category: categories.value[0]?.id, icon: 'grid', tone: 'blue', order: 0, description: '', keywords: '', features: [], isEnabled: true, ...item }
        : { id: '', name: '', order: 0, ...item });
    if (type === 'menu') form.features = [...form.features];
    modalOpen.value = true;
}
async function save() {
    try { await formRef.value.validate(); } catch { return; }
    if (isMenu.value && !toolComponents[form.componentKey]) { message.error('请选择可用的功能页面。'); return; }
    saving.value = true;
    try {
        const base = isMenu.value ? '/api/tools' : '/api/tools/categories';
        const payload = { ...form, name: form.name.trim() };
        if (editingId.value !== null) await api.put(base + '/' + encodeURIComponent(editingId.value), payload);
        else await api.post(base, payload);
        modalOpen.value = false;
        message.success('配置已保存。');
        await refresh();
    } catch (failure) { message.error(await getErrorMessage(failure, '保存失败。')); }
    finally { saving.value = false; }
}
function remove(type, item) {
    Modal.confirm({
        title: '删除“' + item.name + '”？', content: type === 'menu' ? '删除后该菜单将从工作台中移除。' : '仅可删除没有菜单的分类。',
        okText: '删除', cancelText: '取消', okButtonProps: { danger: true },
        async onOk() {
            saving.value = true;
            try {
                await api.delete((type === 'menu' ? '/api/tools/' : '/api/tools/categories/') + encodeURIComponent(item.id));
                message.success('已删除。'); await refresh();
            } catch (failure) { message.error(await getErrorMessage(failure, '删除失败。')); throw failure; }
            finally { saving.value = false; }
        }
    });
}
onMounted(loadManagement);
</script>

<template>
    <section class="menu-settings" aria-labelledby="menu-title">
        <header class="settings-heading"><div><p class="eyebrow">WORKSPACE SETTINGS</p><h1 id="menu-title">菜单管理</h1><p class="intro-copy">维护工具入口、分类与搜索信息，保存后同步更新工作台。</p></div><a-button :loading="loading" :disabled="saving" @click="refresh">刷新配置</a-button></header>
        <a-alert v-if="error" :message="error" type="error" show-icon><template #action><a-button size="small" @click="loadManagement">重试</a-button></template></a-alert>
        <div class="settings-panel">
            <a-tabs v-model:active-key="tab">
                <a-tab-pane key="menus" :tab="'工具菜单 · ' + menus.length">
                    <div class="settings-toolbar"><a-input-search v-model:value="query" placeholder="搜索名称、路由或说明" aria-label="搜索菜单" allow-clear /><a-button type="primary" :disabled="loading || saving || !!error || !categories.length" @click="edit('menu')">新增菜单</a-button></div>
                    <a-alert v-if="!categories.length && !loading && !error" message="请先在分类管理中新增一个分类。" type="info" show-icon />
                    <a-table :columns="menuColumns" :data-source="filteredMenus" row-key="id" :loading="loading" :scroll="{ x: 760 }" :pagination="{ pageSize: 15, showSizeChanger: false, hideOnSinglePage: true }">
                        <template #bodyCell="{ column, record }">
                            <template v-if="column.key === 'menu'"><div class="menu-cell"><span class="menu-preview" :data-tone="record.tone"><WorkbenchIcon :name="record.icon" /></span><div><strong>{{ record.name }}</strong><small>{{ record.route }}</small><small v-if="!toolComponents[record.componentKey]" class="unavailable">功能页面不可用，请重新选择</small></div></div></template>
                            <template v-else-if="column.key === 'category'">{{ categories.find(category => category.id === record.category)?.name || record.category }}</template>
                            <template v-else-if="column.key === 'status'"><a-tag :color="record.isEnabled ? 'green' : 'default'">{{ record.isEnabled ? '启用' : '禁用' }}</a-tag></template>
                            <template v-else-if="column.key === 'actions'"><a-space><a-button size="small" :disabled="saving || loading" @click="edit('menu', record)">编辑</a-button><a-button size="small" danger :disabled="saving || loading" @click="remove('menu', record)">删除</a-button></a-space></template>
                        </template>
                    </a-table>
                </a-tab-pane>
                <a-tab-pane key="categories" :tab="'分类管理 · ' + categories.length">
                    <div class="settings-toolbar category-toolbar"><span>排序值越小，分类越靠前。</span><a-button type="primary" :disabled="loading || saving || !!error" @click="edit('category')">新增分类</a-button></div>
                    <a-table :columns="categoryColumns" :data-source="categories" row-key="id" :loading="loading" :scroll="{ x: 600 }" :pagination="false">
                        <template #bodyCell="{ column, record }"><template v-if="column.key === 'count'">{{ menus.filter(menu => menu.category === record.id).length }}</template><template v-else-if="column.key === 'actions'"><a-space><a-button size="small" :disabled="saving || loading" @click="edit('category', record)">编辑</a-button><a-button size="small" danger :disabled="saving || loading || menus.some(menu => menu.category === record.id)" @click="remove('category', record)">删除</a-button></a-space></template></template>
                    </a-table>
                </a-tab-pane>
            </a-tabs>
        </div>
    </section>
    <a-modal v-model:open="modalOpen" :title="(editingId === null ? '新增' : '编辑') + (isMenu ? '菜单' : '分类')" :width="680" :confirm-loading="saving" :cancel-button-props="{ disabled: saving }" :closable="!saving" :mask-closable="false" :keyboard="!saving" destroy-on-close ok-text="保存" cancel-text="取消" @ok="save">
        <a-form ref="formRef" :model="form" :rules="rules" layout="vertical" class="settings-form" :disabled="saving">
            <a-form-item label="名称" name="name" required><a-input v-model:value="form.name" :maxlength="isMenu ? 120 : 80" /></a-form-item>
            <template v-if="isMenu">
                <div class="settings-form-row"><a-form-item label="功能页面" name="componentKey" required><a-select v-model:value="form.componentKey" :options="componentOptions" show-search option-filter-prop="label" placeholder="选择已有功能" /></a-form-item><a-form-item label="所属分类" name="category" required><a-select v-model:value="form.category" :options="categoryOptions" /></a-form-item></div>
                <a-form-item label="路由地址" name="route" required extra="例如 /tools/my-tool，修改后使用新地址访问。"><a-input v-model:value="form.route" maxlength="200" placeholder="/tools/my-tool" /></a-form-item>
                <div class="settings-form-row"><a-form-item label="图标" name="icon"><a-select v-model:value="form.icon" :options="iconOptions"><template #option="{ value }"><WorkbenchIcon :name="value" /> {{ value }}</template></a-select></a-form-item><a-form-item label="颜色" name="tone"><a-select v-model:value="form.tone" :options="toneOptions" /></a-form-item></div>
                <a-form-item label="说明" name="description"><a-textarea v-model:value="form.description" :rows="2" maxlength="1000" show-count /></a-form-item>
                <a-form-item label="搜索关键词" name="keywords"><a-input v-model:value="form.keywords" maxlength="2000" placeholder="使用空格分隔关键词和别名" /></a-form-item>
                <a-form-item label="功能标签" name="features" extra="输入后按回车，每项最多 100 字符，最多 30 项。"><a-select v-model:value="form.features" mode="tags" :token-separators="['，', ',']" :open="false" /></a-form-item>
            </template>
            <a-form-item v-else label="分类标识" name="id" required extra="创建后不可修改，例如 develop、my-tools。"><a-input v-model:value="form.id" :disabled="editingId !== null" maxlength="64" /></a-form-item>
            <div class="settings-form-row"><a-form-item label="排序" name="order"><a-input-number v-model:value="form.order" :min="-2147483648" :max="2147483647" :precision="0" style="width: 100%" /></a-form-item><a-form-item v-if="isMenu" label="显示状态" name="isEnabled"><a-switch v-model:checked="form.isEnabled" checked-children="启用" un-checked-children="禁用" /></a-form-item></div>
        </a-form>
    </a-modal>
</template>

<style scoped>
.menu-settings { display: grid; gap: 20px; width: 100%; max-width: 1200px; margin-inline: auto; }
.settings-heading { display: flex; align-items: center; justify-content: space-between; gap: 16px; }
.settings-heading h1 { margin: 4px 0 12px; font-size: 30px; color: var(--text); }
.settings-heading .intro-copy { margin-bottom: 0; }
.settings-panel { min-width: 0; padding: 8px 24px 24px; background: var(--surface); border: 1px solid var(--border); border-radius: 18px; }
.settings-toolbar { display: flex; justify-content: space-between; align-items: center; gap: 16px; margin: 8px 0 20px; }
.settings-toolbar .ant-input-search { max-width: 380px; }
.category-toolbar { color: var(--text-muted); font-size: 13px; }
.menu-cell { display: flex; gap: 12px; align-items: center; }
.menu-cell strong { display: block; color: var(--text); }
.menu-cell small { display: block; color: var(--text-muted); overflow-wrap: anywhere; }
.menu-preview { display: grid; place-items: center; flex-shrink: 0; width: 36px; height: 36px; border-radius: 10px; color: var(--accent-strong); background: var(--accent-soft); }
.menu-cell .unavailable { color: #cf4a39; }
.settings-form { padding-top: 16px; }
.settings-form-row { display: grid; grid-template-columns: 1fr 1fr; gap: 16px; }
@media (max-width: 600px) { .settings-heading { align-items: flex-start; flex-direction: column; } .settings-panel { padding: 4px 12px 16px; } .settings-form-row { grid-template-columns: 1fr; gap: 0; } .settings-toolbar { gap: 8px; } }
</style>
