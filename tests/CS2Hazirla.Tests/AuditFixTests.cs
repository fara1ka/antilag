using CS2Hazirla.Core;
using Microsoft.Win32;
using Xunit;

namespace CS2Hazirla.Tests;

/// <summary>Regression tests for the problems found by the independent audits.</summary>
[Collection("Sequential")]
public class AuditFixTests
{
    [Fact]
    public async Task When_nothing_changed_no_journal_is_left_behind_so_no_pointless_revert_prompt()
    {
        using var env = new TestEnv(ProfileKind.Safe);
        env.Settings.Options = Catalog.Options.ToDictionary(o => o.Id, _ => false);     // every option off → empty plan
        var res = await env.Orc.PrepareAsync(env.BuildPlan(), env.Settings, CancellationToken.None);
        Assert.Empty(res.Failed);
        Assert.False(File.Exists(AppPaths.StateFile));
        Assert.False(env.Orc.SessionActive);
    }

    [Fact]
    public async Task Apps_closed_by_an_unreverted_session_are_carried_into_the_next_session_and_reopened_by_the_final_revert()
    {
        using var env = new TestEnv(ProfileKind.Maximum);
        env.Settings.Options["proc.browsers"] = false;
        await env.Orc.PrepareAsync(env.BuildPlan(), env.Settings, CancellationToken.None);
        Assert.DoesNotContain(env.Sys.Procs, p => p.Name == "OneDrive");

        // next day: the tool is opened again and prepares again WITHOUT the user ever reverting
        var orc2 = env.Restart();
        var prev = orc2.Revert(env.Settings, restartApps: false);
        Assert.True(prev.Complete);
        Assert.DoesNotContain(env.Sys.Procs, p => p.Name == "OneDrive");        // not reopened just to be closed again
        Assert.Contains(prev.Carry, c => c.Name == "onedrive");

        await orc2.PrepareAsync(env.BuildPlan(), env.Settings, CancellationToken.None, prev.Carry);
        var final = orc2.Revert(env.Settings);                                   // the real revert
        Assert.True(final.Complete);
        Assert.Contains(env.Sys.Procs, p => p.Name == "OneDrive");               // reopened after all
    }

    [Fact]
    public async Task Apps_that_cannot_be_reopened_automatically_are_listed_for_the_user()
    {
        using var env = new TestEnv(ProfileKind.Maximum);
        env.Sys.AddProc("goodbyedpi", @"C:\Tools\GoodbyeDPI\goodbyedpi.exe");              // needs its command-line switches
        env.Sys.AddProc("StoreThing", @"C:\Program Files\WindowsApps\Vendor.StoreThing_1.0\StoreThing.exe");
        await env.Orc.PrepareAsync(env.BuildPlan(), env.Settings, CancellationToken.None);
        var res = env.Orc.Revert(env.Settings);
        Assert.Contains("GoodbyeDPI", res.NotRestarted);                                    // readable names, not "goodbyedpi.exe"
        Assert.Contains("Googleupdate", res.NotRestarted);                                  // updaters start themselves
        Assert.DoesNotContain(env.Sys.Procs, p => p.Name == "goodbyedpi");
        // browsers and unknown tray apps ARE reopened now (by their own .exe, without arguments)
        Assert.DoesNotContain(res.NotRestarted, n => n.StartsWith("Chrome") || n.StartsWith("Randomtraything"));
    }

    [Fact]
    public void Reopen_policy_covers_helpers_interpreters_store_apps_and_programs_that_need_switches()
    {
        using var env = new TestEnv(ProfileKind.Maximum);   // makes StateGuard believe every path exists
        Assert.True(ReopenPolicy.CanReopen("chrome", @"C:\Program Files\Google\Chrome\chrome.exe", "proc.browsers"));
        Assert.True(ReopenPolicy.CanReopen("lghub_agent", @"C:\Program Files\LGHUB\lghub_agent.exe", "proc.peripherals"));
        Assert.True(ReopenPolicy.CanReopen("whatever", @"C:\Tools\whatever.exe", "proc.trayauto"));
        Assert.False(ReopenPolicy.CanReopen("goodbyedpi", @"C:\Tools\goodbyedpi.exe", "proc.dpi"));
        Assert.False(ReopenPolicy.CanReopen("vgc", @"C:\Program Files\Riot Vanguard\vgc.exe", "proc.anticheat"));
        Assert.False(ReopenPolicy.CanReopen("googleupdate", @"C:\Program Files\Google\Update\GoogleUpdate.exe", "proc.updaters"));
        Assert.False(ReopenPolicy.CanReopen("onedrivestandaloneupdater", @"C:\x\OneDriveStandaloneUpdater.exe", "proc.cloud"));
        Assert.False(ReopenPolicy.CanReopen("discordhelper", @"C:\x\discordhelper.exe", "proc.discord"));
        Assert.False(ReopenPolicy.CanReopen("python", @"C:\Python\python.exe", "proc.trayauto"));
        Assert.False(ReopenPolicy.CanReopen("app", @"C:\Program Files\WindowsApps\V.App_1\app.exe", "proc.chat"));
        Assert.False(ReopenPolicy.CanReopen("app", null, "proc.chat"));
        Assert.False(ReopenPolicy.CanReopen("app", @"\\server\share\app.exe", "proc.chat"));
        Assert.False(ReopenPolicy.CanReopen("app", @"relative\app.exe", "proc.chat"));
    }

    [Fact]
    public void Unknown_tray_apps_and_user_extras_are_reopened_unless_they_look_like_helpers()
    {
        using var env = new TestEnv(ProfileKind.Maximum);
        env.Settings.ExtraProcesses.Add("dropbox");
        var plan = env.BuildPlan();
        var tray = (ProcessPayload)plan.Actions.Single(a => a.Id == "proc:randomtraything").Payload!;
        Assert.True(tray.Restart);
        env.Sys.AddProc("MyHelper");
        env.Settings.ExtraProcesses.Add("myhelper");
        var extra = (ProcessPayload)env.BuildPlan().Actions.Single(a => a.Id == "proc:myhelper").Payload!;
        Assert.False(extra.Restart);                                                // looks like a helper part
        env.Sys.AddProc("MyTool");
        env.Settings.ExtraProcesses.Add("mytool");
        Assert.True(((ProcessPayload)env.BuildPlan().Actions.Single(a => a.Id == "proc:mytool").Payload!).Restart);
    }

    [Fact]
    public async Task The_same_program_closed_in_several_unreverted_sessions_is_reopened_only_once()
    {
        using var env = new TestEnv(ProfileKind.Maximum);
        env.Settings.Options["proc.browsers"] = false;
        await env.Orc.PrepareAsync(env.BuildPlan(), env.Settings, CancellationToken.None);
        var orc2 = env.Restart();
        var prev = orc2.Revert(env.Settings, restartApps: false);
        env.Sys.AddProc("OneDrive");                                                // the user opened it again between the sessions
        await orc2.PrepareAsync(env.BuildPlan(), env.Settings, CancellationToken.None, prev.Carry);
        var starts = env.Sys.Mutations.Count(m => m.StartsWith("restart ") && m.Contains("OneDrive.exe"));
        orc2.Revert(env.Settings);
        Assert.Equal(starts + 1, env.Sys.Mutations.Count(m => m.StartsWith("restart ") && m.Contains("OneDrive.exe")));
        Assert.Single(env.Sys.Procs, p => p.Name == "OneDrive");
    }

    [Fact]
    public void A_corrupt_state_file_is_quarantined_and_never_blocks_the_app()
    {
        using var env = new TestEnv(ProfileKind.Safe);
        File.WriteAllText(AppPaths.StateFile, "{ this is not json");
        var store = new SessionStore();
        Assert.True(store.HasPendingSession());
        Assert.Null(store.LoadPending());
        Assert.False(File.Exists(AppPaths.StateFile));
        Assert.Single(Directory.GetFiles(AppPaths.Root, "state.corrupt-*.json"));
        Assert.False(new Orchestrator(env.Sys, store, env.Log).SessionActive);
    }

    // (discarding a revert that cannot finish is covered by RestoreTests.A_revert_that_cannot_finish_keeps_the_journal_and_can_then_be_discarded)

    [Fact]
    public async Task Processes_that_survive_the_kill_are_reported_as_not_closed_not_as_success()
    {
        using var env = new TestEnv(ProfileKind.Maximum);
        env.Settings.Options["proc.browsers"] = false;
        var p1 = env.Sys.Procs.First(p => p.Name == "OneDrive");
        env.Sys.Stubborn.Add(p1.Pid);
        env.Sys.UnkillablePids.Add(p1.Pid);
        var res = await env.Orc.PrepareAsync(env.BuildPlan(), env.Settings, CancellationToken.None);
        Assert.Contains(res.NotClosed, n => n.Contains("onedrive", StringComparison.OrdinalIgnoreCase));      // typed list, not a string in Skipped
        Assert.DoesNotContain(res.Done, d => d.Contains("onedrive", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(res.Skipped, d => d.Contains("onedrive", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(env.Log.Lines, l => l.StartsWith("W") && l.Contains("kapanmadı"));
        Assert.DoesNotContain(env.Log.Lines, l => l.Contains("OneDrive kapatıldı", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task A_program_that_comes_back_after_the_last_sweep_is_reported_as_not_closed_not_as_closed()
    {
        using var env = new TestEnv(ProfileKind.Maximum);
        env.Settings.Options["proc.browsers"] = false;
        // arm after the first close request; the program is back in the snapshot of the last look (3rd GetProcesses after that:
        // the 1st and 2nd are the two quiet respawn sweeps, which find nothing yet)
        var armed = 0;
        env.Sys.AfterClose = n => { if (n == 1) armed = 1; };
        env.Sys.OnGetProcesses = _ =>
        {
            if (armed == 0) return;
            if (++armed == 4) env.Sys.AddProc("OneDrive", mb: 90);
        };
        var res = await env.Orc.PrepareAsync(env.BuildPlan(), env.Settings, CancellationToken.None);

        Assert.Contains(res.NotClosed, n => n.Contains("onedrive", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(res.Done, d => d.Contains("onedrive", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(env.Log.Lines, l => l.StartsWith("W") && l.Contains("tekrar açıldı, kapatılamadı"));
        Assert.DoesNotContain(env.Log.Lines, l => l.Contains("OneDrive kapatıldı", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(res.Done, d => d.Contains("dropbox", StringComparison.OrdinalIgnoreCase));         // the others are fine
    }

    [Fact]
    public async Task The_respawn_line_is_only_written_when_the_second_kill_did_not_throw()
    {
        using var env = new TestEnv(ProfileKind.Maximum);
        env.Settings.Options["proc.browsers"] = false;
        // a watchdog relaunches OneDrive right after the first close request; this copy ignores the polite request and cannot be ended
        env.Sys.AfterClose = n =>
        {
            if (n != 1) return;
            var back = env.Sys.AddProc("OneDrive", mb: 90);
            env.Sys.Stubborn.Add(back.Info.Pid);
            env.Sys.ThrowOnKillPids.Add(back.Info.Pid);
        };
        var res = await env.Orc.PrepareAsync(env.BuildPlan(), env.Settings, CancellationToken.None);

        Assert.DoesNotContain(env.Log.Lines, l => l.Contains("tekrar kapatıldı"));
        Assert.Contains(env.Log.Lines, l => l.StartsWith("W") && l.Contains("zorla kapatılamadı"));
        Assert.Contains(env.Log.Lines, l => l.StartsWith("D"));                       // the raw reason went to the log file channel
        Assert.Contains(res.NotClosed, n => n.Contains("onedrive", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void VPN_antivirus_other_anticheat_and_dpi_tools_are_closable_groups_not_protected()
    {
        var expect = new Dictionary<string, string>
        {
            ["NordVPN"] = "proc.vpn", ["ProtonVPN"] = "proc.vpn", ["Tailscale"] = "proc.vpn", ["openvpn-gui"] = "proc.vpn",
            ["avast"] = "proc.antivirus", ["AvastUI"] = "proc.antivirus", ["egui"] = "proc.antivirus", ["Bitdefender"] = "proc.antivirus", ["MBAMTray"] = "proc.antivirus",
            ["vgc"] = "proc.anticheat", ["vgtray"] = "proc.anticheat", ["EasyAntiCheat_EOS"] = "proc.anticheat", ["BEService"] = "proc.anticheat", ["ESEAClient"] = "proc.anticheat",
            ["GoodbyeDPI"] = "proc.dpi", ["goodbyedpi_x64"] = "proc.dpi",                  // GoodbyeDPI is its own switch, nothing else is in it
            ["ZapretGUI"] = "proc.bypass", ["v2rayN"] = "proc.bypass", ["Hiddify"] = "proc.bypass", ["winws"] = "proc.bypass",
            ["NVIDIA Overlay"] = "proc.overlay", ["NVIDIA Share"] = "proc.overlay", ["NVIDIA App"] = "proc.overlay",
            ["Discord"] = "proc.discord",            // Discord has its own switch, right above the block-bypass tools
        };
        foreach (var (name, option) in expect)
        {
            Assert.False(ProtectedList.IsProcessProtected(name, @"C:\Program Files\Vendor\x.exe", false), name);
            Assert.Equal(option, Catalog.CategoryOf(ProtectedList.Normalize(name))?.OptionId);
        }
    }

    [Fact]
    public void FACEIT_Steam_and_GPU_driver_components_stay_protected_while_their_front_ends_do_not()
    {
        foreach (var n in new[] { "FACEIT", "FACEITClient", "faceit-ac", "FACEIT_AC_Service", "steam", "steamwebhelper", "NVDisplay.Container", "nvcontainer", "cs2", "MsMpEng" })
            Assert.True(ProtectedList.IsProcessProtected(n, null, false), n);
        Assert.True(ProtectedList.IsProcessProtected("x", @"C:\Program Files\FACEIT AC\x.exe", false));
        Assert.False(ProtectedList.IsProcessProtected("x", @"C:\Program Files\Riot Vanguard\x.exe", false));
        Assert.False(ProtectedList.IsProcessProtected("NVIDIA Overlay", null, false));
    }

    [Fact]
    public async Task Each_new_group_can_be_switched_off_in_the_settings_and_then_its_apps_are_left_running()
    {
        using var env = new TestEnv(ProfileKind.Maximum);
        foreach (var n in new[] { "NordVPN", "AvastUI", "vgtray", "GoodbyeDPI", "NVIDIA Overlay" }) env.Sys.AddProc(n);

        var plan = env.BuildPlan();
        foreach (var id in new[] { "proc:nordvpn", "proc:avastui", "proc:vgtray", "proc:goodbyedpi", "proc:nvidia overlay" })
            Assert.Contains(plan.Active, a => a.Id == id);

        foreach (var opt in new[] { "proc.vpn", "proc.antivirus", "proc.anticheat", "proc.dpi", "proc.overlay" }) env.Settings.Options[opt] = false;
        var off = env.BuildPlan();
        foreach (var id in new[] { "proc:nordvpn", "proc:avastui", "proc:vgtray", "proc:goodbyedpi", "proc:nvidia overlay" })
            Assert.DoesNotContain(off.Actions, a => a.Id == id);          // switched off: not planned, not swept as "unknown" either
        await Task.CompletedTask;
    }

    [Fact]
    public void Stream_profile_keeps_vpn_and_dpi_tools_connectivity_for_streaming()
    {
        Assert.False(Catalog.ProfileDefault(ProfileKind.Stream, "proc.vpn"));
        Assert.False(Catalog.ProfileDefault(ProfileKind.Stream, "proc.dpi"));
        Assert.False(Catalog.ProfileDefault(ProfileKind.Stream, "proc.discord"));
        Assert.True(Catalog.ProfileDefault(ProfileKind.Stream, "proc.antivirus"));
    }
    [Fact]
    public void Notification_suppression_is_part_of_Tam_temizlik_but_not_of_Hafif()
    {
        // Tam temizlik ticks everything; Hafif keeps to the harmless settings. Geri yükle switches the notifications back on.
        Assert.False(Catalog.ProfileDefault(ProfileKind.Safe, "sys.notifications"));
        Assert.True(Catalog.ProfileDefault(ProfileKind.Maximum, "sys.notifications"));
    }

    [Fact]
    public void Settings_with_explicit_nulls_do_not_crash_loading()
    {
        using var env = new TestEnv(ProfileKind.Safe);
        File.WriteAllText(AppPaths.SettingsFile, "{\"NeverClose\": null, \"Options\": null, \"Services\": null, \"ExtraProcesses\": null, \"PingTarget\": null}");
        var s = new SettingsStore().Load();
        Assert.NotNull(s.NeverClose);
        Assert.NotNull(s.Options);
        Assert.NotEmpty(s.Services);
        Assert.Equal("1.1.1.1", s.PingTarget);
    }

    [Fact]
    public async Task A_pre_revert_that_follows_an_empty_session_does_nothing_and_cs2_open_blocks_even_that()
    {
        using var env = new TestEnv(ProfileKind.Safe);
        env.Sys.AddProc("cs2");
        var res = env.Orc.Revert(env.Settings, restartApps: false);
        Assert.True(res.Complete);                                   // no session: nothing to do
        Assert.Empty(env.Sys.Mutations);
        await Task.CompletedTask;
    }
}
