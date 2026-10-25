namespace CS2Hazirla.Core;

public enum UpdateState { None, Available, Downloading, Installing, Failed }

/// <summary>What a check did: it looked, it could not look, it did not look because the game runs, or it did not look because an update is already going on.</summary>
public enum UpdateCheck { Done, Failed, SkippedGame, Busy }

/// <summary>
/// The whole life of one update offer, without any window: found → downloaded and verified → installer started. The screen only shows
/// <see cref="State"/>, <see cref="Progress"/> and <see cref="FailureText"/>; "Sonra" and "Güncelle" call <see cref="Dismiss"/> and <see cref="InstallAsync"/>.
/// Nothing here runs in the background: a check is one request when the app opens (or when the user asks), and the installer is started only after a click.
/// Nothing is asked, downloaded or started while cs2.exe runs: the game is looked at before a check, before and DURING a download (once a second) and
/// right before the installer is started.
/// </summary>
public sealed class UpdateFlow
{
    public const string FailedDownload = "İndirilemedi. İnternet bağlantını kontrol edip tekrar dene.";
    public const string FailedIntegrity = "Dosya doğrulanamadığı için kurulmadı.";
    public const string FailedStart = "Kurulum başlatılamadı. Tekrar dene.";
    public const string FailedGame = "CS2 çalışıyor: oyun açıkken güncelleme yapılmaz.";

    private readonly UpdateClient _client;
    private readonly ISystem _sys;
    private readonly ILog _log;
    private readonly Func<bool> _gameRunning;
    private CancellationTokenSource? _cts;

    /// <param name="gameRunning">Required on purpose: a flow that does not know about the game must not compile.</param>
    public UpdateFlow(UpdateClient client, ISystem sys, ILog log, Func<bool> gameRunning)
    {
        _client = client; _sys = sys; _log = log; _gameRunning = gameRunning;
    }

    public UpdateState State { get; private set; }
    /// <summary>The newest signed version found, also after "Sonra" (a later manual check shows it again).</summary>
    public UpdateOffer? Offer { get; private set; }
    /// <summary>0..1 while downloading.</summary>
    public double Progress { get; private set; }
    public string FailureText { get; private set; } = "";
    /// <summary>The last check finished without an error (the version could be read).</summary>
    public bool LastCheckWorked { get; private set; }
    /// <summary>The last check reached GitHub but the version information did not verify (not the internet's fault).</summary>
    public bool LastCheckRejected { get; private set; }
    public event Action? Changed;

    /// <summary>How long the app waits for the installer to say that it started (it deletes the note next to itself). The app closes only after that.</summary>
    public TimeSpan StartTimeout { get; set; } = TimeSpan.FromSeconds(40);

    public bool IsWorking => State is UpdateState.Downloading or UpdateState.Installing;

    /// <summary>
    /// Asks GitHub for the newest signed version. A failure is only written to the log file: no internet, a captive portal or a blocked address
    /// is normal and must never nag the user.
    /// </summary>
    public async Task<UpdateCheck> CheckAsync(CancellationToken ct = default)
    {
        if (IsWorking) return UpdateCheck.Busy;
        if (_gameRunning()) return UpdateCheck.SkippedGame;
        try
        {
            var offer = await _client.CheckAsync(ct);
            if (IsWorking) return UpdateCheck.Busy;       // the user pressed Güncelle while this was on its way: that wins, nothing is overwritten
            LastCheckWorked = true;
            LastCheckRejected = false;
            Offer = offer;
            Set(offer == null ? UpdateState.None : UpdateState.Available);
            return UpdateCheck.Done;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { return UpdateCheck.Failed; }
        catch (Exception ex)
        {
            // a timeout is an OperationCanceledException too: only the caller's own cancel is silent
            LastCheckWorked = false;
            LastCheckRejected = ex is UpdateException { Integrity: true };
            _log.Detail("Güncelleme denetlenemedi: " + ex.Message);
            Changed?.Invoke();
            return UpdateCheck.Failed;
        }
    }

    /// <summary>"Sonra" or "Kapat": the card goes away until the app is opened again.</summary>
    public void Dismiss()
    {
        if (State is UpdateState.Available or UpdateState.Failed) Set(UpdateState.None);
    }

    public void Cancel() => _cts?.Cancel();

    /// <summary>Leftovers of an earlier download (the installer of the update that was just installed, for one).</summary>
    public void CleanDownloads() => _client.CleanDownloads();

#if DEBUG
    /// <summary>Developer screenshots only: shows the card in a given state without any network.</summary>
    public void Demo(UpdateState state, double progress = 0, string failure = "")
    {
        Offer = new UpdateOffer(new Version(3, 4, 0), 60_000_000, new string('a', 64));
        Progress = progress; FailureText = failure; LastCheckWorked = true;
        Set(state);
    }
#endif

    private sealed class InstallerDidNotStartException : Exception
    {
        public InstallerDidNotStartException() { }
        public InstallerDidNotStartException(Exception inner) : base("Kurulum başlatılamadı.", inner) { }
    }

    /// <summary>
    /// Downloads the installer, checks it against the signed manifest once more, starts it and waits until it says that it started. Returns true when the
    /// installer is running: the caller must close the app now (the installer waits for that).
    /// </summary>
    public async Task<bool> InstallAsync(CancellationToken ct = default)
    {
        if (Offer is not { } offer || IsWorking) return false;
        if (_gameRunning()) { Fail(FailedGame, "CS2 çalışıyor."); return false; }

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _cts = cts;
        Progress = 0;
        FailureText = "";
        Set(UpdateState.Downloading);
        string? path = null;
        var started = false;
        var gameStarted = false;
        var lastGameLook = Environment.TickCount64;

        // a transfer that the user started must not go on while a match is being played: the game is looked at once a second (a snapshot of the process
        // list, no handle) for as long as the download runs
        void OnProgress(double p)
        {
            Progress = p;
            Changed?.Invoke();
            if (Environment.TickCount64 - lastGameLook < 1000) return;
            lastGameLook = Environment.TickCount64;
            if (_gameRunning()) { gameStarted = true; cts.Cancel(); }
        }

        try
        {
            path = await _client.DownloadAsync(offer, new InlineProgress(OnProgress), cts.Token);
            cts.Token.ThrowIfCancellationRequested();

            // the file is checked again right before it is started: nothing may have changed it on disk in between
            if (!await Task.Run(() => UpdateClient.FileMatches(path, offer), CancellationToken.None))
                throw new UpdateException("Dosya indirildikten sonra değişmiş.", integrity: true);
            cts.Token.ThrowIfCancellationRequested();                     // "Vazgeç" pressed while the file was being checked still counts
            if (_gameRunning()) { Fail(FailedGame, "CS2 çalışıyor."); return false; }

            try
            {
                UpdateClient.WriteUpdateNote(path);
                Set(UpdateState.Installing);
                _sys.RestartProcess(path);                               // the hand-off to the Windows shell can fail like any other start
            }
            catch (Exception ex) when (ex is not OperationCanceledException) { throw new InstallerDidNotStartException(ex); }

            // the shell hand-off returns before the installer exists: the installer deletes the note as soon as it has decided to install, and only then
            // may the app close
            var note = path + ".update";
            var waited = System.Diagnostics.Stopwatch.StartNew();
            while (File.Exists(note) && waited.Elapsed < StartTimeout)
                await Task.Delay(100, CancellationToken.None);
            if (File.Exists(note)) throw new InstallerDidNotStartException();
            started = true;
            return true;
        }
        catch (OperationCanceledException) when (gameStarted)
        {
            Fail(FailedGame, "CS2 indirme sırasında başladı.");
            return false;
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested)
        {
            Set(UpdateState.Available);                                   // the user pressed Vazgeç
            return false;
        }
        catch (OperationCanceledException ex)
        {
            Fail(FailedDownload, "Zaman aşımı: " + ex.Message);          // the client's own time limit
            return false;
        }
        catch (UpdateException ex)
        {
            Fail(ex.Integrity ? FailedIntegrity : FailedDownload, ex.Message);
            return false;
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or UnauthorizedAccessException)
        {
            Fail(FailedDownload, ex.ToString());                          // a dropped connection, a full disk, a virus scanner that holds the file
            return false;
        }
        catch (Exception ex)
        {
            Fail(FailedStart, ex.ToString());
            return false;
        }
        finally
        {
            _cts = null;
            if (!started && path != null) UpdateClient.Discard(path);      // a download that will not be used is not left lying around
        }
    }

    private void Fail(string text, string detail)
    {
        _log.Detail("Güncelleme yapılamadı: " + detail);
        FailureText = text;
        Set(UpdateState.Failed);
    }

    private void Set(UpdateState state)
    {
        State = state;
        Changed?.Invoke();
    }

    private sealed class InlineProgress : IProgress<double>
    {
        private readonly Action<double> _report;
        public InlineProgress(Action<double> report) => _report = report;
        public void Report(double value) => _report(value);
    }
}
