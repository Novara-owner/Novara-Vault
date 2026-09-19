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
    private ToolsPage _toolsPage = null!;
    private Button? _selectedNavButton;
    private Microsoft.UI.Xaml.Media.ThemeShadow? _navShadow;



    private sealed class NavFadeRec { public Microsoft.UI.Xaml.Media.Animation.Storyboard Sb = null!; public double Val; }
    private static readonly System.Collections.Generic.Dictionary<Microsoft.UI.Xaml.UIElement, NavFadeRec> _navFades = new();
    private object? _previousContent;
    private SearchPage? _searchPage;
    private object? _preSearchContent;
    private TrashPage? _trashPage;
    private object? _preTrashContent;
    private bool _trashChanged;





    private bool _memoStale, _planStale, _fileStale, _diaryStale;




    private const double MenuBarW = 8, MenuBarH = 48, MenuBarR = 4;
    private const double MenuBtnW = 56, MenuBtnH = 32, MenuBtnR = 8;
    private const double MenuAnchorX = 6;
    private const double MenuBreath = 3;
    private const double MenuTransitionMs = 320;
    private bool _menuExpanded;
    private bool _menuOpen;
    private bool _hoverZoneHover;
    private bool _menuBtnHover;



    private bool _menuAnimating;
    private double _menuProgress;
    private double _menuAnimFrom, _menuAnimTo;
    private System.Diagnostics.Stopwatch? _menuAnimWatch;
    private System.Diagnostics.Stopwatch _glowWatch = System.Diagnostics.Stopwatch.StartNew();


    public void MarkTrashChanged() => _trashChanged = true;

    private Border? _toast;


    private const string ToastCheckIconPath = "M929.28 189.44l-417.28 417.28-158.208-158.208-53.248 53.248 184.832 184.832 26.624 25.6 26.624-25.6 443.904-443.904-53.248-53.248z m-417.28-158.72c-266.24 0-480.768 214.528-480.768 480.768s214.528 480.768 480.768 480.768 480.768-214.528 480.768-480.768c0-51.712-7.168-103.424-25.6-151.552l-59.904 58.88c7.168 29.696 11.776 59.392 11.776 92.672 0 225.792-181.248 407.04-407.04 407.04s-407.04-181.248-407.04-407.04 181.248-407.04 407.04-407.04c111.104 0 210.432 44.032 281.088 114.688L844.8 167.424c-84.992-84.992-203.264-136.704-332.8-136.704z";




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
            var fo = new DoubleAnimation { To = 0, Duration = TimeSpan.FromMilliseconds(300), BeginTime = TimeSpan.FromMilliseconds(1500) };
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
    private bool _restarting;
    private bool _exitSaving;
    private bool _welcomeFading;
    private bool _closeAllowed;
    private bool _animCorruptHide;
    private bool _animSaveFailedHide;
    private bool _closingHandled;
    private bool _corruptDialogIsIoError;
    private bool _themeChangedWhileLocked;

    private AppWindow? _appWindow;

    private TaskbarIcon? _trayIcon;

    public System.Windows.Input.ICommand ShowWindowCommand { get; }
    public System.Windows.Input.ICommand ExitCommand { get; }

    public MainWindow()
    {
        InitializeComponent();

        if (App.CurrentLanguage == "en-US")
        {
            WelcomeSubtitle.FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Assets/Fonts/Comfortaa.ttf#Comfortaa");
        }
        CarouselPageList = new[] { CarouselPage0, CarouselPage1, CarouselPage2, CarouselPage3 };
        BuildCarouselMenus();
        BuildCarouselPage1();
        BuildCarouselPage2();
        BuildCarouselPage3();



        Services.DialogDepth.AttachContainer((Grid)Content, driveChrome: false);

        ApplyStartupTheme();

        this.SizeChanged += (_, e) => UpdateWelcomeLayout(e.Size.Width, e.Size.Height);
        this.SizeChanged += (_, _) => UpdateWorkspaceSwitcherPosition();

        ExtendsContentIntoTitleBar = true;
        SetTitleBar(CustomTitleBar);


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
                if (LockScreenFrame.Visibility == Visibility.Visible) { _themeChangedWhileLocked = true; return; }
                ShowThemeRestartOverlay();
            };

            root.KeyDown += (_, e) =>
            {
                if (e.Key != Windows.System.VirtualKey.Escape) return;
                if (ThemeRestartOverlay.Visibility == Visibility.Visible) { HideThemeRestartOverlay(); e.Handled = true; }
                else if (SaveFailedOverlay.Visibility == Visibility.Visible) { HideSaveFailedDialog(); e.Handled = true; }
            };


            var ctrlK = new KeyboardAccelerator { Key = Windows.System.VirtualKey.K, Modifiers = Windows.System.VirtualKeyModifiers.Control };
            ctrlK.Invoked += (_, _) => { if (!HasTextInputFocus() && LockScreenFrame.Visibility != Visibility.Visible) OpenSearchPage(); };
            root.KeyboardAccelerators.Add(ctrlK);
            root.KeyboardAcceleratorPlacementMode = KeyboardAcceleratorPlacementMode.Hidden;




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
            AddHotkey((Windows.System.VirtualKey)188, Windows.System.VirtualKeyModifiers.Control, () => NavigateToSettings());
            AddHotkey(Windows.System.VirtualKey.Back, Windows.System.VirtualKeyModifiers.Control | Windows.System.VirtualKeyModifiers.Shift, OpenTrashPage);
            AddHotkey(Windows.System.VirtualKey.T, Windows.System.VirtualKeyModifiers.Control, () => NavigateToTools());



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




        var langHint = App.LoadLanguageHint();
        App.ApplyLanguage(langHint ?? "");
        Services.LoadResult? loadResult = null;
        if (App.Store is not { IsLoaded: true })
        {


            Novara.Services.NovaraStore.DiscardOrphanedPasswordFiles(Novara.Services.NovaraStore.DefaultFilePath);
            loadResult = App.Store?.Load();
            if (loadResult != null)
                System.Diagnostics.Debug.WriteLine($"NovaraStore 加载: {loadResult.Status}{(loadResult.Detail != null ? " - " + loadResult.Detail : "")}");
        }

        if (App.Store != null) App.Store.Database.AppSettings ??= new AppSettings();
        App.ApplyStoredSettings();
        TrashPage.CleanupExpired();


        Services.GlobalHotkeyService.Initialize(this, OnQuickCaptureHotkey, OnLockNowHotkey);
        WireNavGlowRelocation();



        if (!Services.GlobalHotkeyService.Register(Services.GlobalHotkeyService.IdLockNow,
                Services.GlobalHotkeyService.MOD_CONTROL | Services.GlobalHotkeyService.MOD_SHIFT, 0x4C))
            App.ShowToast(App.GetString("LockNow_HotkeyConflict"));
        ApplyQuickCaptureHotkey();

        McpService.AuthorizeClient = clientPath =>
        {
            bool allowed = false;



            var gate = new System.Threading.ManualResetEventSlim(false);
            DispatcherQueue.TryEnqueue(() => QueueOrShowMcpAuthorize(clientPath, r => { allowed = r; try { gate.Set(); } catch { } }));


            bool answered;
            try { answered = gate.Wait(TimeSpan.FromMinutes(2)); }
            finally { gate.Dispose(); }
            return answered ? allowed : false;
        };


        if (App.Store != null && McpPermissions.EnsureMigrated(App.Store.Database.AppSettings))
            _ = App.Store.SaveAsync();
        McpService.Start();




        App.InitSystemThemeWatcher();
        Services.StickySync.UpdateLanguage(App.CurrentLanguage);
        Services.StickySync.StartWatching((id, states) => _planPage?.ApplyExternalTodoState(id, states));


        Microsoft.UI.Xaml.Media.CompositionTarget.Rendering += OnMenuRendering;


        var pendingId = Novara.Services.EditRequest.ReadPending();
        if (pendingId.HasValue)
            DispatcherQueue.TryEnqueue(() => HandleEditRequest(pendingId.Value));


        var pendingPath = App.PendingAddPath ?? Novara.Services.AddPathRequest.ReadPending();
        if (!string.IsNullOrWhiteSpace(pendingPath))
            DispatcherQueue.TryEnqueue(() => HandleAddPathRequest(pendingPath));


        var pendingMd = App.PendingImportMd ?? Novara.Services.MdImportRequest.ReadPending();
        if (!string.IsNullOrWhiteSpace(pendingMd))
            DispatcherQueue.TryEnqueue(() => HandleImportMdRequest(pendingMd));


        var pendingReminder = Novara.Services.ReminderEditRequest.ReadPending();
        if (pendingReminder != null)
            DispatcherQueue.TryEnqueue(() => HandleReminderEditRequest(pendingReminder));


        var pendingDue = App.PendingReminderId ?? Novara.Services.ReminderDueRequest.ReadPending();
        if (pendingDue.HasValue)
            DispatcherQueue.TryEnqueue(() => HandleReminderDueRequest(pendingDue.Value));
        Novara.Services.ReminderDueRequest.StartListening(id => DispatcherQueue.TryEnqueue(() => HandleReminderDueRequest(id)));



        try
        {
            if (!Novara.Services.StickySync.HostRunning && Novara.Services.StickySync.HasDesktopCards())
                Novara.Services.StickySync.LaunchHost();
        }
        catch { }


        if (Content is FrameworkElement rootElem)
            App.ApplyLocalizedTexts(rootElem);

        NavMemoText.Text = App.GetString("Nav_Tab_Memo");
        NavFileText.Text = App.GetString("Nav_Tab_File");
        NavPlanText.Text = App.GetString("Nav_Tab_Plan");
        NavDiaryText.Text = App.GetString("Nav_Tab_Diary");


        WelcomeSubtitle.CharacterSpacing = App.CurrentLanguage switch
        {
            "zh-CN" or "zh-TW" => 850,
            "en-US" => 150,
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




            if (App.Store is { IsLoaded: true }) ShowMainContent();
        }




        if (App.Store?.Database.AppSettings.VisibleTabs is { Count: > 0 } tabs)
            ApplyVisibleTabs(tabs.ToHashSet());


        Services.AutoBackupService.SyncAutoBackupTimer(DispatcherQueue);


        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        var windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hwnd);
        _appWindow = AppWindow.GetFromWindowId(windowId);
        _appWindow.Closing += AppWindow_Closing;
        UpdateWorkspaceSwitcherPosition();








        this.Activated += (_, args) =>
        {
            if (LockScreenFrame.Visibility != Visibility.Visible ||
                LockScreenFrame.Content is not LockScreenPage page)
                return;

            if (args.WindowActivationState == WindowActivationState.Deactivated)
                page.ClearInput();
            else if (args.WindowActivationState == WindowActivationState.CodeActivated)
                page.FocusPasswordInput();
        };

        var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "logo.ico");
        try { if (File.Exists(iconPath)) _appWindow.SetIcon(iconPath); } catch { }


        CloseBehavior = App.Store?.Database.AppSettings.CloseBehavior ?? "直接退出";
        UpdateTrayIcon();


        App.UpdateTitleBarColors();





        if (loadResult?.Status == LoadStatus.Corrupted)
            ShowCorruptStoreDialog();
        else if (loadResult?.Status == LoadStatus.IoError)
            ShowIoErrorDialog();


        if (App.Store is { IsEncrypted: true }) ShowLockScreen();


        HookNavHoverEvents();


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






private void CreateTrayIcon()
    {
        if (_trayIcon != null) return;










        System.Drawing.Icon? icon = null;
        try
        {
            var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "128.ico");
            if (File.Exists(iconPath)) icon = new System.Drawing.Icon(iconPath);
        }
        catch { icon = null; }



        TaskbarIcon? created = null;
        try
        {
            var menu = new MenuFlyout { MenuFlyoutPresenterStyle = (Style)Application.Current.Resources["GlassMenuFlyoutPresenterStyle"] };
            menu.Items.Add(new MenuFlyoutItem { Text = App.GetString("Tray_ShowWindow"), Command = ShowWindowCommand });
            menu.Items.Add(new MenuFlyoutItem { Text = App.GetString("Tray_LockNow"), Command = new Services.RelayCommand(() => { if (LockScreenFrame.Visibility == Visibility.Visible) return;
                if (App.Store is { IsLoaded: true, IsEncrypted: true }) LockNow(); else App.ShowToast(App.GetString("Tray_LockNeedLock")); }) });
            menu.Items.Add(new MenuFlyoutSeparator());
            menu.Items.Add(new MenuFlyoutItem { Text = App.GetString("Tray_Exit"), Command = ExitCommand });

            created = new TaskbarIcon
            {
                ToolTipText = "Novara",
                ContextMenuMode = ContextMenuMode.PopupMenu,
                MenuActivation = PopupActivationMode.RightClick,
                NoLeftClickDelay = true,
                ContextFlyout = menu,
            };
            if (icon != null) created.Icon = icon;
            created.LeftClickCommand = new RelayCommand(ShowMainWindow);

            created.ForceCreate();
            _trayIcon = created;
        }
        catch
        {
            created?.Dispose();
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
        @"Local\Novara.SingleInstance.Dev";
#else
        @"Local\Novara.SingleInstance";
#endif
    private static Mutex? _singleInstanceMutex;





    public static bool TryAcquireSingleInstance()
    {
        _singleInstanceMutex = new Mutex(true, SingleInstanceMutexName, out bool createdNew);
        return createdNew;
    }



    public static void WakeExistingInstance()
    {







        var hwnd = FindWindow(null, "Novara");
        if (hwnd != IntPtr.Zero && !IsOwnProcessWindow(hwnd)) hwnd = IntPtr.Zero;
        if (hwnd != IntPtr.Zero)
        {
            ShowWindow(hwnd, SW_RESTORE);
            SetForegroundWindow(hwnd);
        }
        else
        {

            Services.ShowWindowRequest.Raise();
        }
    }



    public static bool AcquireSingleInstance()
    {
        if (TryAcquireSingleInstance()) return true;
        WakeExistingInstance();
        return false;
    }

    public static void ReleaseSingleInstance()
    {
        try { _singleInstanceMutex?.ReleaseMutex(); } catch { }
        _singleInstanceMutex = null;
    }





    private static bool IsOwnProcessWindow(IntPtr hwnd)
    {
        try
        {
            GetWindowThreadProcessId(hwnd, out var pid);
            if (pid == 0) return false;
            var mine = Environment.ProcessPath;
            var theirs = Services.McpClientIdentity.ResolveProcessImagePath((int)pid);
            return !string.IsNullOrEmpty(mine) && !string.IsNullOrEmpty(theirs)
                && string.Equals(mine, theirs, StringComparison.OrdinalIgnoreCase);
        }
        catch { return false; }
    }











    private static async Task<(bool Saved, bool Refused)> FlushEditorForExitAsync(DiaryEditorPage ed)
    {
        bool saved = false, timedOut = false, threw = false;
        try { saved = await ed.SaveCurrentDiaryAsync().WaitAsync(TimeSpan.FromSeconds(3)); }
        catch (TimeoutException) { timedOut = true; }




        catch { threw = true; }
        if (!saved && ed.HasUnsavedEdits)
        {
            App.ShowToast(App.GetString("Editor_SaveFail_Toast"));
            Services.CrashLogger.LogNote("EditorFlushOnExit", timedOut
                ? "editor save timed out after 3s - the last edit may not have been persisted"
                : threw
                    ? "editor save threw before answering - the last edit may not have been persisted"
                    : "editor save returned false with unsaved edits - the last edit was not persisted");
        }
        return (saved, !saved && !timedOut && !threw);
    }






private async void AppWindow_Closing(AppWindow sender, AppWindowClosingEventArgs args)
    {
        if (!_isTrayExit && CloseBehavior == "托盘驻留")
        {
            args.Cancel = true;
            sender.Hide();
            return;
        }



        if (_closingHandled)
        {
            if (!_closeAllowed) args.Cancel = true;
            return;
        }
        args.Cancel = true;
        _closingHandled = true;

        if (RootFrame.Content is DiaryEditorPage editor)
        {



            var (_, editorRefused) = await FlushEditorForExitAsync(editor);








            if (editorRefused && editor.HasUnsavedEdits)
            {
                _closeAllowed = false;
                _closingHandled = false;
                return;
            }
        }





        if (App.Store is { IsLoaded: false })
        {
            _closeAllowed = true;
            this.Close();
            return;
        }



        if (_restarting) { _closeAllowed = true; this.Close(); return; }


        if (_isTrayExit) { _closeAllowed = true; this.Close(); return; }
        try { if (App.Store?.SaveSync() == false) { _closeAllowed = false; _closingHandled = false; return; } }
        catch { _closeAllowed = false; _closingHandled = false; return; }
        _closeAllowed = true;
        this.Close();
    }



    private string _qcMode = "todo";
    private IntPtr _qcPrevForeground;
    private bool _qcAnimating;


    public void ApplyQuickCaptureHotkey()
    {
        Services.GlobalHotkeyService.Unregister(Services.GlobalHotkeyService.IdQuickCapture);
        var s = App.Store?.Database.AppSettings;
        if (s is not { QuickCaptureEnabled: true }) return;
        var (mods, vk) = Services.GlobalHotkeyService.ParsePreset(s.QuickCaptureHotkey);
        if (!Services.GlobalHotkeyService.Register(Services.GlobalHotkeyService.IdQuickCapture, mods, vk))
            App.ShowToast(App.GetString("QuickCapture_HotkeyConflict"));
    }

    private void OnQuickCaptureHotkey()
    {

        if (LockScreenFrame.Visibility == Visibility.Visible) { ShowMainWindow(); return; }
        ShowQuickCapture();
    }

    private void OnLockNowHotkey()
    {
        if (LockScreenFrame.Visibility == Visibility.Visible) return;
        LockNow();
    }

    private void ShowQuickCapture()
    {
        if (QuickCaptureOverlay.Visibility == Visibility.Visible) { QuickCaptureBox.Focus(FocusState.Programmatic); QuickCaptureBox.SelectAll(); return; }
        _qcPrevForeground = GetForegroundWindow();
        ShowMainWindow();
        QuickCaptureBox.Text = "";
        SetQcMode("todo");
        QuickCaptureHint.Text = App.GetString("QuickCapture_Hint");
        QuickCaptureOverlay.Visibility = Visibility.Visible;
        var sb = new Storyboard();
        var si = new DoubleAnimation { To = 1, Duration = TimeSpan.FromMilliseconds(250) }; Storyboard.SetTarget(si, QuickCaptureScrim); Storyboard.SetTargetProperty(si, "Opacity"); sb.Children.Add(si);
        var di = new DoubleAnimation { To = 1, Duration = TimeSpan.FromMilliseconds(300) }; Storyboard.SetTarget(di, QuickCaptureBar); Storyboard.SetTargetProperty(di, "Opacity"); sb.Children.Add(di);
        QuickCaptureTransform.ScaleX = 0.94; QuickCaptureTransform.ScaleY = 0.94; QuickCaptureTransform.TranslateY = 24;
        Motion.AddDialogShowTransform(sb, QuickCaptureTransform);
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
        Motion.AddDialogHideTransform(sb, QuickCaptureTransform);
        sb.Completed += (_, _) =>
        {
            QuickCaptureOverlay.Visibility = Visibility.Collapsed;
            _qcAnimating = false;
            if (_qcPrevForeground != IntPtr.Zero && _qcPrevForeground != WinRT.Interop.WindowNative.GetWindowHandle(this))
                SetForegroundWindow(_qcPrevForeground);
        };
        sb.Begin();
    }

    private void SetQcMode(string mode)
    {
        _qcMode = mode;

        QcModeMemo.Content = App.GetString("Nav_Tab_Memo");
        QcModeTodo.Content = App.GetString("Setting_Stats_Todos");
        QcModeNote.Content = App.GetString("Setting_Stats_Notes");

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
            if (t.Length == 0) return;
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




    internal void SetRestarting() => _restarting = true;

    public async void ExitApp(bool saveFirst)
    {
        if (_exitSaving) return;
        _exitSaving = true;
        _isTrayExit = true;
        try
        {




            if (RootFrame.Content is DiaryEditorPage pendingEditor)
            {
                await FlushEditorForExitAsync(pendingEditor);
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




            Services.GlobalHotkeyService.UnregisterAll();
            try
            {
                RemoveTrayIcon();
            }
            catch
            {

            }
            ReleaseSingleInstance();
            _closingHandled = false;
            _closeAllowed = true;
            Close();
        }
        finally { _exitSaving = false; }
    }


    private const int SW_RESTORE = 9;

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr FindWindow(string? lpClassName, string lpWindowName);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();


    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);





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
        var h = OpenInputDesktop(0, false, 0x0100);
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
        var relocked = false;
        try
        {
            if (RootFrame.Content is DiaryEditorPage ed)
            {
                await FlushEditorForExitAsync(ed);
            }
            try { App.Store!.SaveSync(); } catch { }
            App.RelockStore();
            relocked = true;
            BindStoreSaveFailed();
            ShowLockScreen();


        }
        catch (Exception ex)
        {









            if (relocked)
            {
                System.Diagnostics.Debug.WriteLine($"硬锁失败（内存库已锁定但锁屏未显示，重试锁屏）: {ex}");
                try { ShowLockScreen(); }
                catch (Exception ex2)
                {
                    System.Diagnostics.Debug.WriteLine($"硬锁失败（锁屏兜底也失败，退出进程）: {ex2}");
                    Application.Current.Exit();
                }
            }
            else
            {
                System.Diagnostics.Debug.WriteLine($"硬锁失败（未能锁定，等待重试）: {ex}");
            }
        }
        finally { _lockInProgress = false; }
    }

    private void ShowLockScreen()
    {
        var page = new LockScreenPage();
        page.UnlockSucceeded += HideLockScreen;

        page.CorruptDetected += ShowCorruptStoreDialog;
        page.IoErrorDetected += ShowIoErrorDialog;
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





            if (McpPermissions.EnsureMigrated(store.Database.AppSettings))
                _ = store.SaveAsync();


            if (store.Database.AppSettings.VisibleTabs is { Count: > 0 } unlockedTabs)
                ApplyVisibleTabs(unlockedTabs.ToHashSet());
            TrashPage.CleanupExpired();
            Services.AutoBackupService.SyncAutoBackupTimer(DispatcherQueue);
            if (Content is FrameworkElement root) App.ApplyLocalizedTexts(root);



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




        if (App.Store is { NeedsFormatMigration: true } && !App.Store.Database.AppSettings.GcmMigrationRejected)
            ShowMigrateFormatDialog();
        else if (App.Store is { NeedsKdfMigration: true } && !App.Store.Database.AppSettings.KdfMigrationRejected)
            ShowMigrateFormatDialog();





        if (_themeChangedWhileLocked) { _themeChangedWhileLocked = false; if (App.CurrentTheme == "跟随系统") ShowThemeRestartOverlay(); }


        ApplyQuickCaptureHotkey();
    }










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


    private bool _migrateFailed;
    private bool _migrateIsKdf;

    public void ShowMigrateFormatDialog()
    {
        _migrateFailed = false;


        _migrateIsKdf = App.Store is { NeedsKdfMigration: true };
        MigrateFormatMessage.Text = App.GetString(_migrateIsKdf ? "Security_Migrate_Kdf_Body" : "Security_Migrate_Body");
        MigrateFormatMessage.Foreground = App.GetBrush("AppTextSecondaryBrush");
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
        Motion.AddDialogShowTransform(sb, MigrateFormatDialogTransform);
        sb.Begin();
    }

    private void HideMigrateFormatDialog()
    {
        var sb = new Storyboard();
        var so = new DoubleAnimation { To = 0, Duration = TimeSpan.FromMilliseconds(200) };
        Storyboard.SetTarget(so, MigrateFormatScrim); Storyboard.SetTargetProperty(so, "Opacity"); sb.Children.Add(so);
        var d = new DoubleAnimation { To = 0, Duration = TimeSpan.FromMilliseconds(200) };
        Storyboard.SetTarget(d, MigrateFormatDialog); Storyboard.SetTargetProperty(d, "Opacity"); sb.Children.Add(d);
        Motion.AddDialogHideTransform(sb, MigrateFormatDialogTransform);
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


            if (!_migrateIsKdf && App.Store is { NeedsKdfMigration: true } && !App.Store.Database.AppSettings.KdfMigrationRejected)
            {
                ShowMigrateFormatDialog();
                return;
            }
            HideMigrateFormatDialog();
            return;
        }

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



        DispatcherQueue.TryEnqueue(() =>
        {
            if (!_menuExpanded) return;
            if (_menuOpen) return;
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


    private void ApplyMenuProgress(double p)
    {
        MenuButton.Width = MenuBarW + (MenuBtnW - MenuBarW) * p;
        MenuButton.Height = MenuBarH + (MenuBtnH - MenuBarH) * p;
        MenuButton.CornerRadius = new CornerRadius(MenuBarR + (MenuBtnR - MenuBarR) * p);
        MenuPathIcon.Opacity = p;
    }



    private void OnMenuRendering(object? sender, object e)
    {

        if (_menuAnimating)
        {
            double elapsed = _menuAnimWatch?.Elapsed.TotalMilliseconds ?? 1000;
            double t = Math.Clamp(elapsed / MenuTransitionMs, 0.0, 1.0);
            double eased = 1 - Math.Pow(1 - t, 3);
            _menuProgress = _menuAnimFrom + (_menuAnimTo - _menuAnimFrom) * eased;
            ApplyMenuProgress(_menuProgress);
            MenuButtonTranslate.X = MenuAnchorX;
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
            MenuButtonTranslate.X = MenuAnchorX + s * MenuBreath;
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


        if (!_menuExpanded) { ExpandMenu(); return; }

        var cv = Microsoft.UI.Xaml.Markup.XamlBindingHelper.ConvertValue;
        var menu = new MenuFlyout
        {
            MenuFlyoutPresenterStyle = (Style)Application.Current.Resources["GlassMenuFlyoutPresenterStyle"]
        };
        _menuOpen = true;
        menu.Closed += (_, _) =>
        {
            _menuOpen = false;
            DispatcherQueue.TryEnqueue(CollapseMenu);
        };


        var toolsGroup = new GeometryGroup();
        foreach (var p in IconData.Tools)
            toolsGroup.Children.Add((Geometry)cv(typeof(Geometry), p));
        var toolsItem = new MenuFlyoutItem
        {
            Style = (Style)Application.Current.Resources["GlassMenuFlyoutItemStyle"],
            Text = App.GetString("Menu_Tools"),
            Icon = new PathIcon { Data = toolsGroup, Foreground = App.GetBrush("IconForegroundBrush") },
            KeyboardAcceleratorTextOverride = "Ctrl+T"
        };
        toolsItem.Click += (_, _) => NavigateToTools();
        menu.Items.Add(toolsItem);


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


        var trashItem = new MenuFlyoutItem
        {
            Style = (Style)Application.Current.Resources["GlassMenuFlyoutItemStyle"],
            Text = App.GetString("Trash_Title"),
            Icon = new PathIcon { Data = (Geometry)cv(typeof(Geometry), IconData.Delete), Foreground = App.GetBrush("IconForegroundBrush") },
            KeyboardAcceleratorTextOverride = "Ctrl+Shift+Backspace"
        };
        trashItem.Click += (_, _) => OpenTrashPage();
        menu.Items.Add(trashItem);


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


    public void OpenTrashPage()
    {
        if (ReferenceEquals(RootFrame.Content, _trashPage)) return;
        _preTrashContent = RootFrame.Content;
        _trashPage ??= new TrashPage();
        TrashPage.CleanupExpired();
        _trashPage.Refresh();
        var prevContent = RootFrame.Content;
        RootFrame.Content = _trashPage;
        PlayPageIn(prevContent);
        _trashPage.StealFocus();
        NavBar.Visibility = Visibility.Collapsed;
        BottomToolbarPanel.Visibility = Visibility.Collapsed;
        CustomTitleBar.Visibility = Visibility.Collapsed;
    }







    private void InvalidateTrashStaleTabs()
    {
        if (!_trashChanged) return;
        _trashChanged = false;
        _memoPage = new BasicMemoPage();
        _planPage = new PlanPage();
        _filePathPage = new FilePathPage();
        _diaryPage = new DiaryPage();
        _memoStale = _planStale = _fileStale = _diaryStale = false;
    }






    private void MarkTabPagesStale()
    {
        _memoStale = _memoPage is not null;
        _planStale = _planPage is not null;
        _fileStale = _filePathPage is not null;
        _diaryStale = _diaryPage is not null;
    }









    private object? FreshIfStale(object? content)
    {
        if (content is null) return null;
        if (ReferenceEquals(content, _memoPage) && _memoStale) { _memoPage = new BasicMemoPage(); _memoStale = false; return _memoPage; }
        if (ReferenceEquals(content, _planPage) && _planStale) { _planPage = new PlanPage(); _planStale = false; return _planPage; }
        if (ReferenceEquals(content, _filePathPage) && _fileStale) { _filePathPage = new FilePathPage(); _fileStale = false; return _filePathPage; }
        if (ReferenceEquals(content, _diaryPage) && _diaryStale) { _diaryPage = new DiaryPage(); _diaryStale = false; return _diaryPage; }
        return content;
    }







    public void RetireTabPage(Type pageType)
    {
        App.UiQueue?.TryEnqueue(() =>
        {
            if (pageType == typeof(BasicMemoPage)) _memoStale = _memoPage is not null;
            else if (pageType == typeof(PlanPage)) _planStale = _planPage is not null;
            else if (pageType == typeof(FilePathPage)) _fileStale = _filePathPage is not null;
            else if (pageType == typeof(DiaryPage)) _diaryStale = _diaryPage is not null;
        });
    }





    public void NotifyExternalDbMutation() => App.UiQueue?.TryEnqueue(MarkTabPagesStale);











    public void RefreshAfterSyncPull()
    {
        if (_veilCount > 0 || RootFrame.Content is DiaryEditorPage)
        {
            MarkTabPagesStale();
            return;
        }






        var shown = RootFrame.Content;
        if (_memoPage is not null && !ReferenceEquals(_memoPage, shown)) _memoPage = new BasicMemoPage();
        if (_planPage is not null && !ReferenceEquals(_planPage, shown)) _planPage = new PlanPage();
        if (_filePathPage is not null && !ReferenceEquals(_filePathPage, shown)) _filePathPage = new FilePathPage();
        if (_diaryPage is not null && !ReferenceEquals(_diaryPage, shown)) _diaryPage = new DiaryPage();
        _memoStale = _planStale = _fileStale = _diaryStale = false;

        switch (shown)
        {
            case BasicMemoPage: _memoPage = new BasicMemoPage(); RootFrame.Content = _memoPage; break;
            case FilePathPage: _filePathPage = new FilePathPage(); RootFrame.Content = _filePathPage; break;
            case PlanPage: _planPage = new PlanPage(); RootFrame.Content = _planPage; break;
            case DiaryPage: _diaryPage = new DiaryPage(); RootFrame.Content = _diaryPage; break;
        }

        UpdateWorkspaceSwitcher();
    }


    public void CloseTrashPage()
    {
        if (!ReferenceEquals(RootFrame.Content, _trashPage)) { _preTrashContent = null; return; }
        var prev = _preTrashContent;
        _preTrashContent = null;
        if (prev != null)
        {




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
            prev = FreshIfStale(prev);
            RootFrame.Content = prev;
            PlayPageIn(_trashPage);
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


    private void MaybeShowWelcomeCarousel()
    {
#if DEBUG
        ShowWelcomeCarousel();
#else
        if (App.Store?.Database.AppSettings is { HasCompletedCarousel: false }) ShowWelcomeCarousel();
#endif
    }

    private void ShowWelcomeCarousel()
    {
        _carouselPage = 0;
        BuildCarouselDots();
        SetCarouselPage(0);
        EnsureCarouselBlur();
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
        CarouselBlurHost.Visibility = Visibility.Collapsed;
        WelcomeCarousel.Opacity = 0;
        WelcomeCarousel.Visibility = Visibility.Collapsed;
        var s = App.Store?.Database.AppSettings;
        if (s != null && !s.HasCompletedCarousel)
        {
            s.HasCompletedCarousel = true;
            _ = App.Store?.SaveAsync();
        }
    }

    private void CarouselClose_Click(object sender, RoutedEventArgs e) => CloseWelcomeCarousel();








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


    private static Border MakeCarouselMenuSeparator()
        => new() { Height = 1, Background = App.GetBrush("AppBorderBrush"), Margin = new Thickness(4, 5, 4, 5) };





    private double _carouselFloatStart;

    private Microsoft.UI.Composition.SpriteVisual? _carouselBlurVisual;







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
            _chromeBlurVisual = null;
        }
    }

    private void StartCarouselFloat()
    {
        StopCarouselFloat();
        _carouselFloatStart = (double)System.Diagnostics.Stopwatch.GetTimestamp() / System.Diagnostics.Stopwatch.Frequency;
        _carouselDemoStart = 0;
        CompositionTarget.Rendering += CarouselFloatFrame;
    }

    private void CarouselFloatFrame(object? sender, object e)
    {


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



            _menuHoverLift[i] += (_menuHoverLiftTarget[i] - _menuHoverLift[i]) * 0.18;
            _menuHoverScale[i] += (_menuHoverScaleTarget[i] - _menuHoverScale[i]) * 0.18;
            transforms[i].Y = 5 * Math.Sin(2 * Math.PI * t / 3.0 + phases[i]) + _menuHoverLift[i];
            scales[i].ScaleX = scales[i].ScaleY = _menuHoverScale[i];
        }

        var tabPhases = _floatTabPhases!;
        var tabTransforms = _floatTabTransforms!;
        for (int i = 0; i < 4; i++)
            tabTransforms[i].Y = 6 * Math.Sin(2 * Math.PI * t / 3.0 + tabPhases[i]);
        ApplyCarouselDemo(t - _carouselDemoStart);
    }


    private double[]? _floatPhases;
    private Microsoft.UI.Xaml.Media.TranslateTransform[]? _floatTransforms;
    private Microsoft.UI.Xaml.Media.ScaleTransform[]? _floatScales;
    private double[]? _floatTabPhases;
    private Microsoft.UI.Xaml.Media.TranslateTransform[]? _floatTabTransforms;



    private readonly double[] _menuHoverLift = new double[4];
    private readonly double[] _menuHoverScale = { 1.0, 1.0, 1.0, 1.0 };
    private readonly double[] _menuHoverLiftTarget = new double[4];
    private readonly double[] _menuHoverScaleTarget = { 1.0, 1.0, 1.0, 1.0 };

    private void CarouselMenu_PointerEntered(object sender, PointerRoutedEventArgs e)
    {
        if (sender is FrameworkElement fe && int.TryParse(fe.Tag as string, out var i) && i >= 0 && i < 4)
        {
            _menuHoverLiftTarget[i] = -6;
            _menuHoverScaleTarget[i] = 1.05;
            Canvas.SetZIndex(fe, 10);
        }
    }

    private void CarouselMenu_PointerExited(object sender, PointerRoutedEventArgs e)
    {
        if (sender is FrameworkElement fe && int.TryParse(fe.Tag as string, out var i) && i >= 0 && i < 4)
        {
            _menuHoverLiftTarget[i] = 0;
            _menuHoverScaleTarget[i] = 1.0;
            Canvas.SetZIndex(fe, i);
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

        CarouselDemoCursorTranslate.X = DemoCursorStartX;
        CarouselDemoCursorTranslate.Y = DemoCursorStartY;
        CarouselDemoMenuFace.Width = 8;
        CarouselDemoMenuFace.Height = 48;
        CarouselDemoMenuFace.CornerRadius = new CornerRadius(4);
        CarouselDemoMenuIcon.Opacity = 0;
        CarouselDemoMenuTranslate.X = 6;



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







    private void BuildCarouselPage1()
    {
        CarouselP1Title.Text = App.GetString("Carousel_P1_Title");
        BuildCarouselP1Tab(CarouselP1BtnMemo, IconData.NavMemo, "Nav_Tab_Memo");
        BuildCarouselP1Tab(CarouselP1BtnFile, IconData.NavFile, "Nav_Tab_File");
        BuildCarouselP1Tab(CarouselP1BtnPlan, IconData.NavPlan, "Nav_Tab_Plan");
        BuildCarouselP1Tab(CarouselP1BtnDiary, IconData.NavDiary, "Nav_Tab_Diary");
        SetCarouselP1Selection(CarouselP1BtnMemo, "Carousel_P1_Memo");
    }


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

    private Microsoft.UI.Xaml.Media.ThemeShadow? _carouselP1Shadow;

    private void SetCarouselP1Selection(Button selected, string introKey)
    {




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
            b.Foreground = sel ? brandBrush : dimBrush;
            b.Translation = sel ? new System.Numerics.Vector3(0f, 0f, 20f) : System.Numerics.Vector3.Zero;
            SetNavOpacity(b, sel ? 1.0 : 0.6, NavBar.IsLoaded);
            if (b.Content is StackPanel sp && sp.Children.Count > 0 && sp.Children[0] is Microsoft.UI.Xaml.Shapes.Path icon)
                icon.Fill = sel ? brandBrush : iconBrush;
        }

        CarouselP1Intro.Text = App.GetString(introKey);
        CarouselP1Intro.Opacity = 0;
        var sb = new Storyboard();
        var oi = new DoubleAnimation { To = 1, Duration = TimeSpan.FromMilliseconds(200) };
        Storyboard.SetTarget(oi, CarouselP1Intro); Storyboard.SetTargetProperty(oi, "Opacity"); sb.Children.Add(oi);
        sb.Begin();
    }




    private void BuildCarouselPage2()
    {
        CarouselP2Title.Text = App.GetString("Carousel_P2_Title");
        CarouselP2Intro.Text = App.GetString("Carousel_P2_Desc");
        CarouselDemoMenuIcon.Data = App.CreateGeometry(IconData.Menu);
    }



    private const double DemoCursorStartX = 140, DemoCursorStartY = 150;
    private const double DemoCursorEndX = 600, DemoCursorEndY = 313;

    private static double DemoEaseInOut(double t) => t < 0.5 ? 2 * t * t : 1 - Math.Pow(-2 * t + 2, 2) / 2;
    private static double DemoCubicOut(double t) => 1 - Math.Pow(1 - Math.Clamp(t, 0, 1), 3);
    private static double DemoLerp(double a, double b, double k) => a + (b - a) * k;

    private double _carouselDemoStart;

    private double CarouselClockSeconds() => (double)System.Diagnostics.Stopwatch.GetTimestamp() / System.Diagnostics.Stopwatch.Frequency - _carouselFloatStart;








    private void ApplyCarouselDemo(double t)
    {
        double phase = t % 4.4;
        double p;
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

        CarouselDemoMenuFace.Width = 8 + (56 - 8) * p;
        CarouselDemoMenuFace.Height = 48 + (32 - 48) * p;
        CarouselDemoMenuFace.CornerRadius = new CornerRadius(4 + (8 - 4) * p);
        CarouselDemoMenuIcon.Opacity = p;

        CarouselDemoMenuTranslate.X = p > 0.01 ? 6 : 6 + Math.Sin(t * 2 * Math.PI / 3.2) * 3;
    }






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





    private Grid[] CarouselPageList = System.Array.Empty<Grid>();

    private void BuildCarouselDots()
    {
        CarouselDots.Children.Clear();
        for (int i = 0; i < CarouselPageList.Length; i++)
        {
            var idx = i;


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


        Array.Fill(_menuHoverLiftTarget, 0);
        Array.Fill(_menuHoverScaleTarget, 1.0);
        if (index < 0 || index >= CarouselPageList.Length) return;
        _carouselPage = index;
        if (index == 2) _carouselDemoStart = CarouselClockSeconds();
        for (int i = 0; i < CarouselPageList.Length; i++)
            CarouselPageList[i].Visibility = i == index ? Visibility.Visible : Visibility.Collapsed;

        CarouselCloseButton.Visibility = index == CarouselPageList.Length - 1 ? Visibility.Visible : Visibility.Collapsed;

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
        if (delta < 0) SetCarouselPage(_carouselPage + 1);
        else if (delta > 0) SetCarouselPage(_carouselPage - 1);
        e.Handled = true;
    }

    private void ShowMainContent()
    {
        BottomToolbarPanel.Visibility = Visibility.Visible;
        CustomTitleBar.Visibility = Visibility.Visible;
        NavBar.Visibility = Visibility.Visible;

        var (tab, navButton) = FirstVisibleTab();
        NavigateToPage(tab);
        UpdateNavSelection(navButton);
        UpdateNavBarVisibility();
        UpdateWorkspaceSwitcher();
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




    private bool IsTabVisible(string requestedTag)
    {
        if (App.Store?.Database.AppSettings.VisibleTabs is { Count: > 0 } vt)
            return vt.Contains(TagToTabName(requestedTag));
        return true;
    }


    private void RefuseHiddenTabAction(string requestedTag)
    {
        ShowMainWindow();
        App.ShowToast(App.GetString("Common_Tab_Hidden_Toast"));
        System.Diagnostics.Debug.WriteLine($"N4W-03: action for hidden tab '{requestedTag}' dropped");
    }

    private void WelcomeOverlay_Tapped(object sender, Microsoft.UI.Xaml.Input.TappedRoutedEventArgs e)
    {


        if (App.Store is not { IsLoaded: true }) return;
        if (_welcomeFading) return;
        _welcomeFading = true;
        WelcomeHintBreathing.Pause();

        var fadeOut = new Storyboard();
        var oa = new DoubleAnimation { To = 0, Duration = TimeSpan.FromMilliseconds(400) };
        Storyboard.SetTarget(oa, WelcomeOverlay);
        Storyboard.SetTargetProperty(oa, "Opacity");
        fadeOut.Children.Add(oa);
        fadeOut.Completed += (s, ev) =>
        {
            _welcomeFading = false;
            WelcomeOverlay.Visibility = Visibility.Collapsed;

            if (App.Store != null)
            {
                App.Store.Database.AppSettings.HasCompletedWelcome = true;
                App.Store.SaveSync();
            }
            ShowMainContent();
            MaybeShowWelcomeCarousel();
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


    public void HandleEditRequest(Guid id)
    {
        if (App.Store is not { IsLoaded: true }) return;
        if (WelcomeOverlay.Visibility == Visibility.Visible) return;
        if (!IsTabVisible("Plan")) { RefuseHiddenTabAction("Plan"); return; }
        if (RestorePendingBlocksNavigation()) { RefuseRestorePending(); return; }
        FlushEditorDirty();
        ShowMainWindow();
        var (editTab, editNav) = ResolveNavTarget("Plan");
        NavigateToPage(editTab);
        UpdateNavSelection(editNav);
        RestoreChromeFor(RootFrame.Content);


        RetryOpenEdit(id, 0);
    }

    private void RetryOpenEdit(Guid id, int attempt)
    {
        if (attempt > 20) return;


        if (_planPage?.OpenEditNoteById(id) == true) return;
        if (_planPage?.OpenEditTodoById(id) == true) return;
        DispatcherQueue.TryEnqueue(async () =>
        {
            await System.Threading.Tasks.Task.Delay(200);
            RetryOpenEdit(id, attempt + 1);
        });
    }


    public void HandleAddPathRequest(string path)
    {
        if (App.Store is not { IsLoaded: true }) return;
        if (WelcomeOverlay.Visibility == Visibility.Visible) return;
        if (!IsTabVisible("File")) { RefuseHiddenTabAction("File"); return; }
        if (RestorePendingBlocksNavigation()) { RefuseRestorePending(); return; }
        FlushEditorDirty();
        ShowMainWindow();
        var (pathTab, pathNav) = ResolveNavTarget("File");
        NavigateToPage(pathTab);
        UpdateNavSelection(pathNav);
        RestoreChromeFor(RootFrame.Content);
        _filePathPage?.OpenNewPathDialogWith(path);
    }



    public void HandleImportMdRequest(string filePath)
    {
        if (App.Store is not { IsLoaded: true }) return;
        if (WelcomeOverlay.Visibility == Visibility.Visible) return;
        if (!IsTabVisible("Diary")) { RefuseHiddenTabAction("Diary"); return; }
        if (RestorePendingBlocksNavigation()) { RefuseRestorePending(); return; }
        FlushEditorDirty();
        ShowMainWindow();
        var (diaryTab, diaryNav) = ResolveNavTarget("Diary");
        NavigateToPage(diaryTab);
        UpdateNavSelection(diaryNav);
        RestoreChromeFor(RootFrame.Content);
        _ = _diaryPage?.ImportDocumentFromPath(filePath);
    }


    public void HandleReminderEditRequest(Novara.Services.PendingReminderEdit p)
    {
        if (App.Store is not { IsLoaded: true }) return;
        if (WelcomeOverlay.Visibility == Visibility.Visible) return;
        if (!IsTabVisible("Plan")) { RefuseHiddenTabAction("Plan"); return; }
        if (RestorePendingBlocksNavigation()) { RefuseRestorePending(); return; }
        FlushEditorDirty();
        ShowMainWindow();
        var (remTab, remNav) = ResolveNavTarget("Plan");
        NavigateToPage(remTab);
        UpdateNavSelection(remNav);
        RestoreChromeFor(RootFrame.Content);
        _planPage?.OpenReminderEdit(p);
    }


    public void HandleReminderDueRequest(Guid id)
    {
        if (App.Store is not { IsLoaded: true }) return;
        if (WelcomeOverlay.Visibility == Visibility.Visible) return;
        if (!IsTabVisible("Plan")) { RefuseHiddenTabAction("Plan"); return; }
        if (RestorePendingBlocksNavigation()) { RefuseRestorePending(); return; }
        FlushEditorDirty();
        ShowMainWindow();
        var (planTab, planNav) = ResolveNavTarget("Plan");
        NavigateToPage(planTab);
        UpdateNavSelection(planNav);
        RestoreChromeFor(RootFrame.Content);
        _planPage?.ShowReminderDueById(id);
    }



    private bool RestorePendingBlocksNavigation()
        => App.Store?.IsSaveSuppressed == true;

    private void RefuseRestorePending()
    {
        ShowMainWindow();
        App.ShowToast(App.GetString("Common_Toast_RestorePending"));
    }




    private void FlushEditorDirty()
    {
        if (RootFrame.Content is DiaryEditorPage ed)
        {



            _ = FlushEditorForExitAsync(ed);
        }
    }


    public void OpenSearchPage()
    {


        if (LockScreenFrame.Visibility == Visibility.Visible || WelcomeOverlay.Visibility == Visibility.Visible) return;
        if (RestorePendingBlocksNavigation()) { RefuseRestorePending(); return; }
        if (ReferenceEquals(RootFrame.Content, _searchPage)) { _searchPage?.FocusSearch(); return; }
        FlushEditorDirty();
        _preSearchContent = RootFrame.Content;
        _searchPage ??= new SearchPage();
        _searchPage.ApplyTexts();
        _searchPage.ResetSearch();
        var prevContent = RootFrame.Content;
        RootFrame.Content = _searchPage;
        PlayPageIn(prevContent);

        NavBar.Visibility = Visibility.Collapsed;
        BottomToolbarPanel.Visibility = Visibility.Collapsed;
        CustomTitleBar.Visibility = Visibility.Collapsed;
        _searchPage.FocusSearch();
    }


    public void CloseSearchPage()
    {
        if (!ReferenceEquals(RootFrame.Content, _searchPage)) { _preSearchContent = null; return; }
        var prev = _preSearchContent;
        _preSearchContent = null;
        if (prev != null)
        {
            prev = FreshIfStale(prev);
            RootFrame.Content = prev;
            PlayPageIn(_searchPage);
            RestoreChromeFor(prev);
        }
    }

    private void SyncNavFromContent(object content)
    {
        if (ReferenceEquals(content, _memoPage)) UpdateNavSelection(NavMemo);
        else if (ReferenceEquals(content, _filePathPage)) UpdateNavSelection(NavFile);
        else if (ReferenceEquals(content, _planPage)) UpdateNavSelection(NavPlan);
        else if (ReferenceEquals(content, _diaryPage)) UpdateNavSelection(NavDiary);
    }


    private static bool IsFullscreenPage(object? content)
        => content is SettingsPage or ToolsPage or DiaryEditorPage or SearchPage or TrashPage;




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



    private void RestoreChromeFor(object? page)
    {
        if (page == null) return;
        SyncNavFromContent(page);
        bool fullscreen = IsFullscreenPage(page);
        BottomToolbarPanel.Visibility = fullscreen ? Visibility.Collapsed : Visibility.Visible;
        CustomTitleBar.Visibility = fullscreen ? Visibility.Collapsed : Visibility.Visible;
        UpdateNavBarVisibility();
    }


    public void RunPaletteCommand(string cmd)
    {

        if (RestorePendingBlocksNavigation()) { App.ShowToast(App.GetString("Common_Toast_RestorePending")); return; }
        CloseSearchPage();
        switch (cmd)
        {
            case "new_memo":
                { var (t, n) = ResolveNavTarget("Memo"); NavigateToPage(t); UpdateNavSelection(n); }
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
                NavigateToEditor();
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
                else App.ShowToast(App.GetString("Tray_LockNeedLock"));
                break;
        }
    }


    public void SearchJump(string kind, string id)
    {

        if (RestorePendingBlocksNavigation()) { App.ShowToast(App.GetString("Common_Toast_RestorePending")); return; }


        string tag = kind switch { "memo" => "Memo", "path" => "File", "todo" => "Plan", "note" => "Plan", "diary" => "Diary", _ => "" };
        if (tag != "" && !IsTabVisible(tag)) { App.ShowToast(App.GetString("Common_Tab_Hidden_Toast")); return; }
        CloseSearchPage();
        EnsureTargetWorkspaceVisible(kind, id);
        switch (kind)
        {
            case "memo":
                { var (t, n) = ResolveNavTarget("Memo"); NavigateToPage(t); UpdateNavSelection(n); }
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




        if (string.IsNullOrEmpty(wsId) && !string.IsNullOrEmpty(App.CurrentWorkspaceId))
        {
            App.CurrentWorkspaceId = "";
            UpdateWorkspaceSwitcher();
            RefreshWorkspaceFilters();
        }
    }






    private async void RetryFlashBy(Func<bool> flash, int attempt)
    {
        if (attempt > 20) return;
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




        if (_trashChanged && ReferenceEquals(RootFrame.Content, _trashPage))
            InvalidateTrashStaleTabs();
        var previous = RootFrame.Content;

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
            if (_pageInBoard != sb) return;
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
                () => { if (pending != null && NavBar.Visibility == Visibility.Visible && ReferenceEquals(_selectedNavButton, pending) && pending.Visibility == Visibility.Visible && pending.ActualWidth >= 1) UpdateNavIndicator(pending); });
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
            double rightInset = _appWindow.TitleBar.RightInset;
            double scale = Content.XamlRoot?.RasterizationScale ?? 1.0;
            double rightMargin = rightInset / scale + 8;
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
        manage.Click += (_, _) => NavigateToTools(openWorkspace: true);
        menu.Items.Add(manage);

        menu.ShowAt(WorkspaceSwitcherButton, new Windows.Foundation.Point(0, WorkspaceSwitcherButton.ActualHeight + 4));
    }

    private void UpdateNavBarVisibility()
    {


        if (WelcomeOverlay.Visibility == Visibility.Visible)
        {
            NavBar.Visibility = Visibility.Collapsed;
            return;
        }

        if (RootFrame.Content is SettingsPage or ToolsPage or DiaryEditorPage or SearchPage or TrashPage)
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
        _memoStale = _planStale = _fileStale = _diaryStale = false;
        _diaryEditorPage?.Shutdown();
        _diaryEditorPage = null!;
        _settingsPage = null!;
        _toolsPage = null!;
        _previousContent = null;



        _searchPage = null;
        _trashPage = null;
        _preSearchContent = null;
        _preTrashContent = null;
        _trashChanged = false;
        if (RootFrame.Content is SettingsPage or DiaryEditorPage or SearchPage or TrashPage)
        {
            var (tab, nav) = ResolveNavTarget(_selectedNavButton?.Tag as string ?? "Memo");
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
            _diaryPage = (DiaryPage)FreshIfStale(_diaryPage)!;


            RootFrame.Content = _diaryPage;



            UpdateNavBarVisibility();
            BottomToolbarPanel.Visibility = Visibility.Visible;
            CustomTitleBar.Visibility = Visibility.Visible;
            UpdateNavSelection(NavDiary);
            _diaryEditorPage?.ClearContent();

            var fadeIn = new Storyboard();
            var oi = new DoubleAnimation { To = 1, Duration = TimeSpan.FromMilliseconds(200), EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
            Storyboard.SetTarget(oi, RootFrame);
            Storyboard.SetTargetProperty(oi, "Opacity");
            fadeIn.Children.Add(oi);
            fadeIn.Begin();
        };
        fadeOut.Begin();
    }


    private void NavigateToTools(bool openWorkspace = false)
    {
        _toolsPage ??= new ToolsPage();
        _previousContent = RootFrame.Content;
        BottomToolbarPanel.Visibility = Visibility.Collapsed;

        var fadeOut = new Storyboard();
        var oa = new DoubleAnimation { To = 0, Duration = TimeSpan.FromMilliseconds(120), EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
        Storyboard.SetTarget(oa, RootFrame);
        Storyboard.SetTargetProperty(oa, "Opacity");
        fadeOut.Children.Add(oa);
        fadeOut.Completed += (s, e) =>
        {
            RootFrame.Content = _toolsPage;
            _toolsPage.StealFocus();
            NavBar.Visibility = Visibility.Collapsed;
            BottomToolbarPanel.Visibility = Visibility.Collapsed;
            CustomTitleBar.Visibility = Visibility.Collapsed;
            if (openWorkspace) _toolsPage.OpenWorkspaceManagement();
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
            _settingsPage.StealFocus();
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


                _previousContent = FreshIfStale(_previousContent);
                RootFrame.Content = _previousContent;
            }



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



    public void ShowCorruptStoreDialog()
    {
        StoreCorruptDangerIcon.Data = App.CreateGeometry(IconData.Danger);
        _corruptDialogIsIoError = false;
        StoreCorruptTitleText.Text = App.GetString("Store_Corrupt_Title");
        StoreCorruptDetailText.Text = App.GetString("Store_Corrupt_Detail");
        StoreCorruptConfirmText.Text = App.GetString("Store_Corrupt_RebuildButton");
        StoreCorruptConfirmButton.Background = new SolidColorBrush(Windows.UI.Color.FromArgb(0xCC, 0xFF, 0x45, 0x45));
        StoreCorruptConfirmButton.Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(0xFF, 0xFF, 0xFF, 0xFF));
        ShowCorruptDialogCore();
    }



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
        if (_animCorruptHide) return;
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
        if (_corruptDialogIsIoError) { HideCorruptStoreDialog(); return; }



        try { AutoBackupService.CreateSnapshot(isManual: true); } catch { }



        var reset = App.Store?.ResetDatabase();
        if (reset == null || reset.Status != LoadStatus.EmptyCreated)
        {
            HideCorruptStoreDialog();
            return;
        }
        try
        {
            Novara.Services.PasswordService.Delete();
            Novara.Services.PasswordService.DeleteLockout();
            Novara.Services.WindowsHelloService.Disable();
            Novara.Services.SyncService.MarkRestorePendingConfirm();
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
                ShowMainContent();
            }
        }
    }

    private void ShowSaveFailedDialog()
    {
        _animSaveFailedHide = false;
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
        if (_animSaveFailedHide) return;
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






private void ShowThemeRestartOverlay()
    {
        if (_themeRestartAnimating) return;
        _themeRestartAnimating = true;
        ThemeRestartScrim.Opacity = 0;


        ThemeRestartDialog.Opacity = 0;
        ThemeRestartDialogTransform.ScaleX = 0.92; ThemeRestartDialogTransform.ScaleY = 0.92; ThemeRestartDialogTransform.TranslateY = 20;
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


        if (_themeRestartAnimating) return;
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
        if (_restarting) return;
        _restarting = true;





        if (RootFrame.Content is DiaryEditorPage pendingEditor)
        {
            await FlushEditorForExitAsync(pendingEditor);
            if (ThemeRestartOverlay.Visibility != Visibility.Visible) { _restarting = false; return; }
        }

        HideThemeRestartOverlay();


        if (App.Store?.SaveSync() == false)
        {
            _restarting = false;
            return;
        }

        try
        {
            var exe = Environment.ProcessPath;
            if (string.IsNullOrEmpty(exe)) { _restarting = false; return; }
            ReleaseSingleInstance();
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo { FileName = exe, UseShellExecute = true });
        }
        catch
        {

            AcquireSingleInstance();
            _restarting = false;
            return;
        }
        ExitApp(saveFirst: false);
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
    private Microsoft.UI.Xaml.DispatcherTimer? _mcpAuthorizeTimer;


    private readonly List<(string ClientPath, Action<bool> Result)> _mcpAuthorizeQueue = new();
    private const int McpAuthorizeQueueLimit = 8;

    private void QueueOrShowMcpAuthorize(string clientPath, Action<bool> onResult)
    {
        bool busy = _mcpAuthorizeResult != null || _mcpAuthorizeAnimating || McpAuthorizeOverlay.Visibility == Visibility.Visible;
        if (!busy) { ShowMcpAuthorizeDialog(clientPath, onResult); return; }
        if (_mcpAuthorizeQueue.Count >= McpAuthorizeQueueLimit) { onResult(false); return; }
        _mcpAuthorizeQueue.Add((clientPath, onResult));
    }




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
        Motion.AddDialogShowTransform(sb, McpAuthorizeDialogTransform);
        sb.Begin();
        StartMcpAuthorizeExpiry();
    }

    private void HideMcpAuthorizeDialog(bool allowed) => CloseMcpAuthorizeDialog(invokeResult: true, allowed);

    private void CloseMcpAuthorizeDialog(bool invokeResult, bool allowed)
    {
        StopMcpAuthorizeExpiry();
        if (_mcpAuthorizeAnimating) return;
        _mcpAuthorizeAnimating = true;
        var result = _mcpAuthorizeResult;
        _mcpAuthorizeResult = null;
        var sb = new Storyboard();
        var so = new DoubleAnimation { To = 0, Duration = TimeSpan.FromMilliseconds(200) };
        Storyboard.SetTarget(so, McpAuthorizeScrim); Storyboard.SetTargetProperty(so, "Opacity"); sb.Children.Add(so);
        var d = new DoubleAnimation { To = 0, Duration = TimeSpan.FromMilliseconds(200) };
        Storyboard.SetTarget(d, McpAuthorizeDialog); Storyboard.SetTargetProperty(d, "Opacity"); sb.Children.Add(d);
        Motion.AddDialogHideTransform(sb, McpAuthorizeDialogTransform);
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


        if (invokeResult) { try { result?.Invoke(allowed); } catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"MCP authorize callback late invoke dropped: {ex.Message}"); } }
    }

    private void McpAuthorizeAllow_Click(object sender, RoutedEventArgs e)
    {


        if (WelcomeOverlay.Visibility == Visibility.Visible) return;


        var path = McpAuthorizePath.Text;
        if (!string.IsNullOrEmpty(path) && App.Store != null)
        {
            McpService.SeedDefaultPermission(path);
            NavigateToTools();
            DispatcherQueue.TryEnqueue(() => _toolsPage?.OpenMcpPermEditor(path));
        }
        HideMcpAuthorizeDialog(true);
    }
    private void McpAuthorizeDeny_Click(object sender, RoutedEventArgs e) => HideMcpAuthorizeDialog(false);
}
