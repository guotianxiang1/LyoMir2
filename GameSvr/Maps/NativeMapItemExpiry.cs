namespace GameSvr
{
    /// <summary>
    /// 战神 <c>sub_77A178</c> — the per-cell cleanup sweep.  It dispatches on the cell
    /// node's own type tag, <c>byte [node+0x00]</c>, through a <c>dec al</c> ladder:
    ///
    /// <code>
    /// 77A2B9  8B 06                   mov eax,[esi]
    /// 77A2BB  8A 00                   mov al,byte [eax]
    /// 77A2BD  FE C8                   dec al / 74 15          je 0x77A2D6   ; tag 1
    /// 77A2C1  FE C8                   dec al / 0F 84 ...      je 0x77A3D9   ; tag 2
    /// 77A2C9  FE C8                   dec al / 0F 84 ...      je 0x77A480   ; tag 3
    /// 77A2D1  E9 84 03 00 00          jmp 0x77A65A
    /// </code>
    ///
    /// The tag is copied off the hooked object at link time — <c>0x777DCF 8A 46 04
    /// mov al,byte [esi+4]</c> then <c>0x777DD2 88 03 mov byte [ebx],al</c> — and the two
    /// constructors nail it down:
    ///
    /// <code>
    /// sub_783788 (ground item)  0x7837AA  C6 43 04 02  mov byte [ebx+4],2
    /// sub_717300 (event object) 0x717322  C6 43 04 03  mov byte [ebx+4],3
    /// </code>
    ///
    /// So 战神 numbers them MOVING=1 / ITEM=2 / EVENT=3, while C#'s
    /// <see cref="CellType"/> is EVENT=1 / MOVING=2 / ITEM=3.  Reading the native ladder
    /// with the C# numbering swaps items and events, and that is exactly what happened
    /// here: the whole tag-3 branch (900 000 ms, the StdMode 0x25/0x29 sub-ladder, the
    /// <c>+0x0D</c> never-expire gate and the per-object lifetime override at
    /// <c>+0x20</c>) was applied to ground items, which have none of it.
    ///
    /// The real ground-item branch is tag 2 @0x77A3D9 and it is four instructions long:
    ///
    /// <code>
    /// 77A3EB  8B 06                   mov eax,[esi]
    /// 77A3ED  8B 40 08                mov eax,[eax+8]         ; node.dropTick
    /// 77A3F0  3B 45 08                cmp eax,[ebp+8]         ; vs now
    /// 77A3F3  73 45                   jae 0x77A43A            ; clock went backwards: keep
    /// 77A3F5  8B 06                   mov eax,[esi]
    /// 77A3F7  8B 55 08                mov edx,[ebp+8]
    /// 77A3FA  2B 50 08                sub edx,[eax+8]         ; age
    /// 77A3FD  81 FA C0 27 09 00       cmp edx,0x927C0         ; 600_000 ms = 10 min
    /// 77A403  72 35                   jb  0x77A43A            ; below limit: keep
    /// 77A405  ...                     unlink, Free the object @0x77A422, Free the node
    /// </code>
    ///
    /// No StdMode test, no expirable byte, no per-object override — one flat 10-minute
    /// life.  <c>jb</c> keeps while strictly below, so expiry is <c>age &gt;= limit</c>.
    /// </summary>
    internal static class NativeMapItemExpiry
    {
        /// <summary>
        /// <c>0x927C0</c> = 600_000 ms (10 minutes), the literal at @0x77A3FD in the
        /// tag-2 (ground item) branch.  There is no config knob for it: the stock-Mir2
        /// key names ("ClearDropOnFloorItemTime" and the rest) are 0-hit across the whole
        /// image in GBK, bare ASCII and UTF-16LE.
        /// </summary>
        internal const int GroundItemLifetimeMs = 0x927C0;

        /// <summary>
        /// The tag-2 expiry predicate.  Native compares with <c>jb</c> (keep while below),
        /// so the item is cleared once <c>age &gt;= limit</c>.
        /// </summary>
        internal static bool HasGroundItemExpired(int ageMs, int lifetimeMs)
        {
            return ageMs >= lifetimeMs;
        }

        // ------------------------------------------------------------------------
        // Tag 3 (event object, sub_717300) @0x77A480.  Kept here as the record of what
        // the 900_000 ladder actually belongs to, so the same mis-mapping is not made
        // again.  Nothing in C# consumes it yet: the C# cell model has no event-object
        // cleanup sweep, so this is a MISSING contract, not a live path.  Do NOT wire it
        // to CellType.OS_ITEMOBJECT — that enum value is 3 in C# but ground items are
        // tag 2 in 战神.
        //
        //   77A495  cmp byte [obj+0x0C],0x25 / jne 0x77A53B    ; StdMode 37
        //     77A4A2  cmp byte [obj+0x0D],0 / je -> keep forever
        //     77A4B2  call sub_719BF0(edx=self) -> false = keep
        //     77A4C7  cmp edx,0xDBBA0                          ; 900_000 ms
        //   77A53E  cmp byte [obj+0x0C],0x29 / jne 0x77A5D1    ; StdMode 41
        //     77A54B  cmp byte [obj+0x0D],0 / je -> keep forever
        //     77A560  cmp edx,dword [obj+0x20]                 ; per-object lifetime
        //   77A5D1  (every other StdMode)
        //     77A5D4  cmp byte [obj+0x0D],0 / je -> keep forever
        //     77A5E6  cmp edx,0xDBBA0
        //
        // On expiry the tag-3 branch frees only the 16-byte node, never the object —
        // another difference from tag 2, which frees both (0x77A422 then 0x77A42E).
        // ------------------------------------------------------------------------

        /// <summary><c>0xDBBA0</c> = 900_000 ms @0x77A4C7 and @0x77A5E6 — EVENT objects.</summary>
        internal const int EventObjectLifetimeMs = 0xDBBA0;

        /// <summary><c>cmp byte [obj+0x0C],0x25</c> @0x77A495 — the owned/timed class.</summary>
        internal const byte EventOwnershipStdMode = 0x25;

        /// <summary><c>cmp byte [obj+0x0C],0x29</c> @0x77A53E — the per-object lifetime class.</summary>
        internal const byte EventPerItemLifetimeStdMode = 0x29;
    }
}
