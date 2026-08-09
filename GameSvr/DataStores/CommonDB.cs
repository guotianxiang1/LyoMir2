using MySql.Data.MySqlClient;
using GameSvr.Services;
using System.Data;
using System.Diagnostics;
using System.Text;
using SystemModule;

namespace GameSvr
{
    
    
    
    public class CommonDB
    {
        private static readonly Encoding Latin1Encoding = Encoding.GetEncoding(28591);
        private IDbConnection _dbConnection;

        public int LoadItemsDB()
        {
            if (M2Share.UserEngine?.NativeStdItemDefinitionsPublished == true)
            {
                M2Share.ErrorMessage(
                    "原生标准物品表已发布，拒绝使用 stditems 运行期替换。");
                return -1;
            }
            int result = -1;
            int Idx;
            GoodItem Item;
            const string sSQLString = "SELECT * FROM mir3.stditems ORDER BY idx";
            try
            {
                for (var i = 0; i < M2Share.UserEngine.StdItemList.Count; i++)
                {
                    M2Share.UserEngine.StdItemList[i] = null;
                }
                M2Share.UserEngine.StdItemList.Clear();
                result = -1;
                if (!Open())
                {
                    return result;
                }
                M2Share.UserEngine.StdItemList.Add(
                    NativeType2StdItemStaticCatalog
                        .CreateVerifiedGoldSentinel());
                using (var dr = Query(sSQLString))
                {
                    while (dr.Read())
                    {
                        Item = new GoodItem();
                        try { Idx = dr.GetInt32("Idx"); } catch (Exception) { Idx = 0; }// 序号
                        Item.Name = TryGetGbkStoredString(dr, "iname", "").Trim();// 名称 (战神数据库以 latin1 列保存 GBK 字节)
                        try { Item.StdMode = (byte)dr.GetInt32("Stdmode"); } catch (Exception) { Item.StdMode = 0; }// 分类号 (战神: Stdmode)
                        try { Item.Shape = (byte)dr.GetInt32("Shape"); } catch (Exception) { Item.Shape = 0; }// 装备外观
                        try { Item.Weight = (byte)dr.GetInt32("Weight"); } catch (Exception) { Item.Weight = 0; }// 重量
                        try { Item.AniCount = (ushort)dr.GetInt32("anicount"); } catch (Exception) { Item.AniCount = 0; }// (战神: anicount) word-width: native reads AniCount as a word; TVessel pair-ids >255 (CM_1017 merge, 泉水罐 1245/泉水 1229) — the old (byte) cast truncated them. Other consumers use <255.
                        try { Item.Source = dr.GetInt16("source"); } catch (Exception) { Item.Source = 0; }// (战神: source)
                        try { Item.Outlook = dr.GetInt32("OutLook"); } catch (Exception) { Item.Outlook = 0; }
                        try { Item.Reserved = (byte)dr.GetInt32("NeedConf"); } catch (Exception) { Item.Reserved = 0; }// (战神: NeedConf)
                        try { Item.Looks = dr.GetUInt16("Looks"); } catch (Exception) { Item.Looks = 0; }// 地面物品图片索引 (战神: Looks)
                        try { Item.DuraMax = (ushort)dr.GetInt32("DuraMax"); } catch (Exception) { Item.DuraMax = 0; }// 持久
                        try { Item.Ac = (ushort)HUtil32.Round(dr.GetInt32("AC") * (M2Share.g_Config.nItemsACPowerRate / 10.0)); } catch (Exception) { Item.Ac = 0; }
                        try { Item.Ac2 = (ushort)HUtil32.Round(dr.GetInt32("MaxAc") * (M2Share.g_Config.nItemsACPowerRate / 10.0)); } catch (Exception) { Item.Ac2 = 0; }// (战神: MaxAc)
                        try { Item.Mac = (ushort)HUtil32.Round(dr.GetInt32("MAC") * (M2Share.g_Config.nItemsACPowerRate / 10.0)); } catch (Exception) { Item.Mac = 0; }
                        try { Item.Mac2 = (ushort)HUtil32.Round(dr.GetInt32("MaxMAC") * (M2Share.g_Config.nItemsACPowerRate / 10.0)); } catch (Exception) { Item.Mac2 = 0; }// (战神: MaxMAC)
                        try { Item.Dc = (ushort)HUtil32.Round(dr.GetInt32("DC") * (M2Share.g_Config.nItemsPowerRate / 10.0)); } catch (Exception) { Item.Dc = 0; }
                        try { Item.Dc2 = (ushort)HUtil32.Round(dr.GetInt32("MaxDC") * (M2Share.g_Config.nItemsPowerRate / 10.0)); } catch (Exception) { Item.Dc2 = 0; }// (战神: MaxDC)
                        try { Item.Mc = (ushort)HUtil32.Round(dr.GetInt32("MC") * (M2Share.g_Config.nItemsPowerRate / 10.0)); } catch (Exception) { Item.Mc = 0; }
                        try { Item.Mc2 = (ushort)HUtil32.Round(dr.GetInt32("MaxMC") * (M2Share.g_Config.nItemsPowerRate / 10.0)); } catch (Exception) { Item.Mc2 = 0; }// (战神: MaxMC)
                        try { Item.Sc = (ushort)HUtil32.Round(dr.GetInt32("SC") * (M2Share.g_Config.nItemsPowerRate / 10.0)); } catch (Exception) { Item.Sc = 0; }
                        try { Item.Sc2 = (ushort)HUtil32.Round(dr.GetInt32("MaxSc") * (M2Share.g_Config.nItemsPowerRate / 10.0)); } catch (Exception) { Item.Sc2 = 0; }// (战神: MaxSc)
                        try { Item.Need = dr.GetInt32("Need"); } catch (Exception) { Item.Need = 0; }// 附加条件
                        try { Item.NeedLevel = dr.GetInt32("NeedLevel"); } catch (Exception) { Item.NeedLevel = 0; }// 需要等级
                        try { Item.Price = dr.GetInt32("Price"); } catch (Exception) { Item.Price = 0; }// 价格
                        Item.NeedIdentify = M2Share.GetGameLogItemNameList(Item.Name);
                        switch (Item.StdMode)
                        {
                            case 0:
                            case 55:
                            case 58:
                                Item.ItemType = GoodType.ITEM_LEECHDOM;
                                break;
                            case 5:
                            case 6:
                                Item.ItemType = GoodType.ITEM_WEAPON;
                                break;
                            case 10:
                            case 11:
                                Item.ItemType = GoodType.ITEM_ARMOR;
                                break;
                            case 15:
                            case 19:
                            case 20:
                            case 21:
                            case 22:
                            case 23:
                            case 24:
                            case 26:
                            case 51:
                            case 52:
                            case 53:
                            case 54:
                            case 62:
                            case 63:
                            case 64:
                            case 30:
                                Item.ItemType = GoodType.ITEM_ACCESSORY;
                                break;
                            default:
                                Item.ItemType = GoodType.ITEM_ETC;
                                break;
                        }
                        if (M2Share.UserEngine.StdItemList.Count <= Idx)
                        {
                            M2Share.UserEngine.StdItemList.Add(Item);
                            result = 1;
                        }
                        else
                        {
                            M2Share.MainOutMessage(string.Format("加载物品(Idx:{0} Name:{1})数据失败!!!", new object[] { Idx, Item.Name }));
                            result = -100;
                            return result;
                        }
                    }
                }
                M2Share.g_boGameLogGold = M2Share.GetGameLogItemNameList(Grobal2.sSTRING_GOLDNAME) == 1;
                M2Share.g_boGameLogHumanDie = M2Share.GetGameLogItemNameList(M2Share.g_sHumanDieEvent) == 1;
                M2Share.g_boGameLogGameGold = M2Share.GetGameLogItemNameList(M2Share.g_Config.sGameGoldName) == 1;
                M2Share.g_boGameLogGamePoint = M2Share.GetGameLogItemNameList(M2Share.g_Config.sGamePointName) == 1;
            }
            catch (Exception ex)
            {
                M2Share.ErrorMessage($"加载物品数据库异常: {ex.Message}");
                Console.WriteLine(ex.StackTrace);
                return result;
            }
            finally
            {
                Close();
            }
            return result;
        }

        public int LoadMagicDB()
        {
            if (M2Share.UserEngine?.NativeMagicDefinitionsPublished == true)
            {
                M2Share.ErrorMessage(
                    "原生人物/英雄技能双表已发布，拒绝使用 forcemagic 运行期替换。");
                return -1;
            }
            TMagic Magic;
            const string sSQLString = "select * from mir3.forcemagic";
            var result = -1;
            HUtil32.EnterCriticalSection(M2Share.ProcessHumanCriticalSection);
            try
            {
                M2Share.UserEngine.SwitchMagicList();
                if (!Open())
                {
                    return result;
                }
                using (var dr = Query(sSQLString))
                {
                    while (dr.Read())
                    {
                        Magic = new TMagic
                        {
                            wMagicID = dr.GetUInt16("ForceID"),
                            sMagicName = TryGetGbkStoredString(dr, "name", "").Trim(),
                            btEffectType = 0,
                            btEffect = (byte)TryGetInt32(dr, "Effect", 0),
                            wSpell = dr.GetUInt16("Spell"),
                            wPower = dr.GetUInt16("Power"),
                            wMaxPower = (ushort)TryGetInt32(dr, "PowerParam", 0),
                            btJob = (byte)dr.GetInt32("Job")
                        };
                        Magic.TrainLevel[0] = (byte)dr.GetInt32("NeedL1");
                        Magic.TrainLevel[1] = (byte)dr.GetInt32("NeedL2");
                        Magic.TrainLevel[2] = (byte)dr.GetInt32("NeedL3");
                        Magic.TrainLevel[3] = (byte)dr.GetInt32("NeedL3");
                        Magic.MaxTrain[0] = dr.GetInt32("L1Train");
                        Magic.MaxTrain[1] = dr.GetInt32("L2Train");
                        Magic.MaxTrain[2] = dr.GetInt32("L3Train");
                        Magic.MaxTrain[3] = Magic.MaxTrain[2];
                        Magic.btTrainLv = 3;
                        Magic.dwDelayTime = TryGetInt32(dr, "Delay", 0);
                        Magic.btDefSpell = (byte)TryGetInt32(dr, "DefSpell", 0);
                        Magic.btDefPower = (byte)TryGetInt32(dr, "DefPower", 0);
                        Magic.btDefMaxPower = (byte)TryGetInt32(dr, "DefMaxPower", 0);
                        Magic.sDescr = TryGetString(dr, "Descr", "");
                        if (Magic.wMagicID > 0)
                        {
                            M2Share.UserEngine.m_MagicList.Add(Magic);
                        }
                        else
                        {
                            Magic = null;
                        }
                        result = 1;
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.StackTrace);
            }
            finally
            {
                Close();
                HUtil32.LeaveCriticalSection(M2Share.ProcessHumanCriticalSection);
            }
            return result;
        }

        public int LoadMonsterDB()
        {
            var result = 0;
            TMonInfo Monster;
            const string sSQLString = "select * from mir3.monster";
            HUtil32.EnterCriticalSection(M2Share.ProcessHumanCriticalSection);
            try
            {
                M2Share.UserEngine.MonsterList.Clear();
                if (!Open())
                {
                    return result;
                }
                using (var dr = Query(sSQLString))
                {
                    while (dr.Read())
                    {
                        Monster = new TMonInfo
                        {
                            ItemList = new List<TMonItem>(),
                            sName = TryGetGbkStoredString(dr, "MonName", "").Trim(),
                            btRace = (byte)TryGetInt32(dr, "Race", 0),
                            btRaceImg = (byte)TryGetInt32(dr, "RaceImg", 0),
                            wAppr = (ushort)TryGetInt32(dr, "Appr", 0),
                            wLevel = (ushort)TryGetInt32(dr, "Level", 0),
                            btLifeAttrib = (byte)TryGetInt32(dr, "Undead", 0),
                            wCoolEye = (short)TryGetInt32(dr, "CoolEye", 0),
                            dwExp = TryGetInt32(dr, "Exp", 0)
                        };

                        if (Monster.btRace == 110 || Monster.btRace == 111)
                        {
                            Monster.wHP = (ushort)TryGetInt32(dr, "HP", 0);
                        }
                        else
                        {
                            Monster.wHP = (ushort)HUtil32.Round(TryGetInt32(dr, "HP", 0) * (M2Share.g_Config.nMonsterPowerRate / 10.0));
                        }
                        Monster.wMP = (ushort)HUtil32.Round(TryGetInt32(dr, "MP", 0) * (M2Share.g_Config.nMonsterPowerRate / 10.0));
                        Monster.wAC = (ushort)HUtil32.Round(TryGetInt32(dr, "AC", 0) * (M2Share.g_Config.nMonsterPowerRate / 10.0));
                        Monster.wMAC = (ushort)HUtil32.Round(TryGetInt32(dr, "MAC", 0) * (M2Share.g_Config.nMonsterPowerRate / 10.0));
                        Monster.wDC = (ushort)HUtil32.Round(TryGetInt32(dr, "DC", 0) * (M2Share.g_Config.nMonsterPowerRate / 10.0));
                        Monster.wMaxDC = (ushort)HUtil32.Round(TryGetInt32(dr, "DcMax", 0) * (M2Share.g_Config.nMonsterPowerRate / 10.0));
                        Monster.wMC = (ushort)HUtil32.Round(TryGetInt32(dr, "MC", 0) * (M2Share.g_Config.nMonsterPowerRate / 10.0));
                        Monster.wSC = (ushort)HUtil32.Round(TryGetInt32(dr, "SC", 0) * (M2Share.g_Config.nMonsterPowerRate / 10.0));
                        Monster.wSpeed = (ushort)TryGetInt32(dr, "Speed", 0);
                        Monster.wHitPoint = (ushort)TryGetInt32(dr, "Hit", 0);
                        Monster.wWalkSpeed = (ushort)HUtil32._MAX(200, TryGetInt32(dr, "WalkSpd", 200));
                        Monster.wWalkStep = (ushort)HUtil32._MAX(1, TryGetInt32(dr, "WalkStep", 1));
                        Monster.wWalkWait = (ushort)TryGetInt32(dr, "WalkWait", 0);
                        Monster.wAttackSpeed = (ushort)TryGetInt32(dr, "AttackSpd", 200);
                        if (Monster.wWalkSpeed < 200)
                        {
                            Monster.wWalkSpeed = 200;
                        }
                        if (Monster.wAttackSpeed < 200)
                        {
                            Monster.wAttackSpeed = 200;
                        }
                        Monster.ItemList = null;
                        M2Share.LocalDB.LoadMonitems(Monster.sName, ref Monster.ItemList);
                        M2Share.UserEngine.MonsterList.Add(Monster);
                        result = 1;
                    }
                }
            }
            finally
            {
                Close();
                HUtil32.LeaveCriticalSection(M2Share.ProcessHumanCriticalSection);
            }
            return result;
        }

        private static int TryGetInt32(IDataReader dr, string field, int defaultValue)
        {
            try
            {
                var val = dr[field];
                if (val == null || val == DBNull.Value) return defaultValue;
                return Convert.ToInt32(val);
            }
            catch (Exception ex)
            {
                Debug.WriteLine(ex.Message);
                return defaultValue;
            }
        }

        private static string TryGetString(IDataReader dr, string field, string defaultValue)
        {
            try
            {
                var val = dr[field];
                if (val == null || val == DBNull.Value) return defaultValue;
                return Convert.ToString(val);
            }
            catch (Exception ex)
            {
                Debug.WriteLine(ex.Message); return defaultValue;
            }
        }

        private static string TryGetGbkStoredString(IDataReader dr, string field, string defaultValue)
        {
            try
            {
                var val = dr[field];
                if (val == null || val == DBNull.Value) return defaultValue;
                if (val is byte[] bytes) return HUtil32.GbkEncoding.GetString(bytes);

                var text = Convert.ToString(val);
                if (string.IsNullOrEmpty(text)) return text ?? defaultValue;
                if (ContainsCjk(text)) return text;

                return HUtil32.GbkEncoding.GetString(Latin1Encoding.GetBytes(text));
            }
            catch (Exception ex)
            {
                Debug.WriteLine(ex.Message); return defaultValue;
            }
        }

        private static bool ContainsCjk(string text)
        {
            for (var i = 0; i < text.Length; i++)
            {
                if (text[i] >= 0x2E80) return true;
            }
            return false;
        }

        
        
        
        private IDataReader Query(string sSQLString)
        {
            var command = new MySqlCommand();
            command.Connection = (MySqlConnection)_dbConnection;
            command.CommandText = sSQLString;
            return command.ExecuteReader();
        }

        private int Execute(string sSQLString)
        {
            var command = new MySqlCommand();
            command.Connection = (MySqlConnection)_dbConnection;
            command.CommandText = sSQLString;
            return command.ExecuteNonQuery();
        }

        private bool Open()
        {
            if (_dbConnection == null)
            {
                try
                {
                    _dbConnection = new MySqlConnection(M2Share.g_Config.sConnctionString);
                    _dbConnection.Open();
                    return true;
                }
                catch (Exception e)
                {
                    M2Share.ErrorMessage($"数据库连接失败: {e.Message}");
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine(e);
                    Console.ResetColor();
                    return false;
                }
            }
            else if (_dbConnection.State == ConnectionState.Closed)
            {
                _dbConnection.Open();
            }
            return true;
        }

        private void Close()
        {
            if (_dbConnection != null)
            {
                _dbConnection.Close();
                _dbConnection.Dispose();
                _dbConnection = null;
            }
        }
    }
}
