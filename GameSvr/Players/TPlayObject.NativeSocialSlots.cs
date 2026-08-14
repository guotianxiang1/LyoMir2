using SystemModule;

namespace GameSvr
{
    public partial class TPlayObject
    {
        // 战神 login reconciliation sub_6CCE40 decides whether a relationship
        // exists by testing the LENGTH BYTE of a ShortString slot inside the
        // opaque 128-byte social block, NOT by looking at a parsed name:
        //   0x6CCE70  cmp byte [ebx+0xC48], 0     (spouse slot)
        //   0x6CCE9A  cmp byte [ebx+0xC58], 0     (master slot)
        //   0x6CCF07  cmp byte [ebx+eax*8+0xC78], 0   (students[i], eax = i*2)
        // obj+0xC48 == inflated record 0x650 (block base; LOAD 0x6B096C
        // `lea esi,[eax+0x658]` / SAVE 0x6B1687 `lea edi,[eax+0x658]`, rep movsd
        // 0x20).  Slots are 16 bytes: +0x00 spouse, +0x10 master, +0x20 companion,
        // +0x30..+0x70 students[0..4].
        //
        // We must test the raw byte rather than the derived string.  The codec's
        // tolerant slot reader (DBSvr/Core/NativeHumanDataCodec.cs) deliberately
        // returns "" for a slot whose length byte exceeds 15 or whose bytes are not
        // valid GBK -- which is the normal state of this region: in 30/30 golden
        // records the externally-written ':'/'$' companion string overruns its 0x670
        // slot and its ':' filler (0x3A = 58) lands in a length position.  Native reads
        // that as NON-empty.  Healing off the derived name would therefore clear
        // flags 战神 keeps, destroying a valid marriage or apprenticeship.
        internal const int NativeSocialBlockRecordOffset = 0x0650;
        internal const int NativeSocialSlotStride = 0x10;
        internal const int NativeSpouseSlotOffset = NativeSocialBlockRecordOffset;
        internal const int NativeMasterSlotOffset =
            NativeSocialBlockRecordOffset + NativeSocialSlotStride;
        internal const int NativeStudentSlotBaseOffset =
            NativeSocialBlockRecordOffset + 3 * NativeSocialSlotStride;
        internal const int NativeStudentSlotCount = 5;

        /// <summary>
        /// True when the social-block slot at <paramref name="recordOffset"/> holds
        /// a zero length byte, i.e. what 战神's `cmp byte [ebx+0xNNN], 0` treats as
        /// empty.  When the raw record is unavailable this returns false, so an
        /// absent record can never trigger a heal that clears persisted state.
        /// </summary>
        internal bool IsNativeSocialSlotEmpty(int recordOffset)
        {
            if (m_NativeHumanData == null
                || m_NativeHumanData.Length <= recordOffset)
            {
                return false;
            }
            return m_NativeHumanData[recordOffset] == 0;
        }

        internal bool IsNativeSpouseSlotEmpty() =>
            IsNativeSocialSlotEmpty(NativeSpouseSlotOffset);

        internal bool IsNativeMasterSlotEmpty() =>
            IsNativeSocialSlotEmpty(NativeMasterSlotOffset);

        internal bool IsNativeStudentSlotEmpty(int index)
        {
            if (index < 0 || index >= NativeStudentSlotCount) return true;
            return IsNativeSocialSlotEmpty(
                NativeStudentSlotBaseOffset + index * NativeSocialSlotStride);
        }

        /// <summary>
        /// 战神 sub_6CCE40 legs A and B: a relationship flag whose block name slot
        /// is empty is repaired to false at login.
        ///
        ///   0x6CCE67  cmp byte [ebx+0xB94], 0   ; boMarried set?
        ///   0x6CCE77  je  0x6CCE8A              ; spouse slot empty ->
        ///   0x6CCE8A  mov byte [ebx+0xB94], 0   ;   clear boMarried
        ///   0x6CCE91  cmp byte [ebx+0xB95], 0   ; boStudent (RTTI IsAStudent) set?
        ///   0x6CCEA1  je  0x6CCED3              ; master slot empty ->
        ///   0x6CCED3  mov byte [ebx+0xB95], 0   ;   clear boStudent
        ///
        /// Each leg is a single store: native emits NO message, does not touch the
        /// name slot, does not increment the 0xBF4 counter and does not refresh the
        /// name plate.  It also performs no save -- the repair reaches the record on
        /// a later periodic save (sub_6B0FF0 via sub_6B6510), so nothing is flushed
        /// here either.
        ///
        /// Ordering: heal B is tested BEFORE the graduation level compare at
        /// 0x6CCEB0, so a student with an empty master slot is un-flagged and never
        /// graduates regardless of level.
        /// </summary>
        internal void HealNativeRelationFlags()
        {
            // Leg A -- 0x6CCE67 / 0x6CCE77 / 0x6CCE8A
            if (m_boMarried && IsNativeSpouseSlotEmpty())
            {
                m_boMarried = false;
            }
            else if (m_boMarried && !IsNativeSpouseSlotEmpty())
            {
                NotifyNativeSpouseOnlineCoord();
            }
            // Leg B -- 0x6CCE91 / 0x6CCEA1 / 0x6CCED3
            if (m_boStudent)
            {
                if (IsNativeMasterSlotEmpty())
                {
                    m_boStudent = false;
                }
                else if (m_Abil.Level >= M2Share.g_Config.nMasterOKLevel)
                {
                    // 0x6CCEA3  movzx eax, word [ebx+0x278]      ; Level
                    // 0x6CCEAA  mov edx,[0x7D5CC4]               ; SETKEY_CHUSHI
                    // 0x6CCEB0  cmp eax,[edx] / 0x6CCEB2 jl 0x6CCEC2
                    // 0x6CCEB4  mov edx,1 / 0x6CCEBB call sub_6C5EC8
                    // The student logged in already past the 出师 level, so the
                    // apprenticeship graduates immediately -- mode 1.  Note this
                    // runs BEFORE leg C, and its `jmp 0x6CCEDA` means the
                    // student-array walk still happens afterwards.
                    //
                    // Below-level students take 0x6CCEC2 instead: sub_6CD188 with
                    // cl=2, the "your master is online at <map> x,y" notifier,
                    // which is a separate feature and stays unported here.
                    NativeLeaveMaster(1);
                }
                else
                {
                    // sub_6CD188(cl=2) -> sub_6CF000 master coord @0x006CF000
                    NotifyNativeMasterOnlineCoord();
                }
            }
            // Leg C -- the student-array reconciliation (0x6CCEDA..0x6CD01F)
            HealNativeStudentSlots();
        }

        /// <summary>
        /// 战神 sub_6CCE40 leg C @0x6CCEDA..0x6CD01F.  Walks the five student slots
        /// and repairs the stored student COUNT to the number of non-empty slots,
        /// additionally graduating any student who crossed the 出师 level while this
        /// master was offline.
        ///
        /// Outer gate (both must hold, else the whole loop is skipped):
        ///   0x6CCEDA  movzx eax, word [ebx+0x278]    ; Level
        ///   0x6CCEE1  mov edx,[0x7D6468]             ; SETKEY_SHOUTU cell
        ///   0x6CCEE7  cmp eax,[edx] / 0x6CCEE9 jl 0x6CD01F
        ///   0x6CCEEF  cmp byte [ebx+0xB97],0 / 0x6CCEF6 jbe 0x6CD01F
        /// `[0x7D6468]` is SETKEY_SHOUTU (the ASCII key at 0x79A704, a length-13
        /// Delphi AnsiString) == g_Config.nMinMasterLevel; `[0x7D5CC4]` used below is
        /// SETKEY_CHUSHI (0x79A6EC) == g_Config.nMasterOKLevel.
        ///
        /// Loop body, i = 0..4 (native indexes `[ebx+eax*8+0xC78]` with eax = i*2,
        /// i.e. stride 16 -- 0x6CCF03 `mov eax,esi` / 0x6CCF05 `add eax,eax`):
        ///   0x6CCF07  slot empty -> skip, and it does NOT count toward liveCount
        ///   0x6CCF15  inc liveCount
        ///   0x6CCF35  GetPlayObject(studentName)
        ///   0x6CCF3C  offline -> announce leg (sub_6CD188, cl=1)
        ///   0x6CCF51  online but below CHUSHI -> announce leg (sub_6CD188, cl=1)
        ///   otherwise the remote-graduation leg:
        ///     0x6CCF53  dec byte [ebx+0xB97]        ; storedCount--
        ///     0x6CCF59  dec liveCount
        ///     0x6CCF5C  mov byte [ebx+0xB91],1      ; latch
        ///     0x6CCF63  inc dword [ebx+0xBF4]       ; ApprenticeNum++ (rec 0x174)
        ///     0x6CCFB7  SysMsg("你的徒弟 " + name + " 顺利出师！") cx=0xFCFF
        ///               (fragments: 0x6CD0D4 len 9, 0x6CD0E0 len 11)
        ///     0x6CCFC5  mov byte [ebx+i*16+0xC78],0 ; clear the slot
        /// Count fixup after the loop:
        ///   0x6CD005  cmp storedCount, liveCount / 0x6CD00E je (no change)
        ///   0x6CD013  mov byte [ebx+0xB97], liveCount
        ///   0x6CD019  inc dword [ebx+0xBF4]
        ///
        /// Emptiness is decided on the RAW slot length byte, exactly as legs A/B do,
        /// so a slot holding non-GBK or over-length bytes counts as OCCUPIED here
        /// just as native counts it.  The announce legs are deliberately NOT emitted:
        /// they are sub_6CD188, the "your student is online at <map> x,y" notifier,
        /// which is a separate feature from the heal.
        /// </summary>
        private void HealNativeStudentSlots()
        {
            // 0x6CCEDA / 0x6CCEE7: Level >= SETKEY_SHOUTU
            if (m_Abil.Level < M2Share.g_Config.nMinMasterLevel)
            {
                return;
            }
            // 0x6CCEEF: unsigned test, so only a stored count of 0 skips.
            if (m_nStudentCount <= 0)
            {
                return;
            }

            var liveCount = 0;
            for (var i = 0; i < NativeStudentSlotCount; i++)
            {
                // 0x6CCF07: empty slot contributes nothing and is not announced.
                if (IsNativeStudentSlotEmpty(i))
                {
                    continue;
                }
                liveCount++;

                var studentName = m_sStudentNames != null
                                  && i < m_sStudentNames.Length
                    ? m_sStudentNames[i]
                    : null;
                if (string.IsNullOrEmpty(studentName))
                {
                    // The slot is occupied per the raw length byte but the parsed
                    // name is unusable, so GetPlayObject cannot be attempted.
                    // Native would call it with whatever bytes are there and get
                    // nil; that lands on the offline announce leg, which we do not
                    // emit.  liveCount has already been incremented, matching
                    // 0x6CCF15 running before the lookup.
                    continue;
                }

                var student = M2Share.UserEngine.GetPlayObject(studentName);
                if (student == null)
                {
                    continue;   // 0x6CCF3C offline -> native sub_6CD188 另径，未完整移植
                }
                if (student.m_Abil.Level < M2Share.g_Config.nMasterOKLevel)
                {
                    NotifyNativeStudentOnlineCoord(studentName);
                    continue;   // 0x6CCF51 below 出师 -> announce only
                }

                // 0x6CCF53..0x6CCFC5 remote graduation.
                m_nStudentCount--;
                liveCount--;
                m_boMaster = true;                  // 0x6CCF5C byte [ebx+0xB91] := 1
                BumpNativeApprenticeNum();          // 0x6CCF63 inc [ebx+0xBF4]
                SendMsg(this, Grobal2.RM_SYSMESSAGE, 0, 0xFF, 0xFC, 0,
                    "你的徒弟 " + studentName + " 顺利出师！");
                ClearNativeStudentSlot(i);          // 0x6CCFC5
                if (m_sStudentNames != null && i < m_sStudentNames.Length)
                {
                    m_sStudentNames[i] = string.Empty;
                }
            }

            // 0x6CD005..0x6CD019 count fixup.
            if (m_nStudentCount != liveCount)
            {
                m_nStudentCount = liveCount;
                BumpNativeApprenticeNum();
            }
        }

        /// <summary>
        /// 战神 `inc dword [ebx+0xBF4]` -- the apprentice-relationship change
        /// counter, persisted at record 0x174 (SAVE 0x6B13A0 `mov eax,[ebx+0xBF4]`,
        /// LOAD 0x6B05AF).  The original DBServer republishes it to the
        /// `mir3.user_index.ApprenticeNum` column, which is why it must actually
        /// move rather than be dropped.
        ///
        /// GameSvr has no runtime field for obj+0xBF4, so the counter is carried in
        /// the record blob itself.  That is safe and in fact required here: the codec
        /// does not model 0x174 either, so TryEncode clone-carries whatever this
        /// blob holds (the patch-over-clone mechanism already used for the scalars in
        /// TPlayObject.NativeUnmappedScalars.cs).  Incrementing in place therefore
        /// reaches the wire on the next save, exactly as native's in-object increment
        /// reaches it through sub_6B0FF0.
        ///
        /// Native increments a 32-bit field with no clamp; reproduce that, including
        /// the unchecked wrap, rather than saturating.
        /// </summary>
        private void BumpNativeApprenticeNum()
        {
            const int apprenticeNumOffset = 0x0174;
            var raw = m_NativeHumanData;
            if (raw == null || raw.Length < apprenticeNumOffset + sizeof(int))
            {
                return;
            }
            var span = raw.AsSpan(apprenticeNumOffset, sizeof(int));
            var current = System.Buffers.Binary.BinaryPrimitives
                .ReadInt32LittleEndian(span);
            System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(
                span, unchecked(current + 1));
        }

        /// <summary>
        /// 战神 0x6CCFC5 `mov byte [ebx+i*16+0xC78], 0`: clears a student slot by
        /// zeroing its ShortString LENGTH byte only.  The Delphi assign helper
        /// (M2 sub_4039E4) never zero-fills a slot tail, and neither does this --
        /// the stale name bytes stay behind the zero length byte exactly as native
        /// leaves them, which keeps the save record byte-identical.
        /// </summary>
        private void ClearNativeStudentSlot(int index)
        {
            if (index < 0 || index >= NativeStudentSlotCount) return;
            var offset = NativeStudentSlotBaseOffset
                         + index * NativeSocialSlotStride;
            if (m_NativeHumanData == null
                || m_NativeHumanData.Length <= offset)
            {
                return;
            }
            m_NativeHumanData[offset] = 0;
        }
    }
}
