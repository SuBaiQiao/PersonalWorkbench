<template>
    <header class="app-header">
        <div class="header-inner">
            <RouterLink class="brand" to="/" aria-label="Personal Workbench 工具首页">
                <span class="brand-mark" aria-hidden="true">PW</span>
                <span>Personal Workbench</span>
            </RouterLink>
            <nav class="primary-nav" aria-label="工具导航">
                <RouterLink to="/">工具台</RouterLink>
            </nav>
        </div>
    </header>
    <main class="app-shell">
        <slot />
    </main>
    <footer class="app-footer">
        <span>本地开发工具</span>
        <span>·</span>
        <span>测试数据请勿用于真实业务</span>
    </footer>
</template>
