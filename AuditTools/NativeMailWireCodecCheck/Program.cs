using System.Buffers.Binary;
using System.Reflection;
using System.Text;
using GameSvr.Services;
using SystemModule;

Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
PrepareRuntimeConfig();
var gbk = Encoding.GetEncoding(936);

CheckListInfo();
CheckMailInfo();
CheckMailMessage();
CheckAttachmentRoundTrip();
CheckRecipientIdentity();
CheckNativeMailSchemaSourceContract();
CheckNativeMailboxSourceContract();
CheckNativeMailCreationSourceContract();
CheckNativeMailClaimDeleteSourceContract();

Console.WriteLine("NativeMailWireCodecCheck PASS");

static void PrepareRuntimeConfig()
{
    var runtimeDirectory = AppContext.BaseDirectory;
    File.WriteAllText(Path.Combine(runtimeDirectory, "!Setup.txt"),
        "[Server]" + Environment.NewLine);
    File.WriteAllText(Path.Combine(runtimeDirectory, "String.ini"),
        "[String]" + Environment.NewLine);
    File.WriteAllText(Path.Combine(runtimeDirectory, "Command.conf"),
        "[Command]" + Environment.NewLine);

    var shareDirectory = Path.GetFullPath(Path.Combine(
        runtimeDirectory, "..", "Share"));
    Directory.CreateDirectory(shareDirectory);
    File.WriteAllText(Path.Combine(shareDirectory, "PlayerUpgradeExp.ini"),
        "[PlayerLevelExp]" + Environment.NewLine);
    File.WriteAllText(Path.Combine(shareDirectory, "ServerData.ini"),
        "[Integer]" + Environment.NewLine);
}

void CheckListInfo()
{
    const long timeBits = 0x400921FB54442D18;
    var value = new NativeMailListInfo(
        0x10203040,
        "一二三四五六七八九十甲",
        "系统邮件",
        2,
        1,
        BitConverter.Int64BitsToDouble(timeBits));

    var raw = NativeMailWireCodec.Encode(value);
    Equal(56, raw.Length, "TMailListInfo size");
    Equal(0x10203040, ReadInt32(raw, 0), "TMailListInfo.id +0");
    BytesEqual(gbk.GetBytes("一二三四五六七八九十"), raw.AsSpan(4, 20),
        "TMailListInfo.title GBK truncation +4");
    Equal((byte)0, raw[24], "TMailListInfo.title terminator +24");
    BytesEqual(gbk.GetBytes("系统邮件"), raw.AsSpan(25, 8),
        "TMailListInfo.sender +25");
    AllZero(raw.AsSpan(33, 7), "TMailListInfo.sender NUL padding");
    Equal(2, ReadInt32(raw, 40), "TMailListInfo.mailState +40");
    Equal(1, ReadInt32(raw, 44), "TMailListInfo.attachState +44");
    Equal(timeBits, ReadInt64(raw, 48), "TMailListInfo.time bits +48");
}

void CheckMailInfo()
{
    const long timeBits = unchecked((long)0xC024000000000001);
    var context = string.Concat(Enumerable.Repeat("中", 101));
    var value = new NativeMailInfo(
        -7,
        "发件人甲乙丙丁",
        "标题甲",
        context,
        2,
        1,
        4,
        BitConverter.Int64BitsToDouble(timeBits),
        123456,
        654321,
        3,
        unchecked((int)0x89ABCDEF));

    var raw = NativeMailWireCodec.Encode(value);
    Equal(280, raw.Length, "TMailInfo size");
    Equal(-7, ReadInt32(raw, 0), "TMailInfo.id +0");
    BytesEqual(gbk.GetBytes("发件人甲乙丙丁"), raw.AsSpan(4, 14),
        "TMailInfo.sender GBK boundary +4");
    Equal((byte)0, raw[18], "TMailInfo.sender terminator +18");
    BytesEqual(gbk.GetBytes("标题甲"), raw.AsSpan(19, 6),
        "TMailInfo.title +19");
    AllZero(raw.AsSpan(25, 15), "TMailInfo.title NUL padding");
    BytesEqual(gbk.GetBytes(string.Concat(Enumerable.Repeat("中", 100))),
        raw.AsSpan(40, 200), "TMailInfo.context GBK truncation +40");
    Equal((byte)0, raw[240], "TMailInfo.context terminator +240");
    AllZero(raw.AsSpan(241, 3), "TMailInfo alignment padding +241..243");
    Equal(2, ReadInt32(raw, 244), "TMailInfo.mailState +244");
    Equal(1, ReadInt32(raw, 248), "TMailInfo.attachState +248");
    Equal(4, ReadInt32(raw, 252), "TMailInfo.type +252");
    Equal(timeBits, ReadInt64(raw, 256), "TMailInfo.time bits +256");
    Equal(123456, ReadInt32(raw, 264), "TMailInfo.gold +264");
    Equal(654321, ReadInt32(raw, 268), "TMailInfo.yb +268");
    Equal(3, ReadInt32(raw, 272), "TMailInfo.cnt +272");
    Equal(unchecked((int)0x89ABCDEF), ReadInt32(raw, 276),
        "TMailInfo.mark +276");
}

void CheckMailMessage()
{
    const long timeBits = 0x3FF0000000000001;
    var value = new NativeMailMessage(
        "甲乙丙丁戊己庚辛",
        BitConverter.Int64BitsToDouble(timeBits),
        string.Concat(Enumerable.Repeat("文", 26)));

    var raw = NativeMailWireCodec.Encode(value);
    Equal(80, raw.Length, "TMailMsg size");
    BytesEqual(gbk.GetBytes("甲乙丙丁戊己庚"), raw.AsSpan(0, 14),
        "TMailMsg.name GBK truncation +0");
    Equal((byte)0, raw[14], "TMailMsg.name terminator +14");
    Equal((byte)0, raw[15], "TMailMsg alignment padding +15");
    Equal(timeBits, ReadInt64(raw, 16), "TMailMsg.time bits +16");
    BytesEqual(gbk.GetBytes(string.Concat(Enumerable.Repeat("文", 25))),
        raw.AsSpan(24, 50), "TMailMsg.msg GBK truncation +24");
    Equal((byte)0, raw[74], "TMailMsg.msg terminator +74");
    AllZero(raw.AsSpan(75, 5), "TMailMsg tail padding +75..79");
}

void CheckAttachmentRoundTrip()
{
    var codec = typeof(NativeMailWireCodec).Assembly.GetType(
        "GameSvr.Services.NativeMailAttachmentCodec", throwOnError: true)!;
    var decode = codec.GetMethod("TryDecode", BindingFlags.Static | BindingFlags.NonPublic)!;
    var encode = codec.GetMethod("TryEncode", BindingFlags.Static | BindingFlags.NonPublic)!;
    var normalize = codec.GetMethod("NormalizeRecord", BindingFlags.Static | BindingFlags.NonPublic)!;

    var original = new byte[208];
    BinaryPrimitives.WriteInt32LittleEndian(original.AsSpan(0, 4), 0x10203040);
    BinaryPrimitives.WriteUInt16LittleEndian(original.AsSpan(4, 2), 321);
    BinaryPrimitives.WriteUInt16LittleEndian(original.AsSpan(6, 2), 1200);
    BinaryPrimitives.WriteUInt16LittleEndian(original.AsSpan(8, 2), 2400);
    for (var i = 0; i < 14; i++) original[10 + i] = (byte)(i + 1);
    original[0x27] = 0xC5;
    original[0x70] = 0x5A;
    original[0xB8] = 7;
    original[0xCF] = 0xA5;

    object[] decodeArgs = { original, null!, null! };
    Equal(true, (bool)decode.Invoke(null, decodeArgs)!, "mail attachment decode");
    var item = (TUserItem)decodeArgs[1];
    Equal(0x10203040, item.MakeIndex, "mail attachment make index");
    Equal((ushort)321, item.wIndex, "mail attachment item index");
    Equal((byte)0xC5, item.UpgradeFlags, "mail attachment refine flags");
    Equal((byte)7, item.Bind, "mail attachment bind");
    BytesEqual(original, item.NativeRecord, "mail attachment opaque decode copy");

    item.Dura = 1300;
    object[] encodeArgs = { item, null!, null! };
    Equal(true, (bool)encode.Invoke(null, encodeArgs)!, "mail attachment encode");
    var encoded = (byte[])encodeArgs[1];
    Equal(208, encoded.Length, "mail attachment encoded size");
    Equal((ushort)1300,
        BinaryPrimitives.ReadUInt16LittleEndian(encoded.AsSpan(6, 2)),
        "mail attachment patched durability");
    Equal((byte)0x5A, encoded[0x70], "mail attachment unknown middle byte");
    Equal((byte)0xA5, encoded[0xCF], "mail attachment unknown tail byte");

    var shortRecord = (byte[])normalize.Invoke(null, new object[] { original[..37] })!;
    Equal(208, shortRecord.Length, "short mail attachment padded size");
    BytesEqual(original.AsSpan(0, 37), shortRecord.AsSpan(0, 37),
        "short mail attachment prefix");
    AllZero(shortRecord.AsSpan(37), "short mail attachment zero padding");

    var oversized = new byte[240];
    for (var i = 0; i < oversized.Length; i++) oversized[i] = (byte)i;
    var truncated = (byte[])normalize.Invoke(null, new object[] { oversized })!;
    Equal(208, truncated.Length, "oversized mail attachment truncated size");
    BytesEqual(oversized.AsSpan(0, 208), truncated,
        "oversized mail attachment first 208 bytes");
}

void CheckRecipientIdentity()
{
    var sourcePath = FindRepositoryFile("GameSvr", "Services", "NativeMailStore.cs");
    var source = File.ReadAllText(sourcePath);
    if (!source.Contains("SELECT UserId FROM mir3.user_index ",
            StringComparison.Ordinal))
    {
        throw new InvalidOperationException(
            "native mail recipient lookup must use the 64-bit user_index.UserId");
    }
    if (source.Contains("SELECT idx FROM mir3.user_index ",
            StringComparison.OrdinalIgnoreCase))
    {
        throw new InvalidOperationException(
            "native mail recipient lookup must not use user_index.idx");
    }
}

void CheckNativeMailSchemaSourceContract()
{
    var storeType = typeof(NativeMailWireCodec).Assembly.GetType(
        "GameSvr.Services.NativeMailStore", throwOnError: true)!;
    var schemaField = storeType.GetField(
        "NativeSchemaStatements", BindingFlags.Static | BindingFlags.NonPublic)!;
    var actual = (string[])schemaField.GetValue(null)!;
    string[] expected =
    {
        "CREATE TABLE IF NOT EXISTS gamedata.mailitem(idx int not null AUTO_INCREMENT PRIMARY KEY,sendId bigint not null,sendName char(20) not null,recvId bigint not null,recvName char(20) binary not null,title char(100) binary not null,context char(200) binary not null,mailType tinyint(1) not null,mailstatus tinyint(1) not null,attachstatus tinyint(1) not null,moneyType tinyint(1) not null,moneyCount int not null default 0,attachNum int not null,sendtime datetime,recvtime datetime,modifyDate datetime,createDate datetime not null);",
        "CREATE TABLE IF NOT EXISTS gamedata.attachitem(idx int not null AUTO_INCREMENT PRIMARY KEY,mailId int not null,data blob,modifydate datetime,createDate datetime not null);",
        "CREATE TABLE if not exists gamedata.mailitem_b like gamedata.mailitem;",
        "CREATE TABLE if not exists gamedata.attachitem_b like gamedata.attachitem;",
        "CREATE TABLE IF NOT EXISTS gamedata.Money_order(idx int not null AUTO_INCREMENT PRIMARY KEY,sendId bigint not null,sendName char(20) not null,recvId bigint not null,recvName char(20) binary not null,title char(100) binary not null,context char(200) binary not null,mailType tinyint(1) not null,mailstatus tinyint(1) not null,attachstatus tinyint(1) not null,moneyType tinyint(1) not null,moneyCount int not null default 0,attachNum int not null,sendtime datetime,recvtime datetime,moneyStatus tinyint(1) not null,modifyDate datetime,createDate datetime not null);"
    };

    Equal(expected.Length, actual.Length, "native mail schema statement count");
    for (var i = 0; i < expected.Length; i++)
        Equal(expected[i], actual[i], $"native mail DDL #{i + 1}");
}

void CheckNativeMailboxSourceContract()
{
    var store = File.ReadAllText(
        FindRepositoryFile("GameSvr", "Services", "NativeMailStore.cs"))
        .Replace("\r\n", "\n", StringComparison.Ordinal);
    var player = File.ReadAllText(
        FindRepositoryFile("GameSvr", "Players", "TPlayObject.Mail.cs"))
        .Replace("\r\n", "\n", StringComparison.Ordinal);
    var cache = File.ReadAllText(
        FindRepositoryFile("GameSvr", "Services", "NativeMailCacheService.cs"))
        .Replace("\r\n", "\n", StringComparison.Ordinal);

    Require(store, "private const int LoadPageSize = 100;",
        "native mail loader must retain the 100-row cursor page size");
    Require(store, "AND mailstatus=@mailStatus",
        "native mail loader must query one status at a time");
    Require(store, "AND idx>@cursor",
        "native mail loader must advance by mail idx");
    Require(store, "ORDER BY idx LIMIT {LoadPageSize}",
        "native mail loader must preserve idx order");
    Require(store, "tag, 1,\n                            requireAttachment: false",
        "native mail loader must load status 1 first without attachment filtering");
    Require(store, "tag, 2,\n                            requireAttachment: tag == 5",
        "only tag 5 status 2 loading may require attachstatus=1");
    Forbid(store, "ORDER BY mailstatus",
        "mail lists must not bypass native cache ordering");
    Forbid(store, "mailstatus IN (1,2)",
        "mail statuses must not be merged into a real-time query");

    Require(player, "NativeMailCacheService.TryFind(recipientId, tag, mailId, out var entry)",
        "4461 must resolve mail from the global UserId mailbox cache");
    Require(player, "NativeMailCacheService.TryGetCategory(recipientId, tag, out records)",
        "mail lists must reuse a fully-loaded global cache category");
    Require(player, "NativeMailCacheService.MergeLoadedStatus(",
        "lazy status loads must merge into the global mailbox cache");
    Require(cache, "Dictionary<long, NativeMailbox>",
        "native mailbox cache must be global and keyed by 64-bit UserId");
    Require(cache, "internal static bool TryFind(long recipientId, int tag, int mailId,",
        "global mailbox cache must own lookup by UserId/tag/mailId");
    Require(cache, "internal static NativeMailCacheEntry Register(long recipientId, string recipientName,",
        "new native mail must register in the global UserId cache");
    Forbid(player, "_nativeMailCategories",
        "per-player replacement mail caches must not return");
    Require(player, "entry.Attachments.FirstOrDefault()",
        "tag 4 must best-effort append the cached first attachment");
    Require(player, "tag == 1 ? 21 : tag == 5 ? 1 : 20",
        "native response limits must remain 21/20/1/20");
    Require(player, "EncodeOwnedClientItemRecord(attachment)",
        "mail attachments must reuse their cached session-local id");
    Forbid(player, "TryReadDetail",
        "4461 must never re-query a live mail row");
    Forbid(player, "TryReadAttachments",
        "mail responses must never reload attachment objects");
}

void CheckNativeMailCreationSourceContract()
{
    var service = File.ReadAllText(
        FindRepositoryFile("GameSvr", "Services", "MailService.cs"))
        .Replace("\r\n", "\n", StringComparison.Ordinal);
    var store = File.ReadAllText(
        FindRepositoryFile("GameSvr", "Services", "NativeMailStore.cs"))
        .Replace("\r\n", "\n", StringComparison.Ordinal);
    var player = File.ReadAllText(
        FindRepositoryFile("GameSvr", "Players", "TPlayObject.Mail.cs"))
        .Replace("\r\n", "\n", StringComparison.Ordinal);
    var messages = File.ReadAllText(
        FindRepositoryFile("GameSvr", "Players", "TPlayObject.Message.cs"))
        .Replace("\r\n", "\n", StringComparison.Ordinal);
    var lifecycle = File.ReadAllText(
        FindRepositoryFile("GameSvr", "Players", "TPlayObject.MailLifecycle.cs"))
        .Replace("\r\n", "\n", StringComparison.Ordinal);
    var bridge = File.ReadAllText(
        FindRepositoryFile("GameSvr", "ScriptSystem", "PasEngine", "PasApiBridge.cs"))
        .Replace("\r\n", "\n", StringComparison.Ordinal);

    Require(service,
        "InsertAttachmentBestEffort(connection, mailId, attachmentRecord, createdAt) <= 0",
        "mail attachment failures must skip one item instead of aborting NewFullMailEx");
    Require(service, "UpdateSummaryBestEffort(connection, mailId, moneyType, moneyCount,",
        "mail summary writes must retain native best-effort behavior");
    Require(service, "MySqlDbType.Int32).Value = moneyType",
        "native moneyType must retain its 32-bit value");
    Forbid(service, "MySqlDbType.Byte).Value = (byte)moneyType",
        "native moneyType must not be truncated to a byte");
    Require(service, "NativeMailCacheService.Register(",
        "newly-created mail must register in the global native mailbox cache");
    Require(service, "HUtil32.GbkEncoding.GetString(bytes, 0, 15)",
        "NewFullMailEx item names must use the native 15-byte ShortString boundary");
    Require(store, "internal int MoneyType { get; set; }",
        "loaded native moneyType must remain 32-bit");
    Require(player, "if (tag == 1) TriggerNativeMailQuest();",
        "4460 tag 1 must invoke PlayerCheckNewMail after its response");
    Require(messages, "case Grobal2.CM_SYSTEM_NEWMAIL:\n                    TriggerNativeMailQuest();",
        "client 4464 must invoke PlayerCheckNewMail");
    Require(lifecycle, "\"RunMailQuest\", \"@PlayerCheckNewMail\", this",
        "mail lifecycle must dispatch the configured RunMailQuest procedure");
    Forbid(bridge, "result = PasValue.FromBool(mailCreated);",
        "NewFullMailEx is a Pascal procedure, not a Boolean function");
}

void CheckNativeMailClaimDeleteSourceContract()
{
    var store = File.ReadAllText(
        FindRepositoryFile("GameSvr", "Services", "NativeMailStore.cs"))
        .Replace("\r\n", "\n", StringComparison.Ordinal);
    var player = File.ReadAllText(
        FindRepositoryFile("GameSvr", "Players", "TPlayObject.Mail.cs"))
        .Replace("\r\n", "\n", StringComparison.Ordinal);
    var messages = File.ReadAllText(
        FindRepositoryFile("GameSvr", "Players", "TPlayObject.Message.cs"))
        .Replace("\r\n", "\n", StringComparison.Ordinal);

    Require(messages,
        "case Grobal2.CM_FETCH_ATTACH:\n                    ClientFetchNativeMailAttachments(ProcessMsg.nParam1, ProcessMsg.nParam2);",
        "4462 must dispatch Recog as mailId and Param as tag");
    Require(messages,
        "case Grobal2.CM_DEL_MAIL:\n                    ClientDeleteNativeMail(ProcessMsg.nParam1, ProcessMsg.nParam2);",
        "4463 must dispatch Recog as mailId and Param as tag");
    Require(messages,
        "case Grobal2.CM_FETCH_ATTACH_OFFTM:\n                    ClientFetchNativeMailAttachmentsOffline(ProcessMsg.nParam1);",
        "4468 must dispatch Recog as mailId without accepting a request tag");
    Require(messages,
        "case Grobal2.CM_CLEAR_ALLMAIL:\n                    ClientClearAllNativeMail(ProcessMsg.nParam2);",
        "4495 must dispatch Param as the category tag");
    Require(player, "SendDefMessage(Grobal2.SM_FETCH_ATTACH, -5, 0, 0, 0",
        "4462 must return -5 outside a safe zone");
    Require(player, "if (record.AttachStatus == 2) return -2;",
        "4462 must reject already-claimed mail");
    Require(player,
        "entry.Attachments.Count > BagCapacity.Of(this) - m_ItemList.Count",
        "4462 must reserve one bag slot per attachment before granting");
    Require(player, "(long)m_nGold + record.MoneyCount > m_nGoldMax",
        "4462 must enforce the player's native gold ceiling");
    Require(player, "NativeMailStore.SetMoneyOrderStatusBestEffort(orderId, 2);",
        "failed YuanBao claims must close the native money order as failed");
    Require(player, "if (NativeYuanbaoManager.Enqueue(request)) return 0;",
        "accepted YuanBao claims must return zero while the native callback is pending");
    Require(player,
        "result => CompleteNativeMailYuanbaoClaim(recipientId, entry,",
        "YuanBao claims must retain the mail entry for callback completion");
    Forbid(player, "m_nGameGold += record.MoneyCount",
        "4462 must not replace the native YuanBao account callback with local GameGold");
    Require(player, "SetNativeMailAttachStatus(entry.Record, 2);",
        "successful claims must mark the cached mail claimed before SQL best effort");
    var createOrder = player.IndexOf(
        "NativeMailStore.CreateMoneyOrderBestEffort(record, m_sCharName)",
        StringComparison.Ordinal);
    var branchOnMoneyType = player.IndexOf("if (record.MoneyType == 1)",
        StringComparison.Ordinal);
    var markOrderSucceeded = player.IndexOf(
        "NativeMailStore.SetMoneyOrderStatusBestEffort(orderId, 1);",
        StringComparison.Ordinal);
    var deliverAttachments = player.IndexOf("return DeliverNativeMailAttachments(entry);",
        StringComparison.Ordinal);
    if (!(createOrder >= 0 && createOrder < branchOnMoneyType
          && branchOnMoneyType < markOrderSucceeded
          && markOrderSucceeded < deliverAttachments))
    {
        throw new InvalidOperationException(
            "4462 must create the audit order before currency dispatch and close it before delivery");
    }

    var claim = SliceSource(player,
        "private int FetchNativeMailAttachments(NativeMailCacheEntry entry)",
        "private void CompleteNativeMailYuanbaoClaim(long recipientId,");
    var enqueue = claim.IndexOf("NativeYuanbaoManager.Enqueue(request)",
        StringComparison.Ordinal);
    var pendingReturn = claim.IndexOf("return 0;", enqueue, StringComparison.Ordinal);
    if (!(enqueue >= 0 && pendingReturn > enqueue))
        throw new InvalidOperationException(
            "4462 YuanBao dispatch must enqueue before returning the pending result zero");

    var callback = SliceSource(player,
        "private void CompleteNativeMailYuanbaoClaim(long recipientId,",
        "private TPlayObject ResolveNativeMailClaimPlayer()");
    var callbackFailure = callback.IndexOf("if (result.ErrorCode != 0)",
        StringComparison.Ordinal);
    var markOrderFailed = callback.IndexOf(
        "NativeMailStore.SetMoneyOrderStatusBestEffort(orderId, 2);",
        callbackFailure, StringComparison.Ordinal);
    var replyFailed = callback.IndexOf(
        "Grobal2.SM_FETCH_ATTACH, -4", markOrderFailed, StringComparison.Ordinal);
    if (!(callbackFailure >= 0 && markOrderFailed > callbackFailure
          && replyFailed > markOrderFailed))
        throw new InvalidOperationException(
            "failed YuanBao callbacks must mark order 2 before replying -4");

    var refreshBalance = callback.IndexOf(
        "online.m_nGameGold = result.Balance;", StringComparison.Ordinal);
    var callbackSucceeded = callback.IndexOf(
        "NativeMailStore.SetMoneyOrderStatusBestEffort(orderId, 1);",
        StringComparison.Ordinal);
    var callbackDelivery = callback.IndexOf(
        "claimResult = online.DeliverNativeMailAttachments(entry);",
        StringComparison.Ordinal);
    var callbackReply = callback.LastIndexOf(
        "online?.SendDefMessage(", StringComparison.Ordinal);
    if (!(refreshBalance >= 0 && refreshBalance < callbackSucceeded
          && callbackSucceeded < callbackDelivery
          && callbackDelivery < callbackReply))
        throw new InvalidOperationException(
            "successful YuanBao callbacks must refresh, mark order 1, deliver, then reply");
    Require(callback,
        "Grobal2.SM_FETCH_ATTACH, claimResult, 0, 0, 0, string.Empty",
        "all asynchronous YuanBao completions must use the native 4462 response");
    Forbid(callback, "SM_FETCH_ATTACH_OFFTM",
        "a 4468 request that returned zero must still complete through native 4462");
    Require(player,
        "HUtil32.HiWord(mailId), HUtil32.LoWord(mailId), string.Empty);",
        "4463 must echo the deleted mail id in Tag/Series");
    Require(player,
        "NativeMailStore.ArchiveAndDeleteBestEffort(mailId);\n                NativeMailCacheService.TryRemove(recipientId, tag, mailId, out _);",
        "4463 must remove the cached mail after native best-effort archival");

    var offlineClaim = SliceSource(player,
        "private void ClientFetchNativeMailAttachmentsOffline(int mailId)",
        "private void ClientClearAllNativeMail(int tag)");
    Require(offlineClaim,
        "NativeMailCacheService.TryFind(\n                    recipientId, 5, mailId, out var entry)",
        "4468 must search the already-loaded hard-coded tag-5 cache");
    Require(offlineClaim, "if (result != 0)",
        "4468 must remain silent while the native async result is zero");
    Require(offlineClaim,
        "Grobal2.SM_FETCH_ATTACH_OFFTM, result, 0, 0, 0, string.Empty",
        "4468 response fields must match the native zeroed header");
    Forbid(offlineClaim, "InSafeZone()",
        "4468 must not inherit the 4462 safe-zone gate");

    var clearAll = SliceSource(player,
        "private void ClientClearAllNativeMail(int tag)",
        "private int FetchNativeMailAttachments(NativeMailCacheEntry entry)");
    Require(clearAll,
        "!NativeMailCacheService.ContainsMailbox(recipientId))\n                return;",
        "4495 must send no response when the global mailbox is absent");
    Require(clearAll, "var result = -1;",
        "4495 must return -1 for an existing mailbox with no eligible mail");
    Require(clearAll, "for (var i = entries.Count - 1; i >= 0; i--)",
        "4495 must scan the category from end to beginning");
    Require(clearAll,
        "if (record.MailStatus != 2 || record.AttachStatus is not 2 and not 3)\n                        continue;",
        "4495 must delete only read mail whose attachments are claimed or absent");
    Require(clearAll,
        "NativeMailStore.ArchiveAndDeleteBestEffort(record.Id);",
        "4495 must reuse native best-effort archive/delete");
    Require(clearAll,
        "NativeMailCacheService.TryRemove(\n                            recipientId, tag, record.Id, out _)",
        "4495 must remove every eligible runtime cache object");
    Require(clearAll, "result = 1;",
        "4495 must return 1 after at least one removal");
    Require(clearAll,
        "SendDefMessage(Grobal2.SM_CLEAR_ALLMAIL, result, 0, 0, 0, string.Empty);",
        "4495 response fields must match the native zeroed header");
    Forbid(clearAll, "InSafeZone()",
        "4495 must not have a safe-zone gate");

    var archiveMail = store.IndexOf("INSERT INTO gamedata.mailitem_b(",
        StringComparison.Ordinal);
    var deleteMail = store.IndexOf("DELETE FROM gamedata.mailitem WHERE idx=@mailId",
        StringComparison.Ordinal);
    var archiveAttachment = store.IndexOf("INSERT INTO gamedata.attachitem_b(",
        StringComparison.Ordinal);
    var deleteAttachment = store.IndexOf(
        "DELETE FROM gamedata.attachitem WHERE mailId=@mailId", StringComparison.Ordinal);
    if (!(archiveMail >= 0 && archiveMail < deleteMail
          && deleteMail < archiveAttachment && archiveAttachment < deleteAttachment))
    {
        throw new InvalidOperationException(
            "4463 must preserve native mail archive/delete ordering");
    }
    Require(store,
        "SELECT idx FROM gamedata.mailitem_b WHERE idx=LAST_INSERT_ID()",
        "attachment archives must reference the new mailitem_b id");
    Require(store, "if (archiveAttachment.ExecuteNonQuery() != 1) break;",
        "one attachment archive failure must still reach the native bulk source delete");
    Require(store, "INSERT INTO gamedata.Money_order(\" +",
        "4462 money rewards must use the native Money_order audit table");
    Require(store,
        "sendId,sendName,recvName,recvId,title,context,mailType,mailstatus,\" +",
        "Money_order must preserve the native sender/receiver and mail columns");
    Require(store,
        "attachstatus,moneyType,moneyCount,attachNum,moneyStatus,createDate) VALUES(\" +",
        "Money_order must preserve the native attachment/currency/status columns");
    Require(store,
        "@attachStatus,@moneyType,@moneyCount,@attachNum,0,Now())",
        "new native Money_order rows must start with moneyStatus 0");
    Require(store, "queryId.CommandText = \"SELECT LAST_INSERT_ID()\";",
        "native money claims must retain the inserted Money_order id for callbacks");
    Require(store,
        "UPDATE gamedata.Money_order SET moneyStatus=@status WHERE idx=@orderId",
        "native money callbacks must update Money_order by its inserted id");
    Require(store,
        "insert.Parameters.Add(\"@sendId\", MySqlDbType.Int64).Value = record.SenderId;",
        "Money_order must preserve the original sender id");
    Require(store,
        "insert.Parameters.Add(\"@moneyCount\", MySqlDbType.Int32).Value = record.MoneyCount;",
        "Money_order must preserve the requested currency amount");
    Forbid(store, "BeginTransaction(",
        "native mail archive/delete is a best-effort sequence, not a replacement transaction");
    Forbid(store, "Market_Saved", "native mail must not use replacement storage");
    Forbid(store, "UserData.dat", "native mail must not use replacement storage");
}

static void Require(string source, string expected, string message)
{
    if (!source.Contains(expected, StringComparison.Ordinal))
        throw new InvalidOperationException(message);
}

static void Forbid(string source, string forbidden, string message)
{
    // Forbid() asserts about EXECUTED code, so it must not match prose.  The mail-store
    // header block documents the native schema in English ("the live loader ONLY surfaces
    // mailstatus IN (1,2)"), which a raw substring scan flagged as if it were a query —
    // a false positive that says nothing about behaviour.  Strip // line comments first;
    // the assertion itself is unchanged and still bites on any real merged-status query
    // (verified by mutation: adding `"... WHERE mailstatus IN (1,2) "` to the loader's
    // CommandText re-trips this).
    if (StripLineComments(source).Contains(forbidden, StringComparison.Ordinal))
        throw new InvalidOperationException(message);
}

static string StripLineComments(string source)
{
    var builder = new System.Text.StringBuilder(source.Length);
    foreach (var line in source.Split('\n'))
    {
        var trimmed = line.TrimStart();
        // Only whole-line comments are dropped; a trailing comment on a code line would
        // require real tokenising, and no assertion here depends on one.
        if (!trimmed.StartsWith("//", StringComparison.Ordinal))
            builder.Append(line);
        builder.Append('\n');
    }
    return builder.ToString();
}

static string SliceSource(string source, string startMarker, string endMarker)
{
    var start = source.IndexOf(startMarker, StringComparison.Ordinal);
    var end = source.IndexOf(endMarker, start + startMarker.Length,
        StringComparison.Ordinal);
    if (start < 0 || end < 0)
        throw new InvalidOperationException(
            $"could not isolate source contract between {startMarker} and {endMarker}");
    return source[start..end];
}

static string FindRepositoryFile(params string[] relativeParts)
{
    foreach (var start in new[]
             {
                 Environment.CurrentDirectory,
                 AppContext.BaseDirectory
             })
    {
        for (var directory = new DirectoryInfo(start);
             directory != null;
             directory = directory.Parent)
        {
            var path = relativeParts.Aggregate(directory.FullName, Path.Combine);
            if (File.Exists(path)) return path;
        }
    }
    throw new InvalidOperationException(
        "could not locate the LyoMir2 repository from the test output directory");
}

static int ReadInt32(byte[] source, int offset) =>
    BinaryPrimitives.ReadInt32LittleEndian(source.AsSpan(offset, sizeof(int)));

static long ReadInt64(byte[] source, int offset) =>
    BinaryPrimitives.ReadInt64LittleEndian(source.AsSpan(offset, sizeof(long)));

static void BytesEqual(ReadOnlySpan<byte> expected, ReadOnlySpan<byte> actual, string name)
{
    if (!expected.SequenceEqual(actual))
        throw new InvalidOperationException($"{name}: byte sequence differs");
}

static void AllZero(ReadOnlySpan<byte> actual, string name)
{
    foreach (var value in actual)
    {
        if (value != 0)
            throw new InvalidOperationException($"{name}: expected zero, got {value}");
    }
}

static void Equal<T>(T expected, T actual, string name) where T : IEquatable<T>
{
    if (!expected.Equals(actual))
        throw new InvalidOperationException($"{name}: expected {expected}, got {actual}");
}
