using System.Globalization;
using Collaterale.Core.Hex;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Collaterale.App.ViewModels;

public sealed class HexSide
{
    public string? Path { get; set; }
    public byte[] Data { get; set; } = [];
    public bool Dirty { get; set; }
}

/// <summary>An undoable byte-range copy: what the target looked like before, so it can be put back exactly.</summary>
public sealed record InspectLine(string Label, string Value);

public sealed record HexPatch(Side Target, long Offset, byte[] OldBytes, long OldLength, bool WasDirty);

public sealed partial class HexCompareViewModel : ObservableObject, ISessionContent
{
    /// <summary>Files larger than this are refused: both are held in memory.</summary>
    public const long MaxFileBytes = 512L * 1024 * 1024;

    private readonly Stack<HexPatch> _undo = new();

    public HexSide Left { get; } = new();
    public HexSide Right { get; } = new();

    [ObservableProperty] private HexComparison? _comparison;
    [ObservableProperty] private int _currentRange = -1;
    [ObservableProperty] private long _selectedOffset = -1;
    [ObservableProperty] private int _bytesPerRow = 16;
    [ObservableProperty] private bool _showText = true;
    [ObservableProperty] private string _status = "Open two files to compare their bytes";
    [ObservableProperty] private string? _error;

    public static int[] RowWidths { get; } = [8, 16, 24, 32];

    public event EventHandler? DataChanged;

    public string Title => (Left.Path, Right.Path) switch
    {
        (null, null) => "Hex compare",
        (var l, var r) => $"{(l is null ? "…" : System.IO.Path.GetFileName(l))}  ↔  {(r is null ? "…" : System.IO.Path.GetFileName(r))}  (hex)",
    };

    public string LeftTitle => Left.Path is null ? "(no file)" : PathDisplay.Short(Left.Path) + (Left.Dirty ? "  ●" : "");
    public string RightTitle => Right.Path is null ? "(no file)" : PathDisplay.Short(Right.Path) + (Right.Dirty ? "  ●" : "");
    public string LeftInfo => Left.Path is null ? "" : $"{Left.Data.Length:N0} bytes";
    public string RightInfo => Right.Path is null ? "" : $"{Right.Data.Length:N0} bytes";
    public IReadOnlyList<InspectLine> LeftInspector => Inspect(Side.Left);
    public IReadOnlyList<InspectLine> RightInspector => Inspect(Side.Right);
    public bool HasComparison => Comparison is not null;
    public bool HasDifferences => Comparison is { AreEqual: false };

    public string DifferenceText => Comparison is null ? "" : Comparison.AreEqual ? "Identical" : CurrentRange < 0
        ? $"{Comparison.Ranges.Count:N0}{(Comparison.Truncated ? "+" : "")} differences"
        : $"Difference {CurrentRange + 1:N0} of {Comparison.Ranges.Count:N0}{(Comparison.Truncated ? "+" : "")}";

    // ───────────── loading ─────────────

    public void Open(string left, string right)
    {
        try
        {
            Error = null;
            foreach (var p in new[] { left, right })
            {
                if (new FileInfo(p).Length > MaxFileBytes)
                {
                    Error = $"'{System.IO.Path.GetFileName(p)}' is larger than {MaxFileBytes / (1024 * 1024)} MB and cannot be compared here.";
                    return;
                }
            }

            Left.Data = File.ReadAllBytes(left); Left.Path = left; Left.Dirty = false;
            Right.Data = File.ReadAllBytes(right); Right.Path = right; Right.Dirty = false;
            _undo.Clear();
            Recompare(goToFirst: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Error = ex.Message;
        }
    }

    private string? _pendingLeft, _pendingRight;

    /// <summary>Choose one side from the UI; the comparison starts once both are known.</summary>
    public void OpenSide(Side side, string path)
    {
        if (side == Side.Left) { _pendingLeft = path; } else { _pendingRight = path; }
        _pendingLeft ??= Left.Path;
        _pendingRight ??= Right.Path;
        if (_pendingLeft is not null && _pendingRight is not null)
        {
            Open(_pendingLeft, _pendingRight);
        }
        else
        {
            Status = side == Side.Left ? "Now choose the right file" : "Now choose the left file";
        }
    }

    public void OpenBytes(string? leftName, byte[] left, string? rightName, byte[] right)
    {
        (Left.Path, Left.Data, Left.Dirty) = (leftName, left, false);
        (Right.Path, Right.Data, Right.Dirty) = (rightName, right, false);
        _undo.Clear();
        Recompare(goToFirst: true);
    }

    private void Recompare(bool goToFirst = false)
    {
        Comparison = HexComparison.Compare(Left.Data, Right.Data);
        var n = Comparison.Ranges.Count;
        CurrentRange = n == 0 ? -1 : goToFirst ? 0 : Math.Min(Math.Max(CurrentRange, 0), n - 1);
        Status = Comparison.AreEqual ? "The files are identical" : $"{Comparison.DifferingBytes:N0} differing byte(s) in {n:N0}{(Comparison.Truncated ? "+" : "")} run(s)";
        NotifyAll();
        DataChanged?.Invoke(this, EventArgs.Empty);
    }

    private void NotifyAll()
    {
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(LeftTitle));
        OnPropertyChanged(nameof(RightTitle));
        OnPropertyChanged(nameof(LeftInfo));
        OnPropertyChanged(nameof(RightInfo));
        OnPropertyChanged(nameof(HasComparison));
        OnPropertyChanged(nameof(LeftInspector));
        OnPropertyChanged(nameof(RightInspector));
        OnPropertyChanged(nameof(HasDifferences));
        OnPropertyChanged(nameof(DifferenceText));
        OnPropertyChanged(nameof(CanUndo));
        NextDifferenceCommand.NotifyCanExecuteChanged();
        PreviousDifferenceCommand.NotifyCanExecuteChanged();
        CopyToRightCommand.NotifyCanExecuteChanged();
        CopyToLeftCommand.NotifyCanExecuteChanged();
        UndoCommand.NotifyCanExecuteChanged();
        SaveLeftCommand.NotifyCanExecuteChanged();
        SaveRightCommand.NotifyCanExecuteChanged();
    }

    partial void OnSelectedOffsetChanged(long value)
    {
        OnPropertyChanged(nameof(LeftInspector));
        OnPropertyChanged(nameof(RightInspector));
    }

    partial void OnCurrentRangeChanged(int value)
    {
        OnPropertyChanged(nameof(DifferenceText));
        CopyToRightCommand.NotifyCanExecuteChanged();
        CopyToLeftCommand.NotifyCanExecuteChanged();
    }

    partial void OnBytesPerRowChanged(int value) => DataChanged?.Invoke(this, EventArgs.Empty);

    partial void OnShowTextChanged(bool value) => DataChanged?.Invoke(this, EventArgs.Empty);

    // ───────────── navigation ─────────────

    private bool HasAny => Comparison is { Ranges.Count: > 0 };

    [RelayCommand(CanExecute = nameof(HasAny))]
    private void NextDifference()
    {
        var n = Comparison!.Ranges.Count;
        CurrentRange = (CurrentRange + 1) % n;
        SelectedOffset = Comparison.Ranges[CurrentRange].Offset;
    }

    [RelayCommand(CanExecute = nameof(HasAny))]
    private void PreviousDifference()
    {
        var n = Comparison!.Ranges.Count;
        CurrentRange = (CurrentRange - 1 + n) % n;
        SelectedOffset = Comparison.Ranges[CurrentRange].Offset;
    }

    /// <summary>Select a byte (from a click or "go to offset"); the run containing it becomes the current difference.</summary>
    public void Select(long offset)
    {
        SelectedOffset = Math.Clamp(offset, 0, Math.Max((Comparison?.Length ?? 1) - 1, 0));
        if (Comparison is { } c && c.RangeAt(SelectedOffset) is var i and >= 0)
        {
            CurrentRange = i;
        }
    }

    /// <summary>Parses "0x1A40", "1a40h" or a decimal number.</summary>
    public static bool TryParseOffset(string text, out long offset)
    {
        text = text.Trim().Replace("_", "");
        if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) { return long.TryParse(text[2..], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out offset); }
        if (text.EndsWith('h') || text.EndsWith('H')) { return long.TryParse(text[..^1], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out offset); }
        return long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out offset);
    }

    // ───────────── copying and saving ─────────────

    private bool CanCopy => HasAny && CurrentRange >= 0;

    [RelayCommand(CanExecute = nameof(CanCopy))]
    private void CopyToRight() => CopyRange(CurrentRange, Side.Left);

    [RelayCommand(CanExecute = nameof(CanCopy))]
    private void CopyToLeft() => CopyRange(CurrentRange, Side.Right);

    /// <summary>Overwrite the bytes of one difference on the other side (growing or truncating the file where only one side has data).</summary>
    public void CopyRange(int rangeIndex, Side from)
    {
        if (Comparison is not { } c || rangeIndex < 0 || rangeIndex >= c.Ranges.Count)
        {
            return;
        }

        var range = c.Ranges[rangeIndex];
        var (source, target, targetSide) = from == Side.Left ? (Left, Right, Side.Right) : (Right, Left, Side.Left);

        var oldLength = target.Data.Length;
        var oldSliceLength = (int)Math.Max(0, Math.Min(range.End, oldLength) - range.Offset);
        var old = target.Data.AsSpan((int)range.Offset, oldSliceLength).ToArray();
        _undo.Push(new HexPatch(targetSide, range.Offset, old, oldLength, target.Dirty));

        if (source.Data.Length <= range.Offset)
        {
            // the source has nothing here: copying "nothing" shortens the target (only possible for a trailing run)
            target.Data = target.Data.AsSpan(0, (int)range.Offset).ToArray();
        }
        else
        {
            var end = (int)Math.Min(range.End, source.Data.Length);
            var chunk = source.Data.AsSpan((int)range.Offset, end - (int)range.Offset);
            var newLength = Math.Max(target.Data.Length, end);
            var data = new byte[newLength];
            Array.Copy(target.Data, data, target.Data.Length);
            chunk.CopyTo(data.AsSpan((int)range.Offset));
            target.Data = data;
        }

        target.Dirty = true;
        Recompare();
    }

    public bool CanUndo => _undo.Count > 0;

    [RelayCommand(CanExecute = nameof(CanUndo))]
    private void Undo()
    {
        if (_undo.Count == 0) { return; }
        var p = _undo.Pop();
        var side = p.Target == Side.Left ? Left : Right;
        var data = new byte[p.OldLength];
        Array.Copy(side.Data, data, Math.Min(side.Data.Length, data.Length));
        p.OldBytes.CopyTo(data.AsSpan((int)p.Offset));
        side.Data = data;
        side.Dirty = p.WasDirty;
        Recompare();
    }

    private bool CanSaveLeft => Left.Dirty && Left.Path is not null;
    private bool CanSaveRight => Right.Dirty && Right.Path is not null;

    [RelayCommand(CanExecute = nameof(CanSaveLeft))]
    private void SaveLeft() => Save(Left);

    [RelayCommand(CanExecute = nameof(CanSaveRight))]
    private void SaveRight() => Save(Right);

    private void Save(HexSide side)
    {
        try
        {
            File.WriteAllBytes(side.Path!, side.Data);
            side.Dirty = false;
            Error = null;
            NotifyAll();
            Status = $"Saved {side.Path}";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Error = ex.Message;
        }
    }

    // ───────────── data inspector ─────────────

    /// <summary>Human-readable interpretations of the bytes at the selected offset, for one side.</summary>
    public IReadOnlyList<InspectLine> Inspect(Side side)
    {
        var data = (side == Side.Left ? Left : Right).Data;
        var o = (int)Math.Max(SelectedOffset, 0);
        if (SelectedOffset < 0 || o >= data.Length)
        {
            return [new InspectLine("Offset", SelectedOffset < 0 ? "—" : $"0x{SelectedOffset:X}"), new InspectLine("Value", "(no data at this offset)")];
        }

        var span = data.AsSpan(o);
        var list = new List<InspectLine>
        {
            new InspectLine("Offset", $"0x{SelectedOffset:X}  ({SelectedOffset:N0})"),
            new InspectLine("Hex", $"{data[o]:X2}"),
            new InspectLine("Unsigned", data[o].ToString(CultureInfo.InvariantCulture)),
            new InspectLine("Signed", ((sbyte)data[o]).ToString(CultureInfo.InvariantCulture)),
            new InspectLine("Binary", Convert.ToString(data[o], 2).PadLeft(8, '0')),
            new InspectLine("Char", data[o] is >= 32 and < 127 ? ((char)data[o]).ToString() : "·"),
        };

        if (span.Length >= 2) { list.Add(new InspectLine("UInt16 LE / BE", $"{System.Buffers.Binary.BinaryPrimitives.ReadUInt16LittleEndian(span)} / {System.Buffers.Binary.BinaryPrimitives.ReadUInt16BigEndian(span)}")); }
        if (span.Length >= 4)
        {
            list.Add(new InspectLine("UInt32 LE / BE", $"{System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(span)} / {System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(span)}"));
            list.Add(new InspectLine("Float32 LE", System.Buffers.Binary.BinaryPrimitives.ReadSingleLittleEndian(span).ToString("G9", CultureInfo.InvariantCulture)));
        }

        if (span.Length >= 8)
        {
            list.Add(new InspectLine("UInt64 LE", System.Buffers.Binary.BinaryPrimitives.ReadUInt64LittleEndian(span).ToString(CultureInfo.InvariantCulture)));
            list.Add(new InspectLine("Float64 LE", System.Buffers.Binary.BinaryPrimitives.ReadDoubleLittleEndian(span).ToString("G17", CultureInfo.InvariantCulture)));
        }

        return list;
    }
}
