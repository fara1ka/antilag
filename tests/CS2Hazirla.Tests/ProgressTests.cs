using CS2Hazirla.Core;
using Xunit;

namespace CS2Hazirla.Tests;

/// <summary>The ring on the home screen shows exactly what the Orchestrator reports: finished steps out of planned steps.</summary>
[Collection("Sequential")]
public class ProgressTests
{
    [Fact]
    public async Task Progress_counts_finished_steps_up_to_the_planned_total_and_never_goes_backwards()
    {
        using var env = new TestEnv(ProfileKind.Maximum);
        env.Orc.SettleMs = 0;
        var plan = env.BuildPlan();
        var seen = new List<(int Done, int Total)>();
        env.Orc.Progress = (done, total) => seen.Add((done, total));

        await env.Orc.PrepareAsync(plan, env.Settings, CancellationToken.None);

        Assert.NotEmpty(seen);
        var total = plan.Active.Count();
        Assert.All(seen, p => Assert.Equal(total, p.Total));
        Assert.All(seen, p => Assert.InRange(p.Done, 0, total));
        for (var i = 1; i < seen.Count; i++) Assert.True(seen[i].Done >= seen[i - 1].Done, "progress went backwards");
        Assert.Equal(total, seen[^1].Done);                    // everything planned was finished (done, skipped or failed)
    }

    [Fact]
    public async Task A_run_without_a_progress_listener_works_as_before()
    {
        using var env = new TestEnv(ProfileKind.Maximum);
        env.Orc.SettleMs = 0;
        var result = await env.Orc.PrepareAsync(env.BuildPlan(), env.Settings, CancellationToken.None);
        Assert.NotEmpty(result.Done);
    }
}
