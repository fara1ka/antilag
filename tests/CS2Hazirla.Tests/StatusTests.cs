using CS2Hazirla.Core;
using Xunit;

namespace CS2Hazirla.Tests;

/// <summary>The headline of the home screen: every branch, and the rule that a green "Bitti" is never shown when something went wrong.</summary>
public class StatusTests
{
    private static readonly string[] Three = { "Logitech G HUB", "Wallpaper Engine", "OneDrive" };

    [Fact]
    public void A_running_game_comes_first()
    {
        var v = Status.Compute(new StatusInput(GameRunning: true, Busy: true, Done: true));
        Assert.Equal("CS2 çalışıyor", v.Title);
        Assert.Equal(Tone.Warn, v.Tone);
        Assert.Contains("hiçbir şeyi değiştirmez", v.Hint);
    }

    [Fact]
    public void While_working_it_says_so()
    {
        var v = Status.Compute(new StatusInput(Busy: true, Done: true, ErrorMessage: "x"));
        Assert.Equal("Çalışıyor", v.Title);
        Assert.Equal(Tone.Neutral, v.Tone);
    }

    [Fact]
    public void An_exception_shows_the_fact_but_never_the_raw_text()
    {
        var v = Status.Compute(new StatusInput(Done: true, ErrorMessage: "System.InvalidOperationException: boom"));
        Assert.Equal("İşlem tamamlanamadı", v.Title);
        Assert.Equal("Ayrıntı aşağıdaki listede.", v.Hint);
        Assert.Equal(Tone.Bad, v.Tone);
        Assert.DoesNotContain("boom", v.Title + v.Hint);
    }

    [Fact]
    public void A_complete_restore_says_so()
    {
        var v = Status.Compute(new StatusInput(Restore: RestoreOutcome.Ok, Done: true));
        Assert.Equal("Geri yüklendi", v.Title);
        Assert.Equal("Ayarlar ve hizmetler eski haline döndü.", v.Hint);
        Assert.Equal(Tone.Good, v.Tone);
    }

    [Fact]
    public void A_restore_that_could_not_start_everything_names_up_to_three_programs()
    {
        var v = Status.Compute(new StatusInput(Restore: RestoreOutcome.Partial, NotRestarted: new[] { "Discord", "GoodbyeDPI" }));
        Assert.Equal("Geri yüklendi, 2 program kendiliğinden açılamadı", v.Title);
        Assert.StartsWith("Discord, GoodbyeDPI.", v.Hint);
        Assert.Equal(Tone.Warn, v.Tone);

        var many = Status.Compute(new StatusInput(Restore: RestoreOutcome.Partial, NotRestarted: new[] { "A", "B", "C", "D", "E" }));
        Assert.Equal("Geri yüklendi, 5 program kendiliğinden açılamadı", many.Title);
        Assert.StartsWith("A, B, C ve 2 diğeri.", many.Hint);
        Assert.DoesNotContain("D,", many.Hint);
    }

    [Fact]
    public void A_partial_restore_without_any_name_is_just_a_restore()
    {
        var v = Status.Compute(new StatusInput(Restore: RestoreOutcome.Partial, NotRestarted: Array.Empty<string>()));
        Assert.Equal("Geri yüklendi", v.Title);
        Assert.Equal(Tone.Good, v.Tone);
    }

    [Fact]
    public void Programs_that_did_not_close_make_it_amber_and_name_up_to_three()
    {
        var v = Status.Compute(new StatusInput(Done: true, DoneCount: 12, NotClosed: new[] { "Avast", "Kaspersky" }));
        Assert.Equal("Bitti, ancak 2 program kapanmadı", v.Title);
        Assert.Equal(Tone.Warn, v.Tone);
        Assert.Contains("Avast, Kaspersky", v.Hint);

        var many = Status.Compute(new StatusInput(Done: true, NotClosed: new[] { "A", "B", "C", "D" }));
        Assert.Equal("Bitti, ancak 4 program kapanmadı", many.Title);
        Assert.Contains("A, B, C ve 1 diğeri", many.Hint);
        Assert.DoesNotContain("D,", many.Hint);
    }

    [Fact]
    public void Failed_steps_alone_also_make_it_amber()
    {
        var v = Status.Compute(new StatusInput(Done: true, DoneCount: 9, FailedCount: 3));
        Assert.Equal("Bitti, ancak 3 işlem yapılamadı", v.Title);
        Assert.Equal("Ayrıntı aşağıdaki listede.", v.Hint);
        Assert.Equal(Tone.Warn, v.Tone);
    }

    [Fact]
    public void Both_problems_are_named_programs_first()
    {
        var v = Status.Compute(new StatusInput(Done: true, FailedCount: 2, NotClosed: Three));
        Assert.Equal("Bitti, ancak 3 program kapanmadı", v.Title);
        Assert.Contains("Logitech G HUB, Wallpaper Engine, OneDrive", v.Hint);
        Assert.Contains("2 işlem de yapılamadı", v.Hint);
        Assert.Equal(Tone.Warn, v.Tone);
    }

    [Fact]
    public void A_clean_run_is_the_only_green_bitti()
    {
        var v = Status.Compute(new StatusInput(Done: true, DoneCount: 18));
        Assert.Equal("Bitti", v.Title);
        Assert.Equal(Tone.Good, v.Tone);
        Assert.Contains("18 işlem yapıldı", v.Hint);

        var nothing = Status.Compute(new StatusInput(Done: true));
        Assert.Equal("Bitti", nothing.Title);
        Assert.Contains("bir şey bulunamadı", nothing.Hint);
    }

    [Fact]
    public void Never_green_when_a_step_failed_or_a_program_stayed_open()
    {
        foreach (var failed in new[] { 0, 1, 5 })
            foreach (var notClosed in new[] { 0, 1, 4 })
                foreach (var done in new[] { 0, 7 })
                {
                    var v = Status.Compute(new StatusInput(Done: true, DoneCount: done, FailedCount: failed,
                                                           NotClosed: Enumerable.Range(1, notClosed).Select(i => "P" + i).ToList()));
                    if (failed > 0 || notClosed > 0) Assert.NotEqual(Tone.Good, v.Tone);
                    else Assert.Equal(Tone.Good, v.Tone);
                    if (failed > 0 || notClosed > 0) Assert.StartsWith("Bitti, ancak", v.Title);
                }
    }

    [Fact]
    public void The_idle_home_screen_says_only_which_mode_is_ready_no_sentence_about_what_it_does()
    {
        var v = Status.Compute(new StatusInput());
        Assert.Equal("Başlamaya hazır", v.Title);
        Assert.Equal(Tone.Neutral, v.Tone);
        Assert.Equal("", v.Hint);
        Assert.Equal("", Status.Compute(new StatusInput(PresetName: "Hafif")).Hint);
        Assert.Equal("", Status.Compute(new StatusInput(PresetName: "Tam temizlik", Customized: true)).Hint);
    }

    [Fact]
    public void Choosing_a_mode_shows_the_list_and_nothing_else_the_activity_list_and_the_restore_button_come_with_what_was_done()
    {
        // choosing: the list of programs, no activity list (even when old lines exist), the restore button only when there is something to restore
        var choosing = HomeView.Of(busy: false, done: false, failed: false, hasLogLines: true, hasSession: false);
        Assert.Equal(new HomeView(ShowList: true, ShowLog: false, ShowRestore: false), choosing);
        Assert.True(HomeView.Of(false, false, false, true, hasSession: true).ShowRestore);          // an earlier run is still in effect: the button, no sentence

        // while it runs and once it is done: the activity list takes the place of the list; the button waits until nothing runs
        var running = HomeView.Of(busy: true, done: false, failed: false, hasLogLines: true, hasSession: true);
        Assert.Equal(new HomeView(ShowList: false, ShowLog: true, ShowRestore: false), running);
        var finished = HomeView.Of(busy: false, done: true, failed: false, hasLogLines: true, hasSession: true);
        Assert.Equal(new HomeView(ShowList: false, ShowLog: true, ShowRestore: true), finished);

        // a failure shows the activity list too; without lines there is nothing to show
        Assert.True(HomeView.Of(false, false, failed: true, hasLogLines: true, hasSession: false).ShowLog);
        Assert.False(HomeView.Of(true, true, true, hasLogLines: false, hasSession: true).ShowLog);

        // a finished restore: the headline says so, the list is back (no activity list), nothing is left to restore
        var restored = HomeView.Of(busy: false, done: false, failed: false, hasLogLines: true, hasSession: false);
        Assert.True(restored.ShowList); Assert.False(restored.ShowLog); Assert.False(restored.ShowRestore);

        // the list and the activity list never show together
        foreach (var busy in new[] { false, true }) foreach (var done in new[] { false, true }) foreach (var failed in new[] { false, true })
            foreach (var lines in new[] { false, true })
            {
                var v = HomeView.Of(busy, done, failed, lines, hasSession: true);
                Assert.False(v.ShowList && v.ShowLog);
            }
    }

    [Fact]
    public void After_a_run_the_hint_says_to_close_Antilag_before_starting_the_game()
    {
        var v = Status.Compute(new StatusInput(Done: true, DoneCount: 18));
        Assert.Equal("18 işlem yapıldı. Oyunu başlatmadan önce Antilag'ı kapat.", v.Hint);
        Assert.DoesNotContain("CS2'yi açmadan", v.Hint);
    }

    [Fact]
    public void The_idle_title_names_the_preset_and_says_when_it_was_changed()
    {
        Assert.Equal("Tam temizlik hazır", Status.Compute(new StatusInput(PresetName: "Tam temizlik")).Title);
        Assert.Equal("Hafif (değiştirilmiş) hazır", Status.Compute(new StatusInput(PresetName: "Hafif", Customized: true)).Title);
    }

    [Fact]
    public void The_order_of_precedence_is_game_busy_error_restore_run_idle()
    {
        Assert.Equal("Çalışıyor", Status.Compute(new StatusInput(Busy: true, ErrorMessage: "x", Restore: RestoreOutcome.Ok)).Title);
        Assert.Equal("İşlem tamamlanamadı", Status.Compute(new StatusInput(ErrorMessage: "x", Restore: RestoreOutcome.Ok, Done: true)).Title);
        Assert.Equal("Geri yüklendi", Status.Compute(new StatusInput(Restore: RestoreOutcome.Ok, Done: true)).Title);
        Assert.Equal("Bitti", Status.Compute(new StatusInput(Done: true)).Title);
    }

    [Fact]
    public void The_texts_use_the_agreed_vocabulary()
    {
        var inputs = new[]
        {
            new StatusInput(), new StatusInput(GameRunning: true), new StatusInput(Busy: true), new StatusInput(ErrorMessage: "x"),
            new StatusInput(Done: true), new StatusInput(Done: true, FailedCount: 1),
            new StatusInput(Restore: RestoreOutcome.Ok), new StatusInput(Restore: RestoreOutcome.Partial, NotRestarted: new[] { "A" }),
            new StatusInput(PresetName: "Hafif", Customized: true),
        };
        foreach (var i in inputs)
        {
            var v = Status.Compute(i);
            foreach (var banned in new[] { "Clear", "Hazırla", "Sade", "Detaylı", "geri al", "eski haline döndür" })
                Assert.DoesNotContain(banned, v.Title + " " + v.Hint);
        }
    }
}
