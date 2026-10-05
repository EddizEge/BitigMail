<#
.SYNOPSIS
    Stops the BitigMail TASK-014 GreenMail test mail container.
.DESCRIPTION
    Stops bitigmail-task014-mail. Optionally removes the container if -Remove is passed.
#>
[CmdletBinding()]
param(
    [switch]$Remove
)

$ErrorActionPreference = "Stop"
$ContainerName = "bitigmail-task014-mail"

$Existing = docker ps -a --filter "name=^/${ContainerName}$" --format "{{.ID}}"
if (-not $Existing) {
    Write-Host "[LAB-MAIL-TASK014] Container $ContainerName does not exist."
    exit 0
}

Write-Host "[LAB-MAIL-TASK014] Stopping container $ContainerName..."
docker stop $ContainerName | Out-Null
Write-Host "[LAB-MAIL-TASK014] Container $ContainerName stopped."

if ($Remove) {
    Write-Host "[LAB-MAIL-TASK014] Removing container $ContainerName..."
    docker rm $ContainerName | Out-Null
    Write-Host "[LAB-MAIL-TASK014] Container $ContainerName removed."
}
