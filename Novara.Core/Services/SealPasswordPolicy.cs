using System.Text;

namespace Novara.Services;


public enum SealPasswordIssue
{
    None = 0,


    TooShort,


    WeakFragment,


    TooFewClasses,


    Sequence,
}




























public static class SealPasswordPolicy
{
    public const int MinimumLength = 12;


    public const int MinimumSequenceRun = 6;


    private static readonly string[] Fragments =
    [
        "password", "qwerty", "123456", "novara", "admin",
        "letmein", "iloveyou", "111111", "abc123", "000000",
    ];





    public static (bool Ok, SealPasswordIssue Issue) Evaluate(string? password)
    {
        if (password is null || password.Length < MinimumLength)
            return (false, SealPasswordIssue.TooShort);

        if (ContainsFragment(password))
            return (false, SealPasswordIssue.WeakFragment);

        if (CountClasses(password) < 3)
            return (false, SealPasswordIssue.TooFewClasses);

        if (HasLongRun(password))
            return (false, SealPasswordIssue.Sequence);

        return (true, SealPasswordIssue.None);
    }


    public static BackupPasswordStrength Rate(string? password) => PasswordStrength.Rate(password);





    private static bool ContainsFragment(string password)
    {
        var plain = password.ToLowerInvariant();
        var folded = Fold(password);

        foreach (var fragment in Fragments)
        {
            if (plain.Contains(fragment, StringComparison.Ordinal)) return true;
            if (folded.Contains(fragment, StringComparison.Ordinal)) return true;
        }

        return false;
    }

    private static string Fold(string password)
    {
        var builder = new StringBuilder(password.Length);
        foreach (var ch in password)
        {
            var c = char.ToLowerInvariant(ch);
            c = c switch
            {
                '0' => 'o',
                '1' => 'l',
                '3' => 'e',
                '4' => 'a',
                '5' => 's',
                '7' => 't',
                '@' => 'a',
                '$' => 's',
                _ => c,
            };

            if (char.IsLetterOrDigit(c)) builder.Append(c);
        }

        return builder.ToString();
    }





    private static int CountClasses(string password)
    {
        var hasLowerOrUncasedLetter = false;
        var hasUpper = false;
        var hasDigit = false;
        var hasSymbol = false;

        foreach (var ch in password)
        {
            if (char.IsDigit(ch)) hasDigit = true;
            else if (char.IsUpper(ch)) hasUpper = true;
            else if (char.IsLetter(ch)) hasLowerOrUncasedLetter = true;
            else hasSymbol = true;
        }

        return (hasLowerOrUncasedLetter ? 1 : 0)
               + (hasUpper ? 1 : 0)
               + (hasDigit ? 1 : 0)
               + (hasSymbol ? 1 : 0);
    }

    private static bool HasLongRun(string password)
    {
        var ascending = 1;
        var descending = 1;

        for (var i = 1; i < password.Length; i++)
        {
            var step = password[i] - password[i - 1];
            ascending = step == 1 ? ascending + 1 : 1;
            descending = step == -1 ? descending + 1 : 1;

            if (ascending >= MinimumSequenceRun || descending >= MinimumSequenceRun) return true;
        }

        return false;
    }
}
