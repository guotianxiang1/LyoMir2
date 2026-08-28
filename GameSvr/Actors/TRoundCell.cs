namespace GameSvr
{
    /// <summary>
    /// Field partial for native <c>TRoundCell</c> (VMT 0x006855D8, instance size 88,
    /// parent TObject). Own band <c>[0x004, 0x058)</c> = 84 bytes.
    ///
    /// ── What it is
    /// A short append-only list of candidate cells, each carrying two extra words.
    /// THeroAct owns one per hero at <c>[Self+0x634]</c>, created in its constructor
    /// (<c>0x686585 mov dl,1 / mov eax,[0x68558C] / call 0x68641C</c>, then
    /// <c>0x686591 mov [edi+0x634], eax</c>) — per hero, not shared. The hero mover
    /// <c>sub_68B838</c> reads its count at <c>0x68B937</c>
    /// (<c>cmp dword [[Self+0x634]+0x4C],1 / jb</c>) and then picks an entry at
    /// random (<c>0x68B946 Random(count)</c> feeding <c>sub_68647C</c>), taking
    /// <c>word+4</c> of the fetched record as a direction.
    ///
    /// ── Layout, from the accessors
    /// <code>
    ///   +0x004 .. 9 records, stride 8: { word X, word Y, word A, word B }
    ///   +0x04C    count
    ///   +0x050    0xFFFF   <- the only two fields the ctor writes
    ///   +0x052    0xFFFF
    ///   +0x054 .. 4 bytes UNDETERMINED (no accessor in the recovered cluster
    ///             touches them; instance size 88 says they exist)
    /// </code>
    /// Capacity follows from the count's own offset: the records start at +0x004 and
    /// the count sits at +0x04C, leaving 0x48 = 72 bytes = 9 records of stride 8.
    /// </summary>
    public partial class TRoundCell
    {
        /// <summary>Records that fit between +0x004 and the count at +0x04C.</summary>
        internal const int SlotCount = 9;

        /// <summary>+0x004 record X (word).</summary>
        private readonly ushort[] _x = new ushort[SlotCount];

        /// <summary>+0x006 record Y (word).</summary>
        private readonly ushort[] _y = new ushort[SlotCount];

        /// <summary>+0x008 record A (word). The hero mover reads this one as a
        /// direction (<c>0x68B95B movzx esi, word [ebp-0x18]</c> = <c>word+4</c> of
        /// the 8-byte record <see cref="GetByIndex"/> copies out).</summary>
        private readonly ushort[] _a = new ushort[SlotCount];

        /// <summary>+0x00A record B (word). Written by <see cref="Add"/> and copied
        /// out by <see cref="GetByIndex"/>, but no consumer appears in the recovered
        /// cluster, so its meaning is UNDETERMINED and it is only carried.</summary>
        private readonly ushort[] _b = new ushort[SlotCount];

        /// <summary>+0x04C.</summary>
        private int _count;

        /// <summary>+0x050, seeded 0xFFFF by ctor <c>sub_68641C</c>
        /// (<c>0x686428 mov word [eax+0x50],0xFFFF</c>). No reader recovered.</summary>
        private readonly ushort _sentinelA = 0xFFFF;

        /// <summary>+0x052, seeded 0xFFFF (<c>0x68642E</c>). No reader recovered.</summary>
        private readonly ushort _sentinelB = 0xFFFF;

        internal int Count => _count;

        /// <summary>Exposes +0x050 so the seeded value is observable without
        /// reflection. Native has no such getter; the field is the faithful part and
        /// an accessor changes neither layout nor behaviour.</summary>
        internal ushort SentinelA => _sentinelA;

        /// <summary>Exposes +0x052. See <see cref="SentinelA"/>.</summary>
        internal ushort SentinelB => _sentinelB;

        /// <summary>
        /// <c>sub_686474(Self)</c> — <c>xor edx,edx / mov [Self+0x4C],edx / ret</c>.
        /// Count reset only; the records keep their previous contents. The hero mover
        /// calls this at the top of every move (<c>0x68B893</c>), so the list is
        /// rebuilt per decision pass.
        /// </summary>
        internal void Clear()
        {
            _count = 0;
        }

        /// <summary>
        /// <c>sub_686490(Self, dx=X, cx=Y, [ebp+0x0C]=a, [ebp+8]=b)</c>, <c>ret 8</c>.
        /// <code>
        ///   686494  ebx = [Self+0x4C]
        ///   686497  word[Self+ebx*8+4] = X
        ///   68649F  word[Self+edx*8+6] = Y      ; index re-read each time
        ///   6864AB  word[Self+edx*8+8] = [ebp+0x0C]
        ///   6864B7  word[Self+edx*8+0xA] = [ebp+8]
        ///   6864BC  inc [Self+0x4C]
        /// </code>
        /// <b>There is no capacity check.</b> Native writes at
        /// <c>Self + count*8 + 4</c> for whatever the count happens to be and then
        /// increments it, so a tenth Add runs off the records into +0x04C onward.
        /// That is original behaviour, so the bound is not "fixed" here; the guard
        /// below refuses the write instead of corrupting adjacent managed fields,
        /// which is the closest a managed port can get without inventing a native
        /// clamp. The only caller, <c>sub_68BEC0</c> at <c>0x68BF19</c>, appends at
        /// most one entry per candidate direction, and there are 8 directions, so 9
        /// slots are not exceeded on that path.
        /// </summary>
        internal void Add(ushort x, ushort y, ushort a, ushort b)
        {
            var index = _count;
            _count++;

            if (index < 0 || index >= SlotCount)
            {
                // Native would scribble past the record array here. Refusing the
                // store keeps the count advancing exactly as native does (the
                // increment above is unconditional, matching 0x6864BC) while not
                // corrupting unrelated state.
                return;
            }

            _x[index] = x;
            _y[index] = y;
            _a[index] = a;
            _b[index] = b;
        }

        /// <summary>
        /// <c>sub_68647C(Self, dx=index, ecx=out)</c> — copies the 8-byte record out
        /// as two dwords:
        /// <code>
        ///   68647F  ecx = dword[Self+edx*8+4] ; dword[out]   = ecx   ; X,Y
        ///   686485  ecx = dword[Self+edx*8+8] ; dword[out+4] = ecx   ; A,B
        /// </code>
        /// No range check, exactly as native. The hero mover calls it with
        /// <c>Random(count)</c>, and the count is read from +0x04C, so an in-range
        /// index is the caller's responsibility.
        /// </summary>
        internal void GetByIndex(int index, out ushort x, out ushort y,
            out ushort a, out ushort b)
        {
            if (index < 0 || index >= SlotCount)
            {
                x = y = a = b = 0;
                return;
            }

            x = _x[index];
            y = _y[index];
            a = _a[index];
            b = _b[index];
        }
    }
}
