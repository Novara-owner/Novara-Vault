/* ========== PasswordStrength - Backup Password Strength Meter ==========
Function: Client-side heuristic strength rating for the encrypted backup password (design doc 9.2#6).
          Advisory signal only - the export never blocks on it (audit 2026-08-29: no hard interception,
          an advanced user must not get stuck; empty + mismatched confirm remain the only hard checks).
Corresponding UI: SettingsPage encrypted-backup dialog strength indicator (red/yellow/green text)
Logic Range: Whole file
*/
namespace Novara.Services;

public enum BackupPasswordStrength { Weak, Medium, Strong }

public static class PasswordStrength
{
    /// <summary>
    /// Heuristic rating for the strength indicator. Rules: shorter than 6 chars, a single character
    /// class (digits-only etc. - length alone never helps) or empty = Weak; 12+ chars with 3+ classes
    /// = Strong; everything between = Medium.
    /// </summary>
    public static BackupPasswordStrength Rate(string? password)
    {
        if (string.IsNullOrEmpty(password)) return BackupPasswordStrength.Weak;
        int classes = 0;
        if (password.Any(char.IsLower)) classes++;
        if (password.Any(char.IsUpper)) classes++;
        if (password.Any(char.IsDigit)) classes++;
        if (password.Any(c => !char.IsLetterOrDigit(c))) classes++;
        if (password.Length < 6 || classes == 1) return BackupPasswordStrength.Weak;
        if (password.Length >= 12 && classes >= 3) return BackupPasswordStrength.Strong;
        return BackupPasswordStrength.Medium;
    }
}
