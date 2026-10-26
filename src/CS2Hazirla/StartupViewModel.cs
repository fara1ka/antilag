using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Threading;
using CS2Hazirla.Core;

namespace CS2Hazirla;

/// <summary>One program of the Açılış page: its name, where it starts from, its switch, and the one line that says what the last click did.</summary>
public sealed class StartupRowVM : ObservableObject
{
    private readonly StartupPageVM _owner;
    private bool _on;
    private bool _quiet;
    private bool _busy;
    private string _note = "";

    public StartupRowVM(StartupPageVM owner, StartupEntry entry)
    {
        _owner = owner;
        Entry = entry;
        _on = entry.Enabled;
    }

    /// <summary>What Windows said the last time it was asked.</summary>
    public StartupEntry Entry { get; private set; }
    public string Title => Entry.DisplayName;
    /// <summary>The program file when it is known, else the command Windows runs: so the person can tell two programs of the same name apart.</summary>
    public string Where => Entry.ExePath ?? Entry.Command;
    public bool Locked => Entry.Locked;
    public string LockedReason => Entry.LockedReason;
    /// <summary>A locked row never changes. (A row whose change is being written stays enabled, so the keyboard focus is not lost; a click on it meanwhile is simply put back.)</summary>
    public bool CanSwitch => !Entry.Locked;
    /// <summary>Who the change is for, when it is not just this account: a machine-wide entry switches for everyone who uses this PC.</summary>
    public string Scope => Entry.Source is StartupSource.MachineRun or StartupSource.MachineRun32 or StartupSource.CommonFolder ? StartupSummary.SourceNote(Entry.Source) : "";
    public bool HasScope => Scope.Length > 0;
    /// <summary>What a screen reader says besides the name: the reason a row is locked, else the last result.</summary>
    public string Help => Entry.Locked ? Entry.LockedReason : _note;

    public bool Busy { get => _busy; internal set => Set(ref _busy, value); }

    /// <summary>The result of the last click on THIS row ("... won't start the next time Windows starts"): shown under the row, where the eyes already are.</summary>
    public string Note { get => _note; internal set { if (Set(ref _note, value)) { Raise(nameof(HasNote)); Raise(nameof(Help)); } } }
    public bool HasNote => _note.Length > 0;

    public bool IsOn
    {
        get => _on;
        set
        {
            if (_quiet || _on == value) return;
            if (_busy) { _owner.PutBack(this); return; }          // the last click is still being written: this one is not taken
            _on = value;
            Raise();
            _owner.Switch(this, value);
        }
    }

    /// <summary>Takes over what Windows says now (also puts the switch back where it really is).</summary>
    internal void Show(StartupEntry entry)
    {
        Entry = entry;
        _quiet = true;
        _on = entry.Enabled;
        Raise(nameof(IsOn));
        _quiet = false;
        Raise(nameof(Title)); Raise(nameof(Where)); Raise(nameof(Locked)); Raise(nameof(LockedReason)); Raise(nameof(CanSwitch)); Raise(nameof(Help));
        Raise(nameof(Scope)); Raise(nameof(HasScope));
    }
}

/// <summary>
/// The Açılış page: the programs that start with Windows, each with a switch. The switch only moves Windows' own "start-up apps" flag (the one of
/// Task Manager); nothing is deleted. Nothing is read or changed while CS2 runs, and the list is not read while a run, a preview or an update is working.
/// </summary>
public sealed class StartupPageVM : ObservableObject
{
    private readonly Dispatcher _ui;
    private readonly IStartupStore _store;
    private readonly Func<bool> _gameRunning;
    private readonly Func<bool> _otherWork;
    private int _run;
    private bool _gameBlocked, _loading, _failed, _read;
    private string _summary = "";

    /// <param name="otherWork">A run, a preview or an update is working: reading the list now would disturb its measurements, so the list waits (the main window asks again when the work ends).</param>
    public StartupPageVM(Dispatcher ui, IStartupStore store, Func<bool> gameRunning, Func<bool> otherWork)
    {
        _ui = ui;
        _store = store;
        _gameRunning = gameRunning;
        _otherWork = otherWork;
        OpenWindowsPageCommand = new RelayCommand(() => { if (!_gameRunning()) UpdateVM.ShellOpen("ms-settings:startupapps"); });
        Rows.CollectionChanged += (_, _) => { Raise(nameof(HasRows)); Raise(nameof(ShowEmpty)); };
    }

    public ObservableCollection<StartupRowVM> Rows { get; } = new();
    public RelayCommand OpenWindowsPageCommand { get; }

    public bool HasRows => Rows.Count > 0;
    /// <summary>One reading is over and found nothing (and the reason is not the game or an unreadable list): until then the page must not claim there is nothing.</summary>
    public bool ShowEmpty => _read && !_gameBlocked && !_loading && !_failed && Rows.Count == 0;

    /// <summary>CS2 runs: the list is not read and not changed (the page says so).</summary>
    public bool GameBlocked { get => _gameBlocked; private set { if (Set(ref _gameBlocked, value)) { Raise(nameof(ShowEmpty)); Raise(nameof(ShowWaiting)); } } }
    public bool Failed { get => _failed; private set { if (Set(ref _failed, value)) Raise(nameof(ShowEmpty)); } }
    public string Summary { get => _summary; private set => Set(ref _summary, value); }

    /// <summary>The list cannot be read yet because a run, a preview or an update is working: the page says so instead of staying blank.</summary>
    public bool ShowWaiting => !_read && !_loading && !_gameBlocked && _otherWork();

    /// <summary>The main window tells that its own work started or ended (the line follows).</summary>
    public void OtherWorkChanged() => Raise(nameof(ShowWaiting));

    private void SetLoading(bool on)
    {
        if (_loading == on) return;
        _loading = on;
        Raise(nameof(ShowEmpty)); Raise(nameof(ShowWaiting));
    }

    /// <summary>Reads the list again (when the page opens, and when the window is used again: Task Manager may have changed it meanwhile).</summary>
    public async Task RefreshAsync(bool clearNotes = false)
    {
        if (_gameRunning()) { GameBlocked = true; return; }       // while the game runs this app reads nothing
        GameBlocked = false;
        if (_otherWork()) { Raise(nameof(ShowWaiting)); return; }   // the main window asks again when the work ends
        if (Rows.Any(r => r.Busy)) return;                          // a change is being written: its own reading follows
        if (clearNotes) foreach (var r in Rows) r.Note = "";
        var run = ++_run;
        SetLoading(true);
        IReadOnlyList<StartupEntry>? list = null;
        var failed = false;
        try { list = await Task.Run(() => _store.List()); }
        catch { failed = true; }
        if (run != _run) return;                                    // a newer reading is on its way
        SetLoading(false);
        // the game may have started while the list was being read: that answer is not trusted (an empty list also means "the game is up"); the rows stay as they were
        if (_gameRunning()) { GameBlocked = true; Raise(nameof(ShowEmpty)); return; }
        ApplyList(list, failed);
    }

    /// <summary>Shows what Windows says. The same programs in the same order are only updated in place (the list does not jump back to its top).</summary>
    private void ApplyList(IReadOnlyList<StartupEntry>? list, bool failed)
    {
        var wasRead = _read;
        _read = true;
        if (!wasRead) Raise(nameof(ShowWaiting));
        if (list != null && list.Count == Rows.Count && list.Select(e => e.Id).SequenceEqual(Rows.Select(r => r.Entry.Id)))
        {
            for (var i = 0; i < list.Count; i++)
            {
                if (Equals(Rows[i].Entry, list[i])) continue;
                // the state changed from outside (Task Manager, Windows Settings, the other hive): what the row's last note said is no longer true
                if (Rows[i].Entry.Enabled != list[i].Enabled || Rows[i].Entry.Locked != list[i].Locked) Rows[i].Note = "";
                Rows[i].Show(list[i]);
            }
        }
        else
        {
            Rows.Clear();
            if (list != null)
                foreach (var entry in list) Rows.Add(new StartupRowVM(this, entry));
        }
        Failed = failed;
        Raise(nameof(ShowEmpty));
        RaiseSummary();
    }

    /// <summary>After a switch the list is read again quietly and the rows follow (what Windows says is what the page shows).</summary>
    private async Task ReloadAsync()
    {
        if (_gameRunning() || _otherWork()) return;
        var run = ++_run;
        IReadOnlyList<StartupEntry> list;
        try { list = await Task.Run(() => _store.List()); }
        catch { return; }
        if (run != _run) return;
        SetLoading(false);
        if (_gameRunning()) { GameBlocked = true; return; }
        ApplyList(list, false);
    }

#if DEBUG
    /// <summary>Developer screenshots only: a sample list (a screenshot run never looks at the computer).</summary>
    public void Demo(IReadOnlyList<StartupEntry> entries, string noteOfFirstOff)
    {
        SetLoading(false);
        ApplyList(entries, false);
        var row = Rows.FirstOrDefault(r => !r.Entry.Enabled);
        if (row != null) row.Note = noteOfFirstOff;
    }
#endif

    private void RaiseSummary() => Summary = Rows.Count == 0 ? "" : StartupSummary.Line(Rows.Select(r => r.Entry).ToList());

    /// <summary>A switch was moved: Windows' flag is written (off the window's thread) and read back; the switch then shows what Windows says.</summary>
    internal void Switch(StartupRowVM row, bool on)
    {
        if (_gameRunning())
        {
            GameBlocked = true;
            row.Note = Loc.T("CS2 çalışıyor: oyun açıkken Antilag hiçbir şeyi değiştirmez.");
            PutBack(row);                                           // this click is still being handled: the switch goes back right after it
            return;
        }
        _ = SwitchAsync(row, on);
    }

    private async Task SwitchAsync(StartupRowVM row, bool on)
    {
        foreach (var other in Rows) if (!ReferenceEquals(other, row)) other.Note = "";
        row.Busy = true;                                            // until the answer is in, the row cannot be clicked again
        var entry = row.Entry;
        StartupEntry? back = null;
        string? error = null;
        try { back = await Task.Run(() => _store.SetEnabled(entry, on)); }
        catch (StartupException ex) { error = ex.Message; }
        catch (Exception) { error = Loc.T("Değiştirilemedi; anahtar eski haline döndü."); }
        row.Busy = false;

        if (back == null)
        {
            row.Note = error ?? "";
            row.Show(row.Entry);                                    // the old state
        }
        else
        {
            row.Show(back);                                         // what Windows says now
            if (back.Enabled != on)
                row.Note = Loc.T("Windows bu değişikliği kabul etmedi; anahtar eski haline döndü.");
            else
            {
                row.Note = on
                    ? Loc.T("Bir sonraki Windows açılışında yine kendiliğinden başlayacak.")
                    : Loc.T("Bir sonraki Windows açılışında başlamayacak (şu an açıksa kapanmaz).");
                _ = ReloadAsync();
            }
        }
        RaiseSummary();
    }

    /// <summary>The switch the person just clicked already moved on screen: it goes back after this click has been handled.</summary>
    internal void PutBack(StartupRowVM row) => _ui.BeginInvoke(() => row.Show(row.Entry));
}
