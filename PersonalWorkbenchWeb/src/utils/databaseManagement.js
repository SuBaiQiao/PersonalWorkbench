export function cellText(value) {
    if (value === null) return 'NULL';
    if (value?.$type === 'blob') return `BLOB · ${Math.floor(value.base64.length * 3 / 4) - (value.base64.match(/=+$/)?.[0].length || 0)} bytes`;
    if (value?.$type === 'integer') return value.value;
    return String(value);
}

export function fieldEditor(value, omitted = false) {
    if (omitted) return { mode: 'default', value: '' };
    if (value === null) return { mode: 'null', value: '' };
    if (value?.$type === 'blob') return { mode: 'blob', value: value.base64 };
    if (value?.$type === 'integer') return { mode: 'integer', value: value.value };
    return { mode: typeof value === 'number' ? 'number' : 'text', value: String(value) };
}

export function fieldValue(field) {
    if (field.mode === 'default') return undefined;
    if (field.mode === 'null') return null;
    if (field.mode === 'text') return field.value;
    if (field.mode === 'blob') {
        if (!/^(?:[A-Za-z0-9+/]{4})*(?:[A-Za-z0-9+/]{2}==|[A-Za-z0-9+/]{3}=)?$/.test(field.value)) throw new Error('BLOB 必须是有效的 Base64。');
        return { $type: 'blob', base64: field.value };
    }
    if (field.mode === 'integer') {
        if (!/^-?\d+$/.test(field.value)) throw new Error('请输入有效整数。');
        const integer = BigInt(field.value);
        if (integer < -9223372036854775808n || integer > 9223372036854775807n) throw new Error('整数超出 SQLite 64 位范围。');
        return { $type: 'integer', value: integer.toString() };
    }
    if (!field.value.trim() || !Number.isFinite(Number(field.value))) throw new Error('请输入有效数值。');
    if (/^-?\d+$/.test(field.value) && !Number.isSafeInteger(Number(field.value))) throw new Error('大整数请使用“64 位整数”类型。');
    return Number(field.value);
}

export const valueModes = [
    { value: 'text', label: '文本' }, { value: 'number', label: '数值' }, { value: 'integer', label: '64 位整数' },
    { value: 'null', label: 'NULL' }, { value: 'blob', label: 'BLOB / Base64' }, { value: 'default', label: '省略 / 默认值' }
];

export const columnTypes = [
    { value: 'INTEGER', label: 'INTEGER · 整数' }, { value: 'TEXT', label: 'TEXT · 文本' },
    { value: 'REAL', label: 'REAL · 浮点数' }, { value: 'NUMERIC', label: 'NUMERIC · 数值' },
    { value: 'BLOB', label: 'BLOB · 二进制' }, { value: 'BOOLEAN', label: 'BOOLEAN · 布尔值' },
    { value: 'DATETIME', label: 'DATETIME · 日期时间' }
];
export const columnIndexOptions = [{ value: 'none', label: '无独立单字段索引' }, { value: 'index', label: '普通索引' }, { value: 'unique', label: '唯一索引' }];
export const defaultModes = [{ value: 'none', label: '无默认值' }, { value: 'text', label: '文本' }, { value: 'number', label: '数值' }, { value: 'null', label: 'NULL' }, { value: 'timestamp', label: '当前时间' }, { value: 'expression', label: 'SQL 表达式' }];

export function columnEditor(column = {}, indexes = []) {
    const definition = column.definition || 'TEXT';
    const type = column.type || definition.match(/^\w+(?:\s*\([^)]*\))?/)?.[0] || 'TEXT';
    const field = { name: column.name || '', type, primaryKey: false, autoIncrement: false, notNull: false, unique: false, index: 'none', originalIndex: null, defaultMode: 'none', defaultValue: '', advanced: false, definition, constraintNames: {}, externalPrimaryKey: false };
    let rest = definition.slice(type.length).trim();
    const seen = new Set();
    while (rest) {
        const named = rest.match(/^CONSTRAINT\s+("(?:[^"]|"")*"|`(?:[^`]|``)*`|\[[^\]]*\]|\S+)\s+/i);
        const prefix = named?.[0] || '';
        const clause = rest.slice(prefix.length);
        let key, length;
        const simple = clause.match(/^(PRIMARY\s+KEY(?:\s+AUTOINCREMENT)?|NOT\s+NULL|UNIQUE)(?=\s|$)/i);
        if (simple) {
            key = /^PRIMARY/i.test(simple[0]) ? 'primaryKey' : /^NOT/i.test(simple[0]) ? 'notNull' : 'unique';
            field[key] = true; field.autoIncrement ||= /AUTOINCREMENT/i.test(simple[0]); length = simple[0].length;
        } else if (/^DEFAULT\s+/i.test(clause)) {
            key = 'default';
            const start = clause.match(/^DEFAULT\s+/i)[0].length;
            const value = clause.slice(start).match(/^(?:'(?:[^']|'')*'|[+-]?(?:\d+(?:\.\d*)?|\.\d+)(?:e[+-]?\d+)?|NULL\b|CURRENT_TIMESTAMP\b)/i)?.[0];
            if (!value) { field.advanced = true; break; }
            field.defaultMode = /^'/i.test(value) ? 'text' : /^NULL$/i.test(value) ? 'null' : /^CURRENT_TIMESTAMP$/i.test(value) ? 'timestamp' : 'number';
            field.defaultValue = field.defaultMode === 'text' ? value.slice(1, -1).replaceAll("''", "'") : value;
            length = start + value.length;
        } else { field.advanced = true; break; }
        if (seen.has(key)) { field.advanced = true; break; }
        seen.add(key); field.constraintNames[key] = prefix; rest = clause.slice(length).trim();
    }
    field.externalPrimaryKey = !!column.primaryKey && !field.primaryKey;
    const simpleIndexes = indexes.filter(i => i.origin === 'c' && !i.partial && i.columns.length === 1 && i.columns[0] === field.name);
    field.index = simpleIndexes.some(i => i.unique) ? 'unique' : simpleIndexes.length ? 'index' : 'none';
    field.originalIndex = column.name ? field.index : null;
    return field;
}

export function columnDefinition(field) {
    if (field.advanced) {
        if (!field.definition.trim()) throw new Error('请输入高级字段定义。');
        return field.definition.trim();
    }
    if (!field.type?.trim()) throw new Error('请选择字段类型。');
    if (field.autoIncrement && (!field.primaryKey || field.type.toUpperCase() !== 'INTEGER')) throw new Error('自增仅适用于 INTEGER 主键。');
    const parts = [field.type.trim()];
    const named = (key, value) => (field.constraintNames[key] || '') + value;
    if (field.primaryKey) parts.push(named('primaryKey', 'PRIMARY KEY' + (field.autoIncrement ? ' AUTOINCREMENT' : '')));
    if (field.notNull) parts.push(named('notNull', 'NOT NULL'));
    if (field.unique) parts.push(named('unique', 'UNIQUE'));
    if (field.defaultMode !== 'none') {
        let value;
        if (field.defaultMode === 'text') value = "'" + field.defaultValue.replaceAll("'", "''") + "'";
        else if (field.defaultMode === 'null') value = 'NULL';
        else if (field.defaultMode === 'timestamp') value = 'CURRENT_TIMESTAMP';
        else if (field.defaultMode === 'number') {
            if (!/^[+-]?(?:\d+(?:\.\d*)?|\.\d+)(?:e[+-]?\d+)?$/i.test(field.defaultValue) || !Number.isFinite(Number(field.defaultValue))) throw new Error('默认值必须是有效数值。');
            value = field.defaultValue;
        } else {
            if (!field.defaultValue.trim()) throw new Error('请输入默认值表达式。');
            value = field.defaultValue.trim();
        }
        parts.push(named('default', 'DEFAULT ' + value));
    }
    return parts.join(' ');
}

export function columnPayload(field) {
    if (!field.name.trim()) throw new Error('请输入字段名称。');
    return { name: field.name.trim(), definition: columnDefinition(field), index: field.index === field.originalIndex ? null : field.index };
}
