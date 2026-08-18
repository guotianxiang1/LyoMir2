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
            var mapInfoExFile = Path.Combine(M2Share.sConfigPath,
                M2Share.g_Config.sEnvirDir, "MapInfoEx.txt");
            M2Share.MapManager?.LoadNativeMapInfoEx(mapInfoExFile);
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
                            // 战神 sub_776008 @0x776081-0x7760CA (配置解析器 B) 与
                            // sub_774D98 @0x774DD1-0x774E3A (GM 解析器 A) 同形：
                            //   0x776081  mov ecx,4 / edx="SAFE"(0x776B20) / call 0x4C6E94
                            //             —— **长度 4 前缀**比较(大小写不敏感, UpCase 0x4034D4)，非全等。
                            //   0x77608A  C6 43 5C 01              mov byte [ebx+0x5C],1
                            //   0x7760A3  call 0x4C6964            取 "(...)" 括号参数
                            //   0x7760AB  edx="NOTHROUGH"(0x776B48)
                            //   0x7760B0  call 0x40591C           **大小写敏感**全等(无 UpCase)
                            //   0x7760B7  C6 83 84 00 00 00 01    mov byte [ebx+0x84],1  (参数==NOTHROUGH)
                            //   0x7760C3  C6 83 84 00 00 00 00    mov byte [ebx+0x84],0  (否则)
                            // NOTHROUGH 是 SAFE 的括号参数，不是独立 token；+0x84=boNOTHROUGH。
                            if (HUtil32.CompareLStr(s34, "SAFE", "SAFE".Length))
                            {
                                MapFlag.boSAFE = true;
                                var safeArg = string.Empty;
                                HUtil32.ArrestStringEx(s34, '(', ')', ref safeArg);
                                MapFlag.boNOTHROUGH =
                                    safeArg.Equals("NOTHROUGH", StringComparison.Ordinal);
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
                            // MFLG-24: DROPTOMAP(destMap) -> byte[+0x65]=1 + AnsiString[+0x9c]=destMap。
                            // 与 NORECONNECT 同形（前缀 len9 比较 + 取括号参数 + 空参 result=负数），
                            // 故内联于此以复用 result 写入；原生 token 序也是 15:NORECONNECT→16:DROPTOMAP。
                            //   配置 B 0x7762B4: mov ecx,9 / edx="DROPTOMAP"(0x776C2C) / call 0x4C6E94(前缀)
                            //     0x7762C5 mov byte [ebx+0x65],1
                            //     0x7762DE call 0x4C6964            取 "(...)" 目标图名
                            //     0x7762E3 lea eax,[ebx+0x9c] / 0x7762EC call 0x405554  存字符串
                            //     0x7762F1 cmp dword [ebx+0x9c],0 / jne done
                            //     0x7762FE mov esi,0xFFFFFFF4       空参数 -> result = -12
                            //   GM A 0x7750D7 同形（空参 0x775124 [ebp-4]=-12；取消臂清空 [+0x9c]）。
                            // 效果层消费者已闭合：sub_778EC0 @0x778F75..0x778F8E
                            // 在落格第一遍发现非 landing 的 type-1 event 时读取
                            // +0x65/+0x9c，并调用 sub_768C7C 随机移入目标地图。
                            if (HUtil32.CompareLStr(s34, "DROPTOMAP", "DROPTOMAP".Length))
                            {
                                MapFlag.boDROPTOMAP = true;
                                HUtil32.ArrestStringEx(s34, '(', ')', ref s38);
                                MapFlag.sDropToMap = s38;
                                if (MapFlag.sDropToMap == "")
                                {
                                    result = -12;
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
                            // LimitItemMove 是一个 token 写四个字节的复合开关，
                            // 四个偏移里三个与别的 token 共享：
                            //   0x775A5C  C6 43 67 01  mov byte [ebx+0x67],1  ; NORECALL
                            //   0x775A60  C6 43 68 01  mov byte [ebx+0x68],1  ; NORANDOMMOVE
                            //   0x775A64  C6 43 6B 01  mov byte [ebx+0x6B],1  ; NOPOSITIONMOVE
                            //   0x775A68  C6 43 6C 01  mov byte [ebx+0x6C],1  ; 自有字节
                            //   0x775A79/7D/81/85     同四址写 0（GM toggle=0 臂）
                            //   parser B 0x7769E8/EC/F0/F4 同样四址置 1
                            // 四路联动已经在 3d23493 落地；这里补最后一处差异：
                            // 原版用的是**长度 13 的前缀比较**，不是全等——
                            //   0x7769D2  B9 0D 00 00 00  mov ecx,0xD
                            //   0x7769D7  BA 2C 6F 77 00  mov edx,0x776F2C  ; "LimitItemMove"
                            //   0x7769DF  E8 B0 04 D5 FF  call 0x4C6E94
                            // 与 parser A 的 0x775A42/47/4E 同形。
                            if (HUtil32.CompareLStr(s34, "LimitItemMove", "LimitItemMove".Length))
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
                            // 解析器 A 的同形簇在 0x77523D：
                            //   0x77523D  B9 04 00 00 00     mov ecx,4
                            //   0x775242  BA 74 5D 77 00     mov edx,0x775D74   ; "MINE"
                            //   0x775249  E8 46 1C D5 FF     call 0x4C6E94
                            //   0x775257  C6 43 6A 01        mov byte [ebx+0x6A],1
                            //   0x775268  C6 43 6A 00        同址写 0（GM toggle=0 臂）
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
                            //
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
                            //
                            // 上面这份名单已由本分支独立复扫一遍复核通过（六种形态：
                            // Delphi 记录 FFFFFFFF+len32+chars+NUL / len32+chars+NUL /
                            // len8+chars+NUL / chars+NUL / 裸串 / UTF-16LE，全部
                            // 大小写不敏感）：26 个里 25 个六形态皆 0；EXPRATE 的
                            // 3 处裸命中经逐字节展开确认全是子串——
                            //   0x6AD5F6  10 'MultiTempExpRate'      (ShortString len 0x10)
                            //   0x72C759  FF FF FF FF 10 00 00 00 'MultiTempExpRate'
                            //   0x7D0618  0A 'MonExpRate'            (ShortString len 0x0A)
                            // 同一扫描器对真 token 的对照组各命中恰好 2 条 Delphi
                            // 记录（MINE 0x775D74/0x776CA4、pickup 0x775FCC/0x776F44、
                            // NORECALL 0x775D38/0x776C68、LimitItemMove 0x775FB4/
                            // 0x776F2C、LIMITHEROLEVEL 0x775F10/0x776E40、
                            // UNIFIEDLEVEL 0x775EDC/0x776E0C、LIMITPLAYERLEVEL
                            // 0x775EF4/0x776E24、MapSign 0x775E04/0x776D44、
                            // MAPFIREWALLBURN 0x775DEC/0x776D2C、FLYDROPITEM
                            // 0x775E14/0x776D54），NORIDE 命中 1 条（池 B 独有
                            // 0x776E8C）——与「池 A / 池 B 各一份」的结构吻合，
                            // 说明 0 命中是真缺席而不是扫描器坏了。
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
                var nativeMapDescriptionDirectory = Path.Combine(
                    M2Share.sRootPath, "Share", "config");
                if (!M2Share.MapManager.TryLoadNativeMapAreas(Path.Combine(
                        nativeMapDescriptionDirectory, "maparea.txt"),
                        out var mapAreaError))
                {
                    M2Share.ErrorMessage("maparea.txt load failed: " +
                                         mapAreaError);
                }
                if (!M2Share.MapManager.TryLoadNativeMapDescriptions(Path.Combine(
                        nativeMapDescriptionDirectory, "MapDesc.dat"),
                        out var mapDescriptionError))
                {
                    M2Share.ErrorMessage("MapDesc.dat load failed: " +
                                         mapDescriptionError);
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
            // MFLG-24: 战神 map flag census.  NOTHROUGH 不是独立 token —— 它是 SAFE 的
            // 括号参数 SAFE(NOTHROUGH)，已在 LoadMapInfo 的 SAFE 臂里解析(写 +0x84)。
            // 两个解析器 (sub_774D98 / sub_776008) 都没有独立的 NOTHROUGH 臂 (整幅镜像
            // 只在 SAFE 臂内的 0x774E04 / 0x7760B0 出现一次)，之前把它当顶层 token 是
            // 发明的；移除以对齐原版(原版对独立 NOTHROUGH 静默忽略)。DARE / MONATTACK
            // 则是真的独立 token (0x774F5C→[+5] / 0x774F8B→[+0x90])，保留。
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
            // MFLG-27: 数值型地图标记。原版这一族的形状完全一致——
            //   mov ecx,<literal len> / mov edx,<literal> / call 0x4C6E94  ; 前缀比较
            //   push &out2 / push &out1 / mov ecx,')' / mov edx,'(' /
            //   call 0x4C6964                                             ; 取括号参数
            //   xor edx,edx / call 0x40CA18                               ; StrToIntDef(...,0)
            //   mov word/dword [ebx+off], ax/eax
            // 注意比较器必须是**前缀**：token 串本身带着 "(30)" 后缀，
            // 全等比较对 LIMITHEROLEVEL(30) 一次都匹配不上——所以旧代码
            // 不只是丢了阈值，带参数的写法根本没被识别过。
            //   LIMITHEROLEVEL   len 14  +0xC0 word
            //     A 0x775831 mov edx,0x775F10 / 0x775838 call 0x4C6E94
            //       0x775869 66 89 83 C0 00 00 00  mov word [ebx+0xC0],ax
            //       0x77587D                       mov word [ebx+0xC0],0   (toggle=0)
            //     B 0x77682C 66 89 83 C0 00 00 00  mov word [ebx+0xC0],ax
            //     读取点 0x690315 cmp word[edx+0xC0],0 / jbe 跳过；
            //            0x690339 cmp cx,word[edx+0xC0] / jbe 跳过；
            //            0x690342 mov cx,word[edx+0xC0]  ——数值比较无疑。
            if (HUtil32.CompareLStr(token, "LIMITHEROLEVEL", "LIMITHEROLEVEL".Length))
            {
                var value = string.Empty;
                HUtil32.ArrestStringEx(token, '(', ')', ref value);
                mapFlag.LimitHeroLevel = unchecked((ushort)HUtil32.Str_ToInt(value, 0));
                return true;
            }
            //   LIMITPLAYERLEVEL len 16  +0xBE word
            //     A 0x77580A 66 89 83 BE 00 00 00 / 0x77581E 写 0
            //     B 0x7767E6 66 89 83 BE 00 00 00
            //     读取点 0x69032C cmp cx,word[edx+0xBE]（与 +0xC0 同一函数）
            if (HUtil32.CompareLStr(token, "LIMITPLAYERLEVEL", "LIMITPLAYERLEVEL".Length))
            {
                var value = string.Empty;
                HUtil32.ArrestStringEx(token, '(', ')', ref value);
                mapFlag.LimitPlayerLevel = unchecked((ushort)HUtil32.Str_ToInt(value, 0));
                return true;
            }
            //   UNIFIEDLEVEL     len 12  +0xBC word
            //     A 0x7757AB 66 89 83 BC 00 00 00 / 0x7757BF 写 0
            //     B 0x7767A0 66 89 83 BC 00 00 00
            if (HUtil32.CompareLStr(token, "UNIFIEDLEVEL", "UNIFIEDLEVEL".Length))
            {
                var value = string.Empty;
                HUtil32.ArrestStringEx(token, '(', ')', ref value);
                mapFlag.UnifiedLevel = unchecked((ushort)HUtil32.Str_ToInt(value, 0));
                return true;
            }
            //   MapSign          len 7   +0x62 word
            //     A 0x775407 66 89 43 62 / 0x775418 写 0
            //     B 0x776514 66 89 43 62
            if (HUtil32.CompareLStr(token, "MapSign", "MapSign".Length))
            {
                var value = string.Empty;
                HUtil32.ArrestStringEx(token, '(', ')', ref value);
                mapFlag.MapSign = unchecked((ushort)HUtil32.Str_ToInt(value, 0));
                return true;
            }
            //   MAPFIREWALLBURN  len 15  +0x88 dword，**参数要乘 1000**：
            //     A 0x7753A4 69 C0 E8 03 00 00  imul eax,eax,0x3E8
            //       0x7753AA 89 83 88 00 00 00  mov dword [ebx+0x88],eax
            //       0x7753BD xor eax,eax / 0x7753BF 同址写 0（toggle=0）
            //     B 0x7764C9 imul eax,eax,0x3E8 / 0x7764CF mov dword[ebx+0x88],eax
            //   即配置写的是秒，字段存的是毫秒。MFLG 报告漏了这次 imul。
            if (HUtil32.CompareLStr(token, "MAPFIREWALLBURN", "MAPFIREWALLBURN".Length))
            {
                var value = string.Empty;
                HUtil32.ArrestStringEx(token, '(', ')', ref value);
                mapFlag.MapFireWallBurnMs =
                    unchecked(HUtil32.Str_ToInt(value, 0) * 1000);
                return true;
            }
            //   FLYDROPITEM      len 11  +0xB4 —— **不是数值**，是一张字符串表。
            //     B 0x77651D B9 0B 00 00 00 mov ecx,0xB
            //       0x776522 BA 54 6D 77 00 mov edx,0x776D54  ; "FLYDROPITEM"
            //       0x77652A call 0x4C6E94
            //       0x77654C call 0x4C6964                    ; 取 "(...)"
            //       0x776551 cmp dword [ebp-0xC],0 / je 0x7765C2   ; 空参数臂
            //       0x776557 cmp dword [ebx+0xB4],0 / jne 0x776574
            //       0x776560 mov dl,1 / mov eax,[0x49EB3C] / call 0x404660
            //       0x77656C mov [ebx+0xB4],eax                    ; 惰性 new
            //       0x776574 mov eax,[ebx+0xB4] / mov edx,[eax] / 0x77657C
            //                call [edx+0x44]                       ; 已存在则 Clear
            //       0x776581 循环：0x776588 B1 2F mov cl,0x2F ('/') / 0x77658D
            //                call 0x4C6AEC 切分，0x776598 余串写回
            //       0x77659D cmp dword [ebp-0x10],0 / je            ; 空片跳过
            //       0x7765AE call [ecx+0x38]                        ; TStrings.Add
            //       0x7765B4 call 0x4057D0 / test eax,eax / 0x7765BB jg 0x776581
            //                                                       ; do..while Len>0
            //       0x7765C2 空参数：Clear 后 0x7765DA lea eax,[ebx+0xB4] /
            //                0x7765E0 call 0x414C24 (FreeAndNil) -> null
            //     A 0x775452..0x7754C7 同形。
            //   类型不是猜的：classref [0x49EB3C]=0x49EB88，VMT-0x2C=0x49EC20 处
            //   的 ShortString 为 len=14 'TMirStringList'。
            //   表项语义先前被标 BLOCKED（「物品名还是编号？」），消费点已经定案
            //   是**物品名**：sub_77BA38(eax=mapflag, edx=name)
            //       0x77BA59 mov esi,[ebx+0xB4] / test esi,esi / je    ; 无表 -> false
            //       0x77BA67 call [edx+0x14] / test eax,eax / jle      ; Count<=0 -> false
            //       0x77BA71 mov eax,[ebx+0xB4] / 0x77BA79 call [ecx+0x54]  ; IndexOf
            //       0x77BA7C 40 inc eax / 0x77BA7D 0F 9F C0 setg al    ; IndexOf >= 0
            //   其唯一调用点喂进去的就是物品名：0x6B73F9 mov eax,[edi+0x128] /
            //   0x6B73FF cmp dword [eax+0xB4],0 / je 0x6B74A3（存在性门），再
            //   0x6B740C lea edx,[ebp-8] / call 0x784568，而 sub_784568 是
            //   0x784573 mov edx,[ebx+0x1C] / add edx,4，即取物品 StdItem 的 Name。
            //   C# 侧只复刻解析与存储；0x6B73FF 那道门尚无对应实现（NOT WIRED）。
            if (HUtil32.CompareLStr(token, "FLYDROPITEM", "FLYDROPITEM".Length))
            {
                var value = string.Empty;
                HUtil32.ArrestStringEx(token, '(', ')', ref value);
                if (string.IsNullOrEmpty(value))
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
                var remaining = value;
                var piece = string.Empty;
                do
                {
                    remaining = HUtil32.GetValidStr3(remaining, ref piece, "/");
                    if (piece != "")
                    {
                        mapFlag.FlyDropItemNames.Add(piece);
                    }
                } while (remaining.Length > 0);
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
            // PAODIAN (pool-B only, token literal 0x776E68 len 7; MFLG-06 / MOVE-92).
            // 原生解析器 B @0x77685F 识别后调 sub_77BEDC，后者 @0x77BEE2
            // `mov byte [ebx+0x91],1`（set-only，无视入参），并在 [ebx+0x94]==0 时
            // 惰性 new 一个管理器对象（0x77BF04 call 0x77CD18，classref
            // [0x774800]=0x77484C，600000ms 定时器 + 两张列表）存入 +0x94。
            // 这里 1:1 复刻已证实的 +0x91 置位；管理器与其消费者（0x76A077 /
            // 0x772336 / 0x777D6B->0x77CDD0）语义未证，效果层 fail-closed BLOCKED
            // （详见 TMapFlag.boPAODIAN 文档）。不要凭 boPAODIAN 接线消费者。
            if (token.Equals("PAODIAN", StringComparison.OrdinalIgnoreCase))
            {
                mapFlag.boPAODIAN = true;
                return true;
            }
            // GuildPK (pool-B only, token literal 0x776EF0 len 7; MFLG-06 / MOVE-93).
            // 原生解析器 B @0x776969 识别后：0x77697A mov eax,[0x7D660C] / mov eax,[eax]
            // 取全局管理器，0x776981 mov edx,Envir，0x776983 call sub_698484 —— 后者读
            // Envir[+0x44]（图名串），非空则 0x6984B6 call 0x49F128 把 (图名->Envir)
            // 注册进 manager[+0x54] 列表；随后以 0x776F0C '{' / 0x776F00 '}' 为定界符
            // 抽参。原生**不在 Envir 上写任何字段**，且注册表的强制消费者（读
            // manager[+0x54] 施加行会 PK 规则者）未定位。因此无对应 TMapFlag 字段：
            // 识别该真 token（避免与凭空发明 token 混淆、避免落入 'L' 兜底臂），
            // 但效果层 fail-closed BLOCKED —— 不臆造字段与消费者。
            if (HUtil32.CompareLStr(token, "GuildPK", "GuildPK".Length))
            {
                return true;
            }
            // MFLG-24 补全：配置解析器缺失的 7 个 bool 真 token（双解析器均写字段，
            // 非 §INVENTED；DROPTOMAP 因带括号参数 + 空参 result=-12，见 LoadMapInfo 内联臂）。
            // 证据锚见 TMapFlag 各字段文档。比较器按原生逐 token 区分：
            //   前缀 0x4C6E94(带 mov ecx,len，ASCII 大小写不敏感) -> HUtil32.CompareLStr
            //   全等 0x40BD78(无 ecx，大小写不敏感) -> .Equals(OrdinalIgnoreCase)
            // 效果层 BLOCKED 仅指下列 7 个 bool；带参数的 DROPTOMAP 不在此组，
            // 已由 sub_778EC0 等价落格路径消费。

            // UserNoKill -> byte[+0x71]=1（原生另清 word[+0x74]=0；配置解析对全零新 flag 为 no-op）
            //   B 0x7768E6 前缀 len10 / A 0x775938
            if (HUtil32.CompareLStr(token, "UserNoKill", "UserNoKill".Length))
            {
                mapFlag.boUserNoKill = true;
                return true;
            }
            // NOHERO -> byte[+0x6e]=1   B 0x77672D 前缀 len6 / A 0x77570B
            if (HUtil32.CompareLStr(token, "NOHERO", "NOHERO".Length))
            {
                mapFlag.boNOHERO = true;
                return true;
            }
            // DREAMCASTLEMAP -> byte[+0x6f]=1   B 0x77674C 前缀 len14 / A 0x77573F
            if (HUtil32.CompareLStr(token, "DREAMCASTLEMAP", "DREAMCASTLEMAP".Length))
            {
                mapFlag.boDREAMCASTLEMAP = true;
                return true;
            }
            // NEWMJNORMALPRIZE -> byte[+0x78]=1   B 0x77694A 前缀 len16 / A 0x7759DF
            if (HUtil32.CompareLStr(token, "NEWMJNORMALPRIZE", "NEWMJNORMALPRIZE".Length))
            {
                mapFlag.boNEWMJNORMALPRIZE = true;
                return true;
            }
            // MINGJIANG -> byte[+0x7a]=1   B 0x776407 全等 0x40BD78 / A 0x7752A5
            if (token.Equals("MINGJIANG", StringComparison.OrdinalIgnoreCase))
            {
                mapFlag.boMINGJIANG = true;
                return true;
            }
            // HACKQUEST -> byte[+0x7b]=1   B 0x776421 全等 0x40BD78 / A 0x7752D4
            if (token.Equals("HACKQUEST", StringComparison.OrdinalIgnoreCase))
            {
                mapFlag.boHACKQUEST = true;
                return true;
            }
            // NOEXPLORE -> byte[+0x80]=1   B 0x776455 全等 0x40BD78 / A 0x775332
            if (token.Equals("NOEXPLORE", StringComparison.OrdinalIgnoreCase))
            {
                mapFlag.boNOEXPLORE = true;
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
