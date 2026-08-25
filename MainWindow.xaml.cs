/* ========== Novara Main Window (MainWindow.xaml.cs) ==========
Function: Window shell - top navigation bar, welcome page, tray service, single-instance, theme/language restart overlays, app close/exit lifecycle
Corresponding UI: MainWindow.xaml.cs
Logic Range: Whole file business logic of this module
*/
using System.Collections.Generic;
using System.Runtime.InteropServices;
using H.NotifyIcon;
using H.NotifyIcon.Core;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Windowing;
using Novara.Models;
using Novara.Pages;
using Novara.Services;

namespace Novara;

public sealed partial class MainWindow : Window
{

    private BasicMemoPage _memoPage = null!;
    private FilePathPage _filePathPage = null!;
    private PlanPage _planPage = null!;
    private DiaryPage _diaryPage = null!;
    private DiaryEditorPage _diaryEditorPage = null!;
    private SettingsPage _settingsPage = null!;
    private Button? _selectedNavButton;
    private object? _previousContent;
    private SearchPage? _searchPage; // global search (3.0-4.5)
    private object? _preSearchContent; // page shown before Ctrl+K opened the search page
    private TrashPage? _trashPage; // recycle bin (3.0-14)
    private object? _preTrashContent; // page shown before the trash button opened the trash page
    private bool _trashChanged; // trash mutations happened; rebuild the tab page on close

    // ---- Auto-hiding hamburger menu (4.0): collapsed to a floating 8x48 vertical bar near the right
    //      edge (breathing horizontally), melts into the hamburger on hover/click, auto-collapses 2s
    //      after the pointer leaves (never while the pointer stays on it). ----
    private const double MenuBarW = 8, MenuBarH = 48, MenuBarR = 4;   // collapsed vertical bar
    private const double MenuBtnW = 56, MenuBtnH = 32, MenuBtnR = 8;  // expanded hamburger
    private const double MenuAnchorX = 6;    // melt anchor: bar right edge = panel margin 18 - 6 = 12px from window edge
    private const double MenuBreath = 3;     
    private const double MenuTransitionMs = 320;
    private bool _menuExpanded;
    private bool _menuOpen;         // menu flyout is open - lock the button (no collapse) while it shows
    private bool _hoverZoneHover;   // pointer is over the transparent hover zone
    private bool _menuBtnHover;     // pointer is over the button itself
    // N4W-07: _autoCollapseCts removed - it was never constructed (only Cancel()'d), a dead shell from
    // the removed 1s auto-collapse delay. Collapse is now immediate.
    // Frame-driven animation (CompositionTarget.Rendering, 60fps - DispatcherTimer/Task.Delay jitter made it ~30fps).
    private bool _menuAnimating;    // melt/collapse transition in progress
    private double _menuProgress;   // 0 = vertical bar, 1 = hamburger
    private double _menuAnimFrom, _menuAnimTo;
    private System.Diagnostics.Stopwatch? _menuAnimWatch;
    private System.Diagnostics.Stopwatch _glowWatch = System.Diagnostics.Stopwatch.StartNew(); // breathing phase clock

    /// <summary>Mark that trash mutations (restore/delete-forever/clear-all) require tab pages to reload.</summary>
    public void MarkTrashChanged() => _trashChanged = true;

    private Border? _toast;

    
    private const string ToastCheckIconPath = "M929.28 189.44l-417.28 417.28-158.208-158.208-53.248 53.248 184.832 184.832 26.624 25.6 26.624-25.6 443.904-443.904-53.248-53.248z m-417.28-158.72c-266.24 0-480.768 214.528-480.768 480.768s214.528 480.768 480.768 480.768 480.768-214.528 480.768-480.768c0-51.712-7.168-103.424-25.6-151.552l-59.904 58.88c7.168 29.696 11.776 59.392 11.776 92.672 0 225.792-181.248 407.04-407.04 407.04s-407.04-181.248-407.04-407.04 181.248-407.04 407.04-407.04c111.104 0 210.432 44.032 281.088 114.688L844.8 167.424c-84.992-84.992-203.264-136.704-332.8-136.704z";

    /// <summary>Global non-blocking toast: theme-adaptive floating capsule at the bottom-center, above
    /// everything, hit-test transparent (clicks pass through). Replaces any previous toast.
    
    public void ShowToast(string message)
    {
        if (string.IsNullOrWhiteSpace(message)) return;
        if (_toast != null) { ToastHost.Children.Remove(_toast); _toast = null; }

        var toast = new Border
        {
            Background = App.GetBrush("AppSurfaceElevatedBrush"),
            BorderBrush = App.GetBrush("AppBorderBrush"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(20),
            Padding = new Thickness(16, 10, 20, 10),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new Thickness(0, 0, 0, 28),
            Opacity = 0,
        };

        
        
        
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        var icon = new Viewbox
        {
            Width = 14,
            Height = 14,
            Stretch = Stretch.Uniform,
            Margin = new Thickness(0, 0, 8, 0),
            Child = new PathIcon
            {
                Data = App.CreateGeometry(ToastCheckIconPath),
                Foreground = App.GetBrush("AppPrimaryButtonBrush"),
            },
        };
        row.Children.Add(icon);
        row.Children.Add(new TextBlock
        {
            Text = message,
            FontSize = 14,
            Foreground = App.GetBrush("AppTextPrimaryBrush"),
            VerticalAlignment = VerticalAlignment.Center,
        });
        toast.Child = row;
        toast.RenderTransform = new TranslateTransform { Y = 12 };
        _toast = toast;
        ToastHost.Children.Add(toast);

        var sb = new Storyboard();
        var fadeIn = new DoubleAnimation { To = 1, Duration = TimeSpan.FromMilliseconds(200) };
        Storyboard.SetTarget(fadeIn, toast); Storyboard.SetTargetProperty(fadeIn, "Opacity"); sb.Children.Add(fadeIn);
        var slide = new DoubleAnimation { To = 0, Duration = TimeSpan.FromMilliseconds(200) };
        Storyboard.SetTarget(slide, toast.RenderTransform); Storyboard.SetTargetProperty(slide, "Y"); sb.Children.Add(slide);
        sb.Completed += (_, _) =>
        {
            var fadeOut = new Storyboard();
            var fo = new DoubleAnimation { To = 0, Duration = TimeSpan.FromMilliseconds(300), BeginTime = TimeSpan.FromSeconds(3) };
            Storyboard.SetTarget(fo, toast); Storyboard.SetTargetProperty(fo, "Opacity"); fadeOut.Children.Add(fo);
            fadeOut.Completed += (_, _) => { if (_toast == toast) { ToastHost.Children.Remove(toast); _toast = null; } };
            fadeOut.Begin();
        };
        sb.Begin();
    }

    public string CloseBehavior
    {
        get => _closeBehavior;
        set
        {
            if (_closeBehavior == value) return;
            _closeBehavior = value;
            UpdateTrayIcon();
        }
    }

    private bool _storeInitialized;
    private bool _windowLoadedHandled;
    private string _closeBehavior = "直接退出";

    private bool _themeRestartAnimating;

    private bool _isTrayExit;
    private bool _restarting; // C3 (Round 5): theme restart re-entry guard
    private bool _exitSaving; // N4W-02: ExitApp is now async (editor flush) - guard against re-entry during the flush
    private bool _welcomeFading; // E5-18: welcome overlay fade-out re-entry guard (rapid taps would run two fade-outs and double-fire Completed)
    private bool _closeAllowed; // C7 (Round 5): allow second Close only after editor save completes
    private bool _animCorruptHide; // D3 (Round 5): M2 corrupt-dialog hide animation re-entry guard
    private bool _animSaveFailedHide; // D3 (Round 5): M2 save-failed-dialog hide animation re-entry guard
    private bool _closingHandled; // E3-01: reset on save-failure paths so the next close retries
    private bool _corruptDialogIsIoError; // E3-12: false = corrupt (rebuild offer), true = transient IO failure (retry only, NO rebuild)
    private bool _themeChangedWhileLocked; // E3-08b: system theme changed while the lock screen covered the window - offer the restart overlay after unlock // #27: cancel close, save async, then re-Close to allow exit

    private AppWindow? _appWindow;

    private TaskbarIcon? _trayIcon;

    public System.Windows.Input.ICommand ShowWindowCommand { get; }
    public System.Windows.Input.ICommand ExitCommand { get; }

    public MainWindow()
    {
        InitializeComponent();

        // Window-level dialogs get shadows only - their scrims already cover the full window,
        // so the chrome veil must NOT stack on top (driveChrome:false).
        Services.DialogDepth.AttachContainer((Grid)Content, driveChrome: false);

        ApplyStartupTheme();

        this.SizeChanged += (_, e) => UpdateWelcomeLayout(e.Size.Width, e.Size.Height);

        ExtendsContentIntoTitleBar = true;
        SetTitleBar(CustomTitleBar);

        // Welcome shown by default; skip state corrected after store load in OnWindowLoaded
        WelcomeOverlay.Visibility = Visibility.Visible;
        CustomTitleBar.Visibility = Visibility.Collapsed;
        BottomToolbarPanel.Visibility = Visibility.Collapsed;

        WelcomeEntryAnimation.Completed += (_, _) => WelcomeHintBreathing.Begin();
        WelcomeOverlay.Loaded += (_, _) =>
        {
            if (_storeInitialized && App.Store is not { IsEncrypted: true }) WelcomeEntryAnimation.Begin();
        };
        InitBottomToolbarIcon();
        InitNavIcons();

        if (Content is FrameworkElement root)
        {
            root.ActualThemeChanged += (_, _) =>
            {
                if (App.CurrentTheme != "跟随系统") return;
                if (LockScreenFrame.Visibility == Visibility.Visible) { _themeChangedWhileLocked = true; return; } // E3-08b: remember while the lock screen covers the window (overlay would be occluded), show after unlock
                ShowThemeRestartOverlay();
            };

            root.KeyDown += (_, e) =>
            {
                if (e.Key != Windows.System.VirtualKey.Escape) return;
                if (ThemeRestartOverlay.Visibility == Visibility.Visible) { HideThemeRestartOverlay(); e.Handled = true; }
                else if (SaveFailedOverlay.Visibility == Visibility.Visible) { HideSaveFailedDialog(); e.Handled = true; }
            };

            // Global search hotkey: Ctrl+K from anywhere in the app (3.0-4.5)
            var ctrlK = new KeyboardAccelerator { Key = Windows.System.VirtualKey.K, Modifiers = Windows.System.VirtualKeyModifiers.Control };
            ctrlK.Invoked += (_, _) => { if (!HasTextInputFocus()) OpenSearchPage(); }; 
            root.KeyboardAccelerators.Add(ctrlK);
            root.KeyboardAcceleratorPlacementMode = KeyboardAcceleratorPlacementMode.Hidden; // no ctrl+k tooltip on empty areas

            
            
            void AddHotkey(Windows.System.VirtualKey key, Windows.System.VirtualKeyModifiers mods, Action action)
            {
                var acc = new KeyboardAccelerator { Key = key, Modifiers = mods };
                acc.Invoked += (_, _) => { if (CanInvokeGlobalShortcut()) action(); };
                root.KeyboardAccelerators.Add(acc);
            }
            AddHotkey(Windows.System.VirtualKey.Number1, Windows.System.VirtualKeyModifiers.Control, () => SwitchTab("Memo"));
            AddHotkey(Windows.System.VirtualKey.Number2, Windows.System.VirtualKeyModifiers.Control, () => SwitchTab("File"));
            AddHotkey(Windows.System.VirtualKey.Number3, Windows.System.VirtualKeyModifiers.Control, () => SwitchTab("Plan"));
            AddHotkey(Windows.System.VirtualKey.Number4, Windows.System.VirtualKeyModifiers.Control, () => SwitchTab("Diary"));
            AddHotkey((Windows.System.VirtualKey)188, Windows.System.VirtualKeyModifiers.Control, NavigateToSettings); // VK_OEM_COMMA = Ctrl+,
            AddHotkey(Windows.System.VirtualKey.Back, Windows.System.VirtualKeyModifiers.Control | Windows.System.VirtualKeyModifiers.Shift, OpenTrashPage);

            // Defer all native/IO work to after Loaded: early native calls crash CLR on
            // Win10 19041 with .NET10 preview (0x80131506, CET compat defect)
            root.Loaded += OnWindowLoaded;
        }

        if (App.Store != null)
            App.Store.SaveFailed += _ => DispatcherQueue.TryEnqueue(ShowSaveFailedDialog);

        ShowWindowCommand = new RelayCommand(ShowMainWindow);
        ExitCommand = new RelayCommand(ExitApp);
    }

    private void OnWindowLoaded(object sender, RoutedEventArgs e)
    {
        if (_windowLoadedHandled) return;
        _windowLoadedHandled = true;
        if (Content is FrameworkElement root) root.Loaded -= OnWindowLoaded;

        // 1. Store load + stored settings (file IO / MD5 / registry) - CLR fully ready here
        // Language hint applied first so the lock screen (encrypted startup) renders in the last
        // chosen language; when no hint exists (new user / follow-system), resolve from the OS.
        var langHint = App.LoadLanguageHint();
        App.ApplyLanguage(langHint ?? "");
        Services.LoadResult? loadResult = null;
        if (App.Store is not { IsLoaded: true })
        {
            loadResult = App.Store?.Load();
            if (loadResult != null)
                System.Diagnostics.Debug.WriteLine($"NovaraStore 加载: {loadResult.Status}{(loadResult.Detail != null ? " - " + loadResult.Detail : "")}");
        }
        // Defensive: JSON with explicit "appSettings": null must not NRE the startup chain (aligned with ImportBackup ??=)
        if (App.Store != null) App.Store.Database.AppSettings ??= new AppSettings();
        App.ApplyStoredSettings();
        TrashPage.CleanupExpired(); // 3.0-14: purge soft-deleted items past the 7-day window
        
        McpService.AuthorizeClient = clientPath =>
        {
            bool allowed = false;
            // N4C-01: no `using` - after the 2-minute wait times out this thread returns while the UI may
            // still invoke the callback seconds later; a disposed MRES made Set() throw. The guarded Set
            // below swallows that benign race (the server answer is already decided by then).
            var gate = new System.Threading.ManualResetEventSlim(false);
            DispatcherQueue.TryEnqueue(() => QueueOrShowMcpAuthorize(clientPath, r => { allowed = r; try { gate.Set(); } catch { } }));
            // NM4: bound the wait - if the user walks away from the popup the piped thread must be
            // freed (default deny) instead of blocking its session forever.
            bool answered;
            try { answered = gate.Wait(TimeSpan.FromMinutes(2)); }
            finally { gate.Dispose(); }
            return answered ? allowed : false;
        };
        McpService.Start(); 

        // Sticky-note theme sync: when the app theme is "follow system", push the current
        // system theme to stickies.json now (cold-start correction) and watch for system
        // theme changes at runtime (UISettings.ColorValuesChanged). Manual themes are unaffected.
        App.InitSystemThemeWatcher();
        Services.StickySync.UpdateLanguage(App.CurrentLanguage); // E3-09: same cold-start sync for language - follow-system users who changed the OS language must see the host menus catch up on launch
        Services.StickySync.StartWatching((id, states) => _planPage?.ApplyExternalTodoState(id, states)); // 4.0 #9: host todo-toggle reverse sync

        // Hamburger frame loop (slide + glow breathing) - subscribed once; self-checks toolbar visibility each frame.
        Microsoft.UI.Xaml.Media.CompositionTarget.Rendering += OnMenuRendering;

        // Stage-2 edit jump: consume a pending request that was raised while the app was closed.
        var pendingId = Novara.Services.EditRequest.ReadPending();
        if (pendingId.HasValue)
            DispatcherQueue.TryEnqueue(() => HandleEditRequest(pendingId.Value));

        // Stage-3 add-path: consume a pending path (cold start from the Windows right-click menu).
        var pendingPath = App.PendingAddPath ?? Novara.Services.AddPathRequest.ReadPending();
        if (!string.IsNullOrWhiteSpace(pendingPath))
            DispatcherQueue.TryEnqueue(() => HandleAddPathRequest(pendingPath));

        // Stage-3 reminder edit: consume a pending modify request (cold start from the desktop card).
        var pendingReminder = Novara.Services.ReminderEditRequest.ReadPending();
        if (pendingReminder != null)
            DispatcherQueue.TryEnqueue(() => HandleReminderEditRequest(pendingReminder));

        
        var pendingDue = App.PendingReminderId ?? Novara.Services.ReminderDueRequest.ReadPending();
        if (pendingDue.HasValue)
            DispatcherQueue.TryEnqueue(() => HandleReminderDueRequest(pendingDue.Value));
        Novara.Services.ReminderDueRequest.StartListening(id => DispatcherQueue.TryEnqueue(() => HandleReminderDueRequest(id)));

        // Stage-3: if desktop cards (notes/reminders) exist but the host is not running, wake it.
        // Dev-stage bridge until Inno registers the host Run key; idempotent once Inno ships.
        try
        {
            if (!Novara.Services.StickySync.HostRunning && Novara.Services.StickySync.HasDesktopCards())
                Novara.Services.StickySync.LaunchHost();
        }
        catch { }

        // 2a. Localize MainWindow static texts (Window cannot use x:Bind; controls carry Tag="Key")
        if (Content is FrameworkElement rootElem)
            App.ApplyLocalizedTexts(rootElem);
        // NavBar tab labels set explicitly (defensive: independent of visual-tree traversal)
        NavMemoText.Text = App.GetString("Nav_Tab_Memo");
        NavFileText.Text = App.GetString("Nav_Tab_File");
        NavPlanText.Text = App.GetString("Nav_Tab_Plan");
        NavDiaryText.Text = App.GetString("Nav_Tab_Diary");
        // Welcome subtitle: wide letter spacing tuned for CJK (zh-CN/zh-TW); ko/ja and Latin get little
        // to none (ko/ja texts are long enough that 850 pushed them past the window edge - 2026-08-11).
        WelcomeSubtitle.CharacterSpacing = App.CurrentLanguage switch
        {
            "zh-CN" or "zh-TW" => 850,
            "en-US" => 150, // slightly widened from 0 for a more relaxed look
            _ => 0
        };

        
        var welcomeSettings = App.Store?.Database.AppSettings;
        bool skipWelcome = App.LaunchedFromContextMenu
            || (welcomeSettings != null && !welcomeSettings.WelcomeOnLaunch && welcomeSettings.HasCompletedWelcome);
        if (skipWelcome)
        {
            WelcomeOverlay.Visibility = Visibility.Collapsed;
            CustomTitleBar.Visibility = Visibility.Visible;
            BottomToolbarPanel.Visibility = Visibility.Visible;
            // N5W1-02: encrypted cold start must not build tab pages from an empty db here - the cached
            // page instances get poisoned by the _storeLoaded idempotent guard and HideLockScreen would
            // reuse them blank (a later PersistAll on a blank memo page would wipe the real data).
            // The lock screen covers everything; HideLockScreen re-runs this decision with real data.
            if (App.Store is { IsLoaded: true }) ShowMainContent();
        }

        // 2b. Apply stored VisibleTabs on startup (bug fix: NavBar always showed all 4 tabs after restart;
        // previously only SettingsPage.Loaded applied it). Runs regardless of skipWelcome so the
        // welcome-click path also gets the correct column layout.
        if (App.Store?.Database.AppSettings.VisibleTabs is { Count: > 0 } tabs)
            ApplyVisibleTabs(tabs.ToHashSet());

        
        Services.AutoBackupService.SyncAutoBackupTimer(DispatcherQueue);

        // 3. Window handle / close interception / window icon
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        var windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hwnd);
        _appWindow = AppWindow.GetFromWindowId(windowId);
        _appWindow.Closing += AppWindow_Closing;

        // D1 (Round 5): clear lock PIN on window deactivation (rule G13)
        this.Activated += (_, args) =>
        {
            if (args.WindowActivationState == WindowActivationState.Deactivated &&
                LockScreenFrame.Visibility == Visibility.Visible &&
                LockScreenFrame.Content is LockScreenPage p)
                p.ClearInput();
        };

        var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "logo.ico");
        try { if (File.Exists(iconPath)) _appWindow.SetIcon(iconPath); } catch { } // D8-5 (Round 5): icon set failure must not break startup

        // 4. Close behavior from store (setter triggers tray create/destroy) + tray sync
        CloseBehavior = App.Store?.Database.AppSettings.CloseBehavior ?? "直接退出";
        UpdateTrayIcon();

        // 5. Title bar button colors (GetSysColor P/Invoke + AppWindow)
        App.UpdateTitleBarColors();

        // 6. Corrupted store dialog (plain-DB path)
        if (loadResult?.Status is LoadStatus.Corrupted or LoadStatus.IoError)
            ShowCorruptStoreDialog();

        // 7. Lock screen (encrypted DB)
        if (App.Store is { IsEncrypted: true }) ShowLockScreen();

        // 8. Nav hover brushes (App.GetBrush -> GetSysColor P/Invoke, deferred)
        HookNavHoverEvents();

        // 9. Welcome entrance animation (non-encrypted, not skipped)
        _storeInitialized = true;
        if (!skipWelcome && App.Store is not { IsEncrypted: true })
            WelcomeEntryAnimation.Begin();
    }

    private void UpdateTrayIcon()
    {
        if (_closeBehavior == "托盘驻留")
        {
            CreateTrayIcon();
        }
        else
        {
            RemoveTrayIcon();
        }
    }

    /* ========== MainWindow Tray Service ==========
Function: Taskbar tray icon create/remove, tray context menu (Glass style), show/exit commands; driven by CloseBehavior
Corresponding UI: MainWindow.xaml.cs
Logic Range: Below methods in this region
*/
private void CreateTrayIcon()
    {
        if (_trayIcon != null) return;

        var menu = new MenuFlyout { MenuFlyoutPresenterStyle = (Style)Application.Current.Resources["GlassMenuFlyoutPresenterStyle"] }; // D5 (Round 5): M6 tray menu follows theme
        menu.Items.Add(new MenuFlyoutItem { Text = App.GetString("Tray_ShowWindow"), Command = ShowWindowCommand });
        menu.Items.Add(new MenuFlyoutSeparator());
        menu.Items.Add(new MenuFlyoutItem { Text = App.GetString("Tray_Exit"), Command = ExitCommand });

        _trayIcon = new TaskbarIcon
        {
            Icon = new System.Drawing.Icon(Path.Combine(AppContext.BaseDirectory, "Assets", "128.ico")),
            ToolTipText = "Novara",
            ContextMenuMode = ContextMenuMode.PopupMenu,
            MenuActivation = PopupActivationMode.RightClick,
            NoLeftClickDelay = true,
            ContextFlyout = menu,
        };
        _trayIcon.LeftClickCommand = new RelayCommand(ShowMainWindow);
        // N4R-13: DoubleClickCommand removed - with NoLeftClickDelay=true it can never fire (D26), dead wiring.
        try
        {
            _trayIcon.ForceCreate();
        }
        catch
        {

            _trayIcon = null;
        }
    }

    private void RemoveTrayIcon()
    {
        if (_trayIcon == null) return;
        _trayIcon.Dispose();
        _trayIcon = null;
    }

    private const string SingleInstanceMutexName =
#if DEBUG
        @"Local\Novara.SingleInstance.Dev"; // Debug/test build: separate mutex so dev & installed release can run side by side
#else
        @"Local\Novara.SingleInstance";
#endif
    private static Mutex? _singleInstanceMutex;

    public static bool AcquireSingleInstance()
    {
        _singleInstanceMutex = new Mutex(true, SingleInstanceMutexName, out bool createdNew);
        if (createdNew) return true;

        var hwnd = FindWindow(null, "Novara");
        if (hwnd != IntPtr.Zero)
        {
            ShowWindow(hwnd, SW_RESTORE);
            SetForegroundWindow(hwnd);
        }
        else
        {
            
            Services.ShowWindowRequest.Raise();
        }
        return false;
    }

    public static void ReleaseSingleInstance()
    {
        try { _singleInstanceMutex?.ReleaseMutex(); } catch { }
        _singleInstanceMutex = null;
    }

    /* ========== MainWindow Close/Exit Flow ==========
Function: Tray-resident hide vs direct exit; editor dirty-content save (#27), re-Close after async save (#27/C7), close re-entry guard
Corresponding UI: MainWindow.xaml.cs
Logic Range: Below methods in this region
*/
private async void AppWindow_Closing(AppWindow sender, AppWindowClosingEventArgs args)
    {
        if (!_isTrayExit && CloseBehavior == "托盘驻留")
        {
            args.Cancel = true;
            sender.Hide();
            return;
        }

        // #27: save editor content on direct exit / tray-menu exit (tray-resident X only hides; save on tray exit) - D7 (Round 5)
        // AppWindowClosingEventArgs has no deferral: Cancel first, then Close again after async save.
        if (_closingHandled)
        {
            if (!_closeAllowed) args.Cancel = true; // C7 (Round 5): cancel close requests before save completes (keep window, prevent bypass)
            return;
        }
        args.Cancel = true;
        _closingHandled = true;

        if (RootFrame.Content is DiaryEditorPage editor)
        {
            try { await editor.SaveCurrentDiaryAsync(); } catch { }
        }

        // E3-02: nothing is loaded yet (lock screen / encrypted cold start) - nothing to save, allow close.
        // SaveSync returns false on !_loaded, which would otherwise cancel the close forever.
        if (App.Store is { IsLoaded: false })
        {
            _closingHandled = false;
            _closeAllowed = true;
            this.Close();
            return;
        }
        // E4-09: a theme/language restart already persisted everything (ThemeRestartConfirm saves first);
        // skip the redundant closing save - a failure here would strand the session AFTER the mutex was
        // released and the new process launched (two instances writing the same DB).
        if (_restarting) { _closeAllowed = true; this.Close(); return; }
        
        
        if (_isTrayExit) { _closeAllowed = true; this.Close(); return; }
        try { if (App.Store?.SaveSync() == false) { _closeAllowed = false; _closingHandled = false; return; } } // E1-11 + E3-01: keep session on save failure (SaveFailed dialog shows), but RESET the guard so the next X retries the save instead of being cancelled forever
        catch { _closeAllowed = false; _closingHandled = false; return; }
        _closeAllowed = true;
        this.Close();
    }

    private void ShowMainWindow()
    {
        _appWindow?.Show();
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        ShowWindow(hwnd, SW_RESTORE);
        SetForegroundWindow(hwnd);
    }

    public void ExitApp() => ExitApp(true);

    // E5-04: SettingsPage theme/language restarts set their own _restarting, but Closing checks this
    // window's flag - without it the redundant second SaveSync runs after the mutex was released and
    // the new process launched (two instances writing the same DB).
    internal void SetRestarting() => _restarting = true;

    public async void ExitApp(bool saveFirst)
    {
        if (_exitSaving) return; // N4W-02: async method - a second invoke during the editor flush must not run the exit twice
        _exitSaving = true;
        _isTrayExit = true;
        try
        {
            // N4W-02: flush unsaved editor content BEFORE the synchronous SaveSync below. The Closing
            // handler's own editor save runs after this SaveSync, and its _isTrayExit branch then closes
            // without any further flush - without this pre-save the debounced background write dies with
            // the process and the last diary edit is lost.
            if (RootFrame.Content is DiaryEditorPage pendingEditor)
            {
                try { await pendingEditor.SaveCurrentDiaryAsync(); } catch { }
            }
            if (saveFirst)
            {
                try
                {
                    if (App.Store?.SaveSync() == false)
                    {
                        // C6 (Round 5): save failed - SaveFailed dialog already raised; keep session, do not silently lose data
                        _isTrayExit = false;
                        return;
                    }
                }
                catch
                {
                    
                    _isTrayExit = false;
                    return;
                }
            }
            try
            {
                RemoveTrayIcon();
            }
            catch
            {

            }
            ReleaseSingleInstance();
            _closingHandled = false; // E3-01: clear stale close-guard state - a previous failed save must not cancel this exit AFTER the mutex is released (zombie process + double instance)
            _closeAllowed = true;
            Close();
        }
        finally { _exitSaving = false; }
    }

    // ---- P/Invoke ----
    private const int SW_RESTORE = 9;

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr FindWindow(string? lpClassName, string lpWindowName);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    private void ShowLockScreen()
    {
        var page = new LockScreenPage();
        page.UnlockSucceeded += HideLockScreen;

        page.CorruptDetected += ShowCorruptStoreDialog;
        page.IoErrorDetected += ShowIoErrorDialog; // E3-12: transient read failure - retry-only dialog
        LockScreenFrame.Content = page;
        LockScreenFrame.Visibility = Visibility.Visible;
    }

    private void HideLockScreen()
    {
        LockScreenFrame.Visibility = Visibility.Collapsed;
        LockScreenFrame.Content = null;
        if (App.Store is { } store)
        {
            store.Database.AppSettings ??= new AppSettings();
            CloseBehavior = store.Database.AppSettings.CloseBehavior;
            App.ApplyStoredSettings();
            // N5W1-01: L61 encrypted-side parity - startup applied VisibleTabs from an empty db; the real
            // custom tabs must take effect the moment the decrypted settings are available.
            if (store.Database.AppSettings.VisibleTabs is { Count: > 0 } unlockedTabs)
                ApplyVisibleTabs(unlockedTabs.ToHashSet());
            TrashPage.CleanupExpired(); // E3-03: encrypted users never hit the plaintext-startup cleanup path - purge expired trashed items now that the db is loaded
            Services.AutoBackupService.SyncAutoBackupTimer(DispatcherQueue); // N4S-02: same E3-03 pattern - OnWindowLoaded ran before unlock with an empty db (BackupEnabled=false), so the auto-backup timer never started for encrypted users; sync it now that real settings are loaded
            if (Content is FrameworkElement root) App.ApplyLocalizedTexts(root);
            // Unlock: decide the welcome page from the real (now decrypted) settings.
            // Fixes Bug 10 (encrypted users saw the welcome page on every startup) and completes Bug 9.
            
            if (App.LaunchedFromContextMenu
                || (!store.Database.AppSettings.WelcomeOnLaunch && store.Database.AppSettings.HasCompletedWelcome))
            {
                WelcomeOverlay.Visibility = Visibility.Collapsed;
                CustomTitleBar.Visibility = Visibility.Visible;
                BottomToolbarPanel.Visibility = Visibility.Visible;
                ShowMainContent();
            }
            else
            {
                WelcomeEntryAnimation.Begin();
            }
        }

        // 4.7: a v1-CBC encrypted database unlocks fine - offer the one-time GCM migration now
        
        if (App.Store is { NeedsFormatMigration: true } && !App.Store.Database.AppSettings.GcmMigrationRejected)
            ShowMigrateFormatDialog();

        // E3-08b: the system theme may have changed while the lock screen covered the window (the
        // handler only remembers then) - dynamic controls hold stale brushes, so offer the restart
        // now. Shown after the migration dialog: if both appear, the user handles the top one first,
        // cancelling it reveals the other; a restart re-offers the migration on next launch anyway.
        if (_themeChangedWhileLocked) { _themeChangedWhileLocked = false; if (App.CurrentTheme == "跟随系统") ShowThemeRestartOverlay(); }
    }

    // ---- Dialog depth aid: global outer dimming layer (title bar + navigation area) ----
    // Works with the page Scrim: ChromeScrim covers Row 0-1, the page Scrim covers the page area,
    // same color and opacity combine into a seamless fullscreen veil. IsHitTestVisible=false keeps
    // click-through to title/nav (same as pre-feature behavior).
    // Value-coupled driving (DialogDepth.SetChromeDimLevel): the veil opacity MIRRORS the brightest
    // active page scrim every animation tick, so top and page areas can never stagger apart - the
    // old show/hide-storyboard pair faded the top only after the page fade had finished.
    
    
    private int _veilCount;

    public void VeilShow()
    {
        _veilCount++;
        ChromeScrim.Visibility = Visibility.Visible;
        
        
        ChromeScrim.Opacity = _veilCount > 1 ? 1 : 0;
        var sb = new Storyboard();
        var da = new DoubleAnimation { To = 1, Duration = TimeSpan.FromMilliseconds(250) };
        Storyboard.SetTarget(da, ChromeScrim);
        Storyboard.SetTargetProperty(da, "Opacity");
        sb.Children.Add(da);
        sb.Begin();
    }

    public void VeilHide()
    {
        _veilCount--;
        if (_veilCount > 0) return; 
        _veilCount = 0;
        
        var sb = new Storyboard();
        var da = new DoubleAnimation { To = 0, Duration = TimeSpan.FromMilliseconds(200) };
        Storyboard.SetTarget(da, ChromeScrim);
        Storyboard.SetTargetProperty(da, "Opacity");
        sb.Children.Add(da);
        sb.Completed += (_, _) => { if (_veilCount == 0) ChromeScrim.Visibility = Visibility.Collapsed; }; 
        sb.Begin();
    }

    
    public void VeilClear()
    {
        _veilCount = 0;
        ChromeScrim.Visibility = Visibility.Collapsed;
        ChromeScrim.Opacity = 0;
    }

    // ---- 4.7: one-time v1-CBC -> v2-GCM migration confirmation (after unlock) ----
    private bool _migrateFailed; // failure state: dialog switches to "retry on next launch"

    public void ShowMigrateFormatDialog()
    {
        _migrateFailed = false;
        MigrateFormatMessage.Text = App.GetString("Security_Migrate_Body");
        MigrateFormatTitle.Text = App.GetString("Security_Migrate_Title");
        MigrateLaterButton.Visibility = Visibility.Visible;
        MigrateConfirmText.Text = App.GetString("Security_Migrate_Confirm");
        MigrateFormatDialogTransform.ScaleX = 0.92;
        MigrateFormatDialogTransform.ScaleY = 0.92;
        MigrateFormatDialogTransform.TranslateY = 20;
        MigrateFormatDialog.Opacity = 0;
        MigrateFormatOverlay.Visibility = Visibility.Visible;
        var sb = new Storyboard();
        var da = new DoubleAnimation { To = 1, Duration = TimeSpan.FromMilliseconds(300) };
        Storyboard.SetTarget(da, MigrateFormatScrim); Storyboard.SetTargetProperty(da, "Opacity"); sb.Children.Add(da);
        var dda = new DoubleAnimation { To = 1, Duration = TimeSpan.FromMilliseconds(300) };
        Storyboard.SetTarget(dda, MigrateFormatDialog); Storyboard.SetTargetProperty(dda, "Opacity"); sb.Children.Add(dda);
        foreach (var (v, p) in new[] { (1.0, "ScaleX"), (1.0, "ScaleY"), (0.0, "TranslateY") })
        {
            var a = new DoubleAnimation { To = v, Duration = TimeSpan.FromMilliseconds(400), EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
            Storyboard.SetTarget(a, MigrateFormatDialogTransform); Storyboard.SetTargetProperty(a, p); sb.Children.Add(a);
        }
        sb.Begin();
    }

    private void HideMigrateFormatDialog()
    {
        var sb = new Storyboard();
        var so = new DoubleAnimation { To = 0, Duration = TimeSpan.FromMilliseconds(200) };
        Storyboard.SetTarget(so, MigrateFormatScrim); Storyboard.SetTargetProperty(so, "Opacity"); sb.Children.Add(so);
        var d = new DoubleAnimation { To = 0, Duration = TimeSpan.FromMilliseconds(200) };
        Storyboard.SetTarget(d, MigrateFormatDialog); Storyboard.SetTargetProperty(d, "Opacity"); sb.Children.Add(d);
        foreach (var (v, p) in new[] { (0.92, "ScaleX"), (0.92, "ScaleY"), (20.0, "TranslateY") })
        {
            var a = new DoubleAnimation { To = v, Duration = TimeSpan.FromMilliseconds(250), EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
            Storyboard.SetTarget(a, MigrateFormatDialogTransform); Storyboard.SetTargetProperty(a, p); sb.Children.Add(a);
        }
        sb.Completed += (_, _) => MigrateFormatOverlay.Visibility = Visibility.Collapsed;
        sb.Begin();
    }

    private void MigrateLater_Click(object sender, RoutedEventArgs e)
    {
        
        
        if (App.Store?.Database.AppSettings is { } s)
        {
            s.GcmMigrationRejected = true;
            App.Store.SaveAsync();
        }
        HideMigrateFormatDialog();
    }

    private void MigrateConfirm_Click(object sender, RoutedEventArgs e)
    {
        if (_migrateFailed) { HideMigrateFormatDialog(); return; }
        if (App.Store?.MigrateFormat() == true)
        {
            HideMigrateFormatDialog();
            _settingsPage?.UpdatePrivacyLockUI(); // 4.7: the manual entry disappears immediately after the upgrade
            return;
        }
        // Migration failed: keep the dialog open in a failure state - data untouched, retried next launch.
        _migrateFailed = true;
        MigrateFormatTitle.Text = App.GetString("Security_Migrate_Title");
        MigrateFormatMessage.Text = App.GetString("Security_Migrate_Failed");
        MigrateFormatMessage.Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(0xFF, 0xFF, 0x45, 0x45));
        MigrateLaterButton.Visibility = Visibility.Collapsed;
        MigrateConfirmText.Text = App.GetString("Common_Button_GotIt");
    }

    private void UpdateWelcomeLayout(double w, double h)
    {
        if (h <= 0) return;
        double scale = Math.Clamp(h / 800.0, 0.85, 1.6);
        WelcomeLogo.Width = WelcomeLogo.Height = 80 * scale;
        WelcomeTitleText.FontSize = WelcomeTitleShadowText.FontSize = 80 * scale;
        WelcomeSubtitle.FontSize = 34 * scale;
        WelcomeHint.FontSize = 14 * scale;

        WelcomeLogoTransform.Y = -0.2875 * h;
        WelcomeTitleTransform.Y = -0.075 * h + 2;
        WelcomeSubtitleTransform.Y = 0.1125 * h;
        WelcomeHintTransform.Y = 0.43 * h;
    }

    private void ApplyStartupTheme()
    {
        var theme = App.CurrentTheme;
        if (this.Content is FrameworkElement root)
        {
            root.RequestedTheme = theme switch
            {
                "深色模式" => ElementTheme.Dark,
                "浅色模式" => ElementTheme.Light,
                _ => ElementTheme.Default
            };
        }
    }

    private void InitBottomToolbarIcon()
    {
        var cv = Microsoft.UI.Xaml.Markup.XamlBindingHelper.ConvertValue;
        MenuPathIcon.Data = (Geometry)cv(typeof(Geometry), IconData.Menu);
    }

    
    
    
    

    private void MenuHoverZone_PointerEntered(object sender, PointerRoutedEventArgs e) { _hoverZoneHover = true; ExpandMenu(); }
    private void MenuButton_PointerEntered(object sender, PointerRoutedEventArgs e) { _menuBtnHover = true; ExpandMenu(); }

    private void MenuHoverZone_PointerExited(object sender, PointerRoutedEventArgs e) { _hoverZoneHover = false; OnMenuZonePointerExited(); }
    private void MenuButton_PointerExited(object sender, PointerRoutedEventArgs e) { _menuBtnHover = false; OnMenuZonePointerExited(); }

    private void OnMenuZonePointerExited()
    {
        if (!_menuExpanded) return;
        // Defer one tick: when the pointer leaves one element it may immediately enter the other
        // (hover zone <-> button). Track the pair of hover flags instead of reading GetCurrentPoint
        // (unreliable at the exit boundary) so the collapse only fires once BOTH are clear.
        DispatcherQueue.TryEnqueue(() =>
        {
            if (!_menuExpanded) return;
            if (_menuOpen) return; // menu open -> the button stays locked (user picked a menu item)
            if (!_hoverZoneHover && !_menuBtnHover)
                StartAutoCollapse();
        });
    }

    private void ExpandMenu()
    {
        if (_menuExpanded) return;
        _menuExpanded = true;
        StartMenuTransition(1.0);
    }

    private void CollapseMenu()
    {
        if (!_menuExpanded) return;
        _menuExpanded = false;
        StartMenuTransition(0.0);
    }

    private void StartMenuTransition(double targetProgress)
    {
        _menuAnimFrom = _menuProgress;
        _menuAnimTo = targetProgress;
        _menuAnimWatch = System.Diagnostics.Stopwatch.StartNew();
        _menuAnimating = true;
    }

    /// <summary>Map progress p (0 = vertical bar, 1 = hamburger) onto width/height/corner/icon opacity.</summary>
    private void ApplyMenuProgress(double p)
    {
        MenuButton.Width = MenuBarW + (MenuBtnW - MenuBarW) * p;
        MenuButton.Height = MenuBarH + (MenuBtnH - MenuBarH) * p;
        MenuButton.CornerRadius = new CornerRadius(MenuBarR + (MenuBtnR - MenuBarR) * p);
        MenuPathIcon.Opacity = p;
    }

    /// <summary>Single frame loop for the hamburger (subscribed once in OnWindowLoaded):
    /// drives the "melt" transition at 60fps and the collapsed vertical-bar breathing. No Storyboard (crash red line).</summary>
    private void OnMenuRendering(object? sender, object e)
    {
        // 1) Melt transition: width/height/corner/icon in lockstep ease-out (~260ms).
        if (_menuAnimating)
        {
            double elapsed = _menuAnimWatch?.Elapsed.TotalMilliseconds ?? 1000;
            double t = Math.Clamp(elapsed / MenuTransitionMs, 0.0, 1.0);
            double eased = 1 - Math.Pow(1 - t, 3);
            _menuProgress = _menuAnimFrom + (_menuAnimTo - _menuAnimFrom) * eased;
            ApplyMenuProgress(_menuProgress);
            MenuButtonTranslate.X = MenuAnchorX; // melt keeps the right edge anchored
            if (t >= 1.0)
            {
                _menuProgress = _menuAnimTo;
                ApplyMenuProgress(_menuProgress);
                _menuAnimating = false;
                _menuAnimWatch?.Stop(); _menuAnimWatch = null;
                
                if (_menuAnimTo <= 0.001) _glowWatch.Restart();
            }
        }
        
        else if (!_menuExpanded && _menuProgress <= 0.001 && BottomToolbarPanel.Visibility == Visibility.Visible)
        {
            double phase = (_glowWatch.Elapsed.TotalMilliseconds % 3200.0) / 3200.0;
            double s = Math.Sin(phase * Math.PI * 2.0);
            MenuButtonTranslate.X = MenuAnchorX + s * MenuBreath; // ~9..15px from the window edge
        }
        else
        {
            MenuButtonTranslate.X = MenuAnchorX;
        }
    }

    private void StartAutoCollapse()
    {
        CollapseMenu(); 
    }

    private void MenuButton_Click(object sender, RoutedEventArgs e)
    {
        // Collapsed (vertical bar) state: a click just expands it into the hamburger (the hover zone
        // already handles reveal); the menu opens on a second click once fully expanded.
        if (!_menuExpanded) { ExpandMenu(); return; }

        var cv = Microsoft.UI.Xaml.Markup.XamlBindingHelper.ConvertValue;
        var menu = new MenuFlyout
        {
            MenuFlyoutPresenterStyle = (Style)Application.Current.Resources["GlassMenuFlyoutPresenterStyle"]
        };
        _menuOpen = true; // lock the button while the menu is open (menu item selection does not collapse it)
        menu.Closed += (_, _) =>
        {
            _menuOpen = false;
            DispatcherQueue.TryEnqueue(CollapseMenu);
        };

        // Search
        var searchGroup = new GeometryGroup();
        foreach (var p in IconData.Search)
            searchGroup.Children.Add((Geometry)cv(typeof(Geometry), p));
        var searchItem = new MenuFlyoutItem
        {
            Style = (Style)Application.Current.Resources["GlassMenuFlyoutItemStyle"],
            Text = App.GetString("Menu_Search"),
            Icon = new PathIcon { Data = searchGroup, Foreground = App.GetBrush("IconForegroundBrush") },
            KeyboardAcceleratorTextOverride = "Ctrl+K"
        };
        searchItem.Click += (_, _) => OpenSearchPage();
        menu.Items.Add(searchItem);

        // Trash
        var trashItem = new MenuFlyoutItem
        {
            Style = (Style)Application.Current.Resources["GlassMenuFlyoutItemStyle"],
            Text = App.GetString("Trash_Title"),
            Icon = new PathIcon { Data = (Geometry)cv(typeof(Geometry), IconData.Delete[0]), Foreground = App.GetBrush("IconForegroundBrush") },
            KeyboardAcceleratorTextOverride = "Ctrl+Shift+Backspace"
        };
        trashItem.Click += (_, _) => OpenTrashPage();
        menu.Items.Add(trashItem);

        // Settings
        var settingsGroup = new GeometryGroup();
        foreach (var p in IconData.Settings)
            settingsGroup.Children.Add((Geometry)cv(typeof(Geometry), p));
        var settingsItem = new MenuFlyoutItem
        {
            Style = (Style)Application.Current.Resources["GlassMenuFlyoutItemStyle"],
            Text = App.GetString("Setting_Title_Page"),
            Icon = new PathIcon { Data = settingsGroup, Foreground = App.GetBrush("IconForegroundBrush") },
            KeyboardAcceleratorTextOverride = "Ctrl+,"
        };
        settingsItem.Click += (_, _) => NavigateToSettings();
        menu.Items.Add(settingsItem);

        menu.ShowAt(MenuButton);
    }

    /// <summary>Open the recycle-bin page as a full-blank page (navbar/toolbar/title bar hidden).</summary>
    public void OpenTrashPage()
    {
        if (ReferenceEquals(RootFrame.Content, _trashPage)) return;
        _preTrashContent = RootFrame.Content;
        _trashPage ??= new TrashPage();
        TrashPage.CleanupExpired(); 
        _trashPage.Refresh(); // always rebuild from the database
        RootFrame.Content = _trashPage;
        _trashPage.StealFocus(); // P2-2: steal focus from the first focusable element (no black focus border)
        NavBar.Visibility = Visibility.Collapsed;
        BottomToolbarPanel.Visibility = Visibility.Collapsed;
        CustomTitleBar.Visibility = Visibility.Collapsed;
    }

    /// <summary>Leave the trash page and restore whatever was shown before.</summary>
    public void CloseTrashPage()
    {
        if (!ReferenceEquals(RootFrame.Content, _trashPage)) { _preTrashContent = null; return; }
        var prev = _preTrashContent;
        _preTrashContent = null;
        if (prev != null)
        {
            // Trash mutations (restore/delete-forever/clear-all) invalidate every cached tab page:
            // rebuild ALL of them so their LoadFromStore picks up the new data. The old code rebuilt
            // only the page being returned to - other cached instances stayed stale, and a later
            // PersistAll on such a stale page physically dropped a restored entry from the db (D13).
            if (_trashChanged)
            {
                _trashChanged = false;
                bool wasMemo = ReferenceEquals(prev, _memoPage), wasPlan = ReferenceEquals(prev, _planPage),
                     wasFile = ReferenceEquals(prev, _filePathPage), wasDiary = ReferenceEquals(prev, _diaryPage);
                _memoPage = new BasicMemoPage();
                _planPage = new PlanPage();
                _filePathPage = new FilePathPage();
                _diaryPage = new DiaryPage();
                if (wasMemo) prev = _memoPage;
                else if (wasPlan) prev = _planPage;
                else if (wasFile) prev = _filePathPage;
                else if (wasDiary) prev = _diaryPage;
            }
            RootFrame.Content = prev;
            RestoreChromeFor(prev);
        }
    }

    private void InitNavIcons()
    {
        var cv = Microsoft.UI.Xaml.Markup.XamlBindingHelper.ConvertValue;
        NavMemoPath.Data = (Geometry)cv(typeof(Geometry), IconData.NavMemo);
        NavDiaryPath.Data = (Geometry)cv(typeof(Geometry), IconData.NavDiary);
        NavPlanPath.Data = (Geometry)cv(typeof(Geometry), IconData.NavPlan);

        NavFilePath.Data = (Geometry)cv(typeof(Geometry), IconData.NavFile);
        NavDiaryFilterIcon.Data = (Geometry)cv(typeof(Geometry), IconData.CardExpand);
        NavPlanFilterIcon.Data = (Geometry)cv(typeof(Geometry), IconData.CardExpand);
    }

    private void HookNavHoverEvents()
    {
        // E1-25: resolve the brushes per event - cached instances went stale when the theme switched
        // after unlock (lock screen renders in the system theme, then SetTheme swaps the dictionaries).
        
        // touch the selected button's Foreground, or the white turns black (AppTextPrimaryBrush is
        // near-black in light theme). Only unselected buttons brighten on hover.
        foreach (var btn in new[] { NavMemo, NavFile, NavPlan, NavDiary })
        {
            btn.PointerEntered += (_, _) =>
            {
                if (btn != _selectedNavButton) btn.Foreground = App.GetBrush("AppTextPrimaryBrush");
            };
            btn.PointerExited += (_, _) =>
            {
                if (btn != _selectedNavButton) btn.Foreground = App.GetBrush("AppTextTertiaryBrush");
            };
        }
    }

    public void ApplyVisibleTabs(HashSet<string> visibleTabs)
    {
        // N4W-01: Plan/Diary buttons live inside wrapper Grids (they also host the filter-arrow button).
        // The old code set Grid.Column on the inner BUTTON - a no-op inside the wrapper's single column -
        
        // stacked both wrappers into the same trailing column (two buttons in one cell, one unclickable).
        // Re-column the top-level child instead, and keep the inner button's Visibility in sync because
        // UpdateNavBarVisibility counts the buttons.
        var visibleItems = new List<FrameworkElement>(4);
        foreach (var (btn, host) in new[] { (NavMemo, (FrameworkElement?)null), (NavFile, null), (NavPlan, (FrameworkElement?)NavPlanHost), (NavDiary, (FrameworkElement?)NavDiaryHost) })
        {
            bool isVisible = visibleTabs.Contains(TagToTabName((string)btn.Tag));
            btn.Visibility = isVisible ? Visibility.Visible : Visibility.Collapsed;
            var item = host ?? (FrameworkElement)btn;
            if (host != null) host.Visibility = btn.Visibility;
            if (isVisible) visibleItems.Add(item);
        }

        NavBar.ColumnDefinitions.Clear();
        for (int i = 0; i < visibleItems.Count; i++)
        {
            NavBar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            Grid.SetColumn(visibleItems[i], i);
        }
        UpdateNavBarVisibility();
    }

    private static string TagToTabName(string tag) => tag switch
    {
        "Memo" => "备忘",
        "File" => "文件",
        "Plan" => "计划",
        "Diary" => "日记",
        _ => string.Empty
    };

    private void ShowMainContent()
    {
        BottomToolbarPanel.Visibility = Visibility.Visible;
        CustomTitleBar.Visibility = Visibility.Visible;
        NavBar.Visibility = Visibility.Visible;
        // E4-08: G6 - land on the first visible tab; a hidden "Memo" must not strand the user on a page with no nav entry.
        var (tab, navButton) = FirstVisibleTab();
        NavigateToPage(tab);
        UpdateNavSelection(navButton);
        UpdateNavBarVisibility(); // G7: single visible tab hides the whole bar (welcome-click path must not force-show it)
    }

    private (string Tab, Button Nav) FirstVisibleTab()
    {
        if (App.Store?.Database.AppSettings.VisibleTabs is { Count: > 0 } vt)
        {
            var map = new[] { ("Memo", "备忘", NavMemo), ("File", "文件", NavFile), ("Plan", "计划", NavPlan), ("Diary", "日记", NavDiary) };
            foreach (var (tag, name, btn) in map)
                if (vt.Contains(name)) return (tag, btn);
        }
        return ("Memo", NavMemo);
    }

    /// <summary>E5-05: resolve a requested tab to (tab, nav button), falling back to the first visible
    /// tab when the target is hidden by VisibleTabs (G6-class: never land on a page with no nav entry).</summary>
    private (string Tab, Button Nav) ResolveNavTarget(string requestedTag)
    {
        if (App.Store?.Database.AppSettings.VisibleTabs is { Count: > 0 } vt)
        {
            string name = TagToTabName(requestedTag);
            if (!vt.Contains(name)) return FirstVisibleTab();
        }
        return requestedTag switch
        {
            "Memo" => ("Memo", NavMemo),
            "File" => ("File", NavFile),
            "Plan" => ("Plan", NavPlan),
            "Diary" => ("Diary", NavDiary),
            _ => FirstVisibleTab()
        };
    }

    /// <summary>N4W-03: whether the requested tab is visible under VisibleTabs. External actions
    /// (sticky-note edit / add-path / reminder dialogs / search jump) must not be delivered to a
    /// hidden tab's cached page instance - the user cannot see it, so the request would be silently lost.</summary>
    private bool IsTabVisible(string requestedTag)
    {
        if (App.Store?.Database.AppSettings.VisibleTabs is { Count: > 0 } vt)
            return vt.Contains(TagToTabName(requestedTag));
        return true;
    }

    /// <summary>N4W-03: bring the window up and tell the user why an external request was dropped.</summary>
    private void RefuseHiddenTabAction(string requestedTag)
    {
        ShowMainWindow();
        App.ShowToast(App.GetString("Common_Tab_Hidden_Toast"));
        System.Diagnostics.Debug.WriteLine($"N4W-03: action for hidden tab '{requestedTag}' dropped");
    }

    private void WelcomeOverlay_Tapped(object sender, Microsoft.UI.Xaml.Input.TappedRoutedEventArgs e)
    {
        // Encrypted startup: Store is not loaded until unlock; clicking before the lock screen covers
        // would navigate pages with an empty database (Bug 9: memo page stays blank). Ignore the click.
        if (App.Store is not { IsLoaded: true }) return;
        if (_welcomeFading) return; // E5-18: ignore taps during the fade-out (double animation / double HasCompletedWelcome write)
        _welcomeFading = true;
        WelcomeHintBreathing.Pause(); 

        var fadeOut = new Storyboard();
        var oa = new DoubleAnimation { To = 0, Duration = TimeSpan.FromMilliseconds(400) };
        Storyboard.SetTarget(oa, WelcomeOverlay);
        Storyboard.SetTargetProperty(oa, "Opacity");
        fadeOut.Children.Add(oa);
        fadeOut.Completed += (s, ev) =>
        {
            _welcomeFading = false; // E5-18: re-arm only after the fade fully completed
            WelcomeOverlay.Visibility = Visibility.Collapsed;

            if (App.Store != null)
            {
                App.Store.Database.AppSettings.HasCompletedWelcome = true;
                App.Store.SaveSync(); // E4-25: synchronous persist - a fast exit right after the welcome tap must not lose the flag
            }
            ShowMainContent();
            NavBar.Opacity = 0;
            var fadeIn = new Storyboard();
            var oi = new DoubleAnimation { To = 1, Duration = TimeSpan.FromMilliseconds(400), EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
            Storyboard.SetTarget(oi, NavBar);
            Storyboard.SetTargetProperty(oi, "Opacity");
            fadeIn.Children.Add(oi);
            fadeIn.Begin();
        };
        fadeOut.Begin();
    }

    private void NavButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.Tag is string tag)
        {
            NavigateToPage(tag);
            UpdateNavSelection(button);
            WelcomeOverlay.Visibility = Visibility.Collapsed;
        }
    }

    /// <summary>Edit-request from the desktop sticky note: switch to Plan tab and open the note editor.</summary>
    public void HandleEditRequest(Guid id)
    {
        if (App.Store is not { IsLoaded: true }) return; // E1-19: never navigate on an empty/unloaded store (encrypted cold start) - the lock screen covers this
        if (WelcomeOverlay.Visibility == Visibility.Visible) return; // E3-11: plaintext welcome page occludes the target page - the dialog would open unseen and be dropped when the welcome tap navigates away
        if (!IsTabVisible("Plan")) { RefuseHiddenTabAction("Plan"); return; } // N4W-03: delivering to a hidden tab's page instance loses the request silently
        if (RestorePendingBlocksNavigation()) { RefuseRestorePending(); return; } // N5S-02
        FlushEditorDirty(); // N4W-04: leaving the editor for the plan tab must not abandon unsaved content
        ShowMainWindow(); // restore from minimized (and raise to foreground) first
        var (editTab, editNav) = ResolveNavTarget("Plan"); // E5-05: fall back to first visible when Plan is hidden
        NavigateToPage(editTab);
        UpdateNavSelection(editNav);
        RestoreChromeFor(RootFrame.Content); // D15: external requests must land on a normal tab page with full chrome
        // On a cold start the PlanPage is created now and its data (cards) load asynchronously
        // in its Loaded handler - retry until the note card is found (timeout ~4s).
        RetryOpenEdit(id, 0);
    }

    private void RetryOpenEdit(Guid id, int attempt)
    {
        if (attempt > 20) return; // give up after ~4s
        // Desktop sticky "Edit" is shared by note and todo cards - try the note editor first,
        // then the todo editor (the host bridge only carries an id, not a kind).
        if (_planPage?.OpenEditNoteById(id) == true) return;
        if (_planPage?.OpenEditTodoById(id) == true) return;
        DispatcherQueue.TryEnqueue(async () =>
        {
            await System.Threading.Tasks.Task.Delay(200);
            RetryOpenEdit(id, attempt + 1);
        });
    }

    /// <summary>Add-path request from the Windows right-click menu: switch to the File tab and open the new-path dialog pre-filled.</summary>
    public void HandleAddPathRequest(string path)
    {
        if (App.Store is not { IsLoaded: true }) return; // E1-19: see HandleEditRequest
        if (WelcomeOverlay.Visibility == Visibility.Visible) return; // E3-11: see HandleEditRequest
        if (!IsTabVisible("File")) { RefuseHiddenTabAction("File"); return; } // N4W-03: see HandleEditRequest
        if (RestorePendingBlocksNavigation()) { RefuseRestorePending(); return; } // N5S-02
        FlushEditorDirty(); // N4W-04
        ShowMainWindow(); // restore from minimized (and raise to foreground) first
        var (pathTab, pathNav) = ResolveNavTarget("File"); // E5-05: fall back to first visible when File is hidden
        NavigateToPage(pathTab);
        UpdateNavSelection(pathNav);
        RestoreChromeFor(RootFrame.Content); // D15: see HandleEditRequest
        _filePathPage?.OpenNewPathDialogWith(path);
    }

    /// <summary>Reminder edit request from the desktop card: switch to the Plan tab and open the reminder dialog pre-filled.</summary>
    public void HandleReminderEditRequest(Novara.Services.PendingReminderEdit p)
    {
        if (App.Store is not { IsLoaded: true }) return; // E1-19: see HandleEditRequest
        if (WelcomeOverlay.Visibility == Visibility.Visible) return; // E3-11: see HandleEditRequest
        if (!IsTabVisible("Plan")) { RefuseHiddenTabAction("Plan"); return; } // N4W-03: see HandleEditRequest
        if (RestorePendingBlocksNavigation()) { RefuseRestorePending(); return; } // N5S-02
        FlushEditorDirty(); // N4W-04
        ShowMainWindow();
        var (remTab, remNav) = ResolveNavTarget("Plan"); // E5-05: fall back to first visible when Plan is hidden
        NavigateToPage(remTab);
        UpdateNavSelection(remNav);
        RestoreChromeFor(RootFrame.Content); // D15: see HandleEditRequest
        _planPage?.OpenReminderEdit(p);
    }

    
    public void HandleReminderDueRequest(Guid id)
    {
        if (App.Store is not { IsLoaded: true }) return; // E1-19: see HandleEditRequest
        if (WelcomeOverlay.Visibility == Visibility.Visible) return; // E3-11: see HandleEditRequest
        if (!IsTabVisible("Plan")) { RefuseHiddenTabAction("Plan"); return; } // N4W-03: see HandleEditRequest
        if (RestorePendingBlocksNavigation()) { RefuseRestorePending(); return; } // N5S-02
        FlushEditorDirty(); // N4W-04
        ShowMainWindow();
        var (planTab, planNav) = ResolveNavTarget("Plan"); // E5-05: fall back to first visible when Plan is hidden
        NavigateToPage(planTab);
        UpdateNavSelection(planNav);
        RestoreChromeFor(RootFrame.Content);
        _planPage?.ShowReminderDueById(id);
    }

    /// <summary>N5S-02: while a restored snapshot awaits the restart barrier, any navigation away from
    /// the settings page tears the overlay down and re-opens suppressed write entries. Refuse instead.</summary>
    private bool RestorePendingBlocksNavigation()
        => App.Store?.IsSaveSuppressed == true;

    private void RefuseRestorePending()
    {
        ShowMainWindow();
        App.ShowToast(App.GetString("Common_Toast_RestorePending"));
    }

    /// <summary>N4W-04: the cached editor instance survives page swaps, but navigating away from it used
    /// to abandon unsaved content silently (only Back / window-close saved). Ctrl+K search and external
    /// IPC jumps flush first; SaveCurrentDiaryAsync's dirty check makes this a cheap no-op when clean.</summary>
    private void FlushEditorDirty()
    {
        if (RootFrame.Content is DiaryEditorPage ed)
        {
            try { _ = ed.SaveCurrentDiaryAsync(); } catch { }
        }
    }

    /// <summary>Open the global search page, remembering the current page for Esc / back.</summary>
    public void OpenSearchPage()
    {
        // D17: Ctrl+K must not open search while the lock screen or welcome overlay is covering
        // the window - the search page would be pushed into the occluded RootFrame (looks dead).
        if (LockScreenFrame.Visibility == Visibility.Visible || WelcomeOverlay.Visibility == Visibility.Visible) return;
        if (RestorePendingBlocksNavigation()) { RefuseRestorePending(); return; } // N5S-02: opening search would tear down the restore-restart barrier
        if (ReferenceEquals(RootFrame.Content, _searchPage)) { _searchPage?.FocusSearch(); return; }
        FlushEditorDirty(); // N4W-04
        _preSearchContent = RootFrame.Content;
        _searchPage ??= new SearchPage();
        _searchPage.ApplyTexts();
        _searchPage.ResetSearch(); // always re-enter blank - the instance is cached
        RootFrame.Content = _searchPage;
        // Full-blank page like settings: hide the navbar / settings button / custom title bar.
        NavBar.Visibility = Visibility.Collapsed;
        BottomToolbarPanel.Visibility = Visibility.Collapsed;
        CustomTitleBar.Visibility = Visibility.Collapsed;
        _searchPage.FocusSearch();
    }

    /// <summary>Leave the search page and restore whatever was shown before Ctrl+K.</summary>
    public void CloseSearchPage()
    {
        if (!ReferenceEquals(RootFrame.Content, _searchPage)) { _preSearchContent = null; return; }
        var prev = _preSearchContent;
        _preSearchContent = null;
        if (prev != null)
        {
            RootFrame.Content = prev;
            RestoreChromeFor(prev); // D14: unified restore - full-screen pages (incl. trash) keep their chrome hidden
        }
    }

    private void SyncNavFromContent(object content)
    {
        if (ReferenceEquals(content, _memoPage)) UpdateNavSelection(NavMemo);
        else if (ReferenceEquals(content, _filePathPage)) UpdateNavSelection(NavFile);
        else if (ReferenceEquals(content, _planPage)) UpdateNavSelection(NavPlan);
        else if (ReferenceEquals(content, _diaryPage)) UpdateNavSelection(NavDiary);
    }

    /// <summary>Full-screen pages hide the navbar / bottom toolbar / custom title bar (D13-D16).</summary>
    private static bool IsFullscreenPage(object? content)
        => content is SettingsPage or DiaryEditorPage or SearchPage or TrashPage;

    // N4R-12: IsTabPage removed - zero callers (E1-24 leftover).

    
    private bool CanInvokeGlobalShortcut()
    {
        if (LockScreenFrame.Visibility == Visibility.Visible) return false;
        if (WelcomeOverlay.Visibility == Visibility.Visible) return false;
        if (IsFullscreenPage(RootFrame.Content)) return false;
        
        
        return !HasTextInputFocus();
    }

    
    private bool HasTextInputFocus()
    {
        var focused = FocusManager.GetFocusedElement(Content.XamlRoot);
        return focused is TextBox or PasswordBox;
    }

    
    private void SwitchTab(string tag)
    {
        var (tab, nav) = ResolveNavTarget(tag);
        NavigateToPage(tab);
        UpdateNavSelection(nav);
        
        
        DispatcherQueue.TryEnqueue(StealFocusFromTabPage);
    }

    private void StealFocusFromTabPage()
    {
        switch (RootFrame.Content)
        {
            case BasicMemoPage m: m.StealFocus(); break;
            case FilePathPage f: f.StealFocus(); break;
            case PlanPage p: p.StealFocus(); break;
            case DiaryPage d: d.StealFocus(); break;
        }
    }

    /// <summary>Unified chrome restore for a page being shown again (D14/D16): tab pages get the
    /// full chrome, full-screen pages (settings/editor/search/trash) keep theirs hidden.</summary>
    private void RestoreChromeFor(object? page)
    {
        if (page == null) return;
        SyncNavFromContent(page);
        bool fullscreen = IsFullscreenPage(page);
        BottomToolbarPanel.Visibility = fullscreen ? Visibility.Collapsed : Visibility.Visible;
        CustomTitleBar.Visibility = fullscreen ? Visibility.Collapsed : Visibility.Visible;
        UpdateNavBarVisibility();
    }

    /// <summary>Jump from a search result to its page and open the edit dialog (3.0-4.5).</summary>
    public void SearchJump(string kind, string id)
    {
        // N5S-02: jumping navigates away from the settings page and tears the restore barrier down.
        if (RestorePendingBlocksNavigation()) { App.ShowToast(App.GetString("Common_Toast_RestorePending")); return; }
        // N4W-03: jumping to a VisibleTabs-hidden tab would land on the fallback page while the flash
        // targets the hidden instance (N4D-05-class invisibility) - refuse with feedback, keep search open.
        string tag = kind switch { "memo" => "Memo", "path" => "File", "todo" => "Plan", "note" => "Plan", "diary" => "Diary", _ => "" };
        if (tag != "" && !IsTabVisible(tag)) { App.ShowToast(App.GetString("Common_Tab_Hidden_Toast")); return; }
        CloseSearchPage();
        switch (kind)
        {
            case "memo":
                { var (t, n) = ResolveNavTarget("Memo"); NavigateToPage(t); UpdateNavSelection(n); } // E5-05: never land on a VisibleTabs-hidden page
                if (Guid.TryParse(id, out var mg)) RetryFlashBy(() => _memoPage?.FlashEntryById(mg) == true, 0);
                break;
            case "path":
                { var (t, n) = ResolveNavTarget("File"); NavigateToPage(t); UpdateNavSelection(n); }
                if (Guid.TryParse(id, out var pg)) RetryFlashBy(() => _filePathPage?.FlashPathById(pg) == true, 0);
                break;
            case "todo":
                { var (t, n) = ResolveNavTarget("Plan"); NavigateToPage(t); UpdateNavSelection(n); }
                if (Guid.TryParse(id, out var tg)) RetryFlashBy(() => _planPage?.FlashTodoById(tg) == true, 0);
                break;
            case "note":
                { var (t, n) = ResolveNavTarget("Plan"); NavigateToPage(t); UpdateNavSelection(n); }
                if (Guid.TryParse(id, out var ng)) RetryFlashBy(() => _planPage?.FlashNoteById(ng) == true, 0);
                break;
            case "diary":
                { var (t, n) = ResolveNavTarget("Diary"); NavigateToPage(t); UpdateNavSelection(n); }
                RetryFlashBy(() => _diaryPage?.FlashDiaryById(id) == true, 0);
                break;
        }
        
        
        RestoreChromeFor(RootFrame.Content);
    }

    /// <summary>
    /// Retry flashing the jump target. Every attempt (including the first) waits for the target
    /// page to finish its post-content-switch layout, so TransformToVisual/ChangeView never run
    /// while the page is mid-layout (that caused a UI-hang + XAML abort).
    /// </summary>
    private async void RetryFlashBy(Func<bool> flash, int attempt)
    {
        if (attempt > 20) return; // give up after ~6s
        await System.Threading.Tasks.Task.Delay(300);
        try
        {
            if (flash()) return;
        }
        catch { }
        RetryFlashBy(flash, attempt + 1);
    }

    private void NavigateToPage(string tag)
    {
        switch (tag)
        {
            case "Memo":
                _memoPage ??= new BasicMemoPage();
                RootFrame.Content = _memoPage;
                break;
            case "File":
                _filePathPage ??= new FilePathPage();
                RootFrame.Content = _filePathPage;
                break;
            case "Plan":
                _planPage ??= new PlanPage();
                RootFrame.Content = _planPage;
                break;
            case "Diary":
                _diaryPage ??= new DiaryPage();
                RootFrame.Content = _diaryPage;
                break;
        }
    }

    private void UpdateNavSelection(Button selected)
    {
        _selectedNavButton = selected;
        var dimBrush = App.GetBrush("AppTextTertiaryBrush");
        var whiteBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(0xFF, 0xFF, 0xFF, 0xFF));
        var selectedBg = App.GetBrush("AppPrimaryButtonBrush"); // brand blue
        var transparentBg = new SolidColorBrush(Windows.UI.Color.FromArgb(0, 0, 0, 0));
        var iconBrush = App.GetBrush("IconForegroundBrush");

        var navButtons = new[] { (NavMemo, NavMemoPath), (NavFile, NavFilePath), (NavPlan, NavPlanPath), (NavDiary, NavDiaryPath) };

        foreach (var (btn, icon) in navButtons)
        {
            bool isSelected = btn == selected;
            btn.Foreground = isSelected ? whiteBrush : dimBrush;
            btn.Background = isSelected ? selectedBg : transparentBg;
            btn.BorderBrush = transparentBg; // clean block when idle; template adds border on hover/press
            icon.Fill = isSelected ? whiteBrush : iconBrush;
        }

        
        
        bool diarySelected = selected == NavDiary;
        NavDiaryFilterButton.Visibility = diarySelected ? Visibility.Visible : Visibility.Collapsed;
        NavDiaryFilterIcon.Foreground = diarySelected ? whiteBrush : iconBrush;
        bool planSelected = selected == NavPlan;
        NavPlanFilterButton.Visibility = planSelected ? Visibility.Visible : Visibility.Collapsed;
        NavPlanFilterIcon.Foreground = planSelected ? whiteBrush : iconBrush;
    }

    private void NavDiaryFilterButton_Click(object sender, RoutedEventArgs e)
    {
        
        var menu = new MenuFlyout { MenuFlyoutPresenterStyle = (Style)Application.Current.Resources["GlassMenuFlyoutPresenterStyle"] };
        var activeBrush = App.GetBrush("AppTextPrimaryBrush");
        var normalBrush = App.GetBrush("AppTextSecondaryBrush");
        var iconBrush = App.GetBrush("IconForegroundBrush");

        MenuFlyoutItem MakeItem(string text, string filter, string iconPath)
        {
            bool active = (_diaryPage?.GetFilter() ?? "all") == filter;
            var item = new MenuFlyoutItem { Style = (Style)Application.Current.Resources["GlassMenuFlyoutItemStyle"], Text = text, Foreground = active ? activeBrush : normalBrush };
            item.Icon = active
                ? new FontIcon { Glyph = "\uE73E", FontSize = 12, Foreground = activeBrush }
                : new PathIcon { Data = App.CreateGeometry(iconPath), Foreground = iconBrush };
            item.Click += (_, _) => { _diaryPage?.SetFilter(filter); App.ShowToast(App.GetString("Common_Toast_Switched")); };
            return item;
        }

        menu.Items.Add(MakeItem(App.GetString("Diary_Filter_All"), "all", IconData.Mix));
        menu.Items.Add(MakeItem(App.GetString("Diary_Filter_Document"), "document", IconData.Document));
        menu.Items.Add(MakeItem(App.GetString("Diary_Filter_Diary"), "diary", IconData.NewDiary));
        menu.ShowAt(NavDiary, new Windows.Foundation.Point(0, NavDiary.ActualHeight + 4));
    }

    private void NavPlanFilterButton_Click(object sender, RoutedEventArgs e)
    {
        var menu = new MenuFlyout { MenuFlyoutPresenterStyle = (Style)Application.Current.Resources["GlassMenuFlyoutPresenterStyle"] };
        var activeBrush = App.GetBrush("AppTextPrimaryBrush");
        var normalBrush = App.GetBrush("AppTextSecondaryBrush");
        var iconBrush = App.GetBrush("IconForegroundBrush");

        MenuFlyoutItem MakeItem(string text, string filter, string iconPath)
        {
            bool active = (_planPage?.GetFilter() ?? "all") == filter;
            var item = new MenuFlyoutItem { Style = (Style)Application.Current.Resources["GlassMenuFlyoutItemStyle"], Text = text, Foreground = active ? activeBrush : normalBrush };
            item.Icon = active
                ? new FontIcon { Glyph = "\uE73E", FontSize = 12, Foreground = activeBrush }
                : new PathIcon { Data = App.CreateGeometry(iconPath), Foreground = iconBrush };
            item.Click += (_, _) => { _planPage?.SetFilter(filter); App.ShowToast(App.GetString("Common_Toast_Switched")); };
            return item;
        }

        menu.Items.Add(MakeItem(App.GetString("Diary_Filter_All"), "all", IconData.Mix));
        menu.Items.Add(MakeItem(App.GetString("Plan_Filter_Todo"), "todo", string.Join(" ", IconData.Todo)));
        menu.Items.Add(MakeItem(App.GetString("Plan_Filter_Note"), "note", string.Join(" ", IconData.Note)));
        menu.ShowAt(NavPlan, new Windows.Foundation.Point(0, NavPlan.ActualHeight + 4));
    }

    private void UpdateNavBarVisibility()
    {
        // G1: welcome overlay visible -> navbar hidden (bug fix: ApplyVisibleTabs on startup
        // called UpdateNavBarVisibility while the welcome page was showing, exposing the navbar)
        if (WelcomeOverlay.Visibility == Visibility.Visible)
        {
            NavBar.Visibility = Visibility.Collapsed;
            return;
        }

        if (RootFrame.Content is SettingsPage or DiaryEditorPage or SearchPage or TrashPage) // full-blank pages hide the navbar (D14: trash included)
        {
            NavBar.Visibility = Visibility.Collapsed;
            return;
        }
        int count = 0;
        foreach (var b in new[] { NavMemo, NavFile, NavPlan, NavDiary })
            if (b.Visibility == Visibility.Visible) count++;
        NavBar.Visibility = count <= 1 ? Visibility.Collapsed : Visibility.Visible;
    }

    public void ReloadPages()
    {
        _memoPage = null!;
        _filePathPage = null!;
        _planPage = null!;
        _diaryPage = null!;
        _diaryEditorPage?.Shutdown(); 
        _diaryEditorPage = null!; 
        _settingsPage = null!;
        _previousContent = null;
        // E4-24: drop the cached search/trash instances and their return points - after an import or
        // reset they reference pre-import pages (stale data); the nav below also rescues the user when
        // they are currently sitting on one of those full-screen pages.
        _searchPage = null;
        _trashPage = null;
        _preSearchContent = null;
        _preTrashContent = null;
        _trashChanged = false;
        if (RootFrame.Content is SettingsPage or DiaryEditorPage or SearchPage or TrashPage)
        {
            var (tab, nav) = ResolveNavTarget(_selectedNavButton?.Tag as string ?? "Memo"); // E5-06: fallback to first visible - the selected button may be hidden after VisibleTabs changed
            NavigateToPage(tab);
            UpdateNavSelection(nav);
            UpdateNavBarVisibility();
            BottomToolbarPanel.Visibility = Visibility.Visible;
            CustomTitleBar.Visibility = Visibility.Visible;
        }
    }

    public void UpsertDiary(DiaryEntry entry)
    {
        _diaryPage ??= new DiaryPage();
        _diaryPage.UpsertDiary(entry);
    }

    public void NavigateToEditor(DiaryEntry? diary = null, string? newFormat = null)
    {
        // 4.0: cache the editor page so WebView2 initializes only once (second entry is instant);
        // LoadDiary re-syncs the content + resets format state each time.
        _diaryEditorPage ??= new DiaryEditorPage();
        _diaryEditorPage.LoadDiary(diary, newFormat);

        var fadeOut = new Storyboard();
        var oa = new DoubleAnimation { To = 0, Duration = TimeSpan.FromMilliseconds(120), EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
        Storyboard.SetTarget(oa, RootFrame);
        Storyboard.SetTargetProperty(oa, "Opacity");
        fadeOut.Children.Add(oa);
        fadeOut.Completed += (s, e) =>
        {
            RootFrame.Content = _diaryEditorPage;
            NavBar.Visibility = Visibility.Collapsed;
            BottomToolbarPanel.Visibility = Visibility.Collapsed;
            CustomTitleBar.Visibility = Visibility.Collapsed;

            var fadeIn = new Storyboard();
            var oi = new DoubleAnimation { To = 1, Duration = TimeSpan.FromMilliseconds(200), EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
            Storyboard.SetTarget(oi, RootFrame);
            Storyboard.SetTargetProperty(oi, "Opacity");
            fadeIn.Children.Add(oi);
            fadeIn.Begin();
        };
        fadeOut.Begin();
    }

    public void NavigateBackFromEditor()
    {
        var fadeOut = new Storyboard();
        var oa = new DoubleAnimation { To = 0, Duration = TimeSpan.FromMilliseconds(120), EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
        Storyboard.SetTarget(oa, RootFrame);
        Storyboard.SetTargetProperty(oa, "Opacity");
        fadeOut.Children.Add(oa);
        fadeOut.Completed += (s, e) =>
        {
            _diaryPage ??= new DiaryPage();
            _diaryPage.RefreshDiaryList();
            RootFrame.Content = _diaryPage;
            // 4.0: keep the cached editor instance (WebView2 reuse) - LoadDiary re-syncs on next entry;
            // but clear its DOM + managed refs so a large-image diary doesn't hold Chromium memory while idle.

            UpdateNavBarVisibility();
            BottomToolbarPanel.Visibility = Visibility.Visible;
            CustomTitleBar.Visibility = Visibility.Visible;
            UpdateNavSelection(NavDiary);
            _diaryEditorPage?.ClearContent(); // 4.0(B): release the cached editor's DOM after switching away

            var fadeIn = new Storyboard();
            var oi = new DoubleAnimation { To = 1, Duration = TimeSpan.FromMilliseconds(200), EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
            Storyboard.SetTarget(oi, RootFrame);
            Storyboard.SetTargetProperty(oi, "Opacity");
            fadeIn.Children.Add(oi);
            fadeIn.Begin();
        };
        fadeOut.Begin();
    }

    private void NavigateToSettings()
    {
        _settingsPage ??= new SettingsPage();
        _previousContent = RootFrame.Content;
        
        
        BottomToolbarPanel.Visibility = Visibility.Collapsed;

        var fadeOut = new Storyboard();
        var oa = new DoubleAnimation { To = 0, Duration = TimeSpan.FromMilliseconds(120), EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
        Storyboard.SetTarget(oa, RootFrame);
        Storyboard.SetTargetProperty(oa, "Opacity");
        fadeOut.Children.Add(oa);
        fadeOut.Completed += (s, e) =>
        {
            RootFrame.Content = _settingsPage;
            _settingsPage.StealFocus(); // P2-2: steal focus from the first focusable element (no black focus border)
            NavBar.Visibility = Visibility.Collapsed;
            BottomToolbarPanel.Visibility = Visibility.Collapsed;
            CustomTitleBar.Visibility = Visibility.Collapsed;
            WelcomeOverlay.Visibility = Visibility.Collapsed;

            var fadeIn = new Storyboard();
            var oi = new DoubleAnimation { To = 1, Duration = TimeSpan.FromMilliseconds(200), EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
            Storyboard.SetTarget(oi, RootFrame);
            Storyboard.SetTargetProperty(oi, "Opacity");
            fadeIn.Children.Add(oi);
            fadeIn.Begin();
        };
        fadeOut.Begin();
    }

    public void NavigateBackFromSettings()
    {
        var fadeOut = new Storyboard();
        var oa = new DoubleAnimation { To = 0, Duration = TimeSpan.FromMilliseconds(120), EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
        Storyboard.SetTarget(oa, RootFrame);
        Storyboard.SetTargetProperty(oa, "Opacity");
        fadeOut.Children.Add(oa);
        fadeOut.Completed += (s, e) =>
        {

            Button? fallback = null;
            if (_selectedNavButton != null && _selectedNavButton.Visibility != Visibility.Visible)
            {
                foreach (var b in new[] { NavMemo, NavFile, NavPlan, NavDiary })
                {
                    if (b.Visibility == Visibility.Visible) { fallback = b; break; }
                }
            }
            if (fallback != null && fallback.Tag is string tag)
            {
                NavigateToPage(tag);
                UpdateNavSelection(fallback);
                _previousContent = RootFrame.Content;
            }
            else
            {
                RootFrame.Content = _previousContent;
            }

            // D16: chrome must match the restored page - settings opened from a full-screen page
            // (e.g. trash) returns to that page and must NOT restore the bottom toolbar/title bar.
            BottomToolbarPanel.Visibility = IsFullscreenPage(RootFrame.Content) ? Visibility.Collapsed : Visibility.Visible;
            CustomTitleBar.Visibility = IsFullscreenPage(RootFrame.Content) ? Visibility.Collapsed : Visibility.Visible;
            UpdateNavBarVisibility();
            WelcomeOverlay.Visibility = Visibility.Collapsed;

            var fadeIn = new Storyboard();
            var oi = new DoubleAnimation { To = 1, Duration = TimeSpan.FromMilliseconds(200), EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
            Storyboard.SetTarget(oi, RootFrame);
            Storyboard.SetTargetProperty(oi, "Opacity");
            fadeIn.Children.Add(oi);
            fadeIn.Begin();
        };
        fadeOut.Begin();
    }

    // ================================================================

    public void ShowCorruptStoreDialog()
    {
        _corruptDialogIsIoError = false; // E3-12
        StoreCorruptTitleText.Text = App.GetString("Store_Corrupt_Title");
        StoreCorruptDetailText.Text = App.GetString("Store_Corrupt_Detail");
        StoreCorruptConfirmText.Text = App.GetString("Store_Corrupt_RebuildButton");
        StoreCorruptConfirmButton.Background = new SolidColorBrush(Windows.UI.Color.FromArgb(0xCC, 0xFF, 0x45, 0x45));
        StoreCorruptConfirmButton.Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(0xFF, 0xFF, 0xFF, 0xFF));
        ShowCorruptDialogCore();
    }

    /// <summary>E3-12: transient IO failure (antivirus lock / disk full / permission) - retry only,
    /// NEVER offer the rebuild that would physically wipe a recoverable database.</summary>
    public void ShowIoErrorDialog()
    {
        _corruptDialogIsIoError = true;
        StoreCorruptTitleText.Text = App.GetString("Store_IoError_Title");
        StoreCorruptDetailText.Text = App.GetString("Store_Err_IoFail");
        StoreCorruptConfirmText.Text = App.GetString("Common_Ok");
        StoreCorruptConfirmButton.Background = App.GetBrush("AppSurfaceBrush");
        StoreCorruptConfirmButton.Foreground = App.GetBrush("AppTextPrimaryBrush");
        ShowCorruptDialogCore();
    }

    private void ShowCorruptDialogCore()
    {
        StoreCorruptDialogTransform.ScaleX = 0.92;
        StoreCorruptDialogTransform.ScaleY = 0.92;
        StoreCorruptDialogTransform.TranslateY = 20;
        StoreCorruptDialog.Opacity = 0;
        StoreCorruptOverlay.Visibility = Visibility.Visible;
        var sb = new Storyboard();
        var da = new DoubleAnimation { To = 1, Duration = TimeSpan.FromMilliseconds(300) };
        Storyboard.SetTarget(da, StoreCorruptDialog); Storyboard.SetTargetProperty(da, "Opacity");
        sb.Children.Add(da);
        var xa = new DoubleAnimation { To = 1, Duration = TimeSpan.FromMilliseconds(400), EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut } };
        Storyboard.SetTarget(xa, StoreCorruptDialogTransform); Storyboard.SetTargetProperty(xa, "ScaleX");
        sb.Children.Add(xa);
        var ya = new DoubleAnimation { To = 1, Duration = TimeSpan.FromMilliseconds(400), EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut } };
        Storyboard.SetTarget(ya, StoreCorruptDialogTransform); Storyboard.SetTargetProperty(ya, "ScaleY");
        sb.Children.Add(ya);
        var ty = new DoubleAnimation { To = 0, Duration = TimeSpan.FromMilliseconds(400), EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut } };
        Storyboard.SetTarget(ty, StoreCorruptDialogTransform); Storyboard.SetTargetProperty(ty, "TranslateY");
        sb.Children.Add(ty);
        sb.Begin();
    }

    private void HideCorruptStoreDialog()
    {
        if (_animCorruptHide) return; // D3 (Round 5): M2 hide re-entry guard
        _animCorruptHide = true;
        var sb = new Storyboard();
        var da = new DoubleAnimation { To = 0, Duration = TimeSpan.FromMilliseconds(200) };
        Storyboard.SetTarget(da, StoreCorruptDialog); Storyboard.SetTargetProperty(da, "Opacity");
        sb.Children.Add(da);
        var xa = new DoubleAnimation { To = 0.92, Duration = TimeSpan.FromMilliseconds(250), EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
        Storyboard.SetTarget(xa, StoreCorruptDialogTransform); Storyboard.SetTargetProperty(xa, "ScaleX");
        sb.Children.Add(xa);
        var ya = new DoubleAnimation { To = 0.92, Duration = TimeSpan.FromMilliseconds(250), EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
        Storyboard.SetTarget(ya, StoreCorruptDialogTransform); Storyboard.SetTargetProperty(ya, "ScaleY");
        sb.Children.Add(ya);
        var ty = new DoubleAnimation { To = 20, Duration = TimeSpan.FromMilliseconds(250), EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
        Storyboard.SetTarget(ty, StoreCorruptDialogTransform); Storyboard.SetTargetProperty(ty, "TranslateY");
        sb.Children.Add(ty);
        sb.Completed += (_, _) => { StoreCorruptOverlay.Visibility = Visibility.Collapsed; _animCorruptHide = false; };
        sb.Begin();
    }

    private void StoreCorruptConfirm_Click(object sender, RoutedEventArgs e)
    {
        if (_corruptDialogIsIoError) { HideCorruptStoreDialog(); return; } // E3-12: retry - the user closes the dialog and tries again
        // N5S-11: align with PerformReset/N4S-03 - reset FIRST, delete the password files only when the
        // empty db actually landed on disk. The old order (delete first, ignore result) could leave the
        // old encrypted db on disk with no password file if the reset failed.
        var reset = App.Store?.ResetDatabase();
        if (reset == null || reset.Status != LoadStatus.EmptyCreated)
        {
            HideCorruptStoreDialog(); // stay on the corrupt path - next startup re-enters this rebuild flow
            return;
        }
        try
        {
            Novara.Services.PasswordService.Delete();
            Novara.Services.PasswordService.DeleteLockout();
            Novara.Services.WindowsHelloService.Disable(); // 5.0: corrupted-DB rebuild clears the DB - drop the Hello credential
        }
        finally
        {
            HideCorruptStoreDialog();
            if (LockScreenFrame.Visibility == Visibility.Visible)
            {
                LockScreenFrame.Visibility = Visibility.Collapsed;
                LockScreenFrame.Content = null;
                WelcomeEntryAnimation.Begin();
            }
        }
    }

    private void ShowSaveFailedDialog()
    {
        _animSaveFailedHide = false; // R1 (Round 5): reset re-entry flag when new animation takes over (prevent stuck state)
        SaveFailedDialogTransform.ScaleX = 0.92;
        SaveFailedDialogTransform.ScaleY = 0.92;
        SaveFailedDialogTransform.TranslateY = 20;
        SaveFailedDialog.Opacity = 0;
        SaveFailedOverlay.Visibility = Visibility.Visible;
        var sb = new Storyboard();
        var da = new DoubleAnimation { To = 1, Duration = TimeSpan.FromMilliseconds(300) };
        Storyboard.SetTarget(da, SaveFailedDialog); Storyboard.SetTargetProperty(da, "Opacity");
        sb.Children.Add(da);
        var xa = new DoubleAnimation { To = 1, Duration = TimeSpan.FromMilliseconds(400), EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut } };
        Storyboard.SetTarget(xa, SaveFailedDialogTransform); Storyboard.SetTargetProperty(xa, "ScaleX");
        sb.Children.Add(xa);
        var ya = new DoubleAnimation { To = 1, Duration = TimeSpan.FromMilliseconds(400), EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut } };
        Storyboard.SetTarget(ya, SaveFailedDialogTransform); Storyboard.SetTargetProperty(ya, "ScaleY");
        sb.Children.Add(ya);
        var ty = new DoubleAnimation { To = 0, Duration = TimeSpan.FromMilliseconds(400), EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut } };
        Storyboard.SetTarget(ty, SaveFailedDialogTransform); Storyboard.SetTargetProperty(ty, "TranslateY");
        sb.Children.Add(ty);
        sb.Begin();
    }

    private void HideSaveFailedDialog()
    {
        if (_animSaveFailedHide) return; // D3 (Round 5): M2 hide re-entry guard
        _animSaveFailedHide = true;
        var sb = new Storyboard();
        var da = new DoubleAnimation { To = 0, Duration = TimeSpan.FromMilliseconds(200) };
        Storyboard.SetTarget(da, SaveFailedDialog); Storyboard.SetTargetProperty(da, "Opacity");
        sb.Children.Add(da);
        var xa = new DoubleAnimation { To = 0.92, Duration = TimeSpan.FromMilliseconds(250), EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
        Storyboard.SetTarget(xa, SaveFailedDialogTransform); Storyboard.SetTargetProperty(xa, "ScaleX");
        sb.Children.Add(xa);
        var ya = new DoubleAnimation { To = 0.92, Duration = TimeSpan.FromMilliseconds(250), EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
        Storyboard.SetTarget(ya, SaveFailedDialogTransform); Storyboard.SetTargetProperty(ya, "ScaleY");
        sb.Children.Add(ya);
        var ty = new DoubleAnimation { To = 20, Duration = TimeSpan.FromMilliseconds(250), EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
        Storyboard.SetTarget(ty, SaveFailedDialogTransform); Storyboard.SetTargetProperty(ty, "TranslateY");
        sb.Children.Add(ty);
        sb.Completed += (_, _) => { SaveFailedOverlay.Visibility = Visibility.Collapsed; _animSaveFailedHide = false; };
        sb.Begin();
    }

    private void SaveFailedConfirm_Click(object sender, RoutedEventArgs e) => HideSaveFailedDialog();

    /* ========== MainWindow Theme Restart ==========
Function: System-theme-change restart overlay: show/hide with animation, confirm re-entry guard (C3/R2/R3), lock-screen occlusion guard (#34)
Corresponding UI: MainWindow.xaml.cs
Logic Range: Below methods in this region
*/
private void ShowThemeRestartOverlay()
    {
        if (_themeRestartAnimating) return;
        _themeRestartAnimating = true;
        ThemeRestartScrim.Opacity = 0;
        ThemeRestartOverlay.Visibility = Visibility.Visible;
        var sb = new Storyboard();
        var sa = new DoubleAnimation { To = 1, Duration = TimeSpan.FromMilliseconds(250) };
        Storyboard.SetTarget(sa, ThemeRestartScrim); Storyboard.SetTargetProperty(sa, "Opacity");
        sb.Children.Add(sa);
        var da = new DoubleAnimation { To = 1, Duration = TimeSpan.FromMilliseconds(300) };
        Storyboard.SetTarget(da, ThemeRestartDialog); Storyboard.SetTargetProperty(da, "Opacity");
        sb.Children.Add(da);
        var xa = new DoubleAnimation { To = 1, Duration = TimeSpan.FromMilliseconds(400), EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut } };
        Storyboard.SetTarget(xa, ThemeRestartDialogTransform); Storyboard.SetTargetProperty(xa, "ScaleX");
        sb.Children.Add(xa);
        var ya = new DoubleAnimation { To = 1, Duration = TimeSpan.FromMilliseconds(400), EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut } };
        Storyboard.SetTarget(ya, ThemeRestartDialogTransform); Storyboard.SetTargetProperty(ya, "ScaleY");
        sb.Children.Add(ya);
        var ty = new DoubleAnimation { To = 0, Duration = TimeSpan.FromMilliseconds(400), EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut } };
        Storyboard.SetTarget(ty, ThemeRestartDialogTransform); Storyboard.SetTargetProperty(ty, "TranslateY");
        sb.Children.Add(ty);
        sb.Completed += (_, _) => _themeRestartAnimating = false;
        sb.Begin();
    }

    private void HideThemeRestartOverlay()
    {
        if (ThemeRestartOverlay.Visibility != Visibility.Visible) return;
        _themeRestartAnimating = true;
        var sb = new Storyboard();
        var sa = new DoubleAnimation { To = 0, Duration = TimeSpan.FromMilliseconds(200) };
        Storyboard.SetTarget(sa, ThemeRestartScrim); Storyboard.SetTargetProperty(sa, "Opacity");
        sb.Children.Add(sa);
        var da = new DoubleAnimation { To = 0, Duration = TimeSpan.FromMilliseconds(200) };
        Storyboard.SetTarget(da, ThemeRestartDialog); Storyboard.SetTargetProperty(da, "Opacity");
        sb.Children.Add(da);
        var xa = new DoubleAnimation { To = 0.92, Duration = TimeSpan.FromMilliseconds(250), EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
        Storyboard.SetTarget(xa, ThemeRestartDialogTransform); Storyboard.SetTargetProperty(xa, "ScaleX");
        sb.Children.Add(xa);
        var ya = new DoubleAnimation { To = 0.92, Duration = TimeSpan.FromMilliseconds(250), EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
        Storyboard.SetTarget(ya, ThemeRestartDialogTransform); Storyboard.SetTargetProperty(ya, "ScaleY");
        sb.Children.Add(ya);
        var ty = new DoubleAnimation { To = 20, Duration = TimeSpan.FromMilliseconds(250), EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
        Storyboard.SetTarget(ty, ThemeRestartDialogTransform); Storyboard.SetTargetProperty(ty, "TranslateY");
        sb.Children.Add(ty);
        sb.Completed += (_, _) => { ThemeRestartOverlay.Visibility = Visibility.Collapsed; _themeRestartAnimating = false; };
        sb.Begin();
    }

    private async void ThemeRestartConfirm_Click(object sender, RoutedEventArgs e)
    {
        if (_restarting) return; // C3 (Round 5): prevent duplicate process launch on rapid clicks
        _restarting = true;

        // N4W-02: capture unsaved editor content BEFORE the synchronous SaveSync - the new process reads
        // data.novadb right after launch, and Closing's editor save would only queue a debounced write
        // that dies with this process. The overlay stays visible during the (millisecond) flush so the
        // user cannot cancel mid-flow; if they somehow did, abort the restart below.
        if (RootFrame.Content is DiaryEditorPage pendingEditor)
        {
            try { await pendingEditor.SaveCurrentDiaryAsync(); } catch { }
            if (ThemeRestartOverlay.Visibility != Visibility.Visible) { _restarting = false; return; } // cancelled during flush
        }

        HideThemeRestartOverlay();

        // R3 (Round 5): align with SettingsPage - save first, then launch new process; on failure keep session retryable
        if (App.Store?.SaveSync() == false)
        {
            _restarting = false;
            return; // SaveFailed dialog already raised by event
        }

        try
        {
            var exe = Environment.ProcessPath;
            if (string.IsNullOrEmpty(exe)) { _restarting = false; return; } // R2 (Round 5): reset flag on empty path
            ReleaseSingleInstance();
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo { FileName = exe, UseShellExecute = true });
        }
        catch
        {
            // C3: launch failed - keep current session, retryable
            _restarting = false;
            return;
        }
        ExitApp(saveFirst: false); // E4-09: data already saved above - ExitApp must not abort on a redundant save failure (mutex is already released, new process is running)
    }

    private void ThemeRestartCancel_Click(object sender, RoutedEventArgs e)
    {
        HideThemeRestartOverlay();
    }

    private void ThemeRestartClose_Click(object sender, RoutedEventArgs e)
    {
        HideThemeRestartOverlay();
    }

    private void ThemeRestartScrim_Tapped(object sender, TappedRoutedEventArgs e)
    {
        HideThemeRestartOverlay();
    }

    

    private Action<bool>? _mcpAuthorizeResult;
    private bool _mcpAuthorizeAnimating;
    private Microsoft.UI.Xaml.DispatcherTimer? _mcpAuthorizeTimer; // N4C-01: auto-close an unanswered authorize dialog just before the server wait expires
    // NM1: single-slot callback + the 200ms hide-animation window used to drop/overwrite concurrent
    // first-time authorizations (a piped thread then blocks on its gate forever). Queue them instead.
    private readonly List<(string ClientPath, Action<bool> Result)> _mcpAuthorizeQueue = new();
    private const int McpAuthorizeQueueLimit = 8;

    private void QueueOrShowMcpAuthorize(string clientPath, Action<bool> onResult)
    {
        bool busy = _mcpAuthorizeResult != null || _mcpAuthorizeAnimating || McpAuthorizeOverlay.Visibility == Visibility.Visible;
        if (!busy) { ShowMcpAuthorizeDialog(clientPath, onResult); return; }
        if (_mcpAuthorizeQueue.Count >= McpAuthorizeQueueLimit) { onResult(false); return; } // overflow: deny instead of hanging the client
        _mcpAuthorizeQueue.Add((clientPath, onResult));
    }

    /// <summary>N4C-01: the server side default-denies after 2 minutes - leaving the dialog open made it a
    /// zombie (clicking it hit a disposed gate; new authorizations queued behind it starved). Close it at
    /// 115s without invoking the stale callback, then surface the next queued request.</summary>
    private void StartMcpAuthorizeExpiry()
    {
        _mcpAuthorizeTimer?.Stop();
        _mcpAuthorizeTimer = new Microsoft.UI.Xaml.DispatcherTimer { Interval = TimeSpan.FromSeconds(115) };
        _mcpAuthorizeTimer.Tick += (_, _) => CloseMcpAuthorizeDialog(invokeResult: false, allowed: false);
        _mcpAuthorizeTimer.Start();
    }

    private void StopMcpAuthorizeExpiry()
    {
        _mcpAuthorizeTimer?.Stop();
        _mcpAuthorizeTimer = null;
    }

    private void ShowMcpAuthorizeDialog(string clientPath, Action<bool> onResult)
    {
        _mcpAuthorizeResult = onResult;
        McpAuthorizePath.Text = clientPath;
        McpAuthorizePath.Visibility = string.IsNullOrEmpty(clientPath) ? Visibility.Collapsed : Visibility.Visible;
        McpAuthorizeDialogTransform.ScaleX = 0.92;
        McpAuthorizeDialogTransform.ScaleY = 0.92;
        McpAuthorizeDialogTransform.TranslateY = 20;
        McpAuthorizeDialog.Opacity = 0;
        McpAuthorizeOverlay.Visibility = Visibility.Visible;
        var sb = new Storyboard();
        var da = new DoubleAnimation { To = 1, Duration = TimeSpan.FromMilliseconds(300) };
        Storyboard.SetTarget(da, McpAuthorizeScrim); Storyboard.SetTargetProperty(da, "Opacity"); sb.Children.Add(da);
        var dda = new DoubleAnimation { To = 1, Duration = TimeSpan.FromMilliseconds(300) };
        Storyboard.SetTarget(dda, McpAuthorizeDialog); Storyboard.SetTargetProperty(dda, "Opacity"); sb.Children.Add(dda);
        foreach (var (v, p) in new[] { (1.0, "ScaleX"), (1.0, "ScaleY"), (0.0, "TranslateY") })
        {
            var a = new DoubleAnimation { To = v, Duration = TimeSpan.FromMilliseconds(400), EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
            Storyboard.SetTarget(a, McpAuthorizeDialogTransform); Storyboard.SetTargetProperty(a, p); sb.Children.Add(a);
        }
        sb.Begin();
        StartMcpAuthorizeExpiry(); // N4C-01
    }

    private void HideMcpAuthorizeDialog(bool allowed) => CloseMcpAuthorizeDialog(invokeResult: true, allowed);

    private void CloseMcpAuthorizeDialog(bool invokeResult, bool allowed)
    {
        StopMcpAuthorizeExpiry(); // N4C-01
        if (_mcpAuthorizeAnimating) return;
        _mcpAuthorizeAnimating = true;
        var result = _mcpAuthorizeResult;
        _mcpAuthorizeResult = null;
        var sb = new Storyboard();
        var so = new DoubleAnimation { To = 0, Duration = TimeSpan.FromMilliseconds(200) };
        Storyboard.SetTarget(so, McpAuthorizeScrim); Storyboard.SetTargetProperty(so, "Opacity"); sb.Children.Add(so);
        var d = new DoubleAnimation { To = 0, Duration = TimeSpan.FromMilliseconds(200) };
        Storyboard.SetTarget(d, McpAuthorizeDialog); Storyboard.SetTargetProperty(d, "Opacity"); sb.Children.Add(d);
        foreach (var (v, p) in new[] { (0.92, "ScaleX"), (0.92, "ScaleY"), (20.0, "TranslateY") })
        {
            var a = new DoubleAnimation { To = v, Duration = TimeSpan.FromMilliseconds(250), EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
            Storyboard.SetTarget(a, McpAuthorizeDialogTransform); Storyboard.SetTargetProperty(a, p); sb.Children.Add(a);
        }
        sb.Completed += (_, _) =>
        {
            McpAuthorizeOverlay.Visibility = Visibility.Collapsed; _mcpAuthorizeAnimating = false;
            if (_mcpAuthorizeQueue.Count > 0)
            {
                var next = _mcpAuthorizeQueue[0];
                _mcpAuthorizeQueue.RemoveAt(0);
                ShowMcpAuthorizeDialog(next.ClientPath, next.Result);
            }
        };
        sb.Begin();
        // N4C-01: the gate may already be disposed if the server wait timed out between the timer's
        // 115s UI close and a user click racing it - never let the click path crash on Set().
        if (invokeResult) { try { result?.Invoke(allowed); } catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"MCP authorize callback late invoke dropped: {ex.Message}"); } }
    }

    private void McpAuthorizeAllow_Click(object sender, RoutedEventArgs e) => HideMcpAuthorizeDialog(true);
    private void McpAuthorizeDeny_Click(object sender, RoutedEventArgs e) => HideMcpAuthorizeDialog(false);
}
