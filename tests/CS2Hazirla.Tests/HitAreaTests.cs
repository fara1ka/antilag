using System.Xml.Linq;
using Xunit;

namespace CS2Hazirla.Tests;

/// <summary>
/// Guards the "I have to click exactly on the text" bug: in WPF an element without a Background is only clickable on its drawn pixels,
/// so a template whose root has no Background reacts only on the letters. Every clickable template must paint (or be) its whole box.
/// </summary>
public class HitAreaTests
{
    private static readonly XNamespace Wpf = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
    private static readonly XNamespace X = "http://schemas.microsoft.com/winfx/2006/xaml";

    private static XDocument LoadControls()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "CS2Hazirla.sln"))) dir = dir.Parent;
        Assert.NotNull(dir);
        return XDocument.Load(Path.Combine(dir!.FullName, "src", "CS2Hazirla", "Theme", "Controls.xaml"));
    }

    private static string? StyleKey(XElement template) => template.Ancestors(Wpf + "Style").FirstOrDefault()?.Attribute(X + "Key")?.Value;

    [Fact]
    public void Every_clickable_template_has_a_root_that_catches_clicks_over_its_whole_box()
    {
        var clickable = new[] { "Button", "RadioButton", "CheckBox", "Thumb" };
        var templates = LoadControls().Descendants(Wpf + "ControlTemplate")
            .Where(t => clickable.Contains((string?)t.Attribute("TargetType") ?? "")).ToList();
        Assert.True(templates.Count >= 9, "the theme should define the button, radio, check box and thumb templates");

        foreach (var t in templates)
        {
            var root = t.Elements().First();
            var hasBackground = root.Attribute("Background") != null;
            Assert.True(hasBackground, $"template of '{StyleKey(t) ?? t.Attribute("TargetType")?.Value}' ({t.Attribute("TargetType")?.Value}): its root <{root.Name.LocalName}> has no Background, so only the letters can be clicked");
        }
    }

    [Theory]
    [InlineData("TextButton", 40)]
    [InlineData("LinkButton", 36)]
    [InlineData("PanelTab", 40)]
    [InlineData("SwitchRow", 48)]
    public void The_small_controls_are_at_least_as_tall_as_a_fingertip_allows(string styleKey, int minimum)
    {
        var style = LoadControls().Descendants(Wpf + "Style").First(s => (string?)s.Attribute(X + "Key") == styleKey);
        var setter = style.Elements(Wpf + "Setter").FirstOrDefault(s => (string?)s.Attribute("Property") == "MinHeight");
        Assert.NotNull(setter);
        Assert.True(double.Parse((string)setter!.Attribute("Value")!, System.Globalization.CultureInfo.InvariantCulture) >= minimum, styleKey);
    }

    [Fact]
    public void The_icon_buttons_are_big_boxes_not_thin_strokes()
    {
        var style = LoadControls().Descendants(Wpf + "Style").First(s => (string?)s.Attribute(X + "Key") == "IconButton");
        string Value(string p) => (string)style.Elements(Wpf + "Setter").First(s => (string?)s.Attribute("Property") == p).Attribute("Value")!;
        Assert.True(double.Parse(Value("Width")) >= 40);
        Assert.True(double.Parse(Value("Height")) >= 40);
    }
}
