<script setup>
import { computed, nextTick, onBeforeUnmount, onMounted, ref } from 'vue';
import Hls from 'hls.js';
import mpegts from 'mpegts.js';
import ToolIntro from '../../components/ToolIntro.vue';
import WorkbenchIcon from '../../components/WorkbenchIcon.vue';
import { api, getErrorMessage } from '../../api/client.js';
import { detectMediaType, validateMediaUrl, mediaDisplayName, indexLocalMedia, prepareLocalHls, formatMediaTime, mediaHlsConfig, liveRecoveryAction } from '../../utils/mediaPlayback.js';

const video = ref(null), fileInput = ref(null), folderInput = ref(null);
const sourceTab = ref('local'), networkUrl = ref(''), format = ref('auto'), serverMode = ref(false), isLive = ref(false);
// Ant Form 必须绑定 model 才会完成校验并触发 finish；派生对象只随地址变化更新。
const networkForm = computed(() => ({ url: networkUrl.value }));
const capabilities = ref(null), busy = ref(false), status = ref('选择视频文件，或输入网络地址开始播放。');
const error = ref(''), name = ref(''), engine = ref(''), currentTime = ref(0), duration = ref(0), resolution = ref('—');
const rate = ref(1), muted = ref(false), playlist = ref(''), playlistOptions = ref([]), selectedFile = ref(null);
const uploadProgress = ref(0), hasSource = ref(false), playing = ref(false);
const canRelay = computed(() => capabilities.value?.available === true);
// 实际直播状态来自服务端或清单，不能只依赖用户是否勾选“直播源”。
const liveMode = ref(false), followLive = ref(true), liveDelay = ref(null);
let localEntries = new Map(), localResource, hls, flv, sessionId, pollTimer, operation = 0, controller;
let mediaRecoveries = 0;
let liveTimer, reconnectTimer, activeRelayRequest, reconnectCount = 0, autoPlaySource = true;
let lastProgressAt = 0, lastProgressPosition = 0, lastRecoveryAt = 0, healthySince = 0;

/** 每次切换来源先释放引擎、对象 URL 和服务器会话，异步操作用序号隔离，避免旧请求覆盖新视频。 */
function clearPlayback(keepReconnectCount = false) {
    operation++;
    controller?.abort(); controller = undefined;
    clearTimeout(pollTimer);
    clearTimeout(reconnectTimer); reconnectTimer = undefined;
    clearInterval(liveTimer); liveTimer = undefined;
    hls?.destroy(); hls = undefined;
    if (flv) { flv.pause(); flv.unload(); flv.detachMediaElement(); flv.destroy(); flv = undefined; }
    if (video.value) { video.value.pause(); video.value.removeAttribute('src'); video.value.load(); }
    localResource?.dispose(); localResource = undefined;
    if (sessionId) { api.delete(`/api/media/sessions/${sessionId}`).catch(() => {}); sessionId = undefined; }
    hasSource.value = false; playing.value = false; busy.value = false;
    uploadProgress.value = 0;
    engine.value = ''; currentTime.value = 0; duration.value = 0; resolution.value = '—';
    mediaRecoveries = 0;
    liveMode.value = false; liveDelay.value = null;
    lastProgressAt = 0; lastRecoveryAt = 0; lastProgressPosition = 0; healthySince = 0;
    if (!keepReconnectCount) { reconnectCount = 0; activeRelayRequest = undefined; followLive.value = true; }
}

function stopPlayback() { clearPlayback(); error.value = ''; status.value = '已停止播放。'; }
function playbackError(message) { error.value = message; status.value = '播放遇到问题'; busy.value = false; }

/** 浏览器可能要求用户再次点击播放；保留可操作的视频控件，不把自动播放限制当作视频损坏。 */
async function tryPlay() {
    if (!autoPlaySource) return;
    try { await video.value?.play(); }
    catch (reason) { if (reason.name === 'NotAllowedError') status.value = '视频已准备好，请点击播放器中的播放按钮。'; }
}

function liveTarget() {
    if (Number.isFinite(hls?.liveSyncPosition)) return hls.liveSyncPosition;
    const ranges = video.value?.seekable;
    return ranges?.length ? Math.max(ranges.start(ranges.length - 1), ranges.end(ranges.length - 1) - 2) : null;
}

/** 追到当前直播清单的安全播放点；与用户暂停分开处理，自动恢复不会强行取消暂停。 */
function recoverLive(manual = false, reloadOnly = false) {
    if (!liveMode.value || !video.value || (!manual && video.value.paused)) return;
    const target = liveTarget();
    if (!reloadOnly && Number.isFinite(target) && target > 0) video.value.currentTime = target;
    // 源端尚无新分片时只恢复加载，不倒退重播旧画面。
    if (reloadOnly) hls?.startLoad(-1, true);
    else hls?.startLoad(Number.isFinite(target) && target > 0 ? target : -1);
    video.value.playbackRate = 1;
    lastRecoveryAt = performance.now();
    lastProgressAt = lastRecoveryAt;
    if (manual) {
        followLive.value = true;
        if (hls) { hls.config.liveMaxLatencyDurationCount = 6; hls.config.maxLiveSyncPlaybackRate = 1.1; }
        autoPlaySource = true; tryPlay();
    }
}

/** 每秒检查一次播放进度；落后或停顿才干预，正常播放不反复 seek 或重启加载。 */
function monitorLive() {
    const element = video.value;
    if (!liveMode.value || !element || !hasSource.value || reconnectTimer) return;
    const now = performance.now();
    liveDelay.value = Number.isFinite(hls?.latency) ? Math.max(0, hls.latency) : null;
    if (Math.abs(element.currentTime - lastProgressPosition) > 0.1) {
        lastProgressPosition = element.currentTime; lastProgressAt = now;
        if (!healthySince) healthySince = now;
        if (now - healthySince > 10000) reconnectCount = 0;
    } else if (!element.paused && now - lastProgressAt > 4000) healthySince = 0;
    const action = liveRecoveryAction({ live: true, follow: followLive.value, paused: element.paused,
        seeking: element.seeking, current: element.currentTime, target: liveTarget(),
        stalledMs: now - lastProgressAt, sinceRecoveryMs: now - lastRecoveryAt });
    if (action) { status.value = '正在追赶最新直播画面…'; recoverLive(false, action === 'reload'); }
}

function setLiveMode(live) {
    liveMode.value = live;
    clearInterval(liveTimer); liveTimer = undefined;
    if (live) {
        lastProgressAt = performance.now(); lastProgressPosition = video.value?.currentTime || 0;
        liveTimer = setInterval(monitorLive, 1000);
    }
}

/** 已建立的直播连接失败时有限重试，固定使用原始来源，避免把正在编辑的新地址意外提交。 */
function reconnectLive(token) {
    if (!liveMode.value || !activeRelayRequest || reconnectCount >= 3 || reconnectTimer) return false;
    const element = video.value;
    const request = { ...activeRelayRequest, resume: hasSource.value ? !element?.paused || element?.ended || Boolean(element?.error) : autoPlaySource };
    const attempt = ++reconnectCount;
    clearTimeout(pollTimer); hls?.stopLoad();
    busy.value = true; error.value = ''; status.value = `直播连接中断，正在自动重连（${attempt}/3）…`;
    reconnectTimer = setTimeout(() => {
        reconnectTimer = undefined;
        if (token === operation) startRelay(undefined, request);
    }, 1000 * 2 ** (attempt - 1));
    return true;
}

function onPlay() {
    playing.value = true;
    if (liveMode.value && followLive.value) {
        if (hls) { hls.config.liveMaxLatencyDurationCount = 6; hls.config.maxLiveSyncPlaybackRate = 1.1; }
        lastRecoveryAt = 0; monitorLive();
    }
}

function onPause() {
    playing.value = false;
    // HLS 内部也有直播硬追帧逻辑，暂停期间必须同时关闭，不能只暂停页面自己的检查器。
    if (liveMode.value && hls) { hls.config.liveMaxLatencyDurationCount = Infinity; hls.config.maxLiveSyncPlaybackRate = 1; }
}

function onVisible() {
    if (!document.hidden && liveMode.value && followLive.value && !video.value?.paused) { lastRecoveryAt = 0; monitorLive(); }
}

function changeFollowLive(event) {
    const enabled = event.target.checked;
    if (hls) { hls.config.liveMaxLatencyDurationCount = enabled ? 6 : Infinity; hls.config.maxLiveSyncPlaybackRate = enabled ? 1.1 : 1; }
    if (enabled) { lastRecoveryAt = 0; monitorLive(); }
    else if (video.value) video.value.playbackRate = 1;
}

/** HLS 自动小幅加速不覆盖用户倍速选择，也不让选择器显示瞬时追帧速度。 */
function onRateChange() {
    if (!liveMode.value || !followLive.value) rate.value = video.value.playbackRate;
}

function attach(url, type, live = isLive.value) {
    hasSource.value = true;
    setLiveMode(live);
    const element = video.value;
    element.muted = muted.value; element.playbackRate = rate.value;
    if (type === 'hls') {
        if (Hls.isSupported()) {
            engine.value = 'HLS';
            const instance = new Hls(mediaHlsConfig(live, followLive.value));
            hls = instance;
            const token = operation;
            instance.on(Hls.Events.LEVEL_LOADED, (_, data) => {
                if (token !== operation) return;
                // 网络 HLS 自动识别直播；本地静态清单不会因为缺少 ENDLIST 被误当成实时源。
                const actualLive = live || (!url.startsWith('blob:') && data.details.live);
                if (actualLive && !liveMode.value) {
                    Object.assign(instance.config, mediaHlsConfig(true, followLive.value));
                    Object.assign(instance.userConfig, mediaHlsConfig(true, followLive.value));
                    setLiveMode(true);
                }
            });
            hls.on(Hls.Events.MANIFEST_PARSED, () => { busy.value = false; status.value = '视频已就绪'; tryPlay(); });
            hls.on(Hls.Events.ERROR, (_, data) => {
                if (!data.fatal) return;
                // 只记录错误分类，不输出包含账号或签名的视频地址，便于排查持续播放中的解码异常。
                console.warn('视频播放引擎错误', data.type, data.details, element.error?.code,
                    data.type === Hls.ErrorTypes.MEDIA_ERROR ? data.error?.message : '');
                if (liveMode.value && data.type === Hls.ErrorTypes.NETWORK_ERROR) {
                    if (reconnectLive(token)) return;
                }
                if (data.type === Hls.ErrorTypes.MEDIA_ERROR && mediaRecoveries++ < 1) {
                    const resume = !element.paused || element.ended || Boolean(element.error);
                    hls.recoverMediaError();
                    // 重建媒体缓冲后浏览器会回到暂停态，需要恢复原本正在进行的播放。
                    if (resume) tryPlay();
                    return;
                }
                if (liveMode.value && reconnectLive(token)) return;
                playbackError(data.type === Hls.ErrorTypes.NETWORK_ERROR
                    ? '无法加载 HLS 清单或分片。请检查资源是否齐全、地址是否过期、源站是否允许跨域；网络视频也可切换到服务端兼容播放。'
                    : '当前浏览器无法解码此 HLS 视频，请检查视频编码或使用服务端兼容播放。');
                hls.stopLoad();
            });
            hls.attachMedia(element); hls.loadSource(url);
        } else if (element.canPlayType('application/vnd.apple.mpegurl')) {
            engine.value = '原生 HLS'; element.src = url; tryPlay();
        } else playbackError('当前浏览器不支持 HLS，请使用新版浏览器，或使用服务端兼容播放。');
    } else if (type === 'flv' || type === 'mpegts') {
        if (!mpegts.isSupported()) { playbackError('当前浏览器不支持 FLV / TS 播放，请使用服务端兼容播放。'); return; }
        engine.value = type === 'flv' ? 'FLV' : 'MPEG-TS';
        flv = mpegts.createPlayer({ type: type === 'flv' ? 'flv' : 'mpegts', url, isLive: live }, { enableWorker: false, lazyLoad: !live });
        flv.on(mpegts.Events.ERROR, () => playbackError('FLV / TS 播放失败，请检查源站跨域设置或视频编码，也可使用服务端兼容播放。'));
        flv.attachMediaElement(element); flv.load(); tryPlay();
    } else { engine.value = '浏览器原生'; element.src = url; tryPlay(); }
}

async function playLocal() {
    clearPlayback();
    const token = operation;
    error.value = ''; busy.value = true; status.value = '正在读取本地视频…';
    try {
        if (playlist.value) {
            const resource = await prepareLocalHls(localEntries, playlist.value);
            if (token !== operation) { resource.dispose(); return; }
            localResource = resource; name.value = playlist.value; autoPlaySource = true; attach(resource.url, 'hls', false);
        } else if (selectedFile.value) {
            const file = selectedFile.value, url = URL.createObjectURL(file);
            localResource = { dispose: () => URL.revokeObjectURL(url) };
            name.value = file.name; autoPlaySource = true; attach(url, detectMediaType(file.name), false);
        } else throw new Error('请先选择本地视频或 m3u8 文件夹。');
    } catch (reason) { if (token === operation) playbackError(reason.message); }
}

/** 文件选择和文件夹选择共用目录索引；含多个播放列表时由用户明确选择入口。 */
async function selectFiles(event) {
    const files = [...(event.target.files || [])]; event.target.value = '';
    if (!files.length) return;
    clearPlayback(); error.value = ''; sourceTab.value = 'local';
    try {
        const index = indexLocalMedia(files);
        localEntries = index.entries;
        playlistOptions.value = index.playlists.map(value => ({ label: value, value }));
        playlist.value = index.playlists.find(value => /(^|\/)master\.m3u8$/i.test(value))
            || index.playlists.find(value => /(^|\/)index\.m3u8$/i.test(value)) || index.playlists[0] || '';
        selectedFile.value = index.playlists.length ? null : files.find(file => /\.(mp4|m4v|mov|webm|mkv|flv|avi|ts|mts|m2ts|mpg|mpeg|wmv|ogv|ogg|3gp|rm|rmvb)$/i.test(file.name)) || files[0];
        await playLocal();
    } catch (reason) { playbackError(reason.message); }
}

/** 会话状态轮询在转换期间快速进行；就绪后作为心跳保持暂停中的会话可用。 */
function scheduleStatus(token, attached = false) {
    pollTimer = setTimeout(async () => {
        if (token !== operation || !sessionId) return;
        try {
            const { data } = await api.get(`/api/media/sessions/${sessionId}`, { signal: controller.signal });
            if (token !== operation) return;
            if (data.status === 'failed') { if (!reconnectLive(token)) playbackError(data.error); return; }
            if (!attached && ['ready', 'completed'].includes(data.status)) {
                attach(data.playlistUrl, 'hls', data.isLive ?? activeRelayRequest?.isLive ?? false); attached = true;
            }
            scheduleStatus(token, attached);
        } catch (reason) { if (token === operation && !reconnectLive(token)) playbackError(await getErrorMessage(reason, '播放会话已结束，请重新打开视频。')); }
    }, attached ? liveMode.value ? 2000 : 20000 : 500);
}

async function startRelay(file, retryRequest = null) {
    clearPlayback(Boolean(retryRequest));
    autoPlaySource = retryRequest ? retryRequest.resume : true;
    const token = operation;
    error.value = ''; busy.value = true; uploadProgress.value = 0;
    controller = new AbortController();
    try {
        if (!canRelay.value) throw new Error(capabilities.value?.message || '服务器兼容播放能力尚未就绪，请稍后重试。');
        let response;
        if (file) {
            if (file.size > capabilities.value.maxUploadBytes) throw new Error('兼容播放单文件最大 2GB，较大的兼容文件请先尝试浏览器直接播放。');
            status.value = '正在上传并准备兼容播放…'; name.value = file.name;
            const form = new FormData(); form.append('file', file);
            response = await api.post('/api/media/sessions/upload', form, { signal: controller.signal, timeout: 0,
                onUploadProgress: event => { if (token === operation) uploadProgress.value = Math.round(event.progress * 100) || 0; } });
        } else {
            const url = retryRequest?.url || validateMediaUrl(networkUrl.value);
            const live = retryRequest?.isLive ?? (isLive.value || detectMediaType(url) === 'relay');
            activeRelayRequest = { url, isLive: live };
            setLiveMode(live);
            name.value = mediaDisplayName(url); status.value = '正在连接视频源并准备兼容播放…';
            response = await api.post('/api/media/sessions', activeRelayRequest, { signal: controller.signal });
        }
        if (token !== operation) { api.delete(`/api/media/sessions/${response.data.id}`).catch(() => {}); return; }
        sessionId = response.data.id;
        if (response.data.status === 'failed') { if (reconnectLive(token)) return; throw new Error(response.data.error); }
        status.value = '正在准备视频分片，首次连接可能需要几秒…';
        scheduleStatus(token);
    } catch (reason) { if (token === operation && !reconnectLive(token)) playbackError(await getErrorMessage(reason)); }
}

async function playNetwork() {
    let url;
    try { url = validateMediaUrl(networkUrl.value); }
    catch (reason) { playbackError(reason.message); return; }
    const type = detectMediaType(url);
    if (serverMode.value || type === 'relay') { await startRelay(); return; }
    clearPlayback(); error.value = ''; busy.value = true; status.value = '正在加载网络视频…'; name.value = mediaDisplayName(url);
    autoPlaySource = true;
    await nextTick(); attach(url, format.value === 'auto' ? type : format.value);
}

function updateMetadata() {
    const element = video.value;
    duration.value = element.duration;
    resolution.value = element.videoWidth ? `${element.videoWidth} × ${element.videoHeight}` : '—';
}
function ready() { busy.value = false; if (!error.value) status.value = '视频已就绪'; updateMetadata(); }
function nativeError() {
    if (video.value?.error && hasSource.value && !hls && !flv)
        playbackError('浏览器无法读取或解码此视频。请检查地址及权限，或点击兼容播放使用服务器转换。');
}
function setRate(value) { if (video.value) video.value.playbackRate = value; }
function seek(seconds) { if (video.value && Number.isFinite(video.value.duration)) { if (liveMode.value) { followLive.value = false; changeFollowLive({ target: { checked: false } }); } video.value.currentTime = Math.max(0, Math.min(video.value.duration, video.value.currentTime + seconds)); } }
async function fullscreen() { try { await video.value?.requestFullscreen(); } catch { error.value = '当前浏览器暂不支持全屏，请使用播放器自带的全屏按钮。'; } }
async function snapshot() {
    try {
        if (!video.value?.videoWidth) throw new Error('请先播放视频，再保存当前画面。');
        const canvas = document.createElement('canvas'); canvas.width = video.value.videoWidth; canvas.height = video.value.videoHeight;
        canvas.getContext('2d').drawImage(video.value, 0, 0);
        const data = canvas.toDataURL('image/png'), link = document.createElement('a'); link.href = data; link.download = `video-frame-${Date.now()}.png`; link.click();
    } catch (reason) { error.value = reason.name === 'SecurityError' ? '源站未授权画面读取，当前网络视频无法截图。' : reason.message; }
}
onMounted(async () => {
    document.addEventListener('visibilitychange', onVisible);
    try { capabilities.value = (await api.get('/api/media/capabilities')).data; }
    catch { capabilities.value = { available: false, message: '暂时无法连接服务器兼容播放服务。' }; }
});
onBeforeUnmount(() => { document.removeEventListener('visibilitychange', onVisible); clearPlayback(); });
</script>

<template>
    <section class="video-player-tool">
        <ToolIntro title="视频播放器" eyebrow="MEDIA / VIDEO PLAYER" description="打开本地影像、HLS 播放列表与网络视频，也能连接 RTSP / RTMP 视频流。" />
        <div class="video-workspace">
            <section class="video-stage panel" aria-label="视频播放区域">
                <div class="video-screen">
                    <video ref="video" aria-label="视频播放器" controls playsinline preload="metadata" @loadedmetadata="updateMetadata" @durationchange="updateMetadata" @canplay="ready" @timeupdate="currentTime = video.currentTime" @volumechange="muted = video.muted" @ratechange="onRateChange" @play="onPlay" @pause="onPause" @error="nativeError"></video>
                    <div v-if="!hasSource" class="video-placeholder"><WorkbenchIcon name="video" /><strong>{{ busy ? '正在准备视频' : '你的下一帧，从这里开始' }}</strong><span>{{ busy ? status : '选择本地文件，或粘贴一个视频地址。' }}</span><div class="video-format-tags"><span>MP4 / WEBM</span><span>M3U8 / FLV</span><span>RTSP / RTMP</span></div></div>
                </div>
                <div class="video-status-bar"><span class="video-source-name" :title="name">{{ name || '尚未打开视频' }}</span><span v-if="engine" class="utility-status">{{ engine }}</span></div>
                <div v-if="liveMode && hasSource" class="video-live-controls"><a-checkbox v-model:checked="followLive" @change="changeFollowLive">自动追踪直播</a-checkbox><span v-if="liveDelay !== null">距直播边缘 {{ liveDelay.toFixed(1) }} 秒</span><a-button @click="recoverLive(true)">回到直播</a-button></div>
                <div class="video-actions"><div><a-button :disabled="!hasSource" @click="seek(-10)">后退 10 秒</a-button><a-button :disabled="!hasSource" @click="seek(10)">前进 10 秒</a-button></div><div><a-select v-model:value="rate" aria-label="播放速度" :disabled="liveMode && followLive" :options="[0.5, 0.75, 1, 1.25, 1.5, 2].map(value => ({ value, label: `${value}×` }))" @change="setRate" /><a-button :disabled="!hasSource" @click="snapshot">截图</a-button><a-button :disabled="!hasSource" @click="fullscreen">全屏</a-button><a-button :disabled="!hasSource && !busy && !sessionId" @click="stopPlayback">停止</a-button></div></div>
                <div class="video-metrics"><span><small>播放进度</small>{{ formatMediaTime(currentTime) }} / {{ formatMediaTime(duration) }}</span><span><small>画面尺寸</small>{{ resolution }}</span><span><small>播放状态</small>{{ playing ? '播放中' : hasSource ? '已加载' : '待打开' }}</span></div>
            </section>
            <aside class="panel video-source-panel">
                <div class="panel-heading"><div><p class="panel-kicker">OPEN A SOURCE</p><h2>打开视频</h2></div><WorkbenchIcon name="video" /></div>
                <a-tabs v-model:activeKey="sourceTab">
                    <a-tab-pane key="local" tab="本地文件">
                        <div class="video-file-picker"><WorkbenchIcon name="files" /><strong>文件留在本机，直接播放</strong><p>可选择视频文件，或同时选择 m3u8 与分片。完整 HLS 目录建议使用文件夹入口。</p><a-button type="primary" @click="fileInput.click()">选择视频文件</a-button><a-button @click="folderInput.click()">打开 m3u8 文件夹</a-button></div>
                        <input ref="fileInput" class="video-file-input" type="file" multiple accept="video/*,.m3u8,.m3u,.flv,.mkv,.avi,.ts,.mts,.m2ts,.wmv,.rm,.rmvb,.key,.m4s" @change="selectFiles" />
                        <input ref="folderInput" class="video-file-input" type="file" webkitdirectory directory multiple @change="selectFiles" />
                        <label v-if="playlistOptions.length" class="utility-field utility-gap">HLS 入口<a-select v-model:value="playlist" :options="playlistOptions" show-search option-filter-prop="label" @change="playLocal" /></label>
                        <div v-if="selectedFile" class="utility-note">已选：{{ selectedFile.name }} <a-button type="link" @click="playLocal">重新播放</a-button></div>
                        <a-button v-if="selectedFile" class="video-relay-button" :disabled="!canRelay || busy" @click="startRelay(selectedFile)">上传并兼容播放</a-button>
                        <p class="utility-note">MKV、AVI、WMV 等是否能直接播放取决于内部编码。兼容播放会上传所选单个视频，由服务器转换；单文件最大 2GB。</p>
                    </a-tab-pane>
                    <a-tab-pane key="network" tab="网络地址">
                        <a-form :model="networkForm" layout="vertical" @finish="playNetwork">
                            <a-form-item label="视频地址" name="url">
                                <a-textarea v-model:value="networkUrl" :rows="4" aria-label="视频地址" placeholder="https://example.com/video.m3u8&#10;rtsp://camera/live&#10;rtmp://server/live/stream" spellcheck="false" />
                            </a-form-item>
                            <a-form-item label="播放格式">
                                <a-select v-model:value="format" :options="[{ value: 'auto', label: '自动识别' }, { value: 'hls', label: 'HLS / m3u8' }, { value: 'flv', label: 'FLV' }, { value: 'mpegts', label: 'MPEG-TS' }, { value: 'native', label: '浏览器原生视频' }]" />
                            </a-form-item>
                            <div class="video-source-options"><a-checkbox v-model:checked="isLive">直播源</a-checkbox><a-checkbox v-model:checked="serverMode">服务端兼容播放</a-checkbox></div>
                            <a-button type="primary" html-type="submit" :loading="busy" block>打开网络视频</a-button>
                        </a-form>
                        <p class="utility-note">RTSP / RTMP 自动通过服务器转流。其他地址默认直接播放；跨域、编码不兼容时可开启兼容播放，视频源需能被服务器访问。</p>
                    </a-tab-pane>
                </a-tabs>
                <div class="video-server-status"><span class="signal-dot" :class="{ unavailable: !canRelay }"></span><span>{{ capabilities ? canRelay ? '服务端兼容播放可用' : '仅浏览器直接播放' : '正在检查兼容播放能力…' }}</span></div>
                <p v-if="capabilities && !canRelay" class="utility-note">{{ capabilities.message }}</p>
            </aside>
        </div>
        <a-progress v-if="busy && uploadProgress > 0 && uploadProgress < 100" :percent="uploadProgress" />
        <a-alert class="utility-gap" :type="error ? 'error' : 'info'" :message="error || status" show-icon />
    </section>
</template>

<style scoped>
/* 按钮水波纹在缩屏后的下一帧才更新宽度，将装饰效果限制在按钮内，避免短暂撑出横向滚动。 */
.video-player-tool :deep(.ant-btn) { overflow: hidden; }
.video-workspace { display: grid; grid-template-columns: minmax(0, 1fr) 340px; gap: 20px; align-items: start; }
.video-stage { overflow: hidden; min-width: 0; }
.video-screen { position: relative; display: grid; aspect-ratio: 16 / 9; place-items: center; overflow: hidden; color: #e9eff9; background: #080e19; }
.video-screen video { display: block; width: 100%; height: 100%; max-height: 70vh; background: #080e19; }
.video-placeholder { position: absolute; inset: 0; display: flex; align-items: center; justify-content: center; flex-direction: column; gap: 12px; padding: 24px; background: radial-gradient(ellipse at 50% 10%, #19314b, #080e19 75%); text-align: center; }
.video-placeholder > .workbench-icon { width: 52px; height: 52px; margin-bottom: 8px; color: #9abef5; }
.video-placeholder strong { font-size: 20px; font-weight: 600; }.video-placeholder > span { color: #a0b0c7; font-size: 13px; }
.video-format-tags { display: flex; flex-wrap: wrap; justify-content: center; gap: 8px; margin-top: 12px; }.video-format-tags span { padding: 3px 8px; border: 1px solid #29405e; border-radius: 6px; color: #a6bad6; font-size: 10px; }
.video-status-bar { display: flex; align-items: center; gap: 12px; padding: 16px 20px 0; }.video-source-name { min-width: 0; flex: 1; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; color: var(--text); font-size: 13px; }
.video-live-controls { display: flex; flex-wrap: wrap; align-items: center; gap: 10px; padding: 14px 20px 0; }.video-live-controls > span { color: var(--text-muted); font-size: 12px; }.video-live-controls > .ant-btn { margin-left: auto; }
.video-actions { display: flex; flex-wrap: wrap; align-items: center; justify-content: space-between; gap: 10px; padding: 18px 20px; }.video-actions > div { display: flex; flex-wrap: wrap; gap: 8px; }.video-actions .ant-select { min-width: 78px; }
.video-metrics { display: flex; flex-wrap: wrap; gap: 28px; padding: 16px 20px; border-top: 1px solid var(--border); color: var(--text); font-size: 13px; }.video-metrics span { display: grid; gap: 4px; }.video-metrics small { color: var(--text-subtle); font-size: 10px; }
.video-source-panel { min-width: 0; padding: 24px; }.video-source-panel .panel-heading { margin-bottom: 10px; }.video-source-panel .panel-heading > .workbench-icon { color: var(--accent); }
.video-file-picker { display: flex; flex-direction: column; align-items: stretch; gap: 10px; padding: 18px; border: 1px dashed var(--border-strong); border-radius: 15px; background: var(--well); }.video-file-picker > .workbench-icon { width: 30px; height: 30px; margin-bottom: 4px; color: var(--accent); }.video-file-picker strong { font-size: 13px; }.video-file-picker p { margin: 0 0 6px; color: var(--text-muted); font-size: 12px; line-height: 1.8; }
.video-file-input { display: none; }.video-source-options { display: flex; flex-direction: column; gap: 12px; margin-bottom: 22px; }.video-relay-button { width: 100%; margin-top: 14px; }.video-server-status { display: flex; align-items: center; gap: 10px; margin-top: 22px; padding-top: 18px; border-top: 1px solid var(--border); color: var(--text-muted); font-size: 12px; }.signal-dot.unavailable { background: var(--text-subtle); box-shadow: none; }
@media (max-width: 1200px) { .video-workspace { grid-template-columns: 1fr; }.video-source-panel { max-width: none; } }
@media (max-width: 600px) { .video-placeholder { gap: 6px; padding: 12px; }.video-placeholder strong { font-size: 15px; }.video-placeholder > .workbench-icon { width: 30px; height: 30px; margin: 0; }.video-placeholder > span { font-size: 11px; }.video-format-tags { margin-top: 4px; }.video-actions { padding: 16px; }.video-source-panel { padding: 20px; } }
</style>
