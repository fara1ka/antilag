using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;
using CS2Hazirla.Core;

namespace CS2Hazirla;

/// <summary>
/// The small card in the left bar ("Yeni sürüm var · Güncelle · Sonra") and the update row in Ayarlar. All the logic is in <see cref="UpdateFlow"/>;
/// this class only turns its state into words and buttons. Nothing runs by itself: one check when the app opens (if the switch in Ayarlar is on),
/// and the download starts only when the user presses Güncelle.
/// </summary>
public class UpdateVM : ObservableObject
{
    private readonly UpdateFlow _flow;
    private readonly Dispatcher _ui;
    private readonly AppSettings _settings;
    private readonly Func<bool> _isIdle;
    private readonly Action _exit;
    private readonly Action _saveSettings;
    private readonly Action _busyChanged;
    private readonly Action _flushEdits;
    private bool _checked;
    private bool _checking;
    private bool _wasWorking;
    private UpdateCheck _lastCheck = UpdateCheck.Done;

    public UpdateVM(UpdateFlow flow, Dispatcher ui, AppSettings settings, Func<bool> isIdle, Action exit, Action saveSettings, Action busyChanged, Action flushEdits)
    {
        _flow = flow; _ui = ui; _settings = settings; _isIdle = isIdle; _exit = exit; _saveSettings = saveSettings; _busyChanged = busyChanged; _flushEdits = flushEdits;
        InstallCommand = new RelayCommand(() => _ = InstallAsync(), () => CanInstall);
        DismissCommand = new RelayCommand(() => _flow.Dismiss());
        CancelCommand = new RelayCommand(() => _flow.Cancel());
        CheckNowCommand = new RelayCommand(() => _ = CheckAsync(), () => !_checking && !_flow.IsWorking);
        OpenSourceCommand = new RelayCommand(() => ShellOpen(SourceUrl));
        OpenLicensesCommand = new RelayCommand(() =>
        {
            var dir = Path.Combine(Path.GetDirectoryName(Environment.ProcessPath) ?? "", "licenses");
            ShellOpen(Directory.Exists(dir) ? dir : SourceUrl);
        });
        _flow.Changed += () => _ui.BeginInvoke(new Action(OnFlowChanged));
    }

    /// <summary>A download or an installer start is going on: Başlat, Önizleme, the profile switches and "Uygulamayı kapat" wait (the app closes itself at the end).</summary>
    public bool IsWorking => _flow.IsWorking;

    private void OnFlowChanged()
    {
        RaiseAll();
        if (_wasWorking != _flow.IsWorking) { _wasWorking = _flow.IsWorking; _busyChanged(); }
    }

    public static Version Current => typeof(UpdateVM).Assembly.GetName().Version ?? new Version(0, 0, 0);

    public RelayCommand InstallCommand { get; }
    public RelayCommand DismissCommand { get; }
    public RelayCommand CancelCommand { get; }
    public RelayCommand CheckNowCommand { get; }
    public RelayCommand OpenSourceCommand { get; }
    public RelayCommand OpenLicensesCommand { get; }

    private const string SourceUrl = "https://github.com/fara1ka/antilag";

    /// <summary>
    /// The legal notice the GPL asks every interactive program to show: who owns it, under which licence, that there is no warranty, where the source is.
    /// </summary>
    public string AboutText =>
        $"Antilag {Current.ToString(3)}. Telif hakkı (C) 2026 fara1ka. Bu program özgür yazılımdır: GNU Genel Kamu Lisansı'nın 3. sürümü ya da (isteğe bağlı) sonraki bir sürümünün koşulları altında " +
        "dağıtılır ve HİÇBİR GARANTİ VERİLMEZ. Valve, FACEIT, Discord ve adı geçen diğer şirketlerle bağlantılı değildir. Kaynak kod ve lisans metinleri aşağıdadır.";

    /// <summary>Through the Windows shell, so that a folder or a page opens in the user's normal (not administrator) Explorer or browser.</summary>
    private static void ShellOpen(string target)
    {
        try
        {
            var explorer = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe");
            Process.Start(new ProcessStartInfo(explorer, $"\"{target}\"") { UseShellExecute = false, CreateNoWindow = true });
        }
        catch { /* nothing to open it with: not worth a message */ }
    }

    // ------------------------------------------------------------------ the card

    public bool Show => _flow.State != UpdateState.None;
    public bool ShowButtons => _flow.State is UpdateState.Available or UpdateState.Failed;
    public bool ShowCancel => _flow.State == UpdateState.Downloading;
    public bool ShowProgress => _flow.State == UpdateState.Downloading;
    public double Progress => Math.Clamp(_flow.Progress, 0, 1);

    public string Title => _flow.State switch
    {
        UpdateState.Available => "Yeni sürüm var",
        UpdateState.Downloading => $"İndiriliyor… %{(int)Math.Round(Progress * 100)}",
        UpdateState.Installing => "Kuruluyor…",
        UpdateState.Failed => "Güncelleme yapılamadı",
        _ => "",
    };

    public string Detail => _flow.State switch
    {
        UpdateState.Available => $"Antilag {_flow.Offer?.VersionText}",
        UpdateState.Downloading => "Bitince Antilag kapanıp yeniden açılır.",
        UpdateState.Installing => "Antilag kapanıp yeniden açılacak.",
        UpdateState.Failed => _flow.FailureText,
        _ => "",
    };

    public string InstallText => _flow.State == UpdateState.Failed ? "Tekrar dene" : "Güncelle";
    public string DismissText => _flow.State == UpdateState.Failed ? "Kapat" : "Sonra";

    /// <summary>Not while a run or a restore is going, and not while a check is on its way: the app closes itself at the end of an update.</summary>
    public bool CanInstall => _isIdle() && !_checking && _flow.State is UpdateState.Available or UpdateState.Failed;

    /// <summary>What the window may say when the user tries to close it during an update.</summary>
    public string BusyText => _flow.State == UpdateState.Downloading
        ? "Güncelleme indiriliyor. Bitmesini bekle ya da kartın üstündeki Vazgeç'e bas; sonra Uygulamayı kapat'a bas."
        : "Güncelleme başlıyor, birkaç saniye bekle.";

    // ------------------------------------------------------------------ Ayarlar

    public string VersionText => $"Sürüm {Current.ToString(3)}";
    /// <summary>"Sürüm 3.4.0 · Antilag güncel."</summary>
    public string VersionLine => StatusText.Length == 0 ? VersionText : VersionText + " · " + StatusText;

#if DEBUG
    public void Demo(UpdateState state, double progress = 0, string failure = "") { _flow.Demo(state, progress, failure); _checked = true; RaiseAll(); }
#endif

    public bool CheckForUpdates
    {
        get => _settings.CheckForUpdates;
        set
        {
            if (_settings.CheckForUpdates == value) return;
            _settings.CheckForUpdates = value;
            _saveSettings();
            RaiseAll();
            if (value) _ = CheckAsync();
        }
    }

    /// <summary>What is true now, from what is known (not from which state the card happens to be in).</summary>
    public string StatusText =>
        _flow.IsWorking ? "Güncelleniyor…"
        : _flow.Offer != null && _flow.LastCheckWorked ? $"Yeni sürüm var: {_flow.Offer.VersionText}"
        : _checking ? "Denetleniyor…"
        : _lastCheck == UpdateCheck.SkippedGame ? "CS2 çalışırken denetlenmez."
        : !_checked ? (_settings.CheckForUpdates ? "" : "Denetim kapalı.")
        : _flow.LastCheckRejected ? "Sürüm bilgisi doğrulanamadı; güncelleme sunulmadı."
        : _flow.LastCheckWorked ? "Antilag güncel."
        : "Denetlenemedi. İnternet bağlantını kontrol et.";

    // ------------------------------------------------------------------ doing

    /// <summary>The one automatic check, right after the app opened. Skipped when the switch is off.</summary>
    public async Task StartAsync()
    {
        _flow.CleanDownloads();
        if (_settings.CheckForUpdates) await CheckAsync();
    }

    public async Task CheckAsync()
    {
        if (_checking || _flow.IsWorking) return;
        _checking = true; RaiseAll();
        try
        {
            _lastCheck = await _flow.CheckAsync();
            if (_lastCheck is UpdateCheck.Done or UpdateCheck.Failed) _checked = true;
        }
        finally { _checking = false; RaiseAll(); }
    }

    private async Task InstallAsync()
    {
        if (!CanInstall) return;
        _flushEdits();                       // an update closes the app: what was typed on the Profiller page is saved first, never dropped
        if (await _flow.InstallAsync()) _exit();
    }

    public void Refresh() { InstallCommand.Refresh(); CheckNowCommand.Refresh(); }

    private void RaiseAll()
    {
        foreach (var name in new[]
        {
            nameof(Show), nameof(ShowButtons), nameof(ShowCancel), nameof(ShowProgress), nameof(Progress), nameof(Title), nameof(Detail),
            nameof(InstallText), nameof(DismissText), nameof(CanInstall), nameof(StatusText), nameof(VersionLine), nameof(CheckForUpdates), nameof(BusyText),
        }) Raise(name);
        Refresh();
    }
}
