<script setup>
import { computed, onBeforeUnmount, onMounted, ref } from 'vue';
import { onBeforeRouteLeave } from 'vue-router';
import { Modal, message } from 'ant-design-vue';
import ToolIntro from '../../components/ToolIntro.vue';
import WorkbenchIcon from '../../components/WorkbenchIcon.vue';
import { api, getErrorMessage } from '../../api/client.js';
import { diskSize, fileDate } from '../../utils/fileManager.js';

const status = ref(null), preview = ref(null), busy = ref(''), error = ref(''), result = ref(null);
let statusAbort, disposed = false;
const supported = computed(() => Boolean(status.value?.supported));
const canManage = computed(() => supported.value && status.value.canManage);
const automationLabel = computed(() => !status.value ? '读取中' : !supported.value ? '不支持' : !status.value.automationSupported ? '状态未知' : status.value.autoEnabled ? '已启用' : '未启用');
const mutating = computed(() => ['clean', 'automation'].includes(busy.value));
const lastCleanup = computed(() => result.value || status.value?.lastCleanup);
const stateLabels = { preview: '预览完成', completed: '处理完成', limited: '达到限时或数量上限', running: '已开始，尚未记录完成' };
const triggerLabels = { manual: '手动清理', shutdown: '关机事件', preview: '预览' };

/** 状态读取不扫描或删除 TEMP；只有明确点击预览、清理时才遍历目录。 */
async function refreshStatus() {
    if (busy.value) return;
    busy.value = 'status'; error.value = ''; statusAbort?.abort(); statusAbort = new AbortController();
    try {
        status.value = (await api.get('/api/temp-cleanup/status', { signal: statusAbort.signal })).data;
        result.value = null;
        if (preview.value?.tempPath !== status.value.tempPath) preview.value = null;
    }
    catch (exception) { if (exception.code !== 'ERR_CANCELED') error.value = await getErrorMessage(exception); }
    finally { if (!disposed) busy.value = ''; }
}
async function scan() {
    if (busy.value || !supported.value) return;
    busy.value = 'preview'; error.value = ''; preview.value = null;
    try {
        preview.value = (await api.get('/api/temp-cleanup/preview')).data;
        if (preview.value.tempPath !== status.value.tempPath) { preview.value = null; error.value = 'TEMP 目录已变化，请刷新状态后重新预览。'; }
    }
    catch (exception) { error.value = await getErrorMessage(exception); }
    finally { if (!disposed) busy.value = ''; }
}

/** 自动任务默认关闭，启用前明确展示目标账户和路径；不在访问页面时隐式安装任务。 */
function confirmAutomation() {
    const enabled = !status.value?.autoEnabled;
    Modal.confirm({
        title: enabled ? '启用关机前自动清理？' : '停用关机前自动清理？',
        content: enabled
            ? `目标为 ${status.value.tempPath}。正常关机或重启发起时，任务会以 ${status.value.account} 的普通用户权限尝试删除目录内文件。占用、只读和链接项跳过；Windows 可能提前结束任务。`
            : '将移除此工具为当前账户创建的关机事件任务，保留上次清理记录。',
        okText: enabled ? '启用自动清理' : '停用自动清理', cancelText: '取消',
        async onOk() {
            if (busy.value) return Promise.reject(new Error('请等待当前操作完成。'));
            busy.value = 'automation'; error.value = '';
            try {
                status.value = (await api.put('/api/temp-cleanup/automation', { enabled, expectedTempPath: status.value.tempPath })).data;
                message.success(enabled ? '关机前自动清理已启用。' : '自动清理已停用。');
            } catch (exception) {
                error.value = await getErrorMessage(exception); throw new Error(error.value);
            } finally { if (!disposed) busy.value = ''; }
        }
    });
}
function confirmClean() {
    Modal.confirm({
        title: '清理当前账户的 TEMP？',
        content: `将永久删除 ${status.value.tempPath} 中可处理的文件，无法通过本工具恢复。预览扫描到 ${preview.value.files} 个文件，约 ${diskSize(preview.value.bytes)}；实际清理数量受文件占用与权限影响。`,
        okText: '确认清理', cancelText: '取消', okButtonProps: { danger: true },
        async onOk() {
            if (busy.value) return Promise.reject(new Error('请等待当前操作完成。'));
            busy.value = 'clean'; error.value = '';
            try {
                result.value = (await api.post('/api/temp-cleanup/clean', { expectedTempPath: status.value.tempPath })).data;
                status.value = { ...status.value, lastCleanup: result.value }; preview.value = null;
                message.success('清理已结束，结果已记录。');
            } catch (exception) { error.value = await getErrorMessage(exception); throw new Error(error.value); }
            finally { if (!disposed) busy.value = ''; }
        }
    });
}
function beforeUnload(event) { if (mutating.value) { event.preventDefault(); event.returnValue = ''; } }
onBeforeRouteLeave(() => {
    if (mutating.value) { message.info('操作正在进行，请完成后再离开。'); return false; }
    return true;
});
onMounted(() => { refreshStatus(); window.addEventListener('beforeunload', beforeUnload); });
onBeforeUnmount(() => { disposed = true; statusAbort?.abort(); window.removeEventListener('beforeunload', beforeUnload); });
</script>

<template>
    <section class="temp-cleanup">
        <ToolIntro title="关机临时清理" eyebrow="SYSTEM / TEMP CLEANUP" description="在 Windows 正常关机或重启发起时，自动尝试清理当前后端账户的 %TEMP%。" />
        <a-alert v-if="error" :message="error" type="error" show-icon role="alert" class="cleanup-alert" />
        <a-alert v-if="status && !supported" :message="status.reason" type="info" show-icon class="cleanup-alert" />
        <a-alert v-if="supported && !status.canManage" message="自动配置和手动清理仅允许本机操作，请通过 localhost 地址打开工具台。" type="info" show-icon class="cleanup-alert" />
        <div class="utility-grid">
            <section class="panel utility-panel">
                <div class="panel-heading"><div class="cleanup-title"><span class="cleanup-icon"><WorkbenchIcon name="folder" /></span><div><h2>关机前自动清理</h2><p>{{ status?.machineName || '正在读取后端设备…' }}</p></div></div><span class="utility-status">{{ automationLabel }}</span></div>
                <dl class="cleanup-details"><dt>运行账户</dt><dd>{{ status?.account || '—' }}</dd><dt>清理目录</dt><dd class="cleanup-path">{{ status?.tempPath || '—' }}</dd><dt>触发条件</dt><dd>正常关机或重启发起时</dd><dt>任务状态</dt><dd>{{ status?.taskState || '—' }}</dd><dt>上次触发</dt><dd>{{ fileDate(status?.lastTaskRunUtc) }}</dd><dt>任务返回码</dt><dd>{{ status?.lastTaskResult ?? '—' }}</dd></dl>
                <a-alert v-if="supported && status.reason" :message="status.reason" type="warning" show-icon class="cleanup-alert" />
                <div class="utility-toolbar utility-gap"><a-button type="primary" :loading="busy === 'automation'" :disabled="!canManage || !status?.automationSupported || Boolean(busy)" @click="confirmAutomation">{{ status?.autoEnabled ? '停用自动清理' : '启用自动清理' }}</a-button><a-button :loading="busy === 'status'" :disabled="Boolean(busy)" @click="refreshStatus">刷新状态</a-button></div>
                <p class="utility-note">启用后由 Windows 任务计划程序执行，工具台关闭后也可触发。任务使用当前已登录用户的普通权限，不保存账户密码。停用仅移除此工具的任务。</p>
                <p class="utility-note">Windows 可能在清理完成前结束任务，强制关机或断电无法触发。单次处理限时 20 秒，计划任务最长 30 秒，不会在下次开机时补跑。</p>
                <p v-if="status?.taskName" class="utility-note">任务名称：{{ status.taskName }}</p>
                <p class="utility-note">系统拒绝创建或删除任务时，可用当前 Windows 用户以管理员身份运行后端后重试；清理任务本身仍按普通权限运行。</p>
            </section>
            <section class="panel utility-panel">
                <div class="panel-heading"><h2>清理预览</h2><span class="panel-index">%TEMP%</span></div>
                <p class="cleanup-description">预览只统计当前 TEMP 中的普通文件与目录。不会跟随符号链接或目录联接，也不会改变文件权限。</p>
                <div v-if="preview" class="cleanup-metrics"><div><strong>{{ preview.files.toLocaleString() }}</strong><span>扫描到的文件</span></div><div><strong>{{ diskSize(preview.bytes) }}</strong><span>预计可处理大小</span></div><div><strong>{{ preview.skipped.toLocaleString() }}</strong><span>已跳过的项目</span></div></div>
                <div v-else class="utility-empty"><WorkbenchIcon name="folder" /><p>点击预览后查看本次扫描结果</p></div>
                <a-alert v-if="preview?.limited" message="预览达到限时或数量上限，以上统计仅覆盖已扫描部分。" type="info" show-icon class="cleanup-alert" />
                <div class="utility-toolbar"><a-button :loading="busy === 'preview'" :disabled="!supported || Boolean(busy)" @click="scan">预览可清理内容</a-button><a-button danger :loading="busy === 'clean'" :disabled="!canManage || !preview || Boolean(busy)" @click="confirmClean">立即清理</a-button></div>
                <p class="utility-note">实际清理时跳过被占用、只读、权限不足或含目录链接的项目，保留 TEMP 根目录。文件内容不会被读取或上传。</p>
            </section>
        </div>
        <section class="panel utility-panel cleanup-history">
            <div class="panel-heading"><h2>上次清理记录</h2><span v-if="lastCleanup" class="utility-status">{{ stateLabels[lastCleanup.state] || lastCleanup.state }}</span></div>
            <template v-if="lastCleanup"><div class="cleanup-metrics"><div><strong>{{ lastCleanup.deletedFiles.toLocaleString() }}</strong><span>已删除文件</span></div><div><strong>{{ diskSize(lastCleanup.freedBytes) }}</strong><span>清理文件大小</span></div><div><strong>{{ lastCleanup.deletedFolders.toLocaleString() }}</strong><span>已删除空文件夹</span></div><div><strong>{{ lastCleanup.skipped.toLocaleString() }}</strong><span>已跳过项目</span></div></div><p class="utility-note">{{ triggerLabels[lastCleanup.trigger] || lastCleanup.trigger }} · 开始 {{ fileDate(lastCleanup.startedUtc) }} · 完成 {{ fileDate(lastCleanup.completedUtc) }}</p>
                <a-alert v-if="lastCleanup.state === 'running'" message="上次清理未记录完成，可能被关机或超时中断，不能视为清理成功。" type="warning" show-icon class="cleanup-alert" />
                <a-alert v-if="lastCleanup.limited" message="已达到处理限时或数量上限，剩余项目将在下一次清理时继续尝试。" type="info" show-icon class="cleanup-alert" />
                <details v-if="lastCleanup.errors?.length" class="cleanup-errors"><summary>查看跳过原因示例</summary><ul><li v-for="(item, index) in lastCleanup.errors" :key="index">{{ item }}</li></ul></details>
            </template><p v-else class="utility-note">暂无清理记录，预览不会生成执行日志。</p>
        </section>
    </section>
</template>

<style scoped>
.temp-cleanup { min-width: 0; }
.cleanup-alert { margin: 16px 0; }
.cleanup-title { display: flex; align-items: center; gap: 12px; min-width: 0; }
.panel-heading > .utility-status { flex-shrink: 0; white-space: nowrap; }
.cleanup-title p { margin: 4px 0 0; color: var(--text-muted); font-size: 12px; }
.cleanup-icon { display: grid; place-items: center; width: 42px; height: 42px; flex-shrink: 0; border-radius: 12px; background: var(--accent-soft); color: var(--accent-strong); font-size: 23px; }
.cleanup-details { display: grid; grid-template-columns: 90px minmax(0, 1fr); gap: 12px; margin: 0; font-size: 13px; }
.cleanup-details dt { color: var(--text-muted); }
.cleanup-details dd { margin: 0; min-width: 0; overflow-wrap: anywhere; color: var(--text); }
.cleanup-path { font-family: Consolas,monospace; }
.cleanup-description { color: var(--text-muted); font-size: 14px; line-height: 1.8; }
.cleanup-metrics { display: grid; grid-template-columns: repeat(auto-fit, minmax(120px, 1fr)); gap: 12px; margin: 22px 0; }
.cleanup-metrics > div { display: grid; gap: 5px; padding: 16px; border: 1px solid var(--border); border-radius: 14px; background: var(--well); }
.cleanup-metrics strong { color: var(--text); font-size: 23px; font-weight: 600; overflow-wrap: anywhere; }
.cleanup-metrics span { color: var(--text-muted); font-size: 12px; }
.cleanup-history { margin-top: 20px; }
.cleanup-errors { margin-top: 18px; color: var(--text-muted); font-size: 12px; overflow-wrap: anywhere; }
.cleanup-errors summary { cursor: pointer; padding: 8px 0; }
.cleanup-errors li { margin: 8px 0; }
.utility-empty > .workbench-icon { font-size: 34px; color: var(--text-subtle); }
:deep(.ant-btn) { overflow: hidden; }
@media (max-width: 600px) { .cleanup-title h2 { font-size: 17px; } .cleanup-metrics { grid-template-columns: repeat(2, minmax(0, 1fr)); } .cleanup-details { grid-template-columns: 76px minmax(0, 1fr); gap: 10px; } }
</style>
