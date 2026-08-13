using System.Collections;
using SystemModule;
using SystemModule.Common;

namespace GameSvr
{
    public class CastleManager
    {
        private readonly IList<TUserCastle> _castleList;

        public CastleManager()
        {
            _castleList = new List<TUserCastle>();
        }

        public TUserCastle Find(string sCastleName)
        {
            TUserCastle result = null;
            TUserCastle Castle = null;
            for (var i = 0; i < _castleList.Count; i++)
            {
                Castle = _castleList[i];
                if (string.Compare(Castle.m_sName, sCastleName, StringComparison.OrdinalIgnoreCase) == 0)
                {
                    result = Castle;
                    break;
                }
            }

            return result;
        }

        
        public TUserCastle InCastleWarArea(TBaseObject BaseObject)
        {
            TUserCastle result = null;
            TUserCastle Castle = null;
            for (var i = 0; i < _castleList.Count; i++)
            {
                Castle = _castleList[i];
                if (Castle.InCastleWarArea(BaseObject.m_PEnvir, BaseObject.m_nCurrX, BaseObject.m_nCurrY))
                {
                    result = Castle;
                    break;
                }
            }
            return result;
        }

        public TUserCastle InCastleWarArea(Envirnoment Envir, int nX, int nY)
        {
            TUserCastle result = null;
            TUserCastle Castle = null;
            for (var i = 0; i < _castleList.Count; i++)
            {
                Castle = _castleList[i];
                if (Castle.InCastleWarArea(Envir, nX, nY))
                {
                    result = Castle;
                    break;
                }
            }
            return result;
        }

        public bool AnyCastleUnderWar
        {
            get
            {
                for (var i = 0; i < _castleList.Count; i++)
                {
                    if (_castleList[i]?.m_boUnderWar == true)
                    {
                        return true;
                    }
                }
                return false;
            }
        }

        public void Initialize()
        {
            TUserCastle Castle;
            if (_castleList.Count <= 0)
            {
                Castle = new TUserCastle(M2Share.g_Config.sCastleDir);
                _castleList.Add(Castle);
                Castle.Initialize();
                Castle.m_sConfigDir = "0";
                // '0151'..'0156' are 0-hit in the whole image (raw ASCII and
                // UTF-16LE); native Initialize 0x65AA90 resolves exactly two extra
                // maps, '0150' -> [castle+0x20] (0x65AB0E) and WayMap/'D701' ->
                // [castle+0x24] (0x65AB47), and InCastleWarArea 0x659FD4 consults
                // only those two plus the castle map itself.
                Save();
                return;
            }
            for (var i = 0; i < _castleList.Count; i++)
            {
                Castle = _castleList[i];
                Castle.Initialize();
            }
        }

        
        public TUserCastle IsCastlePalaceEnvir(Envirnoment Envir)
        {
            TUserCastle result = null;
            TUserCastle Castle = null;
            for (var i = 0; i < _castleList.Count; i++)
            {
                Castle = _castleList[i];
                if (Castle.m_MapPalace == Envir)
                {
                    result = Castle;
                    break;
                }
            }
            return result;
        }

        
        public TUserCastle IsCastleEnvir(Envirnoment Envir)
        {
            TUserCastle result = null;
            TUserCastle Castle = null;
            for (var i = 0; i < _castleList.Count; i++)
            {
                Castle = _castleList[i];
                if (Castle.m_MapCastle == Envir)
                {
                    result = Castle;
                    break;
                }
            }
            return result;
        }

        public TUserCastle IsCastleMember(TBaseObject BaseObject)
        {
            for (var i = 0; i < _castleList.Count; i++)
            {
                if (_castleList[i].IsMember(BaseObject))
                {
                    return _castleList[i];
                }
            }
            return null;
        }

        public void Run()
        {
            for (var i = 0; i < _castleList.Count; i++)
            {
                _castleList[i].Run();
            }
        }

        public void GetCastleGoldInfo(ArrayList List)
        {
            for (var i = 0; i < _castleList.Count; i++)
            {
                TUserCastle Castle = _castleList[i];
                List.Add(string.Format(M2Share.g_sGameCommandSbkGoldShowMsg, Castle.m_sName, Castle.m_nTotalGold, Castle.m_nTodayIncome));
            }
        }

        public bool Save()
        {
            TUserCastle Castle;
            var success = SaveCastleList();
            for (var i = 0; i < _castleList.Count; i++)
            {
                Castle = _castleList[i];
                success &= Castle.Save();
            }
            return success;
        }

        public void LoadCastleList()
        {
            var castleFile = Path.Combine(M2Share.sConfigPath, M2Share.g_Config.sCastleFile);
            if (File.Exists(castleFile))
            {
                using (var loadList = new StringList())
                {
                    loadList.LoadFromFile(castleFile);
                    for (var i = 0; i < loadList.Count; i++)
                    {
                        var sCastleDir = loadList[i].Trim();
                        if (!string.IsNullOrEmpty(sCastleDir))
                        {
                            var castle = new TUserCastle(sCastleDir);
                            _castleList.Add(castle);
                        }
                    }
                }
                M2Share.MainOutMessage($"已读取 [{_castleList.Count}] 个城堡信息...", messageColor: ConsoleColor.Green);
            }
            else
            {
                M2Share.MainOutMessage("城堡列表文件未找到!!!");
            }
        }

        private bool SaveCastleList()
        {
            try
            {
                var castleDirPath = Path.Combine(M2Share.sConfigPath, M2Share.g_Config.sCastleDir);
                if (!Directory.Exists(castleDirPath))
                {
                    Directory.CreateDirectory(castleDirPath);
                }
                var loadList = new StringList();
                for (var i = 0; i < _castleList.Count; i++)
                {
                    loadList.Add(i.ToString());
                }
                var savePath = Path.Combine(M2Share.sConfigPath, M2Share.g_Config.sCastleFile);
                AtomicFile.WriteAllText(savePath, loadList.Text, HUtil32.GbkEncoding);
                return true;
            }
            catch (Exception ex)
            {
                M2Share.ErrorMessage($"保存城堡列表失败: {ex.Message}");
                return false;
            }
        }

        public TUserCastle GetCastle(int nIndex)
        {
            TUserCastle result = null;
            if (nIndex >= 0 && nIndex < _castleList.Count)
            {
                result = _castleList[nIndex];
            }
            return result;
        }

        public void GetCastleNameList(IList<string> List)
        {
            TUserCastle Castle;
            for (var i = 0; i < _castleList.Count; i++)
            {
                Castle = _castleList[i];
                List.Add(Castle.m_sName);
            }
        }

        public void IncRateGold(int nGold)
        {
            TUserCastle Castle;
            for (var i = 0; i < _castleList.Count; i++)
            {
                Castle = _castleList[i];
                Castle.IncRateGold(nGold);
            }
        }
    }
}
