using System.Buffers.Binary;
using System.Runtime.CompilerServices;
using GameSvr;
using GameSvr.Services;
using SystemModule;
using SystemModule.Packet;

PrepareConfig();

var failures = new List<string>();
Run("exact online identity", ExactOnlineIdentity);
Run("0062 empty and publish flag", EmptyLoadAndPublishFlag);
Run("0062 item load and capacity", ItemLoadAndCapacity);
Run("0062 malformed native side effects", MalformedLoadSideEffects);
Run("0063 exact save acknowledgement", SaveAcknowledgement);
Run("malformed frames stay silent", MalformedFrames);
Run("016B/016C native producers", NativeRequestProducers);
Run("capacity transitions", CapacityTransitions);
Run("native item gates and log quantity", ItemGatesAndLogQuantity);
Run("exact current NPC gate", ExactCurrentNpcGate);
Run("read-only storage item lookup", ReadOnlyStorageItemLookup);
Run("native operation source contract", NativeOperationSourceContract);

if (failures.Count != 0)
{
    Console.Error.WriteLine(string.Join(Environment.NewLine, failures));
    return 1;
}

Console.WriteLine("NativeAccountStorageCompatCheck PASS tests=12 " +
                  "type1=0062/0063 header=0x48 item=0xD0 client=704");
return 0;

void ExactOnlineIdentity()
{
    var wrongCase = Player("hero", "account");
    Equal(null, NativeAccountStorageClient.FindOnlinePlayer(
        new[] { wrongCase }, Bytes("Hero")), "character case");

    var ghost = Player("Hero", "account");
    ghost.m_boGhost = true;
    var activeDuplicate = Player("Hero", "account");
    Equal(null, NativeAccountStorageClient.FindOnlinePlayer(
        new[] { ghost, activeDuplicate }, Bytes("Hero")),
        "first duplicate ghost rejection");

    var notReady = Player("Ready", "account");
    notReady.m_boReadyRun = false;
    Equal(null, NativeAccountStorageClient.FindOnlinePlayer(
        new[] { notReady }, Bytes("Ready")), "ReadyRun rejection");
    Equal(activeDuplicate, NativeAccountStorageClient.FindOnlinePlayer(
        new[] { activeDuplicate }, Bytes("Hero")), "exact active match");
}

void EmptyLoadAndPublishFlag()
{
    var player = Player("Hero", "Account");
    var published = 0;
    var result = Process(Response(0x0062, 0, 1, "Account", "Hero"),
        player, _ => null, (_, state) =>
        {
            published++;
            Equal(0, state.Capacity, "empty capacity at publish");
        });
    Equal(NativeAccountStorageResponseDisposition.LoadApplied, result,
        "empty disposition");
    Equal(0, player.GetNativeAccountStorageState().Capacity,
        "empty capacity");
    Equal(1, published, "empty publish count");

    var accountCase = Player("Case", "Account");
    result = Process(Response(0x0062, 0, 1, "account", "Case"),
        accountCase, _ => null, (_, _) => published++);
    Equal(NativeAccountStorageResponseDisposition.AccountMismatch, result,
        "account case disposition");
    Equal(-1, accountCase.GetNativeAccountStorageState().Capacity,
        "account mismatch state");
}

void ItemLoadAndCapacity()
{
    var player = Player("Hero", "Account");
    var currentDay = (ushort)Math.Truncate(DateTime.Now.ToOADate());
    var first = ItemRecord(101, 7, (ushort)(currentDay - 10));
    var second = ItemRecord(102, 8, currentDay);
    var invalid = ItemRecord(0, 9, currentDay);
    var tail = StorageTail(2, first, invalid, second);
    var published = 0;
    var result = Process(
        Response(0x0062, 1, 1, "Account", "Hero", tail), player,
        record => NativeAccountStorageClient.DecodeItemRecord(record,
            _ => true),
        (_, state) =>
        {
            published++;
            Equal(2, state.Items.Count, "published item count");
        });

    Equal(NativeAccountStorageResponseDisposition.LoadApplied, result,
        "item disposition");
    var state = player.GetNativeAccountStorageState();
    Equal(2, state.Capacity, "loaded capacity");
    Equal(2, state.Items.Count, "capacity drops overflow item");
    Equal(true, state.Dirty, "load add marks dirty");
    Assert(state.Items[0].ClientItemID != 0, "client id not assigned");
    Equal((byte)0, state.Items[0].btValue[10], "stale day low byte");
    Equal((byte)0, state.Items[0].btValue[11], "stale day high byte");
    Equal(1, published, "item publish count");
}

void MalformedLoadSideEffects()
{
    var player = Player("Hero", "Account");
    var malformed = new byte[4 + NativeAccountStorageClient.ItemSize - 1];
    BinaryPrimitives.WriteUInt16LittleEndian(malformed, 37);
    BinaryPrimitives.WriteUInt16LittleEndian(malformed.AsSpan(2, 2), 1);
    var published = 0;
    var result = Process(
        Response(0x0062, 1, 1, "Account", "Hero", malformed), player,
        _ => throw new InvalidOperationException("decoder reached"),
        (_, _) => published++);
    Equal(NativeAccountStorageResponseDisposition.MalformedLoadData, result,
        "malformed disposition");
    Equal(37, player.GetNativeAccountStorageState().Capacity,
        "capacity side effect before length validation");
    Equal(0, published, "malformed publish count");

    result = Process(
        Response(0x0062, 0, 1, "Account", "Hero"), player,
        _ => null, (_, _) => published++);
    Equal(NativeAccountStorageResponseDisposition.AlreadyLoaded, result,
        "malformed locks loaded state");

    var zeroCount = Player("Zero", "Account");
    var trailing = new byte[9];
    BinaryPrimitives.WriteUInt16LittleEndian(trailing, 12);
    result = Process(
        Response(0x0062, 1, 0, "Account", "Zero", trailing), zeroCount,
        _ => null, (_, _) => published++);
    Equal(NativeAccountStorageResponseDisposition.LoadApplied, result,
        "zero count trailing bytes");
    Equal(12, zeroCount.GetNativeAccountStorageState().Capacity,
        "zero count capacity");
}

void SaveAcknowledgement()
{
    var player = Player("Hero", "Account");
    var state = player.GetNativeAccountStorageState();
    state.Dirty = true;
    var result = Process(Response(0x0063, 2, 0, "Account", "Hero"),
        player, _ => null, (_, _) => { });
    Equal(NativeAccountStorageResponseDisposition.SaveStatusIgnored, result,
        "save status 2");
    Equal(true, state.Dirty, "save status 2 dirty");

    result = Process(Response(0x0063, 1, 0, "Account", "Hero"),
        player, _ => null, (_, _) => { });
    Equal(NativeAccountStorageResponseDisposition.SaveApplied, result,
        "save status 1");
    Equal(false, state.Dirty, "save status 1 dirty");
}

void MalformedFrames()
{
    var player = Player("Hero", "Account");
    var shortFrame = new LegacyDbServerFrame(1, 0, new byte[0x47]);
    Equal(NativeAccountStorageResponseDisposition.InvalidFrame,
        Process(shortFrame, player, _ => null, (_, _) => { }),
        "short frame");
    var wrongType = Response(0x0062, 0, 0, "Account", "Hero");
    wrongType = new LegacyDbServerFrame(2, 0, wrongType.Payload);
    Equal(NativeAccountStorageResponseDisposition.InvalidFrame,
        Process(wrongType, player, _ => null, (_, _) => { }),
        "outer type");

    var longAccount = Response(0x0062, 0, 0, "Account", "Hero");
    longAccount.Payload[0x10] = 21;
    Equal(NativeAccountStorageResponseDisposition.InvalidFrame,
        Process(longAccount, player, _ => null, (_, _) => { }),
        "account ShortString");
}

void NativeRequestProducers()
{
    var player = Player("Hero", "Account");
    byte[] wire = null;
    Equal(true, NativeAccountStorageClient.SendLoadRequest(player, 1,
        bytes =>
        {
            wire = bytes;
            return true;
        }), "016B send");
    Equal(true, LegacyDbServerFrameCodec.TryDecode(wire,
        out var frame, out _), "016B frame");
    Equal((ushort)1, frame.Type, "016B type");
    Equal(NativeAccountStorageClient.LoadCommand,
        BinaryPrimitives.ReadUInt16LittleEndian(frame.Payload),
        "016B command");
    Equal(1, BinaryPrimitives.ReadInt32LittleEndian(
        frame.Payload.AsSpan(4, 4)), "016B mode");

    var state = player.GetNativeAccountStorageState();
    var sends = 0;
    Equal(true, NativeAccountStorageClient.SendDirtySave(player,
        _ =>
        {
            sends++;
            return true;
        }), "clean save result");
    Equal(0, sends, "clean save sends");

    state.Capacity = 4;
    state.Dirty = true;
    state.Items.Add(NativeAccountStorageClient.DecodeItemRecord(
        ItemRecord(101, 7, 1), _ => true));
    Equal(true, NativeAccountStorageClient.SendDirtySave(player,
        bytes =>
        {
            sends++;
            wire = bytes;
            return true;
        }), "016C send");
    Equal(1, sends, "016C sends");
    Equal(true, state.Dirty, "016C keeps dirty until 0063");
    Equal(true, LegacyDbServerFrameCodec.TryDecode(wire,
        out frame, out _), "016C frame");
    Equal(NativeAccountStorageClient.SaveCommand,
        BinaryPrimitives.ReadUInt16LittleEndian(frame.Payload),
        "016C command");
    Equal((ushort)4, BinaryPrimitives.ReadUInt16LittleEndian(
        frame.Payload.AsSpan(NativeAccountStorageClient.HeaderSize, 2)),
        "016C capacity");
    Equal((ushort)1, BinaryPrimitives.ReadUInt16LittleEndian(
        frame.Payload.AsSpan(NativeAccountStorageClient.HeaderSize + 2, 2)),
        "016C count");
    Equal(NativeAccountStorageClient.HeaderSize + 4
          + NativeAccountStorageClient.ItemSize, frame.Payload.Length,
        "016C payload size");
}

void CapacityTransitions()
{
    var state = new NativeAccountStorageState();
    Equal(false, NativeAccountStorageClient.TryChangeCapacity(state, 1),
        "unloaded capacity");
    state.Capacity = 299;
    Equal(true, NativeAccountStorageClient.TryChangeCapacity(state, 1),
        "capacity to max");
    Equal(300, state.Capacity, "capacity max");
    Equal(true, state.Dirty, "capacity dirty");
    Equal(false, NativeAccountStorageClient.TryChangeCapacity(state, 1),
        "capacity overflow");
    Equal(300, state.Capacity, "capacity unchanged on overflow");
    Equal(true, NativeAccountStorageClient.TryChangeCapacity(state, -300),
        "capacity to zero");
    Equal(0, state.Capacity, "capacity zero");
}

void ItemGatesAndLogQuantity()
{
    var stdItem = new GoodItem { NativeReserved02 = 0, StdMode = 6 };
    var item = new TUserItem { Bind = 0, Dura = 37 };
    Equal(true, NativeAccountStorageClient.IsDepositRestricted(null, item),
        "null std item restriction");
    Equal(true, NativeAccountStorageClient.IsDepositRestricted(stdItem, null),
        "null item restriction");
    Equal(false, NativeAccountStorageClient.IsDepositRestricted(stdItem, item),
        "ordinary item restriction");
    item.btValue[10] = 1;
    Equal(true, NativeAccountStorageClient.IsDepositRestricted(stdItem, item),
        "native item +0x34 word one gate");
    item.btValue[10] = 2;
    Equal(false, NativeAccountStorageClient.IsDepositRestricted(stdItem, item),
        "native item +0x34 word two accepted");
    item.btValue[10] = 0;
    item.btValue[11] = 1;
    Equal(false, NativeAccountStorageClient.IsDepositRestricted(stdItem, item),
        "native item +0x34 exact word comparison");
    item.btValue[11] = 0;

    stdItem.NativeReserved02 = 0x0080;
    Equal(true, NativeAccountStorageClient.IsDepositRestricted(stdItem, item),
        "StdItem+2 low-byte 0x80 gate");
    stdItem.NativeReserved02 = 0x8000;
    Equal(false, NativeAccountStorageClient.IsDepositRestricted(stdItem, item),
        "StdItem+2 high-byte is not gate");

    stdItem.NativeReserved02 = 0;
    item.Bind = 1;
    Equal(false, NativeAccountStorageClient.IsDepositRestricted(stdItem, item),
        "extension Bind is not native +0x34");
    item.Bind = 2;
    Equal(false, NativeAccountStorageClient.IsDepositRestricted(stdItem, item),
        "extension Bind two is accepted");

    Equal(0, NativeAccountStorageClient.GetGameDataLogQuantity(null, item),
        "null template log quantity");
    Equal(0, NativeAccountStorageClient.GetGameDataLogQuantity(stdItem, null),
        "null item log quantity");
    Equal(1, NativeAccountStorageClient.GetGameDataLogQuantity(stdItem, item),
        "ordinary non-pile log quantity");
    stdItem.StdMode = 7;
    stdItem.Shape = 0;
    Equal(1, NativeAccountStorageClient.GetGameDataLogQuantity(stdItem, item),
        "StdMode7 charm is not a native pile item");
    stdItem.StdMode = 3;
    stdItem.Shape = 4;
    Equal(37, NativeAccountStorageClient.GetGameDataLogQuantity(stdItem, item),
        "StdMode3 Shape4 luck oil pile quantity");
    stdItem.StdMode = 151;
    stdItem.Shape = 0;
    Equal(37, NativeAccountStorageClient.GetGameDataLogQuantity(stdItem, item),
        "StdMode151 pile quantity");
}

void ExactCurrentNpcGate()
{
    var player = (TPlayObject)RuntimeHelpers.GetUninitializedObject(
        typeof(TPlayObject));
    var npc = (TBaseObject)RuntimeHelpers.GetUninitializedObject(
        typeof(TBaseObject));
    var map = new Envirnoment();
    player.m_NPC = npc;
    player.m_PEnvir = map;
    npc.m_PEnvir = map;
    player.m_nCurrX = 100;
    player.m_nCurrY = 100;
    npc.m_nCurrX = 115;
    npc.m_nCurrY = 85;

    var method = typeof(TPlayObject).GetMethod(
        "IsNativeAccountStorageNpc",
        System.Reflection.BindingFlags.Instance
        | System.Reflection.BindingFlags.NonPublic);
    Assert(method != null, "current-NPC gate method is missing");
    bool Gate(int objectId) => (bool)method.Invoke(player,
        new object[] { objectId });

    Equal(true, Gate(npc.ObjectId), "15-tile boundary and plain NPC");
    npc.m_nCurrX = 116;
    Equal(false, Gate(npc.ObjectId), "16-tile x rejection");
    npc.m_nCurrX = 115;
    npc.m_nCurrY = 84;
    Equal(false, Gate(npc.ObjectId), "16-tile y rejection");
    npc.m_nCurrY = 85;
    Equal(false, Gate(unchecked(npc.ObjectId + 1)),
        "object-id mismatch");
    npc.m_PEnvir = new Envirnoment();
    Equal(false, Gate(npc.ObjectId), "map mismatch");
    player.m_NPC = null;
    Equal(false, Gate(npc.ObjectId), "null current NPC");
}

void ReadOnlyStorageItemLookup()
{
    var player = Player("Lookup", "Account");
    var matches = typeof(TPlayObject).GetMethod(
        "ClientItemIdMatches",
        System.Reflection.BindingFlags.Instance
        | System.Reflection.BindingFlags.NonPublic);
    Assert(matches != null, "client item id predicate is missing");

    var unmatched = new TUserItem { wIndex = 7, ClientItemID = 0 };
    Equal(false, (bool)matches.Invoke(player,
            new object[] { unmatched, 0x12345678 }),
        "generic client item id predicate rejects a forged next id");
    Equal(0, unmatched.ClientItemID,
        "generic client item id predicate must not lazily allocate an id");

    var method = typeof(TPlayObject).GetMethod(
        "FindNativeStorageItemIndex",
        System.Reflection.BindingFlags.Static
        | System.Reflection.BindingFlags.NonPublic);
    Assert(method != null, "native storage item finder is missing");

    var hidden = new TUserItem { wIndex = 7, ClientItemID = 0 };
    var items = new List<TUserItem> { hidden };
    Equal(-1, (int)method.Invoke(null, new object[] { items, 0x23456789 }),
        "zero-id item must not match a forged next client id");
    Equal(0, hidden.ClientItemID,
        "storage lookup must not assign a client id as a side effect");

    hidden.ClientItemID = 0x23456789;
    Equal(0, (int)method.Invoke(null, new object[] { items, 0x23456789 }),
        "preassigned exact client id lookup");
}

void NativeOperationSourceContract()
{
    var sourcePath = Path.Combine(Directory.GetCurrentDirectory(), "GameSvr",
        "Players", "TPlayObject.NativeAccountStorage.Operations.cs");
    if (!File.Exists(sourcePath))
        throw new FileNotFoundException(
            "run this audit from the repository root", sourcePath);
    var source = File.ReadAllText(sourcePath);

    var deposit = Slice(source,
        "internal void ClientNativeAccountStorageItem(",
        "internal void ClientNativeAccountTakeBackStorageItem(");
    Assert(deposit.Contains(
            "for (var i = m_ItemList.Count - 1; i >= 0; i--)",
            StringComparison.Ordinal),
        "deposit bag scan must run from tail to head");
    var full = Slice(deposit,
        "if (state.Items.Count + 1 > state.Capacity)",
        "state.Items.Add(item);");
    var storageFull = full.IndexOf("Grobal2.SM_STORAGE_FULL",
        StringComparison.Ordinal);
    var storageFail = full.IndexOf("Grobal2.SM_STORAGE_FAIL",
        StringComparison.Ordinal);
    Assert(storageFull >= 0 && storageFail > storageFull,
        "full account storage must send 702 before 703");
    Assert(deposit.Contains("LogNativeAccountStorageItem(item, stdItem, 0x01);",
            StringComparison.Ordinal),
        "deposit log action must be 1");
    var depositDirty = deposit.IndexOf("state.Dirty = true;",
        StringComparison.Ordinal);
    var depositAdd = deposit.IndexOf("state.Items.Add(item);",
        StringComparison.Ordinal);
    var depositRemove = deposit.IndexOf("m_ItemList.RemoveAt(itemIndex);",
        StringComparison.Ordinal);
    var depositWeight = deposit.IndexOf("WeightChanged();",
        StringComparison.Ordinal);
    var depositOk = deposit.IndexOf("Grobal2.SM_STORAGE_OK",
        StringComparison.Ordinal);
    var depositLog = deposit.IndexOf(
        "LogNativeAccountStorageItem(item, stdItem, 0x01);",
        StringComparison.Ordinal);
    Assert(depositDirty >= 0 && depositAdd > depositDirty
           && depositRemove > depositAdd && depositWeight > depositRemove
           && depositOk > depositWeight && depositLog > depositOk,
        "account deposit must order dirty -> add -> delete -> weight -> SM701 -> type1 log");
    Assert(!deposit.Contains("SaveHumanRcd", StringComparison.Ordinal)
           && !deposit.Contains("M2Share.AddGameDataLog(", StringComparison.Ordinal)
           && !deposit.Contains("NeedIdentify", StringComparison.Ordinal),
        "account deposit must not force character save or use gated TAB log");

    var takeBack = Slice(source,
        "internal void ClientNativeAccountTakeBackStorageItem(",
        "internal void RejectUnsupportedStorageItem(");
    var prohibitGate = takeBack.IndexOf("if (!m_boCanGetBackItem)",
        StringComparison.Ordinal);
    var dealingGate = takeBack.IndexOf("if (m_boDealing)",
        StringComparison.Ordinal);
    var takeBackNpcGate = takeBack.IndexOf("if (!IsNativeAccountStorageNpc(objectId))",
        StringComparison.Ordinal);
    Assert(prohibitGate >= 0 && dealingGate > prohibitGate
           && takeBackNpcGate > dealingGate,
        "account take-back gates must be native -3 then -2 then NPC/0");
    Assert(!takeBack.Contains("m_boPasswordLocked", StringComparison.Ordinal)
           && !takeBack.Contains("m_nPayMent", StringComparison.Ordinal),
        "account take-back must not substitute password/payment state for native gates");
    var missingTemplate = takeBack.IndexOf("if (stdItem == null)",
        StringComparison.Ordinal);
    var addToBag = takeBack.IndexOf("AddItemToBag(item)",
        StringComparison.Ordinal);
    Assert(missingTemplate >= 0 && addToBag > missingTemplate,
        "missing template must fail before bag insertion");
    Assert(takeBack.Contains("IsAddWeightAvailable(stdItem.Weight)",
            StringComparison.Ordinal),
        "take-back weight must use the validated template");
    var accountAdd = takeBack.IndexOf("SendAddItem(item);",
        StringComparison.Ordinal);
    var accountDirty = takeBack.IndexOf("state.Dirty = true;",
        StringComparison.Ordinal);
    var accountRemove = takeBack.IndexOf("state.Items.RemoveAt(itemIndex);",
        StringComparison.Ordinal);
    var accountOk = takeBack.IndexOf(
        "Grobal2.SM_TAKEBACKSTORAGEITEM_OK", StringComparison.Ordinal);
    var accountLog = takeBack.IndexOf(
        "LogNativeAccountStorageItem(item, stdItem, 0x02);",
        StringComparison.Ordinal);
    Assert(accountAdd >= 0 && accountDirty > accountAdd
           && accountRemove > accountDirty && accountOk > accountRemove
           && accountLog > accountOk,
        "account take-back must order SM200 -> dirty -> delete -> SM705 -> type2 log");
    Assert(!takeBack.Contains("SendSaveItemList", StringComparison.Ordinal)
           && !takeBack.Contains("SM_SAVEITEMLIST", StringComparison.Ordinal)
           && !takeBack.Contains("SaveHumanRcd", StringComparison.Ordinal)
           && !takeBack.Contains("M2Share.AddGameDataLog(",
               StringComparison.Ordinal),
        "account take-back must not emit SM704, force character save, or use TAB log");
    Assert(takeBack.Contains(
               "FindNativeStorageItemIndex(state.Items, clientItemId)",
               StringComparison.Ordinal)
           && !takeBack.Contains("ClientItemIdMatches(state.Items",
               StringComparison.Ordinal),
        "account-storage lookup must compare preassigned ids without lazy allocation");

    var npcGate = Slice(source,
        "private bool IsNativeAccountStorageNpc(",
        "private void SendNativeAccountStorageFailure(");
    foreach (var required in new[]
             {
                 "m_NPC == null", "m_NPC.ObjectId != objectId",
                 "m_NPC.m_PEnvir != m_PEnvir",
                 "Math.Abs(m_NPC.m_nCurrX - m_nCurrX) > 15",
                 "Math.Abs(m_NPC.m_nCurrY - m_nCurrY) > 15"
             })
        Assert(npcGate.Contains(required, StringComparison.Ordinal),
            "missing exact current-NPC gate: " + required);
    foreach (var forbidden in new[]
             {
                 "Merchant", "m_boStorage", "m_boGetback", ">= 15"
             })
        Assert(!npcGate.Contains(forbidden, StringComparison.Ordinal),
            "over-restrictive current-NPC gate: " + forbidden);

    Assert(!deposit.Contains("SaveHumanRcd", StringComparison.Ordinal)
           && !deposit.Contains("m_nPayMent", StringComparison.Ordinal)
           && !takeBack.Contains("SaveHumanRcd", StringComparison.Ordinal),
        "account store/take-back must use native persistence and receiver gates");
    var storageLogStart = source.IndexOf(
        "private void LogNativeAccountStorageItem(",
        StringComparison.Ordinal);
    Assert(storageLogStart >= 0, "account-storage log helper is missing");
    var storageLog = source[storageLogStart..];
    Assert(storageLog.Contains(
            "NativeAccountStorageClient.GetGameDataLogQuantity(",
            StringComparison.Ordinal)
        && storageLog.Contains("M2Share.AddNativeGameDataLog(",
            StringComparison.Ordinal)
        && storageLog.Contains("\"账号仓库\"", StringComparison.Ordinal)
        && !storageLog.Contains("M2Share.AddGameDataLog(",
            StringComparison.Ordinal),
        "account-storage log must emit the native pile quantity");

    var dealSourcePath = Path.Combine(Directory.GetCurrentDirectory(),
        "GameSvr", "Players", "TPlayObject.Operate.cs");
    var quantitySourcePath = Path.Combine(Directory.GetCurrentDirectory(),
        "GameSvr", "Services", "NativeAccountStorageClient.cs");
    if (!File.Exists(dealSourcePath) || !File.Exists(quantitySourcePath))
        throw new FileNotFoundException(
            "run this audit from the repository root", dealSourcePath);

    var dealSource = File.ReadAllText(dealSourcePath);
    var normalDeposit = Slice(dealSource,
        "internal void ClientStorageItem(",
        "internal void ClientTakeBackStorageItem(");
    var normalDepositAdd = normalDeposit.IndexOf(
        "m_StorageItemList.Add(UserItem);", StringComparison.Ordinal);
    var normalDepositRemove = normalDeposit.IndexOf(
        "m_ItemList.RemoveAt(i);", StringComparison.Ordinal);
    var normalDepositWeight = normalDeposit.IndexOf(
        "WeightChanged();", StringComparison.Ordinal);
    var normalDepositOk = normalDeposit.IndexOf(
        "SendStorageItemOk(UserItem);", StringComparison.Ordinal);
    var normalDepositLog = normalDeposit.IndexOf(
        "M2Share.AddNativeGameDataLog(this, 0x01", StringComparison.Ordinal);
    Assert(normalDepositAdd >= 0 && normalDepositRemove > normalDepositAdd
           && normalDepositWeight > normalDepositRemove
           && normalDepositOk > normalDepositWeight
           && normalDepositLog > normalDepositOk,
        "personal deposit must order add -> delete -> weight -> SM701 -> type1 log");
    foreach (var forbidden in new[]
             {
                 "SaveHumanRcd", "M2Share.AddGameDataLog(", "NeedIdentify"
             })
        Assert(!normalDeposit.Contains(forbidden, StringComparison.Ordinal),
            "personal deposit still contains non-native success behavior: "
            + forbidden);
    foreach (var required in new[]
             {
                 "var storageNpc = m_NPC;",
                 "storageNpc.m_PEnvir == m_PEnvir",
                 "Math.Abs(storageNpc.m_nCurrX - m_nCurrX) <= 15",
                 "Math.Abs(storageNpc.m_nCurrY - m_nCurrY) <= 15"
             })
        Assert(normalDeposit.Contains(required, StringComparison.Ordinal),
            "personal deposit lost native cached-NPC gate: " + required);
    foreach (var forbidden in new[]
             {
                 "FindMerchant", "Merchant", "m_boStorage",
                 "sMsg.IndexOf", "GetValidStr3", "ItmUnit.GetItemName",
                 "string.Compare", "m_nPayMent", "boTryModeUseStorage",
                 "g_sTryModeCanotUseStorage"
             })
        Assert(!normalDeposit.Contains(forbidden, StringComparison.Ordinal),
            "personal deposit retains a non-native NPC receiver gate: "
            + forbidden);

    var normalTakeBackStart = dealSource.IndexOf(
        "internal void ClientTakeBackStorageItem(", StringComparison.Ordinal);
    Assert(normalTakeBackStart >= 0, "personal take-back source is missing");
    var normalTakeBack = dealSource[normalTakeBackStart..];
    var normalAdd = normalTakeBack.IndexOf("SendAddItem(UserItem);",
        StringComparison.Ordinal);
    var normalRemove = normalTakeBack.IndexOf(
        "m_StorageItemList.RemoveAt(i);", StringComparison.Ordinal);
    var normalOk = normalTakeBack.IndexOf(
        "Grobal2.SM_TAKEBACKSTORAGEITEM_OK", StringComparison.Ordinal);
    var normalLog = normalTakeBack.IndexOf(
        "M2Share.AddNativeGameDataLog(this, 0x02", StringComparison.Ordinal);
    Assert(normalAdd >= 0 && normalRemove > normalAdd
           && normalOk > normalRemove && normalLog > normalOk,
        "personal take-back must order SM200 -> delete -> SM705 -> type2 log");
    foreach (var forbidden in new[]
             {
                 "SendSaveItemList", "SM_SAVEITEMLIST", "SaveHumanRcd",
                 "M2Share.AddGameDataLog(", "NeedIdentify"
             })
        Assert(!normalTakeBack.Contains(forbidden, StringComparison.Ordinal),
            "personal take-back still contains non-native success behavior: "
            + forbidden);

    var dealEnd = Slice(dealSource,
        "private void ClientDealEnd()", "private void ClientGetMinMap()");
    Equal(2, CountOccurrences(dealEnd,
            "GetGameDataLogQuantity(StdItem, UserItem)"),
        "deal item quantity helper calls");
    var outgoingItems = Slice(dealEnd,
        "for (var i = 0; i < m_DealItemList.Count; i++)",
        "if (m_nDealGolds > 0)");
    var incomingItems = Slice(dealEnd,
        "for (var i = 0; i < m_DealCreat.m_DealItemList.Count; i++)",
        "if (m_DealCreat.m_nDealGolds > 0)");
    foreach (var itemLoop in new[] { outgoingItems, incomingItems })
    {
        Assert(itemLoop.Contains(
                "GetGameDataLogQuantity(StdItem, UserItem)",
                StringComparison.Ordinal)
            && itemLoop.Contains("+ logQuantity +", StringComparison.Ordinal),
            "both deal directions must log the native pile quantity");
        Assert(!itemLoop.Contains("+ \"\\t\" + '1' +",
                StringComparison.Ordinal),
            "deal item log must not hard-code quantity one");
    }

    var quantitySource = File.ReadAllText(quantitySourcePath);
    Assert(quantitySource.Contains(
            "NativeItemFactory.IsPileItem(stdItem) ? item.Dura : 1",
            StringComparison.Ordinal),
        "log quantity must use the native TBasePileItem class mapping");
}

int CountOccurrences(string source, string value)
{
    var count = 0;
    var start = 0;
    while ((start = source.IndexOf(value, start,
               StringComparison.Ordinal)) >= 0)
    {
        count++;
        start += value.Length;
    }

    return count;
}

string Slice(string source, string startMarker, string endMarker)
{
    var start = source.IndexOf(startMarker, StringComparison.Ordinal);
    var end = source.IndexOf(endMarker, start + 1, StringComparison.Ordinal);
    Assert(start >= 0 && end > start,
        $"source boundaries missing: {startMarker} -> {endMarker}");
    return source[start..end];
}

NativeAccountStorageResponseDisposition Process(LegacyDbServerFrame frame,
    TPlayObject player, Func<byte[], TUserItem> decoder,
    Action<TPlayObject, NativeAccountStorageState> publisher) =>
    NativeAccountStorageClient.ProcessResponse(frame,
        name => name.AsSpan().SequenceEqual(
            Bytes(player.m_sCharName)) ? player : null,
        decoder, publisher);

LegacyDbServerFrame Response(ushort command, ushort status, int publishFlag,
    string account, string character, byte[] tail = null)
{
    tail ??= Array.Empty<byte>();
    var payload = new byte[NativeAccountStorageClient.HeaderSize + tail.Length];
    BinaryPrimitives.WriteUInt16LittleEndian(payload, command);
    BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(2, 2), status);
    BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(4, 4), publishFlag);
    WriteShortString(payload, 0x10, account);
    WriteShortString(payload, 0x25, character);
    tail.CopyTo(payload, NativeAccountStorageClient.HeaderSize);
    return new LegacyDbServerFrame(1, 0, payload);
}

byte[] StorageTail(ushort capacity, params byte[][] records)
{
    var tail = new byte[4 + records.Length * NativeAccountStorageClient.ItemSize];
    BinaryPrimitives.WriteUInt16LittleEndian(tail, capacity);
    BinaryPrimitives.WriteUInt16LittleEndian(tail.AsSpan(2, 2),
        (ushort)records.Length);
    for (var i = 0; i < records.Length; i++)
        records[i].CopyTo(tail, 4 + i * NativeAccountStorageClient.ItemSize);
    return tail;
}

byte[] ItemRecord(int makeIndex, ushort itemIndex, ushort day)
{
    var record = new byte[NativeAccountStorageClient.ItemSize];
    BinaryPrimitives.WriteInt32LittleEndian(record, makeIndex);
    BinaryPrimitives.WriteUInt16LittleEndian(record.AsSpan(4, 2), itemIndex);
    BinaryPrimitives.WriteUInt16LittleEndian(record.AsSpan(6, 2), 10);
    BinaryPrimitives.WriteUInt16LittleEndian(record.AsSpan(8, 2), 20);
    BinaryPrimitives.WriteUInt16LittleEndian(record.AsSpan(0x14, 2), day);
    return record;
}

TPlayObject Player(string character, string account)
{
    var player = (TPlayObject)RuntimeHelpers.GetUninitializedObject(
        typeof(TPlayObject));
    player.m_sCharName = character;
    player.m_sUserID = account;
    player.m_boReadyRun = true;
    player.m_boGhost = false;
    return player;
}

void WriteShortString(byte[] payload, int offset, string value)
{
    var bytes = Bytes(value);
    payload[offset] = (byte)bytes.Length;
    bytes.CopyTo(payload, offset + 1);
}

byte[] Bytes(string value) => HUtil32.GbkEncoding.GetBytes(value ?? string.Empty);

void PrepareConfig()
{
    var baseDir = AppContext.BaseDirectory;
    File.WriteAllText(Path.Combine(baseDir, "!Setup.txt"), "[Server]\r\n");
    File.WriteAllText(Path.Combine(baseDir, "String.ini"), "[String]\r\n");
    File.WriteAllText(Path.Combine(baseDir, "Command.conf"), "[Command]\r\n");
    var share = Path.GetFullPath(Path.Combine(baseDir, "..", "Share"));
    Directory.CreateDirectory(share);
    File.WriteAllText(Path.Combine(share, "PlayerUpgradeExp.ini"),
        "[PlayerLevelExp]\r\nLEVEL_1=50\r\n");
}

void Run(string name, Action test)
{
    try
    {
        test();
        Console.WriteLine("PASS " + name);
    }
    catch (Exception ex)
    {
        failures.Add("FAIL " + name + ": " + ex.Message);
    }
}

void Assert(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

void Equal<T>(T expected, T actual, string name)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
        throw new InvalidOperationException(
            $"{name}: expected={expected} actual={actual}");
}
