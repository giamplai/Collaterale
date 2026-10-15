namespace Collaterale.Core.Cli;

/// <summary>The switches of a command line that tune how a session opens (everything but the paths).</summary>
public sealed record LaunchOptions
{
    /// <summary>Used when a command line has no switches, so equal requests compare equal.</summary>
    public static LaunchOptions None { get; } = new();

    public bool AutoMerge { get; init; }
    public bool Force { get; init; }
    public bool ReviewConflicts { get; init; }
    public bool IgnoreUnimportant { get; init; }
    public bool FavorLeft { get; init; }
    public bool FavorRight { get; init; }
    public bool NoBackups { get; init; }
    public bool ReadOnlyLeft { get; init; }
    public bool ReadOnlyRight { get; init; }
    public bool Silent { get; init; }
    public bool Solo { get; init; }
    public bool ExpandAll { get; init; }
    public bool CloseScript { get; init; }

    /// <summary>Name filter masks for the first folder comparison, separated by semicolons.</summary>
    public string? Filters { get; init; }

    /// <summary>Overwrite this file instead of the original when saving.</summary>
    public string? SaveTarget { get; init; }

    /// <summary>null = no quick compare; "" = rules-based; otherwise size, crc or binary.</summary>
    public string? QuickCompare { get; init; }

    /// <summary>Descriptions shown instead of the paths: left, right, center, output.</summary>
    public IReadOnlyList<string?> Titles { get; init; } = [null, null, null, null];

    /// <summary>Version-control paths shown instead of the real ones: left, right, center, output.</summary>
    public IReadOnlyList<string?> Vcs { get; init; } = [null, null, null, null];

    public bool IsReadOnly(bool left) => left ? ReadOnlyLeft : ReadOnlyRight;

    public bool Equals(LaunchOptions? other) => other is not null
        && AutoMerge == other.AutoMerge && Force == other.Force && ReviewConflicts == other.ReviewConflicts && IgnoreUnimportant == other.IgnoreUnimportant
        && FavorLeft == other.FavorLeft && FavorRight == other.FavorRight && NoBackups == other.NoBackups && ReadOnlyLeft == other.ReadOnlyLeft
        && ReadOnlyRight == other.ReadOnlyRight && Silent == other.Silent && Solo == other.Solo && ExpandAll == other.ExpandAll && CloseScript == other.CloseScript
        && Filters == other.Filters && SaveTarget == other.SaveTarget && QuickCompare == other.QuickCompare
        && Titles.SequenceEqual(other.Titles) && Vcs.SequenceEqual(other.Vcs);

    public override int GetHashCode() => HashCode.Combine(AutoMerge, Force, ReviewConflicts, Filters, QuickCompare, Silent);
}
