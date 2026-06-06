#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Build, publish, and package EmailAI Assistant as a single MSI installer.

.DESCRIPTION
    1. Restores NuGet packages
    2. Publishes self-contained single-file WPF app for win-x64
    3. Copies native sqlite-vec extension
    4. Builds MSI via WiX 4

.PREREQUISITES
    - .NET 9 SDK
    - WiX 4: dotnet tool install --global wix
    - sqlite-vec: download vec0.dll from https://github.com/asg017/sqlite-vec/releases
      and place in src/EmailAI.Infrastructure/native/vec0.dll

.EXAMPLE
    .\build.ps1
    .\build.ps1 -Configuration Release -Version "1.0.1"
#>
param(
    [string]$Configuration = "Release",
    [string]$Version = "1.0.0",
    [switch]$SkipMsi
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$Root     = $PSScriptRoot
$SrcDir   = Join-Path $Root "src"
$WpfProj  = Join-Path $SrcDir "EmailAI.WPF\EmailAI.WPF.csproj"
$PublishDir = Join-Path $Root "publish\win-x64"
$InstallerDir = Join-Path $Root "installer"
$OutMsi   = Join-Path $Root "EmailAIAssistant-$Version-Setup.msi"

Write-Host "=== EmailAI Assistant Build ===" -ForegroundColor Cyan
Write-Host "Configuration : $Configuration"
Write-Host "Version       : $Version"
Write-Host "Output MSI    : $OutMsi"
Write-Host ""

# ── Step 1: Restore ──────────────────────────────────────────────────────────
Write-Host ">> Restoring packages…" -ForegroundColor Yellow
dotnet restore (Join-Path $Root "EmailAI.sln") --verbosity quiet
if ($LASTEXITCODE -ne 0) { throw "Restore failed" }

# ── Step 2: Publish (self-contained, single file) ────────────────────────────
Write-Host ">> Publishing WPF application…" -ForegroundColor Yellow
if (Test-Path $PublishDir) { Remove-Item $PublishDir -Recurse -Force }

dotnet publish $WpfProj `
    --configuration $Configuration `
    --runtime win-x64 `
    --self-contained true `
    --output $PublishDir `
    -p:PublishSingleFile=true `
    -p:EnableCompressionInSingleFile=true `
    -p:Version=$Version `
    --verbosity minimal

if ($LASTEXITCODE -ne 0) { throw "Publish failed" }
Write-Host "   Published to: $PublishDir" -ForegroundColor Green

# ── Step 3: Copy native sqlite-vec extension ─────────────────────────────────
Write-Host ">> Copying native sqlite-vec extension…" -ForegroundColor Yellow
$NativeSrc = Join-Path $SrcDir "EmailAI.Infrastructure\native\vec0.dll"
$NativeDst = Join-Path $PublishDir "native"

New-Item -ItemType Directory -Force -Path $NativeDst | Out-Null

if (Test-Path $NativeSrc) {
    $dllSize = (Get-Item $NativeSrc).Length
    if ($dllSize -lt 4096) {
        Write-Host "   WARNING: vec0.dll at $NativeSrc is too small ($dllSize bytes), skipping" -ForegroundColor Yellow
    } else {
        Copy-Item $NativeSrc -Destination $NativeDst -Force
        Write-Host "   Copied vec0.dll" -ForegroundColor Green
    }
} else {
    Write-Host "   WARNING: vec0.dll not found at $NativeSrc" -ForegroundColor Yellow
    Write-Host "   Download from: https://github.com/asg017/sqlite-vec/releases" -ForegroundColor Yellow
    Write-Host "   Semantic search will use in-process fallback without it." -ForegroundColor Yellow
}

# Remove invalid placeholder from prior builds (empty file causes Bad Image error)
$PublishedVec = Join-Path $NativeDst "vec0.dll"
if ((Test-Path $PublishedVec) -and (Get-Item $PublishedVec).Length -lt 4096) {
    Remove-Item $PublishedVec -Force
    Write-Host "   Removed invalid vec0.dll placeholder from publish output" -ForegroundColor Yellow
}

# ── Step 4: Build MSI ────────────────────────────────────────────────────────
if (-not $SkipMsi) {
    Write-Host ">> Building MSI installer with WiX 4…" -ForegroundColor Yellow

    $WxsFile = Join-Path $InstallerDir "EmailAI.Installer.wxs"

    wix build $WxsFile `
        -d "PublishDir=$PublishDir" `
        -d "SrcDir=$SrcDir\EmailAI.WPF" `
        -d "Version=$Version" `
        -ext WixToolset.UI.wixext `
        -o $OutMsi

    if ($LASTEXITCODE -ne 0) { throw "WiX build failed" }

    $SizeMB = [math]::Round((Get-Item $OutMsi).Length / 1MB, 1)
    Write-Host "" 
    Write-Host "=== BUILD COMPLETE ===" -ForegroundColor Green
    Write-Host "MSI: $OutMsi ($SizeMB MB)" -ForegroundColor Green
} else {
    Write-Host ""
    Write-Host "=== BUILD COMPLETE (MSI skipped) ===" -ForegroundColor Green
    Write-Host "Published output: $PublishDir" -ForegroundColor Green
}

Write-Host ""
Write-Host "Next steps:" -ForegroundColor Cyan
Write-Host "  1. Open Sync and connect your email (Gmail, Yahoo, Outlook, or custom IMAP)"
Write-Host "  2. Use an app password if your provider requires 2FA"
Write-Host "  3. Get a DeepSeek API key at https://platform.deepseek.com"
Write-Host "  4. Configure AI settings and run sync"
