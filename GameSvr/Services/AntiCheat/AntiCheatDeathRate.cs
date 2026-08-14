namespace GameSvr.Services.AntiCheat
{
    // ================================================================================================
    // Anti-cheat death rate monitoring system — Model-View-Intent implementation
    //
    // This dormant model describes the contract for monitoring and evaluating player death patterns
    // to detect suspicious behavior (exploit abuse, botting, automated farming). The system tracks
    // death frequency over sliding time windows and applies graduated responses based on severity.
    //
    // Design principles:
    //   - Time-windowed observation (short/medium/long windows for pattern detection)
    //   - Graduated severity levels (info/warning/critical)
    //   - Context-aware evaluation (PK deaths vs PvE, map type, player level)
    //   - Audit trail for all flagged events
    //
    // NOT wired into live anti-cheat pipeline — this is a SPECIFICATION for future implementation
    // or audit locking of an existing native system if binary evidence is discovered.
    // ================================================================================================

    /// <summary>Time window for death rate calculation.</summary>
    public enum DeathRateWindow
    {
        /// <summary>Last 5 minutes — detects rapid repeated deaths (exploit/suicide farming).</summary>
        Short,
        /// <summary>Last 30 minutes — detects sustained abnormal death patterns.</summary>
        Medium,
        /// <summary>Last 2 hours — detects long-term behavioral anomalies.</summary>
        Long,
    }

    /// <summary>Severity classification for death rate violations.</summary>
    public enum DeathRateSeverity
    {
        /// <summary>No violation detected — normal death rate.</summary>
        Normal,
        /// <summary>Informational — slightly elevated, log only.</summary>
        Info,
        /// <summary>Warning — suspicious pattern, flag for review.</summary>
        Warning,
        /// <summary>Critical — clear violation, immediate action required.</summary>
        Critical,
    }

    /// <summary>Context category for death event classification.</summary>
    public enum DeathContext
    {
        /// <summary>PK death (killed by another player).</summary>
        PlayerKill,
        /// <summary>Monster kill in normal map.</summary>
        PveNormal,
        /// <summary>Monster kill in high-risk map (boss area, dungeon).</summary>
        PveHighRisk,
        /// <summary>Environmental death (poison, trap, fall damage).</summary>
        Environmental,
        /// <summary>GM kill or admin action.</summary>
        Administrative,
    }

    /// <summary>Action to take when death rate threshold is exceeded.</summary>
    public enum DeathRateAction
    {
        /// <summary>No action — observation only.</summary>
        None,
        /// <summary>Log to anti-cheat audit trail.</summary>
        LogOnly,
        /// <summary>Log and flag player for GM review.</summary>
        FlagForReview,
        /// <summary>Log, flag, and apply temporary restrictions (e.g., reduced rewards).</summary>
        ApplyRestrictions,
        /// <summary>Log, flag, and kick player from server.</summary>
        KickPlayer,
        /// <summary>Log, flag, and initiate temporary ban.</summary>
        TemporaryBan,
    }

    /// <summary>Configuration thresholds for death rate monitoring.</summary>
    public sealed class DeathRateThresholds
    {
        /// <summary>Max deaths in short window (5 min) before triggering warning (default: 10).</summary>
        public int ShortWindowWarning { get; init; } = 10;
        /// <summary>Max deaths in short window before critical (default: 20).</summary>
        public int ShortWindowCritical { get; init; } = 20;

        /// <summary>Max deaths in medium window (30 min) before warning (default: 30).</summary>
        public int MediumWindowWarning { get; init; } = 30;
        /// <summary>Max deaths in medium window before critical (default: 50).</summary>
        public int MediumWindowCritical { get; init; } = 50;

        /// <summary>Max deaths in long window (2 hours) before warning (default: 60).</summary>
        public int LongWindowWarning { get; init; } = 60;
        /// <summary>Max deaths in long window before critical (default: 100).</summary>
        public int LongWindowCritical { get; init; } = 100;

        /// <summary>Multiplier for PK deaths (lower threshold, PK deaths are less suspicious).</summary>
        public double PkDeathMultiplier { get; init; } = 2.0;
        /// <summary>Multiplier for high-risk PvE (higher threshold, expected in difficult content).</summary>
        public double HighRiskPveMultiplier { get; init; } = 1.5;
    }

    /// <summary>Single death event record for rate calculation.</summary>
    public sealed class DeathEvent
    {
        /// <summary>Timestamp of death (server time, milliseconds since epoch).</summary>
        public long Timestamp { get; init; }
        /// <summary>Context/category of the death.</summary>
        public DeathContext Context { get; init; }
        /// <summary>Map name where death occurred.</summary>
        public string MapName { get; init; } = string.Empty;
        /// <summary>Killer name (player name or monster name, empty for environmental).</summary>
        public string KillerName { get; init; } = string.Empty;
        /// <summary>Player level at time of death.</summary>
        public int PlayerLevel { get; init; }
    }

    /// <summary>Result of death rate evaluation for a player over a specific time window.</summary>
    public sealed class DeathRateEvaluation
    {
        /// <summary>Time window evaluated.</summary>
        public DeathRateWindow Window { get; init; }
        /// <summary>Total death count in the window.</summary>
        public int DeathCount { get; init; }
        /// <summary>Threshold that was checked against (after context adjustments).</summary>
        public int EffectiveThreshold { get; init; }
        /// <summary>Severity classification of the result.</summary>
        public DeathRateSeverity Severity { get; init; }
        /// <summary>Recommended action based on severity and death count.</summary>
        public DeathRateAction RecommendedAction { get; init; }
        /// <summary>True if threshold was exceeded.</summary>
        public bool IsViolation => Severity > DeathRateSeverity.Info;
        /// <summary>Human-readable reason for the classification.</summary>
        public string Reason { get; init; } = string.Empty;
    }

    /// <summary>Complete anti-cheat death rate analysis for a player across all windows.</summary>
    public sealed class DeathRateAnalysis
    {
        /// <summary>Player character name.</summary>
        public string CharName { get; init; } = string.Empty;
        /// <summary>Analysis timestamp (server time).</summary>
        public long AnalysisTimestamp { get; init; }
        /// <summary>Evaluation for short window (5 min).</summary>
        public DeathRateEvaluation ShortWindow { get; init; } = null!;
        /// <summary>Evaluation for medium window (30 min).</summary>
        public DeathRateEvaluation MediumWindow { get; init; } = null!;
        /// <summary>Evaluation for long window (2 hours).</summary>
        public DeathRateEvaluation LongWindow { get; init; } = null!;
        /// <summary>Overall severity (highest severity across all windows).</summary>
        public DeathRateSeverity OverallSeverity { get; init; }
        /// <summary>Overall recommended action (most severe action across all windows).</summary>
        public DeathRateAction OverallAction { get; init; }
        /// <summary>True if any window shows a violation.</summary>
        public bool HasViolation => OverallSeverity > DeathRateSeverity.Info;
    }

    public static class AntiCheatDeathRateEvaluator
    {
        // Default time window durations (milliseconds)
        private const long ShortWindowMs = 5 * 60 * 1000;      // 5 minutes
        private const long MediumWindowMs = 30 * 60 * 1000;    // 30 minutes
        private const long LongWindowMs = 2 * 60 * 60 * 1000;  // 2 hours

        /// <summary>
        /// Evaluate death rate for a player over a specific time window.
        /// </summary>
        /// <param name="events">Death events in chronological order (oldest first).</param>
        /// <param name="window">Time window to evaluate.</param>
        /// <param name="currentTime">Current server time (milliseconds since epoch).</param>
        /// <param name="thresholds">Configuration thresholds.</param>
        public static DeathRateEvaluation EvaluateWindow(
            System.Collections.Generic.IReadOnlyList<DeathEvent> events,
            DeathRateWindow window,
            long currentTime,
            DeathRateThresholds thresholds)
        {
            var windowMs = GetWindowDuration(window);
            var cutoffTime = currentTime - windowMs;

            // Filter events within window
            var recentDeaths = new System.Collections.Generic.List<DeathEvent>();
            foreach (var evt in events)
                if (evt.Timestamp >= cutoffTime)
                    recentDeaths.Add(evt);

            var deathCount = recentDeaths.Count;

            // Calculate context-adjusted threshold
            var effectiveThreshold = CalculateEffectiveThreshold(recentDeaths, window, thresholds);

            // Determine severity
            var (severity, action, reason) = ClassifySeverity(deathCount, effectiveThreshold, window, thresholds);

            return new DeathRateEvaluation
            {
                Window = window,
                DeathCount = deathCount,
                EffectiveThreshold = effectiveThreshold,
                Severity = severity,
                RecommendedAction = action,
                Reason = reason,
            };
        }

        /// <summary>
        /// Perform complete multi-window analysis for a player.
        /// </summary>
        public static DeathRateAnalysis AnalyzePlayer(
            string charName,
            System.Collections.Generic.IReadOnlyList<DeathEvent> events,
            long currentTime,
            DeathRateThresholds thresholds)
        {
            var shortEval = EvaluateWindow(events, DeathRateWindow.Short, currentTime, thresholds);
            var mediumEval = EvaluateWindow(events, DeathRateWindow.Medium, currentTime, thresholds);
            var longEval = EvaluateWindow(events, DeathRateWindow.Long, currentTime, thresholds);

            // Overall severity = max severity across all windows
            var overallSeverity = MaxSeverity(shortEval.Severity, mediumEval.Severity, longEval.Severity);

            // Overall action = most severe action
            var overallAction = MaxAction(shortEval.RecommendedAction, mediumEval.RecommendedAction, longEval.RecommendedAction);

            return new DeathRateAnalysis
            {
                CharName = charName,
                AnalysisTimestamp = currentTime,
                ShortWindow = shortEval,
                MediumWindow = mediumEval,
                LongWindow = longEval,
                OverallSeverity = overallSeverity,
                OverallAction = overallAction,
            };
        }

        private static long GetWindowDuration(DeathRateWindow window) => window switch
        {
            DeathRateWindow.Short => ShortWindowMs,
            DeathRateWindow.Medium => MediumWindowMs,
            DeathRateWindow.Long => LongWindowMs,
            _ => throw new System.ArgumentOutOfRangeException(nameof(window)),
        };

        private static int CalculateEffectiveThreshold(
            System.Collections.Generic.List<DeathEvent> events,
            DeathRateWindow window,
            DeathRateThresholds thresholds)
        {
            var baseThreshold = GetBaseWarningThreshold(window, thresholds);

            // Count deaths by context
            var pkCount = 0;
            var highRiskCount = 0;
            foreach (var evt in events)
            {
                if (evt.Context == DeathContext.PlayerKill)
                    pkCount++;
                else if (evt.Context == DeathContext.PveHighRisk)
                    highRiskCount++;
            }

            // Apply context multipliers (increase threshold for expected-high-death contexts)
            var adjustment = 0.0;
            if (events.Count > 0)
            {
                var pkRatio = (double)pkCount / events.Count;
                var highRiskRatio = (double)highRiskCount / events.Count;
                adjustment = pkRatio * (thresholds.PkDeathMultiplier - 1.0)
                           + highRiskRatio * (thresholds.HighRiskPveMultiplier - 1.0);
            }

            return (int)(baseThreshold * (1.0 + adjustment));
        }

        private static int GetBaseWarningThreshold(DeathRateWindow window, DeathRateThresholds thresholds) => window switch
        {
            DeathRateWindow.Short => thresholds.ShortWindowWarning,
            DeathRateWindow.Medium => thresholds.MediumWindowWarning,
            DeathRateWindow.Long => thresholds.LongWindowWarning,
            _ => throw new System.ArgumentOutOfRangeException(nameof(window)),
        };

        private static (DeathRateSeverity severity, DeathRateAction action, string reason) ClassifySeverity(
            int deathCount,
            int effectiveThreshold,
            DeathRateWindow window,
            DeathRateThresholds thresholds)
        {
            var criticalThreshold = GetCriticalThreshold(window, thresholds);

            if (deathCount >= criticalThreshold)
                return (DeathRateSeverity.Critical, DeathRateAction.ApplyRestrictions,
                       $"Critical: {deathCount} deaths exceeds critical threshold {criticalThreshold}");

            if (deathCount >= effectiveThreshold)
                return (DeathRateSeverity.Warning, DeathRateAction.FlagForReview,
                       $"Warning: {deathCount} deaths exceeds warning threshold {effectiveThreshold}");

            if (deathCount >= effectiveThreshold * 0.7)
                return (DeathRateSeverity.Info, DeathRateAction.LogOnly,
                       $"Info: {deathCount} deaths approaching threshold {effectiveThreshold}");

            return (DeathRateSeverity.Normal, DeathRateAction.None, "Normal death rate");
        }

        private static int GetCriticalThreshold(DeathRateWindow window, DeathRateThresholds thresholds) => window switch
        {
            DeathRateWindow.Short => thresholds.ShortWindowCritical,
            DeathRateWindow.Medium => thresholds.MediumWindowCritical,
            DeathRateWindow.Long => thresholds.LongWindowCritical,
            _ => throw new System.ArgumentOutOfRangeException(nameof(window)),
        };

        private static DeathRateSeverity MaxSeverity(params DeathRateSeverity[] severities)
        {
            var max = DeathRateSeverity.Normal;
            foreach (var s in severities)
                if (s > max)
                    max = s;
            return max;
        }

        private static DeathRateAction MaxAction(params DeathRateAction[] actions)
        {
            var max = DeathRateAction.None;
            foreach (var a in actions)
                if (a > max)
                    max = a;
            return max;
        }
    }
}
