using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Collaterale.Core.Merge;

namespace Collaterale.App.ViewModels;

/// <summary>Shortcuts for working through a merge: accept automatic changes, resolve many sections at once, undo one choice, and a panel with the current line in full.</summary>
public partial class MergeViewModel
{
    // ───────────── accepting and resetting choices ─────────────

    /// <summary>The choice that gives what the automatic merge already puts in the result for a section nobody disputes.</summary>
    private static MergeChoice? AutomaticChoice(MergeRegionKind kind) => kind switch
    {
        MergeRegionKind.LeftChanged or MergeRegionKind.BothSame => MergeChoice.Left,
        MergeRegionKind.RightChanged => MergeChoice.Right,
        _ => null,
    };

    private IEnumerable<int> AutomaticRegions() => Merge is null ? [] : _changes.Where(i => AutomaticChoice(Merge.Regions[i].Kind) is not null);

    /// <summary>How many of the automatic (non-conflicting) changes you have accepted, and how many there are.</summary>
    public int AcceptedCount => AutomaticRegions().Count(_decisions.ContainsKey);

    public int AutomaticCount => AutomaticRegions().Count();

    public string ReviewText => AutomaticCount == 0 ? "" : $"{AcceptedCount} of {AutomaticCount} automatic changes accepted";

    private bool CanAcceptSection => !ResultEdited && Merge is not null && CurrentRegion >= 0 && AutomaticChoice(Merge.Regions[CurrentRegion].Kind) is not null && !_decisions.ContainsKey(CurrentRegion);

    /// <summary>Marks the automatic change under the cursor as reviewed and moves on to the next section that still needs a look.</summary>
    [RelayCommand(CanExecute = nameof(CanAcceptSection))]
    private void AcceptSection()
    {
        var region = CurrentRegion;
        _decisions[region] = AutomaticChoice(Merge!.Regions[region].Kind)!.Value;
        BuildDisplay();
        NextPending();
    }

    private bool CanAcceptAll => !ResultEdited && Merge is not null && AutomaticRegions().Any(i => !_decisions.ContainsKey(i));

    /// <summary>Accepts every change that only one side made (or both made the same way); conflicts stay open.</summary>
    [RelayCommand(CanExecute = nameof(CanAcceptAll))]
    private void AcceptAllAutomatic()
    {
        foreach (var i in AutomaticRegions().ToList()) { _decisions.TryAdd(i, AutomaticChoice(Merge!.Regions[i].Kind)!.Value); }
        BuildDisplay();
        Status = $"Accepted {AcceptedCount} automatic change{(AcceptedCount == 1 ? "" : "s")}";
    }

    private bool CanResetSection => !ResultEdited && Merge is not null && CurrentRegion >= 0 && _decisions.ContainsKey(CurrentRegion);

    /// <summary>Takes back your choice for the section under the cursor: it returns to the automatic result, or to an open conflict.</summary>
    [RelayCommand(CanExecute = nameof(CanResetSection))]
    private void ResetSection()
    {
        _decisions.Remove(CurrentRegion);
        BuildDisplay();
    }

    private bool CanResetChoices => !ResultEdited && _decisions.Count > 0;

    /// <summary>Takes back every choice (the result goes back to the automatic merge, with conflicts open).</summary>
    [RelayCommand(CanExecute = nameof(CanResetChoices))]
    private void ResetChoices()
    {
        _decisions.Clear();
        BuildDisplay();
    }

    /// <summary>Moves to the next section that has no decision yet (a conflict or an automatic change), wrapping around.</summary>
    private void NextPending()
    {
        if (Merge is null || _changes.Count == 0) { return; }
        for (var step = 1; step <= _changes.Count; step++)
        {
            var idx = (Math.Max(CurrentChange, 0) + step) % _changes.Count;
            if (!_decisions.ContainsKey(_changes[idx])) { CurrentChange = idx; return; }
        }
    }

    private bool HasOpenConflicts => !ResultEdited && Merge is not null && UnresolvedCount > 0;

    [RelayCommand(CanExecute = nameof(HasOpenConflicts))]
    private void ResolveAllLeft() => ResolveAllConflicts(MergeChoice.Left);

    [RelayCommand(CanExecute = nameof(HasOpenConflicts))]
    private void ResolveAllRight() => ResolveAllConflicts(MergeChoice.Right);

    [RelayCommand(CanExecute = nameof(HasOpenConflicts))]
    private void ResolveAllBoth() => ResolveAllConflicts(MergeChoice.LeftThenRight);

    [RelayCommand(CanExecute = nameof(HasOpenConflicts))]
    private void ResolveAllCenter() => ResolveAllConflicts(MergeChoice.Base);

    /// <summary>Re-evaluates the buttons above after any change of the decisions or of the result.</summary>
    private void NotifyConveniences()
    {
        OnPropertyChanged(nameof(AcceptedCount));
        OnPropertyChanged(nameof(AutomaticCount));
        OnPropertyChanged(nameof(ReviewText));
        AcceptSectionCommand.NotifyCanExecuteChanged();
        AcceptAllAutomaticCommand.NotifyCanExecuteChanged();
        ResetSectionCommand.NotifyCanExecuteChanged();
        ResetChoicesCommand.NotifyCanExecuteChanged();
        ResolveAllLeftCommand.NotifyCanExecuteChanged();
        ResolveAllRightCommand.NotifyCanExecuteChanged();
        ResolveAllBothCommand.NotifyCanExecuteChanged();
        ResolveAllCenterCommand.NotifyCanExecuteChanged();
        InvalidateDetails();
    }

    // ───────────── the details panel ─────────────

    /// <summary>The panel under the panes: <see cref="DetailsMode.None"/> or <see cref="DetailsMode.Text"/>.</summary>
    [ObservableProperty] private DetailsMode _details;

    public static DetailsMode[] DetailsChoices { get; } = [DetailsMode.None, DetailsMode.Text];

    public bool ShowTextDetails => Details == DetailsMode.Text;

    partial void OnDetailsChanged(DetailsMode value) => OnPropertyChanged(nameof(ShowTextDetails));

    /// <summary>The row the cursor is on in the three input panes (they share their rows) and the line in the result pane, 0-based.</summary>
    [ObservableProperty] private int _topCaretRow;
    [ObservableProperty] private int _resultCaretRow;

    partial void OnTopCaretRowChanged(int value) => InvalidateDetails();
    partial void OnResultCaretRowChanged(int value) => InvalidateDetails();

    private string[]? _leftSplit, _baseSplit, _rightSplit, _resultSplit;

    private void InvalidateDetails()
    {
        (_leftSplit, _baseSplit, _rightSplit, _resultSplit) = (null, null, null, null);
        OnPropertyChanged(nameof(LeftDetailText));
        OnPropertyChanged(nameof(BaseDetailText));
        OnPropertyChanged(nameof(RightDetailText));
        OnPropertyChanged(nameof(ResultDetailText));
        OnPropertyChanged(nameof(LeftDetailLabel));
        OnPropertyChanged(nameof(BaseDetailLabel));
        OnPropertyChanged(nameof(RightDetailLabel));
        OnPropertyChanged(nameof(ResultDetailLabel));
    }

    private static string RowText(ref string[]? cache, string text, int row)
    {
        cache ??= text.Split('\n');
        return row >= 0 && row < cache.Length ? cache[row] : "";
    }

    private static string RowLabel(string name, IReadOnlyList<Collaterale.App.Controls.PaneRow> rows, int row)
        => row >= 0 && row < rows.Count && rows[row] is { IsFiller: false, FoldCount: 0 } r ? $"{name} · line {r.LineNumber}" : $"{name} · (no line here)";

    public string LeftDetailText => RowText(ref _leftSplit, LeftText, TopCaretRow);
    public string BaseDetailText => RowText(ref _baseSplit, BaseText, TopCaretRow);
    public string RightDetailText => RowText(ref _rightSplit, RightText, TopCaretRow);
    public string ResultDetailText => RowText(ref _resultSplit, ResultText, ResultCaretRow);
    public string LeftDetailLabel => RowLabel("Left", LeftRows, TopCaretRow);
    public string BaseDetailLabel => RowLabel("Base", BaseRows, TopCaretRow);
    public string RightDetailLabel => RowLabel("Right", RightRows, TopCaretRow);
    public string ResultDetailLabel => $"Result · line {ResultCaretRow + 1}";
}
