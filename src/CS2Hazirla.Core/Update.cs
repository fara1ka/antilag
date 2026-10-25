using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace CS2Hazirla.Core;

/// <summary>
/// What the owner signs for every release: latest.json, published next to the installer in the same GitHub release.
/// The signature covers the version, the size and the SHA-256 of the installer, so a file or a version can never be paired with the wrong signature.
/// </summary>
public sealed class UpdateManifest
{
    public string Version { get; set; } = "";
    public long Size { get; set; }
    public string Sha256 { get; set; } = "";
    public string Signature { get; set; } = "";

    /// <summary>The exact text that is signed.</summary>
    public string Canonical() => string.Create(CultureInfo.InvariantCulture, $"antilag-update|{Version}|{Size}|{Sha256}");

    // no leading zeros: "03.4.0" and "3.4.0" must never be two different signed texts for one download address
    private static readonly Regex VersionRx = new(@"^(0|[1-9]\d{0,3})\.(0|[1-9]\d{0,3})\.(0|[1-9]\d{0,3})$", RegexOptions.CultureInvariant);
    private static readonly Regex HashRx = new("^[0-9a-f]{64}$", RegexOptions.CultureInvariant);

    public const long MinSize = 1_000_000;
    public const long MaxSize = 500_000_000;

    /// <summary>A manifest with anything odd in it is rejected before the signature is even looked at.</summary>
    public string? Problem() =>
        !VersionRx.IsMatch(Version ?? "") ? "Sürüm numarası geçersiz."
        : Size < MinSize || Size > MaxSize ? "Dosya boyutu geçersiz."
        : !HashRx.IsMatch(Sha256 ?? "") ? "Özet (SHA-256) geçersiz."
        : string.IsNullOrEmpty(Signature) ? "İmza yok."
        : null;

    internal static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    public string ToJson() => JsonSerializer.Serialize(this, JsonOptions);

    /// <summary>Null when the text is not a manifest at all.</summary>
    public static UpdateManifest? FromJson(ReadOnlySpan<byte> utf8)
    {
        try { return JsonSerializer.Deserialize<UpdateManifest>(utf8, JsonOptions); }
        catch (JsonException) { return null; }
    }

    public static UpdateManifest? FromJson(string json) => FromJson(Encoding.UTF8.GetBytes(json));
}

/// <summary>A newer version that is signed and ready to be downloaded.</summary>
public sealed record UpdateOffer(Version Version, long Size, string Sha256)
{
    public string VersionText => Version.ToString(3);
}

public sealed class UpdateException : Exception
{
    public UpdateException(string message, bool integrity = false) : base(message) { Integrity = integrity; }

    /// <summary>
    /// The file really arrived in full and is NOT the one that was signed (or the version information does not verify). Everything else, such as a short
    /// or long answer, a web page from a hotel network or a missing release, is "the network did not work".
    /// </summary>
    public bool Integrity { get; }
}

/// <summary>ECDSA P-256 / SHA-256 signatures. The private keys stay on the owner's PC; the public keys are part of the source.</summary>
public static class UpdateSigning
{
    /// <summary>
    /// The keys an update may be signed with. The first is the everyday key. A second one is generated at the same time and kept offline: should the
    /// first ever be lost or stolen, the owner can still sign with the second, and installed copies accept it. A key that is not in this list cannot be
    /// added to copies that are already installed, so both are shipped from the first updater release on.
    /// </summary>
    public static readonly IReadOnlyList<string> PublicKeys = new[]
    {
        "MFkwEwYHKoZIzj0CAQYIKoZIzj0DAQcDQgAEB6qixvhBc/suFviRjN00dMJxEEoZLt9NHi3rr4JAPf6tPbeycGROToXEE8BlgcTcIRX1MPhk23rfVjrw0ATakQ==",   // everyday key (this PC)
        "MFkwEwYHKoZIzj0CAQYIKoZIzj0DAQcDQgAE5/u+DY0/4jndy0vF0kXFmCWEwK2xngaPpaSPKgfhkPZbjwts3catw82WZ9Y2VMKAkuIheHsju2PFlYcQ+IqZ7A==",   // spare key: encrypted with a passphrase, kept off the PC (USB stick)
    };

    public static (string PrivateKey, string PublicKey) NewKeyPair()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        return (Convert.ToBase64String(key.ExportPkcs8PrivateKey()), Convert.ToBase64String(key.ExportSubjectPublicKeyInfo()));
    }

    public static string PublicKeyOf(string privateKeyBase64)
    {
        using var key = ECDsa.Create();
        key.ImportPkcs8PrivateKey(Convert.FromBase64String(privateKeyBase64.Trim()), out _);
        return Convert.ToBase64String(key.ExportSubjectPublicKeyInfo());
    }

    public static string Sign(UpdateManifest m, string privateKeyBase64)
    {
        using var key = ECDsa.Create();
        key.ImportPkcs8PrivateKey(Convert.FromBase64String(privateKeyBase64.Trim()), out _);
        return Convert.ToBase64String(key.SignData(Encoding.UTF8.GetBytes(m.Canonical()), HashAlgorithmName.SHA256));
    }

    public static bool Verify(UpdateManifest m, string publicKeyBase64) => Verify(m, new[] { publicKeyBase64 });

    /// <summary>True when the signature was made with ANY of the keys.</summary>
    public static bool Verify(UpdateManifest m, IEnumerable<string> publicKeysBase64)
    {
        foreach (var publicKey in publicKeysBase64)
        {
            try
            {
                if (string.IsNullOrEmpty(publicKey)) continue;
                using var key = ECDsa.Create();
                key.ImportSubjectPublicKeyInfo(Convert.FromBase64String(publicKey), out _);
                if (key.VerifyData(Encoding.UTF8.GetBytes(m.Canonical()), Convert.FromBase64String(m.Signature), HashAlgorithmName.SHA256)) return true;
            }
            catch { /* a malformed key, signature or number is simply "not verified" */ }
        }
        return false;
    }
}

/// <summary>
/// Looks for a newer Antilag on the public GitHub releases of the project and downloads it. No account, no token, nothing about the user is sent
/// (only what any web request carries: the address and "Antilag/version"). Everything that is downloaded is checked against a manifest that is
/// signed with the owner's key, so a hijacked account or a swapped file cannot make the app run something the owner did not sign.
/// </summary>
public sealed class UpdateClient : IDisposable
{
    public const string ReleasesUrl = "https://github.com/fara1ka/antilag/releases";
    public const string SetupName = "Antilag-Setup.exe";
    public const string ManifestName = "latest.json";

    private readonly HttpClient _http;
    private readonly string _releases;
    private readonly IReadOnlyList<string> _publicKeys;
    private readonly Version _current;

    /// <param name="handler">Tests only: the production client always talks to GitHub over https.</param>
    public UpdateClient(Version current, HttpMessageHandler? handler = null, string? releasesUrl = null, IReadOnlyList<string>? publicKeys = null, string? downloadRoot = null)
    {
        _current = Normalize(current);
        _releases = (releasesUrl ?? ReleasesUrl).TrimEnd('/');
        _publicKeys = publicKeys ?? UpdateSigning.PublicKeys;
        DownloadRoot = downloadRoot ?? DefaultDownloadRoot;
        _http = new HttpClient(handler ?? CreateHandler()) { Timeout = Timeout.InfiniteTimeSpan };
        _http.DefaultRequestHeaders.UserAgent.ParseAdd($"Antilag/{_current.ToString(3)}");
    }

    /// <summary>
    /// The web handler of the real app. Automatic redirects are OFF on purpose: every hop is followed by hand so that each address passes
    /// <see cref="CheckAddress"/>; cookies are off because nothing here has a use for them. (Public so that a test can pin both settings.)
    /// </summary>
    public static SocketsHttpHandler CreateHandler() => new() { AllowAutoRedirect = false, UseCookies = false };

    /// <summary>
    /// A folder of the current user. Every attempt gets its own random sub-folder inside it (see <see cref="DownloadAsync"/>): a program that runs as the
    /// same user must not be able to make this administrator process write or delete somewhere else by planting a folder with a guessable name.
    /// </summary>
    public static string DefaultDownloadRoot => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Antilag", "update");
    public string DownloadRoot { get; }

    /// <summary>3.4.0 and 3.4.0.0 are the same version.</summary>
    public static Version Normalize(Version v) => new(v.Major, v.Minor, Math.Max(v.Build, 0));

    /// <summary>One small request. Returns null when this is the newest version; throws when nothing could be checked or the manifest does not verify.</summary>
    public async Task<UpdateOffer?> CheckAsync(CancellationToken ct = default)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(10));
        using var resp = await GetAsync(new Uri($"{_releases}/latest/download/{ManifestName}"), cts.Token).ConfigureAwait(false);
        if (resp.Content.Headers.ContentLength > 4096) throw new UpdateException("Sürüm bilgisi beklenenden büyük.");
        await using var stream = await resp.Content.ReadAsStreamAsync(cts.Token).ConfigureAwait(false);
        using var buffer = new MemoryStream();
        var chunk = new byte[1024];
        int n;
        while ((n = await stream.ReadAsync(chunk, cts.Token).ConfigureAwait(false)) > 0)
        {
            buffer.Write(chunk, 0, n);
            if (buffer.Length > 4096) throw new UpdateException("Sürüm bilgisi beklenenden büyük.");
        }
        return Evaluate(UpdateManifest.FromJson(buffer.ToArray()) ?? throw new UpdateException("Sürüm bilgisi okunamadı."));
    }

    /// <summary>Public so that the rules can be tested without a network.</summary>
    public UpdateOffer? Evaluate(UpdateManifest? m)
    {
        if (m == null) throw new UpdateException("Sürüm bilgisi boş.", integrity: true);
        if (m.Problem() is { } problem) throw new UpdateException(problem, integrity: true);
        if (!UpdateSigning.Verify(m, _publicKeys)) throw new UpdateException("Sürüm bilgisinin imzası doğrulanamadı.", integrity: true);
        var version = Version.Parse(m.Version);
        return Normalize(version) > _current ? new UpdateOffer(Normalize(version), m.Size, m.Sha256) : null;
    }

    /// <summary>
    /// Downloads the installer of exactly the signed version into a fresh random folder under <see cref="DownloadRoot"/>. The address is built here from the
    /// signed version number, never taken from the manifest. The file is created FIRST, before any network traffic, and stays open (others may read it, nobody
    /// may write or delete it) until it is complete: the folder is never an empty folder that could be swapped while the transfer runs. A file that is not
    /// complete and equal to the signed size and SHA-256 is deleted again, and the returned path is only ever one that passed.
    /// </summary>
    public async Task<string> DownloadAsync(UpdateOffer offer, IProgress<double>? progress = null, CancellationToken ct = default)
    {
        var dir = NewAttemptFolder();
        var final = Path.Combine(dir, $"Antilag-Setup-{offer.VersionText}.exe");
        FileStream? dst = null;

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromMinutes(15));
        try
        {
            dst = new FileStream(final, FileMode.CreateNew, FileAccess.Write, FileShare.Read);
            using var resp = await GetAsync(new Uri($"{_releases}/download/v{offer.VersionText}/{SetupName}"), cts.Token).ConfigureAwait(false);
            if (resp.Content.Headers.ContentLength is { } length && length != offer.Size) throw new UpdateException("İndirilen dosyanın boyutu beklenenden farklı.");

            using var sha = SHA256.Create();
            long total = 0;
            var lastReport = Environment.TickCount64;
            await using (var src = await resp.Content.ReadAsStreamAsync(cts.Token).ConfigureAwait(false))
            {
                var buffer = new byte[81920];
                int n;
                while ((n = await src.ReadAsync(buffer, cts.Token).ConfigureAwait(false)) > 0)
                {
                    total += n;
                    if (total > offer.Size) throw new UpdateException("İndirilen dosya beklenenden büyük.");
                    sha.TransformBlock(buffer, 0, n, null, 0);
                    await dst.WriteAsync(buffer.AsMemory(0, n), cts.Token).ConfigureAwait(false);
                    if (progress != null && Environment.TickCount64 - lastReport >= 100)   // a few updates per second are plenty for a bar
                    {
                        lastReport = Environment.TickCount64;
                        progress.Report(total / (double)offer.Size);
                    }
                }
                await dst.FlushAsync(cts.Token).ConfigureAwait(false);
            }
            sha.TransformFinalBlock(Array.Empty<byte>(), 0, 0);

            if (total != offer.Size) throw new UpdateException("İndirme yarım kaldı.");
            if (!string.Equals(Convert.ToHexString(sha.Hash!), offer.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new UpdateException("İndirilen dosya imzalanan dosyayla aynı değil.", integrity: true);

            await dst.DisposeAsync().ConfigureAwait(false);
            dst = null;
            progress?.Report(1.0);
            return final;
        }
        catch
        {
            if (dst != null) { await dst.DisposeAsync().ConfigureAwait(false); dst = null; }
            TryDelete(final);
            TryDeleteEmpty(dir);
            throw;
        }
    }

    /// <summary>The file on disk is still exactly what was signed. Called again right before the installer is started.</summary>
    public static bool FileMatches(string path, UpdateOffer offer)
    {
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists || info.Length != offer.Size) return false;
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            return string.Equals(Convert.ToHexString(SHA256.HashData(fs)), offer.Sha256, StringComparison.OrdinalIgnoreCase);
        }
        catch { return false; }
    }

    /// <summary>
    /// Removes what earlier attempts left (a finished installer, a half download, a note). It deletes only the three kinds of file this class creates,
    /// only inside folders it created (32 hex characters) and never follows a link: a planted junction is skipped, not walked into.
    /// </summary>
    public void CleanDownloads()
    {
        try
        {
            if (!Directory.Exists(DownloadRoot) || IsLink(DownloadRoot)) return;
            foreach (var sub in Directory.GetDirectories(DownloadRoot))
            {
                if (!Regex.IsMatch(Path.GetFileName(sub), "^[0-9a-f]{32}$") || IsLink(sub)) continue;
                foreach (var f in Directory.GetFiles(sub))
                {
                    var name = Path.GetFileName(f);
                    if (IsLink(f) || !(name.StartsWith("Antilag-Setup-", StringComparison.Ordinal)
                                       && (name.EndsWith(".exe", StringComparison.Ordinal) || name.EndsWith(".exe.part", StringComparison.Ordinal) || name.EndsWith(".exe.update", StringComparison.Ordinal)))) continue;
                    TryDelete(f);
                }
                TryDeleteEmpty(sub);
            }
            TryDeleteEmpty(DownloadRoot);
        }
        catch { /* best effort */ }
    }

    /// <summary>
    /// A note next to the installer tells it that it was started by the app to update itself: it then waits for the app to close, skips its questions and
    /// opens the app again afterwards, and it deletes the note as the sign that it started. (A file next to it, not a command-line argument, because the
    /// installer is started through the Windows shell so that it does not inherit the administrator rights of this app.)
    /// </summary>
    public static void WriteUpdateNote(string setupPath) => File.WriteAllText(setupPath + ".update", "Antilag güncellemesi");

    /// <summary>A download that will not be used (cancelled, failed the second check, installer did not start): the file, its note and its folder.</summary>
    public static void Discard(string setupPath)
    {
        TryDelete(setupPath);
        TryDelete(setupPath + ".part");
        TryDelete(setupPath + ".update");
        var dir = Path.GetDirectoryName(setupPath);
        if (!string.IsNullOrEmpty(dir) && Regex.IsMatch(Path.GetFileName(dir), "^[0-9a-f]{32}$") && !IsLink(dir)) TryDeleteEmpty(dir);
    }

    // ---------------------------------------------------------------- folders

    private static bool IsLink(string path)
    {
        try { return (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0; }
        catch { return false; }
    }

    private string NewAttemptFolder()
    {
        // the two folders this class creates must be real folders: a junction that someone planted there would send the writes elsewhere
        var parent = Path.GetDirectoryName(DownloadRoot);
        foreach (var folder in new[] { parent, DownloadRoot })
        {
            if (string.IsNullOrEmpty(folder)) continue;
            if (Directory.Exists(folder) && IsLink(folder)) throw new UpdateException("İndirme klasörü güvenli değil.");
        }
        Directory.CreateDirectory(DownloadRoot);
        if (IsLink(DownloadRoot)) throw new UpdateException("İndirme klasörü güvenli değil.");
        var dir = Path.Combine(DownloadRoot, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { }
    }

    private static void TryDeleteEmpty(string dir)
    {
        try { if (Directory.Exists(dir) && Directory.GetFileSystemEntries(dir).Length == 0) Directory.Delete(dir); } catch { }
    }

    // ---------------------------------------------------------------- web

    private async Task<HttpResponseMessage> GetAsync(Uri url, CancellationToken ct)
    {
        for (var hop = 0; hop < 6; hop++)
        {
            CheckAddress(url);
            var resp = await _http.SendAsync(new HttpRequestMessage(HttpMethod.Get, url), HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
            if ((int)resp.StatusCode is 301 or 302 or 303 or 307 or 308)
            {
                var next = resp.Headers.Location;
                resp.Dispose();
                if (next == null) throw new UpdateException("Yönlendirme adresi yok.");
                url = next.IsAbsoluteUri ? next : new Uri(url, next);
                continue;
            }
            if (!resp.IsSuccessStatusCode)
            {
                var code = (int)resp.StatusCode;
                resp.Dispose();
                throw new UpdateException($"Sunucu {code} yanıtı verdi.");
            }
            if (string.Equals(resp.Content.Headers.ContentType?.MediaType, "text/html", StringComparison.OrdinalIgnoreCase))
            {
                resp.Dispose();   // a log-in or block page of a hotel network, a school or a filter: the network did not work, the file is not "wrong"
                throw new UpdateException("Sunucu bir dosya yerine web sayfası gönderdi.");
            }
            return resp;
        }
        throw new UpdateException("Çok fazla yönlendirme.");
    }

    /// <summary>https only, and only GitHub's own hosts (release files are served from *.githubusercontent.com). No IP addresses, no ports, no credentials.</summary>
    private static void CheckAddress(Uri u)
    {
        var host = u.IdnHost.ToLowerInvariant();
        var ok = u.Scheme == Uri.UriSchemeHttps && u.Port == 443 && string.IsNullOrEmpty(u.UserInfo) && !IPAddress.TryParse(host.Trim('[', ']'), out _)
                 && (host == "github.com" || host.EndsWith(".githubusercontent.com", StringComparison.Ordinal));
        if (!ok) throw new UpdateException("Beklenmeyen adres: yalnızca GitHub'ın https adresleri kabul edilir.");
    }

    public void Dispose() => _http.Dispose();
}
