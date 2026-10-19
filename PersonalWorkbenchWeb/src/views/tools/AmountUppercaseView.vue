<script>
import { api, getErrorMessage } from '../../api/client.js';
import { copyWithFeedback } from '../../utils/clipboard.js';
import { createStatus } from '../../utils/status.js';

export default {
    data() {
        return {
            form: { inputAmount: '1234.56' },
            outputAmount: '',
            loading: false,
            status: createStatus()
        };
    },
    methods: {
        /** 请求后端将金额转换为人民币汉字大写。 */
        async convertAmount() {
            this.loading = true;
            this.status = createStatus('正在请求后端转换…');

            try {
                const response = await api.post('/api/amount/convert', this.form);
                this.outputAmount = response.data.outputAmount;
                this.status = createStatus('转换完成。', 'success');
            } catch (error) {
                this.outputAmount = '';
                this.status = createStatus(await getErrorMessage(error), 'error');
            } finally {
                this.loading = false;
            }
        },

        /** 清空输入、输出和状态信息。 */
        clearAmount() {
            this.form.inputAmount = '';
            this.outputAmount = '';
            this.status = createStatus();
        },

        /** 将转换结果复制到系统剪贴板。 */
        async copyAmount() {
            await copyWithFeedback(this.outputAmount, '大写金额已复制到剪贴板。');
        }
    }
};
</script>

<template>
<div id="amountUppercaseApp">
        <section class="page-intro" aria-labelledby="amount-uppercase-title">
            <p class="eyebrow">FINANCE UTILITY / RMB</p>
            <h1 id="amount-uppercase-title">人民币大写转换器</h1>
            <p class="intro-copy">输入阿拉伯数字金额，转换为适用于收据、报销单和合同的人民币汉字大写格式。</p>
        </section>

        <section class="panel converter-panel amount-panel" aria-labelledby="amount-form-title">
            <div class="panel-heading">
                <div>
                    <p class="panel-kicker">CONVERT AMOUNT</p>
                    <h2 id="amount-form-title">输入与输出</h2>
                </div>
                <span class="panel-index">03</span>
            </div>

            <a-form layout="vertical" v-on:finish="convertAmount">
                <a-form-item label="阿拉伯数字金额" extra="支持整数和最多两位小数，例如 1234.56">
                    <a-input v-model:value="form.inputAmount" placeholder="例如：1234.56" autocomplete="off"></a-input>
                </a-form-item>

                <div class="form-actions converter-actions">
                    <a-button type="primary" html-type="button" :loading="loading" v-on:click="convertAmount">转换金额</a-button>
                    <a-button v-on:click="clearAmount">清空</a-button>
                </div>

                <a-alert v-if="status.message" :message="status.message" :type="status.type" show-icon></a-alert>

                <div class="amount-result" aria-live="polite">
                    <div class="amount-result-heading">
                        <div>
                            <span class="panel-kicker">RESULT</span>
                            <h3>人民币汉字大写</h3>
                        </div>
                        <a-button :disabled="!outputAmount" v-on:click="copyAmount">复制结果</a-button>
                    </div>
                    <p class="amount-output">{{ outputAmount || '转换结果将显示在这里' }}</p>
                </div>

                <div class="output-footer">
                    <span>API · <code>POST /api/amount/convert</code></span>
                    <span>小数 · 最多两位</span>
                </div>
            </a-form>
        </section>
</div>
</template>
