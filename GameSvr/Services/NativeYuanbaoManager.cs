using System.Collections.Concurrent;
using MySql.Data.MySqlClient;

namespace GameSvr.Services
{
    internal enum NativeYuanbaoRequestKind : byte
    {
        Mail,
        YbShop,
        Script,
        PasScriptPurchase
    }

    internal sealed class NativeYuanbaoRequest
    {
        internal NativeYuanbaoRequestKind Kind { get; private set; }
        internal long UserId { get; }
        internal byte[] AccountBytes { get; }
        internal byte[] CharacterNameBytes { get; }
        internal byte[] ContextIdBytes { get; private set; }
        internal byte[] ActionBytes { get; private set; }
        internal byte[] DescriptorBytes { get; private set; }
        internal int Amount { get; }
        internal byte Operation { get; }
        internal int OrderId { get; }
        internal int GoodsIndex { get; private set; }
        internal int GoodsCount { get; private set; }
        internal int ReferenceId { get; private set; }
        internal Action<NativeYuanbaoResult> BeforeOuterCompletionLog { get; private set; }
        internal Action<NativeYuanbaoResult> Completion { get; }

        internal NativeYuanbaoRequest(long userId, string account, string characterName,
            int amount, byte operation, int orderId,
            Action<NativeYuanbaoResult> completion)
        {
            UserId = userId;
            AccountBytes = EncodeShortString(account, 20);
            CharacterNameBytes = EncodeShortString(characterName, 15);
            ContextIdBytes = Array.Empty<byte>();
            ActionBytes = EncodeShortString(characterName, 15);
            DescriptorBytes = Array.Empty<byte>();
            Amount = amount;
            Operation = operation;
            OrderId = orderId;
            Completion = completion;
        }

        internal static NativeYuanbaoRequest CreateYbShop(long userId,
            string account, string characterName, int amount, int buyLogId,
            Action<NativeYuanbaoResult> completion)
        {
            var request = new NativeYuanbaoRequest(userId, account, characterName,
                amount, NativeYuanbaoManager.SubtractOperation, buyLogId,
                completion)
            {
                Kind = NativeYuanbaoRequestKind.YbShop,
                GoodsIndex = NativeYbShopPurchaseStore.LingFuGoodsIndex,
                GoodsCount = amount,
                ActionBytes = EncodeShortString(
                    NativeYbShopPurchaseStore.LingFuGoodsName, 15),
                DescriptorBytes = SystemModule.HUtil32.GbkEncoding.GetBytes(
                    NativeYbShopPurchaseStore.LingFuGoodsName + ":" + amount)
            };
            return request;
        }

        internal static NativeYuanbaoRequest CreateScript(long userId,
            string account, string characterName, int amount, byte operation,
            Action<NativeYuanbaoResult> beforeOuterCompletionLog,
            Action<NativeYuanbaoResult> completion)
        {
            return new NativeYuanbaoRequest(userId, account, characterName,
                amount, operation, 0, completion)
            {
                Kind = NativeYuanbaoRequestKind.Script,
                BeforeOuterCompletionLog = beforeOuterCompletionLog
            };
        }

        internal static NativeYuanbaoRequest CreatePasScriptPurchase(
            long userId, string account, string characterName, int amount,
            int scriptLogId, int referenceId, int quantity,
            byte[] callbackBytes, byte[] descriptorBytes,
            Action<NativeYuanbaoResult> beforeOuterCompletionLog,
            Action<NativeYuanbaoResult> completion)
        {
            return new NativeYuanbaoRequest(userId, account, characterName,
                amount, NativeYuanbaoManager.SubtractOperation, scriptLogId,
                completion)
            {
                Kind = NativeYuanbaoRequestKind.PasScriptPurchase,
                GoodsIndex = scriptLogId,
                GoodsCount = quantity,
                ReferenceId = referenceId,
                ActionBytes = callbackBytes?.ToArray() ?? Array.Empty<byte>(),
                DescriptorBytes = descriptorBytes?.ToArray() ?? Array.Empty<byte>(),
                BeforeOuterCompletionLog = beforeOuterCompletionLog
            };
        }

        private static byte[] EncodeShortString(string value, int maxBytes)
        {
            var bytes = SystemModule.HUtil32.GbkEncoding.GetBytes(value ?? string.Empty);
            return bytes.Length <= maxBytes ? bytes : bytes[..maxBytes];
        }

        internal void CreateContextIdForEnqueue()
        {
            if (ContextIdBytes.Length != 0) return;
            ContextIdBytes = NativeYuanbaoContextId.Generate();
        }

        internal void SetScriptCallbackCharacterName(string characterName)
        {
            if (Kind != NativeYuanbaoRequestKind.Script) return;
            ActionBytes = EncodeShortString(characterName, 15);
        }

        internal void SetBeforeOuterCompletionLog(
            Action<NativeYuanbaoResult> beforeOuterCompletionLog)
        {
            BeforeOuterCompletionLog = beforeOuterCompletionLog;
        }
    }

    internal readonly struct NativeYuanbaoResult
    {
        internal int ErrorCode { get; }
        internal int Balance { get; }

        internal NativeYuanbaoResult(int errorCode, int balance)
        {
            ErrorCode = errorCode;
            Balance = balance;
        }
    }

    internal sealed class NativeYuanbaoFifo
    {
        private readonly Queue<NativeYuanbaoRequest> _requests = new();

        internal int Count => _requests.Count;

        internal void Enqueue(NativeYuanbaoRequest request) => _requests.Enqueue(request);

        internal bool TryDequeue(out NativeYuanbaoRequest request)
        {
            if (_requests.Count == 0)
            {
                request = null;
                return false;
            }
            request = _requests.Dequeue();
            return true;
        }
    }

    internal static class NativeYuanbaoManager
    {
        internal const byte AddOperation = 0;
        internal const byte SubtractOperation = 1;
        internal const int InvalidUserId = -1500001;
        internal const int InsufficientBalance = -1500002;
        internal const int SqlFailure = -1500003;
        internal const int NegativeAmount = -1500004;

        internal static string GetErrorText(int errorCode) => errorCode switch
        {
            0 => "支付成功",
            InvalidUserId => "用户ID不合法",
            InsufficientBalance => "元宝数不足",
            SqlFailure => "系统错误",
            NegativeAmount => "不能充值或扣除负数",
            _ => "未知错误"
        };

        private readonly record struct Completion(
            NativeYuanbaoRequest Request, NativeYuanbaoResult Result);

        private static readonly object SyncRoot = new();
        private static readonly NativeYuanbaoFifo Requests = new();
        private static readonly ConcurrentQueue<Completion> Completions = new();
        private static bool _workerRunning;

        internal static bool Enqueue(NativeYuanbaoRequest request)
        {
            if (request == null
                || request.Operation is not AddOperation and not SubtractOperation
                || request.Completion == null)
                return false;

            switch (request.Kind)
            {
                case NativeYuanbaoRequestKind.YbShop:
                    NativeAccountLogManager.EnqueueShop(request,
                        NativeAccountLogManager.ShopBeginStage, 0);
                    break;
                case NativeYuanbaoRequestKind.Script:
                    NativeAccountLogManager.EnqueueScript(request,
                        NativeAccountLogManager.ScriptBeginStage, 0);
                    break;
                case NativeYuanbaoRequestKind.PasScriptPurchase:
                    NativeAccountLogManager.EnqueuePasScriptPurchase(request,
                        NativeAccountLogManager.PasScriptPurchaseBeginStage, 0);
                    break;
                default:
                    NativeAccountLogManager.EnqueueMail(request,
                        NativeAccountLogManager.MailBeginStage, 0);
                    break;
            }
            lock (SyncRoot)
            {
                request.CreateContextIdForEnqueue();
                Requests.Enqueue(request);
                NativeAccountLogManager.EnqueueYuanbao(request,
                    request.Operation == AddOperation
                        ? NativeAccountLogManager.RequestAddStage
                        : NativeAccountLogManager.RequestSubtractStage,
                    0);
                if (_workerRunning) return true;
                _workerRunning = true;
                _ = Task.Run(DrainQueue);
            }
            return true;
        }

        internal static void ProcessCompletions()
        {
            while (Completions.TryDequeue(out var completion))
            {
                if (completion.Request.BeforeOuterCompletionLog != null)
                {
                    try
                    {
                        completion.Request.BeforeOuterCompletionLog?.Invoke(
                            completion.Result);
                    }
                    catch (Exception ex)
                    {
                        M2Share.ErrorMessage(
                            "[NativeYuanbao] balance apply failed: " + ex.Message);
                    }
                }
                try
                {
                    switch (completion.Request.Kind)
                    {
                        case NativeYuanbaoRequestKind.YbShop:
                            NativeAccountLogManager.EnqueueShop(completion.Request,
                                NativeAccountLogManager.ShopEndStage,
                                completion.Result.ErrorCode);
                            break;
                        case NativeYuanbaoRequestKind.Script:
                            NativeAccountLogManager.EnqueueScript(completion.Request,
                                NativeAccountLogManager.ScriptEndStage,
                                completion.Result.ErrorCode);
                            break;
                        case NativeYuanbaoRequestKind.PasScriptPurchase:
                            NativeAccountLogManager.EnqueuePasScriptPurchase(
                                completion.Request,
                                NativeAccountLogManager.PasScriptPurchaseEndStage,
                                completion.Result.ErrorCode);
                            break;
                        default:
                            NativeAccountLogManager.EnqueueMail(completion.Request,
                                NativeAccountLogManager.MailEndStage,
                                completion.Result.ErrorCode);
                            break;
                    }
                    completion.Request.Completion(completion.Result);
                }
                catch (Exception ex)
                {
                    M2Share.ErrorMessage("[NativeYuanbao] completion failed: " + ex.Message);
                }
            }
        }

        private static void DrainQueue()
        {
            while (true)
            {
                NativeYuanbaoRequest request;
                lock (SyncRoot)
                {
                    if (!Requests.TryDequeue(out request))
                    {
                        _workerRunning = false;
                        return;
                    }
                }

                NativeYuanbaoResult result;
                try
                {
                    result = NativeYuanbaoStore.Execute(request);
                }
                catch (Exception ex)
                {
                    M2Share.ErrorMessage("[NativeYuanbao] request failed: " + ex.Message);
                    result = new NativeYuanbaoResult(SqlFailure, 0);
                }
                Completions.Enqueue(new Completion(request, result));
            }
        }
    }

    internal static class NativeYuanbaoStore
    {
        internal const string SelectSql =
            "Select UserID, PTID, ChrName, YBNum, LastModifyYBNumTime " +
            "from gamedata.yb_user_data where (UserID = @userId) limit 1;";
        internal const string UpdateSql =
            "Update gamedata.yb_user_data set PTID = @account, ChrName = @characterName, " +
            "YBNum = @balance, LastModifyYBNumTime = Now() where (UserID = @userId);";
        internal const string InsertSql =
            "Insert into gamedata.yb_user_data(" +
            "UserID, PTID, ChrName, YBNum, LastModifyYBNumTime, GHomePayTotal) " +
            "Values(@userId, @account, @characterName, @balance, Now(), 0);";

        internal static NativeYuanbaoResult Execute(NativeYuanbaoRequest request)
        {
            if (request.UserId <= 0)
                return CompleteBeforeWrite(request,
                    NativeYuanbaoManager.InvalidUserId, 0);
            if (request.Amount < 0)
                return CompleteBeforeWrite(request,
                    NativeYuanbaoManager.NegativeAmount, 0);

            var connectionString = M2Share.g_Config?.sConnctionString;
            if (string.IsNullOrWhiteSpace(connectionString))
                return CompleteBeforeWrite(request,
                    NativeYuanbaoManager.SqlFailure, 0);

            var beforeWriteLogged = false;
            try
            {
                using var connection = new MySqlConnection(connectionString);
                connection.Open();

                var exists = false;
                var currentBalance = 0;
                using (var select = connection.CreateCommand())
                {
                    select.CommandText = SelectSql;
                    select.Parameters.Add("@userId", MySqlDbType.Int64).Value = request.UserId;
                    using var reader = select.ExecuteReader();
                    if (reader.Read())
                    {
                        exists = true;
                        currentBalance = reader.IsDBNull(3)
                            ? 0
                            : Convert.ToInt32(reader.GetValue(3));
                    }
                }

                if (!exists && request.Operation == NativeYuanbaoManager.SubtractOperation)
                    return CompleteBeforeWrite(request,
                        NativeYuanbaoManager.InsufficientBalance, 0);

                var error = CalculateBalance(currentBalance, request.Amount,
                    request.Operation, out var balance);
                if (error != 0)
                    return CompleteBeforeWrite(request, error, currentBalance);

                NativeAccountLogManager.EnqueueYuanbao(request,
                    NativeAccountLogManager.BeforeSqlStage, 0);
                beforeWriteLogged = true;

                using var write = connection.CreateCommand();
                write.CommandText = exists ? UpdateSql : InsertSql;
                write.Parameters.Add("@userId", MySqlDbType.Int64).Value = request.UserId;
                AddRawParameter(write, "@account", request.AccountBytes);
                AddRawParameter(write, "@characterName", request.CharacterNameBytes);
                write.Parameters.Add("@balance", MySqlDbType.Int32).Value = balance;
                if (write.ExecuteNonQuery() != 1)
                    return CompleteAfterWrite(request,
                        NativeYuanbaoManager.SqlFailure, currentBalance);

                NativeAccountLogManager.EnqueueYuanbao(request,
                    NativeAccountLogManager.AfterSqlStage, 0);
                return new NativeYuanbaoResult(0, balance);
            }
            catch (Exception ex)
            {
                M2Share.ErrorMessage("[NativeYuanbao] SQL failed: " + ex.Message);
                return beforeWriteLogged
                    ? CompleteAfterWrite(request,
                        NativeYuanbaoManager.SqlFailure, 0)
                    : CompleteBeforeWrite(request,
                        NativeYuanbaoManager.SqlFailure, 0);
            }
        }

        private static NativeYuanbaoResult CompleteBeforeWrite(
            NativeYuanbaoRequest request, int errorCode, int balance)
        {
            NativeAccountLogManager.EnqueueYuanbao(request,
                NativeAccountLogManager.BeforeSqlStage, errorCode);
            return new NativeYuanbaoResult(errorCode, balance);
        }

        private static NativeYuanbaoResult CompleteAfterWrite(
            NativeYuanbaoRequest request, int errorCode, int balance)
        {
            NativeAccountLogManager.EnqueueYuanbao(request,
                NativeAccountLogManager.AfterSqlStage, errorCode);
            return new NativeYuanbaoResult(errorCode, balance);
        }

        internal static int CalculateBalance(int currentBalance, int amount, byte operation,
            out int balance)
        {
            balance = currentBalance;
            if (amount < 0) return NativeYuanbaoManager.NegativeAmount;

            if (operation == NativeYuanbaoManager.AddOperation)
            {
                balance = unchecked(currentBalance + amount);
                return 0;
            }
            if (operation != NativeYuanbaoManager.SubtractOperation)
                return NativeYuanbaoManager.SqlFailure;
            if (amount > currentBalance)
                return NativeYuanbaoManager.InsufficientBalance;

            balance = currentBalance - amount;
            return 0;
        }

        private static void AddRawParameter(MySqlCommand command, string name, byte[] bytes)
        {
            command.Parameters.Add(name, MySqlDbType.VarBinary,
                Math.Max(1, bytes.Length)).Value = bytes;
        }
    }
}
