using GameSvr.Plugins;
using SystemModule;

namespace GameSvr.PasEngine
{
    /// <summary>
    /// PasApiBridge partial — Yanshen API integration.
    /// Registers the yanshen functions as PAS built-in names so a Pascal script
    /// can call them directly instead of going through the !!!! tunnel.
    ///
    /// 名单口径：只登记 2.08 随包脚本里真实存在的名字 —— 解密后的
    /// AllFuc.pas 声明 104 个、NpcFuc.pas 1 个，另有官方《AllFuc 使用例子》
    /// 声明式注释给出的 20 个插件原生注册名，合计 125。其中 108 个在本表实现。
    ///
    /// 不得再往本表添加英文意译名或改拼写的变体。原版没有它们；登记后
    /// 只会让后来者误以为是逆向结果。同理，原生 M2 脚本函数名
    /// (CheckMapMonByName / GetCastleGuildName 等) 也不属于本表 —— 它们由
    /// PasApiBridge 的原生派发路径处理，登进这里会让它们被眼神开关门控。
    ///
    /// Player-first 名 (ys_toubao/ys_herojp/ys_sendmsg/ys_settimerbyname/
    /// ys_setherocskill/ys_killbbbyname/ys_getother) 的参数索引右移一位以吸收
    /// 首位 Player 实参。
    /// 秒级 CD 族 (ys_setcd/ys_cmptime/ys_gethowtime) 与其毫秒同胞
    /// (ys_setcd_min/ys_cmptime_min) 同址实现于 PasApiBridge.CallStandaloneFunction，
    /// 使用玩家 V 变量 + 时间戳隧道，而非无状态的 api.CD* 方法。
    /// </summary>
    public partial class PasApiBridge
    {
        private static readonly HashSet<string> YanshenApiNames = new(
            @"
            ys_addhp ys_addmp ys_addshuxing ys_addshuxing_pro ys_bbflowme ys_cdgettimes
            ys_change_ly ys_checkmapmonbyname ys_checkwupinisbind ys_chgbigbag ys_cutting
            ys_decexp ys_dingshen ys_doeffect ys_dropitem ys_dropitembyid ys_dropitembyname
            ys_geta ys_getcastleguildname ys_getclientitemidbyitemid
            ys_getdatabyclientitemid ys_getfzhong ys_getitemdbdata ys_getitemid
            ys_getitemjp ys_getmember_playername ys_getmember_roleid ys_getmembercount
            ys_getother ys_getpis ys_getshuxing ys_getsxbyname ys_getys ys_givebb_sx
            ys_givebbskill ys_givebind ys_givedataitem ys_giveduar ys_giveexp ys_giveitem
            ys_giveitemys_jp ys_givenewitem ys_givepis ys_healing ys_herojp ys_huishou
            ys_jitui ys_jitui2 ys_killbbbyname ys_magic_huoqiang ys_makeslave
            ys_makeslaveex ys_myjn_delay ys_myjn_effect ys_myjn_plus ys_myjn_plus2
            ys_myjn_super ys_myjn_undead ys_mymabi ys_myskillexp ys_myysjn ys_newxiguai
            ys_npcgiveitemys ys_pick ys_playerout ys_rename ys_repairinbag ys_senddbmsg
            ys_sendmsg ys_seta ys_setherocskill ys_setitemjp ys_setpetv ys_settimerbyname
            ys_setys ys_shidu ys_shidu_effect ys_sqldbinsert ys_sqldbselect ys_subshuxing
            ys_tantanskill ys_toubao ys_tuitui ys_tuitui2 ys_wupingetdata
            ys_wupingetdata2take ys_wupinmakeindex ys_xixue ysattact ysbinditem
            yschangerole yscreatemon ysfindplayerbyname ysgetbodyitem ysgetg
            ysgetheroshuxing ysgetitem ysgetitemid ysgetonlineplayernum ysgetstr yskillmon
            yskillrole ysnewtuitui yssafezone yssay yssetg yssetstr ysyeman
            ".Split(new[] { ' ', '\r', '\n', '\t' }, StringSplitOptions.RemoveEmptyEntries),
            StringComparer.OrdinalIgnoreCase);

        private static readonly IReadOnlyDictionary<string, string[]> YanshenApiFeatures =
            BuildYanshenApiFeatures();

        private static IReadOnlyDictionary<string, string[]> BuildYanshenApiFeatures()
        {
            var features = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);

            AddYanshenFeature(features, "自定义元素",
                "ys_givepis", "ys_getpis", "ys_givenewitem", "ys_giveitemys_jp",
                "ys_givedataitem", "ys_npcgiveitemys", "ys_giveitem", "ys_setys",
                "ys_getys", "ys_getitemjp", "ys_setitemjp", "ys_giveduar",
                "ys_getitemid", "ys_getclientitemidbyitemid");

            AddYanshenFeature(features, "刀刀切割",
                "ys_myjn_plus", "ys_myjn_plus2", "ys_myjn_undead", "ys_myjn_super",
                "ys_myjn_delay", "ys_cutting", "ys_myysjn", "ys_healing",
                "ys_subshuxing", "ys_addshuxing", "ys_addshuxing_pro", "ys_addhp",
                "ys_addmp", "ys_giveexp");

            AddYanshenFeature(features, "野蛮麻痹",
                "ys_jitui", "ys_jitui2", "ys_tuitui2", "ys_dingshen");

            AddYanshenFeature(features, "特殊宝宝",
                "ys_makeslave", "ys_getsxbyname");

            AddYanshenFeature(features, "自定义伤害",
                "ys_doeffect", "ys_tantanskill");

            AddYanshenFeature(features, "眼神特殊函数",
                "ys_sqldbinsert", "ys_sqldbselect", "ys_senddbmsg", "ys_wupinmakeindex",
                "ys_wupingetdata", "ys_wupingetdata2take", "ys_getdatabyclientitemid",
                "ys_bbflowme", "ys_getfzhong", "ys_getmembercount",
                "ys_getmember_roleid", "ys_getmember_playername", "ys_decexp",
                "ys_rename", "ys_getmember");

            AddYanshenFeature(features, "施毒术",
                "ys_shidu");
            AddYanshenFeature(features, "麻痹概率",
                "ys_mymabi");
            AddYanshenFeature(features, "攻击吸血",
                "ys_xixue");
            AddYanshenFeature(features, "全屏吸怪",
                "ys_newxiguai");

            AddYanshenFeature(features, "高级回收",
                "ys_huishou", "ys_dropitem", "ys_repairinbag");
            AddYanshenFeature(features, "屏蔽自动绑定",
                "ys_givebind", "ys_dropitembyid", "ys_dropitembyname",
                "ys_checkwupinisbind");
            AddYanshenFeature(features, "装备来源",
                "ys_change_ly");
            AddYanshenFeature(features, "行会显示",
                "ys_getshuxing", "ys_checkmapmonbyname");
            AddYanshenFeature(features, "火墙设置时间上限",
                "ys_myskillexp");
            AddYanshenFeature(features, "踢玩家下线",
                "ys_playerout");

            AddYanshenFeature(features, "大背包",
                "ys_chgbigbag");

            AddYanshenFeature(features, "获取沙城归属",
                "ys_getcastleguildname");
            AddYanshenFeature(features, "毫秒级cd记录",
                "ys_cdgettimes");

            AddYanshenFeatures(features, new[] { "眼神特殊函数", "全屏拾取" },
                "ys_pick");
            AddYanshenFeature(features, "怪物伤害触发技能特效",
                "ys_givebbskill", "ys_givebb_sx");
            AddYanshenFeature(features, "眼神特殊函数",
                "ys_setpetv", "ys_makeslaveex");
            AddYanshenFeatures(features,
                new[] { "眼神特殊函数", "自定义伤害_plus", "super攻击触发" },
                "ys_myjn_effect");
            AddYanshenFeatures(features, new[] { "眼神特殊函数", "super攻击触发" },
                "ys_shidu_effect", "ys_tuitui");
            AddYanshenFeatures(features, new[] { "眼神特殊函数", "指定技能id免伤" },
                "ys_seta", "ys_geta");
            AddYanshenFeature(features, "火墙修改",
                "ys_magic_huoqiang");

            // 2.08 原版函数名别名（Player-first 签名；switch 中参数索引右移一位）。
            // 每个别名归属其 C# 孪生函数所在的同一 feature，以复用相同开关门控。
            AddYanshenFeature(features, "自定义元素",
                "ys_toubao", "ys_getother");
            AddYanshenFeature(features, "英雄读取极品",
                "ys_herojp");
            AddYanshenFeature(features, "眼神特殊函数",
                "ys_sendmsg");
            AddYanshenFeature(features, "全局循环函数",
                "ys_settimerbyname");
            AddYanshenFeature(features, "指定英雄放技能",
                "ys_setherocskill");
            AddYanshenFeature(features, "特殊宝宝",
                "ys_killbbbyname");

            // Public Pascal wrappers whose implementation remains in AllFuc/NpcFuc.
            AddYanshenFeature(features, "自定义元素",
                "ys_giveitem_ly");
            AddYanshenFeature(features, "npc自定义函数",
                "npc_creatmons");

            // 2.08 does not give these utility APIs independent switches. Keep them
            // fail-closed behind the documented general Pascal API switch.
            AddYanshenFeature(features, "眼神特殊函数",
                "ys_getitemdbdata", "ysgetitem", "ysgetitemid", "ysbinditem",
                "ysgetbodyitem", "ysgetheroshuxing", "yssafezone",
                "ysgetonlineplayernum", "yskillrole", "yschangerole", "yskillmon",
                "ysfindplayerbyname", "yssay", "ysyeman", "yscreatemon", "ysattact",
                "yssetg", "ysgetg", "yssetstr", "ysgetstr", "ysnewtuitui");

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
                "npc_creatmons" => true,
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

        /// <summary>
        /// AllFuc.pas reaches the two sabak lookups through out-of-range body
        /// slots rather than a !!!! payload:
        ///   Ys_GetCastleGuildName() = This_Player.GetItemNameOnBody(10000)
        ///   Ys_GetCastleLoadName()  = This_Player.GetItemNameOnBody(10001)
        /// Every login runs the second one — the shipped initys() gates on
        /// `Length(Ys_GetCastleLoadName()) &lt; 1`.
        /// </summary>
        private bool TryExecuteCastleNameTunnel(int pos, out string name)
        {
            name = null;
            if (pos != 10000 && pos != 10001) return false;

            const string apiName = "GetItemNameOnBody";
            YanshenApi.EnsureDirectCallReady(M2Share.PluginManager, apiName);
            var api = GetYanshenApi();
            if (api == null) return false;
            using var directCall = YanshenApi.BeginStrictDirectCall(apiName);
            api.EnsureFeatureEnabled("获取沙城归属");

            name = pos == 10000 ? api.GetCastleGuildName() : api.GetCastleLoadName();
            return true;
        }

        /// <summary>
        /// 眼神「读取英雄装备」把 GetItemNameOnBody 的 50..65 号身上格改读英雄身上格 0..15。
        ///
        /// 宿主 sub_6E04CC 就是脚本函数 GetItemNameOnBody —— 注册处
        /// <c>0x0073176B mov edx,0x006E04CC</c> / <c>0x00731771 mov ecx,0x007328C8</c>，
        /// <c>0x007328C8</c> 的 Delphi 长度前缀是 17，串即 "GetItemNameOnBody"。函数体：
        /// <code>
        ///   0x006E04E1  mov  eax,[player+0x4C0]        ; 身上格容器
        ///   0x006E04E7  call sub_75EC20                ; ← 补丁点
        ///   0x006E04EC  mov  ebx,eax / test ebx,ebx / je
        ///   0x006E04F6  call sub_784568                ; out := StdItem([item+0x1C]+4).Name
        /// </code>
        /// 开关打开时装 95 字节桩（安装点 <c>0x100D533D call 0x10032CC0</c>，
        /// 目标 <c>0x006E04E7</c>，续跑点 <c>0x006E04EC</c>，
        /// 门控 <c>0x100D50A6</c> 那一组之后的 <c>cmp [ebx+0x1030],0</c>）：
        /// <code>
        ///   81 7D 04 0B F2 4E 00  cmp dword [ebp+4],0x004EF20B   ; 必须经脚本引擎
        ///   0F 85 ..              jne  orig                      ;   调用桩 0x004EF208 进来
        ///   83 FA 32 / 0F 8C ..   cmp edx,0x32 / jl  orig        ; 有符号下界 50
        ///   83 FA 41 / 0F 8F ..   cmp edx,0x41 / jg  orig        ; 有符号上界 65
        ///   8B B3 B0 0B 00 00     mov esi,[player+0xBB0]         ; 英雄对象
        ///   81 FE 00 00 40 00     cmp esi,0x400000 / jb orig
        ///   8B B6 C0 04 00 00     mov esi,[hero+0x4C0]           ; 英雄身上格容器
        ///   8B FA / 83 EF 32      mov edi,edx / sub edi,0x32
        ///   8B 74 BE 08           mov esi,[esi+edi*4+8]          ; 与 sub_75EC20 的
        ///   81 FE 00 00 40 00     cmp esi,0x400000 / jb orig     ;   [esi+eax*4+8] 同步长
        ///   8B C6 / E9 ..         mov eax,esi / jmp resume       ; 命中则整条 call 不执行
        ///  orig:  E8 &lt;0x0075EC20&gt;  call sub_75EC20
        /// </code>
        /// 四条回退路径（非脚本调用 / 越界 / 无英雄 / 该格为空）都落回原生调用，而原生
        /// <c>sub_75EC48</c> 的 <c>sub dl,0x10 / setb al</c> 让 50..65 恒返回 nil，
        /// 所以回退等价于返回空串——与下面玩家格分支的 else 分支同结果。
        /// 还原支 <c>0x100D53E8 call 0x10033340(src,5,0x6E04E7,0x6E04E7)</c> 写回
        /// <c>E8 34 E7 07 00</c>，与转储一致。
        /// </summary>
        private bool TryReadHeroEquipName(int pos, out string name)
        {
            name = null;
            if (pos < 0x32 || pos > 0x41) return false;

            var api = GetYanshenApi();
            if (api == null || !api.IsHeroReadEquip()) return false;

            var slots = CurrentPlayer?.m_HeroObject?.m_UseItems;
            if (slots == null) return false;

            var slot = pos - 0x32;
            if (slot >= slots.Length) return false;

            var item = slots[slot];
            if (item == null) return false;

            name = M2Share.UserEngine.GetStdItem(item.wIndex)?.Name ?? string.Empty;
            return true;
        }

        /// <summary>
        /// `#$$#` is the plugin's second command prefix, carried on PlayerNotice.
        /// 2.08 AllFuc.pas has exactly one live user:
        ///   procedure Ys_XiGuai(Player:TPlayer);
        ///   begin Player.PlayerNotice('#$$#眼神全屏吸怪',1314); end;
        /// Only that literal is claimed here; any other `#$$#` string stays a
        /// normal notice rather than being swallowed on a guess.
        /// </summary>
        private bool TryExecuteNoticeTunnel(string notice)
        {
            if (!string.Equals(notice, "#$$#眼神全屏吸怪", StringComparison.Ordinal))
                return false;

            const string apiName = "PlayerNotice";
            YanshenApi.EnsureDirectCallReady(M2Share.PluginManager, apiName);
            var api = GetYanshenApi();
            if (api == null) return false;
            using var directCall = YanshenApi.BeginStrictDirectCall(apiName);
            api.EnsureFeatureEnabled("全屏吸怪");

            // The no-argument form takes its range and level cap from S variables
            // instead of parameters; Ys_NewXiGuai(round,lv,num) is the later form
            // that passes them directly. num has no S slot, so it stays unlimited.
            api.VacuumMonstersEx(GetPlayerVar('S', 1, 123).AsInt(),
                GetPlayerVar('S', 1, 124).AsInt(), 0);
            return true;
        }

        /// <summary>
        /// NpcFuc.pas smuggles its monster spawner through CheckMapMonByName by
        /// passing the literal 'yanshen2.0.7' where a map name belongs:
        ///   result:=This_NPC.CheckMapMonByName('yanshen2.0.7',res);
        /// with res = '0^x^y^num^round^Ac^Mac^Dc^DcMax^Mc^Sc^Speed^Hit^hp^Maxhp^
        /// AttackSpd^WalkSpd^MonName^Map'.
        ///
        /// Attribute writes are the plugin JSON apply at 0x100884f0. A malformed
        /// payload throws rather than returning 0, because 0 is what the count
        /// path would report for a map that does not exist.
        /// </summary>
        private bool TryNpcCreatMonsTunnel(string mapName, string payload, out int spawned)
        {
            spawned = 0;
            if (!string.Equals(mapName, YanshenApi.NpcCreatMonsSentinel, StringComparison.OrdinalIgnoreCase))
                return false;

            const string apiName = "NPC_CreatMons";
            YanshenApi.EnsureDirectCallReady(M2Share.PluginManager, apiName);
            var api = GetYanshenApi(allowWithoutPlayer: true);
            if (api == null)
                throw new YanshenApiUnavailableException(apiName, "npc自定义函数", "无法构造 API");

            using var directCall = YanshenApi.BeginStrictDirectCall(apiName);
            api.EnsureFeatureEnabled("npc自定义函数");
            spawned = api.NpcCreateMonsFromPayload(payload);
            return true;
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
                case "ys_givepis":
                    if (args.Count >= 3) { api.GivePis(args[0].AsInt(), args[1].AsInt(), args[2].AsInt()); return true; }
                    return false;
                case "ys_getpis":
                    if (args.Count >= 2) { result = PasValue.FromInt(api.GetPis(args[0].AsInt(), args[1].AsInt())); return true; }
                    return false;
                // Ys_GiveNewItem(Player;ItemName:string;isbind,ys1..ys17:integer)
                // — isbind sits between the name and ys1, so the element block
                // starts at args[2] and the bind flag is a real argument.
                case "ys_givenewitem":
                    if (args.Count >= 19)
                    {
                        var ysArr = new int[17];
                        for (int i = 0; i < 17; i++) ysArr[i] = args[i + 2].AsInt();
                        api.GiveNewItem(args[0].AsString(), args[1].AsInt(), ysArr);
                        return true;
                    }
                    return false;
                // Ys_GiveItemYS_JP(Player;ItemName:string;isbind,ys1..ys17,jp1..jp5,yjp6:integer)
                case "ys_giveitemys_jp":
                    if (args.Count >= 25)
                    {
                        var ysJp = new int[17];
                        var jpJp = new int[6];
                        for (int i = 0; i < 17; i++) ysJp[i] = args[i + 2].AsInt();
                        for (int i = 0; i < 6; i++) jpJp[i] = args[i + 19].AsInt();
                        api.GiveItemYS_JP(args[0].AsString(), args[1].AsInt(), ysJp, jpJp);
                        return true;
                    }
                    return false;
                case "ys_givedataitem":
                    if (args.Count >= 2) { api.GiveDataItem(args[0].AsString(), args[1].AsString()); return true; }
                    return false;
                // Ys_NpcGiveItemYs(Player;ClientItemID,ys1..ys17:integer):integer
                case "ys_npcgiveitemys":
                    if (args.Count >= 18)
                    {
                        var ysNpc = new int[17];
                        for (int i = 0; i < 17; i++) ysNpc[i] = args[i + 1].AsInt();
                        result = PasValue.FromInt(api.NpcGiveItemYs(args[0].AsInt(), ysNpc));
                        return true;
                    }
                    return false;
                case "ys_giveitem":
                    if (args.Count >= 6) { api.GiveItem5El(args[0].AsString(), args[1].AsInt(), args[2].AsInt(), args[3].AsInt(), args[4].AsInt(), args[5].AsInt()); return true; }
                    return false;
                case "ys_setys":
                    if (args.Count >= 3) { result = PasValue.FromInt(api.SetEquipElement(args[0].AsInt(), args[1].AsInt(), args[2].AsInt())); return true; }
                    return false;
                case "ys_getys":
                    if (args.Count >= 3) { result = PasValue.FromInt(api.GetEquipElement(args[0].AsInt(), args[1].AsInt(), args[2].AsInt())); return true; }
                    return false;
                case "ys_getitemjp":
                    if (args.Count >= 3) { result = PasValue.FromInt(api.GetItemExtreme(args[0].AsInt(), args[1].AsInt(), args[2].AsInt())); return true; }
                    return false;
                case "ys_setitemjp":
                    if (args.Count >= 4) { result = PasValue.FromInt(api.SetItemExtreme(args[0].AsInt(), args[1].AsInt(), args[2].AsInt(), args[3].AsInt())); return true; }
                    return false;
                case "ys_giveduar":
                    if (args.Count >= 3) { result = PasValue.FromInt(api.EquipDura(args[0].AsInt(), args[1].AsInt(), args[2].AsInt())); return true; }
                    return false;

                // ═══ 6.2 技能伤害系统 ═══
                case "ys_myjn_plus":
                    if (args.Count >= 8) { result = PasValue.FromInt(api.CustomDamage(args[0].AsInt(), args[1].AsInt(), args[2].AsInt(), args[3].AsInt(), args[4].AsInt(), args[5].AsInt(), args[6].AsInt(), args[7].AsInt())); return true; }
                    return false;
                case "ys_myjn_plus2":
                    if (args.Count >= 9) { result = PasValue.FromInt(api.CustomDamage2(args[0].AsInt(), args[1].AsInt(), args[2].AsInt(), args[3].AsInt(), args[4].AsInt(), args[5].AsInt(), args[6].AsInt(), args[7].AsInt(), args[8].AsInt())); return true; }
                    return false;
                case "ys_myjn_effect":
                    if (args.Count >= 10) { result = PasValue.FromInt(api.CustomDamageEffect(args[0].AsInt(), args[1].AsInt(), args[2].AsInt(), args[3].AsInt(), args[4].AsInt(), args[5].AsInt(), args[6].AsInt(), args[7].AsInt(), args[8].AsInt(), args[9].AsInt())); return true; }
                    return false;
                case "ys_myjn_undead":
                    if (args.Count >= 11) { result = PasValue.FromInt(api.CustomDamageUndead(args[0].AsInt(), args[1].AsInt(), args[2].AsInt(), args[3].AsInt(), args[4].AsInt(), args[5].AsInt(), args[6].AsInt(), args[7].AsInt(), args[8].AsInt(), args[9].AsInt(), args[10].AsInt())); return true; }
                    return false;
                case "ys_myjn_super":
                    if (args.Count >= 13) { result = PasValue.FromInt(api.CustomDamageSuper(args[0].AsInt(), args[1].AsInt(), args[2].AsInt(), args[3].AsInt(), args[4].AsInt(), args[5].AsInt(), args[6].AsInt(), args[7].AsInt(), args[8].AsInt(), args[9].AsInt(), args[10].AsInt(), args[11].AsInt(), args[12].AsInt())); return true; }
                    return false;
                case "ys_myjn_delay":
                    if (args.Count >= 15) { result = PasValue.FromInt(api.CustomDamageDelay(args[0].AsInt(), args[1].AsInt(), args[2].AsInt(), args[3].AsInt(), args[4].AsInt(), args[5].AsInt(), args[6].AsInt(), args[7].AsInt(), args[8].AsInt(), args[9].AsInt(), args[10].AsInt(), args[11].AsInt(), args[12].AsInt(), args[13].AsInt(), args[14].AsInt())); return true; }
                    return false;
                case "ys_cutting":
                    if (args.Count >= 10) { result = PasValue.FromInt(api.HolyDamage(args[0].AsInt(), args[1].AsInt(), args[2].AsInt(), args[3].AsInt(), args[4].AsInt(), args[5].AsInt(), args[6].AsInt(), args[7].AsInt(), args[8].AsInt(), args[9].AsInt())); return true; }
                    return false;
                case "ys_myysjn":
                    if (args.Count >= 12) { result = PasValue.FromInt(api.SuperDamage14(args[0].AsInt(), args[1].AsInt(), args[2].AsInt(), args[3].AsInt(), args[4].AsInt(), args[5].AsInt(), args[6].AsInt(), args[7].AsInt(), args[8].AsInt(), args[9].AsInt(), args[10].AsInt(), args[11].AsString())); return true; }
                    return false;

                // ═══ 6.3 控制技能 ═══
                case "ys_mymabi":
                    if (args.Count >= 7) { result = PasValue.FromInt(api.Paralysis(args[0].AsInt(), args[1].AsInt(), args[2].AsInt(), args[3].AsInt(), args[4].AsInt(), args[5].AsInt(), args[6].AsInt() != 0)); return true; }
                    return false;
                case "ys_shidu":
                    if (args.Count >= 9) { result = PasValue.FromInt(api.Poison(args[0].AsInt(), args[1].AsInt(), args[2].AsInt(), args[3].AsInt(), args[4].AsInt(), args[5].AsInt(), args[6].AsInt(), args[7].AsInt(), args[8].AsInt())); return true; }
                    return false;
                case "ys_shidu_effect":
                    if (args.Count >= 10) { result = PasValue.FromInt(api.PoisonEffect(args[0].AsInt(), args[1].AsInt(), args[2].AsInt(), args[3].AsInt(), args[4].AsInt(), args[5].AsInt(), args[6].AsInt(), args[7].AsInt(), args[8].AsInt(), args[9].AsInt())); return true; }
                    return false;
                case "ys_jitui":
                    if (args.Count >= 8) { result = PasValue.FromInt(api.PushEnemy(args[0].AsInt(), args[1].AsInt(), args[2].AsInt(), args[3].AsInt(), args[4].AsInt(), args[5].AsInt(), args[6].AsInt(), args[7].AsInt())); return true; }
                    return false;
                case "ys_jitui2":
                    if (args.Count >= 9) { result = PasValue.FromInt(api.PushEnemy2(args[0].AsInt(), args[1].AsInt(), args[2].AsInt(), args[3].AsInt(), args[4].AsInt(), args[5].AsInt(), args[6].AsInt(), args[7].AsInt(), args[8].AsInt())); return true; }
                    return false;
                case "ys_tuitui":
                    if (args.Count >= 8) { result = PasValue.FromInt(api.PullEnemy(args[0].AsInt(), args[1].AsInt(), args[2].AsInt(), args[3].AsInt(), args[4].AsInt(), args[5].AsInt(), args[6].AsInt(), args[7].AsInt())); return true; }
                    return false;
                case "ys_tuitui2":
                    if (args.Count >= 9) { result = PasValue.FromInt(api.PullEnemy2(args[0].AsInt(), args[1].AsInt(), args[2].AsInt(), args[3].AsInt(), args[4].AsInt(), args[5].AsInt(), args[6].AsInt(), args[7].AsInt(), args[8].AsInt())); return true; }
                    return false;
                case "ys_dingshen":
                    // 死调用：AllFuc.pas:513 发的是 '!!!!集成函数,9,'+shijian+'$'，
                    // 切出来只有 3 段；而 9 号实现体 sub_10070FD0 在 0x10071020
                    // `83 F8 0A` cmp eax,0xA / 0x10071023 `73 26` jae 要求 ≥10 段，
                    // 不足就在 0x10071034 `B8 88 FC FF FF` 返回 -888。
                    // ⇒ 2.08 原生上 ys_DingShen 永远走不到正文，恒返回 -888，
                    // 不会去写 STATE_LOCKRUN。这里照原生短路，不调 RootTarget。
                    if (args.Count >= 1) { result = PasValue.FromInt(-888); return true; }
                    return false;
                case "ys_xixue":
                    if (args.Count >= 2) { result = PasValue.FromInt(api.LifeSteal(args[0].AsInt(), args[1].AsInt())); return true; }
                    return false;
                case "ys_newxiguai":
                    if (args.Count >= 3) { result = PasValue.FromInt(api.VacuumMonstersEx(args[0].AsInt(), args[1].AsInt(), args[2].AsInt())); return true; }
                    return false;

                // ═══ 6.4 增益/减益/治疗 ═══
                case "ys_healing":
                    if (args.Count >= 8) { result = PasValue.FromInt(api.Healing(args[0].AsInt(), args[1].AsInt(), args[2].AsInt(), args[3].AsInt(), args[4].AsInt(), args[5].AsInt(), args[6].AsInt(), args[7].AsInt())); return true; }
                    return false;
                case "ys_subshuxing":
                    if (args.Count >= 8) { result = PasValue.FromInt(api.SubTempAttr(args[0].AsInt(), args[1].AsInt(), args[2].AsInt(), args[3].AsInt(), args[4].AsInt(), args[5].AsInt(), args[6].AsInt(), args[7].AsInt())); return true; }
                    return false;
                case "ys_addshuxing":
                    if (args.Count >= 9) { result = PasValue.FromInt(api.AddTempAttr(args[0].AsInt(), args[1].AsInt(), args[2].AsInt(), args[3].AsInt(), args[4].AsInt(), args[5].AsInt(), args[6].AsInt(), args[7].AsInt(), args[8].AsInt())); return true; }
                    return false;
                case "ys_addshuxing_pro":
                    if (args.Count >= 10) { result = PasValue.FromInt(api.AddTempAttrPro(args[0].AsInt(), args[1].AsInt(), args[2].AsInt(), args[3].AsInt(), args[4].AsInt(), args[5].AsInt(), args[6].AsInt(), args[7].AsInt(), args[8].AsInt(), args[9].AsInt())); return true; }
                    return false;
                case "ys_addhp":
                    if (args.Count >= 1) { result = PasValue.FromInt(api.AddMaxHp(args[0].AsInt())); return true; }
                    return false;
                case "ys_addmp":
                    if (args.Count >= 1) { result = PasValue.FromInt(api.AddMaxMp(args[0].AsInt())); return true; }
                    return false;
                case "ys_giveexp":
                    if (args.Count >= 1) { result = PasValue.FromInt(api.GiveExp(args[0].AsInt())); return true; }
                    return false;
                case "ys_decexp":
                    if (args.Count >= 3) { result = PasValue.FromInt(api.DecExp(args[0].AsInt(), args[1].AsInt(), args[2].AsInt())); return true; }
                    return false;
                case "ys_seta":
                    if (args.Count >= 3) { result = PasValue.FromInt(api.SetSkillDmgReduction(args[0].AsString(), args[1].AsInt(), args[2].AsInt())); return true; }
                    return false;
                case "ys_geta":
                    if (args.Count >= 2) { result = PasValue.FromInt(api.GetSkillDmgReduction(args[0].AsString(), args[1].AsInt())); return true; }
                    return false;

                // ═══ 6.5 宝宝/宠物系统 ═══
                case "ys_makeslaveex":
                    if (args.Count >= 13) { result = PasValue.FromInt(api.SummonPet(args[0].AsString(), args[1].AsInt(), args[2].AsInt(), args[3].AsInt(), args[4].AsInt(), args[5].AsInt(), args[6].AsInt(), args[7].AsInt(), args[8].AsInt(), args[9].AsInt(), args[10].AsInt(), args[11].AsInt(), args[12].AsInt())); return true; }
                    return false;
                case "ys_makeslave":
                    if (args.Count >= 14) { result = PasValue.FromInt(api.SummonPetRoyalty(args[0].AsString(), args[1].AsInt(), args[2].AsInt(), args[3].AsInt(), args[4].AsInt(), args[5].AsInt(), args[6].AsInt(), args[7].AsInt(), args[8].AsInt(), args[9].AsInt(), args[10].AsInt(), args[11].AsInt(), args[12].AsInt(), args[13].AsInt())); return true; }
                    return false;
                case "ys_setpetv":
                    if (args.Count >= 12) { result = PasValue.FromInt(api.SetPetAttr(args[0].AsString(), args[1].AsInt(), args[2].AsInt(), args[3].AsInt(), args[4].AsInt(), args[5].AsInt(), args[6].AsInt(), args[7].AsInt(), args[8].AsInt(), args[9].AsInt(), args[10].AsInt(), args[11].AsInt())); return true; }
                    return false;
                case "ys_givebbskill":
                    if (args.Count >= 5) { result = PasValue.FromInt(api.GivePetSkill(args[0].AsInt(), args[1].AsInt(), args[2].AsInt(), args[3].AsInt(), args[4].AsString())); return true; }
                    return false;
                case "ys_givebb_sx":
                    if (args.Count >= 4) { result = PasValue.FromInt(api.GivePetSpecialAttr(args[0].AsInt(), args[1].AsInt(), args[2].AsString(), args[3].AsString())); return true; }
                    return false;
                case "ys_bbflowme":
                    if (args.Count >= 1) { result = PasValue.FromInt(api.PetFollowAttack(args[0].AsInt())); return true; }
                    return false;
                case "ys_getsxbyname":
                    if (args.Count >= 2) { result = PasValue.FromInt(api.GetPetAttrByName(args[0].AsString(), args[1].AsInt())); return true; }
                    return false;

                // ═══ 6.6 物品/背包操作 ═══
                case "ys_huishou":
                    result = PasValue.FromInt(api.AutoRecycle()); return true;
                case "ys_pick":
                    if (args.Count >= 4) { result = PasValue.FromInt(api.AutoPickup(args[0].AsInt(), args[1].AsInt(), args[2].AsInt(), args[3].AsInt())); return true; }
                    return false;
                case "ys_getfzhong":
                    if (args.Count >= 1) { result = PasValue.FromInt(api.GetBagWeight(args[0].AsInt())); return true; }
                    return false;
                case "ys_givebind":
                    if (args.Count >= 2) { result = PasValue.FromInt(api.BindUnbindItem(args[0].AsInt(), args[1].AsInt())); return true; }
                    return false;
                case "ys_dropitem":
                    if (args.Count >= 3) { result = PasValue.FromInt(api.DropItem(args[0].AsInt(), args[1].AsInt(), args[2].AsString())); return true; }
                    return false;
                case "ys_dropitembyid":
                    if (args.Count >= 1) { result = PasValue.FromInt(api.DropEquipByPos(args[0].AsInt())); return true; }
                    return false;
                case "ys_dropitembyname":
                    if (args.Count >= 1) { result = PasValue.FromInt(api.DropEquipByName(args[0].AsString())); return true; }
                    return false;
                case "ys_repairinbag":
                    if (args.Count >= 2) { result = PasValue.FromInt(api.RepairBagByStdMode(args[0].AsInt(), args[1].AsInt())); return true; }
                    return false;
                case "ys_getitemid":
                    if (args.Count >= 1) { result = PasValue.FromInt(api.GetItemIdByClientId(args[0].AsInt())); return true; }
                    return false;
                case "ys_getclientitemidbyitemid":
                    if (args.Count >= 1) { result = PasValue.FromInt(api.GetClientItemIdByItemId(args[0].AsInt())); return true; }
                    return false;
                case "ys_change_ly":
                    if (args.Count >= 4) { result = PasValue.FromInt(api.ModifyItemDesc(args[0].AsInt(), args[1].AsString(), args[2].AsString(), args[3].AsString())); return true; }
                    return false;
                case "ys_chgbigbag":
                    if (args.Count >= 2) { result = PasValue.FromInt(api.ChangeBigBag(args[0].AsString(), args[1].AsString())); return true; }
                    return false;
                // ys_CheckWupinIsBind(...):boolean — the wrapper turns the tunnel's
                // int into a Boolean, so the built-in must hand back a Boolean too.
                case "ys_checkwupinisbind":
                    if (args.Count >= 1) { result = PasValue.FromBool(api.CheckItemBind(args[0].AsString())); return true; }
                    return false;

                // ═══ 6.7 物品数据操作 ═══
                case "ys_wupinmakeindex":
                    result = PasValue.FromString(api.GetBagMakeIndexList(args.Count >= 1 && args[0].AsInt() != 0)); return true;
                case "ys_wupingetdata":
                    if (args.Count >= 1) { result = PasValue.FromString(api.GetItemDataByMakeIndex(args[0].AsInt())); return true; }
                    return false;
                case "ys_wupingetdata2take":
                    if (args.Count >= 1) { result = PasValue.FromString(api.GetItemDataAndRecycle(args[0].AsInt())); return true; }
                    return false;
                case "ys_getdatabyclientitemid":
                    if (args.Count >= 1) { result = PasValue.FromString(api.GetItemDataByClientId(args[0].AsInt())); return true; }
                    return false;
                case "ys_getitemdbdata":
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
                case "ys_getshuxing":
                    if (args.Count >= 2) { result = PasValue.FromInt(api.GetCreatureAttr(args[0].AsInt(), args[1].AsInt())); return true; }
                    return false;
                case "ys_getmembercount":
                    result = PasValue.FromInt(api.GetGroupMemberCount()); return true;
                case "ys_getmember_roleid":
                    if (args.Count >= 1) { result = PasValue.FromInt(api.GetGroupMemberRoleId(args[0].AsInt())); return true; }
                    return false;
                case "ys_getmember_playername":
                    if (args.Count >= 1) { result = PasValue.FromString(api.GetGroupMemberName(args[0].AsInt())); return true; }
                    return false;
                case "ys_myskillexp":
                    if (args.Count >= 3) { result = PasValue.FromInt(api.SetSkillExp(args[0].AsString(), args[1].AsInt(), args[2].AsInt())); return true; }
                    return false;

                // ═══ 6.9 数据库操作 ═══
                case "ys_sqldbinsert":
                    if (args.Count >= 2) { result = PasValue.FromInt(api.SqlDbInsert(args[0].AsString(), args[1].AsInt() != 0)); return true; }
                    return false;
                case "ys_sqldbselect":
                    if (args.Count >= 1) { result = PasValue.FromString(api.SqlDbSelect(args[0].AsString())); return true; }
                    return false;
                case "ys_senddbmsg":
                    if (args.Count >= 2) { result = PasValue.FromInt(api.SendDbMsg(args[0].AsInt(), args[1].AsString())); return true; }
                    return false;

                // ═══ 6.10 其他 ═══
                case "ys_doeffect":
                    if (args.Count >= 5) { result = PasValue.FromInt(api.PlayEffect(args[0].AsInt(), args[1].AsInt(), args[2].AsInt(), args[3].AsInt(), args[4].AsInt())); return true; }
                    return false;
                case "ys_tantanskill":
                    if (args.Count >= 10) { result = PasValue.FromInt(api.BounceSkill(args[0].AsInt(), args[1].AsInt(), args[2].AsInt(), args[3].AsInt(), args[4].AsInt(), args[5].AsInt(), args[6].AsInt(), args[7].AsInt(), args[8].AsInt(), args[9].AsInt())); return true; }
                    return false;
                case "ys_magic_huoqiang":
                    if (args.Count >= 7) { result = PasValue.FromInt(api.CustomFireWall(args[0].AsInt(), args[1].AsInt(), args[2].AsInt(), args[3].AsInt(), args[4].AsInt(), args[5].AsInt(), args[6].AsInt())); return true; }
                    return false;
                case "ys_rename":
                    if (args.Count >= 1) { result = PasValue.FromInt(api.Rename(args[0].AsString())); return true; }
                    return false;
                case "ys_playerout":
                    result = PasValue.FromInt(api.KickPlayer()); return true;
                case "ys_checkmapmonbyname":
                    if (args.Count >= 2) { result = PasValue.FromInt(api.CheckMapMonByName(args[0].AsString(), args[1].AsString())); return true; }
                    return false;
                case "ys_getcastleguildname":
                    result = PasValue.FromString(api.GetCastleGuildName()); return true;
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
                case "yssetg":
                    if (args.Count >= 2) { result = PasValue.FromInt(api.SetGlobalInt(args[0].AsString(), args[1].AsInt())); return true; }
                    return false;
                case "ysgetg":
                    if (args.Count >= 1) { result = PasValue.FromInt(api.GetGlobalInt(args[0].AsString())); return true; }
                    return false;
                case "yssetstr":
                    if (args.Count >= 2) { result = PasValue.FromInt(api.SetGlobalString(args[0].AsString(), args[1].AsString())); return true; }
                    return false;
                case "ysgetstr":
                    if (args.Count >= 1) { result = PasValue.FromString(api.GetGlobalString(args[0].AsString())); return true; }
                    return false;
                case "ysgetheroshuxing":
                    if (args.Count >= 2) { result = PasValue.FromInt(api.GetHeroAttr(args[0].ObjVal as TPlayObject, args[1].AsInt())); return true; }
                    return false;
                case "ysnewtuitui":
                    if (args.Count >= 10) { result = PasValue.FromInt(api.NewPushPull(args[0].ObjVal as TPlayObject, args[1].AsInt(), args[2].AsInt(), args[3].AsInt(), args[4].AsInt(), args[5].AsInt(), args[6].AsInt(), args[7].AsInt(), args[8].AsInt(), args[9].AsInt())); return true; }
                    return false;

                // ═══ CD 时间系统 ═══
                case "ys_cdgettimes":
                    if (args.Count >= 1) { result = PasValue.FromInt(api.CDGetTimes(args[0].AsInt())); return true; }
                    return false;
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
