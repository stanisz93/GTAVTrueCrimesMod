using GTA;
using GTAVTrueCrimesMod.Models;
using System;

namespace GTAVTrueCrimesMod.Behaviors
{
    public class ScriptedStalkerShotBehavior : IMissionBackgroundBehavior
    {
        private readonly string id;
        private readonly string targetBehaviorId;
        private readonly float triggerDistance;
        private readonly bool requireTargetNearNodeTarget;
        private readonly float targetMaxDistanceFromNodeTarget;
        private readonly int delayMs;
        private readonly int shotCount;
        private readonly int shotGapMs;
        private readonly int damage;

        private bool triggered;
        private bool done;
        private int fireAt;
        private string state = "waiting_for_target";

        public ScriptedStalkerShotBehavior(MissionEffect config)
        {
            string configuredId = config == null ? "" : config.GetString("id", "");
            targetBehaviorId = config == null ? "" : config.GetString("targetBehaviorId", "");

            if (string.IsNullOrEmpty(targetBehaviorId))
                targetBehaviorId = config == null ? "" : config.GetString("targetId", "");

            if (string.IsNullOrEmpty(targetBehaviorId))
                targetBehaviorId = "main_stalker";

            id = string.IsNullOrEmpty(configuredId)
                ? "scripted_stalker_shot_" + targetBehaviorId
                : configuredId;

            triggerDistance = PositiveOrDefault(config == null ? 0f : config.GetFloat("triggerDistance", 8f), 8f);
            requireTargetNearNodeTarget = config != null && config.GetBool("requireTargetNearNodeTarget", false);
            targetMaxDistanceFromNodeTarget = PositiveOrDefault(config == null ? 0f : config.GetFloat("targetMaxDistanceFromNodeTarget", 12f), 12f);
            delayMs = config == null ? 700 : Math.Max(0, config.GetInt("delayMs", 700));
            shotCount = config == null ? 2 : Math.Max(1, config.GetInt("shotCount", 2));
            shotGapMs = config == null ? 250 : Math.Max(0, config.GetInt("shotGapMs", 250));
            damage = config == null ? 500 : Math.Max(1, config.GetInt("damage", 500));
        }

        public string Id
        {
            get { return id; }
        }

        public string DebugText
        {
            get
            {
                return "scripted_stalker_shot[" + id + "] state=" + state + " target=" + targetBehaviorId;
            }
        }

        public void Tick(MissionRuntime runtime)
        {
            if (done || runtime == null)
                return;

            if (!triggered)
            {
                if (!runtime.IsPlayerNearCurrentNodeTarget(triggerDistance))
                    return;

                if (requireTargetNearNodeTarget &&
                    !runtime.IsScriptedKillTargetNearCurrentNodeTarget(targetBehaviorId, targetMaxDistanceFromNodeTarget))
                {
                    state = "waiting_for_stalker";
                    return;
                }

                triggered = true;
                fireAt = Game.GameTime + delayMs;
                state = "armed";
                return;
            }

            if (Game.GameTime < fireAt)
                return;

            bool started = runtime.TryBeginScriptedKillByOther(targetBehaviorId, shotCount, shotGapMs, damage);

            if (!started)
            {
                state = "missing_target_retry";
                fireAt = Game.GameTime + 500;
                return;
            }

            state = "fired";
            done = true;
        }

        public void Clear()
        {
            done = true;
            state = "cleared";
        }

        private float PositiveOrDefault(float value, float fallback)
        {
            return value > 0f ? value : fallback;
        }
    }
}
