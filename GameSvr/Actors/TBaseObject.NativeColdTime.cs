using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using SystemModule;

namespace GameSvr
{
    /// <summary>
    /// The native cooldown ("coldTime") RUNTIME layer — THumanKind's generic
    /// keyed timer table. The persistence half already round-trips through
    /// TPlayObject.NativeScriptSectionsPayload.cs (ScriptData type 7); this file
    /// supplies the arm / query / tick / notify half, which C# never had.
    ///
    /// Container: obj+0x504, a Delphi TList of 12-byte nodes, created in the
    /// THumanKind ctor at 0x73BFF2..0x73BFFC (class ref [0x421E8C] -> VMT
    /// 0x421ED8, className TList, instSize 16). Node layout, each offset read
    /// from its own instruction:
    ///   +0x00 dword Key       write 0x7481A7 `mov [eax],edx`
    ///   +0x04 dword Remaining write 0x7481AF, decremented 0x748246 `sub [eax+4],edi`
    ///   +0x08 dword Total     write 0x7481C6 / 0x7481D1, read 0x748321
    /// Allocated as exactly 12 bytes (0x748186 `mov eax,0xC` -> 0x74818B GetMem)
    /// and freed as 12 (0x748262 / 0x73C0E7).
    ///
    /// The four native entry points:
    ///   ARM    sub_748130  THumanKind VMT+0x1F0  eax=Self edx=Key ecx=Remaining,
    ///                      Total on the stack ([ebp+8]), `ret 4` at 0x7481EE
    ///   QUERY  sub_748288  THumanKind VMT+0x1F4  eax=Self edx=Key -> Remaining
    ///   TOTAL  sub_7482E0  plain method (0 VMT slots), 1 caller 0x6CEC7D
    ///   TICK   sub_748200  sole caller 0x73C245 inside THumanKind.Run
    ///   BULK   sub_748338  plain method, sets every Remaining to 1
    ///   NOTIFY sub_74839C  called from ARM 0x7481E3 and EXPIRY 0x74825D
    ///
    /// The key is a caller-chosen Word living in the MAGIC-ID space, NOT a dense
    /// slot and NOT the magic-list index. Proof: the arm site at 0x7443E5 takes
    /// it straight out of a magic record via sub_4C853C (`mov ax,[eax+0x10]`)
    /// with no index arithmetic, while sub_741628 exists purely to resolve an id
    /// to a list POSITION by comparing that same word (0x741665/0x741669) — so id
    /// and position are demonstrably different quantities. Most arm sites pass a
    /// literal (0x111, 0x98, 0x74..0x7F, 0x10A ...), so this must stay a sparse
    /// keyed table, never a [skillId] array.
    /// </summary>
    public partial class TBaseObject
    {
        /// <summary>Tick gate, `cmp edi,0xFA` at 0x748216 followed by `jle`, so
        /// the table is only serviced when elapsed is STRICTLY greater than 250.
        /// </summary>
        internal const int NativeColdTimeTickIntervalMilliseconds = 0xFA;

        /// <summary>Notify ident when obj+0x178 == 0, i.e. a real player
        /// (0x7483C6 `mov [ebp-0xC],0xDE4`). The player ctor zeroes that byte at
        /// 0x6AD76F; every monster/hero ctor stamps a nonzero kind code
        /// (0x36 at 0x6864E3, 0x50 at 0x666162, 0x62 at 0x68132A ...).</summary>
        internal const int NativeColdTimePlayerIdent = 0xDE4;

        /// <summary>Notify ident for anything with a nonzero obj+0x178
        /// (0x7483CF `mov [ebp-0xC],0x110F`).</summary>
        internal const int NativeColdTimeNonPlayerIdent = 0x110F;

        /// <summary>Wire element width: the bulk form indexes with
        /// `[edx+esi*8]` / `[edx+esi*8+4]` (0x74843A / 0x748446) and derives the
        /// byte length with `shl eax,3` (0x74845A), and the single form pushes a
        /// literal 8 (0x748488).</summary>
        internal const int NativeColdTimeWireElementSize = 8;

        /// <summary>obj+0x504, the ONE table. Native has a single TList serving
        /// both the runtime (arm/query/tick) and the ScriptData type-7 codec, so
        /// C# must not keep a separate persistence mirror -- a second store is
        /// exactly the dual-source-of-truth drift this port has had to fix
        /// elsewhere. The codec in TPlayObject.NativeScriptSectionsPayload.cs
        /// reads and writes this list directly.
        ///
        /// A List of reference-type entries, so ARM can append (TList.Add,
        /// sub_424AB8: `mov [FList+FCount*4],item` then `inc [list+8]`), TICK can
        /// delete by position (TList.Delete, sub_424B30), and the tick can
        /// decrement Remaining in place the way `sub [eax+4],edi` does.</summary>
        public List<NativeColdTimeEntry> m_NativeColdTimes
            = new List<NativeColdTimeEntry>();

        /// <summary>obj+0x484, latched at 0x74821E. NOTE this offset is shared:
        /// on monsters the same field is a different subsystem's last-tick latch
        /// (0x71AF49, 0x71BB04, ...). Only THumanKind's cooldown tick owns it, so
        /// C# gives the cooldown table its own field rather than aliasing a
        /// general-purpose one.</summary>
        private int m_dwNativeColdTimeTick;

        /// <summary>The 12-byte node. A class, not a struct: the tick mutates
        /// Remaining in place (0x748246) and ARM mutates a found node's fields
        /// (0x7481A7/0x7481AF/0x7481C6) without moving it in the list.</summary>
        public sealed class NativeColdTimeEntry
        {
            /// <summary>elem+0x00, matched by == at 0x74816C in sub_748130.</summary>
            public uint Key;

            /// <summary>elem+0x04, decremented by elapsed ms at 0x748246.</summary>
            public int Remaining;

            /// <summary>elem+0x08, the duration the client was told (0x7481C6).</summary>
            public int Total;
        }

        /// <summary>Does this actor own a cooldown table? Native puts it on
        /// THumanKind, so it exists on TPlayer, TGdMsgGMAgent, THeroAct and the
        /// six hero leaves — but NOT on TCreature, whose VMT+0x1F0 holds a
        /// different function (sub_773CA0 at 0x764608+0x1F0). Monsters therefore
        /// have no table at all.</summary>
        internal virtual bool SupportsNativeColdTime => false;

        /// <summary>THumanKind VMT+0x1F0 = sub_748130. Upsert by key: the scan at
        /// 0x748146..0x748178 breaks out on the FIRST match (`je 0x74817A` at
        /// 0x74816F) and clears the found pointer on mismatch (0x748171), so a
        /// reused node keeps its position. Returns whether a notification was
        /// emitted, mirroring the `je 0x7481E8` short-circuit.</summary>
        internal bool ArmNativeColdTime(uint key, int remaining, int total)
        {
            if (!SupportsNativeColdTime)
            {
                return false;
            }

            var node = FindNativeColdTimeEntry(key);
            if (node == null)
            {
                // 0x748180 `cmp [ebp-8],0` / 0x748184 `je 0x7481E8`: arming an
                // ABSENT key with Remaining == 0 allocates nothing and — the part
                // a naive port gets wrong — sends NO notification at all.
                if (remaining == 0)
                {
                    return false;
                }
                node = new NativeColdTimeEntry();
                m_NativeColdTimes.Add(node);
            }

            node.Key = key;
            node.Remaining = remaining;
            if (total != 0)
            {
                // 0x7481CB path.
                node.Total = total;
            }
            else if (remaining > total)
            {
                // 0x7481B8..0x7481BE compares Remaining against [ebp+8], which is
                // 0 on this path, so the guard is effectively `remaining > 0`.
                // Otherwise Total keeps whatever it had: 0 on a fresh node, the
                // previous duration on a reused one.
                node.Total = remaining;
            }

            // 0x7481D4..0x7481E3 push the node's CURRENT Total (not the argument)
            // and notify with the live Remaining.
            NotifyNativeColdTime(key, remaining, node.Total);
            return true;
        }

        /// <summary>THumanKind VMT+0x1F4 = sub_748288. First match wins
        /// (0x7482CF `jmp out`); absent keys return 0 (0x748296 `xor eax,eax`).
        /// Callers test `test eax,eax / jle` (0x7443D0, 0x6EF698), so a negative
        /// remaining reads as expired.</summary>
        internal int QueryNativeColdTime(uint key)
            => FindNativeColdTimeEntry(key)?.Remaining ?? 0;

        /// <summary>sub_7482E0 — byte-identical to the query except the final
        /// field read is `mov eax,[eax+8]` at 0x748321. Not a virtual method: 0
        /// VMT slots, sole caller 0x6CEC7D.</summary>
        internal int QueryNativeColdTimeTotal(uint key)
            => FindNativeColdTimeEntry(key)?.Total ?? 0;

        /// <summary>sub_748338 — sets EVERY Remaining to 1 (0x748365
        /// `mov dword [eax+4],1`) so the whole table expires on the next tick and
        /// each entry still emits its normal expiry notification. Plain method, 0
        /// VMT slots, sole caller 0x6297E1 in the GM dispatcher.</summary>
        internal void ExpireAllNativeColdTimes()
        {
            if (!SupportsNativeColdTime)
            {
                return;
            }
            foreach (var node in m_NativeColdTimes)
            {
                node.Remaining = 1;
            }
        }

        /// <summary>sub_748200, the tick. Called first thing in THumanKind.Run
        /// (0x73C245), ahead of the death check at 0x73C24C.</summary>
        internal void ProcessNativeColdTimes(int now)
        {
            if (!SupportsNativeColdTime)
            {
                return;
            }

            // 0x748210 `sub edi,[esi+0x484]` then 0x748216 `cmp edi,0xFA` /
            // 0x74821C `jle`. Signed subtraction of raw GetTickCount values, so
            // wraparound is reproduced by `unchecked`.
            var elapsed = unchecked(now - m_dwNativeColdTimeTick);
            if (elapsed <= NativeColdTimeTickIntervalMilliseconds)
            {
                return;
            }
            m_dwNativeColdTimeTick = now;

            // 0x74822D..0x748280 walks BACKWARDS from FCount-1 so deletion is
            // safe, and it subtracts the MEASURED elapsed (edi), not the 250
            // constant — cooldowns are wall-clock accurate and a long stall
            // subtracts the whole stall.
            for (var index = m_NativeColdTimes.Count - 1; index >= 0; index--)
            {
                var node = m_NativeColdTimes[index];
                node.Remaining = unchecked(node.Remaining - elapsed);
                // 0x748250 `jg` keeps only strictly positive: expiry is <= 0.
                if (node.Remaining > 0)
                {
                    continue;
                }
                // 0x748252..0x74825D notify(key, 0, 0) — a DIRECT call to
                // sub_74839C, not a virtual dispatch, so no descendant can
                // intercept expiry. Then FreeMem(12) and TList.Delete.
                NotifyNativeColdTime(node.Key, 0, 0);
                m_NativeColdTimes.RemoveAt(index);
            }
        }

        private NativeColdTimeEntry FindNativeColdTimeEntry(uint key)
        {
            foreach (var node in m_NativeColdTimes)
            {
                if (node.Key == key)
                {
                    return node;
                }
            }
            return null;
        }

        /// <summary>Login-cluster bulk leg: sub_74839C(Self, 0, 0, 0).
        /// An empty table is intentionally silent.</summary>
        internal void SendNativeColdTimeListState()
        {
            if (SupportsNativeColdTime)
            {
                NotifyNativeColdTime(0, 0, 0);
            }
        }

        /// <summary>sub_74839C. The ident is selected by obj+0x178 at 0x7483BD,
        /// and `test edx,edx / jne 0x748476` at 0x7483D6 picks the payload shape:
        /// key 0 sends the WHOLE table, any other key sends that one entry.
        ///
        /// Total is the fourth (stack) argument and is deliberately NOT part of
        /// either payload — a port that sends it diverges. Both shapes carry
        /// {Key, Remaining} only.</summary>
        private void NotifyNativeColdTime(uint key, int remaining, int total)
        {
            // The stack argument exists in the native signature and is read back
            // for the arm-path push at 0x7481D7, but sub_74839C never places it
            // on the wire. Kept in the C# signature so the call shape matches.
            _ = total;

            var ident = m_btRaceServer == 0
                ? NativeColdTimePlayerIdent
                : NativeColdTimeNonPlayerIdent;

            if (key == 0)
            {
                // 0x7483DE..0x748474 bulk form. The count is pushed as a WORD
                // (0x74844C `mov ax,word [ebp-8]`) while the buffer length uses
                // the full dword via `shl eax,3`, so a >65535-entry table
                // truncates the header but not the body. Reproduced verbatim.
                var count = m_NativeColdTimes.Count;
                // 0x7483EA `cmp [ebp-8],0` / `jle 0x74849B`: an empty table sends
                // nothing at all.
                if (count <= 0)
                {
                    return;
                }
                // The send at 0x748468 is INSIDE the fill loop: the back edge is
                // 0x748472 `jne 0x748422` and the send precedes the `inc esi` at
                // 0x74846E. So native emits ONE PACKET PER ELEMENT, each carrying
                // the full count*8 length but only elements 0..i populated --
                // SetLength (0x748406) zero-fills, so the trailing slots are
                // zeroes until a later iteration fills them. Only the last packet
                // is complete. A port that sends the finished buffer once, or N
                // times, does not match the wire.
                var bulk = new byte[count * NativeColdTimeWireElementSize];
                for (var index = 0; index < count; index++)
                {
                    var node = m_NativeColdTimes[index];
                    var element = index * NativeColdTimeWireElementSize;
                    BinaryPrimitives.WriteUInt32LittleEndian(
                        bulk.AsSpan(element, sizeof(uint)), node.Key);
                    BinaryPrimitives.WriteInt32LittleEndian(
                        bulk.AsSpan(element + 4, sizeof(int)), node.Remaining);
                    // ecx is zeroed for the bulk form (0x74845E `xor ecx,ecx`),
                    // and the key slot carries 0 (0x748451 `push 0`).
                    SendNativeColdTimePacket(BuildNativeColdTimePacket(
                        ident, 0, (ushort)count, 0, (byte[])bulk.Clone()));
                }
                return;
            }

            // 0x748476..0x748495 single form: an 8-byte {Key, Remaining} record,
            // count 1, and `push edx` at 0x74847F puts the KEY in the same
            // argument slot the bulk form fills with 0.
            var single = new byte[NativeColdTimeWireElementSize];
            BinaryPrimitives.WriteUInt32LittleEndian(
                single.AsSpan(0, sizeof(uint)), key);
            BinaryPrimitives.WriteInt32LittleEndian(
                single.AsSpan(4, sizeof(int)), remaining);
            SendNativeColdTimePacket(BuildNativeColdTimePacket(
                ident, unchecked((int)key), 1, remaining, single));
        }

        /// <summary>Pure packet builder, split out so an audit can assert the
        /// wire shape without a socket. sub_6D7BF8 builds a 0x1C-byte envelope
        /// zero-filled at 0x6D7C3F and then fills it; mapping the 12-byte
        /// ClientPacket onto the trailing part of that envelope, from the stores
        /// themselves:
        ///   [ebp-0x14] := ecx          0x6D7C67  -> Recog
        ///   [ebp-0x10] := dx (Ident)   0x6D7C71  -> Ident
        ///   [ebp-0x0E] := arg@+0x18    0x6D7C79  -> Param
        ///   [ebp-0x0C] := arg@+0x14    0x6D7C81  -> Tag
        ///   [ebp-0x0A] := arg@+0x10    0x6D7C89  -> Series
        /// and the notify sites push, in program order,
        ///   0x74847F key / 0x748480 count / 0x748482 zero / 0x748487 buffer /
        ///   0x748488 length.
        /// Pushes walk DOWN in address and the `call` puts the return address
        /// below them all, so the LAST push is the one at [ebp+8]. Reading the
        /// slots back: +8 length, +0xC buffer, +0x10 zero, +0x14 count, +0x18
        /// key. Hence Param = keySlot, Tag = count, Series = 0, Recog = ecx.
        /// </summary>
        internal static (ClientPacket Header, byte[] Body)
            BuildNativeColdTimePacket(int ident, int keySlot, ushort count,
                int recog, byte[] body)
            => (Grobal2.MakeDefaultMsg(ident, recog, keySlot, count, 0), body);

        /// <summary>The transport is VMT+0x254, and it differs by class:
        ///   TPlayer / TGdMsgGMAgent -> sub_6D7BF8, the real 77BBAA33 sender
        ///   THeroAct and all six hero leaves -> sub_689A38, which FORWARDS to
        ///     the master's own +0x254 via obj+0x68C (0x689A44, master pointer
        ///     stamped at 0x65229C)
        ///   THumanKind itself -> sub_73C968, a bare `ret 0x14` stub
        /// sub_6D7BF8 gates on `[Self+0x73] == 0` (m_boGhost, 0x6D7C0D) and
        /// `[Self+0x460] > 0` (0x6D7C17); sub_689A38 gates on the MASTER's ghost
        /// flag (0x689A4E) after a null check.</summary>
        private void SendNativeColdTimePacket(
            (ClientPacket Header, byte[] Body) packet)
        {
            var recipient = ResolveNativeColdTimeRecipient();
            if (recipient == null || recipient.m_boGhost)
            {
                return;
            }
            // Logged on the RECIPIENT, not on the sender: for a hero the packet
            // leaves through the master's socket (sub_689A38 forwards to
            // obj+0x68C), so the master is where an observer must look.
            recipient.m_NativeColdTimePacketLog?.Add(packet);
            recipient.SendSocket(packet.Header, packet.Body);
        }

        /// <summary>Audit hook. Null in production, so the only cost on the hot
        /// path is one null check. An audit installs a list here to observe the
        /// exact packets the notifier emits, including the per-element bulk
        /// sequence, without standing up a socket.</summary>
        internal List<(ClientPacket Header, byte[] Body)>
            m_NativeColdTimePacketLog;

        /// <summary>Simplified wrapper for GetNativeColdTimeRemaining - returns
        /// remaining milliseconds for a given key, or 0 if not found/expired.</summary>
        internal int GetNativeColdTimeRemaining(int key)
        {
            return QueryNativeColdTime(unchecked((uint)key));
        }

        /// <summary>Simplified wrapper for SetNativeColdTime - arms a cooldown
        /// with the given key, duration, and current timestamp.</summary>
        internal void SetNativeColdTime(int key, int durationMs, int now)
        {
            ArmNativeColdTime(unchecked((uint)key), durationMs, durationMs);
        }

        /// <summary>Heroes have no socket of their own: sub_689A38 forwards to
        /// obj+0x68C, the master player. THumanKind's own slot is a stub, which
        /// in C# means any actor that is neither a player nor a mastered hero
        /// simply drops the packet.</summary>
        private TPlayObject ResolveNativeColdTimeRecipient()
        {
            if (this is TPlayObject player)
            {
                return player;
            }
            if (this is HeroObject hero)
            {
                return hero.m_Master as TPlayObject;
            }
            return null;
        }
    }
}
