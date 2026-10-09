param([string]$Version = "0.9.0", [string]$Configuration = "Release")
$ErrorActionPreference = "Stop"
if ($Version -notmatch '^\d{1,4}\.\d{1,4}\.\d{1,4}(?:\.\d{1,4})?$') { throw "Version must be a bounded numeric version." }
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))
$release = [IO.Path]::GetFullPath((Join-Path $repo "artifacts\BitigMail-Internal-$Version"))
if (-not $release.StartsWith($repo + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw "Unexpected release path." }
& (Join-Path $repo "scripts\build-desktop.ps1") -Configuration $Configuration -Version $Version
if ($LASTEXITCODE -ne 0) { throw "Desktop package build failed." }
if (Test-Path -LiteralPath $release) { Remove-Item -LiteralPath $release -Recurse -Force }
New-Item -ItemType Directory -Path (Join-Path $release "payload") -Force | Out-Null
Copy-Item -Path (Join-Path $repo "artifacts\desktop-win-x64\*") -Destination (Join-Path $release "payload") -Recurse -Force
& (Join-Path $repo ".tools\dotnet\dotnet.exe") publish (Join-Path $repo "engine\BitigMail.Setup\BitigMail.Setup.csproj") -c $Configuration -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o $release
if ($LASTEXITCODE -ne 0) { throw "Setup publish failed." }
$payload = Join-Path $release "payload"
$files = Get-ChildItem -LiteralPath $payload -Recurse -File | Sort-Object FullName | ForEach-Object { [ordered]@{ RelativePath = $_.FullName.Substring($payload.Length + 1).Replace('\','/'); Length = $_.Length; Sha256 = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToUpperInvariant() } }
$manifest = [ordered]@{ SchemaVersion = 1; Product = "BitigMail"; Version = $Version; MinimumDataSchema = 1; MaximumDataSchema = 1; Files = @($files) }
$manifest | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $release "package-manifest.json") -Encoding utf8NoBOM
@"
BitigMail $Version — unsigned internal Windows build

Prerequisite: Microsoft Edge WebView2 Runtime.
Publisher signature: NOT PRESENT. SHA-256 proves file integrity only, not publisher identity.

Install/update:
  BitigMail.Setup.exe install --package . --allow-unsigned-internal
Launch:
  BitigMail.Setup.exe launch
Rollback:
  BitigMail.Setup.exe rollback
Uninstall application binaries (user data is preserved):
  BitigMail.Setup.exe uninstall
"@ | Set-Content -LiteralPath (Join-Path $release "README.txt") -Encoding utf8NoBOM
Copy-Item -LiteralPath (Join-Path $repo "docs\WINDOWS_QUICK_START_TR.md") -Destination (Join-Path $release "WINDOWS_QUICK_START_TR.md")
$hashes = Get-ChildItem -LiteralPath $release -Recurse -File | Where-Object Name -ne "SHA256SUMS.txt" | Sort-Object FullName | ForEach-Object { "{0}  {1}" -f (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant(), $_.FullName.Substring($release.Length + 1).Replace('\','/') }
$hashes | Set-Content -LiteralPath (Join-Path $release "SHA256SUMS.txt") -Encoding ascii
Write-Output $release
