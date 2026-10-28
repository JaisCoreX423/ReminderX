#Requires -Version 5.1
<#
.SYNOPSIS
    Builds, installs and registers ReminderX to run in the background at logon.
.DESCRIPTION
    Publishes a self-contained win-x64 build (no .NET runtime needs to be installed
    separately), copies it to %LocalAppData%\Programs\ReminderX, creates a per-user
    logon scheduled task (no admin rights required), and launches the app.
#>

[CmdletBinding()]
param(
    [switch]$SkipLaunch
)

$ErrorActionPreference = "Stop"

$RepoRoot   = $PSScriptRoot
$ProjectPath = Join-Path $RepoRoot "src\ReminderX\ReminderX.csproj"
$InstallDir = Join-Path $env:LOCALAPPDATA "Programs\ReminderX"
$TaskName   = "ReminderX"

Write-Host "==> Publishing ReminderX (self-contained win-x64)..." -ForegroundColor Cyan
dotnet publish $ProjectPath `
    -c Release `
    -r win-x64 `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -o (Join-Path $RepoRoot "publish") | Write-Host

if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed with exit code $LASTEXITCODE"
}

Write-Host "==> Installing to $InstallDir ..." -ForegroundColor Cyan
if (Test-Path $InstallDir) {
    Get-Process ReminderX -ErrorAction SilentlyContinue | Stop-Process -Force
    Start-Sleep -Milliseconds 500
}
New-Item -ItemType Directory -Force -Path $InstallDir | Out-Null
Copy-Item (Join-Path $RepoRoot "publish\*") $InstallDir -Recurse -Force

$ExePath = Join-Path $InstallDir "ReminderX.exe"
if (-not (Test-Path $ExePath)) {
    throw "Publish did not produce $ExePath"
}

Write-Host "==> Registering logon task '$TaskName'..." -ForegroundColor Cyan
$User = "$env:USERDOMAIN\$env:USERNAME"
schtasks /Create /TN $TaskName /TR "`"$ExePath`" --background" /SC ONLOGON /RL LIMITED /RU $User /IT /F | Write-Host

Write-Host "==> Creating Desktop shortcut..." -ForegroundColor Cyan
$WshShell = New-Object -ComObject WScript.Shell
$Shortcut = $WshShell.CreateShortcut((Join-Path ([Environment]::GetFolderPath("Desktop")) "ReminderX.lnk"))
$Shortcut.TargetPath = $ExePath
$Shortcut.WorkingDirectory = $InstallDir
$Shortcut.Description = "ReminderX"
$Shortcut.Save()

Write-Host "==> Done." -ForegroundColor Green
Write-Host "    Installed to: $InstallDir"
Write-Host "    Starts automatically at logon (background, tray icon only)."
Write-Host "    Data stored at: $env:LOCALAPPDATA\ReminderX"

if (-not $SkipLaunch) {
    Write-Host "==> Launching ReminderX..." -ForegroundColor Cyan
    Start-Process -FilePath $ExePath
}
