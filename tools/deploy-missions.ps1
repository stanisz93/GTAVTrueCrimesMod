$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
$source = Join-Path $repoRoot "missions"
$destination = "C:\Program Files (x86)\Steam\steamapps\common\Grand Theft Auto V Enhanced\missions"

New-Item -ItemType Directory -Force -Path $destination | Out-Null
Copy-Item -Path (Join-Path $source "*.json") -Destination $destination -Force

Write-Host "Deployed mission JSON files to $destination"
