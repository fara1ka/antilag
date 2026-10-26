using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text.RegularExpressions;
using CS2Hazirla.Core;
using Xunit;
using Xunit.Abstractions;

namespace CS2Hazirla.Tests;

/// <summary>The network facts of a test: what the router is, whether the way to it is Wi-Fi, how long a name lookup took.</summary>
internal sealed class NetCheckFakeEnv : INetEnvironment
{
    public string? Gateway = "192.168.1.1";
    public bool Wifi;
    public double? Dns = 24;
    public bool DnsThrows;
    public bool GatewayThrows;
    public int GatewayCalls, WirelessCalls;
    public readonly List<string> DnsHosts = new();
    /// <summary>The token each name lookup was given (to see that the check cancels it).</summary>
    public readonly List<CancellationToken> DnsTokens = new();
    /// <summary>Full control over one lookup (wait for the token, ignore it...): used instead of <see cref="Dns"/> when set.</summary>
    public Func<CancellationToken, Task<double?>>? DnsHandler;

    public string? FindGateway()
    {
        GatewayCalls++;
        if (GatewayThrows) throw new InvalidOperationException("the network list is broken (test)");
        return Gateway;
    }

    public bool IsWireless() { WirelessCalls++; return Wifi; }

    public Task<double?> DnsLookupMsAsync(string host, CancellationToken ct)
    {
        lock (DnsHosts) { DnsHosts.Add(host); DnsTokens.Add(ct); }
        if (DnsThrows) throw new InvalidOperationException("the lookup is broken (test)");
        if (DnsHandler != null) return DnsHandler(ct);
        return Task.FromResult(Dns);
    }
}

/// <summary>Pings that never answer by themselves but end when their token is cancelled (like the real one, which stops at its next look at the token); remembers the tokens.</summary>
internal sealed class PendingPings
{
    public readonly ConcurrentBag<CancellationToken> Tokens = new();

    public Task<PingStats> Handle(string host, int count, CancellationToken ct)
    {
        Tokens.Add(ct);
        var pending = new TaskCompletionSource<PingStats>(TaskCreationOptions.RunContinuationsAsynchronously);
        ct.Register(() => pending.TrySetCanceled(ct));
        return pending.Task;
    }
}

/// <summary>
/// The internet check: the verdict rules and their edges, the targets, the game and cancellation rules, the progress, the texts in both languages.
/// Pure: a fake system and a fake network, no packet is sent.
/// </summary>
[Collection("language")]
public class NetCheckTests : IDisposable
{
    private const string Gw = "192.168.1.1";
    private const string One = "1.1.1.1";
    private const string Two = "8.8.8.8";
    private const string Own = "play.example.net";

    private static readonly Regex TurkishLetter = new("[çğıöşüÇĞİÖŞÜ]", RegexOptions.CultureInvariant);

    private readonly FakeSystem _sys = new();
    private readonly NetCheckFakeEnv _env = new();
    private readonly CultureInfo _culture = CultureInfo.CurrentCulture;
    private readonly CultureInfo _uiCulture = CultureInfo.CurrentUICulture;
    private bool _game;

    public NetCheckTests() => Loc.Use("tr");

    public void Dispose()
    {
        Loc.Use(TestLanguage.Default);
        CultureInfo.CurrentCulture = _culture;
        CultureInfo.CurrentUICulture = _uiCulture;
    }

    // ------------------------------------------------------------------ helpers

    /// <summary>
    /// A target that answered. Sent, Received and LossPercent agree with each other the way WindowsSystem.PingAsync computes them ((sent - received) * 100 / sent):
    /// the smallest number of pings from <see cref="NetCheck.PingCount"/> up in which <paramref name="loss"/> is a whole number of lost pings (0 % = 50 of 50,
    /// 2 % = 49 of 50, 4 % = 48 of 50, 3 % = 97 of 100, 2.9 % = 971 of 1000).
    /// </summary>
    private static PingStats Ok(string host, double avg = 10, double jitter = 1, double loss = 0)
    {
        for (var sent = NetCheck.PingCount; sent <= 2000; sent++)
        {
            var lost = (int)Math.Round(sent * loss / 100);
            var percent = lost * 100.0 / sent;
            if (Math.Abs(percent - loss) < 1e-9) return new PingStats(host, sent, sent - lost, avg, jitter, percent);
        }
        throw new ArgumentOutOfRangeException(nameof(loss), loss, "this percentage cannot be made of whole pings");
    }

    /// <summary>A target that answered some of an exact number of pings: the loss is computed the way the real ping does.</summary>
    private static PingStats Lost(string host, int sent, int received, double avg = 10, double jitter = 1) =>
        new(host, sent, received, avg, jitter, (sent - received) * 100.0 / sent);

    private static PingStats Silent(string host) => new(host, 3, 0, 0, 0, 100, "yanıt yok");

    private void Gateway(double avg = 2, double jitter = 1, double loss = 0) => _sys.PingResults[Gw] = Ok(Gw, avg, jitter, loss);

    private void Internet(double avg = 20, double jitter = 2, double loss = 0)
    {
        _sys.PingResults[One] = Ok(One, avg, jitter, loss);
        _sys.PingResults[Two] = Ok(Two, avg, jitter, loss);
    }

    private void InternetSilent() { _sys.PingResults[One] = Silent(One); _sys.PingResults[Two] = Silent(Two); }

    private NetCheck Check() => new(_sys, () => _game, _env);

    private Task<NetReport> Run(string? extra = null, IProgress<string>? progress = null, CancellationToken ct = default) =>
        Check().RunAsync(extra, progress, ct);

    private sealed class Collect : IProgress<string>
    {
        public readonly List<string> Items = new();
        public void Report(string value) { lock (Items) Items.Add(value); }
    }

    private static async Task WaitUntil(Func<bool> condition)
    {
        var watch = Stopwatch.StartNew();
        while (!condition())
        {
            if (watch.Elapsed > TimeSpan.FromSeconds(10)) throw new TimeoutException("the condition never became true");
            await Task.Delay(5);
        }
    }

    /// <summary>Sentences the way a reader counts them: a full stop (or ! ?) followed by a space and a capital letter. A decimal point in a number is not one.</summary>
    private static int Sentences(string text) => Regex.Split(text.Trim(), @"(?<=[.!?])\s+(?=\p{Lu})").Length;

    // ------------------------------------------------------------------ the rules, and their edges

    [Theory]
    // gateway avg, jitter, loss | internet avg, jitter, loss | verdict
    [InlineData(2.0, 1.0, 0.0, 20.0, 2.0, 0.0, NetVerdict.Healthy)]
    // the router: exactly at a limit is still fine (average and jitter must be ABOVE theirs, loss counts FROM its limit)
    [InlineData(30.0, 1.0, 0.0, 20.0, 2.0, 0.0, NetVerdict.Healthy)]
    [InlineData(30.1, 1.0, 0.0, 20.0, 2.0, 0.0, NetVerdict.LocalNetwork)]
    [InlineData(2.0, 10.0, 0.0, 20.0, 2.0, 0.0, NetVerdict.Healthy)]
    [InlineData(2.0, 10.1, 0.0, 20.0, 2.0, 0.0, NetVerdict.LocalNetwork)]
    // (a loss in % is made of whole pings by the Ok helper: 2 % = 1 of 50, 2.9 % = 29 of 1000, 3 % = 3 of 100, 4 % = 2 of 50; the "one lost ping is never a finding"
    // rule has its own tests below)
    [InlineData(2.0, 1.0, 2.0, 20.0, 2.0, 0.0, NetVerdict.Healthy)]                  // one lost ping of 50
    [InlineData(2.0, 1.0, 2.9, 20.0, 2.0, 0.0, NetVerdict.Healthy)]                  // 29 lost of 1000: many lost, but under the limit
    [InlineData(2.0, 1.0, 3.0, 20.0, 2.0, 0.0, NetVerdict.LocalNetwork)]             // 3 lost of 100: exactly the limit
    [InlineData(2.0, 1.0, 4.0, 20.0, 2.0, 0.0, NetVerdict.LocalNetwork)]              // two lost pings of 50
    [InlineData(500.0, 1.0, 0.0, 20.0, 2.0, 0.0, NetVerdict.LocalNetwork)]
    // the internet, the router fine
    [InlineData(2.0, 1.0, 0.0, 80.0, 2.0, 0.0, NetVerdict.Healthy)]
    [InlineData(2.0, 1.0, 0.0, 80.1, 2.0, 0.0, NetVerdict.Provider)]
    [InlineData(2.0, 1.0, 0.0, 20.0, 20.0, 0.0, NetVerdict.Healthy)]
    [InlineData(2.0, 1.0, 0.0, 20.0, 20.1, 0.0, NetVerdict.Provider)]
    [InlineData(2.0, 1.0, 0.0, 20.0, 2.0, 2.0, NetVerdict.Healthy)]                  // one lost ping of 50
    [InlineData(2.0, 1.0, 0.0, 20.0, 2.0, 2.9, NetVerdict.Healthy)]                  // 29 lost of 1000: many lost, but under the limit
    [InlineData(2.0, 1.0, 0.0, 20.0, 2.0, 3.0, NetVerdict.Provider)]                 // 3 lost of 100: exactly the limit
    [InlineData(2.0, 1.0, 0.0, 20.0, 2.0, 4.0, NetVerdict.Provider)]                  // two lost pings of 50
    // a bad router wins over a bad internet (the internet looks bad BECAUSE of the router)
    [InlineData(40.0, 1.0, 0.0, 200.0, 2.0, 0.0, NetVerdict.LocalNetwork)]
    [InlineData(2.0, 1.0, 5.0, 20.0, 2.0, 8.0, NetVerdict.LocalNetwork)]
    public async Task The_verdict_follows_the_limits_exactly(double gAvg, double gJitter, double gLoss, double iAvg, double iJitter, double iLoss, NetVerdict expected)
    {
        Gateway(gAvg, gJitter, gLoss);
        Internet(iAvg, iJitter, iLoss);
        Assert.Equal(expected, (await Run()).Verdict);
    }

    [Fact]
    public void The_limits_are_the_agreed_numbers_and_one_lost_ping_is_normal_while_two_are_not()
    {
        Assert.Equal(3, NetCheck.LossLimitPercent);
        Assert.Equal(30, NetCheck.GatewayAvgLimitMs);
        Assert.Equal(10, NetCheck.GatewayJitterLimitMs);
        Assert.Equal(80, NetCheck.InternetAvgLimitMs);
        Assert.Equal(20, NetCheck.InternetJitterLimitMs);
        Assert.Equal(1000, NetCheck.DnsSlowMs);
        // the loss the real ping computes is (sent - received) * 100 / sent: with about 45-50 pings sent
        foreach (var sent in new[] { 45, 46, 48, 50 })
        {
            Assert.True(100.0 / sent < NetCheck.LossLimitPercent, $"one lost ping of {sent} must stay under the limit");
            Assert.True(200.0 / sent >= NetCheck.LossLimitPercent, $"two lost pings of {sent} must reach the limit");
        }
        // a slow target gets fewer pings into the 6 s budget: with 33 or fewer sent, ONE lost echo alone reaches the percentage; that is why the percentage
        // alone is not enough and at least two lost packets are needed as well (tested below)
        foreach (var sent in new[] { 10, 20, 30, 33 })
            Assert.True(100.0 / sent >= NetCheck.LossLimitPercent, $"one lost ping of {sent} is already at the percentage");
        Assert.InRange(NetCheck.PingCount * 0.1, 4, 6);                              // 100 ms between pings: about 5 s of waiting, so the whole check is 6-7 s
    }

    [Fact]
    public async Task One_lost_ping_of_fifty_is_normal_and_two_are_a_problem()
    {
        // the loss the real ping computes: (sent - received) * 100 / sent
        _sys.PingResults[Gw] = new PingStats(Gw, 50, 49, 3, 1, (50 - 49) * 100.0 / 50);
        Assert.Equal(NetVerdict.Healthy, (await Run()).Verdict);
        _sys.PingResults[Gw] = new PingStats(Gw, 45, 44, 3, 1, (45 - 44) * 100.0 / 45);                // a few pings fewer sent: 2.2 %
        Assert.Equal(NetVerdict.Healthy, (await Run()).Verdict);
        _sys.PingResults[Gw] = new PingStats(Gw, 50, 48, 3, 1, (50 - 48) * 100.0 / 50);
        Assert.Equal(NetVerdict.LocalNetwork, (await Run()).Verdict);
        _sys.PingResults[Gw] = new PingStats(Gw, 100, 98, 3, 1, (100 - 98) * 100.0 / 100);             // two of a hundred is 2 %: under the limit
        Assert.Equal(NetVerdict.Healthy, (await Run()).Verdict);

        // the same on the internet side
        _sys.PingResults[Gw] = Ok(Gw, 2, 1, 0);
        _sys.PingResults[One] = new PingStats(One, 50, 49, 20, 2, 2.0);
        Assert.Equal(NetVerdict.Healthy, (await Run()).Verdict);
        _sys.PingResults[One] = new PingStats(One, 50, 48, 20, 2, 4.0);
        Assert.Equal(NetVerdict.Provider, (await Run()).Verdict);
    }

    [Theory]
    // sent, received | does the loss count as over the limit?  (needs AT LEAST TWO lost packets AND the limit of 3 % reached)
    [InlineData(30, 29, false)]                     // 3.3 %: over the percentage, but ONE lost echo is never a finding (a slow target gets fewer than ~34 pings in 6 s)
    [InlineData(30, 28, true)]                      // 6.7 %: two lost
    [InlineData(33, 32, false)]                     // 3.03 %: just over the percentage, still one echo
    [InlineData(20, 19, false)]                     // 5 %
    [InlineData(10, 9, false)]                      // 10 %
    [InlineData(2, 1, false)]                       // 50 %: one of two
    [InlineData(50, 49, false)]                     // 2 %
    [InlineData(50, 48, true)]                      // 4 %
    [InlineData(66, 64, true)]                      // exactly two lost, 3.03 %: the limit is reached
    [InlineData(67, 65, false)]                     // exactly two lost, 2.99 %: under the limit
    [InlineData(100, 97, true)]                     // three lost, exactly 3 %
    [InlineData(100, 98, false)]                    // two lost, 2 %
    [InlineData(5, 3, true)]                        // two lost of five: a target that fell silent after a few answers
    public async Task Loss_is_over_the_limit_only_with_at_least_two_lost_packets_and_at_least_three_percent(int sent, int received, bool over)
    {
        // the router
        _sys.PingResults[Gw] = Lost(Gw, sent, received);
        Assert.Equal(over ? NetVerdict.LocalNetwork : NetVerdict.Healthy, (await Run()).Verdict);

        // the internet, the router fine
        _sys.PingResults[Gw] = Ok(Gw, 2, 1, 0);
        _sys.PingResults[One] = Lost(One, sent, received);
        Assert.Equal(over ? NetVerdict.Provider : NetVerdict.Healthy, (await Run()).Verdict);

        // the second well-known address, the first one fine
        _sys.PingResults[One] = Ok(One, 20, 2, 0);
        _sys.PingResults[Two] = Lost(Two, sent, received);
        Assert.Equal(over ? NetVerdict.Provider : NetVerdict.Healthy, (await Run()).Verdict);
    }

    [Fact]
    public async Task One_lost_packet_of_a_slow_target_never_blames_the_home_network_or_the_provider()
    {
        // 30 pings sent in the 6 s budget, one echo lost: 3.3 %, over the percentage but a single packet
        _sys.PingResults[Gw] = Lost(Gw, 30, 29);
        _sys.PingResults[One] = Lost(One, 30, 29);
        _sys.PingResults[Two] = Lost(Two, 30, 29);
        var r = await Run();
        Assert.Equal(NetVerdict.Healthy, r.Verdict);
        Assert.Equal("Bağlantın sağlıklı görünüyor.", r.Headline);
        Assert.Contains("paket kaybı %3,3", r.Detail);                                // the number is shown as it is; it is just not a problem
    }

    [Fact]
    public async Task One_lost_packet_of_the_own_target_is_no_remark_and_no_evidence_against_the_internet_while_two_are()
    {
        _sys.PingResults[Own] = Lost(Own, 30, 29);
        var one = await Run(Own);
        Assert.Equal(NetVerdict.Healthy, one.Verdict);
        Assert.DoesNotContain("Ama play.example.net", one.Detail);

        _sys.PingResults[Own] = Lost(Own, 30, 28);
        var two = await Run(Own);
        Assert.Equal(NetVerdict.Healthy, two.Verdict);
        Assert.EndsWith("Ama play.example.net adresine giden yol daha kötü: ortalama 10 ms, paket kaybı %6,7, dalgalanma 1 ms.", two.Detail);

        // the own target as the only evidence of the internet (neither well-known address answers)
        Gateway();
        InternetSilent();
        _sys.PingResults[Own] = Lost(Own, 30, 29);
        Assert.Equal(NetVerdict.Healthy, (await Run(Own)).Verdict);
        _sys.PingResults[Own] = Lost(Own, 30, 28);
        Assert.Equal(NetVerdict.Provider, (await Run(Own)).Verdict);
    }

    [Fact]
    public async Task A_target_that_answers_with_some_loss_is_a_result_not_an_error()
    {
        Gateway(loss: 6);
        var r = await Run();
        Assert.Equal(NetVerdict.LocalNetwork, r.Verdict);
        var gateway = r.Targets.Single(t => t.Host == Gw).Stats;
        Assert.NotNull(gateway);
        Assert.Equal(6, gateway!.LossPercent);
    }

    [Fact]
    public async Task Nothing_answering_at_all_is_Unreachable()
    {
        _env.Dns = null;                                                            // the name lookup failed too
        _sys.PingResults[Gw] = Silent(Gw);
        InternetSilent();
        var r = await Run();
        Assert.Equal(NetVerdict.Unreachable, r.Verdict);
        Assert.All(r.Targets, t => Assert.Null(t.Stats));
        Assert.Equal("İnternete ulaşılamadı.", r.Headline);
        Assert.Contains("Ne modemden ne de internet adreslerinden yanıt geldi.", r.Detail);
        Assert.DoesNotMatch(@"\d+ ms", r.Detail);                                  // no number is made up for something that did not answer
    }

    [Fact]
    public async Task A_router_that_answers_while_the_internet_does_not_is_Unreachable_and_the_text_says_which_part_works()
    {
        _env.Dns = null;
        Gateway();
        InternetSilent();
        var r = await Run();
        Assert.Equal(NetVerdict.Unreachable, r.Verdict);
        Assert.NotNull(r.Targets.Single(t => t.Host == Gw).Stats);
        Assert.Equal(
            "Modem: ortalama 2 ms, paket kaybı %0, dalgalanma 1 ms; internet adreslerinden hiçbiri yanıt vermedi. " +
            "Modeme ulaşılıyor ama internete çıkılamıyor gibi görünüyor. " +
            "Modemi kapatıp 30 saniye sonra yeniden aç; düzelmezse internet sağlayıcını ara.", r.Detail);
    }

    [Fact]
    public async Task A_router_that_answers_badly_while_the_internet_is_silent_is_still_Unreachable_with_the_router_numbers_shown()
    {
        _env.Dns = null;
        Gateway(avg: 55, loss: 10);
        InternetSilent();
        var r = await Run();
        Assert.Equal(NetVerdict.Unreachable, r.Verdict);
        Assert.Contains("ortalama 55 ms, paket kaybı %10", r.Detail);
    }

    [Fact]
    public async Task A_name_lookup_that_throws_counts_as_failed_so_silent_pings_stay_Unreachable()
    {
        _env.DnsThrows = true;
        InternetSilent();
        var r = await Run();
        Assert.Null(r.DnsMs);
        Assert.Equal(NetVerdict.Unreachable, r.Verdict);
        Assert.Equal("İnternete ulaşılamadı.", r.Headline);
    }

    // ------------------------------------------------------------------ silent pings, but the name lookup works: pings may be blocked

    [Fact]
    public async Task Silent_pings_with_a_working_name_lookup_are_Unknown_with_a_hedged_text_and_no_modem_advice()
    {
        _env.Dns = 24;
        Gateway();
        InternetSilent();
        var r = await Run();
        Assert.Equal(NetVerdict.Unknown, r.Verdict);
        Assert.Equal(24.0, r.DnsMs);
        Assert.Equal("Ping internet adreslerine ulaşmadı, ama adres çözme çalışıyor.", r.Headline);
        Assert.Equal(
            "Modem: ortalama 2 ms, paket kaybı %0, dalgalanma 1 ms; internet adreslerinden hiçbiri yanıt vermedi. " +
            "Ping engelleniyor olabilir; internetin çalışıyor olabilir.", r.Detail);
        Assert.DoesNotContain("ulaşılamadı", r.Headline);                          // never the flat statement
        Assert.DoesNotContain("Modemi kapatıp", r.Detail);                         // no "restart the modem" advice for a line that may be working
        Assert.DoesNotContain("kopmuş", r.Detail);
    }

    [Fact]
    public async Task Silent_pings_with_a_working_name_lookup_say_which_part_was_silent_when_there_is_no_router_number()
    {
        Gateway(avg: 55, loss: 10);                                                 // a bad router keeps its numbers in the text
        InternetSilent();
        var bad = await Run();
        Assert.Equal(NetVerdict.Unknown, bad.Verdict);
        Assert.StartsWith("Modem: ortalama 55 ms, paket kaybı %10", bad.Detail);

        _sys.PingResults[Gw] = Silent(Gw);
        var silent = await Run();
        Assert.Equal(NetVerdict.Unknown, silent.Verdict);
        Assert.Equal("Ne modemden ne de internet adreslerinden yanıt geldi. Ping engelleniyor olabilir; internetin çalışıyor olabilir.", silent.Detail);

        _env.Gateway = null;
        var none = await Run();
        Assert.Equal(NetVerdict.Unknown, none.Verdict);
        Assert.Equal("İnternet adreslerinden hiçbiri yanıt vermedi. Ping engelleniyor olabilir; internetin çalışıyor olabilir.", none.Detail);
        Assert.DoesNotMatch(@"\d+ ms", none.Detail);
    }

    [Fact]
    public async Task The_hedged_text_in_English()
    {
        Loc.Use("en");
        Gateway();
        InternetSilent();
        var r = await Run();
        Assert.Equal(NetVerdict.Unknown, r.Verdict);
        Assert.Equal("Ping didn't reach the internet addresses, but name lookup works.", r.Headline);
        Assert.Equal(
            "Modem: average 2 ms, packet loss 0%, fluctuation 1 ms; none of the internet addresses answered. " +
            "Ping may be blocked; your internet may be working.", r.Detail);
    }

    [Fact]
    public async Task A_name_lookup_with_a_number_of_zero_still_counts_as_a_lookup_that_worked()
    {
        _env.Dns = 0;                                                               // answered from the Windows cache: that is a number, not "failed"
        InternetSilent();
        Assert.Equal(NetVerdict.Unknown, (await Run()).Verdict);
    }

    [Fact]
    public async Task Only_one_of_the_two_internet_addresses_answering_is_enough_to_judge()
    {
        _sys.PingResults[Two] = Silent(Two);
        var r = await Run();
        Assert.Equal(NetVerdict.Healthy, r.Verdict);
        Assert.Null(r.Targets.Single(t => t.Host == Two).Stats);
        Assert.NotNull(r.Targets.Single(t => t.Host == One).Stats);
    }

    [Fact]
    public async Task One_internet_address_over_the_limits_is_enough_for_the_provider_and_its_numbers_are_the_ones_shown()
    {
        Gateway();
        _sys.PingResults[One] = Ok(One, 20);
        _sys.PingResults[Two] = Ok(Two, avg: 120.4, jitter: 5, loss: 0);
        var r = await Run();
        Assert.Equal(NetVerdict.Provider, r.Verdict);
        Assert.Equal("Sorun büyük olasılıkla internet sağlayıcında.", r.Headline);
        Assert.Equal(
            "Modem: ortalama 2 ms, paket kaybı %0, dalgalanma 1 ms; internet: ortalama 120 ms, paket kaybı %0, dalgalanma 5 ms. " +
            "Modem sağlıklı, ama internete çıkışta oyunda hissedilebilecek kayıp ya da gecikme var. " +
            "Sorun sürerse internet sağlayıcını arayıp hattını sor.", r.Detail);
    }

    // ------------------------------------------------------------------ no router, a silent router

    [Fact]
    public async Task Without_a_router_the_internet_alone_is_judged_and_the_text_says_the_router_was_not_found()
    {
        _env.Gateway = null;
        _env.Wifi = true;                                         // a link kind without a router is not claimed
        var r = await Run();
        Assert.Equal(NetVerdict.Healthy, r.Verdict);
        Assert.False(r.Wireless);
        Assert.Equal(0, _env.WirelessCalls);
        Assert.Equal(new[] { One, Two }, r.Targets.Select(t => t.Host));
        Assert.Contains("Modem bulunamadı", r.Detail);
        Assert.StartsWith("İnternet: ortalama 12 ms", r.Detail);
    }

    [Fact]
    public async Task A_computer_with_only_a_VPN_has_no_router_and_a_broken_network_list_means_no_router_too()
    {
        _env.Gateway = null;                                       // what FindGateway says when the only route is a VPN
        Assert.DoesNotContain(Gw, (await Run()).Targets.Select(t => t.Host));
        _env.Gateway = Gw;
        _env.GatewayThrows = true;                                 // the list cannot be read: no router, no crash
        var r = await Run();
        Assert.Equal(NetVerdict.Healthy, r.Verdict);
        Assert.DoesNotContain(Gw, r.Targets.Select(t => t.Host));
    }

    [Fact]
    public async Task Without_a_router_and_with_a_bad_internet_the_place_of_the_problem_is_Unknown()
    {
        _env.Gateway = null;
        Internet(avg: 150);
        var r = await Run();
        Assert.Equal(NetVerdict.Unknown, r.Verdict);
        Assert.Equal("İnternetinde bir sorun var, ama nerede olduğu belli değil.", r.Headline);
        Assert.Contains("modem bulunamadığı için sorunun evde mi, internet sağlayıcında mı olduğu söylenemez", r.Detail);
        Assert.StartsWith("İnternet: ortalama 150 ms", r.Detail);
    }

    [Fact]
    public async Task A_router_that_does_not_answer_the_ping_is_not_blamed()
    {
        _sys.PingResults[Gw] = Silent(Gw);
        var healthy = await Run();
        Assert.Equal(NetVerdict.Healthy, healthy.Verdict);
        Assert.Null(healthy.Targets.Single(t => t.Host == Gw).Stats);
        Assert.Contains("Modem ping'e yanıt vermedi, bu yüzden ev ağı ayrı ölçülemedi", healthy.Detail);

        Internet(avg: 150);
        var unknown = await Run();
        Assert.Equal(NetVerdict.Unknown, unknown.Verdict);
        Assert.Contains("modem ping'e yanıt vermediği için", unknown.Detail);
    }

    [Fact]
    public async Task No_router_and_no_internet_is_Unreachable_with_a_text_that_shows_no_number()
    {
        _env.Gateway = null;
        _env.Dns = null;
        InternetSilent();
        var r = await Run();
        Assert.Equal(NetVerdict.Unreachable, r.Verdict);
        Assert.Equal(
            "İnternet adreslerinden hiçbiri yanıt vermedi. Bağlantın kopmuş olabilir. Wi-Fi'ın açık olduğunu ya da kablonun takılı olduğunu kontrol et.", r.Detail);
    }

    // ------------------------------------------------------------------ the targets

    [Fact]
    public async Task The_targets_are_the_router_two_internet_addresses_and_the_users_own_in_that_order()
    {
        var r = await Run(Own);
        Assert.Equal(new[] { Gw, One, Two, Own }, r.Targets.Select(t => t.Host));
        Assert.Equal(new[] { "Modemin / yönlendiricin", "İnternet (1.1.1.1)", "İnternet (8.8.8.8)", "Ping hedefin (play.example.net)" }, r.Targets.Select(t => t.Label));
        Assert.All(r.Targets, t => Assert.NotNull(t.Stats));
        Assert.Equal(new[] { Gw, One, Two, Own }, _sys.PingedHosts);                  // each once, started in that order
    }

    [Fact]
    public async Task No_extra_target_means_three_targets_with_a_router_and_two_without()
    {
        var r = await Run(null);
        Assert.Equal(new[] { Gw, One, Two }, r.Targets.Select(t => t.Host));
        _env.Gateway = null;
        Assert.Equal(new[] { One, Two }, (await Run(null)).Targets.Select(t => t.Host));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("1.1.1.1")]
    [InlineData("  1.1.1.1 ")]
    [InlineData("8.8.8.8")]
    [InlineData("192.168.1.1")]
    [InlineData(" 192.168.1.1")]
    public async Task A_blank_extra_target_or_one_that_is_already_there_is_not_pinged_twice(string? extra)
    {
        var r = await Run(extra);
        Assert.Equal(new[] { Gw, One, Two }, r.Targets.Select(t => t.Host));
        Assert.Equal(new[] { Gw, One, Two }, _sys.PingedHosts);
    }

    [Fact]
    public async Task The_extra_target_is_trimmed_and_every_target_gets_the_same_number_of_pings()
    {
        var counts = new ConcurrentBag<int>();
        var hosts = new ConcurrentBag<string>();
        _sys.PingHandler = (host, count, ct) => { counts.Add(count); hosts.Add(host); return Task.FromResult(Ok(host)); };
        var r = await Run("  " + Own + " ");
        Assert.Equal(Own, r.Targets[^1].Host);
        Assert.Contains(Own, hosts);
        Assert.Equal(4, counts.Count);
        Assert.All(counts, c => Assert.Equal(NetCheck.PingCount, c));
    }

    [Fact]
    public async Task A_target_that_does_not_answer_or_does_not_exist_has_no_stats_whatever_the_system_says()
    {
        _sys.PingResults[Two] = new PingStats(Two, 3, 0, 0, 0, 100, "TimedOut");                    // answered nothing, with an error text
        _sys.PingHandler = (host, count, ct) =>
        {
            if (host == Own) throw new InvalidOperationException("the name does not exist (test)");
            return Task.FromResult(_sys.PingResults.TryGetValue(host, out var configured) ? configured : Ok(host));
        };
        var r = await Run(Own);
        Assert.Null(r.Targets.Single(t => t.Host == Two).Stats);
        Assert.Null(r.Targets.Single(t => t.Host == Own).Stats);
        Assert.NotNull(r.Targets.Single(t => t.Host == One).Stats);
        Assert.Equal(NetVerdict.Healthy, r.Verdict);
        Assert.Contains("play.example.net adresi ping'e yanıt vermedi", r.Detail);               // a silent own target is mentioned, it does not decide
    }

    [Fact]
    public async Task All_targets_are_pinged_at_the_same_time()
    {
        var gate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var seen = 0;
        _sys.PingHandler = async (host, count, ct) =>
        {
            if (Interlocked.Increment(ref seen) == 4) gate.SetResult(true);               // the fourth ping asks: only then does any of them finish
            await gate.Task;
            return Ok(host);
        };
        var run = Run(Own);
        var first = await Task.WhenAny(run, Task.Delay(TimeSpan.FromSeconds(10)));
        Assert.Same(run, first);
        Assert.Equal(4, (await run).Targets.Count);
    }

    // ------------------------------------------------------------------ the user's own target

    [Fact]
    public async Task A_far_or_lossy_own_target_never_makes_a_healthy_connection_sick_but_is_mentioned()
    {
        _sys.PingResults[Own] = Ok(Own, avg: 150, jitter: 3, loss: 0);
        var r = await Run(Own);
        Assert.Equal(NetVerdict.Healthy, r.Verdict);
        Assert.EndsWith("Ama play.example.net adresine giden yol daha kötü: ortalama 150 ms, paket kaybı %0, dalgalanma 3 ms.", r.Detail);
    }

    [Fact]
    public async Task A_good_own_target_adds_nothing()
    {
        _sys.PingResults[Own] = Ok(Own, 25);
        var with = await Run(Own);
        var without = await Run();
        Assert.Equal(without.Detail, with.Detail);
        Assert.Equal(without.Headline, with.Headline);
    }

    [Fact]
    public async Task The_own_target_stands_in_for_the_internet_only_when_neither_well_known_address_answers()
    {
        Gateway();
        InternetSilent();
        _sys.PingResults[Own] = Ok(Own, 25);
        var fine = await Run(Own);
        Assert.Equal(NetVerdict.Healthy, fine.Verdict);                      // the internet works, only the two addresses stay quiet
        Assert.StartsWith("Modem: ortalama 2 ms, paket kaybı %0, dalgalanma 1 ms; internet: ortalama 25 ms", fine.Detail);

        _sys.PingResults[Own] = Ok(Own, 150);
        Assert.Equal(NetVerdict.Provider, (await Run(Own)).Verdict);

        _sys.PingResults[Own] = Silent(Own);
        Assert.Equal(NetVerdict.Unknown, (await Run(Own)).Verdict);              // silence everywhere, but the name lookup works: pings may be blocked
        _env.Dns = null;
        Assert.Equal(NetVerdict.Unreachable, (await Run(Own)).Verdict);          // and when the name lookup fails as well
    }

    [Theory]
    [InlineData("1.1.1.2")]
    [InlineData("8.8.4.4")]
    [InlineData("172.15.255.255")]                  // just below 172.16/12
    [InlineData("172.32.0.1")]                      // just above it
    [InlineData("100.63.255.255")]                  // just below the provider space 100.64/10
    [InlineData("100.128.0.1")]                     // just above it
    [InlineData("169.253.1.1")]
    [InlineData("192.169.0.1")]
    [InlineData("11.0.0.1")]
    [InlineData("223.255.255.254")]
    [InlineData("2606:4700:4700::1111")]
    [InlineData("2001:4860:4860::8888")]
    [InlineData("play.example.net")]
    [InlineData("mylocalhost.example.net")]
    [InlineData("PLAY.Example.NET.")]                // letter case and a trailing dot do not matter
    [InlineData("example.com")]
    // a name only loses its place as evidence when it ENDS in a private ending (after a dot) or has no dot at all
    [InlineData("local.example.com")]
    [InlineData("notlocal.com")]
    [InlineData("game.mylocal")]
    [InlineData("my.corp.example.com")]
    [InlineData("homelan.net")]
    [InlineData("home.example.org")]
    [InlineData("lan.example.org")]
    [InlineData("internal.example.net")]
    [InlineData("intranet.example.net")]
    [InlineData("private.example.net")]
    [InlineData("home.arpa.example.net")]
    public async Task A_public_address_or_a_name_stands_in_as_evidence_when_neither_well_known_address_answers(string own)
    {
        Gateway();
        InternetSilent();
        _sys.PingResults[own] = Ok(own, 25);
        var r = await Run(own);
        Assert.Equal(NetVerdict.Healthy, r.Verdict);
        Assert.Contains("internet: ortalama 25 ms", r.Detail);
        _sys.PingResults[own] = Ok(own, 150);
        Assert.Equal(NetVerdict.Provider, (await Run(own)).Verdict);
    }

    [Theory]
    [InlineData("127.0.0.1")]
    [InlineData("127.1.2.3")]
    [InlineData("::1")]
    [InlineData("localhost")]
    [InlineData("LocalHost.")]
    [InlineData("test.localhost")]
    [InlineData("10.0.0.5")]
    [InlineData("10.255.255.255")]
    [InlineData("172.16.0.1")]
    [InlineData("172.31.255.255")]
    [InlineData("192.168.0.9")]
    [InlineData("192.168.1.77")]
    [InlineData("169.254.1.1")]
    [InlineData("fe80::1")]
    [InlineData("fe80::1%12")]
    [InlineData("fd00::1")]
    [InlineData("fc00::5")]
    [InlineData("0.0.0.0")]
    [InlineData("::")]
    [InlineData("::ffff:192.168.1.77")]            // an IPv4 address written the IPv6 way
    [InlineData("::ffff:127.0.0.1")]
    [InlineData("100.64.0.1")]                      // provider space: not an internet server
    [InlineData("224.0.0.1")]
    [InlineData("255.255.255.255")]
    // names that cannot be internet servers: a single word, and the endings of home and company networks (still pinged and shown, just no evidence)
    [InlineData("router")]
    [InlineData("nas")]
    [InlineData("ROUTER.")]                         // letter case and a trailing dot do not matter
    [InlineData("Gaming-PC")]
    [InlineData("mypc.local")]
    [InlineData("MyPc.LOCAL.")]
    [InlineData("deep.name.with.many.labels.local")]
    [InlineData("printer.lan")]
    [InlineData("tv.home")]
    [InlineData("server.internal")]
    [InlineData("pc.localdomain")]
    [InlineData("fritz.home.arpa")]
    [InlineData("home.arpa")]
    [InlineData("HOME.ARPA.")]
    [InlineData("files.corp")]
    [InlineData("wiki.intranet")]
    [InlineData("box.private")]
    public async Task An_address_inside_this_computer_or_the_home_network_never_counts_as_internet_evidence(string own)
    {
        _env.Dns = null;
        Gateway();
        InternetSilent();
        _sys.PingResults[own] = Ok(own, 5);                                       // it answers, as such addresses do whether the internet works or not
        var r = await Run(own);
        Assert.Equal(NetVerdict.Unreachable, r.Verdict);                          // judged as if it were not there
        Assert.Equal("İnternete ulaşılamadı.", r.Headline);
        Assert.NotNull(r.Targets.Single(t => t.Host == own).Stats);               // it was still pinged and is still shown
        Assert.Contains(own, _sys.PingedHosts);

        _env.Dns = 30;
        Assert.Equal(NetVerdict.Unknown, (await Run(own)).Verdict);               // and the hedge works the same way
    }

    [Fact]
    public async Task A_private_own_target_does_not_change_a_healthy_result_but_is_mentioned_when_it_is_worse()
    {
        const string home = "192.168.1.77";
        _sys.PingResults[home] = Ok(home, avg: 150, jitter: 3, loss: 0);
        var r = await Run(home);
        Assert.Equal(NetVerdict.Healthy, r.Verdict);
        Assert.EndsWith("Ama 192.168.1.77 adresine giden yol daha kötü: ortalama 150 ms, paket kaybı %0, dalgalanma 3 ms.", r.Detail);
    }

    [Fact]
    public async Task A_home_network_name_does_not_decide_but_is_mentioned_when_it_is_worse_or_silent()
    {
        const string nas = "nas.local";
        _sys.PingResults[nas] = Ok(nas, avg: 150, jitter: 3, loss: 0);
        var worse = await Run(nas);
        Assert.Equal(NetVerdict.Healthy, worse.Verdict);
        Assert.EndsWith("Ama nas.local adresine giden yol daha kötü: ortalama 150 ms, paket kaybı %0, dalgalanma 3 ms.", worse.Detail);

        _sys.PingResults["router"] = Silent("router");                              // a single word: also a name from the home network
        var silent = await Run("router");
        Assert.Equal(NetVerdict.Healthy, silent.Verdict);
        Assert.EndsWith("router adresi ping'e yanıt vermedi; bazı sunucular ping'i kapatır.", silent.Detail);

        Internet(avg: 150);                                                         // the well-known addresses decide, whatever the home names say
        _sys.PingResults[nas] = Ok(nas, 5);
        Assert.Equal(NetVerdict.Provider, (await Run(nas)).Verdict);
    }

    [Fact]
    public async Task A_private_own_target_never_makes_a_healthy_internet_sick_or_hides_a_bad_one()
    {
        const string home = "10.0.0.5";
        _sys.PingResults[home] = Ok(home, avg: 500, jitter: 90, loss: 40);
        Assert.Equal(NetVerdict.Healthy, (await Run(home)).Verdict);              // the two well-known addresses answered: they decide
        Internet(avg: 150);
        Assert.Equal(NetVerdict.Provider, (await Run(home)).Verdict);
    }

    // ------------------------------------------------------------------ the name lookup

    [Fact]
    public async Task One_name_lookup_is_made_and_its_time_is_reported_as_it_is()
    {
        _env.Dns = 37.5;
        var r = await Run();
        Assert.Equal(37.5, r.DnsMs);
        Assert.Equal(new[] { "steampowered.com" }, _env.DnsHosts);
        Assert.Equal("steampowered.com", NetCheck.DnsHost);
    }

    [Fact]
    public async Task A_lookup_that_fails_or_throws_is_left_out_and_never_breaks_the_check()
    {
        _env.Dns = null;
        var failed = await Run();
        Assert.Null(failed.DnsMs);
        Assert.Equal(NetVerdict.Unknown, failed.Verdict);                          // the pings are fine, but "healthy" would say more than was measured
        Assert.Equal(3, failed.Targets.Count(t => t.Stats != null));              // the pings are still all there

        _env.Dns = 20;
        _env.DnsThrows = true;
        var thrown = await Run();
        Assert.Null(thrown.DnsMs);
        Assert.Equal(NetVerdict.Unknown, thrown.Verdict);
        Assert.Equal(3, thrown.Targets.Count(t => t.Stats != null));
    }

    // ------------------------------------------------------------------ fine pings, but the name lookup did not answer: not "healthy"

    [Fact]
    public async Task Fine_pings_and_a_name_lookup_that_did_not_answer_are_Unknown_with_a_hedged_text_and_no_advice()
    {
        _env.Dns = null;
        Gateway();
        Internet();
        var r = await Run();
        Assert.Null(r.DnsMs);
        Assert.Equal(NetVerdict.Unknown, r.Verdict);
        Assert.Equal("Ping çalışıyor ama adres çözme yanıt vermedi.", r.Headline);
        Assert.Equal(
            "Modem: ortalama 2 ms, paket kaybı %0, dalgalanma 1 ms; internet: ortalama 20 ms, paket kaybı %0, dalgalanma 2 ms. " +
            "Bu, sitelerin ve oyun sunucularının adlarının bulunamaması anlamına gelebilir; bir kez daha dene.", r.Detail);
        Assert.Equal(1, Sentences(r.Headline));
        Assert.Equal(2, Sentences(r.Detail));                                      // the numbers, then ONE sentence
        Assert.DoesNotContain("sağlıklı", r.Headline + " " + r.Detail);            // never the green sentence
        Assert.DoesNotContain("Modemi kapatıp", r.Detail);                         // no "restart the modem" advice, no blame
        Assert.DoesNotContain("sağlayıcın", r.Detail);
        Assert.Equal(3, r.Targets.Count(t => t.Stats != null));                    // the ping rows are still all shown
    }

    [Fact]
    public async Task The_name_lookup_hedge_in_English()
    {
        Loc.Use("en");
        _env.Dns = null;
        Gateway();
        Internet();
        var r = await Run();
        Assert.Equal(NetVerdict.Unknown, r.Verdict);
        Assert.Equal("Ping works, but the name lookup didn't answer.", r.Headline);
        Assert.Equal(
            "Modem: average 2 ms, packet loss 0%, fluctuation 1 ms; internet: average 20 ms, packet loss 0%, fluctuation 2 ms. " +
            "This can mean that the names of websites and game servers can't be found; try once more.", r.Detail);
        Assert.Equal(1, Sentences(r.Headline));
        Assert.Equal(2, Sentences(r.Detail));
    }

    [Fact]
    public async Task The_name_lookup_hedge_names_only_the_addresses_that_answered_when_the_router_was_not_measured()
    {
        _env.Dns = null;
        _sys.PingResults[Gw] = Silent(Gw);                                          // the router does not answer the ping
        var silent = await Run();
        Assert.Equal(NetVerdict.Unknown, silent.Verdict);
        Assert.Equal(
            "İnternet: ortalama 12 ms, paket kaybı %0, dalgalanma 1 ms. " +
            "Bu, sitelerin ve oyun sunucularının adlarının bulunamaması anlamına gelebilir; bir kez daha dene.", silent.Detail);

        _env.Gateway = null;                                                        // no router found
        var none = await Run();
        Assert.Equal(NetVerdict.Unknown, none.Verdict);
        Assert.Equal(silent.Detail, none.Detail);
        Assert.Equal(silent.Headline, none.Headline);
    }

    [Fact]
    public async Task The_name_lookup_hedge_says_nothing_about_the_own_target_and_a_lookup_with_a_number_stays_healthy()
    {
        _env.Dns = null;
        _sys.PingResults[Own] = Silent(Own);
        var hedged = await Run(Own);
        Assert.Equal(NetVerdict.Unknown, hedged.Verdict);
        Assert.Equal(2, Sentences(hedged.Detail));
        Assert.DoesNotContain("play.example.net", hedged.Detail);                   // the row of the list shows it

        _env.Dns = 24;                                                              // the same pings, but the lookup answered: as before
        var healthy = await Run(Own);
        Assert.Equal(NetVerdict.Healthy, healthy.Verdict);
        Assert.Equal("Bağlantın sağlıklı görünüyor.", healthy.Headline);
        _env.Dns = 0;                                                               // answered from the Windows cache: a number, not "no answer"
        Assert.Equal(NetVerdict.Healthy, (await Run(Own)).Verdict);
    }

    [Fact]
    public async Task A_name_lookup_that_did_not_answer_changes_no_other_verdict_or_text()
    {
        _env.Dns = null;
        Gateway(avg: 45);                                                           // the router is over its limit
        Internet();
        var local = await Run();
        Assert.Equal(NetVerdict.LocalNetwork, local.Verdict);
        Assert.Equal("Sorun büyük olasılıkla evdeki ağda (Wi-Fi ya da modem).", local.Headline);

        Gateway();
        Internet(avg: 150);                                                         // the internet is over its limit, the router fine
        var provider = await Run();
        Assert.Equal(NetVerdict.Provider, provider.Verdict);
        Assert.Equal("Sorun büyük olasılıkla internet sağlayıcında.", provider.Headline);

        _env.Gateway = null;                                                        // the internet is over its limit, no router to compare
        var unknown = await Run();
        Assert.Equal(NetVerdict.Unknown, unknown.Verdict);
        Assert.Equal("İnternetinde bir sorun var, ama nerede olduğu belli değil.", unknown.Headline);

        _env.Gateway = Gw;                                                          // nothing on the internet answers, and no name either
        InternetSilent();
        var unreachable = await Run();
        Assert.Equal(NetVerdict.Unreachable, unreachable.Verdict);
        Assert.Equal("İnternete ulaşılamadı.", unreachable.Headline);
    }

    [Theory]
    [InlineData(0.0, false)]                         // answered from the Windows cache
    [InlineData(350.0, false)]                       // a normal cold lookup
    [InlineData(999.9, false)]
    [InlineData(1000.0, true)]
    [InlineData(1200.0, true)]
    public async Task A_slow_lookup_gets_one_remark_when_everything_else_is_healthy(double dns, bool remark)
    {
        _env.Dns = dns;
        var r = await Run();
        Assert.Equal(NetVerdict.Healthy, r.Verdict);
        Assert.Equal(remark, r.Detail.Contains("Adres çözme yavaş"));
    }

    [Fact]
    public async Task The_slow_lookup_remark_in_both_languages()
    {
        _env.Dns = 1500;
        var tr = await Run();
        Assert.EndsWith("Adres çözme yavaş (1500 ms): bu, oyuna girişi geciktirebilir. Sorun sürerse internet sağlayıcına danış.", tr.Detail);
        Loc.Use("en");
        var en = await Run();
        Assert.EndsWith("Name lookup is slow (1500 ms): it can delay getting into a game. If it continues, ask your internet provider.", en.Detail);
    }

    [Fact]
    public async Task A_slow_lookup_is_not_mentioned_when_the_advice_has_the_room_and_the_own_target_remark_comes_first()
    {
        _env.Dns = 1500;
        Internet(avg: 150);
        var provider = await Run();
        Assert.Equal(NetVerdict.Provider, provider.Verdict);
        Assert.DoesNotContain("Adres çözme yavaş", provider.Detail);

        Internet();
        _sys.PingResults[Own] = Ok(Own, 150);
        var healthy = await Run(Own);
        Assert.Contains("Ama play.example.net adresine", healthy.Detail);
        Assert.DoesNotContain("Adres çözme yavaş", healthy.Detail);

        InternetSilent();                                                          // silent pings: the hedged text has no room for it either
        _sys.PingResults[Own] = Silent(Own);
        var hedged = await Run(Own);
        Assert.Equal(NetVerdict.Unknown, hedged.Verdict);
        Assert.DoesNotContain("Adres çözme yavaş", hedged.Detail);
    }

    // ------------------------------------------------------------------ the number of the name lookup is printed like the sentence

    [Theory]
    [InlineData(0.0, "0", "0")]
    [InlineData(2.4, "2,4", "2.4")]
    [InlineData(2.25, "2,3", "2.3")]                // halves go up
    [InlineData(12.0, "12", "12")]
    [InlineData(99.94, "99,9", "99.9")]
    [InlineData(99.96, "100", "100")]               // rounds up into the whole numbers
    [InlineData(100.4, "100", "100")]
    [InlineData(120.5, "121", "121")]
    [InlineData(1500.0, "1500", "1500")]
    public void FormatMs_is_one_decimal_under_100_and_whole_numbers_above_in_the_language_on(double ms, string turkish, string english)
    {
        CultureInfo.CurrentCulture = new CultureInfo("de-DE");                    // the machine's own number format must not decide
        Loc.Use("tr");
        Assert.Equal(turkish, NetCheck.FormatMs(ms));
        Loc.Use("en");
        CultureInfo.CurrentCulture = new CultureInfo("tr-TR");
        Assert.Equal(english, NetCheck.FormatMs(ms));
    }

    [Fact]
    public async Task A_row_with_FormatMs_prints_exactly_the_number_of_the_sentence()
    {
        _env.Dns = 1234.56;
        var r = await Run();
        Assert.Contains($"({NetCheck.FormatMs(r.DnsMs!.Value)} ms)", r.Detail);
        Assert.Contains("(1235 ms)", r.Detail);
    }

    // ------------------------------------------------------------------ Wi-Fi

    [Fact]
    public async Task The_cable_advice_appears_only_for_a_wireless_home_network_problem()
    {
        _env.Wifi = true;
        Gateway(avg: 45);
        var wifi = await Run();
        Assert.Equal(NetVerdict.LocalNetwork, wifi.Verdict);
        Assert.True(wifi.Wireless);
        Assert.Contains("Mümkünse bilgisayarını modeme kabloyla (Ethernet) bağla.", wifi.Detail);
        Assert.DoesNotContain("Modemi kapatıp", wifi.Detail);

        _env.Wifi = false;
        var wired = await Run();
        Assert.Equal(NetVerdict.LocalNetwork, wired.Verdict);
        Assert.False(wired.Wireless);
        Assert.DoesNotContain("kabloyla", wired.Detail);
        Assert.EndsWith("Modemi kapatıp 30 saniye sonra yeniden aç.", wired.Detail);
    }

    [Fact]
    public async Task Wi_Fi_is_never_blamed_when_the_home_network_is_not_the_problem()
    {
        _env.Wifi = true;
        var healthy = await Run();
        Assert.True(healthy.Wireless);                                        // the fact is reported, the advice is not given
        Assert.DoesNotContain("kablo", healthy.Detail);

        Internet(avg: 150);
        var provider = await Run();
        Assert.Equal(NetVerdict.Provider, provider.Verdict);
        Assert.DoesNotContain("kablo", provider.Detail);

        InternetSilent();
        var blocked = await Run();                                                // the name lookup works: the hedged text
        Assert.Equal(NetVerdict.Unknown, blocked.Verdict);
        Assert.DoesNotContain("kabloyla", blocked.Detail);

        _env.Dns = null;
        var unreachable = await Run();
        Assert.Equal(NetVerdict.Unreachable, unreachable.Verdict);
        Assert.DoesNotContain("kabloyla", unreachable.Detail);
    }

    // ------------------------------------------------------------------ the game

    [Fact]
    public async Task While_the_game_runs_nothing_is_read_and_nothing_is_sent()
    {
        _game = true;
        await Assert.ThrowsAsync<GameRunningException>(() => Run(Own));
        Assert.Empty(_sys.PingedHosts);
        Assert.Equal(0, _env.GatewayCalls);
        Assert.Equal(0, _env.WirelessCalls);
        Assert.Empty(_env.DnsHosts);
    }

    [Fact]
    public async Task The_game_is_asked_again_right_before_the_first_packet()
    {
        var asked = 0;
        var check = new NetCheck(_sys, () => ++asked == 2, _env);                // not running at the start, running when asked the second time
        await Assert.ThrowsAsync<GameRunningException>(() => check.RunAsync(Own, null, default));
        Assert.Equal(2, asked);
        Assert.Empty(_sys.PingedHosts);
        Assert.Empty(_env.DnsHosts);
    }

    [Fact]
    public async Task A_game_that_starts_in_the_last_moment_of_the_wait_drops_the_result()
    {
        var asked = 0;
        var check = new NetCheck(_sys, () => ++asked >= 3, _env);                // the fake pings answer at once: the third look is the final one
        await Assert.ThrowsAsync<GameRunningException>(() => check.RunAsync(null, null, default));
        Assert.Equal(3, asked);
        Assert.Equal(3, _sys.PingedHosts.Count);                                  // they were sent while the game was not there yet
    }

    [Fact]
    public async Task A_game_that_starts_while_a_ping_is_still_pending_stops_the_pings_and_the_name_lookup_and_gives_no_result()
    {
        var pings = new PendingPings();
        _sys.PingHandler = pings.Handle;
        _env.DnsHandler = ct =>                                                    // a name lookup that is still waiting too
        {
            var pending = new TaskCompletionSource<double?>(TaskCreationOptions.RunContinuationsAsynchronously);
            ct.Register(() => pending.TrySetCanceled(ct));
            return pending.Task;
        };
        var calls = 0;
        // 1: at the start, 2: right before the first packet, 3: after about half a second (not yet), 4: after about a second (there)
        var check = new NetCheck(_sys, () => Interlocked.Increment(ref calls) >= 4, _env);
        var watch = Stopwatch.StartNew();
        await Assert.ThrowsAsync<GameRunningException>(() => check.RunAsync(Own, null, default));
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(5), $"took {watch.Elapsed}");

        Assert.Equal(4, calls);                                                    // the game is asked once per tick, not more, and not after it was found
        Assert.Equal(4, pings.Tokens.Count);                                       // router, two internet addresses, the own target
        Assert.All(pings.Tokens, t => Assert.True(t.IsCancellationRequested, "a ping still has a live token"));
        Assert.Single(_env.DnsTokens);
        Assert.True(_env.DnsTokens[0].IsCancellationRequested, "the name lookup still has a live token");
        Assert.Equal(4, _sys.PingedHosts.Count);                                   // nothing was started after the game was found
    }

    [Fact]
    public async Task A_name_lookup_that_is_still_running_is_watched_too()
    {
        _env.DnsHandler = ct =>
        {
            var pending = new TaskCompletionSource<double?>(TaskCreationOptions.RunContinuationsAsynchronously);
            ct.Register(() => pending.TrySetCanceled(ct));
            return pending.Task;
        };
        var calls = 0;
        var check = new NetCheck(_sys, () => Interlocked.Increment(ref calls) >= 3, _env);        // the pings are done at once; the first tick finds the game
        await Assert.ThrowsAsync<GameRunningException>(() => check.RunAsync(null, null, default));
        Assert.Equal(3, calls);
        Assert.True(_env.DnsTokens.Single().IsCancellationRequested);
    }

    [Fact]
    public async Task A_ping_that_ignores_its_token_cannot_hold_the_game_exception_up()
    {
        var stuck = new TaskCompletionSource<PingStats>();
        var tokens = new ConcurrentBag<CancellationToken>();
        _sys.PingHandler = (host, count, ct) => { tokens.Add(ct); return stuck.Task; };            // never answers and never looks at the token
        var calls = 0;
        var check = new NetCheck(_sys, () => Interlocked.Increment(ref calls) >= 3, _env);
        var watch = Stopwatch.StartNew();
        await Assert.ThrowsAsync<GameRunningException>(() => check.RunAsync(null, null, default));
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(5), $"took {watch.Elapsed}");
        Assert.All(tokens, t => Assert.True(t.IsCancellationRequested));
        stuck.TrySetResult(Ok("done"));                                            // let it finish: a late answer must not surface anywhere
        await Task.Delay(50);
    }

    [Fact]
    public async Task The_game_is_asked_about_twice_a_second_while_the_pings_run_and_not_in_between()
    {
        // every ping answers after 1.3 s (and ignores the token): ticks at about 0.5 s and 1.0 s, then the pings are done and the final look follows
        _sys.PingHandler = async (host, count, ct) => { await Task.Delay(1300); return Ok(host); };
        var calls = 0;
        var check = new NetCheck(_sys, () => { Interlocked.Increment(ref calls); return false; }, _env);
        var report = await check.RunAsync(null, null, default);
        Assert.Equal(NetVerdict.Healthy, report.Verdict);
        Assert.InRange(calls, 3, 6);                                               // start + before the first packet + final look = 3, plus one per tick (two, three on a slow machine)
    }

    [Fact]
    public async Task A_game_check_that_breaks_while_the_pings_run_stops_them_and_is_not_swallowed()
    {
        var pings = new PendingPings();
        _sys.PingHandler = pings.Handle;
        var calls = 0;
        var check = new NetCheck(_sys, () => Interlocked.Increment(ref calls) < 3 ? false : throw new InvalidOperationException("the process list is broken (test)"), _env);
        await Assert.ThrowsAsync<InvalidOperationException>(() => check.RunAsync(null, null, default));
        Assert.All(pings.Tokens, t => Assert.True(t.IsCancellationRequested));     // nothing keeps sending when the check cannot ask any more
    }

    [Fact]
    public async Task The_game_check_is_required()
    {
        Assert.Throws<ArgumentNullException>(() => new NetCheck(_sys, null!, _env));
        Assert.Throws<ArgumentNullException>(() => new NetCheck(null!, () => false, _env));
        await Task.CompletedTask;
    }

    // ------------------------------------------------------------------ cancellation

    [Fact]
    public async Task A_token_that_is_already_cancelled_stops_before_any_packet()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        await Assert.ThrowsAsync<OperationCanceledException>(() => Run(Own, null, cts.Token));
        Assert.Empty(_sys.PingedHosts);
        Assert.Empty(_env.DnsHosts);
    }

    [Fact]
    public async Task Cancelling_returns_at_once_even_when_a_ping_is_stuck()
    {
        var stuck = new TaskCompletionSource<PingStats>();
        _sys.PingHandler = (host, count, ct) => stuck.Task;                      // never answers and ignores the token
        using var cts = new CancellationTokenSource();
        var run = Run(Own, null, cts.Token);
        await WaitUntil(() => { lock (_sys.PingedHosts) return _sys.PingedHosts.Count == 4; });
        var watch = Stopwatch.StartNew();
        cts.Cancel();
        await Assert.ThrowsAsync<OperationCanceledException>(() => run);          // the plain exception, not a subclass
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(2), $"took {watch.Elapsed}");
        stuck.TrySetResult(Ok("done"));
    }

    [Fact]
    public async Task Cancelling_a_ping_that_listens_to_the_token_gives_the_same_plain_exception()
    {
        _sys.PingHandler = async (host, count, ct) => { await Task.Delay(Timeout.Infinite, ct); return Ok(host); };
        using var cts = new CancellationTokenSource();
        var run = Run(null, null, cts.Token);
        await WaitUntil(() => { lock (_sys.PingedHosts) return _sys.PingedHosts.Count == 3; });
        cts.Cancel();
        await Assert.ThrowsAsync<OperationCanceledException>(() => run);
    }

    [Fact]
    public async Task Cancelling_by_the_caller_cancels_every_ping_and_the_name_lookup_and_never_asks_the_game_in_a_loop()
    {
        var pings = new PendingPings();
        _sys.PingHandler = pings.Handle;
        _env.DnsHandler = ct =>
        {
            var pending = new TaskCompletionSource<double?>(TaskCreationOptions.RunContinuationsAsynchronously);
            ct.Register(() => pending.TrySetCanceled(ct));
            return pending.Task;
        };
        var calls = 0;
        var check = new NetCheck(_sys, () => { Interlocked.Increment(ref calls); return false; }, _env);
        using var cts = new CancellationTokenSource();
        var run = check.RunAsync(Own, null, cts.Token);
        await WaitUntil(() => pings.Tokens.Count == 4 && _env.DnsTokens.Count == 1);
        cts.Cancel();
        await Assert.ThrowsAsync<OperationCanceledException>(() => run);           // the plain exception
        Assert.All(pings.Tokens, t => Assert.True(t.IsCancellationRequested));
        Assert.True(_env.DnsTokens[0].IsCancellationRequested);
        Assert.InRange(calls, 2, 3);                                               // start and before the first packet; maybe one tick; no final look after a cancel
    }

    // ------------------------------------------------------------------ progress

    [Fact]
    public async Task Progress_is_one_line_for_the_wait_and_one_for_the_end_in_Turkish()
    {
        var progress = new Collect();
        await Run(Own, progress);
        Assert.Equal(new[] { "Ping atılıyor; birkaç saniye sürer…", "Sonuç hazırlanıyor…" }, progress.Items);

        _env.Gateway = null;                                                       // the same two lines whatever is pinged
        var short1 = new Collect();
        await Run(null, short1);
        Assert.Equal(new[] { "Ping atılıyor; birkaç saniye sürer…", "Sonuç hazırlanıyor…" }, short1.Items);
    }

    [Fact]
    public async Task Progress_is_reported_in_English_when_English_is_on()
    {
        Loc.Use("en");
        var progress = new Collect();
        await Run(Own, progress);
        Assert.Equal(new[] { "Pinging; this takes a few seconds…", "Preparing the result…" }, progress.Items);
    }

    [Fact]
    public async Task The_waiting_line_comes_before_the_first_packet_and_the_end_line_only_after_the_wait()
    {
        var order = new List<string>();
        var progress = new Collect();
        _sys.PingHandler = (host, count, ct) =>
        {
            lock (progress.Items) order.Add("ping " + progress.Items.Count);       // how many lines were reported when this packet went out
            return Task.FromResult(Ok(host));
        };
        await Run(Own, progress);
        Assert.All(order, o => Assert.Equal("ping 1", o));                          // the waiting line was already out
        Assert.Equal(4, order.Count);
        Assert.Equal("Sonuç hazırlanıyor…", progress.Items[^1]);
    }

    [Fact]
    public async Task A_game_that_shows_up_during_the_wait_reports_the_waiting_line_but_never_the_end_line()
    {
        var pings = new PendingPings();
        _sys.PingHandler = pings.Handle;
        var progress = new Collect();
        var calls = 0;
        var check = new NetCheck(_sys, () => Interlocked.Increment(ref calls) >= 3, _env);
        await Assert.ThrowsAsync<GameRunningException>(() => check.RunAsync(null, progress, default));
        Assert.Equal(new[] { "Ping atılıyor; birkaç saniye sürer…" }, progress.Items);
    }

    [Fact]
    public async Task A_run_without_a_listener_works_and_a_game_that_is_up_reports_nothing()
    {
        Assert.Equal(NetVerdict.Healthy, (await Run()).Verdict);
        _game = true;
        var progress = new Collect();
        await Assert.ThrowsAsync<GameRunningException>(() => Run(Own, progress));
        Assert.Empty(progress.Items);
    }

    // ------------------------------------------------------------------ the texts

    [Fact]
    public async Task The_healthy_text_in_Turkish_and_in_English_with_the_numbers_of_each_language()
    {
        _sys.PingResults[Gw] = Ok(Gw, avg: 12.5, jitter: 2.25, loss: 0);
        _sys.PingResults[One] = Ok(One, avg: 24.4, jitter: 3, loss: 0);
        _sys.PingResults[Two] = Ok(Two, avg: 19, jitter: 1, loss: 0);

        CultureInfo.CurrentCulture = new CultureInfo("en-US");                    // the machine's own number format must not decide
        var tr = await Run();
        Assert.Equal("Bağlantın sağlıklı görünüyor.", tr.Headline);
        Assert.Equal(
            "Modem: ortalama 12,5 ms, paket kaybı %0, dalgalanma 2,3 ms; internet: ortalama 24,4 ms, paket kaybı %0, dalgalanma 3 ms. " +
            "Kayıp, gecikme ve dalgalanma oyunda hissedilen sınırların içinde.", tr.Detail);

        Loc.Use("en");
        CultureInfo.CurrentCulture = new CultureInfo("tr-TR");
        var en = await Run();
        Assert.Equal("Your connection looks healthy.", en.Headline);
        Assert.Equal(
            "Modem: average 12.5 ms, packet loss 0%, fluctuation 2.3 ms; internet: average 24.4 ms, packet loss 0%, fluctuation 3 ms. " +
            "Loss, delay and fluctuation are within the limits that matter in a game.", en.Detail);
    }

    [Fact]
    public async Task The_home_network_text_in_both_languages()
    {
        _env.Wifi = true;
        Gateway(avg: 45.5, jitter: 12.25, loss: 4);
        Internet();
        var tr = await Run();
        Assert.Equal("Sorun büyük olasılıkla evdeki ağda (Wi-Fi ya da modem).", tr.Headline);
        Assert.Equal(
            "Modem: ortalama 45,5 ms, paket kaybı %4, dalgalanma 12,3 ms; internet: ortalama 20 ms, paket kaybı %0, dalgalanma 2 ms. " +
            "Bu değerler oyunda takılma ya da gecikme olarak hissedilebilir; sorun modeme giden yolda ya da modemin kendisinde görünüyor. " +
            "Mümkünse bilgisayarını modeme kabloyla (Ethernet) bağla.", tr.Detail);

        Loc.Use("en");
        var en = await Run();
        Assert.Equal("The problem is most likely in your home network (Wi-Fi or modem).", en.Headline);
        Assert.Equal(
            "Modem: average 45.5 ms, packet loss 4%, fluctuation 12.3 ms; internet: average 20 ms, packet loss 0%, fluctuation 2 ms. " +
            "These values can feel like stutter or lag in a game; the problem seems to be on the way to your modem, or in the modem itself. " +
            "If you can, connect your computer to the modem with a cable (Ethernet).", en.Detail);
    }

    [Fact]
    public async Task The_provider_and_unreachable_headlines_in_English()
    {
        Loc.Use("en");
        Internet(avg: 150);
        Assert.Equal("The problem is most likely with your internet provider.", (await Run()).Headline);
        _env.Dns = null;                                                            // the name lookup failed too: the flat statement
        InternetSilent();
        var unreachable = await Run();
        Assert.Equal("Couldn't reach the internet.", unreachable.Headline);
        Assert.Contains("Your modem answers, but the internet doesn't seem to be reachable.", unreachable.Detail);
        _env.Gateway = null;
        Internet(avg: 150);
        var unknown = await Run();
        Assert.Equal("Your internet has a problem, but it's not clear where.", unknown.Headline);
    }

    [Fact]
    public async Task A_loss_with_a_decimal_follows_the_language()
    {
        Gateway(loss: 3.5);
        Assert.Contains("paket kaybı %3,5", (await Run()).Detail);
        Loc.Use("en");
        Assert.Contains("packet loss 3.5%", (await Run()).Detail);
    }

    [Fact]
    public void The_limits_note_is_said_once_in_each_language_and_is_not_part_of_the_result()
    {
        Assert.Equal("Bu ölçüm birkaç saniye sürer ve ping kullanır; oyun sunucusuna giden yolu birebir göstermez. Ad çözme süresi Windows'un önbelleğine göre değişebilir.", NetCheck.Limits);
        Assert.DoesNotContain("ICMP", NetCheck.Limits);
        Loc.Use("en");
        Assert.Equal("This check takes a few seconds and uses ping; it doesn't show the exact path to the game server. The name lookup time can vary with Windows' cache.", NetCheck.Limits);
        Assert.DoesNotContain("ICMP", NetCheck.Limits);
    }

    [Fact]
    public async Task One_term_for_the_name_lookup_and_the_words_packet_loss_in_the_numbers_sentences()
    {
        // the old words are gone from every sentence of every situation, in both languages
        foreach (var language in new[] { "tr", "en" })
        {
            Loc.Use(language);
            var texts = new List<string> { NetCheck.Limits };
            _env.Dns = 1500;
            foreach (var setup in new Action[]
            {
                () => { },
                () => Gateway(avg: 45, loss: 6),
                () => { Gateway(); Internet(avg: 150); },
                () => { _env.Gateway = null; Internet(avg: 150); },
            })
            {
                _sys.PingResults.Clear();
                _env.Gateway = Gw;
                setup();
                var r = await Run(Own);
                texts.Add(r.Headline);
                texts.Add(r.Detail);
            }
            var all = string.Join(" ", texts);
            Assert.DoesNotContain("Site adreslerinin", all);
            Assert.DoesNotContain("DNS", all);
            Assert.DoesNotContain("ICMP", all);
            Assert.DoesNotMatch(@"(?<!paket )kayıp %", all);                    // "paket kaybı %", never a bare "kayıp %"
            Assert.DoesNotMatch(@"(?<!packet )loss \d", all);                  // "packet loss 4%", never a bare "loss 4%"
        }
    }

    // ------------------------------------------------------------------ every situation: plain, short, complete in both languages

    private const string Far = "play.example.net";
    private const string Home = "192.168.1.77";

    /// <summary>A detail is one to three short sentences; the slow-lookup remark is itself two sentences, so that one situation may have four.</summary>
    private static int MaxSentences(string scenario) => scenario == "healthy_dns_slow" ? 4 : 3;

    private static readonly Dictionary<string, (Action<FakeSystem, NetCheckFakeEnv> Setup, string? Extra, NetVerdict Verdict)> Scenarios = new()
    {
        ["healthy"] = ((s, e) => { }, null, NetVerdict.Healthy),
        ["healthy_wifi"] = ((s, e) => e.Wifi = true, null, NetVerdict.Healthy),
        ["local_wired"] = ((s, e) => s.PingResults[Gw] = Ok(Gw, 45, 12, 4), null, NetVerdict.LocalNetwork),
        ["local_wifi"] = ((s, e) => { e.Wifi = true; s.PingResults[Gw] = Ok(Gw, 45, 3, 0); }, null, NetVerdict.LocalNetwork),
        ["provider"] = ((s, e) => { s.PingResults[One] = Ok(One, 120, 5, 0); s.PingResults[Two] = Ok(Two, 110, 5, 0); }, null, NetVerdict.Provider),
        ["provider_loss"] = ((s, e) => s.PingResults[One] = Ok(One, 30, 3, 5), null, NetVerdict.Provider),
        // silent pings and a failed name lookup: the line is really down
        ["unreachable_router_ok"] = ((s, e) => { e.Dns = null; s.PingResults[One] = Silent(One); s.PingResults[Two] = Silent(Two); }, null, NetVerdict.Unreachable),
        ["unreachable_nothing"] = ((s, e) => { e.Dns = null; s.PingResults[Gw] = Silent(Gw); s.PingResults[One] = Silent(One); s.PingResults[Two] = Silent(Two); }, null, NetVerdict.Unreachable),
        ["unreachable_no_router"] = ((s, e) => { e.Dns = null; e.Gateway = null; s.PingResults[One] = Silent(One); s.PingResults[Two] = Silent(Two); }, null, NetVerdict.Unreachable),
        // silent pings but the name lookup works: pings may be blocked
        ["ping_blocked_router_ok"] = ((s, e) => { s.PingResults[One] = Silent(One); s.PingResults[Two] = Silent(Two); }, null, NetVerdict.Unknown),
        ["ping_blocked_router_bad"] = ((s, e) => { s.PingResults[Gw] = Ok(Gw, 55, 3, 10); s.PingResults[One] = Silent(One); s.PingResults[Two] = Silent(Two); }, null, NetVerdict.Unknown),
        ["ping_blocked_nothing"] = ((s, e) => { s.PingResults[Gw] = Silent(Gw); s.PingResults[One] = Silent(One); s.PingResults[Two] = Silent(Two); }, null, NetVerdict.Unknown),
        ["ping_blocked_no_router"] = ((s, e) => { e.Gateway = null; s.PingResults[One] = Silent(One); s.PingResults[Two] = Silent(Two); }, null, NetVerdict.Unknown),
        // the own target is inside the home network: it answers but proves nothing about the internet
        ["ping_blocked_own_private"] = ((s, e) => { s.PingResults[One] = Silent(One); s.PingResults[Two] = Silent(Two); s.PingResults[Home] = Ok(Home, 3); }, Home, NetVerdict.Unknown),
        ["unreachable_own_private"] = ((s, e) => { e.Dns = null; s.PingResults[One] = Silent(One); s.PingResults[Two] = Silent(Two); s.PingResults[Home] = Ok(Home, 3); }, Home, NetVerdict.Unreachable),
        ["healthy_own_private_bad"] = ((s, e) => s.PingResults[Home] = Ok(Home, 150, 3, 0), Home, NetVerdict.Healthy),
        ["healthy_no_router"] = ((s, e) => e.Gateway = null, null, NetVerdict.Healthy),
        ["healthy_router_silent"] = ((s, e) => s.PingResults[Gw] = Silent(Gw), null, NetVerdict.Healthy),
        ["unknown_no_router"] = ((s, e) => { e.Gateway = null; s.PingResults[One] = Ok(One, 150); }, null, NetVerdict.Unknown),
        ["unknown_router_silent"] = ((s, e) => { s.PingResults[Gw] = Silent(Gw); s.PingResults[One] = Ok(One, 150); }, null, NetVerdict.Unknown),
        ["healthy_own_bad"] = ((s, e) => s.PingResults[Far] = Ok(Far, 150, 3, 1.5), Far, NetVerdict.Healthy),
        ["healthy_own_silent"] = ((s, e) => s.PingResults[Far] = Silent(Far), Far, NetVerdict.Healthy),
        ["healthy_dns_slow"] = ((s, e) => e.Dns = 1500, null, NetVerdict.Healthy),
        ["healthy_own_bad_and_dns_slow"] = ((s, e) => { e.Dns = 1500; s.PingResults[Far] = Ok(Far, 150); }, Far, NetVerdict.Healthy),
        ["provider_own_evidence"] = ((s, e) => { s.PingResults[One] = Silent(One); s.PingResults[Two] = Silent(Two); s.PingResults[Far] = Ok(Far, 150); }, Far, NetVerdict.Provider),
        // fine pings, but the name lookup did not answer: not "healthy", a hedge (the numbers, then one sentence)
        ["dns_failed_pings_fine"] = ((s, e) => e.Dns = null, null, NetVerdict.Unknown),
        ["dns_failed_pings_fine_router_silent"] = ((s, e) => { e.Dns = null; s.PingResults[Gw] = Silent(Gw); }, null, NetVerdict.Unknown),
        ["dns_failed_pings_fine_no_router"] = ((s, e) => { e.Dns = null; e.Gateway = null; }, null, NetVerdict.Unknown),
        ["dns_failed_pings_fine_own_silent"] = ((s, e) => { e.Dns = null; s.PingResults[Far] = Silent(Far); }, Far, NetVerdict.Unknown),
        // one lost packet is never a finding, however few pings were sent
        ["healthy_one_lost_packet"] = ((s, e) => { s.PingResults[Gw] = Lost(Gw, 30, 29); s.PingResults[One] = Lost(One, 30, 29); }, null, NetVerdict.Healthy),
    };

    public static IEnumerable<object[]> ScenarioNames => Scenarios.Keys.Select(k => new object[] { k });

    private async Task<(NetReport Report, List<string> Progress)> RunScenario(string name)
    {
        var (setup, extra, _) = Scenarios[name];
        setup(_sys, _env);
        var progress = new Collect();
        var report = await Run(extra, progress);
        return (report, progress.Items);
    }

    [Theory]
    [MemberData(nameof(ScenarioNames))]
    public async Task Every_situation_gets_its_verdict_and_a_headline_of_one_sentence_and_a_detail_of_one_to_three(string name)
    {
        var (report, _) = await RunScenario(name);
        Assert.Equal(Scenarios[name].Verdict, report.Verdict);
        Assert.Equal(1, Sentences(report.Headline));
        Assert.EndsWith(".", report.Headline);
        Assert.InRange(Sentences(report.Detail), 1, MaxSentences(name));
        Assert.EndsWith(".", report.Detail);
        Assert.DoesNotContain("ICMP", report.Detail);                              // the limits note is the UI's, once under the result
    }

    [Theory]
    [MemberData(nameof(ScenarioNames))]
    public async Task Every_situation_reads_the_same_way_in_English_without_a_Turkish_word_left(string name)
    {
        Loc.Use("en");
        Loc.ClearMissing();
        var (report, progress) = await RunScenario(name);
        Assert.Equal(Scenarios[name].Verdict, report.Verdict);
        Assert.Equal(1, Sentences(report.Headline));
        Assert.InRange(Sentences(report.Detail), 1, MaxSentences(name));
        var all = new List<string> { report.Headline, report.Detail, NetCheck.Limits };
        all.AddRange(report.Targets.Select(t => t.Label));
        all.AddRange(progress);
        foreach (var text in all)
        {
            Assert.DoesNotMatch(TurkishLetter, text);
            Assert.DoesNotContain("{", text);
            Assert.DoesNotContain("}", text);
        }
        Assert.Empty(Loc.Missing);                                                  // an English entry exists for every text that was shown
    }

    [Theory]
    [MemberData(nameof(ScenarioNames))]
    public async Task No_text_scares_or_scores_and_no_number_is_made_up(string name)
    {
        var (report, _) = await RunScenario(name);
        var text = (report.Headline + " " + report.Detail).ToLowerInvariant();
        foreach (var banned in new[] { "tehlike", "kritik", "felaket", "bozuk", "puan", "skor", "!", "kesinlikle", "garanti" })
            Assert.DoesNotContain(banned, text);
        // every "N ms" in the detail belongs to a target that answered (or to the name lookup)
        var answered = report.Targets.Where(t => t.Stats != null).SelectMany(t => new[] { t.Stats!.AvgMs, t.Stats.JitterMs }).Concat(new[] { report.DnsMs ?? -1 }).ToList();
        foreach (Match m in Regex.Matches(report.Detail, @"(\d+(?:,\d+)?) ms"))
        {
            var value = double.Parse(m.Groups[1].Value, CultureInfo.GetCultureInfo("tr-TR"));
            Assert.Contains(answered, a => Math.Abs(a - value) <= 0.5);                       // 12,5 stays 12,5; 120,4 is written as 120
        }
    }
}

/// <summary>Which adapter is "the router's": the choice is made from a plain list, so it is tested without a network.</summary>
public class NetEnvironmentTests
{
    private static GatewayCandidate Nic(string name, NetworkInterfaceType type, int index, string? gateway, OperationalStatus status = OperationalStatus.Up, string? description = null) =>
        new(name, description ?? name + " adapter", type, status,
            gateway == null ? Array.Empty<IPAddress>() : new[] { IPAddress.Parse(gateway) }, index);

    private static GatewayCandidate Cable(int index = 7, string gateway = "192.168.1.1") => Nic("Ethernet", NetworkInterfaceType.Ethernet, index, gateway);
    private static GatewayCandidate Wifi(int index = 18, string gateway = "192.168.1.1") => Nic("Wi-Fi", NetworkInterfaceType.Wireless80211, index, gateway);

    [Fact]
    public void A_cable_with_an_IPv6_and_an_IPv4_gateway_gives_the_IPv4_one()
    {
        // what this very PC reports: fe80::1 first, then 192.168.1.1
        var nic = new GatewayCandidate("Ethernet", "Realtek Gaming 2.5GbE Family Controller", NetworkInterfaceType.Ethernet, OperationalStatus.Up,
            new[] { IPAddress.Parse("fe80::1"), IPAddress.Parse("192.168.1.1") }, 7);
        var choice = WindowsNetEnvironment.PickGateway(new[] { nic }, 7);
        Assert.NotNull(choice);
        Assert.Equal("192.168.1.1", choice!.Gateway);
        Assert.False(choice.Wireless);
    }

    [Fact]
    public void Wi_Fi_alone_is_wireless()
    {
        var choice = WindowsNetEnvironment.PickGateway(new[] { Wifi() }, 18);
        Assert.Equal("192.168.1.1", choice!.Gateway);
        Assert.True(choice.Wireless);
    }

    [Fact]
    public void An_adapter_that_is_down_is_not_a_router()
    {
        var down = Nic("Wi-Fi", NetworkInterfaceType.Wireless80211, 18, "192.168.1.1", OperationalStatus.Down);
        Assert.Null(WindowsNetEnvironment.PickGateway(new[] { down }, 18));
        Assert.False(WindowsNetEnvironment.PickGateway(new[] { down, Cable() }, null)!.Wireless);
    }

    [Fact]
    public void The_adapter_Windows_uses_for_the_internet_wins()
    {
        var both = new[] { Cable(7, "192.168.1.1"), Wifi(18, "192.168.0.1") };
        var viaWifi = WindowsNetEnvironment.PickGateway(both, 18);
        Assert.Equal("192.168.0.1", viaWifi!.Gateway);
        Assert.True(viaWifi.Wireless);
        var viaCable = WindowsNetEnvironment.PickGateway(both, 7);
        Assert.Equal("192.168.1.1", viaCable!.Gateway);
        Assert.False(viaCable.Wireless);
    }

    [Fact]
    public void Without_a_best_adapter_a_cable_wins_over_Wi_Fi_whatever_the_order()
    {
        var choice = WindowsNetEnvironment.PickGateway(new[] { Wifi(18, "192.168.0.1"), Cable(7, "192.168.1.1") }, null);
        Assert.Equal("192.168.1.1", choice!.Gateway);
        Assert.False(choice.Wireless);
        var unknownIndex = WindowsNetEnvironment.PickGateway(new[] { Wifi(18, "192.168.0.1"), Cable(7, "192.168.1.1") }, 999);
        Assert.Equal("192.168.1.1", unknownIndex!.Gateway);
    }

    [Fact]
    public void Loopback_tunnel_and_dial_up_adapters_are_not_a_home_router()
    {
        Assert.Null(WindowsNetEnvironment.PickGateway(new[]
        {
            Nic("Loopback", NetworkInterfaceType.Loopback, 1, "127.0.0.1"),
            Nic("Teredo", NetworkInterfaceType.Tunnel, 12, "10.0.0.1"),
            Nic("PPPoE", NetworkInterfaceType.Ppp, 13, "10.1.1.1"),
        }, null));
    }

    [Theory]
    [InlineData("WireGuard Tunnel", "WireGuard Tunnel", NetworkInterfaceType.Unknown)]
    [InlineData("OpenVPN", "TAP-Windows Adapter V9", NetworkInterfaceType.Ethernet)]
    [InlineData("Ethernet 3", "Wintun Userspace Tunnel", NetworkInterfaceType.Ethernet)]
    [InlineData("Work", "Contoso VPN Client Adapter", NetworkInterfaceType.Ethernet)]
    [InlineData("Tailscale", "Tailscale Tunnel", NetworkInterfaceType.Ethernet)]
    public void A_VPN_adapter_is_not_a_home_router_so_a_VPN_only_computer_has_none(string name, string description, NetworkInterfaceType type)
    {
        var vpn = Nic(name, type, 20, "10.8.0.1", OperationalStatus.Up, description);
        Assert.Null(WindowsNetEnvironment.PickGateway(new[] { vpn }, 20));
    }

    [Fact]
    public void A_VPN_that_carries_the_internet_does_not_hide_the_real_router()
    {
        var vpn = Nic("WireGuard", NetworkInterfaceType.Unknown, 20, "10.8.0.1", OperationalStatus.Up, "WireGuard Tunnel");
        var choice = WindowsNetEnvironment.PickGateway(new[] { vpn, Cable() }, 20);          // the VPN is "best" but is not a candidate
        Assert.Equal("192.168.1.1", choice!.Gateway);
    }

    [Fact]
    public void A_missing_unspecified_or_IPv6_only_gateway_is_no_gateway()
    {
        Assert.Null(WindowsNetEnvironment.PickGateway(new[] { Nic("Ethernet", NetworkInterfaceType.Ethernet, 7, null) }, 7));
        Assert.Null(WindowsNetEnvironment.PickGateway(new[] { Nic("Ethernet", NetworkInterfaceType.Ethernet, 7, "0.0.0.0") }, 7));
        Assert.Null(WindowsNetEnvironment.PickGateway(new[] { Nic("Ethernet", NetworkInterfaceType.Ethernet, 7, "fe80::1") }, 7));
        Assert.Null(WindowsNetEnvironment.PickGateway(Array.Empty<GatewayCandidate>(), null));
    }
}

/// <summary>The real network list of this computer, read only: no packet is sent and nothing is changed. (The real pings are not part of any automated test.)</summary>
[Collection("Sequential")]
public class LiveNetEnvironmentTests
{
    private readonly ITestOutputHelper _out;
    public LiveNetEnvironmentTests(ITestOutputHelper output) => _out = output;

    [Fact]
    public void The_router_and_the_link_kind_are_read_without_throwing_and_a_router_is_an_IPv4_address()
    {
        var env = new WindowsNetEnvironment();
        var gateway = env.FindGateway();
        var wireless = env.IsWireless();
        _out.WriteLine(gateway == null ? "no router found" : $"router found, wireless={wireless}");
        if (gateway == null) return;                                   // a PC without a network (or a VPN-only one) is allowed to say so
        Assert.True(IPAddress.TryParse(gateway, out var address), gateway);
        Assert.Equal(AddressFamily.InterNetwork, address!.AddressFamily);
        Assert.False(IPAddress.Any.Equals(address));
        Assert.False(IPAddress.IsLoopback(address));
    }
}
