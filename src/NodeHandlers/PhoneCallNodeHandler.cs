using GTAVTrueCrimesMod.Models;

namespace GTAVTrueCrimesMod.NodeHandlers
{
    public class PhoneCallNodeHandler : IMissionNodeHandler
    {
        public bool CanHandle(MissionNode node)
        {
            return node != null && node.type == "phone_call";
        }

        public void Enter(MissionRuntime runtime, MissionNode node)
        {
            runtime.StartIncomingMissionCall(node);
        }
    }
}
