using System.Globalization;
using System.Management;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace CS2Hazirla.Core;

// The computer check-up ("Bilgisayar kontrolü"): plain, read-only facts about the PC that matter before a game.
// Nothing here changes anything. A fact that cannot be read is null / left out, never guessed.
//   WindowsHealthProbe  reads the facts (registry, WMI, DriveInfo of two drives, one kernel32 call) -> HealthFacts
//   HealthReport.Build  turns the facts into the list the screen shows (pure, no machine)           -> HealthItem[]

/// <summary>How the screen marks an item: nothing to do, worth a note, needs attention.</summary>
public enum HealthLevel { Good, Note, Warn }

/// <summary>
/// One line of the check-up. <paramref name="Id"/> is an identity (never translated); Title and Text are already in the language of the app.
/// <paramref name="ActionUri"/> is null or one of the "ms-settings:" pages of <see cref="HealthReport.IsAllowedActionUri"/>; ActionText is the button text.
/// </summary>
public sealed record HealthItem(string Id, HealthLevel Level, string Title, string Text, string? ActionUri = null, string? ActionText = null);

/// <summary>A fixed drive. Drive is "C:"; MediaType is "SSD", "HDD" or null when it is not known.</summary>
public sealed record DiskFact(string Drive, long TotalBytes, long FreeBytes, string? MediaType, bool IsSystemDrive, bool HoldsGame);

/// <summary>
/// What the probe found. Every fact that could not be read is null (or an empty list).
/// <c>Disks</c> holds at most two drives: the one with Windows and the one with the game (a single entry when they are the same drive).
/// No other drive is looked at, so a sleeping external disk stays asleep.
/// </summary>
public sealed record HealthFacts(IReadOnlyList<DiskFact> Disks, double? UptimeDays, bool? RebootPending, bool? OnBattery, string? GpuName, DateTime? GpuDriverDate, bool? DiskFailurePredicted);

public interface IHealthProbe
{
    /// <summary>
    /// Reads the facts. <paramref name="gameFolder"/> is where CS2 / the Steam library lives (may be null). Never throws. A part that has not answered
    /// about 6.5 seconds after the start is left out (its facts are null / empty), so a call takes about 7 seconds at the very most.
    /// The caller runs it on a worker thread.
    /// </summary>
    HealthFacts Read(string? gameFolder);
}

/// <summary>Facts -> the list of items. Pure: the same facts and the same <c>now</c> always give the same list.</summary>
public static class HealthReport
{
    private const long GiB = 1L << 30;

    // The lines for free space. The absolute amount decides; the percentage of the drive only matters while the amount is also smallish
    // (70 GB free of a 740 GB drive is 9.5 %, yet it is plenty for updates, so it must not be a warning). Exactly on a line is not below it.
    //   Warn : free < 15 GB                      OR  (free < 10 % of the drive AND free < 50 GB)
    //   Note : (not Warn) AND (free < 30 GB      OR  (free < 20 % of the drive AND free < 100 GB))
    //   Good : otherwise
    private const long WarnFreeBytes = 15 * GiB;             // below this: Warn, whatever the size of the drive
    private const long WarnPercentCapBytes = 50 * GiB;       // below 10 % is a Warn only while the free space is also below this
    private const long NoteFreeBytes = 30 * GiB;             // below this: (at least) a Note, whatever the size of the drive
    private const long NotePercentCapBytes = 100 * GiB;      // below 20 % is a Note only while the free space is also below this
    private const int WarnPercent = 10, NotePercent = 20;

    // the other lines: uptime, driver age
    private const double NoteUptimeDays = 7, WarnUptimeDays = 21;
    private const int MaxShownDays = 9999;                   // a day count is shown up to this (27 years: out of reach for a real PC) so that a title never grows past its room
    private const int OldDriverMonths = 12;

    /// <summary>Settings > System > Storage.</summary>
    public const string StorageSettingsUri = "ms-settings:storagesense";
    /// <summary>Settings > System > Power (and battery).</summary>
    public const string PowerSettingsUri = "ms-settings:powersleep";

    // The only pages an item may open (both are in Microsoft's list of ms-settings: pages). Nothing else ever becomes a button.
    private static readonly HashSet<string> AllowedActions = new(StringComparer.OrdinalIgnoreCase) { StorageSettingsUri, PowerSettingsUri };

    /// <summary>True for the settings pages a check-up button may open. The screen can ask this once more right before it opens one.</summary>
    public static bool IsAllowedActionUri(string? uri) => uri != null && AllowedActions.Contains(uri);

    public static IReadOnlyList<HealthItem> Build(HealthFacts facts, DateTime now)
    {
        var items = new List<HealthItem>();
        if (facts is null) return items;

        var disks = (facts.Disks ?? Array.Empty<DiskFact>()).Where(d => d is not null).ToList();

        // (1) free space: the system drive, and the drive of the game when that is another one
        var system = disks.FirstOrDefault(d => d.IsSystemDrive && d.TotalBytes > 0);
        var gameElsewhere = disks.FirstOrDefault(d => d.HoldsGame && !d.IsSystemDrive && d.TotalBytes > 0);
        if (system != null) items.Add(Space("space.system", system, system.HoldsGame ? SpaceKind.SystemAndGame : SpaceKind.SystemOnly));
        if (gameElsewhere != null) items.Add(Space("space.game", gameElsewhere, SpaceKind.GameOnly));

        // (2) the game on a hard disk (HDD)
        var gameDisk = disks.FirstOrDefault(d => d.HoldsGame);
        if (gameDisk != null && string.Equals(gameDisk.MediaType, "HDD", StringComparison.OrdinalIgnoreCase))
            items.Add(Make("game.hdd", HealthLevel.Note,
                Loc.F("Oyun {0} diskinde (HDD)", gameDisk.Drive),
                Loc.T("Oyun, HDD'de SSD'ye göre biraz daha geç yüklenir.")));

        // (3) disk health: Windows itself predicts a failure
        if (facts.DiskFailurePredicted == true)
            items.Add(Make("disk.health", HealthLevel.Warn,
                Loc.T("Disk için arıza uyarısı"),
                Loc.T("Windows, disklerden birinin bozulabileceğini düşünüyor. Bozuldu demek değil; ama önemli dosyalarını yedekle.")));

        // (4) uptime: Note from 7 days, Warn from 21 days. The tick counter is the time since Windows last really started: with Fast Startup (the Windows default)
        // "Shut down" does not reset it, only a restart does. So the title says "not restarted", never "on", and only "Restart" is advised.
        if (facts.UptimeDays is double up && double.IsFinite(up) && up >= NoteUptimeDays)
        {
            var days = (int)Math.Min(Math.Floor(up), MaxShownDays);
            var warn = up >= WarnUptimeDays;
            items.Add(Make("uptime", warn ? HealthLevel.Warn : HealthLevel.Note,
                Loc.F("Windows {0} gündür yeniden başlatılmadı", days),
                warn
                    ? Loc.T("Bu epey uzun bir süre. Başlat menüsünden Yeniden başlat'ı seç: bellek temizlenir, bekleyen güncellemeler biter.")
                    : Loc.T("Yeniden başlatmak belleği temizler ve bekleyen güncellemeleri bitirir; kapatıp açmak yetmeyebilir.")));
        }

        // (5) Windows is waiting for a restart
        if (facts.RebootPending == true)
            items.Add(Make("reboot", HealthLevel.Warn,
                Loc.T("Windows yeniden başlatma bekliyor"),
                Loc.T("Güncellemeler yeniden başlatılmayı bekliyor. Maçın ortasına denk gelmesin diye oyundan önce yeniden başlat.")));

        // (6) running on battery (a desktop PC says false or nothing: no item)
        if (facts.OnBattery == true)
            items.Add(Make("battery", HealthLevel.Note,
                Loc.T("Bilgisayar pille çalışıyor"),
                Loc.T("Pilde çalışırken bilgisayar genelde daha yavaş olur. Şarj cihazını tak."),
                PowerSettingsUri, Loc.T("Güç ayarlarını aç")));

        // (7) graphics card driver: only the age of the driver is said, never that a newer one exists
        if (!string.IsNullOrWhiteSpace(facts.GpuName) && facts.GpuDriverDate is DateTime driverDate)
        {
            var name = facts.GpuName!.Trim();
            var monthYear = driverDate.ToString("MMMM yyyy", Loc.Culture);
            var old = driverDate.Date < now.Date.AddMonths(-OldDriverMonths);          // exactly 12 months old is not older
            items.Add(Make("gpu.driver", old ? HealthLevel.Note : HealthLevel.Good,
                Loc.T("Ekran kartı sürücüsü"),
                old
                    ? Loc.F("{0}: sürücü {1} tarihli. Üreticinin daha yenisi olabilir.", name, monthYear)
                    : Loc.F("{0}: sürücü {1} tarihli.", name, monthYear)));
        }

        return items;
    }

    // ------------------------------------------------------------------ free space

    private enum SpaceKind { SystemOnly, SystemAndGame, GameOnly }

    private static HealthItem Space(string id, DiskFact d, SpaceKind kind)
    {
        var free = Math.Clamp(d.FreeBytes, 0L, d.TotalBytes);
        var level = SpaceLevel(d.TotalBytes, free);
        var size = Gb(free);

        string title;
        if (kind == SpaceKind.GameOnly) title = Loc.F("Oyun diski {0} {1} boş", d.Drive, size);
        else title = Loc.F("{0} {1} boş", d.Drive, size);

        // A Warn text must be true for every drive that can reach Warn: one with under 15 GB free (updates may really not fit) and also a huge drive with,
        // say, 49 GB free (it fits an update, but it is nearly full). So Warn only says that the free space is low and that room may be limited, never that
        // something does not fit or that the PC slows down: free space is all the code measures.
        string text;
        switch (kind)
        {
            case SpaceKind.GameOnly:
                text = level switch
                {
                    HealthLevel.Good => Loc.T("Oyunlar için yeterince yer var."),
                    HealthLevel.Note => Loc.T("Yer azalıyor. Oyun güncellemeleri için biraz yer açmak iyi olur."),
                    _ => Loc.T("Boş yer az. Oyun güncellemeleri için yer kısıtlı olabilir; biraz yer aç."),
                };
                break;
            case SpaceKind.SystemAndGame:
                text = level switch
                {
                    HealthLevel.Good => Loc.T("Windows ve oyunlar için yeterince yer var."),
                    HealthLevel.Note => Loc.T("Yer azalıyor. Windows ve oyun güncellemeleri için biraz yer açmak iyi olur."),
                    _ => Loc.T("Boş yer az. Windows ve oyun güncellemeleri için yer kısıtlı olabilir; biraz yer aç."),
                };
                break;
            default:
                text = level switch
                {
                    HealthLevel.Good => Loc.T("Windows için yeterince yer var."),
                    HealthLevel.Note => Loc.T("Yer azalıyor. Windows güncellemeleri için biraz yer açmak iyi olur."),
                    _ => Loc.T("Boş yer az. Windows güncellemeleri için yer kısıtlı olabilir; biraz yer aç."),
                };
                break;
        }

        if (level == HealthLevel.Good) return Make(id, level, title, text);
        return Make(id, level, title, text, StorageSettingsUri, Loc.T("Depolama ayarlarını aç"));
    }

    /// <summary>
    /// Warn: below 15 GB free, or below 10 % of the drive while also below 50 GB. Note (not Warn): below 30 GB, or below 20 % of the drive while also below 100 GB.
    /// Good otherwise. Exactly on a line is not below it.
    /// </summary>
    private static HealthLevel SpaceLevel(long total, long free)
    {
        if (free < WarnFreeBytes || (FreeIsBelowPercent(free, total, WarnPercent) && free < WarnPercentCapBytes)) return HealthLevel.Warn;
        if (free < NoteFreeBytes || (FreeIsBelowPercent(free, total, NotePercent) && free < NotePercentCapBytes)) return HealthLevel.Note;
        return HealthLevel.Good;
    }

    /// <summary>"free is below percent % of total", in exact whole-number arithmetic (decimal holds any long), so "exactly 10 %" is exactly on the line.</summary>
    private static bool FreeIsBelowPercent(long free, long total, int percent) => (decimal)free * 100m < (decimal)total * percent;

    /// <summary>"215 GB"; one decimal below 100 GB ("12,3 GB" / "12.3 GB"), none from 100 GB up. 1 GB = 1024 x 1024 x 1024 bytes (what Windows calls GB).</summary>
    private static string Gb(long bytes)
    {
        var ci = Loc.Culture;
        var gb = Math.Max(0L, bytes) / (double)GiB;
        var rounded = Math.Round(gb, 1, MidpointRounding.AwayFromZero);
        return (rounded < 100 ? rounded.ToString("0.0", ci) : gb.ToString("0", ci)) + " GB";
    }

    // ------------------------------------------------------------------ item factory

    /// <summary>The one place an item is made: a button exists only for an allowed settings page and only together with its text.</summary>
    private static HealthItem Make(string id, HealthLevel level, string title, string text, string? actionUri = null, string? actionText = null)
    {
        if (!IsAllowedActionUri(actionUri) || string.IsNullOrWhiteSpace(actionText)) { actionUri = null; actionText = null; }
        return new HealthItem(id, level, title, text, actionUri, actionText);
    }
}

/// <summary>
/// The small decisions of the probe, as pure functions. They are public only so that the tests can reach them (the screen does not need them).
/// </summary>
public static class HealthProbeRules
{
    /// <summary>One line of Win32_VideoController, as read.</summary>
    public sealed record GpuCandidate(string? Name, string? Maker, string? PnpId, ulong AdapterRam, string? DriverDateText, int ConfigErrorCode);

    private const int OldestPlausibleDriverYear = 1995;

    /// <summary>"D:\steam\x" -> 'D'; also "d:/x", "D:" and "\\?\D:\x". A network path, a relative path, an empty text -> null.</summary>
    public static char? DriveLetterOf(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;
        var p = path.Trim();
        if (p.StartsWith(@"\\?\", StringComparison.Ordinal)) p = p.Substring(4);
        if (p.Length >= 2 && p[1] == ':' && char.IsAsciiLetter(p[0])) return char.ToUpperInvariant(p[0]);
        return null;
    }

    /// <summary>MSFT_PhysicalDisk.MediaType: 3 = HDD, 4 = SSD. Everything else (0 unspecified, 5 storage-class memory, unknown) = not known.</summary>
    public static string? MediaTypeName(int mediaType) => mediaType switch { 3 => "HDD", 4 => "SSD", _ => null };

    /// <summary>
    /// The drives the check-up looks at, in this order: the one with Windows, then the one with the game (once when they are the same drive; a drive that is
    /// not known is left out). Nothing else is ever touched: the screen only needs these two, and a sleeping external disk must not be woken for nothing.
    /// </summary>
    public static IReadOnlyList<char> DriveLettersToRead(char? systemLetter, char? gameLetter)
    {
        var letters = new List<char>(2);
        if (systemLetter is char s) letters.Add(char.ToUpperInvariant(s));
        if (gameLetter is char g && !letters.Contains(char.ToUpperInvariant(g))) letters.Add(char.ToUpperInvariant(g));
        return letters;
    }

    /// <summary>
    /// One WMI question, as the storage-kind reading asks it: connect to <paramref name="wmiNamespace"/>, run <paramref name="wql"/> and hand every row
    /// to <paramref name="eachRow"/> as a reader of its properties. Returns true when the question was asked to the end and false when it was stopped
    /// (the game came up, or the part was given up on) before that. Throws when WMI fails.
    /// </summary>
    public delegate bool WmiAsk(string wmiNamespace, string wql, Action<Func<string, object?>> eachRow);

    /// <summary>
    /// Drive letter -> "SSD" / "HDD" (only the letters whose kind is known), from two WMI questions asked one after the other in
    /// root\Microsoft\Windows\Storage: MSFT_PhysicalDisk (DeviceId = the disk number as text, MediaType 3 = HDD, 4 = SSD) and then
    /// MSFT_Partition (DriveLetter, DiskNumber).
    /// <paramref name="stop"/> says "the game is up, or this part was given up on": it is asked before each of the two questions (a new WMI connection each),
    /// and when it says true the question is not asked. Null when nothing could be found out: no disk of a known kind, or a question was stopped.
    /// </summary>
    public static Dictionary<char, string>? MediaTypesFrom(WmiAsk ask, Func<bool> stop)
    {
        const string ns = @"root\Microsoft\Windows\Storage";

        if (stop()) return null;
        var kindOfDisk = new Dictionary<int, string>();
        var finished = ask(ns, "SELECT DeviceId, MediaType FROM MSFT_PhysicalDisk", disk =>
        {
            var kind = MediaTypeName(ToInt(disk("MediaType")));
            var id = ToInt(disk("DeviceId"));
            if (kind != null && id >= 0) kindOfDisk[id] = kind;
        });
        if (!finished || kindOfDisk.Count == 0) return null;

        if (stop()) return null;
        var kindOfLetter = new Dictionary<char, string>();
        finished = ask(ns, "SELECT DriveLetter, DiskNumber FROM MSFT_Partition", partition =>
        {
            var letter = ToLetter(partition("DriveLetter"));
            if (letter != null && kindOfDisk.TryGetValue(ToInt(partition("DiskNumber")), out var kind)) kindOfLetter[letter.Value] = kind;
        });
        return finished ? kindOfLetter : null;
    }

    /// <summary>A WMI number (any width, or text that holds one) -> int; nothing or something else -> -1.</summary>
    public static int ToInt(object? value)
    {
        try { return value == null ? -1 : Convert.ToInt32(value, CultureInfo.InvariantCulture); }
        catch { return -1; }
    }

    /// <summary>A WMI number (any width, or text that holds one) -> ulong; nothing or something else -> 0.</summary>
    public static ulong ToULong(object? value)
    {
        try { return value == null ? 0UL : Convert.ToUInt64(value, CultureInfo.InvariantCulture); }
        catch { return 0UL; }
    }

    /// <summary>A WMI char16 (System.Char), or a number / text that holds one letter -> 'C'. No letter ('\0') -> null.</summary>
    public static char? ToLetter(object? value)
    {
        char c;
        switch (value)
        {
            case char ch: c = ch; break;
            case ushort number: c = (char)number; break;
            case string text when text.Length == 1: c = text[0]; break;
            default: return null;
        }
        return char.IsAsciiLetter(c) ? char.ToUpperInvariant(c) : (char?)null;
    }

    /// <summary>
    /// From GetSystemPowerStatus. ACLineStatus 0 = off the mains, 1 = on the mains, 255 = unknown. BatteryFlag has 128 for "no battery in this PC" and 255 for unknown.
    /// True only when the PC is off the mains AND really has a battery: a desktop PC (no battery, mains) is false, and unclear answers are null, never true.
    /// </summary>
    public static bool? OnBatteryFrom(byte acLineStatus, byte batteryFlag)
    {
        if (acLineStatus == 1) return false;
        if (acLineStatus != 0) return null;
        if (batteryFlag == 255 || (batteryFlag & 128) != 0) return null;
        return true;
    }

    /// <summary>
    /// Pending restart from the two signs the probe trusts. Any sign present = true; false only when both were read and neither is there; else null.
    /// (PendingFileRenameOperations is deliberately NOT a sign: it is often set by harmless installers and updaters for small file swaps, so it alone says nothing about Windows waiting for a restart.)
    /// </summary>
    public static bool? RebootFrom(bool? componentServicingPending, bool? windowsUpdateRequired)
    {
        if (componentServicingPending == true || windowsUpdateRequired == true) return true;
        if (componentServicingPending == false && windowsUpdateRequired == false) return false;
        return null;
    }

    /// <summary>
    /// The date as the driver wrote it. A WMI date is "yyyyMMddHHmmss.ffffff+UUU"; only the first eight characters are used and no time zone is applied
    /// (a driver dated 1 October must not turn into 30 September on a PC west of Greenwich). Zero dates, rubbish and years before 1995 -> null.
    /// </summary>
    public static DateTime? ParseCimDate(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var t = text.Trim();
        if (t.Length < 8) return null;
        if (!DateTime.TryParseExact(t.Substring(0, 8), "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)) return null;
        if (date.Year < OldestPlausibleDriverYear) return null;
        return date;
    }

    /// <summary>
    /// The graphics card the games use. Left out: Windows' own stand-in adapters (the name always contains "Microsoft", also on a Windows in another
    /// language: "Microsoft Basic Display Adapter", "Microsoft Temel Görüntü Bağdaştırıcısı"). Of the rest the best wins: one that works (no device error),
    /// a real PCI card, the most memory (AdapterRAM stops at 4 GB, so a 4 GB+ card beats an integrated one), one with a readable driver date,
    /// an NVIDIA / AMD vendor id over the others. Null when nothing is left.
    /// </summary>
    public static GpuCandidate? PickGpu(IEnumerable<GpuCandidate> candidates)
    {
        return candidates
            .Where(c => c != null && !string.IsNullOrWhiteSpace(c.Name) && !IsMicrosoftAdapter(c))
            .OrderByDescending(c => c.ConfigErrorCode == 0)
            .ThenByDescending(c => c.PnpId != null && c.PnpId.StartsWith(@"PCI\", StringComparison.OrdinalIgnoreCase))
            .ThenByDescending(c => c.AdapterRam)
            .ThenByDescending(c => ParseCimDate(c.DriverDateText) != null)
            .ThenByDescending(c => c.PnpId != null && (c.PnpId.Contains("VEN_10DE", StringComparison.OrdinalIgnoreCase) || c.PnpId.Contains("VEN_1002", StringComparison.OrdinalIgnoreCase)))
            .ThenBy(c => c.Name, StringComparer.Ordinal)
            .FirstOrDefault();
    }

    private static bool IsMicrosoftAdapter(GpuCandidate c) =>
        (c.Name?.Contains("Microsoft", StringComparison.OrdinalIgnoreCase) ?? false) ||
        (c.Maker?.Contains("Microsoft", StringComparison.OrdinalIgnoreCase) ?? false);
}

/// <summary>
/// Real probe. Everything is read-only: DriveInfo (the Windows drive and the game's drive only), the registry (read access), WMI queries (no method is called,
/// nothing is written) and one kernel32 call. It never opens a handle to any program. While cs2.exe runs it reads nothing at all (it returns "nothing known"),
/// whoever calls it: the game is looked at before every part, before every WMI connection and before every WMI row, and a part that Read has given up on
/// (it did not answer in time) does nothing more either.
/// </summary>
public sealed class WindowsHealthProbe : IHealthProbe
{
    private const int WmiTimeoutSeconds = 5;     // each WMI scope: connect and enumerate
    // The four parts that can be slow or stuck (the drives, the storage kinds, the graphics card, the disk failure prediction) are read side by side, each on
    // its own thread; none is waited for longer than this from the start of Read, so Read takes about this long at the very most. A part that has not answered
    // by then is left out (its facts are null / empty) and told to stop (see PartBase.Abandon).
    private const int TotalWaitMs = 6500;

    private static readonly HealthFacts NothingKnown = new(Array.Empty<DiskFact>(), null, null, null, null, null, null);

    private readonly Func<bool> _gameRunning;

    public WindowsHealthProbe() : this(GameIsRunning) { }

    /// <summary>For tests and for a caller that already knows: <paramref name="gameRunning"/> says whether cs2.exe is up (asked before every part of the reading).</summary>
    public WindowsHealthProbe(Func<bool> gameRunning)
    {
        _gameRunning = gameRunning ?? throw new ArgumentNullException(nameof(gameRunning));
    }

    public HealthFacts Read(string? gameFolder)
    {
        var parts = new List<PartBase>();
        try
        {
            if (GameUp()) return NothingKnown;
            var clock = System.Diagnostics.Stopwatch.StartNew();

            // The drives (a sleeping disk can stall) and WMI (the first question of a boot is slow, a stuck one never answers): each of the four gets its own thread
            // and none is waited for longer than TotalWaitMs
            var disksPart = Start(parts, stop => ReadDisks(gameFolder, stop), new List<DiskFact>());
            var mediaPart = Start<Dictionary<char, string>?>(parts, ReadMediaTypes, null);
            var gpuPart = Start<GpuReading?>(parts, ReadGpu, null);
            var failurePart = Start<bool?>(parts, ReadFailurePredicted, null);

            // meanwhile the quick ones, here
            var uptime = Quick(ReadUptimeDays, (double?)null);
            var reboot = Quick(ReadRebootPending, (bool?)null);
            var battery = Quick(ReadOnBattery, (bool?)null);

            var disks = Wait(disksPart, clock, new List<DiskFact>());
            var media = Wait(mediaPart, clock, null);
            var gpu = Wait(gpuPart, clock, null);
            var failure = Wait(failurePart, clock, null);

            var withMedia = new List<DiskFact>(disks.Count);
            foreach (var d in disks)
            {
                string? type = null;
                if (media != null && d.Drive.Length > 0) media.TryGetValue(char.ToUpperInvariant(d.Drive[0]), out type);
                withMedia.Add(type == null ? d : d with { MediaType = type });
            }

            return new HealthFacts(withMedia, uptime, reboot, battery, gpu?.Name, gpu?.Date, failure);
        }
        catch
        {
            return NothingKnown;        // never an exception for the screen
        }
        finally
        {
            // Whatever way Read ends, a part that is still running has nobody waiting for it any more: it must not ask the machine anything else
            foreach (var part in parts) part.Abandon();
        }
    }

    // ------------------------------------------------------------------ the game comes first

    /// <summary>A snapshot of the process list: no handle is opened to any program.</summary>
    private static bool GameIsRunning()
    {
        var ps = System.Diagnostics.Process.GetProcessesByName(Catalog.Cs2ProcessName);
        try { return ps.Length > 0; }
        finally { foreach (var p in ps) p.Dispose(); }
    }

    private bool GameUp()
    {
        try { return _gameRunning(); }
        catch { return true; }          // cannot tell: read nothing
    }

    // ------------------------------------------------------------------ running the parts

    private T Quick<T>(Func<T> work, T fallback)
    {
        try { return GameUp() ? fallback : work(); }
        catch { return fallback; }
    }

    /// <summary>
    /// The flag between Read (which may stop waiting) and the thread of a part (which cannot be cancelled while it sits inside a WMI call): once Read has given up on
    /// the part, the thread finds the flag set at its next check and does nothing more. Volatile: written by one thread, read by another.
    /// </summary>
    private abstract class PartBase
    {
        private volatile bool _abandoned;
        public bool Abandoned => _abandoned;
        public void Abandon() => _abandoned = true;
    }

    private sealed class Part<T> : PartBase
    {
        public Task<T> Work { get; set; } = null!;
    }

    /// <summary>
    /// Starts a part on its own thread. <paramref name="work"/> gets the part's "stop" check (true = the game is up, or Read has given up on this part): it is asked
    /// before the part begins and the part asks it again before each thing it reads. A part that fails or is stopped gives <paramref name="fallback"/>.
    /// </summary>
    private Part<T> Start<T>(List<PartBase> parts, Func<Func<bool>, T> work, T fallback)
    {
        var part = new Part<T>();
        parts.Add(part);
        Func<bool> stop = () => part.Abandoned || GameUp();
        part.Work = Task.Factory.StartNew(() =>
        {
            try { return stop() ? fallback : work(stop); }
            catch { return fallback; }
        }, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);       // LongRunning = its own background thread: a stuck call never holds the app up
        return part;
    }

    /// <summary>The answer of a part, or <paramref name="fallback"/> (and the part is abandoned) when it has not answered by TotalWaitMs after the start of Read.</summary>
    private static T Wait<T>(Part<T> part, System.Diagnostics.Stopwatch clock, T fallback)
    {
        try
        {
            var left = (int)Math.Max(0, TotalWaitMs - clock.ElapsedMilliseconds);
            if (part.Work.Wait(left) && part.Work.IsCompletedSuccessfully) return part.Work.Result;
        }
        catch { /* not known */ }
        part.Abandon();
        return fallback;
    }

    // ------------------------------------------------------------------ disks

    /// <summary>
    /// Only the drive with Windows and the drive of the game folder, and only when they are fixed and ready (no USB sticks, no CD drives, no network drives, no locked
    /// volumes). Every other drive is left alone: nobody asked about it, and an external disk that sleeps would be woken (and could stall the reading) for nothing.
    /// </summary>
    private static List<DiskFact> ReadDisks(string? gameFolder, Func<bool> stop)
    {
        var list = new List<DiskFact>();
        var systemLetter = HealthProbeRules.DriveLetterOf(Environment.SystemDirectory);
        var gameLetter = HealthProbeRules.DriveLetterOf(gameFolder);
        foreach (var letter in HealthProbeRules.DriveLettersToRead(systemLetter, gameLetter))
        {
            if (stop()) break;                  // the game is up, or Read stopped waiting: no further drive is touched
            try
            {
                var drive = new DriveInfo(letter + @":\");
                if (drive.DriveType != DriveType.Fixed || !drive.IsReady) continue;
                var total = drive.TotalSize;
                if (total <= 0) continue;
                var free = Math.Clamp(drive.AvailableFreeSpace, 0L, total);                   // what the user can use (a quota counts)
                list.Add(new DiskFact(letter + ":", total, free, null, letter == systemLetter, gameLetter != null && letter == gameLetter));
            }
            catch { /* this drive could not be read: it is left out */ }
        }
        return list;
    }

    // ------------------------------------------------------------------ storage kinds

    /// <summary>Drive letter -> "SSD" / "HDD" (only the letters whose kind is known); the two questions and their order are in <see cref="HealthProbeRules.MediaTypesFrom"/>.</summary>
    private static Dictionary<char, string>? ReadMediaTypes(Func<bool> stop) =>
        HealthProbeRules.MediaTypesFrom((ns, wql, eachRow) => Query(stop, ns, wql, eachRow), stop);

    // ------------------------------------------------------------------ uptime, restart, battery

    /// <summary>Days since the PC started, from the tick counter (Environment.TickCount64: milliseconds since the start of this Windows session).</summary>
    private static double? ReadUptimeDays()
    {
        var ms = Environment.TickCount64;
        if (ms < 0) return null;
        return ms / 86_400_000.0;
    }

    // The two signs of "Windows waits for a restart" that are used: the KEY existing is the sign (Windows creates it when a restart is needed and removes it
    // at the next start). Their parents exist on this PC while the keys themselves are absent, which is what a PC that waits for nothing looks like.
    //   Component Based Servicing\RebootPending       - set by Windows servicing (updates, features) that need a restart to finish
    //   WindowsUpdate\Auto Update\RebootRequired      - set by Windows Update itself
    // Not used: Session Manager\PendingFileRenameOperations (often set by harmless installers and updaters for small file swaps, so it alone says nothing),
    // and the CBS keys PackagesPending / RebootInProgress / SessionsPending (they describe work in progress or can stay behind).
    private const string ServicingPendingKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Component Based Servicing\RebootPending";
    private const string UpdateRequiredKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\WindowsUpdate\Auto Update\RebootRequired";

    private static bool? ReadRebootPending()
    {
        using var hklm = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
        return HealthProbeRules.RebootFrom(KeyExists(hklm, ServicingPendingKey), KeyExists(hklm, UpdateRequiredKey));
    }

    /// <summary>True / false when the key is / is not there; null when it could not be looked at (no access...).</summary>
    private static bool? KeyExists(RegistryKey root, string subKey)
    {
        try
        {
            using var key = root.OpenSubKey(subKey, false);          // read access only
            return key != null;
        }
        catch { return null; }
    }

    private static bool? ReadOnBattery()
    {
        if (!PowerNative.GetSystemPowerStatus(out var status)) return null;
        return HealthProbeRules.OnBatteryFrom(status.ACLineStatus, status.BatteryFlag);
    }

    private static class PowerNative
    {
        [StructLayout(LayoutKind.Sequential)]
        public struct SystemPowerStatus
        {
            public byte ACLineStatus;
            public byte BatteryFlag;
            public byte BatteryLifePercent;
            public byte SystemStatusFlag;
            public uint BatteryLifeTime;
            public uint BatteryFullLifeTime;
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool GetSystemPowerStatus(out SystemPowerStatus status);
    }

    // ------------------------------------------------------------------ graphics card

    private sealed record GpuReading(string Name, DateTime? Date);

    private static GpuReading? ReadGpu(Func<bool> stop)
    {
        var found = new List<HealthProbeRules.GpuCandidate>();
        var finished = Query(stop, @"root\cimv2", "SELECT Name, AdapterCompatibility, PNPDeviceID, AdapterRAM, DriverDate, ConfigManagerErrorCode FROM Win32_VideoController", adapter =>
        {
            var errorCode = HealthProbeRules.ToInt(adapter("ConfigManagerErrorCode"));
            found.Add(new HealthProbeRules.GpuCandidate(
                adapter("Name") as string,
                adapter("AdapterCompatibility") as string,
                adapter("PNPDeviceID") as string,
                HealthProbeRules.ToULong(adapter("AdapterRAM")),
                adapter("DriverDate") as string,
                errorCode < 0 ? 0 : errorCode));
        });
        if (!finished) return null;          // stopped half way: the cards seen so far may not include the best one

        var best = HealthProbeRules.PickGpu(found);
        if (best == null) return null;
        return new GpuReading((best.Name ?? "").Trim(), HealthProbeRules.ParseCimDate(best.DriverDateText));
    }

    // ------------------------------------------------------------------ disk failure prediction

    /// <summary>
    /// root\wmi MSStorageDriver_FailurePredictStatus (the SMART summary): true when any disk that reports says PredictFailure.
    /// null when no disk reports (many NVMe drives and some drivers do not offer this class: "Not supported") or the class cannot be read.
    /// </summary>
    private static bool? ReadFailurePredicted(Func<bool> stop)
    {
        var reporting = 0;
        var predicted = false;
        var finished = Query(stop, @"root\wmi", "SELECT PredictFailure FROM MSStorageDriver_FailurePredictStatus", status =>
        {
            if (status("PredictFailure") is bool failing)
            {
                reporting++;
                if (failing) predicted = true;
            }
        });
        if (!finished || reporting == 0) return null;          // stopped half way: "no failure predicted" would not be known
        return predicted;
    }

    // ------------------------------------------------------------------ WMI helpers

    /// <summary>
    /// Runs a SELECT in a WMI namespace with a time limit and hands each row over (as a reader of its properties); every object is disposed.
    /// <paramref name="stop"/> (the game is up, or Read gave up on this part) is asked before the connection is made, before the enumeration begins and before every
    /// row is fetched (each fetch can be a call to WMI): when it says true nothing more is done and false is returned. True = asked to the end.
    /// Throws on failure (the caller's part is then "not known").
    /// </summary>
    private static bool Query(Func<bool> stop, string wmiNamespace, string wql, Action<Func<string, object?>> eachRow)
    {
        if (stop()) return false;
        var timeout = TimeSpan.FromSeconds(WmiTimeoutSeconds);
        var scope = new ManagementScope(@"\\.\" + wmiNamespace, new ConnectionOptions { Timeout = timeout });
        scope.Connect();                     // may wait up to the timeout: the answer is looked at again right after

        if (stop()) return false;
        var options = new System.Management.EnumerationOptions { Timeout = timeout };          // (System.IO has an EnumerationOptions too)
        using var searcher = new ManagementObjectSearcher(scope, new ObjectQuery(wql), options);
        using var rows = searcher.Get();
        using var enumerator = rows.GetEnumerator();
        while (true)
        {
            if (stop()) return false;
            if (!enumerator.MoveNext()) return true;
            using var row = enumerator.Current;
            eachRow(name => row[name]);
        }
    }
}
