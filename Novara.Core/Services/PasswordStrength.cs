namespace Novara.Services;

public enum BackupPasswordStrength { Weak, Medium, Strong }

public static class PasswordStrength
{





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
