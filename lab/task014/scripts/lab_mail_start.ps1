<#
.SYNOPSIS
    Starts the isolated BitigMail TASK-014 GreenMail test mail container.
.DESCRIPTION
    Starts the dedicated GreenMail container publishing strictly to loopback:
    127.0.0.1:4025 (SMTP) and 127.0.0.1:4143 (IMAP).
    Configures two discrete authenticated accounts:
    - Source: source@bitigmail.example (username: source)
    - Target: target@bitigmail.example (username: target)
    Security constraints:
    - Dedicated container: bitigmail-task014-mail
    - No privileged mode, no host networking, no docker socket mount.
    - Credentials stored in gitignored lab/task014/local-credentials.json.
    - Passwords are never echoed to console or logs.
    - SMTP is capture-only (in-memory, no external relay).
#>
[CmdletBinding()]
param()

$ErrorActionPreference = "Stop"

$RepoRoot = Split-Path -Parent (Split-Path -Parent (Split-Path -Parent $PSScriptRoot))
if (-not (Test-Path (Join-Path $RepoRoot "fixtures\mail-corpus-v1\manifest.json"))) {
    $RepoRoot = (Get-Location).Path
    if (-not (Test-Path (Join-Path $RepoRoot "fixtures\mail-corpus-v1\manifest.json"))) {
        throw "Could not determine repository root. Please run from repository root or lab/task014/."
    }
}

$LabDir = Join-Path $RepoRoot "lab\task014"
$CredFile = Join-Path $LabDir "local-credentials.json"
$ContainerName = "bitigmail-task014-mail"
$ImageWithDigest = "greenmail/standalone:2.1.12@sha256:9f32971b4f25d32b4de6fa2e297423768441c65e4541f6aecd7631c890a229a7"

function New-SafeSecret {
    $Bytes = New-Object byte[] 24
    [Security.Cryptography.RandomNumberGenerator]::Create().GetBytes($Bytes)
    # Ensure no :, @, or , which delimit GreenMail users string
    return [Convert]::ToBase64String($Bytes).Replace('+', '-').Replace('/', '_').TrimEnd('=')
}

# 1. Ensure local credentials file exists (never commit or log secrets)
if (-not (Test-Path $CredFile)) {
    Write-Host "[LAB-MAIL-TASK014] Generating isolated lab secrets in ignored local config..."
    $SourceSecret = New-SafeSecret
    $TargetSecret = New-SafeSecret

    $Creds = [PSCustomObject]@{
        source = [PSCustomObject]@{
            email = "source@bitigmail.example"
            username = "source"
            password = $SourceSecret
            smtpHost = "127.0.0.1"
            smtpPort = 4025
            imapHost = "127.0.0.1"
            imapPort = 4143
        }
        target = [PSCustomObject]@{
            email = "target@bitigmail.example"
            username = "target"
            password = $TargetSecret
            smtpHost = "127.0.0.1"
            smtpPort = 4025
            imapHost = "127.0.0.1"
            imapPort = 4143
        }
        containerName = $ContainerName
        createdAtUtc = [DateTime]::UtcNow.ToString("o")
    }

    if (-not (Test-Path $LabDir)) {
        New-Item -ItemType Directory -Path $LabDir -Force | Out-Null
    }

    $JsonText = $Creds | ConvertTo-Json -Depth 4
    [System.IO.File]::WriteAllText($CredFile, $JsonText, (New-Object System.Text.UTF8Encoding($false)))
    Write-Host "[LAB-MAIL-TASK014] Credentials written to $CredFile (strictly ignored by git)."
} else {
    $CredsJson = Get-Content -Path $CredFile -Raw | ConvertFrom-Json
    $SourceSecret = $CredsJson.source.password
    $TargetSecret = $CredsJson.target.password
}

if ([string]::IsNullOrWhiteSpace($SourceSecret) -or [string]::IsNullOrWhiteSpace($TargetSecret)) {
    throw "Credential file $CredFile exists but source or target password is empty."
}

# 2. Check if container already exists
$Existing = docker ps -a --filter "name=^/${ContainerName}$" --format "{{.ID}}|{{.Status}}|{{.Names}}"
if ($Existing) {
    $Parts = $Existing.Split('|')
    $ContainerId = $Parts[0]
    $Status = $Parts[1]
    
    if ($Status -like "Up*") {
        Write-Host "[LAB-MAIL-TASK014] Container $ContainerName ($ContainerId) is already running."
        Write-Host "[LAB-MAIL-TASK014] Published endpoints: 127.0.0.1:4025 (SMTP), 127.0.0.1:4143 (IMAP)."
        Write-Host "[LAB-MAIL-TASK014] Credential file: $CredFile (never echoed)."
        exit 0
    } else {
        Write-Host "[LAB-MAIL-TASK014] Container $ContainerName ($ContainerId) exists but is stopped. Starting..."
        docker start $ContainerName | Out-Null
        Write-Host "[LAB-MAIL-TASK014] Started container $ContainerName."
    }
} else {
    # 3. Start dedicated container with strict loopback port mapping
    Write-Host "[LAB-MAIL-TASK014] Starting new container $ContainerName..."
    Write-Host "[LAB-MAIL-TASK014] Image: $ImageWithDigest"
    Write-Host "[LAB-MAIL-TASK014] Ports: 127.0.0.1:4025->3025 (SMTP), 127.0.0.1:4143->3143 (IMAP)"

    # GreenMail standalone setup:
    # Only SMTP (3025) and IMAP (3143), no external relay, two authenticated users:
    # source:pass1@bitigmail.example, target:pass2@bitigmail.example
    $GreenmailOpts = "-Dgreenmail.setup.test.smtp -Dgreenmail.setup.test.imap -Dgreenmail.hostname=0.0.0.0 -Dgreenmail.auth.disabled=false -Dgreenmail.users=source:$SourceSecret@bitigmail.example,target:$TargetSecret@bitigmail.example"

    $RunOutput = docker run -d `
        --name $ContainerName `
        -p 127.0.0.1:4025:3025 `
        -p 127.0.0.1:4143:3143 `
        -e "GREENMAIL_OPTS=$GreenmailOpts" `
        --restart no `
        $ImageWithDigest

    if ($LASTEXITCODE -ne 0) {
        throw "Failed to start GreenMail container. docker run returned exit code $LASTEXITCODE."
    }

    Write-Host "[LAB-MAIL-TASK014] Container started. ID: $RunOutput"
}

# 4. Verify port bindings are strictly loopback (127.0.0.1)
$InspectJson = docker inspect $ContainerName --format "{{json .NetworkSettings.Ports}}" | ConvertFrom-Json
$SmtpBindings = $InspectJson.'3025/tcp'
$ImapBindings = $InspectJson.'3143/tcp'

foreach ($binding in @($SmtpBindings + $ImapBindings)) {
    if ($binding.HostIp -ne "127.0.0.1") {
        docker stop $ContainerName | Out-Null
        docker rm $ContainerName | Out-Null
        throw "[SECURITY VIOLATION] Container port bound to non-loopback IP: $($binding.HostIp). Container destroyed."
    }
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

# 5. Wait for GreenMail services to be fully ready with valid protocol banners (bounded to 45 seconds)
Write-Host "`n[LAB-MAIL-TASK014] Waiting for GreenMail services readiness on loopback (max 45s)..."
$StartTime = [DateTime]::UtcNow
$Ready = $false
$SmtpBanner = $null
$ImapBanner = $null

while (([DateTime]::UtcNow - $StartTime).TotalSeconds -lt 45) {
    if (-not $SmtpBanner) {
        $SmtpBanner = Get-ProtocolBanner -Port 4025 -ExpectedPrefix "220"
    }
    if (-not $ImapBanner) {
        $ImapBanner = Get-ProtocolBanner -Port 4143 -ExpectedPrefix "* OK"
    }
    if ($SmtpBanner -and $ImapBanner) {
        $Ready = $true
        break
    }
    Start-Sleep -Milliseconds 1000
}

if (-not $Ready) {
    throw "[LAB-MAIL-TASK014] TIMEOUT: GreenMail did not present valid non-blank SMTP and IMAP banners within 45 seconds."
}

Write-Host "[LAB-MAIL-TASK014] GreenMail services are fully READY!"
Write-Host "  SMTP Banner (port 4025): $SmtpBanner"
Write-Host "  IMAP Banner (port 4143): $ImapBanner"
Write-Host "  Accounts: source@bitigmail.example (source), target@bitigmail.example (target)"
Write-Host "  Local credential path: $CredFile (passwords kept confidential)"
