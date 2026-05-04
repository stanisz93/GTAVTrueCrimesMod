$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
$generatorPath = Join-Path $repoRoot "tools\generate-elevenlabs-voice.ps1"
$vttConverterPath = Join-Path $repoRoot "tools\convert-vtt-subtitles.ps1"
$exampleConfigPath = Join-Path $repoRoot "tools\voice_generator_config.example.json"
$localConfigPath = Join-Path $repoRoot "tools\voice_generator_config.json"
$legacyConfigName = "elevenlabs" + "-voice" + ".config"
$legacyExampleConfigPath = Join-Path $repoRoot ("tools\" + $legacyConfigName + ".example.json")
$legacyLocalConfigPath = Join-Path $repoRoot ("tools\" + $legacyConfigName + ".json")
$tasksPath = Join-Path $repoRoot ".vscode\tasks.json"
$errors = New-Object System.Collections.Generic.List[string]

function Add-Error {
    param([string]$Message)

    $errors.Add($Message)
}

function Has-Property {
    param(
        [object]$Object,
        [string]$Name
    )

    return $null -ne $Object -and $null -ne $Object.PSObject.Properties[$Name]
}

function Assert-HasProperty {
    param(
        [object]$Object,
        [string]$Name,
        [string]$Context
    )

    if (-not (Has-Property $Object $Name)) {
        Add-Error "$Context missing '$Name'"
    }
}

function Assert-NoProperty {
    param(
        [object]$Object,
        [string]$Name,
        [string]$Context
    )

    if (Has-Property $Object $Name) {
        Add-Error "$Context should not contain legacy '$Name'"
    }
}

function Test-VoiceConfigSchema {
    param(
        [object]$Config,
        [string]$Name
    )

    Assert-HasProperty $Config "voiceConfig" $Name
    Assert-HasProperty $Config "elevenLabs" $Name
    Assert-HasProperty $Config "postprocessing" $Name

    if ((Has-Property $Config "voiceConfig") -and (Has-Property $Config "elevenLabs") -and (Has-Property $Config "postprocessing")) {
        Assert-HasProperty $Config.voiceConfig "outName" "$Name.voiceConfig"
        Assert-HasProperty $Config.voiceConfig "characterPrefix" "$Name.voiceConfig"
        Assert-HasProperty $Config.voiceConfig "onlyPostprocessing" "$Name.voiceConfig"
        Assert-NoProperty $Config.voiceConfig "only" "$Name.voiceConfig"
        Assert-HasProperty $Config.voiceConfig "chunkDelimiter" "$Name.voiceConfig"
        Assert-HasProperty $Config.voiceConfig "subtitleOffsetMs" "$Name.voiceConfig"
        Assert-HasProperty $Config.voiceConfig "audioDir" "$Name.voiceConfig"
        Assert-HasProperty $Config.voiceConfig "subtitleDir" "$Name.voiceConfig"

        Assert-HasProperty $Config.elevenLabs "voiceId" "$Name.elevenLabs"
        Assert-HasProperty $Config.elevenLabs "modelId" "$Name.elevenLabs"
        Assert-HasProperty $Config.elevenLabs "outputFormat" "$Name.elevenLabs"
        Assert-HasProperty $Config.elevenLabs "convertToWav" "$Name.elevenLabs"
        Assert-HasProperty $Config.elevenLabs "voiceSettings" "$Name.elevenLabs"

        Assert-HasProperty $Config.postprocessing "enabled" "$Name.postprocessing"
        Assert-HasProperty $Config.postprocessing "outputSuffix" "$Name.postprocessing"
        Assert-HasProperty $Config.postprocessing "telephone" "$Name.postprocessing"
        Assert-HasProperty $Config.postprocessing "noiseProfile" "$Name.postprocessing"
        Assert-HasProperty $Config.postprocessing "noise" "$Name.postprocessing"
        Assert-HasProperty $Config.postprocessing "hum" "$Name.postprocessing"
        Assert-HasProperty $Config.postprocessing "pan" "$Name.postprocessing"
        Assert-HasProperty $Config.postprocessing "volume" "$Name.postprocessing"
        Assert-NoProperty $Config.postprocessing "only" "$Name.postprocessing"
    }

    $legacyRootKeys = @(
        "apiKey",
        "voiceId",
        "modelId",
        "languageCode",
        "outputFormat",
        "convertToWav",
        "voiceSettings",
        "outName",
        "text",
        "textFile",
        "chunkDelimiter",
        "subtitleOffsetMs",
        "audioDir",
        "subtitleDir",
        "ffmpegPath",
        "only",
        "onlyPostprocessing"
    )

    foreach ($key in $legacyRootKeys) {
        Assert-NoProperty $Config $key $Name
    }
}

function Write-TestWav {
    param(
        [string]$Path,
        [int]$DurationMs
    )

    $sampleRate = 8000
    $channels = 1
    $bitsPerSample = 16
    $blockAlign = [int]($channels * $bitsPerSample / 8)
    $byteRate = $sampleRate * $blockAlign
    $sampleCount = [int][Math]::Round($sampleRate * ($DurationMs / 1000.0))
    $dataSize = $sampleCount * $blockAlign
    $writer = New-Object System.IO.BinaryWriter([System.IO.File]::Create($Path))

    try {
        $writer.Write([System.Text.Encoding]::ASCII.GetBytes("RIFF"))
        $writer.Write([uint32](36 + $dataSize))
        $writer.Write([System.Text.Encoding]::ASCII.GetBytes("WAVE"))
        $writer.Write([System.Text.Encoding]::ASCII.GetBytes("fmt "))
        $writer.Write([uint32]16)
        $writer.Write([uint16]1)
        $writer.Write([uint16]$channels)
        $writer.Write([uint32]$sampleRate)
        $writer.Write([uint32]$byteRate)
        $writer.Write([uint16]$blockAlign)
        $writer.Write([uint16]$bitsPerSample)
        $writer.Write([System.Text.Encoding]::ASCII.GetBytes("data"))
        $writer.Write([uint32]$dataSize)

        for ($i = 0; $i -lt $sampleCount; $i++) {
            $writer.Write([int16]0)
        }
    }
    finally {
        $writer.Dispose()
    }
}

function Test-VttSubtitleSplitter {
    $tempRoot = Join-Path ([System.IO.Path]::GetTempPath()) ("GTAVTrueCrimesMod_VttTest_" + [guid]::NewGuid().ToString("N"))

    try {
        $audioDir = Join-Path $tempRoot "audio"
        $outputDir = Join-Path $tempRoot "subtitles"
        New-Item -ItemType Directory -Force -Path $audioDir | Out-Null
        New-Item -ItemType Directory -Force -Path $outputDir | Out-Null

        Write-TestWav (Join-Path $audioDir "z_01_line.wav") 2000
        Write-TestWav (Join-Path $audioDir "a_02_line.wav") 2000

        $vttPath = Join-Path $audioDir "conversation.vtt"
        @"
WEBVTT

00:00:00.000 --> 00:00:00.800
First line.

00:00:01.100 --> 00:00:01.900
Second line.

00:00:02.100 --> 00:00:02.900
Third line.

00:00:03.100 --> 00:00:03.500
.
"@ | Set-Content -LiteralPath $vttPath -Encoding UTF8

        & $vttConverterPath -AudioFolder $audioDir -OutputFolder $outputDir *> $null

        $firstPath = Join-Path $outputDir "z_01_line.subtitles.json"
        $secondPath = Join-Path $outputDir "a_02_line.subtitles.json"

        if (-not (Test-Path -LiteralPath $firstPath)) {
            Add-Error "VTT splitter did not write first subtitle file"
            return
        }

        if (-not (Test-Path -LiteralPath $secondPath)) {
            Add-Error "VTT splitter did not write second subtitle file"
            return
        }

        $first = Get-Content -LiteralPath $firstPath -Raw -Encoding UTF8 | ConvertFrom-Json
        $second = Get-Content -LiteralPath $secondPath -Raw -Encoding UTF8 | ConvertFrom-Json
        $firstCues = @($first.subtitles)
        $secondCues = @($second.subtitles)

        if ($firstCues.Count -ne 2) {
            Add-Error "VTT splitter expected 2 cues in first subtitle file, got $($firstCues.Count)"
        }

        if ($secondCues.Count -ne 1) {
            Add-Error "VTT splitter expected 1 cue in second subtitle file, got $($secondCues.Count)"
        }

        if ($secondCues.Count -gt 0 -and $secondCues[0].atMs -ne 100) {
            Add-Error "VTT splitter did not convert second segment cue to local time"
        }

        $extraVttPath = Join-Path $audioDir "extra.vtt"
        Copy-Item -LiteralPath $vttPath -Destination $extraVttPath

        try {
            & $vttConverterPath -AudioFolder $audioDir -OutputFolder $outputDir *> $null
            Add-Error "VTT splitter accepted multiple VTT files in the audio folder"
        }
        catch {
            if (-not $_.Exception.Message.Contains("Expected exactly one VTT file")) {
                Add-Error "VTT splitter failed with unexpected multiple-VTT error: $($_.Exception.Message)"
            }
        }

        Remove-Item -LiteralPath $extraVttPath -Force
    }
    catch {
        Add-Error "VTT splitter test failed: $($_.Exception.Message)"
    }
    finally {
        $tempPath = [System.IO.Path]::GetFullPath($tempRoot)
        $tempRootPath = [System.IO.Path]::GetFullPath([System.IO.Path]::GetTempPath())

        if ($tempPath.StartsWith($tempRootPath, [System.StringComparison]::OrdinalIgnoreCase) -and (Test-Path -LiteralPath $tempPath)) {
            Remove-Item -LiteralPath $tempPath -Recurse -Force
        }
    }
}

function Test-VttSubtitleSplitterCueOrderMode {
    $tempRoot = Join-Path ([System.IO.Path]::GetTempPath()) ("GTAVTrueCrimesMod_VttOrderTest_" + [guid]::NewGuid().ToString("N"))

    try {
        $audioDir = Join-Path $tempRoot "audio"
        $outputDir = Join-Path $tempRoot "subtitles"
        New-Item -ItemType Directory -Force -Path $audioDir | Out-Null
        New-Item -ItemType Directory -Force -Path $outputDir | Out-Null

        Write-TestWav (Join-Path $audioDir "z_01_line.wav") 2000
        Write-TestWav (Join-Path $audioDir "a_02_line.wav") 2000

        $vttPath = Join-Path $audioDir "conversation.vtt"
        @"
WEBVTT

00:00:00.000 --> 00:00:03.000
First long line.

00:00:03.000 --> 00:00:03.500
Second short line.
"@ | Set-Content -LiteralPath $vttPath -Encoding UTF8

        & $vttConverterPath -AudioFolder $audioDir -OutputFolder $outputDir *> $null

        $firstPath = Join-Path $outputDir "z_01_line.subtitles.json"
        $secondPath = Join-Path $outputDir "a_02_line.subtitles.json"
        $first = Get-Content -LiteralPath $firstPath -Raw -Encoding UTF8 | ConvertFrom-Json
        $second = Get-Content -LiteralPath $secondPath -Raw -Encoding UTF8 | ConvertFrom-Json
        $firstCues = @($first.subtitles)
        $secondCues = @($second.subtitles)

        if ($firstCues.Count -ne 1 -or $firstCues[0].text -ne "First long line.") {
            Add-Error "VTT splitter auto mode did not map first cue by order"
        }

        if ($secondCues.Count -ne 1 -or $secondCues[0].text -ne "Second short line.") {
            Add-Error "VTT splitter auto mode did not map second cue by order"
        }
    }
    catch {
        Add-Error "VTT cue-order splitter test failed: $($_.Exception.Message)"
    }
    finally {
        $tempPath = [System.IO.Path]::GetFullPath($tempRoot)
        $tempRootPath = [System.IO.Path]::GetFullPath([System.IO.Path]::GetTempPath())

        if ($tempPath.StartsWith($tempRootPath, [System.StringComparison]::OrdinalIgnoreCase) -and (Test-Path -LiteralPath $tempPath)) {
            Remove-Item -LiteralPath $tempPath -Recurse -Force
        }
    }
}

foreach ($scriptToParse in @($generatorPath, $vttConverterPath)) {
    $tokens = $null
    $parseErrors = $null
    [System.Management.Automation.Language.Parser]::ParseFile($scriptToParse, [ref]$tokens, [ref]$parseErrors) | Out-Null

    if ($parseErrors.Count -gt 0) {
        foreach ($parseError in $parseErrors) {
            Add-Error "$(Split-Path -Leaf $scriptToParse) syntax: $($parseError.Message)"
        }
    }
}

$scriptText = Get-Content -LiteralPath $generatorPath -Raw -Encoding UTF8
if (Test-Path -LiteralPath $legacyExampleConfigPath) {
    Add-Error "legacy example config file still exists"
}

if (Test-Path -LiteralPath $legacyLocalConfigPath) {
    Add-Error "legacy local config file still exists"
}

$tasksText = Get-Content -LiteralPath $tasksPath -Raw -Encoding UTF8

if ($tasksText.Contains($legacyConfigName)) {
    Add-Error "VS Code tasks still reference legacy config name"
}

if (-not $tasksText.Contains("voice_generator_config.json")) {
    Add-Error "VS Code tasks do not reference voice_generator_config.json"
}

$forbiddenScriptFragments = @(
    '"voice_config"',
    '"eleven_labs"',
    'Get-JsonValue $postprocessing "only"',
    'only = $PostprocessingOnly',
    'Has-JsonProperty $config "convertToWav"',
    'else { $voiceConfig = $config }',
    'else { $elevenLabsConfig = $config }'
)

foreach ($fragment in $forbiddenScriptFragments) {
    if ($scriptText.Contains($fragment)) {
        Add-Error "generator still contains legacy config fallback: $fragment"
    }
}

$requiredScriptFragments = @(
    'Require-JsonSection $config "voiceConfig"',
    'Require-JsonSection $config "elevenLabs"',
    'Require-JsonSection $config "postprocessing"',
    'Resolve-OutNameWithCharacterPrefix',
    'onlyPostprocessing = $PostprocessingOnly',
    'voiceConfig.onlyPostprocessing requires postprocessing.enabled=true.'
)

foreach ($fragment in $requiredScriptFragments) {
    if (-not $scriptText.Contains($fragment)) {
        Add-Error "generator missing required schema behavior: $fragment"
    }
}

$exampleConfig = Get-Content -LiteralPath $exampleConfigPath -Raw -Encoding UTF8 | ConvertFrom-Json
Test-VoiceConfigSchema $exampleConfig "example config"

if (Test-Path -LiteralPath $localConfigPath) {
    $localConfig = Get-Content -LiteralPath $localConfigPath -Raw -Encoding UTF8 | ConvertFrom-Json
    Test-VoiceConfigSchema $localConfig "local config"
}

$characterConfigFiles = Get-ChildItem -LiteralPath (Join-Path $repoRoot "tools") -Filter "*_voice_generator_config.json" -File

foreach ($characterConfigFile in $characterConfigFiles) {
    $characterConfig = Get-Content -LiteralPath $characterConfigFile.FullName -Raw -Encoding UTF8 | ConvertFrom-Json
    Test-VoiceConfigSchema $characterConfig $characterConfigFile.Name
}

Test-VttSubtitleSplitter
Test-VttSubtitleSplitterCueOrderMode

if ($errors.Count -gt 0) {
    Write-Host "Voice generation tests failed:" -ForegroundColor Red

    foreach ($errorMessage in $errors) {
        Write-Host "* $errorMessage" -ForegroundColor Red
    }

    exit 1
}

Write-Host "Voice generation tests passed." -ForegroundColor Green
