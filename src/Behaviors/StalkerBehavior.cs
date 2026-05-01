using GTA;
using GTA.Math;
using GTA.Native;
using GTAVTrueCrimesMod.Models;
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
                    float distance = stalker.Position.DistanceTo(Game.Player.Character.Position);
                    distanceText = distance.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + "m";
                }

                return "stalker[" + id + "] state=" + state + " dist=" + distanceText;
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
            bool playerLooking = IsPlayerLookingAt(stalker, playerLookingDistance, playerLookingAngle);

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
                pretending = false;
                pretendMode = 0;
                pretendUntil = 0;
                nextPretendTaskAt = 0;
            }

            if (attackEnabled && IsPlayerIsolated(isolationRadius, maxWitnesses) && distance < attackDistance)
            {
                state = "attack_start";
                StartAttack();
                return;
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
            pretendMode = rng.Next(0, 4);
            nextPretendTaskAt = 0;
            stalker.Task.ClearAll();
            TickPretending();
        }

        private void TickPretending()
        {
            if (stalker == null || !stalker.Exists())
                return;

            if (Game.GameTime < nextPretendTaskAt)
                return;

            nextPretendTaskAt = Game.GameTime + 3000;

            if (pretendMode == 0)
                stalker.Task.WanderAround(stalker.Position, 8f);
            else if (pretendMode == 1)
                stalker.Task.FollowNavMeshTo(stalker.Position + stalker.RightVector * 4f);
            else if (pretendMode == 2)
                stalker.Task.UseMobilePhone(5000);
            else
                stalker.Task.StandStill(3000);
        }

        private void StartAttack()
        {
            if (attacking || stalker == null || !stalker.Exists())
                return;

            attacking = true;
            state = "attacking";
            lastMovementState = "attacking";
            stalker.Task.ClearAll();
            stalker.Weapons.Give(WeaponHash.Knife, 1, true, true);
            stalker.Task.Combat(Game.Player.Character);
            nextDamageAt = Game.GameTime + 250;
            GTA.UI.Screen.ShowSubtitle("Ktos rusza na ciebie z nozem.", 4000);
        }

        private void StopAttackAndBlendIn()
        {
            if (stalker == null || !stalker.Exists())
                return;

            attacking = false;
            nextDamageAt = 0;
            stalker.Task.ClearAll();
            stalker.Weapons.Remove(WeaponHash.Knife);

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
            Ped player = Game.Player.Character;
            Ped[] nearby = World.GetNearbyPeds(player, radius);
            int count = 0;

            foreach (Ped ped in nearby)
            {
                if (ped == null || !ped.Exists())
                    continue;

                if (ped == player || ped == stalker)
                    continue;

                if (ped.IsDead)
                    continue;

                count++;
            }

            return count <= maxWitnesses;
        }
    }
}
