<script>
import { markRaw } from 'vue';
import WorkbenchIcon from '../../components/WorkbenchIcon.vue';

export default {
    components: { WorkbenchIcon },
    data() {
        return {
            serverHost: window.location.host,
            sockets: markRaw(new Map()),
            objectUrls: markRaw(new Map()),
            disposed: false,
            loadGeneration: 0,
            devices: [],
            frames: {},
            states: {},
            reconnecting: {},
            loading: false,
            errorMessage: ''
        };
    },
    mounted() {
        this.loadDevices();
    },
    beforeUnmount() {
        this.disposed = true;
        this.loadGeneration += 1;
        this.closeSockets();
    },
    methods: {
        apiOrigin() {
            const host = this.serverHost.trim();
            return `${window.location.protocol}//${host}`;
        },
        websocketOrigin() {
            const host = this.serverHost.trim();
            const protocol = window.location.protocol === 'https:' ? 'wss:' : 'ws:';
            return `${protocol}//${host}`;
        },
        closeSockets() {
            this.sockets.forEach(socket => {
                socket.onmessage = null;
                socket.onerror = null;
                socket.onclose = null;
                socket.close();
            });
            this.sockets.clear();
            this.objectUrls.forEach(url => window.URL.revokeObjectURL(url));
            this.objectUrls.clear();
            this.frames = {};
        },
        async loadDevices() {
            if (this.disposed) return;
            const generation = ++this.loadGeneration;
            this.loading = true;
            this.errorMessage = '';
            this.closeSockets();
            try {
                const response = await fetch(`${this.apiOrigin()}/api/android/devices`, { headers: { 'Accept': 'application/json' } });
                if (!response.ok) {
                    throw new Error(`服务返回 ${response.status}`);
                }
                const payload = await response.json();
                if (this.disposed || generation !== this.loadGeneration) return;
                this.devices = payload.devices || [];
                this.states = Object.fromEntries(this.devices.map(device => [device, 'connecting']));
                this.devices.forEach(device => this.initWebSocket(device));
            } catch (error) {
                if (this.disposed || generation !== this.loadGeneration) return;
                this.devices = [];
                this.errorMessage = `无法连接到后台服务器 ${this.apiOrigin()}`;
                console.error(error);
            } finally {
                if (!this.disposed && generation === this.loadGeneration) this.loading = false;
            }
        },
        initWebSocket(device) {
            if (this.disposed) return;
            const encodedDevice = encodeURIComponent(device);
            const socket = new WebSocket(`${this.websocketOrigin()}/ws/android/${encodedDevice}`);
            socket.binaryType = 'blob';
            this.sockets.set(device, socket);
            socket.onmessage = event => {
                if (this.disposed || this.sockets.get(device) !== socket || !(event.data instanceof Blob)) {
                    return;
                }
                const previous = this.objectUrls.get(device);
                if (previous) {
                    window.URL.revokeObjectURL(previous);
                }
                const url = window.URL.createObjectURL(event.data);
                this.objectUrls.set(device, url);
                this.frames[device] = url;
                this.states[device] = 'streaming';
            };
            socket.onerror = () => {
                if (!this.disposed && this.sockets.get(device) === socket) this.states[device] = 'error';
            };
            socket.onclose = () => {
                if (this.disposed || this.sockets.get(device) !== socket) return;
                if (this.states[device] === 'streaming') {
                    this.states[device] = 'disconnected';
                } else if (this.states[device] !== 'error') {
                    this.states[device] = 'disconnected';
                }
            };
        },
        async reconnectDevice(device) {
            if (this.disposed) return;
            const generation = this.loadGeneration;
            this.reconnecting[device] = true;
            try {
                const response = await fetch(`${this.apiOrigin()}/api/android/reconnect/${encodeURIComponent(device)}`, { method: 'POST' });
                if (!response.ok) {
                    throw new Error(`服务返回 ${response.status}`);
                }
                if (this.disposed || generation !== this.loadGeneration) return;
                const current = this.sockets.get(device);
                current?.close();
                this.states[device] = 'connecting';
                this.initWebSocket(device);
            } catch (error) {
                if (this.disposed || generation !== this.loadGeneration) return;
                this.states[device] = 'error';
                console.error(error);
            } finally {
                this.reconnecting[device] = false;
            }
        },
        statusClass(device) {
            return `is-${this.states[device] || 'connecting'}`;
        },
        statusText(device) {
            return ({ connecting: '连接中...', streaming: '实时传输中', disconnected: '已断开', error: '连接异常' })[this.states[device]] || '连接中...';
        }
    }
};
</script>

<template>
<div id="androidMonitorApp" class="android-monitor-page">
    <header class="android-monitor-header">
        <div class="android-brand">
            <span class="android-brand-icon"><WorkbenchIcon name="phone" /></span>
            <h1>Android 多设备监控大屏</h1>
        </div>
        <div class="android-nav-actions">
            <label class="android-server-input">
                <span>服务:</span>
                <input v-model.trim="serverHost" type="text" aria-label="监控服务地址" v-on:keyup.enter="loadDevices">
            </label>
            <button class="android-btn android-btn-primary" type="button" v-on:click="loadDevices">&#8635; 刷新列表</button>
        </div>
    </header>

    <div class="android-status-bar">
        <div>监控设备数: <strong>{{ devices.length }}</strong></div>
        <div class="android-channel-status">传输通道: WebSocket 全双工流式传输</div>
    </div>

    <main class="android-monitor-main">
        <div v-if="errorMessage" class="android-monitor-error" role="alert">{{ errorMessage }}</div>
        <div v-else-if="loading" class="android-monitor-empty" role="status">正在读取设备列表...</div>
        <div v-else-if="devices.length === 0" class="android-monitor-empty" role="status">devices.txt 中暂无设备</div>
        <div v-else class="android-monitor-grid">
            <article v-for="device in devices" :key="device" class="android-device-card">
                <div class="android-card-header">
                    <div class="android-card-title">
                        <span class="android-live-dot" :class="statusClass(device)" aria-hidden="true"></span>
                        <span>{{ device }}</span>
                    </div>
                </div>
                <div class="android-screen-container">
                    <img v-if="frames[device]" class="android-screen-img" :src="frames[device]" :alt="`${device} 实时画面`">
                    <span v-else class="android-screen-placeholder">{{ statusText(device) }}</span>
                </div>
                <div class="android-card-actions">
                    <span class="android-device-status">{{ statusText(device) }}</span>
                    <button class="android-btn android-btn-compact" type="button" v-on:click="reconnectDevice(device)" :disabled="reconnecting[device]">&#9889; {{ reconnecting[device] ? '重连中...' : '重连' }}</button>
                </div>
            </article>
        </div>
    </main>
</div>
</template>
