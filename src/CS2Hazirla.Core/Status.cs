namespace CS2Hazirla.Core;

public enum Tone { Neutral, Good, Warn, Bad }

public enum RestoreOutcome { None, Ok, Partial }

/// <summary>Everything the home screen's headline depends on. The UI fills it from its own state; nothing here knows about WPF.</summary>
/// <param name="NotClosed">Programs that were still running after Başlat (<see cref="PrepareResult.NotClosed"/>).</param>
/// <param name="NotRestarted">Programs "Geri yükle" could not start again (<see cref="RevertResult.NotRestarted"/>); read when <paramref name="Restore"/> is Partial.</param>
/// <param name="ErrorMessage">Set when the last action ended with an exception. It is never shown (raw text): only the fact is.</param>
public record StatusInput(
    bool Busy = false,
    bool GameRunning = false,
    bool Done = false,
    int DoneCount = 0,
    int FailedCount = 0,
    IReadOnlyList<string>? NotClosed = null,
    RestoreOutcome Restore = RestoreOutcome.None,
    IReadOnlyList<string>? NotRestarted = null,
    string? ErrorMessage = null,
    string PresetName = "",
    bool Customized = false);

public record StatusView(string Title, string Hint, Tone Tone);

/// <summary>
/// What the home screen shows besides the headline: the list of programs while a mode is being chosen, the activity list ("Neler yapıldı")
/// once something was done, and the restore button while there is something to restore. The list and the activity list never show together.
/// </summary>
public record HomeView(bool ShowList, bool ShowLog, bool ShowRestore)
{
    public static HomeView Of(bool busy, bool done, bool failed, bool hasLogLines, bool hasSession)
    {
        var log = hasLogLines && (busy || done || failed);
        return new HomeView(ShowList: !busy && !done && !log, ShowLog: log, ShowRestore: hasSession && !busy);
    }
}

/// <summary>The headline and sub-line of the home screen, decided in one testable place. A green "Bitti" is only ever shown when nothing went wrong.</summary>
public static class Status
{
    /// <summary>Order of precedence: game running, busy, error, restore result, run result, idle.</summary>
    public static StatusView Compute(StatusInput i)
    {
        if (i.GameRunning)
            return new("CS2 çalışıyor", "Oyun açıkken Antilag hiçbir şeyi değiştirmez. Oyunu kapattıktan sonra tekrar dene.", Tone.Warn);
        if (i.Busy)
            return new("Çalışıyor", "Bu yarım dakikayı bulabilir; lütfen bekle.", Tone.Neutral);
        if (!string.IsNullOrWhiteSpace(i.ErrorMessage))
            return new("İşlem tamamlanamadı", "Ayrıntı aşağıdaki listede.", Tone.Bad);

        var notRestarted = i.NotRestarted ?? Array.Empty<string>();
        if (i.Restore == RestoreOutcome.Partial && notRestarted.Count > 0)
            return new($"Geri yüklendi, {notRestarted.Count} program kendiliğinden açılamadı",
                       Names(notRestarted) + ". Gerekirse kendin aç.", Tone.Warn);
        if (i.Restore != RestoreOutcome.None)
            return new("Geri yüklendi", "Ayarlar ve hizmetler eski haline döndü.", Tone.Good);

        if (i.Done)
        {
            var notClosed = i.NotClosed ?? Array.Empty<string>();
            if (notClosed.Count > 0)
                return new($"Bitti, ancak {notClosed.Count} program kapanmadı",
                           $"Kapanmayanlar: {Names(notClosed)}." + (i.FailedCount > 0 ? $" {i.FailedCount} işlem de yapılamadı." : "") + " Ayrıntı aşağıdaki listede.", Tone.Warn);
            if (i.FailedCount > 0)
                return new($"Bitti, ancak {i.FailedCount} işlem yapılamadı", "Ayrıntı aşağıdaki listede.", Tone.Warn);
            return new("Bitti", i.DoneCount > 0
                ? $"{i.DoneCount} işlem yapıldı. Oyunu başlatmadan önce Antilag'ı kapat."
                : "Kapatılacak ya da değiştirilecek bir şey bulunamadı.", Tone.Good);
        }

        // the home screen stays plain: the chosen mode and nothing else (no sentence about what it does, no reminder of an earlier run)
        var title = string.IsNullOrWhiteSpace(i.PresetName) ? "Başlamaya hazır"
            : i.Customized ? $"{i.PresetName} (değiştirilmiş) hazır" : $"{i.PresetName} hazır";
        return new(title, "", Tone.Neutral);
    }

    /// <summary>The first three names; the rest is counted ("A, B, C ve 2 diğeri").</summary>
    private static string Names(IReadOnlyList<string> names)
    {
        var shown = string.Join(", ", names.Take(3));
        return names.Count > 3 ? $"{shown} ve {names.Count - 3} diğeri" : shown;
    }
}
