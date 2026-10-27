<script setup>
import { computed, onBeforeUnmount, onMounted, reactive, ref, watch } from 'vue';
import { onBeforeRouteLeave } from 'vue-router';
import { Modal, message } from 'ant-design-vue';
import ToolIntro from '../../components/ToolIntro.vue';
import WorkbenchIcon from '../../components/WorkbenchIcon.vue';
import { api, getErrorMessage } from '../../api/client.js';
import { copyWithFeedback } from '../../utils/clipboard.js';
import { detectLineEnding, diskSize, fileContentUrl, fileDate, normalizeNewlines, pathCrumbs, restoreNewlines } from '../../utils/fileManager.js';

const capabilities = ref({ roots: [], readOnly: true, maxTextBytes: 2097152, machineName: '' });
const directory = ref({ path: '', parentPath: null, entries: [], total: 0, skipped: 0 });
const address = ref(''), search = ref(''), showHidden = ref(false), sort = ref('name'), descending = ref(false);
const page = ref(1), pageSize = 50, loading = ref(false), loadError = ref(''), initialized = ref(false);
const selected = ref(null), detail = ref(null), detailLoading = ref(false), detailError = ref(''), detailOpen = ref(false);
const createOpen = ref(false), creating = ref(false), createDirectory = ref(false), createName = ref(''), createError = ref('');
const previewOpen = ref(false), opened = ref(null), previewLoading = ref(false), previewError = ref(''), mediaError = ref('');
const textData = ref(null), editing = ref(false), saving = ref(false), content = ref(''), encoding = ref('utf-8'), hasBom = ref(false), lineEnding = ref('LF');
const retryEncoding = ref('gb18030');
const original = reactive({ content: '', encoding: '', hasBom: false, lineEnding: 'LF' });
let listAbort, detailAbort, textAbort, listRevision = 0, detailRevision = 0, textRevision = 0, searchTimer, suppressSearch = false;
const dirty = computed(() => Boolean(textData.value && editing.value && (content.value !== original.content || encoding.value !== original.encoding || hasBom.value !== original.hasBom || lineEnding.value !== original.lineEnding)));
const crumbs = computed(() => pathCrumbs(directory.value.path, capabilities.value.roots));
const currentRoot = computed(() => crumbs.value[0]?.path);
const canWrite = computed(() => initialized.value && !capabilities.value.readOnly && !loadError.value && Boolean(directory.value.path));
const canEdit = computed(() => textData.value && !textData.value.isReadOnly && !capabilities.value.readOnly);
const encodingOptions = [
    { value: 'utf-8', label: 'UTF-8' }, { value: 'utf-16', label: 'UTF-16 LE' }, { value: 'utf-16be', label: 'UTF-16 BE' },
    { value: 'utf-32', label: 'UTF-32 LE' }, { value: 'utf-32be', label: 'UTF-32 BE' }, { value: 'gb18030', label: 'GB18030 / GBK' }
];
const lineOptions = ['LF', 'CRLF', 'CR'].map(value => ({ value, label: value }));

/** 请求序号加取消信号，防止快速换盘、搜索时旧响应覆盖当前目录。 */
async function loadDirectory(path = directory.value.path, reset = false) {
    if (!path) return;
    if (reset) page.value = 1;
    const revision = ++listRevision;
    listAbort?.abort(); listAbort = new AbortController();
    loading.value = true; loadError.value = '';
    try {
        const { data } = await api.get('/api/files/list', { signal: listAbort.signal, params: {
            path, search: search.value, showHidden: showHidden.value, sort: sort.value,
            descending: descending.value, offset: (page.value - 1) * pageSize, limit: pageSize
        } });
        if (revision !== listRevision) return;
        if (data.total > 0 && data.offset >= data.total) {
            page.value = Math.ceil(data.total / pageSize);
            return await loadDirectory(path);
        }
        directory.value = data; address.value = data.path;
        if (!data.entries.some(entry => entry.path === selected.value?.path)) selected.value = null;
    } catch (error) {
        if (revision === listRevision && error.code !== 'ERR_CANCELED') loadError.value = await getErrorMessage(error);
    } finally { if (revision === listRevision) loading.value = false; }
}

async function initialize() {
    loading.value = true; loadError.value = '';
    try {
        capabilities.value = (await api.get('/api/files/roots')).data;
        initialized.value = true;
        if (capabilities.value.roots.length) await loadDirectory(capabilities.value.roots[0].path, true);
        else loadError.value = '没有已就绪的磁盘或已配置的目录，请检查后端配置。';
    } catch (error) { loadError.value = await getErrorMessage(error); }
    finally { loading.value = false; }
}

function resetSearch() {
    // 清空搜索属于换目录的一部分，不再调度旧目录请求，避免慢网络时把新目录切回去。
    suppressSearch = true; search.value = ''; suppressSearch = false; clearTimeout(searchTimer);
}
function navigate(path) {
    resetSearch(); selected.value = null;
    loadDirectory(path, true);
}
watch(search, () => {
    clearTimeout(searchTimer);
    if (suppressSearch) return;
    searchTimer = setTimeout(() => loadDirectory(directory.value.path, true), 300);
}, { flush: 'sync' });
watch([showHidden, sort, descending], () => loadDirectory(directory.value.path, true));
watch(encoding, value => {
    if (value === 'gb18030') hasBom.value = false;
    // 转换为 UTF-16/32 时默认带 BOM，方便其他软件及下次打开时正确识别。
    else if (value !== original.encoding && value.startsWith('utf-') && value !== 'utf-8') hasBom.value = true;
});

async function showDetails(entry) {
    selected.value = entry; detail.value = entry; detailOpen.value = true; detailError.value = '';
    const revision = ++detailRevision;
    detailAbort?.abort(); detailAbort = new AbortController(); detailLoading.value = true;
    try {
        const { data } = await api.get('/api/files/details', { params: { path: entry.path }, signal: detailAbort.signal });
        if (revision === detailRevision) detail.value = data;
    } catch (error) {
        if (revision === detailRevision && error.code !== 'ERR_CANCELED') detailError.value = await getErrorMessage(error);
    } finally { if (revision === detailRevision) detailLoading.value = false; }
}
function closeDetails() { detailOpen.value = false; detailRevision++; detailAbort?.abort(); }

function beginCreate(isDirectory) {
    createDirectory.value = isDirectory; createName.value = ''; createError.value = ''; createOpen.value = true;
}
async function createEntry() {
    if (creating.value) return;
    if (!createName.value.trim()) { createError.value = '请输入名称。'; return; }
    creating.value = true; createError.value = '';
    try {
        const { data } = await api.post('/api/files/entries', { parentPath: directory.value.path, name: createName.value, isDirectory: createDirectory.value });
        createOpen.value = false; message.success(createDirectory.value ? '文件夹已创建。' : '文件已创建。');
        resetSearch(); await loadDirectory(directory.value.path, true); selected.value = data;
        if (!data.isDirectory) await openEntry(data, true);
    } catch (error) { createError.value = await getErrorMessage(error); }
    finally { creating.value = false; }
}

/** 用户有未保存改动时才询问；冲突时保留编辑内容，不提供直接覆盖按钮。 */
function confirmDiscard() {
    if (saving.value) return Promise.resolve(false);
    if (!dirty.value) return Promise.resolve(true);
    return new Promise(resolve => Modal.confirm({ title: '放弃未保存的修改？', content: '当前修改尚未写入磁盘。可以取消后先保存或复制内容。',
        okText: '放弃修改', cancelText: '继续编辑', onOk: () => resolve(true), onCancel: () => resolve(false) }));
}
async function closePreview() {
    if (!await confirmDiscard()) return;
    previewOpen.value = false; editing.value = false; textRevision++; textAbort?.abort(); opened.value = null; textData.value = null;
}
async function openEntry(entry, edit = false) {
    if (entry.isLink) { message.info('请使用该链接指向的实际磁盘路径。'); return; }
    if (entry.isDirectory) { navigate(entry.path); return; }
    if (!await confirmDiscard()) return;
    textRevision++; textAbort?.abort(); selected.value = entry; opened.value = entry; previewOpen.value = true;
    textData.value = null; editing.value = false; previewError.value = ''; mediaError.value = ''; previewLoading.value = false;
    if (entry.previewKind === 'text') await loadText(undefined, edit);
}
async function loadText(readEncoding, edit = editing.value) {
    const revision = ++textRevision;
    textAbort?.abort(); textAbort = new AbortController(); previewLoading.value = true; previewError.value = '';
    try {
        const { data } = await api.get('/api/files/text', { params: { path: opened.value.path, encoding: readEncoding }, signal: textAbort.signal });
        if (revision !== textRevision) return;
        textData.value = data; content.value = normalizeNewlines(data.content); encoding.value = data.encoding;
        hasBom.value = data.hasBom; lineEnding.value = detectLineEnding(data.content);
        Object.assign(original, { content: content.value, encoding: data.encoding, hasBom: data.hasBom, lineEnding: lineEnding.value });
        editing.value = edit && !data.isReadOnly && !capabilities.value.readOnly;
    } catch (error) {
        if (revision === textRevision && error.code !== 'ERR_CANCELED') previewError.value = await getErrorMessage(error);
    } finally { if (revision === textRevision) previewLoading.value = false; }
}
async function reloadText(readEncoding = textData.value?.encoding) {
    if (await confirmDiscard()) await loadText(readEncoding);
}
async function saveText() {
    if (!dirty.value || saving.value || !canEdit.value) return;
    saving.value = true; previewError.value = '';
    try {
        const { data } = await api.put('/api/files/text', { path: opened.value.path, content: restoreNewlines(content.value, lineEnding.value),
            version: textData.value.version, encoding: encoding.value, hasBom: hasBom.value });
        textData.value = data;
        Object.assign(original, { content: content.value, encoding: encoding.value, hasBom: data.hasBom, lineEnding: lineEnding.value });
        hasBom.value = data.hasBom; message.success('文件已保存到磁盘。'); await loadDirectory();
    } catch (error) { previewError.value = await getErrorMessage(error); }
    finally { saving.value = false; }
}
function editorKeydown(event) {
    if ((event.ctrlKey || event.metaKey) && event.key.toLowerCase() === 's' && previewOpen.value && editing.value) {
        event.preventDefault(); saveText();
    }
}
function beforeUnload(event) { if (dirty.value || saving.value) { event.preventDefault(); event.returnValue = ''; } }
function mediaFailed() { mediaError.value = '浏览器无法预览此文件或文件已不可用。可下载原文件；更多视频格式可使用视频播放器。'; }
onBeforeRouteLeave(confirmDiscard);
onMounted(() => { initialize(); window.addEventListener('beforeunload', beforeUnload); window.addEventListener('keydown', editorKeydown); });
onBeforeUnmount(() => {
    clearTimeout(searchTimer); listAbort?.abort(); detailAbort?.abort(); textAbort?.abort();
    window.removeEventListener('beforeunload', beforeUnload); window.removeEventListener('keydown', editorKeydown);
});
</script>

<template>
    <section class="file-manager">
        <ToolIntro title="磁盘文件管理" eyebrow="FILES / EXPLORER" description="浏览服务器磁盘，查看文件信息，预览内容并在线编辑文本。" />
        <section class="panel explorer-panel">
            <div class="explorer-heading">
                <div class="explorer-machine"><span class="explorer-symbol"><WorkbenchIcon name="folder" /></span><div><h2>服务器文件</h2><p>{{ capabilities.machineName || '正在连接…' }} <span v-if="initialized">· {{ capabilities.readOnly ? '只读模式' : '可读写' }}</span></p></div></div>
                <div class="utility-toolbar"><a-button :disabled="!canWrite || loading" @click="beginCreate(true)">新建文件夹</a-button><a-button type="primary" :disabled="!canWrite || loading" @click="beginCreate(false)">新建文件</a-button></div>
            </div>
            <div v-if="capabilities.roots.length" class="drive-strip" aria-label="磁盘列表">
                <button v-for="root in capabilities.roots" :key="root.path" type="button" class="drive-card" :class="{ active: currentRoot === root.path }" @click="navigate(root.path)">
                    <WorkbenchIcon name="drive" /><span><strong>{{ root.name }}</strong><small v-if="root.totalBytes != null">{{ diskSize(root.freeBytes) }} 可用 / {{ diskSize(root.totalBytes) }}</small><small v-else>打开目录</small></span>
                    <span v-if="root.totalBytes > 0" class="drive-meter" aria-hidden="true"><i :style="{ width: `${Math.max(0, Math.min(100, (1 - root.freeBytes / root.totalBytes) * 100))}%` }" /></span>
                </button>
            </div>
            <form class="address-bar" @submit.prevent="loadDirectory(address, true)">
                <a-button :disabled="!directory.parentPath || loading" aria-label="返回上级目录" @click="navigate(directory.parentPath)"><WorkbenchIcon name="arrow-up" /></a-button>
                <label class="path-input"><span class="sr-only">完整目录路径</span><a-input v-model:value="address" aria-label="完整目录路径" placeholder="输入服务器上的完整目录路径" :disabled="!initialized" /></label>
                <a-button html-type="submit" :disabled="!address || loading">前往</a-button>
                <a-button aria-label="刷新目录" :loading="loading" @click="initialized ? loadDirectory() : initialize()"><WorkbenchIcon name="refresh" /></a-button>
            </form>
            <nav v-if="crumbs.length" class="file-breadcrumbs" aria-label="目录路径"><template v-for="(crumb, index) in crumbs" :key="crumb.path"><span v-if="index" aria-hidden="true">/</span><button type="button" :aria-current="index === crumbs.length - 1 ? 'location' : undefined" @click="navigate(crumb.path)">{{ crumb.name }}</button></template></nav>
            <div class="explorer-filters">
                <label class="file-search"><span class="sr-only">搜索当前目录</span><a-input v-model:value="search" aria-label="搜索当前目录" placeholder="搜索当前目录的文件名称" allow-clear :maxlength="200"><template #prefix><WorkbenchIcon name="search" /></template></a-input></label>
                <label for="file-sort" class="sr-only">排序字段</label><a-select id="file-sort" v-model:value="sort" :options="[{ value: 'name', label: '按名称' }, { value: 'modified', label: '按修改时间' }, { value: 'size', label: '按大小' }, { value: 'type', label: '按类型' }]" />
                <a-button @click="descending = !descending">{{ descending ? '降序 ↓' : '升序 ↑' }}</a-button><a-checkbox v-model:checked="showHidden">显示隐藏文件</a-checkbox>
            </div>
            <a-alert v-if="loadError" :message="loadError" class="explorer-alert" type="error" show-icon role="alert"><template #action><a-button size="small" @click="initialized ? loadDirectory(address || directory.path) : initialize()">重试</a-button></template></a-alert>
            <a-alert v-if="directory.skipped" :message="`${directory.skipped} 个项目因权限限制或已消失而跳过。`" class="explorer-alert" type="info" show-icon />
            <a-spin :spinning="loading">
                <div class="file-table-scroll" :aria-busy="loading">
                    <table class="file-table"><thead><tr><th scope="col">名称</th><th scope="col">大小</th><th scope="col">类型</th><th scope="col">修改时间</th><th scope="col">操作</th></tr></thead>
                        <tbody><tr v-for="entry in directory.entries" :key="entry.path" :class="{ selected: selected?.path === entry.path }" @click="selected = entry">
                            <td><button class="file-name" type="button" :title="entry.name" @click.stop="openEntry(entry)"><span class="entry-icon" :class="{ folder: entry.isDirectory }"><WorkbenchIcon :name="entry.isDirectory ? 'folder' : entry.previewKind === 'image' ? 'image' : entry.previewKind === 'video' ? 'video' : 'files'" /></span><span>{{ entry.name }}</span><small v-if="entry.isLink">链接</small><small v-else-if="entry.isReadOnly">只读</small></button></td>
                            <td>{{ entry.isDirectory ? '—' : diskSize(entry.size) }}</td><td>{{ entry.isDirectory ? '文件夹' : entry.extension ? `${entry.extension.slice(1).toUpperCase()} 文件` : '文件' }}</td><td class="date-cell">{{ fileDate(entry.modifiedUtc) }}</td>
                            <td><div class="row-actions"><a-button size="small" :aria-label="`查看 ${entry.name} 的详情`" @click.stop="showDetails(entry)">详情</a-button><a-button v-if="!entry.isDirectory && entry.previewKind === 'text' && !entry.isReadOnly && !entry.isLink" size="small" :aria-label="`编辑 ${entry.name}`" @click.stop="openEntry(entry, true)">编辑</a-button></div></td>
                        </tr></tbody>
                    </table>
                    <div v-if="!loading && !directory.entries.length" class="file-empty"><WorkbenchIcon name="folder" /><strong>{{ search ? '没有匹配的文件' : loadError ? '暂时无法读取目录' : '这个文件夹是空的' }}</strong><span>{{ search ? '搜索仅查找当前目录，不递归子文件夹。' : '可以新建文件夹或文件，也可以前往其他目录。' }}</span></div>
                </div>
            </a-spin>
            <div class="explorer-footer"><span role="status">{{ directory.total }} 个项目 <template v-if="selected">· 已选：{{ selected.name }}</template></span><a-pagination :current="page" :total="directory.total" :page-size="pageSize" :show-size-changer="false" size="small" :show-less-items="true" @change="value => { page = value; loadDirectory(); }" /></div>
        </section>
        <p class="utility-note">操作的是 API 服务器上的文件，访问范围和读写权限由后端配置与系统权限决定。在线编辑上限 {{ diskSize(capabilities.maxTextBytes) }}；文件夹大小不递归统计。</p>

        <a-modal :open="createOpen" :title="createDirectory ? '新建文件夹' : '新建文件'" :confirm-loading="creating" :mask-closable="!creating" :closable="!creating" :cancel-button-props="{ disabled: creating }" ok-text="创建" cancel-text="取消" @ok="createEntry" @cancel="createOpen = false">
            <p class="dialog-path">位置：{{ directory.path }}</p><label class="utility-field">{{ createDirectory ? '文件夹名称' : '文件名称（含扩展名）' }}<a-input v-model:value="createName" :maxlength="255" :disabled="creating" :placeholder="createDirectory ? '例如：项目文档' : '例如：notes.txt'" aria-label="新建项目名称" @press-enter="createEntry" /></label>
            <p v-if="!createDirectory" class="utility-note">创建空文件后，可立即在线编辑文本内容。</p><a-alert v-if="createError" :message="createError" type="error" show-icon role="alert" class="explorer-alert" />
        </a-modal>

        <a-drawer :open="detailOpen" title="详细信息" placement="right" :width="'min(460px, 100vw)'" @close="closeDetails">
            <a-spin :spinning="detailLoading"><template v-if="detail"><div class="details-title"><WorkbenchIcon :name="detail.isDirectory ? 'folder' : 'files'" /><h3>{{ detail.name }}</h3></div><a-alert v-if="detailError" :message="detailError" type="error" show-icon role="alert" />
                <dl class="file-details"><dt>完整路径</dt><dd>{{ detail.path }} <a-button size="small" @click="copyWithFeedback(detail.path, '路径已复制。')">复制路径</a-button></dd><dt>类型</dt><dd>{{ detail.isDirectory ? '文件夹' : detail.extension || '无扩展名文件' }}</dd><dt>大小</dt><dd>{{ detail.isDirectory ? '不递归统计目录大小' : `${diskSize(detail.size)}${detail.size == null ? '' : `（${detail.size.toLocaleString()} 字节）`}` }}</dd><dt>创建时间</dt><dd>{{ fileDate(detail.createdUtc) }}</dd><dt>修改时间</dt><dd>{{ fileDate(detail.modifiedUtc) }}</dd><dt>文件属性</dt><dd>{{ detail.attributes }}</dd><dt>读写状态</dt><dd>{{ detail.isReadOnly ? '只读' : '可尝试写入（仍受系统权限约束）' }}</dd><dt>链接</dt><dd>{{ detail.isLink ? '符号链接 / 目录联接' : '否' }}</dd></dl>
                <a-button v-if="!detail.isDirectory && !detail.isLink" type="primary" @click="openEntry(detail); closeDetails()">预览文件</a-button>
            </template></a-spin>
        </a-drawer>

        <a-modal :open="previewOpen" :title="opened?.name || '文件预览'" width="calc(100vw - var(--file-preview-gutter, 32px))" :footer="null" :mask-closable="false" :closable="!saving" :keyboard="!saving" :destroy-on-close="true" wrap-class-name="file-preview-modal" @cancel="closePreview">
            <template v-if="opened"><p class="dialog-path preview-path" :title="opened.path">{{ opened.path }}</p><div class="preview-toolbar">
                <a-button size="small" :href="fileContentUrl(opened.path, true)" target="_blank" rel="noopener">下载原文件</a-button><a-button size="small" @click="copyWithFeedback(opened.path, '路径已复制。')">复制路径</a-button>
                <template v-if="textData"><a-button v-if="!editing && canEdit" size="small" type="primary" @click="editing = true">在线编辑</a-button><a-button size="small" :disabled="saving || previewLoading" @click="reloadText()">重新加载</a-button><a-button v-if="editing" size="small" type="primary" :disabled="!dirty" :loading="saving" @click="saveText">保存文件</a-button><span class="edit-status" role="status">{{ dirty ? '有未保存的修改' : editing ? '已保存' : '文本预览' }}</span></template>
            </div>
            <a-alert v-if="previewError" :message="previewError" type="error" show-icon role="alert" class="explorer-alert" />
            <div v-if="previewError && !textData && opened.previewKind === 'text'" class="utility-toolbar utility-gap"><div class="retry-encoding"><label for="file-read-encoding">读取编码</label><a-select id="file-read-encoding" v-model:value="retryEncoding" :options="encodingOptions" /></div><a-button :disabled="previewLoading" @click="loadText(retryEncoding, true)">按所选编码重新打开</a-button></div>
            <a-spin wrapper-class-name="preview-spin" :spinning="previewLoading"><div class="preview-body">
                <template v-if="textData">
                    <div class="editor-settings">
                        <div class="setting-field"><label for="file-save-encoding">保存编码</label><a-select id="file-save-encoding" v-model:value="encoding" size="small" :options="encodingOptions" :disabled="!editing || saving" /></div>
                        <div class="setting-field"><label for="file-line-ending">换行方式</label><a-select id="file-line-ending" v-model:value="lineEnding" size="small" :options="lineOptions" :disabled="!editing || saving" /></div>
                        <a-checkbox v-model:checked="hasBom" :disabled="!editing || saving || encoding === 'gb18030'">BOM</a-checkbox><span>{{ content.length.toLocaleString() }} 字符 · {{ content.split('\n').length.toLocaleString() }} 行</span>
                    </div>
                    <a-textarea v-if="editing" v-model:value="content" class="file-editor utility-code" aria-label="文件编辑内容" :rows="20" :disabled="saving" spellcheck="false" />
                    <pre v-else class="file-text-preview utility-code" tabindex="0">{{ content || '（空文件）' }}</pre><p class="utility-note">{{ editing ? 'Ctrl / ⌘ + S 保存。切换编码会转换保存格式；保存冲突时请复制内容后重新加载。' : '按纯文本展示，不执行文件中的 HTML 或脚本。' }}</p>
                </template>
                <img v-else-if="opened.previewKind === 'image'" class="file-image-preview" :src="fileContentUrl(opened.path)" :alt="opened.name" @error="mediaFailed" />
                <iframe v-else-if="opened.previewKind === 'pdf'" class="file-pdf-preview" :src="fileContentUrl(opened.path)" title="PDF 文件预览" @error="mediaFailed" />
                <video v-else-if="opened.previewKind === 'video'" class="file-video-preview" controls playsinline preload="metadata" :src="fileContentUrl(opened.path)" @error="mediaFailed" />
                <audio v-else-if="opened.previewKind === 'audio'" class="file-audio-preview" controls preload="metadata" :src="fileContentUrl(opened.path)" @error="mediaFailed" />
                <div v-else-if="opened.previewKind !== 'text'" class="file-empty"><WorkbenchIcon name="files" /><strong>此格式暂不支持浏览器预览</strong><span>可以下载原文件后使用对应软件打开。</span></div>
                <a-alert v-if="mediaError" :message="mediaError" type="warning" show-icon class="explorer-alert" />
                <p v-if="['pdf', 'audio', 'video'].includes(opened.previewKind)" class="utility-note">预览能力取决于浏览器支持的格式与编码；无法显示时可下载原文件。</p>
            </div></a-spin></template>
        </a-modal>
    </section>
</template>

<style scoped>
.file-manager { min-width: 0; }
.explorer-panel { padding: 24px; overflow: hidden; }
.explorer-heading,.explorer-machine,.preview-toolbar,.explorer-filters,.explorer-footer,.editor-settings,.row-actions { display: flex; align-items: center; gap: 12px; }
.explorer-heading,.explorer-footer { justify-content: space-between; flex-wrap: wrap; }
.explorer-machine h2 { margin: 0; font-size: 19px; }
.explorer-machine p { margin: 4px 0 0; font-size: 13px; color: var(--text-muted); }
.explorer-symbol { display: grid; place-items: center; width: 46px; height: 46px; border-radius: 14px; background: var(--accent-soft); color: var(--accent-strong); font-size: 24px; }
.drive-strip { display: flex; gap: 10px; margin: 22px 0; overflow-x: auto; padding-bottom: 4px; }
.drive-card { position: relative; display: flex; flex-shrink: 0; align-items: center; gap: 12px; padding: 14px 16px 20px; min-width: 205px; max-width: 310px; border: 1px solid var(--border); border-radius: 14px; color: var(--text); background: var(--well); cursor: pointer; text-align: left; overflow: hidden; }
.drive-card.active { border-color: var(--accent); background: var(--accent-soft); }
.drive-card > .workbench-icon { font-size: 23px; color: var(--accent-strong); }
.drive-card > span { min-width: 0; }
.drive-card strong,.drive-card small { display: block; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
.drive-card strong { font-size: 14px; }
.drive-card small { font-size: 12px; margin-top: 4px; color: var(--text-muted); }
.drive-card .drive-meter { position: absolute; bottom: 0; left: 0; right: 0; height: 3px; background: var(--border); }
.drive-meter i { display: block; height: 100%; background: var(--accent); }
.address-bar { display: flex; align-items: center; gap: 8px; margin-top: 20px; }
.path-input { flex: 1; min-width: 0; }
.path-input :deep(input) { font-family: Consolas,monospace; }
.file-breadcrumbs { display: flex; align-items: center; gap: 4px; flex-wrap: wrap; margin: 8px 0 16px; color: var(--text-subtle); }
.file-breadcrumbs button { border: 0; background: transparent; color: var(--text-muted); padding: 7px; border-radius: 6px; cursor: pointer; font-size: 13px; overflow-wrap: anywhere; }
.file-breadcrumbs button:hover,.file-breadcrumbs button[aria-current] { color: var(--accent-strong); background: var(--accent-soft); }
.explorer-filters { flex-wrap: wrap; margin: 18px 0; }
.file-search { flex: 1; min-width: min(230px,100%); }
.explorer-filters :deep(.ant-select) { min-width: 130px; }
.explorer-alert { margin: 16px 0; }
.file-table-scroll { overflow-x: auto; min-height: 260px; }
.file-table { border-collapse: collapse; width: 100%; table-layout: fixed; font-size: 13px; }
.file-table th { text-align: left; font-weight: 500; color: var(--text-muted); background: var(--well); padding: 12px; }
.file-table th:first-child { width: 38%; border-radius: 9px 0 0 9px; }
.file-table th:nth-child(2) { width: 11%; }
.file-table th:nth-child(3) { width: 12%; }
.file-table th:nth-child(4) { width: 23%; }
.file-table th:last-child { width: 130px; border-radius: 0 9px 9px 0; }
.file-table td { padding: 6px 12px; border-bottom: 1px solid var(--border); overflow-wrap: anywhere; }
.file-table tr.selected td { background: var(--accent-soft); }
.file-name { display: flex; width: 100%; min-width: 0; gap: 10px; align-items: center; text-align: left; border: 0; background: transparent; color: var(--text); cursor: pointer; min-height: 44px; padding: 3px 0; }
.file-name > span:nth-child(2) { overflow: hidden; white-space: nowrap; text-overflow: ellipsis; }
.file-name small { flex-shrink: 0; font-size: 11px; color: var(--text-muted); }
.file-name:hover > span:nth-child(2) { color: var(--accent-strong); text-decoration: underline; }
.entry-icon { display: grid; place-items: center; width: 30px; height: 34px; color: var(--text-muted); font-size: 19px; flex-shrink: 0; }
.entry-icon.folder { color: var(--accent-strong); }
.row-actions { gap: 6px; }
.date-cell { color: var(--text-muted); font-size: 12px; }
.file-empty { min-height: 240px; display: flex; align-items: center; justify-content: center; flex-direction: column; gap: 12px; text-align: center; color: var(--text-muted); padding: 30px 16px; }
.file-empty > .workbench-icon { font-size: 42px; color: var(--text-subtle); }
.file-empty strong { color: var(--text); font-weight: 500; }
.file-empty span { font-size: 13px; }
.explorer-footer { margin-top: 20px; font-size: 12px; color: var(--text-muted); }
.explorer-footer > span { min-width: 0; overflow-wrap: anywhere; flex: 1; }
.dialog-path { color: var(--text-muted); overflow-wrap: anywhere; font: 12px/1.7 Consolas,monospace; margin: 0 0 18px; }
.details-title { display: flex; align-items: flex-start; gap: 14px; color: var(--accent-strong); }
.details-title .workbench-icon { font-size: 32px; }
.details-title h3 { color: var(--text); font-size: 18px; overflow-wrap: anywhere; margin: 4px 0 20px; }
.file-details { display: grid; gap: 8px; }
.file-details dt { color: var(--text-muted); font-size: 12px; margin-top: 12px; }
.file-details dd { margin: 0; color: var(--text); font-size: 14px; overflow-wrap: anywhere; line-height: 1.7; }
.file-details dd .ant-btn { margin-top: 8px; display: block; }
/* 预览单独扩大，不影响新建弹窗；弹层被传送到 body，外层样式需限定全局类名。 */
:global(.file-preview-modal .ant-modal) { top: 16px; max-width: 1600px; padding-bottom: 16px; }
:global(.file-preview-modal .ant-modal-content) { display: flex; flex-direction: column; height: min(1200px, calc(100dvh - 32px)); padding: 16px 20px; }
:global(.file-preview-modal .ant-modal-header) { flex-shrink: 0; margin-bottom: 8px; padding-right: 36px; }
:global(.file-preview-modal .ant-modal-title) { overflow-wrap: anywhere; }
:global(.file-preview-modal .ant-modal-body) { display: flex; flex: 1; flex-direction: column; min-height: 0; overflow: auto; }
.preview-path { flex-shrink: 0; margin-bottom: 8px; overflow: hidden; white-space: nowrap; text-overflow: ellipsis; }
.preview-toolbar,.editor-settings { flex-wrap: wrap; flex-shrink: 0; gap: 8px; }
.preview-toolbar { margin-bottom: 8px; }
.edit-status { font-size: 13px; color: var(--accent-strong); }
/* 工具条按实际高度排布，剩余空间全部交给文件内容；短屏仍可滚动访问控件。 */
.preview-spin { display: flex; flex: 1; flex-direction: column; min-height: 180px; }
.preview-spin :deep(.ant-spin-container) { display: flex; flex: 1; flex-direction: column; min-height: 0; }
.preview-body { display: flex; flex: 1; flex-direction: column; min-height: 0; }
.preview-body > .utility-note { flex-shrink: 0; margin-top: 8px; font-size: 12px; }
.editor-settings { color: var(--text-muted); font-size: 12px; margin: 0 0 8px; }
.editor-settings .setting-field { display: flex; align-items: center; gap: 6px; }
.editor-settings .setting-field > label { white-space: nowrap; }
.editor-settings :deep(.ant-select) { min-width: 90px; }
.editor-settings .setting-field:first-child :deep(.ant-select) { min-width: 150px; }
.retry-encoding { display: flex; align-items: center; gap: 8px; }
.retry-encoding :deep(.ant-select) { min-width: 160px; }
.editor-settings > span { margin-left: auto; }
.file-editor { flex: 1; resize: none; min-height: 160px; max-height: none; tab-size: 4; }
.file-text-preview { flex: 1; border: 1px solid var(--border); background: var(--well); border-radius: 10px; padding: 12px; margin: 0; white-space: pre-wrap; overflow-wrap: anywhere; max-height: none; min-height: 160px; overflow: auto; color: var(--text); }
.file-image-preview { display: block; flex: 1; width: 100%; min-height: 0; object-fit: contain; margin: 0 auto; }
.file-pdf-preview { flex: 1; width: 100%; min-height: 180px; border: 1px solid var(--border); border-radius: 10px; }
.file-video-preview { display: block; flex: 1; width: 100%; min-height: 0; object-fit: contain; background: var(--well); }
.file-audio-preview { width: 100%; margin: auto 0; }
.sr-only { position: absolute; width: 1px; height: 1px; padding: 0; margin: -1px; overflow: hidden; clip: rect(0,0,0,0); white-space: nowrap; border: 0; }
:deep(.ant-btn) { overflow: hidden; }
@media (max-width: 900px) { .file-table { min-width: 740px; } }
@media (max-width: 600px) { .explorer-panel { padding: 16px; } .explorer-heading { gap: 18px; } .explorer-heading > .utility-toolbar { width: 100%; } .explorer-heading > .utility-toolbar .ant-btn { flex: 1; } .drive-card { min-width: 190px; } .explorer-footer { align-items: flex-start; flex-direction: column; } .editor-settings > span { width: 100%; margin-left: 0; } }
@media (max-width: 600px) {
    :global(.file-preview-modal) { --file-preview-gutter: 16px; }
    :global(.file-preview-modal .ant-modal) { top: 8px; margin: 0 auto; padding-bottom: 8px; }
    :global(.file-preview-modal .ant-modal-content) { height: calc(100dvh - 16px); padding: 12px; }
    /* 手机仍保留足够的触摸高度，主要收紧空白而不压小点击区域。 */
    .preview-toolbar :deep(.ant-btn) { min-height: 40px; }
    .editor-settings :deep(.ant-select-selector) { min-height: 40px; align-items: center; }
}
</style>
