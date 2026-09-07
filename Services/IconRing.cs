using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;

namespace Novara.Services;

/// <summary>




/// </summary>
public sealed class IconRing
{
    private const double ItemWidth = 48;
    private const double ItemGap = 8;
    private const double ItemStride = ItemWidth + ItemGap;

    private readonly ScrollViewer _scroll;
    private readonly Panel _panel;
    private readonly List<Border> _borders = new();
    private double _stride;          
    private bool _initialPending;    
    private bool _jumping;           
    private Border? _pendingCenter;  

    
    public event Action<Border>? Tapped;

    public IconRing(ScrollViewer scroll, Panel panel)
    {
        _scroll = scroll;
        _panel = panel;
        _scroll.ViewChanged += OnViewChanged;
        _scroll.PointerWheelChanged += OnPointerWheel;
    }

    public IReadOnlyList<Border> Borders => _borders;

    
    public void Build(IEnumerable<string> tags, Func<string, Border> makeBorder)
    {
        if (_panel.Children.Count > 0) return;
        _panel.Children.Clear();
        _borders.Clear();
        var list = tags.ToList();
        _stride = list.Count * ItemStride;
        for (int copy = 0; copy < 3; copy++)
            foreach (var tag in list)
            {
                var b = makeBorder(tag);
                b.Tapped += (_, _) => Tapped?.Invoke(b);
                _panel.Children.Add(b);
                _borders.Add(b);
            }
        _initialPending = true;
        _scroll.SizeChanged += (_, _) => EnsureInitialPosition();
    }

    
    public void EnsureInitialPosition()
    {
        if (!_initialPending || _stride <= 0 || _scroll.ScrollableWidth < _stride) return;
        _initialPending = false;
        if (_pendingCenter != null)
        {
            var pc = _pendingCenter;
            _pendingCenter = null;
            CenterToCore(pc); 
        }
        else
        {
            JumpTo(_stride);
        }
    }

    
    public void Highlight(string? tag)
    {
        foreach (var b in _borders)
        {
            bool sel = !string.IsNullOrEmpty(tag) && b.Tag?.ToString() == tag;
            b.Background = sel ? App.GetBrush("AppSurfaceBrush") : App.GetBrush("AppSurfaceOverlayBrush");
            b.BorderBrush = sel ? App.GetBrush("AppTextTertiaryBrush") : null;
            b.BorderThickness = sel ? new Thickness(1) : new Thickness(0);
        }
    }

    public Border? FindByTag(string tag)
        => _borders.FirstOrDefault(b => b.Tag?.ToString() == tag);

    
    public void CenterTo(Border border)
    {
        if (_initialPending)
        {
            
            
            _pendingCenter = border;
            return;
        }
        _pendingCenter = null;
        CenterToCore(border);
    }

    private void CenterToCore(Border border)
    {
        int idx = _borders.IndexOf(border);
        if (idx < 0 || _stride <= 0) return;
        double raw = idx * ItemStride + ItemWidth / 2 - _scroll.ViewportWidth / 2;
        double target = raw;
        while (target < _stride) target += _stride;
        while (target >= 2 * _stride) target -= _stride;
        _scroll.ChangeView(target, null, null);
    }

    private void JumpTo(double offset)
    {
        _jumping = true;
        _scroll.ChangeView(offset, null, null, true);
        _jumping = false;
    }

    private void OnViewChanged(object? sender, ScrollViewerViewChangedEventArgs e)
    {
        if (_jumping || _stride <= 0) return;
        EnsureInitialPosition();
        double o = _scroll.HorizontalOffset;
        if (o < _stride) JumpTo(o + _stride);
        else if (o >= 2 * _stride) JumpTo(o - _stride);
    }

    private void OnPointerWheel(object sender, PointerRoutedEventArgs e)
    {
        var delta = e.GetCurrentPoint(_scroll).Properties.MouseWheelDelta;
        if (delta == 0 || _stride <= 0) return;
        e.Handled = true; 
        double target = _scroll.HorizontalOffset - delta; 
        while (target < _stride) target += _stride;
        while (target >= 2 * _stride) target -= _stride;
        JumpTo(target);
    }
}
