<script setup>
import { computed, onBeforeUnmount, ref, shallowRef, watch } from 'vue';
import ToolIntro from '../../components/ToolIntro.vue';
import { exportCurl, importCurl, readResponse, validateRequest } from '../../utils/httpTool.js';
import { copyWithFeedback } from '../../utils/clipboard.js';
const method = ref('GET'), url = ref(''), headers = ref('Accept: application/json'), body = ref(''), curl = ref(''), importOpen = ref(false), importError = ref('');
const response = shallowRef(null), error = ref(''), busy = ref(false), pretty = ref(true);
let controller;
const responseText = computed(() => { const text = response.value?.text || ''; if (!pretty.value) return text; try { return JSON.stringify(JSON.parse(text),null,2); } catch { return text; } });
watch([method,url,headers,body], () => { response.value = null; error.value = ''; });
function requestValue() { return { method:method.value,url:url.value,headers:headers.value,body:body.value }; }
async function send() {
    if (busy.value) return;
    error.value = ''; response.value = null;
    let timeout;
    try {
        const request = validateRequest(requestValue());
        controller = new AbortController();
        busy.value = true;
        const started = performance.now();
        let timedOut = false;
        timeout = setTimeout(() => { timedOut = true; controller?.abort(); },15000);
        try {
            const result = await fetch(request.url, { method:request.method, headers:request.headers, body:['GET','HEAD'].includes(request.method) ? undefined : request.body || undefined, signal:controller.signal, credentials:'omit', referrerPolicy:'no-referrer' });
            const content = await readResponse(result);
            response.value = { ...content, status:result.status, statusText:result.statusText, headers:[...result.headers.entries()], duration:Math.round(performance.now()-started), url:result.url };
        } catch (e) {
            if (e.name === 'AbortError') throw new Error(timedOut ? '请求超过 15 秒，已停止。' : '请求已取消。');
            if (e instanceof TypeError) throw new Error('请求未完成。请检查地址、网络、HTTPS 混合内容或目标服务的 CORS 配置。');
            throw e;
        }
    } catch (e) { error.value = e.message; }
    finally { clearTimeout(timeout); busy.value = false; controller = null; }
}
function importRequest() {
    importError.value = '';
    try { const data = importCurl(curl.value); method.value = data.method; url.value = data.url; headers.value = data.headers; body.value = data.body; importOpen.value = false; }
    catch (e) { importError.value = e.message; }
}
function copyCurl() { error.value = ''; try { copyWithFeedback(exportCurl(requestValue()), 'cURL 命令已复制。'); } catch (e) { error.value = e.message; } }
onBeforeUnmount(() => controller?.abort());
</script>
<template><section><ToolIntro title="HTTP 请求工具" eyebrow="DEVELOPER / HTTP" description="编辑请求、检查响应，在一个页面完成轻量接口调试。" /><section class="panel utility-panel"><div class="utility-controls"><label class="utility-field" style="flex:0 0 130px">请求方法<a-select v-model:value="method" :disabled="busy" aria-label="HTTP 方法" :options="['GET','POST','PUT','PATCH','DELETE','HEAD','OPTIONS'].map(value=>({value,label:value}))" /></label><label class="utility-field" style="flex:5">请求地址<a-input v-model:value="url" :disabled="busy" aria-label="HTTP 请求地址" placeholder="https://example.com/api/users" @press-enter="send" /></label><a-button v-if="!busy" type="primary" @click="send">发送请求</a-button><a-button v-else danger @click="controller?.abort()">取消请求</a-button></div><div class="utility-toolbar utility-gap"><a-button :disabled="busy" @click="importOpen = true; importError = ''">导入 cURL</a-button><a-button :disabled="busy" @click="copyCurl">复制为 cURL</a-button><span class="utility-status">浏览器直接请求 · 15 秒超时</span></div><p class="utility-note">目标服务需允许当前来源跨域访问。浏览器限制的请求头可能被忽略；不携带浏览器 Cookie，仅展示允许读取的响应头。cURL 使用 Bash/POSIX 引号语法，导入只填充表单。</p><div class="utility-grid utility-gap"><label class="utility-field">请求头（每行 Name: Value）<a-textarea v-model:value="headers" :disabled="busy" :rows="7" aria-label="HTTP 请求头" class="utility-code" spellcheck="false" /></label><label class="utility-field">请求体（原始文本；GET / HEAD 需留空）<a-textarea v-model:value="body" :disabled="busy" :rows="7" aria-label="HTTP 请求体" class="utility-code" spellcheck="false" placeholder='{"name":"Alex"}' /></label></div><a-alert v-if="error" class="utility-error" :message="error" type="error" role="alert" /><p v-if="busy" role="status" class="utility-note">正在等待响应…</p></section><section v-if="response" class="panel utility-panel utility-gap"><div class="panel-heading"><h2>响应 {{ response.status }} {{ response.statusText }}</h2><span class="utility-status">{{ response.duration }} ms · {{ response.bytes.toLocaleString() }} B</span></div><p class="utility-note">{{ response.url }}</p><a-tabs><a-tab-pane key="body" tab="响应内容"><div class="utility-toolbar utility-gap"><a-checkbox v-model:checked="pretty">格式化 JSON</a-checkbox><a-button @click="copyWithFeedback(responseText, '响应内容已复制。')">复制响应</a-button></div><pre class="utility-output utility-code utility-gap">{{ responseText || '(空响应)' }}</pre></a-tab-pane><a-tab-pane key="headers" tab="响应头"><div class="utility-table-wrap"><table class="utility-table"><tbody><tr v-for="[key,value] in response.headers" :key="key"><th>{{ key }}</th><td>{{ value }}</td></tr></tbody></table></div></a-tab-pane></a-tabs></section><a-modal v-model:open="importOpen" title="导入 cURL" ok-text="导入表单" cancel-text="取消" @ok="importRequest"><label class="utility-field">粘贴 cURL 命令<a-textarea v-model:value="curl" :rows="9" aria-label="cURL 命令" class="utility-code" spellcheck="false" /></label><p class="utility-note">支持 -X、-H、--data-raw、--json 和 --url。文件上传及其他选项会提示，不执行命令。</p><a-alert v-if="importError" class="utility-error" :message="importError" type="error" role="alert" /></a-modal></section></template>
