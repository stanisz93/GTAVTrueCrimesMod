using GTAVTrueCrimesMod.Models;
using System;
using System.Collections.Generic;

namespace GTAVTrueCrimesMod.Systems
{
    public class MissionPhoneCallController
    {
        public const int AnswerAnimationDelayMs = 900;

        private MissionNode node;
        private string caller;
        private bool ringing;
        private bool answered;
        private bool contentStarted;
        private bool completed;
        private bool fallbackTextShown;
        private int nextPromptAt;
        private int contentStartAt;
        private int fallbackTextAt;
        private int completeAt;
        private bool endingStarted;
        private int finishAt;
        private int nextCueIndex;

        public bool IsRinging
        {
            get { return ringing; }
        }

        public List<PhoneCallEvent> StartRinging(MissionNode node, int nowMs)
        {
            Reset();

            this.node = node;
            caller = node == null || string.IsNullOrEmpty(node.caller) ? "Nieznany numer" : node.caller;
            ringing = true;
            nextPromptAt = nowMs + 2000;

            List<PhoneCallEvent> events = new List<PhoneCallEvent>();
            events.Add(CreatePromptEvent(5000));
            events.Add(new PhoneCallEvent(PhoneCallEvent.PlayRingtone));
            return events;
        }

        public List<PhoneCallEvent> Answer(int nowMs)
        {
            List<PhoneCallEvent> events = new List<PhoneCallEvent>();

            if (!ringing || node == null)
                return events;

            ringing = false;
            answered = true;
            contentStarted = false;
            contentStartAt = nowMs + AnswerAnimationDelayMs;
            fallbackTextShown = false;
            nextCueIndex = 0;
            int callDurationMs = GetCompleteAfterMs(node);
            fallbackTextAt = HasSubtitleCues(node) ? 0 : contentStartAt + 1000;
            completeAt = contentStartAt + callDurationMs;

            events.Add(new PhoneCallEvent(PhoneCallEvent.StopRingtone));
            events.Add(new PhoneCallEvent(PhoneCallEvent.ShowAnswered) { text = "Polaczenie odebrane.", durationMs = 1200 });
            events.Add(new PhoneCallEvent(PhoneCallEvent.BeginCallAnimation) { durationMs = AnswerAnimationDelayMs + callDurationMs + 1000 });

            return events;
        }

        public List<PhoneCallEvent> Tick(int nowMs)
        {
            List<PhoneCallEvent> events = new List<PhoneCallEvent>();

            if (ringing)
            {
                if (nowMs >= nextPromptAt)
                {
                    nextPromptAt = nowMs + 2000;
                    events.Add(CreatePromptEvent(2500));
                }

                return events;
            }

            if (!answered || node == null || completed)
                return events;

            if (endingStarted)
            {
                if (nowMs >= finishAt)
                {
                    completed = true;
                    events.Add(new PhoneCallEvent(PhoneCallEvent.Complete));
                }

                return events;
            }

            if (!contentStarted)
            {
                if (nowMs < contentStartAt)
                    return events;

                contentStarted = true;
                events.Add(new PhoneCallEvent(PhoneCallEvent.StartCallHoldAnimation) { durationMs = completeAt - nowMs + 1000 });

                if (!string.IsNullOrEmpty(node.audio))
                    events.Add(new PhoneCallEvent(PhoneCallEvent.PlayAudio) { audio = node.audio });
            }

            AddSubtitleCueEvents(events, nowMs);
            AddFallbackTextEvent(events, nowMs);

            if (nowMs >= completeAt)
            {
                endingStarted = true;
                finishAt = nowMs + 900;
                events.Add(new PhoneCallEvent(PhoneCallEvent.EndCallAnimation) { durationMs = 900 });
            }

            return events;
        }

        public void Reset()
        {
            node = null;
            caller = "";
            ringing = false;
            answered = false;
            contentStarted = false;
            completed = false;
            endingStarted = false;
            fallbackTextShown = false;
            nextPromptAt = 0;
            contentStartAt = 0;
            fallbackTextAt = 0;
            completeAt = 0;
            finishAt = 0;
            nextCueIndex = 0;
        }

        private void AddSubtitleCueEvents(List<PhoneCallEvent> events, int nowMs)
        {
            if (!HasSubtitleCues(node))
                return;

            if (nextCueIndex >= node.subtitles.Length)
                return;

            int elapsed = nowMs - contentStartAt;
            MissionSubtitleCue cue = node.subtitles[nextCueIndex];

            if (elapsed < cue.atMs)
                return;

            events.Add(new PhoneCallEvent(PhoneCallEvent.ShowSubtitle)
            {
                text = cue.text,
                durationMs = cue.durationMs
            });

            nextCueIndex++;
        }

        private void AddFallbackTextEvent(List<PhoneCallEvent> events, int nowMs)
        {
            if (HasSubtitleCues(node) || fallbackTextShown || fallbackTextAt <= 0)
                return;

            if (nowMs < fallbackTextAt)
                return;

            fallbackTextShown = true;

            if (!string.IsNullOrEmpty(node.text))
            {
                events.Add(new PhoneCallEvent(PhoneCallEvent.ShowSubtitle)
                {
                    text = node.text,
                    durationMs = 7000
                });
            }
        }

        private PhoneCallEvent CreatePromptEvent(int durationMs)
        {
            return new PhoneCallEvent(PhoneCallEvent.ShowPrompt)
            {
                text = "Dzwoni: " + caller + " | Enter - odbierz",
                durationMs = durationMs
            };
        }

        private bool HasSubtitleCues(MissionNode node)
        {
            return node != null && node.subtitles != null && node.subtitles.Length > 0;
        }

        private int GetCompleteAfterMs(MissionNode node)
        {
            if (node == null)
                return 8000;

            if (node.completeAfterMs > 0)
                return node.completeAfterMs;

            if (!HasSubtitleCues(node))
                return 8000;

            int lastEnd = 0;

            for (int i = 0; i < node.subtitles.Length; i++)
            {
                int cueEnd = node.subtitles[i].atMs + node.subtitles[i].durationMs;

                if (cueEnd > lastEnd)
                    lastEnd = cueEnd;
            }

            return Math.Max(lastEnd, 1000);
        }
    }
}
