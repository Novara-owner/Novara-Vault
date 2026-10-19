using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using System.Runtime.CompilerServices;

namespace Novara.Services;

internal static class SmoothScroller
{


    private static readonly ConditionalWeakTable<ScrollViewer, object> _running = new();






    public static Task ScrollToTopAsync(ScrollViewer viewer)
    {
        var done = new TaskCompletionSource();
        if (_running.TryGetValue(viewer, out _))
        {
            done.SetResult();
            return done.Task;
        }

        var from = viewer.VerticalOffset;
        if (from < 2)
        {
            _running.Remove(viewer);
            done.SetResult();
            return done.Task;
        }
        _running.Add(viewer, null!);

        var total = Math.Clamp(250 + from * 0.35, 350, 700);
        var clock = System.Diagnostics.Stopwatch.StartNew();
        var lastSet = from;

        EventHandler<object> onFrame = null!;
        onFrame = (sender, e) =>
        {
            try
            {
                var t = Math.Min(1.0, clock.Elapsed.TotalMilliseconds / total);
                var target = from * (1 - t * t * t);


                if (Math.Abs(viewer.VerticalOffset - lastSet) > 4)
                {
                    CompositionTarget.Rendering -= onFrame;
                    _running.Remove(viewer);
                    done.SetResult();
                    return;
                }

                viewer.ChangeView(null, target, null, true);
                lastSet = target;
                if (t >= 1)
                {
                    viewer.ChangeView(null, 0, null, true);
                    CompositionTarget.Rendering -= onFrame;
                    _running.Remove(viewer);
                    done.SetResult();
                }
            }
            catch
            {

                CompositionTarget.Rendering -= onFrame;
                _running.Remove(viewer);
                done.SetResult();
            }
        };

        CompositionTarget.Rendering += onFrame;
        return done.Task;
    }
}
