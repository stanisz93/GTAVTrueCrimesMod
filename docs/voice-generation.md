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
    "outName": "morgan_warning",
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

`subtitlesFile` is resolved relative to the mission JSON file. The mission deploy script copies JSON sidecars from `missions/` recursively.
