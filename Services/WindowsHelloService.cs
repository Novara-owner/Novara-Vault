
using Windows.Security.Credentials;
using Windows.Security.Credentials.UI;

namespace Novara.Services;

public static class WindowsHelloService
{
    private const string Resource = "Novara";
    private const string UserName = "winhello";

    /// <summary>System has Windows Hello configured (availability check, no prompt).</summary>
    public static async Task<bool> IsAvailableAsync()
    {
        try { return await UserConsentVerifier.CheckAvailabilityAsync() == UserConsentVerifierAvailability.Available; }
        catch { return false; }
    }

    /// <summary>Credential exists = Windows Hello unlock is enabled (single source of truth).</summary>
    public static bool IsEnabled()
    {
        try
        {
            var vault = new PasswordVault();
            return vault.FindAllByResource(Resource).Any(c => c.UserName == UserName);
        }
        catch { return false; }
    }

    /// <summary>Store the current password into the vault (enables Windows Hello unlock).</summary>
    public static bool Enable(string password)
    {
        try
        {
            Disable(); // PasswordVault.Add throws on a duplicate (resource,userName) key
            var vault = new PasswordVault();
            vault.Add(new PasswordCredential(Resource, UserName, password));
            return true;
        }
        catch { return false; }
    }

    /// <summary>Remove the stored credential (disables Windows Hello unlock).</summary>
    public static void Disable()
    {
        try
        {
            var vault = new PasswordVault();
            foreach (var c in vault.FindAllByResource(Resource))
                if (c.UserName == UserName) vault.Remove(c);
        }
        catch { }
    }

    /// <summary>Re-store a changed password (keeps the credential consistent after a change).
    /// Callers MUST gate on IsEnabled() - Update enables the credential as a side effect.</summary>
    public static bool Update(string newPassword) => Enable(newPassword);

    /// <summary>Read the stored password back (returns null if absent/unreadable).</summary>
    public static string? TryGetPassword()
    {
        try
        {
            var vault = new PasswordVault();
            foreach (var c in vault.FindAllByResource(Resource))
            {
                if (c.UserName != UserName) continue;
                c.RetrievePassword();
                return c.Password;
            }
            return null;
        }
        catch { return null; }
    }

    /// <summary>Pop the native Windows Hello prompt (message shown inside the system dialog).</summary>
    public static async Task<UserConsentVerificationResult> RequestVerificationAsync(string message)
    {
        try { return await UserConsentVerifier.RequestVerificationAsync(message); }
        catch { return UserConsentVerificationResult.DeviceNotPresent; }
    }
}
