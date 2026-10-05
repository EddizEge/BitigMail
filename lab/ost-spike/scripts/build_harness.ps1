<#
.SYNOPSIS
    Restores dependencies and builds the BitigMail C# Aspose.Email conversion harness.
.DESCRIPTION
    Uses project-local .tools/dotnet/dotnet.exe if present, otherwise checks system dotnet.
    Builds in Release configuration without touching system PATH.
#>
[CmdletBinding()]
param(
    [string]$Configuration = "Release"
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

$LocalDotnet = Join-Path $RepoRoot ".tools\dotnet\dotnet.exe"
if (Test-Path $LocalDotnet) {
    $DotnetCmd = $LocalDotnet
    Write-Host "[BUILD] Using project-local .NET SDK: $DotnetCmd"
} else {
    $SystemDotnet = Get-Command "dotnet" -ErrorAction SilentlyContinue
    if ($SystemDotnet) {
        $DotnetCmd = "dotnet"
        Write-Host "[BUILD] Project-local SDK not found; using system dotnet: $($SystemDotnet.Source)"
    } else {
        throw "No dotnet host found. Please run scripts\install_local_dotnet.ps1 first to install project-local .NET SDK."
    }
}

$CsprojPath = Join-Path $RepoRoot "lab\ost-spike\harness\BitigMail.OstHarness.csproj"
if (-not (Test-Path $CsprojPath)) {
    throw "Project file not found at: $CsprojPath"
}

Write-Host "[BUILD] Restoring NuGet packages for $CsprojPath..."
& $DotnetCmd restore $CsprojPath
if ($LASTEXITCODE -ne 0) {
    throw "NuGet restore failed with exit code $LASTEXITCODE."
}

Write-Host "[BUILD] Building harness ($Configuration)..."
& $DotnetCmd build $CsprojPath -c $Configuration --no-restore
if ($LASTEXITCODE -ne 0) {
    throw "Build failed with exit code $LASTEXITCODE."
}

Write-Host "`n[BUILD] Build SUCCESSFUL!"
