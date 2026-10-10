# PersonalWorkbench

PersonalWorkbench 是一个个人工具箱，使用 Vue 3 + Vite 构建前端，ASP.NET Core 8 提供 API 和 WebSocket。前后端源码分别位于 `PersonalWorkbenchWeb` 和 `PersonalWorkbenchAPI`，同仓库管理，生产环境统一由 ASP.NET Core 托管。

## 功能

- 身份信息生成器：生成仅供开发测试使用的虚构身份信息。
- SQL 驼峰转换器：将 SQL 或命名片段中的驼峰命名转换为大写下划线命名。
- 报销发票 PDF 合并：支持一次选择多个 PDF，也支持将 PDF 文件拖动到页面中，再通过接口合并并下载。
- 图片与 Base64 转换器：支持图片转 Base64，以及 Base64 或 Data URL 转图片下载。
- Cron 表达式生成器：根据常见执行频率和时间参数生成标准五字段 Cron 表达式。
- Android 集群设备监控：读取 `devices.txt`，通过 ADB 抓取设备屏幕并使用 WebSocket 实时推送到监控大屏。
- 端口占用检查：检查本机 TCP/UDP 端口，并显示占用它的进程名称、PID 和监听状态。
- 协同文本编辑器：通过 WebSocket 在多个浏览器之间同步文本，保留历史版本，并提供临时文件上传、下载、删除和粘贴图片上传。

## 技术栈

- .NET 8
- ASP.NET Core Web API Controller
- `PdfSharpCore 1.3.67`
- Vue 3.5.43
- Ant Design Vue 4.2.6
- axios 1.20.0
- Vue Router 4
- Vite 7 + Vue 单文件组件 + 原生 CSS

## 快速开始

### 环境要求

- .NET 8 SDK
- Node.js 20.19+ 或 22.12+（建议使用当前 LTS 版本），以及 npm 或 pnpm 11

### 启动项目

在项目根目录的第一个终端启动 API：

```bash
dotnet run --project PersonalWorkbenchAPI/PersonalWorkbenchAPI.csproj
```

在第二个终端启动前端：

```bash
cd PersonalWorkbenchWeb
npm install
npm run dev
```

也可以使用 pnpm。`pnpm-workspace.yaml` 已允许 Vite 所需的 `esbuild` 和 `core-js` 安装脚本：

```powershell
cd PersonalWorkbenchWeb
pnpm install
pnpm run dev
```

浏览器访问 `http://localhost:5173`。Vite 将 `/api` 和 `/ws` 请求代理到后端，WebSocket 代理已启用，无需为普通开发额外配置 CORS。后端默认地址为：

```text
http://localhost:5500
```

后端默认监听 `0.0.0.0:5500`，端口可通过 `PersonalWorkbenchAPI/appsettings.json` 或发布目录中的 `appsettings.json` 配置。修改 `Urls` 字段即可更换端口，例如 `http://0.0.0.0:5600`。也可以使用 `ASPNETCORE_URLS` 环境变量或 `--urls` 命令行参数覆盖该配置。开发代理目标变更时，将 `PersonalWorkbenchWeb/.env.example` 复制为 `.env.local`，调整 `API_PROXY_TARGET` 并重启 Vite。

Android 监控页默认使用当前浏览器地址，因此在开发环境也通过 Vite 代理连接。手动指定另一台服务时，该服务需要允许浏览器跨域 HTTP 请求；建议保持当前地址并调整开发代理目标。

如需从同一局域网的其他设备访问，请使用本机局域网 IP。如果 Windows 防火墙拦截连接，请在管理员 PowerShell 中为实际端口添加仅允许本地子网访问的入站规则：

```powershell
New-NetFirewallRule -DisplayName 'PersonalWorkbench TCP 5500 局域网访问' -Direction Inbound -Action Allow -Protocol TCP -LocalPort 5500 -RemoteAddress LocalSubnet -Profile Private,Public
```

当前工具台包含可终止本机进程的接口。开放端口后，同一子网中的设备也能调用该接口，请仅在可信网络中使用。

构建整个项目（PowerShell，在仓库根目录）：

```bash
./build.ps1
```

也可以分别执行 `npm run build`（前端目录）和 `dotnet build PersonalWorkbench.sln`（根目录）。先构建前端，再启动开发环境 API，可直接通过 `http://localhost:5500` 使用 `dist` 中的页面；修改前端源码后需要重新构建并重启 API。日常开发推荐使用 Vite 热更新。

### 发布

```powershell
./build.ps1 -Mode Publish
# 或在根目录执行：
dotnet publish PersonalWorkbenchAPI/PersonalWorkbenchAPI.csproj -c Release -o artifacts/publish
```

API 的发布目标会安装前端依赖、执行 Vite 构建，并将 `PersonalWorkbenchWeb/dist` 放入发布包的 `wwwroot`。发布失败会中止，不会静默使用旧前端。生产运行环境需要 ASP.NET Core 8 Runtime，不需要 Node.js：

```powershell
cd artifacts/publish
dotnet PersonalWorkbenchAPI.dll
```

生产环境访问 `http://localhost:5500`。页面、API 和 WebSocket 共用一个服务端口；刷新 `/tools/...` 会返回 SPA 入口，未知 `/api/...` 和 `/ws/...` 请求仍返回 404。

首次 `npm install` 成功后会生成 `PersonalWorkbenchWeb/package-lock.json`，应将其提交到 Git。当前迁移尚未生成锁文件，依赖安装及完整构建验证由维护者执行。

### 迁移后验证

1. 执行 `./build.ps1`，确认前后端均构建成功。
2. 启动 API 和 Vite，确认首页显示八个工具，已有功能可正常请求接口及下载文件。
3. 同时打开两个协同编辑页面，确认文本同步；离开页面后不应继续创建连接。Android 实时画面需在安装 ADB 且设备可访问的环境验证。
4. 执行 `./build.ps1 -Mode Publish` 并启动发布包，确认首页、静态资源和刷新 `/tools/sql-converter` 均正常。
5. 请求未知 `/api/not-found`、`/ws/not-found`，确认返回 404，而不是页面 HTML。

## 目录结构

```text
PersonalWorkbench/
├── PersonalWorkbench.sln             # .NET 解决方案入口
├── build.ps1                         # 整体构建 / 发布入口
├── README.md
├── PersonalWorkbenchAPI/
│   ├── PersonalWorkbenchAPI.csproj
│   ├── Program.cs                    # API、WebSocket、静态文件和 SPA 路由
│   ├── Controllers/
│   ├── Services/
│   ├── Models/
│   ├── Data/
│   │   ├── tools.json
│   │   └── collaborative-editor/     # 运行数据，不随源码或发布包分发
│   ├── Properties/
│   ├── appsettings.json
│   └── devices.txt
└── PersonalWorkbenchWeb/
    ├── package.json
    ├── vite.config.js
    ├── index.html
    ├── .env.example
    └── src/
        ├── main.js                   # npm 依赖和 Vue 应用初始化
        ├── App.vue
        ├── router/                   # 保留原有 /tools/... 地址
        ├── layouts/                  # 公共导航和页脚
        ├── views/
        │   ├── HomeView.vue
        │   └── tools/                # 八个工具的模板与逻辑
        ├── api/                      # axios 实例和错误解析
        ├── utils/                    # 下载、文件、剪贴板、状态能力
        └── styles/                   # 公共样式与 Ant Design 主题
```

## 首页工具配置

首页工具由 `PersonalWorkbenchAPI/Data/tools.json` 配置，前端通过 `GET /api/tools` 读取名称、排序、路由和图标。可以直接调整已有工具的展示；新增工具页面时，还需在 `PersonalWorkbenchWeb/src/router/index.js` 注册对应 Vue 组件：

```json
[
  {
    "name": "SQL 驼峰转换器",
    "route": "/tools/sql-converter",
    "order": 20,
    "icon": "SQL"
  }
]
```

字段说明：

| 字段 | 说明 |
| --- | --- |
| `name` | 首页显示的工具名称 |
| `route` | 站内 Vue Router 路由 |
| `order` | 首页排序号，数值越小越靠前 |
| `icon` | 可选图标文本；未配置时使用工具名称的第一个字符 |

`ToolCatalogService` 会过滤空名称、空路由和外部链接，只允许以 `/` 开头的站内路由。

## API 接口

### 获取行政区划数据

```http
GET /api/identity/areas
```

返回身份信息生成器使用的省、市、区县三级数据。

### 生成测试身份信息

```http
POST /api/identity/generate
Content-Type: application/json
```

请求示例：

```json
{
  "provinceCode": "",
  "cityCode": "",
  "countyCode": "",
  "birthDate": null,
  "gender": "random",
  "count": 5
}
```

`gender` 支持 `random`、`male` 和 `female`，`count` 必须在 1 到 100 之间。此接口生成的数据仅用于测试，不能用于身份验证、实名认证或其他真实业务。

### SQL 驼峰转换

```http
POST /api/sql/convert
Content-Type: application/json
```

请求示例：

```json
{
  "inputSql": "select userName, createTime from userInfo"
}
```

响应示例：

```json
{
  "outputSql": "SELECT USER_NAME, CREATE_TIME FROM USER_INFO"
}
```

### 合并报销发票 PDF

```http
POST /api/pdf/expense-merge
Content-Type: multipart/form-data
```

表单字段名为 `files`，可以重复传入多个 PDF 文件。接口返回 `application/pdf` 文件，默认文件名为 `合并后的PDF文件.pdf`。单次请求大小限制为 100 MB。

合并顺序与 Python 脚本保持一致：第一页高度大于 600pt 的 PDF 会排在普通 PDF 之后，同一文件内部的页序保持不变。

### Base64 转图片

```http
POST /api/image/base64-to-image
Content-Type: application/json
```

请求示例：

```json
{
  "base64": "data:image/png;base64,...",
  "fileName": "converted.png"
}
```

接口返回图片文件。支持纯 Base64 和 Base64 Data URL，文件名为可选字段；未提供文件名时会根据识别出的图片格式自动生成。单次请求最多处理 20 MB 的图片内容。

### 图片转 Base64

```http
POST /api/image/image-to-base64
Content-Type: multipart/form-data
```

表单字段名为 `file`。接口返回图片文件名、媒体类型、纯 Base64 和可直接用于浏览器预览的 `dataUrl`。支持 PNG、JPG、GIF、WEBP、BMP 和 SVG，单张图片不超过 20 MB。

### 生成 Cron 表达式

```http
POST /api/cron/generate
Content-Type: application/json
```

请求示例：

```json
{
  "frequency": "weekly",
  "interval": 1,
  "hour": 9,
  "minute": 30,
  "dayOfWeek": 1,
  "dayOfMonth": 1
}
```

`frequency` 支持 `minutely`、`hourly`、`daily`、`weekly` 和 `monthly`。接口返回 Cron 表达式和中文规则说明。生成结果使用五字段格式：`分钟 小时 日期 月份 星期`，星期日使用 `0`。

### Android 集群设备监控

打开 `/tools/android-monitor` 使用监控大屏。设备地址按一行一个写入 `PersonalWorkbenchAPI/devices.txt`（发布后位于发布目录），支持纯 IP（默认使用 ADB 的 5555 端口）或 `IP:端口`，例如 `192.168.1.20`、`192.168.1.20:5555`。运行环境需要安装并能从 PATH 找到 `adb`；如果同时安装 `ffmpeg`，服务会将画面缩放为 400 像素宽的 JPEG 后推送。页面会为每台设备建立 WebSocket，并持续显示 `adb exec-out screencap -p` 的最新画面。

```http
GET /api/android/devices
POST /api/android/reconnect/{device}
WebSocket /ws/android/{device}
```

### 端口占用检查

打开 `/tools/port-checker` 输入端口号即可检查本机 TCP 和 UDP 占用情况。Windows 环境通过 `netstat -ano` 获取 PID，再读取对应进程名称；其他环境会退回 .NET 的监听端口枚举。

```http
POST /api/port/check
POST /api/port/terminate
Content-Type: application/json
```

请求示例：

```json
{
  "port": 5190
}
```

终止进程请求示例：

```json
{
  "port": 5190,
  "processId": 1234
}
```

服务端会在终止前重新确认 PID 仍占用指定端口，确认成功后终止整个进程树；权限不足或进程已经退出时会返回错误信息。

### 协同文本编辑器

打开 `/tools/collaborative-editor` 后，多个浏览器访问同一地址即可共享文本。文本会保存到 `Data/collaborative-editor/content.txt`，上传文件保存在同目录的 `uploads` 下。页面通过原生 WebSocket `/ws/collaborative` 广播文本和文件列表变化，并支持查看历史版本、恢复、复制、下载、删除以及粘贴图片自动上传。

## 依赖注入

服务在 `PersonalWorkbenchAPI/Program.cs` 中注册：

```csharp
builder.Services.AddSingleton<IdentityGeneratorService>();
builder.Services.AddSingleton<SqlCaseConverterService>();
builder.Services.AddSingleton<ToolCatalogService>();
builder.Services.AddSingleton<PdfExpenseMergeService>();
```

Controller 通过构造函数接收服务实例，业务逻辑放在 `Services` 目录中，避免 Controller 直接承担数据处理工作。

## 前端公共能力

前端使用一个 Vue 应用与 Vue Router，各工具通过懒加载进入对应单文件组件。模板和业务逻辑放在同一个 `.vue` 文件中。Vue、Ant Design Vue 和 axios 通过 npm 安装，由 Vite 打包，无需浏览器运行时访问 CDN。

共享能力按职责拆分：

- 统一配置的 axios 请求实例
- 后端错误信息解析，包括 Blob 下载接口的错误响应
- 页面状态对象和 Ant Design 消息反馈
- 剪贴板复制及复制结果反馈
- 文件大小格式化
- Blob 文件下载和响应文件名解析
- multipart 文件表单构造、文件选择器打开和拖拽类型判断
- `src/main.js` 负责 Vue 应用挂载和 Ant Design Vue 注册
- `src/styles/theme.js` 负责暗色主题配置，由根组件统一提供

协同编辑和 Android 监控在离开页面时释放 WebSocket、重连计时器与图片对象 URL，支持 SPA 页面切换。

业务脚本只保留对应工具的状态和操作逻辑，所有 HTTP 请求均通过 axios 调用后端接口。

## 注意事项

- `PersonalWorkbenchAPI/Data/tools.json` 会复制到构建输出和发布目录。
- Android 的 `devices.txt` 位于 `PersonalWorkbenchAPI`，发布时会一起复制。
- 开发时的协同文本和上传文件已随目录整体迁移到 `PersonalWorkbenchAPI/Data/collaborative-editor`；发布时不会自动复制这些运行数据。部署迁移如需保留历史内容，应备份并单独迁移至新发布目录的 `Data/collaborative-editor`。
- `PersonalWorkbenchWeb/dist`、`node_modules`、`artifacts` 均为生成目录，不提交 Git。
- `bin/`、`obj/`、`.idea/` 等构建产物和 IDE 配置不应提交到 Git。
- PDF 文件只在当前请求期间读取和处理，服务不会将上传文件持久化到磁盘。
