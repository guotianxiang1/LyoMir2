using SystemModule;
using SystemModule.Common;

namespace GameSvr
{
    public class AssociationManager
    {
        private readonly IList<Association> GuildList = null;

        public bool AddGuild(string sGuildName, string sChief)
        {
            Association Guild;
            var result = false;
            if (M2Share.CheckGuildName(sGuildName) && FindGuild(sGuildName) == null)
            {
                Guild = new Association(sGuildName);
                Guild.SetGuildInfo(sChief);
                GuildList.Add(Guild);
                SaveGuildList();
                result = true;
            }
            return result;
        }

        public bool DelGuild(string sGuildName)
        {
            Association Guild;
            var result = false;
            for (var i = 0; i < GuildList.Count; i++)
            {
                Guild = GuildList[i];
                if (string.Compare(Guild.sGuildName, sGuildName, StringComparison.OrdinalIgnoreCase) == 0)
                {
                    if (Guild.m_RankList.Count > 1)
                    {
                        break;
                    }
                    Guild.BackupGuildFile();
                    GuildList.RemoveAt(i);
                    SaveGuildList();
                    result = true;
                    break;
                }
            }
            return result;
        }

        public void ClearGuildInf()
        {
            for (var i = 0; i < GuildList.Count; i++)
            {
                GuildList[i] = null;
            }
            GuildList.Clear();
        }

        public AssociationManager()
        {
            GuildList = new List<Association>();
        }

        public Association FindGuild(string sGuildName)
        {
            Association result = null;
            for (var i = 0; i < GuildList.Count; i++)
            {
                if (GuildList[i].sGuildName == sGuildName)
                {
                    result = GuildList[i];
                    break;
                }
            }
            return result;
        }

        /// <summary>
        /// 战神 sub_5E76F0 normalizes guild lookup keys with sub_40BC50,
        /// which folds only ASCII a-z to A-Z before the hash lookup.
        /// </summary>
        internal Association FindGuildNativeAscii(string guildName)
        {
            if (string.IsNullOrEmpty(guildName))
            {
                return null;
            }

            for (var i = 0; i < GuildList.Count; i++)
            {
                var guild = GuildList[i];
                var candidate = guild?.sGuildName;
                if (candidate != null
                    && candidate.Length == guildName.Length
                    && HUtil32.CompareLStr(candidate, guildName,
                        candidate.Length))
                {
                    return guild;
                }
            }
            return null;
        }

        public void LoadGuildInfo()
        {
            StringList LoadList;
            Association Guild;
            string sGuildName;
            if (File.Exists(M2Share.g_Config.sGuildFile))
            {
                LoadList = new StringList();
                LoadList.LoadFromFile(M2Share.g_Config.sGuildFile);
                for (var i = 0; i < LoadList.Count; i++)
                {
                    sGuildName = LoadList[i].Trim();
                    if (sGuildName != "")
                    {
                        Guild = new Association(sGuildName);
                        GuildList.Add(Guild);
                    }
                }
                for (var i = GuildList.Count - 1; i >= 0; i--)
                {
                    Guild = GuildList[i];
                    if (!Guild.LoadGuild())
                    {
                        M2Share.ErrorMessage(Guild.sGuildName + " 读取出错!!!");
                        GuildList.RemoveAt(i);
                        SaveGuildList();
                    }
                }
                M2Share.MainOutMessage($"已读取 [{GuildList.Count}] 个行会信息...", messageColor: ConsoleColor.Green);
            }
            else
            {
                M2Share.MainOutMessage("行会信息文件未找到，初始化为空列表");
            }
        }

        public Association MemberOfGuild(string sName)
        {
            Association result = null;
            for (var i = 0; i < GuildList.Count; i++)
            {
                if (GuildList[i].IsMember(sName))
                {
                    result = GuildList[i];
                    break;
                }
            }
            return result;
        }

        private bool SaveGuildList()
        {
            StringList SaveList;
            if (M2Share.nServerIndex != 0)
            {
                return true;
            }
            SaveList = new StringList();
            for (var i = 0; i < GuildList.Count; i++)
            {
                SaveList.Add(GuildList[i].sGuildName);
            }
            try
            {
                AtomicFile.WriteAllText(M2Share.g_Config.sGuildFile, SaveList.Text, HUtil32.GbkEncoding);
                return true;
            }
            catch (Exception ex)
            {
                M2Share.ErrorMessage($"保存行会列表失败: {ex.Message}");
                return false;
            }
        }

        public void Run()
        {
            Association Guild;
            bool boChanged;
            TWarGuild WarGuild;
            for (var i = 0; i < GuildList.Count; i++)
            {
                Guild = GuildList[i];
                boChanged = false;
                for (var j = Guild.GuildWarList.Count - 1; j >= 0; j--)
                {
                    WarGuild = Guild.GuildWarList[j];
                    if ((HUtil32.GetTickCount() - WarGuild.dwWarTick) > WarGuild.dwWarTime)
                    {
                        Guild.EndGuildWar(WarGuild.Guild);
                        Guild.GuildWarList.RemoveAt(j);
                        WarGuild = null;
                        boChanged = true;
                    }
                }
                if (boChanged)
                {
                    Guild.UpdateGuildFile();
                }
                Guild.CheckSaveGuildFile();
            }
        }
    }
}
