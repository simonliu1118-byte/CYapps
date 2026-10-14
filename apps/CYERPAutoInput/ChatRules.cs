namespace CYERPAutoInput;

/// <summary>One 發送訊息 rule: words looked for in the buyer's note, and the message sent when one is there.</summary>
internal sealed class ChatRule
{
    /// <summary>Comma separated (，、 also count).</summary>
    public string Keywords { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
}

/// <summary>
/// The 發送訊息 rules of one platform (user, 2026-10-14: several rules, each with its own words
/// and message; a word may belong to one rule only, so a note never picks two messages for
/// the same word). Pure: no UI, unit tested.
/// </summary>
internal static class ChatRules
{
    public static IReadOnlyList<string> Words(string keywords) =>
        keywords.Split([',', '，', '、'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(w => w.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

    /// <summary>The message as one line (Enter sends in 聊聊).</summary>
    public static string OneLine(string message) =>
        string.Join(" ", message.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

    /// <summary>
    /// What is wrong with the rules, for the settings to refuse saving: a word in two rules,
    /// a rule with words but no message or a message but no words. Empty rules are ignored.
    /// </summary>
    public static List<string> Problems(IReadOnlyList<ChatRule> rules)
    {
        var problems = new List<string>();
        var owner = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < rules.Count; i++)
        {
            var words = Words(rules[i].Keywords);
            var message = OneLine(rules[i].Message);
            if (words.Count == 0 && message.Length == 0) continue;
            if (words.Count == 0) problems.Add($"第 {i + 1} 則沒有關鍵字。");
            if (message.Length == 0) problems.Add($"第 {i + 1} 則沒有要送出的訊息。");
            foreach (var word in words)
            {
                var compact = ShopeeScreenshot.Compact(word);
                if (owner.TryGetValue(compact, out var other)) problems.Add($"關鍵字「{word}」同時出現在第 {other + 1} 則和第 {i + 1} 則，請只留在其中一則。");
                else owner[compact] = i;
            }
        }
        return problems;
    }

    /// <summary>The rules whose words the note mentions, in the rules' order (each once), with the word found.</summary>
    public static List<(ChatRule Rule, string Word)> Matches(string note, IReadOnlyList<ChatRule> rules)
    {
        var compact = ShopeeScreenshot.Compact(note);
        var found = new List<(ChatRule, string)>();
        if (compact.Length == 0) return found;
        foreach (var rule in rules)
        {
            if (OneLine(rule.Message).Length == 0) continue;
            var word = Words(rule.Keywords).FirstOrDefault(w => compact.Contains(ShopeeScreenshot.Compact(w), StringComparison.OrdinalIgnoreCase));
            if (word is not null) found.Add((rule, word));
        }
        return found;
    }

    /// <summary>Rules without blank rows, as saved.</summary>
    public static List<ChatRule> Clean(IEnumerable<ChatRule> rules) =>
        rules.Where(r => Words(r.Keywords).Count > 0 || OneLine(r.Message).Length > 0)
            .Select(r => new ChatRule { Keywords = string.Join(",", Words(r.Keywords)), Message = r.Message.Trim() }).ToList();
}
