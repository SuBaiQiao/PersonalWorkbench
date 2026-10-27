import test from 'node:test';
import assert from 'node:assert/strict';
import { detectMediaType, validateMediaUrl, mediaDisplayName, indexLocalMedia, prepareLocalHls, formatMediaTime, mediaHlsConfig, liveRecoveryAction } from '../src/utils/mediaPlayback.js';

function file(name, text, relativePath = '') {
    const value = new File([text], name);
    Object.defineProperty(value, 'webkitRelativePath', { value: relativePath });
    return value;
}

// 对象 URL 适配器用于检查改写后的清单与释放行为，不依赖浏览器播放器实现。
function urls() {
    const objects = new Map(), revoked = [];
    return { objects, revoked, createObjectURL: blob => { const key = `blob:test-${objects.size}`; objects.set(key, blob); return key; }, revokeObjectURL: key => revoked.push(key) };
}

test('视频协议与后缀识别兼容签名参数，网络输入拒绝任意脚本和文件路径', () => {
    for (const value of ['rtsp://camera/live', 'rtmp://server/live', 'rtmps://server/live']) assert.equal(detectMediaType(value), 'relay');
    assert.equal(detectMediaType('https://server/play.M3U8?token=xyz#t'), 'hls');
    assert.equal(detectMediaType('sample.flv'), 'flv');
    assert.equal(detectMediaType('sample.ts'), 'mpegts');
    assert.equal(detectMediaType('sample.mp4'), 'native');
    assert.equal(validateMediaUrl(' https://server/video.mp4 '), 'https://server/video.mp4');
    for (const value of ['javascript:alert(1)', 'file:///C:/test.mp4', 'data:video/mp4;base64,eA==', 'C:\\test.mp4', 'ftp://server/a', '']) assert.throws(() => validateMediaUrl(value));
    assert.equal(mediaDisplayName('rtsp://user:secret@camera:554/live?token=secret'), 'rtsp://camera:554/live');
});

test('本地 HLS 递归改写变体、音轨、初始化段与密钥，并保持清单标签', async () => {
    const adapter = urls();
    const { entries } = indexLocalMedia([
        file('master.m3u8', '#EXTM3U\n#EXT-X-MEDIA:TYPE=AUDIO,URI="audio/list.m3u8"\n#EXT-X-STREAM-INF:BANDWIDTH=100\nvideo/list.m3u8', 'root/master.m3u8'),
        file('list.m3u8', '#EXTM3U\n#EXT-X-TARGETDURATION:2\n#EXT-X-KEY:METHOD=AES-128,URI="../key.key"\n#EXT-X-MAP:URI="init.mp4"\n#EXTINF:2,\nseg%20one.ts?x=1\n#EXT-X-ENDLIST', 'root/video/list.m3u8'),
        file('list.m3u8', '#EXTM3U\n#EXTINF:2,\nseg.aac\n#EXT-X-ENDLIST', 'root/audio/list.m3u8'),
        file('key.key', '0123456789abcdef', 'root/key.key'), file('init.mp4', 'init', 'root/video/init.mp4'),
        file('seg one.ts', 'video', 'root/video/seg one.ts'), file('seg.aac', 'audio', 'root/audio/seg.aac')
    ]);
    const result = await prepareLocalHls(entries, 'root/master.m3u8', adapter);
    assert.equal(adapter.objects.size, 7);
    const master = await adapter.objects.get(result.url).text();
    assert.match(master, /URI="blob:test-\d+"/);
    assert.match(master, /#EXT-X-STREAM-INF:BANDWIDTH=100/);
    const texts = await Promise.all([...adapter.objects.values()].map(blob => blob.text()));
    assert.ok(texts.some(text => text.includes('#EXT-X-KEY:METHOD=AES-128,URI="blob:')));
    assert.ok(texts.some(text => text.includes('#EXT-X-MAP:URI="blob:')));
    result.dispose(); result.dispose();
    assert.equal(adapter.revoked.length, 7);
});

test('独立本地清单可以引用在线分片；缺失文件、循环引用和路径越界会回收已分配资源', async () => {
    const online = indexLocalMedia([file('index.m3u8', '#EXTM3U\n#EXTINF:2,\nhttps://media.example/seg.ts\n#EXT-X-ENDLIST')]);
    const adapter = urls();
    const result = await prepareLocalHls(online.entries, 'index.m3u8', adapter);
    assert.match(await adapter.objects.get(result.url).text(), /https:\/\/media.example\/seg.ts/);
    result.dispose();
    for (const [text, expected] of [
        ['#EXTM3U\n#EXTINF:2,\nseg.ts\nmissing.ts', /缺少本地资源/],
        ['#EXTM3U\nindex.m3u8', /循环引用/],
        ['#EXTM3U\n../../outside.ts', /超出所选文件夹/],
        ['not a manifest', /不是有效/],
        ['#EXTM3U\nfile:///C:/secret.ts', /无法直接访问/]
    ]) {
        const resources = urls();
        const index = indexLocalMedia([file('index.m3u8', text), file('seg.ts', 'data')]);
        await assert.rejects(prepareLocalHls(index.entries, 'index.m3u8', resources), expected);
        assert.equal(resources.revoked.length, resources.objects.size);
    }
    assert.throws(() => indexLocalMedia([file('same.mp4', 'a'), file('same.mp4', 'b')]), /重复路径/);
});

test('本地清单支持根路径后缀匹配，时间显示处理直播与小时', async () => {
    const adapter = urls();
    const index = indexLocalMedia([file('index.m3u8', '#EXTM3U\n/segments/1.ts', 'root/index.m3u8'), file('1.ts', 'data', 'root/segments/1.ts')]);
    const result = await prepareLocalHls(index.entries, 'root/index.m3u8', adapter);
    assert.match(await adapter.objects.get(result.url).text(), /blob:test-/);
    result.dispose();
    assert.equal(formatMediaTime(65), '1:05');
    assert.equal(formatMediaTime(3601), '1:00:01');
    assert.equal(formatMediaTime(Infinity), '直播');
});

test('直播使用短缓冲和有限追帧距离，文件播放仍保留普通缓冲', () => {
    const live = mediaHlsConfig(true), file = mediaHlsConfig(false);
    assert.ok(live.maxBufferLength < file.maxBufferLength);
    assert.ok(Number.isFinite(live.liveMaxLatencyDurationCount));
    assert.ok(live.liveMaxLatencyDurationCount > live.liveSyncDurationCount);
    assert.ok(live.maxLiveSyncPlaybackRate > 1 && live.maxLiveSyncPlaybackRate <= 1.1);
    assert.equal(live.liveSyncOnStallIncrease, 0);
    assert.equal(file.maxBufferLength, 30);
    assert.equal(mediaHlsConfig(true, false).liveMaxLatencyDurationCount, Infinity);
    assert.equal(mediaHlsConfig(true, false).maxLiveSyncPlaybackRate, 1);
});

test('直播落后、停顿可恢复，暂停、手动回看和冷却期间不抢占播放位置', () => {
    const state = { live: true, follow: true, paused: false, seeking: false, current: 10, target: 20, stalledMs: 0, sinceRecoveryMs: 10000 };
    assert.equal(liveRecoveryAction(state), 'seek');
    assert.equal(liveRecoveryAction({ ...state, target: 11, stalledMs: 4500 }), 'seek');
    assert.equal(liveRecoveryAction({ ...state, target: 10.1, stalledMs: 6500 }), 'reload');
    assert.equal(liveRecoveryAction({ ...state, target: 11 }), null);
    for (const change of [{ paused: true }, { follow: false }, { live: false }, { seeking: true }, { target: null }, { sinceRecoveryMs: 1000 }])
        assert.equal(liveRecoveryAction({ ...state, ...change }), null);
});
