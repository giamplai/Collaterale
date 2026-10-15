namespace Collaterale.Core.Cli;

/// <summary>
/// Two-step comparison for file managers that cannot hand over two selected items at once (Windows Explorer, Finder):
/// "Select as left side" remembers a path, "Compare with the selected" pairs it with the current item.
/// </summary>
public static class ShellMark
{
    public const string SelectLeft = "--select-left";
    public const string CompareSelected = "--compare-selected";

    public static string DefaultMarkerPath => Path.Combine(Path.GetDirectoryName(Settings.SettingsStore.DefaultPath)!, "selected-left.txt");

    public static bool Wants(IReadOnlyList<string> args) => args.Count > 0 && args[0] is SelectLeft or CompareSelected;

    /// <summary>
    /// Applies a shell command. Returns true when it was one: <paramref name="rewritten"/> is then the argument list to continue with
    /// (null when nothing more is to be done) and <paramref name="message"/> says what happened.
    /// </summary>
    public static bool Handle(IReadOnlyList<string> args, string markerPath, out string[]? rewritten, out string message, out bool failed)
    {
        rewritten = null; message = ""; failed = false;
        if (!Wants(args))
        {
            return false;
        }

        if (args.Count != 2)
        {
            message = $"{args[0]} needs exactly one path."; failed = true;
            return true;
        }

        var path = Path.GetFullPath(args[1]);
        if (args[0] == SelectLeft)
        {
            if (!File.Exists(path) && !Directory.Exists(path))
            {
                message = $"'{path}' does not exist."; failed = true;
                return true;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(markerPath)!);
            File.WriteAllText(markerPath, path);
            message = $"Selected '{path}' as the left side. Now choose 'Compare with the selected' on the other item.";
            return true;
        }

        var left = File.Exists(markerPath) ? File.ReadAllText(markerPath).Trim() : "";
        if (left.Length == 0 || !(File.Exists(left) || Directory.Exists(left)))
        {
            message = "Nothing is selected yet (or it no longer exists). Choose 'Select as left side' first."; failed = true;
            return true;
        }

        rewritten = [left, path];
        message = $"Comparing '{left}' with '{path}'.";
        return true;
    }
}
