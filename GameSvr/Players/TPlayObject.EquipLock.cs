using System.Collections.Generic;
using SystemModule;

namespace GameSvr
{
    // ================================================================================================
    // 装备密码锁 / "密宝" (equipment password lock) subsystem — faithful port of CM 1068 (password
    // input, worker sub_6D1780) and CM 1084 (unlock timer, worker sub_6D1AB8). Reversed 1:1 against
    // M2Server flat_image.bin (image base 0x00400000). This SUPERSEDES the blanket fail-closed drop
    // that cm-1 registered for these two idents in NativeCmQ1FailClosed (CM 1068/1084).
    //
    // ---- Native dispatch ----
    //   CM 1068 leaf 0x6D959B: payload = LStrCopy(body[ebp-8]); then
    //     sub_6D1780(EAX=self, EDX=Recog=word0[rec]=nParam1, ECX=Param=word[rec+6]=nParam2,
    //                stack=payload string)
    //   CM 1084 leaf 0x6D95C9: sub_6D1AB8(EAX=self)   — no extra args.
    //
    // ---- sub_6D1780 (CM 1068 password input), 12-arm Delphi jump table @0x6D17F8 ----
    //   Prologue: [ebp-8]=GetTickCount; P=[ebp-0xc]= (Recog>0)?IntToStr(Recog):(body if len>0 else "").
    //   0x6D17E8 `cmp Param,0xB / ja 0x6D1A4D` -> Param>0xB falls to the shared exit (no-op).
    //   Shared exit 0x6D1A4D: `test bl,bl` — only arms 0/1 (their >180000ms timeout branch) set bl=1,
    //   which then sends SendDefMessage(vtbl+0xD4, wParam=0xFCFF, "系统已接受解锁申请，处理中...").
    //     case 0  0x6D1828: if [self+0x711]==0 -> exit(no-op). Else throttle [self+0x6F8]; when
    //                        now-[+0x6F8] > 0x2BF20(180000ms): stamp [+0x6F8], bl=1, submit subcmd
    //                        0x0D via 0x6C5438; else re-arm confirm via sub_6C7D88(self,1).
    //     case 1  0x6D1886: sub_6C7D88(self,1); if it returned 0 -> exit. if [self+0xB7B]==0 -> exit.
    //                        if now-[+0x740] > 180000: stamp [+0x740], bl=1, submit subcmd 0x0E.
    //     case 2  0x6D18EB: if [self+0x1801]==0 -> exit. else submit subcmd 0x0F.
    //     case 3  0x6D191E: if [self+0xD31]!=3 -> exit. else submit subcmd 0x10.
    //     case 4  0x6D1951: (no gate) submit subcmd 0x12.
    //     case 5  0x6D1977: (no gate) submit subcmd 0x13.
    //     case 6  0x6D199D: (no gate) submit subcmd 0x14.
    //     case 7  0x6D19C3: (no gate) submit subcmd 0x15.
    //     case 8  0x6D19E6: (no gate) submit subcmd 0x16.
    //     case 9  0x6D1A4D: == shared exit (no-op).
    //     case 10 0x6D1A09: (no gate) submit subcmd 0x18.
    //     case 11 0x6D1A2C: (no gate) submit subcmd 0x19.
    //   The "submit subcmd N" action is sub_6C5438(self, dx=N, ecx=IntToStr([self+0x74C]), stack=P):
    //   it packs a type-0x151(337) "密宝请求" record {name[self+0xAF4], IntToStr([self+0x74C]), P}
    //   and hands it to the CROSS-SERVER broadcast manager [[0x7D62DC]] via sub_71315C — the very
    //   manager cm-1 documented as unmodelled for CM 1090/1200/1217. Not modeled here => fail closed.
    //
    // ---- sub_6D1AB8 (CM 1084 unlock timer) ----
    //   0x6D1ACF `cmp byte [self+0xB78],0 / jbe exit` -> lock mode 0 (unlocked) sends NOTHING.
    //   Else sub_6C7D88(self,1) (returns 0 while locked, having sent the confirm dialog) ; when it
    //   returns 1 it computes remaining=(0x2BF20-(now-[self+0x740]))/1000 (clamped >=0) and, keyed on
    //   [self+0xB7B]/remaining, emits internal ext idents 0x2733(10035)/0x2737(10039) via sub_765E68
    //   plus a SendDefMessage(vtbl+0xD4, 0xFFDB, IntToStr(remaining)+" 秒后才能解锁装备").
    //
    // ---- sub_6C7D88 (the shared confirm gate) ----
    //   `mov al,1 / cmp byte [self+0x711],0 / je return` -> returns 1 (proceed) while the lock is
    //   inactive. While active it (re)sends internal ext ident 0x2733(10035) via sub_765E68 — the
    //   native outbound translator sub_6B3EAC case 10035 @0x6B4B84 maps 10035 to the on-wire logical
    //   message SM_LOCKEQUIP=689(0x2B1) — and returns 0. (Cross-ref NativeShopWriteTransaction, which
    //   models the same gate for CM_DOSHOP; ConfirmPendingOffset=1809=[player+0x711].)
    //
    // ---- CONFIG GATE (default OFF), faithfully preserved ----
    //   The lock is PERSISTENT per-character state, not a runtime toggle. The ONLY writer of the
    //   master flag [player+0x711]=1 is the human-record load routine at 0x6B0AAA, reached only when
    //   the loaded lock-mode field [THumanRcd+0x48] -> [player+0xB78] == 3 (0x6B0A9E `cmp [+0xB78],3 /
    //   jne`). 0x6B0A5F/0x6B0A6B copy [THumanRcd+0x48]/[+0x49] into [player+0xB78]/[player+0xB79], and
    //   0x6B0A74 `[+0xB78]>0` arms the timer flags [+0xB7B]/[+0x4B7]. There is no CM/runtime path that
    //   arms the lock. This C# server does NOT persist or load these DB fields (verified by cm-1:
    //   no +0x711 field, no CM 1068/1084, no SM_LOCKEQUIP), so every character stays disarmed
    //   (mode 0, inactive) — identical to a native character that has never set the 密宝 password.
    //   The lock-state fields below therefore default to their disarmed values; the gates key off them
    //   exactly as native, so 1068/1084 take the native no-op paths and no wire bytes are invented.
    //
    // ---- Disposition ----
    //   * The lock-gated arms (CM 1068 case 0/1/2/3/9/default and all of CM 1084) reproduce their
    //     native gates and, while disarmed (this server's only reachable state), take the native
    //     no-op exit — send nothing, exactly like native for an unlocked character.
    //   * If a future DB-persistence layer ever arms the lock, every armed-path terminal action needs
    //     an unmodelled subsystem — the cross-server 密宝 manager [[0x7D62DC]] (sub_71315C) and/or the
    //     internal-ext translation sub_765E68/sub_6B3EAC — so those armed branches fail closed rather
    //     than fabricate a reply.
    //   * The UNGATED input arms (CM 1068 case 4/5/6/7/8/10/11) always submit a 密宝请求 to the
    //     cross-server manager [[0x7D62DC]]; that manager is not modeled, so they fail closed.
    // ================================================================================================
    public partial class TPlayObject
    {
        // ---- lock-state fields (三件套: native offset -> role -> C# field). Persistent, DB-backed;
        //      not loaded by this port (see CONFIG GATE above) so they stay at the disarmed defaults. ----

        /// <summary>[player+0x711] (dec 1809) byte — master lock-active flag. Set to 1 by the human-record
        /// load 0x6B0AAA only when [player+0xB78]==3; cleared at 0x6D138C. sub_6C7D88 returns "proceed"
        /// while this is 0. Same field as NativeShopWriteTransaction.ConfirmPendingOffset(1809).</summary>
        private bool _nativeEquipLockActive = false;

        /// <summary>[player+0xB78] (dec 2936) byte — lock mode 0..3 loaded from THumanRcd[+0x48] at
        /// 0x6B0A5F. 0 = unlocked (CM 1084 sends nothing); &gt;0 arms the timer; ==3 also arms the master
        /// flag [player+0x711].</summary>
        private byte _nativeEquipLockMode = 0;

        /// <summary>[player+0xB79] (dec 2937) byte — lock type loaded from THumanRcd[+0x49] at 0x6B0A6B.
        /// 1 =&gt; question word Random(6); 2 =&gt; Random(8) (sub_4C77A0), used when [player+0x74C]==0.</summary>
        private byte _nativeEquipLockType = 0;

        /// <summary>[player+0xB7B] (dec 2939) byte — unlock-timer active flag; set to 1 at 0x6B0A8A when
        /// mode&gt;0. Gates CM 1068 case 1 and selects the CM 1084 branch.</summary>
        private bool _nativeEquipLockTimerActive = false;

        /// <summary>[player+0x74C] (dec 1868) word — current unlock-question word; 0 until generated, then
        /// randomized from the lock type. Every CM 1068 submit arm stringifies it (IntToStr).</summary>
        private ushort _nativeEquipLockQuestion = 0;

        /// <summary>[player+0x6F8] (dec 1784) dword — confirm-dialog throttle tick (GetTickCount) for
        /// CM 1068 case 0 and sub_6C7D88; the 180000 ms(0x2BF20) window.</summary>
        private int _nativeEquipLockConfirmTick = 0;

        /// <summary>[player+0x740] (dec 1856) dword — unlock-timer base tick (GetTickCount) for CM 1068
        /// case 1 and CM 1084; remaining = (180000-(now-this))/1000.</summary>
        private int _nativeEquipLockTimerTick = 0;

        /// <summary>[player+0x1801] (dec 6145) byte — CM 1068 case-2 arm gate. Owned by an out-of-scope
        /// subsystem (writers at 0x633940/0x633A9F); disarmed(0) here, so case 2 no-ops.</summary>
        private byte _nativeEquipLockCase2Gate = 0;

        /// <summary>[player+0xD31] (dec 3377) byte — CM 1068 case-3 arm gate (native `!=3 -> exit`). Never
        /// written in the image; disarmed here, so case 3 no-ops.</summary>
        private byte _nativeEquipLockCase3Gate = 0;

        /// <summary>0x2BF20 = 180000 ms = 180 s — the native lock/confirm window (idiv by 1000 -&gt; seconds).</summary>
        private const int NativeEquipLockWindowMs = 0x2BF20;

        private static readonly HashSet<long> _equipLockFailClosedReported = new HashSet<long>();
        private static readonly object _equipLockFailClosedGate = new object();

        /// <summary>
        /// CM dispatch entry for the 装备密码锁 subsystem. Returns true iff it owns the ident.
        ///
        /// INTEGRATOR HOOKUP (this file only documents it; do NOT edit the Operate() switch here):
        /// in TPlayObject.Message.cs Operate()'s `default:` arm, call this FIRST — BEFORE
        /// TryHandleNativeCmQ1 — so CM 1068/1084 route through this faithful handler instead of the
        /// cm-1 fail-closed drop in NativeCmProtocol_Q1 (which still lists 1068/1084 but is now shadowed):
        ///
        ///     default:
        ///         if (!TryHandleEquipLockCm(ProcessMsg)          // &lt;-- add this line, ahead of Q1
        ///             &amp;&amp; !TryHandleNativeSocialProtocol(ProcessMsg)
        ///             &amp;&amp; !TryHandleNativeCmTailProtocol(ProcessMsg)
        ///             &amp;&amp; !TryHandleNativeCmQ1(ProcessMsg)
        ///             &amp;&amp; !TryHandleNativeCmQ2(ProcessMsg)
        ///             &amp;&amp; !TryHandleNativeCmQ3(ProcessMsg))
        ///         {
        ///             result = base.Operate(ProcessMsg);
        ///         }
        ///         break;
        /// </summary>
        private bool TryHandleEquipLockCm(TProcessMessage processMessage)
        {
            switch (processMessage.wIdent)
            {
                case Grobal2.CM_1068:
                    // leaf 0x6D959B: EDX=Recog=nParam1, ECX=Param=nParam2, stack=body(sMsg).
                    NativeEquipLockInput(processMessage.nParam1, processMessage.nParam2, processMessage.sMsg);
                    return true;
                case Grobal2.CM_1084:
                    // leaf 0x6D95C9: sub_6D1AB8(self) — no args.
                    NativeEquipLockTimer();
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>
        /// CM 1068 — worker sub_6D1780. 12-arm Delphi jump table on Param (0..0xB); Param&gt;0xB and arm 9
        /// fall to the shared no-op exit 0x6D1A4D. Payload P (native [ebp-0xc]) = (Recog&gt;0)?Recog:body;
        /// it is only consumed by the cross-server submit arms, which fail closed, so it is not built here.
        /// </summary>
        private void NativeEquipLockInput(int recog, int param, string body)
        {
            // 0x6D17BE..0x6D17E0: payload P (native [ebp-0xc]) = (Recog>0)?IntToStr(Recog):(body if len>0
            // else ""). It is the third field of every 密宝请求 record, so it is built here for the submit
            // arms' fail-closed evidence even though those arms cannot reach the cross-server manager.
            string payload = recog > 0
                ? recog.ToString()
                : (string.IsNullOrEmpty(body) ? string.Empty : body);

            // 0x6D17E8 `cmp eax,0xB / ja 0x6D1A4D`. ECX came from `movzx ecx, word[rec+6]`, so the index
            // is the zero-extended 16-bit Param.
            int arm = param & 0xFFFF;
            if (arm > 0xB)
            {
                return; // default -> shared exit (bl=0), no-op
            }

            switch (arm)
            {
                case 0: // 0x6D1828
                    // `cmp byte [self+0x711],0 / je exit` — disarmed => native no-op (send nothing).
                    if (!_nativeEquipLockActive)
                    {
                        return;
                    }
                    // Armed: `cmp (now-[self+0x6F8]),0x2BF20 / jbe re-confirm`. Timeout submits subcmd 0x0D
                    // to [[0x7D62DC]] and sends 0xFCFF "系统已接受解锁申请…"; within window it re-sends the
                    // confirm dialog via sub_6C7D88 (0x2733/689). Both need unmodelled subsystems.
                    {
                        uint elapsed0 = unchecked((uint)(HUtil32.GetTickCount() - _nativeEquipLockConfirmTick));
                        EquipLockFailClosed(1068, arm, elapsed0 > (uint)NativeEquipLockWindowMs
                            ? "0x6D1828 case0 armed timeout -> 0x6C5438 subcmd 0x0D [[0x7D62DC]] + SendDefMessage(0xFCFF)"
                            : "0x6D1828 case0 armed in-window -> sub_6C7D88 re-confirm 0x2733(10035)/SM_LOCKEQUIP(689)");
                    }
                    return;

                case 1: // 0x6D1886
                    // sub_6C7D88(self,1): proceed while disarmed; while armed it sends 0x2733 and returns 0.
                    if (!EquipLockConfirmGateProceed(1068, arm, "0x6D1886 case1"))
                    {
                        return;
                    }
                    // `cmp byte [self+0xB7B],0 / je exit` — timer flag off => native no-op.
                    if (!_nativeEquipLockTimerActive)
                    {
                        return;
                    }
                    // `cmp (now-[self+0x740]),0x2BF20 / jbe exit` — within window native ALSO no-ops; only a
                    // timeout submits subcmd 0x0E to [[0x7D62DC]].
                    {
                        uint elapsed1 = unchecked((uint)(HUtil32.GetTickCount() - _nativeEquipLockTimerTick));
                        if (elapsed1 <= (uint)NativeEquipLockWindowMs)
                        {
                            return; // jbe exit -> no-op
                        }
                        EquipLockFailClosed(1068, arm,
                            "0x6D1886 case1 armed timeout -> 0x6C5438 subcmd 0x0E [[0x7D62DC]]");
                    }
                    return;

                case 2: // 0x6D18EB
                    // `cmp byte [self+0x1801],0 / je exit`.
                    if (_nativeEquipLockCase2Gate == 0)
                    {
                        return;
                    }
                    EquipLockFailClosed(1068, arm,
                        "0x6D18EB case2 -> 0x6C5438 subcmd 0x0F [[0x7D62DC]]");
                    return;

                case 3: // 0x6D191E
                    // `cmp byte [self+0xD31],3 / jne exit`.
                    if (_nativeEquipLockCase3Gate != 3)
                    {
                        return;
                    }
                    EquipLockFailClosed(1068, arm,
                        "0x6D191E case3 -> 0x6C5438 subcmd 0x10 [[0x7D62DC]]");
                    return;

                case 9: // jump-table entry 9 == shared exit 0x6D1A4D
                    return; // no-op

                case 4:  // 0x6D1951 subcmd 0x12
                case 5:  // 0x6D1977 subcmd 0x13
                case 6:  // 0x6D199D subcmd 0x14
                case 7:  // 0x6D19C3 subcmd 0x15
                case 8:  // 0x6D19E6 subcmd 0x16
                case 10: // 0x6D1A09 subcmd 0x18
                case 11: // 0x6D1A2C subcmd 0x19
                    // No lock gate: native unconditionally packs a type-0x151 "密宝请求" record
                    // {name[self+0xAF4], IntToStr([self+0x74C]), payload} and submits it to the cross-server
                    // broadcast manager [[0x7D62DC]] (sub_71315C). Not modeled here (same manager cm-1
                    // withholds for CM 1090/1200/1217) => fail closed.
                    EquipLockFailClosed(1068, arm,
                        $"ungated cross-server 密宝请求 submit (question={_nativeEquipLockQuestion}, payload='{payload}') -> 0x6C5438 -> [[0x7D62DC]] sub_71315C");
                    return;

                default:
                    return; // unreachable (arm is 0..0xB); mirrors the shared exit
            }
        }

        /// <summary>
        /// CM 1084 — worker sub_6D1AB8. Reads lock mode [self+0xB78]; mode 0 (unlocked) sends nothing.
        /// While armed it gates through sub_6C7D88 and emits 0x2733/0x2737/0xFFDB via the internal-ext
        /// path — unmodelled — so the armed branch fails closed. This server is always mode 0, so the
        /// native no-op path is taken.
        /// </summary>
        private void NativeEquipLockTimer()
        {
            // 0x6D1ACF `cmp byte [self+0xB78],0 / jbe 0x6D1BE2` — mode 0 => return, send nothing.
            if (_nativeEquipLockMode == 0)
            {
                return; // faithful native no-op for an unlocked character
            }

            // Armed: sub_6C7D88(self,1) must pass, then remaining=(0x2BF20-(now-[self+0x740]))/1000 clamped
            // >=0. [self+0xB7B] and remaining pick 0x2733(9)(expired; may Random the question from
            // [self+0xB79]) / 0x2737(remaining) / SendDefMessage(0xFFDB, IntToStr(remaining)+" 秒后才能解锁
            // 装备"). Every send goes through the unmodelled internal-ext translator sub_765E68/sub_6B3EAC.
            int elapsed = HUtil32.GetTickCount() - _nativeEquipLockTimerTick;
            int remaining = (NativeEquipLockWindowMs - elapsed) / 1000; // native idiv 0x3E8
            if (remaining < 0)
            {
                remaining = 0; // 0x6D1B0F `jge / xor esi,esi` clamp
            }
            string detail = _nativeEquipLockTimerActive
                ? (remaining == 0
                    ? $"expired -> 0x2733(9) question={_nativeEquipLockQuestion} type={_nativeEquipLockType}"
                    : $"countdown {remaining}s -> 0x2737 + SendDefMessage(0xFFDB)")
                : $"first-tick -> set [+0xB7B]=1, 0x2737({remaining})";
            EquipLockFailClosed(1084, 0,
                "0x6D1AB8 armed: sub_6C7D88 + " + detail + " (internal-ext sub_765E68/sub_6B3EAC)");
        }

        /// <summary>
        /// sub_6C7D88(self, 1) confirm gate. Returns true (native al=1) to let the caller proceed while
        /// the lock is inactive ([self+0x711]==0). While active, native (re)sends internal ext ident
        /// 0x2733(10035) -&gt; wire SM_LOCKEQUIP(689) and returns 0; that path is unmodelled, so an armed
        /// gate fails closed and returns false.
        /// </summary>
        private bool EquipLockConfirmGateProceed(int ident, int arm, string context)
        {
            // 0x6C7D94 `cmp byte [self+0x711],0 / je return(al=1)`.
            if (!_nativeEquipLockActive)
            {
                return true; // proceed; native sends nothing here
            }

            EquipLockFailClosed(ident, arm, context + " -> sub_6C7D88 armed confirm 0x2733(10035)/SM_LOCKEQUIP(689)");
            return false;
        }

        // sub_63F200 @0x63F244 loads mode 1 before calling the shared sub_6C7D88 gate.
        internal bool NativeMerchantSellEquipLockGate() =>
            EquipLockConfirmGateProceed(Grobal2.CM_USERSELLITEM, 1,
                "sub_63F200 @0x63F244 merchant sell");

        /// <summary>
        /// Drop the packet and record the unreproducible terminal action once per (ident, arm) per
        /// process. Nothing is sent, because the reply native would build depends on a subsystem this
        /// port does not model (the persistent 密宝 lock state, the cross-server manager [[0x7D62DC]], or
        /// the internal-ext translation sub_765E68/sub_6B3EAC). Mirrors NativeCmQ1FailClosed.Drop.
        /// </summary>
        private void EquipLockFailClosed(int ident, int arm, string blocker)
        {
            long key = ((long)ident << 8) | (uint)(arm & 0xFF);
            lock (_equipLockFailClosedGate)
            {
                if (!_equipLockFailClosedReported.Add(key))
                {
                    return;
                }
            }

            M2Share.MainOutMessage(
                $"[CM未移植/装备密码锁] CM {ident} arm={arm} 已丢弃; " +
                $"角色={(string.IsNullOrEmpty(m_sCharName) ? "<unknown>" : m_sCharName)}; " +
                $"缺口={blocker}");
        }
    }
}
