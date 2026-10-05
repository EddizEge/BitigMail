param([string]$Configuration = "Release")
$ErrorActionPreference = "Stop"
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))
$output = [IO.Path]::GetFullPath((Join-Path $repo "artifacts\desktop-win-x64"))
if (-not $output.StartsWith($repo + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw "Unexpected output path." }
if (Test-Path -LiteralPath $output) { Remove-Item -LiteralPath $output -Recurse -Force }
New-Item -ItemType Directory -Path $output | Out-Null
Push-Location (Join-Path $repo "prototype")
try {
    npm run build
    if ($LASTEXITCODE -ne 0) { throw "Frontend build failed." }
} finally { Pop-Location }
& (Join-Path $repo ".tools\dotnet\dotnet.exe") publish (Join-Path $repo "engine\BitigMail.Desktop\BitigMail.Desktop.csproj") -c $Configuration -r win-x64 --self-contained true -o $output
if ($LASTEXITCODE -ne 0) { throw "Desktop publish failed." }
$engineOutput = [IO.Path]::GetFullPath((Join-Path $output "engine"))
if (-not $engineOutput.StartsWith($output + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw "Unexpected engine output path." }
if (Test-Path -LiteralPath $engineOutput) { Remove-Item -LiteralPath $engineOutput -Recurse -Force }
& (Join-Path $repo ".tools\dotnet\dotnet.exe") publish (Join-Path $repo "engine\BitigMail.LocalHost\BitigMail.LocalHost.csproj") -c $Configuration -r win-x64 --self-contained true -o (Join-Path $output "engine")
if ($LASTEXITCODE -ne 0) { throw "Engine publish failed." }
$uiOutput = Join-Path $output "ui"
New-Item -ItemType Directory -Path $uiOutput -Force | Out-Null
Copy-Item -Path (Join-Path $repo "prototype\dist\*") -Destination $uiOutput -Recurse -Force
$manifest = Get-ChildItem -LiteralPath $output -Recurse -File | Sort-Object FullName | ForEach-Object { [pscustomobject]@{ Path = $_.FullName.Substring($output.Length + 1).Replace('\','/'); Length = $_.Length; Sha256 = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant() } }
$manifest | ConvertTo-Json -Depth 3 | Set-Content -LiteralPath (Join-Path $output "package-manifest.json") -Encoding utf8
Write-Output $output
