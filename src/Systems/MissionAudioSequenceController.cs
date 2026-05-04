using GTAVTrueCrimesMod.Models;
using System;
using System.Collections.Generic;

namespace GTAVTrueCrimesMod.Systems
{
    public class MissionAudioSequenceController
    {
        private MissionAudioSegment[] audioSegments;
        private bool active;
        private bool completed;
        private bool segmentStarted;
        private bool fallbackTextShown;
        private int segmentStartAt;
        private int segmentEndAt;
        private int fallbackTextAt;
        private int currentSegmentIndex;
        private int nextCueIndex;

        public bool IsActive
        {
            get { return active; }
        }

        public void Start(MissionAudioSegment[] segments, int nowMs, int startDelayMs)
        {
            Reset();

            audioSegments = segments == null ? new MissionAudioSegment[0] : segments;
            active = audioSegments.Length > 0;
            completed = false;
            segmentStartAt = nowMs + Math.Max(0, startDelayMs);
        }

        public List<PhoneCallEvent> Tick(int nowMs)
        {
            List<PhoneCallEvent> events = new List<PhoneCallEvent>();

            if (!active || completed)
                return events;

            TickAudioSegments(events, nowMs);

            if (currentSegmentIndex >= audioSegments.Length)
            {
                completed = true;
                active = false;
                events.Add(new PhoneCallEvent(PhoneCallEvent.Complete));
            }

            return events;
        }

        public void Reset()
        {
            audioSegments = null;
            active = false;
            completed = false;
            segmentStarted = false;
            fallbackTextShown = false;
            segmentStartAt = 0;
            segmentEndAt = 0;
            fallbackTextAt = 0;
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
                    StartSegment(events, segment);

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

        private void StartSegment(List<PhoneCallEvent> events, MissionAudioSegment segment)
        {
            segmentStarted = true;
            nextCueIndex = 0;
            fallbackTextShown = false;
            segmentEndAt = segmentStartAt + GetSegmentDurationMs(segment);
            fallbackTextAt = HasSubtitleCues(segment) ? 0 : segmentStartAt + 300;

            if (segment != null && !string.IsNullOrEmpty(segment.audio))
                events.Add(new PhoneCallEvent(PhoneCallEvent.PlayAudio)
                {
                    audio = segment.audio,
                    speaker = segment.speaker
                });
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

            if (segment != null && !string.IsNullOrEmpty(segment.text))
            {
                events.Add(new PhoneCallEvent(PhoneCallEvent.ShowSubtitle)
                {
                    text = segment.text,
                    durationMs = GetSegmentDurationMs(segment)
                });
            }
        }

        private bool HasSubtitleCues(MissionAudioSegment segment)
        {
            return segment != null && segment.subtitles != null && segment.subtitles.Length > 0;
        }

        private int GetSegmentDurationMs(MissionAudioSegment segment)
        {
            if (segment == null)
                return 8000;

            if (segment.completeAfterMs > 0)
                return Math.Max(segment.completeAfterMs, GetSubtitleDurationMs(segment));

            if (!HasSubtitleCues(segment))
                return 8000;

            return Math.Max(GetSubtitleDurationMs(segment), 1000);
        }

        private int GetSubtitleDurationMs(MissionAudioSegment segment)
        {
            if (!HasSubtitleCues(segment))
                return 0;

            int lastEnd = 0;

            for (int i = 0; i < segment.subtitles.Length; i++)
            {
                int cueEnd = segment.subtitles[i].atMs + segment.subtitles[i].durationMs;

                if (cueEnd > lastEnd)
                    lastEnd = cueEnd;
            }

            return lastEnd;
        }
    }
}
