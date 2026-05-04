using GTAVTrueCrimesMod.Models;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace GTAVTrueCrimesMod.Systems
{
    public static class MissionAudioFolderLoader
    {
        public static MissionAudioSegment[] LoadConversationSegments(
            string missionFolder,
            string audioFolder,
            string firstSpeaker,
            int gapAfterMs)
        {
            if (string.IsNullOrEmpty(audioFolder))
                return new MissionAudioSegment[0];

            string audioRoot = FindAudioRoot(missionFolder);

            if (string.IsNullOrEmpty(audioRoot))
                return new MissionAudioSegment[0];

            string resolvedAudioFolder = Path.IsPathRooted(audioFolder)
                ? audioFolder
                : Path.Combine(audioRoot, audioFolder.Replace('/', Path.DirectorySeparatorChar));

            if (!Directory.Exists(resolvedAudioFolder))
                return new MissionAudioSegment[0];

            FileInfo[] files = new DirectoryInfo(resolvedAudioFolder).GetFiles("*.wav", SearchOption.TopDirectoryOnly);
            Array.Sort(files, CompareFilesByConversationOrder);

            List<MissionAudioSegment> segments = new List<MissionAudioSegment>();
            string normalizedAudioFolder = NormalizePath(audioFolder);
            string normalizedSubtitleFolder = NormalizePath(Path.Combine("subtitles", normalizedAudioFolder));
            string first = NormalizeSpeaker(firstSpeaker);
            string second = first == "shooter" ? "shouter" : "shooter";

            for (int i = 0; i < files.Length; i++)
            {
                MissionAudioSegment segment = new MissionAudioSegment();
                segment.audio = NormalizePath(MakeRelativePath(audioRoot, files[i].FullName));
                segment.speaker = i % 2 == 0 ? first : second;
                segment.gapAfterMs = Math.Max(0, gapAfterMs);
                segment.completeAfterMs = ReadWavDurationMs(files[i].FullName);

                string subtitleRelativePath = FindSubtitleFile(
                    missionFolder,
                    normalizedSubtitleFolder,
                    Path.GetFileNameWithoutExtension(files[i].Name)
                );

                if (!string.IsNullOrEmpty(subtitleRelativePath))
                {
                    segment.subtitlesFile = subtitleRelativePath;
                    segment.subtitles = ReadSubtitleCuesFile(Path.Combine(missionFolder, subtitleRelativePath));
                }

                segments.Add(segment);
            }

            return segments.ToArray();
        }

        private static int ReadWavDurationMs(string path)
        {
            try
            {
                using (FileStream stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                using (BinaryReader reader = new BinaryReader(stream))
                {
                    if (ReadAscii(reader, 4) != "RIFF")
                        return 0;

                    reader.ReadInt32();

                    if (ReadAscii(reader, 4) != "WAVE")
                        return 0;

                    int byteRate = 0;
                    int dataSize = 0;

                    while (stream.Position + 8 <= stream.Length)
                    {
                        string chunkId = ReadAscii(reader, 4);
                        int chunkSize = reader.ReadInt32();
                        long chunkStart = stream.Position;

                        if (chunkId == "fmt " && chunkSize >= 16)
                        {
                            reader.ReadInt16();
                            reader.ReadInt16();
                            reader.ReadInt32();
                            byteRate = reader.ReadInt32();
                        }
                        else if (chunkId == "data")
                        {
                            dataSize = chunkSize;
                        }

                        long nextChunk = chunkStart + chunkSize;

                        if ((chunkSize % 2) != 0)
                            nextChunk++;

                        if (nextChunk <= chunkStart || nextChunk > stream.Length)
                            break;

                        stream.Position = nextChunk;

                        if (byteRate > 0 && dataSize > 0)
                            return Math.Max(1, (int)Math.Ceiling(dataSize * 1000.0 / byteRate));
                    }
                }
            }
            catch
            {
            }

            return 0;
        }

        private static string ReadAscii(BinaryReader reader, int count)
        {
            return Encoding.ASCII.GetString(reader.ReadBytes(count));
        }

        private static int CompareFilesByConversationOrder(FileInfo left, FileInfo right)
        {
            int leftNumber = ReadFirstNumber(left.Name);
            int rightNumber = ReadFirstNumber(right.Name);
            int numberCompare = leftNumber.CompareTo(rightNumber);

            if (numberCompare != 0)
                return numberCompare;

            return string.Compare(left.Name, right.Name, StringComparison.OrdinalIgnoreCase);
        }

        private static int ReadFirstNumber(string fileName)
        {
            Match match = Regex.Match(fileName, "\\d+");

            if (!match.Success)
                return int.MaxValue;

            int value;

            if (int.TryParse(match.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out value))
                return value;

            return int.MaxValue;
        }

        private static string FindAudioRoot(string missionFolder)
        {
            List<string> candidates = new List<string>();

            if (!string.IsNullOrEmpty(missionFolder))
            {
                DirectoryInfo parent = Directory.GetParent(missionFolder);

                if (parent != null)
                {
                    candidates.Add(Path.Combine(parent.FullName, "audio"));
                    candidates.Add(Path.Combine(parent.FullName, "scripts", "DetectiveAudio"));
                }
            }

            string baseDir = AppDomain.CurrentDomain.BaseDirectory;

            if (!string.IsNullOrEmpty(baseDir))
            {
                string trimmedBaseDir = baseDir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

                if (Path.GetFileName(trimmedBaseDir).Equals("scripts", StringComparison.OrdinalIgnoreCase))
                    candidates.Add(Path.Combine(trimmedBaseDir, "DetectiveAudio"));
                else
                    candidates.Add(Path.Combine(trimmedBaseDir, "scripts", "DetectiveAudio"));
            }

            for (int i = 0; i < candidates.Count; i++)
            {
                if (Directory.Exists(candidates[i]))
                    return candidates[i];
            }

            return candidates.Count == 0 ? "" : candidates[0];
        }

        private static string FindSubtitleFile(string missionFolder, string subtitlesFolder, string baseName)
        {
            string[] candidates = new[]
            {
                NormalizePath(Path.Combine(subtitlesFolder, baseName + ".subtitles.json")),
                NormalizePath(Path.Combine(subtitlesFolder, baseName + ".json"))
            };

            for (int i = 0; i < candidates.Length; i++)
            {
                string path = Path.Combine(missionFolder, candidates[i]);

                if (File.Exists(path))
                    return candidates[i];
            }

            return "";
        }

        private static string NormalizeSpeaker(string speaker)
        {
            if (string.Equals(speaker, "shooter", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(speaker, "officer2", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(speaker, "second", StringComparison.OrdinalIgnoreCase))
            {
                return "shooter";
            }

            return "shouter";
        }

        private static string NormalizePath(string path)
        {
            if (string.IsNullOrEmpty(path))
                return "";

            return path.Replace('\\', '/').Trim('/');
        }

        private static string MakeRelativePath(string root, string fullPath)
        {
            try
            {
                string normalizedRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
                string normalizedPath = Path.GetFullPath(fullPath);

                if (normalizedPath.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase))
                    return normalizedPath.Substring(normalizedRoot.Length);
            }
            catch
            {
            }

            return Path.GetFileName(fullPath);
        }

        private static MissionSubtitleCue[] ReadSubtitleCuesFile(string path)
        {
            try
            {
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

        private static MissionSubtitleCue[] ReadSubtitleCueArray(string cuesJson)
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

        private static string ReadAllTextShared(string filePath)
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

        private static string ReadJsonString(string json, string key)
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

        private static int ReadJsonInt(string json, string key, int fallback)
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

        private static string ReadJsonArray(string json, string key)
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

        private static int FindKeyColon(string json, string key)
        {
            string pattern = "\"" + Regex.Escape(key) + "\"\\s*:";
            Match match = Regex.Match(json, pattern);

            if (!match.Success)
                return -1;

            return match.Index + match.Length - 1;
        }

        private static int FindMatching(string json, int startIndex, char openChar, char closeChar)
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

        private static List<string> SplitJsonObjects(string jsonArrayContent)
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
    }
}
