namespace CS2Hazirla.Core;

/// <summary>
/// Which profile is active and what the user changed in it during THIS run of the app.
///
///  · Every start begins with the defaults of "Tam temizlik": the preset and its switches are not saved between runs.
///  · Moving to another preset never asks anything. The switches the user changed in the one they leave are remembered for this
///    run only, so coming back finds them as they were, while a preset that was not touched always opens with its defaults.
///  · The user's own profiles (<see cref="AppSettings.SavedProfiles"/>) are the only thing that is saved: choosing one loads
///    exactly the switches that were saved in it.
///
/// The active switches live in <see cref="AppSettings.Options"/> (the Planner reads them from there), so nothing else changes.
/// </summary>
public class ProfileSession
{
    public const string SafeKey = "safe";
    public const string MaximumKey = "maximum";
    public const string StreamKey = "stream";
    private const string CustomPrefix = "custom:";

    private readonly AppSettings _s;
    private readonly Dictionary<string, Dictionary<string, bool>> _edits = new();
    // the programs of the home list that were switched against what the profile says: per profile, this run of the app only (like _edits)
    private readonly Dictionary<string, Dictionary<string, bool>> _apps = new();
    private static readonly IReadOnlyDictionary<string, bool> NoChoices = new Dictionary<string, bool>();

    public ProfileSession(AppSettings settings)
    {
        _s = settings;
        ActiveKey = MaximumKey;
        _s.ApplyProfile(ProfileKind.Maximum);
    }

    public string ActiveKey { get; private set; }

    /// <summary>The home-list programs (key → close?) the user switched against the active profile. Empty until something is switched.</summary>
    public IReadOnlyDictionary<string, bool> AppChoices => _apps.TryGetValue(ActiveKey, out var d) ? d : NoChoices;

    /// <summary>Forgets the user's switch for one program (its category was switched in Ayrıntılar: the last click wins).</summary>
    public void ClearAppChoice(string appKey)
    {
        if (_apps.TryGetValue(ActiveKey, out var d) && d.Remove(appKey) && d.Count == 0) _apps.Remove(ActiveKey);
    }

    /// <summary>Records a switch of the home list. A switch back to what the profile says leaves no trace.</summary>
    public void SetAppChoice(string appKey, bool close, bool profileSays)
    {
        if (!_apps.TryGetValue(ActiveKey, out var d)) _apps[ActiveKey] = d = new Dictionary<string, bool>();
        if (close == profileSays) d.Remove(appKey); else d[appKey] = close;
        if (d.Count == 0) _apps.Remove(ActiveKey);
    }

    // ------------------------------------------------------------------ keys and names

    public static string KeyOf(ProfileKind kind) => kind switch
    {
        ProfileKind.Safe => SafeKey,
        ProfileKind.Maximum => MaximumKey,
        _ => StreamKey,
    };

    public static string KeyOfCustom(string id) => CustomPrefix + id;

    public static bool IsCustomKey(string key) => key.StartsWith(CustomPrefix, StringComparison.Ordinal);

    public bool IsCustomActive => IsCustomKey(ActiveKey);

    public SavedProfile? Find(string id) =>
        _s.SavedProfiles.FirstOrDefault(p => string.Equals(p.Id, id, StringComparison.OrdinalIgnoreCase));

    private SavedProfile? FindByKey(string key) => IsCustomKey(key) ? Find(key[CustomPrefix.Length..]) : null;

    public bool Exists(string key) => !IsCustomKey(key) || FindByKey(key) != null;

    public ProfileKind BaseOf(string key) => FindByKey(key)?.Base ?? key switch
    {
        SafeKey => ProfileKind.Safe,
        StreamKey => ProfileKind.Stream,
        _ => ProfileKind.Maximum,
    };

    public string NameOf(string key) => FindByKey(key)?.Name ?? key switch
    {
        SafeKey => Loc.T("Hafif"),
        StreamKey => Loc.T("Yayıncı"),
        _ => Loc.T("Tam temizlik"),
    };

    public string ActiveName => NameOf(ActiveKey);

    // ------------------------------------------------------------------ switches

    /// <summary>Every option with the value a preset gives it.</summary>
    public static Dictionary<string, bool> PresetDefaults(ProfileKind kind) =>
        Catalog.Options.ToDictionary(o => o.Id, o => Catalog.ProfileDefault(kind, o.Id));

    /// <summary>Every option; options that did not exist when the profile was saved take the default of the preset it started from.</summary>
    public static Dictionary<string, bool> Complete(IReadOnlyDictionary<string, bool>? saved, ProfileKind baseKind) =>
        Catalog.Options.ToDictionary(o => o.Id, o =>
        {
            if (saved != null && saved.TryGetValue(o.Id, out var v)) return v;
            // a profile saved while Discord, GoodbyeDPI and the other block-bypass tools were ONE switch ("proc.dpi"): Discord and the
            // other bypass tools keep following it, so the profile closes exactly what it closed before
            if (o.Id is "proc.discord" or "proc.bypass" && saved != null && saved.TryGetValue("proc.dpi", out var merged)) return merged;
            return Catalog.ProfileDefault(baseKind, o.Id);
        });

    /// <summary>The switches a profile opens with when nothing was changed in it during this run.</summary>
    public Dictionary<string, bool> DefaultsOf(string key) =>
        FindByKey(key) is { } p ? Complete(p.Options, p.Base) : PresetDefaults(BaseOf(key));

    /// <summary>The user changed switches of the active profile (compared with what it opens with).</summary>
    public bool IsCustomized
    {
        get
        {
            var d = DefaultsOf(ActiveKey);
            return Catalog.Options.Any(o => _s.IsOn(o.Id) != d[o.Id]);
        }
    }

    // ------------------------------------------------------------------ moving between profiles

    /// <summary>Makes <paramref name="key"/> the active profile. Never asks anything. Returns false when it no longer exists.</summary>
    public bool Activate(string key)
    {
        if (key == ActiveKey) return true;
        if (!Exists(key)) return false;
        _edits[ActiveKey] = new Dictionary<string, bool>(_s.Options);       // what was changed here is still there when the user comes back
        ActiveKey = key;
        Load(key);
        return true;
    }

    public bool Activate(ProfileKind preset) => Activate(KeyOf(preset));

    private void Load(string key)
    {
        _s.Profile = BaseOf(key);
        _s.Options = _edits.TryGetValue(key, out var e) ? Complete(e, BaseOf(key)) : DefaultsOf(key);
    }

    // ------------------------------------------------------------------ carrying a run over to a window that is opened anew

    /// <summary>What the user switched during this run of the app, so that a window opened anew (another language) does not lose it.</summary>
    public record Snapshot(string ActiveKey, Dictionary<string, bool> ActiveOptions,
                           Dictionary<string, Dictionary<string, bool>> Edits, Dictionary<string, Dictionary<string, bool>> Apps);

    public Snapshot TakeSnapshot() => new(ActiveKey, new Dictionary<string, bool>(_s.Options), CopyOf(_edits), CopyOf(_apps));

    public void Restore(Snapshot snap)
    {
        foreach (var (key, v) in snap.Edits) _edits[key] = new Dictionary<string, bool>(v);
        foreach (var (key, v) in snap.Apps) _apps[key] = new Dictionary<string, bool>(v);
        if (!Exists(snap.ActiveKey)) return;                        // the profile is gone: stay on the default preset
        ActiveKey = snap.ActiveKey;
        _s.Profile = BaseOf(ActiveKey);
        _s.Options = Complete(snap.ActiveOptions, BaseOf(ActiveKey));
    }

    private static Dictionary<string, Dictionary<string, bool>> CopyOf(Dictionary<string, Dictionary<string, bool>> d) =>
        d.ToDictionary(kv => kv.Key, kv => new Dictionary<string, bool>(kv.Value));

    // ------------------------------------------------------------------ the user's own profiles

    private string UniqueName(string wanted, string? exceptId)
    {
        var name = string.IsNullOrWhiteSpace(wanted) ? Loc.T("Profil") : wanted.Trim();     // the suggestion speaks the language of the moment; once saved the name is the user's own
        if (name.Length > 40) name = name[..40].TrimEnd();
        bool Taken(string n) => _s.SavedProfiles.Any(p => !string.Equals(p.Id, exceptId, StringComparison.OrdinalIgnoreCase)
                                                           && string.Equals(p.Name, n, StringComparison.CurrentCultureIgnoreCase));
        if (!Taken(name)) return name;
        for (var i = 2; ; i++)
        {
            var candidate = $"{name} {i}";
            if (!Taken(candidate)) return candidate;
        }
    }

    /// <summary>A new profile with the defaults of a preset; it is added to the saved list at once.</summary>
    public SavedProfile Create(ProfileKind baseKind = ProfileKind.Maximum)
    {
        var p = new SavedProfile
        {
            Name = UniqueName(Loc.T("Profil"), null),
            Base = baseKind,
            Options = PresetDefaults(baseKind),
        };
        _s.SavedProfiles.Add(p);
        return p;
    }

    /// <summary>Saves the switches. When the profile is the active one it now runs with exactly what was saved.</summary>
    public void Save(string id, string name, ProfileKind baseKind, IReadOnlyDictionary<string, bool> options)
    {
        var p = Find(id) ?? throw new ArgumentException("profil bulunamadı", nameof(id));
        p.Name = UniqueName(name, p.Id);
        p.Base = baseKind;
        p.Options = Complete(options, baseKind);
        var key = KeyOfCustom(p.Id);
        _edits.Remove(key);
        _apps.Remove(key);
        if (ActiveKey == key) Load(key);
    }

    public void Delete(string id)
    {
        var p = Find(id);
        if (p == null) return;
        var key = KeyOfCustom(p.Id);
        _s.SavedProfiles.Remove(p);
        _edits.Remove(key);
        _apps.Remove(key);
        if (ActiveKey == key)
        {
            ActiveKey = MaximumKey;                                          // the profile in use is gone: back to the default preset
            Load(MaximumKey);
        }
    }
}
