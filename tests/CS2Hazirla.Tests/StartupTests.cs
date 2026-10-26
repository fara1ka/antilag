using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using CS2Hazirla.Core;
using Microsoft.Win32;
using Xunit;
using Xunit.Abstractions;

namespace CS2Hazirla.Tests;

/// <summary>
/// Throw-away places for the startup store: HKCU\Software\CS2HazirlaTest\startup-&lt;guid&gt;\... instead of the real Run and StartupApproved keys
/// (the "machine" Run keys and the "machine" StartupApproved place live under HKCU here too: a test never reads or writes a real HKLM key, and none of the
/// places below is a real Run or StartupApproved key) and temp folders instead of the real Startup folders.
/// The Windows folder is a fake one inside the temp folder, so nothing of the real machine can make an entry look protected.
/// </summary>
internal sealed class StartupSandbox : IDisposable
{
    public string RootKey { get; } = @"Software\CS2HazirlaTest\startup-" + Guid.NewGuid().ToString("N");
    public string Dir { get; } = Path.Combine(Path.GetTempPath(), "cs2h-startup-" + Guid.NewGuid().ToString("N"));

    public string UserFolder => Path.Combine(Dir, "user-startup");
    public string CommonFolder => Path.Combine(Dir, "common-startup");
    public string WindowsDir => Path.Combine(Dir, "Windows");
    public string Apps => Path.Combine(Dir, "Apps");

    public RegistryPlace UserRun => new(RegistryHive.CurrentUser, RootKey + @"\UserRun");
    public RegistryPlace MachineRun => new(RegistryHive.CurrentUser, RootKey + @"\MachineRun");
    public RegistryPlace MachineRun32 => new(RegistryHive.CurrentUser, RootKey + @"\MachineRun32");
    public RegistryPlace ApprovedUser => new(RegistryHive.CurrentUser, RootKey + @"\StartupApprovedUser");           // stands for HKCU\...\StartupApproved
    public RegistryPlace ApprovedMachine => new(RegistryHive.CurrentUser, RootKey + @"\StartupApprovedMachine");     // stands for HKLM\...\StartupApproved

    public StartupLocations Locations => new(UserRun, MachineRun, MachineRun32, ApprovedUser, ApprovedMachine, UserFolder, CommonFolder, WindowsDir);
    public WindowsStartupStore Store() => new(Locations);

    public StartupSandbox()
    {
        Directory.CreateDirectory(UserFolder);
        Directory.CreateDirectory(CommonFolder);
        Directory.CreateDirectory(Path.Combine(WindowsDir, "System32"));
        Directory.CreateDirectory(Apps);
    }

    public string Exe(string fileName) => Path.Combine(Apps, fileName);
    public static string Quoted(string path) => "\"" + path + "\"";

    private static RegistryKey Hkcu() => RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, RegistryView.Registry64);

    public void Put(RegistryPlace place, string name, object value, RegistryValueKind kind = RegistryValueKind.String)
    {
        using var root = Hkcu();
        using var key = root.CreateSubKey(place.SubKey, true)!;
        key.SetValue(name, value, kind);
    }

    public void Remove(RegistryPlace place, string name)
    {
        using var root = Hkcu();
        using var key = root.OpenSubKey(place.SubKey, true);
        key?.DeleteValue(name, false);
    }

    /// <summary>Deletes a whole throw-away key of the sandbox with everything below it.</summary>
    public void RemoveKey(RegistryPlace place)
    {
        using var root = Hkcu();
        root.DeleteSubKeyTree(place.SubKey, false);
    }

    /// <summary>Puts a flag the way Windows would have: below <paramref name="approved"/>\leaf (the user place or the machine place of the sandbox).</summary>
    public void Flag(RegistryPlace approved, string leaf, string name, byte[] data) =>
        Put(new RegistryPlace(RegistryHive.CurrentUser, approved.SubKey + "\\" + leaf), name, data, RegistryValueKind.Binary);

    public byte[]? ReadFlag(RegistryPlace approved, string leaf, string name)
    {
        using var root = Hkcu();
        using var key = root.OpenSubKey(approved.SubKey + "\\" + leaf);
        return key?.GetValue(name) as byte[];
    }

    public bool Exists(RegistryPlace place)
    {
        using var root = Hkcu();
        using var key = root.OpenSubKey(place.SubKey);
        return key != null;
    }

    /// <summary>True when anything was created below either StartupApproved place.</summary>
    public bool ApprovedExists() => Exists(ApprovedUser) || Exists(ApprovedMachine);

    /// <summary>Every value of a key as text, with its type: to prove that nothing changed byte for byte.</summary>
    public List<string> Snapshot(RegistryPlace place)
    {
        var lines = new List<string>();
        using var root = Hkcu();
        using var key = root.OpenSubKey(place.SubKey);
        if (key == null) return new List<string> { "<no key>" };
        foreach (var n in key.GetValueNames().OrderBy(x => x, StringComparer.Ordinal))
        {
            var kind = key.GetValueKind(n);
            var v = key.GetValue(n, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
            var text = v is byte[] b ? Convert.ToHexString(b) : v is string[] m ? string.Join("\u0001", m) : v?.ToString();
            lines.Add($"{n}|{kind}|{text}");
        }
        foreach (var sub in key.GetSubKeyNames().OrderBy(x => x, StringComparer.Ordinal)) lines.Add("subkey:" + sub);
        return lines;
    }

    /// <summary>Every file and folder of a directory with attributes, content and time of last write.</summary>
    public static List<string> FolderSnapshot(string folder) =>
        Directory.GetFileSystemEntries(folder).OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
            .Select(p => $"{Path.GetFileName(p)}|{File.GetAttributes(p)}|{(File.Exists(p) ? Convert.ToHexString(File.ReadAllBytes(p)) : "<dir>")}|{File.GetLastWriteTimeUtc(p).Ticks}")
            .ToList();

    /// <summary>Makes a real .lnk with WScript.Shell, so that the store reads a shortcut that Windows itself wrote.</summary>
    public static void MakeShortcut(string lnkPath, string target, string arguments)
    {
        var type = Type.GetTypeFromProgID("WScript.Shell") ?? throw new InvalidOperationException("WScript.Shell is not available on this PC");
        object shellObject = Activator.CreateInstance(type)!;
        try
        {
            dynamic shell = shellObject;
            dynamic link = shell.CreateShortcut(lnkPath);
            link.TargetPath = target;
            link.Arguments = arguments;
            link.Save();
        }
        finally { Marshal.FinalReleaseComObject(shellObject); }
    }

    public void Dispose()
    {
        try { using var root = Hkcu(); root.DeleteSubKeyTree(RootKey, false); } catch { }
        try { Directory.Delete(Dir, true); } catch { }
    }
}

/// <summary>The Açılış engine against throw-away keys and folders. Nothing here reads or writes a real Run or StartupApproved key.</summary>
public class StartupTests : IDisposable
{
    private readonly StartupSandbox _sb = new();
    public void Dispose() => _sb.Dispose();

    private static byte[] Hex(string hex) => hex.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(h => Convert.ToByte(h, 16)).ToArray();
    private static readonly byte[] EnabledBytes = { 2, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 };
    private static string LeafOf(StartupSource s) => s switch
    {
        StartupSource.MachineRun32 => "Run32",
        StartupSource.UserFolder or StartupSource.CommonFolder => "StartupFolder",
        _ => "Run",
    };

    // Written out here on purpose (not taken from the engine), so that the test and the engine cannot be wrong in the same way.
    private static bool IsMachine(StartupSource s) => s is StartupSource.MachineRun or StartupSource.MachineRun32 or StartupSource.CommonFolder;
    private RegistryPlace ApprovedOf(StartupSource s) => IsMachine(s) ? _sb.ApprovedMachine : _sb.ApprovedUser;
    private RegistryPlace OtherApprovedOf(StartupSource s) => IsMachine(s) ? _sb.ApprovedUser : _sb.ApprovedMachine;

    // An example of the flag that Task Manager writes for "switched off": 03 00 00 00 + the FILETIME of 2026-03-14T09:26:53Z (made up for these tests).
    private const string OffHex = "03 00 00 00 80 EC 92 B1 94 B3 DC 01";

    // ------------------------------------------------------------------------------------------------ the list

    [Fact]
    public void The_list_reads_the_three_run_keys_and_both_folders_sorted_by_display_name()
    {
        _sb.Put(_sb.UserRun, "zeta", StartupSandbox.Quoted(_sb.Exe("zeta.exe")) + " -x");
        _sb.Put(_sb.UserRun, "Alpha", _sb.Exe("alpha.exe"));
        _sb.Put(_sb.MachineRun, "bravo", @"%ProgramFiles%\Bravo\bravo.exe", RegistryValueKind.ExpandString);
        _sb.Put(_sb.MachineRun32, "Charlie", StartupSandbox.Quoted(_sb.Exe("charlie.exe")));
        File.WriteAllText(Path.Combine(_sb.UserFolder, "Delta.cmd"), "@echo off");
        File.WriteAllText(Path.Combine(_sb.CommonFolder, "echo-tool.bat"), "@echo off");

        var list = _sb.Store().List();

        Assert.Equal(new[] { "Alpha", "bravo", "Charlie", "Delta.cmd", "echo-tool.bat", "zeta" }, list.Select(e => e.Name).ToArray());
        Assert.Equal(new[] { "Alpha", "bravo", "Charlie", "Delta", "echo-tool", "zeta" }, list.Select(e => e.DisplayName).ToArray());
        Assert.Equal(new[] { StartupSource.UserRun, StartupSource.MachineRun, StartupSource.MachineRun32, StartupSource.UserFolder, StartupSource.CommonFolder, StartupSource.UserRun },
            list.Select(e => e.Source).ToArray());
        Assert.All(list, e => { Assert.True(e.Enabled); Assert.False(e.Locked); Assert.Equal("", e.LockedReason); });

        var zeta = list.Single(e => e.Name == "zeta");
        Assert.Equal(StartupSandbox.Quoted(_sb.Exe("zeta.exe")) + " -x", zeta.Command);          // the raw line
        Assert.Equal(_sb.Exe("zeta.exe"), zeta.ExePath);                                          // quotes and arguments stripped

        var bravo = list.Single(e => e.Name == "bravo");
        Assert.Equal(@"%ProgramFiles%\Bravo\bravo.exe", bravo.Command);                           // variables stay in the raw line ...
        Assert.Equal(Path.Combine(Environment.GetEnvironmentVariable("ProgramFiles")!, @"Bravo\bravo.exe"), bravo.ExePath);   // ... and are expanded in the path

        var delta = list.Single(e => e.Name == "Delta.cmd");
        Assert.Equal(Path.Combine(_sb.UserFolder, "Delta.cmd"), delta.ExePath);
    }

    [Fact]
    public void An_id_is_the_source_and_the_name_so_the_same_name_in_two_places_stays_two_entries()
    {
        _sb.Put(_sb.UserRun, "Same", _sb.Exe("same.exe"));
        _sb.Put(_sb.MachineRun, "Same", _sb.Exe("same.exe"));
        var list = _sb.Store().List();
        Assert.Equal(new[] { "UserRun|Same", "MachineRun|Same" }, list.Select(e => e.Id).ToArray());      // equal names keep a fixed order: the source order
        for (var i = 0; i < 3; i++) Assert.Equal(new[] { "UserRun|Same", "MachineRun|Same" }, _sb.Store().List().Select(e => e.Id).ToArray());
    }

    [Fact]
    public void Values_that_are_not_text_or_are_empty_are_skipped()
    {
        _sb.Put(_sb.UserRun, "ok", _sb.Exe("ok.exe"));
        _sb.Put(_sb.UserRun, "dword", 1, RegistryValueKind.DWord);
        _sb.Put(_sb.UserRun, "qword", 1L, RegistryValueKind.QWord);
        _sb.Put(_sb.UserRun, "binary", new byte[] { 1, 2, 3 }, RegistryValueKind.Binary);
        _sb.Put(_sb.UserRun, "multi", new[] { "a", "b" }, RegistryValueKind.MultiString);
        _sb.Put(_sb.UserRun, "empty", "");
        _sb.Put(_sb.UserRun, "blank", "   ");
        _sb.Put(_sb.UserRun, "", _sb.Exe("default.exe"));                  // the "(Default)" value
        Assert.Equal(new[] { "ok" }, _sb.Store().List().Select(e => e.Name).ToArray());
    }

    [Fact]
    public void A_text_that_is_not_a_command_is_still_listed_but_has_no_file_and_is_not_locked()
    {
        _sb.Put(_sb.UserRun, "garbage", "???");
        _sb.Put(_sb.UserRun, "onlyquote", "\"");
        _sb.Put(_sb.UserRun, "noext", @"C:\Program Files\Nothing\thing");
        var list = _sb.Store().List();
        Assert.Equal(3, list.Count);
        Assert.All(list, e => { Assert.Null(e.ExePath); Assert.False(e.Locked); Assert.True(e.Enabled); Assert.False(string.IsNullOrWhiteSpace(e.DisplayName)); });
        Assert.Equal("???", list.Single(e => e.Name == "garbage").Command);
    }

    [Fact]
    public void A_missing_key_or_folder_gives_an_empty_list_and_not_an_exception()
    {
        var gone = _sb.Locations with { UserFolder = Path.Combine(_sb.Dir, "does-not-exist"), CommonFolder = "", WindowsDirectory = "" };
        Assert.Empty(new WindowsStartupStore(gone).List());
        Assert.Throws<ArgumentNullException>(() => new WindowsStartupStore(null!));
    }

    [Fact]
    public void A_name_with_a_trailing_space_is_kept_exactly_because_the_flag_has_the_same_name()
    {
        _sb.Put(_sb.UserRun, "Trailing ", _sb.Exe("trailing.exe"));
        var store = _sb.Store();
        var e = store.List().Single();
        Assert.Equal("Trailing ", e.Name);
        store.SetEnabled(e, false);
        Assert.NotNull(_sb.ReadFlag(_sb.ApprovedUser, "Run", "Trailing "));
        Assert.Null(_sb.ReadFlag(_sb.ApprovedUser, "Run", "Trailing"));
    }

    [Fact]
    public void The_display_name_is_the_one_the_program_is_known_by_else_the_registered_name()
    {
        _sb.Put(_sb.UserRun, "RiotClient", StartupSandbox.Quoted(_sb.Exe("RiotClientServices.exe")) + " --launch-background-mode");
        _sb.Put(_sb.UserRun, "Some Helper", _sb.Exe("helper-exe-name.exe"));
        _sb.Put(_sb.UserRun, "{3F2504E0-4F89-11D3-9A0C-0305E82C3301}", _sb.Exe("guidapp.exe"));
        var list = _sb.Store().List();
        Assert.Equal("Riot Client", list.Single(e => e.Name == "RiotClient").DisplayName);                    // FriendlyNames knows it
        Assert.Equal("Some Helper", list.Single(e => e.Name == "Some Helper").DisplayName);                   // the file says nothing: the registered name
        Assert.Equal("Guidapp", list.Single(e => e.Name.StartsWith("{")).DisplayName);                         // a GUID is no name
    }

    [Fact]
    public void A_bare_file_name_is_found_in_the_windows_folders_else_it_is_unknown()
    {
        File.WriteAllBytes(Path.Combine(_sb.WindowsDir, "System32", "notepad.exe"), Array.Empty<byte>());
        _sb.Put(_sb.UserRun, "Notes", "notepad.exe");
        _sb.Put(_sb.UserRun, "Nowhere", "no-such-tool-12345.exe");
        var list = _sb.Store().List();
        var notes = list.Single(e => e.Name == "Notes");
        Assert.Equal(Path.Combine(_sb.WindowsDir, "System32", "notepad.exe"), notes.ExePath);
        Assert.True(notes.Locked);                                                                            // it lives in the Windows folder
        var nowhere = list.Single(e => e.Name == "Nowhere");
        Assert.Null(nowhere.ExePath);
        Assert.False(nowhere.Locked);
    }

    [Fact]
    public void A_script_host_is_looked_through_to_the_script_or_dll_it_carries()
    {
        var sys32 = Path.Combine(_sb.WindowsDir, "System32");
        _sb.Put(_sb.UserRun, "Script", Path.Combine(sys32, "wscript.exe") + " " + StartupSandbox.Quoted(_sb.Exe("run.vbs")));
        _sb.Put(_sb.UserRun, "Vendor", Path.Combine(sys32, "rundll32.exe") + " " + StartupSandbox.Quoted(_sb.Exe("vendor.dll")) + ",Start");
        _sb.Put(_sb.MachineRun, "Batch", Path.Combine(sys32, "cmd.exe") + " /c " + StartupSandbox.Quoted(_sb.Exe("go.bat")));
        var list = _sb.Store().List();
        Assert.Equal(_sb.Exe("run.vbs"), list.Single(e => e.Name == "Script").ExePath);
        Assert.Equal(_sb.Exe("vendor.dll"), list.Single(e => e.Name == "Vendor").ExePath);
        Assert.Equal(_sb.Exe("go.bat"), list.Single(e => e.Name == "Batch").ExePath);
        Assert.All(list, e => Assert.False(e.Locked));                                                        // the host is in the Windows folder, the program it carries is not
    }

    // ------------------------------------------------------------------------------------------------ the folders

    [Fact]
    public void The_startup_folders_list_shortcuts_and_program_files_and_ignore_everything_else()
    {
        var tool = Path.Combine(_sb.Apps, "tool app.exe");
        File.WriteAllBytes(tool, Array.Empty<byte>());
        StartupSandbox.MakeShortcut(Path.Combine(_sb.UserFolder, "My Tool.lnk"), tool, "--hidden --name \"x y\"");
        File.WriteAllBytes(Path.Combine(_sb.UserFolder, "Broken.lnk"), new byte[] { 1, 2, 3, 4 });           // not a shortcut at all
        foreach (var n in new[] { "run.bat", "go.cmd", "prog.exe", "notes.txt", "script.ps1" }) File.WriteAllText(Path.Combine(_sb.UserFolder, n), "");
        var ini = Path.Combine(_sb.UserFolder, "desktop.ini");
        File.WriteAllText(ini, "[.ShellClassInfo]");
        File.SetAttributes(ini, FileAttributes.Hidden | FileAttributes.System);
        var hidden = Path.Combine(_sb.UserFolder, "hidden.cmd");
        File.WriteAllText(hidden, "");
        File.SetAttributes(hidden, FileAttributes.Hidden);
        var system = Path.Combine(_sb.UserFolder, "system.cmd");
        File.WriteAllText(system, "");
        File.SetAttributes(system, FileAttributes.System);
        Directory.CreateDirectory(Path.Combine(_sb.UserFolder, "folder.cmd"));                               // a folder is not started by Windows
        File.WriteAllText(Path.Combine(_sb.CommonFolder, "shared.cmd"), "");

        var list = _sb.Store().List();

        Assert.Equal(new[] { "Broken.lnk", "My Tool.lnk", "go.cmd", "prog.exe", "run.bat" },
            list.Where(e => e.Source == StartupSource.UserFolder).Select(e => e.Name).OrderBy(x => x, StringComparer.Ordinal).ToArray());
        var shared = list.Single(e => e.Source == StartupSource.CommonFolder);
        Assert.Equal("shared.cmd", shared.Name);
        Assert.Equal("CommonFolder|shared.cmd", shared.Id);

        var lnk = list.Single(e => e.Name == "My Tool.lnk");
        Assert.Equal("My Tool", lnk.DisplayName);
        Assert.NotNull(lnk.ExePath);
        Assert.Equal("tool app.exe", Path.GetFileName(lnk.ExePath!), ignoreCase: true);                      // (the temp folder may be spelled short or long)
        Assert.StartsWith("\"", lnk.Command);                                                                 // a target with a space is quoted
        Assert.EndsWith(" --hidden --name \"x y\"", lnk.Command);

        var broken = list.Single(e => e.Name == "Broken.lnk");
        Assert.Null(broken.ExePath);
        Assert.Contains("Broken.lnk", broken.Command);
        Assert.Equal("Broken", broken.DisplayName);
    }

    [Fact]
    public void A_shortcut_to_a_script_host_shows_the_script_it_starts()
    {
        var script = _sb.Exe("arka plan.vbs");
        File.WriteAllText(script, "");
        StartupSandbox.MakeShortcut(Path.Combine(_sb.UserFolder, "Örnek Çalışma.lnk"), Path.Combine(Environment.SystemDirectory, "wscript.exe"), StartupSandbox.Quoted(script));
        var e = _sb.Store().List().Single();
        Assert.Equal("Örnek Çalışma.lnk", e.Name);                         // (a name with Turkish letters survives the whole way)
        Assert.Equal("Örnek Çalışma", e.DisplayName);
        Assert.NotNull(e.ExePath);
        Assert.Equal("arka plan.vbs", Path.GetFileName(e.ExePath!), ignoreCase: true);
        Assert.False(e.Locked);                                                                               // wscript.exe is Windows', the script is the person's
    }

    // ------------------------------------------------------------------------------------------------ the state

    [Theory]
    [InlineData("02 00 00 00 00 00 00 00 00 00 00 00", true)]
    [InlineData("06 00 00 00 00 00 00 00 00 00 00 00", true)]
    [InlineData("0A 00 00 00 00 00 00 00 00 00 00 00", true)]
    [InlineData("00 00 00 00 00 00 00 00 00 00 00 00", true)]
    [InlineData("03 00 00 00 80 EC 92 B1 94 B3 DC 01", false)]
    [InlineData("07 00 00 00 80 EC 92 B1 94 B3 DC 01", false)]
    [InlineData("0B 00 00 00 80 EC 92 B1 94 B3 DC 01", false)]
    [InlineData("01 00 00 00 00 00 00 00 00 00 00 00", false)]
    [InlineData("03 00 00 00", false)]                                  // the flag alone is enough
    [InlineData("03 00 00", true)]                                      // too short to mean anything
    [InlineData("03", true)]
    [InlineData("", true)]
    public void The_state_comes_from_the_lowest_bit_of_the_first_byte_of_the_flag(string hex, bool enabled)
    {
        var bytes = Hex(hex);
        Assert.Equal(!enabled, StartupApproval.IsDisabled(bytes));
        _sb.Put(_sb.UserRun, "Probe", _sb.Exe("probe.exe"));
        _sb.Flag(_sb.ApprovedUser, "Run", "Probe", bytes);                // a user entry: its flag is in the user place ...
        _sb.Put(_sb.MachineRun, "ProbeMachine", _sb.Exe("probe-machine.exe"));
        _sb.Flag(_sb.ApprovedMachine, "Run", "ProbeMachine", bytes);      // ... a machine entry: its flag is in the machine place; the same bits mean the same
        var list = _sb.Store().List();
        Assert.Equal(enabled, list.Single(e => e.Name == "Probe").Enabled);
        Assert.Equal(enabled, list.Single(e => e.Name == "ProbeMachine").Enabled);
    }

    [Fact]
    public void No_flag_a_flag_of_the_wrong_type_and_null_all_mean_enabled()
    {
        Assert.False(StartupApproval.IsDisabled(null));
        _sb.Put(_sb.UserRun, "NoFlag", _sb.Exe("noflag.exe"));
        _sb.Put(_sb.UserRun, "WrongType", _sb.Exe("wrongtype.exe"));
        _sb.Put(new RegistryPlace(RegistryHive.CurrentUser, _sb.ApprovedUser.SubKey + @"\Run"), "WrongType", 3, RegistryValueKind.DWord);
        _sb.Put(_sb.MachineRun, "MachineWrongType", _sb.Exe("machinewrongtype.exe"));
        _sb.Put(new RegistryPlace(RegistryHive.CurrentUser, _sb.ApprovedMachine.SubKey + @"\Run"), "MachineWrongType", 3, RegistryValueKind.DWord);
        Assert.All(_sb.Store().List(), e => Assert.True(e.Enabled));
    }

    [Fact]
    public void Every_source_reads_the_flag_of_its_own_hive_and_its_own_sub_key_and_nowhere_else()
    {
        var names = new Dictionary<StartupSource, string>
        {
            [StartupSource.UserRun] = "A1",
            [StartupSource.MachineRun] = "A2",
            [StartupSource.MachineRun32] = "A3",
            [StartupSource.UserFolder] = "A4.cmd",
            [StartupSource.CommonFolder] = "A5.cmd",
        };
        _sb.Put(_sb.UserRun, "A1", _sb.Exe("a1.exe"));
        _sb.Put(_sb.MachineRun, "A2", _sb.Exe("a2.exe"));
        _sb.Put(_sb.MachineRun32, "A3", _sb.Exe("a3.exe"));
        File.WriteAllText(Path.Combine(_sb.UserFolder, "A4.cmd"), "");
        File.WriteAllText(Path.Combine(_sb.CommonFolder, "A5.cmd"), "");
        var off = Hex(OffHex);

        // decoys: the name of every entry as "switched off" in every place EXCEPT the right one (both hives x the three sub-keys): they must change nothing
        foreach (var (source, name) in names)
            foreach (var hive in new[] { _sb.ApprovedUser, _sb.ApprovedMachine })
                foreach (var leaf in new[] { "Run", "Run32", "StartupFolder" })
                    if (!(hive == ApprovedOf(source) && leaf == LeafOf(source))) _sb.Flag(hive, leaf, name, off);
        var store = _sb.Store();
        Assert.All(store.List(), e => Assert.True(e.Enabled, e.Id));

        // the right flags, one by one: the entry whose flag it is switches, the others do not
        var done = new List<string>();
        foreach (var (source, name) in names)
        {
            _sb.Flag(ApprovedOf(source), LeafOf(source), name, off);
            done.Add(name);
            Assert.Equal(names.Values.Select(n => (n, !done.Contains(n))).OrderBy(x => x.n, StringComparer.Ordinal).ToArray(),
                store.List().OrderBy(e => e.Name, StringComparer.Ordinal).Select(e => (e.Name, e.Enabled)).ToArray());
        }
    }

    [Fact]
    public void The_same_name_in_the_user_and_the_machine_Run_key_has_two_separate_flags()
    {
        _sb.Put(_sb.UserRun, "Shared", _sb.Exe("shared.exe"));
        _sb.Put(_sb.MachineRun, "Shared", _sb.Exe("shared.exe"));
        var off = Hex(OffHex);

        string States()
        {
            var list = _sb.Store().List();
            return "user:" + (list.Single(e => e.Source == StartupSource.UserRun).Enabled ? "on" : "off")
                 + " machine:" + (list.Single(e => e.Source == StartupSource.MachineRun).Enabled ? "on" : "off");
        }

        Assert.Equal("user:on machine:on", States());

        _sb.Flag(_sb.ApprovedUser, "Run", "Shared", off);                 // the user's "Shared" is switched off: the machine's "Shared" is not
        Assert.Equal("user:off machine:on", States());

        _sb.RemoveKey(_sb.ApprovedUser);
        _sb.Flag(_sb.ApprovedMachine, "Run", "Shared", off);              // and the other way round
        Assert.Equal("user:on machine:off", States());

        // switching one of them with the store leaves the other one (and the other hive) untouched
        _sb.RemoveKey(_sb.ApprovedMachine);
        Assert.Equal("user:on machine:on", States());
        var store = _sb.Store();
        var entries = store.List();
        store.SetEnabled(entries.Single(e => e.Source == StartupSource.UserRun), false);
        Assert.Equal("user:off machine:on", States());
        Assert.NotNull(_sb.ReadFlag(_sb.ApprovedUser, "Run", "Shared"));
        Assert.Null(_sb.ReadFlag(_sb.ApprovedMachine, "Run", "Shared"));
        Assert.False(_sb.Exists(_sb.ApprovedMachine));                    // nothing was even created in the machine place
        store.SetEnabled(entries.Single(e => e.Source == StartupSource.MachineRun), false);
        Assert.Equal("user:off machine:off", States());
        store.SetEnabled(store.List().Single(e => e.Source == StartupSource.UserRun), true);
        Assert.Equal("user:on machine:off", States());
    }

    [Fact]
    public void The_state_of_a_machine_entry_comes_from_the_machine_place_only()
    {
        _sb.Put(_sb.MachineRun, "M1", _sb.Exe("m1.exe"));
        _sb.Put(_sb.MachineRun32, "M2", _sb.Exe("m2.exe"));
        File.WriteAllText(Path.Combine(_sb.CommonFolder, "M3.cmd"), "");
        var off = Hex(OffHex);
        var on = Hex("06 00 00 00 00 00 00 00 00 00 00 00");
        var machine = new[] { ("Run", "M1"), ("Run32", "M2"), ("StartupFolder", "M3.cmd") };

        // an HKCU flag with the same name (in the matching sub-key) must not change a machine entry
        foreach (var (leaf, name) in machine) _sb.Flag(_sb.ApprovedUser, leaf, name, off);
        Assert.All(_sb.Store().List(), e => Assert.True(e.Enabled, e.Id));

        // the machine flag decides: off ...
        foreach (var (leaf, name) in machine) _sb.Flag(_sb.ApprovedMachine, leaf, name, off);
        Assert.All(_sb.Store().List(), e => Assert.False(e.Enabled, e.Id));

        // ... and on again, although the HKCU flags still say "switched off"
        foreach (var (leaf, name) in machine) _sb.Flag(_sb.ApprovedMachine, leaf, name, on);
        Assert.All(_sb.Store().List(), e => Assert.True(e.Enabled, e.Id));
    }

    [Fact]
    public void The_state_of_a_user_entry_comes_from_the_user_place_only()
    {
        _sb.Put(_sb.UserRun, "U1", _sb.Exe("u1.exe"));
        File.WriteAllText(Path.Combine(_sb.UserFolder, "U2.cmd"), "");
        var off = Hex(OffHex);
        _sb.Flag(_sb.ApprovedMachine, "Run", "U1", off);
        _sb.Flag(_sb.ApprovedMachine, "StartupFolder", "U2.cmd", off);
        Assert.All(_sb.Store().List(), e => Assert.True(e.Enabled, e.Id));
        _sb.Flag(_sb.ApprovedUser, "Run", "U1", off);
        _sb.Flag(_sb.ApprovedUser, "StartupFolder", "U2.cmd", off);
        Assert.All(_sb.Store().List(), e => Assert.False(e.Enabled, e.Id));
    }

    [Fact]
    public void Each_source_has_the_place_of_its_hive_and_every_sandbox_place_is_a_throw_away_key()
    {
        var loc = _sb.Locations;
        foreach (var s in Enum.GetValues<StartupSource>())
            Assert.Equal(IsMachine(s) ? loc.ApprovedMachine : loc.ApprovedUser, loc.ApprovedFor(s));
        Assert.NotEqual(loc.ApprovedUser, loc.ApprovedMachine);
        // the sandbox only ever points below its own throw-away key in HKCU: no place of it is a real Run or StartupApproved key
        foreach (var place in new[] { loc.UserRun, loc.MachineRun, loc.MachineRun32, loc.ApprovedUser, loc.ApprovedMachine })
        {
            Assert.Equal(RegistryHive.CurrentUser, place.Hive);
            Assert.StartsWith(@"Software\CS2HazirlaTest\startup-", place.SubKey);
        }
    }

    // ------------------------------------------------------------------------------------------------ switching

    [Fact]
    public void SetEnabled_writes_exactly_the_twelve_bytes_Windows_writes_and_reads_them_back()
    {
        _sb.Put(_sb.UserRun, "Alpha", _sb.Exe("alpha.exe"));
        _sb.Put(_sb.MachineRun, "Bravo", _sb.Exe("bravo.exe"));
        _sb.Put(_sb.MachineRun32, "Charlie", _sb.Exe("charlie.exe"));
        File.WriteAllText(Path.Combine(_sb.UserFolder, "Delta.cmd"), "");
        File.WriteAllText(Path.Combine(_sb.CommonFolder, "Foxtrot.bat"), "");
        var store = _sb.Store();
        var all = store.List();
        Assert.Equal(5, all.Count);

        foreach (var e in all)
        {
            var leaf = LeafOf(e.Source);
            var before = DateTime.UtcNow;
            var off = store.SetEnabled(e, false);
            var after = DateTime.UtcNow;

            Assert.Equal(e.Id, off.Id);
            Assert.False(off.Enabled);
            var bytes = _sb.ReadFlag(ApprovedOf(e.Source), leaf, e.Name)!;           // in the place of its own hive ...
            Assert.Equal(12, bytes.Length);
            Assert.Equal(new byte[] { 3, 0, 0, 0 }, bytes[..4]);
            var when = DateTime.FromFileTimeUtc(BitConverter.ToInt64(bytes, 4));
            Assert.InRange(when, before.AddSeconds(-2), after.AddSeconds(2));
            Assert.Null(_sb.ReadFlag(OtherApprovedOf(e.Source), leaf, e.Name));      // ... and in no other

            var on = store.SetEnabled(off, true);
            Assert.True(on.Enabled);
            Assert.Equal(EnabledBytes, _sb.ReadFlag(ApprovedOf(e.Source), leaf, e.Name));
            Assert.Null(_sb.ReadFlag(OtherApprovedOf(e.Source), leaf, e.Name));
            Assert.True(store.List().Single(x => x.Id == e.Id).Enabled);
        }
    }

    [Fact]
    public void Switching_changes_no_Run_value_and_no_file_of_a_Startup_folder_byte_for_byte()
    {
        _sb.Put(_sb.UserRun, "Alpha", StartupSandbox.Quoted(_sb.Exe("alpha.exe")) + " -x");
        _sb.Put(_sb.UserRun, "Expand", @"%ProgramFiles%\Expand\expand.exe", RegistryValueKind.ExpandString);
        _sb.Put(_sb.UserRun, "Number", 7, RegistryValueKind.DWord);
        _sb.Put(_sb.MachineRun, "Bravo", _sb.Exe("bravo.exe"));
        _sb.Put(_sb.MachineRun32, "Charlie", _sb.Exe("charlie.exe"));
        File.WriteAllText(Path.Combine(_sb.UserFolder, "Delta.cmd"), "@echo delta");
        File.WriteAllText(Path.Combine(_sb.CommonFolder, "Foxtrot.bat"), "@echo foxtrot");

        var runKeys = new[] { _sb.UserRun, _sb.MachineRun, _sb.MachineRun32 };
        var runBefore = runKeys.Select(_sb.Snapshot).ToList();
        var foldersBefore = new[] { StartupSandbox.FolderSnapshot(_sb.UserFolder), StartupSandbox.FolderSnapshot(_sb.CommonFolder) };
        var store = _sb.Store();

        foreach (var e in store.List())
        {
            var off = store.SetEnabled(e, false);
            store.SetEnabled(off, true);
            store.SetEnabled(e, false);                                  // and leave everything switched off
        }

        Assert.Equal(runBefore, runKeys.Select(_sb.Snapshot).ToList());
        Assert.Equal(foldersBefore[0], StartupSandbox.FolderSnapshot(_sb.UserFolder));
        Assert.Equal(foldersBefore[1], StartupSandbox.FolderSnapshot(_sb.CommonFolder));
        Assert.All(store.List(), e => Assert.False(e.Enabled));
        // the only thing that was written: one flag per entry, below the StartupApproved place of the entry's own hive
        Assert.Equal(new[] { "subkey:Run", "subkey:StartupFolder" }, _sb.Snapshot(_sb.ApprovedUser));
        Assert.Equal(new[] { "subkey:Run", "subkey:Run32", "subkey:StartupFolder" }, _sb.Snapshot(_sb.ApprovedMachine));
        Assert.Equal(new[] { "Alpha", "Expand" }, ReadNames(_sb.ApprovedUser, "Run"));
        Assert.Equal(new[] { "Delta.cmd" }, ReadNames(_sb.ApprovedUser, "StartupFolder"));
        Assert.Equal(new[] { "Bravo" }, ReadNames(_sb.ApprovedMachine, "Run"));
        Assert.Equal(new[] { "Charlie" }, ReadNames(_sb.ApprovedMachine, "Run32"));
        Assert.Equal(new[] { "Foxtrot.bat" }, ReadNames(_sb.ApprovedMachine, "StartupFolder"));
    }

    private static string[] ReadNames(RegistryPlace approved, string leaf)
    {
        using var root = RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, RegistryView.Registry64);
        using var key = root.OpenSubKey(approved.SubKey + "\\" + leaf);
        return key == null ? Array.Empty<string>() : key.GetValueNames().OrderBy(x => x, StringComparer.Ordinal).ToArray();
    }

    [Fact]
    public void Switching_machine_entries_writes_nothing_to_the_user_place_and_the_other_way_round()
    {
        _sb.Put(_sb.MachineRun, "Bravo", _sb.Exe("bravo.exe"));
        _sb.Put(_sb.MachineRun32, "Charlie", _sb.Exe("charlie.exe"));
        File.WriteAllText(Path.Combine(_sb.CommonFolder, "Foxtrot.bat"), "");
        var store = _sb.Store();
        foreach (var e in store.List())
        {
            var off = store.SetEnabled(e, false);
            store.SetEnabled(off, true);
            store.SetEnabled(e, false);
        }
        Assert.Equal(new List<string> { "<no key>" }, _sb.Snapshot(_sb.ApprovedUser));              // not even the key was created
        Assert.False(_sb.Exists(_sb.ApprovedUser));
        Assert.Equal(new[] { "Bravo" }, ReadNames(_sb.ApprovedMachine, "Run"));

        _sb.RemoveKey(_sb.ApprovedMachine);
        _sb.Put(_sb.UserRun, "Alpha", _sb.Exe("alpha.exe"));
        File.WriteAllText(Path.Combine(_sb.UserFolder, "Delta.cmd"), "");
        foreach (var e in store.List().Where(x => x.Source is StartupSource.UserRun or StartupSource.UserFolder)) store.SetEnabled(e, false);
        Assert.False(_sb.Exists(_sb.ApprovedMachine));
        Assert.Equal(new[] { "Alpha" }, ReadNames(_sb.ApprovedUser, "Run"));
    }

    [Fact]
    public void A_missing_StartupApproved_key_is_created_on_demand_and_not_before()
    {
        _sb.Put(_sb.UserRun, "Alpha", _sb.Exe("alpha.exe"));
        _sb.Put(_sb.MachineRun, "Bravo", _sb.Exe("bravo.exe"));
        var store = _sb.Store();
        store.List();
        Assert.False(_sb.ApprovedExists());                              // listing never creates it
        var all = store.List();
        var alpha = all.Single(e => e.Name == "Alpha");
        var bravo = all.Single(e => e.Name == "Bravo");
        store.SetEnabled(alpha, true);                                   // it is on already: nothing to write
        store.SetEnabled(bravo, true);
        Assert.False(_sb.ApprovedExists());
        store.SetEnabled(alpha, false);
        Assert.True(_sb.Exists(_sb.ApprovedUser));
        Assert.False(_sb.Exists(_sb.ApprovedMachine));                   // each place is created by its own entries only
        Assert.NotNull(_sb.ReadFlag(_sb.ApprovedUser, "Run", "Alpha"));
        store.SetEnabled(bravo, false);
        Assert.True(_sb.Exists(_sb.ApprovedMachine));
        Assert.NotNull(_sb.ReadFlag(_sb.ApprovedMachine, "Run", "Bravo"));
    }

    [Fact]
    public void An_entry_that_is_already_as_asked_is_not_written_again()
    {
        var six = Hex("06 00 00 00 00 00 00 00 00 00 00 00");
        var oldOff = Hex(OffHex);
        var store = _sb.Store();
        foreach (var (run, source) in new[] { (_sb.UserRun, StartupSource.UserRun), (_sb.MachineRun, StartupSource.MachineRun) })
        {
            var approved = ApprovedOf(source);
            _sb.Put(run, "Windowsish", _sb.Exe("a.exe"));
            _sb.Put(run, "Old", _sb.Exe("b.exe"));
            _sb.Flag(approved, "Run", "Windowsish", six);
            _sb.Flag(approved, "Run", "Old", oldOff);
            var list = store.List().Where(e => e.Source == source).ToList();
            store.SetEnabled(list.Single(e => e.Name == "Windowsish"), true);   // already on: Windows' own 06 stays
            store.SetEnabled(list.Single(e => e.Name == "Old"), false);          // already off: the time it was switched off stays
            Assert.Equal(six, _sb.ReadFlag(approved, "Run", "Windowsish"));
            Assert.Equal(oldOff, _sb.ReadFlag(approved, "Run", "Old"));
        }
    }

    [Fact]
    public void Switching_on_an_entry_that_Windows_marked_off_writes_the_plain_enabled_pattern()
    {
        _sb.Put(_sb.UserRun, "Alpha", _sb.Exe("alpha.exe"));
        _sb.Put(_sb.MachineRun, "Bravo", _sb.Exe("bravo.exe"));
        _sb.Flag(_sb.ApprovedUser, "Run", "Alpha", Hex("07 00 00 00 80 EC 92 B1 94 B3 DC 01"));
        _sb.Flag(_sb.ApprovedMachine, "Run", "Bravo", Hex("07 00 00 00 80 EC 92 B1 94 B3 DC 01"));
        var store = _sb.Store();
        foreach (var e in store.List())
        {
            Assert.False(e.Enabled, e.Id);
            Assert.True(store.SetEnabled(e, true).Enabled, e.Id);
            Assert.Equal(EnabledBytes, _sb.ReadFlag(ApprovedOf(e.Source), "Run", e.Name));
        }
    }

    [Fact]
    public void An_entry_that_is_gone_throws_and_writes_nothing()
    {
        _sb.Put(_sb.UserRun, "Alpha", _sb.Exe("alpha.exe"));
        var store = _sb.Store();
        var e = store.List().Single();
        _sb.Remove(_sb.UserRun, "Alpha");                                // removed (by an uninstaller) after the list was read
        var ex = Assert.Throws<StartupException>(() => store.SetEnabled(e, false));
        Assert.False(string.IsNullOrWhiteSpace(ex.Message));
        Assert.False(_sb.ApprovedExists());
    }

    [Fact]
    public void While_the_game_runs_nothing_is_read_and_nothing_is_changed()
    {
        _sb.Put(_sb.UserRun, "Alpha", _sb.Exe("alpha.exe"));
        var entry = _sb.Store().List().Single();
        var gameRunning = true;
        var store = new WindowsStartupStore(_sb.Locations) { GameRunning = () => gameRunning };
        Assert.Empty(store.List());
        var ex = Assert.Throws<StartupException>(() => store.SetEnabled(entry, false));
        Assert.False(string.IsNullOrWhiteSpace(ex.Message));
        Assert.False(_sb.ApprovedExists());
        gameRunning = false;
        Assert.Single(store.List());
        Assert.False(store.SetEnabled(entry, false).Enabled);
    }

    /// <summary>A store whose game check says "not running" for the first <paramref name="quietCalls"/> questions and "running" for every later one.</summary>
    private (WindowsStartupStore Store, Func<int> Asked) StoreWhereTheGameStartsAfter(int quietCalls)
    {
        var asked = 0;
        var store = new WindowsStartupStore(_sb.Locations) { GameRunning = () => ++asked > quietCalls };
        return (store, () => asked);
    }

    [Fact]
    public void A_game_that_starts_while_the_switch_is_made_stops_it_right_before_the_write()
    {
        _sb.Put(_sb.UserRun, "Alpha", _sb.Exe("alpha.exe"));
        _sb.Put(_sb.MachineRun, "Bravo", _sb.Exe("bravo.exe"));
        var entries = _sb.Store().List();
        var gameText = Loc.T("Oyun açıkken değişiklik yapılmaz; oyunu kapatınca tekrar dene.");
        var goneText = Loc.T("Bu program artık Windows'la birlikte açılanlar arasında değil.");

        foreach (var entry in entries)
        {
            // the question is asked: (1) when SetEnabled starts, (2) when it reads the list, (3) once more right before the write.
            // "Not running" for the first two and "running" from the third question on: the third one must stop the write.
            var (store, asked) = StoreWhereTheGameStartsAfter(2);
            var ex = Assert.Throws<StartupException>(() => store.SetEnabled(entry, false));
            Assert.Equal(gameText, ex.Message);
            Assert.Equal(3, asked());                                    // and nothing was asked or read after that
            Assert.False(_sb.ApprovedExists(), entry.Id);                // nothing was written, not even a key was created
            Assert.All(_sb.Store().List(), e => Assert.True(e.Enabled, e.Id));
        }
    }

    [Fact]
    public void A_game_that_starts_while_the_list_is_read_is_not_reported_as_a_program_that_is_gone()
    {
        _sb.Put(_sb.UserRun, "Alpha", _sb.Exe("alpha.exe"));
        var entry = _sb.Store().List().Single();
        var (store, _) = StoreWhereTheGameStartsAfter(1);                // the first question (at the start) says no, the one inside List() says yes: the list comes back empty
        var ex = Assert.Throws<StartupException>(() => store.SetEnabled(entry, false));
        Assert.Equal(Loc.T("Oyun açıkken değişiklik yapılmaz; oyunu kapatınca tekrar dene."), ex.Message);
        Assert.NotEqual(Loc.T("Bu program artık Windows'la birlikte açılanlar arasında değil."), ex.Message);
        Assert.False(_sb.ApprovedExists());
    }

    [Fact]
    public void A_game_that_starts_only_after_the_write_does_not_undo_it()
    {
        _sb.Put(_sb.UserRun, "Alpha", _sb.Exe("alpha.exe"));
        var entry = _sb.Store().List().Single();
        var (store, asked) = StoreWhereTheGameStartsAfter(3);            // start, list, the check before the write: all "not running"; only the read-back after the write says "running"
        var result = store.SetEnabled(entry, false);
        Assert.False(result.Enabled);                                    // (the read-back came back empty, so the answer is the entry as it was switched)
        Assert.Equal(4, asked());
        Assert.NotNull(_sb.ReadFlag(_sb.ApprovedUser, "Run", "Alpha"));
        Assert.False(_sb.Store().List().Single().Enabled);
    }

    [Fact]
    public void A_flag_that_cannot_be_written_is_a_StartupException_and_the_Run_value_stays()
    {
        _sb.Put(_sb.UserRun, "Alpha", _sb.Exe("alpha.exe"));
        _sb.Put(_sb.MachineRun, "Bravo", _sb.Exe("bravo.exe"));
        var tooLong = new RegistryPlace(RegistryHive.CurrentUser, _sb.RootKey + "\\" + new string('x', 300));   // a key name over 255 characters
        var runBefore = new[] { _sb.Snapshot(_sb.UserRun), _sb.Snapshot(_sb.MachineRun) };
        var cases = new[]
        {
            (Locations: _sb.Locations with { ApprovedUser = tooLong }, Source: StartupSource.UserRun),
            (Locations: _sb.Locations with { ApprovedMachine = tooLong }, Source: StartupSource.MachineRun),
        };
        foreach (var (broken, source) in cases)
        {
            var store = new WindowsStartupStore(broken);
            var all = store.List();                                      // listing still works (everything counts as enabled)
            Assert.All(all, e => Assert.True(e.Enabled));
            var ex = Assert.Throws<StartupException>(() => store.SetEnabled(all.Single(e => e.Source == source), false));
            Assert.Equal(Loc.T("Ayar kaydedilemedi; hiçbir şey değişmedi."), ex.Message);   // any other failure than a missing right: the general text, also for HKLM
            Assert.Equal(runBefore, new[] { _sb.Snapshot(_sb.UserRun), _sb.Snapshot(_sb.MachineRun) });
            Assert.False(_sb.ApprovedExists());
        }
    }

    [Theory]
    [InlineData(typeof(UnauthorizedAccessException))]
    [InlineData(typeof(System.Security.SecurityException))]
    public void Without_administrator_rights_a_machine_entry_says_so_and_nothing_changes(Type failure)
    {
        _sb.Put(_sb.MachineRun, "Bravo", _sb.Exe("bravo.exe"));
        _sb.Put(_sb.MachineRun32, "Charlie", _sb.Exe("charlie.exe"));
        File.WriteAllText(Path.Combine(_sb.CommonFolder, "Foxtrot.bat"), "");
        var runBefore = new[] { _sb.Snapshot(_sb.MachineRun), _sb.Snapshot(_sb.MachineRun32) };
        var tried = new List<(RegistryPlace Place, string Leaf, string Name)>();
        var store = new WindowsStartupStore(_sb.Locations)
        {
            // what the registry does to a program without the right to write HKLM: it refuses
            FlagWriter = (place, leaf, name, bytes) =>
            {
                tried.Add((place, leaf, name));
                throw (Exception)Activator.CreateInstance(failure)!;
            },
        };

        var all = store.List();
        Assert.Equal(3, all.Count);
        foreach (var e in all)
        {
            var ex = Assert.Throws<StartupException>(() => store.SetEnabled(e, false));
            Assert.Equal(Loc.T("Windows bu değişikliğe izin vermedi (yönetici izni gerekebilir); hiçbir şey değişmedi."), ex.Message);
            Assert.NotEqual(Loc.T("Ayar kaydedilemedi; hiçbir şey değişmedi."), ex.Message);
        }

        Assert.Equal(3, tried.Count);                                    // it did try to write ...
        Assert.All(tried, t => Assert.Equal(_sb.ApprovedMachine, t.Place));   // ... the machine place, and never the user place
        Assert.False(_sb.ApprovedExists());                              // nothing was written
        Assert.Equal(runBefore, new[] { _sb.Snapshot(_sb.MachineRun), _sb.Snapshot(_sb.MachineRun32) });
        Assert.All(store.List(), e => Assert.True(e.Enabled, e.Id));
    }

    [Fact]
    public void A_refused_write_of_a_user_entry_is_not_called_a_missing_administrator_right()
    {
        _sb.Put(_sb.UserRun, "Alpha", _sb.Exe("alpha.exe"));
        var tried = new List<RegistryPlace>();
        var store = new WindowsStartupStore(_sb.Locations)
        {
            FlagWriter = (place, leaf, name, bytes) => { tried.Add(place); throw new UnauthorizedAccessException(); },
        };
        var ex = Assert.Throws<StartupException>(() => store.SetEnabled(store.List().Single(), false));
        Assert.Equal(Loc.T("Ayar kaydedilemedi; hiçbir şey değişmedi."), ex.Message);            // the HKCU flag does not need administrator rights: that would be a wrong claim
        Assert.Equal(new[] { _sb.ApprovedUser }, tried);
        Assert.False(_sb.ApprovedExists());
    }

    [Fact]
    public void Other_failures_of_the_write_are_the_general_text_for_machine_entries_too()
    {
        _sb.Put(_sb.MachineRun, "Bravo", _sb.Exe("bravo.exe"));
        var store = new WindowsStartupStore(_sb.Locations) { FlagWriter = (_, _, _, _) => throw new IOException("disk") };
        var ex = Assert.Throws<StartupException>(() => store.SetEnabled(store.List().Single(), false));
        Assert.Equal(Loc.T("Ayar kaydedilemedi; hiçbir şey değişmedi."), ex.Message);
    }

    [Fact]
    public void A_null_entry_is_a_programming_mistake_and_says_so()
    {
        Assert.Throws<ArgumentNullException>(() => _sb.Store().SetEnabled(null!, true));
    }

    // ------------------------------------------------------------------------------------------------ locked entries

    [Fact]
    public void Locked_entries_say_why_throw_on_a_switch_and_write_nothing()
    {
        var sys32 = Path.Combine(_sb.WindowsDir, "System32");
        _sb.Put(_sb.UserRun, "LockSelf", StartupSandbox.Quoted(_sb.Exe("Antilag.exe")));
        _sb.Put(_sb.UserRun, "LockAv", StartupSandbox.Quoted(_sb.Exe("AvastUI.exe")) + " /silent");
        _sb.Put(_sb.UserRun, "LockWindows", Path.Combine(sys32, "sometool.exe"));
        _sb.Put(_sb.UserRun, "LockAudio", StartupSandbox.Quoted(_sb.Exe("RtkAudUService64.exe")) + " -background");
        _sb.Put(_sb.MachineRun, "LockGpu", StartupSandbox.Quoted(_sb.Exe("NVDisplay.Container.exe")));
        _sb.Put(_sb.MachineRun, "LockDefender", StartupSandbox.Quoted(_sb.Exe("SecurityHealthSystray.exe")));
        _sb.Put(_sb.MachineRun32, "LockExplorer", StartupSandbox.Quoted(_sb.Exe("explorer.exe")));
        var store = _sb.Store();
        var list = store.List();
        Assert.Equal(7, list.Count);

        foreach (var e in list)
        {
            Assert.True(e.Locked, e.Name);
            Assert.False(string.IsNullOrWhiteSpace(e.LockedReason), e.Name);
            var ex = Assert.Throws<StartupException>(() => store.SetEnabled(e, false));
            Assert.False(string.IsNullOrWhiteSpace(ex.Message));
            // an edited copy that claims not to be locked gets the same answer: the machine's rules decide, not the caller's entry
            Assert.Throws<StartupException>(() => store.SetEnabled(e with { Locked = false, LockedReason = "" }, false));
            Assert.Throws<StartupException>(() => store.SetEnabled(e, true));
        }
        Assert.False(_sb.ApprovedExists());                              // not a single byte was written
        Assert.Equal(StartupGuard.LockReason(_sb.Exe("Antilag.exe"), null), list.Single(e => e.Name == "LockSelf").LockedReason);       // a reason of its own for Antilag
        Assert.NotEqual(list.Single(e => e.Name == "LockSelf").LockedReason, list.Single(e => e.Name == "LockAv").LockedReason);
        Assert.NotEqual(list.Single(e => e.Name == "LockAv").LockedReason, list.Single(e => e.Name == "LockAudio").LockedReason);
    }

    [Fact]
    public void Everything_else_can_be_switched_including_Steam_and_the_FACEIT_client()
    {
        var names = new[] { "Steam", "FACEIT", "Discord", "OneDrive", "Edge", "Free" };
        _sb.Put(_sb.UserRun, "Steam", StartupSandbox.Quoted(_sb.Exe("steam.exe")) + " -silent");
        _sb.Put(_sb.UserRun, "FACEIT", StartupSandbox.Quoted(_sb.Exe("FACEITClient.exe")));
        _sb.Put(_sb.UserRun, "Discord", StartupSandbox.Quoted(_sb.Exe("Update.exe")) + " --processStart Discord.exe");
        _sb.Put(_sb.UserRun, "OneDrive", StartupSandbox.Quoted(_sb.Exe("OneDrive.exe")) + " /background");
        _sb.Put(_sb.UserRun, "Edge", StartupSandbox.Quoted(_sb.Exe("msedge.exe")) + " --no-startup-window");
        _sb.Put(_sb.UserRun, "Free", _sb.Exe("freeone.exe"));
        var store = _sb.Store();
        var list = store.List();
        Assert.Equal(names.OrderBy(x => x, StringComparer.Ordinal).ToArray(), list.Select(e => e.Name).OrderBy(x => x, StringComparer.Ordinal).ToArray());
        Assert.All(list, e => { Assert.False(e.Locked, e.Name); Assert.Equal("", e.LockedReason); });
        foreach (var e in list) Assert.False(store.SetEnabled(e, false).Enabled);
    }

    // ------------------------------------------------------------------------------------------------ the guard on its own

    [Fact]
    public void The_guard_locks_Antilag_security_programs_and_the_names_of_the_protected_list_and_never_states_what_a_program_is()
    {
        const string selfText = "Antilag'ın kendisi; buradan değiştirilemez.";
        const string securityText = "Güvenlik programı; buradan değiştirilemez.";
        // the protected list matches by the beginning of a file name and can match more than it was meant for: the reason only says "may be needed"
        const string maybeNeededText = "Bilgisayarın çalışması için gerekli olabilir; buradan değiştirilemez.";

        Assert.Equal(selfText, StartupGuard.LockReason(@"C:\Apps\Antilag.exe", null));
        Assert.Equal(selfText, StartupGuard.LockReason(@"C:\Old\CS2Hazirla.exe", null));
        Assert.NotNull(Environment.ProcessPath);
        Assert.Equal(selfText, StartupGuard.LockReason(Environment.ProcessPath, null));

        foreach (var av in new[] { "avp.exe", "mbamtray.exe", "BitDefenderAgent.exe", "AvastUI.exe", "ekrn.exe", "NortonUI.exe" })
            Assert.Equal(securityText, StartupGuard.LockReason(@"C:\Apps\" + av, null));

        foreach (var part in new[] { "explorer.exe", "MsMpEng.exe", "SecurityHealthSystray.exe", "NVDisplay.Container.exe", "RtkAudUService64.exe", "nahimic.exe", "igfxtray.exe", "RadeonSoftware.exe" })
            Assert.Equal(maybeNeededText, StartupGuard.LockReason(@"C:\Apps\" + part, null));

        // the other way into the same lock, a file of the Windows folder (not on the protected list), says the same hedged thing
        Assert.Equal(maybeNeededText, StartupGuard.LockReason(@"C:\Fake\Win\System32\sometool.exe", @"C:\Fake\Win", _ => null));
        Assert.Equal(maybeNeededText, StartupGuard.LockReason(@"C:\Fake\Win\System32\sometool.exe", @"C:\Fake\Win", _ => "Microsoft Corporation"));
    }

    [Fact]
    public void The_guard_leaves_game_software_and_ordinary_programs_alone()
    {
        foreach (var free in new[] { "steam.exe", "cs2.exe", "FACEITClient.exe", "freeone.exe", "SteelSeriesGGEZ.exe", "Update.exe", "msedge.exe", "OneDrive.exe",
                                     "NVIDIA Broadcast.exe", "RiotClientServices.exe", "EpicGamesLauncher.exe", "opera.exe", "IDMan.exe", "Discord.exe", "freeone app.exe" })
            Assert.Null(StartupGuard.LockReason(@"C:\Apps\" + free, null));
        Assert.Null(StartupGuard.LockReason(null, @"C:\Windows"));
        Assert.Null(StartupGuard.LockReason("", @"C:\Windows"));
        Assert.Null(StartupGuard.LockReason("   ", @"C:\Windows"));
    }

    [Fact]
    public void A_file_in_the_Windows_folder_is_locked_unless_it_says_it_is_made_by_someone_other_than_Microsoft()
    {
        const string win = @"C:\Fake\Win";
        const string dll = @"C:\Fake\Win\System32\vendor.dll";
        Assert.NotNull(StartupGuard.LockReason(dll, win, _ => "Microsoft Corporation"));
        Assert.NotNull(StartupGuard.LockReason(dll, win, _ => null));                         // unknown = Windows' own
        Assert.NotNull(StartupGuard.LockReason(dll, win, _ => "  "));
        Assert.NotNull(StartupGuard.LockReason(dll, win, _ => throw new IOException("unreadable")));
        Assert.Null(StartupGuard.LockReason(dll, win, _ => "Logitech Inc."));                 // a vendor helper that sits in System32 can be switched

        Assert.NotNull(StartupGuard.LockReason(@"c:\fake\WIN\System32\vendor.dll", win + "\\", _ => null));   // case and a trailing backslash do not matter
        Assert.Null(StartupGuard.LockReason(@"C:\Fake\Win2\vendor.dll", win, _ => null));                   // a sibling folder with the same beginning is not the Windows folder
        Assert.Null(StartupGuard.LockReason(@"C:\Other\vendor.dll", win, _ => "Microsoft Corporation"));    // Microsoft outside the Windows folder is an ordinary program
        Assert.Null(StartupGuard.LockReason(dll, "", _ => null));                              // no Windows folder given: that rule is off
        Assert.Null(StartupGuard.LockReason(dll, null, _ => null));
    }

    [Fact]
    public void A_file_on_the_protected_list_is_never_opened_for_its_version_information()
    {
        foreach (var protectedFile in new[] { @"C:\Apps\steam.exe", @"C:\Apps\cs2.exe", @"C:\Apps\FACEITClient.exe", @"C:\Apps\RtkAudUService64.exe", @"C:\Windows\explorer.exe", @"C:\Apps\antilag.exe" })
            Assert.False(StartupGuard.MayReadFile(protectedFile), protectedFile);
        Assert.True(StartupGuard.MayReadFile(Path.Combine(Path.GetTempPath(), "freeone.exe")));              // an ordinary file on a fixed disk
        Assert.True(StartupGuard.MayReadFile(Path.Combine(Environment.SystemDirectory, "vendor.dll")));
        Assert.False(StartupGuard.MayReadFile(@"\\no-such-server\share\freeone.exe"));                       // a network path could make the list wait: never opened
        Assert.False(StartupGuard.MayReadFile("freeone.exe"));                                                // no folder: nothing to open
        Assert.False(StartupGuard.MayReadFile(null));
        Assert.False(StartupGuard.MayReadFile(""));

        // the company lookup of the guard is not even asked about a protected name
        var asked = new List<string>();
        Assert.NotNull(StartupGuard.LockReason(@"C:\Windows\System32\explorer.exe", @"C:\Windows", p => { asked.Add(p); return "Someone"; }));
        Assert.Empty(asked);
    }

    // ------------------------------------------------------------------------------------------------ the 12 bytes

    [Fact]
    public void The_bytes_are_the_ones_Task_Manager_writes()
    {
        Assert.Equal(EnabledBytes, StartupApproval.Enabled());
        var when = new DateTime(2026, 3, 14, 9, 26, 53, 682, DateTimeKind.Utc).AddTicks(8206);
        var bytes = StartupApproval.Disabled(when);
        Assert.Equal(12, bytes.Length);
        Assert.Equal(new byte[] { 3, 0, 0, 0 }, bytes[..4]);
        Assert.Equal(when.ToFileTimeUtc(), BitConverter.ToInt64(bytes, 4));
        Assert.Equal(when, DateTime.FromFileTimeUtc(BitConverter.ToInt64(bytes, 4)));
        // the byte order, pinned by a fixed example: 03 00 00 00 + the FILETIME of 2026-03-14T09:26:53Z as 8 little-endian bytes (made up for this test)
        var example = Hex(OffHex);
        Assert.True(StartupApproval.IsDisabled(example));
        Assert.Equal(new DateTime(2026, 3, 14, 9, 26, 53, DateTimeKind.Utc), DateTime.FromFileTimeUtc(BitConverter.ToInt64(example, 4)));
        Assert.Equal(example, StartupApproval.Disabled(new DateTime(2026, 3, 14, 9, 26, 53, DateTimeKind.Utc)));
        Assert.False(StartupApproval.IsDisabled(StartupApproval.Enabled()));
        Assert.True(StartupApproval.IsDisabled(StartupApproval.Disabled(DateTime.UtcNow)));
    }

    [Fact]
    public void The_real_locations_are_the_places_Windows_uses()
    {
        var real = StartupLocations.Real;
        const string approved = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved";
        Assert.Equal(new RegistryPlace(RegistryHive.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Run"), real.UserRun);
        Assert.Equal(new RegistryPlace(RegistryHive.LocalMachine, @"Software\Microsoft\Windows\CurrentVersion\Run"), real.MachineRun);
        Assert.Equal(new RegistryPlace(RegistryHive.LocalMachine, @"Software\WOW6432Node\Microsoft\Windows\CurrentVersion\Run"), real.MachineRun32);
        // the flags: one StartupApproved key per hive, the same sub-key path (only these two places are ever written, and only the one of the entry that is switched)
        Assert.Equal(new RegistryPlace(RegistryHive.CurrentUser, approved), real.ApprovedUser);
        Assert.Equal(new RegistryPlace(RegistryHive.LocalMachine, approved), real.ApprovedMachine);
        Assert.Equal(real.ApprovedUser, real.ApprovedFor(StartupSource.UserRun));
        Assert.Equal(real.ApprovedUser, real.ApprovedFor(StartupSource.UserFolder));
        Assert.Equal(real.ApprovedMachine, real.ApprovedFor(StartupSource.MachineRun));
        Assert.Equal(real.ApprovedMachine, real.ApprovedFor(StartupSource.MachineRun32));
        Assert.Equal(real.ApprovedMachine, real.ApprovedFor(StartupSource.CommonFolder));
        Assert.EndsWith(@"Programs\Startup", real.UserFolder);
        Assert.StartsWith(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), real.UserFolder, StringComparison.OrdinalIgnoreCase);
        Assert.False(string.IsNullOrWhiteSpace(real.WindowsDirectory));
    }

    // ------------------------------------------------------------------------------------------------ the command parser

    private static object?[] Case(string command, string? exe) => new object?[] { command.Replace('\'', '"'), exe };

    /// <summary>In the commands an apostrophe stands for a double quote (so the cases stay readable).</summary>
    public static IEnumerable<object?[]> ExeCases()
    {
        // quoted and plain paths
        yield return Case(@"'C:\Program Files\Foo Bar\foo.exe' --silent", @"C:\Program Files\Foo Bar\foo.exe");
        yield return Case(@"C:\Program Files (x86)\Internet Download Manager\IDMan.exe /onboot", @"C:\Program Files (x86)\Internet Download Manager\IDMan.exe");
        yield return Case(@"C:\Riot Games\Riot Client\RiotClientServices.exe --launch-background-mode", @"C:\Riot Games\Riot Client\RiotClientServices.exe");
        yield return Case(@"D:\stea\STEAM\steam.exe -silent", @"D:\stea\STEAM\steam.exe");
        yield return Case(@"C:\Program Files\NVIDIA Corporation\NVIDIA Broadcast\NVIDIA Broadcast.exe --process-start-args '--launch-hidden'", @"C:\Program Files\NVIDIA Corporation\NVIDIA Broadcast\NVIDIA Broadcast.exe");
        yield return Case(@"'C:\Users\me\AppData\Local\Discord\Update.exe' --processStart Discord.exe", @"C:\Users\me\AppData\Local\Discord\Update.exe");
        yield return Case(@"C:\Tools\app.exe", @"C:\Tools\app.exe");
        yield return Case(@"'C:\Tools\app.exe'", @"C:\Tools\app.exe");
        yield return Case(@"'C:\Tools\app.exe'-x", @"C:\Tools\app.exe");
        yield return Case(@"   'C:\Tools\app.exe'   ", @"C:\Tools\app.exe");
        yield return Case(@"C:\Tools\APP.EXE /S", @"C:\Tools\APP.EXE");
        yield return Case(@"'C:\Unclosed quote\app.exe -x", @"C:\Unclosed quote\app.exe");
        yield return Case(@"C:\Tools\start.bat", @"C:\Tools\start.bat");
        yield return Case(@"C:\Tools\run.cmd arg", @"C:\Tools\run.cmd");
        yield return Case(@"C:\Scripts\task.vbs", @"C:\Scripts\task.vbs");
        yield return Case("notepad.exe", "notepad.exe");                                  // a bare name stays bare: the store finds it in the Windows folders
        // hosts that only carry a program
        yield return Case(@"C:\Windows\system32\rundll32.exe C:\Windows\System32\LogiLDA.dll,LogiFetch", @"C:\Windows\System32\LogiLDA.dll");
        yield return Case(@"rundll32.exe 'C:\Program Files\X Y\x.dll',Start arg", @"C:\Program Files\X Y\x.dll");
        yield return Case(@"rundll32 shell32.dll,Control_RunDLL", "shell32.dll");
        yield return Case(@"cmd /c 'C:\Scripts\go.bat'", @"C:\Scripts\go.bat");
        yield return Case(@"C:\Windows\System32\cmd.exe /d /c start '' /min 'C:\Program Files\App\app.exe' -x", @"C:\Program Files\App\app.exe");
        yield return Case(@"cmd /c ''C:\a b\c.bat' arg'", @"C:\a b\c.bat");
        // a pair of quotes around the whole start command is cmd's own and is looked through once
        yield return Case(@"cmd /c 'start /min C:\x\y.exe'", @"C:\x\y.exe");
        yield return Case(@"C:\Windows\System32\cmd.exe /d /c 'start /min C:\x\y.exe'", @"C:\x\y.exe");
        yield return Case(@"cmd /c 'start '' /min 'C:\Program Files\App\app.exe' -x'", @"C:\Program Files\App\app.exe");
        yield return Case(@"cmd /c ' start /min C:\x\y.exe '", @"C:\x\y.exe");
        yield return Case(@"cmd /c 'START C:\x\y.exe'", @"C:\x\y.exe");
        yield return Case(@"cmd /c ''start /min C:\x\y.exe''", @"C:\x\y.exe");
        yield return Case(@"cmd /c 'start'", null);
        yield return Case(@"cmd /c 'starter tool.exe'", "starter tool.exe");                // only the word "start" counts: this is a quoted file name, not a start command
        yield return Case(@"cmd /k C:\Tools\a.exe", @"C:\Tools\a.exe");
        yield return Case(@"C:\Windows\System32\wscript.exe //B 'C:\Users\me\scripts\run.vbs'", @"C:\Users\me\scripts\run.vbs");
        yield return Case(@"cscript //Nologo C:\Scripts\a.vbs", @"C:\Scripts\a.vbs");
        yield return Case(@"powershell -NoProfile -WindowStyle Hidden -File 'C:\Scripts\a.ps1'", @"C:\Scripts\a.ps1");
        // nothing can be told
        yield return Case("", null);
        yield return Case("   ", null);
        yield return Case("garbage", null);
        yield return Case("???", null);
        yield return Case("'", null);
        yield return Case("''", null);
        yield return Case(",,,", null);
        yield return Case(@"C:\Program", null);
        yield return Case(@"C:\Program Files\Nothing\thing", null);
        yield return Case(@"%NOPE_NOT_SET_12345%\foo.exe", null);                          // a variable that does not exist here is not guessed
        yield return Case("rundll32.exe", null);
        yield return Case("cmd /c", null);
        yield return Case("cmd /c echo hi", null);
        yield return Case("cmd /c timeout 5 && start x.exe", null);
        yield return Case("powershell -Command 'Start-Process x'", null);
    }

    [Theory]
    [MemberData(nameof(ExeCases))]
    public void ExeOf_finds_the_file_that_really_starts(string command, string? expected)
    {
        Assert.Equal(expected, StartupCommand.ExeOf(command));
    }

    [Fact]
    public void ExeOf_expands_variables_and_ExeOf_of_null_is_null()
    {
        var pf = Environment.GetEnvironmentVariable("ProgramFiles")!;
        var windir = Environment.GetEnvironmentVariable("windir")!;
        Assert.Equal(Path.Combine(pf, @"Foo\foo.exe"), StartupCommand.ExeOf(@"%ProgramFiles%\Foo\foo.exe -x"));
        Assert.Equal(Path.Combine(pf, @"Foo Bar\foo.exe"), StartupCommand.ExeOf(@"%ProgramFiles%\Foo Bar\foo.exe -x"));
        Assert.Equal(Path.Combine(pf, @"Foo Bar\foo.exe"), StartupCommand.ExeOf("\"%ProgramFiles%\\Foo Bar\\foo.exe\" -x"));
        Assert.Equal(Path.Combine(windir, @"system32\SecurityHealthSystray.exe"), StartupCommand.ExeOf(@"%windir%\system32\SecurityHealthSystray.exe"));
        Assert.Null(StartupCommand.ExeOf(null));
    }

    [Fact]
    public void Split_separates_the_program_from_its_arguments()
    {
        Assert.Equal((@"C:\a b\c.exe", "-x -y"), StartupCommand.Split("\"C:\\a b\\c.exe\" -x -y"));
        Assert.Equal((@"C:\a b\c.exe", "-x"), StartupCommand.Split(@"C:\a b\c.exe -x"));
        Assert.Equal(("notepad.exe", ""), StartupCommand.Split("notepad.exe"));
        Assert.Equal(("cmd", "/c x"), StartupCommand.Split("cmd /c x"));
        Assert.Equal(("", ""), StartupCommand.Split(""));
        Assert.Equal(("", ""), StartupCommand.Split(null));
        Assert.Equal(("", ""), StartupCommand.Split("   "));
    }

    [Fact]
    public void No_text_whatever_makes_the_parser_throw()
    {
        var rnd = new Random(20261026);
        const string alphabet = "abcXYZ .\\:\"%,/-_(){}?*<>|\0\t'=&1";
        for (var i = 0; i < 4000; i++)
        {
            var chars = new char[rnd.Next(0, 48)];
            for (var j = 0; j < chars.Length; j++) chars[j] = alphabet[rnd.Next(alphabet.Length)];
            var text = new string(chars);
            var exe = StartupCommand.ExeOf(text);
            StartupCommand.Split(text);
            if (exe != null)
            {
                Assert.False(string.IsNullOrWhiteSpace(exe), text);
                Assert.DoesNotContain('"', exe);
                Assert.DoesNotContain('%', exe);
            }
        }
    }

    // ------------------------------------------------------------------------------------------------ the plain sentence

    private static StartupEntry Fake(string name, bool enabled) =>
        new("UserRun|" + name, name, name, name, null, StartupSource.UserRun, enabled, false, "");

    [Fact]
    public void The_summary_counts_the_programs_that_open_and_the_ones_that_are_switched_off()
    {
        Assert.Equal("Windows'la birlikte açılan program yok.", StartupSummary.Line(Array.Empty<StartupEntry>()));
        Assert.Equal("Windows'la birlikte açılan program yok.", StartupSummary.Line(null!));
        Assert.Equal("1 program Windows'la birlikte açılıyor.", StartupSummary.Line(new[] { Fake("a", true) }));
        Assert.Equal("3 program Windows'la birlikte açılıyor.", StartupSummary.Line(new[] { Fake("a", true), Fake("b", true), Fake("c", true) }));
        Assert.Equal("2 program Windows'la birlikte açılıyor; 1 program daha kapalı.", StartupSummary.Line(new[] { Fake("a", true), Fake("b", true), Fake("c", false) }));
        Assert.Equal("Hiçbir program Windows'la birlikte açılmıyor; 2 program kapalı.", StartupSummary.Line(new[] { Fake("a", false), Fake("b", false) }));
    }

    [Fact]
    public void Every_source_has_a_note()
    {
        foreach (var s in Enum.GetValues<StartupSource>()) Assert.False(string.IsNullOrWhiteSpace(StartupSummary.SourceNote(s)), s.ToString());
    }
}

/// <summary>The same engine in English: every text it shows has an English entry, and the plurals read right.</summary>
[Collection("language")]
public class StartupEnglishTests : IDisposable
{
    private static readonly Regex TurkishLetter = new("[çğıöşüÇĞİÖŞÜ]", RegexOptions.CultureInvariant);
    private readonly StartupSandbox _sb = new();

    public StartupEnglishTests()
    {
        Loc.Use("en");
        Loc.ClearMissing();
    }

    public void Dispose()
    {
        Loc.Use(TestLanguage.Default);
        _sb.Dispose();
    }

    private static StartupEntry Fake(bool enabled) =>
        new("UserRun|x", "x", "x", "x", null, StartupSource.UserRun, enabled, false, "");

    private static IReadOnlyList<StartupEntry> Many(int on, int off) =>
        Enumerable.Range(0, on).Select(_ => Fake(true)).Concat(Enumerable.Range(0, off).Select(_ => Fake(false))).ToList();

    [Fact]
    public void The_summary_reads_right_for_one_and_for_many()
    {
        Assert.Equal("No programs open with Windows.", StartupSummary.Line(Many(0, 0)));
        Assert.Equal("1 program opens with Windows.", StartupSummary.Line(Many(1, 0)));
        Assert.Equal("3 programs open with Windows.", StartupSummary.Line(Many(3, 0)));
        Assert.Equal("1 program opens with Windows; 1 more program is switched off.", StartupSummary.Line(Many(1, 1)));
        Assert.Equal("3 programs open with Windows; 2 more programs are switched off.", StartupSummary.Line(Many(3, 2)));
        Assert.Equal("No program opens with Windows; 1 program is switched off.", StartupSummary.Line(Many(0, 1)));
        Assert.Equal("No program opens with Windows; 4 programs are switched off.", StartupSummary.Line(Many(0, 4)));
        Assert.Empty(Loc.Missing);
    }

    [Fact]
    public void Every_text_of_the_engine_has_an_English_entry()
    {
        var texts = new List<string>();

        // the reasons, from the guard (the protected list and the Windows folder share one hedged sentence)
        texts.Add(StartupGuard.LockReason(@"C:\Apps\Antilag.exe", null)!);
        texts.Add(StartupGuard.LockReason(@"C:\Apps\avp.exe", null)!);
        texts.Add(StartupGuard.LockReason(@"C:\Apps\RtkAudUService64.exe", null)!);
        texts.Add(StartupGuard.LockReason(@"C:\Fake\Win\System32\sometool.exe", @"C:\Fake\Win", _ => null)!);
        Assert.Equal("It may be needed for your computer to work, so it can't be changed here.", texts[2]);
        Assert.Equal(texts[2], texts[3]);

        // the notes
        foreach (var s in Enum.GetValues<StartupSource>()) texts.Add(StartupSummary.SourceNote(s));

        // the messages of the exceptions
        _sb.Put(_sb.UserRun, "LockSelf", StartupSandbox.Quoted(_sb.Exe("Antilag.exe")));
        _sb.Put(_sb.UserRun, "Gone", _sb.Exe("gone.exe"));
        _sb.Put(_sb.UserRun, "Free", _sb.Exe("free.exe"));
        _sb.Put(_sb.MachineRun, "Shared", _sb.Exe("shared.exe"));
        var store = _sb.Store();
        var list = store.List();
        var locked = list.Single(e => e.Name == "LockSelf");
        var gone = list.Single(e => e.Name == "Gone");
        var free = list.Single(e => e.Name == "Free");
        var shared = list.Single(e => e.Name == "Shared");
        Assert.Equal(locked.LockedReason, texts[0]);                      // the list shows the same sentence in the same language
        texts.Add(Assert.Throws<StartupException>(() => store.SetEnabled(locked, false)).Message);
        _sb.Remove(_sb.UserRun, "Gone");
        texts.Add(Assert.Throws<StartupException>(() => store.SetEnabled(gone, false)).Message);
        texts.Add(Assert.Throws<StartupException>(() => new WindowsStartupStore(_sb.Locations) { GameRunning = () => true }.SetEnabled(free, false)).Message);
        var broken = new WindowsStartupStore(_sb.Locations with { ApprovedUser = new RegistryPlace(RegistryHive.CurrentUser, _sb.RootKey + "\\" + new string('x', 300)) });
        texts.Add(Assert.Throws<StartupException>(() => broken.SetEnabled(free, false)).Message);
        var refused = new WindowsStartupStore(_sb.Locations) { FlagWriter = (_, _, _, _) => throw new UnauthorizedAccessException() };      // HKLM without administrator rights
        var needsAdmin = Assert.Throws<StartupException>(() => refused.SetEnabled(shared, false)).Message;
        Assert.Equal("Windows didn't allow this change (administrator rights may be needed); nothing was changed.", needsAdmin);
        texts.Add(needsAdmin);

        // the sentences about the list
        texts.Add(StartupSummary.Line(Many(0, 0)));
        texts.Add(StartupSummary.Line(Many(2, 0)));
        texts.Add(StartupSummary.Line(Many(2, 1)));
        texts.Add(StartupSummary.Line(Many(0, 3)));

        Assert.Empty(Loc.Missing);
        Assert.All(texts, t =>
        {
            Assert.False(string.IsNullOrWhiteSpace(t));
            Assert.False(TurkishLetter.IsMatch(t), "still Turkish: " + t);
        });
    }
}

/// <summary>Reads the real list of this PC, and only reads. Nothing is switched.</summary>
[Collection("Sequential")]
public class StartupLiveTests
{
    private readonly ITestOutputHelper _out;
    public StartupLiveTests(ITestOutputHelper output) => _out = output;

    private static bool GameRunning()
    {
        var found = Process.GetProcessesByName("cs2");
        try { return found.Length > 0; }
        finally { foreach (var p in found) p.Dispose(); }
    }

    [Fact]
    public void The_real_list_of_this_PC_is_plausible()
    {
        if (GameRunning()) return;                       // nothing of the app reads the machine while the game runs
        var store = new WindowsStartupStore();
        var watch = Stopwatch.StartNew();
        var list = store.List();
        watch.Stop();

        _out.WriteLine($"{list.Count} entries in {watch.ElapsedMilliseconds} ms");
        foreach (var e in list)
            _out.WriteLine($"{(e.Enabled ? "on " : "off")} {(e.Locked ? "LOCKED" : "      ")} {e.Source,-12} {e.DisplayName}  [{e.Name}]  {e.ExePath ?? "-"}  {e.LockedReason}");

        Assert.NotNull(list);
        foreach (var e in list)
        {
            Assert.False(string.IsNullOrWhiteSpace(e.Name));
            Assert.False(string.IsNullOrWhiteSpace(e.Id));
            Assert.False(string.IsNullOrWhiteSpace(e.DisplayName));
            Assert.False(string.IsNullOrWhiteSpace(e.Command));
            Assert.True(e.ExePath == null || e.ExePath.Length > 0);
            Assert.Equal(e.Locked, e.LockedReason.Length > 0);
        }
        Assert.Equal(list.Count, list.Select(e => e.Id).Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(list.Select(e => e.Id).ToArray(), list.OrderBy(e => e.DisplayName, StringComparer.OrdinalIgnoreCase).Select(e => e.Id).ToArray());
        Assert.True(watch.ElapsedMilliseconds < 10_000, "listing took " + watch.ElapsedMilliseconds + " ms");
    }

    [Fact]
    public void Two_readings_in_a_row_give_the_same_list()
    {
        if (GameRunning()) return;
        var store = new WindowsStartupStore();
        Assert.Equal(store.List().Select(e => e.Id + "|" + e.Enabled).ToArray(), store.List().Select(e => e.Id + "|" + e.Enabled).ToArray());
    }
}
