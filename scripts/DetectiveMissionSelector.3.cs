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
            GTA.UI.Screen.ShowSubtitle("Błąd ładowania misji: " + ex.Message, 10000);
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

        mission.startLocation = new JsonVector3();
        mission.startLocation.x = ReadJsonFloat(json, "x", 0f);
        mission.startLocation.y = ReadJsonFloat(json, "y", 0f);
        mission.startLocation.z = ReadJsonFloat(json, "z", 0f);

        // Minimalna obsługa objectives:
        // Na razie bierzemy pierwszy znaleziony "text" jako fallback.
        string firstObjectiveFromArray = ReadJsonString(json, "text");

        if (string.IsNullOrEmpty(mission.firstObjective))
            mission.firstObjective = firstObjectiveFromArray;

        return mission;
    }
    catch (Exception ex)
    {
        GTA.UI.Screen.ShowSubtitle("Błąd JSON: " + Path.GetFileName(filePath) + " | " + ex.Message, 10000);
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
                GTA.UI.Screen.ShowSubtitle("Brak misji JSON w folderze DetectiveMissions.", 5000);
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
            GTA.UI.Screen.ShowSubtitle("Przeładowano JSON-y misji. Liczba misji: " + missions.Count, 5000);
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
    private void StartMission(DetectiveMission mission)
    {
        activeMission = mission;

        if (activeMissionBlip != null && activeMissionBlip.Exists())
        {
            activeMissionBlip.Delete();
            activeMissionBlip = null;
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
            objective = "Rozpoczęto sprawę.";

        GTA.UI.Screen.ShowSubtitle("Sprawa: " + mission.title + " | " + objective, 8000);
    }

    

    private void DrawMissionMenu()
    {
        DrawText("SPRAWY DETEKTYWISTYCZNE", 0.08f, 0.10f, 0.55f);

        if (missions.Count == 0)
        {
            DrawText("Brak plików .json w scripts/DetectiveMissions/", 0.08f, 0.17f, 0.38f);
            DrawText("F7 - zamknij | R - przeładuj", 0.08f, 0.22f, 0.32f);
            return;
        }

        for (int i = 0; i < missions.Count; i++)
        {
            string prefix = i == selectedIndex ? "> " : "  ";
            DrawText(prefix + missions[i].title, 0.08f, 0.17f + i * 0.035f, 0.40f);
        }

        DetectiveMission selected = missions[selectedIndex];

        DrawText("Opis: " + SafeText(selected.description), 0.08f, 0.42f, 0.32f);
        DrawText("Enter - rozpocznij | Backspace/F7 - zamknij | R - przeładuj JSON", 0.08f, 0.47f, 0.30f);
    }

    private void DrawActiveMissionInfo()
    {
        DrawText("Aktywna sprawa: " + activeMission.title, 0.70f, 0.08f, 0.32f);
    }

    private Vector3 ToVector3(JsonVector3 pos)
    {
        if (pos == null)
            return Game.Player.Character.Position;

        return new Vector3(pos.x, pos.y, pos.z);
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
    public SuspectData[] suspects;
    public ObjectiveData[] objectives;

    public string sourceFile;
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