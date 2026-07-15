param(
    [Parameter(Mandatory = $true)]
    [string]$Rid,
    [Parameter(Mandatory = $true)]
    [string]$Ext
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$publishDir = "artifacts\$Rid"

if (-not (Test-Path $publishDir)) {
    throw "Publish directory not found: $publishDir"
}

$items = Get-ChildItem -Path $publishDir -Force
if ($items.Count -eq 0) {
    throw "Publish directory is empty: $publishDir"
}

Get-ChildItem -Path $publishDir -Force | Format-Table -AutoSize

New-Item -ItemType Directory -Path dist -Force | Out-Null
Compress-Archive -Path "$publishDir\*" -DestinationPath "dist\ModHearth-$Rid.$Ext" -Force
