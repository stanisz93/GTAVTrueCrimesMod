using GTA;
using GTA.Math;
using GTA.Native;
using GTAVTrueCrimesMod.Models;
using GTAVTrueCrimesMod.Systems;
using System;

namespace GTAVTrueCrimesMod.Behaviors
{
    public class PoliceAmbushBehavior : IMissionBackgroundBehavior, INodeTransitionBackgroundBehavior, IInteractiveBackgroundBehavior
    {
        private const int ExitRepathIntervalMs = 3500;
        private const float ExitSideOffset = 0.7f;

        private readonly string id;
        private readonly Vector3 spawnPolicemansPosition;
        private readonly float spawnPolicemansRadius;
        private readonly string shouterModelName;
        private readonly string shooterModelName;
        private readonly float sightDistance;
        private readonly float sightAngle;
        private readonly int spotTimeMs;
        private readonly int loseSightResetMs;
        private readonly bool requireLineOfSight;
        private readonly WeaponHash shooterWeapon;
        private readonly int shooterAccuracy;
        private readonly int shooterShootRate;
        private readonly int health;
        private readonly int armor;
        private readonly bool failOnPlayerDeath;
        private readonly bool suppressWantedLevel;
        private readonly bool useScenarioRelationshipGroup;
        private readonly Vector3 listenSpotPosition;
        private readonly bool hasListenSpot;
        private readonly float listenSpotRadius;
        private readonly bool requireListenSpotForDialogue;
        private readonly bool listenRequireCover;
        private readonly string listenPrompt;
        private readonly Vector3 exitAfterDialoguePosition;
        private readonly bool hasExitAfterDialoguePosition;
        private readonly float exitAfterDialogueDistance;
        private readonly int exitAfterDialogueTimeoutMs;
        private readonly MissionAudioSegment[] ambientDialogueSegments;
        private readonly int ambientDialogueDelayMs;
        private readonly int ambientDialogueLoopDelayMs;
        private readonly MissionAudioSegment[] dialogueSegments;
        private readonly int dialogueDelayMs;
        private readonly bool loopDialogue;
        private readonly int loopDialogueDelayMs;
        private readonly MissionAudioSegment[] onPlayerSpottedAudioSegments;
        private readonly MissionAudioSequenceController ambientDialogue = new MissionAudioSequenceController();
        private readonly MissionAudioSequenceController dialogue = new MissionAudioSequenceController();
        private readonly MissionAudioSequenceController spottedAudio = new MissionAudioSequenceController();
        private readonly Random rng = new Random();

        private Ped shouter;
        private Ped shooter;
        private bool spawned;
        private bool triggered;
        private bool exitingAfterDialogue;
        private bool ambientDialogueStarted;
        private bool dialogueStarted;
        private bool spottedAudioStarted;
        private bool playerListening;
        private bool wantedSuppressionActive;
        private int previousMaxWantedLevel = 5;
        private int spottedSince;
        private int lastSeenAt;
        private int nextConversationPoseAt;
        private int nextShooterWatchdogAt;
        private int exitStartedAt;
        private int nextExitTaskAt;
        private bool shooterAttackStarted;
        private RelationshipGroup scenarioRelationshipGroup;
        private bool scenarioRelationshipConfigured;
        private int lastConversationGestureIndex = -1;
        private int lastIdleGestureIndex = -1;
        private string state = "not_spawned";
        private float lastPlayerDistance;

        public PoliceAmbushBehavior(MissionEffect config)
        {
            string configuredId = config == null ? "" : config.GetString("id", "");
            id = string.IsNullOrEmpty(configuredId) ? "police_ambush" : configuredId;

            spawnPolicemansPosition = ReadVector(config, "spawnPolicemans", Vector3.Zero);
            spawnPolicemansRadius = PositiveOrDefault(config == null ? 0f : config.GetFloat("spawnPolicemansRadius", 3f), 3f);
            shouterModelName = config == null ? "" : config.GetString("shouterModel", "Cop01SMY");
            shooterModelName = config == null ? "" : config.GetString("shooterModel", "Cop01SMY");
            sightDistance = PositiveOrDefault(config == null ? 0f : config.GetFloat("sightDistance", 35f), 35f);
            sightAngle = PositiveOrDefault(config == null ? 0f : config.GetFloat("sightAngle", 70f), 70f);
            spotTimeMs = config == null ? 900 : Math.Max(0, config.GetInt("spotTimeMs", 900));
            loseSightResetMs = config == null ? 1200 : Math.Max(0, config.GetInt("loseSightResetMs", 1200));
            requireLineOfSight = config == null || config.GetBool("requireLineOfSight", true);
            shooterWeapon = ParseWeapon(config == null ? "" : config.GetString("weapon", ""), WeaponHash.AssaultShotgun);
            shooterAccuracy = config == null ? 95 : Clamp(config.GetInt("accuracy", 95), 0, 100);
            shooterShootRate = config == null ? 1000 : Math.Max(0, config.GetInt("shootRate", 1000));
            health = config == null ? 250 : Math.Max(100, config.GetInt("health", 250));
            armor = config == null ? 100 : Math.Max(0, config.GetInt("armor", 100));
            failOnPlayerDeath = config == null || config.GetBool("failOnPlayerDeath", true);
            suppressWantedLevel = config == null || config.GetBool("suppressWantedLevel", true);
            useScenarioRelationshipGroup = config == null || config.GetBool("useScenarioRelationshipGroup", true);
            listenSpotPosition = ReadVector(config, "listenSpot", Vector3.Zero);
            hasListenSpot = listenSpotPosition != Vector3.Zero;
            listenSpotRadius = PositiveOrDefault(config == null ? 0f : config.GetFloat("listenSpotRadius", 1.8f), 1.8f);
            requireListenSpotForDialogue = config != null && config.GetBool("requireListenSpotForDialogue", hasListenSpot);
            listenRequireCover = config != null && config.GetBool("listenRequireCover", false);
            listenPrompt = config == null ? "E - Przylgnij do sciany i podsluchuj" : config.GetString("listenPrompt", "E - Przylgnij do sciany i podsluchuj");
            exitAfterDialoguePosition = ReadVector(config, "exitAfterDialogue", Vector3.Zero);
            hasExitAfterDialoguePosition = exitAfterDialoguePosition != Vector3.Zero;
            exitAfterDialogueDistance = PositiveOrDefault(config == null ? 0f : config.GetFloat("exitAfterDialogueDistance", 2.0f), 2.0f);
            exitAfterDialogueTimeoutMs = config == null ? 15000 : Math.Max(1000, config.GetInt("exitAfterDialogueTimeoutMs", 15000));
            ambientDialogueSegments = config == null || config.ambientAudioSegments == null
                ? new MissionAudioSegment[0]
                : config.ambientAudioSegments;
            ambientDialogueDelayMs = config == null ? 1000 : Math.Max(0, config.GetInt("ambientConversationDelayMs", 1000));
            ambientDialogueLoopDelayMs = config == null ? 2000 : Math.Max(0, config.GetInt("ambientConversationLoopDelayMs", 2000));
            dialogueSegments = config == null ? new MissionAudioSegment[0] : config.audioSegments;
            dialogueDelayMs = config == null ? 1500 : Math.Max(0, config.GetInt("dialogueDelayMs", 1500));
            loopDialogue = config != null && config.GetBool("loopDialogue", false);
            loopDialogueDelayMs = config == null ? 2000 : Math.Max(0, config.GetInt("loopDialogueDelayMs", 2000));
            onPlayerSpottedAudioSegments = config == null || config.onPlayerSpottedAudioSegments == null
                ? new MissionAudioSegment[0]
                : config.onPlayerSpottedAudioSegments;
        }

        public string Id
        {
            get { return id; }
        }

        public string DebugText
        {
            get
            {
                return "police_ambush[" + id + "] state=" + state +
                    " triggered=" + triggered +
                    " listening=" + playerListening +
                    " dist=" + lastPlayerDistance.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + "m";
            }
        }

        public void Tick(MissionRuntime runtime)
        {
            if (!spawned)
                Spawn(runtime);

            if (!spawned)
                return;

            Ped player = Game.Player.Character;

            if (player == null || !player.Exists())
                return;

            SuppressWantedLevel();

            if (failOnPlayerDeath && (player.IsDead || player.Health <= 0))
            {
                runtime.FailMission("Zostales zastrzelony przez policje.");
                return;
            }

            lastPlayerDistance = GetClosestDistanceToPlayer(player);

            if (!triggered)
            {
                if (exitingAfterDialogue)
                {
                    TickExitAfterDialogue();
                    return;
                }

                MaintainConversationPose();

                if (TickPlayerSpotting(player))
                {
                    Trigger(runtime);
                    return;
                }

                if (requireListenSpotForDialogue && !playerListening)
                {
                    TickListenSpot(runtime, player);
                    TickPreListenConversation(runtime);
                    return;
                }

                TickDialogue(runtime);
                return;
            }

            TickShooterCombat(player);
            TickSpottedAudio(runtime);
        }

        public void Clear()
        {
            RestoreWantedLevel();
            DeleteCops();
            ambientDialogue.Reset();
            dialogue.Reset();
            spottedAudio.Reset();
            ReleasePlayerListening();
            spawned = false;
            triggered = false;
            exitingAfterDialogue = false;
            ambientDialogueStarted = false;
            dialogueStarted = false;
            spottedAudioStarted = false;
            playerListening = false;
            spottedSince = 0;
            lastSeenAt = 0;
            nextConversationPoseAt = 0;
            nextShooterWatchdogAt = 0;
            exitStartedAt = 0;
            nextExitTaskAt = 0;
            shooterAttackStarted = false;
            lastConversationGestureIndex = -1;
            lastIdleGestureIndex = -1;
            state = "cleared";
        }

        public bool TryInteract(MissionRuntime runtime)
        {
            if (!spawned || triggered || !requireListenSpotForDialogue || playerListening || !hasListenSpot)
                return false;

            Ped player = Game.Player.Character;

            if (player == null || !player.Exists())
                return false;

            if (GetHorizontalDistance(player.Position, listenSpotPosition) > listenSpotRadius)
                return false;

            if (listenRequireCover && !IsPlayerInCover(player))
            {
                GTA.UI.Screen.ShowSubtitle("Wejdz w oslone przy scianie, potem nacisnij E.", 3000);
                return false;
            }

            StopAmbientDialogue(runtime);
            StartPlayerListening(player);
            return true;
        }

        public void ClearForNodeTransition(MissionRuntime runtime)
        {
            StopDialogue(runtime);
            Clear();
        }

        private void Spawn(MissionRuntime runtime)
        {
            Ped player = Game.Player.Character;

            if (player == null || !player.Exists())
                return;

            Vector3 spawnCenter = spawnPolicemansPosition == Vector3.Zero
                ? player.Position + player.ForwardVector * 8f
                : spawnPolicemansPosition;
            Vector3 spreadDirection = GetSpreadDirection(spawnCenter, player.Position);
            float spread = Math.Min(spawnPolicemansRadius, Math.Max(1.0f, spawnPolicemansRadius * 0.65f));
            Vector3 shouterSpawn = spawnCenter + spreadDirection * spread;
            Vector3 shooterSpawn = spawnCenter - spreadDirection * spread;

            ConfigureScenarioRelationshipGroup(player);
            shouter = CreateCop(shouterModelName, shouterSpawn);
            shooter = CreateCop(shooterModelName, shooterSpawn);

            if (shouter == null || !shouter.Exists() || shooter == null || !shooter.Exists())
            {
                state = "spawn_failed";
                DeleteCops();
                return;
            }

            PreparePassiveCop(shouter);
            PreparePassiveCop(shooter);
            FaceEachOther();
            spawned = true;
            state = "spawned";
        }

        private Ped CreateCop(string modelName, Vector3 position)
        {
            Model model = new Model(ParsePedHash(modelName, PedHash.Cop01SMY));
            model.Request(1000);

            Ped ped = World.CreatePed(model, position);

            if (ped == null || !ped.Exists())
                return null;

            ped.IsPersistent = true;
            ped.BlockPermanentEvents = true;
            ped.KeepTaskWhenMarkedAsNoLongerNeeded = true;
            ped.Health = health;
            ped.Armor = armor;
            ApplyScenarioCopFlags(ped);
            return ped;
        }

        private void ConfigureScenarioRelationshipGroup(Ped player)
        {
            if (!useScenarioRelationshipGroup || scenarioRelationshipConfigured)
                return;

            try
            {
                scenarioRelationshipGroup = World.AddRelationshipGroup("TC_POLICE_AMBUSH");
                Function.Call(
                    Hash.SET_RELATIONSHIP_BETWEEN_GROUPS,
                    5,
                    scenarioRelationshipGroup.Hash,
                    player.RelationshipGroup.Hash
                );
                Function.Call(
                    Hash.SET_RELATIONSHIP_BETWEEN_GROUPS,
                    5,
                    player.RelationshipGroup.Hash,
                    scenarioRelationshipGroup.Hash
                );
                scenarioRelationshipConfigured = true;
            }
            catch
            {
                scenarioRelationshipConfigured = false;
            }
        }

        private void ApplyScenarioCopFlags(Ped ped)
        {
            if (ped == null || !ped.Exists())
                return;

            try
            {
                ped.SetConfigFlag(PedConfigFlagToggles.DontInfluenceWantedLevel, true);
            }
            catch
            {
            }

            if (!useScenarioRelationshipGroup || !scenarioRelationshipConfigured)
                return;

            try
            {
                ped.RelationshipGroup = scenarioRelationshipGroup;
            }
            catch
            {
            }
        }

        private void PreparePassiveCop(Ped ped)
        {
            if (ped == null || !ped.Exists())
                return;

            try
            {
                ped.Task.StandStill(-1);
            }
            catch
            {
            }
        }

        private void PrepareShooterForAttack(Ped ped)
        {
            if (ped == null || !ped.Exists())
                return;

            try
            {
                ped.BlockPermanentEvents = false;
                ped.Accuracy = shooterAccuracy;
                ped.ShootRate = shooterShootRate;
                ped.Weapons.Give(shooterWeapon, 999, true, true);
                Function.Call(Hash.SET_CURRENT_PED_WEAPON, ped.Handle, shooterWeapon, true);
                Function.Call(Hash.SET_PED_COMBAT_ABILITY, ped.Handle, 2);
                Function.Call(Hash.SET_PED_COMBAT_MOVEMENT, ped.Handle, 1);
                Function.Call(Hash.SET_PED_COMBAT_RANGE, ped.Handle, 2);
                Function.Call(Hash.SET_PED_FLEE_ATTRIBUTES, ped.Handle, 0, false);
                Function.Call(Hash.SET_PED_FLEE_ATTRIBUTES, ped.Handle, 512, true);
                Function.Call(Hash.SET_PED_COMBAT_ATTRIBUTES, ped.Handle, 5, true);
                Function.Call(Hash.SET_PED_COMBAT_ATTRIBUTES, ped.Handle, 46, true);
                Function.Call(Hash.SET_PED_COMBAT_ATTRIBUTES, ped.Handle, 58, true);
                Function.Call(Hash.SET_PED_KEEP_TASK, ped.Handle, true);
                Function.Call(Hash.SET_PED_TARGET_LOSS_RESPONSE, ped.Handle, 1);
            }
            catch
            {
            }
        }

        private bool CanSeePlayer(Ped ped, Ped player)
        {
            if (ped == null || !ped.Exists() || ped.IsDead)
                return false;

            float distance = ped.Position.DistanceTo(player.Position);

            if (distance > sightDistance)
                return false;

            if (!IsInsideVisionCone(ped, player))
                return false;

            if (!requireLineOfSight)
                return true;

            try
            {
                return Function.Call<bool>(
                    Hash.HAS_ENTITY_CLEAR_LOS_TO_ENTITY,
                    ped.Handle,
                    player.Handle,
                    17
                );
            }
            catch
            {
                return true;
            }
        }

        private bool TickPlayerSpotting(Ped player)
        {
            bool canSeePlayer = CanSeePlayer(shouter, player) || CanSeePlayer(shooter, player);

            if (canSeePlayer)
            {
                if (spottedSince <= 0)
                    spottedSince = Game.GameTime;

                lastSeenAt = Game.GameTime;
                state = "spotting_player";
                return Game.GameTime - spottedSince >= spotTimeMs;
            }

            if (lastSeenAt <= 0 || Game.GameTime - lastSeenAt >= loseSightResetMs)
            {
                spottedSince = 0;
                state = "waiting_for_player";
            }
            else
            {
                state = "losing_sight";
            }

            return false;
        }

        private bool IsInsideVisionCone(Ped ped, Ped player)
        {
            Vector3 toPlayer = player.Position - ped.Position;
            toPlayer = new Vector3(toPlayer.X, toPlayer.Y, 0f);

            if (toPlayer.Length() <= 0.01f)
                return true;

            toPlayer.Normalize();

            Vector3 forward = ped.ForwardVector;
            forward = new Vector3(forward.X, forward.Y, 0f);

            if (forward.Length() <= 0.01f)
                return true;

            forward.Normalize();

            float dot = forward.X * toPlayer.X + forward.Y * toPlayer.Y;
            dot = Math.Max(-1f, Math.Min(1f, dot));
            double angle = Math.Acos(dot) * (180.0 / Math.PI);

            return angle <= sightAngle * 0.5f;
        }

        private void Trigger(MissionRuntime runtime)
        {
            triggered = true;
            state = "ambush_triggered";
            StopAmbientDialogue(runtime);
            StopDialogue(runtime);
            ReleasePlayerListening();
            StartShouterAlert(Game.Player.Character);
            StartSpottedAudio();

            TickShooterCombat(Game.Player.Character);
        }

        private void StartSpottedAudio()
        {
            if (onPlayerSpottedAudioSegments == null || onPlayerSpottedAudioSegments.Length == 0)
                return;

            spottedAudio.Start(onPlayerSpottedAudioSegments, Game.GameTime, 0);
            spottedAudioStarted = true;
        }

        private void TickSpottedAudio(MissionRuntime runtime)
        {
            if (!spottedAudioStarted)
                return;

            ApplyAudioEvents(runtime, spottedAudio.Tick(Game.GameTime), false);

            if (!spottedAudio.IsActive)
                spottedAudioStarted = false;
        }

        private void TickDialogue(MissionRuntime runtime)
        {
            if (dialogueSegments == null || dialogueSegments.Length == 0)
            {
                state = "waiting_for_player";
                return;
            }

            if (!dialogueStarted)
            {
                dialogue.Start(dialogueSegments, Game.GameTime, dialogueDelayMs);
                dialogueStarted = true;
                state = "dialogue_waiting";
            }

            ApplyAudioEvents(runtime, dialogue.Tick(Game.GameTime), true);

            if (dialogue.IsActive)
                state = "dialogue";
        }

        private void TickPreListenConversation(MissionRuntime runtime)
        {
            if (ambientDialogueSegments == null || ambientDialogueSegments.Length == 0)
                return;

            if (!ambientDialogueStarted)
            {
                ambientDialogue.Start(ambientDialogueSegments, Game.GameTime, ambientDialogueDelayMs);
                ambientDialogueStarted = true;
                state = "ambient_dialogue_waiting";
            }

            ApplyAmbientAudioEvents(runtime, ambientDialogue.Tick(Game.GameTime));

            if (ambientDialogue.IsActive)
                state = "ambient_dialogue";
        }

        private void ApplyAmbientAudioEvents(MissionRuntime runtime, System.Collections.Generic.List<PhoneCallEvent> events)
        {
            for (int i = 0; i < events.Count; i++)
            {
                PhoneCallEvent dialogueEvent = events[i];

                if (dialogueEvent.type == PhoneCallEvent.PlayAudio)
                {
                    SetIdleConversationSpeakerPose(dialogueEvent.speaker);
                    runtime.PlayMissionAudioCue(dialogueEvent.audio);
                    continue;
                }

                if (dialogueEvent.type == PhoneCallEvent.ShowSubtitle)
                {
                    runtime.ShowMissionSubtitle(dialogueEvent.text, dialogueEvent.durationMs);
                    continue;
                }

                if (dialogueEvent.type == PhoneCallEvent.Complete && !playerListening && !triggered && !exitingAfterDialogue)
                {
                    ambientDialogue.Start(ambientDialogueSegments, Game.GameTime, ambientDialogueLoopDelayMs);
                    state = "ambient_dialogue_loop_waiting";
                }
            }
        }

        private void ApplyAudioEvents(MissionRuntime runtime, System.Collections.Generic.List<PhoneCallEvent> events, bool canLoopDialogue)
        {
            for (int i = 0; i < events.Count; i++)
            {
                PhoneCallEvent dialogueEvent = events[i];

                if (dialogueEvent.type == PhoneCallEvent.PlayAudio)
                {
                    if (canLoopDialogue)
                        SetConversationSpeakerPose(dialogueEvent.speaker);

                    runtime.PlayMissionAudioCue(dialogueEvent.audio);
                    continue;
                }

                if (dialogueEvent.type == PhoneCallEvent.ShowSubtitle)
                {
                    runtime.ShowMissionSubtitle(dialogueEvent.text, dialogueEvent.durationMs);
                    continue;
                }

                if (dialogueEvent.type == PhoneCallEvent.Complete)
                {
                    if (canLoopDialogue && loopDialogue)
                    {
                        dialogue.Start(dialogueSegments, Game.GameTime, loopDialogueDelayMs);
                        state = "dialogue_loop_waiting";
                    }
                    else if (canLoopDialogue)
                    {
                        ReleasePlayerListening();
                        StartExitAfterDialogue();
                    }
                }
            }
        }

        private void StopDialogue(MissionRuntime runtime)
        {
            dialogue.Reset();
            dialogueStarted = false;

            if (runtime != null)
                runtime.StopMissionAudioCue();
        }

        private void StopAmbientDialogue(MissionRuntime runtime)
        {
            ambientDialogue.Reset();
            ambientDialogueStarted = false;

            if (runtime != null)
                runtime.StopMissionAudioCue();
        }

        private void StartExitAfterDialogue()
        {
            state = "dialogue_done";

            if (!hasExitAfterDialoguePosition)
                return;

            exitingAfterDialogue = true;
            exitStartedAt = Game.GameTime;
            nextExitTaskAt = 0;
            state = "exiting_after_dialogue";
            SendCopsToExit();
        }

        private void TickExitAfterDialogue()
        {
            if (!exitingAfterDialogue)
                return;

            bool shouterGone = DeleteCopIfReachedExit(ref shouter);
            bool shooterGone = DeleteCopIfReachedExit(ref shooter);

            if (shouterGone && shooterGone)
            {
                exitingAfterDialogue = false;
                state = "exited_after_dialogue";
                return;
            }

            if (Game.GameTime - exitStartedAt >= exitAfterDialogueTimeoutMs)
            {
                DeleteCops();
                exitingAfterDialogue = false;
                state = "exited_after_timeout";
                return;
            }

            if (Game.GameTime >= nextExitTaskAt)
                SendCopsToExit();

            state = "exiting_after_dialogue";
        }

        private void SendCopsToExit()
        {
            nextExitTaskAt = Game.GameTime + ExitRepathIntervalMs;

            SendCopToExit(shouter, new Vector3(ExitSideOffset, 0.0f, 0.0f));
            SendCopToExit(shooter, new Vector3(-ExitSideOffset, 0.0f, 0.0f));
        }

        private void SendCopToExit(Ped ped, Vector3 offset)
        {
            if (ped == null || !ped.Exists() || ped.IsDead)
                return;

            try
            {
                ped.Task.ClearAll();
                ped.Weapons.RemoveAll();
                Function.Call(Hash.TASK_FOLLOW_NAV_MESH_TO_COORD,
                    ped.Handle,
                    exitAfterDialoguePosition.X + offset.X,
                    exitAfterDialoguePosition.Y + offset.Y,
                    exitAfterDialoguePosition.Z + offset.Z,
                    1.0f,
                    -1,
                    exitAfterDialogueDistance,
                    false,
                    0.0f
                );
            }
            catch
            {
                try
                {
                    ped.Task.FollowNavMeshTo(exitAfterDialoguePosition + offset);
                }
                catch
                {
                }
            }
        }

        private bool DeleteCopIfReachedExit(ref Ped ped)
        {
            if (ped == null || !ped.Exists())
            {
                ped = null;
                return true;
            }

            if (ped.IsDead)
                return false;

            if (ped.Position.DistanceTo(exitAfterDialoguePosition) > exitAfterDialogueDistance)
                return false;

            DeletePed(ped);
            ped = null;
            return true;
        }

        private void SetConversationSpeakerPose(string speaker)
        {
            if (shouter == null || !shouter.Exists() || shooter == null || !shooter.Exists())
                return;

            Ped speakingPed = string.Equals(speaker, "shooter", StringComparison.OrdinalIgnoreCase)
                ? shooter
                : shouter;

            try
            {
                FaceEachOther();
                PlayArgumentGesture(speakingPed, 2400);
            }
            catch
            {
            }
        }

        private void SetIdleConversationSpeakerPose(string speaker)
        {
            if (shouter == null || !shouter.Exists() || shooter == null || !shooter.Exists())
                return;

            Ped speakingPed = string.Equals(speaker, "shooter", StringComparison.OrdinalIgnoreCase)
                ? shooter
                : shouter;

            try
            {
                FaceEachOther();
                PlayIdleGesture(speakingPed, 1800);
            }
            catch
            {
            }
        }

        private void PlayArgumentGesture(Ped ped, int durationMs)
        {
            int index = ChooseNonRepeatingIndex(ArgumentGestures.Length, lastConversationGestureIndex);
            lastConversationGestureIndex = index;
            PlayConversationGesture(ped, ArgumentGestures[index], durationMs);
        }

        private void PlayIdleGesture(Ped ped, int durationMs)
        {
            int index = ChooseNonRepeatingIndex(IdleConversationGestures.Length, lastIdleGestureIndex);
            lastIdleGestureIndex = index;
            PlayConversationGesture(ped, IdleConversationGestures[index], durationMs);
        }

        private void PlayConversationGesture(Ped ped, ConversationGesture gesture, int durationMs)
        {
            if (ped == null || !ped.Exists() || ped.IsDead)
                return;

            try
            {
                RequestAnimationDictionary(gesture.dict, 500);

                if (!Function.Call<bool>(Hash.HAS_ANIM_DICT_LOADED, gesture.dict))
                    return;

                Function.Call(Hash.CLEAR_PED_SECONDARY_TASK, ped.Handle);
                Function.Call(
                    Hash.TASK_PLAY_ANIM,
                    ped.Handle,
                    gesture.dict,
                    gesture.animation,
                    6.0f,
                    -6.0f,
                    Math.Max(1000, durationMs),
                    48,
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

        private void FaceEachOther()
        {
            if (shouter == null || !shouter.Exists() || shooter == null || !shooter.Exists())
                return;

            try
            {
                SetPedHeadingToward(shouter, shooter.Position);
                SetPedHeadingToward(shooter, shouter.Position);
            }
            catch
            {
            }
        }

        private void MaintainConversationPose()
        {
            if (Game.GameTime < nextConversationPoseAt)
                return;

            nextConversationPoseAt = Game.GameTime + 3000;

            try
            {
                FaceEachOther();

                if (shouter != null && shouter.Exists())
                    Function.Call(Hash.TASK_STAND_STILL, shouter.Handle, 3200);

                if (shooter != null && shooter.Exists())
                    Function.Call(Hash.TASK_STAND_STILL, shooter.Handle, 3200);

                FaceEachOther();
            }
            catch
            {
            }
        }

        private void TickListenSpot(MissionRuntime runtime, Ped player)
        {
            if (!hasListenSpot)
            {
                state = "waiting_for_listen_setup";
                return;
            }

            float distance = GetHorizontalDistance(player.Position, listenSpotPosition);

            if (distance <= listenSpotRadius)
            {
                runtime.SetBackgroundInteractionPrompt(GetListenPrompt(player));
                state = "waiting_for_listen_interact";
                return;
            }

            state = "waiting_for_listen_spot";
        }

        private void StartPlayerListening(Ped player)
        {
            if (player == null || !player.Exists())
                return;

            playerListening = true;
            state = listenRequireCover ? "listening_cover" : "listening";
        }

        private void ReleasePlayerListening()
        {
            if (!playerListening)
                return;

            playerListening = false;
        }

        private string GetListenPrompt(Ped player)
        {
            if (listenRequireCover && !IsPlayerInCover(player))
                return "Wejdz w oslone przy scianie";

            return listenPrompt;
        }

        private bool IsPlayerInCover(Ped player)
        {
            if (player == null || !player.Exists())
                return false;

            try
            {
                return Function.Call<bool>(Hash.IS_PED_IN_COVER, player.Handle, false);
            }
            catch
            {
                return false;
            }
        }

        private void StartShouterAlert(Ped player)
        {
            if (shouter == null || !shouter.Exists() || shouter.IsDead)
                return;

            try
            {
                shouter.Task.ClearAll();
                shouter.Heading = GetHeadingToward(shouter.Position, player.Position);
                Function.Call(Hash.TASK_TURN_PED_TO_FACE_ENTITY, shouter.Handle, player.Handle, 600);
                PlayPointingAnimation(shouter, 3500);
            }
            catch
            {
            }
        }

        private void PlayPointingAnimation(Ped ped, int durationMs)
        {
            try
            {
                string dict = "gestures@m@standing@casual";
                string animation = "gesture_point";

                RequestAnimationDictionary(dict, 500);

                if (!Function.Call<bool>(Hash.HAS_ANIM_DICT_LOADED, dict))
                    return;

                Function.Call(
                    Hash.TASK_PLAY_ANIM,
                    ped.Handle,
                    dict,
                    animation,
                    4.0f,
                    -4.0f,
                    Math.Max(1000, durationMs),
                    49,
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

        private void TickShooterCombat(Ped player)
        {
            if (shooter == null || !shooter.Exists() || shooter.IsDead)
            {
                state = "shooter_dead";
                return;
            }

            try
            {
                if (!shooterAttackStarted)
                {
                    shooter.Task.ClearAll();
                    PrepareShooterForAttack(shooter);
                    shooterAttackStarted = true;
                    StartShooterCombat(player);
                    nextShooterWatchdogAt = Game.GameTime + 5000;
                    state = "shooting";
                    return;
                }

                if (Game.GameTime >= nextShooterWatchdogAt && !IsShooterActivelyFighting(player))
                {
                    StartShooterCombat(player);
                    nextShooterWatchdogAt = Game.GameTime + 5000;
                }

                state = "shooting";
            }
            catch
            {
                state = "shooting_failed";
            }
        }

        private void StartShooterCombat(Ped player)
        {
            if (shooter == null || !shooter.Exists() || player == null || !player.Exists())
                return;

            shooter.Heading = GetHeadingToward(shooter.Position, player.Position);
            Function.Call(Hash.TASK_COMBAT_PED, shooter.Handle, player.Handle, 0, 16);
            Function.Call(Hash.SET_PED_KEEP_TASK, shooter.Handle, true);
        }

        private bool IsShooterActivelyFighting(Ped player)
        {
            if (shooter == null || !shooter.Exists() || player == null || !player.Exists())
                return false;

            try
            {
                if (Function.Call<bool>(Hash.IS_PED_IN_COMBAT, shooter.Handle, player.Handle))
                    return true;
            }
            catch
            {
            }

            try
            {
                if (Function.Call<bool>(Hash.IS_PED_SHOOTING, shooter.Handle))
                    return true;
            }
            catch
            {
            }

            return false;
        }

        private void SuppressWantedLevel()
        {
            if (!suppressWantedLevel)
                return;

            try
            {
                if (!wantedSuppressionActive)
                {
                    previousMaxWantedLevel = Game.MaxWantedLevel;
                    wantedSuppressionActive = true;
                }

                Game.MaxWantedLevel = 0;
                Game.Player.Wanted.SetWantedLevel(0, false);
                Game.Player.Wanted.ApplyWantedLevelChangeNow(false);
                // Keep custom cop-model attackers able to target the player while normal dispatch stays disabled.
                Function.Call(Hash.SET_POLICE_IGNORE_PLAYER, Game.Player.Handle, false);
                Function.Call(Hash.SET_DISPATCH_COPS_FOR_PLAYER, Game.Player.Handle, false);
            }
            catch
            {
            }
        }

        private void RestoreWantedLevel()
        {
            if (!wantedSuppressionActive)
                return;

            try
            {
                Game.Player.Wanted.SetWantedLevel(0, false);
                Game.Player.Wanted.ApplyWantedLevelChangeNow(false);
                Game.MaxWantedLevel = Clamp(previousMaxWantedLevel, 0, 5);
                Function.Call(Hash.SET_POLICE_IGNORE_PLAYER, Game.Player.Handle, false);
                Function.Call(Hash.SET_DISPATCH_COPS_FOR_PLAYER, Game.Player.Handle, true);
            }
            catch
            {
            }

            wantedSuppressionActive = false;
        }

        private float GetClosestDistanceToPlayer(Ped player)
        {
            float distance = 9999f;

            if (shouter != null && shouter.Exists())
                distance = Math.Min(distance, shouter.Position.DistanceTo(player.Position));

            if (shooter != null && shooter.Exists())
                distance = Math.Min(distance, shooter.Position.DistanceTo(player.Position));

            return distance;
        }

        private void DeleteCops()
        {
            DeletePed(shouter);
            DeletePed(shooter);
            shouter = null;
            shooter = null;
        }

        private void DeletePed(Ped ped)
        {
            try
            {
                if (ped != null && ped.Exists())
                    ped.Delete();
            }
            catch
            {
            }
        }

        private int ChooseNonRepeatingIndex(int count, int previousIndex)
        {
            if (count <= 1)
                return 0;

            int index = rng.Next(0, count);

            if (index == previousIndex)
                index = (index + 1 + rng.Next(0, count - 1)) % count;

            return index;
        }

        private struct ConversationGesture
        {
            public readonly string dict;
            public readonly string animation;

            public ConversationGesture(string dict, string animation)
            {
                this.dict = dict;
                this.animation = animation;
            }
        }

        private static readonly ConversationGesture[] ArgumentGestures = new ConversationGesture[]
        {
            new ConversationGesture("gestures@m@standing@casual", "gesture_point"),
            new ConversationGesture("gestures@m@standing@casual", "gesture_no_way"),
            new ConversationGesture("gestures@m@standing@casual", "gesture_damn"),
            new ConversationGesture("gestures@m@standing@casual", "gesture_displeased"),
            new ConversationGesture("gestures@m@standing@casual", "gesture_me_hard"),
            new ConversationGesture("gestures@m@standing@casual", "gesture_you_hard"),
            new ConversationGesture("gestures@m@standing@casual", "gesture_shrug_hard"),
            new ConversationGesture("gestures@m@standing@casual", "gesture_hand_right")
        };

        private static readonly ConversationGesture[] IdleConversationGestures = new ConversationGesture[]
        {
            new ConversationGesture("gestures@m@standing@casual", "gesture_hand_left"),
            new ConversationGesture("gestures@m@standing@casual", "gesture_hand_right"),
            new ConversationGesture("gestures@m@standing@casual", "gesture_hello"),
            new ConversationGesture("gestures@m@standing@casual", "gesture_bring_it_on"),
            new ConversationGesture("gestures@m@standing@casual", "gesture_shrug_soft"),
            new ConversationGesture("gestures@m@standing@casual", "gesture_me_soft"),
            new ConversationGesture("gestures@m@standing@casual", "gesture_you_soft")
        };

        private static void SetPedHeadingToward(Ped ped, Vector3 targetPosition)
        {
            if (ped == null || !ped.Exists())
                return;

            float heading = GetHeadingToward(ped.Position, targetPosition);
            ped.Heading = heading;
            Function.Call(Hash.SET_ENTITY_HEADING, ped.Handle, heading);
        }

        private static float GetHeadingToward(Vector3 from, Vector3 to)
        {
            Vector3 direction = to - from;
            float angle = (float)(Math.Atan2(direction.X, direction.Y) * 180.0 / Math.PI);

            if (angle < 0f)
                angle += 360f;

            return angle;
        }

        private static void RequestAnimationDictionary(string dict, int timeoutMs)
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

        private static Vector3 GetSpreadDirection(Vector3 spawnCenter, Vector3 playerPosition)
        {
            Vector3 toPlayer = playerPosition - spawnCenter;
            toPlayer = new Vector3(toPlayer.X, toPlayer.Y, 0f);

            if (toPlayer.Length() <= 0.01f)
                return new Vector3(1f, 0f, 0f);

            toPlayer.Normalize();

            Vector3 spread = new Vector3(-toPlayer.Y, toPlayer.X, 0f);

            if (spread.Length() <= 0.01f)
                return new Vector3(1f, 0f, 0f);

            spread.Normalize();
            return spread;
        }

        private static Vector3 ReadVector(MissionEffect config, string prefix, Vector3 fallback)
        {
            if (config == null)
                return fallback;

            return new Vector3(
                config.GetFloat(prefix + "X", fallback.X),
                config.GetFloat(prefix + "Y", fallback.Y),
                config.GetFloat(prefix + "Z", fallback.Z)
            );
        }

        private static float GetHorizontalDistance(Vector3 first, Vector3 second)
        {
            float dx = first.X - second.X;
            float dy = first.Y - second.Y;

            return (float)Math.Sqrt(dx * dx + dy * dy);
        }

        private static float PositiveOrDefault(float value, float fallback)
        {
            return value > 0f ? value : fallback;
        }

        private static int Clamp(int value, int min, int max)
        {
            if (value < min)
                return min;

            if (value > max)
                return max;

            return value;
        }

        private static WeaponHash ParseWeapon(string value, WeaponHash fallback)
        {
            if (string.IsNullOrEmpty(value))
                return fallback;

            try
            {
                return (WeaponHash)Enum.Parse(typeof(WeaponHash), value, true);
            }
            catch
            {
                return fallback;
            }
        }

        private static PedHash ParsePedHash(string value, PedHash fallback)
        {
            if (string.IsNullOrEmpty(value))
                return fallback;

            try
            {
                return (PedHash)Enum.Parse(typeof(PedHash), value, true);
            }
            catch
            {
                return fallback;
            }
        }
    }
}
