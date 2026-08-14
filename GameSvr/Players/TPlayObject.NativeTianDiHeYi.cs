using SystemModule;

namespace GameSvr
{
    // ================================================================
    // 天地合一 (group-recall) skill consumer — 1:1 port of the native
    // command handler sub_6C7B28 (0x006C7B28), its executor sub_7274B4
    // (0x007274B4), the per-member gate, and the refusal messages.
    //
    // Every offset/string/colour below is re-derived from flat_image.bin
    // (ImageBase 0x00400000, capstone 5.0.7), NOT taken on trust:
    //
    //  dispatcher idx25 case @0x006239D8 (GM command table 0x7B4654):
    //      0x6239DB  cmp byte [self+0x1b0],0xF  / je 0x6239ED
    //      0x6239E4  cmp bl,3 / jb 0x62B64C       ; bl = caller effective perm
    //      0x6239F0  call sub_6C7B28
    //    self+0x1b0 is a FOREIGN-class displacement (whole-image census: the
    //    only writer 0x5DDF9E belongs to the sub_5DE32C-vmt class and stores 1;
    //    no site ever writes 0xF to +0x1b0, and TPlayObject InitInstance
    //    zero-fills it), so `==0xF` is永久 false here and the gate reduces to
    //    caller permission >= 3 (below that: silent). Modeled in
    //    Command/Commands/TianDiHeYiCommand.cs (the dispatcher stand-in).
    //
    //  command body sub_6C7B28:
    //      0x6C7B41  mov eax,[self+0x128]           ; Envir
    //      0x6C7B47  cmp byte [eax+0x67],0 / jne    ; +0x67 = NORECALL byte
    //      0x6C7B51  cmp byte [eax+5],0 / jne       ; +0x05 = DARE byte
    //        -> 0x6C7C0E str@0x6C7CA4 「在这里您无法使用」 cx=0x38FF vmt+0xD4
    //      0x6C7B5B  GetTickCount; delta=tick-[self+0x728]; div 1000
    //                if elapsedSec >= [self+0xBA3] -> [+0xBA3]=0 else -=elapsedSec
    //                [self+0x728]=tick
    //      0x6C7B94  if [self+0xBA3]!=0 -> 0x6C7BD1
    //                  str@0x6C7C78 「天地合一 」 + IntToStr(cd) + str@0x6C7C8C
    //                  「 秒后方可使用」 cx=0x38FF vmt+0xD4
    //      0x6C7B9D  if sub_6B7BAC(self) [is group leader] -> sub_7274B4(self+0xA80)
    //                else str@0x6C7C54 「天地合一只有队长有权使用」 cx=0x38FF vmt+0xD4
    //      0x6C7BC8  mov byte [self+0xBA3],0xB4     ; cooldown := 180 on BOTH arms
    //
    //  executor sub_7274B4(eax = group = [self+0xA80]):
    //      owner = [group+0x3c]; if owner==0 -> ret
    //      for i in 0..10 (0x727567 cmp esi,0xB):
    //        member = [group+0x48+i*4] -> [+0x10]
    //        if member==0 -> next            (0x7274F2 test/je)
    //        if member==owner -> next        (0x7274F9 cmp [group+0x3c]/je)
    //        name = [member+0x106]
    //        if [member+0xBA4]==0            (0x72750C AllowGroupReCall clear)
    //           || [member.Envir+0x67]!=0    (0x72751B NORECALL)
    //           || [member.Envir+5]!=0       (0x727527 DARE)   -> refuse
    //           refuse: str@0x7275A4 「无法对 」 + name + str@0x7275B4
    //                   「 使用天地合一」 sent to OWNER, cx=0x38FF vmt+0xD4
    //        else sub_6BF458(owner, name)    -> C# TPlayObject.RecallHuman(name)
    //
    //  executor's per-member move sub_6BF458 == the already-ported
    //  TPlayObject.RecallHuman(string) (proven identical entry: GM @CallMan
    //  jt[72] @0x00624C94 is `mov edx,p1 / mov eax,self / call 0x6BF458`),
    //  so the recall effect (RM_SPACEMOVE_FIRE = 10330 = 0x285A) and the
    //  target SpaceMove to the owner's front tile (GetFrontPosition + range-3
    //  GetRecallXY) are reused verbatim rather than re-synthesised.
    //
    //  field map (both transient — sub_6B0FF0 saves NEITHER; fresh object => 0,
    //  matching the native InitInstance zero-fill):
    //      obj+0x728 -> m_dwGroupRecallLastTick  (last-use tick)
    //      obj+0xBA3 -> m_btGroupRecallCd        (down-counting cooldown byte)
    //      obj+0xBA4 -> m_boAllowGroupReCall     (already modeled, TBaseObject.cs)
    //
    //  colour convention (verified against the committed ports): the native
    //  cx word unpacks FColor=cx&0xFF, BColor=cx>>8. cx=0x38FF => 0xFF/0x38 =
    //  MsgColor.Red; cx=0xFFDB => 0xDB/0xFF = MsgColor.Green. All refusals use
    //  vmt+0xD4 (SysMsg) with MsgType.Hint.
    //
    // ----------------------------------------------------------------
    // NORANDOMMOVE random-teleport-magic refusal string — NEGATIVE finding.
    //
    // eqv-23 (docs/eqv_shard23_20260814.md line 36, MOVE-82) claims the
    // random-teleport magic sub_7855F8 sends 「在这里你无法使用」(str@0x785864)
    // on a NORANDOMMOVE map (send 0x7856F0). Independent re-disassembly REFUTES
    // that attribution:
    //   * sub_7855F8's NORANDOMMOVE branch is the EffectType-2 arm
    //     0x78570A: mov eax,[esi+0x128] / 0x785710 cmp byte [eax+0x68],0 /
    //     0x785714 jne 0x785742 -> 0x785742 xor ebx,ebx / jmp end  (SILENT).
    //     Envir+0x68 = NORANDOMMOVE (parser 0x775A60; LimitItemMove compound
    //     0x775A5C..0x775A68 writes +0x67/+0x68/+0x6B/+0x6C). Whole-image
    //     census of every `cmp byte [reg+0x68],0` reader (0x785710, 0x786E54,
    //     …) shows NONE emit a string — NORANDOMMOVE is silent everywhere.
    //   * str@0x785864 is actually sent for OTHER flags: FOXMAP (Envir+0x70,
    //     parser token@0x775F4C, sub_7855F8 case1/3 @0x7856F0/0x7857C1) and
    //     Envir+0x4C4 (case5 @0x785819); the sibling str@0x784EAC is sent by
    //     sub_784E74 @0x784E8B for NODRUG (Envir+0x69, token@0x775D64).
    //   * C# EatUseItems case 2 (TPlayObject.cs) already `if
    //     (!m_PEnvir.Flag.boNORANDOMMOVE) { … }` — a silent no-op on
    //     NORANDOMMOVE, byte-for-byte faithful to sub_7855F8 case2.
    // Therefore NO NORANDOMMOVE refusal string exists to port; adding one would
    // be fabrication (fail-closed). The real 「在这里你无法使用」 gates live in
    // the un-ported random-teleport-magic hotspot sub_7855F8/sub_784E74 and are
    // "report-only" per eqv-23. See the run report for the台账 correction.
    // ================================================================
    public partial class TPlayObject
    {
        // native obj+0x728 — last time 天地合一 was attempted (map-gate passed).
        // Transient: absent from the save codec sub_6B0FF0, so a fresh object
        // starts at 0, exactly like the native InitInstance zero-fill.
        private int m_dwGroupRecallLastTick;

        // native obj+0xBA3 — down-counting cooldown byte (seconds). Set to 0xB4
        // (180) after every accepted attempt, decremented by whole elapsed
        // seconds on each subsequent attempt. Transient (not persisted).
        private byte m_btGroupRecallCd;

        /// <summary>
        /// 天地合一 command body — native sub_6C7B28 (0x006C7B28). The caller
        /// (dispatcher idx25 case) has already applied the perm&gt;=3 gate.
        /// </summary>
        public void NativeTianDiHeYiCommand()
        {
            // 0x6C7B41 map gate: NORECALL (Envir+0x67) or DARE (Envir+5).
            var env = m_PEnvir;
            if (env == null || env.Flag.boNORECALL || env.Flag.boDARE)
            {
                // 0x6C7C0E str@0x6C7CA4, cx=0x38FF.
                SysMsg("在这里您无法使用", MsgColor.Red, MsgType.Hint);
                return;
            }

            // 0x6C7B5B cooldown decay: seconds elapsed since the last attempt.
            // Native `div esi(1000)` is a 32-bit unsigned divide; the tick
            // subtraction wraps like GetTickCount, so use unchecked uint math.
            var tick = HUtil32.GetTickCount();
            var elapsedSec = unchecked((uint)(tick - m_dwGroupRecallLastTick)) / 1000u;
            if (elapsedSec >= m_btGroupRecallCd)
            {
                m_btGroupRecallCd = 0;                       // 0x6C7B87
            }
            else
            {
                m_btGroupRecallCd = (byte)(m_btGroupRecallCd - elapsedSec); // 0x6C7B7F
            }
            m_dwGroupRecallLastTick = tick;                 // 0x6C7B8E

            // 0x6C7B94 still cooling down -> remaining-seconds notice.
            if (m_btGroupRecallCd != 0)
            {
                // 0x6C7BD1: str@0x6C7C78「天地合一 」 + IntToStr(cd) + str@0x6C7C8C.
                SysMsg("天地合一 " + m_btGroupRecallCd + " 秒后方可使用",
                    MsgColor.Red, MsgType.Hint);
                return;
            }

            // 0x6C7B9D leader gate + execute.
            if (NativeIsGroupRecallLeader())
            {
                NativeExecuteTianDiHeYi();                  // 0x7274B4
            }
            else
            {
                // 0x6C7BB5 str@0x6C7C54, cx=0x38FF.
                SysMsg("天地合一只有队长有权使用", MsgColor.Red, MsgType.Hint);
            }

            // 0x6C7BC8 cooldown := 180 — reached by BOTH the execute and the
            // non-leader arms (only the map/cooldown early-returns skip it).
            m_btGroupRecallCd = 180;
        }

        /// <summary>
        /// 天地合一 toggle — native idx23「拒绝天地合一」/ idx24「允许天地合一」
        /// case @0x00623990. Both names dispatch here: an XOR of obj+0xBA4
        /// (m_boAllowGroupReCall) + a Green (cx=0xFFDB) state message.
        /// </summary>
        public void NativeToggleGroupRecall()
        {
            // 0x623993 xor byte [self+0xBA4],1
            m_boAllowGroupReCall = !m_boAllowGroupReCall;
            // 0x6239A6/0x6239BF cx=0xFFDB (Green), str@0x62B8A0 / 0x62B8BC.
            SysMsg(m_boAllowGroupReCall
                    ? M2Share.g_sEnableGroupRecall
                    : M2Share.g_sDisableGroupRecall,
                MsgColor.Green, MsgType.Hint);
        }

        /// <summary>
        /// native sub_6B7BAC (0x006B7BAC): group=[self+0xA80]; false if none,
        /// else sub_726C14 tests self==[group+0x3C] (owner). The C# group model
        /// folds the group object into the leader, so m_GroupOwner is both
        /// [self+0xA80] and [group+0x3C]; leader ⇔ m_GroupOwner == this.
        /// </summary>
        private bool NativeIsGroupRecallLeader()
        {
            return m_GroupOwner != null && ReferenceEquals(m_GroupOwner, this);
        }

        /// <summary>
        /// 天地合一 executor — native sub_7274B4 (0x007274B4). Only invoked when
        /// <c>this</c> is the group leader, so <c>this</c> == [group+0x3C]
        /// (owner) and m_GroupMembers is the 11-slot member array
        /// [group+0x48+idx*4] -&gt; [+0x10].
        /// </summary>
        private void NativeExecuteTianDiHeYi()
        {
            var members = m_GroupMembers;
            if (members == null)
            {
                return; // native [group+0x3c]==0 guard folds to "no group state"
            }

            // 0x7274E8 loop i=0..10 (0x727567 cmp esi,0xB).
            var count = members.Count;
            if (count > NativeGroupMaxMembers)
            {
                count = NativeGroupMaxMembers;
            }
            for (var i = 0; i < count; i++)
            {
                var member = members[i];
                if (member == null)
                {
                    continue; // 0x7274F2 test ebx,ebx / je
                }
                if (ReferenceEquals(member, this))
                {
                    continue; // 0x7274F9 cmp ebx,[group+0x3c] / je (skip the leader)
                }

                var name = member.m_sCharName; // 0x727501 [member+0x106]
                var memberEnv = member.m_PEnvir;
                // 0x72750C AllowGroupReCall(+0xBA4)!=0 && 0x72751B NORECALL(Envir+0x67)==0
                //          && 0x727527 DARE(Envir+5)==0  -> recall, else refuse.
                if (member.m_boAllowGroupReCall
                    && memberEnv != null
                    && !memberEnv.Flag.boNORECALL
                    && !memberEnv.Flag.boDARE)
                {
                    // 0x727533 call sub_6BF458(owner, name) == this.RecallHuman(name).
                    RecallHuman(name);
                }
                else
                {
                    // 0x72753A str@0x7275A4 + name + str@0x7275B4, sent to the owner.
                    SysMsg("无法对 " + name + " 使用天地合一", MsgColor.Red, MsgType.Hint);
                }
            }
        }
    }
}
