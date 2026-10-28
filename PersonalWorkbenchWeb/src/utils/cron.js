import { CronExpressionParser } from 'cron-parser';
import cronstrue from 'cronstrue';
import 'cronstrue/locales/zh_CN.js';
import { describeInstant } from './time.js';

// Quartz uses 1=Sunday ... 7=Saturday; cron-parser uses 0=Sunday ... 6=Saturday.
function quartzWeekdays(field) {
    if (field === '*') return field;
    const names = ['SUN', 'MON', 'TUE', 'WED', 'THU', 'FRI', 'SAT'];
    const numeric = field.toUpperCase().replace(/[A-Z]{3}/g, name => {
        const index = names.indexOf(name);
        if (index < 0) throw new Error('星期名称无效。');
        return String(index + 1);
    });
    const days = new Set();
    for (const part of numeric.split(',')) {
        const match = /^(\*|\d+(?:-\d+)?)(?:\/(\d+))?$/.exec(part);
        if (!match) throw new Error('星期字段无效。');
        const step = Number(match[2] ?? 1);
        const [from, explicitEnd] = match[1] === '*' ? [1, 7] : match[1].split('-').map(Number);
        const to = explicitEnd ?? (match[2] ? 7 : from);
        if (from < 1 || to > 7 || from > to || step < 1 || step > 7) throw new Error('Quartz 星期范围为 1（星期日）到 7（星期六），步长为 1-7。');
        for (let day = from; day <= to; day += step) days.add(day - 1);
    }
    return [...days].sort((a, b) => a - b).join(',');
}

export function inspectCron(expression, { zone = 'UTC', start = Date.now(), count = 10 } = {}) {
    if (String(expression).length > 250) throw new Error('Cron 表达式最多支持 250 个字符。');
    const fields = String(expression).trim().split(/\s+/);
    if (![5, 6].includes(fields.length)) throw new Error('请输入五字段 Cron（分钟 小时 日期 月份 星期）或六字段 Quartz（秒 分钟 小时 日期 月份 星期）。');
    const quartz = fields.length === 6;
    if (!fields.every(field => /^[\dA-Za-z*?,/\-]+$/.test(field))) throw new Error('支持数字、英文月份/星期、*、逗号、范围和步长；六字段的日期或星期支持 ?，暂不支持 L、W、#。');
    if (fields.some(field => /[LW#]/i.test(field.replace(/JUL|WED/gi, '')))) throw new Error('暂不支持 L、W、# 扩展。');
    if (quartz) {
        if (fields.some((field, index) => field.includes('?') && !([3, 5].includes(index) && field === '?')) ||
            (fields[3] === '?') === (fields[5] === '?')) {
            throw new Error('六字段 Quartz 的日期和星期必须有且仅有一项使用 ?，且 ? 必须独占该字段。');
        }
    } else if (fields.some(field => field.includes('?'))) {
        throw new Error('五字段 Cron 不支持 ?；请使用六字段 Quartz 格式。');
    }
    if (!Number.isInteger(count) || count < 1 || count > 20) throw new Error('预览次数必须在 1 到 20 之间。');
    if (!Number.isFinite(start)) throw new Error('起始时间无效。');
    try {
        const normalized = fields.join(' ');
        const parseFields = [...fields];
        if (quartz) {
            parseFields[3] = fields[3] === '?' ? '*' : fields[3];
            parseFields[5] = fields[5] === '?' ? '*' : quartzWeekdays(fields[5]);
        }
        const interval = CronExpressionParser.parse(parseFields.join(' '), { currentDate: new Date(start), tz: zone });
        const occurrences = Array.from({ length: count }, () => describeInstant(interval.next().toDate().getTime(), zone));
        const dayOr = !quartz && fields[2] !== '*' && fields[4] !== '*';
        const description = dayOr ? '按以下字段执行；日期与星期采用「或」匹配，满足任一即可。' : cronstrue.toString(normalized, { locale: 'zh_CN', use24HourTimeFormat: true, dayOfWeekStartIndexZero: !quartz });
        const fieldLabels = quartz ? ['秒', '分钟', '小时', '日期', '月份', '星期'] : ['分钟', '小时', '日期', '月份', '星期'];
        return { expression: normalized, description, fields, fieldLabels, occurrences, dayOr, format: quartz ? 'quartz' : 'standard' };
    } catch (error) {
        throw new Error(`表达式无效或无法找到执行时间：${error.message || error}`);
    }
}
