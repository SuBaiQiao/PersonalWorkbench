<script>
import { api, getErrorMessage } from '../../api/client.js';
import { createStatus } from '../../utils/status.js';

export default {
    data() {
        return {
            port: 5190,
            loading: false,
            status: createStatus(),
            result: null,
            terminatingKey: ''
        };
    },
    methods: {
        async checkPort() {
            this.loading = true;
            this.status = createStatus('正在检查本机端口…');
            try {
                const response = await api.post('/api/port/check', { port: this.port });
                this.result = response.data;
                this.status = createStatus(
                    response.data.isOccupied ? '检测到端口占用。' : '端口未被占用。',
                    response.data.isOccupied ? 'warning' : 'success'
                );
            } catch (error) {
                this.status = createStatus(await getErrorMessage(error), 'error');
            } finally {
                this.loading = false;
            }
        },
        occupancyKey(occupancy) {
            return `${occupancy.protocol}-${occupancy.localAddress}-${occupancy.processId}-${occupancy.state}`;
        },
        isTerminating(occupancy) {
            return this.terminatingKey === this.occupancyKey(occupancy);
        },
        async terminateProcess(occupancy) {
            if (occupancy.processId <= 0 || !globalThis.confirm(`确定要终止 ${occupancy.processName}（PID ${occupancy.processId}）吗？`)) {
                return;
            }

            this.terminatingKey = this.occupancyKey(occupancy);
            this.status = createStatus('正在终止进程…');
            try {
                await api.post('/api/port/terminate', {
                    port: this.result.port,
                    processId: occupancy.processId
                });
                this.status = createStatus('进程已终止，正在刷新端口状态。', 'success');
                await this.checkPort();
            } catch (error) {
                this.status = createStatus(await getErrorMessage(error), 'error');
            } finally {
                this.terminatingKey = '';
            }
        },
        reset() {
            this.result = null;
            this.status = createStatus();
            this.terminatingKey = '';
        }
    }
};
</script>

<template>
<div id="portCheckerApp">
        <section class="page-intro" aria-labelledby="port-checker-title">
            <p class="eyebrow">NETWORK UTILITY / PORT CHECK</p>
            <h1 id="port-checker-title">端口占用检查</h1>
            <p class="intro-copy">检查本机 TCP 和 UDP 端口是否被占用，显示对应应用进程，并可终止当前占用端口的进程。</p>
        </section>

        <section class="panel converter-panel port-check-panel" aria-labelledby="port-check-form-title">
            <div class="panel-heading">
                <div>
                    <p class="panel-kicker">CHECK LOCAL PORT</p>
                    <h2 id="port-check-form-title">输入端口</h2>
                </div>
                <span class="panel-index">07</span>
            </div>

            <a-form layout="vertical" v-on:finish="checkPort">
                <a-form-item label="端口号" extra="请输入 1 到 65535 之间的端口号">
                    <a-input-number v-model:value="port" :min="1" :max="65535" style="width: 100%" v-on:press-enter="checkPort"></a-input-number>
                </a-form-item>

                <div class="form-actions converter-actions">
                    <a-button type="primary" html-type="button" :loading="loading" v-on:click="checkPort">检查端口</a-button>
                    <a-button v-on:click="reset">清空结果</a-button>
                </div>

                <a-alert v-if="status.message" :message="status.message" :type="status.type" show-icon></a-alert>
            </a-form>
        </section>

        <section v-if="result" class="panel results-panel port-results-panel" aria-labelledby="port-result-title">
            <div class="panel-heading results-heading">
                <div>
                    <p class="panel-kicker">RESULT</p>
                    <h2 id="port-result-title">端口 {{ result.port }} 的检查结果</h2>
                </div>
                <span class="port-state-badge" :class="result.isOccupied ? 'is-occupied' : 'is-free'">
                    {{ result.isOccupied ? '已占用' : '未占用' }}
                </span>
            </div>

            <div v-if="!result.isOccupied" class="port-free-state" role="status">
                <strong>端口当前未被占用</strong>
                <span>没有检测到 TCP 或 UDP 监听记录。</span>
            </div>
            <div v-else class="table-wrap">
                <table>
                    <thead>
                        <tr>
                            <th scope="col">协议</th>
                            <th scope="col">本地地址</th>
                            <th scope="col">状态</th>
                            <th scope="col">PID</th>
                            <th scope="col">占用应用</th>
                            <th scope="col">操作</th>
                        </tr>
                    </thead>
                    <tbody>
                        <tr v-for="occupancy in result.occupancies" :key="`${occupancy.protocol}-${occupancy.localAddress}-${occupancy.processId}-${occupancy.state}`">
                            <td>{{ occupancy.protocol }}</td>
                            <td class="mono">{{ occupancy.localAddress }}</td>
                            <td>{{ occupancy.state }}</td>
                            <td class="mono">{{ occupancy.processId || '-' }}</td>
                            <td><strong>{{ occupancy.processName }}</strong></td>
                            <td>
                                <a-button danger size="small" :loading="isTerminating(occupancy)" :disabled="occupancy.processId <= 0" v-on:click="terminateProcess(occupancy)">终止进程</a-button>
                            </td>
                        </tr>
                    </tbody>
                </table>
            </div>

            <div class="output-footer">
                <span>API · <code>POST /api/port/check</code></span>
                <span>数据来源 · netstat -ano</span>
            </div>
        </section>
</div>
</template>
