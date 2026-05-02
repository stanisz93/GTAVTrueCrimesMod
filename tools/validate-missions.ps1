$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
$missionsDir = Join-Path $repoRoot "missions"
$effectsDir = Join-Path $missionsDir "effects"
$knownEffectTypes = @(
    "spawn_stalker"
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
    }

    if ((Has-Property $config "configs") -and $null -ne $config.configs) {
        foreach ($entry in @($config.configs)) {
            if (-not (Has-Property $entry "id") -or [string]::IsNullOrWhiteSpace($entry.id)) {
                Add-Error $File.Name "effect config override missing id"
                continue
            }

            if ($config.type -eq "spawn_stalker") {
                Test-SpawnStalkerSettings $File.Name "override '$($entry.id)'" $entry
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
            if (-not (Has-Property $node "caller") -or [string]::IsNullOrWhiteSpace($node.caller)) {
                Add-Error $file.Name "phone_call '$($node.id)' missing caller"
            }

            if ((Has-Property $node "subtitles") -and $null -ne $node.subtitles) {
                Test-SubtitleCues $file.Name $node.id @($node.subtitles)
            }

            if ((Has-Property $node "subtitlesFile") -and -not [string]::IsNullOrWhiteSpace($node.subtitlesFile)) {
                $subtitlePath = Join-Path $file.DirectoryName $node.subtitlesFile

                if (-not (Test-Path -LiteralPath $subtitlePath)) {
                    Add-Error $file.Name "phone_call '$($node.id)' subtitlesFile '$($node.subtitlesFile)' does not exist"
                }
                else {
                    try {
                        $subtitleJson = Get-Content -LiteralPath $subtitlePath -Raw | ConvertFrom-Json
                        $subtitleCues = if ($subtitleJson -is [array]) { $subtitleJson } else { $subtitleJson.subtitles }

                        if ($null -eq $subtitleCues -or $subtitleCues.Count -eq 0) {
                            Add-Error $file.Name "phone_call '$($node.id)' subtitlesFile '$($node.subtitlesFile)' has no subtitles"
                        }
                        else {
                            Test-SubtitleCues $file.Name $node.id @($subtitleCues)
                        }
                    }
                    catch {
                        Add-Error $file.Name "phone_call '$($node.id)' invalid subtitlesFile '$($node.subtitlesFile)': $($_.Exception.Message)"
                    }
                }
            }
        }

        if ((Has-Property $node "onEnter") -and $null -ne $node.onEnter) {
            foreach ($effect in $node.onEnter) {
                if (-not (Has-Property $effect "type") -or [string]::IsNullOrWhiteSpace($effect.type)) {
                    Add-Error $file.Name "node '$($node.id)' onEnter effect missing type"
                    continue
                }

                if ($knownEffectTypes -notcontains $effect.type) {
                    Add-Error $file.Name "node '$($node.id)' has unknown onEnter effect '$($effect.type)'"
                }

                if ($effect.type -eq "spawn_stalker") {
                    if ((Has-Property $effect "distanceBehindPlayer") -and $effect.distanceBehindPlayer -le 0) {
                        Add-Error $file.Name "spawn_stalker in node '$($node.id)' distanceBehindPlayer must be positive"
                    }

                    if ((Has-Property $effect "followRepathMs") -and $effect.followRepathMs -lt 250) {
                        Add-Error $file.Name "spawn_stalker in node '$($node.id)' followRepathMs should be >= 250"
                    }

                    if ((Has-Property $effect "attackDamage") -and $effect.attackDamage -lt 0) {
                        Add-Error $file.Name "spawn_stalker in node '$($node.id)' attackDamage cannot be negative"
                    }
                }
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
