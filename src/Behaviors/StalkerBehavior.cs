using GTA;
using GTA.Math;
using GTA.Native;
using GTAVTrueCrimesMod.Models;
using GTAVTrueCrimesMod.Systems;
using System;

namespace GTAVTrueCrimesMod.Behaviors
{
    public class StalkerBehavior : IMissionBackgroundBehavior
    {
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
        private readonly Random rng = new Random();

        private Ped stalker;
        private bool pretending;
        private bool attacking;
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
            attackDistance = PositiveOrDefault(config == null ? 0f : config.GetFloat("attackDistance", 25f), 25f);
            meleeDistance = PositiveOrDefault(config == null ? 0f : config.GetFloat("meleeDistance", 4f), 4f);
            followRepathMs = config == null ? 1500 : Math.Max(250, config.GetInt("followRepathMs", 1500));
            pretendDurationMs = config == null ? 5000 : Math.Max(500, config.GetInt("pretendDurationMs", 5000));
            attackDamage = config == null ? 0 : Math.Max(0, config.GetInt("attackDamage", 0));
            attackDamageIntervalMs = config == null ? 450 : Math.Max(100, config.GetInt("attackDamageIntervalMs", 450));
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
            if (stalker == null || !stalker.Exists())
            {
                state = "spawning";
                SpawnBehindPlayer();
            }

            if (stalker == null || !stalker.Exists())
            {
                state = "missing";
                return;
            }

            if (attacking)
            {
                state = "attacking";

                if (!IsPlayerIsolated(isolationRadius, maxWitnesses))
                {
                    StopAttackAndBlendIn();
                    return;
                }

                TickAttackDamage(runtime);
                return;
            }

            Ped player = Game.Player.Character;
            float distance = stalker.Position.DistanceTo(player.Position);
            int witnessCount = CountNearbyWitnesses(isolationRadius);
            bool playerIsolated = witnessCount <= maxWitnesses;

            lastDistance = distance;
            lastWitnessCount = witnessCount;
            lastPlayerIsolated = playerIsolated;
            lastAttackEnabled = attackEnabled;
            bool playerLooking = IsPlayerLookingAt(stalker, playerLookingDistance, playerLookingAngle);
            lastPlayerLooking = playerLooking;

            if (attackEnabled && playerIsolated && distance < attackDistance)
            {
                state = "attack_start";
                StartAttack();
                return;
            }

            if (attackEnabled && playerIsolated)
            {
                ApproachBeforeAttack(player, distance);
                return;
            }

            if (playerLooking && distance < playerLookingDistance)
            {
                if (!pretending)
                {
                    state = "pretend_start";
                    StartPretending();
                }

                TickPretending();
                return;
            }

            if (pretending)
            {
                StopPretendPhoneCall();
                pretending = false;
                pretendMode = 0;
                pretendUntil = 0;
                nextPretendTaskAt = 0;
                currentPretendDirection = Vector3.Zero;
                currentPretendDestination = Vector3.Zero;
            }

            if (Game.GameTime < nextFollowTaskAt)
            {
                state = lastMovementState;
                return;
            }

            nextFollowTaskAt = Game.GameTime + followRepathMs;
            Vector3 followPoint = player.Position - player.ForwardVector * followDistance;

            if (distance > runDistance)
            {
                state = "running";
                lastMovementState = state;
                stalker.Task.RunTo(followPoint);
            }
            else if (distance > walkDistance)
            {
                state = "walking";
                lastMovementState = state;
                stalker.Task.FollowNavMeshTo(followPoint);
            }
            else if (distance < tooCloseDistance)
            {
                state = "too_close";
                lastMovementState = state;
                stalker.Task.FollowNavMeshTo(player.Position - player.ForwardVector * followDistance);
            }
            else
            {
                state = "loitering";
                lastMovementState = state;
                stalker.Task.WanderAround(stalker.Position, 4f);
            }
        }

        public void Clear()
        {
            StopPretendPhoneCall();

            if (stalker != null && stalker.Exists())
            {
                stalker.Delete();
                stalker = null;
            }

            pretending = false;
            attacking = false;
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
            pretending = false;
            pretendMode = 0;
            pretendUntil = 0;
            currentPretendDirection = Vector3.Zero;
            currentPretendDestination = Vector3.Zero;

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

            if (distance > runDistance)
                stalker.Task.RunTo(approachPoint);
            else
                WalkCalmlyTo(approachPoint);
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

            string[] lines = new[]
            {
                "GENERIC_HI",
                "GENERIC_YES",
                "GENERIC_NO",
                "GENERIC_THANKS",
                "GENERIC_BYE",
                "CHAT_STATE"
            };

            try
            {
                Function.Call(
                    Hash.PLAY_PED_AMBIENT_SPEECH_NATIVE,
                    stalker.Handle,
                    lines[rng.Next(0, lines.Length)],
                    "SPEECH_PARAMS_FORCE_NORMAL_CLEAR"
                );
            }
            catch
            {
            }
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
    }
}
