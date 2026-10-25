using CS2Hazirla.Core;
using Xunit;

namespace CS2Hazirla.Tests;

/// <summary>settings.json and state.json: safe writes, nothing silently thrown away, and files from older versions.</summary>
[Collection("Sequential")]
public class StorageTests
{
    // ------------------------------------------------------------ writing

    [Fact]
    public void A_save_replaces_the_file_completely_and_leaves_no_temp_file()
    {
        using var env = new TestEnv(ProfileKind.Safe);
        var store = new SettingsStore();
        var s = new AppSettings();
        s.NeverClose.Add("first");
        store.Save(s);
        s.NeverClose.Clear(); s.NeverClose.Add("second");
        store.Save(s);

        Assert.Equal(new[] { "second" }, store.Load().NeverClose);
        Assert.False(File.Exists(AppPaths.SettingsFile + ".tmp"));
        Assert.False(File.ReadAllBytes(AppPaths.SettingsFile).Take(3).SequenceEqual(new byte[] { 0xEF, 0xBB, 0xBF }));   // plain UTF-8, no BOM
    }

    [Fact]
    public async Task A_save_waits_a_moment_when_the_file_is_held_by_another_program()
    {
        using var env = new TestEnv(ProfileKind.Safe);
        var store = new SettingsStore();
        store.Save(new AppSettings());
        var s = new AppSettings();
        s.NeverClose.Add("kept");

        var holder = new FileStream(AppPaths.SettingsFile, FileMode.Open, FileAccess.Read, FileShare.None);   // e.g. a virus scanner
        var release = Task.Run(async () => { await Task.Delay(30); holder.Dispose(); });
        store.Save(s);                                                       // first swap fails, the retry succeeds
        await release;

        Assert.Contains("kept", store.Load().NeverClose);
    }

    [Fact]
    public void A_save_gives_up_after_three_tries_and_the_old_file_is_untouched()
    {
        using var env = new TestEnv(ProfileKind.Safe);
        var store = new SettingsStore();
        var old = new AppSettings();
        old.NeverClose.Add("old");
        store.Save(old);
        var s = new AppSettings();
        s.NeverClose.Add("new");

        using (new FileStream(AppPaths.SettingsFile, FileMode.Open, FileAccess.Read, FileShare.None))
            Assert.ThrowsAny<IOException>(() => store.Save(s));

        Assert.Equal(new[] { "old" }, store.Load().NeverClose);
    }

    // ------------------------------------------------------------ a settings file that cannot be read

    [Fact]
    public void A_broken_settings_file_is_kept_as_a_copy_defaults_are_used_and_the_fact_is_reported()
    {
        using var env = new TestEnv(ProfileKind.Safe);
        const string broken = "{ \"NeverClose\": [\"keepme\", ";
        File.WriteAllText(AppPaths.SettingsFile, broken);
        var store = new SettingsStore();

        var s = store.Load();

        Assert.Empty(s.NeverClose);                                           // defaults
        Assert.NotEmpty(s.Services);
        Assert.True(s.IsOn("proc.cloud"));
        var copy = Assert.Single(Directory.GetFiles(AppPaths.Root, "settings.bozuk-*.json"));
        Assert.Matches(@"settings\.bozuk-\d{8}-\d{6}\.json$", copy);
        Assert.Equal(broken, File.ReadAllText(copy));                          // nothing was lost
        Assert.True(File.Exists(AppPaths.SettingsFile));                       // and nothing was deleted
        Assert.Equal($"Ayarlar okunamadı, varsayılanlar kullanıldı; eski dosya {Path.GetFileName(copy)} olarak saklandı.", store.LastLoadWarning);
    }

    [Fact]
    public void A_settings_file_that_says_null_counts_as_broken_too()
    {
        using var env = new TestEnv(ProfileKind.Safe);
        File.WriteAllText(AppPaths.SettingsFile, "null");
        var store = new SettingsStore();
        store.Load();
        Assert.NotNull(store.LastLoadWarning);
        Assert.Single(Directory.GetFiles(AppPaths.Root, "settings.bozuk-*.json"));
    }

    [Fact]
    public void A_good_or_missing_settings_file_gives_no_warning_and_no_copy()
    {
        using var env = new TestEnv(ProfileKind.Safe);
        var store = new SettingsStore();
        store.Load();
        Assert.Null(store.LastLoadWarning);                                    // no file yet: a first start, not a problem

        store.Save(new AppSettings());
        store.Load();
        Assert.Null(store.LastLoadWarning);
        Assert.Empty(Directory.GetFiles(AppPaths.Root, "settings.bozuk-*.json"));
    }

    [Fact]
    public void The_warning_goes_away_when_the_next_load_is_fine()
    {
        using var env = new TestEnv(ProfileKind.Safe);
        File.WriteAllText(AppPaths.SettingsFile, "{ broken");
        var store = new SettingsStore();
        store.Load();
        Assert.NotNull(store.LastLoadWarning);
        store.Save(new AppSettings());
        store.Load();
        Assert.Null(store.LastLoadWarning);
    }

    // ------------------------------------------------------------ state.json is never lost

    [Fact]
    public void A_journal_that_cannot_be_moved_aside_is_copied_and_never_deleted_without_a_copy()
    {
        using var env = new TestEnv(ProfileKind.Safe);
        const string text = "{ \"Profile\": \"x\" }";
        File.WriteAllText(AppPaths.StateFile, text);

        using (new FileStream(AppPaths.StateFile, FileMode.Open, FileAccess.Read, FileShare.Read))    // held: it can be read but not moved or deleted
            new SessionStore().Quarantine("corrupt");
        var copy = Assert.Single(Directory.GetFiles(AppPaths.Root, "state.corrupt-*.json"));
        Assert.Equal(text, File.ReadAllText(copy));
        Assert.True(File.Exists(AppPaths.StateFile));                          // still there: it could not be removed, and that is fine

        File.Delete(copy);
        using (new FileStream(AppPaths.StateFile, FileMode.Open, FileAccess.Read, FileShare.None))    // held completely: not even a copy is possible
            new SessionStore().Quarantine("corrupt");
        Assert.Empty(Directory.GetFiles(AppPaths.Root, "state.corrupt-*.json"));
        Assert.Equal(text, File.ReadAllText(AppPaths.StateFile));              // so the original must be untouched
    }

    // ------------------------------------------------------------ files written by older versions

    [Fact]
    public void Old_settings_files_with_removed_fields_still_load_and_keep_the_users_lists()
    {
        using var env = new TestEnv(ProfileKind.Safe);
        File.WriteAllText(AppPaths.SettingsFile,
            "{\"LaunchOptions\": \"-novid\", \"ExitAfterLaunch\": true, \"Cs2PathOverride\": \"D:\\\\x\", \"NeverStopServices\": [\"spooler\"], \"ApprovedSweep\": [\"oldapp\"]," +
            " \"StandbyThresholdGb\": 4.0, \"DetailedMode\": true, \"PingTarget\": \"8.8.8.8\", \"RestartClosedApps\": false," +
            " \"NeverClose\": [\"keepme\"], \"ExtraProcesses\": [\"closeme\"]}");
        var store = new SettingsStore();

        var s = store.Load();

        Assert.Null(store.LastLoadWarning);                                    // unknown members are ignored, the file is not "broken"
        Assert.Equal(new[] { "keepme" }, s.NeverClose);
        Assert.Equal(new[] { "closeme" }, s.ExtraProcesses);
        Assert.Equal("8.8.8.8", s.PingTarget);
        Assert.False(s.RestartClosedApps);
        Assert.Empty(Directory.GetFiles(AppPaths.Root, "settings.bozuk-*.json"));
    }

    [Fact]
    public void Saved_profiles_with_removed_option_ids_still_load_and_the_removed_ids_are_ignored()
    {
        using var env = new TestEnv(ProfileKind.Maximum);
        File.WriteAllText(AppPaths.SettingsFile,
            "{\"SavedProfiles\": [{\"Id\": \"p1\", \"Name\": \"Eski\", \"Base\": \"Maximum\", \"Options\": " +
            "{\"proc.cloud\": false, \"proc.tray\": true, \"proc.trayauto\": false, \"net.nagle\": true, \"mem.standby\": true, \"misc.temp\": true}}]}");
        var settings = new SettingsStore().Load();
        var session = new ProfileSession(settings);

        Assert.True(session.Activate(ProfileSession.KeyOfCustom("p1")));

        Assert.False(settings.IsOn("proc.cloud"));                             // what is still known is kept
        Assert.False(settings.IsOn("proc.trayauto"));
        Assert.All(settings.Options.Keys, k => Assert.NotNull(Catalog.Find(k)));   // the removed ids did not come along
        Assert.Equal(Catalog.Options.Count, settings.Options.Count);
        // the old "proc.tray" = true has no effect: with the one background switch off the plan has no unknown background program
        Assert.DoesNotContain(new Planner(env.Sys).Build(settings, TestEnv.Cs2Path).Actions, a => a.Id == "proc:randomtraything");

        // saving the profile again drops the removed ids from the file
        var p = Assert.Single(settings.SavedProfiles);
        session.Save(p.Id, p.Name, p.Base, settings.Options);
        Assert.DoesNotContain(p.Options.Keys, k => k is "proc.tray" or "net.nagle" or "mem.standby" or "misc.temp");
    }

    [Fact]
    public void The_one_background_switch_is_saved_with_the_profile()
    {
        var settings = new AppSettings();
        var session = new ProfileSession(settings);
        var p = session.Create();
        Assert.True(p.Options["proc.trayauto"]);
        Assert.False(p.Options.ContainsKey("proc.tray"));
    }
}
