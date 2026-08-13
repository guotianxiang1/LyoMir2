using System;
using System.Buffers.Binary;
using GameSvr.Services;
using SystemModule;

namespace GameSvr
{
    /// <summary>
    /// 洗灵石 / 祈福神佑袋 (灵佑点) subsystem — CM 4126 / 4127 / 4128.
    ///
    /// This partial REPLACES the fail-closed stubs cm-4 wired in
    /// TPlayObject.NativeCmTailProtocol.cs. The dispatcher (TPlayObject.Message.cs
    /// default arm) now calls <see cref="TryHandleSoulWashCm"/> first; only the
    /// idents this file cannot reproduce byte-for-byte fall through to the old
    /// NativeCmTailFailClosed drop.
    ///
    /// ── Native data structure (base 0x400000, all offsets on TPlayObject / THumanKind)
    /// The whole 灵佑点 state is four persisted source fields plus three derived
    /// scalars that native recomputes on demand:
    ///
    ///   [+0x5A4] int   current 灵佑点        \ 24-byte "shenYou" window persisted
    ///   [+0x5A8] word[10] slots (神佑属性)   / verbatim as ScriptData section 2
    ///                                          -> C# m_NativeShenYouBlock[0..23]
    ///                                             (TPlayObject.NativeScriptSections.cs)
    ///   [+0x60C] dword bitmask -> cap        -> C# m_wNativeCommonInformationFlags (ushort)
    ///   [+0x610] byte  mode / prereq (>0)    -> C# m_btNativeCommonInformationMode
    ///                                          (both TPlayObject.NativeCommonInformation.cs)
    ///   [+0x59C] int   cap    (DERIVED)      \ recomputed by 0x747CF4 from the
    ///   [+0x5A0] int   base   (DERIVED)      / four fields above; never persisted
    ///   [+0x5BC] byte  slot-count (DERIVED) /  -> computed on demand here
    ///   [+0x178] byte  race (m_btRaceServer); player=0, hero=0x36
    ///
    /// ── cap formula, verbatim from 0x747CF4 (0x747D81..0x747DB9):
    ///   cap = 20 * popcount([+0x60C] & 0x555555) + 40 * popcount([+0x60C] & 0xAAAAAA)
    ///   ( even bits: `shl 2`*`lea *5` = *20 ; odd bits: `shl 3`*`lea *5` = *40 )
    /// The native field is read as a dword and masked over bits 0..23, but the C#
    /// port models [+0x60C] as a 16-bit field (NativeCommonInformation.cs), so this
    /// port masks the 16-bit value — identical while bits 16..23 stay 0, which the
    /// only writer (ClientCommonInformation case 4, bits 0/1/2) guarantees.
    ///
    /// ── base formula (0x747B38): sum over the non-zero slots of
    ///   [[0x7D6014]].lookup(slotId).[+4]. That fixed-length (0x2B-byte) record
    ///   table is the SAME one CM 4125 rebuilds and is NOT modeled in this port
    ///   (it is loaded from a server config file). When every slot is 0 the native
    ///   recompute never calls 0x747B38 and base is exactly 0; that is the only
    ///   case this port can reproduce, so a non-zero slot array is FAIL-CLOSED.
    ///   All 34 golden DB blobs carry an all-zero shenYou window, so the reproducible
    ///   path is the live path.
    /// </summary>
    public partial class TPlayObject
    {
        /// <summary>Each 灵气石 grants 5 灵佑点 (0x747530 `lea edx,[esi+esi*4]`;
        /// the ceil divisor at 0x747CF0 is the float 5.0).</summary>
        private const int SoulWashPointsPerStone = 5;

        /// <summary>[+0x178] value that marks the hero race (0x747343 / 0x6B71B5
        /// `cmp bl,0x36`).</summary>
        private const byte SoulWashHeroRace = 0x36;

        /// <summary>SM 4033/4037 body sizes (0x74735A push 0x20; 0x6B71E1 push 0x18).</summary>
        private const int SoulWashStateBodySize = 0x20;
        private const int SoulWashNeighbourBodySize = 0x18;

        /// <summary>Population count, Brian-Kernighan, byte-identical to 0x4C7A34
        /// (`while (x) { x &= x - 1; ++c; }`).</summary>
        private static int SoulWashPopCount(uint value)
        {
            var count = 0;
            while (value != 0)
            {
                value &= value - 1;
                count++;
            }
            return count;
        }

        /// <summary>[+0x59C] cap from the [+0x60C] bitmask, verbatim from 0x747CF4.</summary>
        private static int SoulWashCap(ushort flags)
        {
            uint bits = flags;
            return SoulWashPopCount(bits & 0x555555u) * (4 * SoulWashPointsPerStone)
                   + SoulWashPopCount(bits & 0xAAAAAAu) * (8 * SoulWashPointsPerStone);
        }

        /// <summary>[+0x5A4] read out of the shenYou window (its first dword).</summary>
        private int GetSoulWashCurrent()
        {
            var block = m_NativeShenYouBlock;
            if (block == null || block.Length < sizeof(int))
            {
                return 0;
            }
            return BinaryPrimitives.ReadInt32LittleEndian(block.AsSpan(0, sizeof(int)));
        }

        /// <summary>Persist a clamped [+0x5A4] back into the shenYou window. Only
        /// called after a valid window exists; native mutates this field in the
        /// recompute (0x747DDC / 0x747E58) and it rides out on the next save.</summary>
        private void SetSoulWashCurrent(int value)
        {
            var block = m_NativeShenYouBlock;
            if (block == null || block.Length < NativeShenYouBlockSize)
            {
                return;
            }
            BinaryPrimitives.WriteInt32LittleEndian(block.AsSpan(0, sizeof(int)), value);
        }

        /// <summary>True when any of the 10 slot words at [+0x5A8] is non-zero.</summary>
        private static bool SoulWashHasAnySlot(byte[] block)
        {
            if (block == null || block.Length < NativeShenYouBlockSize)
            {
                return false;
            }
            for (var i = sizeof(int); i < NativeShenYouBlockSize; i++)
            {
                if (block[i] != 0)
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Reproduce the reachable part of 0x747CF4 as a read-only query: derive
        /// cap and base from the persisted source fields WITHOUT mutating anything.
        /// Returns false when the result depends on the unmodeled [[0x7D6014]]
        /// table (i.e. a slot is set), so callers fail-closed instead of guessing.
        /// </summary>
        private bool TrySoulWashState(out int cap, out int baseValue, out int current)
        {
            cap = 0;
            baseValue = 0;
            current = GetSoulWashCurrent();

            // 0x747CFF `cmp [+0x610],0 / jg`: mode<=0 zeroes cap+base and returns
            // without touching current -> reproducible.
            if (m_btNativeCommonInformationMode <= 0)
            {
                return true;
            }

            // mode>0: a non-zero slot needs 0x747B38 over [[0x7D6014]] -> not modeled.
            if (SoulWashHasAnySlot(m_NativeShenYouBlock))
            {
                return false;
            }

            // zero slots -> [+0x5BC]=0, 0x747B38 skipped, base=0, cap from bitmask.
            cap = SoulWashCap(m_wNativeCommonInformationFlags);
            baseValue = 0;
            return true;
        }

        /// <summary>
        /// Dispatch hook, called from the TPlayObject.Message.cs default arm ahead of
        /// cm-4's TryHandleNativeCmTailProtocol. Returns true when this file owns the
        /// ident (faithful reply OR deliberate fail-closed drop).
        /// </summary>
        internal bool TryHandleSoulWashCm(TProcessMessage processMessage)
        {
            switch (processMessage.wIdent)
            {
                case Grobal2.CM_4127:
                    SoulWashRecomputeAndSend(processMessage.nParam3);
                    return true;
                case Grobal2.CM_4128:
                    SoulWashNeighbourQuery(processMessage.nParam1,
                        processMessage.nParam2, processMessage.nParam3);
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>
        /// CM 4127, native leaf 0x6DAE8D -> 0x747CF4 (recompute) + 0x74730C (send),
        /// both on SELF. The leaf runs the pair when Tag==0, or when Tag==1 while a
        /// hero exists; every other Tag is a silent drop (0x6DAEBE `jne 0x6DBC2C`).
        /// The hero is only a gate — 战神 reloads EAX=[ebp-4]=self before both calls,
        /// so the work never runs on the hero object.
        ///
        /// 0x747CF4 recomputes cap/base and clamps [+0x5A4] to [0, cap] (base=0 here);
        /// 0x74730C answers SM 4033 (0xFC1) through [vmt+0x254] with the 32-byte body
        /// {int cur; int base; int cap; word[10] slots} and Tag = ([+0x178]==0x36).
        /// </summary>
        private void SoulWashRecomputeAndSend(int nTag)
        {
            // 0x6DAE94 / 0x6DAEBE gate: Tag!=0 and not (Tag==1 with hero) -> silent.
            if (nTag != 0 && !(nTag == 1 && m_HeroObject != null))
            {
                return;
            }

            if (!TrySoulWashState(out var cap, out var baseValue, out var current))
            {
                // slot array set -> base needs [[0x7D6014]]; withhold rather than invent.
                NativeCmTailFailClosed.Drop(Grobal2.CM_4127, m_sCharName);
                return;
            }

            // 0x747E2D..0x747E58: when mode>0 native clamps current into [0,cap-base]
            // and writes it back. mode<=0 returns before the clamp (cap==base==0), so
            // current is sent untouched.
            if (m_btNativeCommonInformationMode > 0)
            {
                if (current < 0)
                {
                    current = 0;
                }
                if (current + baseValue > cap)
                {
                    current = cap - baseValue;
                }
                SetSoulWashCurrent(current);
            }

            SendSoulWashState(cap, baseValue, current);
        }

        /// <summary>0x74730C: build the 32-byte state body and send SM 4033 to self.</summary>
        private void SendSoulWashState(int cap, int baseValue, int current)
        {
            var body = new byte[SoulWashStateBodySize];
            BinaryPrimitives.WriteInt32LittleEndian(body.AsSpan(0, sizeof(int)), current);
            BinaryPrimitives.WriteInt32LittleEndian(body.AsSpan(4, sizeof(int)), baseValue);
            BinaryPrimitives.WriteInt32LittleEndian(body.AsSpan(8, sizeof(int)), cap);
            var block = m_NativeShenYouBlock;
            if (block != null && block.Length >= NativeShenYouBlockSize)
            {
                // [+0x5A8] word[10] = shenYou window bytes 4..23.
                Array.Copy(block, sizeof(int), body, 12, NativeShenYouBlockSize - sizeof(int));
            }

            var tag = m_btRaceServer == SoulWashHeroRace ? 1 : 0;
            var header = Grobal2.MakeDefaultMsg(Grobal2.SM_4033, 0, 0, tag, 0);
            SendSocket(header, body);
        }

        /// <summary>
        /// CM 4128, native leaf 0x6DAF23 -> 0x6B7184. 0x6B71A2 validates the target
        /// through 0x76C9D4 (target must sit in self's own 3×3 cell neighbourhood and
        /// not be a ghost); 战神 round-trips a raw object pointer through the client,
        /// which this port replaces with an ObjectManager id lookup + same-map +
        /// Chebyshev distance ≤ 1 + not-ghost, exactly as cm-4 established.
        ///
        /// After the sweep 0x6B71AB requires the target race in {0, 0x36}; anything
        /// else is silent. The surviving path answers SM 4037 (0xFC5) TO SELF through
        /// [vmt+0x254] with the 24-byte body {int [T+0x60C]; byte[20] [T+0x5A8]}.
        /// A race-0x36 (hero) target keeps its shenYou window on the native hero
        /// object, which this port's HeroObject does not model, so that leg is
        /// fail-closed; player targets are answered in full.
        /// </summary>
        private void SoulWashNeighbourQuery(int nRecog, int nX, int nY)
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

            if (target.m_btRaceServer != Grobal2.RC_PLAYOBJECT
                && target.m_btRaceServer != SoulWashHeroRace)
            {
                return;
            }

            if (!(target is TPlayObject tp))
            {
                // race 0x36 hero target: [T+0x60C]/[T+0x5A8] live on the native hero
                // object, unmodeled on HeroObject -> withhold the reply.
                NativeCmTailFailClosed.Drop(Grobal2.CM_4128, m_sCharName);
                return;
            }

            var body = new byte[SoulWashNeighbourBodySize];
            // [T+0x60C] read as a dword; the C# model is a 16-bit field, zero-extended.
            BinaryPrimitives.WriteInt32LittleEndian(body.AsSpan(0, sizeof(int)),
                tp.m_wNativeCommonInformationFlags);
            var block = tp.m_NativeShenYouBlock;
            if (block != null && block.Length >= NativeShenYouBlockSize)
            {
                // [T+0x5A8] byte[20] = target shenYou window bytes 4..23.
                Array.Copy(block, sizeof(int), body, sizeof(int),
                    NativeShenYouBlockSize - sizeof(int));
            }

            var header = Grobal2.MakeDefaultMsg(Grobal2.SM_4037, 0, 0, 0, 0);
            SendSocket(header, body);
        }
    }
}
