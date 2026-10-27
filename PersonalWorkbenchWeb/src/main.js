import { createApp } from 'vue';
import AntDesignVue, { message } from 'ant-design-vue';
import 'ant-design-vue/dist/reset.css';
// 按基础工具样式、全局外观、导航布局、工具组件的顺序加载；导航只覆盖与菜单有关的规则。
import './styles/site.css';
import './styles/glass.css';
import './styles/navigation.css';
import './styles/toolbox.css';
import App from './App.vue';
import { router } from './router/index.js';

const app = createApp(App);
app.use(AntDesignVue);
app.use(router);
app.config.errorHandler = error => {
    console.error('Vue application error:', error);
    message.error(error?.message || '页面操作失败，请打开控制台查看详细信息。');
};
app.mount('#app');
