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

        // sub_6F769C @0x006F769C, reached from UserLogon @0x6B24E7.
        // Always sends SM 4615 (0x1207) via [obj+0x254], Recog=0, Len=8.
        // Body is zeros when [player+0xAE8]==0 (0x6F76BA test esi / je 0x6F76E3)
        // or when the subsequent 0x6A52A0 lookup fails. The non-zero path needs
        // 0x705660's return (two dwords used as the lookup key) which is not
        // wired here; login still emits the packet, matching the always-send.
        private void SendNativeClearPendingRequestOnLogon()
        {
            SendSocket(Grobal2.MakeDefaultMsg(Grobal2.SM_CLEAR_PENDING_REQUEST, 0, 0, 0, 0),
                new byte[8]);
        }

        // sub_6F772C @0x006F772C, reached from UserLogon @0x6B24EE (the call
        // between 4615 and 4628). Always sends SM 4612 via [obj+0x254]:
        //   0x6F7813 66 BA 04 12
        //   Recog=0 Param=0 Tag=0 Series=0
        // Empty list still sends: je 0x6F77EB skips packing but not the send,
        // Len=0 (ebx stays 0 from the xor at 0x6F7750). Body records are 17
        // bytes {type byte + ShortString cap 15} when the offline-notice
        // queue at manager+0x24 is non-empty; C# does not yet populate that
        // queue, so login emits the empty frame native still always fires.
        private void SendNativePendingNoticesOnLogon()
        {
            SendSocket(Grobal2.MakeDefaultMsg(Grobal2.SM_PENDING_NOTICE, 0, 0, 0, 0),
                new byte[0]);
        }
    }
}
