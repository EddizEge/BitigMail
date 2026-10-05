# BitigMail Testing Engine - Stop Script
# Safely stops BitigMail.TestingHost by known PID without killing unrelated processes.

$ErrorActionPreference = "Stop"
$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Definition
$RepoRoot = Split-Path -Parent $ScriptDir
$RuntimeDir = Join-Path $RepoRoot "runtime\testing-engine"
$PidFile = Join-Path $RuntimeDir "testinghost.pid"

$ProjectFile = Join-Path $RepoRoot "engine\BitigMail.TestingHost\BitigMail.TestingHost.csproj"

if (-not (Test-Path $PidFile)) {
    Write-Host "[BİLGİ] Kayıtlı testinghost.pid bulunamadı. Test servisi çalışmıyor olabilir." -ForegroundColor Yellow
    exit 0
}

$KnownPid = (Get-Content $PidFile -ErrorAction SilentlyContinue).Trim()

if ([string]::IsNullOrWhiteSpace($KnownPid)) {
    Remove-Item $PidFile -Force -ErrorAction SilentlyContinue
    Write-Host "[BİLGİ] Test PID dosyası boş. Temizlendi." -ForegroundColor Yellow
    exit 0
}

$CimProc = Get-CimInstance Win32_Process -Filter "ProcessId = $KnownPid" -ErrorAction SilentlyContinue

if ($CimProc) {
    $cmdLine = $CimProc.CommandLine
    $procName = $CimProc.Name
    $isMatching = $false
    if ($cmdLine -and ($cmdLine -like "*BitigMail.TestingHost*" -or $cmdLine -like "*$ProjectFile*")) {
        $isMatching = $true
    } elseif ($procName -like "*BitigMail.TestingHost*") {
        $isMatching = $true
    }

    if ($isMatching) {
        Write-Host "BitigMail.TestingHost (PID: $KnownPid) durduruluyor..." -ForegroundColor Cyan
        Stop-Process -Id ([int]$KnownPid) -Force -ErrorAction SilentlyContinue
        Start-Sleep -Milliseconds 500
        Remove-Item $PidFile -Force -ErrorAction SilentlyContinue
        Write-Host "[BAŞARILI] BitigMail.TestingHost durduruldu." -ForegroundColor Green
    } else {
        Write-Warning "[UYARI] PID $KnownPid başka bir sürece ait (Komut: $cmdLine). Güvenlik gereği sonlandırılmadı. PID dosyası temizleniyor."
        Remove-Item $PidFile -Force -ErrorAction SilentlyContinue
    }
} else {
    Write-Host "[BİLGİ] PID $KnownPid ile çalışan bir süreç bulunamadı. PID dosyası temizleniyor." -ForegroundColor Yellow
    Remove-Item $PidFile -Force -ErrorAction SilentlyContinue
}
