using Windows.Security.Credentials;
using Windows.Security.Credentials.UI;

namespace Novara.Services;

public static class WindowsHelloService
{
    private const string Resource = "Novara";
    private const string UserName = "winhello";


    public static async Task<bool> IsAvailableAsync()
    {
        try { return await UserConsentVerifier.CheckAvailabilityAsync() == UserConsentVerifierAvailability.Available; }
        catch { return false; }
    }


    public static bool IsEnabled()
    {
        try
        {
            var vault = new PasswordVault();
            return vault.FindAllByResource(Resource).Any(c => c.UserName == UserName);
        }
        catch { return false; }
    }


    public static bool Enable(string password)
    {
        try
        {
            Disable();
            var vault = new PasswordVault();
            vault.Add(new PasswordCredential(Resource, UserName, password));
            return true;
        }
        catch { return false; }
    }


    public static void Disable()
    {
        try
        {
            var vault = new PasswordVault();
            foreach (var c in vault.FindAllByResource(Resource))
                if (c.UserName == UserName)
                {
                    try { c.RetrievePassword(); } catch { }
                    vault.Remove(c);
                }
        }
        catch { }
    }



    public static bool Update(string newPassword) => Enable(newPassword);


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


    public static async Task<UserConsentVerificationResult> RequestVerificationAsync(string message)
    {
        try { return await UserConsentVerifier.RequestVerificationAsync(message); }
        catch (System.Exception ex)
        {

            System.Diagnostics.Debug.WriteLine($"Windows Hello verification error: {ex.Message}");
            return UserConsentVerificationResult.DeviceBusy;
        }
    }
}
