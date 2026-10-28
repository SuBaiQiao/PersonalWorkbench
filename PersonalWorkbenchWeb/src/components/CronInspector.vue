<script setup>
import { ref, watch } from 'vue';
import { inspectCron } from '../utils/cron.js';
import { formatDateInput, localZone, parseDateTime, timeZones } from '../utils/time.js';
import { copyWithFeedback } from '../utils/clipboard.js';
const props = defineProps({ expression: { type: String, default: '' } });
const text = ref('*/5 * * * *');
const zone = ref(localZone);
const start = ref(formatDateInput(Date.now()));
const count = ref(10);
const result = ref(null);
const error = ref('');
const examples = [{ label: '每 5 分钟', value: '*/5 * * * *' }, { label: '工作日 09:00', value: '0 9 * * 1-5' }, { label: '每月 1 日', value: '0 0 1 * *' }, { label: '每天 01:09:00（含秒）', value: '0 9 1 * * ?' }, { label: '每天 01:09 和 12:09', value: '0 9 1,12 * * ?' }];
watch(() => props.expression, value => { if (value) text.value = value; });
watch([text, zone, start, count], () => { result.value = null; error.value = ''; });
function analyze() {
    result.value = null; error.value = '';
    try { result.value = inspectCron(text.value, { zone: zone.value, start: parseDateTime(start.value, zone.value), count: count.value }); }
    catch (exception) { error.value = exception.message; }
}
</script>

<template>
    <section class="panel converter-panel cron-inspector" aria-labelledby="cron-inspect-title">
        <div class="panel-heading"><div><p class="panel-kicker">UNDERSTAND &amp; PREVIEW</p><h2 id="cron-inspect-title">解析与执行时间预览</h2></div></div>
        <a-form layout="vertical">
            <a-form-item label="Cron 表达式" extra="五字段：分钟 小时 日期 月份 星期；六字段 Quartz：秒 分钟 小时 日期 月份 星期。"><a-input v-model:value="text" aria-label="待解析 Cron 表达式" placeholder="例如 */5 * * * * 或 0 9 1,12 * * ?" @press-enter="analyze" /></a-form-item>
            <div class="cron-examples"><a-button v-for="example in examples" :key="example.value" size="small" @click="text = example.value">{{ example.label }}</a-button><a-button v-if="props.expression" size="small" @click="text = props.expression">使用上方生成结果</a-button></div>
            <div class="cron-form-grid"><a-form-item label="执行时区"><a-select v-model:value="zone" :options="timeZones" aria-label="Cron 执行时区" /></a-form-item><a-form-item label="预览次数"><a-select v-model:value="count" :options="[5,10,20].map(value=>({value,label:`未来 ${value} 次`}))" aria-label="预览次数" /></a-form-item></div>
            <a-form-item label="从此时间之后开始" extra="按执行时区解释，不含与起始时间相同的执行点；可附带明确时区偏移量。"><a-input v-model:value="start" aria-label="Cron 起始时间" /><a-button class="cron-now" size="small" @click="start = formatDateInput(Date.now(), zone)">使用当前时间</a-button></a-form-item>
            <div class="form-actions converter-actions"><a-button type="primary" @click="analyze">校验并预览</a-button><a-button @click="copyWithFeedback(text, '表达式已复制。')">复制表达式</a-button></div>
            <a-alert v-if="error" :message="error" type="error" show-icon role="alert" />
        </a-form>
        <div v-if="result" class="cron-preview" aria-live="polite"><a-alert :message="result.description" type="success" show-icon /><a-alert v-if="result.dayOr" class="cron-preview" message="日期和星期同时指定时，标准 Cron 按「或」匹配：满足日期或星期任一条件即执行。" type="info" show-icon /><div class="cron-fields" :class="{ 'cron-fields-six': result.fields.length === 6 }"><span v-for="(field,index) in result.fields" :key="index"><small>{{ result.fieldLabels[index] }}</small><code>{{ field }}</code></span></div><h3>接下来 {{ result.occurrences.length }} 次执行</h3><ol class="cron-occurrences"><li v-for="item in result.occurrences" :key="item.milliseconds"><code>{{ item.date }}</code><span>{{ item.seconds }} 秒</span></li></ol><p class="cron-muted">时区：{{ zone }}。夏令时区域的 UTC 偏移量可能随日期变化。</p></div>
        <p class="cron-muted cron-preview">支持 *、逗号列表、范围、步长及英文月份/星期。六字段 Quartz 的日期和星期有且仅有一项使用 ?，星期为 1（星期日）至 7（星期六）；五字段保持原有规则。暂不支持 L、W、# 和年份字段。</p>
    </section>
</template>

<style scoped>
.cron-inspector { margin-top: 24px; }
.cron-examples { display: flex; flex-wrap: wrap; gap: 8px; }
.cron-now { margin-top: 8px; }
.cron-preview { margin-top: 22px; }
.cron-fields { display: grid; grid-template-columns: repeat(5,minmax(0,1fr)); gap: 8px; margin: 22px 0; }
.cron-fields-six { grid-template-columns: repeat(6,minmax(0,1fr)); }
.cron-fields > span { min-width: 0; padding: 12px 8px; border: 1px solid var(--border); border-radius: 14px; background: var(--well); text-align: center; }
.cron-fields small,.cron-fields code { display: block; overflow-wrap: anywhere; }
.cron-fields small,.cron-muted { color: var(--text-muted); font-size: 12px; }
.cron-fields code { margin-top: 8px; }
.cron-occurrences { margin: 0; padding-left: 28px; }
.cron-occurrences li { padding: 12px 4px; border-bottom: 1px solid var(--border); }
.cron-occurrences span { display: inline-block; margin-left: 16px; color: var(--text-muted); font-size: 12px; }
.cron-muted { margin-bottom: 0; line-height: 1.8; }
@media(max-width:600px) { .cron-fields { gap: 4px; } .cron-fields > span { padding: 10px 3px; } .cron-occurrences span { display: block; margin: 4px 0 0; } }
</style>
