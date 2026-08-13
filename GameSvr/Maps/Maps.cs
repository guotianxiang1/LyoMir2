using SystemModule;
using SystemModule.Common;

namespace GameSvr
{
    public class Maps
    {
        public static int LoadMapInfo()
        {
            var sFlag = string.Empty;
            var s34 = string.Empty;
            var s38 = string.Empty;
            var sMapName = string.Empty;
            var s44 = string.Empty;
            var sMapDesc = string.Empty;
            var s4C = string.Empty;
            var sReConnectMap = string.Empty;
            int n14;
            int n18;
            int n1C;
            int n20;
            int nServerIndex;
            TMapFlag MapFlag = null;
            Merchant QuestNPC;
            HashSet<int> limitSkillIds = null;
            string sMapInfoFile;
            var loadFailCount = 0;
            var result = -1;
            var sFileName = Path.Combine(M2Share.sConfigPath, M2Share.g_Config.sEnvirDir, "MapInfo.txt");
            if (File.Exists(sFileName))
            {
                var LoadList = new StringList();
                LoadList.LoadFromFile(sFileName);
                if (LoadList.Count < 0)
                {
                    return result;
                }
                var count = 0;
                while (true)
                {
                    if (count >= LoadList.Count)
                    {
                        break;
                    }
                    if (HUtil32.CompareLStr("ConnectMapInfo", LoadList[count], "ConnectMapInfo".Length))
                    {
                        sMapInfoFile = HUtil32.GetValidStr3(LoadList[count], ref sFlag, new string[] { " ", "\t" });
                        LoadList.RemoveAt(count);
                        if (sMapInfoFile != "")
                        {
                            LoadMapInfo_LoadSubMapInfo(LoadList, sMapInfoFile);
                        }
                    }
                    count++;
                }
                result = 1;
                
                for (var i = 0; i < LoadList.Count; i++)
                {
                    sFlag = LoadList[i];
                    if (!string.IsNullOrEmpty(sFlag) && sFlag[0] == '[')
                    {
                        sMapName = "";
                        MapFlag = new TMapFlag
                        {
                            boSAFE = false
                        };
                        limitSkillIds = new HashSet<int>();
                        sFlag = HUtil32.ArrestStringEx(sFlag, "[", "]", ref sMapName);
                        sMapDesc = HUtil32.GetValidStrCap(sMapName, ref sMapName, new string[] { " ", ",", "\t" });
                        if (sMapDesc != "" && sMapDesc[0] == '\"')
                        {
                            HUtil32.ArrestStringEx(sMapDesc, "\"", "\"", ref sMapDesc);
                        }
                        s4C = HUtil32.GetValidStr3(sMapDesc, ref sMapDesc, new string[] { " ", ",", "\t" }).Trim();
                        nServerIndex = HUtil32.Str_ToInt(s4C, 0);
                        if (sMapName == "")
                        {
                            continue;
                        }
                        MapFlag.nL = 1;
                        QuestNPC = null;
                        MapFlag.boSAFE = false;
                        MapFlag.nNEEDSETONFlag = -1;
                        MapFlag.nNeedONOFF = -1;
                        MapFlag.nMUSICID = -1;
                        while (true)
                        {
                            if (sFlag == "")
                            {
                                break;
                            }
                            sFlag = HUtil32.GetValidStr3(sFlag, ref s34, new string[] { " ", ",", "\t" });
                            if (s34 == "")
                            {
                                break;
                            }
                            if (TryParseLimitSkill(s34, out var parsedLimitSkillIds))
                            {
                                limitSkillIds.UnionWith(parsedLimitSkillIds);
                                continue;
                            }
                            if (NativeMapBreakLevelFlagParser.TryApply(MapFlag, s34))
                            {
                                continue;
                            }
                            if (TryApplySceneFlag(MapFlag, s34))
                            {
                                continue;
                            }
                            if (s34.Equals("SAFE", StringComparison.OrdinalIgnoreCase))
                            {
                                MapFlag.boSAFE = true;
                                continue;
                            }
                            if (s34.Equals("PICKUP", StringComparison.OrdinalIgnoreCase))
                            {
                                MapFlag.boPICKUP = true;
                                continue;
                            }
                            if (string.Compare(s34, "DARK", StringComparison.OrdinalIgnoreCase) == 0)
                            {
                                MapFlag.boDarkness = true;
                                continue;
                            }
                            if (string.Compare(s34, "FIGHT", StringComparison.OrdinalIgnoreCase) == 0)
                            {
                                MapFlag.boFightZone = true;
                                continue;
                            }
                            if (string.Compare(s34, "FREEPK", StringComparison.OrdinalIgnoreCase) == 0)
                            {
                                MapFlag.boFREEPK = true;
                                continue;
                            }
                            if (string.Compare(s34, "DAY", StringComparison.OrdinalIgnoreCase) == 0)
                            {
                                MapFlag.boDayLight = true;
                                continue;
                            }
                            if (string.Compare(s34, "QUIZ", StringComparison.OrdinalIgnoreCase) == 0)
                            {
                                MapFlag.boQUIZ = true;
                                continue;
                            }
                            if (HUtil32.CompareLStr(s34, "NORECONNECT", "NORECONNECT".Length))
                            {
                                MapFlag.boNORECONNECT = true;
                                HUtil32.ArrestStringEx(s34, '(', ')', ref sReConnectMap);
                                MapFlag.sNoReConnectMap = sReConnectMap;
                                if (MapFlag.sNoReConnectMap == "")
                                {
                                    result = -11;
                                }
                                continue;
                            }
                            if (HUtil32.CompareLStr(s34, "CHECKQUEST", "CHECKQUEST".Length))
                            {
                                HUtil32.ArrestStringEx(s34, '(', ')', ref s38);
                                QuestNPC = LoadMapInfo_LoadMapQuest(s38);
                                continue;
                            }
                            // 以下 14 个 token 全部是凭空发明的，解析臂已移除，
                            // 见本文件下方 §INVENTED 说明：
                            //   NEEDSET_ON NEEDSET_OFF MUSIC EXPRATE
                            //   PKWINLEVEL PKWINEXP PKLOSTLEVEL PKLOSTEXP
                            //   DECHP INCHP DECGAMEGOLD DECGAMEPOINT
                            //   INCGAMEGOLD INCGAMEPOINT
                            // 原版解析器 B 对未识别 token 是静默忽略：
                            //   0x776AD3  83 7D FC 00        cmp dword [ebp-4],0
                            //   0x776AD7  0F 85 62 F5 FF FF  jne 0x77603F   ; 下一轮
                            // 不写任何字段、不报错。
                            if (s34.Equals("NEEDHOLE", StringComparison.OrdinalIgnoreCase))
                            {
                                MapFlag.boNEEDHOLE = true;
                                continue;
                            }
                            if (s34.Equals("NORECALL", StringComparison.OrdinalIgnoreCase))
                            {
                                MapFlag.boNORECALL = true;
                                continue;
                            }
                            // NOGUILDRECALL / NODEARRECALL / NOMASTERRECALL 同样是
                            // 发明的（§INVENTED）。注意生产 MapInfo.txt 确实写了
                            // 它们（22 / 24 / 24 处），但原版对它们静默忽略，所以
                            // 线上真实行为一直是「没有效果」——移除解析臂正是与线上
                            // 对齐，不是改变线上行为。
                            if (s34.Equals("NORANDOMMOVE", StringComparison.OrdinalIgnoreCase))
                            {
                                MapFlag.boNORANDOMMOVE = true;
                                continue;
                            }
                            if (s34.Equals("LimitItemMove", StringComparison.OrdinalIgnoreCase))
                            {
                                MapFlag.boLIMITITEMMOVE = true;
                                MapFlag.boNORECALL = true;
                                MapFlag.boNORANDOMMOVE = true;
                                MapFlag.boNOPOSITIONMOVE = true;
                                continue;
                            }
                            if (s34.Equals("BLACKROOM", StringComparison.OrdinalIgnoreCase))
                            {
                                MapFlag.boBLACKROOM = true;
                                continue;
                            }
                            if (s34.Equals("FOXMAP", StringComparison.OrdinalIgnoreCase))
                            {
                                MapFlag.boFOXMAP = true;
                                continue;
                            }
                            if (s34.Equals("NODRUG", StringComparison.OrdinalIgnoreCase))
                            {
                                MapFlag.boNODRUG = true;
                                continue;
                            }
                            // MINE-12: 原版 MINE 走的是**长度 4 的前缀比较**，不是全等。
                            //   0x7763C9  B9 04 00 00 00     mov ecx,4
                            //   0x7763CE  BA A4 6C 77 00     mov edx,0x776CA4   ; "MINE"
                            //   0x7763D6  E8 B9 0A D5 FF     call 0x4C6E94
                            //   0x7763DF  C6 43 6A 01        mov byte [ebx+0x6A],1
                            // 比较器 0x4C6E94 只比前 ecx 个字符，且要求
                            // ecx <= Len(两侧)，逐字符过 UpCase：
                            //   0x4C6EC9  Length(a); cmp esi,eax; jg  fail   ; N <= Len(a)
                            //   0x4C6ED5  Length(b); cmp esi,eax; jg  fail   ; N <= Len(b)
                            //   0x4C6EE1  B3 01              mov bl,1
                            //   0x4C6EEF  8A 44 38 FF        mov al,[a+edi-1]
                            //   0x4C6EF3  E8 DC C5 F3 FF     call 0x4034D4    ; UpCase
                            //   0x4C6EFC  8A 44 38 FF        mov al,[b+edi-1]
                            //   0x4C6F00  E8 CF C5 F3 FF     call 0x4034D4
                            //   0x4C6F06  3A D0 / 74 04      cmp dl,al / je 继续
                            //   0x4C6F0A  33 DB              xor ebx,ebx      ; 失配
                            //   0x4C6F0F  4E / 75 DA         dec esi / jne 循环
                            // 而 0x4034D4 是 UpCase（cmp al,'a' / jb / cmp al,'z' /
                            // ja / sub al,0x20），所以**大小写不敏感**——台账
                            // MINE-12 写的「CASE-SENSITIVE」与字节矛盾，C# 原来的
                            // OrdinalIgnoreCase 在这一点上是对的，错的是全等。
                            // HUtil32.CompareLStr 与 0x4C6E94 逐条同构。
                            // 直接后果：地图里写 MINE2 在原版命中 MINE、置 +0x6A=1。
                            if (HUtil32.CompareLStr(s34, "MINE", "MINE".Length))
                            {
                                MapFlag.boMINE = true;
                                continue;
                            }
                            // MINE-01: MINE2 是凭空发明的，已移除。全镜像三种编码
                            // 各 0 命中（Delphi AnsiString 记录 / 裸 ASCII 大小写
                            // 不敏感 / UTF-16LE 大小写不敏感；同一扫描器对 MINE、
                            // pickup、NORECALL 等真 token 各命中 2 条 Delphi 记录，
                            // 即两个 token 池各一条）。配置里写 MINE2 在原版会被
                            // 上面那条长度 4 的前缀比较命中成 MINE。不要重新接线。
                            // NOTHROWITEM / NODROPITEM 也是发明的（§INVENTED）。
                            if (s34.Equals("NOPOSITIONMOVE", StringComparison.OrdinalIgnoreCase))
                            {
                                MapFlag.boNOPOSITIONMOVE = true;
                                continue;
                            }
                            // NOHORSE 是发明的（§INVENTED）；真 token 是下面的
                            // NORIDE（池 B 独有，0x776E8C，写 [flag+0x85]）。
                            if (s34.Equals("NORIDE", StringComparison.OrdinalIgnoreCase))
                            {
                                MapFlag.boNORIDE = true;
                                continue;
                            }
                            // NOCHAT / KILLFUNC 也是发明的（§INVENTED）。生产
                            // MapInfo.txt 里有 2 处 KILLFUNC(1)，原版静默忽略。
                            if (s34.Equals("NOC2C", StringComparison.OrdinalIgnoreCase))
                            {
                                MapFlag.boNOC2C = true;
                                continue;
                            }
                            // RUNFLAG(n) -> [flag+0xB0], over-encumbered run
                            // exemption. Native parses the parenthesised argument
                            // with StrToIntDef @0x77558A and stores 1 only when it
                            // is non-zero (@0x77559F); a zero argument @0x775593 and
                            // a bare RUNFLAG with no argument @0x7755B3 both store 0.
                            // Str_ToInt on an empty capture yields 0, so the bare
                            // form falls out correctly without a separate branch.
                            if (HUtil32.CompareLStr(s34, "RUNFLAG", "RUNFLAG".Length))
                            {
                                HUtil32.ArrestStringEx(s34, '(', ')', ref s38);
                                MapFlag.boRUNFLAG = HUtil32.Str_ToInt(s38, 0) != 0;
                                continue;
                            }
                            // §INVENTED — 本解析器移除的 26 个凭空发明 token
                            // -----------------------------------------------
                            // 判定方法：对每个 token 做全镜像三编码扫描
                            //   (1) Delphi AnsiString 记录 FF FF FF FF | len32 |
                            //       chars，chars 大小写不敏感比较
                            //   (2) 裸 ASCII 大小写不敏感
                            //   (3) UTF-16LE 大小写不敏感
                            // 三者皆 0 才判发明。同一扫描器对真 token（MINE /
                            // pickup / NORECALL / LimitItemMove / LIMITHEROLEVEL
                            // / UNIFIEDLEVEL / LIMITPLAYERLEVEL / MapSign /
                            // MAPFIREWALLBURN / FLYDROPITEM）各命中恰好 2 条
                            // Delphi 记录（两个 token 池各一条），说明 0 是真的
                            // 缺席而不是扫描器坏了。
                            //
                            // 名单（26 个）：MINE2 NOHUMNOMON MUSIC EXPRATE
                            //   PKWINLEVEL PKWINEXP PKLOSTLEVEL PKLOSTEXP
                            //   DECHP INCHP DECGAMEGOLD DECGAMEPOINT
                            //   INCGAMEGOLD INCGAMEPOINT RUNHUMAN RUNMON
                            //   NOGUILDRECALL NODEARRECALL NOMASTERRECALL
                            //   NOTHROWITEM NODROPITEM NOHORSE NOCHAT
                            //   KILLFUNC NEEDSET_ON NEEDSET_OFF
                            //
                            // 注意 PICKUP **不是**发明：原生 token 是小写
                            // pickup（0x775FCC / 0x776F44），大小写不敏感能匹配。
                            // EXPRATE 的 3 处裸 ASCII 命中（0x6AD5F6 /
                            // 0x72C759 / 0x7D0618）全是 MultiTempExpRate 与
                            // MonExpRate 的子串，不是独立 token。
                            //
                            // 为什么不是无害冗余：原版解析器 B 对未识别 token
                            // 静默忽略（0x776AD3 cmp dword[ebp-4],0 / 0x776AD7
                            // jne 0x77603F 直接进下一轮），既不写字段也不报错。
                            // 所以 DECHP(10/5) 在原版毫无效果，在 C# 却真的开启
                            // 掉血——这是会改变玩法的行为分歧。
                            //
                            // TMapFlag 上对应的字段暂时保留（沿用 NOHUMNOMON 的
                            // 既有先例）：解析臂一去，它们永远停在默认值，消费点
                            // 变成不可达死代码，可观测行为已与原版一致。字段与
                            // 消费点的物理删除留给能编译的集成方，因为
                            // MovementCollisionCheck / DynRoomFlagMapperCheck /
                            // NativeCastleWarFiveCheck 三个审计工具直接引用
                            // boRUNHUMAN / boRUNMON / boNOHORSE / boNOHUMNOMON，
                            // 删字段会让它们编译不过（BUILD-ERROR 是盲区，比
                            // FAIL 更糟）。**不要重新接线。**
                            if (!string.IsNullOrEmpty(s34) && s34[0] == 'L')
                            {
                                MapFlag.nL = HUtil32.Str_ToInt(s34.Substring(1, s34.Length - 1), 1);
                            }
                        }
                        var loadedMap = M2Share.MapManager.AddMapInfo(sMapName, sMapDesc, nServerIndex, MapFlag, QuestNPC);
                        if (loadedMap == null)
                        {
                            loadFailCount++;
                        }
                        else
                        {
                            // Native keeps ONE byte [flag+0xB0] that both the
                            // MapInfo RUNFLAG token and the NORUN/CANRUN parser
                            // write. Envirnoment.NativeCanRunWhileOverweight is
                            // the C# read authority (the run ladder consults it),
                            // so fold the parsed token into it rather than adding
                            // a second authority for the same native field.
                            loadedMap.NativeCanRunWhileOverweight = MapFlag.boRUNFLAG;
                            if (limitSkillIds != null)
                            {
                                loadedMap.LimitSkillIds.UnionWith(limitSkillIds);
                            }
                        }
                    }
                }
                
                for (var i = 0; i < LoadList.Count; i++)
                {
                    sFlag = LoadList[i];
                    if (!string.IsNullOrEmpty(sFlag) && sFlag[0] != '[' && sFlag[0] != ';')
                    {
                        sFlag = HUtil32.GetValidStr3(sFlag, ref s34, new string[] { " ", ",", "\t" });
                        sMapName = s34;
                        sFlag = HUtil32.GetValidStr3(sFlag, ref s34, new string[] { " ", ",", "\t" });
                        n14 = HUtil32.Str_ToInt(s34, 0);
                        sFlag = HUtil32.GetValidStr3(sFlag, ref s34, new string[] { " ", ",", "\t" });
                        n18 = HUtil32.Str_ToInt(s34, 0);
                        sFlag = HUtil32.GetValidStr3(sFlag, ref s34, new string[] { " ", ",", "-", ">", "\t" });
                        s44 = s34;
                        sFlag = HUtil32.GetValidStr3(sFlag, ref s34, new string[] { " ", ",", "\t" });
                        n1C = HUtil32.Str_ToInt(s34, 0);
                        sFlag = HUtil32.GetValidStr3(sFlag, ref s34, new string[] { " ", ",", ";", "\t" });
                        n20 = HUtil32.Str_ToInt(s34, 0);
                        M2Share.MapManager.AddMapRoute(sMapName, n14, n18, s44, n1C, n20);
                    }
                }
                if (loadFailCount > 0)
                {
                    M2Share.MainOutMessage($"地图配置有 {loadFailCount} 个地图未加载，请检查对应 .map 文件。");
                }
                if (M2Share.MapManager.Maps.Count <= 0)
                {
                    result = -10;
                }
                LoadList = null;
            }
            return result;
        }

        internal static bool TryParseLimitSkill(string token, out IReadOnlyCollection<int> skillIds)
        {
            skillIds = Array.Empty<int>();
            if (string.IsNullOrWhiteSpace(token) ||
                !token.StartsWith("LimitSkill", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            var suffix = token.Substring("LimitSkill".Length).Trim();
            if (suffix.Length == 0)
            {
                skillIds = new[] { 0 };
                return true;
            }

            if (suffix[0] != '(' || suffix[^1] != ')')
            {
                return false;
            }

            var body = suffix.Substring(1, suffix.Length - 2).Trim();
            if (body.Length == 0)
            {
                skillIds = new[] { 0 };
                return true;
            }

            var parsed = new List<int>();
            foreach (var part in body.Split('|', StringSplitOptions.RemoveEmptyEntries))
            {
                if (!int.TryParse(part.Trim(), out var id))
                {
                    return false;
                }
                parsed.Add(id);
            }
            if (parsed.Count == 0)
            {
                parsed.Add(0);
            }
            skillIds = parsed;
            return true;
        }

        internal static bool TryApplySceneFlag(TMapFlag mapFlag, string token)
        {
            if (mapFlag == null || string.IsNullOrEmpty(token)) return false;

            if (token.Equals("FIGHT3", StringComparison.OrdinalIgnoreCase))
            {
                mapFlag.boFight3Zone = true;
                return true;
            }
            if (token.Equals("OLDSKY", StringComparison.OrdinalIgnoreCase))
            {
                mapFlag.SceneType = 1;
                return true;
            }
            if (token.Equals("NEWSKY", StringComparison.OrdinalIgnoreCase))
            {
                mapFlag.SceneType = 2;
                return true;
            }
            if (token.Equals("MULSKY", StringComparison.OrdinalIgnoreCase))
            {
                mapFlag.SceneType = 3;
                return true;
            }
            // 战神 sub_774D98 @0x775AC2-0x775AF1: `mov ecx,0xC; mov edx,0x775FDC` ("ONLYDROPSPEC",
            // 12 chars) / sub_4C6E94 compare / `mov byte [ebx+0x76],1`.  Read by the
            // death-drop policy sub_741368 @0x741417 + @0x74143E.
            if (token.Equals("ONLYDROPSPEC", StringComparison.OrdinalIgnoreCase))
            {
                mapFlag.boONLYDROPSPEC = true;
                return true;
            }
            // 战神 sub_774D98 @0x775AF6-0x775B25: `mov ecx,0x10; mov edx,0x775FF4`
            // ("LIMITBAGITEMDROP", 16 chars) / `mov byte [ebx+0x77],1`.  Read by
            // sub_741368 @0x741426 + @0x74144E.
            if (token.Equals("LIMITBAGITEMDROP", StringComparison.OrdinalIgnoreCase))
            {
                mapFlag.boLIMITBAGITEMDROP = true;
                return true;
            }
            // The four revive map gates, all read by 战神 sub_7436F8.  Parser sub_774D98
            // token compare -> `mov byte [ebx+d],1`:
            //   NoRelive      @0x775A1A -> 0x775A28 [ebx+0x72]
            //   RELIVEBACK    @0x77552A -> 0x775538 [ebx+0x7D]
            //   AUTORELIVE    @0x775686 -> 0x775694 [ebx+0x7E]
            //   NOEQUIPRELIVE @0x7756BA -> 0x7756C8 [ebx+0x7F]
            if (token.Equals("NoRelive", StringComparison.OrdinalIgnoreCase))
            {
                mapFlag.boNoRelive = true;
                return true;
            }
            if (token.Equals("RELIVEBACK", StringComparison.OrdinalIgnoreCase))
            {
                mapFlag.boRELIVEBACK = true;
                return true;
            }
            if (token.Equals("AUTORELIVE", StringComparison.OrdinalIgnoreCase))
            {
                mapFlag.boAUTORELIVE = true;
                return true;
            }
            if (token.Equals("NOEQUIPRELIVE", StringComparison.OrdinalIgnoreCase))
            {
                mapFlag.boNOEQUIPRELIVE = true;
                return true;
            }
            // MFLG-12/MFLG-24: Additional map flags from 战神 token census
            if (token.Equals("NOTHROUGH", StringComparison.OrdinalIgnoreCase))
            {
                mapFlag.boNOTHROUGH = true;
                return true;
            }
            if (token.Equals("DARE", StringComparison.OrdinalIgnoreCase))
            {
                mapFlag.boDARE = true;
                return true;
            }
            if (token.Equals("MONATTACK", StringComparison.OrdinalIgnoreCase))
            {
                mapFlag.boMONATTACK = true;
                return true;
            }
            if (token.Equals("LIMITHEROLEVEL", StringComparison.OrdinalIgnoreCase))
            {
                mapFlag.boLIMITHEROLEVEL = true;
                return true;
            }
            // DORMANT gate: 0 consumers in 战神 binary (image-wide scan). Parser recognizes
            // the token to match native domain, but no runtime code reads this field.
            if (token.Equals("NOMAGIC", StringComparison.OrdinalIgnoreCase))
            {
                mapFlag.boNOMAGIC = true;
                return true;
            }
            if (token.Equals("TRIGGERBOMB", StringComparison.OrdinalIgnoreCase))
            {
                mapFlag.boTRIGGERBOMB = true;
                return true;
            }
            return false;
        }

        public static int LoadMinMap()
        {
            var sMapNO = string.Empty;
            var sMapIdx = string.Empty;
            var result = 0;
            var sFileName = Path.Combine(M2Share.sConfigPath, M2Share.g_Config.sEnvirDir, "MiniMap.txt");
            if (File.Exists(sFileName))
            {
                M2Share.MiniMapList.Clear();
                var tMapList = new StringList();
                tMapList.LoadFromFile(sFileName);
                for (var i = 0; i < tMapList.Count; i++)
                {
                    var tStr = tMapList[i];
                    if (tStr != "" && tStr[0] != ';')
                    {
                        tStr = HUtil32.GetValidStr3(tStr, ref sMapNO, new string[] { " ", "\t" });
                        tStr = HUtil32.GetValidStr3(tStr, ref sMapIdx, new string[] { " ", "\t" });
                        var nIdx = HUtil32.Str_ToInt(sMapIdx, 0);
                        if (nIdx > 0)
                        {
                            if (M2Share.MiniMapList.ContainsKey(sMapNO))
                            {
                                M2Share.ErrorMessage($"重复小地图配置信息[{sMapNO}]");
                                continue;
                            }
                            M2Share.MiniMapList.Add(sMapNO, nIdx);
                        }
                    }
                }
            }
            return result;
        }

        private static Merchant LoadMapInfo_LoadMapQuest(string sName)
        {
            var questNPC = new Merchant
            {
                m_sMapName = "0",
                m_nCurrX = 0,
                m_nCurrY = 0,
                m_sCharName = sName,
                m_nFlag = 0,
                m_wAppr = 0,
                m_boIsHide = true,
                m_boIsQuest = false
            };
            M2Share.UserEngine.TryAddQuestNpcExact(questNPC);
            return questNPC;
        }

        private static void LoadMapInfo_LoadSubMapInfo(StringList LoadList, string sFileName)
        {
            string sFilePatchName;
            StringList LoadMapList;
            string sFileDir = Path.Combine(M2Share.sConfigPath, M2Share.g_Config.sEnvirDir, "MapInfo");
            if (!Directory.Exists(sFileDir)) return; // 战神版: 不自动创建
            sFilePatchName = Path.Combine(sFileDir, sFileName);
            if (File.Exists(sFilePatchName))
            {
                LoadMapList = new StringList();
                LoadMapList.LoadFromFile(sFilePatchName);
                for (var i = 0; i < LoadMapList.Count; i++)
                {
                    LoadList.Add(LoadMapList[i]);
                }
                LoadMapList = null;
            }
        }

    }
}
