<script setup>
import { computed, onBeforeUnmount, ref, shallowRef, watch } from 'vue';
import ToolIntro from '../../components/ToolIntro.vue';
import { validateImageTransform, extractPalette } from '../../utils/imageTool.js';
import { copyWithFeedback } from '../../utils/clipboard.js';
const image = shallowRef(null), source = ref(''), fileName = ref(''), originalBytes = ref(0), sourceWidth = ref(0), sourceHeight = ref(0);
const x = ref(0), y = ref(0), cropWidth = ref(1), cropHeight = ref(1), width = ref(1), height = ref(1), locked = ref(true);
const format = ref('image/webp'), quality = ref(85), background = ref('#ffffff'), result = shallowRef(null), error = ref(''), busy = ref(false);
let version = 0;
const palette = ref([]);
const downloadName = computed(() => `${fileName.value.replace(/\.[^.]+$/,'') || 'image'}-edited.${{ 'image/png':'png','image/jpeg':'jpg','image/webp':'webp' }[format.value]}`);
function resetResult() { version++; if (result.value) URL.revokeObjectURL(result.value.url); result.value = null; error.value = ''; busy.value = false; }
watch([x,y,cropWidth,cropHeight], () => { width.value = cropWidth.value; height.value = cropHeight.value; });
watch([x,y,cropWidth,cropHeight,width,height,format,quality,background], resetResult);
function resize(axis, value) {
    if (!locked.value || !value || !cropWidth.value || !cropHeight.value) return;
    if (axis === 'width') height.value = Math.max(1,Math.round(value * cropHeight.value / cropWidth.value));
    else width.value = Math.max(1,Math.round(value * cropWidth.value / cropHeight.value));
}
async function load(event) {
    const file = event.target.files?.[0]; event.target.value = '';
    if (!file) return;
    resetResult(); image.value = null; palette.value = [];
    if (source.value) URL.revokeObjectURL(source.value); source.value = '';
    if (!['image/png','image/jpeg','image/webp','image/gif','image/bmp'].includes(file.type)) { error.value = '请选择 PNG、JPEG、WebP、GIF 或 BMP 图片。'; return; }
    if (file.size > 20_000_000) { error.value = '单张图片请控制在 20 MB 以内。'; return; }
    const ticket = ++version, objectUrl = URL.createObjectURL(file);
    try {
        const loaded = new Image();
        await new Promise((resolve,reject) => { loaded.onload = resolve; loaded.onerror = () => reject(new Error('图片无法解码，请更换文件。')); loaded.src = objectUrl; });
        if (ticket !== version) { URL.revokeObjectURL(objectUrl); return; }
        if (loaded.naturalWidth * loaded.naturalHeight > 40_000_000) throw new Error('原图像素不能超过 4000 万。');
        image.value = loaded; source.value = objectUrl; fileName.value = file.name; originalBytes.value = file.size;
        sourceWidth.value = loaded.naturalWidth; sourceHeight.value = loaded.naturalHeight;
        x.value = y.value = 0; cropWidth.value = width.value = loaded.naturalWidth; cropHeight.value = height.value = loaded.naturalHeight;
        palette.value = extractPalette(loaded);
    } catch (e) { URL.revokeObjectURL(objectUrl); error.value = e.message; }
}
async function process() {
    resetResult(); if (!image.value) { error.value = '请先选择一张图片。'; return; }
    const ticket = version; busy.value = true;
    try {
        const params = {sourceWidth:sourceWidth.value,sourceHeight:sourceHeight.value,x:x.value,y:y.value,cropWidth:cropWidth.value,cropHeight:cropHeight.value,width:width.value,height:height.value};
        validateImageTransform(params);
        const canvas = document.createElement('canvas'); canvas.width = width.value; canvas.height = height.value;
        const context = canvas.getContext('2d');
        if (format.value === 'image/jpeg') { context.fillStyle = background.value; context.fillRect(0,0,canvas.width,canvas.height); }
        context.drawImage(image.value,x.value,y.value,cropWidth.value,cropHeight.value,0,0,width.value,height.value);
        palette.value = extractPalette(canvas);
        const blob = await new Promise(resolve=>canvas.toBlob(resolve,format.value,quality.value/100));
        if (ticket !== version) return;
        if (!blob) throw new Error('图片编码失败，请减少输出尺寸。');
        if (blob.type !== format.value) throw new Error('当前浏览器不支持该输出格式，请选择 PNG 或 JPEG。');
        result.value = { url:URL.createObjectURL(blob), bytes:blob.size };
    } catch (e) { if (ticket === version) error.value = e.message; }
    finally { if (ticket === version) busy.value = false; }
}
function fullCrop() { x.value = y.value = 0; cropWidth.value = sourceWidth.value; cropHeight.value = sourceHeight.value; width.value = sourceWidth.value; height.value = sourceHeight.value; }
onBeforeUnmount(() => { version++; if (source.value) URL.revokeObjectURL(source.value); if (result.value) URL.revokeObjectURL(result.value.url); });
</script>
<template><section><ToolIntro title="图片工具箱" eyebrow="FILES / IMAGE STUDIO" description="裁剪、缩放、转换与压缩，在本地完成一张图片的日常处理。" /><section class="panel utility-panel"><label class="utility-field">选择图片<input class="utility-file" type="file" accept="image/png,image/jpeg,image/webp,image/gif,image/bmp" aria-label="选择待处理图片" @change="load" /></label><p class="utility-note">支持 PNG、JPEG、WebP、GIF、BMP，最大 20 MB / 4000 万像素。动图导出为静态图片；输出不保留原文件元数据。</p></section><a-alert v-if="error" class="utility-error" :message="error" type="error" role="alert" /><div v-if="image" class="utility-grid utility-gap"><section class="panel utility-panel utility-stack"><div class="panel-heading"><h2>处理设置</h2><span class="utility-status">原图 {{ sourceWidth }} × {{ sourceHeight }}</span></div><div class="utility-controls"><label class="utility-field">裁剪 X<a-input-number v-model:value="x" :min="0" :precision="0" aria-label="裁剪 X" /></label><label class="utility-field">裁剪 Y<a-input-number v-model:value="y" :min="0" :precision="0" aria-label="裁剪 Y" /></label></div><div class="utility-controls"><label class="utility-field">裁剪宽度<a-input-number v-model:value="cropWidth" :min="1" :precision="0" aria-label="裁剪宽度" /></label><label class="utility-field">裁剪高度<a-input-number v-model:value="cropHeight" :min="1" :precision="0" aria-label="裁剪高度" /></label></div><div class="utility-toolbar"><a-button @click="fullCrop">恢复整张原图</a-button><span class="utility-note">坐标从左上角开始，单位为像素。</span></div><div class="utility-controls"><label class="utility-field">输出宽度<a-input-number v-model:value="width" :min="1" :max="8192" :precision="0" aria-label="输出宽度" @change="resize('width',$event)" /></label><label class="utility-field">输出高度<a-input-number v-model:value="height" :min="1" :max="8192" :precision="0" aria-label="输出高度" @change="resize('height',$event)" /></label></div><a-checkbox v-model:checked="locked">保持裁剪区域的宽高比</a-checkbox><div class="utility-controls"><label class="utility-field">输出格式<a-select v-model:value="format" aria-label="图片输出格式" :options="[{value:'image/webp',label:'WebP'},{value:'image/jpeg',label:'JPEG'},{value:'image/png',label:'PNG'}]" /></label><label v-if="format === 'image/jpeg'" class="utility-field">透明区域背景<input v-model="background" type="color" aria-label="JPEG 背景色" /></label></div><label v-if="format !== 'image/png'" class="utility-field">输出质量 {{ quality }}%<a-slider v-model:value="quality" :min="10" :max="100" aria-label="图片输出质量" /></label><p v-else class="utility-note">PNG 使用无损编码；缩小尺寸通常能减少体积。</p><a-button type="primary" :loading="busy" @click="process">处理并生成预览</a-button></section><section class="panel utility-panel"><div class="panel-heading"><h2>{{ result ? '处理后预览' : '原图预览' }}</h2><a v-if="result" class="button button-primary" :href="result.url" :download="downloadName">下载图片</a></div><div class="image-preview"><img :src="result?.url || source" alt="图片处理预览" /></div><p class="utility-note">{{ fileName }} · 原图 {{ (originalBytes/1024).toFixed(1) }} KB</p><p v-if="result" class="utility-note">输出 {{ width }} × {{ height }} · {{ (result.bytes/1024).toFixed(1) }} KB · 相比原图 {{ result.bytes <= originalBytes ? '减少' : '增加' }} {{ Math.abs((1-result.bytes/originalBytes)*100).toFixed(1) }}%</p><h2 class="utility-gap">主要颜色</h2><div class="palette utility-gap"><button v-for="color in palette" :key="color" @click="copyWithFeedback(color, '颜色已复制。')" :aria-label="`复制颜色 ${color}`"><span :style="{background:color}"></span><code>{{ color }}</code></button></div><p v-if="!palette.length" class="utility-note">未检测到足够的不透明像素。</p></section></div></section></template>
<style scoped>
.image-preview { display: grid; place-items: center; border: 1px solid var(--border); border-radius: 16px; padding: 12px; min-height: 200px; background: var(--well); }.image-preview img { max-width: 100%; max-height: 450px; object-fit: contain; }.palette { display: flex; flex-wrap: wrap; gap: 12px; }.palette button { padding: 0 0 6px; border: 1px solid var(--border); border-radius: 12px; overflow: hidden; color: var(--text); background: var(--surface-raised); font-size: 11px; }.palette span { display: block; height: 44px; width: 80px; margin-bottom: 5px; }
</style>
