namespace GTAVTrueCrimesMod.Models
{
    public class MissionNode
    {
        public string id;
        public string type;
        public string text;
        public JsonVector3 target;
        public float instructionClearDistance;
        public string completeWhen;
        public string interactionText;
        public string interactionResultText;
        public MissionAudioSegment[] interactionAudioSegments;
        public float interactionDistance;
        public string interactionAnimationDict;
        public string interactionAnimationName;
        public int interactionAnimationDurationMs;
        public int interactionAudioStartDelayMs;
        public int interactionCompleteDelayMs;
        public string setFact;
        public string next;
        public string caller;
        public string audio;
        public string subtitlesFile;
        public MissionSubtitleCue[] subtitles;
        public MissionAudioSegment[] audioSegments;
        public int delayMs;
        public int completeAfterMs;
        public MissionEffect[] onEnter;
    }
}
