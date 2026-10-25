using System.Globalization;
using System.Text;

namespace CS2Hazirla.Core;

/// <summary>
/// The search box of the Ayrıntılar panel: a Turkish keyboard without the Turkish letters still finds what it looks for
/// ("tarayicilar", "ISIK", "gorev" find "Tarayıcılar", "ışık", "görev"), and several words must all be there.
/// English needs nothing of its own: plain letters are only lower-cased (invariantly, so a Turkish Windows cannot turn I into ı; I, ı and İ all
/// fold to i), and both the text and the query go through the same folding, so "Light", "light" and "LIGHT" meet whatever language the text is in.
/// </summary>
public static class SearchText
{
    public static string Normalize(string? text)
    {
        if (string.IsNullOrEmpty(text)) return "";
        var sb = new StringBuilder(text.Length);
        foreach (var ch in text.Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(ch) == UnicodeCategory.NonSpacingMark) continue;     // the dot, cedilla, breve, umlaut
            sb.Append(ch is 'ı' or 'I' or 'İ' ? 'i' : char.ToLowerInvariant(ch));
        }
        return sb.ToString();
    }

    /// <param name="haystack">Already normalized (<see cref="Normalize"/>): the rows keep theirs, it is built once.</param>
    public static bool Matches(string? query, string haystack)
    {
        var q = Normalize(query);
        if (q.Length == 0) return true;
        foreach (var word in q.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            if (!haystack.Contains(word, StringComparison.Ordinal)) return false;
        return true;
    }
}
