namespace GameSvr
{
    public enum NativeDynamicRoomPasScriptRouteState
    {
        NotDynamic,
        ExactCurrent,
        DynamicUnavailableOrStale
    }

    public sealed class NativeDynamicRoomPasScriptBindingHandle
    {
        private readonly NativeDynamicRoomPasScriptRouteTable _owner;

        internal NativeDynamicRoomPasScriptBindingHandle(
            NativeDynamicRoomPasScriptRouteTable owner,
            NormNpc npc,
            NativeDynamicRoomActivationLease activationLease,
            NativeDynamicRoomDynamicNpcScriptBinding plannedBinding,
            string scriptPath,
            bool plannedScriptPresent,
            bool hasCanonicalScriptPath,
            bool definitionMatchesActivation,
            bool boundToLeaseEnvironment,
            bool boundToCurrentActivation)
        {
            _owner = owner;
            Npc = npc;
            NpcObjectId = npc.ObjectId;
            ActivationLease = activationLease;
            PlannedBinding = plannedBinding;
            ScriptPath = scriptPath;
            PlannedScriptPresent = plannedScriptPresent;
            HasCanonicalScriptPath = hasCanonicalScriptPath;
            DefinitionMatchesActivation = definitionMatchesActivation;
            BoundToLeaseEnvironment = boundToLeaseEnvironment;
            BoundToCurrentActivation = boundToCurrentActivation;
        }

        public NormNpc Npc { get; }
        public int NpcObjectId { get; }
        public NativeDynamicRoomActivationLease ActivationLease { get; }
        public NativeDynamicRoomDynamicNpcScriptBinding PlannedBinding { get; }
        public int ActivationGeneration => ActivationLease.Index;
        public string ScriptPath { get; }
        public bool PlannedScriptPresent { get; }
        public bool HasCanonicalScriptPath { get; }
        public bool DefinitionMatchesActivation { get; }
        public bool BoundToLeaseEnvironment { get; }
        public bool BoundToCurrentActivation { get; }

        internal bool IsOwnedBy(NativeDynamicRoomPasScriptRouteTable owner)
        {
            return ReferenceEquals(_owner, owner);
        }

        internal bool Released { get; set; }
    }

    /// <summary>
    /// Describes exact dynamic-room PAS routes connected to the PAS host through
    /// the runtime gate. Validation is a snapshot; the runtime gate serializes
    /// PAS execution with lifecycle changes after a handle is returned.
    /// </summary>
    public sealed class NativeDynamicRoomPasScriptRouteTable
    {
        private readonly object _syncRoot = new();
        private readonly string _scriptRoot;
        private readonly string _scriptRootPrefix;
        private readonly Dictionary<NormNpc, NativeDynamicRoomPasScriptBindingHandle>
            _routesByNpc = new(ReferenceEqualityComparer.Instance);
        private readonly Dictionary<int, NativeDynamicRoomPasScriptBindingHandle>
            _routesByObjectId = new();
        private Action<NativeDynamicRoomPasScriptBindingHandle, bool>
            _registrationCheckpointForTests = null;

        public NativeDynamicRoomPasScriptRouteTable(string dynRoomScriptsDirectory)
        {
            if (string.IsNullOrWhiteSpace(dynRoomScriptsDirectory))
                throw new ArgumentException("DynRoomScripts directory is required.",
                    nameof(dynRoomScriptsDirectory));

            _scriptRoot = Path.GetFullPath(dynRoomScriptsDirectory)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            _scriptRootPrefix = _scriptRoot + Path.DirectorySeparatorChar;
        }

        public string ScriptRoot => _scriptRoot;

        public NativeDynamicRoomPasScriptBindingHandle Register(
            NormNpc npc,
            NativeDynamicRoomActivationLease activationLease,
            NativeDynamicRoomDynamicNpcScriptBinding plannedBinding)
        {
            ArgumentNullException.ThrowIfNull(npc);
            ArgumentNullException.ThrowIfNull(activationLease);
            var plannedScriptPresent = plannedBinding?.HasScript == true;
            var hasCanonicalPath = TryNormalizeScriptPath(
                plannedBinding?.ScriptPath, out var scriptPath);
            var plannedDefinition = plannedBinding?.Definition;
            var definitionMatchesActivation = plannedDefinition != null
                && ReferenceEquals(plannedDefinition,
                    activationLease.Definition);
            var boundToLeaseEnvironment = ReferenceEquals(npc.m_PEnvir,
                activationLease.Environment);
            var boundToCurrentActivation = activationLease.IsCurrentActive();
            var handle = new NativeDynamicRoomPasScriptBindingHandle(
                this, npc, activationLease, plannedBinding, scriptPath,
                plannedScriptPresent, hasCanonicalPath,
                definitionMatchesActivation, boundToLeaseEnvironment,
                boundToCurrentActivation);
            NativeDynamicRoomPasScriptBindingHandle previousByNpc = null;
            NativeDynamicRoomPasScriptBindingHandle previousByObjectId = null;

            _registrationCheckpointForTests?.Invoke(handle, false);
            lock (_syncRoot)
            {
                if (!boundToCurrentActivation
                    && (_routesByNpc.ContainsKey(npc)
                        || _routesByObjectId.ContainsKey(npc.ObjectId)))
                {
                    handle.Released = true;
                    return handle;
                }

                _routesByNpc.TryGetValue(npc, out previousByNpc);
                _routesByObjectId.TryGetValue(npc.ObjectId,
                    out previousByObjectId);

                _routesByNpc[npc] = handle;
                _routesByObjectId[npc.ObjectId] = handle;
            }
            _registrationCheckpointForTests?.Invoke(handle, true);

            if (!boundToCurrentActivation) return handle;

            var remainsCurrent = activationLease.IsCurrentActive();
            lock (_syncRoot)
            {
                if (!IsMappedHandleNoLock(npc, npc.ObjectId, handle))
                {
                    // A newer attempt owns the maps. It may still roll back to
                    // this handle, so only that current publisher may release it.
                    return handle;
                }

                if (!remainsCurrent)
                {
                    // Preserve an existing current route when this activation
                    // became stale between its lock-free checks. With no prior
                    // route, keep this released handle as a fail-closed tombstone.
                    handle.Released = true;
                    if (previousByNpc != null)
                        _routesByNpc[npc] = previousByNpc;
                    if (previousByObjectId != null)
                        _routesByObjectId[npc.ObjectId] = previousByObjectId;
                    return handle;
                }

                if (previousByNpc != null
                    && !ReferenceEquals(previousByNpc, handle))
                    previousByNpc.Released = true;
                if (previousByObjectId != null
                    && !ReferenceEquals(previousByObjectId, handle))
                    previousByObjectId.Released = true;
                return handle;
            }
        }

        public bool Unregister(NativeDynamicRoomPasScriptBindingHandle handle)
        {
            if (handle == null || !handle.IsOwnedBy(this)) return false;

            lock (_syncRoot)
            {
                if (!_routesByNpc.TryGetValue(handle.Npc, out var current)
                    || !ReferenceEquals(current, handle)
                    || handle.Released)
                    return false;

                // Keep the released entry as a tombstone so delayed dynamic input
                // cannot fall through to the legacy basename resolver.
                handle.Released = true;
                return true;
            }
        }

        public NativeDynamicRoomPasScriptRouteState Resolve(
            NormNpc npc,
            int npcObjectId,
            NativeDynamicRoomActivationLease activationLease,
            out string exactScriptPath)
        {
            return ResolveCore(npc, npcObjectId, activationLease,
                requireExpectedLease: true, out _, out exactScriptPath);
        }

        public NativeDynamicRoomPasScriptRouteState ResolveCurrent(
            NormNpc npc,
            out NativeDynamicRoomPasScriptBindingHandle handle,
            out string exactScriptPath)
        {
            return ResolveCore(npc, npc?.ObjectId ?? 0, null,
                requireExpectedLease: false, out handle,
                out exactScriptPath);
        }

        public bool ValidateExpected(
            NormNpc npc,
            NativeDynamicRoomPasScriptBindingHandle expectedHandle,
            out string exactScriptPath)
        {
            exactScriptPath = null;
            if (npc == null || expectedHandle == null
                || !expectedHandle.IsOwnedBy(this))
                return false;

            var npcObjectId = npc.ObjectId;
            lock (_syncRoot)
            {
                if (!IsUsableMappedHandleNoLock(npc, npcObjectId,
                        expectedHandle))
                    return false;
            }

            if (!ValidateOutsideRouteLock(npc, expectedHandle)) return false;

            lock (_syncRoot)
            {
                if (!IsUsableMappedHandleNoLock(npc, npcObjectId,
                        expectedHandle))
                    return false;
            }

            exactScriptPath = expectedHandle.ScriptPath;
            return true;
        }

        private NativeDynamicRoomPasScriptRouteState ResolveCore(
            NormNpc npc,
            int npcObjectId,
            NativeDynamicRoomActivationLease expectedLease,
            bool requireExpectedLease,
            out NativeDynamicRoomPasScriptBindingHandle handle,
            out string exactScriptPath)
        {
            handle = null;
            exactScriptPath = null;
            NativeDynamicRoomPasScriptBindingHandle route;
            bool hasDynamicIdentity;

            lock (_syncRoot)
            {
                if (npc == null || !_routesByNpc.TryGetValue(npc, out route))
                {
                    hasDynamicIdentity = expectedLease != null
                                         || _routesByObjectId.ContainsKey(
                                             npcObjectId);
                    route = null;
                }
                else
                {
                    hasDynamicIdentity = true;
                    if (!IsUsableMappedHandleNoLock(npc, npcObjectId, route)
                        || requireExpectedLease
                        && !ReferenceEquals(route.ActivationLease,
                            expectedLease))
                        route = null;
                }
            }

            if (route == null)
                return hasDynamicIdentity
                    ? NativeDynamicRoomPasScriptRouteState.DynamicUnavailableOrStale
                    : NativeDynamicRoomPasScriptRouteState.NotDynamic;

            if (!ValidateOutsideRouteLock(npc, route))
                return NativeDynamicRoomPasScriptRouteState.DynamicUnavailableOrStale;

            lock (_syncRoot)
            {
                if (!IsUsableMappedHandleNoLock(npc, npcObjectId, route)
                    || requireExpectedLease
                    && !ReferenceEquals(route.ActivationLease,
                        expectedLease))
                    return NativeDynamicRoomPasScriptRouteState.DynamicUnavailableOrStale;
            }

            handle = route;
            exactScriptPath = route.ScriptPath;
            return NativeDynamicRoomPasScriptRouteState.ExactCurrent;
        }

        private bool IsUsableMappedHandleNoLock(NormNpc npc, int npcObjectId,
            NativeDynamicRoomPasScriptBindingHandle handle)
        {
            return IsMappedHandleNoLock(npc, npcObjectId, handle)
                   && !handle.Released
                   && handle.BoundToCurrentActivation
                   && handle.DefinitionMatchesActivation
                   && handle.BoundToLeaseEnvironment
                   && handle.PlannedScriptPresent
                   && handle.HasCanonicalScriptPath;
        }

        private bool IsMappedHandleNoLock(NormNpc npc, int npcObjectId,
            NativeDynamicRoomPasScriptBindingHandle handle)
        {
            return handle != null
                   && ReferenceEquals(handle.Npc, npc)
                   && handle.NpcObjectId == npcObjectId
                   && npc.ObjectId == npcObjectId
                   && _routesByNpc.TryGetValue(npc, out var currentByNpc)
                   && ReferenceEquals(currentByNpc, handle)
                   && _routesByObjectId.TryGetValue(npcObjectId,
                       out var currentByObjectId)
                   && ReferenceEquals(currentByObjectId, handle);
        }

        private static bool ValidateOutsideRouteLock(NormNpc npc,
            NativeDynamicRoomPasScriptBindingHandle handle)
        {
            var activationLease = handle.ActivationLease;
            return activationLease != null
                   && activationLease.IsCurrentActive()
                   && !npc.m_boGhost
                   && ReferenceEquals(npc.m_PEnvir,
                       activationLease.Environment)
                   && M2Share.ObjectManager != null
                   && ReferenceEquals(M2Share.ObjectManager.Get(
                       handle.NpcObjectId), npc)
                   && File.Exists(handle.ScriptPath);
        }

        private bool TryNormalizeScriptPath(string scriptPath,
            out string normalizedPath)
        {
            normalizedPath = null;
            if (string.IsNullOrWhiteSpace(scriptPath)
                || !Path.IsPathFullyQualified(scriptPath))
                return false;

            try
            {
                var fullPath = Path.GetFullPath(scriptPath);
                if (!fullPath.StartsWith(_scriptRootPrefix,
                        StringComparison.OrdinalIgnoreCase)
                    || !string.Equals(Path.GetExtension(fullPath), ".pas",
                        StringComparison.OrdinalIgnoreCase))
                    return false;

                normalizedPath = fullPath;
                return true;
            }
            catch (Exception ex) when (ex is ArgumentException
                                       or NotSupportedException
                                       or PathTooLongException)
            {
                return false;
            }
        }
    }
}
