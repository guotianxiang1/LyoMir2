using System;
using System.Buffers.Binary;
using GameSvr.Services;
using SystemModule;

namespace GameSvr
{
    /// <summary>
    /// 洗灵石 / 祈福神佑袋 (灵佑点) subsystem — CM 4126 / 4127 / 4128.
    ///
    /// Replaces the fail-closed stubs cm-4 wired in TPlayObject.NativeCmTailProtocol.cs.
    /// The dispatcher (TPlayObject.Message.cs default arm) calls
    /// <see cref="TryHandleSoulWashCm"/> first; only the legs this file cannot
    /// reproduce byte-for-byte fall through to NativeCmTailFailClosed.
    ///
    /// ── Native data structure (base 0x400000; obj = TPlayObject/THumanKind offsets)
    ///   [+0x5A4] int      current 灵佑点     \ 24-byte "shenYou" window, persisted
    ///   [+0x5A8] word[10] slots (神佑属性)   / verbatim as ScriptData section 2
    ///                                          -> m_NativeShenYouBlock[0..23]
    ///                                             (TPlayObject.NativeScriptSections.cs)
    ///   [+0x60C] dword    cap bitmask        -> raw record rec+0x580 (see below)
    ///   [+0x610] dword    prereq / mode      -> raw record rec+0x57C (see below)
    ///   [+0x59C] int      cap    (DERIVED)   \ recomputed by 0x747CF4; never
    ///   [+0x5A0] int      base   (DERIVED)   / persisted -> computed on demand
    ///   [+0x5BC] byte     slot-count(DERIVED)/
    ///   [+0x178] byte     race (m_btRaceServer); player=0, hero=0x36
    ///
    /// ── DB codec offsets — CRITICAL: the record offset is NOT the object offset.
    /// The encoder sub_6B0FF0 / decoder sub_6AFD7C move these fields across:
    ///   obj+0x60C (cap)   &lt;-&gt; rec+0x580  (enc 0x6B13DF, dec 0x6B05E9->0x6B05F2)
    ///   obj+0x610 (prereq)&lt;-&gt; rec+0x57C  (enc 0x6B13EB, dec 0x6B05FB->0x6B0604)
    /// The shared DTO codec models NEITHER rec+0x580 nor rec+0x57C. The login/save
    /// bridge therefore restores and persists the live HeroZodiacBlessMask/Gate fields
    /// straight against m_NativeHumanData; fail-closed handlers may also validate the
    /// same raw record directly. The decoder applies ONE fixup:
    /// 0x6B060A `test eax,eax / jg` then 0x6B0611 `mov [obj+0x610],1` — a stored
    /// prereq &lt;= 0 is forced to 1, so after login the prereq is ALWAYS &gt;= 1.
    ///
    /// NOTE: m_wNativeCommonInformationFlags / m_btNativeCommonInformationMode read
    /// rec+0x60C / rec+0x610, which the codec fills from obj+0x608 (word) / obj+0x618
    /// (byte) — a DIFFERENT "common information" feature, NOT these soul-wash fields.
    /// They are deliberately NOT used here.
    ///
    /// ── cap formula, verbatim from 0x747CF4 (0x747D81..0x747DB9):
    ///   cap = 20 * popcount([+0x60C] &amp; 0x555555) + 40 * popcount([+0x60C] &amp; 0xAAAAAA)
    ///   (even bits `shl 2`·`lea *5` = *20; odd bits `shl 3`·`lea *5` = *40; the field
    ///    is a full dword and the masks span bits 0..23; popcount is 0x4C7A34.)
    ///
    /// ── base formula (0x747B38): sum over the non-zero slots of
    ///   [[0x7D6014]].lookup(slotId).[+4] — the fixed-length (0x2B-byte) record table
    ///   CM 4125 rebuilds, NOT modeled in this port (loaded from a server config
    ///   file). When every slot is 0 the recompute never calls 0x747B38 and base is
    ///   exactly 0; a non-zero slot array is therefore FAIL-CLOSED. All 34 golden DB
    ///   blobs carry an all-zero shenYou window, so the reproducible path is live.
    /// </summary>
    public partial class TPlayObject
    {
        /// <summary>Each 灵气石 grants 5 灵佑点 (0x747530 `lea edx,[esi+esi*4]`; the
        /// ceil divisor at 0x747CF0 is the float 5.0).</summary>
        private const int SoulWashPointsPerStone = 5;

        /// <summary>[+0x178] value that marks the hero race (0x747343 / 0x6B71B5
        /// `cmp bl,0x36`).</summary>
        private const byte SoulWashHeroRace = 0x36;

        /// <summary>SM 4033/4037 body sizes (0x74735A push 0x20; 0x6B71E1 push 0x18).</summary>
        private const int SoulWashStateBodySize = 0x20;
        private const int SoulWashNeighbourBodySize = 0x18;

        /// <summary>Raw record slot for obj+0x60C (cap bitmask), enc 0x6B13DF.</summary>
        private const int SoulWashCapRecordOffset = 0x580;

        /// <summary>Raw record slot for obj+0x610 (prereq/mode), enc 0x6B13EB.</summary>
        private const int SoulWashPrereqRecordOffset = 0x57C;

        /// <summary>
        /// Native decoder sub_6AFD7C @0x6B05E9..0x6B0611. Restores the live object
        /// fields used by the zodiac/soul-wash cluster and applies the sole decoder
        /// normalization: a stored obj+0x610 value less than or equal to zero becomes 1.
        /// </summary>
        internal bool RestoreNativeHeroZodiacState()
        {
            var raw = m_NativeHumanData;
            if (raw == null || raw.Length < SoulWashCapRecordOffset + sizeof(uint))
            {
                HeroZodiacBlessMask = 0;
                HeroZodiacBlessGate = 0;
                return false;
            }

            HeroZodiacBlessMask = BinaryPrimitives.ReadUInt32LittleEndian(
                raw.AsSpan(SoulWashCapRecordOffset, sizeof(uint)));
            var storedGate = BinaryPrimitives.ReadInt32LittleEndian(
                raw.AsSpan(SoulWashPrereqRecordOffset, sizeof(int)));
            HeroZodiacBlessGate = storedGate > 0 ? storedGate : 1;
            return true;
        }

        /// <summary>
        /// Native encoder sub_6B0FF0 @0x6B13D9..0x6B13EB. Writes the current live
        /// obj+0x60C/obj+0x610 dwords back verbatim; normalization belongs only to load.
        /// </summary>
        internal bool PersistNativeHeroZodiacState()
        {
            var raw = m_NativeHumanData;
            if (raw == null || raw.Length < SoulWashCapRecordOffset + sizeof(uint))
                return false;

            BinaryPrimitives.WriteUInt32LittleEndian(
                raw.AsSpan(SoulWashCapRecordOffset, sizeof(uint)), HeroZodiacBlessMask);
            BinaryPrimitives.WriteInt32LittleEndian(
                raw.AsSpan(SoulWashPrereqRecordOffset, sizeof(int)), HeroZodiacBlessGate);
            return true;
        }

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
        private static int SoulWashCap(uint bitmask)
        {
            return SoulWashPopCount(bitmask & 0x555555u) * (4 * SoulWashPointsPerStone)
                   + SoulWashPopCount(bitmask & 0xAAAAAAu) * (8 * SoulWashPointsPerStone);
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

        /// <summary>Persist a clamped [+0x5A4] back into the shenYou window; native
        /// mutates this field in the recompute (0x747E58) and it rides out on the
        /// next save through ScriptData section 2.</summary>
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

        /// <summary>Read the cap bitmask (obj+0x60C) out of a player's raw record,
        /// with no fixup. Returns false when the record is unavailable.</summary>
        private static bool TryReadSoulWashCapBitmask(byte[] raw, out uint bitmask)
        {
            bitmask = 0;
            if (raw == null || raw.Length < SoulWashCapRecordOffset + sizeof(int))
            {
                return false;
            }
            bitmask = BinaryPrimitives.ReadUInt32LittleEndian(
                raw.AsSpan(SoulWashCapRecordOffset, sizeof(uint)));
            return true;
        }

        /// <summary>
        /// Read the two persisted source fields (cap bitmask obj+0x60C, prereq
        /// obj+0x610) out of self's raw record and apply the decoder's prereq fixup.
        /// This existing raw accessor remains the fail-closed gate for handlers that
        /// can also be invoked on synthetic objects outside the normal login path.
        /// </summary>
        private bool TrySoulWashSource(out uint capBitmask, out int prereq)
        {
            prereq = 0;
            var raw = m_NativeHumanData;
            if (!TryReadSoulWashCapBitmask(raw, out capBitmask))
            {
                return false;
            }
            var stored = BinaryPrimitives.ReadInt32LittleEndian(
                raw.AsSpan(SoulWashPrereqRecordOffset, sizeof(int)));
            prereq = stored > 0 ? stored : 1; // decoder 0x6B060A..0x6B0611
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
                case Grobal2.CM_4126:
                    SoulWashApply(processMessage.nParam3, processMessage.nParam1);
                    return true;
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
        /// both on SELF. The pair runs when Tag==0, or when Tag==1 while a hero
        /// exists; every other Tag is a silent drop (0x6DAEBE `jne 0x6DBC2C`). The
        /// hero is only a gate — 战神 reloads EAX=[ebp-4]=self before both calls, so
        /// the work never runs on the hero.
        ///
        /// 0x747CF4 recomputes cap/base and clamps [+0x5A4] to [0, cap-base];
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

            if (!TrySoulWashSource(out var capBitmask, out var prereq))
            {
                NativeCmTailFailClosed.Drop(Grobal2.CM_4127, m_sCharName);
                return;
            }

            var current = GetSoulWashCurrent();
            int cap;
            var baseValue = 0;
            if (prereq <= 0)
            {
                // 0x747D08: cap=base=0, current untouched (dead after the login fixup).
                cap = 0;
            }
            else
            {
                // 0x747DBF..0x747E29: non-zero slot -> 0x747B38 base sum.
                if (SoulWashHasAnySlot(m_NativeShenYouBlock))
                {
                    if (!TryComputeSoulWashBaseFromConfig(out var configuredBase))
                    {
                        NativeCmTailFailClosed.Drop(Grobal2.CM_4127, m_sCharName);
                        return;
                    }
                    baseValue = configuredBase;
                }
                cap = SoulWashCap(capBitmask);
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
        /// A race-0x36 (hero) target keeps its state on the native hero object, which
        /// HeroObject does not model, so that leg is fail-closed; player targets are
        /// answered in full.
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

            if (!TryReadSoulWashCapBitmask(tp.m_NativeHumanData, out var capBitmask))
            {
                // target record unavailable -> cannot build the [T+0x60C] field.
                NativeCmTailFailClosed.Drop(Grobal2.CM_4128, m_sCharName);
                return;
            }

            var body = new byte[SoulWashNeighbourBodySize];
            // [T+0x60C] read as a dword (no fixup); it is obj+0x60C = rec+0x580.
            BinaryPrimitives.WriteUInt32LittleEndian(body.AsSpan(0, sizeof(uint)), capBitmask);
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

        /// <summary>
        /// CM 4126, native leaf 0x6DAE74 -> 0x6BF75C. REPLACES cm-4's fail-closed
        /// ClientNativeSoulWashApply (now unreachable, kept in their file untouched):
        /// reproduces the bare reply and every provable reply code, and only
        /// fail-closes the one leg that actually burns a 灵气石.
        ///
        /// 0x6BF75C selector: Tag==1 with a hero runs on the hero, Tag==0 runs on
        /// self, everything else (incl. Tag==1 with no hero) is the bare all-zero SM
        /// 4034 at 0x6BF8E9. Both real bodies answer SM 4034 (0xFC2), whose result
        /// code rides in the Tag slot (0x6BF7C5 push 0/1/0/0 -> Tag=1 etc.):
        ///   Tag=0  [+0x610]&lt;=0 or [+0x59C]&lt;=0     目标不具备洗灵状态
        ///   Tag=3  [+0x5A4]+[+0x5A0] &gt;= [+0x59C]    已洗到上限
        ///   Tag=2  0x746F10 &lt;= 0                    没有可用的洗灵石
        ///   Tag=1  用洗灵石成功
        ///
        /// The Tag=1/Tag=2 leg runs consume(0x746F10) + apply(0x747530): find the
        /// item at Recog, require its name == 灵气石, decrement [+0x26], delete /
        /// client-update it and add 5·n 灵佑点 with tier broadcasts (SM 0x38FF). That
        /// is item-subsystem machinery this port does not model, and it is
        /// destructive, so it is withheld. The gate replies — exactly the legs where
        /// native does NOT consume — are faithful.
        /// </summary>
        private void SoulWashApply(int nTag, int nRecog)
        {
            // 0x6BF839/0x6BF8E9: bare all-zero reply for Tag!=0 unless Tag==1 with a
            // hero. Identical to cm-4's already-live leg.
            if (nTag != 0 && !(nTag == 1 && m_HeroObject != null))
            {
                SendDefMessage(Grobal2.SM_4034, 0, 0, 0, 0, string.Empty);
                return;
            }

            // 0x6BF76C..0x6BF834: the hero leg reads the hero's own [+0x610]/[+0x59C]/
            // [+0x5A0]/[+0x5A4] window, which HeroObject does not model -> fail-closed.
            if (nTag != 0)
            {
                NativeCmTailFailClosed.Drop(Grobal2.CM_4126, m_sCharName);
                return;
            }

            // Tag==0: self leg.
            if (!TrySoulWashSource(out var capBitmask, out var prereq))
            {
                NativeCmTailFailClosed.Drop(Grobal2.CM_4126, m_sCharName);
                return;
            }

            // A non-zero slot array makes the base ([+0x5A0]) depend on [[0x7D6014]].
            if (SoulWashHasAnySlot(m_NativeShenYouBlock)
                && !TryComputeSoulWashBaseFromConfig(out var gateBase))
            {
                NativeCmTailFailClosed.Drop(Grobal2.CM_4126, m_sCharName);
                return;
            }

            var cap = prereq > 0 ? SoulWashCap(capBitmask) : 0;
            var baseValue = 0;
            if (SoulWashHasAnySlot(m_NativeShenYouBlock))
                TryComputeSoulWashBaseFromConfig(out baseValue);
            var current = GetSoulWashCurrent();

            // 0x6BF841 `jle` and 0x6BF84E `jle`: prereq or cap not positive -> Tag=0.
            if (prereq <= 0 || cap <= 0)
            {
                SendDefMessage(Grobal2.SM_4034, 0, 0, 0, 0, string.Empty);
                return;
            }

            // 0x6BF863 `jge`: already at / over the cap -> Tag=3.
            if (current + baseValue >= cap)
            {
                SendDefMessage(Grobal2.SM_4034, 0, 0, 3, 0, string.Empty);
                return;
            }

            // 0x6BF86B onwards: would consume a 灵气石 (0x746F10) and add points
            // (0x747530). Item find / name-match / delete / client-update and the
            // tier broadcasts are unmodeled and destructive -> fail-closed.
            NativeCmTailFailClosed.Drop(Grobal2.CM_4126, m_sCharName);
        }

        /// <summary>0x747B38 — sum table[+4] for each non-zero slot word in shenYou window.</summary>
        private bool TryComputeSoulWashBaseFromConfig(out int baseValue)
        {
            baseValue = 0;
            var block = m_NativeShenYouBlock;
            if (block == null || block.Length < NativeShenYouBlockSize)
                return false;

            Span<ushort> slots = stackalloc ushort[10];
            for (var i = 0; i < slots.Length; i++)
            {
                var off = sizeof(int) + i * sizeof(ushort);
                if (off + sizeof(ushort) > block.Length)
                    break;
                slots[i] = BitConverter.ToUInt16(block, off);
            }

            var sum = NativeShenYouAttributeConfig.Shared.ComputeBaseFromSlots(slots);
            if (sum < 0)
                return false;
            baseValue = sum;
            return true;
        }
    }
}
