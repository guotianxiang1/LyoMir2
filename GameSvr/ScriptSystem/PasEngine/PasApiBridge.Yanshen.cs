using GameSvr.Plugins;
using SystemModule;

namespace GameSvr.PasEngine
{
    /// <summary>
    /// PasApiBridge partial — Yanshen API integration.
    /// Registers ~100 yanshen functions as PAS built-in functions,
    /// allowing Pascal scripts to call them directly without the !!!! tunnel.
    ///
    /// 逆向还原自:
    /// - AllFuc.pas (41+ Pascal 封装函数)
    /// - common.pas (基础 GiveMON/SetItemV/GetItemV 等)
    /// - NpcFuc.pas (NPC 扩展函数)
    /// - yanshen2.0.7.dll 运行时行为
    ///
    /// 2.0.8 支持: 补齐 2.0.8 (yanshen2.0.8.dll / AllFuc 使用例子) 的原版函数名，
    /// 使按原始 眼神 命名编写的脚本可直接派发。新增 Player-first 别名
    /// (ys_toubao/ys_herojp/ys_sendmsg/ys_settimerbyname/ys_setherocskill/
    /// ys_killbbbyname/ys_getother) — 参数索引统一右移一位以吸收首位 Player 实参。
    /// 秒级 CD 族 (ys_setcd/ys_cmptime/ys_gethowtime) 与其毫秒同胞
    /// (ys_setcd_min/ys_cmptime_min) 同址实现于 PasApiBridge.CallStandaloneFunction，
    /// 使用玩家 V 变量 + 时间戳隧道，而非无状态的 api.CD* 方法。
    /// </summary>
    public partial class PasApiBridge
    {
        private static readonly HashSet<string> YanshenApiNames = new(
            @"
            addmaxhp addmaxmp addtempattr addtempattrpro autopickup autorecycle
            bindunbinditem bounceskill cdcmptime cdcmptimems cdgetdiff cdgetremaining
            cdgettimes cdgettimesms changebigbag checkitembind checkmapmonbyname
            customdamage customdamage2 customdamagedelay customdamageeffect
            customdamagesuper customdamageundead customfirewall decexp dropequipbyname
            dropequipbypos dropitem equipdura equipinsurance getbagmakeindexlist
            getbagweight getcastleguildname getcastlelordname getclientitemidbyitemid
            getcreatureattr getequipelement getgroupmembercount getgroupmembername
            getgroupmemberroleid getheroextreme getitemdataandrecycle
            getitemdatabyclientid getitemdatabymakeindex getitemdbdata getitemextreme
            getitemidbyclientid getpetattrbyname getpis getskilldmgreduction
            givedataitem giveexp giveitem5el giveitemwithdesc giveitemys_jp givenewitem
            givepetskill givepetspecialattr givepis healing herocastskill holydamage
            kickplayer killpetbyname lifesteal modifyitemdesc npcgiveitemys paralysis
            petfollowattack playeffect poison poisoneffect pullenemy pullenemy2 pushenemy
            pushenemy2 rename repairbagbystdmode roottarget sendclienteffect senddbmsg
            setequipelement setitemextreme setlooptimer setpetattr setskilldmgreduction
            setskillexp sqldbinsert sqldbselect subtempattr summonpet summonpetroyalty
            superdamage14 updatebodyequip vacuummonstersex
            ys_addhp ys_addmp ys_addshuxing ys_addshuxing_pro ys_bbflowme ys_cdcmptime
            ys_cdcmptimems ys_cdgetdiff ys_cdgetremaining ys_cdgettimes ys_cdgettimesms
            ys_change_ly ys_checkmapmonbyname ys_checkwupinisbind ys_chgbigbag ys_cutting
            ys_decexp ys_dingshen ys_doeffect ys_dropitem ys_dropitembyid ys_dropitembyname
            ys_equipdura ys_equipinsurance ys_geta ys_getcastleguildname
            ys_getcastlelordname ys_getclientitemidbyitemid ys_getdatabyclientitemid
            ys_getequipelement ys_getfzhong ys_getg ys_getheroextreme ys_getitemdbdata
            ys_getitemextreme ys_getitemid ys_getitemjp ys_getmember_playername
            ys_getmember_roleid ys_getmembercount ys_getpis ys_getshuxing ys_getstr
            ys_getsxbyname ys_getys ys_givebb_sx ys_givebbskill ys_givebind
            ys_givedataitem ys_giveduar ys_giveexp ys_giveitem ys_giveitem5el
            ys_giveitemwithdesc ys_giveitemys_jp ys_givenewitem ys_givepis ys_healing
            ys_huishou ys_jitui ys_jitui2 ys_killbbyname ys_magic_huoqiang ys_makeslave
            ys_makeslaveex ys_myjn_delay ys_myjn_effect ys_myjn_plus ys_myjn_plus2
            ys_myjn_super ys_myjn_undead ys_mymabi ys_myskillexp ys_myysjn ys_newxiguai
            ys_npcgiveitemys ys_pick ys_playerout ys_rename ys_repairinbag
            ys_sendclienteffect ys_senddbmsg ys_seta ys_setequipelement ys_setg
            ys_setheroskill ys_setitemextreme ys_setitemjp ys_setloopertimer ys_setpetv
            ys_setstr ys_setys ys_shidu ys_shidu_effect ys_sqldbinsert ys_sqldbselect
            ys_subshuxing ys_tantanskill ys_tuitui ys_tuitui2 ys_updatebodyequip
            ys_wupingetdata ys_wupingetdata2take ys_wupinmakeindex ys_xixue ysattact
            ysbinditem yschangerole yscreatemon ysfindplayerbyname ysgetbodyitem ysgetg
            ysgetheroshuxing ysgetitem ysgetitemid ysgetonlineplayernum ysgetstr yskillmon
            yskillrole ysnewtuitui yssafezone yssay yssetg yssetstr ysyeman
            ys_toubao ys_herojp ys_sendmsg ys_settimerbyname ys_setherocskill
            ys_killbbbyname ys_getother
            ".Split(new[] { ' ', '\r', '\n', '\t' }, StringSplitOptions.RemoveEmptyEntries),
            StringComparer.OrdinalIgnoreCase);

        private static readonly IReadOnlyDictionary<string, string[]> YanshenApiFeatures =
            BuildYanshenApiFeatures();

        private static IReadOnlyDictionary<string, string[]> BuildYanshenApiFeatures()
        {
            var features = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);

            AddYanshenFeature(features, "自定义元素",
                "ys_givepis", "givepis", "ys_getpis", "getpis",
                "ys_givenewitem", "givenewitem", "ys_giveitemys_jp", "giveitemys_jp",
                "ys_giveitemwithdesc", "giveitemwithdesc", "ys_givedataitem", "givedataitem",
                "ys_npcgiveitemys", "npcgiveitemys", "ys_giveitem5el", "giveitem5el", "ys_giveitem",
                "ys_setequipelement", "setequipelement", "ys_setys",
                "ys_getequipelement", "getequipelement", "ys_getys",
                "ys_getitemextreme", "getitemextreme", "ys_getitemjp",
                "ys_setitemextreme", "setitemextreme", "ys_setitemjp",
                "ys_equipdura", "equipdura", "ys_giveduar",
                "ys_equipinsurance", "equipinsurance",
                "ys_getitemid", "getitemidbyclientid",
                "ys_getclientitemidbyitemid", "getclientitemidbyitemid",
                "ys_updatebodyequip", "updatebodyequip");

            AddYanshenFeature(features, "刀刀切割",
                "ys_myjn_plus", "customdamage", "ys_myjn_plus2", "customdamage2",
                "ys_myjn_undead", "customdamageundead",
                "ys_myjn_super", "customdamagesuper", "ys_myjn_delay", "customdamagedelay",
                "ys_cutting", "holydamage", "ys_myysjn", "superdamage14",
                "ys_healing", "healing", "ys_subshuxing", "subtempattr",
                "ys_addshuxing", "addtempattr", "ys_addshuxing_pro", "addtempattrpro",
                "ys_addhp", "addmaxhp", "ys_addmp", "addmaxmp",
                "ys_giveexp", "giveexp");

            AddYanshenFeature(features, "野蛮麻痹",
                "ys_jitui", "pushenemy", "ys_jitui2", "pushenemy2",
                "ys_tuitui2", "pullenemy2", "ys_dingshen", "roottarget");

            AddYanshenFeature(features, "特殊宝宝",
                "ys_makeslave", "summonpetroyalty", "ys_killbbyname", "killpetbyname",
                "ys_getsxbyname", "getpetattrbyname");

            AddYanshenFeature(features, "自定义伤害",
                "ys_doeffect", "playeffect", "ys_tantanskill", "bounceskill");

            AddYanshenFeature(features, "眼神特殊函数",
                "ys_sqldbinsert", "sqldbinsert", "ys_sqldbselect", "sqldbselect",
                "ys_senddbmsg", "senddbmsg", "ys_wupinmakeindex", "getbagmakeindexlist",
                "ys_wupingetdata", "getitemdatabymakeindex",
                "ys_wupingetdata2take", "getitemdataandrecycle",
                "ys_getdatabyclientitemid", "getitemdatabyclientid",
                "ys_sendclienteffect", "sendclienteffect",
                "ys_bbflowme", "petfollowattack", "ys_getfzhong", "getbagweight",
                "ys_getmembercount", "getgroupmembercount",
                "ys_getmember_roleid", "getgroupmemberroleid",
                "ys_getmember_playername", "getgroupmembername",
                "ys_decexp", "decexp", "ys_rename", "rename", "ys_getmember");

            AddYanshenFeature(features, "施毒术", "ys_shidu", "poison");
            AddYanshenFeature(features, "麻痹概率", "ys_mymabi", "paralysis");
            AddYanshenFeature(features, "攻击吸血", "ys_xixue", "lifesteal");
            AddYanshenFeature(features, "全屏吸怪", "ys_newxiguai", "vacuummonstersex");
            AddYanshenFeature(features, "指定英雄放技能", "ys_setheroskill", "herocastskill");
            AddYanshenFeature(features, "高级回收",
                "ys_huishou", "autorecycle", "ys_dropitem", "dropitem",
                "ys_repairinbag", "repairbagbystdmode");
            AddYanshenFeature(features, "屏蔽自动绑定",
                "ys_givebind", "bindunbinditem", "ys_dropitembyid", "dropequipbypos",
                "ys_dropitembyname", "dropequipbyname", "ys_checkwupinisbind", "checkitembind");
            AddYanshenFeature(features, "装备来源", "ys_change_ly", "modifyitemdesc");
            AddYanshenFeature(features, "行会显示",
                "ys_getshuxing", "getcreatureattr", "ys_checkmapmonbyname", "checkmapmonbyname");
            AddYanshenFeature(features, "火墙设置时间上限",
                "ys_myskillexp", "setskillexp");
            AddYanshenFeature(features, "踢玩家下线", "ys_playerout", "kickplayer");
            AddYanshenFeature(features, "全局循环函数", "ys_setloopertimer", "setlooptimer");
            AddYanshenFeature(features, "大背包", "ys_chgbigbag", "changebigbag");
            AddYanshenFeature(features, "英雄读取极品", "ys_getheroextreme", "getheroextreme");
            AddYanshenFeature(features, "获取沙城归属",
                "ys_getcastleguildname", "getcastleguildname",
                "ys_getcastlelordname", "getcastlelordname");
            AddYanshenFeature(features, "毫秒级cd记录",
                "ys_cdgettimes", "cdgettimes", "ys_cdgettimesms", "cdgettimesms",
                "ys_cdcmptime", "cdcmptime", "ys_cdcmptimems", "cdcmptimems",
                "ys_cdgetremaining", "cdgetremaining", "ys_cdgetdiff", "cdgetdiff");

            AddYanshenFeatures(features, new[] { "眼神特殊函数", "全屏拾取" },
                "ys_pick", "autopickup");
            AddYanshenFeature(features, "怪物伤害触发技能特效",
                "ys_givebbskill", "givepetskill", "ys_givebb_sx", "givepetspecialattr");
            AddYanshenFeatures(features,
                new[] { "眼神特殊函数", "怪物伤害触发技能特效" },
                "ys_setpetv", "setpetattr", "ys_makeslaveex", "summonpet");
            AddYanshenFeatures(features,
                new[] { "眼神特殊函数", "自定义伤害_plus", "super攻击触发" },
                "ys_myjn_effect", "customdamageeffect");
            AddYanshenFeatures(features, new[] { "眼神特殊函数", "super攻击触发" },
                "ys_shidu_effect", "poisoneffect", "ys_tuitui", "pullenemy");
            AddYanshenFeatures(features, new[] { "眼神特殊函数", "指定技能id免伤" },
                "ys_seta", "setskilldmgreduction", "ys_geta", "getskilldmgreduction");
            AddYanshenFeature(features, "火墙修改", "ys_magic_huoqiang", "customfirewall");

            // 2.08 原版函数名别名（Player-first 签名；switch 中参数索引右移一位）。
            // 每个别名归属其 C# 孪生函数所在的同一 feature，以复用相同开关门控。
            AddYanshenFeature(features, "自定义元素", "ys_toubao", "ys_getother");
            AddYanshenFeature(features, "英雄读取极品", "ys_herojp");
            AddYanshenFeature(features, "眼神特殊函数", "ys_sendmsg");
            AddYanshenFeature(features, "全局循环函数", "ys_settimerbyname");
            AddYanshenFeature(features, "指定英雄放技能", "ys_setherocskill");
            AddYanshenFeature(features, "特殊宝宝", "ys_killbbbyname");

            // Public Pascal wrappers whose implementation remains in AllFuc/NpcFuc.
            AddYanshenFeature(features, "自定义元素", "ys_giveitem_ly");
            AddYanshenFeature(features, "npc自定义函数", "npc_creatmons");

            // 2.08 does not give these utility APIs independent switches. Keep them
            // fail-closed behind the documented general Pascal API switch.
            AddYanshenFeature(features, "眼神特殊函数",
                "ys_getitemdbdata", "getitemdbdata", "ysgetitem", "ysgetitemid",
                "ysbinditem", "ysgetbodyitem", "ysgetheroshuxing", "yssafezone",
                "ysgetonlineplayernum", "yskillrole", "yschangerole", "yskillmon",
                "ysfindplayerbyname", "yssay", "ysyeman", "yscreatemon", "ysattact",
                "yssetg", "ys_setg", "ysgetg", "ys_getg",
                "yssetstr", "ys_setstr", "ysgetstr", "ys_getstr", "ysnewtuitui");

            var missing = YanshenApiNames.Where(name => !features.ContainsKey(name)).ToArray();
            var wrapperOnly = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "ys_getmember", "ys_giveitem_ly", "npc_creatmons"
            };
            var extra = features.Keys.Where(name =>
                !YanshenApiNames.Contains(name) && !wrapperOnly.Contains(name)).ToArray();
            if (missing.Length != 0 || extra.Length != 0)
            {
                throw new InvalidOperationException(
                    $"Yanshen API feature catalog mismatch; missing={string.Join(',', missing)}; " +
                    $"extra={string.Join(',', extra)}");
            }

            return features;
        }

        private static void AddYanshenFeature(Dictionary<string, string[]> features,
            string featureName, params string[] apiNames)
        {
            AddYanshenFeatures(features, new[] { featureName }, apiNames);
        }

        private static void AddYanshenFeatures(Dictionary<string, string[]> features,
            string[] featureNames, params string[] apiNames)
        {
            foreach (var apiName in apiNames)
                features.Add(apiName, featureNames);
        }

        internal IDisposable BeginYanshenScriptApiCall(string functionName)
        {
            if (!YanshenApiFeatures.TryGetValue(functionName ?? string.Empty,
                    out var requiredFeatures))
                return null;

            YanshenApi.EnsureDirectCallReady(M2Share.PluginManager, functionName);
            var api = GetYanshenApi(CanRunYanshenWithoutCurrentPlayer(functionName));
            if (api == null) return null;

            var scope = YanshenApi.BeginStrictDirectCall(functionName);
            try
            {
                foreach (var featureName in requiredFeatures)
                    api.EnsureFeatureEnabled(featureName);
                return scope;
            }
            catch
            {
                scope.Dispose();
                throw;
            }
        }

        internal PluginInfo BeginYanshenInitialization(string procedureName,
            string sourceFile,
            out bool wasInitialized)
        {
            wasInitialized = false;
            if (!IsYanshenInitializer(procedureName, sourceFile))
                return null;

            var plugin = M2Share.PluginManager?.GetPlugin("YanshenCompat");
            if (plugin?.State != PluginState.Running) return null;
            Monitor.Enter(plugin.InitializationSync);
            wasInitialized = plugin.IsInitialized;
            YanshenApi.EnterInitialization(plugin);
            return plugin;
        }

        private static bool IsYanshenInitializer(string procedureName, string sourceFile)
        {
            if (!string.Equals(procedureName, "initys", StringComparison.OrdinalIgnoreCase) ||
                string.IsNullOrWhiteSpace(sourceFile) ||
                !string.Equals(Path.GetFileName(sourceFile), "RunQuest.pas",
                    StringComparison.OrdinalIgnoreCase))
                return false;

            var parent = Path.GetDirectoryName(sourceFile);
            return !string.IsNullOrWhiteSpace(parent) &&
                   string.Equals(Path.GetFileName(parent), "PsMapQuest",
                       StringComparison.OrdinalIgnoreCase);
        }

        internal static void EndYanshenInitialization(PluginInfo plugin,
            bool wasInitialized, bool succeeded)
        {
            if (plugin == null) return;
            try
            {
                if (succeeded) plugin.IsInitialized = true;
                else plugin.IsInitialized = wasInitialized;
            }
            finally
            {
                YanshenApi.ExitInitialization(plugin);
                Monitor.Exit(plugin.InitializationSync);
            }
        }

        private YanshenApi GetYanshenApi(bool allowWithoutPlayer = false)
        {
            var manager = GetRunningYanshenPluginManager();
            return manager != null && (CurrentPlayer != null || allowWithoutPlayer)
                ? new YanshenApi(CurrentPlayer, CurrentNpc, manager)
                : null;
        }

        private static bool CanRunYanshenWithoutCurrentPlayer(string name)
        {
            return (name ?? string.Empty).ToLowerInvariant() switch
            {
                "ysgetonlineplayernum" => true,
                "ysfindplayerbyname" => true,
                "yscreatemon" => true,
                "yskillmon" => true,
                "yssetg" => true,
                "ys_setg" => true,
                "ysgetg" => true,
                "ys_getg" => true,
                "yssetstr" => true,
                "ys_setstr" => true,
                "ysgetstr" => true,
                "ys_getstr" => true,
                "yssafezone" => true,
                "yskillrole" => true,
                "yschangerole" => true,
                "yssay" => true,
                "ysyeman" => true,
                "ysattact" => true,
                "ysgetbodyitem" => true,
                "ysgetheroshuxing" => true,
                "ysnewtuitui" => true,
                "ysgetitemid" => true,
                "ysgetitem" => true,
                "ysbinditem" => true,
                "ys_getitemdbdata" => true,
                "getitemdbdata" => true,
                _ => false
            };
        }

        private bool ApplyQuestInfo(List<PasValue> args)
        {
            if (CurrentPlayer == null || args.Count != 1)
                return false;

            var api = GetYanshenApi();
            CurrentPlayer.ApplyQuestInfo(args[0].AsString(),
                api != null && api.IsSetTitleEnabled());
            return true;
        }

        private bool TryCallYanshenSignInTunnel(List<PasValue> args, out PasValue result)
        {
            result = PasValue.Nil;
            if (args.Count != 2) return false;

            var command = args[0].AsString();
            var selector = args[1].AsString();
            if (selector.Equals("libmysql", StringComparison.OrdinalIgnoreCase))
            {
                const string apiName = "GetSignInActPrizer";
                YanshenApi.EnsureDirectCallReady(M2Share.PluginManager, apiName);
                var sqlApi = GetYanshenApi();
                if (sqlApi == null) return false;
                using var directCall = YanshenApi.BeginStrictDirectCall(apiName);
                sqlApi.EnsureFeatureEnabled("眼神特殊函数");
                result = PasValue.FromString(sqlApi.SqlDbSelect(command));
                return true;
            }
            if (!selector.Equals("lucker2", StringComparison.OrdinalIgnoreCase)) return false;

            var parts = command.Split(new[] { '^' }, 3, StringSplitOptions.None);
            if (parts.Length != 3 || parts[0] != "!!!!" ||
                !int.TryParse(parts[1], out var operation))
                return false;

            const string tunnelApiName = "GetSignInActPrizer";
            YanshenApi.EnsureDirectCallReady(M2Share.PluginManager, tunnelApiName);
            var api = GetYanshenApi();
            if (api == null) return false;
            using var tunnelCall = YanshenApi.BeginStrictDirectCall(tunnelApiName);
            api.EnsureFeatureEnabled("眼神特殊函数");

            switch (operation)
            {
                case 1:
                    result = PasValue.FromString(api.GetBagMakeIndexList(parts[2] == "1"));
                    return true;
                case 2 when int.TryParse(parts[2], out var makeIndex):
                    result = PasValue.FromString(api.GetItemDataByMakeIndex(makeIndex));
                    return true;
                case 3 when int.TryParse(parts[2], out var recycleIndex):
                    result = PasValue.FromString(api.GetItemDataAndRecycle(recycleIndex));
                    return true;
                case 4 when int.TryParse(parts[2], out var clientItemId):
                    result = PasValue.FromString(api.GetItemDataByClientId(clientItemId));
                    return true;
                case 5 when int.TryParse(parts[2], out var memberIndex):
                    result = PasValue.FromString(api.GetGroupMemberName(memberIndex));
                    return true;
                default:
                    return false;
            }
        }

        internal static bool IsYanshenSignInTunnelCall(List<PasValue> args)
        {
            if (args.Count != 2) return false;

            var command = args[0].AsString();
            var selector = args[1].AsString();
            if (selector.Equals("libmysql", StringComparison.OrdinalIgnoreCase))
                return true;

            return selector.Equals("lucker2", StringComparison.OrdinalIgnoreCase)
                   && command.StartsWith("!!!!^", StringComparison.Ordinal);
        }

        /// <summary>
        /// Try to invoke a yanshen function by name (functions that return values).
        /// Called from TryCallThisPlayerFunc as a fallback when no standard M2 function matches.
        /// </summary>
        public bool TryCallYanshenFunc(string name, List<PasValue> args, out PasValue result)
        {
            result = PasValue.Nil;
            if (!YanshenApiNames.Contains(name ?? string.Empty)) return false;
            var api = GetYanshenApi(CanRunYanshenWithoutCurrentPlayer(name));
            if (api == null) return false;

            using (BeginYanshenScriptApiCall(name))
            {
                switch (name.ToLowerInvariant())
                {
                // ═══ 6.1 元素系统 (17元素) ═══
                case "ys_givepis": case "givepis":
                    if (args.Count >= 3) { api.GivePis(args[0].AsInt(), args[1].AsInt(), args[2].AsInt()); return true; }
                    return false;
                case "ys_getpis": case "getpis":
                    if (args.Count >= 2) { result = PasValue.FromInt(api.GetPis(args[0].AsInt(), args[1].AsInt())); return true; }
                    return false;
                case "ys_givenewitem": case "givenewitem":
                    if (args.Count >= 2) { var ysArr = new int[17]; for (int i = 1; i < args.Count && i <= 17; i++) ysArr[i - 1] = args[i].AsInt(); api.GiveNewItem(args[0].AsString(), 0, ysArr); return true; }
                    return false;
                case "ys_giveitemys_jp": case "giveitemys_jp":
                    if (args.Count >= 2) { var ysJp = new int[17]; var jpJp = new int[6]; for (int i = 1; i < args.Count && i <= 17; i++) ysJp[i - 1] = args[i].AsInt(); for (int i = 18; i < args.Count && i <= 23; i++) jpJp[i - 18] = args[i].AsInt(); api.GiveItemYS_JP(args[0].AsString(), 0, ysJp, jpJp); return true; }
                    return false;
                case "ys_giveitemwithdesc": case "giveitemwithdesc":
                    if (args.Count >= 3) { api.GiveItemWithDesc(args[0].AsString(), args[1].AsString(), args[2].AsString(), args.Count > 3 ? args[3].AsString() : "", 0); return true; }
                    return false;
                case "ys_givedataitem": case "givedataitem":
                    if (args.Count >= 2) { api.GiveDataItem(args[0].AsString(), args[1].AsString()); return true; }
                    return false;
                case "ys_npcgiveitemys": case "npcgiveitemys":
                    if (args.Count >= 1) { var ysNpc = new int[17]; for (int i = 1; i < args.Count && i <= 17; i++) ysNpc[i - 1] = args[i].AsInt(); result = PasValue.FromInt(api.NpcGiveItemYs(args[0].AsInt(), ysNpc)); return true; }
                    return false;
                case "ys_giveitem5el": case "giveitem5el": case "ys_giveitem":
                    if (args.Count >= 6) { api.GiveItem5El(args[0].AsString(), args[1].AsInt(), args[2].AsInt(), args[3].AsInt(), args[4].AsInt(), args[5].AsInt()); return true; }
                    return false;
                case "ys_setequipelement": case "setequipelement": case "ys_setys":
                    if (args.Count >= 3) { result = PasValue.FromInt(api.SetEquipElement(args[0].AsInt(), args[1].AsInt(), args[2].AsInt())); return true; }
                    return false;
                case "ys_getequipelement": case "getequipelement": case "ys_getys":
                    if (args.Count >= 3) { result = PasValue.FromInt(api.GetEquipElement(args[0].AsInt(), args[1].AsInt(), args[2].AsInt())); return true; }
                    return false;
                case "ys_getitemextreme": case "getitemextreme": case "ys_getitemjp":
                    if (args.Count >= 3) { result = PasValue.FromInt(api.GetItemExtreme(args[0].AsInt(), args[1].AsInt(), args[2].AsInt())); return true; }
                    return false;
                case "ys_setitemextreme": case "setitemextreme": case "ys_setitemjp":
                    if (args.Count >= 4) { result = PasValue.FromInt(api.SetItemExtreme(args[0].AsInt(), args[1].AsInt(), args[2].AsInt(), args[3].AsInt())); return true; }
                    return false;
                case "ys_equipdura": case "equipdura": case "ys_giveduar":
                    if (args.Count >= 3) { result = PasValue.FromInt(api.EquipDura(args[0].AsInt(), args[1].AsInt(), args[2].AsInt())); return true; }
                    return false;
                case "ys_equipinsurance": case "equipinsurance":
                    if (args.Count >= 2) { result = PasValue.FromInt(api.EquipInsurance(args[0].AsInt(), args[1].AsInt())); return true; }
                    return false;

                // ═══ 6.2 技能伤害系统 ═══
                case "ys_myjn_plus": case "customdamage":
                    if (args.Count >= 8) { result = PasValue.FromInt(api.CustomDamage(args[0].AsInt(), args[1].AsInt(), args[2].AsInt(), args[3].AsInt(), args[4].AsInt(), args[5].AsInt(), args[6].AsInt(), args[7].AsInt())); return true; }
                    return false;
                case "ys_myjn_plus2": case "customdamage2":
                    if (args.Count >= 9) { result = PasValue.FromInt(api.CustomDamage2(args[0].AsInt(), args[1].AsInt(), args[2].AsInt(), args[3].AsInt(), args[4].AsInt(), args[5].AsInt(), args[6].AsInt(), args[7].AsInt(), args[8].AsInt())); return true; }
                    return false;
                case "ys_myjn_effect": case "customdamageeffect":
                    if (args.Count >= 10) { result = PasValue.FromInt(api.CustomDamageEffect(args[0].AsInt(), args[1].AsInt(), args[2].AsInt(), args[3].AsInt(), args[4].AsInt(), args[5].AsInt(), args[6].AsInt(), args[7].AsInt(), args[8].AsInt(), args[9].AsInt())); return true; }
                    return false;
                case "ys_myjn_undead": case "customdamageundead":
                    if (args.Count >= 11) { result = PasValue.FromInt(api.CustomDamageUndead(args[0].AsInt(), args[1].AsInt(), args[2].AsInt(), args[3].AsInt(), args[4].AsInt(), args[5].AsInt(), args[6].AsInt(), args[7].AsInt(), args[8].AsInt(), args[9].AsInt(), args[10].AsInt())); return true; }
                    return false;
                case "ys_myjn_super": case "customdamagesuper":
                    if (args.Count >= 13) { result = PasValue.FromInt(api.CustomDamageSuper(args[0].AsInt(), args[1].AsInt(), args[2].AsInt(), args[3].AsInt(), args[4].AsInt(), args[5].AsInt(), args[6].AsInt(), args[7].AsInt(), args[8].AsInt(), args[9].AsInt(), args[10].AsInt(), args[11].AsInt(), args[12].AsInt())); return true; }
                    return false;
                case "ys_myjn_delay": case "customdamagedelay":
                    if (args.Count >= 15) { result = PasValue.FromInt(api.CustomDamageDelay(args[0].AsInt(), args[1].AsInt(), args[2].AsInt(), args[3].AsInt(), args[4].AsInt(), args[5].AsInt(), args[6].AsInt(), args[7].AsInt(), args[8].AsInt(), args[9].AsInt(), args[10].AsInt(), args[11].AsInt(), args[12].AsInt(), args[13].AsInt(), args[14].AsInt())); return true; }
                    return false;
                case "ys_cutting": case "holydamage":
                    if (args.Count >= 10) { result = PasValue.FromInt(api.HolyDamage(args[0].AsInt(), args[1].AsInt(), args[2].AsInt(), args[3].AsInt(), args[4].AsInt(), args[5].AsInt(), args[6].AsInt(), args[7].AsInt(), args[8].AsInt(), args[9].AsInt())); return true; }
                    return false;
                case "ys_myysjn": case "superdamage14":
                    if (args.Count >= 12) { result = PasValue.FromInt(api.SuperDamage14(args[0].AsInt(), args[1].AsInt(), args[2].AsInt(), args[3].AsInt(), args[4].AsInt(), args[5].AsInt(), args[6].AsInt(), args[7].AsInt(), args[8].AsInt(), args[9].AsInt(), args[10].AsInt(), args[11].AsString())); return true; }
                    return false;

                // ═══ 6.3 控制技能 ═══
                case "ys_mymabi": case "paralysis":
                    if (args.Count >= 7) { result = PasValue.FromInt(api.Paralysis(args[0].AsInt(), args[1].AsInt(), args[2].AsInt(), args[3].AsInt(), args[4].AsInt(), args[5].AsInt(), args[6].AsInt() != 0)); return true; }
                    return false;
                case "ys_shidu": case "poison":
                    if (args.Count >= 9) { result = PasValue.FromInt(api.Poison(args[0].AsInt(), args[1].AsInt(), args[2].AsInt(), args[3].AsInt(), args[4].AsInt(), args[5].AsInt(), args[6].AsInt(), args[7].AsInt(), args[8].AsInt())); return true; }
                    return false;
                case "ys_shidu_effect": case "poisoneffect":
                    if (args.Count >= 10) { result = PasValue.FromInt(api.PoisonEffect(args[0].AsInt(), args[1].AsInt(), args[2].AsInt(), args[3].AsInt(), args[4].AsInt(), args[5].AsInt(), args[6].AsInt(), args[7].AsInt(), args[8].AsInt(), args[9].AsInt())); return true; }
                    return false;
                case "ys_jitui": case "pushenemy":
                    if (args.Count >= 8) { result = PasValue.FromInt(api.PushEnemy(args[0].AsInt(), args[1].AsInt(), args[2].AsInt(), args[3].AsInt(), args[4].AsInt(), args[5].AsInt(), args[6].AsInt(), args[7].AsInt())); return true; }
                    return false;
                case "ys_jitui2": case "pushenemy2":
                    if (args.Count >= 9) { result = PasValue.FromInt(api.PushEnemy2(args[0].AsInt(), args[1].AsInt(), args[2].AsInt(), args[3].AsInt(), args[4].AsInt(), args[5].AsInt(), args[6].AsInt(), args[7].AsInt(), args[8].AsInt())); return true; }
                    return false;
                case "ys_tuitui": case "pullenemy":
                    if (args.Count >= 8) { result = PasValue.FromInt(api.PullEnemy(args[0].AsInt(), args[1].AsInt(), args[2].AsInt(), args[3].AsInt(), args[4].AsInt(), args[5].AsInt(), args[6].AsInt(), args[7].AsInt())); return true; }
                    return false;
                case "ys_tuitui2": case "pullenemy2":
                    if (args.Count >= 9) { result = PasValue.FromInt(api.PullEnemy2(args[0].AsInt(), args[1].AsInt(), args[2].AsInt(), args[3].AsInt(), args[4].AsInt(), args[5].AsInt(), args[6].AsInt(), args[7].AsInt(), args[8].AsInt())); return true; }
                    return false;
                case "ys_dingshen": case "roottarget":
                    if (args.Count >= 1) { result = PasValue.FromInt(api.RootTarget(args[0].AsInt())); return true; }
                    return false;
                case "ys_xixue": case "lifesteal":
                    if (args.Count >= 2) { result = PasValue.FromInt(api.LifeSteal(args[0].AsInt(), args[1].AsInt())); return true; }
                    return false;
                case "ys_newxiguai": case "vacuummonstersex":
                    if (args.Count >= 3) { result = PasValue.FromInt(api.VacuumMonstersEx(args[0].AsInt(), args[1].AsInt(), args[2].AsInt())); return true; }
                    return false;

                // ═══ 6.4 增益/减益/治疗 ═══
                case "ys_healing": case "healing":
                    if (args.Count >= 8) { result = PasValue.FromInt(api.Healing(args[0].AsInt(), args[1].AsInt(), args[2].AsInt(), args[3].AsInt(), args[4].AsInt(), args[5].AsInt(), args[6].AsInt(), args[7].AsInt())); return true; }
                    return false;
                case "ys_subshuxing": case "subtempattr":
                    if (args.Count >= 8) { result = PasValue.FromInt(api.SubTempAttr(args[0].AsInt(), args[1].AsInt(), args[2].AsInt(), args[3].AsInt(), args[4].AsInt(), args[5].AsInt(), args[6].AsInt(), args[7].AsInt())); return true; }
                    return false;
                case "ys_addshuxing": case "addtempattr":
                    if (args.Count >= 9) { result = PasValue.FromInt(api.AddTempAttr(args[0].AsInt(), args[1].AsInt(), args[2].AsInt(), args[3].AsInt(), args[4].AsInt(), args[5].AsInt(), args[6].AsInt(), args[7].AsInt(), args[8].AsInt())); return true; }
                    return false;
                case "ys_addshuxing_pro": case "addtempattrpro":
                    if (args.Count >= 10) { result = PasValue.FromInt(api.AddTempAttrPro(args[0].AsInt(), args[1].AsInt(), args[2].AsInt(), args[3].AsInt(), args[4].AsInt(), args[5].AsInt(), args[6].AsInt(), args[7].AsInt(), args[8].AsInt(), args[9].AsInt())); return true; }
                    return false;
                case "ys_addhp": case "addmaxhp":
                    if (args.Count >= 1) { result = PasValue.FromInt(api.AddMaxHp(args[0].AsInt())); return true; }
                    return false;
                case "ys_addmp": case "addmaxmp":
                    if (args.Count >= 1) { result = PasValue.FromInt(api.AddMaxMp(args[0].AsInt())); return true; }
                    return false;
                case "ys_giveexp": case "giveexp":
                    if (args.Count >= 1) { result = PasValue.FromInt(api.GiveExp(args[0].AsInt())); return true; }
                    return false;
                case "ys_decexp": case "decexp":
                    if (args.Count >= 3) { result = PasValue.FromInt(api.DecExp(args[0].AsInt(), args[1].AsInt(), args[2].AsInt())); return true; }
                    return false;
                case "ys_seta": case "setskilldmgreduction":
                    if (args.Count >= 3) { result = PasValue.FromInt(api.SetSkillDmgReduction(args[0].AsString(), args[1].AsInt(), args[2].AsInt())); return true; }
                    return false;
                case "ys_geta": case "getskilldmgreduction":
                    if (args.Count >= 2) { result = PasValue.FromInt(api.GetSkillDmgReduction(args[0].AsString(), args[1].AsInt())); return true; }
                    return false;

                // ═══ 6.5 宝宝/宠物系统 ═══
                case "ys_makeslaveex": case "summonpet":
                    if (args.Count >= 13) { result = PasValue.FromInt(api.SummonPet(args[0].AsString(), args[1].AsInt(), args[2].AsInt(), args[3].AsInt(), args[4].AsInt(), args[5].AsInt(), args[6].AsInt(), args[7].AsInt(), args[8].AsInt(), args[9].AsInt(), args[10].AsInt(), args[11].AsInt(), args[12].AsInt())); return true; }
                    return false;
                case "ys_makeslave": case "summonpetroyalty":
                    if (args.Count >= 14) { result = PasValue.FromInt(api.SummonPetRoyalty(args[0].AsString(), args[1].AsInt(), args[2].AsInt(), args[3].AsInt(), args[4].AsInt(), args[5].AsInt(), args[6].AsInt(), args[7].AsInt(), args[8].AsInt(), args[9].AsInt(), args[10].AsInt(), args[11].AsInt(), args[12].AsInt(), args[13].AsInt())); return true; }
                    return false;
                case "ys_setpetv": case "setpetattr":
                    if (args.Count >= 12) { result = PasValue.FromInt(api.SetPetAttr(args[0].AsString(), args[1].AsInt(), args[2].AsInt(), args[3].AsInt(), args[4].AsInt(), args[5].AsInt(), args[6].AsInt(), args[7].AsInt(), args[8].AsInt(), args[9].AsInt(), args[10].AsInt(), args[11].AsInt())); return true; }
                    return false;
                case "ys_givebbskill": case "givepetskill":
                    if (args.Count >= 5) { result = PasValue.FromInt(api.GivePetSkill(args[0].AsInt(), args[1].AsInt(), args[2].AsInt(), args[3].AsInt(), args[4].AsString())); return true; }
                    return false;
                case "ys_givebb_sx": case "givepetspecialattr":
                    if (args.Count >= 4) { result = PasValue.FromInt(api.GivePetSpecialAttr(args[0].AsInt(), args[1].AsInt(), args[2].AsString(), args[3].AsString())); return true; }
                    return false;
                case "ys_bbflowme": case "petfollowattack":
                    if (args.Count >= 1) { result = PasValue.FromInt(api.PetFollowAttack(args[0].AsInt())); return true; }
                    return false;
                case "ys_setheroskill": case "herocastskill":
                    if (args.Count >= 2) { result = PasValue.FromInt(api.HeroCastSkill(args[0].AsInt(), args[1].AsInt())); return true; }
                    return false;
                case "ys_killbbyname": case "killpetbyname":
                    if (args.Count >= 1) { result = PasValue.FromInt(api.KillPetByName(args[0].AsString())); return true; }
                    return false;
                case "ys_getsxbyname": case "getpetattrbyname":
                    if (args.Count >= 2) { result = PasValue.FromInt(api.GetPetAttrByName(args[0].AsString(), args[1].AsInt())); return true; }
                    return false;

                // ═══ 6.6 物品/背包操作 ═══
                case "ys_huishou": case "autorecycle":
                    result = PasValue.FromInt(api.AutoRecycle()); return true;
                case "ys_pick": case "autopickup":
                    if (args.Count >= 4) { result = PasValue.FromInt(api.AutoPickup(args[0].AsInt(), args[1].AsInt(), args[2].AsInt(), args[3].AsInt())); return true; }
                    return false;
                case "ys_getfzhong": case "getbagweight":
                    if (args.Count >= 1) { result = PasValue.FromInt(api.GetBagWeight(args[0].AsInt())); return true; }
                    return false;
                case "ys_givebind": case "bindunbinditem":
                    if (args.Count >= 2) { result = PasValue.FromInt(api.BindUnbindItem(args[0].AsInt(), args[1].AsInt())); return true; }
                    return false;
                case "ys_dropitem": case "dropitem":
                    if (args.Count >= 3) { result = PasValue.FromInt(api.DropItem(args[0].AsInt(), args[1].AsInt(), args[2].AsString())); return true; }
                    return false;
                case "ys_dropitembyid": case "dropequipbypos":
                    if (args.Count >= 1) { result = PasValue.FromInt(api.DropEquipByPos(args[0].AsInt())); return true; }
                    return false;
                case "ys_dropitembyname": case "dropequipbyname":
                    if (args.Count >= 1) { result = PasValue.FromInt(api.DropEquipByName(args[0].AsString())); return true; }
                    return false;
                case "ys_repairinbag": case "repairbagbystdmode":
                    if (args.Count >= 2) { result = PasValue.FromInt(api.RepairBagByStdMode(args[0].AsInt(), args[1].AsInt())); return true; }
                    return false;
                case "ys_getitemid": case "getitemidbyclientid":
                    if (args.Count >= 1) { result = PasValue.FromInt(api.GetItemIdByClientId(args[0].AsInt())); return true; }
                    return false;
                case "ys_getclientitemidbyitemid": case "getclientitemidbyitemid":
                    if (args.Count >= 1) { result = PasValue.FromInt(api.GetClientItemIdByItemId(args[0].AsInt())); return true; }
                    return false;
                case "ys_change_ly": case "modifyitemdesc":
                    if (args.Count >= 4) { result = PasValue.FromInt(api.ModifyItemDesc(args[0].AsInt(), args[1].AsString(), args[2].AsString(), args[3].AsString())); return true; }
                    return false;
                case "ys_chgbigbag": case "changebigbag":
                    if (args.Count >= 2) { result = PasValue.FromInt(api.ChangeBigBag(args[0].AsString(), args[1].AsString())); return true; }
                    return false;
                case "ys_checkwupinisbind": case "checkitembind":
                    if (args.Count >= 1) { result = PasValue.FromInt(api.CheckItemBind(args[0].AsString()) ? 1 : 0); return true; }
                    return false;
                case "ys_updatebodyequip": case "updatebodyequip":
                    if (args.Count >= 1) { result = PasValue.FromInt(api.UpdateBodyEquip(args[0].AsInt())); return true; }
                    return false;

                // ═══ 6.7 物品数据操作 ═══
                case "ys_wupinmakeindex": case "getbagmakeindexlist":
                    result = PasValue.FromString(api.GetBagMakeIndexList(args.Count >= 1 && args[0].AsInt() != 0)); return true;
                case "ys_wupingetdata": case "getitemdatabymakeindex":
                    if (args.Count >= 1) { result = PasValue.FromString(api.GetItemDataByMakeIndex(args[0].AsInt())); return true; }
                    return false;
                case "ys_wupingetdata2take": case "getitemdataandrecycle":
                    if (args.Count >= 1) { result = PasValue.FromString(api.GetItemDataAndRecycle(args[0].AsInt())); return true; }
                    return false;
                case "ys_getdatabyclientitemid": case "getitemdatabyclientid":
                    if (args.Count >= 1) { result = PasValue.FromString(api.GetItemDataByClientId(args[0].AsInt())); return true; }
                    return false;
                case "ys_getitemdbdata": case "getitemdbdata":
                    if (args.Count >= 3 && args[0].ObjVal is TPlayObject itemOwner)
                    {
                        result = PasValue.FromInt(api.GetItemDbData(itemOwner, args[1].AsInt(), args[2].AsInt()));
                        return true;
                    }
                    if (args.Count >= 2) { result = PasValue.FromInt(api.GetItemDbData(args[0].AsInt(), args[1].AsInt())); return true; }
                    return false;
                case "ysgetitem":
                    if (args.Count >= 1) { result = PasValue.FromObject(api.GetItemObject(args[0].AsInt())); return true; }
                    return false;
                case "ysgetitemid":
                    if (args.Count >= 1)
                    {
                        if (args[0].ObjVal is TUserItem itemObject)
                            result = PasValue.FromInt(api.GetItemId(itemObject));
                        else
                            result = PasValue.FromInt(args[0].AsInt());
                        return true;
                    }
                    return false;
                case "ysbinditem":
                    if (args.Count >= 2) { result = PasValue.FromInt(api.SetItemBindByItemId(args[0].AsInt(), args[1].AsInt())); return true; }
                    return false;
                case "ysgetbodyitem":
                    if (args.Count >= 2)
                    {
                        result = PasValue.FromObject(api.GetBodyItem(args[0].ObjVal as TPlayObject, args[1].AsInt()));
                        return true;
                    }
                    return false;

                // ═══ 6.8 角色属性/组队 ═══
                case "ys_getshuxing": case "getcreatureattr":
                    if (args.Count >= 2) { result = PasValue.FromInt(api.GetCreatureAttr(args[0].AsInt(), args[1].AsInt())); return true; }
                    return false;
                case "ys_getmembercount": case "getgroupmembercount":
                    result = PasValue.FromInt(api.GetGroupMemberCount()); return true;
                case "ys_getmember_roleid": case "getgroupmemberroleid":
                    if (args.Count >= 1) { result = PasValue.FromInt(api.GetGroupMemberRoleId(args[0].AsInt())); return true; }
                    return false;
                case "ys_getmember_playername": case "getgroupmembername":
                    if (args.Count >= 1) { result = PasValue.FromString(api.GetGroupMemberName(args[0].AsInt())); return true; }
                    return false;
                case "ys_getheroextreme": case "getheroextreme":
                    if (args.Count >= 2) { result = PasValue.FromInt(api.GetHeroExtreme(args[0].AsInt(), args[1].AsInt())); return true; }
                    return false;
                case "ys_myskillexp": case "setskillexp":
                    if (args.Count >= 3) { result = PasValue.FromInt(api.SetSkillExp(args[0].AsString(), args[1].AsInt(), args[2].AsInt())); return true; }
                    return false;

                // ═══ 6.9 数据库操作 ═══
                case "ys_sqldbinsert": case "sqldbinsert":
                    if (args.Count >= 2) { result = PasValue.FromInt(api.SqlDbInsert(args[0].AsString(), args[1].AsInt() != 0)); return true; }
                    return false;
                case "ys_sqldbselect": case "sqldbselect":
                    if (args.Count >= 1) { result = PasValue.FromString(api.SqlDbSelect(args[0].AsString())); return true; }
                    return false;
                case "ys_senddbmsg": case "senddbmsg":
                    if (args.Count >= 2) { result = PasValue.FromInt(api.SendDbMsg(args[0].AsInt(), args[1].AsString())); return true; }
                    return false;

                // ═══ 6.10 其他 ═══
                case "ys_doeffect": case "playeffect":
                    if (args.Count >= 5) { result = PasValue.FromInt(api.PlayEffect(args[0].AsInt(), args[1].AsInt(), args[2].AsInt(), args[3].AsInt(), args[4].AsInt())); return true; }
                    return false;
                case "ys_tantanskill": case "bounceskill":
                    if (args.Count >= 10) { result = PasValue.FromInt(api.BounceSkill(args[0].AsInt(), args[1].AsInt(), args[2].AsInt(), args[3].AsInt(), args[4].AsInt(), args[5].AsInt(), args[6].AsInt(), args[7].AsInt(), args[8].AsInt(), args[9].AsInt())); return true; }
                    return false;
                case "ys_magic_huoqiang": case "customfirewall":
                    if (args.Count >= 7) { result = PasValue.FromInt(api.CustomFireWall(args[0].AsInt(), args[1].AsInt(), args[2].AsInt(), args[3].AsInt(), args[4].AsInt(), args[5].AsInt(), args[6].AsInt())); return true; }
                    return false;
                case "ys_sendclienteffect": case "sendclienteffect":
                    if (args.Count >= 9) { result = PasValue.FromInt(api.SendClientEffect(args[0].AsInt(), args[1].AsInt(), args[2].AsInt(), args[3].AsInt(), args[4].AsInt(), args[5].AsInt(), args[6].AsInt(), args[7].AsString(), args[8].AsString(), args.Count > 9 ? args[9].AsString() : "")); return true; }
                    return false;
                case "ys_rename": case "rename":
                    if (args.Count >= 1) { result = PasValue.FromInt(api.Rename(args[0].AsString())); return true; }
                    return false;
                case "ys_playerout": case "kickplayer":
                    result = PasValue.FromInt(api.KickPlayer()); return true;
                case "ys_checkmapmonbyname": case "checkmapmonbyname":
                    if (args.Count >= 2) { result = PasValue.FromInt(api.CheckMapMonByName(args[0].AsString(), args[1].AsString())); return true; }
                    return false;
                case "ys_setloopertimer": case "setlooptimer":
                    if (args.Count >= 2) { result = PasValue.FromInt(api.SetLoopTimer(args[0].AsInt(), args[1].AsString())); return true; }
                    return false;
                case "ys_getcastleguildname": case "getcastleguildname":
                    result = PasValue.FromString(api.GetCastleGuildName()); return true;
                case "ys_getcastlelordname": case "getcastlelordname":
                    result = PasValue.FromString(api.GetCastleLordName()); return true;
                case "yssafezone":
                    if (args.Count >= 1) { result = PasValue.FromBool(api.IsSafeZone(args[0].AsInt())); return true; }
                    return false;
                case "ysgetonlineplayernum":
                    result = PasValue.FromInt(api.GetOnlinePlayerNum()); return true;
                case "yskillrole":
                    if (args.Count >= 1) { result = PasValue.FromBool(api.KillRole(args[0].AsInt())); return true; }
                    return false;
                case "yschangerole":
                    if (args.Count >= 7) { result = PasValue.FromInt(api.ChangeRole(args[0].AsInt(), args[1].AsInt(), args[2].AsInt(), args[3].AsInt(), args[4].AsInt(), args[5].AsInt(), args[6].AsInt())); return true; }
                    return false;
                case "yskillmon":
                    if (args.Count >= 2) { result = PasValue.FromInt(api.KillMon(args[0].AsString(), args[1].AsString())); return true; }
                    return false;
                case "ysfindplayerbyname":
                    if (args.Count >= 1) { result = PasValue.FromObject(api.FindPlayerByName(args[0].AsString())); return true; }
                    return false;
                case "yssay":
                    if (args.Count >= 2) { result = PasValue.FromInt(api.Say(args[0].AsInt(), args[1].AsString())); return true; }
                    return false;
                case "ysyeman":
                    if (args.Count >= 4) { result = PasValue.FromInt(api.WildCharge(args[0].ObjVal as TPlayObject, args[1].AsInt(), args[2].AsInt(), args[3].AsInt())); return true; }
                    return false;
                case "yscreatemon":
                    if (args.Count >= 12) { result = PasValue.FromInt(api.CreateMon(args[0].AsString(), args[1].AsInt(), args[2].AsInt(), args[3].AsInt(), args[4].AsString(), args[5].AsInt(), args[6].AsInt(), args[7].AsInt(), args[8].AsInt(), args[9].AsInt(), args[10].AsInt(), args[11].AsInt())); return true; }
                    return false;
                case "ysattact":
                    if (args.Count >= 3 && args[0].ObjVal is TPlayObject attackPlayer) { result = PasValue.FromInt(api.AttackPlayer(attackPlayer, args[1].AsInt(), args[2].AsInt())); return true; }
                    return false;
                case "yssetg": case "ys_setg":
                    if (args.Count >= 2) { result = PasValue.FromInt(api.SetGlobalInt(args[0].AsString(), args[1].AsInt())); return true; }
                    return false;
                case "ysgetg": case "ys_getg":
                    if (args.Count >= 1) { result = PasValue.FromInt(api.GetGlobalInt(args[0].AsString())); return true; }
                    return false;
                case "yssetstr": case "ys_setstr":
                    if (args.Count >= 2) { result = PasValue.FromInt(api.SetGlobalString(args[0].AsString(), args[1].AsString())); return true; }
                    return false;
                case "ysgetstr": case "ys_getstr":
                    if (args.Count >= 1) { result = PasValue.FromString(api.GetGlobalString(args[0].AsString())); return true; }
                    return false;
                case "ysgetheroshuxing":
                    if (args.Count >= 2) { result = PasValue.FromInt(api.GetHeroAttr(args[0].ObjVal as TPlayObject, args[1].AsInt())); return true; }
                    return false;
                case "ysnewtuitui":
                    if (args.Count >= 10) { result = PasValue.FromInt(api.NewPushPull(args[0].ObjVal as TPlayObject, args[1].AsInt(), args[2].AsInt(), args[3].AsInt(), args[4].AsInt(), args[5].AsInt(), args[6].AsInt(), args[7].AsInt(), args[8].AsInt(), args[9].AsInt())); return true; }
                    return false;

                // ═══ CD 时间系统 ═══
                case "ys_cdgettimes": case "cdgettimes":
                    if (args.Count >= 1) { result = PasValue.FromInt(api.CDGetTimes(args[0].AsInt())); return true; }
                    return false;
                case "ys_cdgettimesms": case "cdgettimesms":
                    result = PasValue.FromInt(api.CDGetTimesMs()); return true;
                case "ys_cdcmptime": case "cdcmptime":
                    if (args.Count >= 3) { result = PasValue.FromBool(api.CDCmpTime(args[0].AsInt(), args[1].AsInt(), args[2].AsInt())); return true; }
                    return false;
                case "ys_cdcmptimems": case "cdcmptimems":
                    if (args.Count >= 3) { result = PasValue.FromBool(api.CDCmpTimeMs(args[0].AsInt(), args[1].AsInt(), args[2].AsInt())); return true; }
                    return false;
                case "ys_cdgetremaining": case "cdgetremaining":
                    if (args.Count >= 3) { result = PasValue.FromInt(api.CDGetRemaining(args[0].AsInt(), args[1].AsInt(), args[2].AsInt())); return true; }
                    return false;
                case "ys_cdgetdiff": case "cdgetdiff":
                    if (args.Count >= 2) { result = PasValue.FromInt(api.CDGetDiff(args[0].AsInt(), args[1].AsInt())); return true; }
                    return false;

                // ═══ 2.08 原版函数名（Player-first 签名 → 参数索引右移一位吸收首位 Player）═══
                // args[0]=Player(通常 This_Player==CurrentPlayer)；实参从 args[1] 起。
                // 均调用其 C# 孪生函数完全相同的 YanshenApi 方法（该方法以 CurrentPlayer 为主体）。
                case "ys_toubao": // Ys_TouBao(Player;p,v) → EquipInsurance(p,v)
                    if (args.Count >= 3) { result = PasValue.FromInt(api.EquipInsurance(args[1].AsInt(), args[2].AsInt())); return true; }
                    return false;
                case "ys_herojp": // ys_HeroJp(Player;pos,id) → GetHeroExtreme(pos,id)
                    if (args.Count >= 3) { result = PasValue.FromInt(api.GetHeroExtreme(args[1].AsInt(), args[2].AsInt())); return true; }
                    return false;
                case "ys_setherocskill": // Ys_SetHeroCSkill(Player;magicid,isrun) → HeroCastSkill(magicid,isrun)
                    if (args.Count >= 3) { result = PasValue.FromInt(api.HeroCastSkill(args[1].AsInt(), args[2].AsInt())); return true; }
                    return false;
                case "ys_settimerbyname": // Ys_SetTimerByName(Player;timer,fucName) → SetLoopTimer(timer,fucName)
                    if (args.Count >= 3) { result = PasValue.FromInt(api.SetLoopTimer(args[1].AsInt(), args[2].AsString())); return true; }
                    return false;
                case "ys_killbbbyname": // Ys_KillBBbyName(Player;name) → KillPetByName(name)
                    if (args.Count >= 2) { result = PasValue.FromInt(api.KillPetByName(args[1].AsString())); return true; }
                    return false;
                case "ys_getother": // Ys_GetOther(Player;itemid,id,val,types) → GetOther(itemid,id,val,types)
                    if (args.Count >= 5) { result = PasValue.FromInt(api.GetOther(args[1].AsInt(), args[2].AsInt(), args[3].AsInt(), args[4].AsInt())); return true; }
                    return false;
                case "ys_sendmsg": // ys_SendMsg(Player;roleid,id,hp,sx,sy,tx,ty:int;rs,img1,img2:str) → SendClientEffect(...)
                    if (args.Count >= 10) { result = PasValue.FromInt(api.SendClientEffect(args[1].AsInt(), args[2].AsInt(), args[3].AsInt(), args[4].AsInt(), args[5].AsInt(), args[6].AsInt(), args[7].AsInt(), args[8].AsString(), args[9].AsString(), args.Count > 10 ? args[10].AsString() : "")); return true; }
                    return false;

                default:
                    return false;
                }
            }
        }
    }
}
