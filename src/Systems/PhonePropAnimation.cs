using GTA;
using GTA.Math;
using GTA.Native;
using System;

namespace GTAVTrueCrimesMod.Systems
{
    public class PhonePropAnimation
    {
        private const string AnimDict = "cellphone@";
        private const string HoldAnim = "cellphone_call_listen_base";
        private const int RightHandBone = 28422;

        private readonly Ped ped;
        private Prop phoneProp;
        private bool holding;
        private int holdUntil;
        private bool finishing;
        private int deleteAt;

        public PhonePropAnimation(Ped ped)
        {
            this.ped = ped;
        }

        public bool Active
        {
            get { return phoneProp != null && phoneProp.Exists(); }
        }

        public void BeginPickup(int durationMs)
        {
            if (!CanAnimate())
                return;

            finishing = false;
            deleteAt = 0;
            EnsurePhoneProp();
            StartHoldInternal(durationMs, true);
        }

        public void StartHold(int durationMs)
        {
            if (!CanAnimate())
                return;

            finishing = false;
            deleteAt = 0;
            EnsurePhoneProp();
            StartHoldInternal(durationMs, false);
        }

        public void Finish(int nowMs, int durationMs)
        {
            try
            {
                if (!CanAnimate())
                {
                    Stop();
                    return;
                }

                int outDuration = Math.Max(500, durationMs);
                EnsurePhoneProp();
                holding = false;
                holdUntil = 0;
                ClearSecondaryTask();
                finishing = true;
                deleteAt = nowMs + outDuration;
            }
            catch
            {
                Stop();
            }
        }

        public void Tick(int nowMs)
        {
            if (holding && !finishing)
                TickHold(nowMs);

            if (!finishing || deleteAt <= 0)
                return;

            if (nowMs < deleteAt)
                return;

            DeletePhoneProp();
            holding = false;
            holdUntil = 0;
            finishing = false;
            deleteAt = 0;
        }

        public void Stop()
        {
            if (ped != null && ped.Exists())
            {
                ClearSecondaryTask();
            }

            DeletePhoneProp();
            holding = false;
            holdUntil = 0;
            finishing = false;
            deleteAt = 0;
        }

        private bool CanAnimate()
        {
            return ped != null && ped.Exists();
        }

        private void EnsurePhoneProp()
        {
            if (phoneProp != null && phoneProp.Exists())
                return;

            phoneProp = null;

            Model model = CreateLoadedPhoneModel();

            if (!model.IsLoaded)
                return;

            phoneProp = World.CreateProp(model, GetPhoneSpawnPosition(), false, false);
            model.MarkAsNoLongerNeeded();

            if (phoneProp == null || !phoneProp.Exists())
                return;

            phoneProp.IsPersistent = true;

            try
            {
                Function.Call(Hash.SET_ENTITY_COLLISION, phoneProp.Handle, false, false);
            }
            catch
            {
            }

            AttachPhoneToHand();
        }

        private Model CreateLoadedPhoneModel()
        {
            string[] modelNames = new[]
            {
                "prop_npc_phone_02",
                "prop_player_phone_01",
                "prop_player_phone_02",
                "prop_phone_ing",
                "prop_amb_phone",
                "prop_cs_phone_01"
            };

            for (int i = 0; i < modelNames.Length; i++)
            {
                Model model = new Model(modelNames[i]);

                if (!model.IsValid)
                    continue;

                if (model.Request(500))
                    return model;
            }

            return new Model("prop_npc_phone_02");
        }

        private void AttachPhoneToHand()
        {
            if (phoneProp == null || !phoneProp.Exists() || !CanAnimate())
                return;

            try
            {
                int boneIndex = Function.Call<int>(Hash.GET_PED_BONE_INDEX, ped.Handle, RightHandBone);

                Function.Call(
                    Hash.ATTACH_ENTITY_TO_ENTITY,
                    phoneProp.Handle,
                    ped.Handle,
                    boneIndex,
                    0.0f,
                    0.0f,
                    0.0f,
                    0.0f,
                    0.0f,
                    0.0f,
                    true,
                    true,
                    false,
                    false,
                    2,
                    true
                );
            }
            catch
            {
                try
                {
                    phoneProp.AttachTo(ped.Bones[Bone.PHRightHand], Vector3.Zero, Vector3.Zero);
                }
                catch
                {
                }
            }
        }

        private Vector3 GetPhoneSpawnPosition()
        {
            if (!CanAnimate())
                return Vector3.Zero;

            try
            {
                EntityBone hand = ped.Bones[Bone.PHRightHand];

                if (hand != null && hand.IsValid)
                    return hand.Position;
            }
            catch
            {
            }

            return ped.Position + new Vector3(0.0f, 0.0f, 0.9f);
        }

        private void StartHoldInternal(int durationMs, bool forceReplay)
        {
            int now = Game.GameTime;
            int duration = Math.Max(1000, durationMs);
            int newHoldUntil = now + duration;

            if (newHoldUntil > holdUntil)
                holdUntil = newHoldUntil;

            if (holding && !forceReplay)
                return;

            holding = true;
            float blendInSpeed = forceReplay ? 1.25f : 4.0f;
            PlayPhoneAnimation(HoldAnim, duration, 49, blendInSpeed, -2.0f);
        }

        private void TickHold(int nowMs)
        {
            if (holdUntil > 0 && nowMs >= holdUntil)
            {
                holding = false;
                holdUntil = 0;
                return;
            }

            if (phoneProp == null || !phoneProp.Exists())
                EnsurePhoneProp();

            if (IsPlayingHoldAnimation())
                return;

            int remaining = holdUntil <= 0 ? 3000 : Math.Max(1000, holdUntil - nowMs);
            PlayPhoneAnimation(HoldAnim, remaining, 49, 4.0f, -2.0f);
        }

        private bool IsPlayingHoldAnimation()
        {
            try
            {
                return Function.Call<bool>(
                    Hash.IS_ENTITY_PLAYING_ANIM,
                    ped.Handle,
                    AnimDict,
                    HoldAnim,
                    3
                );
            }
            catch
            {
                return true;
            }
        }

        private void PlayPhoneAnimation(string animationName, int durationMs, int flags, float blendInSpeed, float blendOutSpeed)
        {
            try
            {
                RequestAnimationDictionary(300);

                if (!Function.Call<bool>(Hash.HAS_ANIM_DICT_LOADED, AnimDict))
                    return;

                Function.Call(
                    Hash.TASK_PLAY_ANIM,
                    ped.Handle,
                    AnimDict,
                    animationName,
                    blendInSpeed,
                    blendOutSpeed,
                    durationMs,
                    flags,
                    0.0f,
                    false,
                    false,
                    false
                );
            }
            catch
            {
            }
        }

        private void ClearSecondaryTask()
        {
            if (!CanAnimate())
                return;

            try
            {
                Function.Call(Hash.CLEAR_PED_SECONDARY_TASK, ped.Handle);
            }
            catch
            {
            }
        }

        private void RequestAnimationDictionary(int timeoutMs)
        {
            if (Function.Call<bool>(Hash.HAS_ANIM_DICT_LOADED, AnimDict))
                return;

            Function.Call(Hash.REQUEST_ANIM_DICT, AnimDict);

            int endAt = Game.GameTime + Math.Max(0, timeoutMs);

            while (!Function.Call<bool>(Hash.HAS_ANIM_DICT_LOADED, AnimDict) && Game.GameTime < endAt)
                Script.Yield();
        }

        private void DeletePhoneProp()
        {
            if (phoneProp == null)
                return;

            try
            {
                if (phoneProp.Exists())
                {
                    phoneProp.Detach();
                    phoneProp.Delete();
                }
            }
            catch
            {
            }

            phoneProp = null;
        }
    }
}
