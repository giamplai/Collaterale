using Avalonia.Controls;
using Avalonia.Input;
using Collaterale.Core.Settings;

namespace Collaterale.App.Controls;

/// <summary>Gives a view the keyboard shortcuts of its commands: the defaults of the catalog, or what the user chose in the options.</summary>
public static class ShortcutBinder
{
    /// <summary>Raised after the options changed, so every open view binds its keys again.</summary>
    public static event Action? Changed;

    public static void Refresh() => Changed?.Invoke();

    /// <summary>Replaces the key bindings of a view. Keys that are not in the catalog (<paramref name="fixedKeys"/>) are kept as they are.</summary>
    public static void Apply(Control view, Preferences prefs, IReadOnlyDictionary<string, (System.Windows.Input.ICommand Command, object? Parameter)> commands, IEnumerable<(string Gesture, System.Windows.Input.ICommand Command)>? fixedKeys = null)
    {
        view.KeyBindings.Clear();
        foreach (var (id, (command, parameter)) in commands)
        {
            if (CommandCatalog.Effective(prefs, id) is not { Length: > 0 } gesture) { continue; }
            try
            {
                if (KeyGesture.Parse(gesture) is { } g) { var kb = new KeyBinding { Gesture = g, Command = command }; if (parameter is not null) { kb.CommandParameter = parameter; } view.KeyBindings.Add(kb); }
            }
            catch (Exception ex) when (ex is ArgumentException or FormatException)
            {
                // a shortcut that Avalonia cannot read is simply not bound
            }
        }

        foreach (var (gesture, command) in fixedKeys ?? [])
        {
            if (KeyGesture.Parse(gesture) is { } g) { view.KeyBindings.Add(new KeyBinding { Gesture = g, Command = command }); }
        }
    }
}
