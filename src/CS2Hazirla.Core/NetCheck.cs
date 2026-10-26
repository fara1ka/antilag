using System.Diagnostics;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.InteropServices;

namespace CS2Hazirla.Core;

/// <summary>
/// Where a connection problem probably is. Unknown: there is a problem but the data cannot say where, or no ping was answered although the name lookup worked
/// (some networks drop pings, so the internet may well be working), or the pings were fine but the name lookup did not answer in time.
/// </summary>
public enum NetVerdict { Healthy, LocalNetwork, Provider, Unreachable, Unknown }

/// <summary>One thing that was pinged. Label is plain text in the language on; Stats is null when nothing answered (or the target does not exist).</summary>
public sealed record NetTargetResult(string Label, string Host, PingStats? Stats);

/// <summary>The result of one check: the targets in the order they were asked, the name lookup time (null = not measured), the verdict and two short texts.</summary>
public sealed record NetReport(IReadOnlyList<NetTargetResult> Targets, double? DnsMs, bool Wireless, NetVerdict Verdict, string Headline, string Detail);

/// <summary>What the check needs to know about this computer's network. Reads only; the real one is <see cref="WindowsNetEnvironment"/>.</summary>
public interface INetEnvironment
{
    /// <summary>The IPv4 address of the home router (default gateway), or null when there is none (cable out, VPN only...).</summary>
    string? FindGateway();
    /// <summary>True when the connection that leads to the router is Wi-Fi.</summary>
    bool IsWireless();
    /// <summary>How long ONE name lookup of <paramref name="host"/> takes, in milliseconds; null when it fails.</summary>
    Task<double?> DnsLookupMsAsync(string host, CancellationToken ct);
}

/// <summary>
/// The internet check: a few seconds, one button. It pings the home router, two well-known internet addresses and (when given) the user's own ping target,
/// all at the same time, measures one name lookup, and says WHERE a problem probably is. Nothing else is sent and nothing is stored.
///
/// Honest by construction: a number that could not be read is left out, never guessed; the limits below are about what a game feels, and the text says so.
/// </summary>
public sealed class NetCheck
{
    // ------------------------------------------------------------------ the rules (what a game feels, not what is "perfect")

    /// <summary>
    /// From this much packet loss (%) on, a game stutters or rubber-bands. Loss counts from exactly this value up (>=), and only when AT LEAST TWO packets
    /// were lost (<c>MinLostPackets</c>): one lost echo is never a finding. With the usual ~45-50 pings one lost packet is 2.0-2.3 % and is below the
    /// limit anyway, but a slow target answers fewer pings inside the 6 s budget of WindowsSystem.PingAsync (fewer than ~34 and a single lost echo is already
    /// 3 % or more), and one dropped echo in a few dozen is ordinary on healthy Wi-Fi: it must not blame the home network or the provider.
    /// Two or more lost packets, and at least this percentage, is a real loss.
    /// </summary>
    public const double LossLimitPercent = 3;
    /// <summary>The fewest lost packets (sent - received) that can count as "over the loss limit"; see <see cref="LossLimitPercent"/>. Not part of the contract.</summary>
    private const int MinLostPackets = 2;
    /// <summary>Home network: a ping to the router answers in a few milliseconds when the link is fine; above this average (>, ms) the Wi-Fi or the cable is the weak part.</summary>
    public const double GatewayAvgLimitMs = 30;
    /// <summary>Home network: the average change between two answers (jitter) above this (>, ms) feels like lag spikes.</summary>
    public const double GatewayJitterLimitMs = 10;
    /// <summary>Internet: above this average (>, ms) to well-known servers the line itself is slow.</summary>
    public const double InternetAvgLimitMs = 80;
    /// <summary>Internet: jitter above this (>, ms) feels like lag spikes.</summary>
    public const double InternetJitterLimitMs = 20;
    /// <summary>
    /// A name lookup slower than this (>=, ms) is worth a remark (it delays connecting, not playing). It is a full second because the number depends on the
    /// Windows name cache: the first press on a healthy line takes about 300-400 ms, the next one about 0 ms. A cold lookup of 300-400 ms is normal.
    /// </summary>
    public const double DnsSlowMs = 1000;

    /// <summary>
    /// Pings per target. WindowsSystem.PingAsync waits 100 ms between pings (about 110-115 ms on a normal timer) and stops starting new pings after 6 s, so with all
    /// targets in parallel a working connection takes about 6-7 s (never more than about 7.2 s); a target that does not answer is given up after 3 failures (about 3.3 s).
    /// </summary>
    public const int PingCount = 50;

    public const string CloudflareHost = "1.1.1.1";
    public const string GoogleHost = "8.8.8.8";
    /// <summary>The name that is looked up once (a Steam address: that is the name that matters to the people who use this app).</summary>
    public const string DnsHost = "steampowered.com";

    /// <summary>Shown under every result. Says what the check is not.</summary>
    public static string Limits => Loc.T("Bu ölçüm birkaç saniye sürer ve ping kullanır; oyun sunucusuna giden yolu birebir göstermez. Ad çözme süresi Windows'un önbelleğine göre değişebilir.");

    /// <summary>
    /// The game is asked about this often (ms) while the pings run. The pings are cancelled at the first look that finds the game; a packet that is already on
    /// its way (at most one per target, about a second) may still be answered, no new one is started.
    /// </summary>
    private const int GameWatchMs = 500;

    // ------------------------------------------------------------------ the check

    private readonly ISystem _sys;
    private readonly Func<bool> _gameRunning;
    private readonly INetEnvironment _env;

    /// <param name="sys">Only <see cref="ISystem.PingAsync"/> is used.</param>
    /// <param name="gameRunning">Required. True while cs2.exe runs: the check then reads nothing and sends nothing and throws <see cref="GameRunningException"/>.</param>
    /// <param name="env">The network facts; the real Windows one when null.</param>
    public NetCheck(ISystem sys, Func<bool> gameRunning, INetEnvironment? env = null)
    {
        _sys = sys ?? throw new ArgumentNullException(nameof(sys));
        _gameRunning = gameRunning ?? throw new ArgumentNullException(nameof(gameRunning));
        _env = env ?? new WindowsNetEnvironment();
    }

    private enum Kind { Gateway, Reference, Own }
    private sealed record Spec(string Label, string Host, Kind Kind);

    /// <summary>How the router came out. None: no router known. Silent: known but no answer. Good / Bad: answered, within / over the limits.</summary>
    private enum Side { None, Silent, Good, Bad }

    /// <summary>
    /// Runs the check. Throws <see cref="GameRunningException"/> (before any packet when the game is up at the start; also when it appears while the check runs:
    /// the game is asked about every half second during the wait, the pings and the name lookup are cancelled at once and the result is dropped) and
    /// <see cref="OperationCanceledException"/> (promptly: the pings wind down by themselves within about a second).
    /// After the game was found no new packet is started; only an echo that is already on its way (at most one per target) can still be answered.
    /// A target that fails in any other way is simply "did not answer"; nothing else is thrown. May call <paramref name="progress"/> and the game check from any thread.
    /// </summary>
    public async Task<NetReport> RunAsync(string? extraHost, IProgress<string>? progress, CancellationToken ct)
    {
        // 1. nothing is read and nothing is sent while the game runs
        if (_gameRunning()) throw new GameRunningException();
        ct.ThrowIfCancellationRequested();

        // 2. the router and the kind of link: reads the machine, sends nothing (on a pool thread: a slow Windows call must not hold a window up)
        var (gateway, wireless) = await Task.Run(() => ReadEnvironment()).ConfigureAwait(false);
        ct.ThrowIfCancellationRequested();

        // 3. asked again, right before the first packet
        if (_gameRunning()) throw new GameRunningException();

        // 4. everything at the same time, all under ONE token: it follows the caller's token, and this method alone cancels it when the game shows up
        var specs = BuildTargets(gateway, extraHost);
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(ct);
        progress?.Report(Loc.T("Ping atılıyor; birkaç saniye sürer…"));       // one report for the whole wait, made before the first packet (a faulty listener must not leave pings running)
        var started = new List<(Spec Spec, Task<PingStats?> Task)>();
        foreach (var spec in specs) started.Add((spec, PingOneAsync(spec.Host, stop.Token)));
        var dnsTask = DnsAsync(stop.Token);

        var waits = new List<Task>(started.Select(s => (Task)s.Task)) { dnsTask };
        var all = Task.WhenAll(waits);
        await WaitWatchingTheGameAsync(all, stop, ct).ConfigureAwait(false);
        try { await all.ConfigureAwait(false); }            // finished already; a ping that threw GameRunningException surfaces here
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            ct.ThrowIfCancellationRequested();              // always the plain OperationCanceledException
            throw;
        }

        // 5. the final look: the game may have been started in the last moments of the wait
        if (_gameRunning()) throw new GameRunningException();
        progress?.Report(Loc.T("Sonuç hazırlanıyor…"));

        var targets = new List<NetTargetResult>();
        PingStats? gatewayStats = null, ownStats = null;
        string? ownHost = null;
        var references = new List<PingStats>();
        foreach (var (spec, task) in started)
        {
            var stats = await task.ConfigureAwait(false);    // finished already
            targets.Add(new NetTargetResult(spec.Label, spec.Host, stats));
            switch (spec.Kind)
            {
                case Kind.Gateway: gatewayStats = stats; break;
                case Kind.Reference: if (stats != null) references.Add(stats); break;
                default: ownStats = stats; ownHost = spec.Host; break;
            }
        }
        var dnsMs = await dnsTask.ConfigureAwait(false);

        // the internet is judged by the two well-known addresses; the user's own target stands in only when neither of them answers at all,
        // and only when it is a PUBLIC address or a name that can be an internet server (an address or a name inside the home network, like "router" or
        // "nas.local", answers whether the internet works or not).
        // (A far-away game server, or one that switches ping off, must not make a healthy connection look sick.)
        var ownIsEvidence = references.Count == 0 && ownStats != null && IsPublicTarget(ownHost);
        var evidence = ownIsEvidence ? new List<PingStats> { ownStats! } : references;
        var broken = evidence.FirstOrDefault(s => Exceeds(s, InternetAvgLimitMs, InternetJitterLimitMs));
        var shown = broken ?? evidence.OrderByDescending(s => s.AvgMs).FirstOrDefault();

        var side = gateway == null ? Side.None
                 : gatewayStats == null ? Side.Silent
                 : Exceeds(gatewayStats, GatewayAvgLimitMs, GatewayJitterLimitMs) ? Side.Bad
                 : Side.Good;

        var facts = new Facts(side, gatewayStats, evidence.Count > 0, broken != null, shown, wireless && gateway != null, dnsMs,
                              ownIsEvidence ? null : ownHost, ownIsEvidence ? null : ownStats);
        var (verdict, headline, detail) = Describe(facts);
        return new NetReport(targets, dnsMs, facts.Wireless, verdict, headline, detail);
    }

    // ------------------------------------------------------------------ the verdict

    /// <summary>
    /// The numbers of a target are over a limit: loss from the limit up (and only with at least two packets lost, see <see cref="LossLimitPercent"/>),
    /// average or jitter above theirs. The one place that decides it, for the router, the internet and the user's own target alike.
    /// </summary>
    private static bool Exceeds(PingStats s, double avgLimitMs, double jitterLimitMs) =>
        (s.Sent - s.Received >= MinLostPackets && s.LossPercent >= LossLimitPercent) || s.AvgMs > avgLimitMs || s.JitterMs > jitterLimitMs;

    private sealed record Facts(
        Side Gateway, PingStats? GatewayStats,
        bool InternetAnswered, bool InternetBad, PingStats? Internet,
        bool Wireless, double? DnsMs,
        string? OwnHost, PingStats? OwnStats);

    /// <summary>
    /// The rules, in this order:
    ///  · no internet address answers at all and the name lookup failed too → Unreachable (the router may still answer: the text says which);
    ///  · no internet address answers at all but the name lookup worked     → Unknown, worded as a hedge: some networks drop pings, the internet may be working;
    ///  · the router answers but is over its limits                   → LocalNetwork (the home network: Wi-Fi, cable or modem);
    ///  · the internet is over its limits, the router is fine         → Provider;
    ///  · the internet is over its limits, the router could not be judged (not found / silent) → Unknown: the problem is real but its place is not known;
    ///  · everything that was measured is within the limits, but the name lookup was tried and nothing came back within its limit (DnsMs null)
    ///                                                                → Unknown, worded as a hedge: the pings work, names may not be found; never "healthy", never blame;
    ///  · everything that was measured is within the limits           → Healthy (the text says so when the router could not be measured).
    /// </summary>
    private static (NetVerdict Verdict, string Headline, string Detail) Describe(Facts f)
    {
        if (!f.InternetAnswered)
        {
            var routerKnown = f.Gateway is Side.Good or Side.Bad;
            string first;
            if (routerKnown)
                first = Loc.F("Modem: ortalama {0} ms, paket kaybı %{1}, dalgalanma {2} ms; internet adreslerinden hiçbiri yanıt vermedi.",
                              FormatMs(f.GatewayStats!.AvgMs), Pct(f.GatewayStats.LossPercent), FormatMs(f.GatewayStats.JitterMs));
            else if (f.Gateway == Side.Silent)
                first = Loc.T("Ne modemden ne de internet adreslerinden yanıt geldi.");
            else
                first = Loc.T("İnternet adreslerinden hiçbiri yanıt vermedi.");

            // the name lookup got an answer, so something reaches the internet: only the pings stay unanswered. Some networks drop outbound pings,
            // so this is not stated as "no internet", and there is no "restart the modem" advice for a line that may be working.
            if (f.DnsMs != null)
                return (NetVerdict.Unknown,
                        Loc.T("Ping internet adreslerine ulaşmadı, ama adres çözme çalışıyor."),
                        Join(first, Loc.T("Ping engelleniyor olabilir; internetin çalışıyor olabilir.")));

            if (routerKnown)
                return (NetVerdict.Unreachable, Loc.T("İnternete ulaşılamadı."),
                        Join(first,
                             Loc.T("Modeme ulaşılıyor ama internete çıkılamıyor gibi görünüyor."),
                             Loc.T("Modemi kapatıp 30 saniye sonra yeniden aç; düzelmezse internet sağlayıcını ara.")));
            return (NetVerdict.Unreachable, Loc.T("İnternete ulaşılamadı."),
                    Join(first,
                         Loc.T("Bağlantın kopmuş olabilir."),
                         Loc.T("Wi-Fi'ın açık olduğunu ya da kablonun takılı olduğunu kontrol et.")));
        }

        if (f.Gateway == Side.Bad)
        {
            var advice = f.Wireless
                ? Loc.T("Mümkünse bilgisayarını modeme kabloyla (Ethernet) bağla.")
                : Loc.T("Modemi kapatıp 30 saniye sonra yeniden aç.");
            return (NetVerdict.LocalNetwork,
                    Loc.T("Sorun büyük olasılıkla evdeki ağda (Wi-Fi ya da modem)."),
                    Join(Both(f),
                         Loc.T("Bu değerler oyunda takılma ya da gecikme olarak hissedilebilir; sorun modeme giden yolda ya da modemin kendisinde görünüyor."),
                         advice));
        }

        if (f.InternetBad)
        {
            if (f.Gateway == Side.Good)
                return (NetVerdict.Provider,
                        Loc.T("Sorun büyük olasılıkla internet sağlayıcında."),
                        Join(Both(f),
                             Loc.T("Modem sağlıklı, ama internete çıkışta oyunda hissedilebilecek kayıp ya da gecikme var."),
                             Loc.T("Sorun sürerse internet sağlayıcını arayıp hattını sor.")));

            var place = f.Gateway == Side.Silent
                ? Loc.T("Oyunda hissedilebilecek kayıp ya da gecikme var, ama modem ping'e yanıt vermediği için sorunun evde mi, internet sağlayıcında mı olduğu söylenemez.")
                : Loc.T("Oyunda hissedilebilecek kayıp ya da gecikme var, ama modem bulunamadığı için sorunun evde mi, internet sağlayıcında mı olduğu söylenemez.");
            return (NetVerdict.Unknown,
                    Loc.T("İnternetinde bir sorun var, ama nerede olduğu belli değil."),
                    Join(InternetOnly(f), place, Loc.T("Modemi kapatıp 30 saniye sonra yeniden aç; düzelmezse internet sağlayıcını ara.")));
        }

        // the pings are fine, but the name lookup got no answer within its limit (or failed): "healthy" would claim more than was measured. The text says what
        // was measured (the numbers of the addresses that answered) and one hedged sentence; no advice to restart anything and no blame.
        if (f.DnsMs == null)
            return (NetVerdict.Unknown,
                    Loc.T("Ping çalışıyor ama adres çözme yanıt vermedi."),
                    Join(f.Gateway == Side.Good ? Both(f) : InternetOnly(f),
                         Loc.T("Bu, sitelerin ve oyun sunucularının adlarının bulunamaması anlamına gelebilir; bir kez daha dene.")));

        var meaningOk = f.Gateway switch
        {
            Side.Good => Loc.T("Kayıp, gecikme ve dalgalanma oyunda hissedilen sınırların içinde."),
            Side.Silent => Loc.T("Modem ping'e yanıt vermedi, bu yüzden ev ağı ayrı ölçülemedi; internet ise oyunda hissedilen sınırların içinde."),
            _ => Loc.T("Modem bulunamadı, bu yüzden ev ağı ayrı ölçülemedi; internet ise oyunda hissedilen sınırların içinde."),
        };
        return (NetVerdict.Healthy,
                Loc.T("Bağlantın sağlıklı görünüyor."),
                Join(f.Gateway == Side.Good ? Both(f) : InternetOnly(f), meaningOk, OwnNote(f) ?? DnsNote(f)));
    }

    /// <summary>One remark about the user's own target; only when everything else is healthy (otherwise the advice has the room).</summary>
    private static string? OwnNote(Facts f)
    {
        if (f.OwnHost == null) return null;
        if (f.OwnStats == null) return Loc.F("{0} adresi ping'e yanıt vermedi; bazı sunucular ping'i kapatır.", f.OwnHost);
        if (!Exceeds(f.OwnStats, InternetAvgLimitMs, InternetJitterLimitMs)) return null;
        return Loc.F("Ama {0} adresine giden yol daha kötü: ortalama {1} ms, paket kaybı %{2}, dalgalanma {3} ms.",
                     f.OwnHost, FormatMs(f.OwnStats.AvgMs), Pct(f.OwnStats.LossPercent), FormatMs(f.OwnStats.JitterMs));
    }

    private static string? DnsNote(Facts f) =>
        f.DnsMs is { } d && d >= DnsSlowMs
            ? Loc.F("Adres çözme yavaş ({0} ms): bu, oyuna girişi geciktirebilir. Sorun sürerse internet sağlayıcına danış.", FormatMs(d))
            : null;

    private static string Both(Facts f) =>
        Loc.F("Modem: ortalama {0} ms, paket kaybı %{1}, dalgalanma {2} ms; internet: ortalama {3} ms, paket kaybı %{4}, dalgalanma {5} ms.",
              FormatMs(f.GatewayStats!.AvgMs), Pct(f.GatewayStats.LossPercent), FormatMs(f.GatewayStats.JitterMs),
              FormatMs(f.Internet!.AvgMs), Pct(f.Internet.LossPercent), FormatMs(f.Internet.JitterMs));

    private static string InternetOnly(Facts f) =>
        Loc.F("İnternet: ortalama {0} ms, paket kaybı %{1}, dalgalanma {2} ms.", FormatMs(f.Internet!.AvgMs), Pct(f.Internet.LossPercent), FormatMs(f.Internet.JitterMs));

    private static string Join(params string?[] sentences) => string.Join(" ", sentences.Where(s => !string.IsNullOrEmpty(s)));

    /// <summary>
    /// Milliseconds the way people read them: one decimal under 100 ("2,4" / "2.4"), whole numbers above; the decimal mark follows the language on.
    /// The sentences of the result use this rule, so a row that shows the same number (the name lookup) prints exactly what the sentence says.
    /// </summary>
    public static string FormatMs(double ms) =>
        (ms < 100 ? Math.Round(ms, 1, MidpointRounding.AwayFromZero) : Math.Round(ms, MidpointRounding.AwayFromZero)).ToString("0.#", Loc.Culture);

    private static string Pct(double v) => Math.Round(v, 1, MidpointRounding.AwayFromZero).ToString("0.#", Loc.Culture);

    // ------------------------------------------------------------------ the pieces

    private (string? Gateway, bool Wireless) ReadEnvironment()
    {
        string? gateway = null;
        try { gateway = _env.FindGateway(); } catch { /* a network that cannot be read has no known router */ }
        if (string.IsNullOrWhiteSpace(gateway)) return (null, false);
        var wireless = false;
        try { wireless = _env.IsWireless(); } catch { /* unknown = not claimed */ }
        return (gateway.Trim(), wireless);
    }

    private static List<Spec> BuildTargets(string? gateway, string? extraHost)
    {
        var list = new List<Spec>();
        if (gateway != null) list.Add(new Spec(Loc.T("Modemin / yönlendiricin"), gateway, Kind.Gateway));
        list.Add(new Spec(Loc.F("İnternet ({0})", CloudflareHost), CloudflareHost, Kind.Reference));
        list.Add(new Spec(Loc.F("İnternet ({0})", GoogleHost), GoogleHost, Kind.Reference));
        var extra = extraHost?.Trim();
        if (!string.IsNullOrEmpty(extra) && !list.Any(s => string.Equals(s.Host, extra, StringComparison.OrdinalIgnoreCase)))
            list.Add(new Spec(Loc.F("Ping hedefin ({0})", extra), extra, Kind.Own));
        return list;
    }

    /// <summary>
    /// False for a ping target that is certainly inside this computer or the home network (so it answers whether the internet works or not): an address that is
    /// loopback, private (10/8, 172.16/12, 192.168/16), link-local (169.254/16, fe80::/10), unspecified, IPv6 unique-local (fc00::/7), or one that no internet server
    /// can have (shared provider space 100.64/10, "this network" 0/8, multicast, reserved and broadcast 240/4, site-local fec0::/10), and the name "localhost".
    /// A name is not public either when it cannot be an internet server: a single word without a dot ("router", "nas": a name from the home network) or one that
    /// ends in a name of a home or company network (<see cref="PrivateNameSuffixes"/>, letter case and a trailing dot do not matter).
    /// Other names are public: they are not looked up here (that would send a packet) and may stand in as evidence.
    /// </summary>
    private static bool IsPublicTarget(string? host)
    {
        var text = host?.Trim();
        if (string.IsNullOrEmpty(text)) return false;
        var name = text.TrimEnd('.');
        if (name.Equals("localhost", StringComparison.OrdinalIgnoreCase) || name.EndsWith(".localhost", StringComparison.OrdinalIgnoreCase)) return false;
        if (!IPAddress.TryParse(text, out var ip)) return IsInternetName(name);       // a name

        if (ip.IsIPv4MappedToIPv6) ip = ip.MapToIPv4();
        if (IPAddress.IsLoopback(ip) || ip.Equals(IPAddress.Any) || ip.Equals(IPAddress.IPv6Any)) return false;
        var b = ip.GetAddressBytes();
        if (ip.AddressFamily == AddressFamily.InterNetwork)
            return !(b[0] == 0                                                        // 0/8
                  || b[0] == 10                                                       // 10/8
                  || (b[0] == 100 && (b[1] & 0xC0) == 64)                             // 100.64/10 (shared address space)
                  || (b[0] == 169 && b[1] == 254)                                     // 169.254/16
                  || (b[0] == 172 && (b[1] & 0xF0) == 16)                             // 172.16/12
                  || (b[0] == 192 && b[1] == 168)                                     // 192.168/16
                  || b[0] >= 224);                                                    // multicast, reserved, broadcast
        if (ip.AddressFamily == AddressFamily.InterNetworkV6)
            return !(ip.IsIPv6LinkLocal || ip.IsIPv6SiteLocal                         // fe80::/10, fec0::/10
                  || (b[0] & 0xFE) == 0xFC                                            // fc00::/7 (unique-local)
                  || b[0] == 0xFF);                                                   // multicast
        return false;
    }

    /// <summary>
    /// Endings (without the leading dot) of names that only exist inside a home or company network, never on the internet: .local (mDNS), .lan, .home, .internal,
    /// .localdomain, .home.arpa (RFC 8375), .corp, .intranet, .private. A target with such a name answers (or not) whatever the internet does.
    /// </summary>
    private static readonly string[] PrivateNameSuffixes = { "local", "lan", "home", "internal", "localdomain", "home.arpa", "corp", "intranet", "private" };

    /// <summary>
    /// True when a name (not an IP address) can be an internet server. False for a single word ("router", "nas": no dot) and for names inside a home or company
    /// network (<see cref="PrivateNameSuffixes"/>; "mypc.local", "files.corp"; letter case does not matter). <paramref name="name"/> has no trailing dot.
    /// </summary>
    private static bool IsInternetName(string name)
    {
        if (name.IndexOf('.') < 0) return false;
        foreach (var suffix in PrivateNameSuffixes)
            if (name.Equals(suffix, StringComparison.OrdinalIgnoreCase) || name.EndsWith("." + suffix, StringComparison.OrdinalIgnoreCase)) return false;
        return true;
    }

    /// <summary>
    /// Waits until every ping and the name lookup are done, and asks the game about every <see cref="GameWatchMs"/> ms on the way. On the way out with an exception
    /// (the game is up, the caller cancelled, the game check itself broke) everything that is still running is cancelled first and its failure marked as seen,
    /// so that no packet is started after this method gave up.
    /// </summary>
    private async Task WaitWatchingTheGameAsync(Task all, CancellationTokenSource stop, CancellationToken ct)
    {
        try
        {
            while (!all.IsCompleted)
            {
                using var tick = CancellationTokenSource.CreateLinkedTokenSource(ct);
                await Task.WhenAny(all, Task.Delay(GameWatchMs, tick.Token)).ConfigureAwait(false);
                tick.Cancel();                                                         // the timer is not needed any more
                ct.ThrowIfCancellationRequested();                                     // the plain OperationCanceledException, at once even when a ping ignores its token
                if (!all.IsCompleted && _gameRunning()) throw new GameRunningException();
            }
        }
        catch
        {
            try { stop.Cancel(); } catch { /* a listener that throws must not hide why we stopped */ }
            Observe(all);                                                              // the streams that are still winding down must not leave an unobserved error behind
            throw;
        }
    }

    /// <summary>One target: null = nothing answered (or it does not exist, or the ping failed in any way). A target with some loss is a result, not an error.</summary>
    private async Task<PingStats?> PingOneAsync(string host, CancellationToken ct)
    {
        try
        {
            var stats = await _sys.PingAsync(host, PingCount, ct).ConfigureAwait(false);
            return stats is { Received: > 0 } ? stats : null;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (GameRunningException) { throw; }
        catch { return null; }
    }

    private async Task<double?> DnsAsync(CancellationToken ct)
    {
        try { return await _env.DnsLookupMsAsync(DnsHost, ct).ConfigureAwait(false); }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch { return null; }
    }

    /// <summary>Marks the failure of a task nobody awaits any more as seen.</summary>
    internal static void Observe(Task? task) =>
        task?.ContinueWith(static t => { _ = t.Exception; }, CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
}

/// <summary>An adapter as the network list of Windows shows it (a plain record, so that the choice of the router can be tested without a network).</summary>
public sealed record GatewayCandidate(string Name, string Description, NetworkInterfaceType Type, OperationalStatus Status, IReadOnlyList<IPAddress> Gateways, int? IfIndex);

/// <summary>The router that was chosen and whether the way to it is Wi-Fi.</summary>
public sealed record GatewayChoice(string Gateway, bool Wireless);

/// <summary>
/// The real network facts. Only reads: the adapter list of Windows, one look into the routing table (which adapter leads to the internet) and, for the name lookup,
/// one DNS request. No packet is sent by FindGateway and IsWireless.
/// </summary>
public sealed class WindowsNetEnvironment : INetEnvironment
{
    /// <summary>A name lookup that takes longer than this is given up (null).</summary>
    public const int DnsTimeoutMs = 4000;

    // words in the name of an adapter that only exists for a VPN (a VPN is not the home network)
    private static readonly string[] VpnWords = { "vpn", "tap-windows", "wintun", "wireguard", "tailscale", "zerotier", "hamachi", "nordlynx", "openvpn" };

    public WindowsNetEnvironment() { }

    public string? FindGateway()
    {
        try { return Choose()?.Gateway; }
        catch { return null; }
    }

    /// <summary>The adapter that leads to the router is a Wi-Fi adapter (the same test as WindowsSystem.GetNics uses: Wireless80211). False when there is no router.</summary>
    public bool IsWireless()
    {
        try { return Choose()?.Wireless ?? false; }
        catch { return false; }
    }

    public async Task<double?> DnsLookupMsAsync(string host, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(ct);
        limit.CancelAfter(DnsTimeoutMs);              // one bounded wait for this one request, not a background timer
        Task<IPAddress[]>? lookup = null;
        try
        {
            var started = Stopwatch.GetTimestamp();
            lookup = Dns.GetHostAddressesAsync(host, limit.Token);
            var addresses = await lookup.WaitAsync(limit.Token).ConfigureAwait(false);
            return addresses.Length > 0 ? Stopwatch.GetElapsedTime(started).TotalMilliseconds : null;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            NetCheck.Observe(lookup);
            ct.ThrowIfCancellationRequested();
            throw;
        }
        catch
        {
            NetCheck.Observe(lookup);
            return null;                              // no answer, a refused name, the time limit: simply "not measured"
        }
    }

    private static GatewayChoice? Choose() => PickGateway(Snapshot(), BestInterfaceIndex());

    /// <summary>
    /// The router of this computer: the default IPv4 gateway of an adapter that is up and is not a loopback, tunnel, dial-up/PPP or VPN adapter.
    /// With several such adapters the one Windows would use to reach the internet wins (<paramref name="bestInterfaceIndex"/>); when that one is not among them
    /// (a VPN carries the internet) or is not known, a cable wins over Wi-Fi (Windows prefers the faster link by default). Null when there is none.
    /// </summary>
    public static GatewayChoice? PickGateway(IEnumerable<GatewayCandidate> candidates, int? bestInterfaceIndex)
    {
        var usable = new List<(GatewayCandidate Nic, IPAddress Gateway)>();
        foreach (var c in candidates)
        {
            if (c.Status != OperationalStatus.Up) continue;
            if (c.Type is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel or NetworkInterfaceType.Ppp or NetworkInterfaceType.Slip) continue;
            if (LooksLikeVpn(c)) continue;
            var gateway = c.Gateways.FirstOrDefault(IsUsableGateway);
            if (gateway != null) usable.Add((c, gateway));
        }
        if (usable.Count == 0) return null;

        var pick = -1;
        if (bestInterfaceIndex != null)
            for (var i = 0; i < usable.Count && pick < 0; i++)
                if (usable[i].Nic.IfIndex == bestInterfaceIndex) pick = i;
        if (pick < 0)
        {
            var order = usable.Select((u, i) => (Wifi: u.Nic.Type == NetworkInterfaceType.Wireless80211, Index: i)).OrderBy(x => x.Wifi).ThenBy(x => x.Index);
            pick = order.First().Index;
        }
        return new GatewayChoice(usable[pick].Gateway.ToString(), usable[pick].Nic.Type == NetworkInterfaceType.Wireless80211);
    }

    private static bool IsUsableGateway(IPAddress a) =>
        a.AddressFamily == AddressFamily.InterNetwork && !a.Equals(IPAddress.Any) && !IPAddress.IsLoopback(a);

    private static bool LooksLikeVpn(GatewayCandidate c) =>
        VpnWords.Any(w => (c.Name ?? "").Contains(w, StringComparison.OrdinalIgnoreCase) || (c.Description ?? "").Contains(w, StringComparison.OrdinalIgnoreCase));

    private static List<GatewayCandidate> Snapshot()
    {
        var list = new List<GatewayCandidate>();
        foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
        {
            try
            {
                var props = nic.GetIPProperties();
                int? index = null;
                try { index = props.GetIPv4Properties()?.Index; } catch { /* no IPv4 on this adapter */ }
                list.Add(new GatewayCandidate(nic.Name, nic.Description, nic.NetworkInterfaceType, nic.OperationalStatus,
                                              props.GatewayAddresses.Select(g => g.Address).ToList(), index));
            }
            catch { /* an adapter that cannot be read is not a candidate */ }
        }
        return list;
    }

    /// <summary>Which adapter Windows would use to reach the internet (a look into the routing table; nothing is sent). Null when Windows does not say.</summary>
    private static int? BestInterfaceIndex()
    {
        try
        {
            var address = BitConverter.ToUInt32(IPAddress.Parse(NetCheck.CloudflareHost).GetAddressBytes(), 0);      // network byte order, as the call wants it
            return GetBestInterface(address, out var index) == 0 ? (int)index : null;
        }
        catch { return null; }
    }

    [DllImport("iphlpapi.dll")]
    private static extern int GetBestInterface(uint destinationAddress, out uint bestInterfaceIndex);
}
