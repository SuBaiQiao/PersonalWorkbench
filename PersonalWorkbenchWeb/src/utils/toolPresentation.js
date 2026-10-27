export const toolCategories = [
    { id: 'all', name: '全部工具' },
    { id: 'develop', name: '开发与转换' },
    { id: 'files', name: '文件与图像' },
    { id: 'system', name: '设备与监控' },
    { id: 'workspace', name: '协作与收藏' }
];

const presentations = {
    '/tools/file-manager': { category: 'files', icon: 'folder', tone: 'blue', description: '浏览磁盘目录，预览文件并在线编辑文本。', keywords: '磁盘 文件 文件夹 目录 管理 预览 在线 编辑 file explorer disk folder' },
    '/tools/video-player': { category: 'files', icon: 'video', tone: 'teal', description: '本地影像、HLS 与直播流，在一个播放器里打开。', keywords: '视频 播放器 video mp4 webm mkv avi m3u8 hls flv rtsp rtmp ts' },
    '/tools/json-toolbox': { category: 'develop', icon: 'braces', tone: 'amber', description: '格式化与查询，让复杂 JSON 清晰可读。', keywords: 'json jsonpath 格式化 压缩 树 校验' },
    '/tools/text-diff': { category: 'develop', icon: 'diff', tone: 'rose', description: '并排查看文本，快速找到增加与删除。', keywords: 'diff 文本 对比 差异 比较' },
    '/tools/regex-tester': { category: 'develop', icon: 'regex', tone: 'indigo', description: '实时匹配、分组与替换，验证每一条规则。', keywords: 'regex regexp 正则 表达式 匹配 替换' },
    '/tools/random-generator': { category: 'develop', icon: 'shuffle', tone: 'teal', description: 'UUID、随机字符与密码，按需批量生成。', keywords: 'uuid guid random 随机 密码 字符 数字' },
    '/tools/jwt-decoder': { category: 'develop', icon: 'lock', tone: 'amber', description: '解读令牌声明，直观查看签发与过期时间。', keywords: 'jwt token 令牌 解析 header payload' },
    '/tools/data-to-code': { category: 'develop', icon: 'code', tone: 'blue', description: '从 JSON 样本，生成 C# 与 TypeScript 类型。', keywords: 'json c# csharp typescript 实体 类型 代码' },
    '/tools/http-client': { category: 'develop', icon: 'network', tone: 'teal', description: '编辑请求、检查响应，轻量调试 HTTP 接口。', keywords: 'http api curl get post 请求 接口 调试' },
    '/tools/image-toolbox': { category: 'files', icon: 'crop', tone: 'rose', description: '裁剪、缩放与压缩，处理图片的日常所需。', keywords: '图片 压缩 裁剪 缩放 转换 颜色 png jpg webp' },
    '/tools/identity-generator': { category: 'develop', icon: 'identity', tone: 'blue', description: '为开发测试，准备一份虚构身份信息。', keywords: '身份证 测试 数据 identity' },
    '/tools/sql-converter': { category: 'develop', icon: 'code', tone: 'indigo', description: '将驼峰命名，轻松转换为大写下划线。', keywords: 'sql camel snake' },
    '/tools/amount-uppercase': { category: 'develop', icon: 'currency', tone: 'amber', description: '把数字金额，准确转换为人民币大写。', keywords: '金额 人民币 money' },
    '/tools/expense-invoice': { category: 'files', icon: 'files', tone: 'rose', description: '整理零散发票，合并为一份清晰的 PDF。', keywords: 'pdf 发票 报销 合并' },
    '/tools/image-base64': { category: 'files', icon: 'image', tone: 'teal', description: '图片与 Base64，双向转换，一步完成。', keywords: 'base64 图片 image' },
    '/tools/cron-generator': { category: 'develop', icon: 'clock', tone: 'blue', description: '生成、解析定时规则，预览未来执行时间。', keywords: 'cron 定时 计划 校验 解析 执行' },
    '/tools/timestamp': { category: 'develop', icon: 'clock', tone: 'teal', description: '秒与毫秒、日期与时区，自如转换。', keywords: 'timestamp unix 时间戳 日期 时区 毫秒' },
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
