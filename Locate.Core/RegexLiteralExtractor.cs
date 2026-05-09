using System.Text;

namespace Locate.Core;

/// <summary>
/// Walks a regex pattern statically and extracts the longest contiguous literal substring that the
/// regex must contain in order to match. Used as a pre-filter: when the literal isn't present in a
/// file's bytes, the file can't match the regex, so we skip running the (much more expensive) regex
/// engine entirely. Modeled after ripgrep's required-literal extraction — the single most impactful
/// regex optimization in real-world grep workloads.
/// </summary>
/// <remarks>
/// Conservative on purpose: returns null whenever it can't prove the literal is required, rather than
/// risk dropping a real match. False negatives (no prefilter when one was possible) just cost
/// performance; false positives (prefilter that excludes a real match) would silently drop results.
/// </remarks>
internal static class RegexLiteralExtractor
{
    /// <summary>Extracts the longest contiguous required-literal substring from <paramref name="pattern"/>,
    /// or <c>null</c> if none of length ≥ 2 can be proven required (e.g. top-level alternation, all-optional, etc.).</summary>
    public static string? TryExtractRequiredLiteral(string pattern)
    {
        if (string.IsNullOrEmpty(pattern)) return null;
        if (HasTopLevelAlternation(pattern)) return null;

        var current = new StringBuilder();
        var best = string.Empty;
        var i = 0;

        while (i < pattern.Length)
        {
            var c = pattern[i];

            if (c == '\\')
            {
                if (i + 1 >= pattern.Length) { EndRun(current, ref best); break; }
                var next = pattern[i + 1];

                if (IsClassOrAnchorEscape(next))
                {
                    // \d, \w, \s, \b, ... — non-literal. End the run, skip the escape and any quantifier.
                    EndRun(current, ref best);
                    i = SkipQuantifierIfAny(pattern, i + 2);
                    continue;
                }

                // Escaped literal char (e.g. \., \\, \(, \", \n). Account for trailing quantifier.
                var afterEscape = i + 2;
                if (IsOptionalQuantifier(pattern, afterEscape))
                {
                    EndRun(current, ref best);
                    i = SkipQuantifierIfAny(pattern, afterEscape);
                    continue;
                }
                current.Append(InterpretEscaped(next));
                i = SkipQuantifierIfAny(pattern, afterEscape);
                continue;
            }

            switch (c)
            {
                case '.':
                case '*':
                case '+':
                case '?':
                case '^':
                case '$':
                case '|':
                case '}':
                    EndRun(current, ref best);
                    i++;
                    continue;

                case '{':
                    // A bare '{' shouldn't appear without a preceding atom. End run defensively and skip.
                    EndRun(current, ref best);
                    i = SkipQuantifierIfAny(pattern, i);
                    if (i == pattern.Length - 1 || pattern[i] == '{') i++; // ensure progress
                    continue;

                case '(':
                    EndRun(current, ref best);
                    var afterGroup = SkipBalanced(pattern, i, '(', ')');
                    i = SkipQuantifierIfAny(pattern, afterGroup);
                    continue;

                case '[':
                    EndRun(current, ref best);
                    var afterClass = SkipCharClass(pattern, i);
                    i = SkipQuantifierIfAny(pattern, afterClass);
                    continue;

                default:
                {
                    // Plain literal char. If followed by an optional quantifier, the char isn't required.
                    var quantPos = i + 1;
                    if (IsOptionalQuantifier(pattern, quantPos))
                    {
                        EndRun(current, ref best);
                        i = SkipQuantifierIfAny(pattern, quantPos);
                        continue;
                    }
                    current.Append(c);
                    i = SkipQuantifierIfAny(pattern, quantPos);
                    continue;
                }
            }
        }
        EndRun(current, ref best);

        return best.Length >= 2 ? best : null;
    }

    private static void EndRun(StringBuilder current, ref string best)
    {
        if (current.Length > best.Length) best = current.ToString();
        current.Clear();
    }

    private static bool IsClassOrAnchorEscape(char c) =>
        c is 'd' or 'D' or 'w' or 'W' or 's' or 'S' or 'b' or 'B' or 'A' or 'Z' or 'z' or 'G';

    /// <summary>Translates the char following <c>\</c> into its literal value where the regex engine
    /// would match it as that char (e.g. <c>\n</c> → newline, <c>\.</c> → dot, <c>\\</c> → backslash).
    /// For unknown escapes we use the char as-is — same conservative behaviour as the .NET engine for
    /// non-special escapes.</summary>
    private static char InterpretEscaped(char c) => c switch
    {
        'n' => '\n',
        'r' => '\r',
        't' => '\t',
        'f' => '\f',
        'v' => '\v',
        '0' => '\0',
        _ => c,
    };

    /// <summary>True iff a quantifier starting at <paramref name="pos"/> makes the preceding atom
    /// optional (allows zero occurrences).</summary>
    private static bool IsOptionalQuantifier(string pattern, int pos)
    {
        if (pos >= pattern.Length) return false;
        var c = pattern[pos];
        if (c is '?' or '*') return true;
        if (c == '{')
        {
            var close = pattern.IndexOf('}', pos);
            if (close < 0) return false;
            var inside = pattern.AsSpan(pos + 1, close - pos - 1);
            // {0}, {0,n}, {0,} — optional. Anything else with a non-zero lower bound is required.
            if (inside.Length == 0) return false;
            if (inside[0] != '0') return false;
            if (inside.Length == 1) return true;
            return inside[1] == ',';
        }
        return false;
    }

    /// <summary>If <paramref name="pos"/> sits on a quantifier (<c>?</c>, <c>*</c>, <c>+</c>, or
    /// <c>{n,m}</c>, possibly followed by a lazy <c>?</c>), returns the index just past it; otherwise
    /// returns <paramref name="pos"/> unchanged.</summary>
    private static int SkipQuantifierIfAny(string pattern, int pos)
    {
        if (pos >= pattern.Length) return pos;
        var c = pattern[pos];
        if (c is '?' or '*' or '+')
        {
            var next = pos + 1;
            return next < pattern.Length && pattern[next] == '?' ? next + 1 : next;
        }
        if (c == '{')
        {
            var close = pattern.IndexOf('}', pos);
            if (close < 0) return pos + 1;
            var after = close + 1;
            return after < pattern.Length && pattern[after] == '?' ? after + 1 : after;
        }
        return pos;
    }

    /// <summary>Returns the index just past the matching <paramref name="close"/> (handles nesting and
    /// backslash escapes).</summary>
    private static int SkipBalanced(string pattern, int start, char open, char close)
    {
        var depth = 1;
        var i = start + 1;
        while (i < pattern.Length && depth > 0)
        {
            if (pattern[i] == '\\' && i + 1 < pattern.Length) { i += 2; continue; }
            if (pattern[i] == open) depth++;
            else if (pattern[i] == close) depth--;
            i++;
        }
        return i;
    }

    /// <summary>Returns the index just past the closing <c>]</c> of a character class. Handles the regex
    /// quirk that <c>]</c> as the first character of the class is literal (so <c>[]]</c> matches a single
    /// <c>]</c>) and that backslash escapes inside.</summary>
    private static int SkipCharClass(string pattern, int start)
    {
        var i = start + 1;
        if (i < pattern.Length && pattern[i] == '^') i++;
        if (i < pattern.Length && pattern[i] == ']') i++;
        while (i < pattern.Length && pattern[i] != ']')
        {
            if (pattern[i] == '\\' && i + 1 < pattern.Length) { i += 2; continue; }
            i++;
        }
        return i < pattern.Length ? i + 1 : i;
    }

    /// <summary>True iff the pattern contains a <c>|</c> at top level (outside any group or char class).
    /// Top-level alternation kills the prefilter — neither alternative is individually required.</summary>
    private static bool HasTopLevelAlternation(string pattern)
    {
        var groupDepth = 0;
        var inClass = false;
        for (var i = 0; i < pattern.Length; i++)
        {
            var c = pattern[i];
            if (c == '\\' && i + 1 < pattern.Length) { i++; continue; }
            if (inClass) { if (c == ']') inClass = false; continue; }
            switch (c)
            {
                case '[': inClass = true; break;
                case '(': groupDepth++; break;
                case ')': if (groupDepth > 0) groupDepth--; break;
                case '|': if (groupDepth == 0) return true; break;
            }
        }
        return false;
    }
}
