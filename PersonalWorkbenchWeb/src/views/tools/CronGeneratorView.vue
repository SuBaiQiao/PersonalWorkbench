<script>
import { api, getErrorMessage } from '../../api/client.js';
import { copyWithFeedback } from '../../utils/clipboard.js';
import { createStatus } from '../../utils/status.js';
import CronInspector from '../../components/CronInspector.vue';

const defaultForm = () => ({
    format: 'standard',
    frequency: 'daily',
    interval: 5,
    hour: 9,
    hours: [9],
    minute: 0,
    second: 0,
    dayOfWeek: 1,
    dayOfMonth: 1
});

export default {
    components: { CronInspector },
    data() {
        return {
            form: defaultForm(),
            result: { expression: '', description: '' },
            loading: false,
            status: createStatus(),
            formatOptions: [
                { label: '标准 Cron（五字段）', value: 'standard' },
                { label: 'Quartz（六字段，含秒）', value: 'quartz' }
            ],
            hourOptions: Array.from({ length: 24 }, (_, value) => ({ value, label: `${String(value).padStart(2, '0')} 时` })),
            frequencyOptions: [
                { label: '每隔几分钟', value: 'minutely' },
                { label: '每隔几小时', value: 'hourly' },
                { label: '每天', value: 'daily' },
                { label: '每周', value: 'weekly' },
                { label: '每月', value: 'monthly' }
            ],
            weekdayOptions: [
                { label: '星期一', value: 1 },
                { label: '星期二', value: 2 },
                { label: '星期三', value: 3 },
                { label: '星期四', value: 4 },
                { label: '星期五', value: 5 },
                { label: '星期六', value: 6 },
                { label: '星期日', value: 0 }
            ]
        };
    },
    computed: {
        usesInterval() {
            return ['minutely', 'hourly'].includes(this.form.frequency);
        },
        usesTime() {
            return this.form.frequency !== 'minutely';
        },
        usesMultipleHours() {
            return ['daily', 'weekly', 'monthly'].includes(this.form.frequency);
        },
        formatHint() {
            return this.form.format === 'quartz' ? '秒 分钟 小时 日期 月份 星期' : '分钟 小时 日期 月份 星期';
        },
        intervalMaximum() {
            return this.form.frequency === 'hourly' ? 23 : 59;
        },
        intervalHint() {
            return this.form.frequency === 'hourly'
                ? '小时，范围 1-23；从指定小时开始'
                : '分钟，范围 1-59';
        }
    },
    methods: {
        /** 根据当前表单请求后端生成所选格式的 Cron 表达式。 */
        async generateCron() {
            this.loading = true;
            this.status = createStatus('正在请求后端生成…');
            try {
                const response = await api.post('/api/cron/generate', {
                    ...this.form,
                    hours: this.usesMultipleHours ? this.form.hours : undefined,
                    second: this.form.format === 'quartz' ? this.form.second : 0
                });
                this.result = response.data;
                this.status = createStatus('表达式生成完成。', 'success');
            } catch (error) {
                this.status = createStatus(await getErrorMessage(error), 'error');
            } finally {
                this.loading = false;
            }
        },

        /** 恢复默认生成参数并清空上一轮结果。 */
        resetCronForm() {
            this.form = defaultForm();
            this.result = { expression: '', description: '' };
            this.status = createStatus();
        },

        /** 将生成的 Cron 表达式复制到剪贴板。 */
        async copyCronExpression() {
            await copyWithFeedback(this.result.expression, 'Cron 表达式已复制到剪贴板。');
        }
    }
};
</script>

<template>
<div id="cronGeneratorApp">
        <section class="page-intro" aria-labelledby="cron-title">
            <p class="eyebrow">SCHEDULE UTILITY / CRON</p>
            <h1 id="cron-title">Cron 工具箱</h1>
            <p class="intro-copy">生成与校验五字段 Cron 和六字段 Quartz 表达式，支持多个执行小时，并按所选时区预览未来执行时间。</p>
        </section>

        <section class="panel converter-panel cron-panel" aria-labelledby="cron-form-title">
            <div class="panel-heading">
                <div>
                    <p class="panel-kicker">BUILD EXPRESSION</p>
                    <h2 id="cron-form-title">定时规则</h2>
                </div>
                <span class="panel-index">05</span>
            </div>

            <a-form layout="vertical" v-on:finish="generateCron">
                <div class="cron-form-grid">
                    <a-form-item label="表达式格式" :extra="formatHint">
                        <a-select v-model:value="form.format" :options="formatOptions" aria-label="表达式格式"></a-select>
                    </a-form-item>

                    <a-form-item label="执行频率">
                        <a-select v-model:value="form.frequency" :options="frequencyOptions"></a-select>
                    </a-form-item>

                    <a-form-item v-if="usesInterval" label="执行间隔" :extra="intervalHint">
                        <a-input-number v-model:value="form.interval" :min="1" :max="intervalMaximum" style="width: 100%"></a-input-number>
                    </a-form-item>

                    <a-form-item v-if="usesMultipleHours" label="执行小时（可多选）" extra="所选小时共用下方分钟和秒，例如选择 01 时、12 时">
                        <a-select v-model:value="form.hours" mode="multiple" :options="hourOptions" placeholder="请选择执行小时" aria-label="执行小时（可多选）"></a-select>
                    </a-form-item>

                    <a-form-item v-else-if="usesTime" label="起始小时" extra="24 小时制，范围 0-23">
                        <a-input-number v-model:value="form.hour" :min="0" :max="23" style="width: 100%"></a-input-number>
                    </a-form-item>

                    <a-form-item v-if="usesTime" label="分钟" extra="范围 0-59">
                        <a-input-number v-model:value="form.minute" :min="0" :max="59" style="width: 100%"></a-input-number>
                    </a-form-item>

                    <a-form-item v-if="form.format === 'quartz'" label="秒" extra="范围 0-59">
                        <a-input-number v-model:value="form.second" :min="0" :max="59" style="width: 100%"></a-input-number>
                    </a-form-item>

                    <a-form-item v-if="form.frequency === 'weekly'" label="星期" :extra="form.format === 'quartz' ? 'Quartz 中星期日使用 1' : '标准 Cron 中星期日使用 0'">
                        <a-select v-model:value="form.dayOfWeek" :options="weekdayOptions"></a-select>
                    </a-form-item>

                    <a-form-item v-if="form.frequency === 'monthly'" label="每月第几日" extra="范围 1-31">
                        <a-input-number v-model:value="form.dayOfMonth" :min="1" :max="31" style="width: 100%"></a-input-number>
                    </a-form-item>
                </div>

                <div class="form-actions converter-actions">
                    <a-button type="primary" html-type="button" :loading="loading" v-on:click="generateCron">生成表达式</a-button>
                    <a-button v-on:click="resetCronForm">恢复默认</a-button>
                </div>

                <a-alert v-if="status.message" :message="status.message" :type="status.type" show-icon></a-alert>

                <div class="cron-result" aria-live="polite">
                    <div class="cron-result-heading">
                        <span class="panel-kicker">RESULT</span>
                        <a-button :disabled="!result.expression" v-on:click="copyCronExpression">复制表达式</a-button>
                    </div>
                    <code class="cron-expression">{{ result.expression || '尚未生成' }}</code>
                    <p class="cron-description">{{ result.description || '生成后会显示规则说明。' }}</p>
                </div>

                <div class="output-footer">
                    <span>API · <code>POST /api/cron/generate</code></span>
                    <span>格式 · {{ formatHint }}</span>
                </div>
            </a-form>
        </section>

        <CronInspector :expression="result.expression" />
        <section class="cron-note" aria-label="Cron 格式说明">
            <p><strong>五字段 Cron：</strong><code>分钟 小时 日期 月份 星期</code>；<strong>六字段 Quartz：</strong><code>秒 分钟 小时 日期 月份 星期</code>，日期与星期有一项使用 <code>?</code>。例如 <code>0 9 1,12 * * ?</code> 表示每天 01:09:00 和 12:09:00 执行。</p>
        </section>
</div>
</template>
