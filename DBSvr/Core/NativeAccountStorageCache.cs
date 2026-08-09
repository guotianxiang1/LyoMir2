using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Threading;

namespace DBSvr.Core
{
    public sealed class NativeAccountStorageCache
    {
        private sealed class Entry
        {
            public readonly object PersistSync = new();
            public byte[] Account = Array.Empty<byte>();
            public int StorageIndex;
            public bool Loaded;
            public byte[] Data;
        }

        private readonly object _sync = new();
        private readonly object _renameSync = new();
        private readonly Dictionary<string, Entry> _accounts =
            new(StringComparer.Ordinal);
        private readonly object _saveSync = new();
        private readonly Dictionary<string, SaveWorkItem> _savePending =
            new(StringComparer.Ordinal);
        private readonly Queue<string> _saveOrder = new();
        private Thread _saveThread;
        private bool _saveStopping;
        private IStorageService _saveService;

        private sealed class SaveWorkItem
        {
            public string Key = string.Empty;
            public Entry Owner;
            public byte[] Account = Array.Empty<byte>();
            public byte[] Data = Array.Empty<byte>();
            public int Attempts;

            public void Replace(byte[] data)
            {
                Data = (byte[])data.Clone();
                Attempts = 0;
            }
        }

        public static string NormalizeAccountKey(byte[] account)
        {
            return Convert.ToHexString(NormalizeAccountBytes(account));
        }

        public static byte[] NormalizeAccountBytes(byte[] account)
        {
            account ??= Array.Empty<byte>();
            var normalized = (byte[])account.Clone();
            for (var i = 0; i < normalized.Length; i++)
                if (normalized[i] is >= (byte)'A' and <= (byte)'Z')
                    normalized[i] += (byte)('a' - 'A');
            return normalized;
        }

        public void RegisterAccount(byte[] account)
        {
            account ??= Array.Empty<byte>();
            if (account.Length == 0) return;
            var key = NormalizeAccountKey(account);
            lock (_renameSync)
            lock (_sync)
                if (!_accounts.ContainsKey(key))
                    _accounts.Add(key, new Entry
                    {
                        Account = (byte[])account.Clone()
                    });
        }

        public bool ContainsAccount(byte[] account)
        {
            lock (_sync)
                return _accounts.ContainsKey(NormalizeAccountKey(account));
        }

        /// <summary>
        /// Serializes a database account rename with the storage writer and then
        /// rekeys the in-memory entry. A detached old batch becomes a no-op
        /// because its key no longer resolves after this method returns.
        /// </summary>
        public bool TryRenameAccount(byte[] oldAccount, byte[] newAccount,
            Func<bool> persistRename)
        {
            oldAccount ??= Array.Empty<byte>();
            newAccount ??= Array.Empty<byte>();
            if (oldAccount.Length == 0 || newAccount.Length == 0
                || persistRename == null)
                return false;
            var oldKey = NormalizeAccountKey(oldAccount);
            var newKey = NormalizeAccountKey(newAccount);
            lock (_renameSync)
            {
                Entry entry;
                lock (_sync)
                {
                    if (!_accounts.TryGetValue(oldKey, out entry)) return false;
                }

                lock (entry.PersistSync)
                {
                    lock (_sync)
                    {
                        if (!_accounts.TryGetValue(oldKey, out var current)
                            || !ReferenceEquals(current, entry))
                            return false;
                    }
                    if (!persistRename()) return false;

                    lock (_sync)
                    {
                        _accounts.Remove(oldKey);
                        entry.Account = (byte[])newAccount.Clone();
                        _accounts[newKey] = entry;
                    }
                    lock (_saveSync)
                    {
                        // The native rename does not reject an existing destination
                        // account. The cache has one visible entry per account key, so
                        // source state becomes the visible destination and detached
                        // destination work is made stale by its Owner reference.
                        if (oldKey != newKey)
                            _savePending.Remove(newKey);
                        if (_savePending.Remove(oldKey, out var pending))
                        {
                            pending.Key = newKey;
                            pending.Account = (byte[])newAccount.Clone();
                            _savePending[newKey] = pending;
                            _saveOrder.Enqueue(newKey);
                        }
                        if (entry.Data != null
                            && !_savePending.ContainsKey(newKey))
                        {
                            _savePending.Add(newKey, new SaveWorkItem
                            {
                                Key = newKey,
                                Owner = entry,
                                Account = (byte[])newAccount.Clone(),
                                Data = (byte[])entry.Data.Clone()
                            });
                            _saveOrder.Enqueue(newKey);
                            Monitor.Pulse(_saveSync);
                        }
                    }
                    return true;
                }
            }
        }

        public void LoadStorageIndex(IStorageService service)
        {
            if (service == null) throw new ArgumentNullException(nameof(service));
            lock (_sync)
                foreach (var entry in _accounts.Values)
                {
                    entry.StorageIndex = 0;
                    entry.Loaded = false;
                    entry.Data = null;
                }

            var lastIndex = 0;
            while (true)
            {
                var previousLastIndex = lastIndex;
                var page = service.GetNativeStoragePage(lastIndex, 5000)
                           ?? new List<NativeStorageIndexEntry>();
                if (page.Count == 0) break;
                lock (_sync)
                    foreach (var item in page)
                    {
                        if (item == null) continue;
                        if (_accounts.TryGetValue(
                                NormalizeAccountKey(item.Account),
                                out var entry))
                            entry.StorageIndex = item.Index;
                        if (item.Index > lastIndex) lastIndex = item.Index;
                    }
                if (lastIndex <= previousLastIndex)
                    throw new InvalidOperationException(
                        "native storage index page did not advance");
            }
        }

        public NativeAccountStorageBlobResult Load(IStorageService service,
            byte[] account)
        {
            if (service == null) throw new ArgumentNullException(nameof(service));
            lock (_sync)
            {
                if (!_accounts.TryGetValue(NormalizeAccountKey(account),
                        out var entry))
                    return new NativeAccountStorageBlobResult { Result = 0 };
                if (entry.Loaded)
                    return new NativeAccountStorageBlobResult { Result = -1 };
                if (entry.Data != null)
                {
                    entry.Loaded = true;
                    return new NativeAccountStorageBlobResult
                    {
                        Result = 1,
                        Data = (byte[])entry.Data.Clone()
                    };
                }
                if (entry.StorageIndex == 0)
                    return new NativeAccountStorageBlobResult { Result = 0 };
                var loaded = service.LoadNativeStorage(entry.StorageIndex)
                             ?? new NativeAccountStorageBlobResult { Result = 0 };
                if (loaded.Result == 1)
                {
                    entry.Data = (byte[])loaded.Data.Clone();
                    entry.Loaded = true;
                    return new NativeAccountStorageBlobResult
                    {
                        Result = 1,
                        Data = (byte[])entry.Data.Clone()
                    };
                }
                return loaded;
            }
        }

        public bool TryExtractOfflineItem(IStorageService service,
            byte[] account, int makeIndex, out byte[] itemRecord)
        {
            itemRecord = null;
            if (service == null) throw new ArgumentNullException(nameof(service));
            account ??= Array.Empty<byte>();
            var key = NormalizeAccountKey(account);
            Entry entry;
            lock (_sync)
            {
                if (!_accounts.TryGetValue(key, out entry)) return false;
            }

            lock (entry.PersistSync)
            {
                byte[] loadedData = null;
                int storageIndex;
                lock (_sync)
                {
                    if (!_accounts.TryGetValue(key, out var current)
                        || !ReferenceEquals(current, entry) || entry.Loaded)
                        return false;
                    storageIndex = entry.StorageIndex;
                    if (entry.Data != null)
                        loadedData = (byte[])entry.Data.Clone();
                }

                if (loadedData == null)
                {
                    if (storageIndex == 0) return false;
                    NativeAccountStorageBlobResult loaded;
                    try { loaded = service.LoadNativeStorage(storageIndex); }
                    catch { return false; }
                    if (loaded?.Result != 1 || loaded.Data == null) return false;
                    loadedData = (byte[])loaded.Data.Clone();
                }

                byte[] updated;
                byte[] accountBytes;
                lock (_sync)
                {
                    if (!_accounts.TryGetValue(key, out var current)
                        || !ReferenceEquals(current, entry) || entry.Loaded)
                        return false;
                    if (!IsItemArray(loadedData)) return false;

                    entry.Data = (byte[])loadedData.Clone();
                    updated = (byte[])entry.Data.Clone();
                    var count = BinaryPrimitives.ReadUInt16LittleEndian(
                        updated.AsSpan(2, 2));
                    for (var i = 0; i < count; i++)
                    {
                        var offset = 4 + i * NativeAccountStorageProtocol.ItemSize;
                        if (BinaryPrimitives.ReadInt32LittleEndian(
                                updated.AsSpan(offset, 4)) != makeIndex)
                            continue;
                        itemRecord = updated.AsSpan(offset,
                            NativeAccountStorageProtocol.ItemSize).ToArray();
                        updated.AsSpan(offset,
                            NativeAccountStorageProtocol.ItemSize).Clear();
                        entry.Data = (byte[])updated.Clone();
                        accountBytes = (byte[])entry.Account.Clone();
                        _ = StageSave(entry, key, accountBytes, updated);
                        return true;
                    }
                    return false;
                }
            }
        }

        public void StartSaveWorker(IStorageService service)
        {
            if (service == null) throw new ArgumentNullException(nameof(service));
            lock (_saveSync)
            {
                _saveService = service;
                if (_saveThread?.IsAlive == true) return;
                _saveStopping = false;
                _saveThread = new Thread(ProcessSaveQueue)
                {
                    IsBackground = true,
                    Name = "NativeAccountStorageSave"
                };
                _saveThread.Start();
            }
        }

        public void StopSaveWorker()
        {
            Thread thread;
            lock (_saveSync)
            {
                _saveStopping = true;
                Monitor.PulseAll(_saveSync);
                thread = _saveThread;
            }
            if (thread?.IsAlive == true && thread != Thread.CurrentThread)
                thread.Join();
            lock (_saveSync)
                if (ReferenceEquals(_saveThread, thread)) _saveThread = null;
        }

        public bool StageSave(byte[] account, byte[] data)
        {
            account ??= Array.Empty<byte>();
            data ??= Array.Empty<byte>();
            var key = NormalizeAccountKey(account);
            Entry entry;
            lock (_sync)
            {
                if (!_accounts.TryGetValue(key, out entry)) return false;
                entry.Data = (byte[])data.Clone();
            }
            return StageSave(entry, key, account, data);
        }

        private bool StageSave(Entry entry, string key, byte[] account,
            byte[] data)
        {
            lock (_saveSync)
            {
                if (_saveStopping || _saveThread?.IsAlive != true) return false;
                if (_savePending.TryGetValue(key, out var pending))
                {
                    if (!ReferenceEquals(pending.Owner, entry)) return false;
                    pending.Replace(data);
                    return true;
                }
                _savePending.Add(key, new SaveWorkItem
                {
                    Key = key,
                    Owner = entry,
                    Account = NormalizeAccountBytes(account),
                    Data = (byte[])data.Clone()
                });
                _saveOrder.Enqueue(key);
                Monitor.Pulse(_saveSync);
                return true;
            }
        }

        private static bool IsItemArray(byte[] data)
        {
            if (data == null || data.Length < 4) return false;
            var count = BinaryPrimitives.ReadUInt16LittleEndian(
                data.AsSpan(2, 2));
            return (long)data.Length
                   == 4L + (long)count * NativeAccountStorageProtocol.ItemSize;
        }

        private void ProcessSaveQueue()
        {
            List<SaveWorkItem> batch = null;
            var batchIndex = 0;
            while (true)
            {
                if (batch == null || batchIndex >= batch.Count)
                {
                    lock (_saveSync)
                    {
                        while (_saveOrder.Count == 0 && !_saveStopping)
                            Monitor.Wait(_saveSync);
                        if (_saveOrder.Count == 0 && _saveStopping) return;
                        var limit = _saveStopping ? 200 : 100;
                        batch = new List<SaveWorkItem>(
                            Math.Min(limit, _saveOrder.Count));
                        while (batch.Count < limit && _saveOrder.Count != 0)
                        {
                            var key = _saveOrder.Dequeue();
                            if (_savePending.Remove(key, out var item))
                                batch.Add(item);
                        }
                        batchIndex = 0;
                    }
                    if (batch.Count == 0) continue;
                }

                var active = batch[batchIndex];
                if (TryPersist(active) || ++active.Attempts >= 20)
                {
                    if (active.Attempts == 20)
                        DBShare.MainOutMessage(
                            $"[NativeStorageSave] 丢弃账号保存 {Convert.ToHexString(active.Account)}");
                    batchIndex++;
                    continue;
                }
                if (active.Attempts == 11)
                    DBShare.MainOutMessage(
                        $"[NativeStorageSave] 数据写入MYSQL出错 {Convert.ToHexString(active.Account)}");
                Thread.Sleep(5);
            }
        }

        private bool TryPersist(SaveWorkItem item)
        {
            Entry entry;
            int index;
            lock (_sync)
            {
                if (!_accounts.TryGetValue(item.Key, out entry)
                    || !ReferenceEquals(entry, item.Owner))
                    return true;
            }
            lock (entry.PersistSync)
            {
                lock (_sync)
                {
                    if (!_accounts.TryGetValue(item.Key, out var current)
                        || !ReferenceEquals(current, entry)
                        || !ReferenceEquals(entry, item.Owner))
                        return true;
                    index = entry.StorageIndex;
                }
                if (index == 0)
                {
                    try { index = _saveService.EnsureNativeStorage(item.Account); }
                    catch { index = 0; }
                    if (index == 0) return false;
                    lock (_sync)
                    {
                        if (!_accounts.TryGetValue(item.Key, out var current)
                            || !ReferenceEquals(current, entry)
                            || !ReferenceEquals(entry, item.Owner))
                            return true;
                        entry.StorageIndex = index;
                    }
                }
                try { return _saveService.SaveNativeStorage(index, item.Data); }
                catch { return false; }
            }
        }
    }
}
