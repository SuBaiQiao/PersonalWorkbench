<script setup>
import { ref, watch } from 'vue';
import ToolIntro from '../../components/ToolIntro.vue';
import { generateModels } from '../../utils/modelGenerator.js';
import { copyWithFeedback } from '../../utils/clipboard.js';
const input = ref('{\n  "id": 1,\n  "display_name": "Alex",\n  "active": true,\n  "address": { "city": "Singapore" },\n  "tags": ["developer", "design"],\n  "orders": [{"id": 1, "note": "first"}, {"id": 2}]\n}');
const language = ref('csharp'), rootName = ref('Root'), output = ref(''), error = ref('');
watch([input,language,rootName], () => { output.value = ''; error.value = ''; });
function generate() { error.value = ''; output.value = ''; try { output.value = generateModels(input.value, { language:language.value, rootName:rootName.value }); } catch (e) { error.value = e.message; } }
generate();
</script>
<template><section><ToolIntro title="数据转代码" eyebrow="DEVELOPER / MODELS" description="用 JSON 样本生成 C# 实体与 TypeScript 类型，省去重复定义字段的时间。" /><div class="utility-grid"><section class="panel utility-panel"><div class="utility-controls"><label class="utility-field">目标语言<a-select v-model:value="language" aria-label="目标代码语言" :options="[{value:'csharp',label:'C# 实体'},{value:'typescript',label:'TypeScript 类型'}]" /></label><label class="utility-field">根类型名称<a-input v-model:value="rootName" aria-label="根类型名称" /></label></div><label class="utility-field utility-gap">JSON 样本<a-textarea v-model:value="input" :rows="16" aria-label="代码生成 JSON 样本" class="utility-code" spellcheck="false" /></label><div class="utility-toolbar utility-gap"><a-button type="primary" @click="generate">生成代码</a-button></div><p class="utility-note">递归处理嵌套对象与数组，合并数组样本中的缺失字段和空值。C# 保留原 JSON 字段映射，混合类型使用 JsonElement；空数组暂用未知类型。</p></section><section class="panel utility-panel"><div class="panel-heading"><h2>生成代码</h2><a-button :disabled="!output" @click="copyWithFeedback(output, '代码已复制。')">复制代码</a-button></div><a-alert v-if="error" :message="error" type="error" role="alert" /><pre v-if="output" class="utility-output utility-code">{{ output }}</pre><div v-else-if="!error" class="utility-empty">生成代码会显示在这里</div><p class="utility-note">类型根据样本推断；日期字符串保留为 string。只有 null 的字段需按实际业务补充类型。</p></section></div></section></template>
