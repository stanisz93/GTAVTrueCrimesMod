$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
$generatorPath = Join-Path $repoRoot "tools\generate-elevenlabs-voice.ps1"
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

$tokens = $null
$parseErrors = $null
[System.Management.Automation.Language.Parser]::ParseFile($generatorPath, [ref]$tokens, [ref]$parseErrors) | Out-Null

if ($parseErrors.Count -gt 0) {
    foreach ($parseError in $parseErrors) {
        Add-Error "generator syntax: $($parseError.Message)"
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

if ($errors.Count -gt 0) {
    Write-Host "Voice generation tests failed:" -ForegroundColor Red

    foreach ($errorMessage in $errors) {
        Write-Host "* $errorMessage" -ForegroundColor Red
    }

    exit 1
}

Write-Host "Voice generation tests passed." -ForegroundColor Green
