using SystemModule;
using GameSvr;

namespace GameSvr.Plugins
{
    /// <summary>
    /// Yanshen !!!! tunnel command dispatcher. Parses !!!! protocol, checks
    /// feature toggles, delegates to YanshenApi for actual game logic.
    ///
    /// 基于逆向分析完整还原:
    /// - 40 数字命令ID (格式: !!!!集成函数,ID,参数,参数$；2.07未使用ID 6)
    /// - 15 分隔符命令 (格式: !!!!爱心分割^ID^参数^参数$)
    /// - 7 中文命令名 (格式: !!!!命令名参数:参数:)
    /// - 5 物品给予格式 (物品名!!!!元素数据)
    /// </summary>
    public class YanshenCommandEngine
    {
        readonly TPlayObject _p; readonly NormNpc _n; readonly PluginManager _pm;
        readonly YanshenApi _api;
        public long TotalCommands, TotalErrors;

        /// <summary>
        /// 原生 41 路臂里没有开关门的操作码。1 号臂 0x1007670A 起手是
        /// `8B 15 E4 C0 31 10` + `83 BA 04 01 00 00 64`（版本判定，非开关门）；
        /// 2 号麻痹臂 0x100769B9 首指令即 `8B 4D 08` mov ecx,[ebp+8]，直落
        /// 0x100769EA call 0x1006D690。两臂内都没有 `81 38 F4 01 00 00`
        /// cmp dword[eax],0x1F4 序列。
        /// </summary>
        static readonly HashSet<int> _ungatedCommands = new() { 1, 2 };

        /// <summary>
        /// 每个操作码的门 = 它在 41 路表 0x10077A78 里那条臂读的配置字段，
        /// 与实参个数无关（原生「一族一门」，不是「一函一门」）。
        ///
        /// 32 个臂读同一个全局 0x1031C244 = cfg2+0x11C（accessor 桩 0x100021E0
        /// `A1 E0 C0 31 10` / `05 1C 01 00 00` / `A3 44 C2 31 10`），
        /// 键名由配置序列化器给出：0x1000642A `cmp [esi+0x11c],0x1F4` 之后是
        /// 0x10006456 `68 E4 02 2B 10` push 0x102B02E4
        /// (`D1 DB C9 F1 CC D8 CA E2 BA AF CA FD` = "眼神特殊函数")。
        /// 3 号单独读 0x1031C240 = cfg2+0x524（桩 0x100021D0），
        /// 序列化器 0x1000A183 之后是 0x1000A1B5 `push 0x102B15B0` = "自定义伤害_plus"。
        ///
        /// 6 个臂各有专用门，但它们的字段在两段序列化 run 里都没有条目，
        /// 键名静态不可证，故保留现值并在此登记 —— 仍属 fail-closed：
        ///   25 → cfg2+0x084（桩 0x100020A0）   26 → cfg2+0x0FC（桩 0x100020B0）
        ///   28 → cfg2+0x940（桩 0x10001F90）   30/31 → cfg2+0x1B4（桩 0x10002230）
        ///   37 → cfg2+0x6E0（桩 0x10001D00）
        /// </summary>
        const string SharedTunnelGate = "眼神特殊函数";     // cfg2+0x11C

        static readonly Dictionary<int, string> _toggles = new()
        {
            [3]="自定义伤害_plus",                            // cfg2+0x524
            [4]=SharedTunnelGate,[5]=SharedTunnelGate,[6]=SharedTunnelGate,
            [7]=SharedTunnelGate,[8]=SharedTunnelGate,[9]=SharedTunnelGate,
            [10]=SharedTunnelGate,[11]=SharedTunnelGate,[12]=SharedTunnelGate,
            [13]=SharedTunnelGate,[14]=SharedTunnelGate,[15]=SharedTunnelGate,
            [16]=SharedTunnelGate,[17]=SharedTunnelGate,[18]=SharedTunnelGate,
            [19]=SharedTunnelGate,[20]=SharedTunnelGate,[21]=SharedTunnelGate,
            [22]=SharedTunnelGate,[23]=SharedTunnelGate,[24]=SharedTunnelGate,
            [25]="全局循环函数",                              // cfg2+0x084，键名未证
            [26]="自定义伤害",                                // cfg2+0x0FC，键名未证
            [27]=SharedTunnelGate,
            [28]="指定英雄放技能",                            // cfg2+0x940，键名未证
            [29]=SharedTunnelGate,
            [30]="怪物伤害触发技能特效",                      // cfg2+0x1B4，键名未证
            [31]="怪物伤害触发技能特效",                      // cfg2+0x1B4，键名未证
            [32]=SharedTunnelGate,[33]=SharedTunnelGate,[34]=SharedTunnelGate,
            [35]=SharedTunnelGate,[36]=SharedTunnelGate,
            [37]="火墙修改",                                  // cfg2+0x6E0，键名未证
            [38]=SharedTunnelGate,[39]=SharedTunnelGate,[40]=SharedTunnelGate,
            [41]=SharedTunnelGate,
        };

        // Caret command toggles (^1^ ~ ^37^)
        static readonly Dictionary<int, string> _caretToggles = new()
        {
            [1]="眼神特殊函数",[2]="大背包",[3]="眼神特殊函数",
            [10]="装备来源",[13]="自定义元素",[20]="自定义元素",
            [29]="自定义元素",[30]="高级回收",[31]="行会显示",
            [32]="特殊宝宝",[33]="屏蔽自动绑定",[34]="屏蔽自动绑定",
            [35]="自定义元素",[36]="自定义元素",[37]="特殊宝宝",
            // ^38^ is the only tunnel 2.08 adds over 2.07 (Ys_GetItemDBData).
            [38]="眼神特殊函数",
        };

        /// <summary>参数不足时原生返回的哨兵。三种取值都出现在实现体首部的短路支路上。</summary>
        const int SentinelShort = -888;   // B8 88 FC FF FF   mov eax,0xFFFFFC88
        const int SentinelMinus1 = -1;    // 83 C8 FF         or  eax,-1
        /// <summary>操作码越界（不在 1..41）时派发器自己返回的值。</summary>
        const int SentinelBadOpcode = -777;   // C7 45 E4 F7 FC FF FF

        /// <summary>
        /// 每个操作码的实现体在进入正文前都会先算段数
        /// （<c>(end-begin)/24</c>，`B8 AB AA AA 2A` / `F7 E9` / `C1 FA 02`）
        /// 再和一个下限比，不足就走短路支路返回哨兵。下限是**段数**，
        /// 段 0 是 <c>!!!!集成函数</c>、段 1 是操作码，所以必填实参数 = 下限 − 2。
        ///
        /// 下表 (必填实参数, 哨兵) 逐条取自实现体首部的 `cmp eax,N` / `jae` / 返回值三件套：
        ///
        ///   op  cmpVA       cmp eax,N   短路返回      op  cmpVA       cmp eax,N   短路返回
        ///    3  0x1006DB0C  0x0B  1006DB20 -888       21  0x1007348C  0x03  100734A0 -1
        ///    4  0x100700FF  0x0A  10070113 -888       22  0x10072A81  0x05  10072A95 -1
        ///    5  0x10070700  0x0B  10070714 -888       23  0x100735FF  0x0E  10073936 -1
        ///    7  0x10070D7A  0x05  10070D8E -888       24  0x10073B8C  0x13  10073BA0 -1
        ///    8  0x10070EC8  0x04  10070EDC -888       25  0x10073E6F  0x04  10073E83 -1
        ///    9  0x10071020  0x0A  10071034 -888       26  0x10074105  0x0C  10074119 -1
        ///   10  0x10071761  0x05  1007176A -1         27  0x10074CAC  0x05  10074CC0 -1
        ///   11  0x1007196F  0x04  10071983 -1         28  0x10074F2F  0x04  10074F43 -1
        ///   12  0x1006FE22  0x07  1006FE2E -1         29  0x100750DC  0x03  10075142 -1
        ///   13  0x10071AAE  0x09  10071ABA -1         30  0x100751C1  0x07  100751D5 -1
        ///   14  0x10071F5C  0x09  10071F9A  0         31  0x10075651  0x06  10075665 -1
        ///   15  0x1007269C  0x05  100726B0 -1         32  0x10075BBC  0x06  10075BD0 -1
        ///   16  0x100728EF  0x05  10072903 -1         33  0x100760AB  0x04  100760BF -1
        ///   17  0x10072D1C  0x05  10072D30 -1         34  0x1006E928  0x0C  1006E93C -888
        ///   18  0x10072FDC  0x05  10072FF0  0         37  0x1006F319  0x09  1006F32D -888
        ///   20  0x1007325F  0x04  10073273 -1         39  0x1006F7DD  0x05  1006F7F1 -888
        ///                                             40  0x1006F935  0x05  1006F949 -888
        ///
        /// 1、2、6、19 号实现体没有元数检查（首部无 `cmp eax,N` / `jae` 三件套）。
        /// 35/36/38/41 的下限是 2 段（= 0 个必填实参），检查恒真，故不入表。
        /// </summary>
        static readonly Dictionary<int, (int MinParams, int Sentinel)> _nativeArity = new()
        {
            [3]=(9,SentinelShort),   [4]=(8,SentinelShort),   [5]=(9,SentinelShort),
            [7]=(3,SentinelShort),   [8]=(2,SentinelShort),   [9]=(8,SentinelShort),
            [10]=(3,SentinelMinus1), [11]=(2,SentinelMinus1), [12]=(5,SentinelMinus1),
            [13]=(7,SentinelMinus1), [14]=(7,0),              [15]=(3,SentinelMinus1),
            [16]=(3,SentinelMinus1), [17]=(3,SentinelMinus1), [18]=(3,0),
            [20]=(2,SentinelMinus1), [21]=(1,SentinelMinus1), [22]=(3,SentinelMinus1),
            [23]=(12,SentinelMinus1),[24]=(17,SentinelMinus1),[25]=(2,SentinelMinus1),
            [26]=(10,SentinelMinus1),[27]=(3,SentinelMinus1), [28]=(2,SentinelMinus1),
            [29]=(1,SentinelMinus1), [30]=(5,SentinelMinus1), [31]=(4,SentinelMinus1),
            [32]=(4,SentinelMinus1), [33]=(2,SentinelMinus1), [34]=(10,SentinelShort),
            [37]=(7,SentinelShort),  [39]=(3,SentinelShort),  [40]=(3,SentinelShort),
        };

        static readonly Dictionary<string, string> _chineseToggles =
            new(StringComparer.OrdinalIgnoreCase)
        {
            ["plus伤害"]="刀刀切割",
            ["给与元素"]="自定义元素",["给予元素"]="自定义元素",["获取元素"]="自定义元素",
            ["定义伤害"]="刀刀切割",["攻击伤害"]="刀刀切割",
            ["英雄极品"]="自定义元素",
            ["hq取sj戳"]="毫秒级cd记录",["hq取sj间"]="毫秒级cd记录",
            ["zd义回收"]="高级回收",["zd回收"]="高级回收",
        };

        public YanshenCommandEngine(TPlayObject p, NormNpc n, PluginManager pm = null)
        { _p = p; _n = n; _pm = pm; _api = new YanshenApi(p, n, pm); }

        void EnsureCommandEnabled(TunnelCommand cmd, string apiName)
        {
            string[] featureNames = null;
            if (cmd.Format == TunnelFormat.CaretSeparated)
            {
                if (_caretToggles.TryGetValue(cmd.CommandId, out var featureName))
                    featureNames = new[] { featureName };
            }
            else if (cmd.Format == TunnelFormat.ChineseName)
            {
                if (_chineseToggles.TryGetValue(cmd.ChineseCommand ?? string.Empty,
                        out var featureName))
                    featureNames = new[] { featureName };
            }
            else
            {
                featureNames = GetStandardCommandFeatures(cmd);
            }

            if (featureNames == null)
                throw new YanshenApiUnavailableException(apiName, null,
                    $"命令未登记（{cmd.RawPayload}）");
            foreach (var featureName in featureNames)
                _api.EnsureFeatureEnabled(featureName);
        }

        private static string[] GetStandardCommandFeatures(TunnelCommand cmd)
        {
            // 原生的门挂在臂上，与实参个数无关 —— 41 路表里没有任何一条臂会因为
            // 段数不同而换一把门（段数只被实现体首部的元数检查用，见 _nativeArity）。
            // 因此这里不能再按 Parameters.Length 分流出「多键门」。
            if (_ungatedCommands.Contains(cmd.CommandId)) return Array.Empty<string>();
            return _toggles.TryGetValue(cmd.CommandId, out var featureName)
                ? new[] { featureName }
                : null;
        }

        public bool IsFeatureEnabled(int cmdId)
        {
            if (!_toggles.TryGetValue(cmdId, out var chineseKey)) return true;
            if (_pm == null) return true;
            var val = _pm.GetNativeConfigValue(chineseKey);
            if (val is int i) return i != 0;
            if (val is string s) return s != "0" && s != "0.0" && s != "";
            if (val is System.Text.Json.JsonElement je)
            {
                if (je.ValueKind == System.Text.Json.JsonValueKind.False) return false;
                if (je.ValueKind == System.Text.Json.JsonValueKind.Number) return je.GetDouble() != 0;
                if (je.ValueKind == System.Text.Json.JsonValueKind.String)
                { var str = je.GetString(); return !(str == "0" || str == "0.0" || str == ""); }
                return je.ValueKind != System.Text.Json.JsonValueKind.Null;
            }
            return val != null && !(val is double d && d == 0);
        }

        public string GetFeatureName(int cmdId)
        {
            return _toggles.TryGetValue(cmdId, out var key) ? key : $"cmd_{cmdId}";
        }

        string GetCaretName(int caretId)
        {
            return _caretToggles.TryGetValue(caretId, out var key) ? key : $"caret_{caretId}";
        }

        bool IsCaretEnabled(int caretId)
        {
            if (!_caretToggles.TryGetValue(caretId, out var chineseKey)) return true;
            if (_pm == null) return true;
            var val = _pm.GetNativeConfigValue(chineseKey);
            if (val is int i) return i != 0;
            if (val is string s) return s != "0" && s != "0.0" && s != "";
            if (val is System.Text.Json.JsonElement je)
            {
                if (je.ValueKind == System.Text.Json.JsonValueKind.False) return false;
                if (je.ValueKind == System.Text.Json.JsonValueKind.Number) return je.GetDouble() != 0;
                if (je.ValueKind == System.Text.Json.JsonValueKind.String)
                { var str = je.GetString(); return !(str == "0" || str == "0.0" || str == ""); }
                return je.ValueKind != System.Text.Json.JsonValueKind.Null;
            }
            return val != null && !(val is double d && d == 0);
        }

        // ===== Parameter helpers =====
        int P(TunnelCommand c, int i) => i < c.Parameters.Length && int.TryParse(c.Parameters[i], out var v) ? v : 0;
        string S(TunnelCommand c, int i) => i < c.Parameters.Length ? c.Parameters[i] : "";
        // 数组按「实际存在的字段」定长，不补零：原生 0x10073B40 是逐段 vector::at()，
        // 段数不够就抛异常，而不是把缺的当 0 写进物品。
        static int[] YS(TunnelCommand c, int s)
        {
            var n = Math.Max(0, Math.Min(17, c.Parameters.Length - s));
            var r = new int[n];
            for (int i = 0; i < n; i++) int.TryParse(c.Parameters[s + i], out r[i]);
            return r;
        }
        static string PetAttrType(int flag) => flag switch
        {
            1 => "倍功", 2 => "暴击", 3 => "切割", 4 => "连击", 5 => "连击削弱", _ => ""
        };

        // ===== Main dispatch =====
        public int ExecuteCommand(TunnelCommand cmd, string apiName = "GetBagItemCount")
        {
            TotalCommands++;
            try
            {
                YanshenApi.EnsureDirectCallReady(_pm, apiName);
                using var directCall = YanshenApi.BeginStrictDirectCall(apiName);

                // 两个派发器都先做「至少 2 段」检查，早于取操作码、早于开关门。
                //   集成函数 sub_100761A0: 0x10076254 call 0x100184A0 (vector::size) /
                //     0x10076259 `83 F8 02` cmp eax,2 / 0x1007625C `73 45` jae，不足则
                //     0x1007625E `C7 45 E4 88 FC FF FF` 把结果槽写成 -888 直接返回。
                //   爱心分割 sub_1005DBA0: 0x1005DC79 `83 F8 02` / 0x1005DC7C `73 35` jae，
                //     不足则 0x1005DC7E `C7 45 AC 88 FC FF FF`。
                if ((cmd.Format == TunnelFormat.NumericId
                        || cmd.Format == TunnelFormat.CaretSeparated)
                    && cmd.TokenCount < 2)
                    return SentinelShort;

                // 操作码越界。集成函数 0x100766E7 `83 E8 01` sub eax,1 /
                // 0x100766F0 `83 BD AC FE FF FF 28` cmp [ebp-0x154],0x28 /
                // 0x100766F7 `0F 87 F5 12 00 00` ja 0x100779F2 ->
                // `C7 45 E4 F7 FC FF FF` = -777。爱心分割同理：0x1005DD0A
                // `83 FF 25` cmp edi,0x25 / `ja 0x1005E361` -> `BE F7 FC FF FF` = -777。
                if (cmd.Format == TunnelFormat.CaretSeparated
                    && (cmd.CommandId < 1 || cmd.CommandId > 38))
                    return SentinelBadOpcode;
                if (cmd.Format == TunnelFormat.NumericId
                    && (cmd.CommandId < 1 || cmd.CommandId > 41))
                    return SentinelBadOpcode;

                EnsureCommandEnabled(cmd, apiName);

                // Route by format
                if (cmd.Format == TunnelFormat.CaretSeparated)
                    return ExecuteCaret(cmd);

                // 门过了才轮到实现体自己的元数检查（原生门在臂上、检查在实现体首部）。
                if (_nativeArity.TryGetValue(cmd.CommandId, out var arity)
                    && cmd.Parameters.Length < arity.MinParams)
                    return arity.Sentinel;

                return cmd.CommandId switch
                {
                    1 => _api.SuperDamage14(P(cmd,0),P(cmd,1),P(cmd,2),P(cmd,3),P(cmd,4),P(cmd,5),P(cmd,6),P(cmd,7),P(cmd,8),P(cmd,9),P(cmd,10),S(cmd,11)),
                    2 => _api.Paralysis(P(cmd,0),P(cmd,1),P(cmd,2),P(cmd,3),P(cmd,4),P(cmd,5),P(cmd,6)!=0),
                    3 => ExecuteCustomDamage(cmd),
                    4 => cmd.Parameters.Length >= 9
                        ? _api.PushEnemy2(P(cmd,0),P(cmd,1),P(cmd,2),P(cmd,3),P(cmd,4),P(cmd,5),P(cmd,6),P(cmd,7),P(cmd,8))
                        : _api.PushEnemy(P(cmd,0),P(cmd,1),P(cmd,2),P(cmd,3),P(cmd,4),P(cmd,5),P(cmd,6),P(cmd,7)),
                    5 => cmd.Parameters.Length >= 10
                        ? _api.PoisonEffect(P(cmd,0),P(cmd,1),P(cmd,2),P(cmd,3),P(cmd,4),P(cmd,5),P(cmd,6),P(cmd,7),P(cmd,8),P(cmd,9))
                        : _api.Poison(P(cmd,0),P(cmd,1),P(cmd,2),P(cmd,3),P(cmd,4),P(cmd,5),P(cmd,6),P(cmd,7),P(cmd,8)),
                    7 => _api.DropItem(P(cmd,0),P(cmd,1),S(cmd,2)),
                    8 => _api.LifeSteal(P(cmd,0),P(cmd,1)),
                    // ys_DingShen 也编码成 9 号，但它只发 3 段（AllFuc.pas:513
                    // '!!!!集成函数,9,'+shijian+'$'），过不了 _nativeArity[9] 的 8 参下限，
                    // 上面就已经返回 -888 —— 与原生一致，这里够不到 RootTarget。
                    // 能到这儿的只有 ys_TuiTui（8 参）与 ys_TuiTui2（9 参）。
                    9 => cmd.Parameters.Length >= 9
                        ? _api.PullEnemy2(P(cmd,0),P(cmd,1),P(cmd,2),P(cmd,3),P(cmd,4),P(cmd,5),P(cmd,6),P(cmd,7),P(cmd,8))
                        : _api.PullEnemy(P(cmd,0),P(cmd,1),P(cmd,2),P(cmd,3),P(cmd,4),P(cmd,5),P(cmd,6),P(cmd,7)),
                    10 => _api.SetSkillExp(S(cmd,0),P(cmd,1),P(cmd,2)),
                    11 => string.Equals(S(cmd,0), "MP", StringComparison.OrdinalIgnoreCase)
                        ? _api.AddMaxMp(P(cmd,1)) : _api.AddMaxHp(P(cmd,1)),
                    12 => _api.PlayEffect(P(cmd,0),P(cmd,1),P(cmd,2),P(cmd,3),P(cmd,4)),
                    13 => _api.Healing(P(cmd,0),P(cmd,1),P(cmd,2),P(cmd,3),P(cmd,4),P(cmd,5),P(cmd,6),P(cmd,7)),
                    14 => cmd.Parameters.Length >= 10
                        ? _api.AddTempAttrPro(P(cmd,0),P(cmd,1),P(cmd,2),P(cmd,3),P(cmd,4),P(cmd,5),P(cmd,6),P(cmd,7),P(cmd,8),P(cmd,9))
                        : cmd.Parameters.Length >= 9
                            ? _api.AddTempAttr(P(cmd,0),P(cmd,1),P(cmd,2),P(cmd,3),P(cmd,4),P(cmd,5),P(cmd,6),P(cmd,7),P(cmd,8))
                            : _api.SubTempAttr(P(cmd,0),P(cmd,1),P(cmd,2),P(cmd,3),P(cmd,4),P(cmd,5),P(cmd,6),P(cmd,7)),
                    15 => _api.EquipDura(P(cmd,0),P(cmd,1),P(cmd,2)),
                    16 => _api.SendDirectMessage(P(cmd,0),P(cmd,1),P(cmd,2),P(cmd,3),P(cmd,4),S(cmd,5)),
                    17 => _api.SetEquipElement(P(cmd,0),P(cmd,1),P(cmd,2)),
                    18 => _api.GetEquipElement(P(cmd,0),P(cmd,1),P(cmd,2)),
                    19 => _api.AutoPickup(P(cmd,0),P(cmd,1),P(cmd,2),P(cmd,3)),
                    20 => _api.CheckMapMonByName(S(cmd,0),S(cmd,1)),
                    21 => _api.CheckItemBind(S(cmd,0))?1:0,
                    22 => _api.SendGroundMessage(P(cmd,0),P(cmd,1),P(cmd,2),P(cmd,3),P(cmd,4),P(cmd,5),S(cmd,6)),
                    23 => _api.SetPetAttr(S(cmd,11),P(cmd,0),P(cmd,1),P(cmd,2),P(cmd,3),P(cmd,4),P(cmd,5),P(cmd,6),P(cmd,7),P(cmd,8),P(cmd,9),P(cmd,10)),
                    24 => _api.NpcGiveItemYs(P(cmd,0),YS(cmd,1)),
                    25 => _api.SetLoopTimer(P(cmd,0),S(cmd,1)),
                    26 => _api.BounceSkill(P(cmd,0),P(cmd,1),P(cmd,2),P(cmd,3),P(cmd,4),P(cmd,5),P(cmd,6),P(cmd,7),P(cmd,8),P(cmd,9)),
                    27 => _api.VacuumMonstersEx(P(cmd,0),P(cmd,1),P(cmd,2)),
                    28 => _api.HeroCastSkill(P(cmd,0),P(cmd,1)),
                    29 => _api.GiveExp(P(cmd,0)),
                    30 => _api.GivePetSkill(P(cmd,0),P(cmd,1),P(cmd,2),P(cmd,3),S(cmd,4)),
                    31 => _api.GivePetSpecialAttr(P(cmd,1),P(cmd,2),PetAttrType(P(cmd,0)),S(cmd,3)),
                    32 => _api.GetItemExtreme(P(cmd,0),P(cmd,1),P(cmd,2)),
                    33 => _api.BindUnbindItem(P(cmd,0),P(cmd,1)),
                    34 => _api.HolyDamage(P(cmd,0),P(cmd,1),P(cmd,2),P(cmd,3),P(cmd,4),P(cmd,5),P(cmd,6),P(cmd,7),P(cmd,8),P(cmd,9)),
                    35 => _api.PetFollowAttack(P(cmd,0)),
                    36 => _api.GetBagWeight(P(cmd,0)),
                    37 => _api.CustomFireWall(P(cmd,0),P(cmd,1),P(cmd,2),P(cmd,3),P(cmd,4),P(cmd,5),P(cmd,6)),
                    38 => P(cmd,0) == 1 ? _api.GetGroupMemberRoleId(P(cmd,1)) : _api.GetGroupMemberCount(),
                    39 => _api.DecExp(P(cmd,0),P(cmd,1),P(cmd,2)),
                    40 => P(cmd,0) == 1
                        ? _api.GetSkillDmgReduction(S(cmd,2),P(cmd,1))
                        : _api.SetSkillDmgReduction(S(cmd,3),P(cmd,1),P(cmd,2)),
                    41 => _api.KickPlayer(),
                    _ => ExecuteChinese(cmd)
                };
            }
            catch (YanshenApiUnavailableException) { TotalErrors++; throw; }
            catch { TotalErrors++; return -1; }
        }

        int ExecuteCustomDamage(TunnelCommand cmd)
        {
            return cmd.Parameters.Length switch
            {
                >= 15 => _api.CustomDamageDelay(P(cmd,0),P(cmd,1),P(cmd,2),P(cmd,3),P(cmd,4),P(cmd,5),P(cmd,6),P(cmd,7),P(cmd,8),P(cmd,9),P(cmd,10),P(cmd,11),P(cmd,12),P(cmd,13),P(cmd,14)),
                >= 13 => _api.CustomDamageSuper(P(cmd,0),P(cmd,1),P(cmd,2),P(cmd,3),P(cmd,4),P(cmd,5),P(cmd,6),P(cmd,7),P(cmd,8),P(cmd,9),P(cmd,10),P(cmd,11),P(cmd,12)),
                >= 11 => _api.CustomDamageUndead(P(cmd,0),P(cmd,1),P(cmd,2),P(cmd,3),P(cmd,4),P(cmd,5),P(cmd,6),P(cmd,7),P(cmd,8),P(cmd,9),P(cmd,10)),
                >= 10 => _api.CustomDamageEffect(P(cmd,0),P(cmd,1),P(cmd,2),P(cmd,3),P(cmd,4),P(cmd,5),P(cmd,6),P(cmd,7),P(cmd,8),P(cmd,9)),
                >= 9 => _api.CustomDamage2(P(cmd,0),P(cmd,1),P(cmd,2),P(cmd,3),P(cmd,4),P(cmd,5),P(cmd,6),P(cmd,7),P(cmd,8)),
                _ => _api.CustomDamage(P(cmd,0),P(cmd,1),P(cmd,2),P(cmd,3),P(cmd,4),P(cmd,5),P(cmd,6),P(cmd,7))
            };
        }

        /// <summary>
        /// Execute caret-separated commands (^1^ ~ ^37^).
        /// Format: !!!!^commandID^param1^param2^...$
        /// Reversed from yanshen2.0.7.dll Pascal wrappers.
        /// </summary>
        int ExecuteCaret(TunnelCommand cmd)
        {
            return cmd.CommandId switch
            {
                // ^1^ — ys_SqlDbInsert() — 执行SQL语句 (INSERT/UPDATE/DELETE/SELECT)
                1 => _api.SqlDbInsert(S(cmd,0), P(cmd,1) != 0),

                // ^2^ — ys_ChgBigBag() — 更换大背包 (name, newName)
                2 => _api.ChangeBigBag(S(cmd,0), S(cmd,1)),

                // ^3^ — ys_SendDBMsg() — 向DBServer发送消息 (id, sql)
                3 => _api.SendDbMsg(P(cmd,0), S(cmd,1)),

                // ^10^ — ys_Change_ly() — 修改装备描述/来源 (ClientItemID, pname, desc1, desc2)
                10 => _api.ModifyItemDesc(P(cmd,0), S(cmd,1), S(cmd,2), S(cmd,3)),

                // ^13^ — Ys_GetItemid() — 通过ClientItemID获取Itemid
                13 => _api.GetItemIdByClientId(P(cmd,0)),

                // ^20^ — Ys_GetClientItemIDByItemid() — 通过Itemid获取ClientItemID
                20 => _api.GetClientItemIdByItemId(P(cmd,0)),

                // ^29^ — Ys_UpDataBody() — 更新身体装备数据到客户端 (pid)
                29 => _api.UpdateBodyEquip(P(cmd,0)),

                // ^30^ — Ys_RepairInBag() — 按stdmode修理背包物品 (stdmode, isHero)
                30 => _api.RepairBagByStdMode(P(cmd,0), P(cmd,1)),

                // ^31^ — Ys_Getshuxing() — 获取角色/怪物属性 (roleid, types)
                31 => _api.GetCreatureAttr(P(cmd,0), P(cmd,1)),

                // ^32^ — Ys_KillBBbyName() — 按名字杀死宝宝 (name)
                32 => _api.KillPetByName(S(cmd,0)),

                // ^33^ — Ys_DropItembyId() — 按身体部位爆装备 (id)
                33 => _api.DropEquipByPos(P(cmd,0)),

                // ^34^ — Ys_DropItembyName() — 按装备名字爆装备 (name)
                34 => _api.DropEquipByName(S(cmd,0)),

                // ^35^ — Ys_GetItemJp() — 获取装备极品值 (types, id, jid)
                35 => _api.GetItemExtreme(P(cmd,0), P(cmd,1), P(cmd,2)),

                // ^36^ — Ys_SetItemJp() — 设置装备极品值 (types, id, jid, val)
                36 => _api.SetItemExtreme(P(cmd,0), P(cmd,1), P(cmd,2), P(cmd,3)),

                // ^37^ — Ys_GetsxByName() — 按宝宝名字获取属性 (name, types)
                37 => _api.GetPetAttrByName(S(cmd,0), P(cmd,1)),

                // ^38^ — Ys_GetItemDBData() — 查物品数据库字段 (itemid, pid)
                38 => _api.GetItemDbData(P(cmd,0), P(cmd,1)),

                _ => 0
            };
        }

        /// <summary>
        /// Execute Chinese-named commands (格式2: !!!!命令名 参数:参数:)
        /// </summary>
        int ExecuteChinese(TunnelCommand cmd)
        {
            switch (cmd.ChineseCommand)
            {
                case "plus伤害": return _api.CustomDamage(P(cmd,0),P(cmd,1),P(cmd,2),P(cmd,4),P(cmd,3),P(cmd,5),P(cmd,6),P(cmd,7));
                // 两条隧道的第一个字段是元素类型、第二个才是装备位，而
                // Set/GetEquipElement 的形参顺序是 (装备位, 元素类型)，所以这里要交换。
                // 给与元素 1005E8DD 把字段0 拿去和 1/0x11 比（类型），1005E8B2 拿字段1
                // 去索引 [[Self+0x4C0]+idx*4+8]（装备位）；获取元素 1005EB9B 把字段1
                // 限制在 0xF 以内（装备位），1005EBA4 把字段0 限制在 1..17（类型）。
                case "给与元素":
                case "给予元素": return _api.SetEquipElement(P(cmd,1),P(cmd,0),P(cmd,2));
                case "获取元素": return _api.GetEquipElement(P(cmd,1),P(cmd,0),P(cmd,2));
                case "定义伤害":
                case "攻击伤害": _api.DirectAttack(P(cmd,0),P(cmd,1)); return 0;
                case "英雄极品": return _api.GetHeroExtreme(P(cmd,0),P(cmd,1));
                case "hq取sj戳":
                case "hq取sj间": return Environment.TickCount;
                case "zd义回收":
                case "zd回收": return _api.AutoRecycle();
                default: return 0;
            }
        }

        /// <summary>
        /// Handle item-give-with-elements: 5种格式全支持
        /// 格式1: "itemName!!!!ys1|ys2|ys3|ys4|ys5|"               (5元素旧格式)
        /// 格式2: "itemName!!!!#ys,ys1,ys2,...,ys17$"              (17元素新格式)
        /// 格式3: "itemName!!!!#ys,ys1..,jp1..jp6$jp2ys"          (17元素+6极品)
        /// 格式4: "itemName!!!!#ys……pname……desc1……desc2$zdyly" (带描述来源)
        /// 格式5: "itemName!!!!#ys,datas$data"                     (批量数据给物品)
        /// </summary>
        public bool HandleGiveWithElements(string itemName, int count, bool bind)
        {
            var idx = itemName?.IndexOf("!!!!") ?? -1;
            if (idx < 0) return false;
            const string apiName = "Give";
            YanshenApi.EnsureDirectCallReady(_pm, apiName);
            using var directCall = YanshenApi.BeginStrictDirectCall(apiName);
            _api.EnsureFeatureEnabled("自定义元素");
            var name = itemName[..idx];
            var payload = itemName[(idx + 4)..];

            // 格式1: 旧版5元素 — ys1|ys2|ys3|ys4|ys5|
            if (payload.Contains('|') && !payload.StartsWith("#"))
            {
                var parts = payload.TrimEnd('|').Split('|');
                if (parts.Length >= 5)
                {
                    var ys = new int[5];
                    for (int i = 0; i < 5 && i < parts.Length; i++)
                        int.TryParse(parts[i], out ys[i]);
                    _api.GiveItem5El(name, ys[0], ys[1], ys[2], ys[3], ys[4]);
                    return true;
                }
            }

            // 格式3: 17元素+6极品 — #ys,ys1..ys17,jp1..jp6$jp2ys
            if (payload.EndsWith("jp2ys") || payload.Contains("$jp2ys"))
            {
                var clean = payload.Replace("$jp2ys", "").Replace("#ys,", "");
                var parts = clean.Split(',');
                if (parts.Length >= 23)
                {
                    var ys = new int[17]; var jp = new int[6];
                    for (int i = 0; i < 17 && i < parts.Length; i++) int.TryParse(parts[i], out ys[i]);
                    for (int i = 0; i < 6 && i + 17 < parts.Length; i++) int.TryParse(parts[i + 17], out jp[i]);
                    _api.GiveItemYS_JP(name, bind ? 1 : 0, ys, jp);
                    return true;
                }
            }

            // 格式4: 带描述来源 — #ys……pname……desc1……desc2$zdyly
            if (payload.EndsWith("zdyly") || payload.Contains("$zdyly"))
            {
                var clean = payload.Replace("$zdyly", "").Replace("#ys……", "");
                var parts = clean.Split(new[] { "……" }, StringSplitOptions.None);
                if (parts.Length >= 3)
                {
                    _api.GiveItemWithDesc(name, parts[0], parts[1], parts.Length > 2 ? parts[2] : "", bind ? 1 : 0);
                    return true;
                }
            }

            // 格式5: 批量数据给物品 — #ys,datas$data
            if (payload.EndsWith("data") || payload.Contains("$data"))
            {
                var clean = payload.Replace("$data", "").Replace("#ys,", "");
                _api.GiveDataItem(name, clean);
                return true;
            }

            // 格式2: 17元素新格式 — #ys,ys1,ys2,...,ys17$
            if (payload.StartsWith("#ys,"))
            {
                var parts = payload.TrimEnd('$').Split(',');
                if (parts.Length >= 18)
                {
                    var ys = new int[18];
                    for (int i = 1; i <= 17 && i < parts.Length; i++)
                        int.TryParse(parts[i], out ys[i]);
                    _api.GiveNewItem(name, bind ? 1 : 0, ys[1..]);
                    return true;
                }
            }

            // Fallback: just give the item
            _n?.GotoLable_GiveItem(_p, name, count);
            return true;
        }
    }
}
