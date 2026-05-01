using GTA;
using GTA.Math;
using GTA.Native;
using GTAVTrueCrimesMod.Behaviors;
using GTAVTrueCrimesMod.Effects;
using GTAVTrueCrimesMod.Models;
using GTAVTrueCrimesMod.NodeHandlers;
using System;
using System.Collections.Generic;
using System.IO;
using System.Media;
using System.Text;

namespace GTAVTrueCrimesMod
{
    public class MissionRuntime
    {
        private DetectiveMission activeMission;
        private DetectiveMission retryMission;
        private Blip activeMissionBlip;

        private string currentNodeId;
        private MissionNode currentNode;
        private readonly Dictionary<string, bool> facts = new Dictionary<string, bool>();
        private readonly Stack<string> nodeHistory = new Stack<string>();
        private Blip currentNodeBlip;
        private readonly List<IMissionNodeHandler> nodeHandlers = new List<IMissionNodeHandler>();
        private readonly List<IMissionEffectHandler> effectHandlers = new List<IMissionEffectHandler>();
        private readonly List<IMissionBackgroundBehavior> backgroundBehaviors = new List<IMissionBackgroundBehavior>();
        private bool delayedCompleteActive;
        private int delayedCompleteAt;
        private bool phoneCallRinging;
        private bool phoneDialogShown;
        private int phoneDialogAt;
        private int nextPhonePromptAt;
        private int phoneAudioStartedAt;
        private int nextSubtitleCueIndex;
        private string pendingPhoneCaller;
        private MissionNode pendingPhoneNode;
        private string pendingPhoneText;
        private bool nativeRingtonePlaying;
        private SoundPlayer activeCallPlayer;

        private bool missionFailed = false;
        private string missionFailureReason = "";

        public MissionRuntime()
        {
            nodeHandlers.Add(new PhoneCallNodeHandler());
            nodeHandlers.Add(new DefaultNodeHandler());
            effectHandlers.Add(new SpawnStalkerEffectHandler());
        }

        public DetectiveMission ActiveMission
        {
            get { return activeMission; }
        }

        public string CurrentNodeId
        {
            get { return currentNodeId; }
        }

        public bool MissionFailed
        {
            get { return missionFailed; }
        }

        public string MissionFailureReason
        {
            get { return missionFailureReason; }
        }

        public int BackgroundDebugLineCount
        {
            get { return backgroundBehaviors.Count; }
        }

        public bool IsPhoneRinging
        {
            get { return phoneCallRinging; }
        }

        public void StartMission(DetectiveMission mission)
        {
            activeMission = mission;
            retryMission = mission;
            missionFailed = false;
            missionFailureReason = "";
            facts.Clear();
            nodeHistory.Clear();
            currentNodeId = "";
            currentNode = null;
            ResetNodeTimers();
            ClearBackgroundBehaviors();

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

        public void EnterNode(string nodeId)
        {
            EnterNode(nodeId, true);
        }

        public void EnterNode(string nodeId, bool pushHistory)
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
            ResetNodeTimers();

            ClearNodeBlip();
            ApplyOnEnterEffects(node);

            for (int i = 0; i < nodeHandlers.Count; i++)
            {
                if (nodeHandlers[i].CanHandle(node))
                {
                    nodeHandlers[i].Enter(this, node);
                    return;
                }
            }
        }

        internal void ShowDefaultNode(MissionNode node)
        {
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

        public void TickCurrentNode()
        {
            TickDelayedNodeActions();

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

        public void UpdateBackgroundBehaviors()
        {
            if (missionFailed)
                return;

            for (int i = 0; i < backgroundBehaviors.Count; i++)
                backgroundBehaviors[i].Tick(this);
        }

        public void CompleteCurrentNode()
        {
            if (currentNode == null)
            {
                GTA.UI.Screen.ShowSubtitle("Brak aktywnego node'a.", 4000);
                return;
            }

            if (!string.IsNullOrEmpty(currentNode.setFact))
                facts[currentNode.setFact] = true;

            ClearNodeBlip();
            ResetNodeTimers();

            if (!string.IsNullOrEmpty(currentNode.next))
            {
                EnterNode(currentNode.next);
                return;
            }

            GTA.UI.Screen.ShowSubtitle("Koniec sciezki", 5000);
            currentNode = null;
            currentNodeId = "";
        }

        public void RetryMission()
        {
            if (retryMission == null)
                return;

            StartMission(retryMission);
        }

        public void RestartCurrentNode()
        {
            if (!string.IsNullOrEmpty(currentNodeId))
                EnterNode(currentNodeId, false);
            else
                GTA.UI.Screen.ShowSubtitle("Brak aktualnego node'a do restartu.", 4000);
        }

        public void ReturnToPreviousNode()
        {
            if (nodeHistory.Count > 0)
                EnterNode(nodeHistory.Pop(), false);
            else
                GTA.UI.Screen.ShowSubtitle("Historia node'ow jest pusta.", 4000);
        }

        public void ShowDebugState()
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

        public string GetBackgroundDebugText(int index)
        {
            if (index < 0 || index >= backgroundBehaviors.Count)
                return "";

            return backgroundBehaviors[index].DebugText;
        }

        internal void AddBackgroundBehavior(IMissionBackgroundBehavior behavior)
        {
            if (behavior == null)
                return;

            for (int i = backgroundBehaviors.Count - 1; i >= 0; i--)
            {
                if (backgroundBehaviors[i].Id == behavior.Id)
                {
                    backgroundBehaviors[i].Clear();
                    backgroundBehaviors.RemoveAt(i);
                }
            }

            backgroundBehaviors.Add(behavior);
        }

        private void ApplyOnEnterEffects(MissionNode node)
        {
            if (node == null || node.onEnter == null)
                return;

            for (int i = 0; i < node.onEnter.Length; i++)
            {
                MissionEffect effect = node.onEnter[i];

                for (int h = 0; h < effectHandlers.Count; h++)
                {
                    if (effectHandlers[h].CanHandle(effect))
                    {
                        effectHandlers[h].Apply(this, effect);
                        break;
                    }
                }
            }
        }

        internal void StartIncomingMissionCall(MissionNode node)
        {
            string caller = string.IsNullOrEmpty(node.caller) ? "Nieznany numer" : node.caller;
            pendingPhoneCaller = caller;
            pendingPhoneNode = node;
            pendingPhoneText = "";
            phoneCallRinging = true;
            phoneDialogShown = false;
            phoneDialogAt = 0;
            nextPhonePromptAt = Game.GameTime + 2000;
            delayedCompleteActive = false;
            delayedCompleteAt = 0;

            GTA.UI.Screen.ShowSubtitle("Dzwoni: " + caller + " | Enter - odbierz", 5000);
            PlayNativeRingtone();
        }

        public bool TryAnswerPhoneCall()
        {
            if (!phoneCallRinging || pendingPhoneNode == null)
                return false;

            StopNativeRingtone();
            phoneCallRinging = false;
            pendingPhoneText = pendingPhoneNode.text;
            phoneDialogShown = false;
            phoneAudioStartedAt = Game.GameTime;
            nextSubtitleCueIndex = 0;
            phoneDialogAt = HasPhoneSubtitleCues(pendingPhoneNode) ? 0 : Game.GameTime + 1000;
            delayedCompleteActive = true;
            delayedCompleteAt = Game.GameTime + GetPhoneCallCompleteAfterMs(pendingPhoneNode);

            GTA.UI.Screen.ShowSubtitle("Polaczenie odebrane.", 1200);

            if (!string.IsNullOrEmpty(pendingPhoneNode.audio))
                PlayMissionAudio(pendingPhoneNode.audio);

            return true;
        }

        private void TickDelayedNodeActions()
        {
            if (phoneCallRinging && Game.GameTime >= nextPhonePromptAt)
            {
                nextPhonePromptAt = Game.GameTime + 2000;
                GTA.UI.Screen.ShowSubtitle("Dzwoni: " + pendingPhoneCaller + " | Enter - odbierz", 2500);
            }

            TickPhoneSubtitleCues();

            if (phoneDialogAt > 0 && !phoneDialogShown && Game.GameTime >= phoneDialogAt)
            {
                phoneDialogShown = true;

                if (!string.IsNullOrEmpty(pendingPhoneText))
                    GTA.UI.Screen.ShowSubtitle(pendingPhoneText, 7000);
            }

            if (delayedCompleteActive && Game.GameTime >= delayedCompleteAt)
                CompleteCurrentNode();
        }

        private void TickPhoneSubtitleCues()
        {
            if (pendingPhoneNode == null || !HasPhoneSubtitleCues(pendingPhoneNode))
                return;

            if (nextSubtitleCueIndex >= pendingPhoneNode.subtitles.Length)
                return;

            int elapsed = Game.GameTime - phoneAudioStartedAt;
            MissionSubtitleCue cue = pendingPhoneNode.subtitles[nextSubtitleCueIndex];

            if (elapsed < cue.atMs)
                return;

            GTA.UI.Screen.ShowSubtitle(cue.text, cue.durationMs);
            nextSubtitleCueIndex++;
        }

        private void ResetNodeTimers()
        {
            StopNativeRingtone();
            delayedCompleteActive = false;
            delayedCompleteAt = 0;
            phoneCallRinging = false;
            phoneDialogShown = false;
            phoneDialogAt = 0;
            nextPhonePromptAt = 0;
            phoneAudioStartedAt = 0;
            nextSubtitleCueIndex = 0;
            pendingPhoneCaller = "";
            pendingPhoneNode = null;
            pendingPhoneText = "";
        }

        private bool HasPhoneSubtitleCues(MissionNode node)
        {
            return node != null && node.subtitles != null && node.subtitles.Length > 0;
        }

        private int GetPhoneCallCompleteAfterMs(MissionNode node)
        {
            if (node == null)
                return 8000;

            if (node.completeAfterMs > 0)
                return node.completeAfterMs;

            if (!HasPhoneSubtitleCues(node))
                return 8000;

            int lastEnd = 0;

            for (int i = 0; i < node.subtitles.Length; i++)
            {
                int cueEnd = node.subtitles[i].atMs + node.subtitles[i].durationMs;

                if (cueEnd > lastEnd)
                    lastEnd = cueEnd;
            }

            return Math.Max(lastEnd, 1000);
        }

        private void PlayNativeRingtone()
        {
            try
            {
                StopNativeRingtone();
                Function.Call((Hash)0xF9E56683CA8E11A5, "Remote_Ring", Game.Player.Character.Handle, true);
                nativeRingtonePlaying = true;
            }
            catch
            {
                nativeRingtonePlaying = false;
            }
        }

        private void StopNativeRingtone()
        {
            if (!nativeRingtonePlaying)
                return;

            try
            {
                Function.Call((Hash)0x6C5AE23EFA885092, Game.Player.Character.Handle);
            }
            catch
            {
            }

            nativeRingtonePlaying = false;
        }

        private void PlayMissionAudio(string file)
        {
            try
            {
                string path = Path.Combine(GetScriptsFolder(), "DetectiveAudio", file);

                if (!File.Exists(path))
                    return;

                if (activeCallPlayer != null)
                    activeCallPlayer.Stop();

                activeCallPlayer = new SoundPlayer(path);
                activeCallPlayer.Play();
            }
            catch
            {
            }
        }

        private string GetScriptsFolder()
        {
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;

            if (Path.GetFileName(baseDir.TrimEnd(Path.DirectorySeparatorChar)).Equals("scripts", StringComparison.OrdinalIgnoreCase))
                return baseDir;

            return Path.Combine(baseDir, "scripts");
        }

        private void ClearBackgroundBehaviors()
        {
            for (int i = 0; i < backgroundBehaviors.Count; i++)
                backgroundBehaviors[i].Clear();

            backgroundBehaviors.Clear();
        }

        internal void FailMission(string reason)
        {
            missionFailed = true;
            missionFailureReason = reason;
            activeMission = null;
            currentNode = null;
            currentNodeId = "";

            ResetNodeTimers();
            ClearNodeBlip();
            ClearActiveMissionBlip();
            ClearBackgroundBehaviors();

            GTA.UI.Screen.ShowSubtitle("Misja nieudana.", 4000);
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

        private Vector3 ToVector3(JsonVector3 pos)
        {
            if (pos == null)
                return Game.Player.Character.Position;

            return new Vector3(pos.x, pos.y, pos.z);
        }
    }
}
