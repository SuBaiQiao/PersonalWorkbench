<script setup>
import { computed, onBeforeUnmount, reactive, ref, watch } from 'vue';
import QRCode from 'qrcode';
import JsBarcode from 'jsbarcode';

const form = reactive({
    type: 'qr', content: '', qrSize: 384, errorCorrection: 'M',
    barcodeFormat: 'CODE128', barWidth: 2, barHeight: 100, displayText: true
});
const generating = ref(false);
const errorMessage = ref('');
const output = ref(null);
let generationId = 0;

const isQr = computed(() => form.type === 'qr');
const codeName = computed(() => isQr.value ? '二维码' : '条形码');
const contentHint = computed(() => {
    if (isQr.value) return '支持中文、网址、数字和表情符号；内容会原样写入二维码。';
    if (form.barcodeFormat === 'EAN13') return '输入 12 位数字可自动补全校验位；输入 13 位数字时会校验最后一位。';
    if (form.barcodeFormat === 'CODE39') return '支持大写字母、数字、空格及 - . $ / + %，最多 80 个字符。';
    return '支持英文字母、数字、空格及 ASCII 符号，最多 80 个字符。中文请使用二维码。';
});

function clearResult() {
    generationId++;
    output.value = null;
    errorMessage.value = '';
    generating.value = false;
}

watch(form, clearResult, { flush: 'sync' });
onBeforeUnmount(() => { generationId++; });

function validateContent() {
    if (!form.content.trim()) throw new Error('请输入需要生成的内容。');
    if (isQr.value) return;
    if (form.barcodeFormat === 'EAN13') {
        if (!/^\d{12,13}$/.test(form.content)) throw new Error('EAN-13 需要 12 位或 13 位数字。');
    } else {
        if (form.content.length > 80) throw new Error('条形码内容最多支持 80 个字符。');
        if (form.barcodeFormat === 'CODE39' && !/^[A-Z0-9 .$/+%\-]+$/.test(form.content)) {
            throw new Error('CODE39 仅支持大写字母、数字、空格及 - . $ / + %。');
        }
        if (form.barcodeFormat === 'CODE128' && !/^[\x20-\x7E]+$/.test(form.content)) {
            throw new Error('CODE128 仅支持英文、数字和 ASCII 符号。中文或表情符号请使用二维码。');
        }
    }
}

async function generate() {
    clearResult();
    const currentGeneration = generationId;
    generating.value = true;
    try {
        validateContent();
        const canvas = document.createElement('canvas');
        if (isQr.value) {
            try {
                await QRCode.toCanvas(canvas, form.content, {
                    width: form.qrSize, margin: 4, errorCorrectionLevel: form.errorCorrection,
                    color: { dark: '#000000ff', light: '#ffffffff' }
                });
            } catch {
                throw new Error('二维码生成失败，内容可能超出当前纠错等级的容量，请缩短内容后重试。');
            }
        } else {
            try {
                JsBarcode(canvas, form.content, {
                    format: form.barcodeFormat, width: form.barWidth, height: form.barHeight,
                    displayValue: form.displayText, margin: 24, fontSize: 18,
                    background: '#ffffff', lineColor: '#000000'
                });
            } catch {
                throw new Error(form.barcodeFormat === 'EAN13'
                    ? 'EAN-13 校验位不正确，请检查数字，或输入前 12 位自动生成校验位。'
                    : '条形码生成失败，请检查内容是否符合所选格式。');
            }
        }
        if (currentGeneration !== generationId) return;
        output.value = {
            dataUrl: canvas.toDataURL('image/png'), width: canvas.width, height: canvas.height,
            fileName: isQr.value ? 'qr-code.png' : `barcode-${form.barcodeFormat.toLowerCase()}.png`
        };
    } catch (error) {
        if (currentGeneration === generationId) errorMessage.value = error.message || '生成失败，请重试。';
    } finally {
        if (currentGeneration === generationId) generating.value = false;
    }
}

function clearContent() {
    form.content = '';
    clearResult();
}

function download() {
    if (!output.value) return;
    const link = document.createElement('a');
    link.href = output.value.dataUrl;
    link.download = output.value.fileName;
    document.body.appendChild(link);
    link.click();
    link.remove();
}
</script>

<template>
    <section class="code-page" aria-labelledby="code-generator-title">
        <header class="page-intro">
            <p class="eyebrow">IMAGE UTILITY / QR &amp; BARCODE</p>
            <h1 id="code-generator-title">二维码与条形码生成器</h1>
            <p class="intro-copy">将文本、网址或编号生成可扫描的图片，支持预览和 PNG 下载。内容在当前浏览器内处理。</p>
        </header>

        <div class="code-grid">
            <section class="panel code-panel" aria-labelledby="code-settings-title">
                <div class="panel-heading">
                    <div><p class="panel-kicker">CONTENT &amp; SETTINGS</p><h2 id="code-settings-title">内容与设置</h2></div>
                </div>
                <a-form name="code-generator" layout="vertical" :model="form" :disabled="generating" @finish="generate">
                    <a-form-item name="type" label="生成类型">
                        <a-radio-group v-model:value="form.type" button-style="solid">
                            <a-radio-button value="qr">二维码</a-radio-button>
                            <a-radio-button value="barcode">条形码</a-radio-button>
                        </a-radio-group>
                    </a-form-item>
                    <a-form-item v-if="!isQr" name="barcodeFormat" label="条形码格式">
                        <a-select v-model:value="form.barcodeFormat">
                            <a-select-option value="CODE128">CODE128（通用编号）</a-select-option>
                            <a-select-option value="EAN13">EAN-13（商品编码）</a-select-option>
                            <a-select-option value="CODE39">CODE39（大写字母与数字）</a-select-option>
                        </a-select>
                    </a-form-item>
                    <a-form-item name="content" label="内容" :extra="contentHint">
                        <a-textarea v-model:value="form.content" :rows="5" :maxlength="isQr ? 3000 : 80" show-count :placeholder="isQr ? '输入文本或网址，例如 https://example.com' : '输入编号，例如 PW20260001'" />
                    </a-form-item>
                    <div v-if="isQr" class="code-options">
                        <a-form-item name="qrSize" label="图片尺寸">
                            <a-select v-model:value="form.qrSize">
                                <a-select-option v-for="size in [256, 384, 512, 768]" :key="size" :value="size">{{ size }} × {{ size }} px</a-select-option>
                            </a-select>
                        </a-form-item>
                        <a-form-item name="errorCorrection" label="纠错等级">
                            <a-select v-model:value="form.errorCorrection">
                                <a-select-option value="L">L（低）</a-select-option>
                                <a-select-option value="M">M（标准）</a-select-option>
                                <a-select-option value="Q">Q（较高）</a-select-option>
                                <a-select-option value="H">H（高）</a-select-option>
                            </a-select>
                        </a-form-item>
                    </div>
                    <template v-else>
                        <div class="code-options">
                            <a-form-item name="barWidth" label="条宽">
                                <a-select v-model:value="form.barWidth">
                                    <a-select-option v-for="width in [1, 2, 3]" :key="width" :value="width">{{ width }} px</a-select-option>
                                </a-select>
                            </a-form-item>
                            <a-form-item name="barHeight" label="条高">
                                <a-select v-model:value="form.barHeight">
                                    <a-select-option v-for="height in [60, 100, 140]" :key="height" :value="height">{{ height }} px</a-select-option>
                                </a-select>
                            </a-form-item>
                        </div>
                        <a-form-item><a-checkbox v-model:checked="form.displayText">在条形码下方显示内容</a-checkbox></a-form-item>
                    </template>
                    <div class="code-actions">
                        <a-button type="primary" html-type="submit" :loading="generating">生成{{ codeName }}</a-button>
                        <a-button @click="clearContent">清空</a-button>
                    </div>
                </a-form>
                <a-alert v-if="errorMessage" class="code-error" :message="errorMessage" type="error" show-icon role="alert" />
            </section>

            <section class="panel code-panel code-preview-panel" aria-labelledby="code-preview-title">
                <div class="panel-heading">
                    <div><p class="panel-kicker">PREVIEW &amp; DOWNLOAD</p><h2 id="code-preview-title">图片预览</h2></div>
                    <a-button :disabled="!output || generating" @click="download">下载 PNG</a-button>
                </div>
                <div class="code-preview" :class="{ 'has-output': output }" :aria-busy="generating">
                    <div v-if="output" class="code-image-scroll" tabindex="0" :aria-label="`${codeName}图片，可滚动查看`">
                        <img :src="output.dataUrl" :alt="`生成的${codeName}`" :class="{ 'qr-image': isQr }" :width="output.width" :height="output.height" />
                    </div>
                    <a-empty v-else :description="generating ? '正在生成图片…' : '输入内容后点击生成，即可在这里预览'" />
                </div>
                <div class="code-preview-info" role="status" aria-live="polite">
                    <template v-if="output"><strong>{{ output.width }} × {{ output.height }} px · PNG</strong><span>图片已生成，可以扫描或下载。</span></template>
                    <span v-else>黑色图案、白色背景，自动保留扫描所需的留白。</span>
                </div>
                <p v-if="output && !isQr" class="code-note">较长的条形码可左右滚动查看，下载文件保持原始尺寸。</p>
            </section>
        </div>
    </section>
</template>

<style scoped>
.code-page { max-width: 1100px; margin: 0 auto; }
.code-grid { display: grid; grid-template-columns: minmax(0, 1fr) minmax(0, 1fr); gap: 24px; align-items: start; }
.code-panel { min-width: 0; padding: 24px; }
.code-options { display: grid; grid-template-columns: 1fr 1fr; gap: 16px; }
.code-actions { display: flex; flex-wrap: wrap; gap: 8px; }
.code-error { margin-top: 18px; }
.code-preview { display: grid; align-items: center; min-height: 320px; border: 1px dashed var(--border-strong); border-radius: var(--radius); }
.code-preview.has-output { background: #fff; border-style: solid; }
.code-image-scroll { max-width: 100%; overflow-x: auto; padding: 16px; }
.code-image-scroll img { display: block; max-width: none; margin: 0 auto; }
.code-image-scroll .qr-image { max-width: 100%; height: auto; }
.code-preview-info { display: flex; flex-direction: column; gap: 4px; margin-top: 16px; color: var(--text-muted); font-size: 13px; }
.code-preview-info strong { color: var(--text); font-weight: 500; }
.code-note { margin: 12px 0 0; color: var(--text-muted); font-size: 13px; }
@media (max-width: 800px) { .code-grid { grid-template-columns: 1fr; gap: 16px; } }
@media (max-width: 480px) { .code-panel { padding: 16px; } .code-options { grid-template-columns: 1fr; gap: 16px; } }
</style>
