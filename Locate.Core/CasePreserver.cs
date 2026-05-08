namespace Locate.Core;

internal enum CaseClass
{
    NoLetters,
    AllLower,
    AllUpper,
    TitleCase,
    Mixed,
}

internal static class CasePreserver
{
    public static CaseClass Classify(ReadOnlySpan<char> s)
    {
        var sawUpper = false;
        var sawLower = false;
        var firstLetterSeen = false;
        var firstIsUpper = false;
        var anyNonFirstUpper = false;

        foreach (var c in s)
        {
            if (char.IsUpper(c))
            {
                if (!firstLetterSeen)
                {
                    firstIsUpper = true;
                    firstLetterSeen = true;
                }
                else
                {
                    anyNonFirstUpper = true;
                }
                sawUpper = true;
            }
            else if (char.IsLower(c))
            {
                if (!firstLetterSeen)
                {
                    firstIsUpper = false;
                    firstLetterSeen = true;
                }
                sawLower = true;
            }
        }

        if (!firstLetterSeen) return CaseClass.NoLetters;
        if (sawUpper && !sawLower) return CaseClass.AllUpper;
        if (sawLower && !sawUpper) return CaseClass.AllLower;
        if (firstIsUpper && !anyNonFirstUpper) return CaseClass.TitleCase;
        return CaseClass.Mixed;
    }

    public static string Apply(string replacement, ReadOnlySpan<char> originalMatched)
    {
        if (replacement.Length == 0)
            return replacement;

        return Classify(originalMatched) switch
        {
            CaseClass.AllUpper => replacement.ToUpperInvariant(),
            CaseClass.AllLower => replacement.ToLowerInvariant(),
            CaseClass.TitleCase => ApplyTitleCase(replacement),
            _ => replacement,
        };
    }

    private static string ApplyTitleCase(string s)
    {
        var first = char.ToUpperInvariant(s[0]);
        if (s.Length == 1)
            return first.ToString();
        return first + s[1..].ToLowerInvariant();
    }
}
