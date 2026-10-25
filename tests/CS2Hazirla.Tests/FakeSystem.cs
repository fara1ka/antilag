using CS2Hazirla.Core;
using Microsoft.Win32;

namespace CS2Hazirla.Tests;

public class FakeService
{
    public string Display = "";
    public ServiceRunState State = ServiceRunState.Running;
    public ServiceStart Start = ServiceStart.Automatic;
    public bool CanStop = true;
    public List<string> Dependents = new();
}

/// <summary>In-memory ISystem. When Frozen, any mutating call throws: that is how dry-run is verified.</summary>
public class FakeSystem : ISystem
{
    public bool Frozen;
    public List<string> Mutations = new();
    public List<int> KilledPids = new();
    public HashSet<int> Stubborn = new();      // ignore polite close
    public HashSet<int> UnkillablePids = new();   // survive Kill as well
    public HashSet<int> ThrowOnKillPids = new();  // Kill throws (access denied)
    public Dictionary<int, int> LingerKills = new();  // pid -> how many Kill calls are accepted without the process going away (it is "still being torn down")
    public int Session = 1;
    public int SelfPid = 4242;
    private int _nextPid = 1000;

    public List<ProcInfo> Procs = new();
    public Dictionary<string, FakeService> Services = new(StringComparer.OrdinalIgnoreCase);
    public string ActivePower = "381b4222-f694-41f0-9685-ff5bb260df2e";
    public List<PowerScheme> Schemes = new() { new("381b4222-f694-41f0-9685-ff5bb260df2e", "Balanced") };
    public Dictionary<string, RegValue> Reg = new(StringComparer.OrdinalIgnoreCase);
    public HashSet<string> Keys = new(StringComparer.OrdinalIgnoreCase);
    public MouseParams Mouse = new(6, 10, 1);
    public Dictionary<string, bool> NicPower = new() { ["PCI\\NIC0_0"] = true };
    public List<NicInfo> Nics = new() { new("Ethernet", "{AAAA-BBBB}", "PCI\\NIC0", "PCI\\NIC0_0", true, false) };
    public MemoryInfo Memory = new(32L << 30, 10L << 30, 6L << 30);
    public AccessibilityFlags Acc = new(0x1 | 0x4 | 0x8 | 0x2, 0x1 | 0x4 | 0x8, 0x4 | 0x8);
    public bool Flushed;

    private static string K(RegHive h, string key, string name) => $"{h}|{key}|{name}";
    private static string KK(RegHive h, string key) => $"{h}|{key}";

    public FakeProc AddProc(string name, string? path = null, bool window = false, int mb = 100)
    {
        var p = new ProcInfo(_nextPid++, name, path ?? $@"C:\Program Files\{name}\{name}.exe", Session, window, mb * 1048576L);
        Procs.Add(p);
        return new FakeProc(p);
    }

    public record FakeProc(ProcInfo Info);

    private void Mutate(string what)
    {
        if (Frozen) throw new InvalidOperationException("DRY-RUN VIOLATION: " + what);
        // same rule as WindowsSystem.Gate(): no state change while cs2.exe exists
        if (Procs.Any(p => p.Name.Equals("cs2", StringComparison.OrdinalIgnoreCase))) throw new GameRunningException();
        Mutations.Add(what);
        OnMutation?.Invoke(what);
    }

    /// <summary>Called after every recorded change (lets a test start the game at an exact step of a run).</summary>
    public Action<string>? OnMutation;

    // ---- probes
    public int CurrentProcessId => SelfPid;
    public int CurrentSessionId => Session;
    /// <summary>Called at the start of every GetProcesses() with the number of calls so far (lets a test make a program "come back" at an exact moment).</summary>
    public Action<int>? OnGetProcesses;
    private int _getProcessCalls;
    public IReadOnlyList<ProcInfo> GetProcesses() { OnGetProcesses?.Invoke(++_getProcessCalls); return Procs.ToList(); }

    /// <summary>Names of probes that throw (a broken WMI / powercfg): "GetNics", "GetPower", "GetMouse"...</summary>
    public HashSet<string> BrokenProbes = new();
    /// <summary>What a broken probe throws (default: an English InvalidOperationException, like a real WMI failure).</summary>
    public Exception? ProbeException;
    private void Probe(string name) { if (BrokenProbes.Contains(name)) throw ProbeException ?? new InvalidOperationException(name + " is broken (test)"); }

    public ServiceInfo? GetService(string name)
    {
        if (!Services.TryGetValue(name, out var s)) return null;
        var running = s.Dependents.Where(d => Services.TryGetValue(d, out var ds) && ds.State == ServiceRunState.Running).ToList();
        return new ServiceInfo(name, s.Display, s.State, s.Start, s.CanStop, running);
    }

    public IReadOnlyList<(string Name, string DisplayName)> GetRunningServices() =>
        Services.Where(kv => kv.Value.State == ServiceRunState.Running).Select(kv => (kv.Key, kv.Value.Display)).ToList();

    public PowerState GetPower() { Probe("GetPower"); return new(ActivePower, Schemes.ToList()); }
    public RegValue? ReadRegistry(RegHive hive, string subKey, string name) => Reg.TryGetValue(K(hive, subKey, name), out var v) ? v : null;
    public bool RegistryKeyExists(RegHive hive, string subKey) => Keys.Contains(KK(hive, subKey));
    public MemoryInfo GetMemory() => Memory;
    public double SampleCpuIdlePercent(int sampleMs) => 90;
    public Task<PingStats> PingAsync(string host, int count, CancellationToken ct) =>
        Task.FromResult(new PingStats(host, count, count, 12, 1, 0));
    public IReadOnlyList<NicInfo> GetNics() { Probe("GetNics"); return GetNicsCore(); }
    private IReadOnlyList<NicInfo> GetNicsCore() => Nics.Select(n => n.PowerInstanceName == null ? n : n with { PowerSaveEnabled = NicPower[n.PowerInstanceName] }).ToList();
    public IReadOnlyList<RamModule> GetRamModules() => new[] { new RamModule(16L << 30, 6000, 6000) };
    /// <summary>What the "screen" runs at (null = Windows does not say).</summary>
    public DisplayInfo? Display;
    public DisplayInfo? GetPrimaryDisplay() { Probe("GetPrimaryDisplay"); return Display; }
    /// <summary>Totals per pid for the usage list; the pids that were asked for and are listed here come back, the others are "unreadable".</summary>
    public readonly Dictionary<int, UsageCounters> Usage = new();
    public readonly List<int> UsageAsked = new();
    public IReadOnlyDictionary<int, UsageCounters> GetUsageCounters(IReadOnlyCollection<int> pids)
    {
        UsageAsked.AddRange(pids);
        return pids.Where(Usage.ContainsKey).ToDictionary(p => p, p => Usage[p]);
    }
    public MouseParams GetMouse() { Probe("GetMouse"); return Mouse; }
    public AccessibilityFlags GetAccessibility() => Acc;
    public int FindPidCalls;
    public int? FindPidByName(string name)
    {
        FindPidCalls++;
        var p = Procs.FirstOrDefault(x => x.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        return p?.Pid;
    }

    // ---- mutations
    private bool Live(ProcRef r) => Procs.Any(p => p.Pid == r.Pid && p.Name == r.Name);

    /// <summary>Like WindowsSystem.StreamMode: in stream mode OBS/Discord/overlay tools are protected (production default: off).</summary>
    public bool StreamMode;
    public int CloseCalls;
    /// <summary>Called after every RequestClose with the number of calls so far (lets a test start the game "in the middle" of a batch).</summary>
    public Action<int>? AfterClose;
    public int RestartCalls;
    public Action<int>? AfterRestart;
    /// <summary>Paths RestartProcess refuses to start (the explorer hand-off failed).</summary>
    public HashSet<string> FailRestartPaths = new(StringComparer.OrdinalIgnoreCase);

    public void RequestClose(ProcRef r)
    {
        Mutate($"close {r.Name}");
        if (ProtectedList.IsProcessProtected(r.Name, null, StreamMode)) throw new InvalidOperationException("protected: " + r.Name);
        var p = Procs.FirstOrDefault(x => x.Pid == r.Pid && x.Name == r.Name);
        if (p != null && !Stubborn.Contains(r.Pid)) Procs.Remove(p);
        AfterClose?.Invoke(++CloseCalls);
    }

    public IReadOnlyList<ProcRef> WaitForExit(IEnumerable<ProcRef> procs, int timeoutMs) =>
        procs.Where(Live).ToList();

    public void Kill(ProcRef r)
    {
        Mutate($"kill {r.Name}");
        if (ThrowOnKillPids.Contains(r.Pid)) throw new InvalidOperationException("access denied (test)");
        if (ProtectedList.IsProcessProtected(r.Name, null, StreamMode)) throw new InvalidOperationException("protected: " + r.Name);
        var p = Procs.FirstOrDefault(x => x.Pid == r.Pid && x.Name == r.Name);
        if (p == null) return;
        if (UnkillablePids.Contains(r.Pid)) return;
        if (LingerKills.TryGetValue(r.Pid, out var linger) && linger > 0) { LingerKills[r.Pid] = linger - 1; return; }
        KilledPids.Add(r.Pid);
        Procs.Remove(p);
    }

    public void StopService(string name)
    {
        Mutate($"stop {name}");
        void Stop(string n)
        {
            var s = Services[n];
            foreach (var d in s.Dependents) if (Services[d].State == ServiceRunState.Running) Stop(d);
            s.State = ServiceRunState.Stopped;
        }
        Stop(name);
    }

    public void StartService(string name) { Mutate($"start {name}"); Services[name].State = ServiceRunState.Running; }

    public string DuplicatePowerScheme(string templateGuid)
    {
        Mutate($"dup {templateGuid}");
        var g = Guid.NewGuid().ToString();
        Schemes.Add(new PowerScheme(g, templateGuid == Catalog.UltimateTemplateGuid ? "Ultimate Performance" : "High performance"));
        return g;
    }

    public void SetActivePowerScheme(string guid) { Mutate($"setactive {guid}"); ActivePower = guid; }
    public void DeletePowerScheme(string guid)
    {
        Mutate($"delete {guid}");
        if (guid == ActivePower) throw new InvalidOperationException("cannot delete active plan");
        Schemes.RemoveAll(s => s.Guid == guid);
    }

    public void WriteRegistry(RegHive hive, string subKey, string name, RegistryValueKind kind, string data)
    {
        Mutate($"regset {subKey}\\{name}");
        Keys.Add(KK(hive, subKey));
        Reg[K(hive, subKey, name)] = new RegValue(kind, data);
    }

    public void DeleteRegistryValue(RegHive hive, string subKey, string name)
    {
        Mutate($"regdel {subKey}\\{name}");
        Reg.Remove(K(hive, subKey, name));
    }

    public void DeleteRegistryKeyIfEmpty(RegHive hive, string subKey)
    {
        Mutate($"keydel {subKey}");
        if (!Reg.Keys.Any(k => k.StartsWith(KK(hive, subKey) + "|", StringComparison.OrdinalIgnoreCase))) Keys.Remove(KK(hive, subKey));
    }

    public void FlushDns() { Mutate("dns"); Flushed = true; }
    public void SetNicPower(string inst, bool enabled) { Mutate($"nic {inst}={enabled}"); NicPower[inst] = enabled; }
    public void SetMouse(MouseParams p) { Mutate("mouse"); Mouse = p; }
    public void SetAccessibility(AccessibilityFlags f) { Mutate("accessibility"); Acc = f; }

    public void RestartProcess(string path)
    {
        Mutate($"restart {path}");
        if (FailRestartPaths.Contains(path)) throw new IOException("explorer could not start " + path);
        Procs.Add(new ProcInfo(_nextPid++, Path.GetFileNameWithoutExtension(path), path, Session, false, 50 << 20));
        AfterRestart?.Invoke(++RestartCalls);
    }


    /// <summary>Everything the app may change, as one comparable string.</summary>
    public string Snapshot()
    {
        var lines = new List<string>
        {
            "power:" + ActivePower,
            "schemes:" + string.Join(",", Schemes.Select(s => s.Guid).OrderBy(x => x)),
            "mouse:" + Mouse,
            "acc:" + Acc,
            "nic:" + string.Join(",", NicPower.OrderBy(x => x.Key).Select(x => $"{x.Key}={x.Value}")),
        };
        lines.AddRange(Services.OrderBy(x => x.Key).Select(x => $"svc:{x.Key}:{x.Value.State}:{x.Value.Start}"));
        lines.AddRange(Reg.OrderBy(x => x.Key).Select(x => $"reg:{x.Key}:{x.Value.Kind}:{x.Value.Data}"));
        lines.AddRange(Keys.OrderBy(x => x).Select(x => "key:" + x));
        lines.AddRange(Procs.Select(p => $"proc:{p.Name}:{p.Path}").OrderBy(x => x));
        return string.Join("\n", lines);
    }
}
