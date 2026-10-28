<script setup>
import { computed, onBeforeUnmount, onMounted, reactive, ref, watch } from 'vue';
import { message, Modal } from 'ant-design-vue';
import { api, getErrorMessage } from '../api/client.js';
import WorkbenchIcon from '../components/WorkbenchIcon.vue';
import { cellText, fieldEditor, fieldValue, valueModes, columnEditor, columnPayload } from '../utils/databaseManagement.js';
import DatabaseColumnEditor from '../components/DatabaseColumnEditor.vue';
import { loadTools } from '../composables/useToolCatalog.js';

const base = '/api/database/manage';
const tables = ref([]), selected = ref(''), tableSearch = ref(''), tab = ref('data');
const loading = ref(false), saving = ref(false), error = ref(''), structure = ref(null), rows = ref([]), total = ref(0), editable = ref(false);
const page = ref(1), pageSize = ref(20), search = ref(''), sort = ref(undefined), descending = ref(false);
const sql = ref('-- 在此编写 SQLite SQL，支持多条语句\nSELECT name, type FROM sqlite_schema ORDER BY name;');
const sqlResults = ref([]), sqlError = ref(''), sqlDuration = ref(null);
const logRows = ref([]), logTotal = ref(0), logPage = ref(1), logSearch = ref(''), logStatus = ref(''), logLoading = ref(false), logError = ref('');
const dialog = ref(''), formError = ref(''), rowOriginal = ref(null), form = reactive({ name: '', column: '', columnEditor: columnEditor(), columns: [], fields: {}, indexColumns: [], indexUnique: false });
const busy = computed(() => loading.value || saving.value);
const current = computed(() => tables.value.find(t => t.name === selected.value));
const writable = computed(() => current.value?.type === 'table');
const filteredTables = computed(() => tables.value.filter(t => t.name.toLowerCase().includes(tableSearch.value.toLowerCase())));
const dataColumns = computed(() => [
    ...(structure.value?.columns.filter(c => c.hidden !== 1) || []).map((c, index) => ({ title: c.name, key: 'field-' + index, field: c.name, width: 190 })),
    ...(editable.value ? [{ title: '操作', key: 'actions', width: 145, fixed: 'right' }] : [])
]);
const structureColumns = [{ title: '字段', dataIndex: 'name', width: 180 }, { title: '类型', dataIndex: 'type', width: 140 }, { title: '主键', key: 'pk', width: 80 }, { title: '自增', key: 'auto', width: 80 }, { title: '非空', key: 'required', width: 80 }, { title: '定义 / 约束', dataIndex: 'definition', width: 300 }, { title: '操作', key: 'actions', width: 150 }];
const indexColumns = [{ title: '索引名', dataIndex: 'name', width: 250 }, { title: '字段（按索引顺序）', key: 'columns', width: 240 }, { title: '类型', key: 'kind', width: 180 }, { title: '操作', key: 'actions', width: 100 }];
const primaryKeyUnavailable = computed(() => !!structure.value?.columns.some(c => c.primaryKey && c.name !== form.column));
const autoIncrementUnavailable = computed(() => /WITHOUT\s+ROWID\s*(?:,\s*STRICT)?\s*;?$/i.test(structure.value?.sql || ''));
const logColumns = [{ title: '时间 / 操作', key: 'operation', width: 210 }, { title: 'SQL', dataIndex: 'sql', key: 'sql', width: 400 }, { title: '状态', key: 'status', width: 95 }, { title: '耗时', key: 'duration', width: 90 }, { title: '影响行数', dataIndex: 'affected_rows', width: 90 }];
const signal = new AbortController();
let tableGeneration = 0, logGeneration = 0;
const request = (method, endpoint, data, params) => api.request({ method, url: base + endpoint, data, params, signal: signal.signal });

async function fetchTable() {
    const generation = ++tableGeneration;
    if (!selected.value) { structure.value = null; rows.value = []; total.value = 0; return; }
    loading.value = true; error.value = ''; structure.value = null; rows.value = []; editable.value = false;
    try {
        const params = { table: selected.value };
        const [schema, data] = await Promise.all([
            request('get', '/structure', undefined, params),
            request('get', '/rows', undefined, { ...params, page: page.value, pageSize: pageSize.value, search: search.value, sort: sort.value, descending: descending.value })
        ]);
        if (generation !== tableGeneration) return;
        structure.value = schema.data; rows.value = data.data.rows.map((row, index) => ({ ...row, key: index })); total.value = data.data.total; editable.value = data.data.editable;
    } catch (failure) { if (generation === tableGeneration && !signal.signal.aborted) error.value = await getErrorMessage(failure, '表数据加载失败。'); }
    finally { if (generation === tableGeneration) loading.value = false; }
}
async function refresh(preferred = selected.value) {
    loading.value = true; error.value = '';
    try {
        const { data } = await request('get', '/tables'); tables.value = data;
        selected.value = data.some(t => t.name === preferred) ? preferred : data[0]?.name || '';
        await fetchTable();
    } catch (failure) { if (!signal.signal.aborted) error.value = await getErrorMessage(failure, '数据库加载失败。'); }
    finally { loading.value = false; }
}
async function selectTable(value) {
    selected.value = value; page.value = 1; search.value = ''; sort.value = undefined; descending.value = false;
    await fetchTable();
}
async function mutate(method, endpoint, payload, params = {}, preferred = selected.value) {
    saving.value = true;
    try {
        const { data } = await request(method, endpoint, payload, { table: selected.value, ...params });
        dialog.value = ''; message.success(data.message); page.value = 1; sort.value = undefined;
        await refresh(preferred); await loadTools(true);
    } catch (failure) {
        const text = await getErrorMessage(failure, '数据库操作失败。'); formError.value = text; message.error(text); throw failure;
    } finally { saving.value = false; }
}
function openDialog(kind, column = null, row = null) {
    formError.value = ''; rowOriginal.value = row;
    form.name = kind === 'rename' ? selected.value : column?.name || '';
    form.column = column?.name || ''; form.columnEditor = columnEditor(column || {}, structure.value?.indexes || []);
    form.columns = [columnEditor({ name: 'id', definition: 'INTEGER PRIMARY KEY AUTOINCREMENT' }), columnEditor({ name: 'name', definition: 'TEXT' })];
    form.indexColumns = []; form.indexUnique = false;
    if (kind === 'index') form.name = 'idx_' + selected.value + '_' + Date.now().toString(36);
    form.fields = Object.fromEntries((structure.value?.columns || []).filter(c => c.hidden === 0).map(c => [c.name, fieldEditor(row?.values[c.name], !row)]));
    dialog.value = kind;
}
async function saveDialog() {
    formError.value = '';
    try {
        if (dialog.value === 'create') {
            if (!form.name.trim()) throw new Error('请填写表名。');
            if (form.columns.filter(c => c.primaryKey && !c.advanced).length > 1) throw new Error('选择表单只支持一个主键字段；复合主键请使用 SQL 控制台。');
            await mutate('post', '/tables', { name: form.name.trim(), columns: form.columns.map(columnPayload) }, {}, form.name.trim());
        } else if (dialog.value === 'rename') {
            if (!form.name.trim()) throw new Error('请输入新表名。');
            await mutate('put', '/table', { name: form.name.trim() }, {}, form.name.trim());
        } else if (['add-column', 'edit-column'].includes(dialog.value)) {
            await mutate(dialog.value === 'add-column' ? 'post' : 'put', dialog.value === 'add-column' ? '/columns' : '/column', columnPayload(form.columnEditor), { column: form.column || undefined });
        } else if (dialog.value === 'index') {
            if (!form.name.trim() || !form.indexColumns.length) throw new Error('请填写索引名并选择至少一个字段。');
            await mutate('post', '/indexes', { name: form.name.trim(), columns: form.indexColumns, unique: form.indexUnique });
        } else {
            const values = Object.create(null);
            for (const [name, field] of Object.entries(form.fields)) { const value = fieldValue(field); if (value !== undefined) values[name] = value; }
            await mutate(rowOriginal.value ? 'put' : 'post', rowOriginal.value ? '/row' : '/rows', { values, original: rowOriginal.value?.values, rowId: rowOriginal.value?.rowId });
        }
    } catch (failure) { if (!failure.response) formError.value = failure.message; }
}
function confirmAction(title, content, action) {
    Modal.confirm({ title, content, okText: '确认执行', cancelText: '取消', okButtonProps: { danger: true }, onOk: action });
}
function dropTable() { confirmAction('删除表“' + selected.value + '”？', '将删除表结构及全部数据，操作无法撤销。', () => mutate('delete', '/table')); }
function clearTable() { confirmAction('清空表“' + selected.value + '”？', '将删除全部行，保留表结构。外键和触发器仍会生效。', () => mutate('post', '/clear')); }
function dropColumn(column) { confirmAction('删除字段“' + column.name + '”？', '该字段的数据将被永久删除。被索引或约束引用的字段需要先处理依赖。', () => mutate('delete', '/column', undefined, { column: column.name })); }
function dropIndex(index) { confirmAction('删除索引“' + index.name + '”？', '删除索引会影响查询性能；删除唯一索引后不再限制对应字段组合的重复值。', () => mutate('delete', '/index', undefined, { name: index.name })); }
function deleteRow(row) { confirmAction('删除这条数据？', '删除前将校验原始数据，防止覆盖其他页面的修改。', () => mutate('post', '/delete-row', { values: {}, original: row.values, rowId: row.rowId })); }
async function executeSql() {
    saving.value = true; sqlError.value = ''; sqlResults.value = []; const started = performance.now();
    try {
        const { data } = await request('post', '/sql', { sql: sql.value }); sqlResults.value = data.results;
        message.success('SQL 已执行并提交。'); page.value = 1; sort.value = undefined; await refresh(); await loadTools(true);
    } catch (failure) { sqlError.value = await getErrorMessage(failure, 'SQL 执行失败。'); }
    finally { sqlDuration.value = Math.round(performance.now() - started); saving.value = false; }
}
function confirmSql() {
    confirmAction('执行当前 SQL？', 'SQL 会直接作用于当前数据库，多条语句在同一事务中执行，失败时整体回滚。执行内容和结果状态将持久化留存。', executeSql);
}
async function fetchLogs(reset = false) {
    if (reset) logPage.value = 1;
    const generation = ++logGeneration; logLoading.value = true; logError.value = '';
    try {
        const { data } = await request('get', '/logs', undefined, { page: logPage.value, pageSize: 20, search: logSearch.value, status: logStatus.value });
        if (generation === logGeneration) { logRows.value = data.items; logTotal.value = data.total; }
    } catch (failure) { if (!signal.signal.aborted) logError.value = await getErrorMessage(failure, '日志加载失败。'); }
    finally { if (generation === logGeneration) logLoading.value = false; }
}
function resultColumns(result) { return result.columns.map((name, index) => ({ title: name || '(无名列)', key: String(index), index, width: 190 })); }
const dialogTitle = computed(() => ({ create: '创建数据表', rename: '修改表名', 'add-column': '新增字段', 'edit-column': '修改字段', index: '创建索引', row: rowOriginal.value ? '修改数据' : '新增数据' })[dialog.value]);
watch(tab, value => { if (value === 'logs') fetchLogs(); });
onMounted(() => refresh());
onBeforeUnmount(() => { tableGeneration++; logGeneration++; signal.abort(); });
</script>

<template>
    <section class="database-settings" aria-labelledby="database-title">
        <header class="database-heading">
            <div><p class="eyebrow">WORKSPACE DATA</p><h1 id="database-title">数据库管理</h1><p class="intro-copy">管理当前 SQLite 数据库的表结构与数据，所有管理 SQL 自动留存日志。</p></div>
            <a-space wrap><a-button :loading="loading" :disabled="saving" @click="refresh()">刷新数据库</a-button><a-button type="primary" :disabled="busy" @click="openDialog('create')">创建表</a-button></a-space>
        </header>
        <a-alert v-if="error" type="error" :message="error" show-icon><template #action><a-button size="small" :disabled="busy" @click="refresh()">重试</a-button></template></a-alert>
        <div class="database-workspace">
            <aside class="database-tables" aria-label="数据库表列表">
                <div class="table-list-heading"><strong><WorkbenchIcon name="database" /> 数据表与视图</strong><a-tag>{{ tables.length }}</a-tag></div>
                <a-input v-model:value="tableSearch" placeholder="搜索表名" aria-label="搜索表名" allow-clear />
                <div class="table-list" role="navigation" aria-label="选择数据表">
                    <button v-for="table in filteredTables" :key="table.name" type="button" :disabled="busy" :class="{ active: selected === table.name }" :aria-current="selected === table.name ? 'true' : undefined" @click="selectTable(table.name)"><WorkbenchIcon name="database" /><span>{{ table.name }}</span><small>{{ table.type === 'view' ? '视图' : '表' }}</small></button>
                    <a-empty v-if="!filteredTables.length && !loading" :description="tables.length ? '没有匹配的表' : '暂无数据表'" :image="undefined" />
                </div>
            </aside>
            <div class="database-main">
                <div class="selected-table"><div><strong>{{ selected || '选择或创建一张表' }}</strong><small>SQLite · 当前工作台数据库</small></div><a-tag v-if="current">{{ current.type === 'view' ? 'VIEW' : 'TABLE' }}</a-tag></div>
                <a-tabs v-model:active-key="tab">
                    <a-tab-pane key="data" tab="表数据">
                        <template v-if="selected">
                            <div class="database-toolbar"><a-input-search v-model:value="search" placeholder="搜索当前表所有字段" aria-label="搜索表数据" :disabled="busy" allow-clear @search="page = 1; fetchTable()" /><a-space wrap><a-button :disabled="busy || !writable || !structure" @click="openDialog('row')">新增数据</a-button><a-button danger :disabled="busy || !writable || !structure" @click="clearTable">清空表</a-button></a-space></div>
                            <div class="sort-toolbar"><span>排序字段</span><a-select v-model:value="sort" aria-label="排序字段" allow-clear placeholder="默认顺序" :disabled="busy || !structure" :options="structure?.columns.filter(c => c.hidden !== 1).map(c => ({ value: c.name, label: c.name }))" @change="page = 1; fetchTable()" /><a-switch v-model:checked="descending" checked-children="降序" un-checked-children="升序" :disabled="busy" @change="page = 1; fetchTable()" /><span>{{ total }} 行</span></div>
                            <a-alert v-if="structure && !editable" class="database-note" type="info" message="当前对象无法通过主键或 rowid 唯一定位，修改和删除请使用 SQL 控制台。" show-icon />
                            <a-table :columns="dataColumns" :data-source="rows" row-key="key" :loading="loading" :scroll="{ x: 'max-content' }" :pagination="false" size="small"><template #bodyCell="{ column, record }"><a-space v-if="column.key === 'actions'"><a-button size="small" :disabled="busy" @click="openDialog('row', null, record)">编辑</a-button><a-button size="small" danger :disabled="busy" @click="deleteRow(record)">删除</a-button></a-space><span v-else class="database-cell" :class="{ 'null-cell': record.values[column.field] === null }" :title="cellText(record.values[column.field])">{{ cellText(record.values[column.field]) }}</span></template></a-table>
                            <a-pagination v-model:current="page" v-model:page-size="pageSize" class="database-pagination" :disabled="busy" :total="total" :page-size-options="['20', '50', '100']" show-size-changer :show-total="n => `共 ${n} 行`" @change="fetchTable()" />
                        </template><a-empty v-else description="创建或选择一张表以浏览数据" />
                    </a-tab-pane>
                    <a-tab-pane key="structure" tab="表结构">
                        <template v-if="selected"><div class="database-toolbar"><span class="muted">修改字段时保留其他字段和表级约束。</span><a-space wrap><a-button :disabled="busy || !writable || !structure" @click="openDialog('add-column')">新增字段</a-button><a-button :disabled="busy || !writable || !structure" @click="openDialog('rename')">修改表名</a-button><a-button danger :disabled="busy || !writable || !structure" @click="dropTable">删除表</a-button></a-space></div>
                            <a-table :columns="structureColumns" :data-source="structure?.columns || []" row-key="cid" :loading="loading" :scroll="{ x: 1010 }" :pagination="false" size="small"><template #bodyCell="{ column, record }"><template v-if="column.key === 'pk'">{{ record.primaryKey ? `PK ${record.primaryKey}` : '—' }}</template><template v-else-if="column.key === 'auto'">{{ columnEditor(record).autoIncrement ? '是' : '否' }}</template><template v-else-if="column.key === 'required'">{{ record.notNull ? '是' : '否' }}</template><a-space v-else-if="column.key === 'actions'"><a-button size="small" :disabled="busy || !writable || !record.definition" @click="openDialog('edit-column', record)">修改</a-button><a-button size="small" danger :disabled="busy || !writable" @click="dropColumn(record)">删除</a-button></a-space></template></a-table>
                            <div class="database-toolbar"><strong>索引</strong><a-button :disabled="busy || !writable || !structure" @click="openDialog('index')">创建索引</a-button></div>
                            <a-table :columns="indexColumns" :data-source="structure?.indexes || []" row-key="name" :loading="loading" :scroll="{ x: 770 }" :pagination="false" size="small"><template #bodyCell="{ column, record }"><template v-if="column.key === 'columns'">{{ record.columns.map(c => c ?? '(表达式)').join(' → ') }}</template><template v-else-if="column.key === 'kind'">{{ record.origin === 'pk' ? '主键约束' : record.origin === 'u' ? '唯一约束' : record.unique ? '唯一索引' : '普通索引' }}{{ record.partial ? ' · 部分索引' : '' }}</template><a-button v-else-if="column.key === 'actions'" size="small" danger :disabled="busy || record.origin !== 'c'" @click="dropIndex(record)">删除</a-button></template></a-table>
                            <details class="database-ddl" open><summary>建表 SQL</summary><pre>{{ structure?.sql }}</pre></details>
                        </template><a-empty v-else description="请选择一张表查看结构" />
                    </a-tab-pane>
                    <a-tab-pane key="sql" tab="SQL 控制台">
                        <a-alert type="info" show-icon message="支持多条 SQLite 语句，失败整体回滚。每个结果集最多展示 1000 行，执行限时 30 秒。" description="事务由系统管理；不支持 BEGIN / COMMIT / SAVEPOINT、ATTACH / DETACH 及需要事务外执行的 VACUUM。" />
                        <label class="sql-label" for="database-sql">SQL 语句</label><a-textarea id="database-sql" v-model:value="sql" class="sql-editor" :rows="9" :maxlength="100000" :disabled="saving" spellcheck="false" @keydown.ctrl.enter.prevent="!busy && sql.trim() && confirmSql()" @keydown.meta.enter.prevent="!busy && sql.trim() && confirmSql()" />
                        <div class="database-toolbar"><span class="muted">Ctrl / ⌘ + Enter 执行 · 日志包含 SQL 和绑定参数</span><a-button type="primary" :loading="saving" :disabled="busy || !sql.trim()" @click="confirmSql">执行 SQL</a-button></div>
                        <a-alert v-if="sqlError" type="error" :message="sqlError" show-icon />
                        <p v-if="sqlDuration !== null" class="muted" role="status">本次请求耗时 {{ sqlDuration }} ms · {{ sqlError ? '执行失败，请查看错误及日志' : '已提交' }}</p>
                        <div v-for="(result, index) in sqlResults" :key="index" class="sql-result"><strong>结果 {{ index + 1 }}</strong><span class="muted"> · {{ result.columns.length ? `${result.rows.length} 行` : `影响 ${Math.max(0, result.affectedRows)} 行` }}</span><a-tag v-if="result.truncated" color="orange">仅展示前 1000 行</a-tag><a-table v-if="result.columns.length" :columns="resultColumns(result)" :data-source="result.rows.map((values, key) => ({ values, key }))" row-key="key" :scroll="{ x: 'max-content' }" :pagination="{ pageSize: 20, showSizeChanger: false }" size="small"><template #bodyCell="{ column, record }"><span class="database-cell" :title="cellText(record.values[column.index])">{{ cellText(record.values[column.index]) }}</span></template></a-table></div>
                    </a-tab-pane>
                    <a-tab-pane key="logs" tab="操作日志">
                        <a-alert type="info" show-icon message="SQL 日志独立保存，包含读写、表结构查询、事务及失败语句，删除业务表或导入备份不会清除日志。" description="同一请求 ID 可关联一个操作的多条 SQL。success 表示该语句执行成功，最终是否提交请结合同请求的 COMMIT / ROLLBACK 查看。running 表示结果尚未记录（执行中或服务曾中断）。" />
                        <div class="database-toolbar log-toolbar"><a-input-search v-model:value="logSearch" placeholder="搜索 SQL、参数、操作或错误" aria-label="搜索 SQL 日志" allow-clear @search="fetchLogs(true)" /><a-select v-model:value="logStatus" aria-label="日志状态" :options="[{value:'',label:'全部状态'},{value:'success',label:'成功'},{value:'failed',label:'失败'},{value:'running',label:'执行中 / 未知'}]" @change="fetchLogs(true)" /><a-button :loading="logLoading" @click="fetchLogs()">刷新日志</a-button></div>
                        <a-alert v-if="logError" type="error" :message="logError" show-icon />
                        <a-table :columns="logColumns" :data-source="logRows" row-key="id" :loading="logLoading" :scroll="{ x: 900 }" :pagination="false" size="small"><template #bodyCell="{ column, record }"><template v-if="column.key === 'operation'"><div>{{ new Date(record.started_at).toLocaleString() }}</div><small>{{ record.operation }} · #{{ record.id }}</small></template><code v-else-if="column.key === 'sql'" class="log-sql" :title="record.sql">{{ record.sql }}</code><a-tag v-else-if="column.key === 'status'" :color="record.status === 'success' ? 'green' : record.status === 'failed' ? 'red' : 'orange'">{{ {success:'成功',failed:'失败',running:'未完成'}[record.status] }}</a-tag><template v-else-if="column.key === 'duration'">{{ record.duration_ms === null ? '—' : `${record.duration_ms} ms` }}</template></template><template #expandedRowRender="{ record }"><div class="log-details"><p>请求 ID：{{ record.request_id || '—' }} · 来源：{{ record.actor || '—' }}</p><strong>完整 SQL</strong><pre>{{ record.sql }}</pre><strong>绑定参数</strong><pre>{{ record.parameters }}</pre><a-alert v-if="record.error" type="error" :message="record.error" show-icon /></div></template></a-table>
                        <a-pagination v-model:current="logPage" class="database-pagination" :total="logTotal" :page-size="20" :show-size-changer="false" :show-total="n => `共 ${n} 条`" @change="fetchLogs()" />
                    </a-tab-pane>
                </a-tabs>
            </div>
        </div>
    </section>
    <a-modal :open="!!dialog" :title="dialogTitle" :width="760" :confirm-loading="saving" :cancel-button-props="{ disabled: saving }" :closable="!saving" :keyboard="!saving" :mask-closable="false" ok-text="保存" cancel-text="取消" @ok="saveDialog" @cancel="dialog = ''">
        <a-alert v-if="formError" class="database-note" type="error" :message="formError" show-icon />
        <a-form layout="vertical" :disabled="saving" class="database-form">
            <a-form-item v-if="['create','rename','index'].includes(dialog)" :label="dialog === 'index' ? '索引名' : '表名'" required><a-input v-model:value="form.name" :aria-label="dialog === 'index' ? '索引名' : '表名'" :maxlength="255" /></a-form-item>
            <template v-if="dialog === 'create'"><div v-for="(column, index) in form.columns" :key="index" class="create-column"><div class="database-toolbar"><strong>字段 {{ index + 1 }}</strong><a-button danger :disabled="saving || form.columns.length === 1" :aria-label="`移除字段 ${index + 1}`" @click="form.columns.splice(index, 1)">移除</a-button></div><DatabaseColumnEditor v-model="form.columns[index]" :label="`字段 ${index + 1} `" :disabled="saving" :primary-key-unavailable="form.columns.some((c, i) => i !== index && c.primaryKey)" /></div><a-button :disabled="saving || form.columns.length >= 200" @click="form.columns.push(columnEditor())">添加字段</a-button></template>
            <DatabaseColumnEditor v-if="['add-column','edit-column'].includes(dialog)" v-model="form.columnEditor" :disabled="saving" :primary-key-unavailable="primaryKeyUnavailable" :auto-increment-unavailable="autoIncrementUnavailable" />
            <template v-if="dialog === 'index'"><a-form-item label="索引字段（按选择顺序排列）" required extra="选择多个字段创建复合索引。"><a-select v-model:value="form.indexColumns" aria-label="索引字段" mode="multiple" :options="structure?.columns.filter(c => c.hidden !== 1).map(c => ({ value: c.name, label: c.name }))" /></a-form-item><a-form-item label="索引类型"><a-radio-group v-model:value="form.indexUnique" aria-label="索引类型"><a-radio :value="false">普通索引</a-radio><a-radio :value="true">唯一索引</a-radio></a-radio-group></a-form-item></template>
            <a-alert v-if="dialog === 'edit-column'" type="info" show-icon message="修改名称会自动更新依赖引用；修改类型或约束会事务重建表，校验失败自动回滚。" />
            <template v-if="dialog === 'row'"><p class="muted">“省略 / 默认值”在新增时使用数据库默认值，修改时保留原值。空文本与 NULL 分开保存。</p><div v-for="(field, name) in form.fields" :key="name" class="row-field"><label :for="'db-field-' + name">{{ name }}<small>{{ structure?.columns.find(c => c.name === name)?.type }}</small></label><a-select v-model:value="field.mode" :aria-label="name + ' 数据类型'" :options="valueModes" /><a-textarea :id="'db-field-' + name" v-model:value="field.value" :disabled="saving || ['null','default'].includes(field.mode)" :auto-size="{minRows:1,maxRows:5}" :placeholder="field.mode === 'default' ? '使用默认值 / 保留原值' : field.mode === 'null' ? 'NULL' : '输入值（空文本也会保存）'" /></div></template>
        </a-form>
    </a-modal>
</template>

<style scoped>
.database-settings { display: grid; gap: 20px; width: 100%; max-width: 1600px; margin-inline: auto; }
.database-heading, .database-toolbar, .selected-table, .table-list-heading { display: flex; align-items: center; justify-content: space-between; gap: 16px; }
.database-heading h1 { margin: 4px 0 12px; font-size: 30px; color: var(--text); }
.intro-copy { margin: 0; }
.database-workspace { display: grid; grid-template-columns: 235px minmax(0, 1fr); border: 1px solid var(--border); border-radius: 18px; background: var(--surface); overflow: hidden; }
.database-tables { padding: 22px 14px; border-right: 1px solid var(--border); min-width: 0; }
.table-list-heading { margin-bottom: 18px; gap: 6px; color: var(--text); }
.table-list-heading strong { display: flex; align-items: center; gap: 8px; }
.table-list { display: grid; align-content: start; gap: 6px; margin-top: 14px; }
.table-list button { display: flex; align-items: center; gap: 8px; width: 100%; padding: 12px 10px; min-height: 44px; text-align: left; border: 1px solid transparent; border-radius: 8px; background: transparent; color: var(--text-muted); cursor: pointer; }
.table-list button.active, .table-list button:hover:not(:disabled) { background: var(--accent-soft); color: var(--accent-strong); border-color: var(--border); }
.table-list button:disabled { cursor: wait; opacity: .65; }
.table-list button span { overflow-wrap: anywhere; flex: 1; min-width: 0; }
.table-list button small { white-space: nowrap; }
.table-list button:focus-visible { outline: 2px solid var(--accent-strong); outline-offset: 2px; }
.database-main { padding: 22px 24px; min-width: 0; }
.selected-table strong { display: block; font: 600 20px Consolas, monospace; overflow-wrap: anywhere; color: var(--text); }
.selected-table small { display: block; margin-top: 8px; color: var(--text-muted); }
.database-toolbar { margin: 14px 0 20px; flex-wrap: wrap; }
.database-toolbar .ant-input-search { flex: 1; min-width: 180px; max-width: 380px; }
.sort-toolbar { display: flex; align-items: center; flex-wrap: wrap; gap: 12px; margin-bottom: 20px; color: var(--text-muted); font-size: 13px; }
.sort-toolbar .ant-select { width: 180px; }
.database-pagination { margin-top: 20px; display: flex; justify-content: flex-end; flex-wrap: wrap; gap: 8px; }
.database-cell { display: block; max-width: 340px; overflow: hidden; text-overflow: ellipsis; white-space: pre; font-family: Consolas, monospace; }
.null-cell { color: var(--text-muted); font-style: italic; }
.muted, .log-details p { color: var(--text-muted); font-size: 13px; }
.database-note { margin-bottom: 16px; }
.sql-label { display: block; margin: 18px 0 8px; color: var(--text); font-weight: 600; }
.sql-editor, pre, code { font-family: Consolas, 'Courier New', monospace; }
.sql-editor { tab-size: 4; line-height: 1.65; }
.sql-result { margin-top: 20px; color: var(--text); }
.sql-result .ant-table-wrapper { margin-top: 12px; }
.database-ddl { margin-top: 22px; color: var(--text); }
.database-ddl summary { cursor: pointer; font-weight: 600; }
pre { white-space: pre-wrap; overflow-wrap: anywhere; padding: 14px; background: var(--accent-soft); border-radius: 8px; max-height: 400px; overflow: auto; }
.log-toolbar .ant-select { width: 160px; }
.log-sql { display: -webkit-box; -webkit-line-clamp: 2; -webkit-box-orient: vertical; overflow: hidden; overflow-wrap: anywhere; }
.database-form { padding-top: 18px; }
.create-column { padding: 16px; margin-bottom: 18px; border: 1px solid var(--border); border-radius: 12px; }
.create-column > .database-toolbar { margin-top: 0; color: var(--text); }
.row-field { display: grid; grid-template-columns: minmax(100px, 1fr) 145px 2fr; align-items: start; gap: 12px; margin: 18px 0; }
.row-field label { color: var(--text); overflow-wrap: anywhere; padding-top: 4px; }
.row-field label small { display: block; color: var(--text-muted); margin-top: 3px; }
@media (max-width: 900px) { .database-workspace { grid-template-columns: minmax(0,1fr); } .database-tables { border-right: 0; border-bottom: 1px solid var(--border); } .table-list { display: flex; flex-wrap: wrap; max-height: 190px; overflow: auto; } .table-list button { width: auto; max-width: 100%; } .database-heading { align-items: flex-start; flex-direction: column; } }
@media (max-width: 600px) { .database-main { padding: 16px 12px; } .database-toolbar { align-items: flex-start; gap: 12px; } .row-field { grid-template-columns: 1fr 145px; } .row-field .ant-input { grid-column: 1 / -1; } .create-column { padding: 12px; } }
</style>
