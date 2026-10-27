import { ref } from 'vue';

// 搜索词只保存在本次页面会话中，不写入浏览器历史、localStorage 或后端。
// 顶栏、首页卡片和侧栏共用此状态，避免同一搜索在不同入口显示不同的筛选结果。
const query = ref('');
export function useToolSearch() { return { query, clearSearch: () => { query.value = ''; } }; }
