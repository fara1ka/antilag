using CS2Hazirla.Core;
using Xunit;

namespace CS2Hazirla.Tests;

/// <summary>The search box of the Ayrıntılar panel.</summary>
public class SearchTextTests
{
    private static bool Finds(string query, string text) => SearchText.Matches(query, SearchText.Normalize(text));

    [Theory]
    [InlineData("tarayicilar", "Tarayıcılar (Chrome, Edge, Firefox…)")]
    [InlineData("TARAYICILAR", "Tarayıcılar (Chrome, Edge, Firefox…)")]
    [InlineData("isik", "Bilgisayar markası ve ışık programları")]
    [InlineData("ışık", "Bilgisayar markasi ve isik programlari")]
    [InlineData("ISIK", "ışık")]
    [InlineData("Işık", "isik")]
    [InlineData("İstanbul", "istanbul")]
    [InlineData("guc plani", "En yüksek performans güç planı")]
    [InlineData("oyun magaza", "Diğer oyun mağazaları")]
    public void A_keyboard_without_Turkish_letters_finds_what_it_looks_for(string query, string text) => Assert.True(Finds(query, text));

    [Theory]
    [InlineData("discord vpn", "Discord")]
    [InlineData("xyz", "Discord")]
    [InlineData("tarayici edgex", "Tarayıcılar (Chrome, Edge, Firefox…)")]
    public void Every_word_of_the_query_has_to_be_there(string query, string text) => Assert.False(Finds(query, text));

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void An_empty_search_finds_everything(string? query) => Assert.True(SearchText.Matches(query, SearchText.Normalize("herhangi bir satır")));

    [Fact]
    public void A_search_finds_a_switch_by_the_programs_behind_it_not_only_by_its_title()
    {
        string KeyOf(string id) => SearchText.Normalize(string.Join(" ", Catalog.SearchTerms(id)) + " " + Catalog.Find(id)!.Title);

        Assert.True(SearchText.Matches("dropbox", KeyOf("proc.cloud")));        // the title only names OneDrive and Dropbox as examples; more are behind it
        Assert.True(SearchText.Matches("googledrive", KeyOf("proc.cloud")));
        Assert.True(SearchText.Matches("discord", KeyOf("proc.discord")));
        Assert.True(SearchText.Matches("goodbyedpi", KeyOf("proc.dpi")));
        Assert.True(SearchText.Matches("zapret", KeyOf("proc.bypass")));        // the other block-bypass tools have a switch of their own
        Assert.False(SearchText.Matches("zapret", KeyOf("proc.dpi")));
        Assert.False(SearchText.Matches("discord", KeyOf("proc.dpi")));         // Discord is a switch of its own now
        Assert.True(SearchText.Matches("windows arama", KeyOf("svc.other")));  // a Windows service the switch stops
    }

    [Fact]
    public void Every_switch_can_be_found_by_its_own_title()
    {
        foreach (var o in Catalog.Options)
            Assert.True(SearchText.Matches(o.Title, SearchText.Normalize(o.Title)), o.Id);
    }
}
