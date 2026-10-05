<#
.SYNOPSIS
    Installs a pinned .NET 8.0 SDK locally into .tools/dotnet without touching system PATH.
.DESCRIPTION
    Phase B requirement: Project-local supported pinned .NET SDK installation.
    Uses the official Microsoft dotnet-install script (https://dot.net/v1/dotnet-install.ps1).
    Constraints:
    - Target directory: .tools/dotnet/ (strictly project-local)
    - Pinned version: 8.0.425 (official Microsoft .NET 8.0 SDK release, 2026-09-08)
    - Source reference: https://dotnet.microsoft.com/en-us/download/dotnet/8.0
    - Safe named-argument hashtable splatting to prevent positional argument binding issues
    - Reuses cached installer script if already present in .tools/
    - No machine-wide PATH changes (-NoPath flag used)
    - No registry changes, no administrative privileges required
#>
[CmdletBinding()]
param(
    [string]$Version = "8.0.425",
    [string]$Channel = ""
)

$ErrorActionPreference = "Stop"

# Set project-process environment variables to prevent telemetry, first-time experience, and dev cert generation noise
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = "1"
$env:DOTNET_CLI_TELEMETRY_OPTOUT = "1"
$env:DOTNET_NOLOGO = "1"

$RepoRoot = Split-Path -Parent (Split-Path -Parent (Split-Path -Parent $PSScriptRoot))
if (-not (Test-Path (Join-Path $RepoRoot "fixtures\mail-corpus-v1\manifest.json"))) {
    $RepoRoot = (Get-Location).Path
}

$ToolsDir = Join-Path $RepoRoot ".tools"
$InstallDir = Join-Path $ToolsDir "dotnet"
$InstallScriptPath = Join-Path $ToolsDir "dotnet-install.ps1"

if (-not (Test-Path $ToolsDir)) {
    New-Item -ItemType Directory -Path $ToolsDir -Force | Out-Null
}

Write-Host "=== BitigMail Project-Local .NET SDK Setup ==="
Write-Host "Target Directory : $InstallDir"
Write-Host "Pinned Version   : $Version (Official Microsoft release: 2026-09-08)"
Write-Host "System PATH Mod  : NONE (-NoPath enabled)"

# Download official Microsoft dotnet-install script if not already cached
if (-not (Test-Path $InstallScriptPath)) {
    $Url = "https://dot.net/v1/dotnet-install.ps1"
    Write-Host "Downloading official Microsoft dotnet-install.ps1 from $Url..."
    Invoke-WebRequest -Uri $Url -OutFile $InstallScriptPath -UseBasicParsing
    Write-Host "Saved installer script to $InstallScriptPath."
} else {
    Write-Host "Using existing cached installer script at $InstallScriptPath."
}

# Construct named parameters hashtable for safe splatting
$InstallParams = @{
    InstallDir = $InstallDir
    NoPath = $true
}

if (-not [string]::IsNullOrWhiteSpace($Version)) {
    $InstallParams["Version"] = $Version
} elseif (-not [string]::IsNullOrWhiteSpace($Channel)) {
    $InstallParams["Channel"] = $Channel
}

Write-Host "`nInvoking dotnet-install.ps1 with named parameters:"
$InstallParams.GetEnumerator() | ForEach-Object { Write-Host "  -$($_.Key) $($_.Value)" }

# Execute installer with safe hashtable splatting
& $InstallScriptPath @InstallParams

$LocalDotnet = Join-Path $InstallDir "dotnet.exe"
if (-not (Test-Path $LocalDotnet)) {
    throw "Installation failed: $LocalDotnet does not exist."
}

$InstalledVersion = & $LocalDotnet --version
Write-Host "`n[OK] Pinned project-local .NET SDK installed successfully!"
Write-Host "Binary Location  : $LocalDotnet"
Write-Host "SDK Version      : $InstalledVersion"
Write-Host "Notice: Machine-wide PATH was NOT modified."
