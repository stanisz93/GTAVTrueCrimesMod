using GTAVTrueCrimesMod.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace GTAVTrueCrimesMod.Systems
{
    public class MissionEffectConfigLoader
    {
        private readonly string missionsFolder;
        private readonly string configFolder;
        private readonly Dictionary<string, EffectTypeConfig> cache = new Dictionary<string, EffectTypeConfig>();

        public MissionEffectConfigLoader(string missionsFolder)
        {
            this.missionsFolder = missionsFolder;

            if (string.IsNullOrEmpty(missionsFolder))
                configFolder = "";
            else
                configFolder = Path.Combine(missionsFolder, "effects");
        }

        public MissionEffect Resolve(MissionEffect effect)
        {
            if (effect == null)
                return null;

            MissionEffect resolved = new MissionEffect();
            resolved.type = effect.type;
            resolved.id = effect.id;
            resolved.args = new Dictionary<string, string>();

            EffectTypeConfig config = LoadConfig(effect.type);

            if (config != null)
            {
                MergeEffect(resolved, config.defaultEffect);

                MissionEffect idEffect = config.GetEffectForId(effect.id);

                if (idEffect != null)
                    MergeEffect(resolved, idEffect);
            }

            MergeEffect(resolved, effect);

            resolved.type = GetArg(resolved.args, "type", resolved.type);
            resolved.id = GetArg(resolved.args, "id", resolved.id);

            if (!string.IsNullOrEmpty(resolved.type))
                resolved.args["type"] = resolved.type;

            if (!string.IsNullOrEmpty(resolved.id))
                resolved.args["id"] = resolved.id;

            return resolved;
        }

        private EffectTypeConfig LoadConfig(string effectType)
        {
            if (string.IsNullOrEmpty(effectType) || string.IsNullOrEmpty(configFolder))
                return null;

            if (cache.ContainsKey(effectType))
                return cache[effectType];

            string path = Path.Combine(configFolder, effectType + ".json");
            EffectTypeConfig config = null;

            if (File.Exists(path))
                config = ReadConfigFile(path);

            cache[effectType] = config;
            return config;
        }

        private EffectTypeConfig ReadConfigFile(string path)
        {
            string json = ReadAllTextShared(path);
            EffectTypeConfig config = new EffectTypeConfig();

            config.type = ReadJsonString(json, "type");

            if (string.IsNullOrEmpty(config.type))
                config.type = Path.GetFileNameWithoutExtension(path);

            string defaultJson = ReadJsonObject(json, "default");
            config.defaultEffect = ReadMissionEffect(defaultJson);
            config.defaultEffect.type = config.type;

            if (!config.defaultEffect.args.ContainsKey("type"))
                config.defaultEffect.args["type"] = config.type;

            string configsJson = ReadJsonArray(json, "configs");
            List<string> objects = SplitJsonObjects(configsJson);

            for (int i = 0; i < objects.Count; i++)
            {
                MissionEffect configEffect = ReadMissionEffect(objects[i]);
                string id = configEffect.id;

                if (string.IsNullOrEmpty(id))
                    continue;

                if (!configEffect.args.ContainsKey("type"))
                    configEffect.args["type"] = config.type;

                configEffect.type = config.type;
                config.configsById[id] = configEffect;
            }

            return config;
        }

        private MissionEffect ReadMissionEffect(string json)
        {
            MissionEffect effect = new MissionEffect();

            effect.args = ReadFlatJsonValues(json);
            effect.type = GetArg(effect.args, "type", "");
            effect.id = GetArg(effect.args, "id", "");
            effect.subtitles = ReadSubtitleCues(json, "subtitles");
            effect.audioSegments = ReadAudioSegments(json);

            string subtitlesFile = effect.GetString("subtitlesFile", "");

            if (!string.IsNullOrEmpty(subtitlesFile))
                effect.subtitles = ReadSubtitleCuesFile(subtitlesFile);

            effect.onKilledByPlayer = ReadMissionEffects(json, "onKilledByPlayer");
            effect.onKilledByOther = ReadMissionEffects(json, "onKilledByOther");

            return effect;
        }

        private MissionEffect[] ReadMissionEffects(string json, string key)
        {
            string effectsJson = ReadJsonArray(json, key);

            if (string.IsNullOrEmpty(effectsJson))
                return new MissionEffect[0];

            List<string> objects = SplitJsonObjects(effectsJson);
            List<MissionEffect> effects = new List<MissionEffect>();

            for (int i = 0; i < objects.Count; i++)
            {
                MissionEffect effect = ReadMissionEffect(objects[i]);

                if (!string.IsNullOrEmpty(effect.type))
                    effects.Add(effect);
            }

            return effects.ToArray();
        }

        private void MergeEffect(MissionEffect target, MissionEffect source)
        {
            if (target == null || source == null)
                return;

            MergeArgs(target.args, source.args);

            if (!string.IsNullOrEmpty(source.type))
                target.type = source.type;

            if (!string.IsNullOrEmpty(source.id))
                target.id = source.id;

            if (source.subtitles != null && source.subtitles.Length > 0)
                target.subtitles = source.subtitles;

            if (source.audioSegments != null && source.audioSegments.Length > 0)
                target.audioSegments = source.audioSegments;

            if (source.onKilledByPlayer != null && source.onKilledByPlayer.Length > 0)
                target.onKilledByPlayer = source.onKilledByPlayer;

            if (source.onKilledByOther != null && source.onKilledByOther.Length > 0)
                target.onKilledByOther = source.onKilledByOther;
        }

        private void MergeArgs(Dictionary<string, string> target, Dictionary<string, string> source)
        {
            if (target == null || source == null)
                return;

            foreach (KeyValuePair<string, string> pair in source)
                target[pair.Key] = pair.Value;
        }

        private static string GetArg(Dictionary<string, string> args, string key, string fallback)
        {
            if (args == null || !args.ContainsKey(key))
                return fallback;

            return args[key];
        }

        private MissionSubtitleCue[] ReadSubtitleCues(string json, string key)
        {
            try
            {
                string cuesJson = ReadJsonArray(json, key);

                if (string.IsNullOrEmpty(cuesJson))
                    return new MissionSubtitleCue[0];

                return ReadSubtitleCueArray(cuesJson);
            }
            catch
            {
                return new MissionSubtitleCue[0];
            }
        }

        private MissionAudioSegment[] ReadAudioSegments(string json)
        {
            try
            {
                string segmentsJson = ReadJsonArray(json, "audioSegments");

                if (string.IsNullOrEmpty(segmentsJson))
                    return new MissionAudioSegment[0];

                List<string> segmentObjects = SplitJsonObjects(segmentsJson);
                List<MissionAudioSegment> segments = new List<MissionAudioSegment>();

                for (int i = 0; i < segmentObjects.Count; i++)
                {
                    string segmentJson = segmentObjects[i];
                    MissionAudioSegment segment = new MissionAudioSegment();

                    segment.audio = ReadJsonString(segmentJson, "audio");
                    segment.text = ReadJsonString(segmentJson, "text");
                    segment.subtitlesFile = ReadJsonString(segmentJson, "subtitlesFile");
                    segment.subtitles = ReadSubtitleCues(segmentJson, "subtitles");
                    segment.completeAfterMs = ReadJsonInt(segmentJson, "completeAfterMs", 0);
                    segment.gapAfterMs = ReadJsonInt(segmentJson, "gapAfterMs", 0);

                    if (!string.IsNullOrEmpty(segment.subtitlesFile))
                        segment.subtitles = ReadSubtitleCuesFile(segment.subtitlesFile);

                    if (!string.IsNullOrEmpty(segment.audio) ||
                        !string.IsNullOrEmpty(segment.text) ||
                        (segment.subtitles != null && segment.subtitles.Length > 0) ||
                        segment.completeAfterMs > 0)
                    {
                        segments.Add(segment);
                    }
                }

                return segments.ToArray();
            }
            catch
            {
                return new MissionAudioSegment[0];
            }
        }

        private MissionSubtitleCue[] ReadSubtitleCuesFile(string subtitlesFile)
        {
            try
            {
                string path = subtitlesFile;

                if (!Path.IsPathRooted(path))
                    path = Path.Combine(missionsFolder, subtitlesFile);

                if (!File.Exists(path))
                    return new MissionSubtitleCue[0];

                string json = ReadAllTextShared(path).Trim();
                string cuesJson = json;

                if (json.StartsWith("["))
                {
                    int end = FindMatching(json, 0, '[', ']');

                    if (end >= 0)
                        cuesJson = json.Substring(1, end - 1);
                }
                else
                {
                    cuesJson = ReadJsonArray(json, "subtitles");
                }

                return ReadSubtitleCueArray(cuesJson);
            }
            catch
            {
                return new MissionSubtitleCue[0];
            }
        }

        private MissionSubtitleCue[] ReadSubtitleCueArray(string cuesJson)
        {
            if (string.IsNullOrEmpty(cuesJson))
                return new MissionSubtitleCue[0];

            List<string> cueObjects = SplitJsonObjects(cuesJson);
            List<MissionSubtitleCue> cues = new List<MissionSubtitleCue>();

            for (int i = 0; i < cueObjects.Count; i++)
            {
                string cueJson = cueObjects[i];
                MissionSubtitleCue cue = new MissionSubtitleCue();

                cue.atMs = ReadJsonInt(cueJson, "atMs", 0);
                int endMs = ReadJsonInt(cueJson, "endMs", 0);

                if (endMs > cue.atMs)
                    cue.durationMs = endMs - cue.atMs;
                else
                    cue.durationMs = ReadJsonInt(cueJson, "durationMs", 2500);

                cue.text = ReadJsonString(cueJson, "text");

                if (!string.IsNullOrEmpty(cue.text))
                    cues.Add(cue);
            }

            return cues.ToArray();
        }

        private string ReadAllTextShared(string filePath)
        {
            using (FileStream stream = new FileStream(
                filePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete))
            using (StreamReader reader = new StreamReader(stream, Encoding.UTF8))
            {
                return reader.ReadToEnd();
            }
        }

        private string ReadJsonString(string json, string key)
        {
            try
            {
                string pattern = "\"" + Regex.Escape(key) + "\"\\s*:\\s*\"([^\"]*)\"";
                Match match = Regex.Match(json, pattern);

                if (!match.Success)
                    return "";

                return match.Groups[1].Value;
            }
            catch
            {
                return "";
            }
        }

        private Dictionary<string, string> ReadFlatJsonValues(string json)
        {
            Dictionary<string, string> values = new Dictionary<string, string>();

            try
            {
                string body = StripNestedJson(json);
                MatchCollection matches = Regex.Matches(
                    body,
                    "\"([^\"]+)\"\\s*:\\s*(\"([^\"]*)\"|-?[0-9]+(?:\\.[0-9]+)?|true|false)",
                    RegexOptions.IgnoreCase
                );

                foreach (Match match in matches)
                {
                    string key = match.Groups[1].Value;
                    string value = match.Groups[3].Success && match.Groups[3].Value.Length > 0
                        ? match.Groups[3].Value
                        : match.Groups[2].Value;

                    values[key] = value;
                }
            }
            catch
            {
            }

            return values;
        }

        private string StripNestedJson(string json)
        {
            StringBuilder builder = new StringBuilder();
            bool inString = false;
            bool escaped = false;
            int nestedDepth = 0;

            for (int i = 0; i < json.Length; i++)
            {
                char c = json[i];

                if (escaped)
                {
                    if (nestedDepth == 0)
                        builder.Append(c);

                    escaped = false;
                    continue;
                }

                if (c == '\\' && inString)
                {
                    if (nestedDepth == 0)
                        builder.Append(c);

                    escaped = true;
                    continue;
                }

                if (c == '"')
                {
                    if (nestedDepth == 0)
                        builder.Append(c);

                    inString = !inString;
                    continue;
                }

                if (!inString)
                {
                    if ((c == '{' || c == '[') && i > 0)
                    {
                        nestedDepth++;
                        continue;
                    }

                    if ((c == '}' || c == ']') && nestedDepth > 0)
                    {
                        nestedDepth--;
                        continue;
                    }
                }

                if (nestedDepth == 0)
                    builder.Append(c);
            }

            return builder.ToString();
        }

        private string ReadJsonArray(string json, string key)
        {
            int colonIndex = FindKeyColon(json, key);

            if (colonIndex < 0)
                return "";

            int start = json.IndexOf('[', colonIndex + 1);

            if (start < 0)
                return "";

            int end = FindMatching(json, start, '[', ']');

            if (end < 0)
                return "";

            return json.Substring(start + 1, end - start - 1);
        }

        private string ReadJsonObject(string json, string key)
        {
            int colonIndex = FindKeyColon(json, key);

            if (colonIndex < 0)
                return "";

            int start = json.IndexOf('{', colonIndex + 1);

            if (start < 0)
                return "";

            int end = FindMatching(json, start, '{', '}');

            if (end < 0)
                return "";

            return json.Substring(start, end - start + 1);
        }

        private int ReadJsonInt(string json, string key, int fallback)
        {
            try
            {
                string pattern = "\"" + Regex.Escape(key) + "\"\\s*:\\s*(-?[0-9]+)";
                Match match = Regex.Match(json, pattern);

                if (!match.Success)
                    return fallback;

                return int.Parse(match.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
            }
            catch
            {
                return fallback;
            }
        }

        private int FindKeyColon(string json, string key)
        {
            string pattern = "\"" + Regex.Escape(key) + "\"\\s*:";
            Match match = Regex.Match(json, pattern);

            if (!match.Success)
                return -1;

            return match.Index + match.Length - 1;
        }

        private int FindMatching(string json, int startIndex, char openChar, char closeChar)
        {
            bool inString = false;
            bool escaped = false;
            int depth = 0;

            for (int i = startIndex; i < json.Length; i++)
            {
                char c = json[i];

                if (escaped)
                {
                    escaped = false;
                    continue;
                }

                if (c == '\\' && inString)
                {
                    escaped = true;
                    continue;
                }

                if (c == '"')
                {
                    inString = !inString;
                    continue;
                }

                if (inString)
                    continue;

                if (c == openChar)
                    depth++;

                if (c == closeChar)
                {
                    depth--;

                    if (depth == 0)
                        return i;
                }
            }

            return -1;
        }

        private List<string> SplitJsonObjects(string jsonArrayContent)
        {
            List<string> objects = new List<string>();

            if (string.IsNullOrEmpty(jsonArrayContent))
                return objects;

            bool inString = false;
            bool escaped = false;
            int depth = 0;
            int objectStart = -1;

            for (int i = 0; i < jsonArrayContent.Length; i++)
            {
                char c = jsonArrayContent[i];

                if (escaped)
                {
                    escaped = false;
                    continue;
                }

                if (c == '\\' && inString)
                {
                    escaped = true;
                    continue;
                }

                if (c == '"')
                {
                    inString = !inString;
                    continue;
                }

                if (inString)
                    continue;

                if (c == '{')
                {
                    if (depth == 0)
                        objectStart = i;

                    depth++;
                }

                if (c == '}')
                {
                    depth--;

                    if (depth == 0 && objectStart >= 0)
                    {
                        objects.Add(jsonArrayContent.Substring(objectStart, i - objectStart + 1));
                        objectStart = -1;
                    }
                }
            }

            return objects;
        }

        private class EffectTypeConfig
        {
            public string type;
            public MissionEffect defaultEffect = new MissionEffect();
            public Dictionary<string, MissionEffect> configsById = new Dictionary<string, MissionEffect>();

            public MissionEffect GetEffectForId(string id)
            {
                if (string.IsNullOrEmpty(id) || !configsById.ContainsKey(id))
                    return null;

                return configsById[id];
            }
        }
    }
}
