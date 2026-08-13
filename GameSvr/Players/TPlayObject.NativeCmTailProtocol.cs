using System;
using GameSvr.Services;
using SystemModule;

namespace GameSvr
{
    /// <summary>
    /// CM idents 4125..4651 — the tail of 战神's client-message dispatcher.
    ///
    /// The dispatcher is sub_6D7D68; its selector tree is rooted at 0x6D805C and
    /// every arm that does nothing jumps to the shared exit label 0x6DBC2C
    /// (`33 C0 5A 59 59 64 89 10 E9 D5 00 00 00`, the SEH unwind + return). All
    /// idents handled here resolve to a REAL leaf, never to 0x6DBC2C.
    ///
    /// Dispatcher frame, established at 0x6D7D68..0x6D7D97:
    ///   [ebp-4]    = Self            (0x6D7D83 mov [ebp-4],eax)
    ///   [ebp-0x34] = wire record     (0x6D7D97 mov [ebp-0x34],ebx)
    ///   [ebp-8]    = body string     (0x6D7D7E mov [ebp-8],ecx)
    ///   ESI        = body length     (0x6D7D86 mov esi,[ebp+8])
    /// which this port receives as nParam1/nParam2/nParam3/wParam (Recog/Param/
    /// Tag/Series), sMsg and nBodyLen.
    ///
    /// Reply-slot argument order is pinned off the two senders themselves, not
    /// inferred: sub_6D7CB0 ([vmt+0x250], `ret 0x10`) writes Param from
    /// [ebp+0x14], Tag from [ebp+0x10], Series from [ebp+0xC] and takes the body
    /// string in [ebp+8]; sub_6D7BF8 ([vmt+0x254], `ret 0x14`) writes Param from
    /// [ebp+0x18], Tag from [ebp+0x14], Series from [ebp+0x10] and takes body
    /// pointer in [ebp+0xC] and body length in [ebp+8]. Stack arguments are
    /// therefore pushed left to right, so the FIRST push is Param.
    ///
    /// Where the native leaf runs into a subsystem this port has not modelled,
    /// the arm stops at the last gate it can evaluate from the image and hands
    /// the ident to NativeCmTailFailClosed. Gates that end in "战神 does nothing"
    /// are reproduced as real silence and are faithful.
    /// </summary>
    public partial class TPlayObject
    {
        private bool TryHandleNativeCmTailProtocol(TProcessMessage processMessage)
        {
            // === TaskBoard subsystem === CM 4150/4151/4417/4651 are owned by
            // TPlayObject.TaskBoard.cs (TryHandleTaskBoardCm). It takes precedence over the
            // legacy fail-closed stubs below for those idents.
            if (TryHandleTaskBoardCm(processMessage))
            {
                return true;
            }

            switch (processMessage.wIdent)
            {
                case Grobal2.CM_4125:
                    ClientNativeFixedRecordTableQuery();
                    return true;
                case Grobal2.CM_4126:
                    ClientNativeSoulWashApply(processMessage.nParam3);
                    return true;
                case Grobal2.CM_4127:
                    ClientNativeSoulWashRecompute(processMessage.nParam3);
                    return true;
                case Grobal2.CM_4128:
                    ClientNativeNeighbourSoulStateQuery(processMessage.nParam1,
                        processMessage.nParam2, processMessage.nParam3);
                    return true;
                case Grobal2.CM_4150:
                    ClientNativeTaskBoardRefresh();
                    return true;
                case Grobal2.CM_4151:
                    ClientNativeTaskBoardAction();
                    return true;
                case Grobal2.CM_4173:
                    ClientNativeFreeRecycleEquip();
                    return true;
                case Grobal2.CM_4204:
                    ClientNativeSmsAuthVerify();
                    return true;
                case Grobal2.CM_4205:
                    ClientNativeSmsAuthSend();
                    return true;
                case Grobal2.CM_4215:
                    ClientNativeNeighbourInteract();
                    return true;
                case Grobal2.CM_4218:
                    ClientNativeItemTransferSelf();
                    return true;
                case Grobal2.CM_4408:
                    ClientNativeBeadInlaySelf();
                    return true;
                case Grobal2.CM_4409:
                    ClientNativeJadeInlaySelf();
                    return true;
                case Grobal2.CM_4410:
                    ClientNativeBeadInlayHero();
                    return true;
                case Grobal2.CM_4411:
                    ClientNativeJadeInlayHero();
                    return true;
                case Grobal2.CM_4417:
                    ClientNativeTaskBoardScriptCommand();
                    return true;
                case Grobal2.CM_4446:
                    ClientNativeYuanbaoConsignSettings();
                    return true;
                case Grobal2.CM_4496:
                    ClientNativeFreshmanTaskCommand();
                    return true;
                case Grobal2.CM_4626:
                    ClientNativePagedListQuery();
                    return true;
                case Grobal2.CM_4646:
                    ClientNativePrizeList();
                    return true;
                case Grobal2.CM_4647:
                    ClientNativePrizePrecheck();
                    return true;
                case Grobal2.CM_4648:
                    ClientNativePrizeSettle();
                    return true;
                case Grobal2.CM_4649:
                    ClientNativePrizeClaimWithItemDelete();
                    return true;
                case Grobal2.CM_4650:
                    ClientNativeTreasureMapSynth();
                    return true;
                case Grobal2.CM_4651:
                    ClientNativeTaskBoardTextCommand();
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>
        /// CM 4125, native leaf 0x6DAE25 (`8B 45 FC` / `E8 07 BE 06 00`), worker
        /// 0x746C34.
        ///
        /// 0x746C45 reads the row count from [[0x7D6014]]+0x18 and 0x746C4A
        /// `0F 8E 14 01 00 00 jle 0x746D64` returns without sending anything when
        /// it is not positive. Otherwise the worker allocates count*0x2B bytes,
        /// walks the table with 0x49EE7C (first) / 0x49EE54 (next) copying 0x2B
        /// bytes per row (0x746CB4 `call 0x403260` with ecx=0x2B), and answers
        /// SM 0xFC0 through [vmt+0x254] at 0x746D18 with Recog=count,
        /// Tag=word[[0x7D5AEC]] and the whole buffer as the body. It then always
        /// sends SM 0xFC6 through [vmt+0x250] with Param = byte[[0x7D6938]] != 0
        /// (0x746D28 push 1 vs 0x746D43 push 0).
        ///
        /// Neither the 0x2B-byte row format nor the table at [[0x7D6014]] exists
        /// in this port, so the row count that decides between "send nothing" and
        /// "send two packets" cannot be evaluated at all.
        /// </summary>
        private void ClientNativeFixedRecordTableQuery()
        {
            NativeCmTailFailClosed.Drop(Grobal2.CM_4125, m_sCharName);
        }

        /// <summary>
        /// CM 4126, native leaf 0x6DAE74 (Recog into ECX, Tag into EDX), worker
        /// 0x6BF75C.
        ///
        /// The worker is one selector plus two identical bodies:
        ///   0x6BF763  83 FA 01              cmp edx,1        ; Tag
        ///   0x6BF766  0F 85 CD 00 00 00     jne 0x6BF839
        ///   0x6BF76C  83 BE B0 0B 00 00 00  cmp [esi+0xBB0],0 ; 英雄
        ///   0x6BF773  0F 84 C0 00 00 00     je  0x6BF839
        ///   0x6BF839  85 D2 / 0F 85 A8 00.. test edx,edx / jne 0x6BF8E9
        /// so Tag==1 with a hero runs the hero body, Tag==0 runs the self body,
        /// and everything else — including Tag==1 with no hero — lands on
        /// 0x6BF8E9, which is a bare reply and nothing else:
        ///   0x6BF8E9  6A 00 6A 00 6A 00 6A 00   push 0 x4
        ///   0x6BF8F1  33 C9                     xor ecx,ecx
        ///   0x6BF8F3  66 BA C2 0F               mov dx,0xFC2
        ///   0x6BF8FB  FF 93 50 02 00 00         call [vmt+0x250]
        /// That leg is reproduced exactly. Both real bodies gate on the soul-wash
        /// counters [+0x610], [+0x59C], [+0x5A0], [+0x5A4] and then consume a
        /// stone through 0x746F10 / 0x747530; none of those fields is modelled
        /// here, so the outcome code they put in Tag cannot be derived.
        /// </summary>
        private void ClientNativeSoulWashApply(int nTag)
        {
            if (nTag != 0 && !(nTag == 1 && m_HeroObject != null))
            {
                SendDefMessage(Grobal2.SM_4034, 0, 0, 0, 0, string.Empty);
                return;
            }

            NativeCmTailFailClosed.Drop(Grobal2.CM_4126, m_sCharName);
        }

        /// <summary>
        /// CM 4127, native leaf 0x6DAE8D. The whole leaf is the selector; both
        /// arms then run the same pair of calls ON SELF:
        ///   0x6DAE90  66 8B 40 08           mov ax,[msg+8]      ; Tag
        ///   0x6DAE94  66 83 F8 01 / 75 21   cmp ax,1 / jne 0x6DAEBB
        ///   0x6DAE9D  83 BA B0 0B 00 00 00  cmp [self+0xBB0],0  ; 英雄
        ///   0x6DAEA4  74 15                 je  0x6DAEBB
        ///   0x6DAEA9  E8 46 CE 06 00        call 0x747CF4       ; eax = [ebp-4]
        ///   0x6DAEB1  E8 56 C4 06 00        call 0x74730C       ; eax = [ebp-4]
        ///   0x6DAEBB  66 85 C0              test ax,ax
        ///   0x6DAEBE  0F 85 68 0D 00 00     jne 0x6DBC2C        ; 静默丢弃
        ///   0x6DAEC7  E8 28 CE 06 00        call 0x747CF4       ; eax = [ebp-4]
        ///   0x6DAECF  E8 38 C4 06 00        call 0x74730C       ; eax = [ebp-4]
        /// The hero is only ever a gate — EAX is reloaded from [ebp-4] before both
        /// calls on the Tag==1 arm too, so 战神 never passes the hero in. The work
        /// therefore runs for Tag==0, or for Tag==1 while a hero exists, and every
        /// other Tag is a silent drop, which is what this arm reproduces.
        ///
        /// 0x747CF4 rebuilds [+0x5A8]..[+0x5BC] and recomputes [+0x59C] from the
        /// bit population of [+0x60C] (0x747D8C `call 0x4C7A34`), and 0x74730C
        /// answers SM 0xFC1 through [vmt+0x254] with a 0x20-byte body laid out as
        /// {int [+0x5A4]; int [+0x5A0]; int [+0x59C]; word[10] [+0x5A8]} and
        /// Tag = ([+0x178] == 0x36). None of those fields is modelled here.
        /// </summary>
        private void ClientNativeSoulWashRecompute(int nTag)
        {
            if (nTag != 0 && !(nTag == 1 && m_HeroObject != null))
            {
                return;
            }

            NativeCmTailFailClosed.Drop(Grobal2.CM_4127, m_sCharName);
        }

        /// <summary>
        /// CM 4128, native leaf 0x6DAF23 (Tag pushed, Param into CX, Recog into
        /// EDX), worker 0x6B7184.
        ///
        /// 0x6B71A2 calls the neighbourhood validator 0x76C9D4 with
        /// (Self, target=Recog, x=Param, y=Tag). That validator sweeps the nine
        /// cells x-1..x+1 / y-1..y+1 of Self's own map ([Self+0x128], fetched per
        /// cell by 0x7776A8) and only returns true when some cell node of type 1
        /// carries exactly the pointer the client sent (0x76CA42 `3B 58 04`
        /// cmp ebx,[eax+4]) and that object is not a ghost (0x76CA47
        /// `80 7B 73 00` cmp [ebx+0x73],0). 战神 is round-tripping a server
        /// pointer through the client here; this port carries an object id in
        /// Recog instead, so the same predicate is "resolve the id, then require
        /// same map, Chebyshev distance <= 1 from the client-supplied (x,y), and
        /// not a ghost". 战神 never checks (x,y) against Self's real position, so
        /// neither does this.
        ///
        /// A failed sweep falls straight to the epilogue at 0x6B71F3 with no
        /// reply, and so does a race outside {0, 0x36}:
        ///   0x6B71AB  8A 9F 78 01 00 00     mov bl,[edi+0x178]
        ///   0x6B71B1  84 DB / 74 05         test bl,bl / je 0x6B71BA
        ///   0x6B71B5  80 FB 36 / 75 39      cmp bl,0x36 / jne 0x6B71F3
        /// Both silences are reproduced. The surviving path builds a 24-byte body
        /// {int [T+0x60C]; byte[20] [T+0x5A8]} and answers SM 0xFC5 through
        /// [vmt+0x254] with Recog=0; those two fields are the same unmodelled
        /// soul-wash block as CM 4127.
        /// </summary>
        private void ClientNativeNeighbourSoulStateQuery(int nRecog, int nX, int nY)
        {
            var target = M2Share.ObjectManager.Get(nRecog);
            if (target == null || target.m_boGhost || target.m_PEnvir != m_PEnvir)
            {
                return;
            }

            if (Math.Abs(target.m_nCurrX - nX) > 1 || Math.Abs(target.m_nCurrY - nY) > 1)
            {
                return;
            }

            if (target.m_btRaceServer != Grobal2.RC_PLAYOBJECT && target.m_btRaceServer != 0x36)
            {
                return;
            }

            NativeCmTailFailClosed.Drop(Grobal2.CM_4128, m_sCharName);
        }

        /// <summary>
        /// CM 4150, native leaf 0x6DAF51 (`8B 45 FC` Self / `E8 CB 79 01 00`
        /// call 0x6F2924), which is a thunk: 0x6F2924 loads the task-board object
        /// [[0x7D5D20]] into EAX, swaps Self into EDX (`92 xchg`) and tail-calls
        /// the real worker 0x699B68.
        ///
        /// 0x699B68 opens a 0xA4-dword frame and builds the task-board listing the
        /// client renders; the native leaf then answers it as a single large SM
        /// body. Neither the task-board object at [[0x7D5D20]] nor its per-entry
        /// record layout exists in this port, so the body cannot be derived.
        /// </summary>
        private void ClientNativeTaskBoardRefresh()
        {
            NativeCmTailFailClosed.Drop(Grobal2.CM_4150, m_sCharName);
        }

        /// <summary>
        /// CM 4151, native leaf 0x6DAF5E, worker 0x6999D4.
        ///
        /// The leaf pushes Recog ([record+0]), Param (word[record+6]) and Tag
        /// (word[record+8]), loads the task-board object [[0x7D5D20]] and Self,
        /// then calls 0x6999D4 (a 0x13-dword-frame Delphi routine). That worker is
        /// the task-board command entry (dispatch / accept / complete), all of
        /// which run @Main-style script procedures against the [[0x7D5D20]] object.
        /// None of that script machinery is ported, so the outcome and any reply
        /// cannot be reproduced.
        /// </summary>
        private void ClientNativeTaskBoardAction()
        {
            NativeCmTailFailClosed.Drop(Grobal2.CM_4151, m_sCharName);
        }

        /// <summary>
        /// CM 4173, native leaf 0x6DB068, worker 0x6E600C.
        ///
        /// The leaf folds Param (word[record+6]) and Tag (word[record+8]) into a
        /// single dword through 0x408D40 — that helper is exactly MakeLong(lo=ax,
        /// hi=dx), i.e. HUtil32.MakeLong(Param, Tag) — and calls 0x6E600C(Self,
        /// that dword). 0x6E600C is the free equipment-recycle worker; it resolves
        /// the referenced bag item, deletes it and settles reputation. The item
        /// selection and the recycle payout table are not modelled here, so the
        /// deletion and the SM it would answer are withheld rather than guessed.
        /// </summary>
        private void ClientNativeFreeRecycleEquip()
        {
            NativeCmTailFailClosed.Drop(Grobal2.CM_4173, m_sCharName);
        }

        /// <summary>
        /// CM 4204, native leaf 0x6DAF87, worker 0x6F03E8.
        ///
        /// The leaf copies the packet body string ([ebp-8], via 0x405708) into a
        /// local and calls 0x6F03E8(Self, code=that string, Param=word[record+6]).
        /// 0x6F03E8 is the SMS verification-code CHECK: it compares the client's
        /// code against one issued through the operator's SMS gateway. That gateway
        /// is external to the image, so neither the stored code nor the pass/fail
        /// result can be derived — the check is failed closed.
        /// </summary>
        private void ClientNativeSmsAuthVerify()
        {
            NativeCmTailFailClosed.Drop(Grobal2.CM_4204, m_sCharName);
        }

        /// <summary>
        /// CM 4205, native leaf 0x6DAFAF, worker 0x6F01E4.
        ///
        /// The leaf calls 0x6F01E4(Self, Param=word[record+6], Series=word
        /// [record+0xA]). 0x6F01E4 opens a 0x20C-byte frame to compose the
        /// verification-code SMS and hands it to the operator's SMS gateway. The
        /// gateway is external to the image; issuing a code and the SM that reports
        /// success are outside anything derivable here, so the request fails closed.
        /// </summary>
        private void ClientNativeSmsAuthSend()
        {
            NativeCmTailFailClosed.Drop(Grobal2.CM_4205, m_sCharName);
        }

        /// <summary>
        /// CM 4215, native leaf 0x6DAFCA, worker 0x6E8684.
        ///
        /// The leaf passes Recog ([record+0]), Param (word[record+6]),
        /// Series (word[record+0xA]) and Tag (word[record+8]) to 0x6E8684, a
        /// neighbour-object interaction worker that answers up to three distinct
        /// SM packets through [vmt+0x250]. The reply selection and the target
        /// object fields it reads are not modelled in this port, so no packet can
        /// be reconstructed without inventing its body.
        /// </summary>
        private void ClientNativeNeighbourInteract()
        {
            NativeCmTailFailClosed.Drop(Grobal2.CM_4215, m_sCharName);
        }

        /// <summary>
        /// CM 4218, native leaf 0x6DB00C, worker 0x6F3104.
        ///
        /// The leaf calls 0x6F3104(Self, Recog=[record], MakeLong(Param,Tag) via
        /// 0x408D40). The worker throttles on [Self+0xA6C] (1500 ms, 0x6F3118
        /// `cmp edx,0x5DC`), then admits the move only when the item name matches
        /// the type table [[0x780574]] (0x6F313D) AND the transfer gate 0x774378
        /// passes (0x6F3154) AND the target is not a ghost ([+0x73]). Neither the
        /// item-type table nor the 0x774378 gate is modelled, so the transfer and
        /// its SM are withheld.
        /// </summary>
        private void ClientNativeItemTransferSelf()
        {
            NativeCmTailFailClosed.Drop(Grobal2.CM_4218, m_sCharName);
        }

        /// <summary>
        /// CM 4408, native leaf 0x6DB08A, worker 0x6F37EC with the self/hero
        /// selector DL = 0 (self).
        ///
        /// The leaf calls 0x6F37EC(Self, DL=0, Recog=[record], MakeLong(Param,Tag)
        /// via 0x408D40). With DL=0 the worker targets Self and, once the target is
        /// valid, runs the spirit-bead inlay chain 0x7487A8 (0x6F385E) against the
        /// item at the Recog slot. The inlay chain mutates per-item bead slots and
        /// counters that this port does not model, so the mount and its reply are
        /// withheld rather than invented.
        /// </summary>
        private void ClientNativeBeadInlaySelf()
        {
            NativeCmTailFailClosed.Drop(Grobal2.CM_4408, m_sCharName);
        }

        /// <summary>
        /// CM 4409, native leaf 0x6DB0B2, worker 0x6F38A8 with the self/hero
        /// selector DL = 0 (self).
        ///
        /// The leaf calls 0x6F38A8(Self, DL=0, Param=word[record+6], body length
        /// ESI, body string [ebp-8]). With DL=0 the target is Self; the worker then
        /// runs the jade inlay chain 0x748A18 (0x6F3901), which reads the spirit-
        /// bead template table [[0x7D3F34]] and the item's element bytes. None of
        /// those are modelled, so the inlay and its SM (self leg at 0x6F3928) are
        /// withheld.
        /// </summary>
        private void ClientNativeJadeInlaySelf()
        {
            NativeCmTailFailClosed.Drop(Grobal2.CM_4409, m_sCharName);
        }

        /// <summary>
        /// CM 4410, native leaf 0x6DB0D0, worker 0x6F37EC with the self/hero
        /// selector DL = 1 (hero) — the same bead-inlay worker as CM 4408.
        ///
        /// The leaf calls 0x6F37EC(Self, DL=1, Recog=[record], MakeLong(Param,Tag)
        /// via 0x408D40). With DL=1 the worker resolves the hero [Self+0xBB0],
        /// requires it valid (0x772DA8) and non-ghost ([+0x73]), then runs the same
        /// 0x7487A8 inlay chain against the hero's item. The hero's per-item bead
        /// slots are not modelled, so the mount and reply are withheld (the no-hero
        /// leg would answer with the -99 sentinel, which is folded into the drop).
        /// </summary>
        private void ClientNativeBeadInlayHero()
        {
            NativeCmTailFailClosed.Drop(Grobal2.CM_4410, m_sCharName);
        }

        /// <summary>
        /// CM 4411, native leaf 0x6DB0F8, worker 0x6F38A8 with the self/hero
        /// selector DL = 1 (hero) — the same jade-inlay worker as CM 4409.
        ///
        /// The leaf calls 0x6F38A8(Self, DL=1, Param=word[record+6], body length
        /// ESI, body string [ebp-8]). With DL=1 the worker resolves the hero
        /// [Self+0xBB0], requires it valid (0x772DA8) and non-ghost, then runs the
        /// jade inlay chain 0x748A18 and answers SM 0x113B/4411 through [vmt+0x250]
        /// with the result code. The template table [[0x7D3F34]] and item element
        /// bytes are unmodelled, so the reply is withheld.
        /// </summary>
        private void ClientNativeJadeInlayHero()
        {
            NativeCmTailFailClosed.Drop(Grobal2.CM_4411, m_sCharName);
        }

        /// <summary>
        /// CM 4417, native leaf 0x6DB1BF, worker 0x699EB4.
        ///
        /// The leaf calls 0x699EB4(taskBoard=[[0x7D5D20]], Self, callback=0x6DC000)
        /// — a task-publish-board command that runs @Main-style script procedures
        /// against the board object at [[0x7D5D20]] (its +0x2C script slot). That
        /// script object and its procedures are not modelled in this port, so the
        /// command's effect and any reply cannot be reproduced.
        /// </summary>
        private void ClientNativeTaskBoardScriptCommand()
        {
            NativeCmTailFailClosed.Drop(Grobal2.CM_4417, m_sCharName);
        }

        /// <summary>
        /// CM 4446, native leaf 0x6DBB37, worker 0x6F75C4.
        ///
        /// The leaf calls 0x6F75C4(Self). The worker reads the consignment
        /// collection [Self+0x192C] (0x6F75CB); when it is null (0x6F75D3 `je`) it
        /// does nothing, otherwise it counts the entries through 0x712BE4 and
        /// answers SM 0x115E/4446 with Recog = that count. The [Self+0x192C]
        /// sub-object is not modelled here, so the count that fills Recog cannot be
        /// derived and the reply is withheld.
        /// </summary>
        private void ClientNativeYuanbaoConsignSettings()
        {
            NativeCmTailFailClosed.Drop(Grobal2.CM_4446, m_sCharName);
        }

        /// <summary>
        /// CM 4496, native leaf 0x6DBBDC, worker 0x6FAC8C.
        ///
        /// The leaf calls 0x6FAC8C(Self, Recog=[record]). The worker is the
        /// freshman-task command entry (FreshmanTaskCommand), an 8-local SEH frame
        /// that drives the freshman quest state through script hooks. That script
        /// entry is not wired up in this port, so the command's outcome and reply
        /// cannot be reproduced.
        /// </summary>
        private void ClientNativeFreshmanTaskCommand()
        {
            NativeCmTailFailClosed.Drop(Grobal2.CM_4496, m_sCharName);
        }

        /// <summary>
        /// CM 4626, native leaf 0x6DB394, worker 0x6AE260.
        ///
        /// The leaf calls 0x6AE260(Self, Param=word[record+6], Tag=word[record+8]).
        /// The worker treats Param as a page offset and Tag as a page size capped
        /// at 0x20 (0x6AE285 `cmp,0x20`), reads the total from the list source
        /// [[0x7D5C60]] (0x6AE29C) and copies one page of records. Neither the list
        /// source nor its per-record format is modelled, so the page body cannot be
        /// built and the reply is withheld.
        /// </summary>
        private void ClientNativePagedListQuery()
        {
            NativeCmTailFailClosed.Drop(Grobal2.CM_4626, m_sCharName);
        }

        /// <summary>
        /// CM 4646, native leaf 0x6DBBEB, worker 0x6FBB90.
        ///
        /// The leaf calls 0x6FBB90(Self). The worker walks the reward-id array
        /// [Self+0x62C] for [Self+0x658] entries (0x6FBBF0), resolves each against
        /// the prize manager [[0x7D605C]] (0x6FBBE9) via 0x69C57C and packs the
        /// claimable list into the reply. The reward-id array, its count and the
        /// prize manager are not modelled, so the list body is withheld.
        /// </summary>
        private void ClientNativePrizeList()
        {
            NativeCmTailFailClosed.Drop(Grobal2.CM_4646, m_sCharName);
        }

        /// <summary>
        /// CM 4647, native leaf 0x6DBBF5, worker 0x6FB6FC.
        ///
        /// The leaf calls 0x6FB6FC(Self), the prize-claim precheck. It refuses when
        /// the claimed count [Self+0x658] has reached 10 (0x6FB705 `cmp,0xa`), then
        /// checks the diamond ceiling [Self+0x15C]+0xC350 against [Self+0x68C]
        /// (0x6FB736), answering a fixed notice SM 0x38FF on either failure. Those
        /// counters and the diamond-currency block are not modelled, so the gate
        /// cannot be evaluated and no packet is emitted.
        /// </summary>
        private void ClientNativePrizePrecheck()
        {
            NativeCmTailFailClosed.Drop(Grobal2.CM_4647, m_sCharName);
        }

        /// <summary>
        /// CM 4648, native leaf 0x6DBBFF, worker 0x6FB874.
        ///
        /// The leaf calls 0x6FB874(Self), the prize settlement. It walks the reward
        /// array [Self+0x62C]/[+0x62E] for [Self+0x658] entries (0x6FB8BA), resolves
        /// each through the prize manager [[0x7D605C]] (0x6FB8B3) and credits the
        /// payout onto [Self+0x4F0] (0x6FB8E6). The reward array, the prize manager
        /// and the credited counters are not modelled, so the settlement and its
        /// reply are withheld rather than invented.
        /// </summary>
        private void ClientNativePrizeSettle()
        {
            NativeCmTailFailClosed.Drop(Grobal2.CM_4648, m_sCharName);
        }

        /// <summary>
        /// CM 4649, native leaf 0x6DBC09, worker 0x6FBB28.
        ///
        /// The leaf calls 0x6FBB28(Self, Recog=[record]). The worker resolves the
        /// prize manager [[0x7D605C]] (0x6FBB37) and calls 0x69C47C(manager,
        /// Recog, Self) (0x6FBB42), which sweeps Self's bag for the item carrying
        /// the client-supplied id and deletes it as the cost of the claim. The bag
        /// sweep/delete rule and the manager are not modelled, so the deletion and
        /// the SM it answers are withheld.
        /// </summary>
        private void ClientNativePrizeClaimWithItemDelete()
        {
            NativeCmTailFailClosed.Drop(Grobal2.CM_4649, m_sCharName);
        }

        /// <summary>
        /// CM 4650, native leaf 0x6DBC18, worker 0x6FB51C.
        ///
        /// The leaf calls 0x6FB51C(Self, Recog=[record], body string [ebp-8], body
        /// length ESI). The worker resolves the prize manager [[0x7D605C]], calls
        /// 0x69C648 then the synthesis state machine 0x69C03C (0x6FB54B), whose
        /// 0..5 return code drives a six-way jump table at 0x6FB569 selecting the
        /// SM result. The synthesis machine is not modelled, so the outcome code —
        /// and therefore which of the six replies to send — cannot be derived.
        /// </summary>
        private void ClientNativeTreasureMapSynth()
        {
            NativeCmTailFailClosed.Drop(Grobal2.CM_4650, m_sCharName);
        }

        /// <summary>
        /// CM 4651, native leaf 0x6DB1D8, worker 0x6FC054.
        ///
        /// The leaf copies the packet body string ([ebp-8], via 0x405708) into a
        /// local and calls 0x6FC054(Self, text=that string). The worker loads the
        /// task-board script object [[0x7D5D20]] and only proceeds when its +0x2C
        /// @Main slot is non-null (0x6FC064 `cmp [eax+0x2C],0` / `je`), then runs
        /// the text command through that script. The board script object is not
        /// modelled in this port, so the command is dropped rather than guessed.
        /// </summary>
        private void ClientNativeTaskBoardTextCommand()
        {
            NativeCmTailFailClosed.Drop(Grobal2.CM_4651, m_sCharName);
        }
    }
}
