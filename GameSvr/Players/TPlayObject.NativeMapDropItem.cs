using SystemModule;

namespace GameSvr
{
    public partial class TPlayObject
    {
        private readonly object _nativeMapDropTrackerSync = new();
        private readonly Dictionary<Envirnoment, NativeMapDropTrackerEntry>
            _nativeMapDropTracker = new(ReferenceEqualityComparer.Instance);

        private sealed class NativeMapDropTrackerEntry
        {
            internal ushort Count;
            internal long SwitchGeneration;
            internal long RawGeneration;
        }

        internal void TrackNativeMapDropItem(TUserItem item)
        {
            var environment = m_PEnvir;
            if (item == null || environment == null ||
                M2Share.ServerSwitches?.IsBitSet(2, 0x80) != true ||
                environment.NativeMapDropItems.Count == 0)
            {
                return;
            }

            var itemName = M2Share.UserEngine?.GetStdItemName(item.wIndex);
            if (string.IsNullOrEmpty(itemName) ||
                !environment.NativeMapDropItems.Contains(itemName))
            {
                return;
            }

            lock (_nativeMapDropTrackerSync)
            {
                var switchGeneration =
                    NativeMapDropTrackingGeneration.SwitchGeneration;
                var rawGeneration = environment.NativeMapDropItems.Generation;
                if (!_nativeMapDropTracker.TryGetValue(environment,
                        out var entry) ||
                    entry.SwitchGeneration != switchGeneration ||
                    entry.RawGeneration != rawGeneration)
                {
                    entry = new NativeMapDropTrackerEntry
                    {
                        SwitchGeneration = switchGeneration,
                        RawGeneration = rawGeneration
                    };
                    _nativeMapDropTracker[environment] = entry;
                }
                entry.Count = unchecked((ushort)(entry.Count + 1));
            }
        }

        internal int ReleaseNativeMapDropItems(Envirnoment environment,
            bool removeTracker, Func<TUserItem, bool> tryDrop = null)
        {
            if (environment == null || m_boGhost) return 0;

            var releaseEnabled =
                M2Share.ServerSwitches?.IsBitSet(2, 0x80) == true &&
                environment.NativeMapDropItems.Count != 0;

            lock (_nativeMapDropTrackerSync)
            {
                if (!_nativeMapDropTracker.TryGetValue(environment,
                        out var entry))
                {
                    return 0;
                }

                if (entry.SwitchGeneration !=
                        NativeMapDropTrackingGeneration.SwitchGeneration ||
                    entry.RawGeneration !=
                        environment.NativeMapDropItems.Generation)
                {
                    _nativeMapDropTracker.Remove(environment);
                    return 0;
                }

                if (entry.Count == 0) return 0;

                if (removeTracker)
                    _nativeMapDropTracker.Remove(environment);
                else
                    entry.Count = 0;
            }

            // A leave while the switch is closed or the map list is empty must
            // consume provenance without turning it into a delayed future drop.
            if (!releaseEnabled) return 0;

            var configuredNames = environment.NativeMapDropItems.Snapshot();
            var deletedCount = 0;
            try
            {
                foreach (var configuredName in configuredNames)
                {
                    var itemIndex = M2Share.UserEngine?.GetStdItemIdx(
                        configuredName) ?? 0;
                    if (itemIndex < 1) continue;

                    var deletedItems = new List<TDeleteItem>();
                    for (var bagIndex = m_ItemList.Count - 1;
                         bagIndex >= 0; bagIndex--)
                    {
                        var item = m_ItemList[bagIndex];
                        if (item == null || item.wIndex != itemIndex)
                            continue;

                        var stdItem = M2Share.UserEngine.GetStdItem(item.wIndex);
                        if (stdItem == null) continue;
                        if (!item.NativeMapDropAllowed &&
                            (stdItem.NativeReserved02 & 0x0210) != 0)
                        {
                            continue;
                        }

                        var dropped = tryDrop != null
                            ? tryDrop(item)
                            : DropItemDown(item, 2, true, null, this);
                        if (!dropped) continue;

                        deletedItems.Add(new TDeleteItem
                        {
                            sItemName = M2Share.UserEngine.GetStdItemName(
                                item.wIndex),
                            MakeIndex = item.MakeIndex,
                            ClientItemID = EnsureClientItemId(item)
                        });
                        m_ItemList.RemoveAt(bagIndex);
                    }

                    if (deletedItems.Count > 0 && !m_boGhost)
                    {
                        SendMsg(this, Grobal2.RM_SENDDELITEMLIST, 0,
                            deletedItems.Count, 0, 0, "", deletedItems);
                        WeightChanged();
                        deletedCount += deletedItems.Count;
                    }
                }
            }
            catch (Exception exception)
            {
                M2Share.ErrorMessage(
                    "[Exception] TPlayObject::ReleaseNativeMapDropItems " +
                    exception.Message);
            }
            return deletedCount;
        }

        internal ushort NativeMapDropTrackedCount(Envirnoment environment)
        {
            if (environment == null) return 0;
            lock (_nativeMapDropTrackerSync)
            {
                if (!_nativeMapDropTracker.TryGetValue(environment,
                        out var entry))
                    return 0;
                if (entry.SwitchGeneration !=
                        NativeMapDropTrackingGeneration.SwitchGeneration ||
                    entry.RawGeneration !=
                        environment.NativeMapDropItems.Generation)
                {
                    _nativeMapDropTracker.Remove(environment);
                    return 0;
                }
                return entry.Count;
            }
        }
    }
}
