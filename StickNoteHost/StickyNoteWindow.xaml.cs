using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Windows.UI;
using Windows.UI.Text;
using Microsoft.UI;

namespace StickNoteHost;









public sealed partial class StickyNoteWindow : Window
{
    public bool Locked { get; private set; }
    public string NoteId { get; set; } = "";
    public int PositionIndex { get; set; }
    public bool IsReminder { get; private set; }
    public DateTimeOffset? DueTime { get; set; }

    public StickyNoteWindow(string theme, bool isReminder, DateTimeOffset? dueTime)
    {
        InitializeComponent();
        Title = App.T("Novara 桌面便签", "Novara Sticky Notes", "Novara 桌面便籤", "Novara 스티커 메모", "Novara 付箋");
        _lastTheme = theme;
        IsReminder = isReminder;
        DueTime = dueTime;
        if (isReminder)
        {
            TitleText.Text = App.T("提醒", "Reminder", "提醒", "알림", "リマインダー");
            TitleText.Visibility = Visibility.Collapsed;
            RootBorder.MinWidth = 200;
            RootBorder.MinHeight = 110;
        }

        var presenter = (OverlappedPresenter)AppWindow.Presenter;
        presenter.SetBorderAndTitleBar(false, false);
        presenter.IsResizable = false;
        presenter.IsMinimizable = false;
        presenter.IsMaximizable = false;
        presenter.IsAlwaysOnTop = false;





        Closed += (_, _) => { App.Log("窗口 Closed 触发"); StopCountdownTimer(); _saveTodoTimer?.Stop(); _saveTodoTimer = null; _closed = true; App.NoteWindows.Remove(NoteId); };



        AppWindow.Closing += (_, _) => { App.Log("AppWindow.Closing 触发"); FlushTodoSave(); App.RemoveNote(NoteId); };







        Activated += (_, _) =>
        {


            try
            {
                var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
                NativeMethods.HideTaskbarIcon(hwnd);
                if (Locked) NativeMethods.SetTopmost(hwnd);
            }
            catch { }
            if (!_sized)
            {
                _sized = true;
                ForceInitialSize();
            }
            ApplyTheme(_lastTheme);
        };
    }

    public void SetLocked(bool locked)
    {
        Locked = locked;
        var presenter = (OverlappedPresenter)AppWindow.Presenter;
        presenter.IsAlwaysOnTop = locked;

        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        if (locked) NativeMethods.SetTopmost(hwnd);
        else NativeMethods.SetNotTopmost(hwnd);
    }


    public void RefreshLocalizedTexts()
    {
        Title = App.T("Novara 桌面便签", "Novara Sticky Notes", "Novara 桌面便籤", "Novara 스티커 메모", "Novara 付箋");
        if (IsReminder) TitleText.Text = App.T("提醒", "Reminder", "提醒", "알림", "リマインダー");
        UpdateCountdown();
    }


    public void ShowWindow()
    {        AppWindow.Show();
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        _ = NativeMethods.ShowWindow(hwnd, NativeMethods.SW_RESTORE);
        if (Locked) NativeMethods.SetTopmost(hwnd);
    }

    public void SetContent(string title, string content, DateTimeOffset? dueTime = null, List<StickyTodoItem>? items = null)
    {
        TitleText.Text = IsReminder ? App.T("提醒", "Reminder", "提醒", "알림", "リマインダー") : title;
        if (items != null)
        {




            if (!TodoItemsEqual(_items, items))
            {

                if (_todoDirty && _items != null)
                {


                    var byLabel = new Dictionary<string, StickyTodoItem>();
                    foreach (var it in _items)
                        if (!string.IsNullOrEmpty(it.Label) && !byLabel.ContainsKey(it.Label)) byLabel[it.Label] = it;
                    foreach (var it in items)
                        if (!string.IsNullOrEmpty(it.Label) && byLabel.TryGetValue(it.Label, out var keep))
                            it.Checked = keep.Checked;
                }
                _items = items;
                RenderTodoList();
            }
            CountdownText.Visibility = Visibility.Collapsed;
            StopCountdownTimer();
            return;
        }
        NoteBox.Text = content;
        if (dueTime.HasValue)
        {


            if (_dueFired && dueTime.Value != DueTime) _dueRescheduled = true;
            DueTime = dueTime;
        }
        if (IsReminder)
        {


            NoteBox.TextWrapping = TextWrapping.Wrap;
            NoteBox.TextTrimming = TextTrimming.None;
            NoteBox.HorizontalAlignment = HorizontalAlignment.Stretch;
            CountdownText.Visibility = Visibility.Visible;
            UpdateCountdown();
            if (!_dueFired) StartCountdownTimer();
        }
        else
        {
            NoteBox.TextWrapping = TextWrapping.Wrap;
            NoteBox.TextTrimming = TextTrimming.None;
            NoteBox.HorizontalAlignment = HorizontalAlignment.Left;
            CountdownText.Visibility = Visibility.Collapsed;
            StopCountdownTimer();
        }
    }



    private void RenderTodoList()
    {
        if (_items == null) return;
        var panel = new StackPanel { Spacing = 2 };
        for (int i = 0; i < _items.Count; i++)
            panel.Children.Add(BuildTodoRow(_items, i));
        NoteScroll.Content = panel;
    }


    private static bool TodoItemsEqual(List<StickyTodoItem>? a, List<StickyTodoItem>? b)
    {
        if (ReferenceEquals(a, b)) return true;
        if (a == null || b == null || a.Count != b.Count) return false;
        for (int i = 0; i < a.Count; i++)
            if (!string.Equals(a[i].Label, b[i].Label, StringComparison.Ordinal) || a[i].Checked != b[i].Checked) return false;
        return true;
    }


    public void FlushPendingTodoSave() => FlushTodoSave();

    private Grid BuildTodoRow(List<StickyTodoItem> items, int index)
    {
        var it = items[index];
        bool chk = it.Checked;
        var brand = Color.FromArgb(0xFF, 0x72, 0x76, 0xFF);

        var row = new Grid { ColumnSpacing = 8, Margin = new Thickness(index > 0 ? 22 : 0, 3, 0, 3) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var checkBox = new Border
        {
            Width = 18, Height = 18, CornerRadius = new CornerRadius(4),
            Background = chk ? new SolidColorBrush(brand) : new SolidColorBrush(Color.FromArgb(0, 0, 0, 0)),
            BorderBrush = chk ? new SolidColorBrush(brand) : _borderBrush,
            BorderThickness = new Thickness(1.5),
            HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center,
        };
        var check = new Microsoft.UI.Xaml.Shapes.Path
        {
            Data = (Geometry)Microsoft.UI.Xaml.Markup.XamlBindingHelper.ConvertValue(typeof(Geometry), "M5 11l4 5 10-8"),
            Stroke = new SolidColorBrush(Color.FromArgb(0xFF, 0xFF, 0xFF, 0xFF)),
            StrokeThickness = 2.5, Width = 14, Height = 14, Stretch = Stretch.Uniform,
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
            Visibility = chk ? Visibility.Visible : Visibility.Collapsed, IsHitTestVisible = false,
        };
        checkBox.Child = check;
        var cb = new Border { Width = 28, Height = 28, Background = new SolidColorBrush(Color.FromArgb(0, 0, 0, 0)), VerticalAlignment = VerticalAlignment.Center, Child = checkBox };
        Grid.SetColumn(cb, 0); row.Children.Add(cb);

        var tb = new TextBlock
        {
            Text = it.Label, FontSize = 14, Foreground = _fgBrush, VerticalAlignment = VerticalAlignment.Center,
            TextWrapping = TextWrapping.Wrap, Opacity = chk ? 0.45 : 1,
            TextDecorations = chk ? TextDecorations.Strikethrough : TextDecorations.None,
        };
        var tw = new Grid(); tw.Children.Add(tb);
        Grid.SetColumn(tw, 1); row.Children.Add(tw);

        cb.Tapped += (_, _) => ToggleTodoItem(items, index);
        return row;
    }

    private void ToggleTodoItem(List<StickyTodoItem> items, int index)
    {


        items[index].Checked = !items[index].Checked;
        if (index > 0)
        {
            bool allSubChecked = true;
            for (int i = 1; i < items.Count; i++) if (!items[i].Checked) { allSubChecked = false; break; }
            items[0].Checked = allSubChecked;
        }
        _todoDirty = true;
        RenderTodoList();
        ScheduleSaveTodoItems();
    }


    private void ScheduleSaveTodoItems()
    {
        if (_saveTodoTimer == null)
        {
            _saveTodoTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
            _saveTodoTimer.Tick += (_, _) =>
            {
                _saveTodoTimer.Stop();
                _saveTodoTimer = null;


                var ok = _items != null && App.UpdateTodoItems(NoteId, _items);
                if (ok) _todoDirty = false;
            };
        }
        _saveTodoTimer.Stop();
        _saveTodoTimer.Start();
    }


    private void FlushTodoSave()
    {
        if (_saveTodoTimer == null) return;
        _saveTodoTimer.Stop();
        _saveTodoTimer = null;

        var ok = _items != null && App.UpdateTodoItems(NoteId, _items);
        if (ok) _todoDirty = false;
    }


    private void UpdateCountdown()
    {
        if (!IsReminder || DueTime == null)
        {


            CountdownText.Visibility = Visibility.Collapsed;
            return;
        }
        CountdownText.Visibility = Visibility.Visible;
        var remain = DueTime.Value - DateTimeOffset.Now;
        if (remain.TotalSeconds <= 0)
        {
            CountdownText.Text = App.T("已到期", "Due", "已到期", "만료됨", "期限切れ");
            FireDue();
            return;
        }
        CountdownText.Text = remain.Days > 0
            ? remain.ToString(@"d\.hh\:mm\:ss")
            : remain.ToString(@"hh\:mm\:ss");
    }

    private bool _dueFired;
    private bool _dueRescheduled;
    private bool _closed;


    private async void FireDue()
    {
        if (_dueFired) return;
        _dueFired = true;
        StopCountdownTimer();

        var toast = NoteBox.Text ?? "";
        if (toast.Length > 120) toast = toast.Substring(0, 120) + "…";
        ToastService.Show(App.T("提醒", "Reminder", "提醒", "알림", "リマインダー"), toast);

        for (int i = 0; i < 3; i++)
        {
            if (_closed) return;
            if (_dueRescheduled) { _dueFired = false; _dueRescheduled = false; StartCountdownTimer(); return; }
            try { _ = NativeMethods.MessageBeep(0x30); } catch { }
            await Task.Delay(500);
        }

        var red = new SolidColorBrush(Color.FromArgb(0xFF, 0x8B, 0x2A, 0x2A));
        for (int i = 0; i < 5; i++)
        {
            if (_closed) return;
            if (_dueRescheduled) { _dueFired = false; _dueRescheduled = false; StartCountdownTimer(); return; }
            RootBorder.Background = red;
            await Task.Delay(300);
            if (_closed) return;
            if (_dueRescheduled) { _dueFired = false; _dueRescheduled = false; ApplyTheme(_lastTheme); StartCountdownTimer(); return; }
            ApplyTheme(_lastTheme);
            await Task.Delay(300);
        }
        if (_closed) return;
        if (_dueRescheduled) { _dueFired = false; _dueRescheduled = false; ApplyTheme(_lastTheme); StartCountdownTimer(); return; }
        App.NoteWindows.Remove(NoteId);
        Close();
        App.RemoveNote(NoteId);
    }

    private void StartCountdownTimer()
    {
        if (_countdownTimer != null) return;

        if (!IsReminder || DueTime == null) return;
        _countdownTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _countdownTimer.Tick += (_, _) => UpdateCountdown();
        _countdownTimer.Start();
    }

    private void StopCountdownTimer()
    {
        _countdownTimer?.Stop();
        _countdownTimer = null;
    }

    private DispatcherTimer? _countdownTimer;


    public void ApplyTheme(string theme)
    {
        _lastTheme = theme;
        App.Log($"ApplyTheme: {theme}");
        bool dark = theme == "dark";


        RootBorder.RequestedTheme = dark ? ElementTheme.Dark : ElementTheme.Light;
        RootBorder.Background = new SolidColorBrush(dark
            ? Color.FromArgb(0xFF, 0x1E, 0x1E, 0x1E)
            : Color.FromArgb(0xFF, 0xF2, 0xF2, 0xF2));
        var fg = new SolidColorBrush(dark
            ? Color.FromArgb(0xDD, 0xFF, 0xFF, 0xFF)
            : Color.FromArgb(0xDD, 0x00, 0x00, 0x00));
        _borderBrush = new SolidColorBrush(dark
            ? Color.FromArgb(0x33, 0xFF, 0xFF, 0xFF)
            : Color.FromArgb(0x40, 0x00, 0x00, 0x00));
        RootBorder.BorderBrush = _borderBrush;
        TitleText.Foreground = _brandBrush;
        NoteBox.Foreground = fg;
        _fgBrush = fg;
        if (_items != null)
        {



            double offset = NoteScroll.VerticalOffset;
            RenderTodoList();
            NoteScroll.UpdateLayout();
            NoteScroll.ScrollToVerticalOffset(offset);
        }
    }

    private SolidColorBrush? _fgBrush;
    private SolidColorBrush? _borderBrush;
    private readonly SolidColorBrush _brandBrush = new(Color.FromArgb(0xFF, 0x72, 0x76, 0xFF));
    private string _lastTheme = "light";
    private List<StickyTodoItem>? _items;
    private DispatcherTimer? _saveTodoTimer;
    private bool _todoDirty;

    public void HideFromTaskbar()
    {
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        NativeMethods.HideTaskbarIcon(hwnd);
    }

    private void RootBorder_RightTapped(object sender, RightTappedRoutedEventArgs e)
    {
        var menu = new MenuFlyout
        {
            MenuFlyoutPresenterStyle = (Style)Application.Current.Resources["GlassMenuFlyoutPresenterStyle"]
        };
        if (IsReminder)
        {

            var edit = new MenuFlyoutItem
            {
                Text = App.T("修改", "Modify", "修改", "수정", "編集"),
                Icon = MakeIcon(IconPaths.Edit),
            };
            edit.Click += (_, _) => NovaraBridge.EditReminder(NoteId);
            menu.Items.Add(edit);
        }
        else
        {
            var edit = new MenuFlyoutItem
            {
                Text = App.T("编辑", "Edit", "編輯", "편집", "編集"),
                Icon = MakeIcon(IconPaths.Edit),
            };
            edit.Click += (_, _) => NovaraBridge.EditNote(NoteId);
            menu.Items.Add(edit);
        }
        var lockItem = new MenuFlyoutItem
        {
            Text = Locked ? App.T("解锁", "Unlock", "解鎖", "잠금 해제", "ロック解除") : App.T("锁定", "Lock", "鎖定", "잠금", "ロック"),
            Icon = MakeIcon(Locked ? IconPaths.Unpin : IconPaths.Pin),
        };
        lockItem.Click += (_, _) =>
        {
            SetLocked(!Locked);
            if (!Locked) Activate();
        };
        var close = new MenuFlyoutItem
        {
            Text = App.T("关闭", "Close", "關閉", "닫기", "閉じる"),
            Icon = MakeIcon(IconPaths.Close),
        };
        close.Click += (_, _) =>
        {
            App.NoteWindows.Remove(NoteId);
            Close();
            App.RemoveNote(NoteId);
        };
        menu.Items.Add(lockItem);
        menu.Items.Add(new MenuFlyoutSeparator());
        menu.Items.Add(close);
        menu.ShowAt(RootBorder, e.GetPosition(RootBorder));
    }




    private PathIcon MakeIcon(string path) => new()
    {
        Data = MakeGeometry(path),
        Foreground = _fgBrush ?? new SolidColorBrush(Colors.Gray),
    };

    private static Geometry MakeGeometry(string path) =>
        (Geometry)Microsoft.UI.Xaml.Markup.XamlBindingHelper.ConvertValue(typeof(Geometry), path);

    private void ForceInitialSize()
    {
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        double scale = NativeMethods.GetDpiForWindow(hwnd) / 96.0;
        var mi = new NativeMethods.MONITORINFO { cbSize = Marshal.SizeOf<NativeMethods.MONITORINFO>() };
        _ = NativeMethods.GetMonitorInfoW(NativeMethods.MonitorFromWindow(hwnd, 2), ref mi);



        int ox = mi.rcWork.Left, oy = mi.rcWork.Top;
        int waW = mi.rcWork.Right - mi.rcWork.Left;
        int waH = mi.rcWork.Bottom - mi.rcWork.Top;
        int w = (int)(waW * 0.28), h = (int)(waH * 0.58);
        if (w < 120) w = 400;
        if (h < 120) h = 600;
        if (IsReminder)
        {




            int rw = (int)(300 * scale), rh = (int)(180 * scale);
            int margin = (int)(24 * scale);
            int x = ox + waW - rw - margin - (int)(PositionIndex * 32 * scale);
            int y = oy + waH - rh - margin;
            if (x < ox) x = ox;
            if (y < oy) y = oy;
            NativeMethods.SetWindowSizePos(hwnd, x, y, rw, rh);
        }
        else
        {

            int nx = ox + (int)((200 + PositionIndex * 32) * scale);
            int ny = oy + (int)((220 + PositionIndex * 32) * scale);
            nx = Math.Clamp(nx, ox, Math.Max(ox, ox + waW - w - 24));
            ny = Math.Clamp(ny, oy, Math.Max(oy, oy + waH - h - 24));
            NativeMethods.SetWindowSizePos(hwnd, nx, ny, w, h);
        }
    }



    private const double EdgeHot = 8.0;
    private int _dragMode;
    private int _dragStartX, _dragStartY, _dragStartW, _dragStartH;
    private NativeMethods.POINT _dragStartCursor;
    private bool _dragActive;

    private void RootBorder_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (Locked) return;
        var pt = e.GetCurrentPoint(RootBorder);
        if (pt.Properties.IsRightButtonPressed || !pt.Properties.IsLeftButtonPressed) return;

        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        var pos = pt.Position;
        double w = RootBorder.ActualWidth, h = RootBorder.ActualHeight;
        bool left = pos.X <= EdgeHot, right = pos.X >= w - EdgeHot;
        bool top = pos.Y <= EdgeHot, bottom = pos.Y >= h - EdgeHot;
        _dragMode = left && top ? NativeMethods.HTTOPLEFT
                  : right && top ? NativeMethods.HTTOPRIGHT
                  : left && bottom ? NativeMethods.HTBOTTOMLEFT
                  : right && bottom ? NativeMethods.HTBOTTOMRIGHT
                  : top ? NativeMethods.HTTOP
                  : bottom ? NativeMethods.HTBOTTOM
                  : left ? NativeMethods.HTLEFT
                  : right ? NativeMethods.HTRIGHT
                  : NativeMethods.HTCAPTION;

        NativeMethods.GetWindowRect(hwnd, out var r);
        _dragStartX = r.Left; _dragStartY = r.Top;
        _dragStartW = r.Right - r.Left; _dragStartH = r.Bottom - r.Top;
        NativeMethods.GetCursorPos(out _dragStartCursor);
        _dragActive = true;
        RootBorder.CapturePointer(e.Pointer);
        e.Handled = true;
    }

    private void RootBorder_PointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (!_dragActive) return;
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        NativeMethods.GetCursorPos(out var c);
        int dx = c.X - _dragStartCursor.X, dy = c.Y - _dragStartCursor.Y;

        if (_dragMode == NativeMethods.HTCAPTION)
        {
            int nx = _dragStartX + dx, ny = _dragStartY + dy;

            var mi = new NativeMethods.MONITORINFO { cbSize = Marshal.SizeOf<NativeMethods.MONITORINFO>() };
            _ = NativeMethods.GetMonitorInfoW(NativeMethods.MonitorFromWindow(hwnd, 2), ref mi);


            int minVis = (int)(60 * NativeMethods.GetDpiForWindow(hwnd) / 96.0);
            nx = Math.Clamp(nx, mi.rcWork.Left - _dragStartW + minVis, mi.rcWork.Right - minVis);
            ny = Math.Clamp(ny, mi.rcWork.Top - _dragStartH + minVis, mi.rcWork.Bottom - minVis);
            NativeMethods.SetWindowSizePos(hwnd, nx, ny, _dragStartW, _dragStartH);
            return;
        }


        double scale = NativeMethods.GetDpiForWindow(hwnd) / 96.0;
        int minW = (int)((IsReminder ? 200 : 300) * scale), minH = (int)((IsReminder ? 110 : 450) * scale);
        int L = _dragStartX, T = _dragStartY;
        int R = _dragStartX + _dragStartW, B = _dragStartY + _dragStartH;
        bool l = _dragMode is NativeMethods.HTLEFT or NativeMethods.HTTOPLEFT or NativeMethods.HTBOTTOMLEFT;
        bool r2 = _dragMode is NativeMethods.HTRIGHT or NativeMethods.HTTOPRIGHT or NativeMethods.HTBOTTOMRIGHT;
        bool t = _dragMode is NativeMethods.HTTOP or NativeMethods.HTTOPLEFT or NativeMethods.HTTOPRIGHT;
        bool b = _dragMode is NativeMethods.HTBOTTOM or NativeMethods.HTBOTTOMLEFT or NativeMethods.HTBOTTOMRIGHT;
        if (l) L = _dragStartX + dx;
        if (r2) R = _dragStartX + _dragStartW + dx;
        if (t) T = _dragStartY + dy;
        if (b) B = _dragStartY + _dragStartH + dy;
        if (R - L < minW) { if (l) L = R - minW; else R = L + minW; }
        if (B - T < minH) { if (t) T = B - minH; else B = T + minH; }

        var mi2 = new NativeMethods.MONITORINFO { cbSize = Marshal.SizeOf<NativeMethods.MONITORINFO>() };
        _ = NativeMethods.GetMonitorInfoW(NativeMethods.MonitorFromWindow(hwnd, 2), ref mi2);

        int minVis2 = (int)(60 * scale);
        int w = R - L, h = B - T;
        L = Math.Clamp(L, mi2.rcWork.Left - w + minVis2, mi2.rcWork.Right - minVis2);
        T = Math.Clamp(T, mi2.rcWork.Top - h + minVis2, mi2.rcWork.Bottom - minVis2);
        w = R - L; h = B - T;

        NativeMethods.SetWindowSizePos(hwnd, L, T, w, h);
        e.Handled = true;
    }

    private void RootBorder_PointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (!_dragActive) return;
        _dragActive = false;
        RootBorder.ReleasePointerCapture(e.Pointer);
        e.Handled = true;
    }

    private void RootBorder_PointerCaptureLost(object sender, PointerRoutedEventArgs e)
    {
        _dragActive = false;
    }

    private bool _sized;

}


internal static class NativeMethods
{
    public const int SW_RESTORE = 9;

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)]
    private static extern nint GetWindowLongPtr(nint hWnd, int nIndex);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
    private static extern nint SetWindowLongPtr(nint hWnd, int nIndex, nint dwNewLong);
    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(nint hWnd, nint hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

    private const int GWL_EXSTYLE = -20;
    private const long WS_EX_TOOLWINDOW = 0x00000080L;
    private const long WS_EX_APPWINDOW = 0x00040000L;
    private static readonly nint HWND_TOPMOST = new(-1);
    private const uint SWP_NOSIZE = 0x0001;
    private const uint SWP_NOMOVE = 0x0002;
    private const uint SWP_NOACTIVATE = 0x0010;
    private const uint SWP_NOZORDER = 0x0004;

    public static void HideTaskbarIcon(nint hwnd)
    {
        long exStyle = (long)GetWindowLongPtr(hwnd, GWL_EXSTYLE);
        exStyle |= WS_EX_TOOLWINDOW;
        exStyle &= ~WS_EX_APPWINDOW;
        _ = SetWindowLongPtr(hwnd, GWL_EXSTYLE, (nint)exStyle);
    }

    public static void SetTopmost(nint hwnd)
    {
        _ = SetWindowPos(hwnd, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
    }

    public static void SetNotTopmost(nint hwnd)
    {


        _ = SetWindowPos(hwnd, new(-2), 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
    }

    public static bool SetWindowSizePos(nint hwnd, int x, int y, int w, int h)
    {
        return SetWindowPos(hwnd, IntPtr.Zero, x, y, w, h, SWP_NOZORDER);
    }

    [DllImport("user32.dll")]
    public static extern bool GetWindowRect(nint hWnd, out RECT lpRect);
    [DllImport("user32.dll")]
    public static extern bool GetCursorPos(out POINT lpPoint);
    [DllImport("user32.dll")]
    public static extern bool ShowWindow(nint hWnd, int nCmdShow);
    [DllImport("user32.dll")]
    public static extern bool MessageBeep(uint uType);
    [DllImport("user32.dll")]
    internal static extern uint GetDpiForWindow(nint hWnd);
    [DllImport("user32.dll")]
    public static extern nint MonitorFromWindow(nint hwnd, uint dwFlags);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern bool GetMonitorInfoW(nint hMonitor, ref MONITORINFO lpmi);

    public const int HTCAPTION = 2;
    public const int HTLEFT = 10, HTRIGHT = 11, HTTOP = 12, HTTOPLEFT = 13, HTTOPRIGHT = 14;
    public const int HTBOTTOM = 15, HTBOTTOMLEFT = 16, HTBOTTOMRIGHT = 17;

    [StructLayout(LayoutKind.Sequential)]
    public struct POINT { public int X, Y; }

    [StructLayout(LayoutKind.Sequential)]
    public struct MONITORINFO
    {
        public int cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT { public int Left, Top, Right, Bottom; }
}
