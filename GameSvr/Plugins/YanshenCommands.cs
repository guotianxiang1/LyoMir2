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

        static readonly Dictionary<int, string> _toggles = new()
        {
            [1]="刀刀切割",[2]="麻痹概率",[3]="刀刀切割",
            [4]="野蛮麻痹",[5]="施毒术",[7]="高级回收",
            [8]="攻击吸血",[9]="野蛮麻痹",[10]="火墙设置时间上限",
            [11]="刀刀切割",[12]="自定义伤害",[13]="刀刀切割",
            [14]="刀刀切割",[15]="自定义元素",[17]="自定义元素",
            [16]="眼神特殊函数",[18]="自定义元素",[19]="全屏拾取",[20]="行会显示",
            [21]="屏蔽自动绑定",[22]="眼神特殊函数",[23]="眼神特殊函数",
            [24]="自定义元素",[25]="全局循环函数",[26]="自定义伤害",
            [27]="全屏吸怪",[28]="指定英雄放技能",[29]="刀刀切割",
            [30]="怪物伤害触发技能特效",[31]="怪物伤害触发技能特效",[32]="自定义元素",
            [33]="屏蔽自动绑定",[34]="刀刀切割",[35]="眼神特殊函数",
            [36]="眼神特殊函数",[37]="火墙修改",[38]="眼神特殊函数",
            [39]="眼神特殊函数",[40]="眼神特殊函数",[41]="踢玩家下线",
        };

        // Caret command toggles (^1^ ~ ^37^)
        static readonly Dictionary<int, string> _caretToggles = new()
        {
            [1]="眼神特殊函数",[2]="大背包",[3]="眼神特殊函数",
            [10]="装备来源",[13]="自定义元素",[20]="自定义元素",
            [29]="自定义元素",[30]="高级回收",[31]="行会显示",
            [32]="特殊宝宝",[33]="屏蔽自动绑定",[34]="屏蔽自动绑定",
            [35]="自定义元素",[36]="自定义元素",[37]="特殊宝宝",
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
            switch (cmd.CommandId)
            {
                case 3 when cmd.Parameters.Length == 10:
                    return new[] { "眼神特殊函数", "自定义伤害_plus", "super攻击触发" };
                case 5 when cmd.Parameters.Length >= 10:
                    return new[] { "眼神特殊函数", "super攻击触发" };
                case 9 when cmd.Parameters.Length > 1 && cmd.Parameters.Length < 9:
                    return new[] { "眼神特殊函数", "super攻击触发" };
                case 19:
                    return new[] { "眼神特殊函数", "全屏拾取" };
                case 23:
                    return new[] { "眼神特殊函数", "怪物伤害触发技能特效" };
                case 40:
                    return new[] { "眼神特殊函数", "指定技能id免伤" };
                default:
                    return _toggles.TryGetValue(cmd.CommandId, out var featureName)
                        ? new[] { featureName }
                        : null;
            }
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
        static int[] YS(TunnelCommand c, int s) { var r = new int[17]; for (int i = 0; i < 17 && s + i < c.Parameters.Length; i++) int.TryParse(c.Parameters[s + i], out r[i]); return r; }
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
                EnsureCommandEnabled(cmd, apiName);

                // Route by format
                if (cmd.Format == TunnelFormat.CaretSeparated)
                    return ExecuteCaret(cmd);

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
                    9 => cmd.Parameters.Length == 1
                        ? _api.RootTarget(P(cmd,0))
                        : cmd.Parameters.Length >= 9
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
                case "给与元素":
                case "给予元素": return _api.SetEquipElement(P(cmd,0),P(cmd,1),P(cmd,2));
                case "获取元素": return _api.GetEquipElement(P(cmd,0),P(cmd,1),P(cmd,2));
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
