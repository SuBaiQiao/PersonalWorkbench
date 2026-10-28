$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
# 只在仓库 artifacts 下建立独立 TEMP 样例；不注册、启用或删除系统计划任务。
$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$workspace = Join-Path $repoRoot ('artifacts\temp-cleanup-checks-' + [Guid]::NewGuid().ToString('N'))
$tempRoot = Join-Path $workspace 'Temp'
$stateRoot = Join-Path $workspace 'TempCleanup'
$outside = Join-Path $workspace 'outside'
$scriptPath = Join-Path $repoRoot 'PersonalWorkbenchAPI\Scripts\TempCleanup.ps1'
$checks = 0
$lockStream = $null
function Check([bool]$Condition, [string]$Name) {
    if (-not $Condition) { throw ('检查失败：' + $Name) }
    $script:checks += 1
    Write-Host ('通过：' + $Name)
}
function Run-Worker([string]$Mode, [string]$Temp = $tempRoot, [string]$State = $stateRoot, [bool]$ExpectFailure = $false, [string]$Expected = '') {
    $start = New-Object Diagnostics.ProcessStartInfo
    $start.FileName = Join-Path ([Environment]::SystemDirectory) 'WindowsPowerShell\v1.0\powershell.exe'
    $start.UseShellExecute = $false; $start.CreateNoWindow = $true; $start.WindowStyle = 'Hidden'
    $start.RedirectStandardOutput = $true; $start.RedirectStandardError = $true
    $start.StandardOutputEncoding = [Text.Encoding]::UTF8; $start.StandardErrorEncoding = [Text.Encoding]::UTF8
    # 该脚本兼容 Windows PowerShell 5.1，通过受控且不含双引号的测试路径构建参数。
    $start.Arguments = '-NoLogo -NoProfile -NonInteractive -ExecutionPolicy Bypass -File "' + $scriptPath + '" -Action ' + $Mode + ' -StateDirectory "' + $State + '"'
    if ($Expected) { $start.Arguments += ' -Scheduled -ExpectedTempPath "' + $Expected + '"' }
    $start.EnvironmentVariables['TEMP'] = $Temp
    $process = [Diagnostics.Process]::Start($start)
    try {
        $stdout = $process.StandardOutput.ReadToEndAsync(); $stderr = $process.StandardError.ReadToEndAsync()
        if (-not $process.WaitForExit(35000)) { $process.Kill(); throw '测试清理器超时。' }
        $output = $stdout.GetAwaiter().GetResult(); $errorText = $stderr.GetAwaiter().GetResult()
        if (($process.ExitCode -ne 0) -ne $ExpectFailure) { throw "清理器返回异常：$output $errorText" }
        return $output | ConvertFrom-Json
    } finally { $process.Dispose() }
}
try {
    # 仅提取账户校验函数，以模拟任务对象验证 Windows 简写账户名兼容性，不注册任务。
    $workerAst = [Management.Automation.Language.Parser]::ParseFile($scriptPath, [ref]$null, [ref]$null)
    $ownershipFunction = $workerAst.Find({ param($node) $node -is [Management.Automation.Language.FunctionDefinitionAst] -and $node.Name -eq 'Get-OwnedTask' }, $true)
    . ([scriptblock]::Create($ownershipFunction.Extent.Text))
    $script:sid = [Security.Principal.WindowsIdentity]::GetCurrent().User.Value
    $script:marker = 'PersonalWorkbench.TempCleanup.v1'
    $script:taskName = 'Test-Only-No-Registration'
    $fakeTask = [pscustomobject]@{
        Definition = [pscustomobject]@{ RegistrationInfo = [pscustomobject]@{ Source = $script:marker }; Principal = [pscustomobject]@{ UserId = 'Unresolvable-Short-Name' } }
        Xml = '<Task xmlns="http://schemas.microsoft.com/windows/2004/02/mit/task"><Principals><Principal><UserId>' + $script:sid + '</UserId></Principal></Principals></Task>'
    }
    $fakeFolder = [pscustomobject]@{ Task = $fakeTask }
    $fakeFolder | Add-Member -MemberType ScriptMethod -Name GetTask -Value { param($name) return $this.Task }
    Check ($null -ne (Get-OwnedTask $fakeFolder)) '任务 XML 中的 SID 正确时兼容 COM 返回简写账户名'
    $fakeTask.Xml = $fakeTask.Xml.Replace($script:sid, 'S-1-5-18')
    $rejected = $false
    try { $null = Get-OwnedTask $fakeFolder } catch { $rejected = $true }
    Check $rejected '任务实际运行 SID 不一致时仍拒绝操作'
    $fakeTask.Definition.RegistrationInfo.Source = 'Other-Application'
    $rejected = $false
    try { $null = Get-OwnedTask $fakeFolder } catch { $rejected = $true }
    Check $rejected '同名任务缺少本工具标记时仍拒绝操作'
    [void][IO.Directory]::CreateDirectory((Join-Path $tempRoot 'nested'))
    [void][IO.Directory]::CreateDirectory($outside)
    [IO.File]::WriteAllText((Join-Path $tempRoot '中文.txt'), '清理样例')
    [IO.File]::WriteAllText((Join-Path $tempRoot 'nested\nested.txt'), 'nested')
    [IO.File]::WriteAllBytes((Join-Path $tempRoot 'binary.tmp'), [byte[]](0, 1, 2, 3))
    [IO.File]::WriteAllText((Join-Path $tempRoot 'locked.tmp'), 'occupied')
    [IO.File]::WriteAllText((Join-Path $tempRoot 'readonly.tmp'), 'readonly')
    [IO.File]::SetAttributes((Join-Path $tempRoot 'readonly.tmp'), [IO.FileAttributes]::ReadOnly)
    [IO.File]::WriteAllText((Join-Path $outside 'sentinel.txt'), '不得清理')
    $junction = Join-Path $tempRoot 'outside-link'
    $null = New-Item -ItemType Junction -Path $junction -Target $outside
    $lockStream = [IO.File]::Open((Join-Path $tempRoot 'locked.tmp'), 'Open', 'ReadWrite', 'None')
    $preview = Run-Worker 'Preview'
    Check ($preview.files -eq 4 -and $preview.skipped -ge 2) '预览统计普通文件，跳过链接和只读文件'
    Check ([IO.File]::Exists((Join-Path $tempRoot '中文.txt')) -and -not [IO.Directory]::Exists($stateRoot)) '预览不删除文件或创建状态目录'
    $plan = Run-Worker 'Plan'
    [xml]$xml = $plan.xml
    $namespaces = New-Object Xml.XmlNamespaceManager($xml.NameTable)
    $namespaces.AddNamespace('t', 'http://schemas.microsoft.com/windows/2004/02/mit/task')
    Check ($xml.SelectSingleNode('//t:LogonType', $namespaces).InnerText -eq 'InteractiveToken' -and $xml.SelectSingleNode('//t:RunLevel', $namespaces).InnerText -eq 'LeastPrivilege') '自动任务使用普通交互用户，不保存密码、不提权'
    Check ($xml.SelectSingleNode('//t:Subscription', $namespaces).InnerText.Contains('1074') -and $xml.SelectSingleNode('//t:ExecutionTimeLimit', $namespaces).InnerText -eq 'PT30S') '关机事件订阅与最长执行时间正确'
    Check ($xml.SelectSingleNode('//t:StartWhenAvailable', $namespaces).InnerText -eq 'false' -and $xml.SelectSingleNode('//t:Arguments', $namespaces).InnerText.Contains('-ExpectedTempPath')) '不在开机后补跑，自动任务绑定启用时目录'
    Check (-not [IO.Directory]::Exists($stateRoot)) '生成任务预览不会安装任务或创建持久文件'
    # TASK_VALIDATE_ONLY=1 只让 Windows 校验 XML，不创建、更新、启用或删除任何任务。
    $scheduler = New-Object -ComObject 'Schedule.Service'
    $scheduler.Connect()
    $folder = $scheduler.GetFolder('\')
    $userSid = [Security.Principal.WindowsIdentity]::GetCurrent().User.Value
    $null = $folder.RegisterTask(('PersonalWorkbench-TempCleanup-Validate-' + [Guid]::NewGuid().ToString('N')), $plan.xml, 1, $userSid, $null, 3, $null)
    Check $true 'Windows 任务计划程序原生校验 XML 通过（只校验、不注册）'
    $status = Run-Worker 'Status'
    Check ($status.supported -and $status.tempPath -eq $tempRoot) '状态接口返回当前进程 TEMP'
    $result = Run-Worker 'Clean'
    Check ($result.deletedFiles -eq 3 -and $result.deletedFolders -eq 1) '清理普通文件和空子目录'
    Check ([IO.Directory]::Exists($tempRoot) -and [IO.File]::Exists((Join-Path $outside 'sentinel.txt'))) '保留 TEMP 根目录且不跟随目录联接'
    Check ([IO.File]::Exists((Join-Path $tempRoot 'locked.tmp')) -and [IO.File]::Exists((Join-Path $tempRoot 'readonly.tmp'))) '被占用及只读文件保留'
    Check ([IO.File]::Exists((Join-Path $stateRoot 'last-cleanup.json')) -and $result.state -eq 'completed' -and $result.freedBytes -gt 0) '清理结果保存完整记录'
    $lockStream.Dispose(); $lockStream = $null
    [IO.File]::SetAttributes((Join-Path $tempRoot 'readonly.tmp'), [IO.FileAttributes]::Normal)
    $scheduled = Run-Worker 'Clean' -Expected $tempRoot
    Check ($scheduled.trigger -eq 'shutdown' -and $scheduled.deletedFiles -eq 2) '自动模式复用相同清理规则，释放占用后可继续清理'
    $invalid = Join-Path $workspace 'WrongName'
    [void][IO.Directory]::CreateDirectory($invalid)
    [IO.File]::WriteAllText((Join-Path $invalid 'keep.txt'), '保留')
    $failure = Run-Worker 'Clean' -Temp $invalid -ExpectFailure $true
    Check ([IO.File]::Exists((Join-Path $invalid 'keep.txt')) -and $failure.message.Contains('Temp')) '拒绝误配置的非标准 TEMP 路径'
    $failure = Run-Worker 'Clean' -State (Join-Path $tempRoot 'TempCleanup') -ExpectFailure $true
    Check ($failure.message.Contains('TEMP')) '状态目录不能位于待清理 TEMP 内'
    $failure = Run-Worker 'Clean' -Expected (Join-Path $outside 'Temp') -ExpectFailure $true
    Check ($failure.message.Contains('不一致')) '自动模式遇到 TEMP 变更时拒绝清理其他目录'
    $aliasParent = Join-Path $workspace 'alias'
    [void][IO.Directory]::CreateDirectory($aliasParent)
    $aliasRoot = Join-Path $aliasParent 'Temp'
    $null = New-Item -ItemType Junction -Path $aliasRoot -Target $outside
    $failure = Run-Worker 'Clean' -Temp $aliasRoot -ExpectFailure $true
    Check ($failure.message.Contains('链接') -and [IO.File]::Exists((Join-Path $outside 'sentinel.txt'))) '拒绝自身为联接的 TEMP 根目录'
    [IO.Directory]::Delete($aliasRoot)
    Write-Host "全部 $checks 项检查通过；未注册系统计划任务。"
} finally {
    if ($null -ne $lockStream) { $lockStream.Dispose() }
    # 递归删除前验证绝对路径仍位于仓库 artifacts，并先移除专门建立的联接。
    $allowed = [IO.Path]::GetFullPath((Join-Path $repoRoot 'artifacts')).TrimEnd('\') + '\'
    $full = [IO.Path]::GetFullPath($workspace)
    if ($full.StartsWith($allowed, [StringComparison]::OrdinalIgnoreCase) -and [IO.Path]::GetFileName($full).StartsWith('temp-cleanup-checks-')) {
        foreach ($link in @((Join-Path $tempRoot 'outside-link'), (Join-Path $workspace 'alias\Temp'))) {
            if ([IO.Directory]::Exists($link)) { [IO.Directory]::Delete($link) }
        }
        $readonly = Join-Path $tempRoot 'readonly.tmp'
        if ([IO.File]::Exists($readonly)) { [IO.File]::SetAttributes($readonly, [IO.FileAttributes]::Normal) }
        if ([IO.Directory]::Exists($full)) { [IO.Directory]::Delete($full, $true) }
    }
}
