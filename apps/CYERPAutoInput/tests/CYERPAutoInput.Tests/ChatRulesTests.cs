using Xunit;

namespace CYERPAutoInput.Tests;

/// <summary>設定 › 發送訊息 rules. Messages are made up.</summary>
public class ChatRulesTests
{
    private static ChatRule R(string words, string message) => new() { Keywords = words, Message = message };

    [Fact]
    public void A_word_belongs_to_one_rule_only()
    {
        Assert.Empty(ChatRules.Problems([R("發票,統編", "訊息A"), R("運費，延遲", "訊息B")]));
        var problems = ChatRules.Problems([R("發票,統編", "訊息A"), R("運費、統編", "訊息B")]);
        Assert.Contains(problems, p => p.Contains("統編") && p.Contains("第 1 則") && p.Contains("第 2 則"));
        // The same word twice in one rule is not a conflict; spaces and letter case do not matter.
        Assert.Empty(ChatRules.Problems([R("統編, 統編", "訊息A")]));
        Assert.NotEmpty(ChatRules.Problems([R("ABC", "訊息A"), R("a b c", "訊息B")]));
    }

    [Fact]
    public void Half_filled_rules_are_refused_and_empty_ones_ignored()
    {
        Assert.Contains("第 1 則沒有要送出的訊息。", ChatRules.Problems([R("統編", " ")]));
        Assert.Contains("第 2 則沒有關鍵字。", ChatRules.Problems([R("統編", "訊息A"), R("", "訊息B")]));
        Assert.Empty(ChatRules.Problems([R("", ""), R("統編", "訊息A")]));
        Assert.Single(ChatRules.Clean([R("", ""), R(" 統編 ,發票,", " 訊息A ")]));
        Assert.Equal("統編,發票", ChatRules.Clean([R(" 統編 ,發票,", "訊息A")])[0].Keywords);
    }

    [Fact]
    public void A_note_picks_every_rule_it_mentions_once()
    {
        List<ChatRule> rules = [R("發票,統編", "訊息A"), R("延遲,晚點出", "訊息B"), R("贈品", "訊息C")];
        var found = ChatRules.Matches("希望打統編，也可以晚點出貨嗎 發票寄給我", rules);
        Assert.Equal(["訊息A", "訊息B"], found.Select(f => f.Rule.Message));
        Assert.Equal("發票", found[0].Word);
        Assert.Empty(ChatRules.Matches("", rules));
        Assert.Equal("第一行 第二行", ChatRules.OneLine("第一行\r\n第二行\n"));
    }
}
