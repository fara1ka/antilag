using System.Globalization;

namespace CS2Hazirla.Core;

public enum MetricVerdict { Better, Same, Worse, Info }

public record MetricRow(string Label, string Before, string After, string Delta, MetricVerdict Verdict);

public static class Metrics
{
    public const int PingSamples = 20;

    public static async Task<MetricsSnapshot> CaptureAsync(ISystem sys, string pingHost, CancellationToken ct)
    {
        var pingTask = sys.PingAsync(pingHost, PingSamples, ct);
        var cpu = await Task.Run(() => sys.SampleCpuIdlePercent(1500), ct);
        var ping = await pingTask;
        var mem = sys.GetMemory();
        var count = sys.GetProcesses().Count;
        return new MetricsSnapshot(DateTime.UtcNow, count, mem.AvailableBytes, mem.StandbyBytes, cpu, ping);
    }

    public static (List<MetricRow> Rows, string Summary) Compare(MetricsSnapshot b, MetricsSnapshot a)
    {
        var rows = new List<MetricRow>();
        var ci = CultureInfo.GetCultureInfo("tr-TR");

        MetricVerdict Judge(double delta, double noise, bool higherIsBetter)
        {
            if (Math.Abs(delta) < noise) return MetricVerdict.Same;
            return (delta > 0) == higherIsBetter ? MetricVerdict.Better : MetricVerdict.Worse;
        }

        string Sign(double d, string fmt) => (d > 0 ? "+" : "") + d.ToString(fmt, ci);

        rows.Add(new("Çalışan program ve arka plan işi sayısı", b.ProcessCount.ToString(), a.ProcessCount.ToString(),
            Sign(a.ProcessCount - b.ProcessCount, "0"),
            Judge(a.ProcessCount - b.ProcessCount, 1, false)));

        double ramB = b.FreeRamBytes / 1073741824.0, ramA = a.FreeRamBytes / 1073741824.0;
        rows.Add(new("Boş bellek (RAM, GB)", ramB.ToString("F2", ci), ramA.ToString("F2", ci),
            Sign(ramA - ramB, "F2"), Judge(ramA - ramB, 0.1, true)));

        double sbB = b.StandbyBytes / 1073741824.0, sbA = a.StandbyBytes / 1073741824.0;
        rows.Add(new("Windows'un RAM'de tuttuğu eski veriler (GB)", sbB.ToString("F2", ci), sbA.ToString("F2", ci),
            Sign(sbA - sbB, "F2"), MetricVerdict.Info));

        rows.Add(new("Boştaki işlemci (%)", b.CpuIdlePercent.ToString("F1", ci), a.CpuIdlePercent.ToString("F1", ci),
            Sign(a.CpuIdlePercent - b.CpuIdlePercent, "F1"), Judge(a.CpuIdlePercent - b.CpuIdlePercent, 2, true)));

        if (b.Ping.Error == null && a.Ping.Error == null && b.Ping.Received > 0 && a.Ping.Received > 0)
        {
            rows.Add(new($"İnternet gecikmesi (ping, ms) → {a.Ping.Host}", b.Ping.AvgMs.ToString("F1", ci), a.Ping.AvgMs.ToString("F1", ci),
                Sign(a.Ping.AvgMs - b.Ping.AvgMs, "F1"), Judge(a.Ping.AvgMs - b.Ping.AvgMs, 2, false)));
            rows.Add(new("Gecikme dalgalanması (jitter, ms)", b.Ping.JitterMs.ToString("F1", ci), a.Ping.JitterMs.ToString("F1", ci),
                Sign(a.Ping.JitterMs - b.Ping.JitterMs, "F1"), Judge(a.Ping.JitterMs - b.Ping.JitterMs, 1, false)));
            rows.Add(new("Paket kaybı (%)", b.Ping.LossPercent.ToString("F0", ci), a.Ping.LossPercent.ToString("F0", ci),
                Sign(a.Ping.LossPercent - b.Ping.LossPercent, "F0"), Judge(a.Ping.LossPercent - b.Ping.LossPercent, 5, false)));
        }
        else
        {
            rows.Add(new("İnternet gecikmesi", "—", "—", "—", MetricVerdict.Info));          // the full sentence is the "Not" row below
            rows.Add(new("Not", "İnternet gecikmesi ölçülemedi (bağlantı yok ya da sunucu yanıt vermedi).", "", "", MetricVerdict.Info));
        }

        var measured = rows.Where(r => r.Verdict is MetricVerdict.Better or MetricVerdict.Same or MetricVerdict.Worse).ToList();
        var better = measured.Count(r => r.Verdict == MetricVerdict.Better);
        var worse = measured.Count(r => r.Verdict == MetricVerdict.Worse);

        string summary;
        if (better == 0 && worse == 0)
            summary = "Ölçülen değerlerde anlamlı bir değişiklik yok. Bu normal: bilgisayarın zaten temizse kazanılacak bir şey yoktur. " +
                      "İnternet gecikmesi (ping) internet hattından gelir; bu uygulama onu düşüremez.";
        else if (worse == 0)
            summary = $"{better} değerde iyileşme var, kötüleşen yok. İnternet gecikmesi değişmediyse bu normaldir: o, internet hattına bağlıdır.";
        else
            summary = $"{better} değer iyileşti, {worse} değer kötüleşti. Ölçümler anlık ve dalgalıdır; tek bir ölçüme fazla anlam yükleme.";
        return (rows, summary);
    }
}
