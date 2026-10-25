using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CS2Hazirla.Core;

namespace CS2Hazirla;

/// <summary>One choice in the strip on the home page: a ready-made preset (Hafif, Tam temizlik, Yayıncı) or a profile the user saved.</summary>
public class ProfileChipVM : ObservableObject
{
    private readonly Func<bool> _isActive;
    private readonly Action _activate;

    public ProfileChipVM(string name, string tip, bool startsCustom, Func<bool> isActive, Action activate)
    {
        Name = name; Tip = tip; StartsCustom = startsCustom; _isActive = isActive; _activate = activate;
    }

    public string Name { get; }
    public string Tip { get; }
    /// <summary>The first of the user's own profiles: a thin line separates it from the ready-made presets.</summary>
    public bool StartsCustom { get; }
    public bool IsActive { get => _isActive(); set { if (value) _activate(); } }
    public void Refresh() => Raise(nameof(IsActive));
}

/// <summary>One row of the profile list on the "Profiller" page.</summary>
public class SavedProfileVM : ObservableObject
{
    private readonly Action<SavedProfileVM> _selected;

    public SavedProfileVM(string id, string name, Action<SavedProfileVM> selected)
    {
        Id = id; _name = name; _selected = selected;
    }

    public string Id { get; }

    private string _name;
    public string Name { get => _name; set => Set(ref _name, value); }

    private bool _isSelected;
    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (!Set(ref _isSelected, value)) return;
            if (value) _selected(this);
        }
    }

    /// <summary>The home page currently runs with this profile.</summary>
    private bool _inUse;
    public bool InUse { get => _inUse; set => Set(ref _inUse, value); }

    public void SelectWithoutEvent() { _isSelected = true; Raise(nameof(IsSelected)); }
    public void SetSelectedQuietly(bool selected) { _isSelected = selected; Raise(nameof(IsSelected)); }
}

public partial class MainViewModel
{
    private readonly ProfileSession _profiles;

    // ---------------------------------------------------------------- home page: presets and saved profiles in one strip

    public ObservableCollection<ProfileChipVM> Chips { get; } = new();

    // ---------------------------------------------------------------- "Profiller" page

    public ObservableCollection<SavedProfileVM> SavedProfiles { get; } = new();
    public bool HasNoProfiles => SavedProfiles.Count == 0;
    public bool HasEditing => _editing != null;

    /// <summary>The switches being edited. Their own settings object, so editing a profile never touches the ones the home page runs with.</summary>
    private readonly AppSettings _draft = new();
    private readonly List<OptionVM> _draftOptions = new();
    public ObservableCollection<OptionGroupVM> EditGroups { get; } = new();

    private SavedProfileVM? _editing;
    private bool _dirty;
    private bool _justSaved;

    private string _editName = "";
    public string EditName { get => _editName; set { if (Set(ref _editName, value)) MarkDirty(); } }

    private ProfileKind _editBase = ProfileKind.Maximum;
    public bool EditBaseSafe { get => _editBase == ProfileKind.Safe; set { if (value) SetEditBase(ProfileKind.Safe); } }
    public bool EditBaseMax { get => _editBase == ProfileKind.Maximum; set { if (value) SetEditBase(ProfileKind.Maximum); } }
    public bool EditBaseStream { get => _editBase == ProfileKind.Stream; set { if (value) SetEditBase(ProfileKind.Stream); } }

    private bool _saveFailed;
    public string EditStatus => _saveFailed ? Loc.T("Dosyaya yazılamadı: profil bu açılışta çalışır ama Antilag'ı kapatınca kaybolur.")
        : _dirty ? Loc.T("Kaydedilmemiş değişiklikler var.") : _justSaved ? Loc.T("✓ Kaydedildi.") : "";

    // deleting asks inside the page (no window): the "Profili sil" button turns into "Silinsin mi?  Evet, sil · Vazgeç"
    private bool _confirmingDelete;
    public bool ConfirmingDelete { get => _confirmingDelete; private set => Set(ref _confirmingDelete, value); }

    public RelayCommand NewProfileCommand { get; private set; } = null!;
    public RelayCommand SaveProfileCommand { get; private set; } = null!;
    public RelayCommand AskDeleteCommand { get; private set; } = null!;
    public RelayCommand ConfirmDeleteCommand { get; private set; } = null!;
    public RelayCommand CancelDeleteCommand { get; private set; } = null!;
    public RelayCommand UseProfileCommand { get; private set; } = null!;

    private void InitProfiles()
    {
        foreach (var def in Catalog.Options)
            _draftOptions.Add(new OptionVM(def, _draft) { Changed = MarkDirty });
        foreach (var g in _draftOptions.GroupBy(o => o.Def.Group))
            EditGroups.Add(new OptionGroupVM { Title = Loc.T(g.Key), Items = g.ToList() });       // grouped by the Turkish identity, shown in the chosen language
        _draft.Options = ProfileSession.PresetDefaults(ProfileKind.Maximum);

        NewProfileCommand = new RelayCommand(NewProfile, () => IsIdle);
        SaveProfileCommand = new RelayCommand(SaveEdited, () => _editing != null && IsIdle);
        AskDeleteCommand = new RelayCommand(() => ConfirmingDelete = true, () => _editing != null && IsIdle);
        ConfirmDeleteCommand = new RelayCommand(DeleteEdited, () => _editing != null && IsIdle);
        CancelDeleteCommand = new RelayCommand(() => ConfirmingDelete = false);
        UseProfileCommand = new RelayCommand(UseEdited, () => _editing != null && IsIdle);
        RebuildProfileLists();
    }

    private void RefreshProfileCommands()
    {
        NewProfileCommand.Refresh(); SaveProfileCommand.Refresh(); AskDeleteCommand.Refresh(); ConfirmDeleteCommand.Refresh(); UseProfileCommand.Refresh();
    }

    /// <summary>Re-reads the saved profiles into the strip of the home page and the list of the Profiller page (the profile being edited stays selected).</summary>
    private void RebuildProfileLists()
    {
        var keepId = _editing?.Id;
        Chips.Clear();
        SavedProfiles.Clear();
        _editing = null;

        void Preset(string key, string name, string tip) =>
            Chips.Add(new ProfileChipVM(name, tip, false, () => _profiles.ActiveKey == key, () => ActivateProfile(key)));
        Preset(ProfileSession.SafeKey, Loc.T("Hafif"), Loc.T("Hiçbir programı kapatmaz; Windows güncellemelerini geçici durdurur ve birkaç Windows ayarı yapar (en yüksek performans güç planı, Oyun Modu…)."));
        Preset(ProfileSession.MaximumKey, Loc.T("Tam temizlik"), Loc.T("CS2, Steam, FACEIT, Windows, ekran kartı ve ses sürücüleri ile 'Her zaman açık kalsın' listesindekiler dışındaki programları kapatır. Windows bildirimlerini susturur (geri yüklenene kadar) ve CS2 için tam ekran iyileştirmesini kapatır."));
        Preset(ProfileSession.StreamKey, Loc.T("Yayıncı"), Loc.T("OBS, Streamlabs, Discord, Elgato gibi tanınan yayın araçları, tarayıcı, VPN, GoodbyeDPI, diğer engel aşma programları ve tanınmayan arka plan programları açık kalır; gerisi kapanır. Bildirimler susturulur (geri yüklenene kadar), tam ekran iyileştirmesi kapatılır. Başka bir yayın aracını Ayarlar'dan 'Her zaman açık kalsın' listesine ekle."));

        var first = true;
        foreach (var p in Settings.SavedProfiles)
        {
            var id = p.Id;
            var key = ProfileSession.KeyOfCustom(id);
            Chips.Add(new ProfileChipVM(p.Name, Loc.F("Senin kaydettiğin profil: {0}", p.Name), first, () => _profiles.ActiveKey == key, () => ActivateProfile(key)));
            first = false;
            var row = new SavedProfileVM(id, p.Name, OnProfileRowSelected) { InUse = _profiles.ActiveKey == key };
            SavedProfiles.Add(row);
            if (id == keepId) { row.SelectWithoutEvent(); _editing = row; }
        }
        Raise(nameof(HasNoProfiles)); Raise(nameof(HasEditing));
        RefreshProfileCommands();
    }

    /// <summary>The user picked a profile in the list: its saved switches fill the editor. Edits of the profile that was open are saved first, never dropped.</summary>
    private void OnProfileRowSelected(SavedProfileVM row)
    {
        FlushProfileEdits();
        // saving rebuilt the list: the row that was clicked is a new object now, and the highlight must follow the profile that is in the editor
        row = SavedProfiles.FirstOrDefault(r => r.Id == row.Id) ?? row;
        foreach (var r in SavedProfiles) r.SetSelectedQuietly(r.Id == row.Id);
        var p = Settings.SavedProfiles.FirstOrDefault(x => x.Id == row.Id);
        if (p == null) return;
        _editing = row;
        _editName = p.Name;
        _editBase = p.Base;
        _draft.Options = ProfileSession.Complete(p.Options, p.Base);
        foreach (var o in _draftOptions) o.Refresh();
        _dirty = false; _dirtyOnlyBase = false; _justSaved = false; _saveFailed = false; ConfirmingDelete = false;
        RaiseEditor();
    }

    /// <summary>Saves the profile in the editor when it has unsaved changes (leaving the page or choosing another profile).</summary>
    private void FlushProfileEdits()
    {
        // a click on another starting point only refills the switches: it is not saved by leaving, so one wrong click cannot wipe a hand-tuned profile
        if (_editing != null && _dirty && !_dirtyOnlyBase && IsIdle) SaveEdited();
    }

    private void RaiseEditor()
    {
        Raise(nameof(EditName)); Raise(nameof(EditBaseSafe)); Raise(nameof(EditBaseMax)); Raise(nameof(EditBaseStream));
        Raise(nameof(EditStatus)); Raise(nameof(HasEditing));
        RefreshProfileCommands();
    }

    private bool _dirtyOnlyBase;

    private void MarkDirty()
    {
        _dirty = true; _dirtyOnlyBase = false; _justSaved = false; _saveFailed = false;
        Raise(nameof(EditStatus));
    }

    /// <summary>Choosing another starting point fills the editor with that preset's switches (the profile itself is unchanged until it is saved).</summary>
    private void SetEditBase(ProfileKind kind)
    {
        if (_editBase == kind) return;
        var onlyBase = !_dirty;
        _editBase = kind;
        _draft.Options = ProfileSession.PresetDefaults(kind);
        foreach (var o in _draftOptions) o.Refresh();
        MarkDirty();
        _dirtyOnlyBase = onlyBase;
        Raise(nameof(EditBaseSafe)); Raise(nameof(EditBaseMax)); Raise(nameof(EditBaseStream));
    }

    private void NewProfile()
    {
        if (!IsIdle) return;
        FlushProfileEdits();
        var p = _profiles.Create(ProfileKind.Maximum);
        SaveSettings();
        RebuildProfileLists();
        var row = SavedProfiles.First(r => r.Id == p.Id);
        row.IsSelected = true;                                    // fills the editor
    }

    private void SaveEdited()
    {
        if (_editing == null || !IsIdle) return;
        var key = ProfileSession.KeyOfCustom(_editing.Id);
        var wasInUse = _profiles.ActiveKey == key;
        _profiles.Save(_editing.Id, _editName, _editBase, new Dictionary<string, bool>(_draft.Options));
        var written = SaveSettings();
        var saved = _profiles.Find(_editing.Id);
        _editName = saved?.Name ?? _editName;                    // the name may have been made unique or shortened
        RebuildProfileLists();
        _saveFailed = !written;                                  // the profile works for this run either way; say so if the file could not be written
        _dirty = false; _dirtyOnlyBase = false; _justSaved = written;
        RaiseEditor();
        if (wasInUse)
        {
            // the home page runs with this profile: it now runs with exactly what was saved
            foreach (var o in _allOptions) o.Refresh();
        }
        RaiseProfileChoice();
    }

    private void DeleteEdited()
    {
        ConfirmingDelete = false;
        if (_editing == null || !IsIdle) return;
        _profiles.Delete(_editing.Id);                           // when it was in use the session falls back to "Tam temizlik"
        SaveSettings();
        _editing = null;
        _dirty = false; _justSaved = false;
        RebuildProfileLists();
        foreach (var o in _allOptions) o.Refresh();
        RaiseProfileChoice();
        if (SavedProfiles.Count > 0) SavedProfiles[0].IsSelected = true;
        else RaiseEditor();
    }

    /// <summary>"Bu profille başla": saves what is in the editor, makes it the profile of the home page and goes there.</summary>
    private void UseEdited()
    {
        if (_editing == null || !IsIdle) return;
        // always saved first: when the profile is already in use this also drops the temporary switch changes of the home page,
        // so Başlat runs with exactly what the editor shows
        SaveEdited();
        ActivateProfile(ProfileSession.KeyOfCustom(_editing.Id));
        GoTo(Page.Home);
    }

    private void RaiseProfileChoice()
    {
        foreach (var c in Chips) c.Refresh();
        foreach (var r in SavedProfiles) r.InUse = _profiles.ActiveKey == ProfileSession.KeyOfCustom(r.Id);
        RaiseStatus();        // the headline names the profile and says whether it was changed for this run
    }

#if DEBUG
    /// <summary>Developer screenshots: two sample profiles, the first one open in the editor.</summary>
    public void LoadDemoProfiles()
    {
        var a = _profiles.Create(ProfileKind.Maximum);
        var optionsA = ProfileSession.PresetDefaults(ProfileKind.Maximum);
        optionsA["proc.browsers"] = false; optionsA["proc.claude"] = false;
        _profiles.Save(a.Id, Loc.T("Maç günü"), ProfileKind.Maximum, optionsA);
        var b = _profiles.Create(ProfileKind.Stream);
        _profiles.Save(b.Id, Loc.T("Yayın"), ProfileKind.Stream, ProfileSession.PresetDefaults(ProfileKind.Stream));
        RebuildProfileLists();
        SavedProfiles[0].IsSelected = true;
    }

    /// <summary>Developer screenshots: the first demo profile is the one the home page runs with.</summary>
    public void UseDemoProfile() => ActivateProfile(ProfileSession.KeyOfCustom(Settings.SavedProfiles[0].Id));
#endif
}
