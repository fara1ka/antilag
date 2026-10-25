using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using CS2Hazirla.Core;

namespace CS2Hazirla;

/// <summary>A user-facing message for the activity list, with the raw exception text going to the log file only.</summary>
public static class LogExtensions
{
    public static void Error(this ILog log, string message, Exception ex) { log.Error(message); log.Detail(ex.ToString()); }
}

public static class UiBrushes
{
    public static Brush Get(string key) => (Brush)Application.Current.FindResource(key);

    public static Brush ForTone(Tone tone) => Get(tone switch { Tone.Good => "GoodBrush", Tone.Warn => "WarnBrush", Tone.Bad => "BadBrush", _ => "TextBrush" });
}

// =====================================================================================================================
// options: one switch row = a name, a short hint only where the name is not clear, a caution only where it can cost something
// =====================================================================================================================

public class OptionVM : ObservableObject
{
    private readonly AppSettings _s;
    public OptionDef Def { get; }
    public OptionVM(OptionDef def, AppSettings s)
    {
        Def = def; _s = s;
        // what a search finds: the name, the hint, and the programs / services behind the switch ("dropbox" finds "Yedekleme programları")
        var words = new[] { def.Title, def.Hint, def.Caution }.Concat(Catalog.SearchTerms(def.Id)).Where(t => !string.IsNullOrEmpty(t)).ToList();
        // in English the words that are on screen are searchable too (the Turkish ones stay: people type either); texts without an English entry (program names) add nothing
        if (Loc.IsEnglish)
            words.AddRange(words.Select(t => EnglishStrings.Map.TryGetValue(t!, out var en) ? en : null).Where(t => !string.IsNullOrEmpty(t)).ToList());
        SearchKey = SearchText.Normalize(string.Join(" ", words));
    }

    public string SearchKey { get; }

    public string Title => Loc.T(Def.Title);
    public string Hint => string.IsNullOrEmpty(Def.Hint) ? "" : Loc.T(Def.Hint);
    public bool HasHint => !string.IsNullOrEmpty(Def.Hint);
    public string Caution => string.IsNullOrEmpty(Def.Caution) ? "" : Loc.T(Def.Caution);
    public bool HasCaution => !string.IsNullOrEmpty(Def.Caution);

    /// <summary>Called after the user flips the switch.</summary>
    public Action? Changed { get; set; }

    public bool IsOn
    {
        get => _s.IsOn(Def.Id);
        set { _s.Options[Def.Id] = value; Raise(); Changed?.Invoke(); }
    }

    public void Refresh() => Raise(nameof(IsOn));
}

/// <summary>One line of the activity list. Errors are red, skipped/warning lines amber, finished steps green; the marker says it too, so colour is never the only signal.</summary>
public class LogEntry
{
    public string Time { get; init; } = "";
    public string Text { get; init; } = "";
    /// <summary>"error", "warn", "ok" or "info"</summary>
    public string Kind { get; init; } = "info";
    public Brush TextBrush => UiBrushes.Get(Kind switch { "error" => "BadBrush", "warn" => "WarnBrush", _ => "TextBrush" });
    public string Prefix => Kind switch { "error" => "✗", "warn" => "!", "ok" => "✓", _ => "›" };
    public Brush PrefixBrush => UiBrushes.Get(Kind switch { "error" => "BadBrush", "warn" => "WarnBrush", "ok" => "GoodBrush", _ => "Text3Brush" });

    public static LogEntry From(string time, string level, string message)
    {
        var kind = level switch { "HATA" => "error", "UYARI" => "warn", _ => "info" };       // the levels are the file log's own words, not texts for people
        var text = message;
        // the closing line of a run ("Bitti. ..." / "Done. ...") turns green: it is recognised by the same word the run writes it with
        if (kind == "info" && (text.StartsWith("✓") || text.StartsWith("✅") || text.StartsWith("Bitti.") || text.StartsWith(Loc.T("Bitti."))))
        {
            kind = "ok";
            text = text.TrimStart('✓', '✅', ' ');
        }
        return new LogEntry { Time = time, Text = text, Kind = kind };
    }
}

public class OptionGroupVM
{
    public string Title { get; init; } = "";
    public List<OptionVM> Items { get; init; } = new();
}

// =====================================================================================================================
// small view models
// =====================================================================================================================

public class MetricRowVM
{
    public string Label { get; init; } = "";
    public string Before { get; init; } = "";
    public string After { get; init; } = "";
    public string Delta { get; init; } = "";
    public Brush DeltaBrush { get; init; } = Brushes.Gray;
    /// <summary>A sentence instead of numbers (e.g. "internet gecikmesi ölçülemedi"): shown across the whole row, wrapped.</summary>
    public bool IsNote => After.Length == 0 && Delta.Length == 0;
    public bool IsNumbers => !IsNote;
}

public class MemRowVM
{
    public string Name { get; init; } = "";
    public string CountText { get; init; } = "";
    public string MbText { get; init; } = "";
    public bool IsProtected { get; init; }
    public bool CanClose => !IsProtected;
    public string Tag => IsProtected ? Loc.T("korumalı") : "";
    /// <summary>What a screen reader says for the Kapat button of this row.</summary>
    public string CloseLabel => Loc.F("Kapat: {0}", Name);
    public RelayCommand? CloseCommand { get; set; }
}

public class ServiceRuleVM : ObservableObject
{
    public string Name { get; init; } = "";
    public string Title { get; init; } = "";
    public string Plain { get; init; } = "";
    public string Group { get; init; } = "other";
    private bool _on = true;
    public bool IsEnabled { get => _on; set => Set(ref _on, value); }
    /// <summary>What a screen reader says for the Çıkar button of this row.</summary>
    public string RemoveLabel => Loc.F("Çıkar: {0}", Title);
}

public class AppItemVM
{
    public string Key { get; init; } = "";
    public string Display { get; init; } = "";
    public string RemoveLabel => Loc.F("Çıkar: {0}", Display);
}

/// <summary>A list the user builds by PICKING from what is installed/running, never by typing names.</summary>
public class AppListVM : ObservableObject
{
    public string Title { get; init; } = "";
    public string Hint { get; init; } = "";
    public string AddLabel { get; init; } = Loc.T("Listeden seç");
    public string EmptyText { get; init; } = Loc.T("Henüz bir şey eklemedin.");
    public ObservableCollection<AppItemVM> Items { get; } = new();
    public RelayCommand AddCommand { get; }
    public RelayCommand RemoveCommand { get; }
    public bool IsEmpty => Items.Count == 0;

    public AppListVM(Action<AppListVM> pick)
    {
        AddCommand = new RelayCommand(() => pick(this));
        RemoveCommand = new RelayCommand(o => { if (o is AppItemVM a) { Items.Remove(a); Raise(nameof(IsEmpty)); } });
        Items.CollectionChanged += (_, _) => Raise(nameof(IsEmpty));
    }

    public void Add(string key, string display)
    {
        if (Items.Any(i => string.Equals(i.Key, key, StringComparison.OrdinalIgnoreCase))) return;
        Items.Add(new AppItemVM { Key = key, Display = display });
    }
}

public class PickItemVM : ObservableObject
{
    public string Key { get; init; } = "";
    public string Title { get; init; } = "";
    public string Subtitle { get; init; } = "";
    private bool _checked;
    public bool IsChecked { get => _checked; set => Set(ref _checked, value); }
}

public class PickerRequest
{
    public string Title { get; init; } = "";
    public string Hint { get; init; } = "";
    public List<PickItemVM> Items { get; init; } = new();
}

/// <summary>One row of the preview: what Başlat will do. Text only; the preview has nothing to click or tick.</summary>
public class PlannedActionVM
{
    private readonly PlannedAction _a;
    public PlannedActionVM(PlannedAction a) => _a = a;

    public string Group => _a.Group;
    public string Title => _a.Title;
    public string Detail => _a.Detail;
}

/// <summary>One row of the home list: the program's name and a switch. The switch is "this program is closed by the next Başlat".</summary>
public class HomeAppVM : ObservableObject
{
    private readonly MainViewModel _vm;
    private bool _on;
    private bool _locked;
    private bool _idle = true;

    public HomeAppVM(HomeApp app, MainViewModel vm) { App = app; _vm = vm; }

    public HomeApp App { get; }
    // a program keeps the name it has (Discord, Spotify...); only the row that gathers all browsers is a Turkish text of the app
    public string Title => App.Key == HomeApps.BrowsersKey ? Loc.T(App.Display) : App.Display;

    public bool IsOn { get => _on; set { if (value != _on) _vm.SwitchHomeApp(App, value); } }
    /// <summary>The row can be switched: nothing is running, and the program is not one the chosen mode always keeps open (Discord in Yayıncı).</summary>
    public bool CanSwitch => _idle && !_locked;

    public void Update(bool on, bool locked, bool idle)
    {
        _on = on; _locked = locked; _idle = idle;
        Raise(nameof(IsOn)); Raise(nameof(CanSwitch));
    }
}

public enum Phase { Idle, Preparing, Reverting }

public enum Page { Home, Profiles, Settings }

public enum DrawerTab { Summary, Full, Metrics, Memory }

// =====================================================================================================================
// main view model
// =====================================================================================================================

public partial class MainViewModel : ObservableObject
{
    private readonly Dispatcher _ui;
    private readonly WindowsSystem _sys = new();
    private readonly SettingsStore _settingsStore = new();
    private readonly SessionStore _store = new();
    private readonly FileLog _log = new();
    private readonly Orchestrator _orc;
    private readonly List<OptionVM> _allOptions = new();

    public AppSettings Settings { get; }
    public bool IsElevated { get; } = WindowsSystem.IsElevated();

    public ObservableCollection<OptionGroupVM> Groups { get; } = new();
    public ObservableCollection<LogEntry> LogLines { get; } = new();
    public bool HasLog => LogLines.Count > 0;
    public bool HasMetrics => Metrics.Count > 0;
    /// <summary>The "after" column of Ölçümler. The Turkish word "Sonra" is also the update card's "Later" button, and one Turkish text has one English, so this header is chosen here and not in the dictionary.</summary>
    public string MetricsAfterHeader => Loc.IsEnglish ? "After" : "Sonra";
    public ObservableCollection<MetricRowVM> Metrics { get; } = new();
    public ObservableCollection<MemRowVM> TopMemory { get; } = new();
    public ObservableCollection<string> InfoLines { get; } = new();
    public ObservableCollection<ServiceRuleVM> ServiceRules { get; } = new();
    public AppListVM KeepOpenList { get; }
    /// <summary>The "Yeni sürüm var" card and the update row of Ayarlar.</summary>
    public UpdateVM Update { get; }
    public AppListVM AlwaysCloseList { get; }

    public RelayCommand PrepareCommand { get; }
    public RelayCommand PreviewCommand { get; }
    public RelayCommand RevertCommand { get; }
    public RelayCommand RefreshMemoryCommand { get; }
    public RelayCommand CloseAppCommand { get; }
    public RelayCommand ToggleDrawerCommand { get; }
    public RelayCommand CloseDrawerCommand { get; }
    public RelayCommand AddServiceCommand { get; }
    public RelayCommand RemoveServiceCommand { get; }

    public Action<PreviewViewModel>? ShowPreview { get; set; }
    public Func<PickerRequest, IReadOnlyList<string>>? ShowPicker { get; set; }
    /// <summary>Raised when the user presses "Uygulamayı kapat" (or when CS2 is already running at start).</summary>
    public event Action? ExitRequested;

    /// <param name="carry">Set when this window replaces another one (the language was changed): the switches the user made in this run stay.</param>
    public MainViewModel(Dispatcher ui, ProfileSession.Snapshot? carry = null)
    {
        _ui = ui;
        AppPaths.Ensure();
        FileLog.PruneOldLogs();
        Settings = _settingsStore.Load();
        _profiles = new ProfileSession(Settings);          // every start begins with the defaults of "Tam temizlik"
        if (carry != null) _profiles.Restore(carry);
        _orc = new Orchestrator(_sys, _store, _log);
        _orc.Progress = (done, total) => _ui.BeginInvoke(() => SetSteps(done, total));

        // Steam install + library folders: anything living there is protected whatever its name
        try
        {
            var steam = SteamLocator.FindSteamPath();
            if (steam != null) ProtectedList.AddProtectedRoots(SteamLocator.GetLibraryFolders(steam));
        }
        catch { /* protection by name still applies */ }

        LogLines.CollectionChanged += (_, _) => { Raise(nameof(HasLog)); Raise(nameof(ShowLog)); };
        Metrics.CollectionChanged += (_, _) => Raise(nameof(HasMetrics));
        _log.Entry += (level, msg) => _ui.BeginInvoke(() =>
        {
            LogLines.Add(LogEntry.From(DateTime.Now.ToString("HH:mm:ss"), level, msg));
            while (LogLines.Count > 600) LogLines.RemoveAt(0);
        });

        foreach (var def in Catalog.Options)
            _allOptions.Add(new OptionVM(def, Settings) { Changed = () => OnOptionChanged(def.Id) });
        foreach (var g in _allOptions.GroupBy(o => o.Def.Group))
            Groups.Add(new OptionGroupVM { Title = Loc.T(g.Key), Items = g.ToList() });       // grouped by the Turkish identity, shown in the chosen language
        ClearSearchCommand = new RelayCommand(() => Search = "");
        RebuildShown();

        KeepOpenList = new AppListVM(l => _ = PickAppsAsync(l, Loc.T("Her zaman açık kalacak programları seç"),
            Loc.T("Çalışan programlar listelendi. Kapanmasını asla istemediklerini işaretle.")))
        {
            Title = Loc.T("Her zaman açık kalsın"),
            Hint = Loc.T("Buraya eklediğin programlar, hangi ayar seçili olursa olsun kapatılmaz."),
            EmptyText = Loc.T("Henüz bir şey eklemedin. Her zaman açık kalmasını istediğin bir program varsa ekle."),
        };
        AlwaysCloseList = new AppListVM(l => _ = PickAppsAsync(l, Loc.T("Her seferinde kapanacak programları seç"),
            Loc.T("Çalışan programlar listelendi. Gruplarda olmayan ama her seferinde kapanmasını istediklerini işaretle.")))
        {
            Title = Loc.T("Her seferinde kapat"),
            Hint = Loc.T("Gruplarda olmayan ama her seferinde kapanmasını istediğin programlar."),
        };
        foreach (var n in Settings.NeverClose) KeepOpenList.Add(n, DisplayFor(n));
        foreach (var n in Settings.ExtraProcesses) AlwaysCloseList.Add(n, DisplayFor(n));
        foreach (var r in Settings.Services) ServiceRules.Add(MakeRule(r));
        KeepOpenList.Items.CollectionChanged += (_, _) => _scanStale = true;        // "Her zaman açık kalsın" decides what the scan may list
        AlwaysCloseList.Items.CollectionChanged += (_, _) => _scanStale = true;

        PrepareCommand = new RelayCommand(() => _ = RunAsync(), () => IsIdle);
        PreviewCommand = new RelayCommand(() => _ = PreviewAsync(), () => IsIdle);
        RevertCommand = new RelayCommand(() => _ = UserRevertAsync(), () => IsIdle && (_orc.SessionActive || _demoSession));
        RefreshMemoryCommand = new RelayCommand(() => _ = RefreshTopMemoryAsync());
        CloseAppCommand = new RelayCommand(() => ExitRequested?.Invoke(), () => IsIdle);
        ToggleDrawerCommand = new RelayCommand(() => DrawerOpen = !DrawerOpen);
        CloseDrawerCommand = new RelayCommand(() => DrawerOpen = false);
        AddServiceCommand = new RelayCommand(() => _ = PickServicesAsync());
        RemoveServiceCommand = new RelayCommand(o => { if (o is ServiceRuleVM v) ServiceRules.Remove(v); });
        RescanCommand = new RelayCommand(() => _ = RescanAsync(), () => IsIdle);
        // the flow looks at the game from the download thread too (once a second): that look goes through the window's thread, because it also refreshes the headline
        Update = new UpdateVM(new UpdateFlow(new UpdateClient(UpdateVM.Current), _sys, _log, () => ui.CheckAccess() ? GameRunningNow() : ui.Invoke(GameRunningNow)), ui, Settings,
                              () => IsIdle, () => ExitRequested?.Invoke(), () => SaveSettings(), UpdateBusyChanged, FlushProfileEdits);
        InitProfiles();
    }

    // ---------------------------------------------------------------- pages and the Ayrıntılar panel

    private Page _page = Page.Home;
    public bool PageHome { get => _page == Page.Home; set { if (value) GoTo(Page.Home); } }
    public bool PageProfiles { get => _page == Page.Profiles; set { if (value) GoTo(Page.Profiles); } }
    public bool PageSettings { get => _page == Page.Settings; set { if (value) GoTo(Page.Settings); } }

    public void GoTo(Page page)
    {
        if (_page == page) return;
        if (_page == Page.Profiles) FlushProfileEdits();      // edits of a profile are kept, never dropped silently
        _page = page;
        if (page != Page.Home) DrawerOpen = false;
        else RescanIfStale();
        Raise(nameof(PageHome)); Raise(nameof(PageProfiles)); Raise(nameof(PageSettings));
        SaveSettings();
    }

    private bool _drawerOpen;
    public bool DrawerOpen
    {
        get => _drawerOpen;
        set
        {
            if (!Set(ref _drawerOpen, value)) return;
            if (!value && HasSearch) Search = "";                // a closed panel opens clean next time
            if (value && _drawerTab == DrawerTab.Memory) _ = RefreshTopMemoryAsync();
        }
    }

    // "Özet": the programs that are closed. "Tam liste": everything (programs, Windows services, Windows settings, internet). The search always looks through everything.
    private DrawerTab _drawerTab = DrawerTab.Summary;
    public bool TabSummary { get => _drawerTab == DrawerTab.Summary; set { if (value) SetTab(DrawerTab.Summary); } }
    public bool TabFull { get => _drawerTab == DrawerTab.Full; set { if (value) SetTab(DrawerTab.Full); } }
    public bool ShowSelections => _drawerTab is DrawerTab.Summary or DrawerTab.Full;
    /// <summary>The Wi-Fi / memory-speed notes close the full list; they do not belong between search results.</summary>
    public bool ShowInfo => HasInfo && !HasSearch && _drawerTab == DrawerTab.Full;
    /// <summary>Özet lists programs only; one line at its end says where the rest is.</summary>
    public bool ShowSummaryHint => _drawerTab == DrawerTab.Summary && !HasSearch;
    public bool TabMetrics { get => _drawerTab == DrawerTab.Metrics; set { if (value) SetTab(DrawerTab.Metrics); } }
    public bool TabMemory { get => _drawerTab == DrawerTab.Memory; set { if (value) SetTab(DrawerTab.Memory); } }

    public void SetTab(DrawerTab tab)
    {
        if (_drawerTab == tab) return;
        _drawerTab = tab;
        RebuildShown();
        Raise(nameof(TabSummary)); Raise(nameof(TabFull)); Raise(nameof(TabMetrics)); Raise(nameof(TabMemory)); Raise(nameof(ShowSelections)); Raise(nameof(ShowInfo)); Raise(nameof(ShowSummaryHint));
        if (tab == DrawerTab.Memory) _ = RefreshTopMemoryAsync();
    }

    // ---------------------------------------------------------------- search in Ayrıntılar

    private string _search = "";
    public string Search { get => _search; set { if (Set(ref _search, value ?? "")) { RebuildShown(); Raise(nameof(HasSearch)); Raise(nameof(ShowInfo)); Raise(nameof(ShowSummaryHint)); } } }
    public bool HasSearch => _search.Trim().Length > 0;
    public RelayCommand ClearSearchCommand { get; }
    /// <summary>What the list shows right now: the tab's groups, or every row that matches the search.</summary>
    public ObservableCollection<OptionGroupVM> ShownGroups { get; } = new();
    public bool HasNoResults => HasSearch && ShownGroups.Count == 0;

    private void RebuildShown()
    {
        ShownGroups.Clear();
        foreach (var g in Groups)
        {
            if (!HasSearch && _drawerTab == DrawerTab.Summary && g.Title != Loc.T(Catalog.GroupApps)) continue;     // Özet: only the programs (the title is shown in the chosen language)
            var items = g.Items.Where(o => SearchText.Matches(_search, o.SearchKey)).ToList();
            if (items.Count > 0) ShownGroups.Add(new OptionGroupVM { Title = g.Title, Items = items });
        }
        Raise(nameof(HasNoResults));
    }

    // ---------------------------------------------------------------- presets

    public void SelectProfile(ProfileKind kind) => ActivateProfile(ProfileSession.KeyOf(kind));

    /// <summary>
    /// Switches the active profile without asking anything. What was changed in the profile that is left stays in it for this run,
    /// a profile that was not touched opens with its defaults (see <see cref="ProfileSession"/>).
    /// </summary>
    private void ActivateProfile(string key)
    {
        if (!IsIdle || !_profiles.Activate(key))
        {
            // the radio the user just clicked is already checked on screen: put the selection back
            _ui.BeginInvoke(RaiseProfileChoice);
            return;
        }
        foreach (var o in _allOptions) o.Refresh();
        _restore = RestoreOutcome.None;                 // a new choice: the result of the last restore is no longer the news
        _error = null;
        RaiseProfileChoice();
        RaiseStatus();
    }

    private void OnOptionChanged(string optionId)
    {
        // the user just decided about this whole group in Ayrıntılar: the switches of single programs of the group made earlier no longer apply
        foreach (var r in _scan.Where(a => a.OptionId == optionId)) _profiles.ClearAppChoice(r.Key);
        RaiseStatus();      // "Customized" and the headline follow the switches
    }

    private string DisplayFor(string key) =>
        Settings.DisplayNames.TryGetValue(key, out var d) && !string.IsNullOrWhiteSpace(d) ? d : FriendlyNames.ForProcess(key, null);

    private ServiceRuleVM MakeRule(ServiceRule r)
    {
        return new ServiceRuleVM
        {
            Name = r.Name, Group = r.Group, IsEnabled = r.Enabled,
            // (Catalog.ServiceTitle / ServicePlain give the catalog's texts in the chosen language themselves; the name Windows gave to a service the user added stays as it is)
            Title = Catalog.ServiceTitle(r.Name, Settings.DisplayNames.TryGetValue("svc:" + r.Name, out var d) ? d : null),
            Plain = Catalog.ServicePlain(r.Name) is { Length: > 0 } p ? p : Loc.T("Senin eklediğin hizmet."),
        };
    }

    // ---------------------------------------------------------------- settings page

    public bool RestartClosedApps
    {
        get => Settings.RestartClosedApps;
        set { Settings.RestartClosedApps = value; Raise(); Raise(nameof(RestoreTip)); }
    }

    private void AddKnown(AppListVM list, string key, string? display = null)
    {
        display ??= DisplayFor(key);
        Settings.DisplayNames[key] = display;
        // a program is either "always stays open" or "closed every time", never both. It joins the new list FIRST: removing it from the other
        // list saves the settings, and a name that is in no list at that moment would be forgotten.
        list.Add(key, display);
        var other = ReferenceEquals(list, KeepOpenList) ? AlwaysCloseList : KeepOpenList;
        foreach (var dup in other.Items.Where(i => string.Equals(i.Key, key, StringComparison.OrdinalIgnoreCase)).ToList()) other.Items.Remove(dup);
    }

    private async Task PickAppsAsync(AppListVM list, string title, string hint)
    {
        if (ShowPicker == null) return;
        if (GameRunningNow()) { WarnGameRunning(); return; }
        var stream = Settings.Profile == ProfileKind.Stream;
        var taken = new HashSet<string>(KeepOpenList.Items.Concat(AlwaysCloseList.Items).Select(i => i.Key), StringComparer.OrdinalIgnoreCase);
        // listing every process (and reading each file's description) takes a moment: not on the UI thread
        var items = await Task.Run(() => _sys.GetProcesses()
            .Where(p => p.SessionId == _sys.CurrentSessionId && p.Pid != _sys.CurrentProcessId)
            .GroupBy(p => ProtectedList.Normalize(p.Name))
            .Where(g => !taken.Contains(g.Key) && !g.Any(p => ProtectedList.IsProcessProtected(p.Name, p.Path, stream)))
            .Select(g =>
            {
                var path = g.Select(p => p.Path).FirstOrDefault(x => !string.IsNullOrEmpty(x));
                var bytes = g.Sum(p => p.WorkingSetBytes);
                return new { Key = g.Key, Bytes = bytes, Path = path, Win = g.Any(p => p.HasWindow) };
            })
            .OrderByDescending(x => x.Bytes)
            .Select(x => new PickItemVM
            {
                Key = x.Key,
                Title = FriendlyNames.ForProcess(x.Key, x.Path),
                Subtitle = x.Win ? Loc.F("{0} bellek kullanıyor · ekranda açık", FriendlyNames.Memory(x.Bytes))
                                 : Loc.F("{0} bellek kullanıyor · arka planda çalışıyor", FriendlyNames.Memory(x.Bytes)),
            }).ToList());

        if (items.Count == 0)
        {
            UiDialog.Show(Loc.T("Eklenebilecek başka bir program bulunamadı."), Loc.T("Listeden seç"), MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        var chosen = ShowPicker(new PickerRequest { Title = title, Hint = hint, Items = items });
        foreach (var key in chosen)
        {
            var item = items.FirstOrDefault(i => i.Key == key);
            if (item != null) AddKnown(list, key, item.Title);
        }
        if (chosen.Count > 0) SaveSettings();
    }

    private async Task PickServicesAsync()
    {
        if (ShowPicker == null) return;
        if (GameRunningNow()) { WarnGameRunning(); return; }
        var taken = new HashSet<string>(ServiceRules.Select(r => r.Name), StringComparer.OrdinalIgnoreCase);
        var items = await Task.Run(() => _sys.GetRunningServices()
            .Where(s => !taken.Contains(s.Name) && !ProtectedList.IsServiceProtected(s.Name))
            .OrderBy(s => s.DisplayName)
            .Select(s => new PickItemVM
            {
                Key = s.Name,
                Title = Catalog.ServiceTitle(s.Name, s.DisplayName),
                Subtitle = Catalog.ServicePlain(s.Name) is { Length: > 0 } plain ? plain : s.Name,
            }).ToList());
        if (items.Count == 0)
        {
            UiDialog.Show(Loc.T("Eklenebilecek başka bir hizmet bulunamadı."), Loc.T("Listeden seç"), MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        var chosen = ShowPicker(new PickerRequest
        {
            Title = Loc.T("Geçici durdurulacak hizmetleri seç"),
            Hint = Loc.T("Çalışan Windows hizmetleri listelendi. Bilmediğin bir hizmeti seçme. Hizmetler silinmez, yalnızca durdurulur."),
            Items = items,
        });
        foreach (var key in chosen)
        {
            var item = items.FirstOrDefault(i => i.Key == key);
            if (item == null) continue;
            // settings.json stays the same in both languages: the catalog's Turkish name (or the name Windows gave), never the English one
            Settings.DisplayNames["svc:" + key] = Catalog.ServiceTexts.TryGetValue(key, out var catalogText) ? catalogText.Title : item.Title;
            ServiceRules.Add(MakeRule(new ServiceRule { Name = key, Group = "other", Enabled = true }));
        }
        if (chosen.Count > 0) SaveSettings();
    }

    public void SyncToSettings()
    {
        Settings.NeverClose = KeepOpenList.Items.Select(i => i.Key).ToList();
        Settings.ExtraProcesses = AlwaysCloseList.Items.Select(i => i.Key).ToList();
        Settings.Services = ServiceRules.Select(r => new ServiceRule { Name = r.Name, Group = r.Group, Enabled = r.IsEnabled }).ToList();
        // keep readable names only for entries that still exist
        var live = new HashSet<string>(Settings.NeverClose.Concat(Settings.ExtraProcesses).Concat(Settings.Services.Select(r => "svc:" + r.Name)),
                                       StringComparer.OrdinalIgnoreCase);
        foreach (var k in Settings.DisplayNames.Keys.Where(k => !live.Contains(k)).ToList()) Settings.DisplayNames.Remove(k);
    }

    /// <summary>Writes settings.json. Returns false (and logs) when it could not be written.</summary>
    public bool SaveSettings()
    {
        try { SyncToSettings(); _settingsStore.Save(Settings); return true; }
        catch (Exception ex) { _log.Error(Loc.T("Ayarlar kaydedilemedi."), ex); return false; }
    }

    // ---------------------------------------------------------------- phase / headline

    private Phase _phase = Phase.Idle;
    public Phase Phase
    {
        get => _phase;
        private set
        {
            if (!Set(ref _phase, value)) return;
            Raise(nameof(IsIdle)); Raise(nameof(IsBusy));
            if (value == Phase.Idle) SetSteps(0, 0);
            RaiseStatus();
            if (value != Phase.Idle) DrawerOpen = false;       // the activity is what matters while something runs
            PrepareCommand.Refresh(); PreviewCommand.Refresh(); RevertCommand.Refresh(); CloseAppCommand.Refresh();
            RefreshProfileCommands();
            foreach (var row in TopMemory) row.CloseCommand?.Refresh();
        }
    }

    // a preview plan is being built or shown: Başlat/Önizleme must not be started a second time meanwhile
    private bool _previewing;
    private void SetPreviewing(bool on)
    {
        _previewing = on;
        PrepareCommand.Refresh(); PreviewCommand.Refresh(); Update?.Refresh();
        RefreshProfileCommands();
        Raise(nameof(IsIdle));            // the switches, presets and profiles are locked while the preview plan is being built
        RefreshHomeRows();
    }

    // a program is being closed from the Bellek list (it is journaled like a run: nothing else may start meanwhile)
    private bool _manualClosing;
    private void SetManualClosing(bool on)
    {
        _manualClosing = on;
        PrepareCommand.Refresh(); PreviewCommand.Refresh(); RevertCommand.Refresh(); CloseAppCommand.Refresh(); Update?.Refresh();
        RefreshProfileCommands();
        foreach (var row in TopMemory) row.CloseCommand?.Refresh();
        Raise(nameof(IsIdle));
        RefreshHomeRows();
    }

    /// <summary>Nothing is running and no preview is being built: the switches, presets and profiles may be changed.</summary>
    public bool IsIdle => Phase == Phase.Idle && !_previewing && !_manualClosing && Update?.IsWorking != true;

    // an update is downloading or starting its installer: like a preview or a manual close, nothing else may start meanwhile
    private void UpdateBusyChanged()
    {
        PrepareCommand.Refresh(); PreviewCommand.Refresh(); RevertCommand.Refresh(); CloseAppCommand.Refresh();
        RefreshProfileCommands();
        Raise(nameof(IsIdle));
        RaiseStatus();                    // also refreshes the list rows and the scan button
        foreach (var row in TopMemory) row.CloseCommand?.Refresh();
    }
    public bool IsBusy => Phase != Phase.Idle;

    private bool _gameRunning;
    private bool _done;
    private int _failedCount;
    private int _doneCount;
    private List<string> _notClosed = new();
    private RestoreOutcome _restore = RestoreOutcome.None;
    private List<string> _notRestarted = new();
    private string? _error;
    // true while a new Başlat first undoes the settings of the previous one (programs are NOT reopened then: the new run closes them again)
    private bool _quietRevert;

    /// <summary>true after a run finished: the big button becomes "Uygulamayı kapat".</summary>
    public bool IsDone { get => _done; private set { if (Set(ref _done, value)) RaiseStatus(); } }

    /// <summary>"Geri yükle" only means something while a run is still in effect.</summary>
    public bool HasSession => _orc.SessionActive || _demoSession;
    private bool _demoSession;       // developer screenshots only

    private StatusView CurrentStatus() => Status.Compute(new StatusInput(
        Busy: Phase != Phase.Idle,
        GameRunning: _gameRunning,
        Done: _done,
        DoneCount: _doneCount,
        FailedCount: _failedCount,
        NotClosed: _notClosed,
        Restore: _restore,
        NotRestarted: _notRestarted,
        ErrorMessage: _error,
        PresetName: _profiles.ActiveName,
        Customized: _profiles.IsCustomized || _profiles.AppChoices.Count > 0));

    public string HeroTitle => Phase == Phase.Reverting && _quietRevert ? Loc.T("Hazırlanıyor") : CurrentStatus().Title;
    public string HeroHint => Phase == Phase.Reverting
        ? (_quietRevert ? Loc.T("Önceki çalıştırmanın ayarları geri yükleniyor; hemen ardından yeniden başlayacak.")
           : Settings.RestartClosedApps ? Loc.T("Kapatılan programlar açılıyor, ayarlar ve hizmetler eski haline getiriliyor.")
           : Loc.T("Ayarlar ve hizmetler eski haline getiriliyor."))
        : CurrentStatus().Hint;
    // the plain "ready" headline is white fading to lavender; green / amber / red stay solid because they are status words
    /// <summary>The sentence under the headline is empty while choosing (the home screen stays plain); it only speaks about a result.</summary>
    public bool HasHeroHint => HeroHint.Length > 0;
    /// <summary>"Neler yapıldı" appears when a run is going, finished or failed; never as part of choosing a mode.</summary>
    public bool ShowLog => View.ShowLog;
    /// <summary>The restore button: there is something to restore and nothing is running.</summary>
    public bool ShowRestore => View.ShowRestore;
    /// <summary>"Kapatılan uygulamaları geri aç" when programs were closed (and Ayarlar lets Geri yükle reopen them); otherwise the button only undoes settings and says so.</summary>
    public string RestoreLabel => PendingPrograms() > 0 && Settings.RestartClosedApps ? Loc.T("Kapatılan uygulamaları geri aç") : Loc.T("Ayarları geri yükle");

    private int _pendingPrograms = -1;      // -1: look at the journal again
    private int PendingPrograms() => _pendingPrograms >= 0 ? _pendingPrograms : (_pendingPrograms = _demoSession ? 3 : _orc.SessionActive ? _orc.PendingPrograms() : 0);
    private void ForgetPendingCount() { _pendingPrograms = -1; Raise(nameof(RestoreLabel)); }
    public Brush HeroBrush => IsBusy ? UiBrushes.Get("AccentBrush") : CurrentStatus().Tone == Tone.Neutral ? UiBrushes.Get("HeadlineBrush") : UiBrushes.ForTone(CurrentStatus().Tone);
    /// <summary>The colour of the lit ticks: the accent while working, the colour of the result once it is known.</summary>
    public Brush RingBrush => IsBusy || !(IsDone || _restore != RestoreOutcome.None) ? UiBrushes.Get("AccentBrush") : UiBrushes.ForTone(CurrentStatus().Tone);
    /// <summary>The soft light behind the dial follows the ring: lavender while working or ready, green / amber / red once the result is known.</summary>
    public Brush RingHaloBrush => UiBrushes.Get(IsBusy || !(IsDone || _restore != RestoreOutcome.None) ? "HaloAccent"
        : CurrentStatus().Tone switch { Tone.Good => "HaloGood", Tone.Warn => "HaloWarn", Tone.Bad => "HaloBad", _ => "HaloAccent" });

    // ---- the ring: finished steps / planned steps. While the amount of work is not known (planning, restoring) a short arc travels instead.
    private int _stepsDone;
    private int _stepsTotal;
    private void SetSteps(int done, int total)
    {
        _stepsDone = done; _stepsTotal = total;
        Raise(nameof(RingProgress)); Raise(nameof(RingSweeping)); Raise(nameof(HeroLabel)); Raise(nameof(StepText)); Raise(nameof(HasSteps));
    }

    public double RingProgress => IsBusy ? (_stepsTotal > 0 ? Math.Min(1.0, _stepsDone / (double)_stepsTotal) : 0)
                                  : (IsDone || _restore != RestoreOutcome.None) ? 1 : 0;
    // the arc keeps travelling until the first step is really finished (the measurement and the closing batch take a few seconds): the ring never looks frozen
    public bool RingSweeping => IsBusy && (_stepsTotal == 0 || _stepsDone == 0);
    public string HeroLabel => !IsBusy ? Loc.T("Başlat") : _stepsDone > 0 && _stepsTotal > 0 ? $"{_stepsDone} / {_stepsTotal}" : Loc.T("Çalışıyor");
    public string StepText => IsBusy && _stepsDone > 0 && _stepsTotal > 0 ? Loc.T("adım") : "";
    public bool HasSteps => StepText.Length > 0;

    public string RestoreTip => !HasSession
        ? Loc.T("Henüz geri yüklenecek bir şey yok. Başlat'a bastıktan sonra buradan ayarları eski haline getirebilirsin.")
        : Settings.RestartClosedApps
            ? Loc.T("Kapatılan programları yeniden açar, değiştirilen ayarları ve durdurulan hizmetleri eski haline getirir. " +
                    "Kendini güncelleyen programlar, arka plan yardımcıları, engel aşma programları ve Mağaza uygulamaları açılamaz; hangileri açılmadıysa sonunda söylenir.")
            : Loc.T("Değiştirilen ayarları ve durdurulan hizmetleri eski haline getirir. Programları açmaz: Ayarlar'da 'Kapattığım programları geri açsın' kapalı.");

    private void RaiseStatus()
    {
        Raise(nameof(HeroTitle)); Raise(nameof(HeroHint)); Raise(nameof(HeroBrush)); Raise(nameof(RingBrush));
        Raise(nameof(RingProgress)); Raise(nameof(RingSweeping)); Raise(nameof(HeroLabel)); Raise(nameof(StepText)); Raise(nameof(HasSteps));
        Raise(nameof(HasSession)); Raise(nameof(RestoreTip)); Raise(nameof(GameRunning)); Raise(nameof(RingHaloBrush));
        Raise(nameof(HasHeroHint)); Raise(nameof(ShowLog)); Raise(nameof(ShowRestore)); Raise(nameof(ShowModeCard)); Raise(nameof(RestoreLabel));
        RefreshHomeRows();
        RescanCommand.Refresh();
        Update.Refresh();
    }

    // ---------------------------------------------------------------- the programs list of the home screen: a quick scan, one switch per program

    private IReadOnlyList<HomeApp> _scan = Array.Empty<HomeApp>();
    private DateTime _scanAt = DateTime.MinValue;
    private bool _scanStale;           // the lists of Ayarlar changed: the rows have to be made again
    private bool _scanning;

    /// <summary>One switch per program found by the scan. Plain on purpose: a name and a switch, no sentences.</summary>
    public ObservableCollection<HomeAppVM> HomeRows { get; } = new();
    public bool IsScanning { get => _scanning; private set { if (Set(ref _scanning, value)) Raise(nameof(ShowNoApps)); } }
    /// <summary>Nothing worth listing was found (said in one short line).</summary>
    public bool ShowNoApps => !IsScanning && HomeRows.Count == 0;
    private HomeView View => HomeView.Of(IsBusy, IsDone, _error != null, HasLog, HasSession);
    /// <summary>The list belongs to the choosing stage: while a run goes and after it the activity list takes its place.</summary>
    public bool ShowModeCard => View.ShowList;
    public RelayCommand RescanCommand { get; }

    /// <summary>
    /// Looks at what is running (read-only, a second or less) and lists the programs a person knows: nothing is predefined, so only what
    /// is really on this computer shows. Called when the app opens, when the window comes back to the front after a while, and after a restore.
    /// </summary>
    public async Task RescanAsync(int delayMs = 0)
    {
#if DEBUG
        if (App.OffScreenRender) return;           // a screenshot run shows its own sample list and never looks at the computer
#endif
        if (IsScanning || !IsIdle) return;
        IsScanning = true;
        try
        {
            if (delayMs > 0) await Task.Delay(delayMs);                         // programs that were just reopened need a moment to show up
            if (GameRunningNow()) return;
            var settings = HomeApps.ScanSettings(Settings, ProfileKind.Maximum);        // every switch on, nothing protected for streaming: the row decides per mode
            _scan = await Task.Run(() => HomeApps.From(new Planner(_sys, _log).ScanPrograms(settings)));
            _scanAt = DateTime.UtcNow; _scanStale = false;
            HomeRows.Clear();
            foreach (var a in _scan) HomeRows.Add(new HomeAppVM(a, this));
            RefreshHomeRows();
        }
        catch (Exception ex) { _log.Detail("Program taraması yapılamadı: " + ex); }       // the list simply stays as it was
        finally { IsScanning = false; Raise(nameof(ShowNoApps)); }
    }

    /// <summary>A scan that is a little old is repeated when the window comes back to the front (somebody opened Discord meanwhile).</summary>
    public void RescanIfStale()
    {
        if (IsIdle && !IsDone && (_scanStale || (DateTime.UtcNow - _scanAt).TotalSeconds > 20)) _ = RescanAsync();
    }

    /// <summary>Every row shows what the chosen mode says, or the user's own switch for it.</summary>
    private void RefreshHomeRows()
    {
        var choices = _profiles.AppChoices;
        foreach (var r in HomeRows)
            r.Update(HomeApps.Closes(r.App, Settings, choices), HomeApps.IsLocked(r.App, Settings), IsIdle);
        Raise(nameof(ShowNoApps));
    }

    /// <summary>The user clicked a row: only this program changes, for this profile, for this run of the app.</summary>
    internal void SwitchHomeApp(HomeApp app, bool close)
    {
        if (IsIdle && !HomeApps.IsLocked(app, Settings))
        {
            _profiles.SetAppChoice(app.Key, close, HomeApps.ModeCloses(app, Settings));
            RaiseStatus();                                                        // the headline says "(değiştirilmiş)", the rows follow
        }
        else RefreshHomeRows();
    }

    /// <summary>Puts the switches of the home list into a plan (Başlat and Önizleme both use it).</summary>
    private async Task ApplyHomeChoicesAsync(Plan plan)
    {
        var choices = _profiles.AppChoices.ToDictionary(c => c.Key, c => c.Value);
        if (choices.Count == 0) return;
        IReadOnlyList<PlannedAction>? everything = null;
        if (choices.Values.Any(on => on))        // a program the mode would leave alone is taken from a scan with every switch on
        {
            var all = HomeApps.ScanSettings(Settings, Settings.Profile);
            everything = await Task.Run(() => new Planner(_sys, _log).ScanPrograms(all));
        }
        HomeApps.Apply(plan, everything, choices);
    }

    /// <summary>CS2 was running the last time something was started (the Ayarlar page shows a line about it: its pickers do nothing then).</summary>
    public bool GameRunning => _gameRunning;

    private string _summary = "";
    public string SummaryText { get => _summary; private set => Set(ref _summary, value); }

    public bool HasInfo => InfoLines.Count > 0;

    private void Log(string msg) => _log.Info(msg);

    /// <summary>One-off snapshot check (no polling): is cs2.exe running right now? Also refreshes the headline.</summary>
    private bool GameRunningNow()
    {
        var running = _sys.FindPidByName(Catalog.Cs2ProcessName) != null;
        if (running != _gameRunning)
        {
            _gameRunning = running;
            RaiseStatus();
        }
        return running;
    }

    /// <summary>The game is running: say so in the activity list. No window: the headline already reads "CS2 çalışıyor" and nothing was started.</summary>
    private void WarnGameRunning() =>
        _log.Warn(Loc.T("CS2 şu an çalışıyor. Oyun açıkken Antilag hiçbir şey yapmaz (hile koruma programlarıyla sorun çıkmaması için). Oyunu kapattıktan sonra tekrar dene."));

    // ---------------------------------------------------------------- startup

    public Task InitAsync()
    {
        if (!string.IsNullOrEmpty(_settingsStore.LastLoadWarning))
        {
            // the activity list is hidden until something is done, so a problem with the saved settings is told here, once
            _log.Warn(_settingsStore.LastLoadWarning);
            if (!App.OffScreenRender) UiDialog.Show(_settingsStore.LastLoadWarning, "Antilag", MessageBoxButton.OK, MessageBoxImage.Warning);
        }

        if (GameRunningNow())
        {
            // the one place that still opens a window: the app is about to close itself, so the activity list would vanish with it
            _log.Warn(Loc.T("CS2 çalışıyor: bu uygulama oyun açıkken hiçbir işlem yapmaz ve kapanıyor."));
            UiDialog.Show(Loc.T("CS2 şu an çalışıyor. Oyun açıkken bu uygulama hiçbir şey yapmaz ve kapanır.\n\nOyunu kapattıktan sonra tekrar aç."),
                Loc.T("CS2 çalışıyor"), MessageBoxButton.OK, MessageBoxImage.Information);
            ExitRequested?.Invoke();
            return Task.CompletedTask;
        }

        if (!IsElevated)
            _log.Warn(Loc.T("Yönetici olarak çalışmıyorsun: bazı işlemler başarısız olabilir. Uygulamayı yönetici olarak başlat."));

        if (_orc.SessionActive)
        {
            var pending = _store.LoadPending();
            // Nothing is restored by itself and nothing is said about it: opening the app must not reopen what was closed for the game, and the home
            // screen stays plain. The restore button is the only sign that an earlier run is still in effect.
            if (pending == null && _store.NewerFormat)
            {
                // written by a newer Antilag (the user went back to an older one): the file is untouched
                _log.Warn(Loc.T("Önceki çalıştırmanın kaydı daha yeni bir Antilag sürümüyle yazılmış; bu sürüm onu geri yükleyemez."));
                UiDialog.Show(
                    Loc.T("Önceki çalıştırmanın kaydı bu Antilag sürümünden daha yeni olduğu için geri yüklenemiyor. Kayıt silinmedi.\n\n" +
                          "Antilag'ı en son sürüme güncelle, sonra ana ekrandaki geri yükleme düğmesini kullan."),
                    Loc.T("Antilag: önceki çalıştırma"), MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            else if (pending == null)
            {
                // the journal could not be read (it was set aside as state.corrupt-*.json): there is nothing the app can restore
                _log.Warn(Loc.T("Önceki çalıştırmanın kaydı okunamadı, bir kenara alındı."));
                UiDialog.Show(
                    Loc.T("Önceki çalıştırmanın kaydı okunamadığı için değişiklikler bu uygulamayla geri yüklenemiyor.\n\n" +
                          "Windows'u yeniden başlatmak durdurulan hizmetleri geri getirir. Kapattığın programları kendin açabilirsin; güç planı gibi ayarlar kalmış olabilir."),
                    Loc.T("Antilag: önceki çalıştırma"), MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            ForgetPendingCount();
            RaiseStatus(); RevertCommand.Refresh();
        }

        _ = RescanAsync();
        _ = RefreshInfoAsync();
        if (!App.OffScreenRender) _ = Update.StartAsync();           // the one check per start (skipped when Ayarlar says so)
        return Task.CompletedTask;
    }

    /// <summary>Wi-Fi and memory-speed notes: computed once by the planner's own rules, shown once, in the Ayrıntılar panel.</summary>
    public async Task RefreshInfoAsync()
    {
        var lines = await Task.Run(() => new Planner(_sys, _log).InfoLines());
        InfoLines.Clear();
        foreach (var l in lines) InfoLines.Add(l);
        Raise(nameof(HasInfo)); Raise(nameof(ShowInfo));
    }

    // ---------------------------------------------------------------- top memory (Ayrıntılar > Bellek)

    public async Task RefreshTopMemoryAsync()
    {
        if (GameRunningNow()) return;   // while the game runs this app does nothing
        var stream = Settings.Profile == ProfileKind.Stream;
        var rows = await Task.Run(() =>
        {
            return _sys.GetProcesses()
                .GroupBy(p => ProtectedList.Normalize(p.Name))
                .Select(g => new
                {
                    Name = g.Key, Count = g.Count(), Bytes = g.Sum(p => p.WorkingSetBytes),
                    Path = g.Select(p => p.Path).FirstOrDefault(x => !string.IsNullOrEmpty(x)),
                    Protected = g.Any(p => ProtectedList.IsProcessProtected(p.Name, p.Path, stream))
                             || g.Any(p => p.SessionId != _sys.CurrentSessionId) || g.Key is "cs2hazirla" or "antilag" or "clear",
                })
                .OrderByDescending(x => x.Bytes).Take(10).ToList();
        });

        TopMemory.Clear();
        foreach (var r in rows)
        {
            var name = r.Name;
            TopMemory.Add(new MemRowVM
            {
                Name = FriendlyNames.ForProcess(name, r.Path),
                CountText = r.Count > 1 ? Loc.F("{0} parça", r.Count) : Loc.T("1 parça"),
                MbText = FriendlyNames.Memory(r.Bytes),
                IsProtected = r.Protected,
                CloseCommand = new RelayCommand(() => _ = CloseTopAsync(name), () => IsIdle),
            });
        }
    }

    private async Task CloseTopAsync(string name)
    {
        if (!IsIdle) return;
        if (GameRunningNow()) { WarnGameRunning(); return; }
        // no question: pressing the "Kapat" button of that row is the decision. It is journaled, so Geri yükle can bring the program back.
        _sys.StreamMode = Settings.Profile == ProfileKind.Stream;     // the last-chance protection check inside CloseByUser uses this flag
        SetManualClosing(true);
        try { await Task.Run(() => _orc.CloseByUser(name, Settings.Profile == ProfileKind.Stream)); }
        finally { SetManualClosing(false); }
        ForgetPendingCount();
        await RefreshTopMemoryAsync();
        RaiseStatus(); RevertCommand.Refresh();
    }

    // ---------------------------------------------------------------- preview

    private string? ResolveCs2() => SteamLocator.ResolveCs2();

    private async Task PreviewAsync()
    {
        if (!IsIdle) return;
        if (GameRunningNow()) { WarnGameRunning(); return; }
        SetPreviewing(true);
        try
        {
            SaveSettings();
            _sys.StreamMode = Settings.Profile == ProfileKind.Stream;
            Log(Loc.T("Önizleme hazırlanıyor (hiçbir şey değiştirilmiyor)…"));
            Plan plan;
            try { plan = await Task.Run(() => new Planner(_sys, _log).Build(Settings, ResolveCs2())); }
            catch (Exception ex) { _log.Error(Loc.T("Önizleme hazırlanamadı."), ex); _error = "preview"; return; }

            // The plan is built against the machine as it is NOW. After an earlier run the programs are already closed and the settings
            // already applied, so the list would look nearly empty although the next Başlat first undoes and then redoes everything.
            if (_orc.SessionActive)
                plan.Warnings.Insert(0, Loc.T("Önceki çalıştırmanın değişiklikleri hâlâ geçerli (programlar kapalı, ayarlar uygulanmış). Bu liste yalnızca şu an geriye kalanları gösterir; " +
                                              "Başlat'a basınca önce eski hale döner, sonra baştan yapılır."));

            await ApplyHomeChoicesAsync(plan);
            ShowPreview?.Invoke(new PreviewViewModel(plan));       // information only: nothing in it changes the plan
            Log(Loc.T("Önizleme kapandı."));
        }
        finally { SetPreviewing(false); RaiseStatus(); RescanIfStale(); }
    }

    // ---------------------------------------------------------------- run

    private async Task RunAsync()
    {
        if (!IsIdle) return;
        if (GameRunningNow()) { WarnGameRunning(); return; }
        _failedCount = 0; _doneCount = 0; _notClosed = new(); _error = null;
        _restore = RestoreOutcome.None; _notRestarted = new();
        IsDone = false;
        // a fresh activity list for this run: the lines of the undo of the previous run and of reopened programs belong to it
        SummaryText = "";
        LogLines.Clear();
        Metrics.Clear();

        if (_orc.SessionActive)
        {
            // a new run follows: the settings and services of the previous run are undone first, but its programs are NOT reopened and not kept:
            // only the "Kapatılan uygulamaları geri aç" button brings programs back, and only those of the latest run
            _quietRevert = true;
            Phase = Phase.Reverting;
            RevertResult prev;
            try { prev = await Task.Run(() => _orc.Revert(Settings, restartApps: false, forgetPrograms: true)); }
            catch (Exception ex)
            {
                _quietRevert = false;
                Phase = Phase.Idle;
                _error = "revert";
                _log.Error(Loc.T("Önceki çalıştırma geri yüklenemedi."), ex);
                RaiseStatus();
                return;
            }
            _quietRevert = false;
            Phase = Phase.Idle;
            if (!prev.Complete)
            {
                OfferDiscard(prev);
                return;
            }
        }

        SaveSettings();
        var cs2 = ResolveCs2();

        _sys.StreamMode = Settings.Profile == ProfileKind.Stream;
        Phase = Phase.Preparing;
        _log.Info(Loc.F("Antilag başladı (seçili ayar: {0}).", _profiles.ActiveName));
        try
        {
            _log.Info(Loc.T("Neler yapılacağı belirleniyor…"));
            var plan = await Task.Run(() => new Planner(_sys, _log).Build(Settings, cs2));
            await ApplyHomeChoicesAsync(plan);
            foreach (var w in plan.Warnings) _log.Warn(w);

            var closing = plan.Active.Count(a => a.Kind == ActionKind.CloseProcess);
            var services = plan.Active.Count(a => a.Kind == ActionKind.StopService);
            var others = plan.Active.Count() - closing - services;
            _log.Info(Loc.F("Plan hazır: {0} program kapatılacak, {1} Windows hizmeti durdurulacak, {2} ayar yapılacak.", closing, services, others));
            if (services > 0) _log.Info(Loc.T("Hizmetler yalnızca durdurulur, silinmez; bilgisayarı yeniden başlatınca ya da geri yüklenince geri gelir."));
            SetSteps(0, plan.Active.Count());

            var res = await Task.Run(() => _orc.PrepareAsync(plan, Settings, CancellationToken.None));
            ShowMetrics(res);
            // (every failed step was already written to the list by the Orchestrator, once; skipped ones are harmless notes)
            // "X: artık çalışmıyor" / "X: no longer running" is only a note: the Orchestrator writes it in the language that is on, so the fragment is looked for in that language
            var notRunning = Loc.T("artık çalışmıyor");
            foreach (var s in res.Skipped.Where(x => !x.Contains(notRunning))) _log.Info(Loc.F("Atlandı: {0}", s));

            if (res.AbortedBecauseGameStarted)
            {
                // CS2 opened while the run was going: everything that was left is cancelled. No window; the headline says so.
                _log.Warn(Loc.T("Hazırlık sürerken CS2 açıldı; kalan işlemler iptal edildi. Oyunu kapattıktan sonra Antilag'ı yeniden açıp geri yükle."));
                _gameRunning = true;
                return;
            }

            _failedCount = res.Failed.Count + plan.StepFailures.Count;      // a planning step that could not be checked is a part of the job that was not done
            _doneCount = res.Done.Count;
            _notClosed = res.NotClosed.ToList();
            if (_failedCount > 0 || _notClosed.Count > 0) _log.Warn(Loc.F("Bitti. {0} işlem yapıldı, {1} işlem tam yapılamadı.", _doneCount, _failedCount + _notClosed.Count));
            else _log.Info(Loc.F("Bitti. {0} işlem yapıldı.", _doneCount));
            _log.Info(Loc.T("Oyunu başlatmadan önce Antilag'ı kapat."));
            ForgetPendingCount();
            IsDone = true;
        }
        catch (Exception ex)
        {
            _error = "run";
            _log.Error(Loc.T("İşlem sırasında bir sorun çıktı."), ex);
        }
        finally
        {
            Phase = Phase.Idle;
            RaiseStatus();
        }
    }

    private void OfferDiscard(RevertResult prev)
    {
        var r = UiDialog.Show(
            Loc.F("Önceki çalıştırma tam geri yüklenemedi:\n• {0}", string.Join("\n• ", prev.Problems)) +
            (prev.NotRestarted.Count > 0
                ? "\n\n" + Loc.F("Ayrıca şu programlar kendiliğinden açılamadı; gerekirse kendin aç:\n• {0}", string.Join("\n• ", prev.NotRestarted.Distinct()))
                : "") +
            "\n\n" + Loc.T("Bu kayıt yok sayılıp bir kenara alınsın mı?\n" +
                           "(Windows'u yeniden başlatmak durdurulan hizmetleri geri getirir; ama güç planı, kayıt defteri ve ağ kartı ayarları kendiliğinden eski haline dönmez.)\n" +
                           "Hayır dersen kayıt korunur ve daha sonra tekrar denenebilir."),
            Loc.T("Geri yükleme tamamlanamadı"), MessageBoxButton.YesNo, MessageBoxImage.Warning, defaultNo: true);
        if (r == MessageBoxResult.Yes)
        {
            _orc.DiscardJournal();
            RaiseStatus(); RevertCommand.Refresh();
        }
    }

    private void ShowMetrics(PrepareResult res)
    {
        if (res.Before == null || res.After == null) return;
        var (rows, summary) = Core.Metrics.Compare(res.Before, res.After);
        Metrics.Clear();
        foreach (var r in rows)
            Metrics.Add(new MetricRowVM
            {
                Label = r.Label, Before = r.Before, After = r.After, Delta = r.Delta,
                DeltaBrush = UiBrushes.Get(r.Verdict switch
                {
                    MetricVerdict.Better => "GoodBrush", MetricVerdict.Worse => "BadBrush", _ => "Text3Brush",
                }),
            });
        SummaryText = summary;
        _log.Info(summary);
    }

    // ---------------------------------------------------------------- restore

    private async Task UserRevertAsync()
    {
        if (!IsIdle) return;
        if (GameRunningNow()) { WarnGameRunning(); return; }
        IsDone = false;
        await DoRevertAsync(showDialog: true);
    }

    public async Task DoRevertAsync(bool showDialog)
    {
        if (Phase == Phase.Reverting) return;
        if (!_orc.SessionActive) { Phase = Phase.Idle; return; }
        _error = null; _restore = RestoreOutcome.None; _notRestarted = new();
        LogLines.Clear();                       // the list shows this restore only
        Phase = Phase.Reverting;
        RevertResult res;
        try { res = await Task.Run(() => _orc.Revert(Settings)); }
        catch (Exception ex)
        {
            _log.Error(Loc.T("Geri yükleme sırasında sorun çıktı."), ex);
            _error = "revert";
            Phase = Phase.Idle;
            RaiseStatus();
            return;
        }
        Phase = Phase.Idle;
        ForgetPendingCount();
        _ = RefreshTopMemoryAsync();

        if (!res.Complete)
        {
            if (showDialog) OfferDiscard(res);
            RaiseStatus(); RevertCommand.Refresh();
            return;
        }

        // finished: no window. The activity list names every program that was started again and the ones that could not be;
        // the headline says "Geri yüklendi" (and, when something could not be started again, which programs)
        _notRestarted = res.NotRestarted.Distinct().ToList();
        _restore = _notRestarted.Count > 0 ? RestoreOutcome.Partial : RestoreOutcome.Ok;
        RaiseStatus(); RevertCommand.Refresh();
        _ = RescanAsync(2500);                  // the programs that were reopened are back in the list (after a moment)
    }

#if DEBUG
    /// <summary>Developer screenshots: a list as a scan would find it (a screenshot run does not scan).</summary>
    public void LoadDemoApps()
    {
        HomeApp A(string name, string option, int mb, string key = "", params string[] names) =>
            new(key.Length > 0 ? key : name, name, option, names.Length > 0 ? names : new[] { name.ToLowerInvariant().Replace(" ", "") }, null, (long)mb << 20);
        _scan = new[]
        {
            A("Discord", "proc.discord", 420), A("GoodbyeDPI", "proc.dpi", 18, "", "goodbyedpi"),
            A("Tarayıcılar", "proc.browsers", 2900, HomeApps.BrowsersKey, "chrome", "msedge"),
            A("Microsoft Teams", "proc.chat", 520), A("Spotify", "proc.chat", 310), A("WhatsApp", "proc.chat", 170),
            A("Epic Games Launcher", "proc.launchers", 210), A(Loc.T("Not Defteri"), "proc.windowed", 20),
        };
        HomeRows.Clear();
        foreach (var a in _scan) HomeRows.Add(new HomeAppVM(a, this));
        RefreshHomeRows();
    }

    /// <summary>Developer screenshots: a finished run (state "done" or "warn"), a run in progress ("busy") or the start screen.</summary>
    public void LoadDemo(string state)
    {
        if (state != "idle") { _demoSession = true; ForgetPendingCount(); RevertCommand.Refresh(); }
        Metrics.Clear();
        void M(string l, string b, string a, string d, string brush) => Metrics.Add(new MetricRowVM { Label = l, Before = b, After = a, Delta = d, DeltaBrush = UiBrushes.Get(brush) });
        void L(string t, string lvl, string m) => LogLines.Add(LogEntry.From(t, lvl, m));
        if (state == "idle") return;
        L("14:02:11", "INFO", Loc.F("Antilag başladı (seçili ayar: {0}).", Loc.T("Tam temizlik")));
        L("14:02:12", "INFO", Loc.F("Plan hazır: {0} program kapatılacak, {1} Windows hizmeti durdurulacak, {2} ayar yapılacak.", 9, 4, 5));
        L("14:02:14", "INFO", Loc.F("✓ {0} kapatıldı.", "Google Chrome"));
        L("14:02:15", "INFO", Loc.F("✓ Windows hizmeti durduruldu: {0}.", Loc.T("Windows Arama")));
        if (state == "busy") { Phase = Phase.Preparing; SetSteps(4, 9); return; }
        M(Loc.T("Çalışan program sayısı"), "312", "248", "-64", "GoodBrush");
        M(Loc.T("Boş bellek (RAM, GB)"), Loc.T("9,84"), Loc.T("13,02"), Loc.T("+3,18"), "GoodBrush");
        M(Loc.T("İnternet gecikmesi (ping, ms)"), Loc.T("14,2"), Loc.T("14,0"), Loc.T("-0,2"), "Text3Brush");
        SummaryText = Loc.F("{0} değerde iyileşme var, kötüleşen yok.", 2);
        if (state == "warn")
        {
            L("14:02:16", "UYARI", Loc.T("Atlandı: Not Defteri kapanmadı (sonlandırılamadı)"));
            L("14:02:17", "HATA", Loc.T("Yapılamadı: Yazıcı hizmeti durdurulamadı (Windows izin vermedi)."));
            _notClosed = new List<string> { Loc.T("Not Defteri") }; _failedCount = 1; _doneCount = 17;
        }
        else _doneCount = 18;
        L("14:02:21", "INFO", Loc.F("Bitti. {0} işlem yapıldı.", _doneCount));
        InfoLines.Add(Loc.T("Wi-Fi kullanıyorsun. Mümkünse internet kablosu (Ethernet) kullan; kablosuz bağlantıdaki dalgalanmayı hiçbir ayar düzeltemez."));
        Raise(nameof(HasInfo)); Raise(nameof(ShowInfo));
        IsDone = true;
    }
#endif
}

// =====================================================================================================================
// preview
// =====================================================================================================================

public class PreviewGroupVM
{
    public string Title { get; init; } = "";
    public List<PlannedActionVM> Items { get; init; } = new();
}

/// <summary>What Başlat would do right now, as text. Read-only: the user changes things on the home list or in Ayrıntılar, not here.</summary>
public class PreviewViewModel
{
    public Plan Plan { get; }
    public List<PreviewGroupVM> Groups { get; }
    public IReadOnlyList<string> Warnings => Plan.Warnings;
    public bool HasWarnings => Plan.Warnings.Count > 0;
    public string Header { get; }

    public PreviewViewModel(Plan plan)
    {
        Plan = plan;
        // only what will really happen: a program switched off on the home list is not a part of the run
        var items = plan.Actions.Where(a => a.Enabled).Select(a => new PlannedActionVM(a)).ToList();
        // rows are grouped by the Turkish group text (the identity the planner gives them); the heading is shown in the chosen language
        Groups = items.GroupBy(i => i.Group).Select(g => new PreviewGroupVM { Title = Loc.T(g.Key), Items = g.ToList() }).ToList();
        Header = items.Count == 0
            ? Loc.T("Başlat'a basınca hiçbir şey yapılmayacak. Şu an hiçbir şey değiştirilmedi.")
            : Loc.F("Başlat'a basınca {0} şey yapılacak. Şu an hiçbir şey değiştirilmedi.", items.Count);
    }
}
