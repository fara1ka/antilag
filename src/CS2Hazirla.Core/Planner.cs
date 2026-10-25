using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace CS2Hazirla.Core;

public enum ActionKind
{
    CloseProcess, StopService, PowerPlan, Registry, MouseAccel, Accessibility, NicPower, FlushDns,
}

public record RegChange(RegHive Hive, string SubKey, string Name, RegistryValueKind Kind, object Value);

/// <param name="Restart">"Geri yükle" can start this program again (see <see cref="ReopenPolicy"/>).</param>
public record ProcessPayload(string Name, string Category, string? Path, IReadOnlyList<int> Pids, bool Restart, long Bytes, string? Display = null, string? OptionId = null);
public record ServicePayload(string Name);
public record RegistryPayload(IReadOnlyList<RegChange> Changes);
public record NicPayload(NicInfo Nic);
public record PowerPayload(string? ExistingUltimate, string? ExistingHighPerf);

public class PlannedAction
{
    public string Id { get; init; } = "";
    public ActionKind Kind { get; init; }
    public string Group { get; init; } = "";
    public string Title { get; init; } = "";
    public string Detail { get; init; } = "";
    public bool Enabled { get; set; } = true;
    public object? Payload { get; init; }
}

public class Plan
{
    public List<PlannedAction> Actions { get; } = new();
    /// <summary>Things the user should know (Wi-Fi, XMP, skipped items). Shown in preview and log.</summary>
    public List<string> Warnings { get; } = new();
    /// <summary>"Already set" style notes for items that need no action.</summary>
    public List<string> Notes { get; } = new();
    /// <summary>Planning steps that could not be checked (a probe failed). Their part of the plan is missing, so a run must not end as a plain success.</summary>
    public List<string> StepFailures { get; } = new();
    public string? Cs2Path { get; set; }
    public bool StreamMode { get; set; }

    public IEnumerable<PlannedAction> Active => Actions.Where(a => a.Enabled);
}

/// <summary>
/// Builds the plan. READ-ONLY: only calls the probe half of ISystem, which is what makes
/// dry-run ("Önizleme") safe by construction.
/// </summary>
public class Planner
{
    private readonly ISystem _sys;
    private readonly ILog? _log;

    public Planner(ISystem sys, ILog? log = null) { _sys = sys; _log = log; }

    private static readonly Regex UltimateName = new("ultimate|nihai", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex HighPerfName = new("high performance|y[uü]ksek performans", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    public Plan Build(AppSettings s, string? cs2Path)
    {
        var plan = new Plan { Cs2Path = cs2Path, StreamMode = s.Profile == ProfileKind.Stream };

        // one probe that fails (WMI, powercfg...) must never stop the rest of the plan
        Guard(plan, "Programlar", () => PlanProcesses(s, plan));
        Guard(plan, "Windows hizmetleri", () => PlanServices(s, plan));
        Guard(plan, "Güç planı", () => PlanPower(s, plan));
        Guard(plan, "Windows ayarları", () => PlanRegistry(s, plan, cs2Path));
        Guard(plan, "İnternet ayarları", () => PlanNetwork(s, plan));
        Guard(plan, "Fare ve klavye ayarları", () => PlanMisc(s, plan));
        return plan;
    }

    /// <summary>
    /// The quick look at start-up: only the programs half of the plan, read-only (it does not touch services, power plan, registry or network).
    /// Pass settings with every switch on (<see cref="HomeApps.ScanSettings"/>) to see everything that could be closed right now.
    /// </summary>
    public IReadOnlyList<PlannedAction> ScanPrograms(AppSettings s)
    {
        var plan = new Plan { StreamMode = s.Profile == ProfileKind.Stream };
        Guard(plan, "Programlar", () => PlanProcesses(s, plan));
        // a look that could not be made is not "nothing found": the caller keeps what it showed before
        if (plan.StepFailures.Count > 0) throw new InvalidOperationException("Programlar yoklanamadı.");
        return plan.Actions;
    }

    private void Guard(Plan plan, string what, Action step)
    {
        try { step(); }
        catch (GameRunningException) { throw; }
        catch (Exception ex)
        {
            // `what` is one of six fixed step names (Turkish, see Build); it is translated where it is shown. The Detail line goes to the log file only and stays Turkish.
            var shown = Loc.T(what);
            plan.Warnings.Add(Loc.F("{0} kontrol edilemedi, bu adım atlandı.", shown));
            plan.StepFailures.Add(shown);
            _log?.Detail($"{what} planlanamadı: {ex}");
        }
    }

    // ---------------------------------------------------------------- processes

    private void PlanProcesses(AppSettings s, Plan plan)
    {
        var stream = plan.StreamMode;
        var never = new HashSet<string>(s.NeverClose.Select(ProtectedList.Normalize));
        var me = _sys.CurrentProcessId;
        var session = _sys.CurrentSessionId;

        var procs = _sys.GetProcesses()
            .Where(p => p.SessionId == session && p.Pid != me)
            .GroupBy(p => ProtectedList.Normalize(p.Name))
            .ToDictionary(g => g.Key, g => g.ToList());

        var planned = new HashSet<string>();

        // optionId = the option that closes it; it decides whether "Geri yükle" can open the program again
        void Add(string name, List<ProcInfo> list, string category, string optionId, string? group = null)
        {
            if (!planned.Add(name)) return;
            // the path "Geri yükle" will start again: prefer a process that shows a window. Two different programs can share one exe name
            // (Claude Desktop and the windowless Claude Code claude.exe), and the one the user sees is the one to bring back.
            var path = list.Where(p => p.HasWindow).Select(p => p.Path).FirstOrDefault(x => !string.IsNullOrEmpty(x))
                       ?? list.Select(p => p.Path).FirstOrDefault(x => !string.IsNullOrEmpty(x));
            var bytes = list.Sum(p => p.WorkingSetBytes);
            var display = FriendlyNames.ForProcess(name, path);
            var reopen = ReopenPolicy.CanReopen(name, path, optionId);
            plan.Actions.Add(new PlannedAction
            {
                Id = $"proc:{name}",
                Kind = ActionKind.CloseProcess,
                Group = group ?? Catalog.GroupApps,
                Title = Loc.F("{0}  ({1}, {2} parça)", display, FriendlyNames.Memory(bytes), list.Count),
                // `category` stays Turkish in the payload (identity); only the shown line is translated
                Detail = Loc.T(category) + (reopen ? "" : " · " + Loc.T("Geri yüklenince açılmaz, elle açman gerekir")) + (path != null ? $" · {path}" : ""),
                Payload = new ProcessPayload(name, category, path, list.Select(p => p.Pid).ToList(), reopen, bytes, display, optionId),
            });
        }

        // 1) known categories
        foreach (var cat in Catalog.ProcessCategories)
        {
            if (!s.IsOn(cat.OptionId)) continue;
            foreach (var (name, list) in procs)
            {
                if (!cat.Matches(name)) continue;
                if (never.Contains(name)) continue;
                if (ProtectedList.IsProcessProtected(name, list.Select(p => p.Path).FirstOrDefault(), stream)) continue;
                Add(name, list, cat.Title, cat.OptionId);
            }
        }

        // 2) user-defined extra processes
        if (s.IsOn("proc.extra"))
        {
            foreach (var raw in s.ExtraProcesses)
            {
                var name = ProtectedList.Normalize(raw);
                if (name.Length == 0) continue;
                if (!procs.TryGetValue(name, out var list)) continue;
                if (never.Contains(name)) continue;
                if (ProtectedList.IsProcessProtected(name, list.Select(p => p.Path).FirstOrDefault(), stream))
                {
                    plan.Warnings.Add(Loc.F("'{0}' Windows, Steam, FACEIT ya da ekran/ses sürücüsüne ait olduğundan kapatılmayacak.", FriendlyNames.ForProcess(name, null)));
                    continue;
                }
                Add(name, list, "Senin eklediğin program", "proc.extra");
            }
        }

        // 3) generic sweep (proc.trayauto): windowless user processes that belong to no known category
        if (s.IsOn("proc.trayauto"))
        {
            foreach (var (name, list) in procs)
            {
                if (planned.Contains(name) || never.Contains(name)) continue;
                var known = Catalog.CategoryOf(name);
                if (known != null) continue;                         // handled (or deliberately left alone) above
                if (list.Any(p => p.HasWindow)) continue;            // app has a window → may hold unsaved work, or is a helper of one
                var path = list.Select(p => p.Path).FirstOrDefault(x => !string.IsNullOrEmpty(x));
                if (path == null) continue;                          // unknown origin → leave alone
                if (path.Contains(@"\WindowsApps\", StringComparison.OrdinalIgnoreCase)) continue;
                if (ProtectedList.IsProcessProtected(name, path, stream)) continue;
                Add(name, list, "Tanınmayan arka plan programı", "proc.trayauto");
            }
        }

        // 4) every other open (windowed) user application: asked to close, and ended by force when it has not exited after the
        //    grace period (a program that "closes" to the tray never exits on its own). Unsaved work is lost: the switch says so.
        if (s.IsOn("proc.windowed"))
        {
            foreach (var (name, list) in procs)
            {
                if (planned.Contains(name) || never.Contains(name)) continue;
                if (Catalog.CategoryOf(name) != null) continue;      // has its own category toggle
                if (!list.Any(p => p.HasWindow)) continue;
                var path = list.Select(p => p.Path).FirstOrDefault(x => !string.IsNullOrEmpty(x));
                if (path == null) continue;
                if (ProtectedList.IsProcessProtected(name, path, stream)) continue;
                Add(name, list, "Açık pencereli uygulama", "proc.windowed",
                    group: "Açık duran programlar (kaydedilmemiş iş kaybolur)");
            }
        }
    }

    // ---------------------------------------------------------------- services

    private void PlanServices(AppSettings s, Plan plan)
    {
        foreach (var rule in s.Services)
        {
            if (!rule.Enabled) continue;
            var optionId = rule.Group == "update" ? "svc.update" : "svc.other";
            if (!s.IsOn(optionId)) continue;

            if (ProtectedList.IsServiceProtected(rule.Name))
            {
                plan.Warnings.Add(Loc.F("'{0}' Windows için gerekli olduğundan durdurulmayacak.", Catalog.ServiceTitle(rule.Name)));
                continue;
            }

            var info = _sys.GetService(rule.Name);
            if (info == null) continue;
            if (info.State != ServiceRunState.Running) continue;
            if (!info.CanStop)
            {
                plan.Notes.Add(Loc.F("'{0}' durdurulamıyor (Windows izin vermiyor), atlanacak.", Catalog.ServiceTitle(rule.Name, info.DisplayName)));
                continue;
            }

            var deps = info.RunningDependents.Count > 0 ? " · " + Loc.F("bağımlı: {0}", string.Join(", ", info.RunningDependents)) : "";
            plan.Actions.Add(new PlannedAction
            {
                Id = $"svc:{rule.Name.ToLowerInvariant()}",
                Kind = ActionKind.StopService,
                Group = "Geçici durdurulacak Windows hizmetleri",       // identity (rows are grouped by it); the heading is translated where it is shown
                Title = Catalog.ServiceTitle(rule.Name, info.DisplayName),       // (ServiceTitle / ServicePlain pick the language themselves)
                Detail = (Catalog.ServicePlain(rule.Name) is { Length: > 0 } plain ? plain + " " : "") +
                         Loc.T("Yalnızca durdurulur, silinmez; bilgisayarı yeniden başlatınca ya da geri yüklenince geri gelir.") + deps,
                Payload = new ServicePayload(rule.Name),
            });
        }
    }

    // ---------------------------------------------------------------- power plan

    private void PlanPower(AppSettings s, Plan plan)
    {
        if (!s.IsOn("pwr.plan")) return;
        var power = _sys.GetPower();
        var active = power.Schemes.FirstOrDefault(x => string.Equals(x.Guid, power.ActiveGuid, StringComparison.OrdinalIgnoreCase));
        bool activeIsPerf = string.Equals(power.ActiveGuid, Catalog.UltimateTemplateGuid, StringComparison.OrdinalIgnoreCase)
            || string.Equals(power.ActiveGuid, Catalog.HighPerfGuid, StringComparison.OrdinalIgnoreCase)
            || (active != null && (UltimateName.IsMatch(active.Name) || HighPerfName.IsMatch(active.Name)));
        if (activeIsPerf)
        {
            plan.Notes.Add(Loc.F("Güç planı zaten yüksek performans ({0}).", active?.Name));
            return;
        }

        var ultimate = power.Schemes.FirstOrDefault(x =>
            string.Equals(x.Guid, Catalog.UltimateTemplateGuid, StringComparison.OrdinalIgnoreCase) || UltimateName.IsMatch(x.Name))?.Guid;
        var high = power.Schemes.FirstOrDefault(x =>
            string.Equals(x.Guid, Catalog.HighPerfGuid, StringComparison.OrdinalIgnoreCase) || HighPerfName.IsMatch(x.Name))?.Guid;

        // two whole sentences (the English builds the middle part its own way), not a sentence with a text pasted into it
        var current = active?.Name ?? power.ActiveGuid;
        plan.Actions.Add(new PlannedAction
        {
            Id = "pwr",
            Kind = ActionKind.PowerPlan,
            Group = Catalog.GroupSystem,
            Title = Loc.T("Bilgisayar en yüksek performans modunda çalışacak"),
            Detail = ultimate != null
                ? Loc.F("Şu an '{0}' modu açık; mevcut Nihai Performans planı. Geri yüklenince eski mod geri gelir.", current)
                : Loc.F("Şu an '{0}' modu açık; Nihai Performans planı oluşturulacak (olmazsa Yüksek Performans). Geri yüklenince eski mod geri gelir.", current),
            Payload = new PowerPayload(ultimate, high),
        });
    }

    // ---------------------------------------------------------------- registry-backed tweaks

    private const string GameBarKey = @"Software\Microsoft\GameBar";
    private const string GameConfigKey = @"System\GameConfigStore";
    private const string GameDvrKey = @"Software\Microsoft\Windows\CurrentVersion\GameDVR";
    private const string PushKey = @"Software\Microsoft\Windows\CurrentVersion\PushNotifications";
    public const string LayersKey = @"Software\Microsoft\Windows NT\CurrentVersion\AppCompatFlags\Layers";
    public const string GpuPrefKey = @"Software\Microsoft\DirectX\UserGpuPreferences";
    public const string FsoFlag = "DISABLEDXMAXIMIZEDWINDOWEDMODE";

    private void PlanRegistry(AppSettings s, Plan plan, string? cs2Path)
    {
        // title and detail arrive already in the language of the moment (the callers wrap their texts in Loc.T)
        void AddReg(string optionId, string id, string title, string detail, List<RegChange> wanted)
        {
            if (!s.IsOn(optionId)) return;
            var pending = wanted.Where(c => !AlreadySet(c)).ToList();
            if (pending.Count == 0) { plan.Notes.Add(Loc.F("{0}: zaten ayarlı.", title)); return; }
            plan.Actions.Add(new PlannedAction
            {
                Id = id, Kind = ActionKind.Registry, Group = Catalog.GroupSystem, Title = title,
                Detail = detail, Payload = new RegistryPayload(pending),
            });
        }

        AddReg("sys.gamemode", "reg:gamemode", Loc.T("Windows Oyun Modu açık"),
            Loc.T("Windows oyun sırasında arka plan işlerini kısar. Geri yüklenince eski ayar geri gelir."),
            new()
            {
                new(RegHive.CurrentUser, GameBarKey, "AutoGameModeEnabled", RegistryValueKind.DWord, 1),
                new(RegHive.CurrentUser, GameBarKey, "AllowAutoGameMode", RegistryValueKind.DWord, 1),
            });

        AddReg("sys.gamedvr", "reg:gamedvr", Loc.T("Xbox Game Bar oyun kaydı kapalı"),
            Loc.T("Windows'un oyunu arka planda kaydetmeye hazır beklemesi durur. OBS gibi programlar etkilenmez."),
            new()
            {
                new(RegHive.CurrentUser, GameConfigKey, "GameDVR_Enabled", RegistryValueKind.DWord, 0),
                new(RegHive.CurrentUser, GameDvrKey, "AppCaptureEnabled", RegistryValueKind.DWord, 0),
            });

        AddReg("sys.notifications", "reg:toast", Loc.T("Windows bildirimleri kapalı"),
            Loc.T("Sağ alttan çıkan bildirim kutuları Geri yüklenene kadar kapalı kalır."),
            new() { new(RegHive.CurrentUser, PushKey, "ToastEnabled", RegistryValueKind.DWord, 0) });

        if (s.IsOn("sys.gpupref") || s.IsOn("sys.fso"))
        {
            if (cs2Path == null)
            {
                plan.Warnings.Add(Loc.T("CS2'nin kurulu olduğu yer bulunamadı: CS2 için ekran kartı ayarları atlandı."));
            }
            else
            {
                // registry values keyed by the exe path only: neither the game process nor its files are touched
                var gpuOld = _sys.ReadRegistry(RegHive.CurrentUser, GpuPrefKey, cs2Path)?.Data;
                AddReg("sys.gpupref", "reg:gpupref", Loc.T("CS2 için güçlü ekran kartı seçili"),
                    Loc.T("Windows Ayarlar > Ekran > Grafik bölümündeki seçeneğin aynısı; yalnızca CS2 için. Oyun dosyalarına ve oyunun kendisine dokunulmaz."),
                    new() { new(RegHive.CurrentUser, GpuPrefKey, cs2Path, RegistryValueKind.String, MergeGpuPreference(gpuOld)) });

                var layersOld = _sys.ReadRegistry(RegHive.CurrentUser, LayersKey, cs2Path)?.Data;
                AddReg("sys.fso", "reg:fso", Loc.T("CS2 için tam ekran iyileştirmesi kapalı"),
                    Loc.T("Yalnızca CS2 için: Windows'un tam ekran oyunlara eklediği uyumluluk katmanını bu oyun için kapatır. Tam temizlik ve Yayıncı'da açıktır; Ayrıntılar'dan kapatabilirsin."),
                    new() { new(RegHive.CurrentUser, LayersKey, cs2Path, RegistryValueKind.String, MergeLayerFlags(layersOld, FsoFlag)) });
            }
        }
    }

    private bool AlreadySet(RegChange c)
    {
        var cur = _sys.ReadRegistry(c.Hive, c.SubKey, c.Name);
        return cur != null && cur.Kind == c.Kind && cur.Data == RegCodec.Encode(c.Kind, c.Value);
    }

    /// <summary>Adds a compatibility flag keeping every existing flag (e.g. "~ RUNASADMIN").</summary>
    public static string MergeLayerFlags(string? existing, string flag)
    {
        var tokens = (existing ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries).ToList();
        tokens.RemoveAll(t => t == "~");
        if (!tokens.Contains(flag, StringComparer.OrdinalIgnoreCase)) tokens.Add(flag);
        return "~ " + string.Join(' ', tokens);
    }

    /// <summary>Sets GpuPreference=2 (high performance), keeping any other keys such as SwapEffectUpgradeEnable.</summary>
    public static string MergeGpuPreference(string? existing)
    {
        var parts = (existing ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries)
            .Where(p => !p.StartsWith("GpuPreference=", StringComparison.OrdinalIgnoreCase)).ToList();
        parts.Insert(0, "GpuPreference=2");
        return string.Join(';', parts) + ";";
    }

    // ---------------------------------------------------------------- network

    private void PlanNetwork(AppSettings s, Plan plan)
    {
        if (s.IsOn("net.flushdns"))
            plan.Actions.Add(new PlannedAction
            {
                Id = "dns", Kind = ActionKind.FlushDns, Group = "İnternet", Title = Loc.T("İnternet adres önbelleğini temizle (DNS)"),
                Detail = Loc.T("Eski internet adresi kayıtlarını siler; bir sonraki bağlantıda yenilenir. Ping'i düşürmez."),
            });

        if (s.IsOn("net.nicpower"))
        {
            foreach (var nic in _sys.GetNics())
            {
                if (nic.PowerInstanceName == null || nic.PowerSaveEnabled != true) continue;
                plan.Actions.Add(new PlannedAction
                {
                    Id = $"nic:{nic.Guid}", Kind = ActionKind.NicPower, Group = "İnternet",
                    Title = Loc.F("Güç tasarrufunu kapat: {0}", nic.Name),
                    Detail = Loc.T("'Güç tasarrufu için bu aygıtı kapatabilir' işareti kaldırılır, geri yüklenince geri gelir"),
                    Payload = new NicPayload(nic),
                });
            }
        }
    }

    // ---------------------------------------------------------------- misc

    private void PlanMisc(AppSettings s, Plan plan)
    {
        if (s.IsOn("sys.mouseaccel"))
        {
            var m = _sys.GetMouse();
            if (m.AccelerationOn)
                plan.Actions.Add(new PlannedAction
                {
                    Id = "mouse", Kind = ActionKind.MouseAccel, Group = Catalog.GroupSystem,
                    Title = Loc.T("Fare ivmesini kapat (bu oturum için)"),
                    Detail = Loc.T("Kalıcı kaydedilmez; Windows'u yeniden başlatınca ya da geri yüklenince eski değer geri gelir"),
                    Payload = m,
                });
            else plan.Notes.Add(Loc.T("Fare ivmesi zaten kapalı."));
        }

        if (s.IsOn("sys.hotkeys"))
        {
            var acc = _sys.GetAccessibility();
            if (acc.AnyHotkeyActive)
                plan.Actions.Add(new PlannedAction
                {
                    Id = "hotkeys", Kind = ActionKind.Accessibility, Group = Catalog.GroupSystem,
                    Title = Loc.T("Yapışkan/Filtre/Geçiş tuşları kısayollarını kapat (bu oturum için)"),
                    Detail = Loc.T("Shift x5 gibi kısayollar oyunu simge durumuna küçültebilir; kayıtlı ayarlar yeniden yazılmaz"),
                    Payload = acc,
                });
            else plan.Notes.Add(Loc.T("Yapışkan/Filtre/Geçiş tuşları kısayolları zaten kapalı."));
        }
    }

    // ---------------------------------------------------------------- info (read-only advice)

    /// <summary>The read-only notes, and whether one of them is about the screen (the panel then offers a button that opens Windows' display settings).</summary>
    public sealed record InfoReport(IReadOnlyList<string> Lines, bool ScreenCanBeFaster);

    /// <summary>Read-only notes (Wi-Fi, memory speed). Shown once in the Ayrıntılar panel; they are not part of the plan and never change anything.</summary>
    public IReadOnlyList<string> InfoLines() => Info().Lines;

    public InfoReport Info()
    {
        var lines = new List<string>();
        var screenSlow = false;
        try
        {
            // the screen first: a monitor that runs far below its speed is the most common reason a game feels sluggish
            var screen = _sys.GetPrimaryDisplay();
            var note = DisplayAdvice.Check(screen);
            if (note != null) { lines.Add(note); screenSlow = true; }
        }
        catch { /* advisory only */ }

        try
        {
            if (_sys.GetNics().Any(n => n.IsWireless))
                lines.Add(Loc.T("Wi-Fi kullanıyorsun. Mümkünse internet kablosu (Ethernet) kullan; kablosuz bağlantıdaki dalgalanma ve paket kaybını hiçbir ayar düzeltemez."));
        }
        catch { /* advisory only */ }

        try
        {
            var rpt = RamAnalysis.Check(_sys.GetRamModules());
            if (rpt != null) lines.Add(rpt);
        }
        catch { /* advisory only */ }
        return new InfoReport(lines, screenSlow);
    }
}

public static class RamAnalysis
{
    /// <summary>Returns a warning when the running RAM speed is clearly below the module's rated speed.</summary>
    public static string? Check(IReadOnlyList<RamModule> modules)
    {
        foreach (var m in modules)
        {
            if (m.SpeedMhz is not > 0 || m.ConfiguredMhz is not > 0) continue;
            double rated = m.SpeedMhz.Value;
            double running = m.ConfiguredMhz.Value;
            if (running < rated * 0.6) running *= 2;   // some firmware reports the clock, not the transfer rate
            if (rated - running >= 100)
                return Loc.F("Belleğin (RAM) olabileceğinden yavaş çalışıyor olabilir ({0:F0} yerine {1:F0} MT/s hız için üretilmiş). " +
                             "Bilgisayarın BIOS ayarlarında 'XMP' ya da 'EXPO' diye bir seçenek varsa açmak oyunda fark edilebilir; emin değilsen bir uzmana sor. " +
                             "(Uygulama BIOS'a dokunmaz, bu yalnızca bir bilgi.)", running, rated);
        }
        return null;
    }
}
