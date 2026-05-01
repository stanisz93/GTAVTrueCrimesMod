using GTAVTrueCrimesMod.Models;

namespace GTAVTrueCrimesMod.Effects
{
    public interface IMissionEffectHandler
    {
        bool CanHandle(MissionEffect effect);
        void Apply(MissionRuntime runtime, MissionEffect effect);
    }
}
