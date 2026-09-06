using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Microsoft.UI.Xaml;

namespace Novara.Services;

/// <summary>


/// </summary>
public static class MeltAnim
{
    private sealed class Rec { public System.EventHandler<object>? Handler; public double Val; }
    private static readonly ConditionalWeakTable<FrameworkElement, Rec> _active = new(); 

    
    public static void Begin(FrameworkElement panel, bool expand, double topMargin = 0)
    {
        try
        {
            var targetVal = expand ? 1.0 : 0.0;
            if (_active.TryGetValue(panel, out var hold))
            {
                if (hold.Handler != null)
                {
                    
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
            double start = expand ? 0 : full;
            double target = expand ? full : 0;
            if (expand && full <= 0)
            {
                // Panel not laid out yet (e.g. opened before the page entered the tree - D4 authorize
                // jump). Animating to 0 would pin Height dead AND the terminal record below would then
                // swallow every later expand via the N6-08 same-value short-circuit ("click does
                // nothing"). Snap to the final state WITHOUT recording: the next Begin runs a real
                // animation once the panel has content and a real height.
                panel.Opacity = 1;
                panel.Height = double.NaN;
                return;
            }
            if (expand) { panel.Height = 0; panel.Opacity = 0; }
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
                    panel.Height = start + (target - start) * e;
                    if (topMargin > 0) panel.Margin = new Thickness(0, topMargin * e, 0, 0);
                    panel.Opacity = expand ? eOut : (1 - eIn);
                    if (t >= 1)
                    {
                        Microsoft.UI.Xaml.Media.CompositionTarget.Rendering -= handler;
                        
                        _active.Remove(panel); _active.Add(panel, new Rec { Val = targetVal });
                        if (expand) { panel.Height = double.NaN; panel.Opacity = 1; }
                        else { panel.Visibility = Visibility.Collapsed; panel.Height = double.NaN; panel.Opacity = 0; }
                        if (topMargin > 0) panel.Margin = new Thickness(0, topMargin, 0, 0);
                    }
                }
                catch
                {
                    
                    try { Microsoft.UI.Xaml.Media.CompositionTarget.Rendering -= handler; } catch { }
                    _active.Remove(panel);
                }
            };
            _active.Remove(panel); _active.Add(panel, new Rec { Handler = handler, Val = expand ? 1 : 0 });
            Microsoft.UI.Xaml.Media.CompositionTarget.Rendering += handler;
        }
        catch { }
    }
}
