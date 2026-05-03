using GTA;
using GTA.Math;
using GTA.Native;
using GTAVTrueCrimesMod.Models;
using GTAVTrueCrimesMod.Systems;
using System;

namespace GTAVTrueCrimesMod.Behaviors
{
    public class StalkerBehavior : IMissionBackgroundBehavior, IScriptedMissionKillTarget, INodeTransitionBackgroundBehavior
    {
        private static readonly string[] PretendPhoneSpeechLines = new[]
        {
            "GENERIC_HI",
            "GENERIC_HOWS_IT_GOING",
            "GENERIC_YES",
            "GENERIC_NO",
            "GENERIC_THANKS",
            "GENERIC_BYE",
            "GENERIC_WHATEVER",
            "GENERIC_SHOCKED_MED",
            "GENERIC_CURSE_MED",
            "CHAT_STATE"
        };

        private readonly string id;
        private readonly float distanceBehindPlayer;
        private readonly bool attackEnabled;
        private readonly float followDistance;
        private readonly float runDistance;
        private readonly float walkDistance;
        private readonly float tooCloseDistance;
        private readonly float playerLookingDistance;
        private readonly float playerLookingAngle;
        private readonly float isolationRadius;
        private readonly int maxWitnesses;
        private readonly float attackDistance;
        private readonly float meleeDistance;
        private readonly int followRepathMs;
        private readonly int pretendDurationMs;
        private readonly int attackDamage;
        private readonly int attackDamageIntervalMs;
        private readonly int playerDamageMemoryMs;
        private readonly bool preserveDeadBodyOnNodeExit;
        private readonly MissionEffect[] onKilledByPlayer;
        private readonly MissionEffect[] onKilledByOther;
        private readonly StalkerDecisionConfig decisionConfig;
        private readonly Random rng = new Random();

        private Ped stalker;
        private bool pretending;
        private bool attacking;
        private bool deathHandled;
        private bool scriptedKillPending;
        private bool scriptedKilledByOther;
        private int lastPlayerDamageAt;
        private int scriptedKillNextShotAt;
        private int scriptedKillShotsRemaining;
        private int scriptedKillShotGapMs;
        private int scriptedKillDamage;
        private Ped scriptedShooter;
        private int scriptedShooterCleanupAt;
        private string state = "not_spawned";
        private int pretendMode;
        private int pretendUntil;
        private int nextPretendTaskAt;
        private int nextDamageAt;
        private int nextFollowTaskAt;
        private string lastMovementState = "spawning";
        private bool pretendPhoneActive;
        private bool pretendPhoneHolding;
        private int pretendPhoneHoldAt;
        private int nextPretendPhoneSpeechAt;
        private int nextPretendPhoneMoveAt;
        private bool pretendPhoneWalking;
        private PhonePropAnimation pretendPhoneAnimation;
        private string lastPretendPhoneSpeechLine = "";
        private Vector3 currentPretendDirection;
        private Vector3 currentPretendDestination;
        private int lastWitnessCount;
        private bool lastPlayerIsolated;
        private bool lastPlayerLooking;
        private bool lastAttackEnabled;
        private float lastDistance;

        public StalkerBehavior(MissionEffect config)
        {
            string configuredId = config == null ? "" : config.GetString("id", "");
            float configuredDistanceBehindPlayer = config == null ? 40f : config.GetFloat("distanceBehindPlayer", 40f);

            id = string.IsNullOrEmpty(configuredId) ? "stalker" : configuredId;
            distanceBehindPlayer = configuredDistanceBehindPlayer <= 0f ? 40f : configuredDistanceBehindPlayer;
            attackEnabled = config != null && config.GetBool("attackEnabled", false);
            followDistance = PositiveOrDefault(config == null ? 0f : config.GetFloat("followDistance", 18f), 18f);
            runDistance = PositiveOrDefault(config == null ? 0f : config.GetFloat("runDistance", 45f), 45f);
            walkDistance = PositiveOrDefault(config == null ? 0f : config.GetFloat("walkDistance", 14f), 14f);
            tooCloseDistance = PositiveOrDefault(config == null ? 0f : config.GetFloat("tooCloseDistance", 8f), 8f);
            playerLookingDistance = PositiveOrDefault(config == null ? 0f : config.GetFloat("playerLookingDistance", 45f), 45f);
            playerLookingAngle = PositiveOrDefault(config == null ? 0f : config.GetFloat("playerLookingAngle", 45f), 45f);
            isolationRadius = PositiveOrDefault(config == null ? 0f : config.GetFloat("isolationRadius", 35f), 35f);
            maxWitnesses = config == null ? 0 : Math.Max(0, config.GetInt("maxWitnesses", 0));
            attackDistance = PositiveOrDefault(config == null ? 0f : config.GetFloat("attackDistance", 5f), 5f);
            meleeDistance = PositiveOrDefault(config == null ? 0f : config.GetFloat("meleeDistance", 4f), 4f);
            followRepathMs = config == null ? 1500 : Math.Max(250, config.GetInt("followRepathMs", 1500));
            pretendDurationMs = config == null ? 5000 : Math.Max(500, config.GetInt("pretendDurationMs", 5000));
            attackDamage = config == null ? 0 : Math.Max(0, config.GetInt("attackDamage", 0));
            attackDamageIntervalMs = config == null ? 450 : Math.Max(100, config.GetInt("attackDamageIntervalMs", 450));
            playerDamageMemoryMs = config == null ? 5000 : Math.Max(0, config.GetInt("playerDamageMemoryMs", 5000));
            preserveDeadBodyOnNodeExit = config != null && config.GetBool("preserveDeadBodyOnNodeExit", false);
            onKilledByPlayer = config == null || config.onKilledByPlayer == null ? new MissionEffect[0] : config.onKilledByPlayer;
            onKilledByOther = config == null || config.onKilledByOther == null ? new MissionEffect[0] : config.onKilledByOther;
            decisionConfig = CreateDecisionConfig();
        }

        public string Id
        {
            get { return id; }
        }

        public string DebugText
        {
            get
            {
                string distanceText = "-";

                if (stalker != null && stalker.Exists())
                {
                    distanceText = lastDistance.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + "m";
                }

                return
                    "stalker[" + id + "] state=" + state +
                    " dist=" + distanceText +
                    " witnesses=" + lastWitnessCount +
                    " isolated=" + lastPlayerIsolated +
                    " looking=" + lastPlayerLooking +
                    " attack=" + lastAttackEnabled;
            }
        }

        public void Tick(MissionRuntime runtime)
        {
            if (!EnsureStalkerExists())
                return;

            TickScriptedShooterCleanup();
            TrackPlayerDamage();

            if (HandleStalkerDeath(runtime))
                return;

            if (TickScriptedKillByOther())
                return;

            StalkerTickContext context = CaptureTickContext();
            StalkerDecision decision = StalkerDecisionModel.Decide(decisionConfig, context.ToDecisionInput(attacking, pretending, lastMovementState));

            UpdateDebugSnapshot(context, decision);
            ExecuteDecision(runtime, context, decision);
        }

        private bool EnsureStalkerExists()
        {
            if (stalker != null && stalker.Exists())
                return true;

            state = "spawning";
            SpawnBehindPlayer();

            if (stalker != null && stalker.Exists())
                return true;

            state = "missing";
            return false;
        }

        private StalkerTickContext CaptureTickContext()
        {
            StalkerTickContext context = new StalkerTickContext();

            context.player = Game.Player.Character;
            context.distance = stalker.Position.DistanceTo(context.player.Position);
            context.witnessCount = CountNearbyWitnesses(isolationRadius);
            context.playerLooking = IsPlayerLookingAt(stalker, playerLookingDistance, playerLookingAngle);
            context.canRepath = Game.GameTime >= nextFollowTaskAt;
            context.playerDead = context.player.IsDead || context.player.Health <= 0;

            return context;
        }

        private void UpdateDebugSnapshot(StalkerTickContext context, StalkerDecision decision)
        {
            lastDistance = context.distance;
            lastWitnessCount = context.witnessCount;
            lastPlayerIsolated = decision.isPlayerIsolated;
            lastAttackEnabled = attackEnabled;
            lastPlayerLooking = context.playerLooking;
        }

        private void ExecuteDecision(MissionRuntime runtime, StalkerTickContext context, StalkerDecision decision)
        {
            if (decision == null)
                return;

            if (decision.shouldStopPretending)
                StopPretending();

            if (decision.action == StalkerDecision.AbortAttackWitnesses)
            {
                state = "attacking";
                StopAttackAndBlendIn();
                return;
            }

            if (decision.action == StalkerDecision.FailPlayerKilled)
            {
                state = "attacking";
                runtime.FailMission("Zostales zabity przez stalkera.");
                return;
            }

            if (decision.action == StalkerDecision.ContinueAttackApproach ||
                decision.action == StalkerDecision.ContinueAttackCombat ||
                decision.action == StalkerDecision.ApplyAttackDamage)
            {
                state = "attacking";
                TickAttackDamage(runtime);
                return;
            }

            if (decision.action == StalkerDecision.StartAttack)
            {
                state = "attack_start";
                StartAttack();
                return;
            }

            if (decision.action == StalkerDecision.ApproachAttack)
            {
                ApproachBeforeAttack(context.player, context.distance);
                return;
            }

            if (decision.action == StalkerDecision.Pretend)
            {
                TickPretendState();
                return;
            }

            if (decision.action == StalkerDecision.KeepMovement)
            {
                state = lastMovementState;
                return;
            }

            TickFollowMovement(context, decision.action);
        }

        private void TickPretendState()
        {
            if (!pretending)
            {
                state = "pretend_start";
                StartPretending();
            }

            TickPretending();
        }

        private void TickFollowMovement(StalkerTickContext context, string action)
        {
            nextFollowTaskAt = Game.GameTime + followRepathMs;

            Vector3 followPoint = context.player.Position - context.player.ForwardVector * followDistance;

            if (action == StalkerDecision.RunFollow)
            {
                SetMovementState("running");
                MoveQuicklyWhenUnseen(followPoint, context.playerLooking);
                return;
            }

            if (action == StalkerDecision.WalkFollow)
            {
                SetMovementState("walking");
                MoveQuicklyWhenUnseen(followPoint, context.playerLooking);
                return;
            }

            if (action == StalkerDecision.MoveAwayTooClose)
            {
                SetMovementState("too_close");
                stalker.Task.FollowNavMeshTo(context.player.Position - context.player.ForwardVector * followDistance);
                return;
            }

            SetMovementState("loitering");
            stalker.Task.WanderAround(stalker.Position, 4f);
        }

        private void SetMovementState(string newState)
        {
            state = newState;
            lastMovementState = newState;
        }

        public void Clear()
        {
            StopPretendPhoneCall();

            if (stalker != null && stalker.Exists())
            {
                stalker.Delete();
                stalker = null;
            }

            ResetBehaviorStateAfterClear();
        }

        public void ClearForNodeTransition(MissionRuntime runtime)
        {
            StopPretendPhoneCall();
            ClearScriptedShooter();

            if (ShouldPreserveDeadBodyOnNodeExit())
            {
                try
                {
                    stalker.IsPersistent = true;
                    stalker.BlockPermanentEvents = true;
                }
                catch
                {
                }

                if (runtime != null)
                    runtime.PreserveEntityForMissionCleanup(stalker);

                stalker = null;
                ResetBehaviorStateAfterClear();
                return;
            }

            Clear();
        }

        private bool ShouldPreserveDeadBodyOnNodeExit()
        {
            if (!preserveDeadBodyOnNodeExit)
                return false;

            if (stalker == null || !stalker.Exists())
                return false;

            return stalker.IsDead || stalker.Health <= 0;
        }

        private void ResetBehaviorStateAfterClear()
        {
            pretending = false;
            attacking = false;
            deathHandled = false;
            scriptedKillPending = false;
            scriptedKilledByOther = false;
            lastPlayerDamageAt = 0;
            scriptedKillNextShotAt = 0;
            scriptedKillShotsRemaining = 0;
            scriptedKillShotGapMs = 0;
            scriptedKillDamage = 0;
            ClearScriptedShooter();
            state = "cleared";
            lastMovementState = "cleared";
            pretendMode = 0;
            pretendUntil = 0;
            nextPretendTaskAt = 0;
            nextDamageAt = 0;
            nextFollowTaskAt = 0;
            currentPretendDirection = Vector3.Zero;
            currentPretendDestination = Vector3.Zero;
            ResetPretendPhoneState();
        }

        private StalkerDecisionConfig CreateDecisionConfig()
        {
            return new StalkerDecisionConfig
            {
                attackEnabled = attackEnabled,
                maxWitnesses = maxWitnesses,
                attackDistance = attackDistance,
                meleeDistance = meleeDistance,
                playerLookingDistance = playerLookingDistance,
                runDistance = runDistance,
                walkDistance = walkDistance,
                tooCloseDistance = tooCloseDistance,
                attackDamageEnabled = attackDamage > 0
            };
        }

        private void SpawnBehindPlayer()
        {
            Ped player = Game.Player.Character;
            Vector3 spawnPos = player.Position - player.ForwardVector * distanceBehindPlayer;

            Model model = new Model(PedHash.Business01AMM);
            model.Request(1000);

            stalker = World.CreatePed(model, spawnPos);

            if (stalker == null || !stalker.Exists())
                return;

            stalker.BlockPermanentEvents = true;
            stalker.KeepTaskWhenMarkedAsNoLongerNeeded = true;
            stalker.IsPersistent = true;
            pretending = false;
            attacking = false;
            deathHandled = false;
            scriptedKillPending = false;
            scriptedKilledByOther = false;
            lastPlayerDamageAt = 0;
            scriptedKillNextShotAt = 0;
            scriptedKillShotsRemaining = 0;
            scriptedKillShotGapMs = 0;
            scriptedKillDamage = 0;
            ClearScriptedShooter();
            state = "spawned";
            lastMovementState = "spawned";
            nextDamageAt = 0;
            pretendMode = 0;
            pretendUntil = 0;
            nextPretendTaskAt = 0;
            nextFollowTaskAt = 0;
            currentPretendDirection = Vector3.Zero;
            currentPretendDestination = Vector3.Zero;
            ResetPretendPhoneState();

            GTA.UI.Screen.ShowSubtitle("Nieznajomy wtapia sie w tlum.", 4000);
        }

        private void TrackPlayerDamage()
        {
            if (stalker == null || !stalker.Exists() || deathHandled)
                return;

            try
            {
                Ped player = Game.Player.Character;

                if (player != null && player.Exists() && stalker.HasBeenDamagedBy(player))
                {
                    lastPlayerDamageAt = Game.GameTime;
                    stalker.ClearLastWeaponDamage();
                }
            }
            catch
            {
            }
        }

        private bool HandleStalkerDeath(MissionRuntime runtime)
        {
            if (stalker == null || !stalker.Exists())
                return false;

            if (!stalker.IsDead && stalker.Health > 0)
                return false;

            if (deathHandled)
                return true;

            deathHandled = true;
            scriptedKillPending = false;
            attacking = false;
            StopPretendPhoneCall();

            bool killedByPlayer = WasKilledByPlayer();
            state = killedByPlayer ? "killed_by_player" : "killed_by_other";
            lastMovementState = state;

            MissionEffect[] effects = killedByPlayer ? onKilledByPlayer : onKilledByOther;

            if (effects != null && effects.Length > 0)
                runtime.RunEffects(effects);

            return true;
        }

        private bool WasKilledByPlayer()
        {
            try
            {
                if (scriptedKilledByOther)
                    return false;

                Ped player = Game.Player.Character;
                Entity killer = stalker.Killer;

                if (killer != null && killer.Exists())
                {
                    if (player != null && player.Exists() && killer.Handle == player.Handle)
                        return true;

                    if (player != null && player.Exists() && player.IsInVehicle())
                    {
                        Vehicle vehicle = player.CurrentVehicle;

                        if (vehicle != null && vehicle.Exists() && killer.Handle == vehicle.Handle)
                            return true;
                    }
                }

                if (lastPlayerDamageAt > 0 && Game.GameTime - lastPlayerDamageAt <= playerDamageMemoryMs)
                    return true;
            }
            catch
            {
            }

            return false;
        }

        public bool BeginScriptedKillByOther(int shotCount, int shotGapMs, int damage)
        {
            if (stalker == null || !stalker.Exists() || deathHandled)
                return false;

            if (stalker.IsDead || stalker.Health <= 0)
                return false;

            scriptedKillPending = true;
            scriptedKilledByOther = true;
            scriptedKillNextShotAt = Game.GameTime;
            scriptedKillShotsRemaining = Math.Max(1, shotCount);
            scriptedKillShotGapMs = Math.Max(0, shotGapMs);
            scriptedKillDamage = Math.Max(1, damage);
            attacking = false;

            StopPretendPhoneCall();
            ResetPretendMovementState();

            state = "scripted_shot_pending";
            lastMovementState = state;

            try
            {
                stalker.Task.ClearAll();
            }
            catch
            {
            }

            return true;
        }

        public bool IsNearPosition(Vector3 position, float distance)
        {
            if (stalker == null || !stalker.Exists())
                return false;

            float radius = Math.Max(0.1f, distance);
            return stalker.Position.DistanceTo(position) <= radius;
        }

        private bool TickScriptedKillByOther()
        {
            if (!scriptedKillPending)
                return false;

            state = "scripted_shot";
            lastMovementState = state;

            if (stalker == null || !stalker.Exists() || stalker.IsDead || stalker.Health <= 0)
            {
                scriptedKillPending = false;
                return true;
            }

            if (Game.GameTime < scriptedKillNextShotAt)
                return true;

            FireScriptedShot();
            scriptedKillShotsRemaining--;

            if (scriptedKillShotsRemaining <= 0)
            {
                ApplyScriptedShotKill();
                scriptedKillPending = false;
                return true;
            }

            scriptedKillNextShotAt = Game.GameTime + scriptedKillShotGapMs;
            return true;
        }

        private void FireScriptedShot()
        {
            if (stalker == null || !stalker.Exists())
                return;

            try
            {
                Vector3 target = stalker.Position + new Vector3(0f, 0f, 1.05f);
                Vector3 source = GetScriptedShotSource(target);

                FireScriptedShooterShot(source);

                Function.Call(
                    Hash.SHOOT_SINGLE_BULLET_BETWEEN_COORDS,
                    source.X,
                    source.Y,
                    source.Z,
                    target.X,
                    target.Y,
                    target.Z,
                    scriptedKillDamage,
                    true,
                    (uint)WeaponHash.Pistol,
                    0,
                    true,
                    false,
                    2000.0f
                );
            }
            catch
            {
            }
        }

        private void FireScriptedShooterShot(Vector3 source)
        {
            try
            {
                Ped shooter = EnsureScriptedShooter(source);

                if (shooter == null || !shooter.Exists())
                    return;

                shooter.Position = source;
                shooter.Weapons.Give(WeaponHash.Pistol, 12, true, true);

                Function.Call(
                    Hash.TASK_SHOOT_AT_ENTITY,
                    shooter.Handle,
                    stalker.Handle,
                    Math.Max(350, scriptedKillShotGapMs + 250),
                    unchecked((int)0x5D60E4E0)
                );

                scriptedShooterCleanupAt = Game.GameTime + 2500;
            }
            catch
            {
            }
        }

        private Ped EnsureScriptedShooter(Vector3 source)
        {
            if (scriptedShooter != null && scriptedShooter.Exists())
                return scriptedShooter;

            Model model = new Model(PedHash.Business01AMM);
            model.Request(500);

            scriptedShooter = World.CreatePed(model, source);
            model.MarkAsNoLongerNeeded();

            if (scriptedShooter == null || !scriptedShooter.Exists())
                return null;

            scriptedShooter.IsPersistent = true;
            scriptedShooter.BlockPermanentEvents = true;
            scriptedShooter.KeepTaskWhenMarkedAsNoLongerNeeded = true;

            try
            {
                Function.Call(Hash.SET_ENTITY_VISIBLE, scriptedShooter.Handle, false, false);
                Function.Call(Hash.SET_ENTITY_COLLISION, scriptedShooter.Handle, false, false);
                Function.Call(Hash.FREEZE_ENTITY_POSITION, scriptedShooter.Handle, true);
                Function.Call(Hash.SET_ENTITY_INVINCIBLE, scriptedShooter.Handle, true);
            }
            catch
            {
            }

            return scriptedShooter;
        }

        private void TickScriptedShooterCleanup()
        {
            if (scriptedShooterCleanupAt <= 0 || Game.GameTime < scriptedShooterCleanupAt)
                return;

            ClearScriptedShooter();
        }

        private void ClearScriptedShooter()
        {
            scriptedShooterCleanupAt = 0;

            if (scriptedShooter == null)
                return;

            try
            {
                if (scriptedShooter.Exists())
                    scriptedShooter.Delete();
            }
            catch
            {
            }

            scriptedShooter = null;
        }

        private Vector3 GetScriptedShotSource(Vector3 target)
        {
            try
            {
                Ped player = Game.Player.Character;
                Vector3 toStalkerFromPlayer = target - player.Position;
                toStalkerFromPlayer = new Vector3(toStalkerFromPlayer.X, toStalkerFromPlayer.Y, 0f);

                if (toStalkerFromPlayer.Length() <= 0.01f)
                    toStalkerFromPlayer = player.ForwardVector;

                toStalkerFromPlayer.Normalize();

                Vector3 side = new Vector3(-toStalkerFromPlayer.Y, toStalkerFromPlayer.X, 0f);

                if (side.Length() <= 0.01f)
                    side = player.RightVector;

                side.Normalize();

                Vector3 playerToSide = target + side * 18f - player.Position;
                playerToSide = new Vector3(playerToSide.X, playerToSide.Y, 0f);

                if (playerToSide.Length() > 0.01f)
                {
                    playerToSide.Normalize();

                    if (playerToSide.X * side.X + playerToSide.Y * side.Y < 0f)
                        side = -side;
                }

                return target + side * 18f + new Vector3(0f, 0f, 2.8f);
            }
            catch
            {
                return target + new Vector3(18f, 0f, 2.8f);
            }
        }

        private void ApplyScriptedShotKill()
        {
            if (stalker == null || !stalker.Exists())
                return;

            try
            {
                stalker.Health = 0;
            }
            catch
            {
                try
                {
                    Function.Call(Hash.SET_ENTITY_HEALTH, stalker.Handle, 0);
                }
                catch
                {
                }
            }
        }

        private void StartPretending()
        {
            if (pretending || stalker == null || !stalker.Exists())
                return;

            pretending = true;
            state = "pretending";
            lastMovementState = "pretending";
            pretendUntil = Game.GameTime + pretendDurationMs;
            pretendMode = ChoosePretendMode();
            nextPretendTaskAt = 0;
            currentPretendDirection = ChoosePretendDirection();
            currentPretendDestination = Vector3.Zero;
            ResetPretendPhoneState();
            stalker.Task.ClearAll();
            TickPretending();
        }

        private void TickPretending()
        {
            if (stalker == null || !stalker.Exists())
                return;

            if (pretendMode != 2 && Game.GameTime < nextPretendTaskAt)
                return;

            if (pretendMode == 0)
            {
                nextPretendTaskAt = Game.GameTime + rng.Next(7000, 11000);
                currentPretendDestination = GetPretendPointInCurrentDirection(10f, 17f);
                WalkCalmlyTo(currentPretendDestination);
            }
            else if (pretendMode == 1)
            {
                nextPretendTaskAt = Game.GameTime + rng.Next(6500, 9500);
                currentPretendDestination = GetPretendPointInCurrentDirection(7f, 12f);
                WalkCalmlyTo(currentPretendDestination);
            }
            else if (pretendMode == 2)
            {
                TickPretendPhoneCall();
            }
            else
            {
                nextPretendTaskAt = Game.GameTime + rng.Next(4500, 7500);
                stalker.Task.StandStill(nextPretendTaskAt - Game.GameTime);
            }
        }

        private void StartAttack()
        {
            if (attacking || stalker == null || !stalker.Exists())
                return;

            StopPretendPhoneCall();
            attacking = true;
            state = "attacking";
            lastMovementState = "attacking";
            stalker.Task.ClearAll();
            stalker.Weapons.Give(WeaponHash.Knife, 1, true, true);
            stalker.Task.Combat(Game.Player.Character);
            nextDamageAt = Game.GameTime + 250;
            GTA.UI.Screen.ShowSubtitle("Ktos rusza na ciebie z nozem.", 4000);
        }

        private void ApproachBeforeAttack(Ped player, float distance)
        {
            StopPretendPhoneCall();
            ResetPretendMovementState();

            if (Game.GameTime < nextFollowTaskAt)
            {
                state = "attack_approach_waiting";
                lastMovementState = state;
                return;
            }

            nextFollowTaskAt = Game.GameTime + followRepathMs;
            state = "attack_approach";
            lastMovementState = state;

            Vector3 approachPoint = player.Position;

            MoveQuicklyWhenUnseen(approachPoint, lastPlayerLooking);
        }

        private void StopAttackAndBlendIn()
        {
            if (stalker == null || !stalker.Exists())
                return;

            attacking = false;
            nextDamageAt = 0;
            stalker.Task.ClearAll();
            stalker.Weapons.Remove(WeaponHash.Knife);
            ResetPretendPhoneState();

            state = "attack_aborted_witnesses";
            lastMovementState = state;
            StartPretending();
        }

        private void StopPretending()
        {
            StopPretendPhoneCall();
            ResetPretendMovementState();
        }

        private void ResetPretendMovementState()
        {
            pretending = false;
            pretendMode = 0;
            pretendUntil = 0;
            nextPretendTaskAt = 0;
            currentPretendDirection = Vector3.Zero;
            currentPretendDestination = Vector3.Zero;
        }

        private void TickAttackDamage(MissionRuntime runtime)
        {
            if (stalker == null || !stalker.Exists())
                return;

            Ped player = Game.Player.Character;

            if (player.IsDead || player.Health <= 0)
            {
                runtime.FailMission("Zostales zabity przez stalkera.");
                return;
            }

            float distance = stalker.Position.DistanceTo(player.Position);
            lastDistance = distance;

            if (distance > meleeDistance)
            {
                stalker.Task.Combat(player);
                return;
            }

            if (attackDamage <= 0)
            {
                stalker.Task.Combat(player);
                return;
            }

            if (Game.GameTime < nextDamageAt)
                return;

            nextDamageAt = Game.GameTime + attackDamageIntervalMs;

            if (player.Health <= attackDamage)
            {
                player.Health = 1;
                runtime.FailMission("Zostales zabity przez stalkera.");
                return;
            }

            player.Health = Math.Max(1, player.Health - attackDamage);
        }

        private float PositiveOrDefault(float value, float fallback)
        {
            return value > 0f ? value : fallback;
        }

        private bool IsPlayerLookingAt(Entity target, float maxDistance, float maxAngle)
        {
            Ped player = Game.Player.Character;
            Vector3 toTarget = target.Position - player.Position;
            float distance = toTarget.Length();

            if (distance > maxDistance || distance <= 0.01f)
                return false;

            toTarget.Normalize();

            Vector3 forward = GetGameplayCameraForwardVector();
            forward.Normalize();

            float dot = forward.X * toTarget.X + forward.Y * toTarget.Y + forward.Z * toTarget.Z;
            dot = Math.Max(-1f, Math.Min(1f, dot));

            double angle = Math.Acos(dot) * (180.0 / Math.PI);

            return angle < maxAngle;
        }

        private Vector3 GetGameplayCameraForwardVector()
        {
            Vector3 rotation = Function.Call<Vector3>(Hash.GET_GAMEPLAY_CAM_ROT, 2);
            float pitch = rotation.X * (float)(Math.PI / 180.0);
            float yaw = rotation.Z * (float)(Math.PI / 180.0);
            float cosPitch = (float)Math.Cos(pitch);

            return new Vector3(
                -(float)Math.Sin(yaw) * cosPitch,
                (float)Math.Cos(yaw) * cosPitch,
                (float)Math.Sin(pitch)
            );
        }

        private bool IsPlayerIsolated(float radius, int maxWitnesses)
        {
            int witnessCount = CountNearbyWitnesses(radius);

            lastWitnessCount = witnessCount;
            lastPlayerIsolated = witnessCount <= maxWitnesses;

            return lastPlayerIsolated;
        }

        private int CountNearbyWitnesses(float radius)
        {
            Ped player = Game.Player.Character;
            Ped[] nearby = World.GetNearbyPeds(player, radius);
            int count = 0;

            foreach (Ped ped in nearby)
            {
                if (ped == null || !ped.Exists())
                    continue;

                if (ped.Handle == player.Handle)
                    continue;

                if (stalker != null && stalker.Exists() && ped.Handle == stalker.Handle)
                    continue;

                if (ped.IsDead)
                    continue;

                if (!ped.IsHuman)
                    continue;

                if (ped.IsInVehicle())
                    continue;

                float distance = ped.Position.DistanceTo(player.Position);

                if (distance > 8f && !HasLineOfSight(player, ped))
                    continue;

                count++;
            }

            return count;
        }

        private bool HasLineOfSight(Ped from, Ped to)
        {
            try
            {
                return Function.Call<bool>(
                    Hash.HAS_ENTITY_CLEAR_LOS_TO_ENTITY,
                    from.Handle,
                    to.Handle,
                    17
                );
            }
            catch
            {
                return true;
            }
        }

        private void TickPretendPhoneCall()
        {
            if (stalker == null || !stalker.Exists())
                return;

            if (pretendPhoneAnimation != null)
                pretendPhoneAnimation.Tick(Game.GameTime);

            state = pretendPhoneHolding ? "pretend_phone_call" : "pretend_phone_pickup";
            lastMovementState = state;

            if (!pretendPhoneActive)
            {
                StartPretendPhonePickup();
                return;
            }

            if (!pretendPhoneHolding)
            {
                if (Game.GameTime < pretendPhoneHoldAt)
                    return;

                StartPretendPhoneHold();
            }

            TickPretendPhoneWalking();
            TickPretendPhoneSpeech();
        }

        private void StartPretendPhonePickup()
        {
            pretendPhoneActive = true;
            pretendPhoneHolding = false;
            pretendPhoneHoldAt = Game.GameTime + 1400;
            nextPretendTaskAt = 0;
            nextPretendPhoneSpeechAt = Game.GameTime + rng.Next(2400, 3600);
            nextPretendPhoneMoveAt = 0;
            pretendPhoneWalking = false;
            currentPretendDestination = GetPretendPointInCurrentDirection(12f, 20f);

            try
            {
                EnsurePretendPhoneAnimation().BeginPickup(pretendDurationMs + 12000);
            }
            catch
            {
            }
        }

        private void StartPretendPhoneHold()
        {
            pretendPhoneHolding = true;
            nextPretendTaskAt = 0;
            nextPretendPhoneMoveAt = Game.GameTime + rng.Next(10000, 16000);
            WalkCalmlyTo(currentPretendDestination);
            PlayPretendPhoneHoldAnimation(pretendDurationMs + 12000);
            pretendPhoneWalking = true;
        }

        private void TickPretendPhoneWalking()
        {
            if (!pretendPhoneWalking)
                return;

            if (currentPretendDestination != Vector3.Zero &&
                stalker.Position.DistanceTo(currentPretendDestination) > 2.5f &&
                Game.GameTime < nextPretendPhoneMoveAt)
            {
                return;
            }

            if (Game.GameTime < nextPretendPhoneMoveAt)
                return;

            nextPretendPhoneMoveAt = Game.GameTime + rng.Next(10000, 16000);
            currentPretendDestination = GetPretendPointInCurrentDirection(9f, 16f);

            try
            {
                WalkCalmlyTo(currentPretendDestination);
                PlayPretendPhoneHoldAnimation(rng.Next(9000, 14000));
            }
            catch
            {
            }
        }

        private int ChoosePretendMode()
        {
            int roll = rng.Next(0, 100);

            if (roll < 55)
                return 2;

            if (roll < 75)
                return 0;

            if (roll < 90)
                return 1;

            return 3;
        }

        private Vector3 ChoosePretendDirection()
        {
            Vector3 sidewalkDirection;

            if (TryGetSidewalkPerpendicularDirection(out sidewalkDirection))
                return sidewalkDirection;

            Vector3 direction;
            int roll = rng.Next(0, 4);

            if (roll == 0)
                direction = stalker.ForwardVector;
            else if (roll == 1)
                direction = -stalker.ForwardVector;
            else if (roll == 2)
                direction = stalker.RightVector;
            else
                direction = -stalker.RightVector;

            direction = new Vector3(direction.X, direction.Y, 0f);

            if (direction.Length() <= 0.01f)
                direction = new Vector3(1f, 0f, 0f);

            direction.Normalize();
            return direction;
        }

        private bool TryGetSidewalkPerpendicularDirection(out Vector3 direction)
        {
            direction = Vector3.Zero;

            if (stalker == null || !stalker.Exists())
                return false;

            try
            {
                OutputArgument nodePositionArg = new OutputArgument();
                OutputArgument headingArg = new OutputArgument();

                bool found = Function.Call<bool>(
                    Hash.GET_CLOSEST_VEHICLE_NODE_WITH_HEADING,
                    stalker.Position.X,
                    stalker.Position.Y,
                    stalker.Position.Z,
                    nodePositionArg,
                    headingArg,
                    1,
                    3.0f,
                    0
                );

                if (!found)
                    return false;

                float heading = headingArg.GetResult<float>();
                float headingRad = heading * (float)(Math.PI / 180.0);

                Vector3 roadDirection = new Vector3(
                    (float)Math.Sin(headingRad),
                    (float)Math.Cos(headingRad),
                    0f
                );

                if (roadDirection.Length() <= 0.01f)
                    return false;

                roadDirection.Normalize();
                direction = new Vector3(-roadDirection.Y, roadDirection.X, 0f);

                Ped player = Game.Player.Character;
                Vector3 toPlayer = player.Position - stalker.Position;
                toPlayer = new Vector3(toPlayer.X, toPlayer.Y, 0f);

                if (toPlayer.Length() > 0.01f)
                {
                    toPlayer.Normalize();

                    float dot = direction.X * toPlayer.X + direction.Y * toPlayer.Y;

                    if (dot > 0f)
                        direction = -direction;
                }

                direction.Normalize();
                return true;
            }
            catch
            {
                direction = Vector3.Zero;
                return false;
            }
        }

        private void TickPretendPhoneSpeech()
        {
            if (Game.GameTime < nextPretendPhoneSpeechAt)
                return;

            nextPretendPhoneSpeechAt = Game.GameTime + rng.Next(3500, 7000);
            string line = ChoosePretendPhoneSpeechLine();

            try
            {
                Function.Call(
                    Hash.PLAY_PED_AMBIENT_SPEECH_NATIVE,
                    stalker.Handle,
                    line,
                    "SPEECH_PARAMS_FORCE_NORMAL_CLEAR"
                );
            }
            catch
            {
            }
        }

        private string ChoosePretendPhoneSpeechLine()
        {
            if (PretendPhoneSpeechLines.Length == 0)
                return "CHAT_STATE";

            if (PretendPhoneSpeechLines.Length == 1)
            {
                lastPretendPhoneSpeechLine = PretendPhoneSpeechLines[0];
                return lastPretendPhoneSpeechLine;
            }

            string line = lastPretendPhoneSpeechLine;
            int guard = 0;

            while (line == lastPretendPhoneSpeechLine && guard < 8)
            {
                line = PretendPhoneSpeechLines[rng.Next(0, PretendPhoneSpeechLines.Length)];
                guard++;
            }

            lastPretendPhoneSpeechLine = line;
            return line;
        }

        private void PlayPretendPhoneHoldAnimation(int durationMs)
        {
            try
            {
                EnsurePretendPhoneAnimation().StartHold(Math.Max(1000, durationMs));
            }
            catch
            {
            }
        }

        private void StopPretendPhoneCall()
        {
            if (stalker == null || !stalker.Exists())
            {
                if (pretendPhoneAnimation != null)
                    pretendPhoneAnimation.Stop();

                pretendPhoneAnimation = null;
                ResetPretendPhoneState();
                return;
            }

            if (!pretendPhoneActive && pretendPhoneAnimation == null)
                return;

            if (pretendPhoneAnimation != null)
                pretendPhoneAnimation.Stop();

            pretendPhoneAnimation = null;
            ResetPretendPhoneState();
        }

        private void ResetPretendPhoneState()
        {
            pretendPhoneActive = false;
            pretendPhoneHolding = false;
            pretendPhoneHoldAt = 0;
            nextPretendPhoneSpeechAt = 0;
            nextPretendPhoneMoveAt = 0;
            pretendPhoneWalking = false;
            lastPretendPhoneSpeechLine = "";
        }

        private PhonePropAnimation EnsurePretendPhoneAnimation()
        {
            if (pretendPhoneAnimation == null)
                pretendPhoneAnimation = new PhonePropAnimation(stalker);

            return pretendPhoneAnimation;
        }

        private void WalkCalmlyTo(Vector3 destination)
        {
            if (stalker == null || !stalker.Exists())
                return;

            try
            {
                Function.Call(
                    Hash.TASK_FOLLOW_NAV_MESH_TO_COORD,
                    stalker.Handle,
                    destination.X,
                    destination.Y,
                    destination.Z,
                    0.85f,
                    -1,
                    1.0f,
                    false,
                    0.0f
                );
            }
            catch
            {
                stalker.Task.FollowNavMeshTo(destination);
            }
        }

        private void MoveQuicklyWhenUnseen(Vector3 destination, bool playerLooking)
        {
            if (playerLooking)
            {
                WalkCalmlyTo(destination);
                return;
            }

            SprintTo(destination);
        }

        private void SprintTo(Vector3 destination)
        {
            if (stalker == null || !stalker.Exists())
                return;

            try
            {
                Function.Call(
                    Hash.TASK_FOLLOW_NAV_MESH_TO_COORD,
                    stalker.Handle,
                    destination.X,
                    destination.Y,
                    destination.Z,
                    3.0f,
                    -1,
                    1.0f,
                    false,
                    0.0f
                );
            }
            catch
            {
                stalker.Task.RunTo(destination);
            }
        }

        private Vector3 GetNearbyPretendPoint(float minDistance, float maxDistance)
        {
            float angle = (float)(rng.NextDouble() * Math.PI * 2.0);
            float distance = minDistance + (float)rng.NextDouble() * Math.Max(0.1f, maxDistance - minDistance);

            return stalker.Position + new Vector3(
                (float)Math.Cos(angle) * distance,
                (float)Math.Sin(angle) * distance,
                0f
            );
        }

        private Vector3 GetPretendPointInCurrentDirection(float minDistance, float maxDistance)
        {
            if (currentPretendDirection.Length() <= 0.01f)
                currentPretendDirection = ChoosePretendDirection();

            float distance = minDistance + (float)rng.NextDouble() * Math.Max(0.1f, maxDistance - minDistance);
            float sideDrift = ((float)rng.NextDouble() - 0.5f) * 3f;
            Vector3 side = new Vector3(-currentPretendDirection.Y, currentPretendDirection.X, 0f);

            return stalker.Position + currentPretendDirection * distance + side * sideDrift;
        }

        private class StalkerTickContext
        {
            public Ped player;
            public float distance;
            public int witnessCount;
            public bool playerLooking;
            public bool canRepath;
            public bool playerDead;

            public StalkerDecisionInput ToDecisionInput(bool attacking, bool pretending, string movementState)
            {
                return new StalkerDecisionInput
                {
                    stalkerExists = true,
                    currentlyAttacking = attacking,
                    currentlyPretending = pretending,
                    playerDead = playerDead,
                    witnessCount = witnessCount,
                    distanceToPlayer = distance,
                    playerLookingAtStalker = playerLooking,
                    canRepath = canRepath,
                    lastMovementState = movementState
                };
            }
        }
    }
}
