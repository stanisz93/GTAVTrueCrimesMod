using GTAVTrueCrimesMod.Models;

namespace GTAVTrueCrimesMod.Effects
{
    public class SetFactEffectHandler : IMissionEffectHandler
    {
        public bool CanHandle(MissionEffect effect)
        {
            return effect != null && effect.type == "set_fact";
        }

        public void Apply(MissionRuntime runtime, MissionEffect effect)
        {
            string fact = effect.GetString("fact", effect.id);

            if (string.IsNullOrEmpty(fact))
                return;

            runtime.SetFact(fact, effect.GetBool("value", true));
        }
    }
}
