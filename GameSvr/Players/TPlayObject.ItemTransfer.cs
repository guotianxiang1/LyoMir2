using System;
using GameSvr.Services;
using SystemModule;

namespace GameSvr
{
    /// <summary>
    /// Faithful upgrade of the two CM tail arms that cm-4 had left as bare
    /// fail-closed drops: CM 4215 (邻域对象交互) and CM 4218 ("物品转移到本人").
    ///
    /// Following the house rule that NativeCmTailFailClosed documents — "the gates
    /// 战神 evaluates BEFORE the unported call are reproduced 1:1 at the call site;
    /// only the terminal action is withheld" — this partial reproduces every gate
    /// derivable from the image and hands the ident to NativeCmTailFailClosed only
    /// at the exact point where the native worker would touch a subsystem this port
    /// does not model.  Wire framing (which SM, what body) is NEVER invented.
    ///
    /// ── CM 4215, leaf 0x6DAFCA → worker 0x6E8684 (`ret 8`) ──────────────────────
    /// Register/stack in (Delphi register calling convention):
    ///   EAX=Self, EDX=Param(word[rec+6], UNUSED by the worker),
    ///   ECX=Recog(dword[rec+0]), stack: Series(word[rec+0xA]) then Tag(word[rec+8])
    ///   pushed so [ebp+8]=Series, [ebp+0xC]=Tag.
    /// The worker:
    ///   0x6E8699  call 0x76C9D4(Self, target=Recog, x=Tag, y=Series) -> al
    ///             — the neighbour validator: it sweeps the 9 cells x-1..x+1 /
    ///               y-1..y+1 of Self's own map ([Self+0x128] = m_PEnvir, fetched
    ///               per cell by 0x7776A8) and only returns true when a cell node of
    ///               type 1 (moving object) carries exactly the pointer the client
    ///               sent (0x76CA42 `3B 58 04 cmp ebx,[eax+4]`) and that object is
    ///               NOT a ghost (0x76CA47 `80 7B 73 00 cmp [ebx+0x73],0`).  战神 is
    ///               round-tripping a server pointer through the client here; this
    ///               port carries an ObjectId, so the equivalent predicate is
    ///               "resolve the id, then require live + not-ghost + same map +
    ///               Chebyshev<=1 from the client's (Tag,Series)".  It never checks
    ///               (x,y) against Self's real position, so neither does this.  This
    ///               is the SAME validator CM 4128 uses.
    ///   0x6E86A6  `mov eax,ebx; mov edx,[0x6855E4]; call 0x404828`  — Delphi `is`
    ///             THeroAct (classref 0x6855E4 → VMT 0x685630, name "THeroAct").
    ///     0x6E86B7  edi = Recog.[+0x68C] (the hero's master, C# HeroObject.m_Master);
    ///               reply reads word[(master ?: hero) + 0x608].
    ///   0x6E86FF  else `is THumanKind` (classref 0x73BBE8 → VMT 0x73BC34, name
    ///             "THumanKind" — the C# predicate is IsNativeHumanKind()); reply
    ///             reads word[Recog + 0x608].
    ///   0x6E872D  else: nothing.
    /// Every reply is the SAME SM: `mov dx,0x1015` (SM 4117) through [vmt+0x250]
    /// (SendMsg) with the object's word[+0x608] in the Recog slot and Param/Tag/
    /// Series/body all zero.  [+0x608] is not modelled in this port and SM 4117 has
    /// NO other sender in the whole image (only these three sites emit 0x1015), so
    /// the reply body cannot be derived — it is withheld.
    ///
    /// ── CM 4218, leaf 0x6DB00C → worker 0x6F3104 ────────────────────────────────
    /// Leaf: dx=Tag(word[rec+8]), ax=Param(word[rec+6]), `call 0x408D40` builds
    ///   combined = MakeLong(ax,dx) = (Param & 0xFFFF) | ((Tag & 0xFFFF) << 16) into
    ///   ECX; EDX=Recog(dword[rec+0]); EAX=Self; `call 0x6F3104`.
    /// Worker 0x6F3104(EAX=Self=ebx, EDX=Recog=esi, ECX=combined=edi):
    ///   0x6F3111  eax = GetTickCount (0x408340)
    ///   0x6F3118  edx = eax - [Self+0xA6C]         ; delta since last transfer
    ///   0x6F311E  cmp edx,0x5DC (1500)
    ///   0x6F3124  jbe 0x6F326E                     ; within 1500 ms -> silent drop
    ///   0x6F312A  [Self+0xA6C] = eax               ; store tick (BEFORE the gates)
    ///   0x6F3134  objA = 0x73CF08(Self, Recog)     ; find object in Self's list
    ///             [Self+0x508] whose [+0x18]==Recog (a VISIBLE ground object)
    ///   0x6F313D  `is TMonsterBlowItem` (classref [0x780574] → VMT 0x7805C0, name
    ///             "TMonsterBlowItem") else drop
    ///   0x6F3154  0x774378(Self, combined): walk Self's owned-object linked list at
    ///             [Self+0x388] (node value [+0xC], next [+0x10]); combined must be a
    ///             registered node value, else drop
    ///   0x6F3163  `combined is TItemAttMon` (classref [0x6651A0] → VMT 0x6651EC,
    ///             name "TItemAttMon") else drop
    ///   0x6F317B  ghost gate: byte[combined+0x73]!=0 (m_boGhost) -> drop
    ///   0x6F3187  byte[combined+0x74]!=0 (0x772DA8) -> drop
    ///   0x6F3194  value = word[[objA+0x1C]+0x48] * 100 (0x78B510)
    ///   0x6F31A2  0x66D170(combined, Self, value): combined.[+0x354]=combined.[+0x34C]
    ///             =Self; if value>=combined.[+0x2AC] then [+0x2AC]=0 else [+0x2AC]-=value
    ///   0x6F31AF  count = word objA.0x7845A0; count>1000 -> split (reduce by 1000,
    ///             virtual [Self.vmt+0x260]); else consume whole (virtual [+0x24C])
    ///   0x6F320C  broadcast at combined X/Y ([+0x12C]/[+0x130]) via 0x769258
    ///             (feature string 0x6F327C = "toSelf") and 0x76920C
    ///   0x6F3250  if [combined+0x4D4].[+0x1C]>0 -> 0x6C03F8
    /// i.e. the mechanic is: feed a nearby dropped TMonsterBlowItem into a
    /// player-owned TItemAttMon creature, draining its [+0x2AC] and re-tagging its
    /// owner to Self.  NEITHER TMonsterBlowItem-as-a-live-object NOR TItemAttMon
    /// exists in this port (the item factory only ever returns "TMonsterBlowItem" as
    /// a class-NAME string; there is no TItemAttMon class, no per-player owned-object
    /// list [+0x388], and no 0x774378 registry).  The throttle at [Self+0xA6C] IS
    /// derivable and is reproduced 1:1; everything past it is withheld.  This is a
    /// live-object interaction, NOT a bag DelBagItem/AddItem transfer.
    ///
    /// Hook: TryHandleItemTransferCm(processMessage) below.  防冲突: this file does
    /// not edit the shared dispatcher (TPlayObject.NativeCmTailProtocol.cs).  To
    /// activate, its CM_4215 / CM_4218 arms (currently the placeholder
    /// ClientNativeNeighbourInteract() / ClientNativeItemTransferSelf() drops) should
    /// delegate to TryHandleItemTransferCm, e.g. replace those two arms with:
    ///     case Grobal2.CM_4215:
    ///     case Grobal2.CM_4218:
    ///         return TryHandleItemTransferCm(processMessage);
    /// </summary>
    public partial class TPlayObject
    {
        /// <summary>
        /// [Self+0xA6C] — last-accepted "物品转移到本人" tick, the throttle base for
        /// CM 4218's 0x6F3118 gate.  Not modelled anywhere else in this port, so it
        /// is introduced here (private) rather than reusing an unrelated field.
        /// </summary>
        private int m_dwItemTransferSelfTick;

        /// <summary>
        /// Dispatch hook for the ItemTransfer subsystem (CM 4215 / CM 4218).  Returns
        /// true when the ident belongs here.  Field roles are the dispatcher's:
        /// Recog=nParam1, Param=nParam2, Tag=nParam3, Series=wParam.
        /// </summary>
        private bool TryHandleItemTransferCm(TProcessMessage processMessage)
        {
            switch (processMessage.wIdent)
            {
                case Grobal2.CM_4215:
                    // worker uses Recog + (x=Tag, y=Series); Param is ignored by 0x6E8684.
                    ClientNativeNeighbourInteractFaithful(processMessage.nParam1,
                        processMessage.nParam3, processMessage.wParam);
                    return true;
                case Grobal2.CM_4218:
                    ClientNativeItemTransferSelfFaithful(processMessage.nParam1,
                        processMessage.nParam2, processMessage.nParam3);
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>
        /// CM 4215 邻域对象交互 (worker 0x6E8684).  Reproduces the neighbour validator
        /// (0x76C9D4) and the THeroAct/THumanKind `is` ladder; withholds the SM 4117
        /// reply because its word[+0x608] payload is unmodelled and SM 4117 has no
        /// other sender to cross-reference.
        /// </summary>
        private void ClientNativeNeighbourInteractFaithful(int nRecog, int nX, int nY)
        {
            // 0x76C9D4 — resolve the echoed target and require live + same map.
            var target = M2Share.ObjectManager.Get(nRecog);
            if (target == null || target.m_boGhost || target.m_PEnvir != m_PEnvir)
            {
                return; // validator false -> 0x6E872D epilogue, no reply
            }

            // 3x3 sweep around (x=Tag, y=Series): Chebyshev distance <= 1.
            if (Math.Abs(target.m_nCurrX - nX) > 1 || Math.Abs(target.m_nCurrY - nY) > 1)
            {
                return;
            }

            // 0x6E86A6 — `is THeroAct` (checked first; a hero is also THumanKind).
            if (target is HeroObject hero)
            {
                // 0x6E86B7 — reply source is the hero's master ([+0x68C]) when set,
                // else the hero itself; the value sent is word[source + 0x608].
                // Both the field and SM 4117 (0x1015) are underivable -> withhold.
                _ = hero.m_Master;
                NativeCmTailFailClosed.Drop(Grobal2.CM_4215, m_sCharName);
                return;
            }

            // 0x6E86FF — else `is THumanKind` (players/robots): reply word[target+0x608].
            if (target.IsNativeHumanKind())
            {
                NativeCmTailFailClosed.Drop(Grobal2.CM_4215, m_sCharName);
                return;
            }

            // 0x6E872D — neither THeroAct nor THumanKind (e.g. a monster/NPC): 战神
            // sends nothing.  Silent and faithful.
        }

        /// <summary>
        /// CM 4218 "物品转移到本人" (worker 0x6F3104).  Reproduces the 1500 ms throttle
        /// on [Self+0xA6C] exactly (checked, then stored, before any type gate, just
        /// as 战神 does), then withholds: the transfer feeds a live TMonsterBlowItem
        /// into a player-owned TItemAttMon creature, and neither object model exists
        /// in this port (see the file header for the full data flow).
        /// </summary>
        private void ClientNativeItemTransferSelfFaithful(int nRecog, int nParam, int nTag)
        {
            // combined the client echoes back = MakeLong(Param,Tag) via 0x408D40:
            //   (nParam & 0xFFFF) | ((nTag & 0xFFFF) << 16).  In 战神 this is
            //   Integer(TItemAttMon) round-tripped through the client; it is only
            //   dereferenced past the (unported) 0x774378 owned-list gate, so it is
            //   not resolved here.

            // 0x6F3111..0x6F3124 — throttle gate, reproduced 1:1.  Native uses an
            // unsigned `jbe`; this port follows its house convention of signed tick
            // deltas (identical outside a 49.7-day wrap the port never handles).
            int dwNow = HUtil32.GetTickCount();
            if ((dwNow - m_dwItemTransferSelfTick) <= Grobal2.ItemTransferSelfThrottleMs)
            {
                return; // 0x6F3124: within 1500 ms -> silent epilogue, tick NOT updated
            }

            // 0x6F312A — store the tick BEFORE the type gates, exactly as 战神 does.
            m_dwItemTransferSelfTick = dwNow;

            // 0x6F3134 onward — resolve a live TMonsterBlowItem by Recog in Self's
            // visible list and feed it into the TItemAttMon handle 'combined'.  That
            // whole live-object/owned-list subsystem is absent from this port, so the
            // transfer and its broadcasts are withheld rather than invented.
            _ = nRecog;
            _ = nParam;
            _ = nTag;
            NativeCmTailFailClosed.Drop(Grobal2.CM_4218, m_sCharName);
        }
    }
}
