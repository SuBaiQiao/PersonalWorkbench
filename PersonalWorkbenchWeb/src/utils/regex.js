function expandReplacement(replacement, match, input) {
    let result = '';
    for (let i = 0; i < replacement.length; i++) {
        let part = replacement[i];
        if (part === '$' && i + 1 < replacement.length) {
            const next = replacement[i + 1];
            if (next === '$') { part = '$'; i++; }
            else if (next === '&') { part = match[0]; i++; }
            else if (next === '`') { part = input.slice(0,match.index); i++; }
            else if (next === "'") { part = input.slice(match.index + match[0].length); i++; }
            else if (next === '<' && match.groups) {
                const end = replacement.indexOf('>',i+2);
                if (end >= 0) { part = match.groups[replacement.slice(i+2,end)] ?? ''; i = end; }
            }
            else if (/\d/.test(next)) {
                const two = Number(replacement.slice(i+1,i+3));
                const one = Number(next);
                if (two > 0 && two < match.length && /\d/.test(replacement[i+2] || '')) { part = match[two] ?? ''; i += 2; }
                else if (one > 0 && one < match.length) { part = match[one] ?? ''; i++; }
            }
        }
        result += part;
        if (result.length > 300_000) throw new Error('替换结果超过 30 万字符，请缩小内容。');
    }
    return result;
}
export function evaluateRegex({ pattern, flags, text, replacement }) {
    const regex = new RegExp(pattern, flags), matches = [];
    let match, cursor = 0, replaced = '', total = 0;
    while ((match = regex.exec(text)) !== null) {
        if (matches.length < 500) matches.push({ index:match.index, end:match.index+match[0].length, text:match[0], groups:match.slice(1), named:match.groups || null });
        total++;
        replaced += text.slice(cursor,match.index) + expandReplacement(replacement,match,text);
        cursor = match.index + match[0].length;
        if (replaced.length > 300_000) throw new Error('替换结果超过 30 万字符，请缩小内容。');
        if (!regex.global) break;
        if (match[0] === '') regex.lastIndex += (regex.unicode || regex.unicodeSets) && text.codePointAt(regex.lastIndex) > 0xffff ? 2 : 1;
    }
    replaced += text.slice(cursor);
    if (replaced.length > 300_000) throw new Error('替换结果超过 30 万字符，请缩小内容。');
    return { matches, replaced, limited:total > 500 };
}
