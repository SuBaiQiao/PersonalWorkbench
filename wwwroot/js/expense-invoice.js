(function initializeExpenseInvoice({ api, createFileFormData, createStatus, downloadBlob, formatFileSize, getDownloadFileName, getErrorMessage, isFileDrag, mountVueApp, openFileDialog, prepareFileDrop }) {
    mountVueApp('#expenseInvoiceApp', {
        data() {
            return {
                files: [],
                isDragging: false,
                loading: false,
                status: createStatus()
            };
        },
        methods: {
            formatFileSize,

            /** 打开隐藏的原生多文件选择器。 */
            openFileDialog() {
                openFileDialog(this.$refs.fileInput);
            },

            /** 将文件选择器中的文件追加到当前处理列表。 */
            handleFileChange(event) {
                this.addFiles(event.target.files);
                event.target.value = '';
            },

            /** 允许文件拖入并显示拖拽反馈。 */
            handleDragOver(event) {
                if (prepareFileDrop(event)) {
                    this.isDragging = true;
                }
            },

            /** 将拖入的文件追加到当前处理列表。 */
            handleDrop(event) {
                this.isDragging = false;
                if (isFileDrag(event)) {
                    this.addFiles(event.dataTransfer.files);
                }
            },

            /** 过滤 PDF 并避免同一批次加入完全相同的文件。 */
            addFiles(fileList) {
                const incoming = [...fileList].filter(file => file.name.toLowerCase().endsWith('.pdf'));
                const existingKeys = new Set(this.files.map(file => `${file.name}:${file.size}:${file.lastModified}`));
                this.files.push(...incoming.filter(file => !existingKeys.has(`${file.name}:${file.size}:${file.lastModified}`)));
                this.status = incoming.length === fileList.length
                    ? createStatus()
                    : createStatus('已忽略非 PDF 文件。', 'warning');
            },

            /** 清空本次选择的文件和页面状态。 */
            clearSelection() {
                this.files = [];
                this.status = createStatus();
            },

            /** 上传本次选择的 PDF，并下载后端返回的合并结果。 */
            async mergePdfs() {
                if (this.files.length === 0) {
                    return;
                }

                this.loading = true;
                this.status = createStatus('正在上传并处理 PDF…');
                const formData = createFileFormData('files', this.files);

                try {
                    const response = await api.post('/api/pdf/expense-merge', formData, { responseType: 'blob' });
                    const fileName = getDownloadFileName(response.headers, '合并后的PDF文件.pdf');
                    downloadBlob(response.data, fileName);
                    this.status = createStatus(`处理完成，已生成 ${this.files.length} 个文件的合并结果。`, 'success');
                } catch (error) {
                    this.status = createStatus(await getErrorMessage(error, 'PDF 处理失败，请稍后重试。'), 'error');
                } finally {
                    this.loading = false;
                }
            }
        }
    });
}(window.Workbench));
