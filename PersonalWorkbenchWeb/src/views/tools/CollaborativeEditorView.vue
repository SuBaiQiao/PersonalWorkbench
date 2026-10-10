<script>
import { api, getErrorMessage } from '../../api/client.js';
import { copyWithFeedback } from '../../utils/clipboard.js';
import { createStatus } from '../../utils/status.js';

const socketProtocol = window.location.protocol === 'https:' ? 'wss:' : 'ws:';
const socketUrl = `${socketProtocol}//${window.location.host}/ws/collaborative`;

export default {
    data() {
        return {
            disposed: false,
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
        this.disposed = true;
        if (this.reconnectTimer) {
            clearTimeout(this.reconnectTimer);
        }
        if (this.textUpdateTimer) {
            clearTimeout(this.textUpdateTimer);
        }
        const socket = this.socket;
        this.socket = null;
        socket?.close();
    },
    methods: {
        connect() {
            if (this.disposed) return;
            clearTimeout(this.reconnectTimer);
            this.connectionState = 'connecting';
            const socket = new WebSocket(socketUrl);
            this.socket = socket;
            socket.onopen = () => {
                if (this.disposed || this.socket !== socket) return;
                this.connectionState = 'connected';
                this.status = createStatus('已连接到协同编辑服务。', 'success');
                socket.send(JSON.stringify({ type: 'get_current_state' }));
            };
            socket.onmessage = event => {
                if (!this.disposed && this.socket === socket) this.handleSocketMessage(event.data);
            };
            socket.onerror = () => {
                if (this.disposed || this.socket !== socket) return;
                this.connectionState = 'disconnected';
                this.status = createStatus('协同连接异常，正在尝试重连。', 'error');
            };
            socket.onclose = () => {
                if (this.disposed || this.socket !== socket) return;
                this.connectionState = 'disconnected';
                this.reconnectTimer = setTimeout(() => this.connect(), 2500);
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
};
</script>

<template>
<div id="collaborativeEditorApp" class="collaborative-editor-page">
        <section class="page-intro collaborative-intro" aria-labelledby="collaborative-editor-title">
            <div>
                <p class="eyebrow">COLLABORATION UTILITY / REALTIME TEXT</p>
                <h1 id="collaborative-editor-title">协同文本编辑器</h1>
                <p class="intro-copy">在多个浏览器之间实时同步文本，并把文件作为临时中转内容共享给协作者。</p>
            </div>
            <div class="collaboration-connection" :class="connectionClass" role="status">
                <span class="connection-dot" aria-hidden="true"></span>
                <span>{{ connectionText }}</span>
            </div>
        </section>

        <div class="collaborative-layout">
            <section class="panel collaborative-editor-panel" aria-labelledby="editor-panel-title">
                <div class="panel-heading collaborative-panel-heading">
                    <div>
                        <p class="panel-kicker">SHARED TEXT</p>
                        <h2 id="editor-panel-title">实时文本</h2>
                    </div>
                    <span class="panel-index">08A</span>
                </div>

                <textarea v-model="text" class="collaborative-textarea" rows="18" placeholder="在这里输入文本，其他打开此页面的浏览器会即时看到变化。" v-on:input="handleTextInput" v-on:paste="handlePaste"></textarea>
                <div class="collaborative-editor-footer">
                    <span>{{ text.length }} 个字符 · 自动同步</span>
                    <a-button size="small" v-on:click="copyText">复制全文</a-button>
                </div>
                <a-alert v-if="status.message" :message="status.message" :type="status.type" show-icon></a-alert>
            </section>

            <aside class="collaborative-sidebar">
                <section class="panel collaborative-files-panel" aria-labelledby="files-panel-title">
                    <div class="panel-heading collaborative-panel-heading">
                        <div>
                            <p class="panel-kicker">FILE RELAY</p>
                            <h2 id="files-panel-title">共享文件 <span class="count-badge">{{ files.length }}</span></h2>
                        </div>
                        <span class="panel-index">08B</span>
                    </div>

                    <label class="collaborative-upload-zone" :class="{ 'is-uploading': isUploading }">
                        <span class="upload-mark" aria-hidden="true">FILE</span>
                        <span class="upload-title">选择文件上传</span>
                        <span class="upload-hint">支持批量选择，单个文件不超过 100 MB</span>
                        <span class="button button-secondary collaborative-upload-button">{{ isUploading ? `上传中 ${uploadProgress}%` : '选择文件' }}</span>
                        <input ref="fileInput" type="file" multiple v-on:change="handleFileSelection">
                    </label>

                    <div v-if="files.length === 0" class="collaborative-file-empty">还没有共享文件</div>
                    <ul v-else class="collaborative-file-list">
                        <li v-for="file in files" :key="file">
                            <span class="collaborative-file-name" :title="file">{{ file }}</span>
                            <span class="collaborative-file-actions">
                                <a-button type="link" size="small" v-on:click="downloadFile(file)">下载</a-button>
                                <a-button type="link" danger size="small" v-on:click="deleteFile(file)">删除</a-button>
                            </span>
                        </li>
                    </ul>
                    <a-progress v-if="isUploading" :percent="uploadProgress" size="small" status="active"></a-progress>
                </section>

                <section class="panel collaborative-history-panel" aria-labelledby="history-panel-title">
                    <div class="panel-heading collaborative-panel-heading">
                        <div>
                            <p class="panel-kicker">TEXT HISTORY</p>
                            <h2 id="history-panel-title">历史版本 <span class="count-badge">{{ history.length }}</span></h2>
                        </div>
                        <span class="panel-index">08C</span>
                    </div>
                    <div v-if="history.length === 0" class="collaborative-file-empty">暂无历史版本</div>
                    <ol v-else class="collaborative-history-list">
                        <li v-for="(item, index) in reversedHistory" :key="item.index" v-on:click="showHistory(item.value)">
                            <button type="button">
                                <span class="history-version">版本 {{ history.length - index }}</span>
                                <span>{{ historyPreview(item.value) }}</span>
                            </button>
                        </li>
                    </ol>
                </section>
            </aside>
        </div>

        <div class="collaborative-note">
            <strong>说明：</strong>文本和文件保存在当前服务实例中，适合局域网协作与临时中转。请勿上传敏感或生产数据。
        </div>

        <a-modal v-model:open="historyModalVisible" title="历史记录详情" :footer="null">
            <pre class="history-modal-text">{{ selectedHistory }}</pre>
            <div class="history-modal-actions">
                <a-button type="primary" v-on:click="useHistory">使用此版本</a-button>
                <a-button v-on:click="copyHistory">复制内容</a-button>
            </div>
        </a-modal>
</div>
</template>
