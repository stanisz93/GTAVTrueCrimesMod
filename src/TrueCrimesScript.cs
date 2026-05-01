using GTA;
using GTA.Native;
using GTA.Math;
using GTAVTrueCrimesMod.Models;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Windows.Forms;

namespace GTAVTrueCrimesMod
{
    public class TrueCrimesScript : Script
    {
        private bool menuOpen = false;
        private int selectedIndex = 0;

        private readonly List<DetectiveMission> missions = new List<DetectiveMission>();
        private readonly MissionJsonLoader missionLoader;
        private readonly MissionRuntime missionRuntime = new MissionRuntime();

        public TrueCrimesScript()
        {
            Tick += OnTick;
            KeyDown += OnKeyDown;
            Interval = 0;

            missionLoader = new MissionJsonLoader(FindMissionsFolder());
            LoadMissionsFromJson();

            GTA.UI.Screen.ShowSubtitle(
                "True Crimes: wczytano " + missions.Count + " misji z: " + missionLoader.MissionsFolder,
                7000
            );
        }

        private string FindMissionsFolder()
        {
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;

            if (Path.GetFileName(baseDir.TrimEnd(Path.DirectorySeparatorChar)).Equals("scripts", StringComparison.OrdinalIgnoreCase))
            {
                baseDir = Directory.GetParent(baseDir.TrimEnd(Path.DirectorySeparatorChar)).FullName;
            }

            return Path.Combine(baseDir, "missions");
        }

        private void LoadMissionsFromJson()
        {
            missions.Clear();
            missions.AddRange(missionLoader.LoadMissions());
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

            if (missionRuntime.MissionFailed)
            {
                if (e.KeyCode == Keys.Enter)
                    missionRuntime.RetryMission();

                return;
            }

            if (missionRuntime.IsPhoneRinging)
            {
                if (e.KeyCode == Keys.Enter)
                    missionRuntime.TryAnswerPhoneCall();

                return;
            }

            if (e.KeyCode == Keys.F8)
            {
                missionRuntime.CompleteCurrentNode();
                return;
            }

            if (e.KeyCode == Keys.F9)
            {
                missionRuntime.RestartCurrentNode();
                return;
            }

            if (e.KeyCode == Keys.F10)
            {
                missionRuntime.ReturnToPreviousNode();
                return;
            }

            if (e.KeyCode == Keys.F11)
            {
                missionRuntime.ShowDebugState();
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

                missionRuntime.StartMission(missions[selectedIndex]);
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

            if (missionRuntime.ActiveMission != null)
            {
                DrawActiveMissionInfo();
                missionRuntime.UpdateBackgroundBehaviors();
                missionRuntime.TickCurrentNode();
                DrawBackgroundDebugInfo();
            }

            if (missionRuntime.MissionFailed)
            {
                DrawMissionFailed();
            }
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
            DrawText("Aktywna sprawa: " + missionRuntime.ActiveMission.title, 0.70f, 0.08f, 0.32f);

            if (!string.IsNullOrEmpty(missionRuntime.CurrentNodeId))
                DrawText("Node: " + missionRuntime.CurrentNodeId, 0.70f, 0.115f, 0.28f);
        }

        private void DrawMissionFailed()
        {
            DrawText("MISJA NIEUDANA", 0.36f, 0.34f, 0.72f);
            DrawText(SafeText(missionRuntime.MissionFailureReason), 0.34f, 0.42f, 0.36f);
            DrawText("Enter - powtorz | F7 - menu", 0.37f, 0.48f, 0.32f);
        }

        private void DrawBackgroundDebugInfo()
        {
            for (int i = 0; i < missionRuntime.BackgroundDebugLineCount; i++)
            {
                string text = missionRuntime.GetBackgroundDebugText(i);

                if (!string.IsNullOrEmpty(text))
                    DrawTextColored(text, 0.03f, 0.78f + i * 0.028f, 0.30f, 255, 220, 40, 255);
            }
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
            DrawTextColored(text, x, y, scale, 255, 255, 255, 255);
        }

        private void DrawTextColored(string text, float x, float y, float scale, int red, int green, int blue, int alpha)
        {
            Function.Call(Hash.SET_TEXT_FONT, 0);
            Function.Call(Hash.SET_TEXT_SCALE, scale, scale);
            Function.Call(Hash.SET_TEXT_COLOUR, red, green, blue, alpha);
            Function.Call(Hash.SET_TEXT_OUTLINE);
            Function.Call(Hash.BEGIN_TEXT_COMMAND_DISPLAY_TEXT, "STRING");
            Function.Call(Hash.ADD_TEXT_COMPONENT_SUBSTRING_PLAYER_NAME, text);
            Function.Call(Hash.END_TEXT_COMMAND_DISPLAY_TEXT, x, y);
        }
    }
}
