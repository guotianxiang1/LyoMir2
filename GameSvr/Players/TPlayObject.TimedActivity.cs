using GameSvr.Services;
using SystemModule;

namespace GameSvr
{
    /// <summary>
    /// === TimedActivity subsystem ===
    /// Faithful 1:1 port of three 战神 client-message workers that this port had
    /// previously routed straight to the shared fail-closed sinks
    /// (<see cref="TryHandleNativeCmQ1"/> for CM 1059,
    /// <see cref="TryHandleNativeCmQ3"/> for CM 3344/3410). Disassembled from
    /// flat_image.bin (ImageBase 0x400000) with capstone; the raw dumps live in
    /// tools/timedact_re/*.txt.
    ///
    /// What "upgrade" means here: the DERIVABLE gates each worker evaluates before
    /// it touches unmodelled state are now reproduced exactly (CM 1059's one-shot
    /// arm flag + 10 s throttle, CM 3410's fixed 40-byte body-length gate), and the
    /// full data flow of every worker — including the 40-byte record layout and the
    /// SM 0xD27 reject-code ladder — is documented from the image. Every TERMINAL
    /// action still reads runtime subsystem state that is not a constant in the
    /// image (the shop/mall manager [[0x7D5D98]], the per-subcmd cooldown/skill
    /// virtuals, the online-object table [[0x7D6D50]], the std-item table
    /// [[0x7D5D6C]], the giver's money/pending-gift slots), so per §铁律 fail-closed
    /// those are withheld rather than answered with invented bytes. No SM constant
    /// needed adding — SM 0xD27 already exists as SmIdentConstsA.SM_3367 (3367); it
    /// is only referenced in the notes below because we never reach a faithful send.
    ///
    /// Dispatcher frame (sub_6D7D68 @0x6D7D68..0x6D7D97, verified against
    /// SystemModule/Data/TProcessMessage.cs): [ebp-4]=Self, [ebp-8]=body string,
    /// [ebp-0x34]=wire record, ESI/EDI=body length. Record -> message fields:
    /// [rec+0]=Recog=nParam1, word[rec+6]=Param=nParam2, word[rec+8]=Tag=nParam3,
    /// word[rec+0xA]=Series=wParam, body=sMsg, body length=nBodyLen.
    ///
    /// INTEGRATOR HOOKUP (this file NEVER edits the Operate() switch or the Q1/Q3
    /// files). In TPlayObject.Message.cs Operate()'s `default:` arm the handlers are
    /// chained with `&amp;&amp;` (around line 3075). Insert this call ONCE, immediately
    /// BEFORE `!TryHandleNativeCmQ1(ProcessMsg)`, so a TimedActivity ident is claimed
    /// before both the Q1 (CM 1059) and the Q3 (CM 3344/3410) fall-throughs:
    ///
    ///     if (!TryHandleXxx(ProcessMsg)
    ///         &amp;&amp; ...
    ///         &amp;&amp; !TryHandleTimedActivityCm(ProcessMsg)   // &lt;-- add: 1059 before Q1; 3344/3410 before Q3
    ///         &amp;&amp; !TryHandleNativeCmQ1(ProcessMsg)
    ///         &amp;&amp; !TryHandleNativeCmQ2(ProcessMsg)
    ///         &amp;&amp; !TryHandleNativeCmQ3(ProcessMsg))
    ///     {
    ///         result = base.Operate(ProcessMsg);
    ///     }
    ///
    /// One insertion suffices because the chain is left-to-right and short-circuits.
    /// If a surgical split is preferred instead, the same call may be placed before
    /// `!TryHandleNativeCmQ1` (for CM 1059) and again before `!TryHandleNativeCmQ3`
    /// (for CM 3344/3410); the method switches on wIdent, so the second call simply
    /// returns false for the already-unmatched idents.
    /// </summary>
    public partial class TPlayObject
    {
        // --- CM 1059 throttle state (native [self+0x744]/[self+0x757]) ----------------------
        // The C# port models player state as named fields rather than a byte blob, so these
        // mirror the two native offsets the 0x6D7794 gate reads/writes. The one-shot arm flag
        // (0x757) is set only by the (unported) code that queues a 限时活动 confirmation; while
        // no ported path arms it the gate stays closed and CM 1059 is inert — exactly as a
        // native server behaves when the flag was never raised.

        /// <summary>Last GetTickCount at which the CM 1059 confirm fired — native dword[self+0x744].</summary>
        private int m_dwTimedActivityConfirmTick;

        /// <summary>One-shot "confirm pending" arm flag — native byte[self+0x757].</summary>
        private bool m_boTimedActivityConfirmPending;

        /// <summary>
        /// TimedActivity CM entry point. Returns true when the ident is one of the
        /// three this subsystem owns (so the Operate() chain short-circuits), false
        /// otherwise. See the class remarks for the exact hookup location.
        /// </summary>
        private bool TryHandleTimedActivityCm(TProcessMessage processMessage)
        {
            switch (processMessage.wIdent)
            {
                case Grobal2.CM_1059:
                    TimedActivityConfirm();
                    return true;
                case Grobal2.CM_3344:
                    TimedActivitySkillRefresh();
                    return true;
                case Grobal2.CM_3410:
                    TimedActivityGiveItem(processMessage.nBodyLen);
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>
        /// CM 1059 (限时活动确认), leaf 0x6D9554 -> worker 0x6D7794 -> 0x6E3944.
        ///
        /// Worker 0x6D7794 (Self in EAX) — the 10 s throttle gate, reproduced 1:1:
        /// <code>
        /// 006D779D  call 0x408340                  ; EAX = GetTickCount()  (now)
        /// 006D77A2  cmp byte[esi+0x757],0
        /// 006D77A9  je   0x6D77D3                  ; arm flag == 0 -> return 0 (silent)
        /// 006D77AD  sub  edx,[esi+0x744]           ; edx = now - lastTick
        /// 006D77B3  cmp  edx,0x2710                ; 10000 ms
        /// 006D77B9  jb   0x6D77D3                  ; within 10 s -> return 0 (silent)
        /// 006D77BB  mov  [esi+0x744],eax           ; stamp lastTick = now
        /// 006D77C1  mov  byte[esi+0x757],0         ; clear the one-shot arm flag
        /// 006D77CC  call 0x6E3944(self, dl=1)      ; fire the confirmed activity
        /// </code>
        /// 0x6E3944 packs {word[self+0x278] (| 0x10000 when byte[self+0x182A]!=0), 0, 0}
        /// with cl=1 and dx=0x67 and calls the submit wrapper 0x6D3694, which hands the
        /// record to shop/mall manager [[0x7D5D98]] (0x637A00). That manager's runtime
        /// state — not a constant in the image — decides the effect and any reply, so the
        /// terminal is fail-closed. Both throttle branches produce NO reply, so the gate
        /// is fully reproduced; only the fire path is withheld (and is unreachable today
        /// because no ported code arms byte[self+0x757]).
        /// </summary>
        private void TimedActivityConfirm()
        {
            int now = HUtil32.GetTickCount();

            // 0x6D77A2: cmp byte[self+0x757],0 / je -> the confirmation was never armed.
            if (!m_boTimedActivityConfirmPending)
            {
                return;
            }

            // 0x6D77AD: (now - [self+0x744]) < 0x2710 -> still inside the 10 s window.
            // Cast to uint to mirror the native unsigned `cmp/jb`.
            if ((uint)(now - m_dwTimedActivityConfirmTick) < 0x2710u)
            {
                return;
            }

            // 0x6D77BB/0x6D77C1: stamp the tick and consume the one-shot arm flag.
            m_dwTimedActivityConfirmTick = now;
            m_boTimedActivityConfirmPending = false;

            // 0x6D77CC -> 0x6E3944 -> 0x6D3694: submit subcmd 0x67 to shop/mall manager
            // [[0x7D5D98]]. The manager is not modelled -> fail-closed (withhold).
            NativeCmQ1FailClosed.Drop(Grobal2.CM_1059, m_sCharName);
        }

        /// <summary>
        /// CM 3344 (技能刷新 / skill refresh with per-subcmd cooldown), leaf 0x6DADD6 ->
        /// worker 0x6EC5D8(Self). The leaf passes no body, so there is no derivable
        /// pre-gate; the worker's very first act reads unmodelled state.
        ///
        /// The 台账's "[+0x1F0]/[+0x1F4]/[+0x290]" are VMT slots (called through [Self+0]),
        /// not data fields — this is a generic per-subcmd (0x78) cooldown mechanism:
        /// <code>
        /// 006EC609  call [vmt+0xE8](self,0x78,0)   ; resolve the refresh slot; 0 -> return (silent)
        /// 006EC625  call [vmt+0x1F4](self,0x78)    ; ESI = remaining cooldown ms
        /// 006EC62F  jne  0x6EC691                  ; ESI != 0 -> cooldown-remaining branch
        ///   cooldown &gt; 0: SysMsg (vmt+0xD4, cx=0xFFDB) "&lt;prefix&gt;还需要 &lt;esi/1000&gt; 秒！"
        ///   cooldown == 0:
        ///     006EC635  call 0x6BCE2C(self,0)      ; refresh action
        ///     006EC63E  call [vmt+0x290](self)
        ///     006EC658  call [vmt+0xD8](self,dx=0x2905,...,0x2F)   ; effect broadcast (RM 10501)
        ///     006EC66B  call [vmt+0xD4](self,cx=0xFFDB,"已刷新技能！")
        ///     006EC689  call [vmt+0x1F0](self,0x78, 0x741698(self,0))  ; arm the next cooldown
        /// </code>
        /// Every branch is selected by, and every reply carries a value from, the
        /// per-subcmd cooldown/skill virtuals (vmt+0xE8/0x1F4/0x1F0/0x290) which this
        /// port does not model. The vmt+0xE8 slot gate cannot even be evaluated, so the
        /// whole command is fail-closed — nothing derivable to reproduce.
        /// </summary>
        private void TimedActivitySkillRefresh()
        {
            NativeCmQ3FailClosed.Q3Drop(Grobal2.CM_3344, m_sCharName);
        }

        /// <summary>
        /// CM 3410 (物品赠送确认 / give-item-to-player), leaf 0x6DAED9 -> worker 0x6EBE50.
        ///
        /// Leaf gate (reproduced 1:1): <c>cmp edi,0x28 / jne 0x6DBC2C</c> — anything whose
        /// body length is not exactly 0x28 (40) bytes is dropped silently. The leaf then
        /// decodes the fixed 40-byte record and calls the worker:
        /// <code>
        ///   body[0x00..0x0F]  char[16] targetName   -> worker edx (edi)   [0x405774 PChar->str]
        ///   body[0x10..0x1F]  char[16] itemName     -> worker ecx ([ebp-4])
        ///   body[0x20]        int32   amount        -> worker [ebp+0xC] (esi)
        ///   body[0x24]        int32   unitPrice     -> worker [ebp+8]
        /// </code>
        /// Worker 0x6EBE50 validates in this exact order, replying via vmt+0x250
        /// (SendDefMessage, SM 0xD27 = SmIdentConstsA.SM_3367) with a negative Recog:
        /// <code>
        ///   amount &lt;= 0                                   -> return (silent)
        ///   targetName empty                             -> return (silent)
        ///   target not in [[0x7D6D50]] (0x652784)        -> SM 0xD27 Recog=-1
        ///   itemName not in [[0x7D5D6C]] (0x74C2D4)      -> SM 0xD27 Recog=-2
        ///   CompareText(selfName, targetName) equal      -> SM 0xD27 Recog=-3
        ///   amount*unitPrice &gt; money[self+0x760]         -> SM 0xD27 Recog=-4
        ///   amount &gt; targetCap (0x7481F4(targetObj))     -> SM 0xD27 Recog=-4
        ///   charge fails (0x6D3694 dx=0x7D, ecx=0x2795)  -> SM 0xD27 Recog=-6
        ///   success: store pending gift {[self+0xA10]=targetName, [self+0xA14]=itemName,
        ///            [self+0xA18]=amount} and notify the target
        ///            "%s正在赠送 %s 给您，请别离开" (0x6E148C) — no SM to the sender
        /// </code>
        /// The record LAYOUT is fully modelled above, but every SM 0xD27 send sits behind
        /// the online-object table, the std-item table, the giver's money and the charge/
        /// pending-gift slots — none modelled, and the reject ladder is sequential, so no
        /// single Recog is derivable in isolation. Per §铁律 the 40-byte content is NOT
        /// interpreted and every SM 0xD27 reply is withheld; only the length gate is
        /// reproduced (matching native silence for a wrong-length body).
        /// </summary>
        private void TimedActivityGiveItem(int nBodyLen)
        {
            // 0x6DAED9: cmp edi,0x28 / jne 0x6DBC2C — wrong-length body is native silence.
            if (nBodyLen != 0x28)
            {
                return;
            }

            // 40-byte record decoded above; the give-item transaction (online lookup,
            // item table, money, charge, pending-gift, target notify, SM 0xD27 ladder)
            // is unmodelled -> fail-closed (withhold).
            NativeCmQ3FailClosed.Q3Drop(Grobal2.CM_3410, m_sCharName);
        }
    }
}
