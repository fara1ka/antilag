using System.Windows;

namespace CS2Hazirla;

public partial class PreviewWindow : Window
{
    public PreviewWindow(PreviewViewModel vm)
    {
        InitializeComponent();
        WindowLanguage.Apply(this);
        DataContext = vm;
        SourceInitialized += (_, _) => DarkTitleBar.Apply(this);
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
