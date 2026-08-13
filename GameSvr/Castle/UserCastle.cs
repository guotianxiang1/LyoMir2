using System.Globalization;
using SystemModule;
using SystemModule.Common;

namespace GameSvr
{
    public class TUserCastle
    {
        public TObjUnit[] m_Archer = new TObjUnit[12];
        
        
        
        private readonly IList<Association> m_AttackGuildList;
        
        
        
        private readonly IList<TAttackerInfo> m_AttackWarList;
        
        
        
        public bool m_boShowOverMsg;
        
        
        
        public bool m_boStartWar;
        public bool m_boUnderWar;
        public bool m_boForceWar;
        public TObjUnit m_CenterWall;
        public DateTime m_ChangeDate;
        
        
        
        public TDoorStatus m_DoorStatus;
        
        
        
        private int m_dwSaveTick;
        private int m_dwRunTick;
        public int m_dwStartCastleWarTick;
        public IList<string> m_EnvirList;
        public TObjUnit[] m_Guard = new TObjUnit[4];
        public DateTime m_IncomeToday;
        public TObjUnit m_LeftWall;
        public TObjUnit m_MainDoor;
        
        
        
        public Envirnoment m_MapCastle;
        
        
        
        public Envirnoment m_MapPalace;
        private Envirnoment m_MapSecret;
        
        
        
        public Association m_MasterGuild;
        
        
        
        public int m_nHomeX;
        
        
        
        public int m_nHomeY;
        public int m_nPalaceDoorX;
        
        
        
        public int m_nPalaceDoorY;
        private int m_nPower;
        private int m_nTechLevel;
        
        
        
        public int m_nTodayIncome;
        
        
        
        public int m_nTotalGold;
        public int m_nWarRangeX;
        public int m_nWarRangeY;
        
        
        
        public TObjUnit m_RightWall;
        
        
        
        public string m_sConfigDir = string.Empty;
        
        
        
        public string m_sHomeMap = string.Empty;
        
        
        
        public string m_sMapName = string.Empty;
        
        
        
        public string m_sName = string.Empty;
        public string m_sOwnGuild = string.Empty;
        
        
        
        public string m_sPalaceMap = string.Empty;
        public string m_sSecretMap = string.Empty;
        public DateTime m_WarDate;
        private readonly CastleConfManager castleConf;
        
        
        
        const string AttackSabukWallList = "AttackSabukWall.txt";
        
        
        
        private const string SabukWFileName = "SabukW.txt";

        public TUserCastle(string sCastleDir)
        {
            m_MasterGuild = null;
            m_sHomeMap = M2Share.g_Config.sCastleHomeMap; 
            m_nHomeX = M2Share.g_Config.nCastleHomeX; 
            m_nHomeY = M2Share.g_Config.nCastleHomeY; 
            m_sName = M2Share.g_Config.sCastleName; 
            m_sConfigDir = sCastleDir;
            m_sPalaceMap = "0150";
            m_sSecretMap = "D701";
            m_MapCastle = null;
            m_DoorStatus = null;
            m_boStartWar = false;
            m_boUnderWar = false;
            m_boForceWar = false;
            m_boShowOverMsg = false;
            m_dwRunTick = 0;
            m_AttackWarList = new List<TAttackerInfo>();
            m_AttackGuildList = new List<Association>();
            m_dwSaveTick = 0;
            m_nWarRangeX = M2Share.g_Config.nCastleWarRangeX;
            m_nWarRangeY = M2Share.g_Config.nCastleWarRangeY;
            m_EnvirList = new List<string>();
            var filePath = Path.Combine(M2Share.sConfigPath, M2Share.g_Config.sCastleDir, m_sConfigDir);
            if (!Directory.Exists(filePath))
            {
                Directory.CreateDirectory(filePath);
            }
            castleConf = new CastleConfManager(Path.Combine(filePath, SabukWFileName));
        }

        public int nTechLevel
        {
            get { return m_nTechLevel; }
            set { SetTechLevel(value); }
        }

        public int nPower
        {
            get { return m_nPower; }
            set { SetPower(value); }
        }

        public void Initialize()
        {
            TObjUnit ObjUnit;
            TDoorInfo Door;
            LoadConfig();
            LoadAttackSabukWall();
            if (M2Share.MapManager.GetMapOfServerIndex(m_sMapName) == M2Share.nServerIndex)
            {
                m_MapPalace = M2Share.MapManager.FindMap(m_sPalaceMap);
                if (m_MapPalace == null) M2Share.MainOutMessage($"皇宫地图{m_sPalaceMap}没找到!!!");
                m_MapSecret = M2Share.MapManager.FindMap(m_sSecretMap);
                if (m_MapSecret == null) M2Share.MainOutMessage($"密道地图{m_sSecretMap}没找到!!!");
                m_MapCastle = M2Share.MapManager.FindMap(m_sMapName);
                if (m_MapCastle != null)
                {
                    m_MainDoor.BaseObject = M2Share.UserEngine.RegenMonsterByName(m_MapCastle, m_MainDoor.nX, m_MainDoor.nY, m_MainDoor.sName);
                    if (m_MainDoor.BaseObject != null)
                    {
                        m_MainDoor.BaseObject.m_WAbil.HP = m_MainDoor.nHP;
                        m_MainDoor.BaseObject.m_Castle = this;
                        if (m_MainDoor.nStatus)
                        {
                            ((CastleDoor)m_MainDoor.BaseObject).Open();
                        }
                    }
                    else
                    {
                        M2Share.ErrorMessage("[Error] UserCastle.Initialize MainDoor.UnitObj = nil");
                    }
                    m_LeftWall.BaseObject = M2Share.UserEngine.RegenMonsterByName(m_MapCastle, m_LeftWall.nX, m_LeftWall.nY, m_LeftWall.sName);
                    if (m_LeftWall.BaseObject != null)
                    {
                        m_LeftWall.BaseObject.m_WAbil.HP = m_LeftWall.nHP;
                        m_LeftWall.BaseObject.m_Castle = this;
                    }
                    else
                    {
                        M2Share.ErrorMessage("[错误信息] 城堡初始化城门失败，检查怪物数据库里有没城门的设置: " + m_MainDoor.sName);
                    }
                    m_CenterWall.BaseObject = M2Share.UserEngine.RegenMonsterByName(m_MapCastle, m_CenterWall.nX, m_CenterWall.nY, m_CenterWall.sName);
                    if (m_CenterWall.BaseObject != null)
                    {
                        m_CenterWall.BaseObject.m_WAbil.HP = m_CenterWall.nHP;
                        m_CenterWall.BaseObject.m_Castle = this;
                    }
                    else
                    {
                        M2Share.ErrorMessage("[错误信息] 城堡初始化左城墙失败，检查怪物数据库里有没左城墙的设置: " + m_LeftWall.sName);
                    }
                    m_RightWall.BaseObject = M2Share.UserEngine.RegenMonsterByName(m_MapCastle, m_RightWall.nX, m_RightWall.nY, m_RightWall.sName);
                    if (m_RightWall.BaseObject != null)
                    {
                        m_RightWall.BaseObject.m_WAbil.HP = m_RightWall.nHP;
                        m_RightWall.BaseObject.m_Castle = this;
                    }
                    else
                    {
                        M2Share.ErrorMessage("[错误信息] 城堡初始化中城墙失败，检查怪物数据库里有没中城墙的设置: " + m_CenterWall.sName);
                    }
                    for (var i = m_Archer.GetLowerBound(0); i <= m_Archer.GetUpperBound(0); i++)
                    {
                        ObjUnit = m_Archer[i];
                        if (ObjUnit.nHP <= 0) continue;
                        ObjUnit.BaseObject = M2Share.UserEngine.RegenMonsterByName(m_MapCastle, ObjUnit.nX, ObjUnit.nY, ObjUnit.sName);
                        if (ObjUnit.BaseObject != null)
                        {
                            ObjUnit.BaseObject.m_WAbil.HP = m_Archer[i].nHP;
                            ObjUnit.BaseObject.m_Castle = this;
                            ((GuardUnit)ObjUnit.BaseObject).m_nX550 = ObjUnit.nX;
                            ((GuardUnit)ObjUnit.BaseObject).m_nY554 = ObjUnit.nY;
                            ((GuardUnit)ObjUnit.BaseObject).m_nDirection = 3;
                        }
                        else
                        {
                            // 战神版: 怪物数据库可能没有此名称，跳过
                        }
                    }

                    for (var i = m_Guard.GetLowerBound(0); i <= m_Guard.GetUpperBound(0); i++)
                    {
                        ObjUnit = m_Guard[i];
                        if (ObjUnit.nHP <= 0) continue;
                        ObjUnit.BaseObject = M2Share.UserEngine.RegenMonsterByName(m_MapCastle, ObjUnit.nX, ObjUnit.nY, ObjUnit.sName);
                        if (ObjUnit.BaseObject != null)
                            ObjUnit.BaseObject.m_WAbil.HP = m_Guard[i].nHP;
                        // 战神版: 怪物数据库可能没有此守卫名称，跳过
                    }
                    for (var i = 0; i < m_MapCastle.m_DoorList.Count; i++)
                    {
                        Door = m_MapCastle.m_DoorList[i];
                        if (Math.Abs(Door.nX - m_nPalaceDoorX) <= 3 && Math.Abs(Door.nY - m_nPalaceDoorY) <= 3)
                        {
                            m_DoorStatus = Door.Status;
                        }
                    }
                }
                else
                {
                    M2Share.ErrorMessage($"[错误信息] 城堡所在地图不存在(检查地图配置文件里是否有地图{m_sMapName}的设置)");
                }
            }
        }

        private void LoadConfig()
        {
            castleConf.LoadConfig(this);
            m_MasterGuild = M2Share.GuildManager.FindGuild(m_sOwnGuild);
        }

        private bool SaveConfigFile()
        {
            try
            {
                castleConf.SaveConfig(this);
                return true;
            }
            catch (Exception ex)
            {
                M2Share.ErrorMessage($"保存城堡[{m_sName}]配置失败: {ex.Message}");
                return false;
            }
        }

        
        
        
        private void LoadAttackSabukWall()
        {
            var sabukwallPath = Path.Combine(M2Share.sConfigPath, M2Share.g_Config.sCastleDir, m_sConfigDir);
            if (!Directory.Exists(sabukwallPath))
                Directory.CreateDirectory(sabukwallPath);
            var sFileName = Path.Combine(sabukwallPath, AttackSabukWallList);
            if (!File.Exists(sFileName)) return;
            using var loadList = new StringList();
            try
            {
                loadList.LoadFromFile(sFileName);
                if (loadList.Count < 1) return;

                m_AttackWarList.Clear();
                for (var i = 0; i < loadList.Count; i++)
                {
                    var guildName = string.Empty;
                    var s20 = HUtil32.GetValidStr3(loadList[i], ref guildName, new[] { " ", "\t" });
                    var guild = M2Share.GuildManager.FindGuild(guildName);
                    if (guild == null) continue;

                    HUtil32.ArrestStringEx(s20, '\"', '\"', ref s20);
                    if (!TryParseAttackDate(s20, out var attackDate)) continue;

                    m_AttackWarList.Add(new TAttackerInfo
                    {
                        AttackDate = attackDate,
                        sGuildName = guildName,
                        Guild = guild
                    });
                }
            }
            catch
            {
                M2Share.MainOutMessage("[Error] UserCastle.LoadAttackSabukWall");
            }
        }

        private static bool TryParseAttackDate(string value, out DateTime attackDate)
        {
            var parts = value.Split('-', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 3)
            {
                attackDate = default;
                return false;
            }

            attackDate = new DateTime(
                unchecked((ushort)ParseAttackDatePart(parts[0], 1999)),
                unchecked((ushort)ParseAttackDatePart(parts[1], 1)),
                unchecked((ushort)ParseAttackDatePart(parts[2], 1)));
            return true;
        }

        private static int ParseAttackDatePart(string value, int defaultValue)
        {
            return int.TryParse(value, out var parsed) ? parsed : defaultValue;
        }

        
        
        
        private bool SaveAttackSabukWall()
        {
            try
            {
                var sabukwallPath = Path.Combine(M2Share.sConfigPath, M2Share.g_Config.sCastleDir, m_sConfigDir);
                if (!Directory.Exists(sabukwallPath))
                    Directory.CreateDirectory(sabukwallPath);
                var sFileName = Path.Combine(sabukwallPath, AttackSabukWallList);
                using var loadLis = new StringList();
                for (var i = 0; i < m_AttackWarList.Count; i++)
                {
                    var attackerInfo = m_AttackWarList[i];
                    loadLis.Add(attackerInfo.sGuildName + "       \"" +
                        attackerInfo.AttackDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + '\"');
                }
                AtomicFile.WriteAllText(sFileName, loadLis.Text, HUtil32.GbkEncoding);
                return true;
            }
            catch (Exception ex)
            {
                M2Share.ErrorMessage($"保存城堡[{m_sName}]攻城列表失败: {ex.Message}");
                return false;
            }
        }

        public void Run()
        {
            const string sExceptionMsg = "[Exception] TUserCastle::Run";
            try
            {
                var nowTick = HUtil32.GetTickCount();
                // 0x65BB58 sub eax,[ebx+0x10] / 0x65BB5B cmp eax,0x2710 / jb ret
                if ((nowTick - m_dwRunTick) < 0x2710) return;
                m_dwRunTick = nowTick;
                if (M2Share.nServerIndex != M2Share.MapManager.GetMapOfServerIndex(m_sMapName)) return;
                var now = DateTime.Now;
                var today = now.Date;
                if (m_IncomeToday.Date != today)
                {
                    m_nTodayIncome = 0;
                    m_IncomeToday = now;
                    m_boStartWar = false;
                }
                // 0x49E39C: DecodeTime then hour*3600+min*60+sec. Stored at [ebx+8].
                var timeSec = now.Hour * 3600 + now.Minute * 60 + now.Second;
                if (!m_boStartWar && !m_boUnderWar)
                {
                    // start window [0x11940, 0x12E58) unless +0x2B force skips it
                    if (m_boForceWar || (timeSec >= 0x11940 && timeSec < 0x12E58))
                    {
                        m_boStartWar = true;
                        m_AttackGuildList.Clear();
                        for (var i = m_AttackWarList.Count - 1; i >= 0; i--)
                        {
                            var attackerInfo = m_AttackWarList[i];
                            var attackDay = attackerInfo.AttackDate.Date;
                            if (attackDay == today)
                            {
                                m_AttackGuildList.Add(attackerInfo.Guild);
                            }
                            else if (attackDay < today)
                            {
                                m_AttackWarList.RemoveAt(i);
                            }
                        }
                        if (m_boForceWar || m_AttackGuildList.Count > 0)
                        {
                            m_boUnderWar = true;
                            m_boShowOverMsg = false;
                            m_WarDate = now;
                            m_dwStartCastleWarTick = nowTick;
                        }
                        if (m_boUnderWar)
                        {
                            m_AttackGuildList.Add(m_MasterGuild);
                            StartWallconquestWar();
                            M2Share.UserEngine.SendServerGroupMsg(Grobal2.SS_212, M2Share.nServerIndex, "");
                            // 0x65BE7C len=22, no %s
                            const string sWarStartMsg = "[沙巴克攻城战已经开始]";
                            M2Share.UserEngine.SendBroadCastMsgExt(sWarStartMsg, MsgType.System);
                            M2Share.UserEngine.SendServerGroupMsg(Grobal2.SS_204, M2Share.nServerIndex, sWarStartMsg);
                            M2Share.MainOutMessage(sWarStartMsg);
                            MainDoorControl(true);
                        }
                    }
                }
                for (var i = m_Guard.GetLowerBound(0); i <= m_Guard.GetUpperBound(0); i++)
                {
                    if (m_Guard[i].BaseObject != null && m_Guard[i].BaseObject.m_boGhost)
                    {
                        m_Guard[i].BaseObject = null;
                    }
                }
                for (var i = m_Archer.GetLowerBound(0); i <= m_Archer.GetUpperBound(0); i++)
                {
                    if (m_Archer[i].BaseObject != null && m_Archer[i].BaseObject.m_boGhost)
                    {
                        m_Archer[i].BaseObject = null;
                    }
                }
                if (m_boUnderWar)
                {
                    if (m_LeftWall.BaseObject != null) m_LeftWall.BaseObject.m_boStoneMode = false;
                    if (m_CenterWall.BaseObject != null) m_CenterWall.BaseObject.m_boStoneMode = false;
                    if (m_RightWall.BaseObject != null) m_RightWall.BaseObject.m_boStoneMode = false;
                    if (!m_boShowOverMsg && timeSec >= 0x12C00)
                    {
                        m_boShowOverMsg = true;
                        // 0x65BE9C len=33, hardcoded 10 minutes
                        const string sWarStopTimeMsg = "[沙巴克城攻城战离结束还有10分钟.]";
                        M2Share.UserEngine.SendBroadCastMsgExt(sWarStopTimeMsg, MsgType.System);
                        M2Share.UserEngine.SendServerGroupMsg(Grobal2.SS_204, M2Share.nServerIndex, sWarStopTimeMsg);
                        M2Share.MainOutMessage(sWarStopTimeMsg);
                    }
                    // 0x65BE1B cmp [ebx+0x2B],0 / jne skip;
                    // cmp [ebx+8],0x11940 / jb Stop; cmp 0x12E58 / jbe stay
                    if (!m_boForceWar && (timeSec < 0x11940 || timeSec > 0x12E58))
                    {
                        StopWallconquestWar();
                    }
                }
                else
                {
                    if (m_LeftWall.BaseObject != null) m_LeftWall.BaseObject.m_boStoneMode = true;
                    if (m_CenterWall.BaseObject != null) m_CenterWall.BaseObject.m_boStoneMode = true;
                    if (m_RightWall.BaseObject != null) m_RightWall.BaseObject.m_boStoneMode = true;
                }
            }
            catch
            {
                M2Share.ErrorMessage(sExceptionMsg);
            }
        }

        public bool Save()
        {
            var configSaved = SaveConfigFile();
            var attackListSaved = SaveAttackSabukWall();
            return configSaved && attackListSaved;
        }

        public bool InCastleWarArea(Envirnoment envir, int nX, int nY)
        {
            if (envir == null)
            {
                return false;
            }
            if (envir == m_MapCastle && Math.Abs(m_nHomeX - nX) < m_nWarRangeX &&
                Math.Abs(m_nHomeY - nY) < m_nWarRangeY) return true;
            if (envir == m_MapPalace || envir == m_MapSecret) return true;
            for (var i = 0; i < m_EnvirList.Count; i++) 
                if (m_EnvirList[i] == envir.sMapName)
                    return true;
            return false;
        }

        public bool IsMember(TBaseObject cert)
        {
            return IsMasterGuild(cert.m_MyGuild);
        }

        
        public bool IsAttackAllyGuild(Association Guild)
        {
            Association AttackGuild;
            var result = false;
            for (var i = 0; i < m_AttackGuildList.Count; i++)
            {
                AttackGuild = m_AttackGuildList[i];
                if (AttackGuild != m_MasterGuild && AttackGuild.IsAllyGuild(Guild))
                {
                    result = true;
                    break;
                }
            }
            return result;
        }

        
        public bool IsAttackGuild(Association Guild)
        {
            Association AttackGuild;
            var result = false;
            for (var i = 0; i < m_AttackGuildList.Count; i++)
            {
                AttackGuild = m_AttackGuildList[i];
                if (AttackGuild != m_MasterGuild && AttackGuild == Guild)
                {
                    result = true;
                    break;
                }
            }
            return result;
        }

        public bool CanGetCastle(Association guild)
        {
            if ((HUtil32.GetTickCount() - m_dwStartCastleWarTick) <= M2Share.g_Config.dwGetCastleTime)
            {
                return false;
            }
            var playPbjectList = new List<TBaseObject>();
            M2Share.UserEngine.GetMapRageHuman(m_MapPalace, 0, 0, 1000, playPbjectList);
            var result = true;
            for (var i = 0; i < playPbjectList.Count; i++)
            {
                var playObject = (TPlayObject)playPbjectList[i];
                if (!playObject.m_boDeath && playObject.m_MyGuild != guild)
                {
                    result = false;
                    break;
                }
            }
            playPbjectList = null;
            return result;
        }

        public void GetCastle(Association Guild)
        {
            const string sGetCastleMsg = "[{0} 已被 {1} 占领]";
            var oldGuild = m_MasterGuild;
            var oldOwnGuild = m_sOwnGuild;
            var oldChangeDate = m_ChangeDate;
            m_MasterGuild = Guild;
            m_sOwnGuild = Guild.sGuildName;
            m_ChangeDate = DateTime.Now;
            // 战神 sub_65BEC0 has NO rollback. 0x65A510 (SaveCastleInfo) is a
            // Delphi `procedure` — it never sets eax — and the very next
            // instruction 0x65BF22 `test edi,edi` tests EDI, which was loaded with
            // the OLD guild back at 0x65BEF4 `mov edi,[ebx+0x48]`, i.e. it is the
            // `if oldGuild <> nil` guard below, NOT a save-result check. A failed
            // save therefore leaves the new owner in place; reverting the three
            // fields is a C# invention that would hand the castle back on any
            // transient disk error.
            SaveConfigFile();
            if (oldGuild != null)
            {
                for (var i = m_AttackWarList.Count - 1; i >= 0; i--)
                {
                    var attackerInfo = m_AttackWarList[i];
                    if (attackerInfo.Guild != Guild)
                    {
                        continue;
                    }
                    attackerInfo.Guild = oldGuild;
                    attackerInfo.sGuildName = oldGuild.sGuildName;
                    break;
                }
                oldGuild.RefMemberName();
                // 0x65BF80 call 0x65A3B8 is SAVE, not load.
                // 0x65A3B8 walks [ebx+0x8C], formats '       "'+YYYY-MM-DD+'"\r\n'
                // (0x65A4C8 / 0x65A4DC / 0x65A4F0) and writes AttackSabukWall.txt.
                // The loader is 0x65B22C (FileExists + TStringList + 0x65C908 parse),
                // xref only from init 0x65AAD6. StopWall also saves via 0x65C1AC.
                SaveAttackSabukWall();
            }
            m_MasterGuild.RefMemberName();//刷新新的行会信息
            var s10 = string.Format(sGetCastleMsg, m_sName, m_sOwnGuild);
            M2Share.UserEngine.SendBroadCastMsgExt(s10, MsgType.System);
            M2Share.UserEngine.SendServerGroupMsg(Grobal2.SS_204, M2Share.nServerIndex, s10);
            // 已按战神二进制点验删除 AllGetCastle()（2026-08-03，Tier-1 证据）：
            // 战神易主例程 sub_65BEC0 只设主行会/名称/时间戳 + 刷新 m_AttackWarList([+0x8C]，
            // C# 上方 580-590 已做) + 广播，【无在线玩家循环、无脚本调用】。决定性证据：全段扫描
            // "@GetCastFunc" 出现 0 次（"@GetCastle"/"GetCastFunc" 同为 0；32 处 GetCastle 命中
            // 全是 PAS API 声明）——Delphi GotoLable 必须内嵌字面量，故战神绝无可能执行该脚本。
            // 证据：staging/adjudicate_3_disputed_20260802.md（含反汇编片段）。
            M2Share.MainOutMessage(s10);
        }

        public void StartWallconquestWar()
        {
            TPlayObject PlayObject;
            var ListC = new List<TBaseObject>();
            M2Share.UserEngine.GetMapRageHuman(m_MapPalace, m_nHomeX, m_nHomeY, 100, ListC);
            for (var i = 0; i < ListC.Count; i++)
            {
                PlayObject = (TPlayObject)ListC[i];
                PlayObject.RefShowName();
            }
        }

        
        
        
        public void StopWallconquestWar()
        {
            TPlayObject PlayObject;
            const string sWallWarStop = "[{0} 攻城战已经结束]";
            m_boUnderWar = false;
            m_AttackGuildList.Clear();
            // ✅ 已按战神二进制点验为【忠实】(2026-08-03, Tier-1)：战神 sub_65C080(持有 GBK
            // "[沙巴克攻城战已经结束]" @dword_65C354) 清战争状态字节 + 清攻方行会表后，于
            // 0x65C0DD 调 sub_6526E4(=GetMapRageHuman) 范围 0x64=100，循环调 sub_6B6B78(player,0)
            // (=ChangePKStatus(false))，并在 sub_6ADAE4(player)!=[+0x48](行会≠主行会) 时调
            // sub_768C7C(player, homeMap)(=MapRandomMove)，最后广播。四个被调者身份均已确认。
            // 下面这段与之逐条对应，勿删。证据：staging/adjudicate_3_disputed_20260802.md。
            var ListC = new List<TBaseObject>();
            M2Share.UserEngine.GetMapRageHuman(m_MapCastle, m_nHomeX, m_nHomeY, 100, ListC);
            for (var i = 0; i < ListC.Count; i++)
            {
                PlayObject = ListC[i] as TPlayObject;
                if (PlayObject == null) continue;
                PlayObject.ChangePKStatus(false);
                if (PlayObject.m_MyGuild != m_MasterGuild)
                {
                    PlayObject.MapRandomMove(PlayObject.m_sHomeMap, 0);
                }
            }
            var s14 = string.Format(sWallWarStop, m_sName);
            M2Share.UserEngine.SendBroadCastMsgExt(s14, MsgType.System);
            M2Share.UserEngine.SendServerGroupMsg(Grobal2.SS_204, M2Share.nServerIndex, s14);
            M2Share.MainOutMessage(s14);
        }

        public int InPalaceGuildCount()
        {
            return m_AttackGuildList.Count;
        }

        public bool IsDefenseAllyGuild(Association guild)
        {
            if (!m_boUnderWar) return false; 
            return m_MasterGuild != null && m_MasterGuild.IsAllyGuild(guild);
        }

        
        public bool IsDefenseGuild(Association guild)
        {
            if (!m_boUnderWar) return false;// 如果未开始攻城，则无效
            return guild == m_MasterGuild;
        }

        public bool IsMasterGuild(Association guild)
        {
            return m_MasterGuild != null && m_MasterGuild == guild;
        }

        public short GetHomeX()
        {
            return (short)(m_nHomeX - 4 + M2Share.RandomNumber.Random(9));
        }

        public short GetHomeY()
        {
            return (short)(m_nHomeY - 4 + M2Share.RandomNumber.Random(9));
        }

        public string GetMapName()
        {
            return m_sMapName;
        }

        public bool CheckInPalace(int nX, int nY, TBaseObject cert)
        {
            TObjUnit ObjUnit;
            var result = IsMasterGuild(cert.m_MyGuild);
            if (result) return result;
            ObjUnit = m_LeftWall;
            if (ObjUnit.BaseObject != null && ObjUnit.BaseObject.m_boDeath && ObjUnit.BaseObject.m_nCurrX == nX &&
                ObjUnit.BaseObject.m_nCurrY == nY) result = true;
            ObjUnit = m_CenterWall;
            if (ObjUnit.BaseObject != null && ObjUnit.BaseObject.m_boDeath && ObjUnit.BaseObject.m_nCurrX == nX &&
                ObjUnit.BaseObject.m_nCurrY == nY) result = true;
            ObjUnit = m_RightWall;
            if (ObjUnit.BaseObject != null && ObjUnit.BaseObject.m_boDeath && ObjUnit.BaseObject.m_nCurrX == nX &&
                ObjUnit.BaseObject.m_nCurrY == nY) result = true;
            return result;
        }

        public string GetWarDate()
        {
            const string sMsg = "{0}年{1}月{2}日";
            var result = string.Empty;
            if (m_AttackWarList.Count <= 0) return result;
            var AttackerInfo = m_AttackWarList[0];
            var Year = AttackerInfo.AttackDate.Year;
            var Month = AttackerInfo.AttackDate.Month;
            var Day = AttackerInfo.AttackDate.Day;
            return string.Format(sMsg, Year, Month, Day);
        }

        public string GetAttackWarList()
        {
            TAttackerInfo AttackerInfo;
            var result = string.Empty;
            short wYear = 0;
            short wMonth = 0;
            short wDay = 0;
            var n10 = 0;
            for (var i = 0; i < m_AttackWarList.Count; i++)
            {
                AttackerInfo = m_AttackWarList[i];
                var Year = AttackerInfo.AttackDate.Year;
                var Month = AttackerInfo.AttackDate.Month;
                var Day = AttackerInfo.AttackDate.Day;
                if (Year != wYear || Month != wMonth || Day != wDay)
                {
                    wYear = (short)Year;
                    wMonth = (short)Month;
                    wDay = (short)Day;
                    if (result != "") result = result + '\\';
                    result = result + wYear + '年' + wMonth + '月' + wDay + "日\\";
                    n10 = 0;
                }
                if (n10 > 40)
                {
                    result = result + '\\';
                    n10 = 0;
                }
                var s20 = '\"' + AttackerInfo.sGuildName + '\"';
                n10 += s20.Length;
                result = result + s20;
            }
            return result;
        }

        
        
        
        
        public void IncRateGold(int nGold)
        {
            var nInGold = HUtil32.Round(nGold * (M2Share.g_Config.nCastleTaxRate / 100.0));
            if (m_nTodayIncome + nInGold <= M2Share.g_Config.nCastleOneDayGold)
            {
                m_nTodayIncome += nInGold;
            }
            else
            {
                if (m_nTodayIncome >= M2Share.g_Config.nCastleOneDayGold)
                {
                    nInGold = 0;
                }
                else
                {
                    nInGold = M2Share.g_Config.nCastleOneDayGold - m_nTodayIncome;
                    m_nTodayIncome = M2Share.g_Config.nCastleOneDayGold;
                }
            }
            if (nInGold > 0)
            {
                if (m_nTotalGold + nInGold < M2Share.g_Config.nCastleGoldMax)
                    m_nTotalGold += nInGold;
                else
                    m_nTotalGold = M2Share.g_Config.nCastleGoldMax;
            }
            if ((HUtil32.GetTickCount() - m_dwSaveTick) > 10 * 60 * 1000)
            {
                m_dwSaveTick = HUtil32.GetTickCount();
                if (M2Share.g_boGameLogGold)
                    M2Share.AddGameDataLog("23" + "\t" + '0' + "\t" + '0' + "\t" + '0' + "\t" + "autosave" + "\t" +
                                           Grobal2.sSTRING_GOLDNAME + "\t" + m_nTotalGold + "\t" + '1' + "\t" + '0');
            }
        }

        
        
        
        
        
        
        public int WithDrawalGolds(TPlayObject PlayObject, int nGold)
        {
            var result = -1;
            // 0x65B3B5 mov [ebp-4],-1 ; 0x65B3BC test esi,esi / 0x65B3BE jle 0x65B431
            // nGold<=0 returns -1. sub_65B3A8 never writes -4 (0xFFFFFFFC).
            if (nGold <= 0)
            {
                return result;
            }
            if (m_MasterGuild == PlayObject.m_MyGuild && PlayObject.m_nGuildRankNo == 1)
            {
                if (nGold <= m_nTotalGold)
                {
                    // ✅ 战神字节证据 (Tier-1) — ECON-33 / CGLD-10: 原生是【先信用、成功了才扣池】,
                    // 且扣池是 IncGold 返回 TRUE 之后的唯一动作。EA: sub_65B3A8 @0x65B3E2-0x65B3FA:
                    //   0065B3E2  3b b3 80 00 00 00  cmp  esi,[ebx+0x80]     ; 取款额 vs 池
                    //   0065B3E8  7f 40              jg   0x65B42A           ; 超池 -> ret -2 (池不动)
                    //   0065B3EA  8b d6              mov  edx,esi
                    //   0065B3EC  8b c7              mov  eax,edi
                    //   0065B3EE  8b 08              mov  ecx,[eax]
                    //   0065B3F0  ff 91 8c 02 00 00  call dword [ecx+0x28C]  ; IncGold (= 0x6D791C)
                    //   0065B3F6  84 c0              test al,al
                    //   0065B3F8  74 27              je   0x65B421           ; 信用失败 -> ret -3, 【池不动】
                    //   0065B3FA  29 b3 80 00 00 00  sub  [ebx+0x80],esi     ; 【只有到这里才扣池】
                    // 原来的 C# 先 `m_nTotalGold -= nGold` 再 `IncGold(...)` 且丢弃返回值,顺序相反。
                    // 当前不产生数值差(内联的 m_nGold+nGold<=m_nGoldMax 与 IncGold 内部判断同条件,
                    // 单线程下必然成功),但那是【双权威】写法:同一个上限条件在两处独立实现,
                    // 一旦 IncGold 将来增加任何拒绝条件(封号态/跨服态等),池已扣而玩家没收到,
                    // 每次取款凭空销毁 nGold。改为直接以 IncGold 的返回值为准,消除双权威。
                    if (PlayObject.IncGold(nGold))
                    {
                        m_nTotalGold -= nGold;
                        if (M2Share.g_boGameLogGold)
                            M2Share.AddGameDataLog("22" + "\t" + PlayObject.m_sMapName + "\t" + PlayObject.m_nCurrX +
                                                   "\t" + PlayObject.m_nCurrY + "\t" + PlayObject.m_sCharName + "\t" +
                                                   Grobal2.sSTRING_GOLDNAME + "\t" + nGold + "\t" + '1' + "\t" + '0');
                        PlayObject.GoldChanged();
                        result = 1;
                    }
                    else
                    {
                        result = -3;
                    }
                }
                else
                {
                    result = -2;
                }
            }
            return result;
        }

        public int ReceiptGolds(TPlayObject PlayObject, int nGold)
        {
            var result = -1;
            if (nGold <= 0)
            {
                result = -4;
                return result;
            }
            if (m_MasterGuild == PlayObject.m_MyGuild && PlayObject.m_nGuildRankNo == 1 && nGold > 0)
            {
                if (nGold <= PlayObject.m_nGold)
                {
                    if (m_nTotalGold + nGold <= M2Share.g_Config.nCastleGoldMax)
                    {
                        PlayObject.m_nGold -= nGold;
                        m_nTotalGold += nGold;
                        if (M2Share.g_boGameLogGold)
                            M2Share.AddGameDataLog("23" + "\t" + PlayObject.m_sMapName + "\t" + PlayObject.m_nCurrX +
                                                   "\t" + PlayObject.m_nCurrY + "\t" + PlayObject.m_sCharName + "\t" +
                                                   Grobal2.sSTRING_GOLDNAME + "\t" + nGold + "\t" + '1' + "\t" + '0');
                        PlayObject.GoldChanged();
                        result = 1;
                    }
                    else
                    {
                        result = -3;
                    }
                }
                else
                {
                    result = -2;
                }
            }
            return result;
        }

        
        
        
        
        public void MainDoorControl(bool boClose)
        {
            if (m_MainDoor.BaseObject != null && !m_MainDoor.BaseObject.m_boGhost)
            {
                if (boClose)
                {
                    if (((CastleDoor)m_MainDoor.BaseObject).m_boOpened)
                    {
                        ((CastleDoor)m_MainDoor.BaseObject).Close();
                    }
                }
                else
                {
                    if (!((CastleDoor)m_MainDoor.BaseObject).m_boOpened)
                    {
                        ((CastleDoor)m_MainDoor.BaseObject).Open();
                    }
                }
            }
        }

        
        
        
        
        public bool RepairDoor()
        {
            var result = false;
            var CastleDoor = m_MainDoor;
            if (CastleDoor.BaseObject == null || m_boUnderWar || CastleDoor.BaseObject.m_WAbil.HP >= CastleDoor.BaseObject.m_WAbil.MaxHP)
            {
                return result;
            }
            if (!CastleDoor.BaseObject.m_boDeath)
            {
                if ((HUtil32.GetTickCount() - CastleDoor.BaseObject.m_dwStruckTick) > 60 * 1000)
                {
                    CastleDoor.BaseObject.m_WAbil.HP = CastleDoor.BaseObject.m_WAbil.MaxHP;
                    ((CastleDoor)CastleDoor.BaseObject).RefStatus();
                    result = true;
                }
            }
            else
            {
                // 0x65B578 `sub eax,[esi+0x4E4]` -- the DEAD branch reads the
                // dead-structure clock, not m_dwStruckTick (+0x338, used only by
                // the alive branch at 0x65B54C). Both compare 0xEA60 = 60000 ms.
                if ((HUtil32.GetTickCount()
                     - ((GuardUnit)CastleDoor.BaseObject).m_dwDeadRepairTick)
                    > 60 * 1000)
                {
                    CastleDoor.BaseObject.m_WAbil.HP = CastleDoor.BaseObject.m_WAbil.MaxHP;
                    CastleDoor.BaseObject.m_boDeath = false;
                    ((CastleDoor)CastleDoor.BaseObject).m_boOpened = false;
                    ((CastleDoor)CastleDoor.BaseObject).RefStatus();
                    result = true;
                }
            }
            return result;
        }

        
        
        
        
        
        public bool RepairWall(int nWallIndex)
        {
            var result = false;
            TBaseObject Wall = null;
            switch (nWallIndex)
            {
                case 1:
                    Wall = m_LeftWall.BaseObject;
                    break;
                case 2:
                    Wall = m_CenterWall.BaseObject;
                    break;
                case 3:
                    Wall = m_RightWall.BaseObject;
                    break;
            }
            if (Wall == null || m_boUnderWar || Wall.m_WAbil.HP >= Wall.m_WAbil.MaxHP)
            {
                return result;
            }
            if (!Wall.m_boDeath)
            {
                if ((HUtil32.GetTickCount() - Wall.m_dwStruckTick) > 60 * 1000)
                {
                    Wall.m_WAbil.HP = Wall.m_WAbil.MaxHP;
                    ((WallStructure)Wall).RefStatus();
                    result = true;
                }
            }
            else
            {
                // 0x65B630 `sub eax,[ebx+0x4E4]` -- same split as the door: the
                // dead branch uses the dead-structure clock, the alive branch at
                // 0x65B604 uses m_dwStruckTick (+0x338).
                if ((HUtil32.GetTickCount()
                     - ((GuardUnit)Wall).m_dwDeadRepairTick) > 60 * 1000)
                {
                    Wall.m_WAbil.HP = Wall.m_WAbil.MaxHP;
                    Wall.m_boDeath = false;
                    ((WallStructure)Wall).RefStatus();
                    result = true;
                }
            }
            return result;
        }

        public bool AddAttackerInfo(Association Guild)
        {
            var result = false;
            if (InAttackerList(Guild)) return result;
            var AttackerInfo = new TAttackerInfo();
            AttackerInfo.AttackDate = M2Share.AddDateTimeOfDay(DateTime.Now, M2Share.g_Config.nStartCastleWarDays);
            AttackerInfo.sGuildName = Guild.sGuildName;
            AttackerInfo.Guild = Guild;
            m_AttackWarList.Add(AttackerInfo);
            SaveAttackSabukWall();
            M2Share.UserEngine.SendServerGroupMsg(Grobal2.SS_212, M2Share.nServerIndex, "");
            result = true;
            return result;
        }

        private bool InAttackerList(Association Guild)
        {
            var result = false;
            for (var i = 0; i < m_AttackWarList.Count; i++)
            {
                if (m_AttackWarList[i].Guild == Guild)
                {
                    result = true;
                    break;
                }
            }
            return result;
        }

        private void SetPower(int nPower)
        {
            m_nPower = nPower;
        }

        private void SetTechLevel(int nLevel)
        {
            m_nTechLevel = nLevel;
        }
    }
}
