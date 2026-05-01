$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
$missionsDir = Join-Path $repoRoot "missions"
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
                $lastAt = -1

                foreach ($cue in $node.subtitles) {
                    if (-not (Has-Property $cue "atMs")) {
                        Add-Error $file.Name "phone_call '$($node.id)' subtitle missing atMs"
                        continue
                    }

                    if ($cue.atMs -lt $lastAt) {
                        Add-Error $file.Name "phone_call '$($node.id)' subtitles are not sorted by atMs"
                    }

                    $lastAt = $cue.atMs

                    if ((Has-Property $cue "endMs") -and $cue.endMs -le $cue.atMs) {
                        Add-Error $file.Name "phone_call '$($node.id)' subtitle endMs must be greater than atMs"
                    }

                    if ((Has-Property $cue "durationMs") -and $cue.durationMs -le 0) {
                        Add-Error $file.Name "phone_call '$($node.id)' subtitle durationMs must be positive"
                    }

                    if (-not (Has-Property $cue "endMs") -and -not (Has-Property $cue "durationMs")) {
                        Add-Error $file.Name "phone_call '$($node.id)' subtitle requires endMs or durationMs"
                    }

                    if (-not (Has-Property $cue "text") -or [string]::IsNullOrWhiteSpace($cue.text)) {
                        Add-Error $file.Name "phone_call '$($node.id)' subtitle missing text"
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

if ($errors.Count -gt 0) {
    Write-Host "Mission validation failed:" -ForegroundColor Red

    foreach ($errorMessage in $errors) {
        Write-Host "* $errorMessage" -ForegroundColor Red
    }

    exit 1
}

Write-Host "Mission validation passed for $($files.Count) file(s)." -ForegroundColor Green
