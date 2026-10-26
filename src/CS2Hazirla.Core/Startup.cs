using System.Buffers.Binary;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32;

namespace CS2Hazirla.Core;

// =====================================================================================================================================================
// The Açılış engine: the programs that start by themselves when Windows starts, each with a switch.
//
// What a switch does: it moves Windows' OWN "start-up apps" flag (the one of Task Manager's Startup tab and of Settings > Apps > Startup).
// It never writes, renames or deletes a Run value, a file of a Startup folder, a scheduled task or a service. Nothing is started or stopped.
//
// THE FORMAT OF THAT FLAG, read (read-only) on a real Windows 11 Pro PC (build 10.0.26200):
//   ...\Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\{Run, Run32, StartupFolder}, once below HKCU and once below HKLM (64-bit view)
//   one REG_BINARY value per entry, named exactly like the Run value (trailing spaces included: "Some Tool ") or like the file ("Some Tool.lnk"),
//   12 bytes = a 4-byte little-endian flag + an 8-byte little-endian FILETIME.
//     enabled : 02 00 00 00  00 00 00 00 00 00 00 00                  (what Task Manager writes; Windows itself also writes 06 00 00 00 + zeros, seen under HKLM)
//     disabled: 03 00 00 00  <FILETIME of the moment it was switched off>
//   A missing value means enabled. The lowest bit of the first byte is the "disabled by the user" bit (02/06 = enabled, 03/07 = disabled).
//
// WHICH HIVE HOLDS THE FLAG OF AN ENTRY (what was seen, what was not):
//   * Entries of the user (the HKCU Run values, the user's Startup folder): the flag is below HKCU\...\StartupApproved\{Run, StartupFolder}.
//   * Entries of the machine (the HKLM Run values, the 32-bit HKLM Run values = "Run32", the common Startup folder): the flag is below
//     HKLM\...\StartupApproved\{Run, Run32, StartupFolder}. Seen on that PC: HKLM\...\StartupApproved\Run holds one flag for each HKLM Run value and the
//     HKCU\...\StartupApproved\Run key holds none of those names; HKLM\...\StartupApproved\StartupFolder holds a "03 00 00 00 + FILETIME" flag (switched off by a user)
//     for an item of the COMMON Startup folder, so an elevated Task Manager writes HKLM for the entries of the machine. Public descriptions of Windows say the same
//     (HKLM = machine context), and a tool author reports that an HKCU flag for an HKLM Run value had no effect after a restart.
//   * So: the flag of a machine entry is read from, and written to, HKLM only; an HKCU flag of the same name is never looked at and nothing is written to HKCU for it.
//     An HKCU value and an HKLM value with the same name therefore never share a flag.
//   * NOT verified: that Windows applies exactly this flag to a machine entry at the next start. That follows from what Task Manager writes on that PC, not from a
//     restart test of this program. Writing HKLM needs administrator rights (the release build always has them; a debug build may not, and then says so).
//   * Run32 is Windows' name for the 32-bit view (HKLM\Software\WOW6432Node\...\Run); that key was empty on that PC, so Run32 is from documentation, not from a real value.
//
// Rules this file keeps: only registry values are read and folders listed; no process is opened, started or stopped; nothing runs in the background;
// a failing probe leaves a fact out (null) instead of guessing; a normal failure comes back as a result, not as an exception (the one documented
// exception is StartupException from SetEnabled).
// =====================================================================================================================================================

public enum StartupSource { UserRun, MachineRun, MachineRun32, UserFolder, CommonFolder }

/// <summary>One program that starts with Windows.</summary>
/// <param name="Id">Stable and unique: the source and the value name or file name (identity, never translated).</param>
/// <param name="Name">The registry value name or the file name, exactly as Windows keeps it (this is also the name of its flag).</param>
/// <param name="DisplayName">The plain name for the screen (program names are not translated).</param>
/// <param name="Command">The raw command line (environment variables are not expanded).</param>
/// <param name="ExePath">The file that really starts (variables expanded, quotes and arguments stripped; for a script host such as wscript, cmd /c or rundll32 the script, program or DLL it is given), or null when it cannot be told.</param>
/// <param name="Enabled">True when Windows' start-up flag of this entry does not say "switched off" (no flag at all counts as on).</param>
/// <param name="Locked">True when this screen refuses to change it.</param>
/// <param name="LockedReason">"" when not locked, otherwise one short plain sentence.</param>
public sealed record StartupEntry(string Id, string Name, string DisplayName, string Command, string? ExePath, StartupSource Source, bool Enabled, bool Locked, string LockedReason);

/// <summary>The message is plain text in the language of the app and safe to show.</summary>
public sealed class StartupException : Exception
{
    public StartupException(string message) : base(message) { }
}

public interface IStartupStore
{
    /// <summary>Every program that starts with Windows, sorted by <see cref="StartupEntry.DisplayName"/>. Never throws for a normal failure: what cannot be read is left out.</summary>
    IReadOnlyList<StartupEntry> List();

    /// <summary>
    /// Switches an entry on or off and returns it as it is read back afterwards. Throws <see cref="StartupException"/> (nothing changed) when the game runs,
    /// the entry is locked or gone, the flag of a machine entry needs administrator rights that are missing, or the flag could not be written.
    /// </summary>
    StartupEntry SetEnabled(StartupEntry entry, bool enabled);
}

/// <summary>A registry key: the hive and the path below it.</summary>
public sealed record RegistryPlace(RegistryHive Hive, string SubKey);

/// <summary>
/// Every key and folder the store reads or writes, so that tests can point it at throw-away keys and folders.
/// The flag of an entry lives in the StartupApproved key of the hive the entry itself comes from (see the top of this file):
/// <see cref="ApprovedUser"/> for the entries of the user (UserRun, UserFolder), <see cref="ApprovedMachine"/> for the entries of the machine
/// (MachineRun, MachineRun32, CommonFolder). Below each of them sit the sub-keys Run, Run32 and StartupFolder.
/// These two are the only places that are ever written, and only the one that belongs to the entry that is switched.
/// </summary>
public sealed record StartupLocations(
    RegistryPlace UserRun,
    RegistryPlace MachineRun,
    RegistryPlace MachineRun32,
    RegistryPlace ApprovedUser,
    RegistryPlace ApprovedMachine,
    string UserFolder,
    string CommonFolder,
    string WindowsDirectory)
{
    /// <summary>The real places of this PC.</summary>
    public static StartupLocations Real { get; } = BuildReal();

    private static StartupLocations BuildReal()
    {
        const string run = @"Software\Microsoft\Windows\CurrentVersion\Run";
        const string approved = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved";
        return new StartupLocations(
            new RegistryPlace(RegistryHive.CurrentUser, run),
            new RegistryPlace(RegistryHive.LocalMachine, run),
            new RegistryPlace(RegistryHive.LocalMachine, @"Software\WOW6432Node\Microsoft\Windows\CurrentVersion\Run"),
            new RegistryPlace(RegistryHive.CurrentUser, approved),
            new RegistryPlace(RegistryHive.LocalMachine, approved),
            Environment.GetFolderPath(Environment.SpecialFolder.Startup),
            Environment.GetFolderPath(Environment.SpecialFolder.CommonStartup),
            Environment.GetFolderPath(Environment.SpecialFolder.Windows));
    }

    /// <summary>The StartupApproved place that holds the flag of an entry from <paramref name="source"/>: the machine place for machine entries, the user place for the rest.</summary>
    public RegistryPlace ApprovedFor(StartupSource source) => IsMachineSource(source) ? ApprovedMachine : ApprovedUser;

    /// <summary>True for the entries that belong to the whole PC (HKLM Run, HKLM 32-bit Run, the common Startup folder).</summary>
    internal static bool IsMachineSource(StartupSource source) =>
        source is StartupSource.MachineRun or StartupSource.MachineRun32 or StartupSource.CommonFolder;
}

/// <summary>The 12 bytes of Windows' flag (see the top of this file).</summary>
public static class StartupApproval
{
    public const int Length = 12;

    /// <summary>Disabled when the lowest bit of the first byte is set. A missing, short (under 4 bytes) or unknown value counts as enabled.</summary>
    public static bool IsDisabled(byte[]? data) => data != null && data.Length >= 4 && (data[0] & 1) != 0;

    /// <summary>03 00 00 00 + the time of switching off as a little-endian FILETIME, exactly what Task Manager writes.</summary>
    public static byte[] Disabled(DateTime now)
    {
        var bytes = new byte[Length];
        bytes[0] = 3;
        BinaryPrimitives.WriteInt64LittleEndian(bytes.AsSpan(4), now.ToFileTimeUtc());
        return bytes;
    }

    /// <summary>02 00 00 00 + eight zero bytes, exactly what Task Manager writes.</summary>
    public static byte[] Enabled() => new byte[] { 2, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 };
}

/// <summary>Reads a command line the way the Run keys and shortcuts write it. Pure text work: nothing is read from the disk.</summary>
public static class StartupCommand
{
    private static readonly string[] ProgramExtensions = { ".exe", ".com", ".bat", ".cmd", ".scr" };

    /// <summary>
    /// The program (quotes removed, variables NOT expanded) and the rest of the line. A path with spaces that is not in quotes
    /// (C:\Program Files\Foo\foo.exe /s) ends after its first .exe/.com/.bat/.cmd/.scr that is followed by a space or the end.
    /// </summary>
    public static (string Program, string Arguments) Split(string? command)
    {
        if (string.IsNullOrWhiteSpace(command)) return ("", "");
        var s = command.Trim('\0', ' ', '\t', '\r', '\n');
        if (s.Length == 0) return ("", "");
        if (s[0] == '"')
        {
            var close = s.IndexOf('"', 1);
            if (close >= 0) return (s[1..close].Trim(), s[(close + 1)..].Trim());
            s = s[1..].TrimStart();                                  // a quote that is never closed: read the rest as if it was not there
            if (s.Length == 0) return ("", "");
        }

        var white = IndexOfWhite(s);
        var first = white < 0 ? s : s[..white];
        if (white < 0) return (first, "");
        if (HasProgramExtension(first)) return (first, s[white..].Trim());
        if (first.IndexOf('\\') >= 0 || first.IndexOf(':') >= 0)      // the start of a path with spaces in it
        {
            var end = FindProgramEnd(s);
            if (end > 0) return (s[..end], s[end..].Trim());
        }
        return (first, s[white..].Trim());                            // a bare word: cmd, rundll32, notepad ...
    }

    /// <summary>
    /// The file that really starts, variables expanded, or null when it cannot be told (nothing is guessed). Script hosts are looked through, because the
    /// host is only the carrier: rundll32 gives its DLL, cmd /c its program or batch file, wscript/cscript its script, powershell -File its script.
    /// A bare name stays bare ("notepad.exe", "shell32.dll"); the store finds it in the Windows folders.
    /// </summary>
    public static string? ExeOf(string? command)
    {
        string? target;
        try { target = TargetOf(command, 0); }
        catch { return null; }
        if (string.IsNullOrWhiteSpace(target)) return null;
        var path = target.Trim().Trim('"').Trim();
        if (path.Length == 0) return null;
        try { path = Environment.ExpandEnvironmentVariables(path); }
        catch { return null; }
        if (path.IndexOf('%') >= 0) return null;                       // a variable that does not exist here: not guessed
        if (path.IndexOfAny(new[] { '<', '>', '|', '"', '*', '?', '\0' }) >= 0) return null;
        var ext = Path.GetExtension(path);
        if (ext.Length < 2 || ext.Length > 6) return null;             // a file has an extension; ".exe" ".bat" ".vbs" ".ps1" ".dll"
        for (var i = 1; i < ext.Length; i++) if (!char.IsLetterOrDigit(ext[i])) return null;
        return path;
    }

    // ---------------------------------------------------------------- the hosts that only carry another program

    private static string? TargetOf(string? command, int depth)
    {
        var (program, args) = Split(command);
        if (program.Length == 0) return null;
        switch (Path.GetFileNameWithoutExtension(program).ToLowerInvariant())
        {
            case "rundll32": return RunDllTarget(args);
            case "cmd": return depth >= 2 ? null : CmdTarget(args, depth);
            case "wscript":
            case "cscript": return FirstNonSwitch(args);
            case "powershell":
            case "pwsh": return PowerShellTarget(args);
            default: return program;
        }
    }

    /// <summary>rundll32 [switches] path\file.dll,EntryPoint arguments</summary>
    private static string? RunDllTarget(string args)
    {
        var token = FirstNonSwitch(args);
        if (token == null) return null;
        var comma = token.IndexOf(',');
        var dll = (comma >= 0 ? token[..comma] : token).Trim();
        return dll.Length > 0 ? dll : null;
    }

    private static string? FirstNonSwitch(string args)
    {
        foreach (var token in Tokens(args))
            if (token.Length > 0 && token[0] != '/') return token;
        return null;
    }

    private static string? PowerShellTarget(string args)
    {
        var tokens = Tokens(args);
        for (var i = 0; i < tokens.Count - 1; i++)
            if (tokens[i].Equals("-file", StringComparison.OrdinalIgnoreCase) || tokens[i].Equals("-f", StringComparison.OrdinalIgnoreCase))
                return tokens[i + 1];
        return null;                                                   // -Command "..." and the like: a script text, not a file
    }

    /// <summary>cmd [/d] /c [start ["title"] [/min] ...] program arguments</summary>
    private static string? CmdTarget(string args, int depth)
    {
        var rest = args.TrimStart();
        while (rest.Length > 0 && rest[0] == '/')
        {
            var end = IndexOfWhite(rest);
            var word = end < 0 ? rest : rest[..end];
            rest = end < 0 ? "" : rest[end..].TrimStart();
            if (word.Equals("/c", StringComparison.OrdinalIgnoreCase) || word.Equals("/k", StringComparison.OrdinalIgnoreCase) || word.Equals("/r", StringComparison.OrdinalIgnoreCase))
                return CmdCommand(rest, depth);
        }
        return null;
    }

    private static bool IsStartCommand(string s) =>
        s.Equals("start", StringComparison.OrdinalIgnoreCase) || s.StartsWith("start ", StringComparison.OrdinalIgnoreCase) || s.StartsWith("start\t", StringComparison.OrdinalIgnoreCase);

    private static string? CmdCommand(string s, int depth)
    {
        s = s.Trim();
        if (s.Length > 2 && s[0] == '"' && s[1] == '"' && s[^1] == '"') s = s[1..^1];     // cmd /c ""C:\a b\c.bat" arg": the outer pair is cmd's own
        if (s.Length > 1 && s[0] == '"' && s[^1] == '"')                                   // cmd /c "start /min C:\x\y.exe": a pair around the WHOLE start command is cmd's own too (unwrapped once)
        {
            var inner = s[1..^1].Trim();
            if (IsStartCommand(inner)) s = inner;
        }
        if (IsStartCommand(s))
        {
            s = s[5..].TrimStart();
            s = SkipStartSwitches(s);
            if (s.Length > 0 && s[0] == '"')                                                // the first quoted text of start is the window title, not the program
            {
                var close = s.IndexOf('"', 1);
                s = close < 0 ? "" : s[(close + 1)..].TrimStart();
                s = SkipStartSwitches(s);
            }
        }
        return s.Length == 0 ? null : TargetOf(s, depth + 1);
    }

    private static string SkipStartSwitches(string s)
    {
        while (s.Length > 0 && s[0] == '/')
        {
            var end = IndexOfWhite(s);
            var word = end < 0 ? s : s[..end];
            s = end < 0 ? "" : s[end..].TrimStart();
            if (word.Equals("/d", StringComparison.OrdinalIgnoreCase) && s.Length > 0)      // /d takes the folder to start in
            {
                if (s[0] == '"') { var close = s.IndexOf('"', 1); s = close < 0 ? "" : s[(close + 1)..].TrimStart(); }
                else { var e2 = IndexOfWhite(s); s = e2 < 0 ? "" : s[e2..].TrimStart(); }
            }
        }
        return s;
    }

    // ---------------------------------------------------------------- small text helpers

    /// <summary>Words separated by white space; "quoted text" is one word and loses its quotes (also in the middle of a word: "a b",c).</summary>
    private static List<string> Tokens(string s)
    {
        var result = new List<string>();
        var sb = new StringBuilder();
        var inQuote = false;
        var started = false;
        foreach (var ch in s)
        {
            if (ch == '"') { inQuote = !inQuote; started = true; continue; }
            if (!inQuote && char.IsWhiteSpace(ch))
            {
                if (started) { result.Add(sb.ToString()); sb.Clear(); started = false; }
                continue;
            }
            sb.Append(ch);
            started = true;
        }
        if (started) result.Add(sb.ToString());
        return result;
    }

    private static int IndexOfWhite(string s)
    {
        for (var i = 0; i < s.Length; i++) if (char.IsWhiteSpace(s[i])) return i;
        return -1;
    }

    private static bool HasProgramExtension(string token)
    {
        foreach (var ext in ProgramExtensions) if (token.EndsWith(ext, StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }

    /// <summary>The end of the first .exe/.com/.bat/.cmd/.scr that is followed by white space or the end of the text (-1 when there is none).</summary>
    private static int FindProgramEnd(string s)
    {
        var best = -1;
        foreach (var ext in ProgramExtensions)
        {
            var from = 0;
            while (from < s.Length)
            {
                var i = s.IndexOf(ext, from, StringComparison.OrdinalIgnoreCase);
                if (i < 0) break;
                var end = i + ext.Length;
                if (end == s.Length || char.IsWhiteSpace(s[end]))
                {
                    if (best < 0 || end < best) best = end;
                    break;
                }
                from = i + 1;
            }
        }
        return best;
    }
}

/// <summary>Which entries this screen refuses to change, and why. Reads at most the version resource of one file; never touches a process.</summary>
public static class StartupGuard
{
    // the game software of the protected list: it is protected from being CLOSED, but whether Steam or the FACEIT client starts with Windows is the player's choice
    private static readonly string[] GamePrefixes = { "steam", "gameoverlay", "cs2", "csgo", "valve", "hl2", "srcds", "faceit" };

    /// <summary>
    /// The plain reason an entry whose file is <paramref name="exePath"/> cannot be changed here, or null when it can.
    /// Locked: Antilag itself, security programs, the entries of the protected list (Windows and the graphics/audio driver parts), and files in the Windows folder
    /// unless the file says it is made by someone else than Microsoft (a vendor's helper that sits in System32).
    /// The protected list matches by the beginning of a file name and can match more than it was meant for, and a file in the Windows folder is only "probably Windows' own",
    /// so for those two the reason says "may be needed" and never states what the program is.
    /// </summary>
    /// <param name="companyOf">Test hook: the company name a file carries (null = unknown). The default reads the file's version information.</param>
    public static string? LockReason(string? exePath, string? windowsDirectory, Func<string, string?>? companyOf = null)
    {
        var n = NameOf(exePath);
        if (n == null) return null;

        if (n is "antilag" or "cs2hazirla" || SameFile(exePath, Environment.ProcessPath))
            return Loc.T("Antilag'ın kendisi; buradan değiştirilemez.");

        foreach (var c in Catalog.ProcessCategories)
            if (c.OptionId == "proc.antivirus" && c.Matches(n))
                return Loc.T("Güvenlik programı; buradan değiştirilemez.");

        var windowsPart = (ProtectedList.IsProcessProtected(n, null, false) && !IsGameSoftware(n))
                          || (UnderFolder(exePath, windowsDirectory) && !IsMadeByOthers(exePath!, companyOf));
        return windowsPart ? Loc.T("Bilgisayarın çalışması için gerekli olabilir; buradan değiştirilemez.") : null;
    }

    /// <summary>
    /// False for a file whose name is on the protected list (or that lives in a protected folder such as Steam's): nothing about it is read, not even its
    /// version information. Also false for a file that is not on a fixed disk of this PC (a network share or a drive that may be asleep or gone could make
    /// the list wait for seconds). True for every other file.
    /// </summary>
    public static bool MayReadFile(string? exePath)
    {
        var n = NameOf(exePath);
        if (n == null) return false;
        if (ProtectedList.IsProcessProtected(n, null, false)) return false;
        foreach (var root in ProtectedList.ProtectedRoots)
            if (UnderFolder(exePath, root)) return false;
        return OnFixedDisk(exePath!);
    }

    private static bool OnFixedDisk(string path)
    {
        try
        {
            var root = Path.GetPathRoot(path.Trim());
            if (string.IsNullOrEmpty(root) || root.StartsWith(@"\\", StringComparison.Ordinal)) return false;      // no drive letter, or a network path
            return new DriveInfo(root).DriveType == DriveType.Fixed;
        }
        catch { return false; }
    }

    private static string? NameOf(string? exePath)
    {
        if (string.IsNullOrWhiteSpace(exePath)) return null;
        try
        {
            var n = ProtectedList.Normalize(Path.GetFileNameWithoutExtension(exePath.Trim()));
            return n.Length == 0 ? null : n;
        }
        catch { return null; }
    }

    private static bool IsGameSoftware(string normalizedName)
    {
        foreach (var p in GamePrefixes) if (normalizedName.StartsWith(p, StringComparison.Ordinal)) return true;
        return false;
    }

    private static bool SameFile(string? a, string? b) =>
        !string.IsNullOrWhiteSpace(a) && !string.IsNullOrWhiteSpace(b) && string.Equals(a.Trim(), b.Trim(), StringComparison.OrdinalIgnoreCase);

    private static bool UnderFolder(string? path, string? folder)
    {
        if (string.IsNullOrWhiteSpace(path) || string.IsNullOrWhiteSpace(folder)) return false;
        return path.Trim().StartsWith(folder.Trim().TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>A company is named and it is not Microsoft. An unreadable or unnamed file counts as Windows' own.</summary>
    private static bool IsMadeByOthers(string path, Func<string, string?>? companyOf)
    {
        string? company;
        try { company = companyOf != null ? companyOf(path) : FileVersionInfo.GetVersionInfo(path).CompanyName; }
        catch { company = null; }
        return !string.IsNullOrWhiteSpace(company) && company.IndexOf("Microsoft", StringComparison.OrdinalIgnoreCase) < 0;
    }
}

/// <summary>Plain one-line texts about the list.</summary>
public static class StartupSummary
{
    /// <summary>"3 programs open with Windows; 2 more are switched off." Counts the entries by <see cref="StartupEntry.Enabled"/>; never throws.</summary>
    public static string Line(IReadOnlyList<StartupEntry> all)
    {
        var enabled = 0;
        var off = 0;
        if (all != null)
            foreach (var e in all)
            {
                if (e == null) continue;
                if (e.Enabled) enabled++; else off++;
            }
        if (enabled == 0 && off == 0) return Loc.T("Windows'la birlikte açılan program yok.");
        if (enabled == 0) return Loc.F("Hiçbir program Windows'la birlikte açılmıyor; {0} program kapalı.", off);
        if (off == 0) return Loc.F("{0} program Windows'la birlikte açılıyor.", enabled);
        return Loc.F("{0} program Windows'la birlikte açılıyor; {1} program daha kapalı.", enabled, off);
    }

    /// <summary>A short note on where an entry comes from (for a small grey line under its name).</summary>
    public static string SourceNote(StartupSource source) => source switch
    {
        StartupSource.UserRun => Loc.T("Senin hesabın için"),
        StartupSource.MachineRun or StartupSource.MachineRun32 => Loc.T("Bu bilgisayardaki herkes için"),
        StartupSource.UserFolder => Loc.T("Senin Başlangıç klasörün"),
        StartupSource.CommonFolder => Loc.T("Herkesin Başlangıç klasörü"),
        _ => "",
    };
}

/// <summary>
/// Reads where a .lnk shortcut points (IShellLinkW). The shortcut file is only read: it is not resolved (no search for a moved target), not changed, and the program it points to is not touched.
/// </summary>
internal static class ShortcutFile
{
    private const uint SLGP_RAWPATH = 0x4;

    /// <summary>The target as the shortcut stores it (variables not expanded) and its arguments; null when the file cannot be read; an empty Target for a shortcut that points to something that is not a file (a Store app).</summary>
    public static (string Target, string Arguments)? Read(string lnkPath)
    {
        object? obj = null;
        try
        {
            obj = new ShellLinkObject();
            ((System.Runtime.InteropServices.ComTypes.IPersistFile)obj).Load(lnkPath, 0);        // STGM_READ
            var link = (IShellLinkW)obj;
            var target = new StringBuilder(2048);
            link.GetPath(target, target.Capacity, IntPtr.Zero, SLGP_RAWPATH);
            var arguments = new StringBuilder(4096);
            link.GetArguments(arguments, arguments.Capacity);
            return (target.ToString().Trim(), arguments.ToString().Trim());
        }
        catch { return null; }
        finally
        {
            try { if (obj != null && Marshal.IsComObject(obj)) Marshal.FinalReleaseComObject(obj); } catch { }
        }
    }

    [ComImport, Guid("00021401-0000-0000-C000-000000000046")]
    internal class ShellLinkObject { }

    // the methods must stay in this order: it is the order of the COM table
    [ComImport, Guid("000214F9-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IShellLinkW
    {
        void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszFile, int cch, IntPtr pfd, uint fFlags);
        void GetIDList(out IntPtr ppidl);
        void SetIDList(IntPtr pidl);
        void GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszName, int cch);
        void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string pszName);
        void GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszDir, int cch);
        void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string pszDir);
        void GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszArgs, int cch);
        void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string pszArgs);
        void GetHotkey(out short pwHotkey);
        void SetHotkey(short wHotkey);
        void GetShowCmd(out int piShowCmd);
        void SetShowCmd(int iShowCmd);
        void GetIconLocation([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszIconPath, int cch, out int piIcon);
        void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string pszIconPath, int iIcon);
        void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string pszPathRel, uint dwReserved);
        void Resolve(IntPtr hwnd, uint fFlags);
        void SetPath([MarshalAs(UnmanagedType.LPWStr)] string pszFile);
    }
}

/// <summary>
/// The store of the real PC (or of the throw-away keys and folders of a test). Reading: the three Run keys and the two Startup folders, and the
/// flag of each entry from the StartupApproved key of its own hive. Writing: only that flag, below <see cref="StartupLocations.ApprovedUser"/> for the
/// entries of the user and below <see cref="StartupLocations.ApprovedMachine"/> for the entries of the machine. A call takes a few tens of milliseconds
/// (a version lookup per program the first time); run it off the window's thread. Every call is independent: nothing is kept between calls.
/// </summary>
public sealed class WindowsStartupStore : IStartupStore
{
    private static readonly HashSet<string> FolderExtensions = new(StringComparer.OrdinalIgnoreCase) { ".lnk", ".exe", ".bat", ".cmd" };

    private readonly StartupLocations _loc;

    public WindowsStartupStore() : this(StartupLocations.Real) { }

    public WindowsStartupStore(StartupLocations locations)
    {
        _loc = locations ?? throw new ArgumentNullException(nameof(locations));
    }

    /// <summary>
    /// Optional: when set and it says true, List() reads nothing and returns an empty list, and SetEnabled() changes nothing and throws a
    /// <see cref="StartupException"/> (nothing of the app looks at or changes the machine while the game runs). SetEnabled asks again right before it writes,
    /// because the game may have started while the list was read. The screen should still check the game itself, to say why the list is empty.
    /// </summary>
    public Func<bool>? GameRunning { get; init; }

    /// <summary>
    /// Test hook: replaces the one registry write of this class. Arguments: the StartupApproved place, its sub-key (Run, Run32 or StartupFolder), the value name and the 12 bytes.
    /// The default writes the value for real. A test uses it to make the write fail the way a missing right does, without ever touching a protected key.
    /// </summary>
    public Action<RegistryPlace, string, string, byte[]>? FlagWriter { get; init; }

    private bool GameIsRunning() => GameRunning?.Invoke() == true;

    private static StartupException GameException() => new(Loc.T("Oyun açıkken değişiklik yapılmaz; oyunu kapatınca tekrar dene."));

    private readonly record struct Raw(string Name, string Command, StartupSource Source, string? FilePath);

    // ------------------------------------------------------------------------------------------------------------------------------ List

    public IReadOnlyList<StartupEntry> List()
    {
        if (GameIsRunning()) return Array.Empty<StartupEntry>();

        var raw = new List<Raw>();
        ReadRun(_loc.UserRun, StartupSource.UserRun, raw);
        ReadRun(_loc.MachineRun, StartupSource.MachineRun, raw);
        ReadRun(_loc.MachineRun32, StartupSource.MachineRun32, raw);
        ReadFolder(_loc.UserFolder, StartupSource.UserFolder, raw);
        ReadFolder(_loc.CommonFolder, StartupSource.CommonFolder, raw);

        // one flag key per kind of entry, each read from the hive of its own entries: the flag of a machine entry is never taken from HKCU
        var flags = new Dictionary<StartupSource, Dictionary<string, byte[]>>();
        foreach (var source in Enum.GetValues<StartupSource>()) flags[source] = ReadFlags(_loc.ApprovedFor(source), FlagLeaf(source));

        var entries = new List<StartupEntry>(raw.Count);
        foreach (var r in raw)
        {
            try { entries.Add(Build(r, flags)); }
            catch { /* an entry that cannot be described is left out */ }
        }
        // OrderBy is stable: equal names keep the order they were read in (sources in a fixed order, names in a fixed order)
        return entries.OrderBy(e => e.DisplayName, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static RegistryKey? OpenForRead(RegistryPlace place, string? leaf = null)
    {
        try
        {
            using var root = RegistryKey.OpenBaseKey(place.Hive, RegistryView.Registry64);
            var path = leaf == null ? place.SubKey : place.SubKey.TrimEnd('\\') + "\\" + leaf;
            return root.OpenSubKey(path, false);
        }
        catch { return null; }
    }

    /// <summary>Which sub-key of StartupApproved holds the flag of this kind of entry (the hive comes from <see cref="StartupLocations.ApprovedFor"/>).</summary>
    private static string FlagLeaf(StartupSource source) => source switch
    {
        StartupSource.MachineRun32 => "Run32",
        StartupSource.UserFolder or StartupSource.CommonFolder => "StartupFolder",
        _ => "Run",                                    // the Run values of the user and of the machine: the same sub-key name, in two different hives
    };

    private static void ReadRun(RegistryPlace place, StartupSource source, List<Raw> into)
    {
        using var key = OpenForRead(place);
        if (key == null) return;
        string[] names;
        try { names = key.GetValueNames(); }
        catch { return; }
        Array.Sort(names, StringComparer.OrdinalIgnoreCase);
        foreach (var name in names)
        {
            if (name.Length == 0) continue;            // the "(Default)" value is not a start-up entry
            try
            {
                var kind = key.GetValueKind(name);
                if (kind != RegistryValueKind.String && kind != RegistryValueKind.ExpandString) continue;
                if (key.GetValue(name, null, RegistryValueOptions.DoNotExpandEnvironmentNames) is not string text) continue;
                text = text.Trim('\0', ' ', '\t', '\r', '\n');
                if (text.Length == 0) continue;
                into.Add(new Raw(name, text, source, null));
            }
            catch { /* an unreadable value is skipped */ }
        }
    }

    private static void ReadFolder(string folder, StartupSource source, List<Raw> into)
    {
        if (string.IsNullOrWhiteSpace(folder)) return;
        FileInfo[] files;
        try
        {
            var dir = new DirectoryInfo(folder);
            if (!dir.Exists) return;
            files = dir.GetFiles();                    // files only: a sub-folder is not started by Windows
        }
        catch { return; }
        Array.Sort(files, (a, b) => StringComparer.OrdinalIgnoreCase.Compare(a.Name, b.Name));
        foreach (var f in files)
        {
            try
            {
                if ((f.Attributes & (FileAttributes.Hidden | FileAttributes.System)) != 0) continue;     // desktop.ini and anything hidden
                if (!FolderExtensions.Contains(f.Extension)) continue;
                into.Add(new Raw(f.Name, "", source, f.FullName));
            }
            catch { /* skipped */ }
        }
    }

    /// <summary>The flags of one StartupApproved sub-key of one hive: value name (case-insensitive) → bytes. A value that is not binary is left out (= enabled).</summary>
    private static Dictionary<string, byte[]> ReadFlags(RegistryPlace approved, string leaf)
    {
        var map = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
        using var key = OpenForRead(approved, leaf);
        if (key == null) return map;
        try
        {
            foreach (var name in key.GetValueNames())
            {
                try
                {
                    if (key.GetValueKind(name) == RegistryValueKind.Binary && key.GetValue(name) is byte[] data) map[name] = data;
                }
                catch { /* unreadable = enabled */ }
            }
        }
        catch { }
        return map;
    }

    private StartupEntry Build(Raw r, Dictionary<StartupSource, Dictionary<string, byte[]>> flags)
    {
        var (command, fileKnown) = r.FilePath != null ? CommandOfFile(r.FilePath) : (r.Command, true);
        var exe = fileKnown ? ResolveBareName(StartupCommand.ExeOf(command)) : null;

        var enabled = true;      // the flag of the hive of THIS entry; no flag = on
        if (flags.TryGetValue(r.Source, out var map) && map.TryGetValue(r.Name, out var data)) enabled = !StartupApproval.IsDisabled(data);

        var reason = StartupGuard.LockReason(exe, _loc.WindowsDirectory) ?? "";
        var plainName = r.FilePath != null ? Path.GetFileNameWithoutExtension(r.Name) : r.Name.Trim();

        return new StartupEntry(
            Id: r.Source + "|" + r.Name,
            Name: r.Name,
            DisplayName: DisplayNameFor(plainName, exe),
            Command: command,
            ExePath: exe,
            Source: r.Source,
            Enabled: enabled,
            Locked: reason.Length > 0,
            LockedReason: reason);
    }

    /// <summary>
    /// The command line of a file in a Startup folder: a shortcut gives its target and arguments, any other file is started as it is.
    /// FileKnown is false for a shortcut that cannot be resolved (damaged, or it points to a Store app): the shortcut file itself is then all that is known, and it has no ExePath.
    /// </summary>
    private static (string Command, bool FileKnown) CommandOfFile(string path)
    {
        if (!string.Equals(Path.GetExtension(path), ".lnk", StringComparison.OrdinalIgnoreCase)) return (Quote(path), true);
        var link = ShortcutFile.Read(path);
        if (link == null || link.Value.Target.Length == 0) return (Quote(path), false);
        var target = Quote(link.Value.Target);
        return (link.Value.Arguments.Length == 0 ? target : target + " " + link.Value.Arguments, true);
    }

    private static string Quote(string path)
    {
        foreach (var ch in path) if (char.IsWhiteSpace(ch)) return "\"" + path + "\"";
        return path;
    }

    /// <summary>"notepad.exe" or "shell32.dll" without a folder: Windows finds it in its own folders. Not found = unknown (null).</summary>
    private string? ResolveBareName(string? exe)
    {
        if (exe == null) return null;
        if (exe.IndexOfAny(new[] { '\\', '/' }) >= 0 || Path.IsPathRooted(exe)) return exe;
        if (string.IsNullOrWhiteSpace(_loc.WindowsDirectory)) return null;
        foreach (var dir in new[] { Path.Combine(_loc.WindowsDirectory, "System32"), _loc.WindowsDirectory })
        {
            try
            {
                var candidate = Path.Combine(dir, exe);
                if (File.Exists(candidate)) return candidate;
            }
            catch { }
        }
        return null;
    }

    /// <summary>
    /// What the file says about itself (FriendlyNames); when it says nothing better than its own file name, the name the program registered itself under.
    /// Files of the protected list are never opened, not even for their version information: their name comes from the registered name.
    /// </summary>
    private static string DisplayNameFor(string plainName, string? exe)
    {
        string? exeName = null;
        if (!string.IsNullOrWhiteSpace(exe))
        {
            try { exeName = ProtectedList.Normalize(Path.GetFileNameWithoutExtension(exe)); } catch { }
            if (exeName is { Length: 0 }) exeName = null;
        }

        if (exeName != null)
        {
            var friendly = FriendlyNames.ForProcess(exeName, StartupGuard.MayReadFile(exe) ? exe : null);
            if (friendly.Length > 0 && !string.Equals(friendly, Pretty(exeName), StringComparison.Ordinal)) return friendly;
        }

        var plain = plainName.Trim();
        if (plain.Length > 0 && plain[0] != '{') return plain;                        // a GUID is no name
        if (exeName != null) return Pretty(exeName);
        return plain.Length > 0 ? plain : plainName;
    }

    private static string Pretty(string name) => name.Length == 0 ? name : char.ToUpperInvariant(name[0]) + name[1..];

    // ------------------------------------------------------------------------------------------------------------------------------ SetEnabled

    public StartupEntry SetEnabled(StartupEntry entry, bool enabled)
    {
        ArgumentNullException.ThrowIfNull(entry);
        if (GameIsRunning()) throw GameException();
        if (entry.Locked) throw new StartupException(Loc.T("Bu program korumalı; buradan değiştirilemez."));

        // What is checked is what the machine says now, never what the caller says: a stale or edited entry cannot unlock anything.
        var current = List().FirstOrDefault(e => string.Equals(e.Id, entry.Id, StringComparison.Ordinal));
        if (current == null)
        {
            if (GameIsRunning()) throw GameException();                            // the list is empty because the game has just started, not because the entry is gone
            throw new StartupException(Loc.T("Bu program artık Windows'la birlikte açılanlar arasında değil."));
        }
        if (current.Locked) throw new StartupException(Loc.T("Bu program korumalı; buradan değiştirilemez."));

        if (current.Enabled != enabled)                                            // already as asked: nothing is written
        {
            if (GameIsRunning()) throw GameException();                            // the game may have started while the list was read: look once more, right before the write
            WriteFlag(current, enabled);
        }

        return List().FirstOrDefault(e => string.Equals(e.Id, entry.Id, StringComparison.Ordinal)) ?? current with { Enabled = enabled };
    }

    /// <summary>
    /// The ONLY write of this class: one REG_BINARY value below the StartupApproved key of the entry's own hive (<see cref="StartupLocations.ApprovedFor"/>).
    /// For a machine entry that is HKLM, which needs administrator rights: without them the registry refuses and the message says so (nothing changed).
    /// </summary>
    private void WriteFlag(StartupEntry entry, bool enabled)
    {
        var place = _loc.ApprovedFor(entry.Source);
        var leaf = FlagLeaf(entry.Source);
        var bytes = enabled ? StartupApproval.Enabled() : StartupApproval.Disabled(DateTime.UtcNow);
        try
        {
            if (FlagWriter != null) FlagWriter(place, leaf, entry.Name, bytes);
            else WriteToRegistry(place, leaf, entry.Name, bytes);
        }
        catch (Exception ex) when ((ex is UnauthorizedAccessException || ex is System.Security.SecurityException) && StartupLocations.IsMachineSource(entry.Source))
        {
            throw new StartupException(Loc.T("Windows bu değişikliğe izin vermedi (yönetici izni gerekebilir); hiçbir şey değişmedi."));
        }
        catch (Exception)
        {
            throw new StartupException(Loc.T("Ayar kaydedilemedi; hiçbir şey değişmedi."));
        }
    }

    private static void WriteToRegistry(RegistryPlace place, string leaf, string name, byte[] bytes)
    {
        using var root = RegistryKey.OpenBaseKey(place.Hive, RegistryView.Registry64);
        using var key = root.CreateSubKey(place.SubKey.TrimEnd('\\') + "\\" + leaf, true)
                        ?? throw new IOException("StartupApproved could not be opened");
        key.SetValue(name, bytes, RegistryValueKind.Binary);
    }
}
