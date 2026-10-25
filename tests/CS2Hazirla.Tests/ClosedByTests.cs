using CS2Hazirla.Core;
using Xunit;

namespace CS2Hazirla.Tests;

/// <summary>A program that was closed is remembered together with the switch that closed it: that is what decides whether a later run closes it again.</summary>
[Collection("Sequential")]
public class ClosedByTests
{
    [Fact]
    public async Task The_journal_remembers_which_switch_closed_each_program()
    {
        using var env = new TestEnv(ProfileKind.Maximum);
        env.Orc.SettleMs = 0;
        await env.Orc.PrepareAsync(env.BuildPlan(), env.Settings, CancellationToken.None);

        var state = env.Store.Current ?? env.Store.LoadPending();
        Assert.NotNull(state);
        Assert.Contains(state!.Processes, p => p.Name == "onedrive" && p.ClosedBy == "proc.cloud");
        Assert.Contains(state.Processes, p => p.Name == "randomtraything" && p.ClosedBy == "proc.trayauto");
        Assert.Contains(state.Processes, p => p.Name == "discord" && p.ClosedBy == "proc.discord");     // Discord has its own switch now
    }

    [Fact]
    public void An_unknown_program_closed_by_the_tray_sweep_is_not_held_back_when_the_next_run_has_only_the_windowed_sweep()
    {
        var fromTray = new ProcessRecord { Name = "randomtraything", Path = @"C:\Tools\r.exe", ClosedBy = "proc.trayauto" };
        var fromWindowed = new ProcessRecord { Name = "randomwindowthing", Path = @"C:\Tools\w.exe", ClosedBy = "proc.windowed" };
        var oldJournal = new ProcessRecord { Name = "randomtraything", Path = @"C:\Tools\r.exe" };      // written before the field existed

        var s = new AppSettings();
        s.ApplyProfile(ProfileKind.Maximum);
        Assert.True(Orchestrator.WillBeClosedAgain(fromTray, s));
        Assert.True(Orchestrator.WillBeClosedAgain(fromWindowed, s));

        s.ApplyProfile(ProfileKind.Stream);                       // Yayıncı: the tray sweep is off, the windowed sweep stays on
        Assert.False(Orchestrator.WillBeClosedAgain(fromTray, s));    // nothing will close it again: it is reopened instead of staying closed
        Assert.True(Orchestrator.WillBeClosedAgain(fromWindowed, s));
        Assert.True(Orchestrator.WillBeClosedAgain(oldJournal, s));   // no record of the switch: the old rule (either sweep) applies
    }
}
