namespace Collaterale.Core.Diff;

/// <summary>Splits a line into words, whitespace runs and single punctuation characters (for word-level diffs and similarity).</summary>
public static class Tokenizer
{
    public readonly record struct Token(int Start, int Length);

    public static List<Token> Tokens(string s)
    {
        var tokens = new List<Token>();
        var i = 0;
        while (i < s.Length)
        {
            var start = i;
            if (IsWord(s[i]))
            {
                while (i < s.Length && IsWord(s[i])) { i++; }
            }
            else if (char.IsWhiteSpace(s[i]))
            {
                while (i < s.Length && char.IsWhiteSpace(s[i])) { i++; }
            }
            else
            {
                i++;
                if (char.IsHighSurrogate(s[start]) && i < s.Length && char.IsLowSurrogate(s[i])) { i++; } // keep emoji etc. whole
            }

            tokens.Add(new Token(start, i - start));
        }

        return tokens;
    }

    private static bool IsWord(char c) => char.IsLetterOrDigit(c) || c == '_';
}
