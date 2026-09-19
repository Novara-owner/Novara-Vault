using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace Novara.Services;





























public partial class CountdownBorder : Grid
{
    private UIElement? _content;
    private Brush? _origBrush;
    private GradientStop[] _stops = Array.Empty<GradientStop>();
    private Microsoft.UI.Dispatching.DispatcherQueueTimer? _timer;
    private double _totalSeconds;
    private DateTimeOffset _end;
    private bool _built;


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


    public void Start(int seconds)
    {
        Build();
        Reset();
        if (_content is not Button btn) { Completed?.Invoke(); return; }

        _totalSeconds = seconds;
        _end = DateTimeOffset.UtcNow.AddSeconds(seconds);
        _origBrush = btn.Background;



        var solid = Motion.CooldownFilledColor;
        var dim = Motion.CooldownRestColor;
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



        btn.Opacity = Motion.CooldownDisabledOpacity;

        _timer = DispatcherQueue.CreateTimer();
        _timer.Interval = TimeSpan.FromMilliseconds(Motion.CooldownTickMs);
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




        var progress = 1 - ratio;
        _stops[1].Offset = Math.Min(1.0, Math.Max(0.0, progress - Motion.CooldownBandHalf));
        _stops[2].Offset = Math.Min(1.0, Math.Max(0.0, progress + Motion.CooldownBandHalf));



        if (_content is Button charged)
            charged.Opacity = Motion.CooldownDisabledOpacity
                + (1 - Motion.CooldownDisabledOpacity) * progress;
    }




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
