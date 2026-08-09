namespace GameSvr.Services
{
    public sealed class NativeGildConcernActor
    {
        public NativeGildConcernActor(long id, long gildId)
        {
            Id = id;
            GildId = gildId;
        }

        public long Id { get; }
        public long GildId { get; }
    }

    public sealed class NativeGildConcernDeleteCommand
    {
        public const string LegacySqlTemplate =
            "delete from gamedata.gildconcern where GildID = %d and " +
            "DstGildID = %d;";

        public NativeGildConcernDeleteCommand(long gildId,
            long destinationGildId)
        {
            GildId = gildId;
            DestinationGildId = destinationGildId;
        }

        public long GildId { get; }
        public long DestinationGildId { get; }
    }

    public interface INativeGildCancelConcernHost
    {
        bool TryGetActor(long actorId, out NativeGildConcernActor actor);
        bool GildExists(long gildId);
        bool RemoveConcern(long gildId, long destinationGildId);
    }

    public interface INativeGildConcernDeleteQueue
    {
        void Enqueue(NativeGildConcernDeleteCommand command);
    }

    public interface INativeGildConcernDeleteExecutor
    {
        bool TryExecute(NativeGildConcernDeleteCommand command,
            out string error);
        void ReportSqlFailure(NativeGildConcernDeleteCommand command,
            string error);
    }

    /// <summary>
    /// The legacy worker removes each command before execution. SQL failure is
    /// logged and cannot roll back the already-published in-memory removal.
    /// </summary>
    public sealed class NativeGildConcernLegacyDeleteQueue :
        INativeGildConcernDeleteQueue
    {
        private readonly object _sync = new();
        private readonly object _processSync = new();
        private readonly Queue<NativeGildConcernDeleteCommand> _pending =
            new();
        private readonly INativeGildConcernDeleteExecutor _executor;

        public NativeGildConcernLegacyDeleteQueue(
            INativeGildConcernDeleteExecutor executor)
        {
            _executor = executor ??
                        throw new ArgumentNullException(nameof(executor));
        }

        public int PendingCount
        {
            get
            {
                lock (_sync) return _pending.Count;
            }
        }

        public void Enqueue(NativeGildConcernDeleteCommand command)
        {
            if (command == null) throw new ArgumentNullException(nameof(command));
            lock (_sync) _pending.Enqueue(command);
        }

        public bool ProcessNext()
        {
            lock (_processSync) return ProcessNextSerialized();
        }

        private bool ProcessNextSerialized()
        {
            NativeGildConcernDeleteCommand command;
            lock (_sync)
            {
                if (_pending.Count == 0) return false;
                command = _pending.Dequeue();
            }

            string error;
            try
            {
                if (_executor.TryExecute(command, out error)) return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
            }

            try
            {
                _executor.ReportSqlFailure(command,
                    string.IsNullOrEmpty(error) ? "SQL failed" : error);
            }
            catch
            {
                // The original shared worker continues after logger failure.
            }
            return true;
        }
    }

    /// <summary>
    /// Exact dormant transaction for CM_GILD_CANCLE_CONCERN (4578).
    /// Strategy selection happens before the manager call in the native M2.
    /// </summary>
    public static class NativeGildCancelConcernTransaction
    {
        public const int PermissionDenied = 555;

        public static int Execute(NativeSelfSocialRole role,
            INativeGildCancelConcernHost host,
            INativeGildConcernDeleteQueue writes, long actorId,
            long destinationGildId)
        {
            if (host == null) throw new ArgumentNullException(nameof(host));
            if (writes == null) throw new ArgumentNullException(nameof(writes));
            if (role != NativeSelfSocialRole.GildOwner)
                return PermissionDenied;
            if (!host.TryGetActor(actorId, out var actor)) return 5;
            if (actor == null || actor.GildId == 0) return 12;
            if (!host.GildExists(destinationGildId)) return 25;
            if (!host.RemoveConcern(actor.GildId, destinationGildId))
                return 1000;

            writes.Enqueue(new NativeGildConcernDeleteCommand(actor.GildId,
                destinationGildId));
            return 0;
        }
    }
}
