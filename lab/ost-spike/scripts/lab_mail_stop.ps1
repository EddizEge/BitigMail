<#
.SYNOPSIS
    Stops (and optionally removes) the BitigMail test mail container.
.DESCRIPTION
    Safely shuts down the dedicated GreenMail container.
    Use -Remove to also delete the container after stopping.
#>
[CmdletBinding()]
param(
    [switch]$Remove
)

$ContainerName = "bitigmail-lab-mail"

$Existing = docker ps -a --filter "name=^/${ContainerName}$" --format "{{.ID}}|{{.Status}}"
if (-not $Existing) {
    Write-Host "[LAB-MAIL] Container '$ContainerName' does not exist."
    exit 0
}

Write-Host "[LAB-MAIL] Stopping container '$ContainerName'..."
docker stop $ContainerName | Out-Null
Write-Host "[LAB-MAIL] Container '$ContainerName' stopped."

if ($Remove) {
    Write-Host "[LAB-MAIL] Removing container '$ContainerName'..."
    docker rm $ContainerName | Out-Null
    Write-Host "[LAB-MAIL] Container '$ContainerName' removed."
}
