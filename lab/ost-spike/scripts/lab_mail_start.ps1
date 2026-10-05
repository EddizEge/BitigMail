<#
.SYNOPSIS
    Starts the isolated BitigMail GreenMail standalone test mail container.
.DESCRIPTION
    Phase A: Starts the dedicated GreenMail container publishing strictly to loopback:
    127.0.0.1:3025 (SMTP) and 127.0.0.1:3143 (IMAP).
    Security constraints:
    - Fixed dedicated container: bitigmail-lab-mail
    - No privileged mode, no host networking, no docker socket mount.
    - Credentials stored in gitignored lab/ost-spike/local-credentials.json.
    - Password is never echoed to console or logs.
    - SMTP is capture-only (in-memory, no external relay).
#>
[CmdletBinding()]
param()

$ErrorActionPreference = "Stop"

$RepoRoot = Split-Path -Parent (Split-Path -Parent (Split-Path -Parent $PSScriptRoot))
if (-not (Test-Path (Join-Path $RepoRoot "fixtures\mail-corpus-v1\manifest.json"))) {
    # Fallback if invoked from a different relative location
    $RepoRoot = (Get-Location).Path
    if (-not (Test-Path (Join-Path $RepoRoot "fixtures\mail-corpus-v1\manifest.json"))) {
        throw "Could not determine repository root. Please run from repository root or lab/ost-spike/."
    }
}

$LabDir = Join-Path $RepoRoot "lab\ost-spike"
$CredFile = Join-Path $LabDir "local-credentials.json"
$ContainerName = "bitigmail-lab-mail"
$ImageWithDigest = "greenmail/standalone:2.1.12@sha256:9f32971b4f25d32b4de6fa2e297423768441c65e4541f6aecd7631c890a229a7"

# 1. Ensure local credentials file exists (never commit or log secrets)
if (-not (Test-Path $CredFile)) {
    Write-Host "[LAB-MAIL] Generating isolated lab secret in ignored local config..."
    $Bytes = New-Object byte[] 24
    [Security.Cryptography.RandomNumberGenerator]::Create().GetBytes($Bytes)
    $Secret = [Convert]::ToBase64String($Bytes).Replace('+', '-').Replace('/', '_').TrimEnd('=')

    $Creds = [PSCustomObject]@{
        email = "lab@bitigmail.example"
        username = "lab"
        password = $Secret
        smtpHost = "127.0.0.1"
        smtpPort = 3025
        imapHost = "127.0.0.1"
        imapPort = 3143
        containerName = $ContainerName
        createdAtUtc = [DateTime]::UtcNow.ToString("o")
    }

    $Creds | ConvertTo-Json -Depth 4 | Set-Content -Path $CredFile -Encoding UTF8
    Write-Host "[LAB-MAIL] Credentials written to $CredFile (strictly ignored by git)."
} else {
    $CredsJson = Get-Content -Path $CredFile -Raw | ConvertFrom-Json
    $Secret = $CredsJson.password
}

if ([string]::IsNullOrWhiteSpace($Secret)) {
    throw "Credential file $CredFile exists but password is empty."
}

# 2. Check if container already exists
$Existing = docker ps -a --filter "name=^/${ContainerName}$" --format "{{.ID}}|{{.Status}}|{{.Names}}"
if ($Existing) {
    $Parts = $Existing.Split('|')
    $ContainerId = $Parts[0]
    $Status = $Parts[1]
    
    if ($Status -like "Up*") {
        Write-Host "[LAB-MAIL] Container $ContainerName ($ContainerId) is already running."
        Write-Host "[LAB-MAIL] Published endpoints: 127.0.0.1:3025 (SMTP), 127.0.0.1:3143 (IMAP)."
        Write-Host "[LAB-MAIL] Credential file: $CredFile (never echoed)."
        exit 0
    } else {
        Write-Host "[LAB-MAIL] Container $ContainerName ($ContainerId) exists but is stopped. Starting..."
        docker start $ContainerName | Out-Null
        Write-Host "[LAB-MAIL] Started container $ContainerName."
        exit 0
    }
}

# 3. Start dedicated container with strict loopback port mapping
Write-Host "[LAB-MAIL] Starting new container $ContainerName..."
Write-Host "[LAB-MAIL] Image: $ImageWithDigest"
Write-Host "[LAB-MAIL] Ports: 127.0.0.1:3025->3025 (SMTP), 127.0.0.1:3143->3143 (IMAP)"

# GreenMail standalone setup:
# Only SMTP (3025) and IMAP (3143), no external relay, single authenticated user: login 'lab', email 'lab@bitigmail.example'
$GreenmailOpts = "-Dgreenmail.setup.test.smtp -Dgreenmail.setup.test.imap -Dgreenmail.hostname=0.0.0.0 -Dgreenmail.auth.disabled=false -Dgreenmail.users=lab:$Secret@bitigmail.example"

$RunOutput = docker run -d `
    --name $ContainerName `
    -p 127.0.0.1:3025:3025 `
    -p 127.0.0.1:3143:3143 `
    -e "GREENMAIL_OPTS=$GreenmailOpts" `
    --restart no `
    $ImageWithDigest

if ($LASTEXITCODE -ne 0) {
    throw "Failed to start GreenMail container. docker run returned exit code $LASTEXITCODE."
}

Write-Host "[LAB-MAIL] Container started. ID: $RunOutput"
Write-Host "[LAB-MAIL] Endpoints: SMTP 127.0.0.1:3025, IMAP 127.0.0.1:3143"
Write-Host "[LAB-MAIL] Email: lab@bitigmail.example | Login Username: lab"
Write-Host "[LAB-MAIL] Local credential path: $CredFile"

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

# 4. Wait for GreenMail services to be fully ready with valid protocol banners (bounded to 45 seconds)
Write-Host "`n[LAB-MAIL] Waiting for GreenMail services readiness on loopback (max 45s)..."
$StartTime = [DateTime]::UtcNow
$Ready = $false
$SmtpBanner = $null
$ImapBanner = $null

while (([DateTime]::UtcNow - $StartTime).TotalSeconds -lt 45) {
    if (-not $SmtpBanner) {
        $SmtpBanner = Get-ProtocolBanner -Port 3025 -ExpectedPrefix "220"
    }
    if (-not $ImapBanner) {
        $ImapBanner = Get-ProtocolBanner -Port 3143 -ExpectedPrefix "* OK"
    }
    if ($SmtpBanner -and $ImapBanner) {
        $Ready = $true
        break
    }
    Start-Sleep -Milliseconds 1000
}

if (-not $Ready) {
    throw "[LAB-MAIL] TIMEOUT: GreenMail did not present valid non-blank SMTP and IMAP banners within 45 seconds."
}

Write-Host "[LAB-MAIL] GreenMail services are fully READY!"
Write-Host "  SMTP Banner: $SmtpBanner"
Write-Host "  IMAP Banner: $ImapBanner"

