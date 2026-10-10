<script>
import { api, getErrorMessage } from '../../api/client.js';
import { copyWithFeedback } from '../../utils/clipboard.js';
import { createFileFormData, downloadBlob, formatFileSize, getDownloadFileName, isFileDrag, openFileDialog, prepareFileDrop } from '../../utils/files.js';
import { createStatus } from '../../utils/status.js';

export default {
    data() {
        return {
            decodeForm: { base64: '', fileName: '' },
            decodeLoading: false,
            decodeStatus: createStatus(),
            file: null,
            isDragging: false,
            encodeLoading: false,
            encodeStatus: createStatus(),
            base64Output: '',
            previewUrl: ''
        };
    },
    methods: {
        formatFileSize,

        /** 将 Base64 请求后端解码并下载返回的图片。 */
        async convertBase64ToImage() {
            this.decodeLoading = true;
            this.decodeStatus = createStatus('正在请求后端解码…');
            try {
                const response = await api.post('/api/image/base64-to-image', this.decodeForm, { responseType: 'blob' });
                const fileName = getDownloadFileName(response.headers, 'converted-image');
                downloadBlob(response.data, fileName);
                this.decodeStatus = createStatus('图片已生成并开始下载。', 'success');
            } catch (error) {
                this.decodeStatus = createStatus(await getErrorMessage(error), 'error');
            } finally {
                this.decodeLoading = false;
            }
        },

        /** 清空 Base64 解码表单。 */
        clearDecodeForm() {
            this.decodeForm = { base64: '', fileName: '' };
            this.decodeStatus = createStatus();
        },

        /** 打开隐藏的图片文件选择器。 */
        openFileDialog() {
            openFileDialog(this.$refs.fileInput);
        },

        /** 保存文件选择器返回的首张图片。 */
        handleFileChange(event) {
            this.setFile(event.target.files[0]);
            event.target.value = '';
        },

        /** 允许图片文件拖入上传区域。 */
        handleDragOver(event) {
            if (prepareFileDrop(event)) {
                this.isDragging = true;
            }
        },

        /** 保存拖入的首张图片。 */
        handleDrop(event) {
            this.isDragging = false;
            if (isFileDrag(event)) {
                this.setFile(event.dataTransfer.files[0]);
            }
        },

        /** 更新当前图片并清空上一轮转换结果。 */
        setFile(file) {
            this.file = file || null;
            this.base64Output = '';
            this.previewUrl = '';
            this.encodeStatus = createStatus();
        },

        /** 上传图片并获取后端生成的 Base64 Data URL。 */
        async convertImageToBase64() {
            if (!this.file) {
                return;
            }

            this.encodeLoading = true;
            this.encodeStatus = createStatus('正在上传并请求后端编码…');
            const formData = createFileFormData('file', this.file);

            try {
                const response = await api.post('/api/image/image-to-base64', formData);
                this.base64Output = response.data.dataUrl;
                this.previewUrl = response.data.dataUrl;
                this.encodeStatus = createStatus('转换完成。', 'success');
            } catch (error) {
                this.encodeStatus = createStatus(await getErrorMessage(error), 'error');
            } finally {
                this.encodeLoading = false;
            }
        },

        /** 清空图片、预览和 Base64 结果。 */
        clearEncodeForm() {
            this.file = null;
            this.base64Output = '';
            this.previewUrl = '';
            this.encodeStatus = createStatus();
        },

        /** 将生成的 Data URL 复制到剪贴板。 */
        async copyBase64() {
            await copyWithFeedback(this.base64Output, 'Base64 已复制到剪贴板。');
        }
    }
};
</script>

<template>
<div id="imageBase64App">
        <section class="page-intro" aria-labelledby="image-base64-title">
            <p class="eyebrow">IMAGE UTILITY / BASE64</p>
            <h1 id="image-base64-title">图片与 Base64 转换器</h1>
            <p class="intro-copy">在图片文件和 Base64 文本之间互相转换，实际编码与解码由后端接口完成。</p>
        </section>

        <div class="image-converter-grid">
            <section class="panel converter-panel image-converter-panel" aria-labelledby="base64-to-image-title">
                <div class="panel-heading">
                    <div>
                        <p class="panel-kicker">BASE64 → IMAGE</p>
                        <h2 id="base64-to-image-title">Base64 转图片</h2>
                    </div>
                    <span class="panel-index">04A</span>
                </div>

                <a-form layout="vertical" v-on:finish="convertBase64ToImage">
                    <a-form-item label="Base64 内容或 Data URL">
                        <a-textarea v-model:value="decodeForm.base64" :rows="12" placeholder="粘贴 data:image/png;base64,... 或纯 Base64 内容"></a-textarea>
                    </a-form-item>
                    <a-form-item label="下载文件名（可选）">
                        <a-input v-model:value="decodeForm.fileName" placeholder="converted-image.png"></a-input>
                    </a-form-item>
                    <div class="form-actions converter-actions">
                        <a-button type="primary" html-type="button" :loading="decodeLoading" v-on:click="convertBase64ToImage">转换并下载</a-button>
                        <a-button v-on:click="clearDecodeForm">清空</a-button>
                    </div>
                    <a-alert v-if="decodeStatus.message" :message="decodeStatus.message" :type="decodeStatus.type" show-icon></a-alert>
                    <div class="output-footer">
                        <span>API · <code>POST /api/image/base64-to-image</code></span>
                    </div>
                </a-form>
            </section>

            <section class="panel converter-panel image-converter-panel" aria-labelledby="image-to-base64-title">
                <div class="panel-heading">
                    <div>
                        <p class="panel-kicker">IMAGE → BASE64</p>
                        <h2 id="image-to-base64-title">图片转 Base64</h2>
                    </div>
                    <span class="panel-index">04B</span>
                </div>

                <div class="converter-form">
                    <div class="upload-zone" :class="{ 'is-dragover': isDragging }"
                         role="button" tabindex="0"
                         v-on:click="openFileDialog"
                         v-on:keydown.enter="openFileDialog"
                         v-on:keydown.space.prevent="openFileDialog"
                         v-on:dragover.prevent="handleDragOver"
                         v-on:dragleave.prevent="isDragging = false"
                         v-on:drop.prevent="handleDrop">
                        <span class="upload-mark" aria-hidden="true">IMG</span>
                        <span class="upload-title">选择一张图片</span>
                        <span class="upload-hint">支持 PNG、JPG、GIF、WEBP、BMP 和 SVG，单张不超过 20 MB</span>
                        <a-button v-on:click.stop="openFileDialog">选择文件</a-button>
                        <input ref="fileInput" type="file" accept="image/png,image/jpeg,image/gif,image/webp,image/bmp,image/svg+xml,.png,.jpg,.jpeg,.gif,.webp,.bmp,.svg" hidden v-on:change="handleFileChange" />
                    </div>

                    <div class="selected-files" aria-live="polite">
                        <a-empty v-if="!file" description="还没有选择图片"></a-empty>
                        <div v-else class="selected-file">
                            <strong>{{ file.name }}</strong>
                            <span>{{ formatFileSize(file.size) }}</span>
                        </div>
                    </div>

                    <div class="form-actions converter-actions">
                        <a-button type="primary" :loading="encodeLoading" :disabled="!file" v-on:click="convertImageToBase64">转换为 Base64</a-button>
                        <a-button :disabled="!file || encodeLoading" v-on:click="clearEncodeForm">清空选择</a-button>
                    </div>
                    <a-alert v-if="encodeStatus.message" :message="encodeStatus.message" :type="encodeStatus.type" show-icon></a-alert>

                    <a-form-item label="Base64 Data URL">
                        <a-textarea v-model:value="base64Output" :rows="8" readonly placeholder="转换结果将显示在这里"></a-textarea>
                    </a-form-item>

                    <div class="output-footer">
                        <span>API · <code>POST /api/image/image-to-base64</code></span>
                        <a-button :disabled="!base64Output" v-on:click="copyBase64">复制结果</a-button>
                    </div>
                    <a-image v-if="previewUrl" class="image-preview" :src="previewUrl" alt="转换后的图片预览"></a-image>
                </div>
            </section>
        </div>

        <section class="image-converter-note" aria-label="格式说明">
            <p><strong>提示：</strong>Base64 转图片支持纯 Base64 和以 <code>data:image/...</code> 开头的 Data URL。未提供文件名时，系统会根据图片格式生成下载文件名。</p>
        </section>
</div>
</template>
