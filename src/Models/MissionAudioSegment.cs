namespace GTAVTrueCrimesMod.Models
{
    public class MissionAudioSegment
    {
        public string audio;
        public string text;
        public string subtitlesFile;
        public MissionSubtitleCue[] subtitles;
        public int completeAfterMs;
        public int gapAfterMs;
    }
}
