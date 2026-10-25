using System.Text.RegularExpressions;
using CS2Hazirla.Core;
using Xunit;

namespace CS2Hazirla.Tests;

/// <summary>The app is for people who know nothing about PCs: every option is a plain name, with a short hint only where the name alone is not enough.</summary>
public class PlainLanguageTests
{
    // Options that may carry a Hint (the name is not self-explanatory for a non-technical person). Everything else is a name only.
    private static readonly HashSet<string> MayHaveHint = new()
    {
        "proc.vpn", "proc.antivirus", "proc.anticheat", "proc.bypass", "proc.trayauto", "svc.other",
        "pwr.plan", "sys.gamemode", "sys.gamedvr", "sys.notifications", "sys.gpupref", "sys.fso", "sys.mouseaccel", "sys.hotkeys",
        "net.flushdns", "net.nicpower",
    };

    // The only options whose switching on can really cost the user something: they carry an amber Caution.
    private static readonly HashSet<string> HaveCaution = new()
    {
        "proc.windowed", "proc.claude", "proc.antivirus", "proc.anticheat", "proc.trayauto",
    };

    private static readonly Regex Jargon = new(@"\b(registry|dword|qword|tcp|udp|standby|hklm|hkcu|guid|pid|spi|wmi|powercfg|svchost|ntdll|nagle|latency|kernel|api)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    [Fact]
    public void Every_option_has_a_unique_id_and_a_name()
    {
        Assert.Equal(Catalog.Options.Count, Catalog.Options.Select(o => o.Id).Distinct().Count());
        foreach (var o in Catalog.Options)
        {
            Assert.False(string.IsNullOrWhiteSpace(o.Title), o.Id + " title");
            Assert.False(string.IsNullOrWhiteSpace(o.Group), o.Id + " group");
            Assert.True(o.Hint == null || o.Hint.Trim().Length > 0, o.Id + " hint is empty text instead of null");
            Assert.True(o.Caution == null || o.Caution.Trim().Length > 0, o.Id + " caution is empty text instead of null");
        }
    }

    [Fact]
    public void A_hint_is_one_short_line_and_exists_only_where_the_allow_list_says_so()
    {
        foreach (var o in Catalog.Options.Where(o => o.Hint != null))
        {
            Assert.Contains(o.Id, MayHaveHint);
            Assert.True(o.Hint!.Length <= 70, $"{o.Id}: hint has {o.Hint.Length} characters");
            Assert.DoesNotContain('\n', o.Hint);
        }
        foreach (var id in MayHaveHint) Assert.NotNull(Catalog.Find(id));   // the allow-list itself does not rot
    }

    [Fact]
    public void A_caution_exists_only_for_the_real_risks_and_is_one_short_sentence()
    {
        Assert.Equal(HaveCaution.OrderBy(x => x), Catalog.Options.Where(o => o.Caution != null).Select(o => o.Id).OrderBy(x => x));
        foreach (var o in Catalog.Options.Where(o => o.Caution != null))
        {
            Assert.True(o.Caution!.Length <= 90, $"{o.Id}: caution has {o.Caution.Length} characters");
            Assert.EndsWith(".", o.Caution);
            Assert.Equal(1, o.Caution.Count(c => c == '.'));              // one sentence
        }
        Assert.Equal("Kaydedilmemiş iş kaybolur.", Catalog.Find("proc.windowed")!.Caution);
        Assert.Equal("Açık Claude oturumu kapanır.", Catalog.Find("proc.claude")!.Caution);
        // Discord, GoodbyeDPI and the VPN switch say nothing beyond their name: no "takımla konuşacaksan kapalı tut" under every row
        foreach (var id in new[] { "proc.discord", "proc.dpi", "proc.vpn" }) { Assert.Null(Catalog.Find(id)!.Caution); }
        Assert.Null(Catalog.Find("proc.dpi")!.Hint);
    }

    [Fact]
    public void Programs_that_run_as_a_windows_service_do_not_promise_more_than_the_window_and_the_icon()
    {
        foreach (var id in new[] { "proc.vpn", "proc.antivirus", "proc.anticheat", "proc.bypass" })
        {
            var o = Catalog.Find(id)!;
            Assert.Contains("hizmet olarak çalışan kısmı açık kalır", o.Hint);        // said once, briefly, in the hint
            Assert.DoesNotContain("kapanır", o.Title);                                // the name promises nothing
            Assert.DoesNotContain("kopar", o.Caution ?? "");
        }
    }

    [Fact]
    public void No_technical_jargon_in_titles_hints_or_cautions()
    {
        foreach (var o in Catalog.Options)
            foreach (var text in new[] { o.Title, o.Hint, o.Caution }.Where(t => t != null))
                Assert.False(Jargon.IsMatch(text!), $"{o.Id}: \"{text}\" has a technical word");
    }

    [Fact]
    public void The_removed_options_are_gone_from_the_catalog_and_from_every_preset()
    {
        foreach (var id in new[] { "net.nagle", "mem.standby", "misc.temp", "proc.tray" })
        {
            Assert.Null(Catalog.Find(id));
            foreach (var p in Enum.GetValues<ProfileKind>()) Assert.False(Catalog.ProfileDefault(p, id), $"{id}/{p}");
        }
        Assert.Equal(4, Catalog.Options.Select(o => o.Group).Distinct().Count());     // programs, services, Windows settings, internet: no "doubtful" group
    }

    [Fact]
    public void Discord_and_GoodbyeDPI_are_two_switches_side_by_side_at_the_top_of_the_list_each_for_just_that_program()
    {
        var first = Catalog.Options.Take(2).Select(o => o.Id).ToArray();
        Assert.Equal(new[] { "proc.discord", "proc.dpi" }, first);
        Assert.Equal("Discord", Catalog.Find("proc.discord")!.Title);
        Assert.Equal("GoodbyeDPI", Catalog.Find("proc.dpi")!.Title);                    // not "...ve engel aşma programları": the other tools have their own switch
        Assert.StartsWith("Diğer engel aşma programları", Catalog.Find("proc.bypass")!.Title);
        Assert.Equal("Tanınmayan arka plan programları", Catalog.Find("proc.trayauto")!.Title);
    }

    [Fact]
    public void Texts_use_one_vocabulary()
    {
        var all = Catalog.Options.SelectMany(o => new[] { o.Title, o.Hint, o.Caution, o.Group }).Where(t => t != null).Cast<string>()
            .Concat(Catalog.ServiceTexts.Values.SelectMany(v => new[] { v.Title, v.Plain })).ToList();
        foreach (var text in all)
            foreach (var banned in new[] { "Clear", "Hazırla", "Sade", "Detaylı", "geri al", "Geri al", "eski haline döndür", "Hep açık", "sonlandır" })
                Assert.DoesNotContain(banned, text);
    }

    [Fact]
    public void Every_default_service_has_a_plain_name_and_description()
    {
        foreach (var r in Catalog.DefaultServices())
        {
            Assert.NotEqual(r.Name, Catalog.ServiceTitle(r.Name));
            Assert.False(string.IsNullOrWhiteSpace(Catalog.ServicePlain(r.Name)), r.Name);
        }
        Assert.Equal("Bilinmeyen Hizmet", Catalog.ServiceTitle("SomeUnknownSvc", "Bilinmeyen Hizmet"));
    }

    [Fact]
    public void Settings_that_survive_closing_the_app_are_not_called_temporary()
    {
        // power plan / registry / NIC flags stay until "Geri yükle": the UI must not promise they are temporary
        Assert.DoesNotContain("geçici", Catalog.GroupSystem, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void The_app_itself_is_never_closable_under_its_new_or_old_exe_name()
    {
        Assert.True(ProtectedList.IsProcessProtected("Antilag", null, false));
        Assert.True(ProtectedList.IsProcessProtected("Antilag.exe", null, false));
        Assert.True(ProtectedList.IsProcessProtected("Clear", null, false));
        Assert.True(ProtectedList.IsProcessProtected("Clear.exe", null, false));
        Assert.True(ProtectedList.IsProcessProtected("CS2Hazirla", null, false));
    }

    [Fact]
    public void Metric_labels_use_everyday_words()
    {
        var snap = new MetricsSnapshot(DateTime.UtcNow, 100, 8L << 30, 2L << 30, 90, new PingStats("1.1.1.1", 20, 20, 15, 1, 0));
        var (rows, _) = Metrics.Compare(snap, snap);
        Assert.All(rows, r =>
        {
            Assert.DoesNotContain("CPU", r.Label);
            Assert.DoesNotContain("Standby", r.Label);
            Assert.DoesNotContain("işlem sayısı", r.Label);
        });
    }

    [Fact]
    public void Friendly_names_fall_back_to_a_readable_name_and_format_sizes()
    {
        Assert.Equal("Spotify", FriendlyNames.ForProcess("spotify", null));
        Assert.Equal("Spotify", FriendlyNames.ForProcess("spotify", @"Z:\does\not\exist.exe"));
        Assert.Equal("150 MB", FriendlyNames.Memory(150L << 20));
    }
}

/// <summary>Everything a whole run, a quiet restore and a real restore put in front of the user, checked against the agreed vocabulary.</summary>
[Collection("Sequential")]
public class RunVocabularyTests
{
    private static readonly Regex Banned = new(
        @"Clear|Hazırla|\bSade\b|Detaylı|geri al|Geri al|eski haline döndür|Hep açık|sonlandır|yeniden açıl|Her şey eski haline", RegexOptions.CultureInvariant);

    [Fact]
    public async Task Plan_results_and_log_lines_use_Antilag_Baslat_Geri_yukle_and_kapat()
    {
        using var env = new TestEnv(ProfileKind.Maximum);
        env.Settings.Options = Catalog.Options.ToDictionary(o => o.Id, _ => true);
        env.Sys.AddProc("goodbyedpi", @"C:\Tools\GoodbyeDPI\goodbyedpi.exe");
        env.Sys.Procs.RemoveAll(p => p.Name == "OneDrive");                       // one program that is already gone: "artık çalışmıyor" paths
        var plan = env.BuildPlan();
        var res = await env.Orc.PrepareAsync(plan, env.Settings, CancellationToken.None);
        var orc2 = env.Restart();
        var prev = orc2.Revert(env.Settings, restartApps: false);                  // the quiet path
        await orc2.PrepareAsync(env.BuildPlan(), env.Settings, CancellationToken.None, prev.Carry);
        env.Sys.AddProc("cs2");                                                    // the refusals while the game runs
        var refused = orc2.Revert(env.Settings);
        orc2.CloseByUser("Dropbox", false);
        var pending = await Assert.ThrowsAsync<InvalidOperationException>(() => orc2.PrepareAsync(env.BuildPlan(), env.Settings, CancellationToken.None));
        env.Sys.Procs.RemoveAll(p => p.Name == "cs2");
        var fin = orc2.Revert(env.Settings);                                       // the real one (reopens, lists what cannot be reopened)
        env.Sys.AddProc("cs2");
        var refusal = await Assert.ThrowsAsync<InvalidOperationException>(() => orc2.PrepareAsync(env.BuildPlan(), env.Settings, CancellationToken.None));
        Assert.Contains("Antilag", refusal.Message);

        var texts = plan.Actions.SelectMany(a => new[] { a.Title, a.Detail, a.Group }).Concat(plan.Warnings).Concat(plan.Notes)
            .Concat(res.Done).Concat(res.Failed).Concat(res.Skipped).Concat(res.NotClosed)
            .Concat(prev.Restored).Concat(prev.Problems).Concat(fin.Restored).Concat(fin.Problems).Concat(fin.NotRestarted).Concat(refused.Problems).Concat(new[] { pending.Message, refusal.Message })
            .Concat(env.Log.Lines.Where(l => !l.StartsWith("D")).Select(l => l[2..])).ToList();
        Assert.True(texts.Count > 50);
        foreach (var t in texts) Assert.False(Banned.IsMatch(t), $"\"{t}\" uses a word the vocabulary retired");
        Assert.Contains(texts, t => t == "Önceki ayarlar eski haline döndü.");     // (the one fixed sentence of the quiet path)
        Assert.Contains(texts, t => t == "Ayarlar ve hizmetler eski haline döndü.");
    }
}
