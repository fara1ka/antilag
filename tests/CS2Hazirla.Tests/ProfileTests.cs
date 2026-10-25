using System.Text.Json;
using CS2Hazirla.Core;
using Xunit;

namespace CS2Hazirla.Tests;

/// <summary>The three presets, the user's saved profiles and what survives a restart.</summary>
[Collection("Sequential")]
public class ProfileTests
{
    private static Dictionary<string, bool> Off() => Catalog.Options.ToDictionary(o => o.Id, _ => false);

    private static void AssertDefaults(AppSettings s, ProfileKind kind)
    {
        foreach (var o in Catalog.Options)
            Assert.Equal(Catalog.ProfileDefault(kind, o.Id), s.IsOn(o.Id));
        Assert.Equal(kind, s.Profile);
    }

    // ------------------------------------------------------------ every start is a fresh start

    [Fact]
    public void Every_start_begins_with_the_defaults_of_Tam_temizlik_whatever_an_old_settings_file_says()
    {
        using var env = new TestEnv(ProfileKind.Safe);
        File.WriteAllText(AppPaths.SettingsFile,
            "{\"Profile\": \"Safe\", \"Options\": {\"proc.browsers\": false, \"svc.other\": false}, \"NeverClose\": [\"keepme\"]}");

        var settings = new SettingsStore().Load();
        var session = new ProfileSession(settings);

        Assert.Equal(ProfileSession.MaximumKey, session.ActiveKey);
        AssertDefaults(settings, ProfileKind.Maximum);
        Assert.Contains("keepme", settings.NeverClose);                         // the lists are the user's settings: they stay
    }

    [Fact]
    public void The_active_preset_and_its_switches_are_not_written_to_the_settings_file_but_saved_profiles_are()
    {
        using var env = new TestEnv(ProfileKind.Safe);
        var settings = new AppSettings();
        var session = new ProfileSession(settings);
        settings.Options["proc.browsers"] = false;                              // a change made in this run
        var saved = session.Create(ProfileKind.Stream);
        session.Save(saved.Id, "Maç günü", ProfileKind.Stream, Off());
        new SettingsStore().Save(settings);

        using (var doc = JsonDocument.Parse(File.ReadAllText(AppPaths.SettingsFile)))
        {
            Assert.False(doc.RootElement.TryGetProperty("Options", out _));     // the run's switches are not in the file
            Assert.False(doc.RootElement.TryGetProperty("Profile", out _));
            Assert.True(doc.RootElement.TryGetProperty("SavedProfiles", out _));
        }
        var back = new SettingsStore().Load();
        Assert.Empty(back.Options);                                             // nothing of the run was kept...
        var p = Assert.Single(back.SavedProfiles);                              // ...but the profile was
        Assert.Equal("Maç günü", p.Name);
        Assert.Equal(ProfileKind.Stream, p.Base);
        Assert.All(p.Options.Values, v => Assert.False(v));
        Assert.Equal(Catalog.Options.Count, p.Options.Count);
    }

    // ------------------------------------------------------------ the three presets

    [Fact]
    public void Switching_presets_asks_nothing_and_each_one_keeps_its_own_changes_for_this_run()
    {
        var settings = new AppSettings();
        var session = new ProfileSession(settings);

        settings.Options["proc.cloud"] = false;                                 // changed in Tam temizlik (default: on)
        Assert.True(session.IsCustomized);

        Assert.True(session.Activate(ProfileKind.Safe));
        AssertDefaults(settings, ProfileKind.Safe);                             // Hafif opens with ITS defaults, not with the change
        Assert.False(session.IsCustomized);
        settings.Options["svc.other"] = true;                                   // changed in Hafif (default: off)

        Assert.True(session.Activate(ProfileKind.Stream));
        AssertDefaults(settings, ProfileKind.Stream);                           // an untouched preset is always at its defaults
        Assert.True(settings.IsOn("proc.cloud"));

        Assert.True(session.Activate(ProfileKind.Safe));
        Assert.True(settings.IsOn("svc.other"));                                // back in Hafif: the change is still there
        Assert.True(session.IsCustomized);

        Assert.True(session.Activate(ProfileKind.Maximum));
        Assert.False(settings.IsOn("proc.cloud"));                              // ...and so is the one in Tam temizlik
        Assert.Equal(ProfileKind.Maximum, settings.Profile);
    }

    [Fact]
    public void A_preset_that_was_only_visited_is_still_at_its_defaults_the_next_time()
    {
        var settings = new AppSettings();
        var session = new ProfileSession(settings);
        session.Activate(ProfileKind.Safe);
        session.Activate(ProfileKind.Stream);
        session.Activate(ProfileKind.Safe);
        AssertDefaults(settings, ProfileKind.Safe);
    }

    [Fact]
    public void An_unknown_profile_is_refused_and_nothing_changes()
    {
        var settings = new AppSettings();
        var session = new ProfileSession(settings);
        settings.Options["proc.cloud"] = false;
        Assert.False(session.Activate(ProfileSession.KeyOfCustom("does-not-exist")));
        Assert.Equal(ProfileSession.MaximumKey, session.ActiveKey);
        Assert.False(settings.IsOn("proc.cloud"));
    }

    // ------------------------------------------------------------ the user's own profiles

    [Fact]
    public void A_saved_profile_loads_exactly_what_was_saved_and_keeps_stream_protection_of_its_base()
    {
        var settings = new AppSettings();
        var session = new ProfileSession(settings);
        var p = session.Create(ProfileKind.Stream);
        var options = Off();
        options["proc.browsers"] = true;
        session.Save(p.Id, "Sadece tarayıcı", ProfileKind.Stream, options);

        Assert.True(session.Activate(ProfileSession.KeyOfCustom(p.Id)));

        Assert.Equal("Sadece tarayıcı", session.ActiveName);
        Assert.Equal(ProfileKind.Stream, settings.Profile);                     // OBS/Discord stay protected like in Yayıncı
        Assert.True(settings.IsOn("proc.browsers"));
        Assert.False(settings.IsOn("proc.cloud"));
        Assert.False(session.IsCustomized);
    }

    [Fact]
    public void Changes_made_to_a_profile_in_use_are_for_this_run_only_and_come_back_when_returning_to_it()
    {
        var settings = new AppSettings();
        var session = new ProfileSession(settings);
        var p = session.Create();
        session.Save(p.Id, "Benim", ProfileKind.Maximum, Off());
        var key = ProfileSession.KeyOfCustom(p.Id);

        session.Activate(key);
        settings.Options["proc.cloud"] = true;                                   // this run only
        session.Activate(ProfileKind.Safe);
        session.Activate(key);
        Assert.True(settings.IsOn("proc.cloud"));                                // still there after a detour
        Assert.False(Assert.Single(settings.SavedProfiles).Options["proc.cloud"]);   // but the saved profile itself is unchanged
    }

    [Fact]
    public void Saving_the_profile_in_use_makes_it_run_with_exactly_what_was_saved()
    {
        var settings = new AppSettings();
        var session = new ProfileSession(settings);
        var p = session.Create();
        var key = ProfileSession.KeyOfCustom(p.Id);
        session.Activate(key);
        settings.Options["proc.cloud"] = false;                                  // an edit of this run

        var options = Off();
        options["proc.vpn"] = true;
        session.Save(p.Id, "Yeni ad", ProfileKind.Maximum, options);

        Assert.Equal("Yeni ad", session.ActiveName);
        Assert.True(settings.IsOn("proc.vpn"));
        Assert.False(settings.IsOn("proc.cloud"));
        Assert.False(session.IsCustomized);
    }

    [Fact]
    public void Deleting_the_profile_in_use_returns_to_Tam_temizlik()
    {
        var settings = new AppSettings();
        var session = new ProfileSession(settings);
        var p = session.Create(ProfileKind.Safe);
        session.Activate(ProfileSession.KeyOfCustom(p.Id));
        session.Delete(p.Id);

        Assert.Empty(settings.SavedProfiles);
        Assert.Equal(ProfileSession.MaximumKey, session.ActiveKey);
        AssertDefaults(settings, ProfileKind.Maximum);
    }

    [Fact]
    public void Profile_names_are_never_empty_and_never_the_same_twice()
    {
        var settings = new AppSettings();
        var session = new ProfileSession(settings);
        var a = session.Create();
        var b = session.Create();
        Assert.NotEqual(a.Name, b.Name);
        session.Save(a.Id, "  ", ProfileKind.Maximum, Off());
        Assert.False(string.IsNullOrWhiteSpace(a.Name));
        session.Save(b.Id, a.Name, ProfileKind.Maximum, Off());                 // same name as another profile
        Assert.NotEqual(a.Name, b.Name);
        session.Save(a.Id, new string('x', 100), ProfileKind.Maximum, Off());
        Assert.True(a.Name.Length <= 40);
    }

    [Fact]
    public void Options_added_after_a_profile_was_saved_take_the_default_of_the_preset_it_started_from()
    {
        var settings = new AppSettings();
        var session = new ProfileSession(settings);
        // saved from Tam temizlik before "proc.claude" existed: the file only knows proc.cloud (switched OFF by the user)
        var old = new SavedProfile { Name = "Eski", Base = ProfileKind.Maximum, Options = new Dictionary<string, bool> { ["proc.cloud"] = false } };
        settings.SavedProfiles.Add(old);
        Assert.True(Catalog.ProfileDefault(ProfileKind.Maximum, "proc.claude"));   // (so "true" below can only come from the preset default)
        Assert.True(Catalog.ProfileDefault(ProfileKind.Maximum, "proc.cloud"));

        session.Activate(ProfileSession.KeyOfCustom(old.Id));

        Assert.False(settings.IsOn("proc.cloud"));                               // what was saved wins over the default
        Assert.True(settings.IsOn("proc.claude"));                               // what was never saved takes the preset's default
        Assert.Equal(Catalog.Options.Count, settings.Options.Count);
    }

    [Fact]
    public void A_hand_edited_settings_file_with_broken_profiles_still_loads()
    {
        using var env = new TestEnv(ProfileKind.Safe);
        File.WriteAllText(AppPaths.SettingsFile,
            "{\"SavedProfiles\": [null, {\"Id\": \"a\", \"Name\": \"\", \"Options\": null}, {\"Id\": \"a\", \"Name\": \"İkinci\"}]}");

        var s = new SettingsStore().Load();

        Assert.Equal(2, s.SavedProfiles.Count);
        Assert.Equal(2, s.SavedProfiles.Select(p => p.Id).Distinct().Count());   // the duplicate id was replaced
        Assert.All(s.SavedProfiles, p => { Assert.NotNull(p.Options); Assert.False(string.IsNullOrWhiteSpace(p.Name)); });
    }

    // ------------------------------------------------------------ pressing Clear with a saved profile

    [Fact]
    public async Task Clear_runs_with_the_switches_of_the_chosen_profile()
    {
        using var env = new TestEnv(ProfileKind.Maximum);
        var session = new ProfileSession(env.Settings);
        var p = session.Create();
        var options = Off();
        options["proc.cloud"] = true;                                            // only the backup programs
        session.Save(p.Id, "Sadece yedekleme", ProfileKind.Maximum, options);
        session.Activate(ProfileSession.KeyOfCustom(p.Id));

        var plan = env.BuildPlan();
        var closing = plan.Active.Where(a => a.Kind == ActionKind.CloseProcess).Select(a => a.Id).ToList();
        Assert.Equal(new[] { "proc:dropbox", "proc:onedrive" }, closing.OrderBy(x => x).ToArray());
        Assert.DoesNotContain(plan.Active, a => a.Kind == ActionKind.StopService);   // every other switch of the profile is off

        await env.Orc.PrepareAsync(plan, env.Settings, CancellationToken.None);
        Assert.DoesNotContain(env.Sys.Procs, x => x.Name is "OneDrive" or "Dropbox");
        Assert.Contains(env.Sys.Procs, x => x.Name == "chrome");                  // not part of the profile: left alone
    }
}
