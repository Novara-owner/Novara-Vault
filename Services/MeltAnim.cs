using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Microsoft.UI.Xaml;

namespace Novara.Services;










public static class MeltAnim
{
    private sealed class Rec { public System.EventHandler<object>? Handler; public double Val; }
    private static readonly ConditionalWeakTable<FrameworkElement, Rec> _active = new();





    public static void Begin(FrameworkElement panel, bool expand, double topMargin = 0, double? restHeight = null, System.Action? completed = null, double? startHeightOverride = null)
    {
        try
        {
            var targetVal = expand ? 1.0 : 0.0;
            bool foldToRest = restHeight.HasValue;
            double rest = restHeight ?? 0;
            double? resumeHeight = null;
            double resumeOpacity = expand ? 0.0 : 1.0;
            if (_active.TryGetValue(panel, out var hold))
            {
                if (hold.Handler != null)
                {
                    if (hold.Val == targetVal) return;

                    if (!double.IsNaN(panel.Height)) resumeHeight = panel.Height;
                    else if (startHeightOverride.HasValue) resumeHeight = startHeightOverride;
                    resumeOpacity = panel.Opacity;
                    try { Microsoft.UI.Xaml.Media.CompositionTarget.Rendering -= hold.Handler; } catch { }
                    _active.Remove(panel);
                }
                else
                {

                    if (hold.Val == targetVal && panel.Visibility == Visibility.Visible) return;
                    _active.Remove(panel);
                }
            }

            if (!expand && panel.Visibility == Visibility.Collapsed) return;

            panel.Visibility = Visibility.Visible;
            panel.Height = double.NaN;
            panel.UpdateLayout();
            double full = panel.ActualHeight;
            double start = expand ? resumeHeight ?? (foldToRest ? rest : 0) : resumeHeight ?? full;
            double target = expand ? full : (foldToRest ? rest : 0);
            if (expand && full <= 0)
            {





                panel.Opacity = 1;
                panel.Height = double.NaN;
                completed?.Invoke();
                return;
            }
            if (full > 0) start = System.Math.Clamp(start, 0, full);
            panel.Height = start;
            double mStart = topMargin > 0 ? (resumeHeight.HasValue ? panel.Margin.Top : expand ? 0 : topMargin) : 0;
            double mEnd = expand ? topMargin : 0;
            double opStart = foldToRest ? 1.0 : resumeOpacity;
            double opEnd = foldToRest ? 1.0 : (expand ? 1.0 : 0.0);
            panel.Opacity = opStart;
            Microsoft.UI.Xaml.Media.RectangleGeometry? clip = null;
            if (foldToRest)
            {
                clip = new Microsoft.UI.Xaml.Media.RectangleGeometry();
                panel.Clip = clip;
            }
            var sw = System.Diagnostics.Stopwatch.StartNew();

            System.EventHandler<object> handler = null!;
            handler = (s, a) =>
            {
                try
                {
                    if (!_active.TryGetValue(panel, out var rec) || !ReferenceEquals(rec.Handler, handler))
                    { try { Microsoft.UI.Xaml.Media.CompositionTarget.Rendering -= handler; } catch { } return; }

                    var t = System.Math.Min(1.0, sw.Elapsed.TotalMilliseconds / 220.0);
                    var eOut = 1 - System.Math.Pow(1 - t, 3);
                    var eIn = System.Math.Pow(t, 3);
                    var e = expand ? eOut : eIn;
                    var h = start + (target - start) * e;
                    panel.Height = h;
                    if (topMargin > 0) panel.Margin = new Thickness(0, mStart + (mEnd - mStart) * e, 0, 0);
                    panel.Opacity = opStart + (opEnd - opStart) * e;
                    if (clip != null) clip.Rect = new Windows.Foundation.Rect(0, 0, panel.ActualWidth, h);
                    if (t >= 1)
                    {
                        Microsoft.UI.Xaml.Media.CompositionTarget.Rendering -= handler;

                        _active.Remove(panel); _active.Add(panel, new Rec { Val = targetVal });
                        panel.Clip = null;
                        if (expand) { panel.Height = double.NaN; panel.Opacity = 1; }
                        else if (foldToRest) { panel.Height = rest; panel.Opacity = 1; }
                        else { panel.Visibility = Visibility.Collapsed; panel.Height = double.NaN; panel.Opacity = 0; }
                        if (topMargin > 0) panel.Margin = new Thickness(0, topMargin, 0, 0);
                        completed?.Invoke();
                    }
                }
                catch
                {

                    try { Microsoft.UI.Xaml.Media.CompositionTarget.Rendering -= handler; } catch { }
                    _active.Remove(panel);
                    try { panel.Clip = null; } catch { }
                }
            };
            _active.Remove(panel); _active.Add(panel, new Rec { Handler = handler, Val = expand ? 1 : 0 });
            Microsoft.UI.Xaml.Media.CompositionTarget.Rendering += handler;
        }
        catch { }
    }



    public static void Cancel(FrameworkElement panel)
    {
        try
        {
            if (_active.TryGetValue(panel, out var hold) && hold.Handler != null)
            {
                try { Microsoft.UI.Xaml.Media.CompositionTarget.Rendering -= hold.Handler; } catch { }
            }
            _active.Remove(panel);
            try { panel.Clip = null; } catch { }
            try { panel.Height = double.NaN; } catch { }
        }
        catch { }
    }



    public static void SetInstant(FrameworkElement panel, bool expanded, double topMargin = 0)
    {
        try
        {
            if (_active.TryGetValue(panel, out var hold) && hold.Handler != null)
            {
                try { Microsoft.UI.Xaml.Media.CompositionTarget.Rendering -= hold.Handler; } catch { }
            }
            _active.Remove(panel);
            panel.Visibility = expanded ? Visibility.Visible : Visibility.Collapsed;
            panel.Height = double.NaN;
            panel.Opacity = expanded ? 1.0 : 0.0;
            if (topMargin > 0) panel.Margin = new Thickness(0, topMargin, 0, 0);
            _active.Add(panel, new Rec { Val = expanded ? 1.0 : 0.0 });
        }
        catch { }
    }
}
