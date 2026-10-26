using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using CS2Hazirla.Core;

namespace CS2Hazirla;

/// <summary>One line of "Bilgisayarın durumu": a plain fact, how it stands, and (sometimes) the Windows page that belongs to it.</summary>
public sealed class HealthRowVM
{
    private readonly string? _uri;

    public HealthRowVM(HealthItem item, Func<bool> gameRunning)
    {
        Title = item.Title;
        Text = item.Text;
        IsGood = item.Level == HealthLevel.Good;
        IsNote = item.Level == HealthLevel.Note;
        IsWarn = item.Level == HealthLevel.Warn;
        // a word next to the colour, so that the state never rests on colour alone
        LevelWord = item.Level switch
        {
            HealthLevel.Good => Loc.T("sorun yok"),
            HealthLevel.Note => Loc.T("küçük not"),
            _ => Loc.T("dikkat"),
        };
        // only Windows' own settings pages may be opened from here
        if (item.ActionUri != null && item.ActionUri.StartsWith("ms-settings:", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(item.ActionText))
        {
            _uri = item.ActionUri;
            ActionText = item.ActionText!;
        }
        ActionCommand = new RelayCommand(() => { if (_uri != null && !gameRunning()) UpdateVM.ShellOpen(_uri); });
    }

    public string Title { get; }
    public string Text { get; }
    public string LevelWord { get; }
    public bool IsGood { get; }
    public bool IsNote { get; }
    public bool IsWarn { get; }
    public string ActionText { get; } = "";
    public bool HasAction => _uri != null;
    public RelayCommand ActionCommand { get; }
}

/// <summary>One address of the internet check and what came back from it.</summary>
public sealed class NetRowVM
{
    public NetRowVM(NetTargetResult r)
    {
        Label = r.Label;
        var s = r.Stats;
        Answered = s != null && s.Received > 0;
        Result = !Answered
            ? Loc.T("yanıt vermedi")
            // the numbers are written the way the sentence under the rows writes them (NetCheck.FormatMs)
            : Loc.F("ortalama {0} ms · dalgalanma {1} ms · paket kaybı %{2}", NetCheck.FormatMs(s!.AvgMs), NetCheck.FormatMs(s.JitterMs), s.LossPercent.ToString("0.#", Loc.Culture));
    }

    public string Label { get; }
    public string Result { get; }
    public bool Answered { get; }
}

/// <summary>
/// The Kontrol page: two short, read-only looks. "Bilgisayarın durumu" reads a few facts about the machine (disk space, restart, battery, driver age...);
/// "İnternet kontrolü" pings the router and two well-known addresses for a few seconds and says where a problem probably is.
/// Both run only when the page is open or the button is pressed, never while CS2 runs, and never while Antilag is doing a run (the pings would
/// disturb the before/after measurement).
/// </summary>
public sealed class CheckPageVM : ObservableObject
{
    private static readonly TimeSpan HealthFresh = TimeSpan.FromSeconds(60);

    private readonly IHealthProbe _probe;
    private readonly NetCheck _net;
    private readonly Func<bool> _gameRunning;
    private readonly Func<string?> _gameFolder;
    private readonly Func<string?> _pingTarget;
    private readonly Func<bool> _otherWork;
    private readonly Action _busyChanged;
    private CancellationTokenSource? _cts;
    private DateTime _healthAt = DateTime.MinValue;
    private bool _gameBlocked, _healthLoading, _healthRead, _netBusy, _hasNet, _netProblem, _netHealthy;
    private string _netProgress = "", _netHeadline = "", _netDetail = "", _netDns = "";

    /// <param name="otherWork">Something else of the app is running (a run, a preview, an update): the checks wait.</param>
    /// <param name="busyChanged">Called when one of the two checks starts or ends (the app locks its other buttons meanwhile).</param>
    public CheckPageVM(IHealthProbe probe, NetCheck net, Func<bool> gameRunning, Func<string?> gameFolder, Func<string?> pingTarget,
                       Func<bool> otherWork, Action busyChanged)
    {
        _probe = probe;
        _net = net;
        _gameRunning = gameRunning;
        _gameFolder = gameFolder;
        _pingTarget = pingTarget;
        _otherWork = otherWork;
        _busyChanged = busyChanged;
        RefreshHealthCommand = new RelayCommand(() => _ = RefreshHealthAsync(force: true), () => CanStart);
        RunNetCommand = new RelayCommand(() => _ = RunNetAsync(), () => CanStart);
        CancelNetCommand = new RelayCommand(() => _cts?.Cancel(), () => _netBusy);
        HealthRows.CollectionChanged += (_, _) => { Raise(nameof(HasHealth)); Raise(nameof(ShowHealthEmpty)); };
        NetRows.CollectionChanged += (_, _) => Raise(nameof(HasNetRows));
    }

    public ObservableCollection<HealthRowVM> HealthRows { get; } = new();
    public ObservableCollection<NetRowVM> NetRows { get; } = new();

    public RelayCommand RefreshHealthCommand { get; }
    public RelayCommand RunNetCommand { get; }
    public RelayCommand CancelNetCommand { get; }

    /// <summary>One of the two checks is running (the rest of the app waits for it).</summary>
    public bool IsWorking => _healthLoading || _netBusy;
    public bool CanStart => !IsWorking && !_otherWork();

    /// <summary>CS2 runs: nothing is read and nothing is sent (the page says so).</summary>
    public bool GameBlocked { get => _gameBlocked; private set { if (Set(ref _gameBlocked, value)) { Raise(nameof(ShowHealthEmpty)); Raise(nameof(ShowWaiting)); } } }

    // ---------------------------------------------------------------- Bilgisayarın durumu

    public bool HealthLoading { get => _healthLoading; private set { if (Set(ref _healthLoading, value)) { Raise(nameof(ShowHealthEmpty)); Raise(nameof(ShowWaiting)); } } }
    public bool HasHealth => HealthRows.Count > 0;
    /// <summary>The look is over and found nothing it could say (the page then tells so instead of staying blank).</summary>
    public bool ShowHealthEmpty => _healthRead && !_healthLoading && !_gameBlocked && HealthRows.Count == 0;

    /// <summary>Looks at the machine (a few seconds, on a worker thread). Without <paramref name="force"/> a look that is less than a minute old is kept.</summary>
    public async Task RefreshHealthAsync(bool force = false)
    {
        if (_healthLoading) return;
        if (_gameRunning()) { GameBlocked = true; return; }       // while the game runs this app reads nothing
        GameBlocked = false;
        if (!force && _healthRead && DateTime.UtcNow - _healthAt < HealthFresh) return;
        if (_otherWork() || _netBusy)
        {
            Raise(nameof(ShowWaiting));                             // the card says it is waiting; the main window asks again when the work ends
            return;
        }
        SetHealthBusy(true);
        try
        {
            // the game is asked once more on the worker thread BEFORE anything is read (the Steam files for the game folder included)
            var items = await Task.Run(() => _gameRunning() ? null : HealthReport.Build(_probe.Read(_gameFolder()), DateTime.Now));
            // an empty answer also means "the game is up": when it started meanwhile the answer is not trusted and the page keeps what it showed
            if (items == null || _gameRunning()) { GameBlocked = true; return; }
            ShowHealth(items);
            _healthRead = true;
            _healthAt = DateTime.UtcNow;
        }
        catch
        {
            /* the probe never throws on purpose; a surprise only leaves the card as it was */
            _healthRead = true;
        }
        finally
        {
            SetHealthBusy(false);                                   // whatever happened, the app never stays locked by a look that ended
        }
    }

    /// <summary>The card cannot look yet because a run, a preview or an update is working: it says so instead of staying blank.</summary>
    public bool ShowWaiting => !_healthRead && !_healthLoading && !_gameBlocked && _otherWork();

#if DEBUG
    /// <summary>Developer screenshots only: sample content (a screenshot run never looks at the computer or the network).</summary>
    public void Demo(IReadOnlyList<HealthItem> items, NetReport? net)
    {
        ShowHealth(items);
        _healthRead = true;
        if (net != null) Show(net);
    }
#endif

    private void SetHealthBusy(bool on)
    {
        HealthLoading = on;
        Changed();
    }

    /// <summary>One sentence above the rows: the rows only name the topics there is something to say about, so "nothing to worry about" must be said too.</summary>
    public string HealthSummary { get => _healthSummary; private set => Set(ref _healthSummary, value); }
    private string _healthSummary = "";

    private void ShowHealth(IReadOnlyList<HealthItem> items)
    {
        HealthRows.Clear();
        foreach (var item in items) HealthRows.Add(new HealthRowVM(item, _gameRunning));
        var attention = 0;
        foreach (var item in items) if (item.Level != HealthLevel.Good) attention++;
        HealthSummary = items.Count == 0 ? ""
            : attention == 0 ? Loc.T("Şu an dikkat edilecek bir şey görünmüyor.")
            : Loc.F("Dikkat etmeye değer {0} konu var.", attention);
    }

    // ---------------------------------------------------------------- İnternet kontrolü

    public bool NetBusy { get => _netBusy; private set => Set(ref _netBusy, value); }
    public string NetProgress { get => _netProgress; private set => Set(ref _netProgress, value); }
    public bool HasNetResult { get => _hasNet; private set => Set(ref _hasNet, value); }
    public bool HasNetRows => NetRows.Count > 0;
    /// <summary>The headline is coloured by the verdict: green when healthy, red when a problem was found, plain otherwise.</summary>
    public bool NetHealthy { get => _netHealthy; private set => Set(ref _netHealthy, value); }
    public bool NetProblem { get => _netProblem; private set => Set(ref _netProblem, value); }
    public string NetHeadline { get => _netHeadline; private set => Set(ref _netHeadline, value); }
    public string NetDetail { get => _netDetail; private set => Set(ref _netDetail, value); }
    public string NetDns { get => _netDns; private set { if (Set(ref _netDns, value)) Raise(nameof(HasNetDns)); } }
    public bool HasNetDns => _netDns.Length > 0;
    /// <summary>What the measurement can and cannot tell: shown under every result.</summary>
    public string NetLimits => NetCheck.Limits;

    public async Task RunNetAsync()
    {
        if (_netBusy || _healthLoading) return;
        if (_gameRunning()) { GameBlocked = true; return; }       // not a single packet while the game runs
        if (_otherWork()) return;
        GameBlocked = false;
        ClearNet();
        NetBusy = true;
        NetProgress = Loc.T("Kontrol başlıyor…");
        Changed();
        _cts = new CancellationTokenSource();
        try
        {
            var progress = new Progress<string>(text => { if (_netBusy) NetProgress = text; });       // made here, on the window's thread: reports come back here
            var report = await _net.RunAsync(_pingTarget(), progress, _cts.Token);
            Show(report);
        }
        catch (GameRunningException)
        {
            GameBlocked = true;
            NetProgress = "";
        }
        catch (OperationCanceledException)
        {
            NetProgress = Loc.T("Kontrol durduruldu.");
        }
        catch
        {
            NetProgress = Loc.T("Kontrol yapılamadı.");
        }
        finally
        {
            _cts.Dispose();
            _cts = null;
            NetBusy = false;
            if (HasNetResult) NetProgress = "";
            Changed();
        }
    }

    /// <summary>The last verdict is a statement about that moment: it is dropped when the person leaves the page (it must not be found again later as if it were current).</summary>
    public void ClearNetResult()
    {
        if (!_netBusy) ClearNet();
    }

    private void ClearNet()
    {
        NetRows.Clear();
        HasNetResult = false;
        NetHealthy = false;
        NetProblem = false;
        NetHeadline = "";
        NetDetail = "";
        NetDns = "";
        NetProgress = "";
    }

    private void Show(NetReport report)
    {
        foreach (var t in report.Targets) NetRows.Add(new NetRowVM(t));
        NetDns = report.DnsMs is { } ms
            ? Loc.F("Ad çözme (steampowered.com): {0} ms", NetCheck.FormatMs(ms))
            : "";
        NetHeadline = report.Headline;
        NetDetail = report.Detail;
        NetHealthy = report.Verdict == NetVerdict.Healthy;
        NetProblem = report.Verdict is NetVerdict.LocalNetwork or NetVerdict.Provider or NetVerdict.Unreachable or NetVerdict.Unknown;
        HasNetResult = true;
        Raise(nameof(NetLimits));
    }

    // ---------------------------------------------------------------- shared

    /// <summary>The main window asks this when its own state changed (a run began or ended): the buttons follow.</summary>
    public void OtherWorkChanged()
    {
        RefreshHealthCommand.Refresh(); RunNetCommand.Refresh();
        Raise(nameof(CanStart)); Raise(nameof(ShowWaiting));
    }

    private void Changed()
    {
        Raise(nameof(IsWorking)); Raise(nameof(CanStart));
        RefreshHealthCommand.Refresh(); RunNetCommand.Refresh(); CancelNetCommand.Refresh();
        _busyChanged();
    }
}
