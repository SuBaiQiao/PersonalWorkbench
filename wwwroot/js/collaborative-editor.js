(function initializeCollaborativeEditor({ api, copyWithFeedback, createStatus, getErrorMessage, mountVueApp }) {
    const socketProtocol = window.location.protocol === 'https:' ? 'wss:' : 'ws:';
    const socketUrl = `${socketProtocol}//${window.location.host}/ws/collaborative`;

    mountVueApp('#collaborativeEditorApp', {
        data() {
            return {
                socket: null,
                reconnectTimer: null,
                text: '',
                history: [],
                files: [],
                status: createStatus(),
                connectionState: 'connecting',
                historyModalVisible: false,
                selectedHistory: '',
                isUploading: false,
                uploadProgress: 0,
                textUpdateTimer: null,
                applyingRemoteUpdate: false
            };
        },
        computed: {
            connectionClass() {
                return `is-${this.connectionState}`;
            },
            connectionText() {
                return ({ connecting: '连接中', connected: '已连接', disconnected: '已断开' })[this.connectionState] || '连接异常';
            },
            reversedHistory() {
                return this.history.slice().reverse().map((value, index) => ({ value, index }));
            }
        },
        mounted() {
            this.connect();
        },
        beforeUnmount() {
            if (this.reconnectTimer) {
                clearTimeout(this.reconnectTimer);
            }
            if (this.textUpdateTimer) {
                clearTimeout(this.textUpdateTimer);
            }
            this.socket?.close();
        },
        methods: {
            connect() {
                this.connectionState = 'connecting';
                const socket = new WebSocket(socketUrl);
                this.socket = socket;
                socket.onopen = () => {
                    this.connectionState = 'connected';
                    this.status = createStatus('已连接到协同编辑服务。', 'success');
                    socket.send(JSON.stringify({ type: 'get_current_state' }));
                };
                socket.onmessage = event => this.handleSocketMessage(event.data);
                socket.onerror = () => {
                    this.connectionState = 'disconnected';
                    this.status = createStatus('协同连接异常，正在尝试重连。', 'error');
                };
                socket.onclose = () => {
                    this.connectionState = 'disconnected';
                    if (this.socket === socket) {
                        this.reconnectTimer = setTimeout(() => this.connect(), 2500);
                    }
                };
            },
            handleSocketMessage(rawMessage) {
                let message;
                try {
                    message = JSON.parse(rawMessage);
                } catch {
                    return;
                }

                if (message.type === 'state') {
                    this.applyState(message.state);
                } else if (message.type === 'text_update') {
                    this.applyRemoteText(message.text, message.history);
                } else if (message.type === 'file_list_update') {
                    this.files = message.files || [];
                }
            },
            applyState(state) {
                this.applyingRemoteUpdate = true;
                this.text = state?.text ?? state?.Text ?? '';
                this.history = state?.history ?? state?.History ?? [];
                this.files = state?.files ?? state?.Files ?? [];
                this.$nextTick(() => { this.applyingRemoteUpdate = false; });
            },
            applyRemoteText(nextText, nextHistory) {
                this.applyingRemoteUpdate = true;
                this.text = nextText || '';
                this.history = nextHistory || [...this.history, this.text];
                this.$nextTick(() => { this.applyingRemoteUpdate = false; });
            },
            handleTextInput() {
                if (this.applyingRemoteUpdate) {
                    return;
                }
                if (this.textUpdateTimer) {
                    clearTimeout(this.textUpdateTimer);
                }
                this.textUpdateTimer = setTimeout(() => {
                    if (this.socket?.readyState === WebSocket.OPEN) {
                        this.socket.send(JSON.stringify({ type: 'text_update', text: this.text }));
                    }
                }, 180);
            },
            async handleFileSelection(event) {
                const selectedFiles = [...(event.target.files || [])];
                event.target.value = '';
                await this.uploadFiles(selectedFiles);
            },
            async handlePaste(event) {
                const image = [...(event.clipboardData?.items || [])].find(item => item.type.startsWith('image/'));
                if (!image) {
                    return;
                }
                const file = image.getAsFile();
                if (!file) {
                    return;
                }
                const timestamp = new Date().toISOString().replace(/[:.]/g, '-');
                const pastedFile = new File([file], `pasted_image_${timestamp}.png`, { type: file.type || 'image/png' });
                await this.uploadFiles([pastedFile]);
            },
            async uploadFiles(files) {
                if (files.length === 0) {
                    return;
                }
                this.isUploading = true;
                this.uploadProgress = 0;
                try {
                    for (let index = 0; index < files.length; index += 1) {
                        const formData = new FormData();
                        formData.append('file', files[index], files[index].name);
                        await api.post('/api/collaborative/files', formData, {
                            headers: { 'Content-Type': 'multipart/form-data' },
                            onUploadProgress: event => {
                                if (event.total) {
                                    const current = Math.round((event.loaded / event.total) * 100);
                                    this.uploadProgress = Math.round(((index + current / 100) / files.length) * 100);
                                }
                            }
                        });
                    }
                    this.status = createStatus('文件上传完成。', 'success');
                } catch (error) {
                    this.status = createStatus(await getErrorMessage(error, '文件上传失败。'), 'error');
                } finally {
                    this.isUploading = false;
                    this.uploadProgress = 0;
                }
            },
            downloadFile(fileName) {
                window.location.href = `/api/collaborative/files/${encodeURIComponent(fileName)}`;
            },
            async deleteFile(fileName) {
                if (!window.confirm(`确定删除文件“${fileName}”吗？`)) {
                    return;
                }
                try {
                    await api.delete(`/api/collaborative/files/${encodeURIComponent(fileName)}`);
                    this.status = createStatus('文件删除成功。', 'success');
                } catch (error) {
                    this.status = createStatus(await getErrorMessage(error, '文件删除失败。'), 'error');
                }
            },
            historyPreview(value) {
                const oneLine = (value || '').replace(/\s+/g, ' ').trim();
                return oneLine.length > 54 ? `${oneLine.slice(0, 54)}…` : (oneLine || '空文本');
            },
            showHistory(value) {
                this.selectedHistory = value || '';
                this.historyModalVisible = true;
            },
            async useHistory() {
                this.text = this.selectedHistory;
                this.historyModalVisible = false;
                this.handleTextInput();
            },
            async copyText() {
                await copyWithFeedback(this.text, '全文已复制到剪贴板。');
            },
            async copyHistory() {
                await copyWithFeedback(this.selectedHistory, '历史版本已复制到剪贴板。');
            }
        }
    });
}(window.Workbench));
