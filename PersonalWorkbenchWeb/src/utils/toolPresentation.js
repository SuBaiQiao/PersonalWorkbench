export const toolCategories = [
    { id: 'all', name: '全部工具' },
    { id: 'develop', name: '开发与转换' },
    { id: 'files', name: '文件与图像' },
    { id: 'system', name: '设备与监控' },
    { id: 'workspace', name: '协作与收藏' }
];

const presentations = {
    '/tools/identity-generator': { category: 'develop', icon: 'identity', tone: 'blue', description: '为开发测试，准备一份虚构身份信息。', keywords: '身份证 测试 数据 identity' },
    '/tools/sql-converter': { category: 'develop', icon: 'code', tone: 'indigo', description: '将驼峰命名，轻松转换为大写下划线。', keywords: 'sql camel snake' },
    '/tools/amount-uppercase': { category: 'develop', icon: 'currency', tone: 'amber', description: '把数字金额，准确转换为人民币大写。', keywords: '金额 人民币 money' },
    '/tools/expense-invoice': { category: 'files', icon: 'files', tone: 'rose', description: '整理零散发票，合并为一份清晰的 PDF。', keywords: 'pdf 发票 报销 合并' },
    '/tools/image-base64': { category: 'files', icon: 'image', tone: 'teal', description: '图片与 Base64，双向转换，一步完成。', keywords: 'base64 图片 image' },
    '/tools/cron-generator': { category: 'develop', icon: 'clock', tone: 'blue', description: '让每一次定时执行，都恰逢其时。', keywords: 'cron 定时 计划' },
    '/tools/android-monitor': { category: 'system', icon: 'phone', tone: 'teal', description: '多个 Android 设备，尽在同一视野。', keywords: 'android adb 手机 集群' },
    '/tools/port-checker': { category: 'system', icon: 'network', tone: 'indigo', description: '快速定位端口占用与关联进程。', keywords: 'tcp udp port 端口 进程' },
    '/tools/collaborative-editor': { category: 'workspace', icon: 'edit', tone: 'blue', description: '同步文字、分享文件，让协作更流畅。', keywords: '协同 协作 文本 编辑 同步' },
    '/tools/system-monitor': { category: 'system', icon: 'activity', tone: 'teal', description: 'CPU、内存与显卡，状态一目了然。', keywords: 'cpu gpu 内存 显卡 系统 监控' },
    '/tools/delta-cards': { category: 'workspace', icon: 'cards', tone: 'amber', description: '记录每一份收藏，见证收集的进度。', keywords: 'delta 卡牌 三角洲 收藏' },
    '/tools/crypto': { category: 'develop', icon: 'lock', tone: 'indigo', description: '加密、摘要与编解码，常用能力都在这里。', keywords: 'md5 aes rsa sm2 sm4 base64 url encode 加密 解密 编码' },
    '/tools/code-generator': { category: 'files', icon: 'qr', tone: 'blue', description: '把文字与网址，变成一扫即达的图案。', keywords: 'qr code128 ean 条形码 二维码' }
};

export function getToolPresentation(route) {
    return presentations[route] || { category: 'develop', icon: 'grid', tone: 'blue', description: '一个随时可用的日常小工具。', keywords: '' };
}
