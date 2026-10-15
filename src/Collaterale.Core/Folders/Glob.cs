using System.Text;
using System.Text.RegularExpressions;

namespace Collaterale.Core.Folders;

/// <summary>File-name masks: '*' any run, '?' one character, several masks separated by ';' (or '|').</summary>
public sealed class MaskSet
{
    private readonly Regex[] _regexes;

    public MaskSet(string masks, bool ignoreCase)
    {
        var opts = RegexOptions.CultureInvariant | (ignoreCase ? RegexOptions.IgnoreCase : RegexOptions.None);
        _regexes = [.. masks.Split([';', '|'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(m => new Regex(ToRegex(m), opts))];
    }

    public bool IsEmpty => _regexes.Length == 0;

    public bool IsMatch(string name) => _regexes.Any(r => r.IsMatch(name));

    private static string ToRegex(string mask)
    {
        var sb = new StringBuilder("^");
        foreach (var ch in mask)
        {
            sb.Append(ch switch { '*' => ".*", '?' => ".", _ => Regex.Escape(ch.ToString()) });
        }

        return sb.Append('$').ToString();
    }
}
