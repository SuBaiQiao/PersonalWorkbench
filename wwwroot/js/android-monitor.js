(function initializeAndroidMonitor(global) {
    'use strict';

    const root = global.document.getElementById('androidMonitorApp');
    if (!root) {
        return;
    }

    const initialHost = `${global.location.host}`;
    const sockets = new Map();
    const objectUrls = new Map();

    Workbench.mountVueApp('#androidMonitorApp', {
        data() {
            return {
                serverHost: initialHost,
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
            sockets.forEach(socket => socket.close());
            objectUrls.forEach(url => global.URL.revokeObjectURL(url));
        },
        methods: {
            apiOrigin() {
                const host = this.serverHost.trim();
                return `${global.location.protocol}//${host}`;
            },
            websocketOrigin() {
                const host = this.serverHost.trim();
                const protocol = global.location.protocol === 'https:' ? 'wss:' : 'ws:';
                return `${protocol}//${host}`;
            },
            closeSockets() {
                sockets.forEach(socket => socket.close());
                sockets.clear();
                objectUrls.forEach(url => global.URL.revokeObjectURL(url));
                objectUrls.clear();
            },
            async loadDevices() {
                this.loading = true;
                this.errorMessage = '';
                this.closeSockets();
                try {
                    const response = await fetch(`${this.apiOrigin()}/api/android/devices`, { headers: { 'Accept': 'application/json' } });
                    if (!response.ok) {
                        throw new Error(`服务返回 ${response.status}`);
                    }
                    const payload = await response.json();
                    this.devices = payload.devices || [];
                    this.states = Object.fromEntries(this.devices.map(device => [device, 'connecting']));
                    this.devices.forEach(device => this.initWebSocket(device));
                } catch (error) {
                    this.devices = [];
                    this.errorMessage = `无法连接到后台服务器 ${this.apiOrigin()}`;
                    console.error(error);
                } finally {
                    this.loading = false;
                }
            },
            initWebSocket(device) {
                const encodedDevice = encodeURIComponent(device);
                const socket = new WebSocket(`${this.websocketOrigin()}/ws/android/${encodedDevice}`);
                socket.binaryType = 'blob';
                sockets.set(device, socket);
                socket.onmessage = event => {
                    if (!(event.data instanceof Blob)) {
                        return;
                    }
                    const previous = objectUrls.get(device);
                    if (previous) {
                        global.URL.revokeObjectURL(previous);
                    }
                    const url = global.URL.createObjectURL(event.data);
                    objectUrls.set(device, url);
                    this.frames[device] = url;
                    this.states[device] = 'streaming';
                };
                socket.onerror = () => { this.states[device] = 'error'; };
                socket.onclose = () => {
                    if (this.states[device] === 'streaming') {
                        this.states[device] = 'disconnected';
                    } else if (this.states[device] !== 'error') {
                        this.states[device] = 'disconnected';
                    }
                };
            },
            async reconnectDevice(device) {
                this.reconnecting[device] = true;
                try {
                    const response = await fetch(`${this.apiOrigin()}/api/android/reconnect/${encodeURIComponent(device)}`, { method: 'POST' });
                    if (!response.ok) {
                        throw new Error(`服务返回 ${response.status}`);
                    }
                    const current = sockets.get(device);
                    current?.close();
                    this.states[device] = 'connecting';
                    this.initWebSocket(device);
                } catch (error) {
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
    });
}(window));
