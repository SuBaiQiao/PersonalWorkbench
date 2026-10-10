import { createRouter, createWebHistory } from 'vue-router';

export const router = createRouter({
    history: createWebHistory(),
    scrollBehavior: () => ({ top: 0 }),
    routes: [
        { path: '/', component: () => import('../views/HomeView.vue'), meta: { title: '工具台' } },
        { path: '/tools/identity-generator', component: () => import('../views/tools/IdentityGeneratorView.vue'), meta: { title: '身份信息生成器' } },
        { path: '/tools/sql-converter', component: () => import('../views/tools/SqlConverterView.vue'), meta: { title: 'SQL 驼峰转换器' } },
        { path: '/tools/expense-invoice', component: () => import('../views/tools/ExpenseInvoiceView.vue'), meta: { title: '报销发票 PDF 合并' } },
        { path: '/tools/image-base64', component: () => import('../views/tools/ImageBase64View.vue'), meta: { title: '图片与 Base64 转换器' } },
        { path: '/tools/cron-generator', component: () => import('../views/tools/CronGeneratorView.vue'), meta: { title: 'Cron 表达式生成器' } },
        { path: '/tools/android-monitor', component: () => import('../views/tools/AndroidMonitorView.vue'), meta: { title: 'Android 集群设备监控' } },
        { path: '/tools/port-checker', component: () => import('../views/tools/PortCheckerView.vue'), meta: { title: '端口占用检查' } },
        { path: '/tools/collaborative-editor', component: () => import('../views/tools/CollaborativeEditorView.vue'), meta: { title: '协同文本编辑器' } },
        { path: '/tools/system-monitor', component: () => import('../views/tools/SystemMonitorView.vue'), meta: { title: '设备状态监控' } },
        { path: '/:pathMatch(.*)*', component: () => import('../views/NotFoundView.vue'), meta: { title: '页面不存在' } }
    ]
});

router.afterEach(to => {
    document.title = to.meta.title + ' · Personal Workbench';
});
