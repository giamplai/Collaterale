namespace Collaterale.Core.Support;

/// <summary>
/// Type-ahead in a list: typing letters moves to the first name that starts with them; a pause longer than the limit starts a new word,
/// and repeating one letter cycles through the names that start with it.
/// </summary>
public sealed class IncrementalSearch(int resetMilliseconds, Func<DateTime> clock)
{
    private DateTime _last = DateTime.MinValue;

    public string Buffer { get; private set; } = "";

    public void Reset() => Buffer = "";

    /// <summary>Returns the index to move to; the current one when nothing matches.</summary>
    public int Type(char c, IReadOnlyList<string> names, int current)
    {
        var now = clock();
        if (resetMilliseconds <= 0 || (now - _last).TotalMilliseconds > resetMilliseconds) { Buffer = ""; }
        _last = now;
        Buffer += c;

        var repeated = Buffer.Length > 1 && Buffer.All(x => char.ToLowerInvariant(x) == char.ToLowerInvariant(Buffer[0]));
        var prefix = repeated ? Buffer[..1] : Buffer;
        // one letter (or the same letter again) moves past the current item; a longer word may stay on it
        var start = repeated || Buffer.Length == 1 ? current + 1 : Math.Max(current, 0);
        for (var step = 0; step < names.Count; step++)
        {
            var i = (start + step) % names.Count;
            if (names[i].StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) { return i; }
        }

        return current;
    }
}
