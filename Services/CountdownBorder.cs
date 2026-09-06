using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace Novara.Services;

/// <summary>
/// 9.3: 30s cooldown wrapper for hard-delete confirm buttons (owner design: the button itself is
/// the progress bar). While counting down the button is disabled and its background becomes a
/// horizontal gradient - the solid-danger segment (remaining time) shrinks toward the left edge as
/// time runs out, driven by a 100ms DispatcherTimer updating gradient offsets manually (no XAML
/// animation objects, no layout animation). On completion the original brush is restored and
/// Completed fires; the host then re-enables the button. Closing mid-count and reopening calls
/// Start again (fresh 30s).
///


/// promised. Fixed with a four-stop layout: a fixed-width transition band around the current ratio
/// blends solid-danger into the dimmed trail smoothly, so the boundary reads as a gradient while the
/// band position still conveys remaining time.
/// </summary>
public partial class CountdownBorder : Grid
{
    private UIElement? _content;
    private Brush? _origBrush;
    private GradientStop[] _stops = Array.Empty<GradientStop>();
    private Microsoft.UI.Dispatching.DispatcherQueueTimer? _timer;
    private double _totalSeconds;
    private DateTimeOffset _end;
    private bool _built;

    
    private const double BandHalf = 0.05;

    /// <summary>Raised once when the countdown reaches zero. Re-subscribed per dialog show.</summary>
    public event Action? Completed;

    public CountdownBorder()
    {
        Loaded += (_, _) => Build();
    }

    private void Build()
    {
        if (_built) return;
        _built = true;
        _content = Children.Count > 0 ? Children[0] : null;
    }

    /// <summary>Start (or restart) the countdown on the wrapped confirm button.</summary>
    public void Start(int seconds)
    {
        Build();
        Reset();
        if (_content is not Button btn) { Completed?.Invoke(); return; }

        _totalSeconds = seconds;
        _end = DateTimeOffset.UtcNow.AddSeconds(seconds);
        _origBrush = btn.Background;
        
        
        var solid = Color.FromArgb(0xCC, 0xFF, 0x45, 0x45); // danger red (remaining)
        var dim = Color.FromArgb(0x33, 0xFF, 0x45, 0x45);   // dimmed trail (elapsed)
        _stops = new[]
        {
            new GradientStop { Color = solid, Offset = 0.0 },
            new GradientStop { Color = solid, Offset = 1.0 },
            new GradientStop { Color = dim,   Offset = 1.0 },
            new GradientStop { Color = dim,   Offset = 1.0 },
        };
        var brush = new LinearGradientBrush { StartPoint = new Windows.Foundation.Point(0, 0), EndPoint = new Windows.Foundation.Point(1, 0) };
        foreach (var s in _stops) brush.GradientStops.Add(s);
        btn.Background = brush;

        btn.IsEnabled = false;
        // N3-58: keep the dimming consistent with every host (they all pre-set 0.45 before Start) -
        // 0.6 here silently overrode the host value, leaving the disabled look at two opacities.
        btn.Opacity = 0.45;

        _timer = DispatcherQueue.CreateTimer();
        _timer.Interval = TimeSpan.FromMilliseconds(100);
        _timer.Tick += OnTick;
        _timer.Start();
    }

    private void OnTick(object? sender, object e)
    {
        var remain = (_end - DateTimeOffset.UtcNow).TotalSeconds;
        if (remain <= 0)
        {
            var f = Completed;
            Reset();
            f?.Invoke();
            return;
        }
        var ratio = remain / _totalSeconds;
        
        // overlaps the button edge (very start / very end) it simply clamps and keeps a soft fade.
        _stops[1].Offset = Math.Max(0.0, ratio - BandHalf);
        _stops[2].Offset = Math.Min(1.0, ratio + BandHalf);
    }

    /// <summary>Stop the countdown and restore the button (background + enabled state). Self-healing:
    
    /// keeps every host consistent without each of them re-enabling the confirm button on cancel.</summary>
    public void Reset()
    {
        if (_timer != null) { _timer.Stop(); _timer.Tick -= OnTick; _timer = null; }
        if (_content is Button btn)
        {
            if (_origBrush != null) btn.Background = _origBrush;
            btn.IsEnabled = true;
            btn.Opacity = 1.0;
        }
        _stops = Array.Empty<GradientStop>();
        _origBrush = null;
    }
}
