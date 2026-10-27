import { computed, ref, watch } from 'vue';
import { theme } from 'ant-design-vue';

/**
 * 读取浏览器保存的配色偏好，未设置或存储不可用时使用浅色。
 * localStorage 在隐私模式或受限环境中可能抛错，读取失败不应阻止页面启动。
 * @returns {boolean} 是否启用深色外观。
 */
function storedAppearance() {
    try { return localStorage.getItem('workbench-appearance') === 'dark'; }
    catch { return false; }
}

// 配色、菜单布局与侧栏收起状态相互独立，切换其中一项不会重置其他偏好。
export const isDark = ref(storedAppearance());
/** 切换浅色 / 深色外观，由下方监听器同步 CSS 和本地偏好。 */
export function toggleAppearance() { isDark.value = !isDark.value; }

/**
 * 恢复菜单主题，仅接受已支持的值；旧值、未知值及读取异常统一回退到 App 式菜单。
 * @returns {'app' | 'sidebar'} 菜单布局标识。
 */
function storedMenuTheme() {
    try { return localStorage.getItem('workbench-menu-theme') === 'sidebar' ? 'sidebar' : 'app'; }
    catch { return 'app'; }
}

export const menuTheme = ref(storedMenuTheme());
/**
 * 设置菜单主题，集中校验入口，避免无效值导致布局与选中状态不一致。
 * @param {'app' | 'sidebar'} value App 式菜单或左侧纵向菜单。
 */
export function setMenuTheme(value) {
    if (value === 'app' || value === 'sidebar') menuTheme.value = value;
}

/** 读取桌面侧栏的收起偏好；只将字符串 true 视为收起，默认保持展开。 */
function storedSidebarCollapsed() {
    try { return localStorage.getItem('workbench-sidebar-collapsed') === 'true'; }
    catch { return false; }
}

export const sidebarCollapsed = ref(storedSidebarCollapsed());

// immediate 保证初次加载就同步主题标记；持久化失败时仍保留本次会话中的切换结果。
watch(menuTheme, value => {
    document.documentElement.dataset.menuTheme = value;
    try { localStorage.setItem('workbench-menu-theme', value); }
    catch { /* 浏览器禁止写入存储时，菜单主题仍可在当前页面即时切换。 */ }
}, { immediate: true });

// 收起偏好仅用于桌面图标栏，手机抽屉是否打开由布局组件单独管理，不写入本地存储。
watch(sidebarCollapsed, value => {
    try { localStorage.setItem('workbench-sidebar-collapsed', String(value)); }
    catch { /* 存储失败只影响下次启动时的恢复，不影响当前侧栏操作。 */ }
});

// 原生页面样式读取 data-theme，Ant Design Vue 组件读取下方计算主题，两者由同一状态驱动。
watch(isDark, dark => {
    document.documentElement.dataset.theme = dark ? 'dark' : 'light';
    try { localStorage.setItem('workbench-appearance', dark ? 'dark' : 'light'); }
    catch { /* 外观切换不依赖存储写入成功，受限浏览器也可以正常使用。 */ }
}, { immediate: true });

/**
 * Ant Design Vue 的响应式主题配置，由 App.vue 的 ConfigProvider 统一下发。
 * 与 glass.css 中的语义色保持一致，避免表单、弹层与页面背景在切换外观后产生割裂。
 */
export const workbenchTheme = computed(() => ({
    algorithm: isDark.value ? theme.darkAlgorithm : theme.defaultAlgorithm,
    token: {
        // 主色及提示语义色分别配置明暗版本，深色模式下提高前景亮度以保证可读性。
        colorPrimary: isDark.value ? '#7caaff' : '#2563eb',
        colorInfo: isDark.value ? '#7caaff' : '#2563eb',
        colorSuccess: isDark.value ? '#62d8ae' : '#16805f',
        colorWarning: isDark.value ? '#f6c47b' : '#a9630c',
        colorError: isDark.value ? '#ff969c' : '#c73c4a',
        // 提示框的背景、边框与文字配套设置，避免只切文字而沿用浅色底。
        colorInfoBg: isDark.value ? '#1b3152' : '#edf4ff',
        colorInfoBorder: isDark.value ? '#3a5680' : '#c7dcff',
        colorSuccessBg: isDark.value ? '#173a34' : '#eaf7f1',
        colorSuccessBorder: isDark.value ? '#32685a' : '#b9e3cf',
        colorWarningBg: isDark.value ? '#3b3328' : '#fff6e6',
        colorWarningBorder: isDark.value ? '#6d5737' : '#eed8ac',
        colorErrorBg: isDark.value ? '#3a2635' : '#fff0f1',
        colorErrorBorder: isDark.value ? '#704151' : '#efc7ce',
        // 文本、容器与分隔线覆盖输入框、菜单、下拉弹层等共用的基础视觉层。
        colorText: isDark.value ? '#edf3ff' : '#1d2c43',
        colorTextSecondary: isDark.value ? '#aabbd3' : '#586b83',
        colorTextPlaceholder: isDark.value ? '#8b9db7' : '#718198',
        colorBgContainer: isDark.value ? '#1b2a41' : '#ffffff',
        colorBgElevated: isDark.value ? '#1b2a41' : '#f9fbff',
        colorBgLayout: 'transparent',
        colorBorder: isDark.value ? '#3c4f6b' : '#c9d5e6',
        colorSplit: isDark.value ? '#34445c' : '#dce4ef',
        // 统一控件尺寸与圆角；默认高度保留足够的鼠标及触摸操作区域。
        borderRadius: 14, borderRadiusLG: 22,
        controlHeight: 44, controlHeightLG: 50, controlHeightSM: 32,
        fontFamily: '-apple-system, BlinkMacSystemFont, "Segoe UI", "PingFang SC", "Microsoft YaHei", sans-serif',
        fontSize: 14, boxShadow: '0 12px 40px rgba(24, 51, 96, 0.12)', wireframe: false
    },
    components: {
        // 组件专属覆盖补充全局令牌未直接表达的阴影、表格密度和标签间距。
        Button: { fontWeight: 600, primaryShadow: '0 4px 12px rgba(37, 99, 235, .2)' },
        Table: { headerBg: isDark.value ? '#22344d' : '#eff4fb', cellPaddingBlock: 16 },
        Tabs: { horizontalItemGutter: 22, titleFontSize: 14 },
        Modal: { borderRadiusLG: 28 }
    }
}));
