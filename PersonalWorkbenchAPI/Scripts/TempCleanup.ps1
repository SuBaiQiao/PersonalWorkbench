[CmdletBinding()]
param(
    [ValidateSet('Status', 'Preview', 'Clean', 'Enable', 'Disable', 'Plan')]
    [string]$Action = 'Status',
    [string]$StateDirectory = (Join-Path $env:LOCALAPPDATA 'PersonalWorkbench\TempCleanup'),
    [string]$ExpectedTempPath,
    [switch]$Scheduled
)
# PersonalWorkbench.TempCleanup.v1
# 同一脚本用于预览、手动清理和计划任务，避免两套删除规则出现差异。兼容 Windows PowerShell 5.1。
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = New-Object System.Text.UTF8Encoding($false)
$OutputEncoding = [Console]::OutputEncoding
$marker = 'PersonalWorkbench.TempCleanup.v1'
$utf8 = New-Object System.Text.UTF8Encoding($true)
$mutex = $null
$locked = $false

function Full-Path([string]$Value) {
    if ([string]::IsNullOrWhiteSpace($Value) -or $Value -notmatch '^[A-Za-z]:[\\/]' -or $Value.StartsWith('\\')) {
        throw '仅支持本机磁盘上的完整路径。'
    }
    $full = [IO.Path]::GetFullPath($Value).TrimEnd([char[]]'\/')
    if ($full.Length -le 3 -or $full.IndexOf(':', 2) -ge 0) { throw '拒绝磁盘根目录、设备路径或数据流路径。' }
    return $full
}
function Is-Within([string]$Value, [string]$Root) {
    return $Value.Equals($Root, [StringComparison]::OrdinalIgnoreCase) -or $Value.StartsWith($Root + '\', [StringComparison]::OrdinalIgnoreCase)
}
function Assert-LocalDrive([string]$Value) {
    $drive = New-Object IO.DriveInfo([IO.Path]::GetPathRoot($Value))
    if ($drive.DriveType -notin @([IO.DriveType]::Fixed, [IO.DriveType]::Removable, [IO.DriveType]::Ram)) {
        throw '仅允许本地固定磁盘、移动磁盘或内存盘，拒绝映射网络驱动器。'
    }
}
function Assert-NoLinks([string]$Value) {
    $current = $Value
    while ($current) {
        try {
            if (([IO.File]::GetAttributes($current) -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
                throw '路径包含符号链接或目录联接，已拒绝处理。'
            }
        } catch [IO.FileNotFoundException] { } catch [IO.DirectoryNotFoundException] { }
        $parent = [IO.Path]::GetDirectoryName($current)
        if ($parent -eq $current) { break }
        $current = $parent
    }
}
function Assert-TempEntry([string]$Value) {
    $full = Full-Path $Value
    if (-not (Is-Within $full $script:tempRoot)) { throw '路径越出当前 TEMP 目录，已停止处理。' }
    Assert-NoLinks $full
    return $full
}
function Ensure-StateDirectory {
    Assert-NoLinks $script:stateRoot
    [void][IO.Directory]::CreateDirectory($script:stateRoot)
}
function Write-RunLog($Value) {
    Ensure-StateDirectory
    $temporary = Join-Path $script:stateRoot ('.cleanup-log-' + [Guid]::NewGuid().ToString('N') + '.tmp')
    try {
        [IO.File]::WriteAllText($temporary, ($Value | ConvertTo-Json -Depth 8 -Compress), $script:utf8)
        Assert-NoLinks $script:logPath
        if ([IO.File]::Exists($script:logPath)) { [IO.File]::Replace($temporary, $script:logPath, [System.Management.Automation.Language.NullString]::Value) }
        else { [IO.File]::Move($temporary, $script:logPath) }
    } finally {
        # 仅清理本次在专属状态目录下创建的单个临时文件，不递归清理状态目录。
        if ([IO.File]::Exists($temporary)) { Assert-NoLinks $temporary; [IO.File]::Delete($temporary) }
    }
}
function Read-RunLog {
    if (-not [IO.File]::Exists($script:logPath)) { return $null }
    Assert-NoLinks $script:logPath
    if ((New-Object IO.FileInfo($script:logPath)).Length -gt 131072) { return $null }
    try { return [IO.File]::ReadAllText($script:logPath, [Text.Encoding]::UTF8) | ConvertFrom-Json }
    catch { return $null }
}
function New-TaskXml {
    $subscription = '<QueryList><Query Id="0" Path="System"><Select Path="System">*[System[Provider[@Name=''User32''] and (EventID=1074)]]</Select></Query></QueryList>'
    $arguments = '-NoLogo -NoProfile -NonInteractive -WindowStyle Hidden -ExecutionPolicy Bypass -File "' + $script:workerPath + '" -Action Clean -StateDirectory "' + $script:stateRoot + '" -ExpectedTempPath "' + $script:tempRoot + '" -Scheduled'
    $escapedScript = [Security.SecurityElement]::Escape($script:powershellPath)
    $escapedArguments = [Security.SecurityElement]::Escape($arguments)
    $escapedSubscription = [Security.SecurityElement]::Escape($subscription)
    # 普通用户交互令牌运行，不保存密码、不提升到 SYSTEM，不在开机后补跑过期的清理。
    return @"
<?xml version="1.0" encoding="UTF-16"?>
<Task version="1.3" xmlns="http://schemas.microsoft.com/windows/2004/02/mit/task">
  <RegistrationInfo><Source>$script:marker</Source><Description>正常关机或重启发起时，限时清理当前用户 TEMP；占用、只读和链接项跳过。</Description></RegistrationInfo>
  <Triggers><EventTrigger><Enabled>true</Enabled><Subscription>$escapedSubscription</Subscription></EventTrigger></Triggers>
  <Principals><Principal id="User"><UserId>$script:sid</UserId><LogonType>InteractiveToken</LogonType><RunLevel>LeastPrivilege</RunLevel></Principal></Principals>
  <Settings><MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy><DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries><StopIfGoingOnBatteries>false</StopIfGoingOnBatteries><AllowHardTerminate>true</AllowHardTerminate><StartWhenAvailable>false</StartWhenAvailable><AllowStartOnDemand>true</AllowStartOnDemand><Enabled>true</Enabled><Hidden>true</Hidden><ExecutionTimeLimit>PT30S</ExecutionTimeLimit></Settings>
  <Actions Context="User"><Exec><Command>$escapedScript</Command><Arguments>$escapedArguments</Arguments></Exec></Actions>
</Task>
"@
}
function Get-TaskFolder {
    $scheduler = New-Object -ComObject 'Schedule.Service'
    $scheduler.Connect()
    return $scheduler.GetFolder('\')
}
function Get-OwnedTask($Folder) {
    try { $task = $Folder.GetTask($script:taskName) }
    catch {
        $failure = $_.Exception
        while ($null -ne $failure.InnerException) { $failure = $failure.InnerException }
        if (($failure.HResult -band 0xffff) -eq 2) { return $null }
        throw
    }
    if ($task.Definition.RegistrationInfo.Source -ne $script:marker) { throw '同名计划任务不属于本工具，已拒绝修改。' }
    # COM 可能把 SID 转为不含机器名的简写账户名；优先读取任务 XML 中保留的 SID，
    # 避免简写名解析失败或等待域控制器，同时仍严格核对任务实际运行账户。
    [xml]$taskDocument = $task.Xml
    $taskNamespaces = New-Object Xml.XmlNamespaceManager($taskDocument.NameTable)
    $taskNamespaces.AddNamespace('t', 'http://schemas.microsoft.com/windows/2004/02/mit/task')
    $principalNode = $taskDocument.SelectSingleNode('/t:Task/t:Principals/t:Principal/t:UserId', $taskNamespaces)
    $principalId = if ($null -ne $principalNode) { $principalNode.InnerText } else { $task.Definition.Principal.UserId }
    if ($principalId -ne $script:sid) {
        if ($principalId -match '^S-1-\d+(?:-\d+)+$') { throw '同名任务属于其他账户，已拒绝修改。' }
        try { $principalId = (New-Object Security.Principal.NTAccount($principalId)).Translate([Security.Principal.SecurityIdentifier]).Value }
        catch { throw '无法确认同名任务的运行账户，已拒绝修改。' }
        if ($principalId -ne $script:sid) { throw '同名任务属于其他账户，已拒绝修改。' }
    }
    return $task
}
function Get-Status {
    $status = [ordered]@{ supported = $true; reason = $null; machineName = $env:COMPUTERNAME; account = $script:identity.Name; tempPath = $script:tempRoot;
        stateDirectory = $script:stateRoot; automationSupported = $true; autoEnabled = $false; taskName = $script:taskName; taskState = '未安装';
        lastTaskRunUtc = $null; lastTaskResult = $null; lastCleanup = (Read-RunLog) }
    try {
        $task = Get-OwnedTask (Get-TaskFolder)
        if ($null -ne $task) {
            $status.autoEnabled = [bool]$task.Enabled
            $status.taskState = switch ([int]$task.State) { 1 { '已禁用' } 2 { '排队中' } 3 { '已就绪' } 4 { '正在运行' } default { '未知' } }
            if ($task.LastRunTime.Year -gt 2000) { $status.lastTaskRunUtc = $task.LastRunTime.ToUniversalTime().ToString('o'); $status.lastTaskResult = [long]$task.LastTaskResult }
        }
    } catch { $status.automationSupported = $false; $status.reason = '无法访问任务计划程序：' + $_.Exception.Message }
    return $status
}
function Invoke-Cleanup([bool]$Preview) {
    $clock = [Diagnostics.Stopwatch]::StartNew()
    $stats = [ordered]@{ tempPath = $script:tempRoot; startedUtc = [DateTime]::UtcNow.ToString('o'); completedUtc = $null; trigger = $(if ($Scheduled) { 'shutdown' } elseif ($Preview) { 'preview' } else { 'manual' });
        state = 'running'; inspected = 0; files = 0; folders = 0; bytes = [long]0; deletedFiles = 0; deletedFolders = 0; freedBytes = [long]0; skipped = 0; limited = $false; errors = @() }
    if (-not $Preview) { Write-RunLog $stats }
    $failures = New-Object 'Collections.Generic.List[string]'
    $stack = New-Object 'Collections.Generic.Stack[object]'
    $stack.Push([pscustomobject]@{ path = $script:tempRoot; exit = $false; depth = 0 })
    while ($stack.Count -gt 0) {
        if ($clock.Elapsed.TotalSeconds -ge 20 -or $stats.inspected -ge 100000) { $stats.limited = $true; break }
        $item = $stack.Pop()
        try {
            $directory = Assert-TempEntry $item.path
            if ($item.exit) {
                # 从不删除 TEMP 根目录；子目录也只尝试非递归删除，未清理的文件会保留下来。
                if (-not $Preview -and $directory -ne $script:tempRoot) {
                    try { [IO.Directory]::Delete((Assert-TempEntry $directory), $false); $stats.deletedFolders += 1 }
                    catch [IO.IOException] { } catch [UnauthorizedAccessException] { }
                }
                continue
            }
            if ($item.depth -ge 64) { $stats.skipped += 1; continue }
            $stack.Push([pscustomobject]@{ path = $directory; exit = $true; depth = $item.depth })
            $enumerator = [IO.Directory]::EnumerateFileSystemEntries($directory).GetEnumerator()
            try {
                while ($enumerator.MoveNext()) {
                    if ($clock.Elapsed.TotalSeconds -ge 20 -or $stats.inspected -ge 100000) { $stats.limited = $true; break }
                    $stats.inspected += 1
                    try {
                        $entry = Assert-TempEntry $enumerator.Current
                        $attributes = [IO.File]::GetAttributes($entry)
                        if (($attributes -band [IO.FileAttributes]::Directory) -ne 0) {
                            $stats.folders += 1
                            $stack.Push([pscustomobject]@{ path = $entry; exit = $false; depth = $item.depth + 1 })
                            continue
                        }
                        if (($attributes -band [IO.FileAttributes]::ReadOnly) -ne 0) { $stats.skipped += 1; continue }
                        $length = (New-Object IO.FileInfo($entry)).Length
                        $stats.files += 1; $stats.bytes += $length
                        if (-not $Preview) {
                            [IO.File]::Delete((Assert-TempEntry $entry))
                            $stats.deletedFiles += 1; $stats.freedBytes += $length
                        }
                    } catch {
                        $stats.skipped += 1
                        if ($failures.Count -lt 5) { $failures.Add($_.Exception.Message) }
                    }
                }
            } finally { if ($enumerator -is [IDisposable]) { $enumerator.Dispose() } }
        } catch {
            $stats.skipped += 1
            if ($failures.Count -lt 5) { $failures.Add($_.Exception.Message) }
        }
        if ($stats.limited) { break }
    }
    $stats.completedUtc = [DateTime]::UtcNow.ToString('o')
    $stats.state = $(if ($Preview) { 'preview' } elseif ($stats.limited) { 'limited' } else { 'completed' })
    $stats.errors = $failures.ToArray()
    if (-not $Preview) { Write-RunLog $stats }
    return $stats
}

try {
    if ($env:OS -ne 'Windows_NT') { throw '该功能仅支持 Windows。' }
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    if ($identity.IsSystem -or -not [Environment]::UserInteractive) { throw '请使用交互式 Windows 用户运行后端，系统服务账户不支持此功能。' }
    $sid = $identity.User.Value
    $tempRoot = Full-Path $env:TEMP
    if (($Scheduled -or -not [string]::IsNullOrWhiteSpace($ExpectedTempPath)) -and ([string]::IsNullOrWhiteSpace($ExpectedTempPath) -or -not $tempRoot.Equals((Full-Path $ExpectedTempPath), [StringComparison]::OrdinalIgnoreCase))) {
        throw '当前 TEMP 与确认时的目录不一致，请刷新状态或重新配置自动清理。'
    }
    # 仅接受当前进程 TEMP，且末级目录必须叫 Temp/Tmp，防止环境变量被误设为整个用户目录。
    if ([IO.Path]::GetFileName($tempRoot) -notin @('Temp', 'Tmp')) { throw 'TEMP 末级目录必须是 Temp 或 Tmp；为避免误删，已拒绝非标准临时目录。' }
    Assert-LocalDrive $tempRoot
    Assert-NoLinks $tempRoot
    if (-not [IO.Directory]::Exists($tempRoot)) { throw 'TEMP 目录不存在。' }
    $stateRoot = Full-Path $StateDirectory
    if ([IO.Path]::GetFileName($stateRoot) -ne 'TempCleanup' -or (Is-Within $stateRoot $tempRoot)) { throw '状态目录必须为 TEMP 之外的专属 TempCleanup 文件夹。' }
    Assert-LocalDrive $stateRoot
    Assert-NoLinks $stateRoot
    $logPath = Join-Path $stateRoot 'last-cleanup.json'
    $workerPath = Join-Path $stateRoot 'workbench-temp-cleanup.ps1'
    $powershellPath = Join-Path ([Environment]::SystemDirectory) 'WindowsPowerShell\v1.0\powershell.exe'
    $taskName = 'PersonalWorkbench-TempCleanup-' + $sid
    $mutex = New-Object Threading.Mutex($false, ('Global\PersonalWorkbench.TempCleanup.' + $sid))
    try { $locked = $mutex.WaitOne(0) } catch [Threading.AbandonedMutexException] { $locked = $true }
    if (-not $locked) { throw '已有临时文件操作正在进行，请稍后重试。' }
    $result = switch ($Action) {
        'Status' { Get-Status }
        'Preview' { Invoke-Cleanup $true }
        'Clean' { Invoke-Cleanup $false }
        'Plan' { @{ taskName = $taskName; xml = (New-TaskXml); tempPath = $tempRoot; workerPath = $workerPath } }
        'Enable' {
            $folder = Get-TaskFolder
            $null = Get-OwnedTask $folder
            Ensure-StateDirectory
            Assert-NoLinks $workerPath
            if ([IO.File]::Exists($workerPath) -and -not [IO.File]::ReadAllText($workerPath).Contains($marker)) { throw '状态目录中存在不属于本工具的同名文件，已拒绝覆盖。' }
            if (-not $PSCommandPath.Equals($workerPath, [StringComparison]::OrdinalIgnoreCase)) { [IO.File]::Copy($PSCommandPath, $workerPath, $true) }
            $null = $folder.RegisterTask($taskName, (New-TaskXml), 6, $sid, $null, 3, $null)
            Get-Status
        }
        'Disable' {
            $folder = Get-TaskFolder
            if ($null -ne (Get-OwnedTask $folder)) { $folder.DeleteTask($taskName, 0) }
            Get-Status
        }
    }
    [Console]::WriteLine(($result | ConvertTo-Json -Depth 10 -Compress))
} catch {
    [Console]::WriteLine((@{ message = $_.Exception.Message } | ConvertTo-Json -Compress))
    exit 1
} finally {
    if ($locked) { $mutex.ReleaseMutex() }
    if ($null -ne $mutex) { $mutex.Dispose() }
}
