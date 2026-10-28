<script setup>
import { computed } from 'vue';
import { columnTypes, columnIndexOptions, defaultModes, columnDefinition, columnEditor } from '../utils/databaseManagement.js';
const props = defineProps({ modelValue: { type: Object, required: true }, disabled: Boolean, primaryKeyUnavailable: Boolean, autoIncrementUnavailable: Boolean, label: { type: String, default: '字段' } });
const emit = defineEmits(['update:modelValue']);
const field = computed(() => props.modelValue);
const typeOptions = computed(() => columnTypes.some(t => t.value === field.value.type) ? columnTypes : [...columnTypes, { value: field.value.type, label: field.value.type }]);
const preview = computed(() => { try { return columnDefinition(field.value); } catch (error) { return error.message; } });
function update(key, value) {
    const next = { ...field.value, [key]: value };
    if (key === 'autoIncrement' && value) { next.type = 'INTEGER'; next.primaryKey = true; next.notNull = true; }
    if ((key === 'primaryKey' && !value) || (key === 'type' && value !== 'INTEGER')) next.autoIncrement = false;
    emit('update:modelValue', next);
}
function changeMode(checked) {
    if (checked) emit('update:modelValue', { ...field.value, definition: preview.value, advanced: true });
    else {
        const parsed = columnEditor({ name: field.value.name, type: undefined, definition: field.value.definition });
        if (parsed.advanced) return;
        emit('update:modelValue', { ...parsed, index: field.value.index, originalIndex: field.value.originalIndex, externalPrimaryKey: field.value.externalPrimaryKey });
    }
}
const canUseSelections = computed(() => !columnEditor({ definition: field.value.definition }).advanced);
</script>

<template>
    <div class="column-editor">
        <div class="column-basic">
            <a-form-item label="字段名" required><a-input :value="field.name" :aria-label="label + '名称'" :maxlength="255" :disabled="disabled" @update:value="update('name', $event)" /></a-form-item>
            <a-form-item label="字段类型" required><a-select :value="field.type" :aria-label="label + '类型'" :options="typeOptions" :disabled="disabled || field.advanced" @update:value="update('type', $event)" /></a-form-item>
        </div>
        <template v-if="!field.advanced">
            <div class="column-switches">
                <label><span>主键</span><a-switch :checked="field.primaryKey || field.externalPrimaryKey" :aria-label="label + '主键'" :disabled="disabled || primaryKeyUnavailable || field.externalPrimaryKey" @update:checked="update('primaryKey', $event)" /></label>
                <label><span>自增</span><a-switch :checked="field.autoIncrement" :aria-label="label + '自增'" :disabled="disabled || primaryKeyUnavailable || autoIncrementUnavailable || field.externalPrimaryKey" @update:checked="update('autoIncrement', $event)" /></label>
                <label><span>非空</span><a-switch :checked="field.notNull" :aria-label="label + '非空'" :disabled="disabled" @update:checked="update('notNull', $event)" /></label>
                <label><span>唯一约束</span><a-switch :checked="field.unique" :aria-label="label + '唯一约束'" :disabled="disabled" @update:checked="update('unique', $event)" /></label>
            </div>
            <p class="column-help">开启自增将自动选择 INTEGER 主键。{{ field.externalPrimaryKey ? '当前主键由表级约束定义，修改请使用 SQL 控制台。' : '' }}</p>
            <div class="column-basic"><a-form-item label="默认值类型"><a-select :value="field.defaultMode" :aria-label="label + '默认值类型'" :options="defaultModes" :disabled="disabled" @update:value="update('defaultMode', $event)" /></a-form-item><a-form-item v-if="['text','number','expression'].includes(field.defaultMode)" label="默认值"><a-input :value="field.defaultValue" :aria-label="label + '默认值'" :disabled="disabled" @update:value="update('defaultValue', $event)" /></a-form-item></div>
        </template>
        <a-form-item label="独立单字段索引" extra="只调整此字段的独立单字段索引；复合索引、部分索引及约束索引保持不变。"><a-select :value="field.index" :aria-label="label + '索引'" :options="columnIndexOptions" :disabled="disabled" @update:value="update('index', $event)" /></a-form-item>
        <div class="column-mode"><span>高级 SQL 定义</span><a-switch :checked="field.advanced" :aria-label="label + '高级SQL定义'" :disabled="disabled || (field.advanced && !canUseSelections)" @update:checked="changeMode" /></div>
        <template v-if="field.advanced"><a-alert type="info" show-icon message="当前定义使用高级模式，完整保留约束。定义可被选择表单完整识别后，才能切回选择模式。" /><a-textarea :value="field.definition" :aria-label="label + '高级字段定义'" :rows="4" :maxlength="4000" :disabled="disabled" @update:value="update('definition', $event)" /></template>
        <div v-else class="column-preview"><span>字段定义预览</span><code>{{ preview }}</code></div>
    </div>
</template>

<style scoped>
.column-editor { min-width: 0; }
.column-basic { display: grid; grid-template-columns: 1fr 1fr; gap: 16px; }
.column-switches { display: grid; grid-template-columns: repeat(4,minmax(0,1fr)); gap: 12px; margin: 0 0 12px; }
.column-switches label { display: flex; align-items: center; justify-content: space-between; gap: 8px; color: var(--text); }
.column-help { color: var(--text-muted); font-size: 12px; margin-bottom: 18px; }
.column-mode { display: flex; gap: 12px; align-items: center; color: var(--text); margin-bottom: 12px; }
.column-preview { background: var(--accent-soft); border-radius: 8px; padding: 12px; }
.column-preview span { display: block; color: var(--text-muted); font-size: 12px; margin-bottom: 6px; }
.column-preview code { color: var(--text); overflow-wrap: anywhere; font-family: Consolas,monospace; }
.column-editor .ant-alert { margin-bottom: 12px; }
@media (max-width:600px) { .column-basic { grid-template-columns: minmax(0,1fr); gap: 0; } .column-switches { grid-template-columns: repeat(2,minmax(0,1fr)); gap: 18px; } }
</style>
