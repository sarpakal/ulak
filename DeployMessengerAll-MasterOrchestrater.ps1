$ErrorActionPreference = "Stop"

$repoPath = "C:\Users\sarpa\source\repos\10\Messenger"

try {
    if (-not (Test-Path $repoPath)) {
        throw "Repository path not found: $repoPath"
    }

    # === STEP 0: Clean Project (Deletes bin and obj folders) ===
    Write-Host "`n[0/1] Cleaning project (deleting bin and obj folders)..." -ForegroundColor Yellow
    Set-Location -Path $repoPath

    Get-ChildItem -Path $repoPath -Include bin, obj -Recurse -Directory -ErrorAction SilentlyContinue |
        ForEach-Object {
            Write-Host "Removing: $($_.FullName)" -ForegroundColor Gray
            Remove-Item $_.FullName -Recurse -Force
        }

    # === STEP 1: Deploy Messenger API ===
    Write-Host "`n[1/1] Deploying Messenger API..." -ForegroundColor Cyan
    $script1 = Join-Path $PSScriptRoot "DeployMessengerApi.ps1"
    if (Test-Path $script1) {
        Set-Location -Path $repoPath
        & $script1 -RepoPath $repoPath
    } else {
        throw "DeployMessengerApi.ps1 not found in $PSScriptRoot"
    }

    Write-Host "`n=======================================================" -ForegroundColor Green
    Write-Host " Master Pipeline Execution Completed Successfully!    " -ForegroundColor Green
    Write-Host " API updated & running at https://ulak.akgyh.com      " -ForegroundColor Green
    Write-Host "=======================================================" -ForegroundColor Green

} catch {
    Write-Error "`nMaster pipeline orchestration failed: $_"
    Exit $LASTEXITCODE
}
