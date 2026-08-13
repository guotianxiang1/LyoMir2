using SystemModule;

namespace GameSvr
{
    /// <summary>
    /// Native <c>TCakeFireEvent</c>, self-pointer 0x716E38 / VMT 0x716E84,
    /// instance size 72 — the same size as TMapEvent, because it adds no fields.
    /// Its VMT is a copy of TMapEvent's (+0x08 0x717420, +0x10 0x717448,
    /// Destroy 0x7173E0), so it overrides nothing: no ApplyTo, no Run, no
    /// destructor. The entire class is the constructor <c>sub_718004</c>
    /// (<c>ret 0x10</c>) and the type byte it stamps,
    /// <c>0x718025 6A 08 push 8</c>.
    /// <para>
    /// The engine-wide event factory <c>sub_7189DC</c> reaches it through the
    /// second arm of its cumulative subtract chain —
    /// <c>0x718A27 sub dl,5</c>, <c>0x718A2C sub dl,3</c>, so <c>cl == 8</c> —
    /// and passes 0 for the fourth constructor parameter (<c>0x718A5C 6A 00</c>).
    /// </para>
    /// </summary>
    public class CakeFireEvent : Event
    {
        /// <summary>
        /// Native ctor <c>sub_718004</c>: ecx = Envir (passed straight through to
        /// TMapEvent.Create), [ebp+0x14] X, [ebp+0x10] Y, [ebp+0x0C] duration,
        /// [ebp+8] the fourth flag.
        /// <para>
        /// That fourth flag is NOT the visible flag. The pushes at
        /// 0x718025-0x71802E are <c>8, 1, 1, al</c>, and TMapEvent.Create reads
        /// them as type / visible / b7 / b8 — so visible is a hard 1 and the
        /// parameter lands on b8, the flag that at <c>0x717374</c> selects
        /// AddToMap against the secondary map object <c>Envir.[+0x1C]</c>
        /// instead of the Envirnoment itself. C#'s Envirnoment has no secondary
        /// map, every native call site passes 0, and the factory arm above passes
        /// 0 too, so the parameter is accepted and asserted rather than routed.
        /// </para>
        /// </summary>
        public CakeFireEvent(Envirnoment envir, int nX, int nY, int nTime,
            bool boUseSecondaryMap = false)
            : base(envir, nX, nY, Grobal2.ET_CAKEFIRE, nTime, true)
        {
            if (boUseSecondaryMap)
            {
                throw new NotSupportedException(
                    "TMapEvent.Create arm A (AddToMap on Envir.[+0x1C], " +
                    "@0x71737A) has no C# counterpart; every native call site " +
                    "passes 0 for this flag.");
            }
        }
    }
}
