using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using System.IO;
using AvaloniaEdit;
using AvaloniaEdit.Document;
using AvaloniaEdit.Editing;
using AvaloniaEdit.Rendering;
using AvaloniaEdit.Search;
using AvaloniaEdit.TextMate;
using TextMateSharp.Grammars;
using Collaterale.Core.Diff;

namespace Collaterale.App.Controls;

/// <summary>Per-row facts the pane needs to paint: what kind of row it is, its original line number, and highlighted words.</summary>
/// <param name="FoldCount">When &gt; 0 this row stands for that many hidden unchanged lines (collapsed view).</param>
/// <param name="Tint">Merge views color a row by whose change it carries (left teal, right magenta, conflict red); None uses the row kind.</param>
public sealed record PaneRow(RowKind Kind, int LineNumber, bool IsFiller, int Group, IReadOnlyList<Span> Words, bool Moved = false, int FoldCount = 0, MergeTint Tint = MergeTint.None);

/// <summary>Whose change a merge row shows.</summary>
public enum MergeTint { None, Left, Right, Conflict, Base, Edited }

/// <summary>
/// A read-only AvaloniaEdit editor showing one side of an aligned comparison. Filler rows (blank lines opposite
/// insertions/deletions) keep both panes the same height so they scroll as one.
/// </summary>
public sealed class DiffPane : TextEditor
{
    private IReadOnlyList<PaneRow> _rows = [];
    private bool _internalChange;
    private readonly List<TextAnchor> _fillerAnchors = [];
    private int _currentGroup = -1;
    private TextMate.Installation? _textMate;
    private RegistryOptions? _registry;
    private string? _languageId;
    private readonly RowBackgroundRenderer _background;
    private readonly WordHighlightTransformer _words;

    public DiffPane()
    {
        IsReadOnly = true;
        ShowLineNumbers = false; // we draw original line numbers (blank on fillers) in our own margin
        Options.EnableHyperlinks = false;
        Options.EnableEmailHyperlinks = false;
        Options.HighlightCurrentLine = false;
        Options.ShowEndOfLine = false;
        FontFamily = new FontFamily("Cascadia Mono,JetBrains Mono,Fira Code,DejaVu Sans Mono,Menlo,Consolas,monospace");
        FontSize = 13;
        HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto;
        VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Hidden; // the overview map + synced scrolling replace it

        _background = new RowBackgroundRenderer(this);
        _words = new WordHighlightTransformer(this);
        TextArea.TextView.BackgroundRenderers.Add(_background);
        TextArea.TextView.LineTransformers.Add(_words);
        TextArea.TextView.LineTransformers.Add(new SearchMarkTransformer(this));
        TextArea.TextView.LineTransformers.Add(new PlainOnDifferenceTransformer(this));
        TextArea.AddHandler(KeyDownEvent, OnBackspace, Avalonia.Interactivity.RoutingStrategies.Tunnel);
        TextArea.LeftMargins.Insert(0, new RowNumberMargin(this));
        TextArea.TextView.AddHandler(PointerPressedEvent, (_, e) =>
        {
            var pos = TextArea.TextView.GetPosition(e.GetPosition(TextArea.TextView) + TextArea.TextView.ScrollOffset);
            if (pos is { } p && RowAt(p.Line) is { FoldCount: > 0 })
            {
                FoldClicked?.Invoke(p.Line - 1);
                e.Handled = true;
            }
        }, Avalonia.Interactivity.RoutingStrategies.Tunnel);
        ActualThemeVariantChanged += (_, _) => ApplySyntaxTheme();
    }

    /// <summary>Subclasses need the base control's template; Avalonia otherwise looks for a style of the derived type.</summary>
    protected override Type StyleKeyOverride => typeof(TextEditor);

    public IReadOnlyList<PaneRow> Rows => _rows;

    /// <summary>
    /// Filler rows are real (empty) lines in the document. An anchor on each one keeps track of it while the user types, so the
    /// genuine lines can be told apart again afterwards. Typing into a filler line turns it into a real line.
    /// </summary>
    private void TrackFillers(IReadOnlyList<PaneRow> rows)
    {
        _fillerAnchors.Clear();
        if (Document is not { } doc)
        {
            return;
        }

        for (var i = 0; i < rows.Count && i < doc.LineCount; i++)
        {
            if (rows[i].IsFiller)
            {
                var anchor = doc.CreateAnchor(doc.GetLineByNumber(i + 1).Offset);
                anchor.MovementType = AnchorMovementType.AfterInsertion;
                _fillerAnchors.Add(anchor);
            }
        }
    }

    private HashSet<int> FillerLines()
    {
        var set = new HashSet<int>();
        foreach (var a in _fillerAnchors)
        {
            if (!a.IsDeleted)
            {
                set.Add(a.Line);
            }
        }

        return set;
    }

    /// <summary>The pane's real lines: everything except empty lines that are still pure fillers.</summary>
    public List<string> ReconstructLines()
    {
        var result = new List<string>();
        if (Document is not { } doc)
        {
            return result;
        }

        var fillers = FillerLines();
        foreach (var line in doc.Lines)
        {
            if (line.Length == 0 && fillers.Contains(line.LineNumber))
            {
                continue;
            }

            result.Add(doc.GetText(line.Offset, line.Length));
        }

        return result;
    }

    /// <summary>The caret as (index among the real lines, column), so it can be put back after the rows are rebuilt.</summary>
    public (int Line, int Column) GetCaretModelPosition()
    {
        if (Document is not { } doc)
        {
            return (0, 0);
        }

        var fillers = FillerLines();
        var caretLine = Math.Min(TextArea.Caret.Line, doc.LineCount);
        var modelLine = 0;
        for (var l = 1; l < caretLine; l++)
        {
            var dl = doc.GetLineByNumber(l);
            if (!(dl.Length == 0 && fillers.Contains(l)))
            {
                modelLine++;
            }
        }

        return (modelLine, TextArea.Caret.Column - 1);
    }

    public void RestoreCaret(int modelLine, int column)
    {
        if (Document is not { } doc || _rows.Count == 0)
        {
            return;
        }

        var display = _rows.Select((r, i) => (r, i)).FirstOrDefault(t => !t.r.IsFiller && t.r.LineNumber - 1 == modelLine);
        var line = display.r is null ? doc.LineCount : display.i + 1;
        var length = doc.GetLineByNumber(Math.Min(line, doc.LineCount)).Length;
        TextArea.Caret.Line = Math.Min(line, doc.LineCount);
        TextArea.Caret.Column = Math.Min(column, length) + 1;
    }

    private bool _crosshatch = true;
    private Color? _orphan;
    private bool _syntaxOnDiff = true;
    private bool _backspaceUnindents = true;
    private int _tabSize = 4;

    /// <summary>Applies the text-editing and editor-display preferences to this pane.</summary>
    public void ApplyPreferences(Collaterale.Core.Settings.Preferences p)
    {
        TextArea.IndentationStrategy = p.TextEditing.AutoIndent ? new AvaloniaEdit.Indentation.DefaultIndentationStrategy() : null;
        _backspaceUnindents = p.TextEditing.BackspaceUnindents;
        _tabSize = Math.Max(1, p.TextEditing.TabSize);
        Options.IndentationSize = _tabSize;
        Options.ShowColumnRulers = p.Editor.ColumnLine > 0;
        Options.ColumnRulerPositions = p.Editor.ColumnLine > 0 ? [p.Editor.ColumnLine] : [];
        _crosshatch = p.Editor.CrosshatchBeyondEnd;
        _orphan = p.Editor.UseOrphanColor && Color.TryParse(p.Editor.OrphanColor, out var oc) ? oc : null;
        if (_syntaxOnDiff != p.Editor.SyntaxOnDifferenceLines)
        {
            _syntaxOnDiff = p.Editor.SyntaxOnDifferenceLines;
            TextArea.TextView.Redraw();
        }
    }

    /// <summary>Backspace in leading spaces removes back to the previous tab stop instead of one space.</summary>
    private void OnBackspace(object? sender, Avalonia.Input.KeyEventArgs e)
    {
        if (e.Key != Avalonia.Input.Key.Back || !_backspaceUnindents || IsReadOnly || !TextArea.Selection.IsEmpty || Document is not { } doc) { return; }
        var caret = TextArea.Caret;
        if (caret.Column <= 1) { return; }
        var line = doc.GetLineByNumber(caret.Line);
        var before = doc.GetText(line.Offset, caret.Column - 1);
        if (before.Length == 0 || before.Any(c => c != ' ')) { return; }
        var remove = before.Length % _tabSize == 0 ? _tabSize : before.Length % _tabSize;
        if (remove <= 1) { return; }
        doc.Remove(caret.Offset - remove, remove);
        e.Handled = true;
    }

    private IReadOnlyList<(int Line, int Start, int Length)> _marks = [];

    /// <summary>Highlights the matches of the current search: display line (1-based), column and length.</summary>
    public void SetSearchMarks(IReadOnlyList<(int Line, int Start, int Length)> marks)
    {
        _marks = marks;
        TextArea.TextView.Redraw();
    }

    /// <summary>Spaces, tabs and line ends drawn as symbols.</summary>
    public bool ShowWhitespace
    {
        get => Options.ShowSpaces;
        set => Options.ShowSpaces = Options.ShowTabs = Options.ShowEndOfLine = value;
    }

    /// <summary>The selected text, or the word at the cursor.</summary>
    public string WordAtCaret()
    {
        if (Document is not { } doc) { return ""; }
        if (!TextArea.Selection.IsEmpty && !TextArea.Selection.IsMultiline) { return TextArea.Selection.GetText(); }
        var line = doc.GetLineByNumber(Math.Min(Math.Max(TextArea.Caret.Line, 1), doc.LineCount));
        var text = doc.GetText(line.Offset, line.Length);
        var at = Math.Min(TextArea.Caret.Column - 1, text.Length);
        int a = at, b = at;
        static bool W(char c) => char.IsLetterOrDigit(c) || c == '_';
        while (a > 0 && W(text[a - 1])) { a--; }
        while (b < text.Length && W(text[b])) { b++; }
        return text[a..b];
    }

    /// <summary>The caret as a display line (1-based) and the selected columns (0-based).</summary>
    public (int Line, int StartColumn, int EndColumn) CaretSpan()
    {
        var sel = TextArea.Selection;
        if (!sel.IsEmpty && Document is { } doc && !sel.IsMultiline)
        {
            var line = doc.GetLineByOffset(sel.SurroundingSegment.Offset);
            return (line.LineNumber, sel.SurroundingSegment.Offset - line.Offset, sel.SurroundingSegment.EndOffset - line.Offset);
        }

        return (TextArea.Caret.Line, TextArea.Caret.Column - 1, TextArea.Caret.Column - 1);
    }

    /// <summary>Selects a stretch of one display line and scrolls it into view.</summary>
    public void SelectSpan(int line, int start, int length)
    {
        if (Document is not { } doc || line < 1 || line > doc.LineCount) { return; }
        var l = doc.GetLineByNumber(line);
        var offset = l.Offset + Math.Min(start, l.Length);
        TextArea.Selection = Selection.Create(TextArea, offset, offset + Math.Min(length, l.EndOffset - offset));
        TextArea.Caret.Offset = offset + Math.Min(length, l.EndOffset - offset);
        TextArea.Caret.BringCaretToView();
    }

    /// <summary>Syntax colors are dropped on lines that differ when the preference says so, so they do not fight the difference colors.</summary>
    private sealed class PlainOnDifferenceTransformer(DiffPane pane) : DocumentColorizingTransformer
    {
        protected override void ColorizeLine(DocumentLine line)
        {
            if (pane._syntaxOnDiff || line.Length == 0) { return; }
            if (pane.RowAt(line.LineNumber) is not { } row || row.IsFiller || (row.Kind == RowKind.Same && row.Tint == MergeTint.None) || row.FoldCount > 0) { return; }
            var brush = pane.Foreground;
            ChangeLinePart(line.Offset, line.EndOffset, e => e.TextRunProperties.SetForegroundBrush(brush));
        }
    }

    private sealed class SearchMarkTransformer(DiffPane pane) : DocumentColorizingTransformer
    {
        protected override void ColorizeLine(DocumentLine line)
        {
            if (pane._marks.Count == 0) { return; }
            var brush = new SolidColorBrush(pane.IsDark ? Color.FromArgb(0x90, 0xE0, 0xB0, 0x30) : Color.FromArgb(0xA0, 0xFF, 0xD8, 0x4A));
            foreach (var (l, start, length) in pane._marks)
            {
                if (l != line.LineNumber) { continue; }
                var a = line.Offset + Math.Min(start, line.Length);
                var b = line.Offset + Math.Min(start + length, line.Length);
                if (b > a) { ChangeLinePart(a, b, e => e.TextRunProperties.SetBackgroundBrush(brush)); }
            }
        }
    }

    /// <summary>Raised with the display row (0-based) when the user clicks a collapsed-lines marker.</summary>
    public event Action<int>? FoldClicked;

    /// <summary>Raised when the user (not the program) changes the text of an editable pane.</summary>
    public event EventHandler? UserEdited;

    /// <summary>Adds the take-left / take-right buttons beside the text (the merged result).</summary>
    public void InstallMergeActions(Func<int, Collaterale.Core.Merge.MergeRegionKind?> kindOf, Func<int, bool> isResolved, Action<int, Collaterale.App.ViewModels.MergeChoice> take)
        => TextArea.LeftMargins.Insert(0, new MergeActionMargin(this, kindOf, isResolved, take));

    /// <summary>Stops row coloring and falls back to plain line numbers (used once the text no longer matches the rows).</summary>
    public void ClearRows()
    {
        _rows = [];
        ShowLineNumbers = !IsReadOnly;
        TextArea.TextView.Redraw();
    }

    public int CurrentGroup
    {
        get => _currentGroup;
        set
        {
            if (_currentGroup != value)
            {
                _currentGroup = value;
                TextArea.TextView.InvalidateLayer(KnownLayer.Background);
            }
        }
    }

    /// <summary>Colors the text for the language of <paramref name="path"/> (by extension); no file or an unknown type means plain text.</summary>
    public void SetSyntaxFor(string? path)
    {
        var ext = string.IsNullOrEmpty(path) ? null : Path.GetExtension(path);
        try
        {
            _registry ??= new RegistryOptions(IsDark ? ThemeName.DarkPlus : ThemeName.LightPlus);
            var language = string.IsNullOrEmpty(ext) ? null : _registry.GetLanguageByExtension(ext);
            if (language is null)
            {
                _textMate?.Dispose();
                _textMate = null;
                _languageId = null;
                return;
            }

            _textMate ??= this.InstallTextMate(_registry);
            if (_languageId != language.Id)
            {
                _textMate.SetGrammar(_registry.GetScopeByLanguageId(language.Id));
                _languageId = language.Id;
            }

            ApplySyntaxTheme();
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // Highlighting is a nicety: a broken grammar must never stop a comparison from being shown.
            _textMate = null;
            _languageId = null;
        }
    }

    private void ApplySyntaxTheme()
    {
        if (_textMate is null || _registry is null)
        {
            return;
        }

        _textMate.SetTheme(_registry.LoadTheme(IsDark ? ThemeName.DarkPlus : ThemeName.LightPlus));
    }

    /// <summary>Replaces the displayed text and the per-row information together.</summary>
    public void SetContent(string text, IReadOnlyList<PaneRow> rows)
    {
        _rows = rows;
        _internalChange = true;
        try
        {
            Document = new TextDocument(text);
            Document.TextChanged += (_, _) =>
            {
                if (!_internalChange)
                {
                    UserEdited?.Invoke(this, EventArgs.Empty);
                }
            };
        }
        finally
        {
            _internalChange = false;
        }

        TrackFillers(rows);
        if (!IsReadOnly)
        {
            // Rows are rebuilt after every edit, so the editor's own undo history would point at a document that no longer exists.
            // Undo is handled one level up, on whole-file snapshots.
            Document.UndoStack.SizeLimit = 0;
        }

        ShowLineNumbers = rows.Count == 0 && !IsReadOnly;
        TextArea.TextView.Redraw();
        TextArea.LeftMargins.OfType<RowNumberMargin>().FirstOrDefault()?.InvalidateMeasure();
    }

    internal PaneRow? RowAt(int line) => line >= 1 && line <= _rows.Count ? _rows[line - 1] : null;

    public static Color BackgroundFor(RowKind kind, bool left, bool dark, bool moved = false)
        => moved && kind is RowKind.LeftOnly or RowKind.RightOnly
            ? (dark ? Color.FromArgb(0x66, 0x9A, 0x7B, 0xF0) : Color.FromArgb(0x55, 0xB0, 0x8A, 0xF2))
            : kind switch
        {
            RowKind.Changed => dark ? Color.FromArgb(0x55, 0x6C, 0x7B, 0xE8) : Color.FromArgb(0x4A, 0x8A, 0x96, 0xF5),
            RowKind.LeftOnly => dark ? Color.FromArgb(0x55, 0xE0, 0x5A, 0x5A) : Color.FromArgb(0x48, 0xF2, 0x7C, 0x7C),
            RowKind.RightOnly => dark ? Color.FromArgb(0x55, 0x3F, 0xB9, 0x7C) : Color.FromArgb(0x48, 0x58, 0xCF, 0x95),
            RowKind.Conflict => dark ? Color.FromArgb(0x66, 0xF0, 0xA8, 0x3C) : Color.FromArgb(0x66, 0xF5, 0xB0, 0x42),
            RowKind.Unimportant => dark ? Color.FromArgb(0x30, 0x9A, 0xA4, 0xB4) : Color.FromArgb(0x30, 0x80, 0x8A, 0x9A),
            _ => Colors.Transparent,
        };

    public static Color TintColor(MergeTint tint, bool dark) => tint switch
    {
        MergeTint.Left => dark ? Color.FromArgb(0x66, 0x2A, 0xB0, 0xA8) : Color.FromArgb(0x5A, 0x30, 0xC8, 0xC0),
        MergeTint.Right => dark ? Color.FromArgb(0x66, 0xC0, 0x5A, 0xD0) : Color.FromArgb(0x50, 0xD0, 0x6A, 0xE0),
        MergeTint.Conflict => dark ? Color.FromArgb(0x77, 0xE0, 0x50, 0x50) : Color.FromArgb(0x66, 0xE5, 0x50, 0x50),
        MergeTint.Base => dark ? Color.FromArgb(0x44, 0x9A, 0xA0, 0xAA) : Color.FromArgb(0x40, 0x80, 0x86, 0x90),
        MergeTint.Edited => dark ? Color.FromArgb(0x66, 0xF0, 0xC8, 0x3C) : Color.FromArgb(0x66, 0xF5, 0xD0, 0x42),
        _ => Colors.Transparent,
    };

    public bool IsDark => ActualThemeVariant == Avalonia.Styling.ThemeVariant.Dark;

    private sealed class RowBackgroundRenderer(DiffPane pane) : IBackgroundRenderer
    {
        public KnownLayer Layer => KnownLayer.Background;

        public void Draw(TextView textView, DrawingContext context)
        {
            if (!textView.VisualLinesValid || textView.VisualLines.Count == 0)
            {
                return;
            }

            var dark = pane.IsDark;
            var filler = new SolidColorBrush(dark ? Color.FromArgb(0x28, 0xFF, 0xFF, 0xFF) : Color.FromArgb(0x14, 0x00, 0x00, 0x00));
            var outline = new Pen(new SolidColorBrush(dark ? Color.FromArgb(0xCC, 0x9D, 0xB4, 0xFF) : Color.FromArgb(0xCC, 0x3D, 0x5A, 0xE0)), 1.5);
            var width = textView.Bounds.Width;

            foreach (var vl in textView.VisualLines)
            {
                if (pane.RowAt(vl.FirstDocumentLine.LineNumber) is not { } row)
                {
                    continue;
                }

                var top = vl.VisualTop - textView.VerticalOffset;
                var rect = new Rect(0, top, width, vl.Height);
                if (row.FoldCount > 0)
                {
                    context.FillRectangle(new SolidColorBrush(dark ? Color.FromArgb(0x30, 0x6C, 0x84, 0xFF) : Color.FromArgb(0x1E, 0x4C, 0x6F, 0xFF)), rect);
                }
                else if (row.IsFiller && row.Tint != MergeTint.None)
                {
                    // a slot with no lines of this version: hatched in the color of the change that left it empty
                    var color = TintColor(row.Tint, dark);
                    context.FillRectangle(new SolidColorBrush(Color.FromArgb((byte)(color.A / 2), color.R, color.G, color.B)), rect);
                    var pen = new Pen(new SolidColorBrush(color), 1);
                    using (context.PushClip(rect))
                    {
                        for (var x = rect.Left - rect.Height; x < rect.Right; x += 7)
                        {
                            context.DrawLine(pen, new Point(x, rect.Bottom), new Point(x + rect.Height, rect.Top));
                        }
                    }
                }
                else if (row.IsFiller)
                {
                    context.FillRectangle(filler, rect);
                }
                else if (row.Tint != MergeTint.None)
                {
                    context.FillRectangle(new SolidColorBrush(TintColor(row.Tint, dark)), rect);
                }
                else if (pane._orphan is { } orphan && row.Kind is RowKind.LeftOnly or RowKind.RightOnly && !row.Moved)
                {
                    context.FillRectangle(new SolidColorBrush(Color.FromArgb(dark ? (byte)0x66 : (byte)0x55, orphan.R, orphan.G, orphan.B)), rect);
                }
                else if (row.Kind != RowKind.Same)
                {
                    context.FillRectangle(new SolidColorBrush(BackgroundFor(row.Kind, true, dark, row.Moved)), rect);
                }
            }

            // the space below the last line is hatched so the end of the text can be seen (and a shorter file stands out)
            if (pane._crosshatch && pane._rows.Count > 0)
            {
                var end = textView.DocumentHeight - textView.VerticalOffset;
                if (end < textView.Bounds.Height - 2)
                {
                    var rect = new Rect(0, Math.Max(end, 0), width, textView.Bounds.Height - Math.Max(end, 0));
                    var pen = new Pen(new SolidColorBrush(dark ? Color.FromArgb(0x30, 0xFF, 0xFF, 0xFF) : Color.FromArgb(0x28, 0x00, 0x00, 0x00)), 1);
                    using (context.PushClip(rect))
                    {
                        for (var x = rect.Left - rect.Height; x < rect.Right; x += 9) { context.DrawLine(pen, new Point(x, rect.Bottom), new Point(x + rect.Height, rect.Top)); }
                    }
                }
            }

            // outline the current difference as one block
            if (pane.CurrentGroup >= 0)
            {
                double? top = null, bottom = null;
                foreach (var vl in textView.VisualLines)
                {
                    if (pane.RowAt(vl.FirstDocumentLine.LineNumber) is { } row && row.Group == pane.CurrentGroup)
                    {
                        top ??= vl.VisualTop - textView.VerticalOffset;
                        bottom = vl.VisualTop - textView.VerticalOffset + vl.Height;
                    }
                }

                if (top is not null && bottom is not null)
                {
                    context.DrawRectangle(outline, new Rect(1, top.Value, Math.Max(width - 2, 1), bottom.Value - top.Value));
                }
            }
        }
    }

    /// <summary>Stronger background for the words that changed inside a modified line.</summary>
    private sealed class WordHighlightTransformer(DiffPane pane) : DocumentColorizingTransformer
    {
        protected override void ColorizeLine(DocumentLine line)
        {
            if (pane.RowAt(line.LineNumber) is not { Words.Count: > 0 } row)
            {
                return;
            }

            var dark = pane.IsDark;
            var color = row.Kind == RowKind.Changed
                ? (dark ? Color.FromArgb(0xA0, 0x7C, 0x8C, 0xFF) : Color.FromArgb(0x90, 0x6B, 0x7B, 0xF0))
                : Colors.Transparent;
            var brush = new SolidColorBrush(color);
            foreach (var w in row.Words)
            {
                var start = line.Offset + Math.Min(w.Start, line.Length);
                var end = line.Offset + Math.Min(w.End, line.Length);
                if (end > start)
                {
                    ChangeLinePart(start, end, e => e.TextRunProperties.SetBackgroundBrush(brush));
                }
            }
        }
    }

    /// <summary>Left margin with the original line numbers; filler rows show nothing.</summary>
    private sealed class RowNumberMargin(DiffPane pane) : AbstractMargin
    {
        private static readonly Typeface Mono = new("Cascadia Mono,JetBrains Mono,DejaVu Sans Mono,Menlo,Consolas,monospace");

        protected override Size MeasureOverride(Size availableSize)
        {
            var digits = Math.Max(3, pane.Rows.Count == 0 ? 3 : pane.Rows.Max(r => r.LineNumber).ToString().Length);
            return new Size(digits * pane.FontSize * 0.62 + 18, 0);
        }

        public override void Render(DrawingContext context)
        {
            var tv = TextView;
            if (tv is null || !tv.VisualLinesValid)
            {
                return;
            }

            var brush = pane.IsDark ? new SolidColorBrush(Color.FromRgb(0x8B, 0x93, 0xA3)) : new SolidColorBrush(Color.FromRgb(0x7A, 0x82, 0x92));
            foreach (var vl in tv.VisualLines)
            {
                if (pane.RowAt(vl.FirstDocumentLine.LineNumber) is not { IsFiller: false } row)
                {
                    continue;
                }

                var text = new FormattedText(row.LineNumber.ToString(), System.Globalization.CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Mono, pane.FontSize - 1, brush);
                context.DrawText(text, new Point(Bounds.Width - text.Width - 8, vl.VisualTop - tv.VerticalOffset + (vl.Height - text.Height) / 2));
            }
        }
    }
}
