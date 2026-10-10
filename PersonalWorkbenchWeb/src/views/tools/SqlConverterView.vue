<script>
import { api, getErrorMessage } from '../../api/client.js';
import { copyWithFeedback } from '../../utils/clipboard.js';
import { createStatus } from '../../utils/status.js';

export default {
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
};
</script>

<template>
<div id="sqlConverterApp">
        <section class="page-intro">
            <p class="eyebrow">CODE UTILITY / SQL NAMING</p>
            <h1>SQL 驼峰转换器</h1>
            <p class="intro-copy">把字段、表名和 SQL 片段中的驼峰命名转换为大写下划线命名，转换逻辑由后端接口完成。</p>
        </section>

        <section class="panel converter-panel" aria-labelledby="sql-form-title">
            <div class="panel-heading">
                <div>
                    <p class="panel-kicker">TRANSFORM</p>
                    <h2 id="sql-form-title">输入与输出</h2>
                </div>
                <span class="panel-index">02</span>
            </div>

            <a-form layout="vertical" v-on:finish="convertSql">
                <a-form-item label="输入 SQL / 命名片段">
                    <a-textarea v-model:value="form.inputSql" :rows="9" placeholder="例如：select userId, userName from sysUser"></a-textarea>
                </a-form-item>

                <div class="form-actions converter-actions">
                    <a-button type="primary" html-type="button" :loading="loading" v-on:click="convertSql">转换 SQL</a-button>
                    <a-button v-on:click="clearSql">清空</a-button>
                </div>

                <a-alert v-if="status.message" :message="status.message" :type="status.type" show-icon></a-alert>

                <a-form-item label="输出结果（大写下划线）">
                    <a-textarea v-model:value="outputSql" :rows="9" readonly placeholder="转换结果将显示在这里"></a-textarea>
                </a-form-item>

                <div class="output-footer">
                    <span>API · <code>POST /api/sql/convert</code></span>
                    <a-button :disabled="!outputSql" v-on:click="copySql">复制结果</a-button>
                </div>
            </a-form>
        </section>
</div>
</template>
