import { toolCategories } from './toolPresentation.js';

// 功能名称用于搜索和结果说明，必须对应工具实际提供的能力，不把 Base64 或 MD5 描述成加密算法。
const featuresByRoute = {
    '/tools/crypto': ['MD5', 'Base64', 'AES', 'RSA', 'SM2', 'SM4', 'URL 编解码'],
    '/tools/image-base64': ['图片转 Base64', 'Base64 转图片'],
    '/tools/image-toolbox': ['图片裁剪', '图片缩放', '图片压缩', '格式转换'],
    '/tools/code-generator': ['二维码', 'Code128', 'EAN 条形码'],
    '/tools/file-manager': ['目录浏览', '文件预览', '在线编辑', 'UTF-8 / GBK'],
    '/tools/video-player': ['本地视频', 'M3U8 / HLS', 'FLV', 'RTSP / RTMP'],
    '/tools/json-toolbox': ['JSON 格式化', 'JSONPath', '树形查看', 'JSON 校验'],
    '/tools/text-diff': ['文本对比', '差异比较'],
    '/tools/regex-tester': ['正则匹配', '分组捕获', '正则替换'],
    '/tools/random-generator': ['UUID / GUID', '随机密码', '随机字符'],
    '/tools/jwt-decoder': ['JWT 解码', 'Header / Payload', '过期时间'],
    '/tools/data-to-code': ['JSON 转 C#', 'JSON 转 TypeScript'],
    '/tools/http-client': ['HTTP 请求', 'cURL 导入', '接口调试'],
    '/tools/identity-generator': ['测试身份证', '省市县区划', '测试身份数据'],
    '/tools/sql-converter': ['SQL 命名转换', '驼峰转下划线'],
    '/tools/amount-uppercase': ['人民币大写', '金额转换'],
    '/tools/expense-invoice': ['PDF 合并', '发票整理'],
    '/tools/cron-generator': ['Cron 表达式', '执行时间预览'],
    '/tools/timestamp': ['Unix 时间戳', '日期转换', '时区转换'],
    '/tools/android-monitor': ['ADB', 'Android 设备监控'],
    '/tools/port-checker': ['端口占用', 'TCP / UDP', '关联进程'],
    '/tools/system-monitor': ['CPU', 'GPU', '内存', '磁盘', '进程监控'],
    '/tools/collaborative-editor': ['协同编辑', '实时同步', '文件分享'],
    '/tools/delta-cards': ['卡牌收藏', '收集进度']
};

/** 关联词采用有方向的扩展：搜“加密”可关联 Base64，搜“MD5”仍优先精确定位 MD5。 */
const relatedGroups = [
    { aliases: ['加密', '解密', '加密工具', '加密解密', '数据加密', 'crypto', 'cryptography', 'encryption', 'encrypt', 'decrypt'], terms: ['加密', '解密', 'MD5', 'Base64', 'AES', 'RSA', 'SM2', 'SM4'] },
    { aliases: ['哈希', '散列', '摘要', 'hash', 'digest'], terms: ['MD5', '哈希', '散列', '摘要'] },
    { aliases: ['编码', '解码', '编解码', '编码转换', 'encoding', 'decoding', 'encode', 'decode'], terms: ['Base64', 'URL', 'UTF-8', 'GBK', '编码', '解码'] },
    { aliases: ['base64', 'b64'], terms: ['Base64'] },
    { aliases: ['时间', '日期', '定时', 'time', 'date'], terms: ['时间', '日期', 'Cron', 'Unix', '时间戳', '时区'] },
    { aliases: ['视频', '直播', '播放', '播放器', 'video', 'stream'], terms: ['视频', '直播', 'M3U8', 'HLS', 'FLV', 'RTSP', 'RTMP'] },
    { aliases: ['图片', '图像', 'image', 'photo'], terms: ['图片', '图像', 'image', '裁剪', '缩放', 'PNG', 'JPG', 'WEBP'] },
    { aliases: ['网络', 'network'], terms: ['HTTP', 'cURL', '端口', 'TCP', 'UDP', '网络'] },
    { aliases: ['格式化', 'format'], terms: ['格式化', '格式转换'] },
    { aliases: ['文件', '文件管理', 'file'], terms: ['文件', '目录', '文件夹', '磁盘', 'PDF'] },
    { aliases: ['协作', '协同', '共享', 'collaboration'], terms: ['协作', '协同', '同步', '分享'] }
];

const normalize = value => String(value ?? '').normalize('NFKC').toLowerCase().trim();
// 忽略功能别名中的空格和连接符，例如 BASE-64、base64 与全角 ＢＡＳＥ６４ 等价。
const compact = value => normalize(value).replace(/[\s_-]+/g, '');
const groups = relatedGroups.map(group => ({ aliases: group.aliases.map(compact), terms: group.terms.map(compact) }));

/**
 * 标题、功能、关键词、说明、分类共同建立搜索索引。多个词采用交集，单个词的关联项采用并集。
 * 排序优先级：标题精确命中 > 功能直接命中 > 关键词命中 > 关联命中；同分保留目录原顺序。
 */
export function searchTools(tools, query = '') {
    const normalized = normalize(query);
    const tokens = normalized.replace(/([\u4e00-\u9fff])([a-z0-9])/g, '$1 $2')
        .replace(/([a-z0-9])([\u4e00-\u9fff])/g, '$1 $2').split(/[\s,，]+/).map(compact).filter(Boolean);
    if (normalized && !tokens.length) return [];
    const searches = tokens.map(token => ({ token, related: groups.filter(group => group.aliases.includes(token)).flatMap(group => group.terms) }));
    return tools.map((tool, index) => {
        const features = tool.features ?? featuresByRoute[tool.route] ?? [];
        const name = compact(tool.name), keywords = compact(tool.keywords), description = compact(tool.description);
        const category = compact(toolCategories.find(item => item.id === tool.category)?.name);
        const featureKeys = features.map(compact);
        const haystack = [name, keywords, description, category, ...featureKeys].join(' ');
        let score = name === compact(query) && normalized ? 100 : 0;
        for (const { token, related } of searches) {
            if (name.includes(token)) score += 40;
            else if (featureKeys.some(feature => feature.includes(token))) score += 30;
            else if (keywords.includes(token)) score += 20;
            else if (description.includes(token)) score += 10;
            else if (category.includes(token)) score += 5;
            else if (related.some(term => haystack.includes(term))) score += 2;
            else return null;
        }
        const matchedFeatures = features.filter((_, featureIndex) => searches.some(({ token, related }) =>
            [token, ...related].some(term => featureKeys[featureIndex].includes(term))));
        return { ...tool, features, matchedFeatures, score, index };
    }).filter(Boolean).sort((a, b) => b.score - a.score || a.index - b.index);
}
