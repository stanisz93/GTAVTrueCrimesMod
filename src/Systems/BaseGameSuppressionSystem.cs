using GTA.Native;

namespace GTAVTrueCrimesMod.Systems
{
    public class BaseGameSuppressionSystem
    {
        public void Update()
        {
            ClosePhoneIfVisible();
        }

        private void ClosePhoneIfVisible()
        {
            Function.Call(Hash.DESTROY_MOBILE_PHONE);
            Function.Call(Hash.CELL_CAM_ACTIVATE, false, false);
            Function.Call(Hash.SET_MOBILE_PHONE_RADIO_STATE, false);
        }
    }
}
