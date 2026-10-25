using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using CS2Hazirla.Core;
using Xunit;

namespace CS2Hazirla.Tests;

/// <summary>
/// Turkish and English from one source. The Turkish text in the code is the key; English comes from <see cref="EnglishStrings"/>.
/// These tests read the source files, so a text that is shown but has no English cannot be added without a test failing.
/// </summary>
[Collection("language")]
public class LanguageTests : IDisposable
{
    private static readonly Regex TurkishLetter = new("[çğıöşüÇĞİÖŞÜ]", RegexOptions.CultureInvariant);
    private static readonly Regex PlaceholderRx = new(@"\{(\d+)(?:[:,][^{}]*)?\}", RegexOptions.CultureInvariant);

    public LanguageTests() => Loc.Use(TestLanguage.Default);
    public void Dispose() => Loc.Use(TestLanguage.Default);

    private static string Root()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "CS2Hazirla.sln"))) dir = dir.Parent;
        Assert.NotNull(dir);
        return dir!.FullName;
    }

    private static IEnumerable<string> SourceFiles(string pattern) =>
        Directory.EnumerateFiles(Path.Combine(Root(), "src"), pattern, SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}") && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"));

    // ------------------------------------------------------------------ the mechanism

    [Fact]
    public void Turkish_comes_out_exactly_as_written()
    {
        Loc.Use("tr");
        Loc.ClearMissing();
        Assert.Equal("Önizleme", Loc.T("Önizleme"));
        Assert.Equal("3 program kapatılacak", Loc.F("{0} program kapatılacak", 3));
        Assert.Equal("a 2,5 b", string.Format(new CultureInfo("tr-TR"), "a {0} b", 2.5));
        Assert.Empty(Loc.Missing);        // nothing is looked up in Turkish
    }

    [Fact]
    public void English_uses_the_entry_and_falls_back_to_the_Turkish_text_when_there_is_none()
    {
        Loc.Use("en");
        Loc.ClearMissing();
        Assert.Equal("Türkçe", Loc.T("Türkçe"));
        Assert.Equal("The window reopens when you change the language.", Loc.T("Dili değiştirince pencere yeniden açılır."));
        Assert.Equal("bu metin sözlükte yok", Loc.T("bu metin sözlükte yok"));
        Assert.Contains("bu metin sözlükte yok", Loc.Missing);
    }

    [Theory]
    [InlineData("{0} {0?program:programs} closed", 1, "1 program closed")]
    [InlineData("{0} {0?program:programs} closed", 0, "0 programs closed")]
    [InlineData("{0} {0?program:programs} closed", 12, "12 programs closed")]
    [InlineData("{1?is:are} {0} left", 5, "are 5 left")]
    public void An_English_template_can_say_one_or_many(string template, int n, string expected)
    {
        var args = template.StartsWith("{1?") ? new object[] { n, n } : new object[] { n };
        Assert.Equal(expected, string.Format(CultureInfo.InvariantCulture, Loc.ExpandPlurals(template, args), args));
    }

    [Fact]
    public void The_language_follows_the_choice_and_without_one_Windows()
    {
        Assert.Equal("en", Loc.Detect("en"));
        Assert.Equal("tr", Loc.Detect("tr"));
        Assert.Equal("tr", Loc.Detect("  TR "));
        Assert.Equal("tr", Loc.Detect("de"));                                       // an unknown choice is the source language
        Assert.Equal("tr", Loc.Detect("", new CultureInfo("tr-TR")));
        Assert.Equal("en", Loc.Detect("", new CultureInfo("en-US")));
        Assert.Equal("en", Loc.Detect(null, new CultureInfo("de-DE")));
        Assert.Equal("en", Loc.Detect(null, new CultureInfo("fr-FR")));
    }

    [Fact]
    public void The_saved_language_is_read_without_touching_the_settings_file()
    {
        var dir = Path.Combine(Path.GetTempPath(), "antilag-lang-" + Guid.NewGuid().ToString("N"));
        var before = AppPaths.Root;
        try
        {
            Directory.CreateDirectory(dir);
            AppPaths.Root = dir;
            Assert.Null(SettingsStore.PeekLanguage());                                // no file: no choice
            File.WriteAllText(AppPaths.SettingsFile, "{ not json");
            Assert.Null(SettingsStore.PeekLanguage());                                // unreadable: no choice, and no copy of the broken file
            Assert.Single(Directory.GetFiles(dir));
            File.WriteAllText(AppPaths.SettingsFile, "{ \"Language\": \"en\" }");
            Assert.Equal("en", SettingsStore.PeekLanguage());
            Assert.Equal("en", new SettingsStore().Load().Language);
            File.WriteAllText(AppPaths.SettingsFile, "{ \"Language\": null }");
            Assert.Equal("", new SettingsStore().Load().Language);                   // a hand-edited null never crashes
        }
        finally { AppPaths.Root = before; try { Directory.Delete(dir, true); } catch { } }
    }

    [Fact]
    public void A_profile_run_carries_over_to_the_window_that_replaces_it()
    {
        var s = new AppSettings();
        var a = new ProfileSession(s);
        a.Activate(ProfileKind.Stream);
        s.Options["proc.discord"] = !s.Options["proc.discord"];
        a.SetAppChoice("spotify", true, false);
        var snap = a.TakeSnapshot();

        var s2 = new AppSettings();
        var b = new ProfileSession(s2);
        b.Restore(snap);
        Assert.Equal(ProfileSession.StreamKey, b.ActiveKey);
        Assert.Equal(s.Options["proc.discord"], s2.Options["proc.discord"]);
        Assert.True(b.AppChoices["spotify"]);
        Assert.Equal(ProfileKind.Stream, s2.Profile);
    }

    // ------------------------------------------------------------------ the dictionary

    [Fact]
    public void No_Turkish_text_has_two_different_English_texts()
    {
        Assert.Empty(EnglishStrings.Conflicts);
    }

    [Fact]
    public void Every_entry_has_an_English_text_that_keeps_the_values_of_the_Turkish_one()
    {
        foreach (var (area, entries) in EnglishStrings.Areas)
            foreach (var (tr, en) in entries)
            {
                Assert.False(string.IsNullOrWhiteSpace(tr), $"[{area}] an entry without a Turkish text");
                Assert.False(string.IsNullOrWhiteSpace(en), $"[{area}] no English for: {tr}");
                var trSlots = PlaceholderRx.Matches(tr).Select(m => m.Groups[1].Value).Distinct().OrderBy(x => x).ToList();
                var enSlots = PlaceholderRx.Matches(Loc.ExpandPlurals(en, new object[] { 2, 2, 2, 2, 2, 2 })).Select(m => m.Groups[1].Value).Distinct().OrderBy(x => x).ToList();
                Assert.True(trSlots.SequenceEqual(enSlots), $"[{area}] the English uses other {{n}} values than the Turkish: {tr}  →  {en}");
                // an English text is English: Turkish letters mean it was left untranslated (names that are the same in both are fine: they are equal)
                if (en != tr) Assert.False(TurkishLetter.IsMatch(en), $"[{area}] Turkish letters in the English text: {tr}  →  {en}");
                // the plural syntax of a template must be well-formed
                Assert.Equal(en.Count(c => c == '{'), en.Count(c => c == '}'));
            }
    }

    // ------------------------------------------------------------------ the code: every shown text has an English

    /// <summary>The key of each Loc.T / Loc.F call whose first argument is a string literal (or literals joined with +). Calls with something else as the first argument are returned separately.</summary>
    private static (List<(string File, int Line, string Key)> Keys, List<string> Bad) ReadCalls()
    {
        var keys = new List<(string, int, string)>();
        var bad = new List<string>();
        var call = new Regex(@"\bLoc\.(?:T|F)\(\s*", RegexOptions.CultureInvariant);
        foreach (var file in SourceFiles("*.cs").Where(f => !f.EndsWith("Loc.cs") && !f.Contains($"{Path.DirectorySeparatorChar}Localization{Path.DirectorySeparatorChar}")))
        {
            var text = File.ReadAllText(file);
            foreach (Match m in call.Matches(text))
            {
                var lineStart = text.LastIndexOf('\n', m.Index) + 1;
                if (text.AsSpan(lineStart, m.Index - lineStart).TrimStart().StartsWith("//")) continue;       // a comment
                var line = text.Take(m.Index).Count(c => c == '\n') + 1;
                var at = m.Index + m.Length;
                if (at < text.Length && text[at] is '$' ) { bad.Add($"{Path.GetFileName(file)}:{line} an interpolated string is passed to Loc: write Loc.F(\"... {{0}} ...\", value)"); continue; }
                var literal = ReadLiteral(text, ref at);
                if (literal == null) continue;                                                                   // not a literal: a text that comes from data (checked elsewhere)
                keys.Add((Path.GetFileName(file), line, literal));
            }
        }
        return (keys, bad);
    }

    /// <summary>"a" + "b" → ab (regular and verbatim strings). Returns null when the text at <paramref name="at"/> is not a string literal.</summary>
    private static string? ReadLiteral(string s, ref int at)
    {
        var sb = new StringBuilder();
        var any = false;
        while (true)
        {
            while (at < s.Length && char.IsWhiteSpace(s[at])) at++;
            if (at >= s.Length) break;
            if (s[at] == '"')
            {
                at++;
                while (at < s.Length && s[at] != '"')
                {
                    if (s[at] == '\\' && at + 1 < s.Length)
                    {
                        at++;
                        switch (s[at])
                        {
                            case 'n': sb.Append('\n'); break;
                            case 'r': sb.Append('\r'); break;
                            case 't': sb.Append('\t'); break;
                            case '0': sb.Append('\0'); break;
                            case 'u' when at + 4 < s.Length: sb.Append((char)Convert.ToInt32(s.Substring(at + 1, 4), 16)); at += 4; break;
                            default: sb.Append(s[at]); break;                    // \\ \" \'
                        }
                    }
                    else sb.Append(s[at]);
                    at++;
                }
                at++;
                any = true;
            }
            else if (s[at] == '@' && at + 1 < s.Length && s[at + 1] == '"')
            {
                at += 2;
                while (at < s.Length)
                {
                    if (s[at] == '"') { if (at + 1 < s.Length && s[at + 1] == '"') { sb.Append('"'); at += 2; continue; } break; }
                    sb.Append(s[at]); at++;
                }
                at++;
                any = true;
            }
            else return any ? sb.ToString() : null;
            var look = at;
            while (look < s.Length && char.IsWhiteSpace(s[look])) look++;
            if (look < s.Length && s[look] == '+') { at = look + 1; continue; }
            break;
        }
        return any ? sb.ToString() : null;
    }

    [Fact]
    public void Every_text_that_the_code_translates_has_an_English_entry()
    {
        var (keys, bad) = ReadCalls();
        Assert.Empty(bad);
        Assert.True(keys.Count > 0 || EnglishStrings.Map.Count == 0, "no Loc.T / Loc.F call was found: the reader of the source is broken");
        var missing = keys.Where(k => !EnglishStrings.Map.ContainsKey(k.Key)).Select(k => $"{k.File}:{k.Line}  {Shorten(k.Key)}").Distinct().ToList();
        Assert.True(missing.Count == 0, "No English for:\n" + string.Join("\n", missing));
    }

    [Fact]
    public void A_template_passed_to_Loc_F_has_the_values_its_English_needs()
    {
        var (keys, _) = ReadCalls();
        foreach (var (file, line, key) in keys)
            if (EnglishStrings.Map.TryGetValue(key, out var en))
            {
                var a = PlaceholderRx.Matches(key).Select(m => m.Groups[1].Value).Distinct().OrderBy(x => x);
                var b = PlaceholderRx.Matches(Loc.ExpandPlurals(en, new object[] { 2, 2, 2, 2, 2, 2 })).Select(m => m.Groups[1].Value).Distinct().OrderBy(x => x);
                Assert.True(a.SequenceEqual(b), $"{file}:{line}  {Shorten(key)}");
            }
    }

    /// <summary>The catalog is data (Turkish text in tables), so no Loc.T call shows it to the test: every text in it is checked here.</summary>
    [Fact]
    public void Every_text_of_the_catalog_has_an_English_entry()
    {
        var texts = new List<string>
        {
            Catalog.GroupApps, Catalog.GroupServices, Catalog.GroupSystem, Catalog.GroupNetwork,
        };
        foreach (var o in Catalog.Options)
        {
            texts.Add(o.Title); texts.Add(o.Group);
            if (o.Hint != null) texts.Add(o.Hint);
            if (o.Caution != null) texts.Add(o.Caution);
        }
        foreach (var c in Catalog.ProcessCategories) texts.Add(c.Title);
        foreach (var (title, plain) in Catalog.ServiceTexts.Values) { texts.Add(title); if (plain.Length > 0) texts.Add(plain); }
        var missing = texts.Where(t => !EnglishStrings.Map.ContainsKey(t)).Distinct().Select(Shorten).ToList();
        Assert.True(missing.Count == 0, "No English for these catalog texts:\n" + string.Join("\n", missing));
    }

    [Fact]
    public void An_English_hint_or_caution_stays_as_short_as_the_Turkish_one()
    {
        // the rows of the list have room for one short line (Turkish: hint 70, caution 90 characters): the English must fit the same room
        foreach (var o in Catalog.Options)
        {
            if (o.Hint != null && EnglishStrings.Map.TryGetValue(o.Hint, out var h)) Assert.True(h.Length <= 80, $"{o.Id}: English hint has {h.Length} characters");
            if (o.Caution != null && EnglishStrings.Map.TryGetValue(o.Caution, out var c)) Assert.True(c.Length <= 100, $"{o.Id}: English caution has {c.Length} characters");
        }
    }

    private static string Shorten(string s) => (s.Length > 110 ? s[..110] + "…" : s).Replace("\n", "\\n");

    // ------------------------------------------------------------------ the XAML files: every fixed text goes through {local:T '...'} and has an English

    private static readonly string[] TextAttributes = { "Text", "Content", "ToolTip", "Header", "Title", "Tag", "AutomationProperties.Name", "AutomationProperties.HelpText" };
    private static readonly Regex StringFormatText = new(@"StringFormat\s*=\s*'?([^',}]*)", RegexOptions.CultureInvariant);
    private static readonly Regex TExt = new(@"^\{\s*\w+:T\s+'((?:[^'\\]|\\.)*)'\s*\}$", RegexOptions.CultureInvariant | RegexOptions.Singleline);

    private static string Collapse(string s) => Regex.Replace(s, @"\s+", " ").Trim();
    private static string Unescape(string s) => Regex.Replace(s, @"\\(.)", "$1", RegexOptions.Singleline);

    private static IEnumerable<string> XamlFiles() =>
        SourceFiles("*.xaml").Where(f => !f.Contains($"{Path.DirectorySeparatorChar}Theme{Path.DirectorySeparatorChar}"));

    private static bool IsXmlBookkeeping(XAttribute a) =>
        a.Name.Namespace == XNamespace.Xmlns || a.Name.NamespaceName == "http://www.w3.org/XML/1998/namespace";

    /// <summary>Every fixed text of the XAML files: the key of a {local:T '...'} and, as a mistake, a text written straight into a text property or between tags.</summary>
    private static IEnumerable<(string File, string Where, string Text, bool ViaT)> XamlTexts()
    {
        foreach (var file in XamlFiles())
            foreach (var e in XDocument.Load(file).Descendants())
            {
                foreach (var a in e.Attributes().Where(a => !IsXmlBookkeeping(a) && TextAttributes.Contains(a.Name.LocalName)))
                {
                    var v = a.Value.Trim();
                    var m = TExt.Match(v);
                    var where = $"<{e.Name.LocalName} {a.Name.LocalName}>";
                    if (m.Success) yield return (Path.GetFileName(file), where, Unescape(m.Groups[1].Value), true);
                    else if (!v.StartsWith("{") && v.Any(char.IsLetter)) yield return (Path.GetFileName(file), where, Collapse(v), false);
                }
                foreach (var t in e.Nodes().OfType<XText>())
                {
                    var v = Collapse(t.Value);
                    if (v.Length > 0 && v.Any(char.IsLetter) && !v.StartsWith("{")) yield return (Path.GetFileName(file), $"<{e.Name.LocalName}> text", v, false);
                }
            }
    }

    [Fact]
    public void Every_fixed_text_of_a_XAML_file_is_written_as_local_T()
    {
        var all = XamlTexts().ToList();
        Assert.True(all.Count > 20, "no texts were found in the XAML files: the reader is broken");
        var straight = all.Where(t => !t.ViaT).Select(t => $"{t.File} {t.Where}  {Shorten(t.Text)}").Distinct().ToList();
        Assert.True(straight.Count == 0, "A text written straight into XAML stays Turkish in English. Write it as {local:T '...'} (a quote inside as \\'):\n" + string.Join("\n", straight));
    }

    [Fact]
    public void Every_fixed_text_of_a_XAML_file_has_an_English_entry()
    {
        var missing = XamlTexts().Where(t => t.ViaT && !EnglishStrings.Map.ContainsKey(t.Text)).Select(t => $"{t.File} {t.Where}  {Shorten(t.Text)}").Distinct().ToList();
        Assert.True(missing.Count == 0, "No English for:\n" + string.Join("\n", missing));
    }

    [Fact]
    public void A_XAML_file_has_no_Turkish_sentence_in_an_attribute_that_is_neither_T_nor_bound()
    {
        var offenders = new List<string>();
        foreach (var file in XamlFiles())
            foreach (var e in XDocument.Load(file).Descendants())
                foreach (var a in e.Attributes().Where(a => !IsXmlBookkeeping(a)))
                {
                    if (TExt.IsMatch(a.Value.Trim())) continue;
                    var wordsInFormat = StringFormatText.Matches(a.Value).Any(m => Regex.Replace(m.Groups[1].Value, @"\{[^{}]*\}", "").Any(char.IsLetter));
                    if (TurkishLetter.IsMatch(a.Value) || wordsInFormat) offenders.Add($"{Path.GetFileName(file)} <{e.Name.LocalName} {a.Name.LocalName}=\"{Shorten(a.Value)}\">");
                }
        Assert.True(offenders.Count == 0, "Turkish text where nothing translates it (use {local:T '...'} or give the text to a view-model property):\n" + string.Join("\n", offenders));
    }
}
/// <summary>The language is one global setting: the tests that change it must not run next to each other.</summary>
[CollectionDefinition("language", DisableParallelization = true)]
public class LanguageCollection { }
