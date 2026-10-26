<script setup>
import { ref, shallowRef, watch } from 'vue';
import ToolIntro from '../../components/ToolIntro.vue';
import JsonTree from '../../components/JsonTree.vue';
import { parseJson, queryJson } from '../../utils/developerTools.js';
import { copyWithFeedback } from '../../utils/clipboard.js';
const sample = '{"users":[{"id":1,"name":"小林","active":true},{"id":2,"name":"Alex","active":false}],"total":2}';
const input = ref(sample), output = ref(''), path = ref('$.users[*].name'), indent = ref(2), error = ref(''), tab = ref('text');
const parsed = shallowRef(null), ready = ref(false);
watch(input, () => { ready.value = false; output.value = ''; error.value = ''; });
function run(action) {
    error.value = ''; ready.value = false; output.value = '';
    try {
        const value = parseJson(input.value);
        parsed.value = action === 'query' ? queryJson(value, path.value) : value;
        output.value = JSON.stringify(parsed.value, null, action === 'compress' ? 0 : indent.value);
        ready.value = true;
    } catch (exception) { error.value = exception.message; }
}
run('format');
</script>
<template><section><ToolIntro title="JSON 工具箱" eyebrow="DEVELOPER / JSON" description="整理结构、定位错误、查询数据，让复杂 JSON 清晰可读。" />
<div class="utility-grid"><section class="panel utility-panel"><div class="panel-heading"><h2>原始 JSON</h2><a-button size="small" @click="input = sample">载入示例</a-button></div><label class="utility-field">JSON 内容<a-textarea v-model:value="input" class="utility-code" :rows="18" aria-label="JSON 内容" spellcheck="false" /></label><div class="utility-toolbar utility-gap"><a-button type="primary" @click="run('format')">格式化 / 校验</a-button><a-button @click="run('compress')">压缩</a-button><a-select v-model:value="indent" aria-label="缩进" :options="[{value:2,label:'2 空格'},{value:4,label:'4 空格'}]" /></div><label class="utility-field utility-gap">JSONPath<a-input v-model:value="path" placeholder="$.users[*].name" @press-enter="run('query')" aria-label="JSONPath 查询" /></label><div class="utility-toolbar utility-gap"><a-button @click="run('query')">查询数据</a-button></div><p class="utility-note">支持属性、数组、通配符和递归路径，如 $..name。查询不执行脚本或过滤表达式。</p></section>
<section class="panel utility-panel"><div class="panel-heading"><h2>处理结果</h2><a-button :disabled="!ready" @click="copyWithFeedback(output, 'JSON 已复制。')">复制结果</a-button></div><a-alert v-if="error" :message="error" type="error" show-icon role="alert" /><a-tabs v-model:active-key="tab"><a-tab-pane key="text" tab="文本"><pre v-if="ready" class="utility-output utility-code">{{ output }}</pre><div v-else class="utility-empty">处理结果会显示在这里</div></a-tab-pane><a-tab-pane key="tree" tab="树形结构"><div v-if="ready" class="utility-output utility-code"><JsonTree :value="parsed" /></div><div v-else class="utility-empty">请先格式化或查询 JSON</div></a-tab-pane></a-tabs></section></div><p class="utility-note">内容仅在本地处理。数字采用 JavaScript 数值精度，超大整数建议使用字符串。</p></section></template>
