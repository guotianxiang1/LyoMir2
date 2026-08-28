namespace GameSvr
{
    /// <summary>
    /// Field partial for native <c>TARoundList</c> (VMT 0x00685580, instance size
    /// 540, parent TObject). Own band <c>[0x004, 0x21C)</c> = 536 bytes.
    ///
    /// ── What it is
    /// A fixed 64-slot ring of visited cells. THeroAct owns one per hero at
    /// <c>[Self+0x630]</c>, created in its constructor
    /// (<c>0x686573 mov dl,1 / mov eax,[0x685534] / call 0x686338</c>, then
    /// <c>0x68657F mov [edi+0x630], eax</c>), so this is per-hero state and not a
    /// shared singleton. The hero mover <c>sub_68B838</c> consults it while walking.
    ///
    /// ── Layout, taken from the accessors rather than inferred from the span
    /// <code>
    ///   +0x004 .. 64 records, stride 8: { word X, word Y, word W }  (2 bytes pad)
    ///   +0x20C    count      (append stops at 0x3F)
    ///   +0x210    ring write cursor
    ///   +0x214    0xFFFF     <- the ONLY two fields the ctor writes explicitly
    ///   +0x216    0xFFFF
    /// </code>
    /// The constructor <c>sub_686338</c> is 12 instructions and writes nothing but
    /// those two sentinels, so every other field starts at the zero Delphi already
    /// filled in — which is why the record array needs no explicit initialisation
    /// here either.
    /// </summary>
    public partial class TARoundList
    {
        /// <summary>Ring capacity. <c>0x6863A2 cmp ebx,0x3F / jl</c> appends only
        /// below 63, and the wrap at <c>0x6863AD and ebx,0x8000003F</c> is a mod-64,
        /// so indices run 0..63.</summary>
        internal const int SlotCount = 64;

        /// <summary>The append ceiling itself, kept separate from
        /// <see cref="SlotCount"/> because native compares against 0x3F (63) while
        /// wrapping modulo 64 — one slot is only ever reachable through the ring
        /// path, never through a plain append.</summary>
        internal const int AppendLimit = 0x3F;

        /// <summary>Native miss value of <see cref="IndexOf"/>. It is <b>-2</b>, not
        /// -1: <c>0x6863EE mov edi,0xFFFFFFFE</c> seeds the result and the scan only
        /// overwrites it on a hit. The one caller tests <c>test eax,eax / setge</c>,
        /// i.e. "found" means &gt;= 0, so -2 and -1 would behave the same there —
        /// but the value is part of the contract and is reproduced exactly.</summary>
        internal const int NotFound = -2;

        /// <summary>+0x004 record X (word).</summary>
        private readonly ushort[] _x = new ushort[SlotCount];

        /// <summary>+0x006 record Y (word).</summary>
        private readonly ushort[] _y = new ushort[SlotCount];

        /// <summary>+0x008 record W (word). Written by Add, never read by any of the
        /// accessors in the 0x686300..0x686600 cluster; its consumer is
        /// UNDETERMINED, so it is stored verbatim and nothing is assumed about
        /// it.</summary>
        private readonly ushort[] _w = new ushort[SlotCount];

        /// <summary>+0x20C.</summary>
        private int _count;

        /// <summary>+0x210, the ring cursor used once the list is full.</summary>
        private int _ringCursor;

        /// <summary>+0x214 and +0x216, both seeded to 0xFFFF by the ctor. No reader
        /// appears anywhere in the recovered cluster, so they are modelled as state
        /// rather than given a meaning.</summary>
        private readonly ushort _sentinelA = 0xFFFF;

        /// <summary>+0x216. See <see cref="_sentinelA"/>.</summary>
        private readonly ushort _sentinelB = 0xFFFF;

        internal int Count => _count;

        /// <summary>Exposes +0x214 so the seeded value is observable without
        /// reflection. Native has no such getter — the field is the faithful part,
        /// and an accessor changes neither layout nor behaviour.</summary>
        internal ushort SentinelA => _sentinelA;

        /// <summary>Exposes +0x216. See <see cref="SentinelA"/>.</summary>
        internal ushort SentinelB => _sentinelB;

        /// <summary>
        /// <c>sub_686398(Self, dx=X, cx=Y, [ebp+8]=W)</c>, <c>ret 4</c>.
        /// <code>
        ///   68639C  ebx = [Self+0x20C]                   ; count
        ///   6863A2  cmp ebx,0x3F / jl append
        ///   6863A7  ebx = [Self+0x210] ; and ebx,0x8000003F   ; signed mod 64
        ///   6863BA  inc [Self+0x210]
        ///   6863C2  append: inc [Self+0x20C]
        ///   6863C8  word[Self+ebx*8+4] = X
        ///   6863CD  word[Self+ebx*8+6] = Y
        ///   6863D6  word[Self+ebx*8+8] = W
        /// </code>
        /// Note the index used on the append path is the count read BEFORE the
        /// increment, and on the ring path it is the cursor read before its own
        /// increment, so the two paths never write the same slot in one call.
        /// </summary>
        internal void Add(ushort x, ushort y, ushort w)
        {
            int index;
            if (_count < AppendLimit)
            {
                index = _count;
                _count++;
            }
            else
            {
                // 0x6863AD `and ebx,0x8000003F` plus the 0x6863B5..0x6863B9 fixup is
                // Delphi's signed mod 64: keep the sign bit, mask the low 6, and for
                // a negative value round toward zero. _ringCursor only ever grows
                // from 0 here, so the fixup is unreachable in practice; the mask is
                // reproduced rather than replaced by a plain % so the wrap point
                // matches even if the cursor is ever seeded differently.
                index = _ringCursor & 0x3F;
                _ringCursor++;
            }

            _x[index] = x;
            _y[index] = y;
            _w[index] = w;
        }

        /// <summary>
        /// <c>sub_6863EC(Self, dx=X, cx=Y)</c> — scan from the newest entry back to
        /// index 0 and return the first match, else <see cref="NotFound"/>.
        /// <code>
        ///   6863EE  edi = -2                       ; the miss value
        ///   6863F3  esi = [Self+0x20C] ; dec esi   ; start at count-1
        ///   6863FA  cmp esi,0 / jl done            ; empty list falls straight out
        ///   6863FF  cmp dx, word[Self+esi*8+4] / jne next
        ///   686406  cmp cx, word[Self+esi*8+6] / jne next
        ///   68640D  edi = esi ; jmp done
        ///   686411  next: dec esi ; cmp esi,-1 / jne loop
        /// </code>
        /// The <c>jl</c> at 0x6863FD is signed, and the loop re-tests against -1
        /// after decrementing, so index 0 IS examined — the walk covers the whole
        /// populated range and stops one short of wrapping.
        /// </summary>
        internal int IndexOf(ushort x, ushort y)
        {
            for (var i = _count - 1; i >= 0; i--)
            {
                if (_x[i] == x && _y[i] == y)
                {
                    return i;
                }
            }

            return NotFound;
        }

        /// <summary>
        /// <c>sub_6863E0(Self)</c> — <c>xor edx,edx / mov [Self+0x20C],edx / ret</c>.
        /// It resets the count only: the records and the ring cursor at +0x210 are
        /// left as they were, so a cleared list still carries its old cells until
        /// they are overwritten. Reproduced exactly, including not touching
        /// <see cref="_ringCursor"/>.
        /// </summary>
        internal void Clear()
        {
            _count = 0;
        }
    }
}
