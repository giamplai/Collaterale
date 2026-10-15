namespace Collaterale.Core.Integration;

public sealed record ShellFile(string Path, string Content, bool Executable);

public sealed record RegistryValue(string SubKey, string Value);

/// <summary>The entries that put Collaterale in a file manager's right-click menu.</summary>
public static class ShellIntegration
{
    public static IReadOnlyList<ShellFile> FilesFor(AutoStartPlatform platform, string exe, string home)
    {
        if (platform != AutoStartPlatform.Linux) { return []; }
        var share = home.TrimEnd((char)47, (char)92) + "/.local/share";
        return
        [
            new ShellFile(share + "/kio/servicemenus/collaterale-dolphin.desktop",
                $"[Desktop Entry]\nType=Service\nServiceTypes=KonqPopupMenu/Plugin\nMimeType=application/octet-stream;inode/directory;text/plain;\nActions=compare;\nX-KDE-Submenu=Collaterale\n\n[Desktop Action compare]\nName=Compare the two selected items\nIcon=collaterale\nExec=\"{exe}\" %F\n", Executable: false),
            new ShellFile(share + "/nautilus/scripts/Compare with Collaterale",
                $"#!/usr/bin/env sh\n# Select two files or two folders, then Scripts > Compare with Collaterale\nset -e\nif [ -n \"$NAUTILUS_SCRIPT_SELECTED_FILE_PATHS\" ]; then\n  printf '%s\\n' \"$NAUTILUS_SCRIPT_SELECTED_FILE_PATHS\" | {{\n    IFS= read -r a; IFS= read -r b\n    exec \"{exe}\" \"$a\" \"$b\"\n  }}\nelse\n  exec \"{exe}\" \"$@\"\nfi\n", Executable: true),
        ];
    }

    /// <summary>The per-user registry entries that add "select as left side" and "compare with the selected" to Explorer.</summary>
    public static IReadOnlyList<RegistryValue> WindowsKeys(string exe)
    {
        var list = new List<RegistryValue>();
        foreach (var scope in new[] { "*", "Directory" })
        {
            list.Add(new RegistryValue($@"Software\Classes\{scope}\shell\CollateraleSelect", "Collaterale: select as left side"));
            list.Add(new RegistryValue($@"Software\Classes\{scope}\shell\CollateraleSelect\command", $"\"{exe}\" --select-left \"%1\""));
            list.Add(new RegistryValue($@"Software\Classes\{scope}\shell\CollateraleCompare", "Collaterale: compare with the selected"));
            list.Add(new RegistryValue($@"Software\Classes\{scope}\shell\CollateraleCompare\command", $"\"{exe}\" --compare-selected \"%1\""));
        }

        return list;
    }

    public static bool IsInstalled(AutoStartPlatform platform, string home)
    {
        var files = FilesFor(platform, "x", home);
        if (files.Count > 0) { return files.All(f => File.Exists(f.Path)); }
        return platform == AutoStartPlatform.Windows && OperatingSystem.IsWindows() && WindowsInstalled();
    }

    public static string Install(AutoStartPlatform platform, string exe, string home)
    {
        if (platform == AutoStartPlatform.Mac) { return "On macOS the Finder integration comes with the app: drag Collaterale.app to Applications, then use Services ▸ Compare with Collaterale."; }
        if (platform == AutoStartPlatform.Windows)
        {
            if (!OperatingSystem.IsWindows()) { return "The Explorer entries can only be installed on Windows."; }
            WriteWindows(exe, remove: false);
            return "The Explorer context menu entries were installed for your user.";
        }

        foreach (var f in FilesFor(platform, exe, home))
        {
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(f.Path)!);
            File.WriteAllText(f.Path, f.Content);
            if (f.Executable && !OperatingSystem.IsWindows()) { File.SetUnixFileMode(f.Path, (UnixFileMode)0b111_101_101); }
        }

        return "The file-manager entries were installed (Dolphin and Nautilus). Restart the file manager to see them.";
    }

    public static void Uninstall(AutoStartPlatform platform, string exe, string home)
    {
        foreach (var f in FilesFor(platform, exe, home))
        {
            if (File.Exists(f.Path)) { File.Delete(f.Path); }
        }

        if (platform == AutoStartPlatform.Windows && OperatingSystem.IsWindows()) { WriteWindows(exe, remove: true); }
    }

    /// <summary>Installs for the current user on whatever system this is.</summary>
    public static string InstallForCurrentUser(string exe)
        => Install(AutoStart.Current, exe, Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));

    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private static void WriteWindows(string exe, bool remove)
    {
        if (remove)
        {
            foreach (var scope in new[] { "*", "Directory" })
            {
                foreach (var name in new[] { "CollateraleSelect", "CollateraleCompare" })
                {
                    Microsoft.Win32.Registry.CurrentUser.DeleteSubKeyTree($@"Software\Classes\{scope}\shell\{name}", throwOnMissingSubKey: false);
                }
            }

            return;
        }

        foreach (var v in WindowsKeys(exe))
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(v.SubKey);
            key.SetValue("", v.Value);
        }
    }

    private static bool WindowsInstalled()
    {
        if (!OperatingSystem.IsWindows()) { return false; }
        using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Classes\Directory\shell\CollateraleCompare");
        return key is not null;
    }
}
