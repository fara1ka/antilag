using System.Windows;

namespace CS2Hazirla;

public partial class PreviewWindow : Window
{
    public PreviewWindow(PreviewViewModel vm)
    {
        InitializeComponent();
        DataContext = vm;
        SourceInitialized += (_, _) => DarkTitleBar.Apply(this);
    }

    private void Ok_Click(object sender, RoutedEventArgs e) => Close();
}
