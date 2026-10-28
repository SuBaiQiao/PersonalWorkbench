# PersonalWorkbench

PersonalWorkbench 是一个个人工具箱，使用 Vue 3 + Vite 构建前端，ASP.NET Core 8 提供 API 和 WebSocket。前后端源码分别位于 `PersonalWorkbenchWeb` 和 `PersonalWorkbenchAPI`，同仓库管理，生产环境统一由 ASP.NET Core 托管。

## 功能

- 身份信息生成器：生成仅供开发测试使用的虚构身份信息。
- SQL 驼峰转换器：将 SQL 或命名片段中的驼峰命名转换为大写下划线命名。
- 人民币大写转换器：将阿拉伯数字金额转换为人民币汉字大写，支持整数和两位以内小数。
- 报销发票 PDF 合并：支持一次选择多个 PDF，也支持将 PDF 文件拖动到页面中，再通过接口合并并下载。
- 图片与 Base64 转换器：支持图片转 Base64，以及 Base64 或 Data URL 转图片下载。
- 二维码与条形码生成器：在浏览器内将内容生成为二维码或 CODE128、EAN-13、CODE39 条形码，支持预览、尺寸设置和 PNG 下载。
- Cron 工具箱：生成与解析标准五字段 Cron、含秒的六字段 Quartz，支持小时多选、中文规则说明、时区选择和未来 5/10/20 次执行时间预览；六字段支持日期/星期的 `?`，暂不支持 `L`、`W`、`#` 和年份字段。
- 时间戳工具：秒/毫秒时间戳与日期双向转换、实时当前时间、UTC 与常见时区显示和结果复制；支持负时间戳，校验无效日期和夏令时歧义。
- JSON 工具箱：格式化、压缩、语法错误定位、按需展开树形结构和 JSONPath 查询；不执行查询脚本或过滤表达式。
- 文本差异对比：双栏行级差异、增加/删除高亮、忽略空白或大小写、仅查看差异。
- 正则表达式测试：实时匹配、捕获分组、命名分组和替换预览；独立 Worker 限时执行，避免复杂表达式阻塞页面。
- UUID / 随机生成器：基于 Web Crypto 批量生成 UUID v4、随机字符串、包含所选字符类型的密码和指定范围整数。
- JWT 解析：本地查看 Header / Payload 与 iat、nbf、exp 时间状态，仅解析、不验证签名。
- 数据转代码：JSON 转 C# 实体 / TypeScript 类型，支持嵌套对象、数组样本合并、缺失字段和空值。
- HTTP 请求工具：编辑方法、请求头与原始请求体，查看响应和耗时，支持常用 cURL 命令导入/导出、取消和超时。请求从浏览器直接发出，受 CORS、HTTPS 和浏览器请求头限制，不携带浏览器 Cookie；响应读取上限 2 MB。
- 图片工具箱：浏览器本地裁剪、等比缩放、PNG/JPEG/WebP 转换与质量设置、主要颜色提取和下载；限制 20 MB / 4000 万像素，动图导出为静态图。
- 视频播放器：本地常见视频、完整 m3u8 文件夹、网络 HLS / FLV / TS，支持倍速、快进后退、截图及全屏；通过 FFmpeg 兼容播放 MKV / AVI / WMV 等编码，并接入 RTSP / RTMP 视频源。详见[视频播放器说明](docs/video-player.md)。
- 磁盘文件管理：浏览服务器磁盘目录与文件详情，新建文件夹/文件，预览图片、PDF、音视频，在线编辑文本并检查保存冲突。
- 关机临时清理：可启用 Windows 关机事件任务，限时尝试清理后端账户的 `%TEMP%`，支持预览、手动清理及执行记录。默认未启用，详见[临时清理说明](docs/temp-cleanup.md)。
- Android 集群设备监控：读取 `devices.txt`，通过 ADB 抓取设备屏幕并使用 WebSocket 实时推送到监控大屏。
- 端口占用检查：检查本机 TCP/UDP 端口，并显示占用它的进程名称、PID 和监听状态。
- 协同文本编辑器：通过 WebSocket 在多个浏览器之间同步文本，保留历史版本，并提供临时文件上传、下载、删除和粘贴图片上传。
- 设备状态监控：读取 API 服务所在设备的 CPU、物理内存、显卡和本地磁盘使用情况，支持自动轮询刷新。

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
- Node.js 20.19+ 或 22.12+（建议使用当前 LTS 版本），以及 pnpm 11
- FFmpeg（可选）：视频播放器的服务端兼容播放与 RTSP / RTMP 转流需要可执行的 FFmpeg，包含 `libx264` 和 AAC 编码器；浏览器直接播放不需要它。

### 启动项目

在项目根目录的第一个终端启动 API：

```bash
dotnet run --project PersonalWorkbenchAPI/PersonalWorkbenchAPI.csproj
```

在第二个终端启动前端：

```bash
cd PersonalWorkbenchWeb
pnpm install --frozen-lockfile
pnpm run dev
```

`pnpm-workspace.yaml` 已允许 Vite 所需的 `esbuild` 和 `core-js` 安装脚本：

```powershell
cd PersonalWorkbenchWeb
pnpm install --frozen-lockfile
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

前端依赖由 `PersonalWorkbenchWeb/pnpm-lock.yaml` 锁定，构建脚本会使用冻结安装验证锁文件未漂移。

### 迁移后验证

1. 执行 `./build.ps1`，确认前后端均构建成功。
2. 启动 API 和 Vite，确认首页工具目录与搜索、分类正常，已有功能可正常请求接口及下载文件。
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
        │   └── tools/                # 十个工具的模板与逻辑
        ├── api/                      # axios 实例和错误解析
        ├── utils/                    # 下载、文件、剪贴板、状态能力
        └── styles/                   # 公共样式与 Ant Design 主题
```

## 菜单与分类管理

菜单及分类保存在 SQLite 的 `tool_menus` 和 `menu_categories` 表中。打开顶部 **数据管理 → 菜单管理**（`/settings/menus`）新增、编辑、禁用或删除菜单，维护分类及顺序。名称、路由、功能页面、图标、颜色、说明、关键词、功能标签和显示状态均由数据库加载；首页、侧栏、搜索和页面标题共享目录，保存后立即刷新。

启动时自动执行内置 `Scripts/MenuSchema.sql` 补建菜单表，兼容已有卡牌数据库。仅当菜单表没有任何记录时，在同一事务中执行 `Scripts/InitializeMenus.sql`，载入程序集内置的 `Data/tools.json` 和 `Data/menu-categories.json` 初始数据；已有配置不会被覆盖，全部禁用也不会触发初始化。删除所有菜单后，下次启动会恢复默认菜单。初始化资源随程序集发布，不需要另外复制 JSON/SQL 文件。

前端通过 `GET /api/tools/catalog` 读取启用的菜单及分类，通过数据库中的 `componentKey` 匹配构建时包含的 `views/tools/*View.vue`，动态注册路径。新增业务功能仍需实现对应 Vue 页面并重新构建；在管理页面可以把已有功能绑定到不同菜单路径。菜单管理是固定入口，全部禁用时仍可访问。禁用菜单会隐藏入口，并在下次加载目录时取消对应的动态路由。

菜单维护请求示例：

```json
{
    "name": "SQL 驼峰转换器",
    "route": "/tools/sql-converter",
    "componentKey": "SqlConverterView",
    "category": "develop",
    "order": 20,
    "icon": "code",
    "tone": "indigo",
    "description": "转换 SQL 命名。",
    "keywords": "sql 驼峰 下划线",
    "features": ["SQL 命名转换"],
    "isEnabled": true
}
```

字段说明：

| 字段 | 说明 |
| --- | --- |
| `name` | 首页显示的工具名称 |
| `route` | 站内 Vue Router 路由 |
| `order` | 首页排序号，数值越小越靠前 |
| `icon` | 前端内置图标标识，例如 `code`、`folder`、`clock` |
| `componentKey` | 已构建的功能页面名称，例如 `SqlConverterView` |
| `category` | 已存在的分类标识 |
| `tone` | `blue`、`teal`、`amber`、`rose` 或 `indigo` |
| `description` / `keywords` / `features` | 说明、搜索关键词和功能标签 |
| `isEnabled` | 是否启用菜单 |

接口：`GET /api/tools` 保留启用菜单列表；`GET /api/tools/manage` 返回全部菜单及分类；`POST /api/tools`、`PUT/DELETE /api/tools/{id}` 维护菜单；`POST /api/tools/categories`、`PUT/DELETE /api/tools/categories/{id}` 维护分类。路由必须符合 `/tools/my-tool` 格式且唯一；分类标识创建后不可修改，含有菜单（包括禁用项）的分类不可删除。当前沿用工作台的访问控制方式，菜单启停不作为权限隔离。

回归验证：在根目录执行 `dotnet build tests/MenuCatalogChecks/MenuCatalogChecks.csproj -o artifacts/menu-db-checks`，再执行 `dotnet artifacts/menu-db-checks/MenuCatalogChecks.dll`；前端目录执行 `npm test` 和 `npm run build`。

## API 接口

### 数据库导出与导入

顶部 **数据管理 → 数据库管理**（`/settings/database`）提供当前 SQLite 库的管理页面：

- 表列表与视图浏览、创建表、修改表名、删除表、清空表。
- 表结构与建表 SQL，新增、删除、修改字段名称、类型、默认值及约束。创建表和新增/修改字段默认使用选择表单：字段类型下拉，主键、自增、非空和唯一约束开关，普通/唯一索引选择，默认值类型与内容设置；开启自增自动关联 INTEGER 主键，已有主键时禁止给其他字段再次选择主键。复杂约束自动使用高级 SQL 定义并完整保留，例如 `TEXT CHECK(length(value)>0)`，不会静默转换或丢弃。字段新增/修改在事务中重建表，保留其他字段、表级约束、索引、触发器及自增序号，外键或唯一性校验失败则回滚。
- 索引列表展示索引类型、字段顺序及来源，支持选择一个或多个字段创建普通索引、唯一索引，删除独立索引。字段编辑器内的索引选择只调整独立的非部分单字段索引，保留复合索引、部分索引和约束索引；主键/唯一约束生成的自动索引不可独立删除。复合主键或高级约束通过 SQL 控制台维护。
- 分页查询、按字段排序、全字段文本搜索，新增、修改和删除数据。区分 NULL、空文本与省略/默认值，支持 Base64 BLOB 和 64 位整数。通过 rowid 或复合主键定位，并校验完整原始行快照，发生并发修改时返回 409。
- SQL 控制台支持多条语句及多个结果集、重复列名。每次批处理在同一事务内执行，失败整体回滚；每个结果集最多展示 1000 行（超出会提示），执行限时 30 秒。事务由系统管理，不支持脚本中的 `BEGIN` / `COMMIT` / `SAVEPOINT`、`ATTACH` / `DETACH`，以及需要事务外执行的 `VACUUM`。复杂表级约束可以通过 SQL 创建和维护。
- 操作日志可分页查看，按 SQL、绑定参数、操作类型、错误和状态检索。所有管理连接执行的 SQL（包括查询、表结构检查、PRAGMA、事务、失败语句及控制台完整脚本）均先记录执行意图，再记录完成状态、耗时、影响行数、错误、请求 ID 和来源 IP。多条 SQL 脚本作为一条完整日志留存；同一请求 ID 关联事务内的多条管理语句，最终提交情况查看对应的 `COMMIT` / `ROLLBACK`。`running` 表示结果尚未记录，可能仍在执行或服务曾中断。

日志独立保存在 API 内容目录的 `Data/database-audit.db`，管理连接无法附加其他数据库，删除/清空业务表、导入业务数据库和重启都不会清除日志。审计存储自身的读写不递归记录。该日志文件不包含在业务库导出或发布包中，需要随部署数据单独备份和迁移；默认不自动清理。无法预先写入日志时拒绝执行 SQL。部署运行账户需要能写入该目录。

管理 API 前缀为 `/api/database/manage`：`GET tables/structure/rows/logs`；`POST sql/tables/columns/rows/clear/delete-row/indexes`；`PUT table/column/row`；`DELETE table/column/index`。目标表、字段通过 `table`、`column` 查询参数传入，写入需 JSON 和 `X-Requested-With: XMLHttpRequest`。数据库管理沿用现有工作台访问控制，能访问该 API 的客户端具有当前库的结构和数据管理能力。

验证：根目录执行 `dotnet build tests/DatabaseManagementChecks/DatabaseManagementChecks.csproj -o artifacts/database-management-checks` 后执行 `dotnet artifacts/database-management-checks/DatabaseManagementChecks.dll`；前端目录执行 `npm test` 和 `npm run build`。检查使用独立业务库和审计目录，不修改日常使用的数据库。

数据库功能使用 SQLite，卡牌收集数据默认保存在 `PersonalWorkbenchAPI/Data/personal-workbench.db`。

```http
GET /api/database/export
POST /api/database/import
Content-Type: multipart/form-data
```

导出接口返回完整的 SQLite 数据库备份文件，包含菜单、分类和卡牌数据。导入接口的表单字段名为 `file`，导入前会执行 SQLite 完整性检查，并校验当前应用所需的数据表；旧版没有菜单表的备份会先在临时库中补表并初始化菜单，校验通过后整体覆盖当前数据库，保留备份中的全部表、索引和数据。页面中的导入操作会先要求确认，建议覆盖前先导出当前数据库。

### 加密工具箱

打开 `/tools/crypto` 使用加密工具箱，支持：

- MD5 文本摘要
- Base64 文本编码与解码（UTF-8，支持中文和表情符号）
- URL 组件编码与解码（UTF-8，空格编码为 `%20`，加号编码为 `%2B`，解码保留原始加号）
- AES 对称加密（CBC、PKCS7，Base64 密钥和 IV）
- RSA 非对称加密（RSA-2048、OAEP-SHA256、PEM 密钥）
- SM2 公钥加密（`sm2p256v1`，十六进制密钥）
- SM4 对称加密（CBC、PKCS7，十六进制密钥和 IV）

RSA 和 SM2 页面可生成密钥对，AES 和 SM4 页面可生成随机密钥与 IV。

### 获取行政区划数据

```http
GET /api/identity/areas
```

返回全国 34 个省级地区的省、市、区县三级数据，支持直辖市、省直辖县级行政区划和不设区县的城市。数据随程序集内置，运行时无需联网；来源、固定版本和更新方法见 [区划数据说明](PersonalWorkbenchAPI/Data/china-areas.README.md)。

省级节点的 `supportsIdentityGeneration` 表示是否支持生成大陆居民身份证号码。大陆 31 个省级地区为 `true`；港澳台保留数据源自定义编码用于区划浏览，为 `false`，不参与随机生成，指定这些地区生成时返回 400。

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

### 人民币大写转换

```http
POST /api/amount/convert
Content-Type: application/json
```

请求示例：

```json
{
  "inputAmount": "1234.56"
}
```

响应示例：

```json
{
  "outputAmount": "壹仟贰佰叁拾肆元伍角陆分"
}
```

输入支持整数和最多两位小数，整数金额会以“整”结尾。

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

`frequency` 支持 `minutely`、`hourly`、`daily`、`weekly` 和 `monthly`。接口返回 Cron 表达式和中文规则说明。`format` 默认为 `standard`，生成五字段格式：`分钟 小时 日期 月份 星期`；设为 `quartz` 时生成六字段格式：`秒 分钟 小时 日期 月份 星期`，可通过 `second` 指定 0–59 秒。

`daily`、`weekly` 和 `monthly` 可通过 `hours` 数组选择多个小时，共用相同的分钟和秒；不传 `hours` 时继续使用原有的 `hour`。例如每天 01:09:00 和 12:09:00 只需请求一次：

```json
{
  "format": "quartz",
  "frequency": "daily",
  "hours": [1, 12],
  "minute": 9,
  "second": 0
}
```

结果为 `0 9 1,12 * * ?`；改用 `standard` 则为 `9 1,12 * * *`。五字段仅支持第 0 秒执行。请求的 `dayOfWeek` 始终使用 0=星期日、1=星期一至 6=星期六，服务在生成 Quartz 时自动转换成 1=星期日至 7=星期六，日期与星期有且仅有一项使用 `?`。解析预览也使用相同的格式约定；原有五字段的日期与星期仍按「或」匹配。

Cron 回归检查：在前端目录执行 `node --test tests/cron.test.js tests/time.test.js`，在仓库根目录执行 `dotnet run --project tests/CronGeneratorChecks/CronGeneratorChecks.csproj`。

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

### 设备状态监控

打开 `/tools/system-monitor` 查看 API 服务所在设备的实时状态。接口默认每 5 秒由页面轮询一次，也可以暂停或手动刷新：

```http
GET /api/system/status
```

返回 CPU 使用率、物理内存使用率、显卡适配器信息、显卡驱动可提供的使用率/显存/温度，以及本地固定和可移动磁盘的容量、已用空间和剩余空间。Windows 会优先使用系统计数器；安装 NVIDIA 驱动和 `nvidia-smi` 时可显示 NVIDIA 单卡实时指标。驱动或权限不支持的字段返回 `null`，页面显示“不可用”。

Linux 的 CPU 与物理内存通过 `/proc` 读取，显卡目前支持 `nvidia-smi`。其他系统保留磁盘读取，不支持的 CPU/内存字段返回 `null` 和提示。多页面请求共享至少 3 秒的采样缓存，退出页面会停止刷新并取消未完成请求。

CPU 卡片的“查看详情”按钮打开详情弹窗，通过 `GET /api/system/cpu` 按需读取每个 CPU 插槽的型号、制造商、核心/线程数、系统报告频率、缓存容量和固件虚拟化状态，并展示各逻辑处理器约 0.5 秒采样窗口内的使用率。详情跟随页面刷新设置更新，关闭弹窗会停止请求。Windows 使用 CIM，Linux 使用 `/proc` 和可用的 CPU 频率文件；Linux 暂不提供缓存容量和固件虚拟化状态。不支持的指标显示“不可用”。

显卡卡片的“查看详情”按钮打开显卡详情弹窗：

```http
GET /api/system/gpu
```

NVIDIA 驱动可提供 GPU/显存使用率、总量与已用量、温度、风扇、功耗、时钟、性能状态、计算模式、VBIOS、PCIe 链路、ECC/时钟限制和显卡进程显存。Windows 还会尝试读取 GPU 引擎、适配器显存和进程显存性能计数器；驱动、系统版本或权限不支持的字段返回 `null`，页面显示“不可用”。

点击“查看全部进程”可以查看：

```http
GET /api/system/processes
```

列表按约 0.75 秒窗口采样每个进程的 CPU、工作集/专用/虚拟/分页内存、磁盘读写速率、GPU 引擎/显存（驱动支持时）、线程、句柄、会话、父进程、启动时间、可执行文件路径和 Windows TCP/UDP 连接数，并支持搜索、排序和分页。进程网络字节速率使用 Windows ETW TCP/UDP 事件按 PID 统计，服务需要管理员权限才能启动内核网络采集会话；权限不足、Linux/macOS 或系统不支持时显示不可用，不会用网卡总流量冒充进程带宽。进程列表和显卡详情都使用短缓存，关闭弹窗会停止刷新。

### 协同文本编辑器

打开 `/tools/collaborative-editor` 后，多个浏览器访问同一地址即可共享文本。文本会保存到 `Data/collaborative-editor/content.txt`，上传文件保存在同目录的 `uploads` 下。页面通过原生 WebSocket `/ws/collaborative` 广播文本和文件列表变化，并支持查看历史版本、恢复、复制、下载、删除以及粘贴图片自动上传。

### 磁盘文件管理

打开 `/tools/file-manager` 浏览 **API 所在机器**的磁盘目录。App 式菜单和左侧菜单都提供入口，并适配浅色、深色外观与手机屏幕。

- 展示磁盘容量、文件夹与文件名称、类型、大小、创建/修改时间、完整路径及文件属性。
- 支持地址栏、目录面包屑、返回上级、刷新、当前目录名称搜索、隐藏文件、排序和分页。
- 支持新建文件夹和空文件；文本文件可预览、在线编辑、快捷键保存与下载。图片、PDF、音视频使用浏览器预览，其他格式提供下载。
- 文本默认最多 2 MB，识别 UTF-8 / UTF-16 / UTF-32 BOM，可手动选择 GB18030 / GBK 等编码重新读取。保存可选择编码、BOM、换行方式；默认保留原文件设置。
- 保存前校验原内容的 SHA-256 版本，检测到其他页面或程序已修改时返回 409 并保留编辑内容。同目录临时文件完整写入后再替换原文件，避免写入失败截断原文。

在 `PersonalWorkbenchAPI/appsettings.json` 配置访问范围：

```json
"FileManager": {
  "Roots": ["D:\\Shared", "D:\\Documents"],
  "ReadOnly": false,
  "MaxTextBytes": 2097152
}
```

`Roots` 为空时列出服务端已就绪的本地固定磁盘、移动磁盘与内存盘；指定目录时只能访问这些目录及其子目录。`ReadOnly` 禁止新建与保存。操作始终受 API 运行账户的系统权限约束，不提权；不进入符号链接/目录联接，不支持 UNC 共享、设备路径与 NTFS 数据流。文件夹大小不递归统计，搜索只查找当前层。HTML / SVG 作为纯文本或附件处理，不在同源页面中执行；浏览器预览能力取决于其格式和解码器支持。

当前工作台没有用户登录隔离，能访问文件管理 API 的客户端就拥有配置范围内的文件访问能力。部署到共享网络时应限定 `Roots`，并通过部署层的认证控制访问；可设置 `ReadOnly: true` 提供只读浏览。

接口：`GET /api/files/roots`、`GET /api/files/list`、`GET /api/files/details`、`POST /api/files/entries`、`GET/PUT /api/files/text`、`GET /api/files/content`。查询参数中的 `path` 使用完整服务器路径。写入请求需 JSON 格式及 `X-Requested-With: XMLHttpRequest`；文本保存必须带上读取接口返回的 `version`。内容接口支持 HTTP Range，文件列表和文本响应不缓存。

后端检查：`dotnet run --project tests/FileManagerChecks/FileManagerChecks.csproj`。若常规构建输出被正在运行的服务占用，可用 `dotnet build tests/FileManagerChecks/FileManagerChecks.csproj -o artifacts/file-manager-checks` 后执行 `dotnet artifacts/file-manager-checks/FileManagerChecks.dll`。

## 依赖注入

服务在 `PersonalWorkbenchAPI/Program.cs` 中注册：

```csharp
builder.Services.AddSingleton<IdentityGeneratorService>();
builder.Services.AddSingleton<SqlCaseConverterService>();
builder.Services.AddScoped<ToolCatalogService>();
builder.Services.AddSingleton<PdfExpenseMergeService>();
```

Controller 通过构造函数接收服务实例，业务逻辑放在 `Services` 目录中，避免 Controller 直接承担数据处理工作。

## 前端公共能力

前端使用一个 Vue 应用与 Vue Router，各工具通过懒加载进入对应单文件组件。模板和业务逻辑放在同一个 `.vue` 文件中。Vue、Ant Design Vue 和 axios 通过 npm 安装，由 Vite 打包，无需浏览器运行时访问 CDN。

顶部中间提供统一工具搜索，按名称、说明、功能、分类和关联词查找，首页卡片与侧栏同步筛选。例如“加密”可关联 MD5、Base64、AES 等功能，“MD5”则直接定位支持该能力的工具。结果支持方向键选择、Enter 打开、Esc 关闭；输入法确认文字时不会误跳转。搜索词只保存在当前页面会话中，不发送给后端，也不写入浏览器存储。

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

- `PersonalWorkbenchAPI/Data/tools.json` 与 `menu-categories.json` 仅作空菜单初始化数据，已嵌入程序集；日常维护通过菜单管理页面或数据库接口完成。
- Android 的 `devices.txt` 位于 `PersonalWorkbenchAPI`，发布时会一起复制。
- 开发时的协同文本和上传文件已随目录整体迁移到 `PersonalWorkbenchAPI/Data/collaborative-editor`；发布时不会自动复制这些运行数据。部署迁移如需保留历史内容，应备份并单独迁移至新发布目录的 `Data/collaborative-editor`。
- `PersonalWorkbenchWeb/dist`、`node_modules`、`artifacts` 均为生成目录，不提交 Git。
- `bin/`、`obj/`、`.idea/` 等构建产物和 IDE 配置不应提交到 Git。
- PDF 文件只在当前请求期间读取和处理，服务不会将上传文件持久化到磁盘。
