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
    private SealPage _sealPage = null!;
    private bool _sealMode;
    private TextBlock? _sealReturnLabel;
    private Button? _selectedNavButton;
    private Microsoft.UI.Xaml.Media.ThemeShadow? _navShadow;
    private Microsoft.UI.Xaml.Media.ThemeShadow? _sealNavShadow;



    private sealed class NavFadeRec { public Microsoft.UI.Xaml.Media.Animation.Storyboard Sb = null!; public double Val; }
    private static readonly System.Collections.Generic.Dictionary<Microsoft.UI.Xaml.UIElement, NavFadeRec> _navFades = new();
    private object? _previousContent;
    private SearchPage? _searchPage;
    private object? _preSearchContent;
    private TrashPage? _trashPage;
    private object? _preTrashContent;
    private bool _trashChanged;





    private bool _memoStale, _planStale, _fileStale, _diaryStale;




    private bool _sideOpen;
    private bool _sideAnimating;
    private double _sideProgress;
    private double _sideFrom, _sideTo;
    private double _sideFinalTarget;
    private System.Diagnostics.Stopwatch? _sideWatch;
    private FrameworkElement? _sideRoot;
    private Windows.Foundation.Point _lastPointerPos;
    private double _edgeSign = -1.0;
    private Windows.Foundation.Rect _panelHome;
    private const double SidePhaseMs = 220;


    public void MarkTrashChanged() => _trashChanged = true;

    private Border? _toast;
    private TextBlock? _toastText;


    private const int ToastDefaultHoldMilliseconds = 1500;
    private int _toastHoldMilliseconds = ToastDefaultHoldMilliseconds;


    private bool _toastPersistent;


    private const string ToastCheckIconPath = "M929.28 189.44l-417.28 417.28-158.208-158.208-53.248 53.248 184.832 184.832 26.624 25.6 26.624-25.6 443.904-443.904-53.248-53.248z m-417.28-158.72c-266.24 0-480.768 214.528-480.768 480.768s214.528 480.768 480.768 480.768 480.768-214.528 480.768-480.768c0-51.712-7.168-103.424-25.6-151.552l-59.904 58.88c7.168 29.696 11.776 59.392 11.776 92.672 0 225.792-181.248 407.04-407.04 407.04s-407.04-181.248-407.04-407.04 181.248-407.04 407.04-407.04c111.104 0 210.432 44.032 281.088 114.688L844.8 167.424c-84.992-84.992-203.264-136.704-332.8-136.704z";








    public void ShowToast(string message, ToastTone tone = ToastTone.Success, bool persistent = false)
    {
        var holdMilliseconds = _toastHoldMilliseconds;
        _toastHoldMilliseconds = ToastDefaultHoldMilliseconds;
        if (string.IsNullOrWhiteSpace(message)) return;
        if (_toast != null) { ToastHost.Children.Remove(_toast); _toast = null; _toastText = null; }

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
            Child = tone == ToastTone.Error
                ? new PathIcon
                {
                    Data = App.CreateGeometry(IconData.Close),
                    Foreground = App.GetBrush("AppDangerTextBrush"),
                }
                : new PathIcon
                {
                    Data = App.CreateGeometry(ToastCheckIconPath),
                    Foreground = App.GetBrush("AppPrimaryButtonBrush"),
                },
        };
        row.Children.Add(icon);
        var text = new TextBlock
        {
            Text = message,
            FontSize = 14,
            Foreground = App.GetBrush("AppTextPrimaryBrush"),
            VerticalAlignment = VerticalAlignment.Center,
        };
        _toastText = text;
        row.Children.Add(text);
        toast.Child = row;
        toast.RenderTransform = new TranslateTransform { Y = 12 };
        _toast = toast;
        _toastPersistent = persistent;
        ToastHost.Children.Add(toast);

        var sb = new Storyboard();
        var fadeIn = new DoubleAnimation { To = 1, Duration = TimeSpan.FromMilliseconds(200) };
        Storyboard.SetTarget(fadeIn, toast); Storyboard.SetTargetProperty(fadeIn, "Opacity"); sb.Children.Add(fadeIn);
        var slide = new DoubleAnimation { To = 0, Duration = TimeSpan.FromMilliseconds(200) };
        Storyboard.SetTarget(slide, toast.RenderTransform); Storyboard.SetTargetProperty(slide, "Y"); sb.Children.Add(slide);
        if (persistent) { sb.Begin(); return; }

        sb.Completed += (_, _) =>
        {
            var fadeOut = new Storyboard();
            var fo = new DoubleAnimation { To = 0, Duration = TimeSpan.FromMilliseconds(300), BeginTime = TimeSpan.FromMilliseconds(Math.Max(0, holdMilliseconds)) };
            Storyboard.SetTarget(fo, toast); Storyboard.SetTargetProperty(fo, "Opacity"); fadeOut.Children.Add(fo);
            fadeOut.Completed += (_, _) => { if (_toast == toast) { ToastHost.Children.Remove(toast); _toast = null; _toastText = null; } };
            fadeOut.Begin();
        };
        sb.Begin();
    }



    public void UpdateToast(string message)
    {
        if (string.IsNullOrWhiteSpace(message)) return;
        if (_toast is null)
        {


            if (SealToast.IsActive) ShowToast(message, persistent: true);
            return;
        }
        if (_toastText is null || !_toastPersistent) return;
        _toastText.Text = message;
    }


    public void ShowTimedToast(string message, int holdMilliseconds)
    {
        _toastHoldMilliseconds = holdMilliseconds;
        ShowToast(message);
    }



    public void ClearToast()
    {
        if (_toast is null || !_toastPersistent) return;
        ToastHost.Children.Remove(_toast);
        _toast = null;
        _toastText = null;
        _toastPersistent = false;
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
    private bool _themeRestartHiding;
    private Storyboard? _themeRestartHideSb;

    private bool _autoUpdateAnimating;
    private bool _autoUpdateDismissPending;


    private readonly Dictionary<Grid, Border> _dialogMap = new();
    private readonly DialogUi.DialogWidthFit _dialogWidthFit = new();
    private bool _autoUpdateHiding;
    private Storyboard? _autoUpdateHideSb;
    private Novara.Services.UpdateService.LatestRelease? _autoUpdateRelease;

    private bool _isTrayExit;
    private bool _restarting;
    private bool _themeRestartCancelled;
    private bool _exitSaving;
    private bool _welcomeFading;
    private Services.SandShatter? _shatter;
    private bool _shatterWarming;
    private Storyboard? _shatterFade;
    private Microsoft.UI.Dispatching.DispatcherQueueTimer? _shatterLockWatch;
    private Task<bool>? _welcomeSaveTask;
    private bool _carouselPrepared;
    private bool _closeAllowed;
    private bool _animCorruptHide;
    private bool _rebuildInFlight;
    private bool _animSaveFailedHide;
    private Storyboard? _corruptHideSb;
    private Storyboard? _saveFailedHideSb;
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
        App.ApplyAutomationNames(this);

        if (App.CurrentLanguage == "en-US")
        {
            WelcomeSubtitle.FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Assets/Fonts/Comfortaa.ttf#Comfortaa");
        }
        CarouselPageList = new[] { CarouselPage0, CarouselPage1, CarouselPage2, CarouselPage3, CarouselPageSeal, CarouselPage4, CarouselPage5, CarouselPage6 };
        SealGuidePageList = new[] { SealGuidePage1, SealGuidePage2, SealGuidePage3, SealGuidePage4, SealGuidePage5 };
        SealGuideBleed.SizeChanged += (_, e) =>
        {
            if (_sealGuidePage == 1 && e.NewSize.Width + 952 > _sealGuideMarqueeCapacity) BuildSealGuideMarquee();
        };
        SealGuideMarqueeHost.SizeChanged += (_, _) => SealGuideMarqueeHost.Clip = new RectangleGeometry

        {
            Rect = new Windows.Foundation.Rect(0, 0, SealGuideMarqueeHost.ActualWidth, SealGuideMarqueeHost.ActualHeight)
        };
        EnsureSealGuidePage1Art();
        BuildCarouselChrome();



        Services.DialogDepth.AttachContainer((Grid)Content, driveChrome: false);


        Services.DialogDepth.AttachContainer(SealGuideOverlay, driveChrome: false);

        ApplyStartupTheme();

        this.SizeChanged += (_, e) => UpdateWelcomeLayout(e.Size.Width, e.Size.Height);
        this.SizeChanged += (_, _) => UpdateWorkspaceSwitcherPosition();
        this.SizeChanged += (_, _) => { if (_sideOpen) RecachePanelHome(); };
        this.SizeChanged += (_, _) => ReclampVisibleDialogs();

        ExtendsContentIntoTitleBar = true;
        SetTitleBar(CustomTitleBar);


        WelcomeOverlay.Visibility = Visibility.Visible;
        CustomTitleBar.Visibility = Visibility.Collapsed;
        BottomToolbarPanel.Visibility = Visibility.Collapsed;



        WelcomeOverlay.Loaded += (_, _) =>
        {
            if (_storeInitialized && App.Store is not { IsEncrypted: true }) { WarmWelcomeShatter(); WelcomeEntryAnimation.Begin(); }
        };
        InitNavIcons();
        if (Content is FrameworkElement sideMenuRoot)
        {
            _sideRoot = sideMenuRoot;
            sideMenuRoot.AddHandler(FrameworkElement.PointerPressedEvent,
                new PointerEventHandler(RootPointerPressedForSideMenu), true);
            sideMenuRoot.PointerMoved += OnMenuSideRootPointerMoved;
            sideMenuRoot.PointerExited += OnMenuSideRootPointerExited;
        }

        if (Content is FrameworkElement root)
        {
            root.ActualThemeChanged += (_, _) =>
            {
                if (App.CurrentTheme != "跟随系统") return;
                if (LockScreenFrame.Visibility == Visibility.Visible) { _themeChangedWhileLocked = true; return; }
                if (IsWelcomeCarouselUp) return;
                ShowThemeRestartOverlay();
            };

            root.KeyDown += (_, e) =>
            {
                if (e.Key != Windows.System.VirtualKey.Escape) return;
                if (AutoUpdateOverlay.Visibility == Visibility.Visible) { AutoUpdateCancel_Click(this, new RoutedEventArgs()); e.Handled = true; }
                else if (ThemeRestartOverlay.Visibility == Visibility.Visible) { HideThemeRestartOverlay(); e.Handled = true; }
                else if (SaveFailedOverlay.Visibility == Visibility.Visible) { HideSaveFailedDialog(); e.Handled = true; }
                else if (_sealMode && (SealUnlockContent.Visibility == Visibility.Visible || SealLockedView.Visibility == Visibility.Visible))
                {

                    TryLeaveSealGate();
                    e.Handled = true;
                }
            };




            var ctrlK = new KeyboardAccelerator { Key = Windows.System.VirtualKey.K, Modifiers = Windows.System.VirtualKeyModifiers.Control };
            ctrlK.Invoked += (_, _) => { if (!HasTextInputFocus() && LockScreenFrame.Visibility != Visibility.Visible && !IsWelcomeCarouselUp && !_sealMode) OpenSearchPage(); };
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



    private void ApplyWelcomeBrandFont()
    {
        if (App.CurrentLanguage == "en-US")
            WelcomeSubtitle.FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Assets/Fonts/Comfortaa.ttf#Comfortaa");
        else
            WelcomeSubtitle.ClearValue(Microsoft.UI.Xaml.Controls.TextBlock.FontFamilyProperty);
    }

    private void OnWindowLoaded(object sender, RoutedEventArgs e)
    {
        if (_windowLoadedHandled) return;
        _windowLoadedHandled = true;
        if (Content is FrameworkElement root) root.Loaded -= OnWindowLoaded;
        StartAutoLockWatcher();




        var langHint = App.LoadLanguageHint();
        App.ApplyLanguage(langHint ?? "");




        App.ApplyAutomationNames(this);
        ApplyWelcomeBrandFont();





        Services.AutoBackupService.RecoverInterruptedRestore();
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



        SyncLockHotkey();
        ApplyQuickCaptureHotkey();

        McpService.AuthorizeClient = clientPath =>
        {
            bool allowed = false;



            var gate = new System.Threading.ManualResetEventSlim(false);


            if (!DispatcherQueue.TryEnqueue(() => QueueOrShowMcpAuthorize(clientPath, r => { allowed = r; try { gate.Set(); } catch { } })))
            {
                Services.CrashLogger.LogNote("McpAuthorize", "the UI queue refused the authorization prompt - denying");
                gate.Dispose();
                return false;
            }


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





        if (App.Store is { IsLoaded: true }) ConsumePendingExternalRequests();


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

        SealNavSpaceText.Text = App.GetString("Seal_Tab_Space");
        SealNavCapsuleText.Text = App.GetString("Seal_Tab_Capsule");
        SealNavSettingsText.Text = App.GetString("Setting_Title_Page");


        WelcomeSubtitle.CharacterSpacing = App.CurrentLanguage switch
        {
            "zh-CN" or "zh-TW" => 180,
            "en-US" => 80,
            _ => 0
        };

        WelcomeVersionBadge.Text = Novara.Services.UpdateService.CurrentVersion;


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


        Services.UpdateAutoCheckService.SyncAutoCheckTimer();


        Services.PathPropertyService.Start();


        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        var windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hwnd);
        _appWindow = AppWindow.GetFromWindowId(windowId);
        _appWindow.Closing += AppWindow_Closing;


        Closed += (_, _) => { RemoveTrayIcon(); ReleaseSingleInstance(); };
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
        ApplyEdgeMenuSide(App.Store?.Database.AppSettings.EdgeMenuSide ?? "左侧");
        if (!skipWelcome && App.Store is not { IsEncrypted: true })
        {
            WarmWelcomeShatter();
            WelcomeEntryAnimation.Begin();
        }
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



    public bool HasTrayIcon => _trayIcon != null;






private bool CreateTrayIcon()
    {
        if (_trayIcon != null) return true;










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
            menu.Items.Add(new MenuFlyoutItem { Text = App.GetString("Tray_LockNow"), Command = new Services.RelayCommand(LockNowOrSealDirect) });
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
        catch (Exception ex)
        {


            Services.CrashLogger.LogNote("MainWindow.CreateTrayIcon", ex.ToString());
            created?.Dispose();
            _trayIcon = null;
        }
        return _trayIcon != null;
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






        _singleInstanceMutex?.Dispose();
        _singleInstanceMutex = null;
    }



    public enum RestartOutcome { Ok, SaveFailed, EditorDirty, LaunchFailed }






    internal static RestartOutcome RestartNow()
    {
        try
        {
            var exe = Environment.ProcessPath;
            if (string.IsNullOrEmpty(exe)) return RestartOutcome.LaunchFailed;




            if (App.Store?.SaveSync() == false) return RestartOutcome.SaveFailed;
            if (App.MainWindow is { } pending && pending._diaryEditorPage is { HasUnsavedEdits: true })
            {


                CrashLogger.LogNote("Restart", "aborted: the cached editor still holds unsaved edits");
                return RestartOutcome.EditorDirty;
            }
            ReleaseSingleInstance();
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo { FileName = exe, UseShellExecute = true });
        }
        catch
        {

            AcquireSingleInstance();
            System.Diagnostics.Debug.WriteLine("Novara 重启闭环：新进程启动失败，保持当前会话");
            return RestartOutcome.LaunchFailed;
        }
        if (App.MainWindow is { } mw) { mw.SetRestarting(); mw.ExitApp(saveFirst: false); }
        return RestartOutcome.Ok;
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











    public static async Task<(bool Saved, bool Refused)> FlushEditorForExitAsync(DiaryEditorPage ed)
    {
        bool saved = false, timedOut = false, threw = false;
        try { saved = await ed.SaveCurrentDiaryAsync().WaitAsync(TimeSpan.FromSeconds(3)); }
        catch (TimeoutException) { timedOut = true; }




        catch { threw = true; }
        if (!saved && ed.HasUnsavedEdits)
        {
            App.ShowToast(App.GetString("Editor_SaveFail_Toast"), ToastTone.Error);
            Services.CrashLogger.LogNote("EditorFlushOnExit", timedOut
                ? "editor save timed out after 3s - the last edit may not have been persisted"
                : threw
                    ? "editor save threw before answering - the last edit may not have been persisted"
                    : "editor save returned false with unsaved edits - the last edit was not persisted");
        }
        return (saved, !saved && !timedOut && !threw);
    }






    public static async Task<bool> FlushPendingEditorForRestartAsync()
    {
        if (App.MainWindow is { } mw && mw._diaryEditorPage is { } ed)
        {


            var (_, refused) = await FlushEditorForExitAsync(ed);
            return refused && ed.HasUnsavedEdits;
        }
        return false;
    }









public void BeginUpdateExit()
{
    Services.UpdateAutoCheckService.CancelInFlight();
    Services.PathPropertyService.Shutdown();
    _isTrayExit = true;
}



    private void ReleaseShatterEngine()
    {

        _shatterLockWatch?.Stop();
        _shatterLockWatch = null;
        try { _shatter?.Dispose(); } catch { }
        _shatter = null;
    }

    private async void AppWindow_Closing(AppWindow sender, AppWindowClosingEventArgs args)
    {
        if (!_isTrayExit && CloseBehavior == "托盘驻留" && _trayIcon != null)
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





        if (_restarting || _isTrayExit)
        {



            try { await FlushPendingEditorForRestartAsync(); } catch { }



            if (!_restarting) { try { App.Store?.SaveSync(); } catch { } }
            _closeAllowed = true;
            this.Close();
            return;
        }
        Services.UpdateAutoCheckService.CancelInFlight();
        Services.PathPropertyService.Shutdown();

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



        try { if (App.Store?.SaveSync() == false) { _closeAllowed = false; _closingHandled = false; return; } }
        catch { _closeAllowed = false; _closingHandled = false; return; }
        try { Services.AutoBackupService.TakeExitSnapshotIfDue(); } catch { }
        _closeAllowed = true;
        this.Close();
    }



    private string _qcMode = "todo";
    private IntPtr _qcPrevForeground;
    private bool _qcAnimating;



    public bool ApplyQuickCaptureHotkey()
    {
        Services.GlobalHotkeyService.Unregister(Services.GlobalHotkeyService.IdQuickCapture);
        var s = App.Store?.Database.AppSettings;
        if (s is not { QuickCaptureEnabled: true }) return true;
        var (mods, vk) = Services.GlobalHotkeyService.ParsePreset(s.QuickCaptureHotkey);
        if (!Services.GlobalHotkeyService.Register(Services.GlobalHotkeyService.IdQuickCapture, mods, vk))
        {
            App.ShowToast(App.GetString("QuickCapture_HotkeyConflict"), ToastTone.Error);
            return false;
        }
        return true;
    }

    private void OnQuickCaptureHotkey()
    {

        if (LockScreenFrame.Visibility == Visibility.Visible) { ShowMainWindow(); return; }
        if (_sealMode) return;
        if (IsWelcomeCarouselUp) return;
        if (App.Store?.IsSaveSuppressed == true) return;
        ShowQuickCapture();
    }

    private void OnLockNowHotkey()
    {
        LockNowOrSealDirect();
    }



    public static void SyncLockHotkey()
    {
        bool enabled = App.Store?.Database?.AppSettings?.PrivacyLockEnabled == true;
        if (!Services.GlobalHotkeyService.ApplyLockHotkey(enabled))
            App.ShowToast(App.GetString("LockNow_HotkeyConflict"), ToastTone.Error);
    }

    private void ShowQuickCapture()
    {



        if (LockScreenFrame.Visibility == Visibility.Visible || IsWelcomeCarouselUp || _sealMode) return;
        if (AutoUpdateOverlay.Visibility == Visibility.Visible || ThemeRestartOverlay.Visibility == Visibility.Visible) return;
        if (_veilCount > 0) return;
        if (QuickCaptureOverlay.Visibility == Visibility.Visible) { QuickCaptureBox.Focus(FocusState.Programmatic); QuickCaptureBox.SelectAll(); return; }
        _qcPrevForeground = GetForegroundWindow();
        ShowMainWindow();
        QuickCaptureBox.Text = "";
        SetQcMode("todo");
        QuickCaptureHint.Text = App.GetString("QuickCapture_Hint");
        QuickCaptureOverlay.Visibility = Visibility.Visible;


        DialogUi.RegisterDialogClamp((Grid)Content, _dialogMap, QuickCaptureOverlay, QuickCaptureBar, _dialogWidthFit);
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
        ReleaseShatterEngine();
        try
        {




            if (RootFrame.Content is DiaryEditorPage pendingEditor)
            {






                var (_, editorRefused) = await FlushEditorForExitAsync(pendingEditor);
                if (editorRefused && pendingEditor.HasUnsavedEdits)
                {
                    _isTrayExit = false;
                    return;
                }
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


            try { Services.AutoBackupService.TakeExitSnapshotIfDue(); } catch { }




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
    private bool _lockSuppressWarned;


    private int _sealDirectAutoLockSeconds;
    private bool _sealDirectAutoLockOnSystemLock;

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

        if (_sealMode && SealService.Current.Mode == SealSessionMode.Direct && SealService.Current.IsUnlocked)
        {
            if (_sealDirectAutoLockOnSystemLock)
            {
                _lockStrikes = IsInputDesktopUnavailable() ? _lockStrikes + 1 : 0;
                if (_lockStrikes >= 2) { _lockStrikes = 0; LockSealDirect(); return; }
            }
            else if (_lockStrikes != 0) _lockStrikes = 0;
            if (_sealDirectAutoLockSeconds > 0 && GetIdleSeconds() >= _sealDirectAutoLockSeconds) LockSealDirect();
            return;
        }

        if (LockScreenFrame.Visibility == Visibility.Visible) return;
        if (App.Store is not { IsLoaded: true, IsEncrypted: true }) return;
        if (IsRestoreBarrierBlockingLock(userInitiated: false)) return;
        var st = App.Store.Database.AppSettings;

        _sealDirectAutoLockSeconds = st.AutoLockSeconds;
        _sealDirectAutoLockOnSystemLock = st.AutoLockOnSystemLock;



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
        if (IsRestoreBarrierBlockingLock(userInitiated: true)) return;
        _lockInProgress = true;
        _ = LockNowCoreAsync();
    }






    private bool IsRestoreBarrierBlockingLock(bool userInitiated)
    {
        if (App.Store is not { IsSaveSuppressed: true })
        {
            _lockSuppressWarned = false;
            return false;
        }
        if (userInitiated || !_lockSuppressWarned)
        {
            _lockSuppressWarned = true;
            App.ShowToast(App.GetString("Common_Toast_RestorePending"), ToastTone.Error);
            Services.CrashLogger.LogNote("MainWindow.Lock",
                "lock refused: a restore is pending a restart (disk holds the restored vault, memory still holds the old one)");
        }
        return true;
    }




    public void BindStoreSaveFailed()
    {
        if (App.Store is { } store)
        {
            store.SaveFailed -= OnStoreSaveFailed;
            store.SaveFailed += OnStoreSaveFailed;
        }
    }

    private void OnStoreSaveFailed(string message) => DispatcherQueue.TryEnqueue(ShowSaveFailedDialog);


    private void CloseModalSurfacesForLock()
    {
        DenyQueuedMcpAuthorizations();
        if (McpAuthorizeOverlay.Visibility == Visibility.Visible || _mcpAuthorizeResult != null)
            CloseMcpAuthorizeDialog(invokeResult: true, allowed: false);
        if (MigrateFormatOverlay.Visibility == Visibility.Visible)
            HideMigrateFormatDialog();
    }

    private bool _lockInProgress;
    private bool _carouselInterruptedByLock;




    private async Task LockNowCoreAsync()
    {
        var relocked = false;
        try
        {
            if (RootFrame.Content is DiaryEditorPage ed)
            {
                await FlushEditorForExitAsync(ed);
            }




            try
            {
                if (_welcomeSaveTask is { } welcomeSave && !welcomeSave.Wait(2000))
                    CrashLogger.LogNote("Welcome", "exit commit wait timed out - the background flag write is still running");
            }
            catch (Exception ex)
            {
                CrashLogger.LogNote("Welcome", "exit commit wait failed: " + ex.Message);
            }




            try
            {
                if (WelcomeOverlay.Visibility == Visibility.Visible
                    && App.Store?.Database.AppSettings is { HasCompletedWelcome: true } welcomeSettings)
                    welcomeSettings.HasCompletedWelcome = false;
            }
            catch (Exception ex) { CrashLogger.LogNote("Welcome", "flag rollback failed: " + ex.Message); }


            if (WelcomeShatterHost.Visibility == Visibility.Visible)
            {
                _shatterLockWatch?.Stop();
                _shatter?.Cancel();
                OnShatterFinished(null, EventArgs.Empty);
                _welcomeFading = false;
            }
            try { App.Store!.SaveSync(); } catch { }

            if (App.Store?.Database.AppSettings is { } sealPolicy)
            {
                _sealDirectAutoLockSeconds = sealPolicy.AutoLockSeconds;
                _sealDirectAutoLockOnSystemLock = sealPolicy.AutoLockOnSystemLock;
            }
            App.RelockStore();
            relocked = true;
            BindStoreSaveFailed();




            if (WelcomeCarousel.Visibility == Visibility.Visible)
            {
                StopCarouselFloat();
                _pageSb?.Stop(); _pageSb = null;
                _meltSb?.Stop(); _meltSb = null;
                ResetCarouselMeltLayer();
                _pageAnimLock = false;


                NormalizeCarouselPages(_carouselPage);
                CarouselBlurHost.Visibility = Visibility.Collapsed;
                WelcomeCarousel.Opacity = 0;
                WelcomeCarousel.Visibility = Visibility.Collapsed;
                _carouselInterruptedByLock = true;
            }
            WelcomeMeltLayer.Visibility = Visibility.Collapsed;
            ResetMeltVisual(WelcomeMeltBlob0);
            ResetMeltVisual(WelcomeMeltBlob1);
            ResetMeltVisual(WelcomeMeltBlob2);



            CloseModalSurfacesForLock();
            Services.PathPropertyService.PauseForLock();


            SealService.Current.Lock();
            _sealPage?.ResetAfterSessionLock();
            ExitSealShell();
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
        page.SealDirectUnlocked += OnSealDirectUnlocked;

        page.CorruptDetected += ShowCorruptStoreDialog;
        page.IoErrorDetected += ShowIoErrorDialog;
        LockScreenFrame.Content = page;
        LockScreenFrame.Visibility = Visibility.Visible;
    }



    private void OnSealDirectUnlocked()
    {
        LockScreenFrame.Visibility = Visibility.Collapsed;
        LockScreenFrame.Content = null;


        WelcomeOverlay.Visibility = Visibility.Collapsed;
        NavigateToSeal();
    }

    private void HideLockScreen()
    {
        LockScreenFrame.Visibility = Visibility.Collapsed;
        LockScreenFrame.Content = null;
        ReloadPages();

        ConsumePendingExternalRequests();
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
            Services.UpdateAutoCheckService.SyncAutoCheckTimer();
            Services.PathPropertyService.Start();
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


                WelcomeOverlay.Visibility = Visibility.Visible;
                WarmWelcomeShatter();
                WelcomeEntryAnimation.Begin();
            }
        }




        if (App.Store is { NeedsFormatMigration: true } && !App.Store.Database.AppSettings.GcmMigrationRejected)
            ShowMigrateFormatDialog();
        else if (App.Store is { NeedsKdfMigration: true } && !App.Store.Database.AppSettings.KdfMigrationRejected)
            ShowMigrateFormatDialog();
        else if (App.Store is { NeedsV5Migration: true } && !App.Store.Database.AppSettings.V5MigrationRejected)
            ShowMigrateFormatDialog();








        if (_themeChangedWhileLocked && !_carouselInterruptedByLock)
        {
            _themeChangedWhileLocked = false;
            if (App.CurrentTheme == "跟随系统") ShowThemeRestartOverlay();
        }


        ApplyQuickCaptureHotkey();


        if (_carouselInterruptedByLock)
        {
            _carouselInterruptedByLock = false;
            WelcomeOverlay.Visibility = Visibility.Collapsed;
            CustomTitleBar.Visibility = Visibility.Visible;
            BottomToolbarPanel.Visibility = Visibility.Visible;
            ShowMainContent();
            MaybeShowWelcomeCarousel();
        }
    }










    private int _veilCount;






    public void ShowSealDropVeil()
    {
        if (SealDropVeil.Visibility != Visibility.Visible)
        {
            DialogDepth.ApplyBackdropBlur(SealDropVeil);
            SealDropVeil.Visibility = Visibility.Visible;
        }
    }


    public void HideSealDropVeil() => SealDropVeil.Visibility = Visibility.Collapsed;

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
    private bool _migrateInFlight;
    private bool _migrateIsKdf;
    private bool _migrateIsV5;

    public void ShowMigrateFormatDialog()
    {
        _migrateFailed = false;


        _migrateIsKdf = App.Store is { NeedsKdfMigration: true };
        _migrateIsV5 = App.Store is { NeedsV5Migration: true };
        MigrateFormatMessage.Text = App.GetString(_migrateIsV5 ? "Security_Migrate_V5_Body"
            : _migrateIsKdf ? "Security_Migrate_Kdf_Body" : "Security_Migrate_Body");
        MigrateFormatMessage.Foreground = App.GetBrush("AppTextSecondaryBrush");
        MigrateFormatTitle.Text = App.GetString(_migrateIsV5 ? "Security_Migrate_V5_Title"
            : _migrateIsKdf ? "Security_Migrate_Kdf_Title" : "Security_Migrate_Title");
        MigrateLaterButton.Visibility = Visibility.Visible;
        MigrateConfirmText.Text = App.GetString("Security_Migrate_Confirm");
        MigrateFormatDialogTransform.ScaleX = 0.94; MigrateFormatDialogTransform.ScaleY = 0.94; MigrateFormatDialogTransform.TranslateY = 24;
        MigrateFormatDialog.Opacity = 0;
        MigrateFormatOverlay.Visibility = Visibility.Visible; DialogUi.RegisterDialogClamp((Grid)Content, _dialogMap, MigrateFormatOverlay, MigrateFormatDialog, _dialogWidthFit);
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





            if (_migrateIsV5) { s.V5MigrationRejected = true; if (_migrateIsKdf) s.KdfMigrationRejected = true; }
            else if (_migrateIsKdf) { s.KdfMigrationRejected = true; }
            else { s.GcmMigrationRejected = true; }
            App.Store.SaveAsync();
        }
        HideMigrateFormatDialog();
    }

    private async void MigrateConfirm_Click(object sender, RoutedEventArgs e)
    {
        if (_migrateFailed) { HideMigrateFormatDialog(); return; }
        if (_migrateInFlight) return;




        _migrateInFlight = true;
        MigrateConfirmButton.IsEnabled = false;
        MigrateLaterButton.IsEnabled = false;
        bool ok;
        try
        {
            ok = await System.Threading.Tasks.Task.Run(() =>
                _migrateIsV5 ? App.Store?.MigrateToV5() == true
                : _migrateIsKdf ? App.Store?.MigrateKdf() == true : App.Store?.MigrateFormat() == true);
        }
        catch { ok = false; }
        finally
        {
            _migrateInFlight = false;
            MigrateConfirmButton.IsEnabled = true;
            MigrateLaterButton.IsEnabled = true;
        }

        if (LockScreenFrame.Visibility == Visibility.Visible) return;
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
        MigrateFormatMessage.Foreground = App.GetBrush("AppDangerTextBrush");
        MigrateLaterButton.Visibility = Visibility.Collapsed;
        MigrateConfirmText.Text = App.GetString("Common_Button_Confirm");
    }




    private void UpdateWelcomeLayout(double w, double h)
    {
        if (h <= 0) return;
        double scale = Math.Clamp(h / 800.0, 0.85, 1.6);
        WelcomeLogo.Width = WelcomeLogo.Height = 96 * scale;
        WelcomeTitleText.FontSize = WelcomeTitleShadowText.FontSize = 32 * scale;
        WelcomeSubtitle.FontSize = 16 * scale;
        WelcomeHint.FontSize = 12 * scale;
        WelcomeVersionBadge.FontSize = 11 * scale;
        WelcomeHintLine.Width = 28 * scale;
        WelcomeHintLine.Height = Math.Max(1.0, 1.5 * scale);

        WelcomeLogoTransform.TranslateY = -0.08 * h;
        WelcomeTitleTransform.Y = -0.08 * h + (48 + 36 + 15) * scale;
        WelcomeSubtitleTransform.Y = 0.38 * h;
        WelcomeHintTransform.Y = 0.44 * h;
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

                var t when t != null && t.StartsWith("类纸", StringComparison.Ordinal) => ElementTheme.Light,




                _ when PaperTheme.IsPaperName(PaperTheme.PeekHint()) => ElementTheme.Light,
                _ => ElementTheme.Default
            };
        }
        ApplyPaperBrandToNavGlow();
    }





    public void ApplyPaperBrandToNavGlow()
    {
        if (NavIndicator.Background is RadialGradientBrush rgb)
        {
            var c = PaperTheme.BrandColor;
            foreach (var stop in rgb.GradientStops)
                stop.Color = Windows.UI.Color.FromArgb(stop.Color.A, c.R, c.G, c.B);
        }
    }





    private void MenuButton_PointerEntered(object sender, PointerRoutedEventArgs e) => OpenMenuSidePanel();

    private void OpenMenuSidePanel()
    {
        if (_sideRoot == null) return;
        if (_sideOpen && MenuSidePanel.Visibility == Visibility.Visible) return;
        if (MenuSideItems.Children.Count == 0) BuildMenuSideItems();
        _sideOpen = true;
        MenuSidePanel.Visibility = Visibility.Visible;
        MenuSidePanel.UpdateLayout();
        _sideFinalTarget = 2.0;
        MenuButton.IsHitTestVisible = false;
        MenuSidePanelTranslate.X = 0;
        _panelHome = MenuSidePanel.TransformToVisual(_sideRoot)
                         .TransformBounds(new Windows.Foundation.Rect(0, 0, MenuSidePanel.ActualWidth, MenuSidePanel.ActualHeight));
        MenuSidePanelTranslate.X = _edgeSign * MenuSidePanel.ActualWidth;
        StartSideTransition(Math.Min(_sideProgress + 1.0, 2.0));
        EnsureSideRendering();
    }





    private void RecachePanelHome()
    {
        if (_sideRoot == null) return;
        MenuSidePanel.UpdateLayout();
        var measured = MenuSidePanel.TransformToVisual(_sideRoot)
                          .TransformBounds(new Windows.Foundation.Rect(0, 0, MenuSidePanel.ActualWidth, MenuSidePanel.ActualHeight));
        measured.X -= MenuSidePanelTranslate.X;
        _panelHome = measured;
    }


    public void ApplyEdgeMenuSide(string side)
    {
        bool left = side != "右侧";
        _edgeSign = left ? -1.0 : 1.0;
        var align = left ? HorizontalAlignment.Left : HorizontalAlignment.Right;
        BottomToolbarPanel.HorizontalAlignment = align;
        MenuSidePanel.HorizontalAlignment = align;
        MenuHandleBar.HorizontalAlignment = align;
        MenuButton.HorizontalAlignment = align;
        MenuSidePanel.CornerRadius = left ? new CornerRadius(0, 12, 12, 0) : new CornerRadius(12, 0, 0, 12);
        MenuHandleBar.CornerRadius = left ? new CornerRadius(0, 3, 3, 0) : new CornerRadius(3, 0, 0, 3);
        if (_sideOpen) CloseMenuSidePanel();
        ApplySideProgress(_sideProgress);
    }

    private void CloseMenuSidePanel()
    {
        if (!_sideOpen) return;
        _sideOpen = false;
        MenuButton.IsHitTestVisible = true;
        _sideFinalTarget = 0.0;
        StartSideTransition(Math.Max(_sideProgress - 1.0, 0.0));
        EnsureSideRendering();
    }

    private bool PointerInMenuZone()
    {
        if (_sideRoot == null) return false;
        var pr = _panelHome;
        pr.X -= 12; pr.Y -= 12; pr.Width += 24; pr.Height += 24;
        return pr.Contains(_lastPointerPos);
    }

    private void OnMenuSideRootPointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (_sideRoot == null) return;
        _lastPointerPos = e.GetCurrentPoint(_sideRoot).Position;
        if (!_sideOpen) return;
        if (!PointerInMenuZone()) CloseMenuSidePanel();
    }

    private void OnMenuSideRootPointerExited(object sender, PointerRoutedEventArgs e)
    {

        if (_sideOpen && !PointerInMenuZone()) CloseMenuSidePanel();
    }


    private void RootPointerPressedForSideMenu(object sender, PointerRoutedEventArgs e)
    {
        if (!_sideOpen) return;
        if (IsWithinMenuSide(e.OriginalSource)) return;
        CloseMenuSidePanel();
    }

    private bool IsWithinMenuSide(object source)
    {
        DependencyObject? d = source as DependencyObject;
        while (d != null)
        {
            if (d == MenuSidePanel || d == MenuButton) return true;
            d = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetParent(d);
        }
        return false;
    }

    private void StartSideTransition(double target)
    {
        _sideFrom = _sideProgress;
        _sideTo = target;
        _sideWatch = System.Diagnostics.Stopwatch.StartNew();
        _sideAnimating = true;
    }

    private void EnsureSideRendering()
    {

        Microsoft.UI.Xaml.Media.CompositionTarget.Rendering -= OnMenuSideRendering;
        Microsoft.UI.Xaml.Media.CompositionTarget.Rendering += OnMenuSideRendering;
    }

    private void OnMenuSideRendering(object? sender, object e)
    {
        if (!_sideAnimating) return;
        double elapsed = _sideWatch?.Elapsed.TotalMilliseconds ?? SidePhaseMs;
        double t = Math.Clamp(elapsed / SidePhaseMs, 0.0, 1.0);
        double eased = 1 - Math.Pow(1 - t, 3);
        _sideProgress = _sideFrom + (_sideTo - _sideFrom) * eased;
        ApplySideProgress(_sideProgress);
        if (t >= 1.0)
        {
            _sideAnimating = false;
            _sideWatch?.Stop(); _sideWatch = null;
            if (Math.Abs(_sideProgress - _sideFinalTarget) > 0.001)
            {
                StartSideTransition(Math.Clamp(_sideProgress + Math.Sign(_sideFinalTarget - _sideProgress), 0.0, 2.0));
                EnsureSideRendering();
            }
            else if (!_sideOpen)
                MenuSidePanel.Visibility = Visibility.Collapsed;
        }
    }

    private void ApplySideProgress(double p)
    {

        double handle = Math.Clamp(p, 0.0, 1.0);
        double panel = Math.Clamp(p - 1.0, 0.0, 1.0);
        MenuButtonTranslate.X = _edgeSign * 6 * handle;
        MenuHandleBarTranslate.X = _edgeSign * 6 * handle;
        double panelW = MenuSidePanel.ActualWidth;
        if (panelW < 10) panelW = MenuSidePanel.MinWidth + MenuSidePanel.Padding.Left + MenuSidePanel.Padding.Right;
        MenuSidePanelTranslate.X = _edgeSign * panelW * (1 - panel);
    }

    private void BuildMenuSideItems()
    {
        var cv = Microsoft.UI.Xaml.Markup.XamlBindingHelper.ConvertValue;
        var white = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 255, 255, 255));
        var idle = new SolidColorBrush(Windows.UI.Color.FromArgb(0, 0, 0, 0));
        var hover = App.GetBrush("AppPrimaryButtonHoverBrush");

        TextBlock AddItem(StackPanel host, string[] paths, string textKey, System.Action action)
        {
            var group = new GeometryGroup { FillRule = FillRule.Nonzero };
            foreach (var p in paths)
                group.Children.Add((Geometry)cv(typeof(Geometry), p));
            var content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            content.Children.Add(new Microsoft.UI.Xaml.Shapes.Path
            {
                Data = group,
                Width = 16,
                Height = 16,
                Stretch = Stretch.Uniform,
                Fill = white,
                VerticalAlignment = VerticalAlignment.Center
            });
            var label = new TextBlock
            {
                Text = App.GetString(textKey),
                FontSize = 14,
                Foreground = white,
                VerticalAlignment = VerticalAlignment.Center
            };
            content.Children.Add(label);
            var item = new Border
            {
                Background = idle,
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(10, 8, 10, 8),
                Child = content
            };


            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(item, label.Text);


            item.IsTabStop = true;
            item.KeyDown += (_, e) =>
            {
                if (e.Key is not (Windows.System.VirtualKey.Enter or Windows.System.VirtualKey.Space)) return;
                e.Handled = true;
                action();
                CloseMenuSidePanel();
            };
            item.PointerEntered += (_, _) => item.Background = hover;
            item.PointerExited += (_, _) => item.Background = idle;
            item.PointerPressed += (_, _) =>
            {
                action();
                CloseMenuSidePanel();
            };
            host.Children.Add(item);
            return label;
        }


        AddItem(MenuSideItems, IconData.SealSpace, "Menu_Seal", () => NavigateToSeal());
        AddItem(MenuSideItems, IconData.Tools, "Menu_Tools", () => NavigateToTools());
        AddItem(MenuSideItems, IconData.Search, "Menu_Search", () => OpenSearchPage());
        AddItem(MenuSideItems, new[] { IconData.Delete }, "Trash_Title", () => OpenTrashPage());
        AddItem(MenuSideItems, IconData.Settings, "Setting_Title_Page", () => NavigateToSettings());


        _sealReturnLabel = AddItem(SealSideItems, new[] { IconData.Back },
            SealService.Current.Mode == SealSessionMode.Direct ? "Seal_ReturnLock" : "Seal_ReturnHome",
            ReturnHomeFromSeal);


        SealNavSpacePath.Data = BuildNonzeroGeometry(IconData.SealSpace);
        SealNavCapsulePath.Data = BuildNonzeroGeometry(IconData.Capsule);
        SealNavSettingsPath.Data = BuildNonzeroGeometry(IconData.SealSettings);
        SealNavSpaceButton.Translation = new System.Numerics.Vector3(0f, 0f, 20f);


        try
        {
            if (_sealNavShadow == null)
            {
                _sealNavShadow = new ThemeShadow();
                _sealNavShadow.Receivers.Add(SealNavShadowReceiver);
                foreach (var btn in new[] { SealNavSpaceButton, SealNavCapsule, SealNavSettings })
                    btn.Shadow = _sealNavShadow;
            }
        }
        catch { }
    }


    private static Geometry BuildNonzeroGeometry(string[] paths)
    {
        var cv = Microsoft.UI.Xaml.Markup.XamlBindingHelper.ConvertValue;
        var group = new GeometryGroup { FillRule = FillRule.Nonzero };
        foreach (var path in paths)
            group.Children.Add((Geometry)cv(typeof(Geometry), path));
        return group;
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



        if (_memoPage is not null) _memoPage = RebuildPage(_memoPage, new BasicMemoPage());
        if (_planPage is not null) _planPage = RebuildPage(_planPage, new PlanPage());
        if (_filePathPage is not null) _filePathPage = RebuildPage(_filePathPage, new FilePathPage());
        if (_diaryPage is not null) _diaryPage = RebuildPage(_diaryPage, new DiaryPage());
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



        if (_memoPage is not null && !ReferenceEquals(_memoPage, shown)) _memoPage = RebuildPage(_memoPage, new BasicMemoPage());
        if (_planPage is not null && !ReferenceEquals(_planPage, shown)) _planPage = RebuildPage(_planPage, new PlanPage());
        if (_filePathPage is not null && !ReferenceEquals(_filePathPage, shown)) _filePathPage = RebuildPage(_filePathPage, new FilePathPage());
        if (_diaryPage is not null && !ReferenceEquals(_diaryPage, shown)) _diaryPage = RebuildPage(_diaryPage, new DiaryPage());
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





    private T RebuildPage<T>(T oldPage, T freshPage) where T : class
    {
        if (ReferenceEquals(_previousContent, oldPage)) _previousContent = freshPage;



        if (ReferenceEquals(_preSearchContent, oldPage)) _preSearchContent = freshPage;
        if (ReferenceEquals(_preTrashContent, oldPage)) _preTrashContent = freshPage;
        return freshPage;
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



    public void ApplyMemoDisplayModeSetting()
    {
        _memoPage?.ApplyMemoDisplayModeFromSettings(animate: true);
        _planPage?.ApplyPlanDisplayModeFromSettings(animate: true);
    }

    public void ApplyVisibleTabs(HashSet<string> visibleTabs)
    {





        var visibleItems = new List<FrameworkElement>(4);
        foreach (var (btn, host) in new[] { (NavMemo, NavMemoHost), (NavFile, NavFileHost), (NavPlan, NavPlanHost), (NavDiary, NavDiaryHost) })
        {
            bool isVisible = visibleTabs.Contains(TagToTabName((string)btn.Tag));
            btn.Visibility = isVisible ? Visibility.Visible : Visibility.Collapsed;
            host.Visibility = btn.Visibility;
            if (isVisible) visibleItems.Add(host);
        }

        NavBar.ColumnDefinitions.Clear();
        for (int i = 0; i < visibleItems.Count; i++)
        {
            NavBar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            Grid.SetColumn(visibleItems[i], i);
        }
        NavIndicator.Opacity = 0;
        _navIndicatorPlaced = false;






        if (NavBar.Visibility == Visibility.Visible)
        {
            NavBar.UpdateLayout();
            RelocateNavGlow();
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



    private int _carouselPage;


    private void MaybeShowWelcomeCarousel()
    {
#if DEBUG
        ShowWelcomeCarousel();
#else
        if (App.Store?.Database.AppSettings is { HasCompletedCarousel: false }) ShowWelcomeCarousel();
#endif
    }




    private void PrepareWelcomeCarousel()
    {
        if (_carouselPrepared) return;
        _carouselPrepared = true;
        _carouselPage = 0;
        ApplyCarouselTexts();
        UpdateCarouselMetrics();
        SetCarouselPage(0, animate: false);
        EnsureCarouselBlur();
        ResetCarouselMeltLayer();
    }

    private void ShowWelcomeCarousel()
    {
        if (!_carouselPrepared) PrepareWelcomeCarousel();


        else
        {


            NormalizeCarouselPages(0);
            SetCarouselPage(0, animate: false);
        }
        CarouselBlurHost.Visibility = Visibility.Visible;
        WelcomeCarousel.Visibility = Visibility.Visible;
        WelcomeCarousel.UpdateLayout();
        WelcomeCarousel.Opacity = 0;
        var sb = new Storyboard();
        var oi = new DoubleAnimation { From = 0, To = 1, Duration = TimeSpan.FromMilliseconds(150) };
        Storyboard.SetTarget(oi, WelcomeCarousel); Storyboard.SetTargetProperty(oi, "Opacity"); sb.Children.Add(oi);
        sb.Begin();
        StartCarouselFloat();
    }

    private void CloseWelcomeCarousel()
    {
        StopCarouselFloat();
        _carouselPrepared = false;
        _pageSb?.Stop(); _pageSb = null;
        _meltSb?.Stop(); _meltSb = null;
        _pageAnimLock = false;
        CarouselBlurHost.Visibility = Visibility.Collapsed;
        WelcomeCarousel.Opacity = 0;
        WelcomeCarousel.Visibility = Visibility.Collapsed;
        var s = App.Store?.Database.AppSettings;
        if (s != null && !s.HasCompletedCarousel)
        {
            s.HasCompletedCarousel = true;
            _ = App.Store?.SaveAsync();
        }
        ReplayDeferredExternalRequests();


        if (_themeChangedWhileLocked)
        {
            _themeChangedWhileLocked = false;
            if (App.CurrentTheme == "跟随系统") ShowThemeRestartOverlay();
        }
    }








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

            var factory = compositor.CreateEffectFactory(blur, new[] { "Blur.BlurAmount" });
            var brush = factory.CreateBrush();
            _carouselBlurBrush = brush;
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
            _floatPeriods = new[] { 3.0, 3.7, 4.3, 4.9 };
            _floatAmps = new[] { 5.0, 6.0, 5.0, 7.0 };
            _floatTransforms = new[] { CarouselFloatT0, CarouselFloatT1, CarouselFloatT2, CarouselFloatT3 };
            _floatScales = new[] { CarouselMenuScale0, CarouselMenuScale1, CarouselMenuScale2, CarouselMenuScale3 };
        }
        var t = (double)System.Diagnostics.Stopwatch.GetTimestamp() / System.Diagnostics.Stopwatch.Frequency - _carouselFloatStart;

        if (App.IsAnimationsEnabled)
        {
            var phases = _floatPhases;
            var transforms = _floatTransforms!;
            var scales = _floatScales!;
            for (int i = 0; i < 4; i++)
            {



                _menuHoverLift[i] += (_menuHoverLiftTarget[i] - _menuHoverLift[i]) * 0.18;
                _menuHoverScale[i] += (_menuHoverScaleTarget[i] - _menuHoverScale[i]) * 0.18;
                transforms[i].Y = _floatAmps![i] * Math.Sin(2 * Math.PI * t / _floatPeriods![i] + phases[i]) + _menuHoverLift[i];
                scales[i].ScaleX = scales[i].ScaleY = _menuHoverScale[i];
            }
        }
        ApplyCarouselDemo(t - _carouselDemoStart);
    }


    private double[]? _floatPhases;
    private double[]? _floatPeriods;
    private double[]? _floatAmps;
    private Microsoft.UI.Xaml.Media.TranslateTransform[]? _floatTransforms;
    private Microsoft.UI.Xaml.Media.ScaleTransform[]? _floatScales;



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

        CarouselDemoCursorTranslate.X = DemoCursorStartX;
        CarouselDemoCursorTranslate.Y = DemoCursorStartY;
        CarouselDemoMenuTranslate.X = 0;
        CarouselDemoPanelTranslate.X = -118;
        CarouselDemoGhostHover.Opacity = 0;



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
        CarouselP1IconMemo.Data = App.CreateGeometry(IconData.NavMemo);
        CarouselP1IconFile.Data = App.CreateGeometry(IconData.NavFile);
        CarouselP1IconPlan.Data = App.CreateGeometry(IconData.NavPlan);
        CarouselP1IconDiary.Data = App.CreateGeometry(IconData.NavDiary);
    }





    private void BuildCarouselPage2()
    {


        CarouselDemoStage.SizeChanged += (_, _) => CarouselDemoStage.Clip = new Microsoft.UI.Xaml.Media.RectangleGeometry { Rect = new Windows.Foundation.Rect(0, -600, CarouselDemoStage.ActualWidth, 1600) };
    }



    private const double DemoCursorStartX = 240, DemoCursorStartY = 30;
    private const double DemoCursorEndX = 8, DemoCursorEndY = 158;
    private const double DemoCursorPanelX = 34, DemoCursorPanelY = 103;

    private static double DemoEaseInOut(double t) => t < 0.5 ? 2 * t * t : 1 - Math.Pow(-2 * t + 2, 2) / 2;
    private static double DemoCubicOut(double t) => 1 - Math.Pow(1 - Math.Clamp(t, 0, 1), 3);
    private static double DemoLerp(double a, double b, double k) => a + (b - a) * k;

    private double _carouselDemoStart;

    private double CarouselClockSeconds() => (double)System.Diagnostics.Stopwatch.GetTimestamp() / System.Diagnostics.Stopwatch.Frequency - _carouselFloatStart;








    private void ApplyCarouselDemo(double t)
    {
        double phase = t % 5.0;
        double handleP, panelP, ghostHl;
        double cx, cy;
        if (phase < 1.0)
        {
            double k = DemoEaseInOut(phase);
            cx = DemoLerp(DemoCursorStartX, DemoCursorEndX, k);
            cy = DemoLerp(DemoCursorStartY, DemoCursorEndY, k);
            handleP = 0; panelP = 0; ghostHl = 0;
        }
        else if (phase < 1.22) { cx = DemoCursorEndX; cy = DemoCursorEndY; handleP = DemoCubicOut((phase - 1.0) / 0.22); panelP = 0; ghostHl = 0; }
        else if (phase < 1.44) { cx = DemoCursorEndX; cy = DemoCursorEndY; handleP = 1; panelP = DemoCubicOut((phase - 1.22) / 0.22); ghostHl = 0; }
        else if (phase < 1.7)
        {
            double k = DemoEaseInOut((phase - 1.44) / 0.26);
            cx = DemoLerp(DemoCursorEndX, DemoCursorPanelX, k);
            cy = DemoLerp(DemoCursorEndY, DemoCursorPanelY, k);
            handleP = 1; panelP = 1; ghostHl = 0;
        }
        else if (phase < 2.9) { cx = DemoCursorPanelX; cy = DemoCursorPanelY; handleP = 1; panelP = 1; ghostHl = DemoCubicOut((phase - 1.7) / 0.3); }
        else if (phase < 3.15)
        {
            double k = DemoEaseInOut((phase - 2.9) / 0.25);
            cx = DemoLerp(DemoCursorPanelX, DemoCursorStartX, k);
            cy = DemoLerp(DemoCursorPanelY, DemoCursorStartY, k);
            handleP = 1; panelP = 1; ghostHl = 1;
        }
        else if (phase < 3.37) { cx = DemoCursorStartX; cy = DemoCursorStartY; handleP = 1; panelP = 1 - DemoCubicOut((phase - 3.15) / 0.22); ghostHl = 0; }
        else if (phase < 3.59) { cx = DemoCursorStartX; cy = DemoCursorStartY; handleP = 1 - DemoCubicOut((phase - 3.37) / 0.22); panelP = 0; ghostHl = 0; }
        else { cx = DemoCursorStartX; cy = DemoCursorStartY; handleP = 0; panelP = 0; ghostHl = 0; }

        CarouselDemoCursorTranslate.X = cx;
        CarouselDemoCursorTranslate.Y = cy;
        CarouselDemoMenuTranslate.X = -6 * handleP;
        CarouselDemoPanelTranslate.X = -118 * (1 - panelP);



        CarouselDemoSidePanel.Visibility = panelP > 0 ? Visibility.Visible : Visibility.Collapsed;
        CarouselDemoGhostHover.Opacity = ghostHl;
    }






    private void BuildCarouselChrome()
    {
        _pageTrans = new Microsoft.UI.Xaml.Media.TranslateTransform[CarouselPageList.Length];
        for (int i = 0; i < CarouselPageList.Length; i++)
        {
            var tt = new Microsoft.UI.Xaml.Media.TranslateTransform();
            CarouselPageList[i].RenderTransform = tt;
            _pageTrans[i] = tt;
        }


        CarouselPages.SizeChanged += (_, _) =>
        {
            CarouselPages.Clip = new Microsoft.UI.Xaml.Media.RectangleGeometry { Rect = new Windows.Foundation.Rect(-60, -24, CarouselPages.ActualWidth + 120, CarouselPages.ActualHeight + 48) };
            UpdateCarouselMetrics();
        };
        BuildCarouselPage1();
        BuildCarouselPage2();
        CarouselHotkey1Chip.Text = "Ctrl + K";
        CarouselHotkey2Chip.Text = "Ctrl + Shift + N";
        CarouselHotkey3Chip.Text = "Ctrl + Shift + L";
        CarouselP5SnapIcon.Data = App.CreateGeometry(IconData.Document);
        CarouselP5SyncIcon.Data = App.CreateGeometry(IconData.MdLink);

        CarouselSealIcon1.Data = App.CreateGeometry(IconData.Import);
        CarouselSealIcon2.Data = App.CreateGeometry(IconData.Lock);
        CarouselSealIcon3.Data = BuildNonzeroGeometry(IconData.Capsule);
        CarouselSealIcon4.Data = BuildNonzeroGeometry(IconData.SealSpace);
    }





    private Microsoft.UI.Xaml.Media.ScaleTransform? _carouselPagesScale;
    private bool _carouselPage0Horizontal;
    private double _carouselMetricsLast = -1.0;

    private static readonly Thickness[] CarouselMenuMarginVertical =
    {
        new Thickness(0, -320, 28, 0), new Thickness(20, -116, 0, 0), new Thickness(0, 120, 20, 0), new Thickness(28, 352, 0, 0),
    };
    private static readonly Thickness[] CarouselMenuMarginHorizontal =
    {

        new Thickness(0, -60, 500, 0), new Thickness(166, 36, 0, 0), new Thickness(0, -36, 166, 0), new Thickness(500, 60, 0, 0),
    };

    private void UpdateCarouselMetrics()
    {



        double avail = Math.Max(320.0, ((CarouselPages.Parent as FrameworkElement)?.ActualWidth ?? 992.0) - 112.0);
        CarouselPages.Width = Math.Min(880.0, avail);
        if (CarouselPages.ActualWidth < 50 || CarouselPages.ActualHeight < 50) return;



        bool maximized = (_appWindow?.Presenter as Microsoft.UI.Windowing.OverlappedPresenter)?.State
                         == Microsoft.UI.Windowing.OverlappedPresenterState.Maximized;
        bool horizontal = !maximized;
        if (horizontal != _carouselPage0Horizontal)
        {
            _carouselPage0Horizontal = horizontal;
            Border[] shells = { CarouselMenuMemo, CarouselMenuPath, CarouselMenuPlan, CarouselMenuDiary };
            var margins = horizontal ? CarouselMenuMarginHorizontal : CarouselMenuMarginVertical;
            for (int i = 0; i < 4; i++) shells[i].Margin = margins[i];
        }
        double designH = _carouselPage0Horizontal ? 440 : 620;
        double s = Math.Min(1.0, Math.Min(CarouselPages.ActualWidth / 880.0, CarouselPages.ActualHeight / designH));
        if (_carouselPagesScale == null)
        {
            _carouselPagesScale = new Microsoft.UI.Xaml.Media.ScaleTransform();
            CarouselPages.RenderTransform = _carouselPagesScale;
        }
        if (Math.Abs(s - _carouselMetricsLast) < 0.002) return;
        _carouselMetricsLast = s;
        _carouselPagesScale.ScaleX = _carouselPagesScale.ScaleY = s;
        _carouselPagesScale.CenterX = CarouselPages.ActualWidth / 2;
        _carouselPagesScale.CenterY = CarouselPages.ActualHeight / 2;
    }







    private void ApplyCarouselTexts()
    {
        CarouselP0Title.Text = App.GetString("Carousel_P0_Title");
        BuildCarouselMenus();
        CarouselP1Title.Text = App.GetString("Carousel_P1_Title");

        CarouselP1NameMemo.Text = App.GetString("Nav_Tab_Memo");
        CarouselP1DescMemo.Text = App.GetString("Carousel_P1_Memo");
        CarouselP1NameFile.Text = App.GetString("Nav_Tab_File");
        CarouselP1DescFile.Text = App.GetString("Carousel_P1_Path");
        CarouselP1NamePlan.Text = App.GetString("Nav_Tab_Plan");
        CarouselP1DescPlan.Text = App.GetString("Carousel_P1_Plan");
        CarouselP1NameDiary.Text = App.GetString("Nav_Tab_Diary");
        CarouselP1DescDiary.Text = App.GetString("Carousel_P1_Diary");





        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(CarouselP1CardMemo, App.GetString("Nav_Tab_Memo"));
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(CarouselP1CardFile, App.GetString("Nav_Tab_File"));
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(CarouselP1CardPlan, App.GetString("Nav_Tab_Plan"));
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(CarouselP1CardDiary, App.GetString("Nav_Tab_Diary"));
        CarouselP2Title.Text = App.GetString("Carousel_P2_Title");
        CarouselP2Intro.Text = App.GetString("Carousel_P2_Desc");
        CarouselHotkey1Desc.Text = App.GetString("Carousel_Hotkey_Search_Desc");
        CarouselHotkey2Desc.Text = App.GetString("Carousel_Hotkey_Capture_Desc");
        CarouselHotkey3Desc.Text = App.GetString("Carousel_Hotkey_Lock_Desc");
        CarouselHotkeyNote.Text = App.GetString("Carousel_Hotkey_More");
        CarouselP3Title.Text = App.GetString("Carousel_P3_Title");
        CarouselP3Body.Text = App.GetString("Carousel_P3_Body");
        CarouselLockTitle.Text = App.GetString("Setting_PrivacyLock_Title");
        CarouselLockHelloText.Text = App.GetString("Setting_WinHello_Title");
        CarouselLockSetupText.Text = App.GetString("Setting_PrivacyLock_Setup");
        CarouselSealTitle.Text = App.GetString("Carousel_Seal_Title");
        CarouselSealB1Title.Text = App.GetString("Carousel_Seal_B1_Title");
        CarouselSealB1Body.Text = App.GetString("Carousel_Seal_B1_Body");
        CarouselSealB2Title.Text = App.GetString("Carousel_Seal_B2_Title");
        CarouselSealB2Body.Text = App.GetString("Carousel_Seal_B2_Body");
        CarouselSealB3Title.Text = App.GetString("Carousel_Seal_B3_Title");
        CarouselSealB3Body.Text = App.GetString("Carousel_Seal_B3_Body");
        CarouselSealB4Title.Text = App.GetString("Carousel_Seal_B4_Title");
        CarouselSealB4Body.Text = App.GetString("Carousel_Seal_B4_Body");
        CarouselSealBody.Text = App.GetString("Carousel_Seal_Body");
        SetCarouselBrandTitle(CarouselP4Title, App.GetString("Carousel_P4_Title"));
        CarouselP4B1.Text = App.GetString("Carousel_P4_B1");
        CarouselP4B2.Text = App.GetString("Carousel_P4_B2");
        CarouselP4B3.Text = App.GetString("Carousel_P4_B3");
        CarouselMcpTitle.Text = App.GetString("Setting_Mcp_Title");
        CarouselMcpAuditText.Text = App.GetString("Setting_Mcp_Audit_Button");
        CarouselMcpConfigText.Text = App.GetString("Setting_Mcp_Config");
        CarouselMcpToggleText.Text = App.GetString("Setting_Autostart_Off");
        CarouselP5Title.Text = App.GetString("Carousel_P5_Title");
        CarouselP5SnapTitle.Text = App.GetString("Setting_Connect_Title");
        CarouselP5SnapDesc.Text = App.GetString("Carousel_P5_SnapDesc");
        CarouselP5SyncTitle.Text = App.GetString("Sync_Card_Title");
        CarouselP5SyncDesc.Text = App.GetString("Carousel_P5_SyncDesc");
        CarouselP5Body.Text = App.GetString("Carousel_P5_Body");
        SetCarouselBrandTitle(CarouselP6B1, App.GetString("Carousel_P6_B1"));
        CarouselP6B2.Text = App.GetString("Carousel_P6_B2");
        CarouselP6B3.Text = App.GetString("Carousel_P6_B3");
        CarouselP6GitHubLink.Content = App.GetString("Carousel_P6_GitHub");
        CarouselSkipButton.Content = App.GetString("Carousel_Skip");
        UpdateCarouselBottomBar();
    }



    private static void SetCarouselBrandTitle(TextBlock target, string text)
    {
        target.Inlines.Clear();
        int i = 0;
        while (i < text.Length)
        {
            int hit = text.IndexOf("Novara", i, StringComparison.Ordinal);
            if (hit < 0)
            {
                target.Inlines.Add(new Microsoft.UI.Xaml.Documents.Run { Text = text[i..] });
                break;
            }
            if (hit > i)
                target.Inlines.Add(new Microsoft.UI.Xaml.Documents.Run { Text = text[i..hit] });
            target.Inlines.Add(new Microsoft.UI.Xaml.Documents.Run
            {
                Text = "Novara",
                FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Assets/Fonts/Comfortaa.ttf#Comfortaa"),
            });
            i = hit + "Novara".Length;
        }
    }

    private void UpdateCarouselBottomBar()
    {
        bool last = _carouselPage == CarouselPageList.Length - 1;
        CarouselNextButton.Content = App.GetString(last ? "Carousel_Begin" : "Carousel_Next");
        CarouselSkipButton.Visibility = last ? Visibility.Collapsed : Visibility.Visible;
    }




    private Grid[] CarouselPageList = System.Array.Empty<Grid>();


    private Microsoft.UI.Xaml.Media.TranslateTransform[]? _pageTrans;
    private bool _pageAnimLock;
    private bool _carouselMelting;
    private Storyboard? _pageSb;
    private Storyboard? _meltSb;
    private Microsoft.UI.Composition.CompositionBrush? _carouselBlurBrush;




    private void NormalizeCarouselPages(int index)
    {
        if (index < 0 || index >= CarouselPageList.Length) return;
        for (int i = 0; i < CarouselPageList.Length; i++)
        {
            CarouselPageList[i].Visibility = i == index ? Visibility.Visible : Visibility.Collapsed;
            if (_pageTrans != null) _pageTrans[i].X = 0;
            CarouselPageList[i].Opacity = 1;
        }
    }

    private void SetCarouselPage(int index, bool animate = true)
    {


        Array.Fill(_menuHoverLiftTarget, 0);
        Array.Fill(_menuHoverScaleTarget, 1.0);
        if (index < 0 || index >= CarouselPageList.Length) return;
        if (_pageAnimLock || _carouselMelting) return;
        int old = _carouselPage;
        if (old == index) { UpdateCarouselBottomBar(); return; }
        _carouselPage = index;
        if (index == 2) _carouselDemoStart = CarouselClockSeconds();
        UpdateCarouselBottomBar();
        var oldPage = CarouselPageList[old];
        var newPage = CarouselPageList[index];
        if (!animate || !App.IsAnimationsEnabled || _pageTrans == null || CarouselPages.ActualWidth < 50)
        {
            NormalizeCarouselPages(index);
            return;
        }

        _pageAnimLock = true;
        double w = CarouselPages.ActualWidth;
        double dir = index > old ? 1.0 : -1.0;
        oldPage.Visibility = Visibility.Visible;
        newPage.Visibility = Visibility.Visible;
        newPage.Opacity = 0;
        _pageTrans[old].X = 0;
        _pageTrans[index].X = dir * w;
        var sb = new Storyboard();
        _pageSb = sb;
        var ox = new DoubleAnimation { From = 0, To = -dir * w, Duration = TimeSpan.FromMilliseconds(300), EasingFunction = Motion.Accelerate() };
        Storyboard.SetTarget(ox, _pageTrans[old]); Storyboard.SetTargetProperty(ox, "X");
        var oo = new DoubleAnimation { From = 1, To = 0, Duration = TimeSpan.FromMilliseconds(210) };
        Storyboard.SetTarget(oo, oldPage); Storyboard.SetTargetProperty(oo, "Opacity");
        var nx = new DoubleAnimation { From = dir * w, To = 0, Duration = TimeSpan.FromMilliseconds(360), EasingFunction = Motion.Decelerate(), BeginTime = TimeSpan.FromMilliseconds(30) };
        Storyboard.SetTarget(nx, _pageTrans[index]); Storyboard.SetTargetProperty(nx, "X");
        var no = new DoubleAnimation { From = 0, To = 1, Duration = TimeSpan.FromMilliseconds(200), BeginTime = TimeSpan.FromMilliseconds(30) };
        Storyboard.SetTarget(no, newPage); Storyboard.SetTargetProperty(no, "Opacity");
        sb.Children.Add(ox); sb.Children.Add(oo); sb.Children.Add(nx); sb.Children.Add(no);
        sb.Completed += (_, _) =>
        {
            sb.Stop();
            oldPage.Visibility = Visibility.Collapsed;
            oldPage.Opacity = 1;
            _pageTrans[old].X = 0;
            newPage.Opacity = 1;
            _pageTrans[index].X = 0;
            _pageAnimLock = false;
        };
        sb.Begin();
    }

    private void WelcomeCarousel_WheelChanged(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        if (_carouselMelting || _pageAnimLock) { e.Handled = true; return; }
        var delta = e.GetCurrentPoint(WelcomeCarousel).Properties.MouseWheelDelta;
        if (delta < 0) SetCarouselPage(_carouselPage + 1);
        else if (delta > 0) SetCarouselPage(_carouselPage - 1);
        e.Handled = true;
    }



    private void CarouselNext_Click(object sender, RoutedEventArgs e)
    {
        if (_carouselMelting || _pageAnimLock) return;
        if (_carouselPage >= CarouselPageList.Length - 1) BeginUseCarouselMelt();
        else SetCarouselPage(_carouselPage + 1);
    }

    private void CarouselSkip_Tapped(object sender, Microsoft.UI.Xaml.Input.TappedRoutedEventArgs e) => CarouselSkip();

    private void CarouselSkip_Click(object sender, RoutedEventArgs e) => CarouselSkip();

    private void CarouselSkip()
    {
        if (_carouselMelting) return;


        _pageSb?.Stop(); _pageSb = null; _pageAnimLock = false;


        int last = CarouselPageList.Length - 1;
        if (_carouselPage < last) SetCarouselPage(last);
    }







    private void BeginUseCarouselMelt()
    {
        if (_carouselMelting) return;

        _carouselMelting = true;
        CarouselNextButton.IsEnabled = false;
        StopCarouselFloat();
        var brand = App.GetBrush("AppPrimaryButtonBrush").Color;
        var pt = CarouselNextButton.TransformToVisual(WelcomeCarousel)
                    .TransformPoint(new Windows.Foundation.Point(CarouselNextButton.ActualWidth / 2, CarouselNextButton.ActualHeight / 2));
        PaintMeltBlob(CarouselMeltBlob0, pt, brand, 0.88f, 0.42f);
        PaintMeltBlob(CarouselMeltBlob1, pt, brand, 0.62f, 0.28f);
        PaintMeltBlob(CarouselMeltBlob2, pt, brand, 0.45f, 0.20f);

        double far = MeltFarRadius(pt, WelcomeCarousel.ActualWidth, WelcomeCarousel.ActualHeight);


        try
        {
            CarouselMeltLayer.Visibility = Visibility.Visible;


            SpreadBlob(CarouselMeltBlob0, far * 1.55, 0, 1650);
            SpreadBlob(CarouselMeltBlob1, far * 1.40, 180, 1850);
            SpreadBlob(CarouselMeltBlob2, far * 1.75, 360, 2050);
        }
        catch (Exception ex)
        {
            Services.CrashLogger.LogNote("CarouselMelt", "spread composition failed: " + ex.Message);
            InstantCoverBlob(CarouselMeltBlob0, far * 1.30);
            InstantCoverBlob(CarouselMeltBlob1, far * 1.18);
            InstantCoverBlob(CarouselMeltBlob2, far * 1.45);
            CarouselMeltLayer.Visibility = Visibility.Visible;
        }


        var sb = new Storyboard();
        _meltSb = sb;
        AddMeltDouble(sb, CarouselNextButton, "Opacity", 1, 0, 180, Motion.Accelerate());
        AddMeltDouble(sb, CarouselMeltBlob0, "Opacity", 0, 1.0, 240, null, 0);
        AddMeltDouble(sb, CarouselMeltBlob1, "Opacity", 0, 0.9, 240, null, 150);
        AddMeltDouble(sb, CarouselMeltBlob2, "Opacity", 0, 0.8, 240, null, 300);

        AddMeltDouble(sb, CarouselLayout, "Opacity", 1, 0, 700, Motion.Decelerate(), 200);

        AddMeltDouble(sb, CarouselMeltLayer, "Opacity", 1, 0, 420, Motion.Standard(), 1800);
        AddMeltDouble(sb, CarouselFullBg, "Opacity", 0.94, 0, 420, Motion.Standard(), 1800);
        AddMeltDouble(sb, CarouselTint, "Opacity", 0.3, 0, 420, Motion.Standard(), 1800);
        try
        {
            if (_carouselBlurBrush != null)
            {
                var blur = _carouselBlurBrush.Compositor.CreateScalarKeyFrameAnimation();
                blur.DelayBehavior = Microsoft.UI.Composition.AnimationDelayBehavior.SetInitialValueBeforeDelay;
                blur.DelayTime = TimeSpan.FromMilliseconds(1800);
                blur.Duration = TimeSpan.FromMilliseconds(420);
                blur.InsertKeyFrame(0f, 6f);
                blur.InsertKeyFrame(1f, 0f);
                _carouselBlurBrush.Properties.StartAnimation("Blur.BlurAmount", blur);
            }
        }
        catch (Exception ex)
        {
            Services.CrashLogger.LogNote("CarouselMelt", "blur reveal failed: " + ex.Message);
        }
        sb.Completed += (_, _) =>
        {
            sb.Stop();
            FinishCarouselMelt();
        };
        sb.Begin();
    }



    private static void SpreadBlob(Microsoft.UI.Xaml.Shapes.Ellipse blob, double radiusTarget, int delayMs, int durationMs)
    {
        var visual = Microsoft.UI.Xaml.Hosting.ElementCompositionPreview.GetElementVisual(blob);
        visual.CenterPoint = new System.Numerics.Vector3(80, 80, 0);
        var comp = visual.Compositor;
        var scale = comp.CreateScalarKeyFrameAnimation();
        scale.DelayBehavior = Microsoft.UI.Composition.AnimationDelayBehavior.SetInitialValueBeforeDelay;
        scale.DelayTime = TimeSpan.FromMilliseconds(delayMs);
        scale.Duration = TimeSpan.FromMilliseconds(durationMs);
        scale.InsertKeyFrame(0f, 0.35f);
        scale.InsertKeyFrame(1f, (float)(radiusTarget / 80.0),
            Microsoft.UI.Composition.CompositionEasingFunction.CreateCubicBezierEasingFunction(
                comp, new System.Numerics.Vector2(0.33f, 0.6f), new System.Numerics.Vector2(0.6f, 1f)));
        visual.StartAnimation("Scale.X", scale);
        visual.StartAnimation("Scale.Y", scale);
    }


    private static void InstantCoverBlob(Microsoft.UI.Xaml.Shapes.Ellipse blob, double radiusTarget)
    {
        try
        {
            var visual = Microsoft.UI.Xaml.Hosting.ElementCompositionPreview.GetElementVisual(blob);
            visual.StopAnimation("Scale.X");
            visual.StopAnimation("Scale.Y");
            visual.CenterPoint = new System.Numerics.Vector3(80, 80, 0);
            visual.Scale = new System.Numerics.Vector3((float)(radiusTarget / 80.0), (float)(radiusTarget / 80.0), 1);
        }
        catch { }
    }

    private void FinishCarouselMelt()
    {
        ResetCarouselMeltLayer();
        CloseWelcomeCarousel();
    }



    private void ResetCarouselMeltLayer()
    {
        _carouselMelting = false;
        CarouselMeltLayer.Visibility = Visibility.Collapsed;
        ResetMeltVisual(CarouselMeltBlob0);
        ResetMeltVisual(CarouselMeltBlob1);
        ResetMeltVisual(CarouselMeltBlob2);
        CarouselMeltLayer.Opacity = 1;
        CarouselFullBg.Opacity = 0.94;
        CarouselTint.Opacity = 0.3;
        CarouselLayout.Opacity = 1;
        CarouselNextButton.Opacity = 1;
        CarouselNextButton.IsEnabled = true;
        if (_carouselBlurBrush != null)
        {
            _carouselBlurBrush.Properties.StopAnimation("Blur.BlurAmount");
            _carouselBlurBrush.Properties.InsertScalar("Blur.BlurAmount", 6f);
        }
    }

    private static void ResetMeltVisual(Microsoft.UI.Xaml.Shapes.Ellipse blob)
    {
        try
        {
            var visual = Microsoft.UI.Xaml.Hosting.ElementCompositionPreview.GetElementVisual(blob);
            visual.StopAnimation("Scale.X");
            visual.StopAnimation("Scale.Y");
            visual.Scale = new System.Numerics.Vector3(1, 1, 1);
        }
        catch { }
        blob.Opacity = 0;
    }


    private static double MeltFarRadius(Windows.Foundation.Point pt, double w, double h)
        => Math.Sqrt(Math.Max(
            Math.Max(pt.X * pt.X + pt.Y * pt.Y, (w - pt.X) * (w - pt.X) + pt.Y * pt.Y),
            Math.Max(pt.X * pt.X + (h - pt.Y) * (h - pt.Y), (w - pt.X) * (w - pt.X) + (h - pt.Y) * (h - pt.Y))));

    private static void PaintMeltBlob(Microsoft.UI.Xaml.Shapes.Ellipse blob, Windows.Foundation.Point center, Windows.UI.Color brand, float coreAlpha, float midAlpha)
    {
        blob.Margin = new Thickness(center.X - 80, center.Y - 80, 0, 0);
        var fill = new Microsoft.UI.Xaml.Media.RadialGradientBrush();
        fill.GradientStops.Add(new Microsoft.UI.Xaml.Media.GradientStop { Color = WithAlpha(brand, coreAlpha), Offset = 0 });
        fill.GradientStops.Add(new Microsoft.UI.Xaml.Media.GradientStop { Color = WithAlpha(brand, midAlpha), Offset = 0.55 });
        fill.GradientStops.Add(new Microsoft.UI.Xaml.Media.GradientStop { Color = WithAlpha(brand, 0f), Offset = 1 });
        blob.Fill = fill;
    }

    private static Windows.UI.Color WithAlpha(Windows.UI.Color c, float alpha)
        => Windows.UI.Color.FromArgb((byte)Math.Clamp((int)Math.Round(alpha * 255f), 0, 255), c.R, c.G, c.B);

    private static void AddMeltDouble(Storyboard sb, DependencyObject target, string prop, double from, double to, int ms,
        Microsoft.UI.Xaml.Media.Animation.EasingFunctionBase? ease = null, int beginMs = 0)
    {

        var da = new DoubleAnimation { From = from, To = to, Duration = TimeSpan.FromMilliseconds(ms) };
        if (ease != null) da.EasingFunction = ease;
        if (beginMs > 0) da.BeginTime = TimeSpan.FromMilliseconds(beginMs);
        Storyboard.SetTarget(da, target);
        Storyboard.SetTargetProperty(da, prop);
        sb.Children.Add(da);
    }

    private void ShowMainContent(bool replayDeferred = true, bool landingContentOnly = false)
    {




        if (!landingContentOnly)
        {
            BottomToolbarPanel.Visibility = Visibility.Visible;
            CustomTitleBar.Visibility = Visibility.Visible;
        }
        NavBar.Visibility = Visibility.Visible;

        var (tab, navButton) = FirstVisibleTab();
        NavigateToPage(tab);
        UpdateNavSelection(navButton);
        UpdateNavBarVisibility();
        if (!landingContentOnly)
        {
            UpdateWorkspaceSwitcher();
        }
        if (replayDeferred) ReplayDeferredExternalRequests();
    }



    private void ShowMainChrome()
    {
        BottomToolbarPanel.Visibility = Visibility.Visible;
        CustomTitleBar.Visibility = Visibility.Visible;
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
        App.ShowToast(App.GetString("Common_Tab_Hidden_Toast"), ToastTone.Error);
        System.Diagnostics.Debug.WriteLine($"action for hidden tab '{requestedTag}' dropped");
    }

    private void WelcomeOverlay_Tapped(object sender, Microsoft.UI.Xaml.Input.TappedRoutedEventArgs e)
    {


        if (App.Store is not { IsLoaded: true }) return;
        if (_welcomeFading) return;
        _welcomeFading = true;



        if (App.IsAnimationsEnabled && BeginWelcomeShatter(e.GetPosition((FrameworkElement)Content)))
            return;

        if (App.IsAnimationsEnabled && BeginWelcomeMelt(e.GetPosition((FrameworkElement)Content)))
            return;
        WelcomeExitSimple();
    }


    private void WelcomeExitSimple()
    {
        var fadeOut = new Storyboard();
        var oa = new DoubleAnimation { From = 1, To = 0, Duration = TimeSpan.FromMilliseconds(400) };
        Storyboard.SetTarget(oa, WelcomeOverlay);
        Storyboard.SetTargetProperty(oa, "Opacity");
        fadeOut.Children.Add(oa);
        fadeOut.Completed += (s, ev) =>
        {

            if (LockScreenFrame.Visibility == Visibility.Visible)
            {
                _welcomeFading = false;
                WelcomeOverlay.Visibility = Visibility.Collapsed;
                return;
            }
            _welcomeFading = false;
            WelcomeOverlay.Visibility = Visibility.Collapsed;
            CommitWelcomeExitWithRetry();
            ShowMainContent();
            MaybeShowWelcomeCarousel();
            NavBar.Opacity = 0;
            var fadeIn = new Storyboard();
            var oi = new DoubleAnimation { From = 0, To = 1, Duration = TimeSpan.FromMilliseconds(400), EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
            Storyboard.SetTarget(oi, NavBar);
            Storyboard.SetTargetProperty(oi, "Opacity");
            fadeIn.Children.Add(oi);
            fadeIn.Begin();
        };
        fadeOut.Begin();
    }



    private void WarmWelcomeShatter()
    {
        PrepareWelcomeCarousel();
        if (_shatter != null || _shatterWarming) return;
        _shatterWarming = true;
        _ = DispatcherQueue.TryEnqueue(async () =>
        {
            try
            {
                var root = Content as FrameworkElement;
                var win = new Windows.Foundation.Size(
                    Math.Max(320, root?.ActualWidth ?? 1280), Math.Max(240, root?.ActualHeight ?? 800));
                _shatter = await Services.SandShatter.TryCreateAsync(win);
                if (_shatter != null)
                {
                    WelcomeShatterHost.Children.Clear();
                    WelcomeShatterHost.Children.Add(_shatter.Layer);
                    _shatter.Finished += OnShatterFinished;
                }
            }
            catch (Exception ex)
            {
                Services.CrashLogger.LogNote("SandShatter", "warm hook failed: " + ex);
                _shatter = null;
            }
            finally
            {
                _shatterWarming = false;
            }
        });
    }


    private void OnShatterFinished(object? sender, EventArgs e)
    {
        _shatterLockWatch?.Stop();


        if (_shatter?.TakeSummary() is { } perfSummary)
            DispatcherQueue.TryEnqueue(() =>
                _ = System.Threading.Tasks.Task.Run(() => Services.CrashLogger.LogPerf("SandShatter", perfSummary)));
        if (_welcomeResizeHook is not null && Content is FrameworkElement wroot)
        {
            wroot.SizeChanged -= _welcomeResizeHook;
            _welcomeResizeHook = null;
        }
        _shatterFade?.Stop();
        _shatterFade = null;
        WelcomeShatterHost.Visibility = Visibility.Collapsed;
        WelcomeLogo.Opacity = 1;
        WelcomeTitleGroup.Opacity = 1;
        WelcomeSubtitle.Opacity = 1;
        WelcomeHintGroup.Opacity = 1;
        WelcomeVersionBadge.Opacity = 0.55;



        if (WelcomeOverlay.Visibility != Visibility.Visible && LockScreenFrame.Visibility != Visibility.Visible)
        {
            DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
            {
                var swCar = System.Diagnostics.Stopwatch.StartNew();
                MaybeShowWelcomeCarousel();
                long msCar = swCar.ElapsedMilliseconds;
                _ = System.Threading.Tasks.Task.Run(() =>
                    Services.CrashLogger.LogPerf("SandShatter", $"carousel show (deferred): {msCar}ms"));
            });
        }
    }





    private Microsoft.UI.Xaml.SizeChangedEventHandler? _welcomeResizeHook;

    private void OnWelcomeRootSizeChanged(object sender, SizeChangedEventArgs e)
    {


        _shatter?.Resize(new Windows.Foundation.Size(e.NewSize.Width, e.NewSize.Height));
    }

    private bool BeginWelcomeShatter(Windows.Foundation.Point pt)
    {
        if (_shatter == null) { WarmWelcomeShatter(); return false; }
        var root = (FrameworkElement)Content;
        if (root == null || root.ActualWidth < 50 || root.ActualHeight < 50) return false;
        try
        {




            WelcomeEntryAnimation.Stop();

            var logoRect = WelcomeLogo.TransformToVisual(root)
                .TransformBounds(new Windows.Foundation.Rect(0, 0, WelcomeLogo.ActualWidth, WelcomeLogo.ActualHeight));
            _shatter.Burst(logoRect, pt, new Windows.Foundation.Size(root.ActualWidth, root.ActualHeight));
            WelcomeShatterHost.Visibility = Visibility.Visible;
            WelcomeLogo.Opacity = 0;


            _welcomeSaveTask = Task.Run(() => WelcomeExitCommit());


            ShowMainContent(replayDeferred: false, landingContentOnly: true);



            _shatterLockWatch?.Stop();
            _shatterLockWatch = DispatcherQueue.CreateTimer();
            _shatterLockWatch.Interval = TimeSpan.FromMilliseconds(100);
            _shatterLockWatch.IsRepeating = true;
            _shatterLockWatch.Tick += (_, _) =>
            {
                if (LockScreenFrame.Visibility != Visibility.Visible) return;
                _shatterLockWatch?.Stop();
                _shatter?.Cancel();
                OnShatterFinished(null, EventArgs.Empty);
                _welcomeFading = false;
            };
            _shatterLockWatch.Start();
            if (_welcomeResizeHook is null)
            {
                _welcomeResizeHook = OnWelcomeRootSizeChanged;
                root.SizeChanged += _welcomeResizeHook;
            }


            var sb = new Storyboard();
            AddMeltDouble(sb, WelcomeTitleGroup, "Opacity", 1, 0, 350, Motion.Standard(), 0);
            AddMeltDouble(sb, WelcomeSubtitle, "Opacity", 1, 0, 350, Motion.Standard(), 0);
            AddMeltDouble(sb, WelcomeHintGroup, "Opacity", 1, 0, 350, Motion.Standard(), 0);
            AddMeltDouble(sb, WelcomeVersionBadge, "Opacity", 0.55, 0, 350, Motion.Standard(), 0);
            _shatterFade = sb;
            sb.Begin();

            var prep = DispatcherQueue.CreateTimer();
            prep.Interval = TimeSpan.FromMilliseconds(620);
            prep.IsRepeating = false;
            prep.Tick += (_, _) =>
            {
                prep.Stop();
                var swTick = System.Diagnostics.Stopwatch.StartNew();


                if (LockScreenFrame.Visibility == Visibility.Visible)
                {
                    _shatter?.Cancel();
                    OnShatterFinished(null, EventArgs.Empty);
                    _welcomeFading = false;


                    return;
                }
                _welcomeFading = false;



                bool committed = false;
                try
                {
                    var saveTask = _welcomeSaveTask;
                    if (saveTask is not null && saveTask.Wait(2000) && saveTask.Status == TaskStatus.RanToCompletion)
                        committed = saveTask.Result;
                }
                catch (Exception ex)
                {
                    CrashLogger.LogNote("Welcome", "exit commit wait: " + ex.Message);
                }

                if (!committed) CommitWelcomeExitWithRetry();
                long msCommit = swTick.ElapsedMilliseconds;
                WelcomeOverlay.Visibility = Visibility.Collapsed;
                UpdateNavBarVisibility();
                ShowMainChrome();
                ReplayDeferredExternalRequests();
                long msMain = swTick.ElapsedMilliseconds;

                NavBar.Opacity = 0;
                var fadeIn = new Storyboard();
                var oi = new DoubleAnimation { From = 0, To = 1, Duration = TimeSpan.FromMilliseconds(400), EasingFunction = new CubicEase { EasingMode = Microsoft.UI.Xaml.Media.Animation.EasingMode.EaseOut } };
                Storyboard.SetTarget(oi, NavBar);
                Storyboard.SetTargetProperty(oi, "Opacity");
                fadeIn.Children.Add(oi);
                fadeIn.Begin();
                _ = System.Threading.Tasks.Task.Run(() => Services.CrashLogger.LogPerf("SandShatter",
                    $"swap tick: commit={msCommit}ms main={msMain - msCommit}ms total={swTick.ElapsedMilliseconds}ms"));
            };
            prep.Start();
            return true;
        }
        catch (Exception ex)
        {
            Services.CrashLogger.LogNote("SandShatter", "burst failed: " + ex.Message);
            _shatter.Cancel();
            OnShatterFinished(null, EventArgs.Empty);
            return false;
        }
    }






    private bool BeginWelcomeMelt(Windows.Foundation.Point pt)
    {
        var root = (FrameworkElement)Content;
        if (root == null || root.ActualWidth < 50 || root.ActualHeight < 50) return false;
        try
        {
            var brand = App.GetBrush("AppPrimaryButtonBrush").Color;
            double far = MeltFarRadius(pt, root.ActualWidth, root.ActualHeight);
            PaintMeltBlob(WelcomeMeltBlob0, pt, brand, 0.88f, 0.42f);
            PaintMeltBlob(WelcomeMeltBlob1, pt, brand, 0.62f, 0.28f);
            PaintMeltBlob(WelcomeMeltBlob2, pt, brand, 0.45f, 0.20f);
            WelcomeMeltLayer.Visibility = Visibility.Visible;



            SpreadBlob(WelcomeMeltBlob0, far * 1.55, 0, 1560);
            SpreadBlob(WelcomeMeltBlob1, far * 1.40, 140, 1420);
            SpreadBlob(WelcomeMeltBlob2, far * 1.75, 280, 1280);
        }
        catch (Exception ex)
        {
            Services.CrashLogger.LogNote("WelcomeMelt", "spread failed: " + ex.Message);


            WelcomeMeltLayer.Visibility = Visibility.Collapsed;
            ResetMeltVisual(WelcomeMeltBlob0);
            ResetMeltVisual(WelcomeMeltBlob1);
            ResetMeltVisual(WelcomeMeltBlob2);
            return false;
        }
        NavigateToPage(FirstVisibleTab().Tab);
        var sb = new Storyboard();

        AddMeltDouble(sb, WelcomeMeltBlob0, "Opacity", 0, 1.0, 240, null, 0);
        AddMeltDouble(sb, WelcomeMeltBlob1, "Opacity", 0, 0.9, 240, null, 180);
        AddMeltDouble(sb, WelcomeMeltBlob2, "Opacity", 0, 0.8, 240, null, 360);

        AddMeltDouble(sb, WelcomeLogo, "Opacity", 1, 0, 1400, Motion.Standard(), 200);
        AddMeltDouble(sb, WelcomeTitleGroup, "Opacity", 1, 0, 1400, Motion.Standard(), 200);
        AddMeltDouble(sb, WelcomeSubtitle, "Opacity", 1, 0, 1400, Motion.Standard(), 200);
        AddMeltDouble(sb, WelcomeHintGroup, "Opacity", 1, 0, 1400, Motion.Standard(), 200);
        AddMeltDouble(sb, WelcomeVersionBadge, "Opacity", 0.55, 0, 1400, Motion.Standard(), 200);
        AddMeltDouble(sb, WelcomeMeltLayer, "Opacity", 1, 0, 300, Motion.Decelerate(), 1600);


        var prep = DispatcherQueue.CreateTimer();
        prep.Interval = TimeSpan.FromMilliseconds(1600);
        prep.IsRepeating = false;
        prep.Tick += (_, _) =>
        {
            prep.Stop();


            if (LockScreenFrame.Visibility == Visibility.Visible)
            {
                _welcomeFading = false;
                return;
            }
            _welcomeFading = false;
            CommitWelcomeExitWithRetry();
            WelcomeOverlay.Visibility = Visibility.Collapsed;
            ShowMainContent();
            MaybeShowWelcomeCarousel();
            NavBar.Opacity = 0;
            var fadeIn = new Storyboard();
            var oi = new DoubleAnimation { From = 0, To = 1, Duration = TimeSpan.FromMilliseconds(400), EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
            Storyboard.SetTarget(oi, NavBar);
            Storyboard.SetTargetProperty(oi, "Opacity");
            fadeIn.Children.Add(oi);
            fadeIn.Begin();
        };
        prep.Start();
        sb.Completed += (_, _) =>
        {
            sb.Stop();
            WelcomeMeltLayer.Visibility = Visibility.Collapsed;
            WelcomeMeltLayer.Opacity = 1;
            ResetMeltVisual(WelcomeMeltBlob0);
            ResetMeltVisual(WelcomeMeltBlob1);
            ResetMeltVisual(WelcomeMeltBlob2);
            WelcomeLogo.Opacity = 1;
            WelcomeTitleGroup.Opacity = 1;
            WelcomeSubtitle.Opacity = 1;
            WelcomeHintGroup.Opacity = 1;
            WelcomeVersionBadge.Opacity = 0.55;

        };
        sb.Begin();
        return true;
    }



    private void CommitWelcomeExitWithRetry()
    {
        if (WelcomeExitCommit()) return;
        if (!WelcomeExitCommit()) CrashLogger.LogNote("Welcome", "exit commit retry failed");
    }



    private bool WelcomeExitCommit()
    {
        try
        {



            var store = App.Store;
            if (store is not { IsLoaded: true }) return false;
            store.Database.AppSettings.HasCompletedWelcome = true;
            if (store.SaveSync()) return true;
            CrashLogger.LogNote("Welcome", "exit commit was not persisted");
            return false;
        }
        catch (Exception ex)
        {
            CrashLogger.LogNote("Welcome", "exit commit failed: " + ex.Message);
            return false;
        }
    }

    private void NavButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.Tag is string tag)
        {

            if (IsTabAlreadyActive(tag)) { ScrollActiveTabToTop(tag); return; }
            NavigateToPage(tag);
            UpdateNavSelection(button);
            WelcomeOverlay.Visibility = Visibility.Collapsed;
        }
    }


    private bool IsTabAlreadyActive(string tag) => tag switch
    {
        "Memo" => ReferenceEquals(RootFrame.Content, _memoPage),
        "File" => ReferenceEquals(RootFrame.Content, _filePathPage),
        "Plan" => ReferenceEquals(RootFrame.Content, _planPage),
        "Diary" => ReferenceEquals(RootFrame.Content, _diaryPage),
        _ => false,
    };

    private void ScrollActiveTabToTop(string tag)
    {
        switch (tag)
        {
            case "Memo": _memoPage.ScrollToMain(); break;
            case "File": _filePathPage.ScrollToMain(); break;
            case "Plan": _planPage.ScrollToMain(); break;
            case "Diary": _diaryPage.ScrollToMain(); break;
        }
    }

    private readonly List<Action> _deferredExternalRequests = new();
    private readonly HashSet<string> _deferredExternalKeys = new(StringComparer.Ordinal);
    private bool _replayingExternalRequests;
    private readonly HashSet<string> _deferredDropLogged = new(StringComparer.Ordinal);
    private bool _startupPendingConsumed;
    private bool _startupPendingConsuming;


    private const int MaxDeferredExternalRequests = 16;





    private void DeferExternalRequest(Action request, string key)
    {
        if (_deferredExternalRequests.Count >= MaxDeferredExternalRequests)
        {


            if (_deferredDropLogged.Add(key))
                Services.CrashLogger.LogNote("DeferredExternalRequest",
                    $"dropped '{key}': queue is at its {MaxDeferredExternalRequests}-request bound");
            return;
        }


        if (!_deferredExternalKeys.Add(key)) return;
        _deferredExternalRequests.Add(request);
    }

    private void ReplayDeferredExternalRequests()
    {
        if (_deferredExternalRequests.Count == 0 || _replayingExternalRequests) return;
        if (App.Store is not { IsLoaded: true }) return;
        if (WelcomeOverlay.Visibility == Visibility.Visible || IsWelcomeCarouselUp || IsSealSurfaceBlocking()) return;
        _replayingExternalRequests = true;


        if (!DispatcherQueue.TryEnqueue(() =>
        {
            try
            {
                if (App.Store is not { IsLoaded: true }) return;
                if (WelcomeOverlay.Visibility == Visibility.Visible || IsWelcomeCarouselUp || IsSealSurfaceBlocking()) return;
                var requests = _deferredExternalRequests.ToArray();
                _deferredExternalRequests.Clear();
                _deferredExternalKeys.Clear();
                _deferredDropLogged.Clear();
                foreach (var request in requests)
                {


                    try { request(); }
                    catch (Exception ex) { Services.CrashLogger.LogNote("DeferredExternalRequest", ex.Message); }
                }
            }
            finally { _replayingExternalRequests = false; }
        }))
            _replayingExternalRequests = false;
    }




    private void ConsumePendingExternalRequests()
    {
        if (_startupPendingConsumed || _startupPendingConsuming) return;
        _startupPendingConsuming = true;


        var complete = false;
        try
        {

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



        App.ClearPendingLaunchArguments();
        complete = true;
        }
        catch (Exception ex)
        {
            Services.CrashLogger.LogNote("ConsumePendingExternalRequests", ex.Message);
        }
        finally
        {
            _startupPendingConsuming = false;
            if (complete) _startupPendingConsumed = true;
        }
    }



    private bool IsSealSurfaceBlocking()
        => _sealMode
            || SealGuideOverlay.Visibility == Visibility.Visible
            || SealUnlockContent.Visibility == Visibility.Visible;


    public void HandleEditRequest(Guid id)
    {


        if (App.Store is not { IsLoaded: true } || WelcomeOverlay.Visibility == Visibility.Visible
            || IsSealSurfaceBlocking() || IsWelcomeCarouselUp)
        {
            DeferExternalRequest(() => HandleEditRequest(id), "edit:" + id);
            return;
        }
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
        if (App.Store is not { IsLoaded: true } || WelcomeOverlay.Visibility == Visibility.Visible
            || IsSealSurfaceBlocking() || IsWelcomeCarouselUp)
        {
            DeferExternalRequest(() => HandleAddPathRequest(path), "add-path:" + path);
            return;
        }
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
        if (App.Store is not { IsLoaded: true } || WelcomeOverlay.Visibility == Visibility.Visible
            || IsSealSurfaceBlocking() || IsWelcomeCarouselUp)
        {
            DeferExternalRequest(() => HandleImportMdRequest(filePath), "md-import:" + filePath);
            return;
        }
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
        if (App.Store is not { IsLoaded: true } || WelcomeOverlay.Visibility == Visibility.Visible
            || IsSealSurfaceBlocking() || IsWelcomeCarouselUp)
        {
            DeferExternalRequest(() => HandleReminderEditRequest(p), "reminder-edit:" + p.Id);
            return;
        }
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
        if (App.Store is not { IsLoaded: true } || WelcomeOverlay.Visibility == Visibility.Visible
            || IsSealSurfaceBlocking() || IsWelcomeCarouselUp)
        {
            DeferExternalRequest(() => HandleReminderDueRequest(id), "reminder-due:" + id);
            return;
        }
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
        App.ShowToast(App.GetString("Common_Toast_RestorePending"), ToastTone.Error);
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




        if (RootFrame.Content is DiaryEditorPage editor)
        {
            if (_searchOpeningFromEditor) return;
            _searchOpeningFromEditor = true;
            OpenSearchPageAfterEditorFlushAsync(editor);
            return;
        }
        ShowSearchPageCore();
    }

    private bool _searchOpeningFromEditor;

    private async void OpenSearchPageAfterEditorFlushAsync(DiaryEditorPage editor)
    {
        try
        {
            await FlushEditorForExitAsync(editor);


            if (!ReferenceEquals(RootFrame.Content, editor)) return;
            if (LockScreenFrame.Visibility == Visibility.Visible || WelcomeOverlay.Visibility == Visibility.Visible) return;
            if (RestorePendingBlocksNavigation()) { RefuseRestorePending(); return; }
            ShowSearchPageCore();
        }
        finally { _searchOpeningFromEditor = false; }
    }

    private void ShowSearchPageCore()
    {
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




    private bool IsWelcomeCarouselUp => WelcomeCarousel.Visibility == Visibility.Visible;




    private bool CanInvokeGlobalShortcut()
    {
        if (LockScreenFrame.Visibility == Visibility.Visible) return false;
        if (WelcomeOverlay.Visibility == Visibility.Visible) return false;
        if (IsWelcomeCarouselUp) return false;
        if (_sealMode) return false;
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

        if (RestorePendingBlocksNavigation()) { App.ShowToast(App.GetString("Common_Toast_RestorePending"), ToastTone.Error); return; }
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
                if (!IsTabVisible("Diary")) { RefuseHiddenTabAction("Diary"); break; }
                NavigateToEditor();
                break;
            case "new_document":
                if (!IsTabVisible("Diary")) { RefuseHiddenTabAction("Diary"); break; }
                NavigateToEditor(null, "markdown");
                break;
            case "open_settings":
                NavigateToSettings();
                break;
            case "open_trash":
                OpenTrashPage();
                break;
            case "open_seal":
                NavigateToSeal();
                break;
            case "lock_now":
                if (App.Store is { IsEncrypted: true }) LockNow();
                else App.ShowToast(App.GetString("Tray_LockNeedLock"), ToastTone.Error);
                break;
        }
    }


    public void SearchJump(string kind, string id)
    {

        if (RestorePendingBlocksNavigation()) { App.ShowToast(App.GetString("Common_Toast_RestorePending"), ToastTone.Error); return; }


        string tag = kind switch { "memo" => "Memo", "path" => "File", "todo" => "Plan", "note" => "Plan", "diary" => "Diary", _ => "" };
        if (tag != "" && !IsTabVisible(tag)) { App.ShowToast(App.GetString("Common_Tab_Hidden_Toast"), ToastTone.Error); return; }
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
        if (!App.IsAnimationsEnabled) return;
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








    private bool _navGlowRetryHooked;
    private EventHandler<object>? _navGlowRetryHandler;

    private void WireNavGlowRelocation()
    {




        try
        {
            Microsoft.UI.Xaml.Hosting.ElementCompositionPreview.SetIsTranslationEnabled(NavIndicator, true);

            Microsoft.UI.Xaml.Hosting.ElementCompositionPreview.SetIsTranslationEnabled(SealNavIndicator, true);
        }
        catch { }
        NavBar.SizeChanged += (_, _) => RelocateNavGlow();


        SealNavBar.SizeChanged += (_, _) => RelocateSealGlow();
    }



    private void RelocateNavGlow()
    {
        ClampNavTabWidths();
        if (SilentPlaceNavGlow()) return;
        HookNavGlowRetry(animate: false);
    }










    private void ClampNavTabWidths()
    {
        if (NavBar.ActualWidth < 1) return;
        double cap = NavBar.ActualWidth / 4.0 - 16;
        NavMemoHost.MaxWidth = cap;
        NavFileHost.MaxWidth = cap;
        NavPlanHost.MaxWidth = cap;
        NavDiaryHost.MaxWidth = cap;
    }







    private void ClampSealTabWidths()
    {
        if (SealNavBar.ActualWidth < 1) return;
        double cap = SealNavBar.ActualWidth / 4.0 - 16;
        SealNavSpaceHost.MaxWidth = cap;
        SealNavCapsuleHost.MaxWidth = cap;
        SealNavSettingsHost.MaxWidth = cap;
    }


    private bool SilentPlaceNavGlow()
    {
        var selected = _selectedNavButton;
        if (selected == null) return false;
        if (NavBar.Visibility != Visibility.Visible || selected.Visibility != Visibility.Visible
            || selected.ActualWidth < 1) return false;


        if (!NavGlowWidthIsFresh(selected)) return false;
        var pt = selected.TransformToVisual(NavBar).TransformPoint(new Windows.Foundation.Point(0, 0));
        SetNavGlowX(pt.X);
        NavIndicator.Width = selected.ActualWidth;
        NavIndicator.Opacity = 1;
        _navIndicatorPlaced = true;
        return true;
    }





    private bool NavGlowWidthIsFresh(Button selected)
    {
        int cols = NavBar.ColumnDefinitions.Count;
        if (cols <= 0 || NavBar.ActualWidth <= 1) return true;
        double expected = Math.Min(NavBar.ActualWidth / cols, NavBar.ActualWidth / 4.0) - 16;
        return expected <= 1 || Math.Abs(selected.ActualWidth - expected) <= Math.Max(10, expected * 0.2);
    }




    private void HookNavGlowRetry(bool animate)
    {
        if (_navGlowRetryHooked) return;
        _navGlowRetryHooked = true;
        void Unhook()
        {
            var h = _navGlowRetryHandler;
            if (h != null) NavBar.LayoutUpdated -= h;
            _navGlowRetryHandler = null;
            _navGlowRetryHooked = false;
        }
        EventHandler<object> handler = (_, _) =>
        {
            if (NavBar.Visibility != Visibility.Visible)
            {
                Unhook();
                return;
            }
            if (animate)
            {
                var sel = _selectedNavButton;
                if (sel == null || sel.Visibility != Visibility.Visible || sel.ActualWidth < 1) return;
                Unhook();
                UpdateNavIndicator(sel);
                return;
            }
            if (!SilentPlaceNavGlow()) return;
            Unhook();
        };
        _navGlowRetryHandler = handler;
        NavBar.LayoutUpdated += handler;
        DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
        {
            if (!_navGlowRetryHooked) return;
            if (SilentPlaceNavGlow()) { Unhook(); return; }


            if (NavBar.Visibility != Visibility.Visible) Unhook();
        });
    }



    private void UpdateNavIndicator(Button selected)
    {
        if (NavBar.Visibility != Visibility.Visible || selected == null
            || selected.Visibility != Visibility.Visible || selected.ActualWidth < 1)
        {

            HookNavGlowRetry(animate: true);
            return;
        }
        if (!NavGlowWidthIsFresh(selected))
        {


            HookNavGlowRetry(animate: true);
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



    private float _navGlowX;

    private void SetNavGlowX(double x)
    {
        _navGlowX = (float)x;
        try
        {
            var visual = Microsoft.UI.Xaml.Hosting.ElementCompositionPreview.GetElementVisual(NavIndicator);
            visual.StopAnimation("Translation.X");
            var jump = visual.Compositor.CreateScalarKeyFrameAnimation();
            jump.Duration = TimeSpan.FromMilliseconds(1);
            jump.InsertKeyFrame(1.0f, (float)x);
            visual.StartAnimation("Translation.X", jump);
        }
        catch
        {

            try { Microsoft.UI.Xaml.Hosting.ElementCompositionPreview.GetElementVisual(NavIndicator).StopAnimation("Translation.X"); } catch { }
            NavIndicatorTranslate.X = x;
        }
    }

    private void SlideNavGlow(double x)
    {
        var target = (float)x;
        try
        {
            var visual = Microsoft.UI.Xaml.Hosting.ElementCompositionPreview.GetElementVisual(NavIndicator);
            float startX = _navGlowX;
            var comp = visual.Compositor;
            var anim = comp.CreateScalarKeyFrameAnimation();
            anim.Duration = TimeSpan.FromMilliseconds(Services.Motion.Normal);

            anim.InsertKeyFrame(0.4f, Lerp(startX, target, 0.55f));
            anim.InsertKeyFrame(0.75f, Lerp(startX, target, 0.87f));
            anim.InsertKeyFrame(1.0f, target);
            visual.StartAnimation("Translation.X", anim);
            _navGlowX = target;
        }
        catch
        {

            try { Microsoft.UI.Xaml.Hosting.ElementCompositionPreview.GetElementVisual(NavIndicator).StopAnimation("Translation.X"); } catch { }
            NavIndicatorTranslate.X = x;
            _navGlowX = target;
        }
    }

    private static float Lerp(float a, float b, float t) => a + (b - a) * t;



    private int _sealTabIndex = -1;
    private bool _sealIndicatorPlaced;
    private float _sealGlowX;
    private bool _sealGlowRetryHooked;

    private void SealNavSpace_Click(object sender, RoutedEventArgs e) => SelectSealTab(0);
    private void SealNavCapsule_Click(object sender, RoutedEventArgs e) => SelectSealTab(1);
    private void SealNavSettings_Click(object sender, RoutedEventArgs e) => SelectSealTab(2);


    private void SelectSealTab(int index)
    {
        if (index < 0 || index > 2) return;
        bool animateGlow = index != _sealTabIndex;


        if (index != _sealTabIndex) _sealPage?.CloseDialogsOnLeave();
        _sealTabIndex = index;
        _sealPage?.ShowView(index);
        UpdateSealNavSelection(index, animateGlow);
        _sealPage?.StealFocus();
    }





    private Grid[] SealGuidePageList = System.Array.Empty<Grid>();
    private int _sealGuidePage = -1;
    private Storyboard? _sealGuideMarqueeSb;
    private double _sealGuideMarqueeCapacity;
    private int _sealGuideMarqueeGen;


    private void ShowSealGuideAt(int index)
    {
        SealUnlockContent.Visibility = Visibility.Collapsed;
        SealGateFooterPanel.Visibility = Visibility.Collapsed;
        SealGuidePages.Visibility = Visibility.Visible;
        SealGuideBottomBar.Visibility = Visibility.Visible;
        SealGuideOverlay.Visibility = Visibility.Visible;
        SealGuideOverlay.UpdateLayout();

        if (index != SealGuidePageList.Length - 1) EnqueueSealGuideMarqueeBuild();
        ApplySealGuidePage1Texts();
        ApplySealGuidePage2Texts();
        ApplySealGuidePage3Texts();
        ApplySealGuidePage4Texts();
        ApplySealGuidePage5Texts();
        SetSealGuidePage(index);
    }



    private void EnqueueSealGuideMarqueeBuild()
    {
        var gen = ++_sealGuideMarqueeGen;
        _sealGuideMarqueeSb?.Stop();
        _sealGuideMarqueeSb = null;
        DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
        {
            if (gen != _sealGuideMarqueeGen) return;
            BuildSealGuideMarquee();
        });
    }


    private void EnsureSealGuidePage1Art()
    {
        SealGuideP1TabSpaceIcon.Data = BuildNonzeroGeometry(IconData.SealSpace);
        SealGuideP1TabCapsuleIcon.Data = BuildNonzeroGeometry(IconData.Capsule);
    }

    private void ApplySealGuidePage1Texts()
    {

        SealGuideP1Title.Inlines.Clear();
        SealGuideP1Title.Inlines.Add(new Microsoft.UI.Xaml.Documents.Run { Text = App.GetString("Seal_Guide5_P1_Title_A") });
        SealGuideP1Title.Inlines.Add(new Microsoft.UI.Xaml.Documents.Run
        {
            Text = App.GetString("Seal_Name"),
            FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Assets/Fonts/Comfortaa.ttf#Comfortaa"),
        });
        SealGuideP1Title.Inlines.Add(new Microsoft.UI.Xaml.Documents.Run { Text = App.GetString("Seal_Guide5_P1_Title_B") });

        SealGuideP1TabSpaceText.Text = App.GetString("Seal_Tab_Space");
        SealGuideP1TabCapsuleText.Text = App.GetString("Seal_Tab_Capsule");
        SealGuideP1S1.Text = App.GetString("Seal_Guide5_P1_S1");
        SealGuideP1S2.Text = App.GetString("Seal_Guide5_P1_S2");
        SealGuideP1S3.Text = App.GetString("Seal_Guide5_P1_S3");
        SealGuideP1S4.Text = App.GetString("Seal_Guide5_P1_S4");
        SealGuideP1S5.Text = App.GetString("Seal_Guide5_P1_S5");
        SealGuideP1S6.Text = App.GetString("Seal_Guide5_P1_S6");
    }

    private void ApplySealGuidePage2Texts()
    {
        SealGuideP2Sentence.Text = App.GetString("Seal_Guide5_P2_Sentence");
        SealGuideP2Subline.Text = App.GetString("Seal_Guide5_P2_Subline");
    }

    private void ApplySealGuidePage3Texts()
    {
        SealGuideP3Kicker.Text = App.GetString("Seal_Tab_Capsule");

        SealGuideP3Title.Inlines.Clear();
        SealGuideP3Title.Inlines.Add(new Microsoft.UI.Xaml.Documents.Run { Text = App.GetString("Seal_Guide5_P3_TitleA") });
        SealGuideP3Title.Inlines.Add(new Microsoft.UI.Xaml.Documents.Run
        {
            Text = App.GetString("Seal_Guide5_P3_TitleNum"),
            Foreground = App.GetBrush("AppPrimaryButtonBrush"),
        });
        SealGuideP3Title.Inlines.Add(new Microsoft.UI.Xaml.Documents.Run { Text = App.GetString("Seal_Guide5_P3_TitleB") });
        SealGuideP3Start.Text = App.GetString("Seal_Guide5_P3_Tick1");
        SealGuideP3End.Text = App.GetString("Seal_Guide5_P3_End");
        SealGuideP3Body.Text = App.GetString("Seal_Guide5_P3_Body");
        SealGuideP3L1.Text = App.GetString("Seal_Guide5_P3_L1");
        SealGuideP3L2.Text = App.GetString("Seal_Guide5_P3_L2");
        SealGuideP3L3.Text = App.GetString("Seal_Guide5_P3_L3");
        SealGuideP3L4.Text = App.GetString("Seal_Guide5_P3_L4");
        SealGuideP3L5.Text = App.GetString("Seal_Guide5_P3_L5");
        SealGuideP3L6.Text = App.GetString("Seal_Guide5_P3_L6");
    }



    private void ApplySealGuidePage4Texts()
    {
        SealGuideP4Kicker.Text = App.GetString("Seal_Guide5_P4_Kicker");
        SealGuideP4Title.Text = App.GetString("Seal_Guide5_P4_Title");
        SealGuideP4Lead.Text = App.GetString("Seal_Guide5_P4_Lead");
        SealGuideP4PasswordBox.PlaceholderText = App.GetString("Lock_Password_Placeholder");
        SealGuideP4RightName.Text = App.GetString("Seal_Name");
        SealGuideP4LeftCap.Text = App.GetString("Seal_Guide5_P4_LeftCap");
        SealGuideP4RightCap.Text = App.GetString("Seal_Guide5_P4_RightCap");
        SealGuideP4Foot1.Text = App.GetString("Seal_Guide5_P4_Foot1");
        SealGuideP4Foot2.Text = App.GetString("Seal_Guide5_P4_Foot2");
    }



    private void ApplySealGuidePage5Texts()
    {
        SealGuideP5Brand.Text = App.GetString("Seal_Name");
        SealGuideP5Sub.Text = App.GetString("Seal_Guide5_P5_Sub");
        SealGuideP5C1T.Text = App.GetString("Seal_Guide5_P5_C1_T");
        SealGuideP5C1D.Text = App.GetString("Seal_Guide5_P5_C1_D");
        SealGuideP5C2T.Text = App.GetString("Seal_Guide5_P5_C2_T");
        SealGuideP5C2D.Text = App.GetString("Seal_Guide5_P5_C2_D");
        SealGuideP5C3T.Text = App.GetString("Seal_Guide5_P5_C3_T");
        SealGuideP5C3D.Text = App.GetString("Seal_Guide5_P5_C3_D");
        SealGuideP5C4T.Text = App.GetString("Seal_Guide5_P5_C4_T");
        SealGuideP5C4D.Text = App.GetString("Seal_Guide5_P5_C4_D");
        SealGuideP5C5T.Text = App.GetString("Seal_Guide5_P5_C5_T");
        SealGuideP5C5D.Text = App.GetString("Seal_Guide5_P5_C5_D");
        SealGuideP5C6T.Text = App.GetString("Seal_Guide5_P5_C6_T");
        SealGuideP5C6D.Text = App.GetString("Seal_Guide5_P5_C6_D");
        SealGuideP5Note.Text = App.GetString("Seal_Guide5_P5_Note");
    }




    private void SetSealGuidePage(int index)
    {
        if (index < 0 || index >= SealGuidePageList.Length) return;
        if (_sealGuidePage == index)
        {
            UpdateSealGuideBottomBar(index);
            return;
        }
        _sealGuidePage = index;
        UpdateSealGuideBottomBar(index);
        if (index == SealGuidePageList.Length - 1) MarkSpaceGuideSeen();
        UpdateSealGuideFields(index);
        for (int i = 0; i < SealGuidePageList.Length; i++)
        {
            SealGuidePageList[i].Visibility = i == index ? Visibility.Visible : Visibility.Collapsed;
        }

        if (index < SealGuidePageList.Length - 1 && _sealGuideMarqueeSb == null) EnqueueSealGuideMarqueeBuild();
        SealGuidePrimaryButton.Focus(FocusState.Programmatic);
    }

    private void UpdateSealGuideBottomBar(int index)
    {
        bool last = index == SealGuidePageList.Length - 1;
        SealGuidePrimaryButton.Content = App.GetString(last ? "Seal_FirstRun_SetPassword" : "Seal_Tour_Next");
        SealGuideSkipButton.Content = App.GetString(last ? "Seal_FirstRun_Skip" : "Seal_Tour_Skip");

        SealGuideImportButton.Content = App.GetString("Seal_FirstRun_Import");
        SealGuideImportButton.Visibility = last ? Visibility.Visible : Visibility.Collapsed;
    }


    private void UpdateSealGuideFields(int index)
    {
        SealGuideP2Field.Visibility = index == 1 ? Visibility.Visible : Visibility.Collapsed;
        SealGuideP3Field.Visibility = index == 2 ? Visibility.Visible : Visibility.Collapsed;
        if (index == 1)
        {
            EnsureSealGuideMarqueeCapacity();
            if (_sealGuideMarqueeSb == null) BuildSealGuideMarquee();
            _sealGuideMarqueeSb?.Begin();
        }
        else _sealGuideMarqueeSb?.Stop();
    }




    private void BuildSealGuideMarquee()
    {
        _sealGuideMarqueeGen++;
        var rows = new[] { SealGuideMarqueeRow0, SealGuideMarqueeRow1, SealGuideMarqueeRow2,
                           SealGuideMarqueeRow3, SealGuideMarqueeRow4, SealGuideMarqueeRow5 };
        foreach (var row in rows) row.Children.Clear();
        _sealGuideMarqueeSb?.Stop();
        var all = SealGlyphs.All.ToList();
        var rng = new Random();
        for (int i = all.Count - 1; i > 0; i--)
        {
            int j = rng.Next(i + 1);
            (all[i], all[j]) = (all[j], all[i]);
        }
        const double iconSize = 28;
        const double gap = 40;
        const double step = iconSize + gap;
        const double rowWidth = 14 * step;
        double windowWidth = SealGuideBleed.ActualWidth;
        if (windowWidth < 50) windowWidth = 800;
        int copies = Math.Max(2, (int)Math.Ceiling((windowWidth + rowWidth) / rowWidth));
        _sealGuideMarqueeCapacity = copies * rowWidth;
        var sb = new Storyboard();
        for (int r = 0; r < 6; r++)
        {
            var canvas = new Canvas
            {
                Width = rowWidth * copies,
                Height = 56,
                HorizontalAlignment = HorizontalAlignment.Left,
            };
            for (int copy = 0; copy < copies; copy++)
            {
                for (int k = 0; k < 14; k++)
                {
                    var glyph = all[(r * 14 + k) % all.Count];
                    var icon = SealIconFactory.Create(glyph, iconSize);
                    icon.Opacity = 0.45 + rng.NextDouble() * 0.15;
                    icon.IsHitTestVisible = false;
                    Canvas.SetLeft(icon, copy * rowWidth + k * step);
                    Canvas.SetTop(icon, 14);
                    canvas.Children.Add(icon);
                }
            }
            var tx = new TranslateTransform();
            canvas.RenderTransform = tx;
            rows[r].Children.Add(canvas);
            bool right = r % 2 == 0;
            var anim = new DoubleAnimation
            {
                From = right ? -rowWidth : 0,
                To = right ? 0 : -rowWidth,
                Duration = TimeSpan.FromSeconds(16 + rng.NextDouble() * 10),
                RepeatBehavior = RepeatBehavior.Forever,
            };
            Storyboard.SetTarget(anim, tx);
            Storyboard.SetTargetProperty(anim, "X");
            sb.Children.Add(anim);
        }


        _sealGuideMarqueeSb = sb;


        if (SealGuideP2Field.Visibility == Visibility.Visible) sb.Begin();
    }



    private void EnsureSealGuideMarqueeCapacity()
    {
        const double rowWidth = 14 * 68;
        var width = SealGuideBleed.ActualWidth;
        if (width < 50) return;
        if (width + rowWidth > _sealGuideMarqueeCapacity) BuildSealGuideMarquee();
    }


    private void SealGuide_WheelChanged(object sender, PointerRoutedEventArgs e)
    {
        if (SealGuidePages.Visibility != Visibility.Visible || _sealGuidePage < 0)
        {
            e.Handled = true;
            return;
        }
        var delta = e.GetCurrentPoint(SealGuideOverlay).Properties.MouseWheelDelta;
        if (delta < 0) SetSealGuidePage(_sealGuidePage + 1);
        else if (delta > 0) SetSealGuidePage(_sealGuidePage - 1);
        e.Handled = true;
    }

    private void SealGuidePrimary_Click(object sender, RoutedEventArgs e)
    {
        if (_sealGuidePage >= 0 && _sealGuidePage < SealGuidePageList.Length - 1)
        {
            SetSealGuidePage(_sealGuidePage + 1);
            return;
        }
        SealGuideSetPwd_Click(sender, e);
    }



    private void SealGuideImport_Click(object sender, RoutedEventArgs e)
    {
        EnterSealShell(skipGate: true);
        _sealPage?.ShowSealImportDialog();
    }

    private void SealGuideSkip_Click(object sender, RoutedEventArgs e)
    {
        if (_sealGuidePage >= 0 && _sealGuidePage < SealGuidePageList.Length - 1)
        {
            SetSealGuidePage(SealGuidePageList.Length - 1);
            return;
        }
        HideSealGuide();



        _sealPage?.StealFocus();
    }



    private void SealGuideSetPwd_Click(object sender, RoutedEventArgs e)
    {
        HideSealGuide();
        _sealPage?.ShowPasswordDialog(succeeded =>
        {
            if (succeeded) return;
            ShowSealGuideAt(SealGuidePageList.Length - 1);
        });
    }


    private void HideSealGuide()
    {

        _sealCountdownBreathing?.Stop();
        SealLockCountdown.Opacity = 1;
        _sealGuidePage = -1;
        _sealGuideMarqueeSb?.Stop();
        SealGuideP2Field.Visibility = Visibility.Collapsed;
        SealGuideP3Field.Visibility = Visibility.Collapsed;
        SealGuidePages.Visibility = Visibility.Collapsed;
        SealGuideBottomBar.Visibility = Visibility.Collapsed;
        foreach (var page in SealGuidePageList)
        {
            page.Visibility = Visibility.Collapsed;
        }
        SealGuideOverlay.Visibility = Visibility.Collapsed;
    }

    private void MarkSpaceGuideSeen()
    {
        var s = App.Store?.Database.AppSettings;
        if (s != null && !s.SpaceGuideSeen)
        {
            s.SpaceGuideSeen = true;
            _ = App.Store?.SaveAsync();
        }
    }



    private Novara.Services.SealLockout? _sealLockout;
    private Microsoft.UI.Dispatching.DispatcherQueueTimer? _sealLockTimer;
    private bool _sealUnlockBusy;
    private bool _sealUnlockErrorActive;
    private bool _sealShakeRunning;
    private bool _sealLockedShown;
    private bool _sealUnlockExiting;

    private bool _sealReturning;
    private Storyboard? _sealEntrySb;
    private Storyboard? _sealExitSb;
    private Storyboard? _sealCountdownBreathing;

    private static bool SpaceIsInitialized()
        => new Novara.Services.SealSpaceStore(Novara.Services.SealSpacePaths.Default).Exists();




    private void ShowSealGate()
    {
        if (SpaceIsInitialized())
        {
            ShowSealUnlockPage();
            return;
        }
#if DEBUG
        ShowSealGuideAt(0);
#else
        var seen = App.Store?.Database.AppSettings?.SpaceGuideSeen ?? false;
        ShowSealGuideAt(seen ? SealGuidePageList.Length - 1 : 0);
#endif
    }







    private bool _sealForgotCdReady;
    private bool _sealForgotConfirming;
    private bool _sealForgotHiding;
    private Storyboard? _sealForgotHideSb;

    private void SealForgot_Tapped(object sender, TappedRoutedEventArgs e) => ShowSealForgotDialog();


    private void SealGateBack_Tapped(object sender, TappedRoutedEventArgs e) => TryLeaveSealGate();

    private void ShowSealForgotDialog()
    {
        _sealForgotConfirming = false;
        _sealForgotCdReady = false;
        SealForgotDangerIcon.Data = App.CreateGeometry(IconData.Danger);
        SealForgotTitle.Text = App.GetString("Seal_Forgot_Title");
        SealForgotWarning.Text = App.GetString("Seal_Forgot_Warning");
        SealForgotPhraseLabel.Text = App.GetString("Seal_Forgot_PhraseLabel");
        SealForgotPhrase.Text = App.GetString("Seal_Forgot_Phrase");
        SealForgotCancelButton.Content = App.GetString("Common_Button_Cancel");
        SealForgotConfirmButton.Content = App.GetString("Seal_Forgot_Confirm");
        SealForgotPhraseBox.Text = "";
        SealForgotConfirmButton.IsEnabled = false;
        SealForgotConfirmButton.Opacity = Motion.CooldownDisabledOpacity;



        _sealForgotHiding = false;
        _sealForgotHideSb?.Stop();
        _sealForgotHideSb = null;
        SealForgotScrim.Opacity = 0;
        SealForgotDialog.Opacity = 0;
        SealForgotDialogTransform.ScaleX = 0.92; SealForgotDialogTransform.ScaleY = 0.92; SealForgotDialogTransform.TranslateY = 20;
        SealForgotOverlay.Visibility = Visibility.Visible;
        DialogUi.RegisterDialogClamp((Grid)Content, _dialogMap, SealForgotOverlay, SealForgotDialog, _dialogWidthFit);
        var showSb = new Storyboard();
        var scrimIn = new DoubleAnimation { To = 1, Duration = TimeSpan.FromMilliseconds(250) };
        Storyboard.SetTarget(scrimIn, SealForgotScrim); Storyboard.SetTargetProperty(scrimIn, "Opacity");
        showSb.Children.Add(scrimIn);
        var dialogIn = new DoubleAnimation { To = 1, Duration = TimeSpan.FromMilliseconds(300) };
        Storyboard.SetTarget(dialogIn, SealForgotDialog); Storyboard.SetTargetProperty(dialogIn, "Opacity");
        showSb.Children.Add(dialogIn);
        Motion.AddDialogShowTransform(showSb, SealForgotDialogTransform);
        showSb.Begin();
        SealForgotCd.Completed -= OnSealForgotCdCompleted;
        SealForgotCd.Completed += OnSealForgotCdCompleted;
        SealForgotCd.Start(Motion.CooldownSeconds);
    }

    private void OnSealForgotCdCompleted()
    {
        SealForgotCd.Completed -= OnSealForgotCdCompleted;
        _sealForgotCdReady = true;
        UpdateSealForgotConfirmState();
    }

    private void SealForgotPhraseBox_TextChanged(object sender, TextChangedEventArgs e) => UpdateSealForgotConfirmState();


    private void UpdateSealForgotConfirmState()
    {
        var ok = _sealForgotCdReady
                 && Services.DestructiveConfirm.Matches(SealForgotPhraseBox.Text, App.GetString("Seal_Forgot_Phrase"));
        SealForgotConfirmButton.IsEnabled = ok;
        SealForgotConfirmButton.Opacity = ok ? 1 : Motion.CooldownDisabledOpacity;
    }

    private void HideSealForgotDialog()
    {





        if (_sealForgotHiding) return;
        _sealForgotHiding = true;
        var cdGen = SealForgotCd.Generation;
        SealForgotCd.Stop();
        var sb = new Storyboard();
        var scrimOut = new DoubleAnimation { To = 0, Duration = TimeSpan.FromMilliseconds(200) };
        Storyboard.SetTarget(scrimOut, SealForgotScrim); Storyboard.SetTargetProperty(scrimOut, "Opacity");
        sb.Children.Add(scrimOut);
        var dialogOut = new DoubleAnimation { To = 0, Duration = TimeSpan.FromMilliseconds(200) };
        Storyboard.SetTarget(dialogOut, SealForgotDialog); Storyboard.SetTargetProperty(dialogOut, "Opacity");
        sb.Children.Add(dialogOut);
        var sx = new DoubleAnimation { To = 0.92, Duration = TimeSpan.FromMilliseconds(250), EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
        Storyboard.SetTarget(sx, SealForgotDialogTransform); Storyboard.SetTargetProperty(sx, "ScaleX");
        sb.Children.Add(sx);
        var sy = new DoubleAnimation { To = 0.92, Duration = TimeSpan.FromMilliseconds(250), EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
        Storyboard.SetTarget(sy, SealForgotDialogTransform); Storyboard.SetTargetProperty(sy, "ScaleY");
        sb.Children.Add(sy);
        var ty = new DoubleAnimation { To = 20, Duration = TimeSpan.FromMilliseconds(250), EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
        Storyboard.SetTarget(ty, SealForgotDialogTransform); Storyboard.SetTargetProperty(ty, "TranslateY");
        sb.Children.Add(ty);
        sb.Completed += (_, _) =>
        {
            if (!ReferenceEquals(_sealForgotHideSb, sb)) return;
            _sealForgotHideSb = null;
            SealForgotOverlay.Visibility = Visibility.Collapsed;
            SealForgotPhraseBox.Text = "";
            _sealForgotCdReady = false;
            SealForgotCd.ResetAfterHide(cdGen);
            _sealForgotHiding = false;
        };
        _sealForgotHideSb = sb;
        sb.Begin();
    }

    private void SealForgotClose_Click(object sender, RoutedEventArgs e) => HideSealForgotDialog();
    private void SealForgotCancel_Click(object sender, RoutedEventArgs e) => HideSealForgotDialog();
    private void SealForgotScrim_Tapped(object sender, TappedRoutedEventArgs e)
    {
        if (ReferenceEquals(e.OriginalSource, SealForgotScrim)) HideSealForgotDialog();
    }

    private async void SealForgotConfirm_Click(object sender, RoutedEventArgs e)
    {
        if (_sealForgotConfirming) return;

        if (!Services.DestructiveConfirm.Matches(SealForgotPhraseBox.Text, App.GetString("Seal_Forgot_Phrase")))
            return;

        _sealForgotConfirming = true;
        SealForgotConfirmButton.IsEnabled = false;
        try
        {
            var status = await Novara.Services.SealService.Current.ClearWithoutSessionAsync();
            if (status != SealServiceStatus.Ok)
            {
                App.ShowToast(App.GetString("Common_Toast_Failed"), ToastTone.Error);
                Services.CrashLogger.LogNote("SealForgot", status.ToString());
                return;
            }

            HideSealForgotDialog();

            ExitSealShell();
            ResetSealLockedState();
            ShowSealGate();
            App.ShowToast(App.GetString("Seal_Forgot_Done"));
        }
        catch (Exception ex)
        {
            Services.CrashLogger.LogNote("SealForgot", ex.Message);
            App.ShowToast(App.GetString("Common_Toast_Failed"), ToastTone.Error);
        }
        finally { _sealForgotConfirming = false; }
    }

    private void ShowSealUnlockPage()
    {
        ApplySealUnlockTexts();
        HideSealGuide();
        SealUnlockContent.Visibility = Visibility.Visible;
        SealGateFooterPanel.Visibility = Visibility.Visible;
        SealGuideOverlay.Visibility = Visibility.Visible;
        SealGuideOverlay.UpdateLayout();

        var hint = new Novara.Services.SealSpaceStore(Novara.Services.SealSpacePaths.Default).ReadHint();
        SealUnlockHint.Text = hint ?? string.Empty;
        SealUnlockHint.Visibility = string.IsNullOrWhiteSpace(hint) ? Visibility.Collapsed : Visibility.Visible;

        SealUnlockError.Visibility = Visibility.Collapsed;
        _sealUnlockErrorActive = false;

        SealUnlockTitleTransform.X = 0;
        SealUnlockPanelTransform.X = 0;
        _sealUnlockExiting = false;

        Novara.Services.PasswordFeedback.ClearError(SealUnlockPasswordBox);
        SealUnlockPasswordBox.BorderThickness = new Thickness(1);
        SealUnlockPasswordBox.Password = string.Empty;
        SealUnlockError.Visibility = Visibility.Collapsed;
        RefreshSealLockState();
        PlaySealUnlockEntry();
        if (!(_sealLockout?.IsLocked ?? false)) SealUnlockPasswordBox.Focus(FocusState.Programmatic);
    }

    private void ApplySealUnlockTexts()
    {

        SealUnlockTitle.Text = App.GetString("Seal_Name");
        SealUnlockPasswordBox.PlaceholderText = App.GetString("Seal_Unlock_Placeholder");

        SealLockedTitle.Text = App.GetString("Lock_Locked_Title");
        SealLockedDesc.Text = App.GetString("Lock_Locked_Desc");
        SealLockWaitHint.Text = App.GetString("Lock_Locked_WaitHint");
        SealLockFooter.Text = App.GetString("Lock_Locked_Footer");
        SealForgotText.Text = App.GetString("Lock_Forgot_Hint");

        SealGateBackText.Text = App.GetString(
            SealService.Current.Mode == SealSessionMode.Direct ? "Seal_ReturnLock" : "Seal_ReturnHome");
    }




    private void PlaySealUnlockEntry()
    {
        _sealExitSb?.Stop();
        _sealEntrySb?.Stop();

        SealUnlockTitle.Opacity = 0;
        SealUnlockPanel.Opacity = 0;
        SealUnlockTitleTransform.Y = 0;
        SealUnlockHint.Opacity = 1;
        SealUnlockError.Opacity = 1;
        SealForgotText.Opacity = 0;
        SealGateBackText.Opacity = 0;
        _sealEntrySb = new Storyboard();

        void Fade(DependencyObject target, string property, double from, double to, int ms, int delay)
        {
            var a = new DoubleAnimation
            {
                From = from,
                To = to,
                Duration = TimeSpan.FromMilliseconds(ms),
                BeginTime = TimeSpan.FromMilliseconds(delay),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
            };
            Storyboard.SetTarget(a, target);
            Storyboard.SetTargetProperty(a, property);
            _sealEntrySb!.Children.Add(a);
        }

        Fade(SealUnlockTitle, "Opacity", 0, 1, 800, 100);
        Fade(SealUnlockTitleTransform, "Y", 20, 0, 800, 100);
        Fade(SealUnlockPanel, "Opacity", 0, 1, 600, 350);
        Fade(SealUnlockHint, "Opacity", 0, 1, 600, 450);
        Fade(SealUnlockError, "Opacity", 0, 1, 600, 550);

        Fade(SealForgotText, "Opacity", 0, 0.45, 600, 650);
        Fade(SealGateBackText, "Opacity", 0, 0.45, 600, 650);
        _sealEntrySb.Begin();
    }



    private void ShowSealLocked()
    {
        if (_sealLockedShown) return;
        _sealLockedShown = true;
        SealUnlockContent.Visibility = Visibility.Collapsed;
        SealGateFooterPanel.Visibility = Visibility.Collapsed;
        SealLockedView.Opacity = 0;
        SealLockedView.Visibility = Visibility.Visible;

        var sb = new Storyboard();
        var fade = new DoubleAnimation
        {
            From = 0,
            To = 1,
            Duration = TimeSpan.FromMilliseconds(300),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
        };
        Storyboard.SetTarget(fade, SealLockedView);
        Storyboard.SetTargetProperty(fade, "Opacity");
        sb.Children.Add(fade);
        sb.Begin();

        _sealCountdownBreathing?.Stop();
        _sealCountdownBreathing = new Storyboard { RepeatBehavior = RepeatBehavior.Forever, AutoReverse = true };
        var breath = new DoubleAnimation
        {
            From = 0.55,
            To = 1,
            Duration = TimeSpan.FromMilliseconds(3000),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut },
        };
        Storyboard.SetTarget(breath, SealLockCountdown);
        Storyboard.SetTargetProperty(breath, "Opacity");
        _sealCountdownBreathing.Children.Add(breath);
        _sealCountdownBreathing.Begin();
    }


    private void HideSealLocked()
    {
        if (!_sealLockedShown) return;
        _sealLockedShown = false;
        _sealCountdownBreathing?.Stop();
        SealLockedView.Visibility = Visibility.Collapsed;
        SealLockCountdown.Opacity = 1;
        SealUnlockContent.Visibility = Visibility.Visible;
        SealGateFooterPanel.Visibility = Visibility.Visible;
        PlaySealUnlockEntry();
        SealUnlockPasswordBox.Focus(FocusState.Programmatic);
    }





    private void ResetSealLockedState()
    {
        StopSealLockTimer();
        if (!_sealLockedShown) return;
        _sealLockedShown = false;
        _sealCountdownBreathing?.Stop();
        SealLockedView.Visibility = Visibility.Collapsed;
        SealLockCountdown.Opacity = 1;
        SealUnlockContent.Visibility = Visibility.Visible;
    }


    private void RefreshSealLockState()
    {
        var lockout = _sealLockout ??= new Novara.Services.SealLockout(Novara.Services.SealSpacePaths.Default.Lockout);
        var left = lockout.Remaining;
        bool locked = left > TimeSpan.Zero;

        SealUnlockPasswordBox.IsEnabled = !locked && !_sealUnlockBusy;
        SealUnlockButton.IsEnabled = !locked && !_sealUnlockBusy;

        if (locked)
        {

            SealLockCountdown.Text = left.ToString(@"hh\:mm\:ss");
            ShowSealLocked();
            StartSealLockTimer();
        }
        else
        {
            StopSealLockTimer();
            HideSealLocked();
        }
    }

    private void StartSealLockTimer()
    {
        if (_sealLockTimer is null)
        {
            _sealLockTimer = DispatcherQueue.CreateTimer();
            _sealLockTimer.Interval = TimeSpan.FromSeconds(1);
            _sealLockTimer.IsRepeating = true;
            _sealLockTimer.Tick += (_, _) => RefreshSealLockState();
        }
        if (!_sealLockTimer.IsRunning) _sealLockTimer.Start();
    }

    private void StopSealLockTimer() => _sealLockTimer?.Stop();

    private void SealUnlock_Click(object sender, RoutedEventArgs e) => _ = TryUnlockSealAsync();




    private void TryLeaveSealGate()
    {
        if (!_sealMode) return;
        if (_sealUnlockBusy || _sealUnlockExiting) return;
        ReturnHomeFromSeal();
    }

    private void SealUnlockPassword_PasswordChanged(object sender, RoutedEventArgs e)
    {



        if (_sealUnlockBusy) return;
        if (_sealUnlockErrorActive && string.IsNullOrEmpty(SealUnlockPasswordBox.Password)) return;
        _sealUnlockErrorActive = false;
        SealUnlockPasswordBox.ClearValue(PasswordBox.BorderBrushProperty);
        SealUnlockError.Visibility = Visibility.Collapsed;
    }

    private async void SealUnlockPassword_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == Windows.System.VirtualKey.Escape)
        {
            e.Handled = true;
            TryLeaveSealGate();
            return;
        }
        if (e.Key != Windows.System.VirtualKey.Enter) return;
        e.Handled = true;
        await TryUnlockSealAsync();
    }

    private async Task TryUnlockSealAsync()
    {
        if (_sealUnlockBusy) return;
        var lockout = _sealLockout ??= new Novara.Services.SealLockout(Novara.Services.SealSpacePaths.Default.Lockout);
        if (lockout.IsLocked) { RefreshSealLockState(); return; }

        SealFocusSink.Focus(FocusState.Programmatic);

        var pw = SealUnlockPasswordBox.Password ?? string.Empty;
        if (pw.Length == 0)
        {

            _sealUnlockErrorActive = false;
            SealUnlockError.Visibility = Visibility.Collapsed;



            _sealUnlockBusy = true;
            SealUnlockButton.IsEnabled = false;
            try
            {
                await ShakeSealUnlockAsync();
            }
            finally
            {
                _sealUnlockBusy = false;
                SealUnlockButton.IsEnabled = true;
                SealUnlockPasswordBox.Focus(FocusState.Programmatic);
            }
            return;
        }

        _sealUnlockBusy = true;
        SealUnlockButton.IsEnabled = false;
        SealUnlockPasswordBox.IsEnabled = false;
        try
        {
            var status = await Task.Run(() => Novara.Services.SealService.Current.UnlockAsync(pw));
            if (status == Novara.Services.SealServiceStatus.Ok)
            {
                lockout.RegisterSuccess();
                SealUnlockPasswordBox.Password = string.Empty;


                if (Novara.Services.SealService.Current.ConsumeUnlockWeakened())
                    App.ShowToast(App.GetString("Seal_Unlock_Weakened"));
                PlaySealUnlockExit();
                return;
            }

            if (status == Novara.Services.SealServiceStatus.WrongPassword)
            {


                lockout.RegisterFailure();
                var lockedNow = lockout.IsLocked;
                _sealUnlockErrorActive = false;
                SealUnlockError.Visibility = Visibility.Collapsed;
                SealUnlockPasswordBox.Password = string.Empty;
                if (!lockedNow && !_sealUnlockExiting) await ShakeSealUnlockAsync();
            }
            else if (status == Novara.Services.SealServiceStatus.NotUnlocked)
            {


            }
            else
            {
                _sealUnlockErrorActive = true;
                SealUnlockPasswordBox.Password = string.Empty;
                SealUnlockPasswordBox.ClearValue(PasswordBox.BorderBrushProperty);
                SealUnlockError.Text = status switch
                {
                    Novara.Services.SealServiceStatus.NotInitialized => App.GetString("Seal_Unlock_NotReady"),
                    Novara.Services.SealServiceStatus.VersionUnsupported => App.GetString("Seal_Pwd_Err_Unsupported"),
                    _ => App.GetString("Seal_Pwd_Err_Io"),
                };
                SealUnlockError.Visibility = Visibility.Visible;
            }
        }
        catch (Exception ex)
        {



            CrashLogger.LogNote("SealGateUnlock", ex.GetType().Name);
            _sealUnlockErrorActive = true;
            SealUnlockPasswordBox.Password = string.Empty;
            SealUnlockError.Text = App.GetString("Seal_Pwd_Err_Io");
            SealUnlockError.Visibility = Visibility.Visible;
        }
        finally
        {
            _sealUnlockBusy = false;
            RefreshSealLockState();
            if (!lockout.IsLocked) SealUnlockPasswordBox.Focus(FocusState.Programmatic);
        }
    }



    private void PlaySealUnlockExit()
    {
        if (_sealUnlockExiting) return;
        _sealUnlockExiting = true;

        var sb = new Storyboard();
        _sealExitSb = sb;

        var tx = new DoubleAnimation { To = -240, Duration = TimeSpan.FromMilliseconds(500), EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn } };
        Storyboard.SetTarget(tx, SealUnlockTitleTransform); Storyboard.SetTargetProperty(tx, "X"); sb.Children.Add(tx);
        var to = new DoubleAnimation { To = 0, Duration = TimeSpan.FromMilliseconds(500) };
        Storyboard.SetTarget(to, SealUnlockTitle); Storyboard.SetTargetProperty(to, "Opacity"); sb.Children.Add(to);

        var bx = new DoubleAnimation { To = 240, Duration = TimeSpan.FromMilliseconds(500), EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn } };
        Storyboard.SetTarget(bx, SealUnlockPanelTransform); Storyboard.SetTargetProperty(bx, "X"); sb.Children.Add(bx);
        var bo = new DoubleAnimation { To = 0, Duration = TimeSpan.FromMilliseconds(500) };
        Storyboard.SetTarget(bo, SealUnlockPanel); Storyboard.SetTargetProperty(bo, "Opacity"); sb.Children.Add(bo);


        var ho = new DoubleAnimation { To = 0, Duration = TimeSpan.FromMilliseconds(500) };
        Storyboard.SetTarget(ho, SealUnlockHint); Storyboard.SetTargetProperty(ho, "Opacity"); sb.Children.Add(ho);
        var eo = new DoubleAnimation { To = 0, Duration = TimeSpan.FromMilliseconds(500) };
        Storyboard.SetTarget(eo, SealUnlockError); Storyboard.SetTargetProperty(eo, "Opacity"); sb.Children.Add(eo);

        sb.Completed += (_, _) =>
        {
            _sealUnlockExiting = false;
            HideSealGuide();
            SealUnlockTitleTransform.X = 0;
            SealUnlockTitle.Opacity = 1;
            SealUnlockPanelTransform.X = 0;
            SealUnlockPanel.Opacity = 1;
            _sealPage?.RefreshItems();
            _sealPage?.StealFocus();
        };
        sb.Begin();
    }



    private async Task ShakeSealUnlockAsync()
    {
        if (_sealShakeRunning || _sealUnlockExiting) return;
        _sealShakeRunning = true;
        try
        {


            Novara.Services.PasswordFeedback.ShowError(SealUnlockPasswordBox);
            SealUnlockPasswordBox.BorderThickness = new Thickness(2);



            var shake = new Storyboard();
            var sx = new DoubleAnimationUsingKeyFrames();
            Storyboard.SetTarget(sx, SealUnlockPanelTransform);
            Storyboard.SetTargetProperty(sx, "X");
            foreach (var (ms, v) in new[] { (0, 0.0), (50, -8.0), (150, 7.0), (250, -5.0), (340, 3.0), (400, 0.0) })
                sx.KeyFrames.Add(new LinearDoubleKeyFrame { KeyTime = KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(ms)), Value = v });
            shake.Children.Add(sx);
            var shaken = new TaskCompletionSource();
            shake.Completed += (_, _) => shaken.TrySetResult();
            shake.Begin();
            await shaken.Task;

            for (int i = 0; i < 2; i++)
            {
                Novara.Services.PasswordFeedback.ClearError(SealUnlockPasswordBox);
                await Task.Delay(120);
                Novara.Services.PasswordFeedback.ShowError(SealUnlockPasswordBox);
                await Task.Delay(120);
            }

            Novara.Services.PasswordFeedback.ClearError(SealUnlockPasswordBox);
            SealUnlockPasswordBox.BorderThickness = new Thickness(1);
            SealUnlockPanelTransform.X = 0;
        }
        finally { _sealShakeRunning = false; }
    }

    private Button? SealTabButton(int index) => index switch
    {
        0 => SealNavSpaceButton,
        1 => SealNavCapsule,
        2 => SealNavSettings,
        _ => null,
    };



    private void UpdateSealNavSelection(int index, bool animateGlow)
    {
        var brand = App.GetBrush("AppPrimaryButtonBrush");
        var dim = App.GetBrush("AppTextTertiaryBrush");
        var icon = App.GetBrush("IconForegroundBrush");
        var clearBg = new SolidColorBrush(Windows.UI.Color.FromArgb(0, 0, 0, 0));
        var raisedBg = App.GetBrush("NavFaceRaisedBrush");
        var items = new (Button Btn, Microsoft.UI.Xaml.Shapes.Path Icon, TextBlock Text)[]
        {
            (SealNavSpaceButton, SealNavSpacePath, SealNavSpaceText),
            (SealNavCapsule, SealNavCapsulePath, SealNavCapsuleText),
            (SealNavSettings, SealNavSettingsPath, SealNavSettingsText),
        };
        for (int i = 0; i < items.Length; i++)
        {
            bool selected = i == index;
            items[i].Btn.Background = selected ? raisedBg : clearBg;
            items[i].Btn.Translation = selected ? new System.Numerics.Vector3(0f, 0f, 20f) : System.Numerics.Vector3.Zero;
            SetNavOpacity(items[i].Btn, selected ? 1.0 : 0.6, SealNavBar.IsLoaded);
            SetNavOpacity(items[i].Icon, selected ? 1.0 : 0.6, SealNavBar.IsLoaded);
            items[i].Btn.Foreground = selected ? brand : dim;
            items[i].Icon.Fill = selected ? brand : icon;
            items[i].Text.Foreground = selected ? brand : dim;
        }
        var btn = SealTabButton(index);
        if (btn == null) return;
        if (!TryPlaceSealGlow(btn, animateGlow)) HookSealGlowRetry();
    }



    private bool TryPlaceSealGlow(Button btn, bool animate)
    {
        if (SealNavBar.Visibility != Visibility.Visible || btn.Visibility != Visibility.Visible || btn.ActualWidth < 1) return false;
        double expected = SealNavBar.ActualWidth / 4.0 - 16;
        if (expected > 1 && Math.Abs(btn.ActualWidth - expected) > Math.Max(10, expected * 0.2)) return false;
        var pt = btn.TransformToVisual(SealNavBar).TransformPoint(new Windows.Foundation.Point(0, 0));
        SealNavIndicator.Width = btn.ActualWidth;
        SealNavIndicator.Opacity = 1;
        if (!animate || !_sealIndicatorPlaced)
        {
            SetSealGlowX(pt.X);
            _sealIndicatorPlaced = true;
            return true;
        }
        SlideSealGlow(pt.X);
        return true;
    }

    private void SetSealGlowX(double x)
    {
        _sealGlowX = (float)x;
        try
        {
            var visual = Microsoft.UI.Xaml.Hosting.ElementCompositionPreview.GetElementVisual(SealNavIndicator);
            visual.StopAnimation("Translation.X");
            var jump = visual.Compositor.CreateScalarKeyFrameAnimation();
            jump.Duration = TimeSpan.FromMilliseconds(1);
            jump.InsertKeyFrame(1.0f, (float)x);
            visual.StartAnimation("Translation.X", jump);
        }
        catch
        {
            try { Microsoft.UI.Xaml.Hosting.ElementCompositionPreview.GetElementVisual(SealNavIndicator).StopAnimation("Translation.X"); } catch { }
            SealNavIndicatorTranslate.X = x;
        }
    }

    private void SlideSealGlow(double x)
    {
        var target = (float)x;
        try
        {
            var visual = Microsoft.UI.Xaml.Hosting.ElementCompositionPreview.GetElementVisual(SealNavIndicator);
            float startX = _sealGlowX;
            var anim = visual.Compositor.CreateScalarKeyFrameAnimation();
            anim.Duration = TimeSpan.FromMilliseconds(Services.Motion.Normal);

            anim.InsertKeyFrame(0.4f, Lerp(startX, target, 0.55f));
            anim.InsertKeyFrame(0.75f, Lerp(startX, target, 0.87f));
            anim.InsertKeyFrame(1.0f, target);
            visual.StartAnimation("Translation.X", anim);
            _sealGlowX = target;
        }
        catch
        {
            try { Microsoft.UI.Xaml.Hosting.ElementCompositionPreview.GetElementVisual(SealNavIndicator).StopAnimation("Translation.X"); } catch { }
            SealNavIndicatorTranslate.X = x;
            _sealGlowX = target;
        }
    }


    private void RelocateSealGlow()
    {
        ClampSealTabWidths();
        var btn = SealTabButton(_sealTabIndex);
        if (btn != null && TryPlaceSealGlow(btn, animate: false)) return;
        HookSealGlowRetry();
    }



    private void HookSealGlowRetry()
    {
        if (_sealGlowRetryHooked) return;
        _sealGlowRetryHooked = true;
        EventHandler<object>? handler = null;
        void Unhook()
        {
            if (!_sealGlowRetryHooked) return;
            _sealGlowRetryHooked = false;
            if (handler != null) SealNavBar.LayoutUpdated -= handler;
        }
        void TryOnce()
        {
            var btn = SealTabButton(_sealTabIndex);
            if (btn != null && TryPlaceSealGlow(btn, animate: true)) { Unhook(); return; }
            if (SealNavBar.Visibility != Visibility.Visible) Unhook();
        }
        handler = (_, _) => TryOnce();
        SealNavBar.LayoutUpdated += handler;
        DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, TryOnce);
    }


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


        menu.Placement = Microsoft.UI.Xaml.Controls.Primitives.FlyoutPlacementMode.BottomEdgeAlignedRight;
        menu.ShowAt(NavDiaryFilterButton, new Windows.Foundation.Point(NavDiaryFilterButton.ActualWidth, NavDiaryFilterButton.ActualHeight + 4));
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

        menu.Placement = Microsoft.UI.Xaml.Controls.Primitives.FlyoutPlacementMode.BottomEdgeAlignedRight;
        menu.ShowAt(NavPlanFilterButton, new Windows.Foundation.Point(NavPlanFilterButton.ActualWidth, NavPlanFilterButton.ActualHeight + 4));
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


    private const string TitleEasterEggUrl = "https://novara.xin/easteregg.html";

    private void TitleBrandButton_Click(object sender, RoutedEventArgs e)
    {

        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(TitleEasterEggUrl) { UseShellExecute = true }); }
        catch (Exception ex)
        {
            App.ShowToast(App.GetString("Connect_DeployHelp_Fail"), ToastTone.Error);
            Services.CrashLogger.LogNote("MainWindow.TitleBrandButton", ex.Message);
        }
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

        if (_sealMode)
        {
            NavBar.Visibility = Visibility.Collapsed;
            return;
        }
        int count = 0;
        foreach (var b in new[] { NavMemo, NavFile, NavPlan, NavDiary })
            if (b.Visibility == Visibility.Visible) count++;
        NavBar.Visibility = count <= 1 ? Visibility.Collapsed : Visibility.Visible;





        if (NavBar.Visibility == Visibility.Visible)
        {

            NavBar.UpdateLayout();
            RelocateNavGlow();
        }
    }

    public void ReloadPages()
    {
        ExitSealShell();
        _memoPage = null!;
        _filePathPage = null!;
        _planPage = null!;
        _diaryPage = null!;
        _memoStale = _planStale = _fileStale = _diaryStale = false;
        _diaryEditorPage?.Shutdown();
        _diaryEditorPage = null!;
        _settingsPage = null!;
        _toolsPage = null!;
        _sealPage = null!;
        _previousContent = null;



        _searchPage = null;
        _trashPage = null;
        _preSearchContent = null;
        _preTrashContent = null;
        _trashChanged = false;
        if (RootFrame.Content is SettingsPage or ToolsPage or SealPage or DiaryEditorPage or SearchPage or TrashPage)
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



        if (!IsTabVisible("Diary"))
        {
            _diaryEditorPage?.PrepareForExit();
            var (tab, nav) = ResolveNavTarget("Diary");
            NavigateToPage(tab);
            UpdateNavSelection(nav);
            _diaryEditorPage?.ClearContent();
            return;
        }
        _diaryEditorPage?.PrepareForExit();
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



    private void NavigateToSeal()
    {
        _sealPage ??= new SealPage();

        if (ReferenceEquals(RootFrame.Content, _sealPage)) return;
        _previousContent = RootFrame.Content;

        var fadeOut = new Storyboard();
        var oa = new DoubleAnimation { To = 0, Duration = TimeSpan.FromMilliseconds(120), EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
        Storyboard.SetTarget(oa, RootFrame);
        Storyboard.SetTargetProperty(oa, "Opacity");
        fadeOut.Children.Add(oa);
        fadeOut.Completed += (s, e) =>
        {
            RootFrame.Content = _sealPage;
            EnterSealShell();
            _sealPage.StealFocus();
            var fadeIn = new Storyboard();
            var oi = new DoubleAnimation { To = 1, Duration = TimeSpan.FromMilliseconds(200), EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
            Storyboard.SetTarget(oi, RootFrame);
            Storyboard.SetTargetProperty(oi, "Opacity");
            fadeIn.Children.Add(oi);
            fadeIn.Begin();
        };
        fadeOut.Begin();
    }




    private void EnterSealShell(bool skipGate = false)
    {
        _sealReturning = false;
        _sealMode = true;
        NavBar.Visibility = Visibility.Collapsed;
        SealNavBar.Visibility = Visibility.Visible;


        SealNavBar.UpdateLayout();
        ClampSealTabWidths();
        SealNavBar.UpdateLayout();

        _sealIndicatorPlaced = false;
        _sealTabIndex = -1;
        SelectSealTab(0);




        if (skipGate || (SealService.Current.Mode == SealSessionMode.Direct && SealService.Current.IsUnlocked))
        {
            SealUnlockContent.Visibility = Visibility.Collapsed;
            HideSealGuide();
        }
        else
        {
            ShowSealGate();
        }


        if (MenuSideItems.Children.Count == 0) BuildMenuSideItems();

        if (_sealReturnLabel is not null)
            _sealReturnLabel.Text = App.GetString(
                SealService.Current.Mode == SealSessionMode.Direct ? "Seal_ReturnLock" : "Seal_ReturnHome");
        MenuSideItems.Visibility = Visibility.Collapsed;
        SealSideItems.Visibility = Visibility.Visible;
        TitleBrandText.Text = "Novara Space";
        WorkspaceSwitcherButton.Visibility = Visibility.Collapsed;
        BottomToolbarPanel.Visibility = Visibility.Visible;
        CustomTitleBar.Visibility = Visibility.Visible;
        CloseMenuSidePanel();
    }



    private void ExitSealShell()
    {
        if (!_sealMode) return;
        _sealMode = false;
        ResetSealLockedState();
        HideSealGuide();
        SealNavBar.Visibility = Visibility.Collapsed;
        MenuSideItems.Visibility = Visibility.Visible;
        SealSideItems.Visibility = Visibility.Collapsed;
        TitleBrandText.Text = "Novara";
        UpdateWorkspaceSwitcher();
        UpdateNavBarVisibility();
        ReplayDeferredExternalRequests();
    }




    public void ReturnToSealWizardAfterRollback()
    {
        ExitSealShell();
        ShowSealGate();
    }



    public void ReturnHomeFromSeal()
    {
        if (!_sealMode) return;



        if (_sealReturning) return;
        _sealReturning = true;

        if (SealService.Current.Mode == SealSessionMode.Direct)
        {
            _sealReturning = false;
            LockSealDirect();
            return;
        }
        var fadeOut = new Storyboard();
        var oa = new DoubleAnimation { To = 0, Duration = TimeSpan.FromMilliseconds(120), EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
        Storyboard.SetTarget(oa, RootFrame);
        Storyboard.SetTargetProperty(oa, "Opacity");
        fadeOut.Children.Add(oa);
        fadeOut.Completed += (s, e) => CompleteSealDeparture();
        fadeOut.Begin();



        _ = SealDepartureWatchdogAsync();
    }

    private async System.Threading.Tasks.Task SealDepartureWatchdogAsync()
    {
        try { await System.Threading.Tasks.Task.Delay(500); } catch { return; }
        if (!_sealReturning) return;


        if (!DispatcherQueue.TryEnqueue(() =>
        {
            if (_sealReturning) CompleteSealDeparture();
        }))
            _sealReturning = false;
    }


    private void CompleteSealDeparture()
    {
        if (!_sealReturning) return;



        SealService.Current.Lock();
        _sealPage?.ResetAfterSessionLock();


        WelcomeOverlay.Visibility = Visibility.Collapsed;
        ExitSealShell();
        RestorePreviousContent();
        RestoreChromeFor(RootFrame.Content);
        _previousContent = null;

        var fadeIn = new Storyboard();
        var oi = new DoubleAnimation { To = 1, Duration = TimeSpan.FromMilliseconds(200), EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
        Storyboard.SetTarget(oi, RootFrame);
        Storyboard.SetTargetProperty(oi, "Opacity");
        fadeIn.Children.Add(oi);
        fadeIn.Begin();
        _sealReturning = false;
    }



    private void LockSealDirect()
    {
        if (!_sealMode) return;
        if (SealService.Current.Mode != SealSessionMode.Direct) return;
        if (LockScreenFrame.Visibility == Visibility.Visible) return;

        SealService.Current.Lock();
        _sealPage?.ResetAfterSessionLock();


        WelcomeOverlay.Visibility = Visibility.Collapsed;
        ExitSealShell();
        RestorePreviousContent();
        RestoreChromeFor(RootFrame.Content);
        _previousContent = null;
        LockScreenFrame.Content = null;
        ShowLockScreen();
    }


    private void LockNowOrSealDirect()
    {
        if (_sealMode && SealService.Current.Mode == SealSessionMode.Direct && SealService.Current.IsUnlocked)
        {
            LockSealDirect();
            return;
        }

        if (LockScreenFrame.Visibility == Visibility.Visible) return;
        if (App.Store is { IsLoaded: true, IsEncrypted: true }) LockNow();
        else App.ShowToast(App.GetString("Tray_LockNeedLock"), ToastTone.Error);
    }



    private void RestorePreviousContent()
    {
        Button? fallback = null;
        if (_selectedNavButton != null && _selectedNavButton.Visibility != Visibility.Visible)
        {
            foreach (var b in new[] { NavMemo, NavFile, NavPlan, NavDiary })
            {
                if (b.Visibility == Visibility.Visible) { fallback = b; break; }
            }
        }
        if (fallback?.Tag is string tag)
        {
            NavigateToPage(tag);
            UpdateNavSelection(fallback);
            _previousContent = null;
        }
        else if (_previousContent == null)
        {
            var (tab, nav) = ResolveNavTarget(_selectedNavButton?.Tag as string ?? "Memo");
            NavigateToPage(tab);
            UpdateNavSelection(nav);
            _previousContent = null;
        }
        else
        {
            _previousContent = FreshIfStale(_previousContent);
            RootFrame.Content = _previousContent;
            _previousContent = null;
        }
    }


    private void NavigateToTools(bool openWorkspace = false)
    {
        _toolsPage ??= new ToolsPage();



        if (ReferenceEquals(RootFrame.Content, _toolsPage))
        {
            if (openWorkspace) _toolsPage.OpenWorkspaceManagement();
            return;
        }
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


        if (ReferenceEquals(RootFrame.Content, _settingsPage)) return;
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

            RestorePreviousContent();



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
        StoreCorruptConfirmButton.Background = App.GetBrush("AppDangerBrush");
        StoreCorruptConfirmButton.Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(0xFF, 0xFF, 0xFF, 0xFF));
        ShowCorruptDialogCore();
    }



    public void ShowIoErrorDialog()
    {
        _corruptDialogIsIoError = true;
        StoreCorruptTitleText.Text = App.GetString("Store_IoError_Title");
        StoreCorruptDetailText.Text = App.GetString("Store_Err_IoFail");
        StoreCorruptConfirmText.Text = App.GetString("Common_Button_Confirm");
        StoreCorruptConfirmButton.Background = App.GetBrush("AppSurfaceBrush");
        StoreCorruptConfirmButton.Foreground = App.GetBrush("AppTextPrimaryBrush");
        ShowCorruptDialogCore();
    }

    private void ShowCorruptDialogCore()
    {




        _corruptHideSb?.Stop();
        _corruptHideSb = null;
        _animCorruptHide = false;
        StoreCorruptDialogTransform.ScaleX = 0.94; StoreCorruptDialogTransform.ScaleY = 0.94; StoreCorruptDialogTransform.TranslateY = 24;
        StoreCorruptDialog.Opacity = 0;
        StoreCorruptOverlay.Visibility = Visibility.Visible; DialogUi.RegisterDialogClamp((Grid)Content, _dialogMap, StoreCorruptOverlay, StoreCorruptDialog, _dialogWidthFit);
        var sb = new Storyboard();
        var da = new DoubleAnimation { To = 1, Duration = TimeSpan.FromMilliseconds(300) };
        Storyboard.SetTarget(da, StoreCorruptDialog); Storyboard.SetTargetProperty(da, "Opacity");
        sb.Children.Add(da);
        Motion.AddDialogShowTransform(sb, StoreCorruptDialogTransform);
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
        Motion.AddDialogHideTransform(sb, StoreCorruptDialogTransform);
        _corruptHideSb = sb;
        sb.Completed += (_, _) =>
        {
            _animCorruptHide = false;
            if (!ReferenceEquals(_corruptHideSb, sb)) return;
            _corruptHideSb = null;
            StoreCorruptOverlay.Visibility = Visibility.Collapsed;
        };
        sb.Begin();
    }

    private async void StoreCorruptConfirm_Click(object sender, RoutedEventArgs e)
    {



        if (_rebuildInFlight) return;
        _rebuildInFlight = true;
        StoreCorruptConfirmButton.IsEnabled = false;
        try
        {
            if (_corruptDialogIsIoError) { HideCorruptStoreDialog(); return; }






            try { await System.Threading.Tasks.Task.Run(() => AutoBackupService.CreateSnapshot(isManual: true)); } catch { }



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
                Novara.Services.SyncService.MarkRestorePendingConfirmOrWarn("corrupt-store rebuild");
            }
            finally
            {
                HideCorruptStoreDialog();
                if (LockScreenFrame.Visibility == Visibility.Visible)
                {
                    LockScreenFrame.Visibility = Visibility.Collapsed;
                    LockScreenFrame.Content = null;


                    WelcomeOverlay.Visibility = Visibility.Visible;
                    WarmWelcomeShatter();
                    WelcomeEntryAnimation.Begin();
                }
                else
                {
                    ShowMainContent();
                }
            }
        }
        finally
        {
            _rebuildInFlight = false;
            StoreCorruptConfirmButton.IsEnabled = true;
        }
    }

    private void ReclampVisibleDialogs()
        => DialogUi.ReclampVisibleDialogs((Grid)Content, _dialogMap, widthFit: _dialogWidthFit);

    private void ShowSaveFailedDialog()
    {
        _animSaveFailedHide = false;
        _saveFailedHideSb = null;
        SaveFailedDialogTransform.ScaleX = 0.94; SaveFailedDialogTransform.ScaleY = 0.94; SaveFailedDialogTransform.TranslateY = 24;
        SaveFailedDialog.Opacity = 0;
        SaveFailedOverlay.Visibility = Visibility.Visible; DialogUi.RegisterDialogClamp((Grid)Content, _dialogMap, SaveFailedOverlay, SaveFailedDialog, _dialogWidthFit);
        var sb = new Storyboard();
        var da = new DoubleAnimation { To = 1, Duration = TimeSpan.FromMilliseconds(300) };
        Storyboard.SetTarget(da, SaveFailedDialog); Storyboard.SetTargetProperty(da, "Opacity");
        sb.Children.Add(da);
        Motion.AddDialogShowTransform(sb, SaveFailedDialogTransform);
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
        Motion.AddDialogHideTransform(sb, SaveFailedDialogTransform);
        _saveFailedHideSb = sb;
        sb.Completed += (_, _) =>
        {
            if (!ReferenceEquals(_saveFailedHideSb, sb)) return;
            _saveFailedHideSb = null;
            SaveFailedOverlay.Visibility = Visibility.Collapsed;
            _animSaveFailedHide = false;
        };
        sb.Begin();
    }

    private void SaveFailedConfirm_Click(object sender, RoutedEventArgs e) => HideSaveFailedDialog();











    private bool CanShowAutoUpdateReminder()
        => !_restarting
           && LockScreenFrame.Visibility != Visibility.Visible
           && WelcomeOverlay.Visibility != Visibility.Visible
           && !IsWelcomeCarouselUp
           && McpAuthorizeOverlay.Visibility != Visibility.Visible
           && ThemeRestartOverlay.Visibility != Visibility.Visible
           && QuickCaptureOverlay.Visibility != Visibility.Visible
           && StoreCorruptOverlay.Visibility != Visibility.Visible
           && SaveFailedOverlay.Visibility != Visibility.Visible
           && MigrateFormatOverlay.Visibility != Visibility.Visible
           && !IsSealSurfaceBlocking()
           && _veilCount == 0;



    public bool ShowAutoUpdateReminder(Novara.Services.UpdateService.LatestRelease rel)
    {
        if (AutoUpdateOverlay.Visibility == Visibility.Visible || _autoUpdateAnimating) return true;
        if (!CanShowAutoUpdateReminder()) return false;
        _autoUpdateRelease = rel;
        _autoUpdateHiding = false;
        _autoUpdateHideSb = null;

        AutoUpdateTitleText.Text = string.Format(App.GetString("Update_Auto_Title"), Novara.Services.UpdatePolicy.FormatDisplayVersion(rel.Version));
        AutoUpdateBodyText.Text = string.Format(App.GetString("Update_Auto_Body"),
            Novara.Services.UpdateService.CurrentVersion, Novara.Services.UpdatePolicy.FormatDisplayVersion(rel.Version));
        AutoUpdateUpdateText.Text = App.GetString("Update_Btn_Install");
        _autoUpdateAnimating = true;
        _autoUpdateDismissPending = false;
        AutoUpdateScrim.Opacity = 0;
        AutoUpdateDialog.Opacity = 0;
        AutoUpdateDialogTransform.ScaleX = 0.92; AutoUpdateDialogTransform.ScaleY = 0.92; AutoUpdateDialogTransform.TranslateY = 20;
        AutoUpdateOverlay.Visibility = Visibility.Visible; DialogUi.RegisterDialogClamp((Grid)Content, _dialogMap, AutoUpdateOverlay, AutoUpdateDialog, _dialogWidthFit);
        var sb = new Storyboard();
        var sa = new DoubleAnimation { To = 1, Duration = TimeSpan.FromMilliseconds(250) };
        Storyboard.SetTarget(sa, AutoUpdateScrim); Storyboard.SetTargetProperty(sa, "Opacity");
        sb.Children.Add(sa);
        var da = new DoubleAnimation { To = 1, Duration = TimeSpan.FromMilliseconds(300) };
        Storyboard.SetTarget(da, AutoUpdateDialog); Storyboard.SetTargetProperty(da, "Opacity");
        sb.Children.Add(da);
        var xa = new DoubleAnimation { To = 1, Duration = TimeSpan.FromMilliseconds(400), EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut } };
        Storyboard.SetTarget(xa, AutoUpdateDialogTransform); Storyboard.SetTargetProperty(xa, "ScaleX");
        sb.Children.Add(xa);
        var ya = new DoubleAnimation { To = 1, Duration = TimeSpan.FromMilliseconds(400), EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut } };
        Storyboard.SetTarget(ya, AutoUpdateDialogTransform); Storyboard.SetTargetProperty(ya, "ScaleY");
        sb.Children.Add(ya);
        var ty = new DoubleAnimation { To = 0, Duration = TimeSpan.FromMilliseconds(400), EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut } };
        Storyboard.SetTarget(ty, AutoUpdateDialogTransform); Storyboard.SetTargetProperty(ty, "TranslateY");
        sb.Children.Add(ty);
        sb.Completed += (_, _) =>
        {
            _autoUpdateAnimating = false;

            if (_autoUpdateDismissPending) { _autoUpdateDismissPending = false; HideAutoUpdateOverlay(); }
        };
        sb.Begin();


        DispatcherQueue.TryEnqueue(() => AutoUpdateCancelButton.Focus(FocusState.Programmatic));
        return true;
    }


    public void DismissAutoUpdateReminder()
    {
        _autoUpdateRelease = null;
        if (AutoUpdateOverlay.Visibility != Visibility.Visible) return;


        if (_autoUpdateAnimating) { _autoUpdateDismissPending = true; return; }
        if (_autoUpdateHiding) return;
        HideAutoUpdateOverlay();
    }

    private void HideAutoUpdateOverlay()
    {
        if (_autoUpdateHiding) return;
        _autoUpdateHiding = true;
        var sb = new Storyboard();
        var sa = new DoubleAnimation { To = 0, Duration = TimeSpan.FromMilliseconds(200) };
        Storyboard.SetTarget(sa, AutoUpdateScrim); Storyboard.SetTargetProperty(sa, "Opacity");
        sb.Children.Add(sa);
        var da = new DoubleAnimation { To = 0, Duration = TimeSpan.FromMilliseconds(200) };
        Storyboard.SetTarget(da, AutoUpdateDialog); Storyboard.SetTargetProperty(da, "Opacity");
        sb.Children.Add(da);
        var xa = new DoubleAnimation { To = 0.92, Duration = TimeSpan.FromMilliseconds(250), EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
        Storyboard.SetTarget(xa, AutoUpdateDialogTransform); Storyboard.SetTargetProperty(xa, "ScaleX");
        sb.Children.Add(xa);
        var ya = new DoubleAnimation { To = 0.92, Duration = TimeSpan.FromMilliseconds(250), EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
        Storyboard.SetTarget(ya, AutoUpdateDialogTransform); Storyboard.SetTargetProperty(ya, "ScaleY");
        sb.Children.Add(ya);
        var ty = new DoubleAnimation { To = 20, Duration = TimeSpan.FromMilliseconds(250), EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
        Storyboard.SetTarget(ty, AutoUpdateDialogTransform); Storyboard.SetTargetProperty(ty, "TranslateY");
        sb.Children.Add(ty);
        sb.Completed += (_, _) =>
        {
            if (!ReferenceEquals(_autoUpdateHideSb, sb)) return;
            _autoUpdateHideSb = null;
            AutoUpdateOverlay.Visibility = Visibility.Collapsed;
            _autoUpdateHiding = false;
            _autoUpdateDismissPending = false;
        };
        _autoUpdateHideSb = sb;
        sb.Begin();
    }

    private void AutoUpdateScrim_Tapped(object sender, TappedRoutedEventArgs e) => AutoUpdateCancel_Click(sender, e);


    private void AutoUpdateOverlay_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key != Windows.System.VirtualKey.Escape) return;
        e.Handled = true;
        AutoUpdateCancel_Click(this, new RoutedEventArgs());
    }



    private void AutoUpdateCancel_Click(object sender, RoutedEventArgs e)
    {
        var rel = _autoUpdateRelease;
        if (App.Store is { IsLoaded: true } store && rel is not null)
        {
            store.Database.AppSettings.UpdatePendingVersion = rel.Version;


            if (!store.SaveSync())
                Services.CrashLogger.LogNote("AutoUpdate", "pending version save failed");

            _settingsPage?.NotifyPendingVersionChanged();
        }
        Services.UpdateAutoCheckService.ClearPendingRelease();
        _autoUpdateRelease = null;
        HideAutoUpdateOverlay();
    }



    private void AutoUpdateUpdate_Click(object sender, RoutedEventArgs e)
    {
        var rel = _autoUpdateRelease;
        Services.UpdateAutoCheckService.ClearPendingRelease();
        _autoUpdateRelease = null;
        HideAutoUpdateOverlay();
        if (rel == null) return;
        Pages.SettingsPage.PendingAutoUpdateRelease = rel;
        if (RootFrame.Content is Pages.SettingsPage sp) sp.ConsumeQueuedUpdate();
        else NavigateToSettings();
    }

    private void ShowThemeRestartOverlay()
    {





        if (IsSealSurfaceBlocking() || IsWelcomeCarouselUp) return;
        if (AutoUpdateOverlay.Visibility == Visibility.Visible || _autoUpdateAnimating) return;
        if (QuickCaptureOverlay.Visibility == Visibility.Visible) return;



        _themeRestartHiding = false;
        _themeRestartHideSb = null;
        if (_themeRestartAnimating) return;
        _themeRestartAnimating = true;
        ThemeRestartScrim.Opacity = 0;


        ThemeRestartDialog.Opacity = 0;
        ThemeRestartDialogTransform.ScaleX = 0.94; ThemeRestartDialogTransform.ScaleY = 0.94; ThemeRestartDialogTransform.TranslateY = 24;
        ThemeRestartOverlay.Visibility = Visibility.Visible; DialogUi.RegisterDialogClamp((Grid)Content, _dialogMap, ThemeRestartOverlay, ThemeRestartDialog, _dialogWidthFit);
        var sb = new Storyboard();
        var sa = new DoubleAnimation { To = 1, Duration = TimeSpan.FromMilliseconds(250) };
        Storyboard.SetTarget(sa, ThemeRestartScrim); Storyboard.SetTargetProperty(sa, "Opacity");
        sb.Children.Add(sa);
        var da = new DoubleAnimation { To = 1, Duration = TimeSpan.FromMilliseconds(300) };
        Storyboard.SetTarget(da, ThemeRestartDialog); Storyboard.SetTargetProperty(da, "Opacity");
        sb.Children.Add(da);
        Motion.AddDialogShowTransform(sb, ThemeRestartDialogTransform);
        sb.Completed += (_, _) => _themeRestartAnimating = false;
        sb.Begin();
    }

    private void HideThemeRestartOverlay()
    {




        if (_themeRestartHiding) return;
        _themeRestartHiding = true;
        _themeRestartCancelled = true;
        var sb = new Storyboard();
        var sa = new DoubleAnimation { To = 0, Duration = TimeSpan.FromMilliseconds(200) };
        Storyboard.SetTarget(sa, ThemeRestartScrim); Storyboard.SetTargetProperty(sa, "Opacity");
        sb.Children.Add(sa);
        var da = new DoubleAnimation { To = 0, Duration = TimeSpan.FromMilliseconds(200) };
        Storyboard.SetTarget(da, ThemeRestartDialog); Storyboard.SetTargetProperty(da, "Opacity");
        sb.Children.Add(da);
        Motion.AddDialogHideTransform(sb, ThemeRestartDialogTransform);
        sb.Completed += (_, _) =>
        {
            if (!ReferenceEquals(_themeRestartHideSb, sb)) return;
            _themeRestartHideSb = null;
            ThemeRestartOverlay.Visibility = Visibility.Collapsed;
            _themeRestartHiding = false;
        };
        _themeRestartHideSb = sb;
        sb.Begin();
    }

    private async void ThemeRestartConfirm_Click(object sender, RoutedEventArgs e)
    {
        if (_restarting) return;
        _restarting = true;
        _themeRestartCancelled = false;






        if (await FlushPendingEditorForRestartAsync())
        {

            _restarting = false;
            return;
        }
        if (_themeRestartCancelled) { _restarting = false; return; }

        HideThemeRestartOverlay();




        var outcome = RestartNow();
        if (outcome == RestartOutcome.Ok) return;
        _restarting = false;
        if (outcome == RestartOutcome.EditorDirty)
            App.ShowToast(App.GetString("Common_Restart_EditorDirty"), ToastTone.Error);
        else if (outcome == RestartOutcome.SaveFailed)
            App.ShowToast(App.GetString("Common_Toast_Failed"), ToastTone.Error);
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




    private readonly List<(string ClientPath, Action<bool> Result, DateTimeOffset EnqueuedAt)> _mcpAuthorizeQueue = new();
    private const int McpAuthorizeQueueLimit = 8;



    private static readonly TimeSpan McpAuthorizeQueueMaxAge = TimeSpan.FromSeconds(15);

    private void DrainMcpAuthorizeQueue()
    {
        while (_mcpAuthorizeQueue.Count > 0)
        {
            var next = _mcpAuthorizeQueue[0];
            _mcpAuthorizeQueue.RemoveAt(0);
            if (DateTimeOffset.UtcNow - next.EnqueuedAt > McpAuthorizeQueueMaxAge)
            {
                try { next.Result(false); } catch { }
                continue;
            }
            ShowMcpAuthorizeDialog(next.ClientPath, next.Result);
            return;
        }
    }

    private void DenyQueuedMcpAuthorizations()
    {
        while (_mcpAuthorizeQueue.Count > 0)
        {
            var queued = _mcpAuthorizeQueue[0];
            _mcpAuthorizeQueue.RemoveAt(0);
            try { queued.Result(false); } catch { }
        }
    }

    private void QueueOrShowMcpAuthorize(string clientPath, Action<bool> onResult)
    {

        if (LockScreenFrame.Visibility == Visibility.Visible) { onResult(false); return; }
        bool busy = _mcpAuthorizeResult != null || _mcpAuthorizeAnimating || McpAuthorizeOverlay.Visibility == Visibility.Visible;
        if (!busy) { ShowMcpAuthorizeDialog(clientPath, onResult); return; }
        if (_mcpAuthorizeQueue.Count >= McpAuthorizeQueueLimit) { onResult(false); return; }
        _mcpAuthorizeQueue.Add((clientPath, onResult, DateTimeOffset.UtcNow));
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




        if (WelcomeOverlay.Visibility == Visibility.Visible || IsWelcomeCarouselUp)
        {
            onResult?.Invoke(false);
            DrainMcpAuthorizeQueue();
            return;
        }
        if (LockScreenFrame.Visibility == Visibility.Visible)
        {
            onResult?.Invoke(false);
            DrainMcpAuthorizeQueue();
            return;
        }
        _mcpAuthorizeResult = onResult;
        McpAuthorizePath.Text = clientPath;
        McpAuthorizePath.Visibility = string.IsNullOrEmpty(clientPath) ? Visibility.Collapsed : Visibility.Visible;
        McpAuthorizeDialogTransform.ScaleX = 0.94; McpAuthorizeDialogTransform.ScaleY = 0.94; McpAuthorizeDialogTransform.TranslateY = 24;
        McpAuthorizeDialog.Opacity = 0;
        McpAuthorizeOverlay.Visibility = Visibility.Visible; DialogUi.RegisterDialogClamp((Grid)Content, _dialogMap, McpAuthorizeOverlay, McpAuthorizeDialog, _dialogWidthFit);
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
            DrainMcpAuthorizeQueue();
        };
        sb.Begin();


        if (invokeResult) { try { result?.Invoke(allowed); } catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"MCP authorize callback late invoke dropped: {ex.Message}"); } }
    }

    private void McpAuthorizeAllow_Click(object sender, RoutedEventArgs e)
    {


        if (WelcomeOverlay.Visibility == Visibility.Visible || IsWelcomeCarouselUp) return;
        if (LockScreenFrame.Visibility == Visibility.Visible) return;


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
