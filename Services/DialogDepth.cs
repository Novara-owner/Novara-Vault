using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Novara.Services;

public static class DialogDepth
{



    private sealed class BlurState
    {
        public Microsoft.UI.Composition.SpriteVisual? Visual;
        public bool HandlerAttached;
        public bool CoverWindow;
        public FrameworkElement? Root;
    }

    private static readonly ConditionalWeakTable<FrameworkElement, BlurState> BlurStates = new();







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




            ApplyBackdropBlur(scrim, coverWindow: true);

            scrim.RegisterPropertyChangedCallback(UIElement.VisibilityProperty, (d, _) =>
            {
                if (((UIElement)d).Visibility == Visibility.Visible &&
                    BlurStates.TryGetValue((FrameworkElement)d, out var revealed))
                    UpdateBlurGeometry(revealed, (FrameworkElement)d);
            });


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

            scrim.Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(0, 0, 0, 0));
        }
    }







    public static void ApplyBackdropBlur(FrameworkElement host, bool coverWindow = false)
    {
        try
        {
            var compositor = Microsoft.UI.Xaml.Hosting.ElementCompositionPreview.GetElementVisual(host).Compositor;
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
            visual.Size = new System.Numerics.Vector2((float)host.ActualSize.X, (float)host.ActualSize.Y);

            var state = BlurStates.GetValue(host, _ => new BlurState());
            state.CoverWindow = coverWindow;
            state.Root = coverWindow ? App.MainWindow?.Content as FrameworkElement : null;
            if (!state.HandlerAttached)
            {
                state.HandlerAttached = true;
                host.SizeChanged += (_, _) => UpdateBlurGeometry(state, host);
            }




            state.Visual = visual;
            UpdateBlurGeometry(state, host);
            Microsoft.UI.Xaml.Hosting.ElementCompositionPreview.SetElementChildVisual(host, visual);
        }
        catch
        {

        }
    }



    private static void UpdateBlurGeometry(BlurState state, FrameworkElement host)
    {
        if (state.Visual is not { } visual) return;

        if (state.CoverWindow && state.Root is null)
            state.Root = App.MainWindow?.Content as FrameworkElement;
        if (state.CoverWindow && state.Root is { } root && root.ActualSize.X > 0 && root.ActualSize.Y > 0)
        {
            try
            {
                var origin = host.TransformToVisual(root).TransformPoint(new Windows.Foundation.Point(0, 0));
                visual.Offset = new System.Numerics.Vector3((float)-origin.X, (float)-origin.Y, 0);
                visual.Size = new System.Numerics.Vector2((float)root.ActualSize.X, (float)root.ActualSize.Y);
                return;
            }
            catch
            {

            }
        }
        visual.Offset = System.Numerics.Vector3.Zero;
        visual.Size = new System.Numerics.Vector2((float)host.ActualSize.X, (float)host.ActualSize.Y);
    }

    private static void OnOverlayVisibilityChanged(DependencyObject d, DependencyProperty p)
    {
        if (((UIElement)d).Visibility == Visibility.Visible) VeilShow();
        else VeilHide();
    }



    public static void AttachPair(Grid scrim, Border dialog, bool driveChrome = true)
    {
        ApplyBlurScrim(scrim);
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
