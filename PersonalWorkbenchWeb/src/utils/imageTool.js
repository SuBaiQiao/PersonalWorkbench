export function validateImageTransform({ sourceWidth, sourceHeight, x, y, cropWidth, cropHeight, width, height }) {
    const values = [sourceWidth,sourceHeight,x,y,cropWidth,cropHeight,width,height];
    if (values.some(value => !Number.isInteger(value))) throw new Error('尺寸与裁剪坐标需要使用整数。');
    if (x < 0 || y < 0 || cropWidth < 1 || cropHeight < 1 || x + cropWidth > sourceWidth || y + cropHeight > sourceHeight) throw new Error('裁剪范围需要位于原图内，且宽高大于 0。');
    if (width < 1 || height < 1 || width > 8192 || height > 8192 || width * height > 40_000_000) throw new Error('输出尺寸每边为 1–8192 像素，总像素不能超过 4000 万。');
}
export function extractPalette(canvas) {
    const sample = document.createElement('canvas');
    sample.width = sample.height = 64;
    const context = sample.getContext('2d', { willReadFrequently:true });
    context.drawImage(canvas,0,0,64,64);
    const pixels = context.getImageData(0,0,64,64).data, bins = new Map();
    for (let i=0;i<pixels.length;i+=4) {
        if (pixels[i+3] < 128) continue;
        const rgb = [pixels[i],pixels[i+1],pixels[i+2]], key = rgb.map(value => Math.floor(value/32)).join(',');
        const bin = bins.get(key) || { count:0, sums:[0,0,0] };
        bin.count++; rgb.forEach((value,index) => { bin.sums[index] += value; }); bins.set(key,bin);
    }
    return [...bins.values()].sort((a,b)=>b.count-a.count).slice(0,6).map(bin => '#' + bin.sums.map(sum=>Math.round(sum/bin.count).toString(16).padStart(2,'0')).join('').toUpperCase());
}
