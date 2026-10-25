namespace CS2Hazirla.Core;

/// <summary>What the main screen runs at now, and the fastest refresh rate it offers at the same size and colour depth (both in Hz).</summary>
public record DisplayInfo(int Width, int Height, int CurrentHz, int MaxHz);

/// <summary>
/// The note about the refresh rate. A screen that runs far below what it can do (60 Hz on a 144 Hz monitor) is one of the most common reasons a game
/// feels sluggish, and no program of ours can fix it: Windows is told so, nothing is changed here.
/// </summary>
public static class DisplayAdvice
{
    /// <summary>
    /// The screen could run clearly faster: at least 20% more, and only below 200 Hz now. 144 → 165 is not worth a sentence, and a monitor that already runs
    /// at 360 Hz gains next to nothing from 540 Hz (such a mode often costs other things): nobody is nagged about that.
    /// </summary>
    public static bool CanBeFaster(DisplayInfo? d) => d != null && d.CurrentHz >= 24 && d.CurrentHz < 200 && d.MaxHz > d.CurrentHz && d.MaxHz >= d.CurrentHz * 1.2;

    public static string? Check(DisplayInfo? d) =>
        !CanBeFaster(d) ? null
        : Loc.F("Ekranın {0} Hz'e kadar çıkabiliyor ama şu an {1} Hz'de çalışıyor. Windows Ayarlar > Sistem > Ekran > Gelişmiş ekran'dan yenileme hızını yükseltirsen görüntü daha akıcı olur ve ekran gecikmesi azalır.",
                d!.MaxHz, d.CurrentHz);
}

/// <summary>The totals a process has used so far: CPU time (100 ns units) and bytes read and written. Two readings a moment apart give a rate.</summary>
public readonly record struct UsageCounters(long CpuTicks, long IoBytes);

/// <summary>The arithmetic of "who is using the machine right now": pure, so that it can be tested.</summary>
public static class UsageMath
{
    /// <summary>CPU: percent of the WHOLE processor (all logical cores) over the interval. Read/write: bytes per second.</summary>
    public static (double CpuPercent, double BytesPerSecond) Rate(UsageCounters before, UsageCounters after, double seconds, int logicalCores)
    {
        if (seconds <= 0 || logicalCores <= 0) return (0, 0);
        var cpu = Math.Max(0, after.CpuTicks - before.CpuTicks) / 1e7 / seconds / logicalCores * 100;
        var io = Math.Max(0, after.IoBytes - before.IoBytes) / seconds;
        return (Math.Min(cpu, 100), io);
    }

    public static string CpuText(double percent) =>
        percent < 0.05 ? Loc.T("%0") : percent < 10 ? Loc.F("%{0:0.0}", percent) : Loc.F("%{0:0}", percent);

    public static string RateText(double bytesPerSecond) =>
        bytesPerSecond < 1024 ? Loc.T("0 KB/sn")
        : bytesPerSecond < 1024 * 1024 ? Loc.F("{0:0} KB/sn", bytesPerSecond / 1024)
        : Loc.F("{0:0.0} MB/sn", bytesPerSecond / 1024 / 1024);
}
