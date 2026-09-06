/* ========== DialogDepth - Dialog Floating Effect Infrastructure ==========
Function: Auto-wire depth treatment for every modal dialog - real z-shadow (ThemeShadow,
receiver = own scrim). Veil driving is now EXPLICIT: pages call VeilShow/VeilHide so the
window-level fullscreen scrim (ChromeScrim) fades in/out in lockstep with the dialog.
Zero intrusion: pages only call AttachContainer once in the constructor.
Corresponding UI: all *Overlay grids across pages + MainWindow window-level dialogs
Logic Range: Whole file - container scan, scrim/border pairing, explicit veil show/hide */
using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Novara.Services;

public static class DialogDepth
{
    /// <summary>
    /// Scan a root container (page root grid or window root grid): for every overlay group that
    /// contains a "*Scrim" grid + a Border dialog, attach a ThemeShadow to the dialog so the dialog
    /// casts a real z-shadow onto its own scrim. (driveChrome is kept for signature compatibility;
    /// the old value-coupled chrome veil was removed in 5.x and replaced by explicit VeilShow/VeilHide.)
    /// </summary>
    public static void AttachContainer(Grid container, bool driveChrome = true, bool autoVeil = false)
    {
        var processed = new HashSet<Grid>();
        var candidates = new List<Grid> { container };
        candidates.AddRange(container.Children.OfType<Grid>());

        foreach (var overlay in candidates)
        {
            Grid? scrim = null;
            Border? dialog = null;
            foreach (var c in overlay.Children)
            {
                if (c is Grid g && g.Name.EndsWith("Scrim", StringComparison.Ordinal)) scrim = g;
                else if (c is Border b) dialog ??= b;
            }
            if (scrim == null || dialog == null || !processed.Add(scrim)) continue;

            ApplyBlurScrim(scrim); 

            if (dialog.Shadow == null)
            {
                var shadow = new Microsoft.UI.Xaml.Media.ThemeShadow();
                shadow.Receivers.Add(scrim);
                dialog.Shadow = shadow;
                dialog.Translation = new System.Numerics.Vector3(0, 0, 32);
            }

            
            
            if (autoVeil) overlay.RegisterPropertyChangedCallback(UIElement.VisibilityProperty, OnOverlayVisibilityChanged);
        }
    }

    
    
    
    private static void ApplyBlurScrim(Grid scrim)
    {
        try
        {
            
            
            if (scrim.Background is Microsoft.UI.Xaml.Media.SolidColorBrush sb && sb.Color.A == 0) return;
            scrim.Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(0, 0, 0, 0));
            var compositor = Microsoft.UI.Xaml.Hosting.ElementCompositionPreview.GetElementVisual(scrim).Compositor;
            var blur = new Microsoft.Graphics.Canvas.Effects.GaussianBlurEffect
            {
                Name = "Blur",
                BlurAmount = 6f,
                BorderMode = Microsoft.Graphics.Canvas.Effects.EffectBorderMode.Soft, 
                Optimization = Microsoft.Graphics.Canvas.Effects.EffectOptimization.Balanced,
                Source = new Microsoft.UI.Composition.CompositionEffectSourceParameter("Backdrop"),
            };
            var brush = compositor.CreateEffectFactory(blur).CreateBrush();
            brush.SetSourceParameter("Backdrop", compositor.CreateBackdropBrush());
            var visual = compositor.CreateSpriteVisual();
            visual.Brush = brush;
            visual.Size = new System.Numerics.Vector2((float)scrim.ActualWidth, (float)scrim.ActualHeight);
            scrim.SizeChanged += (_, e) => visual.Size = new System.Numerics.Vector2((float)e.NewSize.Width, (float)e.NewSize.Height);
            Microsoft.UI.Xaml.Hosting.ElementCompositionPreview.SetElementChildVisual(scrim, visual);

            
            if (scrim.Parent is Grid overlay)
            {
                int idx = overlay.Children.IndexOf(scrim);
                overlay.Children.Insert(idx + 1, new Border
                {
                    Background = App.GetBrush("AppSurfaceBrush"),
                    Opacity = 0.3,
                    IsHitTestVisible = false,
                });
            }
        }
        catch
        {
            // blur is decorative - fall back to a transparent scrim so the dialog still works
            scrim.Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(0, 0, 0, 0));
        }
    }

    private static void OnOverlayVisibilityChanged(DependencyObject d, DependencyProperty p)
    {
        if (((UIElement)d).Visibility == Visibility.Visible) VeilShow();
        else VeilHide();
    }

    /// <summary>Explicit pairing for overlays the container scan cannot reach (nested deeper than
    /// one level, e.g. LockScreen's ForgotOverlay). Shadow only.</summary>
    public static void AttachPair(Grid scrim, Border dialog, bool driveChrome = true)
    {
        ApplyBlurScrim(scrim); // 9.3#5
        if (dialog.Shadow == null)
        {
            var shadow = new Microsoft.UI.Xaml.Media.ThemeShadow();
            shadow.Receivers.Add(scrim);
            dialog.Shadow = shadow;
            dialog.Translation = new System.Numerics.Vector3(0, 0, 32);
        }
    }

    
    public static void VeilShow() => App.MainWindow?.VeilShow();

    
    public static void VeilHide() => App.MainWindow?.VeilHide();

    
    public static void VeilHideImmediate() => App.MainWindow?.VeilHideImmediate();

    
    public static void VeilClear() => App.MainWindow?.VeilClear();
}
