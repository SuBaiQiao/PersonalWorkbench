(function initializeSqlConverter({ api, copyWithFeedback, createStatus, getErrorMessage, mountVueApp }) {
    mountVueApp('#sqlConverterApp', {
        data() {
            return {
                form: { inputSql: '' },
                outputSql: '',
                loading: false,
                status: createStatus()
            };
        },
        methods: {
            /** 请求后端完成 SQL 命名转换。 */
            async convertSql() {
                this.loading = true;
                this.status = createStatus('正在请求后端转换…');

                try {
                    const response = await api.post('/api/sql/convert', this.form);
                    this.outputSql = response.data.outputSql;
                    this.status = this.outputSql
                        ? createStatus('转换完成。', 'success')
                        : createStatus('输入为空，未生成结果。', 'warning');
                } catch (error) {
                    this.status = createStatus(await getErrorMessage(error), 'error');
                } finally {
                    this.loading = false;
                }
            },

            /** 清空输入、输出和状态信息。 */
            clearSql() {
                this.form.inputSql = '';
                this.outputSql = '';
                this.status = createStatus();
            },

            /** 将转换结果复制到系统剪贴板。 */
            async copySql() {
                await copyWithFeedback(this.outputSql, '结果已复制到剪贴板。');
            }
        }
    });
}(window.Workbench));
