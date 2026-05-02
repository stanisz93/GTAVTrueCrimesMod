$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
$source = Join-Path $repoRoot "missions"
$destination = "C:\Program Files (x86)\Steam\steamapps\common\Grand Theft Auto V Enhanced\missions"

New-Item -ItemType Directory -Force -Path $destination | Out-Null

$files = Get-ChildItem -LiteralPath $source -Filter "*.json" -File -Recurse |
    Where-Object { $_.Name -notlike "*.node-snippet.json" }
$missionFiles = New-Object System.Collections.Generic.List[object]
$subtitleFiles = New-Object System.Collections.Generic.List[object]
$effectConfigFiles = New-Object System.Collections.Generic.List[object]
$otherFiles = New-Object System.Collections.Generic.List[object]

foreach ($file in $files) {
    $relativePath = $file.FullName.Substring($source.Length).TrimStart([char[]]@("\", "/"))
    $targetPath = Join-Path $destination $relativePath
    $targetDir = Split-Path -Parent $targetPath

    New-Item -ItemType Directory -Force -Path $targetDir | Out-Null
    Copy-Item -LiteralPath $file.FullName -Destination $targetPath -Force

    if ($relativePath -like "subtitles\*" -or $relativePath -like "subtitles/*") {
        $subtitleFiles.Add($file) | Out-Null
    }
    elseif ($relativePath -like "effects\*" -or $relativePath -like "effects/*") {
        $effectConfigFiles.Add($file) | Out-Null
    }
    elseif ($relativePath.IndexOf("\") -lt 0 -and $relativePath.IndexOf("/") -lt 0) {
        $missionFiles.Add($file) | Out-Null
    }
    else {
        $otherFiles.Add($file) | Out-Null
    }
}

Write-Host "Deployed $($missionFiles.Count) mission JSON file(s), $($subtitleFiles.Count) subtitle JSON file(s), $($effectConfigFiles.Count) effect config JSON file(s), and $($otherFiles.Count) other JSON file(s) to $destination"
