import { CronExpressionParser } from 'cron-parser';
import cronstrue from 'cronstrue';
import 'cronstrue/locales/zh_CN.js';
import { describeInstant } from './time.js';

export function inspectCron(expression, { zone = 'UTC', start = Date.now(), count = 10 } = {}) {
    if (String(expression).length > 250) throw new Error('Cron 表达式最多支持 250 个字符。');
    const fields = String(expression).trim().split(/\s+/);
    if (fields.length !== 5) throw new Error('请输入标准五字段 Cron：分钟 小时 日期 月份 星期；不支持 Quartz 秒字段。');
    if (!fields.every(field => /^[\dA-Za-z*,/\-]+$/.test(field))) throw new Error('支持数字、英文月份/星期、*、逗号、范围和步长；不支持 ?、L、W、# 等 Quartz 扩展。');
    if (fields.some(field => /[LW#?]/i.test(field.replace(/JUL|WED/gi, '')))) throw new Error('不支持 Quartz 扩展，请使用标准五字段表达式。');
    if (!Number.isInteger(count) || count < 1 || count > 20) throw new Error('预览次数必须在 1 到 20 之间。');
    if (!Number.isFinite(start)) throw new Error('起始时间无效。');
    try {
        const normalized = fields.join(' ');
        const interval = CronExpressionParser.parse(normalized, { currentDate: new Date(start), tz: zone });
        const occurrences = Array.from({ length: count }, () => describeInstant(interval.next().toDate().getTime(), zone));
        const dayOr = fields[2] !== '*' && fields[4] !== '*';
        const description = dayOr ? '按以下字段执行；日期与星期采用「或」匹配，满足任一即可。' : cronstrue.toString(normalized, { locale: 'zh_CN', use24HourTimeFormat: true, dayOfWeekStartIndexZero: true });
        return { expression: normalized, description, fields, occurrences, dayOr };
    } catch (error) {
        throw new Error(`表达式无效或无法找到执行时间：${error.message || error}`);
    }
}
