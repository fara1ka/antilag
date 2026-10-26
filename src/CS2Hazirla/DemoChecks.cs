#if DEBUG
using System;
using System.Collections.Generic;
using CS2Hazirla.Core;

namespace CS2Hazirla;

/// <summary>Developer screenshots only: sample content for the Açılış and Kontrol pages. A screenshot run never looks at the computer or sends a packet.</summary>
public partial class MainViewModel
{
    public void LoadDemoStartup()
    {
        StartupEntry E(string name, string display, string command, string? exe, StartupSource source, bool on, string reason = "") =>
            new($"{source}|{name}", name, display, command, exe, source, on, reason.Length > 0, reason);

        Startup.Demo(new[]
        {
            E("Discord", "Discord", @"C:\Users\Oyuncu\AppData\Local\Discord\Update.exe --processStart Discord.exe", @"C:\Users\Oyuncu\AppData\Local\Discord\Update.exe", StartupSource.UserRun, false),
            E("LGHUB", "Logitech G HUB", @"""C:\Program Files\LGHUB\lghub.exe"" --background", @"C:\Program Files\LGHUB\lghub.exe", StartupSource.UserRun, true),
            E("OneDrive", "Microsoft OneDrive", @"""C:\Users\Oyuncu\AppData\Local\Microsoft\OneDrive\OneDrive.exe"" /background", @"C:\Users\Oyuncu\AppData\Local\Microsoft\OneDrive\OneDrive.exe", StartupSource.UserRun, true),
            E("NVIDIA Broadcast", "NVIDIA Broadcast", @"""C:\Program Files\NVIDIA Corporation\NVIDIA Broadcast\NVIDIA Broadcast.exe"" --autostart", @"C:\Program Files\NVIDIA Corporation\NVIDIA Broadcast\NVIDIA Broadcast.exe", StartupSource.UserRun, true),
            E("SecurityHealth", "Windows Security", @"%windir%\system32\SecurityHealthSystray.exe", @"C:\WINDOWS\system32\SecurityHealthSystray.exe", StartupSource.MachineRun, true,
              Loc.T("Bilgisayarın çalışması için gerekli olabilir; buradan değiştirilemez.")),
            E("Spotify", "Spotify", @"C:\Users\Oyuncu\AppData\Roaming\Spotify\Spotify.exe /minimized", @"C:\Users\Oyuncu\AppData\Roaming\Spotify\Spotify.exe", StartupSource.UserRun, false),
            E("Steam", "Steam", @"""D:\Steam\steam.exe"" -silent", @"D:\Steam\steam.exe", StartupSource.UserRun, true),
        }, Loc.T("Bir sonraki Windows açılışında başlamayacak (şu an açıksa kapanmaz)."));
    }

    public void LoadDemoCheck()
    {
        const long gb = 1024L * 1024 * 1024;
        var facts = new HealthFacts(
            new[]
            {
                new DiskFact("C:", 740 * gb, 70 * gb, "SSD", IsSystemDrive: true, HoldsGame: false),
                new DiskFact("D:", 1172 * gb, 331 * gb, "SSD", IsSystemDrive: false, HoldsGame: true),
            },
            UptimeDays: 9.4, RebootPending: true, OnBattery: false,
            GpuName: "NVIDIA GeForce RTX 5080", GpuDriverDate: DateTime.Now.AddMonths(-15), DiskFailurePredicted: false);
        var items = HealthReport.Build(facts, DateTime.Now);

        var ci = Loc.Culture;
        var targets = new[]
        {
            new NetTargetResult(Loc.T("Modemin / yönlendiricin"), "192.168.1.1", new PingStats("192.168.1.1", 50, 50, 1.2, 0.3, 0)),
            new NetTargetResult(Loc.F("İnternet ({0})", "1.1.1.1"), "1.1.1.1", new PingStats("1.1.1.1", 50, 50, 14.4, 1.1, 0)),
            new NetTargetResult(Loc.F("İnternet ({0})", "8.8.8.8"), "8.8.8.8", new PingStats("8.8.8.8", 50, 49, 17.9, 2.6, 2)),
        };
        var net = new NetReport(targets, 21, false, NetVerdict.Healthy, Loc.T("Bağlantın sağlıklı görünüyor."),
            Loc.F("Modem: ortalama {0} ms, paket kaybı %{1}, dalgalanma {2} ms; internet: ortalama {3} ms, paket kaybı %{4}, dalgalanma {5} ms.",
                  1.2.ToString("0.#", ci), 0.ToString(ci), 0.3.ToString("0.#", ci), 14.4.ToString("0.#", ci), 0.ToString(ci), 1.1.ToString("0.#", ci))
            + " " + Loc.T("Kayıp, gecikme ve dalgalanma oyunda hissedilen sınırların içinde."));
        Check.Demo(items, net);
    }
}
#endif
