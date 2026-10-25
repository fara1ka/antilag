using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Interop;
using System.Runtime.InteropServices;

namespace CS2Hazirla;

/// <summary>
/// Same call shape as MessageBox.Show(text, caption, buttons, icon), but in the app's own dark look
/// (a white system message box in the middle of a dark app looks unfinished). Always shown on the UI thread.
/// </summary>
public static class UiDialog
{
    public static MessageBoxResult Show(string text, string title, MessageBoxButton buttons = MessageBoxButton.OK, MessageBoxImage image = MessageBoxImage.None, bool defaultNo = false)
    {
        var app = Application.Current;
        if (app == null) return MessageBoxResult.None;
#if DEBUG
        if (App.OffScreenRender) return MessageBoxResult.OK;    // developer screenshot run: never open (and activate) a window
#endif
        if (!app.Dispatcher.CheckAccess())
            return app.Dispatcher.Invoke(() => Show(text, title, buttons, image, defaultNo));

        var w = new DialogWindow(text, title, buttons, image, defaultNo);
        var owner = app.MainWindow;
        if (owner != null && owner.IsVisible && owner.IsLoaded && !ReferenceEquals(owner, w))
        {
            // a dialog opened from a dialog (picker/preview) belongs to the window that is on top
            foreach (Window win in app.Windows)
                if (win.IsActive && win.IsVisible && !ReferenceEquals(win, w)) { owner = win; break; }
            w.Owner = owner;
        }
        else w.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        w.ShowDialog();
        return w.Result;
    }
}

public partial class DialogWindow : Window
{
    public MessageBoxResult Result { get; private set; } = MessageBoxResult.None;
    private MessageBoxResult _escape = MessageBoxResult.OK;

    /// <param name="defaultNo">Enter answers "Hayır" (for questions whose "Evet" destroys something).</param>
    public DialogWindow(string text, string title, MessageBoxButton buttons, MessageBoxImage image, bool defaultNo = false)
    {
        InitializeComponent();
        Title = title;
        HeadingText.Text = title;
        MessageText.Text = text;
        SourceInitialized += (_, _) => DarkTitleBar.Apply(this);

        var (brush, soft) = image switch
        {
            MessageBoxImage.Warning => ("WarnBrush", "WarnSoftBrush"),
            MessageBoxImage.Error => ("BadBrush", "BadSoftBrush"),
            _ => ("AccentBrush", "AccentSoftBrush"),
        };
        IconPath.SetValue(System.Windows.Documents.TextElement.ForegroundProperty, (Brush)FindResource(brush));
        IconBack.Fill = (Brush)FindResource(soft);

        switch (buttons)
        {
            case MessageBoxButton.YesNo:
                AddButton("Hayır", MessageBoxResult.No, isDefault: defaultNo, isCancel: true);
                AddButton("Evet", MessageBoxResult.Yes, isDefault: !defaultNo, isCancel: false);
                _escape = MessageBoxResult.No;
                break;
            case MessageBoxButton.YesNoCancel:
                AddButton("Vazgeç", MessageBoxResult.Cancel, false, true);
                AddButton("Hayır", MessageBoxResult.No, false, false);
                AddButton("Evet", MessageBoxResult.Yes, true, false);
                _escape = MessageBoxResult.Cancel;
                break;
            case MessageBoxButton.OKCancel:
                AddButton("Vazgeç", MessageBoxResult.Cancel, false, true);
                AddButton("Tamam", MessageBoxResult.OK, true, false);
                _escape = MessageBoxResult.Cancel;
                break;
            default:
                AddButton("Tamam", MessageBoxResult.OK, true, true);
                break;
        }
    }

    private void AddButton(string label, MessageBoxResult result, bool isDefault, bool isCancel)
    {
        var b = new Button
        {
            Content = label,
            IsDefault = isDefault,
            IsCancel = isCancel,
            MinWidth = 92,
            Padding = new Thickness(20, 9, 20, 9),
            Margin = new Thickness(ButtonRow.Children.Count == 0 ? 0 : 10, 0, 0, 0),
        };
        if (isDefault) b.Style = (Style)FindResource("PrimaryButton");
        b.Click += (_, _) => { Result = result; DialogResult = true; };
        ButtonRow.Children.Add(b);
    }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        if (Result == MessageBoxResult.None) Result = _escape;     // closed with the window's X or Esc
        base.OnClosing(e);
    }
}

/// <summary>Dark title bar (and a matching caption colour on Windows 11) so the OS window frame does not break the dark design.</summary>
public static class DarkTitleBar
{
    [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

    private const int UseImmersiveDarkMode = 20;     // Windows 10 20H1+ / 11
    private const int BorderColor = 34;               // Windows 11
    private const int CaptionColor = 35;               // Windows 11
    private const int TextColor = 36;                  // Windows 11

    public static void Apply(Window w)
    {
        try
        {
            var h = new WindowInteropHelper(w).Handle;
            if (h == IntPtr.Zero) return;
            int on = 1;
            DwmSetWindowAttribute(h, UseImmersiveDarkMode, ref on, sizeof(int));
            int bg = 0x00181312;       // COLORREF 0x00BBGGRR of #121318 (Bg)
            int line = 0x003F332E;     // #2E333F (Hairline)
            int text = 0x00F4EEEC;     // #ECEEF4 (Text)
            DwmSetWindowAttribute(h, CaptionColor, ref bg, sizeof(int));
            DwmSetWindowAttribute(h, BorderColor, ref line, sizeof(int));
            DwmSetWindowAttribute(h, TextColor, ref text, sizeof(int));
        }
        catch { /* purely cosmetic: older Windows versions simply keep their normal title bar */ }
    }
}
