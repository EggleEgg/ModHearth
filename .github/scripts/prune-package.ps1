# Used for removing unused runtimes. Mostly useless now but it doesnt hurt

param(
    [Parameter(Mandatory = $true)]
    [string]$Rid,
    [Parameter(Mandatory = $true)]
    [string]$Ext
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$publishDir = "artifacts\$Rid"
$runtimesDir = Join-Path $publishDir "runtimes"
if (Test-Path $runtimesDir) {
    $removePatterns = switch -Wildcard ($Rid) {
        "linux-*" { @("win*", "osx*", "maccatalyst*", "android*", "ios*", "tvos*", "browser*") }
        "osx-*" { @("win*", "linux*", "android*", "ios*", "tvos*", "browser*") }
        default { @("linux*", "osx*", "maccatalyst*", "android*", "ios*", "tvos*", "browser*") } # Windows
    }    foreach ($pattern in $removePatterns) {
        Get-ChildItem -Path $runtimesDir -Directory -Filter $pattern -ErrorAction SilentlyContinue |
        Remove-Item -Recurse -Force -ErrorAction SilentlyContinue
    }
}

$nativeDir = Join-Path $publishDir "native"
if (Test-Path $nativeDir) {
    Get-ChildItem -Path $nativeDir -File -Include *.so, *.dylib -ErrorAction SilentlyContinue |
    Remove-Item -Force -ErrorAction SilentlyContinue
}

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
