(function initializeCronGenerator({ api, copyWithFeedback, createStatus, getErrorMessage, mountVueApp }) {
    const defaultForm = () => ({
        frequency: 'daily',
        interval: 5,
        hour: 9,
        minute: 0,
        dayOfWeek: 1,
        dayOfMonth: 1
    });

    mountVueApp('#cronGeneratorApp', {
        data() {
            return {
                form: defaultForm(),
                result: { expression: '', description: '' },
                loading: false,
                status: createStatus(),
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
            /** 根据当前表单请求后端生成五字段 Cron 表达式。 */
            async generateCron() {
                this.loading = true;
                this.status = createStatus('正在请求后端生成…');
                try {
                    const response = await api.post('/api/cron/generate', this.form);
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
    });
}(window.Workbench));
