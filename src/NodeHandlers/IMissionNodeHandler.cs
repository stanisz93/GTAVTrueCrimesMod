using GTAVTrueCrimesMod.Models;

namespace GTAVTrueCrimesMod.NodeHandlers
{
    public interface IMissionNodeHandler
    {
        bool CanHandle(MissionNode node);
        void Enter(MissionRuntime runtime, MissionNode node);
    }
}
