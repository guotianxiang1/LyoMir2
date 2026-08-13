using SystemModule;
using GameSvr.Services;

namespace GameSvr
{
    public partial class TPlayObject
    {
        // sub_6F7638 @0x006F7638, reached from UserLogon @0x6B24E0.
        // Always sends SM 4613 (0x1205) via [obj+0x254]:
        //   push [player+0x58C]; push [player+0x588]; call 0x6A52A0
        //   if null -> body = 8 zero bytes
        //   else body = dword[req+0x18] || dword[req+0x1C]  (TargetKey)
        //   Recog=0 Param=0 Tag=0 Series=0 Len=8
        private void SendNativePendingRequestOnLogon()
        {
            var body = new byte[8];
            var playerId = GetCachedNativeUserId();
            if (playerId != 0 &&
                CorpsService.TryGetOwnPendingRequest(playerId, out var request) &&
                request != null)
            {
                var target = request.TargetKey;
                body[0] = (byte)target;
                body[1] = (byte)(target >> 8);
                body[2] = (byte)(target >> 16);
                body[3] = (byte)(target >> 24);
                body[4] = (byte)(target >> 32);
                body[5] = (byte)(target >> 40);
                body[6] = (byte)(target >> 48);
                body[7] = (byte)(target >> 56);
            }
            SendSocket(Grobal2.MakeDefaultMsg(Grobal2.SM_PENDING_REQUEST, 0, 0, 0, 0),
                body);
        }
    }
}
