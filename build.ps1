param(
    [ValidateSet('Build', 'Publish')]
    [string]$Mode = 'Build'
)

$ErrorActionPreference = 'Stop'
Push-Location $PSScriptRoot
try {
    if ($Mode -eq 'Publish') {
        # The API publish target installs and builds the frontend automatically.
        & dotnet publish PersonalWorkbenchAPI/PersonalWorkbenchAPI.csproj -c Release -o artifacts/publish
        if ($LASTEXITCODE -ne 0) { throw 'Publish failed.' }
    } else {
        Push-Location PersonalWorkbenchWeb
        try {
            if (Test-Path package-lock.json) {
                & npm ci --no-audit --no-fund
            } else {
                & npm install --no-audit --no-fund
            }
            if ($LASTEXITCODE -ne 0) { throw 'Frontend dependency installation failed.' }
            & npm run build
            if ($LASTEXITCODE -ne 0) { throw 'Frontend build failed.' }
        } finally {
            Pop-Location
        }
        & dotnet build PersonalWorkbench.sln
        if ($LASTEXITCODE -ne 0) { throw 'API build failed.' }
    }
} finally {
    Pop-Location
}
