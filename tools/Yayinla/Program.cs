using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using CS2Hazirla.Core;

// Antilag yayın aracı (yalnızca proje sahibinin bilgisayarında kullanılır; uygulamanın parçası değildir).
//   anahtar-uret                         günlük imza anahtarını bir kez üretir (bu bilgisayarda, Windows hesabına bağlı şifreli saklanır)
//   yedek-anahtar-uret [--parola-dosyasi f]  yedek anahtarı üretir; parolayla şifrelenir (parola sorulur ya da dosyadan okunur ve dosya silinir), bir USB belleğe de kopyala
//   imzala <Setup.exe> --beklenen x.y.z [--onceki vA.B.C] [--yedek] [--cikis latest.json]
//   dogrula <latest.json> <Setup.exe>    imzayı ve dosyayı denetler
//   kontrol [--surum 3.3.0] [--indir]    uygulamanın yaptığı gibi GitHub'daki en yeni sürüme bakar; --indir kurulum dosyasını da indirip doğrular ve siler

static string KeyDir() => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Antilag-Yayin");
static string KeyFile() => Path.Combine(KeyDir(), "imza.anahtar");
static string DefaultBackupDir() => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Antilag-yedek-anahtar");

static string? Option(string[] args, string name)
{
    var i = Array.IndexOf(args, name);
    return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
}

static int Fail(string message)
{
    Console.Error.WriteLine("HATA: " + message);
    return 1;
}

// the everyday key never lies around as plain text: it is encrypted for this Windows account (DPAPI), so a copy of the file is useless elsewhere
const string EntropyText = "Antilag-imza-anahtari-v1";

static string ProtectKey(string privateKeyBase64) =>
    "dpapi1:" + Convert.ToBase64String(ProtectedData.Protect(Convert.FromBase64String(privateKeyBase64), Encoding.UTF8.GetBytes(EntropyText), DataProtectionScope.CurrentUser));

static string LoadEverydayKey()
{
    var text = File.ReadAllText(KeyFile()).Trim();
    if (text.StartsWith("dpapi1:", StringComparison.Ordinal))
        return Convert.ToBase64String(ProtectedData.Unprotect(Convert.FromBase64String(text["dpapi1:".Length..]), Encoding.UTF8.GetBytes(EntropyText), DataProtectionScope.CurrentUser));
    // a key written as plain text by an earlier version of this tool: protect it now
    File.WriteAllText(KeyFile(), ProtectKey(text));
    Console.WriteLine("Eski düz metin anahtar dosyası şifrelendi.");
    return text;
}

static string ReadSecret(string prompt)
{
    Console.Write(prompt);
    var sb = new StringBuilder();
    while (true)
    {
        var key = Console.ReadKey(true);
        if (key.Key == ConsoleKey.Enter) break;
        if (key.Key == ConsoleKey.Backspace) { if (sb.Length > 0) { sb.Length--; Console.Write("\b \b"); } continue; }
        if (!char.IsControl(key.KeyChar)) { sb.Append(key.KeyChar); Console.Write('*'); }
    }
    Console.WriteLine();
    return sb.ToString();
}

// the public keys that the copies of a released version trust: read from the source of that tag (empty when the tag has no updater yet)
static List<string> PreviousKeys(string tag)
{
    try
    {
        var psi = new ProcessStartInfo("git", $"show {tag}:src/CS2Hazirla.Core/Update.cs") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
        using var proc = Process.Start(psi)!;
        var text = proc.StandardOutput.ReadToEnd();
        proc.WaitForExit();
        if (proc.ExitCode != 0) return new List<string>();
        var start = text.IndexOf("PublicKeys", StringComparison.Ordinal);
        if (start < 0) return new List<string>();
        var end = text.IndexOf("};", start, StringComparison.Ordinal);
        var block = end > start ? text[start..end] : text[start..];
        return System.Text.RegularExpressions.Regex.Matches(block, "\"(MFkw[A-Za-z0-9+/=]{80,})\"").Select(m => m.Groups[1].Value).ToList();
    }
    catch { return new List<string>(); }
}

static string BackupFile(string[] args) => Path.Combine(Option(args, "--klasor") ?? DefaultBackupDir(), "yedek.anahtar");

if (args.Length == 0) return Fail("Komut yok. Kullanım: anahtar-uret | yedek-anahtar-uret | imzala <Setup.exe> | dogrula <latest.json> <Setup.exe> | kontrol");

switch (args[0])
{
    case "anahtar-uret":
    {
        var file = KeyFile();
        if (File.Exists(file))
        {
            Console.WriteLine("İmza anahtarı zaten var, üzerine yazılmadı: " + file);
            Console.WriteLine("Genel anahtar: " + UpdateSigning.PublicKeyOf(LoadEverydayKey()));
            return 0;
        }
        var (priv, pub) = UpdateSigning.NewKeyPair();
        Directory.CreateDirectory(KeyDir());
        File.WriteAllText(file, ProtectKey(priv));
        Console.WriteLine("İmza anahtarı yazıldı (bu Windows hesabına bağlı şifreli): " + file);
        Console.WriteLine("Genel anahtar (kaynak koda yazılır): " + pub);
        return 0;
    }

    case "yedek-anahtar-uret":
    {
        var file = BackupFile(args);
        if (File.Exists(file)) return Fail("Yedek anahtar zaten var, üzerine yazılmadı: " + file);
        if (file.Contains("OneDrive", StringComparison.OrdinalIgnoreCase) || file.Contains("Dropbox", StringComparison.OrdinalIgnoreCase))
            Console.WriteLine("UYARI: bu klasör bir bulut eşitleme klasörü görünüyor; yedek anahtarı bulutta tutmak istemiyorsan --klasor ile başka bir yer ver (örneğin USB belleğin).");
        string pass;
        var passFile = Option(args, "--parola-dosyasi");
        if (passFile != null)
        {
            // for a passphrase that is handed over in a file (it is deleted as soon as it has been read)
            if (!File.Exists(passFile)) return Fail("Parola dosyası bulunamadı.");
            pass = File.ReadAllText(passFile).Trim();
            try { File.Delete(passFile); } catch { }
            if (pass.Length < 12) return Fail("Parola en az 12 karakter olmalı.");
        }
        else
        {
            pass = ReadSecret("Yedek anahtar için bir parola seç (en az 12 karakter; bir yere yaz, kaybolursa yedek işe yaramaz): ");
            if (pass.Length < 12) return Fail("Parola en az 12 karakter olmalı.");
            if (pass != ReadSecret("Parolayı bir daha yaz: ")) return Fail("Parolalar aynı değil.");
        }

        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var encrypted = key.ExportEncryptedPkcs8PrivateKey(pass, new PbeParameters(PbeEncryptionAlgorithm.Aes256Cbc, HashAlgorithmName.SHA256, 600_000));
        var pub = Convert.ToBase64String(key.ExportSubjectPublicKeyInfo());
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        File.WriteAllText(file, Convert.ToBase64String(encrypted));
        File.WriteAllText(Path.Combine(Path.GetDirectoryName(file)!, "yedek.genel.txt"), pub);
        Console.WriteLine("Yedek anahtar yazıldı: " + file);
        Console.WriteLine("Bu klasörü bir USB belleğe kopyala. Parola olmadan işe yaramaz; parola olmadan da kurtarılamaz.");
        Console.WriteLine("Genel anahtar (kaynak koda yazılır): " + pub);
        return 0;
    }

    case "imzala":
    {
        if (args.Length < 2 || !File.Exists(args[1])) return Fail("Kurulum dosyası bulunamadı.");
        var setup = args[1];

        string priv;
        if (args.Contains("--yedek"))
        {
            var backup = BackupFile(args);
            if (!File.Exists(backup)) return Fail("Yedek anahtar dosyası bulunamadı: " + backup);
            try
            {
                using var key = ECDsa.Create();
                string passphrase;
                var passFile = Option(args, "--parola-dosyasi");
                if (passFile != null && File.Exists(passFile)) { passphrase = File.ReadAllText(passFile).Trim(); try { File.Delete(passFile); } catch { } }
                else passphrase = ReadSecret("Yedek anahtarın parolası: ");
                key.ImportEncryptedPkcs8PrivateKey(passphrase, Convert.FromBase64String(File.ReadAllText(backup).Trim()), out _);
                priv = Convert.ToBase64String(key.ExportPkcs8PrivateKey());
            }
            catch (CryptographicException) { return Fail("Parola yanlış ya da dosya bozuk."); }
        }
        else
        {
            if (!File.Exists(KeyFile())) return Fail("İmza anahtarı yok. Önce: anahtar-uret");
            priv = LoadEverydayKey();
        }

        // the version is read from the installer itself and must be the one that is meant to be published: a mismatch would make every installed copy
        // download the "update", install the same old version and offer it again
        // the installer's version resource is padded with spaces and sometimes a terminating character: only "x.y.z" is meant
        var product = (FileVersionInfo.GetVersionInfo(setup).ProductVersion ?? "").Trim('\0', ' ', '\t', '\r', '\n');
        var parts = product.Split('.');
        if (parts.Length < 3) return Fail("Kurulum dosyasında sürüm numarası okunamadı.");
        var version = string.Join('.', parts.Take(3));
        var expected = Option(args, "--beklenen");
        if (expected == null) return Fail("--beklenen <sürüm> verilmeli (csproj'daki sürüm). Kurulum dosyası: " + version);
        if (expected != version) return Fail($"Kurulum dosyasının sürümü ({version}) beklenen sürümle ({expected}) aynı değil. Yayınlanmadı.");

        var bytes = File.ReadAllBytes(setup);
        var manifest = new UpdateManifest
        {
            Version = version,
            Size = bytes.Length,
            Sha256 = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(),
        };
        manifest.Signature = UpdateSigning.Sign(manifest, priv);

        // an update signed with a key that the shipped app does not know would be refused by every installed copy: stop here
        if (!UpdateSigning.PublicKeys.Contains(UpdateSigning.PublicKeyOf(priv)))
            return Fail("Bu imza anahtarının genel anahtarı, uygulamanın içindeki genel anahtarlardan hiçbiri değil. Yayınlanmadı.");

        // what matters is not this source tree but the copies that are ALREADY installed: they trust the keys of the last released tag. The key that signs
        // must be one of those, and none of them may be dropped from the new list (a copy cannot learn a key later).
        var previousTag = Option(args, "--onceki");
        if (!string.IsNullOrEmpty(previousTag))
        {
            var previous = PreviousKeys(previousTag);
            if (previous.Count > 0)
            {
                if (!previous.Contains(UpdateSigning.PublicKeyOf(priv)))
                    return Fail($"Bu anahtar {previousTag} sürümünde kurulu kopyaların güvendiği anahtarlardan biri değil: kurulu hiçbir Antilag bu güncellemeyi kabul etmez. Yayınlanmadı.");
                var dropped = previous.Where(k => !UpdateSigning.PublicKeys.Contains(k)).ToList();
                if (dropped.Count > 0)
                    return Fail($"{previousTag} sürümündeki {dropped.Count} genel anahtar yeni listede yok: kurulu kopyalar sonradan anahtar öğrenemez. Yayınlanmadı.");
            }
        }
        if (manifest.Problem() is { } problem) return Fail(problem);
        if (!UpdateSigning.Verify(manifest, UpdateSigning.PublicKeys)) return Fail("İmza doğrulanamadı.");

        var output = Option(args, "--cikis") ?? Path.Combine(Path.GetDirectoryName(Path.GetFullPath(setup))!, UpdateClient.ManifestName);
        File.WriteAllText(output, manifest.ToJson());
        Console.WriteLine($"İmzalandı: sürüm {manifest.Version}, {manifest.Size:N0} bayt, SHA-256 {manifest.Sha256}");
        Console.WriteLine("Yazıldı: " + output);
        return 0;
    }

    case "dogrula":
    {
        if (args.Length < 3 || !File.Exists(args[1]) || !File.Exists(args[2])) return Fail("Kullanım: dogrula <latest.json> <Setup.exe>");
        var manifest = UpdateManifest.FromJson(File.ReadAllText(args[1]));
        if (manifest == null) return Fail("latest.json okunamadı.");
        if (manifest.Problem() is { } problem) return Fail(problem);
        if (!UpdateSigning.Verify(manifest, UpdateSigning.PublicKeys)) return Fail("İmza uygulamadaki genel anahtarlarla doğrulanamadı.");
        var offer = new UpdateOffer(Version.Parse(manifest.Version), manifest.Size, manifest.Sha256);
        if (!UpdateClient.FileMatches(args[2], offer)) return Fail("Kurulum dosyası imzalanan dosyayla aynı değil.");
        Console.WriteLine($"Doğru: sürüm {manifest.Version}, imza ve dosya uyuşuyor.");
        return 0;
    }

    case "kontrol":
    {
        var current = Version.Parse(Option(args, "--surum") ?? "0.0.1");
        using var client = new UpdateClient(current);
        try
        {
            var offer = await client.CheckAsync();
            Console.WriteLine(offer == null
                ? $"Güncel: {current.ToString(3)} sürümünden yeni bir sürüm yok."
                : $"Yeni sürüm bulundu: {offer.VersionText} ({offer.Size:N0} bayt, imza doğrulandı).");
            if (offer != null && args.Contains("--indir"))
            {
                // the same download the app makes, into the same kind of folder, through GitHub's real redirects: size and SHA-256 must equal the signed values
                var temp = Path.Combine(Path.GetTempPath(), "antilag-kontrol-" + Guid.NewGuid().ToString("N"), "update");
                using var downloader = new UpdateClient(current, downloadRoot: temp);
                var path = await downloader.DownloadAsync(offer);
                Console.WriteLine($"İndirildi ve doğrulandı: {new FileInfo(path).Length:N0} bayt, SHA-256 imzalıyla aynı.");
                downloader.CleanDownloads();
                try { Directory.Delete(Path.GetDirectoryName(temp)!, true); } catch { }
            }
            return 0;
        }
        catch (Exception ex) { return Fail(ex.Message); }
    }

    default:
        return Fail("Bilinmeyen komut: " + args[0]);
}
