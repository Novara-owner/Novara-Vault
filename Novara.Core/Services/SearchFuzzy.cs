namespace Novara.Services;

/// <summary>
/// Global-search fuzzy matching, kept in Novara.Core so it is unit-testable and shared.
/// Matching rules (2026-08-21): exact substring wins; otherwise a constrained subsequence match.
/// The subsequence is deliberately bounded so that long random texts (API keys, URLs) do not match a
/// query by merely containing its letters scattered far apart (e.g. searching "novara" no longer hits
/// an unrelated entry whose key happens to contain n,o,v,a,r,a in order).
/// </summary>
public static class SearchFuzzy
{
    /// <summary>Query split on whitespace; every word must match (AND). Empty query matches everything.</summary>
    public static bool MatchesQuery(string text, string query)
    {
        if (string.IsNullOrEmpty(query)) return true;
        foreach (var word in query.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            if (!ContainsFuzzy(text, word)) return false;
        return true;
    }

    /// <summary>Exact substring first; otherwise a bounded subsequence (letters in order, nearby).</summary>
    public static bool ContainsFuzzy(string text, string word)
    {
        if (text.IndexOf(word, StringComparison.Ordinal) >= 0) return true;
        // Subsequence matching only for longer words: very short words stay exact to avoid over-matching.
        if (word.Length < 4) return false;
        // Bound how many unrelated chars may sit between two consecutive matched letters.
        // Keeps typo tolerance (nearby transpositions) while rejecting far-scattered matches.
        const int maxGap = 2;
        int j = 0;
        int prev = -1;
        for (int i = 0; i < text.Length && j < word.Length; i++)
        {
            if (text[i] != word[j]) continue;
            if (prev >= 0 && i - prev - 1 > maxGap) return false;
            prev = i;
            j++;
        }
        return j == word.Length;
    }
}
