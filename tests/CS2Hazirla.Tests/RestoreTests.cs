using CS2Hazirla.Core;
using Microsoft.Win32;
using Xunit;

namespace CS2Hazirla.Tests;

/// <summary>"Geri yükle": reopening what was closed, and the corner cases an independent review found.</summary>
[Collection("Sequential")]
public class RestoreTests
{
    private static Task<PrepareResult> Run(TestEnv env) =>
        env.Orc.PrepareAsync(env.BuildPlan(), env.Settings, CancellationToken.None);

    private static SessionState OpenJournal(TestEnv env, params ProcessRecord[] records)
    {
        var st = env.Store.Begin("Maximum");
        st.Processes.AddRange(records);
        env.Store.Save();
        return st;
    }

    // ------------------------------------------------------------ the game appears while programs are being closed / reopened

    [Fact]
    public async Task Game_starting_in_the_middle_of_the_closing_batch_stops_everything_at_once()
    {
        using var env = new TestEnv(ProfileKind.Maximum);
        env.Sys.AfterClose = n => { if (n == 1) env.Sys.AddProc("cs2"); };
        var res = await Run(env);

        Assert.True(res.AbortedBecauseGameStarted);
        Assert.Equal(1, env.Sys.Mutations.Count(m => m.StartsWith("close ")));           // only the request that was already out
        Assert.DoesNotContain(env.Sys.Mutations, m => m.StartsWith("stop ") || m.StartsWith("regset") || m.StartsWith("setactive"));
        Assert.Null(res.After);
        Assert.Empty(env.Sys.KilledPids);
    }

    [Fact]
    public void Game_starting_during_a_restore_stops_it_and_keeps_the_rest_in_the_journal()
    {
        using var env = new TestEnv(ProfileKind.Maximum);
        OpenJournal(env,
            new ProcessRecord { Name = "toola", Path = @"C:\Tools\toola.exe", CanReopen = true },
            new ProcessRecord { Name = "toolb", Path = @"C:\Tools\toolb.exe", CanReopen = true },
            new ProcessRecord { Name = "toolc", Path = @"C:\Tools\toolc.exe", CanReopen = true });
        env.Sys.AfterRestart = n => { if (n == 1) env.Sys.AddProc("cs2"); };

        var res = env.Restart().Revert(env.Settings);

        Assert.False(res.Complete);
        Assert.Equal(1, env.Sys.Mutations.Count(m => m.StartsWith("restart ")));
        var pending = new SessionStore().LoadPending();
        Assert.NotNull(pending);
        Assert.Equal(2, pending!.Processes.Count(p => !p.Reverted));                    // the two that were not opened are still to do
    }

    // ------------------------------------------------------------ what counts as "already open"

    [Fact]
    public async Task Revert_never_starts_a_second_copy_of_a_program_that_could_not_be_closed()
    {
        using var env = new TestEnv(ProfileKind.Maximum);
        var app = env.Sys.AddProc("WordLike", window: true);
        env.Sys.Stubborn.Add(app.Info.Pid);
        env.Sys.UnkillablePids.Add(app.Info.Pid);                // Windows refuses to end it: it stays open
        await Run(env);

        var res = env.Orc.Revert(env.Settings);

        Assert.True(res.Complete);
        Assert.Single(env.Sys.Procs, p => p.Name == "WordLike");
        Assert.DoesNotContain(env.Sys.Mutations, m => m.StartsWith("restart ") && m.Contains("WordLike"));
        Assert.DoesNotContain(res.NotRestarted, n => n.StartsWith("Wordlike"));
    }

    [Fact]
    public async Task Claude_Desktop_is_reopened_even_while_a_Claude_Code_claude_exe_keeps_running()
    {
        const string cli = @"C:\Users\x\.local\bin\claude.exe";
        const string desktop = @"C:\Users\x\AppData\Local\AnthropicClaude\app-1.0\claude.exe";
        using var env = new TestEnv(ProfileKind.Maximum);
        env.Sys.AddProc("claude", cli, mb: 80);                                // windowless, listed first
        env.Sys.AddProc("claude", desktop, window: true, mb: 600);

        var action = Assert.Single(env.BuildPlan().Actions, a => a.Id == "proc:claude");
        Assert.Equal(desktop, ((ProcessPayload)action.Payload!).Path);         // the program the user sees is the one that comes back
        await Run(env);
        Assert.DoesNotContain(env.Sys.Procs, p => p.Name == "claude");
        env.Sys.AddProc("claude", cli, mb: 80);                                // later the user starts Claude Code again from a terminal

        env.Orc.Revert(env.Settings);

        Assert.Contains(env.Sys.Mutations, m => m == "restart " + desktop);
        Assert.Contains(env.Sys.Procs, p => p.Path == desktop);
    }

    [Fact]
    public void A_program_closed_in_two_sessions_is_reopened_from_the_newest_path()
    {
        using var env = new TestEnv(ProfileKind.Maximum);
        StateGuard.FileExists = p => p.Contains("app-1.0.2");                    // the old folder was deleted by the self-update
        env.Sys.Procs.RemoveAll(p => p.Name == "Discord");
        OpenJournal(env,
            new ProcessRecord { Name = "discord", Path = @"C:\Users\x\Discord\app-1.0.1\Discord.exe", CanReopen = true },
            new ProcessRecord { Name = "discord", Path = @"C:\Users\x\Discord\app-1.0.2\Discord.exe", CanReopen = true });

        var res = env.Restart().Revert(env.Settings);

        Assert.True(res.Complete);
        Assert.Empty(res.NotRestarted);
        Assert.Single(env.Sys.Procs, p => p.Name == "Discord");
        Assert.Contains(env.Sys.Mutations, m => m.Contains("app-1.0.2"));
        Assert.DoesNotContain(env.Sys.Mutations, m => m.Contains("app-1.0.1"));
    }

    // ------------------------------------------------------------ what cannot be started is reported, never dropped

    [Fact]
    public void Programs_that_cannot_be_started_are_listed_and_the_rest_is_still_reopened()
    {
        using var env = new TestEnv(ProfileKind.Maximum);
        StateGuard.FileExists = p => !p.Contains(@"\gone\");
        env.Sys.FailRestartPaths.Add(@"C:\Tools\blocked.exe");
        OpenJournal(env,
            new ProcessRecord { Name = "gonetool", Path = @"C:\Tools\gone\gonetool.exe", CanReopen = true },
            new ProcessRecord { Name = "storething", Path = @"C:\Program Files\WindowsApps\V.Store_1\storething.exe", CanReopen = true },
            new ProcessRecord { Name = "blocked", Path = @"C:\Tools\blocked.exe", CanReopen = true },
            new ProcessRecord { Name = "goodtool", Path = @"C:\Tools\goodtool.exe", CanReopen = true });

        var res = env.Restart().Revert(env.Settings);

        Assert.True(res.Complete);
        Assert.Contains("Gonetool", res.NotRestarted);                                  // readable names, not "gonetool.exe"
        Assert.Contains("Storething (Store uygulaması)", res.NotRestarted);
        Assert.Contains("Blocked", res.NotRestarted);
        Assert.Contains(env.Sys.Procs, p => p.Name == "goodtool");
        Assert.Single(res.Restored);
        Assert.False(new SessionStore().HasPendingSession());
    }

    // ------------------------------------------------------------ the "yeniden açsın" switch is read when restoring

    [Fact]
    public async Task Switching_reopening_off_after_Clear_is_honoured_by_the_restore()
    {
        using var env = new TestEnv(ProfileKind.Maximum);
        await Run(env);
        env.Settings.RestartClosedApps = false;

        var res = env.Orc.Revert(env.Settings);

        Assert.True(res.Complete);
        Assert.DoesNotContain(env.Sys.Procs, p => p.Name is "OneDrive" or "chrome" or "Spotify");
        Assert.Empty(res.NotRestarted);
    }

    [Fact]
    public async Task Switching_reopening_on_after_Clear_still_reopens_what_was_closed_while_it_was_off()
    {
        using var env = new TestEnv(ProfileKind.Maximum);
        env.Settings.RestartClosedApps = false;
        await Run(env);
        env.Settings.RestartClosedApps = true;

        env.Orc.Revert(env.Settings);

        Assert.Contains(env.Sys.Procs, p => p.Name == "OneDrive");
        Assert.Contains(env.Sys.Procs, p => p.Name == "chrome");
    }

    // ------------------------------------------------------------ closing from the RAM list is part of the journal

    [Fact]
    public async Task A_program_closed_from_the_memory_list_comes_back_with_the_restore()
    {
        using var env = new TestEnv(ProfileKind.Maximum);
        env.Settings.Options["proc.cloud"] = false;                            // OneDrive/Dropbox stay for the main run
        await Run(env);
        Assert.Contains(env.Sys.Procs, p => p.Name == "OneDrive");

        Assert.True(env.Orc.CloseByUser("OneDrive", streamMode: false));
        Assert.DoesNotContain(env.Sys.Procs, p => p.Name == "OneDrive");

        env.Orc.Revert(env.Settings);

        Assert.Contains(env.Sys.Procs, p => p.Name == "OneDrive");
    }

    // ------------------------------------------------------------ a new Clear with other switches

    [Fact]
    public async Task A_new_Clear_reopens_the_deferred_programs_that_its_switches_will_not_close_again()
    {
        using var env = new TestEnv(ProfileKind.Maximum);
        await Run(env);
        Assert.DoesNotContain(env.Sys.Procs, p => p.Name is "chrome" or "OneDrive");

        // the user picks a profile that leaves the browsers alone and presses Clear again
        var orc2 = env.Restart();
        var prev = orc2.Revert(env.Settings, restartApps: false);
        Assert.DoesNotContain(env.Sys.Procs, p => p.Name == "chrome");              // deferred, not reopened by the undo itself
        env.Settings.Options["proc.browsers"] = false;

        var deferred = orc2.ReopenDeferred(prev.Carry, env.Settings);

        Assert.Contains(env.Sys.Procs, p => p.Name == "chrome");                   // reopened now: nothing will close it again
        Assert.DoesNotContain(deferred.Carry, c => c.Name == "chrome");
        Assert.Contains(deferred.Carry, c => c.Name == "onedrive");                // the new run closes this one again
        Assert.DoesNotContain(env.Sys.Procs, p => p.Name == "OneDrive");
    }

    [Fact]
    public async Task Hafif_after_a_Tam_temizlik_run_gives_every_program_back()
    {
        using var env = new TestEnv(ProfileKind.Maximum);
        await Run(env);
        var orc2 = env.Restart();
        var prev = orc2.Revert(env.Settings, restartApps: false);
        env.Settings.ApplyProfile(ProfileKind.Safe);                               // Hafif closes no program at all

        var deferred = orc2.ReopenDeferred(prev.Carry, env.Settings);

        Assert.Contains(env.Sys.Procs, p => p.Name is "chrome" or "OneDrive" or "Spotify");
        Assert.DoesNotContain(deferred.Carry, c => c.CanReopen);                   // only what cannot be started by hand stays behind
    }

    [Fact]
    public void The_switches_decide_whether_a_deferred_program_is_closed_again()
    {
        var s = new AppSettings();
        s.ApplyProfile(ProfileKind.Maximum);
        var onedrive = new ProcessRecord { Name = "onedrive", Path = @"C:\Program Files\OneDrive\OneDrive.exe" };
        Assert.True(Orchestrator.WillBeClosedAgain(onedrive, s));

        s.Options["proc.cloud"] = false;                                            // its group is switched off
        Assert.False(Orchestrator.WillBeClosedAgain(onedrive, s));

        s.Options["proc.cloud"] = true;
        s.NeverClose.Add("OneDrive");                                               // or the user put it on "Hep açık kalsın"
        Assert.False(Orchestrator.WillBeClosedAgain(onedrive, s));
        s.NeverClose.Clear();

        var unknown = new ProcessRecord { Name = "randomtraything", Path = @"C:\Tools\r.exe" };   // no group: only the sweeps close it
        Assert.True(Orchestrator.WillBeClosedAgain(unknown, s));
        s.Options["proc.trayauto"] = false; s.Options["proc.windowed"] = false;
        Assert.False(Orchestrator.WillBeClosedAgain(unknown, s));
        s.ExtraProcesses.Add("randomtraything");                                    // unless it is on "Her seferinde kapatılsın"
        Assert.True(Orchestrator.WillBeClosedAgain(unknown, s));

        s.ApplyProfile(ProfileKind.Stream);                                         // Yayıncı protects Discord whatever the switches say
        Assert.False(Orchestrator.WillBeClosedAgain(new ProcessRecord { Name = "discord", Path = @"C:\x\Discord.exe" }, s));
    }

    // ------------------------------------------------------------ the policy itself, one rule at a time

    [Theory]
    [InlineData("proc.updaters"), InlineData("proc.dpi"), InlineData("proc.bypass"), InlineData("proc.anticheat")]
    public void Programs_of_these_groups_are_never_started_by_hand(string optionId)
    {
        using var env = new TestEnv(ProfileKind.Maximum);                      // makes StateGuard believe every path exists: only the GROUP can say no
        Assert.True(ReopenPolicy.CanReopen("normaltool", @"C:\Tools\normaltool.exe", "proc.cloud"));   // control: the same program in another group can
        Assert.False(ReopenPolicy.CanReopen("normaltool", @"C:\Tools\normaltool.exe", optionId));
    }

    [Theory]
    [InlineData("proc.cloud"), InlineData("proc.browsers"), InlineData("proc.chat"), InlineData("proc.peripherals"), InlineData("proc.claude"),
     InlineData("proc.windowed"), InlineData("proc.extra"), InlineData("proc.trayauto")]
    public void Programs_of_every_other_group_can_be_started_again(string optionId)
    {
        using var env = new TestEnv(ProfileKind.Maximum);                      // makes StateGuard believe every path exists
        Assert.True(ReopenPolicy.CanReopen("normaltool", @"C:\Tools\normaltool.exe", optionId));
    }

    [Theory]
    [InlineData("node"), InlineData("python"), InlineData("pythonw"), InlineData("java"), InlineData("javaw"), InlineData("dotnet"), InlineData("cmd"),
     InlineData("powershell"), InlineData("pwsh"), InlineData("wscript"), InlineData("ruby"), InlineData("bash"), InlineData("git")]
    public void Interpreters_would_only_open_an_empty_console(string name)
    {
        using var env = new TestEnv(ProfileKind.Maximum);
        Assert.False(ReopenPolicy.CanReopen(name, @"C:\Tools\" + name + ".exe", "proc.trayauto"));
    }

    [Theory]
    [InlineData("vendorhelper"), InlineData("vendorcrashreporter"), InlineData("vendorupdater"), InlineData("vendorupdate"), InlineData("vendorservice"),
     InlineData("vendorsvc"), InlineData("vendorbroker"), InlineData("vendorrenderer"), InlineData("vendorsetup"), InlineData("vendorinstaller")]
    public void Helper_parts_are_left_to_the_program_they_belong_to(string name)
    {
        using var env = new TestEnv(ProfileKind.Maximum);
        Assert.False(ReopenPolicy.CanReopen(name, @"C:\Tools\" + name + ".exe", "proc.cloud"));
    }

    // ------------------------------------------------------------ a revert that cannot finish can be given up on

    [Fact]
    public async Task A_revert_that_cannot_finish_keeps_the_journal_and_can_then_be_discarded()
    {
        using var env = new TestEnv(ProfileKind.Safe);
        await Run(env);
        var failing = new Orchestrator(new CrashingSystem(env.Sys, failOn: "setactive"), new SessionStore(), env.Log) { SettleMs = 0 };

        var res = failing.Revert(env.Settings);

        Assert.False(res.Complete);
        Assert.True(failing.SessionActive);                                    // the journal is kept for another try
        failing.DiscardJournal();
        Assert.False(failing.SessionActive);
        Assert.Single(Directory.GetFiles(AppPaths.Root, "state.discarded-*.json"));
        await env.Restart().PrepareAsync(env.BuildPlan(), env.Settings, CancellationToken.None);   // not blocked any more
    }

    // ------------------------------------------------------------ the restore says only what it knows

    [Fact]
    public async Task The_restore_does_not_claim_everything_is_back_when_programs_have_to_be_opened_by_hand()
    {
        using var env = new TestEnv(ProfileKind.Maximum);
        await Run(env);                       // closes GoogleUpdate among others: an updater is never started by hand
        var res = env.Orc.Revert(env.Settings);

        Assert.True(res.Complete);
        var lines = env.Log.Lines;
        var closing = lines.FindIndex(l => l.StartsWith("I") && l.Contains("Ayarlar ve hizmetler eski haline döndü."));
        Assert.True(closing >= 0, "closing line is missing");
        Assert.DoesNotContain(lines, l => l.Contains("Her şey eski haline döndü"));
        Assert.DoesNotContain(lines, l => l.Contains("yeniden açıldı"));                       // "başlatıldı": the start itself is not verified
        Assert.Contains(lines, l => l.StartsWith("I") && l.Contains("başlatıldı"));
        Assert.Contains(res.Restored, r => r.EndsWith("başlatıldı"));
        var warn = lines.FindIndex(l => l.StartsWith("W") && l.Contains("kendiliğinden açılamadı"));
        Assert.True(warn > closing, "the warning that names the programs comes after the closing line");
        Assert.Contains("Googleupdate", lines[warn]);
    }

    [Fact]
    public async Task The_restore_has_no_hand_open_warning_when_everything_was_started()
    {
        using var env = new TestEnv(ProfileKind.Safe);
        await Run(env);
        var res = env.Orc.Revert(env.Settings);

        Assert.Empty(res.NotRestarted);
        Assert.Contains(env.Log.Lines, l => l.Contains("Ayarlar ve hizmetler eski haline döndü."));
        Assert.DoesNotContain(env.Log.Lines, l => l.StartsWith("W"));
    }

    // ------------------------------------------------------------ the closed-programs list is never only in memory

    [Fact]
    public async Task A_quiet_restore_leaves_a_journal_with_only_the_closed_programs_so_a_crash_before_the_next_run_loses_nothing()
    {
        using var env = new TestEnv(ProfileKind.Maximum);
        env.Settings.Options["proc.browsers"] = false;
        await Run(env);
        Assert.DoesNotContain(env.Sys.Procs, p => p.Name == "OneDrive");
        Assert.Equal(ServiceRunState.Stopped, env.Sys.Services["wuauserv"].State);

        var prev = env.Restart().Revert(env.Settings, restartApps: false);

        Assert.True(prev.Complete);
        Assert.Equal(ServiceRunState.Running, env.Sys.Services["wuauserv"].State);            // the settings really went back
        Assert.Contains(env.Log.Lines, l => l.Contains("Önceki ayarlar eski haline döndü."));
        Assert.DoesNotContain(env.Sys.Procs, p => p.Name == "OneDrive");                      // programs are deferred
        var onDisk = new SessionStore().LoadPending();                                        // the journal is NOT gone...
        Assert.NotNull(onDisk);
        Assert.Contains(onDisk!.Processes, p => p.Name == "onedrive" && p.CanReopen);
        Assert.Equal(prev.Carry.Count, onDisk.Processes.Count);
        Assert.Empty(onDisk.Services); Assert.Empty(onDisk.Registry); Assert.Empty(onDisk.NicPower);   // ...and holds nothing else
        Assert.Null(onDisk.Mouse); Assert.Null(onDisk.Accessibility);
        Assert.Null(onDisk.OriginalPowerGuid); Assert.Null(onDisk.CreatedPowerGuid);

        // --- the app is killed right here: the next run never starts. A fresh app still reopens everything ---
        var orc3 = env.Restart();
        Assert.True(orc3.SessionActive);
        var res = orc3.Revert(env.Settings);
        Assert.True(res.Complete, string.Join("; ", res.Problems));
        Assert.Contains(env.Sys.Procs, p => p.Name == "OneDrive");
        Assert.Contains(env.Sys.Procs, p => p.Name == "Dropbox");
        Assert.False(File.Exists(AppPaths.StateFile));
    }

    [Fact]
    public async Task The_run_after_a_quiet_restore_takes_over_that_journal_and_the_programs_are_on_disk_at_every_step()
    {
        using var env = new TestEnv(ProfileKind.Maximum);
        env.Settings.Options["proc.browsers"] = false;
        await Run(env);
        var orc2 = env.Restart();
        var prev = orc2.Revert(env.Settings, restartApps: false);

        var checks = 0;
        var alwaysThere = true;
        env.Sys.OnMutation = _ =>
        {
            checks++;
            var d = new SessionStore().LoadPending();                                         // what a crash at this very moment would leave
            if (d == null || !d.Processes.Any(p => p.Name == "onedrive")) alwaysThere = false;
        };
        await orc2.PrepareAsync(env.BuildPlan(), env.Settings, CancellationToken.None, prev.Carry);

        Assert.True(checks > 0);
        Assert.True(alwaysThere);
        var final = orc2.Revert(env.Settings);
        Assert.True(final.Complete, string.Join("; ", final.Problems));
        Assert.Contains(env.Sys.Procs, p => p.Name == "OneDrive");
    }

    [Fact]
    public async Task Only_a_journal_of_closed_programs_is_taken_over_a_real_session_still_has_to_be_restored_first()
    {
        using var env = new TestEnv(ProfileKind.Safe);
        await Run(env);                                                                       // services, power plan, registry... are in the journal
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            env.Orc.PrepareAsync(env.BuildPlan(), env.Settings, CancellationToken.None, Array.Empty<ProcessRecord>()));
    }

    // ------------------------------------------------------------ closing from the memory list always leaves a journal

    [Fact]
    public void A_program_closed_from_the_memory_list_without_any_session_starts_one_so_Geri_yukle_can_reopen_it()
    {
        using var env = new TestEnv(ProfileKind.Maximum);
        Assert.False(env.Orc.SessionActive);

        Assert.True(env.Orc.CloseByUser("OneDrive", streamMode: false));

        Assert.DoesNotContain(env.Sys.Procs, p => p.Name == "OneDrive");
        Assert.True(env.Orc.SessionActive);
        Assert.True(File.Exists(AppPaths.StateFile));
        var res = env.Restart().Revert(env.Settings);                                         // a new app start: only the file is left
        Assert.True(res.Complete);
        Assert.Contains(env.Sys.Procs, p => p.Name == "OneDrive");
        Assert.False(File.Exists(AppPaths.StateFile));
    }

    [Fact]
    public void Closing_from_the_memory_list_adds_to_a_journal_that_is_waiting_on_disk()
    {
        using var env = new TestEnv(ProfileKind.Maximum);
        OpenJournal(env, new ProcessRecord { Name = "toola", Path = @"C:\Tools\toola.exe", CanReopen = true });
        var orc = env.Restart();                                                              // nothing loaded yet: the journal is only a file

        Assert.True(orc.CloseByUser("Dropbox", streamMode: false));

        var onDisk = new SessionStore().LoadPending()!;
        Assert.Contains(onDisk.Processes, p => p.Name == "toola");                            // the earlier record was not replaced
        Assert.Contains(onDisk.Processes, p => p.Name == "dropbox");
    }

    [Fact]
    public void Nothing_is_journaled_for_a_close_that_does_not_happen()
    {
        using var env = new TestEnv(ProfileKind.Maximum);
        Assert.False(env.Orc.CloseByUser("no-such-program", false));                          // not running
        Assert.False(env.Orc.CloseByUser("steam", false));                                    // protected
        env.Sys.AddProc("cs2");
        Assert.False(env.Orc.CloseByUser("OneDrive", false));                                 // the game gate
        Assert.Contains(env.Sys.Procs, p => p.Name == "OneDrive");
        Assert.False(File.Exists(AppPaths.StateFile));
        Assert.False(env.Orc.SessionActive);
    }

    // ------------------------------------------------------------ journals written by 2.0.0 and earlier

    [Fact]
    public void A_journal_with_the_old_network_card_values_of_the_removed_Nagle_option_still_reverts()
    {
        using var env = new TestEnv(ProfileKind.Safe);
        const string key = @"SYSTEM\CurrentControlSet\Services\Tcpip\Parameters\Interfaces\{AAAA-BBBB}";
        env.Sys.Keys.Add("LocalMachine|" + key);
        env.Sys.Reg[$"LocalMachine|{key}|TcpAckFrequency"] = new RegValue(RegistryValueKind.DWord, "1");     // written by 2.0.0
        env.Sys.Reg[$"LocalMachine|{key}|TCPNoDelay"] = new RegValue(RegistryValueKind.DWord, "1");
        var st = env.Store.Begin("Maximum");
        st.Registry.Add(new RegistryRecord { Hive = RegHive.LocalMachine, SubKey = key, Name = "TcpAckFrequency", KeyExisted = true, ValueExisted = false, OldKind = RegistryValueKind.DWord });
        st.Registry.Add(new RegistryRecord { Hive = RegHive.LocalMachine, SubKey = key, Name = "TCPNoDelay", KeyExisted = true, ValueExisted = true, OldKind = RegistryValueKind.DWord, OldData = "0" });
        env.Store.Save();

        var res = env.Restart().Revert(env.Settings);

        Assert.True(res.Complete, string.Join("; ", res.Problems));
        Assert.False(env.Sys.Reg.ContainsKey($"LocalMachine|{key}|TcpAckFrequency"));                      // did not exist before: removed again
        Assert.Equal("0", env.Sys.Reg[$"LocalMachine|{key}|TCPNoDelay"].Data);                              // existed before: the old value is back
        Assert.False(File.Exists(AppPaths.StateFile));
    }

    [Fact]
    public void A_journal_with_fields_that_no_longer_exist_still_loads_and_reverts()
    {
        using var env = new TestEnv(ProfileKind.Safe);
        File.WriteAllText(AppPaths.StateFile,
            "{\"Version\": 1, \"StartedUtc\": \"2026-10-01T10:00:00Z\", \"AppPid\": 1234, \"Profile\": \"Maximum\", \"AppliedPowerGuid\": \"8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c\"," +
            " \"Services\": [{\"Name\": \"Spooler\", \"WasRunning\": true, \"StartType\": \"Automatic\", \"Reverted\": false}], \"Somethingnew\": 5}");
        env.Sys.Services["Spooler"] = new FakeService { Display = "Print Spooler", State = ServiceRunState.Stopped, Start = ServiceStart.Automatic };

        var res = env.Restart().Revert(env.Settings);

        Assert.True(res.Complete, string.Join("; ", res.Problems));
        Assert.Equal(ServiceRunState.Running, env.Sys.Services["Spooler"].State);
    }

    // ------------------------------------------------------------ nothing technical reaches the user

    [Fact]
    public async Task A_step_that_throws_shows_a_Turkish_line_and_the_raw_text_goes_to_the_log_file_channel_only()
    {
        using var env = new TestEnv(ProfileKind.Safe);
        var orc = new Orchestrator(new CrashingSystem(env.Sys, failOn: "setactive"), env.Store, env.Log) { SettleMs = 0 };

        var res = await orc.PrepareAsync(env.BuildPlan(), env.Settings, CancellationToken.None);

        Assert.Contains(res.Failed, f => f.StartsWith("Şu adım yapılamadı: "));
        Assert.DoesNotContain(res.Failed, f => f.Contains("boom"));
        Assert.DoesNotContain(env.Log.Lines, l => !l.StartsWith("D") && l.Contains("boom"));   // I / W / E lines are shown to the user
        Assert.Contains(env.Log.Lines, l => l.StartsWith("D") && l.Contains("boom"));          // the detail is kept for the file
    }

    [Fact]
    public async Task A_revert_step_that_throws_is_reported_without_the_raw_exception_text()
    {
        using var env = new TestEnv(ProfileKind.Safe);
        await Run(env);
        var failing = new Orchestrator(new CrashingSystem(env.Sys, failOn: "setactive"), new SessionStore(), env.Log) { SettleMs = 0 };

        var res = failing.Revert(env.Settings);

        Assert.Contains(res.Problems, p => p == "Şu adım yapılamadı: Güç planı");
        Assert.DoesNotContain(env.Log.Lines, l => !l.StartsWith("D") && l.Contains("boom"));
        Assert.Contains(env.Log.Lines, l => l.StartsWith("D") && l.Contains("boom"));
    }
}
