# BitigMail Local Engine - Start Script
# Starts BitigMail.LocalHost on 127.0.0.1:6174 without killing unrelated processes.

$ErrorActionPreference = "Stop"
$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Definition
$RepoRoot = Split-Path -Parent $ScriptDir
$RuntimeDir = Join-Path $RepoRoot "runtime\local-engine"
$PidFile = Join-Path $RuntimeDir "localhost.pid"
$ProjectFile = Join-Path $RepoRoot "engine\BitigMail.LocalHost\BitigMail.LocalHost.csproj"

if (-not (Test-Path $RuntimeDir)) {
    New-Item -ItemType Directory -Path $RuntimeDir -Force | Out-Null
}

$DotnetExe = Join-Path $RepoRoot ".tools\dotnet\dotnet.exe"
if (-not (Test-Path $DotnetExe)) {
    Write-Error "[HATA] Proje yerel .NET SDK bulunamadı: $DotnetExe"
    exit 1
}

# Process-only environment variables to disable telemetry and banner noise
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = "1"
$env:DOTNET_CLI_TELEMETRY_OPTOUT = "1"
$env:DOTNET_NOLOGO = "1"
$env:DOTNET_MULTILEVEL_LOOKUP = "0"

$Port = 6174
$Connection = Get-NetTCPConnection -LocalPort $Port -ErrorAction SilentlyContinue | Where-Object { $_.State -eq "Listen" }

if ($Connection) {
    $OccupyingPid = $Connection.OwningProcess
    $KnownPid = $null
    if (Test-Path $PidFile) {
        $KnownPid = (Get-Content $PidFile -ErrorAction SilentlyContinue).Trim()
    }

    if ($KnownPid -and ($OccupyingPid -eq [int]$KnownPid)) {
        Write-Host "[BİLGİ] BitigMail.LocalHost zaten PID $OccupyingPid ile 6174 portunda çalışıyor." -ForegroundColor Green
        exit 0
    } else {
        Write-Error "[HATA] 6174 portu farklı bir süreç (PID: $OccupyingPid) tarafından kullanılıyor. Güvenlik politikası gereği ilgisiz süreçler sonlandırılmaz. Lütfen port çakışmasını giderin."
        exit 1
    }
}

Write-Host "BitigMail.LocalHost (127.0.0.1:6174) başlatılıyor..." -ForegroundColor Cyan

$Process = Start-Process -FilePath $DotnetExe `
    -ArgumentList "run --project `"$ProjectFile`"" `
    -WorkingDirectory $RepoRoot `
    -WindowStyle Hidden `
    -PassThru

$Started = $false
for ($i = 0; $i -lt 40; $i++) {
    Start-Sleep -Milliseconds 500
    if ($Process.HasExited) {
        break
    }
    $conn = Get-NetTCPConnection -LocalPort $Port -ErrorAction SilentlyContinue | Where-Object { $_.State -eq "Listen" }
    if ($conn) {
        $Started = $true
        Write-Host "[BAŞARILI] BitigMail.LocalHost PID $($conn.OwningProcess) ile $Port portunda dinliyor." -ForegroundColor Green
        Write-Host "Uç Nokta: http://127.0.0.1:$Port" -ForegroundColor Gray
        break
    }
}

if (-not $Started) {
    if (-not $Process.HasExited) {
        Stop-Process -Id $Process.Id -Force -ErrorAction SilentlyContinue
    }
    Write-Error "[HATA] BitigMail.LocalHost $Port portunu dinlemeye başlayamadı veya süreç erken sonlandı."
    exit 1
}
