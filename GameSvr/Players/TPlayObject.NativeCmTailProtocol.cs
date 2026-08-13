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
    }
}
