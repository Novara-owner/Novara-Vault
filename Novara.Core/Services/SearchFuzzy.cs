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
        
        foreach (var word in query.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
            if (!ContainsFuzzy(text, word)) return false;
        return true;
    }

    /// <summary>Exact substring first; otherwise a bounded subsequence (letters in order, nearby).</summary>
    public static bool ContainsFuzzy(string text, string word)
    {
        if (text.IndexOf(word, StringComparison.Ordinal) >= 0) return true;
        // Subsequence matching only for longer words: very short words stay exact to avoid over-matching.
        if (word.Length < 4) return false;
        // N3-44: recursion depth in MatchFrom is proportional to word.Length - an absurdly long
        // query word (pasted blob / programmatic call) with a hot first-letter match could walk the
        // stack into StackOverflowException. A word longer than the text can never be a subsequence
        // of it, so that case is a hard miss; anything still plausible is bounded well below
        // dangerous depths by this pair of guards (real queries are far shorter than 512 chars).
        if (word.Length > text.Length) return false;
        if (word.Length > 512) return false;
        // Bound how many unrelated chars may sit between two consecutive matched letters.
        // Keeps typo tolerance (nearby transpositions) while rejecting far-scattered matches.
        const int maxGap = 2;
        // N2-28: backtracking match from each candidate start (N1-34 added per-start retries, but
        // WITHIN a start the greedy pass still had no backtracking - e.g. "abcd" vs "abbZZcXd":
        // taking b@1 forces c@5 to break the gap while b@2 allows the legal c@5-d@7 chain).
        // N4-09: MatchFrom is a pure function of (pos, wi), so failed states are memoized in
        
        // 3^word.Length on all-same-char words) and freezes the synchronous UI search.
        var failed = new HashSet<(int Pos, int Wi)>();
        for (int start = 0; start < text.Length; start++)
        {
            if (text[start] != word[0]) continue;
            if (MatchFrom(text, start + 1, 1, word, maxGap, failed)) return true;
        }
        return false;
    }

    /// <summary>N2-28: recursive backtracking subsequence match - at each letter every occurrence
    /// within the gap window is tried, so an early greedy pick can never strand a legal chain.
    /// N4-09: `failed` memoizes (pos, wi) states already proven dead, capping the work at
    
    private static bool MatchFrom(string text, int pos, int wi, string word, int maxGap, HashSet<(int Pos, int Wi)> failed)
    {
        if (wi == word.Length) return true;
        int limit = Math.Min(text.Length, pos + maxGap + 1);
        for (int i = pos; i < limit; i++)
        {
            if (text[i] != word[wi]) continue;
            if (failed.Contains((i + 1, wi + 1))) continue; // memo hit: this state already failed
            if (MatchFrom(text, i + 1, wi + 1, word, maxGap, failed)) return true;
            failed.Add((i + 1, wi + 1)); // proven dead - never re-explore it
        }
        return false;
    }
}
