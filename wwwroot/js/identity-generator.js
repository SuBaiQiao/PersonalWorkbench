(function initializeIdentityGenerator({ api, copyWithFeedback, createStatus, getErrorMessage, mountVueApp }) {
    mountVueApp('#identityGeneratorApp', {
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
    });
}(window.Workbench));
