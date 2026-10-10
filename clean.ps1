$ErrorActionPreference = 'Stop'

# Remove generated files from this repository only.
$cleanupRoot = [System.IO.Path]::GetFullPath($PSScriptRoot)
$cleanupNames = @(
    'bin'
    'obj'
    '.run-build'
    '.verify-collab'
    'PersonalWorkbenchAPI\bin'
    'PersonalWorkbenchAPI\obj'
    'task_plan.md'
    'findings.md'
    'progress.md'
)

# Validate every target before deleting anything. Do not follow directory links.
$cleanupTargets = foreach ($name in $cleanupNames) {
    $absolute = [System.IO.Path]::GetFullPath((Join-Path $cleanupRoot $name))
    if (-not $absolute.StartsWith($cleanupRoot + '\', [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Target outside repository: $absolute"
    }
    if (-not (Test-Path -LiteralPath $absolute)) { continue }

    $item = Get-Item -LiteralPath $absolute -Force
    if (($item.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0) {
        throw "Refusing to remove a link: $absolute"
    }
    if ($item.PSIsContainer) {
        $links = @(Get-ChildItem -LiteralPath $absolute -Recurse -Force | Where-Object {
            ($_.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0
        })
        if ($links.Count -gt 0) {
            throw "Directory contains links: $absolute"
        }
    }
    $item
}

foreach ($target in $cleanupTargets) {
    if ($target.PSIsContainer) {
        Remove-Item -LiteralPath $target.FullName -Recurse -Force
    } else {
        Remove-Item -LiteralPath $target.FullName -Force
    }
    Write-Host "Removed: $($target.FullName)"
}
