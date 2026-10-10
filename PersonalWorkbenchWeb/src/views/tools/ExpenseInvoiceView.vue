<script>
import { api, getErrorMessage } from '../../api/client.js';
import { createFileFormData, downloadBlob, formatFileSize, getDownloadFileName, isFileDrag, openFileDialog, prepareFileDrop } from '../../utils/files.js';
import { createStatus } from '../../utils/status.js';

export default {
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
};
</script>

<template>
<div id="expenseInvoiceApp">
        <section class="page-intro">
            <p class="eyebrow">PDF UTILITY / EXPENSE WORKFLOW</p>
            <h1>报销发票 PDF 合并</h1>
            <p class="intro-copy">选择本次需要处理的所有 PDF。系统会按文件选择顺序合并，并将第一页高度超过 600pt 的文件放到结果末尾。</p>
        </section>

        <section class="panel converter-panel pdf-panel" aria-labelledby="pdf-form-title">
            <div class="panel-heading">
                <div>
                    <p class="panel-kicker">SELECT FILES</p>
                    <h2 id="pdf-form-title">本次处理文件</h2>
                </div>
                <span class="panel-index">03</span>
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
                    <span class="upload-mark" aria-hidden="true">PDF</span>
                    <span class="upload-title">选择多个 PDF 文件</span>
                    <span class="upload-hint">也可以直接把 PDF 拖到这里；文件只在本次请求中处理，不会扫描其他文件夹或长期保存</span>
                    <a-button v-on:click.stop="openFileDialog">选择文件</a-button>
                    <input ref="fileInput" type="file" accept="application/pdf,.pdf" multiple hidden v-on:change="handleFileChange" />
                </div>

                <div class="selected-files" aria-live="polite">
                        <a-empty v-if="files.length === 0" description="还没有选择文件"></a-empty>
                    <template v-else>
                        <p class="file-summary">已选择 {{ files.length }} 个文件，合并顺序按选择顺序处理</p>
                        <ol class="file-list">
                            <li v-for="(file, index) in files" :key="`${file.name}-${file.size}-${index}`">
                                <span>{{ file.name }}</span>
                                <small>{{ formatFileSize(file.size) }}</small>
                            </li>
                        </ol>
                    </template>
                </div>

                <div class="form-actions converter-actions">
                    <a-button type="primary" :loading="loading" :disabled="files.length === 0" v-on:click="mergePdfs">合并并下载</a-button>
                    <a-button :disabled="files.length === 0 || loading" v-on:click="clearSelection">清空选择</a-button>
                </div>

                <a-alert v-if="status.message" :message="status.message" :type="status.type" show-icon></a-alert>
            </div>
        </section>

        <section class="pdf-rules" aria-labelledby="pdf-rules-title">
            <div>
                <p class="panel-kicker">PROCESSING RULES</p>
                <h2 id="pdf-rules-title">处理规则</h2>
            </div>
            <ol>
                <li>只处理本次选择的 PDF 文件。</li>
                <li>保持文件选择顺序，普通页面先合并。</li>
                <li>第一页高度超过 600pt 的文件统一放到最后。</li>
            </ol>
        </section>
</div>
</template>
