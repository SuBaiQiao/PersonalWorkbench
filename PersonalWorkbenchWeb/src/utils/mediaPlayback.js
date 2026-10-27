/** 识别常见容器与流协议；无后缀的签名地址可以在界面手动指定播放格式。 */
export function detectMediaType(value) {
    if (/^rt(?:sp|mps?):\/\//i.test(value)) return 'relay';
    const path = value.split(/[?#]/)[0].toLowerCase();
    if (path.endsWith('.m3u8') || path.endsWith('.m3u')) return 'hls';
    if (path.endsWith('.flv')) return 'flv';
    if (/\.(ts|m2ts|mts)$/.test(path)) return 'mpegts';
    return 'native';
}

/** 网络输入不接受本地路径或任意脚本协议；本地文件必须由用户通过文件选择器授权读取。 */
export function validateMediaUrl(value) {
    const text = value.trim();
    let url;
    try { url = new URL(text); } catch { throw new Error('请输入完整的视频地址，例如 https:// 或 rtsp:// 开头的地址。'); }
    if (!['http:', 'https:', 'rtsp:', 'rtmp:', 'rtmps:'].includes(url.protocol) || !url.hostname)
        throw new Error('网络地址仅支持 HTTP、HTTPS、RTSP、RTMP 和 RTMPS；本地视频请使用文件入口。');
    return text;
}

/** 状态栏隐藏网络地址中的认证信息，避免账号或签名参数出现在播放标题中。 */
export function mediaDisplayName(value) {
    try {
        const url = new URL(value);
        return `${url.protocol}//${url.host}${url.pathname}`;
    } catch { return value; }
}

function normalizePath(value) {
    const parts = [];
    for (const part of value.replace(/\\/g, '/').split('/')) {
        if (!part || part === '.') continue;
        if (part === '..') { if (!parts.length) throw new Error('播放列表引用超出所选文件夹，请选择包含完整视频资源的上级文件夹。'); parts.pop(); }
        else parts.push(part);
    }
    return parts.join('/');
}

/** 返回可选文件与 m3u8 入口；文件夹中的其他资源只在实际被清单引用时创建对象 URL。 */
export function indexLocalMedia(files) {
    const entries = new Map();
    for (const file of files) {
        const name = normalizePath(file.webkitRelativePath || file.name);
        if (entries.has(name)) throw new Error(`所选文件存在重复路径：${name}`);
        entries.set(name, file);
    }
    return { entries, playlists: [...entries.keys()].filter(name => /\.m3u8?$/i.test(name)) };
}

/**
 * 将本地 HLS 清单及其子清单、分片、初始化段、加密密钥改写为浏览器对象 URL。
 * 浏览器不能凭 m3u8 文件直接访问相邻文件，必须由用户一起选择分片或选择完整文件夹。
 * 返回 dispose 统一释放资源；发生缺失文件、循环清单等错误时也会立即回收已分配的 URL。
 */
export async function prepareLocalHls(entries, entry, urlApi = URL) {
    const urls = [];
    const resolved = new Map();
    const visiting = new Set();
    const dispose = () => { for (const url of urls.splice(0)) urlApi.revokeObjectURL(url); };
    const create = blob => { const url = urlApi.createObjectURL(blob); urls.push(url); return url; };

    async function resource(reference, parent) {
        if (/^https?:\/\//i.test(reference)) return reference;
        if (reference.startsWith('//')) return `${location.protocol}${reference}`;
        if (/^[a-z][a-z\d+.-]*:/i.test(reference)) throw new Error(`本地清单包含无法直接访问的地址：${reference}。请使用相对路径或 HTTP 地址。`);
        let decoded;
        try { decoded = decodeURIComponent(reference.split(/[?#]/)[0]); }
        catch { throw new Error(`播放列表包含无效的资源路径：${reference}`); }
        const directory = parent.includes('/') ? parent.slice(0, parent.lastIndexOf('/') + 1) : '';
        let key = normalizePath(decoded.startsWith('/') ? decoded : directory + decoded);
        if (!entries.has(key) && decoded.startsWith('/')) {
            const matches = [...entries.keys()].filter(name => name.endsWith('/' + key) || name === key);
            if (matches.length === 1) key = matches[0];
        }
        if (!entries.has(key)) throw new Error(`缺少本地资源：${reference}。请重新选择 m3u8 所在的完整文件夹及分片。`);
        return resolve(key);
    }

    async function resolve(key) {
        if (visiting.has(key)) throw new Error(`播放列表存在循环引用：${key}`);
        if (resolved.has(key)) return resolved.get(key);
        const file = entries.get(key);
        if (!file) throw new Error(`找不到播放列表：${key}`);
        visiting.add(key);
        let url;
        if (/\.m3u8?$/i.test(key)) {
            const text = (await file.text()).replace(/^\uFEFF/, '');
            if (!text.trimStart().startsWith('#EXTM3U')) throw new Error(`文件不是有效的 HLS 播放列表：${key}`);
            const lines = [];
            for (const line of text.split(/\r?\n/)) {
                const trimmed = line.trim();
                if (!trimmed || trimmed.startsWith('#')) {
                    // EXT-X-KEY、EXT-X-MAP、EXT-X-MEDIA 等标签中的 URI 同样需要改写。
                    let rewritten = line;
                    const matches = [...line.matchAll(/\bURI="([^"]+)"/g)];
                    for (const match of matches) rewritten = rewritten.replace(match[0], `URI="${await resource(match[1], key)}"`);
                    lines.push(rewritten);
                } else lines.push(await resource(trimmed, key));
            }
            url = create(new Blob([lines.join('\n')], { type: 'application/vnd.apple.mpegurl' }));
        } else url = create(file);
        visiting.delete(key);
        resolved.set(key, url);
        return url;
    }
    try { return { url: await resolve(entry), dispose }; }
    catch (error) { dispose(); throw error; }
}

export function formatMediaTime(seconds) {
    if (!Number.isFinite(seconds)) return '直播';
    const total = Math.max(0, Math.floor(seconds));
    const hours = Math.floor(total / 3600);
    return [hours || null, Math.floor(total / 60) % 60, total % 60].filter(value => value !== null)
        .map((value, index) => index === 0 ? String(value) : String(value).padStart(2, '0')).join(':');
}

/** 直播使用短缓冲与有限追帧距离；普通文件保留完整的暂停、回看和倍速行为。 */
export function mediaHlsConfig(live, follow = true) {
    return live ? {
        enableWorker: true, lowLatencyMode: true,
        maxBufferLength: 6, maxMaxBufferLength: 12, backBufferLength: 10,
        liveSyncDurationCount: 2, liveMaxLatencyDurationCount: follow ? 6 : Infinity,
        maxLiveSyncPlaybackRate: follow ? 1.1 : 1, liveSyncOnStallIncrease: 0,
        highBufferWatchdogPeriod: 1
    } : { enableWorker: true, maxBufferLength: 30, backBufferLength: 60 };
}

/**
 * 计算直播恢复动作，供定时检查、恢复播放与切回前台共用。
 * 暂停、主动回看和冷却期间不自动跳转；明显落后时追帧，持续停顿时重新启动加载。
 */
export function liveRecoveryAction({ live, follow, paused, seeking, current, target, stalledMs, sinceRecoveryMs }) {
    if (!live || !follow || paused || seeking || !Number.isFinite(target) || target <= 0 || sinceRecoveryMs < 5000) return null;
    const behind = target - current;
    if (behind > 3 || (stalledMs > 4000 && behind > 0.5)) return 'seek';
    if (stalledMs > 6000) return 'reload';
    return null;
}
