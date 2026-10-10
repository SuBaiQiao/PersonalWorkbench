import axios from 'axios';

export const api = axios.create({
    baseURL: '/',
    timeout: 120000,
    headers: { 'X-Requested-With': 'XMLHttpRequest' }
});

const defaultErrorMessage = '请求失败，请稍后重试。';

export async function getErrorMessage(error, fallback = defaultErrorMessage) {
    const responseData = error?.response?.data;
    if (responseData instanceof Blob) {
        try {
            const text = await responseData.text();
            const payload = JSON.parse(text);
            return payload.message || payload.detail || payload.title || fallback;
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

    if (responseData?.detail || responseData?.title) {
        return responseData.detail || responseData.title;
    }

    return error?.message || fallback;
}
