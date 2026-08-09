namespace GameSvr
{
    /// <summary>
    /// 战神 <c>sub_77A178</c> — the per-cell ground-item cleanup ladder.  The map-region
    /// routine dispatches on cell type at @0x77A2B9 (<c>dec al</c> chain; type 3 =
    /// OS_ITEMOBJECT → @0x77A480) and then runs a three-way ladder on the map item's
    /// StdMode byte at <c>+0x0C</c>, with a mandatory <c>+0x0D</c> "expirable" gate on
    /// every branch:
    ///
    /// <code>
    /// 77A495  cmp byte [item+0x0C],0x25 / jne 0x77A53B      ; StdMode 37
    ///   77A4A2  cmp byte [item+0x0D],0 / je   -> skip (NEVER EXPIRES)
    ///   77A4B2  call sub_719BF0(edx=self)    -> false = skip (extra ownership test)
    ///   77A4C7  cmp edx,0xDBBA0              ; 900_000 ms = 15 min
    /// 77A53E  cmp byte [item+0x0C],0x29 / jne 0x77A5D1      ; StdMode 41
    ///   77A54B  cmp byte [item+0x0D],0 / je   -> skip
    ///   77A560  cmp edx,dword [item+0x20]    ; PER-ITEM stored lifetime
    /// 77A5D1  (all other StdModes)
    ///   77A5D4  cmp byte [item+0x0D],0 / je   -> skip
    ///   77A5E6  cmp edx,0xDBBA0             ; 900_000 ms = 15 min
    /// </code>
    ///
    /// In every branch the age is <c>[ebp+8] - dword [cell+8]</c> (now minus the cell's
    /// add-tick) and the comparison is <c>jb</c> (below ⇒ keep), so expiry happens at
    /// <c>age &gt;= limit</c>.  On expiry all three unlink the cell node and
    /// <c>call sub_402FD0(edx=0x10)</c> to free the 16-byte entry.
    ///
    /// The C# side used one flat <c>dwClearDropOnFloorItemTime</c> (default 3_600_000 =
    /// 1 h) with no StdMode ladder and no never-expire gate, so (a) permanent ground items
    /// were destroyed after an hour and (b) items native clears at 15 min lingered for an
    /// hour and stayed pickable long past their native lifetime.
    /// </summary>
    internal static class NativeMapItemExpiry
    {
        /// <summary>
        /// <c>0xDBBA0</c> = 900_000 ms (15 minutes), hardcoded at BOTH @0x77A4C7 and
        /// @0x77A5E6 — this is a literal in the image, not a config value.
        /// </summary>
        internal const int DefaultLifetimeMs = 0xDBBA0;

        /// <summary><c>cmp byte [item+0x0C],0x25</c> @0x77A495 — the owned/timed class.</summary>
        internal const byte OwnershipStdMode = 0x25;

        /// <summary><c>cmp byte [item+0x0C],0x29</c> @0x77A53E — the per-item lifetime class.</summary>
        internal const byte PerItemLifetimeStdMode = 0x29;

        /// <summary>
        /// Resolves the age limit in ms for a ground item, or returns false when the item
        /// must NEVER expire (the <c>+0x0D == 0</c> class).
        /// </summary>
        internal static bool TryResolveLifetimeMs(MapItem mapItem, byte stdMode,
            out int lifetimeMs)
        {
            lifetimeMs = DefaultLifetimeMs;
            if (mapItem == null) return false;

            // 0x77A4A2 / 0x77A54B / 0x77A5D4 — the gate is on every branch, so one test
            // up front is equivalent.
            if (mapItem.NativeExpirable == 0) return false;

            // 0x77A560: only StdMode 41 substitutes the per-item dword.
            if (stdMode == PerItemLifetimeStdMode && mapItem.NativeLifetimeMs > 0)
            {
                lifetimeMs = mapItem.NativeLifetimeMs;
            }
            return true;
        }

        /// <summary>
        /// The expiry predicate: native compares with <c>jb</c> (keep while below), so the
        /// item is cleared once <c>age &gt;= limit</c>.
        /// </summary>
        internal static bool HasExpired(MapItem mapItem, byte stdMode, int ageMs)
        {
            if (!TryResolveLifetimeMs(mapItem, stdMode, out var lifetimeMs))
                return false;
            return ageMs >= lifetimeMs;
        }
    }
}
