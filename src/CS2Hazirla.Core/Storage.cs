using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CS2Hazirla.Core;

public static class AppPaths
{
    /// <summary>%APPDATA%\CS2Hazirla, or the folder in CS2HAZIRLA_DATA (used for tests / portable use).</summary>
    public static string Root { get; set; } =
        Environment.GetEnvironmentVariable("CS2HAZIRLA_DATA") is { Length: > 0 } custom
            ? custom
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "CS2Hazirla");

    public static string SettingsFile => Path.Combine(Root, "settings.json");
    public static string StateFile => Path.Combine(Root, "state.json");
    public static string LogDir => Path.Combine(Root, "logs");

    public static void Ensure()
    {
        Directory.CreateDirectory(Root);
        Directory.CreateDirectory(LogDir);
    }
}

internal static class Json
{
    public static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    /// <summary>Write to a temp file (flushed to disk) and swap, so a kill or a power cut mid-write never leaves a half-written file.</summary>
    public static void WriteAtomic(string path, string json)
    {
        var dir = Path.GetDirectoryName(path)!;
        Directory.CreateDirectory(dir);
        var tmp = path + ".tmp";
        using (var fs = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            fs.Write(new UTF8Encoding(false).GetBytes(json));
            fs.Flush(true);   // through the OS cache to the disk BEFORE the old file is replaced
        }
        // an antivirus scanner or the search indexer can hold the target for a moment
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                if (File.Exists(path)) File.Replace(tmp, path, null);
                else File.Move(tmp, path);
                return;
            }
            catch (IOException) when (attempt < 3) { Thread.Sleep(50 * attempt); }
        }
    }
}

public class SettingsStore
{
    /// <summary>Set by <see cref="Load"/> when settings.json existed but could not be read: the sentence for the activity log (null = all fine).</summary>
    public string? LastLoadWarning { get; private set; }

    public AppSettings Load()
    {
        LastLoadWarning = null;
        if (File.Exists(AppPaths.SettingsFile))
        {
            try
            {
                var s = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(AppPaths.SettingsFile), Json.Options);
                if (s != null)
                {
                    // explicit nulls in a hand-edited file must never crash the app
                    s.Options ??= new(); s.NeverClose ??= new(); s.ExtraProcesses ??= new();
                    s.DisplayNames ??= new();
                    // saved profiles: a hand-edited file must never crash the app or hide a profile behind a duplicate id
                    s.SavedProfiles ??= new();
                    s.SavedProfiles.RemoveAll(p => p == null);
                    var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    foreach (var p in s.SavedProfiles)
                    {
                        if (string.IsNullOrWhiteSpace(p.Id) || !ids.Add(p.Id)) { p.Id = Guid.NewGuid().ToString("N"); ids.Add(p.Id); }
                        p.Options ??= new();
                        if (string.IsNullOrWhiteSpace(p.Name)) p.Name = "Profil";
                    }
                    // only a MISSING list gets the defaults: a user who removed every service from the list keeps it empty
                    s.Services ??= Catalog.DefaultServices();
                    if (string.IsNullOrWhiteSpace(s.PingTarget)) s.PingTarget = "1.1.1.1";
                    return s;
                }
            }
            catch
            {
                // unreadable settings must never block the app: fall back to defaults (below), but keep the file
            }
            LastLoadWarning = KeepBrokenCopy();
        }

        var fresh = new AppSettings();
        fresh.ApplyProfile(ProfileKind.Maximum);
        return fresh;
    }

    /// <summary>The next Save would overwrite the unreadable file: a copy (never a deletion) keeps the user's lists and profiles recoverable.</summary>
    private static string KeepBrokenCopy()
    {
        var copy = $"settings.bozuk-{DateTime.Now:yyyyMMdd-HHmmss}.json";
        try
        {
            var target = Path.Combine(AppPaths.Root, copy);
            if (!File.Exists(target)) File.Copy(AppPaths.SettingsFile, target);
            return $"Ayarlar okunamadı, varsayılanlar kullanıldı; eski dosya {copy} olarak saklandı.";
        }
        catch { return "Ayarlar okunamadı, varsayılanlar kullanıldı."; }
    }

    public void Save(AppSettings s) =>
        Json.WriteAtomic(AppPaths.SettingsFile, JsonSerializer.Serialize(s, Json.Options));
}

/// <summary>Holds the write-ahead journal (state.json) of the active session.</summary>
public class SessionStore
{
    public SessionState? Current { get; private set; }

    /// <summary>state.json exists but was written by a newer Antilag than this one: it was NOT touched, and this version cannot restore from it.</summary>
    public bool NewerFormat { get; private set; }

    public bool HasPendingSession()
    {
        try { return File.Exists(AppPaths.StateFile); }
        catch { return false; }
    }

    public SessionState Begin(string profile)
    {
        Current = new SessionState { Profile = profile, StartedUtc = DateTime.UtcNow };
        Save();
        return Current;
    }

    /// <summary>Takes over an existing journal (state recovered from disk / used by tests).</summary>
    public void Adopt(SessionState s) { Current = s; Save(); }

    public SessionState? LoadPending()
    {
        try
        {
            NewerFormat = false;
            if (!File.Exists(AppPaths.StateFile)) return Current = null;
            var text = File.ReadAllText(AppPaths.StateFile);
            if (SchemaOf(text) > SessionState.CurrentSchema) { NewerFormat = true; return Current = null; }
            Current = JsonSerializer.Deserialize<SessionState>(text, Json.Options);
            if (Current == null) Quarantine("corrupt");
            return Current;
        }
        catch
        {
            // an unreadable journal must not block the app forever
            Quarantine("corrupt");
            return null;
        }
    }

    /// <summary>The format number written in the file (0 when there is none or the file cannot be read as JSON): read before anything else is parsed, because a newer file may hold values this version does not know.</summary>
    private static int SchemaOf(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            foreach (var p in doc.RootElement.EnumerateObject())
                if (p.Name.Equals("Schema", StringComparison.OrdinalIgnoreCase) && p.Value.TryGetInt32(out var n)) return n;
        }
        catch { }
        return 0;
    }

    private readonly object _gate = new();

    public void Save()
    {
        lock (_gate)
        {
            if (Current == null) return;
            Json.WriteAtomic(AppPaths.StateFile, JsonSerializer.Serialize(Current, Json.Options));
        }
    }

    /// <summary>Moves state.json aside so it can no longer block anything. The file is never deleted unless a copy of it was kept.</summary>
    public void Quarantine(string reason)
    {
        lock (_gate)
        {
            Current = null;
            var aside = Path.Combine(AppPaths.Root, $"state.{reason}-{DateTime.Now:yyyyMMdd-HHmmss}.json");
            try
            {
                if (File.Exists(AppPaths.StateFile)) File.Move(AppPaths.StateFile, aside, true);
            }
            catch
            {
                // the move failed (file held by another program?): copy, and remove the original only when the copy exists
                try { File.Copy(AppPaths.StateFile, aside, true); File.Delete(AppPaths.StateFile); } catch { }
            }
        }
    }

    public void Clear()
    {
        Current = null;
        try { if (File.Exists(AppPaths.StateFile)) File.Delete(AppPaths.StateFile); }
        catch { /* best effort */ }
    }
}

public interface ILog
{
    void Info(string message);
    void Warn(string message);
    void Error(string message);
    /// <summary>Technical detail (raw exception text...) for the log FILE only: it is never shown to the user.</summary>
    void Detail(string message);
}

public class FileLog : ILog
{
    private readonly object _gate = new();
    public event Action<string>? Line;
    /// <summary>(level, message): lets the UI show plain text with its own icons instead of the technical file format.</summary>
    public event Action<string, string>? Entry;

    public void Info(string message) => Write("INFO", message);
    public void Warn(string message) => Write("UYARI", message);
    public void Error(string message) => Write("HATA", message);
    public void Detail(string message) => Write("AYRINTI", message, show: false);

    private void Write(string level, string message, bool show = true)
    {
        var line = $"{DateTime.Now:HH:mm:ss} [{level}] {message}";
        try
        {
            lock (_gate)
            {
                Directory.CreateDirectory(AppPaths.LogDir);
                File.AppendAllText(Path.Combine(AppPaths.LogDir, $"{DateTime.Now:yyyy-MM-dd}.log"), line + Environment.NewLine);
            }
        }
        catch { /* logging must never break a revert */ }
        if (!show) return;
        Line?.Invoke(line);
        Entry?.Invoke(level, message);
    }

    public static void PruneOldLogs(int keep = 14)
    {
        try
        {
            var files = new DirectoryInfo(AppPaths.LogDir).GetFiles("*.log").OrderByDescending(f => f.Name).Skip(keep);
            foreach (var f in files) f.Delete();
        }
        catch { }
    }
}

public class NullLog : ILog
{
    public List<string> Lines { get; } = new();
    public void Info(string message) => Lines.Add("I " + message);
    public void Warn(string message) => Lines.Add("W " + message);
    public void Error(string message) => Lines.Add("E " + message);
    public void Detail(string message) => Lines.Add("D " + message);
}
