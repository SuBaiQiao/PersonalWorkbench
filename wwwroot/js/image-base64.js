const base64ToImageForm = document.querySelector('#base64ToImageForm');
const base64Input = document.querySelector('#base64Input');
const base64FileName = document.querySelector('#base64FileName');
const base64ToImageButton = document.querySelector('#base64ToImageButton');
const clearBase64Button = document.querySelector('#clearBase64Button');
const base64ToImageStatus = document.querySelector('#base64ToImageStatus');

const imageToBase64Form = document.querySelector('#imageToBase64Form');
const imageUploadZone = document.querySelector('#imageUploadZone');
const imageFileInput = document.querySelector('#imageFile');
const imageFileSummary = document.querySelector('#imageFileSummary');
const imageToBase64Button = document.querySelector('#imageToBase64Button');
const clearImageButton = document.querySelector('#clearImageButton');
const imageToBase64Status = document.querySelector('#imageToBase64Status');
const base64Output = document.querySelector('#base64Output');
const copyBase64Button = document.querySelector('#copyBase64Button');
const imagePreview = document.querySelector('#imagePreview');

function setImageStatus(element, message, tone = '') {
    element.textContent = message;
    if (tone) {
        element.dataset.tone = tone;
    } else {
        delete element.dataset.tone;
    }
}

async function readImageError(response) {
    try {
        const payload = await response.json();
        return payload.message || '请求失败，请稍后重试。';
    } catch {
        return '请求失败，请稍后重试。';
    }
}

function getDownloadFileName(response) {
    const disposition = response.headers.get('Content-Disposition') || '';
    const encodedName = disposition.match(/filename\*=UTF-8''([^;]+)/i);
    if (encodedName) {
        return decodeURIComponent(encodedName[1]);
    }

    const plainName = disposition.match(/filename="?([^";]+)"?/i);
    return plainName ? plainName[1] : 'converted-image';
}

function downloadBlob(blob, fileName) {
    const objectUrl = URL.createObjectURL(blob);
    const link = document.createElement('a');
    link.href = objectUrl;
    link.download = fileName;
    document.body.appendChild(link);
    link.click();
    link.remove();
    window.setTimeout(() => URL.revokeObjectURL(objectUrl), 1000);
}

async function convertBase64ToImage(event) {
    event.preventDefault();
    base64ToImageButton.disabled = true;
    setImageStatus(base64ToImageStatus, '正在请求后端解码…');

    try {
        const response = await fetch('/api/image/base64-to-image', {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({
                base64: base64Input.value,
                fileName: base64FileName.value
            })
        });

        if (!response.ok) {
            throw new Error(await readImageError(response));
        }

        downloadBlob(await response.blob(), getDownloadFileName(response));
        setImageStatus(base64ToImageStatus, '图片已生成并开始下载。', 'success');
    } catch (error) {
        setImageStatus(base64ToImageStatus, error.message || '请求失败，请稍后重试。', 'error');
    } finally {
        base64ToImageButton.disabled = false;
    }
}

function clearBase64ToImage() {
    base64Input.value = '';
    base64FileName.value = '';
    setImageStatus(base64ToImageStatus, '');
    base64Input.focus();
}

function renderImageFile(file) {
    if (!file) {
        imageFileSummary.innerHTML = '<p class="file-empty">还没有选择图片</p>';
        imageToBase64Button.disabled = true;
        clearImageButton.disabled = true;
        return;
    }

    imageFileSummary.innerHTML = `<div class="selected-file"><strong>${escapeHtml(file.name)}</strong><span>${formatBytes(file.size)}</span></div>`;
    imageToBase64Button.disabled = false;
    clearImageButton.disabled = false;
}

function escapeHtml(value) {
    return value.replace(/[&<>'"]/g, character => ({
        '&': '&amp;',
        '<': '&lt;',
        '>': '&gt;',
        "'": '&#39;',
        '"': '&quot;'
    }[character]));
}

function formatBytes(size) {
    if (size < 1024) {
        return `${size} B`;
    }

    return `${(size / 1024 / 1024).toFixed(2)} MB`;
}

async function convertImageToBase64(event) {
    event.preventDefault();
    const file = imageFileInput.files[0];
    if (!file) {
        return;
    }

    imageToBase64Button.disabled = true;
    setImageStatus(imageToBase64Status, '正在上传并请求后端编码…');
    const formData = new FormData();
    formData.append('file', file);

    try {
        const response = await fetch('/api/image/image-to-base64', {
            method: 'POST',
            body: formData
        });

        if (!response.ok) {
            throw new Error(await readImageError(response));
        }

        const result = await response.json();
        base64Output.value = result.dataUrl;
        imagePreview.src = result.dataUrl;
        imagePreview.hidden = false;
        copyBase64Button.disabled = !result.dataUrl;
        setImageStatus(imageToBase64Status, '转换完成。', 'success');
    } catch (error) {
        setImageStatus(imageToBase64Status, error.message || '请求失败，请稍后重试。', 'error');
    } finally {
        imageToBase64Button.disabled = false;
    }
}

function clearImageToBase64() {
    imageFileInput.value = '';
    base64Output.value = '';
    imagePreview.removeAttribute('src');
    imagePreview.hidden = true;
    copyBase64Button.disabled = true;
    setImageStatus(imageToBase64Status, '');
    renderImageFile(null);
}

async function copyBase64() {
    if (!base64Output.value) {
        return;
    }

    try {
        await navigator.clipboard.writeText(base64Output.value);
        setImageStatus(imageToBase64Status, 'Base64 已复制到剪贴板。', 'success');
    } catch {
        setImageStatus(imageToBase64Status, '复制失败，请检查浏览器剪贴板权限。', 'error');
    }
}

function handleImageDrop(event) {
    event.preventDefault();
    imageUploadZone.classList.remove('is-dragover');
    const [file] = event.dataTransfer.files;
    if (!file) {
        return;
    }

    const transfer = new DataTransfer();
    transfer.items.add(file);
    imageFileInput.files = transfer.files;
    renderImageFile(file);
}

base64ToImageForm.addEventListener('submit', convertBase64ToImage);
clearBase64Button.addEventListener('click', clearBase64ToImage);
imageToBase64Form.addEventListener('submit', convertImageToBase64);
clearImageButton.addEventListener('click', clearImageToBase64);
copyBase64Button.addEventListener('click', copyBase64);
imageFileInput.addEventListener('change', () => renderImageFile(imageFileInput.files[0]));
imageUploadZone.addEventListener('dragover', event => {
    event.preventDefault();
    imageUploadZone.classList.add('is-dragover');
});
imageUploadZone.addEventListener('dragleave', () => imageUploadZone.classList.remove('is-dragover'));
imageUploadZone.addEventListener('drop', handleImageDrop);
