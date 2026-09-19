namespace Novara.Services;








public static class SearchFuzzy
{

    public static bool MatchesQuery(string text, string query)
    {
        if (string.IsNullOrEmpty(query)) return true;

        foreach (var word in query.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
            if (!ContainsFuzzy(text, word)) return false;
        return true;
    }


    public static bool ContainsFuzzy(string text, string word)
    {
        if (text.IndexOf(word, StringComparison.Ordinal) >= 0) return true;

        if (word.Length < 4) return false;





        if (word.Length > text.Length) return false;
        if (word.Length > 512) return false;


        const int maxGap = 2;






        var failed = new HashSet<(int Pos, int Wi)>();
        for (int start = 0; start < text.Length; start++)
        {
            if (text[start] != word[0]) continue;
            if (MatchFrom(text, start + 1, 1, word, maxGap, failed)) return true;
        }
        return false;
    }





    private static bool MatchFrom(string text, int pos, int wi, string word, int maxGap, HashSet<(int Pos, int Wi)> failed)
    {
        if (wi == word.Length) return true;
        int limit = Math.Min(text.Length, pos + maxGap + 1);
        for (int i = pos; i < limit; i++)
        {
            if (text[i] != word[wi]) continue;
            if (failed.Contains((i + 1, wi + 1))) continue;
            if (MatchFrom(text, i + 1, wi + 1, word, maxGap, failed)) return true;
            failed.Add((i + 1, wi + 1));
        }
        return false;
    }
}
