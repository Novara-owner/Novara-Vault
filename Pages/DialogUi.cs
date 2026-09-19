using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Windows.UI;
using Novara.Services;

namespace Novara.Pages;






internal static class DialogUi
{
    public static Grid? FindOverlayScrim(Grid overlay)
        => overlay.Children.OfType<Grid>().FirstOrDefault(g => g.Name.EndsWith("Scrim", StringComparison.Ordinal));

    public static void ShowOverlay(Grid root,
        Dictionary<Grid, int> gens, Dictionary<Grid, Border> dialogMap,
        Grid overlay, Border dialog, CompositeTransform transform)
    {
        gens[overlay] = System.Collections.Generic.CollectionExtensions.GetValueOrDefault(gens, overlay) + 1;
        var scrim = FindOverlayScrim(overlay);
        transform.ScaleX = 0.94; transform.ScaleY = 0.94; transform.TranslateY = 24;
        if (scrim != null) scrim.Opacity = 0;
        dialog.Opacity = 0;
        overlay.Visibility = Visibility.Visible;
        var sb = new Storyboard();
        if (scrim != null)
        {
            var si = new DoubleAnimation { To = 1, Duration = TimeSpan.FromMilliseconds(250) };
            Storyboard.SetTarget(si, scrim); Storyboard.SetTargetProperty(si, "Opacity"); sb.Children.Add(si);
        }
        var di = new DoubleAnimation { To = 1, Duration = TimeSpan.FromMilliseconds(300) };
        Storyboard.SetTarget(di, dialog); Storyboard.SetTargetProperty(di, "Opacity"); sb.Children.Add(di);
        Motion.AddDialogShowTransform(sb, transform);

        ClampDialogHeight(dialog, root);
        dialogMap[overlay] = dialog;
        sb.Begin();
    }





    public static void RegisterDialogClamp(Grid root, System.Collections.Generic.Dictionary<Grid, Border> dialogMap,
        Grid overlay, Border dialog)
    {
        ClampDialogHeight(dialog, root);
        dialogMap[overlay] = dialog;
    }

    public static void ClampDialogHeight(Border dialog, Grid root)
        => dialog.MaxHeight = Math.Max(360, root.ActualHeight - 60);


    public static void ClampDialogHeight(Border dialog, double viewport)
        => dialog.MaxHeight = viewport > 0 ? Math.Max(360, viewport - 60) : 720;

    public static void ReclampVisibleDialogs(Grid root, Dictionary<Grid, Border> dialogMap,
        DialogScrollFit? scrollFit = null)
    {
        foreach (var (overlay, dialog) in dialogMap)
            if (overlay.Visibility == Visibility.Visible)
                ClampDialogHeight(dialog, root);




        scrollFit?.RefitAll(root.ActualHeight);
    }












    public sealed class DialogScrollFit
    {


        private readonly Dictionary<ScrollViewer, double> _designedCaps = new();

        private readonly Dictionary<ScrollViewer, double> _chromeFloors = new();

        public void Fit(ScrollViewer scroll, double fallbackChrome, double viewportHeight)
        {
            _chromeFloors[scroll] = fallbackChrome;
            if (!_designedCaps.TryGetValue(scroll, out var designedCap))
                _designedCaps[scroll] = designedCap = scroll.MaxHeight;
            scroll.MaxHeight = Compute(designedCap, scroll, fallbackChrome, viewportHeight);
        }

        public void RefitAll(double viewportHeight)
        {
            foreach (var (scroll, fallbackChrome) in _chromeFloors)
            {
                var designedCap = _designedCaps.TryGetValue(scroll, out var cap) ? cap : double.PositiveInfinity;
                scroll.MaxHeight = Compute(designedCap, scroll, fallbackChrome, viewportHeight);
            }
        }

        private static double Compute(double designedCap, ScrollViewer scroll, double fallbackChrome, double viewportHeight)
            => Math.Max(120, Math.Min(designedCap, viewportHeight - 60 - ResolveChrome(scroll, fallbackChrome)));







        private static double ResolveChrome(ScrollViewer scroll, double fallbackChrome)
        {
            try
            {
                if (scroll.Parent is not Grid host || host.RowDefinitions.Count == 0) return fallbackChrome;
                int scrollRow = Grid.GetRow(scroll);
                double chrome = host.Padding.Top + host.Padding.Bottom + scroll.Margin.Top + scroll.Margin.Bottom;
                for (int i = 0; i < host.RowDefinitions.Count; i++)
                    if (i != scrollRow) chrome += host.RowDefinitions[i].ActualHeight;
                if (host.Parent is Border dialog) chrome += dialog.BorderThickness.Top + dialog.BorderThickness.Bottom;
                return Math.Max(chrome, fallbackChrome);
            }
            catch { return fallbackChrome; }
        }
    }

    public static void HideOverlay(Grid root,
        Dictionary<Grid, int> gens, Dictionary<Grid, Border> dialogMap,
        Grid overlay, Border dialog, CompositeTransform transform, Action onCompleted)
    {
        var gen = System.Collections.Generic.CollectionExtensions.GetValueOrDefault(gens, overlay);
        var scrim = FindOverlayScrim(overlay);
        var sb = new Storyboard();
        if (scrim != null)
        {
            var so = new DoubleAnimation { To = 0, Duration = TimeSpan.FromMilliseconds(200) };
            Storyboard.SetTarget(so, scrim); Storyboard.SetTargetProperty(so, "Opacity"); sb.Children.Add(so);
        }
        var di = new DoubleAnimation { To = 0, Duration = TimeSpan.FromMilliseconds(200) };
        Storyboard.SetTarget(di, dialog); Storyboard.SetTargetProperty(di, "Opacity"); sb.Children.Add(di);
        Motion.AddDialogHideTransform(sb, transform);
        sb.Completed += (_, _) =>
        {

            if (gen == System.Collections.Generic.CollectionExtensions.GetValueOrDefault(gens, overlay)) overlay.Visibility = Visibility.Collapsed;
            onCompleted();
        };
        sb.Begin();
    }

    public static async void FlashTextBox(
        Dictionary<TextBox, System.Threading.CancellationTokenSource> ctsMap,
        Dictionary<TextBox, Microsoft.UI.Xaml.Media.Brush> originalBgs, TextBox tb)
    {
        if (ctsMap.TryGetValue(tb, out var prev)) { prev.Cancel(); prev.Dispose(); }
        var cts = new System.Threading.CancellationTokenSource();
        ctsMap[tb] = cts;

        var orig = originalBgs.TryGetValue(tb, out var existing) ? existing : tb.Background;
        originalBgs[tb] = orig;
        tb.Background = new SolidColorBrush(Color.FromArgb(0x33, 0xFF, 0x45, 0x45));
        try { await System.Threading.Tasks.Task.Delay(600, cts.Token); }
        catch (System.Threading.Tasks.TaskCanceledException)
        {

            if (ctsMap.TryGetValue(tb, out var cur) && !ReferenceEquals(cur, cts)) return;
            tb.Background = orig;
            originalBgs.Remove(tb);
            ctsMap.Remove(tb);
            return;
        }
        tb.Background = orig;
        originalBgs.Remove(tb);
        ctsMap.Remove(tb);
        cts.Dispose();
    }

    public static void CancelAllFlashes(Dictionary<TextBox, System.Threading.CancellationTokenSource> ctsMap)
    {
        foreach (var cts in ctsMap.Values) { cts.Cancel(); cts.Dispose(); }
        ctsMap.Clear();
    }
}
