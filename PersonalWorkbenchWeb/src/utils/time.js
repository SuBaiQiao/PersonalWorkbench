import { DateTime } from 'luxon';

export const localZone = Intl.DateTimeFormat().resolvedOptions().timeZone || 'UTC';
export const timeZones = [...new Set([localZone, 'Asia/Shanghai', 'Asia/Singapore', 'UTC', 'Asia/Tokyo', 'America/New_York', 'Europe/London'])].map(value => ({ value, label: value === localZone ? `${value}（本地）` : value }));

export function parseTimestamp(text, unit = 'milliseconds') {
    if (!['seconds', 'milliseconds'].includes(unit)) throw new Error('请选择秒或毫秒。');
    if (!/^-?\d+$/.test(String(text).trim())) throw new Error('请输入整数时间戳，支持负数，不支持小数或科学计数法。');
    const value = Number(String(text).trim());
    const milliseconds = unit === 'seconds' ? value * 1000 : value;
    if (!Number.isSafeInteger(milliseconds) || !DateTime.fromMillis(milliseconds).isValid) throw new Error('时间戳超出可支持的日期范围。');
    return milliseconds;
}

export function parseDateTime(text, zone = localZone) {
    const input = String(text).trim().replace(' ', 'T');
    if (!/^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}(?::\d{2}(?:\.\d{1,3})?)?(?:Z|[+-]\d{2}:\d{2})?$/i.test(input)) throw new Error('请输入完整日期和时间，例如 2026-10-22 09:30:00，可附带 Z 或 +08:00。');
    const explicitOffset = /(?:Z|[+-]\d{2}:\d{2})$/i.test(input);
    const date = DateTime.fromISO(input, { zone, setZone: explicitOffset });
    if (!date.isValid) throw new Error('日期、时间或时区无效，请检查月份、日期和时间范围。');
    if (!explicitOffset) {
        const normalizedInput = input.slice(0, 16);
        if (date.toFormat("yyyy-MM-dd'T'HH:mm") !== normalizedInput) throw new Error('该本地时间因夏令时切换而不存在，请选择其他时间。');
        if (date.getPossibleOffsets().length > 1) throw new Error('该本地时间在夏令时切换时出现两次，请附带明确偏移量，例如 -04:00 或 -05:00。');
    }
    return date.toMillis();
}

export function formatDateInput(milliseconds, zone = localZone) {
    return DateTime.fromMillis(milliseconds, { zone }).toFormat('yyyy-MM-dd HH:mm:ss.SSS');
}

export function describeInstant(milliseconds, zone = localZone) {
    const date = DateTime.fromMillis(milliseconds, { zone });
    if (!date.isValid) throw new Error('日期或时区无效。');
    return { milliseconds: String(milliseconds), seconds: String(Math.floor(milliseconds / 1000)), date: date.toFormat('yyyy-MM-dd HH:mm:ss.SSS ZZ'), utc: date.toUTC().toISO() };
}
