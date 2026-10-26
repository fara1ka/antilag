using System.Diagnostics;
using System.Globalization;
using System.Text.RegularExpressions;
using CS2Hazirla.Core;
using Xunit;
using Xunit.Abstractions;

namespace CS2Hazirla.Tests;

/// <summary>
/// How long a title of the check-up may be (a guard against rambling, not a width: the screen wraps a long title). The uptime title carries a number
/// ("Windows 9999 gündür yeniden başlatılmadı" is 40 characters in Turkish, "Windows hasn't restarted in 9999 days" is 37 in English), and 40 holds for it too.
/// </summary>
internal static class HealthTitleLimit
{
    public const int Usual = 40, Uptime = 40;
    public static int For(string id) => id == "uptime" ? Uptime : Usual;
}

/// <summary>The computer check-up: synthetic facts in, the list of items out. Pure; the language is set per test.</summary>
[Collection("language")]
public class HealthReportTests : IDisposable
{
    private const long GiB = 1L << 30;
    private static readonly DateTime Now = new(2026, 10, 26, 12, 0, 0);
    private static readonly Regex TurkishLetter = new("[çğıöşüÇĞİÖŞÜ]", RegexOptions.CultureInvariant);

    public HealthReportTests() => Loc.Use(TestLanguage.Default);
    public void Dispose() => Loc.Use(TestLanguage.Default);

    // ------------------------------------------------------------------ helpers

    private static DiskFact Disk(string drive, long total, long free, string? media = null, bool system = false, bool game = false) =>
        new(drive, total, free, media, system, game);

    private static HealthFacts Facts(IReadOnlyList<DiskFact>? disks = null, double? uptime = null, bool? reboot = null, bool? battery = null,
                                     string? gpu = null, DateTime? gpuDate = null, bool? failing = null) =>
        new(disks ?? Array.Empty<DiskFact>(), uptime, reboot, battery, gpu, gpuDate, failing);

    private static HealthFacts One(DiskFact d) => Facts(new[] { d });

    private static IReadOnlyList<HealthItem> Build(HealthFacts f) => HealthReport.Build(f, Now);

    private static string[] Ids(IEnumerable<HealthItem> items) => items.Select(i => i.Id).ToArray();

    /// <summary>A PC that has something to say about every item.</summary>
    private static HealthFacts Everything() => Facts(
        new[] { Disk("C:", 500 * GiB, 20 * GiB, "SSD", system: true), Disk("D:", 1000 * GiB, 50 * GiB, "HDD", game: true) },
        uptime: 30, reboot: true, battery: true, gpu: "NVIDIA GeForce RTX 5080", gpuDate: new DateTime(2024, 1, 15), failing: true);

    /// <summary>Many different PCs: every level of every item, in every arrangement of the drives.</summary>
    private static IEnumerable<HealthFacts> AllKinds()
    {
        foreach (var freeGiB in new long[] { 400, 90, 40 })            // of 1000 GB: good, note (under 100 GB and under 20 %), warn (under 50 GB and under 10 %)
        {
            var free = freeGiB * GiB;
            yield return Facts(new[] { Disk("C:", 1000 * GiB, free, "SSD", system: true) });
            yield return Facts(new[] { Disk("C:", 1000 * GiB, free, "HDD", system: true, game: true) });
            yield return Facts(new[] { Disk("C:", 1000 * GiB, 400 * GiB, "SSD", system: true), Disk("D:", 1000 * GiB, free, "HDD", game: true) });
        }
        yield return Facts(uptime: 8);
        yield return Facts(uptime: 30);
        yield return Facts(uptime: 365.5);
        yield return Facts(uptime: 9999);
        yield return Facts(uptime: 123456789);          // far out of reach for a real PC: the number shown stops at 9999
        yield return Facts(reboot: true);
        yield return Facts(battery: true);
        yield return Facts(failing: true);
        yield return Facts(gpu: "NVIDIA GeForce RTX 5080", gpuDate: new DateTime(2026, 9, 30));
        yield return Facts(gpu: "NVIDIA GeForce RTX 5080", gpuDate: new DateTime(2022, 5, 1));
        yield return Everything();
    }

    // ------------------------------------------------------------------ nothing known

    [Fact]
    public void Nothing_known_gives_no_items()
    {
        Assert.Empty(Build(Facts()));
        Assert.Empty(Build(new HealthFacts(Array.Empty<DiskFact>(), null, null, null, null, null, null)));
    }

    [Fact]
    public void A_fine_computer_says_nothing_about_restart_battery_disk_health_or_uptime()
    {
        var items = Build(Facts(new[] { Disk("C:", 500 * GiB, 300 * GiB, "SSD", system: true) }, uptime: 2, reboot: false, battery: false, failing: false));
        Assert.Equal(new[] { "space.system" }, Ids(items));
    }

    // ------------------------------------------------------------------ (1) free space

    // The lines: the amount decides, the percentage only while the amount is also smallish.
    //   Warn : free < 15 GB  OR  (free < 10 % AND free < 50 GB)
    //   Note : (not Warn) AND (free < 30 GB  OR  (free < 20 % AND free < 100 GB))
    //   Good : otherwise.   Exactly on a line is not below it.
    [Theory]
    // a 1000 GB disk: under 50 GB it is under 10 % too, from 50 GB the 20 % line only counts below 100 GB
    [InlineData(1000, 1000, 0, HealthLevel.Good)]
    [InlineData(1000, 400, 0, HealthLevel.Good)]
    [InlineData(1000, 200, 0, HealthLevel.Good)]       // exactly 20 % (and over 100 GB)
    [InlineData(1000, 100, 0, HealthLevel.Good)]       // exactly 100 GB: not below the cap of the 20 % line
    [InlineData(1000, 100, -1, HealthLevel.Note)]      // one byte below 100 GB (and below 20 %)
    [InlineData(1000, 51, 0, HealthLevel.Note)]
    [InlineData(1000, 50, 0, HealthLevel.Note)]        // exactly 50 GB: not below the cap of the 10 % line (the old rule warned here)
    [InlineData(1000, 50, -1, HealthLevel.Warn)]       // one byte below 50 GB (and below 10 %)
    [InlineData(1000, 30, 0, HealthLevel.Warn)]
    [InlineData(1000, 15, 0, HealthLevel.Warn)]
    [InlineData(1000, 0, 0, HealthLevel.Warn)]
    // a 400 GB disk: the percentages are exact (40 GB = 10 %, 80 GB = 20 %)
    [InlineData(400, 80, 0, HealthLevel.Good)]         // exactly 20 %: not below
    [InlineData(400, 80, -1, HealthLevel.Note)]
    [InlineData(400, 40, 0, HealthLevel.Note)]         // exactly 10 %: not below
    [InlineData(400, 40, -1, HealthLevel.Warn)]
    // a 100 GB disk: the sizes decide (the percentages are far above them)
    [InlineData(100, 100, 0, HealthLevel.Good)]
    [InlineData(100, 30, 0, HealthLevel.Good)]         // exactly 30 GB (and 30 %)
    [InlineData(100, 30, -1, HealthLevel.Note)]
    [InlineData(100, 15, 0, HealthLevel.Note)]         // exactly 15 GB (and 15 %): not below
    [InlineData(100, 15, -1, HealthLevel.Warn)]
    [InlineData(100, 10, 0, HealthLevel.Warn)]
    // a 2000 GB disk: 150 GB is much, even if it is only 7.5 % (the old rule warned here)
    [InlineData(2000, 400, 0, HealthLevel.Good)]
    [InlineData(2000, 150, 0, HealthLevel.Good)]
    [InlineData(2000, 100, 0, HealthLevel.Good)]
    [InlineData(2000, 100, -1, HealthLevel.Note)]
    [InlineData(2000, 50, 0, HealthLevel.Note)]
    [InlineData(2000, 50, -1, HealthLevel.Warn)]
    [InlineData(2000, 49, 0, HealthLevel.Warn)]
    // the cases that were wrong or are the edges of the new rule
    [InlineData(740, 70, 0, HealthLevel.Note)]         // 9.5 %, but 70 GB is plenty for updates: not a warning
    [InlineData(2048, 190, 0, HealthLevel.Good)]       // 9.3 %, but 190 GB
    [InlineData(600, 45, 0, HealthLevel.Warn)]         // 7.5 % and under 50 GB
    [InlineData(556, 50, 0, HealthLevel.Note)]         // exactly 50 GB at 9 %: not below 50 GB
    [InlineData(556, 50, -1, HealthLevel.Warn)]
    [InlineData(526, 100, 0, HealthLevel.Good)]        // exactly 100 GB at 19 %: not below 100 GB
    [InlineData(526, 100, -1, HealthLevel.Note)]
    public void The_free_space_of_the_system_drive_follows_the_lines(int totalGiB, int freeGiB, int extraBytes, HealthLevel expected)
    {
        var item = Assert.Single(Build(One(Disk("C:", totalGiB * GiB, freeGiB * GiB + extraBytes, system: true))));
        Assert.Equal("space.system", item.Id);
        Assert.Equal(expected, item.Level);
        if (expected == HealthLevel.Good) { Assert.Null(item.ActionUri); Assert.Null(item.ActionText); }
        else { Assert.Equal("ms-settings:storagesense", item.ActionUri); Assert.False(string.IsNullOrWhiteSpace(item.ActionText)); }
    }

    [Theory]
    // on a small drive (100 GB) only the sizes count: the 15 GB and the 30 GB line, with fractions
    [InlineData(14.9, HealthLevel.Warn)]
    [InlineData(15.0, HealthLevel.Note)]
    [InlineData(29.9, HealthLevel.Note)]
    [InlineData(30.0, HealthLevel.Good)]
    public void On_a_small_drive_the_15_and_30_GB_lines_decide(double freeGiB, HealthLevel expected)
    {
        var item = Assert.Single(Build(One(Disk("C:", 100 * GiB, (long)(freeGiB * GiB), system: true))));
        Assert.Equal(expected, item.Level);
    }

    [Theory]
    [InlineData(1000, 100, 0, HealthLevel.Good)]
    [InlineData(1000, 100, -1, HealthLevel.Note)]
    [InlineData(1000, 50, 0, HealthLevel.Note)]
    [InlineData(1000, 50, -1, HealthLevel.Warn)]
    [InlineData(740, 70, 0, HealthLevel.Note)]
    [InlineData(100, 15, 0, HealthLevel.Note)]
    [InlineData(100, 15, -1, HealthLevel.Warn)]
    public void The_game_drive_follows_the_same_lines(int totalGiB, int freeGiB, int extraBytes, HealthLevel expected)
    {
        var facts = Facts(new[] { Disk("C:", 1000 * GiB, 900 * GiB, system: true), Disk("D:", totalGiB * GiB, freeGiB * GiB + extraBytes, game: true) });
        var item = Assert.Single(Build(facts), i => i.Id == "space.game");
        Assert.Equal(expected, item.Level);
        Assert.Equal(expected == HealthLevel.Good ? null : "ms-settings:storagesense", item.ActionUri);
    }

    [Fact]
    public void A_big_drive_with_70_GB_free_is_a_note_that_does_not_say_very_low_or_not_fitting()
    {
        // C: with 70 GB free of 740 GB (9.5 %) was told "very low, updates may not fit" with a red chip
        var facts = One(Disk("C:", 740 * GiB, 70 * GiB, system: true));
        foreach (var language in new[] { "tr", "en" })
        {
            Loc.Use(language);
            var item = Assert.Single(Build(facts));
            Assert.Equal(HealthLevel.Note, item.Level);
            Assert.DoesNotContain(language == "tr" ? "çok az" : "very low", item.Text);
            Assert.DoesNotContain(language == "tr" ? "sığma" : "not fit", item.Text);
        }
    }

    [Fact]
    public void The_texts_of_the_three_levels_differ_for_each_kind_of_drive()
    {
        foreach (var language in new[] { "tr", "en" })
        {
            Loc.Use(language);
            foreach (var kind in new[] { "system", "system+game", "game" })
            {
                var texts = new List<string>();
                foreach (var freeGiB in new long[] { 400, 90, 40 })          // of 1000 GB: good, note, warn
                {
                    var c = Disk("C:", 1000 * GiB, kind == "game" ? 900 * GiB : freeGiB * GiB, system: true, game: kind == "system+game");
                    var facts = kind == "game" ? Facts(new[] { c, Disk("D:", 1000 * GiB, freeGiB * GiB, game: true) }) : One(c);
                    texts.Add(Build(facts).Single(i => i.Id == (kind == "game" ? "space.game" : "space.system")).Text);
                }
                Assert.Equal(3, texts.Distinct().Count());
            }
        }
    }

    [Fact]
    public void The_warn_texts_are_true_for_every_drive_that_can_reach_them()
    {
        // The two kinds of drive that reach Warn: a small one with under 15 GB free (an update may really not fit) and a huge one that is under 10 % but still
        // has 49 GB free (an update fits). The text may only say that the free space is low and that room may be limited: nothing that is not measured.
        var small = Disk("C:", 20 * GiB, 5 * GiB, system: true);
        var huge = Disk("C:", 2048 * GiB, 49 * GiB, system: true);
        foreach (var language in new[] { "tr", "en" })
        {
            Loc.Use(language);
            var forbidden = language == "tr"
                ? new[] { "sığma", "yavaş", "çok az", "!" }
                : new[] { "fit", "slow", "very low", "!" };
            var texts = new List<string>();
            foreach (var drive in new[] { small, huge })
            {
                var systemOnly = Assert.Single(Build(One(drive)));
                var systemAndGame = Assert.Single(Build(One(drive with { HoldsGame = true })));
                var gameOnly = Assert.Single(Build(Facts(new[] { Disk("C:", 1000 * GiB, 900 * GiB, system: true), drive with { Drive = "D:", IsSystemDrive = false, HoldsGame = true } })), i => i.Id == "space.game");
                foreach (var item in new[] { systemOnly, systemAndGame, gameOnly })
                {
                    Assert.Equal(HealthLevel.Warn, item.Level);
                    foreach (var word in forbidden) Assert.DoesNotContain(word, item.Text);
                    Assert.True(item.Text.Length <= 180);
                    Assert.True(Regex.Matches(item.Text, @"[.!?](\s|$)").Count is >= 1 and <= 2, item.Text);
                    texts.Add(item.Text);
                }
            }
            Assert.Equal(3, texts.Distinct().Count());              // one text per kind of drive, the same for the small and the huge drive
        }
    }

    [Fact]
    public void The_system_drive_item_says_the_drive_and_the_free_space_in_both_languages()
    {
        var facts = One(Disk("C:", 500 * GiB, 215 * GiB, system: true));
        Loc.Use("tr");
        var tr = Assert.Single(Build(facts));
        Assert.Equal("C: 215 GB boş", tr.Title);
        Assert.Equal(HealthLevel.Good, tr.Level);
        Loc.Use("en");
        var en = Assert.Single(Build(facts));
        Assert.Equal("C: 215 GB free", en.Title);
        Assert.Equal(HealthLevel.Good, en.Level);
    }

    [Theory]
    [InlineData(0.04, "0,0 GB", "0.0 GB")]
    [InlineData(12.34, "12,3 GB", "12.3 GB")]
    [InlineData(99.94, "99,9 GB", "99.9 GB")]
    [InlineData(99.96, "100 GB", "100 GB")]            // rounds to 100: no decimal then
    [InlineData(100.0, "100 GB", "100 GB")]
    [InlineData(215.0, "215 GB", "215 GB")]
    [InlineData(1862.4, "1862 GB", "1862 GB")]
    public void Sizes_have_one_decimal_below_100_GB_and_none_from_100_GB(double gb, string turkish, string english)
    {
        var facts = One(Disk("C:", 4000 * GiB, (long)(gb * GiB), system: true));
        Loc.Use("tr");
        Assert.Equal($"C: {turkish} boş", Assert.Single(Build(facts)).Title);
        Loc.Use("en");
        Assert.Equal($"C: {english} free", Assert.Single(Build(facts)).Title);
    }

    [Fact]
    public void The_game_drive_has_its_own_item_only_when_it_is_another_drive()
    {
        var c = Disk("C:", 500 * GiB, 215 * GiB, "SSD", system: true);
        var d = Disk("D:", 1000 * GiB, 400 * GiB, "SSD", game: true);

        Loc.Use("tr");
        var two = Build(Facts(new[] { c, d }));
        Assert.Equal(new[] { "space.system", "space.game" }, Ids(two));
        Assert.Equal("C: 215 GB boş", two[0].Title);
        Assert.Equal("Oyun diski D: 400 GB boş", two[1].Title);
        Loc.Use("en");
        Assert.Equal("Game drive D: 400 GB free", Build(Facts(new[] { c, d }))[1].Title);

        // the game on the system drive itself: one item
        Assert.Equal(new[] { "space.system" }, Ids(Build(Facts(new[] { c with { HoldsGame = true }, d with { HoldsGame = false } }))));
        // the game nowhere known: only the system drive
        Assert.Equal(new[] { "space.system" }, Ids(Build(Facts(new[] { c, d with { HoldsGame = false } }))));
    }

    [Fact]
    public void The_game_drive_is_shown_even_when_the_system_drive_is_not_known()
    {
        var facts = Facts(new[] { Disk("D:", 1000 * GiB, 400 * GiB, game: true), Disk("E:", 1000 * GiB, 10 * GiB) });
        Assert.Equal(new[] { "space.game" }, Ids(Build(facts)));          // E: is neither Windows' nor the game's drive: not shown
    }

    [Fact]
    public void The_text_of_the_system_drive_names_the_games_only_when_they_are_on_it()
    {
        var c = Disk("C:", 500 * GiB, 215 * GiB, system: true);
        Loc.Use("tr");
        Assert.Equal("Windows için yeterince yer var.", Assert.Single(Build(One(c))).Text);
        Assert.Equal("Windows ve oyunlar için yeterince yer var.", Assert.Single(Build(One(c with { HoldsGame = true }))).Text);
        Loc.Use("en");
        Assert.Equal("There is plenty of room for Windows.", Assert.Single(Build(One(c))).Text);
        Assert.Equal("There is plenty of room for Windows and your games.", Assert.Single(Build(One(c with { HoldsGame = true }))).Text);
    }

    [Fact]
    public void Odd_sizes_never_throw_and_never_make_up_an_item()
    {
        Assert.Empty(Build(One(Disk("C:", 0, 0, system: true))));
        Assert.Empty(Build(One(Disk("C:", -5, 10, system: true))));

        Loc.Use("tr");
        var over = Assert.Single(Build(One(Disk("C:", 100 * GiB, 500 * GiB, system: true))));    // more free than the disk has: cut to the disk
        Assert.Equal(HealthLevel.Good, over.Level);
        Assert.Equal("C: 100 GB boş", over.Title);
        var under = Assert.Single(Build(One(Disk("C:", 100 * GiB, -1, system: true))));          // less than nothing: nothing
        Assert.Equal(HealthLevel.Warn, under.Level);
        Assert.Equal("C: 0,0 GB boş", under.Title);
    }

    // ------------------------------------------------------------------ (2) the game on a hard disk

    [Fact]
    public void The_game_on_a_hard_disk_is_a_note_without_a_button()
    {
        var c = Disk("C:", 500 * GiB, 215 * GiB, "SSD", system: true);
        var d = Disk("D:", 1000 * GiB, 400 * GiB, "HDD", game: true);

        Loc.Use("tr");
        var items = Build(Facts(new[] { c, d }));
        Assert.Equal(new[] { "space.system", "space.game", "game.hdd" }, Ids(items));
        var hdd = items[2];
        Assert.Equal(HealthLevel.Note, hdd.Level);
        Assert.Equal("Oyun D: diskinde (HDD)", hdd.Title);
        Assert.Null(hdd.ActionUri);
        Assert.Null(hdd.ActionText);
        Loc.Use("en");
        Assert.Equal("The game is on an HDD (D:)", Build(Facts(new[] { c, d }))[2].Title);
    }

    [Fact]
    public void The_hard_disk_note_needs_a_game_that_is_on_a_known_hard_disk()
    {
        var c = Disk("C:", 500 * GiB, 215 * GiB, system: true);
        Assert.DoesNotContain("game.hdd", Ids(Build(One(c with { MediaType = "SSD", HoldsGame = true }))));       // an SSD
        Assert.DoesNotContain("game.hdd", Ids(Build(One(c with { MediaType = null, HoldsGame = true }))));        // not known
        Assert.DoesNotContain("game.hdd", Ids(Build(One(c with { MediaType = "HDD", HoldsGame = false }))));      // an HDD, but not the game's
        Assert.Contains("game.hdd", Ids(Build(One(c with { MediaType = "HDD", HoldsGame = true }))));             // the system drive is the game's drive
        Assert.Contains("game.hdd", Ids(Build(One(c with { MediaType = "hdd", HoldsGame = true }))));
    }

    [Fact]
    public void The_hard_disk_note_does_not_depend_on_the_size_of_the_disk()
    {
        // a disk with no size is no space item, but the game is still on a hard disk
        var items = Build(Facts(new[] { Disk("D:", 0, 0, "HDD", game: true) }));
        Assert.Equal(new[] { "game.hdd" }, Ids(items));
    }

    // ------------------------------------------------------------------ (3) disk health

    [Fact]
    public void A_predicted_disk_failure_is_a_warning_and_only_then()
    {
        Loc.Use("tr");
        var item = Assert.Single(Build(Facts(failing: true)));
        Assert.Equal("disk.health", item.Id);
        Assert.Equal(HealthLevel.Warn, item.Level);
        Assert.Equal("Disk için arıza uyarısı", item.Title);
        Assert.Null(item.ActionUri);
        Assert.Contains("yedekle", item.Text);
        Assert.Empty(Build(Facts(failing: false)));
        Assert.Empty(Build(Facts(failing: null)));

        Loc.Use("en");
        var en = Assert.Single(Build(Facts(failing: true)));
        Assert.Contains("back up", en.Text);
        Assert.Contains("doesn't mean it's broken", en.Text);        // a prediction, not a verdict
    }

    // ------------------------------------------------------------------ (4) uptime

    [Theory]
    [InlineData(0.0)]
    [InlineData(0.5)]
    [InlineData(6.99)]
    [InlineData(6.9999999)]
    [InlineData(-3.0)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void Less_than_7_days_or_no_number_is_nothing_to_say(double days)
    {
        Assert.Empty(Build(Facts(uptime: days)));
    }

    [Theory]
    [InlineData(7.0, HealthLevel.Note)]
    [InlineData(7.01, HealthLevel.Note)]
    [InlineData(14.0, HealthLevel.Note)]
    [InlineData(20.99, HealthLevel.Note)]
    [InlineData(20.9999999, HealthLevel.Note)]
    [InlineData(21.0, HealthLevel.Warn)]
    [InlineData(21.5, HealthLevel.Warn)]
    [InlineData(120.0, HealthLevel.Warn)]
    public void Uptime_is_a_note_from_7_days_and_a_warning_from_21_days(double days, HealthLevel expected)
    {
        var item = Assert.Single(Build(Facts(uptime: days)));
        Assert.Equal("uptime", item.Id);
        Assert.Equal(expected, item.Level);
        Assert.Null(item.ActionUri);
    }

    [Theory]
    [InlineData(7.0, 7)]
    [InlineData(7.9, 7)]
    [InlineData(21.0, 21)]
    [InlineData(100.5, 100)]
    [InlineData(9999.4, 9999)]
    [InlineData(12345.0, 9999)]                 // out of reach for a real PC: the number stops at 9999 so that the title keeps its length
    [InlineData(123456789.0, 9999)]
    public void The_title_counts_whole_days_since_the_last_restart_in_both_languages(double days, int whole)
    {
        // The tick counter is the time since Windows last really started: with Fast Startup "Shut down" does not reset it. So the title never says the PC "was on".
        Loc.Use("tr");
        Assert.Equal($"Windows {whole} gündür yeniden başlatılmadı", Assert.Single(Build(Facts(uptime: days))).Title);
        Loc.Use("en");
        Assert.Equal($"Windows hasn't restarted in {whole} days", Assert.Single(Build(Facts(uptime: days))).Title);
    }

    [Theory]
    [InlineData("tr")]
    [InlineData("en")]
    public void The_uptime_title_never_says_the_PC_was_on_and_keeps_its_room_for_any_day_count(string language)
    {
        Loc.Use(language);
        foreach (var days in new[] { 7.0, 8.0, 21.0, 99.0, 100.0, 365.0, 1000.0, 9999.0, 10000.0, 1e9, double.MaxValue })
        {
            var item = Assert.Single(Build(Facts(uptime: days)));
            Assert.True(item.Title.Length <= HealthTitleLimit.Uptime, $"[{language}] {item.Title.Length} characters: {item.Title}");
            Assert.DoesNotContain("açık", item.Title);
            Assert.DoesNotContain("been on", item.Title);
        }
    }

    [Fact]
    public void The_uptime_text_advises_a_restart_and_says_that_shutting_down_may_not_be_enough()
    {
        Loc.Use("en");
        var note = Assert.Single(Build(Facts(uptime: 8))).Text;
        var warn = Assert.Single(Build(Facts(uptime: 30))).Text;
        Assert.Equal("Restarting clears memory and finishes any waiting updates; shutting down and starting again may not be enough.", note);
        Assert.Equal("That's quite a long time. Choose Restart from the Start menu: memory is cleared and waiting updates are finished.", warn);
        Assert.NotEqual(note, warn);

        Loc.Use("tr");
        Assert.Equal("Yeniden başlatmak belleği temizler ve bekleyen güncellemeleri bitirir; kapatıp açmak yetmeyebilir.", Assert.Single(Build(Facts(uptime: 8))).Text);
        Assert.Equal("Bu epey uzun bir süre. Başlat menüsünden Yeniden başlat'ı seç: bellek temizlenir, bekleyen güncellemeler biter.", Assert.Single(Build(Facts(uptime: 30))).Text);
    }

    [Fact]
    public void The_uptime_levels_and_thresholds_are_unchanged_by_the_new_wording()
    {
        Assert.Equal(HealthLevel.Note, Assert.Single(Build(Facts(uptime: 7))).Level);
        Assert.Equal(HealthLevel.Note, Assert.Single(Build(Facts(uptime: 20.99))).Level);
        Assert.Equal(HealthLevel.Warn, Assert.Single(Build(Facts(uptime: 21))).Level);
        Assert.Empty(Build(Facts(uptime: 6.99)));
    }

    // ------------------------------------------------------------------ (5) restart pending

    [Fact]
    public void A_pending_restart_is_a_warning_without_a_button_and_only_when_true()
    {
        Loc.Use("tr");
        var item = Assert.Single(Build(Facts(reboot: true)));
        Assert.Equal("reboot", item.Id);
        Assert.Equal(HealthLevel.Warn, item.Level);
        Assert.Equal("Windows yeniden başlatma bekliyor", item.Title);
        Assert.Null(item.ActionUri);
        Assert.Null(item.ActionText);
        Assert.Empty(Build(Facts(reboot: false)));
        Assert.Empty(Build(Facts(reboot: null)));
        Loc.Use("en");
        Assert.Equal("Windows is waiting for a restart", Assert.Single(Build(Facts(reboot: true))).Title);
    }

    // ------------------------------------------------------------------ (6) battery

    [Fact]
    public void Running_on_battery_is_a_note_with_the_power_settings_button()
    {
        Loc.Use("tr");
        var item = Assert.Single(Build(Facts(battery: true)));
        Assert.Equal("battery", item.Id);
        Assert.Equal(HealthLevel.Note, item.Level);
        Assert.Equal("ms-settings:powersleep", item.ActionUri);
        Assert.Equal("Güç ayarlarını aç", item.ActionText);
        Loc.Use("en");
        var en = Assert.Single(Build(Facts(battery: true)));
        Assert.Equal("Open power settings", en.ActionText);
        Assert.Equal("Your PC is running on battery", en.Title);
    }

    [Fact]
    public void A_desktop_PC_has_no_battery_item()
    {
        Assert.Empty(Build(Facts(battery: false)));
        Assert.Empty(Build(Facts(battery: null)));
    }

    // ------------------------------------------------------------------ (7) graphics card driver

    [Theory]
    [InlineData(2026, 9, 30, HealthLevel.Good)]
    [InlineData(2026, 10, 26, HealthLevel.Good)]
    [InlineData(2027, 1, 1, HealthLevel.Good)]          // a date in the future (wrong clock?) is not "old"
    [InlineData(2025, 10, 27, HealthLevel.Good)]
    [InlineData(2025, 10, 26, HealthLevel.Good)]        // exactly 12 months old: not older
    [InlineData(2025, 10, 25, HealthLevel.Note)]        // one day more
    [InlineData(2024, 1, 15, HealthLevel.Note)]
    [InlineData(2006, 6, 21, HealthLevel.Note)]
    public void The_driver_is_a_note_when_it_is_older_than_12_months(int year, int month, int day, HealthLevel expected)
    {
        var item = Assert.Single(Build(Facts(gpu: "NVIDIA GeForce RTX 5080", gpuDate: new DateTime(year, month, day))));
        Assert.Equal("gpu.driver", item.Id);
        Assert.Equal(expected, item.Level);
        Assert.Null(item.ActionUri);          // no link to any site, no button
        Assert.Null(item.ActionText);
    }

    [Fact]
    public void The_age_is_counted_in_days_of_the_calendar_not_in_hours()
    {
        var late = new DateTime(2026, 10, 26, 0, 0, 0);
        Assert.Equal(HealthLevel.Good, Assert.Single(HealthReport.Build(Facts(gpu: "X", gpuDate: new DateTime(2025, 10, 26, 23, 59, 59)), late)).Level);
        Assert.Equal(HealthLevel.Note, Assert.Single(HealthReport.Build(Facts(gpu: "X", gpuDate: new DateTime(2025, 10, 25, 23, 59, 59)), late)).Level);
    }

    [Fact]
    public void The_12_months_line_follows_the_calendar_in_a_leap_year()
    {
        var leapDay = new DateTime(2028, 2, 29);
        Assert.Equal(HealthLevel.Good, Assert.Single(HealthReport.Build(Facts(gpu: "X", gpuDate: new DateTime(2027, 2, 28)), leapDay)).Level);
        Assert.Equal(HealthLevel.Note, Assert.Single(HealthReport.Build(Facts(gpu: "X", gpuDate: new DateTime(2027, 2, 27)), leapDay)).Level);
    }

    [Fact]
    public void The_driver_item_needs_both_the_name_and_the_date()
    {
        Assert.Empty(Build(Facts(gpu: null, gpuDate: new DateTime(2024, 1, 1))));
        Assert.Empty(Build(Facts(gpu: "  ", gpuDate: new DateTime(2024, 1, 1))));
        Assert.Empty(Build(Facts(gpu: "NVIDIA GeForce RTX 5080", gpuDate: null)));
    }

    [Fact]
    public void The_driver_text_names_the_card_and_the_month_in_both_languages_and_never_claims_a_newer_driver_exists()
    {
        var old = Facts(gpu: " NVIDIA GeForce RTX 5080 ", gpuDate: new DateTime(2024, 3, 9));
        var recent = Facts(gpu: "NVIDIA GeForce RTX 5080", gpuDate: new DateTime(2026, 9, 30));

        Loc.Use("en");
        Assert.Equal("Graphics card driver", Assert.Single(Build(old)).Title);
        Assert.Equal("NVIDIA GeForce RTX 5080: the driver is from March 2024. The maker may have a newer one.", Assert.Single(Build(old)).Text);
        Assert.Equal("NVIDIA GeForce RTX 5080: the driver is from September 2026.", Assert.Single(Build(recent)).Text);

        Loc.Use("tr");
        var march = new DateTime(2024, 3, 9).ToString("MMMM yyyy", CultureInfo.GetCultureInfo("tr-TR"));
        var september = new DateTime(2026, 9, 30).ToString("MMMM yyyy", CultureInfo.GetCultureInfo("tr-TR"));
        Assert.Contains("2024", march);
        Assert.Equal($"NVIDIA GeForce RTX 5080: sürücü {march} tarihli. Üreticinin daha yenisi olabilir.", Assert.Single(Build(old)).Text);
        Assert.Equal($"NVIDIA GeForce RTX 5080: sürücü {september} tarihli.", Assert.Single(Build(recent)).Text);
    }

    [Fact]
    public void The_month_follows_the_language_of_the_app_not_the_language_of_the_thread()
    {
        var facts = Facts(gpu: "X", gpuDate: new DateTime(2026, 9, 30));
        var saved = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("de-DE");
            Loc.Use("tr");
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("de-DE");          // (Loc.Use("tr") leaves the thread's culture alone: put it back to German)
            Assert.Contains(new DateTime(2026, 9, 30).ToString("MMMM yyyy", CultureInfo.GetCultureInfo("tr-TR")), Assert.Single(Build(facts)).Text);
            Loc.Use("en");
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("de-DE");
            Assert.Contains("September 2026", Assert.Single(Build(facts)).Text);
        }
        finally { CultureInfo.CurrentCulture = saved; }
    }

    // ------------------------------------------------------------------ the whole list

    [Fact]
    public void The_items_come_in_the_order_of_the_description()
    {
        var items = Build(Everything());
        Assert.Equal(new[] { "space.system", "space.game", "game.hdd", "disk.health", "uptime", "reboot", "battery", "gpu.driver" }, Ids(items));
        Assert.Equal(items.Count, items.Select(i => i.Id).Distinct().Count());
    }

    [Fact]
    public void The_list_is_pure_the_same_facts_give_the_same_items()
    {
        Assert.Equal(Build(Everything()), Build(Everything()));
        Loc.Use("en");
        var first = Build(Everything());
        Assert.Equal(first, Build(Everything()));
    }

    [Theory]
    [InlineData("tr")]
    [InlineData("en")]
    public void Titles_are_short_and_texts_are_at_most_two_short_sentences(string language)
    {
        Loc.Use(language);
        foreach (var facts in AllKinds())
            foreach (var item in Build(facts))
            {
                Assert.False(string.IsNullOrWhiteSpace(item.Id));
                Assert.True(item.Title.Length > 0 && item.Title.Length <= HealthTitleLimit.For(item.Id), $"[{language}] title has {item.Title.Length} characters: {item.Title}");
                Assert.True(item.Text.Length is > 0 and <= 180, $"[{language}] text has {item.Text.Length} characters: {item.Text}");
                var sentences = Regex.Matches(item.Text, @"[.!?](\s|$)").Count;
                Assert.True(sentences is >= 1 and <= 2, $"[{language}] {sentences} sentences: {item.Text}");
                Assert.DoesNotContain("!", item.Title + item.Text);            // no alarm
            }
    }

    [Theory]
    [InlineData("tr")]
    [InlineData("en")]
    public void A_button_is_only_ever_an_allowed_settings_page_with_its_text(string language)
    {
        Loc.Use(language);
        foreach (var facts in AllKinds())
            foreach (var item in Build(facts))
            {
                Assert.Equal(item.ActionUri == null, item.ActionText == null);
                if (item.ActionUri == null) continue;
                Assert.StartsWith("ms-settings:", item.ActionUri);
                Assert.True(HealthReport.IsAllowedActionUri(item.ActionUri), item.ActionUri);
                Assert.False(string.IsNullOrWhiteSpace(item.ActionText));
            }
    }

    [Fact]
    public void Only_the_documented_settings_pages_are_allowed()
    {
        Assert.True(HealthReport.IsAllowedActionUri("ms-settings:storagesense"));
        Assert.True(HealthReport.IsAllowedActionUri("ms-settings:powersleep"));
        Assert.False(HealthReport.IsAllowedActionUri(null));
        Assert.False(HealthReport.IsAllowedActionUri(""));
        Assert.False(HealthReport.IsAllowedActionUri("ms-settings:windowsupdate"));
        Assert.False(HealthReport.IsAllowedActionUri("ms-settings:storagesense?x=1"));
        Assert.False(HealthReport.IsAllowedActionUri("https://example.com"));
        Assert.False(HealthReport.IsAllowedActionUri(@"file:///C:/Windows/System32/cmd.exe"));
        Assert.False(HealthReport.IsAllowedActionUri("cmd.exe"));
    }

    [Fact]
    public void Every_text_exists_in_English_and_none_of_it_is_Turkish()
    {
        Loc.Use("en");
        Loc.ClearMissing();
        foreach (var facts in AllKinds())
            foreach (var item in Build(facts))
            {
                var all = item.Title + " | " + item.Text + " | " + item.ActionText;
                Assert.False(TurkishLetter.IsMatch(all), all);
            }
        Assert.Empty(Loc.Missing);
    }

    [Fact]
    public void The_titles_say_the_facts_so_that_the_numbers_are_in_them_in_both_languages()
    {
        foreach (var language in new[] { "tr", "en" })
        {
            Loc.Use(language);
            var items = Build(Everything());
            var point = language == "tr" ? "," : ".";                // under 100 GB one decimal: 20 GB is "20,0 GB" / "20.0 GB"
            Assert.Contains("C:", items[0].Title);
            Assert.Contains("D:", items[1].Title);
            Assert.Contains($"20{point}0 GB", items[0].Title);
            Assert.Contains($"50{point}0 GB", items[1].Title);
            Assert.Contains("30", items.Single(i => i.Id == "uptime").Title);
            Assert.Contains("D:", items.Single(i => i.Id == "game.hdd").Title);
            Assert.Contains("RTX 5080", items.Single(i => i.Id == "gpu.driver").Text);
            Assert.Contains("2024", items.Single(i => i.Id == "gpu.driver").Text);
        }
    }
}

/// <summary>The small decisions of the probe (pure) and the rule that nothing is read while the game runs.</summary>
public class HealthProbeRulesTests
{
    private static HealthProbeRules.GpuCandidate Gpu(string name, string pnp, ulong ram, string? date, string maker = "", int error = 0) =>
        new(name, maker, pnp, ram, date, error);

    // ------------------------------------------------------------------ small decisions

    [Theory]
    [InlineData(@"D:\steam\steamapps\common", "D")]
    [InlineData(@"d:/games/cs2", "D")]
    [InlineData("D:", "D")]
    [InlineData(@"  C:\Program Files\Steam  ", "C")]
    [InlineData(@"\\?\E:\Games", "E")]
    [InlineData(@"\\server\share\Games", null)]
    [InlineData(@"relative\path", null)]
    [InlineData("1:\\x", null)]
    [InlineData("", null)]
    [InlineData("   ", null)]
    [InlineData(null, null)]
    public void The_drive_letter_of_a_path(string? path, string? expected)
    {
        var letter = HealthProbeRules.DriveLetterOf(path);
        Assert.Equal(expected, letter?.ToString());
    }

    [Theory]
    [InlineData(3, "HDD")]
    [InlineData(4, "SSD")]
    [InlineData(0, null)]
    [InlineData(1, null)]
    [InlineData(2, null)]
    [InlineData(5, null)]
    [InlineData(7, null)]
    [InlineData(-1, null)]
    public void Only_the_media_types_3_and_4_are_known(int code, string? expected)
    {
        Assert.Equal(expected, HealthProbeRules.MediaTypeName(code));
    }

    private static string Power(int acLine, int batteryFlag) =>
        HealthProbeRules.OnBatteryFrom((byte)acLine, (byte)batteryFlag) switch { true => "battery", false => "mains", null => "unknown" };

    [Theory]
    [InlineData(0, 1, "battery")]       // off the mains, battery high
    [InlineData(0, 2, "battery")]
    [InlineData(0, 4, "battery")]
    [InlineData(0, 0, "battery")]       // a battery between "low" and "high" has no flag at all
    [InlineData(0, 128, "unknown")]     // off the mains but no battery: nothing sensible to say
    [InlineData(0, 255, "unknown")]
    [InlineData(1, 128, "mains")]       // a desktop PC
    [InlineData(1, 1, "mains")]
    [InlineData(1, 9, "mains")]         // charging
    [InlineData(1, 255, "mains")]
    [InlineData(255, 128, "unknown")]
    [InlineData(255, 1, "unknown")]
    [InlineData(2, 1, "unknown")]
    public void On_battery_is_only_true_for_a_PC_that_has_a_battery_and_is_off_the_mains(int acLine, int batteryFlag, string expected)
    {
        Assert.Equal(expected, Power(acLine, batteryFlag));
    }

    [Fact]
    public void A_desktop_PC_is_never_on_battery()
    {
        // the real answer of a desktop on this kind of PC: on the mains (1), flag 128 (no system battery)
        Assert.NotEqual(true, HealthProbeRules.OnBatteryFrom(1, 128));
        // even an odd one that says "off the mains" with "no battery" is not "on battery"
        Assert.NotEqual(true, HealthProbeRules.OnBatteryFrom(0, 128));
    }

    [Fact]
    public void A_pending_restart_is_any_trusted_sign_and_not_pending_only_when_both_were_read()
    {
        Assert.True(HealthProbeRules.RebootFrom(true, null));
        Assert.True(HealthProbeRules.RebootFrom(null, true));
        Assert.True(HealthProbeRules.RebootFrom(true, false));
        Assert.True(HealthProbeRules.RebootFrom(false, true));
        Assert.True(HealthProbeRules.RebootFrom(true, true));
        Assert.False(HealthProbeRules.RebootFrom(false, false));
        Assert.Null(HealthProbeRules.RebootFrom(false, null));
        Assert.Null(HealthProbeRules.RebootFrom(null, false));
        Assert.Null(HealthProbeRules.RebootFrom(null, null));
    }

    [Theory]
    [InlineData("20260930000000.000000-000", 2026, 9, 30)]       // what this kind of PC reports for the NVIDIA driver
    [InlineData("20060621000000.000000-000", 2006, 6, 21)]
    [InlineData("20261001000000.000000+180", 2026, 10, 1)]       // the date as written: no shift to 30 September
    [InlineData("20261001000000.000000-480", 2026, 10, 1)]
    [InlineData("20240229", 2024, 2, 29)]
    public void A_WMI_date_is_read_as_written(string text, int year, int month, int day)
    {
        Assert.Equal(new DateTime(year, month, day), HealthProbeRules.ParseCimDate(text));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("2026")]
    [InlineData("garbage")]
    [InlineData("00000000000000.000000+000")]       // the zero date
    [InlineData("20261340000000.000000+000")]       // month 13
    [InlineData("20260230000000.000000+000")]       // 30 February
    [InlineData("19700101000000.000000+000")]       // before there were graphics drivers
    public void A_WMI_date_that_is_not_a_real_date_is_not_known(string text)
    {
        Assert.Null(HealthProbeRules.ParseCimDate(text));
    }

    [Fact]
    public void No_WMI_date_at_all_is_not_known()
    {
        Assert.Null(HealthProbeRules.ParseCimDate(null));
    }

    // ------------------------------------------------------------------ the graphics card

    [Fact]
    public void The_card_of_this_kind_of_PC_is_found_and_the_stand_in_adapter_is_left_out()
    {
        // read from a Turkish Windows with an NVIDIA card and an unused AMD chip: the stand-in's name is Turkish
        var basic = Gpu("Microsoft Temel Görüntü Bağdaştırıcısı", @"PCI\VEN_1002&DEV_13C0&SUBSYS_7E261462&REV_E6\4&16ADCFD2&0&0041", 0, "20060621000000.000000-000", "(Standart ekran türleri)", 10);
        var nvidia = Gpu("NVIDIA GeForce RTX 5080", @"PCI\VEN_10DE&DEV_2C02&SUBSYS_53101462&REV_A1\6E480EF7C32DB04800", 4293918720, "20260930000000.000000-000", "NVIDIA");
        Assert.Equal(nvidia, HealthProbeRules.PickGpu(new[] { basic, nvidia }));
        Assert.Equal(nvidia, HealthProbeRules.PickGpu(new[] { nvidia, basic }));
    }

    [Fact]
    public void Only_stand_in_adapters_means_no_card_is_known()
    {
        Assert.Null(HealthProbeRules.PickGpu(new[]
        {
            Gpu("Microsoft Basic Display Adapter", @"PCI\VEN_1234&DEV_1111", 0, "20060621000000.000000-000"),
            Gpu("Carte d'affichage de base Microsoft", @"PCI\VEN_1234&DEV_1112", 0, "20060621000000.000000-000"),
            Gpu("Microsoft Hyper-V Video", @"VMBUS\{DA0A7802}", 0, "20060621000000.000000-000"),
            Gpu("Some Card", @"PCI\VEN_1234&DEV_1113", 0, null, "Microsoft Corporation"),
            Gpu("", @"PCI\VEN_1234", 0, null),
        }));
        Assert.Null(HealthProbeRules.PickGpu(Array.Empty<HealthProbeRules.GpuCandidate>()));
    }

    [Fact]
    public void On_a_laptop_with_two_cards_the_bigger_one_is_picked()
    {
        var intel = Gpu("Intel(R) UHD Graphics", @"PCI\VEN_8086&DEV_9A49", 1073741824, "20250115000000.000000-000", "Intel Corporation");
        var nvidia = Gpu("NVIDIA GeForce RTX 3060 Laptop GPU", @"PCI\VEN_10DE&DEV_2560", 4293918720, "20240301000000.000000-000", "NVIDIA");
        Assert.Equal(nvidia, HealthProbeRules.PickGpu(new[] { intel, nvidia }));
        Assert.Equal(nvidia, HealthProbeRules.PickGpu(new[] { nvidia, intel }));
    }

    [Fact]
    public void A_real_card_beats_a_virtual_display_and_a_working_one_beats_one_with_a_device_error()
    {
        var virtualDisplay = Gpu("Virtual Display Adapter", @"ROOT\DISPLAY\0000", 4293918720, "20260101000000.000000-000");
        var amd = Gpu("AMD Radeon RX 7800 XT", @"PCI\VEN_1002&DEV_747E", 4293918720, "20250601000000.000000-000", "Advanced Micro Devices, Inc.");
        Assert.Equal(amd, HealthProbeRules.PickGpu(new[] { virtualDisplay, amd }));

        var broken = Gpu("NVIDIA GeForce RTX 4090", @"PCI\VEN_10DE&DEV_2684", 4293918720, "20260101000000.000000-000", "NVIDIA", error: 43);
        var integrated = Gpu("Intel(R) UHD Graphics", @"PCI\VEN_8086&DEV_4680", 1073741824, "20250101000000.000000-000", "Intel Corporation");
        Assert.Equal(integrated, HealthProbeRules.PickGpu(new[] { broken, integrated }));
        Assert.Equal(broken, HealthProbeRules.PickGpu(new[] { broken }));           // nothing better: the card with the error is still the only one
    }

    [Fact]
    public void With_equal_cards_the_one_with_a_readable_driver_date_wins_and_the_choice_does_not_depend_on_the_order()
    {
        var dated = Gpu("Card B", @"PCI\VEN_1234&DEV_0002", 4293918720, "20250101000000.000000-000");
        var undated = Gpu("Card A", @"PCI\VEN_1234&DEV_0001", 4293918720, "00000000000000.000000+000");
        Assert.Equal(dated, HealthProbeRules.PickGpu(new[] { undated, dated }));
        Assert.Equal(dated, HealthProbeRules.PickGpu(new[] { dated, undated }));

        var a = Gpu("Card A", @"PCI\VEN_1234&DEV_0001", 4293918720, "20250101000000.000000-000");
        var b = Gpu("Card B", @"PCI\VEN_1234&DEV_0002", 4293918720, "20250101000000.000000-000");
        Assert.Equal(a, HealthProbeRules.PickGpu(new[] { b, a }));
        Assert.Equal(a, HealthProbeRules.PickGpu(new[] { a, b }));
    }

    // ------------------------------------------------------------------ which drives are read

    [Fact]
    public void Only_the_system_drive_and_the_drive_of_the_game_are_read_each_once_and_in_that_order()
    {
        Assert.Equal(new[] { 'C' }, HealthProbeRules.DriveLettersToRead('C', null).ToArray());
        Assert.Equal(new[] { 'C', 'D' }, HealthProbeRules.DriveLettersToRead('C', 'D').ToArray());
        Assert.Equal(new[] { 'C' }, HealthProbeRules.DriveLettersToRead('C', 'C').ToArray());          // the game on the system drive: one drive
        Assert.Equal(new[] { 'C' }, HealthProbeRules.DriveLettersToRead('C', 'c').ToArray());
        Assert.Equal(new[] { 'C', 'E' }, HealthProbeRules.DriveLettersToRead('c', 'e').ToArray());
        Assert.Equal(new[] { 'D' }, HealthProbeRules.DriveLettersToRead(null, 'D').ToArray());          // the system drive not known
        Assert.Empty(HealthProbeRules.DriveLettersToRead(null, null));
    }

    [Fact]
    public void However_many_drives_a_PC_has_never_more_than_two_are_read()
    {
        foreach (var system in new char?[] { null, 'C' })
            foreach (var game in new char?[] { null, 'C', 'D', 'Z' })
                Assert.InRange(HealthProbeRules.DriveLettersToRead(system, game).Count, 0, 2);
    }

    // ------------------------------------------------------------------ the storage kinds: the game comes first, also between the two WMI questions

    /// <summary>Stands in for WMI: answers the two questions of the storage-kind reading with fixed rows and records which questions were asked.</summary>
    private sealed class FakeStorage
    {
        public readonly List<string> Asked = new();
        public bool KindsKnown = true, FirstFinishes = true, SecondFinishes = true;
        public Action? AfterFirstQuestion;

        public bool Ask(string wmiNamespace, string wql, Action<Func<string, object?>> eachRow)
        {
            Asked.Add(wmiNamespace + " | " + wql);
            if (wql.Contains("MSFT_PhysicalDisk"))
            {
                Row(eachRow, ("DeviceId", "0"), ("MediaType", (ushort)(KindsKnown ? 4 : 0)));          // an SSD
                Row(eachRow, ("DeviceId", "1"), ("MediaType", (ushort)(KindsKnown ? 3 : 0)));          // an HDD
                Row(eachRow, ("DeviceId", "2"), ("MediaType", (ushort)0));                              // a kind that is not known
                AfterFirstQuestion?.Invoke();
                return FirstFinishes;
            }
            Row(eachRow, ("DriveLetter", 'C'), ("DiskNumber", 0u));
            Row(eachRow, ("DriveLetter", 'd'), ("DiskNumber", 1u));
            Row(eachRow, ("DriveLetter", 'E'), ("DiskNumber", 2u));          // on the disk of no known kind: no entry
            Row(eachRow, ("DriveLetter", '\0'), ("DiskNumber", 0u));         // a partition without a letter
            Row(eachRow, ("DriveLetter", 'F'), ("DiskNumber", 9u));          // on a disk that was not listed
            return SecondFinishes;
        }

        private static void Row(Action<Func<string, object?>> eachRow, params (string Name, object Value)[] values) =>
            eachRow(name => values.Where(v => v.Name == name).Select(v => (object?)v.Value).FirstOrDefault());
    }

    [Fact]
    public void The_two_questions_give_the_kind_of_each_lettered_drive_and_are_asked_in_order()
    {
        var storage = new FakeStorage();
        var map = HealthProbeRules.MediaTypesFrom(storage.Ask, () => false);
        Assert.NotNull(map);
        Assert.Equal(2, map!.Count);
        Assert.Equal("SSD", map['C']);
        Assert.Equal("HDD", map['D']);

        Assert.Equal(2, storage.Asked.Count);
        Assert.Contains(@"root\Microsoft\Windows\Storage", storage.Asked[0]);
        Assert.Contains("MSFT_PhysicalDisk", storage.Asked[0]);
        Assert.Contains("MSFT_Partition", storage.Asked[1]);
    }

    [Theory]
    [InlineData(0, 0)]          // the game is up before anything: nothing is asked
    [InlineData(1, 1)]          // the game comes up after the first check: the first question was asked, the second is not
    [InlineData(2, 2)]          // the game is not up in time: both
    [InlineData(10, 2)]
    public void The_second_question_is_not_asked_once_the_game_is_up(int checksBeforeTheGame, int expectedQuestions)
    {
        var storage = new FakeStorage();
        var checks = 0;
        var map = HealthProbeRules.MediaTypesFrom(storage.Ask, () => ++checks > checksBeforeTheGame);          // "the game is running" from the check after N checks on

        Assert.Equal(expectedQuestions, storage.Asked.Count);
        if (expectedQuestions == 2) Assert.NotNull(map);
        else Assert.Null(map);
    }

    [Fact]
    public void A_part_that_was_given_up_on_while_its_first_question_ran_does_not_ask_the_second()
    {
        var storage = new FakeStorage();
        var givenUp = false;
        storage.AfterFirstQuestion = () => givenUp = true;          // Read stops waiting while the first WMI question is still being answered

        Assert.Null(HealthProbeRules.MediaTypesFrom(storage.Ask, () => givenUp));
        Assert.Single(storage.Asked);
        Assert.Contains("MSFT_PhysicalDisk", storage.Asked[0]);
    }

    [Fact]
    public void A_question_that_was_stopped_gives_no_answer_and_ends_the_part()
    {
        var first = new FakeStorage { FirstFinishes = false };
        Assert.Null(HealthProbeRules.MediaTypesFrom(first.Ask, () => false));
        Assert.Single(first.Asked);                                  // the first question did not finish: the second is not asked

        var second = new FakeStorage { SecondFinishes = false };
        Assert.Null(HealthProbeRules.MediaTypesFrom(second.Ask, () => false));          // half an answer is not an answer
        Assert.Equal(2, second.Asked.Count);
    }

    [Fact]
    public void Without_a_disk_of_a_known_kind_the_second_question_is_not_asked()
    {
        var storage = new FakeStorage { KindsKnown = false };
        Assert.Null(HealthProbeRules.MediaTypesFrom(storage.Ask, () => false));
        Assert.Single(storage.Asked);
    }

    [Fact]
    public void A_failing_WMI_question_is_passed_on_so_that_the_part_is_not_known()
    {
        Assert.Throws<InvalidOperationException>(() => HealthProbeRules.MediaTypesFrom((_, _, _) => throw new InvalidOperationException("WMI is down"), () => false));
    }

    [Theory]
    [InlineData('c', "C")]
    [InlineData('D', "D")]
    [InlineData((ushort)67, "C")]
    [InlineData("e", "E")]
    [InlineData("ee", null)]
    [InlineData("", null)]
    [InlineData('\0', null)]
    [InlineData((ushort)0, null)]
    [InlineData('1', null)]
    [InlineData(5, null)]
    [InlineData(null, null)]
    public void A_WMI_drive_letter_is_a_letter_or_nothing(object? value, string? expected)
    {
        Assert.Equal(expected, HealthProbeRules.ToLetter(value)?.ToString());
    }

    [Theory]
    [InlineData(null, -1)]
    [InlineData("3", 3)]
    [InlineData((ushort)4, 4)]
    [InlineData(3u, 3)]
    [InlineData("x", -1)]
    [InlineData("", -1)]
    public void A_WMI_number_is_read_as_an_int_or_minus_one(object? value, int expected)
    {
        Assert.Equal(expected, HealthProbeRules.ToInt(value));
    }

    [Theory]
    [InlineData(null, 0UL)]
    [InlineData(4293918720UL, 4293918720UL)]
    [InlineData("1024", 1024UL)]
    [InlineData("x", 0UL)]
    [InlineData(-5, 0UL)]
    public void A_WMI_number_is_read_as_an_unsigned_number_or_zero(object? value, ulong expected)
    {
        Assert.Equal(expected, HealthProbeRules.ToULong(value));
    }

    // ------------------------------------------------------------------ the game comes first

    private static void AssertNothingKnown(HealthFacts f)
    {
        Assert.NotNull(f);
        Assert.Empty(f.Disks);
        Assert.Null(f.UptimeDays);
        Assert.Null(f.RebootPending);
        Assert.Null(f.OnBattery);
        Assert.Null(f.GpuName);
        Assert.Null(f.GpuDriverDate);
        Assert.Null(f.DiskFailurePredicted);
    }

    [Fact]
    public void While_the_game_runs_the_probe_reads_nothing()
    {
        // (a real PC always has disks and an uptime: if anything was read, the test would see it)
        AssertNothingKnown(new WindowsHealthProbe(() => true).Read(@"D:\steam"));
        AssertNothingKnown(new WindowsHealthProbe(() => true).Read(null));
    }

    [Fact]
    public void A_game_check_that_fails_counts_as_the_game_running()
    {
        AssertNothingKnown(new WindowsHealthProbe(() => throw new InvalidOperationException("no process list")).Read(null));
    }

    [Fact]
    public void A_probe_needs_a_game_check()
    {
        Assert.Throws<ArgumentNullException>(() => new WindowsHealthProbe(null!));
    }

    [Fact]
    public void A_game_that_comes_up_right_after_the_first_check_leaves_every_part_unread()
    {
        // The first check (the one at the start of Read) says "no game"; every later one says "game". Each of the four threaded parts (the drives and the three
        // WMI parts) and each quick part asks before it reads anything, so none of them reads: this PC always has a system drive, an uptime and a graphics
        // card, so anything that was read would show. (Nothing here touches the machine: every part stops at its first check.)
        var checks = 0;
        var probe = new WindowsHealthProbe(() => Interlocked.Increment(ref checks) > 1);

        AssertNothingKnown(probe.Read(Environment.SystemDirectory));
        Assert.True(Volatile.Read(ref checks) >= 2, "the game was looked at again after the start");
    }

    [Fact]
    public void Nothing_known_gives_an_empty_check_up()
    {
        var nothing = new WindowsHealthProbe(() => true).Read(null);
        Assert.Empty(HealthReport.Build(nothing, DateTime.Now));
    }
}

/// <summary>The real probe on the real PC. Read-only (DriveInfo, registry reads, WMI queries, one kernel32 read); the game, if it runs, is not disturbed.</summary>
[Collection("Sequential")]
public class LiveHealthProbeTests
{
    private readonly ITestOutputHelper _out;
    public LiveHealthProbeTests(ITestOutputHelper o) => _out = o;

    private static bool GameIsRunning() => new WindowsSystem().FindPidByName(Catalog.Cs2ProcessName) != null;

    private static void AssertValidItems(IReadOnlyList<HealthItem> items)
    {
        Assert.Equal(items.Count, items.Select(i => i.Id).Distinct().Count());
        foreach (var item in items)
        {
            Assert.False(string.IsNullOrWhiteSpace(item.Id));
            Assert.True(Enum.IsDefined(item.Level));
            Assert.True(item.Title.Length > 0 && item.Title.Length <= HealthTitleLimit.For(item.Id), item.Title);
            Assert.False(string.IsNullOrWhiteSpace(item.Text));
            Assert.Equal(item.ActionUri == null, item.ActionText == null);
            if (item.ActionUri != null) Assert.True(HealthReport.IsAllowedActionUri(item.ActionUri), item.ActionUri);
        }
    }

    [Fact]
    public void The_probe_reads_this_PC_without_throwing_and_the_values_are_plausible()
    {
        var gameUp = GameIsRunning();
        var clock = Stopwatch.StartNew();
        var facts = new WindowsHealthProbe().Read(null);
        clock.Stop();
        _out.WriteLine($"{clock.ElapsedMilliseconds} ms; game running: {gameUp}");
        Assert.NotNull(facts);
        Assert.NotNull(facts.Disks);
        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(10), $"took {clock.Elapsed}");          // every part is bounded: one that has not answered after 6.5 seconds is left out

        if (gameUp)
        {
            // the game comes first: nothing is read
            Assert.Empty(facts.Disks);
            Assert.Null(facts.UptimeDays);
            return;
        }

        foreach (var d in facts.Disks)
            _out.WriteLine($"{d.Drive} total {d.TotalBytes >> 30} GB free {d.FreeBytes >> 30} GB media={d.MediaType ?? "?"} system={d.IsSystemDrive} game={d.HoldsGame}");
        _out.WriteLine($"uptime {facts.UptimeDays:F2} days; restart pending {facts.RebootPending?.ToString() ?? "?"}; on battery {facts.OnBattery?.ToString() ?? "?"}; " +
                       $"gpu {facts.GpuName ?? "?"} / {facts.GpuDriverDate?.ToString("yyyy-MM-dd") ?? "?"}; disk failure predicted {facts.DiskFailurePredicted?.ToString() ?? "?"}");

        foreach (var d in facts.Disks)
        {
            Assert.Matches("^[A-Z]:$", d.Drive);
            Assert.True(d.TotalBytes > 0);
            Assert.InRange(d.FreeBytes, 0, d.TotalBytes);
            Assert.True(d.MediaType is null or "SSD" or "HDD", d.MediaType);
            Assert.False(d.HoldsGame);                     // no game folder was given
        }
        Assert.Equal(facts.Disks.Count, facts.Disks.Select(d => d.Drive).Distinct().Count());
        Assert.True(facts.Disks.Count(d => d.IsSystemDrive) <= 1);
        // no game folder was given: only the drive with Windows is looked at (no other drive is touched)
        Assert.True(facts.Disks.Count <= 1);
        Assert.All(facts.Disks, d => Assert.True(d.IsSystemDrive));

        if (facts.UptimeDays is double up) Assert.InRange(up, 0, 36500);
        if (facts.GpuName != null)
        {
            Assert.False(string.IsNullOrWhiteSpace(facts.GpuName));
            Assert.DoesNotContain("Microsoft", facts.GpuName, StringComparison.OrdinalIgnoreCase);
        }
        if (facts.GpuDriverDate is DateTime driverDate) Assert.True(driverDate.Year >= 1995 && driverDate <= DateTime.Now.AddYears(1));
        Assert.True(facts.GpuName != null || facts.GpuDriverDate == null);          // a date never comes without a card

        var items = HealthReport.Build(facts, DateTime.Now);
        _out.WriteLine(string.Join("\n", items.Select(i => $"[{i.Level}] {i.Id}: {i.Title} - {i.Text}" + (i.ActionUri == null ? "" : $" ({i.ActionText} -> {i.ActionUri})"))));
        AssertValidItems(items);
        if (facts.Disks.Any(d => d.IsSystemDrive)) Assert.Contains(items, i => i.Id == "space.system");
    }

    [Fact]
    public void The_drive_of_a_given_game_folder_is_the_one_that_holds_the_game()
    {
        if (GameIsRunning()) return;                                  // nothing is read while the game runs (see the test above)
        var facts = new WindowsHealthProbe().Read(Environment.SystemDirectory);
        foreach (var d in facts.Disks) _out.WriteLine($"{d.Drive} system={d.IsSystemDrive} game={d.HoldsGame}");
        if (facts.Disks.Count == 0) return;                           // a PC without a readable fixed drive: nothing to compare

        Assert.True(facts.Disks.Count <= 1);                          // the game is on the Windows drive: that is the only drive looked at
        var system = facts.Disks.FirstOrDefault(d => d.IsSystemDrive);
        if (system != null) Assert.True(system.HoldsGame);            // the folder is on the system drive
        Assert.All(facts.Disks.Where(d => !d.IsSystemDrive), d => Assert.False(d.HoldsGame));

        var items = HealthReport.Build(facts, DateTime.Now);
        AssertValidItems(items);
        Assert.DoesNotContain(items, i => i.Id == "space.game");      // one drive: one space item
    }

    [Fact]
    public void Only_the_system_drive_and_the_drive_of_the_game_folder_are_listed_whatever_other_drives_there_are()
    {
        if (GameIsRunning()) return;                                  // nothing is read while the game runs

        // another fixed drive of this PC, if there is one: its type comes from the list of drives, nothing of it is opened here
        var systemRoot = Path.GetPathRoot(Environment.SystemDirectory) ?? "";
        if (systemRoot.Length < 2 || systemRoot[1] != ':') return;          // Windows on something other than a lettered drive: nothing to compare
        var other =DriveInfo.GetDrives().FirstOrDefault(d => d.DriveType == DriveType.Fixed && !string.Equals(d.Name, systemRoot, StringComparison.OrdinalIgnoreCase));
        if (other == null) return;                                    // a PC with one drive: nothing to compare (the tests above cover it)

        var facts = new WindowsHealthProbe().Read(Path.Combine(other.Name, "Games"));
        foreach (var d in facts.Disks) _out.WriteLine($"{d.Drive} system={d.IsSystemDrive} game={d.HoldsGame}");

        Assert.True(facts.Disks.Count <= 2);
        var allowed = new[] { systemRoot.Substring(0, 1).ToUpperInvariant() + ":", other.Name.Substring(0, 1).ToUpperInvariant() + ":" };
        Assert.All(facts.Disks, d => Assert.Contains(d.Drive, allowed));          // no third drive
        Assert.All(facts.Disks, d => Assert.Equal(d.Drive == allowed[1], d.HoldsGame));
        Assert.All(facts.Disks, d => Assert.Equal(d.Drive == allowed[0], d.IsSystemDrive));
    }
}
