<script setup>
import { computed, onBeforeUnmount, ref, shallowRef, watch } from 'vue';
import ToolIntro from '../../components/ToolIntro.vue';
import { copyWithFeedback } from '../../utils/clipboard.js';
const examples = [
    { label: '邮箱', pattern: '[\\w.+-]+@[\\w.-]+\\.[A-Za-z]{2,}', text: '联系 hello@example.com 或 work@example.org' },
    { label: '命名分组', pattern: '(?<year>\\d{4})-(?<month>\\d{2})-(?<day>\\d{2})', text: '日期：2026-10-22 与 2027-01-01' },
    { label: '网址', pattern: 'https?://[^\\s]+', text: '访问 https://example.com 和 http://localhost:5500' }
];
const pattern = ref(examples[0].pattern), text = ref(examples[0].text), flags = ref('g'), replacement = ref('[已替换]');
const result = shallowRef(null), error = ref(''), busy = ref(false);
let worker, timer, timeout;
function stop() { worker?.terminate(); worker = null; clearTimeout(timeout); }
function run() {
    clearTimeout(timer); stop(); result.value = null; error.value = ''; busy.value = false;
    if (!pattern.value) return;
    if (pattern.value.length > 2000 || text.value.length > 100_000 || replacement.value.length > 10_000) { error.value = '表达式最多 2000 字符，测试文本最多 10 万字符，替换内容最多 1 万字符。'; return; }
    busy.value = true;
    worker = new Worker(new URL('../../workers/regex.worker.js', import.meta.url), { type: 'module' });
    timeout = setTimeout(() => { stop(); busy.value = false; error.value = '匹配耗时过长，已停止。请简化表达式或缩小文本范围。'; }, 1500);
    worker.onmessage = ({ data }) => { stop(); busy.value = false; if (data.error) error.value = data.error; else result.value = data; };
    worker.onerror = () => { stop(); busy.value = false; error.value = '表达式执行失败，请检查语法。'; };
    worker.postMessage({ pattern: pattern.value, flags: flags.value, text: text.value, replacement: replacement.value });
}
watch([pattern, text, flags, replacement], () => { stop(); result.value = null; error.value = ''; busy.value = false; clearTimeout(timer); timer = setTimeout(run, 350); });
const highlighted = computed(() => {
    const segments = []; let cursor = 0;
    for (const match of result.value?.matches || []) {
        if (match.index > cursor) segments.push({ value: text.value.slice(cursor, match.index) });
        segments.push({ value: match.text || '∅', hit: true }); cursor = match.end;
    }
    segments.push({ value: text.value.slice(cursor) }); return segments;
});
function load(example) { pattern.value = example.pattern; text.value = example.text; flags.value = 'g'; }
onBeforeUnmount(() => { stop(); clearTimeout(timer); });
run();
</script>
<template><section><ToolIntro title="正则表达式测试" eyebrow="DEVELOPER / REGEXP" description="边写边验证匹配结果，查看捕获分组，并预览替换后的内容。" /><section class="panel utility-panel"><div class="utility-controls"><label class="utility-field" style="flex:4">表达式（无需两端斜杠）<a-input v-model:value="pattern" aria-label="正则表达式" class="utility-code" spellcheck="false" /></label><label class="utility-field">Flags<a-input v-model:value="flags" aria-label="正则 Flags" placeholder="gim" /></label><a-button type="primary" @click="run">运行测试</a-button></div><div class="utility-toolbar utility-gap"><span class="utility-note">常用示例</span><a-button v-for="example in examples" :key="example.label" size="small" @click="load(example)">{{ example.label }}</a-button></div><p class="utility-note">使用 JavaScript 正则语法。g 全局匹配 · i 忽略大小写 · m 多行 · s 点匹配换行 · u Unicode。零宽匹配显示为 ∅。</p></section><div class="utility-grid utility-gap"><section class="panel utility-panel"><label class="utility-field">测试文本<a-textarea v-model:value="text" :rows="9" aria-label="正则测试文本" spellcheck="false" class="utility-code" /></label><label class="utility-field utility-gap">替换内容（支持 $1、$&、$&lt;name&gt;）<a-input v-model:value="replacement" aria-label="正则替换内容" /></label></section><section class="panel utility-panel"><div class="panel-heading"><h2>匹配结果</h2><span class="utility-status">{{ busy ? '正在匹配…' : `${result?.matches.length ?? 0} 个匹配` }}</span></div><a-alert v-if="error" :message="error" type="error" role="alert" /><template v-if="result"><p v-if="result.limited" class="utility-note">仅展示前 500 个匹配。</p><pre class="utility-output utility-code"><template v-for="(segment, i) in highlighted" :key="i"><mark v-if="segment.hit">{{ segment.value }}</mark><template v-else>{{ segment.value }}</template></template></pre><div class="utility-table-wrap utility-gap"><table class="utility-table"><thead><tr><th># / 位置</th><th>匹配与分组</th></tr></thead><tbody><tr v-for="(match,i) in result.matches" :key="i"><td>{{ i + 1 }} / {{ match.index }}</td><td><code>{{ match.text || '∅' }}</code><p v-for="(group, g) in match.groups" :key="g" class="utility-note">${{ g + 1 }} = {{ group ?? '(未匹配)' }}</p><p v-if="match.named" class="utility-note">{{ JSON.stringify(match.named) }}</p></td></tr></tbody></table></div></template></section></div><section v-if="result" class="panel utility-panel utility-gap"><div class="panel-heading"><h2>替换预览</h2><a-button @click="copyWithFeedback(result.replaced, '替换结果已复制。')">复制结果</a-button></div><pre class="utility-output utility-code">{{ result.replaced }}</pre></section></section></template>
<style scoped>mark { color: var(--text); background: color-mix(in srgb,var(--accent) 24%,transparent); border-radius: 3px; padding: 1px 0; }</style>
