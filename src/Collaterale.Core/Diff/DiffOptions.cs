using System.Text.RegularExpressions;
using Collaterale.Core.Languages;

namespace Collaterale.Core.Diff;

public enum WhitespaceMode
{
    /// <summary>Every whitespace character matters.</summary>
    Exact,

    /// <summary>Runs of whitespace are equal to a single space ("a  b" == "a b").</summary>
    IgnoreAmount,

    /// <summary>Whitespace at the start and end of a line is ignored.</summary>
    IgnoreLeadingTrailing,

    /// <summary>All whitespace is ignored ("a b" == "ab").</summary>
    IgnoreAll,
}

/// <summary>
/// A stretch of each file that is compared only with the stretch of the other file: lines <c>[LeftStart, LeftStart + LeftCount)</c> of the
/// left text against <c>[RightStart, RightStart + RightCount)</c> of the right text. A count of 0 means "nothing on that side", so the
/// lines of the other side stand alone, with empty padding opposite them.
/// </summary>
public readonly record struct Isolation(int LeftStart, int LeftCount, int RightStart, int RightCount)
{
    public int LeftEnd => LeftStart + LeftCount;
    public int RightEnd => RightStart + RightCount;
}

/// <summary>What counts as a difference. Lines equal under these options are "same", or "unimportant" if their raw text still differs.</summary>
public sealed record DiffOptions
{
    public WhitespaceMode Whitespace { get; init; } = WhitespaceMode.Exact;

    public bool IgnoreCase { get; init; }

    /// <summary>Regular expressions; matching text is removed from a line before comparing (timestamps, version stamps, comments…).</summary>
    public IReadOnlyList<string> IgnorePatterns { get; init; } = [];

    /// <summary>Treat dates and times (ISO, RFC 2822, 12/24-hour clocks…) as unimportant: lines that differ only by a timestamp are not differences.</summary>
    public bool IgnoreTimestamps { get; init; }

    /// <summary>Blank (empty or whitespace-only) lines that exist on one side only are unimportant: adding or removing empty lines is not a difference.</summary>
    public bool IgnoreBlankLines { get; init; }

    /// <summary>
    /// When set, comments of this language are unimportant: a line that differs only in its comment is not a difference, and
    /// lines that hold nothing but a comment are never reported as added or removed. Null compares comments like any other text.
    /// </summary>
    public CommentSyntax? Comments { get; init; }

    public DiffAlgorithm Algorithm { get; init; } = DiffAlgorithm.Myers;

    /// <summary>
    /// Stretches that are realigned on their own (the user's "isolate"): the text before, between and after them is compared
    /// normally, but a line of an isolated stretch never pairs with a line outside its counterpart. Entries must be in file order and not
    /// overlap; invalid ones are ignored.
    /// </summary>
    public IReadOnlyList<Isolation> Isolations { get; init; } = [];

    /// <summary>Recognise blocks that were cut from one place and pasted in another and flag them as moved.</summary>
    public bool DetectMoves { get; init; } = true;

    /// <summary>Shortcut for the very common "no special options" case.</summary>
    public bool IsExact => Whitespace == WhitespaceMode.Exact && !IgnoreCase && IgnorePatterns.Count == 0 && !IgnoreTimestamps && !IgnoreBlankLines && Comments is null;

    public static DiffOptions Default { get; } = new();

    internal Regex[] CompiledPatterns() => Compile(IgnorePatterns);

    /// <summary>The built-in timestamp patterns, compiled only when <see cref="IgnoreTimestamps"/> is on. Every one of them needs a digit to match.</summary>
    internal Regex[] CompiledTimestampPatterns() => IgnoreTimestamps ? Compile(Timestamps.Patterns) : [];

    private static Regex[] Compile(IEnumerable<string> patterns)
        => [.. patterns.Where(p => !string.IsNullOrEmpty(p)).Select(p => new Regex(p, RegexOptions.CultureInvariant | RegexOptions.Compiled, TimeSpan.FromSeconds(1)))];
}
