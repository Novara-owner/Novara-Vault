/* ========== DiaryPage - Diary Tab ==========
Function: Diary entry list - pinned-first + modified-time-desc ordering, star/pin incremental updates, entry open/delete
Corresponding UI: DiaryPage.xaml.cs
Logic Range: Whole file business logic of this module
*/
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.UI;
using Windows.Foundation;
using Novara.Models;
using Novara.Services;
using Windows.Storage.Pickers;
using WinRT.Interop;
using System.IO;
using System.Text;
using System.Threading;
using System.Text.RegularExpressions;

namespace Novara.Pages;

public sealed partial class DiaryPage : Page
{
    private readonly MenuFlyout _emptyAreaMenu;
    private readonly MenuFlyout _cardContextMenu;
    private MenuFlyoutItem? _cardPinItem;
    private MenuFlyoutItem? _cardStarItem;
    private MenuFlyoutItem? _cardExportItem;
    private MenuFlyoutItem? _cardExportNoImageItem;
    private DiaryEntry? _currentMenuTarget;
    private bool _hasLoaded;
    private bool _entrancePlayed; // E4-16: play the entrance animation only once (page instances are cached by MainWindow)
    private string _currentFilter = "all"; 
    private CancellationTokenSource? _loadCts; // P0-1: cancel in-flight chunked render on re-refresh

    private readonly List<DiaryEntry> _diaries;

    private readonly Dictionary<DiaryEntry, Viewbox> _diaryPinIcons = new();
    private readonly Dictionary<DiaryEntry, Viewbox> _diaryStarIcons = new();

    private readonly Storyboard _deleteConfirmShowStoryboard = new();
    private readonly Storyboard _deleteConfirmHideStoryboard = new();

    
    private Border? _dragCard;
    private bool _dragging;
    private DateTime _dragEndTime;      
    private Border? _dragEndCard;       
    private Point _grabOffset;
        private Image? _dragGhost;
        private Border? _dropIndicator;
        private int _dropIndex;
        private int _dragOriginIndex;
    private Point _lastPointerInRoot;
    private int _autoScrollDir;
    private int _autoScrollFrame;
    private bool _autoScrollActive;

    public DiaryPage()
    {
        InitializeComponent();
        Novara.Services.DialogDepth.AttachContainer((Grid)Content); // dialog depth: shadow + chrome veil auto-wiring
        KeyDown += Page_KeyDown;

        _diaries = (App.Store?.Database.DiaryItems ?? new List<DiaryEntry>()).Where(x => !x.IsDeleted).ToList();
        _emptyAreaMenu = BuildEmptyAreaMenu();
        _cardContextMenu = BuildCardContextMenu();        InitDeleteConfirmAnimations();

        Loaded += Page_Loaded;

        Unloaded += (_, _) =>
        {
            _deleteConfirmHideStoryboard.Stop();
            DeleteConfirmOverlay.Visibility = Visibility.Collapsed;
            _currentMenuTarget = null;
            DialogDepth.VeilClear(); 
            
            if (_dragCard != null) { _dragCard.BorderBrush = new SolidColorBrush(App.GetBrush("AppBorderBrush").Color); _dragCard.BorderThickness = new Thickness(1); _dragCard.Opacity = 1; }
            _dragging = false; _dragCard = null;
            if (_dragGhost != null) { DragLayer.Children.Remove(_dragGhost); _dragGhost = null; }
            if (_dropIndicator != null) { DiaryList.Children.Remove(_dropIndicator); _dropIndicator = null; }
            StopAutoScroll();
        };
    }

    private void Page_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key != Windows.System.VirtualKey.Escape) return;
        if (DeleteConfirmOverlay.Visibility == Visibility.Visible) { CloseDeleteConfirmDialog(); e.Handled = true; }
    }

    private void Page_Loaded(object sender, RoutedEventArgs e)
    {
        if (!_hasLoaded) { _hasLoaded = true; RefreshDiaryList(); } // E4-16: entrance animation now fires inside RefreshDiaryList after chunked render completes
    }

    
    public void StealFocus() => FocusSink.Focus(FocusState.Programmatic);

    public async void RefreshDiaryList()
    {
        
        _loadCts?.Cancel();
        var cts = _loadCts = new CancellationTokenSource();
        var token = cts.Token;

        double savedOffset = DiaryScrollViewer.VerticalOffset; 

        _diaryPinIcons.Clear();
        _diaryStarIcons.Clear();
        DiaryList.Children.Clear();
        EmptyHint.Visibility = Visibility.Collapsed; // P0-1: hide until chunked render decides the real empty state

        var diaries = _diaries;
        if (diaries.Count == 0) { _loadCts = null; UpdateEmptyHint(); return; } 

        var sorted = diaries
            .OrderByDescending(d => d.IsPinned)
            .ThenByDescending(d => d.IsPinned ? (d.PinnedAt ?? d.ModifiedAt) : DateTime.MinValue) 
            .ThenBy(d => d.Order <= 0 ? int.MaxValue : d.Order) 
            .ThenByDescending(d => d.ModifiedAt)
            .ToList();

        bool playEntrance = !_entrancePlayed; // P0-1: only the first load plays the cascade
        int entIdx = 0;

        try
        {
            await ChunkedRender.RunAsync(sorted.Count, 8, DispatcherQueue, (s, e) =>
            {
                for (int i = s; i < e; i++)
                {
                    if (token.IsCancellationRequested) return;
                    var card = BuildDiaryCard(sorted[i], i);
                    DiaryList.Children.Add(card);
                    if (playEntrance && entIdx < 10) App.PlayCardEntrance(card, entIdx); entIdx++;
                }
            }, token);
        }
        catch (OperationCanceledException) { return; }

        if (token.IsCancellationRequested) return;
        _entrancePlayed = true; // P0-1: cascade already played per-card above
        _loadCts = null; 
        SetFilter(_currentFilter); // NH4: rebuilt cards default to Visible - re-apply the active filter so it survives refresh/pin/delete/import
        UpdateEmptyHint();
        if (savedOffset > 0) DiaryScrollViewer.ChangeView(null, savedOffset, null, true); 
    }

    private Border BuildDiaryCard(DiaryEntry entry, int index)
    {
        var card = new Border
        {
            CornerRadius = new CornerRadius(12),
            Background = App.GetBrush("AppSurfaceOverlayBrush"),
            BorderBrush = App.GetBrush("AppBorderBrush"),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(20, 18, 20, 18),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            RenderTransform = new TranslateTransform(),
            Tag = entry
        };

        AttachCardHover(card);

        card.Tapped += (s, _) =>
        {
            
            
            if (ReferenceEquals(s, _dragEndCard) && (DateTime.Now - _dragEndTime).TotalMilliseconds < 300) return;
            App.MainWindow?.NavigateToEditor(entry);
        };

        card.ContextRequested += (s, e) =>
        {
            e.Handled = true;
            _currentMenuTarget = entry;
            UpdatePinStarMenuItems(entry);
            if (e.TryGetPosition(card, out var pos))
                _cardContextMenu.ShowAt(card, pos);
            else
                _cardContextMenu.ShowAt(card, new Windows.Foundation.Point(0, 0));
        };

        var innerGrid = new Grid();
        innerGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        innerGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var titleRow = new Grid();
        titleRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        titleRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var titleText = new TextBlock
        {
            Text = entry.Title,
            FontSize = 15,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            CharacterSpacing = App.GetCharacterSpacing(entry.Title, 180),
            Foreground = App.GetBrush("AppTextPrimaryBrush"),
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(titleText, 0);
        titleRow.Children.Add(titleText);

        var iconStack = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center,
            Spacing = 4
        };
        Grid.SetColumn(iconStack, 1);
        titleRow.Children.Add(iconStack);

        
        if (entry.Format == "markdown")
        {
            iconStack.Children.Add(new Viewbox
            {
                Width = 16, Height = 16,
                Stretch = Microsoft.UI.Xaml.Media.Stretch.Uniform,
                VerticalAlignment = VerticalAlignment.Center,
                Child = new PathIcon
                {
                    Data = App.CreateGeometry(IconData.MdBadge),
                    Foreground = new SolidColorBrush(Color.FromArgb(0xFF, 0xFF, 0xD7, 0x00))
                }
            });
        }

        var pinIcon = new Viewbox
        {
            Width = 16, Height = 16,
            Stretch = Microsoft.UI.Xaml.Media.Stretch.Uniform,
            Visibility = entry.IsPinned ? Visibility.Visible : Visibility.Collapsed,
            Child = new PathIcon
            {
                Data = App.CreateGeometry(IconData.CardPin),
                Foreground = new SolidColorBrush(Color.FromArgb(0xFF, 0x72, 0x76, 0xFF))
            }
        };
        iconStack.Children.Add(pinIcon);
        _diaryPinIcons[entry] = pinIcon;

        var starIcon = new Viewbox
        {
            Width = 16, Height = 16,
            Stretch = Microsoft.UI.Xaml.Media.Stretch.Uniform,
            Visibility = entry.IsStarred ? Visibility.Visible : Visibility.Collapsed,
            Child = new PathIcon
            {
                Data = App.CreateGeometry(IconData.CardStar),
                Foreground = new SolidColorBrush(Color.FromArgb(0xFF, 0x72, 0x76, 0xFF))
            }
        };
        iconStack.Children.Add(starIcon);
        _diaryStarIcons[entry] = starIcon;

        Grid.SetRow(titleRow, 0);
        innerGrid.Children.Add(titleRow);

        var timeRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 12, 0, 0),
            Spacing = 36
        };
        var createdText = string.Format(App.GetString("Diary_Created_At"), entry.CreatedAt.ToString("yyyy-MM-dd HH:mm"));
        var modifiedText = string.Format(App.GetString("Diary_Modified_At"), entry.ModifiedAt.ToString("yyyy-MM-dd HH:mm"));
        timeRow.Children.Add(new TextBlock
        {
            Text = createdText,
            FontSize = 12, FontWeight = Microsoft.UI.Text.FontWeights.Light,
            CharacterSpacing = App.GetCharacterSpacing(createdText, 60),
            Foreground = App.GetBrush("AppTextTertiaryBrush")
        });
        timeRow.Children.Add(new TextBlock
        {
            Text = modifiedText,
            FontSize = 12, FontWeight = Microsoft.UI.Text.FontWeights.Light,
            CharacterSpacing = App.GetCharacterSpacing(modifiedText, 60),
            Foreground = App.GetBrush("AppTextTertiaryBrush")
        });

        Grid.SetRow(timeRow, 1);
        innerGrid.Children.Add(timeRow);

        card.Child = innerGrid;

        AttachCardDrag(card);

        return card;
    }

    private void AttachCardHover(Border card)
    {

        var baseColor = Color.FromArgb(0x33, 0xFF, 0xFF, 0xFF);
        if (App.GetBrush("AppBorderBrush") is SolidColorBrush baseBrush)
        {
            baseColor = baseBrush.Color;
            card.BorderBrush = new SolidColorBrush(baseColor);
        }
        var hoverColor = Color.FromArgb(
            (byte)Math.Min(baseColor.A + 0x30, 0xFF),
            baseColor.R,
            baseColor.G,
            baseColor.B);
        card.PointerEntered += (_, _) =>
        {
            if (_dragging) return; 
            // E4-12: manual color swap - ColorAnimation on hover is 0xc000027b-sensitive during page
            // switches (DiaryPage crash 2026-08-10); only the TranslateY lift stays animated.
            if (card.BorderBrush is SolidColorBrush sb) sb.Color = hoverColor;
            if (card.RenderTransform is TranslateTransform t)
            {
                var la = new DoubleAnimation { To = -3, Duration = TimeSpan.FromMilliseconds(200), EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
                var st = new Storyboard(); Storyboard.SetTarget(la, t); Storyboard.SetTargetProperty(la, "Y"); st.Children.Add(la); st.Begin();
            }
        };
        card.PointerExited += (_, _) =>
        {
            if (_dragging) return; 
            if (card.BorderBrush is SolidColorBrush sb) sb.Color = baseColor;
            if (card.RenderTransform is TranslateTransform t)
            {
                var la = new DoubleAnimation { To = 0, Duration = TimeSpan.FromMilliseconds(300), EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
                var st = new Storyboard(); Storyboard.SetTarget(la, t); Storyboard.SetTargetProperty(la, "Y"); st.Children.Add(la); st.Begin();
            }
        };
    }

    

    private void AttachCardDrag(Border card)
    {
        bool armed = false;
        DispatcherTimer? timer = null;
        Point grabOffset = default;

        card.PointerPressed += (s, e) =>
        {
            if (_dragging) return;
            if (_currentFilter != "all") return; 
            if (card.Tag is DiaryEntry d && d.IsPinned) return; 
            if (App.IsDescendantOfButton(e.OriginalSource as Microsoft.UI.Xaml.DependencyObject)) return; 
            var pt = e.GetCurrentPoint(card);
            if (pt.Properties.IsRightButtonPressed || !pt.Properties.IsLeftButtonPressed) return;
            grabOffset = pt.Position;
            var pointer = e.Pointer;
            armed = true;

            
            var rtb = new RenderTargetBitmap();
            var renderOp = rtb.RenderAsync(card);

            timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
            timer.Tick += async (_, _) =>
            {
                timer.Stop(); timer = null;
                if (!armed || _dragging || !card.IsLoaded) { armed = false; return; } 
                try { await renderOp; } catch { }
                if (!armed || _dragging || !card.IsLoaded) { armed = false; return; } 
                BeginDrag(card, grabOffset, rtb);
                try { card.CapturePointer(pointer); } catch { }
            };
            timer.Start();
        };

        card.PointerMoved += (s, e) =>
        {
            if (_dragging)
            {
                if (ReferenceEquals(_dragCard, card)) UpdateDrag(e);
                return;
            }
            if (armed)
            {
                var pt = e.GetCurrentPoint(card);
                double dx = pt.Position.X - grabOffset.X, dy = pt.Position.Y - grabOffset.Y;
                if (dx * dx + dy * dy > 64) { armed = false; timer?.Stop(); timer = null; }
            }
        };

        card.PointerReleased += (s, e) =>
        {
            if (_dragging && ReferenceEquals(_dragCard, card)) EndDrag();
            armed = false; timer?.Stop(); timer = null;
        };
        card.PointerCanceled += (s, e) =>
        {
            if (_dragging && ReferenceEquals(_dragCard, card)) EndDrag();
            armed = false; timer?.Stop(); timer = null;
        };
        card.PointerCaptureLost += (s, e) =>
        {
            if (_dragging && ReferenceEquals(_dragCard, card)) EndDrag();
            armed = false; timer?.Stop(); timer = null;
        };
    }

    private void BeginDrag(Border card, Point grabOffset, RenderTargetBitmap rtb)
    {
        _dragging = true;
        _dragCard = card;
        _grabOffset = grabOffset;
        _dropIndex = _dragOriginIndex = CountVisibleBefore(card); // N2-06: origin in VISIBLE-slot space (same space as ComputeDropIndex)

        App.StopCardEntrance(card); 
        card.BorderBrush = new SolidColorBrush(Color.FromArgb(0xFF, 0x72, 0x76, 0xFF));
        card.BorderThickness = new Thickness(2);
        card.Opacity = 0.35;
        card.RenderTransform = new TranslateTransform(); 

        try
        {
            var ghost = new Image
            {
                Source = rtb,
                Width = card.ActualWidth,
                Height = card.ActualHeight,
                Opacity = 0.92,
            };
            var pos = card.TransformToVisual(RootGrid).TransformPoint(new Point(0, 0));
            _dragGhost = ghost;
            DragLayer.Children.Add(ghost);
            Canvas.SetLeft(ghost, pos.X);
            Canvas.SetTop(ghost, pos.Y);
        }
        catch { _dragGhost = null; }
    }

    private void UpdateDrag(PointerRoutedEventArgs e)
    {
        if (_dragCard == null) return;
        var pos = e.GetCurrentPoint(RootGrid).Position;
        _lastPointerInRoot = pos;
        if (_dragGhost != null)
        {
            Canvas.SetLeft(_dragGhost, pos.X - _grabOffset.X);
            Canvas.SetTop(_dragGhost, pos.Y - _grabOffset.Y);
        }
        var ptInList = e.GetCurrentPoint(DiaryList).Position;
        UpdateDropIndicator(ComputeDropIndex(ptInList));
        HandleAutoScroll(e);
    }

    private int ComputeDropIndex(Point ptInList)
    {
        int count = 0;
        foreach (var child in DiaryList.Children)
        {
            if (ReferenceEquals(child, _dropIndicator)) continue;
            
            
            if (child is FrameworkElement fe && fe.Visibility == Visibility.Visible)
            {
                var top = fe.TransformToVisual(DiaryList).TransformPoint(new Point(0, 0)).Y;
                if (ptInList.Y < top + fe.ActualHeight / 2) break;
                count++;
            }
        }
        return count;
    }

    private void UpdateDropIndicator(int index)
    {
        
        
        int pinnedVisible = 0;
        foreach (var child in DiaryList.Children)
            if (child is Border b && b.Visibility == Visibility.Visible && b.Tag is DiaryEntry d && d.IsPinned)
                pinnedVisible++;
        index = Math.Max(index, pinnedVisible);
        
        if (index == _dragOriginIndex || index == _dragOriginIndex + 1)
        {
            if (_dropIndicator != null) { DiaryList.Children.Remove(_dropIndicator); _dropIndicator = null; }
            _dropIndex = _dragOriginIndex; 
            return;
        }
        if (_dropIndicator != null) DiaryList.Children.Remove(_dropIndicator);
        _dropIndicator = CreateDropIndicator();
        DiaryList.Children.Insert(index, _dropIndicator);
        _dropIndex = index;
    }

    private Border CreateDropIndicator()
    {
        var b = new Border { Height = 16, Background = new SolidColorBrush(Color.FromArgb(0, 0, 0, 0)) };
        b.Child = new Border
        {
            Height = 2,
            CornerRadius = new CornerRadius(1),
            Background = new SolidColorBrush(Color.FromArgb(0xFF, 0x72, 0x76, 0xFF)),
            VerticalAlignment = VerticalAlignment.Center,
        };
        return b;
    }

    private void HandleAutoScroll(PointerRoutedEventArgs e)
    {
        var pt = e.GetCurrentPoint(DiaryScrollViewer).Position;
        double topZone = 40, bottomZone = 40;
        int dir = 0;
        if (pt.Y < topZone) dir = -1;
        else if (pt.Y > DiaryScrollViewer.ViewportHeight - bottomZone) dir = 1;

        if (dir == 0) { StopAutoScroll(); return; }
        _autoScrollDir = dir;
        StartAutoScroll();
    }

    private void StartAutoScroll()
    {
        if (_autoScrollActive) return;
        _autoScrollActive = true;
        _autoScrollFrame = 0;
        Microsoft.UI.Xaml.Media.CompositionTarget.Rendering += OnAutoScrollRendering;
    }

    private void StopAutoScroll()
    {
        if (!_autoScrollActive) return;
        _autoScrollActive = false;
        Microsoft.UI.Xaml.Media.CompositionTarget.Rendering -= OnAutoScrollRendering;
        _autoScrollDir = 0;
    }

    private void OnAutoScrollRendering(object? sender, object e)
    {
        if (_dragCard == null || _autoScrollDir == 0) { StopAutoScroll(); return; }
        var newOffset = Math.Clamp(DiaryScrollViewer.VerticalOffset + _autoScrollDir * 8, 0, DiaryScrollViewer.ScrollableHeight);
        DiaryScrollViewer.ChangeView(null, newOffset, null, true);

        if (++_autoScrollFrame < 3) return;
        _autoScrollFrame = 0;
        var ptInList = RootGrid.TransformToVisual(DiaryList).TransformPoint(_lastPointerInRoot);
        UpdateDropIndicator(ComputeDropIndex(ptInList));
    }

    private void EndDrag()
    {
        if (!_dragging || _dragCard == null) return;
        var card = _dragCard;
        _dragCard = null;
        _dragging = false;
        _dragEndTime = DateTime.Now; 
        _dragEndCard = card; 

        int dropIndex = _dropIndex;

        if (_dragGhost != null) { DragLayer.Children.Remove(_dragGhost); _dragGhost = null; }
        if (_dropIndicator != null) { DiaryList.Children.Remove(_dropIndicator); _dropIndicator = null; }
        StopAutoScroll();

        card.BorderBrush = new SolidColorBrush(App.GetBrush("AppBorderBrush").Color);
        card.BorderThickness = new Thickness(1);
        card.Opacity = 1;

        // N2-06: dropIndex is in "slot space including the dragged card" (ComputeDropIndex counts it);
        // convert to the without-self space, then translate back to a physical Children index.
        int curVisible = CountVisibleBefore(card); // current visible slot, excluding the card itself
        int dst = dropIndex > curVisible ? dropIndex - 1 : dropIndex;
        if (dst != curVisible)
        {
            DiaryList.Children.Remove(card);
            DiaryList.Children.Insert(PhysicalIndexForVisibleSlot(dst), card);
        }

        PersistDiaryOrder();
    }

    /// <summary>N2-06: number of Visible cards strictly before <paramref name="card"/> (its visible slot).</summary>
    private int CountVisibleBefore(Border card)
    {
        int n = 0;
        foreach (var child in DiaryList.Children)
        {
            if (ReferenceEquals(child, card)) break;
            if (child is FrameworkElement fe && fe.Visibility == Visibility.Visible) n++;
        }
        return n;
    }

    /// <summary>N2-06: physical Children index where a card must land to become the
    /// <paramref name="visibleSlot"/>-th visible card (past the end when the slot exceeds them).</summary>
    private int PhysicalIndexForVisibleSlot(int visibleSlot)
    {
        int seen = 0;
        for (int i = 0; i < DiaryList.Children.Count; i++)
        {
            if (DiaryList.Children[i] is FrameworkElement fe && fe.Visibility == Visibility.Visible)
            {
                if (seen == visibleSlot) return i;
                seen++;
            }
        }
        return DiaryList.Children.Count;
    }

    
    
    
    private void PersistDiaryOrder()
    {
        int order = 0;
        foreach (var child in DiaryList.Children)
            if (child is Border b && b.Visibility == Visibility.Visible && b.Tag is DiaryEntry d && !d.IsPinned)
                d.Order = ++order;
        foreach (var child in DiaryList.Children)
            if (child is Border b2 && b2.Visibility != Visibility.Visible && b2.Tag is DiaryEntry h && !h.IsPinned)
                h.Order = ++order;
        App.Store?.SaveAsync();
    }

    public bool FlashDiaryById(string id)
    {
        foreach (var child in DiaryList.Children)
        {
            if (child is not Microsoft.UI.Xaml.Controls.Border card || card.Tag is not Models.DiaryEntry entry || entry.Id != id) continue;
            // N4D-05: the card may be hidden by the diary/document filter - flashing an invisible card
            // reported success while the user saw nothing. Reset to "mixed" first so the target shows.
            if (card.Visibility != Visibility.Visible && _currentFilter != "all") SetFilter("all");
            try
            {
                var t = card.TransformToVisual(DiaryScrollViewer);
                var pt = t.TransformPoint(new Windows.Foundation.Point(0, 0));
                DiaryScrollViewer.ChangeView(null, System.Math.Max(0, DiaryScrollViewer.VerticalOffset + pt.Y - 20), null, false);
            }
            catch { }
            App.FlashCard(card);
            return true;
        }
        return false;
    }

    private void UpdateEmptyHint()
    {
        bool anyVisible = false;
        foreach (var c in DiaryList.Children) if (c.Visibility == Visibility.Visible) { anyVisible = true; break; }
        if (anyVisible) { EmptyHint.Visibility = Visibility.Collapsed; return; }
        EmptyHint.Visibility = Visibility.Visible;
        EmptyHint.Text = !string.IsNullOrEmpty(App.CurrentWorkspaceId) ? App.GetString("Workspace_EmptyHint") : _currentFilter switch
        {
            "diary" => App.GetString("Diary_Filter_Diary_Empty"),
            "document" => App.GetString("Diary_Filter_Document_Empty"),
            _ => App.GetString("Diary_Empty_Tip")
        };
        FloatInHint();
    }

    
    public string GetFilter() => _currentFilter;
    public void SetFilter(string filter)
    {
        _currentFilter = filter;
        ApplyCardFilters();
    }

    
    public void ApplyCardFilters()
    {
        string wsId = App.CurrentWorkspaceId;
        bool wsActive = !string.IsNullOrEmpty(wsId);
        foreach (var child in DiaryList.Children)
        {
            if (child is not Border b || b.Tag is not DiaryEntry d) continue;
            bool isDoc = d.Format == "markdown";
            bool typeMatch = _currentFilter == "all" || (_currentFilter == "document" && isDoc) || (_currentFilter == "diary" && !isDoc);
            bool wsMatch = !wsActive || d.WorkspaceId == wsId;
            b.Visibility = (typeMatch && wsMatch) ? Visibility.Visible : Visibility.Collapsed;
        }
        UpdateEmptyHint();
    }

    private void FloatInHint()
    {
        EmptyHint.Opacity = 0;
        if (EmptyHint.RenderTransform is not TranslateTransform tt)
        {
            tt = new TranslateTransform { Y = 20 };
            EmptyHint.RenderTransform = tt;
        }
        else tt.Y = 20;
        var sb = new Storyboard();
        var oa = new DoubleAnimation { To = 0.6, Duration = TimeSpan.FromMilliseconds(400), EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
        Storyboard.SetTarget(oa, EmptyHint); Storyboard.SetTargetProperty(oa, "Opacity");
        sb.Children.Add(oa);
        var ya = new DoubleAnimation { To = 0, Duration = TimeSpan.FromMilliseconds(400), EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
        Storyboard.SetTarget(ya, tt); Storyboard.SetTargetProperty(ya, "Y");
        sb.Children.Add(ya);
        sb.Begin();
    }

    // ================================================================

    // ================================================================

    private MenuFlyout BuildEmptyAreaMenu()
    {
        var menu = new MenuFlyout();
        menu.MenuFlyoutPresenterStyle = (Style)Application.Current.Resources["GlassMenuFlyoutPresenterStyle"];

        
        var newDocItem = new MenuFlyoutItem
        {
            Style = (Style)Application.Current.Resources["GlassMenuFlyoutItemStyle"],
            Text = App.GetString("Diary_New_Document"),
            Icon = new PathIcon { Data = App.CreateGeometry(IconData.Document), Foreground = App.GetBrush("IconForegroundBrush") }
        };
        newDocItem.Click += (_, _) => App.MainWindow?.NavigateToEditor(null, "markdown"); 
        menu.Items.Add(newDocItem);

        var newDiaryItem = new MenuFlyoutItem
        {
            Style = (Style)Application.Current.Resources["GlassMenuFlyoutItemStyle"],
            Text = App.GetString("Menu_New_Diary"),
            Icon = new PathIcon { Data = App.CreateGeometry(IconData.NewDiary), Foreground = App.GetBrush("IconForegroundBrush") }
        };
        newDiaryItem.Click += (_, _) => App.MainWindow?.NavigateToEditor();
        menu.Items.Add(newDiaryItem);

        var importDocItem = new MenuFlyoutItem
        {
            Style = (Style)Application.Current.Resources["GlassMenuFlyoutItemStyle"],
            Text = App.GetString("Diary_Import_Document"),
            Icon = new PathIcon { Data = App.CreateGeometry(IconData.Import), Foreground = App.GetBrush("IconForegroundBrush") }
        };
        importDocItem.Click += (_, _) => _ = ImportDocument(); 
        menu.Items.Add(importDocItem);

        return menu;
    }

    
    private async System.Threading.Tasks.Task ImportDocument()
    {
        try
        {
            var picker = new FileOpenPicker();
            var hwnd = WindowNative.GetWindowHandle(App.MainWindow);
            InitializeWithWindow.Initialize(picker, hwnd);
            picker.FileTypeFilter.Add(".md");
            picker.FileTypeFilter.Add(".markdown");
            var file = await picker.PickSingleFileAsync();
            if (file == null) return;
            await ImportDocumentFromPath(file.Path);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"导入 Markdown 失败: {ex.Message}");
            App.ShowToast(App.GetString("Diary_Import_Fail")); // NH11: surface the failure to the user
        }
    }

    /// <summary>Shared import core for both the in-app picker and the system .md right-click menu
    /// ("Import Novara"): read the raw text, store it untouched, filename (sans extension) as title.
    /// Empty files are rejected silently, matching NH11.</summary>
    public async System.Threading.Tasks.Task ImportDocumentFromPath(string filePath)
    {
        try
        {
            var ext = Path.GetExtension(filePath);
            if (!string.Equals(ext, ".md", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(ext, ".markdown", StringComparison.OrdinalIgnoreCase)) return; // whitelist

            if (new FileInfo(filePath).Length > 2 * 1024 * 1024) { App.ShowToast(App.GetString("Diary_Import_TooLarge")); return; } 
            
            
            string content;
            try
            {
                var bytes = await File.ReadAllBytesAsync(filePath);
                content = new System.Text.UTF8Encoding(false, true).GetString(bytes);
                if (content.Length > 0 && content[0] == '\uFEFF') content = content.Substring(1); 
            }
            catch (System.Text.DecoderFallbackException)
            {
                App.ShowToast(App.GetString("Diary_Import_Fail"));
                return;
            }
            // NH11: empty file -> nothing to create (matches the editor's empty-doc rule)
            if (string.IsNullOrWhiteSpace(content)) return;
            var title = Path.GetFileNameWithoutExtension(filePath);
            if (string.IsNullOrWhiteSpace(title)) title = App.GetString("DiaryEditor_Untitled");
            // NH11+N2H-4: enforce the 120 non-whitespace-char title cap, matching the editor's count
            title = DiaryEditorPage.EnforceTitleLength(title);

            UpsertDiary(new DiaryEntry { Title = title, Content = content, Format = "markdown", WorkspaceId = App.CurrentWorkspaceId });
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"导入 Markdown 失败: {ex.Message}");
            App.ShowToast(App.GetString("Diary_Import_Fail")); // NH11: surface the failure to the user
        }
    }

    // Bug 21c: menu is built once; refresh pin/star item text+icon per right-click target
    private void UpdatePinStarMenuItems(DiaryEntry entry)
    {
        if (_cardPinItem != null)
        {
            _cardPinItem.Text = entry.IsPinned ? App.GetString("Menu_Unpin") : App.GetString("Menu_Pin");
            if (_cardPinItem.Icon is PathIcon p) p.Data = App.CreateGeometry(entry.IsPinned ? IconData.Unpin : IconData.Pin);
        }
        if (_cardStarItem != null)
        {
            _cardStarItem.Text = entry.IsStarred ? App.GetString("Menu_Unstar") : App.GetString("Menu_Star");
            if (_cardStarItem.Icon is PathIcon s) s.Data = App.CreateGeometry(entry.IsStarred ? IconData.Unstar : IconData.Star);
        }

        
        bool isDoc = entry.Format == "markdown";
        if (_cardExportItem != null)
            _cardExportItem.Text = isDoc ? App.GetString("Diary_Export") : App.GetString("Menu_ExportMd");
        if (_cardExportNoImageItem != null)
            _cardExportNoImageItem.Visibility = isDoc ? Visibility.Collapsed : Visibility.Visible;
    }

    private MenuFlyout BuildCardContextMenu()
    {
        var menu = new MenuFlyout();
        menu.MenuFlyoutPresenterStyle = (Style)Application.Current.Resources["GlassMenuFlyoutPresenterStyle"];

        var pinItem = new MenuFlyoutItem
        {
            Style = (Style)Application.Current.Resources["GlassMenuFlyoutItemStyle"],
            Text = App.GetString("Menu_Pin"),
            Icon = new PathIcon
            {
                Data = (Geometry)Microsoft.UI.Xaml.Markup.XamlBindingHelper.ConvertValue(typeof(Geometry), IconData.Pin),
                Foreground = App.GetBrush("IconForegroundBrush")
            }
        };
        _cardPinItem = pinItem;
        pinItem.Click += (s, _) => { if (_currentMenuTarget != null) TogglePin(_currentMenuTarget); };

        var starItem = new MenuFlyoutItem
        {
            Style = (Style)Application.Current.Resources["GlassMenuFlyoutItemStyle"],
            Text = App.GetString("Menu_Star"),
            Icon = new PathIcon
            {
                Data = (Geometry)Microsoft.UI.Xaml.Markup.XamlBindingHelper.ConvertValue(typeof(Geometry), IconData.Star),
                Foreground = App.GetBrush("IconForegroundBrush")
            }
        };
        _cardStarItem = starItem;
        starItem.Click += (s, _) => { if (_currentMenuTarget != null) ToggleStar(_currentMenuTarget); };

        var exportItem = new MenuFlyoutItem
        {
            Style = (Style)Application.Current.Resources["GlassMenuFlyoutItemStyle"],
            Text = App.GetString("Menu_ExportMd"),
            Icon = new PathIcon
            {
                Data = (Geometry)Microsoft.UI.Xaml.Markup.XamlBindingHelper.ConvertValue(typeof(Geometry), IconData.ExportMd),
                Foreground = App.GetBrush("IconForegroundBrush")
            }
        };
        exportItem.Click += (s, _e) => { if (_currentMenuTarget != null) _ = ExportDiaryAsMarkdown(_currentMenuTarget, false); };
        _cardExportItem = exportItem;

        var exportNoImageItem = new MenuFlyoutItem
        {
            Style = (Style)Application.Current.Resources["GlassMenuFlyoutItemStyle"],
            Text = App.GetString("Menu_ExportMdNoImage"),
            Icon = new PathIcon
            {
                Data = (Geometry)Microsoft.UI.Xaml.Markup.XamlBindingHelper.ConvertValue(typeof(Geometry), IconData.ExportMdNoImage),
                Foreground = App.GetBrush("IconForegroundBrush")
            }
        };
        exportNoImageItem.Click += (s, _e) => { if (_currentMenuTarget != null) _ = ExportDiaryAsMarkdown(_currentMenuTarget, true); };
        _cardExportNoImageItem = exportNoImageItem;

        var sep = new MenuFlyoutSeparator();
        var deleteItem = new MenuFlyoutItem
        {
            Style = (Style)Application.Current.Resources["GlassMenuFlyoutItemStyle"],
            Text = App.GetString("Menu_Delete"),
            Foreground = new SolidColorBrush(Color.FromArgb(0xFF, 0xFF, 0x45, 0x45)),
            Icon = new PathIcon
            {
                Data = App.CreateGeometry(IconData.SoftDelete),
                Foreground = new SolidColorBrush(Color.FromArgb(0xFF, 0xFF, 0x45, 0x45))
            }
        };
        deleteItem.Click += (s, _) => { if (_currentMenuTarget != null) OpenDeleteConfirmDialog(); };

        menu.Items.Add(pinItem);
        menu.Items.Add(starItem);
        menu.Items.Add(exportItem);
        menu.Items.Add(exportNoImageItem);
        menu.Items.Add(sep);
        menu.Items.Add(deleteItem);
        return menu;
    }

    // ================================================================

    // ================================================================

    public void UpsertDiary(DiaryEntry entry)
    {
        // D22: look up in the FULL db list, not the filtered _diaries - if the diary was trashed
        // while its editor stayed open, it is missing from _diaries but still in the db; the old
        // code fell into the else branch and saved a SECOND copy with a fresh Id.
        var all = App.Store?.Database.DiaryItems;
        var existing = all?.Find(d => d.Id == entry.Id);
        if (existing != null)
        {
            existing.Title = entry.Title;
            existing.Content = entry.Content;
            existing.Format = entry.Format; // NH12: keep the field authoritative even though format is immutable today (future conversion must not silently drop it)
            existing.ModifiedAt = entry.ModifiedAt;
            if (existing.IsDeleted) // saving resurrects the trashed diary as its single clean copy
            {
                existing.IsDeleted = false;
                existing.DeletedAt = default;
            }
            if (!_diaries.Contains(existing)) _diaries.Add(existing);
        }
        else
        {
            entry.Id = Guid.NewGuid().ToString(); entry.CreatedAt = DateTime.Now; entry.ModifiedAt = DateTime.Now;
            
            
            
            var wsId = App.CurrentWorkspaceId;
            bool inView(DiaryEntry d) => string.IsNullOrEmpty(wsId) || d.WorkspaceId == wsId;
            if (all != null && all.Any(d => !d.IsDeleted && inView(d) && d.Order > 0))
            {
                
                
                foreach (var d in all) if (!d.IsDeleted && !d.IsPinned && inView(d) && d.Order > 0) d.Order++;
                entry.Order = 1;
            }
            App.Store?.Database.DiaryItems.Add(entry); _diaries.Add(entry); // E1-01: new diary MUST land in the db (else it vanishes on restart)
        }
        App.Store?.SaveAsync();
        RefreshDiaryList();
    }

    private void TogglePin(DiaryEntry entry)
    {
        if (!entry.IsPinned)
        {
            
            foreach (var d in _diaries)
            {
                
                if (d != entry && d.IsPinned && d.Format == entry.Format && d.WorkspaceId == entry.WorkspaceId)
                {
                    d.IsPinned = false;
                    d.PinnedAt = null;
                    if (_diaryPinIcons.TryGetValue(d, out var opv)) opv.Visibility = Visibility.Collapsed;
                }
            }
            entry.IsPinned = true;
            entry.PinnedAt = DateTime.Now;
        }
        else
        {
            entry.IsPinned = false;
            entry.PinnedAt = null;
        }

        App.Store?.SaveAsync();
        if (_diaryPinIcons.TryGetValue(entry, out var pv))
            pv.Visibility = entry.IsPinned ? Visibility.Visible : Visibility.Collapsed;
        
        if (entry.IsPinned)
        {
            
            
            if (_loadCts != null) RefreshDiaryList();
            else ReorderDiaryCards();
        }
    }

    private void ToggleStar(DiaryEntry entry)
    {
        entry.IsStarred = !entry.IsStarred;
        App.Store?.SaveAsync();
        if (_diaryStarIcons.TryGetValue(entry, out var sv))
            sv.Visibility = entry.IsStarred ? Visibility.Visible : Visibility.Collapsed;
    }

    private void ReorderDiaryCards()
    {
        var ordered = DiaryList.Children.Cast<Border>()
            .OrderByDescending(c => c.Tag is DiaryEntry d && d.IsPinned)
            .ThenByDescending(c => c.Tag is DiaryEntry d && d.IsPinned ? (d.PinnedAt ?? d.ModifiedAt) : DateTime.MinValue)
            .ThenBy(c => c.Tag is DiaryEntry d ? (d.Order <= 0 ? int.MaxValue : d.Order) : int.MaxValue)
            .ThenByDescending(c => c.Tag is DiaryEntry d ? d.ModifiedAt : DateTime.MinValue)
            .ToList();
        for (int i = 0; i < ordered.Count; i++)
        {
            int idx = DiaryList.Children.IndexOf(ordered[i]);
            if (idx != i) DiaryList.Children.Move((uint)idx, (uint)i);
        }
    }

    private void DeleteDiary(DiaryEntry entry)
    {
        // Smooth removal: fade the target card out while lower cards slide up to fill the gap, then
        // drop it from data and rebuild. Find the card via its Tag (entries map 1:1 to cards).
        Microsoft.UI.Xaml.Controls.Border? card = null;
        foreach (var child in DiaryList.Children)
            if (child is Microsoft.UI.Xaml.Controls.Border b && ReferenceEquals(b.Tag, entry)) { card = b; break; }

        if (card == null || card.Parent != DiaryList)
        {
            // Fallback: nothing to animate, remove directly.
            _diaries.Remove(entry);
            entry.IsDeleted = true;
            entry.DeletedAt = DateTime.Now;
            App.Store?.SaveAsync();
            _diaryPinIcons.Remove(entry);
            _diaryStarIcons.Remove(entry);
            _currentMenuTarget = null;
            RefreshDiaryList();
            App.ShowToast(App.GetString("Common_Toast_Deleted"));
            return;
        }

        App.PlayCardRemoval(DiaryList, card, _dragging, () =>
        {
            _diaries.Remove(entry);
            entry.IsDeleted = true;
            entry.DeletedAt = DateTime.Now;
            App.Store?.SaveAsync();
            _diaryPinIcons.Remove(entry);
            _diaryStarIcons.Remove(entry);
            _currentMenuTarget = null;
            RefreshDiaryList();
            App.ShowToast(App.GetString("Common_Toast_Deleted"));
        });
    }

    /* ========== Diary Export as Markdown ==========
    Function: 4.0 #10 - right-click a diary card and export it as a single .md file (title + HTML body
    converted to Markdown). Images stay inline as base64 data URIs so the file is self-contained.
    Corresponding UI: DiaryPage card context menu "Menu_ExportMd"
    Logic Range: below
    */
    private async System.Threading.Tasks.Task ExportDiaryAsMarkdown(DiaryEntry entry, bool removeImages)
    {
        try
        {
            if (App.MainWindow is not { } mw) return;
            var picker = new FileSavePicker();
            InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(mw));
            picker.FileTypeChoices.Add("Markdown", new List<string> { ".md" });
            picker.SuggestedFileName = SanitizeFileName(entry.Title);
            var file = await picker.PickSaveFileAsync();
            if (file == null) return;

            string md;
            if (entry.Format == "markdown")
                md = entry.Content; 
            else
                md = HtmlToMarkdown(entry.Title, entry.Content, removeImages);
            File.WriteAllText(file.Path, md, new UTF8Encoding(false));
            App.ShowToast(App.GetString("Common_Toast_Exported"));
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"导出 Markdown 失败: {ex.Message}");
        }
    }

    private static string SanitizeFileName(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return "diary";
        var cleaned = Regex.Replace(name, @"[<>:""/\\|?*]", "_");
        cleaned = cleaned.Trim().TrimEnd('.');
        return string.IsNullOrEmpty(cleaned) ? "diary" : cleaned;
    }

    private static string HtmlToMarkdown(string title, string html, bool removeImages = false)
    {
        var sb = new StringBuilder();
        sb.Append("# ").Append(title).AppendLine().AppendLine();

        if (string.IsNullOrWhiteSpace(html)) return sb.ToString();

        // N5D-09: strip REAL tags while the text is still encoded, THEN decode. Decoding first turned
        
        // silently losing plain words from the exported markdown.
        var t = html;

        // Images: preserve the base64 data URI (self-contained markdown), or drop entirely
        // (removeImages) - no placeholder, so the text stays clean.
        t = Regex.Replace(t, @"<img[^>]*src=[""']([^""']+)[""'][^>]*/?>",
            removeImages ? "" : "![]($1)", RegexOptions.IgnoreCase);

        // Inline formatting
        t = Regex.Replace(t, @"</?(?:b|strong)>", "**", RegexOptions.IgnoreCase);
        t = Regex.Replace(t, @"</?(?:i|em)>", "*", RegexOptions.IgnoreCase);
        // u / font / span / p / div: no markdown equivalent - strip the tag, keep the content
        t = Regex.Replace(t, @"</?(?:u|font|span|p|div)[^>]*>", "", RegexOptions.IgnoreCase);

        // Lists
        t = Regex.Replace(t, @"<ul[^>]*>", "\n", RegexOptions.IgnoreCase);
        t = Regex.Replace(t, @"</ul>", "\n", RegexOptions.IgnoreCase);
        t = Regex.Replace(t, @"<ol[^>]*>", "\n", RegexOptions.IgnoreCase);
        t = Regex.Replace(t, @"</ol>", "\n", RegexOptions.IgnoreCase);
        t = Regex.Replace(t, @"<li[^>]*>", "- ", RegexOptions.IgnoreCase);
        t = Regex.Replace(t, @"</li>", "\n", RegexOptions.IgnoreCase);

        // Line breaks
        t = Regex.Replace(t, @"<br\s*/?>", "\n", RegexOptions.IgnoreCase);

        // Any remaining tag -> strip
        t = Regex.Replace(t, @"<[^>]+>", "");

        // N5D-09: decode LAST - entities the user typed as visible text ("&lt;div&gt;") survive the
        // tag stripper (they are not real tags while encoded) and only become literal text here.
        t = System.Net.WebUtility.HtmlDecode(t);

        // Collapse 3+ newlines
        t = Regex.Replace(t, @"\n{3,}", "\n\n");

        sb.Append(t.Trim());
        return sb.ToString();
    }

    // ================================================================

    // ================================================================

    private void InitDeleteConfirmAnimations()
    {
        // Show animation
        var scrimFadeIn = new DoubleAnimation { To = 1, Duration = TimeSpan.FromMilliseconds(250) };
        Storyboard.SetTarget(scrimFadeIn, DeleteConfirmScrim);
        Storyboard.SetTargetProperty(scrimFadeIn, "Opacity");
        _deleteConfirmShowStoryboard.Children.Add(scrimFadeIn);

        var dialogFadeIn = new DoubleAnimation { To = 1, Duration = TimeSpan.FromMilliseconds(300) };
        Storyboard.SetTarget(dialogFadeIn, DeleteConfirmDialog);
        Storyboard.SetTargetProperty(dialogFadeIn, "Opacity");
        _deleteConfirmShowStoryboard.Children.Add(dialogFadeIn);

        var scaleXIn = new DoubleAnimation { To = 1, Duration = TimeSpan.FromMilliseconds(400), EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
        Storyboard.SetTarget(scaleXIn, DeleteConfirmDialogTransform);
        Storyboard.SetTargetProperty(scaleXIn, "ScaleX");
        _deleteConfirmShowStoryboard.Children.Add(scaleXIn);

        var scaleYIn = new DoubleAnimation { To = 1, Duration = TimeSpan.FromMilliseconds(400), EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
        Storyboard.SetTarget(scaleYIn, DeleteConfirmDialogTransform);
        Storyboard.SetTargetProperty(scaleYIn, "ScaleY");
        _deleteConfirmShowStoryboard.Children.Add(scaleYIn);

        var slideUp = new DoubleAnimation { To = 0, Duration = TimeSpan.FromMilliseconds(400), EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
        Storyboard.SetTarget(slideUp, DeleteConfirmDialogTransform);
        Storyboard.SetTargetProperty(slideUp, "TranslateY");
        _deleteConfirmShowStoryboard.Children.Add(slideUp);

        // Hide animation
        var scrimFadeOut = new DoubleAnimation { To = 0, Duration = TimeSpan.FromMilliseconds(200) };
        Storyboard.SetTarget(scrimFadeOut, DeleteConfirmScrim);
        Storyboard.SetTargetProperty(scrimFadeOut, "Opacity");
        _deleteConfirmHideStoryboard.Children.Add(scrimFadeOut);

        var dialogFadeOut = new DoubleAnimation { To = 0, Duration = TimeSpan.FromMilliseconds(200) };
        Storyboard.SetTarget(dialogFadeOut, DeleteConfirmDialog);
        Storyboard.SetTargetProperty(dialogFadeOut, "Opacity");
        _deleteConfirmHideStoryboard.Children.Add(dialogFadeOut);

        var scaleXOut = new DoubleAnimation { To = 0.92, Duration = TimeSpan.FromMilliseconds(250), EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
        Storyboard.SetTarget(scaleXOut, DeleteConfirmDialogTransform);
        Storyboard.SetTargetProperty(scaleXOut, "ScaleX");
        _deleteConfirmHideStoryboard.Children.Add(scaleXOut);

        var scaleYOut = new DoubleAnimation { To = 0.92, Duration = TimeSpan.FromMilliseconds(250), EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
        Storyboard.SetTarget(scaleYOut, DeleteConfirmDialogTransform);
        Storyboard.SetTargetProperty(scaleYOut, "ScaleY");
        _deleteConfirmHideStoryboard.Children.Add(scaleYOut);

        var slideDown = new DoubleAnimation { To = 20, Duration = TimeSpan.FromMilliseconds(250), EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
        Storyboard.SetTarget(slideDown, DeleteConfirmDialogTransform);
        Storyboard.SetTargetProperty(slideDown, "TranslateY");
        _deleteConfirmHideStoryboard.Children.Add(slideDown);

        _deleteConfirmHideStoryboard.Completed += (_, _) =>
        {
            DeleteConfirmOverlay.Visibility = Visibility.Collapsed;
        };
    }

    private void OpenDeleteConfirmDialog()
    {
        DeleteConfirmDialogTransform.ScaleX = 0.94; DeleteConfirmDialogTransform.ScaleY = 0.94; DeleteConfirmDialogTransform.TranslateY = 24;
        DeleteConfirmDialog.Opacity = 0;
        DeleteConfirmScrim.Opacity = 0;
        DeleteConfirmOverlay.Visibility = Visibility.Visible;
        DialogDepth.VeilShow(); 
        _deleteConfirmShowStoryboard.Stop();
        _deleteConfirmShowStoryboard.Begin();
    }

    private void CloseDeleteConfirmDialog()
    {
        DialogDepth.VeilHide(); 
        _deleteConfirmHideStoryboard.Begin();
        _currentMenuTarget = null;
    }

    private void DeleteConfirmCloseButton_Click(object sender, RoutedEventArgs e) => CloseDeleteConfirmDialog();
    private void DeleteCancelButton_Click(object sender, RoutedEventArgs e) => CloseDeleteConfirmDialog();

    private void DeleteConfirmScrim_Tapped(object sender, TappedRoutedEventArgs e)
    {
        if (ReferenceEquals(e.OriginalSource, DeleteConfirmScrim)) CloseDeleteConfirmDialog();
    }

    private void DeleteConfirmButton_Click(object sender, RoutedEventArgs e)
    {
        if (_currentMenuTarget != null)
        {
            DeleteDiary(_currentMenuTarget);
        }
        CloseDeleteConfirmDialog();
    }

    private void RootGrid_ContextRequested(UIElement sender, ContextRequestedEventArgs args)
    {
        // N5D-06: keyboard Menu / Shift+F10 carries no position - fall back like the card menu does.
        if (!args.TryGetPosition(RootGrid, out var point)) point = new Windows.Foundation.Point(0, 0);
        _emptyAreaMenu.ShowAt(RootGrid, point);
    }
}
