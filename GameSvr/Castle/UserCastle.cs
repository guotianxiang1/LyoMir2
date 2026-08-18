using System.Globalization;
using SystemModule;
using SystemModule.Common;
using GameSvr.Plugins;

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
        // +0x04 byte. Day-roll 0x65BBC3 mov byte [ebx+4],0x14. Persist WineCount.
        public byte m_btWineCount;
        // +0x08 clock-of-day seconds (hour*3600+min*60+sec), occupancy 0x65C6AF
        public int m_nClockOfDaySec;
        
        
        
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

        public TUserCastle(string sCastleDir)
        {
            m_MasterGuild = null;
            m_sHomeMap = M2Share.g_Config.sCastleHomeMap;
            // 0x6592F9 push 0x2BC / 0x659315 push 0x190
            m_nHomeX = 0x2BC;
            m_nHomeY = 0x190;
            m_sName = M2Share.g_Config.sCastleName;
            m_sConfigDir = sCastleDir;
            m_sPalaceMap = "0150";
            m_sSecretMap = "D701";
            m_sMapName = "3";
            m_MapCastle = null;
            m_DoorStatus = null;
            m_boStartWar = false;
            m_boUnderWar = false;
            m_boForceWar = false;
            m_boShowOverMsg = false;
            m_dwRunTick = 0;
            m_nClockOfDaySec = 0;
            m_btWineCount = 0;
            m_ChangeDate = CastleConfManager.DelphiEpoch;
            m_WarDate = CastleConfManager.DelphiEpoch;
            m_IncomeToday = CastleConfManager.DelphiEpoch;
            m_AttackWarList = new List<TAttackerInfo>();
            m_AttackGuildList = new List<Association>();
            m_dwSaveTick = 0;
            m_EnvirList = new List<string>();
            // 0x6592C1 push 0x268 / 0x6592DD push 0x10C
            m_nPalaceDoorX = 0x268;
            m_nPalaceDoorY = 0x10C;
            m_MainDoor = new TObjUnit
            {
                nX = 0x2A0,
                nY = 0x14A,
                sName = "SabukDoor",
                nStatus = true,
                nHP = 2000
            };
            m_LeftWall = new TObjUnit { nX = 624, nY = 278, sName = "城墙左", nHP = 2000 };
            m_CenterWall = new TObjUnit { nX = 627, nY = 278, sName = "城墙中", nHP = 2000 };
            m_RightWall = new TObjUnit { nX = 634, nY = 271, sName = "城墙右", nHP = 2000 };
            for (var i = 0; i < m_Archer.Length; i++)
            {
                m_Archer[i] = new TObjUnit { sName = "弓箭手", nHP = 0 };
            }
            for (var i = 0; i < m_Guard.Length; i++)
            {
                m_Guard[i] = new TObjUnit { sName = "护卫", nHP = 0 };
            }
            var castleDir = NativeCastleDir();
            if (!Directory.Exists(castleDir))
            {
                Directory.CreateDirectory(castleDir);
            }
            castleConf = new CastleConfManager(castleDir);
        }

        internal static string NativeCastleDir()
        {
            return Path.Combine(M2Share.sConfigPath, M2Share.g_Config.sCastleDir);
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
            var sabukwallPath = NativeCastleDir();
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
            // sub_65A3B8 @0x65A3D4 returns before file creation when the
            // castle map field (+0x1C) is nil (for example on a non-host node).
            if (m_MapCastle == null)
            {
                return true;
            }

            try
            {
                var sabukwallPath = NativeCastleDir();
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
                // 0x65BB51 call 0x65c690 is BEFORE the 10s gate.
                TryCapturePalaceFromRun();
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
                    // 0x65BBC3 C6 43 04 14
                    m_btWineCount = 0x14;
                }
                // 0x49E39C: DecodeTime then hour*3600+min*60+sec. Stored at [ebx+8].
                m_nClockOfDaySec = now.Hour * 3600 + now.Minute * 60 + now.Second;
                var timeSec = m_nClockOfDaySec;
                YanshenPangu2Patches.TryGetSiegeDayClock(out var siegeStartSec,
                    out var siegeEndSec, out var siegeWarnSec, out var siegeCaptureSec);
                if (!m_boStartWar && !m_boUnderWar)
                {
                    // start window [start,end) unless +0x2B force skips it
                    if (m_boForceWar || (timeSec >= siegeStartSec && timeSec < siegeEndSec))
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
                // 0x65BD50..0x65BDAA also drop ghosted door/walls, not just guards.
                if (m_MainDoor.BaseObject != null && m_MainDoor.BaseObject.m_boGhost)
                    m_MainDoor.BaseObject = null;
                if (m_LeftWall.BaseObject != null && m_LeftWall.BaseObject.m_boGhost)
                    m_LeftWall.BaseObject = null;
                if (m_CenterWall.BaseObject != null && m_CenterWall.BaseObject.m_boGhost)
                    m_CenterWall.BaseObject = null;
                if (m_RightWall.BaseObject != null && m_RightWall.BaseObject.m_boGhost)
                    m_RightWall.BaseObject = null;
                if (m_boUnderWar)
                {
                    if (m_LeftWall.BaseObject != null) m_LeftWall.BaseObject.m_boStoneMode = false;
                    if (m_CenterWall.BaseObject != null) m_CenterWall.BaseObject.m_boStoneMode = false;
                    if (m_RightWall.BaseObject != null) m_RightWall.BaseObject.m_boStoneMode = false;
                    if (!m_boShowOverMsg && timeSec >= siegeWarnSec)
                    {
                        m_boShowOverMsg = true;
                        // 0x65BE9C len=33, hardcoded 10 minutes
                        const string sWarStopTimeMsg = "[沙巴克城攻城战离结束还有10分钟.]";
                        M2Share.UserEngine.SendBroadCastMsgExt(sWarStopTimeMsg, MsgType.System);
                        M2Share.UserEngine.SendServerGroupMsg(Grobal2.SS_204, M2Share.nServerIndex, sWarStopTimeMsg);
                        M2Share.MainOutMessage(sWarStopTimeMsg);
                    }
                    // 0x65BE1B cmp [ebx+0x2B],0 / jne skip;
                    // cmp [ebx+8],start / jb Stop; cmp end / jbe stay
                    if (!m_boForceWar && (timeSec < siegeStartSec || timeSec > siegeEndSec))
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

        // 0x65C690. Native calls this every Run, before the 10s throttle.
        // Capture unlock is clock-of-day >= 0x11B98 (20:10:00), not elapsed-from-start.
        private void TryCapturePalaceFromRun()
        {
            if (!m_boUnderWar || m_MapPalace == null) return;
            YanshenPangu2Patches.TryGetSiegeDayClock(out _, out _, out _,
                out var siegeCaptureSec);
            if (m_nClockOfDaySec < siegeCaptureSec) return;
            var humans = new List<TBaseObject>();
            M2Share.UserEngine.GetMapRageHuman(m_MapPalace, 0, 0, 0x3E8, humans);
            Association firstGuild = null;
            var allSame = true;
            for (var i = 0; i < humans.Count; i++)
            {
                var player = humans[i] as TPlayObject;
                if (player == null || player.m_boDeath) continue;
                if (firstGuild == null)
                {
                    firstGuild = player.m_MyGuild;
                    continue;
                }
                if (player.m_MyGuild != firstGuild)
                {
                    allSame = false;
                    break;
                }
            }
            if (!allSame || firstGuild == null || firstGuild == m_MasterGuild) return;
            if (!IsAttackGuild(firstGuild)) return;
            GetCastle(firstGuild, notifyServerGroup: true);
            if (m_AttackGuildList.Count <= 1)
            {
                StopWallconquestWar();
            }
        }

        public bool Save()
        {
            var configSaved = SaveConfigFile();
            var attackListSaved = SaveAttackSabukWall();
            return configSaved && attackListSaved;
        }

        // sub_659FD4, called with eax=castle, edx=envir, ecx=X, [esp+4]=Y:
        //   00659FE0  3B 50 20        cmp edx,[eax+0x20]  ; PalaceMap  (0x65AB0E)
        //   00659FE3  74 2A           je  .true
        //   00659FE5  3B 50 24        cmp edx,[eax+0x24]  ; SecretMap  (0x65AB47)
        //   00659FE8  74 25           je  .true
        //   00659FEA  3B 50 1C        cmp edx,[eax+0x1c]  ; CastleMap  (0x65ABB2)
        //   00659FED  75 22           jne .false
        //   00659FEF  81 FF 05 02..   cmp edi,0x205 / 7E jle .false
        //   00659FF7  81 FF EA 02..   cmp edi,0x2EA / 7D jge .false
        //   00659FFF  81 FE BC 00..   cmp esi,0x0BC / 7E jle .false
        //   0065A007  81 FE 90 01..   cmp esi,0x190 / 7D jge .false
        // The rectangle is hard-coded and absolute, not a radius around Home:
        // no CastleWarRange config key exists in the image (0 hits ASCII-ci and
        // UTF-16LE), and neither does any castle envir list -- '0151'..'0156'
        // are 0-hit too, so the old extra-map loop had no native counterpart.
        // Native has no nil test on envir; with a nil envir on a server that does
        // not host the castle every map field is nil too, so it answers true there.
        public bool InCastleWarArea(Envirnoment envir, int nX, int nY)
        {
            if (envir == m_MapPalace || envir == m_MapSecret) return true;
            if (envir != m_MapCastle) return false;
            return nX > 0x205 && nX < 0x2EA && nY > 0xBC && nY < 0x190;
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
            // 0x65C6AF cmp [ebx+8],captureSec / jb skip. Not elapsed-from-start.
            YanshenPangu2Patches.TryGetSiegeDayClock(out _, out _, out _,
                out var siegeCaptureSec);
            if (m_nClockOfDaySec < siegeCaptureSec)
            {
                return false;
            }
            var playPbjectList = new List<TBaseObject>();
            M2Share.UserEngine.GetMapRageHuman(m_MapPalace, 0, 0, 1000, playPbjectList);
            Association firstGuild = null;
            var result = true;
            for (var i = 0; i < playPbjectList.Count; i++)
            {
                var playObject = (TPlayObject)playPbjectList[i];
                if (playObject.m_boDeath) continue;
                if (firstGuild == null)
                {
                    firstGuild = playObject.m_MyGuild;
                    continue;
                }
                if (playObject.m_MyGuild != firstGuild)
                {
                    result = false;
                    break;
                }
            }
            playPbjectList = null;
            if (!result || firstGuild == null || firstGuild != guild) return false;
            if (firstGuild == m_MasterGuild) return false;
            return true;
        }

        public void GetCastle(Association Guild, bool notifyServerGroup = false)
        {
            const string sGetCastleMsg = "[{0} 已被 {1} 占领]";
            var oldGuild = m_MasterGuild;
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
            m_MasterGuild.RefMemberName();
            var s10 = string.Format(sGetCastleMsg, m_sName, m_sOwnGuild);
            M2Share.UserEngine.SendBroadCastMsgExt(s10, MsgType.System);
            M2Share.UserEngine.SendServerGroupMsg(Grobal2.SS_204, M2Share.nServerIndex, s10);
            // cl=1 at 0x65C76F: GetCastle sends SS_211 itself (0x65BFD2 / 0x65BFE7
            // mov dx,0xD3). cl=0 callers (0x65785C xor ecx,ecx) skip it.
            if (notifyServerGroup)
            {
                M2Share.UserEngine.SendServerGroupMsg(Grobal2.SS_211, M2Share.nServerIndex, m_sOwnGuild);
            }
            M2Share.MainOutMessage(s10);
        }

        public void StartWallconquestWar()
        {
            TPlayObject PlayObject;
            var ListC = new List<TBaseObject>();
            M2Share.UserEngine.GetMapRageHuman(m_MapCastle, m_nHomeX, m_nHomeY, 0xC8, ListC);
            for (var i = 0; i < ListC.Count; i++)
            {
                PlayObject = (TPlayObject)ListC[i];
                PlayObject.RefShowName();
            }
        }

        
        
        
        public void StopWallconquestWar()
        {
            TPlayObject PlayObject;
            const string sWallWarStop = "[沙巴克攻城战已经结束]";
            m_boUnderWar = false;
            m_boForceWar = false;
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
            var s14 = sWallWarStop;
            M2Share.UserEngine.SendBroadCastMsgExt(s14, MsgType.System);
            M2Share.UserEngine.SendServerGroupMsg(Grobal2.SS_204, M2Share.nServerIndex, s14);
            M2Share.MainOutMessage(s14);
            // 0x65C13E..0x65C1AC: Trunc(WarDate) vs Trunc(attackDate); delete
            // when attackDay >= warDay (jl/jb keep), then call 0x65A3B8 save.
            var warDay = m_WarDate.Date;
            for (var i = m_AttackWarList.Count - 1; i >= 0; i--)
            {
                if (m_AttackWarList[i].AttackDate.Date < warDay) continue;
                m_AttackWarList.RemoveAt(i);
            }
            SaveAttackSabukWall();
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
            // CGLD-01..04 @sub_65B31C: tax = @ROUND(price * 0.05) — 0x65B329 fild(signed) /
            // 0x65B332 fmulp / 0x65B334 call @ROUND(=0x403574, fistp=banker's half-to-even).
            // The 0.05 is an 80-bit x87 tbyte @0x65B39C (cd cc cc cc cc cc cc cc fa 3f); a
            // float32/float64 reinterpret of those 10 bytes yields garbage, so the rate must be
            // recomputed (nCastleTaxRate=5 -> 5/100.0), never read from the raw constant width.
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
            // 0x65B465 mov [ebp-4],-1 ; 0x65B46C test esi,esi / 0x65B46E jle 0x65B4E5
            if (nGold <= 0)
            {
                return result;
            }
            if (m_MasterGuild == PlayObject.m_MyGuild && PlayObject.m_nGuildRankNo == 1)
            {
                // ✅ 战神字节证据 (Tier-1) — CGLD-11: 存款 sub_65B458 先测【池上限】(→-3)再测
                // 【玩家余额】(→-2)，两道门顺序与取款不同。EA @0x65B492-0x65B4AE:
                //   0065B492  8b 43 80           mov eax,[ebx+0x80]          ; 池 m_nTotalGold(+0x80)
                //   0065B498  03 c6              add eax,esi                 ; + nGold
                //   0065B49A  3d 00 e1 f5 05     cmp eax,0x5f5e100           ; vs 上限(100,000,000)
                //   0065B49F  7f 3d              jg  0x65b4de                ; 超上限 -> ret -3【先判】
                //   0065B4A5  e8 ba c8 06 00     call 0x6c7d64 (DecGold)     ; 扣玩家(内含 nGold<=gold 判)
                //   0065B4AA  84 c0 / 74 27      test al / je 0x65b4d5       ; 余额不足 -> ret -2【后判】
                //   0065B4AE  01 b3 80 00 00 00  add [ebx+0x80],esi          ; 只有两门皆过才加池
                // 原来的 C# 先判余额(→-2)再判上限(→-3)，两者【同时失败】时返回 -2，原生返回 -3
                // (池满 100M 且玩家钱不足即命中；提示语 -3"存放限制"/-2"没那么多金币" 因此不同)。
                // DecGold(0x6c7d64) 成功 <=> 0<nGold<=m_nGold(+0x15c)，故内联 `nGold<=m_nGold` 与其等价。
                if (m_nTotalGold + nGold <= M2Share.g_Config.nCastleGoldMax)
                {
                    if (nGold <= PlayObject.m_nGold)
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
                        result = -2;
                    }
                }
                else
                {
                    result = -3;
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
            var guildName = Guild?.sGuildName ?? string.Empty;
            var result = NativeMirrorAddAttacker(Guild);
            // sub_65B658 @0x65B6BA..0x65B6CD sends [Guild+0x10]. The send
            // remains after the duplicate gate, so duplicate requests fan out too.
            M2Share.UserEngine.SendServerGroupMsg(Grobal2.SS_212,
                M2Share.nServerIndex, guildName);
            return result;
        }

        /// <summary>
        /// 战神 sub_65B6E0 (ident 212 stub sub_6577B0 调 [[0x7D6214]]): 按行会名
        /// FindGuild(0x5E76F0) 后若不在 [castle+0x8C] 攻击列表则追加并
        /// sub_65A3B8 保存。无 SS_212 扇出 (与 AddAttackerInfo 的 live 发送方不同)。
        /// </summary>
        internal bool NativeMirrorAddAttacker(Association Guild)
        {
            if (Guild == null || InAttackerList(Guild))
            {
                return false;
            }

            var AttackerInfo = new TAttackerInfo();
            // 0x65B686 Now / 0x65B68B fadd dword [0x65B6DC] = 3.0 TDateTime days
            AttackerInfo.AttackDate = DateTime.Now.AddDays(3.0);
            AttackerInfo.sGuildName = Guild.sGuildName;
            AttackerInfo.Guild = Guild;
            m_AttackWarList.Add(AttackerInfo);
            SaveAttackSabukWall();
            return true;
        }

        internal bool IsAttackerGuild(Association guild) => InAttackerList(guild);

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
