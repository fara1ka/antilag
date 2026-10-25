using CS2Hazirla.Core;
using Xunit;

namespace CS2Hazirla.Tests;

/// <summary>One failing probe must not stop everything, and the registry changes the planner can make must always be revertible.</summary>
[Collection("Sequential")]
public class RobustnessTests
{
    // ------------------------------------------------------------ the planner survives a broken probe

    [Theory]
    [InlineData("GetNics", "İnternet ayarları")]
    [InlineData("GetPower", "Güç planı")]
    [InlineData("GetMouse", "Fare ve klavye ayarları")]
    public void One_broken_probe_skips_only_its_own_step_the_plan_still_closes_programs_and_stops_services(string probe, string step)
    {
        using var env = new TestEnv(ProfileKind.Maximum);
        env.Sys.BrokenProbes.Add(probe);

        var plan = env.BuildPlan();                                              // must not throw

        Assert.Contains(plan.Active, a => a.Kind == ActionKind.CloseProcess);
        Assert.Contains(plan.Active, a => a.Kind == ActionKind.StopService);
        Assert.Contains($"{step} kontrol edilemedi, bu adım atlandı.", plan.Warnings);
        Assert.DoesNotContain(plan.Warnings, w => w.Contains("broken"));        // no raw English in what the user reads
    }

    [Fact]
    public void Every_probe_broken_at_once_still_leaves_processes_services_and_registry_settings()
    {
        using var env = new TestEnv(ProfileKind.Maximum);
        env.Sys.BrokenProbes.UnionWith(new[] { "GetNics", "GetPower", "GetMouse" });

        var plan = env.BuildPlan();

        Assert.Contains(plan.Active, a => a.Kind == ActionKind.CloseProcess);
        Assert.Contains(plan.Active, a => a.Kind == ActionKind.StopService);
        Assert.Contains(plan.Active, a => a.Kind == ActionKind.Registry);
        Assert.Equal(3, plan.Warnings.Count(w => w.EndsWith("kontrol edilemedi, bu adım atlandı.")));
    }

    [Fact]
    public void The_technical_reason_of_a_skipped_step_goes_to_the_log_file_channel()
    {
        using var env = new TestEnv(ProfileKind.Maximum);
        env.Sys.BrokenProbes.Add("GetPower");

        new Planner(env.Sys, env.Log).Build(env.Settings, TestEnv.Cs2Path);

        Assert.Contains(env.Log.Lines, l => l.StartsWith("D") && l.Contains("GetPower is broken"));
        Assert.DoesNotContain(env.Log.Lines, l => !l.StartsWith("D"));
    }

    [Fact]
    public void A_running_game_is_never_swallowed_by_the_guard()
    {
        using var env = new TestEnv(ProfileKind.Maximum);
        env.Sys.BrokenProbes.Add("GetPower");
        env.Sys.ProbeException = new GameRunningException();

        Assert.Throws<GameRunningException>(() => env.BuildPlan());
    }

    // ------------------------------------------------------------ every registry write can be reverted

    [Theory]
    [InlineData(ProfileKind.Safe)]
    [InlineData(ProfileKind.Maximum)]
    [InlineData(ProfileKind.Stream)]
    public async Task Every_registry_value_a_plan_with_all_options_on_writes_is_accepted_by_the_StateGuard(ProfileKind profile)
    {
        using var env = new TestEnv(profile);
        env.Settings.Options = Catalog.Options.ToDictionary(o => o.Id, _ => true);     // a new option cannot slip past the allow-list
        var plan = env.BuildPlan();

        var changes = plan.Actions.Where(a => a.Kind == ActionKind.Registry).SelectMany(a => ((RegistryPayload)a.Payload!).Changes).ToList();
        Assert.True(changes.Count >= 5, "the test machine must make the planner write registry values");
        foreach (var c in changes)
            Assert.True(StateGuard.IsAllowed(new RegistryRecord { Hive = c.Hive, SubKey = c.SubKey, Name = c.Name }),
                $"{c.Hive}\\{c.SubKey}\\{c.Name} is planned but the StateGuard would refuse to revert it");
        Assert.DoesNotContain(changes, c => c.Hive == RegHive.LocalMachine);            // nothing writes to HKLM any more

        // and the journal of a real run holds nothing the revert would refuse
        await env.Orc.PrepareAsync(plan, env.Settings, CancellationToken.None);
        Assert.NotEmpty(env.Store.Current!.Registry);
        Assert.All(env.Store.Current.Registry, r => Assert.True(StateGuard.IsAllowed(r), $"{r.SubKey}\\{r.Name}"));
    }
}
