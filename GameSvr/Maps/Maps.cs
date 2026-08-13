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
                            if (TryApplyNumericFlag(MapFlag, s34, ref s38))
                            {
                                continue;
                            }
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
                            // 0x775249 / 0x7763D6 `call 0x4C6E94` with `B9 04 00 00 00
                            // mov ecx,4`, i.e. a 4-character prefix test: every token
                            // beginning with "MINE" -- including the "MINE2" that used to
                            // have its own invented arm here -- sets this one flag.
                            if (HUtil32.CompareLStr(s34, "MINE", "MINE".Length))
                            {
                                MapFlag.boMINE = true;
                                continue;
                            }
                            if (s34.Equals("NOPOSITIONMOVE", StringComparison.OrdinalIgnoreCase))
                            {
                                MapFlag.boNOPOSITIONMOVE = true;
                                continue;
                            }
                            if (s34.Equals("NORIDE", StringComparison.OrdinalIgnoreCase))
                            {
                                MapFlag.boNORIDE = true;
                                continue;
                            }
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
                            // Tokens outside the two 战神 pools are silently ignored,
                            // exactly like parser B's 0x776AD3 loop-continue. The
                            // removed set and its 0-hit scan are documented on
                            // TMapFlag itself.
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

        // The 战神 tokens whose parenthesised argument carries a value rather than acting as
        // a switch. All of them are matched with sub_4C6E94, whose body (0x4C6EC5 n<=0 guard,
        // 0x4C6ECC/0x4C6ED8 both-lengths >= n guards, then a per-character UpCase compare via
        // 0x4034D4) is HUtil32.CompareLStr instruction for instruction -- a length-limited
        // case-insensitive PREFIX test, which is what lets the still-parenthesised token match.
        // Each arm then pulls "(...)" with sub_4C6964 and converts with sub_40CA18, whose edx
        // is zeroed at every call site, so the default is 0 and never -1.
        internal static bool TryApplyNumericFlag(TMapFlag mapFlag, string token,
            ref string capture)
        {
            if (mapFlag == null || string.IsNullOrEmpty(token)) return false;

            // 0x77649C prefix(15) -> 0x7764C9 `69 C0 E8 03 00 00 imul eax,eax,0x3E8`
            // -> 0x7764CF `89 83 88 00 00 00 mov dword [ebx+0x88],eax`. Seconds in, ms out.
            if (HUtil32.CompareLStr(token, "MAPFIREWALLBURN", "MAPFIREWALLBURN".Length))
            {
                HUtil32.ArrestStringEx(token, '(', ')', ref capture);
                mapFlag.nMAPFIREWALLBURN =
                    unchecked(HUtil32.Str_ToInt(capture, 0) * 1000);
                return true;
            }
            // 0x7764E7 prefix(7) -> 0x776514 `66 89 43 62 mov word [ebx+0x62],ax`.
            if (HUtil32.CompareLStr(token, "MapSign", "MapSign".Length))
            {
                HUtil32.ArrestStringEx(token, '(', ')', ref capture);
                mapFlag.nMapSign = unchecked((ushort)HUtil32.Str_ToInt(capture, 0));
                return true;
            }
            // 0x776773 prefix(12) -> 0x7767A0 `66 89 83 BC 00 00 00`.
            if (HUtil32.CompareLStr(token, "UNIFIEDLEVEL", "UNIFIEDLEVEL".Length))
            {
                HUtil32.ArrestStringEx(token, '(', ')', ref capture);
                mapFlag.nUNIFIEDLEVEL = unchecked((ushort)HUtil32.Str_ToInt(capture, 0));
                return true;
            }
            // 0x7767B9 prefix(16) -> 0x7767E6 `66 89 83 BE 00 00 00`.
            if (HUtil32.CompareLStr(token, "LIMITPLAYERLEVEL", "LIMITPLAYERLEVEL".Length))
            {
                HUtil32.ArrestStringEx(token, '(', ')', ref capture);
                mapFlag.nLIMITPLAYERLEVEL =
                    unchecked((ushort)HUtil32.Str_ToInt(capture, 0));
                return true;
            }
            // 0x7767FF prefix(14) -> 0x77682C `66 89 83 C0 00 00 00`.
            if (HUtil32.CompareLStr(token, "LIMITHEROLEVEL", "LIMITHEROLEVEL".Length))
            {
                HUtil32.ArrestStringEx(token, '(', ')', ref capture);
                mapFlag.nLIMITHEROLEVEL =
                    unchecked((ushort)HUtil32.Str_ToInt(capture, 0));
                return true;
            }
            // 0x77652A prefix(11). [flag+0xB4] is a TMirStringList pointer, so an empty
            // argument frees the list (0x7765C2 arm: clear via [vmt+0x44] then FreeAndNil
            // 0x414C24) and a non-empty one creates-or-clears it and appends every '/'
            // separated piece (0x776588 `B1 2F mov cl,0x2F`, empty pieces skipped at
            // 0x77659D, loop while the remainder still has length at 0x7765B9).
            if (HUtil32.CompareLStr(token, "FLYDROPITEM", "FLYDROPITEM".Length))
            {
                HUtil32.ArrestStringEx(token, '(', ')', ref capture);
                if (string.IsNullOrEmpty(capture))
                {
                    mapFlag.FlyDropItemNames = null;
                    return true;
                }
                if (mapFlag.FlyDropItemNames == null)
                {
                    mapFlag.FlyDropItemNames = new List<string>();
                }
                else
                {
                    mapFlag.FlyDropItemNames.Clear();
                }
                var remaining = capture;
                var piece = string.Empty;
                do
                {
                    remaining = HUtil32.GetValidStr3(remaining, ref piece, "/");
                    if (piece != "") mapFlag.FlyDropItemNames.Add(piece);
                } while (remaining.Length > 0);
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
