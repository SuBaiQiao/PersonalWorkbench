const pdfMergeForm = document.querySelector('#pdfMergeForm');
const pdfFilesInput = document.querySelector('#pdfFiles');
const uploadZone = document.querySelector('#uploadZone');
const selectedFiles = document.querySelector('#selectedFiles');
const mergePdfButton = document.querySelector('#mergePdfButton');
const clearPdfButton = document.querySelector('#clearPdfButton');
const pdfStatus = document.querySelector('#pdfStatus');
let dragDepth = 0;

function setPdfStatus(message, tone = '') {
    pdfStatus.textContent = message;
    if (tone) {
        pdfStatus.dataset.tone = tone;
    } else {
        delete pdfStatus.dataset.tone;
    }
}

function formatFileSize(bytes) {
    if (bytes < 1024 * 1024) {
        return `${Math.max(1, Math.round(bytes / 1024))} KB`;
    }

    return `${(bytes / (1024 * 1024)).toFixed(1)} MB`;
}

function renderSelectedFiles() {
    const files = [...pdfFilesInput.files];
    selectedFiles.replaceChildren();
    mergePdfButton.disabled = files.length === 0;
    clearPdfButton.disabled = files.length === 0;

    if (files.length === 0) {
        const empty = document.createElement('p');
        empty.className = 'file-empty';
        empty.textContent = '还没有选择文件';
        selectedFiles.append(empty);
        return;
    }

    const summary = document.createElement('div');
    summary.className = 'file-summary';
    summary.textContent = `已选择 ${files.length} 个文件，合并顺序按选择顺序处理`;
    selectedFiles.append(summary);

    const list = document.createElement('ol');
    list.className = 'file-list';
    files.forEach((file) => {
        const item = document.createElement('li');
        const name = document.createElement('span');
        name.textContent = file.name;
        const size = document.createElement('small');
        size.textContent = formatFileSize(file.size);
        item.append(name, size);
        list.append(item);
    });
    selectedFiles.append(list);
}

function updateInputFiles(files, append = false) {
    const transfer = new DataTransfer();
    if (append) {
        [...pdfFilesInput.files].forEach((file) => transfer.items.add(file));
    }
    [...files].forEach((file) => transfer.items.add(file));
    pdfFilesInput.files = transfer.files;
    renderSelectedFiles();
}

function isFileDrag(event) {
    return event.dataTransfer?.types?.includes('Files') === true;
}

function handleDragEnter(event) {
    if (!isFileDrag(event)) {
        return;
    }

    event.preventDefault();
    dragDepth += 1;
    uploadZone.classList.add('is-dragover');
}

function handleDragOver(event) {
    if (!isFileDrag(event)) {
        return;
    }

    event.preventDefault();
    event.dataTransfer.dropEffect = 'copy';
}

function handleDragLeave(event) {
    if (!isFileDrag(event)) {
        return;
    }

    event.preventDefault();
    dragDepth = Math.max(0, dragDepth - 1);
    if (dragDepth === 0) {
        uploadZone.classList.remove('is-dragover');
    }
}

function handleDrop(event) {
    if (!isFileDrag(event)) {
        return;
    }

    event.preventDefault();
    dragDepth = 0;
    uploadZone.classList.remove('is-dragover');
    updateInputFiles(event.dataTransfer.files, true);
}

function preventWindowFileDrop(event) {
    if (isFileDrag(event)) {
        event.preventDefault();
    }
}

function clearSelection() {
    pdfFilesInput.value = '';
    renderSelectedFiles();
    setPdfStatus('');
}

async function readPdfError(response) {
    try {
        const payload = await response.json();
        return payload.message || 'PDF 处理失败，请稍后重试。';
    } catch {
        return 'PDF 处理失败，请稍后重试。';
    }
}

async function mergePdfs(event) {
    event.preventDefault();
    const files = [...pdfFilesInput.files];
    if (files.length === 0) {
        return;
    }

    mergePdfButton.disabled = true;
    clearPdfButton.disabled = true;
    setPdfStatus('正在上传并处理 PDF…');

    const formData = new FormData();
    files.forEach((file) => formData.append('files', file, file.name));

    try {
        const response = await fetch('/api/pdf/expense-merge', {
            method: 'POST',
            body: formData
        });

        if (!response.ok) {
            throw new Error(await readPdfError(response));
        }

        const blob = await response.blob();
        const downloadUrl = URL.createObjectURL(blob);
        const link = document.createElement('a');
        link.href = downloadUrl;
        link.download = '合并后的PDF文件.pdf';
        document.body.append(link);
        link.click();
        link.remove();
        URL.revokeObjectURL(downloadUrl);
        setPdfStatus(`处理完成，已生成 ${files.length} 个文件的合并结果。`, 'success');
    } catch (error) {
        setPdfStatus(error.message || 'PDF 处理失败，请稍后重试。', 'error');
    } finally {
        mergePdfButton.disabled = files.length === 0;
        clearPdfButton.disabled = files.length === 0;
    }
}

pdfFilesInput.addEventListener('change', renderSelectedFiles);
uploadZone.addEventListener('dragenter', handleDragEnter);
uploadZone.addEventListener('dragover', handleDragOver);
uploadZone.addEventListener('dragleave', handleDragLeave);
uploadZone.addEventListener('drop', handleDrop);
document.addEventListener('dragover', preventWindowFileDrop);
document.addEventListener('drop', preventWindowFileDrop);
clearPdfButton.addEventListener('click', clearSelection);
pdfMergeForm.addEventListener('submit', mergePdfs);
renderSelectedFiles();
