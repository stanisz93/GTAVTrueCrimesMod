$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
$missionsDir = Join-Path $repoRoot "missions"
$effectsDir = Join-Path $missionsDir "effects"
$knownEffectTypes = @(
    "spawn_stalker",
    "phone_call",
    "set_fact",
    "scripted_stalker_shot"
)
$knownNodeTypes = @(
    "objective",
    "clue",
    "dialog",
    "ending",
    "phone_call"
)
$errors = New-Object System.Collections.Generic.List[string]

function Add-Error {
    param(
        [string]$File,
        [string]$Message
    )

    $errors.Add("$File`: $Message")
}

function Has-Property {
    param(
        [object]$Object,
        [string]$Name
    )

    return $null -ne $Object.PSObject.Properties[$Name]
}

function Test-SpeakableText {
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

function Test-SubtitleCues {
    param(
        [string]$FileName,
        [string]$NodeId,
        [object[]]$Cues
    )

    $lastAt = -1

    foreach ($cue in $Cues) {
        if (-not (Has-Property $cue "atMs")) {
            Add-Error $FileName "phone_call '$NodeId' subtitle missing atMs"
            continue
        }

        if ($cue.atMs -lt $lastAt) {
            Add-Error $FileName "phone_call '$NodeId' subtitles are not sorted by atMs"
        }

        $lastAt = $cue.atMs

        if ((Has-Property $cue "endMs") -and $cue.endMs -le $cue.atMs) {
            Add-Error $FileName "phone_call '$NodeId' subtitle endMs must be greater than atMs"
        }

        if ((Has-Property $cue "durationMs") -and $cue.durationMs -le 0) {
            Add-Error $FileName "phone_call '$NodeId' subtitle durationMs must be positive"
        }

        if (-not (Has-Property $cue "endMs") -and -not (Has-Property $cue "durationMs")) {
            Add-Error $FileName "phone_call '$NodeId' subtitle requires endMs or durationMs"
        }

        if (-not (Has-Property $cue "text") -or [string]::IsNullOrWhiteSpace($cue.text)) {
            Add-Error $FileName "phone_call '$NodeId' subtitle missing text"
        }
        elseif (-not (Test-SpeakableText $cue.text)) {
            Add-Error $FileName "phone_call '$NodeId' subtitle '$($cue.text)' has no speakable text"
        }
    }
}

function Test-NonNegativeNumber {
    param(
        [string]$FileName,
        [string]$Scope,
        [object]$Object,
        [string]$Name
    )

    if ((Has-Property $Object $Name) -and $Object.$Name -lt 0) {
        Add-Error $FileName "$Scope $Name cannot be negative"
    }
}

function Test-PositiveNumber {
    param(
        [string]$FileName,
        [string]$Scope,
        [object]$Object,
        [string]$Name
    )

    if ((Has-Property $Object $Name) -and $Object.$Name -le 0) {
        Add-Error $FileName "$Scope $Name must be positive"
    }
}

function Test-MinNumber {
    param(
        [string]$FileName,
        [string]$Scope,
        [object]$Object,
        [string]$Name,
        [double]$Minimum
    )

    if ((Has-Property $Object $Name) -and $Object.$Name -lt $Minimum) {
        Add-Error $FileName "$Scope $Name should be >= $Minimum"
    }
}

function Test-SpawnStalkerSettings {
    param(
        [string]$FileName,
        [string]$Scope,
        [object]$Settings
    )

    $positiveFields = @(
        "distanceBehindPlayer",
        "followDistance",
        "runDistance",
        "walkDistance",
        "tooCloseDistance",
        "playerLookingDistance",
        "playerLookingAngle",
        "pretendDurationMs",
        "attackDistance",
        "isolationRadius",
        "meleeDistance",
        "attackDamageIntervalMs"
    )

    foreach ($field in $positiveFields) {
        Test-PositiveNumber $FileName $Scope $Settings $field
    }

    Test-MinNumber $FileName $Scope $Settings "followRepathMs" 250
    Test-MinNumber $FileName $Scope $Settings "pretendDurationMs" 500
    Test-MinNumber $FileName $Scope $Settings "attackDamageIntervalMs" 100
    Test-NonNegativeNumber $FileName $Scope $Settings "maxWitnesses"
    Test-NonNegativeNumber $FileName $Scope $Settings "attackDamage"
    Test-NonNegativeNumber $FileName $Scope $Settings "playerDamageMemoryMs"
}

function Test-ScriptedStalkerShotSettings {
    param(
        [string]$FileName,
        [string]$Scope,
        [object]$Settings
    )

    if ((Has-Property $Settings "targetBehaviorId") -and [string]::IsNullOrWhiteSpace($Settings.targetBehaviorId)) {
        Add-Error $FileName "$Scope targetBehaviorId cannot be empty"
    }

    Test-PositiveNumber $FileName $Scope $Settings "triggerDistance"
    Test-PositiveNumber $FileName $Scope $Settings "targetMaxDistanceFromNodeTarget"
    Test-PositiveNumber $FileName $Scope $Settings "shotCount"
    Test-PositiveNumber $FileName $Scope $Settings "damage"
    Test-NonNegativeNumber $FileName $Scope $Settings "delayMs"
    Test-NonNegativeNumber $FileName $Scope $Settings "shotGapMs"
}

function Test-PhoneCallFields {
    param(
        [string]$FileName,
        [string]$Scope,
        [object]$Call,
        [string]$BaseDir
    )

    if (-not (Has-Property $Call "caller") -or [string]::IsNullOrWhiteSpace($Call.caller)) {
        Add-Error $FileName "$Scope phone_call missing caller"
    }

    if ((Has-Property $Call "subtitles") -and $null -ne $Call.subtitles) {
        Test-SubtitleCues $FileName $Scope @($Call.subtitles)
    }

    if ((Has-Property $Call "subtitlesFile") -and -not [string]::IsNullOrWhiteSpace($Call.subtitlesFile)) {
        $subtitlePath = Join-Path $BaseDir $Call.subtitlesFile

        if (-not (Test-Path -LiteralPath $subtitlePath)) {
            Add-Error $FileName "$Scope subtitlesFile '$($Call.subtitlesFile)' does not exist"
        }
        else {
            try {
                $subtitleJson = Get-Content -LiteralPath $subtitlePath -Raw | ConvertFrom-Json
                $subtitleCues = if ($subtitleJson -is [array]) { $subtitleJson } else { $subtitleJson.subtitles }

                if ($null -eq $subtitleCues -or $subtitleCues.Count -eq 0) {
                    Add-Error $FileName "$Scope subtitlesFile '$($Call.subtitlesFile)' has no subtitles"
                }
                else {
                    Test-SubtitleCues $FileName $Scope @($subtitleCues)
                }
            }
            catch {
                Add-Error $FileName "$Scope invalid subtitlesFile '$($Call.subtitlesFile)': $($_.Exception.Message)"
            }
        }
    }

    if ((Has-Property $Call "audioSegments") -and $null -ne $Call.audioSegments) {
        $index = 0

        foreach ($segment in @($Call.audioSegments)) {
            Test-AudioSegment $FileName "$Scope audioSegments[$index]" $segment $BaseDir
            $index++
        }
    }
}

function Test-AudioSegment {
    param(
        [string]$FileName,
        [string]$Scope,
        [object]$Segment,
        [string]$BaseDir
    )

    $hasAudio = (Has-Property $Segment "audio") -and -not [string]::IsNullOrWhiteSpace($Segment.audio)
    $hasText = (Has-Property $Segment "text") -and -not [string]::IsNullOrWhiteSpace($Segment.text)
    $hasSubtitles = (Has-Property $Segment "subtitles") -and $null -ne $Segment.subtitles
    $hasSubtitlesFile = (Has-Property $Segment "subtitlesFile") -and -not [string]::IsNullOrWhiteSpace($Segment.subtitlesFile)
    $hasDuration = (Has-Property $Segment "completeAfterMs") -and $Segment.completeAfterMs -gt 0

    if (-not $hasAudio -and -not $hasText -and -not $hasSubtitles -and -not $hasSubtitlesFile -and -not $hasDuration) {
        Add-Error $FileName "$Scope has no audio, text, subtitles, subtitlesFile, or completeAfterMs"
    }

    if ((Has-Property $Segment "completeAfterMs") -and $Segment.completeAfterMs -le 0) {
        Add-Error $FileName "$Scope completeAfterMs must be positive"
    }

    if ((Has-Property $Segment "gapAfterMs") -and $Segment.gapAfterMs -lt 0) {
        Add-Error $FileName "$Scope gapAfterMs cannot be negative"
    }

    if ($hasSubtitles) {
        Test-SubtitleCues $FileName $Scope @($Segment.subtitles)
    }

    if ($hasSubtitlesFile) {
        $subtitlePath = Join-Path $BaseDir $Segment.subtitlesFile

        if (-not (Test-Path -LiteralPath $subtitlePath)) {
            Add-Error $FileName "$Scope subtitlesFile '$($Segment.subtitlesFile)' does not exist"
        }
        else {
            try {
                $subtitleJson = Get-Content -LiteralPath $subtitlePath -Raw | ConvertFrom-Json
                $subtitleCues = if ($subtitleJson -is [array]) { $subtitleJson } else { $subtitleJson.subtitles }

                if ($null -eq $subtitleCues -or $subtitleCues.Count -eq 0) {
                    Add-Error $FileName "$Scope subtitlesFile '$($Segment.subtitlesFile)' has no subtitles"
                }
                else {
                    Test-SubtitleCues $FileName $Scope @($subtitleCues)
                }
            }
            catch {
                Add-Error $FileName "$Scope invalid subtitlesFile '$($Segment.subtitlesFile)': $($_.Exception.Message)"
            }
        }
    }
}

function Test-EffectObject {
    param(
        [string]$FileName,
        [string]$Scope,
        [object]$Effect,
        [string]$BaseDir,
        [bool]$RequireType = $true
    )

    if (-not (Has-Property $Effect "type") -or [string]::IsNullOrWhiteSpace($Effect.type)) {
        if ($RequireType) {
            Add-Error $FileName "$Scope effect missing type"
        }

        $hookNamesWithoutType = @("onKilledByPlayer", "onKilledByOther")

        foreach ($hookName in $hookNamesWithoutType) {
            if (-not (Has-Property $Effect $hookName) -or $null -eq $Effect.$hookName) {
                continue
            }

            foreach ($hookEffect in @($Effect.$hookName)) {
                Test-EffectObject $FileName "$Scope $hookName" $hookEffect $BaseDir $true
            }
        }

        return
    }

    if ($knownEffectTypes -notcontains $Effect.type) {
        Add-Error $FileName "$Scope has unknown effect '$($Effect.type)'"
    }

    if ($Effect.type -eq "spawn_stalker") {
        Test-SpawnStalkerSettings $FileName $Scope $Effect
    }
    elseif ($Effect.type -eq "phone_call") {
        Test-PhoneCallFields $FileName $Scope $Effect $BaseDir
    }
    elseif ($Effect.type -eq "set_fact") {
        if (-not (Has-Property $Effect "fact") -or [string]::IsNullOrWhiteSpace($Effect.fact)) {
            Add-Error $FileName "$Scope set_fact missing fact"
        }
    }
    elseif ($Effect.type -eq "scripted_stalker_shot") {
        Test-ScriptedStalkerShotSettings $FileName $Scope $Effect
    }

    $hookNames = @("onKilledByPlayer", "onKilledByOther")

    foreach ($hookName in $hookNames) {
        if (-not (Has-Property $Effect $hookName) -or $null -eq $Effect.$hookName) {
            continue
        }

        foreach ($hookEffect in @($Effect.$hookName)) {
            Test-EffectObject $FileName "$Scope $hookName" $hookEffect $BaseDir $true
        }
    }
}

function Test-EffectConfigFile {
    param([System.IO.FileInfo]$File)

    $config = $null

    try {
        $config = Get-Content -LiteralPath $File.FullName -Raw | ConvertFrom-Json
    }
    catch {
        Add-Error $File.Name "invalid effect config JSON: $($_.Exception.Message)"
        return
    }

    if (-not (Has-Property $config "type") -or [string]::IsNullOrWhiteSpace($config.type)) {
        Add-Error $File.Name "effect config missing type"
        return
    }

    if ($knownEffectTypes -notcontains $config.type) {
        Add-Error $File.Name "effect config has unknown type '$($config.type)'"
    }

    if ($File.BaseName -ne $config.type) {
        Add-Error $File.Name "effect config filename should match type '$($config.type)'"
    }

    if (-not (Has-Property $config "default") -or $null -eq $config.default) {
        Add-Error $File.Name "effect config missing default settings"
    }
    elseif ($config.type -eq "spawn_stalker") {
        Test-SpawnStalkerSettings $File.Name "default" $config.default
        Test-EffectObject $File.Name "default" $config.default $missionsDir $false
    }

    if ((Has-Property $config "configs") -and $null -ne $config.configs) {
        foreach ($entry in @($config.configs)) {
            if (-not (Has-Property $entry "id") -or [string]::IsNullOrWhiteSpace($entry.id)) {
                Add-Error $File.Name "effect config override missing id"
                continue
            }

            if ($config.type -eq "spawn_stalker") {
                Test-SpawnStalkerSettings $File.Name "override '$($entry.id)'" $entry
                Test-EffectObject $File.Name "override '$($entry.id)'" $entry $missionsDir $false
            }
        }
    }
}

if (-not (Test-Path -LiteralPath $missionsDir)) {
    throw "Missions folder not found: $missionsDir"
}

$files = Get-ChildItem -LiteralPath $missionsDir -Filter "*.json" -File

if ($files.Count -eq 0) {
    throw "No mission JSON files found in $missionsDir"
}

foreach ($file in $files) {
    $mission = $null

    try {
        $mission = Get-Content -LiteralPath $file.FullName -Raw | ConvertFrom-Json
    }
    catch {
        Add-Error $file.Name "invalid JSON: $($_.Exception.Message)"
        continue
    }

    if (-not (Has-Property $mission "id") -or [string]::IsNullOrWhiteSpace($mission.id)) {
        Add-Error $file.Name "missing mission id"
    }

    if (-not (Has-Property $mission "nodes") -or $null -eq $mission.nodes -or $mission.nodes.Count -eq 0) {
        Add-Error $file.Name "missing nodes array"
        continue
    }

    $nodeIds = @{}

    foreach ($node in $mission.nodes) {
        if (-not (Has-Property $node "id") -or [string]::IsNullOrWhiteSpace($node.id)) {
            Add-Error $file.Name "node is missing id"
            continue
        }

        if ($nodeIds.ContainsKey($node.id)) {
            Add-Error $file.Name "duplicate node id '$($node.id)'"
        }
        else {
            $nodeIds[$node.id] = $true
        }
    }

    if ((Has-Property $mission "firstNode") -and -not [string]::IsNullOrWhiteSpace($mission.firstNode)) {
        if (-not $nodeIds.ContainsKey($mission.firstNode)) {
            Add-Error $file.Name "firstNode '$($mission.firstNode)' does not exist"
        }
    }

    if ((Has-Property $mission "debugStartNode") -and -not [string]::IsNullOrWhiteSpace($mission.debugStartNode)) {
        if (-not $nodeIds.ContainsKey($mission.debugStartNode)) {
            Add-Error $file.Name "debugStartNode '$($mission.debugStartNode)' does not exist"
        }
    }

    foreach ($node in $mission.nodes) {
        if (-not (Has-Property $node "id") -or [string]::IsNullOrWhiteSpace($node.id)) {
            continue
        }

        if (-not (Has-Property $node "type") -or [string]::IsNullOrWhiteSpace($node.type)) {
            Add-Error $file.Name "node '$($node.id)' missing type"
        }
        elseif ($knownNodeTypes -notcontains $node.type) {
            Add-Error $file.Name "node '$($node.id)' has unknown type '$($node.type)'"
        }

        if ((Has-Property $node "next") -and -not [string]::IsNullOrWhiteSpace($node.next)) {
            if (-not $nodeIds.ContainsKey($node.next)) {
                Add-Error $file.Name "node '$($node.id)' next '$($node.next)' does not exist"
            }
        }

        if ((Has-Property $node "completeWhen") -and $node.completeWhen -eq "playerNearTarget") {
            if (-not (Has-Property $node "target") -or $null -eq $node.target) {
                Add-Error $file.Name "node '$($node.id)' completeWhen=playerNearTarget requires target"
            }
        }

        if ($node.type -eq "phone_call") {
            Test-PhoneCallFields $file.Name "phone_call '$($node.id)'" $node $file.DirectoryName
        }

        if ((Has-Property $node "onEnter") -and $null -ne $node.onEnter) {
            foreach ($effect in $node.onEnter) {
                Test-EffectObject $file.Name "node '$($node.id)' onEnter" $effect $file.DirectoryName $true
            }
        }
    }
}

if (Test-Path -LiteralPath $effectsDir) {
    $effectConfigFiles = Get-ChildItem -LiteralPath $effectsDir -Filter "*.json" -File

    foreach ($file in $effectConfigFiles) {
        Test-EffectConfigFile $file
    }
}

if ($errors.Count -gt 0) {
    Write-Host "Mission validation failed:" -ForegroundColor Red

    foreach ($errorMessage in $errors) {
        Write-Host "* $errorMessage" -ForegroundColor Red
    }

    exit 1
}

Write-Host "Mission validation passed for $($files.Count) file(s)." -ForegroundColor Green
