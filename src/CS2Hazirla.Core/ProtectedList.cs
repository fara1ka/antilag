namespace CS2Hazirla.Core;

/// <summary>
/// The hard-coded, NON-negotiable protection list: only what is needed to use the PC, the hardware, Steam, CS2 and FACEIT.
/// There is deliberately no API to remove entries at runtime and the settings file has no field that feeds into these sets.
///
/// Everything else (VPN, third-party antivirus, other anti-cheats, DPI/proxy tools, peripheral software, overlays,
/// Discord, developer tools...) is NOT here: those are closable groups the user switches on/off in the UI (see Catalog).
/// Over-matching is acceptable here (an unnecessarily protected process only means "not closed").
/// </summary>
public static class ProtectedList
{
    public static string Normalize(string name)
    {
        var n = name.Trim().ToLowerInvariant();
        return n.EndsWith(".exe", StringComparison.Ordinal) ? n[..^4] : n;
    }

    // ---- processes: exact names ----
    private static readonly HashSet<string> CoreNames = new(StringComparer.Ordinal)
    {
        // Windows core
        "system", "idle", "system idle process", "registry", "memory compression", "secure system",
        "smss", "csrss", "wininit", "services", "lsass", "lsaiso", "winlogon", "svchost", "dwm", "explorer",
        "audiodg", "ctfmon", "fontdrvhost", "sihost", "taskhostw", "taskhost", "runtimebroker",
        "searchhost", "searchindexer", "searchapp", "searchprotocolhost", "searchfilterhost",
        "startmenuexperiencehost", "shellexperiencehost", "textinputhost", "applicationframehost",
        "wmiprvse", "dllhost", "taskmgr", "backgroundtaskhost", "lockapp", "logonui", "userinit",
        "rundll32", "spoolsv", "wudfhost", "dashost", "sppsvc", "trustedinstaller", "tiworker", "msiexec",
        "werfault", "wuauclt", "usocoreworker", "mousocoreworker", "conhost", "cmd", "powershell", "pwsh",
        "windowsterminal", "wt", "openconsole", "winstore.app", "systemsettings",
        "smartscreen", "wlanext", "sgrmbroker", "unsecapp", "ngciso", "wmiapsrv",
        // Windows Defender (never disabled)
        "msmpeng", "nissrv", "mpcmdrun", "mpdefendercoreservice", "securityhealthsystray",
        "securityhealthservice", "msseces", "mssense", "sensecncproxy", "senseir", "windefend",
        // CS2, Steam and FACEIT: the only game-related software that stays
        "cs2", "csgo", "hl2", "steam", "steamwebhelper", "steamservice", "steamerrorreporter",
        "gameoverlayui", "faceit", "faceitclient", "faceit-ac",
        // this app itself and the WSL VM (cannot be closed from outside anyway)
        "cs2hazirla", "clear", "antilag", "vmmem", "vmmemwsl",
        // embedded web runtimes many apps depend on (they exit by themselves once their host apps are closed)
        "msedgewebview2", "webview2", "crashpad_handler", "cefsharp.browsersubprocess",
    };

    private static readonly string[] CorePrefixes =
    {
        // Steam, Valve and the game itself (prefix match: cs2*, csgo*, any Valve helper)
        "steam", "gameoverlay", "cs2", "csgo", "valve", "hl2", "srcds", "crashhandler", "gldriverquery",
        "vrcompositor", "vrserver", "vrmonitor", "vrdashboard", "vrstartup",
        // FACEIT anti-cheat (every other anti-cheat is closable)
        "faceit",
        // GPU DRIVER components only. NVIDIA's overlay / Share / GeForce Experience / App front-ends are NOT driver components:
        // they add overhead and are closable (Catalog: overlay group); the display container hosting the driver services is not.
        "nvcontainer", "nvdisplay", "radeon", "amd", "atieclxx", "atiesrxx", "igfx", "igcc", "intel",
        // audio drivers
        "rtk", "ravbg", "ravcpl", "realtek", "nahimic", "sonicstudio", "waves", "dolby", "creative",
        // Windows Defender components living outside %WINDIR%
        "msmpeng", "mpdefender", "defender", "windowsdefender", "securityhealth", "sgrm", "sechealth",
        "webview2", "msedgewebview",
    };

    private static readonly string[] CoreContains = { "nvcontainer", "faceit" };

    // ---- extra protection in stream mode ----
    private static readonly string[] StreamPrefixes =
    {
        "obs", "streamlabs", "slobs", "inputoverlay", "input-overlay", "keyoverlay", "keyviewer",
        "nohboard", "keystrokes", "kbdoverlay", "discord", "streamdeck", "elgato", "voicemeeter",
        "streamer.bot", "streamerbot", "touchportal", "twitchstudio", "lioranboard", "xsplit",
        "meld", "vban", "nvidia broadcast", "camerahub", "logicapture",
    };

    // ---- services ----
    private static readonly HashSet<string> CoreServices = new(StringComparer.Ordinal)
    {
        "rpcss", "rpceptmapper", "dcomlaunch", "lsm", "samss", "eventlog", "eventsystem", "plugplay",
        "power", "profsvc", "winmgmt", "cryptsvc", "bfe", "mpssvc", "audiosrv", "audioendpointbuilder",
        "windefend", "wdnissvc", "wdfilter", "sense", "securityhealthservice", "wscsvc", "schedule",
        "themes", "shellhwdetection", "gpsvc", "usermanager", "dhcp", "dnscache", "nlasvc", "netprofm",
        "lanmanworkstation", "lanmanserver", "wlansvc", "dot3svc", "nsi", "netman", "iphlpsvc", "keyiso",
        "msiserver", "trustedinstaller", "vss", "wlidsvc", "timebrokersvc", "systemeventsbroker",
        "tokenbroker", "coremessagingregistrar", "cdpsvc", "fontcache", "camsvc", "state repository service",
        "staterepository", "appinfo", "brokerinfrastructure", "lmhosts", "wcmsvc",
    };

    // Must cover every GPU/audio DRIVER prefix of CorePrefixes: the "pick a service" list offers any running service that is not protected here.
    private static readonly string[] ServicePrefixes =
    {
        "nv", "amd", "ati", "radeon", "steam", "faceit",
        "rtk", "realtek", "nahimic", "audio", "bth", "bluetooth", "hid", "intel", "igfx", "wd", "mpssvc",
        "windefend", "sense", "securityhealth",
        "igcc", "sonicstudio", "waves", "dolby", "dax", "creative",
    };

    private static readonly string[] ServiceContains = { "faceit" };

    public static IEnumerable<string> AllExactProcessNames => CoreNames;

    public static IEnumerable<string> AllProcessPrefixes => CorePrefixes;

    public static IEnumerable<string> AllStreamPrefixes => StreamPrefixes;

    public static IEnumerable<string> AllExactServiceNames => CoreServices;

    public static string WindowsDirectory { get; set; } =
        Environment.GetFolderPath(Environment.SpecialFolder.Windows);

    // Anything that LIVES inside these folders is protected, whatever its name (Steam + game libraries, FACEIT).
    private static string[] _protectedRoots = DefaultRoots();

    private static string[] DefaultRoots()
    {
        var roots = new List<string>();
        foreach (var f in new[] { Environment.SpecialFolder.ProgramFiles, Environment.SpecialFolder.ProgramFilesX86, Environment.SpecialFolder.LocalApplicationData })
        {
            var b = Environment.GetFolderPath(f);
            if (b.Length == 0) continue;
            roots.Add(Path.Combine(b, "FACEIT"));
            roots.Add(Path.Combine(b, "FACEIT AC"));
            roots.Add(Path.Combine(b, "Steam"));
            roots.Add(Path.Combine(b, "Valve"));
        }
        return roots.ToArray();
    }

    /// <summary>Adds detected folders (Steam install, Steam library folders) to the built-in protected roots. Can only ADD.</summary>
    public static void AddProtectedRoots(IEnumerable<string> roots)
    {
        var all = _protectedRoots.ToList();
        foreach (var r in roots)
        {
            if (string.IsNullOrWhiteSpace(r)) continue;
            string full;
            try { full = Path.GetFullPath(r); } catch { continue; }
            if (!all.Contains(full, StringComparer.OrdinalIgnoreCase)) all.Add(full);
        }
        _protectedRoots = all.ToArray();
    }

    public static IReadOnlyList<string> ProtectedRoots => _protectedRoots;

    public static bool IsProcessProtected(string name, string? path, bool streamMode)
    {
        var n = Normalize(name);
        if (n.Length == 0) return true;
        if (CoreNames.Contains(n)) return true;
        foreach (var p in CorePrefixes) if (n.StartsWith(p, StringComparison.Ordinal)) return true;
        foreach (var c in CoreContains) if (n.Contains(c, StringComparison.Ordinal)) return true;

        if (!string.IsNullOrEmpty(path))
        {
            var win = WindowsDirectory.TrimEnd('\\') + "\\";
            if (path.StartsWith(win, StringComparison.OrdinalIgnoreCase)) return true;
            foreach (var root in _protectedRoots)
            {
                var r = root.TrimEnd('\\') + "\\";
                if (path.StartsWith(r, StringComparison.OrdinalIgnoreCase)) return true;
            }
        }

        if (streamMode)
            foreach (var p in StreamPrefixes) if (n.StartsWith(p, StringComparison.Ordinal)) return true;

        return false;
    }

    public static bool IsServiceProtected(string serviceName)
    {
        var n = serviceName.Trim().ToLowerInvariant();
        if (n.Length == 0) return true;
        if (CoreServices.Contains(n)) return true;
        foreach (var p in ServicePrefixes) if (n.StartsWith(p, StringComparison.Ordinal)) return true;
        foreach (var c in ServiceContains) if (n.Contains(c, StringComparison.Ordinal)) return true;
        return false;
    }
}
