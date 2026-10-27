<script setup>
import { ref } from 'vue';
import WorkbenchIcon from './WorkbenchIcon.vue';
import { menuTheme, setMenuTheme } from '../styles/theme.js';

// 弹层开关仅属于当前选择器；真正的菜单主题由 theme.js 共享并持久化。
const open = ref(false);
// 选项标识需与 setMenuTheme 支持的值、navigation.css 的布局选择器保持一致。
const options = [
    { id: 'app', name: 'App 式菜单', description: '卡片入口，轻盈直观', icon: 'grid' },
    { id: 'sidebar', name: '左侧纵向菜单', description: '分类常驻，快速切换', icon: 'sidebar' }
];
/**
 * 应用菜单布局并关闭选择面板。
 * 只更新共享主题状态，不导航、不刷新页面，从而保留当前工具的表单与计算结果。
 * @param {'app' | 'sidebar'} value 用户选中的菜单主题。
 */
function selectTheme(value) {
    setMenuTheme(value);
    open.value = false;
}
</script>

<template>
    <!-- 使用点击触发，兼容鼠标、触摸和键盘；aria-expanded 与弹层状态同步。 -->
    <a-popover v-model:open="open" trigger="click" placement="bottomRight" overlay-class-name="menu-theme-popover">
        <button class="menu-theme-button" type="button" aria-label="切换菜单主题" :aria-expanded="open">
            <WorkbenchIcon :name="menuTheme === 'sidebar' ? 'sidebar' : 'grid'" />
            <span>菜单主题</span>
            <WorkbenchIcon name="chevron" />
        </button>
        <template #content>
            <div class="menu-theme-heading"><strong>选择菜单主题</strong><span>按你的习惯，组织工作空间。</span></div>
            <div class="menu-theme-options" role="group" aria-label="菜单主题">
                <button v-for="option in options" :key="option.id" class="menu-theme-option" :class="{ selected: menuTheme === option.id }" :aria-pressed="menuTheme === option.id" type="button" @click="selectTheme(option.id)">
                    <!-- 缩略图仅用于表现布局，读屏器通过下方名称与 aria-pressed 获取选择状态。 -->
                    <span class="menu-theme-preview" :class="`preview-${option.id}`" aria-hidden="true">
                        <span class="preview-header"></span>
                        <span v-if="option.id === 'sidebar'" class="preview-sidebar"><i></i><i></i><i></i><i></i></span>
                        <span class="preview-content"><i v-for="index in 6" :key="index"></i></span>
                    </span>
                    <span class="menu-theme-option-title"><WorkbenchIcon :name="option.icon" />{{ option.name }}<WorkbenchIcon v-if="menuTheme === option.id" name="check" /></span>
                    <span class="menu-theme-option-description">{{ option.description }}</span>
                </button>
            </div>
            <p class="menu-theme-footnote">自动记住选择 · 浅色与深色外观均可搭配</p>
        </template>
    </a-popover>
</template>
