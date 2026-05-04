param(
    [Parameter(Mandatory = $true)]
    [string]$AudioFolder,

    [string]$VttPath = "",

    [string]$OutputFolder = "",

    [string]$AudioFilter = "*.wav",

    [ValidateSet("Auto", "ByCueOrder", "ByDuration")]
    [string]$SplitMode = "Auto",

    [int]$SubtitleOffsetMs = 0,

    [switch]$KeepPunctuationOnlyCues
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot

function Resolve-ProjectPath {
    param(
        [string]$Path,
        [bool]$MustExist = $true
    )

    if ([System.IO.Path]::IsPathRooted($Path)) {
        if ($MustExist) {
            return (Resolve-Path -LiteralPath $Path).Path
        }

        return [System.IO.Path]::GetFullPath($Path)
    }

    $candidate = Join-Path (Get-Location) $Path

    if ($MustExist -and (Test-Path -LiteralPath $candidate)) {
        return (Resolve-Path -LiteralPath $candidate).Path
    }

    if (-not $MustExist) {
        return [System.IO.Path]::GetFullPath($candidate)
    }

    $repoCandidate = Join-Path $repoRoot $Path

    return (Resolve-Path -LiteralPath $repoCandidate).Path
}

function Convert-VttTimestampToMs {
    param([string]$Timestamp)

    $clean = $Timestamp.Trim().Replace(",", ".")
    $parts = $clean.Split(":")

    if ($parts.Length -ne 2 -and $parts.Length -ne 3) {
        throw "Invalid VTT timestamp '$Timestamp'."
    }

    $hours = 0
    $minutes = 0
    $secondsPart = ""

    if ($parts.Length -eq 3) {
        $hours = [int]$parts[0]
        $minutes = [int]$parts[1]
        $secondsPart = $parts[2]
    }
    else {
        $minutes = [int]$parts[0]
        $secondsPart = $parts[1]
    }

    $secondParts = $secondsPart.Split(".")

    if ($secondParts.Length -ne 2) {
        throw "Invalid VTT timestamp '$Timestamp'."
    }

    $seconds = [int]$secondParts[0]
    $millisecondsText = $secondParts[1].PadRight(3, [char]"0").Substring(0, 3)
    $milliseconds = [int]$millisecondsText

    return (($hours * 3600 + $minutes * 60 + $seconds) * 1000 + $milliseconds)
}

function Convert-CueText {
    param([string[]]$Lines)

    $text = ($Lines -join " ").Trim()
    $text = [regex]::Replace($text, "<[^>]+>", "")
    $text = [System.Net.WebUtility]::HtmlDecode($text)
    $text = [regex]::Replace($text, "\s+", " ").Trim()
    $text = [regex]::Replace($text, "\s+([\.,!\?:;])", '$1')

    return $text
}

function Read-VttCues {
    param([string]$Path)

    $lines = Get-Content -LiteralPath $Path -Encoding UTF8
    $cues = New-Object System.Collections.Generic.List[object]
    $timestampPattern = "^\s*(?<start>(?:\d+:)?\d{2}:\d{2}[\.,]\d{1,3})\s+-->\s+(?<end>(?:\d+:)?\d{2}:\d{2}[\.,]\d{1,3})(?:\s+.*)?$"
    $i = 0

    while ($i -lt $lines.Count) {
        $line = $lines[$i].Trim()

        if ($line.Length -eq 0 -or $line -eq "WEBVTT") {
            $i++
            continue
        }

        if ($line.StartsWith("NOTE") -or $line -eq "STYLE" -or $line -eq "REGION") {
            while ($i -lt $lines.Count -and $lines[$i].Trim().Length -gt 0) {
                $i++
            }

            continue
        }

        $timestampLine = $lines[$i]
        $timestampMatch = [regex]::Match($timestampLine, $timestampPattern)

        if (-not $timestampMatch.Success -and ($i + 1) -lt $lines.Count) {
            $possibleTimestamp = $lines[$i + 1]
            $possibleMatch = [regex]::Match($possibleTimestamp, $timestampPattern)

            if ($possibleMatch.Success) {
                $i++
                $timestampLine = $possibleTimestamp
                $timestampMatch = $possibleMatch
            }
        }

        if (-not $timestampMatch.Success) {
            while ($i -lt $lines.Count -and $lines[$i].Trim().Length -gt 0) {
                $i++
            }

            continue
        }

        $startMs = Convert-VttTimestampToMs $timestampMatch.Groups["start"].Value
        $endMs = Convert-VttTimestampToMs $timestampMatch.Groups["end"].Value
        $i++

        $textLines = New-Object System.Collections.Generic.List[string]

        while ($i -lt $lines.Count -and $lines[$i].Trim().Length -gt 0) {
            $textLines.Add($lines[$i])
            $i++
        }

        $text = Convert-CueText $textLines.ToArray()

        if ($text.Length -eq 0) {
            continue
        }

        if (-not $KeepPunctuationOnlyCues -and [regex]::IsMatch($text, "^[\p{P}\p{S}\s]+$")) {
            continue
        }

        if ($endMs -le $startMs) {
            continue
        }

        $cues.Add([pscustomobject]@{
            atMs = $startMs
            endMs = $endMs
            text = $text
        })
    }

    return $cues
}

function Read-WavDurationMs {
    param([string]$Path)

    $stream = [System.IO.File]::Open($Path, [System.IO.FileMode]::Open, [System.IO.FileAccess]::Read, [System.IO.FileShare]::ReadWrite)

    try {
        $reader = New-Object System.IO.BinaryReader($stream)
        $riff = [System.Text.Encoding]::ASCII.GetString($reader.ReadBytes(4))
        $reader.ReadUInt32() | Out-Null
        $wave = [System.Text.Encoding]::ASCII.GetString($reader.ReadBytes(4))

        if ($riff -ne "RIFF" -or $wave -ne "WAVE") {
            throw "File is not a RIFF/WAVE file."
        }

        $byteRate = 0
        $dataSize = 0

        while ($stream.Position + 8 -le $stream.Length) {
            $chunkId = [System.Text.Encoding]::ASCII.GetString($reader.ReadBytes(4))
            $chunkSize = [int64]$reader.ReadUInt32()
            $chunkStart = $stream.Position

            if ($chunkId -eq "fmt ") {
                $reader.ReadUInt16() | Out-Null
                $reader.ReadUInt16() | Out-Null
                $reader.ReadUInt32() | Out-Null
                $byteRate = [int]$reader.ReadUInt32()
            }
            elseif ($chunkId -eq "data") {
                $dataSize = $chunkSize
            }

            $nextPosition = $chunkStart + $chunkSize

            if (($chunkSize % 2) -eq 1) {
                $nextPosition++
            }

            if ($nextPosition -gt $stream.Length) {
                break
            }

            $stream.Position = $nextPosition
        }

        if ($byteRate -le 0 -or $dataSize -le 0) {
            throw "Missing fmt/data chunks in WAV file '$Path'."
        }

        return [int][Math]::Round(($dataSize * 1000.0) / $byteRate)
    }
    finally {
        $stream.Dispose()
    }
}

function Convert-ToSubtitleCue {
    param(
        [object]$Cue,
        [int]$SegmentStartMs,
        [int]$SegmentEndMs
    )

    $localStart = [Math]::Max(0, $Cue.atMs - $SegmentStartMs + $SubtitleOffsetMs)
    $localEnd = [Math]::Min($SegmentEndMs - $SegmentStartMs, $Cue.endMs - $SegmentStartMs + $SubtitleOffsetMs)

    if ($localEnd -le $localStart) {
        return $null
    }

    return [pscustomobject]@{
        atMs = [int]$localStart
        endMs = [int]$localEnd
        text = $Cue.text
    }
}

function Convert-ToOrderedSubtitleCue {
    param(
        [object]$Cue,
        [int]$SegmentDurationMs
    )

    $start = [Math]::Max(0, $SubtitleOffsetMs)
    $end = [Math]::Max($start + 1, ($Cue.endMs - $Cue.atMs) + $SubtitleOffsetMs)

    if ($SegmentDurationMs -gt 0) {
        $end = [Math]::Min($SegmentDurationMs, $end)
    }

    return [pscustomobject]@{
        atMs = [int]$start
        endMs = [int]$end
        text = $Cue.text
    }
}

function Get-FirstNumberFromFileName {
    param([System.IO.FileInfo]$File)

    $match = [regex]::Match($File.Name, "\d+")

    if (-not $match.Success) {
        return [int]::MaxValue
    }

    $value = 0

    if ([int]::TryParse($match.Value, [ref]$value)) {
        return $value
    }

    return [int]::MaxValue
}

$resolvedAudioFolder = Resolve-ProjectPath $AudioFolder $true

if ([string]::IsNullOrWhiteSpace($VttPath)) {
    $vttFiles = @(Get-ChildItem -LiteralPath $resolvedAudioFolder -Filter "*.vtt" -File)

    if ($vttFiles.Count -eq 0) {
        throw "Expected exactly one VTT file in '$resolvedAudioFolder', but found none."
    }

    if ($vttFiles.Count -gt 1) {
        throw "Expected exactly one VTT file in '$resolvedAudioFolder', but found $($vttFiles.Count): $($vttFiles.Name -join ', ')."
    }

    $resolvedVttPath = $vttFiles[0].FullName
}
else {
    $resolvedVttPath = Resolve-ProjectPath $VttPath $true

    if ((Split-Path -Parent $resolvedVttPath) -ne $resolvedAudioFolder) {
        Write-Host "Warning: VTT file is not in the audio folder. Folder auto-detection expects one VTT next to the WAV files." -ForegroundColor Yellow
    }
}

if ([string]::IsNullOrWhiteSpace($OutputFolder)) {
    $outputLeaf = Split-Path -Leaf $resolvedAudioFolder
    $resolvedOutputFolder = Join-Path $repoRoot "missions\subtitles\$outputLeaf"
}
else {
    $resolvedOutputFolder = Resolve-ProjectPath $OutputFolder $false
}

New-Item -ItemType Directory -Force -Path $resolvedOutputFolder | Out-Null

$audioFiles = @(Get-ChildItem -LiteralPath $resolvedAudioFolder -Filter $AudioFilter -File | Sort-Object @{ Expression = { Get-FirstNumberFromFileName $_ } }, @{ Expression = { $_.Name } })

if ($audioFiles.Count -eq 0) {
    throw "No audio files matching '$AudioFilter' found in '$resolvedAudioFolder'."
}

if ($audioFiles.Count -lt 2) {
    throw "Expected multiple WAV files in '$resolvedAudioFolder', but found $($audioFiles.Count)."
}

$vttCues = Read-VttCues $resolvedVttPath

if ($vttCues.Count -eq 0) {
    throw "No subtitle cues found in '$resolvedVttPath'."
}

$segments = New-Object System.Collections.Generic.List[object]
$cursorMs = 0

foreach ($audioFile in $audioFiles) {
    $durationMs = Read-WavDurationMs $audioFile.FullName
    $segments.Add([pscustomobject]@{
        file = $audioFile
        startMs = $cursorMs
        endMs = $cursorMs + $durationMs
        durationMs = $durationMs
    })

    $cursorMs += $durationMs
}

$writtenCount = 0
$resolvedSplitMode = $SplitMode

if ($resolvedSplitMode -eq "Auto") {
    if ($vttCues.Count -eq $segments.Count) {
        $resolvedSplitMode = "ByCueOrder"
    }
    else {
        $resolvedSplitMode = "ByDuration"
    }
}

for ($segmentIndex = 0; $segmentIndex -lt $segments.Count; $segmentIndex++) {
    $segment = $segments[$segmentIndex]
    $segmentCues = New-Object System.Collections.Generic.List[object]

    if ($resolvedSplitMode -eq "ByCueOrder") {
        if ($segmentIndex -lt $vttCues.Count) {
            $segmentCues.Add((Convert-ToOrderedSubtitleCue $vttCues[$segmentIndex] $segment.durationMs))
        }
    }
    else {
        foreach ($cue in $vttCues) {
            $midpointMs = [int][Math]::Floor(($cue.atMs + $cue.endMs) / 2.0)

            if ($midpointMs -lt $segment.startMs -or $midpointMs -ge $segment.endMs) {
                continue
            }

            $converted = Convert-ToSubtitleCue $cue $segment.startMs $segment.endMs

            if ($null -ne $converted) {
                $segmentCues.Add($converted)
            }
        }
    }

    $subtitleObject = [pscustomobject]@{
        subtitles = $segmentCues.ToArray()
    }

    $baseName = [System.IO.Path]::GetFileNameWithoutExtension($segment.file.Name)
    $outputPath = Join-Path $resolvedOutputFolder "$baseName.subtitles.json"
    $subtitleObject | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $outputPath -Encoding UTF8
    $writtenCount++

    Write-Host ("{0}: {1} cue(s)" -f $segment.file.Name, $segmentCues.Count)
}

Write-Host ("Split mode: {0}" -f $resolvedSplitMode)
Write-Host ("Wrote {0} subtitle file(s) to {1}" -f $writtenCount, $resolvedOutputFolder) -ForegroundColor Green
