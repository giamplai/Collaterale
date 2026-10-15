using Avalonia;

namespace Collaterale.App;

internal static class Program
{
    /// <summary>Set when a window closes; a failed or unsaved command-line merge must make the process exit non-zero for git.</summary>
    public static int ExitCode { get; set; }

    [STAThread]
    public static int Main(string[] args)
    {
        // --diff prints and exits: no window, so it works over ssh, in scripts and as GIT_EXTERNAL_DIFF
        if (Collaterale.Core.Cli.CliDiff.Wants(args))
        {
            return Collaterale.Core.Cli.CliDiff.Run(args, Console.Out, Console.Error);
        }

        // file-manager helpers: remember a left side, or pair it with the current item
        if (Collaterale.Core.Cli.ShellMark.Handle(args, Collaterale.Core.Cli.ShellMark.DefaultMarkerPath, out var marked, out var note, out var failed))
        {
            if (marked is null || failed)
            {
                (failed ? Console.Error : Console.Out).WriteLine(note);
                return failed ? 1 : 0;
            }

            args = marked;
        }

        // --help and --version never need a window
        if (Collaterale.Core.Cli.LaunchParser.Informational(args, AppInfo.Name, AppInfo.Version) is { } text)
        {
            Console.WriteLine(text);
            return 0;
        }

        // quick compares and automatic merges answer through the exit code, with no window
        if (Collaterale.Core.Cli.CliHeadless.TryRun(Collaterale.Core.Cli.LaunchParser.Parse(args), Console.Out, Console.Error, out var headlessCode, new Collaterale.Core.Settings.SettingsStore(Collaterale.Core.Settings.SettingsStore.DefaultPath).Load()))
        {
            return headlessCode;
        }

        var lifetimeCode = BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        Collaterale.Core.Folders.ArchiveExtractor.CleanUp();
        return ExitCode != 0 ? ExitCode : lifetimeCode;
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>().UsePlatformDetect().WithInterFont().LogToTrace();
}
