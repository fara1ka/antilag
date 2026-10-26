using System.Text.Json.Serialization;
using Microsoft.Win32;

namespace CS2Hazirla.Core;

public enum ProfileKind { Safe, Maximum, Stream }

public enum RegHive { CurrentUser, LocalMachine }

public enum ServiceRunState { Stopped, Running, Other }

public enum ServiceStart { Boot, System, Automatic, Manual, Disabled, Unknown }

public record ProcInfo(int Pid, string Name, string? Path, int SessionId, bool HasWindow, long WorkingSetBytes);

/// <summary>A process identity (pid + real process name). Close/kill verify BOTH against the live snapshot before any handle is opened,
/// so a recycled pid can never end up pointing at the game, Steam or an anti-cheat process.</summary>
public record ProcRef(int Pid, string Name);

/// <summary>Thrown by the system layer when a state-changing call is attempted while cs2.exe exists.</summary>
public class GameRunningException : InvalidOperationException
{
    public GameRunningException() : base("CS2 çalışıyor: oyun açıkken sistemde hiçbir değişiklik yapılmaz (anti-cheat güvenliği).") { }
}

public record ServiceInfo(
    string Name,
    string DisplayName,
    ServiceRunState State,
    ServiceStart StartType,
    bool CanStop,
    IReadOnlyList<string> RunningDependents);

public record PowerScheme(string Guid, string Name);

public record PowerState(string ActiveGuid, IReadOnlyList<PowerScheme> Schemes);

/// <summary>Serialized registry value. Data encoding depends on Kind (see RegCodec).</summary>
public record RegValue(RegistryValueKind Kind, string Data);

public record MemoryInfo(long TotalBytes, long AvailableBytes, long StandbyBytes);

public record PingStats(string Host, int Sent, int Received, double AvgMs, double JitterMs, double LossPercent, string? Error = null);

public record NicInfo(string Name, string Guid, string PnpId, string? PowerInstanceName, bool? PowerSaveEnabled, bool IsWireless);

public record RamModule(long CapacityBytes, int? SpeedMhz, int? ConfiguredMhz);

public record MouseParams(int Threshold1, int Threshold2, int Accel)
{
    public bool AccelerationOn => Accel != 0;
}

/// <summary>
/// Flags of the Sticky/Toggle/Filter Keys accessibility features. Bit 0x4 = "shortcut active": the keyboard
/// shortcut (Shift x5, Num Lock held 5 s, Right Shift held 8 s) that pops a dialog and can minimize the game.
/// </summary>
public record AccessibilityFlags(uint Sticky, uint Toggle, uint Filter)
{
    public const uint HotkeyActive = 0x4;
    public const uint ConfirmHotkey = 0x8;
    public bool AnyHotkeyActive => ((Sticky | Toggle | Filter) & HotkeyActive) != 0;

    public AccessibilityFlags WithoutHotkeys() => new(
        Sticky & ~(HotkeyActive | ConfirmHotkey), Toggle & ~(HotkeyActive | ConfirmHotkey), Filter & ~(HotkeyActive | ConfirmHotkey));
}

public record MetricsSnapshot(
    DateTime AtUtc,
    int ProcessCount,
    long FreeRamBytes,
    long StandbyBytes,
    double CpuIdlePercent,
    PingStats Ping);

/// <summary>Strictly typed registry value encode/decode so the journal can restore the exact original value.</summary>
public static class RegCodec
{
    public static string Encode(RegistryValueKind kind, object value) => kind switch
    {
        RegistryValueKind.DWord => unchecked((uint)Convert.ToInt32(value)).ToString(),
        RegistryValueKind.QWord => Convert.ToInt64(value).ToString(),
        RegistryValueKind.Binary => Convert.ToBase64String((byte[])value),
        RegistryValueKind.MultiString => string.Join("\0", (string[])value),
        _ => Convert.ToString(value) ?? "",
    };

    public static object Decode(RegistryValueKind kind, string data) => kind switch
    {
        RegistryValueKind.DWord => unchecked((int)uint.Parse(data)),
        RegistryValueKind.QWord => long.Parse(data),
        RegistryValueKind.Binary => Convert.FromBase64String(data),
        RegistryValueKind.MultiString => data.Split('\0'),
        _ => data,
    };
}

public class ServiceRule
{
    public string Name { get; set; } = "";
    /// <summary>"update" (network/bandwidth related) or "other".</summary>
    public string Group { get; set; } = "other";
    public bool Enabled { get; set; } = true;
}

/// <summary>A profile the user made on the "Profiller" page: a name and the switches exactly as they were saved.</summary>
public class SavedProfile
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "";
    /// <summary>The preset it started from. It decides the defaults of switches that were added later, and whether the stream tools (OBS...) are protected.</summary>
    public ProfileKind Base { get; set; } = ProfileKind.Maximum;
    public Dictionary<string, bool> Options { get; set; } = new();
}

public class AppSettings
{
    // The active preset and its switches belong to ONE session on purpose: every start begins with the defaults of "Tam temizlik"
    // (see ProfileSession). Only the user's saved profiles (SavedProfiles) and lists survive a restart.
    [JsonIgnore] public ProfileKind Profile { get; set; } = ProfileKind.Maximum;
    [JsonIgnore] public Dictionary<string, bool> Options { get; set; } = new();
    /// <summary>Profiles the user saved on the "Profiller" page.</summary>
    public List<SavedProfile> SavedProfiles { get; set; } = new();
    /// <summary>User-curated "never close" list ("Her zaman açık kalsın"; process names without .exe, lowercase).</summary>
    public List<string> NeverClose { get; set; } = new();
    /// <summary>User-curated "Her seferinde kapat" list.</summary>
    public List<string> ExtraProcesses { get; set; } = new();
    public List<ServiceRule> Services { get; set; } = Catalog.DefaultServices();
    /// <summary>Readable names of programs/services the user picked ("onedrive" → "Microsoft OneDrive", "svc:xyz" → service title), so the lists stay readable after a restart.</summary>
    public Dictionary<string, string> DisplayNames { get; set; } = new();
    public string PingTarget { get; set; } = "1.1.1.1";
    /// <summary>
    /// Always on in the app: Geri yükle reopens the programs that were closed. (The switch in Ayarlar was removed in 3.7: nobody understood it. A value that an older
    /// settings.json carries is ignored, so nobody is left with a setting that cannot be changed any more. The Orchestrator still honours the value; the tests use it.)
    /// </summary>
    [JsonIgnore] public bool RestartClosedApps { get; set; } = true;
    /// <summary>Once when the app opens, one small request asks GitHub for the newest version number. Nothing about the user is sent.</summary>
    public bool CheckForUpdates { get; set; } = true;
    /// <summary>
    /// A newer version that the check finds is downloaded and installed by the app itself, right after it opened (on by default; needs <see cref="CheckForUpdates"/>).
    /// Off: the card only tells and the user presses Güncelle. Never while CS2 runs: the app does not even start then.
    /// </summary>
    public bool AutoUpdate { get; set; } = true;
    /// <summary>
    /// The version for which an automatic update was already started. It is tried ONCE per version: if the installer failed after the app had closed, the next start must
    /// not try it again and again (the user can still press Güncelle). Forgotten again only when the game opened before anything was transferred.
    /// </summary>
    public string AutoUpdateTried { get; set; } = "";
    /// <summary>"tr" or "en", chosen on the Ayarlar page. Empty = the user never chose: Turkish on a Turkish Windows, English on every other (see <see cref="Loc.Detect"/>).</summary>
    public string Language { get; set; } = "";

    public bool IsOn(string optionId)
    {
        if (Options.TryGetValue(optionId, out var v)) return v;
        return Catalog.ProfileDefault(Profile, optionId);
    }

    public void ApplyProfile(ProfileKind kind)
    {
        Profile = kind;
        Options = Catalog.Options.ToDictionary(o => o.Id, o => Catalog.ProfileDefault(kind, o.Id));
    }
}

// ---- session journal (state.json) ----

public class ServiceRecord
{
    public string Name { get; set; } = "";
    public bool WasRunning { get; set; }
    public ServiceStart StartType { get; set; }
    public bool Reverted { get; set; }
}

public class RegistryRecord
{
    public RegHive Hive { get; set; }
    public string SubKey { get; set; } = "";
    public string Name { get; set; } = "";
    public bool KeyExisted { get; set; }
    public bool ValueExisted { get; set; }
    public RegistryValueKind OldKind { get; set; }
    public string? OldData { get; set; }
    public bool Reverted { get; set; }
}

public class ProcessRecord
{
    public string Name { get; set; } = "";
    public string? Path { get; set; }
    /// <summary>Legacy flag (older journals): this program was meant to come back after a revert.</summary>
    public bool RestartOnRevert { get; set; }
    /// <summary>"Geri yükle" reopens every program that is safe to launch without arguments (not helpers, updaters or parameter-driven tools).</summary>
    public bool CanReopen { get; set; }
    /// <summary>The switch that closed this program (e.g. "proc.cloud"). Older journals do not have it. A later run uses it to decide whether the program will be closed again.</summary>
    public string? ClosedBy { get; set; }
    public bool Reverted { get; set; }
}

public class NicPowerRecord
{
    public string InstanceName { get; set; } = "";
    public bool OldEnabled { get; set; }
    public bool Reverted { get; set; }
}

public class MouseRecord
{
    public int Threshold1 { get; set; }
    public int Threshold2 { get; set; }
    public int Accel { get; set; }
    public bool Reverted { get; set; }
}

public class AccessibilityRecord
{
    public uint Sticky { get; set; }
    public uint Toggle { get; set; }
    public uint Filter { get; set; }
    public bool Reverted { get; set; }
}

public class SessionState
{
    /// <summary>
    /// The format of this file. A newer Antilag may one day write a higher number: an older one then leaves the file alone and says so, instead of
    /// setting it aside as "corrupt" (that would break "everything can be restored").
    /// </summary>
    public const int CurrentSchema = 1;
    public int Schema { get; set; } = CurrentSchema;

    public DateTime StartedUtc { get; set; } = DateTime.UtcNow;
    public string Profile { get; set; } = "";

    public string? OriginalPowerGuid { get; set; }
    public string? CreatedPowerGuid { get; set; }
    public bool PowerReverted { get; set; }

    public List<ServiceRecord> Services { get; set; } = new();
    public List<RegistryRecord> Registry { get; set; } = new();
    public List<ProcessRecord> Processes { get; set; } = new();
    public List<NicPowerRecord> NicPower { get; set; } = new();
    public MouseRecord? Mouse { get; set; }
    public AccessibilityRecord? Accessibility { get; set; }
}
