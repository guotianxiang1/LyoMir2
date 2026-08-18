using System;
using System.Collections.Generic;

namespace GameSvr.Services
{
    // ---------------------------------------------------------------------------
    // Dormant 1:1 model of the native M2Server GM "deny / ban" persistent list ops.
    //
    // Source of truth: M2Server_unpacked_fixed.exe (战神 / "God of War" server),
    //   IDA db m2full.i64, image base 0x400000,
    //   SHA256 5540f43bc58d8d67673927c4186941e253403bb7d3a2a0b40ebfcf049670b14e.
    //
    // The only persistent block-list store present in the native binary is the file
    // "BlockUsers.Dat". It backs the mute family the C# port exposes as
    //   @DisableSendMsg / @EnableSendMsg / @DisableSendMsgList
    // (and the parallel @ShutUp / @ReleaseShutUp / @ShutUpList commands, which share
    // the SAME singleton manager object and the SAME file). It is a *timed* block:
    // each entry carries a remaining-seconds counter that a periodic sweep decrements,
    // auto-removing the entry (and clearing the player's flag) when it reaches <= 0.
    //
    // Native function map (all addresses are file VA, image base 0x400000):
    //   sub_6222D0  Load(mgr)              read BlockUsers.Dat -> in-memory list
    //   sub_621B14  Add(mgr, who, secs)    create-or-extend entry, set player flag, save
    //   sub_621CE4  Delete(mgr, who)       remove entry, clear player flag, save
    //   sub_622040  Tick(mgr, nowMs)       decrement remaining, expire <=0, save if changed
    //   sub_6221CC  Tick(mgr, nowMs)       same sweep over the secondary list (flag+2970)
    //   sub_621E44  TickGate(mgr, nowMs)   runs the two sweeps only every > 10000 ms
    //   sub_622630  Save(mgr)              write list1 -> file, or delete file if empty
    //   sub_622728  Save(mgr)              write the secondary list -> same file
    //   sub_621C4C  ctor/Load-all          builds the 511-bucket hash, then Load(mgr)
    //   off_7D7104  -> the singleton manager instance
    //   sub_657110  ProcessOthGsMsg cases 209/210 -> cross-server Add/Delete replication
    //
    // On-disk record (native Delphi packed record, exactly 20 bytes):
    //     TBlockUser = packed record
    //       sName : string[15];   // ShortString: 1 length byte + 15 char bytes = 16 bytes
    //       nValue: Integer;      // 4 bytes, little-endian = remaining seconds
    //     end;                    // SizeOf = 20
    // Load guard: the file size MUST be an exact multiple of 20; otherwise the native
    // loader reads nothing and the list stays empty (`size >= 0 && (size % 20) == 0`).
    //
    // "Hit" effect: Add/Delete/expiry locate the *online* player by name and set/clear a
    // boolean on the player object (primary list -> +0xB99 / 2969; secondary -> +0xB9A /
    // 2970). The C# port's equivalents are TPlayObject.m_boFilterSendMsg / m_boShutup;
    // chat is gated on it ("禁止聊天!!!"). The name field only carries a character name,
    // so this store is NOT the IP/account login-deny store (see note at bottom).
    //
    // This type is DORMANT: it is self-contained, references no live GameSvr state, and
    // is not wired into any command handler. It exists so the exact native contract is
    // captured and asserted (see AuditTools/NativeGmDenyListCommandsCheck) before the
    // real @DisableSendMsg / @EnableSendMsg / @DisableSendMsgList handlers are revived.
    // ---------------------------------------------------------------------------

    /// <summary>
    /// Persistent backing store for BlockUsers.Dat, abstracted so the list logic can be
    /// exercised without a filesystem. <see cref="Load"/> returns null/empty when the file
    /// is absent; <see cref="Delete"/> removes the file (used when the list drains empty).
    /// </summary>
    public interface INativeBlockUserStore
    {
        byte[] Load();
        void Save(byte[] data);
        void Delete();
    }

    /// <summary>
    /// File-backed BlockUsers.Dat store used by the live GameSvr path.  The native
    /// manager addresses the file relative to the Envir directory; keeping that
    /// resolution outside the list itself also makes the exact codec testable with
    /// an in-memory store and prevents accidental writes to a publish tree.
    /// </summary>
    public sealed class NativeBlockUserFileStore : INativeBlockUserStore
    {
        public NativeBlockUserFileStore(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath))
                throw new ArgumentException("BlockUsers.Dat path is required.",
                    nameof(filePath));
            FilePath = Path.GetFullPath(filePath);
        }

        public string FilePath { get; }

        public byte[] Load()
        {
            return File.Exists(FilePath) ? File.ReadAllBytes(FilePath) : null;
        }

        public void Save(byte[] data)
        {
            var directory = Path.GetDirectoryName(FilePath);
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);
            File.WriteAllBytes(FilePath, data ?? Array.Empty<byte>());
        }

        public void Delete()
        {
            if (File.Exists(FilePath))
                File.Delete(FilePath);
        }
    }

    /// <summary>
    /// Models the native "set/clear boolean flag on the online player" side effect
    /// (player+2969 for the primary list). Optional; a null sink means "player offline".
    /// </summary>
    public interface INativeBlockUserSink
    {
        void SetBlocked(string name, bool blocked);
    }

    /// <summary>One BlockUsers.Dat entry: a name plus a remaining-seconds countdown.</summary>
    public sealed class NativeBlockUserEntry
    {
        public string Name;
        public byte[] NameBytes;
        public int RemainSeconds;
        public long LastTickMs;
    }

    /// <summary>
    /// Exact codec for the 20-byte BlockUsers.Dat record and the whole-file image.
    /// Name bytes are stored/loaded 1:1 (the native ShortString holds the server's ANSI
    /// bytes, i.e. GBK); callers that want text choose an encoding, but the codec itself
    /// is byte-accurate so a round trip never loses data.
    /// </summary>
    public static class NativeBlockUserRecordCodec
    {
        public const int RecordSize = 20;      // SizeOf(TBlockUser)
        public const int NameCapacity = 15;    // string[15] -> at most 15 char bytes
        public const int LengthPrefixSize = 1; // ShortString length byte
        public const int NameFieldSize = 16;   // length byte + 15 char bytes
        public const int ValueOffset = 16;     // Integer starts right after the name field

        /// <summary>True if a raw file image length is a legal BlockUsers.Dat size.</summary>
        public static bool IsValidImageLength(int length)
            => length >= 0 && (length % RecordSize) == 0;

        /// <summary>Record count implied by a valid image length.</summary>
        public static int RecordCount(int length) => length / RecordSize;

        /// <summary>
        /// Encode one record. <paramref name="nameBytes"/> is the raw ANSI name; only the
        /// first <see cref="NameCapacity"/> bytes are kept (native truncates to string[15]).
        /// </summary>
        public static void EncodeRecord(byte[] dst, int offset, byte[] nameBytes, int value)
        {
            if (dst == null) throw new ArgumentNullException(nameof(dst));
            if (offset < 0 || offset + RecordSize > dst.Length)
                throw new ArgumentOutOfRangeException(nameof(offset));

            for (var i = 0; i < RecordSize; i++)
                dst[offset + i] = 0;

            var len = nameBytes == null ? 0 : nameBytes.Length;
            if (len > NameCapacity) len = NameCapacity;
            dst[offset] = (byte)len;                 // ShortString length prefix
            for (var i = 0; i < len; i++)
                dst[offset + LengthPrefixSize + i] = nameBytes[i];

            // Integer, little-endian, at offset+16.
            dst[offset + ValueOffset + 0] = (byte)(value & 0xFF);
            dst[offset + ValueOffset + 1] = (byte)((value >> 8) & 0xFF);
            dst[offset + ValueOffset + 2] = (byte)((value >> 16) & 0xFF);
            dst[offset + ValueOffset + 3] = (byte)((value >> 24) & 0xFF);
        }

        /// <summary>Decode the raw ANSI name bytes of one record (length taken from prefix).</summary>
        public static byte[] DecodeNameBytes(byte[] src, int offset)
        {
            if (src == null) throw new ArgumentNullException(nameof(src));
            if (offset < 0 || offset + RecordSize > src.Length)
                throw new ArgumentOutOfRangeException(nameof(offset));

            int len = src[offset];
            if (len > NameCapacity) len = NameCapacity; // native caps at the field size
            var name = new byte[len];
            Array.Copy(src, offset + LengthPrefixSize, name, 0, len);
            return name;
        }

        /// <summary>Decode the little-endian Integer value of one record.</summary>
        public static int DecodeValue(byte[] src, int offset)
        {
            return src[offset + ValueOffset + 0]
                 | (src[offset + ValueOffset + 1] << 8)
                 | (src[offset + ValueOffset + 2] << 16)
                 | (src[offset + ValueOffset + 3] << 24);
        }
    }

    /// <summary>
    /// 1:1 model of the native BlockUsers.Dat manager list (primary list, player flag +2969).
    /// Names are handled as raw ANSI byte strings via a fixed <see cref="Encoding"/> so the
    /// on-disk bytes round-trip exactly regardless of code page.
    /// </summary>
    public sealed class NativeGmBlockUserList
    {
        // 10_000 ms: native sweep gate (sub_621E44: `if (now - lastSweep) > 0x2710`).
        public const long SweepIntervalMs = 10000;

        private readonly INativeBlockUserStore _store;
        private readonly INativeBlockUserSink _sink;
        private readonly System.Text.Encoding _encoding;

        // Native prepends on both Load and Add, so the newest entry is at the head.
        private readonly LinkedList<NativeBlockUserEntry> _entries = new();

        private long _lastSweepMs;

        public NativeGmBlockUserList(
            INativeBlockUserStore store,
            INativeBlockUserSink sink = null,
            System.Text.Encoding encoding = null)
        {
            _store = store ?? throw new ArgumentNullException(nameof(store));
            _sink = sink;
            // Latin1 maps every byte 0..255 to a char 1:1 -> lossless round trip.
            _encoding = encoding ?? System.Text.Encoding.Latin1;
        }

        public int Count => _entries.Count;

        /// <summary>sub_6222D0: load the file into memory. Invalid sizes load nothing.</summary>
        public void Load(long nowMs)
        {
            _entries.Clear();

            var data = _store.Load();
            if (data == null || data.Length == 0)
                return;
            if (!NativeBlockUserRecordCodec.IsValidImageLength(data.Length))
                return; // native `size % 20 != 0` guard: reads nothing, list stays empty

            var count = NativeBlockUserRecordCodec.RecordCount(data.Length);
            for (var i = 0; i < count; i++)
            {
                var offset = i * NativeBlockUserRecordCodec.RecordSize;
                var nameBytes = NativeBlockUserRecordCodec.DecodeNameBytes(data, offset);
                var value = NativeBlockUserRecordCodec.DecodeValue(data, offset);
                var entry = new NativeBlockUserEntry
                {
                    Name = _encoding.GetString(nameBytes),
                    NameBytes = nameBytes,
                    RemainSeconds = value,
                    LastTickMs = nowMs, // native stamps each node with GetTickCount at load
                };
                Prepend(entry);
            }
        }

        /// <summary>
        /// sub_621B14: create-or-extend. Existing name -> add seconds and return the new
        /// total WITHOUT saving. New name -> create, set the player flag, and save.
        /// Returns the entry's resulting RemainSeconds.
        /// </summary>
        public int Add(string name, int addSeconds, long nowMs)
        {
            var lookupBytes = FoldNameBytes(name);
            var existing = FindFirst(lookupBytes);
            if (existing != null)
            {
                existing.Value.RemainSeconds += addSeconds; // extend; native does NOT re-save
                return existing.Value.RemainSeconds;
            }

            var storedBytes = TruncateNameBytes(lookupBytes);

            var entry = new NativeBlockUserEntry
            {
                Name = _encoding.GetString(storedBytes),
                NameBytes = storedBytes,
                RemainSeconds = addSeconds,
                LastTickMs = nowMs,
            };
            Prepend(entry);
            _sink?.SetBlocked(name, true);
            Save();
            return entry.RemainSeconds;
        }

        /// <summary>sub_621CE4: remove by name, clear the player flag, save. No-op if absent.</summary>
        public bool Delete(string name)
        {
            var lookupBytes = FoldNameBytes(name);
            var removed = false;
            var node = _entries.First;
            while (node != null)
            {
                var next = node.Next;
                if (NameBytesEqual(node.Value.NameBytes, lookupBytes))
                {
                    var storedName = node.Value.Name;
                    Remove(node);
                    _sink?.SetBlocked(storedName, false);
                    Save(); // sub_621CE4 persists after every matching node
                    removed = true;
                }
                node = next;
            }
            return removed;
        }

        /// <summary>
        /// sub_621E44 + sub_622040: gated sweep. Only runs when more than
        /// <see cref="SweepIntervalMs"/> has elapsed since the last sweep. Decrements each
        /// entry by whole elapsed seconds, expires entries at &lt;= 0, saves if any changed.
        /// </summary>
        public void Tick(long nowMs)
        {
            if (ElapsedTick32(nowMs, _lastSweepMs) <= (uint)SweepIntervalMs)
                return;
            _lastSweepMs = nowMs;
            Sweep(nowMs);
        }

        /// <summary>The unconditional sweep body (sub_622040) — exposed for the audit.</summary>
        public bool Sweep(long nowMs)
        {
            var changed = false;
            var node = _entries.First;
            while (node != null)
            {
                var next = node.Next;
                var e = node.Value;
                e.RemainSeconds -= (int)(ElapsedTick32(nowMs, e.LastTickMs) / 1000U);
                e.LastTickMs = nowMs;
                if (e.RemainSeconds <= 0)
                {
                    _sink?.SetBlocked(e.Name, false);
                    Remove(node);
                    changed = true;
                }
                node = next;
            }
            if (changed)
                Save();
            return changed;
        }

        // GetTickCount is a 32-bit unsigned millisecond counter; subtracting the
        // low 32 bits preserves elapsed time when the counter wraps around.
        private static uint ElapsedTick32(long nowMs, long thenMs)
            => unchecked((uint)((int)nowMs - (int)thenMs));

        /// <summary>"Hit": is this name currently in the list (mute active)?</summary>
        public bool Contains(string name) => FindFirst(FoldNameBytes(name)) != null;

        /// <summary>@DisableSendMsgList / @ShutUpList: enumerate current entries (name + remaining).</summary>
        public IReadOnlyList<NativeBlockUserEntry> Snapshot()
        {
            var list = new List<NativeBlockUserEntry>(_entries.Count);
            foreach (var e in _entries)
                list.Add(new NativeBlockUserEntry
                {
                    Name = e.Name,
                    NameBytes = e.NameBytes == null ? null : (byte[])e.NameBytes.Clone(),
                    RemainSeconds = e.RemainSeconds,
                    LastTickMs = e.LastTickMs,
                });
            return list;
        }

        /// <summary>sub_622630: empty list deletes the file; otherwise write every record.</summary>
        public void Save()
        {
            if (_entries.Count <= 0)
            {
                _store.Delete();
                return;
            }

            var data = new byte[_entries.Count * NativeBlockUserRecordCodec.RecordSize];
            var i = 0;
            foreach (var e in _entries)
            {
                var offset = i * NativeBlockUserRecordCodec.RecordSize;
                NativeBlockUserRecordCodec.EncodeRecord(
                    data, offset, e.NameBytes ?? _encoding.GetBytes(e.Name), e.RemainSeconds);
                i++;
            }
            _store.Save(data);
        }

        private void Prepend(NativeBlockUserEntry entry)
        {
            _entries.AddFirst(entry);
        }

        private void Remove(LinkedListNode<NativeBlockUserEntry> node)
        {
            _entries.Remove(node);
        }

        private LinkedListNode<NativeBlockUserEntry> FindFirst(byte[] nameBytes)
        {
            for (var node = _entries.First; node != null; node = node.Next)
            {
                if (NameBytesEqual(node.Value.NameBytes, nameBytes))
                    return node;
            }
            return null;
        }

        private byte[] FoldNameBytes(string name)
        {
            var bytes = _encoding.GetBytes(name ?? string.Empty);
            for (var i = 0; i < bytes.Length; i++)
            {
                if (bytes[i] >= (byte)'A' && bytes[i] <= (byte)'Z')
                    bytes[i] = (byte)(bytes[i] + ('a' - 'A'));
            }
            return bytes;
        }

        private static byte[] TruncateNameBytes(byte[] nameBytes)
        {
            var length = Math.Min(nameBytes.Length, NativeBlockUserRecordCodec.NameCapacity);
            var stored = new byte[length];
            Array.Copy(nameBytes, stored, length);
            return stored;
        }

        private static bool NameBytesEqual(byte[] left, byte[] right)
        {
            if (ReferenceEquals(left, right))
                return true;
            if (left == null || right == null || left.Length != right.Length)
                return false;
            for (var i = 0; i < left.Length; i++)
            {
                if (left[i] != right[i])
                    return false;
            }
            return true;
        }
    }

    /// <summary>
    /// Command-level contract facade. Maps the C# GM command surface onto the native
    /// BlockUsers.Dat list so the add / delete / show / hit semantics are named explicitly:
    ///   @DisableSendMsg  name [seconds]  -> Add     (create-or-extend, save on create)
    ///   @EnableSendMsg   name           -> Delete  (remove, save)
    ///   @DisableSendMsgList             -> Show    (enumerate)
    ///   (chat gate)                     -> Hit     (Contains)
    /// The native command also parses the duration argument with a default (sub_40CA18);
    /// a non-positive duration is ignored by the native @ShutUp handler.
    /// </summary>
    public sealed class NativeGmDenyListCommands
    {
        private readonly NativeGmBlockUserList _list;

        public NativeGmDenyListCommands(NativeGmBlockUserList list)
        {
            _list = list ?? throw new ArgumentNullException(nameof(list));
        }

        /// <summary>Native default duration used when the duration arg fails to parse.</summary>
        public const int DefaultDurationSeconds = 10;

        public int DisableSendMsg(string name, int seconds, long nowMs)
            => _list.Add(name, seconds, nowMs);

        public bool EnableSendMsg(string name)
            => _list.Delete(name);

        public IReadOnlyList<NativeBlockUserEntry> DisableSendMsgList()
            => _list.Snapshot();

        public bool Hit(string name)
            => _list.Contains(name);
    }

    // =========================================================================
    // @DenyIPaddrLogon / @DenyAccountLogon / @DenyCharNameLogon family (login deny).
    //
    // STATUS: CONFIRMED ABSENT from this native binary (m2full.i64). This whole family
    // — the three add commands, their Del*/Show* siblings, the login admission check, the
    // per-list stores, and the rejection messages — was NOT reversed from the EXE; it does
    // not exist in it. The types below (NativeGmDenyLogonList / NativeGmDenyLogonGate)
    // therefore model the C# PORT's placeholder feature, NOT a native contract, and are
    // kept only to document the shape the port committed to. Do not cite them as reversed.
    //
    // Convergent negative evidence (independently reached by two passes):
    //   - GM-dispatch map jpt_622B15 (the authoritative @-command switch table) has NO
    //     deny-logon handler entry and no command-table record for this family.
    //   - Exhaustive raw-image scan over all segments < 0xE00000 found ZERO occurrences of
    //     "Deny" as a command/name prefix and none of "DenyIP"/"DenyAccount"/"DenyChr"/
    //     "IPAddrList"/"AccountList"/"ChrNameList"; no deny-list load/save routine and no
    //     full deny-login rejection message exist. The only related datum is a bare 4-char
    //     label "禁止登录" @0x0078DF14, referenced as a data-table dword (not a login message).
    //   - No deny-list file: the only ".Dat" block file is BlockUsers.Dat (the mute store
    //     above), and the only login-related list file present is WhiteList.txt, reloaded
    //     by @ReloadWhiteList (idx 505, perm 4, handler 0x00629465) — a white-list, with no
    //     add/del GM command (it is file-edited), i.e. not a deny list.
    //
    // Consequently the C# port's M2Share.LoadDenyIPAddrList / GetDenyAccountList / SaveDeny*
    // and TPlayObject.CheckDenyLogon, the three file names (DenyIPAddrList.txt /
    // DenyChrNameList.txt / DenyAccountList.txt) and the g_sYour*DenyLogon messages are all
    // porter-authored placeholders (generic-GOM shape), not artifacts of this EXE. The
    // IP -> Account -> CharName precedence encoded below is taken from that port code, not
    // from the native binary. If a deny-logon feature is ever required, its native storage
    // would have to be sourced from a different M2Server build, not this one.
    // =========================================================================

    /// <summary>
    /// Which deny-logon list matched at login. Evaluation order is the native admission
    /// order: IP first, then account, then character name.
    /// </summary>
    public enum NativeDenyLogonKind
    {
        None = 0,
        IPaddr = 1,
        Account = 2,
        CharName = 3,
    }

    /// <summary>Abstract text store for a deny-logon list (one name per line).</summary>
    public interface INativeStringListStore
    {
        IReadOnlyList<string> Load();
        void Save(IReadOnlyList<string> lines);
    }

    /// <summary>
    /// Dormant model of one deny-logon list (@Deny*Logon / @DelDeny*Logon / @ShowDeny*Logon).
    /// Models the C# PORT's placeholder feature — this family is CONFIRMED ABSENT from the
    /// native binary (see the status block above). Exact-match name set; the port's login
    /// check tests membership only.
    /// </summary>
    public sealed class NativeGmDenyLogonList
    {
        private readonly INativeStringListStore _store;
        private readonly List<string> _names = new();
        private readonly HashSet<string> _index = new(StringComparer.OrdinalIgnoreCase);

        public NativeGmDenyLogonList(INativeStringListStore store)
        {
            _store = store ?? throw new ArgumentNullException(nameof(store));
        }

        public int Count => _names.Count;

        public void Load()
        {
            _names.Clear();
            _index.Clear();
            var lines = _store.Load();
            if (lines == null) return;
            foreach (var raw in lines)
            {
                var name = raw?.Trim();
                if (string.IsNullOrEmpty(name)) continue;
                if (_index.Add(name)) _names.Add(name);
            }
        }

        /// <summary>@Deny*Logon: add a name (de-duplicated); save only when it changed.</summary>
        public bool Add(string name)
        {
            name = name?.Trim();
            if (string.IsNullOrEmpty(name)) return false;
            if (!_index.Add(name)) return false;
            _names.Add(name);
            Save();
            return true;
        }

        /// <summary>@DelDeny*Logon: remove a name; save only when it changed.</summary>
        public bool Delete(string name)
        {
            name = name?.Trim();
            if (string.IsNullOrEmpty(name)) return false;
            if (!_index.Remove(name)) return false;
            _names.RemoveAll(n => string.Equals(n, name, StringComparison.OrdinalIgnoreCase));
            Save();
            return true;
        }

        /// <summary>Login "hit": exact (case-insensitive) membership.</summary>
        public bool Contains(string name)
            => name != null && _index.Contains(name.Trim());

        /// <summary>@ShowDeny*Logon: enumerate.</summary>
        public IReadOnlyList<string> Snapshot() => _names.ToArray();

        public void Save() => _store.Save(_names.ToArray());
    }

    /// <summary>
    /// Login-admission gate across the three deny-logon lists, in the precedence order
    /// (IP, then account, then char name) used by the C# port's TPlayObject.CheckDenyLogon.
    /// A non-None result means that port code force-closes the session
    /// (m_boEmergencyClose = true). This mirrors the PORT's placeholder — the deny-logon
    /// feature is CONFIRMED ABSENT from the native binary (see the status block above).
    /// </summary>
    public sealed class NativeGmDenyLogonGate
    {
        private readonly NativeGmDenyLogonList _ip;
        private readonly NativeGmDenyLogonList _account;
        private readonly NativeGmDenyLogonList _charName;

        public NativeGmDenyLogonGate(
            NativeGmDenyLogonList ip,
            NativeGmDenyLogonList account,
            NativeGmDenyLogonList charName)
        {
            _ip = ip ?? throw new ArgumentNullException(nameof(ip));
            _account = account ?? throw new ArgumentNullException(nameof(account));
            _charName = charName ?? throw new ArgumentNullException(nameof(charName));
        }

        public NativeDenyLogonKind Check(string ipaddr, string account, string charName)
        {
            if (_ip.Contains(ipaddr)) return NativeDenyLogonKind.IPaddr;
            if (_account.Contains(account)) return NativeDenyLogonKind.Account;
            if (_charName.Contains(charName)) return NativeDenyLogonKind.CharName;
            return NativeDenyLogonKind.None;
        }
    }
}
