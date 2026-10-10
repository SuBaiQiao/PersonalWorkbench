import { message } from 'ant-design-vue';

export async function copyText(text) {
    if (!text) {
        return;
    }

    await window.navigator.clipboard.writeText(text);
}

export async function copyWithFeedback(text, successMessage, failureMessage = '复制失败，请检查浏览器剪贴板权限。') {
    try {
        await copyText(text);
        message.success(successMessage);
        return true;
    } catch {
        message.error(failureMessage);
        return false;
    }
}
