<script>
import { api, getErrorMessage } from '../../api/client.js';
import { copyWithFeedback } from '../../utils/clipboard.js';
import { createStatus } from '../../utils/status.js';

export default {
    data() {
        return {
            areas: [],
            rows: [],
            loading: false,
            status: createStatus(),
            form: {
                provinceCode: undefined,
                cityCode: undefined,
                countyCode: undefined,
                birthDate: '',
                gender: 'random',
                count: 5
            },
            genderOptions: [
                { label: '随机', value: 'random' },
                { label: '男', value: 'male' },
                { label: '女', value: 'female' }
            ]
        };
    },
    computed: {
        provinceOptions() {
            return this.areas.map(area => ({ label: area.name, value: area.code }));
        },
        selectedProvince() {
            return this.areas.find(area => area.code === this.form.provinceCode);
        },
        cityOptions() {
            return (this.selectedProvince?.cities ?? []).map(city => ({ label: city.name, value: city.code }));
        },
        selectedCity() {
            return this.selectedProvince?.cities.find(city => city.code === this.form.cityCode);
        },
        countyOptions() {
            return (this.selectedCity?.counties ?? []).map(county => ({ label: county.name, value: county.code }));
        }
    },
    methods: {
        /** 切换省份时清空不再有效的城市和区县。 */
        handleProvinceChange() {
            this.form.cityCode = undefined;
            this.form.countyCode = undefined;
        },

        /** 切换城市时清空不再有效的区县。 */
        handleCityChange() {
            this.form.countyCode = undefined;
        },

        /** 根据当前表单参数请求后端生成测试身份数据。 */
        async generateIdentities() {
            this.loading = true;
            this.status = createStatus('正在请求后端生成…');

            try {
                const payload = {
                    ...this.form,
                    provinceCode: this.form.provinceCode || null,
                    cityCode: this.form.cityCode || null,
                    countyCode: this.form.countyCode || null,
                    birthDate: this.form.birthDate || null
                };
                const response = await api.post('/api/identity/generate', payload);
                this.rows = response.data;
                this.status = createStatus(`已生成 ${this.rows.length} 条测试数据。`, 'success');
            } catch (error) {
                this.rows = [];
                this.status = createStatus(await getErrorMessage(error, '生成失败，请检查输入后重试。'), 'error');
            } finally {
                this.loading = false;
            }
        },

        /** 将表格结果按制表符格式复制到剪贴板。 */
        async copyIdentities() {
            const text = this.rows
                .map(row => [row.name, row.idNumber, row.gender, row.birthDate, row.areaCode].join('\t'))
                .join('\n');

            await copyWithFeedback(text, '结果已复制到剪贴板。');
        },

        /** 加载行政区划数据并生成首批默认结果。 */
        async initialize() {
            this.status = createStatus('正在加载地区数据…');
            try {
                const response = await api.get('/api/identity/areas');
                this.areas = response.data;
                await this.generateIdentities();
            } catch (error) {
                this.status = createStatus(await getErrorMessage(error, '地区数据加载失败。'), 'error');
            }
        }
    },
    mounted() {
        this.initialize();
    }
};
</script>

<template>
<div id="identityGeneratorApp">
        <section class="page-intro">
            <p class="eyebrow">DATA UTILITY / TEST FIXTURES</p>
            <h1>身份信息生成器</h1>
            <p class="intro-copy">生成仅用于开发联调的虚构身份信息。数据由后端生成，身份证校验位符合 18 位号码规则。</p>
        </section>

        <div class="tool-grid identity-grid">
            <section class="panel form-panel" aria-labelledby="identity-form-title">
                <div class="panel-heading">
                    <div>
                        <p class="panel-kicker">CONFIGURE</p>
                        <h2 id="identity-form-title">生成参数</h2>
                    </div>
                    <span class="panel-index">01</span>
                </div>

                <a-form layout="vertical" v-on:finish="generateIdentities">
                    <fieldset>
                        <legend>户籍地区</legend>
                        <div class="field-grid field-grid-3">
                            <a-form-item label="省 / 直辖市">
                                <a-select v-model:value="form.provinceCode" :options="provinceOptions" placeholder="随机省份" allow-clear v-on:change="handleProvinceChange"></a-select>
                            </a-form-item>
                            <a-form-item label="城市">
                                <a-select v-model:value="form.cityCode" :options="cityOptions" placeholder="随机城市" allow-clear :disabled="!form.provinceCode" v-on:change="handleCityChange"></a-select>
                            </a-form-item>
                            <a-form-item label="区 / 县">
                                <a-select v-model:value="form.countyCode" :options="countyOptions" placeholder="随机区县" allow-clear :disabled="!form.cityCode"></a-select>
                            </a-form-item>
                        </div>
                    </fieldset>

                    <div class="field-grid field-grid-3">
                        <a-form-item label="出生日期" extra="留空则随机生成 1960–2005 年日期">
                            <a-input v-model:value="form.birthDate" type="date"></a-input>
                        </a-form-item>
                        <a-form-item label="性别">
                            <a-select v-model:value="form.gender" :options="genderOptions"></a-select>
                        </a-form-item>
                        <a-form-item label="生成数量" extra="最多生成 100 条">
                            <a-input-number v-model:value="form.count" :min="1" :max="100" style="width: 100%"></a-input-number>
                        </a-form-item>
                    </div>

                    <div class="form-actions">
                        <a-button type="primary" html-type="button" :loading="loading" v-on:click="generateIdentities">生成测试数据</a-button>
                    </div>
                    <a-alert v-if="status.message" :message="status.message" :type="status.type" show-icon></a-alert>
                </a-form>
            </section>

            <aside class="panel note-panel" aria-labelledby="identity-note-title">
                <p class="panel-kicker">GUARDRAILS</p>
                <h2 id="identity-note-title">使用边界</h2>
                <ul class="note-list">
                    <li>姓名、号码和地区均为随机组合，不对应真实个人。</li>
                    <li>数据只适合接口联调、表单校验和演示。</li>
                    <li>不要写入生产库、用于实名认证或任何真实业务。</li>
                </ul>
                <div class="note-rule"></div>
                <p class="note-meta">API · <code>POST /api/identity/generate</code></p>
            </aside>
        </div>

        <section class="panel results-panel" aria-labelledby="identity-results-title">
            <div class="panel-heading results-heading">
                <div>
                    <p class="panel-kicker">OUTPUT</p>
                    <h2 id="identity-results-title">生成结果 <span class="count-badge">{{ rows.length }}</span></h2>
                </div>
                <a-button :disabled="rows.length === 0" v-on:click="copyIdentities">复制结果</a-button>
            </div>

            <div class="table-wrap">
                <table>
                    <thead>
                        <tr>
                            <th scope="col">姓名</th>
                            <th scope="col">身份证号码</th>
                            <th scope="col">性别</th>
                            <th scope="col">出生日期</th>
                            <th scope="col">区划代码</th>
                        </tr>
                    </thead>
                    <tbody>
                        <tr v-if="rows.length === 0" class="empty-row"><td colspan="5">暂无生成结果</td></tr>
                        <tr v-for="row in rows" :key="row.idNumber">
                            <td>{{ row.name }}</td>
                            <td class="mono">{{ row.idNumber }}</td>
                            <td>{{ row.gender }}</td>
                            <td>{{ row.birthDate }}</td>
                            <td>{{ row.areaCode }}</td>
                        </tr>
                    </tbody>
                </table>
            </div>
        </section>
</div>
</template>
