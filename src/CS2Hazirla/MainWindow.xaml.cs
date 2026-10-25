using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CS2Hazirla.Core;

namespace CS2Hazirla;

public partial class MainWindow : Window
{
    private readonly MainViewModel _vm;
    private readonly string[] _args;
    private bool _exiting;
    private bool _logAtEnd = true;

    public MainWindow(MainViewModel vm, string[] args)
    {
        _vm = vm;
        _args = args;
        InitializeComponent();
        WindowLanguage.Apply(this);
        DataContext = vm;
#if DEBUG
        // screenshot runs must never disturb the person at the computer (a game, a call...): off-screen, no focus, no taskbar button
        if (Array.IndexOf(args, "--screenshot") >= 0)
        {
            WindowStartupLocation = WindowStartupLocation.Manual;
            Left = -20000; Top = -20000;
            ShowActivated = false;
            ShowInTaskbar = false;
        }
#endif
        // never open larger than the screen (small laptops): the buttons at the bottom must stay reachable
        var work = SystemParameters.WorkArea;
        Height = Math.Min(Height, work.Height - 20);
        Width = Math.Min(Width, work.Width - 20);
        MinHeight = Math.Min(MinHeight, Height);
        MinWidth = Math.Min(MinWidth, Width);
        Icon = BitmapFrame.Create(new Uri("pack://application:,,,/Assets/app.ico"));

        AdminBadge.Visibility = vm.IsElevated ? Visibility.Collapsed : Visibility.Visible;

        // looks: the fine grain over the background glow, the entrance of the home page, the breathing light behind the dial, the bar on the mode card
        Grain.Fill = MakeGrain();
        _breathing = new Breathing(HeroHalo, HaloScale);
        Activated += (_, _) => { UpdateBreathing(); vm.RescanIfStale(); vm.RefreshScreenNote(); };
        Deactivated += (_, _) => UpdateBreathing();
        StateChanged += (_, _) => UpdateBreathing();
        IsVisibleChanged += (_, _) => UpdateBreathing();
        vm.PropertyChanged += (_, e) =>
        {
            switch (e.PropertyName)
            {
                case nameof(MainViewModel.IsBusy):
                case nameof(MainViewModel.PageHome):
                case nameof(MainViewModel.DrawerOpen):
                    UpdateBreathing();
                    break;
            }
        };
        ContentHost.SizeChanged += (_, _) => ApplyHomeLayout();
        RunIntro();

        vm.ShowPreview = ShowPreview;
        vm.ShowPicker = ShowPicker;
        vm.ExitRequested += ExitApp;
        // a new language: the window opens again in it (after this click has been handled, not inside it)
        vm.LanguageChangeRequested += language => Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Background, () => App.ReopenIn(this, language));
        SourceInitialized += (_, _) => DarkTitleBar.Apply(this);

        // Esc closes the Ayrıntılar panel
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key != Key.Escape || !vm.DrawerOpen) return;
            // the first Esc clears a search that is on screen (wherever the focus is), the next one closes the panel;
            // the search box exists only on Özet / Tam liste, so on the other tabs Esc just closes
            if (vm.HasSearch && vm.ShowSelections) vm.Search = ""; else vm.DrawerOpen = false;
            e.Handled = true;
        };

        // keyboard: opening Ayrıntılar moves the focus into the panel (the search box when there is one; Tab then stays inside the panel);
        // closing returns the focus to the button that opened it
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName != nameof(MainViewModel.DrawerOpen)) return;
            Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Input, () =>
            {
                if (vm.DrawerOpen)
                {
                    if (!vm.ShowSelections || !SearchBox.Focus()) DrawerPanel.MoveFocus(new TraversalRequest(FocusNavigationDirection.First));
                }
                else if (MainPage.IsVisible) DetailsButton.Focus();
            });
        };

        // a new search or another tab shows another list: it starts at its top, not at the scroll position of the old one
        ((INotifyCollectionChanged)vm.ShownGroups).CollectionChanged += (_, e) =>
        {
            if (e.Action == NotifyCollectionChangedAction.Reset) SelectionScroll.ScrollToTop();
        };

        // the mouse wheel scrolls the page when the list under the pointer has nothing more to scroll
        void ForwardWheelAtEdges(System.Windows.Controls.ScrollViewer list) => list.PreviewMouseWheel += (s, e) =>
        {
            var sv = (System.Windows.Controls.ScrollViewer)s;
            var atEdge = sv.ScrollableHeight == 0 || (e.Delta > 0 && sv.VerticalOffset <= 0) || (e.Delta < 0 && sv.VerticalOffset >= sv.ScrollableHeight);
            if (!atEdge) return;
            e.Handled = true;
            MainPage.RaiseEvent(new MouseWheelEventArgs(e.MouseDevice, e.Timestamp, e.Delta) { RoutedEvent = UIElement.MouseWheelEvent, Source = s });
        };
        ForwardWheelAtEdges(LogScroll);
        ForwardWheelAtEdges(HomeListScroll);

        // follow the newest log line only while the reader is at the bottom: someone who scrolled up to read must not be pulled away
        LogScroll.ScrollChanged += (_, e) =>
        {
            if (e.ExtentHeightChange == 0) _logAtEnd = e.VerticalOffset + e.ViewportHeight >= e.ExtentHeight - 12;
        };
        ((INotifyCollectionChanged)vm.LogLines).CollectionChanged +=
            (_, e) =>
            {
                if (e.Action == NotifyCollectionChangedAction.Reset) _logAtEnd = true;     // a new run starts with an empty list: follow it again
                Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Background, () => { if (_logAtEnd) LogScroll.ScrollToEnd(); });
            };

        // list changes in Ayarlar are saved at once
        ((INotifyCollectionChanged)vm.KeepOpenList.Items).CollectionChanged += (_, _) => vm.SaveSettings();
        ((INotifyCollectionChanged)vm.AlwaysCloseList.Items).CollectionChanged += (_, _) => vm.SaveSettings();
        ((INotifyCollectionChanged)vm.ServiceRules).CollectionChanged += (_, _) => vm.SaveSettings();

        Loaded += async (_, _) => await OnLoadedAsync();
    }

    private void Scrim_Click(object sender, MouseButtonEventArgs e) => _vm.DrawerOpen = false;

    // ---------------------------------------------------------------- looks

    private readonly Breathing _breathing;
    private bool _assumeActive;      // DEBUG performance runs: the window is off-screen and never "active", yet the loop must be measured

    /// <summary>
    /// The light behind the dial breathes only while the app is ready, in front, on the home page and nothing else is on top of it.
    /// Anything else stops it completely (no clock stays behind); so does Windows with animations off.
    /// </summary>
    private void UpdateBreathing()
    {
        var front = (IsActive || _assumeActive) && IsVisible && WindowState != WindowState.Minimized;
        _breathing.Update(front && _vm.PageHome && !_vm.IsBusy && !_vm.DrawerOpen);
    }

    private bool? _wideHome;

    /// <summary>
    /// Wide window: the dial and the status stand on the left, the mode strip, its card and the buttons on the right. Narrow window: one column, the dial first.
    /// Only the grid cell of the right block changes; nothing is rebuilt.
    /// </summary>
    private void ApplyHomeLayout()
    {
        var wide = ContentHost.ActualWidth >= 880;      // 24 + 300 + 24 + 500 + 24 = 872 needed
        if (_wideHome == wide) return;
        _wideHome = wide;
        // side by side the block keeps one height whatever mode is chosen (the card grows and shrinks inside it): the strip a person is clicking never moves
        HomeGrid.MinHeight = wide ? 470 : 0;
        System.Windows.Controls.Grid.SetColumn(RightCol, wide ? 1 : 0);
        System.Windows.Controls.Grid.SetRow(RightCol, wide ? 0 : 1);
        RightCol.Margin = wide ? new Thickness(24, 0, 0, 0) : new Thickness(0, 28, 0, 0);
        // side by side the buttons read from the left edge of the block; stacked under the dial they are centred
        IntroButtons.HorizontalAlignment = wide ? HorizontalAlignment.Left : HorizontalAlignment.Center;
        IntroButtons.Margin = wide ? new Thickness(-8, 10, 0, 0) : new Thickness(0, 10, 0, 0);
    }

    /// <summary>One entrance when the app opens: the left bar, then the dial, the headline, the mode strip, the card and the buttons rise in a short sequence.</summary>
    private void RunIntro()
    {
        Motion.Reveal(RailHost, 0, 0, 260);
        Motion.Reveal(IntroRing, 40, 12, 420);
        Motion.Reveal(IntroTitle, 140, 10, 340);
        Motion.Reveal(IntroHint, 180, 10, 340);
        Motion.Reveal(IntroChips, 220, 10, 340);
        Motion.Reveal(IntroCard, 270, 10, 340);
        Motion.Reveal(IntroButtons, 320, 8, 300);
    }

    /// <summary>
    /// A 96-unit tile of near-invisible noise (white at 0-2/255 opacity) laid over the big dark gradients: without it a smooth dark gradient shows visible bands.
    /// Made in memory (about 80 KB), so the program file does not grow.
    /// </summary>
    private ImageBrush MakeGrain()
    {
        var scale = Math.Max(1.0, VisualTreeHelper.GetDpi(this).DpiScaleX);
        var size = (int)Math.Round(96 * scale);
        var pixels = new int[size * size];
        var rng = new Random(7);
        for (var i = 0; i < pixels.Length; i++)
        {
            var v = rng.Next(3);                                  // 0, 1 or 2 out of 255
            pixels[i] = (v << 24) | (v << 16) | (v << 8) | v;     // premultiplied white
        }
        var bmp = new WriteableBitmap(size, size, 96 * scale, 96 * scale, PixelFormats.Pbgra32, null);
        bmp.WritePixels(new Int32Rect(0, 0, size, size), pixels, size * 4, 0);
        bmp.Freeze();
        var brush = new ImageBrush(bmp) { TileMode = TileMode.Tile, ViewportUnits = BrushMappingMode.Absolute, Viewport = new Rect(0, 0, 96, 96) };
        RenderOptions.SetCachingHint(brush, CachingHint.Cache);
        brush.Freeze();
        return brush;
    }

    /// <summary>
    /// "Uygulamayı kapat": closes the window and ends the process. No tray icon, no background task, no scheduled job is ever left behind.
    /// </summary>
    private void ExitApp()
    {
        if (_exiting) return;
        _exiting = true;
        _vm.SaveSettings();
        Application.Current.Shutdown();
    }

    private async Task OnLoadedAsync()
    {
#if DEBUG
        var di = Array.IndexOf(_args, "--demo");
        if (di >= 0) _vm.LoadDemo(di + 1 < _args.Length && !_args[di + 1].StartsWith("--") ? _args[di + 1] : "done");
#endif
        var skipInit = false;
#if DEBUG
        skipInit = Array.IndexOf(_args, "--no-init") >= 0;     // developer renders only: a release build always runs its start-up checks
#endif
        if (!skipInit) await _vm.InitAsync();
#if DEBUG
        await DebugHooks();
#endif
    }

#if DEBUG
    /// <summary>Developer aid: render the window to a PNG (--screenshot file.png [--demo idle|busy|done|warn] [--no-init] [--noapps] [--drawer] [--full] [--search text] [--hafif] [--stream] [--metrics] [--memory] [--profiles] [--settings] [--preview] [--picker] [--dialog] [--narrow] [--chips]).</summary>
    private async Task DebugHooks()
    {
        var i = Array.IndexOf(_args, "--screenshot");
        if (i < 0 || i + 1 >= _args.Length) return;
        bool Has(string f) => Array.IndexOf(_args, f) >= 0;

        // performance run: the window stays off-screen, behaves as if it were in front (so the breathing loop runs) and ends the process after N seconds;
        // the caller samples the process CPU time meanwhile
        var idle = Array.IndexOf(_args, "--idle-test");
        if (idle >= 0)
        {
            var seconds = idle + 1 < _args.Length && int.TryParse(_args[idle + 1], out var s) ? s : 20;
            Motion.ForceStill = false;
            _assumeActive = true;
            UpdateBreathing();
            File.WriteAllText(_args[i + 1] + ".idle.txt", $"motion allowed={Motion.Allowed} breathing running={_breathing.IsRunning} tier={System.Windows.Media.RenderCapability.Tier >> 16}");
            await Task.Delay(seconds * 1000);
            Environment.Exit(0);
        }

        if (!Has("--noapps")) _vm.LoadDemoApps();
        var ui = Array.IndexOf(_args, "--update");
        if (ui >= 0 && ui + 1 < _args.Length)
            switch (_args[ui + 1])
            {
                case "available": _vm.Update.Demo(Core.UpdateState.Available); break;
                case "busy": _vm.Update.Demo(Core.UpdateState.Downloading, 0.37); break;
                case "failed": _vm.Update.Demo(Core.UpdateState.Failed, 0, Core.UpdateFlow.FailedDownload); break;
            }
        if (Has("--chips")) { _vm.LoadDemoProfiles(); _vm.UseDemoProfile(); }
        if (Has("--profiles")) { _vm.LoadDemoProfiles(); _vm.GoTo(Page.Profiles); }
        if (Has("--settings")) _vm.GoTo(Page.Settings);
        if (Has("--hafif")) _vm.SelectProfile(Core.ProfileKind.Safe);
        if (Has("--stream")) _vm.SelectProfile(Core.ProfileKind.Stream);
        if (Has("--full")) _vm.SetTab(DrawerTab.Full);
        var si = Array.IndexOf(_args, "--search");
        if (si >= 0 && si + 1 < _args.Length) _vm.Search = _args[si + 1];
        if (Has("--metrics")) _vm.SetTab(DrawerTab.Metrics);
        if (Has("--sortcpu")) _vm.UsageSortCpu = true;
        if (Has("--memory")) { _vm.DrawerOpen = true; _vm.SetTab(DrawerTab.Memory); await Task.Delay(4500); }      // the two readings of the Kullanım list take two seconds
        if (Has("--drawer")) { _vm.DrawerOpen = true; await Task.Delay(700); }
        var sc = Array.IndexOf(_args, "--scroll");
        if (sc >= 0 && sc + 1 < _args.Length && double.TryParse(_args[sc + 1], out var scrollTo)) { SettingsPage.ScrollToVerticalOffset(scrollTo); await Task.Delay(400); }
        if (Has("--end")) { SelectionScroll.ScrollToEnd(); SettingsPage.ScrollToEnd(); await Task.Delay(400); }       // the list of the drawer (Öneriler) and the Ayarlar page, scrolled to their end
        if (Has("--narrow")) { Width = 940; Height = 620; UpdateLayout(); }   // smallest supported size
        if (Has("--info")) await _vm.RefreshInfoAsync();
        var path = _args[i + 1];

        if (Has("--reopen"))
        {
            // the language switch for real: choose English on Ayarlar, wait for the window that replaces this one and render THAT one
            _vm.SelectProfile(Core.ProfileKind.Stream);         // a choice of this run: it must survive the switch
            _vm.GoTo(Page.Settings);
            _vm.LanguageEnglish = true;
            await Task.Delay(3000);
            var fresh = Application.Current.MainWindow;
            var freshVm = fresh.DataContext as MainViewModel;
            File.WriteAllText(path + ".reopen.txt", $"replaced: {!ReferenceEquals(fresh, this)}; language: {Core.Loc.Language}; windows open: {Application.Current.Windows.Count}; title: {fresh.Title}; page settings: {freshVm?.PageSettings}; active profile: {freshVm?.TakeSessionSnapshot().ActiveKey}; saved language: {Core.SettingsStore.PeekLanguage()}");
            Render(fresh, path);
            Environment.Exit(0);
        }

        if (Has("--preview"))
        {
            // real read-only plan of this machine (Tam temizlik) → text dump + window screenshot
            var s = new Core.AppSettings();
            s.ApplyProfile(Core.ProfileKind.Maximum);
            var plan = await Task.Run(() => new Core.Planner(new Core.WindowsSystem()).Build(s, Core.SteamLocator.ResolveCs2()));
            File.WriteAllText(path + ".plan.txt",
                string.Join("\n", plan.Actions.Select(a => $"[{a.Kind}] enabled={a.Enabled} {a.Title}  |  {a.Detail}")) +
                "\n--- warnings ---\n" + string.Join("\n", plan.Warnings) + "\n--- notes ---\n" + string.Join("\n", plan.Notes));
            var pw = new PreviewWindow(new PreviewViewModel(plan)) { Owner = this, WindowStartupLocation = WindowStartupLocation.Manual, Left = -20000, Top = -20000, ShowActivated = false, ShowInTaskbar = false };
            pw.Show();
            await Task.Delay(1500);
            Render(pw, path);
        }
        else if (Has("--dialog"))
        {
            var dlg = new DialogWindow(
                Loc.T("Önceki çalıştırma tam geri yüklenemedi:\n• Güç planı geri yüklenemedi\n\nBu kayıt yok sayılıp bir kenara alınsın mı?\nHayır dersen kayıt korunur ve daha sonra tekrar denenebilir."),
                Loc.T("Geri yükleme tamamlanamadı"), MessageBoxButton.YesNo, MessageBoxImage.Warning, defaultNo: true)
            { Owner = this, WindowStartupLocation = WindowStartupLocation.Manual, Left = -20000, Top = -20000, ShowActivated = false, ShowInTaskbar = false };
            dlg.Show();
            await Task.Delay(1200);
            Render(dlg, path);
            dlg.Close();
        }
        else if (Has("--picker"))
        {
            var req = new PickerRequest
            {
                Title = Loc.T("Her zaman açık kalacak programları seç"),
                Hint = Loc.T("Çalışan programlar listelendi. Kapanmasını asla istemediklerini işaretle."),
                Items = new List<PickItemVM>
                {
                    new() { Key = "chrome", Title = "Google Chrome", Subtitle = Loc.T("1,2 GB bellek kullanıyor · ekranda açık"), IsChecked = true },
                    new() { Key = "spotify", Title = "Spotify", Subtitle = Loc.T("310 MB bellek kullanıyor · ekranda açık") },
                    new() { Key = "onedrive", Title = "Microsoft OneDrive", Subtitle = Loc.T("95 MB bellek kullanıyor · arka planda çalışıyor") },
                    new() { Key = "discord", Title = "Discord", Subtitle = Loc.T("480 MB bellek kullanıyor · ekranda açık") },
                },
            };
            var pk = new PickerWindow(req) { Owner = this, WindowStartupLocation = WindowStartupLocation.Manual, Left = -20000, Top = -20000, ShowActivated = false, ShowInTaskbar = false };
            pk.Show();
            await Task.Delay(1200);
            Render(pk, path);
        }
        else
        {
            await Task.Delay(1800);
            Render(this, path);
        }
        // a render in English also says which texts had no English (they stayed Turkish in the picture)
        if (Core.Loc.IsEnglish) File.WriteAllLines(path + ".missing.txt", Core.Loc.Missing.OrderBy(x => x));
        _exiting = true;
        Application.Current.Shutdown();
    }

    private static void Render(Window w, string path)
    {
        w.UpdateLayout();
        var rtb = new RenderTargetBitmap((int)w.ActualWidth, (int)w.ActualHeight, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(w);
        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(rtb));
        using var fs = File.Create(path);
        enc.Save(fs);
    }
#endif

    private void ShowPreview(PreviewViewModel pvm)
    {
        var w = new PreviewWindow(pvm) { Owner = this };
        w.ShowDialog();
    }

    /// <summary>Opens the checkbox picker and returns the keys the user ticked (empty when cancelled).</summary>
    private IReadOnlyList<string> ShowPicker(PickerRequest req)
    {
        var w = new PickerWindow(req) { Owner = this };
        return w.ShowDialog() == true ? w.Chosen : new List<string>();
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if ((_vm.IsBusy || _vm.Update.IsWorking) && !_exiting)
        {
            UiDialog.Show(_vm.Update.IsWorking ? _vm.Update.BusyText
                                               : Loc.T("Şu an bir işlem sürüyor. Bitmesini bekle; sonra Uygulamayı kapat'a bas."), "Antilag",
                MessageBoxButton.OK, MessageBoxImage.Information);
            e.Cancel = true;
            return;
        }
        // closing the window closes the app: there is no tray, no background mode
        _vm.SaveSettings();
        base.OnClosing(e);
    }
}
