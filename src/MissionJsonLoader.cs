using GTA;
using GTAVTrueCrimesMod.Models;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace GTAVTrueCrimesMod
{
    public class MissionJsonLoader
    {
        private readonly string missionsFolder;

        public MissionJsonLoader(string missionsFolder)
        {
            this.missionsFolder = missionsFolder;
        }

        public string MissionsFolder
        {
            get { return missionsFolder; }
        }

        public List<DetectiveMission> LoadMissions()
        {
            List<DetectiveMission> missions = new List<DetectiveMission>();

            try
            {
                if (!Directory.Exists(missionsFolder))
                {
                    Directory.CreateDirectory(missionsFolder);
                    GTA.UI.Screen.ShowSubtitle("Utworzono folder missions. Dodaj tam pliki .json.", 7000);
                    return missions;
                }

                string[] files = Directory.GetFiles(missionsFolder, "*.json");

                foreach (string file in files)
                {
                    DetectiveMission mission = LoadMissionFile(file);

                    if (mission == null)
                        continue;

                    if (string.IsNullOrEmpty(mission.id))
                        mission.id = Path.GetFileNameWithoutExtension(file);

                    if (string.IsNullOrEmpty(mission.title))
                        mission.title = mission.id;

                    mission.sourceFile = file;
                    missions.Add(mission);
                }
            }
            catch (Exception ex)
            {
                GTA.UI.Screen.ShowSubtitle("Blad ladowania misji: " + ex.Message, 10000);
            }

            return missions;
        }

        private DetectiveMission LoadMissionFile(string filePath)
        {
            try
            {
                string json = ReadAllTextShared(filePath);

                DetectiveMission mission = new DetectiveMission();

                mission.id = ReadJsonString(json, "id");
                mission.title = ReadJsonString(json, "title");
                mission.description = ReadJsonString(json, "description");
                mission.firstObjective = ReadJsonString(json, "firstObjective");
                mission.firstNode = ReadJsonString(json, "firstNode");
                mission.debugStartNode = ReadJsonString(json, "debugStartNode");

                mission.startLocation = new JsonVector3();
                string startLocationJson = ReadJsonObject(json, "startLocation");
                mission.startLocation.x = ReadJsonFloat(startLocationJson, "x", 0f);
                mission.startLocation.y = ReadJsonFloat(startLocationJson, "y", 0f);
                mission.startLocation.z = ReadJsonFloat(startLocationJson, "z", 0f);

                mission.nodes = ReadMissionNodes(json, Path.GetDirectoryName(filePath));

                string firstObjectiveFromArray = ReadJsonString(json, "text");

                if (string.IsNullOrEmpty(mission.firstObjective))
                    mission.firstObjective = firstObjectiveFromArray;

                return mission;
            }
            catch (Exception ex)
            {
                GTA.UI.Screen.ShowSubtitle("Blad JSON: " + Path.GetFileName(filePath) + " | " + ex.Message, 10000);
                return null;
            }
        }

        private MissionNode[] ReadMissionNodes(string json, string missionFolder)
        {
            try
            {
                string nodesJson = ReadJsonArray(json, "nodes");

                if (string.IsNullOrEmpty(nodesJson))
                    return new MissionNode[0];

                List<string> nodeObjects = SplitJsonObjects(nodesJson);
                List<MissionNode> nodes = new List<MissionNode>();

                for (int i = 0; i < nodeObjects.Count; i++)
                {
                    string nodeJson = nodeObjects[i];
                    MissionNode node = new MissionNode();

                    node.id = ReadJsonString(nodeJson, "id");
                    node.type = ReadJsonString(nodeJson, "type");
                    node.text = ReadJsonString(nodeJson, "text");
                    node.completeWhen = ReadJsonString(nodeJson, "completeWhen");
                    node.setFact = ReadJsonString(nodeJson, "setFact");
                    node.next = ReadJsonString(nodeJson, "next");
                    node.caller = ReadJsonString(nodeJson, "caller");
                    node.audio = ReadJsonString(nodeJson, "audio");
                    node.subtitlesFile = ReadJsonString(nodeJson, "subtitlesFile");
                    node.subtitles = ReadSubtitleCues(nodeJson, "subtitles");

                    if (!string.IsNullOrEmpty(node.subtitlesFile))
                        node.subtitles = ReadSubtitleCuesFile(missionFolder, node.subtitlesFile);

                    node.audioSegments = ReadAudioSegments(nodeJson, missionFolder);
                    node.completeAfterMs = ReadJsonInt(nodeJson, "completeAfterMs", 0);
                    node.onEnter = ReadMissionEffects(nodeJson, "onEnter", missionFolder);

                    string targetJson = ReadJsonObject(nodeJson, "target");

                    if (!string.IsNullOrEmpty(targetJson))
                    {
                        node.target = new JsonVector3();
                        node.target.x = ReadJsonFloat(targetJson, "x", 0f);
                        node.target.y = ReadJsonFloat(targetJson, "y", 0f);
                        node.target.z = ReadJsonFloat(targetJson, "z", 0f);
                    }

                    if (!string.IsNullOrEmpty(node.id))
                        nodes.Add(node);
                }

                return nodes.ToArray();
            }
            catch
            {
                return new MissionNode[0];
            }
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

        private MissionSubtitleCue[] ReadSubtitleCuesFile(string missionFolder, string subtitlesFile)
        {
            try
            {
                string path = subtitlesFile;

                if (!Path.IsPathRooted(path))
                    path = Path.Combine(missionFolder, subtitlesFile);

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

        private MissionEffect[] ReadMissionEffects(string json, string key, string missionFolder)
        {
            try
            {
                string effectsJson = ReadJsonArray(json, key);

                if (string.IsNullOrEmpty(effectsJson))
                    return new MissionEffect[0];

                List<string> effectObjects = SplitJsonObjects(effectsJson);
                List<MissionEffect> effects = new List<MissionEffect>();

                for (int i = 0; i < effectObjects.Count; i++)
                {
                    MissionEffect effect = ReadMissionEffect(effectObjects[i], missionFolder);

                    if (!string.IsNullOrEmpty(effect.type))
                        effects.Add(effect);
                }

                return effects.ToArray();
            }
            catch
            {
                return new MissionEffect[0];
            }
        }

        private MissionEffect ReadMissionEffect(string effectJson, string missionFolder)
        {
            MissionEffect effect = new MissionEffect();

            effect.args = ReadFlatJsonValues(effectJson);
            effect.type = effect.GetString("type", "");
            effect.id = effect.GetString("id", "");
            effect.subtitles = ReadSubtitleCues(effectJson, "subtitles");

            string subtitlesFile = effect.GetString("subtitlesFile", "");

            if (!string.IsNullOrEmpty(subtitlesFile))
                effect.subtitles = ReadSubtitleCuesFile(missionFolder, subtitlesFile);

            effect.audioSegments = ReadAudioSegments(effectJson, missionFolder);
            effect.onKilledByPlayer = ReadMissionEffects(effectJson, "onKilledByPlayer", missionFolder);
            effect.onKilledByOther = ReadMissionEffects(effectJson, "onKilledByOther", missionFolder);

            return effect;
        }

        private MissionAudioSegment[] ReadAudioSegments(string json, string missionFolder)
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
                        segment.subtitles = ReadSubtitleCuesFile(missionFolder, segment.subtitlesFile);

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

        private float ReadJsonFloat(string json, string key, float fallback)
        {
            try
            {
                string pattern = "\"" + Regex.Escape(key) + "\"\\s*:\\s*(-?[0-9]+(?:\\.[0-9]+)?)";
                Match match = Regex.Match(json, pattern);

                if (!match.Success)
                    return fallback;

                return float.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
            }
            catch
            {
                return fallback;
            }
        }

        private bool ReadJsonBool(string json, string key, bool fallback)
        {
            try
            {
                string pattern = "\"" + Regex.Escape(key) + "\"\\s*:\\s*(true|false)";
                Match match = Regex.Match(json, pattern, RegexOptions.IgnoreCase);

                if (!match.Success)
                    return fallback;

                return string.Equals(match.Groups[1].Value, "true", StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return fallback;
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

        private int ReadJsonInt(string json, string key, int fallback)
        {
            try
            {
                string pattern = "\"" + Regex.Escape(key) + "\"\\s*:\\s*(-?[0-9]+)";
                Match match = Regex.Match(json, pattern);

                if (!match.Success)
                    return fallback;

                return int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
            }
            catch
            {
                return fallback;
            }
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
            int depth = 0;
            bool inString = false;
            bool escaped = false;

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

        private List<string> SplitJsonObjects(string json)
        {
            List<string> objects = new List<string>();
            int index = 0;

            while (index < json.Length)
            {
                int start = json.IndexOf('{', index);

                if (start < 0)
                    break;

                int end = FindMatching(json, start, '{', '}');

                if (end < 0)
                    break;

                objects.Add(json.Substring(start, end - start + 1));
                index = end + 1;
            }

            return objects;
        }
    }
}
