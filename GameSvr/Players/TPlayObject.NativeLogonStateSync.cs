namespace GameSvr
{
    public partial class TPlayObject
    {
        // 战神 player login-state cluster sub_6E9A98 (player VMT 0x6AC8C8 + 0x204).
        // Heroes use their distinct sub_69057C cluster; see
        // HeroObject.NativeLogonStateSync.cs. This method is not called directly:
        // UserLogon (sub_6B1D64) queues RM 0x3010 at 0x6B2358, and the Operate loop's
        // secondary dispatcher sub_743AD8 (0x6B6247 call 0x743AD8) turns case 0x3010
        // into `0x743BF7 call [edx+0x204]`. The body fans out four legs, in this order:
        //
        //   0x6E9AA0 call 0x7468B4  -> SM 3324 : Recog=[self+0x60C], Param=word[self+0x610],
        //                             Tag=0, Series=(m_btRaceServer==54 hero ? 1 : 0).
        //                             They round-trip through record+0x580/+0x57C;
        //                             decoder 0x6B060A..0x6B0611 maps prereq <=0 to 1.
        //   0x6E9AA7 call 0x6F0A50  -> SM 1264 : Recog=0, Tag=Series=0, no body,
        //                             Param = ([0x7D7038]+3 & 0x80) ? 1 : 0.
        //                             Bit 31 is ServerSwitch.Bin's "新交易行" switch;
        //                             both the set and clear arms send exactly once.
        //   0x6E9AAE call 0x6E99B8  -> SM 3554 : timed-ability snapshot. FULLY resolved,
        //                             emitted below.
        //   0x6E9ABB call 0x74839C  -> SM 3556 : cold-time {Key,Remaining} list.
        //                             0x7483EE jle means an empty list sends nothing.
        //
        // All four byte-verified legs are emitted below.
        private void SendNativeLogonStateSync()
        {
            if (TrySoulWashSource(out var capBitmask, out var prereq))
            {
                var state = BuildSm3324(unchecked((int)capBitmask),
                    unchecked((ushort)prereq),
                    m_btRaceServer == SoulWashHeroRace);
                SendSocket(state.Header, state.Msg);
            }

            var tradeLine = BuildSm1264(
                M2Share.ServerSwitches?.IsBitSet(3, 0x80) == true);
            SendSocket(tradeLine.Header, string.Empty);

            // 3554 的构造在 TBaseObject.TimedAbility.cs 只保留一份实现。合并 w/m-sm-c 时
            // 发现它与已在 master 的 BuildTimedAbilityListState 是同一功能的两份实现
            // （同为 ident 0xDE2、同样遍历 [self+0xDC]、同样 10 字节记录），取了证据注释
            // 更完整、且已有审计工具 NativeTimedAbilityListCheck 钉住的那份。
            var snapshot = BuildTimedAbilityListState();
            SendNativeLogonStateSnapshot(snapshot.Header, snapshot.Body);
            SendNativeColdTimeListState();
        }

        internal virtual void SendNativeLogonStateSnapshot(
            SystemModule.ClientPacket header, byte[] body)
        {
            SendSocket(header, body);
        }
    }
}
