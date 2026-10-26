namespace CS2Hazirla.Core;

public class PrepareResult
{
    /// <summary>cs2.exe appeared while the preparation was running: everything after that point was skipped.</summary>
    public bool AbortedBecauseGameStarted { get; set; }
    public MetricsSnapshot? Before { get; set; }
    public MetricsSnapshot? After { get; set; }
    public List<string> Done { get; } = new();
    public List<string> Failed { get; } = new();
    /// <summary>Harmless skips (already gone, not running...).</summary>
    public List<string> Skipped { get; } = new();
    /// <summary>Programs (readable names) that are STILL RUNNING after the close request, the forced end and a last look: the user must hear about them.</summary>
    public List<string> NotClosed { get; } = new();
}

public class RevertResult
{
    public List<string> Restored { get; } = new();
    public List<string> Problems { get; } = new();
    /// <summary>Apps (readable names) that were closed but are NOT reopened automatically (browsers, Store apps, windowed apps...): the user opens them.</summary>
    public List<string> NotRestarted { get; } = new();
    /// <summary>Restart records kept for a following preparation (revert was called with restartApps:false).</summary>
    public List<ProcessRecord> Carry { get; } = new();
    public bool Complete => Problems.Count == 0;
}

/// <summary>
/// Applies a plan with a write-ahead journal and reverts it. Every mutation is recorded in
/// state.json BEFORE it happens, so a crash/kill at any point is recoverable.
/// </summary>
public class Orchestrator
{
    private readonly ISystem _sys;
    private readonly SessionStore _store;
    private readonly ILog _log;

    /// <summary>Pause (ms) before the last look at the closed programs, so one that starts itself again has time to show up. Tests set 0.</summary>
    public int SettleMs { get; set; } = 1000;

    /// <summary>Called with (finished steps, planned steps) as the run advances. The home screen's ring shows exactly this number.
    /// Closing programs is one batch (they share one grace period), so that part advances in one jump.</summary>
    public Action<int, int>? Progress { get; set; }

    public Orchestrator(ISystem sys, SessionStore store, ILog log)
    {
        _sys = sys; _store = store; _log = log;
    }

    public bool SessionActive => _store.Current != null || _store.HasPendingSession();

    /// <summary>How many closed programs a restore would still reopen (0 when the journal holds only settings, or there is no journal).</summary>
    public int PendingPrograms() => (_store.Current ?? _store.LoadPending())?.Processes.Count(p => !p.Reverted) ?? 0;

    // =================================================================== PREPARE

    /// <param name="carryOver">After a quiet restore (<c>Revert(restartApps:false)</c> and <see cref="ReopenDeferred"/>): the COMPLETE list of programs that
    /// still have to be reopened by the real restore. Null = a plain run.</param>
    public async Task<PrepareResult> PrepareAsync(Plan plan, AppSettings s, CancellationToken ct,
                                                  IEnumerable<ProcessRecord>? carryOver = null)
    {
        // A quiet Geri yükle (Revert with restartApps:false) leaves a journal that holds ONLY the programs still to reopen: this run takes
        // it over, so those programs are on disk at every moment. Any other pending journal still has to be reverted first.
        var carry = carryOver?.ToList();
        var left = carry != null ? _store.Current ?? _store.LoadPending() : null;
        var adopt = left != null && OnlyPrograms(left);
        if (SessionActive && !adopt)
            throw new InvalidOperationException(Loc.T("Önceki oturum henüz geri yüklenmedi. Önce önceki çalıştırmayı geri yükle."));

        // Never operate while the game (or its anti-cheat session) is up: all preparation happens BEFORE the game starts.
        if (_sys.FindPidByName(Catalog.Cs2ProcessName) != null)
            throw new InvalidOperationException(Loc.T("CS2 zaten çalışıyor. Antilag yalnızca oyun açılmadan ÖNCE çalışır; oyunu kapatıp tekrar dene."));

        var result = new PrepareResult();

        // The journal is opened BEFORE the slow "before" measurement: apps closed by an earlier, never-reverted session
        // (carryOver) are on disk from the first moment, so nothing about them is lost if a later step throws or the app crashes.
        var state = adopt ? left! : _store.Begin(s.Profile.ToString());
        if (adopt) { state.Profile = s.Profile.ToString(); state.StartedUtc = DateTime.UtcNow; state.Processes.Clear(); }
        foreach (var r in carry ?? new List<ProcessRecord>())
            state.Processes.Add(new ProcessRecord { Name = r.Name, Path = r.Path, CanReopen = r.CanReopen, RestartOnRevert = r.RestartOnRevert, ClosedBy = r.ClosedBy });
        _store.Save();
        _log.Info(Loc.T("✓ Eski ayarların yedeği alındı."));

        _log.Info(Loc.T("Önce mevcut durumu ölçüyorum (internet gecikmesi, boş bellek, işlemci). Birkaç saniye sürer…"));
        result.Before = await Metrics.CaptureAsync(_sys, s.PingTarget, ct);

        var actions = plan.Active.OrderBy(a => a.Kind).ToList();

        bool GameUp()
        {
            if (_sys.FindPidByName(Catalog.Cs2ProcessName) == null) return false;
            if (!result.AbortedBecauseGameStarted)      // said once, however many times the check runs afterwards
            {
                result.AbortedBecauseGameStarted = true;
                _log.Error(Loc.T("CS2 açıldığı için işlem durduruldu. Oyun açıkken hiçbir şeye dokunmuyorum."));
            }
            return true;
        }

        void Report() => Progress?.Invoke(
            Math.Min(actions.Count, result.Done.Count + result.Failed.Count + result.Skipped.Count + result.NotClosed.Count), actions.Count);

        // processes are closed as a batch: request close for all → one shared grace period → force-close leftovers (except windowed apps)
        if (!GameUp())
        {
            RunProcessActions(plan, s, state, actions.Where(a => a.Kind == ActionKind.CloseProcess).ToList(), result, GameUp);
            Report();
        }

        foreach (var a in actions.Where(a => a.Kind != ActionKind.CloseProcess))
        {
            ct.ThrowIfCancellationRequested();
            if (result.AbortedBecauseGameStarted || GameUp()) break;   // re-checked before EVERY change, not just at entry
            try
            {
                switch (a.Kind)
                {
                    case ActionKind.StopService: ExecStopService(state, (ServicePayload)a.Payload!, result); break;
                    case ActionKind.PowerPlan: ExecPowerPlan(state, (PowerPayload)a.Payload!, result); break;
                    case ActionKind.Registry: ExecRegistry(state, (RegistryPayload)a.Payload!, a, result); break;
                    case ActionKind.MouseAccel: ExecMouse(state, a, result); break;
                    case ActionKind.Accessibility: ExecAccessibility(state, a, result); break;
                    case ActionKind.NicPower: ExecNic(state, (NicPayload)a.Payload!, result); break;
                    case ActionKind.FlushDns:
                        _sys.FlushDns(); result.Done.Add(a.Title); _log.Info(Loc.T("✓ İnternet adres önbelleği (DNS) temizlendi."));
                        break;
                }
            }
            catch (GameRunningException)
            {
                if (!result.AbortedBecauseGameStarted)
                    _log.Error(Loc.T("CS2 açıldığı için işlem durduruldu. Oyun açıkken hiçbir şeye dokunmuyorum."));
                result.AbortedBecauseGameStarted = true;
                break;
            }
            catch (Exception ex)
            {
                // the raw (usually English) text only goes to the log file
                result.Failed.Add(Loc.F("Şu adım yapılamadı: {0}", a.Title));
                _log.Error(Loc.F("Şu adım yapılamadı: {0}", a.Title));
                _log.Detail($"{a.Title}: {ex}");
            }
            Report();
        }

        // nothing was actually changed → there is nothing to revert, so no journal is left behind
        if (JournalIsEmpty(state)) _store.Clear();

        if (result.AbortedBecauseGameStarted) return result;   // no after-measurement: nothing more is touched

        _log.Info(Loc.T("Sonucu ölçüyorum…"));
        result.After = await Metrics.CaptureAsync(_sys, s.PingTarget, ct);
        return result;
    }

    /// <summary>Nothing but closed programs is recorded (what a quiet restore leaves behind, or what the memory list's close writes).</summary>
    private static bool OnlyPrograms(SessionState st) =>
        st.Services.Count == 0 && st.Registry.Count == 0 && st.NicPower.Count == 0 &&
        st.Mouse == null && st.Accessibility == null && st.OriginalPowerGuid == null && st.CreatedPowerGuid == null;

    private static bool JournalIsEmpty(SessionState st) => OnlyPrograms(st) && st.Processes.Count == 0;

    // ---- processes ----

    private void RunProcessActions(Plan plan, AppSettings s, SessionState state, List<PlannedAction> acts, PrepareResult result,
                                   Func<bool> gameUp)
    {
        if (acts.Count == 0) return;
        var targets = new List<(PlannedAction Act, ProcessPayload P, List<ProcRef> Procs)>();
        var session = _sys.CurrentSessionId;
        var live = _sys.GetProcesses();

        foreach (var a in acts)
        {
            var p = (ProcessPayload)a.Payload!;
            // fresh pid list + last-chance protection check (defense in depth). Identity = pid + real name.
            var pids = live.Where(x => x.SessionId == session && x.Pid != _sys.CurrentProcessId
                                       && ProtectedList.Normalize(x.Name) == p.Name
                                       && !ProtectedList.IsProcessProtected(x.Name, x.Path, plan.StreamMode))
                           .Select(x => new ProcRef(x.Pid, x.Name)).ToList();
            if (pids.Count == 0)
            {
                result.Skipped.Add(Loc.F("{0}: artık çalışmıyor", p.Display ?? p.Name));
                continue;
            }

            // no question is asked: the user chose what may be closed, and "Geri yükle" opens everything again
            state.Processes.Add(new ProcessRecord { Name = p.Name, Path = p.Path, CanReopen = p.Restart, ClosedBy = p.OptionId });
            targets.Add((a, p, pids));
            if (gameUp()) return;
        }

        if (targets.Count == 0) return;
        _store.Save();   // journal first

        // the journal was just written to disk (slow): check the game again right before touching anything
        if (gameUp()) return;

        var shownByName = new Dictionary<string, string>();
        foreach (var t in targets) shownByName.TryAdd(t.P.Name, t.P.Display ?? t.P.Name);
        string Shown(ProcRef r) => shownByName.TryGetValue(ProtectedList.Normalize(r.Name), out var d) ? d : r.Name;

        try
        {
            foreach (var t in targets)
                foreach (var proc in t.Procs)
                {
                    try { _sys.RequestClose(proc); }
                    catch (GameRunningException) { throw; }
                    catch { /* already gone, or protected: skipped */ }
                }

            _log.Info(Loc.F("{0} program kapatılıyor… (önce nazikçe; kapanmayanlar en geç 6 saniye sonra zorla kapatılır)", targets.Count));
            var all = targets.SelectMany(t => t.Procs).ToList();
            var remaining = _sys.WaitForExit(all, 6000);
            var remainingSet = remaining.ToHashSet();

            // everything that is still there after the grace period is closed by force. A program that "closes" to the tray
            // (WhatsApp, IDM, Claude...) never exits on a polite request, and a "save changes?" prompt gets the same 6 seconds.
            // WindowsSystem.Kill re-verifies pid+name and the protected list right before terminating.
            var toKill = remaining.ToList();
            var killAccepted = new HashSet<ProcRef>();
            foreach (var proc in toKill) if (TryKill(proc, Shown(proc))) killAccepted.Add(proc);
            var lingering = toKill.Count > 0 ? _sys.WaitForExit(toKill, 2500) : new List<ProcRef>();

            // A process that was ended but is still listed is usually still being torn down (a browser or overlay with a lot of GPU memory can take
            // several seconds) or ignored the first request: ask once more and give it a real chance before it is reported as "not closed".
            var askAgain = lingering.Where(killAccepted.Contains).ToList();
            if (askAgain.Count > 0)
            {
                foreach (var proc in askAgain) TryKill(proc, Shown(proc));
                // only the copies whose end was ACCEPTED are waited for: one that was refused can never go away and must not hold up the rest
                var stillThere = _sys.WaitForExit(askAgain, 6000);
                lingering = lingering.Where(p => !killAccepted.Contains(p)).Concat(stillThere).ToList();
            }
            var stillAfterKill = lingering.ToHashSet();

            // programs that start themselves again at once (a tray watchdog, a helper that relaunches its main app) get two more sweeps
            var targetNames = targets.Select(t => t.P.Name).ToHashSet();
            var seenPids = all.ToHashSet();
            List<ProcRef> TargetsRunning() => _sys.GetProcesses()
                .Where(x => x.SessionId == session && x.Pid != _sys.CurrentProcessId
                            && targetNames.Contains(ProtectedList.Normalize(x.Name))
                            && !ProtectedList.IsProcessProtected(x.Name, x.Path, plan.StreamMode))
                .Select(x => new ProcRef(x.Pid, x.Name)).ToList();

            // The sweeps go on while programs keep coming back (a service or a helper that relaunches the program a few seconds later, like the NVIDIA app
            // or an updater): every sweep that finds a new copy ends it again, and the run is finished only after two quiet looks in a row. Bounded three
            // ways, so that a program that can never be kept closed costs a few seconds and not a hang: at most ten sweeps, at most 15 seconds in all, and a
            // program that has come back three times is left alone (it is then reported as "tekrar açıldı").
            var quiet = 0;
            var sweepClock = System.Diagnostics.Stopwatch.StartNew();
            var comebacks = new Dictionary<string, int>();
            for (var pass = 0; pass < 10 && quiet < 2 && sweepClock.Elapsed < TimeSpan.FromSeconds(15); pass++)
            {
                if (gameUp()) throw new GameRunningException();          // every sweep looks at the game first, also the quiet ones
                if (pass > 0 && SettleMs > 0) Thread.Sleep(SettleMs);
                var respawned = TargetsRunning().Where(r => !seenPids.Contains(r)).ToList();
                foreach (var r in respawned) seenPids.Add(r);
                respawned = respawned.Where(r =>
                {
                    var name = ProtectedList.Normalize(r.Name);
                    comebacks[name] = comebacks.GetValueOrDefault(name) + 1;
                    return comebacks[name] <= 3;
                }).ToList();
                if (respawned.Count == 0) { quiet++; continue; }
                quiet = 0;
                foreach (var r in respawned)
                {
                    try { _sys.RequestClose(r); }
                    catch (GameRunningException) { throw; }
                    catch { /* protected or gone */ }
                }
                var killedAll = true;
                foreach (var r in _sys.WaitForExit(respawned, 1000)) killedAll &= TryKill(r, Shown(r));
                var leftAfterKill = _sys.WaitForExit(respawned, 1500);
                if (killedAll && leftAfterKill.Count == 0)
                    _log.Info(Loc.F("Kendini yeniden açan {0} program tekrar kapatıldı.", respawned.Select(r => ProtectedList.Normalize(r.Name)).Distinct().Count()));
            }

            // the last look: a program that comes back a moment after the sweeps (G HUB, Wallpaper Engine...) must not be reported as closed
            if (SettleMs > 0) Thread.Sleep(SettleMs);
            var back = TargetsRunning().Select(r => ProtectedList.Normalize(r.Name)).ToHashSet();

            foreach (var t in targets)
            {
                var shown = t.P.Display ?? t.P.Name;
                var open = t.Procs.Count(p => stillAfterKill.Contains(p));
                var killed = t.Procs.Count(p => remainingSet.Contains(p) && !stillAfterKill.Contains(p));
                if (open > 0)
                {
                    result.NotClosed.Add(shown);
                    var refused = t.Procs.Any(p => stillAfterKill.Contains(p) && !killAccepted.Contains(p));
                    _log.Warn(refused ? Loc.F("{0} kapanmadı (Windows kapatmaya izin vermedi).", shown)
                                      : Loc.F("{0} kapanmadı (kapatma isteği kabul edildi ama program hâlâ çalışıyor).", shown));
                    // for the log file only: which copies, and whether the forced ending was refused or accepted but did not take effect
                    foreach (var p in t.Procs.Where(p => stillAfterKill.Contains(p)))
                        _log.Detail($"Kapanmayan işlem: {p.Name} ({p.Pid}); zorla kapatma {(killAccepted.Contains(p) ? "kabul edildi ama işlem yaşamaya devam etti" : "reddedildi")}.");
                }
                else if (back.Contains(t.P.Name))
                {
                    result.NotClosed.Add(shown);
                    _log.Warn(Loc.F("{0} tekrar açıldı, kapatılamadı.", shown));
                    _log.Detail($"{t.P.Name}: kapatıldıktan sonra kendiliğinden yeniden açıldı (bir hizmet ya da yardımcı program açıyor olabilir).");
                }
                else
                {
                    result.Done.Add(Loc.F("{0} kapatıldı", shown));
                    _log.Info(killed > 0 ? Loc.F("✓ {0} kapatıldı (kapanmadığı için zorla).", shown) : Loc.F("✓ {0} kapatıldı.", shown));
                }
            }
        }
        catch (GameRunningException)
        {
            gameUp();   // marks the result as aborted and logs it
        }
        gameUp();       // final check after the whole batch
    }

    /// <summary>Ends one process by force. False when Windows refused (the raw reason goes to the log file); a running game still stops everything.</summary>
    private bool TryKill(ProcRef proc, string shown)
    {
        try { _sys.Kill(proc); return true; }
        catch (GameRunningException) { throw; }
        catch (Exception ex)
        {
            _log.Warn(Loc.F("{0} zorla kapatılamadı.", shown));
            _log.Detail($"{proc.Name} ({proc.Pid}): {ex}");
            return false;
        }
    }

    // ---- services ----

    private void ExecStopService(SessionState state, ServicePayload p, PrepareResult result)
    {
        // (Catalog.ServiceTitle already gives the title in the chosen language)
        if (ProtectedList.IsServiceProtected(p.Name)) { result.Skipped.Add(Loc.F("{0}: korumalı hizmet", Catalog.ServiceTitle(p.Name))); return; }
        var info = _sys.GetService(p.Name);
        if (info == null || info.State != ServiceRunState.Running) { result.Skipped.Add(Loc.F("{0}: çalışmıyor", Catalog.ServiceTitle(p.Name))); return; }

        // record the service and every running dependent (they get stopped with it)
        state.Services.Add(new ServiceRecord { Name = info.Name, WasRunning = true, StartType = info.StartType });
        foreach (var dep in info.RunningDependents)
        {
            if (ProtectedList.IsServiceProtected(dep))
                throw new InvalidOperationException($"Bağımlı servis '{dep}' korumalı olduğu için '{p.Name}' durdurulmadı.");
            var d = _sys.GetService(dep);
            state.Services.Add(new ServiceRecord { Name = dep, WasRunning = true, StartType = d?.StartType ?? ServiceStart.Unknown });
        }
        _store.Save();

        _sys.StopService(p.Name);
        result.Done.Add(Loc.F("Hizmet durduruldu: {0}", Catalog.ServiceTitle(p.Name, info.DisplayName)));
        _log.Info(Loc.F("✓ Windows hizmeti durduruldu: {0}.", Catalog.ServiceTitle(p.Name, info.DisplayName)));
    }

    // ---- power plan ----

    private void ExecPowerPlan(SessionState state, PowerPayload p, PrepareResult result)
    {
        var current = _sys.GetPower();
        state.OriginalPowerGuid = current.ActiveGuid;
        _store.Save();

        var target = p.ExistingUltimate;
        if (target == null)
        {
            try
            {
                target = _sys.DuplicatePowerScheme(Catalog.UltimateTemplateGuid);
                state.CreatedPowerGuid = target; _store.Save();
                _log.Info(Loc.T("Nihai Performans güç planı hazırlandı."));
            }
            catch (Exception ex)
            {
                _log.Warn(Loc.T("Nihai Performans güç planı hazırlanamadı; Yüksek Performans denenecek."));
                _log.Detail(ex.ToString());
            }
        }
        if (target == null) target = p.ExistingHighPerf;
        if (target == null)
        {
            try
            {
                target = _sys.DuplicatePowerScheme(Catalog.HighPerfGuid);
                state.CreatedPowerGuid = target; _store.Save();
            }
            catch (Exception ex)
            {
                _log.Warn(Loc.T("Yüksek Performans güç planı da hazırlanamadı."));
                _log.Detail(ex.ToString());
            }
        }
        if (target == null)
        {
            result.Failed.Add(Loc.T("Şu adım yapılamadı: Güç planı (uygun plan bulunamadı)"));
            _log.Error(Loc.T("Şu adım yapılamadı: Güç planı (uygun plan bulunamadı)"));
            return;
        }

        _sys.SetActivePowerScheme(target);
        result.Done.Add(Loc.T("Güç planı en yüksek performansa alındı"));
        _log.Info(Loc.F("✓ Güç planı en yüksek performansa alındı (eski mod: {0}). Geri yüklenince eski haline döner.",
            current.Schemes.FirstOrDefault(x => x.Guid == current.ActiveGuid)?.Name ?? Loc.T("bilinmiyor")));
    }

    // ---- registry ----

    private void ExecRegistry(SessionState state, RegistryPayload p, PlannedAction a, PrepareResult result)
    {
        foreach (var c in p.Changes)
        {
            var already = state.Registry.Any(r => r.Hive == c.Hive && r.SubKey == c.SubKey &&
                                                   string.Equals(r.Name, c.Name, StringComparison.OrdinalIgnoreCase));
            var old = _sys.ReadRegistry(c.Hive, c.SubKey, c.Name);
            if (!already)
            {
                state.Registry.Add(new RegistryRecord
                {
                    Hive = c.Hive, SubKey = c.SubKey, Name = c.Name,
                    KeyExisted = _sys.RegistryKeyExists(c.Hive, c.SubKey),
                    ValueExisted = old != null, OldKind = old?.Kind ?? c.Kind, OldData = old?.Data,
                });
                _store.Save();
            }
            _sys.WriteRegistry(c.Hive, c.SubKey, c.Name, c.Kind, RegCodec.Encode(c.Kind, c.Value));
        }
        result.Done.Add(a.Title);
        _log.Info(Loc.F("✓ {0}.", a.Title));
    }

    // ---- mouse / nic ----

    private void ExecMouse(SessionState state, PlannedAction a, PrepareResult result)
    {
        // no question: the user switched "Fare ivmesini kapat" on, and 'Geri yükle' brings the old value back
        var cur = _sys.GetMouse();
        if (!cur.AccelerationOn) return;
        state.Mouse = new MouseRecord { Threshold1 = cur.Threshold1, Threshold2 = cur.Threshold2, Accel = cur.Accel };
        _store.Save();
        _sys.SetMouse(new MouseParams(0, 0, 0));
        result.Done.Add(a.Title);
        _log.Info(Loc.T("✓ Fare ivmesi bu oturum için kapatıldı."));
    }

    private void ExecAccessibility(SessionState state, PlannedAction a, PrepareResult result)
    {
        var cur = _sys.GetAccessibility();
        if (!cur.AnyHotkeyActive) return;
        state.Accessibility = new AccessibilityRecord { Sticky = cur.Sticky, Toggle = cur.Toggle, Filter = cur.Filter };
        _store.Save();
        _sys.SetAccessibility(cur.WithoutHotkeys());
        result.Done.Add(a.Title);
        _log.Info(Loc.T("✓ Yapışkan Tuşlar penceresi bu oturum için engellendi."));
    }

    private void ExecNic(SessionState state, NicPayload p, PrepareResult result)
    {
        var inst = p.Nic.PowerInstanceName!;
        state.NicPower.Add(new NicPowerRecord { InstanceName = inst, OldEnabled = true });
        _store.Save();
        _sys.SetNicPower(inst, false);
        result.Done.Add(Loc.F("Ağ kartının enerji tasarrufu kapatıldı: {0}", p.Nic.Name));
        _log.Info(Loc.F("✓ Ağ kartının ({0}) enerji tasarrufu kapatıldı.", p.Nic.Name));
    }

    // =================================================================== REVERT

    /// <param name="restartApps">false when a new preparation follows right away (no point reopening apps that are about to be closed again).</param>
    /// <param name="forgetPrograms">A new Başlat undoes the settings and services of the previous run but does NOT bring its programs back and does not keep
    /// them for later: only the "Geri yükle" button reopens programs, and only those of the latest run. Their journal entries are dropped.</param>
    public RevertResult Revert(AppSettings s, bool restartApps = true, bool forgetPrograms = false)
    {
        var res = new RevertResult();
        var state = _store.Current ?? _store.LoadPending();
        if (state == null && _store.NewerFormat)
        {
            res.Problems.Add(Loc.T("Önceki çalıştırmanın kaydı daha yeni bir Antilag sürümüyle yazılmış; bu sürüm onu geri yükleyemez. Antilag'ı güncelleyip yeniden dene."));
            _log.Warn(res.Problems[0]);
            return res;   // state.json is kept untouched
        }
        if (state == null) { _log.Info(Loc.T("Geri yüklenecek bir şey yok.")); return res; }

        if (_sys.FindPidByName(Catalog.Cs2ProcessName) != null)
        {
            res.Problems.Add(Loc.T("CS2 çalışıyor: oyun açıkken geri yükleme çalışmaz (anti-cheat güvenliği). Oyunu kapattıktan sonra tekrar dene."));
            _log.Warn(res.Problems[0]);
            return res;   // state.json is kept untouched
        }

        _log.Info(forgetPrograms ? Loc.T("Önceki çalıştırmanın ayarları geri yükleniyor…")
            : restartApps
            ? Loc.T("Geri yüklüyorum: ayarlar eski haline dönüyor, kapatılan programlar yeniden başlatılıyor…")
            : Loc.T("Önceki ayarlar geri yükleniyor (programlar şimdilik açılmıyor: Antilag onları zaten tekrar kapatacak)…"));

        // mouse
        if (state.Mouse is { Reverted: false } m)
            Try(res, Loc.T("Fare ivmesi"), () =>
            {
                _sys.SetMouse(new MouseParams(m.Threshold1, m.Threshold2, m.Accel));
                var now = _sys.GetMouse();
                if (now.Accel != m.Accel) throw new InvalidOperationException("doğrulama başarısız");
                m.Reverted = true; res.Restored.Add(Loc.T("Fare ivmesi eski haline döndü"));
            });

        // sticky / toggle / filter keys
        if (state.Accessibility is { Reverted: false } acc)
            Try(res, Loc.T("Yapışkan/Filtre tuşları"), () =>
            {
                _sys.SetAccessibility(new AccessibilityFlags(acc.Sticky, acc.Toggle, acc.Filter));
                acc.Reverted = true; res.Restored.Add(Loc.T("Yapışkan/Filtre/Geçiş tuşları eski haline döndü"));
            });

        // nic
        foreach (var n in state.NicPower.Where(x => !x.Reverted))
            Try(res, Loc.T("Ağ kartı güç tasarrufu"), () =>
            {
                _sys.SetNicPower(n.InstanceName, n.OldEnabled); n.Reverted = true;
                res.Restored.Add(Loc.T("Ağ kartı güç tasarrufu eski haline döndü"));
            });

        // registry (reverse order)
        foreach (var r in Enumerable.Reverse(state.Registry).Where(x => !x.Reverted))
        {
            if (!StateGuard.IsAllowed(r))
            {
                // state.json is user-writable but replayed with admin rights: never touch anything this app could not have written
                _log.Error(Loc.T("Kayıt dosyasındaki bir Windows ayarı tanınmadığı için atlandı (güvenlik)."));
                _log.Detail($"İzin listesinde olmayan kayıt yok sayıldı: {r.Hive}\\{r.SubKey}\\{r.Name}");
                r.Reverted = true;
                continue;
            }
            Try(res, Loc.T("Windows ayarı"), () =>
            {
                if (r.ValueExisted)
                {
                    _sys.WriteRegistry(r.Hive, r.SubKey, r.Name, r.OldKind, r.OldData ?? "");
                    var back = _sys.ReadRegistry(r.Hive, r.SubKey, r.Name);
                    if (back == null || back.Kind != r.OldKind || back.Data != (r.OldData ?? ""))
                        throw new InvalidOperationException("geri yazılan değer doğrulanamadı");
                }
                else
                {
                    _sys.DeleteRegistryValue(r.Hive, r.SubKey, r.Name);
                    if (!r.KeyExisted) _sys.DeleteRegistryKeyIfEmpty(r.Hive, r.SubKey);
                }
                r.Reverted = true;
            }, $@"{r.Hive}\{r.SubKey}\{r.Name}");
        }
        if (state.Registry.Count > 0 && state.Registry.All(x => x.Reverted))
            res.Restored.Add(Loc.F("Windows ayarları: {0} değişiklik eski haline döndü", state.Registry.Count));

        // services (original order: a service before its dependents)
        foreach (var sv in state.Services.Where(x => !x.Reverted))
        {
            if (!StateGuard.IsValidServiceName(sv.Name))
            {
                _log.Error(Loc.T("Kayıt dosyasındaki bir hizmet adı geçersiz olduğu için atlandı (güvenlik)."));
                _log.Detail($"Geçersiz hizmet adı yok sayıldı: {sv.Name}");
                sv.Reverted = true; continue;
            }
            Try(res, Loc.F("Hizmet: {0}", Catalog.ServiceTitle(sv.Name)), () =>
            {
                if (sv.WasRunning)
                {
                    var cur = _sys.GetService(sv.Name);
                    if (cur != null && cur.State != ServiceRunState.Running) _sys.StartService(sv.Name);
                    cur = _sys.GetService(sv.Name);
                    if (cur != null && cur.State != ServiceRunState.Running)
                        throw new InvalidOperationException("servis tekrar başlatılamadı");
                    if (cur != null && sv.StartType != ServiceStart.Unknown && cur.StartType != sv.StartType)
                        _log.Detail($"Servis {sv.Name} başlangıç türü beklenenden farklı: {cur.StartType} (kayıt: {sv.StartType}).");
                }
                sv.Reverted = true;
            }, sv.Name);
        }
        if (state.Services.Count > 0 && state.Services.All(x => x.Reverted))
            res.Restored.Add(Loc.F("Arka plan hizmetleri: {0} hizmet eski durumuna döndü", state.Services.Count));

        // power plan (GUIDs from state.json are validated before they reach powercfg)
        if (state.OriginalPowerGuid != null && !StateGuard.IsValidGuid(state.OriginalPowerGuid)) { _log.Error(Loc.T("Kayıt dosyasındaki güç planı kimliği geçersiz, atlandı (güvenlik).")); state.OriginalPowerGuid = null; }
        if (state.CreatedPowerGuid != null && !StateGuard.IsValidGuid(state.CreatedPowerGuid)) { _log.Error(Loc.T("Kayıt dosyasındaki oluşturulan güç planı kimliği geçersiz, atlandı (güvenlik).")); state.CreatedPowerGuid = null; }
        if (!state.PowerReverted && (state.OriginalPowerGuid != null || state.CreatedPowerGuid != null))
            Try(res, Loc.T("Güç planı"), () =>
            {
                if (state.OriginalPowerGuid != null)
                {
                    _sys.SetActivePowerScheme(state.OriginalPowerGuid);
                    var now = _sys.GetPower().ActiveGuid;
                    if (!string.Equals(now, state.OriginalPowerGuid, StringComparison.OrdinalIgnoreCase))
                        throw new InvalidOperationException("eski plan etkinleştirilemedi");
                }
                if (state.CreatedPowerGuid != null)
                {
                    // an earlier restore that was interrupted may already have deleted it: a plan that is gone needs no second delete
                    var created = state.CreatedPowerGuid;
                    if (_sys.GetPower().Schemes.Any(sc => string.Equals(sc.Guid, created, StringComparison.OrdinalIgnoreCase)))
                        _sys.DeletePowerScheme(created);
                }
                state.PowerReverted = true;
                res.Restored.Add(state.CreatedPowerGuid != null ? Loc.T("Güç planı eski haline döndü (oluşturulan plan silindi)") : Loc.T("Güç planı eski haline döndü"));
            });

        // apps closed earlier: "Geri yükle" starts every one that can be started again; the rest is listed for the user
        // Two different programs can share one exe name (Claude Desktop and the Claude Code claude.exe): "already open" means the same
        // name AND the same file. (A live process whose path is unknown counts as the same, which only ever errs on the safe side.)
        var live = forgetPrograms ? new List<(string Name, string? Path)>() : LiveProcesses();      // a forgetting undo opens nothing: no need to look at every process
        var seen = new HashSet<string>();
        // newest record first: a program closed in several unreverted sessions is reopened from the path that was valid last
        // (self-updating apps such as Discord move to a new folder with every update)
        foreach (var pr in state.Processes.Where(x => !x.Reverted).Reverse().ToList())
        {
            if (forgetPrograms) continue;          // a new Başlat: nothing is reopened here; when the undo succeeds the whole journal is dropped below, when it fails the records stay for the restore button
            if (!s.RestartClosedApps) { pr.Reverted = true; continue; }   // reopening switched off (Ayarlar has no such switch any more: only settings built in code, e.g. by a test, can have it off)
            if (!seen.Add(pr.Name)) { pr.Reverted = true; continue; }     // closed in several sessions: one entry is enough
            if (!restartApps)
            {
                // carried into the next session (see below). They are NOT marked reverted here:
                // if this revert ends up incomplete the journal is kept, and the apps must still be reopened by a later revert.
                res.Carry.Add(new ProcessRecord { Name = pr.Name, Path = pr.Path, CanReopen = pr.CanReopen, RestartOnRevert = pr.RestartOnRevert, ClosedBy = pr.ClosedBy });
                continue;
            }
            if (_sys.FindPidByName(Catalog.Cs2ProcessName) != null)
            {
                // the game started while the restore was running: nothing more is started, and what is left stays in the journal
                res.Problems.Add(Loc.T("CS2 çalıştığı için kalan programlar açılmadı. Oyunu kapattıktan sonra geri yüklemeyi tekrar dene."));
                _log.Warn(res.Problems[^1]);
                break;
            }
            pr.Reverted = true;   // best-effort; a failed restart must not block clearing the journal
            if (!ReopenOne(pr, live, res))
            {
                pr.Reverted = false;                                       // this one was not opened: it stays in the journal
                break;
            }
        }

        if (!res.Complete)
        {
            _store.Save();
            _log.Error(Loc.F("Geri yükleme tam yapılamadı ({0} sorun). Yedek korundu, tekrar deneyebilirsin.", res.Problems.Count));
        }
        else if (res.Carry.Count > 0)
        {
            // quiet restore with programs still to reopen: the journal is NOT deleted. It is cut down to those programs (everything else
            // is undone), so they are on disk until the run that follows takes them over - or until Geri yükle reopens them after a crash.
            state.Services.Clear(); state.Registry.Clear(); state.NicPower.Clear();
            state.Mouse = null; state.Accessibility = null;
            state.OriginalPowerGuid = null; state.CreatedPowerGuid = null; state.PowerReverted = false;
            state.Processes.RemoveAll(p => p.Reverted);
            _store.Save();
            _log.Info(Loc.T("Önceki ayarlar eski haline döndü."));
        }
        else
        {
            _store.Clear();
            _log.Info(restartApps ? Loc.T("Ayarlar ve hizmetler eski haline döndü.") : Loc.T("Önceki ayarlar eski haline döndü."));
        }
        LogNotReopened(res);
        return res;
    }

    // ---- reopening closed programs (used by Geri yükle and by a new Başlat) ----

    // Two different programs can share one exe name (Claude Desktop and the Claude Code claude.exe): "already open" means the same
    // name AND the same file. (A live process whose path is unknown counts as the same, which only ever errs on the safe side.)
    private List<(string Name, string? Path)> LiveProcesses() =>
        _sys.GetProcesses().Select(p => (Name: ProtectedList.Normalize(p.Name), p.Path)).ToList();

    private static bool AlreadyOpen(List<(string Name, string? Path)> live, ProcessRecord r) => live.Any(l => l.Name == r.Name &&
        (string.IsNullOrEmpty(l.Path) || string.IsNullOrEmpty(r.Path) || string.Equals(l.Path, r.Path, StringComparison.OrdinalIgnoreCase)));

    /// <summary>
    /// Starts one closed program again, or says why it cannot (<see cref="RevertResult.NotRestarted"/> gets the readable name).
    /// "Başlatıldı" is all that is known: whether the program really came up is not verified.
    /// Returns false only when the game started meanwhile (stop everything).
    /// </summary>
    private bool ReopenOne(ProcessRecord pr, List<(string Name, string? Path)> live, RevertResult res)
    {
        if (AlreadyOpen(live, pr)) return true;                            // open again already (never closed, or the user reopened it)
        var shown = FriendlyNames.ForProcess(pr.Name, pr.Path);
        void ByHand(string entry) { if (!res.NotRestarted.Contains(entry)) res.NotRestarted.Add(entry); }
        if (!string.IsNullOrEmpty(pr.Path) && pr.Path.Contains(@"\WindowsApps\", StringComparison.OrdinalIgnoreCase))
        {
            // Store (MSIX) apps cannot be started by their package path; the user reopens them from the Start menu
            _log.Info(Loc.F("{0} bir Microsoft Store uygulaması: otomatik açılamaz, Başlat menüsünden açabilirsin.", shown));
            ByHand(Loc.F("{0} (Store uygulaması)", shown));
            return true;
        }
        if (!(pr.CanReopen || pr.RestartOnRevert) || string.IsNullOrEmpty(pr.Path)) { ByHand(shown); return true; }
        if (!StateGuard.IsSafeRestartPath(pr.Path)) { _log.Warn(Loc.F("{0}: başlatma yolu güvenli değil ya da dosya yok, atlandı.", shown)); ByHand(shown); return true; }
        try
        {
            _sys.RestartProcess(pr.Path);
            live.Add((pr.Name, pr.Path));
            res.Restored.Add(Loc.F("{0} başlatıldı", shown));
            _log.Info(Loc.F("✓ {0} başlatıldı.", shown));
        }
        catch (GameRunningException)
        {
            res.Problems.Add(Loc.T("CS2 çalıştığı için kalan programlar açılmadı. Oyunu kapattıktan sonra geri yüklemeyi tekrar dene."));
            _log.Warn(res.Problems[^1]);
            return false;
        }
        catch (Exception ex)
        {
            _log.Warn(Loc.F("{0} başlatılamadı.", shown));
            _log.Detail($"{pr.Path}: {ex}");
            ByHand(shown);
        }
        return true;
    }

    private void LogNotReopened(RevertResult res)
    {
        if (res.NotRestarted.Count > 0)
            _log.Warn(Loc.F("Şu programlar kendiliğinden açılamadı, gerekirse kendin aç: {0}.", string.Join(", ", res.NotRestarted)));
    }

    /// <summary>
    /// Will a new Başlat with these settings close this program again? (Decided by the switches, not by what is running right now:
    /// the program is closed at this moment, so a plan built now cannot list it.)
    /// </summary>
    public static bool WillBeClosedAgain(ProcessRecord r, AppSettings s)
    {
        var name = ProtectedList.Normalize(r.Name);
        if (s.NeverClose.Any(n => ProtectedList.Normalize(n) == name)) return false;
        if (ProtectedList.IsProcessProtected(name, r.Path, s.Profile == ProfileKind.Stream)) return false;
        if (s.ExtraProcesses.Any(n => ProtectedList.Normalize(n) == name)) return s.IsOn("proc.extra");
        var cat = Catalog.CategoryOf(name);
        if (cat != null) return s.IsOn(cat.OptionId);
        // an unknown program is only ever closed by a sweep: the one that closed it is the one that matters (the two select different programs)
        if (r.ClosedBy is "proc.trayauto" or "proc.windowed") return s.IsOn(r.ClosedBy);
        return s.IsOn("proc.windowed") || s.IsOn("proc.trayauto");   // older journals did not record which sweep it was
    }

    /// <summary>
    /// A new Başlat first undoes the previous one's settings and DEFERS reopening its programs, because it is going to close them again.
    /// That only holds when the new switches really close them: whatever they would leave alone (another preset or profile was chosen
    /// in between) is reopened now. Returns what stays deferred, to be journaled by the new run.
    /// </summary>
    public RevertResult ReopenDeferred(IReadOnlyList<ProcessRecord> deferred, AppSettings s)
    {
        var res = new RevertResult();
        if (deferred.Count == 0 || !s.RestartClosedApps) return res;
        if (_sys.FindPidByName(Catalog.Cs2ProcessName) != null) { res.Carry.AddRange(deferred); return res; }   // nothing is touched while the game runs
        var live = LiveProcesses();
        for (var i = 0; i < deferred.Count; i++)
        {
            var pr = deferred[i];
            if (WillBeClosedAgain(pr, s)) { res.Carry.Add(pr); continue; }
            if (!ReopenOne(pr, live, res)) { res.Carry.AddRange(deferred.Skip(i)); break; }
        }
        LogNotReopened(res);
        return res;
    }

    /// <summary>
    /// The user chose to give up on a revert that cannot complete: the journal is kept as state.discarded-*.json (for inspection)
    /// and no longer blocks new preparations. A Windows restart fixes most of what a revert would have restored.
    /// </summary>
    public void DiscardJournal()
    {
        _store.Quarantine("discarded");
        _log.Warn(Loc.T("Geri yükleme yok sayıldı: kayıt dosyası (state.json) bir kenara alındı."));
    }

    /// <param name="tech">Technical detail for the log file only (registry path, service name...).</param>
    private void Try(RevertResult res, string what, Action a, string? tech = null)
    {
        try { a(); }
        catch (Exception ex)
        {
            res.Problems.Add(Loc.F("Şu adım yapılamadı: {0}", what));
            _log.Error(Loc.F("Şu adım yapılamadı: {0}", what));
            _log.Detail($"{what}{(tech != null ? $" [{tech}]" : "")}: {ex}");
        }
    }

    // =================================================================== user-initiated close (top RAM list)

    /// <summary>
    /// Closes a non-protected process group chosen by the user (politely, then by force). It is ALWAYS journaled - a journal is started
    /// when there is none - so that "Geri yükle" can open the program again.
    /// </summary>
    public bool CloseByUser(string name, bool streamMode)
    {
        if (_sys.FindPidByName(Catalog.Cs2ProcessName) != null)
        {
            _log.Warn(Loc.T("CS2 çalışıyor: oyun açıkken hiçbir uygulama kapatılmaz."));
            return false;
        }
        var n = ProtectedList.Normalize(name);
        var procs = _sys.GetProcesses().Where(p => p.SessionId == _sys.CurrentSessionId && p.Pid != _sys.CurrentProcessId
                                                   && ProtectedList.Normalize(p.Name) == n).ToList();
        if (procs.Count == 0) return false;
        if (procs.Any(p => ProtectedList.IsProcessProtected(p.Name, p.Path, streamMode)))
        {
            _log.Warn(Loc.F("{0}.exe korumalı: kapatılmadı.", n));
            return false;
        }
        var path = procs.Select(p => p.Path).FirstOrDefault(x => !string.IsNullOrEmpty(x));
        var shown = FriendlyNames.ForProcess(n, path);

        var state = _store.Current ?? _store.LoadPending();
        if (state == null && _store.NewerFormat)
        {
            _log.Warn(Loc.T("Önceki çalıştırmanın kaydı daha yeni bir Antilag sürümüyle yazılmış: kayıt bozulmasın diye şimdi bir program kapatılmadı."));
            return false;
        }
        state ??= _store.Begin("Manuel");
        state.Processes.Add(new ProcessRecord { Name = n, Path = path, CanReopen = ReopenPolicy.CanReopen(n, path, null) });
        _store.Save();   // journal first, like everything else

        var refs = procs.Select(p => new ProcRef(p.Pid, p.Name)).ToList();
        try
        {
            foreach (var r in refs)
            {
                try { _sys.RequestClose(r); }
                catch (GameRunningException) { throw; }
                catch { /* already gone, or protected */ }
            }
            var allEnded = true;
            foreach (var r in _sys.WaitForExit(refs, 5000)) allEnded &= TryKill(r, shown);
            if (!allEnded) return false;
        }
        catch (GameRunningException)
        {
            _log.Warn(Loc.T("CS2 çalışıyor: oyun açıkken hiçbir uygulama kapatılmaz."));
            return false;
        }
        _log.Info(Loc.F("✓ {0} kapatıldı.", shown));
        return true;
    }
}
