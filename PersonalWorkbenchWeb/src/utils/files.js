export function formatFileSize(bytes) {
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

export function getDownloadFileName(headers, fallback) {
    const disposition = headers?.['content-disposition'] || '';
    const encodedName = disposition.match(/filename\*=UTF-8''([^;]+)/i);
    if (encodedName) {
        return decodeURIComponent(encodedName[1]);
    }

    const plainName = disposition.match(/filename="?([^";]+)"?/i);
    return plainName ? plainName[1] : fallback;
}

export function downloadBlob(blob, fileName) {
    const objectUrl = window.URL.createObjectURL(blob);
    const link = window.document.createElement('a');
    link.href = objectUrl;
    link.download = fileName;
    window.document.body.appendChild(link);
    link.click();
    link.remove();
    window.setTimeout(() => window.URL.revokeObjectURL(objectUrl), 1000);
}

export function isFileDrag(event) {
    return event?.dataTransfer?.types?.includes('Files') === true;
}

export function prepareFileDrop(event) {
    if (!isFileDrag(event)) {
        return false;
    }

    event.dataTransfer.dropEffect = 'copy';
    return true;
}

export function openFileDialog(input) {
    input?.click();
}

export function createFileFormData(fieldName, files) {
    const formData = new FormData();
    const values = files instanceof File ? [files] : [...files];
    values.forEach(file => formData.append(fieldName, file, file.name));
    return formData;
}
