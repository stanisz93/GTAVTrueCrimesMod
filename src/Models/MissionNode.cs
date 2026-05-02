namespace GTAVTrueCrimesMod.Models
{
    public class MissionNode
    {
        public string id;
        public string type;
        public string text;
        public JsonVector3 target;
        public string completeWhen;
        public string setFact;
        public string next;
        public string caller;
        public string audio;
        public string subtitlesFile;
        public MissionSubtitleCue[] subtitles;
        public int completeAfterMs;
        public MissionEffect[] onEnter;
    }
}
