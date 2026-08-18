using GameSvr.Services;
using SystemModule;

namespace GameSvr
{
    /// <summary>
    /// CM idents 3180..4124 — the third quarter of 战神's missing client-message
    /// handlers (dispatcher sub_6D7D68, selector root 0x6D805C reading
    /// word[record+4]). The 99-ident native-arm/no-C#-case gap is quartered
    /// 25/25/24/25 by cm-2's authoritative tooling; this file restores items
    /// 51..74. Every ident here resolves to a REAL tree leaf, never the shared
    /// exit label 0x6DBC2C.
    ///
    /// HOOKING (this port never edits TPlayObject.Message.cs's Operate() switch):
    /// wire this in from the Operate() default arm, ahead of the social chain, e.g.
    ///     default:
    ///         if (!TryHandleNativeCmQ3(ProcessMsg) &&
    ///             !TryHandleNativeSocialProtocol(ProcessMsg))
    ///         {
    ///             result = base.Operate(ProcessMsg);
    ///         }
    ///         break;
    /// Today these 24 idents fall through default -> TryHandleNativeSocialProtocol
    /// (which returns false for them) -> base.Operate; inserting the call above
    /// claims them without touching any shared file.
    ///
    /// Dispatcher frame (0x6D7D68..0x6D7D97): [ebp-4]=Self, [ebp-8]=body string,
    /// [ebp-0x34]=wire record, ESI/EDI=body length. The 12-byte record is
    /// {int Recog @0; word Ident @4; word Param @6; word Tag @8; word Series @0xA},
    /// delivered here as nParam1=Recog, nParam2=Param, nParam3=Tag, wParam=Series,
    /// sMsg=body, nBodyLen=body length (see SystemModule/Data/TProcessMessage.cs).
    ///
    /// Reply-slot order is pinned off the senders (same as the CM-tail port):
    /// vmt+0x250 (sub_6D7CB0, ret 0x10) => SendDefMessage(SM, Recog=ecx, Param,
    /// Tag, Series, body) with the four stack pushes being Param/Tag/Series/body.
    /// Only pre-gates evaluable from modelled state are reproduced 1:1; every
    /// terminal action that reads unmodelled state is withheld via
    /// NativeCmQ3FailClosed (see that file for per-ident evidence).
    /// </summary>
    public partial class TPlayObject
    {
        private bool TryHandleNativeCmQ3(TProcessMessage processMessage)
        {
            switch (processMessage.wIdent)
            {
                case Grobal2.CM_3180: Q3Cm3180(processMessage.nBodyLen); return true;
                case Grobal2.CM_3190: Q3Cm3190(); return true;
                case Grobal2.CM_3191: Q3Cm3191(); return true;
                case Grobal2.CM_3208: Q3Cm3208(); return true;
                case Grobal2.CM_3209: Q3Cm3209(); return true;
                case Grobal2.CM_3282: Q3Cm3282(processMessage.nBodyLen); return true;
                case Grobal2.CM_3283: Q3Cm3283(); return true;
                case Grobal2.CM_3284: Q3Cm3284(); return true;
                case Grobal2.CM_3285: Q3Cm3285(); return true;
                case Grobal2.CM_3286: Q3Cm3286(); return true;
                case Grobal2.CM_3287: Q3Cm3287(); return true;
                case Grobal2.CM_3288: Q3Cm3288(); return true;
                case Grobal2.CM_3294: Q3Cm3294(processMessage.nBodyLen); return true;
                case Grobal2.CM_3295: Q3Cm3295(processMessage); return true;
                case Grobal2.CM_3306: Q3Cm3306(processMessage.nBodyLen); return true;
                case Grobal2.CM_3307: Q3Cm3307(); return true;
                case Grobal2.CM_3340: Q3Cm3340(); return true;
                case Grobal2.CM_3344: Q3Cm3344(); return true;
                case Grobal2.CM_3410: Q3Cm3410(processMessage.nBodyLen); return true;
                case Grobal2.CM_3503: Q3Cm3503(); return true;
                case Grobal2.CM_4102: Q3Cm4102(); return true;
                case Grobal2.CM_4105: Q3Cm4105(); return true;
                case Grobal2.CM_4123: Q3Cm4123(processMessage.nParam3); return true;
                case Grobal2.CM_4124: Q3Cm4124(processMessage.nParam3); return true;
                default:
                    return false;
            }
        }

        /// <summary>
        /// CM 3180, leaf 0x6DA405 (`0F B7 CE 8B 55 F8 8B 45 FC E8..`), worker
        /// 0x6E3280(Self, body=[ebp-8], len=ECX).
        ///
        /// The worker gates on the body length at 0x6E32A0 `83 F9 18 cmp ecx,0x18` /
        /// `0F 8C.. jl 0x6E3428`; the jl target is the shared reply at 0x6E3428
        /// which — with EDI still 0 from 0x6E329E `33 FF xor edi,edi` — sends
        /// SM 0x6BF (1727) with Recog=EDI and an all-zero body via vmt+0x250. That
        /// short-body reject is fully derivable, so it is reproduced. For
        /// nBodyLen>=0x18 the worker resolves objects by name (0x73CF08), matches a
        /// 5×6 slot table off [obj+0x1C]+0x44 and answers SM 0x24 detail packets
        /// then the SM 0x6BF status — none of that roster structure is modelled.
        /// </summary>
        private void Q3Cm3180(int nBodyLen)
        {
            if (nBodyLen < 0x18)
            {
                SendDefMessage(Grobal2.SM_1727, 0, 0, 0, 0, string.Empty);
                return;
            }

            NativeCmQ3FailClosed.Q3Drop(Grobal2.CM_3180, m_sCharName);
        }

        /// <summary>
        /// CM 3190, leaf 0x6DA5AE, worker 0x6E590C(Self, Recog=[record]). The worker
        /// scans the member collection [Self+0x508] for the entry whose [+0x18]
        /// equals Recog (0x6E5930 loop via 0x424D4C), resolves it against the
        /// manager [[0x7D5D6C]] (0x752A20) and answers SM 0xB40 with Recog = that
        /// result. The collection and manager are not modelled, so the Recog cannot
        /// be derived and the reply is withheld.
        /// </summary>
        private void Q3Cm3190() => NativeCmQ3FailClosed.Q3Drop(Grobal2.CM_3190, m_sCharName);

        /// <summary>
        /// CM 3191, leaf 0x6DA5C0, worker 0x6E5BA8(Self, Recog=[record]). The leaf
        /// first decodes the server date [[0x7D6A88]] through 0x40EB9C and gates on
        /// its month/day (0x6DA5DC `cmp word[ebp-0xC],0` / `cmp word[ebp-0xE],5` /
        /// `jb 0x6DBC2C`); that date global is not modelled, so the gate cannot be
        /// evaluated. The worker itself walks the [Self+0x508] member collection and
        /// answers SM 0xB41, also unmodelled. Withheld.
        /// </summary>
        private void Q3Cm3191() => NativeCmQ3FailClosed.Q3Drop(Grobal2.CM_3191, m_sCharName);

        /// <summary>
        /// CM 3208, leaf 0x6DA54C, worker 0x6EA5E0(Self, Recog=[record],
        /// Param=word[+6], len=SI, body=[ebp-8]). The worker drives the task-publish
        /// board script object [[0x7D5D20]] against the object table [[0x7D6D50]] and
        /// the per-player field [+0xA1C]. The board's @Main-style procedures are not
        /// modelled in this port, so the command's effect and reply are withheld.
        /// </summary>
        private void Q3Cm3208() => NativeCmQ3FailClosed.Q3Drop(Grobal2.CM_3208, m_sCharName);

        /// <summary>
        /// CM 3209, leaf 0x6DA56D, worker 0x6EA858(Self, Recog=[record],
        /// Param=word[+6], len=SI, body=[ebp-8]). Same task board [[0x7D5D20]] as
        /// CM 3208; the short-parameter leg (0x6EA8B2 `cmp [ebp+8],0x14` / `jle`)
        /// converges at 0x6EACDE which emits notice 0x3008 through 0x765E68 rather
        /// than staying silent, so even that leg drives unported board/notice code.
        /// Withheld.
        /// </summary>
        private void Q3Cm3209() => NativeCmQ3FailClosed.Q3Drop(Grobal2.CM_3209, m_sCharName);

        /// <summary>
        /// CM 3282, leaf 0x6DA600, worker 0x6E64BC(Self, body=[ebp-8], len=ECX). The
        /// worker gates on the body length at 0x6E64E1 `83 F9 14 cmp ecx,0x14` /
        /// `jl 0x6E661C`; that jl target is a bare SEH teardown + ret — 战神 sends
        /// nothing for a short body — so it is reproduced as silence. For
        /// nBodyLen>=0x14 the worker resolves an object by name (0x73CF08), reads
        /// [obj+0x3C0] and answers SM 0xCD3 (vmt+0x254 then +0x250); that object
        /// table is not modelled, so the reply is withheld.
        /// </summary>
        private void Q3Cm3282(int nBodyLen)
        {
            if (nBodyLen < 0x14)
            {
                return;
            }

            NativeCmQ3FailClosed.Q3Drop(Grobal2.CM_3282, m_sCharName);
        }

        /// <summary>
        /// CM 3283, leaf 0x6DA626, worker 0x6E67B0(Self, Recog=[record]). The worker
        /// rebuilds the slot collection at [Self+0x9F4]/[+0x9F8]/[+0x9FC] against the
        /// template [[0x7D64B8]] and formats a reply body from it. Those fields and
        /// the template are not modelled, so the body cannot be built. Withheld.
        /// </summary>
        private void Q3Cm3283() => NativeCmQ3FailClosed.Q3Drop(Grobal2.CM_3283, m_sCharName);

        /// <summary>
        /// CM 3284 is owned by the earlier TryHandleQiankunCm route. This fallback
        /// remains fail-closed so Q3 never becomes a second owner of the same ident.
        /// </summary>
        private void Q3Cm3284() => NativeCmQ3FailClosed.Q3Drop(Grobal2.CM_3284, m_sCharName);

        /// <summary>
        /// CM 3285, leaf 0x6DA638 (`cmp word[record+6],1 / sete dl`), worker
        /// 0x6E6DE8(Self, DL=(Param==1)). The worker indexes the slot collection
        /// [Self+0x9F4]/[+0x9FC] (0x6E6E00 `cmp edx,[ebx+0x9F4]`) and applies an
        /// action through 0x6E68A8/0x6DF62C/0x6D3694. That collection is not
        /// modelled, so the action and any reply are withheld.
        /// </summary>
        private void Q3Cm3285() => NativeCmQ3FailClosed.Q3Drop(Grobal2.CM_3285, m_sCharName);

        /// <summary>
        /// CM 3286 is likewise owned by TryHandleQiankunCm ahead of Q3. The live
        /// handler closes the empty-list reset leg and withholds config-backed
        /// non-empty rewards.
        /// </summary>
        private void Q3Cm3286() => NativeCmQ3FailClosed.Q3Drop(Grobal2.CM_3286, m_sCharName);

        /// <summary>
        /// CM 3287, leaf 0x6DA895, worker 0x6E8734(Self, Recog=[record],
        /// ECX=MakeLong(Param,Tag,Series) via 0x408D40). The worker drives the
        /// pet/summon subsystem anchored at [[0x7D6784]] and the fields
        /// [+0x128]/[+0x760]/[+0x9A0]. None of those are modelled, so the command
        /// and reply are withheld.
        /// </summary>
        private void Q3Cm3287() => NativeCmQ3FailClosed.Q3Drop(Grobal2.CM_3287, m_sCharName);

        /// <summary>
        /// CM 3288, leaf 0x6DA8C4, worker 0x6E8820(Self, Recog=[record],
        /// ECX=MakeLong(Param,Tag,Series)). Same pet/summon subsystem as CM 3287
        /// plus [Self+0x178]; the internal 0x6E882C `cmp ecx,1` gate still leads
        /// into the unported [[0x7D6784]] code, so the action is withheld.
        /// </summary>
        private void Q3Cm3288() => NativeCmQ3FailClosed.Q3Drop(Grobal2.CM_3288, m_sCharName);

        /// <summary>
        /// CM 3294, leaf 0x6DA613, worker 0x6EB190(Self, body=[ebp-8], len=EBX). The
        /// worker gates on body length at 0x6EB1A6 `83 FB 04 cmp ebx,4` /
        /// `jl 0x6EB282`; that jl target pops the frame and returns without sending,
        /// so a short body is reproduced as silence. For nBodyLen>=4 the worker
        /// builds a stall/display record from [Self+0x3C4]/[+0xB74]/[+0x18BC]/[+0x18C0]
        /// and the globals [[0x7D5D6C]]/[[0x7D7038]]/[[0x7D6784]], answering
        /// SM 0xCF1/0xB9A. Those fields are not modelled, so the reply is withheld.
        /// </summary>
        private void Q3Cm3294(int nBodyLen)
        {
            if (nBodyLen < 4)
            {
                return;
            }

            NativeCmQ3FailClosed.Q3Drop(Grobal2.CM_3294, m_sCharName);
        }

        /// <summary>
        /// CM 3295 fallback. The normal owner is TryHandleNameQueryCm, which runs
        /// before Q3; keeping this arm on the same implementation prevents a
        /// future dispatcher-order change from reverting the command to silence.
        /// </summary>
        private void Q3Cm3295(TProcessMessage processMessage)
            => HandleNativeCm3295(processMessage);

        /// <summary>
        /// CM 3306, leaf 0x6DAB39: `66 83 FE 04 cmp si,4` / `0F 82.. jb 0x6DBC2C`
        /// silently drops any body shorter than 4 bytes, then calls worker
        /// 0x6EFD54(Self, Recog=[record], ECX=[[ebp-8]], Tag=word[+8], Param=word[+6]).
        /// The short-body silence is reproduced. The worker keys off the name
        /// [Self+0x106] and [+0x12C]/[+0x130]/[+0x24C] plus [[0x7D5D6C]], answering
        /// SM 0x275/0x276; those fields are not modelled, so the reply is withheld.
        /// </summary>
        private void Q3Cm3306(int nBodyLen)
        {
            if (nBodyLen < 4)
            {
                return;
            }

            NativeCmQ3FailClosed.Q3Drop(Grobal2.CM_3306, m_sCharName);
        }

        /// <summary>
        /// CM 3307, leaf 0x6DABEA, worker 0x6CBD78(Self, Recog=[record]). The worker
        /// walks the member collection [Self+0x508] and the fields [+0x258]/[+0x248]
        /// with the managers [[0x7D5D6C]]/[[0x7D5F20]], answering SM 0xD04. The
        /// collection and managers are not modelled, so the reply is withheld.
        /// </summary>
        private void Q3Cm3307() => NativeCmQ3FailClosed.Q3Drop(Grobal2.CM_3307, m_sCharName);

        /// <summary>
        /// CM 3340, leaf 0x6DAC30: `A1 38 70 7D 00 mov eax,[0x7D7038]` /
        /// `F6 40 03 20 test byte[eax+3],0x20` / `je 0x6DBC2C` gates on a config
        /// flag before calling worker 0x79E78C with two strings copied from
        /// [Self+0x106] and [Self+0xB09] and the manager [[0x7D5ECC]]. That config
        /// flag is read from the five-byte ServerSwitch store. The [[0x7DD050]]
        /// text-command subsystem remains unmodelled, so the worker is withheld.
        /// </summary>
        private void Q3Cm3340()
        {
            if (!NativeClientVersionPolicy.IsClientInfoCollectionEnabled())
            {
                return;
            }

            NativeCmQ3FailClosed.Q3Drop(Grobal2.CM_3340, m_sCharName);
        }

        /// <summary>
        /// CM 3344, leaf 0x6DADD6, worker 0x6EC5D8(Self). The worker reads
        /// [Self+0x1F0]/[+0x1F4]/[+0x290] and drives 0x6BCE2C/0x741698. Those fields
        /// are not modelled, so the action and any reply are withheld.
        /// </summary>
        private void Q3Cm3344() => NativeCmQ3FailClosed.Q3Drop(Grobal2.CM_3344, m_sCharName);

        /// <summary>
        /// CM 3410, leaf 0x6DAED9: `83 FF 28 cmp edi,0x28` / `0F 85.. jne 0x6DBC2C`
        /// silently drops anything whose body length (EDI = zero-extended nBodyLen)
        /// is not exactly 0x28, then reads the 0x28-byte record ([body+0x10],
        /// [+0x20], [+0x24]) into worker 0x6EBE50. The length gate is reproduced as
        /// silence. The worker walks [Self+0x760]/[+0xA10]/[+0xA14]/[+0xA18] and the
        /// object table [[0x7D6D50]], answering SM 0xD27; those fields are not
        /// modelled, so the reply is withheld.
        /// </summary>
        private void Q3Cm3410(int nBodyLen)
        {
            if (nBodyLen != 0x28)
            {
                return;
            }

            NativeCmQ3FailClosed.Q3Drop(Grobal2.CM_3410, m_sCharName);
        }

        /// <summary>
        /// CM 3503, leaf 0x6DAF44, worker 0x6EF970(Self). The worker returns at once
        /// when there is no hero (0x6EF98E `mov eax,[Self+0xBB0]` / `test eax` /
        /// `je 0x6EF9D8` — a bare teardown), so the no-hero case is reproduced as
        /// silence. With a hero, 0x690A24 returns -1/-2 which selects a notice
        /// string (0x6EFA04/0x6EFA20) sent as SM 0x38FF via vmt+0xD4; that hero
        /// check is not modelled, so the notice is withheld.
        /// </summary>
        private void Q3Cm3503()
        {
            if (m_HeroObject == null)
            {
                return;
            }

            NativeCmQ3FailClosed.Q3Drop(Grobal2.CM_3503, m_sCharName);
        }

        /// <summary>
        /// CM 4102, leaf 0x6DABFC, worker 0x6B7BCC(Self, Param=word[+6], Tag=word[+8],
        /// len=ESI, body=[ebp-8]). The short-body leg (0x6B7BD5 `cmp ebx,0xC` / `jb`)
        /// is NOT silent — it stores Param/Tag into [Self+0x18DC]/[+0x18DE]
        /// (0x6B7C03) — and the main path reads the trade/market globals
        /// [[0x7D62DC]]/[[0x7D6214]]/[[0x7D5C0C]]/[[0x7D6038]]/[[0x7D5D98]] with
        /// [+0x18DC]/[+0x18DE]/[+0xAF4]. None of those are modelled, so the command
        /// and its side effects are withheld.
        /// </summary>
        private void Q3Cm4102() => NativeCmQ3FailClosed.Q3Drop(Grobal2.CM_4102, m_sCharName);

        /// <summary>
        /// CM 4105, leaf 0x6DA005, which fires three workers in order.
        /// 0x7742C0(Self) drops stealth state 0x40 and re-broadcasts RM_TURN
        /// (0x774317 `mov dx,0x2711`) — modelled as BreakNativeStealthOnAction.
        /// 0x6BCE2C(Self, Ident=word[+4]) cancels the pending channels, emitting
        /// 0x4D0 / 0x4D2 / 0xD57 (0x6EE164, 0x6EF62E, 0x6EE2DF) — modelled as
        /// CancelNativeActionChannels; note its Ident argument is dead, since all
        /// three callees open with `mov edx,eax`. 0x6EE174(Self, Ident) is the mount
        /// summon and drives [+0x4C0]/[+0xA24]/[+0x1914]. Only that third worker is
        /// still unmodelled, but it is the bulk of the leaf, so the arm stays
        /// withheld rather than emitting two thirds of a native response.
        /// </summary>
        private void Q3Cm4105() => NativeCmQ3FailClosed.Q3Drop(Grobal2.CM_4105, m_sCharName);

        /// <summary>
        /// CM 4123, leaf 0x6DAE32, worker 0x6BF908(Self, Recog=ECX, Tag=EDX,
        /// len=SI, body=[ebp-8]). The worker selects on Tag: 0x6BF916 `cmp [ebp-4],1`
        /// (Tag) / `jne 0x6BF9D2` and 0x6BF920 `cmp [Self+0xBB0],0` / `je 0x6BF9D2`
        /// route Tag==1-with-hero to the hero leg, else fall to 0x6BF9D2 where
        /// `cmp [ebp-4],0` / `jne 0x6BFA63` sends SM 0xFC3 with Recog=1 (0x6BFA6B
        /// `mov ecx,1`). That invalid-target reject — Tag!=0 and not(Tag==1 &&
        /// hero) — is reproduced. The Tag==0 and Tag==1&&hero legs consume the
        /// soul-wash counters [+0x610]/[+0x5A4] via 0x747B38/0x747878/0x74738C, which
        /// are not modelled, so their SM 0xFC3 outcome is withheld.
        /// </summary>
        private void Q3Cm4123(int nTag)
        {
            if (nTag != 0 && !(nTag == 1 && m_HeroObject != null))
            {
                SendDefMessage(Grobal2.SM_4035, 1, 0, 0, 0, string.Empty);
                return;
            }

            NativeCmQ3FailClosed.Q3Drop(Grobal2.CM_4123, m_sCharName);
        }

        /// <summary>
        /// CM 4124, leaf 0x6DAE53, worker 0x6BFA88(Self, Recog=ECX, Tag=EDX,
        /// len=SI, body=[ebp-8]) — the sibling of CM 4123. 0x6BFA93 `cmp edx,1`
        /// (Tag) / `jne 0x6BFB54` and 0x6BFA9C `cmp [Self+0xBB0],0` / `je 0x6BFB54`
        /// route Tag==1-with-hero to the hero leg; otherwise at 0x6BFB54
        /// `test edx,edx` / `jne 0x6BFBF0` sends SM 0xFC3 with Recog=0 (0x6BFBF8
        /// `xor ecx,ecx`). That invalid-target reject is reproduced (note Recog=0
        /// here vs Recog=1 for CM 4123). The Tag==0 / Tag==1&&hero legs need the
        /// soul-wash counter [+0x610] and the chain 0x747878/0x746EA8/0x747444,
        /// which are not modelled, so their outcome is withheld.
        /// </summary>
        private void Q3Cm4124(int nTag)
        {
            if (nTag != 0 && !(nTag == 1 && m_HeroObject != null))
            {
                SendDefMessage(Grobal2.SM_4035, 0, 0, 0, 0, string.Empty);
                return;
            }

            NativeCmQ3FailClosed.Q3Drop(Grobal2.CM_4124, m_sCharName);
        }
    }
}
