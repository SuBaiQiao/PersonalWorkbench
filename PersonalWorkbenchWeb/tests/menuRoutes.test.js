import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { createMenuRoutes } from '../src/utils/menuRoutes.js';
import { searchTools } from '../src/utils/toolSearch.js';

test('数据库决定菜单路径、标题和组件，禁用及未知组件不注册', () => {
    const component = () => {};
    const menus = [
        { id: 1, route: '/tools/new-name', componentKey: 'DemoView', name: '数据库标题', icon: 'clock', isEnabled: true },
        { id: 2, route: '/tools/disabled', componentKey: 'DemoView', isEnabled: false },
        { id: 3, route: '/tools/unknown', componentKey: 'UnknownView' },
        { id: 4, route: '//example.com', componentKey: 'DemoView' },
        { id: 5, route: '/settings/menus', componentKey: 'DemoView' }
    ];
    const routes = createMenuRoutes(menus, { DemoView: component });
    assert.equal(routes.length, 1);
    assert.equal(routes[0].path, '/tools/new-name');
    assert.equal(routes[0].meta.title, '数据库标题');
    assert.equal(routes[0].component, component);
});

test('所有初始化菜单均对应实际存在的 Vue 组件', () => {
    const menus = JSON.parse(readFileSync(new URL('../../PersonalWorkbenchAPI/Data/tools.json', import.meta.url), 'utf8'));
    for (const menu of menus) {
        assert.ok(readFileSync(new URL('../src/views/tools/' + menu.componentKey + '.vue', import.meta.url), 'utf8').length > 0);
    }
});

test('自定义分类、功能、关键词从响应生效，不依赖内置路由映射', () => {
    const menus = [{ name: '新工具', route: '/tools/custom', categoryName: '自定义工作区', features: ['可维护功能'], keywords: '别名' }];
    for (const query of ['自定义工作区', '可维护功能', '别名']) assert.equal(searchTools(menus, query).length, 1);
    assert.equal(searchTools([{ ...menus[0], features: [] }], '可维护功能').length, 0);
});
