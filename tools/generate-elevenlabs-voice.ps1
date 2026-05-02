param(
    [string]$ConfigFile = "",
    [string]$VoiceId,
    [string]$ElevenLabsApiKey,
    [string]$TextFile,
    [string]$Text,
    [string]$OutName,
    [string]$ModelId = "eleven_multilingual_v2",
    [string]$LanguageCode = "pl",
    [string]$OutputFormat = "mp3_44100_192",
    [string]$AudioDir = ".\audio",
    [string]$SubtitleDir = ".\missions\subtitles",
    [string]$FfmpegPath = "",
    [string]$ChunkDelimiter = "|",
    [int]$SubtitleOffsetMs = 0,
    [double]$Speed = 0.73,
    [double]$Stability = 0.70,
    [double]$SimilarityBoost = 0.78,
    [double]$Style = 0.0,
    [bool]$UseSpeakerBoost = $true,
    [switch]$ConvertToWav
)

$ErrorActionPreference = "Stop"
$PostprocessingEnabled = $false
$PostprocessingOnly = $false
$PostprocessingOutputSuffix = "_phone"
$PostprocessingTelephone = $false
$PostprocessingNoise = 0.0
$PostprocessingNoiseProfile = "phone"
$PostprocessingHum = 0.0
$PostprocessingPan = 0.0
$PostprocessingVolume = 1.0

function Get-AudioExtension {
    param([string]$Format)

    if ($Format.StartsWith("mp3_")) { return "mp3" }
    if ($Format.StartsWith("wav_")) { return "wav" }
    if ($Format.StartsWith("pcm_")) { return "pcm" }
    if ($Format.StartsWith("ulaw_")) { return "ulaw" }

    return "audio"
}

function Has-JsonProperty {
    param(
        [object]$Object,
        [string]$Name
    )

    return $null -ne $Object -and $null -ne $Object.PSObject.Properties[$Name]
}

function Get-JsonValue {
    param(
        [object]$Object,
        [string]$Name,
        [object]$Fallback
    )

    if (Has-JsonProperty $Object $Name) {
        return $Object.PSObject.Properties[$Name].Value
    }

    return $Fallback
}

function Require-JsonSection {
    param(
        [object]$Object,
        [string]$Name
    )

    if (-not (Has-JsonProperty $Object $Name) -or $null -eq $Object.PSObject.Properties[$Name].Value) {
        throw "Config file requires '$Name' section."
    }

    return $Object.PSObject.Properties[$Name].Value
}

function Format-FilterNumber {
    param([double]$Value)

    return $Value.ToString("0.###", [System.Globalization.CultureInfo]::InvariantCulture)
}

function Clamp-Double {
    param(
        [double]$Value,
        [double]$Min,
        [double]$Max
    )

    return [Math]::Max($Min, [Math]::Min($Max, $Value))
}

function New-ChunkedText {
    param(
        [string]$RawText,
        [string]$Delimiter
    )

    $result = [pscustomobject]@{
        requestText = ""
        chunks = New-Object System.Collections.Generic.List[object]
    }

    if ([string]::IsNullOrEmpty($Delimiter) -or -not $RawText.Contains($Delimiter)) {
        $cleanText = $RawText.Trim()
        $result.requestText = $cleanText

        if (-not [string]::IsNullOrWhiteSpace($cleanText)) {
            $result.chunks.Add([pscustomobject]@{
                startIndex = 0
                endIndex = [Math]::Max(0, $cleanText.Length - 1)
                text = $cleanText
            })
        }

        return $result
    }

    $builder = New-Object System.Text.StringBuilder
    $parts = $RawText -split [Regex]::Escape($Delimiter)

    foreach ($part in $parts) {
        $chunkText = $part.Trim()

        if ([string]::IsNullOrWhiteSpace($chunkText)) {
            continue
        }

        if ($builder.Length -gt 0) {
            [void]$builder.Append(" ")
        }

        $startIndex = $builder.Length
        [void]$builder.Append($chunkText)
        $endIndex = $builder.Length - 1

        $result.chunks.Add([pscustomobject]@{
            startIndex = $startIndex
            endIndex = $endIndex
            text = $chunkText
        })
    }

    $result.requestText = $builder.ToString()
    return $result
}

function New-SubtitleCue {
    param(
        [object[]]$Characters,
        [object[]]$Starts,
        [object[]]$Ends,
        [int]$StartIndex,
        [int]$EndIndex
    )

    while ($StartIndex -le $EndIndex -and [string]::IsNullOrWhiteSpace([string]$Characters[$StartIndex])) {
        $StartIndex++
    }

    while ($EndIndex -ge $StartIndex -and [string]::IsNullOrWhiteSpace([string]$Characters[$EndIndex])) {
        $EndIndex--
    }

    if ($StartIndex -gt $EndIndex) {
        return $null
    }

    $textBuilder = New-Object System.Text.StringBuilder

    for ($i = $StartIndex; $i -le $EndIndex; $i++) {
        [void]$textBuilder.Append([string]$Characters[$i])
    }

    $text = $textBuilder.ToString().Trim()

    if ([string]::IsNullOrWhiteSpace($text) -or -not (Test-SpeakableSubtitleText $text)) {
        return $null
    }

    $atMs = [int][Math]::Round(([double]$Starts[$StartIndex]) * 1000.0)
    $endMs = [int][Math]::Round(([double]$Ends[$EndIndex]) * 1000.0)

    if ($endMs -le $atMs) {
        $endMs = $atMs + 1000
    }

    [pscustomobject]@{
        atMs = $atMs
        endMs = $endMs
        text = $text
    }
}

function Test-SpeakableSubtitleText {
    param([string]$Text)

    if ([string]::IsNullOrWhiteSpace($Text)) {
        return $false
    }

    for ($i = 0; $i -lt $Text.Length; $i++) {
        if ([char]::IsLetterOrDigit($Text[$i])) {
            return $true
        }
    }

    return $false
}

function Apply-SubtitleOffset {
    param(
        [object[]]$Cues,
        [int]$OffsetMs
    )

    if ($OffsetMs -eq 0) {
        return $Cues
    }

    foreach ($cue in $Cues) {
        $durationMs = [int]$cue.endMs - [int]$cue.atMs
        $cue.atMs = [Math]::Max(0, [int]$cue.atMs + $OffsetMs)
        $cue.endMs = [Math]::Max($cue.atMs + 1, $cue.atMs + $durationMs)
    }

    return $Cues
}

function Convert-AlignmentToChunkCues {
    param(
        [object]$Alignment,
        [object[]]$Chunks
    )

    if ($null -eq $Alignment) {
        throw "ElevenLabs response does not contain alignment data."
    }

    $characters = @($Alignment.characters)
    $starts = @($Alignment.character_start_times_seconds)
    $ends = @($Alignment.character_end_times_seconds)

    if ($characters.Count -eq 0 -or $starts.Count -ne $characters.Count -or $ends.Count -ne $characters.Count) {
        throw "ElevenLabs alignment arrays are missing or inconsistent."
    }

    $cues = New-Object System.Collections.Generic.List[object]

    foreach ($chunk in $Chunks) {
        if (-not (Test-SpeakableSubtitleText ([string]$chunk.text))) {
            continue
        }

        $startIndex = [int]$chunk.startIndex
        $endIndex = [int]$chunk.endIndex

        if ($startIndex -ge $characters.Count) {
            continue
        }

        if ($endIndex -ge $characters.Count) {
            $endIndex = $characters.Count - 1
        }

        while ($startIndex -le $endIndex -and [string]::IsNullOrWhiteSpace([string]$characters[$startIndex])) {
            $startIndex++
        }

        while ($endIndex -ge $startIndex -and [string]::IsNullOrWhiteSpace([string]$characters[$endIndex])) {
            $endIndex--
        }

        if ($startIndex -gt $endIndex) {
            continue
        }

        $atMs = [int][Math]::Round(([double]$starts[$startIndex]) * 1000.0)
        $endMs = [int][Math]::Round(([double]$ends[$endIndex]) * 1000.0)

        if ($endMs -le $atMs) {
            $endMs = $atMs + 1000
        }

        $cues.Add([pscustomobject]@{
            atMs = $atMs
            endMs = $endMs
            text = [string]$chunk.text
        })
    }

    return $cues
}

function Convert-AlignmentToSubtitleCues {
    param([object]$Alignment)

    if ($null -eq $Alignment) {
        throw "ElevenLabs response does not contain alignment data."
    }

    $characters = @($Alignment.characters)
    $starts = @($Alignment.character_start_times_seconds)
    $ends = @($Alignment.character_end_times_seconds)

    if ($characters.Count -eq 0 -or $starts.Count -ne $characters.Count -or $ends.Count -ne $characters.Count) {
        throw "ElevenLabs alignment arrays are missing or inconsistent."
    }

    $cues = New-Object System.Collections.Generic.List[object]
    $segmentStart = 0
    $sentenceEndChars = ".?!..."

    for ($i = 0; $i -lt $characters.Count; $i++) {
        $char = [string]$characters[$i]
        $isNewLine = $char -eq "`n" -or $char -eq "`r"
        $isSentenceEnd = $sentenceEndChars.Contains($char)

        if (-not $isNewLine -and -not $isSentenceEnd) {
            continue
        }

        $cue = New-SubtitleCue -Characters $characters -Starts $starts -Ends $ends -StartIndex $segmentStart -EndIndex $i

        if ($null -ne $cue) {
            $cues.Add($cue)
        }

        $segmentStart = $i + 1
    }

    if ($segmentStart -lt $characters.Count) {
        $cue = New-SubtitleCue -Characters $characters -Starts $starts -Ends $ends -StartIndex $segmentStart -EndIndex ($characters.Count - 1)

        if ($null -ne $cue) {
            $cues.Add($cue)
        }
    }

    return $cues
}

function Resolve-FfmpegPath {
    param([string]$ConfiguredPath)

    if (-not [string]::IsNullOrWhiteSpace($ConfiguredPath)) {
        $resolvedConfiguredPath = if ([System.IO.Path]::IsPathRooted($ConfiguredPath)) {
            $ConfiguredPath
        }
        else {
            Join-Path $repoRoot $ConfiguredPath
        }

        if (Test-Path -LiteralPath $resolvedConfiguredPath) {
            return (Resolve-Path -LiteralPath $resolvedConfiguredPath).Path
        }

        Write-Warning "Configured ffmpegPath was not found: $resolvedConfiguredPath"
    }

    $fromPath = Get-Command ffmpeg -ErrorAction SilentlyContinue

    if ($null -ne $fromPath) {
        return $fromPath.Source
    }

    $commonPaths = @(
        "C:\ffmpeg\bin\ffmpeg.exe",
        "C:\Program Files\ffmpeg\bin\ffmpeg.exe",
        "C:\Program Files\Krita (x64)\bin\ffmpeg.exe"
    )

    foreach ($path in $commonPaths) {
        if (Test-Path -LiteralPath $path) {
            return $path
        }
    }

    return ""
}

function Resolve-ExistingAudioPath {
    param(
        [string]$Directory,
        [string]$BaseName,
        [string]$ExcludeBaseName
    )

    if ([string]::IsNullOrWhiteSpace($BaseName)) {
        return ""
    }

    if (-not [string]::IsNullOrWhiteSpace([System.IO.Path]::GetExtension($BaseName))) {
        $exactPath = Join-Path $Directory $BaseName

        if (Test-Path -LiteralPath $exactPath) {
            return (Resolve-Path -LiteralPath $exactPath).Path
        }
    }

    $extensions = @("mp3", "wav", "pcm", "ulaw", "audio")

    foreach ($extension in $extensions) {
        $candidateName = "$BaseName.$extension"

        if ($candidateName -eq "$ExcludeBaseName.$extension") {
            continue
        }

        $candidatePath = Join-Path $Directory $candidateName

        if (Test-Path -LiteralPath $candidatePath) {
            return (Resolve-Path -LiteralPath $candidatePath).Path
        }
    }

    return ""
}

function New-PostprocessingFilter {
    param(
        [bool]$Enabled,
        [bool]$Telephone,
        [double]$Pan,
        [double]$Volume
    )

    if (-not $Enabled) {
        return ""
    }

    $filters = New-Object System.Collections.Generic.List[string]

    if ($Telephone) {
        $filters.Add("aformat=channel_layouts=mono") | Out-Null
        $filters.Add("highpass=f=300") | Out-Null
        $filters.Add("lowpass=f=3400") | Out-Null
        $filters.Add("acompressor=threshold=-18dB:ratio=3:attack=5:release=80") | Out-Null
    }

    if ([Math]::Abs($Volume - 1.0) -gt 0.001) {
        $filters.Add("volume=$(Format-FilterNumber $Volume)") | Out-Null
    }

    $clampedPan = Clamp-Double $Pan -1.0 1.0

    if ([Math]::Abs($clampedPan) -gt 0.001) {
        $leftGain = if ($clampedPan -lt 0) { 1.0 } else { 1.0 - $clampedPan }
        $rightGain = if ($clampedPan -gt 0) { 1.0 } else { 1.0 + $clampedPan }
        $filters.Add("pan=stereo|FL=$(Format-FilterNumber $leftGain)*c0|FR=$(Format-FilterNumber $rightGain)*c0") | Out-Null
    }

    if ($filters.Count -eq 0) {
        return "anull"
    }

    return ($filters -join ",")
}

function New-PostprocessingNoiseMix {
    param(
        [double]$Noise,
        [string]$Profile,
        [double]$Hum
    )

    $noiseLevel = Clamp-Double $Noise 0.0 1.0
    $humLevel = Clamp-Double $Hum 0.0 1.0

    if ($noiseLevel -le 0.0 -and $humLevel -le 0.0) {
        return $null
    }

    $graphParts = New-Object System.Collections.Generic.List[string]
    $labels = New-Object System.Collections.Generic.List[string]
    $normalizedProfile = if ([string]::IsNullOrWhiteSpace($Profile)) { "phone" } else { $Profile.ToLowerInvariant() }

    if ($noiseLevel -gt 0.0) {
        if ($normalizedProfile -eq "simple") {
            $graphParts.Add("anoisesrc=color=white:amplitude=$(Format-FilterNumber $noiseLevel)[noise_simple]") | Out-Null
            $labels.Add("[noise_simple]") | Out-Null
        }
        else {
            $hiss = $noiseLevel * 0.65
            $lineBed = $noiseLevel * 0.35
            $edge = $noiseLevel * 0.18

            $graphParts.Add("anoisesrc=color=white:amplitude=$(Format-FilterNumber $hiss),highpass=f=1800,lowpass=f=7200[hiss]") | Out-Null
            $graphParts.Add("anoisesrc=color=pink:amplitude=$(Format-FilterNumber $lineBed),highpass=f=220,lowpass=f=1800[linebed]") | Out-Null
            $graphParts.Add("anoisesrc=color=white:amplitude=$(Format-FilterNumber $edge),highpass=f=5000,lowpass=f=9000[edge]") | Out-Null
            $labels.Add("[hiss]") | Out-Null
            $labels.Add("[linebed]") | Out-Null
            $labels.Add("[edge]") | Out-Null
        }
    }

    if ($humLevel -gt 0.0) {
        $graphParts.Add("sine=frequency=60:sample_rate=44100,volume=$(Format-FilterNumber $humLevel)[hum]") | Out-Null
        $labels.Add("[hum]") | Out-Null
    }

    if ($labels.Count -eq 1) {
        return [pscustomobject]@{
            graph = ($graphParts -join ";")
            label = $labels[0]
        }
    }

    $labelChain = ""

    foreach ($label in $labels) {
        $labelChain += $label
    }

    $graphParts.Add("$labelChain" + "amix=inputs=$($labels.Count):duration=first:dropout_transition=0[phone_noise]") | Out-Null

    return [pscustomobject]@{
        graph = ($graphParts -join ";")
        label = "[phone_noise]"
    }
}

function Invoke-WavConversion {
    param(
        [string]$FfmpegPath,
        [string]$InputPath,
        [string]$OutputPath,
        [bool]$PostprocessingEnabled,
        [bool]$PostprocessingTelephone,
        [double]$PostprocessingPan,
        [double]$PostprocessingVolume,
        [double]$PostprocessingNoise,
        [string]$PostprocessingNoiseProfile,
        [double]$PostprocessingHum
    )

    $postprocessingFilter = New-PostprocessingFilter `
        -Enabled $PostprocessingEnabled `
        -Telephone $PostprocessingTelephone `
        -Pan $PostprocessingPan `
        -Volume $PostprocessingVolume
    $postprocessingNoiseMix = New-PostprocessingNoiseMix `
        -Noise $PostprocessingNoise `
        -Profile $PostprocessingNoiseProfile `
        -Hum $PostprocessingHum

    if ($PostprocessingEnabled -and $null -ne $postprocessingNoiseMix) {
        $filterComplex = "[0:a]$postprocessingFilter[voice];$($postprocessingNoiseMix.graph);[voice]$($postprocessingNoiseMix.label)amix=inputs=2:duration=first:dropout_transition=0[a]"
        & $FfmpegPath -y -i $InputPath -filter_complex $filterComplex -map "[a]" -ar 44100 -ac 2 -c:a pcm_s16le $OutputPath | Out-Null
    }
    elseif ($PostprocessingEnabled) {
        & $FfmpegPath -y -i $InputPath -af $postprocessingFilter -ar 44100 -ac 2 -c:a pcm_s16le $OutputPath | Out-Null
    }
    else {
        & $FfmpegPath -y -i $InputPath -ar 44100 -ac 2 -c:a pcm_s16le $OutputPath | Out-Null
    }
}

$repoRoot = Split-Path -Parent $PSScriptRoot

if (-not [string]::IsNullOrWhiteSpace($ConfigFile)) {
    $resolvedConfigFile = if ([System.IO.Path]::IsPathRooted($ConfigFile)) { $ConfigFile } else { Join-Path $repoRoot $ConfigFile }

    if (-not (Test-Path -LiteralPath $resolvedConfigFile)) {
        throw "Config file not found: $resolvedConfigFile"
    }

    $config = Get-Content -LiteralPath $resolvedConfigFile -Raw -Encoding UTF8 | ConvertFrom-Json
    $voiceConfig = Require-JsonSection $config "voiceConfig"
    $elevenLabsConfig = Require-JsonSection $config "elevenLabs"
    $postprocessing = Require-JsonSection $config "postprocessing"

    $TextFile = Get-JsonValue $voiceConfig "textFile" $TextFile
    $Text = Get-JsonValue $voiceConfig "text" $Text
    $OutName = Get-JsonValue $voiceConfig "outName" $OutName
    $AudioDir = Get-JsonValue $voiceConfig "audioDir" $AudioDir
    $SubtitleDir = Get-JsonValue $voiceConfig "subtitleDir" $SubtitleDir
    $FfmpegPath = Get-JsonValue $voiceConfig "ffmpegPath" $FfmpegPath
    $ChunkDelimiter = Get-JsonValue $voiceConfig "chunkDelimiter" $ChunkDelimiter
    $SubtitleOffsetMs = [int](Get-JsonValue $voiceConfig "subtitleOffsetMs" $SubtitleOffsetMs)
    $PostprocessingOnly = [bool](Get-JsonValue $voiceConfig "onlyPostprocessing" $PostprocessingOnly)

    $ElevenLabsApiKey = Get-JsonValue $elevenLabsConfig "apiKey" $ElevenLabsApiKey
    $VoiceId = Get-JsonValue $elevenLabsConfig "voiceId" $VoiceId
    $ModelId = Get-JsonValue $elevenLabsConfig "modelId" $ModelId
    $LanguageCode = Get-JsonValue $elevenLabsConfig "languageCode" $LanguageCode
    $OutputFormat = Get-JsonValue $elevenLabsConfig "outputFormat" $OutputFormat

    if (Has-JsonProperty $elevenLabsConfig "convertToWav") {
        $ConvertToWav = [bool]$elevenLabsConfig.convertToWav
    }

    if (Has-JsonProperty $elevenLabsConfig "voiceSettings") {
        $voiceSettings = $elevenLabsConfig.voiceSettings
        $Speed = [double](Get-JsonValue $voiceSettings "speed" $Speed)
        $Stability = [double](Get-JsonValue $voiceSettings "stability" $Stability)
        $SimilarityBoost = [double](Get-JsonValue $voiceSettings "similarityBoost" $SimilarityBoost)
        $SimilarityBoost = [double](Get-JsonValue $voiceSettings "similarity_boost" $SimilarityBoost)
        $Style = [double](Get-JsonValue $voiceSettings "style" $Style)
        $UseSpeakerBoost = [bool](Get-JsonValue $voiceSettings "useSpeakerBoost" $UseSpeakerBoost)
        $UseSpeakerBoost = [bool](Get-JsonValue $voiceSettings "use_speaker_boost" $UseSpeakerBoost)
    }

    $PostprocessingEnabled = [bool](Get-JsonValue $postprocessing "enabled" $PostprocessingEnabled)
    $PostprocessingOutputSuffix = [string](Get-JsonValue $postprocessing "outputSuffix" $PostprocessingOutputSuffix)
    $PostprocessingTelephone = [bool](Get-JsonValue $postprocessing "telephone" $PostprocessingTelephone)
    $PostprocessingNoise = [double](Get-JsonValue $postprocessing "noise" $PostprocessingNoise)
    $PostprocessingNoiseProfile = [string](Get-JsonValue $postprocessing "noiseProfile" $PostprocessingNoiseProfile)
    $PostprocessingHum = [double](Get-JsonValue $postprocessing "hum" $PostprocessingHum)
    $PostprocessingPan = [double](Get-JsonValue $postprocessing "pan" $PostprocessingPan)
    $PostprocessingVolume = [double](Get-JsonValue $postprocessing "volume" $PostprocessingVolume)
}

$resolvedAudioDir = if ([System.IO.Path]::IsPathRooted($AudioDir)) { $AudioDir } else { Join-Path $repoRoot $AudioDir }
$resolvedSubtitleDir = if ([System.IO.Path]::IsPathRooted($SubtitleDir)) { $SubtitleDir } else { Join-Path $repoRoot $SubtitleDir }
$resolvedApiKey = if ([string]::IsNullOrWhiteSpace($ElevenLabsApiKey)) { $env:ELEVENLABS_API_KEY } else { $ElevenLabsApiKey }

if (-not $PostprocessingOnly -and [string]::IsNullOrWhiteSpace($VoiceId)) {
    throw "VoiceId is required. Set voiceId in config or pass -VoiceId."
}

if ([string]::IsNullOrWhiteSpace($OutName)) {
    throw "OutName is required. Set outName in config or pass -OutName."
}

New-Item -ItemType Directory -Force -Path $resolvedAudioDir | Out-Null
New-Item -ItemType Directory -Force -Path $resolvedSubtitleDir | Out-Null

if ($PostprocessingOnly) {
    if (-not $PostprocessingEnabled) {
        throw "voiceConfig.onlyPostprocessing requires postprocessing.enabled=true."
    }

    $ffmpegPath = Resolve-FfmpegPath -ConfiguredPath $FfmpegPath

    if ([string]::IsNullOrWhiteSpace($ffmpegPath)) {
        throw "ffmpeg not found. Set ffmpegPath in config or install ffmpeg in PATH."
    }

    $variantSuffix = if ([string]::IsNullOrWhiteSpace($PostprocessingOutputSuffix)) { "_phone" } else { $PostprocessingOutputSuffix }
    $sourceBaseName = if ([string]::IsNullOrWhiteSpace([System.IO.Path]::GetExtension($OutName))) {
        $OutName
    }
    else {
        [System.IO.Path]::GetFileNameWithoutExtension($OutName)
    }
    $variantBaseName = "$sourceBaseName$variantSuffix"
    $sourceAudioPath = Resolve-ExistingAudioPath `
        -Directory $resolvedAudioDir `
        -BaseName $OutName `
        -ExcludeBaseName $variantBaseName

    if ([string]::IsNullOrWhiteSpace($sourceAudioPath)) {
        throw "Postprocessing source audio not found. Expected audio/$OutName.mp3 or audio/$OutName.wav."
    }

    $variantAudioFileName = "$variantBaseName.wav"
    $variantAudioPath = Join-Path $resolvedAudioDir $variantAudioFileName

    Write-Host "Postprocessing existing audio without ElevenLabs: $sourceAudioPath"
    Invoke-WavConversion `
        -FfmpegPath $ffmpegPath `
        -InputPath $sourceAudioPath `
        -OutputPath $variantAudioPath `
        -PostprocessingEnabled $PostprocessingEnabled `
        -PostprocessingTelephone $PostprocessingTelephone `
        -PostprocessingPan $PostprocessingPan `
        -PostprocessingVolume $PostprocessingVolume `
        -PostprocessingNoise $PostprocessingNoise `
        -PostprocessingNoiseProfile $PostprocessingNoiseProfile `
        -PostprocessingHum $PostprocessingHum

    $subtitleFileName = ""
    $subtitleCandidates = @(
        "$OutName.subtitles.json",
        "$OutName.json"
    )

    foreach ($candidate in $subtitleCandidates) {
        if (Test-Path -LiteralPath (Join-Path $resolvedSubtitleDir $candidate)) {
            $subtitleFileName = $candidate
            break
        }
    }

    $snippet = [ordered]@{
        audio = $variantAudioFileName
    }

    if (-not [string]::IsNullOrWhiteSpace($subtitleFileName)) {
        $snippet.subtitlesFile = "subtitles/$subtitleFileName"
    }

    $snippetPath = Join-Path $resolvedSubtitleDir "$variantBaseName.node-snippet.json"
    [pscustomobject]$snippet | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath $snippetPath -Encoding UTF8

    Write-Host "Postprocessed audio: $variantAudioPath"
    Write-Host "Mission node snippet: $snippetPath"
    return
}

if ([string]::IsNullOrWhiteSpace($resolvedApiKey)) {
    throw "Set apiKey in local config or ELEVENLABS_API_KEY in PowerShell."
}

$sourceTextName = "inline"

if ([string]::IsNullOrWhiteSpace($Text)) {
    if ([string]::IsNullOrWhiteSpace($TextFile)) {
        throw "Text is required. Set text/textFile in config or pass -TextFile."
    }

    $resolvedTextFile = if ([System.IO.Path]::IsPathRooted($TextFile)) { $TextFile } else { Join-Path $repoRoot $TextFile }
    $resolvedTextFile = (Resolve-Path -LiteralPath $resolvedTextFile).Path
    $Text = Get-Content -LiteralPath $resolvedTextFile -Raw -Encoding UTF8
    $sourceTextName = Split-Path -Leaf $resolvedTextFile
}

if ([string]::IsNullOrWhiteSpace($Text)) {
    throw "Text is empty."
}

$chunkedText = New-ChunkedText -RawText $Text -Delimiter $ChunkDelimiter

if ([string]::IsNullOrWhiteSpace($chunkedText.requestText)) {
    throw "Text has no speakable content after chunk parsing."
}

$body = @{
    text = $chunkedText.requestText
    model_id = $ModelId
    language_code = $LanguageCode
    voice_settings = @{
        speed = $Speed
        stability = $Stability
        similarity_boost = $SimilarityBoost
        style = $Style
        use_speaker_boost = $UseSpeakerBoost
    }
} | ConvertTo-Json -Depth 8

$encodedVoiceId = [System.Uri]::EscapeDataString($VoiceId)
$uri = "https://api.elevenlabs.io/v1/text-to-speech/$encodedVoiceId/with-timestamps?output_format=$OutputFormat"

Write-Host "Generating ElevenLabs voice: $OutName"

$response = Invoke-RestMethod `
    -Method Post `
    -Uri $uri `
    -Headers @{ "xi-api-key" = $resolvedApiKey } `
    -ContentType "application/json" `
    -Body $body

if ([string]::IsNullOrWhiteSpace($response.audio_base64)) {
    throw "ElevenLabs response does not contain audio_base64."
}

$audioExtension = Get-AudioExtension $OutputFormat
$audioFileName = "$OutName.$audioExtension"
$audioPath = Join-Path $resolvedAudioDir $audioFileName
[System.IO.File]::WriteAllBytes($audioPath, [System.Convert]::FromBase64String($response.audio_base64))

$alignment = if ($null -ne $response.alignment) { $response.alignment } else { $response.normalized_alignment }

if ($chunkedText.chunks.Count -gt 1) {
    $cues = Convert-AlignmentToChunkCues -Alignment $alignment -Chunks $chunkedText.chunks.ToArray()
}
else {
    $cues = Convert-AlignmentToSubtitleCues -Alignment $alignment
}

$cues = Apply-SubtitleOffset -Cues @($cues) -OffsetMs $SubtitleOffsetMs

$subtitlePath = Join-Path $resolvedSubtitleDir "$OutName.subtitles.json"

$nodeAudioFileName = $audioFileName

if ($ConvertToWav -and $audioExtension -ne "wav") {
    $ffmpegPath = Resolve-FfmpegPath -ConfiguredPath $FfmpegPath

    if ([string]::IsNullOrWhiteSpace($ffmpegPath)) {
        Write-Warning "ffmpeg not found. Kept $audioFileName. The current mod audio player expects WAV files."
    }
    else {
        $wavPath = Join-Path $resolvedAudioDir "$OutName.wav"
        Invoke-WavConversion `
            -FfmpegPath $ffmpegPath `
            -InputPath $audioPath `
            -OutputPath $wavPath `
            -PostprocessingEnabled $PostprocessingEnabled `
            -PostprocessingTelephone $PostprocessingTelephone `
            -PostprocessingPan $PostprocessingPan `
            -PostprocessingVolume $PostprocessingVolume `
            -PostprocessingNoise $PostprocessingNoise `
            -PostprocessingNoiseProfile $PostprocessingNoiseProfile `
            -PostprocessingHum $PostprocessingHum

        $nodeAudioFileName = "$OutName.wav"
    }
}

$subtitleObject = [pscustomobject]@{
    audio = $nodeAudioFileName
    sourceText = $sourceTextName
    chunkDelimiter = $ChunkDelimiter
    subtitleOffsetMs = $SubtitleOffsetMs
    postprocessing = [pscustomobject]@{
        enabled = $PostprocessingEnabled
        onlyPostprocessing = $PostprocessingOnly
        outputSuffix = $PostprocessingOutputSuffix
        telephone = $PostprocessingTelephone
        noise = $PostprocessingNoise
        noiseProfile = $PostprocessingNoiseProfile
        hum = $PostprocessingHum
        pan = $PostprocessingPan
        volume = $PostprocessingVolume
    }
    generatedAtUtc = (Get-Date).ToUniversalTime().ToString("o")
    subtitles = @($cues)
}

$subtitleObject | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $subtitlePath -Encoding UTF8

$relativeSubtitlePath = "subtitles/$OutName.subtitles.json"
$snippet = [pscustomobject]@{
    audio = $nodeAudioFileName
    subtitlesFile = $relativeSubtitlePath
}

$snippetPath = Join-Path $resolvedSubtitleDir "$OutName.node-snippet.json"
$snippet | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath $snippetPath -Encoding UTF8

Write-Host "Audio: $audioPath"
Write-Host "Subtitles: $subtitlePath"
Write-Host "Mission node snippet: $snippetPath"

if ($nodeAudioFileName.EndsWith(".mp3")) {
    Write-Warning "Generated MP3. Current MissionRuntime uses SoundPlayer, so convert/copy a WAV before using it in-game."
}
