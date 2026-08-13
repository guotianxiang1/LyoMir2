using System.Collections;
using System.Reflection;
using GameSvr;
using GameSvr.Services;
using SystemModule;

// In-process isolated-engine MAIL harness (machine-safety FIRST: SINGLE process, NO network stack, NO
// DBSvr, NO MySQL/YBDB, NO background engine threads; strictly serial; Environment.Exit at the end). Same
// technique as InProcEngineRunCheck / InProcSocialRunCheck / InProcHeroRunCheck: construct the M2Share
// engine singletons directly (bypassing GameApp.Initialize / StartEngine and the 30s DBSvr native-def
// gate), inject the native StdItem defs the DBSvr Type2 stream would supply, then drive the REAL native
// mail lifecycle end-to-end and capture the real in-memory state mutations (not model stubs).
//
// This harness resolves the mail-domain "缺当前版真实端到端" (missing real end-to-end) hold. The existing
// AuditTools/NativeMailCacheLifecycleCheck already pins the in-memory cache DATA STRUCTURE in isolation
// (seed/merge/mark-read/sweep/capacity), but it never touches a TPlayObject, a bag, or gold. This harness
// drives the REAL TPlayObject mail HANDLERS so the player <-> cache <-> bag <-> gold integration RUNS.
//
// Native mail is a two-tier design (verified in source):
//   * In-memory tier  : NativeMailCacheService — a process-static Dictionary<long,NativeMailbox> that is
//                        the runtime authority. deliver/read/claim/delete mutate THIS first.
//   * DB tier         : NativeMailStore — pure MySQL, every method gated by TryOpenConnection. With no
//                        connection string it is INERT: each best-effort write returns early and no-ops.
//                        This is the native contract ("updates its in-memory state even when this SQL
//                        write fails"). The harness sets sConnctionString="" so the whole DB tier no-ops.
//
// REAL mail lifecycle driven here (no model stubs):
//   * DELIVER     : NativeMailCacheService.Register — the real in-memory delivery primitive the send path
//                   (MailService.CreateNativeMail) calls AFTER its DB insert. Mailbox list + unread count mutate.
//   * READ        : the real private TPlayObject.ClientFetchNativeMailInfo handler -> MarkNativeMailRead ->
//                   NativeMailCacheService.MarkRead flips the in-memory MailStatus 1->2 and decrements unread.
//   * CLAIM ITEM  : the real private TPlayObject.FetchNativeMailAttachments -> DeliverNativeMailAttachments ->
//                   real AddItemToBag puts the TUserItem into m_ItemList; AttachStatus flips 1->2 in cache.
//   * CLAIM GOLD  : the same real claim core with a moneyType==0 gold payload -> IncGold(moneyCount)
//                   (native 0x70B7DB call [vmt+0x28C]; real in-memory purse credit + GoldChanged);
//                   AttachStatus flips 1->2. Overflow returns -3 with gold untouched.
//   * DELETE      : the real private TPlayObject.ClientDeleteNativeMail -> NativeMailCacheService.TryRemove
//                   removes the mail from the in-memory category list.
//
// SKIP'd (documented, not faked — see RunSkips): the send-path MySQL insert, the moneyType==1 yuanbao
// async claim (DBSvr/YBDB round-trip; its ladder is the dormant NativeMailWriteTransaction model), and all
// NativeMailStore persistence/archive/retention (best-effort write-behind, no-op with no connection).
//
// The internal cache/record/entry types are reached by reflection — the SAME idiom
// AuditTools/NativeMailCacheLifecycleCheck uses — so this harness needs NO GameSvr edit / InternalsVisibleTo.
//
// Evidence goes to stdout and inproc_mail_evidence.txt next to the executable.

int rc = 0;
var evidence = new List<string>();
void Log(string s) { evidence.Add(s); Console.WriteLine("  " + s); }
void Assert(bool cond, string msg) { if (!cond) throw new Exception("ASSERT FAILED: " + msg); }

// ---- reflected handles for the internal native-mail types (idiom from NativeMailCacheLifecycleCheck) ----
var gameAssembly = typeof(NativeMailWireCodec).Assembly;
var cacheType = gameAssembly.GetType("GameSvr.Services.NativeMailCacheService", throwOnError: true);
var recordType = gameAssembly.GetType("GameSvr.Services.NativeMailRecord", throwOnError: true);
var entryType = gameAssembly.GetType("GameSvr.Services.NativeMailCacheEntry", throwOnError: true);

// ---- non-public REAL TPlayObject mail handlers driven by reflection (same as the sibling harnesses) ----
var miFetchInfo = typeof(TPlayObject).GetMethod("ClientFetchNativeMailInfo",
    BindingFlags.Instance | BindingFlags.NonPublic, null, new[] { typeof(int), typeof(int) }, null)
    ?? throw new MissingMethodException("TPlayObject.ClientFetchNativeMailInfo");
var miFetchAttach = typeof(TPlayObject).GetMethod("FetchNativeMailAttachments",
    BindingFlags.Instance | BindingFlags.NonPublic, null, new[] { entryType }, null)
    ?? throw new MissingMethodException("TPlayObject.FetchNativeMailAttachments");
var miDelete = typeof(TPlayObject).GetMethod("ClientDeleteNativeMail",
    BindingFlags.Instance | BindingFlags.NonPublic, null, new[] { typeof(int), typeof(int) }, null)
    ?? throw new MissingMethodException("TPlayObject.ClientDeleteNativeMail");
var fiRecipientId = typeof(TPlayObject).GetField("_nativeMailRecipientId",
    BindingFlags.Instance | BindingFlags.NonPublic)
    ?? throw new MissingFieldException("TPlayObject._nativeMailRecipientId");

const long RecipientId = 0x5001;

try
{
    PrepareConfig();
    BootSingletons();
    Log("BOOT singletons: g_Config/RandomNumber/ObjectManager/UserEngine/MapManager constructed "
        + "(no GameApp.Initialize, no DBSvr gate, no network, no background threads)");
    Assert(string.IsNullOrWhiteSpace(M2Share.g_Config.sConnctionString),
        "DB tier inert: sConnctionString='' so every NativeMailStore.TryOpenConnection returns false (no-op)");
    Log("DB TIER INERT: sConnctionString='' -> NativeMailStore load/mark/archive/retention all no-op "
        + "(best-effort write-behind of the in-memory cache; native contract keeps the in-mem mutation)");

    InjectNativeDefs();
    ResetCache();

    var player = NewPlayer("mail-owner", "mail-user");
    Log($"PLAYER '{player.m_sCharName}' offline in-proc; recipientId injected=0x{RecipientId:X} "
        + $"(the DBSvr/mir3.user_index recvId resolution is SKIP'd/injected); bag empty={player.m_ItemList.Count == 0}");

    RunMailLifecycle(player);
    RunSkips();

    Console.WriteLine(
        "PASS InProcMailRunCheck deliver=REAL(NativeMailCacheService.Register->mailbox+unread) "
        + "read=REAL(ClientFetchNativeMailInfo->MarkRead flips MailStatus 1->2, unread--) "
        + "claim-item=REAL(FetchNativeMailAttachments->AddItemToBag, attachstatus 1->2) "
        + "claim-gold=REAL(FetchNativeMailAttachments->IncGold, attachstatus 1->2) "
        + "delete=REAL(ClientDeleteNativeMail->TryRemove) "
        + "send-DB/yuanbao-async/DB-persistence=SKIP(no-connection) "
        + "single-process no-network no-DBSvr no-MySQL");
}
catch (Exception ex)
{
    Console.Error.WriteLine("FAIL InProcMailRunCheck: " + ex);
    rc = 1;
}

try { File.WriteAllLines(Path.Combine(AppContext.BaseDirectory, "inproc_mail_evidence.txt"), evidence); }
catch { /* evidence file is best-effort */ }

// Hard-exit so no lingering engine state can keep the process alive.
Environment.Exit(rc);

// ===================== mail lifecycle =====================

void RunMailLifecycle(TPlayObject player)
{
    var now = DateTime.Now;

    // ---- 1. DELIVER: the real in-memory delivery primitive (send path's post-DB-insert half) ----------
    // recA: normal unread mail, no attachment (for READ + DELETE).
    // recB: unread mail carrying a 铁剑 item attachment (for CLAIM ITEM).
    // recC: delivered-read mail carrying a gold payload, no item attachments (for CLAIM GOLD).
    var recA = NewRecord(1001, mailType: 1, mailStatus: 1, attachStatus: 3, moneyType: 0, moneyCount: 0,
        "系统", "到期提醒", "欢迎回来", now);
    var recB = NewRecord(1002, mailType: 1, mailStatus: 1, attachStatus: 1, moneyType: 0, moneyCount: 0,
        "系统", "附件邮件", "领取附件", now);
    var recC = NewRecord(1003, mailType: 1, mailStatus: 2, attachStatus: 1, moneyType: 0, moneyCount: 500,
        "系统", "金币返还", "领取金币", now);

    var attachItem = MakeItem("铁剑");
    Assert(attachItem != null, "real UserEngine.CopyToUserItemFromName built the 铁剑 mail attachment");

    var entryA = Register(RecipientId, player.m_sCharName, recA, new List<TUserItem>(), now);
    var entryB = Register(RecipientId, player.m_sCharName, recB, new List<TUserItem> { attachItem }, now);
    var entryC = Register(RecipientId, player.m_sCharName, recC, new List<TUserItem>(), now);
    Assert(entryA != null && entryB != null && entryC != null,
        "all three mails registered into the real in-memory mailbox");

    var unread0 = UnreadCounts(RecipientId);
    Log($"DELIVER Register x3 (real in-mem primitive the send path calls after its DB insert): "
        + $"MailboxCount={MailboxCount()} tag1-category={CategoryCount(RecipientId, 1)} unread[tag1]={unread0[0]}");
    Assert(MailboxCount() == 1, "one in-memory mailbox created for the injected recipient id");
    Assert(CategoryCount(RecipientId, 1) == 3, "three mails live in the tag-1 in-memory category list");
    Assert(TryFind(RecipientId, 1, 1001, out _), "a delivered mail resolves by id from the in-memory cache");
    Assert(unread0[0] == 2, "two unread mails counted in-memory (A,B unread; C delivered read)");

    // ---- 2. READ: the real ClientFetchNativeMailInfo handler flips the in-memory read flag -------------
    Assert(RecMailStatus(recA) == 1, "mail A starts unread (MailStatus=1)");
    miFetchInfo.Invoke(player, new object[] { 1001, 1 });   // REAL read handler (offline: SendSocket no-op)
    var unread1 = UnreadCounts(RecipientId);
    Log($"READ ClientFetchNativeMailInfo(1001): mail A MailStatus 1->{RecMailStatus(recA)}; "
        + $"unread[tag1] {unread0[0]}->{unread1[0]} (real MarkRead in-memory mutation; DB MarkRead no-op)");
    Assert(RecMailStatus(recA) == 2, "real read handler flipped the in-memory MailStatus 1->2");
    Assert(unread1[0] == 1, "real read handler decremented the in-memory unread count");

    // ---- 3. CLAIM ITEM: the real FetchNativeMailAttachments core lands the item via real AddItemToBag --
    int bag0 = player.m_ItemList.Count;
    Assert(RecAttachStatus(recB) == 1, "mail B has an unclaimed attachment (AttachStatus=1)");
    int claimItem = (int)miFetchAttach.Invoke(player, new object[] { entryB });   // REAL claim core
    int bag1 = player.m_ItemList.Count;
    Log($"CLAIM-ITEM FetchNativeMailAttachments(1002)={claimItem}: bag {bag0}->{bag1}; "
        + $"claimed wIndex={(bag1 > bag0 ? player.m_ItemList[bag1 - 1].wIndex : -1)} (attach '铁剑' wIndex={attachItem.wIndex}); "
        + $"AttachStatus 1->{RecAttachStatus(recB)} (real in-memory mutations; DB attach-status write no-op)");
    Assert(claimItem == 1, "real attachment claim returned Delivered(1)");
    Assert(bag1 == bag0 + 1, "real AddItemToBag put the mail attachment into the player's in-memory bag");
    Assert(player.m_ItemList[bag1 - 1].wIndex == attachItem.wIndex, "the claimed bag item is the 铁剑 attachment");
    Assert(RecAttachStatus(recB) == 2, "real claim flipped the in-memory AttachStatus 1->2 (claimed)");

    // ---- 4. CLAIM GOLD: the real claim core credits the mail gold into the in-memory purse ------------
    player.m_nGoldMax = 2_000_000_000;   // native ctor sets this from nHumanMaxGold (0 without config load)
    player.m_nGold = 0;
    Assert(RecAttachStatus(recC) == 1, "mail C has an unclaimed gold payload (AttachStatus=1)");
    int claimGold = (int)miFetchAttach.Invoke(player, new object[] { entryC });    // REAL claim core (gold)
    Log($"CLAIM-GOLD FetchNativeMailAttachments(1003)={claimGold}: m_nGold 0->{player.m_nGold} (moneyCount=500); "
        + $"AttachStatus 1->{RecAttachStatus(recC)} (real in-memory gold credit; Money_order DB row SKIP'd/no-op)");
    Assert(claimGold == 1, "real gold claim returned Delivered(1)");
    Assert(player.m_nGold == 500, "real claim credited the mail gold through IncGold");
    Assert(RecAttachStatus(recC) == 2, "real gold claim flipped the in-memory AttachStatus 1->2 (claimed)");

    // Overflow gate: native 0x70B7C0 call 0x6D7948 / 0x70B7C9 mov esi,-3. Gold and
    // AttachStatus must stay put — this is the "扣了邮件没给钱" inverse: refuse
    // before IncGold so a full purse cannot burn the mail.
    var recOverflow = NewRecord(1004, mailType: 1, mailStatus: 2, attachStatus: 1,
        moneyType: 0, moneyCount: 100, "系统", "溢出", "不应发放", now);
    var entryOverflow = Register(RecipientId, player.m_sCharName, recOverflow,
        new List<TUserItem>(), now);
    player.m_nGold = player.m_nGoldMax;
    int goldBeforeOverflow = player.m_nGold;
    int claimOverflow = (int)miFetchAttach.Invoke(player, new object[] { entryOverflow });
    Log($"CLAIM-GOLD overflow FetchNativeMailAttachments(1004)={claimOverflow}: "
        + $"m_nGold stayed {player.m_nGold} (max={player.m_nGoldMax}); "
        + $"AttachStatus 1->{RecAttachStatus(recOverflow)}");
    Assert(claimOverflow == -3, "gold overflow must return -3 (native esi=-3)");
    Assert(player.m_nGold == goldBeforeOverflow, "overflow must not credit gold");
    Assert(RecAttachStatus(recOverflow) == 1, "overflow must not mark the mail claimed");

    // ---- 5. DELETE: the real ClientDeleteNativeMail handler removes the mail from the in-memory cache --
    int cat0 = CategoryCount(RecipientId, 1);
    miDelete.Invoke(player, new object[] { 1001, 1 });    // REAL delete handler (offline: SendDefMessage no-op)
    int cat1 = CategoryCount(RecipientId, 1);
    Log($"DELETE ClientDeleteNativeMail(1001): tag1-category {cat0}->{cat1}; "
        + $"TryFind(1001)={TryFind(RecipientId, 1, 1001, out _)} (real TryRemove; DB archive/delete no-op)");
    Assert(!TryFind(RecipientId, 1, 1001, out _), "real delete removed mail A from the in-memory mailbox");
    Assert(cat1 == cat0 - 1, "real delete shrank the in-memory category list by one");
}

void RunSkips()
{
    Log("SEND-DB SKIP: MailService.NewFullMailEx/CreateNativeMail hard-requires MySQL (opens a "
        + "MySqlConnection, INSERTs gamedata.mailitem/attachitem, resolves recvId from mir3.user_index). "
        + "Its in-memory half — NativeMailCacheService.Register — IS driven live above; the DB insert is SKIP'd. Not faked.");
    Log("YUANBAO-ASYNC SKIP: the moneyType==1 claim branch enqueues NativeYuanbaoManager + a DBSvr/YBDB "
        + "round-trip; its result ladder is the dormant NativeMailWriteTransaction.YuanbaoClaimComplete model. "
        + "Not driven live (no YBDB in-process). Not faked.");
    Log("DB-PERSISTENCE SKIP: NativeMailStore load/mark/archive/retention are best-effort write-behind of the "
        + "in-memory cache; with sConnctionString='' every TryOpenConnection returns false so they no-op — "
        + "exactly the native contract ('updates its in-memory state even when this SQL write fails').");
}

// ===================== native definition injection (DBSvr Type2 data, built in-memory) =========

void InjectNativeDefs()
{
    var eng = M2Share.UserEngine;

    // Faithful native StdItem layout: index 0 is the "金币" sentinel the DBSvr Type2 stream uses; the real
    // 铁剑 weapon follows so CopyToUserItemFromName / GetStdItem resolve the mail attachment 1:1.
    eng.StdItemList.Add(new GoodItem { Name = "金币", NativeWireIndex = 0, ItemType = GoodType.ITEM_GOLD });
    eng.StdItemList.Add(new GoodItem
    {
        Name = "铁剑", ItemType = GoodType.ITEM_WEAPON, StdMode = 5, Shape = 1,
        Weight = 5, DuraMax = 5000, Dc = 3, Dc2 = 8
    });

    Assert(eng.GetStdItem("铁剑") != null, "injected weapon StdItem resolves by name (mail attachment factory)");
    Log($"DEFS injected in-memory: StdItemList={eng.StdItemList.Count} (sentinel '金币' + '铁剑' attachment)");
}

// ===================== reflection helpers over the internal native-mail types ===================

object NewRecord(int id, byte mailType, byte mailStatus, byte attachStatus, int moneyType, int moneyCount,
    string sender, string title, string context, DateTime created)
{
    var record = Activator.CreateInstance(recordType, nonPublic: true);
    SetProp(record, "Id", id);
    SetProp(record, "SenderId", -1L);
    SetProp(record, "Sender", sender);
    SetProp(record, "Title", title);
    SetProp(record, "Context", context);
    SetProp(record, "MailType", mailType);
    SetProp(record, "MailStatus", mailStatus);
    SetProp(record, "AttachStatus", attachStatus);
    SetProp(record, "MoneyType", moneyType);
    SetProp(record, "MoneyCount", moneyCount);
    SetProp(record, "CreateDate", created);
    return record;
}

void SetProp(object target, string name, object value) => recordType
    .GetProperty(name, BindingFlags.Instance | BindingFlags.NonPublic)
    .SetValue(target, value);

byte RecMailStatus(object record) => (byte)recordType
    .GetProperty("MailStatus", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(record);

byte RecAttachStatus(object record) => (byte)recordType
    .GetProperty("AttachStatus", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(record);

object InvokeCache(string method, object[] args) => cacheType
    .GetMethod(method, BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, args);

object Register(long recipientId, string name, object record, List<TUserItem> attachments, DateTime now) =>
    InvokeCache("Register", new object[] { recipientId, name, record, attachments, now });

bool TryFind(long recipientId, int tag, int mailId, out object entry)
{
    var args = new object[] { recipientId, tag, mailId, null };
    var ok = (bool)InvokeCache("TryFind", args);
    entry = ok ? args[3] : null;
    return ok;
}

int[] UnreadCounts(long recipientId)
{
    var args = new object[] { recipientId, null };
    var ok = (bool)InvokeCache("TryGetUnreadCounts", args);
    return ok ? (int[])args[1] : new int[6];
}

int CategoryCount(long recipientId, int tag)
{
    var args = new object[] { recipientId, tag, null };
    var ok = (bool)InvokeCache("TryGetCachedCategory", args);
    return ok ? ((ICollection)args[2]).Count : 0;
}

int MailboxCount() => (int)cacheType
    .GetProperty("MailboxCount", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);

void ResetCache() => InvokeCache("ResetForTests", new object[] { 0 });

// ===================== helpers (shared idiom with the sibling harnesses) ========================

TUserItem MakeItem(string name)
{
    TUserItem item = null;
    return M2Share.UserEngine.CopyToUserItemFromName(name, ref item) ? item : null;
}

TPlayObject NewPlayer(string charName, string userId)
{
    // Offline keeps every SendSocket/SendDefMessage/SendAddItem a no-op (early return); ghost=false keeps
    // the self message-queue (SendMsg) enqueuing. The ctor allocates m_ItemList/m_MsgList (non-null).
    var p = new TPlayObject
    {
        m_boOffLineFlag = true, m_boGhost = false, m_boDeath = false,
        m_sCharName = charName, m_sUserID = userId
    };
    p.m_Abil.Level = 30;
    fiRecipientId.SetValue(p, RecipientId);   // inject the DBSvr/user_index recvId resolution
    return p;
}

void PrepareConfig()
{
    var baseDir = AppContext.BaseDirectory;
    File.WriteAllText(Path.Combine(baseDir, "!Setup.txt"), "[Server]\r\n");
    File.WriteAllText(Path.Combine(baseDir, "String.ini"), "[String]\r\n");
    File.WriteAllText(Path.Combine(baseDir, "Command.conf"), "[Command]\r\n");
    var share = Path.GetFullPath(Path.Combine(baseDir, "..", "Share"));
    Directory.CreateDirectory(share);
    File.WriteAllText(Path.Combine(share, "PlayerUpgradeExp.ini"), "[PlayerLevelExp]\r\nLEVEL_1=50\r\n");
}

void BootSingletons()
{
    M2Share.g_Config = new GameSvrConfig();
    M2Share.g_Config.sConnctionString = string.Empty;   // force the DB tier inert (best-effort no-op)
    M2Share.RandomNumber = RandomNumber.GetInstance();
    M2Share.ObjectManager = new ObjectManager();
    M2Share.UserEngine = new UserEngine();
    M2Share.MapManager = new MapManager();
    M2Share.ProcessMsgCriticalSection = new object();   // SendUpdateMsg/SendMsg (WeightChanged/GoldChanged)
    M2Share.ProcessHumanCriticalSection = new object();
    M2Share.LogMsgCriticalSection = new object();
    M2Share.LogStringList = new System.Collections.ArrayList();
}
