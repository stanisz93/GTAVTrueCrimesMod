using GTAVTrueCrimesMod.Models;

namespace GTAVTrueCrimesMod.NodeHandlers
{
    public class DefaultNodeHandler : IMissionNodeHandler
    {
        public bool CanHandle(MissionNode node)
        {
            return true;
        }

        public void Enter(MissionRuntime runtime, MissionNode node)
        {
            runtime.ShowDefaultNode(node);
        }
    }
}
