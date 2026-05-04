using GTAVTrueCrimesMod.Behaviors;
using GTAVTrueCrimesMod.Models;

namespace GTAVTrueCrimesMod.Effects
{
    public class SpawnPoliceAmbushEffectHandler : IMissionEffectHandler
    {
        public bool CanHandle(MissionEffect effect)
        {
            return effect != null && effect.type == "spawn_police_ambush";
        }

        public void Apply(MissionRuntime runtime, MissionEffect effect)
        {
            runtime.AddBackgroundBehavior(
                new PoliceAmbushBehavior(effect),
                effect.GetString("lifetime", "node")
            );
        }
    }
}
