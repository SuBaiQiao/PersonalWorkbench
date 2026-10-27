import { createRouter, createWebHistory } from 'vue-router';

export const router = createRouter({
    history: createWebHistory(),
    scrollBehavior: () => ({ top: 0 }),
    routes: [
        { path: '/', component: () => import('../views/HomeView.vue'), meta: { title: '工具台' } },
        { path: '/tools/video-player', component: () => import('../views/tools/VideoPlayerView.vue'), meta: { title: '视频播放器' } },
        { path: '/tools/identity-generator', component: () => import('../views/tools/IdentityGeneratorView.vue'), meta: { title: '身份信息生成器' } },
        { path: '/tools/sql-converter', component: () => import('../views/tools/SqlConverterView.vue'), meta: { title: 'SQL 驼峰转换器' } },
        { path: '/tools/amount-uppercase', component: () => import('../views/tools/AmountUppercaseView.vue'), meta: { title: '人民币大写转换器' } },
        { path: '/tools/expense-invoice', component: () => import('../views/tools/ExpenseInvoiceView.vue'), meta: { title: '报销发票 PDF 合并' } },
        { path: '/tools/image-base64', component: () => import('../views/tools/ImageBase64View.vue'), meta: { title: '图片与 Base64 转换器' } },
        { path: '/tools/cron-generator', component: () => import('../views/tools/CronGeneratorView.vue'), meta: { title: 'Cron 工具箱' } },
        { path: '/tools/timestamp', component: () => import('../views/tools/TimestampView.vue'), meta: { title: '时间戳工具' } },
        { path: '/tools/json-toolbox', component: () => import('../views/tools/JsonToolboxView.vue'), meta: { title: 'JSON 工具箱' } },
        { path: '/tools/text-diff', component: () => import('../views/tools/TextDiffView.vue'), meta: { title: '文本差异对比' } },
        { path: '/tools/regex-tester', component: () => import('../views/tools/RegexTesterView.vue'), meta: { title: '正则表达式测试' } },
        { path: '/tools/random-generator', component: () => import('../views/tools/RandomGeneratorView.vue'), meta: { title: 'UUID / 随机生成器' } },
        { path: '/tools/jwt-decoder', component: () => import('../views/tools/JwtDecoderView.vue'), meta: { title: 'JWT 解析' } },
        { path: '/tools/data-to-code', component: () => import('../views/tools/DataToCodeView.vue'), meta: { title: '数据转代码' } },
        { path: '/tools/http-client', component: () => import('../views/tools/HttpClientView.vue'), meta: { title: 'HTTP 请求工具' } },
        { path: '/tools/image-toolbox', component: () => import('../views/tools/ImageToolboxView.vue'), meta: { title: '图片工具箱' } },
        { path: '/tools/android-monitor', component: () => import('../views/tools/AndroidMonitorView.vue'), meta: { title: 'Android 集群设备监控' } },
        { path: '/tools/port-checker', component: () => import('../views/tools/PortCheckerView.vue'), meta: { title: '端口占用检查' } },
        { path: '/tools/collaborative-editor', component: () => import('../views/tools/CollaborativeEditorView.vue'), meta: { title: '协同文本编辑器' } },
        { path: '/tools/system-monitor', component: () => import('../views/tools/SystemMonitorView.vue'), meta: { title: '设备状态监控' } },
        { path: '/tools/delta-cards', component: () => import('../views/tools/DeltaCardsView.vue'), meta: { title: '三角洲卡牌收集' } },
        { path: '/tools/crypto', component: () => import('../views/tools/CryptoView.vue'), meta: { title: '加密工具箱' } },
        { path: '/tools/code-generator', component: () => import('../views/tools/CodeGeneratorView.vue'), meta: { title: '二维码与条形码生成器' } },
        { path: '/:pathMatch(.*)*', component: () => import('../views/NotFoundView.vue'), meta: { title: '页面不存在' } }
    ]
});

router.afterEach(to => {
    document.title = to.meta.title + ' · Personal Workbench';
});
