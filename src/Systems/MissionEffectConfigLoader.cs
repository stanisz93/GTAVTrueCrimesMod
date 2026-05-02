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
        private readonly string configFolder;
        private readonly Dictionary<string, EffectTypeConfig> cache = new Dictionary<string, EffectTypeConfig>();

        public MissionEffectConfigLoader(string missionsFolder)
        {
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
                MergeArgs(resolved.args, config.defaultArgs);

                Dictionary<string, string> idArgs = config.GetArgsForId(effect.id);

                if (idArgs != null)
                    MergeArgs(resolved.args, idArgs);
            }

            MergeArgs(resolved.args, effect.args);

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
            config.defaultArgs = ReadFlatJsonValues(defaultJson);

            if (!config.defaultArgs.ContainsKey("type"))
                config.defaultArgs["type"] = config.type;

            string configsJson = ReadJsonArray(json, "configs");
            List<string> objects = SplitJsonObjects(configsJson);

            for (int i = 0; i < objects.Count; i++)
            {
                Dictionary<string, string> args = ReadFlatJsonValues(objects[i]);
                string id = GetArg(args, "id", "");

                if (string.IsNullOrEmpty(id))
                    continue;

                if (!args.ContainsKey("type"))
                    args["type"] = config.type;

                config.configsById[id] = args;
            }

            return config;
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
            public Dictionary<string, string> defaultArgs = new Dictionary<string, string>();
            public Dictionary<string, Dictionary<string, string>> configsById = new Dictionary<string, Dictionary<string, string>>();

            public Dictionary<string, string> GetArgsForId(string id)
            {
                if (string.IsNullOrEmpty(id) || !configsById.ContainsKey(id))
                    return null;

                return configsById[id];
            }
        }
    }
}
