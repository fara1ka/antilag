using CS2Hazirla.Core;
using Microsoft.Win32;
using Xunit;
using Xunit.Abstractions;

namespace CS2Hazirla.Tests;

/// <summary>
/// Tests against the REAL Windows implementation. Everything here is read-only or confined to a
/// throw-away HKCU\Software\CS2HazirlaTest key. Power plan switching is opt-in (CS2H_LIVE=1).
/// </summary>
[Collection("Sequential")]
public class LiveReadOnlyTests
{
    private readonly ITestOutputHelper _out;
    private readonly WindowsSystem _sys = new();
    public LiveReadOnlyTests(ITestOutputHelper o) => _out = o;

    [Fact]
    public void Process_list_contains_this_test_process_with_a_path()
    {
        var me = _sys.GetProcesses().FirstOrDefault(p => p.Pid == Environment.ProcessId);
        Assert.NotNull(me);
        Assert.False(string.IsNullOrEmpty(me!.Path));
        Assert.Equal(_sys.CurrentSessionId, me.SessionId);
    }

    [Fact]
    public void Memory_probe_returns_plausible_values_including_standby()
    {
        var m = _sys.GetMemory();
        _out.WriteLine($"Total {m.TotalBytes / 1048576} MB, free {m.AvailableBytes / 1048576} MB, standby {m.StandbyBytes / 1048576} MB");
        Assert.True(m.TotalBytes > 1L << 30);
        Assert.InRange(m.AvailableBytes, 1, m.TotalBytes);
        Assert.InRange(m.StandbyBytes, 0, m.TotalBytes);
    }

    [Fact]
    public void FindPidByName_uses_the_snapshot_and_finds_this_process_or_nothing()
    {
        // several processes can share this name (test hosts): the answer must be the id of ONE of them, compared with what the OS says
        var me = System.Diagnostics.Process.GetCurrentProcess().ProcessName;
        var ids = System.Diagnostics.Process.GetProcessesByName(me).Select(p => p.Id).ToList();
        var found = _sys.FindPidByName(me);
        Assert.NotNull(found);
        Assert.Contains(found!.Value, ids);
        Assert.Null(_sys.FindPidByName("definitely-not-a-process-12345"));
    }

    [Fact]
    public void Cpu_idle_percent_is_in_range()
    {
        var idle = _sys.SampleCpuIdlePercent(300);
        _out.WriteLine($"idle {idle:F1}%");
        Assert.InRange(idle, 0, 100);
    }

    [Fact]
    public void Power_state_has_an_active_scheme_that_is_in_the_list()
    {
        var p = _sys.GetPower();
        _out.WriteLine($"active {p.ActiveGuid}; " + string.Join(" | ", p.Schemes.Select(s => $"{s.Guid} {s.Name}")));
        Assert.Contains(p.Schemes, s => s.Guid == p.ActiveGuid);
    }

    [Fact]
    public void Service_probe_reads_state_start_type_and_handles_missing_services()
    {
        var s = _sys.GetService("Spooler");
        Assert.NotNull(s);
        _out.WriteLine($"Spooler {s!.State} {s.StartType} canStop={s.CanStop} deps=[{string.Join(",", s.RunningDependents)}]");
        Assert.Null(_sys.GetService("DefinitelyNotAService12345"));
        foreach (var r in Catalog.DefaultServices())
        {
            var i = _sys.GetService(r.Name);
            _out.WriteLine($"{r.Name}: {(i == null ? "yok" : $"{i.State}/{i.StartType}/canStop={i.CanStop}")}");
        }
    }

    [Fact]
    public async Task Ping_to_loopback_works_and_unknown_host_reports_an_error_instead_of_throwing()
    {
        var ok = await _sys.PingAsync("127.0.0.1", 3, CancellationToken.None);
        Assert.Equal(3, ok.Received);
        Assert.Equal(0, ok.LossPercent);
        var bad = await _sys.PingAsync("host.invalid", 3, CancellationToken.None);
        Assert.Equal(0, bad.Received);
        Assert.NotNull(bad.Error);
    }

    [Fact]
    public async Task Ping_to_an_address_that_never_answers_gives_up_after_three_failures_not_after_twenty()
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var bad = await _sys.PingAsync("192.0.2.1", 20, CancellationToken.None);   // TEST-NET-1: reserved, nothing answers
        Assert.Equal(0, bad.Received);
        Assert.InRange(bad.Sent, 1, 3);                                              // never the 20 samples (1 only when the machine has no network at all)
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(8), $"took {sw.Elapsed}");
    }

    [Fact]
    public void Hardware_probes_do_not_throw()
    {
        var ram = _sys.GetRamModules();
        _out.WriteLine(string.Join("; ", ram.Select(r => $"{r.CapacityBytes >> 30} GB speed={r.SpeedMhz} configured={r.ConfiguredMhz}")));
        Assert.NotEmpty(ram);
        _out.WriteLine("XMP check: " + (RamAnalysis.Check(ram) ?? "uyarı yok"));
        var nics = _sys.GetNics();
        _out.WriteLine(string.Join("; ", nics.Select(n => $"{n.Name} wifi={n.IsWireless} powerSave={n.PowerSaveEnabled}")));
        var mouse = _sys.GetMouse();
        _out.WriteLine($"mouse accel={mouse.Accel} t1={mouse.Threshold1} t2={mouse.Threshold2}");
    }

    [Fact]
    public void Steam_and_cs2_are_located_when_installed()
    {
        var steam = SteamLocator.FindSteamPath();
        _out.WriteLine($"steam={steam}; cs2={(steam == null ? null : SteamLocator.FindCs2Exe(steam))}");
        if (steam != null) Assert.True(File.Exists(Path.Combine(steam, "steam.exe")));
    }
}

[Collection("Sequential")]
public class LiveRegistrySandboxTests : IDisposable
{
    private const string Sandbox = @"Software\CS2HazirlaTest";
    private readonly WindowsSystem _sys = new();
    private readonly string _oldRoot = AppPaths.Root;
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "cs2h-live-" + Guid.NewGuid().ToString("N"));

    public LiveRegistrySandboxTests()
    {
        if (!StateGuard.ExtraAllowedHkcuRoots.Contains(Sandbox)) StateGuard.ExtraAllowedHkcuRoots.Add(Sandbox);
        AppPaths.Root = _dir;
        AppPaths.Ensure();
        Cleanup();
    }

    public void Dispose()
    {
        Cleanup();
        AppPaths.Root = _oldRoot;
        try { Directory.Delete(_dir, true); } catch { }
    }

    private static void Cleanup()
    {
        using var k = Registry.CurrentUser;
        k.DeleteSubKeyTree(Sandbox, false);
    }

    [Fact]
    public void Every_value_kind_survives_a_write_read_cycle()
    {
        _sys.WriteRegistry(RegHive.CurrentUser, Sandbox, "dw", RegistryValueKind.DWord, "4294967295");   // -1
        _sys.WriteRegistry(RegHive.CurrentUser, Sandbox, "qw", RegistryValueKind.QWord, "9007199254740993");
        _sys.WriteRegistry(RegHive.CurrentUser, Sandbox, "s", RegistryValueKind.String, "~ RUNASADMIN");
        _sys.WriteRegistry(RegHive.CurrentUser, Sandbox, "e", RegistryValueKind.ExpandString, "%TEMP%\\x");
        _sys.WriteRegistry(RegHive.CurrentUser, Sandbox, "b", RegistryValueKind.Binary, Convert.ToBase64String(new byte[] { 1, 2, 3 }));
        _sys.WriteRegistry(RegHive.CurrentUser, Sandbox, "m", RegistryValueKind.MultiString, "a\0b");
        Assert.Equal(new RegValue(RegistryValueKind.DWord, "4294967295"), _sys.ReadRegistry(RegHive.CurrentUser, Sandbox, "dw"));
        Assert.Equal(new RegValue(RegistryValueKind.QWord, "9007199254740993"), _sys.ReadRegistry(RegHive.CurrentUser, Sandbox, "qw"));
        Assert.Equal(new RegValue(RegistryValueKind.String, "~ RUNASADMIN"), _sys.ReadRegistry(RegHive.CurrentUser, Sandbox, "s"));
        Assert.Equal(new RegValue(RegistryValueKind.ExpandString, "%TEMP%\\x"), _sys.ReadRegistry(RegHive.CurrentUser, Sandbox, "e"));
        Assert.Equal(new RegValue(RegistryValueKind.Binary, Convert.ToBase64String(new byte[] { 1, 2, 3 })), _sys.ReadRegistry(RegHive.CurrentUser, Sandbox, "b"));
        Assert.Equal(new RegValue(RegistryValueKind.MultiString, "a\0b"), _sys.ReadRegistry(RegHive.CurrentUser, Sandbox, "m"));
        Assert.Null(_sys.ReadRegistry(RegHive.CurrentUser, Sandbox, "nope"));
    }

    [Fact]
    public async Task Orchestrator_journal_and_revert_work_against_the_real_registry()
    {
        // pre-existing value of another kind, plus a value in a key that does not exist yet
        _sys.WriteRegistry(RegHive.CurrentUser, Sandbox + @"\Existing", "Flag", RegistryValueKind.String, "old-text");
        var changes = new List<RegChange>
        {
            new(RegHive.CurrentUser, Sandbox + @"\Existing", "Flag", RegistryValueKind.DWord, 7),
            new(RegHive.CurrentUser, Sandbox + @"\Fresh", "Value", RegistryValueKind.DWord, 1),
        };
        var plan = new Plan();
        plan.Actions.Add(new PlannedAction { Id = "reg:t", Kind = ActionKind.Registry, Title = "sandbox", Payload = new RegistryPayload(changes) });
        var settings = new AppSettings { PingTarget = "127.0.0.1" };
        var store = new SessionStore();
        var orc = new Orchestrator(_sys, store, new NullLog());

        await orc.PrepareAsync(plan, settings, CancellationToken.None);

        Assert.Equal("7", _sys.ReadRegistry(RegHive.CurrentUser, Sandbox + @"\Existing", "Flag")!.Data);
        Assert.True(File.Exists(AppPaths.StateFile));
        // the journal on disk holds the old value (this is what survives a kill)
        var disk = File.ReadAllText(AppPaths.StateFile);
        Assert.Contains("old-text", disk);

        // "new process": only the file is left
        var orc2 = new Orchestrator(_sys, new SessionStore(), new NullLog());
        var res = orc2.Revert(settings);

        Assert.True(res.Complete, string.Join(";", res.Problems));
        Assert.Equal(new RegValue(RegistryValueKind.String, "old-text"), _sys.ReadRegistry(RegHive.CurrentUser, Sandbox + @"\Existing", "Flag"));
        Assert.False(_sys.RegistryKeyExists(RegHive.CurrentUser, Sandbox + @"\Fresh"));   // key we created is gone again
        Assert.False(File.Exists(AppPaths.StateFile));
    }
}

[Collection("Sequential")]
public class LiveSessionSettingsTests
{
    /// <summary>Opt-in (CS2H_LIVE=1): flips session-only input settings for a moment and restores them (flags = 0, no broadcast).</summary>
    [RealMachineFact]
    public void Mouse_and_hotkey_settings_apply_without_a_broadcast_and_restore_exactly()
    {
        var sys = new WindowsSystem();

        var m0 = sys.GetMouse();
        var a0 = sys.GetAccessibility();
        try
        {
            sys.SetMouse(m0 with { Accel = m0.Accel == 0 ? 1 : 0 });
            Assert.NotEqual(m0.Accel, sys.GetMouse().Accel);

            sys.SetAccessibility(new AccessibilityFlags(a0.Sticky ^ 0x20, a0.Toggle, a0.Filter));   // 0x20 = audible feedback bit
            Assert.Equal(a0.Sticky ^ 0x20, sys.GetAccessibility().Sticky);
        }
        finally
        {
            sys.SetMouse(m0);
            sys.SetAccessibility(a0);
        }
        Assert.Equal(m0, sys.GetMouse());
        Assert.Equal(a0, sys.GetAccessibility());
    }
}

[Collection("Sequential")]
public class LiveHandleTests
{
    private readonly ITestOutputHelper _out;
    public LiveHandleTests(ITestOutputHelper o) => _out = o;

    [Fact]
    public void Process_enumeration_opens_no_handle_to_protected_processes_so_paths_stay_unknown()
    {
        var all = new WindowsSystem().GetProcesses();
        var protectedOnes = all.Where(p => ProtectedList.IsProcessProtected(p.Name, null, false)).ToList();
        _out.WriteLine($"{protectedOnes.Count} protected of {all.Count}: " + string.Join(", ", protectedOnes.Select(p => p.Name).Distinct().Take(25)));
        Assert.NotEmpty(protectedOnes);
        Assert.All(protectedOnes, p => { Assert.Null(p.Path); Assert.False(p.HasWindow); });
    }

    [Fact]
    public async Task Close_request_posts_WM_CLOSE_to_every_window_and_refuses_protected_names()
    {
        var sys = new WindowsSystem();
        Assert.Throws<InvalidOperationException>(() => sys.RequestClose(new ProcRef(Environment.ProcessId, "cs2")));
        Assert.Throws<InvalidOperationException>(() => sys.Kill(new ProcRef(Environment.ProcessId, "steam")));

        // a throw-away process: the identity check passes only with its real name
        // (copied out of the Windows folder: anything under %WINDIR% is protected by path)
        var dir = Path.Combine(Path.GetTempPath(), "zzvictim-dir-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var exe = Path.Combine(dir, "zzvictim.exe");
        File.Copy(Path.Combine(Environment.SystemDirectory, "ping.exe"), exe);
        using var victim = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(exe, "-n 30 127.0.0.1") { CreateNoWindow = true, UseShellExecute = false })!;
        var wrongName = new ProcRef(victim.Id, "notepad");
        Assert.Empty(sys.WaitForExit(new[] { wrongName }, 200));            // wrong identity = not "running"
        sys.RequestClose(wrongName);                                         // no-op: identity mismatch
        Assert.False(victim.HasExited);

        var right = new ProcRef(victim.Id, victim.ProcessName);
        Assert.Single(sys.WaitForExit(new[] { right }, 100));
        sys.Kill(right);
        Assert.True(victim.WaitForExit(5000));
        try { Directory.Delete(dir, true); } catch { }
        await Task.CompletedTask;
    }
}
/// <summary>Opt-in (set CS2H_LIVE=1): briefly switches the REAL active power plan and restores it.</summary>
[Collection("Sequential")]
public class LivePowerTests
{
    private readonly ITestOutputHelper _out;
    public LivePowerTests(ITestOutputHelper o) => _out = o;

    [RealMachineFact]
    public async Task Power_plan_switch_and_revert_leaves_the_machine_exactly_as_before()
    {
        var sys = new WindowsSystem();
        var before = sys.GetPower();
        var oldRoot = AppPaths.Root;
        AppPaths.Root = Path.Combine(Path.GetTempPath(), "cs2h-livepower-" + Guid.NewGuid().ToString("N"));
        AppPaths.Ensure();
        try
        {
            var plan = new Plan();
            plan.Actions.Add(new PlannedAction { Id = "pwr", Kind = ActionKind.PowerPlan, Title = "pwr",
                Payload = new PowerPayload(null, null) });
            var settings = new AppSettings { PingTarget = "127.0.0.1" };
            var orc = new Orchestrator(sys, new SessionStore(), new NullLog());

            var res = await orc.PrepareAsync(plan, settings, CancellationToken.None);
            var during = sys.GetPower();
            _out.WriteLine($"before={before.ActiveGuid} during={during.ActiveGuid} failed=[{string.Join(";", res.Failed)}]");
            Assert.NotEqual(before.ActiveGuid, during.ActiveGuid);

            var rev = orc.Revert(settings);
            Assert.True(rev.Complete, string.Join(";", rev.Problems));
        }
        finally
        {
            // belt and braces: never leave the user on a different plan
            try { sys.SetActivePowerScheme(before.ActiveGuid); } catch { }
            AppPaths.Root = oldRoot;
        }

        var after = sys.GetPower();
        Assert.Equal(before.ActiveGuid, after.ActiveGuid);
        Assert.Equal(before.Schemes.Select(s => s.Guid).OrderBy(x => x), after.Schemes.Select(s => s.Guid).OrderBy(x => x));
    }
}
