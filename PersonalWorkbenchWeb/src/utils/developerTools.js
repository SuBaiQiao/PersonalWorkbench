import { JSONPath } from 'jsonpath-plus';
import { diffArrays } from 'diff';
import { parse as parseJsonSyntax, printParseErrorCode } from 'jsonc-parser';

export function parseJson(text) {
    if (text.length > 1_000_000) throw new Error('JSON 内容请控制在 1 MB 字符以内。');
    try { return JSON.parse(text); }
    catch (error) {
        const errors = [];
        parseJsonSyntax(text, errors, { disallowComments:true, allowTrailingComma:false, allowEmptyContent:false });
        if (errors.length) {
            const before = text.slice(0, errors[0].offset);
            const lines = before.split('\n');
            throw new Error(`JSON 语法错误：第 ${lines.length} 行，第 ${lines.at(-1).length + 1} 列（${printParseErrorCode(errors[0].error)}）。`);
        }
        throw new Error(`JSON 语法错误：${error.message}`);
    }
}

export function queryJson(value, path) {
    if (!path.trim().startsWith('$')) throw new Error('JSONPath 需要以 $ 开头，例如 $.users[*].name。');
    if (path.length > 500) throw new Error('JSONPath 不能超过 500 个字符。');
    // Queries never evaluate JavaScript expressions from user input.
    return JSONPath({ path, json: value, eval: false, wrap: true });
}

export function compareText(left, right, { ignoreWhitespace = false, ignoreCase = false } = {}) {
    if (left.length + right.length > 200_000) throw new Error('两侧文本总长度请控制在 20 万字符以内。');
    const a = left.replace(/\r\n?/g, '\n').split('\n');
    const b = right.replace(/\r\n?/g, '\n').split('\n');
    if (Math.max(a.length, b.length) > 5000) throw new Error('单侧文本不能超过 5000 行。');
    const normalize = text => {
        if (ignoreWhitespace) text = text.replace(/\s/g, '');
        return ignoreCase ? text.toLocaleLowerCase() : text;
    };
    const parts = diffArrays(a.map(normalize), b.map(normalize), { timeout: 1000, maxEditLength: 2000 });
    if (!parts) throw new Error('差异较多，超出对比限制，请缩小文本范围。');
    const rows = [];
    let i = 0, j = 0, added = 0, removed = 0;
    for (const part of parts) {
        for (let n = 0; n < part.count; n++) {
            if (part.removed) { rows.push({ type: 'removed', left: a[i], leftLine: ++i }); removed++; }
            else if (part.added) { rows.push({ type: 'added', right: b[j], rightLine: ++j }); added++; }
            else { rows.push({ type: 'same', left: a[i], right: b[j], leftLine: ++i, rightLine: ++j }); }
        }
    }
    return { rows, added, removed };
}

export function decodeJwt(token) {
    const parts = token.trim().replace(/^Bearer\s+/i, '').split('.');
    if (parts.length !== 3 || !parts[0] || !parts[1] || parts.some(part => !/^[\w-]*$/.test(part))) {
        throw new Error('请输入三段式 JWT（Header.Payload.Signature）。');
    }
    const decode = part => {
        if (part.length > 100_000 || part.length % 4 === 1) throw new Error('JWT 分段长度无效。');
        const base64 = part.replace(/-/g, '+').replace(/_/g, '/');
        const bytes = Uint8Array.from(atob(base64.padEnd(Math.ceil(base64.length / 4) * 4, '=')), char => char.charCodeAt(0));
        const value = JSON.parse(new TextDecoder('utf-8', { fatal: true }).decode(bytes));
        if (!value || typeof value !== 'object' || Array.isArray(value)) throw new Error('JWT Header 和 Payload 必须是 JSON 对象。');
        return value;
    };
    try { return { header: decode(parts[0]), payload: decode(parts[1]), signature: parts[2] }; }
    catch (error) { throw new Error(`JWT 解析失败：${error.message}`); }
}

export function jwtDates(payload, now = Date.now()) {
    return ['iat', 'nbf', 'exp'].filter(key => Object.hasOwn(payload, key)).map(key => {
        const value = payload[key];
        const date = typeof value === 'number' ? new Date(value * 1000) : new Date(NaN);
        return { key, value, date: Number.isNaN(date.getTime()) ? '无效 NumericDate' : date.toISOString(),
            state: key === 'exp' ? (now >= value * 1000 ? '已过期' : '尚未过期') : key === 'nbf' ? (now < value * 1000 ? '尚未生效' : '已到生效时间') : '签发时间' };
    });
}

export function secureIndex(size, source = globalThis.crypto) {
    if (!Number.isInteger(size) || size < 1 || size > 0x100000000) throw new Error('随机范围无效。');
    const limit = Math.floor(0x100000000 / size) * size;
    const buffer = new Uint32Array(1);
    do { source.getRandomValues(buffer); } while (buffer[0] >= limit);
    return buffer[0] % size;
}

export function generateRandom({ mode = 'uuid', count = 10, length = 20, groups = ['lower', 'upper', 'digits', 'symbols'], min = 0, max = 100 }) {
    if (!Number.isInteger(count) || count < 1 || count > 500) throw new Error('生成数量需要在 1–500 之间。');
    if (mode === 'number') {
        if (!Number.isSafeInteger(min) || !Number.isSafeInteger(max) || max < min || max - min >= 0x100000000) throw new Error('请输入有效整数范围，跨度不能超过 2³²。');
        return Array.from({ length: count }, () => String(min + secureIndex(max - min + 1)));
    }
    if (mode === 'uuid') return Array.from({ length: count }, () => {
        const bytes = crypto.getRandomValues(new Uint8Array(16));
        bytes[6] = (bytes[6] & 15) | 64;
        bytes[8] = (bytes[8] & 63) | 128;
        const h = Array.from(bytes, byte => byte.toString(16).padStart(2, '0')).join('');
        return `${h.slice(0, 8)}-${h.slice(8, 12)}-${h.slice(12, 16)}-${h.slice(16, 20)}-${h.slice(20)}`;
    });
    const alphabets = { lower: 'abcdefghijklmnopqrstuvwxyz', upper: 'ABCDEFGHIJKLMNOPQRSTUVWXYZ', digits: '0123456789', symbols: '!@#$%^&*()-_=+[]{}:,.?' };
    const selected = [...new Set(groups)].map(key => alphabets[key]).filter(Boolean);
    if (!selected.length) throw new Error('请至少选择一类字符。');
    if (!Number.isInteger(length) || length < 4 || length > 256) throw new Error('长度需要在 4–256 之间。');
    const alphabet = selected.join('');
    return Array.from({ length: count }, () => {
        const chars = mode === 'password' ? selected.map(set => set[secureIndex(set.length)]) : [];
        while (chars.length < length) chars.push(alphabet[secureIndex(alphabet.length)]);
        for (let i = chars.length - 1; i > 0; i--) { const j = secureIndex(i + 1); [chars[i], chars[j]] = [chars[j], chars[i]]; }
        return chars.join('');
    });
}
