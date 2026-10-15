namespace Collaterale.Core.Integration;

public enum AutoStartPlatform { Linux, Mac, Windows }

/// <summary>A file that makes the clipboard watcher start when the user logs in.</summary>
public sealed record AutoStartEntry(string Path, string Content);

/// <summary>Starting <c>collaterale --clipboard</c> at login: a desktop entry on Linux, a launch agent on macOS, the per-user Run key on Windows.</summary>
public static class AutoStart
{
    public static AutoStartPlatform Current => OperatingSystem.IsWindows() ? AutoStartPlatform.Windows : OperatingSystem.IsMacOS() ? AutoStartPlatform.Mac : AutoStartPlatform.Linux;

    public static string WindowsCommand(string exe) => $"\"{exe}\" --clipboard";

    public static AutoStartEntry? EntryFor(AutoStartPlatform platform, string exe, string home) => platform switch
    {
        AutoStartPlatform.Linux => new AutoStartEntry(home.TrimEnd((char)47, (char)92) + "/.config/autostart/collaterale-clipboard.desktop",
            $"[Desktop Entry]\nType=Application\nName=Collaterale clipboard compare\nExec=\"{exe}\" --clipboard\nX-GNOME-Autostart-enabled=true\nTerminal=false\n"),
        AutoStartPlatform.Mac => new AutoStartEntry(home.TrimEnd((char)47, (char)92) + "/Library/LaunchAgents/collaterale.clipboard.plist",
            $"""
            <?xml version="1.0" encoding="UTF-8"?>
            <!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
            <plist version="1.0">
            <dict>
              <key>Label</key><string>collaterale.clipboard</string>
              <key>ProgramArguments</key>
              <array>
                <string>{System.Security.SecurityElement.Escape(exe)}</string>
                <string>--clipboard</string>
              </array>
              <key>RunAtLoad</key><true/>
            </dict>
            </plist>
            """),
        _ => null,
    };

    public static bool IsEnabled(AutoStartPlatform platform, string home)
    {
        if (EntryFor(platform, "x", home) is { } e) { return File.Exists(e.Path); }
        return OperatingSystem.IsWindows() && WindowsRunKeyExists();
    }

    public static void Set(AutoStartPlatform platform, bool enabled, string exe, string home)
    {
        if (EntryFor(platform, exe, home) is { } e)
        {
            if (enabled)
            {
                Directory.CreateDirectory(System.IO.Path.GetDirectoryName(e.Path)!);
                File.WriteAllText(e.Path, e.Content);
            }
            else if (File.Exists(e.Path))
            {
                File.Delete(e.Path);
            }

            return;
        }

        if (OperatingSystem.IsWindows()) { SetWindowsRunKey(enabled, exe); }
    }

    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private static void SetWindowsRunKey(bool enabled, string exe)
    {
        using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", writable: true);
        if (key is null) { return; }
        if (enabled) { key.SetValue("CollateraleClipboard", WindowsCommand(exe)); } else { key.DeleteValue("CollateraleClipboard", throwOnMissingValue: false); }
    }

    private static bool WindowsRunKeyExists()
    {
        if (!OperatingSystem.IsWindows()) { return false; }
        using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run");
        return key?.GetValue("CollateraleClipboard") is not null;
    }
}
