import test from 'node:test';
import assert from 'node:assert/strict';
import { pathCrumbs, diskSize, detectLineEnding, normalizeNewlines, restoreNewlines, fileContentUrl } from '../src/utils/fileManager.js';

test('目录面包屑按服务器路径生成，限定根目录不泄漏上级并拒绝同名前缀', () => {
    assert.deepEqual(pathCrumbs('D:\\资料\\项目 A\\报告', [{ name: '资料', path: 'D:\\资料' }]), [
        { name: '资料', path: 'D:\\资料' }, { name: '项目 A', path: 'D:\\资料\\项目 A' }, { name: '报告', path: 'D:\\资料\\项目 A\\报告' }
    ]);
    assert.equal(pathCrumbs('d:\\DATA\\folder', [{ name: 'D', path: 'D:\\' }]).at(-1).path, 'D:\\DATA\\folder');
    assert.deepEqual(pathCrumbs('/data-other/file', [{ name: 'data', path: '/data' }]), []);
    assert.deepEqual(pathCrumbs('/var/log', [{ name: '/', path: '/' }]).map(item => item.path), ['/', '/var', '/var/log']);
    assert.equal(pathCrumbs('/data/private/file', [{ name: 'root', path: '/data' }, { name: 'private', path: '/data/private' }])[0].name, 'private');
});

test('磁盘容量支持空值、大磁盘和空文件', () => {
    assert.equal(diskSize(null), '—');
    assert.equal(diskSize(0), '0 B');
    assert.equal(diskSize(1024 ** 4 * 1.5), '1.5 TB');
});

test('textarea 的 LF 可以还原成原文件的 CRLF 或 CR，中文不发生变化', () => {
    const source = '第一行\r\n第二行\r\n';
    assert.equal(detectLineEnding(source), 'CRLF');
    assert.equal(restoreNewlines(normalizeNewlines(source), detectLineEnding(source)), source);
    assert.equal(detectLineEnding('a\rb\r'), 'CR');
    assert.equal(restoreNewlines('a\nb\n', 'CR'), 'a\rb\r');
    assert.equal(detectLineEnding('a\nb'), 'LF');
    assert.equal(detectLineEnding(''), 'LF');
});

test('预览与下载参数完整编码特殊文件名，不把文件路径当成网页地址', () => {
    const path = 'D:\\数据\\a#b&c?.txt';
    const url = new URL(fileContentUrl(path, true), 'https://workbench.test');
    assert.equal(url.pathname, '/api/files/content');
    assert.equal(url.searchParams.get('path'), path);
    assert.equal(url.searchParams.get('download'), 'true');
});
