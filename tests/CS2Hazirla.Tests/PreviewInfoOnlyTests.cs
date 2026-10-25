using System.Xml.Linq;
using Xunit;

namespace CS2Hazirla.Tests;

/// <summary>
/// The preview ("Önizleme") is where people read what Başlat will do. It carries no action: nothing to tick, no "Her zaman açık kalsın",
/// only a way to close the window.
/// </summary>
public class PreviewInfoOnlyTests
{
    private static readonly XNamespace Wpf = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

    private static XDocument LoadPreview()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "CS2Hazirla.sln"))) dir = dir.Parent;
        Assert.NotNull(dir);
        return XDocument.Load(Path.Combine(dir!.FullName, "src", "CS2Hazirla", "PreviewWindow.xaml"));
    }

    [Fact]
    public void The_preview_has_no_control_to_use_except_the_close_button()
    {
        var all = LoadPreview().Descendants().ToList();
        var interactive = new[] { "CheckBox", "RadioButton", "ToggleButton", "ComboBox", "TextBox", "Slider" };
        Assert.DoesNotContain(all, e => interactive.Contains(e.Name.LocalName));

        var buttons = all.Where(e => e.Name == Wpf + "Button").ToList();
        var only = Assert.Single(buttons);
        Assert.Equal("{local:T 'Kapat'}", (string?)only.Attribute("Content"));
        Assert.True((bool?)only.Attribute("IsCancel") == true, "Esc must close the window");
    }

    [Fact]
    public void Nothing_in_the_preview_is_bound_to_a_command_or_offers_always_open()
    {
        var doc = LoadPreview();
        Assert.DoesNotContain(doc.Descendants().SelectMany(e => e.Attributes()), a => a.Name.LocalName == "Command");
        var text = doc.ToString();
        Assert.DoesNotContain("Her zaman açık kalsın", text);
        Assert.DoesNotContain("Mode=TwoWay", text);
    }
}
