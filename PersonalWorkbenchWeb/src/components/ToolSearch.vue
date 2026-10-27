<script setup>
import { computed, nextTick, onBeforeUnmount, onMounted, ref, watch } from 'vue';
import { useRoute, useRouter, isNavigationFailure, NavigationFailureType } from 'vue-router';
import WorkbenchIcon from './WorkbenchIcon.vue';
import { useToolCatalog } from '../composables/useToolCatalog.js';
import { useToolSearch } from '../composables/useToolSearch.js';
import { searchTools } from '../utils/toolSearch.js';

const { query, clearSearch } = useToolSearch();
const { presentedTools, loading, errorMessage, loadTools } = useToolCatalog();
const router = useRouter(), route = useRoute();
const root = ref(null), input = ref(null), open = ref(false), activeIndex = ref(0);
const results = computed(() => searchTools(presentedTools.value, query.value));
const hasQuery = computed(() => Boolean(query.value.trim()));
const canShowResults = computed(() => open.value && hasQuery.value && !loading.value && !errorMessage.value);
const activeId = computed(() => canShowResults.value && results.value[activeIndex.value] ? `tool-search-option-${activeIndex.value}` : undefined);
const suggestions = ['加密', 'Base64', 'MD5', '视频', '时间'];

watch(query, value => { if (value.trim()) open.value = true; activeIndex.value = 0; });
watch(() => route.path, () => { open.value = false; clearSearch(); });
function outside(event) { if (!root.value?.contains(event.target)) open.value = false; }
function blur(event) { if (!root.value?.contains(event.relatedTarget)) open.value = false; }
function clear() { clearSearch(); input.value?.focus(); open.value = true; }
function suggest(value) { query.value = value; input.value?.focus(); }
function retryTools() {
    // 重试按钮会被加载状态替换，先将焦点交回输入框，避免按钮移除导致面板意外关闭。
    input.value?.focus(); open.value = true; loadTools();
}

/** 导航仍交给 Vue Router，文件编辑等工具的未保存确认可以正常拦截跳转。 */
async function selectTool(tool) {
    const failure = await router.push(tool.route);
    if (failure && !isNavigationFailure(failure, NavigationFailureType.duplicated)) return;
    clearSearch(); open.value = false;
}

/** 输入焦点留在组合框，方向键移动高亮；中文输入法确认候选字时不触发工具跳转。 */
async function keydown(event) {
    if (event.isComposing || event.keyCode === 229) return;
    if (event.key === 'Escape') { event.preventDefault(); event.stopPropagation(); open.value = false; return; }
    if (loading.value || errorMessage.value) return;
    if (event.key === 'Enter') {
        if (open.value && hasQuery.value && results.value[activeIndex.value]) {
            event.preventDefault(); await selectTool(results.value[activeIndex.value]);
        }
        return;
    }
    if (!['ArrowDown', 'ArrowUp'].includes(event.key) || !hasQuery.value || !results.value.length) return;
    event.preventDefault();
    if (!open.value) { open.value = true; activeIndex.value = event.key === 'ArrowDown' ? 0 : results.value.length - 1; }
    else activeIndex.value = (activeIndex.value + (event.key === 'ArrowDown' ? 1 : -1) + results.value.length) % results.value.length;
    await nextTick(); root.value?.querySelector(`#tool-search-option-${activeIndex.value}`)?.scrollIntoView({ block: 'nearest' });
}
onMounted(() => document.addEventListener('pointerdown', outside));
onBeforeUnmount(() => document.removeEventListener('pointerdown', outside));
</script>

<template>
    <div ref="root" class="global-tool-search" @focusout="blur">
        <div class="global-search-field"><WorkbenchIcon name="search" />
            <input ref="input" v-model="query" type="search" role="combobox" aria-label="搜索工具" aria-autocomplete="list" :aria-expanded="open" :aria-controls="canShowResults ? 'tool-search-results' : undefined" :aria-activedescendant="activeId" placeholder="搜索工具或功能，如 加密、Base64" autocomplete="off" :maxlength="120" @focus="open = true" @keydown="keydown" />
            <button v-if="query" type="button" aria-label="清空工具搜索" @click="clear"><WorkbenchIcon name="close" /></button>
        </div>
        <div v-if="open" class="global-search-popover">
            <div v-if="loading" class="search-state" role="status">正在加载工具目录…</div>
            <div v-else-if="errorMessage" class="search-state" role="alert"><span>{{ errorMessage }}</span><a-button size="small" @click="retryTools">重新加载</a-button></div>
            <div v-else-if="!hasQuery" class="search-suggestions"><p>按名称、功能或关联词查找工具</p><div><button v-for="value in suggestions" :key="value" type="button" @click="suggest(value)">{{ value }}</button></div></div>
            <template v-else>
                <div class="search-summary" role="status">{{ results.length ? `找到 ${results.length} 个相关工具` : '没有找到匹配的工具' }}<span>↑ ↓ 选择 · Enter 打开</span></div>
                <ul id="tool-search-results" role="listbox" aria-label="工具搜索结果" class="global-search-results">
                    <li v-for="(tool, index) in results" :key="tool.route" role="presentation"><button :id="`tool-search-option-${index}`" class="search-result" type="button" role="option" tabindex="-1" :aria-selected="index === activeIndex" @pointermove="activeIndex = index" @click="selectTool(tool)">
                        <span class="search-result-icon"><WorkbenchIcon :name="tool.icon" /></span><span class="search-result-copy"><strong>{{ tool.name }}</strong><small>{{ tool.description }}</small><span v-if="tool.features.length" class="search-feature-list"><span v-for="feature in (tool.matchedFeatures.length ? tool.matchedFeatures : tool.features).slice(0, 6)" :key="feature">{{ feature }}</span></span></span><WorkbenchIcon name="arrow" />
                    </button></li>
                </ul>
                <p v-if="!results.length" class="search-empty">试试“加密”“图片”“时间”，或输入 MD5、RTSP 等具体功能。</p>
            </template>
        </div>
    </div>
</template>

<style scoped>
.global-tool-search { position: relative; min-width: 0; flex: 1; max-width: 640px; margin-inline: auto; }
.global-search-field { display: flex; align-items: center; min-height: 44px; gap: 10px; padding: 0 12px; border: 1px solid var(--border); border-radius: 14px; color: var(--text-subtle); background: var(--well); }
.global-search-field:focus-within { outline: 2px solid var(--accent); outline-offset: 2px; }
.global-search-field input { width: 100%; min-width: 0; height: 42px; min-height: 42px; padding: 0; border: 0; outline: 0; color: var(--text); background: transparent; box-shadow: none; font-size: 13px; }
.global-search-field input::-webkit-search-cancel-button { display: none; }
.global-search-field input::placeholder { color: var(--text-subtle); }
.global-search-field > button { display: grid; place-items: center; width: 32px; height: 40px; flex-shrink: 0; padding: 0; border: 0; color: var(--text-muted); background: transparent; cursor: pointer; }
.global-search-popover { position: absolute; top: calc(100% + 8px); left: 0; z-index: 200; width: 100%; overflow: hidden; border: 1px solid var(--border); border-radius: 16px; color: var(--text); background: var(--bg); box-shadow: var(--shadow-float); }
.search-state,.search-suggestions { display: grid; gap: 12px; padding: 18px; color: var(--text-muted); font-size: 13px; }
.search-suggestions p { margin: 0; }
.search-suggestions > div { display: flex; flex-wrap: wrap; gap: 8px; }
.search-suggestions button { min-height: 36px; padding: 0 12px; border: 1px solid var(--border); border-radius: 9px; background: var(--well); color: var(--text); cursor: pointer; }
.search-summary { display: flex; justify-content: space-between; gap: 8px; padding: 12px 14px; border-bottom: 1px solid var(--border); font-size: 12px; color: var(--text-muted); }
.search-summary > span { color: var(--text-subtle); font-size: 11px; }
.global-search-results { max-height: min(440px, calc(100dvh - 264px)); margin: 0; padding: 4px; list-style: none; overflow: auto; overscroll-behavior: contain; }
.search-result { display: flex; width: 100%; align-items: flex-start; gap: 10px; padding: 12px 10px; border: 0; border-radius: 10px; background: transparent; color: var(--text); text-align: left; cursor: pointer; }
.search-result[aria-selected="true"] { background: var(--accent-soft); }
.search-result:focus-visible,.search-suggestions button:focus-visible,.global-search-field > button:focus-visible { outline: 2px solid var(--accent); outline-offset: -2px; }
.search-result-icon { display: grid; place-items: center; width: 32px; height: 32px; flex-shrink: 0; border-radius: 9px; color: var(--accent-strong); background: var(--accent-soft); }
.search-result-copy { display: grid; flex: 1; min-width: 0; gap: 4px; }
.search-result strong { font-size: 13px; font-weight: 600; }
.search-result small { font-size: 12px; line-height: 1.6; color: var(--text-muted); overflow-wrap: anywhere; }
.search-result > .workbench-icon { width: 14px; margin-top: 8px; color: var(--text-subtle); }
.search-feature-list { display: flex; flex-wrap: wrap; gap: 4px; margin-top: 3px; }
.search-feature-list > span { padding: 2px 6px; border: 1px solid var(--border); border-radius: 5px; color: var(--accent-strong); background: var(--well); font-size: 10px; }
.search-empty { margin: 0; padding: 16px; color: var(--text-muted); font-size: 12px; }
@media (max-width: 1100px) { .global-tool-search { order: 2; flex-basis: 100%; max-width: none; margin: 0; } }
@media (max-width: 600px) { .global-search-field input { font-size: 16px; } .search-summary > span { display: none; } .search-result { padding-inline: 6px; } }
</style>
