using System;
using System.Windows;
using System.Windows.Markup;
using CS2Hazirla.Core;

namespace CS2Hazirla;

/// <summary>
/// The fixed texts of the XAML files: <c>Text="{local:T 'Ayarlar'}"</c>. The Turkish text in the file is the key (as in the code); while the app speaks
/// English the markup extension returns the English one from <see cref="EnglishStrings"/>. A text that comes from a binding was made in the right
/// language by the view model. Write a quote inside the text as \' (and a brace as \{ or \}).
/// <c>LanguageTests</c> checks that every text property of every XAML file uses it and that every such text has an English entry.
/// </summary>
[MarkupExtensionReturnType(typeof(string))]
public class TExtension : MarkupExtension
{
    public string Text { get; set; } = "";

    public TExtension() { }
    public TExtension(string text) => Text = text;

    public override object ProvideValue(IServiceProvider serviceProvider) => Loc.T(Text);
}

/// <summary>The files say xml:lang="tr-TR": a window that shows English text is set to English (quotation marks, number formats of bindings).</summary>
internal static class WindowLanguage
{
    public static void Apply(Window window)
    {
        if (Loc.IsEnglish) window.Language = XmlLanguage.GetLanguage("en-US");
    }
}
