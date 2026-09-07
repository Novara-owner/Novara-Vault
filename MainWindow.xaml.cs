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
    private Microsoft.UI.Xaml.Media.ThemeShadow? _navShadow; 

    
    
    private sealed class NavFadeRec { public Microsoft.UI.Xaml.Media.Animation.Storyboard Sb = null!; public double Val; }
    private static readonly System.Collections.Generic.Dictionary<Microsoft.UI.Xaml.UIElement, NavFadeRec> _navFades = new();
    private object? _previousContent;
    private SearchPage? _searchPage; // global search (3.0-4.5)
    private object? _preSearchContent; // page shown before Ctrl+K opened the search page
    private TrashPage? _trashPage; // recycle bin (3.0-14)
    private object? _preTrashContent; // page shown before the trash button opened the trash page
    private bool _trashChanged; // trash mutations happened; rebuild the tab page on close
    // N2-01: MCP wrote new entries directly into the shared Database from a pipe thread - the
    // cached page instance missed them and its next PersistAll would drop them (P0). A stale page
    // instance is replaced wholesale on the user's next navigation to it (safe point: no dialogs
    // or drag state can be mid-flight on a page the user is not looking at; LoadFromStore is not
    // re-runnable so the instance itself must be swapped, same technique as ReloadPages).
    private bool _memoStale, _planStale, _fileStale, _diaryStale;

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
            var fo = new DoubleAnimation { To = 0, Duration = TimeSpan.FromMilliseconds(300), BeginTime = TimeSpan.FromMilliseconds(1500) }; // N2-43: F5 contract = toast gone in ~2s (0.2 in + 1.5 hold + 0.3 out); 3s hold broke it
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
        CarouselPageList = new[] { CarouselPage0, CarouselPage1, CarouselPage2, CarouselPage3 }; // 9.3: XAML fields are instance members - init in ctor
        BuildCarouselMenus(); // 9.3: carousel page 0 - real-rendered context menus (static content, language is restart-applied anyway)
        BuildCarouselPage1(); // 9.3: carousel page 1 - four NavBar-style tab buttons + per-page intro text
        BuildCarouselPage2(); // 9.3: carousel page 2 - hamburger handle auto-demo (cursor + melt mirror)
        BuildCarouselPage3(); // 9.3: carousel page 3 - privacy lock showcase (settings list + flowing border)

        // Window-level dialogs get shadows only - their scrims already cover the full window,
        // so the chrome veil must NOT stack on top (driveChrome:false).
        Services.DialogDepth.AttachContainer((Grid)Content, driveChrome: false);

        ApplyStartupTheme();

        this.SizeChanged += (_, e) => UpdateWelcomeLayout(e.Size.Width, e.Size.Height);
        this.SizeChanged += (_, _) => UpdateWorkspaceSwitcherPosition(); 

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
            ctrlK.Invoked += (_, _) => { if (!HasTextInputFocus() && LockScreenFrame.Visibility != Visibility.Visible) OpenSearchPage(); }; 
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
            AddHotkey((Windows.System.VirtualKey)188, Windows.System.VirtualKeyModifiers.Control, () => NavigateToSettings()); // VK_OEM_COMMA = Ctrl+,
            AddHotkey(Windows.System.VirtualKey.Back, Windows.System.VirtualKeyModifiers.Control | Windows.System.VirtualKeyModifiers.Shift, OpenTrashPage);

            // Defer all native/IO work to after Loaded: early native calls crash CLR on
            // Win10 19041 with .NET10 preview (0x80131506, CET compat defect)
            root.Loaded += OnWindowLoaded;
        }

        if (App.Store != null)
            App.Store.SaveFailed += OnStoreSaveFailed;

        ShowWindowCommand = new RelayCommand(ShowMainWindow);
        ExitCommand = new RelayCommand(ExitApp);
    }

    private void OnWindowLoaded(object sender, RoutedEventArgs e)
    {
        if (_windowLoadedHandled) return;
        _windowLoadedHandled = true;
        if (Content is FrameworkElement root) root.Loaded -= OnWindowLoaded;
        StartAutoLockWatcher(); 

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
        // 9.3 Quick Capture + Auto-Lock: system-wide hotkeys (Win32 RegisterHotKey on the main window).
        // Registered here for plaintext startup; re-registered after unlock (settings become readable only then).
        Services.GlobalHotkeyService.Initialize(this, OnQuickCaptureHotkey, OnLockNowHotkey);
        WireNavGlowRelocation(); 
        Services.GlobalHotkeyService.Register(Services.GlobalHotkeyService.IdLockNow,
            Services.GlobalHotkeyService.MOD_CONTROL | Services.GlobalHotkeyService.MOD_SHIFT, 0x4C); // Ctrl+Shift+L - LockNow guards itself
        ApplyQuickCaptureHotkey();
        
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
        // 9.2#5: one-shot legacy migration (pre-permission-center authorized paths -> full set, D2).
        // Must run before the server accepts any connection; persisted only when something moved.
        if (App.Store != null && McpPermissions.EnsureMigrated(App.Store.Database.AppSettings))
            _ = App.Store.SaveAsync();
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

        // Stage-3 md-import: consume a pending markdown import (cold start from the .md right-click menu).
        var pendingMd = App.PendingImportMd ?? Novara.Services.MdImportRequest.ReadPending();
        if (!string.IsNullOrWhiteSpace(pendingMd))
            DispatcherQueue.TryEnqueue(() => HandleImportMdRequest(pendingMd));

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
        UpdateWorkspaceSwitcherPosition(); 

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
        // N2-03: IoError must NOT share the corrupt dialog - the rebuild button would guide the user
        // into ResetDatabase on a HEALTHY db after a transient IO failure (AV/indexer/disk-full).
        
        if (loadResult?.Status == LoadStatus.Corrupted)
            ShowCorruptStoreDialog();
        else if (loadResult?.Status == LoadStatus.IoError)
            ShowIoErrorDialog(); // E3-12: transient read failure - retry only, NEVER offer rebuild

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
        menu.Items.Add(new MenuFlyoutItem { Text = App.GetString("Tray_LockNow"), Command = new Services.RelayCommand(() => { if (LockScreenFrame.Visibility == Visibility.Visible) return; 
            if (App.Store is { IsLoaded: true, IsEncrypted: true }) LockNow(); else App.ShowToast(App.GetString("Tray_LockNeedLock")); }) });
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

            _trayIcon.Dispose(); 
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
            try
            {
                
                
                var saveTask = editor.SaveCurrentDiaryAsync();
                await System.Threading.Tasks.Task.WhenAny(saveTask, System.Threading.Tasks.Task.Delay(3000));
            }
            catch { }
        }

        // E3-02: nothing is loaded yet (lock screen / encrypted cold start) - nothing to save, allow close.
        // SaveSync returns false on !_loaded, which would otherwise cancel the close forever.
        
        
        if (App.Store is { IsLoaded: false })
        {
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

    

    private string _qcMode = "todo"; // todo / note / memo - to-do is the most common capture
    private IntPtr _qcPrevForeground;
    private bool _qcAnimating;

    /// <summary>(Re)apply the Quick Capture hotkey from settings. Called on plaintext startup and after unlock.</summary>
    public void ApplyQuickCaptureHotkey()
    {
        Services.GlobalHotkeyService.Unregister(Services.GlobalHotkeyService.IdQuickCapture);
        var s = App.Store?.Database.AppSettings;
        if (s is not { QuickCaptureEnabled: true }) return;
        var (mods, vk) = Services.GlobalHotkeyService.ParsePreset(s.QuickCaptureHotkey);
        if (!Services.GlobalHotkeyService.Register(Services.GlobalHotkeyService.IdQuickCapture, mods, vk))
            App.ShowToast(App.GetString("QuickCapture_HotkeyConflict")); // combo owned by another app - say so and stay off
    }

    private void OnQuickCaptureHotkey()
    {
        // Locked database: never show the capture bar (nothing can be written and no side entrance) - just raise the lock screen.
        if (LockScreenFrame.Visibility == Visibility.Visible) { ShowMainWindow(); return; }
        ShowQuickCapture();
    }

    private void OnLockNowHotkey()
    {
        if (LockScreenFrame.Visibility == Visibility.Visible) return; // already locked
        LockNow(); // internally guards "no privacy lock" / double-fire
    }

    private void ShowQuickCapture()
    {
        if (QuickCaptureOverlay.Visibility == Visibility.Visible) { QuickCaptureBox.Focus(FocusState.Programmatic); QuickCaptureBox.SelectAll(); return; }
        _qcPrevForeground = GetForegroundWindow(); // hand focus back to the user's app when done
        ShowMainWindow(); // restore from tray / minimized + foreground
        QuickCaptureBox.Text = "";
        SetQcMode("todo");
        QuickCaptureHint.Text = App.GetString("QuickCapture_Hint");
        QuickCaptureOverlay.Visibility = Visibility.Visible;
        var sb = new Storyboard();
        var si = new DoubleAnimation { To = 1, Duration = TimeSpan.FromMilliseconds(250) }; Storyboard.SetTarget(si, QuickCaptureScrim); Storyboard.SetTargetProperty(si, "Opacity"); sb.Children.Add(si);
        var di = new DoubleAnimation { To = 1, Duration = TimeSpan.FromMilliseconds(300) }; Storyboard.SetTarget(di, QuickCaptureBar); Storyboard.SetTargetProperty(di, "Opacity"); sb.Children.Add(di);
        QuickCaptureTransform.ScaleX = 0.94; QuickCaptureTransform.ScaleY = 0.94; QuickCaptureTransform.TranslateY = 24; 
        Motion.AddDialogShowTransform(sb, QuickCaptureTransform); // M1: EmphasizedDecelerate (Motion)
        sb.Begin();
        QuickCaptureBox.Focus(FocusState.Programmatic);
    }

    private void HideQuickCapture()
    {
        if (_qcAnimating || QuickCaptureOverlay.Visibility != Visibility.Visible) return;
        _qcAnimating = true;
        var sb = new Storyboard();
        var so = new DoubleAnimation { To = 0, Duration = TimeSpan.FromMilliseconds(200) }; Storyboard.SetTarget(so, QuickCaptureScrim); Storyboard.SetTargetProperty(so, "Opacity"); sb.Children.Add(so);
        var d = new DoubleAnimation { To = 0, Duration = TimeSpan.FromMilliseconds(200) }; Storyboard.SetTarget(d, QuickCaptureBar); Storyboard.SetTargetProperty(d, "Opacity"); sb.Children.Add(d);
        Motion.AddDialogHideTransform(sb, QuickCaptureTransform); // M1: EmphasizedAccelerate (Motion)
        sb.Completed += (_, _) =>
        {
            QuickCaptureOverlay.Visibility = Visibility.Collapsed;
            _qcAnimating = false;
            if (_qcPrevForeground != IntPtr.Zero && _qcPrevForeground != WinRT.Interop.WindowNative.GetWindowHandle(this))
                SetForegroundWindow(_qcPrevForeground); // hand focus back to the user's app
        };
        sb.Begin();
    }

    private void SetQcMode(string mode)
    {
        _qcMode = mode;
        // Button captions (reused nav/stats wording); assigned here to keep the XAML free of x:Bind text
        QcModeMemo.Content = App.GetString("Nav_Tab_Memo");
        QcModeTodo.Content = App.GetString("Setting_Stats_Todos");
        QcModeNote.Content = App.GetString("Setting_Stats_Notes");
        // Selected state = brand-blue border + brand text (explicit values only - no runtime Style swap, MCP trap #2)
        var hot = App.GetBrush("AppPrimaryButtonBrush");
        var cool = App.GetBrush("AppBorderBrush");
        var coolText = App.GetBrush("AppTextSecondaryBrush");
        QcModeMemo.BorderBrush = mode == "memo" ? hot : cool; QcModeMemo.Foreground = mode == "memo" ? hot : coolText;
        QcModeTodo.BorderBrush = mode == "todo" ? hot : cool; QcModeTodo.Foreground = mode == "todo" ? hot : coolText;
        QcModeNote.BorderBrush = mode == "note" ? hot : cool; QcModeNote.Foreground = mode == "note" ? hot : coolText;
    }

    private void QuickCaptureBox_KeyDown(object sender, Microsoft.UI.Xaml.Input.KeyRoutedEventArgs e)
    {
        if (e.Key == Windows.System.VirtualKey.Enter)
        {
            e.Handled = true;
            if (_qcAnimating) return; 
            var t = QuickCaptureBox.Text.Trim();
            if (t.Length == 0) return; // M5: empty capture is a no-op
            switch (_qcMode)
            {
                case "memo": _memoPage ??= new BasicMemoPage(); _memoPage.QuickCaptureMemo(t); break;
                case "note": _planPage ??= new PlanPage(); _planPage.QuickCaptureNote(t); break;
                default: _planPage ??= new PlanPage(); _planPage.QuickCaptureTodo(t); break;
            }
            HideQuickCapture();
        }
        else if (e.Key == Windows.System.VirtualKey.Escape) { e.Handled = true; HideQuickCapture(); }
        else if (e.Key == Windows.System.VirtualKey.Tab)
        {
            e.Handled = true;
            SetQcMode(_qcMode switch { "todo" => "note", "note" => "memo", _ => "todo" });
            QuickCaptureBox.Focus(FocusState.Programmatic);
        }
    }

    private void QcModeButton_Click(object sender, RoutedEventArgs e)
    {
        if (ReferenceEquals(sender, QcModeMemo)) SetQcMode("memo");
        else if (ReferenceEquals(sender, QcModeNote)) SetQcMode("note");
        else SetQcMode("todo");
        QuickCaptureBox.Focus(FocusState.Programmatic);
    }

    private void QuickCaptureScrim_Tapped(object sender, TappedRoutedEventArgs e)
    {
        if (ReferenceEquals(e.OriginalSource, QuickCaptureScrim)) HideQuickCapture();
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
                try { await pendingEditor.SaveCurrentDiaryAsync().WaitAsync(TimeSpan.FromSeconds(3)); } catch { } 
            }
            if (saveFirst)
            {
                
                
                if (LockScreenFrame.Visibility == Visibility.Visible)
                {
                    _isTrayExit = true;
                }
                else
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
            }
            // N3-07: unregister hotkeys only on the actual-exit path. It used to run at method entry -
            // when the SaveSync below failed and the session was kept (C6), the Quick Capture and
            // Ctrl+Shift+L hotkeys stayed dead for the rest of the run (no re-registration path).
            // The X-close path (AppWindow_Closing) never unregisters early - same semantics here.
            Services.GlobalHotkeyService.UnregisterAll(); // 9.3: no hotkeys without a running process
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

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    
    
    
    
    [DllImport("user32.dll")]
    private static extern bool GetLastInputInfo(ref LASTINPUTINFO plii);
    [StructLayout(LayoutKind.Sequential)]
    private struct LASTINPUTINFO { public uint cbSize; public uint dwTime; }
    [DllImport("user32.dll")]
    private static extern IntPtr OpenInputDesktop(uint dwFlags, bool fInherit, uint dwDesiredAccess);
    [DllImport("user32.dll")]
    private static extern bool CloseDesktop(IntPtr hDesktop);

    private Microsoft.UI.Dispatching.DispatcherQueueTimer? _autoLockTimer;
    private int _lockStrikes; 

    private void StartAutoLockWatcher()
    {
        if (_autoLockTimer != null) return;
        _autoLockTimer = DispatcherQueue.CreateTimer();
        
        
        _autoLockTimer.Interval = TimeSpan.FromSeconds(1);
        _autoLockTimer.Tick += (_, _) => CheckAutoLock();
        _autoLockTimer.Start();
    }

    
    private void CheckAutoLock()
    {
        if (LockScreenFrame.Visibility == Visibility.Visible) return; 
        if (App.Store is not { IsLoaded: true, IsEncrypted: true }) return; 
        var st = App.Store.Database.AppSettings;
        
        
        
        if (st.AutoLockOnSystemLock)
        {
            _lockStrikes = IsInputDesktopUnavailable() ? _lockStrikes + 1 : 0;
            if (_lockStrikes >= 2) { _lockStrikes = 0; LockNow(); return; }
        }
        else if (_lockStrikes != 0) _lockStrikes = 0; 
        if (st.AutoLockSeconds > 0 && GetIdleSeconds() >= st.AutoLockSeconds) LockNow();
    }

    
    
    
    private static bool IsInputDesktopUnavailable()
    {
        var h = OpenInputDesktop(0, false, 0x0100); // DESKTOP_SWITCHDESKTOP
        if (h == IntPtr.Zero) return true;
        CloseDesktop(h);
        return false;
    }

    private static int GetIdleSeconds()
    {
        var info = new LASTINPUTINFO { cbSize = (uint)System.Runtime.InteropServices.Marshal.SizeOf<LASTINPUTINFO>() };
        if (!GetLastInputInfo(ref info)) return 0;
        
        
        var idleMs = unchecked((uint)Environment.TickCount - info.dwTime);
        return (int)(idleMs / 1000);
    }

    
    public void LockNow()
    {
        if (LockScreenFrame.Visibility == Visibility.Visible) return; 
        if (_lockInProgress) return; 
        if (App.Store is not { IsLoaded: true, IsEncrypted: true }) return; 
        _lockInProgress = true;
        _ = LockNowCoreAsync();
    }

    
    
    private void BindStoreSaveFailed()
    {
        if (App.Store is { } store)
        {
            store.SaveFailed -= OnStoreSaveFailed;
            store.SaveFailed += OnStoreSaveFailed;
        }
    }

    private void OnStoreSaveFailed(string message) => DispatcherQueue.TryEnqueue(ShowSaveFailedDialog);

    private bool _lockInProgress;

    
    
    
    private async Task LockNowCoreAsync()
    {
        try
        {
            if (RootFrame.Content is DiaryEditorPage ed)
            {
                try { await ed.SaveCurrentDiaryAsync().WaitAsync(TimeSpan.FromSeconds(3)); } catch { }
            }
            try { App.Store!.SaveSync(); } catch { }
            App.RelockStore(); 
            BindStoreSaveFailed(); 
            ShowLockScreen();
            
            
        }
        finally { _lockInProgress = false; }
    }

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
        ReloadPages(); 
        
        if (App.Store is { } store)
        {
            store.Database.AppSettings ??= new AppSettings();
            CloseBehavior = store.Database.AppSettings.CloseBehavior;
            App.ApplyStoredSettings();
            // N2-04: MCP legacy permission migration must re-run here. At startup it ran on the
            // EMPTY placeholder AppSettings (encrypted db not yet loaded, SaveAsync no-op'd on
            // !_loaded), and LoadWithPassword replaced the whole Database instance afterwards,
            // silently discarding the in-memory migration - pre-9.2#5 authorizations would hit
            
            if (McpPermissions.EnsureMigrated(store.Database.AppSettings))
                _ = store.SaveAsync();
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
        
        // 9.2#7: v2 databases on legacy 100k iterations get the symmetric KDF-hardening offer.
        if (App.Store is { NeedsFormatMigration: true } && !App.Store.Database.AppSettings.GcmMigrationRejected)
            ShowMigrateFormatDialog();
        else if (App.Store is { NeedsKdfMigration: true } && !App.Store.Database.AppSettings.KdfMigrationRejected)
            ShowMigrateFormatDialog();

        // E3-08b: the system theme may have changed while the lock screen covered the window (the
        // handler only remembers then) - dynamic controls hold stale brushes, so offer the restart
        // now. Shown after the migration dialog: if both appear, the user handles the top one first,
        // cancelling it reveals the other; a restart re-offers the migration on next launch anyway.
        if (_themeChangedWhileLocked) { _themeChangedWhileLocked = false; if (App.CurrentTheme == "跟随系统") ShowThemeRestartOverlay(); }

        // 9.3 Quick Capture: settings become readable only now (encrypted startup) - (re)apply the hotkey.
        ApplyQuickCaptureHotkey();
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
        EnsureChromeBlur(); 
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

    
    
    public void VeilHideImmediate()
    {
        _veilCount--;
        if (_veilCount > 0) return;
        _veilCount = 0;
        ChromeScrim.Visibility = Visibility.Collapsed;
        ChromeScrim.Opacity = 0;
    }

    
    public void VeilClear()
    {
        _veilCount = 0;
        ChromeScrim.Visibility = Visibility.Collapsed;
        ChromeScrim.Opacity = 0;
    }

    // ---- 4.7: one-time v1-CBC -> v2-GCM migration confirmation (after unlock) ----
    private bool _migrateFailed; // failure state: dialog switches to "retry on next launch"
    private bool _migrateIsKdf; // 9.2#7: the dialog currently shows the KDF-hardening (v2->v3) flavor

    public void ShowMigrateFormatDialog()
    {
        _migrateFailed = false;
        // 9.2#7: one dialog, two flavors - GCM (v1->v2) and KDF hardening (v2->v3), dispatched by
        // which migration is actually pending (settings entry re-opens it for the pending kind).
        _migrateIsKdf = App.Store is { NeedsKdfMigration: true };
        MigrateFormatMessage.Text = App.GetString(_migrateIsKdf ? "Security_Migrate_Kdf_Body" : "Security_Migrate_Body");
        MigrateFormatMessage.Foreground = App.GetBrush("AppTextSecondaryBrush"); // reset the red failure tint (KDF pipeline may re-open after a GCM failure tint)
        MigrateFormatTitle.Text = App.GetString(_migrateIsKdf ? "Security_Migrate_Kdf_Title" : "Security_Migrate_Title");
        MigrateLaterButton.Visibility = Visibility.Visible;
        MigrateConfirmText.Text = App.GetString("Security_Migrate_Confirm");
        MigrateFormatDialogTransform.ScaleX = 0.94; MigrateFormatDialogTransform.ScaleY = 0.94; MigrateFormatDialogTransform.TranslateY = 24;
        MigrateFormatDialog.Opacity = 0;
        MigrateFormatOverlay.Visibility = Visibility.Visible;
        var sb = new Storyboard();
        var da = new DoubleAnimation { To = 1, Duration = TimeSpan.FromMilliseconds(300) };
        Storyboard.SetTarget(da, MigrateFormatScrim); Storyboard.SetTargetProperty(da, "Opacity"); sb.Children.Add(da);
        var dda = new DoubleAnimation { To = 1, Duration = TimeSpan.FromMilliseconds(300) };
        Storyboard.SetTarget(dda, MigrateFormatDialog); Storyboard.SetTargetProperty(dda, "Opacity"); sb.Children.Add(dda);
        Motion.AddDialogShowTransform(sb, MigrateFormatDialogTransform); // M1: EmphasizedDecelerate (Motion)
        sb.Begin();
    }

    private void HideMigrateFormatDialog()
    {
        var sb = new Storyboard();
        var so = new DoubleAnimation { To = 0, Duration = TimeSpan.FromMilliseconds(200) };
        Storyboard.SetTarget(so, MigrateFormatScrim); Storyboard.SetTargetProperty(so, "Opacity"); sb.Children.Add(so);
        var d = new DoubleAnimation { To = 0, Duration = TimeSpan.FromMilliseconds(200) };
        Storyboard.SetTarget(d, MigrateFormatDialog); Storyboard.SetTargetProperty(d, "Opacity"); sb.Children.Add(d);
        Motion.AddDialogHideTransform(sb, MigrateFormatDialogTransform); // M1: EmphasizedAccelerate (Motion)
        sb.Completed += (_, _) => MigrateFormatOverlay.Visibility = Visibility.Collapsed;
        sb.Begin();
    }

    private void MigrateLater_Click(object sender, RoutedEventArgs e)
    {
        
        
        if (App.Store?.Database.AppSettings is { } s)
        {
            if (_migrateIsKdf) s.KdfMigrationRejected = true; else s.GcmMigrationRejected = true;
            App.Store.SaveAsync();
        }
        HideMigrateFormatDialog();
    }

    private void MigrateConfirm_Click(object sender, RoutedEventArgs e)
    {
        if (_migrateFailed) { HideMigrateFormatDialog(); return; }
        var ok = _migrateIsKdf ? App.Store?.MigrateKdf() == true : App.Store?.MigrateFormat() == true;
        if (ok)
        {
            _settingsPage?.UpdatePrivacyLockUI();
            // 9.2#7 pipeline: a v1 library just became v2 - if the KDF offer is still pending it is
            // shown right away (the user has just expressed the upgrade intent, not a double nag).
            if (!_migrateIsKdf && App.Store is { NeedsKdfMigration: true } && !App.Store.Database.AppSettings.KdfMigrationRejected)
            {
                ShowMigrateFormatDialog();
                return;
            }
            HideMigrateFormatDialog();
            return;
        }
        // Migration failed: keep the dialog open in a failure state - data untouched, retried next launch.
        _migrateFailed = true;
        var kdf = _migrateIsKdf;
        MigrateFormatTitle.Text = App.GetString(kdf ? "Security_Migrate_Kdf_Title" : "Security_Migrate_Title");
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
            Icon = new PathIcon { Data = (Geometry)cv(typeof(Geometry), IconData.Delete), Foreground = App.GetBrush("IconForegroundBrush") },
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
        var prevContent = RootFrame.Content;
        RootFrame.Content = _trashPage;
        PlayPageIn(prevContent); // M2: page-in transition
        _trashPage.StealFocus(); // P2-2: steal focus from the first focusable element (no black focus border)
        NavBar.Visibility = Visibility.Collapsed;
        BottomToolbarPanel.Visibility = Visibility.Collapsed;
        CustomTitleBar.Visibility = Visibility.Collapsed;
    }

    /// <summary>
    /// N2-02/D13: trash mutations (restore / delete-forever / clear-all) invalidate every cached
    /// tab page - rebuild all four so their LoadFromStore picks up the new data before any
    /// PersistAll can physically drop a restored (no-card, not-soft-deleted) entry from the db.
    /// Consumed by CloseTrashPage AND NavigateToPage (the SearchJump / palette / IPC bypass).
    /// </summary>
    private void InvalidateTrashStaleTabs()
    {
        if (!_trashChanged) return;
        _trashChanged = false;
        _memoPage = new BasicMemoPage();
        _planPage = new PlanPage();
        _filePathPage = new FilePathPage();
        _diaryPage = new DiaryPage();
        _memoStale = _planStale = _fileStale = _diaryStale = false; // fresh instances already contain the new data
    }

    /// <summary>
    /// N2-01: called by the MCP service after a successful direct write into the shared Database.
    /// Marks every already-created tab page stale; each instance is swapped for a fresh one when
    /// the user next navigates to it (see NavigateToPage). Safe to call from any thread.
    /// </summary>
    public void NotifyExternalDbMutation()
    {
        App.UiQueue?.TryEnqueue(() =>
        {
            _memoStale = _memoPage is not null;
            _planStale = _planPage is not null;
            _fileStale = _filePathPage is not null;
            _diaryStale = _diaryPage is not null;
        });
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
                bool wasMemo = ReferenceEquals(prev, _memoPage), wasPlan = ReferenceEquals(prev, _planPage),
                     wasFile = ReferenceEquals(prev, _filePathPage), wasDiary = ReferenceEquals(prev, _diaryPage);
                InvalidateTrashStaleTabs();
                if (wasMemo) prev = _memoPage;
                else if (wasPlan) prev = _planPage;
                else if (wasFile) prev = _filePathPage;
                else if (wasDiary) prev = _diaryPage;
            }
            RootFrame.Content = prev;
            PlayPageIn(_trashPage); // M2: page-in transition (previous = the trash page we are leaving)
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
        
        
        try
        {
            if (_navShadow == null)
            {
                _navShadow = new Microsoft.UI.Xaml.Media.ThemeShadow();
                _navShadow.Receivers.Add(NavShadowReceiver);
                foreach (var btn in new[] { NavMemo, NavFile, NavPlan, NavDiary })
                    btn.Shadow = _navShadow;
            }
        }
        catch { }
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
        _navIndicatorPlaced = false; 
        NavIndicator.Opacity = 0;
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

    

    private int _carouselPage;

    /// <summary>Show the promo carousel on true first launch; debug builds replay it every launch.</summary>
    private void MaybeShowWelcomeCarousel()
    {
#if DEBUG
        ShowWelcomeCarousel();
#else
        if (App.Store?.Database.AppSettings is { HasCompletedCarousel: false }) ShowWelcomeCarousel(); // N4-01: own flag - HasCompletedWelcome is already true by the time we get here (set right before this call in WelcomeOverlay_Tapped)
#endif
    }

    private void ShowWelcomeCarousel()
    {
        _carouselPage = 0;
        BuildCarouselDots();
        SetCarouselPage(0);
        EnsureCarouselBlur(); // owner-approved frosted backdrop: blur + tint while the carousel is up
        CarouselBlurHost.Visibility = Visibility.Visible;
        WelcomeCarousel.Visibility = Visibility.Visible;
        WelcomeCarousel.Opacity = 0;
        
        
        var contentH = (Content as FrameworkElement)?.ActualHeight ?? 0;
        var s = contentH > 120 ? Math.Max(0.5, Math.Min(1.0, (contentH - 80) / 465.0)) : 1.0;
        CarouselHost.RenderTransformOrigin = new Windows.Foundation.Point(0.5, 0.5);
        CarouselHost.RenderTransform = new Microsoft.UI.Xaml.Media.ScaleTransform { ScaleX = s, ScaleY = s };
        var sb = new Storyboard();
        var oi = new DoubleAnimation { To = 1, Duration = TimeSpan.FromMilliseconds(300) };
        Storyboard.SetTarget(oi, WelcomeCarousel); Storyboard.SetTargetProperty(oi, "Opacity"); sb.Children.Add(oi);
        sb.Begin();
        CarouselCard.Focus(FocusState.Programmatic);
        CarouselP0Title.Text = App.GetString("Carousel_P0_Title");
        StartCarouselFloat();
    }

    private void CloseWelcomeCarousel()
    {
        StopCarouselFloat();
        CarouselBlurHost.Visibility = Visibility.Collapsed; // blur visual goes with it - zero idle cost
        WelcomeCarousel.Opacity = 0;
        WelcomeCarousel.Visibility = Visibility.Collapsed;
        var s = App.Store?.Database.AppSettings;
        if (s != null && !s.HasCompletedCarousel)
        {
            s.HasCompletedCarousel = true; // played once - never again (N4-01: persisted under its own flag, welcome flag keeps its E4-25 tap-time write)
            _ = App.Store?.SaveAsync();
        }
    }

    private void CarouselClose_Click(object sender, RoutedEventArgs e) => CloseWelcomeCarousel();

    // ===================== 9.3 carousel page 0: real-rendered context menus =====================

    /// <summary>
    /// 9.3 Fill the four carousel menu shells with real-rendered menu items (owner call: native
    /// drawing instead of screenshots - theme-adaptive, visually identical to the real menus).
    /// Text keys / icon paths / metrics are exactly the ones the real context menus use.
    /// </summary>
    private void BuildCarouselMenus()
    {
        FillCarouselMenu(CarouselMenuMemo, new UIElement[]
        {
            MakeCarouselMenuRow("Menu_New_Group", IconData.NewGroup),
            MakeCarouselMenuRow("Menu_New_Entry", string.Join(" ", IconData.NewEntry)),
        });
        FillCarouselMenu(CarouselMenuPath, new UIElement[]
        {
            MakeCarouselMenuRow("Menu_New_Path", IconData.NavFile),
            MakeCarouselMenuRow("Menu_DetectPath", IconData.RefreshPaths),
        });
        FillCarouselMenu(CarouselMenuPlan, new UIElement[]
        {
            MakeCarouselMenuRow("Menu_New_Todo", string.Join(" ", IconData.Todo)),
            MakeCarouselMenuRow("Menu_New_Note", string.Join(" ", IconData.Note)),
            MakeCarouselMenuRow("Menu_AddReminder", IconData.Reminder),
            MakeCarouselMenuSeparator(),
            MakeCarouselMenuRow("Menu_CloseAllDesktop", IconData.CancelDesktop),
        });
        FillCarouselMenu(CarouselMenuDiary, new UIElement[]
        {
            MakeCarouselMenuRow("Diary_New_Document", IconData.Document),
            MakeCarouselMenuRow("Menu_New_Diary", IconData.NewDiary),
            MakeCarouselMenuRow("Diary_Import_Document", IconData.Import),
        });
    }

    private static void FillCarouselMenu(Border shell, UIElement[] rows)
    {
        var stack = new StackPanel();
        foreach (var row in rows) stack.Children.Add(row);
        shell.Child = stack;
    }

    // One menu row = Viewbox(16x16) wrapping PathIcon + 14px text - the GlassMenuFlyoutItemStyle template layout.
    private static StackPanel MakeCarouselMenuRow(string textKey, string pathData)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Padding = new Thickness(12, 9, 12, 9) };
        var icon = new PathIcon
        {
            Data = App.CreateGeometry(pathData),
            Foreground = App.GetBrush("IconForegroundBrush"),
        };
        row.Children.Add(new Viewbox
        {
            Width = 16,
            Height = 16,
            Stretch = Stretch.Uniform,
            Margin = new Thickness(0, 0, 8, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Child = icon,
        });
        row.Children.Add(new TextBlock
        {
            Text = App.GetString(textKey),
            FontSize = 14,
            Foreground = App.GetBrush("AppTextPrimaryBrush"),
            VerticalAlignment = VerticalAlignment.Center,
        });
        return row;
    }

    // Stand-in for the default MenuFlyoutSeparator (1px line, inset from both sides).
    private static Border MakeCarouselMenuSeparator()
        => new() { Height = 1, Background = App.GetBrush("AppBorderBrush"), Margin = new Thickness(4, 5, 4, 5) };

    // 9.3 Gentle floating loop (owner idea): CompositionTarget.Rendering drives TranslateTransform.Y
    // with a sine wave - frame-synced with the display refresh (DispatcherTimer ticked at only ~30fps,
    // owner feedback), no Storyboard on layout properties (3.5 red line), pure render-transform updates.
    // Elapsed time comes from Stopwatch so the phase stays correct regardless of frame rate.
    private double _carouselFloatStart; // Stopwatch seconds when the loop (re)started

    private Microsoft.UI.Composition.SpriteVisual? _carouselBlurVisual; // frosted backdrop, created lazily on first show

    /// <summary>
    /// 9.3 Owner-approved frosted backdrop: everything behind the carousel is blurred (Composition
    /// BackdropBrush -> Win2D GaussianBlurEffect) and a low-opacity theme tint sits on top (XAML
    /// CarouselTint). Created once; the host is Collapsed when the carousel closes, so the effect
    /// costs nothing while idle.
    /// </summary>
    private void EnsureCarouselBlur()
    {
        if (_carouselBlurVisual != null) return;
        try
        {
            var compositor = Microsoft.UI.Xaml.Hosting.ElementCompositionPreview.GetElementVisual(CarouselBlurHost).Compositor;
            var blur = new Microsoft.Graphics.Canvas.Effects.GaussianBlurEffect
            {
                Name = "Blur",
                BlurAmount = 6f, 
                BorderMode = Microsoft.Graphics.Canvas.Effects.EffectBorderMode.Hard,
                Optimization = Microsoft.Graphics.Canvas.Effects.EffectOptimization.Balanced,
                Source = new Microsoft.UI.Composition.CompositionEffectSourceParameter("Backdrop"),
            };
            var factory = compositor.CreateEffectFactory(blur);
            var brush = factory.CreateBrush();
            brush.SetSourceParameter("Backdrop", compositor.CreateBackdropBrush());
            _carouselBlurVisual = compositor.CreateSpriteVisual();
            _carouselBlurVisual.Brush = brush;
            _carouselBlurVisual.Size = new System.Numerics.Vector2((float)CarouselBlurHost.ActualWidth, (float)CarouselBlurHost.ActualHeight);
            CarouselBlurHost.SizeChanged += (_, e) =>
            {
                if (_carouselBlurVisual != null)
                    _carouselBlurVisual.Size = new System.Numerics.Vector2((float)e.NewSize.Width, (float)e.NewSize.Height);
            };
            Microsoft.UI.Xaml.Hosting.ElementCompositionPreview.SetElementChildVisual(CarouselBlurHost, _carouselBlurVisual);
        }
        catch (System.Exception ex)
        {
            // blur is decorative - a failure (e.g. Win2D native missing) must not break the carousel
            System.Diagnostics.Debug.WriteLine($"carousel blur init failed: {ex.Message}");
            _carouselBlurVisual = null;
        }
    }

    
    
    private Microsoft.UI.Composition.SpriteVisual? _chromeBlurVisual;
    private void EnsureChromeBlur()
    {
        if (_chromeBlurVisual != null) return;
        try
        {
            var compositor = Microsoft.UI.Xaml.Hosting.ElementCompositionPreview.GetElementVisual(ChromeBlurHost).Compositor;
            var blur = new Microsoft.Graphics.Canvas.Effects.GaussianBlurEffect
            {
                Name = "Blur",
                BlurAmount = 6f,
                BorderMode = Microsoft.Graphics.Canvas.Effects.EffectBorderMode.Soft, 
                Optimization = Microsoft.Graphics.Canvas.Effects.EffectOptimization.Balanced,
                Source = new Microsoft.UI.Composition.CompositionEffectSourceParameter("Backdrop"),
            };
            var brush = compositor.CreateEffectFactory(blur).CreateBrush();
            brush.SetSourceParameter("Backdrop", compositor.CreateBackdropBrush());
            _chromeBlurVisual = compositor.CreateSpriteVisual();
            _chromeBlurVisual.Brush = brush;
            _chromeBlurVisual.Size = new System.Numerics.Vector2((float)ChromeBlurHost.ActualWidth, (float)ChromeBlurHost.ActualHeight);
            ChromeBlurHost.SizeChanged += (_, e) =>
            {
                if (_chromeBlurVisual != null)
                    _chromeBlurVisual.Size = new System.Numerics.Vector2((float)e.NewSize.Width, (float)e.NewSize.Height);
            };
            Microsoft.UI.Xaml.Hosting.ElementCompositionPreview.SetElementChildVisual(ChromeBlurHost, _chromeBlurVisual);
        }
        catch
        {
            _chromeBlurVisual = null; // blur is decorative - a failure must not break dialogs
        }
    }

    private void StartCarouselFloat()
    {
        StopCarouselFloat();
        _carouselFloatStart = (double)System.Diagnostics.Stopwatch.GetTimestamp() / System.Diagnostics.Stopwatch.Frequency;
        _carouselDemoStart = 0; // demo clock restarts with the carousel
        CompositionTarget.Rendering += CarouselFloatFrame;
    }

    private void CarouselFloatFrame(object? sender, object e)
    {
        // N4-19: these five arrays used to be allocated every frame (60Hz GC churn) - the control
        // references are stable for the window lifetime, so build them once on the first frame.
        if (_floatPhases == null)
        {
            _floatPhases = new[] { 0.0, Math.PI / 2, Math.PI, Math.PI * 3 / 2 };
            _floatTransforms = new[] { CarouselFloatT0, CarouselFloatT1, CarouselFloatT2, CarouselFloatT3 };
            _floatScales = new[] { CarouselMenuScale0, CarouselMenuScale1, CarouselMenuScale2, CarouselMenuScale3 };
            _floatTabPhases = new[] { Math.PI / 4, Math.PI * 3 / 4, Math.PI * 5 / 4, Math.PI * 7 / 4 };
            _floatTabTransforms = new[] { CarouselP1FloatT0, CarouselP1FloatT1, CarouselP1FloatT2, CarouselP1FloatT3 };
        }
        var t = (double)System.Diagnostics.Stopwatch.GetTimestamp() / System.Diagnostics.Stopwatch.Frequency - _carouselFloatStart;
        var phases = _floatPhases;
        var transforms = _floatTransforms!;
        var scales = _floatScales!;
        for (int i = 0; i < 4; i++)
        {
            // hover lift/scale ease toward their target and stack ON TOP of the sine float - the
            // float loop owns Translate.Y, so hover must never write it directly (owner request:
            // menu scales up + lifts on pointer hover)
            _menuHoverLift[i] += (_menuHoverLiftTarget[i] - _menuHoverLift[i]) * 0.18;
            _menuHoverScale[i] += (_menuHoverScaleTarget[i] - _menuHoverScale[i]) * 0.18;
            transforms[i].Y = 5 * Math.Sin(2 * Math.PI * t / 3.0 + phases[i]) + _menuHoverLift[i];
            scales[i].ScaleX = scales[i].ScaleY = _menuHoverScale[i];
        }
        // page-1 tab buttons float too (a bit wider, phase-shifted so the two groups never sway in lockstep)
        var tabPhases = _floatTabPhases!;
        var tabTransforms = _floatTabTransforms!;
        for (int i = 0; i < 4; i++)
            tabTransforms[i].Y = 6 * Math.Sin(2 * Math.PI * t / 3.0 + tabPhases[i]);
        ApplyCarouselDemo(t - _carouselDemoStart); // page-2 demo runs on its own clock (restarted when the user lands on page 2)
    }

    // N4-19: per-frame scratch arrays, allocated once on the first rendered frame (see CarouselFloatFrame).
    private double[]? _floatPhases;
    private Microsoft.UI.Xaml.Media.TranslateTransform[]? _floatTransforms;
    private Microsoft.UI.Xaml.Media.ScaleTransform[]? _floatScales;
    private double[]? _floatTabPhases;
    private Microsoft.UI.Xaml.Media.TranslateTransform[]? _floatTabTransforms;

    // hover state of the four page-0 menus: targets eased toward per-frame in CarouselFloatFrame.
    // Scale arrays MUST start at 1.0 - a default-zero target collapsed all four menus to nothing.
    private readonly double[] _menuHoverLift = new double[4];
    private readonly double[] _menuHoverScale = { 1.0, 1.0, 1.0, 1.0 };
    private readonly double[] _menuHoverLiftTarget = new double[4];
    private readonly double[] _menuHoverScaleTarget = { 1.0, 1.0, 1.0, 1.0 };

    private void CarouselMenu_PointerEntered(object sender, PointerRoutedEventArgs e)
    {
        if (sender is FrameworkElement fe && int.TryParse(fe.Tag as string, out var i) && i >= 0 && i < 4)
        {
            _menuHoverLiftTarget[i] = -6;    // lift up
            _menuHoverScaleTarget[i] = 1.05; // slightly larger
            Canvas.SetZIndex(fe, 10);        // hovered menu floats to the top - no more feeling squeezed between neighbors
        }
    }

    private void CarouselMenu_PointerExited(object sender, PointerRoutedEventArgs e)
    {
        if (sender is FrameworkElement fe && int.TryParse(fe.Tag as string, out var i) && i >= 0 && i < 4)
        {
            _menuHoverLiftTarget[i] = 0;
            _menuHoverScaleTarget[i] = 1.0;
            Canvas.SetZIndex(fe, i);         // restore declaration order (0..3)
        }
    }

    private void StopCarouselFloat()
    {
        CompositionTarget.Rendering -= CarouselFloatFrame;
        CarouselFloatT0.Y = 0;
        CarouselFloatT1.Y = 0;
        CarouselFloatT2.Y = 0;
        CarouselFloatT3.Y = 0;
        CarouselP1FloatT0.Y = 0;
        CarouselP1FloatT1.Y = 0;
        CarouselP1FloatT2.Y = 0;
        CarouselP1FloatT3.Y = 0;
        // reset the page-2 demo props to their collapsed rest state
        CarouselDemoCursorTranslate.X = DemoCursorStartX;
        CarouselDemoCursorTranslate.Y = DemoCursorStartY;
        CarouselDemoMenuFace.Width = 8;
        CarouselDemoMenuFace.Height = 48;
        CarouselDemoMenuFace.CornerRadius = new CornerRadius(4);
        CarouselDemoMenuIcon.Opacity = 0;
        CarouselDemoMenuTranslate.X = 6;
        // N4-20: reset the hover easing state + menu scales too - without it a reopened carousel
        // started from the mid-transition values of the previous display (single display per
        // process, so this is purely defensive).
        for (int i = 0; i < 4; i++)
        {
            _menuHoverLift[i] = 0;
            _menuHoverScale[i] = 1.0;
            _menuHoverLiftTarget[i] = 0;
            _menuHoverScaleTarget[i] = 1.0;
        }
        CarouselMenuScale0.ScaleX = CarouselMenuScale0.ScaleY = 1.0;
        CarouselMenuScale1.ScaleX = CarouselMenuScale1.ScaleY = 1.0;
        CarouselMenuScale2.ScaleX = CarouselMenuScale2.ScaleY = 1.0;
        CarouselMenuScale3.ScaleX = CarouselMenuScale3.ScaleY = 1.0;
    }

    // ===================== 9.3 carousel page 1: four NavBar-style tab buttons + intro =====================

    /// <summary>
    /// 9.3 Fill the four tab buttons (real NavBar layout: Path 20x20 + 14px text, real icon paths
    /// and Nav_Tab_* keys) and preselect the first tab so the intro area is never empty.
    /// </summary>
    private void BuildCarouselPage1()
    {
        CarouselP1Title.Text = App.GetString("Carousel_P1_Title");
        BuildCarouselP1Tab(CarouselP1BtnMemo, IconData.NavMemo, "Nav_Tab_Memo");
        BuildCarouselP1Tab(CarouselP1BtnFile, IconData.NavFile, "Nav_Tab_File");
        BuildCarouselP1Tab(CarouselP1BtnPlan, IconData.NavPlan, "Nav_Tab_Plan");
        BuildCarouselP1Tab(CarouselP1BtnDiary, IconData.NavDiary, "Nav_Tab_Diary");
        SetCarouselP1Selection(CarouselP1BtnMemo, "Carousel_P1_Memo"); // default = first tab (owner-approved)
    }

    // One tab button content = Path(20x20) + 14px text - the real NavBar button layout.
    private static void BuildCarouselP1Tab(Button btn, string iconPath, string textKey)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        panel.Children.Add(new Microsoft.UI.Xaml.Shapes.Path
        {
            Width = 20,
            Height = 20,
            Stretch = Stretch.Uniform,
            Data = App.CreateGeometry(iconPath),
            Fill = App.GetBrush("IconForegroundBrush"),
        });
        panel.Children.Add(new TextBlock
        {
            Text = App.GetString(textKey),
            FontSize = 14,
            VerticalAlignment = VerticalAlignment.Center,
        });
        btn.Content = panel;
    }

    private void CarouselP1Tab_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string introKey)
            SetCarouselP1Selection(btn, introKey);
    }

    private Microsoft.UI.Xaml.Media.ThemeShadow? _carouselP1Shadow; // carousel-only receiver - the real nav receiver lives inside the hidden NavBar

    private void SetCarouselP1Selection(Button selected, string introKey)
    {
        // Full replica of the real NavBar selection visuals (UpdateNavSelection): raised background,
        // brand-blue text+icon on selection, dim gray text + 0.6 fade otherwise, NO border.
        // The lift reads through ThemeShadow: on the dialog background the raised face alone is
        // invisible (same tone), the Z=20 shadow is what makes the selection obvious (real nav parity).
        var buttons = new[] { CarouselP1BtnMemo, CarouselP1BtnFile, CarouselP1BtnPlan, CarouselP1BtnDiary };
        var dimBrush = App.GetBrush("AppTextTertiaryBrush");
        var brandBrush = App.GetBrush("AppPrimaryButtonBrush");
        var iconBrush = App.GetBrush("IconForegroundBrush");
        var clearBg = new SolidColorBrush(Windows.UI.Color.FromArgb(0, 0, 0, 0));
        var raisedBg = App.GetBrush("NavFaceRaisedBrush");
        try
        {
            if (_carouselP1Shadow == null)
            {
                _carouselP1Shadow = new Microsoft.UI.Xaml.Media.ThemeShadow();
                _carouselP1Shadow.Receivers.Add(CarouselP1ShadowReceiver);
                foreach (var b in buttons) b.Shadow = _carouselP1Shadow;
            }
        }
        catch { }
        foreach (var b in buttons)
        {
            bool sel = b == selected;
            b.Background = sel ? raisedBg : clearBg;
            b.Foreground = sel ? brandBrush : dimBrush; // TextBlock inherits - same as the real NavBar
            b.Translation = sel ? new System.Numerics.Vector3(0f, 0f, 20f) : System.Numerics.Vector3.Zero; // lift = the real nav signal
            SetNavOpacity(b, sel ? 1.0 : 0.6, NavBar.IsLoaded); // same pulse engine as the real NavBar
            if (b.Content is StackPanel sp && sp.Children.Count > 0 && sp.Children[0] is Microsoft.UI.Xaml.Shapes.Path icon)
                icon.Fill = sel ? brandBrush : iconBrush;
        }
        // intro text: swap content, fade in 200ms (opacity animation precedent: ShowWelcomeCarousel)
        CarouselP1Intro.Text = App.GetString(introKey);
        CarouselP1Intro.Opacity = 0;
        var sb = new Storyboard();
        var oi = new DoubleAnimation { To = 1, Duration = TimeSpan.FromMilliseconds(200) };
        Storyboard.SetTarget(oi, CarouselP1Intro); Storyboard.SetTargetProperty(oi, "Opacity"); sb.Children.Add(oi);
        sb.Begin();
    }

    // ===================== 9.3 carousel page 2: hamburger handle auto-demo =====================

    /// <summary>9.3 Fill page-2 texts and the mirrored hamburger icon (real IconData.Menu).</summary>
    private void BuildCarouselPage2()
    {
        CarouselP2Title.Text = App.GetString("Carousel_P2_Title");
        CarouselP2Intro.Text = App.GetString("Carousel_P2_Desc");
        CarouselDemoMenuIcon.Data = App.CreateGeometry(IconData.Menu);
    }

    // Demo script constants (owner-approved loop, 4.5s per round). Cursor tip coordinates are
    // carousel-card pixels; the end point tracks the handle position (margin right 70 / bottom 115).
    private const double DemoCursorStartX = 140, DemoCursorStartY = 150; // far side of the card
    private const double DemoCursorEndX = 600, DemoCursorEndY = 313;     // tip lands on the handle bar

    private static double DemoEaseInOut(double t) => t < 0.5 ? 2 * t * t : 1 - Math.Pow(-2 * t + 2, 2) / 2;
    private static double DemoCubicOut(double t) => 1 - Math.Pow(1 - Math.Clamp(t, 0, 1), 3); // real melt easing
    private static double DemoLerp(double a, double b, double k) => a + (b - a) * k;

    private double _carouselDemoStart; // demo-local clock origin - reset whenever the user lands on page 2

    private double CarouselClockSeconds() => (double)System.Diagnostics.Stopwatch.GetTimestamp() / System.Diagnostics.Stopwatch.Frequency - _carouselFloatStart;

    /// <summary>
    /// 9.3 Page-2 demo timeline (demo-local clock): cursor approaches (1.0s) -> handle melts open
    /// (0.32s, real transition) -> hold (1.08s) -> cursor leaves AND handle melts back starting at
    /// the SAME instant (real PointerExited behavior - owner: the collapse must follow the cursor,
    /// not run ahead of it) -> short breathing rest (1.2s) -> loop. Mirror of the real hamburger
    
    /// </summary>
    private void ApplyCarouselDemo(double t)
    {
        double phase = t % 4.4;
        double p; // melt progress: 0 = collapsed bar, 1 = expanded hamburger
        double cx, cy;
        if (phase < 1.0)
        {
            double k = DemoEaseInOut(phase);
            cx = DemoLerp(DemoCursorStartX, DemoCursorEndX, k);
            cy = DemoLerp(DemoCursorStartY, DemoCursorEndY, k);
            p = 0;
        }
        else if (phase < 1.32) { cx = DemoCursorEndX; cy = DemoCursorEndY; p = DemoCubicOut((phase - 1.0) / 0.32); }
        else if (phase < 2.4) { cx = DemoCursorEndX; cy = DemoCursorEndY; p = 1; }
        else
        {
            // leave phase [2.4, 3.2) and rest [3.2, 4.4): collapse starts exactly when the cursor
            // starts moving away (same instant, no head start - that gap read as two animations)
            if (phase < 3.2)
            {
                double k = DemoEaseInOut((phase - 2.4) / 0.8);
                cx = DemoLerp(DemoCursorEndX, DemoCursorStartX, k);
                cy = DemoLerp(DemoCursorEndY, DemoCursorStartY, k);
            }
            else { cx = DemoCursorStartX; cy = DemoCursorStartY; }
            p = phase < 2.72 ? 1 - DemoCubicOut((phase - 2.4) / 0.32) : 0;
        }

        CarouselDemoCursorTranslate.X = cx;
        CarouselDemoCursorTranslate.Y = cy;
        // ApplyMenuProgress mirror (real: width/height/corner/icon opacity in lockstep)
        CarouselDemoMenuFace.Width = 8 + (56 - 8) * p;
        CarouselDemoMenuFace.Height = 48 + (32 - 48) * p;
        CarouselDemoMenuFace.CornerRadius = new CornerRadius(4 + (8 - 4) * p);
        CarouselDemoMenuIcon.Opacity = p;
        
        CarouselDemoMenuTranslate.X = p > 0.01 ? 6 : 6 + Math.Sin(t * 2 * Math.PI / 3.2) * 3;
    }

    // ===================== 9.3 carousel page 3: privacy lock showcase =====================

    /// <summary>9.3 Fill page-3 texts: all cards replicate real settings-page entries (owner-picked:
    /// MCP above, archive/restore below) and the lock card shows the unlocked state with two brand
    /// blue buttons (Windows Hello on the left of the master switch - owner call).</summary>
    private void BuildCarouselPage3()
    {
        CarouselP3Title.Text = App.GetString("Carousel_P3_Title");
        CarouselP3Intro.Text = App.GetString("Carousel_P3_Desc");
        CarouselMcpTitle.Text = App.GetString("Setting_Mcp_Title");
        CarouselMcpAuditText.Text = App.GetString("Setting_Mcp_Audit_Button");
        CarouselMcpConfigText.Text = App.GetString("Setting_Mcp_Config");
        CarouselMcpToggleText.Text = App.GetString("Setting_Autostart_Off");
        CarouselArchiveTitle.Text = App.GetString("Setting_Archive_Title");
        CarouselArchiveButtonText.Text = App.GetString("Setting_Archive_ImportExport");
        CarouselLockTitle.Text = App.GetString("Setting_PrivacyLock_Title");
        CarouselLockHelloText.Text = App.GetString("Setting_WinHello_Title");
        CarouselLockSetupText.Text = App.GetString("Setting_PrivacyLock_Setup");
    }

    // Flowing brand border removed (owner call: didn't work visually). Page-3 cards are pure
    // Viewbox-scaled replicas now - no per-frame code needed for this page.


    private Grid[] CarouselPageList = System.Array.Empty<Grid>();

    private void BuildCarouselDots()
    {
        CarouselDots.Children.Clear();
        for (int i = 0; i < CarouselPageList.Length; i++)
        {
            var idx = i;
            // Border directly (no Button shell) - a transparent-background Button flashes system
            // colors on hover (3.5 trap) and the dots must not react to hover at all.
            var dot = new Border
            {
                CornerRadius = new CornerRadius(5),
                Height = 10,
                Width = idx == 0 ? 26 : 10,
                Background = idx == 0 ? App.GetBrush("AppPrimaryButtonBrush") : App.GetBrush("AppBorderBrush"),
                Margin = new Thickness(5, 5, 5, 5),
            };
            dot.Tapped += (_, _) => SetCarouselPage(idx);
            CarouselDots.Children.Add(dot);
        }
    }

    private void SetCarouselPage(int index)
    {
        // N2-47: wheel/dot page switches don't guarantee PointerExited - stale hover targets would
        // ease a menu toward lifted/scaled on the next show until the pointer re-entered and left.
        Array.Fill(_menuHoverLiftTarget, 0);
        Array.Fill(_menuHoverScaleTarget, 1.0);
        if (index < 0 || index >= CarouselPageList.Length) return;
        _carouselPage = index;
        if (index == 2) _carouselDemoStart = CarouselClockSeconds(); // owner call: landing on page 2 replays the demo from its first frame
        for (int i = 0; i < CarouselPageList.Length; i++)
            CarouselPageList[i].Visibility = i == index ? Visibility.Visible : Visibility.Collapsed;
        // 9.3: the X button appears ONLY on the last page - the user reads every card before closing (owner call)
        CarouselCloseButton.Visibility = index == CarouselPageList.Length - 1 ? Visibility.Visible : Visibility.Collapsed;
        // dots: selected grows into a brand-blue pill, others stay small gray dots
        int dotIdx = 0;
        foreach (var child in CarouselDots.Children)
        {
            if (child is Border dot)
            {
                bool sel = dotIdx == index;
                dot.Width = sel ? 26 : 10;
                dot.Background = sel ? App.GetBrush("AppPrimaryButtonBrush") : App.GetBrush("AppBorderBrush");
                dotIdx++;
            }
        }
    }

    private void WelcomeCarousel_WheelChanged(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        var delta = e.GetCurrentPoint(WelcomeCarousel).Properties.MouseWheelDelta;
        if (delta < 0) SetCarouselPage(_carouselPage + 1); // scroll down = next page
        else if (delta > 0) SetCarouselPage(_carouselPage - 1); // scroll up = previous page
        e.Handled = true;
    }

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
        UpdateWorkspaceSwitcher(); // 9.3 Workspace: title-bar switcher visibility from the workspace list
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
            MaybeShowWelcomeCarousel(); // 9.3: first-launch promo carousel (debug builds replay every launch)
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

    /// <summary>Import request from the .md file right-click menu: switch to the Records tab and import
    /// the file through the diary page's shared import core.</summary>
    public void HandleImportMdRequest(string filePath)
    {
        if (App.Store is not { IsLoaded: true }) return; // E1-19: see HandleEditRequest
        if (WelcomeOverlay.Visibility == Visibility.Visible) return; // E3-11: see HandleEditRequest
        if (!IsTabVisible("Diary")) { RefuseHiddenTabAction("Diary"); return; } // N4W-03: see HandleEditRequest
        if (RestorePendingBlocksNavigation()) { RefuseRestorePending(); return; } // N5S-02
        FlushEditorDirty(); // N4W-04
        ShowMainWindow(); // restore from minimized (and raise to foreground) first
        var (diaryTab, diaryNav) = ResolveNavTarget("Diary"); // E5-05: fall back to first visible when Diary is hidden
        NavigateToPage(diaryTab);
        UpdateNavSelection(diaryNav);
        RestoreChromeFor(RootFrame.Content); // D15: see HandleEditRequest
        _ = _diaryPage?.ImportDocumentFromPath(filePath);
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
        var prevContent = RootFrame.Content;
        RootFrame.Content = _searchPage;
        PlayPageIn(prevContent); // M2: page-in transition
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
            PlayPageIn(_searchPage); // M2: page-in transition (previous = the search page we are leaving)
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

    /// <summary>9.3 Command Palette: execute a command picked from the search page's "&gt;" mode.</summary>
    public void RunPaletteCommand(string cmd)
    {
        // N5S-02 parity with SearchJump: commands navigate and open dialogs - never tear the restore barrier down.
        if (RestorePendingBlocksNavigation()) { App.ShowToast(App.GetString("Common_Toast_RestorePending")); return; }
        CloseSearchPage();
        switch (cmd)
        {
            case "new_memo":
                { var (t, n) = ResolveNavTarget("Memo"); NavigateToPage(t); UpdateNavSelection(n); } // E5-05: never land on a VisibleTabs-hidden page
                _memoPage?.OpenNewEntryDialog();
                break;
            case "new_todo":
                { var (t, n) = ResolveNavTarget("Plan"); NavigateToPage(t); UpdateNavSelection(n); }
                _planPage?.ShowNewItemDialog(App.GetString("Plan_Todo_NewTitle"));
                break;
            case "new_note":
                { var (t, n) = ResolveNavTarget("Plan"); NavigateToPage(t); UpdateNavSelection(n); }
                _planPage?.ShowNewNoteDialog();
                break;
            case "new_diary":
                NavigateToEditor(); // switches to the diary tab and opens the editor (5.0 flow)
                break;
            case "new_document":
                NavigateToEditor(null, "markdown");
                break;
            case "open_settings":
                NavigateToSettings();
                break;
            case "open_trash":
                OpenTrashPage();
                break;
            case "lock_now":
                if (App.Store is { IsEncrypted: true }) LockNow();
                else App.ShowToast(App.GetString("Tray_LockNeedLock")); // same as the tray command
                break;
        }
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
        EnsureTargetWorkspaceVisible(kind, id); 
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

    /// <summary>9.3 Workspace C2: if the jump target belongs to a workspace, switch to it so the
    /// card is visible after the jump (search stays global, but the landing page must show it).</summary>
    private void EnsureTargetWorkspaceVisible(string kind, string id)
    {
        var db = App.Store?.Database;
        if (db == null) return;
        string? wsId = null;
        if (kind == "memo" && Guid.TryParse(id, out var mg)) wsId = db.MemoEntries.FirstOrDefault(x => x.Id == mg)?.WorkspaceId;
        else if (kind == "path" && Guid.TryParse(id, out var pg)) wsId = db.PathBackupItems.FirstOrDefault(x => x.Id == pg)?.WorkspaceId;
        else if (kind == "todo" && Guid.TryParse(id, out var tg)) wsId = db.TodoCards.FirstOrDefault(x => x.Id == tg)?.WorkspaceId;
        else if (kind == "note" && Guid.TryParse(id, out var ng)) wsId = db.NoteCards.FirstOrDefault(x => x.Id == ng)?.WorkspaceId;
        else if (kind == "diary") wsId = db.DiaryItems.FirstOrDefault(x => x.Id == id)?.WorkspaceId;

        if (!string.IsNullOrEmpty(wsId) && App.CurrentWorkspaceId != wsId)
        {
            App.CurrentWorkspaceId = wsId;
            UpdateWorkspaceSwitcher();
            RefreshWorkspaceFilters();
            return;
        }
        // N3-43: the target is UNASSIGNED (WorkspaceId empty) while a workspace view is active -
        // unassigned cards are hidden under any workspace filter, so the flash target would be
        
        // card is reachable (search is global by design, doc 9.3 UI decisions #3).
        if (string.IsNullOrEmpty(wsId) && !string.IsNullOrEmpty(App.CurrentWorkspaceId))
        {
            App.CurrentWorkspaceId = "";
            UpdateWorkspaceSwitcher();
            RefreshWorkspaceFilters();
        }
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
        // N2-02: leaving the trash page via SearchJump / RunPaletteCommand / IPC edit all funnel
        // through here and previously bypassed CloseTrashPage's rebuild - the cached tab page stayed
        // stale and its next PersistAll physically dropped restored entries from the db (D13 class).
        // Any navigation OUT of the trash page must run the same invalidation first.
        if (_trashChanged && ReferenceEquals(RootFrame.Content, _trashPage))
            InvalidateTrashStaleTabs();
        var previous = RootFrame.Content; // M2: page-in transition dedup anchor
        // N2-01: swap a page instance that missed external (MCP) db writes.
        if (_memoStale && tag == "Memo") { _memoPage = new BasicMemoPage(); _memoStale = false; }
        else if (_planStale && tag == "Plan") { _planPage = new PlanPage(); _planStale = false; }
        else if (_fileStale && tag == "File") { _filePathPage = new FilePathPage(); _fileStale = false; }
        else if (_diaryStale && tag == "Diary") { _diaryPage = new DiaryPage(); _diaryStale = false; }
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
        PlayPageIn(previous);
    }

    

    private Storyboard? _pageInBoard;

    
    
    
    
    private void PlayPageIn(object? previous)
    {
        if (ReferenceEquals(previous, RootFrame.Content)) return;
        _pageInBoard?.Stop();
        _pageInBoard = null;
        var tt = RootFrame.RenderTransform as TranslateTransform ?? new TranslateTransform();
        RootFrame.RenderTransform = tt;
        RootFrame.Opacity = 0;
        tt.Y = 12;
        var sb = new Storyboard();
        var fi = new DoubleAnimation { To = 1, Duration = TimeSpan.FromMilliseconds(240) };
        Storyboard.SetTarget(fi, RootFrame); Storyboard.SetTargetProperty(fi, "Opacity"); sb.Children.Add(fi);
        sb.Children.Add(Services.Motion.Eased(0, 260, Services.Motion.Decelerate, tt, "Y"));
        _pageInBoard = sb;
        sb.Completed += (_, _) =>
        {
            if (_pageInBoard != sb) return; // a newer transition took over - it owns the reset
            _pageInBoard = null;
            RootFrame.Opacity = 1;
            tt.Y = 0;
        };
        sb.Begin();
    }

    private void UpdateNavSelection(Button selected)
    {
        
        
        
        if (_selectedNavButton == selected) return;
        _selectedNavButton = selected;
        var dimBrush = App.GetBrush("AppTextTertiaryBrush");
        var brandBrush = App.GetBrush("AppPrimaryButtonBrush");
        var iconBrush = App.GetBrush("IconForegroundBrush");
        var clearBg = new SolidColorBrush(Windows.UI.Color.FromArgb(0, 0, 0, 0));
        
        var raisedBg = App.GetBrush("NavFaceRaisedBrush");

        var navButtons = new[] { (Btn: NavMemo, Icon: NavMemoPath), (Btn: NavFile, Icon: NavFilePath), (Btn: NavPlan, Icon: NavPlanPath), (Btn: NavDiary, Icon: NavDiaryPath) };

        foreach (var item in navButtons)
        {
            bool isSelected = item.Btn == selected;
            
            item.Btn.Background = isSelected ? raisedBg : clearBg;
            item.Btn.Translation = isSelected
                ? new System.Numerics.Vector3(0f, 0f, 20f) 
                : System.Numerics.Vector3.Zero;
            
            SetNavOpacity(item.Btn, isSelected ? 1.0 : 0.6, NavBar.IsLoaded);
            
            SetNavOpacity(item.Icon, isSelected ? 1.0 : 0.6, NavBar.IsLoaded);
            
            item.Btn.Foreground = isSelected ? brandBrush : dimBrush;
            item.Icon.Fill = isSelected ? brandBrush : iconBrush;
        }

        
        
        bool diarySelected = selected == NavDiary;
        NavDiaryFilterButton.Visibility = diarySelected ? Visibility.Visible : Visibility.Collapsed;
        NavDiaryFilterIcon.Foreground = diarySelected ? brandBrush : iconBrush;
        bool planSelected = selected == NavPlan;
        NavPlanFilterButton.Visibility = planSelected ? Visibility.Visible : Visibility.Collapsed;
        NavPlanFilterIcon.Foreground = planSelected ? brandBrush : iconBrush;
        UpdateNavIndicator(selected); 
    }

    

    private bool _navIndicatorPlaced;

    
    
    private void WireNavGlowRelocation()
    {
        NavBar.SizeChanged += (_, _) =>
        {
            if (_selectedNavButton == null) return;
            _navIndicatorPlaced = false;
            UpdateNavIndicator(_selectedNavButton);
        };
    }

    
    
    
    private void UpdateNavIndicator(Button selected)
    {
        if (NavBar.Visibility != Visibility.Visible || selected == null
            || selected.Visibility != Visibility.Visible || selected.ActualWidth < 1)
        {
            
            var pending = selected;
            DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low,
                () => { if (pending != null && NavBar.Visibility == Visibility.Visible && ReferenceEquals(_selectedNavButton, pending) && pending.Visibility == Visibility.Visible && pending.ActualWidth >= 1) UpdateNavIndicator(pending); }); // N5-T2-03
            return;
        }
        var pt = selected.TransformToVisual(NavBar).TransformPoint(new Windows.Foundation.Point(0, 0));
        NavIndicator.Width = selected.ActualWidth; 
        NavIndicator.Opacity = 1;
        if (!_navIndicatorPlaced)
        {
            SetNavGlowX(pt.X); 
            _navIndicatorPlaced = true;
            return;
        }
        SlideNavGlow(pt.X);
    }

    
    
    private void SetNavGlowX(double x)
    {
        try
        {
            var visual = Microsoft.UI.Xaml.Hosting.ElementCompositionPreview.GetElementVisual(NavIndicator);
            visual.Offset = new System.Numerics.Vector3((float)x, visual.Offset.Y, 0);
        }
        catch
        {
            
            try { var v = Microsoft.UI.Xaml.Hosting.ElementCompositionPreview.GetElementVisual(NavIndicator); v.StopAnimation("Offset.X"); v.Offset = new System.Numerics.Vector3(0, v.Offset.Y, 0); } catch { }
            NavIndicatorTranslate.X = x;
        }
    }

    private void SlideNavGlow(double x)
    {
        try
        {
            var visual = Microsoft.UI.Xaml.Hosting.ElementCompositionPreview.GetElementVisual(NavIndicator);
            float startX = visual.Offset.X;
            var comp = visual.Compositor;
            var anim = comp.CreateScalarKeyFrameAnimation();
            anim.Duration = TimeSpan.FromMilliseconds(Services.Motion.Normal);
            
            anim.InsertKeyFrame(0.4f, Lerp(startX, (float)x, 0.55f));
            anim.InsertKeyFrame(0.75f, Lerp(startX, (float)x, 0.87f));
            anim.InsertKeyFrame(1.0f, (float)x);
            visual.StartAnimation("Offset.X", anim);
        }
        catch
        {
            
            try { var v = Microsoft.UI.Xaml.Hosting.ElementCompositionPreview.GetElementVisual(NavIndicator); v.StopAnimation("Offset.X"); v.Offset = new System.Numerics.Vector3(0, v.Offset.Y, 0); } catch { }
            NavIndicatorTranslate.X = x;
        }
    }

    private static float Lerp(float a, float b, float t) => a + (b - a) * t;

    
    private static void SetNavOpacity(Microsoft.UI.Xaml.FrameworkElement el, double opacity, bool animate)
    {
        try
        {
            if (!animate || el.ActualWidth <= 0)
            {
                el.Opacity = opacity;
                _navFades.Remove(el);
                return;
            }
            if (_navFades.TryGetValue(el, out var hold) && hold.Val == opacity) return; 

            double from = hold?.Val ?? el.Opacity;
            if (hold != null) { try { hold.Sb.Stop(); } catch { } _navFades.Remove(el); }

            var anim = new Microsoft.UI.Xaml.Media.Animation.DoubleAnimation
            {
                From = from,
                To = opacity,
                Duration = new Duration(TimeSpan.FromMilliseconds(200)),
                EasingFunction = new Microsoft.UI.Xaml.Media.Animation.CubicEase { EasingMode = Microsoft.UI.Xaml.Media.Animation.EasingMode.EaseOut },
            };
            Microsoft.UI.Xaml.Media.Animation.Storyboard.SetTarget(anim, el);
            Microsoft.UI.Xaml.Media.Animation.Storyboard.SetTargetProperty(anim, "Opacity");
            var sb = new Microsoft.UI.Xaml.Media.Animation.Storyboard();
            sb.Children.Add(anim);
            _navFades[el] = new NavFadeRec { Sb = sb, Val = opacity };
            sb.Begin();
        }
        catch { el.Opacity = opacity; }
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
                : new PathIcon { Data = App.CreateGeometry(iconPath), Foreground = iconBrush, Opacity = 0.6 };
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
                : new PathIcon { Data = App.CreateGeometry(iconPath), Foreground = iconBrush, Opacity = 0.6 };
            item.Click += (_, _) => { _planPage?.SetFilter(filter); App.ShowToast(App.GetString("Common_Toast_Switched")); };
            return item;
        }

        menu.Items.Add(MakeItem(App.GetString("Diary_Filter_All"), "all", IconData.Mix));
        menu.Items.Add(MakeItem(App.GetString("Plan_Filter_Todo"), "todo", string.Join(" ", IconData.Todo)));
        menu.Items.Add(MakeItem(App.GetString("Plan_Filter_Note"), "note", string.Join(" ", IconData.Note)));
        menu.ShowAt(NavPlan, new Windows.Foundation.Point(0, NavPlan.ActualHeight + 4));
    }

    // ===================== 9.3 Workspace: title-bar switcher =====================

    /// <summary>Update the switcher's visibility/icon/text/color from the workspace list and App.CurrentWorkspaceId.</summary>
    public void UpdateWorkspaceSwitcher()
    {
        var ws = App.Store?.Database.AppSettings.Workspaces;
        if (ws == null || ws.Count == 0)
        {
            WorkspaceSwitcherButton.Visibility = Visibility.Collapsed;
            App.CurrentWorkspaceId = "";
            return;
        }

        WorkspaceSwitcherButton.Visibility = Visibility.Visible;
        var brand = App.GetBrush("AppPrimaryButtonBrush");
        var normal = App.GetBrush("AppTextSecondaryBrush");
        var iconNormal = App.GetBrush("IconForegroundBrush");

        var current = ws.FirstOrDefault(w => w.Id == App.CurrentWorkspaceId);
        if (current == null) App.CurrentWorkspaceId = ""; 
        bool active = current != null;
        WorkspaceSwitcherIcon.Data = App.CreateGeometry(active ? IconData.GetGroupPath(current!.IconKey) : IconData.Mix);
        WorkspaceSwitcherIcon.Foreground = active ? brand : iconNormal;
        WorkspaceSwitcherText.Text = active ? current!.Name : App.GetString("Workspace_All");
        WorkspaceSwitcherText.Foreground = active ? brand : normal;
    }

    /// <summary>9.3 Workspace: re-apply the workspace filter on every already-built tab page.
    /// Unbuilt pages apply the filter lazily in their LoadFromStore.</summary>
    public void RefreshWorkspaceFilters()
    {
        _memoPage?.ApplyCardFilters();
        _filePathPage?.ApplyCardFilters();
        _planPage?.ApplyCardFilters();
        _diaryPage?.ApplyCardFilters();
    }

    
    
    private void UpdateWorkspaceSwitcherPosition()
    {
        if (_appWindow == null) return;
        try
        {
            double rightInset = _appWindow.TitleBar.RightInset; // physical pixels
            double scale = Content.XamlRoot?.RasterizationScale ?? 1.0;
            double rightMargin = rightInset / scale + 8; // logical pixels + 8px gap
            WorkspaceSwitcherButton.Margin = new Thickness(0, 0, rightMargin, 0);
        }
        catch { }
    }

    private void WorkspaceSwitcherButton_Click(object sender, RoutedEventArgs e)
    {
        var ws = App.Store?.Database.AppSettings.Workspaces;
        var menu = new MenuFlyout { MenuFlyoutPresenterStyle = (Style)Application.Current.Resources["GlassMenuFlyoutPresenterStyle"] };
        var activeBrush = App.GetBrush("AppTextPrimaryBrush");
        var normalBrush = App.GetBrush("AppTextSecondaryBrush");
        var iconBrush = App.GetBrush("IconForegroundBrush");

        MenuFlyoutItem MakeItem(string text, string iconPath, bool active)
        {
            var item = new MenuFlyoutItem { Style = (Style)Application.Current.Resources["GlassMenuFlyoutItemStyle"], Text = text, Foreground = active ? activeBrush : normalBrush };
            item.Icon = active
                ? new FontIcon { Glyph = "\uE73E", FontSize = 12, Foreground = activeBrush }
                : new PathIcon { Data = App.CreateGeometry(iconPath), Foreground = iconBrush, Opacity = 0.6 };
            return item;
        }

        var all = MakeItem(App.GetString("Workspace_All"), IconData.Mix, string.IsNullOrEmpty(App.CurrentWorkspaceId));
        all.Click += (_, _) => { App.CurrentWorkspaceId = ""; UpdateWorkspaceSwitcher(); RefreshWorkspaceFilters(); };
        menu.Items.Add(all);

        if (ws != null)
            foreach (var w in ws)
            {
                var item = MakeItem(w.Name, IconData.GetGroupPath(w.IconKey), w.Id == App.CurrentWorkspaceId);
                var wid = w.Id;
                item.Click += (_, _) => { App.CurrentWorkspaceId = wid; UpdateWorkspaceSwitcher(); RefreshWorkspaceFilters(); };
                menu.Items.Add(item);
            }

        menu.Items.Add(new MenuFlyoutSeparator());
        var manage = MakeItem(App.GetString("Workspace_Manage"), string.Join(" ", IconData.Settings), false);
        manage.Click += (_, _) => NavigateToSettings(openWorkspace: true);
        menu.Items.Add(manage);

        menu.ShowAt(WorkspaceSwitcherButton, new Windows.Foundation.Point(0, WorkspaceSwitcherButton.ActualHeight + 4));
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
        _memoStale = _planStale = _fileStale = _diaryStale = false; // N2-01: fresh instances below already contain external writes
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
        UpdateWorkspaceSwitcher(); 
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

    private void NavigateToSettings(bool openWorkspace = false)
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
            if (openWorkspace) _settingsPage.OpenWorkspaceManagement(); 

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
        StoreCorruptDangerIcon.Data = App.CreateGeometry(IconData.Danger); // N2-13: danger triangle
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
        StoreCorruptDialogTransform.ScaleX = 0.94; StoreCorruptDialogTransform.ScaleY = 0.94; StoreCorruptDialogTransform.TranslateY = 24;
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
            else
            {
                ShowMainContent(); // N2-49: plaintext + skipped-welcome rebuild left RootFrame empty and chrome hidden - unrecoverable without a restart
            }
        }
    }

    private void ShowSaveFailedDialog()
    {
        _animSaveFailedHide = false; // R1 (Round 5): reset re-entry flag when new animation takes over (prevent stuck state)
        SaveFailedDialogTransform.ScaleX = 0.94; SaveFailedDialogTransform.ScaleY = 0.94; SaveFailedDialogTransform.TranslateY = 24;
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
            try { await pendingEditor.SaveCurrentDiaryAsync().WaitAsync(TimeSpan.FromSeconds(3)); } catch { } 
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
            AcquireSingleInstance(); 
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
        // N2-50/E3-11: the plaintext welcome page occludes the target page - the dialog would open
        // unseen and be dropped when the welcome tap navigates away. The agent just re-requests.
        if (WelcomeOverlay.Visibility == Visibility.Visible) { onResult?.Invoke(false); return; }
        _mcpAuthorizeResult = onResult;
        McpAuthorizePath.Text = clientPath;
        McpAuthorizePath.Visibility = string.IsNullOrEmpty(clientPath) ? Visibility.Collapsed : Visibility.Visible;
        McpAuthorizeDialogTransform.ScaleX = 0.94; McpAuthorizeDialogTransform.ScaleY = 0.94; McpAuthorizeDialogTransform.TranslateY = 24;
        McpAuthorizeDialog.Opacity = 0;
        McpAuthorizeOverlay.Visibility = Visibility.Visible;
        var sb = new Storyboard();
        var da = new DoubleAnimation { To = 1, Duration = TimeSpan.FromMilliseconds(300) };
        Storyboard.SetTarget(da, McpAuthorizeScrim); Storyboard.SetTargetProperty(da, "Opacity"); sb.Children.Add(da);
        var dda = new DoubleAnimation { To = 1, Duration = TimeSpan.FromMilliseconds(300) };
        Storyboard.SetTarget(dda, McpAuthorizeDialog); Storyboard.SetTargetProperty(dda, "Opacity"); sb.Children.Add(dda);
        Motion.AddDialogShowTransform(sb, McpAuthorizeDialogTransform); // M1: EmphasizedDecelerate (Motion)
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
        Motion.AddDialogHideTransform(sb, McpAuthorizeDialogTransform); // M1: EmphasizedAccelerate (Motion)
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

    private void McpAuthorizeAllow_Click(object sender, RoutedEventArgs e)
    {
        // N2-50/E3-11: the plaintext welcome page occludes the target page - navigating to settings
        // from under it stranded RootFrame empty. Same guard as HandleEditRequest.
        if (WelcomeOverlay.Visibility == Visibility.Visible) return;
        // 9.2#5 (D4): land the fresh client on its permission editor immediately - the default set
        // only reads non-sensitive zones, so the user should see and adjust the matrix right away.
        var path = McpAuthorizePath.Text;
        if (!string.IsNullOrEmpty(path) && App.Store != null)
        {
            McpService.SeedDefaultPermission(path); // N2-21: pre-seed under WhitelistGate (idempotent, before the pipe-thread write)
            NavigateToSettings();
            DispatcherQueue.TryEnqueue(() => _settingsPage?.OpenMcpPermEditor(path)); // next frame - page may have just been created
        }
        HideMcpAuthorizeDialog(true);
    }
    private void McpAuthorizeDeny_Click(object sender, RoutedEventArgs e) => HideMcpAuthorizeDialog(false);
}
