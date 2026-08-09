using ProtoBuf;
using System.Collections.Generic;

namespace SystemModule
{
    [ProtoContract]
    public class THumDataInfo
    {
        [ProtoMember(1)] public TRecordHeader Header { get; set; }
        [ProtoMember(2)] public THumInfoData Data { get; set; }

        // Raw native records are carried between DBServer and M2 so fields that are not yet
        // represented by the C# model survive a load/save cycle byte-for-byte.
        [ProtoMember(3, OverwriteList = true)] public byte[] NativeData { get; set; }
        [ProtoMember(4)] public uint NativeDataCrc { get; set; }
        [ProtoMember(5, OverwriteList = true)] public byte[] NativeScriptData { get; set; }
        [ProtoMember(6)] public uint NativeScriptDataCrc { get; set; }
        [ProtoMember(7)] public long NativeUserId { get; set; }

        public THumDataInfo()
        {
            Header = new TRecordHeader();
            Data = new THumInfoData();
            Data.Initialization();
        }

        public void PrepareForTransport()
        {
            Header ??= new TRecordHeader();
            Data ??= new THumInfoData();
            Data.PrepareForTransport();
        }

        public void RestoreAfterTransport()
        {
            Data?.RestoreAfterTransport();
        }
    }

    [ProtoContract]
    public class SaveHumDataPacket : CmdPacket
    {
        [ProtoMember(1)]
        public string sAccount { get; set; }
        [ProtoMember(2)]
        public string sCharName { get; set; }
        [ProtoMember(3)]
        public THumDataInfo HumDataInfo { get; set; }
    }

    [ProtoContract]
    public class LoadHumDataPacket : CmdPacket
    {
        [ProtoMember(1)]
        public string sAccount { get; set; }
        [ProtoMember(2)]
        public string sChrName { get; set; }
        [ProtoMember(3)]
        public string sUserAddr { get; set; }
        [ProtoMember(4)]
        public int nSessionID { get; set; }
    }

    [ProtoContract]
    public class THumInfoData
    {
        [ProtoMember(1)]
        public string sCharName;
        [ProtoMember(2)]
        public string sCurMap;
        [ProtoMember(3)]
        public short wCurX;
        [ProtoMember(4)]
        public short wCurY;
        [ProtoMember(5)]
        public byte btDir;
        [ProtoMember(6)]
        public byte btHair;
        [ProtoMember(7)]
        public byte btSex;
        [ProtoMember(8)]
        public byte btJob;
        [ProtoMember(9)]
        public int nGold;
        [ProtoMember(10)]
        public TAbility Abil;
        [ProtoMember(11, OverwriteList = true)]
        public ushort[] wStatusTimeArr;
        [ProtoMember(12)]
        public string sHomeMap;
        [ProtoMember(13)]
        public short wHomeX;
        [ProtoMember(14)]
        public short wHomeY;
        [ProtoMember(15)]
        public TNakedAbility BonusAbil;
        [ProtoMember(16)]
        public int nBonusPoint;
        [ProtoMember(17)]
        public byte btCreditPoint;
        [ProtoMember(18)]
        public byte btReLevel;
        [ProtoMember(19)]
        public string sMasterName;
        [ProtoMember(20)]
        public bool boMaster;
        [ProtoMember(21)]
        public string sDearName;
        [ProtoMember(22)]
        public string sStoragePwd;
        [ProtoMember(23)]
        public int nGameGold;
        [ProtoMember(24)]
        public int nGamePoint;
        [ProtoMember(25)]
        public int nPayMentPoint;
        [ProtoMember(26)]
        public int nPKPoint;
        [ProtoMember(27)]
        public byte btAllowGroup;
        [ProtoMember(28)]
        public byte btF9;
        [ProtoMember(29)]
        public byte btAttatckMode;
        [ProtoMember(30)]
        public byte btIncHealth;
        [ProtoMember(31)]
        public byte btIncSpell;
        [ProtoMember(32)]
        public byte btIncHealing;
        [ProtoMember(33)]
        public byte btFightZoneDieCount;
        [ProtoMember(34)]
        public byte btEE;
        [ProtoMember(35)]
        public byte btEF;
        [ProtoMember(36)]
        public string sAccount;
        [ProtoMember(37)]
        public bool boLockLogon;
        [ProtoMember(38)]
        public short wContribution;
        [ProtoMember(39)]
        public int nHungerStatus;
        [ProtoMember(40)]
        public bool boAllowGuildReCall;
        [ProtoMember(41)]
        public short wGroupRcallTime;
        [ProtoMember(42)]
        public double dBodyLuck;
        [ProtoMember(43)]
        public bool boAllowGroupReCall;
        [ProtoMember(44, OverwriteList = true)]
        public byte[] QuestUnitOpen;
        [ProtoMember(45, OverwriteList = true)]
        public byte[] QuestUnit;
        [ProtoMember(46, OverwriteList = true)]
        public byte[] QuestFlag;
        [ProtoMember(47)]
        public byte MarryCount;
        [ProtoMember(48, OverwriteList = true)]
        public TUserItem[] HumItems;
        [ProtoMember(49, OverwriteList = true)]
        public TUserItem[] BagItems;
        [ProtoMember(50, OverwriteList = true)]
        public TUserItem[] StorageItems;
        [ProtoMember(51, OverwriteList = true)]
        public TMagicRcd[] Magic;
        [ProtoMember(52, OverwriteList = true)]
        public int[] IntVar;
        [ProtoMember(53)]
        public int ForceLv;
        [ProtoMember(54)]
        public int ForceExp;
        [ProtoMember(55)]
        public int FightPoints;
        [ProtoMember(56)]
        public int sfLevel;
        [ProtoMember(57)]
        public Dictionary<int, int> ScriptV;
        [ProtoMember(58)]
        public Dictionary<int, int> ScriptS;
        [ProtoMember(59)]
        public int StorageSpaceCount;
        [ProtoMember(60)]
        public int nShengWan;
        [ProtoMember(61)]
        public int nNickLinFu;
        [ProtoMember(62)]
        public double NativeHeroIntimacy;
        [ProtoMember(63, OverwriteList = true)]
        public byte[] NativeHeroExperienceAccumulator;
        [ProtoMember(64)]
        public byte btSecHeroPracticeRewardMode;
        [ProtoMember(65)]
        public byte btSecHeroPracticeCostTier;
        [ProtoMember(66)]
        public ushort wSecHeroPracticeLevel;
        [ProtoMember(67)]
        public int nLingFu;
        [ProtoMember(68)]
        public int nUsedLingFu;
        [ProtoMember(69)]
        public byte btGoldActNextLevel;
        [ProtoMember(70)]
        public byte btFirstUsedGiftStage;
        [ProtoMember(71)]
        public int nActivePoint;
        [ProtoMember(72)]
        public bool boStudent;
        [ProtoMember(73)]
        public byte btStudentOrder;
        [ProtoMember(74)]
        public byte btStudentCount;
        [ProtoMember(75, OverwriteList = true)]
        public string[] sStudentNames;
        [ProtoMember(76)]
        public bool boAllowMaster;
        [ProtoMember(77, OverwriteList = true)]
        public int[] ExchangeBookPersonalRareCounters;
        [ProtoMember(78)]
        [System.ComponentModel.DefaultValue(true)]
        public bool boAllowMarry;
        [ProtoMember(79)]
        public bool boMarried;
        /// <summary>
        /// Verbatim copy of the 128-byte opaque social block at
        /// NativeHumanDataCodec.NativeSocialBlockOffset (0x650, length 0x80) in the
        /// native 战神 human record.  It holds eight ShortString[15] slots — spouse
        /// (+0x00), master (+0x10), companion (+0x20) and five students (+0x30..
        /// +0x70) — which 战神 load/save move as ONE block (LOAD 0x6B096C /
        /// SAVE 0x6B168E, rep movsd 0x20).  The names surfaced on sDearName /
        /// sMasterName / sStudentNames are DERIVED from these slots; this blob is the
        /// authoritative bytes and must be preserved byte-for-exact-byte on every
        /// round-trip (including the external ':'/'$' companion string, whose grammar
        /// is not part of 战神 and must never be re-encoded from the derived names).
        /// </summary>
        [ProtoMember(80, OverwriteList = true)]
        public byte[] NativeSocialBlob;

        public THumInfoData()
        {
            ScriptV = new Dictionary<int, int>();
            ScriptS = new Dictionary<int, int>();
            StorageSpaceCount = 48;
            boAllowMarry = true;
            EnsureFixedStudentNames(ref sStudentNames);
        }

        public void Initialization()
        {
            QuestUnitOpen = new byte[128];
            QuestUnit = new byte[128];
            QuestFlag = new byte[128];
            HumItems = new TUserItem[Grobal2.HUMAN_EQUIPPED_ITEM_COUNT];
            BagItems = new TUserItem[48];
            StorageItems = new TUserItem[192];
            Magic = new TMagicRcd[Grobal2.MAXMAGIC];
            Abil = new TAbility();
            BonusAbil = new TNakedAbility();
            wStatusTimeArr = new ushort[12];
            IntVar = System.Array.Empty<int>();
            NativeHeroExperienceAccumulator = new byte[24];
            ExchangeBookPersonalRareCounters = new int[8];
            ScriptV = new Dictionary<int, int>();
            ScriptS = new Dictionary<int, int>();
            EnsureFixedStudentNames(ref sStudentNames);
        }

        internal void PrepareForTransport()
        {
            EnsureTransportArray(ref HumItems, Grobal2.HUMAN_EQUIPPED_ITEM_COUNT);
            EnsureTransportArray(ref BagItems, 48);
            EnsureTransportArray(ref StorageItems, 192);
            EnsureTransportArray(ref Magic, Grobal2.MAXMAGIC);
            EnsureFixedStudentNames(ref sStudentNames);
            EnsureMinimumLength(ref ExchangeBookPersonalRareCounters, 8);
        }

        internal void RestoreAfterTransport()
        {
            EnsureMinimumLength(ref HumItems, Grobal2.HUMAN_EQUIPPED_ITEM_COUNT);
            if (NativeHeroExperienceAccumulator == null ||
                NativeHeroExperienceAccumulator.Length != 24)
                NativeHeroExperienceAccumulator = new byte[24];
            EnsureMinimumLength(ref ExchangeBookPersonalRareCounters, 8);
            RestoreTransportItems(HumItems);
            RestoreTransportItems(BagItems);
            RestoreTransportItems(StorageItems);
            EnsureFixedStudentNames(ref sStudentNames);
            if (Magic == null) return;
            for (var i = 0; i < Magic.Length; i++)
                if (Magic[i]?.IsTransportPlaceholder() == true) Magic[i] = null;
        }

        private static void EnsureTransportArray<T>(ref T[] values, int minimumLength)
            where T : class, new()
        {
            EnsureMinimumLength(ref values, minimumLength);
            T placeholder = null;
            for (var i = 0; i < values.Length; i++)
                values[i] ??= placeholder ??= new T();
        }

        private static void EnsureMinimumLength<T>(ref T[] values, int minimumLength)
        {
            if (values == null) values = new T[minimumLength];
            else if (values.Length < minimumLength) System.Array.Resize(ref values, minimumLength);
        }

        private static void EnsureFixedStudentNames(ref string[] values)
        {
            if (values == null) values = new string[5];
            else if (values.Length != 5) System.Array.Resize(ref values, 5);
            for (var i = 0; i < values.Length; i++)
                values[i] ??= string.Empty;
        }

        private static void RestoreTransportItems(TUserItem[] items)
        {
            if (items == null) return;
            for (var i = 0; i < items.Length; i++)
                if (items[i]?.IsTransportPlaceholder() == true) items[i] = null;
        }
    }
}
