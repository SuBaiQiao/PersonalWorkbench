export function validateRequest({ method = 'GET', url, headers = '', body = '' }) {
    let parsed;
    try { parsed = new URL(url); } catch { throw new Error('请输入完整的 HTTP 或 HTTPS 地址。'); }
    if (!['http:', 'https:'].includes(parsed.protocol) || parsed.username || parsed.password) throw new Error('仅支持 HTTP/HTTPS 地址；身份信息请放在请求头中。');
    method = method.toUpperCase();
    if (!['GET','POST','PUT','PATCH','DELETE','HEAD','OPTIONS'].includes(method)) throw new Error('不支持该请求方法。');
    if (['GET','HEAD'].includes(method) && body.trim()) throw new Error(`${method} 请求不能包含 Body，请清空请求体或更换方法。`);
    const entries = [];
    for (const line of headers.split(/\r?\n/).filter(line => line.trim())) {
        const colon = line.indexOf(':');
        if (colon < 1) throw new Error('请求头每行需要使用 Name: Value 格式。');
        const key = line.slice(0,colon).trim(), value = line.slice(colon+1).trim();
        if (!/^[!#$%&'*+.^_`|~\w-]+$/.test(key) || /[\r\n\0]/.test(value)) throw new Error('请求头名称或内容无效。');
        entries.push([key,value]);
    }
    return { method, url: parsed.href, headers: entries, body };
}
const quote = value => "'" + value.replaceAll("'", "'\\''") + "'";
export function exportCurl(request) {
    const parsed = validateRequest(request);
    if (parsed.body && !parsed.headers.some(([key]) => key.toLowerCase() === 'content-type')) parsed.headers.push(['Content-Type','text/plain;charset=UTF-8']);
    return ['curl', '-X', parsed.method, quote(parsed.url), ...parsed.headers.flatMap(([key,value]) => ['-H',quote(`${key}: ${value}`)]), ...(parsed.body ? ['--data-raw',quote(parsed.body)] : [])].join(' ');
}
function tokenize(command) {
    const tokens = []; let token = '', quoteMode = '', started = false;
    for (let i=0; i<command.length; i++) {
        const char = command[i];
        if (char === '\\' && quoteMode !== "'") { const next = command[++i]; if (next === undefined) throw new Error('cURL 命令以未完成的转义结尾。'); if (next !== '\n' && next !== '\r') { token += next; started = true; } else if (next === '\r' && command[i+1] === '\n') i++; }
        else if (quoteMode) { if (char === quoteMode) quoteMode = ''; else token += char; }
        else if (char === "'" || char === '"') { quoteMode = char; started = true; }
        else if (/\s/.test(char)) { if (started) { tokens.push(token); token = ''; started = false; } }
        else { token += char; started = true; }
    }
    if (quoteMode) throw new Error('cURL 命令引号未闭合。');
    if (started) tokens.push(token);
    return tokens;
}
export function importCurl(command) {
    if (command.length > 200_000) throw new Error('cURL 命令过长。');
    const tokens = tokenize(command.trim());
    if (!/^curl(?:\.exe)?$/i.test(tokens.shift() || '')) throw new Error('命令需要以 curl 开头。');
    let method = '', url = '', body = '', hasBody = false, jsonBody = false;
    const headers = [];
    const value = index => { if (tokens[index] === undefined) throw new Error('cURL 选项缺少参数。'); return tokens[index]; };
    for (let i=0;i<tokens.length;i++) {
        const token = tokens[i];
        if (['-X','--request'].includes(token)) method = value(++i).toUpperCase();
        else if (['-H','--header'].includes(token)) headers.push(value(++i));
        else if (['-d','--data','--data-raw','--data-binary','--json'].includes(token)) {
            const data = value(++i);
            if (token !== '--data-raw' && data.startsWith('@')) throw new Error('不支持读取 @文件，请直接粘贴请求体。');
            if (hasBody) throw new Error('暂不支持多个 data 参数，请合并后导入。');
            body = data; hasBody = true; jsonBody = token === '--json';
        }
        else if (token === '--url') url = value(++i);
        else if (token === '-I' || token === '--head') method = 'HEAD';
        else if (['-L','--location','--compressed','-s','--silent','-S','--show-error'].includes(token)) continue;
        else if (token.startsWith('-')) throw new Error(`暂不支持 cURL 选项 ${token}。支持 -X、-H、--data-raw、--json、--url。`);
        else if (!url) url = token;
        else throw new Error('暂不支持多个请求地址。');
    }
    if (hasBody && !headers.some(header => /^content-type:/i.test(header))) headers.push(`Content-Type: ${jsonBody ? 'application/json' : 'application/x-www-form-urlencoded'}`);
    if (jsonBody && !headers.some(header => /^accept:/i.test(header))) headers.push('Accept: application/json');
    const request = { method: method || (hasBody ? 'POST' : 'GET'), url, headers: headers.join('\n'), body };
    validateRequest(request); return request;
}
export async function readResponse(response, maxBytes = 2_000_000) {
    if (!response.body) return { text: '', bytes: 0 };
    const reader = response.body.getReader(), decoder = new TextDecoder();
    let text = '', bytes = 0;
    try {
        while (true) {
            const { value, done } = await reader.read();
            if (done) break;
            bytes += value.byteLength;
            if (bytes > maxBytes) { await reader.cancel(); throw new Error('响应超过 2 MB，请使用专门的下载工具处理。'); }
            text += decoder.decode(value, { stream: true });
        }
        return { text: text + decoder.decode(), bytes };
    } finally { reader.releaseLock(); }
}
