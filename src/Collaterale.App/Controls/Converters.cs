using System.Globalization;
using Avalonia.Data.Converters;
using Collaterale.Core.Diff;

namespace Collaterale.App.Controls;

public static class Converters
{
    public static readonly IValueConverter WhitespaceLabel = new FuncConverter<WhitespaceMode>(m => m switch
    {
        WhitespaceMode.IgnoreAmount => "Ignore amount of whitespace",
        WhitespaceMode.IgnoreLeadingTrailing => "Ignore leading/trailing whitespace",
        WhitespaceMode.IgnoreAll => "Ignore all whitespace",
        _ => "Compare whitespace",
    });

    public static readonly IValueConverter ImageModeLabel = new FuncConverter<Collaterale.App.ViewModels.ImageViewMode>(m => m switch
    {
        Collaterale.App.ViewModels.ImageViewMode.SideBySide => "Side by side",
        Collaterale.App.ViewModels.ImageViewMode.Overlay => "Overlay (blend)",
        Collaterale.App.ViewModels.ImageViewMode.Swipe => "Swipe (curtain)",
        _ => "Difference highlight",
    });

    public static readonly IValueConverter IsOverlayMode = new FuncConverter<Collaterale.App.ViewModels.ImageViewMode, bool>(m => m == Collaterale.App.ViewModels.ImageViewMode.Overlay);

    public static readonly IValueConverter AlgorithmLabel = new FuncConverter<DiffAlgorithm>(a => a switch
    {
        DiffAlgorithm.Patience => "Readable (patience)",
        DiffAlgorithm.Histogram => "Smart (histogram)",
        _ => "Minimal (Myers)",
    });

    public static readonly IValueConverter FolderStateBrush = new FuncConverter<Collaterale.Core.Folders.FolderEntryState, Avalonia.Media.IBrush>(st => st switch
    {
        Collaterale.Core.Folders.FolderEntryState.Different => new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.FromArgb(0x3C, 0x8A, 0x96, 0xF5)),
        Collaterale.Core.Folders.FolderEntryState.LeftOnly => new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.FromArgb(0x3A, 0xF2, 0x7C, 0x7C)),
        Collaterale.Core.Folders.FolderEntryState.RightOnly => new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.FromArgb(0x3A, 0x58, 0xCF, 0x95)),
        Collaterale.Core.Folders.FolderEntryState.TypeMismatch or Collaterale.Core.Folders.FolderEntryState.Error => new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.FromArgb(0x4A, 0xF5, 0xB0, 0x42)),
        _ => Avalonia.Media.Brushes.Transparent,
    });

    public static readonly IValueConverter DeleteBrush = new FuncConverter<bool, Avalonia.Media.IBrush>(d => d ? new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#E5484D")) : new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#3D8B5F")));

    public static readonly IValueConverter BoldIf = new FuncConverter<bool, Avalonia.Media.FontWeight>(b => b ? Avalonia.Media.FontWeight.Bold : Avalonia.Media.FontWeight.Normal);

    public static readonly IValueConverter DetailsLabel = new FuncConverter<Collaterale.App.ViewModels.DetailsMode>(m => m switch
    {
        Collaterale.App.ViewModels.DetailsMode.Text => "Text details",
        Collaterale.App.ViewModels.DetailsMode.Hex => "Hex details",
        _ => "No details",
    });

    public static readonly IValueConverter MergeFilterLabel = new FuncConverter<Collaterale.App.ViewModels.MergeFilter>(f => f switch
    {
        Collaterale.App.ViewModels.MergeFilter.Differences => "Differences",
        Collaterale.App.ViewModels.MergeFilter.Same => "Same",
        Collaterale.App.ViewModels.MergeFilter.Conflicts => "Conflicts",
        Collaterale.App.ViewModels.MergeFilter.Left => "Left changes",
        Collaterale.App.ViewModels.MergeFilter.Center => "Same on both sides",
        Collaterale.App.ViewModels.MergeFilter.Right => "Right changes",
        _ => "All sections",
    });

    public static readonly IValueConverter CriteriaLabel = new FuncConverter<Collaterale.Core.Folders.CompareCriteria>(c => c switch
    {
        Collaterale.Core.Folders.CompareCriteria.SizeOnly => "Size only",
        Collaterale.Core.Folders.CompareCriteria.Content => "Contents only",
        Collaterale.Core.Folders.CompareCriteria.QuickThenContent => "Size and date, then contents",
        _ => "Size and date",
    });

    public static readonly IValueConverter ContentModeLabel = new FuncConverter<Collaterale.Core.Folders.ContentMode>(m => m switch
    {
        Collaterale.Core.Folders.ContentMode.Crc => "Checksum (CRC-32)",
        Collaterale.Core.Folders.ContentMode.Sha1 => "Checksum (SHA-1)",
        Collaterale.Core.Folders.ContentMode.Sha256 => "Checksum (SHA-256)",
        Collaterale.Core.Folders.ContentMode.Rules => "As text, with rules (ignore line endings, whitespace, comments…)",
        _ => "Byte by byte",
    });

    public static readonly IValueConverter ArchiveModeLabel = new FuncConverter<Collaterale.Core.Folders.ArchiveHandling>(m => m switch
    {
        Collaterale.Core.Folders.ArchiveHandling.AsFoldersOnceOpened => "As folders once opened",
        Collaterale.Core.Folders.ArchiveHandling.AsFoldersAlways => "As folders, always",
        _ => "As files",
    });

    public static readonly IValueConverter ExpandModeLabel = new FuncConverter<Collaterale.App.ViewModels.ExpandMode>(m => m switch
    {
        Collaterale.App.ViewModels.ExpandMode.All => "Expand all folders",
        Collaterale.App.ViewModels.ExpandMode.None => "Keep folders collapsed",
        _ => "Expand only folders with differences",
    });

    public static readonly IValueConverter FilterKindLabel = new FuncConverter<Collaterale.Core.Folders.OtherFilterKind>(k => k switch
    {
        Collaterale.Core.Folders.OtherFilterKind.SmallerThan => "Smaller than (bytes)",
        Collaterale.Core.Folders.OtherFilterKind.LargerThan => "Larger than (bytes)",
        Collaterale.Core.Folders.OtherFilterKind.ModifiedBefore => "Modified before (date)",
        Collaterale.Core.Folders.OtherFilterKind.ModifiedAfter => "Modified after (date)",
        Collaterale.Core.Folders.OtherFilterKind.OlderThanDays => "Not modified in the last (days)",
        Collaterale.Core.Folders.OtherFilterKind.NewerThanDays => "Modified in the last (days)",
        _ => "Read-only, hidden or system",
    });

    public static readonly IValueConverter FilterKindNeedsDate = new FuncConverter<Collaterale.Core.Folders.OtherFilterKind, bool>(k => k is Collaterale.Core.Folders.OtherFilterKind.ModifiedBefore or Collaterale.Core.Folders.OtherFilterKind.ModifiedAfter);

    public static readonly IValueConverter FilterKindNeedsNumber = new FuncConverter<Collaterale.Core.Folders.OtherFilterKind, bool>(k => k is not (Collaterale.Core.Folders.OtherFilterKind.ModifiedBefore or Collaterale.Core.Folders.OtherFilterKind.ModifiedAfter or Collaterale.Core.Folders.OtherFilterKind.HasAttribute));

    public static readonly IValueConverter IsRulesMode = new FuncConverter<Collaterale.Core.Folders.ContentMode, bool>(m => m == Collaterale.Core.Folders.ContentMode.Rules);

    /// <summary>Criteria that read file contents (the content options only matter for those).</summary>
    public static readonly IValueConverter UsesContents = new FuncConverter<Collaterale.Core.Folders.CompareCriteria, bool>(c => c is Collaterale.Core.Folders.CompareCriteria.Content or Collaterale.Core.Folders.CompareCriteria.QuickThenContent);

    public static readonly IValueConverter IsQuickThenContent = new FuncConverter<Collaterale.Core.Folders.CompareCriteria, bool>(c => c == Collaterale.Core.Folders.CompareCriteria.QuickThenContent);

    public static readonly IValueConverter SyncModeLabel = new FuncConverter<Collaterale.Core.Folders.SyncMode>(m => m switch
    {
        Collaterale.Core.Folders.SyncMode.UpdateRight => "Update right — copy new and newer files from left to right",
        Collaterale.Core.Folders.SyncMode.UpdateLeft => "Update left — copy new and newer files from right to left",
        Collaterale.Core.Folders.SyncMode.MirrorToRight => "Mirror to right — make right identical to left (deletes extras)",
        Collaterale.Core.Folders.SyncMode.MirrorToLeft => "Mirror to left — make left identical to right (deletes extras)",
        _ => "Two-way — copy the newer version of each file both ways",
    });

    /// <summary>A number box shows decimals; the settings hold whole numbers.</summary>
    public static readonly IValueConverter IntToDecimal = new IntDecimalConverter();

    private sealed class IntDecimalConverter : IValueConverter
    {
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value is int i ? (decimal?)i : null;

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => value is decimal d ? (int)d : 0;
    }

    private sealed class FuncConverter<TIn, TOut>(Func<TIn, TOut> f) : IValueConverter
    {
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value is TIn v ? f(v) : default(TOut);

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
    }

    private sealed class FuncConverter<T>(Func<T, string> f) : IValueConverter
    {
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value is T v ? f(v) : null;

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
    }
}
