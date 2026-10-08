(function initializeImageBase64({ api, copyWithFeedback, createFileFormData, createStatus, downloadBlob, formatFileSize, getDownloadFileName, getErrorMessage, isFileDrag, mountVueApp, openFileDialog, prepareFileDrop }) {
    mountVueApp('#imageBase64App', {
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
    });
}(window.Workbench));
