using System.Globalization;
using SystemModule;

namespace GameSvr
{
    public partial class TPlayObject
    {
        internal const string NativeMagicTowerNextPrizeDialog =
            "您给我一张灵符，我可以直接带您进入下一关\\" +
            "{cmd}<不，我要回天庭/@RgivUp>          |" +
            "<是的，进入下一关/@JinRuTong>\\";
        internal const string NativeMagicTowerSkyNextPrizeDialog =
            "\\您给我一张灵符，我可以直接带您进入下一关，您是否愿意？\\ \\" +
            "{cmd}<不，我要回天庭/@RgivUp>          |" +
            "<是的，进入下一关/@JinRuTong>\\";
        internal const string NativeMagicTowerSkyPrizeFailureMessage =
            "[失败]：怪物未被完全消灭，或你已得到本关宝物。";

        private const string NativeMagicTowerHiddenPrize = "神秘天赐";
        private const string NativeMagicTowerDiamondHundredPrize = "金刚石:100";
        private const string NativeMagicTowerDiamondLogName = "金刚宝石";
        private const string NativeMagicTowerDiamondLogReason = "闯天关大奖";

        // Original transient descriptors at TPlayer +D14/+D18/+D1C.
        internal string m_sNativeMagicTowerPrimaryPrize = string.Empty;
        internal string m_sNativeMagicTowerPersonalPrize = string.Empty;
        internal string m_sNativeMagicTowerServerPrize = string.Empty;
        internal int m_nNativeMagicTowerAllKilledCount;

        internal bool ClientGetNativeMagicTowerPrize(NormNpc npc)
        {
            return ClientGetNativeMagicTowerPrize(npc,
                NextNativeMagicTowerLegacyRandom);
        }

        internal bool ClientGetNativeMagicTowerPrize(NormNpc npc,
            Func<int, int> random)
        {
            if (npc == null || !npc.HasNativePasProperty(12)) return false;
            random ??= _ => 0;

            lock (m_CreditCard.SyncRoot)
            {
                if (m_btNativeMagicTowerPhase == 4)
                {
                    SendNativeMagicTowerPrizeDialog(npc,
                        NativeMagicTowerNextPrizeDialog);
                    return false;
                }

                if (m_btNativeMagicTowerPhase != 3)
                {
                    SendNativeMagicTowerPrizeDialog(npc,
                        "您消灭的怪物太少了吧！\\ \\" +
                        NativeMagicTowerNextPrizeDialog);
                    return false;
                }

                var defeated = m_btNativeMagicTowerDefeatedMonsterCount;
                m_btNativeMagicTowerDefeatedMonsterCount = 0;
                m_btNativeMagicTowerPhase = 4;

                var experience = GetNativeMagicTowerNewExperience(random);
                string tierPrize;
                if (defeated is >= 41 and <= 46)
                {
                    tierPrize = "铜天赐";
                }
                else if (defeated is >= 47 and <= 49)
                {
                    tierPrize = "银天赐";
                }
                else if (defeated is >= 50 and <= 80)
                {
                    tierPrize = "金天赐";
                    m_nNativeMagicTowerAllKilledCount = unchecked(
                        m_nNativeMagicTowerAllKilledCount + 1);
                }
                else
                {
                    tierPrize = "木天赐";
                    experience = 0;
                }

                _ = TryGiveNativeMagicTowerSingleItem(tierPrize, npc);
                var dialog = "您本次总共阻击了 " + defeated +
                             " 个怪物\\您获得了：" + tierPrize + "\\";
                var systemMessage = defeated is >= 50 and <= 80
                    ? m_sCharName + " 在魔王岭歼灭了魔王大军的全部怪物！"
                    : string.Empty;

                if (experience > 0)
                {
                    var hero = m_HeroObject;
                    if (hero != null && !hero.m_boDeath)
                    {
                        var percentage = (hero.m_Abil.Level - 1) / 5 + 5;
                        if (percentage > 10) percentage = 10;
                        var heroExperience = unchecked(experience * percentage / 100);
                        GrantNativeHeroExperience(hero, heroExperience, true, false);
                    }

                    GrantNativePlayerExperience(experience, false, true, 0);
                    dialog += "经验 : " + experience + "点\\";
                }

                if (m_btNativeMagicTowerMysteryFlag != 0)
                {
                    if (defeated >= 47)
                    {
                        _ = TryGiveNativeMagicTowerSingleItem(
                            NativeMagicTowerHiddenPrize, npc);
                        dialog += "您消灭了足够多的怪物，赢得了隐藏关中的奖励：" +
                                  "<神秘天赐/C=RED>\\";
                    }
                    else
                    {
                        dialog += "<您阻击的怪物数量不足，未能获得隐藏关中的奖励" +
                                  "/C=GREEN>\\";
                    }
                    m_btNativeMagicTowerMysteryFlag = 0;
                }

                var catalog = NativeMagicTowerPrizeCatalog.Capture();
                var route = m_btNativeMagicTowerSpecialRoute;
                if (route is >= 1 and <= 5)
                {
                    var serverPrize = SelectNativeMagicTowerThresholdPrize(
                        catalog.ServerPrizes[route - 1],
                        NextNativeMagicTowerRoll(random));
                    if (TryGiveNativeMagicTowerSingleItem(serverPrize, npc))
                    {
                        dialog += "服务器大奖：" + serverPrize + "\\";
                        systemMessage += m_sCharName +
                            " 在魔王岭拦截逃离的怪物有功,魔王岭守卫给予 " +
                            serverPrize + " 作为奖励！";
                    }
                    m_btNativeMagicTowerSpecialRoute = 0;
                }

                if (m_boNativeMagicTowerHundredth)
                {
                    var personalPrize = SelectNativeMagicTowerThresholdPrize(
                        catalog.PersonalPrizes,
                        NextNativeMagicTowerRoll(random));
                    if (TryGiveNativeMagicTowerSingleItem(personalPrize, npc))
                    {
                        dialog += "个人大奖：" + personalPrize + "\\";
                        systemMessage += m_sCharName +
                            " 在魔王岭拦截逃离的怪物有功,魔王岭守卫给予 " +
                            personalPrize + " 作为奖励！";
                    }
                    m_boNativeMagicTowerHundredth = false;
                }

                SendNativeMagicTowerPrizeDialog(npc,
                    dialog + NativeMagicTowerNextPrizeDialog);
                if (!string.IsNullOrEmpty(systemMessage))
                    SendNativeMagicTowerSystemMessage(systemMessage, 0x38);
                return true;
            }
        }

        internal bool GetNativeMagicTowerSkyPrize(NormNpc npc)
        {
            lock (m_CreditCard.SyncRoot)
            {
                if (m_btNativeMagicTowerPhase != 4 ||
                    HasNativeMagicTowerBlockingMonster())
                {
                    SendNativeMagicTowerPrizeDialog(npc,
                        NativeMagicTowerSkyPrizeFailureMessage);
                    return false;
                }

                m_btNativeMagicTowerPhase = 0;
                var primaryPrize = m_sNativeMagicTowerPrimaryPrize ?? string.Empty;
                if (primaryPrize.Length != 0)
                    _ = TryGiveNativeMagicTowerDescriptor(primaryPrize, npc);

                var serverPrize = m_sNativeMagicTowerServerPrize ?? string.Empty;
                if (serverPrize.Length != 0)
                {
                    if (string.Equals(serverPrize,
                            NativeMagicTowerDiamondHundredPrize,
                            StringComparison.Ordinal))
                    {
                        m_nNativeDiamondCache = unchecked(
                            m_nNativeDiamondCache + 100);
                        M2Share.AddGameDataLog(string.Join('\t', 50,
                            m_sMapName, m_nCurrX, m_nCurrY, m_sCharName,
                            NativeMagicTowerDiamondLogName, 100, 1,
                            NativeMagicTowerDiamondLogReason));
                    }
                    else
                    {
                        _ = TryGiveNativeMagicTowerSingleItem(serverPrize, npc);
                    }

                    BroadcastNativeMagicTowerPrize(serverPrize);
                    m_sNativeMagicTowerServerPrize = string.Empty;
                    m_btNativeMagicTowerSpecialRoute = 0;
                }

                if (m_boNativeMagicTowerHundredth)
                {
                    var personalPrize = m_sNativeMagicTowerPersonalPrize ??
                                        string.Empty;
                    _ = TryGiveNativeMagicTowerSingleItem(personalPrize, npc);
                    BroadcastNativeMagicTowerPrize(personalPrize);
                    m_boNativeMagicTowerHundredth = false;
                    m_sNativeMagicTowerPersonalPrize = string.Empty;
                }

                var resultMessage = "经过你的奋斗，你终于获得了[" +
                                    primaryPrize +
                                    "]\\可能有更好的宝藏在下一关等着您哦！";
                m_sNativeMagicTowerPrimaryPrize = string.Empty;
                SendNativeMagicTowerSystemMessage(resultMessage, 0xFC);
                SendNativeMagicTowerPrizeDialog(npc,
                    resultMessage + NativeMagicTowerSkyNextPrizeDialog);
                return true;
            }
        }

        private int GetNativeMagicTowerNewExperience(Func<int, int> random)
        {
            var catalog = NativeMagicTowerPrizeCatalog.Capture();
            var minimum = catalog.MinimumExperience;
            var maximum = catalog.MaximumExperience;
            var span = unchecked(maximum - minimum);
            var addition = random(span);
            return unchecked(minimum + addition / 10_000 * 10_000);
        }

        internal static string SelectNativeMagicTowerThresholdPrize(string path,
            string section, int roll)
        {
            return SelectNativeMagicTowerThresholdPrize(
                ReadNativeMagicTowerThresholdEntries(path, section), roll);
        }

        private static string SelectNativeMagicTowerThresholdPrize(
            IReadOnlyList<NativeMagicTowerThresholdEntry> entries, int roll)
        {
            for (var index = 0; index < entries.Count; index++)
            {
                if (roll <= entries[index].Threshold)
                    return entries[index].Descriptor;
            }
            return string.Empty;
        }

        private static NativeMagicTowerThresholdEntry[]
            ReadNativeMagicTowerThresholdEntries(string path, string section)
        {
            var values = ReadNativeMagicTowerIniSection(path, section);
            return values
                .Where(pair => pair.Key.StartsWith("爆物",
                    StringComparison.Ordinal))
                .Select(pair => new
                {
                    Pair = pair,
                    Index = ParseNativeMagicTowerInteger(
                        pair.Key.AsSpan(2).ToString())
                })
                .OrderBy(entry => entry.Index)
                .Select(entry =>
                {
                    var separator = entry.Pair.Value.LastIndexOf('/');
                    if (separator <= 0)
                        return new NativeMagicTowerThresholdEntry(
                            string.Empty, int.MinValue);
                    return new NativeMagicTowerThresholdEntry(
                        entry.Pair.Value[..separator].Trim(),
                        ParseNativeMagicTowerInteger(
                            entry.Pair.Value[(separator + 1)..]));
                })
                .Where(entry => entry.Descriptor.Length != 0)
                .ToArray();
        }

        private static Dictionary<string, string> ReadNativeMagicTowerIniSection(
            string path, string wantedSection)
        {
            var result = new Dictionary<string, string>(StringComparer.Ordinal);
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return result;

            string currentSection = string.Empty;
            foreach (var rawLine in File.ReadLines(path, HUtil32.GbkEncoding))
            {
                var line = rawLine.Trim();
                if (line.Length == 0 || line[0] is ';' or '#') continue;
                if (line[0] == '[' && line[^1] == ']')
                {
                    currentSection = line[1..^1].Trim();
                    continue;
                }
                if (!string.Equals(currentSection, wantedSection,
                        StringComparison.Ordinal)) continue;
                var equals = line.IndexOf('=');
                if (equals <= 0) continue;
                result[line[..equals].Trim()] = line[(equals + 1)..].Trim();
            }
            return result;
        }

        private static int ParseNativeMagicTowerInteger(string value)
        {
            return int.TryParse(value, NumberStyles.Integer,
                CultureInfo.InvariantCulture, out var result) ? result : 0;
        }

        private static int NextNativeMagicTowerRoll(Func<int, int> random)
        {
            var roll = random(100);
            return roll is >= 0 and < 100 ? roll : 0;
        }

        private static int NextNativeMagicTowerLegacyRandom(int range)
        {
            if (range > 0) return M2Share.RandomNumber.Random(range);
            // Keep the current process owner in sequence for the native
            // Random(0/negative) call. Exact Delphi output remains gated on
            // migration of every process-global Random consumer.
            _ = M2Share.RandomNumber.Random();
            return 0;
        }

        private bool HasNativeMagicTowerBlockingMonster()
        {
            var environment = m_PEnvir;
            if (environment == null) return false;
            for (var x = 0; x < environment.wWidth; x++)
            {
                for (var y = 0; y < environment.wHeight; y++)
                {
                    var valid = false;
                    var cell = environment.GetMapCellInfo(x, y, ref valid);
                    if (!valid || cell.ObjList == null) continue;
                    for (var index = 0; index < cell.ObjList.Count; index++)
                    {
                        var entry = cell.ObjList[index];
                        if (entry?.CellType != CellType.OS_MOVINGOBJECT ||
                            entry.CellObj is not TBaseObject actor ||
                            actor.m_boGhost || actor.m_boDeath ||
                            actor.m_btRaceServer < Grobal2.RC_ANIMAL ||
                            actor.GetMaster() != null)
                            continue;
                        return true;
                    }
                }
            }
            return false;
        }

        private bool TryGiveNativeMagicTowerDescriptor(string descriptor,
            NormNpc npc)
        {
            if (string.IsNullOrEmpty(descriptor)) return false;
            var separator = descriptor.IndexOf(':');
            var name = separator < 0 ? descriptor : descriptor[..separator];
            var amount = separator < 0 ? 1 : ParseNativeMagicTowerInteger(
                descriptor[(separator + 1)..]);
            if (amount <= 0) amount = 1;

            var showSuccess = true;
            var success = true;
            switch (name)
            {
                case "经验":
                    GrantNativePlayerExperience(amount, true, true, 0);
                    showSuccess = false;
                    break;
                case "英雄经验":
                    showSuccess = false;
                    if (m_HeroObject == null)
                    {
                        SendNativeMagicTowerSystemMessage(
                            "请先将您的英雄召唤出来！", 0xFF);
                    }
                    else if (m_HeroObject.m_Abil.Level == 200)
                    {
                        m_HeroObject.SendMsg(m_HeroObject,
                            Grobal2.RM_SYSMESSAGE, 0, 0xDB, 0xFF, 0,
                            "你的英雄级数已满");
                        showSuccess = true;
                    }
                    else
                    {
                        GrantNativeHeroExperience(m_HeroObject, amount,
                            false, true);
                        showSuccess = true;
                    }
                    break;
                case "灵符":
                case "限时灵符":
                    success = GiveNativeMagicTowerLingFu(amount, npc);
                    break;
                case "牛气值":
                    success = AddNativeCattle(amount);
                    break;
                case "声望":
                    m_nShengWan = unchecked(m_nShengWan + amount);
                    break;
                case "金币":
                    m_nGold = unchecked(m_nGold + amount);
                    break;
                case "荣耀点":
                    success = GiveNativeMagicTowerGlory(amount);
                    showSuccess = false;
                    break;
                default:
                    success = TryGiveNativeMagicTowerItems(name, amount, npc);
                    break;
            }

            if (success && showSuccess)
                SendNativeMagicTowerSystemMessage(
                    "恭喜：你获得了：" + descriptor, 0xFC);
            return success;
        }

        private bool TryGiveNativeMagicTowerItems(string itemName, int count,
            NormNpc npc)
        {
            var gaveAny = false;
            while (count > 0)
            {
                TUserItem item = null;
                if (M2Share.UserEngine == null ||
                    !M2Share.UserEngine.CopyToUserItemFromName(itemName,
                        ref item) || item == null)
                {
                    if (m_btPermission > 3)
                        SendNativeMagicTowerSystemMessage(
                            "[错误]：不存在的奖品：" + itemName, 0x38);
                    break;
                }

                var standardItem = M2Share.UserEngine.GetStdItem(item.wIndex);
                if (standardItem == null)
                {
                    Dispose(item);
                    break;
                }

                var quantity = 1;
                if (standardItem.StdMode == 7)
                {
                    if (item.DuraMax == 0)
                    {
                        Dispose(item);
                        break;
                    }
                    quantity = Math.Min(count, item.DuraMax);
                    item.Dura = unchecked((ushort)quantity);
                }

                if (!AddItemToBag(item))
                {
                    Dispose(item);
                    break;
                }
                SendAddItem(item);
                WriteNativeMagicTowerGiveLog(standardItem.Name, item,
                    quantity, npc);
                gaveAny = true;
                count -= quantity;
            }
            return gaveAny;
        }

        private bool TryGiveNativeMagicTowerSingleItem(string itemName,
            NormNpc npc)
        {
            if (string.IsNullOrEmpty(itemName) || M2Share.UserEngine == null)
                return false;
            TUserItem item = null;
            if (!M2Share.UserEngine.CopyToUserItemFromName(itemName, ref item) ||
                item == null)
                return false;
            var standardItem = M2Share.UserEngine.GetStdItem(item.wIndex);
            if (standardItem == null || !AddItemToBag(item))
            {
                Dispose(item);
                return false;
            }
            SendAddItem(item);
            WriteNativeMagicTowerGiveLog(standardItem.Name, item, 1, npc);
            return true;
        }

        private bool GiveNativeMagicTowerLingFu(int amount, NormNpc npc)
        {
            if (amount <= 0) return false;
            var creditService = M2Share.CreditCardService ??
                                NativeCreditCardService.Disabled;
            if (!creditService.Enabled)
                return AddNativeLingFu(23001, amount);

            var value = unchecked(m_CreditCard.Value + amount);
            m_CreditCard.Value = value < 0 ? 0 : value;
            m_CreditCard.Dirty = true;
            m_CreditCard.DirtyVersion++;
            RefreshNativeLingFu();
            M2Share.AddGameDataLog(string.Join('\t', 9, m_sMapName,
                m_nCurrX, m_nCurrY, m_sCharName, "限时灵符", 23002,
                amount, npc == null ? string.Empty :
                    "npc给予" + npc.m_sCharName + '-' + npc.m_sMapName));
            return true;
        }

        private bool GiveNativeMagicTowerGlory(int amount)
        {
            if (amount <= 0) return false;
            SendNativeMagicTowerSystemMessage(amount + "点荣耀点增加", 0xDB);
            m_CreditCard.GloryPointValue = unchecked(
                m_CreditCard.GloryPointValue + amount);
            m_CreditCard.GloryPointDirty = true;
            m_CreditCard.GloryPointDirtyVersion++;
            RefreshNativeLingFu();
            M2Share.AddGameDataLog(string.Join('\t', 9, m_sMapName,
                m_nCurrX, m_nCurrY, m_sCharName, "荣耀点", 888888,
                amount, "系统给予"));
            return true;
        }

        private void WriteNativeMagicTowerGiveLog(string itemName,
            TUserItem item, int quantity, NormNpc npc)
        {
            var description = npc == null ? "系统给予" :
                "npc给予" + npc.m_sCharName + '-' + npc.m_sMapName;
            M2Share.AddGameDataLog(string.Join('\t', 9, m_sMapName,
                m_nCurrX, m_nCurrY, m_sCharName, itemName,
                item.MakeIndex, quantity, description));
        }

        private void BroadcastNativeMagicTowerPrize(string descriptor)
        {
            var message = "恭喜：" + m_sCharName +
                          " 在闯天关活动中获得了：" + descriptor +
                          " 如您也想参加，请和各地老兵对话，点击[闯天关]进入天庭即可！";
            if (M2Share.UserEngine == null) return;
            foreach (var player in M2Share.UserEngine.PlayObjects)
            {
                if (player == null || player.m_boGhost) continue;
                player.SendMsg(player, Grobal2.RM_SYSMESSAGE, 0,
                    0xFF, 0x38, 0, message);
            }
        }

        private void SendNativeMagicTowerSystemMessage(string message,
            int backgroundColor)
        {
            SendNativeMagicTowerSystemMessage(message, 0xFF,
                backgroundColor);
        }

        private void SendNativeMagicTowerSystemMessage(string message,
            int foregroundColor, int backgroundColor)
        {
            SendMsg(this, Grobal2.RM_SYSMESSAGE, 0, foregroundColor,
                backgroundColor, 0, message ?? string.Empty);
        }

        private void SendNativeMagicTowerPrizeDialog(NormNpc npc,
            string message)
        {
            m_NPC = npc;
            SendMsg(npc, Grobal2.RM_MERCHANTSAY, 0, 0, 0, 0,
                (npc?.m_sCharName ?? string.Empty) + "/" +
                (message ?? string.Empty));
        }

        internal static void InitializeNativeMagicTowerPrizeCatalog(
            string rootPath)
        {
            NativeMagicTowerPrizeCatalog.Initialize(rootPath);
        }

        private sealed class NativeMagicTowerPrizeCatalog
        {
            private static readonly object SyncRoot = new();
            private static NativeMagicTowerPrizeCatalog s_Catalog;

            private NativeMagicTowerPrizeCatalog()
            {
                for (var route = 0; route < ServerPrizes.Length; route++)
                    ServerPrizes[route] =
                        Array.Empty<NativeMagicTowerThresholdEntry>();
            }

            internal int MinimumExperience { get; private set; }
            internal int MaximumExperience { get; private set; }
            internal NativeMagicTowerThresholdEntry[][] ServerPrizes { get; } =
                new NativeMagicTowerThresholdEntry[5][];
            internal NativeMagicTowerThresholdEntry[] PersonalPrizes
                { get; private set; } =
                Array.Empty<NativeMagicTowerThresholdEntry>();

            internal static void Initialize(string rootPath)
            {
                lock (SyncRoot)
                    s_Catalog = Load(rootPath);
            }

            internal static NativeMagicTowerPrizeCatalog Capture()
            {
                lock (SyncRoot)
                {
                    s_Catalog ??= Load(M2Share.sRootPath);
                    return s_Catalog;
                }
            }

            private static NativeMagicTowerPrizeCatalog Load(string rootPath)
            {
                var result = new NativeMagicTowerPrizeCatalog();
                try
                {
                    if (string.IsNullOrEmpty(rootPath)) return result;
                    var configPath = Path.Combine(Path.GetFullPath(rootPath),
                        "Share", "config");
                    var expValues = ReadNativeMagicTowerIniSection(
                        Path.Combine(configPath, "NewExp.ini"), "配置");
                    result.MinimumExperience = ParseNativeMagicTowerInteger(
                        expValues.TryGetValue("最小经验", out var minimum)
                            ? minimum : null);
                    result.MaximumExperience = ParseNativeMagicTowerInteger(
                        expValues.TryGetValue("最大经验", out var maximum)
                            ? maximum : null);

                    var serverPath = Path.Combine(configPath,
                        "NewServPrize.ini");
                    if (!File.Exists(serverPath))
                    {
                        M2Share.ErrorMessage(
                            "[Error]: 缺少新天关服务器奖励配置文件：" +
                            serverPath);
                    }
                    for (var route = 0;
                         route < result.ServerPrizes.Length;
                         route++)
                        result.ServerPrizes[route] =
                            ReadNativeMagicTowerThresholdEntries(serverPath,
                                "配置" + (route + 1));
                    var personalPath = Path.Combine(configPath,
                        "NewSelfPrize.ini");
                    if (!File.Exists(personalPath))
                    {
                        M2Share.ErrorMessage(
                            "[Error]: 缺少新天关个人奖励配置文件：" +
                            personalPath);
                    }
                    result.PersonalPrizes =
                        ReadNativeMagicTowerThresholdEntries(personalPath,
                            "配置");
                    return result;
                }
                catch (Exception e)
                {
                    try
                    {
                        M2Share.ErrorMessage(
                            "[Exception]:TNewSkyQuest.LoadPrize: " +
                            e.Message);
                    }
                    catch
                    {
                    }
                    return new NativeMagicTowerPrizeCatalog();
                }
            }
        }
    }
}
