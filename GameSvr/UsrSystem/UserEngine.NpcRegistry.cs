namespace GameSvr
{
    public partial class UserEngine
    {
        private static readonly object NpcRegistryFallbackSync = new object();
        private readonly HashSet<NormNpc> _dynamicRoomQuestNpcs = new(
            ReferenceEqualityComparer.Instance);

        // GameApp installs the process lock after constructing UserEngine.
        private object NpcRegistrySync =>
            Volatile.Read(ref M2Share.ProcessHumanCriticalSection)
            ?? NpcRegistryFallbackSync;

        public bool TryAddMerchantExact(Merchant expected)
        {
            if (expected == null) return false;

            lock (NpcRegistrySync)
            {
                if (ContainsReference(m_MerchantList, expected)) return false;
                m_MerchantList.Add(expected);
                return true;
            }
        }

        public bool TryRemoveMerchantExact(Merchant expected)
        {
            if (expected == null) return false;

            lock (NpcRegistrySync)
            {
                return RemoveReferences(m_MerchantList, expected,
                    ref nMerchantPosition);
            }
        }

        public Merchant[] SnapshotMerchants()
        {
            lock (NpcRegistrySync)
            {
                var snapshot = new Merchant[m_MerchantList.Count];
                m_MerchantList.CopyTo(snapshot, 0);
                return snapshot;
            }
        }

        public bool TryAddQuestNpcExact(NormNpc expected)
        {
            if (expected == null) return false;

            lock (NpcRegistrySync)
            {
                if (ContainsReference(QuestNPCList, expected)) return false;
                QuestNPCList.Add(expected);
                return true;
            }
        }

        public bool TryAddDynamicRoomQuestNpcExact(NormNpc expected)
        {
            if (expected == null) return false;

            lock (NpcRegistrySync)
            {
                if (ContainsReference(QuestNPCList, expected)
                    || !_dynamicRoomQuestNpcs.Add(expected))
                    return false;
                QuestNPCList.Add(expected);
                return true;
            }
        }

        public bool TryRemoveQuestNpcExact(NormNpc expected)
        {
            if (expected == null) return false;

            lock (NpcRegistrySync)
            {
                var removed = RemoveReferences(QuestNPCList, expected,
                    ref nNpcPosition);
                if (removed) _dynamicRoomQuestNpcs.Remove(expected);
                return removed;
            }
        }

        public bool TryRemoveRegisteredNpcEverywhereExact(NormNpc expected)
        {
            if (expected == null) return false;

            lock (NpcRegistrySync)
            {
                var removedMerchant = expected is Merchant merchant
                    && RemoveReferences(m_MerchantList, merchant,
                        ref nMerchantPosition);
                var removedQuestNpc = RemoveReferences(QuestNPCList, expected,
                    ref nNpcPosition);
                if (removedQuestNpc) _dynamicRoomQuestNpcs.Remove(expected);
                return removedMerchant || removedQuestNpc;
            }
        }

        public NormNpc[] SnapshotQuestNpcs()
        {
            lock (NpcRegistrySync)
            {
                var snapshot = new NormNpc[QuestNPCList.Count];
                QuestNPCList.CopyTo(snapshot, 0);
                return snapshot;
            }
        }

        public NormNpc[] SnapshotReloadableQuestNpcs()
        {
            lock (NpcRegistrySync)
            {
                return QuestNPCList.Where(npc =>
                        !_dynamicRoomQuestNpcs.Contains(npc))
                    .ToArray();
            }
        }

        public bool IsDynamicRoomQuestNpcExact(NormNpc expected)
        {
            if (expected == null) return false;
            lock (NpcRegistrySync)
            {
                return _dynamicRoomQuestNpcs.Contains(expected)
                       && ContainsReference(QuestNPCList, expected);
            }
        }

        public void SnapshotNpcRegistry(out Merchant[] merchants,
            out NormNpc[] questNpcs)
        {
            lock (NpcRegistrySync)
            {
                merchants = new Merchant[m_MerchantList.Count];
                m_MerchantList.CopyTo(merchants, 0);
                questNpcs = new NormNpc[QuestNPCList.Count];
                QuestNPCList.CopyTo(questNpcs, 0);
            }
        }

        public bool ContainsRegisteredNpcExact(TBaseObject expected)
        {
            if (expected == null) return false;

            lock (NpcRegistrySync)
            {
                return (expected is Merchant merchant
                        && ContainsReference(m_MerchantList, merchant))
                    || (expected is NormNpc npc
                        && ContainsReference(QuestNPCList, npc));
            }
        }

        private static bool ContainsReference<T>(IList<T> list, T expected)
            where T : class
        {
            for (var i = 0; i < list.Count; i++)
            {
                if (ReferenceEquals(list[i], expected)) return true;
            }
            return false;
        }

        private static bool RemoveReferences<T>(IList<T> list, T expected,
            ref int processPosition)
            where T : class
        {
            var removed = false;
            for (var i = list.Count - 1; i >= 0; i--)
            {
                if (!ReferenceEquals(list[i], expected)) continue;
                list.RemoveAt(i);
                if (i < processPosition) processPosition--;
                removed = true;
            }

            if (processPosition < 0 || processPosition >= list.Count)
                processPosition = 0;
            return removed;
        }
    }
}
