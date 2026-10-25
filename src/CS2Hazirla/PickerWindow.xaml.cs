using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using CS2Hazirla.Core;

namespace CS2Hazirla;

/// <summary>Checkbox list the user picks from. Nothing is typed in as a name: the search box only filters what is shown.</summary>
public partial class PickerWindow : Window
{
    private readonly PickerRequest _req;
    public List<string> Chosen { get; } = new();

    public PickerWindow(PickerRequest req)
    {
        _req = req;
        InitializeComponent();
        WindowLanguage.Apply(this);
        Title = req.Title;
        TitleText.Text = req.Title;
        HintText.Text = req.Hint;
        OkButton.Content = Loc.T("Ekle");
        foreach (var i in req.Items) i.PropertyChanged += (_, _) => UpdateCount();
        SourceInitialized += (_, _) => DarkTitleBar.Apply(this);
        ApplyFilter();
        UpdateCount();
        Loaded += (_, _) => SearchBox.Focus();
    }

    private void ApplyFilter()
    {
        var q = SearchBox.Text.Trim();
        var shown = q.Length == 0
            ? _req.Items
            : _req.Items.Where(i => i.Title.Contains(q, StringComparison.CurrentCultureIgnoreCase)
                                 || i.Subtitle.Contains(q, StringComparison.CurrentCultureIgnoreCase)
                                 || i.Key.Contains(q, StringComparison.OrdinalIgnoreCase)).ToList();
        ItemList.ItemsSource = shown;
        EmptyText.Visibility = shown.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        EmptyText.Text = _req.Items.Count == 0 ? Loc.T("Listelenecek program yok.") : Loc.T("Aramana uyan program yok.");
    }

    private void UpdateCount()
    {
        var n = _req.Items.Count(i => i.IsChecked);
        CountText.Text = n == 0 ? Loc.F("{0} seçenek", _req.Items.Count) : Loc.F("{0} tanesi seçildi", n);
        OkButton.IsEnabled = n > 0;
    }

    private void Search_Changed(object sender, TextChangedEventArgs e) => ApplyFilter();

    private void Row_Click(object sender, MouseButtonEventArgs e)
    {
        // clicking anywhere on the row toggles it (the checkbox itself handles its own click)
        if (e.OriginalSource is DependencyObject d && FindAncestor<CheckBox>(d) != null) return;
        if (sender is FrameworkElement { DataContext: PickItemVM item }) item.IsChecked = !item.IsChecked;
    }

    private static T? FindAncestor<T>(DependencyObject d) where T : DependencyObject
    {
        while (d != null)
        {
            if (d is T t) return t;
            d = System.Windows.Media.VisualTreeHelper.GetParent(d);
        }
        return null;
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        Chosen.AddRange(_req.Items.Where(i => i.IsChecked).Select(i => i.Key));
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
