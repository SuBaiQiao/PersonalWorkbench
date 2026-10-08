(function initializeWorkbench(global) {
    'use strict';

    const defaultErrorMessage = '请求失败，请稍后重试。';
    const api = global.axios?.create({
        baseURL: '/',
        timeout: 120000,
        headers: { 'X-Requested-With': 'XMLHttpRequest' }
    });

    /**
     * 检查公共布局中的前端依赖是否均已加载成功。
     * @throws {Error} 缺少 Vue、Ant Design Vue 或 axios 时抛出。
     */
    function assertFrontendDependencies() {
        const missingDependencies = [
            ['Vue', global.Vue],
            ['Ant Design Vue', global.antd],
            ['axios', global.axios]
        ]
            .filter(([, dependency]) => !dependency)
            .map(([name]) => name);

        if (missingDependencies.length > 0) {
            throw new Error(`前端依赖加载失败：${missingDependencies.join('、')}。请检查 CDN 资源和网络连接。`);
        }
    }

    /**
     * 从 axios 异常中提取后端业务提示，屏蔽不同响应类型的处理差异。
     * @param {unknown} error axios 或浏览器异常。
     * @param {string} fallback 无明确错误信息时使用的提示。
     * @returns {Promise<string>} 可直接展示给用户的错误信息。
     */
    async function getErrorMessage(error, fallback = defaultErrorMessage) {
        const responseData = error?.response?.data;
        if (responseData instanceof Blob) {
            try {
                const text = await responseData.text();
                const payload = JSON.parse(text);
                return payload.message || fallback;
            } catch {
                return error?.message || fallback;
            }
        }

        if (typeof responseData === 'string' && responseData.trim()) {
            return responseData;
        }

        if (responseData?.message) {
            return responseData.message;
        }

        return error?.message || fallback;
    }

    /**
     * 将文本写入系统剪贴板。
     * @param {string} text 待复制的文本。
     * @returns {Promise<void>} 复制完成任务。
     */
    async function copyText(text) {
        if (!text) {
            return;
        }

        await global.navigator.clipboard.writeText(text);
    }

    /**
     * 复制文本并使用 Ant Design 消息组件反馈处理结果。
     * @param {string} text 待复制的文本。
     * @param {string} successMessage 复制成功提示。
     * @param {string} failureMessage 复制失败提示。
     * @returns {Promise<boolean>} 复制成功时返回 true。
     */
    async function copyWithFeedback(text, successMessage, failureMessage = '复制失败，请检查浏览器剪贴板权限。') {
        try {
            await copyText(text);
            global.antd.message.success(successMessage);
            return true;
        } catch {
            global.antd.message.error(failureMessage);
            return false;
        }
    }

    /**
     * 创建页面统一使用的状态提示对象。
     * @param {string} message 提示文字。
     * @param {'info'|'success'|'warning'|'error'} type Ant Design Alert 类型。
     * @returns {{message: string, type: string}} 页面状态对象。
     */
    function createStatus(message = '', type = 'info') {
        return { message, type };
    }

    /**
     * 将字节数格式化为适合文件列表展示的单位。
     * @param {number} bytes 文件字节数。
     * @returns {string} 格式化后的文件大小。
     */
    function formatFileSize(bytes) {
        if (!Number.isFinite(bytes) || bytes <= 0) {
            return '0 B';
        }

        if (bytes < 1024) {
            return `${bytes} B`;
        }

        if (bytes < 1024 * 1024) {
            return `${Math.max(1, Math.round(bytes / 1024))} KB`;
        }

        return `${(bytes / 1024 / 1024).toFixed(2)} MB`;
    }

    /**
     * 从 Content-Disposition 响应头解析服务端返回的下载文件名。
     * @param {Record<string, string>} headers axios 响应头。
     * @param {string} fallback 无文件名时使用的默认名称。
     * @returns {string} 下载文件名。
     */
    function getDownloadFileName(headers, fallback) {
        const disposition = headers?.['content-disposition'] || '';
        const encodedName = disposition.match(/filename\*=UTF-8''([^;]+)/i);
        if (encodedName) {
            return decodeURIComponent(encodedName[1]);
        }

        const plainName = disposition.match(/filename="?([^";]+)"?/i);
        return plainName ? plainName[1] : fallback;
    }

    /**
     * 使用临时对象 URL 下载 Blob，并在下载触发后释放浏览器资源。
     * @param {Blob} blob 待下载的二进制内容。
     * @param {string} fileName 下载文件名。
     */
    function downloadBlob(blob, fileName) {
        const objectUrl = global.URL.createObjectURL(blob);
        const link = global.document.createElement('a');
        link.href = objectUrl;
        link.download = fileName;
        global.document.body.appendChild(link);
        link.click();
        link.remove();
        global.setTimeout(() => global.URL.revokeObjectURL(objectUrl), 1000);
    }

    /**
     * 判断拖拽事件是否包含操作系统文件。
     * @param {DragEvent} event 浏览器拖拽事件。
     * @returns {boolean} 包含文件时返回 true。
     */
    function isFileDrag(event) {
        return event?.dataTransfer?.types?.includes('Files') === true;
    }

    /**
     * 设置文件拖拽的复制效果。
     * @param {DragEvent} event 浏览器拖拽事件。
     * @returns {boolean} 当前事件包含文件时返回 true。
     */
    function prepareFileDrop(event) {
        if (!isFileDrag(event)) {
            return false;
        }

        event.dataTransfer.dropEffect = 'copy';
        return true;
    }

    /**
     * 打开指定的隐藏文件选择器。
     * @param {HTMLInputElement} input 文件输入控件。
     */
    function openFileDialog(input) {
        input?.click();
    }

    /**
     * 将一个或多个文件加入 multipart/form-data。
     * @param {string} fieldName 后端接收的表单字段名。
     * @param {File|Iterable<File>} files 单个文件或文件集合。
     * @returns {FormData} 可直接交给 axios 的表单对象。
     */
    function createFileFormData(fieldName, files) {
        const formData = new FormData();
        const values = files instanceof File ? [files] : [...files];
        values.forEach(file => formData.append(fieldName, file, file.name));
        return formData;
    }

    /**
     * 创建并挂载统一注册 Ant Design Vue 的 Vue 应用。
     * @param {string} selector 页面 Vue 根节点选择器。
     * @param {import('vue').Component} rootComponent Vue 根组件配置。
     * @returns {object} 已挂载的 Vue 组件实例。
     */
    function mountVueApp(selector, rootComponent) {
        assertFrontendDependencies();
        const element = global.document.querySelector(selector);
        if (!element) {
            throw new Error(`找不到 Vue 挂载节点：${selector}`);
        }

        const themeConfig = {
            token: {
                colorPrimary: '#f97316',
                colorInfo: '#60a5fa',
                borderRadius: 6,
                colorBgContainer: '#23231f',
                colorBgElevated: '#2a2924'
            }
        };
        if (global.antd.theme?.darkAlgorithm) {
            themeConfig.algorithm = global.antd.theme.darkAlgorithm;
        }

        const app = global.Vue.createApp(rootComponent);
        app.config.errorHandler = (error) => {
            global.console.error('Vue application error:', error);
            global.antd.message.error(error?.message || '页面操作失败，请打开控制台查看详细信息。');
        };
        app.use(global.antd);
        app.mixin({
            data() {
                return {
                    workbenchTheme: themeConfig
                };
            }
        });
        return app.mount(element);
    }

    global.Workbench = Object.freeze({
        api,
        assertFrontendDependencies,
        copyText,
        copyWithFeedback,
        createFileFormData,
        createStatus,
        downloadBlob,
        formatFileSize,
        getDownloadFileName,
        getErrorMessage,
        isFileDrag,
        mountVueApp,
        openFileDialog,
        prepareFileDrop
    });
}(window));
