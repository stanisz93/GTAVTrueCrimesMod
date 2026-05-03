using GTA;
using GTA.Math;
using GTA.Native;
using GTAVTrueCrimesMod.Behaviors;
using GTAVTrueCrimesMod.Effects;
using GTAVTrueCrimesMod.Models;
using GTAVTrueCrimesMod.NodeHandlers;
using GTAVTrueCrimesMod.Systems;
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
        private readonly List<BackgroundBehaviorRegistration> backgroundBehaviors = new List<BackgroundBehaviorRegistration>();
        private readonly List<Entity> missionCleanupEntities = new List<Entity>();
        private readonly List<DelayedIncomingMissionCall> delayedIncomingMissionCalls = new List<DelayedIncomingMissionCall>();
        private readonly List<DelayedSideMissionCall> delayedSideMissionCalls = new List<DelayedSideMissionCall>();
        private readonly MissionPhoneCallController phoneCall = new MissionPhoneCallController();
        private readonly MissionAudioSequenceController interactionAudio = new MissionAudioSequenceController();
        private MissionEffectConfigLoader effectConfigLoader;
        private bool phoneCallCompletesCurrentNode;
        private bool nativeRingtonePlaying;
        private bool playerPhoneAnimationActive;
        private PhonePropAnimation playerPhoneAnimation;
        private SoundPlayer activeCallPlayer;
        private MemoryStream activeCallAudioStream;
        private bool interactionCompletionPending;
        private bool interactionPickupPending;
        private int interactionPickupCompleteAt;
        private int interactionCompleteAt;
        private string currentInteractionPrompt = "";
        private string currentNodeInstructionText = "";

        private bool missionFailed = false;
        private string missionFailureReason = "";

        public MissionRuntime()
        {
            nodeHandlers.Add(new PhoneCallNodeHandler());
            nodeHandlers.Add(new DefaultNodeHandler());
            effectHandlers.Add(new SetFactEffectHandler());
            effectHandlers.Add(new PhoneCallEffectHandler());
            effectHandlers.Add(new SpawnStalkerEffectHandler());
            effectHandlers.Add(new ScriptedStalkerShotEffectHandler());
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
            get { return phoneCall.IsRinging; }
        }

        public string CurrentInteractionPrompt
        {
            get { return currentInteractionPrompt; }
        }

        public string CurrentNodeInstructionText
        {
            get { return currentNodeInstructionText; }
        }

        public void StartMission(DetectiveMission mission)
        {
            activeMission = mission;
            retryMission = mission;
            effectConfigLoader = CreateEffectConfigLoader(mission);
            missionFailed = false;
            missionFailureReason = "";
            facts.Clear();
            nodeHistory.Clear();
            currentNodeId = "";
            currentNode = null;
            ResetNodeTimers();
            ClearBackgroundBehaviors();
            ClearMissionCleanupEntities();

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

            if (!string.IsNullOrEmpty(currentNodeId) && currentNodeId != nodeId)
                ClearBackgroundBehaviorsForNode(currentNodeId);

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

            currentNodeInstructionText = text;
        }

        public void TickCurrentNode()
        {
            TickDelayedNodeActions();
            currentInteractionPrompt = "";

            if (currentNode == null)
                return;

            if (interactionCompletionPending)
                return;

            TickNodeInstructionClear();

            if (currentNode.completeWhen == "interactWithPreservedBody")
            {
                TickPreservedBodyInteraction();
                return;
            }

            if (currentNode.completeWhen != "playerNearTarget")
                return;

            if (currentNode.target == null)
                return;

            float distance = Game.Player.Character.Position.DistanceTo(ToVector3(currentNode.target));

            if (distance <= 3.0f)
                CompleteCurrentNode();
        }

        public bool TryInteractWithCurrentNode()
        {
            if (currentNode == null || missionFailed)
                return false;

            if (currentNode.completeWhen != "interactWithPreservedBody")
                return false;

            return TryInteractWithPreservedBody();
        }

        private void TickNodeInstructionClear()
        {
            if (currentNode == null || currentNode.target == null)
                return;

            if (string.IsNullOrEmpty(currentNodeInstructionText))
                return;

            if (currentNode.instructionClearDistance <= 0f)
                return;

            float distance = Game.Player.Character.Position.DistanceTo(ToVector3(currentNode.target));

            if (distance <= currentNode.instructionClearDistance)
                currentNodeInstructionText = "";
        }

        public void UpdateBackgroundBehaviors()
        {
            if (missionFailed)
                return;

            for (int i = 0; i < backgroundBehaviors.Count; i++)
                backgroundBehaviors[i].Behavior.Tick(this);
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

            ClearBackgroundBehaviorsForNode(currentNodeId);
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

            return backgroundBehaviors[index].Behavior.DebugText;
        }

        internal void AddBackgroundBehavior(IMissionBackgroundBehavior behavior)
        {
            AddBackgroundBehavior(behavior, "mission");
        }

        internal void AddBackgroundBehavior(IMissionBackgroundBehavior behavior, string lifetime)
        {
            if (behavior == null)
                return;

            for (int i = backgroundBehaviors.Count - 1; i >= 0; i--)
            {
                if (backgroundBehaviors[i].Behavior.Id == behavior.Id)
                {
                    backgroundBehaviors[i].Behavior.Clear();
                    backgroundBehaviors.RemoveAt(i);
                }
            }

            BackgroundBehaviorRegistration registration = new BackgroundBehaviorRegistration();
            registration.Behavior = behavior;
            registration.Lifetime = string.IsNullOrEmpty(lifetime) ? "mission" : lifetime.ToLowerInvariant();
            registration.OwnerNodeId = registration.Lifetime == "node" ? currentNodeId : "";

            backgroundBehaviors.Add(registration);
        }

        internal bool IsPlayerNearCurrentNodeTarget(float distance)
        {
            if (currentNode == null || currentNode.target == null)
                return false;

            float radius = Math.Max(0.1f, distance);

            return Game.Player.Character.Position.DistanceTo(ToVector3(currentNode.target)) <= radius;
        }

        private void TickPreservedBodyInteraction()
        {
            Entity body = FindNearestPreservedDeadPed(GetInteractionDistance(currentNode));

            if (body == null)
                return;

            currentInteractionPrompt = "E - " + GetInteractionText(currentNode);
        }

        private bool TryInteractWithPreservedBody()
        {
            Entity body = FindNearestPreservedDeadPed(GetInteractionDistance(currentNode));

            if (body == null)
                return false;

            if (interactionCompletionPending)
                return true;

            interactionCompletionPending = true;
            interactionPickupPending = true;
            interactionPickupCompleteAt = Game.GameTime + GetInteractionAnimationDuration(currentNode);
            interactionCompleteAt = 0;
            currentInteractionPrompt = "";
            PlayInteractionPickupAnimation(currentNode);
            return true;
        }

        private void StartInteractionContent()
        {
            if (currentNode == null)
                return;

            string resultText = currentNode.interactionResultText;
            bool hasAudio = HasInteractionAudioSegments(currentNode);

            if (!string.IsNullOrEmpty(resultText))
            {
                int durationMs = hasAudio
                    ? Math.Max(1000, GetInteractionAudioStartDelay(currentNode))
                    : GetInteractionCompleteDelay(currentNode);

                GTA.UI.Screen.ShowSubtitle(resultText, durationMs);
            }

            if (hasAudio)
            {
                interactionCompleteAt = 0;
                interactionAudio.Start(
                    currentNode.interactionAudioSegments,
                    Game.GameTime,
                    GetInteractionAudioStartDelay(currentNode)
                );
                currentInteractionPrompt = "";
                return;
            }

            if (!string.IsNullOrEmpty(resultText))
            {
                int delayMs = GetInteractionCompleteDelay(currentNode);
                interactionCompleteAt = Game.GameTime + delayMs;
                currentInteractionPrompt = "";
                return;
            }

            CompleteCurrentNode();
        }

        private Entity FindNearestPreservedDeadPed(float distance)
        {
            Ped player = Game.Player.Character;
            Entity nearest = null;
            float bestDistance = Math.Max(0.1f, distance);

            for (int i = missionCleanupEntities.Count - 1; i >= 0; i--)
            {
                Entity entity = missionCleanupEntities[i];

                if (entity == null || !entity.Exists())
                {
                    missionCleanupEntities.RemoveAt(i);
                    continue;
                }

                Ped ped = entity as Ped;

                if (ped == null)
                    continue;

                if (!ped.IsDead && ped.Health > 0)
                    continue;

                float currentDistance = ped.Position.DistanceTo(player.Position);

                if (currentDistance > bestDistance)
                    continue;

                bestDistance = currentDistance;
                nearest = ped;
            }

            return nearest;
        }

        private string GetInteractionText(MissionNode node)
        {
            if (node != null && !string.IsNullOrEmpty(node.interactionText))
                return node.interactionText;

            return "Przeszukaj cialo";
        }

        private float GetInteractionDistance(MissionNode node)
        {
            if (node != null && node.interactionDistance > 0f)
                return node.interactionDistance;

            return 2.5f;
        }

        private int GetInteractionCompleteDelay(MissionNode node)
        {
            if (node != null && node.interactionCompleteDelayMs > 0)
                return node.interactionCompleteDelayMs;

            return 4500;
        }

        private int GetInteractionAudioStartDelay(MissionNode node)
        {
            if (node != null && node.interactionAudioStartDelayMs > 0)
                return node.interactionAudioStartDelayMs;

            if (node != null && !string.IsNullOrEmpty(node.interactionResultText))
                return 1800;

            return 0;
        }

        private int GetInteractionAnimationDuration(MissionNode node)
        {
            if (node != null && node.interactionAnimationDurationMs > 0)
                return node.interactionAnimationDurationMs;

            return 1800;
        }

        private string GetInteractionAnimationDict(MissionNode node)
        {
            if (node != null && !string.IsNullOrEmpty(node.interactionAnimationDict))
                return node.interactionAnimationDict;

            return "pickup_object";
        }

        private string GetInteractionAnimationName(MissionNode node)
        {
            if (node != null && !string.IsNullOrEmpty(node.interactionAnimationName))
                return node.interactionAnimationName;

            return "pickup_low";
        }

        private bool HasInteractionAudioSegments(MissionNode node)
        {
            return node != null &&
                node.interactionAudioSegments != null &&
                node.interactionAudioSegments.Length > 0;
        }

        private void PlayInteractionPickupAnimation(MissionNode node)
        {
            try
            {
                Ped player = Game.Player.Character;

                if (player == null || !player.Exists())
                    return;

                string dict = GetInteractionAnimationDict(node);
                string animation = GetInteractionAnimationName(node);
                int durationMs = GetInteractionAnimationDuration(node);

                RequestAnimationDictionary(dict, 500);

                if (!Function.Call<bool>(Hash.HAS_ANIM_DICT_LOADED, dict))
                    return;

                Function.Call(
                    Hash.TASK_PLAY_ANIM,
                    player.Handle,
                    dict,
                    animation,
                    4.0f,
                    -4.0f,
                    durationMs,
                    0,
                    0.0f,
                    false,
                    false,
                    false
                );
            }
            catch
            {
            }
        }

        private void RequestAnimationDictionary(string dict, int timeoutMs)
        {
            if (string.IsNullOrEmpty(dict))
                return;

            try
            {
                if (Function.Call<bool>(Hash.HAS_ANIM_DICT_LOADED, dict))
                    return;

                Function.Call(Hash.REQUEST_ANIM_DICT, dict);

                int endAt = Game.GameTime + Math.Max(0, timeoutMs);

                while (!Function.Call<bool>(Hash.HAS_ANIM_DICT_LOADED, dict) && Game.GameTime < endAt)
                    Script.Yield();
            }
            catch
            {
            }
        }

        internal bool TryBeginScriptedKillByOther(string behaviorId, int shotCount, int shotGapMs, int damage)
        {
            if (string.IsNullOrEmpty(behaviorId))
                return false;

            for (int i = 0; i < backgroundBehaviors.Count; i++)
            {
                IMissionBackgroundBehavior behavior = backgroundBehaviors[i].Behavior;

                if (behavior == null || behavior.Id != behaviorId)
                    continue;

                IScriptedMissionKillTarget target = behavior as IScriptedMissionKillTarget;

                if (target == null)
                    return false;

                return target.BeginScriptedKillByOther(shotCount, shotGapMs, damage);
            }

            return false;
        }

        internal bool IsScriptedKillTargetNearCurrentNodeTarget(string behaviorId, float distance)
        {
            if (currentNode == null || currentNode.target == null || string.IsNullOrEmpty(behaviorId))
                return false;

            Vector3 targetPosition = ToVector3(currentNode.target);
            float radius = Math.Max(0.1f, distance);

            for (int i = 0; i < backgroundBehaviors.Count; i++)
            {
                IMissionBackgroundBehavior behavior = backgroundBehaviors[i].Behavior;

                if (behavior == null || behavior.Id != behaviorId)
                    continue;

                IScriptedMissionKillTarget target = behavior as IScriptedMissionKillTarget;

                if (target == null)
                    return false;

                return target.IsNearPosition(targetPosition, radius);
            }

            return false;
        }

        internal void PreserveEntityForMissionCleanup(Entity entity)
        {
            if (entity == null || !entity.Exists())
                return;

            for (int i = 0; i < missionCleanupEntities.Count; i++)
            {
                if (missionCleanupEntities[i] != null &&
                    missionCleanupEntities[i].Exists() &&
                    missionCleanupEntities[i].Handle == entity.Handle)
                {
                    return;
                }
            }

            missionCleanupEntities.Add(entity);
        }

        private void ApplyOnEnterEffects(MissionNode node)
        {
            if (node == null || node.onEnter == null)
                return;

            RunEffects(node.onEnter);
        }

        internal void RunEffects(MissionEffect[] effects)
        {
            if (effects == null)
                return;

            for (int i = 0; i < effects.Length; i++)
            {
                MissionEffect effect = ResolveMissionEffect(effects[i]);

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

        private MissionEffectConfigLoader CreateEffectConfigLoader(DetectiveMission mission)
        {
            if (mission == null || string.IsNullOrEmpty(mission.sourceFile))
                return new MissionEffectConfigLoader("");

            return new MissionEffectConfigLoader(Path.GetDirectoryName(mission.sourceFile));
        }

        private MissionEffect ResolveMissionEffect(MissionEffect effect)
        {
            if (effectConfigLoader == null)
                return effect;

            return effectConfigLoader.Resolve(effect);
        }

        internal void StartIncomingMissionCall(MissionNode node)
        {
            if (node != null && node.delayMs > 0)
            {
                ScheduleIncomingMissionCall(node, node.delayMs);
                return;
            }

            phoneCallCompletesCurrentNode = true;
            ApplyPhoneCallEvents(phoneCall.StartRinging(node, Game.GameTime));
        }

        private void ScheduleIncomingMissionCall(MissionNode node, int delayMs)
        {
            if (node == null)
                return;

            DelayedIncomingMissionCall delayedCall = new DelayedIncomingMissionCall();
            delayedCall.Node = node;
            delayedCall.StartAt = Game.GameTime + Math.Max(0, delayMs);
            delayedIncomingMissionCalls.Add(delayedCall);
        }

        internal void StartSideMissionCall(MissionEffect effect)
        {
            if (effect == null)
                return;

            MissionNode node = new MissionNode();
            node.type = "phone_call";
            node.caller = effect.GetString("caller", "Nieznany numer");
            node.text = effect.GetString("text", "");
            node.audio = effect.GetString("audio", "");
            node.subtitlesFile = effect.GetString("subtitlesFile", "");
            node.subtitles = effect.subtitles;
            node.audioSegments = effect.audioSegments;
            node.completeAfterMs = effect.GetInt("completeAfterMs", 0);

            phoneCallCompletesCurrentNode = effect.GetBool("completeCurrentNode", false);
            ApplyPhoneCallEvents(phoneCall.StartRinging(node, Game.GameTime));
        }

        internal void ScheduleSideMissionCall(MissionEffect effect, int delayMs)
        {
            if (effect == null)
                return;

            DelayedSideMissionCall delayedCall = new DelayedSideMissionCall();
            delayedCall.Effect = effect;
            delayedCall.StartAt = Game.GameTime + Math.Max(0, delayMs);
            delayedSideMissionCalls.Add(delayedCall);
        }

        internal void SetFact(string fact, bool value)
        {
            if (string.IsNullOrEmpty(fact))
                return;

            facts[fact] = value;
        }

        public bool TryAnswerPhoneCall()
        {
            if (!phoneCall.IsRinging)
                return false;

            ApplyPhoneCallEvents(phoneCall.Answer(Game.GameTime));
            return true;
        }

        private void TickDelayedNodeActions()
        {
            TickDelayedIncomingMissionCalls();
            TickDelayedSideMissionCalls();
            ApplyPhoneCallEvents(phoneCall.Tick(Game.GameTime));
            ApplyInteractionAudioEvents(interactionAudio.Tick(Game.GameTime));
            TickPlayerPhoneAnimation();

            if (interactionPickupPending && Game.GameTime >= interactionPickupCompleteAt)
            {
                interactionPickupPending = false;
                interactionPickupCompleteAt = 0;
                StartInteractionContent();
            }

            if (interactionCompletionPending && interactionCompleteAt > 0 && Game.GameTime >= interactionCompleteAt)
                CompleteCurrentNode();
        }

        private void ResetNodeTimers()
        {
            StopNativeRingtone();
            StopPlayerPhoneAnimation();
            StopMissionAudio();
            phoneCall.Reset();
            interactionAudio.Reset();
            delayedIncomingMissionCalls.Clear();
            delayedSideMissionCalls.Clear();
            phoneCallCompletesCurrentNode = false;
            interactionCompletionPending = false;
            interactionPickupPending = false;
            interactionPickupCompleteAt = 0;
            interactionCompleteAt = 0;
            currentInteractionPrompt = "";
            currentNodeInstructionText = "";
        }

        private void ApplyPhoneCallEvents(List<PhoneCallEvent> events)
        {
            for (int i = 0; i < events.Count; i++)
            {
                PhoneCallEvent phoneEvent = events[i];

                if (phoneEvent.type == PhoneCallEvent.ShowPrompt ||
                    phoneEvent.type == PhoneCallEvent.ShowAnswered ||
                    phoneEvent.type == PhoneCallEvent.ShowSubtitle)
                {
                    GTA.UI.Screen.ShowSubtitle(phoneEvent.text, phoneEvent.durationMs);
                    continue;
                }

                if (phoneEvent.type == PhoneCallEvent.PlayRingtone)
                {
                    PlayNativeRingtone();
                    continue;
                }

                if (phoneEvent.type == PhoneCallEvent.StopRingtone)
                {
                    StopNativeRingtone();
                    continue;
                }

                if (phoneEvent.type == PhoneCallEvent.PlayAudio)
                {
                    PlayMissionAudio(phoneEvent.audio);
                    continue;
                }

                if (phoneEvent.type == PhoneCallEvent.BeginCallAnimation)
                {
                    StartPlayerPhoneAnimation(phoneEvent.durationMs);
                    continue;
                }

                if (phoneEvent.type == PhoneCallEvent.StartCallHoldAnimation)
                {
                    StartPlayerPhoneHoldAnimation(phoneEvent.durationMs);
                    continue;
                }

                if (phoneEvent.type == PhoneCallEvent.EndCallAnimation)
                {
                    FinishPlayerPhoneAnimation(phoneEvent.durationMs);
                    continue;
                }

                if (phoneEvent.type == PhoneCallEvent.Complete)
                {
                    if (phoneCallCompletesCurrentNode)
                        CompleteCurrentNode();
                    else
                        phoneCall.Reset();

                    phoneCallCompletesCurrentNode = false;
                }
            }
        }

        private void TickDelayedSideMissionCalls()
        {
            for (int i = delayedSideMissionCalls.Count - 1; i >= 0; i--)
            {
                DelayedSideMissionCall delayedCall = delayedSideMissionCalls[i];

                if (Game.GameTime < delayedCall.StartAt)
                    continue;

                delayedSideMissionCalls.RemoveAt(i);
                StartSideMissionCall(delayedCall.Effect);
            }
        }

        private void TickDelayedIncomingMissionCalls()
        {
            for (int i = delayedIncomingMissionCalls.Count - 1; i >= 0; i--)
            {
                DelayedIncomingMissionCall delayedCall = delayedIncomingMissionCalls[i];

                if (Game.GameTime < delayedCall.StartAt)
                    continue;

                delayedIncomingMissionCalls.RemoveAt(i);
                phoneCallCompletesCurrentNode = true;
                ApplyPhoneCallEvents(phoneCall.StartRinging(delayedCall.Node, Game.GameTime));
            }
        }

        private void ApplyInteractionAudioEvents(List<PhoneCallEvent> events)
        {
            for (int i = 0; i < events.Count; i++)
            {
                PhoneCallEvent audioEvent = events[i];

                if (audioEvent.type == PhoneCallEvent.ShowSubtitle)
                {
                    GTA.UI.Screen.ShowSubtitle(audioEvent.text, audioEvent.durationMs);
                    continue;
                }

                if (audioEvent.type == PhoneCallEvent.PlayAudio)
                {
                    PlayMissionAudio(audioEvent.audio);
                    continue;
                }

                if (audioEvent.type == PhoneCallEvent.Complete)
                {
                    CompleteCurrentNode();
                    return;
                }
            }
        }

        private void StartPlayerPhoneAnimation(int durationMs)
        {
            try
            {
                int duration = Math.Max(1000, durationMs);
                EnsurePlayerPhoneAnimation().BeginPickup(duration);
                playerPhoneAnimationActive = true;
            }
            catch
            {
                StopPlayerPhoneAnimation();
                playerPhoneAnimationActive = false;
            }
        }

        private void StartPlayerPhoneHoldAnimation(int durationMs)
        {
            try
            {
                int duration = Math.Max(1000, durationMs);
                EnsurePlayerPhoneAnimation().StartHold(duration);
                playerPhoneAnimationActive = true;
            }
            catch
            {
                StopPlayerPhoneAnimation();
                playerPhoneAnimationActive = false;
            }
        }

        private void FinishPlayerPhoneAnimation(int durationMs)
        {
            if (playerPhoneAnimation != null)
                playerPhoneAnimation.Finish(Game.GameTime, durationMs);

            playerPhoneAnimationActive = playerPhoneAnimation != null && playerPhoneAnimation.Active;
        }

        private void StopPlayerPhoneAnimation()
        {
            if (!playerPhoneAnimationActive && playerPhoneAnimation == null)
                return;

            if (playerPhoneAnimation != null)
                playerPhoneAnimation.Stop();

            playerPhoneAnimation = null;
            playerPhoneAnimationActive = false;
        }

        private PhonePropAnimation EnsurePlayerPhoneAnimation()
        {
            if (playerPhoneAnimation == null)
                playerPhoneAnimation = new PhonePropAnimation(Game.Player.Character);

            return playerPhoneAnimation;
        }

        private void TickPlayerPhoneAnimation()
        {
            if (playerPhoneAnimation == null)
                return;

            playerPhoneAnimation.Tick(Game.GameTime);
            playerPhoneAnimationActive = playerPhoneAnimation.Active;

            if (!playerPhoneAnimationActive)
                playerPhoneAnimation = null;
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
                {
                    GTA.UI.Screen.ShowSubtitle("Brak audio: " + file, 3500);
                    return;
                }

                StopMissionAudio();

                byte[] audioBytes = File.ReadAllBytes(path);
                activeCallAudioStream = new MemoryStream(audioBytes);
                activeCallPlayer = new SoundPlayer(activeCallAudioStream);
                activeCallPlayer.Load();
                activeCallPlayer.Play();
            }
            catch (Exception ex)
            {
                GTA.UI.Screen.ShowSubtitle("Blad audio: " + ex.Message, 3500);
            }
        }

        private void StopMissionAudio()
        {
            try
            {
                if (activeCallPlayer != null)
                {
                    activeCallPlayer.Stop();
                    activeCallPlayer.Dispose();
                    activeCallPlayer = null;
                }

                if (activeCallAudioStream != null)
                {
                    activeCallAudioStream.Dispose();
                    activeCallAudioStream = null;
                }
            }
            catch
            {
                activeCallPlayer = null;
                activeCallAudioStream = null;
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
                backgroundBehaviors[i].Behavior.Clear();

            backgroundBehaviors.Clear();
        }

        private void ClearBackgroundBehaviorsForNode(string nodeId)
        {
            if (string.IsNullOrEmpty(nodeId))
                return;

            for (int i = backgroundBehaviors.Count - 1; i >= 0; i--)
            {
                if (backgroundBehaviors[i].Lifetime != "node")
                    continue;

                if (backgroundBehaviors[i].OwnerNodeId != nodeId)
                    continue;

                INodeTransitionBackgroundBehavior transitionBehavior =
                    backgroundBehaviors[i].Behavior as INodeTransitionBackgroundBehavior;

                if (transitionBehavior != null)
                    transitionBehavior.ClearForNodeTransition(this);
                else
                    backgroundBehaviors[i].Behavior.Clear();

                backgroundBehaviors.RemoveAt(i);
            }
        }

        private void ClearMissionCleanupEntities()
        {
            for (int i = missionCleanupEntities.Count - 1; i >= 0; i--)
            {
                Entity entity = missionCleanupEntities[i];

                try
                {
                    if (entity != null && entity.Exists())
                        entity.Delete();
                }
                catch
                {
                }
            }

            missionCleanupEntities.Clear();
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
            ClearMissionCleanupEntities();

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

        private class BackgroundBehaviorRegistration
        {
            public IMissionBackgroundBehavior Behavior;
            public string Lifetime;
            public string OwnerNodeId;
        }

        private class DelayedSideMissionCall
        {
            public MissionEffect Effect;
            public int StartAt;
        }

        private class DelayedIncomingMissionCall
        {
            public MissionNode Node;
            public int StartAt;
        }
    }
}
