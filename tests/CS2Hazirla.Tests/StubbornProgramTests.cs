using CS2Hazirla.Core;
using Xunit;

namespace CS2Hazirla.Tests;

/// <summary>
/// Programs that do not want to stay closed (an overlay that its own service starts again, a browser that takes seconds to let go of the GPU):
/// the run keeps ending them for a bounded time, asks twice before it gives up, and says in the log file what happened.
/// </summary>
[Collection("Sequential")]
public class StubbornProgramTests
{
    private static TestEnv Env()
    {
        var env = new TestEnv(ProfileKind.Maximum);
        env.Settings.Options["proc.browsers"] = false;
        return env;
    }

    [Fact]
    public async Task A_program_that_is_started_again_several_times_is_ended_each_time_and_finally_counts_as_closed()
    {
        using var env = Env();
        var armed = 0;
        env.Sys.AfterClose = n => { if (n == 1) armed = 1; };
        env.Sys.OnGetProcesses = _ =>
        {
            if (armed == 0) return;
            armed++;
            if (armed <= 4) env.Sys.AddProc("OneDrive", mb: 90);          // back after the 1st, 2nd and 3rd look: three relaunches
        };
        var res = await env.Orc.PrepareAsync(env.BuildPlan(), env.Settings, CancellationToken.None);

        Assert.Contains(res.Done, d => d.Contains("onedrive", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(res.NotClosed, n => n.Contains("onedrive", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(env.Sys.Procs, p => p.Name == "OneDrive");
        Assert.Contains(env.Log.Lines, l => l.Contains("tekrar kapatıldı"));
    }

    [Fact]
    public async Task A_program_that_always_comes_back_is_given_up_after_a_bounded_number_of_sweeps_and_is_reported_honestly()
    {
        using var env = Env();
        var armed = 0;
        env.Sys.AfterClose = n => { if (n == 1) armed = 1; };
        env.Sys.OnGetProcesses = _ => { if (armed > 0) { armed++; env.Sys.AddProc("OneDrive", mb: 90); } };
        var res = await env.Orc.PrepareAsync(env.BuildPlan(), env.Settings, CancellationToken.None);

        Assert.Contains(res.NotClosed, n => n.Contains("onedrive", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(env.Log.Lines, l => l.StartsWith("W") && l.Contains("tekrar açıldı, kapatılamadı"));
        Assert.Contains(env.Log.Lines, l => l.StartsWith("D") && l.Contains("kendiliğinden yeniden açıldı"));    // the reason, for the log file
        Assert.True(armed < 30, $"the sweeps did not stop (looked {armed} times)");                              // bounded: no endless loop
    }

    [Fact]
    public async Task A_process_that_is_still_listed_after_the_first_forced_end_is_ended_again_and_waited_for()
    {
        using var env = Env();
        var one = env.Sys.Procs.First(p => p.Name == "OneDrive");
        env.Sys.Stubborn.Add(one.Pid);
        env.Sys.LingerKills[one.Pid] = 1;                                    // the first kill is accepted but the process is still being torn down
        var res = await env.Orc.PrepareAsync(env.BuildPlan(), env.Settings, CancellationToken.None);

        Assert.Contains(res.Done, d => d.Contains("onedrive", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(res.NotClosed, n => n.Contains("onedrive", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(2, env.Sys.Mutations.Count(m => m == "kill OneDrive"));
    }

    [Fact]
    public async Task A_process_that_never_goes_away_is_reported_and_the_log_file_says_whether_the_end_was_refused_or_ignored()
    {
        using var env = Env();
        var accepted = env.Sys.Procs.First(p => p.Name == "OneDrive");
        var refused = env.Sys.Procs.First(p => p.Name == "Dropbox");
        foreach (var p in new[] { accepted, refused }) env.Sys.Stubborn.Add(p.Pid);
        env.Sys.UnkillablePids.Add(accepted.Pid);                            // the end is accepted and has no effect
        env.Sys.ThrowOnKillPids.Add(refused.Pid);                            // the end is refused (access denied)
        var res = await env.Orc.PrepareAsync(env.BuildPlan(), env.Settings, CancellationToken.None);

        Assert.Contains(res.NotClosed, n => n.Contains("onedrive", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(res.NotClosed, n => n.Contains("dropbox", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(env.Log.Lines, l => l.StartsWith("D") && l.Contains("OneDrive") && l.Contains("kabul edildi ama işlem yaşamaya devam etti"));
        Assert.Contains(env.Log.Lines, l => l.StartsWith("D") && l.Contains("Dropbox") && l.Contains("reddedildi"));
        // nothing is reported as closed that is not
        Assert.DoesNotContain(res.Done, d => d.Contains("onedrive", StringComparison.OrdinalIgnoreCase) || d.Contains("dropbox", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task The_extra_sweeps_never_touch_a_protected_program_or_the_game()
    {
        using var env = Env();
        var armed = 0;
        env.Sys.AfterClose = n => { if (n == 1) armed = 1; };
        env.Sys.OnGetProcesses = _ => { if (armed > 0 && ++armed < 12) { env.Sys.AddProc("OneDrive", mb: 90); } };
        await env.Orc.PrepareAsync(env.BuildPlan(), env.Settings, CancellationToken.None);

        Assert.DoesNotContain(env.Sys.Mutations, m => (m.StartsWith("close ") || m.StartsWith("kill ")) && (m.Contains("steam", StringComparison.OrdinalIgnoreCase) || m.Contains("FACEIT") || m.Contains("NVDisplay") || m.Contains("explorer") || m.Contains("MsMpEng") || m.Contains("svchost")));
    }

    [Fact]
    public async Task The_game_starting_in_a_late_sweep_stops_everything_and_says_so()
    {
        using var env = Env();
        var armed = 0;
        env.Sys.AfterClose = n => { if (n == 1) armed = 1; };
        env.Sys.OnGetProcesses = _ =>
        {
            if (armed == 0) return;
            armed++;
            if (armed <= 3) env.Sys.AddProc("OneDrive", mb: 90);         // two relaunches
            if (armed == 4) env.Sys.AddProc("cs2", mb: 100);             // then the game
        };
        var res = await env.Orc.PrepareAsync(env.BuildPlan(), env.Settings, CancellationToken.None);

        Assert.True(res.AbortedBecauseGameStarted);
        Assert.Contains(env.Sys.Procs, p => p.Name == "cs2");            // never touched
    }

    [Fact]
    public async Task A_program_that_comes_back_every_time_is_left_alone_after_three_comebacks()
    {
        using var env = Env();
        var armed = 0;
        env.Sys.AfterClose = n => { if (n == 1) armed = 1; };
        env.Sys.OnGetProcesses = _ => { if (armed > 0) { armed++; env.Sys.AddProc("OneDrive", mb: 90); } };
        var res = await env.Orc.PrepareAsync(env.BuildPlan(), env.Settings, CancellationToken.None);

        Assert.Contains(res.NotClosed, n => n.Contains("onedrive", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(4, env.Sys.Mutations.Count(m => m == "close OneDrive"));      // the first time and three comebacks, then it is left alone
        Assert.True(armed <= 12, $"looked {armed} times");
    }

    [Fact]
    public async Task The_warning_tells_a_refused_end_from_one_that_was_accepted_but_did_not_take_effect()
    {
        using var env = Env();
        var accepted = env.Sys.Procs.First(p => p.Name == "OneDrive");
        var refused = env.Sys.Procs.First(p => p.Name == "Dropbox");
        foreach (var p in new[] { accepted, refused }) env.Sys.Stubborn.Add(p.Pid);
        env.Sys.UnkillablePids.Add(accepted.Pid);
        env.Sys.ThrowOnKillPids.Add(refused.Pid);
        await env.Orc.PrepareAsync(env.BuildPlan(), env.Settings, CancellationToken.None);

        var all = string.Join(" | ", env.Log.Lines);
        Assert.True(env.Log.Lines.Any(l => l.StartsWith("W") && l.Contains("kabul edildi ama program hâlâ çalışıyor")), "log: " + all);
        Assert.True(env.Log.Lines.Any(l => l.StartsWith("W") && l.Contains("Windows kapatmaya izin vermedi")), "log: " + all);
    }

    [Fact]
    public void A_running_process_is_not_one_that_has_ended()
    {
        using var me = System.Diagnostics.Process.GetCurrentProcess();
        Assert.False(WindowsSystem.HasEnded(me));                          // an ended process that only a handle keeps listed has no threads
    }
}

[Collection("Sequential")]
public class UpdateSettingTests
{
    [Fact]
    public void The_switch_is_on_by_default_survives_a_save_and_an_old_settings_file_means_on()
    {
        using var env = new TestEnv(ProfileKind.Safe);
        var store = new SettingsStore();
        Assert.True(store.Load().CheckForUpdates);                                         // nothing saved yet
        File.WriteAllText(AppPaths.SettingsFile, "{ \"PingTarget\": \"1.1.1.1\" }");        // a 3.3 file has no such field
        Assert.True(store.Load().CheckForUpdates);
        var s = store.Load();
        s.CheckForUpdates = false;
        store.Save(s);
        Assert.False(new SettingsStore().Load().CheckForUpdates);
    }
}
