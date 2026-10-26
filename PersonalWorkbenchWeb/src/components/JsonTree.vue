<script setup>
import { computed, ref } from 'vue';
const props = defineProps({ value: null, label: { type: String, default: '$' }, depth: { type: Number, default: 0 } });
const branch = computed(() => props.value !== null && typeof props.value === 'object');
const entries = computed(() => branch.value ? Object.entries(props.value) : []);
const expanded = ref(props.depth < 2);
</script>
<template>
    <details v-if="branch" :open="expanded" class="json-tree" @toggle="expanded = $event.target.open"><summary><strong>{{ label }}</strong> <span>{{ Array.isArray(value) ? 'Array' : 'Object' }} · {{ entries.length }}</span></summary><template v-if="expanded"><div v-if="depth < 12" class="json-tree-children"><JsonTree v-for="[key, child] in entries.slice(0, 100)" :key="key" :value="child" :label="key" :depth="depth + 1" /><p v-if="entries.length > 100" class="utility-note">仅展示前 100 个子节点，可用 JSONPath 筛选。</p></div><p v-else class="utility-note">层级较深，请使用 JSONPath 查看。</p></template></details>
    <div v-else class="json-tree-leaf"><strong>{{ label }}</strong><span>{{ JSON.stringify(value) }}</span></div>
</template>
<style scoped>
.json-tree { padding: 5px 0; } summary { cursor: pointer; overflow-wrap: anywhere; } summary span { color: var(--text-muted); font-size: 12px; margin-left: 10px; } .json-tree-children { padding-left: 20px; border-left: 1px solid var(--border); margin-left: 5px; } .json-tree-leaf { padding: 5px 0; display: flex; gap: 12px; overflow-wrap: anywhere; } .json-tree-leaf span { color: var(--accent-strong); white-space: pre-wrap; } strong { font-weight: 500; }
</style>
