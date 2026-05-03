using GTAVTrueCrimesMod.Behaviors;
using GTAVTrueCrimesMod.Models;

namespace GTAVTrueCrimesMod.Effects
{
    public class ScriptedStalkerShotEffectHandler : IMissionEffectHandler
    {
        public bool CanHandle(MissionEffect effect)
        {
            return effect != null && effect.type == "scripted_stalker_shot";
        }

        public void Apply(MissionRuntime runtime, MissionEffect effect)
        {
            runtime.AddBackgroundBehavior(
                new ScriptedStalkerShotBehavior(effect),
                effect.GetString("lifetime", "node")
            );
        }
    }
}
