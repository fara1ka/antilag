using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Management;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.ServiceProcess;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace CS2Hazirla.Core;

/// <summary>Real ISystem. Only standard Windows APIs, powercfg, registry, SCM and WMI are used.</summary>
public sealed class WindowsSystem : ISystem
{
    private static readonly Regex GuidRx = new(@"[0-9a-fA-F]{8}-(?:[0-9a-fA-F]{4}-){3}[0-9a-fA-F]{12}", RegexOptions.Compiled);
    private readonly int _sessionId = Process.GetCurrentProcess().SessionId;

    /// <summary>Set by the app from the active profile so the last-chance kill check includes stream protections.</summary>
    public bool StreamMode { get; set; }

    public static bool IsElevated()
    {
        using var id = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(id).IsInRole(WindowsBuiltInRole.Administrator);
    }

    public int CurrentProcessId => Environment.ProcessId;
    public int CurrentSessionId => _sessionId;

    // ---------------------------------------------------------------- processes

    /// <summary>
    /// Enumerates processes from the system snapshot. For every process whose NAME is on the protected list
    /// (CS2, Steam, anti-cheat, Windows core, ...) no handle is opened and none of its windows is queried:
    /// those entries carry only name / pid / session / working set from the snapshot.
    /// </summary>
    public IReadOnlyList<ProcInfo> GetProcesses()
    {
        var procs = Process.GetProcesses();
        var list = new List<ProcInfo>(procs.Length);
        try
        {
            // while the game is up NO handle is opened to anything and no window is queried: snapshot data only
            var gameUp = procs.Any(p => { try { return p.ProcessName.Equals(Catalog.Cs2ProcessName, StringComparison.OrdinalIgnoreCase); } catch { return false; } });

            var untouchable = new HashSet<int>();
            foreach (var p in procs)
            {
                try { if (gameUp || ProtectedList.IsProcessProtected(p.ProcessName, null, false)) untouchable.Add(p.Id); }
                catch { untouchable.Add(p.Id); }
            }

            var windowed = WindowedPids(untouchable);
            foreach (var p in procs)
            {
                try
                {
                    if (!untouchable.Contains(p.Id) && HasEnded(p)) continue;        // ended, only a handle elsewhere keeps the entry
                    var path = untouchable.Contains(p.Id) ? null : GetPath(p.Id);
                    long ws = 0;
                    try { ws = p.WorkingSet64; } catch { }
                    list.Add(new ProcInfo(p.Id, p.ProcessName, path, p.SessionId, windowed.Contains(p.Id), ws));
                }
                catch { /* process exited while enumerating */ }
            }
        }
        finally { foreach (var p in procs) p.Dispose(); }
        return list;
    }

    public int? FindPidByName(string name)
    {
        var ps = Process.GetProcessesByName(name);   // snapshot-based: no handle is opened
        try { return ps.Length > 0 ? ps[0].Id : null; }
        finally { foreach (var p in ps) p.Dispose(); }
    }

    private static HashSet<int> WindowedPids(HashSet<int> untouchable)
    {
        var set = new HashSet<int>();
        Native.EnumWindows((h, _) =>
        {
            // owner pid first (a pure lookup), so nothing is ever queried on windows of untouchable processes
            Native.GetWindowThreadProcessId(h, out var pid);
            if (untouchable.Contains((int)pid)) return true;
            if (!Native.IsWindowVisible(h)) return true;
            if (Native.GetWindow(h, Native.GW_OWNER) != IntPtr.Zero) return true;
            var ex = Native.GetWindowLong(h, Native.GWL_EXSTYLE);
            if ((ex & Native.WS_EX_TOOLWINDOW) != 0 && (ex & Native.WS_EX_APPWINDOW) == 0) return true;
            if (Native.GetWindowTextLength(h) == 0) return true;
            set.Add((int)pid);
            return true;
        }, IntPtr.Zero);
        return set;
    }

    private static string? GetPath(int pid)
    {
        var h = Native.OpenProcess(Native.PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
        if (h == IntPtr.Zero) return null;
        try
        {
            var sb = new StringBuilder(1024);
            var size = sb.Capacity;
            return Native.QueryFullProcessImageName(h, 0, sb, ref size) ? sb.ToString() : null;
        }
        finally { Native.CloseHandle(h); }
    }

    /// <summary>Every state-changing call is refused while cs2.exe exists (there is no bypass).</summary>
    private void Gate()
    {
        if (FindPidByName(Catalog.Cs2ProcessName) != null) throw new GameRunningException();
    }

    /// <summary>
    /// A process that has ended but is still held by a handle in another process (a monitor, a service, a virus scanner) stays in the system list for as
    /// long as that handle lives, with no threads. It is gone for every practical purpose; counting it as "running" made an overlay that HAD been ended show
    /// up as "not closed". Snapshot data only, no handle is opened.
    /// </summary>
    public static bool HasEnded(Process p)
    {
        try { return p.Threads.Count == 0; }
        catch { return false; }
    }

    /// <summary>True only when a live process has exactly this pid AND this name (snapshot lookup, no handle).</summary>
    private static bool IsLive(ProcRef r)
    {
        Process[] ps;
        try { ps = Process.GetProcessesByName(r.Name); } catch { return false; }
        try { return ps.Any(p => p.Id == r.Pid && !HasEnded(p)); }
        finally { foreach (var p in ps) p.Dispose(); }
    }

    public void RequestClose(ProcRef r)
    {
        Gate();
        // identity + protection are verified from the snapshot BEFORE any handle is opened
        if (ProtectedList.IsProcessProtected(r.Name, null, StreamMode)) throw new InvalidOperationException($"{r.Name} korumalı: dokunulmadı.");
        if (!IsLive(r)) return;

        // ask EVERY visible top-level window of this process to close (CloseMainWindow only reaches one)
        var sent = 0;
        Native.EnumWindows((h, _) =>
        {
            Native.GetWindowThreadProcessId(h, out var pid);
            if ((int)pid != r.Pid || !Native.IsWindowVisible(h)) return true;
            if (Native.PostMessage(h, Native.WM_CLOSE, IntPtr.Zero, IntPtr.Zero)) sent++;
            return true;
        }, IntPtr.Zero);

        if (sent == 0)
        {
            using var p = Process.GetProcessById(r.Pid);
            p.CloseMainWindow();   // no visible window: usually a no-op, the caller falls back to Kill for non-windowed apps
        }
    }

    public IReadOnlyList<ProcRef> WaitForExit(IEnumerable<ProcRef> procs, int timeoutMs)
    {
        var sw = Stopwatch.StartNew();
        var left = procs.Where(IsLive).ToList();
        while (left.Count > 0 && sw.ElapsedMilliseconds < timeoutMs)
        {
            Thread.Sleep(150);
            left = left.Where(IsLive).ToList();
        }
        return left;
    }

    public void Kill(ProcRef r)
    {
        Gate();
        if (ProtectedList.IsProcessProtected(r.Name, null, StreamMode)) throw new InvalidOperationException($"{r.Name} korumalı: kapatılmadı.");
        if (!IsLive(r)) return;
        // path-based protection (Windows folder, Steam/anti-cheat install folders) is checked right before terminating
        if (ProtectedList.IsProcessProtected(r.Name, GetPath(r.Pid), StreamMode))
            throw new InvalidOperationException($"{r.Name} korumalı: kapatılmadı.");
        using var p = Process.GetProcessById(r.Pid);
        p.Kill();
    }

    public void RestartProcess(string path)
    {
        Gate();
        ShellOpen(path);
    }

    private static readonly string ExplorerExe = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe");

    private static void ShellOpen(string target)
    {
        // explorer.exe hands the target to the already-running (non-elevated) shell → de-elevated start.
        // Full path on purpose: a bare name could resolve to a planted file with this admin token.
        Process.Start(new ProcessStartInfo(ExplorerExe, $"\"{target}\"") { UseShellExecute = false, CreateNoWindow = true });
    }

    // ---------------------------------------------------------------- services

    public ServiceInfo? GetService(string name)
    {
        try
        {
            using var sc = new ServiceController(name);
            var status = sc.Status;   // throws when the service does not exist
            var deps = new List<string>();
            CollectRunningDependents(sc, deps);
            return new ServiceInfo(sc.ServiceName, sc.DisplayName, Map(status), Map(sc.StartType), sc.CanStop, deps);
        }
        catch (InvalidOperationException) { return null; }
        catch (Win32Exception) { return null; }
    }

    public IReadOnlyList<(string Name, string DisplayName)> GetRunningServices()
    {
        var list = new List<(string, string)>();
        foreach (var sc in ServiceController.GetServices())
        {
            // protected services (Steam, FACEIT, drivers, Windows core) are never offered, so their status is not even read
            if (ProtectedList.IsServiceProtected(sc.ServiceName)) { sc.Dispose(); continue; }
            try { if (sc.Status == ServiceControllerStatus.Running) list.Add((sc.ServiceName, sc.DisplayName)); }
            catch { /* service vanished or is not readable */ }
            finally { sc.Dispose(); }
        }
        return list;
    }

    private static void CollectRunningDependents(ServiceController sc, List<string> acc)
    {
        foreach (var d in sc.DependentServices)
        {
            if (d.Status == ServiceControllerStatus.Stopped) continue;
            if (acc.Contains(d.ServiceName)) continue;
            acc.Add(d.ServiceName);
            CollectRunningDependents(d, acc);
        }
    }

    private static ServiceRunState Map(ServiceControllerStatus s) => s switch
    {
        ServiceControllerStatus.Running => ServiceRunState.Running,
        ServiceControllerStatus.Stopped => ServiceRunState.Stopped,
        _ => ServiceRunState.Other,
    };

    private static ServiceStart Map(ServiceStartMode m) => m switch
    {
        ServiceStartMode.Boot => ServiceStart.Boot,
        ServiceStartMode.System => ServiceStart.System,
        ServiceStartMode.Automatic => ServiceStart.Automatic,
        ServiceStartMode.Manual => ServiceStart.Manual,
        ServiceStartMode.Disabled => ServiceStart.Disabled,
        _ => ServiceStart.Unknown,
    };

    public void StopService(string name)
    {
        Gate();
        using var sc = new ServiceController(name);
        StopRecursive(sc);
    }

    private static void StopRecursive(ServiceController sc)
    {
        foreach (var d in sc.DependentServices)
            if (d.Status != ServiceControllerStatus.Stopped) StopRecursive(d);
        sc.Refresh();
        if (sc.Status == ServiceControllerStatus.Running) sc.Stop();
        sc.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(25));
    }

    public void StartService(string name)
    {
        Gate();
        using var sc = new ServiceController(name);
        sc.Refresh();
        if (sc.Status == ServiceControllerStatus.Running) return;
        if (sc.Status != ServiceControllerStatus.StartPending) sc.Start();
        sc.WaitForStatus(ServiceControllerStatus.Running, TimeSpan.FromSeconds(30));
    }

    // ---------------------------------------------------------------- power plan

    private static string Run(string exe, string args, int timeoutMs = 20000)
    {
        // full System32 path: a bare name could resolve to a planted executable and run it with this admin token
        if (!Path.IsPathRooted(exe)) exe = Path.Combine(Environment.SystemDirectory, exe + ".exe");
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        Encoding enc;
        try { enc = Encoding.GetEncoding((int)Native.GetOEMCP()); }          // not CurrentCulture: in English the app sets it to en-US, whatever Windows speaks
        catch { enc = Encoding.UTF8; }
        var psi = new ProcessStartInfo(exe, args)
        {
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true,
            StandardOutputEncoding = enc, StandardErrorEncoding = enc,
        };
        using var p = Process.Start(psi)!;
        var stdout = p.StandardOutput.ReadToEndAsync();
        var stderr = p.StandardError.ReadToEndAsync();
        if (!p.WaitForExit(timeoutMs)) { try { p.Kill(); } catch { } throw new System.TimeoutException($"{exe} zaman aşımı"); }
        var o = stdout.Result;
        if (p.ExitCode != 0) throw new InvalidOperationException($"{exe} {args} → {p.ExitCode}: {(stderr.Result + o).Trim()}");
        return o;
    }

    public PowerState GetPower()
    {
        var active = GuidRx.Match(Run("powercfg", "/getactivescheme")).Value;
        var schemes = new List<PowerScheme>();
        foreach (var line in Run("powercfg", "/list").Split('\n'))
        {
            var m = GuidRx.Match(line);
            if (!m.Success) continue;
            var nm = Regex.Match(line, @"\(([^)]*)\)");
            schemes.Add(new PowerScheme(m.Value.ToLowerInvariant(), nm.Success ? nm.Groups[1].Value : ""));
        }
        return new PowerState(active.ToLowerInvariant(), schemes);
    }

    public string DuplicatePowerScheme(string templateGuid)
    {
        Gate();
        var m = GuidRx.Match(Run("powercfg", $"-duplicatescheme {templateGuid}"));
        if (!m.Success) throw new InvalidOperationException("powercfg yeni plan GUID'i döndürmedi.");
        return m.Value.ToLowerInvariant();
    }

    public void SetActivePowerScheme(string guid) { Gate(); Run("powercfg", $"/setactive {RequireGuid(guid)}"); }

    public void DeletePowerScheme(string guid) { Gate(); Run("powercfg", $"/delete {RequireGuid(guid)}"); }

    private static string RequireGuid(string g) =>
        Guid.TryParse(g, out var parsed) ? parsed.ToString() : throw new ArgumentException("Geçersiz güç planı GUID'i.");

    // ---------------------------------------------------------------- registry

    private static RegistryKey Base(RegHive h) =>
        RegistryKey.OpenBaseKey(h == RegHive.CurrentUser ? RegistryHive.CurrentUser : RegistryHive.LocalMachine, RegistryView.Registry64);

    public RegValue? ReadRegistry(RegHive hive, string subKey, string name)
    {
        using var b = Base(hive);
        using var k = b.OpenSubKey(subKey);
        if (k == null) return null;
        var v = k.GetValue(name, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
        if (v == null) return null;
        var kind = k.GetValueKind(name);
        return new RegValue(kind, RegCodec.Encode(kind, v));
    }

    public bool RegistryKeyExists(RegHive hive, string subKey)
    {
        using var b = Base(hive);
        using var k = b.OpenSubKey(subKey);
        return k != null;
    }

    public void WriteRegistry(RegHive hive, string subKey, string name, RegistryValueKind kind, string data)
    {
        Gate();
        using var b = Base(hive);
        using var k = b.CreateSubKey(subKey, true) ?? throw new InvalidOperationException($"Anahtar açılamadı: {subKey}");
        k.SetValue(name, RegCodec.Decode(kind, data), kind);
    }

    public void DeleteRegistryValue(RegHive hive, string subKey, string name)
    {
        Gate();
        using var b = Base(hive);
        using var k = b.OpenSubKey(subKey, true);
        k?.DeleteValue(name, false);
    }

    public void DeleteRegistryKeyIfEmpty(RegHive hive, string subKey)
    {
        Gate();
        using var b = Base(hive);
        using (var k = b.OpenSubKey(subKey))
        {
            if (k == null || k.ValueCount > 0 || k.SubKeyCount > 0) return;
        }
        var idx = subKey.LastIndexOf('\\');
        if (idx < 0) { b.DeleteSubKey(subKey, false); return; }
        using var parent = b.OpenSubKey(subKey[..idx], true);
        parent?.DeleteSubKey(subKey[(idx + 1)..], false);
    }

    // ---------------------------------------------------------------- memory / cpu

    public MemoryInfo GetMemory()
    {
        var m = new Native.MemoryStatusEx { dwLength = (uint)Marshal.SizeOf<Native.MemoryStatusEx>() };
        Native.GlobalMemoryStatusEx(ref m);
        return new MemoryInfo((long)m.ullTotalPhys, (long)m.ullAvailPhys, ReadStandbyBytes());
    }

    private static long ReadStandbyBytes()
    {
        // SYSTEM_MEMORY_LIST_INFORMATION: 5 counters, PageCountByPriority[8], RepurposedPagesByPriority[8], 1 counter
        var size = IntPtr.Size * 22;
        var buf = Marshal.AllocHGlobal(size);
        try
        {
            var status = Native.NtQuerySystemInformation(Native.SystemMemoryListInformation, buf, size, out _);
            if (status != 0) return 0;
            long pages = 0;
            for (var i = 0; i < 8; i++)
                pages += Marshal.ReadIntPtr(buf, IntPtr.Size * (5 + i)).ToInt64();
            return pages * 4096;
        }
        catch { return 0; }
        finally { Marshal.FreeHGlobal(buf); }
    }

    public double SampleCpuIdlePercent(int sampleMs)
    {
        Native.GetSystemTimes(out var i1, out var k1, out var u1);
        Thread.Sleep(sampleMs);
        Native.GetSystemTimes(out var i2, out var k2, out var u2);
        double idle = i2 - i1, total = (k2 - k1) + (u2 - u1);
        return total <= 0 ? 0 : Math.Clamp(idle / total * 100.0, 0, 100);
    }

    public IReadOnlyList<RamModule> GetRamModules()
    {
        var list = new List<RamModule>();
        using var s = new ManagementObjectSearcher("SELECT Capacity, Speed, ConfiguredClockSpeed FROM Win32_PhysicalMemory");
        foreach (ManagementBaseObject o in s.Get())
        {
            list.Add(new RamModule(
                o["Capacity"] is ulong cap ? (long)cap : 0,
                o["Speed"] is uint sp ? (int)sp : null,
                o["ConfiguredClockSpeed"] is uint cc ? (int)cc : null));
        }
        return list;
    }

    // ---------------------------------------------------------------- network

    public async Task<PingStats> PingAsync(string host, int count, CancellationToken ct)
    {
        var rtts = new List<double>();
        string? error = null;
        var sent = 0;
        // an unreachable target (ICMP blocked, no connection) must not hold the run up: give up after 3 failures in a row or ~6 seconds
        var failedInARow = 0;
        var budget = Stopwatch.StartNew();
        using var ping = new Ping();
        for (var i = 0; i < count && failedInARow < 3 && budget.Elapsed < TimeSpan.FromSeconds(6); i++)
        {
            ct.ThrowIfCancellationRequested();
            sent++;
            try
            {
                var reply = await ping.SendPingAsync(host, 1000);
                if (reply.Status == IPStatus.Success) { rtts.Add(reply.RoundtripTime); failedInARow = 0; }
                else { error ??= reply.Status.ToString(); failedInARow++; }
            }
            catch (PingException ex)
            {
                error ??= ex.InnerException?.Message ?? ex.Message;
                break;   // unresolvable host etc.: no point repeating
            }
            catch (Exception ex) { error ??= ex.Message; break; }
            try { await Task.Delay(100, ct); } catch (OperationCanceledException) { throw; }
        }

        if (rtts.Count == 0)
            return new PingStats(host, sent, 0, 0, 0, 100, error ?? "yanıt yok");

        double jitter = 0;
        for (var i = 1; i < rtts.Count; i++) jitter += Math.Abs(rtts[i] - rtts[i - 1]);
        jitter = rtts.Count > 1 ? jitter / (rtts.Count - 1) : 0;
        return new PingStats(host, sent, rtts.Count, rtts.Average(), jitter, (sent - rtts.Count) * 100.0 / sent);
    }

    public void FlushDns() { Gate(); Run("ipconfig", "/flushdns"); }

    public IReadOnlyList<NicInfo> GetNics()
    {
        var power = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        try
        {
            using var ps = new ManagementObjectSearcher(@"root\wmi", "SELECT InstanceName, Enable FROM MSPower_DeviceEnable");
            foreach (ManagementBaseObject o in ps.Get())
                if (o["InstanceName"] is string inst && o["Enable"] is bool en) power[inst] = en;
        }
        catch { /* adapter has no power management info */ }

        var wireless = NetworkInterface.GetAllNetworkInterfaces()
            .Where(n => n.NetworkInterfaceType == NetworkInterfaceType.Wireless80211 && n.OperationalStatus == OperationalStatus.Up)
            .Select(n => n.Id.ToLowerInvariant()).ToHashSet();

        var list = new List<NicInfo>();
        try
        {
            using var s = new ManagementObjectSearcher(
                "SELECT Name, NetConnectionID, PNPDeviceID, GUID FROM Win32_NetworkAdapter WHERE PhysicalAdapter = TRUE AND NetEnabled = TRUE");
            foreach (ManagementBaseObject o in s.Get())
            {
                var pnp = o["PNPDeviceID"] as string ?? "";
                var guid = o["GUID"] as string ?? "";
                if (pnp.Length == 0 || guid.Length == 0) continue;
                var inst = power.Keys.FirstOrDefault(k => k.StartsWith(pnp, StringComparison.OrdinalIgnoreCase));
                list.Add(new NicInfo(
                    (o["NetConnectionID"] as string) ?? (o["Name"] as string) ?? pnp,
                    guid, pnp, inst, inst != null ? power[inst] : null, wireless.Contains(guid.ToLowerInvariant())));
            }
        }
        catch { /* WMI unavailable: no adapter list (the network step is then simply empty) */ }
        return list;
    }

    public void SetNicPower(string powerInstanceName, bool enabled)
    {
        Gate();
        using var s = new ManagementObjectSearcher(@"root\wmi", "SELECT * FROM MSPower_DeviceEnable");
        foreach (ManagementObject o in s.Get())
        {
            if (!string.Equals(o["InstanceName"] as string, powerInstanceName, StringComparison.OrdinalIgnoreCase)) continue;
            o["Enable"] = enabled;
            o.Put();
            return;
        }
        throw new InvalidOperationException("Adaptör güç yönetimi kaydı bulunamadı.");
    }

    // ---------------------------------------------------------------- mouse / keyboard accessibility

    public MouseParams GetMouse()
    {
        var a = new int[3];
        if (!Native.SystemParametersInfo(Native.SPI_GETMOUSE, 0, a, 0)) throw new Win32Exception();
        return new MouseParams(a[0], a[1], a[2]);
    }

    public void SetMouse(MouseParams p)
    {
        Gate();
        // flags = 0: running session only. No SPIF_UPDATEINIFILE (nothing persisted) and no SPIF_SENDCHANGE
        // (no WM_SETTINGCHANGE broadcast reaching every top-level window, Steam and game windows included).
        var a = new[] { p.Threshold1, p.Threshold2, p.Accel };
        if (!Native.SystemParametersInfo(Native.SPI_SETMOUSE, 0, a, 0)) throw new Win32Exception();
    }

    public AccessibilityFlags GetAccessibility()
    {
        var sk = new Native.StickyKeys { cbSize = (uint)Marshal.SizeOf<Native.StickyKeys>() };
        var tk = new Native.ToggleKeys { cbSize = (uint)Marshal.SizeOf<Native.ToggleKeys>() };
        var fk = new Native.FilterKeys { cbSize = (uint)Marshal.SizeOf<Native.FilterKeys>() };
        if (!Native.SystemParametersInfo(Native.SPI_GETSTICKYKEYS, sk.cbSize, ref sk, 0)) throw new Win32Exception();
        if (!Native.SystemParametersInfo(Native.SPI_GETTOGGLEKEYS, tk.cbSize, ref tk, 0)) throw new Win32Exception();
        if (!Native.SystemParametersInfo(Native.SPI_GETFILTERKEYS, fk.cbSize, ref fk, 0)) throw new Win32Exception();
        return new AccessibilityFlags(sk.dwFlags, tk.dwFlags, fk.dwFlags);
    }

    public void SetAccessibility(AccessibilityFlags f)
    {
        Gate();
        // flags = 0: session only (saved settings are never rewritten) and no WM_SETTINGCHANGE broadcast to other apps
        var sk = new Native.StickyKeys { cbSize = (uint)Marshal.SizeOf<Native.StickyKeys>(), dwFlags = f.Sticky };
        var tk = new Native.ToggleKeys { cbSize = (uint)Marshal.SizeOf<Native.ToggleKeys>(), dwFlags = f.Toggle };
        var fk = new Native.FilterKeys { cbSize = (uint)Marshal.SizeOf<Native.FilterKeys>() };
        if (!Native.SystemParametersInfo(Native.SPI_GETFILTERKEYS, fk.cbSize, ref fk, 0)) throw new Win32Exception();   // keep wait/delay/repeat/bounce
        fk.dwFlags = f.Filter;
        if (!Native.SystemParametersInfo(Native.SPI_SETSTICKYKEYS, sk.cbSize, ref sk, 0)) throw new Win32Exception();
        if (!Native.SystemParametersInfo(Native.SPI_SETTOGGLEKEYS, tk.cbSize, ref tk, 0)) throw new Win32Exception();
        if (!Native.SystemParametersInfo(Native.SPI_SETFILTERKEYS, fk.cbSize, ref fk, 0)) throw new Win32Exception();
    }
}
