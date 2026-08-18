using System.Collections;
using SystemModule;
using SystemModule.Common;

namespace GameSvr
{
    public class LocalDB
    {
        public bool LoadAdminList()
        {
            var adminList = M2Share.UserEngine.m_AdminList;
            adminList.Clear();

            string fileName = Path.Combine(M2Share.sConfigPath,
                M2Share.g_Config.sEnvirDir, "AdminList.txt");
            bool sourceLoaded = File.Exists(fileName);
            if (sourceLoaded)
            {
                byte[] fileBytes = File.ReadAllBytes(fileName);
                int lineStart = 0;
                for (int index = 0; index <= fileBytes.Length; index++)
                {
                    if (index < fileBytes.Length &&
                        fileBytes[index] != (byte)'\r' &&
                        fileBytes[index] != (byte)'\n')
                    {
                        continue;
                    }

                    AddNativeAdminLine(fileBytes.AsSpan(lineStart, index - lineStart));
                    if (index < fileBytes.Length && fileBytes[index] == (byte)'\r' &&
                        index + 1 < fileBytes.Length && fileBytes[index + 1] == (byte)'\n')
                    {
                        index++;
                    }
                    lineStart = index + 1;
                }
            }

            ReloadNativeFestivalConfig();
            return sourceLoaded;
        }

        private static void AddNativeAdminLine(ReadOnlySpan<byte> line)
        {
            if (line.IsEmpty || line[0] == (byte)';')
            {
                return;
            }

            int permission = line[0] switch
            {
                (byte)'*' => 4,
                (byte)'1' => 3,
                (byte)'2' => 2,
                _ => 0
            };
            if (permission == 0)
            {
                return;
            }

            int delimiter = line.IndexOfAny((byte)'\t', (byte)' ');
            if (delimiter < 0)
            {
                return;
            }

            ReadOnlySpan<byte> name = TrimNativeBytes(line[(delimiter + 1)..]);
            if (name.IsEmpty)
            {
                return;
            }

            int nameLength = Math.Min(name.Length, 14);
            var nativeName = name[..nameLength].ToArray();
            FoldAsciiLower(nativeName);
            M2Share.UserEngine.m_AdminList.Insert(0, new TAdminInfo
            {
                nLv = permission,
                sChrName = HUtil32.GbkEncoding.GetString(nativeName),
                sIPaddr = string.Empty,
                NativeChrNameBytes = nativeName
            });
        }

        private static ReadOnlySpan<byte> TrimNativeBytes(ReadOnlySpan<byte> value)
        {
            int start = 0;
            int end = value.Length;
            while (start < end && value[start] <= 0x20)
            {
                start++;
            }
            while (end > start && value[end - 1] <= 0x20)
            {
                end--;
            }
            return value[start..end];
        }

        internal static void FoldAsciiLower(Span<byte> value)
        {
            for (int index = 0; index < value.Length; index++)
            {
                if (value[index] >= (byte)'A' && value[index] <= (byte)'Z')
                {
                    value[index] = (byte)(value[index] + 0x20);
                }
            }
        }

        private static void ReloadNativeFestivalConfig()
        {
            string feastDaysPath = NativeFestivalConfig.ResolveDefaultPath(
                M2Share.sRootPath, M2Share.g_Config.sBaseDir);
            if (!NativeFestivalConfig.TryLoad(feastDaysPath,
                    out var loadedConfig, out var error))
            {
                M2Share.ErrorMessage("加载节日配置文件失败: " + error);
                return;
            }

            M2Share.FestivalConfig = NativeFestivalConfig.Append(
                M2Share.FestivalConfig, loadedConfig);
        }

        public void LoadGuardList()
        {
            try
            {
                var s14 = string.Empty;
                var s1C = string.Empty;
                var s20 = string.Empty;
                var s24 = string.Empty;
                var s28 = string.Empty;
                var s2C = string.Empty;
                StringList tGuardList;
                TBaseObject tGuard;
                var sfilename = Path.Combine(M2Share.sConfigPath, M2Share.g_Config.sEnvirDir, "GuardList.txt");
                if (File.Exists(sfilename))
                {
                    tGuardList = new StringList();
                    tGuardList.LoadFromFile(sfilename);
                    for (var i = 0; i < tGuardList.Count; i++)
                    {
                        s14 = tGuardList[i];
                        if (!string.IsNullOrEmpty(s14) && s14[0] != ';')
                        {
                            s14 = HUtil32.GetValidStrCap(s14, ref s1C, new[] { " " });
                            if (!string.IsNullOrEmpty(s1C) && s1C[0] == '\"')
                            {
                                HUtil32.ArrestStringEx(s1C, '\"', '\"', ref s1C);
                            }
                            s14 = HUtil32.GetValidStr3(s14, ref s20, new[] { ' ' });
                            s14 = HUtil32.GetValidStr3(s14, ref s24, new[] { ' ', ',' });
                            s14 = HUtil32.GetValidStr3(s14, ref s28, new[] { ' ', ',', ':' });
                            s14 = HUtil32.GetValidStr3(s14, ref s2C, new[] { ' ', ':' });
                            if (!string.IsNullOrEmpty(s1C) && s20 != "" && s2C != "")
                            {
                                tGuard = M2Share.UserEngine.RegenMonsterByName(s20, (short)HUtil32.Str_ToInt(s24, 0), (short)HUtil32.Str_ToInt(s28, 0), s1C);
                                if (tGuard != null)
                                {
                                    tGuard.m_btDirection = (byte)HUtil32.Str_ToInt(s2C, 0);
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.StackTrace);
            }
        }

        
        
        
        public void LoadMakeItem()
        {
            int nItemCount;
            var sLine = string.Empty;
            var sSubName = string.Empty;
            var sItemName = string.Empty;
            IList<TMakeItem> List28 = null;
            var sFileName = Path.Combine(M2Share.sConfigPath, M2Share.g_Config.sEnvirDir, "MakeItem.txt");
            if (File.Exists(sFileName))
            {
                using var LoadList = new StringList();
                LoadList.LoadFromFile(sFileName);
                for (var i = 0; i < LoadList.Count; i++)
                {
                    sLine = LoadList[i].Trim();
                    if (string.IsNullOrEmpty(sLine) || sLine.StartsWith(";"))
                    {
                        continue;
                    }
                    if (sLine.StartsWith("["))
                    {
                        if (List28 != null)
                        {
                            AddMakeItemSection(sItemName, List28);
                        }
                        List28 = new List<TMakeItem>();
                        HUtil32.ArrestStringEx(sLine, '[', ']', ref sItemName);
                    }
                    else
                    {
                        if (List28 != null)
                        {
                            sLine = HUtil32.GetValidStr3(sLine, ref sSubName, new[] { " ", "\t" });
                            // 0x74DA19 mov edx,1 / 0x74DA21 call 0x40CA18(StrToIntDef)：解析失败回落 1。
                            // HUtil32.Str_ToInt 的 out 参数会被 int.TryParse 覆写成 0，def 形同虚设，
                            // 所以这里不能走它——写不出数量的配方行会得到 need=0，材料白送。
                            nItemCount = int.TryParse(sLine.Trim(), out var parsedCount) ? parsedCount : 1;
                            List28.Add(new TMakeItem() { ItemName = sSubName, ItemCount = nItemCount });
                        }
                    }
                }
                if (List28 != null)
                {
                    AddMakeItemSection(sItemName, List28);
                }
            }
        }

        // 原生解析器 sub_74D8C4 只调 AddObject（VMT+0x3C @0x74D997 提交上一节、
        // @0x74DA4F 提交最后一节），函数体内没有任何 Clear，所以重名的 [节] 会共存；
        // 查找 sub_74E0F8 从索引 0 线性扫、0x74E14A call 0x40591C 比中就
        // 0x74E14F jne 之外直接返回（0x74E161 jmp 出循环），即【第一条命中】。
        // Dictionary.Add 对重复 key 抛 ArgumentException，会把加载打断在半路，
        // MsgGetReloadMakeItemList 触发的重载更是必炸。
        private static void AddMakeItemSection(string sItemName, IList<TMakeItem> List28)
        {
            if (!M2Share.g_MakeItemList.ContainsKey(sItemName))
            {
                M2Share.g_MakeItemList.Add(sItemName, List28);
            }
        }

        public void LoadPsNpcScript()
        {
            M2Share.PasEngine?.LoadNpcScriptMap();
        }

        public int LoadMapQuest()
        {
            M2Share.PasEngine?.LoadMapQuestMap();
            return 1;
        }

        public void LoadMerchant()
        {
            var sLineText = string.Empty;
            var sScript = string.Empty;
            var sMapName = string.Empty;
            var sX = string.Empty;
            var sY = string.Empty;
            var sName = string.Empty;
            var sFlag = string.Empty;
            var sAppr = string.Empty;
            var sIsCalste = string.Empty;
            var sCanMove = string.Empty;
            var sMoveTime = string.Empty;
            var sFileName = Path.Combine(M2Share.sConfigPath, M2Share.g_Config.sEnvirDir, "Merchant.txt");
            if (File.Exists(sFileName))
            {
                var tMerchantList = new StringList();
                tMerchantList.LoadFromFile(sFileName);
                for (var i = 0; i < tMerchantList.Count; i++)
                {
                    sLineText = tMerchantList[i].Trim();
                    if (sLineText != "" && sLineText[0] != ';')
                    {
                        sLineText = HUtil32.GetValidStr3(sLineText, ref sScript, new[] { " ", "\t" });
                        sLineText = HUtil32.GetValidStr3(sLineText, ref sMapName, new[] { " ", "\t" });
                        sLineText = HUtil32.GetValidStr3(sLineText, ref sX, new[] { " ", "\t" });
                        sLineText = HUtil32.GetValidStr3(sLineText, ref sY, new[] { " ", "\t" });
                        sLineText = HUtil32.GetValidStr3(sLineText, ref sName, new[] { " ", "\t" });
                        if (!string.IsNullOrEmpty(sName) && sName[0] == '\"')
                        {
                            HUtil32.ArrestStringEx(sName, '\"', '\"', ref sName);
                        }
                        sLineText = HUtil32.GetValidStr3(sLineText, ref sFlag, new[] { " ", "\t" });
                        sLineText = HUtil32.GetValidStr3(sLineText, ref sAppr, new[] { " ", "\t" });
                        sLineText = HUtil32.GetValidStr3(sLineText, ref sIsCalste, new[] { " ", "\t" });
                        sLineText = HUtil32.GetValidStr3(sLineText, ref sCanMove, new[] { " ", "\t" });
                        sLineText = HUtil32.GetValidStr3(sLineText, ref sMoveTime, new[] { " ", "\t" });
                        if (sScript != "" && sMapName != "" && sAppr != "")
                        {
                            var tMerchantNPC = new Merchant
                            {
                                m_sScript = sScript,
                                m_sMapName = sMapName,
                                m_nCurrX = (short)HUtil32.Str_ToInt(sX, 0),
                                m_nCurrY = (short)HUtil32.Str_ToInt(sY, 0),
                                m_sCharName = sName,
                                m_nFlag = (short)HUtil32.Str_ToInt(sFlag, 0),
                                m_wAppr = (ushort)HUtil32.Str_ToInt(sAppr, 0),
                                m_dwMoveTime = HUtil32.Str_ToInt(sMoveTime, 0)
                            };
                            if (HUtil32.Str_ToInt(sIsCalste, 0) != 0)
                            {
                                tMerchantNPC.m_boCastle = true;
                            }
                            if (HUtil32.Str_ToInt(sCanMove, 0) != 0 && tMerchantNPC.m_dwMoveTime > 0)
                            {
                                tMerchantNPC.m_boCanMove = true;
                            }
                            M2Share.UserEngine.AddMerchant(tMerchantNPC);
                        }
                    }
                }
            }
        }

        public int LoadPsNpcScriptNpcs()
        {
            var result = 0;
            var sScript = string.Empty;
            var sMapName = string.Empty;
            var sX = string.Empty;
            var sY = string.Empty;
            var sName = string.Empty;
            var sDir = string.Empty;
            var sAppr = string.Empty;
            var sCastle = string.Empty;
            var sFileName = Path.Combine(M2Share.sConfigPath, M2Share.g_Config.sEnvirDir, "PsNpcScript.txt");
            if (!File.Exists(sFileName))
            {
                return result;
            }

            using var loadList = new StringList();
            loadList.LoadFromFile(sFileName);
            for (var i = 0; i < loadList.Count; i++)
            {
                var lineText = loadList[i].Trim();
                if (string.IsNullOrEmpty(lineText) || lineText[0] == ';')
                {
                    continue;
                }

                lineText = HUtil32.GetValidStr3(lineText, ref sScript, new[] { " ", "\t" });
                lineText = HUtil32.GetValidStr3(lineText, ref sMapName, new[] { " ", "\t" });
                lineText = HUtil32.GetValidStr3(lineText, ref sX, new[] { " ", "\t" });
                lineText = HUtil32.GetValidStr3(lineText, ref sY, new[] { " ", "\t" });
                lineText = HUtil32.GetValidStrCap(lineText, ref sName, new[] { " ", "\t" });
                if (!string.IsNullOrEmpty(sName) && sName[0] == '\"')
                {
                    HUtil32.ArrestStringEx(sName, '\"', '\"', ref sName);
                }
                lineText = HUtil32.GetValidStr3(lineText, ref sDir, new[] { " ", "\t" });
                lineText = HUtil32.GetValidStr3(lineText, ref sAppr, new[] { " ", "\t" });
                HUtil32.GetValidStr3(lineText, ref sCastle, new[] { " ", "\t" });

                if (string.IsNullOrEmpty(sScript) || string.IsNullOrEmpty(sMapName) || string.IsNullOrEmpty(sName) || string.IsNullOrEmpty(sAppr))
                {
                    continue;
                }

                var direction = (byte)HUtil32.Str_ToInt(sDir, 0);
                var merchant = new Merchant
                {
                    m_sScript = sScript,
                    m_sMapName = sMapName,
                    m_nCurrX = (short)HUtil32.Str_ToInt(sX, 0),
                    m_nCurrY = (short)HUtil32.Str_ToInt(sY, 0),
                    m_sCharName = sName,
                    m_nFlag = direction,
                    m_btDirection = direction,
                    m_wAppr = (ushort)HUtil32.Str_ToInt(sAppr, 0),
                    m_boCastle = HUtil32.Str_ToInt(sCastle, 0) != 0
                };
                NativeCelebrityStatueManager.Initialize(merchant);
                M2Share.UserEngine.AddMerchant(merchant);
                result++;
            }
            return result;
        }

        private void LoadMonGen_LoadMapGen(StringList MonGenList, string sFileName)
        {
            var sFileDir = Path.Combine(M2Share.sConfigPath, M2Share.g_Config.sEnvirDir, "MonGen");
            if (!Directory.Exists(sFileDir))
            {
                Directory.CreateDirectory(sFileDir);
            }
            var sFilePatchName = sFileDir + sFileName;
            if (!File.Exists(sFilePatchName)) return;
            using var LoadList = new StringList();
            LoadList.LoadFromFile(sFilePatchName);
            for (var i = 0; i < LoadList.Count; i++)
            {
                MonGenList.Add(LoadList[i]);
            }
        }

        public int LoadMonGen()
        {
            var sLineText = string.Empty;
            var sData = string.Empty;
            int i;
            var result = 0;
            var sFileName = Path.Combine(M2Share.sConfigPath, M2Share.g_Config.sEnvirDir, "MonGen.txt");
            if (File.Exists(sFileName))
            {
                using var LoadList = new StringList();
                LoadList.LoadFromFile(sFileName);
                i = 0;
                while (true)
                {
                    if (i >= LoadList.Count)
                    {
                        break;
                    }
                    if (HUtil32.CompareLStr("loadgen", LoadList[i], "loadgen".Length))
                    {
                        var sMapGenFile = HUtil32.GetValidStr3(LoadList[i], ref sLineText, new[] { " ", "\t" });
                        LoadList.RemoveAt(i);
                        if (sMapGenFile != "")
                        {
                            LoadMonGen_LoadMapGen(LoadList, sMapGenFile);
                        }
                    }
                    i++;
                }
                MonGenInfo MonGenInfo;
                for (i = 0; i < LoadList.Count; i++)
                {
                    sLineText = LoadList[i];
                    if (!string.IsNullOrEmpty(sLineText) && sLineText[0] != ';')
                    {
                        MonGenInfo = new MonGenInfo();
                        sLineText = HUtil32.GetValidStr3(sLineText, ref sData, new[] { " ", "\t" });
                        MonGenInfo.sMapName = sData;
                        sLineText = HUtil32.GetValidStr3(sLineText, ref sData, new[] { " ", "\t" });
                        var monGenX = HUtil32.Str_ToInt(sData, -1);
                        sLineText = HUtil32.GetValidStr3(sLineText, ref sData, new[] { " ", "\t" });
                        var monGenY = HUtil32.Str_ToInt(sData, -1);
                        if (monGenX < 0 || monGenY < 0)
                        {
                            // sub_5F878C @0x5F88A6 ShowMessage(0x5F8970 + line) — 坐标错误--
                            M2Share.MainOutMessage("坐标错误--" + LoadList[i]);
                            continue;
                        }
                        MonGenInfo.nX = monGenX;
                        MonGenInfo.nY = monGenY;
                        sLineText = HUtil32.GetValidStrCap(sLineText, ref sData, new[] { " ", "\t" });
                        if (!string.IsNullOrEmpty(sData) && sData[0] == '\"')
                        {
                            HUtil32.ArrestStringEx(sData, "\"", "\"", ref sData);
                        }
                        MonGenInfo.sMonName = sData;
                        sLineText = HUtil32.GetValidStr3(sLineText, ref sData, new[] { " ", "\t" });
                        MonGenInfo.nRange = HUtil32.Str_ToInt(sData, 0);
                        sLineText = HUtil32.GetValidStr3(sLineText, ref sData, new[] { " ", "\t" });
                        MonGenInfo.nCount = HUtil32.Str_ToInt(sData, 0);
                        sLineText = HUtil32.GetValidStr3(sLineText, ref sData, new[] { " ", "\t" });
                        MonGenInfo.dwZenTime = HUtil32.Str_ToInt(sData, -1) * 60 * 1000;
                        sLineText = HUtil32.GetValidStr3(sLineText, ref sData, new[] { " ", "\t" });
                        MonGenInfo.nMissionGenRate = HUtil32.Str_ToInt(sData, 0);// Focused coordinate spawn rate 1-100
                        // SPWN-13 / SPWN-14: 第 8 列同时就是 [gen+0x28]，第 9 列是
                        // [gen+0x40] 生成播报。原生 LoadMonGen (0x67B35C) 整个函数被
                        // VMProtect 虚拟化（0x67B35C `E9 A3 2B 44 00` -> 0xABDF04 的
                        // `push imm32 / call` VM 入口），无法直接读列序；列归属靠
                        // TMonGen 记录 0x44 字节里唯一空闲的整型槽 +0x28 与唯一的
                        // 1 字节 dyn array 槽 +0x40 反推，并与真实 mongen.txt 的
                        // 「最多九列」实测吻合（ys207/ys208/pas-include 四份抓包合计
                        // 43405 行七列 + 648 行八列 + 70 行九列，无十列）。
                        MonGenInfo.nCorpseSeconds = MonGenInfo.nMissionGenRate;
                        sLineText = HUtil32.GetValidStr3(sLineText, ref sData, new[] { " ", "\t" });
                        // 第 9 列用与其余各列相同的分词器切；实测九列样本的播报文本
                        // 全是无空格中文句子，所以「取剩余整行」与「取一个 token」
                        // 在真实数据上不可区分。
                        MonGenInfo.GenAnnounceBytes = string.IsNullOrEmpty(sData)
                            ? null
                            : HUtil32.GbkEncoding.GetBytes(sData);
                        if (!string.IsNullOrEmpty(MonGenInfo.sMapName) && !string.IsNullOrEmpty(MonGenInfo.sMonName) && MonGenInfo.dwZenTime != 0 && M2Share.MapManager.GetMapInfo(M2Share.nServerIndex, MonGenInfo.sMapName) != null)
                        {
                            MonGenInfo.CertList = new List<TBaseObject>();
                            MonGenInfo.Envir = M2Share.MapManager.FindMap(MonGenInfo.sMapName);
                            if (MonGenInfo.Envir != null)
                            {
                                M2Share.UserEngine.m_MonGenList.Add(MonGenInfo);
                            }
                            else
                            {
                                MonGenInfo = null;
                            }
                        }
                    }
                }
                MonGenInfo = new MonGenInfo
                {
                    CertList = new List<TBaseObject>(),
                    Envir = null
                };
                M2Share.UserEngine.m_MonGenList.Add(MonGenInfo);
                result = 1;
            }
            return result;
        }

        public int LoadMonitems(string MonName, ref IList<TMonItem> ItemList)
        {
            var s30 = string.Empty;
            var result = 0;
            var s24 = Path.Combine(M2Share.sConfigPath, M2Share.g_Config.sEnvirDir, "MonItems", $"{MonName}.txt");
            if (File.Exists(s24))
            {
                if (ItemList != null)
                {
                    for (var i = 0; i < ItemList.Count; i++)
                    {
                        ItemList[i] = null;
                    }
                    ItemList.Clear();
                }
                using var LoadList = new StringList();
                LoadList.LoadFromFile(s24);
                for (var i = 0; i < LoadList.Count; i++)
                {
                    // 战神 sub_6799E0 @0x679ADE-0x679B00 的行过滤只有两步，且都作用在
                    // 【Trim 之后】的整行上：
                    //   0x679AE4  call sub_40C140          ; Trim（两端剥 <=0x20，见
                    //                                      ;   0x40C171/0x40C193 `cmp byte,0x20 / jbe`）
                    //   0x679AE9  cmp [ebp-0x14],0 / je    ; 空行跳过
                    //   0x679AF6  mov edx,0x679CD4         ; 长度前缀=1 的字面量 ";"
                    //   0x679AFB  call sub_40591C (_LStrCmp) / 0x679B00 je  ; 【整行等于 ";"】才跳过
                    // 不是「以 ';' 开头就跳过」。分号开头但仍能解析出物品名的行在原生里
                    // 是生效的（例 ";1/100 屠龙" → 首 token ";1" → StrToIntDef 失败取默认 1）。
                    // 生产实测：363 个 MonItems 文件 14848 行里，以 ';' 开头的只有 4 行
                    // （幻影蜘蛛:54 / 虹魔蝎卫:55 / 虹魔蝎卫0:16 / 邪恶毒蛇8:54，全是韩文
                    // 残留注释），四行的物品名都查不到 StdItem，原生同样丢弃 → 本次改动
                    // 对该部署【零可观测差异】；带首尾空白的行 0 条，所以补 Trim 同样无影响。
                    var s28 = LoadList[i]?.Trim();
                    if (!string.IsNullOrEmpty(s28) && s28 != ";")
                    {
                        // ✅ 战神字节证据 (Tier-1)。EA: TUserEngine.LoadMonItems `sub_6799E0`
                        // (目录字面量 0x679CB0="MonItems\", 扩展名 0x679CC4=".txt",
                        //  注释字符 0x679CD4=";")。
                        // 分隔符集逐字节 —— sub_4C6BA4(GetValidStr3) 的栈参 [ebp+0xC] 是
                        // 【个数-1】(0x4C6BF0 `mov eax,[ebp+0xC]` / `inc eax`)：
                        //   字段1/2: `push 2` + [ebp-0x3C]={09,2F,20} = {TAB,'/',' '} 三个
                        //            (0x679B06-0x679B21 / 0x679B31-0x679B4C)
                        //   字段3/4: `push 1` + [ebp-0x48]={09,20}    = {TAB,' '}     两个
                        //            (0x679B5C-0x679B73 / 0x679B83-0x679B9A)
                        s28 = HUtil32.GetValidStr3(s28, ref s30, new[] { " ", "/", "\t" });
                        // 战神 0x679C06 `BA 01 00 00 00` mov edx,1 → sub_40CA18(StrToIntDef)
                        // 默认值是 【1】不是 -1；随后 0x679C13 `48` dec eax → SelPoint=值-1。
                        // sub_40CA18 = StrToIntDef: call sub_403DCC(Val); `cmp [ebp-0x10],0 / je`
                        // 解析出错才取 default → 与 Str_ToInt(s,def) 语义一致。
                        var n18 = HUtil32.Str_ToInt(s30, 1);
                        s28 = HUtil32.GetValidStr3(s28, ref s30, new[] { " ", "/", "\t" });
                        // 战神 0x679C1A `BA 01 00 00 00` → MaxPoint = StrToIntDef(f2, 1)（无 dec）
                        var n1C = HUtil32.Str_ToInt(s30, 1);
                        s28 = HUtil32.GetValidStr3(s28, ref s30, new[] { " ", "\t" });
                        // 战神 sub_6799E0 函数体 0x6799E0..0x679C92 内【零个 0x22('"') 比较】：
                        // 只出现 09/2F/20（分隔符）与 3B（';' 注释）。MonItems 行的物品名
                        // 【不去引号】，带引号的名字会在 sub_74C2D4 查表失败 → 整行被丢弃。
                        // 故此处不能沿用 Npcs/MonGen 那套 ArrestStringEx 去引号。
                        var s2C = s30;
                        s28 = HUtil32.GetValidStr3(s28, ref s30, new[] { " ", "\t" });
                        // 战神 0x679C2D `BA 01 00 00 00` → Count = StrToIntDef(f4, 1)
                        var n20 = HUtil32.Str_ToInt(s30, 1);
                        // 战神的行接受条件【只有一条】：物品名能解析成 StdItem。
                        //   0x679BB4 call sub_74C2D4(名字) → [ebp-0x2C]
                        //   0x679BC1 `cmp [ebp-0x2C],0 / je 0x679BDD`  ← nil 则不分配记录
                        //   0x679BDD `cmp [ebp-8],0 / je 0x679C4E`     ← 未分配则整行跳过
                        // 【没有】任何 SelPoint>0 / MaxPoint>0 的数值门。
                        // 例：一行 "1/0<TAB>记忆项链" → 原生 SelPoint=1-1=0, MaxPoint=
                        //     StrToIntDef("0",1)=0 → Random(0)<=0 恒真 = 每杀必掉；
                        //     旧 C# 的 `n1C>0` 门把它整行丢弃（祖玛教主/祖玛教主00 各一行,
                        //     样本数据 D:/loym2/staging/pas-include-context-20260714/Envir/MonItems 实测）。
                        //
                        // ⚠️ 这里【不能】用 `GetStdItemIdx(name) > 0` 当门：
                        // GetStdItemIdx / CopyToUserItemFromName 在检测到原生金币哨兵时
                        // (HasNativeStdItemSentinel: items[0].NativeWireIndex==0 && Name=="金币")
                        // 从下标 1 开始扫，**跳过金币本身**；而战神 sub_74C2D4 是纯哈希查表、
                        // 无任何下标排除，且掉落消费者 sub_71FA20 @0x71FB64 靠
                        // `cmp word ptr [StdItem],0` 判定"这行是金币"→ 金币【必须】解析成功。
                        // 样本数据里有 369 行金币(遍布 328 个怪),用 GetStdItemIdx 当门会把
                        // 这些金币掉落全部静默杀掉。故按战神语义直接查名字表(含 index 0)。
                        if (ResolvesToStdItemName(s2C))
                        {
                            if (ItemList == null)
                            {
                                ItemList = new List<TMonItem>();
                            }
                            var MonItem = new TMonItem
                            {
                                SelPoint = n18 - 1,
                                MaxPoint = n1C,
                                ItemName = s2C,
                                Count = n20
                            };
                            ItemList.Add(MonItem);
                            result++;
                        }
                    }
                }
            }
            return result;
        }

        /// <summary>
        /// 战神 <c>sub_74C2D4</c> 的等价：按名字在标准物品表里查一次，命中即真。
        /// EA 0x74C2D4: <c>test esi,esi / je</c>（空名 → nil）然后
        /// <c>mov eax,[ebx+0x20] ; call sub_49F5F4</c>（纯哈希查表）。
        /// 与 <see cref="UserEngine.GetStdItemIdx"/> 的区别：**不跳过下标 0 的金币哨兵**，
        /// 因为战神这条查表没有任何下标排除，而 MonItems 行里金币是合法且常见的物品名
        /// （样本数据 369 行）。掉落消费者 sub_71FA20 @0x71FB64 用
        /// <c>cmp word ptr [StdItem],0</c> 区分金币行，前提是金币能查到。
        /// </summary>
        private static bool ResolvesToStdItemName(string sItemName)
        {
            if (string.IsNullOrEmpty(sItemName)) return false;
            var items = M2Share.UserEngine?.StdItemList;
            if (items == null) return false;
            for (var i = 0; i < items.Count; i++)
            {
                var StdItem = items[i];
                if (StdItem == null) continue;
                if (StdItem.Name.Equals(sItemName,
                        StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

        public void LoadNpcs()
        {
            var s10 = string.Empty;
            var s18 = string.Empty;
            var s1C = string.Empty;
            var s20 = string.Empty;
            var s24 = string.Empty;
            var s28 = string.Empty;
            var s2C = string.Empty;
            var s30 = string.Empty;
            var s34 = string.Empty;
            var s38 = string.Empty;
            NormNpc NPC;
            string sFileName = Path.Combine(M2Share.sConfigPath, M2Share.g_Config.sEnvirDir, "Npcs.txt");
            if (File.Exists(sFileName))
            {
                using var LoadList = new StringList();
                LoadList.LoadFromFile(sFileName);
                for (var i = 0; i < LoadList.Count; i++)
                {
                    s18 = LoadList[i].Trim();
                    if (!string.IsNullOrEmpty(s18) && s18[0] != ';')
                    {
                        s18 = HUtil32.GetValidStrCap(s18, ref s20, new[] { " ", "\t" });
                        if (!string.IsNullOrEmpty(s20) && s20[0] == '\"')
                        {
                            HUtil32.ArrestStringEx(s20, "\"", "\"", ref s20);
                        }
                        s18 = HUtil32.GetValidStr3(s18, ref s24, new[] { " ", "\t" });
                        s18 = HUtil32.GetValidStr3(s18, ref s28, new[] { " ", "\t" });
                        s18 = HUtil32.GetValidStr3(s18, ref s2C, new[] { " ", "\t" });
                        s18 = HUtil32.GetValidStr3(s18, ref s30, new[] { " ", "\t" });
                        s18 = HUtil32.GetValidStr3(s18, ref s34, new[] { " ", "\t" });
                        s18 = HUtil32.GetValidStr3(s18, ref s38, new[] { " ", "\t" });
                        if (!string.IsNullOrEmpty(s20) && !string.IsNullOrEmpty(s28) && !string.IsNullOrEmpty(s38))
                        {
                            NPC = null;
                            switch (HUtil32.Str_ToInt(s24, 0))
                            {
                                case 0:
                                    NPC = new Merchant();
                                    break;
                                case 1:
                                    NPC = new TGuildOfficial();
                                    break;
                                case 2:
                                    NPC = new CastleOfficial();
                                    break;
                            }
                            if (NPC != null)
                            {
                                NPC.m_sMapName = s28;
                                NPC.m_nCurrX = (short)HUtil32.Str_ToInt(s2C, 0);
                                NPC.m_nCurrY = (short)HUtil32.Str_ToInt(s30, 0);
                                NPC.m_sCharName = s20;
                                NPC.m_nFlag = (short)HUtil32.Str_ToInt(s34, 0);
                                NPC.m_wAppr = (ushort)HUtil32.Str_ToInt(s38, 0);
                                M2Share.UserEngine.TryAddQuestNpcExact(NPC);

                            }
                        }
                    }
                }
            }
        }

        public void LoadStartPoint()
        {
            var tStr = string.Empty;
            var s18 = string.Empty;
            var s1C = string.Empty;
            var s20 = string.Empty;
            var s22 = string.Empty;
            var s24 = string.Empty;
            var s26 = string.Empty;
            var s28 = string.Empty;
            var s30 = string.Empty;
            TStartPoint StartPoint;
            var sFileName = Path.Combine(M2Share.sConfigPath, M2Share.g_Config.sEnvirDir, "StartPoint.txt");
            if (File.Exists(sFileName))
            {
                M2Share.StartPointList.Clear();
                using var LoadList = new StringList();
                LoadList.LoadFromFile(sFileName);
                for (var i = 0; i < LoadList.Count; i++)
                {
                    tStr = LoadList[i].Trim();
                    if (!string.IsNullOrEmpty(tStr) && tStr[0] != ';')
                    {
                        tStr = HUtil32.GetValidStr3(tStr, ref s18, new[] { " ", "\t" });
                        tStr = HUtil32.GetValidStr3(tStr, ref s1C, new[] { " ", "\t" });
                        tStr = HUtil32.GetValidStr3(tStr, ref s20, new[] { " ", "\t" });
                        tStr = HUtil32.GetValidStr3(tStr, ref s22, new[] { " ", "\t" });
                        tStr = HUtil32.GetValidStr3(tStr, ref s24, new[] { " ", "\t" });
                        tStr = HUtil32.GetValidStr3(tStr, ref s26, new[] { " ", "\t" });
                        tStr = HUtil32.GetValidStr3(tStr, ref s28, new[] { " ", "\t" });
                        tStr = HUtil32.GetValidStr3(tStr, ref s30, new[] { " ", "\t" });
                        if (s18 != "" && !string.IsNullOrEmpty(s1C) && s20 != "")
                        {
                            StartPoint = new TStartPoint
                            {
                                m_sMapName = s18,
                                m_nCurrX = (short)HUtil32.Str_ToInt(s1C, 0),
                                m_nCurrY = (short)HUtil32.Str_ToInt(s20, 0),
                                m_boNotAllowSay = Convert.ToBoolean(HUtil32.Str_ToInt(s22, 0)),
                                m_nRange = HUtil32.Str_ToInt(s24, 0),
                                m_nType = HUtil32.Str_ToInt(s26, 0),
                                m_nPkZone = HUtil32.Str_ToInt(s28, 0),
                                m_nPkFire = HUtil32.Str_ToInt(s30, 0)
                            };
                            M2Share.StartPointList.Add(StartPoint);
                        }
                    }
                }
            }
        }

        public int LoadSafeZone()
        {
            var result = 0;
            var sFileName = Path.Combine(M2Share.sConfigPath, M2Share.g_Config.sEnvirDir, "SafeZone.txt");
            M2Share.SafeZoneList.Clear();
            if (!File.Exists(sFileName))
            {
                return result;
            }

            using var loadList = new StringList();
            loadList.LoadFromFile(sFileName);
            for (var i = 0; i < loadList.Count; i++)
            {
                var line = loadList[i].Trim();
                if (string.IsNullOrEmpty(line) || line[0] == ';')
                {
                    continue;
                }

                var parts = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length < 4)
                {
                    continue;
                }

                var area = new TSafeZoneArea { MapName = parts[0] };
                for (var partIndex = 1; partIndex < parts.Length; partIndex++)
                {
                    var xy = parts[partIndex].Split('|');
                    if (xy.Length != 2)
                    {
                        continue;
                    }

                    var x = HUtil32.Str_ToInt(xy[0], int.MinValue);
                    var y = HUtil32.Str_ToInt(xy[1], int.MinValue);
                    if (x == int.MinValue || y == int.MinValue)
                    {
                        continue;
                    }
                    area.Points.Add((x, y));
                }

                if (area.Points.Count >= 3)
                {
                    M2Share.SafeZoneList.Add(area);
                    result++;
                }
            }
            return result;
        }

        public int LoadUnbindList()
        {
            var result = 0;
            var tStr = string.Empty;
            var sData = string.Empty;
            var sItemName = string.Empty;
            int n10;
            var sFileName = Path.Combine(M2Share.sConfigPath, M2Share.g_Config.sEnvirDir, "UnbindList.txt");
            if (File.Exists(sFileName))
            {
                using var LoadList = new StringList();
                LoadList.LoadFromFile(sFileName);
                for (var i = 0; i < LoadList.Count; i++)
                {
                    tStr = LoadList[i];
                    if (!string.IsNullOrEmpty(tStr) && tStr[0] != ';')
                    {
                        tStr = HUtil32.GetValidStr3(tStr, ref sData, new[] { " ", "\t" });
                        tStr = HUtil32.GetValidStrCap(tStr, ref sItemName, new[] { " ", "\t" });
                        if (!string.IsNullOrEmpty(sItemName) && sItemName[0] == '\"')
                        {
                            HUtil32.ArrestStringEx(sItemName, "\"", "\"", ref sItemName);
                        }
                        n10 = HUtil32.Str_ToInt(sData, 0);
                        if (n10 > 0)
                        {
                            if (M2Share.g_UnbindList.ContainsKey(n10))
                            {
                                Console.WriteLine("Duplicate unbind item[{0}]...", sItemName);
                                continue;
                            }
                            M2Share.g_UnbindList.Add(n10, sItemName);
                        }
                        else
                        {
                            result = -i;// need to negate
                            break;
                        }
                    }
                }
            }
            return result;
        }

        public void ReLoadNpc()
        {

        }

        public void ReLoadMerchants()
        {
            int nX;
            int nY;
            var sScript = string.Empty;
            var sMapName = string.Empty;
            var sX = string.Empty;
            var sY = string.Empty;
            var sCharName = string.Empty;
            var sFlag = string.Empty;
            var sAppr = string.Empty;
            var sCastle = string.Empty;
            var sCanMove = string.Empty;
            var sMoveTime = string.Empty;
            Merchant Merchant;
            bool boNewNpc;
            var sFileName = Path.Combine(M2Share.sConfigPath, M2Share.g_Config.sEnvirDir, "Merchant.txt");
            if (!File.Exists(sFileName))
            {
                return;
            }
            var merchants = M2Share.UserEngine.SnapshotMerchants();
            for (var i = 0; i < merchants.Length; i++)
            {
                Merchant = merchants[i];
                if (Merchant != M2Share.g_FunctionNPC)
                {
                    Merchant.m_nFlag = -1;
                }
            }
            using var LoadList = new StringList();
            LoadList.LoadFromFile(sFileName);
            for (var i = 0; i < LoadList.Count; i++)
            {
                var sLineText = LoadList[i].Trim();
                if (sLineText != "" && sLineText[0] != ';')
                {
                    sLineText = HUtil32.GetValidStr3(sLineText, ref sScript, new[] { " ", "\t" });
                    sLineText = HUtil32.GetValidStr3(sLineText, ref sMapName, new[] { " ", "\t" });
                    sLineText = HUtil32.GetValidStr3(sLineText, ref sX, new[] { " ", "\t" });
                    sLineText = HUtil32.GetValidStr3(sLineText, ref sY, new[] { " ", "\t" });
                    sLineText = HUtil32.GetValidStr3(sLineText, ref sCharName, new[] { " ", "\t" });
                    if (sCharName != "" && sCharName[0] == '\"')
                    {
                        HUtil32.ArrestStringEx(sCharName, '\"', '\"', ref sCharName);
                    }
                    sLineText = HUtil32.GetValidStr3(sLineText, ref sFlag, new[] { " ", "\t" });
                    sLineText = HUtil32.GetValidStr3(sLineText, ref sAppr, new[] { " ", "\t" });
                    sLineText = HUtil32.GetValidStr3(sLineText, ref sCastle, new[] { " ", "\t" });
                    sLineText = HUtil32.GetValidStr3(sLineText, ref sCanMove, new[] { " ", "\t" });
                    sLineText = HUtil32.GetValidStr3(sLineText, ref sMoveTime, new[] { " ", "\t" });
                    nX = HUtil32.Str_ToInt(sX, 0);
                    nY = HUtil32.Str_ToInt(sY, 0);
                    boNewNpc = true;
                    merchants = M2Share.UserEngine.SnapshotMerchants();
                    for (var j = 0; j < merchants.Length; j++)
                    {
                        Merchant = merchants[j];
                        if (Merchant.m_sMapName == sMapName && Merchant.m_nCurrX == nX && Merchant.m_nCurrY == nY)
                        {
                            boNewNpc = false;
                            Merchant.m_sScript = sScript;
                            Merchant.m_sCharName = sCharName;
                            Merchant.m_nFlag = (short)HUtil32.Str_ToInt(sFlag, 0);
                            Merchant.m_wAppr = (ushort)HUtil32.Str_ToInt(sAppr, 0);
                            Merchant.m_dwMoveTime = HUtil32.Str_ToInt(sMoveTime, 0);
                            if (HUtil32.Str_ToInt(sCastle, 0) != 1)
                            {
                                Merchant.m_boCastle = true;
                            }
                            else
                            {
                                Merchant.m_boCastle = false;
                            }
                            if (HUtil32.Str_ToInt(sCanMove, 0) != 0 && Merchant.m_dwMoveTime > 0)
                            {
                                Merchant.m_boCanMove = true;
                            }
                            break;
                        }
                    }
                    if (boNewNpc)
                    {
                        Merchant = new Merchant
                        {
                            m_sMapName = sMapName
                        };
                        Merchant.m_PEnvir = M2Share.MapManager.FindMap(Merchant.m_sMapName);
                        if (Merchant.m_PEnvir != null)
                        {
                            Merchant.m_sScript = sScript;
                            Merchant.m_nCurrX = (short)nX;
                            Merchant.m_nCurrY = (short)nY;
                            Merchant.m_sCharName = sCharName;
                            Merchant.m_nFlag = (short)HUtil32.Str_ToInt(sFlag, 0);
                            Merchant.m_wAppr = (ushort)HUtil32.Str_ToInt(sAppr, 0);
                            Merchant.m_dwMoveTime = HUtil32.Str_ToInt(sMoveTime, 0);
                            if (HUtil32.Str_ToInt(sCastle, 0) != 1)
                            {
                                Merchant.m_boCastle = true;
                            }
                            else
                            {
                                Merchant.m_boCastle = false;
                            }
                            if (HUtil32.Str_ToInt(sCanMove, 0) != 0 && Merchant.m_dwMoveTime > 0)
                            {
                                Merchant.m_boCanMove = true;
                            }
                            M2Share.UserEngine.TryAddMerchantExact(Merchant);
                            Merchant.Initialize();
                        }
                    }
                }
            }
            merchants = M2Share.UserEngine.SnapshotMerchants();
            for (var i = merchants.Length - 1; i >= 0; i--)
            {
                Merchant = merchants[i];
                if (Merchant.m_nFlag == -1)
                {
                    Merchant.m_boGhost = true;
                    Merchant.m_dwGhostTick = HUtil32.GetTickCount();
                    M2Share.UserEngine.TryRemoveMerchantExact(Merchant);
                }
            }
        }

    }
}
