using CS2Hazirla.Core;
using Microsoft.Win32;
using Xunit;

namespace CS2Hazirla.Tests;

[CollectionDefinition("Sequential", DisableParallelization = true)]
public class SequentialCollection { }

/// <summary>A realistic "dirty" machine plus an isolated %APPDATA%\CS2Hazirla replacement.</summary>
public class TestEnv : IDisposable
{
    public const string Cs2Path = @"D:\steam\steamapps\common\Counter-Strike Global Offensive\game\bin\win64\cs2.exe";

    public readonly string Dir;
    public readonly string OldRoot;
    public readonly FakeSystem Sys = new();
    public readonly NullLog Log = new();
    public SessionStore Store = new();
    public Orchestrator Orc;
    public AppSettings Settings;

    public TestEnv(ProfileKind profile = ProfileKind.Maximum)
    {
        OldRoot = AppPaths.Root;
        StateGuard.FileExists = _ => true;          // fake processes have fake paths
        Dir = Path.Combine(Path.GetTempPath(), "cs2h-test-" + Guid.NewGuid().ToString("N"));
        AppPaths.Root = Dir;
        AppPaths.Ensure();

        // processes
        Sys.AddProc("chrome", window: true, mb: 900);
        Sys.AddProc("chrome", mb: 300);
        Sys.AddProc("msedge", window: true, mb: 400);
        Sys.AddProc("OneDrive", mb: 150);
        Sys.AddProc("Dropbox", mb: 120);
        Sys.AddProc("EpicGamesLauncher", window: true, mb: 200);
        Sys.AddProc("GoogleUpdate", mb: 20);
        Sys.AddProc("ArmouryCrate.UserSessionHelper", mb: 60);
        Sys.AddProc("Spotify", window: true, mb: 300);
        Sys.AddProc("Discord", mb: 400);
        Sys.AddProc("ms-teams", window: true, mb: 500);
        Sys.AddProc("RandomTrayThing", mb: 40);
        // keyboard / mouse / headset software (closable, restarted after the game)
        Sys.AddProc("lghub_agent", @"C:\Program Files\LGHUB\lghub_agent.exe", mb: 150);
        Sys.AddProc("SteelSeriesGGClient", @"C:\Program Files\SteelSeries\GG\SteelSeriesGGClient.exe", window: true, mb: 120);
        Sys.AddProc("HyperX NGENUITY", @"C:\Program Files\HyperX\NGENUITY\HyperX NGENUITY.exe", mb: 90);
        Sys.AddProc("iCUE", @"C:\Program Files\Corsair\CORSAIR iCUE Software\iCUE.exe", mb: 200);
        // protected / must-survive
        Sys.AddProc("steam", @"D:\steam\steam.exe", window: true);
        Sys.AddProc("steamwebhelper", @"D:\steam\bin\cef\steamwebhelper.exe");
        Sys.AddProc("NVDisplay.Container", @"C:\Program Files\NVIDIA Corporation\Display.NvContainer\NVDisplay.Container.exe");
        Sys.AddProc("svchost", @"C:\Windows\System32\svchost.exe");
        Sys.AddProc("explorer", @"C:\Windows\explorer.exe", window: true);
        Sys.AddProc("obs64", @"C:\Program Files\obs-studio\bin\64bit\obs64.exe", window: true);
        Sys.AddProc("FACEITClient", @"C:\Program Files\FACEIT\FACEITClient.exe");
        Sys.AddProc("MsMpEng", @"C:\ProgramData\Microsoft\Windows Defender\Platform\MsMpEng.exe");
        Sys.AddProc("notepad", @"C:\Windows\System32\notepad.exe", window: true);

        // services
        foreach (var (n, d) in new[] { ("wuauserv", "Windows Update"), ("UsoSvc", "Update Orchestrator"), ("BITS", "BITS"),
                     ("DoSvc", "Delivery Optimization"), ("WSearch", "Windows Search"), ("Spooler", "Print Spooler"),
                     ("DiagTrack", "Connected User Experiences"), ("XblAuthManager", "Xbox Live Auth"),
                     ("XblGameSave", "Xbox Live Game Save"), ("XboxNetApiSvc", "Xbox Live Networking") })
            Sys.Services[n] = new FakeService { Display = d, Start = n is "DiagTrack" or "WSearch" ? ServiceStart.Automatic : ServiceStart.Manual };
        Sys.Services["Spooler"].Dependents.Add("PrintNotify");
        Sys.Services["PrintNotify"] = new FakeService { Display = "Printer Extensions", Start = ServiceStart.Manual };
        Sys.Services["Audiosrv"] = new FakeService { Display = "Windows Audio" };

        // registry: Toast already exists as 1, Layers exists with another flag, GameBar key absent
        Sys.Keys.Add("CurrentUser|Software\\Microsoft\\Windows\\CurrentVersion\\PushNotifications");
        Sys.Reg["CurrentUser|Software\\Microsoft\\Windows\\CurrentVersion\\PushNotifications|ToastEnabled"] = new RegValue(RegistryValueKind.DWord, "1");
        Sys.Keys.Add("CurrentUser|" + Planner.LayersKey);
        Sys.Reg[$"CurrentUser|{Planner.LayersKey}|{Cs2Path}"] = new RegValue(RegistryValueKind.String, "~ RUNASADMIN");
        Sys.Keys.Add("CurrentUser|" + Planner.GpuPrefKey);

        Settings = new AppSettings();
        Settings.ApplyProfile(profile);
        Orc = new Orchestrator(Sys, Store, Log) { SettleMs = 0 };
    }

    /// <summary>Simulates restarting the app: new store/orchestrator, same disk and same machine.</summary>
    public Orchestrator Restart()
    {
        Store = new SessionStore();
        Orc = new Orchestrator(Sys, Store, Log) { SettleMs = 0 };
        return Orc;
    }

    public Plan BuildPlan() => new Planner(Sys).Build(Settings, Cs2Path);

    public void Dispose()
    {
        AppPaths.Root = OldRoot;
        StateGuard.FileExists = File.Exists;
        try { Directory.Delete(Dir, true); } catch { }
    }
}
