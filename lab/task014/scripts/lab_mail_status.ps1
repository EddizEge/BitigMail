<#
.SYNOPSIS
    Reports status of the BitigMail TASK-014 GreenMail test mail container.
.DESCRIPTION
    Verifies:
    - Dedicated container bitigmail-task014-mail is running
    - Port bindings are strictly loopback (127.0.0.1:4025, 127.0.0.1:4143)
    - SMTP (220) and IMAP (* OK) protocol banners respond
    - Both source and target accounts authenticate successfully over IMAP without printing secrets
#>
[CmdletBinding()]
param()

$ErrorActionPreference = "Stop"

$RepoRoot = Split-Path -Parent (Split-Path -Parent (Split-Path -Parent $PSScriptRoot))
if (-not (Test-Path (Join-Path $RepoRoot "fixtures\mail-corpus-v1\manifest.json"))) {
    $RepoRoot = (Get-Location).Path
    if (-not (Test-Path (Join-Path $RepoRoot "fixtures\mail-corpus-v1\manifest.json"))) {
        throw "Could not determine repository root."
    }
}

$LabDir = Join-Path $RepoRoot "lab\task014"
$CredFile = Join-Path $LabDir "local-credentials.json"
$ContainerName = "bitigmail-task014-mail"

Write-Host "=== BitigMail TASK-014 Lab Mail Status ==="

# 1. Container check
$InspectRaw = docker ps -a --filter "name=^/${ContainerName}$" --format "{{.ID}}|{{.Status}}|{{.Names}}"
if (-not $InspectRaw) {
    Write-Host "[STATUS] Container $ContainerName does NOT exist."
    exit 1
}

$Parts = $InspectRaw.Split('|')
$ContainerId = $Parts[0]
$Status = $Parts[1]
Write-Host "[CONTAINER] $ContainerName ($ContainerId) - Status: $Status"

if ($Status -notlike "Up*") {
    Write-Host "[STATUS] Container is not running."
    exit 1
}

# 2. Port bindings check
$InspectJson = docker inspect $ContainerName --format "{{json .NetworkSettings.Ports}}" | ConvertFrom-Json
$SmtpBindings = $InspectJson.'3025/tcp'
$ImapBindings = $InspectJson.'3143/tcp'

$SmtpHostPort = $null
$ImapHostPort = $null

foreach ($b in $SmtpBindings) {
    if ($b.HostIp -ne "127.0.0.1") {
        Write-Host "[SECURITY ERROR] SMTP bound to non-loopback IP: $($b.HostIp)" -ForegroundColor Red
        exit 2
    }
    $SmtpHostPort = $b.HostPort
}

foreach ($b in $ImapBindings) {
    if ($b.HostIp -ne "127.0.0.1") {
        Write-Host "[SECURITY ERROR] IMAP bound to non-loopback IP: $($b.HostIp)" -ForegroundColor Red
        exit 2
    }
    $ImapHostPort = $b.HostPort
}

Write-Host "[PORTS] SMTP loopback: 127.0.0.1:$SmtpHostPort (container: 3025)"
Write-Host "[PORTS] IMAP loopback: 127.0.0.1:$ImapHostPort (container: 3143)"

# 3. Banner check
function Get-ProtocolBanner {
    param([int]$Port)
    $client = $null
    try {
        $client = New-Object System.Net.Sockets.TcpClient
        $connect = $client.BeginConnect("127.0.0.1", $Port, $null, $null)
        if (-not $connect.AsyncWaitHandle.WaitOne(2000, $false) -or -not $client.Connected) {
            return $null
        }
        $client.EndConnect($connect)
        $client.ReceiveTimeout = 2000
        $stream = $client.GetStream()
        $reader = New-Object System.IO.StreamReader($stream, [System.Text.Encoding]::ASCII)
        $banner = $reader.ReadLine()
        return $banner
    } catch {
        return $null
    } finally {
        if ($client) {
            $client.Close()
            $client.Dispose()
        }
    }
}

$SmtpBanner = Get-ProtocolBanner -Port ([int]$SmtpHostPort)
$ImapBanner = Get-ProtocolBanner -Port ([int]$ImapHostPort)

Write-Host "[BANNER] SMTP: $(if ($SmtpBanner) { $SmtpBanner } else { 'FAILED' })"
Write-Host "[BANNER] IMAP: $(if ($ImapBanner) { $ImapBanner } else { 'FAILED' })"

if (-not $SmtpBanner -or -not $ImapBanner) {
    Write-Host "[STATUS] One or more protocol banners failed to respond."
    exit 1
}

# 4. Credentials check
if (-not (Test-Path $CredFile)) {
    Write-Host "[CREDS] Credential file not found at $CredFile"
    exit 1
}

$CredsJson = Get-Content -Path $CredFile -Raw | ConvertFrom-Json
if (-not $CredsJson.source.password -or -not $CredsJson.target.password) {
    Write-Host "[CREDS] Passwords missing in $CredFile"
    exit 1
}
Write-Host "[CREDS] Configured accounts: $($CredsJson.source.email) (username: $($CredsJson.source.username)), $($CredsJson.target.email) (username: $($CredsJson.target.username))"

# 5. IMAP Login smoke check (Python stdlib, passwords never printed)
$CheckPy = @"
import imaplib, json, sys

with open(r'$CredFile', 'r', encoding='utf-8-sig') as f:
    creds = json.load(f)

for role in ['source', 'target']:
    u = creds[role]['username']
    p = creds[role]['password']
    try:
        imap = imaplib.IMAP4('127.0.0.1', int('$ImapHostPort'))
        res = imap.login(u, p)
        imap.logout()
        if res[0] != 'OK':
            print(f'FAIL:{role}:bad response {res}')
            sys.exit(1)
        print(f'OK:{role}')
    except Exception as e:
        print(f'FAIL:{role}:{e}')
        sys.exit(1)
"@

$PyRes = & python -c $CheckPy
$PyExit = $LASTEXITCODE
if ($PyExit -ne 0) {
    Write-Host "[AUTH] IMAP authentication test failed: $PyRes"
    exit 1
}

Write-Host "[AUTH] Both source and target accounts authenticated successfully over IMAP."
Write-Host "[STATUS] TASK-014 Lab Mail is HEALTHY and READY."
exit 0
