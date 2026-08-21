using System.Collections.Generic;
using GameSvr.Services;
using SystemModule;

namespace GameSvr
{
    /// <summary>
    /// 答题/广播 (quiz / cross-server broadcast) subsystem — CM 1090 / 1200 / 1217.
    ///
    /// These three idents live in the CM missing-set Quarter 1 (see
    /// <see cref="NativeCmQ1FailClosed"/>); cm-1 registered them fail-closed. This part
    /// upgrades them to a faithful 1:1 disposition derived from the three-piece worker set
    /// 0x6BD674 / 0x6C53B8 / 0x71315C plus the broadcast singleton [[0x7D62DC]]. The
    /// derivation is reproduced below so the disposition is auditable without re-running
    /// capstone (tools/cm1_re/_qb_probe*.py, _qb_xref.py, _qb_mgr.py).
    ///
    /// ============================================================================
    /// PIECE 1 — worker 0x6BD674  (CM 1090 with cl=0; CM 1200 with cl=(Param==1))
    /// ----------------------------------------------------------------------------
    /// Register-frame in:  eax=Self, edx=answer body string, cl=mode flag.
    ///   CM 1090 leaf 0x6D9732: `xor ecx,ecx / xor edx,edx / call 0x6BD674` -> cl=0, body=null.
    ///   CM 1200 leaf 0x6DA21F: copies [ebp-8] body to edx, `cmp word[rec+6],1 / sete cl`
    ///                          -> cl=(Param==1), body=client answer.
    ///
    /// Every terminal action in 0x6BD674 is guarded by the per-player answer-state block:
    ///   [Self+0x7B0] dword  — cooldown/timer (set 0x5DC on reject)         (writers: this worker + poser)
    ///   [Self+0x7B4] dword  — speak/answer count (cap 3)                   (inc at 0x6BD6AE)
    ///   [Self+0x7B8] char[] — the correct answer text (empty => wrong)     (read via 0x405774 at 0x6BD7AE)
    ///   [Self+0x7C3] byte   — quiz-active flag                             (SET=1 only by poser 0x6D6644)
    ///   [Self+0x7C4] byte   — answer-window-open flag                      (SET=1 only by driver 0x6DCF30)
    ///
    ///   0x6BD699 test cl,cl / je 0x6BD76A:
    ///     cl!=0 (CM 1200 Param==1):
    ///       0x6BD6A1 cmp [Self+0x7C3],0 / je 0x6BD76A   (inactive -> fall to the cl=0 label)
    ///       0x6BD6AE inc [Self+0x7B4]; cmp >3:
    ///         >3  -> reset the whole block, [0x7B0]=0x5DC, SM 0x38FF
    ///                '你超过了三次仍未答题，连接中断' (0x6BD89C) via vtbl+0xD4,
    ///                then 0x765E68(Self,Self,cx=0x2710) — the 10000ms disconnect countdown.
    ///         <=3 -> build a 0x48 broadcast record {word[+0]=subcmd 0x159, name[Self+0xAF4]@+0x10,
    ///                map[Self+0x106]@+0x25} and push it via [[0x7D62DC]] 0x71315C (PIECE 3).
    ///     cl==0 label 0x6BD76A (this is where CM 1090 ALWAYS lands, body empty):
    ///       0x6BD76A cmp [Self+0x7C3],0 / je 0x6BD864   (inactive -> return, NOTHING done)
    ///       0x6BD777 cmp [Self+0x7C4],0 / je 0x6BD864   (window closed -> return, NOTHING done)
    ///       else grade: reset [0x7B0]/[0x7B4]/[0x7C3]/[0x7C4]; if [0x7B8]==0 -> wrong path;
    ///         else CompareStr(0x40BD78) submitted-vs-stored:
    ///           equal   -> [0x7B8]=0, 0x7731C0(Self,dl=0x19), [[0x7D5D6C]] 0x750F3C(&item);
    ///                      item!=null -> give via 0x6C87B4(Self,item,cl=1,...),
    ///                      format '回答正确, 你获得奖励' (0x6BD8C4) + item, SM 0xFFDB (vtbl+0xD4).
    ///           unequal -> wrong path 0x6BD827: [0x7B8]=0, [0x7B0]=0x5DC,
    ///                      SM 0x38FF '回答错误，连接中断' (0x6BD8E4) via vtbl+0xD4, 0x765E68(...,10000).
    ///
    /// ============================================================================
    /// PIECE 2 — worker 0x6C53B8  (CM 1217)
    /// ----------------------------------------------------------------------------
    /// Register/stack-frame in: eax=Self, edx=body, cx=subcmd, [ebp+8]=arg1, [ebp+0xC]=arg2.
    ///   CM 1217 leaf 0x6DA372: `push 0 / push 0 / mov cx,0x165 / xor edx,edx / call 0x6C53B8`
    ///                          -> subcmd=0x165, arg1=arg2=0, body=null.
    /// UNCONDITIONAL (no answer-state gate). Builds a 0x48 record:
    ///   word[+0x00]=subcmd(0x165)  word[+0x02]=arg2  dword[+0x04]=arg1
    ///   char name[+0x10]=[Self+0xAF4] (0x14)  char map[+0x25]=[Self+0x106] (0xF)
    ///   char body[+0x35]=edx-body (0xF, via 0x4057AC then 0x4039E4)
    /// then pushes it via [[0x7D62DC]] 0x71315C (PIECE 3). ret 8.
    ///
    /// ============================================================================
    /// PIECE 3 — broadcast push 0x71315C  +  singleton [[0x7D62DC]]
    /// ----------------------------------------------------------------------------
    /// 0x71315C(eax=*mgr, edx=record, ecx=0, [ebp+8]=0):
    ///   block=AllocMem(0x54); FillChar(block,0x54,0);
    ///   dword[block+0x00]=0x33AABB77 (frame magic)  word[block+0x04]=1 (type: player record)
    ///   dword[block+0x08]=0x48 (payload len)        copy the 0x48 record to block+0x0C;
    ///   0x713CBC(mgr, block, len=0x54).
    /// 0x713CBC: if mgr.[+0x4C]!=0 -> direct send 0x4C93F8(mgr.[+0x38] session, block, len, 1);
    ///   else enqueue node{data,len} into the linked list head[mgr+0xE0]/tail[mgr+0xE4],
    ///   count[mgr+0xE8], under critical section [mgr+0xC8]; a network thread drains it to the peer.
    /// The same channel carries GM `reloadStditem` (0x713094 builds a type-2, 0xC-byte frame),
    /// which is how NativeGmItemExtraCommands names off_7D62DC. [[0x7D62DC]] is a POINTER to a
    /// global slot (0x7DC7D4) holding a manager constructed at runtime (86 read-sites, never a
    /// static object): the server-group CENTER / cross-server 0x33AABB77 message channel.
    ///
    /// ============================================================================
    /// FIDELITY DISPOSITION (有据不臆造)
    /// ----------------------------------------------------------------------------
    /// • [Self+0x7C3] and [Self+0x7C4] are written to 1 ONLY by the quiz poser (0x6D6644,
    ///   which first sends SM 0x3C6 '请更新到最新的客户端' via vtbl+0x250) and the answer-window
    ///   driver (0x6DCF30) — the client-version / anti-cheat challenge subsystem. That subsystem
    ///   is NOT ported (grep of GameSvr for 0x7c3/0x7c4/0x7b8/答题/回答正确/发言次数 finds only
    ///   these Q1 files), so in this port the flags are permanently 0 and [Self+0x7B8] is empty.
    ///   Therefore every branch of 0x6BD674 is guarded out and the worker performs NO observable
    ///   action. CM 1090 and CM 1200 are reproduced as a FAITHFUL SILENT no-op (not a withheld
    ///   reply — 战神 itself sends nothing here). The flag-gated inner actions (answer grading +
    ///   reward via [[0x7D5D6C]]/0x6C87B4, the answer broadcast, the over-limit disconnect) remain
    ///   unreachable AND depend on unmodelled managers, so they are fail-closed by construction.
    ///
    /// • CM 1217 has no answer-state gate: it ALWAYS pushes a 0x33AABB77 frame onto the center
    ///   channel [[0x7D62DC]]. That channel is not modelled in this GameSvr (all C# 0x33AABB77
    ///   usage is the separate DBSvr frame codec), so emitting the frame would put invented bytes
    ///   on an un-owned wire. CM 1217 stays FAIL-CLOSED via the shared Q1 gap ledger.
    ///
    /// ============================================================================
    /// INTEGRATOR HOOKUP (防冲突: this part does NOT edit TPlayObject.Message.cs)
    /// ----------------------------------------------------------------------------
    /// Insert the call to <see cref="TryHandleQuizBroadcastCm"/> in Operate()'s `default:` arm
    /// BEFORE TryHandleNativeCmQ1 so the faithful quiz/broadcast disposition takes precedence
    /// over the Q1 fail-closed arms for 1090/1200/1217 —
    ///
    ///     default:
    ///         if (!TryHandleNativeSocialProtocol(ProcessMsg)
    ///             &amp;&amp; !TryHandleNativeCmTailProtocol(ProcessMsg)
    ///             &amp;&amp; !TryHandleQuizBroadcastCm(ProcessMsg)   // ← add this, before Q1
    ///             &amp;&amp; !TryHandleNativeCmQ1(ProcessMsg)
    ///             &amp;&amp; !TryHandleNativeCmQ2(ProcessMsg)
    ///             &amp;&amp; !TryHandleNativeCmQ3(ProcessMsg))
    ///         {
    ///             result = base.Operate(ProcessMsg);
    ///         }
    ///         break;
    /// </summary>
    public partial class TPlayObject
    {
        /// <summary>Native Self+0x7B0 quiz cooldown/timer.</summary>
        internal int m_nNativeQuizCooldown;

        /// <summary>Native Self+0x7B4 quiz speak/answer count.</summary>
        internal int m_nNativeQuizAnswerCount;

        // Idents whose faithful native behaviour is a silent no-op in this port (answer-state
        // dormant), surfaced once per ident so it reads as "faithful dormant", NOT as a gap/drop.
        private static readonly HashSet<int> s_quizDormantNoted = new HashSet<int>();

        private static readonly object s_quizDormantGate = new object();

        /// <summary>
        /// Faithful quiz/broadcast dispatch for CM 1090 / 1200 / 1217. Returns true when the
        /// ident is one of the three (so the caller short-circuits before the Q1 fail-closed
        /// arms). See the class-level comment for the full data-flow derivation and the
        /// INTEGRATOR HOOKUP note (insert before TryHandleNativeCmQ1).
        /// </summary>
        private bool TryHandleQuizBroadcastCm(TProcessMessage processMessage)
        {
            switch (processMessage.wIdent)
            {
                case Grobal2.CM_1090:
                    ClientQuizAnswerSelf1090();
                    return true;
                case Grobal2.CM_1200:
                    ClientQuizAnswerBody1200(processMessage.nParam2);
                    return true;
                case Grobal2.CM_1217:
                    ClientBroadcastSubmit1217();
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>
        /// CM 1090 -> 0x6BD674 with cl=0, body empty. cl=0 always lands at label 0x6BD76A whose
        /// first act is `cmp [Self+0x7C3],0 / je 0x6BD864` — with the poser unported the flag is 0,
        /// so 战神 returns having done nothing. Reproduced verbatim as a faithful silent no-op.
        /// </summary>
        private void ClientQuizAnswerSelf1090()
        {
            NoteQuizAnswerDormant(Grobal2.CM_1090);
        }

        /// <summary>
        /// CM 1200 -> 0x6BD674 with cl=(Param==1) and the client answer as the body. Whether
        /// cl is 0 or 1, the path is gated by [Self+0x7C3]: the cl=1 arm bails at
        /// `0x6BD6A1 cmp [Self+0x7C3],0 / je 0x6BD76A` and the cl=0 label bails at
        /// `0x6BD76A cmp [Self+0x7C3],0 / je 0x6BD864`. With the flag permanently 0 no branch
        /// runs (no count increment, no broadcast, no disconnect). Faithful silent no-op.
        /// </summary>
        /// <param name="nParam2">Param (word[rec+6]); cl=(Param==1) in 战神. Kept for parity/audit.</param>
        private void ClientQuizAnswerBody1200(int nParam2)
        {
            _ = nParam2;
            NoteQuizAnswerDormant(Grobal2.CM_1200);
        }

        /// <summary>
        /// CM 1217 -> 0x6C53B8 unconditionally builds {subcmd 0x165, name, map, body} and pushes
        /// a 0x33AABB77 type-1 frame onto the center/cross-server channel [[0x7D62DC]] (0x71315C).
        /// That channel is not modelled in this GameSvr, so the broadcast is fail-closed via the
        /// shared Q1 gap ledger (the 1217 entry already lives in NativeCmQ1FailClosed — reuse it).
        /// </summary>
        private void ClientBroadcastSubmit1217()
        {
            NativeCmQ1FailClosed.Drop(Grobal2.CM_1217, m_sCharName);
        }

        private void NoteQuizAnswerDormant(int ident)
        {
            lock (s_quizDormantGate)
            {
                if (!s_quizDormantNoted.Add(ident))
                {
                    return;
                }
            }

            M2Share.MainOutMessage(
                $"[答题/广播] CM {ident} 忠实空操作(dormant): worker 0x6BD674 每条分支均被答题态 " +
                $"[self+0x7C3]/[0x7C4] 门住，而置位它们的出题子系统(0x6D6644/0x6DCF30)未移植，故本端恒为 0，" +
                $"与 战神 无激活答题时的行为一致(不发包/不改状态); 角色=" +
                $"{(string.IsNullOrEmpty(m_sCharName) ? "<unknown>" : m_sCharName)}");
        }
    }
}
