using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace CS2Hazirla.Core;

public static class SteamLocator
{
    public static string? FindSteamPath()
    {
        try
        {
            using var hkcu = RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, RegistryView.Registry64);
            using var k = hkcu.OpenSubKey(@"Software\Valve\Steam");
            if (k?.GetValue("SteamPath") is string p && Directory.Exists(p)) return Path.GetFullPath(p);
        }
        catch { }
        foreach (var view in new[] { RegistryView.Registry32, RegistryView.Registry64 })
        {
            try
            {
                using var hklm = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view);
                using var k = hklm.OpenSubKey(@"SOFTWARE\Valve\Steam");
                if (k?.GetValue("InstallPath") is string p && Directory.Exists(p)) return Path.GetFullPath(p);
            }
            catch { }
        }
        return null;
    }

    public static string? FindSteamExe()
    {
        var root = FindSteamPath();
        if (root == null) return null;
        var exe = Path.Combine(root, "steam.exe");
        return File.Exists(exe) ? exe : null;
    }

    /// <summary>Library folder paths found in libraryfolders.vdf (new and old format).</summary>
    public static IEnumerable<string> ParseLibraryPaths(string vdfText)
    {
        var found = new List<string>();
        foreach (Match m in Regex.Matches(vdfText, "\"path\"\\s*\"((?:[^\"\\\\]|\\\\.)*)\"", RegexOptions.IgnoreCase))
            found.Add(Unescape(m.Groups[1].Value));
        // pre-2021 format: "1"  "D:\\Games\\SteamLibrary"
        foreach (Match m in Regex.Matches(vdfText, "\"\\d+\"\\s+\"([A-Za-z]:\\\\\\\\(?:[^\"\\\\]|\\\\.)*)\""))
            found.Add(Unescape(m.Groups[1].Value));
        return found.Distinct(StringComparer.OrdinalIgnoreCase);
    }

    private static string Unescape(string s) => s.Replace(@"\\", @"\");

    public static string? ParseInstallDir(string acfText)
    {
        var m = Regex.Match(acfText, "\"installdir\"\\s+\"([^\"]+)\"", RegexOptions.IgnoreCase);
        return m.Success ? m.Groups[1].Value : null;
    }

    public static IReadOnlyList<string> GetLibraryFolders(string steamPath)
    {
        var libs = new List<string> { steamPath };
        var vdf = Path.Combine(steamPath, "steamapps", "libraryfolders.vdf");
        try
        {
            if (File.Exists(vdf))
                libs.AddRange(ParseLibraryPaths(File.ReadAllText(vdf)));
        }
        catch { }
        return libs.Select(p => { try { return Path.GetFullPath(p); } catch { return p; } })
                   .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    public static string? FindCs2Exe(string steamPath)
    {
        foreach (var lib in GetLibraryFolders(steamPath))
        {
            var manifest = Path.Combine(lib, "steamapps", $"appmanifest_{Catalog.Cs2AppId}.acf");
            if (!File.Exists(manifest)) continue;
            try
            {
                var dir = ParseInstallDir(File.ReadAllText(manifest));
                if (dir == null) continue;
                var exe = Path.Combine(lib, "steamapps", "common", dir, "game", "bin", "win64", "cs2.exe");
                if (File.Exists(exe)) return exe;
            }
            catch { }
        }
        return null;
    }

    /// <summary>The cs2.exe of the Steam library that has CS2 installed, or null.</summary>
    public static string? ResolveCs2()
    {
        var steam = FindSteamPath();
        return steam == null ? null : FindCs2Exe(steam);
    }
}
