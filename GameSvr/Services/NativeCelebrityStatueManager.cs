using System.Text;
using SystemModule;
using SystemModule.Common;
using GameSvr.Services;

namespace GameSvr
{
    public static class NativeCelebrityStatueManager
    {
        private const string CastleSection = "CastleOwenrStatue";
        private static readonly object CastleOwnerSync = new object();

        public static void Initialize(NormNpc npc)
        {
            if (npc == null)
                return;

            if (npc.m_wAppr >= 50 && npc.m_wAppr <= 55)
            {
                npc.m_boCelebrityStatue = true;
                npc.m_btJob = (byte)((npc.m_wAppr - 50) / 2);
                npc.m_btGender = (npc.m_wAppr - 50) % 2 == 0
                    ? PlayGender.Man
                    : PlayGender.WoMan;
                Load(npc, GetHeroFile(), $"hero{npc.m_wAppr - 50}");
                return;
            }

            if (npc.m_wAppr == 156)
            {
                npc.m_boCelebrityStatue = true;
                npc.m_boCastleOwnerStatue = true;
                Load(npc, GetCastleOwnerFile(), CastleSection);
            }
        }

        public static string GetCelebrityName(NormNpc npc)
        {
            return npc?.m_boCelebrityStatue == true
                ? npc.m_sCelebrityPlayerName ?? string.Empty
                : string.Empty;
        }

        /// <summary>
        /// Native GetCelebLv (PAS proc, registrar sub_738720 on class 0x0063CFA8 = TPsNpc,
        /// handler sub_64FF5C). The whole native body is two instructions:
        /// <c>movzx eax, word ptr [eax+5AAh]; ret</c> — an unconditional WORD read of the
        /// statue's level field, with no statue-kind guard. The same +0x5AA slot is written by
        /// ReqBecomeCeleb (sub_64FDC0 @0x64FE40 <c>mov [edx+5AAh], ax</c>, the applicant's
        /// level) and persisted by sub_643350 @0x643737 under the Hero.ini key '等级', which
        /// identifies it as <see cref="NormNpc.m_wCelebrityLevel"/>. Faithful port keeps the
        /// unguarded read: a non-statue NPC natively yields whatever the slot holds, which for
        /// a never-initialised statue is 0.
        /// </summary>
        public static int GetCelebrityLevel(NormNpc npc)
        {
            return npc?.m_wCelebrityLevel ?? 0;
        }

        public static bool SetCelebrityColor(NormNpc npc, bool enabled)
        {
            if (npc?.m_boCelebrityStatue != true)
                return false;

            var oldColor = npc.m_boCelebrityColor;
            npc.m_boCelebrityColor = enabled;
            var fileName = npc.m_boCastleOwnerStatue ? GetCastleOwnerFile() : GetHeroFile();
            var section = npc.m_boCastleOwnerStatue
                ? CastleSection
                : $"hero{npc.m_wAppr - 50}";
            try
            {
                var config = new StatueIni(fileName);
                config.OpenIfPresent();
                config.Store(section, npc);
                var changed = npc.SetBodyState(Grobal2.STATE_CELEBRITY, enabled);
                if (changed && npc.m_PEnvir != null)
                    npc.StatusChanged();
                return true;
            }
            catch (Exception ex)
            {
                npc.m_boCelebrityColor = oldColor;
                M2Share.ErrorMessage($"保存原生雕像配置失败: {ex.Message}");
                return false;
            }
        }

        public static int TryBecomeCelebrity(NormNpc npc, TPlayObject player)
        {
            if (npc?.m_boCelebrityStatue != true || npc.m_boCastleOwnerStatue || player == null)
                return -1;
            if (player.m_Abil.Level < 35)
                return -2;
            if (player.m_btJob != npc.m_btJob || player.m_btGender != npc.m_btGender)
                return -3;
            if (npc.m_wCelebrityLevel > player.m_Abil.Level
                || npc.m_wCelebrityLevel == player.m_Abil.Level
                && npc.m_nCelebrityExperience >= player.m_Abil.Exp)
                return -5;

            var oldName = npc.m_sCelebrityPlayerName;
            var oldLevel = npc.m_wCelebrityLevel;
            var oldExperience = npc.m_nCelebrityExperience;
            var oldCreatedAt = npc.m_CelebrityCreatedAt;
            npc.m_sCelebrityPlayerName = TruncateGbk(player.m_sCharName, 15);
            npc.m_wCelebrityLevel = player.m_Abil.Level;
            npc.m_nCelebrityExperience = player.m_Abil.Exp;
            npc.m_CelebrityCreatedAt = DateTime.Now;
            try
            {
                var config = new StatueIni(GetHeroFile());
                config.OpenIfPresent();
                config.Store($"hero{npc.m_wAppr - 50}", npc);
            }
            catch (Exception ex)
            {
                npc.m_sCelebrityPlayerName = oldName;
                npc.m_wCelebrityLevel = oldLevel;
                npc.m_nCelebrityExperience = oldExperience;
                npc.m_CelebrityCreatedAt = oldCreatedAt;
                M2Share.ErrorMessage($"保存天下第一雕像失败: {ex.Message}");
                return -1;
            }

            if (npc.m_PEnvir != null)
                npc.SendRefMsg(Grobal2.RM_USERNAME, 0, 0, 0, 0, npc.GetShowName());
            return 0;
        }

        public static int TrySetCastleOwner(TPlayObject player)
        {
            if (player == null)
                return -1;

            lock (CastleOwnerSync)
            {
                var npc = FindCastleOwnerStatue();
                if (npc == null)
                    return -1;

                var playerName = TruncateGbk(player.m_sCharName, 15);
                if (NativeNameEquals(playerName,
                        npc.m_sCelebrityPlayerName ?? string.Empty))
                    return 0;

                var oldJob = npc.m_btJob;
                var oldGender = npc.m_btGender;
                npc.m_wAppr = GetCastleOwnerAppearance(
                    player.m_btJob, player.m_btGender);

                if (npc.m_boIsHide)
                {
                    npc.m_boIsHide = false;
                    if (npc.m_PEnvir != null)
                    {
                        npc.SendRefMsg(Grobal2.RM_HIT, npc.m_btDirection,
                            npc.m_nCurrX, npc.m_nCurrY, 0, string.Empty);
                    }
                }
                else if (oldJob != player.m_btJob
                         || oldGender != player.m_btGender)
                {
                    if (npc.m_PEnvir != null)
                        npc.FeatureChanged();
                    npc.m_btJob = player.m_btJob;
                    npc.m_btGender = player.m_btGender;
                }

                npc.m_sCelebrityPlayerName = playerName;
                if (npc.m_PEnvir != null)
                    npc.RefShowName();

                if (npc.m_boCelebrityColor)
                {
                    npc.m_boCelebrityColor = false;
                    var changed = npc.SetBodyState(Grobal2.STATE_CELEBRITY, false);
                    if (changed && npc.m_PEnvir != null)
                        npc.StatusChanged();
                }

                var fileName = GetCastleOwnerFile();
                if (!File.Exists(fileName))
                {
                    M2Share.ErrorMessage($"[Error]{fileName}");
                    return 1;
                }

                var config = new StatueIni(fileName);
                config.OpenIfPresent();
                config.Store(CastleSection, npc);

                return 1;
            }
        }

        public static string TruncateGbk(string value, int maxBytes)
        {
            if (string.IsNullOrEmpty(value) || maxBytes <= 0)
                return string.Empty;
            var builder = new StringBuilder(value.Length);
            var byteCount = 0;
            foreach (var character in value)
            {
                var charBytes = HUtil32.GbkEncoding.GetByteCount(new[] { character });
                if (byteCount + charBytes > maxBytes)
                    break;
                builder.Append(character);
                byteCount += charBytes;
            }
            return builder.ToString();
        }

        private static void Load(NormNpc npc, string fileName, string section)
        {
            if (!File.Exists(fileName) || new FileInfo(fileName).Length == 0)
            {
                if (string.Equals(fileName, GetHeroFile(), StringComparison.OrdinalIgnoreCase))
                    NativeStartupConfigValidation.ReportHeroIniMissing(fileName);
                return;
            }
            try
            {
                var config = new StatueIni(fileName);
                config.OpenIfPresent();
                if (npc.m_boCastleOwnerStatue)
                {
                    npc.m_btJob = unchecked((byte)config.ReadInteger(
                        section, "Job", npc.m_btJob));
                    npc.m_btGender = (PlayGender)unchecked((byte)config.ReadInteger(
                        section, "Gender", (int)npc.m_btGender));
                    npc.m_wAppr = GetCastleOwnerAppearance(
                        npc.m_btJob, npc.m_btGender);
                    _ = config.ReadInteger(section, "Requested", 0);
                    npc.m_boIsHide = false;
                    npc.m_sCelebrityPlayerName = TruncateGbk(config.ReadString(
                        section, "Name", npc.m_sCelebrityPlayerName ?? string.Empty), 15);
                }
                else
                {
                    npc.m_sCelebrityPlayerName = config.ReadString(
                        section, "Name", string.Empty);
                    npc.m_sCelebrityPlayerName = config.ReadString(
                        section, "角色名", npc.m_sCelebrityPlayerName);
                    npc.m_wCelebrityLevel = (ushort)Math.Clamp(
                        config.ReadInteger(section, "等级", 0), 0, ushort.MaxValue);
                    npc.m_nCelebrityExperience = Math.Max(0,
                        config.ReadInteger(section, "经验", 0));
                    npc.m_wCelebrityInnerPower = (ushort)Math.Clamp(
                        config.ReadInteger(section, "内功", 0), 0, ushort.MaxValue);
                    npc.m_wCelebrityMindLevel = (ushort)Math.Clamp(
                        config.ReadInteger(section, "心法等级", 0), 0, ushort.MaxValue);
                }
                npc.m_boCelebrityColor = config.ReadInteger(
                    section, npc.m_boCastleOwnerStatue ? "Color" : "颜色", 0) != 0;
                npc.SetBodyState(Grobal2.STATE_CELEBRITY, npc.m_boCelebrityColor);
            }
            catch (Exception ex)
            {
                M2Share.ErrorMessage($"读取原生雕像配置失败: {ex.Message}");
            }
        }

        private static string GetHeroFile()
        {
            return Path.GetFullPath(Path.Combine(
                M2Share.sConfigPath,
                M2Share.g_Config?.sBaseDir ?? string.Empty,
                "config",
                "Hero.ini"));
        }

        private static string GetCastleOwnerFile()
        {
            return Path.GetFullPath(Path.Combine(
                M2Share.sConfigPath,
                M2Share.g_Config?.sCastleDir ?? string.Empty,
                "沙巴克城主雕像.ini"));
        }

        private sealed class StatueIni : IniFile
        {
            public StatueIni(string fileName) : base(fileName)
            {
            }

            public void OpenIfPresent()
            {
                if (File.Exists(FileName) && new FileInfo(FileName).Length > 0)
                    Load();
            }

            public void Store(string section, NormNpc npc)
            {
                if (npc.m_boCastleOwnerStatue)
                {
                    SetCachedInteger(section, "Job", npc.m_btJob);
                    SetCachedInteger(section, "Gender", (int)npc.m_btGender);
                    SetCachedInteger(section, "Requested", npc.m_boIsHide ? 0 : 1);
                    SetCachedString(section, "Name", npc.m_sCelebrityPlayerName ?? string.Empty);
                    SetCachedInteger(section, "Color", npc.m_boCelebrityColor ? 1 : 0);
                }
                else
                {
                    SetCachedString(section, "角色名", npc.m_sCelebrityPlayerName ?? string.Empty);
                    SetCachedInteger(section, "等级", npc.m_wCelebrityLevel);
                    SetCachedInteger(section, "经验", npc.m_nCelebrityExperience);
                    SetCachedInteger(section, "内功", npc.m_wCelebrityInnerPower);
                    SetCachedInteger(section, "心法等级", npc.m_wCelebrityMindLevel);
                    SetCachedString(section, "创建时间",
                        (npc.m_CelebrityCreatedAt == default ? DateTime.Now : npc.m_CelebrityCreatedAt)
                        .ToString("yyyy-MM-dd HH:mm:ss"));
                    SetCachedInteger(section, "颜色", npc.m_boCelebrityColor ? 1 : 0);
                }
                Save();
            }
        }

        private static NormNpc FindCastleOwnerStatue()
        {
            var userEngine = M2Share.UserEngine;
            if (userEngine == null)
                return null;

            var merchants = userEngine.SnapshotMerchants();
            for (var i = merchants.Length - 1; i >= 0; i--)
            {
                var npc = merchants[i];
                if (npc?.m_boCastleOwnerStatue == true && !npc.m_boGhost)
                    return npc;
            }
            return null;
        }

        private static ushort GetCastleOwnerAppearance(byte job, PlayGender gender)
        {
            var genderValue = (int)gender;
            return job <= 2 && (uint)genderValue <= 1
                ? (ushort)(156 + job * 2 + genderValue)
                : (ushort)156;
        }

        private static bool NativeNameEquals(string left, string right)
        {
            var leftBytes = HUtil32.GbkEncoding.GetBytes(left ?? string.Empty);
            var rightBytes = HUtil32.GbkEncoding.GetBytes(right ?? string.Empty);
            if (leftBytes.Length != rightBytes.Length)
                return false;

            for (var i = 0; i < leftBytes.Length; i++)
            {
                var leftByte = FoldAscii(leftBytes[i]);
                var rightByte = FoldAscii(rightBytes[i]);
                if (leftByte != rightByte)
                    return false;
            }
            return true;
        }

        private static byte FoldAscii(byte value)
        {
            return value >= (byte)'a' && value <= (byte)'z'
                ? (byte)(value - ('a' - 'A'))
                : value;
        }
    }
}
