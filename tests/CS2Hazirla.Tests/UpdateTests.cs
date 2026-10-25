using System.Diagnostics;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using CS2Hazirla.Core;
using Xunit;

namespace CS2Hazirla.Tests;

/// <summary>
/// The update check and download: nothing is trusted that the owner's signature does not cover, and a failure never reaches the user as anything but a plain sentence.
/// No test touches the network: the web is a fake handler that answers from a table.
/// </summary>
public class UpdateTests : IDisposable
{
    private const string Base = "https://github.com/fara1ka/antilag/releases";
    private readonly (string PrivateKey, string PublicKey) _keys = UpdateSigning.NewKeyPair();
    // two levels under a folder of its own, like the real %LOCALAPPDATA%\Antilag\update: the folder above the download root is checked as well
    private readonly string _outer = Path.Combine(Path.GetTempPath(), "antilag-update-test-" + Guid.NewGuid().ToString("N"));
    private readonly string _dir;

    public UpdateTests() { _dir = Path.Combine(_outer, "update"); }

    public void Dispose() { try { Directory.Delete(_outer, true); } catch { } }

    // ------------------------------------------------------------------ helpers

    private sealed class FakeWeb : HttpMessageHandler
    {
        public readonly Dictionary<string, Func<HttpResponseMessage>> Routes = new();
        public readonly List<HttpRequestMessage> Requests = new();
        /// <summary>Lets a test hold an answer back (a slow check, for instance).</summary>
        public Func<HttpRequestMessage, Task>? Gate;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Requests.Add(request);
            if (Requests.Count > 50) throw new InvalidOperationException("runaway: more than 50 requests in one test");   // a loop that lost its limit fails instead of hanging
            if (Gate != null) await Gate(request);
            var url = request.RequestUri!.ToString();
            return Routes.TryGetValue(url, out var make) ? make() : new HttpResponseMessage(HttpStatusCode.NotFound);
        }
    }

    private static HttpResponseMessage Ok(byte[] body) => new(HttpStatusCode.OK) { Content = new ByteArrayContent(body) };
    private static HttpResponseMessage Redirect(string to, HttpStatusCode code = HttpStatusCode.Found)
    {
        var r = new HttpResponseMessage(code);
        r.Headers.Location = new Uri(to);
        return r;
    }

    private static byte[] Installer(int size = 1_500_000, byte fill = 7) => Enumerable.Repeat(fill, size).ToArray();

    private UpdateManifest Signed(string version, byte[] file, string? key = null)
    {
        var m = new UpdateManifest { Version = version, Size = file.Length, Sha256 = Convert.ToHexString(SHA256.HashData(file)).ToLowerInvariant() };
        m.Signature = UpdateSigning.Sign(m, key ?? _keys.PrivateKey);
        return m;
    }

    private UpdateClient Client(FakeWeb web, string current = "3.3.0.0") =>
        new(Version.Parse(current), web, Base, new[] { _keys.PublicKey }, _dir);

    private static FakeWeb WebWith(UpdateManifest m, byte[]? file = null)
    {
        var web = new FakeWeb();
        web.Routes[$"{Base}/latest/download/latest.json"] = () => Ok(Encoding.UTF8.GetBytes(m.ToJson()));
        if (file != null) web.Routes[$"{Base}/download/v{m.Version}/Antilag-Setup.exe"] = () => Ok(file);
        return web;
    }

    /// <summary>Everything under the download root, however deep.</summary>
    private string[] Left() => Directory.Exists(_dir) ? Directory.GetFiles(_dir, "*", SearchOption.AllDirectories) : Array.Empty<string>();

    // ------------------------------------------------------------------ the signature

    [Fact]
    public void A_signed_manifest_verifies_and_nothing_else_does()
    {
        var file = Installer();
        var m = Signed("3.4.0", file);
        Assert.True(UpdateSigning.Verify(m, _keys.PublicKey));

        foreach (var tampered in new[]
        {
            new UpdateManifest { Version = "3.4.1", Size = m.Size, Sha256 = m.Sha256, Signature = m.Signature },     // another version, same signature
            new UpdateManifest { Version = m.Version, Size = m.Size + 1, Sha256 = m.Sha256, Signature = m.Signature }, // another size
            new UpdateManifest { Version = m.Version, Size = m.Size, Sha256 = new string('a', 64), Signature = m.Signature }, // another file
        })
            Assert.False(UpdateSigning.Verify(tampered, _keys.PublicKey));

        var stranger = UpdateSigning.NewKeyPair();
        Assert.False(UpdateSigning.Verify(m, stranger.PublicKey), "a signature made with another key must not verify");
        Assert.False(UpdateSigning.Verify(Signed("3.4.0", file, stranger.PrivateKey), _keys.PublicKey));
    }

    [Fact]
    public void Either_of_two_shipped_keys_may_sign_and_a_third_may_not()
    {
        var everyday = UpdateSigning.NewKeyPair();
        var spare = UpdateSigning.NewKeyPair();
        var thief = UpdateSigning.NewKeyPair();
        var keys = new[] { everyday.PublicKey, spare.PublicKey };
        var file = Installer();

        Assert.True(UpdateSigning.Verify(Signed("3.4.0", file, everyday.PrivateKey), keys));
        Assert.True(UpdateSigning.Verify(Signed("3.4.0", file, spare.PrivateKey), keys));      // the key that is kept offline works after a loss of the first
        Assert.False(UpdateSigning.Verify(Signed("3.4.0", file, thief.PrivateKey), keys));
        Assert.False(UpdateSigning.Verify(Signed("3.4.0", file, everyday.PrivateKey), Array.Empty<string>()));
        Assert.Equal(spare.PublicKey, UpdateSigning.PublicKeyOf(spare.PrivateKey));
    }

    [Theory]
    [InlineData("")]
    [InlineData("not base64 !!")]
    [InlineData("AAAA")]
    public void Garbage_is_not_verified_and_never_throws(string signature)
    {
        var m = Signed("3.4.0", Installer());
        m.Signature = signature;
        Assert.False(UpdateSigning.Verify(m, _keys.PublicKey));
        Assert.False(UpdateSigning.Verify(Signed("3.4.0", Installer()), ""));          // no public key known: nothing verifies
        Assert.False(UpdateSigning.Verify(Signed("3.4.0", Installer()), "###"));
    }

    [Fact]
    public void The_build_that_ships_carries_the_owners_public_key_and_it_is_a_real_key()
    {
        // an empty or broken key would make every update fail silently
        Assert.NotEmpty(UpdateSigning.PublicKeys);
        foreach (var k in UpdateSigning.PublicKeys)
        {
            using var ecdsa = ECDsa.Create();
            ecdsa.ImportSubjectPublicKeyInfo(Convert.FromBase64String(k), out _);
            Assert.Equal(256, ecdsa.KeySize);
        }
        Assert.Equal(UpdateSigning.PublicKeys.Count, UpdateSigning.PublicKeys.Distinct().Count());
    }

    [Fact]
    public void The_first_updater_release_ships_the_everyday_key_and_the_spare_key()
    {
        // a key that is not in an installed copy can never be added to it by an update signed with another key: both must be there from the start
        Assert.Equal(2, UpdateSigning.PublicKeys.Count);
    }

    [Theory]
    [InlineData("3.4", 2_000_000, "")]
    [InlineData("3.4.0-beta", 2_000_000, "")]
    [InlineData("../1.0.0", 2_000_000, "")]
    [InlineData("03.4.0", 2_000_000, "")]
    [InlineData("3.04.0", 2_000_000, "")]
    [InlineData("3.4.0", 10, "")]
    [InlineData("3.4.0", 900_000_000, "")]
    [InlineData("3.4.0", 2_000_000, "ABC")]
    public void A_manifest_with_anything_odd_in_it_is_refused_before_the_signature_is_looked_at(string version, long size, string sha)
    {
        var m = new UpdateManifest { Version = version, Size = size, Sha256 = sha == "" ? new string('a', 64) : sha };
        m.Signature = UpdateSigning.Sign(m, _keys.PrivateKey);                 // a perfectly valid signature over odd values: only the validator can refuse it
        Assert.NotNull(m.Problem());
        var ex = Assert.Throws<UpdateException>(() => new UpdateClient(new Version(3, 3, 0), new FakeWeb(), Base, new[] { _keys.PublicKey }, _dir).Evaluate(m));
        Assert.True(ex.Integrity);
        Assert.Equal(m.Problem(), ex.Message);
    }

    [Fact]
    public void A_sha256_in_capitals_is_refused_so_there_is_one_way_to_write_it()
    {
        var m = Signed("3.4.0", Installer());
        m.Sha256 = m.Sha256.ToUpperInvariant();
        Assert.NotNull(m.Problem());
    }

    [Fact]
    public void The_signed_text_does_not_depend_on_the_language_of_the_computer()
    {
        var old = System.Globalization.CultureInfo.CurrentCulture;
        try
        {
            var m = new UpdateManifest { Version = "3.4.0", Size = 61_234_567, Sha256 = new string('a', 64) };
            System.Globalization.CultureInfo.CurrentCulture = new System.Globalization.CultureInfo("ar-SA");   // other digits and separators
            Assert.Equal("antilag-update|3.4.0|61234567|" + new string('a', 64), m.Canonical());
        }
        finally { System.Globalization.CultureInfo.CurrentCulture = old; }
    }

    // ------------------------------------------------------------------ the check

    [Fact]
    public async Task A_newer_signed_version_is_offered()
    {
        var file = Installer();
        var web = WebWith(Signed("3.4.0", file));
        var offer = await Client(web).CheckAsync();
        Assert.NotNull(offer);
        Assert.Equal(new Version(3, 4, 0), offer!.Version);
        Assert.Equal("3.4.0", offer.VersionText);
        Assert.Equal(file.Length, offer.Size);
    }

    [Theory]
    [InlineData("3.3.0")]      // the same version
    [InlineData("3.2.0")]      // an older release, correctly signed: no downgrade
    [InlineData("2.9.9")]
    public async Task The_same_or_an_older_version_is_not_offered(string published)
    {
        var web = WebWith(Signed(published, Installer()));
        Assert.Null(await Client(web, current: "3.3.0.0").CheckAsync());
    }

    [Fact]
    public async Task A_four_part_running_version_equals_the_three_part_one()
    {
        Assert.Null(await Client(WebWith(Signed("3.3.0", Installer())), current: "3.3.0.0").CheckAsync());
        Assert.NotNull(await Client(WebWith(Signed("3.3.1", Installer())), current: "3.3.0.0").CheckAsync());
    }

    [Fact]
    public async Task A_manifest_that_does_not_verify_is_an_integrity_failure()
    {
        var stranger = UpdateSigning.NewKeyPair();
        var web = WebWith(Signed("9.0.0", Installer(), stranger.PrivateKey));
        var ex = await Assert.ThrowsAsync<UpdateException>(() => Client(web).CheckAsync());
        Assert.True(ex.Integrity);
    }

    [Fact]
    public async Task The_check_is_one_plain_request_that_carries_nothing_about_the_user()
    {
        var web = WebWith(Signed("3.4.0", Installer()));
        await Client(web).CheckAsync();
        var req = Assert.Single(web.Requests);
        Assert.Equal($"{Base}/latest/download/latest.json", req.RequestUri!.ToString());
        Assert.Equal(HttpMethod.Get, req.Method);
        Assert.Null(req.Headers.Authorization);
        Assert.False(req.Headers.Contains("Cookie"));
        Assert.Equal("Antilag/3.3.0", req.Headers.UserAgent.ToString());
        Assert.Null(req.Content);
    }

    [Fact]
    public async Task Redirects_to_githubs_own_file_hosts_are_followed_and_nothing_else()
    {
        var m = Signed("3.4.0", Installer());
        var json = Encoding.UTF8.GetBytes(m.ToJson());

        var good = new FakeWeb();
        good.Routes[$"{Base}/latest/download/latest.json"] = () => Redirect($"{Base}/download/v3.4.0/latest.json");
        good.Routes[$"{Base}/download/v3.4.0/latest.json"] = () => Redirect("https://release-assets.githubusercontent.com/github-production-release-asset/1/2");
        good.Routes["https://release-assets.githubusercontent.com/github-production-release-asset/1/2"] = () => Ok(json);
        Assert.NotNull(await Client(good).CheckAsync());
        Assert.All(good.Requests, r => Assert.Null(r.Headers.Authorization));

        foreach (var evil in new[]
        {
            "https://evil.example/latest.json",                       // another host
            "http://github.com/fara1ka/antilag/releases/latest.json",  // not https
            "https://github.com.evil.example/x",                       // a look-alike
            "https://notgithub.com/x",
            "https://githubusercontent.com.evil.example/x",
            "https://user:pw@github.com/x",                            // credentials in the address
            "https://github.com:8443/x",                               // another port
            "https://evilgithubusercontent.com/x",                     // ends with the name but is not a sub-domain of it
            "https://140.82.112.3/x",                                  // an IP address instead of a name
            "https://[::1]/x",
        })
        {
            var web = new FakeWeb();
            web.Routes[$"{Base}/latest/download/latest.json"] = () => Redirect(evil);
            web.Routes[evil] = () => Ok(json);
            await Assert.ThrowsAsync<UpdateException>(() => Client(web).CheckAsync());
            Assert.Single(web.Requests);                               // the bad address was never even requested
        }
    }

    [Fact]
    public async Task A_redirect_loop_ends()
    {
        var web = new FakeWeb();
        web.Routes[$"{Base}/latest/download/latest.json"] = () => Redirect($"{Base}/latest/download/latest.json");
        var ex = await Assert.ThrowsAsync<UpdateException>(() => Client(web).CheckAsync());
        Assert.Contains("yönlendirme", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.True(web.Requests.Count <= 7);
    }

    [Fact]
    public async Task A_missing_release_a_huge_answer_and_a_captive_portal_page_are_all_just_failures()
    {
        // no release yet (404)
        await Assert.ThrowsAsync<UpdateException>(() => Client(new FakeWeb()).CheckAsync());

        // a body that is far too big for a manifest
        var big = new FakeWeb();
        big.Routes[$"{Base}/latest/download/latest.json"] = () => Ok(new byte[100_000]);
        var tooBig = await Assert.ThrowsAsync<UpdateException>(() => Client(big).CheckAsync());
        Assert.False(tooBig.Integrity);

        // an html page from a hotel network
        var portal = new FakeWeb();
        portal.Routes[$"{Base}/latest/download/latest.json"] = () => Ok(Encoding.UTF8.GetBytes("<html><body>Please log in</body></html>"));
        await Assert.ThrowsAsync<UpdateException>(() => Client(portal).CheckAsync());
    }

    // ------------------------------------------------------------------ the download

    [Fact]
    public async Task The_installer_is_downloaded_from_the_signed_version_into_a_fresh_folder_and_checked()
    {
        var file = Installer();
        var web = WebWith(Signed("3.4.0", file), file);
        var client = Client(web);
        var offer = (await client.CheckAsync())!;

        var seen = new List<double>();
        var path = await client.DownloadAsync(offer, new Progress<double>(seen.Add));

        Assert.Equal("Antilag-Setup-3.4.0.exe", Path.GetFileName(path));
        Assert.Equal(_dir, Path.GetDirectoryName(Path.GetDirectoryName(path)));                      // inside the root, one random folder deep
        Assert.Matches("^[0-9a-f]{32}$", Path.GetFileName(Path.GetDirectoryName(path))!);
        Assert.Equal(file, await File.ReadAllBytesAsync(path));
        Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(path)!, "*.part"));
        Assert.True(UpdateClient.FileMatches(path, offer));
        Assert.Equal($"{Base}/download/v3.4.0/Antilag-Setup.exe", web.Requests.Last().RequestUri!.ToString());   // built from the signed version

        var again = await client.DownloadAsync(offer);
        Assert.NotEqual(Path.GetDirectoryName(path), Path.GetDirectoryName(again));                  // every attempt has its own, unpredictable folder
    }

    [Fact]
    public async Task The_download_address_is_never_taken_from_the_manifest()
    {
        var file = Installer();
        var m = Signed("3.4.0", file);
        var web = WebWith(m, file);
        // an extra "url" field in a (signed) manifest changes nothing: the only address ever used is built from the version
        web.Routes[$"{Base}/latest/download/latest.json"] = () => Ok(Encoding.UTF8.GetBytes(m.ToJson().Replace("{", "{ \"url\": \"https://evil.example/x.exe\",")));
        var client = Client(web);
        var offer = (await client.CheckAsync())!;
        await client.DownloadAsync(offer);
        Assert.DoesNotContain(web.Requests, r => r.RequestUri!.Host == "evil.example");
    }

    [Fact]
    public async Task A_file_that_is_not_the_signed_one_is_refused_and_leaves_nothing_behind()
    {
        var file = Installer();
        var offer = (await Client(WebWith(Signed("3.4.0", file))).CheckAsync())!;

        // the whole file arrived and it is another file: THIS is an integrity failure
        var swapped = WebWith(Signed("3.4.0", file), Installer(fill: 9));
        var ex = await Assert.ThrowsAsync<UpdateException>(() => Client(swapped).DownloadAsync(offer));
        Assert.True(ex.Integrity);
        Assert.Empty(Left());

        // too long or too short: something between us and GitHub answered (a filter, a proxy): the network did not work, the file is not "wrong"
        var longer = WebWith(Signed("3.4.0", file), Installer(file.Length + 1));
        ex = await Assert.ThrowsAsync<UpdateException>(() => Client(longer).DownloadAsync(offer));
        Assert.False(ex.Integrity);
        Assert.Empty(Left());

        var shorter = WebWith(Signed("3.4.0", file), Installer(file.Length - 1));
        ex = await Assert.ThrowsAsync<UpdateException>(() => Client(shorter).DownloadAsync(offer));
        Assert.False(ex.Integrity);
        Assert.Empty(Left());
    }

    [Fact]
    public async Task A_web_page_instead_of_the_installer_is_a_network_problem_not_a_wrong_file()
    {
        var file = Installer();
        var offer = (await Client(WebWith(Signed("3.4.0", file))).CheckAsync())!;
        var web = WebWith(Signed("3.4.0", file));
        web.Routes[$"{Base}/download/v3.4.0/Antilag-Setup.exe"] = () =>
        {
            var r = Ok(new byte[file.Length]);
            r.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("text/html");
            return r;
        };
        var ex = await Assert.ThrowsAsync<UpdateException>(() => Client(web).DownloadAsync(offer));
        Assert.False(ex.Integrity);
        Assert.Empty(Left());
    }

    [Fact]
    public async Task A_download_that_stops_halfway_is_not_kept()
    {
        var file = Installer();
        var offer = (await Client(WebWith(Signed("3.4.0", file))).CheckAsync())!;
        var web = WebWith(Signed("3.4.0", file));
        web.Routes[$"{Base}/download/v3.4.0/Antilag-Setup.exe"] = () => new HttpResponseMessage(HttpStatusCode.OK) { Content = new FailingContent(file.Length / 2) };
        await Assert.ThrowsAnyAsync<Exception>(() => Client(web).DownloadAsync(offer));
        Assert.Empty(Left());
    }

    private sealed class FailingContent : HttpContent
    {
        private readonly int _bytes;
        public FailingContent(int bytes) => _bytes = bytes;
        protected override async Task SerializeToStreamAsync(Stream stream, TransportContext? context)
        {
            await stream.WriteAsync(new byte[_bytes]);
            throw new IOException("connection lost");
        }
        protected override bool TryComputeLength(out long length) { length = -1; return false; }
    }

    [Fact]
    public async Task A_bad_status_for_the_installer_is_a_failure_and_a_cancel_stops_it()
    {
        var file = Installer();
        var offer = (await Client(WebWith(Signed("3.4.0", file))).CheckAsync())!;
        await Assert.ThrowsAsync<UpdateException>(() => Client(WebWith(Signed("3.4.0", file))).DownloadAsync(offer));   // 404: no installer in that release

        var web = WebWith(Signed("3.4.0", file), file);
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Client(web).DownloadAsync(offer, null, cts.Token));
        Assert.Empty(Left());
    }

    [Fact]
    public async Task A_file_changed_on_disk_after_the_download_no_longer_matches()
    {
        var file = Installer();
        var client = Client(WebWith(Signed("3.4.0", file), file));
        var offer = (await client.CheckAsync())!;
        var path = await client.DownloadAsync(offer);
        Assert.True(UpdateClient.FileMatches(path, offer));

        var bytes = await File.ReadAllBytesAsync(path);
        bytes[100] ^= 1;
        await File.WriteAllBytesAsync(path, bytes);
        Assert.False(UpdateClient.FileMatches(path, offer));
        File.Delete(path);
        Assert.False(UpdateClient.FileMatches(path, offer));
    }

    // ------------------------------------------------------------------ the folders (this app runs as administrator, the folders belong to the user)

    private static void MakeJunction(string link, string target)
    {
        var p = Process.Start(new ProcessStartInfo("cmd.exe", $"/c mklink /J \"{link}\" \"{target}\"") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true })!;
        p.WaitForExit();
        Assert.True(Directory.Exists(link), "the junction could not be created for the test");
    }

    [Fact]
    public async Task A_planted_link_in_place_of_the_download_folder_is_refused_and_nothing_is_written_or_deleted_behind_it()
    {
        var elsewhere = Path.Combine(Path.GetTempPath(), "antilag-elsewhere-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(elsewhere);
        // the file sits in a folder that looks exactly like one of ours: only the "root is a link" guard keeps CleanDownloads from deleting it
        var sentinelDir = Path.Combine(elsewhere, "0123456789abcdef0123456789abcdef");
        Directory.CreateDirectory(sentinelDir);
        var sentinel = Path.Combine(sentinelDir, "Antilag-Setup-1.0.0.exe");
        await File.WriteAllTextAsync(sentinel, "must survive");
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_dir)!);
            MakeJunction(_dir, elsewhere);

            var file = Installer();
            var web = WebWith(Signed("3.4.0", file), file);
            var client = Client(web);
            var offer = (await client.CheckAsync())!;
            var ex = await Assert.ThrowsAsync<UpdateException>(() => client.DownloadAsync(offer));
            Assert.False(ex.Integrity);
            Assert.DoesNotContain(web.Requests, r => r.RequestUri!.AbsolutePath.EndsWith("Antilag-Setup.exe"));   // refused before anything was downloaded

            client.CleanDownloads();
            Assert.Equal("must survive", await File.ReadAllTextAsync(sentinel));
            Assert.Single(Directory.GetFileSystemEntries(elsewhere));
        }
        finally
        {
            try { Directory.Delete(_dir); } catch { }            // removes only the link
            try { Directory.Delete(elsewhere, true); } catch { }
        }
    }

    [Fact]
    public async Task A_planted_link_inside_the_download_folder_is_not_walked_into_when_cleaning()
    {
        var elsewhere = Path.Combine(Path.GetTempPath(), "antilag-elsewhere-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(elsewhere);
        var sentinel = Path.Combine(elsewhere, "Antilag-Setup-1.0.0.exe");
        await File.WriteAllTextAsync(sentinel, "must survive");
        try
        {
            Directory.CreateDirectory(_dir);
            MakeJunction(Path.Combine(_dir, "0123456789abcdef0123456789abcdef"), elsewhere);       // looks exactly like one of our folders
            Client(new FakeWeb()).CleanDownloads();
            Assert.Equal("must survive", await File.ReadAllTextAsync(sentinel));
        }
        finally
        {
            try { Directory.Delete(Path.Combine(_dir, "0123456789abcdef0123456789abcdef")); } catch { }
            try { Directory.Delete(elsewhere, true); } catch { }
        }
    }

    [Fact]
    public async Task Cleaning_removes_only_what_this_class_creates()
    {
        var file = Installer();
        var client = Client(WebWith(Signed("3.4.0", file), file));
        var path = await client.DownloadAsync((await client.CheckAsync())!);
        UpdateClient.WriteUpdateNote(path);
        var ours = Path.GetDirectoryName(path)!;
        await File.WriteAllTextAsync(Path.Combine(ours, "notes.txt"), "not ours");                    // an unknown file inside our folder
        var foreign = Path.Combine(_dir, "my-own-folder");                                           // an unknown folder inside the root
        Directory.CreateDirectory(foreign);
        await File.WriteAllTextAsync(Path.Combine(foreign, "keep.txt"), "not ours");
        await File.WriteAllTextAsync(Path.Combine(foreign, "Antilag-Setup-1.0.0.exe"), "not ours either");   // our name, but not in a folder of ours

        client.CleanDownloads();

        Assert.False(File.Exists(path));
        Assert.False(File.Exists(path + ".update"));
        Assert.True(File.Exists(Path.Combine(ours, "notes.txt")));
        Assert.True(File.Exists(Path.Combine(foreign, "keep.txt")));
        Assert.True(File.Exists(Path.Combine(foreign, "Antilag-Setup-1.0.0.exe")));
    }

    [Fact]
    public async Task Leftovers_are_cleaned_and_the_folder_goes_with_them()
    {
        var file = Installer();
        var client = Client(WebWith(Signed("3.4.0", file), file));
        var path = await client.DownloadAsync((await client.CheckAsync())!);
        UpdateClient.WriteUpdateNote(path);
        Assert.True(File.Exists(path + ".update"));
        client.CleanDownloads();
        Assert.False(Directory.Exists(_dir));
    }

    // ------------------------------------------------------------------ the flow

    private (UpdateFlow Flow, FakeSystem Sys, NullLog Log, FakeWeb Web) Flow(UpdateManifest m, byte[]? file, Func<bool>? game = null, bool installerStarts = true)
    {
        var web = WebWith(m, file);
        var sys = new FakeSystem();
        // the real installer deletes the note next to itself as the first thing it does: that is how the app knows that it started
        if (installerStarts)
            sys.OnMutation = what => { if (what.StartsWith("restart ")) { var p = what["restart ".Length..]; Assert.True(File.Exists(p + ".update"), "the note must be there when the installer starts"); File.Delete(p + ".update"); } };
        var log = new NullLog();
        return (new UpdateFlow(Client(web), sys, log, game ?? (() => false)) { StartTimeout = TimeSpan.FromMilliseconds(400) }, sys, log, web);
    }

    [Fact]
    public async Task A_found_update_is_offered_and_Sonra_hides_it_until_the_next_start()
    {
        var f = Flow(Signed("3.4.0", Installer()), null);
        var changes = 0;
        f.Flow.Changed += () => changes++;
        await f.Flow.CheckAsync();
        Assert.Equal(UpdateState.Available, f.Flow.State);
        Assert.Equal("3.4.0", f.Flow.Offer!.VersionText);
        Assert.True(f.Flow.LastCheckWorked);
        Assert.True(changes > 0);

        f.Flow.Dismiss();
        Assert.Equal(UpdateState.None, f.Flow.State);
        Assert.NotNull(f.Flow.Offer);
        await f.Flow.CheckAsync();                                       // asking again (Ayarlar) shows it again
        Assert.Equal(UpdateState.Available, f.Flow.State);
    }

    [Fact]
    public async Task Being_up_to_date_shows_nothing()
    {
        var f = Flow(Signed("3.3.0", Installer()), null);
        await f.Flow.CheckAsync();
        Assert.Equal(UpdateState.None, f.Flow.State);
        Assert.Null(f.Flow.Offer);
        Assert.True(f.Flow.LastCheckWorked);
    }

    [Fact]
    public async Task While_the_game_runs_nothing_is_even_asked()
    {
        var f = Flow(Signed("3.4.0", Installer()), null, game: () => true);
        await f.Flow.CheckAsync();
        Assert.Empty(f.Web.Requests);
        Assert.Equal(UpdateState.None, f.Flow.State);
    }

    [Fact]
    public async Task No_internet_says_nothing_to_the_user_only_to_the_log_file()
    {
        var sys = new FakeSystem();
        var log = new NullLog();
        var flow = new UpdateFlow(Client(new FakeWeb()), sys, log, () => false);           // every address answers 404
        await flow.CheckAsync();
        Assert.Equal(UpdateState.None, flow.State);
        Assert.False(flow.LastCheckWorked);
        Assert.All(log.Lines, l => Assert.StartsWith("D ", l));               // technical detail only: nothing in the activity list
        Assert.NotEmpty(log.Lines);
        Assert.Empty(sys.Mutations);
    }

    [Fact]
    public async Task Installing_downloads_verifies_leaves_a_note_starts_the_installer_through_the_shell_and_waits_for_it()
    {
        var file = Installer();
        var f = Flow(Signed("3.4.0", file), file);
        await f.Flow.CheckAsync();

        var started = await f.Flow.InstallAsync();
        Assert.True(started);
        Assert.Equal(UpdateState.Installing, f.Flow.State);
        Assert.Equal(1.0, f.Flow.Progress, 3);

        var setup = Assert.Single(Left().Where(p => p.EndsWith("Antilag-Setup-3.4.0.exe")));
        Assert.Equal(new[] { $"restart {setup}" }, f.Sys.Mutations);          // RestartProcess = the de-elevated explorer hand-off
        Assert.False(File.Exists(setup + ".update"), "the installer took the note: it started");
    }

    [Fact]
    public async Task The_app_does_not_close_for_an_installer_that_never_says_it_started()
    {
        var file = Installer();
        var f = Flow(Signed("3.4.0", file), file, installerStarts: false);     // the shell hand-off "worked" but no installer ever appeared
        await f.Flow.CheckAsync();
        var sw = Stopwatch.StartNew();
        Assert.False(await f.Flow.InstallAsync());
        Assert.True(sw.Elapsed >= TimeSpan.FromMilliseconds(350));
        Assert.Equal(UpdateState.Failed, f.Flow.State);
        Assert.Equal(UpdateFlow.FailedStart, f.Flow.FailureText);
        Assert.Empty(Left().Where(p => p.EndsWith(".update")));                // no stale note is left behind
    }

    [Fact]
    public async Task While_the_game_runs_nothing_is_downloaded_or_started()
    {
        var file = Installer();
        var running = false;
        var f = Flow(Signed("3.4.0", file), file, game: () => running);
        await f.Flow.CheckAsync();
        running = true;
        Assert.False(await f.Flow.InstallAsync());
        Assert.Equal(UpdateState.Failed, f.Flow.State);
        Assert.Equal(UpdateFlow.FailedGame, f.Flow.FailureText);
        Assert.Empty(f.Sys.Mutations);
        Assert.DoesNotContain(f.Web.Requests, r => r.RequestUri!.AbsolutePath.EndsWith("Antilag-Setup.exe"));
    }

    [Fact]
    public async Task The_game_starting_during_the_download_stops_the_installer_from_starting()
    {
        var file = Installer();
        var running = false;
        var f = Flow(Signed("3.4.0", file), file, game: () => running);
        await f.Flow.CheckAsync();
        f.Flow.Changed += () => { if (f.Flow.Progress >= 1.0) running = true; };
        Assert.False(await f.Flow.InstallAsync());
        Assert.Equal(UpdateFlow.FailedGame, f.Flow.FailureText);
        Assert.Empty(f.Sys.Mutations);
    }

    [Fact]
    public async Task A_swapped_installer_ends_with_a_plain_sentence_and_nothing_is_started()
    {
        var file = Installer();
        var f = Flow(Signed("3.4.0", file), Installer(fill: 1));
        await f.Flow.CheckAsync();
        Assert.False(await f.Flow.InstallAsync());
        Assert.Equal(UpdateState.Failed, f.Flow.State);
        Assert.Equal(UpdateFlow.FailedIntegrity, f.Flow.FailureText);
        Assert.Empty(f.Sys.Mutations);
        Assert.Empty(Left());
        Assert.DoesNotContain(f.Log.Lines, l => !l.StartsWith("D "));
    }

    [Fact]
    public async Task A_network_failure_while_downloading_says_to_check_the_internet()
    {
        var file = Installer();
        var f = Flow(Signed("3.4.0", file), null);                             // the installer address answers 404
        await f.Flow.CheckAsync();
        Assert.False(await f.Flow.InstallAsync());
        Assert.Equal(UpdateFlow.FailedDownload, f.Flow.FailureText);
        Assert.Equal(UpdateState.Failed, f.Flow.State);

        f.Flow.Dismiss();                                                       // "Kapat" on the failed card
        Assert.Equal(UpdateState.None, f.Flow.State);
    }

    [Fact]
    public async Task When_the_installer_cannot_be_started_the_card_says_so_and_can_try_again()
    {
        var file = Installer();
        var f = Flow(Signed("3.4.0", file), file);
        var failFirst = true;
        var consume = f.Sys.OnMutation!;
        f.Sys.OnMutation = what =>
        {
            if (!what.StartsWith("restart ")) return;
            if (failFirst) { failFirst = false; f.Sys.FailRestartPaths.Add(what["restart ".Length..]); return; }   // the explorer hand-off throws
            consume(what);
        };
        await f.Flow.CheckAsync();
        Assert.False(await f.Flow.InstallAsync());
        Assert.Equal(UpdateFlow.FailedStart, f.Flow.FailureText);

        Assert.True(await f.Flow.InstallAsync());                               // the second try works
        Assert.Equal(UpdateState.Installing, f.Flow.State);
    }

    [Fact]
    public async Task Cancelling_a_download_returns_to_the_offer()
    {
        var file = Installer();
        var f = Flow(Signed("3.4.0", file), file);
        await f.Flow.CheckAsync();
        f.Flow.Changed += () => { if (f.Flow.State == UpdateState.Downloading && f.Flow.Progress > 0) f.Flow.Cancel(); };
        Assert.False(await f.Flow.InstallAsync());
        Assert.Equal(UpdateState.Available, f.Flow.State);
        Assert.Empty(f.Sys.Mutations);
        Assert.Empty(Left());
    }

    [Fact]
    public async Task Nothing_happens_without_an_offer_or_while_already_working()
    {
        var f = Flow(Signed("3.3.0", Installer()), null);
        await f.Flow.CheckAsync();
        Assert.False(await f.Flow.InstallAsync());                              // nothing newer was found
        Assert.Equal(UpdateState.None, f.Flow.State);
        Assert.DoesNotContain(f.Web.Requests, r => r.RequestUri!.AbsolutePath.EndsWith("Antilag-Setup.exe"));
    }

    [Fact]
    public void The_update_texts_use_the_agreed_vocabulary()
    {
        foreach (var t in new[] { UpdateFlow.FailedDownload, UpdateFlow.FailedIntegrity, UpdateFlow.FailedStart, UpdateFlow.FailedGame })
            foreach (var banned in new[] { "Clear", "Hazırla", "Sade", "Detaylı", "token", "exception", "http" })
                Assert.DoesNotContain(banned, t, StringComparison.OrdinalIgnoreCase);
    }

    // ------------------------------------------------------------------ what the second review found missing

    [Fact]
    public void The_real_web_handler_follows_no_redirect_and_keeps_no_cookies()
    {
        using var handler = UpdateClient.CreateHandler();
        Assert.False(handler.AllowAutoRedirect);      // the address rules only work because every hop is followed by hand
        Assert.False(handler.UseCookies);
    }

    private sealed class UnknownLengthContent : HttpContent
    {
        private readonly byte[] _bytes;
        public UnknownLengthContent(byte[] bytes) => _bytes = bytes;
        protected override async Task SerializeToStreamAsync(Stream stream, TransportContext? context) => await stream.WriteAsync(_bytes);
        protected override bool TryComputeLength(out long length) { length = -1; return false; }
    }

    /// <summary>A stream that delivers its bytes slowly, like a real download (HttpContent that is not buffered first).</summary>
    private sealed class SlowStream : Stream
    {
        private readonly byte[] _data; private readonly int _chunk; private readonly int _pauseMs; private int _pos;
        public SlowStream(byte[] data, int chunk, int pauseMs) { _data = data; _chunk = chunk; _pauseMs = pauseMs; }
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (_pos >= _data.Length) return 0;
            await Task.Delay(_pauseMs, cancellationToken);
            var n = Math.Min(Math.Min(buffer.Length, _chunk), _data.Length - _pos);
            _data.AsMemory(_pos, n).CopyTo(buffer);
            _pos += n;
            return n;
        }
        public override int Read(byte[] buffer, int offset, int count) => ReadAsync(buffer.AsMemory(offset, count)).AsTask().GetAwaiter().GetResult();
        public override bool CanRead => true; public override bool CanSeek => false; public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException(); public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    [Fact]
    public async Task A_body_without_a_length_that_is_too_long_or_too_short_is_a_network_problem_and_leaves_nothing_behind()
    {
        var file = Installer();
        var offer = (await Client(WebWith(Signed("3.4.0", file))).CheckAsync())!;
        foreach (var size in new[] { file.Length + 5, file.Length - 5 })
        {
            var web = WebWith(Signed("3.4.0", file));
            web.Routes[$"{Base}/download/v3.4.0/Antilag-Setup.exe"] = () => new HttpResponseMessage(HttpStatusCode.OK) { Content = new UnknownLengthContent(Installer(size)) };
            var ex = await Assert.ThrowsAsync<UpdateException>(() => Client(web).DownloadAsync(offer));
            Assert.False(ex.Integrity);                                  // the streaming guards, not the header check, did this
            Assert.Empty(Left());
        }
    }

    [Fact]
    public async Task A_check_that_times_out_is_a_failed_check_and_not_a_silent_cancel()
    {
        var web = new FakeWeb();
        web.Routes[$"{Base}/latest/download/latest.json"] = () => throw new TaskCanceledException("timeout");
        var log = new NullLog();
        var flow = new UpdateFlow(Client(web), new FakeSystem(), log, () => false);
        Assert.Equal(UpdateCheck.Failed, await flow.CheckAsync());
        Assert.False(flow.LastCheckWorked);
        Assert.Contains(log.Lines, l => l.StartsWith("D "));
    }

    [Fact]
    public async Task A_manifest_that_does_not_verify_is_reported_as_rejected_not_as_a_missing_internet()
    {
        var stranger = UpdateSigning.NewKeyPair();
        var f = Flow(Signed("9.0.0", Installer(), stranger.PrivateKey), null);
        Assert.Equal(UpdateCheck.Failed, await f.Flow.CheckAsync());
        Assert.True(f.Flow.LastCheckRejected);
        Assert.Equal(UpdateState.None, f.Flow.State);                    // and nothing is offered
    }

    [Fact]
    public async Task A_download_that_is_cut_off_says_to_check_the_internet_not_that_the_installer_could_not_start()
    {
        var file = Installer();
        var web = WebWith(Signed("3.4.0", file));
        web.Routes[$"{Base}/download/v3.4.0/Antilag-Setup.exe"] = () => new HttpResponseMessage(HttpStatusCode.OK) { Content = new FailingContent(file.Length / 2) };
        var flow = new UpdateFlow(Client(web), new FakeSystem(), new NullLog(), () => false);
        await flow.CheckAsync();
        Assert.False(await flow.InstallAsync());
        Assert.Equal(UpdateFlow.FailedDownload, flow.FailureText);
        Assert.Empty(Left());
    }

    [Fact]
    public async Task A_file_swapped_after_the_download_is_caught_by_the_second_check_before_the_installer_starts()
    {
        var file = Installer();
        var f = Flow(Signed("3.4.0", file), file);
        await f.Flow.CheckAsync();
        var swapped = false;
        f.Flow.Changed += () =>
        {
            if (swapped || f.Flow.State != UpdateState.Downloading || f.Flow.Progress < 1.0) return;
            swapped = true;                                              // the last report comes after the file was completed and closed
            File.WriteAllBytes(Left().Single(p => p.EndsWith("Antilag-Setup-3.4.0.exe")), Installer(fill: 2));
        };
        Assert.False(await f.Flow.InstallAsync());
        Assert.True(swapped);
        Assert.Equal(UpdateFlow.FailedIntegrity, f.Flow.FailureText);
        Assert.Empty(f.Sys.Mutations);
        Assert.Empty(Left());
    }

    [Fact]
    public async Task The_game_starting_while_the_installer_is_downloading_stops_the_download()
    {
        var file = Installer();
        var running = false;
        var web = WebWith(Signed("3.4.0", file));
        web.Routes[$"{Base}/download/v3.4.0/Antilag-Setup.exe"] = () => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(new SlowStream(file, 300_000, 450)) };
        var sys = new FakeSystem();
        var flow = new UpdateFlow(Client(web), sys, new NullLog(), () => running);
        await flow.CheckAsync();
        var install = flow.InstallAsync();
        await Task.Delay(700);
        running = true;                                                  // the user starts CS2 in the middle of the transfer
        Assert.False(await install);
        Assert.Equal(UpdateState.Failed, flow.State);
        Assert.Equal(UpdateFlow.FailedGame, flow.FailureText);
        Assert.Empty(sys.Mutations);
        Assert.Empty(Left());
    }

    [Fact]
    public async Task A_check_that_is_still_on_its_way_never_overwrites_an_update_that_has_started()
    {
        var file = Installer();
        var f = Flow(Signed("3.4.0", file), file);
        await f.Flow.CheckAsync();                                       // 3.4.0 is offered
        var gate = new TaskCompletionSource();
        f.Web.Gate = async req => { if (req.RequestUri!.AbsolutePath.EndsWith("latest.json")) await gate.Task; };
        var check = f.Flow.CheckAsync();                                 // a second, slow check (Şimdi denetle)
        Assert.True(await f.Flow.InstallAsync());                        // meanwhile the user pressed Güncelle: the installer starts
        gate.SetResult();
        Assert.Equal(UpdateCheck.Busy, await check);
        Assert.Equal(UpdateState.Installing, f.Flow.State);              // the late answer did not put the card back to "Available"
    }

    [Fact]
    public async Task The_installer_taking_a_while_to_start_is_waited_for()
    {
        var file = Installer();
        var f = Flow(Signed("3.4.0", file), file, installerStarts: false);
        f.Flow.StartTimeout = TimeSpan.FromSeconds(10);
        f.Sys.OnMutation = what =>
        {
            if (!what.StartsWith("restart ")) return;
            var note = what["restart ".Length..] + ".update";
            _ = Task.Run(async () => { await Task.Delay(350); File.Delete(note); });      // a slow start (a virus scanner looks at the new file first)
        };
        await f.Flow.CheckAsync();
        var sw = Stopwatch.StartNew();
        Assert.True(await f.Flow.InstallAsync());
        Assert.True(sw.Elapsed >= TimeSpan.FromMilliseconds(300));
        Assert.Equal(UpdateState.Installing, f.Flow.State);
    }

    [Fact]
    public async Task A_second_press_while_an_update_is_going_on_does_not_start_a_second_download()
    {
        var file = Installer();
        var web = WebWith(Signed("3.4.0", file));
        web.Routes[$"{Base}/download/v3.4.0/Antilag-Setup.exe"] = () => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(new SlowStream(file, 400_000, 200)) };
        var sys = new FakeSystem();
        sys.OnMutation = what => { if (what.StartsWith("restart ")) File.Delete(what["restart ".Length..] + ".update"); };
        var flow = new UpdateFlow(Client(web), sys, new NullLog(), () => false) { StartTimeout = TimeSpan.FromMilliseconds(400) };
        await flow.CheckAsync();
        var first = flow.InstallAsync();
        await Task.Delay(100);
        Assert.False(await flow.InstallAsync());                         // refused: one is already running
        Assert.True(await first);
        Assert.Equal(1, web.Requests.Count(r => r.RequestUri!.AbsolutePath.EndsWith("Antilag-Setup.exe")));
    }

    [Fact]
    public async Task A_planted_link_in_place_of_the_PARENT_of_the_download_folder_is_refused_too()
    {
        var elsewhere = Path.Combine(Path.GetTempPath(), "antilag-elsewhere-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(elsewhere);
        Directory.CreateDirectory(_outer);
        var link = Path.Combine(_outer, "link");
        try
        {
            MakeJunction(link, elsewhere);
            var file = Installer();
            var web = WebWith(Signed("3.4.0", file), file);
            using var client = new UpdateClient(new Version(3, 3, 0, 0), web, Base, new[] { _keys.PublicKey }, Path.Combine(link, "update"));
            var offer = (await client.CheckAsync())!;
            var ex = await Assert.ThrowsAsync<UpdateException>(() => client.DownloadAsync(offer));
            Assert.False(ex.Integrity);
            Assert.Empty(Directory.GetFileSystemEntries(elsewhere));     // nothing was created behind the link
        }
        finally
        {
            try { Directory.Delete(link); } catch { }
            try { Directory.Delete(elsewhere, true); } catch { }
        }
    }
}

/// <summary>An update must never damage the journal that makes "Geri yükle" possible.</summary>
[Collection("Sequential")]
public class JournalFormatTests
{
    [Fact]
    public void A_journal_has_a_format_number()
    {
        using var env = new TestEnv(ProfileKind.Safe);
        var store = new SessionStore();
        store.Begin("Hafif");
        Assert.Contains("\"Schema\": 1", File.ReadAllText(AppPaths.StateFile));
    }

    [Fact]
    public void A_journal_from_a_newer_Antilag_is_left_alone_and_the_restore_says_why()
    {
        using var env = new TestEnv(ProfileKind.Safe);
        // values this version has never heard of (a new enum name) must not make the file "corrupt"
        var future = "{ \"Schema\": 99, \"Profile\": \"Hafif\", \"Services\": [ { \"Name\": \"x\", \"Mode\": \"FromTheFuture\" } ] }";
        File.WriteAllText(AppPaths.StateFile, future);

        var orc = env.Restart();
        Assert.True(orc.SessionActive);                                   // the restore button stays there
        var res = orc.Revert(env.Settings);
        Assert.False(res.Complete);
        Assert.Contains(res.Problems, p => p.Contains("daha yeni"));
        Assert.True(env.Store.NewerFormat);
        Assert.Equal(future, File.ReadAllText(AppPaths.StateFile));       // not rewritten, not set aside
        Assert.Empty(Directory.GetFiles(AppPaths.Root, "state.*.json"));  // no state.corrupt-*.json
    }

    [Fact]
    public void A_manual_close_does_not_overwrite_a_newer_journal()
    {
        using var env = new TestEnv(ProfileKind.Safe);
        var future = "{ \"Schema\": 99, \"Profile\": \"Hafif\" }";
        File.WriteAllText(AppPaths.StateFile, future);
        var orc = env.Restart();
        var closed = orc.CloseByUser("Dropbox", false);
        Assert.False(closed);
        Assert.Equal(future, File.ReadAllText(AppPaths.StateFile));
        Assert.Contains(env.Sys.Procs, p => p.Name == "Dropbox");        // nothing was closed without a journal to put it in
    }

    [Fact]
    public void A_journal_of_this_version_and_an_older_one_without_a_number_load_as_before()
    {
        using var env = new TestEnv(ProfileKind.Safe);
        File.WriteAllText(AppPaths.StateFile, "{ \"Profile\": \"Hafif\" }");     // written by 3.3 and earlier: no Schema
        var loaded = new SessionStore().LoadPending();
        Assert.NotNull(loaded);
        Assert.Equal(1, loaded!.Schema);
    }
}
