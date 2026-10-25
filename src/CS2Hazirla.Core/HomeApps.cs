namespace CS2Hazirla.Core;

/// <summary>One row of the home screen's list: a program (or all browsers together) that is running now and could be closed without touching the game.</summary>
/// <param name="Key">What a choice is remembered under: the program's readable name, or <see cref="HomeApps.BrowsersKey"/>.</param>
/// <param name="OptionId">The switch (category) that would close it; it decides the row's default.</param>
public sealed record HomeApp(string Key, string Display, string OptionId, IReadOnlyList<string> Names, string? Path, long Bytes);

/// <summary>
/// The home screen lists what is really on this computer, found by a quick scan when Antilag opens (nothing is predefined): the programs a person
/// opens and knows - Discord, GoodbyeDPI, browsers, Spotify, WhatsApp, Teams, game stores, VPN apps and any other open application.
/// Background helpers (updaters, sync clients, brand and lighting tools, overlays, antivirus, anti-cheat parts, unknown tray programs) are not
/// listed; their switches live in Ayrıntılar and keep closing what they always closed. Each row is a switch: the mode says what it starts as,
/// a click on it changes only this program, for this run of the app.
/// </summary>
public static class HomeApps
{
    public const string BrowsersKey = "browsers";

    // categories whose programs have a face; GoodbyeDPI is its own switch, the other block-bypass tools (proc.bypass) are not listed
    private static readonly HashSet<string> Listed = new()
    {
        "proc.discord", "proc.dpi", "proc.browsers", "proc.launchers", "proc.chat", "proc.claude", "proc.vpn", "proc.windowed",
    };

    /// <summary>Settings with every switch on: the plan they give lists everything closable, whatever the chosen mode is. Stream protection (OBS, Discord) follows <paramref name="profile"/>.</summary>
    public static AppSettings ScanSettings(AppSettings s, ProfileKind profile) => new()
    {
        Profile = profile,
        Options = Catalog.Options.ToDictionary(o => o.Id, _ => true),
        NeverClose = s.NeverClose,
        ExtraProcesses = s.ExtraProcesses,
    };

    /// <summary>The rows for what a scan found: Discord, GoodbyeDPI, then the rest with the biggest program first.</summary>
    public static IReadOnlyList<HomeApp> From(IEnumerable<PlannedAction> scan)
    {
        var rows = new List<HomeApp>();
        var browsers = new List<ProcessPayload>();
        foreach (var a in scan)
        {
            if (a.Kind != ActionKind.CloseProcess || a.Payload is not ProcessPayload p || p.OptionId == null || !Listed.Contains(p.OptionId)) continue;
            if (ReopenPolicy.IsHelper(p.Name)) continue;                  // a web helper, a crash reporter, a service executable: the program it belongs to is the row
            if (p.OptionId == "proc.browsers") { browsers.Add(p); continue; }
            rows.Add(new HomeApp(p.Display ?? p.Name, p.Display ?? p.Name, p.OptionId, new[] { p.Name }, p.Path, p.Bytes));
        }

        // one program that runs under two exe names (Teams and its new version) is one row
        var merged = rows.GroupBy(r => (r.Key.ToLowerInvariant(), r.OptionId))
            .Select(g => g.Count() == 1 ? g.First()
                : new HomeApp(g.First().Key, g.First().Display, g.Key.OptionId, g.SelectMany(x => x.Names).Distinct().ToList(), g.First().Path, g.Sum(x => x.Bytes)))
            .ToList();
        if (browsers.Count > 0)
            // "Tarayıcılar" stays Turkish here (it is the row's Display, which the scan keeps and sorts by); the row translates it where it is shown (HomeAppVM.Title)
            merged.Add(new HomeApp(BrowsersKey, "Tarayıcılar", "proc.browsers", browsers.Select(b => b.Name).Distinct().ToList(), browsers[0].Path, browsers.Sum(b => b.Bytes)));

        static int Rank(HomeApp r) => r.OptionId switch { "proc.discord" => 0, "proc.dpi" => 1, _ => 2 };
        return merged.OrderBy(Rank).ThenByDescending(r => r.Bytes).ThenBy(r => r.Display, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    /// <summary>In Yayıncı some programs (Discord, OBS...) can never be closed: their row shows "kept open" and cannot be switched.</summary>
    public static bool IsLocked(HomeApp app, AppSettings s) =>
        app.Names.All(n => ProtectedList.IsProcessProtected(n, app.Path, s.Profile == ProfileKind.Stream));

    /// <summary>What the chosen mode says about this program, before any click of the user.</summary>
    public static bool ModeCloses(HomeApp app, AppSettings s) => !IsLocked(app, s) && s.IsOn(app.OptionId);

    /// <summary>What the row shows: the user's own switch when there is one, the mode otherwise.</summary>
    public static bool Closes(HomeApp app, AppSettings s, IReadOnlyDictionary<string, bool> choices) =>
        !IsLocked(app, s) && (choices.TryGetValue(app.Key, out var on) ? on : s.IsOn(app.OptionId));

    /// <summary>
    /// Puts the user's switches into a plan that the mode produced: a program switched off is not closed; a program switched on is closed even when
    /// the mode would leave it (it is taken from <paramref name="everything"/>, the scan with every switch on, when the plan has no entry for it).
    /// A program that is not running any more is simply not there.
    /// </summary>
    public static void Apply(Plan plan, IReadOnlyList<PlannedAction>? everything, IReadOnlyDictionary<string, bool> choices)
    {
        foreach (var (key, on) in choices)
        {
            bool Matches(PlannedAction a) => a.Kind == ActionKind.CloseProcess && a.Payload is ProcessPayload p &&
                (key == BrowsersKey ? p.OptionId == "proc.browsers" : string.Equals(p.Display ?? p.Name, key, StringComparison.CurrentCultureIgnoreCase));

            var inPlan = plan.Actions.Where(Matches).ToList();
            if (!on) { foreach (var a in inPlan) a.Enabled = false; continue; }

            foreach (var a in inPlan) a.Enabled = true;
            foreach (var a in (everything ?? Array.Empty<PlannedAction>()).Where(Matches))
            {
                if (plan.Actions.Any(x => x.Id == a.Id)) continue;
                a.Enabled = true;
                plan.Actions.Add(a);
            }
        }
    }
}
