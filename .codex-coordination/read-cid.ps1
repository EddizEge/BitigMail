$ErrorActionPreference = 'Stop'
Add-Type -Path (Join-Path (Get-Location) 'lab/ost-spike/harness/bin/Release/net8.0/Aspose.Email.dll')
$cidEvidence = [System.Collections.Generic.List[object]]::new()
function Read-CidFolder($store, $folder, $label) {
 foreach ($info in $folder.EnumerateMessages()) {
  $msg = $store.ExtractMessage($info)
  try {
   if ($msg.InternetMessageId -ne '<msg-2024-11-inline-cid@projeler.example>') { continue }
   $html = $msg.BodyHtml.Replace("`r`n", "`n")
   foreach ($att in $msg.Attachments) {
    $cidProps = @($att.Properties.Values | Where-Object { ($_.Tag -band 0xFFFF0000L) -eq 0x37120000L } | ForEach-Object { [pscustomobject]@{ tag=('{0:X8}' -f $_.Tag); value=$_.GetString() } })
    $cidEvidence.Add([pscustomobject]@{ stage=$label; fileName=$att.LongFileName; bytes=$att.BinaryData.Length; attachmentSha256=[Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($att.BinaryData)); htmlSha256=[Convert]::ToHexString([Security.Cryptography.SHA256]::HashData([Text.Encoding]::UTF8.GetBytes($html))); htmlReferencesExpectedCid=$html.Contains('cid:proje_logo_cid'); cidProperties=$cidProps })
   }
  } finally { $msg.Dispose() }
 }
 foreach ($child in $folder.GetSubFolders()) { Read-CidFolder $store $child $label }
}
foreach ($entry in @(@('source','lab/ost-spike/input/bitigmail-lab-full.ost'),@('output','lab/ost-spike/output/genuine-full-converted-04.pst'))) {
 $stream=[IO.File]::Open((Join-Path (Get-Location) $entry[1]),[IO.FileMode]::Open,[IO.FileAccess]::Read,[IO.FileShare]::Read)
 try {
  $storage=[Aspose.Email.Storage.Pst.PersonalStorage]::FromStream($stream)
  try { Read-CidFolder $storage $storage.RootFolder $entry[0] } finally { $storage.Dispose() }
 } finally { $stream.Dispose() }
}
if ($cidEvidence.Count -ne 2) { throw "Expected source and output CID records, found $($cidEvidence.Count)" }
$cidJson=$cidEvidence.ToArray() | ConvertTo-Json -Depth 6
$cidJson | Set-Content -LiteralPath 'lab/ost-spike/output/task009-astra-cid-readonly.json' -Encoding utf8
$cidJson
