namespace Collaterale.Core.Languages;

/// <summary>Patterns for the parts of a line that change every time (dates, times, durations) and carry no meaning for a comparison.</summary>
public static class Timestamps
{
    private const string Months = "(?:Jan(?:uary)?|Feb(?:ruary)?|Mar(?:ch)?|Apr(?:il)?|May|Jun(?:e)?|Jul(?:y)?|Aug(?:ust)?|Sep(?:t(?:ember)?)?|Oct(?:ober)?|Nov(?:ember)?|Dec(?:ember)?)";
    private const string Days = "(?:Mon|Tue|Wed|Thu|Fri|Sat|Sun)[a-z]*";
    private const string Zone = @"(?:\s?(?:Z|UTC|GMT|[+-]\d{2}:?\d{2}))?";
    private const string Time = @"\d{1,2}:\d{2}(?::\d{2}(?:[.,]\d{1,9})?)?(?:\s?[AaPp][Mm])?";

    /// <summary>Regular expressions, most specific first (a date-time is removed as a whole before its date and time parts would be).</summary>
    public static readonly IReadOnlyList<string> Patterns =
    [
        // 2024-03-01T08:15:30.123Z, 2024-03-01 08:15, 2024/03/01 08:15:30 +0100
        $@"\b\d{{4}}[-/.]\d{{1,2}}[-/.]\d{{1,2}}[T ]{Time}{Zone}",
        // Fri, 01 Mar 2024 08:15:30 GMT, Friday March 1 2024
        $@"\b(?:{Days},?\s+)?\d{{1,2}}\s+{Months}\s+\d{{4}}(?:\s+{Time}{Zone})?",
        $@"\b(?:{Days},?\s+)?{Months}\s+\d{{1,2}}(?:st|nd|rd|th)?,?\s+\d{{4}}(?:\s+{Time}{Zone})?",
        // 2024-03-01, 2024/03/01, 01/03/2024, 01.03.2024
        @"\b\d{4}[-/.]\d{1,2}[-/.]\d{1,2}\b",
        @"\b\d{1,2}[-/.]\d{1,2}[-/.]\d{2,4}\b",
        // 08:15, 08:15:30.123, 8:15 PM
        $@"\b{Time}{Zone}\b",
    ];
}
