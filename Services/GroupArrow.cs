using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Shapes;

namespace Novara.Services;








public sealed partial class GroupArrow : Grid
{
    private readonly Line _l1;
    private readonly Line _l2;
    private readonly ScaleTransform _scale;
    private Storyboard? _storyboard;
    private bool _hasState;


    private static readonly Endpoints L1Expanded = new(3, 6, 8, 11);
    private static readonly Endpoints L2Expanded = new(8, 11, 13, 6);
    private static readonly Endpoints L1Collapsed = new(6, 3, 11, 8);
    private static readonly Endpoints L2Collapsed = new(11, 8, 6, 13);

    private readonly record struct Endpoints(double X1, double Y1, double X2, double Y2);

    public GroupArrow()
    {
        Width = 16;
        Height = 16;
        RenderTransformOrigin = new Windows.Foundation.Point(0.5, 0.5);
        _scale = new ScaleTransform { ScaleX = 1, ScaleY = 1 };
        RenderTransform = _scale;
        IsHitTestVisible = false;

        var canvas = new Canvas { Width = 16, Height = 16, IsHitTestVisible = false };
        var stroke = App.GetBrush("AppTextSecondaryBrush");
        _l1 = NewLine(stroke);
        _l2 = NewLine(stroke);
        canvas.Children.Add(_l2);
        canvas.Children.Add(_l1);
        Children.Add(canvas);
        Apply(L1Collapsed, L2Collapsed);
    }


    public bool IsExpanded { get; private set; }


    public void SetStroke(Brush brush)
    {
        _l1.Stroke = brush;
        _l2.Stroke = brush;
    }



    public void SetExpanded(bool expanded, bool animate, double durationMs = 240, double delayMs = 30)
    {
        if (_hasState && expanded == IsExpanded && _storyboard == null) return;
        var l1Target = expanded ? L1Expanded : L1Collapsed;
        var l2Target = expanded ? L2Expanded : L2Collapsed;
        var l1Current = ReadEndpoints(_l1);
        var l2Current = ReadEndpoints(_l2);

        _storyboard?.Stop();
        _storyboard = null;
        Apply(l1Current, l2Current);
        IsExpanded = expanded;
        _hasState = true;

        if (!animate)
        {
            Apply(l1Target, l2Target);
            _scale.ScaleX = 1;
            _scale.ScaleY = 1;
            return;
        }

        var sb = new Storyboard();
        double outerDelay = delayMs + 25;

        Animate(sb, _l1, "X2", l1Current.X2, l1Target.X2, delayMs, durationMs);
        Animate(sb, _l1, "Y2", l1Current.Y2, l1Target.Y2, delayMs, durationMs);
        Animate(sb, _l2, "X1", l2Current.X1, l2Target.X1, delayMs, durationMs);
        Animate(sb, _l2, "Y1", l2Current.Y1, l2Target.Y1, delayMs, durationMs);

        Animate(sb, _l1, "X1", l1Current.X1, l1Target.X1, outerDelay, durationMs);
        Animate(sb, _l1, "Y1", l1Current.Y1, l1Target.Y1, outerDelay, durationMs);
        Animate(sb, _l2, "X2", l2Current.X2, l2Target.X2, outerDelay, durationMs);
        Animate(sb, _l2, "Y2", l2Current.Y2, l2Target.Y2, outerDelay, durationMs);

        AnimateScale(sb, 0.96, 1.0, delayMs, 160);
        _storyboard = sb;
        sb.Begin();
    }

    private static Line NewLine(Brush stroke) => new()
    {
        StrokeThickness = 1.6,
        StrokeStartLineCap = PenLineCap.Round,
        StrokeEndLineCap = PenLineCap.Round,
        Stroke = stroke,
        IsHitTestVisible = false,
    };

    private static Endpoints ReadEndpoints(Line line) => new(line.X1, line.Y1, line.X2, line.Y2);

    private void Apply(Endpoints l1, Endpoints l2)
    {
        _l1.X1 = l1.X1; _l1.Y1 = l1.Y1; _l1.X2 = l1.X2; _l1.Y2 = l1.Y2;
        _l2.X1 = l2.X1; _l2.Y1 = l2.Y1; _l2.X2 = l2.X2; _l2.Y2 = l2.Y2;
    }

    private static void Animate(Storyboard sb, DependencyObject target, string property, double from, double to,
        double beginMs, double durationMs)
    {
        var anim = new DoubleAnimation
        {
            From = from,
            To = to,
            Duration = new Duration(TimeSpan.FromMilliseconds(durationMs)),
            BeginTime = TimeSpan.FromMilliseconds(beginMs),
            EasingFunction = new BackEase { Amplitude = 0.15, EasingMode = EasingMode.EaseOut },
            EnableDependentAnimation = true,
        };
        Storyboard.SetTarget(anim, target);
        Storyboard.SetTargetProperty(anim, property);
        sb.Children.Add(anim);
    }

    private void AnimateScale(Storyboard sb, double from, double to, double beginMs, double durationMs)
    {
        foreach (var prop in new[] { "ScaleX", "ScaleY" })
        {
            var anim = new DoubleAnimation
            {
                From = from,
                To = to,
                Duration = new Duration(TimeSpan.FromMilliseconds(durationMs)),
                BeginTime = TimeSpan.FromMilliseconds(beginMs),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
                EnableDependentAnimation = true,
            };
            Storyboard.SetTarget(anim, _scale);
            Storyboard.SetTargetProperty(anim, prop);
            sb.Children.Add(anim);
        }
    }
}
