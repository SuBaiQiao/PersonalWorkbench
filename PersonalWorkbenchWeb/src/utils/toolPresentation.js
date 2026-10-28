// 菜单展示字段来自数据库；这里只处理缺省值，不按路由保存配置。
export function getToolPresentation(tool) {
    return { ...tool, icon: tool.icon || 'grid', tone: tool.tone || 'blue', features: tool.features || [] };
}
