using System.Text.RegularExpressions;

namespace GameGate.Core;

/// <summary>
/// Dirty-word (abusive language) filter matching the Delphi AbusiveFilter.
/// Config file: AbusiveFilter.txt — each line is "pattern|action".
/// Actions: ReplaceAll, ReplaceOne, DropConnect.
/// Replacement is "***" (Delphi convention).
/// </summary>
public sealed class AbusiveFilter
{
    private readonly List<FilterRule> _rules = [];
    private readonly object _rulesLock = new();

    public int RuleCount { get { lock (_rulesLock) return _rules.Count; } }

    public void LoadRules(string filePath)
    {
        if (!File.Exists(filePath)) return;
        var loaded = new List<FilterRule>();
        foreach (var raw in File.ReadAllLines(filePath, System.Text.Encoding.GetEncoding("GBK")))
        {
            var line = raw.Trim();
            if (string.IsNullOrEmpty(line) || line.StartsWith('#') || line.StartsWith(';'))
                continue;

            int sep = line.LastIndexOf('|');
            if (sep < 0) continue;
            string pattern = line[..sep].Trim();
            string action = line[(sep + 1)..].Trim();
            if (string.IsNullOrEmpty(pattern) || string.IsNullOrEmpty(action)) continue;

            FilterAction act = action.ToLowerInvariant() switch
            {
                "replaceall" => FilterAction.ReplaceAll,
                "replaceone" => FilterAction.ReplaceOne,
                "dropconnect" => FilterAction.DropConnect,
                _ => FilterAction.ReplaceAll
            };

            loaded.Add(new FilterRule { Pattern = pattern, Action = act });
        }
        lock (_rulesLock) _rules.AddRange(loaded);
    }

    public void ReloadRules(string filePath)
    {
        lock (_rulesLock) _rules.Clear();
        LoadRules(filePath);
    }

    /// <summary>
    /// Filter a chat message. Returns (filteredText, shouldDrop).
    /// shouldDrop=true means the connection should be killed (DropConnect match).
    /// </summary>
    public (string filtered, bool shouldDrop) Filter(string text)
    {
        FilterRule[] rules;
        lock (_rulesLock) rules = _rules.ToArray();
        if (string.IsNullOrEmpty(text) || rules.Length == 0)
            return (text, false);

        bool shouldDrop = false;

        foreach (var rule in rules)
        {
            if (!text.Contains(rule.Pattern, StringComparison.OrdinalIgnoreCase))
                continue;

            switch (rule.Action)
            {
                case FilterAction.DropConnect:
                    shouldDrop = true;
                    break;
                case FilterAction.ReplaceOne:
                    {
                        int idx = text.IndexOf(rule.Pattern, StringComparison.OrdinalIgnoreCase);
                        if (idx >= 0)
                            text = text[..idx] + "***" + text[(idx + rule.Pattern.Length)..];
                    }
                    break;
                case FilterAction.ReplaceAll:
                    text = ReplaceAllIgnoreCase(text, rule.Pattern, "***");
                    break;
            }
        }

        return (text, shouldDrop);
    }

    private static string ReplaceAllIgnoreCase(string source, string oldValue, string newValue)
    {
        if (string.IsNullOrEmpty(oldValue)) return source;
        int idx = 0;
        var result = new System.Text.StringBuilder(source.Length);
        while (idx < source.Length)
        {
            int found = source.IndexOf(oldValue, idx, StringComparison.OrdinalIgnoreCase);
            if (found < 0)
            {
                result.Append(source.AsSpan(idx));
                break;
            }
            result.Append(source.AsSpan(idx, found - idx));
            result.Append(newValue);
            idx = found + oldValue.Length;
        }
        return result.ToString();
    }

    private sealed class FilterRule
    {
        public string Pattern = "";
        public FilterAction Action;
    }

    public enum FilterAction
    {
        ReplaceAll,
        ReplaceOne,
        DropConnect
    }
}
