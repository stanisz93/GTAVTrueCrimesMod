namespace GTAVTrueCrimesMod.Systems
{
    public class StalkerDecisionConfig
    {
        public bool attackEnabled;
        public int maxWitnesses;
        public float attackDistance;
        public float meleeDistance;
        public float playerLookingDistance;
        public float runDistance;
        public float walkDistance;
        public float tooCloseDistance;
        public bool attackDamageEnabled;
    }

    public class StalkerDecisionInput
    {
        public bool stalkerExists;
        public bool currentlyAttacking;
        public bool currentlyPretending;
        public bool playerDead;
        public int witnessCount;
        public float distanceToPlayer;
        public bool playerLookingAtStalker;
        public bool canRepath;
        public string lastMovementState;
    }

    public class StalkerDecision
    {
        public const string Spawn = "spawn";
        public const string Missing = "missing";
        public const string AbortAttackWitnesses = "abort_attack_witnesses";
        public const string FailPlayerKilled = "fail_player_killed";
        public const string ContinueAttackApproach = "continue_attack_approach";
        public const string ContinueAttackCombat = "continue_attack_combat";
        public const string ApplyAttackDamage = "apply_attack_damage";
        public const string StartAttack = "start_attack";
        public const string ApproachAttack = "approach_attack";
        public const string Pretend = "pretend";
        public const string KeepMovement = "keep_movement";
        public const string RunFollow = "run_follow";
        public const string WalkFollow = "walk_follow";
        public const string MoveAwayTooClose = "move_away_too_close";
        public const string Loiter = "loiter";

        public string action;
        public bool isPlayerIsolated;
        public bool shouldStopPretending;
    }

    public static class StalkerDecisionModel
    {
        public static StalkerDecision Decide(StalkerDecisionConfig config, StalkerDecisionInput input)
        {
            StalkerDecision decision = new StalkerDecision();

            if (config == null)
                config = new StalkerDecisionConfig();

            if (input == null)
            {
                decision.action = StalkerDecision.Missing;
                return decision;
            }

            decision.isPlayerIsolated = input.witnessCount <= config.maxWitnesses;

            if (!input.stalkerExists)
            {
                decision.action = StalkerDecision.Spawn;
                return decision;
            }

            if (input.currentlyAttacking)
            {
                if (!decision.isPlayerIsolated)
                {
                    decision.action = StalkerDecision.AbortAttackWitnesses;
                    return decision;
                }

                if (input.playerDead)
                {
                    decision.action = StalkerDecision.FailPlayerKilled;
                    return decision;
                }

                if (input.distanceToPlayer > config.meleeDistance)
                {
                    decision.action = StalkerDecision.ContinueAttackApproach;
                    return decision;
                }

                decision.action = config.attackDamageEnabled
                    ? StalkerDecision.ApplyAttackDamage
                    : StalkerDecision.ContinueAttackCombat;
                return decision;
            }

            if (config.attackEnabled && decision.isPlayerIsolated)
            {
                decision.shouldStopPretending = input.currentlyPretending;

                if (input.distanceToPlayer < config.attackDistance)
                    decision.action = StalkerDecision.StartAttack;
                else
                    decision.action = StalkerDecision.ApproachAttack;

                return decision;
            }

            if (input.playerLookingAtStalker && input.distanceToPlayer < config.playerLookingDistance)
            {
                decision.action = StalkerDecision.Pretend;
                return decision;
            }

            if (input.currentlyPretending)
                decision.shouldStopPretending = true;

            if (!input.canRepath)
            {
                decision.action = StalkerDecision.KeepMovement;
                return decision;
            }

            if (input.distanceToPlayer > config.runDistance)
            {
                decision.action = StalkerDecision.RunFollow;
                return decision;
            }

            if (input.distanceToPlayer > config.walkDistance)
            {
                decision.action = StalkerDecision.WalkFollow;
                return decision;
            }

            if (input.distanceToPlayer < config.tooCloseDistance)
            {
                decision.action = StalkerDecision.MoveAwayTooClose;
                return decision;
            }

            decision.action = StalkerDecision.Loiter;
            return decision;
        }
    }
}
