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
                            if (HUtil32.CompareLStr(s34, "NEEDSET_ON", "NEEDSET_ON".Length))
                            {
                                MapFlag.nNeedONOFF = 1;
                                HUtil32.ArrestStringEx(s34, '(', ')', ref s38);
                                MapFlag.nNEEDSETONFlag = HUtil32.Str_ToInt(s38, -1);
                                continue;
                            }
                            if (HUtil32.CompareLStr(s34, "NEEDSET_OFF", "NEEDSET_OFF".Length))
                            {
                                MapFlag.nNeedONOFF = 0;
                                HUtil32.ArrestStringEx(s34, '(', ')', ref s38);
                                MapFlag.nNEEDSETONFlag = HUtil32.Str_ToInt(s38, -1);
                                continue;
                            }
                            if (HUtil32.CompareLStr(s34, "MUSIC", "MUSIC".Length))
                            {
                                MapFlag.boMUSIC = true;
                                HUtil32.ArrestStringEx(s34, '(', ')', ref s38);
                                MapFlag.nMUSICID = HUtil32.Str_ToInt(s38, -1);
                                continue;
                            }
                            if (HUtil32.CompareLStr(s34, "EXPRATE", "EXPRATE".Length))
                            {
                                MapFlag.boEXPRATE = true;
                                HUtil32.ArrestStringEx(s34, '(', ')', ref s38);
                                MapFlag.nEXPRATE = HUtil32.Str_ToInt(s38, -1);
                                continue;
                            }
                            if (HUtil32.CompareLStr(s34, "PKWINLEVEL", "PKWINLEVEL".Length))
                            {
                                MapFlag.boPKWINLEVEL = true;
                                HUtil32.ArrestStringEx(s34, '(', ')', ref s38);
                                MapFlag.nPKWINLEVEL = HUtil32.Str_ToInt(s38, -1);
                                continue;
                            }
                            if (HUtil32.CompareLStr(s34, "PKWINEXP", "PKWINEXP".Length))
                            {
                                MapFlag.boPKWINEXP = true;
                                HUtil32.ArrestStringEx(s34, '(', ')', ref s38);
                                MapFlag.nPKWINEXP = HUtil32.Str_ToInt(s38, -1);
                                continue;
                            }
                            if (HUtil32.CompareLStr(s34, "PKLOSTLEVEL", "PKLOSTLEVEL".Length))
                            {
                                MapFlag.boPKLOSTLEVEL = true;
                                HUtil32.ArrestStringEx(s34, '(', ')', ref s38);
                                MapFlag.nPKLOSTLEVEL = HUtil32.Str_ToInt(s38, -1);
                                continue;
                            }
                            if (HUtil32.CompareLStr(s34, "PKLOSTEXP", "PKLOSTEXP".Length))
                            {
                                MapFlag.boPKLOSTEXP = true;
                                HUtil32.ArrestStringEx(s34, '(', ')', ref s38);
                                MapFlag.nPKLOSTEXP = HUtil32.Str_ToInt(s38, -1);
                                continue;
                            }
                            if (HUtil32.CompareLStr(s34, "DECHP", "DECHP".Length))
                            {
                                MapFlag.boDECHP = true;
                                HUtil32.ArrestStringEx(s34, '(', ')', ref s38);
                                MapFlag.nDECHPPOINT = HUtil32.Str_ToInt(HUtil32.GetValidStr3(s38, ref s38, HUtil32.Backslash), -1);
                                MapFlag.nDECHPTIME = HUtil32.Str_ToInt(s38, -1);
                                continue;
                            }
                            if (HUtil32.CompareLStr(s34, "INCHP", "INCHP".Length))
                            {
                                MapFlag.boINCHP = true;
                                HUtil32.ArrestStringEx(s34, '(', ')', ref s38);
                                MapFlag.nINCHPPOINT = HUtil32.Str_ToInt(HUtil32.GetValidStr3(s38, ref s38, HUtil32.Backslash), -1);
                                MapFlag.nINCHPTIME = HUtil32.Str_ToInt(s38, -1);
                                continue;
                            }
                            if (HUtil32.CompareLStr(s34, "DECGAMEGOLD", "DECGAMEGOLD".Length))
                            {
                                MapFlag.boDECGAMEGOLD = true;
                                HUtil32.ArrestStringEx(s34, '(', ')', ref s38);
                                MapFlag.nDECGAMEGOLD = HUtil32.Str_ToInt(HUtil32.GetValidStr3(s38, ref s38, HUtil32.Backslash), -1);
                                MapFlag.nDECGAMEGOLDTIME = HUtil32.Str_ToInt(s38, -1);
                                continue;
                            }
                            if (HUtil32.CompareLStr(s34, "DECGAMEPOINT", "DECGAMEPOINT".Length))
                            {
                                MapFlag.boDECGAMEPOINT = true;
                                HUtil32.ArrestStringEx(s34, '(', ')', ref s38);
                                MapFlag.nDECGAMEPOINT = HUtil32.Str_ToInt(HUtil32.GetValidStr3(s38, ref s38, HUtil32.Backslash), -1);
                                MapFlag.nDECGAMEPOINTTIME = HUtil32.Str_ToInt(s38, -1);
                                continue;
                            }
                            if (HUtil32.CompareLStr(s34, "INCGAMEGOLD", "INCGAMEGOLD".Length))
                            {
                                MapFlag.boINCGAMEGOLD = true;
                                HUtil32.ArrestStringEx(s34, '(', ')', ref s38);
                                MapFlag.nINCGAMEGOLD = HUtil32.Str_ToInt(HUtil32.GetValidStr3(s38, ref s38, HUtil32.Backslash), -1);
                                MapFlag.nINCGAMEGOLDTIME = HUtil32.Str_ToInt(s38, -1);
                                continue;
                            }
                            if (HUtil32.CompareLStr(s34, "INCGAMEPOINT", "INCGAMEPOINT".Length))
                            {
                                MapFlag.boINCGAMEPOINT = true;
                                HUtil32.ArrestStringEx(s34, '(', ')', ref s38);
                                MapFlag.nINCGAMEPOINT = HUtil32.Str_ToInt(HUtil32.GetValidStr3(s38, ref s38, HUtil32.Backslash), -1);
                                MapFlag.nINCGAMEPOINTTIME = HUtil32.Str_ToInt(s38, -1);
                                continue;
                            }
                            if (s34.Equals("RUNHUMAN", StringComparison.OrdinalIgnoreCase))
                            {
                                MapFlag.boRUNHUMAN = true;
                                continue;
                            }
                            if (s34.Equals("RUNMON", StringComparison.OrdinalIgnoreCase))
                            {
                                MapFlag.boRUNMON = true;
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
                            if (s34.Equals("NOGUILDRECALL", StringComparison.OrdinalIgnoreCase))
                            {
                                MapFlag.boNOGUILDRECALL = true;
                                continue;
                            }
                            if (s34.Equals("NODEARRECALL", StringComparison.OrdinalIgnoreCase))
                            {
                                MapFlag.boNODEARRECALL = true;
                                continue;
                            }
                            if (s34.Equals("NOMASTERRECALL", StringComparison.OrdinalIgnoreCase))
                            {
                                MapFlag.boNOMASTERRECALL = true;
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
                            if (s34.Equals("MINE", StringComparison.OrdinalIgnoreCase))
                            {
                                MapFlag.boMINE = true;
                                continue;
                            }
                            if (s34.Equals("MINE2", StringComparison.OrdinalIgnoreCase))
                            {
                                MapFlag.boMINE2 = true;
                                continue;
                            }
                            if (s34.Equals("NOTHROWITEM", StringComparison.OrdinalIgnoreCase))
                            {
                                MapFlag.boNOTHROWITEM = true;
                                continue;
                            }
                            if (s34.Equals("NODROPITEM", StringComparison.OrdinalIgnoreCase))
                            {
                                MapFlag.boNODROPITEM = true;
                                continue;
                            }
                            if (s34.Equals("NOPOSITIONMOVE", StringComparison.OrdinalIgnoreCase))
                            {
                                MapFlag.boNOPOSITIONMOVE = true;
                                continue;
                            }
                            if (s34.Equals("NOHORSE", StringComparison.OrdinalIgnoreCase))
                            {
                                MapFlag.boNOPOSITIONMOVE = true;
                                continue;
                            }
                            if (s34.Equals("NOCHAT", StringComparison.OrdinalIgnoreCase))
                            {
                                MapFlag.boNOCHAT = true;
                                continue;
                            }
                            if (HUtil32.CompareLStr(s34, "KILLFUNC", "KILLFUNC".Length))
                            {
                                MapFlag.boKILLFUNC = true;
                                HUtil32.ArrestStringEx(s34, '(', ')', ref s38);
                                MapFlag.nKILLFUNCNO = HUtil32.Str_ToInt(s38, -1);
                                continue;
                            }
                            // NOHUMNOMON REMOVED (2026-08-09, Tier-1 negative
                            // evidence). 战神 has no such map flag: an image-wide
                            // byte scan for NOHUMNOMON / NOHUMNOMONSTER / NOHUM /
                            // NOMON / NoHumNoMon / NOHUMANNOMON / NOHUM_NOMON
                            // returns 0 hits, and the complete map-flag token
                            // census -- the two parallel literal blocks at
                            // 0x775BFC and 0x776B20, 46 tokens each (SAFE,
                            // NOTHROUGH, DARK, FIGHT, FIGHT3, FREEPK, DAY, QUIZ,
                            // DARE, MONATTACK, ... LIMITHEROLEVEL, NOMAGIC,
                            // TRIGGERBOMB, FOXMAP, ...) -- contains no equivalent.
                            // Parsing it let a map file silently suppress monster
                            // regeneration in a way native never does.
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
                        else if (limitSkillIds != null)
                        {
                            loadedMap.LimitSkillIds.UnionWith(limitSkillIds);
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
