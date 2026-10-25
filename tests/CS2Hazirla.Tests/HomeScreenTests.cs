using CS2Hazirla.Core;
using Xunit;

namespace CS2Hazirla.Tests;

/// <summary>
/// The home screen lists what a quick scan finds (a switch per program), a new Başlat reopens nothing, and only "Kapatılan uygulamaları geri aç"
/// brings back the programs of the latest run.
/// </summary>
[Collection("Sequential")]
public class HomeScreenTests
{
    private static IReadOnlyList<HomeApp> Scan(TestEnv env) =>
        HomeApps.From(new Planner(env.Sys).ScanPrograms(HomeApps.ScanSettings(env.Settings, ProfileKind.Maximum)));

    private static AppSettings Mode(ProfileKind kind)
    {
        var s = new AppSettings();
        s.ApplyProfile(kind);
        return s;
    }

    // ------------------------------------------------------------ what the scan lists

    [Fact]
    public void Only_programs_a_person_knows_are_listed_never_background_helpers_or_other_block_bypass_tools()
    {
        using var env = new TestEnv(ProfileKind.Maximum);
        env.Sys.AddProc("goodbyedpi", @"C:\Tools\GoodbyeDPI\goodbyedpi.exe");
        env.Sys.AddProc("zapret", @"C:\Tools\Zapret\zapret.exe");
        var rows = Scan(env);

        var known = new[] { "proc.discord", "proc.dpi", "proc.browsers", "proc.launchers", "proc.chat", "proc.claude", "proc.vpn", "proc.windowed" };
        Assert.All(rows, r => Assert.Contains(r.OptionId, known));
        foreach (var option in new[] { "proc.discord", "proc.dpi", "proc.browsers", "proc.chat", "proc.launchers" })
            Assert.Contains(rows, r => r.OptionId == option);                         // what is really running here is there
        // background things of the same machine (OneDrive, GoogleUpdate, Armoury Crate, G HUB, an unknown tray program) are not
        Assert.DoesNotContain(rows, r => r.Names.Any(n => n is "onedrive" or "dropbox" or "googleupdate" or "randomtraything" or "lghub_agent"));
        Assert.DoesNotContain(rows, r => r.Names.Contains("zapret"));                 // the other block-bypass tools are not on the home list
        Assert.Contains(rows, r => r.Names.Contains("goodbyedpi"));
    }

    [Fact]
    public void Nothing_is_predefined_a_program_that_is_not_running_is_not_listed()
    {
        using var env = new TestEnv(ProfileKind.Maximum);
        env.Sys.Procs.RemoveAll(p => p.Name is "Spotify" or "ms-teams" or "Discord");
        var rows = Scan(env);
        Assert.DoesNotContain(rows, r => r.OptionId is "proc.discord");
        Assert.DoesNotContain(rows, r => r.Names.Any(n => n is "spotify" or "ms-teams"));
        Assert.Contains(rows, r => r.OptionId == "proc.browsers");                    // Chrome and Edge still run
    }

    [Fact]
    public void Browsers_are_one_row_and_Discord_then_GoodbyeDPI_come_first_then_the_biggest_program()
    {
        using var env = new TestEnv(ProfileKind.Maximum);
        env.Sys.AddProc("goodbyedpi", @"C:\Tools\GoodbyeDPI\goodbyedpi.exe", mb: 5);
        var rows = Scan(env);

        var browsers = Assert.Single(rows, r => r.Key == HomeApps.BrowsersKey);
        Assert.Contains("chrome", browsers.Names); Assert.Contains("msedge", browsers.Names);
        Assert.Equal("proc.discord", rows[0].OptionId);
        Assert.Equal("proc.dpi", rows[1].OptionId);
        var rest = rows.Skip(2).Select(r => r.Bytes).ToList();
        Assert.Equal(rest.OrderByDescending(b => b), rest);                           // biggest first
    }

    // ------------------------------------------------------------ what a row shows

    [Fact]
    public void A_row_starts_as_the_chosen_mode_says_and_the_users_switch_wins()
    {
        using var env = new TestEnv(ProfileKind.Maximum);
        var spotify = Scan(env).First(r => r.Names.Contains("spotify"));
        var none = new Dictionary<string, bool>();

        Assert.False(HomeApps.Closes(spotify, Mode(ProfileKind.Safe), none));         // Hafif closes no program...
        Assert.True(HomeApps.Closes(spotify, Mode(ProfileKind.Maximum), none));
        Assert.True(HomeApps.Closes(spotify, Mode(ProfileKind.Stream), none));
        Assert.True(HomeApps.Closes(spotify, Mode(ProfileKind.Safe), new Dictionary<string, bool> { [spotify.Key] = true }));     // ...unless the user switches this one on
        Assert.False(HomeApps.Closes(spotify, Mode(ProfileKind.Maximum), new Dictionary<string, bool> { [spotify.Key] = false }));
    }

    [Fact]
    public void In_Yayinci_Discord_and_the_stream_tools_are_locked_and_never_shown_as_closing()
    {
        using var env = new TestEnv(ProfileKind.Maximum);
        var discord = Scan(env).First(r => r.OptionId == "proc.discord");
        var stream = Mode(ProfileKind.Stream);

        Assert.True(HomeApps.IsLocked(discord, stream));
        Assert.False(HomeApps.Closes(discord, stream, new Dictionary<string, bool> { [discord.Key] = true }));   // not even when a switch says so
        Assert.False(HomeApps.IsLocked(discord, Mode(ProfileKind.Maximum)));
    }

    // ------------------------------------------------------------ what a run does with the switches

    [Fact]
    public void A_program_switched_off_stays_open_and_one_switched_on_is_closed_even_in_a_mode_that_leaves_it()
    {
        using var env = new TestEnv(ProfileKind.Maximum);
        var rows = Scan(env);
        var spotify = rows.First(r => r.Names.Contains("spotify"));
        var browsers = rows.First(r => r.Key == HomeApps.BrowsersKey);

        // Tam temizlik: Spotify switched off stays
        var plan = new Planner(env.Sys).Build(env.Settings, TestEnv.Cs2Path);
        Assert.Contains(plan.Active, a => a.Id == "proc:spotify");
        HomeApps.Apply(plan, null, new Dictionary<string, bool> { [spotify.Key] = false, [browsers.Key] = false });
        Assert.DoesNotContain(plan.Active, a => a.Id is "proc:spotify" or "proc:chrome" or "proc:msedge");
        Assert.Contains(plan.Active, a => a.Id == "proc:discord");                   // nothing else changed

        // Hafif closes no program: Spotify switched on is closed, nothing else is
        var hafif = Mode(ProfileKind.Safe);
        var hafifPlan = new Planner(env.Sys).Build(hafif, TestEnv.Cs2Path);
        Assert.DoesNotContain(hafifPlan.Active, a => a.Kind == ActionKind.CloseProcess);
        var everything = new Planner(env.Sys).ScanPrograms(HomeApps.ScanSettings(hafif, hafif.Profile));
        HomeApps.Apply(hafifPlan, everything, new Dictionary<string, bool> { [spotify.Key] = true });
        Assert.Equal(new[] { "proc:spotify" }, hafifPlan.Active.Where(a => a.Kind == ActionKind.CloseProcess).Select(a => a.Id).ToArray());
    }

    [Fact]
    public void A_switch_for_a_program_that_is_gone_or_protected_changes_nothing()
    {
        using var env = new TestEnv(ProfileKind.Maximum);
        env.Settings.Profile = ProfileKind.Stream;
        var plan = new Planner(env.Sys).Build(env.Settings, TestEnv.Cs2Path);
        var everything = new Planner(env.Sys).ScanPrograms(HomeApps.ScanSettings(env.Settings, ProfileKind.Stream));
        var before = plan.Active.Select(a => a.Id).OrderBy(x => x).ToList();

        HomeApps.Apply(plan, everything, new Dictionary<string, bool> { ["Discord"] = true, ["Not running any more"] = true });
        Assert.DoesNotContain(plan.Active, a => a.Id == "proc:discord");              // Yayıncı protects it, the scan under Yayıncı does not even list it
        Assert.Equal(before, plan.Active.Select(a => a.Id).OrderBy(x => x).ToList());
    }

    [Fact]
    public void The_switches_are_remembered_per_profile_for_this_run_and_a_switch_back_leaves_no_trace()
    {
        var settings = new AppSettings();
        var session = new ProfileSession(settings);
        Assert.Empty(session.AppChoices);

        session.SetAppChoice("Spotify", close: false, profileSays: true);
        Assert.False(session.AppChoices["Spotify"]);
        session.Activate(ProfileKind.Safe);
        Assert.Empty(session.AppChoices);                                            // another profile has its own
        session.Activate(ProfileKind.Maximum);
        Assert.False(session.AppChoices["Spotify"]);                                  // and finds it again when the user comes back

        session.SetAppChoice("Spotify", close: true, profileSays: true);              // switched back to what the profile says
        Assert.Empty(session.AppChoices);

        session.SetAppChoice("Spotify", close: false, profileSays: true);
        session.ClearAppChoice("Spotify");                                           // the whole group was switched in Ayrıntılar: the last click wins
        Assert.Empty(session.AppChoices);
    }

    [Fact]
    public void A_launcher_and_its_web_helper_are_one_row_and_one_switch_covers_both()
    {
        using var env = new TestEnv(ProfileKind.Maximum);
        env.Sys.AddProc("EpicWebHelper", @"C:\Program Files\Epic Games\Launcher\Portal\Binaries\Win64\EpicWebHelper.exe");
        env.Sys.AddProc("WhatsApp.Root", @"C:\Program Files\Chat\WhatsApp.Root.exe", window: true);
        var rows = Scan(env);

        var epic = Assert.Single(rows, r => r.Display == "Epic Games Launcher");          // not "Epicwebhelper" as a second row
        Assert.DoesNotContain(rows, r => r.Names.Contains("epicwebhelper"));
        Assert.Contains(rows, r => r.Display == "WhatsApp");                               // a Store app has no readable file description

        var plan = new Planner(env.Sys).Build(env.Settings, TestEnv.Cs2Path);
        Assert.Contains(plan.Active, a => a.Id == "proc:epicwebhelper");                   // Tam temizlik closes the helper as before
        HomeApps.Apply(plan, null, new Dictionary<string, bool> { [epic.Key] = false });
        Assert.DoesNotContain(plan.Active, a => a.Id is "proc:epicgameslauncher" or "proc:epicwebhelper");   // the row's switch keeps both open
    }

    [Fact]
    public void Helpers_and_service_programs_are_not_rows_of_their_own()
    {
        using var env = new TestEnv(ProfileKind.Maximum);
        env.Sys.AddProc("riotclientcrashhandler", @"C:\Riot Games\Riot Client\crash.exe");
        env.Sys.AddProc("vpnthing-service", @"C:\Program Files\VPNThing\vpnthing-service.exe");
        var rows = Scan(env);
        Assert.DoesNotContain(rows, r => r.Names.Any(n => n.Contains("crash") || n.Contains("service")));
    }

    [Fact]
    public void Saving_or_deleting_a_profile_drops_the_switches_made_on_the_home_list()
    {
        var settings = new AppSettings();
        var session = new ProfileSession(settings);
        var p = session.Create(ProfileKind.Maximum);
        session.Activate(ProfileSession.KeyOfCustom(p.Id));
        session.SetAppChoice("Discord", close: false, profileSays: true);

        session.Save(p.Id, p.Name, ProfileKind.Maximum, p.Options);                  // the profile now runs with exactly what was saved
        Assert.Empty(session.AppChoices);

        session.SetAppChoice("Discord", close: false, profileSays: true);
        session.Delete(p.Id);
        Assert.Equal(ProfileSession.MaximumKey, session.ActiveKey);
        Assert.Empty(session.AppChoices);
    }

    // ------------------------------------------------------------ GoodbyeDPI is one program, the other bypass tools have their own switch

    [Fact]
    public void The_GoodbyeDPI_switch_closes_GoodbyeDPI_only_and_the_other_bypass_tools_follow_their_own_switch()
    {
        using var env = new TestEnv(ProfileKind.Maximum);
        env.Sys.AddProc("goodbyedpi", @"C:\Tools\GoodbyeDPI\goodbyedpi.exe");
        env.Sys.AddProc("zapret", @"C:\Tools\Zapret\zapret.exe");

        env.Settings.Options["proc.dpi"] = false;
        var p1 = env.BuildPlan();
        Assert.DoesNotContain(p1.Active, a => a.Id == "proc:goodbyedpi");
        Assert.Contains(p1.Active, a => a.Id == "proc:zapret");                       // its own switch is still on

        env.Settings.Options["proc.dpi"] = true; env.Settings.Options["proc.bypass"] = false;
        var p2 = env.BuildPlan();
        Assert.Contains(p2.Active, a => a.Id == "proc:goodbyedpi");
        Assert.DoesNotContain(p2.Active, a => a.Id == "proc:zapret");
    }

    [Fact]
    public void A_profile_saved_before_the_split_closes_exactly_what_it_closed_before()
    {
        // the old single "proc.dpi" switch covered GoodbyeDPI, Discord and the other bypass tools
        var off = ProfileSession.Complete(new Dictionary<string, bool> { ["proc.dpi"] = false }, ProfileKind.Maximum);
        Assert.False(off["proc.dpi"]); Assert.False(off["proc.bypass"]); Assert.False(off["proc.discord"]);
        var on = ProfileSession.Complete(new Dictionary<string, bool> { ["proc.dpi"] = true }, ProfileKind.Stream);
        Assert.True(on["proc.bypass"]); Assert.True(on["proc.discord"]);
        var split = ProfileSession.Complete(new Dictionary<string, bool> { ["proc.dpi"] = true, ["proc.bypass"] = false }, ProfileKind.Maximum);
        Assert.True(split["proc.dpi"]); Assert.False(split["proc.bypass"]);          // a profile that knows the new switch is left as it is
    }

    // ------------------------------------------------------------ Başlat reopens nothing

    private static async Task CloseWith(TestEnv env, Orchestrator orc)
    {
        orc.SettleMs = 0;
        await orc.PrepareAsync(env.BuildPlan(), env.Settings, CancellationToken.None);
    }

    [Fact]
    public async Task A_new_Basla_undoes_the_settings_but_neither_reopens_nor_keeps_the_programs_of_the_earlier_run()
    {
        using var env = new TestEnv(ProfileKind.Maximum);
        await CloseWith(env, env.Orc);
        Assert.DoesNotContain(env.Sys.Procs, p => p.Name == "OneDrive");
        Assert.Equal(ServiceRunState.Stopped, env.Sys.Services["wuauserv"].State);

        var orc2 = env.Restart();
        var prev = orc2.Revert(env.Settings, restartApps: false, forgetPrograms: true);

        Assert.True(prev.Complete);
        Assert.Empty(prev.Carry);
        Assert.Equal(ServiceRunState.Running, env.Sys.Services["wuauserv"].State);    // settings and services are back
        Assert.DoesNotContain(env.Sys.Procs, p => p.Name is "OneDrive" or "Dropbox" or "chrome");   // no program came back
        Assert.False(orc2.SessionActive);                                             // nothing is left that a later restore could reopen
        await CloseWith(env, orc2);                                                   // and the new run starts without complaint
    }

    [Fact]
    public async Task The_restore_button_knows_whether_there_are_programs_to_reopen()
    {
        using var hafif = new TestEnv(ProfileKind.Safe);
        await CloseWith(hafif, hafif.Orc);                                                        // Hafif closes no program
        Assert.True(hafif.Orc.SessionActive);
        Assert.Equal(0, hafif.Orc.PendingPrograms());                                             // the button then says it only restores settings

        using var tam = new TestEnv(ProfileKind.Maximum);
        await CloseWith(tam, tam.Orc);
        Assert.True(tam.Orc.PendingPrograms() > 0);
        Assert.True(tam.Orc.Revert(tam.Settings).Complete);
        Assert.Equal(0, tam.Orc.PendingPrograms());
    }

    [Fact]
    public async Task A_new_Basla_whose_undo_fails_keeps_the_programs_for_the_restore_button()
    {
        using var env = new TestEnv(ProfileKind.Maximum);
        env.Settings.Options["proc.browsers"] = false;
        await CloseWith(env, env.Orc);
        Assert.DoesNotContain(env.Sys.Procs, p => p.Name == "OneDrive");

        // the undo of the earlier run fails halfway (the power plan cannot be set back)
        var failing = new Orchestrator(new CrashingSystem(env.Sys, failOn: "setactive"), new SessionStore(), env.Log) { SettleMs = 0 };
        var prev = failing.Revert(env.Settings, restartApps: false, forgetPrograms: true);
        Assert.False(prev.Complete);

        var pending = new SessionStore().LoadPending();                                           // the journal is kept, and so are the programs in it
        Assert.NotNull(pending);
        Assert.Contains(pending!.Processes, p => p.Name == "onedrive" && p.CanReopen && !p.Reverted);
        Assert.True(env.Restart().PendingPrograms() > 0);

        var fin = env.Restart().Revert(env.Settings);                                             // the restore button still brings them back
        Assert.True(fin.Complete, string.Join("; ", fin.Problems));
        Assert.Contains(env.Sys.Procs, p => p.Name == "OneDrive");
    }

    [Fact]
    public async Task The_forgetting_undo_of_a_new_Basla_speaks_the_agreed_vocabulary_and_says_nothing_about_programs_coming_back()
    {
        using var env = new TestEnv(ProfileKind.Maximum);
        await CloseWith(env, env.Orc);
        var before = env.Log.Lines.Count;
        var res = env.Restart().Revert(env.Settings, restartApps: false, forgetPrograms: true);
        Assert.True(res.Complete);
        var lines = env.Log.Lines.Skip(before).Where(l => !l.StartsWith("D")).Select(l => l[2..]).ToList();
        Assert.Contains("Önceki çalıştırmanın ayarları geri yükleniyor…", lines);
        Assert.Contains("Önceki ayarlar eski haline döndü.", lines);
        Assert.DoesNotContain(lines, l => l.Contains("başlatıldı") || l.Contains("açılmadı") || l.Contains("açılamadı"));
    }

    [Fact]
    public async Task The_restore_button_reopens_only_what_the_latest_run_closed()
    {
        using var env = new TestEnv(ProfileKind.Maximum);
        await CloseWith(env, env.Orc);                                                // run 1 closes OneDrive, Spotify, ...
        env.Sys.AddProc("Spotify", @"C:\Program Files\Spotify\Spotify.exe", window: true);   // the user opens Spotify again

        var orc2 = env.Restart();
        Assert.True(orc2.Revert(env.Settings, restartApps: false, forgetPrograms: true).Complete);
        await CloseWith(env, orc2);                                                   // run 2 closes Spotify (and nothing else is left to close)
        Assert.DoesNotContain(env.Sys.Procs, p => p.Name == "Spotify");

        var restored = orc2.Revert(env.Settings);
        Assert.True(restored.Complete, string.Join("; ", restored.Problems));
        Assert.Contains(env.Sys.Procs, p => p.Name == "Spotify");                     // the latest run's program is back
        Assert.DoesNotContain(env.Sys.Procs, p => p.Name == "OneDrive");              // run 1's programs are not
    }
}
