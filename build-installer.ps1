#Requires -Version 5.1
<#
.SYNOPSIS
    Builds ReminderXSetup.exe — a single, double-clickable installer with the app bundled
    inside it. Needs the .NET 10 SDK; the resulting exe needs nothing else installed on the
    target machine.
.PARAMETER Version
    Optional version to stamp into both executables (e.g. 1.0.2 or v1.0.2). Defaults to the
    <Version> in the project files. The release workflow passes the git tag here.
#>

[CmdletBinding()]
param(
    [string]$Version
)

$ErrorActionPreference = "Stop"
$RepoRoot = $PSScriptRoot

$AppProject   = Join-Path $RepoRoot "src\ReminderX\ReminderX.csproj"
$SetupProject = Join-Path $RepoRoot "src\ReminderX.Setup\ReminderX.Setup.csproj"
$AppPublishDir = Join-Path $RepoRoot "publish"
$PayloadZip    = Join-Path $RepoRoot "src\ReminderX.Setup\payload.zip"
$DistDir       = Join-Path $RepoRoot "dist"

$versionArgs = @()
if ($Version) {
    $Version = $Version.TrimStart('v', 'V')
    $versionArgs = @("-p:Version=$Version")
    Write-Host "==> Building version $Version" -ForegroundColor Cyan
}

Write-Host "==> [1/4] Publishing ReminderX (self-contained win-x64)..." -ForegroundColor Cyan
if (Test-Path $AppPublishDir) { Remove-Item $AppPublishDir -Recurse -Force }
# ReadyToRun: ships pre-compiled code so paths that first run hours after startup (e.g. the first
# MCP call from an AI client) don't have to be JIT-compiled on the spot.
dotnet publish $AppProject -c Release -r win-x64 --self-contained true `
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:PublishReadyToRun=true `
    @versionArgs -o $AppPublishDir | Write-Host
if ($LASTEXITCODE -ne 0) { throw "Publishing ReminderX failed." }

Write-Host "==> [2/4] Packaging payload.zip..." -ForegroundColor Cyan
if (Test-Path $PayloadZip) { Remove-Item $PayloadZip -Force }
Compress-Archive -Path (Join-Path $AppPublishDir "*") -DestinationPath $PayloadZip -CompressionLevel Optimal

Write-Host "==> [3/4] Publishing ReminderXSetup.exe (self-contained win-x64)..." -ForegroundColor Cyan
if (Test-Path $DistDir) { Remove-Item $DistDir -Recurse -Force }
dotnet publish $SetupProject -c Release -r win-x64 --self-contained true `
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
    @versionArgs -o $DistDir | Write-Host
if ($LASTEXITCODE -ne 0) { throw "Publishing ReminderXSetup failed." }

Write-Host "==> [4/4] Cleaning up intermediates..." -ForegroundColor Cyan
Remove-Item $PayloadZip -Force -ErrorAction SilentlyContinue
Remove-Item $AppPublishDir -Recurse -Force -ErrorAction SilentlyContinue
Remove-Item (Join-Path $DistDir "*.pdb") -Force -ErrorAction SilentlyContinue
Remove-Item (Join-Path $RepoRoot "src\ReminderX\bin") -Recurse -Force -ErrorAction SilentlyContinue
Remove-Item (Join-Path $RepoRoot "src\ReminderX\obj") -Recurse -Force -ErrorAction SilentlyContinue
Remove-Item (Join-Path $RepoRoot "src\ReminderX.Setup\bin") -Recurse -Force -ErrorAction SilentlyContinue
Remove-Item (Join-Path $RepoRoot "src\ReminderX.Setup\obj") -Recurse -Force -ErrorAction SilentlyContinue

$finalExe = Join-Path $DistDir "ReminderXSetup.exe"
Write-Host ""
Write-Host "==> Done: $finalExe" -ForegroundColor Green
Write-Host "    Double-click it to install (or run with --uninstall [--remove-data])."
