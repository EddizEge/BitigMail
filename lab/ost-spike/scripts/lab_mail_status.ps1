<#
.SYNOPSIS
    Checks the status and loopback connectivity of the BitigMail test mail container.
.DESCRIPTION
    Verifies container health, loopback port bindings (127.0.0.1 only), and TCP connectivity
    for SMTP (3025) and IMAP (3143). Never exposes credentials or environment dumps.
#>
[CmdletBinding()]
param()

$RepoRoot = Split-Path -Parent (Split-Path -Parent (Split-Path -Parent $PSScriptRoot))
if (-not (Test-Path (Join-Path $RepoRoot "fixtures\mail-corpus-v1\manifest.json"))) {
    $RepoRoot = (Get-Location).Path
}
$LabDir = Join-Path $RepoRoot "lab\ost-spike"
$CredFile = Join-Path $LabDir "local-credentials.json"
$ContainerName = "bitigmail-lab-mail"

Write-Host "=== BitigMail Lab Mail Status ==="

# 1. Check container inspection
$InspectJson = docker inspect $ContainerName 2>$null
if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($InspectJson)) {
    Write-Host "[STATUS] Container '$ContainerName' NOT FOUND."
    Write-Host "[STATUS] Run lab_mail_start.ps1 to start the test mail container."
    exit 1
}

$Container = $InspectJson | ConvertFrom-Json
$State = $Container[0].State
$Config = $Container[0].Config
$HostConfig = $Container[0].HostConfig
$NetworkSettings = $Container[0].NetworkSettings

Write-Host "Container Name : $ContainerName"
Write-Host "Container ID   : $($Container[0].Id.Substring(0, 12))"
Write-Host "Running State  : $($State.Status) (Running=$($State.Running), OOMKilled=$($State.OOMKilled))"
Write-Host "Started At     : $($State.StartedAt)"
Write-Host "Image Digest   : $($Config.Image)"

# 2. Verify Port Bindings (Strictly Loopback Only)
$PortBindings = $HostConfig.PortBindings
$SmtpBinding = $PortBindings."3025/tcp"
$ImapBinding = $PortBindings."3143/tcp"

$SmtpIp = $SmtpBinding[0].HostIp
$SmtpPort = $SmtpBinding[0].HostPort
$ImapIp = $ImapBinding[0].HostIp
$ImapPort = $ImapBinding[0].HostPort

Write-Host "`nPort Bindings Verification:"
Write-Host "  SMTP: $SmtpIp`:$SmtpPort -> 3025 (Expected: 127.0.0.1:3025)"
Write-Host "  IMAP: $ImapIp`:$ImapPort -> 3143 (Expected: 127.0.0.1:3143)"

$LoopbackOnly = ($SmtpIp -eq "127.0.0.1" -and $ImapIp -eq "127.0.0.1")
if (-not $LoopbackOnly) {
    Write-Error "SECURITY VIOLATION: Container ports are NOT strictly bound to 127.0.0.1!"
    exit 2
} else {
    Write-Host "  [OK] Ports are strictly bound to loopback 127.0.0.1."
}

function Get-ProtocolBanner {
    param(
        [int]$Port,
        [string]$ExpectedPrefix
    )
    $client = $null
    try {
        $client = New-Object System.Net.Sockets.TcpClient
        $connect = $client.BeginConnect("127.0.0.1", $Port, $null, $null)
        $success = $connect.AsyncWaitHandle.WaitOne(1500, $false)
        if (-not $success -or -not $client.Connected) {
            return $null
        }
        $client.EndConnect($connect)
        $client.ReceiveTimeout = 2000
        $stream = $client.GetStream()
        $stream.ReadTimeout = 2000
        $reader = New-Object System.IO.StreamReader($stream, [System.Text.Encoding]::ASCII)
        $banner = $reader.ReadLine()
        if (-not [string]::IsNullOrWhiteSpace($banner)) {
            $bannerTrimmed = $banner.Trim()
            if (-not [string]::IsNullOrWhiteSpace($ExpectedPrefix)) {
                if ($bannerTrimmed.StartsWith($ExpectedPrefix, [StringComparison]::OrdinalIgnoreCase) -or $bannerTrimmed -like "*GreenMail*") {
                    return $bannerTrimmed
                }
            } else {
                return $bannerTrimmed
            }
        }
        return $null
    } catch {
        return $null
    } finally {
        if ($client) {
            $client.Close()
            $client.Dispose()
        }
    }
}

# 3. Test TCP connectivity and verify nonblank GreenMail banners (bounded retry up to 30s)
if ($State.Running) {
    Write-Host "`nSocket Connectivity & Banner Probes (max 30s):"
    $StartTime = [DateTime]::UtcNow
    $SmtpBanner = $null
    $ImapBanner = $null

    while (([DateTime]::UtcNow - $StartTime).TotalSeconds -lt 30) {
        if (-not $SmtpBanner) {
            $SmtpBanner = Get-ProtocolBanner -Port 3025 -ExpectedPrefix "220"
        }
        if (-not $ImapBanner) {
            $ImapBanner = Get-ProtocolBanner -Port 3143 -ExpectedPrefix "* OK"
        }
        if ($SmtpBanner -and $ImapBanner) {
            break
        }
        Start-Sleep -Milliseconds 1000
    }

    Write-Host "  SMTP (127.0.0.1:3025): $(if ($SmtpBanner) { "CONNECTED. Banner: $SmtpBanner" } else { "FAILED / BLANK BANNER" })"
    Write-Host "  IMAP (127.0.0.1:3143): $(if ($ImapBanner) { "CONNECTED. Banner: $ImapBanner" } else { "FAILED / BLANK BANNER" })"

    if ([string]::IsNullOrWhiteSpace($SmtpBanner) -or [string]::IsNullOrWhiteSpace($ImapBanner)) {
        Write-Error "READINESS FAILURE: SMTP or IMAP banner is blank or missing expected GreenMail signature."
        exit 1
    } else {
        Write-Host "  [OK] Both SMTP and IMAP services responded with valid non-blank GreenMail banners."
    }
} else {
    Write-Error "Container is not running."
    exit 1
}

# 4. Check credentials file status
Write-Host "`nCredential Status:"
if (Test-Path $CredFile) {
    Write-Host "  Credential file present at: $CredFile"
    Write-Host "  Email: lab@bitigmail.example | Login Username: lab (password hidden/secure)"
} else {
    Write-Host "  WARNING: Local credential file missing at $CredFile"
}

Write-Host "=== End Status ==="
