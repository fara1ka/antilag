namespace CS2Hazirla.Core;

/// <summary>
/// The English for every Turkish text of the app. The entries live in one file per area (<c>En*.cs</c>), so that people (and agents) who work
/// on different parts of the app never edit the same file. A Turkish text that appears in several areas must have the same English everywhere.
/// </summary>
public static class EnglishStrings
{
    private static readonly Lazy<(Dictionary<string, string> Map, List<string> Conflicts)> Built = new(Build);

    /// <summary>Turkish text → English text.</summary>
    public static IReadOnlyDictionary<string, string> Map => Built.Value.Map;

    /// <summary>Turkish texts that two entries translate differently (a bug: the tests list them).</summary>
    public static IReadOnlyList<string> Conflicts => Built.Value.Conflicts;

    /// <summary>Every area with its entries, in the order they are read.</summary>
    public static IReadOnlyList<(string Area, (string Tr, string En)[] Entries)> Areas { get; } = new (string, (string, string)[])[]
    {
        ("Common", EnCommon.Entries),
        ("Catalog", EnCatalog.Entries),
        ("Planner", EnPlanner.Entries),
        ("Orchestrator", EnOrchestrator.Entries),
        ("System", EnSystem.Entries),
        ("Update", EnUpdate.Entries),
        ("ViewModels", EnViewModels.Entries),
        ("Profiles", EnProfiles.Entries),
        ("Windows", EnWindows.Entries),
        ("Xaml", EnXaml.Entries),
    };

    private static (Dictionary<string, string>, List<string>) Build()
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        var conflicts = new List<string>();
        foreach (var (area, entries) in Areas)
            foreach (var (tr, en) in entries)
            {
                if (map.TryGetValue(tr, out var existing))
                {
                    if (!string.Equals(existing, en, StringComparison.Ordinal)) conflicts.Add($"[{area}] {tr}");
                    continue;
                }
                map[tr] = en;
            }
        return (map, conflicts);
    }
}
