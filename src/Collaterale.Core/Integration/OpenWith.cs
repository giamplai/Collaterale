using System.Diagnostics;
using Collaterale.Core.Settings;

namespace Collaterale.Core.Integration;

/// <summary>Starts an external program on a file (the "Open with" menu).</summary>
public static class OpenWith
{
    public static bool IsUsable(OpenWithApp app) => !string.IsNullOrWhiteSpace(app.Name) && !string.IsNullOrWhiteSpace(app.Command);

    /// <summary>The arguments with <c>%1</c> and <c>%2</c> replaced; a quoted group stays one argument even with spaces in it. With no arguments set, the file alone is passed.</summary>
    public static IReadOnlyList<string> BuildArguments(OpenWithApp app, string file, string? other)
    {
        var template = string.IsNullOrWhiteSpace(app.Arguments) ? "\"%1\"" : app.Arguments;
        var result = new List<string>();
        foreach (var token in Split(template))
        {
            if (token.Contains("%2", StringComparison.Ordinal) && other is null) { continue; }   // no second file: leave that argument out
            result.Add(token.Replace("%1", file, StringComparison.Ordinal).Replace("%2", other ?? "", StringComparison.Ordinal));
        }

        return result;
    }

    private static List<string> Split(string text)
    {
        var list = new List<string>();
        var sb = new System.Text.StringBuilder();
        var quoted = false;
        var any = false;
        foreach (var c in text)
        {
            if (c == '"') { quoted = !quoted; any = true; }
            else if (char.IsWhiteSpace(c) && !quoted)
            {
                if (any) { list.Add(sb.ToString()); sb.Clear(); any = false; }
            }
            else { sb.Append(c); any = true; }
        }

        if (any) { list.Add(sb.ToString()); }
        return list;
    }

    /// <summary>Starts the program; the error text is returned instead of thrown (a missing program is an everyday event).</summary>
    public static string? Start(OpenWithApp app, string file, string? other = null)
    {
        try
        {
            var info = new ProcessStartInfo(app.Command) { UseShellExecute = false };
            foreach (var a in BuildArguments(app, file, other)) { info.ArgumentList.Add(a); }
            using var p = Process.Start(info);
            return p is null ? $"'{app.Command}' did not start." : null;
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or IOException)
        {
            return $"Cannot start '{app.Command}': {ex.Message}";
        }
    }
}
