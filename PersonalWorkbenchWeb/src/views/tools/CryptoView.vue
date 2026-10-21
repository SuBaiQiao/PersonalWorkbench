<script setup>
import { computed, reactive, ref } from 'vue';
import { api, getErrorMessage } from '../../api/client.js';
import { copyWithFeedback } from '../../utils/clipboard.js';

const activeTab = ref('md5');
const loading = ref(false);
const status = ref('');
const result = ref('');

const md5 = reactive({ text: '' });
const encodingForms = reactive({
    base64: { operation: 'encode', text: '' },
    url: { operation: 'encode', text: '' }
});
const encodingTools = [
    { key: 'base64', name: 'Base64', hint: '使用 UTF-8 编码，支持中文和表情符号。解码时请输入有效的 Base64 文本。' },
    { key: 'url', name: 'URL', hint: '按 UTF-8 编码 URL 组件：空格编码为 %20，加号编码为 %2B；解码时保留原始加号。' }
];
const submitLabel = computed(() => {
    const form = encodingForms[activeTab.value];
    if (form) return form.operation === 'encode' ? '编码' : '解码';
    return activeTab.value === 'md5' ? '计算 MD5' : '执行操作';
});
const aes = reactive({ operation: 'encrypt', text: '', keyBase64: '', ivBase64: '' });
const rsa = reactive({ operation: 'encrypt', text: '', keyPem: '', publicKeyPem: '', privateKeyPem: '' });
const sm2 = reactive({ operation: 'encrypt', text: '', keyHex: '', publicKeyHex: '', privateKeyHex: '' });
const sm4 = reactive({ operation: 'encrypt', text: '', keyHex: '', ivHex: '' });

function clearResult() {
    result.value = '';
    status.value = '';
}

async function run(endpoint, payload) {
    loading.value = true;
    status.value = '';
    result.value = '';
    try {
        const response = await api.post(`/api/crypto/${endpoint}`, payload);
        result.value = response.data.result;
        status.value = '处理完成。';
    } catch (error) {
        status.value = await getErrorMessage(error, '处理失败。');
    } finally {
        loading.value = false;
    }
}

async function generateAesKey() {
    const response = await api.get('/api/crypto/aes/key');
    aes.keyBase64 = response.data.keyBase64;
    aes.ivBase64 = response.data.ivBase64;
}

async function generateRsaKey() {
    const response = await api.get('/api/crypto/rsa/key');
    rsa.publicKeyPem = response.data.publicKeyPem;
    rsa.privateKeyPem = response.data.privateKeyPem;
    rsa.keyPem = rsa.operation === 'encrypt' ? rsa.publicKeyPem : rsa.privateKeyPem;
}

async function generateSm2Key() {
    const response = await api.get('/api/crypto/sm2/key');
    sm2.publicKeyHex = response.data.publicKeyHex;
    sm2.privateKeyHex = response.data.privateKeyHex;
    sm2.keyHex = sm2.operation === 'encrypt' ? sm2.publicKeyHex : sm2.privateKeyHex;
}

async function generateSm4Key() {
    const response = await api.get('/api/crypto/sm4/key');
    sm4.keyHex = response.data.keyHex;
    sm4.ivHex = response.data.ivHex;
}

function syncRsaKey() { rsa.keyPem = rsa.operation === 'encrypt' ? rsa.publicKeyPem : rsa.privateKeyPem; }
function syncSm2Key() { sm2.keyHex = sm2.operation === 'encrypt' ? sm2.publicKeyHex : sm2.privateKeyHex; }
function submit() {
    if (activeTab.value === 'md5') return run('md5', md5);
    if (encodingForms[activeTab.value]) return run(activeTab.value, encodingForms[activeTab.value]);
    if (activeTab.value === 'aes') return run('aes', aes);
    if (activeTab.value === 'rsa') return run('rsa', rsa);
    if (activeTab.value === 'sm2') return run('sm2', sm2);
    return run('sm4', sm4);
}
</script>

<template>
    <section class="crypto-page" aria-labelledby="crypto-title">
        <header class="page-intro">
            <p class="eyebrow">SECURITY UTILITY / CRYPTOGRAPHY</p>
            <h1 id="crypto-title">加密工具箱</h1>
            <p class="intro-copy">支持 Base64、URL 编码与解码，以及摘要、对称加密、非对称加密和国密算法。</p>
        </header>

        <section class="panel crypto-panel">
            <a-tabs v-model:activeKey="activeTab" @change="clearResult">
                <a-tab-pane key="md5" tab="MD5">
                    <a-form layout="vertical">
                        <a-form-item label="待计算文本"><a-textarea v-model:value="md5.text" :rows="6" placeholder="输入需要计算 MD5 的文本" /></a-form-item>
                    </a-form>
                </a-tab-pane>
                <a-tab-pane v-for="tool in encodingTools" :key="tool.key" :tab="`${tool.name} 编码/解码`">
                    <a-form layout="vertical">
                        <a-form-item label="操作">
                            <a-select v-model:value="encodingForms[tool.key].operation" @change="clearResult">
                                <a-select-option value="encode">编码</a-select-option>
                                <a-select-option value="decode">解码</a-select-option>
                            </a-select>
                        </a-form-item>
                        <a-form-item :label="encodingForms[tool.key].operation === 'encode' ? '待编码文本' : `待解码文本（${tool.name}）`">
                            <a-textarea v-model:value="encodingForms[tool.key].text" :rows="6" :placeholder="encodingForms[tool.key].operation === 'encode' ? '输入需要编码的文本' : `输入需要解码的 ${tool.name} 文本`" />
                        </a-form-item>
                        <a-alert :message="tool.hint" type="info" show-icon />
                    </a-form>
                </a-tab-pane>
                <a-tab-pane key="aes" tab="AES 对称加密">
                    <a-form layout="vertical">
                        <div class="crypto-row"><a-form-item label="操作"><a-select v-model:value="aes.operation"><a-select-option value="encrypt">加密</a-select-option><a-select-option value="decrypt">解密</a-select-option></a-select></a-form-item><a-form-item label="密钥 / IV"><a-button @click="generateAesKey">生成 AES-256 密钥</a-button></a-form-item></div>
                        <a-form-item label="密钥（Base64）"><a-input v-model:value="aes.keyBase64" /></a-form-item>
                        <a-form-item label="IV（Base64）"><a-input v-model:value="aes.ivBase64" /></a-form-item>
                        <a-form-item :label="aes.operation === 'encrypt' ? '明文' : '密文（Base64）'"><a-textarea v-model:value="aes.text" :rows="5" /></a-form-item>
                    </a-form>
                </a-tab-pane>
                <a-tab-pane key="rsa" tab="RSA 非对称加密">
                    <a-form layout="vertical">
                        <div class="crypto-row"><a-form-item label="操作"><a-select v-model:value="rsa.operation" @change="syncRsaKey"><a-select-option value="encrypt">公钥加密</a-select-option><a-select-option value="decrypt">私钥解密</a-select-option></a-select></a-form-item><a-form-item label="密钥"><a-button @click="generateRsaKey">生成 RSA-2048 密钥对</a-button></a-form-item></div>
                        <a-form-item label="当前密钥（PEM）"><a-textarea v-model:value="rsa.keyPem" :rows="5" /></a-form-item>
                        <a-form-item :label="rsa.operation === 'encrypt' ? '明文' : '密文（Base64）'"><a-textarea v-model:value="rsa.text" :rows="4" /></a-form-item>
                        <a-alert v-if="rsa.publicKeyPem && rsa.privateKeyPem" message="密钥对已生成：加密使用公钥，解密使用私钥。" type="info" show-icon />
                    </a-form>
                </a-tab-pane>
                <a-tab-pane key="sm2" tab="SM2">
                    <a-form layout="vertical">
                        <div class="crypto-row"><a-form-item label="操作"><a-select v-model:value="sm2.operation" @change="syncSm2Key"><a-select-option value="encrypt">公钥加密</a-select-option><a-select-option value="decrypt">私钥解密</a-select-option></a-select></a-form-item><a-form-item label="密钥"><a-button @click="generateSm2Key">生成 SM2 密钥对</a-button></a-form-item></div>
                        <a-form-item label="当前密钥（十六进制）"><a-input v-model:value="sm2.keyHex" /></a-form-item>
                        <a-form-item :label="sm2.operation === 'encrypt' ? '明文' : '密文（十六进制）'"><a-textarea v-model:value="sm2.text" :rows="4" /></a-form-item>
                    </a-form>
                </a-tab-pane>
                <a-tab-pane key="sm4" tab="SM4">
                    <a-form layout="vertical">
                        <div class="crypto-row"><a-form-item label="操作"><a-select v-model:value="sm4.operation"><a-select-option value="encrypt">加密</a-select-option><a-select-option value="decrypt">解密</a-select-option></a-select></a-form-item><a-form-item label="密钥 / IV"><a-button @click="generateSm4Key">生成 SM4 密钥</a-button></a-form-item></div>
                        <a-form-item label="密钥（32 位十六进制）"><a-input v-model:value="sm4.keyHex" /></a-form-item>
                        <a-form-item label="IV（32 位十六进制）"><a-input v-model:value="sm4.ivHex" /></a-form-item>
                        <a-form-item :label="sm4.operation === 'encrypt' ? '明文' : '密文（Base64）'"><a-textarea v-model:value="sm4.text" :rows="4" /></a-form-item>
                    </a-form>
                </a-tab-pane>
            </a-tabs>

            <div class="crypto-actions"><a-button type="primary" :loading="loading" @click="submit">{{ submitLabel }}</a-button><a-button @click="clearResult">清空结果</a-button></div>
            <a-alert v-if="status" :message="status" :type="status === '处理完成。' ? 'success' : 'error'" show-icon />
            <div class="crypto-result"><div class="crypto-result-heading"><h2>结果</h2><a-button :disabled="!result" @click="copyWithFeedback(result, '结果已复制。')">复制结果</a-button></div><a-textarea :value="result" :rows="8" readonly placeholder="结果将显示在这里" /></div>
        </section>
    </section>
</template>

<style scoped>
.crypto-page { max-width: 980px; margin: 0 auto; }
.crypto-panel { padding: 24px; }
.crypto-row { display: grid; grid-template-columns: 1fr 1fr; gap: 16px; }
.crypto-actions { display: flex; gap: 8px; margin: 4px 0 18px; }
.crypto-result { margin-top: 22px; padding-top: 18px; border-top: 1px solid var(--border); }
.crypto-result-heading { display: flex; align-items: center; justify-content: space-between; margin-bottom: 10px; }
.crypto-result-heading h2 { margin: 0; font-size: 16px; }
@media (max-width: 640px) { .crypto-panel { padding: 16px; } .crypto-row { grid-template-columns: 1fr; gap: 0; } }
</style>
