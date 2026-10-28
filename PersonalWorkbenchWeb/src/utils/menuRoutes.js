// 只允许数据库引用构建时已包含的 Vue 页面，不执行数据库中的脚本或模块地址。
export function createMenuRoutes(tools, components) {
    return tools.filter(tool => tool.isEnabled !== false && components[tool.componentKey]
        && /^\/tools\/[a-z0-9]+(?:-[a-z0-9]+)*$/.test(tool.route))
        .map(tool => ({
            path: tool.route,
            name: 'menu-' + tool.id,
            component: components[tool.componentKey],
            meta: { title: tool.name, icon: tool.icon, menuId: tool.id }
        }));
}
