using System;
using System.Collections.Generic;
using System.IO;
using SystemModule;
using SystemModule.Common;

namespace GameSvr.Configs
{
    /// <summary>
    /// Hero template loaded from Share/hero.ini
    /// </summary>
    public class HeroTemplate
    {
        public string Name;       // 角色名
        public int Level;         // 等级
        public int Energy;        // 能量
        public int AwakenTime;    // 觉醒时间
        public int InnerPower;    // 内功
        public int XinFaLevel;    // 心法等级
        public string Role;       // 角色
    }

    /// <summary>
    /// Class drop/reward entry from Share/warr.ini, fashi.ini, taos.ini, assassin.ini
    /// Format: ItemName:Count or ItemName:Amount/LevelRequired
    /// </summary>
    public class ClassDropEntry
    {
        public string ItemName;
        public int Count;
    }

    public class ClassRewardEntry
    {
        public string ItemName;
        public int Amount;
        public int LevelRequired;
    }

    /// <summary>
    /// Self-pickup entry from Share/self100.ini
    /// </summary>
    public class SelfPickupEntry
    {
        public string ItemName;
        public int Weight; // cumulative probability
    }

    /// <summary>
    /// Big item / lottery entry from Share/bigitem.ini
    /// </summary>
    public class BigItemEntry
    {
        public string ItemName;
        public int Weight;
    }

    // ========================================================================
    // Individual INI file loaders (each inherits IniFile for GBK parsing)
    // ========================================================================

    /// <summary>
    /// Loads Share/PlayerUpgradeExp.ini — level exp requirements and rate multipliers
    /// Sections: [PlayerLevelExpRate] (rate multipliers), [PlayerLevelExp] (base exp)
    /// Key format: LEVEL_1=100, LEVEL_2=200, etc.
    /// </summary>
    public class PlayerUpgradeExpLoader : IniFile
    {
        /// <summary>Exp rate multipliers per level (index 1-99). Raw values from 20-54.</summary>
        public int[] LevelExpRate;

        /// <summary>Base exp per level (index 0-based). Values range 100 → 4250000000.</summary>
        public long[] LevelExp;

        /// <summary>Maximum level loaded from the file.</summary>
        public int MaxLevel;

        public PlayerUpgradeExpLoader(string fileName) : base(fileName)
        {
            LevelExpRate = new int[100]; // 1-99
            LevelExp = new long[Grobal2.MAXCHANGELEVEL];
            MaxLevel = 0;
            Load();
            LoadExpData();
        }

        private void LoadExpData()
        {
            // Load rate multipliers from [PlayerLevelExpRate]
            for (int i = 1; i <= 99; i++)
            {
                var key = $"LEVEL_{i}";
                var rate = ReadInteger("PlayerLevelExpRate", key, 0);
                if (rate > 0)
                {
                    LevelExpRate[i] = rate;
                    if (i > MaxLevel) MaxLevel = i;
                }
            }

            // Load base exp from [PlayerLevelExp]
            for (int i = 1; i <= MaxLevel; i++)
            {
                var key = $"LEVEL_{i}";
                var raw = ReadString("PlayerLevelExp", key, "");
                if (!string.IsNullOrEmpty(raw) && long.TryParse(raw, out long val))
                {
                    LevelExp[i - 1] = val;
                }
            }
        }

        /// <summary>
        /// Applies loaded exp data into M2Share.g_Config.dwNeedExps and g_dwOldNeedExps.
        /// Also applies exp rate to dwKillMonExpMultiple.
        /// </summary>
        public void ApplyToConfig()
        {
            // Apply rate multipliers
            for (int i = 1; i <= MaxLevel && i < M2Share.g_Config.dwNeedExps.Length; i++)
            {
                if (LevelExpRate[i] > 0)
                {
                    M2Share.g_Config.dwNeedExps[i] = LevelExpRate[i];
                    M2Share.g_dwOldNeedExps[i] = LevelExpRate[i];
                }
            }

            // Apply base exp values
            if (LevelExp[0] > 0)
            {
                M2Share.g_Config.nBaseExp = (int)LevelExp[0];
            }
        }
    }

    /// <summary>
    /// Loads class-specific quest/drop configs from Share/warr.ini, fashi.ini, taos.ini, assassin.ini
    /// Sections: [怪物1]-[怪物5] (monster drops), [奖励1]-[奖励5] (quest rewards)
    /// Also loads guardian variants: gwarr.ini, gfashi.ini, gtaos.ini
    /// </summary>
    public class ClassConfigLoader : IniFile
    {
        /// <summary>Monster drops: [怪物N] -> list of ItemName:Count</summary>
        public Dictionary<int, List<ClassDropEntry>> MonsterDrops = new Dictionary<int, List<ClassDropEntry>>();

        /// <summary>Quest rewards: [奖励N] -> list of ItemName:Amount/LevelRequired</summary>
        public Dictionary<int, List<ClassRewardEntry>> QuestRewards = new Dictionary<int, List<ClassRewardEntry>>();

        public ClassConfigLoader(string fileName) : base(fileName)
        {
            Load();
            ParseClassConfig();
        }

        private void ParseClassConfig()
        {
            for (int tier = 1; tier <= 5; tier++)
            {
                var monSection = $"怪物{tier}";
                var monKeys = GetSectionItemName(monSection);
                if (monKeys != null)
                {
                    var drops = new List<ClassDropEntry>();
                    foreach (var key in monKeys)
                    {
                        var val = ReadString(monSection, key, "");
                        if (!string.IsNullOrEmpty(val))
                        {
                            // Format: ItemName:Count or ItemName:Count/ItemName:Count
                            foreach (var part in val.Split('/'))
                            {
                                var colon = part.IndexOf(':');
                                if (colon > 0)
                                {
                                    drops.Add(new ClassDropEntry
                                    {
                                        ItemName = part.Substring(0, colon).Trim(),
                                        Count = int.TryParse(part.Substring(colon + 1).Trim(), out int c) ? c : 1
                                    });
                                }
                            }
                        }
                    }
                    if (drops.Count > 0) MonsterDrops[tier] = drops;
                }

                var rewSection = $"奖励{tier}";
                var rewKeys = GetSectionItemName(rewSection);
                if (rewKeys != null)
                {
                    var rewards = new List<ClassRewardEntry>();
                    foreach (var key in rewKeys)
                    {
                        var val = ReadString(rewSection, key, "");
                        if (!string.IsNullOrEmpty(val))
                        {
                            // Format: ItemName:Amount/LevelRequired
                            var slash = val.IndexOf('/');
                            if (slash > 0)
                            {
                                var itemPart = val.Substring(0, slash);
                                var levelStr = val.Substring(slash + 1);
                                var colon = itemPart.IndexOf(':');
                                rewards.Add(new ClassRewardEntry
                                {
                                    ItemName = (colon > 0) ? itemPart.Substring(0, colon).Trim() : itemPart.Trim(),
                                    Amount = (colon > 0 && int.TryParse(itemPart.Substring(colon + 1).Trim(), out int a)) ? a : 1,
                                    LevelRequired = int.TryParse(levelStr.Trim(), out int lr) ? lr : 1
                                });
                            }
                        }
                    }
                    if (rewards.Count > 0) QuestRewards[tier] = rewards;
                }
            }
        }

        /// <summary>
        /// Get monster drops for a given level range.
        /// Level ranges map to tiers: 1=1-15, 2=16-30, 3=31-45, 4=46-60, 5=61+
        /// </summary>
        public List<ClassDropEntry> GetMonsterDrops(int level)
        {
            int tier = LevelToTier(level);
            return MonsterDrops.ContainsKey(tier) ? MonsterDrops[tier] : new List<ClassDropEntry>();
        }

        /// <summary>
        /// Get quest rewards for a given level range.
        /// </summary>
        public List<ClassRewardEntry> GetQuestRewards(int level)
        {
            int tier = LevelToTier(level);
            return QuestRewards.ContainsKey(tier) ? QuestRewards[tier] : new List<ClassRewardEntry>();
        }

        private int LevelToTier(int level)
        {
            if (level <= 15) return 1;
            if (level <= 30) return 2;
            if (level <= 45) return 3;
            if (level <= 60) return 4;
            return 5;
        }
    }

    /// <summary>
    /// Loads Share/hero.ini — hero character templates
    /// Sections: [hero0] through [hero5]
    /// Keys: 角色名, 等级, 能量, 觉醒时间, 内功, 心法等级, 角色
    /// </summary>
    public class HeroConfigLoader : IniFile
    {
        public List<HeroTemplate> Heroes = new List<HeroTemplate>();

        public HeroConfigLoader(string fileName) : base(fileName)
        {
            Load();
            ParseHeroConfig();
        }

        private void ParseHeroConfig()
        {
            for (int i = 0; i <= 5; i++)
            {
                var section = $"hero{i}";
                var name = ReadString(section, "角色名", "");
                if (!string.IsNullOrEmpty(name))
                {
                    Heroes.Add(new HeroTemplate
                    {
                        Name = name,
                        Level = ReadInteger(section, "等级", 1),
                        Energy = ReadInteger(section, "能量", 0),
                        AwakenTime = ReadInteger(section, "觉醒时间", 0),
                        InnerPower = ReadInteger(section, "内功", 0),
                        XinFaLevel = ReadInteger(section, "心法等级", 0),
                        Role = ReadString(section, "角色", "")
                    });
                }
            }
        }
    }

    /// <summary>
    /// Loads Share/Mir2Actor.ini — actor/misc game settings
    /// Section: [Setup]
    /// Keys: THeroPointActor, THeroPointPeriod, LFMultiple, SafeZoneHPRecover,
    ///       SafeZoneMPRecover, unionMaxLv, PetSwitch, GlobalSeeZone, etc.
    /// </summary>
    public class Mir2ActorLoader : IniFile
    {
        // Hero point system
        public int THeroPointActor;
        public int THeroPointPeriod;

        // Lucky/Fortune multiplier
        public int LFMultiple;

        // Safe zone recovery
        public int SafeZoneHPRecover;
        public int SafeZoneMPRecover;

        // Union/guild max level
        public int UnionMaxLv;

        // Pet system switch
        public int PetSwitch;

        // Global vision zone
        public int GlobalSeeZone;

        // Raw key-value store for all unrecognized keys
        public Dictionary<string, string> RawValues = new Dictionary<string, string>();

        public Mir2ActorLoader(string fileName) : base(fileName)
        {
            THeroPointActor = 0;
            THeroPointPeriod = 0;
            LFMultiple = 1;
            SafeZoneHPRecover = 0;
            SafeZoneMPRecover = 0;
            UnionMaxLv = 5;
            PetSwitch = 0;
            GlobalSeeZone = 0;
            Load();
            ParseSetup();
        }

        private void ParseSetup()
        {
            var keys = GetSectionItemName("Setup");
            if (keys == null) return;

            foreach (var key in keys)
            {
                var val = ReadString("Setup", key, "");
                RawValues[key] = val;

                switch (key)
                {
                    case "THeroPointActor": THeroPointActor = HUtil32.Str_ToInt(val, 0); break;
                    case "THeroPointPeriod": THeroPointPeriod = HUtil32.Str_ToInt(val, 0); break;
                    case "LFMultiple": LFMultiple = HUtil32.Str_ToInt(val, 1); break;
                    case "SafeZoneHPRecover": SafeZoneHPRecover = HUtil32.Str_ToInt(val, 0); break;
                    case "SafeZoneMPRecover": SafeZoneMPRecover = HUtil32.Str_ToInt(val, 0); break;
                    case "unionMaxLv": UnionMaxLv = HUtil32.Str_ToInt(val, 5); break;
                    case "PetSwitch": PetSwitch = HUtil32.Str_ToInt(val, 0); break;
                    case "GlobalSeeZone": GlobalSeeZone = HUtil32.Str_ToInt(val, 0); break;
                }
            }
        }
    }

    /// <summary>
    /// Loads Share/serverinfo.ini — server network/identity info
    /// Section: [Setup]
    /// Keys: AreaID, GroupID, EncKey, Token (XML), VoiceAddr, VoicePort
    /// </summary>
    public class ServerInfoLoader : IniFile
    {
        public int AreaID;
        public int GroupID;
        public string EncKey;
        public string Token;     // XML content
        public string VoiceAddr;
        public int VoicePort;

        public ServerInfoLoader(string fileName) : base(fileName)
        {
            Load();
            ParseSetup();
        }

        private void ParseSetup()
        {
            AreaID = ReadInteger("Setup", "AreaID", 0);
            GroupID = ReadInteger("Setup", "GroupID", 0);
            EncKey = ReadString("Setup", "EncKey", "");
            Token = ReadString("Setup", "Token", "");
            VoiceAddr = ReadString("Setup", "VoiceAddr", "");
            VoicePort = ReadInteger("Setup", "VoicePort", 0);
        }
    }

    /// <summary>
    /// Loads Share/self100.ini — self-pickup item list
    /// Section: [自捡]
    /// Format: 自捡N=ItemName/Weight (cumulative probability)
    /// </summary>
    public class SelfPickupLoader : IniFile
    {
        public List<SelfPickupEntry> Items = new List<SelfPickupEntry>();
        public int TotalWeight; // cumulative max weight

        public SelfPickupLoader(string fileName) : base(fileName)
        {
            TotalWeight = 0;
            Load();
            ParseItems();
        }

        private void ParseItems()
        {
            var keys = GetSectionItemName("自捡");
            if (keys == null) return;

            int maxIdx = 0;
            foreach (var key in keys)
            {
                if (key.StartsWith("自捡") && int.TryParse(key.Substring(2), out int idx))
                {
                    if (idx > maxIdx) maxIdx = idx;
                }
            }

            for (int i = 1; i <= maxIdx; i++)
            {
                var val = ReadString("自捡", $"自捡{i}", "");
                if (!string.IsNullOrEmpty(val))
                {
                    var slash = val.LastIndexOf('/');
                    if (slash > 0)
                    {
                        var name = val.Substring(0, slash).Trim();
                        var weight = HUtil32.Str_ToInt(val.Substring(slash + 1).Trim(), 0);
                        Items.Add(new SelfPickupEntry { ItemName = name, Weight = weight });
                        TotalWeight = weight; // last entry holds cumulative
                    }
                }
            }
        }
    }

    /// <summary>
    /// Loads Share/bigitem.ini — lottery / big item pools
    /// Sections: [抽奖1] through [抽奖5]
    /// Format: 抽奖N=ItemName/Weight
    /// </summary>
    public class BigItemLoader : IniFile
    {
        /// <summary>Lottery pool index (1-5) -> entries</summary>
        public Dictionary<int, List<BigItemEntry>> Pools = new Dictionary<int, List<BigItemEntry>>();

        public BigItemLoader(string fileName) : base(fileName)
        {
            Load();
            ParsePools();
        }

        private void ParsePools()
        {
            for (int pool = 1; pool <= 5; pool++)
            {
                var section = $"抽奖{pool}";
                var entries = new List<BigItemEntry>();
                var keys = GetSectionItemName(section);
                if (keys == null) continue;

                foreach (var key in keys)
                {
                    var val = ReadString(section, key, "");
                    if (!string.IsNullOrEmpty(val))
                    {
                        var slash = val.IndexOf('/');
                        if (slash > 0)
                        {
                            entries.Add(new BigItemEntry
                            {
                                ItemName = val.Substring(0, slash).Trim(),
                                Weight = HUtil32.Str_ToInt(val.Substring(slash + 1).Trim(), 0)
                            });
                        }
                    }
                }
                if (entries.Count > 0) Pools[pool] = entries;
            }
        }
    }

    /// <summary>
    /// Loads Share/GoldID.ini native reward pools.
    /// Sections: [配置1] through [配置9], keys: 奖励1 through 奖励100.
    /// </summary>
    public class GoldIDLoader : IniFile
    {
        public Dictionary<int, List<string>> Pools { get; } =
            new Dictionary<int, List<string>>();

        public GoldIDLoader(string fileName) : base(fileName)
        {
            Load();
            ParsePools(9);
        }

        private void ParsePools(int poolCount)
        {
            for (var pool = 1; pool <= poolCount; pool++)
            {
                var section = $"配置{pool}";
                var items = new List<string>();
                for (var reward = 1; reward <= 100; reward++)
                {
                    var itemName = ReadString(section, $"奖励{reward}", "");
                    if (string.IsNullOrEmpty(itemName)) break;
                    items.Add(itemName);
                }
                Pools[pool] = items;
            }
        }
    }

    /// <summary>
    /// Loads Share/Config/NewGoldID.ini native level 46..55 reward pools.
    /// </summary>
    public sealed class GoldActRewardLoader : IniFile
    {
        public Dictionary<int, List<string>> Pools { get; } =
            new Dictionary<int, List<string>>();

        public GoldActRewardLoader(string fileName) : base(fileName)
        {
            Load();
            for (var pool = 1; pool <= 10; pool++)
            {
                var items = new List<string>();
                for (var reward = 1; reward <= 100; reward++)
                {
                    var itemName = ReadString($"配置{pool}", $"奖励{reward}", "");
                    if (!string.IsNullOrEmpty(itemName)) items.Add(itemName);
                }
                Pools[pool] = items;
            }
        }
    }

    // ========================================================================
    // Central loader that orchestrates all Share/*.ini file loading
    // ========================================================================

    /// <summary>
    /// Central config loader for all Share/*.ini files (战神 original config format).
    /// Wired into initialization to replace hardcoded defaults with file-loaded values.
    /// All files are GBK-encoded; individual loaders inherit IniFile which handles this.
    /// Empty or missing files are handled gracefully (return null / empty collections).
    /// </summary>
    public class ZhanshenConfig
    {
        /// <summary>Level exp requirements and rate multipliers from PlayerUpgradeExp.ini</summary>
        public PlayerUpgradeExpLoader PlayerExp;

        /// <summary>Warrior class config from warr.ini</summary>
        public ClassConfigLoader WarrConfig;

        /// <summary>Wizard class config from fashi.ini</summary>
        public ClassConfigLoader FashiConfig;

        /// <summary>Taoist class config from taos.ini</summary>
        public ClassConfigLoader TaosConfig;

        /// <summary>Assassin class config from assassin.ini</summary>
        public ClassConfigLoader AssassinConfig;

        /// <summary>Hero character templates from hero.ini</summary>
        public HeroConfigLoader HeroConfig;

        /// <summary>Actor/misc settings from Mir2Actor.ini</summary>
        public Mir2ActorLoader ActorConfig;

        /// <summary>Server info from serverinfo.ini</summary>
        public ServerInfoLoader ServerInfo;

        /// <summary>Self-pickup items from self100.ini</summary>
        public SelfPickupLoader SelfPickupConfig;

        /// <summary>Lottery/big item pools from bigitem.ini</summary>
        public BigItemLoader BigItemConfig;

        /// <summary>Gold item categories from GoldID.ini</summary>
        public GoldIDLoader GoldIDConfig;

        /// <summary>Level 46..55 rewards from Config/NewGoldID.ini</summary>
        public GoldActRewardLoader GoldActRewardConfig;

        // Guardian class configs
        public ClassConfigLoader GWarrConfig;
        public ClassConfigLoader GFashiConfig;
        public ClassConfigLoader GTaosConfig;

        /// <summary>
        /// Load all Share/*.ini files from the given Share directory path.
        /// </summary>
        /// <param name="sharePath">Full path to the Share/ directory (e.g., sRootPath/Share)</param>
        public void LoadAll(string sharePath)
        {
            PlayerExp = TryLoad<PlayerUpgradeExpLoader>(sharePath, "PlayerUpgradeExp.ini");
            WarrConfig = TryLoad<ClassConfigLoader>(sharePath, "warr.ini");
            FashiConfig = TryLoad<ClassConfigLoader>(sharePath, "fashi.ini");
            TaosConfig = TryLoad<ClassConfigLoader>(sharePath, "taos.ini");
            AssassinConfig = TryLoad<ClassConfigLoader>(sharePath, "assassin.ini");
            HeroConfig = TryLoad<HeroConfigLoader>(sharePath, "hero.ini");
            ActorConfig = TryLoad<Mir2ActorLoader>(sharePath, "Mir2Actor.ini");
            ServerInfo = TryLoad<ServerInfoLoader>(sharePath, "serverinfo.ini");
            SelfPickupConfig = TryLoad<SelfPickupLoader>(sharePath, "self100.ini");
            BigItemConfig = TryLoad<BigItemLoader>(sharePath, "bigitem.ini");
            GoldIDConfig = TryLoad<GoldIDLoader>(sharePath, "GoldID.ini");
            GoldActRewardConfig = TryLoad<GoldActRewardLoader>(sharePath,
                Path.Combine("Config", "NewGoldID.ini"));

            // Guardian class configs
            GWarrConfig = TryLoad<ClassConfigLoader>(sharePath, "gwarr.ini");
            GFashiConfig = TryLoad<ClassConfigLoader>(sharePath, "gfashi.ini");
            GTaosConfig = TryLoad<ClassConfigLoader>(sharePath, "gtaos.ini");
        }

        /// <summary>
        /// Tries to load a config file. Returns null if file doesn't exist or is empty.
        /// </summary>
        private T TryLoad<T>(string sharePath, string fileName) where T : IniFile
        {
            var fullPath = Path.Combine(sharePath, fileName);
            if (!File.Exists(fullPath) || new FileInfo(fullPath).Length == 0)
                return null;

            try
            {
                return (T)Activator.CreateInstance(typeof(T), fullPath);
            }
            catch (Exception ex)
            {
                // File exists but may be empty or malformed - graceful skip
                System.Console.WriteLine($"[ZhanshenConfig] Skipping {fileName}: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Applies the loaded exp config into M2Share. Overrides hardcoded ExpsConfig values
        /// with values from PlayerUpgradeExp.ini when available.
        /// </summary>
        public void ApplyToM2Share()
        {
            PlayerExp?.ApplyToConfig();
        }

        /// <summary>
        /// Gets the class config for a given job type. Returns null if not loaded.
        /// </summary>
        public ClassConfigLoader GetClassConfig(int job)
        {
            // Job constants from M2Share: jWarr=0, jWizard=1, jTaos=2
            switch (job)
            {
                case 0: return WarrConfig;
                case 1: return FashiConfig;
                case 2: return TaosConfig;
                default: return null;
            }
        }

        /// <summary>
        /// Gets the guardian class config for a given job type.
        /// </summary>
        public ClassConfigLoader GetGuardianConfig(int job)
        {
            switch (job)
            {
                case 0: return GWarrConfig;
                case 1: return GFashiConfig;
                case 2: return GTaosConfig;
                default: return null;
            }
        }
    }
}
