<script setup>
import { computed, onBeforeUnmount, ref, watch } from 'vue';
import { describeInstant, formatDateInput, localZone, parseDateTime, parseTimestamp, timeZones } from '../../utils/time.js';
import { copyWithFeedback } from '../../utils/clipboard.js';

const now = ref(Date.now());
const paused = ref(false);
const zone = ref(localZone);
const unit = ref('milliseconds');
const timestamp = ref(String(now.value));
const dateText = ref(formatDateInput(now.value));
const result = ref(null);
const error = ref('');
const current = computed(() => describeInstant(now.value, zone.value));
const timer = setInterval(() => { if (!paused.value) now.value = Date.now(); }, 1000);
onBeforeUnmount(() => clearInterval(timer));
watch([zone, unit, timestamp, dateText], () => { result.value = null; error.value = ''; });

function convert(kind) {
    result.value = null;
    error.value = '';
    try {
        const milliseconds = kind === 'timestamp' ? parseTimestamp(timestamp.value, unit.value) : parseDateTime(dateText.value, zone.value);
        result.value = describeInstant(milliseconds, zone.value);
    } catch (exception) { error.value = exception.message; }
}
function useNow(kind) {
    const instant = Date.now();
    if (kind === 'timestamp') timestamp.value = unit.value === 'seconds' ? String(Math.floor(instant / 1000)) : String(instant);
    else dateText.value = formatDateInput(instant, zone.value);
}
const rows = computed(() => result.value ? [
    ['所选时区的日期', result.value.date], ['UTC / ISO 8601', result.value.utc],
    ['Unix 时间戳（秒）', result.value.seconds], ['Unix 时间戳（毫秒）', result.value.milliseconds]
] : []);
</script>

<template>
    <section aria-labelledby="timestamp-title">
        <header class="page-intro"><p class="eyebrow">TIME UTILITY / TIMESTAMP</p><h1 id="timestamp-title">时间戳工具</h1><p class="intro-copy">在时间戳与日期之间转换，明确单位与时区，让每一个时间都准确可读。</p></header>
        <section class="panel time-current" aria-label="当前时间">
            <div><p class="panel-kicker">RIGHT NOW</p><h2>当前时间</h2><p class="time-now">{{ current.date }}</p></div>
            <div class="time-current-actions"><a-checkbox v-model:checked="paused">暂停更新</a-checkbox><a-button @click="copyWithFeedback(current.seconds, '秒时间戳已复制。')">复制秒</a-button><a-button @click="copyWithFeedback(current.milliseconds, '毫秒时间戳已复制。')">复制毫秒</a-button></div>
            <div class="time-live-values"><span>秒 <code>{{ current.seconds }}</code></span><span>毫秒 <code>{{ current.milliseconds }}</code></span></div>
        </section>
        <div class="time-zone"><label for="timestamp-zone">转换 / 显示时区</label><a-select id="timestamp-zone" v-model:value="zone" :options="timeZones" aria-label="转换时区" /></div>
        <div class="time-grid">
            <section class="panel time-panel"><div class="panel-heading"><div><p class="panel-kicker">TIMESTAMP → DATE</p><h2>时间戳转日期</h2></div></div>
                <a-form layout="vertical"><a-form-item label="单位"><a-radio-group v-model:value="unit"><a-radio-button value="seconds">秒</a-radio-button><a-radio-button value="milliseconds">毫秒</a-radio-button></a-radio-group></a-form-item><a-form-item label="时间戳"><a-input v-model:value="timestamp" aria-label="待转换时间戳" placeholder="输入整数时间戳" @press-enter="convert('timestamp')" /></a-form-item><div class="time-actions"><a-button type="primary" @click="convert('timestamp')">转换为日期</a-button><a-button @click="useNow('timestamp')">填入当前时间戳</a-button></div></a-form>
            </section>
            <section class="panel time-panel"><div class="panel-heading"><div><p class="panel-kicker">DATE → TIMESTAMP</p><h2>日期转时间戳</h2></div></div>
                <a-form layout="vertical"><a-form-item label="日期时间" extra="无偏移量时按所选时区解释；附带 Z 或 +08:00 时使用输入的偏移量。"><a-input v-model:value="dateText" aria-label="待转换日期" placeholder="2026-10-22 09:30:00.000" @press-enter="convert('date')" /></a-form-item><div class="time-actions"><a-button type="primary" @click="convert('date')">转换为时间戳</a-button><a-button @click="useNow('date')">填入当前日期</a-button></div></a-form>
            </section>
        </div>
        <a-alert v-if="error" class="time-feedback" :message="error" type="error" show-icon role="alert" />
        <section v-if="result" class="panel time-panel time-feedback" aria-label="转换结果" aria-live="polite"><div class="panel-heading"><h2>转换结果</h2><span class="time-muted">{{ zone }}</span></div><div v-for="[label, value] in rows" :key="label" class="time-result-row"><div><span class="time-muted">{{ label }}</span><code>{{ value }}</code></div><a-button size="small" :aria-label="`复制${label}`" @click="copyWithFeedback(value, '结果已复制。')">复制</a-button></div></section>
        <p class="time-muted time-feedback">Unix 时间戳以 1970-01-01 00:00:00 UTC 为起点，本身不带时区。支持负时间戳；秒结果向下取整。</p>
    </section>
</template>

<style scoped>
.time-current,.time-panel { padding: 26px; }
.time-current { display: flex; align-items: center; flex-wrap: wrap; justify-content: space-between; gap: 20px; }
.time-now { margin: 12px 0 0; font-size: clamp(16px,2.3vw,25px); font-variant-numeric: tabular-nums; overflow-wrap: anywhere; }
.time-current-actions,.time-actions,.time-live-values { display: flex; flex-wrap: wrap; gap: 10px; align-items: center; }
.time-live-values { width: 100%; color: var(--text-muted); column-gap: 28px; }
.time-zone { display: flex; align-items: center; flex-wrap: wrap; gap: 16px; margin: 24px 0; font-size: 14px; }
.time-zone .ant-select { width: min(100%,320px); }
.time-grid { display: grid; grid-template-columns: repeat(2,minmax(0,1fr)); gap: 20px; }
.time-panel { min-width: 0; }
.time-feedback { margin-top: 22px; }
.time-muted { color: var(--text-muted); font-size: 13px; }
.time-result-row { display: flex; align-items: center; justify-content: space-between; gap: 16px; padding: 16px 0; border-top: 1px solid var(--border); }
.time-result-row div { min-width: 0; }
.time-result-row code { display: block; margin-top: 6px; overflow-wrap: anywhere; }
@media(max-width:760px) { .time-grid { grid-template-columns: 1fr; } .time-current,.time-panel { padding: 20px; } }
</style>
