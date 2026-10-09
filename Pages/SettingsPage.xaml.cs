using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Windows.UI;
using Windows.Security.Credentials.UI;
using Novara.Services;
using WinRT.Interop;
using Windows.ApplicationModel.DataTransfer;

namespace Novara.Pages;

public sealed partial class SettingsPage : Page
{

    private readonly System.Collections.Generic.Dictionary<Grid, int> _overlayAnimGens = new();
    private readonly System.Collections.Generic.Dictionary<Grid, Border> _overlayDialogs = new();





    private void ShowOverlay(Grid overlay, Border dialog, CompositeTransform transform)
        => DialogUi.ShowOverlay(SettingsRoot, _overlayAnimGens, _overlayDialogs, overlay, dialog, transform);











    private void RegisterDialogClamp(Grid overlay, Border dialog)
        => DialogUi.RegisterDialogClamp(SettingsRoot, _overlayDialogs, overlay, dialog);
    private void ReclampVisibleDialogs()
        => DialogUi.ReclampVisibleDialogs(SettingsRoot, _overlayDialogs);
    private void HideOverlay(Grid overlay, Border dialog, CompositeTransform transform, Action onCompleted)
        => DialogUi.HideOverlay(SettingsRoot, _overlayAnimGens, _overlayDialogs, overlay, dialog, transform, onCompleted);

    private string _currentTheme = App.CurrentTheme;
    private string _pendingTheme = string.Empty;
    private bool? _pendingAnimationToggle;
    private string _autoStart = "关闭";
    private string _contextMenu = "开启";
    private string _closeBehavior = "直接退出";
    private string _currentLanguage = "";
    private string? _pendingLanguage;
    private bool _restarting;
    private bool _languageRestartAnimating;
    private bool _isPrivacyLockEnabled = false;
    private readonly HashSet<string> _visibleTabs = new() { "备忘", "文件", "计划", "日记" };

    private bool _themeRestartAnimating;


    private bool _animAutoLockSync;
    private bool _animQuickCaptureInfo;
    private bool _autoLockOnSystemLock;
    private int _autoLockSeconds;
    private bool _welcomeOnLaunch;



    private static readonly string[] TabNames = { "备忘", "文件", "计划", "日记" };

    private readonly Dictionary<TextBox, System.Threading.CancellationTokenSource> _flashCtsMap = new();
    private readonly Dictionary<TextBox, Microsoft.UI.Xaml.Media.Brush> _flashOriginalBgs = new();

    public SettingsPage()
    {
        InitializeComponent();
        SettingsTitleText.Text = App.GetString("Setting_Title_Page");

        FooterBrandText.Text = "Novara";
        FooterVersionText.Text = "v" + (GetType().Assembly.GetName().Version?.ToString(3) ?? "0.0.0");
        UpdateCheckButtonText.Text = App.GetString("Setting_Update_Button");
        EdgeMenuTitleText.Text = App.GetString("Setting_EdgeMenu_Title");
        UpdateTitleText.Text = App.GetString("Setting_Update_Title");


        Novara.Services.DialogDepth.AttachContainer((Grid)Content, autoVeil: true);


        foreach (var dangerIcon in new Microsoft.UI.Xaml.Controls.PathIcon[]
        {
            PrivacyLockWarnDangerIcon, CloseLockDangerIcon,
        })
            dangerIcon.Data = App.CreateGeometry(IconData.Danger);
        KeyDown += Page_KeyDown;
        SettingsRoot.SizeChanged += (_, _) => ReclampVisibleDialogs();
        BackPathIcon.Data = (Geometry)Microsoft.UI.Xaml.Markup.XamlBindingHelper.ConvertValue(typeof(Geometry), IconData.Back);

        Unloaded += (_, _) =>
        {
            DialogUi.CancelAllFlashes(_flashCtsMap);

            SetPasswordOverlay.Visibility = Visibility.Collapsed;
            ChangePasswordOverlay.Visibility = Visibility.Collapsed;
            ClosePrivacyLockOverlay.Visibility = Visibility.Collapsed;
            ThemeRestartOverlay.Visibility = Visibility.Collapsed;
            LanguageRestartOverlay.Visibility = Visibility.Collapsed;
            PrivacyLockWarningOverlay.Visibility = Visibility.Collapsed;
            AutoLockSyncConfirmOverlay.Visibility = Visibility.Collapsed;
            WindowsHelloOverlay.Visibility = Visibility.Collapsed;



            QuickCaptureInfoOverlay.Visibility = Visibility.Collapsed;


            UpdateDialogOverlay.Visibility = Visibility.Collapsed;
            UpdateUpToDateOverlay.Visibility = Visibility.Collapsed;

            _animSetPwd = _animChangePwd = _animCloseLock = _animWarn = false;
            _themeRestartAnimating = _languageRestartAnimating = false;
            _animAutoLockSync = false;
            _animQuickCaptureInfo = false;
            _animWinHello = false;




            _animWarnShow = false;
        };
        Loaded += (_, _) =>
        {
            AnimationTitleText.Text = App.GetString("Setting_Animation_Title");

            if (App.IsAnimationsEnabled) CardsFloatIn.Begin();
            else App.ShowCardsStatically(Card5, AutoLockCard, QuickCaptureCard, Card2, ContextMenuCard, Card1, Card3, CardAnimation, Card8, Card6, EdgeMenuCard, WelcomeCard, UpdateCard, Card9);

            var settings = App.Store?.Database.AppSettings;
            if (settings != null)
            {
                _currentTheme = string.IsNullOrEmpty(settings.Theme) ? "跟随系统" : settings.Theme;
                UpdateAnimationButton();

                try { _autoStart = StartupService.IsEnabled() ? "开启" : "关闭"; }
                catch { _autoStart = settings.AutoStart ? "开启" : "关闭"; }
                _contextMenu = ContextMenuService.IsRegistered() ? "开启" : "关闭";
                _closeBehavior = string.IsNullOrEmpty(settings.CloseBehavior) ? "直接退出" : settings.CloseBehavior;
                if (settings.VisibleTabs is { Count: > 0 } tabs)
                {
                    _visibleTabs.Clear();
                    foreach (var t in tabs) _visibleTabs.Add(t);
                }
                _isPrivacyLockEnabled = settings.PrivacyLockEnabled;
                _currentLanguage = settings.AppLanguage ?? "";
                _welcomeOnLaunch = settings.WelcomeOnLaunch;
                _edgeMenuSide = string.IsNullOrWhiteSpace(settings.EdgeMenuSide) ? "左侧" : settings.EdgeMenuSide;
            }
            else
            {
                _autoStart = StartupService.IsEnabled() ? "开启" : "关闭";
                _contextMenu = ContextMenuService.IsRegistered() ? "开启" : "关闭";
            }
            UpdateThemeButton();
            UpdatePrivacyLockUI();
            UpdateAutoStartButton();
            UpdateContextMenuButton();
            UpdateCloseBehaviorButton();
            UpdateLanguageButton();
            UpdateWelcomeButton();
            UpdateEdgeMenuButton();
            UpdateTabsButton();
            RefreshUpdateDot();
            App.MainWindow?.ApplyVisibleTabs(_visibleTabs);
        };
    }


    private void FlashTextBox(TextBox tb) => DialogUi.FlashTextBox(_flashCtsMap, _flashOriginalBgs, tb);

    private void ApplyToggleState(Button btn, bool on)
    {
        btn.Style = on ? (Style)Application.Current.Resources["NovaraPrimaryButtonStyle"]
                       : (Style)Resources["SettingsDropdownButtonStyle"];
        btn.Background = on ? App.GetBrush("AppPrimaryButtonBrush")
                            : App.GetBrush("AppSurfaceBrush");
        btn.Foreground = on ? new SolidColorBrush(Color.FromArgb(255, 255, 255, 255))
                            : App.GetBrush("AppTextSecondaryBrush");
    }

    private static void PersistSetting(Action<Novara.Models.AppSettings> mutate)
    {
        var store = App.Store;
        if (store == null) return;
        mutate(store.Database.AppSettings);
        store.SaveAsync();
    }

    public void StealFocus() => FocusSink.Focus(FocusState.Programmatic);

    private void Page_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key != Windows.System.VirtualKey.Escape) return;
        if (PrivacyLockWarningOverlay.Visibility == Visibility.Visible) { HidePrivacyLockWarningDialog(); e.Handled = true; return; }
        if (LanguageRestartOverlay.Visibility == Visibility.Visible) { _pendingLanguage = null; HideLanguageRestartOverlay(); e.Handled = true; return; }
        if (ThemeRestartOverlay.Visibility == Visibility.Visible) { _pendingTheme = string.Empty; _pendingAnimationToggle = null; HideThemeRestartOverlay(); e.Handled = true; return; }
        if (ChangePasswordOverlay.Visibility == Visibility.Visible) { HideChangePasswordDialog(); e.Handled = true; return; }
        if (ClosePrivacyLockOverlay.Visibility == Visibility.Visible) { HideClosePrivacyLockDialog(); e.Handled = true; return; }
        if (SetPasswordOverlay.Visibility == Visibility.Visible) { if (_setPwdInFlight) { e.Handled = true; return; } HideSetPasswordDialog(); e.Handled = true; return; }

        if (WindowsHelloOverlay.Visibility == Visibility.Visible) { HideWindowsHelloDialog(); e.Handled = true; return; }

        if (QuickCaptureInfoOverlay.Visibility == Visibility.Visible) { HideQuickCaptureInfoDialog(); e.Handled = true; return; }
        if (AutoLockSyncConfirmOverlay.Visibility == Visibility.Visible) { HideAutoLockSyncConfirmDialog(); e.Handled = true; return; }
        if (UpdateUpToDateOverlay.Visibility == Visibility.Visible) { HideUpdateUpToDateDialog(); e.Handled = true; return; }
        if (UpdateDialogOverlay.Visibility == Visibility.Visible) { CancelUpdateInteraction(); e.Handled = true; return; }
    }

    private void BackButton_Click(object sender, RoutedEventArgs e)
    {
        App.MainWindow?.NavigateBackFromSettings();
    }

    private static string TabNameLabel(string name) => name switch
    {
        "备忘" => App.GetString("Nav_Tab_Memo"),
        "文件" => App.GetString("Nav_Tab_File"),
        "计划" => App.GetString("Nav_Tab_Plan"),
        "日记" => App.GetString("Nav_Tab_Diary"),
        _ => name
    };

    private void ThemeButton_Click(object sender, RoutedEventArgs e)
    {
        var menu = new MenuFlyout { MenuFlyoutPresenterStyle = (Style)Application.Current.Resources["GlassMenuFlyoutPresenterStyle"] };
        var accentBrush = App.GetBrush("AppTextPrimaryBrush");
        var normalBrush = App.GetBrush("AppTextSecondaryBrush");

        MenuFlyoutItem MakeItem(string text, bool active)
        {
            var item = new MenuFlyoutItem { Style = (Style)Application.Current.Resources["GlassMenuFlyoutItemStyle"], Text = text, Foreground = active ? accentBrush : normalBrush };
            if (active) item.Icon = new FontIcon { Glyph = "", FontSize = 12, Foreground = accentBrush };
            return item;
        }

        var darkItem = MakeItem(App.GetString("Setting_Theme_Dark"), _currentTheme == "深色模式");
        var lightItem = MakeItem(App.GetString("Setting_Theme_Light"), _currentTheme == "浅色模式");
        var systemItem = MakeItem(App.GetString("Setting_Theme_System"), _currentTheme == "跟随系统");

        var paperItems = new List<MenuFlyoutItem>();
        for (var i = 0; i < PaperTheme.Names.Length; i++)
        {
            var name = PaperTheme.Names[i];
            var item = MakeItem(App.GetString(PaperTheme.LabelKeys[i]), _currentTheme == name);
            item.Click += (_, _) => HandleThemeSelection(name);
            paperItems.Add(item);
        }

        darkItem.Click += (_, _) => HandleThemeSelection("深色模式");
        lightItem.Click += (_, _) => HandleThemeSelection("浅色模式");
        systemItem.Click += (_, _) => HandleThemeSelection("跟随系统");

        menu.Items.Add(darkItem); menu.Items.Add(lightItem); menu.Items.Add(systemItem);
        foreach (var pi in paperItems) menu.Items.Add(pi);
        menu.ShowAt(ThemeButton, new Windows.Foundation.Point(0, ThemeButton.ActualHeight + 4));
    }







    private void AnimationButton_Click(object sender, RoutedEventArgs e)
    {
        var menu = new MenuFlyout { MenuFlyoutPresenterStyle = (Style)Application.Current.Resources["GlassMenuFlyoutPresenterStyle"] };
        var accentBrush = App.GetBrush("AppTextPrimaryBrush");
        var normalBrush = App.GetBrush("AppTextSecondaryBrush");
        var current = App.IsAnimationsEnabled;

        MenuFlyoutItem MakeItem(string text, bool active)
        {
            var item = new MenuFlyoutItem { Style = (Style)Application.Current.Resources["GlassMenuFlyoutItemStyle"], Text = text, Foreground = active ? accentBrush : normalBrush };
            if (active) item.Icon = new FontIcon { Glyph = "\uE73E", FontSize = 12, Foreground = accentBrush };
            return item;
        }
        var onItem = MakeItem(App.GetString("Setting_Autostart_On"), current);
        var offItem = MakeItem(App.GetString("Setting_Autostart_Off"), !current);
        onItem.Click += (_, _) => RequestAnimationsEnabled(true);
        offItem.Click += (_, _) => RequestAnimationsEnabled(false);
        menu.Items.Add(onItem); menu.Items.Add(offItem);
        menu.ShowAt(AnimationButton, new Windows.Foundation.Point(0, AnimationButton.ActualHeight + 4));
    }



    private void RequestAnimationsEnabled(bool enabled)
    {
        if (App.IsAnimationsEnabled == enabled) return;
        if (_themeRestartAnimating) return;
        _pendingTheme = string.Empty;
        _pendingAnimationToggle = enabled;
        ShowThemeRestartOverlay();
    }

    private void UpdateAnimationButton()
    {
        AnimationButtonText.Text = App.GetString(App.IsAnimationsEnabled ? "Setting_Autostart_On" : "Setting_Autostart_Off");
        ApplyToggleState(AnimationButton, App.IsAnimationsEnabled);
    }

    private void HandleThemeSelection(string theme)
    {
        if (theme == _currentTheme) return;
        if (_themeRestartAnimating) return;
        _pendingAnimationToggle = null;
        _pendingTheme = theme;
        ShowThemeRestartOverlay();
    }






private void ShowThemeRestartOverlay()
    {
        if (_themeRestartAnimating) return;
        _themeRestartAnimating = true;


        bool isAnimation = _pendingAnimationToggle is bool;
        ThemeRestartTitleText.Text = App.GetString(isAnimation ? "Setting_AnimationRestart_Title" : "Setting_ThemeRestart_Title");
        ThemeRestartBodyText.Text = App.GetString(isAnimation ? "Setting_AnimationRestart_Tip" : "Theme_Restart_ConfirmTip");
        ThemeRestartScrim.Opacity = 0;


        ThemeRestartDialog.Opacity = 0;


        ThemeRestartDialogTransform.ScaleX = 0.92; ThemeRestartDialogTransform.ScaleY = 0.92; ThemeRestartDialogTransform.TranslateY = 20;
        RegisterDialogClamp(ThemeRestartOverlay, ThemeRestartDialog);
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



        await MainWindow.FlushPendingEditorForRestartAsync();


        if (ThemeRestartOverlay.Visibility != Visibility.Visible) { _restarting = false; return; }
        if (_pendingAnimationToggle is not bool && string.IsNullOrEmpty(_pendingTheme)) { _restarting = false; return; }


        if (_pendingAnimationToggle is bool animToggle)
        {
            _pendingAnimationToggle = null;
            HideThemeRestartOverlay();
            if (App.Store is { } animStore)
            {
                animStore.Database.AppSettings.AnimationsEnabled = animToggle;
                if (!animStore.SaveSync())
                {
                    _restarting = false;
                    animStore.Database.AppSettings.AnimationsEnabled = !animToggle;
                    UpdateAnimationButton();
                    return;
                }
            }
            UpdateAnimationButton();
            RestartAppNow();
            return;
        }

        var oldTheme = _currentTheme;
        _currentTheme = _pendingTheme;
        UpdateThemeButton();
        _pendingTheme = string.Empty;
        HideThemeRestartOverlay();

        if (App.Store is { } ts)
        {
            ts.Database.AppSettings.Theme = _currentTheme;
            if (!ts.SaveSync())
            {
                _restarting = false;
                _currentTheme = oldTheme;
                ts.Database.AppSettings.Theme = oldTheme;
                UpdateThemeButton();
                return;
            }
        }

        string stickyTheme = _currentTheme == "深色模式" ? "dark" : _currentTheme == "浅色模式" || PaperTheme.IsPaperName(_currentTheme) ? "light" : Services.StickySync.ResolveTheme();
        Services.StickySync.UpdateTheme(stickyTheme);
        PaperTheme.WriteHint(_currentTheme);

        RestartAppNow();
    }




    private void RestartAppNow()
    {
        try
        {
            var exe = Environment.ProcessPath;
            if (string.IsNullOrEmpty(exe)) { _restarting = false; return; }
            Novara.MainWindow.ReleaseSingleInstance();
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo { FileName = exe, UseShellExecute = true });
        }
        catch
        {

            Novara.MainWindow.AcquireSingleInstance();
            System.Diagnostics.Debug.WriteLine("Novara 重启闭环：新进程启动失败，保持当前会话");
            _restarting = false;
            return;
        }
        if (App.MainWindow is { } mw) { mw.SetRestarting(); mw.ExitApp(saveFirst: false); }
    }

    private void ThemeRestartCancel_Click(object sender, RoutedEventArgs e)
    {
        _pendingTheme = string.Empty;
        _pendingAnimationToggle = null;
        HideThemeRestartOverlay();
    }

    private void ThemeRestartClose_Click(object sender, RoutedEventArgs e)
    {
        _pendingTheme = string.Empty;
        _pendingAnimationToggle = null;
        HideThemeRestartOverlay();
    }

    private void ThemeRestartScrim_Tapped(object sender, TappedRoutedEventArgs e)
    {
        _pendingTheme = string.Empty;
        _pendingAnimationToggle = null;
        HideThemeRestartOverlay();
    }

    private void CustomizeTabsButton_Click(object sender, RoutedEventArgs e)
    {
        ShowTabsMenu();
    }


    private void UpdateTabsButton()
        => ApplyToggleState(CustomizeTabsButton, _visibleTabs.Count < TabNames.Length);

    private void ShowTabsMenu()
    {
        var menu = new MenuFlyout { MenuFlyoutPresenterStyle = (Style)Application.Current.Resources["GlassMenuFlyoutPresenterStyle"] };
        var accentBrush = App.GetBrush("AppTextPrimaryBrush");
        var normalBrush = App.GetBrush("AppTextSecondaryBrush");

        foreach (var name in TabNames)
        {
            bool active = _visibleTabs.Contains(name);
            var item = new MenuFlyoutItem { Style = (Style)Application.Current.Resources["GlassMenuFlyoutItemStyle"], Text = TabNameLabel(name), Foreground = active ? accentBrush : normalBrush };
            if (active) item.Icon = new FontIcon { Glyph = "\uE73E", FontSize = 12, Foreground = accentBrush };
            item.Click += (_, _) =>
            {
                if (_visibleTabs.Contains(name))
                {
                    if (_visibleTabs.Count > 1) _visibleTabs.Remove(name);
                }
                else _visibleTabs.Add(name);
                App.MainWindow?.ApplyVisibleTabs(_visibleTabs);
                PersistSetting(s => { s.VisibleTabs = _visibleTabs.ToList(); });
                UpdateTabsButton();
                DispatcherQueue.TryEnqueue(() => ShowTabsMenu());
            };
            menu.Items.Add(item);
        }

        menu.ShowAt(CustomizeTabsButton, new Windows.Foundation.Point(0, CustomizeTabsButton.ActualHeight + 4));
    }

    private void AutoStartButton_Click(object sender, RoutedEventArgs e)
    {
        var menu = new MenuFlyout { MenuFlyoutPresenterStyle = (Style)Application.Current.Resources["GlassMenuFlyoutPresenterStyle"] };
        var accentBrush = App.GetBrush("AppTextPrimaryBrush");
        var normalBrush = App.GetBrush("AppTextSecondaryBrush");

        MenuFlyoutItem MakeItem(string text, bool active)
        {
            var item = new MenuFlyoutItem { Style = (Style)Application.Current.Resources["GlassMenuFlyoutItemStyle"], Text = text, Foreground = active ? accentBrush : normalBrush };
            if (active) item.Icon = new FontIcon { Glyph = "\uE73E", FontSize = 12, Foreground = accentBrush };
            return item;
        }




        var onItem = MakeItem(App.GetString("Setting_Autostart_On"), _autoStart == "开启");
        var offItem = MakeItem(App.GetString("Setting_Autostart_Off"), _autoStart == "关闭");

        onItem.Click += (_, _) =>
        {
            if (StartupService.Enable())
            {
                _autoStart = "开启";
                UpdateAutoStartButton();
                PersistSetting(s => s.AutoStart = true);
                App.ShowToast(App.GetString("Common_Toast_Switched"));
            }
            else
            {
                App.ShowToast(App.GetString("Setting_Autostart_FailOn"), ToastTone.Error);
            }
        };
        offItem.Click += (_, _) =>
        {
            if (StartupService.Disable())
            {
                _autoStart = "关闭";
                UpdateAutoStartButton();
                PersistSetting(s => s.AutoStart = false);
                App.ShowToast(App.GetString("Common_Toast_Switched"));
            }
            else
            {
                App.ShowToast(App.GetString("Setting_Autostart_FailOff"), ToastTone.Error);
            }
        };

        menu.Items.Add(onItem); menu.Items.Add(offItem);
        menu.ShowAt(AutoStartButton, new Windows.Foundation.Point(0, AutoStartButton.ActualHeight + 4));
    }

    private void UpdateAutoStartButton()
    {
        bool on = _autoStart == "开启";
        ((TextBlock)AutoStartButton.Content).Text = on ? App.GetString("Setting_Autostart_On") : App.GetString("Setting_Autostart_Off");
        ApplyToggleState(AutoStartButton, on);
    }

    private void ContextMenuButton_Click(object sender, RoutedEventArgs e)
    {
        var menu = new MenuFlyout { MenuFlyoutPresenterStyle = (Style)Application.Current.Resources["GlassMenuFlyoutPresenterStyle"] };
        var accentBrush = App.GetBrush("AppTextPrimaryBrush");
        var normalBrush = App.GetBrush("AppTextSecondaryBrush");

        MenuFlyoutItem MakeItem(string text, bool active)
        {
            var item = new MenuFlyoutItem { Style = (Style)Application.Current.Resources["GlassMenuFlyoutItemStyle"], Text = text, Foreground = active ? accentBrush : normalBrush };
            if (active) item.Icon = new FontIcon { Glyph = "\uE73E", FontSize = 12, Foreground = accentBrush };
            return item;
        }


        var onItem = MakeItem(App.GetString("Setting_ContextMenu_On"), _contextMenu == "开启");
        var offItem = MakeItem(App.GetString("Setting_ContextMenu_Off"), _contextMenu == "关闭");

        onItem.Click += (_, _) =>
        {
            if (ContextMenuService.RegisterAll())
            {
                _contextMenu = "开启";
                UpdateContextMenuButton();
                PersistSetting(s => s.ContextMenu = true);
                App.ShowToast(App.GetString("Common_Toast_Switched"));
            }
            else
            {
                App.ShowToast(App.GetString("Setting_ContextMenu_FailOn"), ToastTone.Error);
            }
        };
        offItem.Click += (_, _) =>
        {
            if (ContextMenuService.UnregisterAll())
            {
                _contextMenu = "关闭";
                UpdateContextMenuButton();
                PersistSetting(s => s.ContextMenu = false);
                App.ShowToast(App.GetString("Common_Toast_Switched"));
            }
            else
            {
                App.ShowToast(App.GetString("Setting_ContextMenu_FailOff"), ToastTone.Error);
            }
        };

        menu.Items.Add(onItem); menu.Items.Add(offItem);
        menu.ShowAt(ContextMenuButton, new Windows.Foundation.Point(0, ContextMenuButton.ActualHeight + 4));
    }

    private void UpdateContextMenuButton()
    {
        bool on = _contextMenu == "开启";
        ((TextBlock)ContextMenuButton.Content).Text = on ? App.GetString("Setting_ContextMenu_On") : App.GetString("Setting_ContextMenu_Off");
        ApplyToggleState(ContextMenuButton, on);
    }






private void ShowSetPasswordDialog()
    {
        _animSetPwd = false;

        SetPasswordBox.Text = "";
        SetPasswordConfirmBox.Text = "";
        SetPasswordLockoutCheckBox.IsChecked = false;
        SetPasswordDialogTransform.ScaleX = 0.94; SetPasswordDialogTransform.ScaleY = 0.94; SetPasswordDialogTransform.TranslateY = 24;
        SetPasswordDialog.Opacity = 0;
        SetPasswordScrim.Opacity = 0;
        RegisterDialogClamp(SetPasswordOverlay, SetPasswordDialog);
        SetPasswordOverlay.Visibility = Visibility.Visible;
        var sb = new Storyboard();
        var si = new DoubleAnimation { To = 1, Duration = TimeSpan.FromMilliseconds(250) };
        Storyboard.SetTarget(si, SetPasswordScrim); Storyboard.SetTargetProperty(si, "Opacity"); sb.Children.Add(si);
        var di = new DoubleAnimation { To = 1, Duration = TimeSpan.FromMilliseconds(300) };
        Storyboard.SetTarget(di, SetPasswordDialog); Storyboard.SetTargetProperty(di, "Opacity"); sb.Children.Add(di);
        Motion.AddDialogShowTransform(sb, SetPasswordDialogTransform);
        sb.Begin();
    }

    private void PrivacyLockButton_Click(object sender, RoutedEventArgs e)
    {
        ShowSetPasswordDialog();
    }

    private void HideSetPasswordDialog()
    {
        if (_animSetPwd) return;
        _animSetPwd = true;
        var sb = new Storyboard();
        var so = new DoubleAnimation { To = 0, Duration = TimeSpan.FromMilliseconds(200) };
        Storyboard.SetTarget(so, SetPasswordScrim); Storyboard.SetTargetProperty(so, "Opacity"); sb.Children.Add(so);
        var d = new DoubleAnimation { To = 0, Duration = TimeSpan.FromMilliseconds(200) };
        Storyboard.SetTarget(d, SetPasswordDialog); Storyboard.SetTargetProperty(d, "Opacity"); sb.Children.Add(d);
        Motion.AddDialogHideTransform(sb, SetPasswordDialogTransform);
        sb.Completed += (_, _) => { SetPasswordOverlay.Visibility = Visibility.Collapsed; _animSetPwd = false; };
        sb.Begin();
    }

    private void SetPasswordClose_Click(object sender, RoutedEventArgs e)
    {
        if (_setPwdInFlight) return;
        HideSetPasswordDialog();
    }

    private void SetPasswordScrim_Tapped(object sender, TappedRoutedEventArgs e)
    {
        if (_setPwdInFlight) return;
        if (ReferenceEquals(e.OriginalSource, SetPasswordScrim)) HideSetPasswordDialog();
    }

    private void ChangePasswordButton_Click(object sender, RoutedEventArgs e)
    {
        _animChangePwd = false;
        ChangePasswordOldBox.Text = "";
        ChangePasswordNewBox.Text = "";
        ChangePasswordConfirmBox.Text = "";
        ChangePasswordDialogTransform.ScaleX = 0.94; ChangePasswordDialogTransform.ScaleY = 0.94; ChangePasswordDialogTransform.TranslateY = 24;
        ChangePasswordDialog.Opacity = 0;
        ChangePasswordScrim.Opacity = 0;
        RegisterDialogClamp(ChangePasswordOverlay, ChangePasswordDialog);
        ChangePasswordOverlay.Visibility = Visibility.Visible;
        var sb = new Storyboard();
        var si = new DoubleAnimation { To = 1, Duration = TimeSpan.FromMilliseconds(250) };
        Storyboard.SetTarget(si, ChangePasswordScrim); Storyboard.SetTargetProperty(si, "Opacity"); sb.Children.Add(si);
        var di = new DoubleAnimation { To = 1, Duration = TimeSpan.FromMilliseconds(300) };
        Storyboard.SetTarget(di, ChangePasswordDialog); Storyboard.SetTargetProperty(di, "Opacity"); sb.Children.Add(di);
        Motion.AddDialogShowTransform(sb, ChangePasswordDialogTransform);
        sb.Begin();
    }

    private void HideChangePasswordDialog()
    {
        if (_animChangePwd) return;
        _animChangePwd = true;
        var sb = new Storyboard();
        var so = new DoubleAnimation { To = 0, Duration = TimeSpan.FromMilliseconds(200) };
        Storyboard.SetTarget(so, ChangePasswordScrim); Storyboard.SetTargetProperty(so, "Opacity"); sb.Children.Add(so);
        var d = new DoubleAnimation { To = 0, Duration = TimeSpan.FromMilliseconds(200) };
        Storyboard.SetTarget(d, ChangePasswordDialog); Storyboard.SetTargetProperty(d, "Opacity"); sb.Children.Add(d);
        Motion.AddDialogHideTransform(sb, ChangePasswordDialogTransform);
        sb.Completed += (_, _) => { ChangePasswordOverlay.Visibility = Visibility.Collapsed; _animChangePwd = false; };
        sb.Begin();
    }

    private void ChangePasswordClose_Click(object sender, RoutedEventArgs e) => HideChangePasswordDialog();

    private void ChangePasswordScrim_Tapped(object sender, TappedRoutedEventArgs e)
    {
        if (ReferenceEquals(e.OriginalSource, ChangePasswordScrim)) HideChangePasswordDialog();
    }

    private void ChangePasswordConfirm_Click(object sender, RoutedEventArgs e)
    {
        var oldPw = ChangePasswordOldBox.Text.Trim();
        var newPw = ChangePasswordNewBox.Text.Trim();
        var confirmPw = ChangePasswordConfirmBox.Text.Trim();

        if (oldPw.Length < 6)
        {
            FlashTextBox(ChangePasswordOldBox);
            return;
        }
        if (newPw.Length < 6)
        {
            FlashTextBox(ChangePasswordNewBox);
            return;
        }
        if (confirmPw.Length < 6)
        {
            FlashTextBox(ChangePasswordConfirmBox);
            return;
        }
        if (newPw != confirmPw)
        {
            FlashTextBox(ChangePasswordConfirmBox);
            return;
        }







        if (!PasswordService.Verify(oldPw)) { FlashTextBox(ChangePasswordOldBox); return; }
        if (!PasswordService.StageChange(newPw)) { FlashTextBox(ChangePasswordConfirmBox); return; }
        if (App.Store?.Reencrypt(newPw) == false) { PasswordService.DiscardStaged(); FlashTextBox(ChangePasswordConfirmBox); return; }
        if (!PasswordService.PromoteStaged())
        {
            if (App.Store?.Reencrypt(oldPw) != true)
            {




                System.Diagnostics.Debug.WriteLine("改密回滚失败：数据库与密码文件不一致（暂存记录保留，启动时可恢复）");
                FlashTextBox(ChangePasswordOldBox);
                FlashTextBox(ChangePasswordConfirmBox);
                return;
            }
            PasswordService.DiscardStaged();
            FlashTextBox(ChangePasswordConfirmBox);
            return;
        }
        if (WindowsHelloService.IsEnabled())
        {




            _ = System.Threading.Tasks.Task.Run(() => WindowsHelloService.Update(newPw))
                .ContinueWith(t => { if (t.IsFaulted || !t.Result) App.ShowToast(App.GetString("Hello_Update_Fail"), ToastTone.Error); });
        }




        _ = SyncService.RewrapForKeyPasswordChangeAsync(oldPw, newPw)
            .ContinueWith(t =>
            {



                if (t.IsFaulted) { App.ShowToast(App.GetString("Sync_KeyWrap_RewrapFailed"), ToastTone.Error); return; }
                switch (t.Result)
                {
                    case SyncService.RewrapOutcome.SessionTornDown:
                        App.ShowToast(App.GetString("Sync_Status_KeyWrapStale")); break;
                    case SyncService.RewrapOutcome.StateWriteFailed:
                    case SyncService.RewrapOutcome.RoundTripFailed:
                        App.ShowToast(App.GetString("Sync_KeyWrap_RewrapFailed"), ToastTone.Error); break;

                }
            });

        HideChangePasswordDialog();
    }

    private void DisablePrivacyLockButton_Click(object sender, RoutedEventArgs e)
    {
        _animCloseLock = false;
        ClosePrivacyLockPasswordBox.Text = "";
        ClosePrivacyLockDialogTransform.ScaleX = 0.94; ClosePrivacyLockDialogTransform.ScaleY = 0.94; ClosePrivacyLockDialogTransform.TranslateY = 24;
        ClosePrivacyLockDialog.Opacity = 0;
        ClosePrivacyLockScrim.Opacity = 0;
        RegisterDialogClamp(ClosePrivacyLockOverlay, ClosePrivacyLockDialog);
        ClosePrivacyLockOverlay.Visibility = Visibility.Visible;
        var sb = new Storyboard();
        var si = new DoubleAnimation { To = 1, Duration = TimeSpan.FromMilliseconds(250) };
        Storyboard.SetTarget(si, ClosePrivacyLockScrim); Storyboard.SetTargetProperty(si, "Opacity"); sb.Children.Add(si);
        var di = new DoubleAnimation { To = 1, Duration = TimeSpan.FromMilliseconds(300) };
        Storyboard.SetTarget(di, ClosePrivacyLockDialog); Storyboard.SetTargetProperty(di, "Opacity"); sb.Children.Add(di);
        Motion.AddDialogShowTransform(sb, ClosePrivacyLockDialogTransform);
        sb.Begin();
    }

    private void HideClosePrivacyLockDialog()
    {
        if (_animCloseLock) return;
        _animCloseLock = true;
        var sb = new Storyboard();
        var so = new DoubleAnimation { To = 0, Duration = TimeSpan.FromMilliseconds(200) };
        Storyboard.SetTarget(so, ClosePrivacyLockScrim); Storyboard.SetTargetProperty(so, "Opacity"); sb.Children.Add(so);
        var d = new DoubleAnimation { To = 0, Duration = TimeSpan.FromMilliseconds(200) };
        Storyboard.SetTarget(d, ClosePrivacyLockDialog); Storyboard.SetTargetProperty(d, "Opacity"); sb.Children.Add(d);
        Motion.AddDialogHideTransform(sb, ClosePrivacyLockDialogTransform);
        sb.Completed += (_, _) => { ClosePrivacyLockOverlay.Visibility = Visibility.Collapsed; _animCloseLock = false; };
        sb.Begin();
    }

    private void ClosePrivacyLockClose_Click(object sender, RoutedEventArgs e) => HideClosePrivacyLockDialog();

    private void ClosePrivacyLockScrim_Tapped(object sender, TappedRoutedEventArgs e)
    {
        if (ReferenceEquals(e.OriginalSource, ClosePrivacyLockScrim)) HideClosePrivacyLockDialog();
    }

    private void ClosePrivacyLockConfirm_Click(object sender, RoutedEventArgs e)
    {
        var oldPw = ClosePrivacyLockPasswordBox.Text.Trim();

        if (oldPw.Length < 6)
        {
            FlashTextBox(ClosePrivacyLockPasswordBox);
            return;
        }

        if (!PasswordService.Verify(oldPw)) { FlashTextBox(ClosePrivacyLockPasswordBox); return; }
        HideClosePrivacyLockDialog();
        ShowPrivacyLockWarningDialog();
    }









private void ShowPrivacyLockWarningDialog()
    {
        if (_animWarnShow) return;
        _animWarnShow = true;
        _animWarn = false;


        PrivacyLockWarningErrorText.Visibility = Visibility.Collapsed;



        var syncState = Services.SyncService.State;
        var syncPaired = !string.IsNullOrEmpty(syncState.SpaceId) && !string.IsNullOrEmpty(syncState.DeviceId);
        PrivacyLockWarningSyncLossText.Text = App.GetString("Sync_LockOff_KeyWrapLost");
        PrivacyLockWarningSyncLossText.Visibility = syncPaired ? Visibility.Visible : Visibility.Collapsed;
        PrivacyLockWarningDialogTransform.ScaleX = 0.94; PrivacyLockWarningDialogTransform.ScaleY = 0.94; PrivacyLockWarningDialogTransform.TranslateY = 24;
        PrivacyLockWarningDialog.Opacity = 0;
        PrivacyLockWarningScrim.Opacity = 0;
        RegisterDialogClamp(PrivacyLockWarningOverlay, PrivacyLockWarningDialog);
        PrivacyLockWarningOverlay.Visibility = Visibility.Visible;
        var sb = new Storyboard();
        var si = new DoubleAnimation { To = 1, Duration = TimeSpan.FromMilliseconds(250) };
        Storyboard.SetTarget(si, PrivacyLockWarningScrim); Storyboard.SetTargetProperty(si, "Opacity"); sb.Children.Add(si);
        var di = new DoubleAnimation { To = 1, Duration = TimeSpan.FromMilliseconds(300) };
        Storyboard.SetTarget(di, PrivacyLockWarningDialog); Storyboard.SetTargetProperty(di, "Opacity"); sb.Children.Add(di);
        Motion.AddDialogShowTransform(sb, PrivacyLockWarningDialogTransform);
        sb.Completed += (_, _) => _animWarnShow = false;
        sb.Begin();
    }

    private void HidePrivacyLockWarningDialog()
    {
        if (_animWarn) return;
        _animWarn = true;
        var sb = new Storyboard();
        var so = new DoubleAnimation { To = 0, Duration = TimeSpan.FromMilliseconds(200) };
        Storyboard.SetTarget(so, PrivacyLockWarningScrim); Storyboard.SetTargetProperty(so, "Opacity"); sb.Children.Add(so);
        var d = new DoubleAnimation { To = 0, Duration = TimeSpan.FromMilliseconds(200) };
        Storyboard.SetTarget(d, PrivacyLockWarningDialog); Storyboard.SetTargetProperty(d, "Opacity"); sb.Children.Add(d);
        Motion.AddDialogHideTransform(sb, PrivacyLockWarningDialogTransform);
        sb.Completed += (_, _) => { PrivacyLockWarningOverlay.Visibility = Visibility.Collapsed; _animWarn = false; };
        sb.Begin();
    }

    private void PrivacyLockWarningClose_Click(object sender, RoutedEventArgs e) => HidePrivacyLockWarningDialog();
    private void PrivacyLockWarningKeep_Click(object sender, RoutedEventArgs e) => HidePrivacyLockWarningDialog();

    private void PrivacyLockWarningScrim_Tapped(object sender, TappedRoutedEventArgs e)
    {
        if (ReferenceEquals(e.OriginalSource, PrivacyLockWarningScrim)) HidePrivacyLockWarningDialog();
    }

    private void PrivacyLockWarningConfirm_Click(object sender, RoutedEventArgs e)
    {

        if (App.Store?.DisableEncryption() != true)
        {


            PrivacyLockWarningErrorText.Visibility = Visibility.Visible;
            return;
        }
        PasswordService.Delete();
        PasswordService.DeleteLockout();
        WindowsHelloService.Disable();
        PersistSetting(s => s.PrivacyLockEnabled = false);
        _isPrivacyLockEnabled = false;
        UpdatePrivacyLockUI();




        App.ApplyStoredSettings();
        HidePrivacyLockWarningDialog();
    }

    public void UpdatePrivacyLockUI()
    {
        PrivacyLockButton.Visibility = _isPrivacyLockEnabled ? Visibility.Collapsed : Visibility.Visible;
        PrivacyLockEnabledPanel.Visibility = _isPrivacyLockEnabled ? Visibility.Visible : Visibility.Collapsed;
        UpdateAutoLockCard();


        MigrateFormatEntryButton.Visibility = App.Store is { IsEncrypted: true } && (App.Store.NeedsFormatMigration || App.Store.NeedsKdfMigration)
            ? Visibility.Visible : Visibility.Collapsed;

        _winHelloEnabled = WindowsHelloService.IsEnabled();
        ApplyToggleState(WindowsHelloButton, _winHelloEnabled);
    }

    private void MigrateFormatEntryButton_Click(object sender, RoutedEventArgs e)
        => App.MainWindow?.ShowMigrateFormatDialog();

    private void CloseBehaviorButton_Click(object sender, RoutedEventArgs e)
    {
        var menu = new MenuFlyout { MenuFlyoutPresenterStyle = (Style)Application.Current.Resources["GlassMenuFlyoutPresenterStyle"] };
        var accentBrush = App.GetBrush("AppTextPrimaryBrush");
        var normalBrush = App.GetBrush("AppTextSecondaryBrush");

        MenuFlyoutItem MakeItem(string text, bool active)
        {
            var item = new MenuFlyoutItem { Style = (Style)Application.Current.Resources["GlassMenuFlyoutItemStyle"], Text = text, Foreground = active ? accentBrush : normalBrush };
            if (active) item.Icon = new FontIcon { Glyph = "\uE73E", FontSize = 12, Foreground = accentBrush };
            return item;
        }


        var trayItem = MakeItem(App.GetString("Setting_ExitBehavior_Tray"), _closeBehavior == "托盘驻留");
        var exitItem = MakeItem(App.GetString("Setting_ExitBehavior_Exit"), _closeBehavior == "直接退出");

        trayItem.Click += (_, _) =>
        {
            _closeBehavior = "托盘驻留";
            UpdateCloseBehaviorButton();
            if (App.MainWindow is { } mw) mw.CloseBehavior = _closeBehavior;
            PersistSetting(s => s.CloseBehavior = _closeBehavior);
            App.ShowToast(App.GetString("Common_Toast_Switched"));
        };
        exitItem.Click += (_, _) =>
        {
            _closeBehavior = "直接退出";
            UpdateCloseBehaviorButton();
            if (App.MainWindow is { } mw) mw.CloseBehavior = _closeBehavior;
            PersistSetting(s => s.CloseBehavior = _closeBehavior);
            App.ShowToast(App.GetString("Common_Toast_Switched"));
        };

        menu.Items.Add(trayItem); menu.Items.Add(exitItem);
        menu.ShowAt(CloseBehaviorButton, new Windows.Foundation.Point(0, CloseBehaviorButton.ActualHeight + 4));
    }

    private void LanguageButton_Click(object sender, RoutedEventArgs e)
    {
        var menu = new MenuFlyout { MenuFlyoutPresenterStyle = (Style)Application.Current.Resources["GlassMenuFlyoutPresenterStyle"] };
        var accentBrush = App.GetBrush("AppTextPrimaryBrush");
        var normalBrush = App.GetBrush("AppTextSecondaryBrush");

        MenuFlyoutItem MakeItem(string text, bool active)
        {
            var item = new MenuFlyoutItem { Style = (Style)Application.Current.Resources["GlassMenuFlyoutItemStyle"], Text = text, Foreground = active ? accentBrush : normalBrush };
            if (active) item.Icon = new FontIcon { Glyph = "\uE73E", FontSize = 12, Foreground = accentBrush };
            return item;
        }

        var zhItem = MakeItem(App.GetString("Setting_Language_Auto"), string.IsNullOrEmpty(_currentLanguage));
        var zhItem2 = MakeItem(App.GetString("Setting_Language_Chinese"), _currentLanguage == "zh-CN");
        var twItem = MakeItem(App.GetString("Setting_Language_Traditional"), _currentLanguage == "zh-TW");
        var enItem = MakeItem(App.GetString("Setting_Language_English"), _currentLanguage == "en-US");
        var koItem = MakeItem(App.GetString("Setting_Language_Korean"), _currentLanguage == "ko-KR");
        var jaItem = MakeItem(App.GetString("Setting_Language_Japanese"), _currentLanguage == "ja-JP");

        zhItem.Click += (_, _) => SelectLanguage("");
        zhItem2.Click += (_, _) => SelectLanguage("zh-CN");
        twItem.Click += (_, _) => SelectLanguage("zh-TW");
        enItem.Click += (_, _) => SelectLanguage("en-US");
        koItem.Click += (_, _) => SelectLanguage("ko-KR");
        jaItem.Click += (_, _) => SelectLanguage("ja-JP");

        menu.Items.Add(zhItem); menu.Items.Add(zhItem2); menu.Items.Add(twItem); menu.Items.Add(enItem); menu.Items.Add(koItem); menu.Items.Add(jaItem);
        menu.ShowAt(LanguageButton, new Windows.Foundation.Point(0, LanguageButton.ActualHeight + 4));
    }

    private void SelectLanguage(string lang)
    {
        if (lang == _currentLanguage) return;
        _pendingLanguage = lang;
        ShowLanguageRestartOverlay();
    }

    private void ShowLanguageRestartOverlay()
    {
        if (_languageRestartAnimating) return;
        _languageRestartAnimating = true;
        LanguageRestartScrim.Opacity = 0;

        LanguageRestartDialog.Opacity = 0;

        LanguageRestartDialogTransform.ScaleX = 0.92; LanguageRestartDialogTransform.ScaleY = 0.92; LanguageRestartDialogTransform.TranslateY = 20;
        RegisterDialogClamp(LanguageRestartOverlay, LanguageRestartDialog);
        LanguageRestartOverlay.Visibility = Visibility.Visible;
        var sb = new Storyboard();
        var sa = new DoubleAnimation { To = 1, Duration = TimeSpan.FromMilliseconds(250) };
        Storyboard.SetTarget(sa, LanguageRestartScrim); Storyboard.SetTargetProperty(sa, "Opacity");
        sb.Children.Add(sa);
        var da = new DoubleAnimation { To = 1, Duration = TimeSpan.FromMilliseconds(300) };
        Storyboard.SetTarget(da, LanguageRestartDialog); Storyboard.SetTargetProperty(da, "Opacity");
        sb.Children.Add(da);
        var xa = new DoubleAnimation { To = 1, Duration = TimeSpan.FromMilliseconds(400), EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut } };
        Storyboard.SetTarget(xa, LanguageRestartDialogTransform); Storyboard.SetTargetProperty(xa, "ScaleX");
        sb.Children.Add(xa);
        var ya = new DoubleAnimation { To = 1, Duration = TimeSpan.FromMilliseconds(400), EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut } };
        Storyboard.SetTarget(ya, LanguageRestartDialogTransform); Storyboard.SetTargetProperty(ya, "ScaleY");
        sb.Children.Add(ya);
        var ty = new DoubleAnimation { To = 0, Duration = TimeSpan.FromMilliseconds(400), EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut } };
        Storyboard.SetTarget(ty, LanguageRestartDialogTransform); Storyboard.SetTargetProperty(ty, "TranslateY");
        sb.Children.Add(ty);
        sb.Completed += (_, _) => _languageRestartAnimating = false;
        sb.Begin();
    }

    private void HideLanguageRestartOverlay()
    {

        if (_languageRestartAnimating) return;
        _languageRestartAnimating = true;
        var sb = new Storyboard();
        var sa = new DoubleAnimation { To = 0, Duration = TimeSpan.FromMilliseconds(200) };
        Storyboard.SetTarget(sa, LanguageRestartScrim); Storyboard.SetTargetProperty(sa, "Opacity");
        sb.Children.Add(sa);
        var da = new DoubleAnimation { To = 0, Duration = TimeSpan.FromMilliseconds(200) };
        Storyboard.SetTarget(da, LanguageRestartDialog); Storyboard.SetTargetProperty(da, "Opacity");
        sb.Children.Add(da);
        var xa = new DoubleAnimation { To = 0.92, Duration = TimeSpan.FromMilliseconds(250), EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
        Storyboard.SetTarget(xa, LanguageRestartDialogTransform); Storyboard.SetTargetProperty(xa, "ScaleX");
        sb.Children.Add(xa);
        var ya = new DoubleAnimation { To = 0.92, Duration = TimeSpan.FromMilliseconds(250), EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
        Storyboard.SetTarget(ya, LanguageRestartDialogTransform); Storyboard.SetTargetProperty(ya, "ScaleY");
        sb.Children.Add(ya);
        var ty = new DoubleAnimation { To = 20, Duration = TimeSpan.FromMilliseconds(250), EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
        Storyboard.SetTarget(ty, LanguageRestartDialogTransform); Storyboard.SetTargetProperty(ty, "TranslateY");
        sb.Children.Add(ty);
        sb.Completed += (_, _) => { LanguageRestartOverlay.Visibility = Visibility.Collapsed; _languageRestartAnimating = false; };
        sb.Begin();
    }

    private async void LanguageRestartConfirm_Click(object sender, RoutedEventArgs e)
    {
        if (_restarting) return;
        _restarting = true;


        await MainWindow.FlushPendingEditorForRestartAsync();

        if (LanguageRestartOverlay.Visibility != Visibility.Visible) { _restarting = false; return; }
        if (_pendingLanguage == null) { _restarting = false; return; }
        var pendingLanguage = _pendingLanguage;

        var oldLanguage = _currentLanguage;
        _currentLanguage = pendingLanguage;
        _pendingLanguage = null;
        UpdateLanguageButton();
        HideLanguageRestartOverlay();

        if (App.Store is { } ls)
        {
            ls.Database.AppSettings.AppLanguage = _currentLanguage;
            if (!ls.SaveSync())
            {
                _restarting = false;
                _currentLanguage = oldLanguage;
                ls.Database.AppSettings.AppLanguage = oldLanguage;
                UpdateLanguageButton();
                return;
            }
        }
        Services.StickySync.UpdateLanguage(string.IsNullOrEmpty(_currentLanguage) ? App.ResolveSystemLanguage() : _currentLanguage);
        App.SaveLanguageHint(_currentLanguage);



        RestartAppNow();
    }

    private void LanguageRestartCancel_Click(object sender, RoutedEventArgs e)
    {
        _pendingLanguage = null;
        HideLanguageRestartOverlay();
    }

    private void LanguageRestartClose_Click(object sender, RoutedEventArgs e)
    {
        _pendingLanguage = null;
        HideLanguageRestartOverlay();
    }

    private void LanguageRestartScrim_Tapped(object sender, TappedRoutedEventArgs e)
    {
        _pendingLanguage = null;
        HideLanguageRestartOverlay();
    }

    private void UpdateLanguageButton()
    {
        var text = string.IsNullOrEmpty(_currentLanguage)
            ? App.GetString("Setting_Language_Auto")
            : _currentLanguage switch
            {
                "en-US" => App.GetString("Setting_Language_English"),
                "zh-TW" => App.GetString("Setting_Language_Traditional"),
                "ko-KR" => App.GetString("Setting_Language_Korean"),
                "ja-JP" => App.GetString("Setting_Language_Japanese"),
                _ => App.GetString("Setting_Language_Chinese"),
            };
        ((TextBlock)LanguageButton.Content).Text = text;
    }

    private void UpdateCloseBehaviorButton()
    {
        bool on = _closeBehavior == "托盘驻留";
        ((TextBlock)CloseBehaviorButton.Content).Text = on ? App.GetString("Setting_ExitBehavior_Tray") : App.GetString("Setting_ExitBehavior_Exit");
        ApplyToggleState(CloseBehaviorButton, on);
    }




    private void UpdateAutoLockCard()
    {
        var settings = App.Store?.Database.AppSettings;
        if (settings == null) return;
        _autoLockOnSystemLock = settings.AutoLockOnSystemLock;
        _autoLockSeconds = settings.AutoLockSeconds;

        AutoLockCard.Visibility = _isPrivacyLockEnabled ? Visibility.Visible : Visibility.Collapsed;
        AutoLockTitleText.Text = App.GetString("Setting_AutoLock_Title");
        AutoLockSyncConfirmTitleText.Text = App.GetString("Setting_AutoLock_SyncConfirm_Title");
        AutoLockSyncConfirmMessageText.Text = App.GetString("Setting_AutoLock_SyncConfirm_Desc");
        AutoLockSyncConfirmCancelText.Text = App.GetString("Common_Button_Cancel");
        AutoLockSyncConfirmOkText.Text = App.GetString("Setting_AutoLock_SyncConfirm_Ok");

        ApplyToggleState(SyncLockButton, _autoLockOnSystemLock);
        SyncLockButtonText.Text = App.GetString("Setting_AutoLock_Sync");

        ApplyToggleState(AutoLockButton, _autoLockSeconds > 0);
        AutoLockButtonText.Text = _autoLockSeconds > 0


            ? ("" + _autoLockSeconds + "s").Replace("3600s", "60min").Replace("1800s", "30min").Replace("600s", "10min").Replace("300s", "5min").Replace("60s", "1min")
            : App.GetString("Setting_AutoLock_Never");


        QuickCaptureTitleText.Text = App.GetString("Setting_QuickCapture");


        QuickCaptureInfoTitleText.Text = App.GetString("Setting_QuickCapture");
        QuickCaptureInfoMessageText.Text = App.GetString("Setting_QuickCapture_Desc");
        QuickCaptureInfoCancelText.Text = App.GetString("Common_Button_Cancel");
        QuickCaptureInfoOkText.Text = App.GetString("Common_Button_Confirm");
        ApplyQuickCaptureState();
    }

    private void ApplyQuickCaptureState()
    {
        var s = App.Store?.Database.AppSettings;
        bool on = s?.QuickCaptureEnabled ?? false;
        ApplyToggleState(QuickCaptureSwitchButton, on);
        QuickCaptureSwitchText.Text = App.GetString(on ? "Setting_QuickCapture_On" : "Setting_QuickCapture_Off");

        QuickCaptureHotkeyButton.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
        ApplyToggleState(QuickCaptureHotkeyButton, false);
        QuickCaptureHotkeyText.Text = s?.QuickCaptureHotkey switch
        {
            "CtrlAltN" => "Ctrl+Alt+N",
            "AltN" => "Alt+N",
            "CtrlShiftSpace" => "Ctrl+Shift+Space",
            _ => "Ctrl+Shift+N"
        };
    }

    private void QuickCaptureSwitchButton_Click(object sender, RoutedEventArgs e)
    {


        var menu = new MenuFlyout { MenuFlyoutPresenterStyle = (Style)Application.Current.Resources["GlassMenuFlyoutPresenterStyle"] };
        var accentBrush = App.GetBrush("AppTextPrimaryBrush");
        var normalBrush = App.GetBrush("AppTextSecondaryBrush");
        var on = App.Store?.Database.AppSettings?.QuickCaptureEnabled ?? false;
        MenuFlyoutItem MakeItem(string text, bool active)
        {
            var item = new MenuFlyoutItem { Style = (Style)Application.Current.Resources["GlassMenuFlyoutItemStyle"], Text = text, Foreground = active ? accentBrush : normalBrush };
            if (active) item.Icon = new FontIcon { Glyph = "\uE73E", FontSize = 12, Foreground = accentBrush };
            return item;
        }
        var onItem = MakeItem(App.GetString("Setting_QuickCapture_On"), on);
        var offItem = MakeItem(App.GetString("Setting_QuickCapture_Off"), !on);
        onItem.Click += (_, _) => ShowQuickCaptureInfoDialog();
        offItem.Click += (_, _) => SetQuickCaptureEnabled(false);
        menu.Items.Add(onItem); menu.Items.Add(offItem);
        menu.ShowAt(QuickCaptureSwitchButton, new Windows.Foundation.Point(0, QuickCaptureSwitchButton.ActualHeight + 4));
    }

    private void SetQuickCaptureEnabled(bool on)
    {
        var s = App.Store?.Database.AppSettings; if (s == null) return;
        s.QuickCaptureEnabled = on;
        ApplyQuickCaptureState();
        _ = App.Store?.SaveAsync();
        App.MainWindow?.ApplyQuickCaptureHotkey();
    }

    private void ShowQuickCaptureInfoDialog()
    {
        _animQuickCaptureInfo = false;
        ShowOverlay(QuickCaptureInfoOverlay, QuickCaptureInfoDialog, QuickCaptureInfoDialogTransform);
    }
    private void HideQuickCaptureInfoDialog()
    {
        if (_animQuickCaptureInfo) return;
        _animQuickCaptureInfo = true;
        HideOverlay(QuickCaptureInfoOverlay, QuickCaptureInfoDialog, QuickCaptureInfoDialogTransform, () => _animQuickCaptureInfo = false);
    }
    private void QuickCaptureInfoClose_Click(object sender, RoutedEventArgs e) => HideQuickCaptureInfoDialog();
    private void QuickCaptureInfoCancel_Click(object sender, RoutedEventArgs e) => HideQuickCaptureInfoDialog();
    private void QuickCaptureInfoScrim_Tapped(object sender, TappedRoutedEventArgs e)
    { if (ReferenceEquals(e.OriginalSource, QuickCaptureInfoScrim)) HideQuickCaptureInfoDialog(); }
    private void QuickCaptureInfoOk_Click(object sender, RoutedEventArgs e)
    {
        SetQuickCaptureEnabled(true);
        HideQuickCaptureInfoDialog();
        App.ShowToast(App.GetString("Common_Toast_Created"));
    }

    private void QuickCaptureHotkeyButton_Click(object sender, RoutedEventArgs e)
    {
        var menu = new MenuFlyout { MenuFlyoutPresenterStyle = (Style)Application.Current.Resources["GlassMenuFlyoutPresenterStyle"] };
        var accentBrush = App.GetBrush("AppTextPrimaryBrush");
        var normalBrush = App.GetBrush("AppTextSecondaryBrush");
        var current = App.Store?.Database.AppSettings?.QuickCaptureHotkey;
        foreach (var (id, label) in new[] { ("CtrlShiftN", "Ctrl+Shift+N"), ("CtrlAltN", "Ctrl+Alt+N"), ("AltN", "Alt+N"), ("CtrlShiftSpace", "Ctrl+Shift+Space") })
        {
            var mi = new MenuFlyoutItem { Style = (Style)Application.Current.Resources["GlassMenuFlyoutItemStyle"], Text = label };
            mi.Foreground = current == id ? accentBrush : normalBrush;
            mi.Click += (_, _) =>
            {
                var s = App.Store?.Database.AppSettings; if (s == null) return;
                s.QuickCaptureHotkey = id;
                ApplyQuickCaptureState();
                _ = App.Store?.SaveAsync();
                App.MainWindow?.ApplyQuickCaptureHotkey();
            };
            menu.Items.Add(mi);
        }
        menu.ShowAt(QuickCaptureHotkeyButton, new Windows.Foundation.Point(0, QuickCaptureHotkeyButton.ActualHeight + 4));
    }



    private void SyncLockButton_Click(object sender, RoutedEventArgs e)
    {
        var menu = new MenuFlyout { MenuFlyoutPresenterStyle = (Style)Application.Current.Resources["GlassMenuFlyoutPresenterStyle"] };
        var accentBrush = App.GetBrush("AppTextPrimaryBrush");
        var normalBrush = App.GetBrush("AppTextSecondaryBrush");
        MenuFlyoutItem MakeItem(string text, bool active)
        {
            var item = new MenuFlyoutItem { Style = (Style)Application.Current.Resources["GlassMenuFlyoutItemStyle"], Text = text, Foreground = active ? accentBrush : normalBrush };
            if (active) item.Icon = new FontIcon { Glyph = "\uE73E", FontSize = 12, Foreground = accentBrush };
            return item;
        }
        var onItem = MakeItem(App.GetString("Setting_Autostart_On"), _autoLockOnSystemLock);
        var offItem = MakeItem(App.GetString("Setting_Autostart_Off"), !_autoLockOnSystemLock);
        onItem.Click += (_, _) => ShowAutoLockSyncConfirmDialog();
        offItem.Click += (_, _) => { _autoLockOnSystemLock = false; PersistAutoLock(); UpdateAutoLockCard(); App.ShowToast(App.GetString("Common_Toast_Switched")); };
        menu.Items.Add(onItem); menu.Items.Add(offItem);
        menu.ShowAt(SyncLockButton, new Windows.Foundation.Point(0, SyncLockButton.ActualHeight + 4));
    }

    private void AutoLockButton_Click(object sender, RoutedEventArgs e)
    {
        var menu = new MenuFlyout { MenuFlyoutPresenterStyle = (Style)Application.Current.Resources["GlassMenuFlyoutPresenterStyle"] };
        var accentBrush = App.GetBrush("AppTextPrimaryBrush");
        var normalBrush = App.GetBrush("AppTextSecondaryBrush");

        var options = new (string Label, int Seconds)[] {
            (App.GetString("Setting_AutoLock_Never"), 0),
            (string.Format(App.GetString("Setting_AutoLock_Seconds"), 30), 30),
            (string.Format(App.GetString("Setting_AutoLock_Minutes"), 1), 60),
            (string.Format(App.GetString("Setting_AutoLock_Minutes"), 5), 300),
            (string.Format(App.GetString("Setting_AutoLock_Minutes"), 10), 600),
            (string.Format(App.GetString("Setting_AutoLock_Minutes"), 30), 1800),
            (string.Format(App.GetString("Setting_AutoLock_Minutes"), 60), 3600),
        };
        foreach (var opt in options)
        {
            var item = new MenuFlyoutItem { Style = (Style)Application.Current.Resources["GlassMenuFlyoutItemStyle"], Text = opt.Label, Foreground = _autoLockSeconds == opt.Seconds ? accentBrush : normalBrush };
            if (_autoLockSeconds == opt.Seconds) item.Icon = new FontIcon { Glyph = "\uE73E", FontSize = 12, Foreground = accentBrush };
            var sec = opt.Seconds;
            item.Click += (_, _) => { _autoLockSeconds = sec; PersistAutoLock(); UpdateAutoLockCard(); App.ShowToast(App.GetString("Common_Toast_Switched")); };
            menu.Items.Add(item);
        }
        menu.ShowAt(AutoLockButton, new Windows.Foundation.Point(0, AutoLockButton.ActualHeight + 4));
    }

    private void PersistAutoLock()
    {
        var settings = App.Store?.Database.AppSettings;
        if (settings == null) return;
        settings.AutoLockOnSystemLock = _autoLockOnSystemLock;
        settings.AutoLockSeconds = _autoLockSeconds;
        App.Store?.SaveAsync();
    }


    private void ShowAutoLockSyncConfirmDialog()
    {
        _animAutoLockSync = false;
        ShowOverlay(AutoLockSyncConfirmOverlay, AutoLockSyncConfirmDialog, AutoLockSyncConfirmDialogTransform);
    }
    private void HideAutoLockSyncConfirmDialog()
    {
        if (_animAutoLockSync) return;
        _animAutoLockSync = true;
        HideOverlay(AutoLockSyncConfirmOverlay, AutoLockSyncConfirmDialog, AutoLockSyncConfirmDialogTransform, () => _animAutoLockSync = false);
    }
    private void AutoLockSyncConfirmClose_Click(object sender, RoutedEventArgs e) => HideAutoLockSyncConfirmDialog();
    private void AutoLockSyncConfirmCancel_Click(object sender, RoutedEventArgs e) => HideAutoLockSyncConfirmDialog();
    private void AutoLockSyncConfirmScrim_Tapped(object sender, TappedRoutedEventArgs e)
    { if (ReferenceEquals(e.OriginalSource, AutoLockSyncConfirmScrim)) HideAutoLockSyncConfirmDialog(); }
    private void AutoLockSyncConfirmOk_Click(object sender, RoutedEventArgs e)
    {
        _autoLockOnSystemLock = true;
        PersistAutoLock(); UpdateAutoLockCard();
        App.ShowToast(App.GetString("Common_Toast_Switched"));
        HideAutoLockSyncConfirmDialog();
    }



    private bool _winHelloEnabled;

    private void WindowsHelloButton_Click(object sender, RoutedEventArgs e)
    {
        var menu = new MenuFlyout { MenuFlyoutPresenterStyle = (Style)Application.Current.Resources["GlassMenuFlyoutPresenterStyle"] };
        var accentBrush = App.GetBrush("AppTextPrimaryBrush");
        var normalBrush = App.GetBrush("AppTextSecondaryBrush");

        MenuFlyoutItem MakeItem(string text, bool active)
        {
            var item = new MenuFlyoutItem { Style = (Style)Application.Current.Resources["GlassMenuFlyoutItemStyle"], Text = text, Foreground = active ? accentBrush : normalBrush };
            if (active) item.Icon = new FontIcon { Glyph = "\uE73E", FontSize = 12, Foreground = accentBrush };
            return item;
        }

        var onItem = MakeItem(App.GetString("Setting_Autostart_On"), _winHelloEnabled);
        var offItem = MakeItem(App.GetString("Setting_Autostart_Off"), !_winHelloEnabled);
        onItem.Click += (_, _) => SetWinHelloEnabled(true);
        offItem.Click += (_, _) => SetWinHelloEnabled(false);

        menu.Items.Add(onItem); menu.Items.Add(offItem);
        menu.ShowAt(WindowsHelloButton, new Windows.Foundation.Point(0, WindowsHelloButton.ActualHeight + 4));
    }

    private void SetWinHelloEnabled(bool enabled)
    {
        if (enabled)
        {
            ShowWindowsHelloDialog();
            _ = StartWinHelloEnableAsync().ContinueWith(t => System.Diagnostics.Debug.WriteLine($"WinHello enable failed: {t.Exception}"), System.Threading.Tasks.TaskContinuationOptions.OnlyOnFaulted);
            return;
        }
        WindowsHelloService.Disable();
        _winHelloEnabled = false;
        ApplyToggleState(WindowsHelloButton, false);
    }






    private async Task StartWinHelloEnableAsync()
    {
        if (_winHelloEnabling) return;
        _winHelloEnabling = true;
        try
        {
        if (!await WindowsHelloService.IsAvailableAsync())
        {

            return;
        }

        var result = await WindowsHelloService.RequestVerificationAsync(App.GetString("Lock_WinHello_VerifyMsg"));
        if (result != UserConsentVerificationResult.Verified)
        {

            return;
        }

        var pw = App.Store?.Password;


        bool enabled = false;
        if (!string.IsNullOrEmpty(pw))
            enabled = await System.Threading.Tasks.Task.Run(() => WindowsHelloService.Enable(pw!));
        if (!enabled)
        {

            return;
        }

        _winHelloEnabled = true;
        ApplyToggleState(WindowsHelloButton, true);
        HideWindowsHelloDialog();
        }
        finally { _winHelloEnabling = false; }
    }

    private bool _animWinHello;
    private bool _winHelloEnabling;

    private void ShowWindowsHelloDialog()
    {
        _animWinHello = false;
        WindowsHelloDialogTransform.ScaleX = 0.94; WindowsHelloDialogTransform.ScaleY = 0.94; WindowsHelloDialogTransform.TranslateY = 24;
        WindowsHelloDialog.Opacity = 0;
        WindowsHelloScrim.Opacity = 0;
        RegisterDialogClamp(WindowsHelloOverlay, WindowsHelloDialog);
        WindowsHelloOverlay.Visibility = Visibility.Visible;
        var sb = new Storyboard();
        var si = new DoubleAnimation { To = 1, Duration = TimeSpan.FromMilliseconds(250) };
        Storyboard.SetTarget(si, WindowsHelloScrim); Storyboard.SetTargetProperty(si, "Opacity"); sb.Children.Add(si);
        var di = new DoubleAnimation { To = 1, Duration = TimeSpan.FromMilliseconds(300) };
        Storyboard.SetTarget(di, WindowsHelloDialog); Storyboard.SetTargetProperty(di, "Opacity"); sb.Children.Add(di);
        Motion.AddDialogShowTransform(sb, WindowsHelloDialogTransform);
        sb.Begin();
    }

    private void HideWindowsHelloDialog()
    {
        if (_animWinHello) return;
        _animWinHello = true;
        var sb = new Storyboard();
        var so = new DoubleAnimation { To = 0, Duration = TimeSpan.FromMilliseconds(200) };
        Storyboard.SetTarget(so, WindowsHelloScrim); Storyboard.SetTargetProperty(so, "Opacity"); sb.Children.Add(so);
        var d = new DoubleAnimation { To = 0, Duration = TimeSpan.FromMilliseconds(200) };
        Storyboard.SetTarget(d, WindowsHelloDialog); Storyboard.SetTargetProperty(d, "Opacity"); sb.Children.Add(d);
        Motion.AddDialogHideTransform(sb, WindowsHelloDialogTransform);
        sb.Completed += (_, _) =>
        {
            WindowsHelloOverlay.Visibility = Visibility.Collapsed;
            _animWinHello = false;

            if (!_winHelloEnabled) ApplyToggleState(WindowsHelloButton, false);
        };
        sb.Begin();
    }

    private void WindowsHelloClose_Click(object sender, RoutedEventArgs e) => HideWindowsHelloDialog();

    private void WindowsHelloScrim_Tapped(object sender, TappedRoutedEventArgs e)
    {
        if (ReferenceEquals(e.OriginalSource, WindowsHelloScrim)) HideWindowsHelloDialog();
    }

    private void WindowsHelloGotoSettings_Click(object sender, RoutedEventArgs e)
    {
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("ms-settings:signinoptions") { UseShellExecute = true }); }
        catch { }
    }




    private void WebsiteButton_Click(object sender, RoutedEventArgs e)
    {

        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("https://novara.xin") { UseShellExecute = true }); }
        catch { }
    }


    private const string EasterEggUrl = "https://novara.xin/easteregg.html";


    private void Footer_Tapped(object sender, Microsoft.UI.Xaml.Input.TappedRoutedEventArgs e)
    {
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(EasterEggUrl) { UseShellExecute = true }); }
        catch { }
    }

    private async void SetPasswordConfirm_Click(object sender, RoutedEventArgs e)
    {
        var pw = SetPasswordBox.Text.Trim();
        var confirm = SetPasswordConfirmBox.Text.Trim();

        if (pw.Length < 6)
        {
            FlashTextBox(SetPasswordBox);
            return;
        }
        if (confirm.Length < 6)
        {
            FlashTextBox(SetPasswordConfirmBox);
            return;
        }
        if (pw != confirm)
        {
            FlashTextBox(SetPasswordConfirmBox);
            return;
        }








        if (_setPwdInFlight) return;
        _setPwdInFlight = true;
        SetPasswordConfirmButton.IsEnabled = false;
        SetPasswordCancelButton.IsEnabled = false;
        bool created, encrypted;
        try
        {
            var outcome = await System.Threading.Tasks.Task.Run(() =>
            {
                if (!PasswordService.Create(pw)) return (Created: false, Encrypted: false);
                return (Created: true, Encrypted: App.Store?.EnableEncryption(pw) == true);
            });
            created = outcome.Created;
            encrypted = outcome.Encrypted;
        }
        catch { created = false; encrypted = false; }
        finally
        {
            _setPwdInFlight = false;
            SetPasswordConfirmButton.IsEnabled = true;
            SetPasswordCancelButton.IsEnabled = true;
        }

        if (!created) { FlashTextBox(SetPasswordBox); return; }
        PasswordService.SetLockoutEnabled(SetPasswordLockoutCheckBox.IsChecked == true);
        if (!encrypted)
        {


            PasswordService.Delete();
            PasswordService.DeleteLockout();
            FlashTextBox(SetPasswordBox);
            FlashTextBox(SetPasswordConfirmBox);
            return;
        }
        PersistSetting(s => s.PrivacyLockEnabled = true);
        _isPrivacyLockEnabled = true;
        UpdatePrivacyLockUI();




        App.ApplyStoredSettings();
        HideSetPasswordDialog();
    }

    private bool _animSetPwd;
    private bool _setPwdInFlight;
    private bool _animChangePwd;
    private bool _animCloseLock;
    private bool _animWarn;
    private bool _animWarnShow;


    private static string ThemeLabel(string theme) => theme switch
    {
        "深色模式" => App.GetString("Setting_Theme_Dark"),
        "浅色模式" => App.GetString("Setting_Theme_Light"),
        "跟随系统" => App.GetString("Setting_Theme_System"),
        _ when PaperTheme.TryGetLabelKey(theme, out var key) => App.GetString(key),
        _ => theme
    };

    private void UpdateThemeButton()
    {
        ((TextBlock)ThemeButton.Content).Text = ThemeLabel(_currentTheme);
        ApplyToggleState(ThemeButton, _currentTheme != "跟随系统");
    }

    private void WelcomeOnLaunchButton_Click(object sender, RoutedEventArgs e)
    {
        var menu = new MenuFlyout { MenuFlyoutPresenterStyle = (Style)Application.Current.Resources["GlassMenuFlyoutPresenterStyle"] };
        var accentBrush = App.GetBrush("AppTextPrimaryBrush");
        var normalBrush = App.GetBrush("AppTextSecondaryBrush");

        MenuFlyoutItem MakeItem(string text, bool active)
        {
            var item = new MenuFlyoutItem { Style = (Style)Application.Current.Resources["GlassMenuFlyoutItemStyle"], Text = text, Foreground = active ? accentBrush : normalBrush };
            if (active) item.Icon = new FontIcon { Glyph = "\uE73E", FontSize = 12, Foreground = accentBrush };
            return item;
        }

        var onItem = MakeItem(App.GetString("Setting_Autostart_On"), _welcomeOnLaunch);
        var offItem = MakeItem(App.GetString("Setting_Autostart_Off"), !_welcomeOnLaunch);
        onItem.Click += (_, _) => { if (_welcomeOnLaunch) return; _welcomeOnLaunch = true; PersistSetting(s => s.WelcomeOnLaunch = true); UpdateWelcomeButton(); App.ShowToast(App.GetString("Common_Toast_Switched")); };
        offItem.Click += (_, _) => { if (!_welcomeOnLaunch) return; _welcomeOnLaunch = false; PersistSetting(s => s.WelcomeOnLaunch = false); UpdateWelcomeButton(); App.ShowToast(App.GetString("Common_Toast_Switched")); };

        menu.Items.Add(onItem); menu.Items.Add(offItem);
        menu.ShowAt(WelcomeOnLaunchButton, new Windows.Foundation.Point(0, WelcomeOnLaunchButton.ActualHeight + 4));
    }

    private void UpdateWelcomeButton()
    {
        ((TextBlock)WelcomeOnLaunchButton.Content).Text = _welcomeOnLaunch
            ? App.GetString("Setting_Autostart_On")
            : App.GetString("Setting_Autostart_Off");
        ApplyToggleState(WelcomeOnLaunchButton, _welcomeOnLaunch);
    }


    private string _edgeMenuSide = "左侧";

    private void EdgeMenuSideButton_Click(object sender, RoutedEventArgs e)
    {
        var menu = new MenuFlyout();
        menu.MenuFlyoutPresenterStyle = (Style)Application.Current.Resources["GlassMenuFlyoutPresenterStyle"];
        void AddItem(string textKey, string side)
        {
            bool active = _edgeMenuSide == side;
            var item = new MenuFlyoutItem
            {
                Style = (Style)Application.Current.Resources["GlassMenuFlyoutItemStyle"],
                Text = App.GetString(textKey),
                Foreground = active ? App.GetBrush("AppTextPrimaryBrush") : App.GetBrush("AppTextSecondaryBrush")
            };
            if (active) item.Icon = new FontIcon { Glyph = "\uE73E", FontSize = 12, Foreground = App.GetBrush("AppTextPrimaryBrush") };
            item.Click += (_, _) =>
            {
                if (_edgeMenuSide == side) return;
                _edgeMenuSide = side;
                PersistSetting(s => s.EdgeMenuSide = side);
                UpdateEdgeMenuButton();
                App.MainWindow?.ApplyEdgeMenuSide(_edgeMenuSide);
                App.ShowToast(App.GetString("Common_Toast_Switched"));
            };
            menu.Items.Add(item);
        }
        AddItem("EdgeMenu_Left", "左侧");
        AddItem("EdgeMenu_Right", "右侧");
        menu.ShowAt(EdgeMenuSideButton, new Windows.Foundation.Point(0, EdgeMenuSideButton.ActualHeight + 4));
    }

    private void UpdateEdgeMenuButton()
    {
        EdgeMenuSideText.Text = App.GetString(_edgeMenuSide == "右侧" ? "EdgeMenu_Right" : "EdgeMenu_Left");
    }



    private Novara.Services.UpdateService.LatestRelease? _latestRelease;
    private System.Threading.CancellationTokenSource? _updateDownloadCts;
    private bool _updateBusy;
    private bool _updateDownloading;

    private async void UpdateCheckButton_Click(object sender, RoutedEventArgs e)
    {
        if (_updateBusy) return;
        _updateBusy = true;
        try
        {
            var result = await Novara.Services.UpdateService.CheckLatestAsync(System.Threading.CancellationToken.None);
            if (result.Error != null)
            {
                if (result.Error != "cancelled") App.ShowToast(string.Format(App.GetString("Update_Fail_Check"), result.Error), ToastTone.Error);
                return;
            }
            if (result.Latest == null || !result.HasNewer)
            {
                RefreshUpdateDot();
                UpdateUpToDateTitleText.Text = App.GetString("Update_Latest_Title");
                UpdateUpToDateBodyText.Text = string.Format(App.GetString("Update_Latest_Body"), Novara.Services.UpdateService.CurrentVersion);
                UpdateUpToDateCloseText.Text = App.GetString("Update_Btn_Cancel");
                RegisterDialogClamp(UpdateUpToDateOverlay, UpdateUpToDateDialog);
                ShowOverlay(UpdateUpToDateOverlay, UpdateUpToDateDialog, UpdateUpToDateDialogTransform);
                return;
            }
            _latestRelease = result.Latest;
            UpdateNewTitleText.Text = string.Format(App.GetString("Update_New_Title"), _latestRelease.Version);
            UpdateNewDateText.Text = string.IsNullOrEmpty(_latestRelease.Date) ? "" : string.Format(App.GetString("Update_New_Date"), _latestRelease.Date);
            var lang = App.CurrentLanguage;
            var notes = (lang == "zh-CN" || lang == "zh-TW")
                ? (_latestRelease.NotesZh ?? _latestRelease.NotesEn)
                : (_latestRelease.NotesEn ?? _latestRelease.NotesZh);
            UpdateNotesText.Text = notes ?? "";
            ResetUpdateInstallUi();
            RegisterDialogClamp(UpdateDialogOverlay, UpdateDialog);
            ShowOverlay(UpdateDialogOverlay, UpdateDialog, UpdateDialogTransform);
        }
        catch (Exception ex)
        {
            App.ShowToast(string.Format(App.GetString("Update_Fail_Check"), ex.Message), ToastTone.Error);
        }
        finally
        {
            _updateBusy = false;
        }
    }

    private async void UpdateUpgrade_Click(object sender, RoutedEventArgs e)
    {
        if (_latestRelease == null || _updateBusy) return;
        _updateBusy = true;
        UpdateInstallButton.IsEnabled = false;
        UpdateProgressBar.Visibility = Visibility.Visible;
        UpdateStatusText.Visibility = Visibility.Visible;
        UpdateProgressBar.Value = 0;
        UpdateStatusText.Text = string.Format(App.GetString("Update_Downloading"), 0);
        _updateDownloadCts = new System.Threading.CancellationTokenSource();
        _updateDownloading = true;
        try
        {
            var progress = new Progress<double>(v =>
            {
                UpdateProgressBar.Value = v;
                UpdateStatusText.Text = string.Format(App.GetString("Update_Downloading"), v);
            });
            var file = await Novara.Services.UpdateService.DownloadAsync(_latestRelease, progress, _updateDownloadCts.Token);
            _updateDownloading = false;
            UpdateStatusText.Text = App.GetString("Update_Verifying");
            var actual = await System.Threading.Tasks.Task.Run(() => Novara.Services.UpdateService.Sha256Hex(file));
            var expected = _latestRelease.Sha256?.Trim();
            if (string.IsNullOrEmpty(expected) || !string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase))
            {
                try { System.IO.File.Delete(file); } catch { }
                App.ShowToast(App.GetString("Update_Fail_Hash"), ToastTone.Error);
                ResetUpdateInstallUi();
                return;
            }
            UpdateStatusText.Text = App.GetString("Update_Launching");
            await System.Threading.Tasks.Task.Delay(400);
            if (App.Store != null) App.Store.SaveSync();
            try
            {
                Novara.Services.UpdateService.LaunchInstaller(file);
            }
            catch (Exception launchEx)
            {

                App.ShowToast(string.Format(App.GetString("Update_Fail_Launch"), launchEx.Message), ToastTone.Error);
                ResetUpdateInstallUi();
                return;
            }
            App.MainWindow?.BeginUpdateExit();
            App.MainWindow?.Close();
        }
        catch (OperationCanceledException)
        {
            ResetUpdateInstallUi();
        }
        catch (Exception ex)
        {
            App.ShowToast(string.Format(App.GetString("Update_Fail_Download"), ex.Message), ToastTone.Error);
            ResetUpdateInstallUi();
        }
        finally
        {
            _updateDownloadCts?.Dispose();
            _updateDownloadCts = null;
            _updateDownloading = false;
            _updateBusy = false;
        }
    }

    private void UpdateCancel_Click(object sender, RoutedEventArgs e) => CancelUpdateInteraction();

    private void CancelUpdateInteraction()
    {
        if (_updateBusy && _updateDownloading) { _updateDownloadCts?.Cancel(); return; }
        HideUpdateDialog();
    }

    private void UpdateDialogScrim_Tapped(object sender, TappedRoutedEventArgs e) => UpdateCancel_Click(sender, e);
    private void UpdateDialogClose_Click(object sender, RoutedEventArgs e) => UpdateCancel_Click(sender, e);
    private void UpdateUpToDateScrim_Tapped(object sender, TappedRoutedEventArgs e) => HideUpdateUpToDateDialog();
    private void UpdateUpToDateClose_Click(object sender, RoutedEventArgs e) => HideUpdateUpToDateDialog();
    private void HideUpdateDialog()
    {


        var rel = _latestRelease;
        if (App.Store is { IsLoaded: true } store && rel is not null)
        {
            store.Database.AppSettings.UpdatePendingVersion = rel.Version;
            _ = store.SaveAsync();
        }
        HideOverlay(UpdateDialogOverlay, UpdateDialog, UpdateDialogTransform, () => { });
        RefreshUpdateDot();
    }



    private void RefreshUpdateDot()
    {
        var db = App.Store?.Database;
        if (db == null) return;
        var pending = db.AppSettings.UpdatePendingVersion;
        if (!string.IsNullOrEmpty(pending) && !Novara.Services.UpdateService.IsNewer(pending, Novara.Services.UpdateService.CurrentVersion))
        {
            db.AppSettings.UpdatePendingVersion = "";
            _ = App.Store!.SaveAsync();
            pending = "";
        }
        UpdateAttentionDot.Visibility = string.IsNullOrEmpty(pending) ? Visibility.Collapsed : Visibility.Visible;
    }
    private void HideUpdateUpToDateDialog() => HideOverlay(UpdateUpToDateOverlay, UpdateUpToDateDialog, UpdateUpToDateDialogTransform, () => { });

    private void ResetUpdateInstallUi()
    {
        UpdateProgressBar.Visibility = Visibility.Collapsed;
        UpdateStatusText.Visibility = Visibility.Collapsed;
        UpdateCancelText.Text = App.GetString("Update_Btn_Cancel");
        UpdateInstallButton.IsEnabled = true;
        UpdateInstallText.Text = App.GetString("Update_Btn_Install");
    }


}
