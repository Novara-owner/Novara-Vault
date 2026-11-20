namespace Novara.Services;

internal static class PasswordFeedback
{

    private const string InvalidState = "Invalid";
    private const string ValidState = "Valid";

    public static void ShowError(Microsoft.UI.Xaml.Controls.PasswordBox box)
    {

        box.BorderBrush = App.GetBrush("AppDangerTextBrush");

        Microsoft.UI.Xaml.VisualStateManager.GoToState(box, InvalidState, false);
    }

    public static void ClearError(Microsoft.UI.Xaml.Controls.PasswordBox box)
    {
        box.ClearValue(Microsoft.UI.Xaml.Controls.PasswordBox.BorderBrushProperty);
        Microsoft.UI.Xaml.VisualStateManager.GoToState(box, ValidState, false);
    }
}
