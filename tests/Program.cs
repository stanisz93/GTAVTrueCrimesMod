using GTAVTrueCrimesMod.Models;
using GTAVTrueCrimesMod.Systems;
using System;
using System.Collections.Generic;

namespace GTAVTrueCrimesMod.Tests
{
    internal static class Program
    {
        private static int failures;

        private static int Main()
        {
            TestSubtitlesWaitUntilPhoneIsAnswered();
            TestNodeCompletesAfterLastSubtitleEnds();
            TestCompleteAfterOverride();

            if (failures > 0)
            {
                Console.WriteLine("FAILED: " + failures + " phone logic test(s).");
                return 1;
            }

            Console.WriteLine("OK: phone logic tests passed.");
            return 0;
        }

        private static void TestSubtitlesWaitUntilPhoneIsAnswered()
        {
            MissionNode node = CreatePhoneNode();
            MissionPhoneCallController phone = new MissionPhoneCallController();

            List<PhoneCallEvent> started = phone.StartRinging(node, 10000);
            AssertContains(started, PhoneCallEvent.ShowPrompt, "ringing starts with pickup prompt");
            AssertContains(started, PhoneCallEvent.PlayRingtone, "ringing starts native ringtone");
            AssertNotContains(started, PhoneCallEvent.ShowSubtitle, "ringing start does not show dialogue subtitles");

            List<PhoneCallEvent> beforeAnswer = phone.Tick(18000);
            AssertNotContains(beforeAnswer, PhoneCallEvent.ShowSubtitle, "ringing tick does not show dialogue subtitles");
            AssertNotContains(beforeAnswer, PhoneCallEvent.Complete, "ringing tick does not complete node");

            List<PhoneCallEvent> answered = phone.Answer(20000);
            AssertContains(answered, PhoneCallEvent.StopRingtone, "answer stops ringtone");
            AssertContains(answered, PhoneCallEvent.ShowAnswered, "answer shows answered prompt");
            AssertContains(answered, PhoneCallEvent.PlayAudio, "answer starts mission audio");

            List<PhoneCallEvent> firstCue = phone.Tick(20000);
            AssertSubtitle(firstCue, "Pierwsza linia.", "first subtitle appears after answer");
        }

        private static void TestNodeCompletesAfterLastSubtitleEnds()
        {
            MissionNode node = CreatePhoneNode();
            MissionPhoneCallController phone = new MissionPhoneCallController();

            phone.StartRinging(node, 0);
            phone.Answer(1000);

            AssertNotContains(phone.Tick(2599), PhoneCallEvent.Complete, "node is not complete before first cue ends");
            phone.Tick(2600);
            AssertNotContains(phone.Tick(3799), PhoneCallEvent.Complete, "node is not complete before last cue ends");

            List<PhoneCallEvent> completed = phone.Tick(3800);
            AssertContains(completed, PhoneCallEvent.Complete, "node completes after last subtitle end");
        }

        private static void TestCompleteAfterOverride()
        {
            MissionNode node = CreatePhoneNode();
            node.completeAfterMs = 9000;

            MissionPhoneCallController phone = new MissionPhoneCallController();
            phone.StartRinging(node, 0);
            phone.Answer(100);

            AssertNotContains(phone.Tick(9099), PhoneCallEvent.Complete, "completeAfterMs override waits until configured time");
            AssertContains(phone.Tick(9100), PhoneCallEvent.Complete, "completeAfterMs override completes at configured time");
        }

        private static MissionNode CreatePhoneNode()
        {
            return new MissionNode
            {
                type = "phone_call",
                caller = "Morgan",
                audio = "morgan_warning.wav",
                text = "Fallback text",
                subtitles = new[]
                {
                    new MissionSubtitleCue { atMs = 0, durationMs = 1600, text = "Pierwsza linia." },
                    new MissionSubtitleCue { atMs = 1700, durationMs = 1100, text = "Druga linia." }
                }
            };
        }

        private static void AssertContains(List<PhoneCallEvent> events, string type, string message)
        {
            if (Contains(events, type))
                return;

            Fail(message + " | missing event: " + type);
        }

        private static void AssertNotContains(List<PhoneCallEvent> events, string type, string message)
        {
            if (!Contains(events, type))
                return;

            Fail(message + " | unexpected event: " + type);
        }

        private static void AssertSubtitle(List<PhoneCallEvent> events, string text, string message)
        {
            for (int i = 0; i < events.Count; i++)
            {
                if (events[i].type == PhoneCallEvent.ShowSubtitle && events[i].text == text)
                    return;
            }

            Fail(message + " | missing subtitle: " + text);
        }

        private static bool Contains(List<PhoneCallEvent> events, string type)
        {
            for (int i = 0; i < events.Count; i++)
            {
                if (events[i].type == type)
                    return true;
            }

            return false;
        }

        private static void Fail(string message)
        {
            failures++;
            Console.WriteLine("FAIL: " + message);
        }
    }
}
