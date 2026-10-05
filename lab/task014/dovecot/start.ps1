$ErrorActionPreference = 'Stop'
$containerName = 'bitigmail-task014-dovecot'
$imageId = 'sha256:838d90983c27688591a4a905694a798462543d9a328ed1858309bd2a8bd4ab3a'
$credentialPath = Join-Path $PSScriptRoot 'local-credentials.json'
if (-not (Test-Path -LiteralPath $credentialPath)) {
    $previous = Get-Content (Join-Path (Split-Path -Parent $PSScriptRoot) 'local-credentials.json') -Raw | ConvertFrom-Json
    $previous.source.imapPort = 5143
    $previous.target.imapPort = 5143
    $previous.containerName = $containerName
    [IO.File]::WriteAllText($credentialPath, ($previous | ConvertTo-Json -Depth 5), [Text.UTF8Encoding]::new($false))
}
$credentials = Get-Content $credentialPath -Raw | ConvertFrom-Json
$lines = @()
foreach ($role in @('source','target')) {
    $account = $credentials.$role
    if ($account.imapHost -ne '127.0.0.1' -or $account.imapPort -ne 5143 -or $account.username -notmatch '^[a-zA-Z0-9_-]+$' -or $account.password -match '[:\r\n]') { throw 'Invalid isolated lab credential configuration.' }
    $lines += $account.username + ':{PLAIN}' + $account.password
}
$usersPath = Join-Path $PSScriptRoot 'users'
[IO.File]::WriteAllText($usersPath, (($lines -join "`n") + "`n"), [Text.UTF8Encoding]::new($false))
$existing = docker ps -a --filter "name=^/${containerName}$" --format '{{.Names}}'
if ($existing) {
    $owner = docker inspect --format '{{index .Config.Labels "bitigmail.task"}}' $containerName
    if ($owner -ne 'TASK-014') { throw 'Existing container is not owned by this lab.' }
    docker start $containerName | Out-Null
} else {
    docker run -d --name $containerName --label bitigmail.task=TASK-014 --publish 127.0.0.1:5143:143 --mount "type=bind,source=$usersPath,target=/run/bitigmail-users,readonly" --mount 'type=volume,source=bitigmail-task014-dovecot-data,target=/mail' $imageId | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'Could not start isolated Dovecot lab.' }
}
Write-Output 'Dovecot lab started on 127.0.0.1:5143. Credentials were not printed. Existing mail is preserved.'
