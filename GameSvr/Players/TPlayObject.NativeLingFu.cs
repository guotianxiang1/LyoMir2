using System;
using System.Buffers.Binary;
using SystemModule;

namespace GameSvr
{
    public sealed class NativeCreditCardAccount
    {
        internal readonly object SyncRoot = new();
        internal long DirtyVersion;
        public bool Loaded;
        public bool Dirty;
        public int Value;
        public int Value2;
        public int UsedValue;
        public uint Index;
        public int LastSaveTick;
        public bool GloryPointDirty;
        public int GloryPointValue;
        public int GloryPointPeriod;
        internal long GloryPointDirtyVersion;

        internal void Reset(int currentTick)
        {
            Loaded = false;
            Dirty = false;
            Value = 0;
            Value2 = 0;
            UsedValue = 0;
            Index = 0;
            LastSaveTick = currentTick;
            GloryPointDirty = false;
            GloryPointValue = 0;
            GloryPointPeriod = 0;
            GloryPointDirtyVersion = 0;
            DirtyVersion = 0;
        }

        internal void ClearMonthly()
        {
            Value2 = 0;
            Dirty = true;
            DirtyVersion++;
        }

        internal void ClearAll(int currentTick)
        {
            Value = 0;
            Value2 = 0;
            UsedValue = 0;
            Dirty = false;
            LastSaveTick = currentTick;
            DirtyVersion++;
        }
    }

    public partial class TPlayObject
    {
        public int m_nLingFu;
        public int m_nUsedLingFu;
        internal int m_nNativeDiamondCache;
        public readonly NativeCreditCardAccount m_CreditCard = new();
        private readonly object m_NativeLingFuReasonSync = new();
        private readonly int[] m_NativeLingFuReasonSessionBuckets = new int[10];
        private readonly int[] m_NativeLingFuReasonBuckets = new int[10];

        public bool TryGetNativeLingFuBalance(out int balance)
        {
            var service = M2Share.CreditCardService ?? NativeCreditCardService.Disabled;
            lock (m_CreditCard.SyncRoot)
            {
                if (service.Enabled && !m_CreditCard.Loaded)
                {
                    balance = 0;
                    return false;
                }

                balance = m_nLingFu;
                var creditBalance = unchecked(m_CreditCard.Value + m_CreditCard.Value2);
                if (service.Enabled && creditBalance > 0)
                    balance = unchecked(balance + creditBalance);
                return true;
            }
        }

        public bool AddNativeLingFu(int reason, int amount, bool writeLog = true)
        {
            if (amount <= 0) return false;
            lock (m_CreditCard.SyncRoot)
                m_nLingFu = unchecked(m_nLingFu + amount);
            RefreshNativeLingFu();
            if (writeLog)
                AddNativeLingFuLog(9, "灵符", reason, amount, "npc给予：");
            return true;
        }

        public bool AddNativeLimitedLingFu(int reason, int amount)
        {
            if (amount <= 0) return false;
            lock (m_CreditCard.SyncRoot)
            {
                if (!m_CreditCard.Loaded) return false;
                var value = unchecked(m_CreditCard.Value + amount);
                m_CreditCard.Value = value < 0 ? 0 : value;
                m_CreditCard.Dirty = true;
                m_CreditCard.DirtyVersion++;
            }
            RefreshNativeLingFu();
            AddNativeLingFuLog(9, "限时灵符", reason, amount, "npc给予");
            return true;
        }

        public bool DecNativeLingFu(int reason, int amount)
        {
            if (reason < 0 || amount < 0) return false;

            var service = M2Share.CreditCardService ?? NativeCreditCardService.Disabled;
            var insufficient = false;
            lock (m_CreditCard.SyncRoot)
            {
                if (service.Enabled && !m_CreditCard.Loaded)
                    return false;

                var creditBalance = unchecked(m_CreditCard.Value + m_CreditCard.Value2);
                var balance = m_nLingFu;
                if (service.Enabled && creditBalance > 0)
                    balance = unchecked(balance + creditBalance);

                if (balance < amount)
                {
                    insufficient = true;
                }
                else
                {
                    var remaining = amount;
                    if (service.Enabled && creditBalance > 0 && remaining > 0)
                    {
                        var creditDebit = remaining > creditBalance
                            ? creditBalance
                            : remaining;
                        remaining = unchecked(remaining - creditDebit);

                        var valueDebit = 0;
                        if (creditDebit > m_CreditCard.Value2)
                        {
                            valueDebit = unchecked(creditDebit - m_CreditCard.Value2);
                            m_CreditCard.Value2 = 0;
                        }
                        else
                        {
                            m_CreditCard.Value2 = unchecked(m_CreditCard.Value2 - creditDebit);
                        }

                        if (valueDebit > 0)
                        {
                            m_CreditCard.Value = unchecked(m_CreditCard.Value - valueDebit);
                            m_CreditCard.UsedValue = unchecked(
                                m_CreditCard.UsedValue + valueDebit);
                        }
                        m_CreditCard.Dirty = true;
                        m_CreditCard.DirtyVersion++;
                    }

                    if (remaining > 0)
                        m_nLingFu = unchecked(m_nLingFu - remaining);
                    if (reason is not (30_003 or 30_006))
                        AddNativeLingFuDebitLog(reason, amount);
                    m_nUsedLingFu = unchecked(m_nUsedLingFu + amount);
                }
            }

            if (insufficient)
            {
                SendMsg(this, Grobal2.RM_SYSMESSAGE, 0, 0xFF, 0xFC, 0,
                    "灵符不足");
                return false;
            }

            RefreshNativeLingFu();
            AddNativeLingFuReasonUsage(reason, amount);
            return true;
        }

        internal bool TryGetNativeLingFuReasonBuckets(out int[] buckets)
        {
            lock (m_NativeLingFuReasonSync)
            {
                var total = 0;
                for (var i = 0; i < m_NativeLingFuReasonBuckets.Length; i++)
                    total = unchecked(total + m_NativeLingFuReasonBuckets[i]);
                if (total <= 0)
                {
                    buckets = null;
                    return false;
                }

                buckets = (int[])m_NativeLingFuReasonBuckets.Clone();
                return true;
            }
        }

        internal void ClearNativeLingFuReasonBuckets()
        {
            lock (m_NativeLingFuReasonSync)
            {
                Array.Clear(m_NativeLingFuReasonSessionBuckets, 0,
                    m_NativeLingFuReasonSessionBuckets.Length);
                Array.Clear(m_NativeLingFuReasonBuckets, 0,
                    m_NativeLingFuReasonBuckets.Length);
            }
        }

        private void AddNativeLingFuReasonUsage(int reason, int amount)
        {
            if ((uint)reason >= m_NativeLingFuReasonBuckets.Length) return;
            lock (m_NativeLingFuReasonSync)
            {
                m_NativeLingFuReasonSessionBuckets[reason] = unchecked(
                    m_NativeLingFuReasonSessionBuckets[reason] + amount);
                m_NativeLingFuReasonBuckets[reason] = unchecked(
                    m_NativeLingFuReasonBuckets[reason] + amount);
            }
        }

        private void AddNativeMagicTowerLingFuUsage()
        {
            AddNativeLingFuReasonUsage(1, 1);
        }

        private void AddNativeLingFuDebitLog(int reason, int amount)
        {
            AddNativeLingFuLog(10, "灵符", reason, amount, "npc扣除");
        }

        private void AddNativeLingFuLog(int type, string itemName, int reason,
            int amount, string npcPrefix)
        {
            var description = m_NPC == null
                ? string.Empty
                : npcPrefix + m_NPC.m_sCharName + '-' + m_NPC.m_sMapName;
            M2Share.AddGameDataLog(string.Join('\t', type, m_sMapName,
                m_nCurrX, m_nCurrY, m_sCharName, itemName, reason, amount,
                description));
        }

        internal void RefreshNativeLingFu()
        {
            SendMsg(this, Grobal2.RM_LINGFU_CHANGED, 0, 0, 0, 0, string.Empty);
        }

        internal byte[] BuildNativeCapitalInfoBody()
        {
            var body = new byte[24];
            lock (m_CreditCard.SyncRoot)
            {
                BinaryPrimitives.WriteInt32LittleEndian(body.AsSpan(0, 4), m_nLingFu);
                BinaryPrimitives.WriteInt32LittleEndian(body.AsSpan(4, 4), m_nGameGold);
                BinaryPrimitives.WriteInt32LittleEndian(body.AsSpan(8, 4),
                    m_nNativeDiamondCache);
                BinaryPrimitives.WriteInt32LittleEndian(body.AsSpan(12, 4), m_CreditCard.Value);
                BinaryPrimitives.WriteInt32LittleEndian(body.AsSpan(16, 4),
                    m_CreditCard.GloryPointValue);
                BinaryPrimitives.WriteInt32LittleEndian(body.AsSpan(20, 4),
                    m_CreditCard.GloryPointPeriod);
            }
            return body;
        }

        internal int GetNativeDiamondCount()
        {
            var result = 0;
            var userEngine = M2Share.UserEngine;
            if (userEngine == null) return result;
            foreach (var item in m_ItemList)
            {
                if (item == null) continue;
                var stdItem = userEngine.GetStdItem(item.wIndex);
                if (stdItem != null && string.Equals(stdItem.Name, "金刚石",
                        StringComparison.Ordinal))
                    result = unchecked(result + item.Dura);
            }
            return result;
        }

        internal void InitializeNativeDiamondCacheAfterLogon()
        {
            m_nNativeDiamondCache = GetNativeDiamondCount();
            RefreshNativeLingFu();
        }

        internal void SendNativeCapitalInfo()
        {
            var header = Grobal2.MakeDefaultMsg(Grobal2.SM_GETDIAMNUM_EXT, 0, 0, 0, 0);
            SendSocket(header, BuildNativeCapitalInfoBody());
        }
    }
}
