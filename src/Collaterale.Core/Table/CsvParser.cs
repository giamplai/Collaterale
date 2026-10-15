using System.Text;

namespace Collaterale.Core.Table;

/// <summary>A parsed delimited text file: the delimiter that was used and the rows of cells (rows may have different lengths).</summary>
public sealed record ParsedTable(char Delimiter, IReadOnlyList<string[]> Rows)
{
    public int ColumnCount => Rows.Count == 0 ? 0 : Rows.Max(r => r.Length);
}

/// <summary>RFC 4180 style CSV/TSV parser: quoted fields, doubled quotes, line breaks inside quotes, any of CRLF/LF/CR.</summary>
public static class CsvParser
{
    private static readonly char[] Candidates = [',', ';', '\t', '|'];

    /// <summary>Parses <paramref name="text"/>; a null <paramref name="delimiter"/> means detect it.</summary>
    public static ParsedTable Parse(string text, char? delimiter = null)
    {
        var d = delimiter ?? Detect(text);
        return new ParsedTable(d, ParseRows(text, d));
    }

    /// <summary>The candidate delimiter that gives the most consistent column count over the first rows (ties go to the one with more columns).</summary>
    public static char Detect(string text)
    {
        var sample = text.Length > 200_000 ? text[..200_000] : text;
        char best = ',';
        double bestScore = -1;
        foreach (var c in Candidates)
        {
            var rows = ParseRows(sample, c).Take(60).ToList();
            if (rows.Count == 0) { continue; }
            var counts = rows.Select(r => r.Length).ToList();
            var mode = counts.GroupBy(x => x).OrderByDescending(g => g.Count()).First();
            if (mode.Key < 2) { continue; }                                           // a delimiter that never splits is not a delimiter
            var score = (double)mode.Count() / counts.Count * 1000 + mode.Key;       // consistency first, then column count
            if (score > bestScore) { bestScore = score; best = c; }
        }

        return best;
    }

    public static List<string[]> ParseRows(string text, char delimiter)
    {
        var rows = new List<string[]>();
        var row = new List<string>();
        var field = new StringBuilder();
        var inQuotes = false;
        var fieldWasQuoted = false;
        var i = 0;

        void EndField()
        {
            row.Add(field.ToString());
            field.Clear();
            fieldWasQuoted = false;
        }

        void EndRow()
        {
            EndField();
            rows.Add([.. row]);
            row.Clear();
        }

        while (i < text.Length)
        {
            var ch = text[i];
            if (inQuotes)
            {
                if (ch == '"')
                {
                    if (i + 1 < text.Length && text[i + 1] == '"') { field.Append('"'); i += 2; continue; }
                    inQuotes = false;
                    i++;
                    continue;
                }

                field.Append(ch);
                i++;
                continue;
            }

            if (ch == '"' && field.Length == 0 && !fieldWasQuoted)
            {
                inQuotes = true;
                fieldWasQuoted = true;
                i++;
            }
            else if (ch == delimiter)
            {
                EndField();
                i++;
            }
            else if (ch == '\r' || ch == '\n')
            {
                EndRow();
                i += ch == '\r' && i + 1 < text.Length && text[i + 1] == '\n' ? 2 : 1;
            }
            else
            {
                field.Append(ch);
                i++;
            }
        }

        // the last row has no terminating newline; an empty trailing line is not a row
        if (field.Length > 0 || row.Count > 0 || fieldWasQuoted)
        {
            EndRow();
        }

        return rows;
    }

    /// <summary>Writes a field the way a spreadsheet would: quoted only when it has to be.</summary>
    public static string Escape(string value, char delimiter)
        => value.IndexOfAny([delimiter, '"', '\r', '\n']) >= 0 ? "\"" + value.Replace("\"", "\"\"") + "\"" : value;
}
