using System.Globalization;
using SystemModule;
using SystemModule.Common;

namespace GameSvr
{
    /// <summary>
    /// 战神城堡落盘。文件名和键名是协议，必须和原版 TIniFile 一致。
    ///
    /// 运行态 <c>沙巴克.txt</c> SaveCastleInfo @0x65A510：
    ///   setup: OwnGuild / changeDate / WarDate / IncomeToday / TotalGold /
    ///          TodayIncome / WineCount
    ///   defense: MainDoorOpen / MainDoorHP / LeftWallHP / CenterWallHP /
    ///            RightWallHP / Archer_{0..11}_HP / Guard_{0..3}_HP / flagOwnerN
    /// 静态布局 <c>沙巴克基础配置.txt</c> 只在 Init 里 LoadDual @0x659134 读，从不回写。
    /// C# 旧的 SabukW.txt / [Setup] / Archer_1_HP / ChangeDate / ISO-8601 日期
    /// 全镜像 0 命中，属于 OpenMir2 发明。
    /// </summary>
    public class CastleConfManager : IniFile
    {
        public const string StateFileName = "沙巴克.txt";
        public const string BaseFileName = "沙巴克基础配置.txt";

        // Delphi TDateTime 0.0
        public static readonly DateTime DelphiEpoch = new(1899, 12, 30);

        private readonly string _castleDir;
        private readonly string _statePath;

        public CastleConfManager(string castleDir)
            : base(Path.Combine(castleDir, StateFileName))
        {
            _castleDir = castleDir;
            _statePath = Path.Combine(castleDir, StateFileName);
            if (File.Exists(_statePath))
            {
                Load();
            }
        }

        public void LoadConfig(TUserCastle userCastle)
        {
            LoadBaseConfig(userCastle);
            LoadStateConfig(userCastle);
        }

        private void LoadBaseConfig(TUserCastle userCastle)
        {
            var basePath = Path.Combine(_castleDir, BaseFileName);
            if (!File.Exists(basePath))
            {
                return;
            }

            var baseIni = new NativeCastleIni(basePath);
            userCastle.m_sName = baseIni.GetString("setup", "CastleName", userCastle.m_sName);
            userCastle.m_sMapName = baseIni.GetString("defense", "CastleMap", userCastle.m_sMapName);
            var defMap = baseIni.GetString("defense", "DefMapStr", userCastle.m_sMapName);
            if (!string.IsNullOrEmpty(defMap))
            {
                userCastle.m_sMapName = defMap;
            }
            userCastle.m_sSecretMap = baseIni.GetString("defense", "WayMap", userCastle.m_sSecretMap);
            userCastle.m_nPalaceDoorX = baseIni.GetInteger("defense", "OfficeDoorX", userCastle.m_nPalaceDoorX);
            userCastle.m_nPalaceDoorY = baseIni.GetInteger("defense", "OfficeDoorY", userCastle.m_nPalaceDoorY);
            userCastle.m_nHomeX = baseIni.GetInteger("defense", "HomeX", userCastle.m_nHomeX);
            userCastle.m_nHomeY = baseIni.GetInteger("defense", "HomeY", userCastle.m_nHomeY);
            userCastle.m_MainDoor.nX = (short)baseIni.GetInteger("defense", "MainDoorX", userCastle.m_MainDoor.nX);
            userCastle.m_MainDoor.nY = (short)baseIni.GetInteger("defense", "MainDoorY", userCastle.m_MainDoor.nY);
            userCastle.m_MainDoor.sName = baseIni.GetString("defense", "MainDoorName", userCastle.m_MainDoor.sName);
            userCastle.m_LeftWall.nX = (short)baseIni.GetInteger("defense", "LeftWallX", userCastle.m_LeftWall.nX);
            userCastle.m_LeftWall.nY = (short)baseIni.GetInteger("defense", "LeftWallY", userCastle.m_LeftWall.nY);
            userCastle.m_LeftWall.sName = baseIni.GetString("defense", "LeftWallName", userCastle.m_LeftWall.sName);
            userCastle.m_CenterWall.nX = (short)baseIni.GetInteger("defense", "CenterWallX", userCastle.m_CenterWall.nX);
            userCastle.m_CenterWall.nY = (short)baseIni.GetInteger("defense", "CenterWallY", userCastle.m_CenterWall.nY);
            userCastle.m_CenterWall.sName = baseIni.GetString("defense", "CenterWallName", userCastle.m_CenterWall.sName);
            userCastle.m_RightWall.nX = (short)baseIni.GetInteger("defense", "RightWallX", userCastle.m_RightWall.nX);
            userCastle.m_RightWall.nY = (short)baseIni.GetInteger("defense", "RightWallY", userCastle.m_RightWall.nY);
            userCastle.m_RightWall.sName = baseIni.GetString("defense", "RightWallName", userCastle.m_RightWall.sName);
        }

        private void LoadStateConfig(TUserCastle userCastle)
        {
            if (!File.Exists(_statePath))
            {
                return;
            }

            userCastle.m_sOwnGuild = ReadString("setup", "OwnGuild", "");
            userCastle.m_ChangeDate = ReadDelphiDate("setup", "changeDate", userCastle.m_ChangeDate);
            userCastle.m_WarDate = ReadDelphiDate("setup", "WarDate", userCastle.m_WarDate);
            userCastle.m_IncomeToday = ReadDelphiDateTime("setup", "IncomeToday", userCastle.m_IncomeToday);
            userCastle.m_nTotalGold = ReadInteger("setup", "TotalGold", 0);
            userCastle.m_nTodayIncome = ReadInteger("setup", "TodayIncome", 0);
            userCastle.m_btWineCount = (byte)ReadInteger("setup", "WineCount", userCastle.m_btWineCount);
            userCastle.m_MainDoor.nStatus = ReadBool("defense", "MainDoorOpen", userCastle.m_MainDoor.nStatus);
            userCastle.m_MainDoor.nHP = Read<ushort>("defense", "MainDoorHP", userCastle.m_MainDoor.nHP);
            userCastle.m_LeftWall.nHP = Read<ushort>("defense", "LeftWallHP", userCastle.m_LeftWall.nHP);
            userCastle.m_CenterWall.nHP = Read<ushort>("defense", "CenterWallHP", userCastle.m_CenterWall.nHP);
            userCastle.m_RightWall.nHP = Read<ushort>("defense", "RightWallHP", userCastle.m_RightWall.nHP);
            for (var i = 0; i < userCastle.m_Archer.Length; i++)
            {
                userCastle.m_Archer[i].nHP = Read<ushort>("defense", "Archer_" + i + "_HP",
                    userCastle.m_Archer[i].nHP);
            }
            for (var i = 0; i < userCastle.m_Guard.Length; i++)
            {
                userCastle.m_Guard[i].nHP = Read<ushort>("defense", "Guard_" + i + "_HP",
                    userCastle.m_Guard[i].nHP);
            }
        }

        public void SaveConfig(TUserCastle userCastle)
        {
            if (!Directory.Exists(_castleDir))
            {
                Directory.CreateDirectory(_castleDir);
            }
            if (M2Share.MapManager.GetMapOfServerIndex(userCastle.m_sMapName) != M2Share.nServerIndex)
            {
                return;
            }

            // 0x65A5A8 OwnGuild / 0x65A5BE changeDate / 0x65A5D4 WarDate /
            // 0x65A5EA IncomeToday / 0x65A601 TotalGold / 0x65A618 TodayIncome /
            // 0x65A62E WineCount. Native always writes, including 0.
            SetCachedString("setup", "OwnGuild", userCastle.m_sOwnGuild ?? string.Empty);
            SetCachedString("setup", "changeDate", FormatDelphiDate(userCastle.m_ChangeDate));
            SetCachedString("setup", "WarDate", FormatDelphiDate(userCastle.m_WarDate));
            SetCachedString("setup", "IncomeToday", FormatDelphiDateTime(userCastle.m_IncomeToday));
            SetCachedInteger("setup", "TotalGold", userCastle.m_nTotalGold);
            SetCachedInteger("setup", "TodayIncome", userCastle.m_nTodayIncome);
            SetCachedInteger("setup", "WineCount", userCastle.m_btWineCount);

            if (userCastle.m_MainDoor.BaseObject != null)
            {
                SetCachedString("defense", "MainDoorOpen",
                    ((CastleDoor)userCastle.m_MainDoor.BaseObject).m_boOpened ? "1" : "0");
                SetCachedInteger("defense", "MainDoorHP",
                    userCastle.m_MainDoor.BaseObject.m_WAbil.HP);
            }
            if (userCastle.m_LeftWall.BaseObject != null)
            {
                SetCachedInteger("defense", "LeftWallHP",
                    userCastle.m_LeftWall.BaseObject.m_WAbil.HP);
            }
            if (userCastle.m_CenterWall.BaseObject != null)
            {
                SetCachedInteger("defense", "CenterWallHP",
                    userCastle.m_CenterWall.BaseObject.m_WAbil.HP);
            }
            if (userCastle.m_RightWall.BaseObject != null)
            {
                SetCachedInteger("defense", "RightWallHP",
                    userCastle.m_RightWall.BaseObject.m_WAbil.HP);
            }

            // Archer/Guard 下标从 0 起：0x65A711 eax=ebx, 0x65A772 cmp ebx,0xC
            for (var i = 0; i < userCastle.m_Archer.Length; i++)
            {
                var obj = userCastle.m_Archer[i];
                var hp = obj.BaseObject != null ? obj.BaseObject.m_WAbil.HP : 0;
                SetCachedInteger("defense", "Archer_" + i + "_HP", hp);
            }
            for (var i = 0; i < userCastle.m_Guard.Length; i++)
            {
                var obj = userCastle.m_Guard[i];
                var hp = obj.BaseObject != null ? obj.BaseObject.m_WAbil.HP : 0;
                SetCachedInteger("defense", "Guard_" + i + "_HP", hp);
            }
            Save();
        }

        public static string FormatDelphiDate(DateTime value)
        {
            return value.ToString("yyyy/M/d", CultureInfo.InvariantCulture);
        }

        public static string FormatDelphiDateTime(DateTime value)
        {
            return value.ToString("yyyy/M/d H:mm:ss", CultureInfo.InvariantCulture);
        }

        private DateTime ReadDelphiDate(string section, string key, DateTime defValue)
        {
            var text = ReadString(section, key, string.Empty);
            if (string.IsNullOrEmpty(text))
            {
                return defValue;
            }
            return DateTime.TryParse(text, CultureInfo.InvariantCulture,
                DateTimeStyles.AllowWhiteSpaces, out var parsed)
                ? parsed.Date
                : defValue;
        }

        private DateTime ReadDelphiDateTime(string section, string key, DateTime defValue)
        {
            var text = ReadString(section, key, string.Empty);
            if (string.IsNullOrEmpty(text))
            {
                return defValue;
            }
            return DateTime.TryParse(text, CultureInfo.InvariantCulture,
                DateTimeStyles.AllowWhiteSpaces, out var parsed)
                ? parsed
                : defValue;
        }

        private sealed class NativeCastleIni : IniFile
        {
            public NativeCastleIni(string fileName) : base(fileName)
            {
                Load();
            }

            public string GetString(string section, string key, string defValue)
            {
                return ReadString(section, key, defValue);
            }

            public int GetInteger(string section, string key, int defValue)
            {
                return ReadInteger(section, key, defValue);
            }
        }
    }
}
