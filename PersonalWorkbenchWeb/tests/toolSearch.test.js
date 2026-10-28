import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { getToolPresentation } from '../src/utils/toolPresentation.js';
import { searchTools } from '../src/utils/toolSearch.js';

// 使用真实目录验证功能索引，防止首页、侧栏或新增目录项与搜索规则脱节。
const categories = JSON.parse(readFileSync(new URL('../../PersonalWorkbenchAPI/Data/menu-categories.json', import.meta.url), 'utf8'));
const tools = JSON.parse(readFileSync(new URL('../../PersonalWorkbenchAPI/Data/tools.json', import.meta.url), 'utf8'))
    .map(tool => ({ ...getToolPresentation(tool), categoryName: categories.find(category => category.id === tool.category)?.name }));
const routes = query => searchTools(tools, query).map(tool => tool.route);

test('加密关联到 MD5、Base64 等具体能力，并优先展示直接命中的工具', () => {
    const results = searchTools(tools, '加密');
    assert.equal(results[0].route, '/tools/crypto');
    assert.ok(results[0].matchedFeatures.includes('MD5'));
    assert.ok(results[0].matchedFeatures.includes('Base64'));
    assert.ok(results.some(tool => tool.route === '/tools/image-base64'));
    assert.ok(!results.some(tool => tool.route === '/tools/image-toolbox'));
    assert.ok(routes('加密工具').includes('/tools/image-base64'));
});

test('具体功能精确定位，不反向扩大为所有关联工具', () => {
    assert.deepEqual(routes('MD5'), ['/tools/crypto']);
    assert.deepEqual(routes('ｍｄ５'), ['/tools/crypto']);
    assert.deepEqual(routes('md-5'), ['/tools/crypto']);
    assert.ok(routes('BASE64').includes('/tools/crypto'));
    assert.ok(routes('BASE64').includes('/tools/image-base64'));
    assert.deepEqual(routes('b64'), routes('Base64'));
    assert.deepEqual(routes('RTSP'), ['/tools/video-player']);
    assert.deepEqual(routes('哈希'), ['/tools/crypto']);
});

test('多关键词取交集，中英文连写、全角及空格不妨碍搜索', () => {
    assert.deepEqual(routes('图片 Base64'), ['/tools/image-base64']);
    assert.deepEqual(routes('Base64图片'), ['/tools/image-base64']);
    assert.deepEqual(routes(' ＢＡＳＥ６４　图片 '), ['/tools/image-base64']);
    assert.deepEqual(routes('加密 md5'), ['/tools/crypto']);
    assert.deepEqual(routes('json 格式化'), ['/tools/json-toolbox']);
});

test('支持别名、分类、功能和说明，未知或特殊符号不会误匹配全部工具', () => {
    assert.ok(routes('定时').includes('/tools/cron-generator'));
    assert.ok(routes('定时').includes('/tools/timestamp'));
    assert.ok(routes('设备').includes('/tools/system-monitor'));
    assert.deepEqual(routes('uuid'), ['/tools/random-generator']);
    assert.deepEqual(routes('不存在的工具 xyz'), []);
    assert.deepEqual(routes('<script>alert(1)</script>'), []);
    assert.deepEqual(routes('---'), []);
});

test('空查询及同分保留目录顺序，不修改共享工具对象', () => {
    const before = JSON.stringify(tools);
    assert.deepEqual(routes('   '), tools.map(tool => tool.route));
    searchTools(tools, '加密');
    assert.equal(JSON.stringify(tools), before);
    const sameScore = [{ name: '甲工具', route: '/a', keywords: 'abc' }, { name: '乙工具', route: '/b', keywords: 'abc' }];
    assert.deepEqual(searchTools(sameScore, 'abc').map(tool => tool.route), ['/a', '/b']);
});
