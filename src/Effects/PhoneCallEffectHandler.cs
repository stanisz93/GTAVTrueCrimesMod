using GTAVTrueCrimesMod.Models;

namespace GTAVTrueCrimesMod.Effects
{
    public class PhoneCallEffectHandler : IMissionEffectHandler
    {
        public bool CanHandle(MissionEffect effect)
        {
            return effect != null && effect.type == "phone_call";
        }

        public void Apply(MissionRuntime runtime, MissionEffect effect)
        {
            int delayMs = effect.GetInt("delayMs", 0);

            if (delayMs > 0)
            {
                runtime.ScheduleSideMissionCall(effect, delayMs);
                return;
            }

            runtime.StartSideMissionCall(effect);
        }
    }
}
