using System.Collections.Concurrent;
using System.Globalization;
using System.Text.RegularExpressions;

namespace CS2Hazirla.Core;

/// <summary>
/// Two languages from one source. The text in the code is Turkish and doubles as the key; <see cref="EnglishStrings"/> holds the English for every one of them.
/// In Turkish nothing is looked up at all (the Turkish text comes out exactly as it is written), so everything that reads Turkish text keeps working.
///
/// Rules for code that shows text:
///  · translate where the text is SHOWN: <c>Loc.T("...")</c> for a fixed text, <c>Loc.F("... {0} ...", x)</c> for a text with values in it;
///  · the first argument is ONE string literal (literals joined with + are fine): it is the key, and the tests read it from the source;
///  · never translate in a static initializer, and never translate a value that is compared, stored in a file or used as an id (identity stays Turkish,
///    the display text next to it is translated);
///  · English may need a plural: write <c>{0?program:programs}</c> in the English template (the word before the colon when argument 0 is 1, else the one after it).
/// </summary>
public static class Loc
{
    public const string Turkish = "tr";
    public const string English = "en";

    private static volatile string _language = Turkish;
    private static readonly ConcurrentDictionary<string, byte> _missing = new();

    public static string Language => _language;
    public static bool IsEnglish => _language == English;

    /// <summary>The culture of numbers in texts that name one explicitly (Turkish "9,84", English "9.84").</summary>
    public static CultureInfo Culture => CultureInfo.GetCultureInfo(IsEnglish ? "en-US" : "tr-TR");

    /// <summary>Texts that were asked for in English and have no English entry (they stayed Turkish). For tests and diagnostics.</summary>
    public static IReadOnlyCollection<string> Missing => _missing.Keys.ToList();

    public static string Normalize(string? language) =>
        string.Equals(language?.Trim(), English, StringComparison.OrdinalIgnoreCase) ? English : Turkish;

    /// <summary>
    /// The language of a start: the one the user chose, or without a choice Turkish on a Turkish Windows and English on every other.
    /// </summary>
    public static string Detect(string? saved, CultureInfo? windowsLanguage = null)
    {
        if (!string.IsNullOrWhiteSpace(saved)) return Normalize(saved);
        var ui = windowsLanguage ?? CultureInfo.CurrentUICulture;
        return string.Equals(ui.TwoLetterISOLanguageName, "tr", StringComparison.OrdinalIgnoreCase) ? Turkish : English;
    }

    /// <summary>Makes <paramref name="language"/> the language of everything that is shown from now on (and of the numbers the app formats).</summary>
    public static void Use(string? language)
    {
        _language = Normalize(language);
        // English numbers read "1.5 GB"; Turkish keeps what Windows says (as before)
        var culture = IsEnglish ? CultureInfo.GetCultureInfo("en-US") : null;
        CultureInfo.DefaultThreadCurrentCulture = culture;
        CultureInfo.DefaultThreadCurrentUICulture = culture;
        if (culture != null) { CultureInfo.CurrentCulture = culture; CultureInfo.CurrentUICulture = culture; }
    }

    public static string T(string tr)
    {
        if (_language != English) return tr;
        if (EnglishStrings.Map.TryGetValue(tr, out var en)) return en;
        _missing.TryAdd(tr, 0);
        return tr;
    }

    public static string F(string tr, params object?[] args)
    {
        var template = T(tr);
        if (_language == English) template = ExpandPlurals(template, args);
        return string.Format(CultureInfo.CurrentCulture, template, args);
    }

    private static readonly Regex Plural = new(@"\{(\d+)\?([^{}:]*):([^{}]*)\}", RegexOptions.CultureInvariant);

    /// <summary><c>{0?program:programs}</c> becomes "program" when argument 0 is 1 and "programs" otherwise.</summary>
    public static string ExpandPlurals(string template, object?[] args) =>
        Plural.Replace(template, m =>
        {
            var i = int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
            var one = i < args.Length && args[i] != null && IsOne(args[i]!);
            return one ? m.Groups[2].Value : m.Groups[3].Value;
        });

    private static bool IsOne(object value)
    {
        try { return Convert.ToInt64(value, CultureInfo.InvariantCulture) == 1; }
        catch { return false; }
    }

    /// <summary>Test hook: forget what was reported missing.</summary>
    public static void ClearMissing() => _missing.Clear();
}
