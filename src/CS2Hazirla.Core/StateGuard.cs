using System.Text.RegularExpressions;

namespace CS2Hazirla.Core;

/// <summary>
/// state.json lives in the user's profile (user-writable) but is replayed by an ELEVATED process on revert.
/// Everything read from it is therefore checked against what this app can actually have written; anything else is ignored.
/// </summary>
public static class StateGuard
{
    private static readonly string[] HkcuAllowed =
    {
        @"Software\Microsoft\GameBar",
        @"System\GameConfigStore",
        @"Software\Microsoft\Windows\CurrentVersion\GameDVR",
        @"Software\Microsoft\Windows\CurrentVersion\PushNotifications",
        @"Software\Microsoft\Windows NT\CurrentVersion\AppCompatFlags\Layers",
        @"Software\Microsoft\DirectX\UserGpuPreferences",
    };

    /// <summary>Extra HKCU roots (tests only: the app's own sandbox keys). Empty in production.</summary>
    public static List<string> ExtraAllowedHkcuRoots { get; } = new();

    /// <summary>Seam for tests; production always checks the real file system.</summary>
    public static Func<string, bool> FileExists { get; set; } = File.Exists;

    // HKLM is allowed ONLY so that journals written by 2.0.0 and earlier (which could contain the old "Nagle" option's network card values)
    // can still be reverted. Nothing writes these values any more.
    private const string TcpInterfacesPrefix = @"SYSTEM\CurrentControlSet\Services\Tcpip\Parameters\Interfaces\{";
    private static readonly string[] HklmValueNames = { "TcpAckFrequency", "TCPNoDelay" };
    private static readonly Regex ServiceName = new(@"^[A-Za-z0-9_.$\- ]{1,256}$", RegexOptions.Compiled);

    public static bool IsAllowed(RegistryRecord r)
    {
        var key = r.SubKey.Trim('\\');
        if (key.Contains("..", StringComparison.Ordinal)) return false;

        if (r.Hive == RegHive.CurrentUser)
            return HkcuAllowed.Concat(ExtraAllowedHkcuRoots).Any(p => key.Equals(p, StringComparison.OrdinalIgnoreCase)
                                        || key.StartsWith(p + "\\", StringComparison.OrdinalIgnoreCase));

        return key.StartsWith(TcpInterfacesPrefix, StringComparison.OrdinalIgnoreCase)
               && HklmValueNames.Contains(r.Name, StringComparer.OrdinalIgnoreCase);
    }

    public static bool IsValidServiceName(string name) => ServiceName.IsMatch(name);

    public static bool IsValidGuid(string? g) => g != null && Guid.TryParse(g, out _);

    /// <summary>Only an existing local .exe may be relaunched (de-elevated) after the game.</summary>
    public static bool IsSafeRestartPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return false;
        try
        {
            if (!Path.IsPathRooted(path) || path.StartsWith(@"\\", StringComparison.Ordinal)) return false;
            return path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) && FileExists(path);
        }
        catch { return false; }
    }
}
