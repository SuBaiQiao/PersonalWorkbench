/** 磁盘路径由服务器生成；这里仅负责显示，不用浏览器路径规则改写服务器路径。 */
export function pathCrumbs(path, roots) {
    const windows = /^[a-z]:[\\/]/i.test(path);
    const comparable = value => windows ? value.toLowerCase().replaceAll('/', '\\') : value;
    const root = [...roots].sort((a, b) => b.path.length - a.path.length).find(item => {
        const prefix = comparable(item.path);
        const target = comparable(path);
        return target === prefix || target.startsWith(prefix.endsWith(windows ? '\\' : '/') ? prefix : prefix + (windows ? '\\' : '/'));
    });
    if (!root) return [];
    const separator = windows ? '\\' : '/';
    let current = root.path;
    const crumbs = [{ name: root.name, path: current }];
    for (const name of path.slice(root.path.length).split(windows ? /[\\/]/ : /\//).filter(Boolean)) {
        current = current.replace(/[\\/]$/, '') + separator + name;
        crumbs.push({ name, path: current });
    }
    return crumbs;
}

export function diskSize(bytes) {
    if (bytes == null || !Number.isFinite(bytes) || bytes < 0) return '—';
    const units = ['B', 'KB', 'MB', 'GB', 'TB', 'PB'];
    let value = bytes, index = 0;
    while (value >= 1024 && index < units.length - 1) { value /= 1024; index++; }
    return `${index === 0 ? value : value.toFixed(value >= 100 ? 0 : 1)} ${units[index]}`;
}

export function fileDate(value) {
    const date = new Date(value);
    return value && !Number.isNaN(date.valueOf()) ? date.toLocaleString('zh-CN', { hour12: false }) : '—';
}

/** textarea 会把换行规范为 LF；编辑前记录原换行，保存时还原，避免整份文件出现无意义差异。 */
export function detectLineEnding(text) {
    const crlf = (text.match(/\r\n/g) || []).length;
    const lf = (text.match(/(?<!\r)\n/g) || []).length;
    const cr = (text.match(/\r(?!\n)/g) || []).length;
    if (crlf >= Math.max(lf, cr) && crlf > 0) return 'CRLF';
    return cr > lf ? 'CR' : 'LF';
}
export const normalizeNewlines = text => text.replace(/\r\n|\r/g, '\n');
export const restoreNewlines = (text, ending) => normalizeNewlines(text).replaceAll('\n', ending === 'CRLF' ? '\r\n' : ending === 'CR' ? '\r' : '\n');
export const fileContentUrl = (path, download = false) => `/api/files/content?${new URLSearchParams({ path, download: String(download) })}`;
