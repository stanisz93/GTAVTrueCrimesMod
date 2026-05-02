# Stalker Behavior

This document describes the current `spawn_stalker` effect, its config file,
runtime behavior, and the intended extension path for event hooks such as
`onKilledByPlayer`.

## Files

- Mission usage: `missions/silence_after_midnight.json`
- Effect config: `missions/effects/spawn_stalker.json`
- Effect entry point: `src/Effects/SpawnStalkerEffectHandler.cs`
- Runtime behavior: `src/Behaviors/StalkerBehavior.cs`
- Decision model: `src/Systems/StalkerDecisionModel.cs`
- Effect config merge: `src/Systems/MissionEffectConfigLoader.cs`
- Logic tests: `tests/Program.cs`

## Mission Usage

A mission node starts the stalker through an `onEnter` effect:

```json
"onEnter": [
  {
    "type": "spawn_stalker",
    "id": "main_stalker"
  }
]
```

The node does not need to list all stalker tuning values. The runtime loads
them from `missions/effects/spawn_stalker.json`.

## Config Layout

`missions/effects/spawn_stalker.json` has one default section and optional
per-id overrides:

```json
{
  "type": "spawn_stalker",
  "default": {
    "distanceBehindPlayer": 3.0,
    "followDistance": 12.0,
    "attackEnabled": true
  },
  "configs": [
    {
      "id": "main_stalker"
    },
    {
      "id": "dock_stalker",
      "distanceBehindPlayer": 25.0,
      "attackDistance": 7.0
    }
  ]
}
```

To use a specific config, reference its `id` from the mission node:

```json
{
  "type": "spawn_stalker",
  "id": "dock_stalker"
}
```

## Merge Order

When a `spawn_stalker` effect runs, settings are merged in this order:

1. `default` from `missions/effects/spawn_stalker.json`
2. The matching entry from `configs[]` by `id`
3. Inline values placed directly on the mission effect

Inline values are still useful for quick local debugging:

```json
{
  "type": "spawn_stalker",
  "id": "main_stalker",
  "distanceBehindPlayer": 5.0
}
```

That overrides only `distanceBehindPlayer`; all other values still come from
the config file.

## Parameters

| Parameter | Meaning |
| --- | --- |
| `distanceBehindPlayer` | Spawn distance behind the player. Small values are useful for debug visibility. |
| `followDistance` | Target distance the stalker tries to keep behind the player while following. |
| `runDistance` | If farther than this, the stalker runs to the follow point. |
| `walkDistance` | If farther than this but closer than `runDistance`, the stalker walks to the follow point. |
| `tooCloseDistance` | If closer than this, the stalker moves back instead of staying beside the player. |
| `followRepathMs` | How often the stalker receives a new movement task. Too low can make AI look jittery. |
| `playerLookingDistance` | Maximum distance for detecting that the player camera is looking at the stalker. |
| `playerLookingAngle` | Camera cone angle in degrees for the "player is looking" check. |
| `pretendDurationMs` | Fallback duration. Current behavior pretends while the player keeps looking. |
| `attackEnabled` | Enables knife attack behavior when isolation conditions are met. |
| `attackDistance` | Distance where the stalker draws the knife and starts combat. |
| `isolationRadius` | Radius around the player used to count witnesses. |
| `maxWitnesses` | Maximum witness count where the player is still considered isolated. |
| `meleeDistance` | Distance where optional extra scripted knife damage can be applied. |
| `attackDamage` | Extra scripted damage per tick. `0` means use only normal GTA knife combat damage. |
| `attackDamageIntervalMs` | Interval for extra scripted damage when `attackDamage` is greater than `0`. |

## Decision Tree

The pure decision logic lives in `StalkerDecisionModel`.

```text
if stalker does not exist:
  spawn

if stalker is already attacking:
  if player is no longer isolated:
    abort attack and blend in
  else if player is dead:
    fail mission
  else if stalker is outside meleeDistance:
    continue attack approach
  else if attackDamage > 0:
    apply extra scripted damage
  else:
    continue GTA combat

if attackEnabled and player is isolated:
  stop pretending if needed
  if distance < attackDistance:
    draw knife and start attack
  else:
    approach before attack

if player camera is looking at stalker:
  pretend to be a pedestrian

if movement is waiting for next repath:
  keep current movement

if distance > runDistance:
  run to follow point
else if distance > walkDistance:
  walk to follow point
else if distance < tooCloseDistance:
  move back behind player
else:
  loiter nearby
```

Important rule: looking at an attacking stalker does not stop the attack.
Only witnesses can interrupt an active attack.

## Pretending

When the player looks at the stalker and attack conditions are not available,
the stalker enters a pretend mode. Current pretend modes include:

- calm walking
- short repositioning
- phone-call behavior with a held phone prop and ambient speech
- brief standing

The phone pretend mode is weighted more heavily than the other modes. Movement
tries to stay consistent by reusing a chosen direction instead of selecting a
new random target every few seconds.

## Witnesses And Isolation

Witnesses are nearby human NPCs around the player. The current filter ignores:

- the player
- the stalker
- dead peds
- non-human peds
- peds inside vehicles
- peds without line of sight, unless they are very close

If witness count is less than or equal to `maxWitnesses`, the player is treated
as isolated.

## Debugging

During an active mission, background behavior debug text is drawn in yellow in
the lower-left area. The stalker debug line includes:

```text
stalker[main_stalker] state=... dist=... witnesses=... isolated=... looking=... attack=...
```

Useful states include:

- `spawning`
- `spawned`
- `running`
- `walking`
- `too_close`
- `loitering`
- `pretend_start`
- `pretend_phone_pickup`
- `pretend_phone_call`
- `attack_approach`
- `attack_start`
- `attacking`
- `attack_aborted_witnesses`

F11 still shows the current mission node and active facts.

## Deployment

`tools/deploy-missions.ps1` copies JSON files under `missions`, including:

- root mission JSON files
- `missions/subtitles/*.json`
- `missions/effects/*.json`

Files ending with `.node-snippet.json` are skipped.

## Validation

`tools/validate-missions.ps1` validates:

- mission node structure
- known node and effect types
- subtitle files
- `spawn_stalker` numeric config sanity
- effect config filenames matching their `type`

Run:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\validate-missions.ps1
```

## Next Step: Event Hooks

The next planned direction is to let a stalker effect react to its own events:

```json
{
  "type": "spawn_stalker",
  "id": "main_stalker",
  "lifetime": "node",
  "onKilledByPlayer": [
    {
      "type": "phone_call",
      "caller": "Morgan",
      "audio": "stalker_killed_by_player.wav",
      "subtitlesFile": "subtitles/stalker_killed_by_player.json"
    }
  ],
  "onKilledByOther": [
    {
      "type": "phone_call",
      "caller": "Morgan",
      "audio": "stalker_killed_by_other.wav",
      "subtitlesFile": "subtitles/stalker_killed_by_other.json"
    }
  ]
}
```

This is not implemented yet. The likely implementation is:

- keep `spawn_stalker` as an effect, not a node
- add side-effect phone calls that do not complete the current node
- let `StalkerBehavior` detect death and ask `MissionRuntime` to run hook effects
- support `lifetime: "node"` so the stalker can be cleaned up when the owning node ends

