using System.Collections.Generic;
using GameSvr.Services;
using SystemModule;

namespace GameSvr
{
    /// <summary>
    /// Faithful ports of the two hero-facing native CM handlers cm-3 had left
    /// fail-closed:
    ///   CM 3503 (0x0DAF) leaf 0x6DAF44 -> worker 0x6EF970 — hero skill 升龙破
    ///           readiness probe.
    ///   CM 4105 (0x1009) leaf 0x6DA005 -> workers 0x7742C0 / 0x6BCE2C / 0x6EE174 —
    ///           mount-summon / status-refresh triple.
    ///
    /// HOOKING (this port never edits the shared Operate()/dispatch files):
    /// TPlayObject.Message.cs threads the native fallbacks as
    ///     if (... &amp;&amp; !TryHandleNativeCmQ1(ProcessMsg)
    ///            &amp;&amp; !TryHandleNativeCmQ2(ProcessMsg)
    ///            &amp;&amp; !TryHandleNativeCmQ3(ProcessMsg))
    ///         result = base.Operate(ProcessMsg);
    /// Wire THIS probe in AHEAD of the Q3 arm so it upgrades CM 3503 / CM 4105
    /// before their fail-closed Q3 stubs (TPlayObject.NativeCmProtocol_Q3.cs) can
    /// claim them, i.e.
    ///     ... &amp;&amp; !TryHandleHeroNotifyCm(ProcessMsg)   // &lt;-- insert before Q3
    ///         &amp;&amp; !TryHandleNativeCmQ3(ProcessMsg))
    /// No shared file is touched here; the one-line insertion above is the whole
    /// integration step.
    ///
    /// Evidence base: flat_image.bin @ ImageBase 0x400000, capstone x86-32.
    /// THeroAct VMT 0x685630 (HeroObject.cs). Every gate that reads modelled state
    /// is reproduced 1:1; the legs that read an unmodelled field are withheld with
    /// a throttled record rather than inventing wire bytes (§铁律 fail-closed).
    /// </summary>
    public partial class TPlayObject
    {
        // === HeroNotify subsystem ===

        /// <summary>Magic id AND cold-time key for 升龙破, the literal 0x111 that
        /// 0x690A24 hands to both hero VMT calls (0x690A31 `mov dx,0x111` for the
        /// magic-list probe, 0x690A54 `mov edx,0x111` for the cooldown probe). Equal
        /// to SpellsDef.SKILL_273 and to TBaseObject.NativeSkill273's ColdTimeKey, so
        /// a cooldown armed by an actual 升龙破 cast (SetNativeColdTime 0x111) is the
        /// very entry this probe reads back.</summary>
        private const int HeroNotifyDragonBreakId = 0x111;

        /// <summary>Notice colour word cx=0x38FF at 0x6EF9C7, split for the wire the
        /// way TBaseObject.NativeSkill273.cs splits its own vmt+0xD4 hint: low byte =
        /// FColor, high byte = BColor. MakeWord(0xFF,0x38) is exactly the
        /// btRedMsgFColor/btRedMsgBColor default pair (GameSvrConfig.cs 0xFF / 0x38),
        /// i.e. MsgColor.Red.</summary>
        private const byte HeroNotifyRedFColor = 0xFF;   // 0x38FF & 0xFF
        private const byte HeroNotifyRedBColor = 0x38;   // 0x38FF >> 8

        /// <summary>String @0x6EFA04 (18 GBK bytes, byte-exact round-trip).</summary>
        private const string HeroNotifyDragonBreakNotLearned = "没有学会技能升龙破";

        /// <summary>String @0x6EFA20 (20 GBK bytes, byte-exact round-trip).</summary>
        private const string HeroNotifyDragonBreakOnCooldown = "技能升龙破还在冷却中";

        private static readonly HashSet<int> HeroNotifyReportedGaps = new HashSet<int>();
        private static readonly object HeroNotifyReportLock = new object();

        /// <summary>
        /// Insert before TryHandleNativeCmQ3 (see the class remarks). Returns true
        /// for the two idents it owns so the dispatch chain stops.
        /// </summary>
        private bool TryHandleHeroNotifyCm(TProcessMessage processMessage)
        {
            switch (processMessage.wIdent)
            {
                case Grobal2.CM_3503: HeroNotifyCm3503(); return true;
                case Grobal2.CM_4105: HeroNotifyCm4105(); return true;
                default:
                    return false;
            }
        }

        /// <summary>
        /// CM 3503, leaf 0x6DAF44 (`mov eax,[ebp-4]` Self / `call 0x6EF970`), worker
        /// 0x6EF970(Self). Data flow, each offset from its own instruction:
        ///
        ///   0x6EF98E  mov eax,[Self+0xBB0]  -> m_HeroObject
        ///   0x6EF994  test eax,eax
        ///   0x6EF996  je 0x6EF9D8           -> no hero: bare SEH teardown, SILENCE
        ///   0x6EF998  call 0x690A24         -> HeroCheckState(hero), returns 0/-1/-2
        ///   0x6EF99D  sub eax,-2 / branch   -> -1 picks string @0x6EFA04,
        ///                                       -2 picks string @0x6EFA20, 0 none
        ///   0x6EF9C1  cmp [ebp-4],0 / je    -> empty string: send nothing
        ///   0x6EF9C7  mov cx,0x38FF         -> SysMsg red colour word
        ///   0x6EF9D2  call [Self.vmt+0xD4]  -> SysMsg on Self (the player)
        ///
        /// The no-hero SILENCE and both notice legs read only modelled state
        /// (m_HeroObject, the hero magic list, the hero cold-time table) and are
        /// reproduced 1:1. The "ready" leg (learned + off cooldown) writes the
        /// unmodelled arm flag [hero+0x6D9]=1 and sends nothing, so it is withheld.
        /// </summary>
        private void HeroNotifyCm3503()
        {
            // 0x6EF98E..0x6EF996: no hero -> silence.
            HeroObject hero = m_HeroObject;
            if (hero == null)
            {
                return;
            }

            switch (HeroNotifyDragonBreakState(hero))
            {
                case -1:
                    // 0x6EF9A5..0x6EF9B2 -> string @0x6EFA04.
                    HeroNotifyRedSysMsg(HeroNotifyDragonBreakNotLearned);
                    break;
                case -2:
                    // 0x6EF9B4..0x6EF9BC -> string @0x6EFA20.
                    HeroNotifyRedSysMsg(HeroNotifyDragonBreakOnCooldown);
                    break;
                default:
                    // 0x690A75 sets [hero+0x6D9]=1 and 0x6EF9C1 sends nothing. That
                    // arm flag (read by the hero 升龙破 combat legs at 0x692AFF /
                    // 0x693C88 / 0x6946CF) is not modelled, so the packet is dropped
                    // rather than pretending to arm a skill this port cannot fire.
                    HeroNotifyFailClosed(Grobal2.CM_3503,
                        "hero 升龙破 就绪腿置 [hero+0x6D9]=1（arm 标志 + 其战斗读取端 "
                        + "0x692AFF/0x693C88/0x6946CF 未建模）；原生此腿本就不发包");
                    break;
            }
        }

        /// <summary>
        /// 0x690A24 HeroCheckState(hero), operating on the hero (eax=[Self+0xBB0] at
        /// the call site). Returns -1 (not learned), -2 (still cooling) or 0 (ready):
        ///
        ///   0x690A2F  xor ecx,ecx / mov dx,0x111
        ///   0x690A39  call [hero.vmt+0xE8]  -> 0x741628 FindMagic(hero,cl=0,id=0x111)
        ///   0x690A46  je (== 0)             -> not learned: [hero+0x6D9]=0, ret -1
        ///   0x690A54  mov edx,0x111
        ///   0x690A5D  call [hero.vmt+0x1F4] -> 0x748288 QueryColdTime(hero,0x111)
        ///   0x690A63  test eax,eax / jle    -> &gt;0: cooling: [hero+0x6D9]=0, ret -2
        ///   0x690A75  (&lt;=0)               -> ready: [hero+0x6D9]=1, ret 0
        ///
        /// hero VMT+0xE8 (0x741628) scans [hero+0x500] (the magic list) for the
        /// TUserMagic whose MagicInfo.wMagicID equals the id; with cl=0 it applies no
        /// level filter — exactly HeroObject.FindHeroMagicById. hero VMT+0x1F4
        /// (0x748288) is the cold-time query modelled as
        /// TBaseObject.QueryNativeColdTime / GetNativeColdTimeRemaining, which
        /// HeroObject supports (SupportsNativeColdTime =&gt; true).
        /// </summary>
        private static int HeroNotifyDragonBreakState(HeroObject hero)
        {
            // 0x690A39 hero.vmt+0xE8: magic learned?
            if (!HeroNotifyHasLearnedMagic(hero, HeroNotifyDragonBreakId))
            {
                return -1;
            }

            // 0x690A5D hero.vmt+0x1F4: remaining > 0 reads as "still cooling"
            // (0x690A63 test/jle keeps <=0, including a negative, as ready).
            if (hero.GetNativeColdTimeRemaining(HeroNotifyDragonBreakId) > 0)
            {
                return -2;
            }

            return 0;
        }

        /// <summary>
        /// 0x741628 hero VMT+0xE8 with cl=0: linear scan of the hero magic list
        /// [hero+0x500] comparing MagicInfo.wMagicID (0x741665 `mov ax,[[item]+0x10]`
        /// / 0x741669 `cmp ax,id`). Mirrors HeroObject.FindHeroMagicById (which cm-3
        /// keeps private on the hero) without touching that file.
        /// </summary>
        private static bool HeroNotifyHasLearnedMagic(HeroObject hero, int magicId)
        {
            IList<TUserMagic> list = hero.m_HeroMagicList;
            if (list == null)
            {
                return false;
            }

            foreach (TUserMagic magic in list)
            {
                if (magic?.MagicInfo != null && magic.MagicInfo.wMagicID == magicId)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// 0x6EF9C7..0x6EF9D2: cx=0x38FF SysMsg on Self via vmt+0xD4. Reproduced as a
        /// direct RM_SYSMESSAGE with the split colour bytes, identical in shape to
        /// TBaseObject.NativeSkill273.cs's vmt+0xD4 hint (the same 升龙破 family).
        /// </summary>
        private void HeroNotifyRedSysMsg(string text)
        {
            SendMsg(this, Grobal2.RM_SYSMESSAGE, 0,
                HeroNotifyRedFColor, HeroNotifyRedBColor, 0, text);
        }

        /// <summary>
        /// CM 4105, leaf 0x6DA005 fires three workers in program order, every one of
        /// which drives an unmodelled subsystem:
        ///
        ///   0x7742C0(Self) — stealth reveal. Gates on state 0x40
        ///     (0x7742D6 `mov dl,0x40` / 0x772960 getter); when set it clears it
        ///     (0x7731C0) and broadcasts RM_TURN 0x2711 built from [Self+0x12C]/
        ///     [+0x130] plus vmt+0x90 (GetShowName) and byte [Self+0x154], through
        ///     vmt+0xD8. Modelled as TBaseObject.BreakNativeStealthOnAction.
        ///   0x6BCE2C(Self,Ident=word[rec+4]) — cancel the pending action channels.
        ///     0x6BCE2C..0x6BCE52 is a single-ret body holding exactly three calls:
        ///     0x6EE128 (0x6EE164 `mov dx,0x4D0`), 0x6EF5D0 (0x6EF62E `mov dx,0x4D2`)
        ///     and vmt+0x1D8 = 0x6EE2AC (0x6EE2DF `mov dx,0xD57`). Modelled as
        ///     TPlayObject.CancelNativeActionChannels. The Ident argument is dead:
        ///     all three callees start by overwriting edx with eax.
        ///   0x6EE174(Self,Ident) — mount summon. Gates on state 0x33 (0x6BBEB8),
        ///     the mount object [Self+0x128].[0x85], [Self+0x4C0] via 0x75EC20, and
        ///     [Self+0xA24]==0x72, each raising its own notice (SM 0xFCFF strings
        ///     @0x6EE248 "当前地图不能召唤坐骑！" / @0x6EE268 "您无主宰者马牌，无法召唤
        ///     坐骑！"; SM 0xFFDB string @0x6EE290 "正在开启心法，不能上坐骑！"); on
        ///     success it re-refreshes (0x6BCE2C), stamps the timer [Self+0x1914] and
        ///     sends SM 0xD54 via vmt+0xE0.
        ///
        /// Workers one and two are now modelled, but the mount-summon fields
        /// [+0x4C0]/[+0xA24]/[+0x1914] behind worker three still are not, and that
        /// worker is the point of the command. Emitting only the two refreshes would
        /// answer CM 4105 with neither the mount nor any of its three refusal
        /// notices, so the arm stays withheld. The evidence is already in cm-3's
        /// ledger, so this reuses that throttled record rather than duplicating it.
        /// </summary>
        private void HeroNotifyCm4105()
        {
            NativeCmQ3FailClosed.Q3Drop(Grobal2.CM_4105, m_sCharName);
        }

        /// <summary>Drop the packet and record the gap once per ident per process,
        /// mirroring NativeCmQ3FailClosed.Q3Drop's throttle. Used for the CM 3503
        /// ready leg, whose blocker is not the stale whole-ident entry in the Q3
        /// ledger (the hero legs above are now implemented).</summary>
        private void HeroNotifyFailClosed(int ident, string blocker)
        {
            lock (HeroNotifyReportLock)
            {
                if (!HeroNotifyReportedGaps.Add(ident))
                {
                    return;
                }
            }

            M2Share.MainOutMessage(
                $"[CM未移植:HeroNotify] CM {ident} 部分腿已丢弃; "
                + $"角色={(string.IsNullOrEmpty(m_sCharName) ? "<unknown>" : m_sCharName)}; "
                + $"缺口={blocker}");
        }
    }
}
