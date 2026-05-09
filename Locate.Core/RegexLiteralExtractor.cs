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

                // \p{Cat} / \P{Cat} — Unicode-category escape; consume the {…} block.
                if (next is 'p' or 'P')
                {
                    EndRun(current, ref best);
                    var bracePos = i + 2;
                    if (bracePos < pattern.Length && pattern[bracePos] == '{')
                    {
                        var braceClose = pattern.IndexOf('}', bracePos);
                        i = braceClose < 0 ? pattern.Length : SkipQuantifierIfAny(pattern, braceClose + 1);
                    }
                    else
                    {
                        i = SkipQuantifierIfAny(pattern, i + 2);
                    }
                    continue;
                }

                // \xHH (hex), \uHHHH (Unicode), \cX (control-char), \k<name> (named back-ref) all
                // produce a single character that we conservatively treat as a run-breaker — without
                // running the regex parser ourselves we can't know which char it is, and getting it
                // wrong is a silent missed-match bug. Skipping past the escape's payload.
                if (next is 'x' or 'u' or 'c' or 'k' or 'a' or 'e')
                {
                    EndRun(current, ref best);
                    i = next switch
                    {
                        'x' => i + 4,                                     // \xHH
                        'u' => i + 6,                                     // \uHHHH
                        'c' => i + 3,                                     // \cX
                        'k' => SkipUntilClosingAngle(pattern, i + 2),     // \k<name>
                        _   => i + 2,                                     // \a, \e
                    };
                    if (i > pattern.Length) i = pattern.Length;
                    i = SkipQuantifierIfAny(pattern, i);
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
                var escapeBreaksRun = IsRepeatingQuantifier(pattern, afterEscape);
                i = SkipQuantifierIfAny(pattern, afterEscape);
                if (escapeBreaksRun) EndRun(current, ref best);
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
                    // SkipQuantifierIfAny may advance past the closing '}' to pattern.Length when the
                    // quantifier ends the pattern (e.g. "foo.{3}"); we must NOT then read pattern[i]
                    // — that's a guaranteed IndexOutOfRangeException.
                    EndRun(current, ref best);
                    i = SkipQuantifierIfAny(pattern, i);
                    if (i >= pattern.Length) continue;
                    if (pattern[i] == '{') i++;             // ensure forward progress on adjacent bare '{'
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
                    // If a {n>1} or {n,m>1} follows, the char is required AT LEAST n times — meaning the
                    // run can't continue with the next pattern char as if it were adjacent (e.g. "foa{2}b"
                    // matches "foaab", which doesn't contain the substring "foab"). End the run so we
                    // don't claim a non-existent contiguous literal.
                    var brokeRun = IsRepeatingQuantifier(pattern, quantPos);
                    i = SkipQuantifierIfAny(pattern, quantPos);
                    if (brokeRun) EndRun(current, ref best);
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

    private static int SkipUntilClosingAngle(string pattern, int startAt)
    {
        // \k<name> — find the matching '>'. If absent (malformed), bail to end of pattern so the
        // outer loop terminates rather than spinning.
        var angleClose = pattern.IndexOf('>', startAt);
        return angleClose < 0 ? pattern.Length : angleClose + 1;
    }

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

    /// <summary>True iff a quantifier at <paramref name="pos"/> repeats the preceding atom more than
    /// once — i.e. the atom appears ≥ 2 times in the match. After such a quantifier the previous run
    /// can't be claimed as a contiguous literal: <c>foa{2}b</c> matches <c>foaab</c>, but the
    /// substring <c>foab</c> isn't in <c>foaab</c>. <c>?</c>, <c>*</c>, <c>+</c>, <c>{1}</c>,
    /// <c>{0,1}</c>, <c>{0,n}</c> all leave the prefix contiguous and return false here.</summary>
    private static bool IsRepeatingQuantifier(string pattern, int pos)
    {
        if (pos >= pattern.Length) return false;
        if (pattern[pos] != '{') return false;            // ?, *, + each repeat 0/1 times — prefix stays contiguous
        var close = pattern.IndexOf('}', pos);
        if (close < 0) return false;
        var inside = pattern.AsSpan(pos + 1, close - pos - 1);
        if (inside.Length == 0) return false;
        // Parse minimum count. {0}, {0,m}, {1}, {1,m} all keep the prefix contiguous; anything else
        // (≥ 2 forced repetitions of the just-appended char) breaks contiguity.
        var commaIndex = inside.IndexOf(',');
        var minSpan = commaIndex < 0 ? inside : inside[..commaIndex];
        if (!int.TryParse(minSpan, out var min)) return false;
        return min >= 2;
    }

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
