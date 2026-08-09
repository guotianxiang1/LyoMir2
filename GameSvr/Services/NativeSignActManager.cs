namespace GameSvr.Services
{
    public sealed class NativeSignActRow
    {
        public NativeSignActRow(int index, string characterName,
            int signCount, int prizeType)
        {
            Index = index;
            CharacterName = characterName ?? string.Empty;
            SignCount = signCount;
            PrizeType = prizeType;
        }

        public int Index { get; }
        public string CharacterName { get; }
        public int SignCount { get; }
        public int PrizeType { get; }
    }

    public sealed class NativeSignActEverydayRow
    {
        public NativeSignActEverydayRow(int index, string characterName,
            int prizeTag)
        {
            Index = index;
            CharacterName = characterName ?? string.Empty;
            PrizeTag = prizeTag;
        }

        public int Index { get; }
        public string CharacterName { get; }
        public int PrizeTag { get; }
    }

    public sealed class NativeSignActWinners
    {
        internal NativeSignActWinners(string primary, string lucky1,
            string lucky2)
        {
            Primary = primary ?? string.Empty;
            Lucky1 = lucky1 ?? string.Empty;
            Lucky2 = lucky2 ?? string.Empty;
        }

        public string Primary { get; }
        public string Lucky1 { get; }
        public string Lucky2 { get; }
    }

    public interface INativeSignActStore
    {
        bool EnsureSchemas(out string error);
        bool TryGetSignCountRow(string characterName, out NativeSignActRow row);
        bool TryGetSignPrizeRow(string characterName, out NativeSignActRow row);
        bool InsertSignAct(string characterName);
        bool UpdateSignCount(int index, int signCount);
        bool ResetSignAct();
        int QueryExistingSignActPrizeCount();
        IReadOnlyList<NativeSignActRow> SelectSignActDrawCandidates(
            out int queryCount);
        bool UpdateSignActPrizeType(int index, int prizeType);
        IReadOnlyList<NativeSignActRow> SelectSignActWinners();

        bool ReplaceEverydaySignIn(string characterName);
        IReadOnlyList<int> SelectYesterdayPrizeTags(string characterName);
        IReadOnlyList<NativeSignActEverydayRow>
            SelectYesterdayEverydayWinners(out int queryCount);
        IReadOnlyList<NativeSignActEverydayRow>
            SelectYesterdayEverydayDrawCandidates();
        bool UpdateEverydayPrizeTag(int index, int prizeTag);
    }

    public enum NativeSignActDrawResult
    {
        AlreadyDrawn,
        NoWinners,
        Success,
        UpdateFailed
    }

    public enum NativeSignActDailyProcessResult
    {
        NoDateChange,
        WaitingForMinuteSix,
        Processed
    }

    /// <summary>
    /// Synchronous native SignAct state. The GameState execution domain
    /// serializes runtime calls; this type does not own a thread, timer,
    /// switch, transaction, reward, or callback.
    /// </summary>
    public sealed class NativeSignActManager
    {
        public const string EverydayWinnerSeparator = ", ";

        private readonly INativeSignActStore _store;
        private DateOnly? _lastEverydayLocalDate;
        private string _everydayPrimary = string.Empty;
        private string _everydaySecondary = string.Empty;

        public NativeSignActManager(INativeSignActStore store)
        {
            _store = store ?? throw new ArgumentNullException(nameof(store));
        }

        public DateOnly? LastEverydayLocalDate => _lastEverydayLocalDate;

        public bool SignIn(bool activityEnabled, string characterName)
        {
            if (!activityEnabled) return false;
            characterName ??= string.Empty;
            if (!_store.TryGetSignCountRow(characterName, out var row))
                return _store.InsertSignAct(characterName);
            return _store.UpdateSignCount(row.Index,
                unchecked(row.SignCount + 1));
        }

        public bool OpenActivity() => _store.ResetSignAct();

        public NativeSignActDrawResult CloseActivity()
        {
            // Delphi tests ExecuteQuery's signed row count as a Boolean here;
            // its -1 SQL-error result therefore follows the non-zero branch.
            if (_store.QueryExistingSignActPrizeCount() != 0)
                return NativeSignActDrawResult.AlreadyDrawn;

            var candidates = _store.SelectSignActDrawCandidates(
                                 out var candidateQueryCount)
                             ?? Array.Empty<NativeSignActRow>();
            if (candidateQueryCount == 0)
                return NativeSignActDrawResult.NoWinners;
            var count = Math.Min(3, candidates.Count);

            for (var i = 0; i < count; i++)
            {
                var prizeType = i == 0 ? 1 : 2;
                if (!_store.UpdateSignActPrizeType(
                        candidates[i].Index, prizeType))
                    return NativeSignActDrawResult.UpdateFailed;
            }
            return NativeSignActDrawResult.Success;
        }

        public int Claim(string characterName)
        {
            if (!_store.TryGetSignPrizeRow(characterName ?? string.Empty,
                    out var row))
                return 0;

            var prizeType = row.PrizeType;
            if (prizeType is not (1 or 2)) return prizeType;
            return _store.UpdateSignActPrizeType(row.Index, prizeType + 2)
                ? prizeType
                : -1;
        }

        public NativeSignActWinners GetWinners()
        {
            var primary = string.Empty;
            var lucky1 = string.Empty;
            var lucky2 = string.Empty;
            var rows = _store.SelectSignActWinners()
                       ?? Array.Empty<NativeSignActRow>();
            foreach (var row in rows)
            {
                if (row.PrizeType is 1 or 3)
                {
                    primary = row.CharacterName;
                }
                else if (string.IsNullOrEmpty(lucky1))
                {
                    lucky1 = row.CharacterName;
                }
                else if (string.IsNullOrEmpty(lucky2))
                {
                    lucky2 = row.CharacterName;
                }
                else
                {
                    break;
                }
            }
            return new NativeSignActWinners(primary, lucky1, lucky2);
        }

        public void SignInEveryday(string characterName)
        {
            _store.ReplaceEverydaySignIn(characterName ?? string.Empty);
        }

        public int GetYesterdayPrizeTag(string characterName)
        {
            var tags = _store.SelectYesterdayPrizeTags(
                           characterName ?? string.Empty)
                       ?? Array.Empty<int>();
            return tags.Count == 1 ? tags[0] : 0;
        }

        public string GetEverydayWinners(int prizeLevel) => prizeLevel == 1
            ? _everydayPrimary
            : _everydaySecondary;

        public NativeSignActDailyProcessResult ProcessEveryday(
            DateTime localNow)
        {
            var currentDate = DateOnly.FromDateTime(localNow);
            if (_lastEverydayLocalDate == currentDate)
                return NativeSignActDailyProcessResult.NoDateChange;
            if (_lastEverydayLocalDate.HasValue && localNow.Minute <= 5)
                return NativeSignActDailyProcessResult.WaitingForMinuteSix;

            _lastEverydayLocalDate = currentDate;
            LoadOrDrawEverydayWinners();
            return NativeSignActDailyProcessResult.Processed;
        }

        internal void LoadOrDrawEverydayWinners()
        {
            _everydayPrimary = string.Empty;
            _everydaySecondary = string.Empty;

            var existing = _store.SelectYesterdayEverydayWinners(
                               out var existingQueryCount)
                           ?? Array.Empty<NativeSignActEverydayRow>();
            // This original branch also treats ExecuteQuery=-1 as non-zero.
            // On failure it keeps the winner strings empty and must not redraw.
            if (existingQueryCount != 0)
            {
                foreach (var row in existing)
                    AddEverydayWinner(row.CharacterName, row.PrizeTag);
                return;
            }

            var candidates = _store.SelectYesterdayEverydayDrawCandidates()
                             ?? Array.Empty<NativeSignActEverydayRow>();
            var count = Math.Min(4, candidates.Count);
            for (var i = 0; i < count; i++)
            {
                var prizeTag = i == 0 ? 1 : 2;
                var row = candidates[i];
                AddEverydayWinner(row.CharacterName, prizeTag);
                _store.UpdateEverydayPrizeTag(row.Index, prizeTag);
            }
        }

        private void AddEverydayWinner(string characterName, int prizeTag)
        {
            characterName ??= string.Empty;
            if (prizeTag == 1)
            {
                _everydayPrimary = characterName;
                return;
            }

            _everydaySecondary = string.IsNullOrEmpty(_everydaySecondary)
                ? characterName
                : _everydaySecondary + EverydayWinnerSeparator + characterName;
        }
    }
}
