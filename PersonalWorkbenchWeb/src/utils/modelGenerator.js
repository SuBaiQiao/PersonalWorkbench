import { parseJson } from './developerTools.js';

function infer(value, depth = 0) {
    if (depth > 20) throw new Error('JSON 层级不能超过 20 层。');
    const kind = value === null ? 'null' : Array.isArray(value) ? 'array' : typeof value === 'number' ? (Number.isSafeInteger(value) ? 'integer' : 'number') : typeof value;
    const schema = { types: new Set([kind]), fields: new Map(), optional: new Set(), item: null };
    if (kind === 'object') for (const [key, child] of Object.entries(value)) schema.fields.set(key, infer(child, depth + 1));
    if (kind === 'array') for (const child of value) schema.item = merge(schema.item, infer(child, depth + 1));
    return schema;
}
function merge(a, b) {
    if (!a) return b;
    if (!b) return a;
    const fields = new Map();
    const optional = new Set([...a.optional, ...b.optional]);
    for (const key of new Set([...a.fields.keys(), ...b.fields.keys()])) {
        fields.set(key, merge(a.fields.get(key), b.fields.get(key)));
        if (a.types.has('object') && b.types.has('object') && (!a.fields.has(key) || !b.fields.has(key))) optional.add(key);
    }
    return { types: new Set([...a.types, ...b.types]), fields, optional, item: merge(a.item, b.item) };
}
function identifier(value, fallback = 'Item') {
    const name = String(value).replace(/([a-z0-9])([A-Z])/g, '$1 $2').split(/[^A-Za-z0-9]+/).filter(Boolean).map(part => part[0].toUpperCase() + part.slice(1)).join('');
    return name ? (/^\d/.test(name) ? '_' + name : name) : fallback;
}
export function generateModels(text, { language = 'csharp', rootName = 'Root' } = {}) {
    if (!/^[A-Za-z_][A-Za-z0-9_]{0,59}$/.test(rootName)) throw new Error('根名称请使用英文字母、数字和下划线，以字母或下划线开头，最长 60 字符。');
    const schema = infer(parseJson(text));
    const usedNames = new Set(['List', 'JsonElement', 'JsonPropertyName', 'JsonPropertyNameAttribute', 'System']);
    const definitions = [];
    let objectCount = 0;
    function unique(name, used = usedNames) { let candidate = name, i = 2; while (used.has(candidate)) candidate = name + i++; used.add(candidate); return candidate; }
    const root = unique(identifier(rootName, 'Root'));
    function render(node, hint, optional = false, forcedName = '') {
        if (!node) return language === 'typescript' ? 'unknown' : 'JsonElement';
        const types = [...node.types].filter(type => type !== 'null');
        if (types.includes('number') && types.includes('integer')) types.splice(types.indexOf('integer'), 1);
        const nullable = node.types.has('null') || optional;
        const renderKind = kind => {
            if (kind === 'object') {
                if (++objectCount > 200) throw new Error('对象结构较多，请使用更小的代表性 JSON 样本（最多 200 个类型）。');
                const name = forcedName || unique(identifier(hint));
                const props = new Set([name]);
                const lines = [...node.fields].map(([key, value]) => {
                    const isOptional = node.optional.has(key);
                    const type = render(value, name + identifier(key), isOptional);
                    if (language === 'typescript') return `  ${JSON.stringify(key)}${isOptional ? '?' : ''}: ${type};`;
                    const propName = unique(identifier(key, 'Value'), props);
                    const init = !type.endsWith('?') && (type === 'string' || type.startsWith('List<') || (!['long','double','bool','JsonElement'].includes(type))) ? ' = default!;' : '';
                    return `    [JsonPropertyName(${JSON.stringify(key)})]\n    public ${type} ${propName} { get; set; }${init}`;
                });
                definitions.push(language === 'typescript' ? `export interface ${name} {\n${lines.join('\n')}\n}` : `public class ${name}\n{\n${lines.join('\n\n')}\n}`);
                return name;
            }
            if (kind === 'array') { const item = render(node.item, hint + 'Item'); return language === 'typescript' ? `Array<${item}>` : `List<${item}>`; }
            return language === 'typescript' ? ({ integer: 'number', number: 'number', string: 'string', boolean: 'boolean' }[kind] || 'unknown') : ({ integer: 'long', number: 'double', string: 'string', boolean: 'bool' }[kind] || 'JsonElement');
        };
        if (language === 'csharp' && types.length > 1) return nullable ? 'JsonElement?' : 'JsonElement';
        const mapped = types.map(renderKind);
        if (language === 'typescript') return [...mapped, ...(node.types.has('null') ? ['null'] : [])].join(' | ') || 'null';
        return (mapped[0] || 'JsonElement') + (nullable ? '?' : '');
    }
    const objectRoot = schema.types.size === 1 && schema.types.has('object');
    const rootType = render(schema, root, false, objectRoot ? root : '');
    const prefix = language === 'typescript' ? (objectRoot ? '' : `export type ${root} = ${rootType};\n\n`) : `#nullable enable\nusing System.Collections.Generic;\nusing System.Text.Json;\nusing System.Text.Json.Serialization;\n\n// 根类型：${rootType}\n\n`;
    return prefix + definitions.reverse().join('\n\n');
}
