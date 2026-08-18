using SystemModule;

namespace GameSvr.Services
{
    // =======================================================================
    // 跨服禁言复制 —— OthGs ident 209 / 210。
    // 底本 flat_image.bin (ImageBase=0x400000, file_off = VA - 0x400000)。
    //
    // 两条 ident 都只是把本服的「禁言名单管理器」操作转发给他服:
    //   209 stub @0x65723D  mov ecx,[ebp+0x10] / mov edx,[ebp+0xC] / call 0x6580B8
    //       sub_6580B8: 0x6580D6 call 0x405708(_LStrFromPChar) 取裸角色名;
    //                   0x6580DE mov eax,[0x7D7104] / mov eax,[eax]  (管理器单例)
    //                   0x6580E5 mov ecx,esi        (= 帧头第三个 dword)
    //                   0x6580E7 call 0x621B14      (Add)
    //   210 stub @0x65724D  mov ecx,[ebp+8] / mov edx,[ebp+0xC] / call 0x657FF8
    //       sub_657FF8: 0x658013 call 0x405708 取裸角色名;
    //                   0x65801B mov eax,[0x7D7104] / mov eax,[eax]
    //                   0x658022 call 0x621CE4      (Delete)
    //       —— stub 送进 ecx 的是长度, 但 sub_657FF8 序言只有 0x657FFE `mov ebx,edx`,
    //          从不读 ecx, 所以 210 既无长度门也无第三参。
    //
    // [[0x7D7104]] 的身份本仓早已坐实并建模: Services/NativeGmDenyListCommands.cs
    // 的头注释把 off_7D7104 记为 BlockUsers.Dat 管理器单例, 并逐个列出
    // sub_621B14=Add / sub_621CE4=Delete / sub_622040=Tick / sub_622630=Save,
    // 连「sub_657110 ProcessOthGsMsg cases 209/210 -> cross-server Add/Delete
    // replication」这一行都已写明 —— 本文件即把那两条接线补上。
    //
    // sub_621B14(mgr, edx=name, cx=seconds) 逐条:
    //   0x621B4D call 0x40BCBC  = ASCII A-Z 转 a-z          (故仅 ASCII 大小写不敏感)
    //   0x621B5F/0x621B6A       遍历 [mgr+0x20] 链表, 逐节点 _LStrFromShortString
    //                           (0x405774) 后 _LStrCmp (0x40591C)
    //   命中: 0x621B71 movzx eax,word[ebp-6] / 0x621B77 add [node+0x14],eax
    //         —— **累加**剩余秒数, 且不写玩家标志、不落盘。
    //   未命中: 0x621B98 GetMem(0x20) / 0x621BAA 清零 /
    //           0x621BBD 0x4057AC + 0x621BCD 0x4039E4(cl=0x0F) 存 ASCII-fold 名
    //           (ShortString[15]) / 0x621BD6 [node+0x14]=movzx(cx) /
    //           0x621BE6 [node+0x18]=GetTickCount(0x408340) / 头插 /
    //           0x621BFA inc [mgr+0x24] /
    //           0x621C10 mov byte[player+0xB99],1 (在线才写) /
    //           0x621C19 call 0x622630 (Save)
    // sub_621CE4(mgr, edx=name) 逐条:
    //   0x621D14 ASCII A-Z 转 a-z; 命中则摘链 + 0x621D6E FreeMem(node,0x20);
    //   0x621D88 mov byte[player+0xB99],0 (在线才写); 0x621D8F dec [mgr+0x24];
    //   0x621D94 call 0x622630 (Save)。未命中则**什么都不做**(连 Save 都没有)。
    //
    // 单位: 计数器是**秒**。清扫 sub_622040 @0x622085:
    //   eax = now - [node+0x18]; 0x62208A mov ecx,0x3E8; 0x622091 div ecx;
    //   0x622093 sub [node+0x14],eax   => 每 1000 ms 扣 1。
    // 第三个 dword 取的是**低 16 位**: 0x621B71 `movzx eax,word[ebp-6]`。
    //
    // C# 活动入口统一落到 NativeGmBlockUserList。g_DenySayMsgList 只保留为旧调用点
    // 所需的投影；原始 ShortString 字节、重复节点、剩余秒数和 BlockUsers.Dat 落盘
    // 均由 canonical 链表保存。聊天命中、GM 命令和 209/210 不再各自改字典。
    // =======================================================================
    public static class NativeMirrorChatBan
    {
        public const int NativeNameByteCapacity = 15;

        private static readonly object PersistentSync = new();
        private static NativeGmBlockUserList _persistentList;

        /// <summary>
        /// 创建活动禁言表。0x40BCFA..0x40BD06 只把 ASCII A-Z 加 0x20，
        /// 非 ASCII 字符保持逐码元序数比较。
        /// </summary>
        public static System.Collections.Concurrent.ConcurrentDictionary<string, long>
            CreateStore()
        {
            return new System.Collections.Concurrent.ConcurrentDictionary<string, long>(
                NativeAsciiCaseInsensitiveComparer.Instance);
        }

        /// <summary>
        /// Loads the native primary mute list from Envir\BlockUsers.Dat and publishes
        /// a compatibility view to <see cref="M2Share.g_DenySayMsgList"/>.
        /// </summary>
        public static bool TryInitializePersistentStore(string envirDirectory,
            out int loadedCount, out string error)
        {
            loadedCount = 0;
            error = string.Empty;
            try
            {
                var filePath = Path.Combine(envirDirectory ?? string.Empty,
                    "BlockUsers.Dat");
                var candidate = new NativeGmBlockUserList(
                    new NativeBlockUserFileStore(filePath),
                    new OnlinePlayerBlockSink(), HUtil32.GbkEncoding);
                var now = HUtil32.GetTickCount();
                candidate.Load(now);

                lock (PersistentSync)
                {
                    _persistentList = candidate;
                    loadedCount = candidate.Count;
                    PublishPersistentSnapshot(candidate.Snapshot(), now);
                }
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        /// <summary>
        /// 战神 sub_621B14 经 ident 209 的跨服腿: 给 <paramref name="name"/> 追加
        /// <paramref name="nParam"/> 秒禁言。已在名单上则累加剩余时长。
        /// </summary>
        public static int Add(string name, int nParam)
        {
            if (name == null)
                return 0;

            var now = HUtil32.GetTickCount();
            var seconds = nParam & 0xFFFF;
            lock (PersistentSync)
            {
                if (_persistentList != null)
                {
                    var total = _persistentList.Add(name, seconds, now);
                    PublishPersistentSnapshot(_persistentList.Snapshot(), now);
                    return total;
                }
            }

            var list = M2Share.g_DenySayMsgList;
            if (list == null)
                return 0;

            // The native node stores the folded ShortString[15], while the lookup
            // string is not truncated before comparison.
            var lookupName = FoldAnsiName(name);
            var storedName = NormalizeStoredName(name);
            long addMs = seconds * 1000L;
            var totalSeconds = seconds;

            HUtil32.EnterCriticalSection(list);
            try
            {
                if (list.TryGetValue(lookupName, out var expireTick))
                {
                    var updated = AddTickMilliseconds(expireTick, addMs);
                    list[lookupName] = updated;
                    totalSeconds = RemainingSeconds(now, updated);
                }
                else
                {
                    list[storedName] = AddTickMilliseconds(now, addMs);
                }
            }
            finally
            {
                HUtil32.LeaveCriticalSection(list);
            }
            return totalSeconds;
        }

        /// <summary>
        /// 战神 sub_621CE4 经 ident 210 的跨服腿: 把 <paramref name="name"/> 从禁言
        /// 名单摘除。不在名单上时 native 什么都不做。
        /// </summary>
        public static bool Remove(string name)
        {
            if (name == null)
                return false;

            lock (PersistentSync)
            {
                if (_persistentList != null)
                {
                    var removed = _persistentList.Delete(name);
                    PublishPersistentSnapshot(_persistentList.Snapshot(),
                        HUtil32.GetTickCount());
                    return removed;
                }
            }

            var list = M2Share.g_DenySayMsgList;
            if (list == null)
                return false;

            var lookupName = FoldAnsiName(name);
            HUtil32.EnterCriticalSection(list);
            try
            {
                return list.TryRemove(lookupName, out _);
            }
            finally
            {
                HUtil32.LeaveCriticalSection(list);
            }
        }

        /// <summary>Native list membership used by the player chat/login path.</summary>
        public static bool Contains(string name)
        {
            if (name == null)
                return false;

            lock (PersistentSync)
            {
                if (_persistentList != null)
                    return _persistentList.Contains(name);
            }

            var list = M2Share.g_DenySayMsgList;
            return list != null && list.ContainsKey(FoldAnsiName(name));
        }

        /// <summary>Current canonical rows, including duplicate native nodes.</summary>
        public static IReadOnlyList<NativeBlockUserEntry> Snapshot()
        {
            lock (PersistentSync)
            {
                if (_persistentList != null)
                    return _persistentList.Snapshot();
            }

            var result = new List<NativeBlockUserEntry>();
            var list = M2Share.g_DenySayMsgList;
            if (list == null)
                return result;
            var now = HUtil32.GetTickCount();
            foreach (var item in list)
            {
                result.Add(new NativeBlockUserEntry
                {
                    Name = item.Key,
                    NameBytes = HUtil32.GbkEncoding.GetBytes(item.Key),
                    RemainSeconds = RemainingSeconds(now, item.Value),
                    LastTickMs = now,
                });
            }
            return result;
        }

        /// <summary>
        /// Runs sub_621E44/sub_622040 and returns compatibility keys removed by
        /// this sweep. Without a persistent model it keeps the isolated-harness
        /// in-memory cleanup behavior.
        /// </summary>
        public static IReadOnlyList<string> Tick(long nowMs)
        {
            lock (PersistentSync)
            {
                if (_persistentList != null)
                {
                    var before = new HashSet<string>(
                        M2Share.g_DenySayMsgList?.Keys ?? Array.Empty<string>(),
                        NativeAsciiCaseInsensitiveComparer.Instance);
                    _persistentList.Tick(nowMs);
                    PublishPersistentSnapshot(_persistentList.Snapshot(), nowMs);
                    before.ExceptWith(M2Share.g_DenySayMsgList?.Keys
                        ?? Array.Empty<string>());
                    return before.ToList();
                }
            }

            var removed = new List<string>();
            var list = M2Share.g_DenySayMsgList;
            if (list == null)
                return removed;
            foreach (var item in list)
            {
                if (IsExpired(nowMs, item.Value))
                    removed.Add(item.Key);
            }
            for (var i = 0; i < removed.Count; i++)
                list.TryRemove(removed[i], out _);
            return removed;
        }

        /// <summary>Unsigned 32-bit GetTickCount elapsed comparison.</summary>
        public static bool IsExpired(long nowMs, long deadlineMs)
        {
            var elapsed = unchecked((uint)((int)nowMs - (int)deadlineMs));
            return elapsed < 0x8000_0000U;
        }

        /// <summary>Remaining whole seconds under the native 32-bit tick model.</summary>
        public static int RemainingSeconds(long nowMs, long deadlineMs)
        {
            if (IsExpired(nowMs, deadlineMs))
                return 0;
            var remainingMs = unchecked((uint)((int)deadlineMs - (int)nowMs));
            return (int)(remainingMs / 1000U);
        }

        /// <summary>True when an unsigned 32-bit tick interval exceeds the gate.</summary>
        public static bool HasElapsed(long nowMs, long thenMs, uint intervalMs)
            => unchecked((uint)((int)nowMs - (int)thenMs)) > intervalMs;

        private static long AddTickMilliseconds(long tickMs, long deltaMs)
            => unchecked((long)(int)((uint)(int)tickMs + (uint)deltaMs));

        private static void PublishPersistentSnapshot(
            IReadOnlyList<NativeBlockUserEntry> entries, long nowMs)
        {
            var view = M2Share.g_DenySayMsgList;
            if (view == null)
            {
                view = CreateStore();
                M2Share.g_DenySayMsgList = view;
            }
            view.Clear();
            for (var i = 0; i < entries.Count; i++)
            {
                var entry = entries[i];
                var rawName = entry.NameBytes
                    ?? HUtil32.GbkEncoding.GetBytes(entry.Name ?? string.Empty);
                var key = DecodeAnsiBytes(rawName);
                var deadline = AddTickMilliseconds(entry.LastTickMs,
                    entry.RemainSeconds * 1000L);
                view.AddOrUpdate(key, deadline, (_, oldDeadline) =>
                    RemainingSeconds(nowMs, deadline) >
                    RemainingSeconds(nowMs, oldDeadline)
                        ? deadline
                        : oldDeadline);
            }
        }

        private static string NormalizeStoredName(string name)
        {
            var bytes = HUtil32.GbkEncoding.GetBytes(name);
            var length = Math.Min(bytes.Length, NativeNameByteCapacity);
            if (length != bytes.Length)
                Array.Resize(ref bytes, length);
            FoldAsciiBytes(bytes);
            return DecodeAnsiBytes(bytes);
        }

        private static string FoldAnsiName(string name)
        {
            var bytes = HUtil32.GbkEncoding.GetBytes(name);
            FoldAsciiBytes(bytes);
            return DecodeAnsiBytes(bytes);
        }

        private static string DecodeAnsiBytes(byte[] bytes)
        {
            var decoded = HUtil32.GbkEncoding.GetString(bytes);
            var roundTrip = HUtil32.GbkEncoding.GetBytes(decoded);
            if (roundTrip.Length == bytes.Length)
            {
                var equal = true;
                for (var i = 0; i < bytes.Length; i++)
                {
                    if (roundTrip[i] != bytes[i])
                    {
                        equal = false;
                        break;
                    }
                }
                if (equal)
                    return decoded;
            }

            // Preserve a malformed/truncated ANSI record byte-for-byte in the
            // string key; a later valid Unicode name will not alias it.
            return System.Text.Encoding.Latin1.GetString(bytes);
        }

        private static void FoldAsciiBytes(byte[] bytes)
        {
            for (var i = 0; i < bytes.Length; i++)
            {
                if (bytes[i] >= (byte)'A' && bytes[i] <= (byte)'Z')
                    bytes[i] = (byte)(bytes[i] + ('a' - 'A'));
            }
        }

        private sealed class OnlinePlayerBlockSink : INativeBlockUserSink
        {
            public void SetBlocked(string name, bool blocked)
            {
                var player = M2Share.UserEngine?.GetPlayObject(name);
                if (player != null)
                    player.m_boFilterSendMsg = blocked;
            }
        }

        private sealed class NativeAsciiCaseInsensitiveComparer
            : IEqualityComparer<string>
        {
            internal static readonly NativeAsciiCaseInsensitiveComparer Instance = new();

            public bool Equals(string x, string y)
            {
                if (ReferenceEquals(x, y))
                {
                    return true;
                }
                if (x == null || y == null || x.Length != y.Length)
                {
                    return false;
                }
                for (var i = 0; i < x.Length; i++)
                {
                    if (FoldAscii(x[i]) != FoldAscii(y[i]))
                    {
                        return false;
                    }
                }
                return true;
            }

            public int GetHashCode(string value)
            {
                unchecked
                {
                    var hash = 5381;
                    for (var i = 0; i < value.Length; i++)
                    {
                        hash = ((hash << 5) + hash) ^ FoldAscii(value[i]);
                    }
                    return hash;
                }
            }

            private static char FoldAscii(char value)
            {
                return value >= 'A' && value <= 'Z'
                    ? (char)(value + ('a' - 'A'))
                    : value;
            }
        }
    }
}
