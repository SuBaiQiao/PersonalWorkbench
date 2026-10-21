# Workbench 界面设计

整体参考 Apple Liquid Glass 的分层、透明材质与轻盈导航，在 Web 上使用 CSS 渐变、半透明表面和背景模糊实现。导航具有较高的通透感，表单与数据区采用更实的表面，保证文字和结果可读。

## 样式入口

- `src/styles/glass.css`：浅色与深色设计变量、全站布局、首页、玻璃材质和组件外观。
- `src/styles/site.css`：各类工具页面的布局、上传区域、结果区和设备监控。
- `src/styles/theme.js`：Ant Design Vue 主题，与 CSS 外观同步；默认浅色，记住用户的外观选择。
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

参考：[Apple Materials](https://developer.apple.com/design/human-interface-guidelines/materials)。
