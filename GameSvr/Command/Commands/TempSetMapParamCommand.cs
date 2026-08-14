using GameSvr.CommandSystem;
using System.Collections.Generic;
using SystemModule;

namespace GameSvr
{
    [GameCommand("TempSetMapParam", "设置地图属性",
        "地图代号 参数", 5)]
    public sealed class TempSetMapParamCommand : BaseCommond
    {
        private const int Success = 1;
        private const int UnsupportedAttribute = 100;
        // 原生 parser A 唯一的非 1/非 0x64 返回：DROPTOMAP 括号参数为空。
        // 0x775124 C7 45 FC F4 FF FF FF  mov dword [ebp-4],-12
        private const int DropToMapMissingArgument = -12;
        private const string NativeHelp =
            "命令格式：@TempSetMapParam 地图名 属性 [1|0] " +
            "1表示增加属性，0表示取消属性";

        [DefaultCommand]
        public void TempSetMapParam(string[] @Params, TPlayObject PlayObject)
        {
            if (@Params == null || @Params.Length < 3 ||
                string.IsNullOrEmpty(@Params[0]) ||
                string.IsNullOrEmpty(@Params[1]) ||
                string.IsNullOrEmpty(@Params[2]))
            {
                PlayObject.SysMsg(NativeHelp, MsgColor.Blue, MsgType.Hint);
                return;
            }

            var mapName = @Params[0];
            var attribute = @Params[1];
            var state = HUtil32.Str_ToInt(@Params[2], 0);
            var environment = M2Share.MapManager.FindMap(mapName);
            if (environment == null)
            {
                PlayObject.SysMsg("没找到地图 " + mapName, MsgColor.Red,
                    MsgType.Hint);
                return;
            }

            var result = ApplyPickupAttribute(environment, attribute, state);
            if (result == Success)
            {
                var operation = state == 1 ? "增加地图属性=" : "取消地图属性=";
                PlayObject.SysMsg(operation + attribute + "，操作成功",
                    MsgColor.Blue, MsgType.Hint);
            }
            else if (result == UnsupportedAttribute)
            {
                PlayObject.SysMsg("该GM命令目前不支持此地图属性=" + attribute,
                    MsgColor.Red, MsgType.Hint);
            }
            else if (state == 0 || state == 1)
            {
                var operation = state == 1 ? "增加地图属性=" : "取消地图属性=";
                PlayObject.SysMsg(operation + attribute + "，操作失败",
                    MsgColor.Red, MsgType.Hint);
            }
        }

        internal static int ApplyPickupAttribute(Envirnoment environment,
            string attribute, int state)
        {
            if (environment?.Flag == null || attribute == null ||
                IsNativeTrimEmpty(attribute) ||
                unchecked((uint)state) >= 2)
                return 0;

            // 上面这道门是原生包装器 sub_774D24：0x774D55 cmp dword [ebp-8],0 / je
            // （Trim 后为空则返 0）、0x774D5D sub eax,2 / jae（state 必须无符号 <2），
            // 通过后才 0x774D68 call sub_774D98 —— 即下面这条 token 分发链。
            // 分发链默认结果 1（0x774DBE mov dword [ebp-4],1），全链未命中则
            // 0x775BBE 覆写 0x64。
            //
            // 下面 8 个臂是 MFLG 残口补齐的同一批已证 token（字面串在两个 token 池
            // 里各一条，parser A/B 都有臂）。parser A 与 parser B 的区别是 A 带
            // on/off 双臂：`cmp edi,1 / jne` 走置位，`test edi,edi / jne` 走清零。
            // 字段本身的消费者一律 **未接线**（fail-closed，见 TMapFlag 各字段文档）：
            // 这里复刻的是 GM 命令自己的可观测行为——原版对这些 token 返 1（"操作
            // 成功"），而不是 0x64（"不支持"）。
            var flag = environment.Flag;
            var enable = state == 1;

            // CHECKQUEST — parser A @0x775161 writes result 0x64, never a field.
            if (HUtil32.CompareLStr(attribute, "CHECKQUEST", "CHECKQUEST".Length))
                return UnsupportedAttribute;
            // PAODIAN / NORIDE / GuildPK — parser B only; GM A rejects @0x775xxx fall-through.
            if (attribute.Equals("PAODIAN", StringComparison.OrdinalIgnoreCase)
                || attribute.Equals("NORIDE", StringComparison.OrdinalIgnoreCase)
                || HUtil32.CompareLStr(attribute, "GuildPK", "GuildPK".Length))
                return UnsupportedAttribute;

            // SAFE(+NOTHROUGH) -> [+0x5C] / [+0x84]; 0x774DC5 mov ecx,4 / 0x774DDF
            if (HUtil32.CompareLStr(attribute, "SAFE", "SAFE".Length))
            {
                flag.boSAFE = enable;
                if (enable)
                {
                    var safeParam = string.Empty;
                    HUtil32.ArrestStringEx(attribute, '(', ')', ref safeParam);
                    flag.boNOTHROUGH = safeParam.Equals("NOTHROUGH",
                        StringComparison.Ordinal);
                }
                else
                {
                    flag.boNOTHROUGH = false;
                }
                return Success;
            }
            if (TryApplyEqualsBool(flag, attribute, enable, "DARK", v => flag.boDarkness = v)) // 0x774E50 [+0x5A]
                return Success;
            if (TryApplyEqualsBool(flag, attribute, enable, "FIGHT", v => flag.boFightZone = v)) // 0x774E7F [+0x5D]
                return Success;
            if (TryApplyEqualsBool(flag, attribute, enable, "FIGHT3", v => flag.boFight3Zone = v)) // 0x774EAE [+0x5E]
                return Success;
            if (TryApplyEqualsBool(flag, attribute, enable, "FREEPK", v => flag.boFREEPK = v)) // 0x774EDD [+0x5F]
                return Success;
            if (TryApplyEqualsBool(flag, attribute, enable, "DAY", v => flag.boDayLight = v)) // 0x774F0C [+0x5B]
                return Success;
            if (TryApplyEqualsBool(flag, attribute, enable, "QUIZ", v => flag.boQUIZ = v)) // 0x774F3B [+0x60]
                return Success;
            if (TryApplyEqualsBool(flag, attribute, enable, "DARE", v => flag.boDARE = v)) // 0x774F6A [+5]
                return Success;
            if (TryApplyEqualsBool(flag, attribute, enable, "MONATTACK", v => flag.boMONATTACK = v)) // 0x774F99 [+0x90]
                return Success;
            if (TryApplySkyToken(flag, attribute, enable)) // OLDSKY/NEWSKY/MULSKY -> [+0x8C]
                return Success;
            // NORECONNECT prefix 8 -> [+0x64]+[+0x9C]; 0x775058
            if (HUtil32.CompareLStr(attribute, "NORECONNECT", "NORECONNECT".Length))
            {
                flag.boNORECONNECT = enable;
                if (!enable)
                    flag.sNoReConnectMap = string.Empty;
                else
                {
                    var map = string.Empty;
                    HUtil32.ArrestStringEx(attribute, '(', ')', ref map);
                    flag.sNoReConnectMap = map;
                }
                return Success;
            }

            // DROPTOMAP(图名) -> [+0x65] + [+0x9C]
            //   0x7750D2 mov ecx,9 / 0x7750D7 mov edx,0x775CFC / 0x7750DE call 0x4C6E94
            //   on : 0x7750EC 置 1 -> 0x775104 call 0x4C6964 取参 -> 0x775112 存 [+0x9C]
            //        0x775117 cmp dword [ebx+0x9C],0 / jne 尾声；空则 0x775124 结果 -12
            //   off: 0x775138 写 0 + 0x775142 call 0x405500 (LStrClr [+0x9C])
            if (HUtil32.CompareLStr(attribute, "DROPTOMAP", "DROPTOMAP".Length))
            {
                flag.boDROPTOMAP = enable;
                if (!enable)
                {
                    flag.sDropToMap = string.Empty;
                    return Success;
                }
                var dropToMap = string.Empty;
                HUtil32.ArrestStringEx(attribute, '(', ')', ref dropToMap);
                flag.sDropToMap = dropToMap;
                return flag.sDropToMap == ""
                    ? DropToMapMissingArgument
                    : Success;
            }
            // MINGJIANG -> [+0x7A]；比较器 0x40BD78 全等
            //   0x7752A5 mov edx,0x775D9C / 0x7752AC call 0x40BD78
            //   on 0x7752BA 置 1 / off 0x7752CB 写 0
            if (attribute.Equals("MINGJIANG", StringComparison.OrdinalIgnoreCase))
            {
                flag.boMINGJIANG = enable;
                return Success;
            }
            // HACKQUEST -> [+0x7B]；0x7752D4 compare / on 0x7752E9 / off 0x7752FA
            if (attribute.Equals("HACKQUEST", StringComparison.OrdinalIgnoreCase))
            {
                flag.boHACKQUEST = enable;
                return Success;
            }
            // NOEXPLORE -> [+0x80]；0x775332 compare / on 0x775347 / off 0x77535B
            if (attribute.Equals("NOEXPLORE", StringComparison.OrdinalIgnoreCase))
            {
                flag.boNOEXPLORE = enable;
                return Success;
            }
            // NOHERO -> [+0x6E]；0x775706 mov ecx,6 / on 0x775720 / off 0x775731
            if (HUtil32.CompareLStr(attribute, "NOHERO", "NOHERO".Length))
            {
                flag.boNOHERO = enable;
                return Success;
            }
            // DREAMCASTLEMAP -> [+0x6F]；0x77573A mov ecx,0xE / on 0x775754 / off 0x775765
            if (HUtil32.CompareLStr(attribute, "DREAMCASTLEMAP",
                    "DREAMCASTLEMAP".Length))
            {
                flag.boDREAMCASTLEMAP = enable;
                return Success;
            }
            // UserNoKill -> [+0x71]，并在两臂上都把 word [+0x74] 清零
            //   0x775933 mov ecx,0xA / on 0x77594D 置 1 + 0x775951 清 word
            //                        / off 0x775964 写 0 + 0x775968 清 word
            if (HUtil32.CompareLStr(attribute, "UserNoKill", "UserNoKill".Length))
            {
                flag.boUserNoKill = enable;
                flag.UserNoKillLevelCap = 0;
                return Success;
            }
            // NEWMJNORMALPRIZE -> [+0x78]；0x7759DA mov ecx,0x10 / on 0x7759F4 / off 0x775A05
            if (HUtil32.CompareLStr(attribute, "NEWMJNORMALPRIZE",
                    "NEWMJNORMALPRIZE".Length))
            {
                flag.boNEWMJNORMALPRIZE = enable;
                return Success;
            }

            // Prefix bool tokens (parser A 0x4C6E94)
            if (TryApplyPrefixBool(flag, attribute, enable, "NEEDHOLE", v => flag.boNEEDHOLE = v)) // [+0x66]
                return Success;
            if (TryApplyPrefixBool(flag, attribute, enable, "NORECALL", v => flag.boNORECALL = v)) // [+0x67]
                return Success;
            if (TryApplyPrefixBool(flag, attribute, enable, "NORANDOMMOVE", v => flag.boNORANDOMMOVE = v)) // [+0x68]
                return Success;
            if (TryApplyPrefixBool(flag, attribute, enable, "NODRUG", v => flag.boNODRUG = v)) // [+0x69]
                return Success;
            if (TryApplyPrefixBool(flag, attribute, enable, "MINE", v => flag.boMINE = v)) // 0x775257 [+0x6A]
                return Success;
            if (TryApplyPrefixBool(flag, attribute, enable, "NOPOSITIONMOVE", v => flag.boNOPOSITIONMOVE = v)) // [+0x6B]
                return Success;
            if (TryApplyPrefixBool(flag, attribute, enable, "NOMAGIC", v => flag.boNOMAGIC = v)) // 0x7758A5 [+0x81]
                return Success;

            // Equals bool tokens
            if (TryApplyEqualsBool(flag, attribute, enable, "BLACKROOM", v => flag.boBLACKROOM = v)) // [+0x7C]
                return Success;
            if (TryApplyEqualsBool(flag, attribute, enable, "RELIVEBACK", v => flag.boRELIVEBACK = v)) // [+0x7D]
                return Success;
            if (TryApplyEqualsBool(flag, attribute, enable, "AUTORELIVE", v => flag.boAUTORELIVE = v)) // [+0x7E]
                return Success;
            if (TryApplyEqualsBool(flag, attribute, enable, "NOEQUIPRELIVE", v => flag.boNOEQUIPRELIVE = v)) // [+0x7F]
                return Success;
            if (TryApplyEqualsBool(flag, attribute, enable, "TRIGGERBOMB", v => flag.boTRIGGERBOMB = v)) // 0x7758DF [+0x83]
                return Success;
            if (TryApplyEqualsBool(flag, attribute, enable, "FOXMAP", v => flag.boFOXMAP = v)) // 0x775919 [+0x70]
                return Success;
            if (TryApplyEqualsBool(flag, attribute, enable, "NoRelive", v => flag.boNoRelive = v)) // [+0x72]
                return Success;
            if (TryApplyEqualsBool(flag, attribute, enable, "ONLYDROPSPEC", v => flag.boONLYDROPSPEC = v)) // [+0x76]
                return Success;
            if (TryApplyEqualsBool(flag, attribute, enable, "LIMITBAGITEMDROP", v => flag.boLIMITBAGITEMDROP = v)) // [+0x77]
                return Success;

            // NOC2C prefix 5 -> [+0x82]; 0x7756F7 dec edi / sete al
            if (HUtil32.CompareLStr(attribute, "NOC2C", "NOC2C".Length))
            {
                flag.boNOC2C = enable;
                return Success;
            }

            // LimitItemMove prefix 13 -> four bytes [+0x67/68/6B/6C]; 0x775A5C
            if (HUtil32.CompareLStr(attribute, "LimitItemMove", "LimitItemMove".Length))
            {
                var on = enable;
                flag.boLIMITITEMMOVE = on;
                flag.boNORECALL = on;
                flag.boNORANDOMMOVE = on;
                flag.boNOPOSITIONMOVE = on;
                return Success;
            }

            // Numeric / string-table tokens with parenthesized params
            if (TryApplyNumericWordParam(flag, attribute, enable, "MapSign", "MapSign".Length,
                    v => flag.MapSign = v)) // 0x775407 [+0x62]
                return Success;
            if (TryApplyNumericDwordParam(flag, attribute, enable, "MAPFIREWALLBURN",
                    "MAPFIREWALLBURN".Length, v => flag.MapFireWallBurnMs = v * 1000)) // 0x7753A4 imul 0x3E8
                return Success;
            if (TryApplyFlyDropItem(flag, attribute, enable)) // 0x775452 [+0xB4]
                return Success;
            if (TryApplyRunFlag(flag, attribute, enable)) // 0x775xxx [+0xB0]
                return Success;
            if (TryApplyNumericWordParam(flag, attribute, enable, "UNIFIEDLEVEL",
                    "UNIFIEDLEVEL".Length, v => flag.UnifiedLevel = v)) // [+0xBC]
                return Success;
            if (TryApplyNumericWordParam(flag, attribute, enable, "LIMITPLAYERLEVEL",
                    "LIMITPLAYERLEVEL".Length, v => flag.LimitPlayerLevel = v)) // [+0xBE]
                return Success;
            if (TryApplyNumericWordParam(flag, attribute, enable, "LIMITHEROLEVEL",
                    "LIMITHEROLEVEL".Length, v => flag.LimitHeroLevel = v)) // [+0xC0]
                return Success;
            if (enable && NativeMapBreakLevelFlagParser.TryApply(flag, attribute))
                return Success;
            if (!enable && HUtil32.CompareLStr(attribute, "BREAKLEVEL", "BREAKLEVEL".Length))
            {
                flag.BreakLevel = 0;
                return Success;
            }
            if (!enable && HUtil32.CompareLStr(attribute, "CRAZYBREAKLEVEL", "CRAZYBREAKLEVEL".Length))
            {
                flag.CrazyBreakLevel = 0;
                return Success;
            }

            // LimitSkill — parser A extracts skill id, no Envir field; GM A has no write arm.
            if (HUtil32.CompareLStr(attribute, "LimitSkill", "LimitSkill".Length))
                return UnsupportedAttribute;

            // 其余未识别 token 落到 0x775BBE 写 0x64。缩窄边界由审计工具
            // NativeTempSetMapParamPickupCheck 用 CHECKQUEST 锁住（SAFE 等已移植）。
            // pickup -> [+0x6D]；与上面臂共用 0x4C6E94，是**前缀**比较不是全等：
            //   0x775A8E mov ecx,6 / 0x775A93 mov edx,0x775FCC ("pickup")
            //   0x775A9A call 0x4C6E94
            // sub_4C6E94(s1,s2,n) 只在 0x4C6ECC/0x4C6ED8 判两串 Length>=n，
            // 没有 Length==n 那道门，随后逐字符过 @UpCase(0x4034D4) 比前 n 个，
            // 所以 "pickupXYZ" 原版命中而 C# 的全等不命中。
            // 安全性：parser A 全链 51 个 token 里没有第二个以 "pickup" 开头的字面串，
            // 改成前缀不会抢走原版路由到别的臂的输入。
            if (!HUtil32.CompareLStr(attribute, "pickup", "pickup".Length))
                return UnsupportedAttribute;

            environment.Flag.boPICKUP = state == 1;
            return Success;
        }

        /// <summary>
        /// 原生包装器 <c>sub_774D24</c> 的空串判定看的是 <b>Trim 之后</b>的串：
        /// <c>0x774D50 call 0x40C140</c>(Trim) 写 <c>[ebp-8]</c>，
        /// <c>0x774D55 cmp dword [ebp-8],0 / je</c> 为空则整条命令返 0。
        /// Delphi <c>Trim</c>(0x40C140) 的边界是 <c>cmp byte [..],0x20 / jbe</c>
        /// （0x40C171 与 0x40C193 两端各一处）——掐掉两端所有 &lt;= 0x20 的字节，
        /// 与 .NET 的 Unicode 空白集并不等价（.NET 不掐 #01..#08，却掐 U+00A0），
        /// 故这里按字节阈值自己判，不用 <c>string.IsNullOrWhiteSpace</c>。
        /// <para>
        /// Trim 的结果**只**用于这道判定：<c>0x774D64 mov edx,esi</c> 传给
        /// <c>sub_774D98</c> 的仍是未 Trim 的原串，回显消息用的也是原串
        /// （caller 0x62996B / 0x629A30 同取 <c>[ebp-0x38]</c>），所以下游一律不 Trim。
        /// </para>
        /// </summary>
        private static bool IsNativeTrimEmpty(string value)
        {
            foreach (var character in value)
            {
                if (character > ' ')
                {
                    return false;
                }
            }

            return true;
        }

        private static bool TryApplyEqualsBool(TMapFlag flag, string attribute, bool enable,
            string token, Action<bool> write)
        {
            if (!attribute.Equals(token, StringComparison.OrdinalIgnoreCase))
                return false;
            write(enable);
            return true;
        }

        private static bool TryApplyPrefixBool(TMapFlag flag, string attribute, bool enable,
            string token, Action<bool> write)
        {
            if (!HUtil32.CompareLStr(attribute, token, token.Length))
                return false;
            write(enable);
            return true;
        }

        private static bool TryApplySkyToken(TMapFlag flag, string attribute, bool enable)
        {
            if (attribute.Equals("OLDSKY", StringComparison.OrdinalIgnoreCase))
            {
                flag.SceneType = enable ? (byte)1 : (byte)0;
                return true;
            }
            if (attribute.Equals("NEWSKY", StringComparison.OrdinalIgnoreCase))
            {
                flag.SceneType = enable ? (byte)2 : (byte)0;
                return true;
            }
            if (attribute.Equals("MULSKY", StringComparison.OrdinalIgnoreCase))
            {
                flag.SceneType = enable ? (byte)3 : (byte)0;
                return true;
            }
            return false;
        }

        private static bool TryApplyNumericWordParam(TMapFlag flag, string attribute,
            bool enable, string token, int prefixLen, Action<ushort> write)
        {
            if (!HUtil32.CompareLStr(attribute, token, prefixLen))
                return false;
            write(enable
                ? unchecked((ushort)HUtil32.Str_ToInt(ExtractParenParam(attribute), 0))
                : (ushort)0);
            return true;
        }

        private static bool TryApplyNumericDwordParam(TMapFlag flag, string attribute,
            bool enable, string token, int prefixLen, Action<int> write)
        {
            if (!HUtil32.CompareLStr(attribute, token, prefixLen))
                return false;
            write(enable ? HUtil32.Str_ToInt(ExtractParenParam(attribute), 0) : 0);
            return true;
        }

        private static bool TryApplyFlyDropItem(TMapFlag flag, string attribute, bool enable)
        {
            if (!HUtil32.CompareLStr(attribute, "FLYDROPITEM", "FLYDROPITEM".Length))
                return false;
            if (!enable)
            {
                flag.FlyDropItemNames = null;
                return true;
            }
            var value = ExtractParenParam(attribute);
            if (value == "")
            {
                flag.FlyDropItemNames = null;
                return true;
            }
            flag.FlyDropItemNames ??= new List<string>();
            flag.FlyDropItemNames.Clear();
            var remaining = value;
            var piece = string.Empty;
            do
            {
                remaining = HUtil32.GetValidStr3(remaining, ref piece, "/");
                if (piece != "")
                    flag.FlyDropItemNames.Add(piece);
            } while (remaining.Length > 0);
            return true;
        }

        private static bool TryApplyRunFlag(TMapFlag flag, string attribute, bool enable)
        {
            if (!HUtil32.CompareLStr(attribute, "RUNFLAG", "RUNFLAG".Length))
                return false;
            if (!enable)
            {
                flag.boRUNFLAG = false;
                return true;
            }
            var value = ExtractParenParam(attribute);
            flag.boRUNFLAG = HUtil32.Str_ToInt(value, 1) != 0;
            return true;
        }

        private static string ExtractParenParam(string attribute)
        {
            var value = string.Empty;
            HUtil32.ArrestStringEx(attribute, '(', ')', ref value);
            return value;
        }
    }
}
