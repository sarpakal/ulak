[CmdletBinding()]
param (
    [string]$RepoPath = "C:\Users\sarpa\source\repos\10\Messenger"
)

$ErrorActionPreference = "Stop"

$projectPath = Join-Path $RepoPath "Messenger.Api"
$publishPath = Join-Path $projectPath "bin\Release\net10.0\linux-x64\publish"

try {
    if (-not (Test-Path $projectPath)) {
        throw "Project path not found: $projectPath"
    }

    Set-Location -Path $projectPath

    Write-Host "[1/4] Publishing Messenger.Api..." -ForegroundColor Cyan
    dotnet publish Messenger.Api.csproj -c Release -r linux-x64 --self-contained false
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed with exit code $LASTEXITCODE" }

    if (-not (Test-Path $publishPath)) {
        throw "Expected publish path does not exist: $publishPath"
    }

    Write-Host "[2/4] Uploading to VPS..." -ForegroundColor Cyan
    scp -O -r "$publishPath\" sarpvps:~/apps/ulak-messenger/publish_new/

    Write-Host "[3/4] Swapping publish folders on VPS..." -ForegroundColor Cyan
    ssh sarpvps "cp -r ~/apps/ulak-messenger/publish_new/. ~/apps/ulak-messenger/publish/ && rm -rf ~/apps/ulak-messenger/publish_new"

    Write-Host "[4/4] Rebuilding and restarting container..." -ForegroundColor Cyan
    ssh sarpvps "cd ~/apps && docker compose build ulak-messenger && docker compose up -d ulak-messenger"

    Write-Host "`nStartup logs:" -ForegroundColor Magenta
    Start-Sleep -Seconds 3
    ssh sarpvps "docker logs ulak-service --tail 15"

    Write-Host "`nDone. Service running at https://ulak.akgyh.com" -ForegroundColor Green

} catch {
    Write-Error "Deployment failed: $_"
    Exit $LASTEXITCODE
}
