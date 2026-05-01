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
            TestStalkerStartsAttackBeforePretending();
            TestStalkerAttackStopsOnlyForWitnesses();
            TestStalkerAttackDamageFlow();
            TestStalkerPretendsOnlyWhenAttackIsUnavailable();
            TestStalkerFollowMovementBands();

            if (failures > 0)
            {
                Console.WriteLine("FAILED: " + failures + " logic test(s).");
                return 1;
            }

            Console.WriteLine("OK: logic tests passed.");
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

            int answerAt = 20000;
            int contentAt = answerAt + MissionPhoneCallController.AnswerAnimationDelayMs;
            List<PhoneCallEvent> answered = phone.Answer(answerAt);
            AssertContains(answered, PhoneCallEvent.StopRingtone, "answer stops ringtone");
            AssertContains(answered, PhoneCallEvent.ShowAnswered, "answer shows answered prompt");
            AssertContains(answered, PhoneCallEvent.BeginCallAnimation, "answer starts player phone animation");
            AssertNotContains(answered, PhoneCallEvent.PlayAudio, "answer does not start mission audio before phone is raised");
            AssertNotContains(answered, PhoneCallEvent.ShowSubtitle, "answer does not show dialogue subtitles before phone is raised");

            List<PhoneCallEvent> beforePhoneRaised = phone.Tick(contentAt - 1);
            AssertNotContains(beforePhoneRaised, PhoneCallEvent.PlayAudio, "audio waits for phone raise delay");
            AssertNotContains(beforePhoneRaised, PhoneCallEvent.ShowSubtitle, "subtitles wait for phone raise delay");

            List<PhoneCallEvent> firstCue = phone.Tick(contentAt);
            AssertContains(firstCue, PhoneCallEvent.StartCallHoldAnimation, "phone switches to hold animation when audio starts");
            AssertContains(firstCue, PhoneCallEvent.PlayAudio, "audio starts when phone is raised");
            AssertSubtitle(firstCue, "Pierwsza linia.", "first subtitle appears after answer");
        }

        private static void TestNodeCompletesAfterLastSubtitleEnds()
        {
            MissionNode node = CreatePhoneNode();
            MissionPhoneCallController phone = new MissionPhoneCallController();

            phone.StartRinging(node, 0);
            int answerAt = 1000;
            int contentAt = answerAt + MissionPhoneCallController.AnswerAnimationDelayMs;
            int firstCueEndAt = contentAt + 1600;
            int lastCueEndAt = contentAt + 2800;
            int hangupEndAt = lastCueEndAt + 900;
            phone.Answer(answerAt);

            AssertNotContains(phone.Tick(firstCueEndAt - 1), PhoneCallEvent.Complete, "node is not complete before first cue ends");
            phone.Tick(firstCueEndAt);
            AssertNotContains(phone.Tick(lastCueEndAt - 1), PhoneCallEvent.Complete, "node is not complete before last cue ends");

            List<PhoneCallEvent> ending = phone.Tick(lastCueEndAt);
            AssertContains(ending, PhoneCallEvent.EndCallAnimation, "node ends player phone animation after last subtitle end");
            AssertNotContains(ending, PhoneCallEvent.Complete, "node waits for phone hangup before completing");

            AssertNotContains(phone.Tick(hangupEndAt - 1), PhoneCallEvent.Complete, "node is not complete before phone hangup finishes");
            AssertContains(phone.Tick(hangupEndAt), PhoneCallEvent.Complete, "node completes after phone hangup finishes");
        }

        private static void TestCompleteAfterOverride()
        {
            MissionNode node = CreatePhoneNode();
            node.completeAfterMs = 9000;

            MissionPhoneCallController phone = new MissionPhoneCallController();
            phone.StartRinging(node, 0);
            int answerAt = 100;
            int completeAt = answerAt + MissionPhoneCallController.AnswerAnimationDelayMs + node.completeAfterMs;
            int hangupEndAt = completeAt + 900;
            phone.Answer(answerAt);

            AssertNotContains(phone.Tick(completeAt - 1), PhoneCallEvent.Complete, "completeAfterMs override waits until configured time");
            List<PhoneCallEvent> ending = phone.Tick(completeAt);
            AssertContains(ending, PhoneCallEvent.EndCallAnimation, "completeAfterMs override starts phone hangup at configured time");
            AssertNotContains(ending, PhoneCallEvent.Complete, "completeAfterMs override waits for phone hangup before completing");
            AssertContains(phone.Tick(hangupEndAt), PhoneCallEvent.Complete, "completeAfterMs override completes after phone hangup");
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

        private static void TestStalkerStartsAttackBeforePretending()
        {
            StalkerDecision decision = StalkerDecisionModel.Decide(CreateStalkerConfig(), new StalkerDecisionInput
            {
                stalkerExists = true,
                currentlyAttacking = false,
                witnessCount = 0,
                distanceToPlayer = 10f,
                playerLookingAtStalker = true,
                canRepath = true
            });

            AssertEqual(StalkerDecision.StartAttack, decision.action, "isolated player in attack distance triggers attack before pretend");
        }

        private static void TestStalkerAttackStopsOnlyForWitnesses()
        {
            StalkerDecision lookingWhileAttacking = StalkerDecisionModel.Decide(CreateStalkerConfig(), new StalkerDecisionInput
            {
                stalkerExists = true,
                currentlyAttacking = true,
                witnessCount = 0,
                distanceToPlayer = 10f,
                playerLookingAtStalker = true,
                canRepath = true
            });

            AssertEqual(StalkerDecision.ContinueAttackApproach, lookingWhileAttacking.action, "looking at attacking stalker does not stop attack");

            StalkerDecision witnessesArrive = StalkerDecisionModel.Decide(CreateStalkerConfig(), new StalkerDecisionInput
            {
                stalkerExists = true,
                currentlyAttacking = true,
                witnessCount = 1,
                distanceToPlayer = 3f,
                playerLookingAtStalker = false,
                canRepath = true
            });

            AssertEqual(StalkerDecision.AbortAttackWitnesses, witnessesArrive.action, "witnesses stop active attack");
        }

        private static void TestStalkerAttackDamageFlow()
        {
            StalkerDecision approach = StalkerDecisionModel.Decide(CreateStalkerConfig(), new StalkerDecisionInput
            {
                stalkerExists = true,
                currentlyAttacking = true,
                witnessCount = 0,
                distanceToPlayer = 8f,
                canRepath = true
            });

            AssertEqual(StalkerDecision.ContinueAttackApproach, approach.action, "attacking stalker approaches before melee range");

            StalkerDecision damage = StalkerDecisionModel.Decide(CreateStalkerConfig(), new StalkerDecisionInput
            {
                stalkerExists = true,
                currentlyAttacking = true,
                witnessCount = 0,
                distanceToPlayer = 2f,
                canRepath = true
            });

            AssertEqual(StalkerDecision.ApplyAttackDamage, damage.action, "attacking stalker applies damage in melee range");

            StalkerDecision killed = StalkerDecisionModel.Decide(CreateStalkerConfig(), new StalkerDecisionInput
            {
                stalkerExists = true,
                currentlyAttacking = true,
                playerDead = true,
                witnessCount = 0,
                distanceToPlayer = 2f,
                canRepath = true
            });

            AssertEqual(StalkerDecision.FailPlayerKilled, killed.action, "dead player fails mission during attack");
        }

        private static void TestStalkerPretendsOnlyWhenAttackIsUnavailable()
        {
            StalkerDecision notIsolated = StalkerDecisionModel.Decide(CreateStalkerConfig(), new StalkerDecisionInput
            {
                stalkerExists = true,
                currentlyAttacking = false,
                witnessCount = 2,
                distanceToPlayer = 10f,
                playerLookingAtStalker = true,
                canRepath = true
            });

            AssertEqual(StalkerDecision.Pretend, notIsolated.action, "stalker pretends when player sees him but witnesses block attack");

            StalkerDecision isolatedButTooFarForKnife = StalkerDecisionModel.Decide(CreateStalkerConfig(), new StalkerDecisionInput
            {
                stalkerExists = true,
                currentlyAttacking = false,
                witnessCount = 0,
                distanceToPlayer = 30f,
                playerLookingAtStalker = true,
                canRepath = true
            });

            AssertEqual(StalkerDecision.ApproachAttack, isolatedButTooFarForKnife.action, "isolated stalker approaches attack even when player sees him outside knife distance");
        }

        private static void TestStalkerFollowMovementBands()
        {
            AssertEqual(StalkerDecision.RunFollow, DecideFollowAt(60f), "far stalker runs to follow point");
            AssertEqual(StalkerDecision.WalkFollow, DecideFollowAt(20f), "mid distance stalker walks to follow point");
            AssertEqual(StalkerDecision.MoveAwayTooClose, DecideFollowAt(4f), "too close stalker moves away");
            AssertEqual(StalkerDecision.Loiter, DecideFollowAt(10f), "comfortable distance stalker loiters");

            StalkerDecision waiting = StalkerDecisionModel.Decide(CreateStalkerConfig(), new StalkerDecisionInput
            {
                stalkerExists = true,
                currentlyAttacking = false,
                witnessCount = 2,
                distanceToPlayer = 60f,
                playerLookingAtStalker = false,
                canRepath = false
            });

            AssertEqual(StalkerDecision.KeepMovement, waiting.action, "stalker keeps movement while waiting for repath");
        }

        private static string DecideFollowAt(float distance)
        {
            StalkerDecision decision = StalkerDecisionModel.Decide(CreateStalkerConfig(), new StalkerDecisionInput
            {
                stalkerExists = true,
                currentlyAttacking = false,
                witnessCount = 2,
                distanceToPlayer = distance,
                playerLookingAtStalker = false,
                canRepath = true
            });

            return decision.action;
        }

        private static StalkerDecisionConfig CreateStalkerConfig()
        {
            return new StalkerDecisionConfig
            {
                attackEnabled = true,
                maxWitnesses = 0,
                attackDistance = 12f,
                meleeDistance = 4f,
                playerLookingDistance = 45f,
                runDistance = 45f,
                walkDistance = 14f,
                tooCloseDistance = 8f,
                attackDamageEnabled = true
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

        private static void AssertEqual(string expected, string actual, string message)
        {
            if (expected == actual)
                return;

            Fail(message + " | expected: " + expected + ", actual: " + actual);
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
