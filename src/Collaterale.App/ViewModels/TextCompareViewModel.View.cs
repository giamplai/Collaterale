using System.Text;
using Collaterale.Core.Diff;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Collaterale.App.ViewModels;

public enum DetailsMode { None, Text, Hex }

/// <summary>How the text panes are laid out and what is shown below them.</summary>
public partial class TextCompareViewModel
{
    [ObservableProperty] private bool _showWhitespace;
    [ObservableProperty] private bool _wordWrap;

    /// <summary>Panes stacked (left above right) instead of side by side.</summary>
    [ObservableProperty] private bool _overUnder;

    [ObservableProperty] private DetailsMode _details;

    public static DetailsMode[] DetailsChoices { get; } = Enum.GetValues<DetailsMode>();

    [ObservableProperty] private int _leftCaretLine;
    [ObservableProperty] private int _rightCaretLine;

    public bool ShowTextDetails => Details == DetailsMode.Text;
    public bool ShowHexDetails => Details == DetailsMode.Hex;
    public bool ShowAnyDetails => Details != DetailsMode.None;

    partial void OnDetailsChanged(DetailsMode value)
    {
        OnPropertyChanged(nameof(ShowTextDetails));
        OnPropertyChanged(nameof(ShowHexDetails));
        OnPropertyChanged(nameof(ShowAnyDetails));
    }

    partial void OnLeftCaretLineChanged(int value) => NotifyDetails();
    partial void OnRightCaretLineChanged(int value) => NotifyDetails();

    private void NotifyDetails()
    {
        OnPropertyChanged(nameof(LeftDetailText));
        OnPropertyChanged(nameof(RightDetailText));
        OnPropertyChanged(nameof(LeftHexText));
        OnPropertyChanged(nameof(RightHexText));
        OnPropertyChanged(nameof(LeftDetailLabel));
        OnPropertyChanged(nameof(RightDetailLabel));
        OnPropertyChanged(nameof(DetailKind));
        OnPropertyChanged(nameof(LeftDetailWords));
        OnPropertyChanged(nameof(RightDetailWords));
    }

    private static string LineAt(TextSide s, int i) => i >= 0 && i < s.Lines.Count ? s.Lines[i] : "";

    private int _detailRow = -1;

    /// <summary>The kind of the row the details panel shows (colors both lines like the panes do); Same when there is no row.</summary>
    public RowKind DetailKind => DetailComparisonRow() is { } r ? Comparison!.Rows[r].Kind : RowKind.Same;

    /// <summary>The words that differ inside the two lines facing each other (only on a changed row).</summary>
    public IReadOnlyList<Span> LeftDetailWords => DetailComparisonRow() is { } r ? Comparison!.IntraLine(r).Left : [];
    public IReadOnlyList<Span> RightDetailWords => DetailComparisonRow() is { } r ? Comparison!.IntraLine(r).Right : [];

    private int? DetailComparisonRow()
    {
        if (Comparison is not { } c || _detailRow < 0 || _detailRow >= DisplayMap.Count) { return null; }
        var r = DisplayMap[_detailRow];
        return r >= 0 && r < c.Rows.Count ? r : null;
    }

    public string LeftDetailText => LineAt(Left, LeftCaretLine);
    public string RightDetailText => LineAt(Right, RightCaretLine);
    public string LeftDetailLabel => LeftCaretLine < 0 ? "Left · (no line here)" : $"Left · line {LeftCaretLine + 1}";
    public string RightDetailLabel => RightCaretLine < 0 ? "Right · (no line here)" : $"Right · line {RightCaretLine + 1}";
    public string LeftHexText => HexOf(Left, LeftCaretLine);
    public string RightHexText => HexOf(Right, RightCaretLine);

    /// <summary>The bytes of one line in the side's own encoding, sixteen to a row, with the offset and the text beside them.</summary>
    public static string HexOf(TextSide side, int line)
    {
        if (line < 0 || line >= side.Lines.Count) { return ""; }
        var bytes = side.Encoding.GetBytes(side.Lines[line]);
        var sb = new StringBuilder();
        for (var i = 0; i < bytes.Length; i += 16)
        {
            var chunk = bytes.Skip(i).Take(16).ToArray();
            sb.Append(i.ToString("X4")).Append("  ");
            sb.Append(string.Join(' ', chunk.Select(b => b.ToString("X2")))).Append(new string(' ', (16 - chunk.Length) * 3 + 2));
            sb.Append(new string([.. chunk.Select(b => b is >= 32 and < 127 ? (char)b : '.')]));
            if (i + 16 < bytes.Length) { sb.Append('\n'); }
        }

        return sb.ToString();
    }

    /// <summary>The options changed: show the text again so the panes pick up the new settings.</summary>
    public void PreferencesChanged() => Rebuild();

    /// <summary>
    /// The cursor moved to a row of either pane: the details panel shows the lines that face each other on that row — the two sides
    /// of the comparison — whichever pane the cursor is in. A side with only padding on the row has no line there (-1).
    /// </summary>
    public void CaretMoved(Side side, int row)
    {
        TryCompleteAlign(side, row);
        _detailRow = row;
        LeftCaretLine = LineOnRow(LeftRows, row);
        RightCaretLine = LineOnRow(RightRows, row);
        NotifyDetails(); // the lines may be the same ones while the row changed (and with it the highlighted words)
    }

    private static int LineOnRow(IReadOnlyList<Collaterale.App.Controls.PaneRow> rows, int row)
        => row >= 0 && row < rows.Count && rows[row] is { IsFiller: false, FoldCount: 0, LineNumber: > 0 } r ? r.LineNumber - 1 : -1;

    /// <summary>Applies a line edited in the details panel as one undoable step.</summary>
    public void SetLine(Side side, int line, string text)
    {
        var s = side == Side.Left ? Left : Right;
        if ((side == Side.Left ? LeftLocked : RightLocked) || line < 0 || line >= s.Lines.Count || s.Lines[line] == text) { return; }
        FlushEdits?.Invoke();
        PushUndo();
        s.Lines[line] = text;
        s.Dirty = true;
        Recompare();
        NotifyDetails();
    }
}
