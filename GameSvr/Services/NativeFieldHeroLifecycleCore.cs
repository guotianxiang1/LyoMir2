using System.Collections.ObjectModel;

namespace GameSvr.Services
{
    public interface INativeFieldHeroLifecycleEntry
    {
        bool IsGhost { get; }
        int RunTick { get; set; }
        int RunInterval { get; }
        int GhostTick { get; }

        void InvokeNativeVmt7C();
        void Run();
        void Free();
    }

    /// <summary>
    /// Independent model of sub_605790 and sub_605814. It remains detached
    /// from UserEngine while the FieldHero actor Run chain is fail-closed.
    /// </summary>
    public sealed class NativeFieldHeroLifecycleCore
    {
        public const uint PendingFreeDelay = 300000;
        public const string PendingReaperExceptionMessage =
            "[Exception]:TMonFortress.RefreshDeleteActs";

        private readonly List<INativeFieldHeroLifecycleEntry> _active = new();
        private readonly List<INativeFieldHeroLifecycleEntry> _pending = new();
        private readonly Action<string> _exceptionLogger;
        private readonly ReadOnlyCollection<INativeFieldHeroLifecycleEntry>
            _activeView;
        private readonly ReadOnlyCollection<INativeFieldHeroLifecycleEntry>
            _pendingView;

        public NativeFieldHeroLifecycleCore(
            Action<string> exceptionLogger = null)
        {
            _exceptionLogger = exceptionLogger ?? (_ => { });
            _activeView = _active.AsReadOnly();
            _pendingView = _pending.AsReadOnly();
        }

        public IReadOnlyList<INativeFieldHeroLifecycleEntry> Active =>
            _activeView;

        public IReadOnlyList<INativeFieldHeroLifecycleEntry> Pending =>
            _pendingView;

        public void AddActive(INativeFieldHeroLifecycleEntry entry)
        {
            if (entry == null) throw new ArgumentNullException(nameof(entry));
            _active.Add(entry);
        }

        public void ProcessActive(Func<int> getTickCount)
        {
            if (getTickCount == null)
                throw new ArgumentNullException(nameof(getTickCount));

            var now = getTickCount();
            var index = 0;
            while (index < _active.Count)
            {
                var entry = _active[index];
                if (entry.IsGhost)
                {
                    entry.InvokeNativeVmt7C();
                    _pending.Add(entry);
                    _active.RemoveAt(index);
                    continue;
                }

                var elapsed = unchecked((uint)(now - entry.RunTick));
                if (elapsed > unchecked((uint)entry.RunInterval))
                {
                    entry.RunTick = now;
                    entry.Run();
                }
                index++;
            }
        }

        public void ReapPending(int now)
        {
            try
            {
                for (var index = _pending.Count - 1; index >= 0; index--)
                {
                    var entry = _pending[index];
                    var elapsed = unchecked((uint)(now - entry.GhostTick));
                    if (elapsed < PendingFreeDelay) continue;

                    _pending.RemoveAt(index);
                    entry.Free();
                }
            }
            catch (Exception)
            {
                _exceptionLogger(PendingReaperExceptionMessage);
            }
        }
    }
}
