using CS2Hazirla.Core;
using Xunit;

namespace CS2Hazirla.Tests;

/// <summary>Tam temizlik ticks every switch; Discord and the block-bypass tools are two switches; profiles saved with the old merged switch keep their meaning.</summary>
[Collection("Sequential")]
public class SplitSwitchTests
{
    [Fact]
    public void Tam_temizlik_opens_with_every_single_switch_ticked()
    {
        foreach (var o in Catalog.Options)
            Assert.True(Catalog.ProfileDefault(ProfileKind.Maximum, o.Id), $"{o.Id} is not ticked in Tam temizlik");

        var s = new AppSettings();
        s.ApplyProfile(ProfileKind.Maximum);
        Assert.All(Catalog.Options, o => Assert.True(s.IsOn(o.Id), o.Id));

        Assert.All(ProfileSession.PresetDefaults(ProfileKind.Maximum), kv => Assert.True(kv.Value, kv.Key));

        var fresh = new AppSettings();
        var session = new ProfileSession(fresh);                                  // a fresh start is Tam temizlik, with every switch ticked
        Assert.Equal(ProfileSession.MaximumKey, session.ActiveKey);
        Assert.All(Catalog.Options, o => Assert.True(fresh.IsOn(o.Id), o.Id));
        Assert.False(session.IsCustomized);
    }

    [Fact]
    public void Yayinci_is_Tam_temizlik_without_the_tools_a_streamer_keeps_open()
    {
        var off = Catalog.Options.Where(o => !Catalog.ProfileDefault(ProfileKind.Stream, o.Id)).Select(o => o.Id).OrderBy(x => x).ToArray();
        Assert.Equal(new[] { "proc.browsers", "proc.bypass", "proc.discord", "proc.dpi", "proc.trayauto", "proc.vpn" }, off);
    }

    [Fact]
    public void Hafif_closes_no_program()
    {
        Assert.DoesNotContain(Catalog.Options, o => o.Id.StartsWith("proc.") && Catalog.ProfileDefault(ProfileKind.Safe, o.Id));
    }

    [Fact]
    public void Hafif_is_exactly_the_seven_harmless_switches_it_always_was()
    {
        var on = Catalog.Options.Where(o => Catalog.ProfileDefault(ProfileKind.Safe, o.Id)).Select(o => o.Id).OrderBy(x => x, StringComparer.Ordinal).ToArray();
        Assert.Equal(new[] { "net.flushdns", "pwr.plan", "svc.update", "sys.gamedvr", "sys.gamemode", "sys.gpupref", "sys.hotkeys" }, on);
    }

    [Fact]
    public void A_profile_saved_with_the_old_merged_switch_keeps_Discord_and_the_bypass_tools_together()
    {
        // saved while Discord followed proc.dpi: the user had both OFF (kept open) ...
        var bothOff = ProfileSession.Complete(new Dictionary<string, bool> { ["proc.dpi"] = false, ["proc.browsers"] = true }, ProfileKind.Maximum);
        Assert.False(bothOff["proc.dpi"]);
        Assert.False(bothOff["proc.discord"]);          // ... so Discord must not suddenly be closed
        Assert.True(bothOff["proc.browsers"]);

        var bothOn = ProfileSession.Complete(new Dictionary<string, bool> { ["proc.dpi"] = true }, ProfileKind.Stream);
        Assert.True(bothOn["proc.discord"]);            // the saved value wins over the preset the profile started from

        // a profile that already knows the new switch is left as it is
        var split = ProfileSession.Complete(new Dictionary<string, bool> { ["proc.dpi"] = true, ["proc.discord"] = false }, ProfileKind.Maximum);
        Assert.True(split["proc.dpi"]);
        Assert.False(split["proc.discord"]);
    }

    [Fact]
    public void A_settings_file_of_the_previous_version_loads_and_its_profile_runs_as_before()
    {
        using var env = new TestEnv(ProfileKind.Maximum);
        File.WriteAllText(AppPaths.SettingsFile,
            "{\"SavedProfiles\": [{\"Id\": \"a\", \"Name\": \"Maç\", \"Base\": \"Maximum\", \"Options\": {\"proc.dpi\": false, \"proc.browsers\": false}}]}");

        var settings = new SettingsStore().Load();
        var session = new ProfileSession(settings);
        Assert.True(session.Activate(ProfileSession.KeyOfCustom("a")));

        env.Sys.AddProc("goodbyedpi", @"C:\Tools\GoodbyeDPI\goodbyedpi.exe");
        var plan = new Planner(env.Sys).Build(settings, TestEnv.Cs2Path);
        Assert.DoesNotContain(plan.Actions, a => a.Id is "proc:discord" or "proc:goodbyedpi" or "proc:chrome");   // the profile kept all three open
        Assert.Contains(plan.Active, a => a.Id == "proc:onedrive");                                               // while the plan itself is not empty
    }

    [Fact]
    public void A_profile_saved_by_3_0_with_the_two_settings_off_does_not_get_them_switched_on()
    {
        // 3.0 wrote every switch of a saved profile; the new Tam temizlik default must not leak into it
        using var env = new TestEnv(ProfileKind.Maximum);
        File.WriteAllText(AppPaths.SettingsFile,
            "{\"SavedProfiles\": [{\"Id\": \"a\", \"Name\": \"Maç\", \"Base\": \"Maximum\", \"Options\": {\"proc.dpi\": true, \"sys.fso\": false, \"sys.notifications\": false}}]}");

        var settings = new SettingsStore().Load();
        var session = new ProfileSession(settings);
        Assert.True(session.Activate(ProfileSession.KeyOfCustom("a")));

        Assert.False(settings.IsOn("sys.fso"));
        Assert.False(settings.IsOn("sys.notifications"));
        Assert.True(settings.IsOn("proc.discord"));        // the old merged switch was on, so Discord keeps being closed
        Assert.True(settings.IsOn("proc.dpi"));
    }
}
