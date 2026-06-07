param(
    [string]$TargetDir = "C:\Program Files\Cold Turkey\web"
)

# Request Admin privileges if not running as Admin
$isAdmin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
if (-not $isAdmin) {
    Write-Host "This script requires Administrator privileges to replace Cold Turkey files."
    Write-Host "Restarting with elevated privileges..."
    Start-Process powershell -ArgumentList "-NoProfile -ExecutionPolicy Bypass -File `"$PSCommandPath`" -TargetDir `"$TargetDir`"" -Verb RunAs
    exit
}

$ScriptsDir = Join-Path -Path $PSScriptRoot -ChildPath "Unobfuscated_Backup\web\Scripts"

Write-Host "Building project via Webpack..."
Push-Location -Path $ScriptsDir
npm run build
if ($LASTEXITCODE -ne 0) {
    Write-Error "Build failed! Deployment aborted."
    Pop-Location
    pause
    exit
}
Pop-Location

$SourceDir = Join-Path -Path $PSScriptRoot -ChildPath "Unobfuscated_Backup\web\Bundled"

if (-not (Test-Path -Path $SourceDir)) {
    Write-Error "Source directory not found: $SourceDir"
    pause
    exit
}

if (-not (Test-Path -Path $TargetDir)) {
    Write-Warning "Target directory not found: $TargetDir"
    $TargetDir = Read-Host "Please enter the path to the Cold Turkey web directory (e.g. C:\Program Files\Cold Turkey\web)"
    
    if (-not (Test-Path -Path $TargetDir)) {
        Write-Error "Target directory still not found. Exiting."
        pause
        exit
    }
}

Write-Host "Replacing Cold Turkey web files..."
Write-Host "Source: $SourceDir"
Write-Host "Target: $TargetDir"
Write-Host ""

# Backup the original target directory just in case
$BackupDir = "$TargetDir.bak_$(Get-Date -Format 'yyyyMMdd_HHmmss')"
Write-Host "Backing up original files to: $BackupDir"
Copy-Item -Path $TargetDir -Destination $BackupDir -Recurse -Force

# Clear the target directory so deleted files don't stick around
Write-Host "Clearing target directory..."
Remove-Item -Path "$TargetDir\*" -Recurse -Force

# Copy new files over
Write-Host "Copying new files..."
Copy-Item -Path "$SourceDir\*" -Destination $TargetDir -Recurse -Force

Write-Host ""
Write-Host "Replacement complete! You may need to restart Cold Turkey for changes to take effect."
pause
