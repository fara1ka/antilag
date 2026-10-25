using CS2Hazirla.Core;
using Xunit;

namespace CS2Hazirla.Tests;

/// <summary>Regression tests for the problems found by the independent reviews of the earlier redesigns.</summary>
[Collection("Sequential")]
public class ReviewFixTests
{
    [Fact]
    public void Hafif_really_closes_no_program()
    {
        foreach (var o in Catalog.Options.Where(o => o.Id.StartsWith("proc.")))
            Assert.False(Catalog.ProfileDefault(ProfileKind.Safe, o.Id), o.Id);

        using var env = new TestEnv(ProfileKind.Safe);
        env.Settings.ExtraProcesses.Add("dropbox");          // even a user-listed program is not touched by Hafif
        Assert.DoesNotContain(env.BuildPlan().Active, a => a.Kind == ActionKind.CloseProcess);
    }

    [Fact]
    public void Yayinci_does_not_show_a_discord_switch_that_never_acts()
    {
        // Discord has its own switch (proc.discord), next to the one of the block-bypass tools (proc.dpi)
        Assert.False(Catalog.ProfileDefault(ProfileKind.Stream, "proc.discord"));
        Assert.False(Catalog.ProfileDefault(ProfileKind.Stream, "proc.dpi"));
        Assert.True(Catalog.ProfileDefault(ProfileKind.Maximum, "proc.discord"));
        Assert.True(Catalog.ProfileDefault(ProfileKind.Maximum, "proc.dpi"));
        Assert.True(ProtectedList.IsProcessProtected("Discord", null, streamMode: true));
        Assert.True(ProtectedList.IsProcessProtected("LogiCapture", null, streamMode: true));
    }

    [Fact]
    public void The_one_background_switch_decides_and_an_old_proc_tray_key_is_ignored()
    {
        using var env = new TestEnv(ProfileKind.Maximum);
        env.Settings.Options["proc.tray"] = true;                 // key of a saved profile / settings from 2.0.0: no option behind it any more
        env.Settings.Options["proc.trayauto"] = true;
        var plan = env.BuildPlan();
        var action = plan.Active.SingleOrDefault(a => a.Kind == ActionKind.CloseProcess && a.Id == "proc:randomtraything");
        Assert.NotNull(action);                                // closed without asking, not merely suggested

        env.Settings.Options["proc.trayauto"] = false;         // off: the sweep does not run at all, whatever the old key says
        Assert.DoesNotContain(env.BuildPlan().Actions, a => a.Id == "proc:randomtraything");
    }

    [Theory]
    [InlineData("igcc")]
    [InlineData("sonicstudio")]
    [InlineData("waves")]
    [InlineData("dolby")]
    [InlineData("creative")]
    [InlineData("nvcontainer")]
    [InlineData("nvdisplay")]
    [InlineData("radeon")]
    [InlineData("amd")]
    [InlineData("igfx")]
    [InlineData("intel")]
    [InlineData("rtk")]
    [InlineData("realtek")]
    [InlineData("nahimic")]
    public void Every_gpu_and_audio_driver_prefix_is_protected_for_services_too(string prefix)
    {
        // the "pick a service" list offers every running service that is not protected: the service side must not be narrower
        Assert.True(ProtectedList.IsProcessProtected(prefix + "helper", null, false), "process side: " + prefix);
        Assert.True(ProtectedList.IsServiceProtected(prefix + "service"), "service side: " + prefix);
    }

    [Fact]
    public void An_empty_service_list_stays_empty_after_loading()
    {
        using var env = new TestEnv(ProfileKind.Safe);
        File.WriteAllText(AppPaths.SettingsFile, "{\"Services\": []}");
        Assert.Empty(new SettingsStore().Load().Services);
        File.WriteAllText(AppPaths.SettingsFile, "{}");
        Assert.NotEmpty(new SettingsStore().Load().Services);          // a missing list still gets the defaults
    }

    [Fact]
    public void Picked_programs_keep_their_readable_names_in_settings()
    {
        using var env = new TestEnv(ProfileKind.Safe);
        var s = new AppSettings();
        s.DisplayNames["onedrive"] = "Microsoft OneDrive";
        s.DisplayNames["svc:SomeSvc"] = "Örnek Hizmet";
        new SettingsStore().Save(s);
        var back = new SettingsStore().Load();
        Assert.Equal("Microsoft OneDrive", back.DisplayNames["onedrive"]);
        Assert.Equal("Örnek Hizmet", back.DisplayNames["svc:SomeSvc"]);
    }

    [Fact]
    public async Task A_revert_that_does_not_finish_keeps_the_apps_that_still_have_to_be_reopened()
    {
        using var env = new TestEnv(ProfileKind.Maximum);
        env.Settings.Options["proc.browsers"] = false;
        await env.Orc.PrepareAsync(env.BuildPlan(), env.Settings, CancellationToken.None);
        Assert.DoesNotContain(env.Sys.Procs, p => p.Name == "OneDrive");

        // "Hazırla" pressed again: revert WITHOUT reopening apps, but one step of the revert fails (power plan)
        var failing = new Orchestrator(new CrashingSystem(env.Sys, failOn: "setactive"), new SessionStore(), env.Log) { SettleMs = 0 };
        var prev = failing.Revert(env.Settings, restartApps: false);
        Assert.False(prev.Complete);

        // the journal was kept, and OneDrive is still marked "to reopen" in it (it used to be flagged as done and forgotten)
        var pending = new SessionStore().LoadPending();
        Assert.NotNull(pending);
        Assert.Contains(pending!.Processes, p => p.Name == "onedrive" && p.CanReopen && !p.Reverted);

        // a later, working revert reopens it
        var fin = env.Restart().Revert(env.Settings);
        Assert.True(fin.Complete, string.Join("; ", fin.Problems));
        Assert.Contains(env.Sys.Procs, p => p.Name == "OneDrive");
    }

    [Fact]
    public async Task Apps_carried_over_from_an_earlier_session_are_on_disk_before_the_slow_measurement()
    {
        using var env = new TestEnv(ProfileKind.Safe);
        env.Settings.Options = Catalog.Options.ToDictionary(o => o.Id, _ => false);
        var carry = new[] { new ProcessRecord { Name = "onedrive", Path = @"C:\Program Files\OneDrive\OneDrive.exe", CanReopen = true } };
        using var cts = new CancellationTokenSource();
        cts.Cancel();                                         // the "before" measurement throws right away
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => env.Orc.PrepareAsync(env.BuildPlan(), env.Settings, cts.Token, carry));
        var pending = new SessionStore().LoadPending();
        Assert.NotNull(pending);                              // journal exists although the run died early
        Assert.Contains(pending!.Processes, p => p.Name == "onedrive" && p.CanReopen);
    }

    [Fact]
    public async Task Journals_written_by_the_previous_version_still_reopen_their_apps()
    {
        using var env = new TestEnv(ProfileKind.Safe);
        env.Settings.Options = Catalog.Options.ToDictionary(o => o.Id, _ => false);
        var old = new[] { new ProcessRecord { Name = "onedrive", Path = @"C:\Program Files\OneDrive\OneDrive.exe", RestartOnRevert = true } };
        await env.Orc.PrepareAsync(env.BuildPlan(), env.Settings, CancellationToken.None, old);
        var res = env.Orc.Revert(env.Settings);
        Assert.True(res.Complete);
        Assert.Contains(env.Sys.Procs, p => p.Name == "OneDrive");
    }
}
