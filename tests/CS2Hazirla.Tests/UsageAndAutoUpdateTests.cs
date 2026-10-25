using CS2Hazirla.Core;
using Xunit;

namespace CS2Hazirla.Tests;

/// <summary>The refresh-rate note, the numbers of the Kullanım list, and the rule for the automatic update.</summary>
[Collection("language")]
public class UsageAndAutoUpdateTests : IDisposable
{
    public UsageAndAutoUpdateTests() => Loc.Use("tr");
    public void Dispose() => Loc.Use(TestLanguage.Default);

    // ------------------------------------------------------------------ the screen

    [Theory]
    [InlineData(60, 144, true)]
    [InlineData(60, 75, true)]            // 25% more: worth saying
    [InlineData(100, 120, true)]          // exactly 20% more
    [InlineData(144, 165, false)]         // 14.6% more: not worth a sentence
    [InlineData(144, 240, true)]          // a 240 Hz monitor left at 144: a real gain
    [InlineData(360, 540, false)]         // already very fast: nobody is nagged (the rule is "below 200 Hz now")
    [InlineData(144, 144, false)]
    [InlineData(165, 144, false)]         // the "maximum" can never be below the current rate: nothing to say
    [InlineData(1, 144, false)]           // 0 and 1 mean "the driver's default": no number to compare
    [InlineData(0, 0, false)]
    public void The_screen_note_appears_only_when_the_monitor_runs_clearly_below_what_it_can_do(int current, int max, bool expected)
    {
        var d = new DisplayInfo(1920, 1080, current, max);
        Assert.Equal(expected, DisplayAdvice.CanBeFaster(d));
        Assert.Equal(expected, DisplayAdvice.Check(d) != null);
    }

    [Fact]
    public void The_screen_note_names_both_numbers_and_says_what_to_do_and_that_nothing_was_changed_here()
    {
        var text = DisplayAdvice.Check(new DisplayInfo(1920, 1080, 60, 144))!;
        Assert.Contains("144 Hz", text);
        Assert.Contains("60 Hz", text);
        Assert.Contains("Gelişmiş ekran", text);               // where to go in Windows
        Assert.Null(DisplayAdvice.Check(null));                // Windows does not say: no note
    }

    [Fact]
    public void The_planner_puts_the_screen_note_first_and_says_so_for_the_button()
    {
        using var env = new TestEnv(ProfileKind.Maximum);
        env.Sys.Display = new DisplayInfo(2560, 1440, 60, 165);
        var report = new Planner(env.Sys, env.Log).Info();
        Assert.True(report.ScreenCanBeFaster);
        Assert.Contains("165 Hz", report.Lines[0]);
        Assert.Equal(report.Lines, new Planner(env.Sys, env.Log).InfoLines());     // the old entry point still gives the same lines

        env.Sys.Display = new DisplayInfo(2560, 1440, 165, 165);
        Assert.False(new Planner(env.Sys, env.Log).Info().ScreenCanBeFaster);
        Assert.DoesNotContain(new Planner(env.Sys, env.Log).Info().Lines, l => l.Contains("Hz"));
    }

    [Fact]
    public void A_screen_that_cannot_be_read_costs_nothing_the_other_notes_stay()
    {
        using var env = new TestEnv(ProfileKind.Maximum);
        env.Sys.BrokenProbes.Add("GetPrimaryDisplay");
        var report = new Planner(env.Sys, env.Log).Info();
        Assert.False(report.ScreenCanBeFaster);
        Assert.DoesNotContain(report.Lines, l => l.Contains(" Hz"));
    }

    [Fact]
    public void Looking_at_the_screen_changes_nothing()
    {
        using var env = new TestEnv(ProfileKind.Maximum);
        env.Sys.Display = new DisplayInfo(1920, 1080, 60, 144);
        var before = env.Sys.Snapshot();
        env.Sys.Frozen = true;                                  // any mutating call would throw
        _ = new Planner(env.Sys, env.Log).Info();
        Assert.Empty(env.Sys.Mutations);
        Assert.Equal(before, env.Sys.Snapshot());
    }

    // ------------------------------------------------------------------ the numbers of the Kullanım list

    [Fact]
    public void A_rate_is_the_share_of_the_whole_processor_and_the_bytes_per_second()
    {
        // 1.6 s of CPU time over 2 s on 8 logical cores = 10% of the whole processor; 4 MB read and written over 2 s = 2 MB/s
        var (cpu, io) = UsageMath.Rate(new UsageCounters(0, 0), new UsageCounters(16_000_000, 4L << 20), 2.0, 8);
        Assert.Equal(10.0, cpu, 6);
        Assert.Equal(2.0 * 1024 * 1024, io, 3);
    }

    [Fact]
    public void A_rate_never_goes_below_zero_or_above_the_whole_processor_and_a_bad_interval_gives_zero()
    {
        Assert.Equal((0.0, 0.0), UsageMath.Rate(new UsageCounters(500, 500), new UsageCounters(100, 100), 2, 8));      // counters that went back (a restarted pid)
        Assert.Equal(100.0, UsageMath.Rate(new UsageCounters(0, 0), new UsageCounters(1_000_000_000_000, 0), 2, 8).CpuPercent);
        Assert.Equal((0.0, 0.0), UsageMath.Rate(new UsageCounters(0, 0), new UsageCounters(1, 1), 0, 8));
        Assert.Equal((0.0, 0.0), UsageMath.Rate(new UsageCounters(0, 0), new UsageCounters(1, 1), 2, 0));
    }

    [Fact]
    public void The_numbers_read_plainly_in_both_languages()
    {
        Assert.Equal("%0", UsageMath.CpuText(0.01));
        Assert.StartsWith("%3", UsageMath.CpuText(3.4));
        Assert.Equal("%25", UsageMath.CpuText(25.2));
        Assert.Equal("0 KB/sn", UsageMath.RateText(100));
        Assert.Contains("KB/sn", UsageMath.RateText(300 * 1024));
        Assert.Contains("MB/sn", UsageMath.RateText(5 * 1024 * 1024));

        Loc.Use("en");
        Assert.Equal("0%", UsageMath.CpuText(0.01));
        Assert.Equal("3.4%", UsageMath.CpuText(3.4));
        Assert.Equal("25%", UsageMath.CpuText(25.2));
        Assert.Equal("0 KB/s", UsageMath.RateText(100));
        Assert.Equal("300 KB/s", UsageMath.RateText(300 * 1024));
        Assert.Equal("5.0 MB/s", UsageMath.RateText(5 * 1024 * 1024));
    }

    // ------------------------------------------------------------------ the automatic update

    private static UpdateOffer Offer(string version) => new(Version.Parse(version), 60_000_000, new string('a', 64));

    [Fact]
    public void A_fresh_installation_updates_by_itself()
    {
        var s = new AppSettings();
        Assert.True(s.CheckForUpdates);
        Assert.True(s.AutoUpdate);                                 // on by default (the owner's decision)
        Assert.Equal("", s.AutoUpdateTried);
        Assert.True(AutoUpdatePolicy.ShouldInstall(s, Offer("3.6.0"), idle: true));
    }

    [Fact]
    public void Every_switch_that_says_no_is_respected()
    {
        var offer = Offer("3.6.0");
        Assert.False(AutoUpdatePolicy.ShouldInstall(new AppSettings { AutoUpdate = false }, offer, true));          // the user only wants to be told
        Assert.False(AutoUpdatePolicy.ShouldInstall(new AppSettings { CheckForUpdates = false }, offer, true));     // no check, nothing to install
        Assert.False(AutoUpdatePolicy.ShouldInstall(new AppSettings(), offer, idle: false));                        // something is running
        Assert.False(AutoUpdatePolicy.ShouldInstall(new AppSettings(), null, true));                                // no newer version
    }

    [Fact]
    public void A_version_is_tried_by_itself_only_once()
    {
        var s = new AppSettings { AutoUpdateTried = "3.6.0" };
        Assert.False(AutoUpdatePolicy.ShouldInstall(s, Offer("3.6.0"), true));      // the installer failed after the app had closed: no loop at every start
        Assert.True(AutoUpdatePolicy.ShouldInstall(s, Offer("3.6.1"), true));       // a newer version gets its own try
    }

    [Fact]
    public void Only_the_game_that_opened_before_anything_was_transferred_may_be_tried_again_at_the_next_start()
    {
        Assert.True(AutoUpdatePolicy.MayTryAgainAtNextStart(UpdateFlow.FailedGame));
        Assert.False(AutoUpdatePolicy.MayTryAgainAtNextStart(UpdateFlow.FailedDownload));        // a 404 or a wrong size is not the internet: no 60 MB at every start
        Assert.False(AutoUpdatePolicy.MayTryAgainAtNextStart(UpdateFlow.FailedStart));
        Assert.False(AutoUpdatePolicy.MayTryAgainAtNextStart(UpdateFlow.FailedIntegrity));
    }

    [Fact]
    public void Settings_files_of_older_versions_get_the_automatic_update_and_a_choice_to_keep_it_off_is_remembered()
    {
        var dir = Path.Combine(Path.GetTempPath(), "antilag-auto-" + Guid.NewGuid().ToString("N"));
        var before = AppPaths.Root;
        try
        {
            Directory.CreateDirectory(dir);
            AppPaths.Root = dir;
            File.WriteAllText(AppPaths.SettingsFile, "{ \"CheckForUpdates\": true }");                    // a file written before the switch existed
            Assert.True(new SettingsStore().Load().AutoUpdate);
            File.WriteAllText(AppPaths.SettingsFile, "{ \"AutoUpdate\": false, \"AutoUpdateTried\": \"3.6.0\" }");
            var kept = new SettingsStore().Load();
            Assert.False(kept.AutoUpdate);
            Assert.Equal("3.6.0", kept.AutoUpdateTried);
            File.WriteAllText(AppPaths.SettingsFile, "{ \"AutoUpdateTried\": null }");                    // a hand-edited null never crashes
            Assert.Equal("", new SettingsStore().Load().AutoUpdateTried);
        }
        finally { AppPaths.Root = before; try { Directory.Delete(dir, true); } catch { } }
    }
}
