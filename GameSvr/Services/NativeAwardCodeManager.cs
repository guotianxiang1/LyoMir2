using System.Globalization;
using GameSvr.PasEngine;
using MySql.Data.MySqlClient;
using SystemModule;
using SystemModule.Packet;

namespace GameSvr.Services
{
    internal sealed class NativeAwardCodeTask
    {
        internal NativeAwardCodeTask(byte taskType, byte[] payload,
            int enqueuedTick)
        {
            TaskType = taskType;
            Payload = payload?.ToArray() ?? Array.Empty<byte>();
            EnqueuedTick = enqueuedTick;
        }

        internal byte TaskType { get; }
        internal byte[] Payload { get; }
        internal int EnqueuedTick { get; }
    }

    internal sealed class NativeAwardCodeCompletion
    {
        internal NativeAwardCodeCompletion(byte taskType, long playerId,
            byte[] codeBytes, int result, int awardCodeType, int activeParam)
        {
            TaskType = taskType;
            PlayerId = playerId;
            CodeBytes = codeBytes?.ToArray() ?? Array.Empty<byte>();
            Result = result;
            AwardCodeType = awardCodeType;
            ActiveParam = activeParam;
        }

        internal byte TaskType { get; }
        internal long PlayerId { get; }
        internal byte[] CodeBytes { get; }
        internal int Result { get; }
        internal int AwardCodeType { get; }
        internal int ActiveParam { get; }

        internal static NativeAwardCodeCompletion Failure(
            NativeAwardCodeTask task)
        {
            if (task.TaskType ==
                    NativeAwardCodeSetActiveParamTaskCodec.TaskType
                && NativeAwardCodeSetActiveParamTaskCodec.TryDecode(
                    task.Payload, out var setTask, out _))
            {
                return new NativeAwardCodeCompletion(task.TaskType,
                    setTask.PlayerId, setTask.CodeBytes,
                    NativeAwardCodeSetActiveParamTaskCodec.FailureResult, 0, 0);
            }

            if (task.TaskType == NativeAwardCodeTaskCodec.QueryTaskType
                && NativeAwardCodeTaskCodec.TryDecodeQuery(
                    task.Payload, out var queryTask, out _))
            {
                return new NativeAwardCodeCompletion(task.TaskType,
                    queryTask.PlayerId, queryTask.CodeBytes,
                    NativeAwardCodeTaskCodec.QueryMiss, 0, 0);
            }

            return new NativeAwardCodeCompletion(task.TaskType, 0,
                Array.Empty<byte>(), NativeAwardCodeTaskCodec.QueryMiss, 0, 0);
        }
    }

    /// <summary>
    /// One shared FIFO for every native award-code operation. Process executes
    /// at most one mature head and its callback synchronously on UserEngine.
    /// </summary>
    internal sealed class NativeAwardCodeManager
    {
        private readonly object _syncRoot = new();
        private readonly Queue<NativeAwardCodeTask> _tasks = new();
        private readonly Func<NativeAwardCodeTask, NativeAwardCodeCompletion>
            _execute;
        private readonly Action<NativeAwardCodeCompletion> _complete;
        private readonly Action<Exception> _error;

        internal NativeAwardCodeManager(
            Func<NativeAwardCodeTask, NativeAwardCodeCompletion> execute,
            Action<NativeAwardCodeCompletion> complete,
            Action<Exception> error)
        {
            _execute = execute ?? throw new ArgumentNullException(nameof(execute));
            _complete = complete ?? throw new ArgumentNullException(nameof(complete));
            _error = error ?? (_ => { });
        }

        internal int PendingCount
        {
            get
            {
                lock (_syncRoot) return _tasks.Count;
            }
        }

        internal void Enqueue(byte taskType, byte[] payload, int currentTick)
        {
            lock (_syncRoot)
                _tasks.Enqueue(new NativeAwardCodeTask(
                    taskType, payload, currentTick));
        }

        internal void Process(int currentTick)
        {
            NativeAwardCodeTask task;
            lock (_syncRoot)
            {
                if (_tasks.Count == 0) return;
                var head = _tasks.Peek();
                if (!IsMature(currentTick, head.EnqueuedTick)) return;
                task = _tasks.Dequeue();
            }

            NativeAwardCodeCompletion completion;
            try
            {
                completion = _execute(task)
                             ?? NativeAwardCodeCompletion.Failure(task);
            }
            catch (Exception ex)
            {
                ReportError(ex);
                completion = NativeAwardCodeCompletion.Failure(task);
            }

            try
            {
                _complete(completion);
            }
            catch (Exception ex)
            {
                ReportError(ex);
            }
        }

        internal static bool IsMature(int currentTick, int enqueuedTick)
        {
            return unchecked(currentTick - enqueuedTick) >=
                   NativeAwardCodeTaskCodec.MinimumQueueAgeMilliseconds;
        }

        private void ReportError(Exception exception)
        {
            try
            {
                _error(exception);
            }
            catch
            {
                // Error reporting must not interrupt FIFO processing.
            }
        }
    }

    internal static class NativeAwardCodeService
    {
        private static readonly NativeAwardCodeManager Manager = new(
            NativeAwardCodeStore.Execute,
            DispatchCompletion,
            ex => M2Share.ErrorMessage(
                "[NativeAwardCode] process failed: " + ex.Message));

        internal static void EnqueueQuery(TPlayObject player, string code)
        {
            if (player == null) return;
            var playerId = player.GetCachedNativeUserId();
            if (playerId == 0) return;
            if (!NativeAwardCodeTaskCodec.TryEncodeQuery(code, playerId,
                    player.m_sCharName, out var payload, out _))
                return;
            Manager.Enqueue(NativeAwardCodeTaskCodec.QueryTaskType,
                payload, HUtil32.GetTickCount());
        }

        internal static void EnqueueSetActiveParam(TPlayObject player,
            string code, int activeParam)
        {
            if (player == null) return;
            var playerId = player.GetCachedNativeUserId();
            if (playerId == 0) return;
            if (!NativeAwardCodeSetActiveParamTaskCodec.TryEncode(code,
                    activeParam, playerId, player.m_sCharName,
                    out var payload, out _))
                return;
            Manager.Enqueue(NativeAwardCodeSetActiveParamTaskCodec.TaskType,
                payload, HUtil32.GetTickCount());
        }

        internal static void Process(int currentTick)
        {
            Manager.Process(currentTick);
        }

        private static void DispatchCompletion(
            NativeAwardCodeCompletion completion)
        {
            var player = ResolveOnlinePlayer(completion.PlayerId);
            var pasEngine = M2Share.PasEngine;
            if (player == null || pasEngine == null) return;

            var runQuest = pasEngine.FindScriptFile(
                Path.Combine("PsMapQuest", "RunQuest.pas"));
            if (runQuest == null) return;

            pasEngine.TryCallProcedure(runQuest, "AwardCodeExecCallBack",
                player, null,
                PasValue.FromInt(completion.Result),
                PasValue.FromString(HUtil32.GbkEncoding.GetString(
                    completion.CodeBytes)),
                PasValue.FromInt(completion.AwardCodeType),
                PasValue.FromInt(completion.ActiveParam));
        }

        internal static TPlayObject ResolveOnlinePlayer(long playerId)
        {
            if (playerId == 0) return null;
            var userEngine = M2Share.UserEngine;
            if (userEngine == null) return null;
            foreach (var candidate in userEngine.PlayObjects)
            {
                if (candidate != null && !candidate.m_boGhost
                    && candidate.GetCachedNativeUserId() == playerId)
                    return candidate;
            }
            return null;
        }
    }

    internal static class NativeAwardCodeStore
    {
        internal static NativeAwardCodeCompletion Execute(
            NativeAwardCodeTask task)
        {
            if (task.TaskType == NativeAwardCodeTaskCodec.QueryTaskType)
                return ExecuteQuery(task);
            if (task.TaskType ==
                NativeAwardCodeSetActiveParamTaskCodec.TaskType)
                return ExecuteSet(task);
            return NativeAwardCodeCompletion.Failure(task);
        }

        internal static string BuildSelectSql(byte[] codeBytes)
        {
            var code = HUtil32.GbkEncoding.GetString(
                codeBytes ?? Array.Empty<byte>());
            return NativeAwardCodeTaskCodec.QuerySqlFormat.Replace(
                "%s", code, StringComparison.Ordinal);
        }

        internal static string BuildUpdateSql(byte[] codeBytes,
            int activeParam, long playerId, byte[] roleNameBytes)
        {
            var code = HUtil32.GbkEncoding.GetString(
                codeBytes ?? Array.Empty<byte>());
            var roleName = HUtil32.GbkEncoding.GetString(
                roleNameBytes ?? Array.Empty<byte>());
            return "Update gamedata.awardcodes  set ActiveParam = " +
                   activeParam.ToString(CultureInfo.InvariantCulture) +
                   ", OwnerPlayerID = " +
                   playerId.ToString(CultureInfo.InvariantCulture) +
                   ", OwnerChrName = '" + roleName +
                   "', ModifyDate = Now() where AwardCode like '" + code +
                   "';";
        }

        private static NativeAwardCodeCompletion ExecuteQuery(
            NativeAwardCodeTask task)
        {
            if (!NativeAwardCodeTaskCodec.TryDecodeQuery(task.Payload,
                    out var request, out _))
                return NativeAwardCodeCompletion.Failure(task);

            var sql = BuildSelectSql(request.CodeBytes);
            try
            {
                using var connection = OpenConnection();
                using var command = connection.CreateCommand();
                command.CommandText = sql;
                using var reader = command.ExecuteReader();
                if (!reader.Read())
                {
                    LogSqlFailed(sql);
                    return NativeAwardCodeCompletion.Failure(task);
                }

                var awardCodeType = ReadInt32(reader, 0);
                var activeParam = ReadInt32(reader, 1);
                _ = ReadInt32(reader, 2);
                _ = ReadInt32(reader, 3);
                _ = ReadInt64(reader, 4);
                _ = ReadString(reader, 5);
                return new NativeAwardCodeCompletion(task.TaskType,
                    request.PlayerId, request.CodeBytes,
                    NativeAwardCodeTaskCodec.QueryHit,
                    awardCodeType, activeParam);
            }
            catch (Exception)
            {
                LogSqlFailed(sql);
                return NativeAwardCodeCompletion.Failure(task);
            }
        }

        private static NativeAwardCodeCompletion ExecuteSet(
            NativeAwardCodeTask task)
        {
            if (!NativeAwardCodeSetActiveParamTaskCodec.TryDecode(task.Payload,
                    out var request, out _))
                return NativeAwardCodeCompletion.Failure(task);

            var selectSql = BuildSelectSql(request.CodeBytes);
            int awardCodeType;
            long ownerPlayerId;
            try
            {
                using var connection = OpenConnection();
                using (var select = connection.CreateCommand())
                {
                    select.CommandText = selectSql;
                    using var reader = select.ExecuteReader();
                    if (!reader.Read())
                    {
                        LogSqlFailed(selectSql);
                        return NativeAwardCodeCompletion.Failure(task);
                    }

                    awardCodeType = ReadInt32(reader, 0);
                    _ = ReadInt32(reader, 1);
                    _ = ReadInt32(reader, 2);
                    _ = ReadInt32(reader, 3);
                    ownerPlayerId = ReadInt64(reader, 4);
                    _ = ReadString(reader, 5);
                }

                if (!NativeAwardCodeSetActiveParamTaskCodec.CanUpdate(
                        1, ownerPlayerId, request.PlayerId))
                    return NativeAwardCodeCompletion.Failure(task);

                var updateSql = BuildUpdateSql(request.CodeBytes,
                    request.ActiveParam, request.PlayerId,
                    request.RoleNameBytes);
                try
                {
                    using var update = connection.CreateCommand();
                    update.CommandText = updateSql;
                    update.ExecuteNonQuery();
                }
                catch (Exception)
                {
                    M2Share.MainOutMessage("[Exception]: " + updateSql);
                    LogSqlFailed(updateSql);
                    return NativeAwardCodeCompletion.Failure(task);
                }

                return new NativeAwardCodeCompletion(task.TaskType,
                    request.PlayerId, request.CodeBytes,
                    NativeAwardCodeSetActiveParamTaskCodec.SuccessResult,
                    awardCodeType, request.ActiveParam);
            }
            catch (Exception)
            {
                LogSqlFailed(selectSql);
                return NativeAwardCodeCompletion.Failure(task);
            }
        }

        private static MySqlConnection OpenConnection()
        {
            var connectionString = M2Share.g_Config?.sConnctionString;
            if (string.IsNullOrWhiteSpace(connectionString))
                throw new InvalidOperationException(
                    "award-code database connection is not configured");
            var connection = new MySqlConnection(connectionString);
            connection.Open();
            return connection;
        }

        private static int ReadInt32(MySqlDataReader reader, int ordinal)
        {
            return reader.IsDBNull(ordinal)
                ? 0
                : Convert.ToInt32(reader.GetValue(ordinal),
                    CultureInfo.InvariantCulture);
        }

        private static long ReadInt64(MySqlDataReader reader, int ordinal)
        {
            return reader.IsDBNull(ordinal)
                ? 0
                : Convert.ToInt64(reader.GetValue(ordinal),
                    CultureInfo.InvariantCulture);
        }

        private static string ReadString(MySqlDataReader reader, int ordinal)
        {
            return reader.IsDBNull(ordinal)
                ? string.Empty
                : Convert.ToString(reader.GetValue(ordinal),
                    CultureInfo.InvariantCulture) ?? string.Empty;
        }

        private static void LogSqlFailed(string sql)
        {
            M2Share.MainOutMessage("执行sql失败:" + sql);
        }
    }
}
