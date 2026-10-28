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
        const features = tool.features ?? [];
        const name = compact(tool.name), keywords = compact(tool.keywords), description = compact(tool.description);
        const category = compact(tool.categoryName);
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
