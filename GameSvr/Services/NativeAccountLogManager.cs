using System.Globalization;
using MySql.Data.MySqlClient;

namespace GameSvr.Services
{
    internal sealed class NativeAccountLogRecord
    {
        internal byte[] ContextIdBytes { get; }
        internal byte[] OrderIdBytes { get; }
        internal long UserId { get; }
        internal byte[] CharacterNameBytes { get; }
        internal byte[] AccountBytes { get; }
        internal byte[] FromWhoBytes { get; }
        internal string PayType { get; }
        internal int ItemIndex { get; }
        internal int ItemCount { get; }
        internal int Amount { get; }
        internal int Result { get; }
        internal string AccountAction { get; }
        internal byte[] DescriptionBytes { get; }

        private NativeAccountLogRecord(NativeYuanbaoRequest request, byte stage,
            int result)
        {
            ContextIdBytes = Array.Empty<byte>();
            OrderIdBytes = Array.Empty<byte>();
            UserId = request.UserId;
            CharacterNameBytes = request.CharacterNameBytes;
            AccountBytes = request.AccountBytes;
            FromWhoBytes = request.ActionBytes;
            PayType = NativeAccountLogManager.MailPayType;
            ItemIndex = 0;
            ItemCount = 0;
            Amount = request.Amount;
            Result = result;
            AccountAction = stage == NativeAccountLogManager.MailBeginStage
                ? NativeAccountLogManager.MailBeginAction
                : NativeAccountLogManager.MailEndAction;
            DescriptionBytes = BuildDescription(request.ActionBytes, stage, result);
        }

        internal static NativeAccountLogRecord CreateMail(
            NativeYuanbaoRequest request, byte stage, int result)
        {
            if (stage is not NativeAccountLogManager.MailBeginStage
                and not NativeAccountLogManager.MailEndStage)
                throw new ArgumentOutOfRangeException(nameof(stage));
            return new NativeAccountLogRecord(request, stage, result);
        }

        private NativeAccountLogRecord(NativeYuanbaoRequest request, byte stage,
            int result, NativeYuanbaoRequestKind kind)
        {
            if (kind == NativeYuanbaoRequestKind.YbShop)
            {
                ContextIdBytes = Array.Empty<byte>();
                OrderIdBytes = System.Text.Encoding.ASCII.GetBytes(
                    request.OrderId.ToString(CultureInfo.InvariantCulture));
                UserId = request.UserId;
                CharacterNameBytes = request.AccountBytes;
                AccountBytes = request.CharacterNameBytes;
                FromWhoBytes = request.CharacterNameBytes;
                PayType = NativeAccountLogManager.ShopPayType;
                ItemIndex = stage == NativeAccountLogManager.ShopBeginStage
                    ? request.GoodsIndex
                    : 0;
                ItemCount = stage == NativeAccountLogManager.ShopBeginStage
                    ? request.GoodsCount
                    : 0;
                Amount = request.Amount;
                Result = result;
                AccountAction = stage == NativeAccountLogManager.ShopBeginStage
                    ? NativeAccountLogManager.ShopBeginAction
                    : NativeAccountLogManager.ShopEndAction;
                DescriptionBytes = stage == NativeAccountLogManager.ShopBeginStage
                    ? SystemModule.HUtil32.GbkEncoding.GetBytes("元宝商城购买")
                    : ConcatGbk("元宝商城购买", request.DescriptorBytes,
                        "(" + NativeYuanbaoManager.GetErrorText(result) + ")");
                return;
            }

            if (kind == NativeYuanbaoRequestKind.PasScriptPurchase)
            {
                ContextIdBytes = Array.Empty<byte>();
                OrderIdBytes = System.Text.Encoding.ASCII.GetBytes(
                    request.ReferenceId.ToString(CultureInfo.InvariantCulture));
                UserId = request.UserId;
                CharacterNameBytes = request.CharacterNameBytes;
                AccountBytes = request.AccountBytes;
                FromWhoBytes = request.ActionBytes;
                PayType = NativeAccountLogManager.PasScriptPurchasePayType;
                ItemIndex = request.GoodsIndex;
                ItemCount = request.GoodsCount;
                Amount = request.Amount;
                Result = result;
                AccountAction = stage ==
                    NativeAccountLogManager.PasScriptPurchaseBeginStage
                        ? NativeAccountLogManager.PasScriptPurchaseBeginAction
                        : NativeAccountLogManager.PasScriptPurchaseEndAction;
                DescriptionBytes = request.DescriptorBytes;
                return;
            }

            if (kind != NativeYuanbaoRequestKind.Script)
                throw new ArgumentOutOfRangeException(nameof(kind));
            ContextIdBytes = Array.Empty<byte>();
            OrderIdBytes = Array.Empty<byte>();
            UserId = stage == NativeAccountLogManager.ScriptBeginStage
                ? -1
                : request.UserId;
            CharacterNameBytes = request.CharacterNameBytes;
            AccountBytes = stage == NativeAccountLogManager.ScriptBeginStage
                ? Array.Empty<byte>()
                : request.AccountBytes;
            FromWhoBytes = stage == NativeAccountLogManager.ScriptBeginStage
                ? request.CharacterNameBytes
                : request.ActionBytes;
            PayType = request.Operation == NativeYuanbaoManager.AddOperation
                ? NativeAccountLogManager.ScriptAddPayType
                : NativeAccountLogManager.ScriptSubtractPayType;
            ItemIndex = 0;
            ItemCount = 0;
            Amount = request.Amount;
            Result = result;
            AccountAction = stage == NativeAccountLogManager.ScriptBeginStage
                ? NativeAccountLogManager.ScriptBeginAction
                : NativeAccountLogManager.ScriptEndAction;
            DescriptionBytes = BuildScriptDescription(request, stage, result);
        }

        internal static NativeAccountLogRecord CreateShop(
            NativeYuanbaoRequest request, byte stage, int result)
        {
            if (stage is not NativeAccountLogManager.ShopBeginStage
                and not NativeAccountLogManager.ShopEndStage)
                throw new ArgumentOutOfRangeException(nameof(stage));
            return new NativeAccountLogRecord(request, stage, result,
                NativeYuanbaoRequestKind.YbShop);
        }

        internal static NativeAccountLogRecord CreateScript(
            NativeYuanbaoRequest request, byte stage, int result)
        {
            if (stage is not NativeAccountLogManager.ScriptBeginStage
                and not NativeAccountLogManager.ScriptEndStage)
                throw new ArgumentOutOfRangeException(nameof(stage));
            return new NativeAccountLogRecord(request, stage, result,
                NativeYuanbaoRequestKind.Script);
        }

        internal static NativeAccountLogRecord CreatePasScriptPurchase(
            NativeYuanbaoRequest request, byte stage, int result)
        {
            if (stage is not NativeAccountLogManager.PasScriptPurchaseBeginStage
                and not NativeAccountLogManager.PasScriptPurchaseEndStage)
                throw new ArgumentOutOfRangeException(nameof(stage));
            return new NativeAccountLogRecord(request, stage, result,
                NativeYuanbaoRequestKind.PasScriptPurchase);
        }

        private NativeAccountLogRecord(NativeYuanbaoRequest request,
            byte stage, int result, bool yuanbao)
        {
            if (!yuanbao || stage > NativeAccountLogManager.AfterSqlStage)
                throw new ArgumentOutOfRangeException(nameof(stage));

            ContextIdBytes = request.ContextIdBytes;
            OrderIdBytes = System.Text.Encoding.ASCII.GetBytes(
                request.OrderId.ToString(CultureInfo.InvariantCulture));
            UserId = request.UserId;
            CharacterNameBytes = request.CharacterNameBytes;
            AccountBytes = request.AccountBytes;
            FromWhoBytes = request.CharacterNameBytes;
            PayType = NativeAccountLogManager.YuanbaoPayType;
            ItemIndex = 0;
            ItemCount = 0;
            Amount = request.Amount;
            Result = result;
            AccountAction = stage switch
            {
                NativeAccountLogManager.RequestAddStage =>
                    NativeAccountLogManager.RequestAddAction,
                NativeAccountLogManager.RequestSubtractStage =>
                    NativeAccountLogManager.RequestSubtractAction,
                NativeAccountLogManager.BeforeSqlStage =>
                    NativeAccountLogManager.BeforeSqlAction,
                _ => NativeAccountLogManager.AfterSqlAction
            };
            DescriptionBytes = SystemModule.HUtil32.GbkEncoding.GetBytes(
                BuildYuanbaoDescription(stage, result));
        }

        internal static NativeAccountLogRecord CreateYuanbao(
            NativeYuanbaoRequest request, byte stage, int result) =>
            new(request, stage, result, true);

        private static string BuildYuanbaoDescription(byte stage, int result)
        {
            if (stage == NativeAccountLogManager.RequestAddStage)
                return "增加元宝请求";
            if (stage == NativeAccountLogManager.RequestSubtractStage)
                return "扣除元宝请求";
            if (stage == NativeAccountLogManager.BeforeSqlStage)
                return result == 0
                    ? "准备修改元宝数"
                    : NativeYuanbaoManager.GetErrorText(result);
            return result == 0 ? "元宝改变" : "元宝修改语句失败";
        }

        private static byte[] BuildDescription(byte[] characterName, byte stage,
            int result)
        {
            var prefix = stage == NativeAccountLogManager.MailBeginStage
                ? "邮件("
                : "领取元宝(";
            var suffix = stage == NativeAccountLogManager.MailBeginStage
                ? ")领取元宝"
                : result == 0 ? ")成功" : ")失败";
            return ConcatGbk(prefix, characterName, suffix);
        }

        private static byte[] BuildScriptDescription(NativeYuanbaoRequest request,
            byte stage, int result)
        {
            var add = request.Operation == NativeYuanbaoManager.AddOperation;
            var suffix = stage == NativeAccountLogManager.ScriptBeginStage
                ? add ? ")请求充值" : ")请求扣费"
                : add
                    ? result == 0 ? ")充值成功" : ")充值失败"
                    : result == 0 ? ")扣费成功" : ")扣费失败";
            var characterName = stage == NativeAccountLogManager.ScriptBeginStage
                ? request.CharacterNameBytes
                : request.ActionBytes;
            return ConcatGbk("脚本(", characterName, suffix);
        }

        private static byte[] ConcatGbk(string prefix, byte[] middle, string suffix)
        {
            var prefixBytes = SystemModule.HUtil32.GbkEncoding.GetBytes(prefix);
            var suffixBytes = SystemModule.HUtil32.GbkEncoding.GetBytes(suffix);
            var result = new byte[prefixBytes.Length + middle.Length + suffixBytes.Length];
            Buffer.BlockCopy(prefixBytes, 0, result, 0, prefixBytes.Length);
            Buffer.BlockCopy(middle, 0, result, prefixBytes.Length, middle.Length);
            Buffer.BlockCopy(suffixBytes, 0, result,
                prefixBytes.Length + middle.Length, suffixBytes.Length);
            return result;
        }
    }

    internal static class NativeAccountLogManager
    {
        internal const byte RequestAddStage = 0;
        internal const byte RequestSubtractStage = 1;
        internal const byte BeforeSqlStage = 2;
        internal const byte AfterSqlStage = 3;
        internal const byte MailBeginStage = 10;
        internal const byte MailEndStage = 11;
        internal const byte ShopBeginStage = 25;
        internal const byte ShopEndStage = 26;
        internal const byte PasScriptPurchaseBeginStage = 27;
        internal const byte PasScriptPurchaseEndStage = 28;
        internal const byte ScriptBeginStage = 29;
        internal const byte ScriptEndStage = 30;
        internal const string MailPayType = "ypMailGetYb";
        internal const string YuanbaoPayType = "ypYBSystem";
        internal const string RequestAddAction = "ltRequestAddYB";
        internal const string RequestSubtractAction = "ltRequestSubYB";
        internal const string BeforeSqlAction = "ltExecSQLBefore";
        internal const string AfterSqlAction = "ltExecSQLFinished";
        internal const string MailBeginAction = "ltMailAddYbBegin";
        internal const string MailEndAction = "ltMailAddYbEnd";
        internal const string ShopPayType = "ypYBShop";
        internal const string ShopBeginAction = "ltYBShopBuyBegin";
        internal const string ShopEndAction = "ltYBShopBuyEnd";
        internal const string PasScriptPurchasePayType = "ypScriptBuy";
        internal const string PasScriptPurchaseBeginAction =
            "ltYBScriptBuyBegin";
        internal const string PasScriptPurchaseEndAction = "ltYBScriptBuyEnd";
        internal const string ScriptAddPayType = "ypScriptModifyAdd";
        internal const string ScriptSubtractPayType = "ypScriptModifySub";
        internal const string ScriptBeginAction = "ltScriptRequestModify";
        internal const string ScriptEndAction = "ltScriptModyfyEnd";
        internal const int MaxInsertAttempts = 20;
        internal const int RetryDelayMilliseconds = 10;
        internal const int DdlRefreshMilliseconds = 60000;

        private static readonly object SyncRoot = new();
        private static readonly Queue<NativeAccountLogRecord> Records = new();
        private static bool _workerRunning;

        internal static void Start()
        {
            lock (SyncRoot)
            {
                if (_workerRunning) return;
                _workerRunning = true;
                _ = Task.Run(Run);
            }
        }

        internal static void EnqueueMail(NativeYuanbaoRequest request, byte stage,
            int result)
        {
            var record = NativeAccountLogRecord.CreateMail(request, stage, result);
            lock (SyncRoot)
            {
                Records.Enqueue(record);
            }
            Start();
        }

        internal static void EnqueueShop(NativeYuanbaoRequest request, byte stage,
            int result)
        {
            var record = NativeAccountLogRecord.CreateShop(request, stage, result);
            lock (SyncRoot)
            {
                Records.Enqueue(record);
            }
            Start();
        }

        internal static void EnqueueScript(NativeYuanbaoRequest request,
            byte stage, int result)
        {
            var record = NativeAccountLogRecord.CreateScript(request, stage, result);
            lock (SyncRoot)
            {
                Records.Enqueue(record);
            }
            Start();
        }

        internal static void EnqueuePasScriptPurchase(
            NativeYuanbaoRequest request, byte stage, int result)
        {
            var record = NativeAccountLogRecord.CreatePasScriptPurchase(
                request, stage, result);
            lock (SyncRoot)
            {
                Records.Enqueue(record);
            }
            Start();
        }

        internal static void EnqueueYuanbao(NativeYuanbaoRequest request,
            byte stage, int result)
        {
            var record = NativeAccountLogRecord.CreateYuanbao(request, stage,
                result);
            lock (SyncRoot)
            {
                Records.Enqueue(record);
            }
            Start();
        }

        private static async Task Run()
        {
            var nextDdlRefresh = DateTime.MinValue;
            while (true)
            {
                if (DateTime.UtcNow >= nextDdlRefresh)
                {
                    NativeAccountLogStore.EnsureCurrentTable(DateTime.Now);
                    nextDdlRefresh = DateTime.UtcNow.AddMilliseconds(
                        DdlRefreshMilliseconds);
                }

                NativeAccountLogRecord record = null;
                lock (SyncRoot)
                {
                    if (Records.Count != 0) record = Records.Dequeue();
                }

                if (record == null)
                {
                    await Task.Delay(RetryDelayMilliseconds).ConfigureAwait(false);
                    continue;
                }

                for (var attempt = 1; attempt <= MaxInsertAttempts; attempt++)
                {
                    if (NativeAccountLogStore.TryInsert(record, DateTime.Now)) break;
                    if (attempt == MaxInsertAttempts) break;
                    await Task.Delay(RetryDelayMilliseconds).ConfigureAwait(false);
                }
            }
        }
    }

    internal static class NativeAccountLogStore
    {
        internal const string PayTypeEnumSql =
            "'ypYBSystem','ypGHomePay','ypConsignment','ypGMModifyAdd'," +
            "'ypGMModifySub','ypStrengthenEquip','ypYBShop','ypStallBuyItem'," +
            "'ypMailGetYb','ypScriptBuy','ypScriptModifyAdd','ypScriptModifySub'";
        internal const string AccountActionEnumSql =
            "'ltRequestAddYB','ltRequestSubYB','ltExecSQLBefore'," +
            "'ltExecSQLFinished','ltGHomePayEnd','ltGHomePayStart'," +
            "'ltGMRequestModify','ltGMModyfyEnd','ltStallSubYbBegin'," +
            "'ltStallSubYbEnd','ltMailAddYbBegin','ltMailAddYbEnd'," +
            "'ltConsignmentbegin','ltRequestSubBuyerYB'," +
            "'ltRequestAddSellerYB','ltGiveItemsSucess','ltGiveItemsFail'," +
            "'ltConsignmentEnd','ltTimeOutBegin','ltRequestSubPlayerYB'," +
            "'ltGetBackItemsSucess','ltGetBackItemsFail','ltTimeOutEnd'," +
            "'ltStrengthenEquipBegin','ltStrengthenEquipEnd'," +
            "'ltYBShopBuyBegin','ltYBShopBuyEnd','ltYBScriptBuyBegin'," +
            "'ltYBScriptBuyEnd','ltScriptRequestModify','ltScriptModyfyEnd'";
        internal const string CreateTableSqlFormat =
            "Create Table if not Exists gamelog.{0}(idx int not null AUTO_INCREMENT PRIMARY KEY, UpdateTime DateTime not null, Context_Id Char(32) not null,Order_Id Char(32) not null,UserID bigint(20) NOT NULL default 0,PTID varchar(20) character set latin1 collate latin1_bin NOT NULL default '',ChrName char(15) character set latin1 collate latin1_bin NOT NULL default '',FromWho char(15) character set latin1 collate latin1_bin NOT NULL default '',Pay_Type Enum({1}),ItemIdx int,ItemNum int,amount int, nResult int,AccountAct Enum({2}),ActDesc varchar(255) character set latin1 collate latin1_bin NOT NULL default '',index Query1_index(UserID, Pay_Type, nResult, UpdateTime), index Query2_index(Context_Id, Order_Id, Pay_Type, nResult, AccountAct), index Time_Index(UpdateTime)) Max_ROWS=20000000000;";
        internal const string NativeInsertSqlFormat =
            "insert into gamelog.%s(UpdateTime, Context_Id, Order_Id, UserID, ChrName, PTID, FromWho,Pay_Type, ItemIdx, ItemNum, amount, nResult, AccountAct, ActDesc) values(Now(),\"%s\",\"%s\", %d, \"%s\", \"%s\", \"%s\", \"%s\", %d, %d, %d, %d, \"%s\", \"%s\");";
        internal const string InsertSqlFormat =
            "insert into gamelog.{0}(UpdateTime, Context_Id, Order_Id, UserID, ChrName, PTID, FromWho,Pay_Type, ItemIdx, ItemNum, amount, nResult, AccountAct, ActDesc) values(Now(),@contextId,@orderId,@userId,@characterName,@account,@fromWho,@payType,@itemIndex,@itemCount,@amount,@result,@accountAction,@description);";
        internal const string ShowColumnSqlFormat =
            "Show COLUMNS From gamelog.{0} like '{1}';";
        internal const string AlterEnumColumnSqlFormat =
            "Alter Table gamelog.{0} MODIFY COLUMN {1} Enum({2});";

        private static string _currentTableName = string.Empty;
        private static DateTime _lastFailureReportUtc = DateTime.MinValue;

        internal static string BuildTableName(DateTime value) =>
            "AccountLog" + value.ToString("yyyyMM", CultureInfo.InvariantCulture);

        internal static string BuildCreateTableSql(string tableName) =>
            string.Format(CultureInfo.InvariantCulture, CreateTableSqlFormat,
                tableName, PayTypeEnumSql, AccountActionEnumSql);

        internal static string BuildShowColumnSql(string tableName,
            string columnName) =>
            string.Format(CultureInfo.InvariantCulture, ShowColumnSqlFormat,
                tableName, columnName);

        internal static string BuildAlterEnumColumnSql(string tableName,
            string columnName, string enumSql) =>
            string.Format(CultureInfo.InvariantCulture, AlterEnumColumnSqlFormat,
                tableName, columnName, enumSql);

        internal static bool EnsureCurrentTable(DateTime now)
        {
            var tableName = BuildTableName(now);
            var connectionString = M2Share.g_Config?.sConnctionString;
            if (string.IsNullOrWhiteSpace(connectionString)) return false;

            try
            {
                using var connection = new MySqlConnection(connectionString);
                connection.Open();
                using var command = connection.CreateCommand();
                command.CommandText = BuildCreateTableSql(tableName);
                command.ExecuteNonQuery();
                EnsureEnumColumn(connection, tableName, "Pay_Type", PayTypeEnumSql);
                EnsureEnumColumn(connection, tableName, "AccountAct",
                    AccountActionEnumSql);
                _currentTableName = tableName;
                return true;
            }
            catch (Exception ex)
            {
                ReportFailure(tableName, ex.Message);
                return false;
            }
        }

        internal static bool TryInsert(NativeAccountLogRecord record, DateTime now)
        {
            var tableName = _currentTableName;
            if (string.IsNullOrEmpty(tableName))
            {
                if (!EnsureCurrentTable(now)) return false;
                tableName = _currentTableName;
            }

            var connectionString = M2Share.g_Config?.sConnctionString;
            if (string.IsNullOrWhiteSpace(connectionString)) return false;

            try
            {
                using var connection = new MySqlConnection(connectionString);
                connection.Open();
                using var command = connection.CreateCommand();
                command.CommandText = string.Format(CultureInfo.InvariantCulture,
                    InsertSqlFormat, tableName);
                AddRawParameter(command, "@contextId", record.ContextIdBytes);
                AddRawParameter(command, "@orderId", record.OrderIdBytes);
                command.Parameters.Add("@userId", MySqlDbType.Int64).Value = record.UserId;
                AddRawParameter(command, "@characterName", record.CharacterNameBytes);
                AddRawParameter(command, "@account", record.AccountBytes);
                AddRawParameter(command, "@fromWho", record.FromWhoBytes);
                command.Parameters.Add("@payType", MySqlDbType.VarChar).Value =
                    record.PayType;
                command.Parameters.Add("@itemIndex", MySqlDbType.Int32).Value =
                    record.ItemIndex;
                command.Parameters.Add("@itemCount", MySqlDbType.Int32).Value =
                    record.ItemCount;
                command.Parameters.Add("@amount", MySqlDbType.Int32).Value = record.Amount;
                command.Parameters.Add("@result", MySqlDbType.Int32).Value = record.Result;
                command.Parameters.Add("@accountAction", MySqlDbType.VarChar).Value =
                    record.AccountAction;
                AddRawParameter(command, "@description", record.DescriptionBytes);
                if (command.ExecuteNonQuery() == 1) return true;
                ReportFailure(tableName, "INSERT affected no row");
                return false;
            }
            catch (Exception ex)
            {
                ReportFailure(tableName, ex.Message);
                return false;
            }
        }

        private static void EnsureEnumColumn(MySqlConnection connection,
            string tableName, string columnName, string enumSql)
        {
            string currentType;
            using (var command = connection.CreateCommand())
            {
                command.CommandText = BuildShowColumnSql(tableName, columnName);
                using var reader = command.ExecuteReader();
                currentType = reader.Read()
                    ? Convert.ToString(reader.GetValue(1),
                        CultureInfo.InvariantCulture) ?? string.Empty
                    : string.Empty;
            }

            var expectedType = "enum(" + enumSql + ")";
            if (string.Equals(currentType, expectedType,
                    StringComparison.Ordinal)) return;

            using var alter = connection.CreateCommand();
            alter.CommandText = BuildAlterEnumColumnSql(tableName, columnName,
                enumSql);
            alter.ExecuteNonQuery();
        }

        private static void ReportFailure(string tableName, string message)
        {
            var now = DateTime.UtcNow;
            if (now - _lastFailureReportUtc < TimeSpan.FromSeconds(60)) return;
            _lastFailureReportUtc = now;
            M2Share.ErrorMessage(
                $"[NativeAccountLog] SQL failed: gamelog.{tableName}: {message}");
        }

        private static void AddRawParameter(MySqlCommand command, string name,
            byte[] bytes)
        {
            command.Parameters.Add(name, MySqlDbType.VarBinary,
                Math.Max(1, bytes.Length)).Value = bytes;
        }
    }
}
