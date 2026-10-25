using CS2Hazirla.Core;
using Microsoft.Win32;
using Xunit;

namespace CS2Hazirla.Tests;

public class ProtectedListTests
{
    [Fact]
    public void Every_hardcoded_protected_name_and_prefix_is_protected()
    {
        foreach (var n in ProtectedList.AllExactProcessNames)
            Assert.True(ProtectedList.IsProcessProtected(n, null, false), n);
        foreach (var n in ProtectedList.AllExactProcessNames)
            Assert.True(ProtectedList.IsProcessProtected(n + ".EXE", null, false), n + ".EXE");
        foreach (var p in ProtectedList.AllProcessPrefixes)
            Assert.True(ProtectedList.IsProcessProtected(p + "helper", null, false), p);
        foreach (var p in ProtectedList.AllStreamPrefixes)
        {
            Assert.True(ProtectedList.IsProcessProtected(p, null, true), p + " (stream)");
        }
    }

    [Theory]
    [InlineData("steam"), InlineData("steamwebhelper"), InlineData("cs2"), InlineData("FACEITClient"), InlineData("faceit-ac"),
     InlineData("NVDisplay.Container"), InlineData("nvcontainer"), InlineData("RadeonSoftware"),
     InlineData("svchost"), InlineData("lsass"), InlineData("dwm"), InlineData("explorer"), InlineData("audiodg"),
     InlineData("ctfmon"), InlineData("fontdrvhost"), InlineData("MsMpEng"), InlineData("csrss"), InlineData("winlogon"),
     InlineData("services"), InlineData("smss"), InlineData("wininit"), InlineData("System"), InlineData("RtkAudUService64"),
     InlineData("msedgewebview2"), InlineData("DefenderSessionHelper"),
     InlineData("NVDisplay.Container"), InlineData("nvcontainer"), InlineData("vmmem")]
    public void Spec_examples_are_protected(string name) =>
        Assert.True(ProtectedList.IsProcessProtected(name, null, false));

    [Theory]
    [InlineData("node"), InlineData("python"), InlineData("code"), InlineData("claude"), InlineData("esbuild"), InlineData("chrome-native-host"),
     InlineData("dotnet"), InlineData("docker"), InlineData("Cursor")]
    public void Developer_tools_and_editors_are_no_longer_protected_everything_except_cs_can_go(string name) =>
        Assert.False(ProtectedList.IsProcessProtected(name, @"C:\Users\x\AppData\Local\Programs\x\x.exe", false), name);

    [Theory]
    [InlineData("lghub_agent"), InlineData("lghub"), InlineData("lghub_updater"), InlineData("LogiOptionsMgr"), InlineData("logioptionsplus_agent"),
     InlineData("logi_lamparray_service"), InlineData("SteelSeriesGGClient"), InlineData("SteelSeriesEngine"), InlineData("SteelSeriesGG"),
     InlineData("HyperX NGENUITY"), InlineData("NGENUITY"), InlineData("HyperXNgenuity"), InlineData("RazerCentral"), InlineData("RazerAppEngine"),
     InlineData("SynapseServer"), InlineData("iCUE"), InlineData("Corsair.Service")]
    public void Keyboard_mouse_headset_software_is_NOT_protected_and_is_a_closable_category(string name)
    {
        Assert.False(ProtectedList.IsProcessProtected(name, @"C:\Program Files\Vendor\x.exe", false), name);
        Assert.False(ProtectedList.IsProcessProtected(name, null, true), name + " (stream)");
        Assert.Equal("proc.peripherals", Catalog.CategoryOf(ProtectedList.Normalize(name))?.OptionId);
    }

    [Theory]
    [InlineData("obs64"), InlineData("Discord"), InlineData("InputOverlay"), InlineData("input-overlay")]
    public void Stream_mode_protects_obs_discord_overlay(string name)
    {
        Assert.True(ProtectedList.IsProcessProtected(name, null, true));
    }

    [Fact]
    public void Anything_under_the_windows_directory_is_protected()
    {
        Assert.True(ProtectedList.IsProcessProtected("whatever", @"C:\Windows\System32\whatever.exe", false));
    }

    [Fact]
    public void Default_closable_categories_never_contain_protected_names()
    {
        foreach (var cat in Catalog.ProcessCategories)
        {
            foreach (var n in cat.Names)
                Assert.False(ProtectedList.IsProcessProtected(n, null, false), $"{cat.Title}: {n}");
            foreach (var p in cat.Prefixes)
                Assert.False(ProtectedList.IsProcessProtected(p + "service", null, false), $"{cat.Title}: {p}*");
        }
    }

    [Fact]
    public void Default_stoppable_services_are_not_protected_and_core_services_are()
    {
        foreach (var r in Catalog.DefaultServices())
            Assert.False(ProtectedList.IsServiceProtected(r.Name), r.Name);
        foreach (var n in new[] { "WinDefend", "Audiosrv", "AudioEndpointBuilder", "RpcSs", "Dnscache", "nvlddmkm", "NVDisplay.ContainerLocalSystem",
                     "Steam Client Service", "FACEIT", "WlanSvc", "Dhcp", "BthServ" })
            Assert.True(ProtectedList.IsServiceProtected(n), n);
    }

    [Fact]
    public void Stop_list_contains_no_xbox_gip_service_by_default()
    {
        // XboxGipSvc handles Xbox controller input; stopping it would break gamepads
        Assert.DoesNotContain(Catalog.DefaultServices(), r => r.Name == "XboxGipSvc");
    }
}

[Collection("Sequential")]
public class PlannerTests
{
    [Fact]
    public void Close_list_contains_nothing_from_the_protected_list_in_any_profile()
    {
        foreach (var profile in Enum.GetValues<ProfileKind>())
        {
            using var env = new TestEnv(profile);
            // user tries hard to close protected stuff: extra list + every option on
            env.Settings.Options = Catalog.Options.ToDictionary(o => o.Id, _ => true);
            env.Settings.ExtraProcesses.AddRange(new[] { "steam", "svchost", "obs64", "MsMpEng", "cs2", "FACEITClient" });
            // plus every protected name as a running windowless process
            foreach (var n in ProtectedList.AllExactProcessNames.Concat(ProtectedList.AllProcessPrefixes.Select(p => p + "x")))
                env.Sys.AddProc(n, $@"C:\Users\x\AppData\Local\{n}\{n}.exe");

            var plan = env.BuildPlan();
            var stream = profile == ProfileKind.Stream;
            var closing = plan.Actions.Where(a => a.Kind == ActionKind.CloseProcess).Select(a => (ProcessPayload)a.Payload!).ToList();

            Assert.NotEmpty(closing);
            foreach (var p in closing)
                Assert.False(ProtectedList.IsProcessProtected(p.Name, p.Path, stream), $"{profile}: {p.Name}");

            var stops = plan.Actions.Where(a => a.Kind == ActionKind.StopService).Select(a => ((ServicePayload)a.Payload!).Name);
            foreach (var s in stops) Assert.False(ProtectedList.IsServiceProtected(s), s);
        }
    }

    [Fact]
    public void Protected_extra_processes_are_rejected_with_a_warning()
    {
        using var env = new TestEnv(ProfileKind.Maximum);
        env.Settings.ExtraProcesses.Add("steam");
        var plan = env.BuildPlan();
        Assert.DoesNotContain(plan.Actions, a => a.Id == "proc:steam");
        Assert.Contains(plan.Warnings, w => w.Contains("steam", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Protected_service_added_by_user_is_rejected()
    {
        using var env = new TestEnv(ProfileKind.Maximum);
        env.Settings.Services.Add(new ServiceRule { Name = "Audiosrv", Group = "other" });
        var plan = env.BuildPlan();
        Assert.DoesNotContain(plan.Actions, a => a.Id == "svc:audiosrv");
        Assert.Contains(plan.Warnings, w => w.Contains("Audiosrv"));
    }

    [Fact]
    public void Discord_and_the_block_bypass_tools_have_their_own_switches_and_Discord_is_always_kept_in_stream_mode()
    {
        using (var env = new TestEnv(ProfileKind.Maximum))
        {
            env.Sys.AddProc("goodbyedpi", @"C:\Tools\GoodbyeDPI\goodbyedpi.exe");
            var plan = env.BuildPlan();
            Assert.Contains(plan.Active, a => a.Id == "proc:discord");
            Assert.Contains(plan.Active, a => a.Id == "proc:goodbyedpi");          // both are closed by Tam temizlik

            env.Settings.Options["proc.dpi"] = false;                               // GoodbyeDPI stays, Discord is still closed
            var dpiKept = env.BuildPlan();
            Assert.Contains(dpiKept.Active, a => a.Id == "proc:discord");
            Assert.DoesNotContain(dpiKept.Actions, a => a.Id == "proc:goodbyedpi");

            env.Settings.Options["proc.dpi"] = true;
            env.Settings.Options["proc.discord"] = false;                           // Discord stays, GoodbyeDPI is still closed
            var discordKept = env.BuildPlan();
            Assert.DoesNotContain(discordKept.Actions, a => a.Id == "proc:discord");
            Assert.Contains(discordKept.Active, a => a.Id == "proc:goodbyedpi");
        }

        using (var env = new TestEnv(ProfileKind.Stream))
        {
            env.Settings.Options["proc.dpi"] = true;       // even if the user switches it on...
            env.Settings.Options["proc.discord"] = true;
            var plan = env.BuildPlan();
            Assert.DoesNotContain(plan.Actions, a => a.Id == "proc:discord");   // ...stream mode protects it
            Assert.DoesNotContain(plan.Actions, a => a.Id == "proc:obs64");
        }
    }

    [Fact]
    public void Unknown_windowless_processes_are_left_alone_when_the_background_switch_is_off_and_windowed_ones_have_their_own_switch()
    {
        using var env = new TestEnv(ProfileKind.Maximum);
        env.Settings.Options["proc.trayauto"] = false;
        env.Sys.AddProc("WindowedEditor", window: true);
        env.Sys.AddProc("HelperOfWindowedApp", window: false);
        env.Sys.AddProc("HelperOfWindowedApp", window: true);   // same name has a window → leave the family alone
        env.Sys.AddProc("msedgewebview2");
        env.Sys.AddProc("DefenderSessionHelper");
        var plan = env.BuildPlan();

        Assert.DoesNotContain(plan.Actions, a => a.Id == "proc:randomtraything");   // switch off: not even suggested
        // windowed apps are handled by the windowed sweep (closed like everything else, by force when they do not exit)
        var editor = Assert.Single(plan.Actions, a => a.Id == "proc:windowededitor");
        Assert.True(editor.Enabled);
        Assert.DoesNotContain(plan.Actions, a => a.Id is "proc:msedgewebview2" or "proc:defendersessionhelper");
    }

    [Fact]
    public void Maximum_profile_closes_unknown_tray_apps_without_asking_but_never_protected_ones()
    {
        using var env = new TestEnv(ProfileKind.Maximum);
        Assert.True(env.Settings.IsOn("proc.trayauto"));
        env.Sys.AddProc("DefenderSessionHelper");
        env.Sys.AddProc("msedgewebview2");
        var plan = env.BuildPlan();
        var tray = Assert.Single(plan.Actions, a => a.Id == "proc:randomtraything");
        Assert.True(tray.Enabled);
        Assert.Contains(plan.Active, a => a.Id == "proc:randomtraything");
        Assert.DoesNotContain(plan.Actions, a => a.Id is "proc:defendersessionhelper" or "proc:msedgewebview2");
    }

    [Fact]
    public async Task Peripheral_software_is_closed_in_maximum_and_comes_back_after_the_game()
    {
        using var env = new TestEnv(ProfileKind.Maximum);
        var plan = env.BuildPlan();
        foreach (var n in new[] { "lghub_agent", "steelseriesggclient", "hyperx ngenuity", "icue" })
            Assert.Contains(plan.Active, a => a.Id == $"proc:{n}");

        await env.Orc.PrepareAsync(plan, env.Settings, CancellationToken.None);
        foreach (var n in new[] { "lghub_agent", "SteelSeriesGGClient", "HyperX NGENUITY", "iCUE" })
            Assert.DoesNotContain(env.Sys.Procs, p => p.Name == n);

        env.Orc.Revert(env.Settings);
        foreach (var n in new[] { "lghub_agent", "SteelSeriesGGClient", "HyperX NGENUITY", "iCUE" })
            Assert.Contains(env.Sys.Procs, p => p.Name == n);
    }

    [Fact]
    public void Peripheral_software_is_left_alone_in_the_safe_profile()
    {
        using var env = new TestEnv(ProfileKind.Safe);
        Assert.DoesNotContain(env.BuildPlan().Actions, a => a.Id == "proc:lghub_agent");
    }

    [Fact]
    public async Task Unknown_background_programs_stay_running_when_their_switch_is_off()
    {
        using var env = new TestEnv(ProfileKind.Maximum);
        env.Settings.Options["proc.trayauto"] = false;
        await env.Orc.PrepareAsync(env.BuildPlan(), env.Settings, CancellationToken.None);
        Assert.Contains(env.Sys.Procs, p => p.Name == "RandomTrayThing");
        env.Orc.Revert(env.Settings);

        env.Settings.Options["proc.trayauto"] = true;
        await env.Orc.PrepareAsync(env.BuildPlan(), env.Settings, CancellationToken.None);
        Assert.DoesNotContain(env.Sys.Procs, p => p.Name == "RandomTrayThing");
    }

    [Fact]
    public void NeverClose_list_is_honoured()
    {
        using var env = new TestEnv(ProfileKind.Maximum);
        env.Settings.NeverClose.Add("onedrive");
        env.Settings.NeverClose.Add("randomtraything");
        var plan = env.BuildPlan();
        Assert.DoesNotContain(plan.Actions, a => a.Id is "proc:onedrive" or "proc:randomtraything");
        Assert.Contains(plan.Actions, a => a.Id == "proc:dropbox");
    }

    [Fact]
    public void Hafif_leaves_the_game_settings_alone_and_Yayinci_keeps_unknown_tray_apps_open()
    {
        // Tam temizlik ticks every switch (see SplitSwitchTests); Hafif must not switch on anything that touches the game's own settings
        Assert.False(Catalog.ProfileDefault(ProfileKind.Safe, "sys.fso"));            // the only shim that loads into the game process
        Assert.False(Catalog.ProfileDefault(ProfileKind.Safe, "sys.notifications"));
        Assert.False(Catalog.ProfileDefault(ProfileKind.Stream, "proc.trayauto"));
    }

    [Fact]
    public void Layer_flag_merge_keeps_existing_flags()
    {
        Assert.Equal("~ RUNASADMIN DISABLEDXMAXIMIZEDWINDOWEDMODE", Planner.MergeLayerFlags("~ RUNASADMIN", Planner.FsoFlag));
        Assert.Equal("~ DISABLEDXMAXIMIZEDWINDOWEDMODE", Planner.MergeLayerFlags(null, Planner.FsoFlag));
        Assert.Equal("~ DISABLEDXMAXIMIZEDWINDOWEDMODE", Planner.MergeLayerFlags("~ DISABLEDXMAXIMIZEDWINDOWEDMODE", Planner.FsoFlag));
        Assert.Equal("GpuPreference=2;", Planner.MergeGpuPreference(null));
        Assert.Equal("GpuPreference=2;SwapEffectUpgradeEnable=1;", Planner.MergeGpuPreference("SwapEffectUpgradeEnable=1;GpuPreference=1;"));
    }
}

[Collection("Sequential")]
public class DryRunTests
{
    [Fact]
    public void Planning_with_every_option_on_changes_nothing_and_calls_no_mutating_method()
    {
        using var env = new TestEnv(ProfileKind.Maximum);
        env.Settings.Options = Catalog.Options.ToDictionary(o => o.Id, _ => true);
        var before = env.Sys.Snapshot();
        env.Sys.Frozen = true;   // any mutating call now throws

        var plan = env.BuildPlan();

        Assert.NotEmpty(plan.Actions);
        Assert.Empty(env.Sys.Mutations);
        Assert.Equal(before, env.Sys.Snapshot());
        Assert.False(File.Exists(AppPaths.StateFile));
    }

    [Fact]
    public void Preview_lists_everything_that_would_happen()
    {
        using var env = new TestEnv(ProfileKind.Maximum);
        var plan = env.BuildPlan();
        foreach (var kind in new[] { ActionKind.CloseProcess, ActionKind.StopService, ActionKind.PowerPlan, ActionKind.Registry,
                     ActionKind.MouseAccel, ActionKind.NicPower, ActionKind.Accessibility, ActionKind.FlushDns })
            Assert.Contains(plan.Actions, a => a.Kind == kind);
    }
}

[Collection("Sequential")]
public class RevertTests
{
    private static async Task<PrepareResult> Run(TestEnv env)
    {
        var plan = env.BuildPlan();
        return await env.Orc.PrepareAsync(plan, env.Settings, CancellationToken.None);
    }

    [Fact]
    public async Task Prepare_then_revert_restores_power_services_registry_and_everything_else_exactly()
    {
        using var env = new TestEnv(ProfileKind.Maximum);
        env.Settings.Options["proc.browsers"] = false;      // two chrome processes come back as one: the process list would differ
        env.Settings.Options["proc.updaters"] = false;      // updaters and helper parts start themselves: not reopened by hand
        env.Settings.Options["proc.oem"] = false;           // (the test machine's only oem program is such a helper)
        var before = env.Sys.Snapshot();

        var result = await Run(env);

        Assert.Empty(result.Failed);
        Assert.True(File.Exists(AppPaths.StateFile));
        Assert.NotEqual(before, env.Sys.Snapshot());                               // something really changed
        Assert.NotEqual("381b4222-f694-41f0-9685-ff5bb260df2e", env.Sys.ActivePower);
        Assert.Equal(ServiceRunState.Stopped, env.Sys.Services["wuauserv"].State);
        Assert.Equal(ServiceRunState.Stopped, env.Sys.Services["PrintNotify"].State);   // dependent stopped with Spooler
        Assert.DoesNotContain(env.Sys.Procs, p => p.Name == "OneDrive");
        Assert.Contains(env.Sys.Procs, p => p.Name == "steam");

        var revert = env.Orc.Revert(env.Settings);

        Assert.True(revert.Complete, string.Join("; ", revert.Problems));
        Assert.False(File.Exists(AppPaths.StateFile));
        Assert.Equal(before, env.Sys.Snapshot());
    }

    [Fact]
    public async Task Registry_values_keep_their_original_kind_and_missing_keys_are_removed_again()
    {
        using var env = new TestEnv(ProfileKind.Safe);
        env.Settings.Options["sys.fso"] = true;     // off by default (see Catalog); switched on here to test the merge/restore
        env.Sys.Reg["CurrentUser|System\\GameConfigStore|GameDVR_Enabled"] = new RegValue(RegistryValueKind.QWord, "1");
        env.Sys.Keys.Add("CurrentUser|System\\GameConfigStore");

        await Run(env);

        Assert.True(env.Sys.Keys.Contains("CurrentUser|Software\\Microsoft\\GameBar"));    // created for Game Mode
        Assert.Equal("0", env.Sys.Reg["CurrentUser|System\\GameConfigStore|GameDVR_Enabled"].Data);
        Assert.Equal("~ RUNASADMIN DISABLEDXMAXIMIZEDWINDOWEDMODE", env.Sys.Reg[$"CurrentUser|{Planner.LayersKey}|{TestEnv.Cs2Path}"].Data);

        env.Orc.Revert(env.Settings);

        Assert.False(env.Sys.Keys.Contains("CurrentUser|Software\\Microsoft\\GameBar"));
        Assert.Equal(RegistryValueKind.QWord, env.Sys.Reg["CurrentUser|System\\GameConfigStore|GameDVR_Enabled"].Kind);
        Assert.Equal("1", env.Sys.Reg["CurrentUser|System\\GameConfigStore|GameDVR_Enabled"].Data);
        Assert.Equal("~ RUNASADMIN", env.Sys.Reg[$"CurrentUser|{Planner.LayersKey}|{TestEnv.Cs2Path}"].Data);
        Assert.Equal("1", env.Sys.Reg["CurrentUser|Software\\Microsoft\\Windows\\CurrentVersion\\PushNotifications|ToastEnabled"].Data);
    }

    [Fact]
    public async Task Killed_app_is_detected_on_next_start_and_revert_still_restores_everything()
    {
        using var env = new TestEnv(ProfileKind.Maximum);
        env.Settings.Options["proc.browsers"] = false;
        env.Settings.Options["proc.launchers"] = false;
        env.Settings.Options["proc.updaters"] = false;      // updaters and helper parts start themselves: not reopened by hand
        env.Settings.Options["proc.oem"] = false;
        var before = env.Sys.Snapshot();

        await Run(env);
        // --- the app is killed from Task Manager here: no Revert, in-memory state is gone ---

        var fresh = new SessionStore();
        Assert.True(fresh.HasPendingSession());                 // → "Geri alma teklif et"
        var orc2 = env.Restart();
        Assert.True(orc2.SessionActive);

        var result = orc2.Revert(env.Settings);

        Assert.True(result.Complete, string.Join("; ", result.Problems));
        Assert.Equal(before, env.Sys.Snapshot());
        Assert.False(new SessionStore().HasPendingSession());
    }

    [Fact]
    public async Task Journal_is_written_before_the_change_so_a_crash_mid_run_is_recoverable()
    {
        using var env = new TestEnv(ProfileKind.Maximum);
        env.Settings.Options["proc.browsers"] = false;
        env.Settings.Options["proc.launchers"] = false;
        env.Settings.Options["proc.updaters"] = false;
        env.Settings.Options["proc.oem"] = false;
        var before = env.Sys.Snapshot();

        // blow up in the middle of the run: after services stopped, when the power plan is switched
        env.Sys.Services["DiagTrack"].CanStop = true;
        var plan = env.BuildPlan();
        var crashing = new CrashingSystem(env.Sys, failOn: "setactive");
        var orc = new Orchestrator(crashing, env.Store, env.Log) { SettleMs = 0 };
        var result = await orc.PrepareAsync(plan, env.Settings, CancellationToken.None);
        Assert.NotEmpty(result.Failed);                          // power plan step failed, rest continued

        var rev = env.Restart().Revert(env.Settings);
        Assert.True(rev.Complete, string.Join("; ", rev.Problems));
        Assert.Equal(before, env.Sys.Snapshot());
    }

    [Fact]
    public async Task A_run_closes_the_browsers_and_changes_the_mouse_setting_without_asking_anything()
    {
        using var env = new TestEnv(ProfileKind.Maximum);
        var result = await Run(env);
        Assert.DoesNotContain(env.Sys.Procs, p => p.Name is "chrome" or "msedge");
        Assert.DoesNotContain(result.Skipped, x => x.Contains("onaylamadığın için"));
        Assert.Equal(new MouseParams(0, 0, 0), env.Sys.Mouse);                        // switched on in the list = agreed to
        env.Orc.Revert(env.Settings);
        Assert.Equal(new MouseParams(6, 10, 1), env.Sys.Mouse);                       // and "Geri yükle" brings the old value back
    }

    [Fact]
    public async Task Nothing_protected_is_closed_or_killed_during_a_real_run()
    {
        using var env = new TestEnv(ProfileKind.Stream);
        env.Sys.StreamMode = true;                                    // like the real system layer in the Yayıncı preset
        env.Settings.Options["proc.trayauto"] = true;
        var stubborn = env.Sys.AddProc("RandomTrayThing", mb: 10);   // second instance that ignores polite close
        env.Sys.Stubborn.Add(stubborn.Info.Pid);
        await Run(env);
        foreach (var keep in new[] { "steam", "steamwebhelper", "NVDisplay.Container", "svchost", "explorer", "obs64", "FACEITClient", "MsMpEng", "Discord", "notepad" })
            Assert.Contains(env.Sys.Procs, p => p.Name == keep);
        // keyboard/mouse/headset software is NOT protected any more: closed in stream mode too
        foreach (var gone in new[] { "lghub_agent", "SteelSeriesGGClient", "HyperX NGENUITY", "iCUE" })
            Assert.DoesNotContain(env.Sys.Procs, p => p.Name == gone);
        Assert.Contains(env.Sys.KilledPids, k => k == stubborn.Info.Pid);   // leftovers of non-protected apps are killed after the grace period
    }

    [Fact]
    public async Task Restore_reopens_everything_that_can_be_started_again_browsers_included()
    {
        using var env = new TestEnv(ProfileKind.Maximum);
        await Run(env);
        Assert.DoesNotContain(env.Sys.Procs, p => p.Name == "OneDrive");
        var res = env.Orc.Revert(env.Settings);
        foreach (var back in new[] { "OneDrive", "Dropbox", "chrome", "msedge", "Spotify", "Discord", "EpicGamesLauncher", "lghub_agent", "iCUE", "RandomTrayThing" })
            Assert.Contains(env.Sys.Procs, p => p.Name == back);
        // updaters and helper parts start themselves when needed: not started by hand, but listed for the user
        Assert.DoesNotContain(env.Sys.Procs, p => p.Name is "GoogleUpdate" or "ArmouryCrate.UserSessionHelper");
        Assert.Contains("Googleupdate", res.NotRestarted);                       // readable names (from the file when it exists, else the exe name)
        Assert.Contains("Armourycrate.usersessionhelper", res.NotRestarted);
    }

    [Fact]
    public async Task Restore_reopens_nothing_when_the_setting_is_switched_off()
    {
        using var env = new TestEnv(ProfileKind.Maximum);
        env.Settings.RestartClosedApps = false;
        await Run(env);
        var res = env.Orc.Revert(env.Settings);
        Assert.True(res.Complete);
        Assert.DoesNotContain(env.Sys.Procs, p => p.Name is "OneDrive" or "chrome" or "Spotify");
        Assert.Empty(res.NotRestarted);
    }

    [Fact]
    public async Task Windowed_apps_that_ignore_the_polite_request_are_ended_by_force()
    {
        using var env = new TestEnv(ProfileKind.Maximum);
        // a program that "closes" to the tray (WhatsApp, IDM, Claude...) or shows a "save changes?" dialog never exits by itself
        var app = env.Sys.AddProc("WordLike", window: true);
        var tray = env.Sys.AddProc("IDMan", @"C:\Program Files (x86)\Internet Download Manager\IDMan.exe", window: true);
        env.Sys.Stubborn.Add(app.Info.Pid);
        env.Sys.Stubborn.Add(tray.Info.Pid);
        var res = await Run(env);
        Assert.DoesNotContain(env.Sys.Procs, p => p.Pid == app.Info.Pid || p.Pid == tray.Info.Pid);
        Assert.Contains(app.Info.Pid, env.Sys.KilledPids);
        Assert.Contains(tray.Info.Pid, env.Sys.KilledPids);
        Assert.Contains(res.Done, d => d.Contains("wordlike", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(res.Skipped, s => s.Contains("wordlike", StringComparison.OrdinalIgnoreCase));
        Assert.Empty(res.NotClosed);
    }

    [Fact]
    public async Task A_program_that_starts_itself_again_right_after_being_closed_is_closed_once_more()
    {
        using var env = new TestEnv(ProfileKind.Maximum);
        env.Sys.AfterClose = n => { if (n == 1) env.Sys.AddProc("OneDrive", mb: 90); };       // a watchdog relaunches it
        await Run(env);
        Assert.DoesNotContain(env.Sys.Procs, p => p.Name == "OneDrive");
    }

    [Fact]
    public async Task WhatsApp_from_the_store_is_closed_by_force_even_when_it_only_hides_in_the_tray()
    {
        using var env = new TestEnv(ProfileKind.Maximum);
        var wa = env.Sys.AddProc("WhatsApp.Root", @"C:\Program Files\WindowsApps\5319275A.WhatsAppDesktop_2.2\WhatsApp.Root.exe", mb: 300);
        env.Sys.Stubborn.Add(wa.Info.Pid);
        Assert.Equal("proc.chat", Catalog.CategoryOf("whatsapp.root")?.OptionId);
        var res = await Run(env);
        Assert.DoesNotContain(env.Sys.Procs, p => p.Pid == wa.Info.Pid);
        Assert.Contains(wa.Info.Pid, env.Sys.KilledPids);
        Assert.Contains(res.Done, d => d.Contains("whatsapp", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Windowed_apps_are_closed_without_any_confirmation()
    {
        using var env = new TestEnv(ProfileKind.Maximum);
        var app = env.Sys.AddProc("WordLike", window: true);
        await Run(env);
        Assert.DoesNotContain(env.Sys.Procs, p => p.Pid == app.Info.Pid);
    }

    [Fact]
    public async Task Claude_has_its_own_switch_and_is_ended_by_force_when_it_only_hides_in_the_tray()
    {
        using var env = new TestEnv(ProfileKind.Maximum);
        var claude = env.Sys.AddProc("claude", window: true, mb: 600);
        var stubborn = env.Sys.AddProc("claude", mb: 150);
        env.Sys.Stubborn.Add(claude.Info.Pid);                  // closing the window only hides it in the tray
        env.Sys.Stubborn.Add(stubborn.Info.Pid);                // a part that does not exit by itself
        var plan = env.BuildPlan();
        var action = Assert.Single(plan.Actions, a => a.Id == "proc:claude");
        Assert.Equal("Claude", ((ProcessPayload)action.Payload!).Category);
        await Run(env);
        Assert.Contains(env.Sys.KilledPids, k => k == stubborn.Info.Pid);
        Assert.Contains(env.Sys.KilledPids, k => k == claude.Info.Pid);
        Assert.DoesNotContain(env.Sys.Procs, p => p.Name == "claude");
    }

    [Fact]
    public void Claude_stays_open_when_its_switch_is_off_even_though_other_open_programs_are_closed()
    {
        using var env = new TestEnv(ProfileKind.Maximum);
        env.Sys.AddProc("claude", window: true, mb: 600);
        env.Settings.Options["proc.claude"] = false;
        Assert.True(env.Settings.IsOn("proc.windowed"));
        Assert.DoesNotContain(env.BuildPlan().Actions, a => a.Id == "proc:claude");
    }

    [Fact]
    public async Task Services_start_type_is_never_changed()
    {
        using var env = new TestEnv(ProfileKind.Maximum);
        await Run(env);
        Assert.All(env.Sys.Services.Values, s => Assert.NotEqual(ServiceStart.Disabled, s.Start));
        Assert.DoesNotContain(env.Sys.Mutations, m => m.Contains("starttype", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Power_plan_created_by_the_app_is_deleted_again()
    {
        using var env = new TestEnv(ProfileKind.Safe);
        var schemes = env.Sys.Schemes.Count;
        await Run(env);
        Assert.Equal(schemes + 1, env.Sys.Schemes.Count);
        env.Orc.Revert(env.Settings);
        Assert.Equal(schemes, env.Sys.Schemes.Count);
        Assert.Equal("381b4222-f694-41f0-9685-ff5bb260df2e", env.Sys.ActivePower);
    }

    [Fact]
    public async Task A_second_prepare_is_refused_while_a_session_is_pending()
    {
        using var env = new TestEnv(ProfileKind.Safe);
        await Run(env);
        await Assert.ThrowsAsync<InvalidOperationException>(() => Run(env));
    }
}

/// <summary>Wraps FakeSystem and throws on one specific mutating call.</summary>
public class CrashingSystem : ISystem
{
    private readonly FakeSystem _f;
    private readonly string _failOn;
    public CrashingSystem(FakeSystem f, string failOn) { _f = f; _failOn = failOn; }
    public int CurrentProcessId => _f.CurrentProcessId; public int CurrentSessionId => _f.CurrentSessionId;
    public IReadOnlyList<ProcInfo> GetProcesses() => _f.GetProcesses();
    public ServiceInfo? GetService(string n) => _f.GetService(n);
    public IReadOnlyList<(string Name, string DisplayName)> GetRunningServices() => _f.GetRunningServices();
    public PowerState GetPower() => _f.GetPower();
    public RegValue? ReadRegistry(RegHive h, string k, string n) => _f.ReadRegistry(h, k, n);
    public bool RegistryKeyExists(RegHive h, string k) => _f.RegistryKeyExists(h, k);
    public MemoryInfo GetMemory() => _f.GetMemory();
    public double SampleCpuIdlePercent(int ms) => _f.SampleCpuIdlePercent(ms);
    public Task<PingStats> PingAsync(string host, int c, CancellationToken ct) => _f.PingAsync(host, c, ct);
    public IReadOnlyList<NicInfo> GetNics() => _f.GetNics();
    public IReadOnlyList<RamModule> GetRamModules() => _f.GetRamModules();
    public MouseParams GetMouse() => _f.GetMouse();
    public DisplayInfo? GetPrimaryDisplay() => _f.GetPrimaryDisplay();
    public IReadOnlyDictionary<int, UsageCounters> GetUsageCounters(IReadOnlyCollection<int> pids) => _f.GetUsageCounters(pids);
    public int? FindPidByName(string name) => _f.FindPidByName(name);
    public void RequestClose(ProcRef p) => _f.RequestClose(p);
    public IReadOnlyList<ProcRef> WaitForExit(IEnumerable<ProcRef> procs, int t) => _f.WaitForExit(procs, t);
    public void Kill(ProcRef p) => _f.Kill(p);
    public AccessibilityFlags GetAccessibility() => _f.GetAccessibility();
    public void SetAccessibility(AccessibilityFlags f) => _f.SetAccessibility(f);
    public void StopService(string n) => _f.StopService(n);
    public void StartService(string n) => _f.StartService(n);
    public string DuplicatePowerScheme(string t) => _f.DuplicatePowerScheme(t);
    public void SetActivePowerScheme(string g) { if (_failOn == "setactive") throw new InvalidOperationException("boom"); _f.SetActivePowerScheme(g); }
    public void DeletePowerScheme(string g) => _f.DeletePowerScheme(g);
    public void WriteRegistry(RegHive h, string k, string n, RegistryValueKind kind, string d) => _f.WriteRegistry(h, k, n, kind, d);
    public void DeleteRegistryValue(RegHive h, string k, string n) => _f.DeleteRegistryValue(h, k, n);
    public void DeleteRegistryKeyIfEmpty(RegHive h, string k) => _f.DeleteRegistryKeyIfEmpty(h, k);
    public void FlushDns() => _f.FlushDns();
    public void SetNicPower(string i, bool e) => _f.SetNicPower(i, e);
    public void SetMouse(MouseParams p) => _f.SetMouse(p);
    public void RestartProcess(string p) => _f.RestartProcess(p);
}

[Collection("Sequential")]
public class GameSafetyTests
{
    [Fact]
    public async Task Preparation_is_refused_while_cs2_is_already_running_and_nothing_is_touched()
    {
        using var env = new TestEnv(ProfileKind.Maximum);
        env.Sys.AddProc("cs2");
        var before = env.Sys.Snapshot();
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            env.Orc.PrepareAsync(env.BuildPlan(), env.Settings, CancellationToken.None));
        Assert.Empty(env.Sys.Mutations);
        Assert.Equal(before, env.Sys.Snapshot());
        Assert.False(File.Exists(AppPaths.StateFile));
    }

    [Fact]
    public async Task Preparation_stops_at_once_when_cs2_appears_midway_and_changes_nothing_further()
    {
        using var env = new TestEnv(ProfileKind.Maximum);
        env.Settings.Options["proc.browsers"] = false;
        var plan = env.BuildPlan();
        // the game "starts" while the preparation is running (simulated: it appears right after the power plan was switched)
        var mutationsWhenGameAppeared = -1;
        env.Sys.OnMutation = what =>
        {
            if (mutationsWhenGameAppeared < 0 && what.StartsWith("setactive")) { env.Sys.AddProc("cs2"); mutationsWhenGameAppeared = env.Sys.Mutations.Count; }
        };
        var result = await env.Orc.PrepareAsync(plan, env.Settings, CancellationToken.None);
        Assert.True(mutationsWhenGameAppeared >= 0, "the power plan step never ran");
        Assert.True(result.AbortedBecauseGameStarted);
        Assert.Null(result.After);
        Assert.Equal(mutationsWhenGameAppeared, env.Sys.Mutations.Count);   // not a single change after cs2 appeared
    }

    [Fact]
    public async Task Revert_is_refused_while_cs2_runs_and_the_journal_is_kept()
    {
        using var env = new TestEnv(ProfileKind.Safe);
        await env.Orc.PrepareAsync(env.BuildPlan(), env.Settings, CancellationToken.None);
        env.Sys.AddProc("cs2");
        var snap = env.Sys.Snapshot();
        var mutations = env.Sys.Mutations.Count;

        var res = env.Orc.Revert(env.Settings);

        Assert.False(res.Complete);
        Assert.Equal(snap, env.Sys.Snapshot());
        Assert.Equal(mutations, env.Sys.Mutations.Count);
        Assert.True(File.Exists(AppPaths.StateFile));
    }

    [Fact]
    public async Task Top_memory_close_is_refused_while_cs2_runs()
    {
        using var env = new TestEnv(ProfileKind.Safe);
        env.Sys.AddProc("cs2");
        Assert.False(env.Orc.CloseByUser("chrome", false));
        Assert.Contains(env.Sys.Procs, p => p.Name == "chrome");
        await Task.CompletedTask;
    }

    [Fact]
    public void The_game_process_can_never_be_reached_through_the_system_interface()
    {
        // structural guarantee: ISystem has no member that could act on the game process in any way except
        // close/kill by identity, and those only ever receive processes that survived the protected-list filter
        var names = typeof(ISystem).GetMethods().Select(m => m.Name).ToList();
        foreach (var banned in new[] { "SetHighPriority", "GetCommandLine", "SetAffinity", "ReadMemory", "WriteMemory",
                     "RequestTimerResolution", "ReleaseTimerResolution", "Inject", "LaunchDeElevated" })
            Assert.DoesNotContain(banned, names);
    }

    [Fact]
    public void Valve_and_anticheat_helpers_are_protected_by_prefix_and_by_install_folder()
    {
        foreach (var n in new[] { "cs2", "cs2_helper", "csgo", "CrashHandler64", "valve_whatever", "FACEITService", "steamclient64", "gldriverquery" })
            Assert.True(ProtectedList.IsProcessProtected(n, null, false), n);

        ProtectedList.AddProtectedRoots(new[] { @"D:\stea\steam" });
        Assert.True(ProtectedList.IsProcessProtected("totally-unlisted", @"D:\stea\steam\steamapps\common\Some Game\sg.exe", false));
        Assert.True(ProtectedList.IsProcessProtected("x", @"C:\Program Files\FACEIT\x.exe", false));
        Assert.False(ProtectedList.IsProcessProtected("x", @"C:\Program Files\SomeVendor\x.exe", false));
    }

    [Fact]
    public async Task Kill_and_close_work_on_identity_so_a_recycled_pid_cannot_hit_another_process()
    {
        using var env = new TestEnv(ProfileKind.Maximum);
        var stubborn = env.Sys.AddProc("RandomTrayThing", mb: 10);
        env.Sys.Stubborn.Add(stubborn.Info.Pid);
        // the pid now belongs to a different process name (recycled)
        var recycled = new ProcRef(stubborn.Info.Pid, "cs2");
        Assert.Throws<InvalidOperationException>(() => env.Sys.Kill(recycled));            // protected name: refused
        Assert.Throws<InvalidOperationException>(() => env.Sys.RequestClose(recycled));
        await Task.CompletedTask;
    }

    [Fact]
    public void State_file_contents_are_validated_before_the_elevated_revert_replays_them()
    {
        Assert.True(StateGuard.IsAllowed(new RegistryRecord { Hive = RegHive.CurrentUser, SubKey = @"Software\Microsoft\GameBar", Name = "AllowAutoGameMode" }));
        Assert.True(StateGuard.IsAllowed(new RegistryRecord { Hive = RegHive.LocalMachine, SubKey = @"SYSTEM\CurrentControlSet\Services\Tcpip\Parameters\Interfaces\{ABC}", Name = "TcpAckFrequency" }));
        Assert.False(StateGuard.IsAllowed(new RegistryRecord { Hive = RegHive.LocalMachine, SubKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", Name = "evil" }));
        Assert.False(StateGuard.IsAllowed(new RegistryRecord { Hive = RegHive.LocalMachine, SubKey = @"SYSTEM\CurrentControlSet\Services\Tcpip\Parameters\Interfaces\{ABC}", Name = "ImagePath" }));
        Assert.False(StateGuard.IsAllowed(new RegistryRecord { Hive = RegHive.CurrentUser, SubKey = @"Software\Microsoft\GameBar\..\..\Windows\CurrentVersion\Run", Name = "x" }));
        Assert.False(StateGuard.IsValidGuid("not-a-guid"));
        Assert.False(StateGuard.IsSafeRestartPath(@"\\server\share\x.exe"));
        Assert.False(StateGuard.IsSafeRestartPath(@"C:\does\not\exist.exe"));
    }

    [Fact]
    public async Task A_tampered_state_file_cannot_make_revert_write_outside_the_allow_list()
    {
        using var env = new TestEnv(ProfileKind.Safe);
        var state = new SessionState { Profile = "x" };
        state.Registry.Add(new RegistryRecord { Hive = RegHive.LocalMachine, SubKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", Name = "evil",
            ValueExisted = true, OldKind = RegistryValueKind.String, OldData = "calc.exe" });
        state.Services.Add(new ServiceRecord { Name = "..\\bad service;x", WasRunning = true });
        state.OriginalPowerGuid = "garbage";
        var store = new SessionStore();
        store.Adopt(state);
        var orc = new Orchestrator(env.Sys, store, env.Log);
        var res = orc.Revert(env.Settings);
        Assert.True(res.Complete);
        Assert.DoesNotContain(env.Sys.Mutations, m => m.Contains("Run\\evil") || m.Contains("setactive"));
        await Task.CompletedTask;
    }
}
public class MetricsTests
{
    private static MetricsSnapshot Snap(int procs, double freeGb, double idle, double ping, double jitter = 1, double loss = 0) =>
        new(DateTime.UtcNow, procs, (long)(freeGb * (1L << 30)), 2L << 30, idle, new PingStats("1.1.1.1", 20, 20, ping, jitter, loss));

    [Fact]
    public void Unchanged_numbers_are_reported_honestly()
    {
        var (rows, summary) = Metrics.Compare(Snap(200, 10, 90, 15), Snap(200, 10.02, 90.4, 15.3));
        Assert.All(rows.Where(r => r.Verdict != MetricVerdict.Info), r => Assert.Equal(MetricVerdict.Same, r.Verdict));
        Assert.Contains("anlamlı bir değişiklik yok", summary);
    }

    [Fact]
    public void Real_improvements_are_flagged_and_ping_that_did_not_move_is_not()
    {
        var (rows, summary) = Metrics.Compare(Snap(220, 8, 80, 15), Snap(150, 12, 93, 15));
        Assert.Equal(MetricVerdict.Better, rows.First(r => r.Label.StartsWith("Çalışan")).Verdict);
        Assert.Equal(MetricVerdict.Better, rows.First(r => r.Label.StartsWith("Boş bellek")).Verdict);
        Assert.Equal(MetricVerdict.Better, rows.First(r => r.Label.StartsWith("Boştaki işlemci")).Verdict);
        Assert.Equal(MetricVerdict.Same, rows.First(r => r.Label.StartsWith("İnternet gecikmesi")).Verdict);
        Assert.Contains("iyileşme", summary);
    }

    [Fact]
    public void Worse_values_are_shown_as_worse()
    {
        var (rows, _) = Metrics.Compare(Snap(150, 12, 93, 15), Snap(220, 8, 80, 40, 8, 10));
        Assert.Equal(MetricVerdict.Worse, rows.First(r => r.Label.StartsWith("İnternet gecikmesi")).Verdict);
        Assert.Equal(MetricVerdict.Worse, rows.First(r => r.Label.StartsWith("Gecikme dalgalanması")).Verdict);
    }

    [Fact]
    public void Unreachable_ping_target_does_not_crash()
    {
        var bad = new MetricsSnapshot(DateTime.UtcNow, 100, 1 << 30, 1 << 30, 90, new PingStats("x", 1, 0, 0, 0, 100, "yanıt yok"));
        var (rows, _) = Metrics.Compare(bad, bad);
        Assert.Contains(rows, r => r.Label == "İnternet gecikmesi");
    }

    [Fact]
    public void Xmp_warning_only_when_running_clearly_below_rated_speed()
    {
        Assert.Null(RamAnalysis.Check(new[] { new RamModule(16L << 30, 6000, 6000) }));
        Assert.Null(RamAnalysis.Check(new[] { new RamModule(16L << 30, 3200, 1600) }));      // clock vs transfer rate reporting
        var warn = RamAnalysis.Check(new[] { new RamModule(16L << 30, 6000, 4800) });
        Assert.NotNull(warn);
        Assert.Contains("XMP", warn);
    }
}

public class SteamLocatorTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "cs2h-steam-" + Guid.NewGuid().ToString("N"));
    public SteamLocatorTests() => Directory.CreateDirectory(_dir);
    public void Dispose() { try { Directory.Delete(_dir, true); } catch { } }

    [Fact]
    public void Parses_new_and_old_libraryfolders_vdf()
    {
        var neu = "\"libraryfolders\"\n{\n\t\"0\"\n\t{\n\t\t\"path\"\t\t\"D:\\\\stea\\\\steam\"\n\t\t\"apps\" { \"730\" \"1\" }\n\t}\n\t\"1\"\n\t{\n\t\t\"path\"\t\t\"E:\\\\SteamLibrary\"\n\t}\n}";
        Assert.Equal(new[] { @"D:\stea\steam", @"E:\SteamLibrary" }, SteamLocator.ParseLibraryPaths(neu));
        var old = "\"LibraryFolders\"\n{\n\t\"TimeNextStatsReport\"\t\t\"1\"\n\t\"1\"\t\t\"F:\\\\Games\\\\Steam\"\n}";
        Assert.Equal(new[] { @"F:\Games\Steam" }, SteamLocator.ParseLibraryPaths(old));
    }

    [Fact]
    public void Finds_cs2_in_a_secondary_library()
    {
        var steam = Path.Combine(_dir, "steam");
        var lib2 = Path.Combine(_dir, "lib2");
        Directory.CreateDirectory(Path.Combine(steam, "steamapps"));
        File.WriteAllText(Path.Combine(steam, "steamapps", "libraryfolders.vdf"),
            "\"libraryfolders\"{\"0\"{\"path\"\"" + steam.Replace(@"\", @"\\") + "\"}\"1\"{\"path\"\"" + lib2.Replace(@"\", @"\\") + "\"}}");
        var exeDir = Path.Combine(lib2, "steamapps", "common", "Counter-Strike Global Offensive", "game", "bin", "win64");
        Directory.CreateDirectory(exeDir);
        File.WriteAllText(Path.Combine(exeDir, "cs2.exe"), "x");
        File.WriteAllText(Path.Combine(lib2, "steamapps", "appmanifest_730.acf"),
            "\"AppState\"\n{\n\t\"appid\"\t\t\"730\"\n\t\"installdir\"\t\t\"Counter-Strike Global Offensive\"\n}");

        Assert.Equal(Path.Combine(exeDir, "cs2.exe"), SteamLocator.FindCs2Exe(steam));
    }

    [Fact]
    public void Returns_null_when_cs2_is_not_installed()
    {
        Directory.CreateDirectory(Path.Combine(_dir, "steam", "steamapps"));
        Assert.Null(SteamLocator.FindCs2Exe(Path.Combine(_dir, "steam")));
    }

    [Fact]
    public void ResolveCs2_is_the_cs2_of_the_steam_install_found_in_the_registry_or_null()
    {
        var steam = SteamLocator.FindSteamPath();
        Assert.Equal(steam == null ? null : SteamLocator.FindCs2Exe(steam), SteamLocator.ResolveCs2());
    }
}
