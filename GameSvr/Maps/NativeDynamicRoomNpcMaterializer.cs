using SystemModule;

namespace GameSvr
{
    /// <summary>
    /// Builds the native dynamic-room controller and configured NPCs without
    /// publishing partially constructed actors. The returned journal owns the
    /// only commit/compensation path across ObjectManager, map cells, and the
    /// dynamic quest-NPC registry.
    /// </summary>
    public sealed class NativeDynamicRoomNpcMaterializer
        : INativeDynamicRoomNpcMaterializer
    {
        private sealed class PreparedNpc
        {
            public Merchant Npc { get; init; }
            public NativeDynamicRoomDynamicNpcScriptBinding Binding { get; init; }
            public ObjectManager.DeferredRegistration Registration { get; init; }
            public bool IsConfigured { get; init; }
            public bool ObjectPublished { get; set; }
            public bool MapPublished { get; set; }
            public bool RegistryPublished { get; set; }
            public bool RegistrationDisposed { get; set; }
        }

        private enum JournalState
        {
            Prepared,
            Committing,
            Committed,
            RollingBack,
            RolledBack,
            Destroying,
            Destroyed
        }

        private sealed class Journal
            : INativeDynamicRoomNpcMaterializationJournal
        {
            private readonly object _syncRoot = new();
            private readonly ObjectManager _objectManager;
            private readonly UserEngine _userEngine;
            private readonly PreparedNpc[] _entries;
            private readonly IReadOnlyList<NativeDynamicRoomMaterializedNpc>
                _publishedNpcs;
            private JournalState _state = JournalState.Prepared;
            private object _ownerCapability;
            private bool _hasUnresolvedPublication;

            public Journal(ObjectManager objectManager, UserEngine userEngine,
                NativeDynamicRoomDefinition definition,
                Envirnoment environment, int physicalInstanceId,
                PreparedNpc[] entries)
            {
                _objectManager = objectManager;
                _userEngine = userEngine;
                Definition = definition;
                Environment = environment;
                PhysicalInstanceId = physicalInstanceId;
                _entries = entries;
                _publishedNpcs = Array.AsReadOnly(entries.Select(entry =>
                        new NativeDynamicRoomMaterializedNpc(entry.Npc,
                            entry.Binding))
                    .ToArray());
            }

            public NativeDynamicRoomDefinition Definition { get; }
            public Envirnoment Environment { get; }
            public int PhysicalInstanceId { get; }
            public IReadOnlyList<NativeDynamicRoomMaterializedNpc> Npcs =>
                _publishedNpcs;

            public bool IsCommitted
            {
                get
                {
                    lock (_syncRoot)
                        return _state == JournalState.Committed;
                }
            }

            public bool IsDestroyed
            {
                get
                {
                    lock (_syncRoot)
                        return _state == JournalState.Destroyed;
                }
            }

            public bool HasUnresolvedPublication
            {
                get
                {
                    lock (_syncRoot)
                        return _hasUnresolvedPublication;
                }
            }

            public bool TryClaimOwnership(object ownerCapability)
            {
                if (ownerCapability == null) return false;
                lock (_syncRoot)
                {
                    if (_state != JournalState.Committed
                        || _hasUnresolvedPublication)
                        return false;
                    if (_ownerCapability == null)
                    {
                        _ownerCapability = ownerCapability;
                        return true;
                    }
                    return ReferenceEquals(_ownerCapability,
                        ownerCapability);
                }
            }

            public bool IsOwnershipClaimedBy(object ownerCapability)
            {
                if (ownerCapability == null) return false;
                lock (_syncRoot)
                    return ReferenceEquals(_ownerCapability,
                        ownerCapability);
            }

            public bool TryReleaseOwnershipClaim(object ownerCapability)
            {
                if (ownerCapability == null) return false;
                lock (_syncRoot)
                {
                    if (!ReferenceEquals(_ownerCapability, ownerCapability))
                        return false;
                    _ownerCapability = null;
                    return true;
                }
            }

            public bool TryCommit()
            {
                lock (_syncRoot)
                {
                    if (_state == JournalState.Committed)
                        return !_hasUnresolvedPublication;
                    if (_state != JournalState.Prepared
                        || _ownerCapability != null)
                        return false;
                    _state = JournalState.Committing;

                    var committed = false;
                    try
                    {
                        foreach (var entry in _entries)
                        {
                            if (!entry.Registration.TryCommit(entry.Npc))
                                return false;
                            entry.ObjectPublished = true;
                        }

                        foreach (var entry in _entries)
                        {
                            if (!entry.IsConfigured) continue;
                            if (!ReferenceEquals(Environment.AddToMap(
                                    entry.Npc.m_nCurrX,
                                    entry.Npc.m_nCurrY,
                                    CellType.OS_MOVINGOBJECT, entry.Npc),
                                    entry.Npc)
                                || !Environment
                                    .ContainsMovingObjectEverywhereExact(
                                        entry.Npc))
                                return false;
                            entry.MapPublished = true;

                            if (!_userEngine
                                    .TryAddDynamicRoomQuestNpcExact(entry.Npc))
                                return false;
                            entry.RegistryPublished = true;
                        }

                        foreach (var entry in _entries)
                            DisposeRegistration(entry);

                        _hasUnresolvedPublication = false;
                        _state = JournalState.Committed;
                        committed = true;
                        return true;
                    }
                    catch
                    {
                        return false;
                    }
                    finally
                    {
                        if (!committed)
                        {
                            var clean = CleanupExact(rollbackRegistrations: true);
                            _hasUnresolvedPublication = !clean;
                            _state = JournalState.RolledBack;
                        }
                    }
                }
            }

            public bool TryRollback()
            {
                lock (_syncRoot)
                {
                    if (_state == JournalState.RolledBack)
                        return !_hasUnresolvedPublication;
                    if (_state == JournalState.Destroyed) return true;
                    if (_ownerCapability != null
                        || _state is JournalState.Committing
                            or JournalState.RollingBack
                            or JournalState.Destroying)
                        return false;
                    if (_state is not (JournalState.Prepared
                        or JournalState.Committed))
                        return false;

                    _state = JournalState.RollingBack;
                    var clean = CleanupExact(rollbackRegistrations: true);
                    _hasUnresolvedPublication = !clean;
                    _state = JournalState.RolledBack;
                    return clean;
                }
            }

            public bool TryDestroyExact()
            {
                lock (_syncRoot)
                {
                    if (_state == JournalState.Destroyed)
                        return !_hasUnresolvedPublication;
                    if (_state != JournalState.Committed
                        || _ownerCapability == null)
                        return false;

                    _state = JournalState.Destroying;
                    var clean = CleanupExact(rollbackRegistrations: false);
                    _hasUnresolvedPublication = !clean;
                    _state = clean
                        ? JournalState.Destroyed
                        : JournalState.Committed;
                    return clean;
                }
            }

            private bool CleanupExact(bool rollbackRegistrations)
            {
                var clean = true;
                for (var index = _entries.Length - 1; index >= 0; index--)
                {
                    var entry = _entries[index];
                    entry.Npc.m_boGhost = true;
                    entry.Npc.m_dwGhostTick = HUtil32.GetTickCount();
                    try
                    {
                        entry.Npc.ClearScript();
                    }
                    catch
                    {
                        clean = false;
                    }

                    if (entry.RegistryPublished
                        || _userEngine.ContainsRegisteredNpcExact(entry.Npc))
                    {
                        try
                        {
                            _userEngine.TryRemoveRegisteredNpcEverywhereExact(
                                entry.Npc);
                            entry.RegistryPublished = _userEngine
                                .ContainsRegisteredNpcExact(entry.Npc);
                        }
                        catch
                        {
                            clean = false;
                        }
                    }
                    if (entry.RegistryPublished) clean = false;

                    if (entry.MapPublished
                        || Environment.ContainsMovingObjectEverywhereExact(
                            entry.Npc))
                    {
                        try
                        {
                            // Dynamic NPCs are not part of the environment's
                            // native monster/player counters.
                            entry.Npc.m_boDelFormMaped = true;
                            Environment.RemoveMovingObjectEverywhereExact(
                                entry.Npc, notifyDynamicRoomLifecycle: false);
                            entry.Npc.m_boAddToMaped = false;
                            entry.MapPublished = Environment
                                .ContainsMovingObjectEverywhereExact(entry.Npc);
                        }
                        catch
                        {
                            clean = false;
                        }
                    }
                    if (entry.MapPublished) clean = false;

                    try
                    {
                        var exactCurrent = ReferenceEquals(
                            _objectManager.Get(entry.Npc.ObjectId), entry.Npc);
                        if (exactCurrent)
                        {
                            var removed = rollbackRegistrations
                                && !entry.RegistrationDisposed
                                ? entry.Registration.TryRollback(entry.Npc)
                                : _objectManager.Remove(entry.Npc.ObjectId,
                                    entry.Npc);
                            if (!removed && ReferenceEquals(
                                    _objectManager.Get(entry.Npc.ObjectId),
                                    entry.Npc))
                                clean = false;
                        }
                        entry.ObjectPublished = ReferenceEquals(
                            _objectManager.Get(entry.Npc.ObjectId), entry.Npc);
                    }
                    catch
                    {
                        clean = false;
                    }
                    if (entry.ObjectPublished) clean = false;

                    if (rollbackRegistrations)
                    {
                        try
                        {
                            if (!entry.RegistrationDisposed
                                && !entry.ObjectPublished)
                                entry.Registration.TryRollback(entry.Npc);
                            DisposeRegistration(entry);
                        }
                        catch
                        {
                            clean = false;
                        }
                    }
                }
                return clean;
            }

            private static void DisposeRegistration(PreparedNpc entry)
            {
                if (entry.RegistrationDisposed) return;
                entry.Registration.Dispose();
                entry.RegistrationDisposed = true;
            }
        }

        private readonly ObjectManager _objectManager;
        private readonly UserEngine _userEngine;

        public NativeDynamicRoomNpcMaterializer(ObjectManager objectManager,
            UserEngine userEngine)
        {
            ArgumentNullException.ThrowIfNull(objectManager);
            ArgumentNullException.ThrowIfNull(userEngine);
            _objectManager = objectManager;
            _userEngine = userEngine;
        }

        public bool TryPrepare(NativeDynamicRoomDefinition definition,
            Envirnoment environment, int physicalInstanceId,
            IReadOnlyList<NativeDynamicRoomDynamicNpcScriptBinding> bindings,
            out INativeDynamicRoomNpcMaterializationJournal journal,
            out string diagnostic)
        {
            journal = null;
            diagnostic = null;
            if (!TryValidatePlan(definition, environment, physicalInstanceId,
                    bindings, out var planned, out diagnostic))
                return false;

            var entries = new List<PreparedNpc>(planned.Length);
            try
            {
                foreach (var binding in planned)
                {
                    var registration = _objectManager.BeginDeferredRegistration();
                    Merchant npc;
                    try
                    {
                        npc = new Merchant();
                    }
                    catch
                    {
                        registration.Dispose();
                        throw;
                    }

                    ConfigureNpc(npc, definition, environment, binding);
                    entries.Add(new PreparedNpc
                    {
                        Npc = npc,
                        Binding = binding,
                        Registration = registration,
                        IsConfigured = binding.Role ==
                            NativeDynamicRoomDynamicNpcScriptRole
                                .ConfiguredVisible
                    });
                }

                journal = new Journal(_objectManager, _userEngine, definition,
                    environment, physicalInstanceId, entries.ToArray());
                return true;
            }
            catch (Exception ex)
            {
                for (var index = entries.Count - 1; index >= 0; index--)
                {
                    try
                    {
                        entries[index].Registration.TryRollback(
                            entries[index].Npc);
                        entries[index].Registration.Dispose();
                    }
                    catch
                    {
                        // The diagnostic reports the construction failure; a
                        // pending deferred registration is never published.
                    }
                }
                diagnostic = $"dynamic room NPC preparation failed ({ex.GetType().Name})";
                return false;
            }
        }

        private static bool TryValidatePlan(
            NativeDynamicRoomDefinition definition, Envirnoment environment,
            int physicalInstanceId,
            IReadOnlyList<NativeDynamicRoomDynamicNpcScriptBinding> bindings,
            out NativeDynamicRoomDynamicNpcScriptBinding[] planned,
            out string diagnostic)
        {
            planned = null;
            diagnostic = null;
            if (definition == null || environment == null || bindings == null
                || physicalInstanceId < 0)
            {
                diagnostic = "definition, environment, physical instance, or bindings are invalid";
                return false;
            }
            if (!environment.IsDynamicRoom
                || environment.DynamicRoomState != 0
                || environment.DynamicRoomPlayerCount != 0
                || environment.DynamicRoomPhysicalInstanceId
                    != physicalInstanceId
                || !string.Equals(environment.DynamicRoomName,
                    definition.RoomName, StringComparison.Ordinal))
            {
                diagnostic = "environment is not the exact dormant physical room";
                return false;
            }
            if (definition.ConfiguredNpcs == null)
            {
                diagnostic = "configured NPC definitions are null";
                return false;
            }

            try
            {
                planned = bindings.ToArray();
            }
            catch (Exception ex)
            {
                diagnostic = $"bindings could not be snapshotted ({ex.GetType().Name})";
                return false;
            }
            if (planned.Length != definition.ConfiguredNpcs.Count + 1)
            {
                diagnostic = "planned binding count does not match the room definition";
                return false;
            }

            var expectedConfigured = new HashSet<
                NativeDynamicRoomConfiguredNpcDefinition>(
                definition.ConfiguredNpcs,
                ReferenceEqualityComparer.Instance);
            if (expectedConfigured.Count != definition.ConfiguredNpcs.Count)
            {
                diagnostic = "configured NPC definitions contain null or duplicate references";
                return false;
            }
            var seenConfigured = new HashSet<
                NativeDynamicRoomConfiguredNpcDefinition>(
                ReferenceEqualityComparer.Instance);
            var seenBindings = new HashSet<
                NativeDynamicRoomDynamicNpcScriptBinding>(
                ReferenceEqualityComparer.Instance);
            var controllers = 0;
            foreach (var binding in planned)
            {
                if (binding == null || !seenBindings.Add(binding)
                    || !ReferenceEquals(binding.Definition, definition)
                    || string.IsNullOrWhiteSpace(binding.ScriptFileName)
                    || string.IsNullOrWhiteSpace(binding.ScriptPath)
                    || !Path.IsPathFullyQualified(binding.ScriptPath))
                {
                    diagnostic = "planned binding identity or script path is invalid";
                    return false;
                }

                if (binding.Role ==
                    NativeDynamicRoomDynamicNpcScriptRole.HiddenController)
                {
                    if (binding.ConfiguredNpc != null || ++controllers != 1)
                    {
                        diagnostic = "hidden controller binding is not unique";
                        return false;
                    }
                    continue;
                }
                if (binding.Role !=
                        NativeDynamicRoomDynamicNpcScriptRole.ConfiguredVisible
                    || binding.ConfiguredNpc == null
                    || !expectedConfigured.Contains(binding.ConfiguredNpc)
                    || !seenConfigured.Add(binding.ConfiguredNpc)
                    || !CoordinatesFit(binding.ConfiguredNpc))
                {
                    diagnostic = "configured NPC binding or numeric field is invalid";
                    return false;
                }
            }
            if (controllers != 1
                || seenConfigured.Count != expectedConfigured.Count)
            {
                diagnostic = "planned bindings do not cover the exact room NPC set";
                return false;
            }
            return true;
        }

        private static bool CoordinatesFit(
            NativeDynamicRoomConfiguredNpcDefinition configured)
        {
            return configured.X is >= 0 and <= short.MaxValue
                   && configured.Y is >= 0 and <= short.MaxValue
                   && configured.Face is >= 0 and <= short.MaxValue
                   && configured.Body is >= 0 and <= ushort.MaxValue;
        }

        private static void ConfigureNpc(Merchant npc,
            NativeDynamicRoomDefinition definition, Envirnoment environment,
            NativeDynamicRoomDynamicNpcScriptBinding binding)
        {
            npc.m_PEnvir = environment;
            npc.m_sMapName = environment.sMapName;
            npc.m_sFilePath = Path.GetDirectoryName(binding.ScriptPath)
                              ?? string.Empty;
            npc.m_boGhost = false;
            npc.m_boDeath = false;
            npc.m_boAddtoMapSuccess = false;

            if (binding.Role ==
                NativeDynamicRoomDynamicNpcScriptRole.HiddenController)
            {
                npc.m_sScript = Path.GetFileNameWithoutExtension(
                    binding.ScriptFileName);
                npc.m_sCharName = definition.RoomName;
                npc.m_nCurrX = 0;
                npc.m_nCurrY = 0;
                npc.m_nFlag = 0;
                npc.m_wAppr = 0;
                npc.m_boIsHide = true;
                npc.m_boIsQuest = false;
                return;
            }

            var configured = binding.ConfiguredNpc;
            npc.m_sScript = configured.ScriptName;
            npc.m_sCharName = configured.NpcName;
            npc.m_nCurrX = (short)configured.X;
            npc.m_nCurrY = (short)configured.Y;
            npc.m_nFlag = (short)configured.Face;
            npc.m_btDirection = (byte)(configured.Face & 7);
            npc.m_wAppr = (ushort)configured.Body;
            npc.m_boIsHide = false;
            npc.m_boIsQuest = true;
        }
    }
}
