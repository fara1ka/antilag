using System;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Windows.Data;
using System.Windows.Input;

namespace CS2Hazirla;

public abstract class ObservableObject : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    protected bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (Equals(field, value)) return false;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        return true;
    }

    protected void Raise([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public class RelayCommand : ICommand
{
    private readonly Action<object?> _run;
    private readonly Func<object?, bool>? _can;

    public RelayCommand(Action<object?> run, Func<object?, bool>? can = null) { _run = run; _can = can; }
    public RelayCommand(Action run, Func<bool>? can = null) : this(_ => run(), can == null ? null : _ => can()) { }

    public event EventHandler? CanExecuteChanged;
    public bool CanExecute(object? parameter) => _can?.Invoke(parameter) ?? true;
    public void Execute(object? parameter) => _run(parameter);
    public void Refresh() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}

/// <summary>Visible when the bound bool is false (the "other" half of a two-state area, without a second property).</summary>
public sealed class NotBoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is true ? System.Windows.Visibility.Collapsed : System.Windows.Visibility.Visible;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>
/// Animation lengths used by the whole theme, chosen once at startup. When Windows has "animate controls and elements inside windows"
/// switched off, every transition becomes instant (hover and press states still change, they just do not move) and the busy ring stands still.
/// </summary>
public sealed class MotionDictionary : System.Windows.ResourceDictionary
{
    public MotionDictionary()
    {
        var animated = System.Windows.SystemParameters.ClientAreaAnimation;
        foreach (var (key, ms) in new (string, double)[]
                 { ("D06", 60), ("D07", 70), ("D08", 80), ("D10", 100), ("D12", 120), ("D14", 140), ("D16", 160), ("D18", 180),
                   ("D20", 200), ("D22", 220), ("D25", 250), ("D30", 300) })
            this[key] = new System.Windows.Duration(animated ? TimeSpan.FromMilliseconds(ms) : TimeSpan.Zero);
        // the spinner repeats forever: a zero length would spin at full CPU speed, so a still ring is a one-day rotation
        this["DSpin"] = new System.Windows.Duration(animated ? TimeSpan.FromSeconds(1.15) : TimeSpan.FromDays(1));
    }
}
