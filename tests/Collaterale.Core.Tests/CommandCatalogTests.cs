using Collaterale.Core.Settings;

namespace Collaterale.Core.Tests;

public class CommandCatalogTests
{
    [Theory]
    [InlineData("ctrl+f", "Ctrl+F")]
    [InlineData("SHIFT+ctrl+f3", "Ctrl+Shift+F3")]
    [InlineData("Alt + Left", "Alt+Left")]
    [InlineData(" f8 ", "F8")]
    [InlineData("cmd+s", "Meta+S")]
    [InlineData("Ctrl+Alt+Shift+Delete", "Ctrl+Alt+Shift+Delete")]
    public void Shortcuts_are_written_in_one_canonical_form(string typed, string canonical)
        => Assert.Equal(canonical, CommandCatalog.Normalize(typed));

    [Theory]
    [InlineData("")]
    [InlineData("Ctrl+")]
    [InlineData("Ctrl+Shift")]
    [InlineData("Ctrl+F+G")]
    [InlineData("Bogus+F")]
    public void A_shortcut_needs_exactly_one_key_and_known_modifiers(string typed)
        => Assert.Null(CommandCatalog.Normalize(typed));

    [Fact]
    public void Every_command_has_a_unique_id_a_title_and_a_description()
    {
        Assert.NotEmpty(CommandCatalog.All);
        Assert.Equal(CommandCatalog.All.Count, CommandCatalog.All.Select(c => c.Id).Distinct().Count());
        Assert.All(CommandCatalog.All, c => { Assert.False(string.IsNullOrWhiteSpace(c.Title)); Assert.False(string.IsNullOrWhiteSpace(c.Description)); });
        Assert.All(CommandCatalog.All.Where(c => c.Default is not null), c => Assert.Equal(c.Default, CommandCatalog.Normalize(c.Default!)));
    }

    [Fact]
    public void The_default_shortcuts_do_not_clash_inside_one_view()
    {
        foreach (var view in Enum.GetValues<CommandView>())
        {
            var used = CommandCatalog.All.Where(c => c.Default is not null && c.View == view).GroupBy(c => c.Default);
            Assert.All(used, g => Assert.True(g.Count() == 1, $"{view}: {g.Key} is used by {string.Join(", ", g.Select(c => c.Id))}"));
        }
    }

    [Fact]
    public void The_effective_shortcut_is_the_default_unless_the_user_changed_or_removed_it()
    {
        var prefs = new Preferences();
        Assert.Equal("Ctrl+F", CommandCatalog.Effective(prefs, "text.find"));
        prefs.Commands.Add(new CommandBinding { Command = "text.find", Shortcut = "ctrl+shift+f" });
        Assert.Equal("Ctrl+Shift+F", CommandCatalog.Effective(prefs, "text.find"));
        prefs.Commands[0].Shortcut = "";
        Assert.Equal("", CommandCatalog.Effective(prefs, "text.find"));
        Assert.Null(CommandCatalog.Effective(prefs, "no.such.command"));
    }

    [Fact]
    public void Assigning_a_shortcut_reports_the_command_that_already_has_it()
    {
        var prefs = new Preferences();
        Assert.Equal("text.find", CommandCatalog.Owner(prefs, "Ctrl+F", CommandView.Text)?.Id);
        Assert.Null(CommandCatalog.Owner(prefs, "Ctrl+Shift+Alt+Q", CommandView.Text));
        Assert.Null(CommandCatalog.Owner(prefs, "Ctrl+F", CommandView.Merge));          // another view: no clash

        var result = CommandCatalog.Assign(prefs, "text.replace", "ctrl+f", reassign: false);
        Assert.Equal(AssignResult.Taken, result.Status);
        Assert.Equal("text.find", result.Other);
        Assert.Equal("Ctrl+H", CommandCatalog.Effective(prefs, "text.replace"));      // refused: nothing changed

        result = CommandCatalog.Assign(prefs, "text.replace", "ctrl+f", reassign: true);
        Assert.Equal(AssignResult.Done, result.Status);
        Assert.Equal("Ctrl+F", CommandCatalog.Effective(prefs, "text.replace"));
        Assert.Equal("", CommandCatalog.Effective(prefs, "text.find"));               // the other one lost it
    }

    [Fact]
    public void Invalid_shortcuts_are_rejected_and_defaults_can_be_restored()
    {
        var prefs = new Preferences();
        Assert.Equal(AssignResult.Invalid, CommandCatalog.Assign(prefs, "text.find", "Ctrl+", reassign: false).Status);
        CommandCatalog.Assign(prefs, "text.find", "F2", reassign: false);
        Assert.Equal("F2", CommandCatalog.Effective(prefs, "text.find"));
        CommandCatalog.Restore(prefs, "text.find");
        Assert.Equal("Ctrl+F", CommandCatalog.Effective(prefs, "text.find"));
        Assert.Empty(prefs.Commands);
    }

    [Fact]
    public void The_search_box_filters_by_name_or_description()
    {
        Assert.Contains(CommandCatalog.Search("find", CommandView.Text), c => c.Id == "text.find");
        Assert.All(CommandCatalog.Search("merge", null), c => Assert.Contains("merge", (c.Title + " " + c.Description).ToLowerInvariant()));
        Assert.Equal(CommandCatalog.All.Count(c => c.View == CommandView.Folder), CommandCatalog.Search("", CommandView.Folder).Count);
    }
}
