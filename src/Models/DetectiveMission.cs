namespace GTAVTrueCrimesMod.Models
{
    public class DetectiveMission
    {
        public string id;
        public string title;
        public string description;
        public JsonVector3 startLocation;
        public string firstObjective;
        public string firstNode;
        public string debugStartNode;
        public MissionNode[] nodes;
        public SuspectData[] suspects;
        public ObjectiveData[] objectives;

        public string sourceFile;
    }
}
