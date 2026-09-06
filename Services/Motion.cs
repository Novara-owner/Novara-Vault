









namespace Novara.Services;

public static class Motion
{
    
    public const int Fast = 120;   
    public const int Normal = 240; 
    public const int Stagger = 45; 
    

    
    public const int DlgIn = 340;  
    public const int DlgOut = 180; 

    
    
    public static Microsoft.UI.Xaml.Media.Animation.EasingFunctionBase Decelerate()
        => new Microsoft.UI.Xaml.Media.Animation.PowerEase { EasingMode = Microsoft.UI.Xaml.Media.Animation.EasingMode.EaseOut, Power = 5 };
    
    public static Microsoft.UI.Xaml.Media.Animation.EasingFunctionBase Accelerate()
        => new Microsoft.UI.Xaml.Media.Animation.PowerEase { EasingMode = Microsoft.UI.Xaml.Media.Animation.EasingMode.EaseIn, Power = 3 };
    
    public static Microsoft.UI.Xaml.Media.Animation.EasingFunctionBase Standard()
        => new Microsoft.UI.Xaml.Media.Animation.CubicEase { EasingMode = Microsoft.UI.Xaml.Media.Animation.EasingMode.EaseInOut };

    
    public static Microsoft.UI.Xaml.Media.Animation.DoubleAnimation Eased(
        double to, int ms, Func<Microsoft.UI.Xaml.Media.Animation.EasingFunctionBase> easing,
        Microsoft.UI.Xaml.DependencyObject target, string property, int delayMs = 0)
    {
        var anim = new Microsoft.UI.Xaml.Media.Animation.DoubleAnimation
        {
            To = to,
            Duration = TimeSpan.FromMilliseconds(ms),
            BeginTime = delayMs > 0 ? TimeSpan.FromMilliseconds(delayMs) : TimeSpan.Zero,
            EasingFunction = easing(),
        };
        Microsoft.UI.Xaml.Media.Animation.Storyboard.SetTarget(anim, target);
        Microsoft.UI.Xaml.Media.Animation.Storyboard.SetTargetProperty(anim, property);
        return anim;
    }

    
    
    public static void AddDialogShowTransform(Microsoft.UI.Xaml.Media.Animation.Storyboard sb,
        Microsoft.UI.Xaml.DependencyObject transform)
    {
        foreach (var (v, p) in new[] { (1.0, "ScaleX"), (1.0, "ScaleY"), (0.0, "TranslateY") })
            sb.Children.Add(Eased(v, DlgIn, Decelerate, transform, p));
    }

    
    public static void AddDialogHideTransform(Microsoft.UI.Xaml.Media.Animation.Storyboard sb,
        Microsoft.UI.Xaml.DependencyObject transform)
    {
        foreach (var (v, p) in new[] { (0.94, "ScaleX"), (0.94, "ScaleY"), (24.0, "TranslateY") })
            sb.Children.Add(Eased(v, DlgOut, Accelerate, transform, p));
    }

    
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Microsoft.UI.Xaml.Media.Animation.Storyboard, object> _staggerWired = new();

    
    
    public static void StaggerReset(Microsoft.UI.Xaml.Controls.Border dialog)
    {
        if (dialog.Child is not Microsoft.UI.Xaml.Controls.Panel root) return;
        foreach (var child in root.Children)
        {
            if (child is not Microsoft.UI.Xaml.FrameworkElement fe) continue;
            
            
            if (fe.RenderTransform is not Microsoft.UI.Xaml.Media.TranslateTransform tt)
            {
                if (fe.RenderTransform != null) continue;
                tt = new Microsoft.UI.Xaml.Media.TranslateTransform();
                fe.RenderTransform = tt;
            }
            fe.Opacity = 0;
            tt.Y = 14;
        }
    }

    
    
    
    public static void StaggerWire(Microsoft.UI.Xaml.Media.Animation.Storyboard sb,
        Microsoft.UI.Xaml.Controls.Border dialog)
    {
        if (_staggerWired.TryGetValue(sb, out _)) return;
        if (dialog.Child is not Microsoft.UI.Xaml.Controls.Panel root) return;
        _staggerWired.Add(sb, true);
        int step = 0;
        foreach (var child in root.Children)
        {
            if (child is not Microsoft.UI.Xaml.FrameworkElement fe) continue;
            if (fe.RenderTransform is not Microsoft.UI.Xaml.Media.TranslateTransform tt) continue;
            int delay = Stagger * step;
            var fi = new Microsoft.UI.Xaml.Media.Animation.DoubleAnimation
            { To = 1, Duration = TimeSpan.FromMilliseconds(200), BeginTime = TimeSpan.FromMilliseconds(delay) };
            Microsoft.UI.Xaml.Media.Animation.Storyboard.SetTarget(fi, fe);
            Microsoft.UI.Xaml.Media.Animation.Storyboard.SetTargetProperty(fi, "Opacity");
            sb.Children.Add(fi);
            sb.Children.Add(Eased(0, 240, Decelerate, tt, "Y", delay));
            if (++step >= 4) break;
        }
    }
}
