import { computed, onMounted, ref } from 'vue';
import { api, getErrorMessage } from '../api/client.js';
import { getToolPresentation } from '../utils/toolPresentation.js';

export const tools = ref([]);
export const menuCategories = ref([]);
const loading = ref(true);
const errorMessage = ref('');
export const toolCategories = computed(() => [{ id: 'all', name: '全部工具' }, ...menuCategories.value]);
const presentedTools = computed(() => tools.value.map(tool => ({
    ...getToolPresentation(tool), categoryName: menuCategories.value.find(category => category.id === tool.category)?.name || ''
})));
let loaded = false;
let pendingRequest;

// 首页、侧栏、搜索和动态路由共享同一数据库快照，失败时可重试。
export async function loadTools(force = false) {
    if (pendingRequest) return pendingRequest;
    if (loaded && !force) return;
    loading.value = true;
    errorMessage.value = '';
    pendingRequest = (async () => {
        try {
            const { data } = await api.get('/api/tools/catalog');
            menuCategories.value = data.categories;
            tools.value = data.tools;
            loaded = true;
        } catch (error) {
            errorMessage.value = await getErrorMessage(error, '工具目录加载失败。');
        } finally {
            loading.value = false;
            pendingRequest = undefined;
        }
    })();
    return pendingRequest;
}

export function useToolCatalog() {
    onMounted(() => loadTools());
    return { tools, presentedTools, toolCategories, menuCategories, loading, errorMessage, loadTools: () => loadTools(true) };
}
