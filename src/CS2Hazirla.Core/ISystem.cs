using Microsoft.Win32;

namespace CS2Hazirla.Core;

/// <summary>
/// The only way the app touches the operating system. Read-only probes come first; everything
/// below the "MUTATING" marker changes system state and must never be called from Planner (dry-run).
/// </summary>
public interface ISystem
{
    // ---------- read-only probes ----------
    int CurrentProcessId { get; }
    int CurrentSessionId { get; }
    IReadOnlyList<ProcInfo> GetProcesses();
    ServiceInfo? GetService(string name);
    /// <summary>Names + display names of currently running services (for the "pick from a list" dialog).</summary>
    IReadOnlyList<(string Name, string DisplayName)> GetRunningServices();
    PowerState GetPower();
    RegValue? ReadRegistry(RegHive hive, string subKey, string name);
    bool RegistryKeyExists(RegHive hive, string subKey);
    MemoryInfo GetMemory();
    double SampleCpuIdlePercent(int sampleMs);
    Task<PingStats> PingAsync(string host, int count, CancellationToken ct);
    IReadOnlyList<NicInfo> GetNics();
    IReadOnlyList<RamModule> GetRamModules();
    MouseParams GetMouse();
    AccessibilityFlags GetAccessibility();
    /// <summary>The main screen's size and refresh rate (now and the fastest it offers), or null when Windows does not say. Only reads.</summary>
    DisplayInfo? GetPrimaryDisplay();
    /// <summary>
    /// The totals (CPU time, bytes read and written) of these processes; one that cannot be read is simply missing. Only reads. A process whose NAME is on the
    /// protected list is never read (no handle is opened to it), whatever is asked; nothing is read while the game runs. (A program that is protected only by its folder is
    /// kept out of the question by the caller, which knows the paths.)
    /// </summary>
    IReadOnlyDictionary<int, UsageCounters> GetUsageCounters(IReadOnlyCollection<int> pids);

    /// <summary>
    /// Id of a running process with this name, or null. Built on the system process snapshot only:
    /// it opens NO handle to the process. This is the only call used while the game is running.
    /// </summary>
    int? FindPidByName(string name);

    // ---------- MUTATING ----------
    void RequestClose(ProcRef p);
    /// <summary>Waits up to timeoutMs; returns the processes that are still running. Uses snapshots only (no handles).</summary>
    IReadOnlyList<ProcRef> WaitForExit(IEnumerable<ProcRef> procs, int timeoutMs);
    void Kill(ProcRef p);
    // NOTE: there is deliberately NO method that touches the game process (priority, affinity, memory,
    // command line, handles). High priority is requested through the official "-high" launch option.

    /// <summary>Stops the service (and its running dependents first). Never changes the start type.</summary>
    void StopService(string name);
    void StartService(string name);

    string DuplicatePowerScheme(string templateGuid);
    void SetActivePowerScheme(string guid);
    void DeletePowerScheme(string guid);

    void WriteRegistry(RegHive hive, string subKey, string name, RegistryValueKind kind, string data);
    void DeleteRegistryValue(RegHive hive, string subKey, string name);
    void DeleteRegistryKeyIfEmpty(RegHive hive, string subKey);

    void FlushDns();
    void SetNicPower(string powerInstanceName, bool enabled);
    void SetMouse(MouseParams p);
    /// <summary>Applies Sticky/Toggle/Filter Keys flags to the running session only (nothing is persisted).</summary>
    void SetAccessibility(AccessibilityFlags f);

    /// <summary>Starts an executable de-elevated (through the shell) so restarted apps do not run as admin.</summary>
    void RestartProcess(string path);
}
