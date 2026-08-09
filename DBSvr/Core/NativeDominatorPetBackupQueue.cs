using System;
using System.Collections.Generic;
using System.Threading;
using MySql.Data.MySqlClient;

namespace DBSvr.Core
{
    public sealed class NativeDominatorPetBackupQueue
    {
        private const int NormalDelayMilliseconds = 30_000;
        private const int LogAttempt = 11;
        private const int DropAttempt = 20;

        private sealed class WorkItem
        {
            public string MasterName = string.Empty;
            public long MasterId;
            public int Level;
            public uint Experience;
            public byte[] Blob = Array.Empty<byte>();
            public long AddedTick;
            public int Attempts;
        }

        private readonly object _sync = new();
        private readonly Queue<WorkItem> _pending = new();
        private Thread _thread;
        private bool _stopping;

        public void Start()
        {
            lock (_sync)
            {
                if (_thread?.IsAlive == true) return;
                _stopping = false;
                _thread = new Thread(Process)
                {
                    IsBackground = true,
                    Name = "NativeDominatorPetBackup"
                };
                _thread.Start();
            }
        }

        public void Stop()
        {
            Thread thread;
            lock (_sync)
            {
                _stopping = true;
                Monitor.PulseAll(_sync);
                thread = _thread;
            }
            if (thread?.IsAlive == true && thread != Thread.CurrentThread)
                thread.Join();
            lock (_sync)
                if (ReferenceEquals(_thread, thread)) _thread = null;
        }

        public void Enqueue(string masterName, long masterId, int level,
            uint experience, byte[] blob)
        {
            if (blob == null) throw new ArgumentNullException(nameof(blob));
            lock (_sync)
            {
                if (_stopping)
                {
                    DBShare.MainOutMessage(
                        $"[NativePetBackup] 停机期间拒绝 MasterId={masterId}");
                    return;
                }
                _pending.Enqueue(new WorkItem
                {
                    MasterName = masterName ?? string.Empty,
                    MasterId = masterId,
                    Level = level,
                    Experience = experience,
                    Blob = (byte[])blob.Clone(),
                    AddedTick = Environment.TickCount64
                });
                Monitor.Pulse(_sync);
            }
        }

        private void Process()
        {
            while (true)
            {
                WorkItem item;
                lock (_sync)
                {
                    while (_pending.Count == 0 && !_stopping)
                        Monitor.Wait(_sync);
                    if (_pending.Count == 0 && _stopping) return;
                    item = _pending.Peek();
                    if (!_stopping)
                    {
                        var elapsed = Environment.TickCount64 - item.AddedTick;
                        if (elapsed < NormalDelayMilliseconds)
                        {
                            Monitor.Wait(_sync,
                                (int)(NormalDelayMilliseconds - elapsed));
                            continue;
                        }
                    }
                }

                var success = TryPersist(item);
                if (!success) item.Attempts++;
                if (!success && item.Attempts == LogAttempt)
                    DBShare.MainOutMessage(
                        $"[NativePetBackup] 数据写入MYSQL出错 MasterId={item.MasterId}");
                if (success || item.Attempts >= DropAttempt)
                {
                    lock (_sync)
                    {
                        if (_pending.Count != 0
                            && ReferenceEquals(_pending.Peek(), item))
                            _pending.Dequeue();
                    }
                }
                else if (!_stopping)
                    Thread.Sleep(5);
            }
        }

        private static bool TryPersist(WorkItem item)
        {
            try
            {
                using var connection = new MySqlConnection(DBShare.DBConnection);
                connection.Open();
                using (var session = new MySqlCommand(
                           "SET SESSION TRANSACTION ISOLATION LEVEL READ COMMITTED; SET SESSION wait_timeout=2073600",
                           connection))
                    session.ExecuteNonQuery();

                using (var exists = new MySqlCommand(
                           "SELECT Idx FROM mir3_backup.dominatorpet WHERE MasterId=@masterId LIMIT 1",
                           connection))
                {
                    exists.Parameters.AddWithValue("@masterId", item.MasterId);
                    var index = exists.ExecuteScalar();
                    if (index == null || index == DBNull.Value)
                    {
                        using var insert = new MySqlCommand(
                            @"INSERT INTO mir3_backup.dominatorpet
                                (MasterName, MasterId, Level, Exp, CreateDate)
                              VALUES(@name, @masterId, @level, @exp, NOW())",
                            connection);
                        insert.Parameters.Add(LegacyGbkText.Parameter(
                            "@name", item.MasterName));
                        insert.Parameters.AddWithValue("@masterId", item.MasterId);
                        insert.Parameters.AddWithValue("@level", item.Level);
                        insert.Parameters.AddWithValue("@exp", item.Experience);
                        insert.ExecuteNonQuery();
                        // The original retries after inserting and writes the Blob next pass.
                        return false;
                    }
                }

                using (var update = new MySqlCommand(
                           @"UPDATE mir3_backup.dominatorpet
                             SET Level=@level, Exp=@exp, ModifyDate=NOW()
                             WHERE MasterId=@masterId",
                           connection))
                {
                    update.Parameters.AddWithValue("@level", item.Level);
                    update.Parameters.AddWithValue("@exp", item.Experience);
                    update.Parameters.AddWithValue("@masterId", item.MasterId);
                    if (update.ExecuteNonQuery() <= 0) return false;
                }

                using var data = new MySqlCommand(
                    @"UPDATE mir3_backup.dominatorpet
                      SET Data=UNHEX(@data) WHERE MasterId=@masterId",
                    connection);
                data.Parameters.Add("@data", MySqlDbType.LongText).Value =
                    Convert.ToHexString(item.Blob);
                data.Parameters.AddWithValue("@masterId", item.MasterId);
                return data.ExecuteNonQuery() > 0;
            }
            catch { return false; }
        }
    }
}
