#Requires -Version 5.1
<#
.SYNOPSIS
    Removes ReminderX: stops it, deletes the logon task, shortcuts and program files.
.PARAMETER RemoveData
    Also deletes all reminders/to-dos/history/attachments. Omit to keep your data in case
    you reinstall later.
#>

[CmdletBinding()]
param(
    [switch]$RemoveData
)

$ErrorActionPreference = "SilentlyContinue"

$InstallDir = Join-Path $env:LOCALAPPDATA "Programs\ReminderX"
$DataDir    = Join-Path $env:LOCALAPPDATA "ReminderX"
$TaskName   = "ReminderX"

Write-Host "==> Stopping ReminderX..." -ForegroundColor Cyan
Get-Process ReminderX -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Milliseconds 500

Write-Host "==> Removing logon task..." -ForegroundColor Cyan
schtasks /Delete /TN $TaskName /F | Out-Null

Write-Host "==> Removing shortcuts..." -ForegroundColor Cyan
Remove-Item (Join-Path ([Environment]::GetFolderPath("Desktop")) "ReminderX.lnk") -Force
Remove-Item (Join-Path ([Environment]::GetFolderPath("Programs")) "ReminderX.lnk") -Force

Write-Host "==> Removing program files ($InstallDir)..." -ForegroundColor Cyan
Remove-Item $InstallDir -Recurse -Force

if ($RemoveData) {
    Write-Host "==> Removing data ($DataDir)..." -ForegroundColor Cyan
    Remove-Item $DataDir -Recurse -Force
} else {
    Write-Host "    Keeping your data at $DataDir (pass -RemoveData to delete it too)." -ForegroundColor Yellow
}

Write-Host "==> ReminderX uninstalled." -ForegroundColor Green
