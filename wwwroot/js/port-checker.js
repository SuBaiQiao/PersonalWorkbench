(function initializePortChecker({ api, createStatus, getErrorMessage, mountVueApp }) {
    mountVueApp('#portCheckerApp', {
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
    });
}(window.Workbench));
