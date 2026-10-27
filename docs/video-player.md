# 视频播放器

入口：`/tools/video-player`，同时显示在工具台和侧栏的文件分类中。

## 本地文件

“选择视频文件”可以打开 MP4、WebM、MOV、FLV、TS 等文件；能否直接播放还取决于浏览器支持的音视频编码。“上传并兼容播放”把所选单个视频上传到 API，使用 FFmpeg 转为 H.264 / AAC HLS，适合浏览器不能解码的 MKV、AVI、WMV、MPEG、RMVB 等文件。上传入口单文件上限为 2GB。

本地 m3u8 不包含实际视频，需要关联清单引用的分片、子清单、初始化段及密钥。使用“打开 m3u8 文件夹”选择包含完整资源的目录，或一次选择清单与所有相邻文件；包含多个清单时可以切换 HLS 入口。播放器优先选择 master.m3u8，其次选择 index.m3u8。清单的相对路径、上级目录、带 URL 编码的文件名及 URI 标签会转换为浏览器对象 URL，停止或离开页面时释放。

本地直接播放仅使用浏览器获准读取的文件，不上传到服务器。单独选择的 m3u8 如果引用在线分片，也可直接加载；缺少本地资源时显示具体路径。不要只选择一个引用本地分片的 m3u8 文件，也不能通过网页任意读取其他本机目录。循环清单及超出授权目录的引用会被拒绝。

## 网络视频

输入 HTTP / HTTPS 地址，可自动识别 m3u8、FLV 和 TS；签名地址或无文件后缀地址可以手动选择格式。直播 FLV 请勾选“直播源”。浏览器直接加载仍受源站 CORS、HTTPS 混合内容与编码支持限制；遇到跨域或编码问题时，可以启用“服务端兼容播放”。

RTSP、RTMP 和 RTMPS 自动创建服务器转流会话，浏览器播放其生成的 HLS。视频源必须能被 API 所在机器访问；如果 API 部署在另一台机器，“本机地址”应按服务器的网络环境理解。支持在 RTSP URL 中提供源站所需的账号密码；播放标题隐藏认证信息与查询参数，FFmpeg 原始错误日志不会回传给浏览器。

直播转流使用一秒 HLS 分片、一秒关键帧间隔和短缓冲，RTSP / RTMP 的直播标记由服务器返回，用户不必额外勾选。直播画面最多转换为 1080p / 25fps，采用快速编码以减少 CPU 积压。浏览器默认跟随约两个分片后的安全播放点；明显落后、连续停顿或回到前台时会追赶最新可播放位置。页面中的“距直播边缘”仅表示相对当前清单的落后程度，不等同于摄像头到浏览器的真实端到端延迟。

直播支持“自动追踪直播”和“回到直播”。主动暂停不会被定时检查取消；继续播放时会追赶最新位置。需要回看时可取消自动追踪。服务端直播会话每两秒检查连接状态，断流后最多自动重连三次，成功恢复连续播放后重置重试次数；重连固定使用原始地址，不会提交输入框中尚未确认的新地址。停止或离开页面会取消所有追帧与重连任务。

HLS 转流仍有首次等待与分片缓冲延迟，不属于零延迟监控。受损文件、不受 FFmpeg 支持的编码、DRM 保护内容或不可访问的视频源不能保证播放。

## 服务器配置

将 FFmpeg 加入 PATH，或在 `PersonalWorkbenchAPI/appsettings.json` / 发布配置中指定：

```json
{
  "MediaPlayback": {
    "FfmpegPath": "C:\\Tools\\ffmpeg\\bin\\ffmpeg.exe"
  }
}
```

也可通过环境变量 `MediaPlayback__FfmpegPath` 设置。探测结果在服务进程中缓存，安装或更换 FFmpeg 后需要重启 API。程序不会自动安装 FFmpeg。

每个兼容播放会话使用独立随机标识和临时目录，最多同时运行 3 个会话。播放器定期发送心跳，暂停期间仍可继续播放；停止、切换来源或离开组件时请求释放会话。异常关闭浏览器后，无访问超过 2 分钟的会话会被回收；单次会话最长 4 小时、输出缓存最多 2GB。进程退出时清理其会话。直播轮换最近分片，普通文件保留已转换的分片以便回看。

临时资源不写入工作台数据库、静态发布目录或 Git。网络请求仅允许 HTTP、HTTPS、RTSP、RTMP、RTMPS；服务器文件路径与脚本协议不接受。FFmpeg 使用 `ArgumentList` 传参并限制输入协议 / 上传文件的解复用格式，媒体接口只提供生成清单及分片，不能下载上传原件或任意服务器文件。

## API

| 接口 | 用途 |
| --- | --- |
| `GET /api/media/capabilities` | 查询 FFmpeg 可用性和上传上限 |
| `POST /api/media/sessions` | JSON `{ "url": "rtsp://camera/live", "isLive": true }` 创建转流 |
| `POST /api/media/sessions/upload` | multipart `file` 上传兼容播放 |
| `GET /api/media/sessions/{id}` | 查询 starting / ready / completed / failed 状态，同时续期 |
| `GET /api/media/sessions/{id}/index.m3u8` | 获取动态 HLS 清单 |
| `GET /api/media/sessions/{id}/segment_000000.ts` | 获取分片，支持 Range |
| `DELETE /api/media/sessions/{id}` | 停止并清理会话，支持重复调用 |

## 实现与验证

- 前端 `mediaPlayback.js` 负责地址校验、本地目录索引、HLS 资源改写与释放。
- `VideoPlayerView.vue` 统一管理原生视频、HLS.js、mpegts.js 和服务端会话的生命周期。
- `MediaPlaybackService` 负责 FFmpeg 探测、并发限制、转流、心跳、错误分类和临时目录清理。
- `pnpm test` 包含本地变体 / 音轨 / 密钥清单、缺失资源、循环引用、路径越界及对象 URL 回收检查。

协议与播放引擎文档：[HLS.js](https://github.com/video-dev/hls.js)、[mpegts.js](https://github.com/xqq/mpegts.js)、[FFmpeg 协议](https://ffmpeg.org/ffmpeg-protocols.html)、[FFmpeg HLS](https://ffmpeg.org/ffmpeg-formats.html#hls-2)。
