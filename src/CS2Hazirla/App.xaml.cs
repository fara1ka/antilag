using System;
using System.Diagnostics;
using System.Threading;
using System.Windows;
using CS2Hazirla.Core;

namespace CS2Hazirla;

public partial class App : Application
{
    private static Mutex? _mutex;
    public static MainViewModel? Vm { get; private set; }

    /// <summary>DEBUG screenshot runs only: no dialog may ever open (an activated window would take the keyboard away from a running game).</summary>
    public static bool OffScreenRender { get; private set; }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // the long explanations in tooltips (undo, close) need more than the 5 s default; they also appear a little sooner
        System.Windows.Controls.ToolTipService.ShowDurationProperty.OverrideMetadata(typeof(DependencyObject), new FrameworkPropertyMetadata(20000));
        System.Windows.Controls.ToolTipService.InitialShowDelayProperty.OverrideMetadata(typeof(DependencyObject), new FrameworkPropertyMetadata(350));

        if (!AdminGate.Ensure(e.Args))
        {
            Shutdown();
            return;
        }

        // developer screenshot renders (DEBUG builds only) are off-screen and change nothing: they may run next to a game,
        // and they never touch the real %APPDATA%\CS2Hazirla (settings, journal): without an explicit CS2HAZIRLA_DATA they use a throw-away folder
        var offScreenRender = false;
#if DEBUG
        offScreenRender = Array.IndexOf(e.Args, "--screenshot") >= 0;
        OffScreenRender = offScreenRender;
        Motion.ForceStill = offScreenRender;        // a screenshot shows the finished state, never a frame of a transition
        if (offScreenRender)
        {
            // WPF swallows binding mistakes (a misspelled property just shows nothing): write them next to the screenshot so a render run reports them
            var i = Array.IndexOf(e.Args, "--screenshot");
            if (i + 1 < e.Args.Length)
            {
                var bindingLog = e.Args[i + 1] + ".bindings.txt";
                System.IO.File.WriteAllText(bindingLog, "");
                System.Diagnostics.PresentationTraceSources.Refresh();
                System.Diagnostics.PresentationTraceSources.DataBindingSource.Listeners.Add(new System.Diagnostics.TextWriterTraceListener(bindingLog));
                System.Diagnostics.PresentationTraceSources.DataBindingSource.Switch.Level = Environment.GetEnvironmentVariable("CS2H_BINDTRACE") == "all" ? System.Diagnostics.SourceLevels.All : System.Diagnostics.SourceLevels.Warning | System.Diagnostics.SourceLevels.Error;
                System.Diagnostics.Trace.AutoFlush = true;
            }
        }
        if (offScreenRender && string.IsNullOrEmpty(Environment.GetEnvironmentVariable("CS2HAZIRLA_DATA")))
            Environment.SetEnvironmentVariable("CS2HAZIRLA_DATA", System.IO.Path.Combine(System.IO.Path.GetTempPath(), "antilag-shot-" + Guid.NewGuid().ToString("N")));
#endif

        // CS2 already running: do nothing at all and leave (the app must never run next to the game)
        try
        {
            if (!offScreenRender && new WindowsSystem().FindPidByName(Catalog.Cs2ProcessName) != null)
            {
                UiDialog.Show("CS2 şu an çalışıyor. Oyun açıkken bu uygulama hiçbir şey yapmaz ve kapanır.\n\nOyunu kapattıktan sonra tekrar aç.",
                    "Antilag", MessageBoxButton.OK, MessageBoxImage.Information);
                Shutdown();
                return;
            }
        }
        catch { /* the check is best effort; the system layer has its own gate */ }

        var mutexName = @"Local\CS2Hazirla.SingleInstance";
#if DEBUG
        // developer screenshot runs must neither disturb nor be blocked by an app instance the user has open
        if (Array.IndexOf(e.Args, "--screenshot") >= 0) mutexName += ".shot";
#endif
        _mutex = new Mutex(true, mutexName, out var created);
        if (!created)
        {
            UiDialog.Show("Antilag zaten çalışıyor (görev yöneticisinde Antilag.exe).", "Antilag",
                MessageBoxButton.OK, MessageBoxImage.Information);
            Shutdown();
            return;
        }

        DispatcherUnhandledException += (_, args) =>
        {
            try { new FileLog().Error("Beklenmeyen hata: " + args.Exception); } catch { }
#if DEBUG
            if (OffScreenRender) { args.Handled = true; Environment.Exit(1); return; }
#endif
            UiDialog.Show("Beklenmeyen bir hata oluştu.\n\nAyrıntı günlük dosyasına yazıldı.",
                "Antilag", MessageBoxButton.OK, MessageBoxImage.Error);
            args.Handled = true;
            // no window means nothing is left to interact with: never keep an invisible elevated process alive
            if (MainWindow == null || !MainWindow.IsVisible) Shutdown();
        };

        try
        {
            Vm = new MainViewModel(Dispatcher);
            var win = new MainWindow(Vm, e.Args);
            MainWindow = win;
            win.Show();
        }
        catch (Exception ex)
        {
            try { new FileLog().Error("Başlatma hatası: " + ex); } catch { }
            UiDialog.Show("Uygulama başlatılamadı.\n\nAyrıntı günlük dosyasına yazıldı.", "Antilag", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown();
        }
    }
    protected override void OnExit(ExitEventArgs e)
    {
        try { _mutex?.ReleaseMutex(); } catch { }
        base.OnExit(e);
        // "Uygulamayı kapat" must leave nothing behind: end the process outright, no lingering thread can keep it alive
        Environment.Exit(e.ApplicationExitCode);
    }
}

/// <summary>
/// The app runs ONLY as administrator. The Release manifest already asks Windows for elevation; this gate
/// covers the cases where that is bypassed (e.g. RunAsInvoker): it relaunches itself elevated, or exits.
/// Debug builds (UI development) skip it unless --require-admin is passed.
/// </summary>
internal static class AdminGate
{
    public static bool Ensure(string[] args)
    {
        var enforce = true;
#if DEBUG
        enforce = Array.IndexOf(args, "--require-admin") >= 0;
#endif
        if (!enforce || WindowsSystem.IsElevated()) return true;

        var relaunch = Array.IndexOf(args, "--no-relaunch") < 0;
        if (relaunch)
        {
            try
            {
                Process.Start(new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = true, Verb = "runas" });
                return false;   // the elevated copy takes over
            }
            catch (System.ComponentModel.Win32Exception)
            {
                // user declined the UAC prompt: fall through to the message
            }
        }

        UiDialog.Show("Antilag yalnızca yönetici olarak çalışır (Windows hizmetlerini, güç planını ve ayarlarını değiştirmek için gerekli).\n\n" +
                        "Uygulamaya sağ tıklayıp 'Yönetici olarak çalıştır' de; Windows izin isterse 'Evet'e bas.",
            "Yönetici yetkisi gerekli", MessageBoxButton.OK, MessageBoxImage.Warning);
        return false;
    }
}

