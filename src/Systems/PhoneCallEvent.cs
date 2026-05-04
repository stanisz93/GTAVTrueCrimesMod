namespace GTAVTrueCrimesMod.Systems
{
    public class PhoneCallEvent
    {
        public const string ShowPrompt = "show_prompt";
        public const string ShowAnswered = "show_answered";
        public const string ShowSubtitle = "show_subtitle";
        public const string PlayRingtone = "play_ringtone";
        public const string StopRingtone = "stop_ringtone";
        public const string PlayAudio = "play_audio";
        public const string BeginCallAnimation = "begin_call_animation";
        public const string StartCallHoldAnimation = "start_call_hold_animation";
        public const string EndCallAnimation = "end_call_animation";
        public const string Complete = "complete";

        public string type;
        public string text;
        public string audio;
        public string speaker;
        public int durationMs;

        public PhoneCallEvent(string type)
        {
            this.type = type;
        }
    }
}
