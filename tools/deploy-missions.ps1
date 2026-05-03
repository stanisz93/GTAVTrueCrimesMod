$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
$source = Join-Path $repoRoot "missions"
$audioSource = Join-Path $repoRoot "audio"
$destination = "C:\Program Files (x86)\Steam\steamapps\common\Grand Theft Auto V Enhanced\missions"
$audioDestination = "C:\Program Files (x86)\Steam\steamapps\common\Grand Theft Auto V Enhanced\scripts\DetectiveAudio"

function Get-CanonicalDirectoryPath {
    param([string]$Path)

    $fullPath = [System.IO.Path]::GetFullPath($Path)

    if (Test-Path -LiteralPath $fullPath) {
        $item = Get-Item -LiteralPath $fullPath -Force

        if ($item.LinkType -and $item.Target -and $item.Target.Count -gt 0) {
            $fullPath = [System.IO.Path]::GetFullPath([string]$item.Target[0])
        }
        else {
            $fullPath = $item.FullName
        }
    }

    return $fullPath.TrimEnd([char[]]@("\", "/"))
}

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

$audioFiles = New-Object System.Collections.Generic.List[object]
$lockedAudioFiles = New-Object System.Collections.Generic.List[object]
$audioDeploySkippedBecauseLinked = $false

if (Test-Path -LiteralPath $audioSource) {
    $resolvedAudioSource = Get-CanonicalDirectoryPath $audioSource
    $resolvedAudioDestination = Get-CanonicalDirectoryPath $audioDestination

    if ([string]::Equals($resolvedAudioSource, $resolvedAudioDestination, [System.StringComparison]::OrdinalIgnoreCase)) {
        $audioDeploySkippedBecauseLinked = $true
    }
    else {
        New-Item -ItemType Directory -Force -Path $audioDestination | Out-Null

        $wavFiles = Get-ChildItem -LiteralPath $audioSource -Filter "*.wav" -File -Recurse

        foreach ($file in $wavFiles) {
            $relativePath = $file.FullName.Substring($audioSource.Length).TrimStart([char[]]@("\", "/"))
            $targetPath = Join-Path $audioDestination $relativePath
            $targetDir = Split-Path -Parent $targetPath

            New-Item -ItemType Directory -Force -Path $targetDir | Out-Null

            try {
                Copy-Item -LiteralPath $file.FullName -Destination $targetPath -Force
                $audioFiles.Add($file) | Out-Null
            }
            catch {
                $lockedAudioFiles.Add($file) | Out-Null
                Write-Warning "Skipped locked audio file '$($file.Name)': $($_.Exception.Message)"
            }
        }
    }
}

Write-Host "Deployed $($missionFiles.Count) mission JSON file(s), $($subtitleFiles.Count) subtitle JSON file(s), $($effectConfigFiles.Count) effect config JSON file(s), $($otherFiles.Count) other JSON file(s), and $($audioFiles.Count) WAV audio file(s)."

if ($audioDeploySkippedBecauseLinked) {
    Write-Host "Skipped WAV audio copy because audio destination is linked to local audio source."
}

if ($lockedAudioFiles.Count -gt 0) {
    Write-Warning "$($lockedAudioFiles.Count) WAV audio file(s) were locked by GTA or another process and were not overwritten."
}

Write-Host "Mission destination: $destination"
Write-Host "Audio destination: $audioDestination"
