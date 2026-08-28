/* ========== SettingsPage - Settings Tab ==========
Function: App settings - language/theme/autostart/tray behavior/privacy lock, import-export backup, database reset, restart overlays
Corresponding UI: SettingsPage.xaml.cs
Logic Range: Whole file business logic of this module
*/
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Windows.UI;
using Windows.Security.Credentials.UI;
using Novara.Services;
using Windows.Storage.Pickers;
using WinRT.Interop;
using Windows.ApplicationModel.DataTransfer;
using System.Security.Cryptography;

namespace Novara.Pages;

public sealed partial class SettingsPage : Page
{
    private string _currentTheme = App.CurrentTheme;
    private string _pendingTheme = string.Empty;
    private string _autoStart = "关闭";
    private string _contextMenu = "开启";
    private string _closeBehavior = "直接退出";
    private string _currentLanguage = ""; // i18n language card state machine (zh-CN/en-US/zh-TW/ko-KR/ja-JP; empty = follow system)
    private string _pendingLanguage = string.Empty;
    private bool _restarting; // #33: restart-confirm re-entry guard (prevent double process)
    private bool _languageRestartAnimating;
    private bool _isPrivacyLockEnabled = false;
    private readonly HashSet<string> _visibleTabs = new() { "备忘", "文件", "计划", "日记" };

    private bool _themeRestartAnimating;

    
    private bool _statsEnabled;
    private bool _statsExpanded;
    private bool _animAutoLockSync;
    private bool _autoLockOnSystemLock; 
    private int _autoLockSeconds;       
    private bool _welcomeOnLaunch; 

    private static readonly string[] TabNames = { "备忘", "文件", "计划", "日记" };

    public SettingsPage()
    {
        InitializeComponent();
        SettingsTitleText.Text = App.GetString("Setting_Title_Page"); 
        
        
        StatsExpandIcon.Data = App.CreateGeometry(IconData.CardCollapse);
        McpExpandIcon.Data = App.CreateGeometry(IconData.CardCollapse);
        Novara.Services.DialogDepth.AttachContainer((Grid)Content, autoVeil: true); 
        KeyDown += Page_KeyDown;
        BackPathIcon.Data = (Geometry)Microsoft.UI.Xaml.Markup.XamlBindingHelper.ConvertValue(typeof(Geometry), IconData.Back);

        Unloaded += (_, _) =>
        {
            foreach (var cts in _flashCtsMap.Values) { cts.Cancel(); cts.Dispose(); } // E5-24: per-box CTS map
            _flashCtsMap.Clear();
            // D4 (Round 5): M4 destroy all dialogs on page unload
            ResetConfirmOverlay.Visibility = Visibility.Collapsed;
            SetPasswordOverlay.Visibility = Visibility.Collapsed;
            ChangePasswordOverlay.Visibility = Visibility.Collapsed;
            ClosePrivacyLockOverlay.Visibility = Visibility.Collapsed;
            ThemeRestartOverlay.Visibility = Visibility.Collapsed;
            LanguageRestartOverlay.Visibility = Visibility.Collapsed;
            PrivacyLockWarningOverlay.Visibility = Visibility.Collapsed;
            ImportExportPasswordOverlay.Visibility = Visibility.Collapsed;
            ExportOptionsOverlay.Visibility = Visibility.Collapsed;
            ImportConfirmOverlay.Visibility = Visibility.Collapsed;
            ResetPasswordOverlay.Visibility = Visibility.Collapsed;
            ImportResultOverlay.Visibility = Visibility.Collapsed;
            BackupIntroOverlay.Visibility = Visibility.Collapsed;
            BackupNowOverlay.Visibility = Visibility.Collapsed;
            BackupAutoOverlay.Visibility = Visibility.Collapsed;
            BackupRestoreOverlay.Visibility = Visibility.Collapsed;
            BackupDeleteOverlay.Visibility = Visibility.Collapsed;
            BackupRestoreConfirmOverlay.Visibility = Visibility.Collapsed;
            BackupDeleteConfirmOverlay.Visibility = Visibility.Collapsed;
            BackupRestartOverlay.Visibility = Visibility.Collapsed;
            ExportMdNoticeOverlay.Visibility = Visibility.Collapsed;
            McpConfigOverlay.Visibility = Visibility.Collapsed;
            McpResetConfirmOverlay.Visibility = Visibility.Collapsed;
            McpCopyOverlay.Visibility = Visibility.Collapsed;
            McpDeleteConfirmOverlay.Visibility = Visibility.Collapsed;
            AutoLockSyncConfirmOverlay.Visibility = Visibility.Collapsed; // N6-11
            McpAuditOverlay.Visibility = Visibility.Collapsed;
            McpAuditClearConfirmOverlay.Visibility = Visibility.Collapsed;
            CsvImportPreviewOverlay.Visibility = Visibility.Collapsed;
            CsvExportNoticeOverlay.Visibility = Visibility.Collapsed;
            WindowsHelloOverlay.Visibility = Visibility.Collapsed; // ND8
            McpRevokeConfirmOverlay.Visibility = Visibility.Collapsed; // ND8
            // R1 (Round 5): reset all hide-animation flags on unload (prevent stuck)
            _animSetPwd = _animChangePwd = _animCloseLock = _animWarn = _animResetConfirm = _animResetPwd = _animImportPwd = _animImportConfirm = _animImportResult = false;
            _animBackupIntro = _animBackupNow = _animBackupAuto = _animBackupRestore = _animBackupDelete = _animBackupRestoreConfirm = _animBackupDeleteConfirm = false;
            _animExportMdNotice = _animExportOptions = _animWarnShow = false; 
            _themeRestartAnimating = _languageRestartAnimating = false; 
            _animMcpConfig = _animMcpResetConfirm = _animMcpCopy = _animMcpDeleteConfirm = _animMcpRevokeConfirm = false;
            _animMcpAudit = _animMcpAuditClear = false; 
            _animAutoLockSync = false; 
            _pendingVerifiedAction = null; // N5T1-02: a stale gated flow must not hijack the next password verify
            _pendingExport = false;        // N5T1-02: same for the native export flag
            _pendingReloadAfterImport = false; // N5T1-03: stale flag would fire an unrelated ReloadPages later
_animCsvPreview = false;
            _animCsvExportNotice = false;
        };
        Loaded += (_, _) =>
        {
            CardsFloatIn.Begin();

            // N5S-02: barrier self-heal - if the restore-restart overlay was torn down by a navigation
            // (IPC / Ctrl+K) and the user came back, put it straight back up; suppression is still on.
            if (App.Store?.IsSaveSuppressed == true && BackupRestartOverlay.Visibility != Visibility.Visible)
                ShowBackupRestartDialog();

            var settings = App.Store?.Database.AppSettings;
            if (settings != null)
            {
                _currentTheme = string.IsNullOrEmpty(settings.Theme) ? "跟随系统" : settings.Theme;
                // D2 (Round 5): S3 show real registry autostart state
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
                _currentLanguage = settings.AppLanguage ?? ""; // empty = follow system
                _backupEnabled = settings.BackupEnabled;
                _autoBackupEnabled = settings.AutoBackupEnabled;
                _backupFreqIndex = settings.BackupFreq;
                _statsEnabled = settings.StatsEnabled;
                _welcomeOnLaunch = settings.WelcomeOnLaunch;
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
            InitBackupFreqCombo();
            UpdateBackupUI();
            _statsExpanded = true; 
            UpdateStatsUI();
            UpdateWelcomeButton();
            StatsTitleText.Text = App.GetString("Setting_Stats_Title"); 
            UpdateMcpUI(); 
            App.MainWindow?.ApplyVisibleTabs(_visibleTabs);
        };
    }

    
    public void StealFocus() => FocusSink.Focus(FocusState.Programmatic);

    private void Page_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key != Windows.System.VirtualKey.Escape) return;
        if (PrivacyLockWarningOverlay.Visibility == Visibility.Visible) { HidePrivacyLockWarningDialog(); e.Handled = true; return; }
        if (ImportResultOverlay.Visibility == Visibility.Visible)
        {

            HideImportResult();
            if (_pendingReloadAfterImport) { _pendingReloadAfterImport = false; App.MainWindow?.ReloadPages(); }
            e.Handled = true;
            return;
        }
        if (ResetPasswordOverlay.Visibility == Visibility.Visible) { HideResetPasswordDialog(); e.Handled = true; return; }
        if (LanguageRestartOverlay.Visibility == Visibility.Visible) { _pendingLanguage = string.Empty; HideLanguageRestartOverlay(); e.Handled = true; return; }
        if (ExportOptionsOverlay.Visibility == Visibility.Visible) { HideExportOptionsDialog(); e.Handled = true; return; }
        if (ImportExportPasswordOverlay.Visibility == Visibility.Visible) { HideImportExportPasswordDialog(); e.Handled = true; return; }
        if (ThemeRestartOverlay.Visibility == Visibility.Visible) { _pendingTheme = string.Empty; HideThemeRestartOverlay(); e.Handled = true; return; }
        if (ResetConfirmOverlay.Visibility == Visibility.Visible) { HideResetConfirmDialog(); e.Handled = true; return; }
        if (ImportConfirmOverlay.Visibility == Visibility.Visible) { HideImportConfirmDialog(); e.Handled = true; return; }
        if (ChangePasswordOverlay.Visibility == Visibility.Visible) { HideChangePasswordDialog(); e.Handled = true; return; }
        if (ClosePrivacyLockOverlay.Visibility == Visibility.Visible) { HideClosePrivacyLockDialog(); e.Handled = true; return; }
        if (SetPasswordOverlay.Visibility == Visibility.Visible) { HideSetPasswordDialog(); e.Handled = true; return; }
        if (BackupIntroOverlay.Visibility == Visibility.Visible) { HideBackupIntroDialog(); e.Handled = true; return; }
        if (BackupNowOverlay.Visibility == Visibility.Visible) { HideBackupNowDialog(); e.Handled = true; return; }
        if (BackupAutoOverlay.Visibility == Visibility.Visible) { RevertBackupAutoDraft(); HideBackupAutoDialog(); UpdateBackupUI(); e.Handled = true; return; }
        if (BackupRestoreOverlay.Visibility == Visibility.Visible) { HideBackupRestoreDialog(); e.Handled = true; return; }
        if (BackupDeleteOverlay.Visibility == Visibility.Visible) { HideBackupDeleteDialog(); e.Handled = true; return; }
        if (BackupRestoreConfirmOverlay.Visibility == Visibility.Visible) { CancelBackupRestoreConfirm(); e.Handled = true; return; }
        if (BackupDeleteConfirmOverlay.Visibility == Visibility.Visible) { HideBackupDeleteConfirmDialog(); e.Handled = true; return; }
        if (BackupRestartOverlay.Visibility == Visibility.Visible) { e.Handled = true; RestartApp(rollbackOnFail: true); return; }
        if (ExportMdNoticeOverlay.Visibility == Visibility.Visible) { HideExportMdNoticeDialog(); e.Handled = true; return; }
        if (CsvImportPreviewOverlay.Visibility == Visibility.Visible) { HideCsvImportPreview(); e.Handled = true; return; }
        if (CsvExportNoticeOverlay.Visibility == Visibility.Visible) { HideCsvExportNoticeDialog(); e.Handled = true; return; }
        // N4T-05: the six 5.0-era dialogs were never wired into the Esc chain (U2 parity)
        if (WindowsHelloOverlay.Visibility == Visibility.Visible) { HideWindowsHelloDialog(); e.Handled = true; return; }
        if (McpConfigOverlay.Visibility == Visibility.Visible) { HideMcpConfigDialog(); e.Handled = true; return; }
        if (McpResetConfirmOverlay.Visibility == Visibility.Visible) { HideMcpResetConfirmDialog(); e.Handled = true; return; }
        if (McpCopyOverlay.Visibility == Visibility.Visible) { HideMcpCopyDialog(); e.Handled = true; return; }
        if (McpDeleteConfirmOverlay.Visibility == Visibility.Visible) { HideMcpDeleteConfirmDialog(); e.Handled = true; return; }
        if (McpRevokeConfirmOverlay.Visibility == Visibility.Visible) { HideMcpRevokeConfirmDialog(); e.Handled = true; return; }
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
            if (active) item.Icon = new FontIcon { Glyph = "\uE73E", FontSize = 12, Foreground = accentBrush };
            return item;
        }

        var darkItem = MakeItem(App.GetString("Setting_Theme_Dark"), _currentTheme == "深色模式");
        var lightItem = MakeItem(App.GetString("Setting_Theme_Light"), _currentTheme == "浅色模式");
        var systemItem = MakeItem(App.GetString("Setting_Theme_System"), _currentTheme == "跟随系统");

        darkItem.Click += (_, _) => HandleThemeSelection("深色模式");
        lightItem.Click += (_, _) => HandleThemeSelection("浅色模式");
        systemItem.Click += (_, _) => HandleThemeSelection("跟随系统");

        menu.Items.Add(darkItem); menu.Items.Add(lightItem); menu.Items.Add(systemItem);
        menu.ShowAt(ThemeButton, new Windows.Foundation.Point(0, ThemeButton.ActualHeight + 4));
    }

    private void HandleThemeSelection(string theme)
    {
        if (theme == _currentTheme) return;

        _pendingTheme = theme;
        ShowThemeRestartOverlay();
    }

    /* ========== SettingsPage Theme/Language Restart ==========
Function: Theme & language change restart overlays: sync save before relaunch (#34), re-entry guard (#33), launch-failure keep-session (#26)
Corresponding UI: SettingsPage.xaml.cs
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

    private void ThemeRestartConfirm_Click(object sender, RoutedEventArgs e)
    {
        if (_restarting) return; // #33
        _restarting = true;
        var oldTheme = _currentTheme; // E5-27: keep the previous value - roll back UI + in-memory setting if the save fails (E4-26)
        _currentTheme = _pendingTheme;
        UpdateThemeButton();
        _pendingTheme = string.Empty;
        HideThemeRestartOverlay();
        // Sync write: the app restarts immediately and async SaveAsync may not flush Theme in time.
        if (App.Store is { } ts)
        {
            ts.Database.AppSettings.Theme = _currentTheme;
            if (!ts.SaveSync())
            {
                _restarting = false;
                _currentTheme = oldTheme;                     // E5-27: no partial state - the button must not show a theme that never persisted
                ts.Database.AppSettings.Theme = oldTheme;
                UpdateThemeButton();
                return; // #34: abort restart on save failure (SaveFailed dialog already raised)
            }
        }
        
        string stickyTheme = _currentTheme == "深色模式" ? "dark" : _currentTheme == "浅色模式" ? "light" : Services.StickySync.ResolveTheme();
        Services.StickySync.UpdateTheme(stickyTheme);

        try
        {
            var exe = Environment.ProcessPath;
            if (string.IsNullOrEmpty(exe)) { _restarting = false; return; } // #26: abort restart if own path cannot be resolved
            Novara.MainWindow.ReleaseSingleInstance();
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo { FileName = exe, UseShellExecute = true });
        }
        catch
        {
            // #26: launch failed - keep session (avoid silent app loss), retryable
            System.Diagnostics.Debug.WriteLine("Novara 重启闭环：新进程启动失败，保持当前会话");
            _restarting = false;
            return;
        }
        if (App.MainWindow is { } mw) { mw.SetRestarting(); mw.ExitApp(saveFirst: false); } // E5-04: mark the restart on MainWindow so Closing skips the redundant second save; E4-09: data already saved above - ExitApp must not abort on a redundant save failure (mutex is already released, new process is running)
    }

    private void ThemeRestartCancel_Click(object sender, RoutedEventArgs e)
    {
        _pendingTheme = string.Empty;
        HideThemeRestartOverlay();
    }

    private void ThemeRestartClose_Click(object sender, RoutedEventArgs e)
    {
        _pendingTheme = string.Empty;
        HideThemeRestartOverlay();
    }

    private void ThemeRestartScrim_Tapped(object sender, TappedRoutedEventArgs e)
    {
        _pendingTheme = string.Empty;
        HideThemeRestartOverlay();
    }

    private void CustomizeTabsButton_Click(object sender, RoutedEventArgs e)
    {
        ShowTabsMenu();
    }

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
                UpdateStatsUI(); 
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

        MenuFlyoutItem MakeItem(string text)
        {
            bool active = _autoStart == text;
            var item = new MenuFlyoutItem { Style = (Style)Application.Current.Resources["GlassMenuFlyoutItemStyle"], Text = text, Foreground = active ? accentBrush : normalBrush };
            if (active) item.Icon = new FontIcon { Glyph = "\uE73E", FontSize = 12, Foreground = accentBrush };
            return item;
        }

        var onItem = MakeItem(App.GetString("Setting_Autostart_On"));
        var offItem = MakeItem(App.GetString("Setting_Autostart_Off"));

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
                App.ShowToast(App.GetString("Setting_Autostart_FailOn"));
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
                App.ShowToast(App.GetString("Setting_Autostart_FailOff"));
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

        MenuFlyoutItem MakeItem(string text)
        {
            bool active = _contextMenu == text;
            var item = new MenuFlyoutItem { Style = (Style)Application.Current.Resources["GlassMenuFlyoutItemStyle"], Text = text, Foreground = active ? accentBrush : normalBrush };
            if (active) item.Icon = new FontIcon { Glyph = "\uE73E", FontSize = 12, Foreground = accentBrush };
            return item;
        }

        var onItem = MakeItem(App.GetString("Setting_ContextMenu_On"));
        var offItem = MakeItem(App.GetString("Setting_ContextMenu_Off"));

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
                App.ShowToast(App.GetString("Setting_ContextMenu_FailOn"));
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
                App.ShowToast(App.GetString("Setting_ContextMenu_FailOff"));
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

    /* ========== SettingsPage Privacy Lock Dialogs ==========
Function: Set/change/disable password & warning dialogs: 6-digit validation, red-flash rejection, change-password transaction (C1)
Corresponding UI: SettingsPage.xaml.cs
Logic Range: Below methods in this region
*/
private void ShowSetPasswordDialog()
    {
        SetPasswordBox.Text = "";
        SetPasswordConfirmBox.Text = "";
        SetPasswordLockoutCheckBox.IsChecked = false;
        SetPasswordDialogTransform.ScaleX = 0.92;
        SetPasswordDialogTransform.ScaleY = 0.92;
        SetPasswordDialogTransform.TranslateY = 20;
        SetPasswordDialog.Opacity = 0;
        SetPasswordScrim.Opacity = 0;
        SetPasswordOverlay.Visibility = Visibility.Visible;
        var sb = new Storyboard();
        var si = new DoubleAnimation { To = 1, Duration = TimeSpan.FromMilliseconds(250) };
        Storyboard.SetTarget(si, SetPasswordScrim); Storyboard.SetTargetProperty(si, "Opacity"); sb.Children.Add(si);
        var di = new DoubleAnimation { To = 1, Duration = TimeSpan.FromMilliseconds(300) };
        Storyboard.SetTarget(di, SetPasswordDialog); Storyboard.SetTargetProperty(di, "Opacity"); sb.Children.Add(di);
        foreach (var (v, p) in new[] { (1.0, "ScaleX"), (1.0, "ScaleY"), (0.0, "TranslateY") })
        {
            var a = new DoubleAnimation { To = v, Duration = TimeSpan.FromMilliseconds(400), EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
            Storyboard.SetTarget(a, SetPasswordDialogTransform); Storyboard.SetTargetProperty(a, p); sb.Children.Add(a);
        }
        sb.Begin();
    }

    private void PrivacyLockButton_Click(object sender, RoutedEventArgs e)
    {
        ShowSetPasswordDialog();
    }

    private void HideSetPasswordDialog()
    {
        if (_animSetPwd) return; // D3 (Round 5): M2 hide re-entry guard
        _animSetPwd = true;
        var sb = new Storyboard();
        var so = new DoubleAnimation { To = 0, Duration = TimeSpan.FromMilliseconds(200) };
        Storyboard.SetTarget(so, SetPasswordScrim); Storyboard.SetTargetProperty(so, "Opacity"); sb.Children.Add(so);
        var d = new DoubleAnimation { To = 0, Duration = TimeSpan.FromMilliseconds(200) };
        Storyboard.SetTarget(d, SetPasswordDialog); Storyboard.SetTargetProperty(d, "Opacity"); sb.Children.Add(d);
        foreach (var (v, p) in new[] { (0.92, "ScaleX"), (0.92, "ScaleY"), (20.0, "TranslateY") })
        {
            var a = new DoubleAnimation { To = v, Duration = TimeSpan.FromMilliseconds(250), EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
            Storyboard.SetTarget(a, SetPasswordDialogTransform); Storyboard.SetTargetProperty(a, p); sb.Children.Add(a);
        }
        sb.Completed += (_, _) => { SetPasswordOverlay.Visibility = Visibility.Collapsed; _animSetPwd = false; };
        sb.Begin();
    }

    private void SetPasswordClose_Click(object sender, RoutedEventArgs e) => HideSetPasswordDialog();

    private void SetPasswordScrim_Tapped(object sender, TappedRoutedEventArgs e)
    {
        if (ReferenceEquals(e.OriginalSource, SetPasswordScrim)) HideSetPasswordDialog();
    }

    private void ChangePasswordButton_Click(object sender, RoutedEventArgs e)
    {
        ChangePasswordOldBox.Text = "";
        ChangePasswordNewBox.Text = "";
        ChangePasswordConfirmBox.Text = "";
        ChangePasswordDialogTransform.ScaleX = 0.92;
        ChangePasswordDialogTransform.ScaleY = 0.92;
        ChangePasswordDialogTransform.TranslateY = 20;
        ChangePasswordDialog.Opacity = 0;
        ChangePasswordScrim.Opacity = 0;
        ChangePasswordOverlay.Visibility = Visibility.Visible;
        var sb = new Storyboard();
        var si = new DoubleAnimation { To = 1, Duration = TimeSpan.FromMilliseconds(250) };
        Storyboard.SetTarget(si, ChangePasswordScrim); Storyboard.SetTargetProperty(si, "Opacity"); sb.Children.Add(si);
        var di = new DoubleAnimation { To = 1, Duration = TimeSpan.FromMilliseconds(300) };
        Storyboard.SetTarget(di, ChangePasswordDialog); Storyboard.SetTargetProperty(di, "Opacity"); sb.Children.Add(di);
        foreach (var (v, p) in new[] { (1.0, "ScaleX"), (1.0, "ScaleY"), (0.0, "TranslateY") })
        {
            var a = new DoubleAnimation { To = v, Duration = TimeSpan.FromMilliseconds(400), EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
            Storyboard.SetTarget(a, ChangePasswordDialogTransform); Storyboard.SetTargetProperty(a, p); sb.Children.Add(a);
        }
        sb.Begin();
    }

    private void HideChangePasswordDialog()
    {
        if (_animChangePwd) return; // D3 (Round 5): M2 hide re-entry guard
        _animChangePwd = true;
        var sb = new Storyboard();
        var so = new DoubleAnimation { To = 0, Duration = TimeSpan.FromMilliseconds(200) };
        Storyboard.SetTarget(so, ChangePasswordScrim); Storyboard.SetTargetProperty(so, "Opacity"); sb.Children.Add(so);
        var d = new DoubleAnimation { To = 0, Duration = TimeSpan.FromMilliseconds(200) };
        Storyboard.SetTarget(d, ChangePasswordDialog); Storyboard.SetTargetProperty(d, "Opacity"); sb.Children.Add(d);
        foreach (var (v, p) in new[] { (0.92, "ScaleX"), (0.92, "ScaleY"), (20.0, "TranslateY") })
        {
            var a = new DoubleAnimation { To = v, Duration = TimeSpan.FromMilliseconds(250), EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
            Storyboard.SetTarget(a, ChangePasswordDialogTransform); Storyboard.SetTargetProperty(a, p); sb.Children.Add(a);
        }
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

        // C1 (Round 5): change-password transaction - re-encrypt data.novadb first (security.dat keeps old), then switch security.dat; any failure keeps both files consistent
        if (!PasswordService.Verify(oldPw)) { FlashTextBox(ChangePasswordOldBox); return; }
        if (App.Store?.Reencrypt(newPw) == false) { FlashTextBox(ChangePasswordConfirmBox); return; } // #25: re-encrypt failed - security.dat untouched, password state consistent
        if (!PasswordService.ChangePassword(oldPw, newPw))
        {
            if (App.Store?.Reencrypt(oldPw) != true)
            {
                // E4-11: rollback itself failed (double disk failure) - data.novadb is encrypted with the
                // new password while security.dat still holds the old one. Warn and keep the dialog open so
                // the user does not leave the session and hit an undecryptable DB on restart.
                System.Diagnostics.Debug.WriteLine("改密回滚失败：数据库与密码文件不一致");
                FlashTextBox(ChangePasswordOldBox);
                FlashTextBox(ChangePasswordConfirmBox);
                return;
            }
            FlashTextBox(ChangePasswordConfirmBox); // C1: roll back data.novadb to old password, consistent with security.dat
            return;
        }
        if (WindowsHelloService.IsEnabled()) _ = System.Threading.Tasks.Task.Run(() => WindowsHelloService.Update(newPw)); // NH1+N2H-2: resync only when enabled; off UI thread (PasswordVault.Add blocks)
        HideChangePasswordDialog();
    }

    private void DisablePrivacyLockButton_Click(object sender, RoutedEventArgs e)
    {
        ClosePrivacyLockPasswordBox.Text = "";
        ClosePrivacyLockDialogTransform.ScaleX = 0.92;
        ClosePrivacyLockDialogTransform.ScaleY = 0.92;
        ClosePrivacyLockDialogTransform.TranslateY = 20;
        ClosePrivacyLockDialog.Opacity = 0;
        ClosePrivacyLockScrim.Opacity = 0;
        ClosePrivacyLockOverlay.Visibility = Visibility.Visible;
        var sb = new Storyboard();
        var si = new DoubleAnimation { To = 1, Duration = TimeSpan.FromMilliseconds(250) };
        Storyboard.SetTarget(si, ClosePrivacyLockScrim); Storyboard.SetTargetProperty(si, "Opacity"); sb.Children.Add(si);
        var di = new DoubleAnimation { To = 1, Duration = TimeSpan.FromMilliseconds(300) };
        Storyboard.SetTarget(di, ClosePrivacyLockDialog); Storyboard.SetTargetProperty(di, "Opacity"); sb.Children.Add(di);
        foreach (var (v, p) in new[] { (1.0, "ScaleX"), (1.0, "ScaleY"), (0.0, "TranslateY") })
        {
            var a = new DoubleAnimation { To = v, Duration = TimeSpan.FromMilliseconds(400), EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
            Storyboard.SetTarget(a, ClosePrivacyLockDialogTransform); Storyboard.SetTargetProperty(a, p); sb.Children.Add(a);
        }
        sb.Begin();
    }

    private void HideClosePrivacyLockDialog()
    {
        if (_animCloseLock) return; // D3 (Round 5): M2 hide re-entry guard
        _animCloseLock = true;
        var sb = new Storyboard();
        var so = new DoubleAnimation { To = 0, Duration = TimeSpan.FromMilliseconds(200) };
        Storyboard.SetTarget(so, ClosePrivacyLockScrim); Storyboard.SetTargetProperty(so, "Opacity"); sb.Children.Add(so);
        var d = new DoubleAnimation { To = 0, Duration = TimeSpan.FromMilliseconds(200) };
        Storyboard.SetTarget(d, ClosePrivacyLockDialog); Storyboard.SetTargetProperty(d, "Opacity"); sb.Children.Add(d);
        foreach (var (v, p) in new[] { (0.92, "ScaleX"), (0.92, "ScaleY"), (20.0, "TranslateY") })
        {
            var a = new DoubleAnimation { To = v, Duration = TimeSpan.FromMilliseconds(250), EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
            Storyboard.SetTarget(a, ClosePrivacyLockDialogTransform); Storyboard.SetTargetProperty(a, p); sb.Children.Add(a);
        }
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

    // ================================================================

    // ================================================================
    /* ========== SettingsPage Privacy Warning ==========
Function: High-risk warning dialog when disabling privacy lock; red three-state confirm button
Corresponding UI: SettingsPage.xaml.cs
Logic Range: Below methods in this region
*/
private void ShowPrivacyLockWarningDialog()
    {
        if (_animWarnShow) return; // E5-25: ignore re-entry while the show animation runs (double Begin = visual flicker)
        _animWarnShow = true;
        PrivacyLockWarningErrorText.Visibility = Visibility.Collapsed; // E4-27: reset the inline error each open
        PrivacyLockWarningDialogTransform.ScaleX = 0.92;
        PrivacyLockWarningDialogTransform.ScaleY = 0.92;
        PrivacyLockWarningDialogTransform.TranslateY = 20;
        PrivacyLockWarningDialog.Opacity = 0;
        PrivacyLockWarningScrim.Opacity = 0;
        PrivacyLockWarningOverlay.Visibility = Visibility.Visible;
        var sb = new Storyboard();
        var si = new DoubleAnimation { To = 1, Duration = TimeSpan.FromMilliseconds(250) };
        Storyboard.SetTarget(si, PrivacyLockWarningScrim); Storyboard.SetTargetProperty(si, "Opacity"); sb.Children.Add(si);
        var di = new DoubleAnimation { To = 1, Duration = TimeSpan.FromMilliseconds(300) };
        Storyboard.SetTarget(di, PrivacyLockWarningDialog); Storyboard.SetTargetProperty(di, "Opacity"); sb.Children.Add(di);
        foreach (var (v, p) in new[] { (1.0, "ScaleX"), (1.0, "ScaleY"), (0.0, "TranslateY") })
        {
            var a = new DoubleAnimation { To = v, Duration = TimeSpan.FromMilliseconds(400), EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
            Storyboard.SetTarget(a, PrivacyLockWarningDialogTransform); Storyboard.SetTargetProperty(a, p); sb.Children.Add(a);
        }
        sb.Completed += (_, _) => _animWarnShow = false; // E5-25: re-arm when the show animation finished (dialog can be reopened after closing)
        sb.Begin();
    }

    private void HidePrivacyLockWarningDialog()
    {
        if (_animWarn) return; // D3 (Round 5): M2 hide re-entry guard
        _animWarn = true;
        var sb = new Storyboard();
        var so = new DoubleAnimation { To = 0, Duration = TimeSpan.FromMilliseconds(200) };
        Storyboard.SetTarget(so, PrivacyLockWarningScrim); Storyboard.SetTargetProperty(so, "Opacity"); sb.Children.Add(so);
        var d = new DoubleAnimation { To = 0, Duration = TimeSpan.FromMilliseconds(200) };
        Storyboard.SetTarget(d, PrivacyLockWarningDialog); Storyboard.SetTargetProperty(d, "Opacity"); sb.Children.Add(d);
        foreach (var (v, p) in new[] { (0.92, "ScaleX"), (0.92, "ScaleY"), (20.0, "TranslateY") })
        {
            var a = new DoubleAnimation { To = v, Duration = TimeSpan.FromMilliseconds(250), EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
            Storyboard.SetTarget(a, PrivacyLockWarningDialogTransform); Storyboard.SetTargetProperty(a, p); sb.Children.Add(a);
        }
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
            // E4-27: surface the failure inline - the store already rolled back the encryption flag,
            // but the user must know the disable-lock action did not take effect.
            PrivacyLockWarningErrorText.Visibility = Visibility.Visible;
            return;
        }
        PasswordService.Delete();
        PasswordService.DeleteLockout(); // E1-20: full reset incl. FailCount - a previously locked-out user must not be locked again after 1 wrong try
        WindowsHelloService.Disable(); // 5.0: the password no longer exists - drop the Hello credential
        PersistSetting(s => s.PrivacyLockEnabled = false);
        _isPrivacyLockEnabled = false;
        UpdatePrivacyLockUI();
        HidePrivacyLockWarningDialog();
    }

    public void UpdatePrivacyLockUI() // public: MainWindow refreshes it right after a GCM migration
    {
        PrivacyLockButton.Visibility = _isPrivacyLockEnabled ? Visibility.Collapsed : Visibility.Visible;
        PrivacyLockEnabledPanel.Visibility = _isPrivacyLockEnabled ? Visibility.Visible : Visibility.Collapsed;
        UpdateAutoLockCard();
        // 4.7: manual GCM-migration entry - visible only while a v1-CBC database is still un-migrated.
        MigrateFormatEntryButton.Visibility = App.Store is { IsEncrypted: true, NeedsFormatMigration: true }
            ? Visibility.Visible : Visibility.Collapsed;
        // 5.0: reflect the real PasswordVault state (it survives restart; _winHelloEnabled is only in-memory)
        _winHelloEnabled = WindowsHelloService.IsEnabled();
        ApplyToggleState(WindowsHelloButton, _winHelloEnabled);
    }

    private void MigrateFormatEntryButton_Click(object sender, RoutedEventArgs e)
        => App.MainWindow?.ShowMigrateFormatDialog(); // 4.7: manual upgrade re-opens the same confirmation

    private void CloseBehaviorButton_Click(object sender, RoutedEventArgs e)
    {
        var menu = new MenuFlyout { MenuFlyoutPresenterStyle = (Style)Application.Current.Resources["GlassMenuFlyoutPresenterStyle"] };
        var accentBrush = App.GetBrush("AppTextPrimaryBrush");
        var normalBrush = App.GetBrush("AppTextSecondaryBrush");

        MenuFlyoutItem MakeItem(string text)
        {
            bool active = _closeBehavior == text;
            var item = new MenuFlyoutItem { Style = (Style)Application.Current.Resources["GlassMenuFlyoutItemStyle"], Text = text, Foreground = active ? accentBrush : normalBrush };
            if (active) item.Icon = new FontIcon { Glyph = "\uE73E", FontSize = 12, Foreground = accentBrush };
            return item;
        }

        var trayItem = MakeItem(App.GetString("Setting_ExitBehavior_Tray"));
        var exitItem = MakeItem(App.GetString("Setting_ExitBehavior_Exit"));

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

        zhItem.Click += (_, _) => SelectLanguage(""); // follow system
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
        if (LanguageRestartOverlay.Visibility != Visibility.Visible) return;
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

    private void LanguageRestartConfirm_Click(object sender, RoutedEventArgs e)
    {
        if (_restarting) return; // #33
        _restarting = true;
        var oldLanguage = _currentLanguage; // E5-27: keep the previous value - roll back UI + in-memory setting if the save fails (E4-26)
        _currentLanguage = _pendingLanguage;
        _pendingLanguage = string.Empty;
        UpdateLanguageButton();
        HideLanguageRestartOverlay();
        // Sync write: the app restarts immediately and async SaveAsync may not flush AppLanguage in time.
        if (App.Store is { } ls)
        {
            ls.Database.AppSettings.AppLanguage = _currentLanguage;
            if (!ls.SaveSync())
            {
                _restarting = false;
                _currentLanguage = oldLanguage;               // E5-27: no partial state - the button must not show a language that never persisted
                ls.Database.AppSettings.AppLanguage = oldLanguage;
                UpdateLanguageButton();
                return; // #34: abort restart on save failure
            }
        }
        Services.StickySync.UpdateLanguage(string.IsNullOrEmpty(_currentLanguage) ? App.ResolveSystemLanguage() : _currentLanguage); // i18n: host menus follow the effective language (follow-system resolves to the OS language)
        App.SaveLanguageHint(_currentLanguage);

        try
        {
            var exe = Environment.ProcessPath;
            if (string.IsNullOrEmpty(exe)) { _restarting = false; return; }
            Novara.MainWindow.ReleaseSingleInstance();
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo { FileName = exe, UseShellExecute = true });
        }
        catch
        {
            System.Diagnostics.Debug.WriteLine("Novara 重启闭环：新进程启动失败，保持当前会话");
            _restarting = false;
            return;
        }
        if (App.MainWindow is { } mw) { mw.SetRestarting(); mw.ExitApp(saveFirst: false); } // E5-04: mark the restart on MainWindow so Closing skips the redundant second save; E4-09: data already saved above - ExitApp must not abort on a redundant save failure (mutex is already released, new process is running)
    }

    private void LanguageRestartCancel_Click(object sender, RoutedEventArgs e)
    {
        _pendingLanguage = string.Empty;
        HideLanguageRestartOverlay();
    }

    private void LanguageRestartClose_Click(object sender, RoutedEventArgs e)
    {
        _pendingLanguage = string.Empty;
        HideLanguageRestartOverlay();
    }

    private void LanguageRestartScrim_Tapped(object sender, TappedRoutedEventArgs e)
    {
        _pendingLanguage = string.Empty;
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
            ? ("" + _autoLockSeconds + "s").Replace("60s", "1min").Replace("300s", "5min").Replace("600s", "10min").Replace("1800s", "30min").Replace("3600s", "60min")
            : App.GetString("Setting_AutoLock_Never");
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
            (App.GetString("Setting_AutoLock_Never"), 0), ("30s", 30), ("1min", 60), ("5min", 300), ("10min", 600), ("30min", 1800), ("60min", 3600)
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

    
    private void ShowAutoLockSyncConfirmDialog() => ShowOverlay(AutoLockSyncConfirmOverlay, AutoLockSyncConfirmDialog, AutoLockSyncConfirmDialogTransform);
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

    private void ApplyToggleState(Button btn, bool on)
    {
        btn.Style = on ? (Style)Application.Current.Resources["NovaraPrimaryButtonStyle"]
                       : (Style)Resources["SettingsDropdownButtonStyle"];
        btn.Background = on ? App.GetBrush("AppPrimaryButtonBrush")
                            : App.GetBrush("AppSurfaceBrush");
        btn.Foreground = on ? new SolidColorBrush(Color.FromArgb(255, 255, 255, 255))
                            : App.GetBrush("AppTextSecondaryBrush");
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
            _ = StartWinHelloEnableAsync();
            return;
        }
        WindowsHelloService.Disable();
        _winHelloEnabled = false;
        ApplyToggleState(WindowsHelloButton, false);
    }

    /* ========== Windows Hello Enable Flow ==========
    Function: auto-start one Hello verification after the dialog opens; on success store the current
    password into PasswordVault and turn the button on; on failure keep the dialog open (retry /
    open Windows Settings / cancel).
    */
    private async Task StartWinHelloEnableAsync()
    {
        if (_winHelloEnabling) return; // NH13: toggle double-click used to stack parallel verification flows
        _winHelloEnabling = true;
        try
        {
        if (!await WindowsHelloService.IsAvailableAsync())
        {
            // No Windows Hello on this PC -> dialog stays; the "open Windows Settings" button is the exit.
            return;
        }

        var result = await WindowsHelloService.RequestVerificationAsync(App.GetString("Lock_WinHello_VerifyMsg"));
        if (result != UserConsentVerificationResult.Verified)
        {
            // Failed/cancelled -> dialog stays open.
            return;
        }

        var pw = App.Store?.Password;
        // NH6: the vault's FIRST entry creation for this app is known to block for tens of seconds -
        // run it off the UI thread so the dialog stays responsive instead of freezing the window.
        bool enabled = false;
        if (!string.IsNullOrEmpty(pw))
            enabled = await System.Threading.Tasks.Task.Run(() => WindowsHelloService.Enable(pw!));
        if (!enabled)
        {
            // Vault write failed -> dialog stays open.
            return;
        }

        _winHelloEnabled = true;
        ApplyToggleState(WindowsHelloButton, true);
        HideWindowsHelloDialog();
        }
        finally { _winHelloEnabling = false; }
    }

    private bool _animWinHello;
    private bool _winHelloEnabling; // NH13: enable-flow re-entry guard

    private void ShowWindowsHelloDialog()
    {
        WindowsHelloDialogTransform.ScaleX = 0.92;
        WindowsHelloDialogTransform.ScaleY = 0.92;
        WindowsHelloDialogTransform.TranslateY = 20;
        WindowsHelloDialog.Opacity = 0;
        WindowsHelloScrim.Opacity = 0;
        WindowsHelloOverlay.Visibility = Visibility.Visible;
        var sb = new Storyboard();
        var si = new DoubleAnimation { To = 1, Duration = TimeSpan.FromMilliseconds(250) };
        Storyboard.SetTarget(si, WindowsHelloScrim); Storyboard.SetTargetProperty(si, "Opacity"); sb.Children.Add(si);
        var di = new DoubleAnimation { To = 1, Duration = TimeSpan.FromMilliseconds(300) };
        Storyboard.SetTarget(di, WindowsHelloDialog); Storyboard.SetTargetProperty(di, "Opacity"); sb.Children.Add(di);
        foreach (var (v, p) in new[] { (1.0, "ScaleX"), (1.0, "ScaleY"), (0.0, "TranslateY") })
        {
            var a = new DoubleAnimation { To = v, Duration = TimeSpan.FromMilliseconds(400), EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
            Storyboard.SetTarget(a, WindowsHelloDialogTransform); Storyboard.SetTargetProperty(a, p); sb.Children.Add(a);
        }
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
        foreach (var (v, p) in new[] { (0.92, "ScaleX"), (0.92, "ScaleY"), (20.0, "TranslateY") })
        {
            var a = new DoubleAnimation { To = v, Duration = TimeSpan.FromMilliseconds(250), EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
            Storyboard.SetTarget(a, WindowsHelloDialogTransform); Storyboard.SetTargetProperty(a, p); sb.Children.Add(a);
        }
        sb.Completed += (_, _) =>
        {
            WindowsHelloOverlay.Visibility = Visibility.Collapsed;
            _animWinHello = false;
            // Closing the dialog without a successful enable keeps the button in its off state.
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

    private static void PersistSetting(Action<Novara.Models.AppSettings> mutate)
    {
        var store = App.Store;
        if (store == null) return;
        mutate(store.Database.AppSettings);
        store.SaveAsync();
    }

    private void ResetDataButton_Click(object sender, RoutedEventArgs e)
    {
        ResetConfirmDialogTransform.ScaleX = 0.92;
        ResetConfirmDialogTransform.ScaleY = 0.92;
        ResetConfirmDialogTransform.TranslateY = 20;
        ResetConfirmDialog.Opacity = 0;
        ResetConfirmScrim.Opacity = 0;
        ResetConfirmOverlay.Visibility = Visibility.Visible;
        var sb = new Storyboard();
        var si = new DoubleAnimation { To = 1, Duration = TimeSpan.FromMilliseconds(250) };
        Storyboard.SetTarget(si, ResetConfirmScrim); Storyboard.SetTargetProperty(si, "Opacity"); sb.Children.Add(si);
        var di = new DoubleAnimation { To = 1, Duration = TimeSpan.FromMilliseconds(300) };
        Storyboard.SetTarget(di, ResetConfirmDialog); Storyboard.SetTargetProperty(di, "Opacity"); sb.Children.Add(di);
        foreach (var (v, p) in new[] { (1.0, "ScaleX"), (1.0, "ScaleY"), (0.0, "TranslateY") })
        {
            var a = new DoubleAnimation { To = v, Duration = TimeSpan.FromMilliseconds(400), EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
            Storyboard.SetTarget(a, ResetConfirmDialogTransform); Storyboard.SetTargetProperty(a, p); sb.Children.Add(a);
        }
        sb.Begin();
    }

    private void WebsiteButton_Click(object sender, RoutedEventArgs e)
    {
        // 4.0: open the official Novara website in the default browser.
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("https://novara.xin") { UseShellExecute = true }); }
        catch { }
    }

    private void HideResetConfirmDialog()
    {
        if (_animResetConfirm) return; // D3 (Round 5): M2 hide re-entry guard
        _animResetConfirm = true;
        var sb = new Storyboard();
        var so = new DoubleAnimation { To = 0, Duration = TimeSpan.FromMilliseconds(200) };
        Storyboard.SetTarget(so, ResetConfirmScrim); Storyboard.SetTargetProperty(so, "Opacity"); sb.Children.Add(so);
        var d = new DoubleAnimation { To = 0, Duration = TimeSpan.FromMilliseconds(200) };
        Storyboard.SetTarget(d, ResetConfirmDialog); Storyboard.SetTargetProperty(d, "Opacity"); sb.Children.Add(d);
        foreach (var (v, p) in new[] { (0.92, "ScaleX"), (0.92, "ScaleY"), (20.0, "TranslateY") })
        {
            var a = new DoubleAnimation { To = v, Duration = TimeSpan.FromMilliseconds(250), EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
            Storyboard.SetTarget(a, ResetConfirmDialogTransform); Storyboard.SetTargetProperty(a, p); sb.Children.Add(a);
        }
        sb.Completed += (_, _) => { ResetConfirmOverlay.Visibility = Visibility.Collapsed; _animResetConfirm = false; };
        sb.Begin();
    }

    private void ResetConfirmClose_Click(object sender, RoutedEventArgs e) => HideResetConfirmDialog();
    private void ResetCancel_Click(object sender, RoutedEventArgs e) => HideResetConfirmDialog();

    private void ResetConfirmScrim_Tapped(object sender, TappedRoutedEventArgs e)
    {
        if (ReferenceEquals(e.OriginalSource, ResetConfirmScrim)) HideResetConfirmDialog();
    }

    private bool _resetConfirmBusy; // N5T1-04: hide-animation double-click ran PerformReset twice
    private bool _pickerFlowBusy; // N5T1-05: one FileSavePicker flow at a time - a second concurrent picker throws COM

    private void ResetConfirmButton_Click(object sender, RoutedEventArgs e)
    {
        if (_resetConfirmBusy) return;
        _resetConfirmBusy = true;

        HideResetConfirmDialog();
        if (App.Store is { IsEncrypted: true })
            ShowResetPasswordDialog(); // password path re-arms via its own cancel/failure flow
        else
            PerformReset();
        _resetConfirmBusy = false;
    }

    private void PerformReset()
    {
        var resetResult = App.Store?.ResetDatabase();
        if (resetResult == null || resetResult.Status != LoadStatus.EmptyCreated)
        {
            // E4-07: reset failed (file locked / permission) - keep security.dat + lockout.dat so the
            // user is not locked out of the still-encrypted DB on restart; data is untouched.
            ShowImportResult(App.GetString("Setting_Reset_FailTitle"), App.GetString("Setting_Reset_FailDesc"));
            return;
        }
        PasswordService.Delete();
        PasswordService.DeleteLockout();
        WindowsHelloService.Disable(); // 5.0: reset clears the DB - drop the Hello credential
        Services.StickySync.ClearAllNotes(); // E3-07: host data is local-only - reset clears the desktop notes too (host closes all notes and exits)
        _isPrivacyLockEnabled = false;
        UpdatePrivacyLockUI();
        App.ShowToast(App.GetString("Common_Toast_Reset"));
        App.MainWindow?.ReloadPages();
    }

    /* ========== SettingsPage Database Reset ==========
Function: Reset flow: password verification (when locked), confirmation dialog, rebuild blank plaintext DB
Corresponding UI: SettingsPage.xaml.cs
Logic Range: Below methods in this region
*/
private void ShowResetPasswordDialog()
    {
        ResetPasswordBox.Text = "";
        ResetPasswordDialogTransform.ScaleX = 0.92;
        ResetPasswordDialogTransform.ScaleY = 0.92;
        ResetPasswordDialogTransform.TranslateY = 20;
        ResetPasswordDialog.Opacity = 0;
        ResetPasswordScrim.Opacity = 0;
        ResetPasswordOverlay.Visibility = Visibility.Visible;
        var sb = new Storyboard();
        var si = new DoubleAnimation { To = 1, Duration = TimeSpan.FromMilliseconds(250) };
        Storyboard.SetTarget(si, ResetPasswordScrim); Storyboard.SetTargetProperty(si, "Opacity"); sb.Children.Add(si);
        var di = new DoubleAnimation { To = 1, Duration = TimeSpan.FromMilliseconds(300) };
        Storyboard.SetTarget(di, ResetPasswordDialog); Storyboard.SetTargetProperty(di, "Opacity"); sb.Children.Add(di);
        foreach (var (v, p) in new[] { (1.0, "ScaleX"), (1.0, "ScaleY"), (0.0, "TranslateY") })
        {
            var a = new DoubleAnimation { To = v, Duration = TimeSpan.FromMilliseconds(400), EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
            Storyboard.SetTarget(a, ResetPasswordDialogTransform); Storyboard.SetTargetProperty(a, p); sb.Children.Add(a);
        }
        sb.Begin();
    }

    private void HideResetPasswordDialog()
    {
        if (_animResetPwd) return; // D3 (Round 5): M2 hide re-entry guard
        _animResetPwd = true;
        var sb = new Storyboard();
        var so = new DoubleAnimation { To = 0, Duration = TimeSpan.FromMilliseconds(200) };
        Storyboard.SetTarget(so, ResetPasswordScrim); Storyboard.SetTargetProperty(so, "Opacity"); sb.Children.Add(so);
        var d = new DoubleAnimation { To = 0, Duration = TimeSpan.FromMilliseconds(200) };
        Storyboard.SetTarget(d, ResetPasswordDialog); Storyboard.SetTargetProperty(d, "Opacity"); sb.Children.Add(d);
        foreach (var (v, p) in new[] { (0.92, "ScaleX"), (0.92, "ScaleY"), (20.0, "TranslateY") })
        {
            var a = new DoubleAnimation { To = v, Duration = TimeSpan.FromMilliseconds(250), EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
            Storyboard.SetTarget(a, ResetPasswordDialogTransform); Storyboard.SetTargetProperty(a, p); sb.Children.Add(a);
        }
        sb.Completed += (_, _) => { ResetPasswordOverlay.Visibility = Visibility.Collapsed; _animResetPwd = false; };
        sb.Begin();
    }

    private void ResetPasswordClose_Click(object sender, RoutedEventArgs e) => HideResetPasswordDialog();

    private void ResetPasswordScrim_Tapped(object sender, TappedRoutedEventArgs e)
    {
        if (ReferenceEquals(e.OriginalSource, ResetPasswordScrim)) HideResetPasswordDialog();
    }

    private void ResetPasswordConfirm_Click(object sender, RoutedEventArgs e)
    {
        var pw = ResetPasswordBox.Text.Trim();
        if (pw.Length < 6) { FlashTextBox(ResetPasswordBox); return; }
        if (!PasswordService.Verify(pw)) { FlashTextBox(ResetPasswordBox); return; }
        HideResetPasswordDialog();
        PerformReset();
    }

    private void SetPasswordConfirm_Click(object sender, RoutedEventArgs e)
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

        if (!PasswordService.Create(pw)) { FlashTextBox(SetPasswordBox); return; }
        PasswordService.SetLockoutEnabled(SetPasswordLockoutCheckBox.IsChecked == true);
        if (App.Store?.EnableEncryption(pw) != true)
        {
            // E4-28: surface the failure - the store already rolled back (flag/password restored),
            // but the user must not be silently dropped out of the set-password flow.
            PasswordService.Delete();
            PasswordService.DeleteLockout(); // E1-20: full reset incl. FailCount (see above)
            FlashTextBox(SetPasswordBox);
            FlashTextBox(SetPasswordConfirmBox);
            return;
        }
        PersistSetting(s => s.PrivacyLockEnabled = true);
        _isPrivacyLockEnabled = true;
        UpdatePrivacyLockUI();
        HideSetPasswordDialog();
    }

    private readonly Dictionary<TextBox, System.Threading.CancellationTokenSource> _flashCtsMap = new(); // E5-24: per-box CTS - a shared CTS made a second flash cancel the first (only one box blinked)
    private bool _animSetPwd; // D3 (Round 5): M2 HideSetPasswordDialog hide animation re-entry guard
    private bool _animChangePwd; // D3 (Round 5): M2 HideChangePasswordDialog hide animation re-entry guard
    private bool _animCloseLock; // D3 (Round 5): M2 HideClosePrivacyLockDialog hide animation re-entry guard
    private bool _animWarn; // D3 (Round 5): M2 HidePrivacyLockWarningDialog hide animation re-entry guard
    private bool _animWarnShow; // E5-25: ShowPrivacyLockWarningDialog show animation re-entry guard (rapid confirm clicks re-show the dialog over itself)
    private bool _animResetConfirm; // D3 (Round 5): M2 HideResetConfirmDialog hide animation re-entry guard
    private bool _animResetPwd; // D3 (Round 5): M2 HideResetPasswordDialog hide animation re-entry guard
    private bool _animImportPwd; // D3 (Round 5): M2 HideImportExportPasswordDialog hide animation re-entry guard
    private bool _animExportOptions;
    private bool _animImportConfirm; // D3 (Round 5): M2 HideImportConfirmDialog hide animation re-entry guard
    private bool _animImportResult; // D3 (Round 5): M2 HideImportResult hide animation re-entry guard
    private bool _animExportMdNotice;
    private bool _animCsvPreview;
    private bool _animCsvExportNotice;

    
    private bool _backupEnabled;               
    private bool _autoBackupEnabled;            
    private int _backupFreqIndex;              
    private bool _pendingDeleteIsClearAll;     
    private string? _pendingRestoreSnapshot;   
    private readonly List<string> _pendingDeleteSnapshots = new(); 
    private bool _animBackupIntro, _animBackupNow, _animBackupAuto, _animBackupRestore, _animBackupDelete;
    private bool _animBackupRestoreConfirm, _animBackupDeleteConfirm;
    private bool _animMcpConfig, _animMcpResetConfirm, _animMcpCopy, _animMcpDeleteConfirm;
    private bool _animMcpRevokeConfirm;
    private bool _animMcpAudit, _animMcpAuditClear;
    private string? _pendingRevokePath;

    private readonly Dictionary<TextBox, Microsoft.UI.Xaml.Media.Brush> _flashOriginalBgs = new();

    private async void FlashTextBox(TextBox tb)
    {
        if (_flashCtsMap.TryGetValue(tb, out var prev)) { prev.Cancel(); prev.Dispose(); }
        var cts = new System.Threading.CancellationTokenSource();
        _flashCtsMap[tb] = cts;

        var orig = _flashOriginalBgs.TryGetValue(tb, out var existing) ? existing : tb.Background;
        _flashOriginalBgs[tb] = orig;
        tb.Background = new SolidColorBrush(Color.FromArgb(0x33, 0xFF, 0x45, 0x45));
        try { await System.Threading.Tasks.Task.Delay(600, cts.Token); }
        catch (System.Threading.Tasks.TaskCanceledException)
        {
            // N5F-01: identity check - a superseded cancel must not wipe the newer flash.
            if (_flashCtsMap.TryGetValue(tb, out var cur) && !ReferenceEquals(cur, cts)) return;
            tb.Background = orig;
            _flashOriginalBgs.Remove(tb);
            _flashCtsMap.Remove(tb);
            return;
        }
        tb.Background = orig;
        _flashOriginalBgs.Remove(tb);
        _flashCtsMap.Remove(tb);
    }


    private static string ThemeLabel(string theme) => theme switch
    {
        "深色模式" => App.GetString("Setting_Theme_Dark"),
        "浅色模式" => App.GetString("Setting_Theme_Light"),
        "跟随系统" => App.GetString("Setting_Theme_System"),
        _ => theme
    };

    private void UpdateThemeButton()
    {
        ((TextBlock)ThemeButton.Content).Text = ThemeLabel(_currentTheme);
    }

    // ================================================================

    // ================================================================

    private bool _pendingExport;
    private Action? _pendingVerifiedAction; // N4T-07(A): session-convenience flow waiting behind the password gate
    private bool _pendingReloadAfterImport;

    private void ImportExportButton_Click(object sender, RoutedEventArgs e)
    {
        var menu = new MenuFlyout { MenuFlyoutPresenterStyle = (Style)Application.Current.Resources["GlassMenuFlyoutPresenterStyle"] };
        var normalBrush = App.GetBrush("AppTextSecondaryBrush");

        MenuFlyoutItem MakeItem(string text)
        {
            return new MenuFlyoutItem { Style = (Style)Application.Current.Resources["GlassMenuFlyoutItemStyle"], Text = text, Foreground = normalBrush };
        }

        
        var importSub = new MenuFlyoutSubItem
        {
            Text = App.GetString("Setting_Archive_Import"),
            Foreground = normalBrush,
            Style = (Style)Application.Current.Resources["GlassMenuFlyoutSubItemStyle"],
        };
        var nativeImportItem = MakeItem(App.GetString("Setting_Archive_ExportNative"));
        var csvImportItem = MakeItem(App.GetString("Setting_CsvImport"));
        nativeImportItem.Click += (_, _) => ShowImportConfirmDialog();
        csvImportItem.Click += (_, _) => RunPrivacyGated(() => _ = PickAndShowCsvImportAsync()); // N4T-07(A)
        importSub.Items.Add(nativeImportItem);
        importSub.Items.Add(csvImportItem);

        
        var exportSub = new MenuFlyoutSubItem
        {
            Text = App.GetString("Setting_Archive_Export"),
            Foreground = normalBrush,
            Style = (Style)Application.Current.Resources["GlassMenuFlyoutSubItemStyle"],
        };
        var mdItem = MakeItem(App.GetString("Setting_Archive_ExportMd"));
        var nativeExportItem = MakeItem(App.GetString("Setting_Archive_ExportNative"));
        var csvExportItem = MakeItem(App.GetString("Setting_CsvExport"));
        var htmlExportItem = MakeItem(App.GetString("Setting_ExportHtml"));
        var pdfExportItem = MakeItem(App.GetString("Setting_ExportPdf"));
        mdItem.Click += (_, _) => RunPrivacyGated(ShowExportMdNoticeDialog); // N4T-07(A)
        nativeExportItem.Click += (_, _) => ExportNativeFlow();
        csvExportItem.Click += (_, _) => RunPrivacyGated(ShowCsvExportNoticeDialog); // N4T-07(A)
        htmlExportItem.Click += (_, _) => RunPrivacyGated(() => _ = ExportHtmlFlowAsync()); // N4T-07(A)
        pdfExportItem.Click += (_, _) => RunPrivacyGated(() => _ = ExportPdfFlowAsync()); // N4T-07(A)
        exportSub.Items.Add(mdItem);
        exportSub.Items.Add(nativeExportItem);
        exportSub.Items.Add(csvExportItem);
        exportSub.Items.Add(htmlExportItem);
        exportSub.Items.Add(pdfExportItem);

        menu.Items.Add(importSub);
        menu.Items.Add(exportSub);
        menu.ShowAt(ImportExportButton, new Windows.Foundation.Point(0, ImportExportButton.ActualHeight + 4));
    }

    
    private void ExportNativeFlow()
    {
        if (App.Store is { IsEncrypted: true })
        {
            _pendingExport = true;
            ShowImportExportPasswordDialog(App.GetString("Setting_Export_VerifyTitle"));
        }
        else
        {
            ShowExportOptionsDialog();
        }
    }

    private async void ImportConfirmContinue_Click(object sender, RoutedEventArgs e)
    {
        if (_animImportConfirm) return; // E4-06: block double-click during the hide animation (two concurrent FileOpenPickers can throw a COM exception in async void)
        HideImportConfirmDialog();
        try
        {
            if (App.Store is { IsEncrypted: true })
            {
                _pendingExport = false;
                ShowImportExportPasswordDialog(App.GetString("Setting_Import_VerifyTitle"));
                return;
            }
            var path = await PickImportFileAsync();
            if (path == null) return;
            ImportBackupFlow(path);
        }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"导入失败: {ex}"); }
    }

    private async System.Threading.Tasks.Task<string?> PickImportFileAsync()
    {
        var picker = new FileOpenPicker();
        InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(App.MainWindow));
        picker.FileTypeFilter.Add(".novabak");
        var file = await picker.PickSingleFileAsync();
        return file?.Path;
    }

    private async System.Threading.Tasks.Task ImportAfterPasswordAsync(string? prePickedPath)
    {
        try // E5-26: fire-and-forget from ImportExportPasswordConfirm_Click - an exception must not escape as an unobserved task fault
        {
            var path = prePickedPath;
            if (string.IsNullOrEmpty(path))
            {
                path = await PickImportFileAsync();
                if (path == null) return;
            }
            ImportBackupFlow(path);
        }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"导入失败: {ex}"); }
    }

    private async System.Threading.Tasks.Task ExportBackupFlowAsync(bool includePaths)
    {
        try // E5-26: fire-and-forget from ExportOptionsConfirm_Click - an exception must not escape as an unobserved task fault
        {
            var picker = new FileSavePicker();
            InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(App.MainWindow));
            picker.FileTypeChoices.Add(App.GetString("Setting_Backup_FilePrefix"), new List<string> { ".novabak" });
            picker.SuggestedFileName = string.Format("{0}_{1:yyyyMMdd_HHmmss}", App.GetString("Setting_Backup_FilePrefix"), DateTime.Now);
            var file = await picker.PickSaveFileAsync();
            if (file == null) return;

            bool ok = App.Store?.ExportBackup(file.Path, includePaths) ?? false;
            ShowImportResult(ok ? App.GetString("Setting_Export_Success") : App.GetString("Setting_Export_Fail"),
                ok ? App.GetString("Setting_Export_SuccessDesc") : App.GetString("Setting_Export_FailDesc"));
        }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"导出失败: {ex}"); }
    }

    private void ImportBackupFlow(string path)
    {
        var result = App.Store?.ImportBackup(path);
        if (result == null || result.Status != LoadStatus.Ok)
        {
            ShowImportResult(App.GetString("Setting_Import_Fail"), result?.Detail ?? App.GetString("Setting_Import_FailDesc"));
            return;
        }

        _pendingReloadAfterImport = true;
        WindowsHelloService.Disable(); // 5.0: import replaces the DB - drop the credential (it may point at the old password)
        Services.StickySync.ClearAllNotes(); // E3-07: host data is local-only - a full import replaces the db, so the desktop notes of the previous data are cleared (re-send to desktop on the new machine)

        var imported = App.Store?.Database.AppSettings;
        if (imported != null)
        {
            if (App.MainWindow is { } mw)
                mw.CloseBehavior = string.IsNullOrEmpty(imported.CloseBehavior) ? "直接退出" : imported.CloseBehavior;
            if (imported.AutoStart) StartupService.Enable(); else StartupService.Disable();
            if (imported.VisibleTabs is { Count: > 0 } tabs)
                App.MainWindow?.ApplyVisibleTabs(tabs.ToHashSet());
            // E4-29: sync the imported global right-click menu setting to the registry (the settings
            // toggle echoes the real registry state via IsRegistered()).
            if (imported.ContextMenu) Services.ContextMenuService.RegisterAll();
            else Services.ContextMenuService.UnregisterAll();
            // E5-10: echo the imported values into the page fields/toggles - the registry was synced
            // above but the toggle text still showed the pre-import state until re-entering the page.
            _autoStart = imported.AutoStart ? "开启" : "关闭";
            _contextMenu = imported.ContextMenu ? "开启" : "关闭";
            UpdateAutoStartButton();
            UpdateContextMenuButton();
        }
        var theme = App.Store?.Database.AppSettings.Theme;
        var lang = App.Store?.Database.AppSettings.AppLanguage; // #37: sync CurrentLanguage on import
        // E4-32: capture the change BEFORE ApplyLanguage mutates App.CurrentLanguage - the old check
        // (lang != App.CurrentLanguage) ran after ApplyLanguage, so it was always false and a
        // language-only change never prompted the restart (theme still worked because SetTheme is not
        // applied during import).
        bool langChanged = !string.IsNullOrEmpty(lang) && lang != App.CurrentLanguage;
        if (!string.IsNullOrEmpty(lang))
        {
            if (langChanged) App.ApplyLanguage(lang);
            App.SaveLanguageHint(lang); // D8-4 (Round 5): sync language.dat (source for encrypted lock screen)
        }
        if ((!string.IsNullOrEmpty(theme) && theme != App.CurrentTheme) || langChanged)
        {
            ShowImportResult(App.GetString("Setting_Import_Done"), App.GetString("Setting_Import_Done_ThemeDesc")); // theme/language change prompts restart
        }
        else
        {
            ShowImportResult(App.GetString("Setting_Import_Done"), App.GetString("Setting_Import_Done_Desc"));
        }
    }

    /* ========== SettingsPage Import/Export ==========
Function: Backup export (always plaintext, password verify when locked) & import (re-encrypt, language/theme sync, result dialogs)
Corresponding UI: SettingsPage.xaml.cs
Logic Range: Below methods in this region
*/
    private void ShowImportExportPasswordDialog(string title)
    {
        _pendingVerifiedAction = null; // N5T1-02: a fresh dialog always starts clean - stale gated flows die here too
        ImportExportPasswordTitle.Text = title;
        ImportExportPasswordBox.Text = "";
        ImportExportPasswordDialogTransform.ScaleX = 0.92;
        ImportExportPasswordDialogTransform.ScaleY = 0.92;
        ImportExportPasswordDialogTransform.TranslateY = 20;
        ImportExportPasswordDialog.Opacity = 0;
        ImportExportPasswordScrim.Opacity = 0;
        ImportExportPasswordOverlay.Visibility = Visibility.Visible;
        var sb = new Storyboard();
        var si = new DoubleAnimation { To = 1, Duration = TimeSpan.FromMilliseconds(250) };
        Storyboard.SetTarget(si, ImportExportPasswordScrim); Storyboard.SetTargetProperty(si, "Opacity"); sb.Children.Add(si);
        var di = new DoubleAnimation { To = 1, Duration = TimeSpan.FromMilliseconds(300) };
        Storyboard.SetTarget(di, ImportExportPasswordDialog); Storyboard.SetTargetProperty(di, "Opacity"); sb.Children.Add(di);
        foreach (var (v, p) in new[] { (1.0, "ScaleX"), (1.0, "ScaleY"), (0.0, "TranslateY") })
        {
            var a = new DoubleAnimation { To = v, Duration = TimeSpan.FromMilliseconds(400), EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
            Storyboard.SetTarget(a, ImportExportPasswordDialogTransform); Storyboard.SetTargetProperty(a, p); sb.Children.Add(a);
        }
        sb.Begin();
    }

    private void HideImportExportPasswordDialog()
    {
        _pendingVerifiedAction = null; // N4T-07(A): any non-confirm close (cancel/X/Scrim/Esc) drops the gated flow
        if (_animImportPwd) return; // D3 (Round 5): M2 hide re-entry guard
        _animImportPwd = true;
        var sb = new Storyboard();
        var so = new DoubleAnimation { To = 0, Duration = TimeSpan.FromMilliseconds(200) };
        Storyboard.SetTarget(so, ImportExportPasswordScrim); Storyboard.SetTargetProperty(so, "Opacity"); sb.Children.Add(so);
        var d = new DoubleAnimation { To = 0, Duration = TimeSpan.FromMilliseconds(200) };
        Storyboard.SetTarget(d, ImportExportPasswordDialog); Storyboard.SetTargetProperty(d, "Opacity"); sb.Children.Add(d);
        foreach (var (v, p) in new[] { (0.92, "ScaleX"), (0.92, "ScaleY"), (20.0, "TranslateY") })
        {
            var a = new DoubleAnimation { To = v, Duration = TimeSpan.FromMilliseconds(250), EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
            Storyboard.SetTarget(a, ImportExportPasswordDialogTransform); Storyboard.SetTargetProperty(a, p); sb.Children.Add(a);
        }
        sb.Completed += (_, _) => { ImportExportPasswordOverlay.Visibility = Visibility.Collapsed; _animImportPwd = false; };
        sb.Begin();
    }

    private void ImportExportPasswordClose_Click(object sender, RoutedEventArgs e) => HideImportExportPasswordDialog();

    private void ImportExportPasswordScrim_Tapped(object sender, TappedRoutedEventArgs e)
    {
        if (ReferenceEquals(e.OriginalSource, ImportExportPasswordScrim)) HideImportExportPasswordDialog();
    }

    private void ShowExportOptionsDialog()
    {
        ExportIncludePathsCheckBox.IsChecked = false;
        ExportOptionsDialogTransform.ScaleX = 0.92;
        ExportOptionsDialogTransform.ScaleY = 0.92;
        ExportOptionsDialogTransform.TranslateY = 20;
        ExportOptionsDialog.Opacity = 0;
        ExportOptionsScrim.Opacity = 0;
        ExportOptionsOverlay.Visibility = Visibility.Visible;
        var sb = new Storyboard();
        var si = new DoubleAnimation { To = 1, Duration = TimeSpan.FromMilliseconds(250) };
        Storyboard.SetTarget(si, ExportOptionsScrim); Storyboard.SetTargetProperty(si, "Opacity"); sb.Children.Add(si);
        var di = new DoubleAnimation { To = 1, Duration = TimeSpan.FromMilliseconds(300) };
        Storyboard.SetTarget(di, ExportOptionsDialog); Storyboard.SetTargetProperty(di, "Opacity"); sb.Children.Add(di);
        foreach (var (v, p) in new[] { (1.0, "ScaleX"), (1.0, "ScaleY"), (0.0, "TranslateY") })
        {
            var a = new DoubleAnimation { To = v, Duration = TimeSpan.FromMilliseconds(400), EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
            Storyboard.SetTarget(a, ExportOptionsDialogTransform); Storyboard.SetTargetProperty(a, p); sb.Children.Add(a);
        }
        sb.Begin();
    }

    private void HideExportOptionsDialog()
    {
        if (_animExportOptions) return;
        _animExportOptions = true;
        var sb = new Storyboard();
        var so = new DoubleAnimation { To = 0, Duration = TimeSpan.FromMilliseconds(200) };
        Storyboard.SetTarget(so, ExportOptionsScrim); Storyboard.SetTargetProperty(so, "Opacity"); sb.Children.Add(so);
        var d = new DoubleAnimation { To = 0, Duration = TimeSpan.FromMilliseconds(200) };
        Storyboard.SetTarget(d, ExportOptionsDialog); Storyboard.SetTargetProperty(d, "Opacity"); sb.Children.Add(d);
        foreach (var (v, p) in new[] { (0.92, "ScaleX"), (0.92, "ScaleY"), (20.0, "TranslateY") })
        {
            var a = new DoubleAnimation { To = v, Duration = TimeSpan.FromMilliseconds(250), EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
            Storyboard.SetTarget(a, ExportOptionsDialogTransform); Storyboard.SetTargetProperty(a, p); sb.Children.Add(a);
        }
        sb.Completed += (_, _) => { ExportOptionsOverlay.Visibility = Visibility.Collapsed; _animExportOptions = false; };
        sb.Begin();
    }

    private void ExportOptionsClose_Click(object sender, RoutedEventArgs e) => HideExportOptionsDialog();

    private void ExportOptionsCancel_Click(object sender, RoutedEventArgs e) => HideExportOptionsDialog();

    private void ExportOptionsScrim_Tapped(object sender, TappedRoutedEventArgs e)
    {
        if (ReferenceEquals(e.OriginalSource, ExportOptionsScrim)) HideExportOptionsDialog();
    }

    private async void ExportOptionsConfirm_Click(object sender, RoutedEventArgs e)
    {
        if (_pickerFlowBusy) return; // N5T1-05
        _pickerFlowBusy = true;
        try
        {
            bool includePaths = ExportIncludePathsCheckBox.IsChecked == true; // 4.4: default unchecked -> paths excluded
            HideExportOptionsDialog();
            await ExportBackupFlowAsync(includePaths);
        }
        finally { _pickerFlowBusy = false; }
    }

    

    private void ShowExportMdNoticeDialog() => ShowOverlay(ExportMdNoticeOverlay, ExportMdNoticeDialog, ExportMdNoticeDialogTransform);
    private void HideExportMdNoticeDialog()
    {
        if (_animExportMdNotice) return;
        _animExportMdNotice = true;
        HideOverlay(ExportMdNoticeOverlay, ExportMdNoticeDialog, ExportMdNoticeDialogTransform, () => _animExportMdNotice = false);
    }
    private void ExportMdNoticeClose_Click(object sender, RoutedEventArgs e) => HideExportMdNoticeDialog();
    private void ExportMdNoticeCancel_Click(object sender, RoutedEventArgs e) => HideExportMdNoticeDialog();
    private void ExportMdNoticeScrim_Tapped(object sender, TappedRoutedEventArgs e)
    { if (ReferenceEquals(e.OriginalSource, ExportMdNoticeScrim)) HideExportMdNoticeDialog(); }

    private async void ExportMdNoticeConfirm_Click(object sender, RoutedEventArgs e)
    {
        if (_pickerFlowBusy) return; // N5T1-05
        _pickerFlowBusy = true;
        try
        {
            HideExportMdNoticeDialog();
            await ExportMdFlowAsync();
        }
        finally { _pickerFlowBusy = false; }
    }

    private async System.Threading.Tasks.Task ExportMdFlowAsync()
    {
        try
        {
            var picker = new FileSavePicker();
            InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(App.MainWindow));
            picker.FileTypeChoices.Add("Markdown", new List<string> { ".md" });
            picker.SuggestedFileName = $"Novara_汇总_{DateTime.Now:yyyyMMdd_HHmmss}";
            var file = await picker.PickSaveFileAsync();
            if (file == null) return;

            var md = BuildSummaryMarkdown();
            System.IO.File.WriteAllText(file.Path, md, new System.Text.UTF8Encoding(false));
            App.ShowToast(App.GetString("Common_Toast_Exported"));
        }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"导出 Markdown 失败: {ex.Message}"); App.ShowToast(App.GetString("Common_Toast_Failed")); } // N5T2-06
    }

    
    private async System.Threading.Tasks.Task ExportHtmlFlowAsync()
    {
        try
        {
            var picker = new FileSavePicker();
            InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(App.MainWindow));
            picker.FileTypeChoices.Add("HTML", new List<string> { ".html" });
            picker.SuggestedFileName = $"Novara_记录合集_{DateTime.Now:yyyyMMdd_HHmmss}";
            var file = await picker.PickSaveFileAsync();
            if (file == null) return;

            var html = BuildHtmlCollection();
            System.IO.File.WriteAllText(file.Path, html, new System.Text.UTF8Encoding(false));
            App.ShowToast(App.GetString("Common_Toast_Exported"));
        }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"导出 HTML 合集失败: {ex.Message}"); App.ShowToast(App.GetString("Common_Toast_Failed")); } // N5T2-06
    }

    private string BuildHtmlCollection()
    {
        string logo = "";
        try
        {
            var logoPath = System.IO.Path.Combine(AppContext.BaseDirectory, "Assets", "logo.svg");
            if (System.IO.File.Exists(logoPath)) logo = System.IO.File.ReadAllText(logoPath);
        }
        catch { }

        var db = App.Store?.Database;
        if (db == null) return "";
        var diaries = db.DiaryItems.Where(x => !x.IsDeleted)
            .OrderByDescending(d => d.IsPinned)
            .ThenByDescending(d => d.IsPinned ? (d.PinnedAt ?? d.ModifiedAt) : DateTime.MinValue)
            .ThenByDescending(d => d.ModifiedAt)
            .ToList();

        var sb = new System.Text.StringBuilder();
        sb.AppendLine("<!DOCTYPE html>");
        sb.AppendLine("<html lang=\"zh-CN\">");
        sb.AppendLine("<head>");
        // N5T2-04: second line of defense for exported HTML - even if a sanitizer bypass ever slips
        // through, this CSP blocks script/executable vectors when the file is opened in a browser or
        // rendered by the PDF pipeline (inline styles stay allowed for diary formatting).
        sb.AppendLine("<meta http-equiv=\"Content-Security-Policy\" content=\"default-src 'none'; style-src 'unsafe-inline'; img-src * data:;\">");
        sb.AppendLine("<meta charset=\"UTF-8\">");
        sb.AppendLine("<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">");
        sb.AppendLine("<title>Novara 记录合集</title>");
        sb.AppendLine("<style>");
        sb.AppendLine("body{font-family:-apple-system,'Segoe UI','Microsoft YaHei',sans-serif;max-width:820px;margin:40px auto;padding:0 20px;color:#1a1a1a;line-height:1.7;}");
        sb.AppendLine(".header{display:flex;align-items:center;gap:14px;padding-bottom:18px;border-bottom:2px solid #7276ff;margin-bottom:16px;}");
        sb.AppendLine(".header svg{width:46px;height:46px;flex-shrink:0;}");
        sb.AppendLine(".logo{width:46px;height:46px;border-radius:12px;background:#7276ff;color:#fff;font-size:26px;font-weight:700;display:flex;align-items:center;justify-content:center;flex-shrink:0;}");
        sb.AppendLine(".brand{font-size:24px;font-weight:700;color:#7276ff;line-height:1.2;}");
        sb.AppendLine(".tagline{font-size:13px;color:#888;}");
        sb.AppendLine(".meta{color:#999;font-size:13px;margin-bottom:8px;}");
        sb.AppendLine(".entry{margin:28px 0;padding-bottom:24px;border-bottom:1px solid #eee;}");
        sb.AppendLine(".entry-title{font-size:20px;font-weight:600;margin-bottom:6px;word-break:break-all;}");
        sb.AppendLine(".entry-content{margin-top:10px;}");
        sb.AppendLine(".entry-content img{max-width:100%;height:auto;}");
        sb.AppendLine("pre{white-space:pre-wrap;word-wrap:break-word;background:#f5f5f5;padding:14px;border-radius:6px;font-family:Consolas,'Courier New',monospace;font-size:13px;}");
        sb.AppendLine(".footer{margin-top:40px;padding-top:16px;border-top:2px solid #7276ff;text-align:center;color:#999;font-size:13px;}");
        sb.AppendLine(".footer .slogan{color:#666;margin-bottom:4px;}");
        sb.AppendLine(".footer a{color:#7276ff;text-decoration:none;font-weight:600;}");
        sb.AppendLine("@media print{.entry{page-break-after:always;}}");
        sb.AppendLine("</style>");
        sb.AppendLine("</head>");
        sb.AppendLine("<body>");
        var logoHtml = string.IsNullOrEmpty(logo) ? "<span class=\"logo\">N</span>" : logo;
        sb.AppendLine("<div class=\"header\">" + logoHtml + "<div><div class=\"brand\">Novara</div><div class=\"tagline\">本地优先的个人知识管家</div></div></div>");
        sb.AppendLine($"<p class=\"meta\">导出时间：{DateTime.Now:yyyy-MM-dd HH:mm}</p>");

        foreach (var d in diaries)
        {
            string title = System.Net.WebUtility.HtmlEncode(string.IsNullOrWhiteSpace(d.Title) ? "（无标题）" : d.Title);
            string meta = $"{App.GetString("Search_Created")} {d.CreatedAt:yyyy-MM-dd HH:mm} · {App.GetString("Search_Modified")} {d.ModifiedAt:yyyy-MM-dd HH:mm}";
            sb.AppendLine("<div class=\"entry\">");
            sb.AppendLine($"<div class=\"entry-title\">{title}</div>");
            sb.AppendLine($"<div class=\"meta\">{meta}</div>");
            if (d.Format == "markdown")
            {
                sb.AppendLine("<div class=\"entry-content\"><pre>" + System.Net.WebUtility.HtmlEncode(d.Content ?? "") + "</pre></div>");
            }
            else
            {
                sb.AppendLine("<div class=\"entry-content\">" + HtmlSanitizer.Sanitize(d.Content ?? "") + "</div>");
            }
            sb.AppendLine("</div>");
        }

        sb.AppendLine("<div class=\"footer\"><div class=\"slogan\">Novara · 本地优先 · 私密安心</div><div>官网：<a href=\"https://novara.xin\">novara.xin</a></div></div>");
        sb.AppendLine("</body>");
        sb.AppendLine("</html>");
        return sb.ToString();
    }

    
    private async System.Threading.Tasks.Task ExportPdfFlowAsync()
    {
        try
        {
            var picker = new FileSavePicker();
            InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(App.MainWindow));
            picker.FileTypeChoices.Add("PDF", new List<string> { ".pdf" });
            picker.SuggestedFileName = $"Novara_记录合集_{DateTime.Now:yyyyMMdd_HHmmss}";
            var file = await picker.PickSaveFileAsync();
            if (file == null) return;

            bool pdfOk = await ExportPdfViaWebView2Async(BuildHtmlCollection(), file.Path);
            App.ShowToast(App.GetString(pdfOk ? "Common_Toast_Exported" : "Common_Toast_Failed")); // N4T-02: WebView2 missing / print failure must not toast success
        }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"导出 PDF 合集失败: {ex.Message}"); }
    }

    /// <summary>N4T-02: returns true only when the PDF actually landed on disk.</summary>
    private async System.Threading.Tasks.Task<bool> ExportPdfViaWebView2Async(string html, string pdfPath)
    {
        var webView = new Microsoft.UI.Xaml.Controls.WebView2
        {
            Visibility = Visibility.Collapsed, 
            Width = 794,   // A4 @96dpi
            Height = 1123,
        };
        SettingsRoot.Children.Add(webView);
        try
        {
            
            var env = await Microsoft.Web.WebView2.Core.CoreWebView2Environment.CreateWithOptionsAsync(
                null,
                System.IO.Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    Novara.Services.CoreEnv.DataDirName, "Webview2"),
                null);
            await webView.EnsureCoreWebView2Async(env);
            var cv = webView.CoreWebView2;
            if (cv == null) return false;

            var navTcs = new System.Threading.Tasks.TaskCompletionSource<bool>();
            webView.NavigationCompleted += (_, e) => navTcs.TrySetResult(e.IsSuccess); // N5T2-05: a failed navigation (error page) must not print as a "successful" PDF
            webView.NavigateToString(html);
            // N4T-02: a navigation that never completes used to hang this flow forever with no feedback.
            var completed = await System.Threading.Tasks.Task.WhenAny(navTcs.Task, System.Threading.Tasks.Task.Delay(TimeSpan.FromSeconds(30)));
            if (completed != navTcs.Task || !await navTcs.Task) return false;
            await System.Threading.Tasks.Task.Delay(400); 

            var ps = cv.Environment.CreatePrintSettings();
            ps.PageWidth = 8.27;    
            ps.PageHeight = 11.69;
            ps.MarginTop = 0.5;
            ps.MarginBottom = 0.5;
            ps.MarginLeft = 0.6;
            ps.MarginRight = 0.6;
            ps.ShouldPrintBackgrounds = true;
            return await cv.PrintToPdfAsync(pdfPath, ps);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"PDF 打印失败: {ex.Message}");
            return false;
        }
        finally
        {
            SettingsRoot.Children.Remove(webView);
            try { webView.Close(); } catch { }
        }
    }

    private string BuildSummaryMarkdown()
    {
        var db = App.Store?.Database;
        if (db == null) return "";
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("# Novara 数据汇总");
        sb.AppendLine();
        sb.AppendLine($"> 导出时间：{DateTime.Now:yyyy-MM-dd HH:mm}");
        sb.AppendLine();

        
        var entries = db.MemoEntries.Where(x => !x.IsDeleted).ToList();
        var groups = db.MemoGroups.ToList();
        sb.AppendLine("## 备忘");
        sb.AppendLine();
        foreach (var g in groups)
        {
            sb.AppendLine($"### 📁 {g.Name}");
            var inGroup = entries.Where(e => e.GroupId == g.Id).ToList();
            if (inGroup.Count == 0) { sb.AppendLine("（空）"); sb.AppendLine(); continue; }
            foreach (var e in inGroup)
            {
                sb.AppendLine($"- **{e.Name}**");
                if (!string.IsNullOrWhiteSpace(e.KeyInfo)) sb.AppendLine($"  - {e.KeyInfo}");
                foreach (var f in e.Fields)
                    if (!string.IsNullOrWhiteSpace(f.Value)) sb.AppendLine($"  - {f.Label}：{f.Value}");
            }
            sb.AppendLine();
        }
        var uncategorized = entries.Where(e => !e.GroupId.HasValue).ToList();
        if (uncategorized.Count > 0)
        {
            sb.AppendLine("### 📁 未分类");
            foreach (var e in uncategorized)
            {
                sb.AppendLine($"- **{e.Name}**");
                if (!string.IsNullOrWhiteSpace(e.KeyInfo)) sb.AppendLine($"  - {e.KeyInfo}");
                foreach (var f in e.Fields)
                    if (!string.IsNullOrWhiteSpace(f.Value)) sb.AppendLine($"  - {f.Label}：{f.Value}");
            }
            sb.AppendLine();
        }

        
        var paths = db.PathBackupItems.Where(x => !x.IsDeleted).ToList();
        if (paths.Count > 0)
        {
            sb.AppendLine("## 路径备份");
            sb.AppendLine();
            foreach (var p in paths)
            {
                sb.AppendLine($"- **{p.Name}**");
                sb.AppendLine($"  - 路径：{p.Path}");
                if (!string.IsNullOrWhiteSpace(p.Note)) sb.AppendLine($"  - 备注：{p.Note}");
            }
            sb.AppendLine();
        }

        
        var todos = db.TodoCards.Where(x => !x.IsDeleted).ToList();
        var notes = db.NoteCards.Where(x => !x.IsDeleted).ToList();
        if (todos.Count > 0 || notes.Count > 0)
        {
            sb.AppendLine("## 计划");
            sb.AppendLine();
            if (todos.Count > 0)
            {
                sb.AppendLine("### 待办");
                foreach (var t in todos)
                {
                    sb.AppendLine($"- **{t.Title}**");
                    if (!string.IsNullOrWhiteSpace(t.MainText))
                    {
                        bool mainDone = t.CheckedStates is { Count: > 0 } cs && cs[0];
                        sb.AppendLine($"  - {(mainDone ? "[x]" : "[ ]")} {t.MainText}");
                    }
                    for (int i = 0; i < t.SubTexts.Count; i++)
                    {
                        bool done = t.CheckedStates is { Count: > 0 } cs && i + 1 < cs.Count && cs[i + 1];
                        sb.AppendLine($"  - {(done ? "[x]" : "[ ]")} {t.SubTexts[i]}");
                    }
                }
                sb.AppendLine();
            }
            if (notes.Count > 0)
            {
                sb.AppendLine("### 便签");
                foreach (var n in notes)
                {
                    sb.AppendLine($"- **{n.Title}**");
                    if (!string.IsNullOrWhiteSpace(n.Content)) sb.AppendLine($"  - {n.Content}");
                }
                sb.AppendLine();
            }
        }

        
        var diaries = db.DiaryItems.Where(x => !x.IsDeleted).ToList();
        if (diaries.Count > 0)
        {
            sb.AppendLine("## 日记");
            sb.AppendLine();
            foreach (var d in diaries)
            {
                sb.AppendLine($"### {d.Title}");
                sb.AppendLine();
                sb.AppendLine(ConvertDiaryHtmlToPlainMarkdown(d.Content));
                sb.AppendLine();
            }
        }

        return sb.ToString();
    }

    
    private static string ConvertDiaryHtmlToPlainMarkdown(string html)
    {
        if (string.IsNullOrWhiteSpace(html)) return "";
        var t = System.Net.WebUtility.HtmlDecode(html);
        t = System.Text.RegularExpressions.Regex.Replace(t, @"<img[^>]*>", "", System.Text.RegularExpressions.RegexOptions.IgnoreCase); 
        t = System.Text.RegularExpressions.Regex.Replace(t, @"</?(?:b|strong)>", "**", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        t = System.Text.RegularExpressions.Regex.Replace(t, @"</?(?:i|em)>", "*", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        t = System.Text.RegularExpressions.Regex.Replace(t, @"<li[^>]*>", "- ", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        t = System.Text.RegularExpressions.Regex.Replace(t, @"</li>", "\n", System.Text.RegularExpressions.RegexOptions.IgnoreCase); 
        t = System.Text.RegularExpressions.Regex.Replace(t, @"</?(?:ul|ol)[^>]*>", "\n", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        t = System.Text.RegularExpressions.Regex.Replace(t, @"<br\s*/?>", "\n", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        t = System.Text.RegularExpressions.Regex.Replace(t, @"</p>", "\n", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        t = System.Text.RegularExpressions.Regex.Replace(t, @"<[^>]+>", "");
        t = System.Text.RegularExpressions.Regex.Replace(t, @"\n{3,}", "\n\n");
        return t.Trim();
    }

    /// <summary>N4T-07(A): the CSV/MD/HTML/PDF convenience flows must pass the same privacy-lock
    /// password gate as the native .novabak paths when the lock is enabled (design 4.6).</summary>
    private void RunPrivacyGated(Action flow)
    {
        if (!_isPrivacyLockEnabled) { flow(); return; }
        _pendingExport = false; // keep the native flag from hijacking the confirm branch
        _pendingVerifiedAction = flow;
        ShowImportExportPasswordDialog(App.GetString("Setting_Archive_VerifyTitle"));
    }

    private void ImportExportPasswordConfirm_Click(object sender, RoutedEventArgs e)
    {
        if (_animImportPwd) return; // E4-05: block double-click during the hide animation (second click would read consumed _pendingExport and fall into the import branch)
        var pw = ImportExportPasswordBox.Text.Trim();
        if (pw.Length < 6) { FlashTextBox(ImportExportPasswordBox); return; }
        if (!PasswordService.Verify(pw)) { FlashTextBox(ImportExportPasswordBox); return; }
        // N4T-07(A): a gated convenience flow takes priority over the native import/export dispatch.
        if (_pendingVerifiedAction != null)
        {
            var action = _pendingVerifiedAction;
            _pendingVerifiedAction = null;
            HideImportExportPasswordDialog();
            action();
            return;
        }
        bool export = _pendingExport;
        _pendingExport = false;
        HideImportExportPasswordDialog();
        if (export) ShowExportOptionsDialog();
        else _ = ImportAfterPasswordAsync(null); // E5-28: pre-picked-path flow removed - the file is picked after the password verify (ImportAfterPasswordAsync re-picks when null)
    }

    private void ShowImportConfirmDialog()
    {
        ImportConfirmDialogTransform.ScaleX = 0.92;
        ImportConfirmDialogTransform.ScaleY = 0.92;
        ImportConfirmDialogTransform.TranslateY = 20;
        ImportConfirmDialog.Opacity = 0;
        ImportConfirmScrim.Opacity = 0;
        ImportConfirmOverlay.Visibility = Visibility.Visible;
        var sb = new Storyboard();
        var si = new DoubleAnimation { To = 1, Duration = TimeSpan.FromMilliseconds(250) };
        Storyboard.SetTarget(si, ImportConfirmScrim); Storyboard.SetTargetProperty(si, "Opacity"); sb.Children.Add(si);
        var di = new DoubleAnimation { To = 1, Duration = TimeSpan.FromMilliseconds(300) };
        Storyboard.SetTarget(di, ImportConfirmDialog); Storyboard.SetTargetProperty(di, "Opacity"); sb.Children.Add(di);
        foreach (var (v, p) in new[] { (1.0, "ScaleX"), (1.0, "ScaleY"), (0.0, "TranslateY") })
        {
            var a = new DoubleAnimation { To = v, Duration = TimeSpan.FromMilliseconds(400), EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
            Storyboard.SetTarget(a, ImportConfirmDialogTransform); Storyboard.SetTargetProperty(a, p); sb.Children.Add(a);
        }
        sb.Begin();
    }

    private void HideImportConfirmDialog()
    {
        if (_animImportConfirm) return; // D3 (Round 5): M2 hide re-entry guard
        _animImportConfirm = true;
        var sb = new Storyboard();
        var so = new DoubleAnimation { To = 0, Duration = TimeSpan.FromMilliseconds(200) };
        Storyboard.SetTarget(so, ImportConfirmScrim); Storyboard.SetTargetProperty(so, "Opacity"); sb.Children.Add(so);
        var d = new DoubleAnimation { To = 0, Duration = TimeSpan.FromMilliseconds(200) };
        Storyboard.SetTarget(d, ImportConfirmDialog); Storyboard.SetTargetProperty(d, "Opacity"); sb.Children.Add(d);
        foreach (var (v, p) in new[] { (0.92, "ScaleX"), (0.92, "ScaleY"), (20.0, "TranslateY") })
        {
            var a = new DoubleAnimation { To = v, Duration = TimeSpan.FromMilliseconds(250), EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
            Storyboard.SetTarget(a, ImportConfirmDialogTransform); Storyboard.SetTargetProperty(a, p); sb.Children.Add(a);
        }
        sb.Completed += (_, _) => { ImportConfirmOverlay.Visibility = Visibility.Collapsed; _animImportConfirm = false; };
        sb.Begin();
    }

    private void ImportConfirmCancel_Click(object sender, RoutedEventArgs e) => HideImportConfirmDialog();

    private void ImportConfirmScrim_Tapped(object sender, TappedRoutedEventArgs e)
    {
        if (ReferenceEquals(e.OriginalSource, ImportConfirmScrim)) HideImportConfirmDialog();
    }

    private void ShowImportResult(string title, string message)
    {
        ImportResultTitle.Text = title;
        ImportResultMessage.Text = message;
        ImportResultDialogTransform.ScaleX = 0.92;
        ImportResultDialogTransform.ScaleY = 0.92;
        ImportResultDialogTransform.TranslateY = 20;
        var scrim = FindOverlayScrim(ImportResultOverlay);
        if (scrim != null) scrim.Opacity = 0; // ND3
        ImportResultDialog.Opacity = 0;
        ImportResultOverlay.Visibility = Visibility.Visible;
        var sb = new Storyboard();
        if (scrim != null)
        {
            var si = new DoubleAnimation { To = 1, Duration = TimeSpan.FromMilliseconds(250) };
            Storyboard.SetTarget(si, scrim); Storyboard.SetTargetProperty(si, "Opacity"); sb.Children.Add(si);
        }
        var di = new DoubleAnimation { To = 1, Duration = TimeSpan.FromMilliseconds(300) };
        Storyboard.SetTarget(di, ImportResultDialog); Storyboard.SetTargetProperty(di, "Opacity"); sb.Children.Add(di);
        foreach (var (v, p) in new[] { (1.0, "ScaleX"), (1.0, "ScaleY"), (0.0, "TranslateY") })
        {
            var a = new DoubleAnimation { To = v, Duration = TimeSpan.FromMilliseconds(400), EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
            Storyboard.SetTarget(a, ImportResultDialogTransform); Storyboard.SetTargetProperty(a, p); sb.Children.Add(a);
        }
        sb.Begin();
    }

    private void HideImportResult()
    {
        if (_animImportResult) return; // D3 (Round 5): M2 hide re-entry guard
        _animImportResult = true;
        var sb = new Storyboard();
        var scrim = FindOverlayScrim(ImportResultOverlay);
        if (scrim != null)
        {
            var so = new DoubleAnimation { To = 0, Duration = TimeSpan.FromMilliseconds(200) };
            Storyboard.SetTarget(so, scrim); Storyboard.SetTargetProperty(so, "Opacity"); sb.Children.Add(so);
        }
        var d = new DoubleAnimation { To = 0, Duration = TimeSpan.FromMilliseconds(200) };
        Storyboard.SetTarget(d, ImportResultDialog); Storyboard.SetTargetProperty(d, "Opacity"); sb.Children.Add(d);
        foreach (var (v, p) in new[] { (0.92, "ScaleX"), (0.92, "ScaleY"), (20.0, "TranslateY") })
        {
            var a = new DoubleAnimation { To = v, Duration = TimeSpan.FromMilliseconds(250), EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
            Storyboard.SetTarget(a, ImportResultDialogTransform); Storyboard.SetTargetProperty(a, p); sb.Children.Add(a);
        }
        sb.Completed += (_, _) => { ImportResultOverlay.Visibility = Visibility.Collapsed; _animImportResult = false; };
        sb.Begin();
    }

    private void ImportResultConfirm_Click(object sender, RoutedEventArgs e)
    {
        HideImportResult();

        if (_pendingReloadAfterImport)
        {
            _pendingReloadAfterImport = false;
            App.MainWindow?.ReloadPages();
        }
    }

    /* ========== CSV Import Preview Overlay ========== Function: preview imported CSV entries; case A (Novara) confirm-only, case B (unknown) type selector + cancel/confirm Corresponding UI: CsvImportPreviewOverlay Logic Range: Show/Hide animation + event stubs */
    private void ShowCsvImportPreview(bool isNovaraFormat)
    {
        CsvImportPreviewTitle.Text = App.GetString("Setting_CsvImport_Title");
        CsvImportPreviewConfirmText.Text = App.GetString("Common_Button_Confirm");

        if (isNovaraFormat)
        {
            CsvImportTypePanel.Visibility = Visibility.Collapsed;
            CsvImportPreviewCancelBtn.Visibility = Visibility.Collapsed;
            CsvImportPreviewConfirmBtn.HorizontalAlignment = HorizontalAlignment.Center;
            CsvImportPreviewConfirmBtn.Width = 190;
        }
        else
        {
            CsvImportTypeButtonText.Text = CsvTypeLabel(_csvImportType);
            CsvImportTypePanel.Visibility = Visibility.Visible;
            CsvImportPreviewCancelBtn.Visibility = Visibility.Visible;
            CsvImportPreviewCancelText.Text = App.GetString("Common_Button_Cancel");
            CsvImportPreviewConfirmBtn.HorizontalAlignment = HorizontalAlignment.Right;
        }

        CsvImportPreviewDialogTransform.ScaleX = 0.92;
        CsvImportPreviewDialogTransform.ScaleY = 0.92;
        CsvImportPreviewDialogTransform.TranslateY = 20;
        CsvImportPreviewDialog.Opacity = 0;
        CsvImportPreviewScrim.Opacity = 0;
        CsvImportPreviewOverlay.Visibility = Visibility.Visible;
        var sb = new Storyboard();
        var si = new DoubleAnimation { To = 1, Duration = TimeSpan.FromMilliseconds(250) };
        Storyboard.SetTarget(si, CsvImportPreviewScrim); Storyboard.SetTargetProperty(si, "Opacity"); sb.Children.Add(si);
        var di = new DoubleAnimation { To = 1, Duration = TimeSpan.FromMilliseconds(300) };
        Storyboard.SetTarget(di, CsvImportPreviewDialog); Storyboard.SetTargetProperty(di, "Opacity"); sb.Children.Add(di);
        foreach (var (v, p) in new[] { (1.0, "ScaleX"), (1.0, "ScaleY"), (0.0, "TranslateY") })
        {
            var a = new DoubleAnimation { To = v, Duration = TimeSpan.FromMilliseconds(400), EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
            Storyboard.SetTarget(a, CsvImportPreviewDialogTransform); Storyboard.SetTargetProperty(a, p); sb.Children.Add(a);
        }
        sb.Begin();
    }

    private void HideCsvImportPreview()
    {
        if (_animCsvPreview) return;
        _animCsvPreview = true;
        var sb = new Storyboard();
        var so = new DoubleAnimation { To = 0, Duration = TimeSpan.FromMilliseconds(200) };
        Storyboard.SetTarget(so, CsvImportPreviewScrim); Storyboard.SetTargetProperty(so, "Opacity"); sb.Children.Add(so);
        var d = new DoubleAnimation { To = 0, Duration = TimeSpan.FromMilliseconds(200) };
        Storyboard.SetTarget(d, CsvImportPreviewDialog); Storyboard.SetTargetProperty(d, "Opacity"); sb.Children.Add(d);
        foreach (var (v, p) in new[] { (0.92, "ScaleX"), (0.92, "ScaleY"), (20.0, "TranslateY") })
        {
            var a = new DoubleAnimation { To = v, Duration = TimeSpan.FromMilliseconds(250), EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
            Storyboard.SetTarget(a, CsvImportPreviewDialogTransform); Storyboard.SetTargetProperty(a, p); sb.Children.Add(a);
        }
        sb.Completed += (_, _) => { CsvImportPreviewOverlay.Visibility = Visibility.Collapsed; _animCsvPreview = false; };
        sb.Begin();
    }

    private void CsvImportPreviewScrim_Tapped(object sender, TappedRoutedEventArgs e) { if (ReferenceEquals(e.OriginalSource, CsvImportPreviewScrim)) HideCsvImportPreview(); }
    private void CsvImportPreviewClose_Click(object sender, RoutedEventArgs e) => HideCsvImportPreview();
    private void CsvImportPreviewCancel_Click(object sender, RoutedEventArgs e) => HideCsvImportPreview();
    private CsvImportResult? _csvImportResult;
    private string _csvImportText = "";
    private string _csvImportType = "账户";
    private static readonly string[] CsvImportTypeOptions = { "邮箱", "账户", "API Key", "网站", "银行卡", "WiFi", "证件", "自定义" };
    private MenuFlyout? _csvTypeMenu;

    private async System.Threading.Tasks.Task PickAndShowCsvImportAsync()
    {
        try
        {
            var picker = new FileOpenPicker();
            InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(App.MainWindow));
            picker.FileTypeFilter.Add(".csv");
            var file = await picker.PickSingleFileAsync();
            if (file == null) return;
            _csvImportText = System.IO.File.ReadAllText(file.Path);

            
            _csvImportType = "账户";

            _csvImportResult = CsvImportExportService.ParseCsv(_csvImportText, _csvImportType);
            DedupCsvAgainstExisting(_csvImportResult);
            PopulateCsvImportPreview(_csvImportResult);
            ShowCsvImportPreview(_csvImportResult.IsNovaraFormat);
        }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"CSV 导入失败: {ex.Message}"); }
    }

    private void PopulateCsvImportPreview(CsvImportResult result)
    {
        CsvImportPreviewList.Children.Clear();
        foreach (var entry in result.Entries)
        {
            CsvImportPreviewList.Children.Add(new TextBlock
            {
                Text = $"  {entry.Type}  {entry.Name}",
                FontSize = 13,
                Foreground = App.GetBrush("AppTextSecondaryBrush"),
                Margin = new Thickness(0, 0, 0, 6),
                TextWrapping = TextWrapping.NoWrap,
                TextTrimming = TextTrimming.CharacterEllipsis,
            });
        }
        if (result.Entries.Count == 0)
            CsvImportPreviewList.Children.Add(new TextBlock { Text = "（无有效条目）", FontSize = 13, Foreground = App.GetBrush("AppTextTertiaryBrush") });
        var baseInfo = result.IsNovaraFormat
            ? string.Format(App.GetString("Setting_CsvImport_NovaraInfo"), result.Entries.Count)
            : App.GetString("Setting_CsvImport_UnknownInfo");
        // NC10: tell the user how many rows were filtered out (required-field misses + duplicates)
        if (result.SkippedCount > 0)
            baseInfo += string.Format(App.GetString("Setting_CsvImport_Skipped"), result.SkippedCount);
        CsvImportPreviewInfo.Text = baseInfo;
    }

    
    
    
    private void DedupCsvAgainstExisting(CsvImportResult result)
    {
        var db = App.Store?.Database;
        if (db == null) return;

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var e in db.MemoEntries)
        {
            if (e.IsDeleted) continue;
            var gname = e.GroupId.HasValue
                ? db.MemoGroups.FirstOrDefault(g => g.Id == e.GroupId.Value)?.Name ?? ""
                : "";
            seen.Add(e.Type + '\u0001' + e.Name + '\u0001' + gname.Trim());
        }

        var keptEntries = new List<Novara.Models.MemoEntry>();
        var keptGroups = new List<string?>();
        for (int i = 0; i < result.Entries.Count; i++)
        {
            var entry = result.Entries[i];
            var gn = result.GroupNames.Count > i ? result.GroupNames[i] : null;
            var key = entry.Type + '\u0001' + entry.Name + '\u0001' + (string.IsNullOrWhiteSpace(gn) ? "" : gn!.Trim());
            if (seen.Add(key)) { keptEntries.Add(entry); keptGroups.Add(gn); }
            else result.SkippedCount++;
        }
        result.Entries = keptEntries;
        result.GroupNames = keptGroups;
    }

    private void CsvImportPreviewConfirm_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(_csvImportText)) return;
        HideCsvImportPreview();
        var db = App.Store?.Database;
        if (db == null)
        {
            // NC13: no store -> clear the pending import state instead of leaving it dangling.
            _csvImportText = "";
            _csvImportResult = null;
            return;
        }
        var finalResult = _csvImportResult != null && _csvImportResult.IsNovaraFormat
            ? _csvImportResult
            : CsvImportExportService.ParseCsv(_csvImportText, _csvImportType);
        DedupCsvAgainstExisting(finalResult); 
        for (int i = 0; i < finalResult.Entries.Count; i++)
        {
            var entry = finalResult.Entries[i];
            if (!finalResult.IsNovaraFormat) entry.Type = _csvImportType;
            if (entry.Type == "自定义" && string.IsNullOrEmpty(entry.IconKey))
                entry.IconKey = GetRandomIconKey();
            entry.GroupId = ResolveGroupId(finalResult.GroupNames.Count > i ? finalResult.GroupNames[i] : null);
            db.MemoEntries.Add(entry);
        }
        App.Store?.SaveAsync();
        _csvImportText = "";
        _csvImportResult = null;
        App.MainWindow?.ReloadPages(); // N4T-01: the memo page instance is cached with a _storeLoaded idempotent guard - without a rebuild the imported entries/groups stay invisible until restart (native import at :2162 already did this)
        App.ShowToast(App.GetString("Common_Toast_Imported"));
    }

    private static string GetRandomIconKey()
    {
        var groupKeys = IconData.GroupIconKeysInOrder()
            .Where(k => k.StartsWith("Group"))
            .ToArray();
        if (groupKeys.Length == 0) return "Group01";
        return groupKeys[Random.Shared.Next(groupKeys.Length)];
    }

    
    
    private Guid? ResolveGroupId(string? groupName)
    {
        if (string.IsNullOrWhiteSpace(groupName)) return null;
        var db = App.Store?.Database;
        if (db == null) return null;
        var existing = db.MemoGroups.FirstOrDefault(g => string.Equals(g.Name, groupName.Trim(), StringComparison.OrdinalIgnoreCase));
        if (existing != null) return existing.Id;
        var newGroup = new Novara.Models.MemoGroup { Name = groupName, IconKey = GetRandomIconKey(), CreatedAt = DateTime.Now };
        db.MemoGroups.Add(newGroup);
        return newGroup.Id;
    }

    private MenuFlyout BuildCsvTypeMenu()
    {
        var menu = new MenuFlyout();
        menu.MenuFlyoutPresenterStyle = (Style)Application.Current.Resources["GlassMenuFlyoutPresenterStyle"];
        foreach (var type in CsvImportTypeOptions)
        {
            var item = new MenuFlyoutItem
            {
                Style = (Style)Application.Current.Resources["GlassMenuFlyoutItemStyle"],
                Text = CsvTypeLabel(type),
            };
            item.Click += (s, e) =>
            {
                _csvImportType = type;
                CsvImportTypeButtonText.Text = CsvTypeLabel(type);
                if (!string.IsNullOrEmpty(_csvImportText))
                {
                    _csvImportResult = CsvImportExportService.ParseCsv(_csvImportText, _csvImportType);
                    DedupCsvAgainstExisting(_csvImportResult);
                    PopulateCsvImportPreview(_csvImportResult);
                }
            };
            menu.Items.Add(item);
        }
        return menu;
    }

    private void CsvImportTypeButton_Click(object sender, RoutedEventArgs e)
    {
        _csvTypeMenu ??= BuildCsvTypeMenu();
        if (sender is Button btn)
            _csvTypeMenu.ShowAt(btn, new Windows.Foundation.Point(0, btn.ActualHeight + 4));
    }

    private static string CsvTypeLabel(string type) => type switch
    {
        "邮箱" => App.GetString("Memo_Type_Email"),
        "账户" => App.GetString("Memo_Type_Account"),
        "API Key" => App.GetString("Memo_Type_ApiKey"),
        "网站" => App.GetString("Memo_Type_Website"),
        "银行卡" => App.GetString("Memo_Type_BankCard"),
        "WiFi" => App.GetString("Memo_Type_Wifi"),
        "证件" => App.GetString("Memo_Type_IdCard"),
        "自定义" => App.GetString("Memo_Type_Custom"),
        _ => type
    };

    /* ========== CSV Export Notice Overlay ========== Function: confirm dialog before CSV export (same layout as MD notice) Corresponding UI: CsvExportNoticeOverlay */
    private void ShowCsvExportNoticeDialog()
    {
        CsvExportNoticeTitle.Text = App.GetString("Setting_CsvExport_NoticeTitle");
        CsvExportNoticeDesc.Text = App.GetString("Setting_CsvExport_NoticeDesc");
        CsvExportNoticeCancelText.Text = App.GetString("Common_Button_Cancel");
        CsvExportNoticeConfirmText.Text = App.GetString("Setting_Archive_Export");

        CsvExportNoticeDialogTransform.ScaleX = 0.92;
        CsvExportNoticeDialogTransform.ScaleY = 0.92;
        CsvExportNoticeDialogTransform.TranslateY = 20;
        CsvExportNoticeDialog.Opacity = 0;
        CsvExportNoticeScrim.Opacity = 0;
        CsvExportNoticeOverlay.Visibility = Visibility.Visible;
        var sb = new Storyboard();
        var si = new DoubleAnimation { To = 1, Duration = TimeSpan.FromMilliseconds(250) };
        Storyboard.SetTarget(si, CsvExportNoticeScrim); Storyboard.SetTargetProperty(si, "Opacity"); sb.Children.Add(si);
        var di = new DoubleAnimation { To = 1, Duration = TimeSpan.FromMilliseconds(300) };
        Storyboard.SetTarget(di, CsvExportNoticeDialog); Storyboard.SetTargetProperty(di, "Opacity"); sb.Children.Add(di);
        foreach (var (v, p) in new[] { (1.0, "ScaleX"), (1.0, "ScaleY"), (0.0, "TranslateY") })
        {
            var a = new DoubleAnimation { To = v, Duration = TimeSpan.FromMilliseconds(400), EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
            Storyboard.SetTarget(a, CsvExportNoticeDialogTransform); Storyboard.SetTargetProperty(a, p); sb.Children.Add(a);
        }
        sb.Begin();
    }

    private void HideCsvExportNoticeDialog()
    {
        if (_animCsvExportNotice) return;
        _animCsvExportNotice = true;
        var sb = new Storyboard();
        var so = new DoubleAnimation { To = 0, Duration = TimeSpan.FromMilliseconds(200) };
        Storyboard.SetTarget(so, CsvExportNoticeScrim); Storyboard.SetTargetProperty(so, "Opacity"); sb.Children.Add(so);
        var d = new DoubleAnimation { To = 0, Duration = TimeSpan.FromMilliseconds(200) };
        Storyboard.SetTarget(d, CsvExportNoticeDialog); Storyboard.SetTargetProperty(d, "Opacity"); sb.Children.Add(d);
        foreach (var (v, p) in new[] { (0.92, "ScaleX"), (0.92, "ScaleY"), (20.0, "TranslateY") })
        {
            var a = new DoubleAnimation { To = v, Duration = TimeSpan.FromMilliseconds(250), EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
            Storyboard.SetTarget(a, CsvExportNoticeDialogTransform); Storyboard.SetTargetProperty(a, p); sb.Children.Add(a);
        }
        sb.Completed += (_, _) => { CsvExportNoticeOverlay.Visibility = Visibility.Collapsed; _animCsvExportNotice = false; };
        sb.Begin();
    }

    private void CsvExportNoticeScrim_Tapped(object sender, TappedRoutedEventArgs e) { if (ReferenceEquals(e.OriginalSource, CsvExportNoticeScrim)) HideCsvExportNoticeDialog(); }
    private void CsvExportNoticeClose_Click(object sender, RoutedEventArgs e) => HideCsvExportNoticeDialog();
    private void CsvExportNoticeCancel_Click(object sender, RoutedEventArgs e) => HideCsvExportNoticeDialog();
    private async void CsvExportNoticeConfirm_Click(object sender, RoutedEventArgs e)
    {
        if (_pickerFlowBusy) return; // N5T1-05
        _pickerFlowBusy = true;
        try
        {
            HideCsvExportNoticeDialog();
            await ExportCsvFlowAsync();
        }
        finally { _pickerFlowBusy = false; }
    }

    private async System.Threading.Tasks.Task ExportCsvFlowAsync()
    {
        try
        {
            var picker = new FileSavePicker();
            InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(App.MainWindow));
            picker.FileTypeChoices.Add("CSV", new List<string> { ".csv" });
            picker.SuggestedFileName = $"Novara_{App.GetString("Setting_CsvExport_FileName")}_{DateTime.Now:yyyyMMdd_HHmmss}";
            var file = await picker.PickSaveFileAsync();
            if (file == null) return;

            var entries = App.Store?.Database.MemoEntries ?? new();
            var groups = App.Store?.Database.MemoGroups ?? new();
            var csv = CsvImportExportService.ExportMemoEntriesToCsv(entries, groups);
            System.IO.File.WriteAllText(file.Path, csv, new System.Text.UTF8Encoding(true));
            App.ShowToast(App.GetString("Common_Toast_Exported"));
        }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"CSV 导出失败: {ex.Message}"); App.ShowToast(App.GetString("Common_Toast_Failed")); } // N5T2-06
    }

    

    private void InitBackupFreqCombo()
    {
        BackupFreqButtonText.Text = GetBackupFreqLabel(_backupFreqIndex);
    }

    private string GetBackupFreqLabel(int index) => index switch
    {
        1 => App.GetString("Setting_Backup_Freq_1h"),
        2 => App.GetString("Setting_Backup_Freq_6h"),
        3 => App.GetString("Setting_Backup_Freq_1d"),
        _ => App.GetString("Setting_Backup_Freq_30m"),
    };

    private void UpdateBackupUI()
    {
        bool hasSnapshots = AutoBackupService.HasSnapshots();

        
        ((TextBlock)BackupToggleButton.Content).Text = _backupEnabled
            ? App.GetString("Setting_Backup_On")
            : App.GetString("Setting_Backup_Off");
        ApplyToggleState(BackupToggleButton, _backupEnabled);

        
        BackupManageButton.Visibility = (_backupEnabled || hasSnapshots) ? Visibility.Visible : Visibility.Collapsed;
        
        BackupModeButton.Visibility = _backupEnabled ? Visibility.Visible : Visibility.Collapsed;
        
        ApplyToggleState(BackupModeButton, _autoBackupEnabled);
    }

    private void PersistBackupSettings()
    {
        PersistSetting(s =>
        {
            s.BackupEnabled = _backupEnabled;
            s.AutoBackupEnabled = _autoBackupEnabled;
            s.BackupFreq = _backupFreqIndex;
        });
        AutoBackupService.SyncAutoBackupTimer(DispatcherQueue); 
    }

    
    private void BackupToggleButton_Click(object sender, RoutedEventArgs e)
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

        var onItem = MakeItem(App.GetString("Setting_Backup_On"), _backupEnabled);
        var offItem = MakeItem(App.GetString("Setting_Backup_Off"), !_backupEnabled);

        onItem.Click += (_, _) =>
        {
            if (_backupEnabled) return;
            if (!AutoBackupService.HasSnapshots()) ShowBackupIntroDialog(); 
            else EnableBackup();
        };
        offItem.Click += (_, _) =>
        {
            if (!_backupEnabled) return;
            _backupEnabled = false;
            PersistBackupSettings();
            UpdateBackupUI();
            App.ShowToast(App.GetString("Common_Toast_Switched"));
            
        };

        menu.Items.Add(onItem); menu.Items.Add(offItem);
        menu.ShowAt(BackupToggleButton, new Windows.Foundation.Point(0, BackupToggleButton.ActualHeight + 4));
    }

    private void EnableBackup()
    {
        _backupEnabled = true;
        PersistBackupSettings();
        UpdateBackupUI();
        App.ShowToast(App.GetString("Common_Toast_Switched"));
    }

    
    private void BackupManageButton_Click(object sender, RoutedEventArgs e)
    {
        var menu = new MenuFlyout { MenuFlyoutPresenterStyle = (Style)Application.Current.Resources["GlassMenuFlyoutPresenterStyle"] };
        var normalBrush = App.GetBrush("AppTextSecondaryBrush");

        MenuFlyoutItem MakeItem(string text)
        {
            var item = new MenuFlyoutItem { Style = (Style)Application.Current.Resources["GlassMenuFlyoutItemStyle"], Text = text, Foreground = normalBrush };
            return item;
        }

        var restore = MakeItem(App.GetString("Setting_Backup_Restore"));
        var delete = MakeItem(App.GetString("Setting_Backup_Delete"));
        restore.Click += (_, _) => ShowBackupRestoreDialog();
        delete.Click += (_, _) => ShowBackupDeleteDialog();

        menu.Items.Add(restore); menu.Items.Add(delete);
        menu.ShowAt(BackupManageButton, new Windows.Foundation.Point(0, BackupManageButton.ActualHeight + 4));
    }

    
    private void BackupModeButton_Click(object sender, RoutedEventArgs e)
    {
        var menu = new MenuFlyout { MenuFlyoutPresenterStyle = (Style)Application.Current.Resources["GlassMenuFlyoutPresenterStyle"] };
        var normalBrush = App.GetBrush("AppTextSecondaryBrush");

        MenuFlyoutItem MakeItem(string text)
        {
            var item = new MenuFlyoutItem { Style = (Style)Application.Current.Resources["GlassMenuFlyoutItemStyle"], Text = text, Foreground = normalBrush };
            return item;
        }

        var now = MakeItem(App.GetString("Setting_Backup_Now"));
        var auto = MakeItem(App.GetString("Setting_Backup_Auto"));
        now.Click += (_, _) => ShowBackupNowDialog();
        auto.Click += (_, _) => ShowBackupAutoDialog();

        menu.Items.Add(now); menu.Items.Add(auto);
        menu.ShowAt(BackupModeButton, new Windows.Foundation.Point(0, BackupModeButton.ActualHeight + 4));
    }

    
    private void BackupAutoModeButton_Click(object sender, RoutedEventArgs e)
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

        var onItem = MakeItem(App.GetString("Setting_Backup_On"), _autoBackupEnabled);
        var offItem = MakeItem(App.GetString("Setting_Backup_Off"), !_autoBackupEnabled);
        onItem.Click += (_, _) => SetAutoBackupEnabled(true);
        offItem.Click += (_, _) => SetAutoBackupEnabled(false);

        menu.Items.Add(onItem); menu.Items.Add(offItem);
        menu.ShowAt(BackupAutoModeButton, new Windows.Foundation.Point(0, BackupAutoModeButton.ActualHeight + 4));
    }

    private void SetAutoBackupEnabled(bool on)
    {
        _autoBackupEnabled = on;
        UpdateBackupAutoModeButton();
        App.ShowToast(App.GetString("Common_Toast_Switched"));
        
        BackupFreqRow.Opacity = on ? 1.0 : 0.35;
        BackupFreqButton.IsEnabled = on;
    }

    private void UpdateBackupAutoModeButton()
    {
        ((TextBlock)BackupAutoModeText).Text = _autoBackupEnabled
            ? App.GetString("Setting_Backup_On")
            : App.GetString("Setting_Backup_Off");
        ApplyToggleState(BackupAutoModeButton, _autoBackupEnabled); 
    }

    private void BackupFreqButton_Click(object sender, RoutedEventArgs e)
    {
        var menu = new MenuFlyout { MenuFlyoutPresenterStyle = (Style)Application.Current.Resources["GlassMenuFlyoutPresenterStyle"] };
        var accentBrush = App.GetBrush("AppTextPrimaryBrush");
        var normalBrush = App.GetBrush("AppTextSecondaryBrush");

        MenuFlyoutItem MakeItem(int idx, string text)
        {
            bool active = _backupFreqIndex == idx;
            var item = new MenuFlyoutItem { Style = (Style)Application.Current.Resources["GlassMenuFlyoutItemStyle"], Text = text, Foreground = active ? accentBrush : normalBrush };
            if (active) item.Icon = new FontIcon { Glyph = "\uE73E", FontSize = 12, Foreground = accentBrush };
            return item;
        }

        var m30 = MakeItem(0, App.GetString("Setting_Backup_Freq_30m"));
        var m1h = MakeItem(1, App.GetString("Setting_Backup_Freq_1h"));
        var m6h = MakeItem(2, App.GetString("Setting_Backup_Freq_6h"));
        var m1d = MakeItem(3, App.GetString("Setting_Backup_Freq_1d"));

        void Select(int idx) { _backupFreqIndex = idx; BackupFreqButtonText.Text = GetBackupFreqLabel(idx); }
        m30.Click += (_, _) => Select(0);
        m1h.Click += (_, _) => Select(1);
        m6h.Click += (_, _) => Select(2);
        m1d.Click += (_, _) => Select(3);

        menu.Items.Add(m30); menu.Items.Add(m1h); menu.Items.Add(m6h); menu.Items.Add(m1d);
        menu.ShowAt(BackupFreqButton, new Windows.Foundation.Point(0, BackupFreqButton.ActualHeight + 4));
    }

    private void BackupAutoConfirm_Click(object sender, RoutedEventArgs e)
    {
        PersistBackupSettings(); 
        UpdateBackupUI(); 
        HideBackupAutoDialog();
    }

    
    private static Grid? FindOverlayScrim(Grid overlay)
        => overlay.Children.OfType<Grid>().FirstOrDefault(g => g.Name.EndsWith("Scrim", StringComparison.Ordinal));

    private void ShowOverlay(Grid overlay, Border dialog, CompositeTransform transform)
    {
        var scrim = FindOverlayScrim(overlay);
        transform.ScaleX = 0.92; transform.ScaleY = 0.92; transform.TranslateY = 20;
        if (scrim != null) scrim.Opacity = 0; // ND3: scrims are #2E000000 now - without a fade they hard-cut dark while ChromeScrim stays off
        dialog.Opacity = 0;
        overlay.Visibility = Visibility.Visible;
        var sb = new Storyboard();
        if (scrim != null)
        {
            var si = new DoubleAnimation { To = 1, Duration = TimeSpan.FromMilliseconds(250) };
            Storyboard.SetTarget(si, scrim); Storyboard.SetTargetProperty(si, "Opacity"); sb.Children.Add(si);
        }
        var di = new DoubleAnimation { To = 1, Duration = TimeSpan.FromMilliseconds(300) };
        Storyboard.SetTarget(di, dialog); Storyboard.SetTargetProperty(di, "Opacity"); sb.Children.Add(di);
        foreach (var (v, p) in new[] { (1.0, "ScaleX"), (1.0, "ScaleY"), (0.0, "TranslateY") })
        {
            var a = new DoubleAnimation { To = v, Duration = TimeSpan.FromMilliseconds(400), EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
            Storyboard.SetTarget(a, transform); Storyboard.SetTargetProperty(a, p); sb.Children.Add(a);
        }
        sb.Begin();
    }

    private void HideOverlay(Grid overlay, Border dialog, CompositeTransform transform, Action onCompleted)
    {
        var scrim = FindOverlayScrim(overlay);
        var sb = new Storyboard();
        if (scrim != null)
        {
            var so = new DoubleAnimation { To = 0, Duration = TimeSpan.FromMilliseconds(200) };
            Storyboard.SetTarget(so, scrim); Storyboard.SetTargetProperty(so, "Opacity"); sb.Children.Add(so);
        }
        var di = new DoubleAnimation { To = 0, Duration = TimeSpan.FromMilliseconds(200) };
        Storyboard.SetTarget(di, dialog); Storyboard.SetTargetProperty(di, "Opacity"); sb.Children.Add(di);
        foreach (var (v, p) in new[] { (0.92, "ScaleX"), (0.92, "ScaleY"), (20.0, "TranslateY") })
        {
            var a = new DoubleAnimation { To = v, Duration = TimeSpan.FromMilliseconds(250), EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
            Storyboard.SetTarget(a, transform); Storyboard.SetTargetProperty(a, p); sb.Children.Add(a);
        }
        sb.Completed += (_, _) => { overlay.Visibility = Visibility.Collapsed; onCompleted(); };
        sb.Begin();
    }

    
    private void ShowBackupIntroDialog() => ShowOverlay(BackupIntroOverlay, BackupIntroDialog, BackupIntroDialogTransform);
    private void HideBackupIntroDialog()
    {
        if (_animBackupIntro) return;
        _animBackupIntro = true;
        HideOverlay(BackupIntroOverlay, BackupIntroDialog, BackupIntroDialogTransform, () => _animBackupIntro = false);
    }
    private void BackupIntroConfirm_Click(object sender, RoutedEventArgs e) { HideBackupIntroDialog(); EnableBackup(); }
    private void BackupIntroClose_Click(object sender, RoutedEventArgs e) => HideBackupIntroDialog();
    private void BackupIntroScrim_Tapped(object sender, TappedRoutedEventArgs e)
    { if (ReferenceEquals(e.OriginalSource, BackupIntroScrim)) HideBackupIntroDialog(); }

    
    private void ShowBackupNowDialog() => ShowOverlay(BackupNowOverlay, BackupNowDialog, BackupNowDialogTransform);
    private void HideBackupNowDialog()
    {
        if (_animBackupNow) return;
        _animBackupNow = true;
        HideOverlay(BackupNowOverlay, BackupNowDialog, BackupNowDialogTransform, () => _animBackupNow = false);
    }
    private void BackupNowConfirm_Click(object sender, RoutedEventArgs e)
    {
        var snapshot = AutoBackupService.CreateSnapshot(isManual: true);
        HideBackupNowDialog();
        UpdateBackupUI();
        if (snapshot == null) { App.ShowToast(App.GetString("Common_Toast_Failed")); return; } // N4S-05: disk full / file locked must not report success
        App.ShowToast(App.GetString("Common_Toast_Created"));
    }
    private void BackupNowCancel_Click(object sender, RoutedEventArgs e) => HideBackupNowDialog();
    private void BackupNowClose_Click(object sender, RoutedEventArgs e) => HideBackupNowDialog();
    private void BackupNowScrim_Tapped(object sender, TappedRoutedEventArgs e)
    { if (ReferenceEquals(e.OriginalSource, BackupNowScrim)) HideBackupNowDialog(); }

    
    private bool _draftAutoBackup;   
    private int _draftBackupFreq;     

    private void ShowBackupAutoDialog()
    {
        _draftAutoBackup = _autoBackupEnabled;
        _draftBackupFreq = _backupFreqIndex;
        UpdateBackupAutoModeButton();
        BackupFreqRow.Opacity = _autoBackupEnabled ? 1.0 : 0.35;
        BackupFreqButton.IsEnabled = _autoBackupEnabled;
        ShowOverlay(BackupAutoOverlay, BackupAutoDialog, BackupAutoDialogTransform);
    }
    
    private void RevertBackupAutoDraft()
    {
        _autoBackupEnabled = _draftAutoBackup;
        _backupFreqIndex = _draftBackupFreq;
        BackupFreqButtonText.Text = GetBackupFreqLabel(_backupFreqIndex);
    }
    private void HideBackupAutoDialog()
    {
        if (_animBackupAuto) return;
        _animBackupAuto = true;
        HideOverlay(BackupAutoOverlay, BackupAutoDialog, BackupAutoDialogTransform, () => _animBackupAuto = false);
    }
    private void BackupAutoCancel_Click(object sender, RoutedEventArgs e)
    {
        RevertBackupAutoDraft();
        HideBackupAutoDialog();
        UpdateBackupUI();
    }
    private void BackupAutoClose_Click(object sender, RoutedEventArgs e)
    {
        RevertBackupAutoDraft();
        HideBackupAutoDialog();
        UpdateBackupUI();
    }
    private void BackupAutoScrim_Tapped(object sender, TappedRoutedEventArgs e)
    { if (ReferenceEquals(e.OriginalSource, BackupAutoScrim)) { RevertBackupAutoDraft(); HideBackupAutoDialog(); UpdateBackupUI(); } }

    
    private void ShowBackupRestoreDialog()
    {
        BuildBackupList(BackupRestoreList, singleSelect: true);
        ShowOverlay(BackupRestoreOverlay, BackupRestoreDialog, BackupRestoreDialogTransform);
    }
    private void HideBackupRestoreDialog()
    {
        if (_animBackupRestore) return;
        _animBackupRestore = true;
        HideOverlay(BackupRestoreOverlay, BackupRestoreDialog, BackupRestoreDialogTransform, () => _animBackupRestore = false);
    }
    private void BackupRestoreConfirm_Click(object sender, RoutedEventArgs e)
    {
        
        _pendingRestoreSnapshot = GetCheckedSnapshot(BackupRestoreList);
        if (string.IsNullOrEmpty(_pendingRestoreSnapshot)) return; 
        
        HideBackupRestoreDialog();
        ShowBackupRestoreConfirmDialog();
    }
    private void BackupRestoreCancel_Click(object sender, RoutedEventArgs e) => HideBackupRestoreDialog();
    private void BackupRestoreClose_Click(object sender, RoutedEventArgs e) => HideBackupRestoreDialog();
    private void BackupRestoreScrim_Tapped(object sender, TappedRoutedEventArgs e)
    { if (ReferenceEquals(e.OriginalSource, BackupRestoreScrim)) HideBackupRestoreDialog(); }

    
    private void ShowBackupRestoreConfirmDialog() => ShowOverlay(BackupRestoreConfirmOverlay, BackupRestoreConfirmDialog, BackupRestoreConfirmDialogTransform);
    private void HideBackupRestoreConfirmDialog()
    {
        if (_animBackupRestoreConfirm) return;
        _animBackupRestoreConfirm = true;
        HideOverlay(BackupRestoreConfirmOverlay, BackupRestoreConfirmDialog, BackupRestoreConfirmDialogTransform, () => _animBackupRestoreConfirm = false);
    }
    private void CancelBackupRestoreConfirm()
    {
        _pendingRestoreSnapshot = null; 
        HideBackupRestoreConfirmDialog();
    }
    private void BackupRestoreConfirmOk_Click(object sender, RoutedEventArgs e)
    {
        HideBackupRestoreConfirmDialog();
        if (string.IsNullOrEmpty(_pendingRestoreSnapshot)) return;
        if (AutoBackupService.Restore(_pendingRestoreSnapshot))
        {
            _pendingRestoreSnapshot = null;
            UpdateBackupUI();
            ShowBackupRestartDialog(); 
        }
        else
        {
            App.ShowToast(App.GetString("Store_Err_IoFail"));
        }
    }
    private void BackupRestoreConfirmCancel_Click(object sender, RoutedEventArgs e) => CancelBackupRestoreConfirm();
    private void BackupRestoreConfirmClose_Click(object sender, RoutedEventArgs e) => CancelBackupRestoreConfirm();
    private void BackupRestoreConfirmScrim_Tapped(object sender, TappedRoutedEventArgs e)
    { if (ReferenceEquals(e.OriginalSource, BackupRestoreConfirmScrim)) CancelBackupRestoreConfirm(); }

    
    private void ShowBackupDeleteDialog()
    {
        BuildBackupList(BackupDeleteList, singleSelect: false);
        ShowOverlay(BackupDeleteOverlay, BackupDeleteDialog, BackupDeleteDialogTransform);
    }
    private void HideBackupDeleteDialog()
    {
        if (_animBackupDelete) return;
        _animBackupDelete = true;
        HideOverlay(BackupDeleteOverlay, BackupDeleteDialog, BackupDeleteDialogTransform, () => _animBackupDelete = false);
    }
    private void BackupDeleteConfirm_Click(object sender, RoutedEventArgs e)
    {
        _pendingDeleteIsClearAll = false;
        _pendingDeleteSnapshots.Clear();
        _pendingDeleteSnapshots.AddRange(GetCheckedSnapshots(BackupDeleteList));
        if (_pendingDeleteSnapshots.Count == 0) return; 
        HideBackupDeleteDialog();
        ShowBackupDeleteConfirmDialog();
    }
    private void BackupDeleteClearAll_Click(object sender, RoutedEventArgs e)
    {
        _pendingDeleteIsClearAll = true;
        HideBackupDeleteDialog();
        ShowBackupDeleteConfirmDialog();
    }
    private void BackupDeleteClose_Click(object sender, RoutedEventArgs e) => HideBackupDeleteDialog();
    private void BackupDeleteScrim_Tapped(object sender, TappedRoutedEventArgs e)
    { if (ReferenceEquals(e.OriginalSource, BackupDeleteScrim)) HideBackupDeleteDialog(); }

    
    private void ShowBackupDeleteConfirmDialog()
    {
        BackupDeleteConfirmTipText.Text = _pendingDeleteIsClearAll
            ? App.GetString("Setting_Backup_ClearAllConfirm_Tip")
            : App.GetString("Setting_Backup_DeleteConfirm_Tip");
        ShowOverlay(BackupDeleteConfirmOverlay, BackupDeleteConfirmDialog, BackupDeleteConfirmDialogTransform);
    }
    private void HideBackupDeleteConfirmDialog()
    {
        if (_animBackupDeleteConfirm) return;
        _animBackupDeleteConfirm = true;
        HideOverlay(BackupDeleteConfirmOverlay, BackupDeleteConfirmDialog, BackupDeleteConfirmDialogTransform, () => _animBackupDeleteConfirm = false);
    }
    private void BackupDeleteConfirmOk_Click(object sender, RoutedEventArgs e)
    {
        HideBackupDeleteConfirmDialog();
        if (_pendingDeleteIsClearAll)
        {
            AutoBackupService.ClearAll();
            _pendingDeleteSnapshots.Clear(); 
        }
        else
        {
            AutoBackupService.DeleteSnapshots(_pendingDeleteSnapshots);
            _pendingDeleteSnapshots.Clear();
        }
        UpdateBackupUI();
        App.ShowToast(App.GetString("Common_Toast_Deleted"));
    }
    private void BackupDeleteConfirmCancel_Click(object sender, RoutedEventArgs e) => HideBackupDeleteConfirmDialog();
    private void BackupDeleteConfirmClose_Click(object sender, RoutedEventArgs e) => HideBackupDeleteConfirmDialog();
    private void BackupDeleteConfirmScrim_Tapped(object sender, TappedRoutedEventArgs e)
    { if (ReferenceEquals(e.OriginalSource, BackupDeleteConfirmScrim)) HideBackupDeleteConfirmDialog(); }

    
    private void ShowBackupRestartDialog() => ShowOverlay(BackupRestartOverlay, BackupRestartDialog, BackupRestartDialogTransform);
    private void BackupRestartOk_Click(object sender, RoutedEventArgs e)
    {
        RestartApp(rollbackOnFail: true);
    }

    
    private void RestartApp(bool rollbackOnFail = false)
    {
        if (_restarting) return;
        _restarting = true;
        try
        {
            var exe = Environment.ProcessPath;
            if (string.IsNullOrEmpty(exe)) { _restarting = false; HandleRestartFailure(rollbackOnFail); return; }
            Novara.MainWindow.ReleaseSingleInstance();
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo { FileName = exe, UseShellExecute = true });
        }
        catch
        {
            System.Diagnostics.Debug.WriteLine("Novara 重启闭环：新进程启动失败，保持当前会话");
            _restarting = false;
            HandleRestartFailure(rollbackOnFail);
            return;
        }
        if (App.MainWindow is { } mw) { mw.SetRestarting(); mw.ExitApp(saveFirst: false); }
    }

    
    private void HandleRestartFailure(bool rollbackOnFail)
    {
        if (!rollbackOnFail) return;
        BackupRestartOverlay.Visibility = Visibility.Collapsed;
        if (AutoBackupService.RollbackLastRestore())
        {
            UpdateBackupUI();
            App.ShowToast(App.GetString("Setting_Backup_RestartFail_Rollback"));
        }
        else
        {
            App.ShowToast(App.GetString("Setting_Backup_RestartFail_RollbackFail"));
        }
    }

    
    private void BuildBackupList(StackPanel list, bool singleSelect)
    {
        list.Children.Clear();
        var snapshots = AutoBackupService.ListSnapshots();
        if (snapshots.Count == 0)
        {
            var empty = new TextBlock
            {
                Text = App.GetString("Setting_Backup_Empty_Tip"),
                FontSize = 13,
                Foreground = App.GetBrush("AppTextTertiaryBrush"),
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 12, 0, 12),
            };
            list.Children.Add(empty);
            return;
        }

        var boxes = new List<CheckBox>();
        foreach (var s in snapshots)
        {
            var row = new Grid { Height = 40 };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var check = new CheckBox
            {
                MinWidth = 0,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 12, 0),
                Tag = s.FileName,
            };
            boxes.Add(check);
            if (singleSelect)
            {
                check.Checked += (_, _) =>
                {
                    foreach (var b in boxes)
                        if (!ReferenceEquals(b, check)) b.IsChecked = false;
                };
            }
            Grid.SetColumn(check, 0);
            row.Children.Add(check);

            var text = new TextBlock
            {
                Text = $"{s.Timestamp:yyyy-MM-dd HH:mm:ss}   ·   {FormatSize(s.SizeBytes)}",
                FontSize = 14,
                Foreground = App.GetBrush("AppTextPrimaryBrush"),
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
            };
            Grid.SetColumn(text, 1);
            row.Children.Add(text);

            var tagText = new TextBlock
            {
                Text = s.IsManual
                    ? App.GetString("Setting_Backup_Tag_Manual")
                    : App.GetString("Setting_Backup_Tag_Auto"),
                FontSize = 12,
                Foreground = App.GetBrush("AppPrimaryButtonBrush"),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(12, 0, 0, 0),
            };
            Grid.SetColumn(tagText, 2);
            row.Children.Add(tagText);

            list.Children.Add(row);
        }
    }

    private static string FormatSize(long bytes)
    {
        if (bytes < 1024) return $"{bytes} B";
        if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F1} KB";
        return $"{bytes / (1024.0 * 1024.0):F1} MB";
    }

    private static string? GetCheckedSnapshot(StackPanel list)
    {
        foreach (var child in list.Children)
        {
            if (child is Grid g && g.Children.Count > 0 && g.Children[0] is CheckBox cb && cb.IsChecked == true)
                return cb.Tag as string;
        }
        return null;
    }

    private static List<string> GetCheckedSnapshots(StackPanel list)
    {
        var result = new List<string>();
        foreach (var child in list.Children)
        {
            if (child is Grid g && g.Children.Count > 0 && g.Children[0] is CheckBox cb && cb.IsChecked == true && cb.Tag is string name)
                result.Add(name);
        }
        return result;
    }

    

    private void StatsToggleButton_Click(object sender, RoutedEventArgs e)
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

        var onItem = MakeItem(App.GetString("Setting_Stats_On"), _statsEnabled);
        var offItem = MakeItem(App.GetString("Setting_Stats_Off"), !_statsEnabled);

        onItem.Click += (_, _) =>
        {
            if (_statsEnabled) return;
            _statsEnabled = true;
            _statsExpanded = true; 
            PersistSetting(s => s.StatsEnabled = true);
            UpdateStatsUI();
            App.ShowToast(App.GetString("Common_Toast_Switched"));
        };
        offItem.Click += (_, _) =>
        {
            if (!_statsEnabled) return;
            _statsEnabled = false;
            PersistSetting(s => s.StatsEnabled = false);
            UpdateStatsUI();
            App.ShowToast(App.GetString("Common_Toast_Switched"));
        };

        menu.Items.Add(onItem); menu.Items.Add(offItem);
        menu.ShowAt(StatsToggleButton, new Windows.Foundation.Point(0, StatsToggleButton.ActualHeight + 4));
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

    private void StatsExpandButton_Click(object sender, RoutedEventArgs e)
    {
        _statsExpanded = !_statsExpanded;
        UpdateStatsUI();
    }

    private void UpdateStatsUI()
    {
        
        ((TextBlock)StatsToggleButton.Content).Text = _statsEnabled
            ? App.GetString("Setting_Stats_On")
            : App.GetString("Setting_Stats_Off");
        ApplyToggleState(StatsToggleButton, _statsEnabled);

        
        StatsExpandButton.Visibility = _statsEnabled ? Visibility.Visible : Visibility.Collapsed;
        StatsExpandIcon.Data = _statsExpanded
            ? App.CreateGeometry(IconData.CardCollapse)
            : App.CreateGeometry(IconData.CardExpand);

        
        if (_statsEnabled && _statsExpanded)
        {
            StatsDetailPanel.Visibility = Visibility.Visible;
            BuildStatsPanel();
            Services.MeltAnim.Begin(StatsDetailPanel, true, 16.0);
        }
        else if (_statsEnabled)
        {
            Services.MeltAnim.Begin(StatsDetailPanel, false, 16.0); 
            StatsGrid.Children.Clear();
        }
        else
        {
            StatsDetailPanel.Visibility = Visibility.Collapsed;
            StatsGrid.Children.Clear();
        }
    }

    private void BuildStatsPanel()
    {
        var db = App.Store?.Database;
        if (db == null) { StatsGrid.Children.Clear(); return; }

        var entries = db.MemoEntries.Where(x => !x.IsDeleted).ToList();
        var paths = db.PathBackupItems.Where(x => !x.IsDeleted).ToList();
        var todos = db.TodoCards.Where(x => !x.IsDeleted).ToList();
        var notes = db.NoteCards.Where(x => !x.IsDeleted).ToList();
        var diaries = db.DiaryItems.Where(x => !x.IsDeleted && x.Format != "markdown").ToList(); 
        var documents = db.DiaryItems.Where(x => !x.IsDeleted && x.Format == "markdown").ToList(); 

        
        double completion = 0;
        if (todos.Count > 0)
        {
            int done = todos.Count(t => t.CheckedStates is { Count: > 0 } cs && cs.All(c => c));
            completion = (double)done / todos.Count * 100.0;
        }

        
        string storage = "—";
        try
        {
            var f = new System.IO.FileInfo(Novara.Services.NovaraStore.DefaultFilePath);
            if (f.Exists)
            {
                double b = f.Length;
                storage = b >= 1024 * 1024 ? $"{b / (1024.0 * 1024.0):F1} MB"
                          : b >= 1024 ? $"{b / 1024.0:F1} KB"
                          : $"{b} B";
            }
        }
        catch { }

        
        var times = new List<DateTime>();
        times.AddRange(diaries.Select(d => d.ModifiedAt));
        times.AddRange(documents.Select(d => d.ModifiedAt));
        times.AddRange(entries.Select(e => e.CreatedAt));
        times.AddRange(paths.Select(p => p.CreatedAt));
        times.AddRange(todos.Select(t => t.CreatedAt));
        times.AddRange(notes.Select(n => n.CreatedAt));
        string lastActive = times.Count > 0 ? times.Max().ToString("yyyy-MM-dd HH:mm") : App.GetString("Setting_Stats_Never");

        
        bool showMemo = _visibleTabs.Contains("备忘");
        bool showFile = _visibleTabs.Contains("文件");
        bool showPlan = _visibleTabs.Contains("计划");
        bool showDiary = _visibleTabs.Contains("日记");

        var blocks = new List<(string label, string value, bool show)>
        {
            (App.GetString("Setting_Stats_MemoEntries"), entries.Count.ToString(), showMemo),
            (App.GetString("Setting_Stats_Groups"), db.MemoGroups.Count.ToString(), showMemo),
            (App.GetString("Setting_Stats_Paths"), paths.Count.ToString(), showFile),
            (App.GetString("Setting_Stats_Todos"), todos.Count.ToString(), showPlan),
            (App.GetString("Setting_Stats_Notes"), notes.Count.ToString(), showPlan),
            (App.GetString("Setting_Stats_Diaries"), diaries.Count.ToString(), showDiary),
            (App.GetString("Setting_Stats_Documents"), documents.Count.ToString(), showDiary),
            (App.GetString("Setting_Stats_Completion"), $"{completion:F0}%", showPlan),
            (App.GetString("Setting_Stats_Storage"), storage, true),
            (App.GetString("Setting_Stats_Starred"),
                (entries.Count(e => e.IsStarred) + paths.Count(p => p.IsStarred) + todos.Count(t => t.IsStarred) + notes.Count(n => n.IsStarred) + diaries.Count(d => d.IsStarred) + documents.Count(d => d.IsStarred)).ToString(), true),
            (App.GetString("Setting_Stats_Trash"),
                (db.MemoEntries.Count(x => x.IsDeleted) + db.PathBackupItems.Count(x => x.IsDeleted) + db.TodoCards.Count(x => x.IsDeleted) + db.NoteCards.Count(x => x.IsDeleted) + db.DiaryItems.Count(x => x.IsDeleted)).ToString(), true),
            (App.GetString("Setting_Stats_LastActive"), lastActive, true),
        };

        var visible = blocks.Where(b => b.show).ToList();

        StatsGrid.Children.Clear();
        if (visible.Count == 0) return;

        
        int cols = 3;
        int rows = (visible.Count + cols - 1) / cols;
        StatsGrid.ColumnDefinitions.Clear();
        StatsGrid.RowDefinitions.Clear();
        for (int c = 0; c < cols; c++) StatsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        for (int r = 0; r < rows; r++) StatsGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        for (int i = 0; i < visible.Count; i++)
        {
            var (label, value, _) = visible[i];
            var block = new Border
            {
                Padding = new Thickness(16, 14, 16, 14),
                Margin = new Thickness(i % cols == 0 ? 0 : 6, i / cols == 0 ? 0 : 6, i % cols == cols - 1 ? 0 : 6, 0),
                Background = App.GetBrush("AppSurfaceBrush"),
                CornerRadius = new CornerRadius(10),
            };
            var sp = new StackPanel { Spacing = 6 };
            var valueText = new TextBlock
            {
                Text = value,
                FontSize = 22,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Foreground = App.GetBrush("AppPrimaryButtonBrush"),
            };
            var labelText = new TextBlock
            {
                Text = label,
                FontSize = 12,
                Foreground = App.GetBrush("AppTextSecondaryBrush"),
                TextTrimming = TextTrimming.CharacterEllipsis,
            };
            sp.Children.Add(valueText);
            sp.Children.Add(labelText);
            block.Child = sp;
            Grid.SetColumn(block, i % cols);
            Grid.SetRow(block, i / cols);
            StatsGrid.Children.Add(block);
        }
    }

    

    private void UpdateMcpUI()
    {
        var settings = App.Store?.Database.AppSettings;
        if (settings == null) return;

        bool enabled = settings.McpEnabled;
        bool expanded = settings.McpDetailExpanded;

        
        McpTitleText.Text = App.GetString("Setting_Mcp_Title");
        McpAuditButtonText.Text = App.GetString("Setting_Mcp_Audit_Button");
        McpConfigButtonText.Text = App.GetString("Setting_Mcp_Config");
        McpToggleText.Text = App.GetString(enabled ? "Setting_Autostart_On" : "Setting_Autostart_Off");
        McpNoAuthorizedText.Text = App.GetString("Setting_Mcp_NoAuthorized");

        
        ApplyToggleState(McpToggleButton, enabled);

        
        McpConfigButton.Visibility = enabled ? Visibility.Visible : Visibility.Collapsed;
        McpAuditButton.Visibility = enabled ? Visibility.Visible : Visibility.Collapsed;
        McpExpandButton.Visibility = enabled ? Visibility.Visible : Visibility.Collapsed;
        McpExpandIcon.Data = expanded
            ? App.CreateGeometry(IconData.CardCollapse)
            : App.CreateGeometry(IconData.CardExpand);

        
        if (enabled && expanded) { BuildMcpAuthorizedList(); Services.MeltAnim.Begin(McpDetailPanel, true, 16.0); }
        else if (enabled) { Services.MeltAnim.Begin(McpDetailPanel, false, 16.0); }
        else { McpDetailPanel.Visibility = Visibility.Collapsed; }

        
        McpConfigTitleText.Text = App.GetString("Setting_Mcp_Config_Title");
        McpKeyLabelText.Text = App.GetString("Setting_Mcp_Key");
        McpResetButtonText.Text = App.GetString("Setting_Mcp_Reset");
        McpJsonLabelText.Text = App.GetString("Setting_Mcp_Json");
        McpCopyButtonText.Text = App.GetString("Setting_Mcp_Copy");
        McpDeletePermissionLabelText.Text = App.GetString("Setting_Mcp_DeletePermission");
        McpConfigDoneText.Text = App.GetString("Common_Button_Confirm");
        McpResetConfirmTitleText.Text = App.GetString("Setting_Mcp_Reset_Title");
        McpResetConfirmMessageText.Text = App.GetString("Setting_Mcp_Reset_Message");
        McpResetConfirmCancelText.Text = App.GetString("Common_Button_Cancel");
        McpResetConfirmOkText.Text = App.GetString("Setting_Mcp_Reset_Confirm");
        McpRevokeConfirmTitleText.Text = App.GetString("Setting_Mcp_RevokeConfirm_Title");
        McpRevokeConfirmMessageText.Text = App.GetString("Setting_Mcp_RevokeConfirm_Message");
        McpRevokeConfirmCancelText.Text = App.GetString("Common_Button_Cancel");
        McpRevokeConfirmOkText.Text = App.GetString("Setting_Mcp_Revoke");
        McpCopyTitleText.Text = App.GetString("Setting_Mcp_Copy_Title");
        McpCopyDescText.Text = App.GetString("Setting_Mcp_Copy_Desc");
        McpCopyJsonButtonText.Text = App.GetString("Setting_Mcp_Json");
        McpCopyHintButtonText.Text = App.GetString("Setting_Mcp_Copy_Hint_Short");
        McpDeleteConfirmTitleText.Text = App.GetString("Setting_Mcp_DeleteConfirm_Title");
        McpDeleteConfirmMessageText.Text = App.GetString("Setting_Mcp_DeleteConfirm_Message");
        McpDeleteConfirmCancelText.Text = App.GetString("Common_Button_Cancel");
        McpDeleteConfirmOkText.Text = App.GetString("Setting_Mcp_DeleteConfirm_Ok");

        
        McpAuditTitleText.Text = App.GetString("Setting_Mcp_Audit_Title");
        McpAuditClearButtonText.Text = App.GetString("Setting_Mcp_Audit_Clear");
        McpAuditCloseButtonText.Text = App.GetString("Setting_Mcp_Audit_Close");
        McpAuditEmptyText.Text = App.GetString("Setting_Mcp_Audit_Empty");
        McpAuditClearConfirmTitleText.Text = App.GetString("Setting_Mcp_Audit_ClearConfirm_Title");
        McpAuditClearConfirmMessageText.Text = App.GetString("Setting_Mcp_Audit_ClearConfirm_Message");
        McpAuditClearConfirmCancelText.Text = App.GetString("Common_Button_Cancel");
        McpAuditClearConfirmOkText.Text = App.GetString("Setting_Mcp_Audit_ClearConfirm_Ok");

        UpdateMcpDeletePermissionButton(settings.McpDeleteEnabled);
    }

    private void McpToggleButton_Click(object sender, RoutedEventArgs e)
    {
        var settings = App.Store?.Database.AppSettings;
        if (settings == null) return;

        var menu = new MenuFlyout { MenuFlyoutPresenterStyle = (Style)Application.Current.Resources["GlassMenuFlyoutPresenterStyle"] };
        var accentBrush = App.GetBrush("AppTextPrimaryBrush");
        var normalBrush = App.GetBrush("AppTextSecondaryBrush");

        MenuFlyoutItem MakeItem(string text, bool active)
        {
            var item = new MenuFlyoutItem { Style = (Style)Application.Current.Resources["GlassMenuFlyoutItemStyle"], Text = text, Foreground = active ? accentBrush : normalBrush };
            if (active) item.Icon = new FontIcon { Glyph = "\uE73E", FontSize = 12, Foreground = accentBrush };
            return item;
        }

        var onItem = MakeItem(App.GetString("Setting_Autostart_On"), settings.McpEnabled);
        var offItem = MakeItem(App.GetString("Setting_Autostart_Off"), !settings.McpEnabled);

        onItem.Click += (_, _) => SetMcpEnabled(true);
        offItem.Click += (_, _) => SetMcpEnabled(false);

        menu.Items.Add(onItem); menu.Items.Add(offItem);
        menu.ShowAt(McpToggleButton, new Windows.Foundation.Point(0, McpToggleButton.ActualHeight + 4));
    }

    private void SetMcpEnabled(bool enabled)
    {
        var settings = App.Store?.Database.AppSettings;
        if (settings == null) return;
        settings.McpEnabled = enabled;
        if (enabled && string.IsNullOrEmpty(settings.McpToken))
        {
            
            settings.McpToken = GenerateToken();
            settings.McpTokenGeneratedAt = DateTime.Now;
        }
        App.Store?.SaveAsync();
        UpdateMcpUI();
        App.ShowToast(App.GetString("Common_Toast_Switched"));
    }

    private void McpExpandButton_Click(object sender, RoutedEventArgs e)
    {
        var settings = App.Store?.Database.AppSettings;
        if (settings == null) return;
        settings.McpDetailExpanded = !settings.McpDetailExpanded;
        App.Store?.SaveAsync();
        UpdateMcpUI();
    }

    private void McpConfigButton_Click(object sender, RoutedEventArgs e)
    {
        UpdateMcpUI(); 
        ShowMcpConfigDialog();
    }

    private static string GenerateToken()
    {
        var bytes = RandomNumberGenerator.GetBytes(32);
        return Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_'); // Base64Url
    }

    private static string FindNovaraMcpExe()
    {
        var sideBySide = Path.Combine(AppContext.BaseDirectory, "NovaraMCP.exe");
        if (File.Exists(sideBySide)) return sideBySide;
        
        for (var dir = Path.GetFullPath(AppContext.BaseDirectory); ; )
        {
            var candidate = Path.Combine(dir, "NovaraMCP", "bin", "Debug", "net8.0", "win-x64", "NovaraMCP.exe");
            if (File.Exists(candidate)) return candidate;
            var parent = Directory.GetParent(dir)?.FullName;
            if (string.IsNullOrEmpty(parent) || parent == dir) break;
            dir = parent;
        }
        return sideBySide;
    }

    private void CopyToClipboard(string text)
    {
        var dp = new DataPackage();
        dp.SetText(text);
        try { Clipboard.SetContent(dp); } catch { }
        App.ShowToast(App.GetString("Common_Toast_Copied"));
    }

    private void BuildMcpAuthorizedList()
    {
        McpAuthorizedList.Children.Clear();
        var list = Novara.Services.McpService.GetAuthorizedSnapshot(); // N4C-02: locked snapshot instead of iterating the live List
        if (list.Count == 0)
        {
            McpAuthorizedListHost.MinHeight = 120; 
            McpNoAuthorizedText.Visibility = Visibility.Visible;
            return;
        }
        McpAuthorizedListHost.MinHeight = 0; 
        McpNoAuthorizedText.Visibility = Visibility.Collapsed;
        foreach (var path in list)
        {
            var row = new Grid { Margin = new Thickness(0, 0, 0, 6) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var pathText = new TextBlock
            {
                Text = path,
                FontSize = 12,
                Foreground = App.GetBrush("AppTextPrimaryBrush"),
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
            };
            Grid.SetColumn(pathText, 0);
            row.Children.Add(pathText);

            var revoke = new Button
            {
                Content = new TextBlock { Text = App.GetString("Setting_Mcp_Revoke"), FontSize = 12 },
                Height = 28,
                MinWidth = 80,
                Padding = new Thickness(10, 0, 10, 0),
                Style = (Style)Resources["McpDangerButtonStyle"], 
                CornerRadius = new CornerRadius(8),
                VerticalAlignment = VerticalAlignment.Center,
                Tag = path,
            };
            revoke.Click += McpRevokeButton_Click;
            Grid.SetColumn(revoke, 1);
            row.Children.Add(revoke);

            McpAuthorizedList.Children.Add(row);
        }
    }

    private void McpRevokeButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string path)
        {
            _pendingRevokePath = path;
            ShowMcpRevokeConfirmDialog();
        }
    }

    
    private void ShowMcpRevokeConfirmDialog() => ShowOverlay(McpRevokeConfirmOverlay, McpRevokeConfirmDialog, McpRevokeConfirmDialogTransform);
    private void HideMcpRevokeConfirmDialog()
    {
        if (_animMcpRevokeConfirm) return;
        _animMcpRevokeConfirm = true;
        HideOverlay(McpRevokeConfirmOverlay, McpRevokeConfirmDialog, McpRevokeConfirmDialogTransform, () => _animMcpRevokeConfirm = false);
    }
    private void McpRevokeConfirmCancel_Click(object sender, RoutedEventArgs e) => HideMcpRevokeConfirmDialog();
    private void McpRevokeConfirmClose_Click(object sender, RoutedEventArgs e) => HideMcpRevokeConfirmDialog();
    private void McpRevokeConfirmScrim_Tapped(object sender, TappedRoutedEventArgs e)
    { if (ReferenceEquals(e.OriginalSource, McpRevokeConfirmScrim)) HideMcpRevokeConfirmDialog(); }
    private void McpRevokeConfirmOk_Click(object sender, RoutedEventArgs e)
    {
        if (_pendingRevokePath != null)
        {
            if (Novara.Services.McpService.RevokeAuthorizedProcess(_pendingRevokePath)) // N4C-02: locked removal
                App.Store?.SaveAsync();
            BuildMcpAuthorizedList();
        }
        _pendingRevokePath = null;
        HideMcpRevokeConfirmDialog();
    }

    

    private void McpAuditButton_Click(object sender, RoutedEventArgs e)
    {
        BuildMcpAuditList();
        ShowMcpAuditDialog();
    }

    
    private void BuildMcpAuditList()
    {
        McpAuditList.Children.Clear();
        var events = Novara.Services.McpAuditLog.ReadLatest(200);
        if (events.Count == 0)
        {
            McpAuditEmptyText.Text = App.GetString("Setting_Mcp_Audit_Empty");
            McpAuditEmptyText.Visibility = Visibility.Visible;
            return;
        }
        McpAuditEmptyText.Visibility = Visibility.Collapsed;
        foreach (var evt in events)
            McpAuditList.Children.Add(BuildMcpAuditRow(evt));
    }

    private static FrameworkElement BuildMcpAuditRow(McpAuditEvent evt)
    {
        var row = new Grid { Margin = new Thickness(0, 0, 0, 10) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });                        
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });   
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });                        

        var proc = new TextBlock
        {
            Text = Novara.Services.McpAuditLog.ShortName(evt.Path.Length > 0 ? evt.Path : evt.Client),
            FontSize = 13,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Foreground = App.GetBrush("AppPrimaryButtonBrush"),
            VerticalAlignment = VerticalAlignment.Top,
            MaxWidth = 140,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Margin = new Thickness(0, 0, 12, 0),
        };
        ToolTipService.SetToolTip(proc, evt.Path);
        Grid.SetColumn(proc, 0);
        row.Children.Add(proc);

        var info = new StackPanel { Spacing = 3, VerticalAlignment = VerticalAlignment.Center };
        var line1 = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        var evLabel = evt.Ev switch
        {
            "auth_ok" => App.GetString("Setting_Mcp_Audit_Ev_Conn"),
            "auth_new" => App.GetString("Setting_Mcp_Audit_Ev_NewAuth"),
            "auth_denied" => App.GetString("Setting_Mcp_Audit_Ev_Denied"),
            "revoke" => App.GetString("Setting_Mcp_Audit_Ev_Revoke"),
            _ => evt.Tool, 
        };
        line1.Children.Add(new TextBlock { Text = evLabel, FontSize = 12, Foreground = App.GetBrush("AppTextPrimaryBrush"), VerticalAlignment = VerticalAlignment.Center });
        line1.Children.Add(new TextBlock { Text = evt.Ts, FontSize = 11, Foreground = App.GetBrush("AppTextSecondaryBrush"), VerticalAlignment = VerticalAlignment.Center });
        info.Children.Add(line1);

        string desc = evt.Ev == "call"
            ? (evt.Title.Length > 0 ? $"{evt.Target} · {evt.Title}" : evt.Target)
            : evt.Reason;
        if (desc.Length > 0)
        {
            var descText = new TextBlock
            {
                Text = desc,
                FontSize = 11,
                Foreground = App.GetBrush("AppTextSecondaryBrush"),
                TextTrimming = TextTrimming.CharacterEllipsis,
            };
            ToolTipService.SetToolTip(descText, desc);
            info.Children.Add(descText);
        }
        Grid.SetColumn(info, 1);
        row.Children.Add(info);

        
        var glyph = new FontIcon
        {
            Glyph = evt.Ok ? "\uE73E" : "\uE711",
            FontSize = 14,
            Foreground = new SolidColorBrush(evt.Ok
                ? Color.FromArgb(255, 0x4C, 0xAF, 0x50)
                : Color.FromArgb(255, 0xFF, 0x45, 0x45)),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(10, 0, 2, 0),
        };
        Grid.SetColumn(glyph, 2);
        row.Children.Add(glyph);

        return row;
    }

    private void ShowMcpAuditDialog() => ShowOverlay(McpAuditOverlay, McpAuditDialog, McpAuditDialogTransform);
    private void HideMcpAuditDialog()
    {
        if (_animMcpAudit) return;
        _animMcpAudit = true;
        HideOverlay(McpAuditOverlay, McpAuditDialog, McpAuditDialogTransform, () => _animMcpAudit = false);
    }
    private void McpAuditClose_Click(object sender, RoutedEventArgs e) => HideMcpAuditDialog();
    private void McpAuditCloseBottom_Click(object sender, RoutedEventArgs e) => HideMcpAuditDialog();
    private void McpAuditScrim_Tapped(object sender, TappedRoutedEventArgs e)
    { if (ReferenceEquals(e.OriginalSource, McpAuditScrim)) HideMcpAuditDialog(); }

    private void McpAuditClear_Click(object sender, RoutedEventArgs e) => ShowMcpAuditClearConfirmDialog();

    
    private void ShowMcpAuditClearConfirmDialog() => ShowOverlay(McpAuditClearConfirmOverlay, McpAuditClearConfirmDialog, McpAuditClearConfirmDialogTransform);
    private void HideMcpAuditClearConfirmDialog()
    {
        if (_animMcpAuditClear) return;
        _animMcpAuditClear = true;
        HideOverlay(McpAuditClearConfirmOverlay, McpAuditClearConfirmDialog, McpAuditClearConfirmDialogTransform, () => _animMcpAuditClear = false);
    }
    private void McpAuditClearConfirmCancel_Click(object sender, RoutedEventArgs e) => HideMcpAuditClearConfirmDialog();
    private void McpAuditClearConfirmClose_Click(object sender, RoutedEventArgs e) => HideMcpAuditClearConfirmDialog();
    private void McpAuditClearConfirmScrim_Tapped(object sender, TappedRoutedEventArgs e)
    { if (ReferenceEquals(e.OriginalSource, McpAuditClearConfirmScrim)) HideMcpAuditClearConfirmDialog(); }
    private void McpAuditClearConfirmOk_Click(object sender, RoutedEventArgs e)
    {
        Novara.Services.McpAuditLog.ClearAll();
        BuildMcpAuditList();
        HideMcpAuditClearConfirmDialog();
    }

    

    private void ShowMcpConfigDialog() => ShowOverlay(McpConfigOverlay, McpConfigDialog, McpConfigDialogTransform);
    private void HideMcpConfigDialog()
    {
        if (_animMcpConfig) return;
        _animMcpConfig = true;
        HideOverlay(McpConfigOverlay, McpConfigDialog, McpConfigDialogTransform, () => _animMcpConfig = false);
    }
    private void McpConfigClose_Click(object sender, RoutedEventArgs e) => HideMcpConfigDialog();
    private void McpConfigScrim_Tapped(object sender, TappedRoutedEventArgs e)
    { if (ReferenceEquals(e.OriginalSource, McpConfigScrim)) HideMcpConfigDialog(); }

    private void McpResetButton_Click(object sender, RoutedEventArgs e) => ShowMcpResetConfirmDialog();

    
    private void ShowMcpResetConfirmDialog() => ShowOverlay(McpResetConfirmOverlay, McpResetConfirmDialog, McpResetConfirmDialogTransform);
    private void HideMcpResetConfirmDialog()
    {
        if (_animMcpResetConfirm) return;
        _animMcpResetConfirm = true;
        HideOverlay(McpResetConfirmOverlay, McpResetConfirmDialog, McpResetConfirmDialogTransform, () => _animMcpResetConfirm = false);
    }
    private void McpResetConfirmCancel_Click(object sender, RoutedEventArgs e) => HideMcpResetConfirmDialog();
    private void McpResetConfirmClose_Click(object sender, RoutedEventArgs e) => HideMcpResetConfirmDialog();
    private void McpResetConfirmScrim_Tapped(object sender, TappedRoutedEventArgs e)
    { if (ReferenceEquals(e.OriginalSource, McpResetConfirmScrim)) HideMcpResetConfirmDialog(); }
    private void McpResetConfirmOk_Click(object sender, RoutedEventArgs e)
    {
        var settings = App.Store?.Database.AppSettings;
        if (settings == null) return;
        settings.McpToken = GenerateToken();
        settings.McpTokenGeneratedAt = DateTime.Now;
        App.Store?.SaveAsync();
        HideMcpResetConfirmDialog();
        UpdateMcpUI();
        App.ShowToast(App.GetString("Common_Toast_Reset"));
    }

    
    private void McpCopyButton_Click(object sender, RoutedEventArgs e) => ShowMcpCopyDialog();
    private void ShowMcpCopyDialog() => ShowOverlay(McpCopyOverlay, McpCopyDialog, McpCopyDialogTransform);
    private void HideMcpCopyDialog()
    {
        if (_animMcpCopy) return;
        _animMcpCopy = true;
        HideOverlay(McpCopyOverlay, McpCopyDialog, McpCopyDialogTransform, () => _animMcpCopy = false);
    }
    private void McpCopyClose_Click(object sender, RoutedEventArgs e) => HideMcpCopyDialog();
    private void McpCopyScrim_Tapped(object sender, TappedRoutedEventArgs e)
    { if (ReferenceEquals(e.OriginalSource, McpCopyScrim)) HideMcpCopyDialog(); }
    private void McpCopyJson_Click(object sender, RoutedEventArgs e)
    {
        CopyToClipboard(BuildMcpJson());
        HideMcpCopyDialog();
    }
    private void McpCopyHint_Click(object sender, RoutedEventArgs e)
    {
        CopyToClipboard(BuildMcpHint());
        HideMcpCopyDialog();
    }

    
    private void McpDeletePermissionButton_Click(object sender, RoutedEventArgs e)
    {
        var settings = App.Store?.Database.AppSettings;
        if (settings == null) return;

        var menu = new MenuFlyout { MenuFlyoutPresenterStyle = (Style)Application.Current.Resources["GlassMenuFlyoutPresenterStyle"] };
        var accentBrush = App.GetBrush("AppTextPrimaryBrush");
        var normalBrush = App.GetBrush("AppTextSecondaryBrush");

        MenuFlyoutItem MakeItem(string text, bool active)
        {
            var item = new MenuFlyoutItem { Style = (Style)Application.Current.Resources["GlassMenuFlyoutItemStyle"], Text = text, Foreground = active ? accentBrush : normalBrush };
            if (active) item.Icon = new FontIcon { Glyph = "\uE73E", FontSize = 12, Foreground = accentBrush };
            return item;
        }

        var onItem = MakeItem(App.GetString("Setting_Autostart_On"), settings.McpDeleteEnabled);
        var offItem = MakeItem(App.GetString("Setting_Autostart_Off"), !settings.McpDeleteEnabled);

        onItem.Click += (_, _) =>
        {
            if (!settings.McpDeleteEnabled) ShowMcpDeleteConfirmDialog(); 
        };
        offItem.Click += (_, _) => SetMcpDeleteEnabled(false);

        menu.Items.Add(onItem); menu.Items.Add(offItem);
        menu.ShowAt(McpDeletePermissionButton, new Windows.Foundation.Point(0, McpDeletePermissionButton.ActualHeight + 4));
    }

    private void SetMcpDeleteEnabled(bool enabled)
    {
        var settings = App.Store?.Database.AppSettings;
        if (settings == null) return;
        settings.McpDeleteEnabled = enabled;
        App.Store?.SaveAsync();
        UpdateMcpDeletePermissionButton(enabled);
    }

    private void UpdateMcpDeletePermissionButton(bool on)
    {
        
        McpDeletePermissionButton.Style = on
            ? (Style)Resources["McpDangerButtonStyle"]
            : (Style)Application.Current.Resources["NovaraOutlineButtonStyle"];
        McpDeletePermissionButton.Background = on
            ? new SolidColorBrush(Color.FromArgb(204, 255, 69, 69)) // #CCFF4545
            : new SolidColorBrush(Color.FromArgb(0, 0, 0, 0));
        McpDeletePermissionButton.Foreground = on
            ? new SolidColorBrush(Color.FromArgb(255, 255, 255, 255))
            : App.GetBrush("AppTextPrimaryBrush");
        McpDeletePermissionText.Text = App.GetString(on ? "Setting_Autostart_On" : "Setting_Autostart_Off");
    }

    private void ShowMcpDeleteConfirmDialog() => ShowOverlay(McpDeleteConfirmOverlay, McpDeleteConfirmDialog, McpDeleteConfirmDialogTransform);
    private void HideMcpDeleteConfirmDialog()
    {
        if (_animMcpDeleteConfirm) return;
        _animMcpDeleteConfirm = true;
        HideOverlay(McpDeleteConfirmOverlay, McpDeleteConfirmDialog, McpDeleteConfirmDialogTransform, () => _animMcpDeleteConfirm = false);
    }
    private void McpDeleteConfirmCancel_Click(object sender, RoutedEventArgs e) => HideMcpDeleteConfirmDialog();
    private void McpDeleteConfirmClose_Click(object sender, RoutedEventArgs e) => HideMcpDeleteConfirmDialog();
    private void McpDeleteConfirmScrim_Tapped(object sender, TappedRoutedEventArgs e)
    { if (ReferenceEquals(e.OriginalSource, McpDeleteConfirmScrim)) HideMcpDeleteConfirmDialog(); }
    private void McpDeleteConfirmOk_Click(object sender, RoutedEventArgs e)
    {
        HideMcpDeleteConfirmDialog();
        SetMcpDeleteEnabled(true);
    }

    
    private string BuildMcpJson()
    {
        var settings = App.Store?.Database.AppSettings;
        var exe = FindNovaraMcpExe();
        var token = settings?.McpToken ?? "";
        var config = new
        {
            mcpServers = new
            {
                novara = new
                {
                    command = exe,
                    args = new[] { "--token", token }
                }
            }
        };
        return System.Text.Json.JsonSerializer.Serialize(config, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
    }

    private string BuildMcpHint() => App.GetString("Setting_Mcp_Hint").Replace("{JSON}", BuildMcpJson());
}
