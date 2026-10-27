import { computed, onMounted, ref } from 'vue';
import { api, getErrorMessage } from '../api/client.js';
import { getToolPresentation } from '../utils/toolPresentation.js';

/**
 * 工具目录的共享状态。
 * 状态放在模块作用域，使首页、桌面侧栏和手机抽屉共用同一份后端目录与请求结果；
 * 若放在 useToolCatalog 内部，每个组件实例都会单独加载，菜单与首页也可能不同步。
 */
const tools = ref([]);
const loading = ref(true);
const errorMessage = ref('');
// 后端决定可用工具及顺序，前端补充分类、图标、简介和搜索关键词。
const presentedTools = computed(() => tools.value.map(tool => ({ ...tool, ...getToolPresentation(tool.route) })));
// loaded 表示至少成功加载过一次；pendingRequest 用于合并尚未完成的并发请求。
let loaded = false;
let pendingRequest;

/**
 * 加载工具目录；已有成功缓存时直接复用，显式刷新时才重新请求。
 * @param {boolean} force 是否跳过成功缓存，用于用户主动重试或刷新。
 * @returns {Promise<void>} 请求完成后兑现；失败信息通过共享 errorMessage 提供给界面。
 */
async function loadTools(force = false) {
    // 多个组件同时挂载时共享同一个 Promise，避免重复请求及加载状态相互覆盖。
    if (pendingRequest) return pendingRequest;
    if (loaded && !force) return;
    loading.value = true;
    errorMessage.value = '';
    pendingRequest = (async () => {
        try {
            const response = await api.get('/api/tools');
            tools.value = response.data;
            loaded = true;
        } catch (error) {
            // 保留上一次的目录数据，失败时由各入口展示统一错误并提供重试。
            errorMessage.value = await getErrorMessage(error, '工具目录加载失败。');
        } finally {
            loading.value = false;
            // 无论成功失败都释放请求锁，后续刷新或失败重试才能真正发起请求。
            pendingRequest = undefined;
        }
    })();
    return pendingRequest;
}

/**
 * 在组件挂载时按需准备目录，返回供界面直接绑定的响应式状态。
 * 暴露的 loadTools 是用户操作入口，始终强制刷新；挂载时的内部调用则优先复用缓存。
 */
export function useToolCatalog() {
    onMounted(() => loadTools());
    return { tools, presentedTools, loading, errorMessage, loadTools: () => loadTools(true) };
}
