# Stalker Behavior

This document describes the current `spawn_stalker` effect, its behavior config,
runtime behavior, and event hooks such as `onKilledByPlayer`.

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
    "id": "main_stalker",
    "lifetime": "node"
  }
]
```

The node does not need to list all stalker tuning values. The runtime loads
them from `missions/effects/spawn_stalker.json`.

Story-specific consequences, such as facts and phone calls after the stalker
dies, should live in the mission node. The shared `spawn_stalker` config should
stay focused on reusable movement, detection, and combat settings.

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
- local `audio/*.wav` files into `scripts/DetectiveAudio`

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

## Event Hooks

The stalker effect can react to its own events through nested effect arrays.
Current supported death hooks are:

- `onKilledByPlayer`
- `onKilledByOther`

These hooks should live inline in the mission node, because they are
story-specific reactions. Keep `missions/effects/spawn_stalker.json` focused on
reusable stalker behavior tuning.

```json
{
  "type": "spawn_stalker",
  "id": "main_stalker",
  "lifetime": "node",
  "onKilledByPlayer": [
    {
      "type": "set_fact",
      "fact": "stalker_killed_by_player"
    },
    {
      "type": "phone_call",
      "caller": "Morgan",
      "audioSegments": [
        {
          "audio": "player_killed_stalker_first.wav",
          "subtitlesFile": "subtitles/player_killed_stalker_first.subtitles.json"
        }
      ]
    }
  ],
  "onKilledByOther": [
    {
      "type": "set_fact",
      "fact": "stalker_killed_by_other"
    },
    {
      "type": "phone_call",
      "caller": "Morgan",
      "text": "Ktos zdjal tego, ktory cie sledzil. Nie zakladaj, ze to dobra wiadomosc.",
      "completeAfterMs": 7000
    }
  ]
}
```

Hook effects are normal effects. Current supported hook effects are:

- `set_fact`
- `phone_call`

Important: hook phone calls are side-effect calls. They ring, can be answered,
play subtitles/audio, and hang up, but they do not complete the current mission
node.

Set `"completeCurrentNode": true` on a hook `phone_call` when the current node
should advance after that call finishes. This is useful for scenes where the
stalker death, not arrival at the marker, is the real node completion beat.

Phone calls may use either the legacy single `audio`/`subtitlesFile` fields or
`audioSegments`. Segments run sequentially under one call, so the player keeps
holding the phone until the last segment finishes.

Killed-by-player detection uses two signals:

- `Ped.Killer` when GTA records the killer entity
- recent player damage memory via `playerDamageMemoryMs`

This means a stalker can still count as killed by the player if the player
damaged him shortly before the final GTA physics/combat event.

## Lifetime

`lifetime` controls how long the spawned stalker background behavior exists.

- `mission` keeps it until mission end/failure/retry.
- `node` removes it when the owning node ends or changes.

The current mission uses node-scoped lifetime in `silence_after_midnight.json`:

```json
"lifetime": "node"
```

## Scripted Rescue Shot

`scripted_stalker_shot` is a node-scoped background effect that waits until the
player is near the current node target, then starts a scripted gunshot kill on a
target stalker behavior.

```json
{
  "type": "scripted_stalker_shot",
  "id": "scene_arrival_stalker_shot",
  "targetBehaviorId": "main_stalker",
  "triggerDistance": 8.0,
  "requireTargetNearNodeTarget": true,
  "targetMaxDistanceFromNodeTarget": 12.0,
  "delayMs": 700,
  "shotCount": 2,
  "shotGapMs": 250,
  "damage": 500
}
```

With `requireTargetNearNodeTarget`, the shot waits until both the player and the
stalker are close enough to the current node target. The shot marks the death as
`onKilledByOther`, so the stalker's normal death hooks decide what happens next.
