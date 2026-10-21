import { computed, ref, watch } from 'vue';
import { theme } from 'ant-design-vue';

function storedAppearance() {
    try { return localStorage.getItem('workbench-appearance') === 'dark'; }
    catch { return false; }
}

export const isDark = ref(storedAppearance());
export function toggleAppearance() { isDark.value = !isDark.value; }

watch(isDark, dark => {
    document.documentElement.dataset.theme = dark ? 'dark' : 'light';
    try { localStorage.setItem('workbench-appearance', dark ? 'dark' : 'light'); }
    catch { /* Appearance still works when browser storage is unavailable. */ }
}, { immediate: true });

export const workbenchTheme = computed(() => ({
    algorithm: isDark.value ? theme.darkAlgorithm : theme.defaultAlgorithm,
    token: {
        colorPrimary: isDark.value ? '#7caaff' : '#2563eb',
        colorInfo: isDark.value ? '#7caaff' : '#2563eb',
        colorSuccess: isDark.value ? '#62d8ae' : '#16805f',
        colorWarning: isDark.value ? '#f6c47b' : '#a9630c',
        colorError: isDark.value ? '#ff969c' : '#c73c4a',
        colorInfoBg: isDark.value ? '#1b3152' : '#edf4ff',
        colorInfoBorder: isDark.value ? '#3a5680' : '#c7dcff',
        colorSuccessBg: isDark.value ? '#173a34' : '#eaf7f1',
        colorSuccessBorder: isDark.value ? '#32685a' : '#b9e3cf',
        colorWarningBg: isDark.value ? '#3b3328' : '#fff6e6',
        colorWarningBorder: isDark.value ? '#6d5737' : '#eed8ac',
        colorErrorBg: isDark.value ? '#3a2635' : '#fff0f1',
        colorErrorBorder: isDark.value ? '#704151' : '#efc7ce',
        colorText: isDark.value ? '#edf3ff' : '#1d2c43',
        colorTextSecondary: isDark.value ? '#aabbd3' : '#586b83',
        colorTextPlaceholder: isDark.value ? '#8b9db7' : '#718198',
        colorBgContainer: isDark.value ? '#1b2a41' : '#ffffff',
        colorBgElevated: isDark.value ? '#1b2a41' : '#f9fbff',
        colorBgLayout: 'transparent',
        colorBorder: isDark.value ? '#3c4f6b' : '#c9d5e6',
        colorSplit: isDark.value ? '#34445c' : '#dce4ef',
        borderRadius: 14, borderRadiusLG: 22,
        controlHeight: 44, controlHeightLG: 50, controlHeightSM: 32,
        fontFamily: '-apple-system, BlinkMacSystemFont, "Segoe UI", "PingFang SC", "Microsoft YaHei", sans-serif',
        fontSize: 14, boxShadow: '0 12px 40px rgba(24, 51, 96, 0.12)', wireframe: false
    },
    components: {
        Button: { fontWeight: 600, primaryShadow: '0 4px 12px rgba(37, 99, 235, .2)' },
        Table: { headerBg: isDark.value ? '#22344d' : '#eff4fb', cellPaddingBlock: 16 },
        Tabs: { horizontalItemGutter: 22, titleFontSize: 14 },
        Modal: { borderRadiusLG: 28 }
    }
}));
