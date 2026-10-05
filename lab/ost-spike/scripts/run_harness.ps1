<#
.SYNOPSIS
    Runs the BitigMail C# Aspose.Email conversion harness.
.DESCRIPTION
    Executes either:
    1) synthetic-smoke (EML -> new Unicode PST smoke test)
    2) ost-to-pst (genuine OST -> new Unicode PST per-item extraction)
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)]
    [ValidateSet("synthetic-smoke", "synthetic-eml-to-pst", "ost-to-pst")]
    [string]$Mode,

    [string]$InputOst,
    [string]$OutputPst,
    [string]$Manifest,
    [string]$License,
    [string]$Report
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
} else {
    $DotnetCmd = "dotnet"
}

$CsprojPath = Join-Path $RepoRoot "lab\ost-spike\harness\BitigMail.OstHarness.csproj"
$DefaultManifest = Join-Path $RepoRoot "fixtures\mail-corpus-v1\manifest.json"
$DefaultReportDir = Join-Path $RepoRoot "lab\ost-spike\output"

if (-not (Test-Path $DefaultReportDir)) {
    New-Item -ItemType Directory -Path $DefaultReportDir -Force | Out-Null
}

$ArgsList = @("run", "--project", $CsprojPath, "-c", "Release", "--no-build", "--", "--mode", $Mode)

if ($Mode -like "synthetic*") {
    $ManifestPath = if ($Manifest) { $Manifest } else { $DefaultManifest }
    $OutputPath = if ($OutputPst) { $OutputPst } else { Join-Path $DefaultReportDir "synthetic-smoke.pst" }
    $ReportPath = if ($Report) { $Report } else { Join-Path $DefaultReportDir "synthetic-smoke-report.json" }

    $ArgsList += @("--manifest", $ManifestPath, "--output-pst", $OutputPath, "--report", $ReportPath)
} elseif ($Mode -eq "ost-to-pst") {
    if (-not $InputOst) {
        throw "Parameter -InputOst is required for ost-to-pst mode."
    }
    $OutputPath = if ($OutputPst) { $OutputPst } else { Join-Path $DefaultReportDir "genuine-full-converted-04.pst" }
    $ReportPath = if ($Report) { $Report } else { Join-Path $DefaultReportDir "genuine-full-converted-04-report.json" }

    $ArgsList += @("--input", $InputOst, "--output-pst", $OutputPath, "--report", $ReportPath)
    if ($Manifest) {
        $ArgsList += @("--manifest", $Manifest)
    } else {
        $ArgsList += @("--manifest", $DefaultManifest)
    }
}

if ($License) {
    $ArgsList += @("--license", $License)
}

Write-Host "[RUN] Executing harness in mode: $Mode"
& $DotnetCmd @ArgsList
exit $LASTEXITCODE
