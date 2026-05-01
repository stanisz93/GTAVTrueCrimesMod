using GTA;
using GTA.Native;
using GTA.Math;

using System;
using System.IO;
using System.Text;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Globalization;
using System.Windows.Forms;

public class DetectiveMissionSelector : Script
{
    private bool menuOpen = false;
    private int selectedIndex = 0;

    private readonly List<DetectiveMission> missions = new List<DetectiveMission>();

    private DetectiveMission activeMission;
    private Blip activeMissionBlip;

    private string currentNodeId;
    private MissionNode currentNode;
    private readonly Dictionary<string, bool> facts = new Dictionary<string, bool>();
    private readonly Stack<string> nodeHistory = new Stack<string>();
    private Blip currentNodeBlip;

    private readonly string missionsFolder;

    public DetectiveMissionSelector()
    {
        Tick += OnTick;
        KeyDown += OnKeyDown;
        Interval = 0;

        string baseDir = AppDomain.CurrentDomain.BaseDirectory;

        if (Path.GetFileName(baseDir.TrimEnd(Path.DirectorySeparatorChar)).Equals("scripts", StringComparison.OrdinalIgnoreCase))
        {
            baseDir = Directory.GetParent(baseDir.TrimEnd(Path.DirectorySeparatorChar)).FullName;
        }

        missionsFolder = Path.Combine(baseDir, "missions");
        LoadMissionsFromJson();

        GTA.UI.Screen.ShowSubtitle(
            "Detective Missions: wczytano " + missions.Count + " misji z: " + missionsFolder,
            7000
        );
    }

    private void LoadMissionsFromJson()
    {
        missions.Clear();

        try
        {
            if (!Directory.Exists(missionsFolder))
            {
                Directory.CreateDirectory(missionsFolder);
                GTA.UI.Screen.ShowSubtitle("Utworzono folder missions. Dodaj tam pliki .json.", 7000);
                return;
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
    }

    private DetectiveMission LoadMissionFile(string filePath)
    {
        try
        {
            string json = File.ReadAllText(filePath, Encoding.UTF8);

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

            mission.nodes = ReadMissionNodes(json);

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

    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.KeyCode == Keys.F7)
        {
            menuOpen = !menuOpen;

            if (menuOpen)
            {
                LoadMissionsFromJson();
                selectedIndex = Clamp(selectedIndex, 0, Math.Max(0, missions.Count - 1));
            }

            return;
        }

        if (e.KeyCode == Keys.F8)
        {
            CompleteCurrentNode();
            return;
        }

        if (e.KeyCode == Keys.F9)
        {
            if (!string.IsNullOrEmpty(currentNodeId))
                EnterNode(currentNodeId, false);
            else
                GTA.UI.Screen.ShowSubtitle("Brak aktualnego node'a do restartu.", 4000);

            return;
        }

        if (e.KeyCode == Keys.F10)
        {
            if (nodeHistory.Count > 0)
                EnterNode(nodeHistory.Pop(), false);
            else
                GTA.UI.Screen.ShowSubtitle("Historia node'ow jest pusta.", 4000);

            return;
        }

        if (e.KeyCode == Keys.F11)
        {
            ShowDebugState();
            return;
        }

        if (!menuOpen)
            return;

        if (e.KeyCode == Keys.Up)
        {
            if (missions.Count == 0)
                return;

            selectedIndex--;

            if (selectedIndex < 0)
                selectedIndex = missions.Count - 1;
        }

        if (e.KeyCode == Keys.Down)
        {
            if (missions.Count == 0)
                return;

            selectedIndex++;

            if (selectedIndex >= missions.Count)
                selectedIndex = 0;
        }

        if (e.KeyCode == Keys.Enter)
        {
            if (missions.Count == 0)
            {
                GTA.UI.Screen.ShowSubtitle("Brak misji JSON w folderze missions.", 5000);
                return;
            }

            StartMission(missions[selectedIndex]);
            menuOpen = false;
        }

        if (e.KeyCode == Keys.Back)
        {
            menuOpen = false;
        }

        if (e.KeyCode == Keys.R)
        {
            LoadMissionsFromJson();
            GTA.UI.Screen.ShowSubtitle("Przeladowano JSON-y misji. Liczba misji: " + missions.Count, 5000);
        }

        if (e.KeyCode == Keys.V)
        {
            ShowPlayerCoordinates();
        }
    }

    private void OnTick(object sender, EventArgs e)
    {
        if (menuOpen)
        {
            DrawMissionMenu();
        }

        if (activeMission != null)
        {
            DrawActiveMissionInfo();
            TickCurrentNode();
        }
    }

    private MissionNode[] ReadMissionNodes(string json)
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

    private void StartMission(DetectiveMission mission)
    {
        activeMission = mission;
        facts.Clear();
        nodeHistory.Clear();
        currentNodeId = "";
        currentNode = null;

        ClearActiveMissionBlip();
        ClearNodeBlip();

        string startNodeId = mission.debugStartNode;

        if (string.IsNullOrEmpty(startNodeId))
            startNodeId = mission.firstNode;

        if (!string.IsNullOrEmpty(startNodeId))
        {
            GTA.UI.Screen.ShowSubtitle("Sprawa: " + mission.title, 2500);
            EnterNode(startNodeId);
            return;
        }

        Vector3 start = ToVector3(mission.startLocation);

        activeMissionBlip = World.CreateBlip(start);
        activeMissionBlip.Sprite = BlipSprite.Standard;
        activeMissionBlip.Color = BlipColor.Red;
        activeMissionBlip.Name = mission.title;

        string objective = mission.firstObjective;

        if (string.IsNullOrEmpty(objective) && mission.objectives != null && mission.objectives.Length > 0)
            objective = mission.objectives[0].text;

        if (string.IsNullOrEmpty(objective))
            objective = "Rozpoczeto sprawe.";

        GTA.UI.Screen.ShowSubtitle("Sprawa: " + mission.title + " | " + objective, 8000);
    }

    private void EnterNode(string nodeId)
    {
        EnterNode(nodeId, true);
    }

    private void EnterNode(string nodeId, bool pushHistory)
    {
        if (activeMission == null)
        {
            GTA.UI.Screen.ShowSubtitle("Brak aktywnej misji.", 4000);
            return;
        }

        MissionNode node = FindNode(nodeId);

        if (node == null)
        {
            GTA.UI.Screen.ShowSubtitle("Blad: nie znaleziono node'a: " + nodeId, 6000);
            return;
        }

        if (pushHistory && !string.IsNullOrEmpty(currentNodeId))
            nodeHistory.Push(currentNodeId);

        currentNodeId = nodeId;
        currentNode = node;

        ClearNodeBlip();

        if (node.target != null)
        {
            currentNodeBlip = World.CreateBlip(ToVector3(node.target));
            currentNodeBlip.Sprite = BlipSprite.Standard;
            currentNodeBlip.Color = BlipColor.Yellow;
            currentNodeBlip.Name = string.IsNullOrEmpty(node.text) ? node.id : node.text;
        }

        string text = node.text;

        if (string.IsNullOrEmpty(text))
            text = "Node: " + node.id;

        GTA.UI.Screen.ShowSubtitle(text, 8000);
    }

    private void TickCurrentNode()
    {
        if (currentNode == null)
            return;

        if (currentNode.completeWhen != "playerNearTarget")
            return;

        if (currentNode.target == null)
            return;

        float distance = Game.Player.Character.Position.DistanceTo(ToVector3(currentNode.target));

        if (distance <= 3.0f)
            CompleteCurrentNode();
    }

    private void CompleteCurrentNode()
    {
        if (currentNode == null)
        {
            GTA.UI.Screen.ShowSubtitle("Brak aktywnego node'a.", 4000);
            return;
        }

        if (!string.IsNullOrEmpty(currentNode.setFact))
            facts[currentNode.setFact] = true;

        ClearNodeBlip();

        if (!string.IsNullOrEmpty(currentNode.next))
        {
            EnterNode(currentNode.next);
            return;
        }

        GTA.UI.Screen.ShowSubtitle("Koniec sciezki", 5000);
        currentNode = null;
        currentNodeId = "";
    }

    private MissionNode FindNode(string id)
    {
        if (activeMission == null || activeMission.nodes == null || string.IsNullOrEmpty(id))
            return null;

        for (int i = 0; i < activeMission.nodes.Length; i++)
        {
            if (activeMission.nodes[i] != null && activeMission.nodes[i].id == id)
                return activeMission.nodes[i];
        }

        return null;
    }

    private void ClearActiveMissionBlip()
    {
        if (activeMissionBlip != null && activeMissionBlip.Exists())
        {
            activeMissionBlip.Delete();
            activeMissionBlip = null;
        }
    }

    private void ClearNodeBlip()
    {
        if (currentNodeBlip != null && currentNodeBlip.Exists())
        {
            currentNodeBlip.Delete();
            currentNodeBlip = null;
        }
    }

    private void ShowDebugState()
    {
        StringBuilder text = new StringBuilder();
        text.Append("Node: ");
        text.Append(string.IsNullOrEmpty(currentNodeId) ? "-" : currentNodeId);
        text.Append(" | Facts: ");

        bool anyFact = false;

        foreach (KeyValuePair<string, bool> fact in facts)
        {
            if (!fact.Value)
                continue;

            if (anyFact)
                text.Append(", ");

            text.Append(fact.Key);
            anyFact = true;
        }

        if (!anyFact)
            text.Append("-");

        GTA.UI.Screen.ShowSubtitle(text.ToString(), 8000);
    }

    private void DrawMissionMenu()
    {
        DrawText("SPRAWY DETEKTYWISTYCZNE", 0.08f, 0.10f, 0.55f);

        if (missions.Count == 0)
        {
            DrawText("Brak plikow .json w folderze missions/", 0.08f, 0.17f, 0.38f);
            DrawText("F7 - zamknij | R - przeladuj | V - pozycja gracza", 0.08f, 0.22f, 0.32f);
            return;
        }

        for (int i = 0; i < missions.Count; i++)
        {
            string prefix = i == selectedIndex ? "> " : "  ";
            DrawText(prefix + missions[i].title, 0.08f, 0.17f + i * 0.035f, 0.40f);
        }

        DetectiveMission selected = missions[selectedIndex];

        DrawText("Opis: " + SafeText(selected.description), 0.08f, 0.42f, 0.32f);
        DrawText("Enter - rozpocznij | Backspace/F7 - zamknij | R - przeladuj JSON | V - pozycja gracza", 0.08f, 0.47f, 0.30f);
    }

    private void DrawActiveMissionInfo()
    {
        DrawText("Aktywna sprawa: " + activeMission.title, 0.70f, 0.08f, 0.32f);

        if (!string.IsNullOrEmpty(currentNodeId))
            DrawText("Node: " + currentNodeId, 0.70f, 0.115f, 0.28f);
    }

    private Vector3 ToVector3(JsonVector3 pos)
    {
        if (pos == null)
            return Game.Player.Character.Position;

        return new Vector3(pos.x, pos.y, pos.z);
    }

    private void ShowPlayerCoordinates()
    {
        Vector3 pos = Game.Player.Character.Position;

        string text =
            "Pozycja gracza: x=" + pos.X.ToString("0.00", CultureInfo.InvariantCulture) +
            ", y=" + pos.Y.ToString("0.00", CultureInfo.InvariantCulture) +
            ", z=" + pos.Z.ToString("0.00", CultureInfo.InvariantCulture);

        GTA.UI.Screen.ShowSubtitle(text, 8000);
    }

    private string SafeText(string value)
    {
        if (string.IsNullOrEmpty(value))
            return "-";

        if (value.Length > 90)
            return value.Substring(0, 90) + "...";

        return value;
    }

    private int Clamp(int value, int min, int max)
    {
        if (value < min)
            return min;

        if (value > max)
            return max;

        return value;
    }

    private void DrawText(string text, float x, float y, float scale)
    {
        Function.Call(Hash.SET_TEXT_FONT, 0);
        Function.Call(Hash.SET_TEXT_SCALE, scale, scale);
        Function.Call(Hash.SET_TEXT_COLOUR, 255, 255, 255, 255);
        Function.Call(Hash.SET_TEXT_OUTLINE);
        Function.Call(Hash.BEGIN_TEXT_COMMAND_DISPLAY_TEXT, "STRING");
        Function.Call(Hash.ADD_TEXT_COMPONENT_SUBSTRING_PLAYER_NAME, text);
        Function.Call(Hash.END_TEXT_COMMAND_DISPLAY_TEXT, x, y);
    }
}

public class DetectiveMission
{
    public string id;
    public string title;
    public string description;
    public JsonVector3 startLocation;
    public string firstObjective;
    public string firstNode;
    public string debugStartNode;
    public MissionNode[] nodes;
    public SuspectData[] suspects;
    public ObjectiveData[] objectives;

    public string sourceFile;
}

public class MissionNode
{
    public string id;
    public string type;
    public string text;
    public JsonVector3 target;
    public string completeWhen;
    public string setFact;
    public string next;
}

public class JsonVector3
{
    public float x;
    public float y;
    public float z;
}

public class SuspectData
{
    public string id;
    public string name;
    public string role;
}

public class ObjectiveData
{
    public string id;
    public string text;
}
