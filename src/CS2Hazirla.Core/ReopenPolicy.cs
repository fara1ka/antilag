namespace CS2Hazirla.Core;

/// <summary>
/// "Geri yükle" starts every closed program again, as far as that is safe: a program is started by its own .exe path,
/// without arguments, so anything that only works with arguments or is not meant to be started by hand is left out
/// (and listed for the user instead).
/// </summary>
public static class ReopenPolicy
{
    // These need command-line parameters or restart themselves: GoodbyeDPI/Zapret without their switches do nothing useful,
    // updaters come back on their own schedule, anti-cheat components are started by their game.
    private static readonly HashSet<string> NeverReopenedOptions = new() { "proc.updaters", "proc.dpi", "proc.bypass", "proc.anticheat" };

    // Started without arguments these open a console or a blank interpreter.
    private static readonly HashSet<string> Interpreters = new(StringComparer.Ordinal)
    {
        "node", "nodejs", "python", "python3", "pythonw", "py", "java", "javaw", "javaws", "dotnet", "cmd", "powershell", "pwsh",
        "wscript", "cscript", "ruby", "perl", "php", "deno", "bun", "wsl", "wslhost", "bash", "sh", "ssh", "git", "npm", "npx",
    };

    // Pieces that are not meant to be started by hand: crash reporters, updaters, helper/renderer processes, Windows-service
    // executables. The program they belong to (which is reopened) starts them again when it needs them.
    // Tray agents are deliberately NOT here: lghub_agent or an iCUE tray process is the only part of that program that runs.
    private static readonly string[] HelperTokens =
    {
        "helper", "crash", "updat", "service", "svc", "broker", "renderer", "setup", "install",
    };

    /// <summary>A piece that belongs to a program rather than being one (crash reporter, updater, renderer, service executable...).</summary>
    public static bool IsHelper(string normalizedName) => HelperTokens.Any(t => normalizedName.Contains(t, StringComparison.Ordinal));

    /// <param name="normalizedName">Process name without .exe, lower-case.</param>
    /// <param name="optionId">The option that closed it (e.g. "proc.cloud").</param>
    public static bool CanReopen(string normalizedName, string? path, string? optionId)
    {
        if (!StateGuard.IsSafeRestartPath(path)) return false;
        // Store (MSIX) apps cannot be started by their package path
        if (path!.Contains(@"\WindowsApps\", StringComparison.OrdinalIgnoreCase)) return false;
        if (optionId != null && NeverReopenedOptions.Contains(optionId)) return false;
        if (Interpreters.Contains(normalizedName)) return false;
        return !IsHelper(normalizedName);
    }
}
