import { watch } from 'vue';
import { createRouter, createWebHistory } from 'vue-router';
import { tools, loadTools } from '../composables/useToolCatalog.js';
import { createMenuRoutes } from '../utils/menuRoutes.js';

// 组件实现仍随构建发布，名称、地址和展示配置由数据库维护。
const modules = import.meta.glob('../views/tools/*View.vue');
export const toolComponents = Object.fromEntries(Object.entries(modules).map(([path, loader]) => [path.split('/').pop().replace('.vue', ''), loader]));

export const router = createRouter({
    history: createWebHistory(),
    scrollBehavior: () => false,
    routes: [
        { path: '/', name: 'home', component: () => import('../views/HomeView.vue'), meta: { title: '工具台' } },
        // 固定维护入口使空菜单、全部禁用时仍然可以管理菜单。
        { path: '/settings/menus', name: 'menu-settings', component: () => import('../views/MenuSettingsView.vue'), meta: { title: '菜单管理', icon: 'menu' } },
        { path: '/settings/database', name: 'database-settings', component: () => import('../views/DatabaseSettingsView.vue'), meta: { title: '数据库管理', icon: 'database' } },
        { path: '/:pathMatch(.*)*', name: 'not-found', component: () => import('../views/NotFoundView.vue'), meta: { title: '页面不存在' } }
    ]
});

let removeRoutes = [];
watch(tools, () => {
    removeRoutes.forEach(remove => remove());
    removeRoutes = createMenuRoutes(tools.value, toolComponents).map(route => router.addRoute(route));
    const current = router.currentRoute.value;
    if (current.name === 'not-found' && router.resolve(current.fullPath).name !== 'not-found')
        router.replace(current.fullPath);
});

router.beforeEach(async to => {
    // 首页和维护入口可以先呈现加载/重试界面，不等待后端目录。
    if (!to.path.startsWith('/tools/')) return;
    await loadTools();
    // 首次直接访问工具地址时，目录返回后重新匹配刚注册的路由。
    const resolved = router.resolve(to.fullPath);
    if (resolved.name !== to.name) return to.fullPath;
});
router.afterEach(to => { document.title = to.meta.title + ' · Personal Workbench'; });
