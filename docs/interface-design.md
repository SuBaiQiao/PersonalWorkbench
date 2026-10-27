# Workbench 界面设计

整体参考 Apple Liquid Glass 的分层、透明材质与轻盈导航，在 Web 上使用 CSS 渐变、半透明表面和背景模糊实现。导航具有较高的通透感，表单与数据区采用更实的表面，保证文字和结果可读。

## 样式入口

- `src/styles/glass.css`：浅色与深色设计变量、全站布局、首页、玻璃材质和组件外观。
- `src/styles/site.css`：各类工具页面的布局、上传区域、结果区和设备监控。
- `src/styles/theme.js`：Ant Design Vue 主题，与 CSS 外观同步；默认浅色，记住用户的外观选择。
- `src/styles/navigation.css`：App 式菜单与左侧纵向菜单的布局、主题选择预览和手机抽屉导航。
- `src/components/MenuThemePicker.vue`：顶部“菜单主题”入口，可随时切换菜单布局。
- `src/components/WorkbenchSidebar.vue`：按分类展示工具目录，支持搜索、选中状态、桌面收起和手机抽屉。
- `src/composables/useToolCatalog.js`：首页与侧栏共用后端工具目录、加载状态和重试入口，避免重复请求。
- `src/components/WorkbenchIcon.vue`：统一尺寸和笔画的 SVG 图标。
- `src/utils/toolPresentation.js`：工具卡片的分类、图标、说明和搜索关键词。菜单目录继续读取后端配置。

## 设计约定

- 主色使用 `--accent`，内容文字使用 `--text`、`--text-muted` 和 `--text-subtle`。
- 一级面板使用 `--surface`、`--glass-border`、`--radius` 和 `--shadow-glass`。
- 输入和结果区使用 `--surface-raised` 或 `--well`，避免叠加过多透明层。
- 主导航、工具页、表格、弹窗与下拉菜单共享圆角、间距和配色。
- 二维码、条形码及设备视频保留原始图像颜色，不受主题影响。
- 交互反馈主要采用颜色、阴影和小幅位移；不使用持续装饰动画。
- 支持键盘聚焦、跳至主要内容、减少动态效果和减少透明度设置。
- 不支持背景模糊的浏览器使用更实的背景表面。

## 菜单主题

顶部“菜单主题”提供两种布局，默认保留 App 式菜单。App 式菜单通过首页工具卡片进入功能；左侧纵向菜单将分类与工具入口常驻左侧，并突出当前页面。侧栏首页使用紧凑的介绍区，把空间留给工具列表。切换菜单主题不会切换路由或重新创建正在使用的工具页面。

菜单布局与浅色 / 深色外观独立，可自由组合。`workbench-menu-theme`、`workbench-sidebar-collapsed` 和原有的 `workbench-appearance` 分别保存在浏览器本地；浏览器禁止存储时仍能即时切换。桌面侧栏可收起为图标栏；900px 及以下通过标题栏菜单按钮打开抽屉，支持 Escape 关闭、焦点约束与选择工具后自动关闭。

参考：[Apple Materials](https://developer.apple.com/design/human-interface-guidelines/materials)。
