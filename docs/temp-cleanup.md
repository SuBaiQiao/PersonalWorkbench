# 关机临时清理

打开 `/tools/temp-cleanup`，在“设备与监控”分类中进入，也可以搜索“TEMP”“关机”“清理”。功能操作的是 **API 后端所在 Windows 机器、运行后端的交互用户账户**，并非访问网页的设备。自动清理默认未启用，访问页面或重启 API 都不会自动删除临时文件。

## 使用

1. 通过本机 `http://localhost:5500`，或本机 Vite 地址打开工具台。
2. 核对运行账户和完整 TEMP 路径；“预览可清理内容”只统计，不删除。
3. 点击“启用自动清理”，确认目标目录后创建当前账户的 Windows 计划任务。工具台关闭后任务仍保留；停用会删除该工具为当前账户创建的任务，保留执行记录。
4. 如需马上清理，先预览，再点击“立即清理”并确认。清理是永久删除，无法通过本工具恢复。

任务名称为 `PersonalWorkbench-TempCleanup-<当前用户 SID>`，运行级别为普通用户 `LeastPrivilege`，登录类型为 `InteractiveToken`，不保存密码，不作为 SYSTEM 运行。若系统拒绝注册或删除任务，可用相同 Windows 用户以管理员身份运行后端后重试。系统服务账户、Linux 和 macOS 不支持此功能。

## 触发与限制

计划任务订阅 System 日志中 User32 的 **1074** 事件，在正常关机或重启发起时尝试执行。微软说明及示例见：[关机事件与事件触发任务](https://learn.microsoft.com/en-us/troubleshoot/windows-server/system-management-components/identify-cause-of-wmi-shutdown)、[任务运行账户与权限](https://learn.microsoft.com/en-us/windows/win32/taskschd/security-contexts-for-running-tasks)。

这属于关机事件触发的尽力清理，不会阻止或延迟 Windows 关机，也不承诺系统等待所有文件删除完成。Windows 提前结束进程、用户交互会话已经退出、未产生 1074 事件、强制关机或断电时，任务可能无法开始或完成。任务不会在下次开机后补跑。

- 清理器最多处理 20 秒、10 万个项目、64 层目录；计划任务最长运行 30 秒。
- 仅使用当前进程的 `%TEMP%`，只接受末级名称为 `Temp` / `Tmp` 的本机绝对路径。UNC、设备路径、数据流、磁盘根目录及非标准目录均拒绝。
- 不跟随符号链接或目录联接；不修改文件权限、不强制关闭占用进程，跳过只读、占用和权限不足的项目。保留 TEMP 根目录，只尝试非递归删除已清空的子目录。
- 自动任务绑定启用时的 TEMP 路径。环境变量发生变化后须重新配置；手动清理同样校验网页确认的路径，避免后端重启切换目录后误清理其他目标。
- 状态目录默认 `%LOCALAPPDATA%\PersonalWorkbench\TempCleanup`，存放独立脚本和 `last-cleanup.json`。该目录必须位于 TEMP 之外。部署后移动工具台不会改变已复制的任务脚本；更新清理器实现后可重新启用以更新脚本。
- 日志显示处理数量、跳过原因示例及完成状态，不读取或上传文件内容。文件大小是成功删除文件的逻辑大小，压缩文件、硬链接或仍有打开句柄的文件不代表立即释放等量物理空间。
- `running` 且任务已经结束表示未能记录完成，不能视为成功；可结合任务返回码判断是否被 Windows 或超时中断。

旧菜单库会进行一次增量登记，保留既有菜单配置；之后人为修改、禁用或删除该菜单，不会在每次启动时被重新覆盖。可在菜单管理中重新添加组件 `TempCleanupView`。

## 接口

| 方法与路径 | 用途 |
| --- | --- |
| `GET /api/temp-cleanup/status` | 读取支持情况、目标账户、任务状态和上次执行记录 |
| `GET /api/temp-cleanup/preview` | 只读扫描，返回数量和大小 |
| `POST /api/temp-cleanup/clean` | `{ "expectedTempPath": "C:\\Users\\User\\AppData\\Local\\Temp" }`，确认路径后立即清理 |
| `PUT /api/temp-cleanup/automation` | `{ "enabled": true, "expectedTempPath": "..." }`，启用或停用任务 |

写入接口要求来自本机回环地址、请求头 `X-Requested-With: XMLHttpRequest`，并检查 Origin 及代理转发来源；不接受网页传入任意清理目录。预览和实际清理均由同一 PowerShell 脚本实现。

## 验证

在仓库根目录运行：

```powershell
powershell.exe -NoLogo -NoProfile -NonInteractive -ExecutionPolicy Bypass -File tests/TempCleanupChecks.ps1
```

检查仅在 `artifacts` 下建立隔离的 TEMP 样例，覆盖普通文件、二进制文件、只读文件、占用文件、目录联接、根路径限制、执行日志、自动模式绑定目录，以及计划任务 XML。不会清理真实 TEMP，不注册、启用或删除系统任务，不触发关机。
