namespace Collaterale.Core.Settings;

public enum CommandView { Text, Merge, Folder }

/// <summary>A command the user can bind to a key and place in a menu or toolbar.</summary>
public sealed record CommandInfo(string Id, CommandView View, string Title, string Description, string? Default);

public enum AssignResult { Done, Invalid, Taken }

public sealed record Assignment(AssignResult Status, string? Other = null);

/// <summary>The list of commands with their default shortcuts, and the user's changes (kept in <see cref="Preferences.Commands"/>).</summary>
public static class CommandCatalog
{
    public static IReadOnlyList<CommandInfo> All { get; } =
    [
        new("text.next", CommandView.Text, "Next difference", "Go to the next difference section.", "Alt+Down"),
        new("text.prev", CommandView.Text, "Previous difference", "Go to the previous difference section.", "Alt+Up"),
        new("text.next2", CommandView.Text, "Next difference (F8)", "Go to the next difference section.", "F8"),
        new("text.prev2", CommandView.Text, "Previous difference (Shift+F8)", "Go to the previous difference section.", "Shift+F8"),
        new("text.nextitem", CommandView.Text, "Next individual difference", "Go to the next changed word or line (inside long lines with several differences).", "F7"),
        new("text.previtem", CommandView.Text, "Previous individual difference", "Go to the previous changed word or line.", "Shift+F7"),
        new("text.nextfile", CommandView.Text, "Next file with differences", "Open the next file with differences of the folder this file came from.", "Ctrl+Alt+Down"),
        new("text.prevfile", CommandView.Text, "Previous file with differences", "Open the previous file with differences of the folder this file came from.", "Ctrl+Alt+Up"),
        new("text.copyright", CommandView.Text, "Copy to right", "Replace this difference on the right with the left side.", "Alt+Right"),
        new("text.copyleft", CommandView.Text, "Copy to left", "Replace this difference on the left with the right side.", "Alt+Left"),
        new("text.undo", CommandView.Text, "Undo", "Undo the last edit or copy.", "Ctrl+Z"),
        new("text.redo", CommandView.Text, "Redo", "Redo what was undone.", "Ctrl+Y"),
        new("text.reload", CommandView.Text, "Reload", "Read both files again.", "F5"),
        new("text.goto", CommandView.Text, "Go to line", "Jump to a line number.", "Ctrl+G"),
        new("text.find", CommandView.Text, "Find", "Search for text in the files.", "Ctrl+F"),
        new("text.replace", CommandView.Text, "Replace", "Find and replace text.", "Ctrl+H"),
        new("text.findnext", CommandView.Text, "Find next", "Continue the search forward.", "F3"),
        new("text.findprev", CommandView.Text, "Find previous", "Continue the search backward.", "Shift+F3"),
        new("text.isolate", CommandView.Text, "Isolate selected lines", "Realign the selected lines on their own, with empty space opposite them.", "Ctrl+Alt+I"),
        new("text.align", CommandView.Text, "Align with…", "Choose the line of one pane, then click the line of the other pane to place opposite it.", "Ctrl+Alt+A"),
        new("text.swap", CommandView.Text, "Swap sides", "Exchange the left and right files.", null),
        new("merge.next", CommandView.Merge, "Next change", "Go to the next change.", "Alt+Down"),
        new("merge.prev", CommandView.Merge, "Previous change", "Go to the previous change.", "Alt+Up"),
        new("merge.nextconflict", CommandView.Merge, "Next conflict", "Go to the next unresolved conflict.", "F8"),
        new("merge.takeleft", CommandView.Merge, "Take left", "Use the left version for this section.", "Alt+Left"),
        new("merge.takeright", CommandView.Merge, "Take right", "Use the right version for this section.", "Alt+Right"),
        new("merge.takecenter", CommandView.Merge, "Take center", "Use the common ancestor for this section.", null),
        new("merge.takeboth", CommandView.Merge, "Take both", "Left lines followed by right lines.", null),
        new("merge.save", CommandView.Merge, "Save", "Write the merged result.", "Ctrl+S"),
        new("folder.refresh", CommandView.Folder, "Refresh", "Compare the folders again.", "F5"),
        new("folder.copyright", CommandView.Folder, "Copy to right", "Copy the selection to the right folder.", "Ctrl+Right"),
        new("folder.copyleft", CommandView.Folder, "Copy to left", "Copy the selection to the left folder.", "Ctrl+Left"),
        new("folder.expand", CommandView.Folder, "Expand all", "Open every folder.", "Ctrl+E"),
    ];

    private static readonly string[] Modifiers = ["Ctrl", "Alt", "Shift", "Meta"];

    /// <summary>The shortcut in one spelling (<c>Ctrl+Alt+Shift+Meta+Key</c>), or null when it is not one modifier-list plus exactly one key.</summary>
    public static string? Normalize(string? typed)
    {
        if (string.IsNullOrWhiteSpace(typed)) { return null; }
        var mods = new HashSet<string>();
        string? key = null;
        foreach (var raw in typed.Split('+', StringSplitOptions.TrimEntries))
        {
            if (raw.Length == 0) { return null; }
            var m = raw.ToLowerInvariant() switch
            {
                "ctrl" or "control" => "Ctrl",
                "alt" or "option" => "Alt",
                "shift" => "Shift",
                "meta" or "cmd" or "command" or "win" or "super" => "Meta",
                _ => null,
            };
            if (m is not null) { mods.Add(m); continue; }
            if (key is not null) { return null; }
            key = raw.Length == 1 ? raw.ToUpperInvariant() : char.ToUpperInvariant(raw[0]) + raw[1..].ToLowerInvariant();
        }

        if (key is null) { return null; }
        if (key.Length > 1 && !char.IsLetter(key[0]) && !char.IsDigit(key[0])) { return null; }
        if (key.Length > 1 && key.Any(c => !char.IsLetterOrDigit(c))) { return null; }
        if (key.Length > 1 && key.Count(char.IsUpper) > 1) { return null; }
        return string.Join('+', Modifiers.Where(mods.Contains).Append(key));
    }

    private static CommandBinding? Stored(Preferences p, string id) => p.Commands.FirstOrDefault(c => c.Command == id);

    /// <summary>The shortcut a command has now: the default, the user's choice, or empty when the user removed it; null for an unknown command.</summary>
    public static string? Effective(Preferences p, string id)
    {
        var info = All.FirstOrDefault(c => c.Id == id);
        if (info is null) { return null; }
        // a settings file edited by hand may spell it differently, or wrongly: a wrong one falls back to the default
        return Stored(p, id) is { Shortcut: { } s } ? (s.Length == 0 ? "" : Normalize(s) ?? info.Default ?? "") : info.Default ?? "";
    }

    /// <summary>The command of that view that already uses the shortcut.</summary>
    public static CommandInfo? Owner(Preferences p, string shortcut, CommandView view)
    {
        var key = Normalize(shortcut);
        return key is null ? null : All.FirstOrDefault(c => c.View == view && Effective(p, c.Id) == key);
    }

    public static Assignment Assign(Preferences p, string id, string shortcut, bool reassign)
    {
        var info = All.FirstOrDefault(c => c.Id == id);
        var key = Normalize(shortcut);
        if (info is null || key is null) { return new Assignment(AssignResult.Invalid); }
        if (Owner(p, key, info.View) is { } other && other.Id != id)
        {
            if (!reassign) { return new Assignment(AssignResult.Taken, other.Id); }
            Set(p, other.Id, "");
        }

        Set(p, id, key);
        return new Assignment(AssignResult.Done);
    }

    private static void Set(Preferences p, string id, string shortcut)
    {
        var b = Stored(p, id);
        if (b is null) { p.Commands.Add(b = new CommandBinding { Command = id }); }
        b.Shortcut = shortcut;
        Tidy(p, b);
    }

    /// <summary>Back to the default shortcut.</summary>
    public static void Restore(Preferences p, string id)
    {
        if (Stored(p, id) is { } b) { b.Shortcut = null; Tidy(p, b); }
    }

    /// <summary>A binding that says nothing the defaults do not already say is not kept.</summary>
    private static void Tidy(Preferences p, CommandBinding b)
    {
        if (b.Shortcut is null) { p.Commands.Remove(b); }
    }

    public static IReadOnlyList<CommandInfo> Search(string text, CommandView? view)
        => [.. All.Where(c => (view is null || c.View == view) && (text.Length == 0 || $"{c.Title} {c.Description}".Contains(text, StringComparison.OrdinalIgnoreCase)))];
}
