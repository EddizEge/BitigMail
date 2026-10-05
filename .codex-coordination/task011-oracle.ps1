$ErrorActionPreference = 'Stop'
Add-Type -Path (Join-Path (Get-Location) 'lab/ost-spike/harness/bin/Release/net8.0/Aspose.Email.dll')
$task011Rows = [System.Collections.Generic.List[object]]::new()
function Read-Task011Folder($store, $folder, $folderPath) {
 foreach ($info in $folder.EnumerateMessages()) {
  $msg = $store.ExtractMessage($info)
  try {
   $atts = @($msg.Attachments | ForEach-Object {
    [pscustomobject]@{ name=$_.LongFileName; bytes=$_.BinaryData.Length; sha256=[Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($_.BinaryData)) }
   })
   $submit = $msg.ClientSubmitTime
   $delivery = $msg.DeliveryTime
   $chosen = if ($submit -ne [DateTime]::MinValue) { $submit } else { $delivery }
   $utc = if ($chosen.Kind -eq [DateTimeKind]::Unspecified) { [DateTime]::SpecifyKind($chosen,[DateTimeKind]::Utc) } else { $chosen.ToUniversalTime() }
   $turkeyDate = if ($chosen -eq [DateTime]::MinValue) { $null } else { $utc.AddHours(3).ToString('yyyy-MM-dd') }
   $task011Rows.Add([pscustomobject]@{ folder=$folderPath; folderId=$folder.EntryIdString; entryId=$info.EntryIdString; subject=$msg.Subject; messageId=$msg.InternetMessageId; submit=$submit.ToString('o'); submitKind=$submit.Kind.ToString(); delivery=$delivery.ToString('o'); deliveryKind=$delivery.Kind.ToString(); turkeyDate=$turkeyDate; attachments=$atts })
  } finally { $msg.Dispose() }
 }
 foreach ($child in $folder.GetSubFolders()) { Read-Task011Folder $store $child ($folderPath + '/' + $child.DisplayName) }
}
$task011Source = Join-Path (Get-Location) 'lab/ost-spike/input/bitigmail-lab-full.ost'
$task011Stream = [IO.File]::Open($task011Source,[IO.FileMode]::Open,[IO.FileAccess]::Read,[IO.FileShare]::Read)
try {
 $task011Storage = [Aspose.Email.Storage.Pst.PersonalStorage]::FromStream($task011Stream)
 try { Read-Task011Folder $task011Storage $task011Storage.RootFolder '[Root]' } finally { $task011Storage.Dispose() }
} finally { $task011Stream.Dispose() }
$task011Result = [pscustomobject]@{ sourceSha256=(Get-FileHash -LiteralPath $task011Source -Algorithm SHA256).Hash; datePolicy='submit fallback delivery; stored UTC +03 calendar'; messages=$task011Rows.ToArray() }
$task011Result | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath 'lab/ost-spike/output/task011-astra-source-oracle.json' -Encoding utf8
$task011Rows | Select-Object folder,subject,turkeyDate,@{n='attachments';e={$_.attachments.Count}} | ConvertTo-Json -Depth 3
