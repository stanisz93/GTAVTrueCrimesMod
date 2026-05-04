# Voice generation

This project can generate ElevenLabs TTS audio plus subtitle timing sidecars.

## Setup

Set your ElevenLabs API key in the current PowerShell session:

```powershell
$env:ELEVENLABS_API_KEY = "your-api-key"
```

Find a voice ID in ElevenLabs, then create a local config file:

```powershell
Copy-Item .\tools\voice_generator_config.example.json .\tools\voice_generator_config.json
```

`tools/voice_generator_config.json` is ignored by Git, so you can freely change voice IDs and scratch text.

You can also put the key in that local ignored config as `"elevenLabs": { "apiKey": "..." }`. Do not put a real key in `voice_generator_config.example.json`.

## Config

Example:

```json
{
  "voiceConfig": {
    "outName": "warning",
    "characterPrefix": "morgan",
    "text": "Cześć. Tu Morgan. | Posłuchaj mnie. | Nie idź alejką. Nie dziś wieczorem.",
    "chunkDelimiter": "|",
    "subtitleOffsetMs": 0,
    "onlyPostprocessing": false,
    "audioDir": ".\\audio",
    "subtitleDir": ".\\missions\\subtitles",
    "ffmpegPath": ""
  },
  "elevenLabs": {
    "apiKey": "",
    "voiceId": "YOUR_VOICE_ID",
    "modelId": "eleven_multilingual_v2",
    "languageCode": "pl",
    "outputFormat": "mp3_44100_192",
    "convertToWav": true,
    "voiceSettings": {
      "speed": 0.73,
      "stability": 0.70,
      "similarityBoost": 0.78,
      "style": 0.0,
      "useSpeakerBoost": true
    }
  },
  "postprocessing": {
    "enabled": false,
    "outputSuffix": "_phone",
    "telephone": true,
    "noiseProfile": "phone",
    "noise": 0.012,
    "hum": 0.0015,
    "pan": -0.25,
    "volume": 1.08
  }
}
```

Use `|` in `voiceConfig.text` to mark subtitle chunks. The script removes `|` before sending text to ElevenLabs, then maps ElevenLabs character timing back to one subtitle cue per chunk. If subtitles feel early in-game, set `voiceConfig.subtitleOffsetMs` to a positive value such as `250` or `400`.

`voiceConfig.characterPrefix` is prepended to `outName` when needed. For example, `"characterPrefix": "morgan"` and `"outName": "warning"` produce `morgan_warning.wav` and `morgan_warning.subtitles.json`. If `outName` already starts with `morgan_`, the prefix is not duplicated.

## Generate audio and subtitles

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\generate-elevenlabs-voice.ps1 `
  -ConfigFile ".\tools\voice_generator_config.json" `
  -ConvertToWav
```

Or run the VS Code task `generate ElevenLabs voice`.

The script calls ElevenLabs `text-to-speech/{voice_id}/with-timestamps`, saves the generated audio, and converts character-level alignment into sentence-level subtitle cues.

Default voice settings:

```text
model_id: eleven_multilingual_v2
language_code: pl
speed: 0.73
stability: 0.70
similarity_boost: 0.78
style: 0.0
use_speaker_boost: true
```

Outputs:

```text
audio/morgan_warning.mp3
audio/morgan_warning.wav              # only when ffmpeg is installed and -ConvertToWav is used
missions/subtitles/morgan_warning.subtitles.json
missions/subtitles/morgan_warning.node-snippet.json
```

When WAV conversion succeeds, the intermediate MP3 generated in that run is deleted automatically.

Current runtime audio playback uses `System.Media.SoundPlayer`, so mission audio should be WAV in-game. ElevenLabs Creator supports high bitrate MP3; WAV/PCM 44.1 kHz requires a higher ElevenLabs tier, so local ffmpeg conversion is the practical path.

If `ffmpeg` is not visible in PATH, set `voiceConfig.ffmpegPath` in the local ignored config, for example:

```json
"ffmpegPath": "C:\\ffmpeg\\bin\\ffmpeg.exe"
```

## Postprocessing

Audio effects are applied after ElevenLabs generation, during WAV conversion. They can be enabled per config:

```json
"postprocessing": {
  "enabled": true,
  "outputSuffix": "_phone",
  "telephone": true,
  "noiseProfile": "phone",
  "noise": 0.012,
  "hum": 0.0015,
  "pan": -0.25,
  "volume": 1.08
}
```

`telephone` narrows the voice to a phone-like band, `noiseProfile: "phone"` uses layered band-limited hiss/line noise, `hum` adds subtle 60 Hz line hum, `pan` moves the voice left/right from `-1.0` to `1.0`, and `volume` adjusts loudness. Use `noiseProfile: "simple"` if you only want flat white noise.

To tweak the effect without calling ElevenLabs again, set `voiceConfig.onlyPostprocessing` to `true`. The script will look for `audio/{outName}.mp3` or `audio/{outName}.wav`, create a variant such as `audio/{outName}_phone.wav`, and write a small node snippet for that variant.

## Mission JSON

Use the generated sidecar instead of inline subtitles:

```json
{
  "id": "warning_call",
  "type": "phone_call",
  "caller": "Nieznany numer",
  "audio": "morgan_warning.wav",
  "subtitlesFile": "subtitles/morgan_warning.subtitles.json",
  "next": "go_to_scene"
}
```

`subtitlesFile` is resolved relative to the mission JSON file. Runtime audio is
loaded from `scripts/DetectiveAudio`, and `tools/deploy-missions.ps1` copies WAV
files from local `audio/` into that game folder.

For a call made from an effect hook, or any call that should chain multiple
audio files, use `audioSegments`:

```json
{
  "type": "phone_call",
  "caller": "Morgan",
  "audioSegments": [
    {
      "audio": "first_warning.wav",
      "subtitlesFile": "subtitles/first_warning.subtitles.json",
      "gapAfterMs": 250
    },
    {
      "audio": "second_warning.wav",
      "subtitlesFile": "subtitles/second_warning.subtitles.json"
    }
  ]
}
```

The runtime starts the phone hold animation once, plays each segment in order,
then hangs up after the final segment.

For two-person overheard conversations, use a folder and numbered output names:

```json
{
  "type": "spawn_police_ambush",
  "conversationFolder": "police_station_ambush",
  "conversationFirstSpeaker": "shouter",
  "conversationGapAfterMs": 400
}
```

The loader reads `audio/police_station_ambush/*.wav` by the first number in the
file name, alternates speakers between `shouter` and `shooter`, and looks for
matching subtitle files under `missions/subtitles/police_station_ambush/`. Use
names like `01_intro.wav`, `02_reply.wav`, and matching
`01_intro.subtitles.json`.

## Split Studio VTT subtitles

If ElevenLabs Studio exports one `.vtt` file for a conversation that is split
into multiple WAV files, put that `.vtt` in the same folder as the WAV files and
convert it into one runtime subtitle JSON per WAV:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\convert-vtt-subtitles.ps1 `
  -AudioFolder ".\audio\police_station_ambush" `
  -OutputFolder ".\missions\subtitles\police_station_ambush"
```

The splitter expects multiple WAV files and exactly one VTT file in the audio
folder. If there are zero or several VTT files, it stops with an error so the
wrong captions do not get matched silently.

The splitter orders WAV files by the first number in the file name. In `Auto`
mode, if the number of VTT cues matches the number of WAV files, it maps cues
to WAVs one by one in that order. This is the preferred mode for Studio exports
where each WAV is one spoken line. If the counts differ, it falls back to
duration-based splitting and assigns each VTT cue to the audio file whose time
range contains the cue midpoint. It writes files like:

```text
missions/subtitles/police_station_ambush/01_intro.subtitles.json
missions/subtitles/police_station_ambush/02_reply.subtitles.json
```

Keep the first number in each WAV name aligned with the Studio conversation
order, for example `officer_01_intro.wav`, `officer_02_reply.wav`,
`officer_10_later.wav`.
