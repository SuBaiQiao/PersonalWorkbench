<script setup>
import { computed, onBeforeUnmount, onMounted, ref, watch } from 'vue';
import { api, getErrorMessage } from '../../api/client.js';

const snapshot = ref(null);
const loading = ref(false);
const errorMessage = ref('');
const cpuDetails = ref(null);
const cpuDetailsOpen = ref(false);
const cpuDetailsLoading = ref(false);
const cpuDetailsError = ref('');
const gpuDetails = ref(null);
const gpuDetailsOpen = ref(false);
const gpuDetailsLoading = ref(false);
const gpuDetailsError = ref('');
const processDetails = ref(null);
const processDetailsOpen = ref(false);
const processDetailsLoading = ref(false);
const processDetailsError = ref('');
const processSearch = ref('');
const refreshInterval = ref(5);
const intervals = [{ value: 0, label: '暂停刷新' }, { value: 5, label: '每 5 秒' }, { value: 10, label: '每 10 秒' }, { value: 30, label: '每 30 秒' }];
let timer;
let controller;
let cpuTimer;
let cpuController;
let gpuTimer;
let gpuController;
let processTimer;
let processController;
let disposed = false;

function formatBytes(bytes) {
    if (!Number.isFinite(bytes) || bytes < 0) return '不可用';
    const units = ['B', 'KiB', 'MiB', 'GiB', 'TiB'];
    let value = bytes;
    let index = 0;
    while (value >= 1024 && index < units.length - 1) { value /= 1024; index += 1; }
    return `${value.toFixed(index === 0 ? 0 : 1)} ${units[index]}`;
}
const formatPercent = value => Number.isFinite(value) ? `${value.toFixed(1)}%` : '不可用';
const progressColor = value => value >= 90 ? 'var(--danger)' : 'var(--orange)';
function formatRate(bytes) {
    if (!Number.isFinite(bytes) || bytes < 0) return '不可用';
    return `${formatBytes(bytes)}/s`;
}
function processTime(value) { return value ? new Date(value).toLocaleString() : '不可用'; }
const updatedAt = computed(() => snapshot.value ? new Date(snapshot.value.collectedAt).toLocaleTimeString() : '尚未采样');
const diskTotal = computed(() => {
    const disks = (snapshot.value?.disks || []).filter(disk => disk.isReady && disk.totalBytes > 0);
    if (!disks.length) return null;
    const total = disks.reduce((sum, disk) => sum + disk.totalBytes, 0);
    const used = disks.reduce((sum, disk) => sum + disk.usedBytes, 0);
    return { total, used, percent: used / total * 100 };
});
const filteredProcesses = computed(() => {
    const processes = processDetails.value?.processes || [];
    const term = processSearch.value.trim().toLocaleLowerCase();
    if (!term) return processes;
    return processes.filter(process => `${process.name} ${process.processId} ${process.filePath || ''}`.toLocaleLowerCase().includes(term));
});
const processColumns = [
    { title: 'PID', dataIndex: 'processId', key: 'pid', width: 76, sorter: (a, b) => a.processId - b.processId },
    { title: '进程', dataIndex: 'name', key: 'name', width: 180, sorter: (a, b) => a.name.localeCompare(b.name) },
    { title: 'CPU', dataIndex: 'cpuPercent', key: 'cpu', width: 90, sorter: (a, b) => (a.cpuPercent ?? -1) - (b.cpuPercent ?? -1) },
    { title: '工作集', dataIndex: 'workingSetBytes', key: 'workingSet', width: 110, sorter: (a, b) => (a.workingSetBytes ?? -1) - (b.workingSetBytes ?? -1) },
    { title: '专用内存', dataIndex: 'privateMemoryBytes', key: 'privateMemory', width: 110, sorter: (a, b) => (a.privateMemoryBytes ?? -1) - (b.privateMemoryBytes ?? -1) },
    { title: '虚拟地址空间', dataIndex: 'virtualMemoryBytes', key: 'virtualMemory', width: 120 },
    { title: '分页内存', dataIndex: 'pagedMemoryBytes', key: 'pagedMemory', width: 110 },
    { title: 'GPU', dataIndex: 'gpuPercent', key: 'gpu', width: 84, sorter: (a, b) => (a.gpuPercent ?? -1) - (b.gpuPercent ?? -1) },
    { title: 'GPU 显存', dataIndex: 'gpuMemoryBytes', key: 'gpuMemory', width: 110 },
    { title: '网络接收', dataIndex: 'networkReceivedBytesPerSecond', key: 'networkReceived', width: 112 },
    { title: '网络发送', dataIndex: 'networkSentBytesPerSecond', key: 'networkSent', width: 112 },
    { title: 'TCP', dataIndex: 'tcpConnections', key: 'tcp', width: 64 },
    { title: 'UDP', dataIndex: 'udpEndpoints', key: 'udp', width: 64 },
    { title: '读速率', dataIndex: 'readBytesPerSecond', key: 'read', width: 110 },
    { title: '写速率', dataIndex: 'writeBytesPerSecond', key: 'write', width: 110 },
    { title: '线程', dataIndex: 'threadCount', key: 'threads', width: 76 },
    { title: '句柄', dataIndex: 'handleCount', key: 'handles', width: 76 },
    { title: '会话', dataIndex: 'sessionId', key: 'session', width: 76 },
    { title: '父进程 PID', dataIndex: 'parentProcessId', key: 'parent', width: 100 },
    { title: '优先级', dataIndex: 'basePriority', key: 'priority', width: 76 },
    { title: '状态', dataIndex: 'status', key: 'status', width: 92 },
    { title: '启动时间', dataIndex: 'startedAt', key: 'startedAt', width: 170 },
    { title: '可执行文件', dataIndex: 'filePath', key: 'path', width: 320 },
];
const gpuEngineColumns = [
    { title: 'GPU 实例', dataIndex: 'adapterInstance', key: 'adapter', width: 300 },
    { title: 'PID', dataIndex: 'processId', key: 'pid', width: 90 },
    { title: '引擎', dataIndex: 'engineType', key: 'engine', width: 150 },
    { title: '使用率', dataIndex: 'usagePercent', key: 'usage', width: 100 },
];
function formatUptime(seconds) {
    const days = Math.floor(seconds / 86400);
    return `${days ? `${days} 天 ` : ''}${Math.floor(seconds % 86400 / 3600)} 小时 ${Math.floor(seconds % 3600 / 60)} 分钟`;
}
function scheduleRefresh() {
    window.clearTimeout(timer);
    if (!disposed && !loading.value && refreshInterval.value > 0) timer = window.setTimeout(loadStatus, refreshInterval.value * 1000);
}
async function loadStatus() {
    if (disposed || loading.value) return;
    window.clearTimeout(timer);
    loading.value = true;
    controller = new AbortController();
    try {
        const response = await api.get('/api/system/status', { signal: controller.signal, timeout: 20000 });
        if (!disposed) { snapshot.value = response.data; errorMessage.value = ''; }
    } catch (error) {
        if (!disposed && error.code !== 'ERR_CANCELED') {
            errorMessage.value = await getErrorMessage(error, '设备状态读取失败。');
        }
    } finally { loading.value = false; scheduleRefresh(); }
}
function scheduleCpuRefresh() {
    window.clearTimeout(cpuTimer);
    if (!disposed && cpuDetailsOpen.value && !cpuDetailsLoading.value && refreshInterval.value > 0)
        cpuTimer = window.setTimeout(loadCpuDetails, refreshInterval.value * 1000);
}
async function loadCpuDetails() {
    if (disposed || !cpuDetailsOpen.value || cpuDetailsLoading.value) return;
    window.clearTimeout(cpuTimer);
    cpuDetailsLoading.value = true;
    cpuController = new AbortController();
    const request = cpuController;
    try {
        const response = await api.get('/api/system/cpu', { signal: request.signal, timeout: 15000 });
        if (!disposed && !request.signal.aborted) { cpuDetails.value = response.data; cpuDetailsError.value = ''; }
    } catch (error) {
        if (!disposed && !request.signal.aborted) cpuDetailsError.value = await getErrorMessage(error, 'CPU 详细信息读取失败。');
    } finally { cpuDetailsLoading.value = false; scheduleCpuRefresh(); }
}
function scheduleGpuRefresh() {
    window.clearTimeout(gpuTimer);
    if (!disposed && gpuDetailsOpen.value && !gpuDetailsLoading.value && refreshInterval.value > 0)
        gpuTimer = window.setTimeout(loadGpuDetails, refreshInterval.value * 1000);
}
async function loadGpuDetails() {
    if (disposed || !gpuDetailsOpen.value || gpuDetailsLoading.value) return;
    window.clearTimeout(gpuTimer);
    gpuDetailsLoading.value = true;
    gpuController = new AbortController();
    const request = gpuController;
    try {
        const response = await api.get('/api/system/gpu', { signal: request.signal, timeout: 30000 });
        if (!disposed && !request.signal.aborted) { gpuDetails.value = response.data; gpuDetailsError.value = ''; }
    } catch (error) {
        if (!disposed && !request.signal.aborted) gpuDetailsError.value = await getErrorMessage(error, '显卡详细信息读取失败。');
    } finally { gpuDetailsLoading.value = false; scheduleGpuRefresh(); }
}
function scheduleProcessRefresh() {
    window.clearTimeout(processTimer);
    if (!disposed && processDetailsOpen.value && !processDetailsLoading.value && refreshInterval.value > 0)
        processTimer = window.setTimeout(loadProcessDetails, refreshInterval.value * 1000);
}
async function loadProcessDetails() {
    if (disposed || !processDetailsOpen.value || processDetailsLoading.value) return;
    window.clearTimeout(processTimer);
    processDetailsLoading.value = true;
    processController = new AbortController();
    const request = processController;
    try {
        const response = await api.get('/api/system/processes', { signal: request.signal, timeout: 40000 });
        if (!disposed && !request.signal.aborted) { processDetails.value = response.data; processDetailsError.value = ''; }
    } catch (error) {
        if (!disposed && !request.signal.aborted) processDetailsError.value = await getErrorMessage(error, '进程信息读取失败。');
    } finally { processDetailsLoading.value = false; scheduleProcessRefresh(); }
}
watch(cpuDetailsOpen, open => {
    if (open) loadCpuDetails();
    else { window.clearTimeout(cpuTimer); cpuController?.abort(); }
});
watch(gpuDetailsOpen, open => {
    if (open) loadGpuDetails();
    else { window.clearTimeout(gpuTimer); gpuController?.abort(); }
});
watch(processDetailsOpen, open => {
    if (open) loadProcessDetails();
    else { window.clearTimeout(processTimer); processController?.abort(); }
});
watch(refreshInterval, () => { scheduleCpuRefresh(); scheduleGpuRefresh(); scheduleProcessRefresh(); });
onMounted(loadStatus);
onBeforeUnmount(() => {
    disposed = true;
    window.clearTimeout(timer); window.clearTimeout(cpuTimer); window.clearTimeout(gpuTimer); window.clearTimeout(processTimer);
    controller?.abort(); cpuController?.abort(); gpuController?.abort(); processController?.abort();
});
</script>

<template>
    <section class="page-intro" aria-labelledby="system-monitor-title">
        <p class="eyebrow">HOST TELEMETRY / SYSTEM STATUS</p>
        <h1 id="system-monitor-title">设备状态监控</h1>
        <p class="intro-copy">查看运行 API 服务的电脑的 CPU、物理内存、显卡和本地磁盘状态。</p>
    </section>

    <div class="monitor-toolbar">
        <span class="monitor-update">{{ snapshot ? `采样时间 ${updatedAt}` : '尚未采样' }}{{ errorMessage && snapshot ? ' · 数据可能已过期' : '' }}</span>
        <div class="monitor-actions">
            <label for="monitor-refresh">自动刷新</label>
            <a-select id="monitor-refresh" v-model:value="refreshInterval" :options="intervals" aria-label="自动刷新间隔" @change="scheduleRefresh" />
            <a-button @click="processDetailsOpen = true">查看全部进程</a-button>
            <a-button @click="loadStatus">立即刷新</a-button>
        </div>
    </div>
    <a-alert v-if="errorMessage" class="monitor-alert" :message="errorMessage" type="error" show-icon role="alert" />
    <div v-if="loading && !snapshot" class="monitor-empty" role="status">正在读取设备状态…</div>
    <template v-if="snapshot">
        <div class="monitor-host"><strong>{{ snapshot.machineName }}</strong><span>{{ snapshot.operatingSystem }}</span><span>已运行 {{ formatUptime(snapshot.uptimeSeconds) }}</span></div>

        <div class="monitor-grid">
            <section class="panel monitor-panel" aria-labelledby="cpu-status-title">
                <div class="panel-heading"><div><p class="panel-kicker">PROCESSOR</p><h2 id="cpu-status-title">CPU</h2></div><div class="monitor-panel-actions"><span class="panel-index">01</span><a-button size="small" type="text" aria-label="查看 CPU 详情" @click="cpuDetailsOpen = true">查看详情 →</a-button></div></div>
                <p class="monitor-hardware">{{ snapshot.cpu.name || '型号不可用' }}</p>
                <div class="monitor-metric"><span>整机使用率</span><strong>{{ formatPercent(snapshot.cpu.usagePercent) }}</strong></div>
                <a-progress v-if="snapshot.cpu.usagePercent != null" :percent="snapshot.cpu.usagePercent" :show-info="false" :stroke-color="progressColor(snapshot.cpu.usagePercent)" aria-label="CPU 使用率" />
                <dl class="monitor-details"><div><dt>物理核心</dt><dd>{{ snapshot.cpu.physicalCores ?? '不可用' }}</dd></div><div><dt>逻辑处理器</dt><dd>{{ snapshot.cpu.logicalProcessors }}</dd></div></dl>
            </section>

            <section class="panel monitor-panel" aria-labelledby="memory-status-title">
                <div class="panel-heading"><div><p class="panel-kicker">PHYSICAL MEMORY</p><h2 id="memory-status-title">内存</h2></div><span class="panel-index">02</span></div>
                <p class="monitor-hardware">已用 {{ formatBytes(snapshot.memory.usedBytes) }} / 总计 {{ formatBytes(snapshot.memory.totalBytes) }}</p>
                <div class="monitor-metric"><span>物理内存使用率</span><strong>{{ formatPercent(snapshot.memory.usagePercent) }}</strong></div>
                <a-progress v-if="snapshot.memory.usagePercent != null" :percent="snapshot.memory.usagePercent" :show-info="false" :stroke-color="progressColor(snapshot.memory.usagePercent)" aria-label="内存使用率" />
                <dl class="monitor-details"><div><dt>总内存</dt><dd>{{ formatBytes(snapshot.memory.totalBytes) }}</dd></div><div><dt>可用内存</dt><dd>{{ formatBytes(snapshot.memory.availableBytes) }}</dd></div></dl>
            </section>

            <section class="panel monitor-panel monitor-wide" aria-labelledby="gpu-status-title">
                <div class="panel-heading"><div><p class="panel-kicker">GRAPHICS</p><h2 id="gpu-status-title">显卡</h2></div><div class="monitor-panel-actions"><span class="panel-index">03</span><a-button size="small" type="text" aria-label="查看显卡详情" @click="gpuDetailsOpen = true">查看详情 →</a-button></div></div>
                <dl class="monitor-details monitor-gpu-summary">
                    <div><dt>整机最繁忙 GPU 引擎</dt><dd>{{ formatPercent(snapshot.gpu.usagePercent) }}</dd></div>
                    <div><dt>专用显存占用合计</dt><dd>{{ formatBytes(snapshot.gpu.dedicatedUsedBytes) }}</dd></div>
                    <div><dt>共享显存占用合计</dt><dd>{{ formatBytes(snapshot.gpu.sharedUsedBytes) }}</dd></div>
                </dl>
                <div v-if="snapshot.gpu.adapters.length" class="monitor-adapters">
                    <article v-for="adapter in snapshot.gpu.adapters" :key="adapter.id" class="monitor-adapter">
                        <h3>{{ adapter.name }}</h3><p class="monitor-note">驱动 {{ adapter.driverVersion || '不可用' }}</p>
                        <dl class="monitor-details">
                            <div><dt>使用率</dt><dd>{{ formatPercent(adapter.usagePercent) }}</dd></div>
                            <div><dt>温度</dt><dd>{{ adapter.temperatureCelsius == null ? '不可用' : `${adapter.temperatureCelsius.toFixed(0)} °C` }}</dd></div>
                            <div><dt>显存占用</dt><dd>{{ formatBytes(adapter.usedMemoryBytes) }}</dd></div>
                            <div><dt>显存容量</dt><dd>{{ formatBytes(adapter.totalMemoryBytes) }}</dd></div>
                        </dl>
                    </article>
                </div>
                <p v-else class="monitor-note">未检测到可读取的显卡。</p>
                <p class="monitor-note">{{ snapshot.gpu.note }}</p>
            </section>

            <section class="panel monitor-panel monitor-wide" aria-labelledby="disk-status-title">
                <div class="panel-heading"><div><p class="panel-kicker">LOCAL STORAGE</p><h2 id="disk-status-title">磁盘使用情况</h2></div><span class="panel-index">04</span></div>
                <p v-if="diskTotal" class="monitor-note">卷容量合计 {{ formatBytes(diskTotal.total) }}，已用 {{ formatBytes(diskTotal.used) }}（{{ formatPercent(diskTotal.percent) }}）。按本地卷统计，不代表物理硬盘数量。</p>
                <div v-if="snapshot.disks.length" class="monitor-disks">
                    <article v-for="disk in snapshot.disks" :key="disk.name" class="monitor-disk">
                        <div class="monitor-disk-title"><h3>{{ disk.name }} <span>{{ disk.label }}</span></h3><strong>{{ formatPercent(disk.usagePercent) }}</strong></div>
                        <p class="monitor-note">{{ disk.fileSystem || '文件系统不可用' }} · {{ disk.driveType === 'Removable' ? '可移动磁盘' : '本地磁盘' }}</p>
                        <a-progress v-if="disk.usagePercent != null" :percent="disk.usagePercent" :show-info="false" :stroke-color="progressColor(disk.usagePercent)" :aria-label="`${disk.name} 使用率`" />
                        <dl v-if="disk.isReady" class="monitor-details monitor-disk-details"><div><dt>已用</dt><dd>{{ formatBytes(disk.usedBytes) }}</dd></div><div><dt>剩余</dt><dd>{{ formatBytes(disk.freeBytes) }}</dd></div><div><dt>总容量</dt><dd>{{ formatBytes(disk.totalBytes) }}</dd></div></dl>
                        <p v-else class="monitor-note">{{ disk.error }}</p>
                    </article>
                </div>
                <p v-else class="monitor-note">未检测到可读取的本地磁盘。</p>
            </section>
        </div>
        <a-alert v-if="snapshot.warnings.length" class="monitor-alert" :message="snapshot.warnings.join('；')" type="warning" show-icon />
    </template>

    <a-modal v-model:open="cpuDetailsOpen" title="CPU 详细信息" width="780px" :footer="null" :style="{ top: '40px' }" :body-style="{ maxHeight: 'calc(100vh - 160px)', overflowY: 'auto' }" destroy-on-close>
        <div class="cpu-detail-meta"><span>详情随页面刷新间隔更新</span><a-button size="small" @click="loadCpuDetails">刷新 CPU 详情</a-button></div>
        <a-alert v-if="cpuDetailsError" class="monitor-alert" :message="cpuDetailsError + (cpuDetails ? '（显示上次采样数据）' : '')" type="error" show-icon />
        <div v-if="cpuDetailsLoading && !cpuDetails" class="monitor-empty" role="status">正在读取 CPU 详细信息…</div>
        <template v-if="cpuDetails">
            <div class="cpu-detail-meta"><span>架构 {{ cpuDetails.architecture }}</span><span>采样时间 {{ new Date(cpuDetails.collectedAt).toLocaleTimeString() }}</span></div>
            <a-alert v-if="cpuDetails.warnings.length" class="monitor-alert" :message="cpuDetails.warnings.join('；')" type="warning" show-icon />
            <div class="cpu-package-list">
                <article v-for="cpu in cpuDetails.packages" :key="cpu.id" class="cpu-package-card">
                    <h3>{{ cpu.name }}</h3>
                    <p class="monitor-note">{{ cpu.manufacturer || '制造商不可用' }} · 插槽 {{ cpu.socket || '不可用' }}</p>
                    <dl class="monitor-details cpu-package-details">
                        <div><dt>物理核心</dt><dd>{{ cpu.physicalCores ?? '不可用' }}</dd></div><div><dt>逻辑处理器</dt><dd>{{ cpu.logicalProcessors ?? '不可用' }}</dd></div>
                        <div><dt>系统报告频率</dt><dd>{{ cpu.reportedClockMhz == null ? '不可用' : `${cpu.reportedClockMhz.toFixed(0)} MHz` }}</dd></div><div><dt>系统报告最大频率</dt><dd>{{ cpu.maxClockMhz == null ? '不可用' : `${cpu.maxClockMhz.toFixed(0)} MHz` }}</dd></div>
                        <div><dt>L2 缓存</dt><dd>{{ formatBytes(cpu.l2CacheBytes) }}</dd></div><div><dt>L3 缓存</dt><dd>{{ formatBytes(cpu.l3CacheBytes) }}</dd></div>
                        <div><dt>固件虚拟化</dt><dd>{{ cpu.virtualizationFirmwareEnabled == null ? '不可用' : (cpu.virtualizationFirmwareEnabled ? '已启用' : '未启用') }}</dd></div>
                    </dl>
                </article>
            </div>
            <p class="monitor-note">频率由系统报告，可能与瞬时睿频不同。每格代表一个逻辑处理器，使用率按约 0.5 秒的采样窗口计算。</p>
            <p v-if="!cpuDetails.packages.length" class="monitor-note">CPU 硬件详情不可用。</p>
            <div v-if="cpuDetails.logicalProcessors.length" class="cpu-logical-section"><h3>各逻辑处理器使用率</h3><div class="cpu-logical-grid"><div v-for="processor in cpuDetails.logicalProcessors" :key="processor.id" class="cpu-logical-item"><span>CPU {{ processor.id }}</span><strong>{{ formatPercent(processor.usagePercent) }}</strong><a-progress v-if="processor.usagePercent != null" :percent="processor.usagePercent" :show-info="false" :stroke-color="progressColor(processor.usagePercent)" :aria-label="`CPU ${processor.id} 使用率`" /></div></div></div>
        </template>
    </a-modal>

    <a-modal v-model:open="gpuDetailsOpen" title="显卡详细信息" width="980px" :footer="null" :style="{ top: '40px' }" :body-style="{ maxHeight: 'calc(100vh - 160px)', overflowY: 'auto' }" destroy-on-close>
        <div class="cpu-detail-meta"><span>{{ gpuDetails ? `采样时间 ${new Date(gpuDetails.collectedAt).toLocaleTimeString()}` : '读取显卡传感器与性能计数器' }}</span><a-button size="small" @click="loadGpuDetails">刷新显卡详情</a-button></div>
        <a-alert v-if="gpuDetailsError" class="monitor-alert" :message="gpuDetailsError + (gpuDetails ? '（显示上次采样数据）' : '')" type="error" show-icon />
        <div v-if="gpuDetailsLoading && !gpuDetails" class="monitor-empty" role="status">正在读取显卡详细信息…</div>
        <template v-if="gpuDetails">
            <a-alert v-for="warning in gpuDetails.warnings" :key="warning" class="monitor-alert" :message="warning" type="warning" show-icon />
            <div v-if="gpuDetails.adapters.length" class="gpu-detail-grid">
                <article v-for="adapter in gpuDetails.adapters" :key="adapter.id" class="gpu-detail-card">
                    <h3>{{ adapter.name }}</h3>
                    <p class="monitor-note">{{ adapter.pciBusId || adapter.id }} · 驱动 {{ adapter.driverVersion || '不可用' }}</p>
                    <dl class="monitor-details gpu-metrics">
                        <div><dt>GPU 使用率</dt><dd>{{ formatPercent(adapter.gpuUsagePercent) }}</dd></div><div><dt>显存控制器</dt><dd>{{ formatPercent(adapter.memoryUsagePercent) }}</dd></div>
                        <div><dt>显存已用 / 总量</dt><dd>{{ formatBytes(adapter.usedMemoryBytes) }} / {{ formatBytes(adapter.totalMemoryBytes) }}</dd></div><div><dt>显存剩余</dt><dd>{{ formatBytes(adapter.freeMemoryBytes) }}</dd></div>
                        <div><dt>GPU 温度</dt><dd>{{ adapter.temperatureCelsius == null ? '不可用' : `${adapter.temperatureCelsius.toFixed(0)} °C` }}</dd></div><div><dt>显存温度</dt><dd>{{ adapter.memoryTemperatureCelsius == null ? '不可用' : `${adapter.memoryTemperatureCelsius.toFixed(0)} °C` }}</dd></div>
                        <div><dt>风扇转速</dt><dd>{{ formatPercent(adapter.fanPercent) }}</dd></div><div><dt>功耗 / 上限</dt><dd>{{ adapter.powerDrawWatts == null ? '不可用' : `${adapter.powerDrawWatts.toFixed(1)} W` }} / {{ adapter.powerLimitWatts == null ? '不可用' : `${adapter.powerLimitWatts.toFixed(1)} W` }}</dd></div>
                        <div><dt>图形 / 显存时钟</dt><dd>{{ adapter.graphicsClockMhz == null ? '不可用' : `${adapter.graphicsClockMhz} MHz` }} / {{ adapter.memoryClockMhz == null ? '不可用' : `${adapter.memoryClockMhz} MHz` }}</dd></div><div><dt>最大图形 / 显存时钟</dt><dd>{{ adapter.maxGraphicsClockMhz == null ? '不可用' : `${adapter.maxGraphicsClockMhz} MHz` }} / {{ adapter.maxMemoryClockMhz == null ? '不可用' : `${adapter.maxMemoryClockMhz} MHz` }}</dd></div>
                        <div><dt>编码 / 解码引擎</dt><dd>{{ formatPercent(adapter.encoderUsagePercent) }} / {{ formatPercent(adapter.decoderUsagePercent) }}</dd></div><div><dt>性能状态 / 计算模式</dt><dd>{{ adapter.performanceState || '不可用' }} / {{ adapter.computeMode || '不可用' }}</dd></div>
                        <div><dt>VBIOS</dt><dd>{{ adapter.vbiosVersion || '不可用' }}</dd></div><div><dt>PCIe 当前链路</dt><dd>{{ adapter.pciLinkGeneration == null ? '不可用' : `Gen ${adapter.pciLinkGeneration}` }} / {{ adapter.pciLinkWidth == null ? '不可用' : `x${adapter.pciLinkWidth}` }}</dd></div>
                        <div><dt>ECC</dt><dd>{{ adapter.eccMode || '不可用' }}</dd></div><div><dt>时钟限制</dt><dd>{{ adapter.clockThrottleReasons || '不可用' }}</dd></div>
                    </dl>
                    <div class="gpu-process-list"><h4>该显卡上的进程</h4><template v-if="adapter.processes.length"><div v-for="process in adapter.processes" :key="process.processId" class="gpu-process-row"><span>{{ process.name || '进程' }} (PID {{ process.processId }})</span><strong>{{ formatBytes(process.usedMemoryBytes) }}</strong></div></template><p v-else class="monitor-note">驱动未报告显卡进程。</p></div>
                </article>
            </div>
            <p v-else class="monitor-note">未读取到显卡适配器。</p>
            <section v-if="gpuDetails.memoryCounters.length" class="gpu-counter-section"><h3>Windows GPU 实例显存计数器</h3><a-table :data-source="gpuDetails.memoryCounters" :columns="[{ title: '适配器实例', dataIndex: 'adapterInstance', key: 'adapter' }, { title: '专用显存', dataIndex: 'dedicatedBytes', key: 'dedicated' }, { title: '共享显存', dataIndex: 'sharedBytes', key: 'shared' }]" :pagination="false" row-key="adapterInstance" size="small"><template #bodyCell="{ column, text }"><span v-if="column.key === 'dedicated' || column.key === 'shared'">{{ formatBytes(text) }}</span></template></a-table></section>
            <section v-if="gpuDetails.processMemory.length" class="gpu-counter-section"><h3>各进程 GPU 显存计数器</h3><a-table :data-source="gpuDetails.processMemory" :columns="[{ title: 'PID', dataIndex: 'processId', key: 'pid' }, { title: '专用显存', dataIndex: 'dedicatedBytes', key: 'dedicated' }, { title: '共享显存', dataIndex: 'sharedBytes', key: 'shared' }]" :pagination="{ pageSize: 10 }" row-key="processId" size="small"><template #bodyCell="{ column, text }"><span v-if="column.key === 'dedicated' || column.key === 'shared'">{{ formatBytes(text) }}</span></template></a-table></section>
            <section v-if="gpuDetails.engines.length" class="gpu-counter-section"><h3>GPU 引擎使用率</h3><a-table :data-source="gpuDetails.engines" :columns="gpuEngineColumns" :pagination="{ pageSize: 10 }" :scroll="{ x: 700 }" :row-key="record => `${record.adapterInstance}-${record.processId}-${record.engineType}`" size="small"><template #bodyCell="{ column, text }"><span v-if="column.key === 'pid'">{{ text ?? '系统' }}</span><span v-else-if="column.key === 'usage'">{{ formatPercent(text) }}</span></template></a-table></section>
        </template>
    </a-modal>

    <a-modal v-model:open="processDetailsOpen" title="进程资源占用" width="1120px" :footer="null" :style="{ top: '32px' }" :body-style="{ maxHeight: 'calc(100vh - 140px)', overflowY: 'auto' }" destroy-on-close>
        <div class="process-toolbar"><div><strong>{{ processDetails ? `${processDetails.processes.length} 个进程` : '读取进程列表' }}</strong><span v-if="processDetails">采样窗口 {{ (processDetails.sampleIntervalMilliseconds / 1000).toFixed(1) }} 秒 · {{ processDetails.processorCount }} 个逻辑处理器</span></div><div class="process-actions"><a-input v-model:value="processSearch" allow-clear placeholder="搜索进程名称、PID 或路径" aria-label="搜索进程" /><a-button @click="loadProcessDetails">刷新进程</a-button></div></div>
        <a-alert v-if="processDetailsError" class="monitor-alert" :message="processDetailsError + (processDetails ? '（显示上次采样数据）' : '')" type="error" show-icon />
        <div v-if="processDetailsLoading && !processDetails" class="monitor-empty" role="status">正在采样所有进程…</div>
        <template v-if="processDetails">
            <a-alert class="monitor-alert" :message="processDetails.networkNote" :type="processDetails.networkBandwidthAvailable ? 'info' : 'warning'" show-icon />
            <p class="monitor-note">系统端点总数：TCP {{ processDetails.activeTcpConnections ?? '不可用' }} · UDP {{ processDetails.activeUdpEndpoints ?? '不可用' }}。读写速率为进程 I/O，可能包含设备 I/O；虚拟地址空间不是实际内存占用。CPU 使用率按整机逻辑处理器数量归一化。</p>
            <a-alert v-for="warning in processDetails.warnings.filter(warning => warning !== processDetails.networkNote)" :key="warning" class="monitor-alert" :message="warning" type="warning" show-icon />
            <a-table :columns="processColumns" :data-source="filteredProcesses" :pagination="{ pageSize: 25, showSizeChanger: true, pageSizeOptions: ['25', '50', '100'] }" :scroll="{ x: 2200 }" row-key="processId" size="small">
                <template #bodyCell="{ column, record, text }">
                    <span v-if="column.key === 'cpu' || column.key === 'gpu'">{{ formatPercent(text) }}</span>
                    <span v-else-if="['workingSet', 'privateMemory', 'virtualMemory', 'pagedMemory', 'gpuMemory'].includes(column.key)">{{ formatBytes(text) }}</span>
                    <span v-else-if="['networkReceived', 'networkSent', 'read', 'write'].includes(column.key)">{{ formatRate(text) }}</span>
                    <span v-else-if="column.key === 'startedAt'">{{ processTime(text) }}</span>
                    <span v-else-if="column.key === 'path'" class="process-path" :title="text || ''">{{ text || '不可用' }}</span>
                    <span v-else-if="column.key === 'tcp' || column.key === 'udp'">{{ text ?? '不可用' }}</span>
                    <span v-else-if="column.key === 'name'" :title="record.error || ''">{{ text }}</span>
                    <span v-else-if="column.key === 'status'" :title="record.error || ''">{{ record.error || text }}</span>
                </template>
            </a-table>
            <p v-if="!filteredProcesses.length" class="monitor-note">没有匹配的进程。</p>
        </template>
    </a-modal>
</template>

<style scoped>
.monitor-toolbar, .monitor-actions, .monitor-host, .monitor-metric, .monitor-disk-title { display: flex; align-items: center; gap: 14px; }
.monitor-toolbar { justify-content: space-between; margin-bottom: 18px; flex-wrap: wrap; }
.monitor-actions { flex-wrap: wrap; gap: 10px; }
.monitor-actions label, .monitor-update, .monitor-host, .monitor-note { color: var(--text-muted); font-size: 13px; }
.monitor-actions .ant-select { width: 132px; }
.monitor-host { flex-wrap: wrap; padding: 16px 0 24px; }
.monitor-host strong { color: var(--text); overflow-wrap: anywhere; }
.monitor-alert { margin: 16px 0; }
.monitor-empty { padding: 48px 20px; color: var(--text-muted); text-align: center; }
.monitor-grid { display: grid; grid-template-columns: repeat(2, minmax(0, 1fr)); gap: 20px; }
.monitor-panel { min-width: 0; padding: 26px; }
.monitor-panel-actions { display: flex; align-items: center; gap: 10px; }
.monitor-wide { grid-column: 1 / -1; }
.monitor-hardware { min-height: 44px; color: var(--text-muted); overflow-wrap: anywhere; }
.monitor-metric { justify-content: space-between; }
.monitor-metric strong { font-size: 36px; font-family: Consolas, monospace; font-weight: 500; }
.monitor-details { display: grid; grid-template-columns: repeat(2, minmax(0, 1fr)); gap: 16px; margin: 20px 0 0; }
.monitor-details div { border-top: 1px solid var(--border); padding-top: 10px; }
.monitor-details dt { color: var(--text-muted); font-size: 12px; }
.monitor-details dd { margin: 4px 0 0; font-family: Consolas, monospace; overflow-wrap: anywhere; }
.monitor-gpu-summary { grid-template-columns: repeat(3, minmax(0, 1fr)); margin: 0 0 24px; }
.monitor-adapters, .monitor-disks { display: grid; grid-template-columns: repeat(2, minmax(0, 1fr)); gap: 16px; }
.monitor-adapter, .monitor-disk { min-width: 0; padding: 18px; border: 1px solid var(--border); border-radius: var(--radius); background: var(--surface-raised); }
.monitor-panel h3 { font-size: 15px; margin: 0 0 6px; overflow-wrap: anywhere; }
.monitor-note { margin: 10px 0 0; overflow-wrap: anywhere; }
.monitor-disk-title { justify-content: space-between; align-items: flex-start; }
.monitor-disk-title h3 span { color: var(--text-muted); font-weight: 400; }
.monitor-disk-title strong { color: var(--orange-soft); font-family: Consolas, monospace; white-space: nowrap; }
.monitor-disk-details { grid-template-columns: repeat(3, minmax(0, 1fr)); }
.monitor-disks { margin-top: 20px; }
.cpu-detail-meta { display: flex; gap: 20px; margin-bottom: 18px; color: var(--text-muted); font-size: 13px; flex-wrap: wrap; }
.cpu-package-list { display: grid; gap: 16px; }
.cpu-package-card { padding: 16px; border: 1px solid var(--border); border-radius: var(--radius); background: var(--surface-raised); }
.cpu-package-card h3, .cpu-logical-section h3 { margin: 0; font-size: 16px; }
.cpu-package-details { margin-top: 14px; }
.cpu-logical-section { margin-top: 24px; }
.cpu-logical-grid { display: grid; grid-template-columns: repeat(4, minmax(0, 1fr)); gap: 10px; margin-top: 12px; }
.cpu-logical-item { padding: 10px; border: 1px solid var(--border); border-radius: 6px; }
.cpu-logical-item span, .cpu-logical-item strong { display: block; }
.cpu-logical-item span { color: var(--text-muted); font-size: 12px; }
.cpu-logical-item strong { margin-top: 3px; font-family: Consolas, monospace; font-weight: 500; }
.cpu-logical-item .ant-progress { margin-top: 4px; }
.gpu-detail-grid { display: grid; gap: 16px; }
.gpu-detail-card { min-width: 0; padding: 18px; border: 1px solid var(--border); border-radius: var(--radius); background: var(--surface-raised); }
.gpu-detail-card h3, .gpu-counter-section h3 { margin: 0; font-size: 15px; overflow-wrap: anywhere; }
.gpu-metrics { grid-template-columns: repeat(3, minmax(0, 1fr)); }
.gpu-process-list, .gpu-counter-section { margin-top: 22px; }
.gpu-process-list h4 { margin: 0 0 8px; font-size: 13px; }
.gpu-process-row { display: flex; justify-content: space-between; gap: 16px; padding: 8px 0; border-top: 1px solid var(--border); overflow-wrap: anywhere; }
.gpu-process-row strong { white-space: nowrap; font-family: Consolas, monospace; font-weight: 500; }
.process-toolbar, .process-actions { display: flex; align-items: center; gap: 12px; }
.process-toolbar { justify-content: space-between; margin-bottom: 12px; flex-wrap: wrap; }
.process-toolbar > div:first-child { display: flex; align-items: baseline; gap: 12px; flex-wrap: wrap; }
.process-toolbar span { color: var(--text-muted); font-size: 12px; }
.process-actions { flex-wrap: nowrap; }
.process-actions .ant-input-affix-wrapper { width: 280px; }
.process-path { display: block; max-width: 300px; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
@media (max-width: 780px) {
    .monitor-grid, .monitor-adapters, .monitor-disks, .monitor-gpu-summary { grid-template-columns: 1fr; }
    .monitor-panel { padding: 20px 18px; }
    .monitor-disk-details { gap: 8px; font-size: 13px; }
    .cpu-logical-grid { grid-template-columns: repeat(2, minmax(0, 1fr)); }
    .gpu-metrics { grid-template-columns: repeat(2, minmax(0, 1fr)); }
    .process-actions { flex-wrap: wrap; width: 100%; }
    .process-actions .ant-input-affix-wrapper { width: 100%; }
}
</style>
