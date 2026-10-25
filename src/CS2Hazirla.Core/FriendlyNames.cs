using System.Collections.Concurrent;
using System.Diagnostics;

namespace CS2Hazirla.Core;

/// <summary>Human-readable names for programs ("Google Chrome" instead of "chrome.exe"). Purely cosmetic; always falls back to the file name.</summary>
public static class FriendlyNames
{
    private static readonly ConcurrentDictionary<string, string> Cache = new(StringComparer.OrdinalIgnoreCase);

    private static readonly HashSet<string> TooGeneric = new(StringComparer.OrdinalIgnoreCase)
    {
        "application", "uygulama", "launcher", "service", "setup", "update", "updater", "host", "main", "program", "app",
    };

    /// <summary>"Google Chrome" for chrome.exe when the file carries a description; otherwise the exe name.</summary>
    public static string ForProcess(string normalizedName, string? path)
    {
        var key = normalizedName + "|" + (path ?? "");
        return Cache.GetOrAdd(key, _ => Resolve(normalizedName, path));
    }

    // The names people know, for programs whose files carry no readable description (Store apps, tools) or that run as several exe files
    // (a launcher and its web helper): the home list shows one row for such a program, and a switch on it covers all of them.
    private static readonly Dictionary<string, string> Known = new(StringComparer.Ordinal)
    {
        ["whatsapp.root"] = "WhatsApp", ["whatsappdesktop"] = "WhatsApp",
        ["ms-teams"] = "Microsoft Teams", ["msteams"] = "Microsoft Teams", ["teams"] = "Microsoft Teams",
        ["epicgameslauncher"] = "Epic Games Launcher", ["epicwebhelper"] = "Epic Games Launcher",
        ["riotclientservices"] = "Riot Client", ["riotclientux"] = "Riot Client", ["riotclientuxrender"] = "Riot Client",
        ["eadesktop"] = "EA app", ["eabackgroundservice"] = "EA app",
        ["origin"] = "Origin", ["originwebhelperservice"] = "Origin",
        ["galaxyclient"] = "GOG Galaxy", ["galaxyclient helper"] = "GOG Galaxy",
        ["ubisoftconnect"] = "Ubisoft Connect", ["upc"] = "Ubisoft Connect", ["uplay"] = "Ubisoft Connect", ["ubisoftgamelauncher"] = "Ubisoft Connect",
        ["battle.net"] = "Battle.net",
    };

    private static string Resolve(string name, string? path)
    {
        if (name.StartsWith("goodbyedpi", StringComparison.Ordinal)) return "GoodbyeDPI";       // the name the user knows (its file carries no description)
        if (Known.TryGetValue(name, out var known)) return known;
        try
        {
            if (!string.IsNullOrEmpty(path) && File.Exists(path))
            {
                var vi = FileVersionInfo.GetVersionInfo(path);
                foreach (var candidate in new[] { vi.FileDescription, vi.ProductName })
                {
                    var c = candidate?.Trim();
                    if (string.IsNullOrEmpty(c) || c.Length > 48 || TooGeneric.Contains(c)) continue;
                    if (c.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) continue;       // a file name is not a readable name
                    return c;
                }
            }
        }
        catch { /* cosmetic only */ }
        return Pretty(name);
    }

    private static string Pretty(string name)
    {
        if (name.Length == 0) return name;
        return char.ToUpperInvariant(name[0]) + name[1..];
    }

    public static string Memory(long bytes) =>
        bytes >= 1L << 30 ? $"{bytes / 1073741824.0:0.0} GB" : $"{bytes / 1048576} MB";
}
