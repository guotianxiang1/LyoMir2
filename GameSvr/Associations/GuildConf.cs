using SystemModule.Common;

namespace GameSvr
{
    public class GuildConf : IniFile
    {
        public GuildConf(string guidName, string fileName) : base(fileName)
        {
            if (!File.Exists(fileName))
            {
                WriteString("Guild", "GuildName", guidName);
            }
            Load();
        }

        public void LoadConfig(Association guild)
        {
            guild.m_nBuildPoint = ReadInteger("Guild", "BuildPoint", guild.m_nBuildPoint);
            guild.m_nAurae = ReadInteger("Guild", "Aurae", guild.m_nAurae);
            guild.m_nStability = ReadInteger("Guild", "Stability", guild.m_nStability);
            guild.m_nFlourishing = ReadInteger("Guild", "Flourishing", guild.m_nFlourishing);
            guild.m_nChiefItemCount = ReadInteger("Guild", "ChiefItemCount", guild.m_nChiefItemCount);
        }

        public void SaveGuildConfig(Association guild)
        {
            SetCachedString("Guild", "GuildName", guild.sGuildName);
            SetCachedInteger("Guild", "BuildPoint", guild.m_nBuildPoint);
            SetCachedInteger("Guild", "Aurae", guild.m_nAurae);
            SetCachedInteger("Guild", "Stability", guild.m_nStability);
            SetCachedInteger("Guild", "Flourishing", guild.m_nFlourishing);
            SetCachedInteger("Guild", "ChiefItemCount", guild.m_nChiefItemCount);
            Save();
        }
    }
}
