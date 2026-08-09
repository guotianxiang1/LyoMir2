using System.Diagnostics;
using System.Threading;
using GameGate.Models;
using SystemModule;

namespace GameGate.Core;

/// <summary>
/// 10-dimension speed detection engine.
/// An action violates its configured interval when it arrives before that interval elapses.
/// </summary>
public sealed class SpeedDetector
{
    private const double ViolationWindow = 5.0; // 5s window (Delphi original, not 10s)
    private const int DropConnectThreshold = 30;

    private readonly GateConfig _cfg;
    private readonly Dictionary<ActionType, double> _limits = new();
    private readonly object _limitsLock = new();

    // Stats
    public long TotalChecks, TotalViolations, TotalPenalties;
    public readonly Dictionary<ActionType, int> ViolationsByType = new();

    // Events
    public event Action<ClientSession, ActionType, double, double>? OnViolation;
    public event Action<ClientSession, PenaltyLevel, string>? OnPenalty;

    public SpeedDetector(GateConfig cfg)
    {
        _cfg = cfg;
        foreach (ActionType a in Enum.GetValues<ActionType>())
            ViolationsByType[a] = 0;

        ReloadLimits();
    }

    /// <summary>
    /// Returns false when the measured interval is shorter than the configured action limit.
    /// </summary>
    public bool Check(ClientSession session, ActionType action)
    {
        if (session == null || session.IsBanned) return false;

        Interlocked.Increment(ref TotalChecks);
        double now = (double)Stopwatch.GetTimestamp() / Stopwatch.Frequency;
        var record = session.SpeedRecords[(byte)action];

        if (record.LastTime == 0)
        {
            record.LastTime = now;
            record.TotalCount = 1;
            return true;
        }

        double elapsed = now - record.LastTime;
        double limit;
        lock (_limitsLock) limit = _limits[action];

        if (elapsed >= limit)
        {
            // OK: speed is acceptable
            record.LastTime = now;
            record.TotalCount++;
            record.ViolateCount = 0;
            Interlocked.Exchange(ref session.DropPackCount, 0);
            CheckWindowReset(session, now);
            return true;
        }

        // VIOLATION — packet arrived too fast
        record.ViolateCount++;
        record.TotalCount++;
        record.LastTime = now;
        Interlocked.Increment(ref session.TotalViolations);
        Interlocked.Increment(ref TotalViolations);

        lock (ViolationsByType)
            ViolationsByType[action] = ViolationsByType.GetValueOrDefault(action) + 1;

        Interlocked.Increment(ref session.DropPackCount);
        if (action <= ActionType.TURN) Interlocked.Increment(ref session.ViolationCount1);
        else if (action == ActionType.CHAT) Interlocked.Increment(ref session.ViolationCount2);
        else Interlocked.Increment(ref session.ViolationCount3);

        CheckThresholds(session, action);
        OnViolation?.Invoke(session, action, elapsed, limit);
        return false;
    }

    private void CheckWindowReset(ClientSession session, double now)
    {
        // Delphi uses 5-second violation window, not 10 seconds
        if (now - session.LastCheckTime10s > ViolationWindow)
        {
            session.LastCheckTime10s = now;
            Interlocked.Exchange(ref session.ViolationCount1, 0);
            Interlocked.Exchange(ref session.ViolationCount2, 0);
            Interlocked.Exchange(ref session.ViolationCount3, 0);
        }
    }

    private void CheckThresholds(ClientSession session, ActionType action)
    {
        int v1 = session.ViolationCount1, v2 = session.ViolationCount2, v3 = session.ViolationCount3;
        bool shouldDrop = (action <= ActionType.TURN && v1 >= 3) ||
                          (action == ActionType.CHAT && v2 >= 5) ||
                          (action >= ActionType.BUY && v3 >= 5);

        if (!shouldDrop) return;

        var dropCount = Interlocked.Increment(ref session.DropConnectCount);
        if (dropCount >= DropConnectThreshold)
            ApplyPenalty(session, PenaltyLevel.BANNED, "DropConnect threshold reached");
        else if (session.PenaltyLevel == PenaltyLevel.NONE)
            ApplyPenalty(session, PenaltyLevel.WARNED, $"Speed violation: {action}");
        else if (session.PenaltyLevel == PenaltyLevel.WARNED)
            ApplyPenalty(session, PenaltyLevel.OBSERVED, $"Repeated violation: {action}");
        else if (session.PenaltyLevel == PenaltyLevel.OBSERVED)
            ApplyPenalty(session, PenaltyLevel.MUTED, $"Muted: {action}");
        else if (session.PenaltyLevel == PenaltyLevel.MUTED)
            ApplyPenalty(session, PenaltyLevel.BANNED, $"Kicked: {action}");
    }

    private void ApplyPenalty(ClientSession session, PenaltyLevel level, string reason)
    {
        session.PenaltyLevel = level;
        Interlocked.Increment(ref TotalPenalties);
        if (level >= PenaltyLevel.BANNED) { session.BanFlag = true; session.State = SessionState.BANNED; }
        else if (level == PenaltyLevel.MUTED) session.State = SessionState.MUTED;
        OnPenalty?.Invoke(session, level, reason);
    }

    public void ReloadLimits()
    {
        lock (_limitsLock)
        {
            _limits[ActionType.WALK] = _cfg.WalkInterval / 1000.0;
            _limits[ActionType.RUN] = _cfg.WalkInterval / 1000.0 * 0.75;
            _limits[ActionType.ATTACK] = _cfg.AttackInterval / 1000.0;
            _limits[ActionType.CAST] = _cfg.CastInterval / 1000.0;
            _limits[ActionType.TURN] = _cfg.TurnInterval / 1000.0;
            _limits[ActionType.BUY] = _cfg.ShopInterval / 1000.0;
            _limits[ActionType.CURE] = _cfg.CureInterval / 1000.0;
            _limits[ActionType.NPC] = _cfg.NpcInterval / 1000.0;
            _limits[ActionType.TRADE] = 1.0;
            _limits[ActionType.CHAT] = 0.8;
        }
    }

    public double GetLimit(ActionType a)
    {
        lock (_limitsLock) return _limits.GetValueOrDefault(a, 0.5);
    }
}

/// <summary>Classify game command into action type for speed detection.</summary>
public static class ActionClassifier
{
    public static bool TryClassify(ushort cmd, out ActionType action)
    {
        switch (cmd)
        {
            case Grobal2.CM_WALK:
                action = ActionType.WALK;
                return true;
            case Grobal2.CM_RUN:
            case Grobal2.CM_HORSERUN:
                action = ActionType.RUN;
                return true;
            case Grobal2.CM_HIT:
            case Grobal2.CM_HEAVYHIT:
            case Grobal2.CM_BIGHIT:
            case Grobal2.CM_POWERHIT:
            case Grobal2.CM_LONGHIT:
            case Grobal2.CM_WIDEHIT:
            case Grobal2.CM_FIREHIT:
            case Grobal2.CM_CRSHIT:
            case Grobal2.CM_TWINHIT:
                action = ActionType.ATTACK;
                return true;
            case Grobal2.CM_SPELL:
                action = ActionType.CAST;
                return true;
            case Grobal2.CM_TURN:
                action = ActionType.TURN;
                return true;
            case Grobal2.CM_SAY:
                action = ActionType.CHAT;
                return true;
            case Grobal2.CM_USERBUYITEM:
            case Grobal2.CM_BUY_STALLITEM:
                action = ActionType.BUY;
                return true;
            case Grobal2.CM_EAT:
            case Grobal2.CM_1069:
                action = ActionType.CURE;
                return true;
            case Grobal2.CM_CLICKNPC:
            case Grobal2.CM_MERCHANTDLGSELECT:
                action = ActionType.NPC;
                return true;
            case >= Grobal2.CM_DEALTRY and <= Grobal2.CM_DEALEND:
                action = ActionType.TRADE;
                return true;
            default:
                action = default;
                return false;
        }
    }
}
