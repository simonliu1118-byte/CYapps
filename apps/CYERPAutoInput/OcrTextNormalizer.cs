using System.Text;

namespace CYERPAutoInput;

internal static class OcrTextNormalizer
{
    private static readonly Dictionary<char, char> CommonSimplifiedToTraditional = new()
    {
        ['换'] = '換', ['单'] = '單', ['数'] = '數', ['号'] = '號', ['规'] = '規',
        ['类'] = '類', ['赠'] = '贈', ['备'] = '備', ['库'] = '庫', ['别'] = '別',
        ['价'] = '價', ['额'] = '額', ['货'] = '貨', ['户'] = '戶', ['税'] = '稅',
        ['发'] = '發', ['开'] = '開', ['间'] = '間', ['资'] = '資', ['业'] = '業',
        ['销'] = '銷', ['员'] = '員', ['币'] = '幣', ['汇'] = '匯', ['传'] = '傳',
        ['联'] = '聯', ['电'] = '電', ['话'] = '話', ['邮'] = '郵', ['码'] = '碼',
        ['张'] = '張', ['输'] = '輸', ['证'] = '證', ['账'] = '帳', ['务'] = '務',
        ['总'] = '總', ['统'] = '統', ['应'] = '應', ['计'] = '計'
    };

    public static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;

        var builder = new StringBuilder(value.Length);
        foreach (var ch in value.Trim())
        {
            if (char.IsWhiteSpace(ch) || ch == '　') continue;

            var normalized = ch switch
            {
                '（' => '(',
                '）' => ')',
                '：' => ':',
                _ => CommonSimplifiedToTraditional.TryGetValue(ch, out var mapped) ? mapped : ch
            };
            builder.Append(normalized);
        }
        return builder.ToString();
    }
}
