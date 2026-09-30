# PersonalWorkbench

PersonalWorkbench 是一个基于 ASP.NET Core 8 Razor Pages 的个人工具箱。项目采用“Razor Pages 页面 + API Controller + Service 业务服务”的结构，首页通过 JSON 配置展示可用工具。

## 功能

- 身份信息生成器：生成仅供开发测试使用的虚构身份信息。
- SQL 驼峰转换器：将 SQL 或命名片段中的驼峰命名转换为大写下划线命名。
- 报销发票 PDF 合并：支持一次选择多个 PDF，也支持将 PDF 文件拖动到页面中，再通过接口合并并下载。
- 图片与 Base64 转换器：支持图片转 Base64，以及 Base64 或 Data URL 转图片下载。

## 技术栈

- .NET 8
- ASP.NET Core Razor Pages
- ASP.NET Core Web API Controller
- `PdfSharpCore 1.3.67`
- 前端使用 Razor、原生 JavaScript 和 CSS，没有额外的前端框架依赖

## 快速开始

### 环境要求

- .NET 8 SDK

### 启动项目

在项目根目录执行：

```bash
dotnet restore
dotnet run --project PersonalWorkbench.csproj
```

开发环境默认地址为：

```text
http://localhost:5190
```

也可以使用以下命令验证项目：

```bash
dotnet build PersonalWorkbench.csproj
```

## 目录结构

```text
PersonalWorkbench/
├── Controllers/              # API Controller，只负责 HTTP 请求和响应
├── Data/
│   └── tools.json             # 首页工具目录配置
├── Models/                   # 请求、响应和领域模型
├── Pages/                    # Razor Pages 页面
│   ├── Index.cshtml           # 官网首页
│   └── Tools/                 # 各个工具页面
├── Services/                 # 业务逻辑服务
├── wwwroot/
│   ├── css/                   # 全局样式
│   └── js/                    # 各工具页面的前端脚本
├── Program.cs                # 应用启动、依赖注入和路由配置
├── PersonalWorkbench.csproj  # 项目文件和 NuGet 依赖
└── PersonalWorkbench.sln     # Visual Studio、Rider 解决方案文件
```

## 首页工具配置

首页工具由 `Data/tools.json` 配置，不需要修改首页 Razor 页面即可增加或调整工具：

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
| `route` | 站内 Razor Page 路由 |
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

## 依赖注入

服务在 `Program.cs` 中注册：

```csharp
builder.Services.AddSingleton<IdentityGeneratorService>();
builder.Services.AddSingleton<SqlCaseConverterService>();
builder.Services.AddSingleton<ToolCatalogService>();
builder.Services.AddSingleton<PdfExpenseMergeService>();
```

Controller 通过构造函数接收服务实例，业务逻辑放在 `Services` 目录中，避免 Controller 直接承担数据处理工作。

## 注意事项

- `Data/tools.json` 会复制到构建输出目录，修改配置后重新启动应用即可生效。
- `bin/`、`obj/`、`.idea/` 等构建产物和 IDE 配置不应提交到 Git。
- PDF 文件只在当前请求期间读取和处理，服务不会将上传文件持久化到磁盘。
