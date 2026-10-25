namespace CS2Hazirla.Core;

/// <summary>English for the language choice itself and for texts that belong to no single area. Owned by whoever builds the language switch.</summary>
public static class EnCommon
{
    public static readonly (string Tr, string En)[] Entries =
    {
        // the language card says both names whatever language is on: these are the same in both
        ("Dil / Language", "Dil / Language"),
        ("Türkçe", "Türkçe"),
        ("English", "English"),
        ("Dili değiştirince pencere yeniden açılır.", "The window reopens when you change the language."),
        // what a screen reader says for the small buttons of a row (the visible button says only Çıkar / Kapat)
        ("Çıkar: {0}", "Remove: {0}"),
        ("Kapat: {0}", "Close: {0}"),
    };
}
