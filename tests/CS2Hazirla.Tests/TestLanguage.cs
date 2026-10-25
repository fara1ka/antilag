using System.Runtime.CompilerServices;
using CS2Hazirla.Core;

namespace CS2Hazirla.Tests;

/// <summary>
/// The tests run in Turkish (the source language: they read Turkish text). A developer can run the whole suite in English
/// (<c>CS2H_TEST_LANG=en</c>): the tests that check Turkish wording then fail by design, but a test of BEHAVIOUR that fails means that a
/// translated text was used where an identity was meant (a group name compared, a key stored...).
/// </summary>
internal static class TestLanguage
{
    public static string Default => Environment.GetEnvironmentVariable("CS2H_TEST_LANG") == "en" ? "en" : "tr";

    [ModuleInitializer]
    public static void Init() => Loc.Use(Default);
}
