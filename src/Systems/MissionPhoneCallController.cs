using GTAVTrueCrimesMod.Models;
using System;
using System.Collections.Generic;

namespace GTAVTrueCrimesMod.Systems
{
    public class MissionPhoneCallController
    {
        public const int AnswerAnimationDelayMs = 900;
        public const int HangupCleanupDelayMs = 1800;

        private MissionNode node;
        private MissionAudioSegment[] audioSegments;
        private string caller;
        private bool ringing;
        private bool answered;
        private bool contentStarted;
        private bool segmentStarted;
        private bool completed;
        private bool fallbackTextShown;
        private int nextPromptAt;
        private int contentStartAt;
        private int segmentStartAt;
        private int segmentEndAt;
        private int fallbackTextAt;
        private int completeAt;
        private bool endingStarted;
        private int finishAt;
        private int currentSegmentIndex;
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
            segmentStarted = false;
            contentStartAt = nowMs + AnswerAnimationDelayMs;
            fallbackTextShown = false;
            nextCueIndex = 0;
            currentSegmentIndex = 0;
            audioSegments = BuildAudioSegments(node);
            segmentStartAt = contentStartAt;
            segmentEndAt = 0;
            int callDurationMs = GetTotalCompleteAfterMs(audioSegments);
            fallbackTextAt = 0;
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
            }

            TickAudioSegments(events, nowMs);

            if (nowMs >= completeAt)
            {
                endingStarted = true;
                finishAt = nowMs + HangupCleanupDelayMs;
                events.Add(new PhoneCallEvent(PhoneCallEvent.EndCallAnimation) { durationMs = HangupCleanupDelayMs });
            }

            return events;
        }

        public void Reset()
        {
            node = null;
            audioSegments = null;
            caller = "";
            ringing = false;
            answered = false;
            contentStarted = false;
            segmentStarted = false;
            completed = false;
            endingStarted = false;
            fallbackTextShown = false;
            nextPromptAt = 0;
            contentStartAt = 0;
            segmentStartAt = 0;
            segmentEndAt = 0;
            fallbackTextAt = 0;
            completeAt = 0;
            finishAt = 0;
            currentSegmentIndex = 0;
            nextCueIndex = 0;
        }

        private void TickAudioSegments(List<PhoneCallEvent> events, int nowMs)
        {
            if (audioSegments == null || audioSegments.Length == 0)
                return;

            while (currentSegmentIndex < audioSegments.Length)
            {
                MissionAudioSegment segment = audioSegments[currentSegmentIndex];

                if (nowMs < segmentStartAt)
                    return;

                if (!segmentStarted)
                    StartSegment(events, segment, nowMs);

                AddSubtitleCueEvents(events, segment, nowMs);
                AddFallbackTextEvent(events, segment, nowMs);

                if (nowMs < segmentEndAt)
                    return;

                int gapAfterMs = Math.Max(0, segment.gapAfterMs);
                currentSegmentIndex++;

                if (currentSegmentIndex >= audioSegments.Length)
                    return;

                segmentStartAt = segmentEndAt + gapAfterMs;
                segmentEndAt = 0;
                segmentStarted = false;
                fallbackTextShown = false;
                nextCueIndex = 0;
            }
        }

        private void StartSegment(List<PhoneCallEvent> events, MissionAudioSegment segment, int nowMs)
        {
            segmentStarted = true;
            nextCueIndex = 0;
            fallbackTextShown = false;
            segmentEndAt = segmentStartAt + GetSegmentDurationMs(segment);
            fallbackTextAt = HasSubtitleCues(segment) ? 0 : segmentStartAt + 1000;

            if (!string.IsNullOrEmpty(segment.audio))
                events.Add(new PhoneCallEvent(PhoneCallEvent.PlayAudio) { audio = segment.audio });
        }

        private void AddSubtitleCueEvents(List<PhoneCallEvent> events, MissionAudioSegment segment, int nowMs)
        {
            if (!HasSubtitleCues(segment))
                return;

            while (nextCueIndex < segment.subtitles.Length)
            {
                int elapsed = nowMs - segmentStartAt;
                MissionSubtitleCue cue = segment.subtitles[nextCueIndex];

                if (elapsed < cue.atMs)
                    return;

                events.Add(new PhoneCallEvent(PhoneCallEvent.ShowSubtitle)
                {
                    text = cue.text,
                    durationMs = cue.durationMs
                });

                nextCueIndex++;
            }
        }

        private void AddFallbackTextEvent(List<PhoneCallEvent> events, MissionAudioSegment segment, int nowMs)
        {
            if (HasSubtitleCues(segment) || fallbackTextShown || fallbackTextAt <= 0)
                return;

            if (nowMs < fallbackTextAt)
                return;

            fallbackTextShown = true;

            if (!string.IsNullOrEmpty(segment.text))
            {
                events.Add(new PhoneCallEvent(PhoneCallEvent.ShowSubtitle)
                {
                    text = segment.text,
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

        private bool HasSubtitleCues(MissionAudioSegment segment)
        {
            return segment != null && segment.subtitles != null && segment.subtitles.Length > 0;
        }

        private MissionAudioSegment[] BuildAudioSegments(MissionNode node)
        {
            if (node == null)
                return new MissionAudioSegment[0];

            if (node.audioSegments != null && node.audioSegments.Length > 0)
                return node.audioSegments;

            MissionAudioSegment segment = new MissionAudioSegment();
            segment.audio = node.audio;
            segment.text = node.text;
            segment.subtitlesFile = node.subtitlesFile;
            segment.subtitles = node.subtitles;
            segment.completeAfterMs = node.completeAfterMs;
            segment.gapAfterMs = 0;

            return new[] { segment };
        }

        private int GetTotalCompleteAfterMs(MissionAudioSegment[] segments)
        {
            if (segments == null || segments.Length == 0)
                return 8000;

            int total = 0;

            for (int i = 0; i < segments.Length; i++)
            {
                total += GetSegmentDurationMs(segments[i]);

                if (i < segments.Length - 1)
                    total += Math.Max(0, segments[i].gapAfterMs);
            }

            return Math.Max(total, 1000);
        }

        private int GetSegmentDurationMs(MissionAudioSegment segment)
        {
            if (segment == null)
                return 8000;

            if (segment.completeAfterMs > 0)
                return segment.completeAfterMs;

            if (!HasSubtitleCues(segment))
                return 8000;

            int lastEnd = 0;

            for (int i = 0; i < segment.subtitles.Length; i++)
            {
                int cueEnd = segment.subtitles[i].atMs + segment.subtitles[i].durationMs;

                if (cueEnd > lastEnd)
                    lastEnd = cueEnd;
            }

            return Math.Max(lastEnd, 1000);
        }
    }
}
