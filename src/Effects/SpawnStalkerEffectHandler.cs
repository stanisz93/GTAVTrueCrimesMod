using GTAVTrueCrimesMod.Behaviors;
using GTAVTrueCrimesMod.Models;

namespace GTAVTrueCrimesMod.Effects
{
    public class SpawnStalkerEffectHandler : IMissionEffectHandler
    {
        public bool CanHandle(MissionEffect effect)
        {
            return effect != null && effect.type == "spawn_stalker";
        }

        public void Apply(MissionRuntime runtime, MissionEffect effect)
        {
            runtime.AddBackgroundBehavior(new StalkerBehavior(
                effect
            ));
        }
    }
}
