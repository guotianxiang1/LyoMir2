using System.Buffers.Binary;
using SystemModule;

namespace GameSvr
{
    public partial class HeroObject
    {
        // Hero record offsets are record-base offsets. Native sub_6888FC keeps
        // EBX=record+8, then loads [ebx+0x108]/[ebx+0x10C] at 0x688A5F/
        // 0x688A50 into hero+0x610/+0x60C respectively.
        internal const int NativeLogonPrereqRecordOffset = 0x110;
        internal const int NativeLogonCapBitmaskRecordOffset = 0x114;

        private bool TryGetNativeHeroLogonState(out uint capBitmask,
            out int prereq)
        {
            capBitmask = 0;
            prereq = 0;
            var raw = NativeHeroState?.FixedRecord;
            if (raw == null ||
                raw.Length < NativeLogonCapBitmaskRecordOffset + sizeof(uint))
            {
                return false;
            }

            capBitmask = BinaryPrimitives.ReadUInt32LittleEndian(
                raw.AsSpan(NativeLogonCapBitmaskRecordOffset, sizeof(uint)));
            var storedPrereq = BinaryPrimitives.ReadInt32LittleEndian(
                raw.AsSpan(NativeLogonPrereqRecordOffset, sizeof(int)));
            prereq = storedPrereq > 0 ? storedPrereq : 1;
            return true;
        }

        // THeroAct and its three job VMTs route +0x204 to sub_69057C, not the
        // player's sub_6E9A98. The hero cluster is exactly 3324 then the
        // sub_74839C cold-time list (4367 for race 54).
        private void SendNativeHeroLogonStateSync()
        {
            var master = FindMaster();
            if (master == null)
                return;

            if (TryGetNativeHeroLogonState(out var capBitmask, out var prereq))
            {
                var soulWash = BuildSm3324(unchecked((int)capBitmask),
                    unchecked((ushort)prereq),
                    m_btRaceServer == Grobal2.RC_HEROOBJECT);
                master.SendSocket(soulWash.Header, soulWash.Msg);
            }

            SendNativeColdTimeListState();
        }
    }
}
