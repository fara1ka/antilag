using System;
using CS2Hazirla.Core;

namespace CS2Hazirla;

/// <summary>The language card at the top of Ayarlar.</summary>
public partial class MainViewModel
{
    /// <summary>The user chose a language: the app opens a new window in it (<see cref="App.ReopenIn"/>). Every text is made in the language that is on when a window is built.</summary>
    public event Action<string>? LanguageChangeRequested;

    public bool LanguageTurkish { get => Loc.Language == Loc.Turkish; set { if (value) ChooseLanguage(Loc.Turkish); } }
    public bool LanguageEnglish { get => Loc.Language == Loc.English; set { if (value) ChooseLanguage(Loc.English); } }
    public string LanguageHint => Loc.T("Dili değiştirince pencere yeniden açılır.");

    private void ChooseLanguage(string language)
    {
        if (language == Loc.Language) return;
        if (!IsIdle)
        {
            // something is running: the radio that was just clicked goes back to the language that is on
            _ui.BeginInvoke(() => { Raise(nameof(LanguageTurkish)); Raise(nameof(LanguageEnglish)); });
            return;
        }
        FlushProfileEdits();                 // a profile that is being edited is kept, never dropped silently
        Settings.Language = language;
        SaveSettings();
        LanguageChangeRequested?.Invoke(language);
    }

    /// <summary>What this run has switched (profile, switches of programs): handed to the window that replaces this one.</summary>
    public ProfileSession.Snapshot TakeSessionSnapshot() => _profiles.TakeSnapshot();
}
