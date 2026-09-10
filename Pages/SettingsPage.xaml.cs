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
    private bool _animQuickCaptureInfo; // 9.3: Quick Capture info dialog hide guard
    private bool _animPageWipe; // 9.3: per-page wipe dialog guards
    private bool _animPageWipeFinal;
    private bool _animPageGroupLoss;
    private readonly System.Collections.Generic.Dictionary<Grid, int> _overlayAnimGens = new(); 
    private readonly System.Collections.Generic.Dictionary<Grid, Border> _overlayDialogs = new(); 
    private bool _animNetActivity; // 9.3: network activity panel guard
    private bool _autoLockOnSystemLock; 
    private int _autoLockSeconds;       
    private bool _welcomeOnLaunch; 

    
    private bool _animWorkspaceManage;
    private bool _animWorkspaceEdit;
    private bool _animWorkspaceDelete;
    private Services.IconRing? _workspaceIconRing; 
    private string _workspaceSelectedIcon = "";
    private string? _editingWorkspaceId; // null = create new
    private string? _pendingDeleteWorkspaceId;

    private static readonly string[] TabNames = { "备忘", "文件", "计划", "日记" };

    public SettingsPage()
    {
        InitializeComponent();
        SettingsTitleText.Text = App.GetString("Setting_Title_Page"); 
        ConnectCardTitle.Text = App.GetString("Setting_Connect_Title"); 
        ExportViewerButtonText.Text = App.GetString("Setting_Connect_ExportViewer");
        ConnectIntroTitle.Text = App.GetString("Setting_Connect_ExportViewer"); 
        ConnectIntroDesc.Text = App.GetString("Connect_IntroDesc");
        ConnectChkMemo.Content = App.GetString("Connect_Data_Memo");
        ConnectChkFile.Content = App.GetString("Connect_Data_File");
        ConnectChkPlan.Content = App.GetString("Connect_Data_Plan");
        ConnectChkDiary.Content = App.GetString("Connect_Data_Diary");
        DeployHelpText.Text = App.GetString("Connect_DeployHelp");
        ConnectConfirmText.Text = App.GetString("Common_Button_Confirm");
        ConnectPwdTitle.Text = App.GetString("Connect_Pwd_Title");
        ConnectPwdDesc.Text = App.GetString("Connect_Pwd_Desc");
        ConnectPwdUseLock.Content = App.GetString("Setting_EncBackup_UseLock");
        ConnectPwdPasswordBox.PlaceholderText = App.GetString("Connect_Pwd_PasswordHint");
        ConnectPwdConfirmBox.PlaceholderText = App.GetString("Connect_Pwd_ConfirmHint");
        ConnectPwdStrengthHint.Text = App.GetString("Setting_EncBackup_StrengthHint");
        ConnectPwdCancelText.Text = App.GetString("Common_Button_Cancel");
        ConnectPwdConfirmText.Text = App.GetString("Common_Button_Confirm");
        ConnectLockNoticeTitle.Text = App.GetString("Connect_LockNotice_Title");
        ConnectLockNoticeDangerIcon.Data = App.CreateGeometry(IconData.Danger);
        ConnectLockNoticeDesc.Text = App.GetString("Connect_LockNotice_Desc");
        ConnectLockNoticeCancelText.Text = App.GetString("Common_Button_Cancel");
        
        
        StatsExpandIcon.Data = App.CreateGeometry(IconData.CardCollapse);
        McpExpandIcon.Data = App.CreateGeometry(IconData.CardCollapse);
        Novara.Services.DialogDepth.AttachContainer((Grid)Content, autoVeil: true); 
        // N2-13: danger-triangle icon on every destructive-dialog title (one-shot Data assignment;
        // red #FF4545 is set in XAML and matches each title's color language)
        foreach (var dangerIcon in new Microsoft.UI.Xaml.Controls.PathIcon[]
        {
            ResetConfirmDangerIcon, PrivacyLockWarnDangerIcon, CloseLockDangerIcon,
            BackupDeleteDangerIcon, BackupRestoreDangerIcon, PageWipeFinalDangerIcon,
            PageGroupLossDangerIcon, McpDeleteConfirmDangerIcon, WorkspaceDeleteDangerIcon,
            ImportConfirmDangerIcon, McpRevokeConfirmDangerIcon, McpResetConfirmDangerIcon,
            McpAuditClearDangerIcon, // N2-13 sweep: red-titled dialogs that missed the first pass
        })
            dangerIcon.Data = App.CreateGeometry(IconData.Danger);
        KeyDown += Page_KeyDown;
        SettingsRoot.SizeChanged += (_, _) => ReclampVisibleDialogs(); 
        WorkspaceNameTextBox.TextChanged += (_, _) => UpdateWorkspaceEditConfirmState(); // N2-53: live confirm-button state
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
            ExportPlainWarnOverlay.Visibility = Visibility.Collapsed; // N2-13
            McpConfigOverlay.Visibility = Visibility.Collapsed;
            McpResetConfirmOverlay.Visibility = Visibility.Collapsed;
            McpCopyOverlay.Visibility = Visibility.Collapsed;
            McpDeleteConfirmOverlay.Visibility = Visibility.Collapsed;
            AutoLockSyncConfirmOverlay.Visibility = Visibility.Collapsed; // N6-11
            McpAuditOverlay.Visibility = Visibility.Collapsed;
            McpAuditClearConfirmOverlay.Visibility = Visibility.Collapsed;
            CsvImportPreviewOverlay.Visibility = Visibility.Collapsed;
            CsvExportNoticeOverlay.Visibility = Visibility.Collapsed;
            EncExportOverlay.Visibility = Visibility.Collapsed;      // 9.2#6
            EncImportPwdOverlay.Visibility = Visibility.Collapsed;   // 9.2#6
            WindowsHelloOverlay.Visibility = Visibility.Collapsed; // ND8
            McpRevokeConfirmOverlay.Visibility = Visibility.Collapsed; // ND8
            // N2-10: the 9.3 dialogs below had their hide-guards reset (see below) but were missing
            // from this M4 destroy list - an open dialog survived Ctrl+K/IPC/Back leaving the page
            // (instance is cached) and reappeared on re-entry.
            QuickCaptureInfoOverlay.Visibility = Visibility.Collapsed; // 9.3
            PageWipeOverlay.Visibility = Visibility.Collapsed; // 9.3
            PageWipeFinalOverlay.Visibility = Visibility.Collapsed; // 9.3
            PageGroupLossOverlay.Visibility = Visibility.Collapsed; // 9.3
            NetActivityOverlay.Visibility = Visibility.Collapsed; // 9.3
            WorkspaceManageOverlay.Visibility = Visibility.Collapsed; // 9.3
            WorkspaceEditOverlay.Visibility = Visibility.Collapsed; // 9.3
            WorkspaceDeleteOverlay.Visibility = Visibility.Collapsed; // 9.3
            // R1 (Round 5): reset all hide-animation flags on unload (prevent stuck)
            _animSetPwd = _animChangePwd = _animCloseLock = _animWarn = _animResetConfirm = _animResetPwd = _animImportPwd = _animImportConfirm = _animImportResult = false;
            _animBackupIntro = _animBackupNow = _animBackupAuto = _animBackupRestore = _animBackupDelete = _animBackupRestoreConfirm = _animBackupDeleteConfirm = false;
            _animExportMdNotice = _animExportOptions = _animWarnShow = false; 
            _animExportPlainWarn = false; // N2-13
            _themeRestartAnimating = _languageRestartAnimating = false; 
            _animMcpConfig = _animMcpResetConfirm = _animMcpCopy = _animMcpDeleteConfirm = _animMcpRevokeConfirm = false;
            _animMcpAudit = _animMcpAuditClear = false; 
            McpPermOverlay.Visibility = Visibility.Collapsed; 
            _animMcpPerm = false;
            _animAutoLockSync = false; 
            _animQuickCaptureInfo = false; // 9.3: Quick Capture info dialog guard
            _animPageWipe = false; _animPageWipeFinal = false; _animPageGroupLoss = false; // 9.3: page-wipe guards
            _animNetActivity = false; // 9.3: network activity panel guard
            _animWinHello = false; // N2-11: stuck guard made the Hello dialog permanently un-closable
            _animWorkspaceManage = _animWorkspaceEdit = _animWorkspaceDelete = false; // N2-11: same for the workspace dialogs
            _pendingVerifiedAction = null; // N5T1-02: a stale gated flow must not hijack the next password verify
            _pendingExport = false;        // N5T1-02: same for the native export flag
            
            
_animCsvPreview = false;
            _animCsvExportNotice = false;
        };
        Loaded += (_, _) =>
        {
            CardsFloatIn.Begin();

            // 9.2#5 (D4): the authorize dialog may land here before the page is loaded (fresh
            // navigation) - OpenMcpPermEditor defers into this hook so the permission matrix and the
            // melt animations below never run against an unmeasured tree.
            if (_pendingMcpPermPath != null)
            {
                var pending = _pendingMcpPermPath;
                _pendingMcpPermPath = null;
                OpenMcpPermEditor(pending);
            }

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
        if (ExportPlainWarnOverlay.Visibility == Visibility.Visible) { _pendingPlainExportContinue = null; HidePlainExportWarnDialog(); e.Handled = true; return; } // N2-13
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
        // N3-13: the 9.1-9.3 era dialogs were never wired into the Esc chain (N2-52 follow-up)
        if (QuickCaptureInfoOverlay.Visibility == Visibility.Visible) { HideQuickCaptureInfoDialog(); e.Handled = true; return; }
        if (NetActivityOverlay.Visibility == Visibility.Visible) { HideNetActivityPanel(); e.Handled = true; return; }
        if (AutoLockSyncConfirmOverlay.Visibility == Visibility.Visible) { HideAutoLockSyncConfirmDialog(); e.Handled = true; return; }
        if (PageWipeOverlay.Visibility == Visibility.Visible) { HidePageWipeDialog(); e.Handled = true; return; }
        if (PageWipeFinalOverlay.Visibility == Visibility.Visible) { HidePageWipeFinalDialog(); e.Handled = true; return; }
        if (PageGroupLossOverlay.Visibility == Visibility.Visible) { HidePageGroupLossDialog(); e.Handled = true; return; }
        if (WorkspaceManageOverlay.Visibility == Visibility.Visible) { HideWorkspaceManage(); e.Handled = true; return; }
        if (WorkspaceEditOverlay.Visibility == Visibility.Visible) { HideWorkspaceEdit(); e.Handled = true; return; }
        if (WorkspaceDeleteOverlay.Visibility == Visibility.Visible) { HideWorkspaceDelete(); e.Handled = true; return; }
        if (EncExportOverlay.Visibility == Visibility.Visible) { HideEncExportDialog(); e.Handled = true; return; }
        if (EncImportPwdOverlay.Visibility == Visibility.Visible) { HideEncImportPwdDialog(); e.Handled = true; return; }
        if (McpPermOverlay.Visibility == Visibility.Visible) { HideMcpPermDialog(); e.Handled = true; return; }
        if (McpAuditOverlay.Visibility == Visibility.Visible) { HideMcpAuditDialog(); e.Handled = true; return; }
        if (McpAuditClearConfirmOverlay.Visibility == Visibility.Visible) { HideMcpAuditClearConfirmDialog(); e.Handled = true; return; }
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
            Novara.MainWindow.AcquireSingleInstance(); 
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
        SetPasswordDialogTransform.ScaleX = 0.94; SetPasswordDialogTransform.ScaleY = 0.94; SetPasswordDialogTransform.TranslateY = 24;
        SetPasswordDialog.Opacity = 0;
        SetPasswordScrim.Opacity = 0;
        SetPasswordOverlay.Visibility = Visibility.Visible;
        var sb = new Storyboard();
        var si = new DoubleAnimation { To = 1, Duration = TimeSpan.FromMilliseconds(250) };
        Storyboard.SetTarget(si, SetPasswordScrim); Storyboard.SetTargetProperty(si, "Opacity"); sb.Children.Add(si);
        var di = new DoubleAnimation { To = 1, Duration = TimeSpan.FromMilliseconds(300) };
        Storyboard.SetTarget(di, SetPasswordDialog); Storyboard.SetTargetProperty(di, "Opacity"); sb.Children.Add(di);
        Motion.AddDialogShowTransform(sb, SetPasswordDialogTransform); // M1: EmphasizedDecelerate (Motion)
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
        Motion.AddDialogHideTransform(sb, SetPasswordDialogTransform); // M1: EmphasizedAccelerate (Motion)
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
        ChangePasswordDialogTransform.ScaleX = 0.94; ChangePasswordDialogTransform.ScaleY = 0.94; ChangePasswordDialogTransform.TranslateY = 24;
        ChangePasswordDialog.Opacity = 0;
        ChangePasswordScrim.Opacity = 0;
        ChangePasswordOverlay.Visibility = Visibility.Visible;
        var sb = new Storyboard();
        var si = new DoubleAnimation { To = 1, Duration = TimeSpan.FromMilliseconds(250) };
        Storyboard.SetTarget(si, ChangePasswordScrim); Storyboard.SetTargetProperty(si, "Opacity"); sb.Children.Add(si);
        var di = new DoubleAnimation { To = 1, Duration = TimeSpan.FromMilliseconds(300) };
        Storyboard.SetTarget(di, ChangePasswordDialog); Storyboard.SetTargetProperty(di, "Opacity"); sb.Children.Add(di);
        Motion.AddDialogShowTransform(sb, ChangePasswordDialogTransform); // M1: EmphasizedDecelerate (Motion)
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
        Motion.AddDialogHideTransform(sb, ChangePasswordDialogTransform); // M1: EmphasizedAccelerate (Motion)
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
        if (WindowsHelloService.IsEnabled())
        {
            // N4-36: observe the result - Update removes the old credential first, so a failed re-add
            // used to disable Hello unlock silently. Still off the UI thread (PasswordVault.Add
            // blocks); App.ShowToast marshals back through UiQueue itself. The "lock right after a
            // change" race degrades gracefully to password unlock (by design, no data risk).
            _ = System.Threading.Tasks.Task.Run(() => WindowsHelloService.Update(newPw))
                .ContinueWith(t => { if (t.IsFaulted || !t.Result) App.ShowToast(App.GetString("Hello_Update_Fail")); });
        }
        HideChangePasswordDialog();
    }

    private void DisablePrivacyLockButton_Click(object sender, RoutedEventArgs e)
    {
        ClosePrivacyLockPasswordBox.Text = "";
        ClosePrivacyLockDialogTransform.ScaleX = 0.94; ClosePrivacyLockDialogTransform.ScaleY = 0.94; ClosePrivacyLockDialogTransform.TranslateY = 24;
        ClosePrivacyLockDialog.Opacity = 0;
        ClosePrivacyLockScrim.Opacity = 0;
        ClosePrivacyLockOverlay.Visibility = Visibility.Visible;
        var sb = new Storyboard();
        var si = new DoubleAnimation { To = 1, Duration = TimeSpan.FromMilliseconds(250) };
        Storyboard.SetTarget(si, ClosePrivacyLockScrim); Storyboard.SetTargetProperty(si, "Opacity"); sb.Children.Add(si);
        var di = new DoubleAnimation { To = 1, Duration = TimeSpan.FromMilliseconds(300) };
        Storyboard.SetTarget(di, ClosePrivacyLockDialog); Storyboard.SetTargetProperty(di, "Opacity"); sb.Children.Add(di);
        Motion.AddDialogShowTransform(sb, ClosePrivacyLockDialogTransform); // M1: EmphasizedDecelerate (Motion)
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
        Motion.AddDialogHideTransform(sb, ClosePrivacyLockDialogTransform); // M1: EmphasizedAccelerate (Motion)
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
        PrivacyLockWarningDialogTransform.ScaleX = 0.94; PrivacyLockWarningDialogTransform.ScaleY = 0.94; PrivacyLockWarningDialogTransform.TranslateY = 24;
        PrivacyLockWarningDialog.Opacity = 0;
        PrivacyLockWarningScrim.Opacity = 0;
        PrivacyLockWarningOverlay.Visibility = Visibility.Visible;
        var sb = new Storyboard();
        var si = new DoubleAnimation { To = 1, Duration = TimeSpan.FromMilliseconds(250) };
        Storyboard.SetTarget(si, PrivacyLockWarningScrim); Storyboard.SetTargetProperty(si, "Opacity"); sb.Children.Add(si);
        var di = new DoubleAnimation { To = 1, Duration = TimeSpan.FromMilliseconds(300) };
        Storyboard.SetTarget(di, PrivacyLockWarningDialog); Storyboard.SetTargetProperty(di, "Opacity"); sb.Children.Add(di);
        Motion.AddDialogShowTransform(sb, PrivacyLockWarningDialogTransform); // M1: EmphasizedDecelerate (Motion)
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
        Motion.AddDialogHideTransform(sb, PrivacyLockWarningDialogTransform); // M1: EmphasizedAccelerate (Motion)
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
        // 9.2#7: same entry now also covers the v2->v3 KDF-hardening offer (same dialog, new flavor).
        MigrateFormatEntryButton.Visibility = App.Store is { IsEncrypted: true } && (App.Store.NeedsFormatMigration || App.Store.NeedsKdfMigration)
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
            // N2-51: longest-first order - "3600s".Replace("600s",...) ran before the 3600s rule and
            // turned 3600s into "310min" (the "600s" substring lives inside "3600s").
            ? ("" + _autoLockSeconds + "s").Replace("3600s", "60min").Replace("1800s", "30min").Replace("600s", "10min").Replace("300s", "5min").Replace("60s", "1min")
            : App.GetString("Setting_AutoLock_Never");

        // 9.3 Quick Capture card - same settings source, always visible (independent of the privacy lock)
        QuickCaptureTitleText.Text = App.GetString("Setting_QuickCapture");
        ResetDataButtonText.Text = App.GetString("Setting_DataWipe");
        ApplyToggleState(ResetDataButton, false); // gray base like every other card (owner call)
        NetActivityTitleText.Text = App.GetString("NetActivity_Panel");
        ApplyToggleState(NetActivityPanelButton, false); // gray base - static style alone renders transparent
        NetActivityPanelButtonText.Text = App.GetString("NetActivity_Open");
        // 9.3 Workspace card + dialogs
        WorkspaceCardTitleText.Text = App.GetString("Workspace_Title");
        ApplyToggleState(WorkspaceManageButton, false);
        WorkspaceManageButtonText.Text = App.GetString("Workspace_Manage");
        WorkspaceManageTitle.Text = App.GetString("Workspace_Title");
        WorkspaceNewButtonText.Text = App.GetString("Workspace_New");
        WorkspaceEditNameLabel.Text = App.GetString("Workspace_Name");
        WorkspaceEditIconLabel.Text = App.GetString("Common_Label_PickIcon");
        WorkspaceNameTextBox.PlaceholderText = App.GetString("Workspace_NamePlaceholder");
        WorkspaceEditCancelText.Text = App.GetString("Common_Button_Cancel");
        WorkspaceEditConfirmText.Text = App.GetString("Common_Button_Confirm");
        WorkspaceDeleteTitle.Text = App.GetString("Workspace_DeleteTitle");
        WorkspaceDeleteDesc.Text = App.GetString("Workspace_DeleteDesc");
        WorkspaceDeleteCancelText.Text = App.GetString("Common_Button_Cancel");
        WorkspaceDeleteConfirmText.Text = App.GetString("Workspace_Delete");
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
        // The hotkey picker only exists while the feature is on, styled as the off-state (gray) selector
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
        // Owner call: the switch is a dropdown (On/Off) like every other card - enabling goes
        // through the info dialog so the feature details are seen before the hotkey is claimed.
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
        App.MainWindow?.ApplyQuickCaptureHotkey(); // re-register (or unregister) the system hotkey live
    }

    private void ShowQuickCaptureInfoDialog()
    {
        _animQuickCaptureInfo = false; // N3-16: show re-arms the hide guard (M2/N2-11 parity)
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

    

    private void NetActivityPanelButton_Click(object sender, RoutedEventArgs e)
    {
        // N3-16: Show re-arms the hide guard - if this Show lands while a previous Hide animation is
        // still in flight, the new Show animation replaces the Hide's on the same properties and its
        // Completed never fires, leaving _animNetActivity stuck true and every close path deadlocked.
        _animNetActivity = false;
        NetActivityPanelTitle.Text = App.GetString("NetActivity_Panel");
        NetActivityClearText.Text = App.GetString("NetActivity_Clear");
        RenderNetActivityList();
        ShowOverlay(NetActivityOverlay, NetActivityDialog, NetActivityDialogTransform);
        FitDialogScroll(NetActivityScroll, 144); 
    }

    private void RenderNetActivityList()
    {
        NetActivityList.Children.Clear();
        var recent = Services.NetworkActivityService.Recent;
        if (recent.Count == 0)
        {
            NetActivityList.Children.Add(new TextBlock
            {
                Text = App.GetString("NetActivity_Empty"),
                FontSize = 13, Foreground = App.GetBrush("AppTextTertiaryBrush"),
                HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 12, 0, 12),
            });
            return;
        }
        foreach (var it in recent)
        {
            var row = new Grid { ColumnSpacing = 10 };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var kindText = new TextBlock
            {
                Text = App.GetString(it.KindKey) + "  " + (it.Host.Length > 0 ? it.Host : "-"),
                FontSize = 13, Foreground = App.GetBrush("AppTextPrimaryBrush"),
                VerticalAlignment = VerticalAlignment.Center,
            };
            Grid.SetColumn(kindText, 0);
            var meta = new TextBlock
            {
                Text = it.Time.ToString("HH:mm:ss") + "  " + (it.Success ? "✓" : "✗"),
                FontSize = 12,
                Foreground = it.Success ? App.GetBrush("AppTextTertiaryBrush") : new SolidColorBrush(Windows.UI.Color.FromArgb(0xFF, 0xFF, 0x45, 0x45)),
                VerticalAlignment = VerticalAlignment.Center,
            };
            Grid.SetColumn(meta, 1);
            row.Children.Add(kindText); row.Children.Add(meta);
            var card = new Border
            {
                Background = App.GetBrush("AppSurfaceOverlayBrush"),
                BorderBrush = App.GetBrush("AppBorderBrush"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(14, 10, 14, 10),
                Child = row,
            };
            NetActivityList.Children.Add(card);
        }
    }

    private void NetActivityClear_Click(object sender, RoutedEventArgs e)
    {
        Services.NetworkActivityService.ClearRecent();
        RenderNetActivityList();
    }

    private void NetActivityClose_Click(object sender, RoutedEventArgs e) => HideNetActivityPanel();

    private void NetActivityScrim_Tapped(object sender, TappedRoutedEventArgs e)
    {
        if (ReferenceEquals(e.OriginalSource, NetActivityScrim)) HideNetActivityPanel();
    }

    private void HideNetActivityPanel()
    {
        if (_animNetActivity) return;
        _animNetActivity = true;
        HideOverlay(NetActivityOverlay, NetActivityDialog, NetActivityDialogTransform, () => _animNetActivity = false);
    }

    // ===================== 9.3 Workspace: management dialogs =====================

    public void OpenWorkspaceManagement()
    {
        _animWorkspaceManage = false; // N2-11: show re-arms the hide guard
        RenderWorkspaceList();
        ShowOverlay(WorkspaceManageOverlay, WorkspaceManageDialog, WorkspaceManageDialogTransform);
        FitDialogScroll(WorkspaceScroll, 100); 
    }

    private void WorkspaceManageButton_Click(object sender, RoutedEventArgs e) => OpenWorkspaceManagement();

    private void RenderWorkspaceList()
    {
        WorkspaceListPanel.Children.Clear();
        var ws = App.Store?.Database.AppSettings.Workspaces;
        if (ws == null || ws.Count == 0)
        {
            WorkspaceListPanel.Children.Add(new TextBlock
            {
                Text = App.GetString("Workspace_Empty"),
                FontSize = 13, Foreground = App.GetBrush("AppTextTertiaryBrush"),
                HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 20, 0, 20),
            });
            return;
        }
        foreach (var w in ws)
        {
            var row = new Grid { ColumnSpacing = 10 };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var icon = new Viewbox { Width = 22, Height = 22, Stretch = Stretch.Uniform, VerticalAlignment = VerticalAlignment.Center, Child = new PathIcon { Data = App.CreateGeometry(IconData.GetGroupPath(w.IconKey)), Foreground = App.GetBrush("IconForegroundBrush") } };
            Grid.SetColumn(icon, 0);
            var nameText = new TextBlock { Text = w.Name, FontSize = 14, Foreground = App.GetBrush("AppTextPrimaryBrush"), VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(nameText, 1);

            var editBtn = new Button
            {
                Width = 64, Height = 30,
                Style = (Style)Application.Current.Resources["NovaraOutlineButtonStyle"],
                CornerRadius = new CornerRadius(8),
                VerticalAlignment = VerticalAlignment.Center,
                Content = new TextBlock { Text = App.GetString("Workspace_EditShort"), FontSize = 13, Foreground = App.GetBrush("AppTextSecondaryBrush"), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center }
            };
            Grid.SetColumn(editBtn, 2);
            var wid = w.Id;
            editBtn.Click += (_, _) => OpenWorkspaceEditDialog(wid);

            var delBtn = new Button
            {
                Width = 64, Height = 30,
                Style = (Style)Resources["McpDangerButtonStyle"],
                CornerRadius = new CornerRadius(8),
                VerticalAlignment = VerticalAlignment.Center,
                Content = new TextBlock { Text = App.GetString("Workspace_Delete"), FontSize = 13, Foreground = new SolidColorBrush(Color.FromArgb(255, 255, 255, 255)), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center }
            };
            Grid.SetColumn(delBtn, 3);
            delBtn.Click += (_, _) => { _pendingDeleteWorkspaceId = wid; _animWorkspaceDelete = false; ShowOverlay(WorkspaceDeleteOverlay, WorkspaceDeleteDialog, WorkspaceDeleteDialogTransform); }; // N2-11: re-arm guard

            row.Children.Add(icon); row.Children.Add(nameText); row.Children.Add(editBtn); row.Children.Add(delBtn);
            WorkspaceListPanel.Children.Add(new Border
            {
                Background = App.GetBrush("AppSurfaceOverlayBrush"),
                BorderBrush = App.GetBrush("AppBorderBrush"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(14, 10, 14, 10),
                Child = row,
            });
        }
    }

    private void WorkspaceManageClose_Click(object sender, RoutedEventArgs e) => HideWorkspaceManage();
    private void WorkspaceManageScrim_Tapped(object sender, TappedRoutedEventArgs e)
    {
        if (ReferenceEquals(e.OriginalSource, WorkspaceManageScrim)) HideWorkspaceManage();
    }
    private void HideWorkspaceManage()
    {
        if (_animWorkspaceManage) return;
        _animWorkspaceManage = true;
        HideOverlay(WorkspaceManageOverlay, WorkspaceManageDialog, WorkspaceManageDialogTransform, () => _animWorkspaceManage = false);
    }

    private void WorkspaceNewButton_Click(object sender, RoutedEventArgs e) => OpenWorkspaceEditDialog(null);

    private void OpenWorkspaceEditDialog(string? id)
    {
        _animWorkspaceEdit = false; // N2-11: show re-arms the hide guard
        _editingWorkspaceId = id;
        BuildWorkspaceIconSelector();
        var ws = App.Store?.Database.AppSettings.Workspaces;
        var w = id == null ? null : ws?.FirstOrDefault(x => x.Id == id);
        WorkspaceEditTitle.Text = App.GetString(id == null ? "Workspace_New" : "Workspace_Edit");
        WorkspaceNameTextBox.Text = w?.Name ?? "";
        _workspaceSelectedIcon = w?.IconKey ?? "";
        HighlightWorkspaceIcon();
        UpdateWorkspaceEditConfirmState(); // N2-53: M5 parity - empty name disables confirm before the user types
        ShowOverlay(WorkspaceEditOverlay, WorkspaceEditDialog, WorkspaceEditDialogTransform);
    }

    // N2-53: M5 parity - invalid content disables the confirm button (no more silent no-op click)
    private void UpdateWorkspaceEditConfirmState()
        => WorkspaceEditConfirmButton.IsEnabled = !string.IsNullOrEmpty(WorkspaceNameTextBox.Text.Trim());

    private void BuildWorkspaceIconSelector()
    {
        
        if (WorkspaceIconPanel.Children.Count > 0) return;
        _workspaceIconRing ??= new Services.IconRing(WorkspaceIconScrollViewer, WorkspaceIconPanel);
        _workspaceIconRing.Tapped -= WorkspaceIconRingTapped;
        _workspaceIconRing.Tapped += WorkspaceIconRingTapped;
        _workspaceIconRing.Build(IconData.GroupIconKeysInOrder(), key => new Border
        {
            Width = 48, Height = 48, CornerRadius = new CornerRadius(12),
            Background = App.GetBrush("AppSurfaceOverlayBrush"),
            Tag = key,
            Child = new Viewbox { Width = 24, Height = 24, Stretch = Stretch.Uniform, Child = new PathIcon { Data = App.CreateGeometry(IconData.GetGroupPath(key)), Foreground = App.GetBrush("IconForegroundBrush") } }
        });
    }

    private void WorkspaceIconRingTapped(Border b)
    {
        _workspaceSelectedIcon = b.Tag?.ToString() ?? "";
        _workspaceIconRing?.Highlight(_workspaceSelectedIcon);
        _workspaceIconRing?.CenterTo(b);
    }

    private void HighlightWorkspaceIcon()
        => _workspaceIconRing?.Highlight(_workspaceSelectedIcon);

    private void WorkspaceEditClose_Click(object sender, RoutedEventArgs e) => HideWorkspaceEdit();
    private void WorkspaceEditScrim_Tapped(object sender, TappedRoutedEventArgs e)
    {
        if (ReferenceEquals(e.OriginalSource, WorkspaceEditScrim)) HideWorkspaceEdit();
    }
    private void WorkspaceEditCancel_Click(object sender, RoutedEventArgs e) => HideWorkspaceEdit();
    private void HideWorkspaceEdit()
    {
        if (_animWorkspaceEdit) return;
        _animWorkspaceEdit = true;
        HideOverlay(WorkspaceEditOverlay, WorkspaceEditDialog, WorkspaceEditDialogTransform, () => _animWorkspaceEdit = false);
    }

    private void WorkspaceEditConfirm_Click(object sender, RoutedEventArgs e)
    {
        var name = WorkspaceNameTextBox.Text.Trim();
        if (string.IsNullOrEmpty(name)) return; 
        var s = App.Store?.Database.AppSettings;
        if (s == null) { HideWorkspaceEdit(); return; }

        if (_editingWorkspaceId == null)
        {
            
            var newWs = new Novara.Models.WorkspaceItem
            {
                Name = name,
                IconKey = string.IsNullOrEmpty(_workspaceSelectedIcon) ? "Group01" : _workspaceSelectedIcon,
            };
            s.Workspaces.Add(newWs);
            App.CurrentWorkspaceId = newWs.Id; 
        }
        else
        {
            
            var w = s.Workspaces.FirstOrDefault(x => x.Id == _editingWorkspaceId);
            if (w != null)
            {
                w.Name = name;
                if (!string.IsNullOrEmpty(_workspaceSelectedIcon)) w.IconKey = _workspaceSelectedIcon;
            }
        }

        _ = App.Store?.SaveAsync();
        RenderWorkspaceList();
        App.MainWindow?.UpdateWorkspaceSwitcher();
        App.MainWindow?.RefreshWorkspaceFilters(); 
        HideWorkspaceEdit();
    }

    private void WorkspaceDeleteClose_Click(object sender, RoutedEventArgs e) => HideWorkspaceDelete();
    private void WorkspaceDeleteScrim_Tapped(object sender, TappedRoutedEventArgs e)
    {
        if (ReferenceEquals(e.OriginalSource, WorkspaceDeleteScrim)) HideWorkspaceDelete();
    }
    private void WorkspaceDeleteCancel_Click(object sender, RoutedEventArgs e) => HideWorkspaceDelete();
    private void HideWorkspaceDelete()
    {
        if (_animWorkspaceDelete) return;
        _animWorkspaceDelete = true;
        HideOverlay(WorkspaceDeleteOverlay, WorkspaceDeleteDialog, WorkspaceDeleteDialogTransform, () => _animWorkspaceDelete = false);
    }

    private void WorkspaceDeleteConfirm_Click(object sender, RoutedEventArgs e)
    {
        var wid = _pendingDeleteWorkspaceId;
        if (string.IsNullOrEmpty(wid)) { HideWorkspaceDelete(); return; }
        var db = App.Store?.Database;
        if (db == null) { HideWorkspaceDelete(); return; }

        
        db.AppSettings.Workspaces.RemoveAll(x => x.Id == wid);

        
        foreach (var en in db.MemoEntries) if (en.WorkspaceId == wid) en.WorkspaceId = "";
        foreach (var en in db.PathBackupItems) if (en.WorkspaceId == wid) en.WorkspaceId = "";
        foreach (var en in db.TodoCards) if (en.WorkspaceId == wid) en.WorkspaceId = "";
        foreach (var en in db.NoteCards) if (en.WorkspaceId == wid) en.WorkspaceId = "";
        foreach (var en in db.DiaryItems) if (en.WorkspaceId == wid) en.WorkspaceId = "";

        
        if (App.CurrentWorkspaceId == wid) App.CurrentWorkspaceId = "";

        _ = App.Store?.SaveAsync();
        RenderWorkspaceList();
        App.MainWindow?.UpdateWorkspaceSwitcher();
        App.MainWindow?.RefreshWorkspaceFilters();
        HideWorkspaceDelete();
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
        _animAutoLockSync = false; // N3-16: show re-arms the hide guard (M2/N2-11 parity)
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
            _ = StartWinHelloEnableAsync().ContinueWith(t => System.Diagnostics.Debug.WriteLine($"WinHello enable failed: {t.Exception}"), System.Threading.Tasks.TaskContinuationOptions.OnlyOnFaulted); 
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
        _animWinHello = false; // N2-11: show re-arms the hide guard (parity with EncExport/McpPerm)
        WindowsHelloDialogTransform.ScaleX = 0.94; WindowsHelloDialogTransform.ScaleY = 0.94; WindowsHelloDialogTransform.TranslateY = 24;
        WindowsHelloDialog.Opacity = 0;
        WindowsHelloScrim.Opacity = 0;
        WindowsHelloOverlay.Visibility = Visibility.Visible;
        var sb = new Storyboard();
        var si = new DoubleAnimation { To = 1, Duration = TimeSpan.FromMilliseconds(250) };
        Storyboard.SetTarget(si, WindowsHelloScrim); Storyboard.SetTargetProperty(si, "Opacity"); sb.Children.Add(si);
        var di = new DoubleAnimation { To = 1, Duration = TimeSpan.FromMilliseconds(300) };
        Storyboard.SetTarget(di, WindowsHelloDialog); Storyboard.SetTargetProperty(di, "Opacity"); sb.Children.Add(di);
        Motion.AddDialogShowTransform(sb, WindowsHelloDialogTransform); // M1: EmphasizedDecelerate (Motion)
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
        Motion.AddDialogHideTransform(sb, WindowsHelloDialogTransform); // M1: EmphasizedAccelerate (Motion)
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
        var menu = new MenuFlyout { MenuFlyoutPresenterStyle = (Style)Application.Current.Resources["GlassMenuFlyoutPresenterStyle"] };
        var dangerBrush = new SolidColorBrush(Color.FromArgb(0xFF, 0xFF, 0x45, 0x45));
        var normalBrush = App.GetBrush("AppTextSecondaryBrush");
        MenuFlyoutItem MakeItem(string text, bool danger = false)
        {
            var item = new MenuFlyoutItem { Style = (Style)Application.Current.Resources["GlassMenuFlyoutItemStyle"], Text = text, Foreground = danger ? dangerBrush : normalBrush };
            return item;
        }
        var allItem = MakeItem(App.GetString("DataWipe_All"));
        var memoItem = MakeItem(App.GetString("DataWipe_Memo"));
        var pathItem = MakeItem(App.GetString("DataWipe_Path"));
        var planItem = MakeItem(App.GetString("DataWipe_Plan"));
        var diaryItem = MakeItem(App.GetString("DataWipe_Diary"));
        allItem.Click += (_, _) => RunPrivacyGated(ShowResetConfirmDialogFlow);
        memoItem.Click += (_, _) => RunPrivacyGated(() => ShowPageWipeDialog("memo"));
        pathItem.Click += (_, _) => RunPrivacyGated(() => ShowPageWipeDialog("path"));
        planItem.Click += (_, _) => RunPrivacyGated(() => ShowPageWipeDialog("plan"));
        diaryItem.Click += (_, _) => RunPrivacyGated(() => ShowPageWipeDialog("diary"));
        menu.Items.Add(allItem); menu.Items.Add(memoItem); menu.Items.Add(pathItem); menu.Items.Add(planItem); menu.Items.Add(diaryItem);
        menu.ShowAt(ResetDataButton, new Windows.Foundation.Point(0, ResetDataButton.ActualHeight + 4));
    }

    private void ShowResetConfirmDialogFlow()
    {
        // legacy full-reset flow, now with the 30s cooldown (global hard-delete rule)
        _resetConfirmBusy = false; // N4-35: show re-arms the click guard (M2 show-rearm pattern - the flag stays set through the hide animation)
        ResetConfirmCd.Completed -= OnResetConfirmCdCompleted; 
        ResetConfirmCd.Completed += OnResetConfirmCdCompleted;
        ResetConfirmButton.IsEnabled = false;
        ResetConfirmButton.Opacity = 0.45;
        ShowResetConfirmDialog();
        ResetConfirmCd.Start(30);
    }

    private void OnResetConfirmCdCompleted()
    {
        ResetConfirmCd.Completed -= OnResetConfirmCdCompleted;
        ResetConfirmButton.IsEnabled = true;
        ResetConfirmButton.Opacity = 1;
    }

    private int _pageWipePage = 0; // 0=memo 1=path 2=plan 3=diary

    private static readonly string[] PageWipeNames = { "DataWipe_PageName_Memo", "DataWipe_PageName_Path", "DataWipe_PageName_Plan", "DataWipe_PageName_Diary" };

    private void ShowPageWipeDialog(string pageKey)
    {
        _pageWipePage = pageKey switch { "path" => 1, "plan" => 2, "diary" => 3, _ => 0 };
        var page = _pageWipePage;
        var (titleKey, count) = page switch
        {
            0 => ("DataWipe_PageName_Memo", App.Store?.Database.MemoEntries.Count(x => !x.IsDeleted)),
            1 => ("DataWipe_PageName_Path", App.Store?.Database.PathBackupItems.Count(x => !x.IsDeleted)),
            2 => ("DataWipe_PageName_Plan", (App.Store?.Database.TodoCards.Count(x => !x.IsDeleted) ?? 0) + (App.Store?.Database.NoteCards.Count(x => !x.IsDeleted) ?? 0)),
            _ => ("DataWipe_PageName_Diary", App.Store?.Database.DiaryItems.Count(x => !x.IsDeleted)),
        };
        PageWipeTitleText.Text = App.GetString(titleKey);
        PageWipeMessageText.Text = string.Format(App.GetString("DataWipe_Stat"), App.GetString(titleKey), count ?? 0);
        PageWipeSoftText.Text = App.GetString("DataWipe_Soft_Btn");
        PageWipeHardText.Text = App.GetString("DataWipe_Hard_Btn");
        _animPageWipe = false; // N3-16: show re-arms the hide guard (M2/N2-11 parity)
        ShowOverlay(PageWipeOverlay, PageWipeDialog, PageWipeDialogTransform);
    }

    private void HidePageWipeDialog()
    {
        
        if (_animPageWipe) return;
        _animPageWipe = true;
        HideOverlay(PageWipeOverlay, PageWipeDialog, PageWipeDialogTransform, () => _animPageWipe = false);
    }
    private void PageWipeClose_Click(object sender, RoutedEventArgs e) => HidePageWipeDialog();
    private void PageWipeScrim_Tapped(object sender, TappedRoutedEventArgs e)
    { if (ReferenceEquals(e.OriginalSource, PageWipeScrim)) HidePageWipeDialog(); }

    private void PageWipeSoftButton_Click(object sender, RoutedEventArgs e)
    {
        HidePageWipeDialog();
        if (_pageWipePage == 0)
            ShowPageGroupLossDialog(); // memo: groups are physically removed - extra confirmation (no cooldown, owner call)
        else
            PerformPageWipe(_pageWipePage, soft: true);
    }

    private void PageWipeHardButton_Click(object sender, RoutedEventArgs e)
    {
        HidePageWipeDialog();
        PageWipeFinalTitleText.Text = App.GetString("DataWipe_Final_Title");
        PageWipeFinalMessageText.Text = string.Format(App.GetString("DataWipe_Final_Desc"), App.GetString(PageWipeNames[_pageWipePage]));
        PageWipeFinalCancelText.Text = App.GetString("Common_Button_Cancel");
        PageWipeFinalOkText.Text = App.GetString("DataWipe_Hard_Btn");
        PageWipeFinalCd.Completed -= OnPageWipeFinalCdCompleted; 
        PageWipeFinalCd.Completed += OnPageWipeFinalCdCompleted;
        PageWipeFinalOkButton.IsEnabled = false;
        PageWipeFinalOkButton.Opacity = 0.45;
        _animPageWipeFinal = false; // N3-16: show re-arms the hide guard (M2/N2-11 parity)
        ShowOverlay(PageWipeFinalOverlay, PageWipeFinalDialog, PageWipeFinalDialogTransform);
        PageWipeFinalCd.Start(30);
    }

    private void OnPageWipeFinalCdCompleted()
    {
        PageWipeFinalCd.Completed -= OnPageWipeFinalCdCompleted;
        PageWipeFinalOkButton.IsEnabled = true;
        PageWipeFinalOkButton.Opacity = 1;
    }

    private void HidePageWipeFinalDialog()
    {
        if (_animPageWipeFinal) return;
        _animPageWipeFinal = true;
        PageWipeFinalCd.Reset();
        HideOverlay(PageWipeFinalOverlay, PageWipeFinalDialog, PageWipeFinalDialogTransform, () => _animPageWipeFinal = false);
    }
    private void PageWipeFinalClose_Click(object sender, RoutedEventArgs e) => HidePageWipeFinalDialog();
    private void PageWipeFinalCancel_Click(object sender, RoutedEventArgs e) => HidePageWipeFinalDialog();
    private void PageWipeFinalScrim_Tapped(object sender, TappedRoutedEventArgs e)
    { if (ReferenceEquals(e.OriginalSource, PageWipeFinalScrim)) HidePageWipeFinalDialog(); }
    private void PageWipeFinalOk_Click(object sender, RoutedEventArgs e)
    {
        HidePageWipeFinalDialog();
        PerformPageWipe(_pageWipePage, soft: false);
    }

    private void ShowPageGroupLossDialog()
    {
        PageGroupLossTitleText.Text = App.GetString("DataWipe_GroupLoss_Title");
        PageGroupLossMessageText.Text = App.GetString("DataWipe_GroupLoss_Desc");
        PageGroupLossCancelText.Text = App.GetString("Common_Button_Cancel");
        PageGroupLossOkText.Text = App.GetString("Common_Button_Confirm");
        _animPageGroupLoss = false; // N3-16: show re-arms the hide guard (M2/N2-11 parity)
        ShowOverlay(PageGroupLossOverlay, PageGroupLossDialog, PageGroupLossDialogTransform);
    }
    private void HidePageGroupLossDialog()
    {
        if (_animPageGroupLoss) return;
        _animPageGroupLoss = true;
        HideOverlay(PageGroupLossOverlay, PageGroupLossDialog, PageGroupLossDialogTransform, () => _animPageGroupLoss = false);
    }
    private void PageGroupLossClose_Click(object sender, RoutedEventArgs e) => HidePageGroupLossDialog();
    private void PageGroupLossCancel_Click(object sender, RoutedEventArgs e) => HidePageGroupLossDialog();
    private void PageGroupLossScrim_Tapped(object sender, TappedRoutedEventArgs e)
    { if (ReferenceEquals(e.OriginalSource, PageGroupLossScrim)) HidePageGroupLossDialog(); }
    private void PageGroupLossOk_Click(object sender, RoutedEventArgs e)
    {
        HidePageGroupLossDialog();
        PerformPageWipe(0, soft: true);
    }

    /// <summary>Execute the page wipe. Soft: all live entities become recycled (memo groups are cleared unconditionally, matching the hard path - owner call).
    /// Hard: physical removal - memo also drops its groups. Desktop stickies of the page are removed either way.</summary>
    private void PerformPageWipe(int page, bool soft)
    {
        var store = App.Store; if (store == null) return;
        var db = store.Database;
        var now = DateTime.Now;
        if (page == 0)
        {
            foreach (var x in db.MemoEntries.Where(x => !x.IsDeleted)) { if (soft) { x.IsDeleted = true; x.DeletedAt = now; } }
            
            db.MemoGroups.Clear();
            if (!soft) db.MemoEntries.RemoveAll(x => true);
        }
        else if (page == 1)
        {
            foreach (var x in db.PathBackupItems.Where(x => !x.IsDeleted)) { if (soft) { x.IsDeleted = true; x.DeletedAt = now; } }
            if (!soft) db.PathBackupItems.RemoveAll(x => true);
        }
        else if (page == 2)
        {
            foreach (var t in db.TodoCards.Where(x => !x.IsDeleted).ToList())
            {
                if (soft) { t.IsDeleted = true; t.DeletedAt = now; }
                if (Services.StickySync.Contains(t.Id.ToString())) Services.StickySync.RemoveNote(t.Id.ToString());
            }
            foreach (var n in db.NoteCards.Where(x => !x.IsDeleted).ToList())
            {
                if (soft) { n.IsDeleted = true; n.DeletedAt = now; }
                if (Services.StickySync.Contains(n.Id.ToString())) Services.StickySync.RemoveNote(n.Id.ToString());
            }
            if (!soft) { db.TodoCards.RemoveAll(x => true); db.NoteCards.RemoveAll(x => true); }
        }
        else
        {
            foreach (var x in db.DiaryItems.Where(x => !x.IsDeleted)) { if (soft) { x.IsDeleted = true; x.DeletedAt = now; } }
            if (!soft) db.DiaryItems.RemoveAll(x => true);
        }
        _ = store.SaveAsync();
        App.MainWindow?.ReloadPages();
        App.ShowToast(App.GetString(soft ? "Common_Toast_Deleted" : "DataWipe_Done_Hard"));
    }

    private void ShowResetConfirmDialog()
    {
        ResetConfirmDialogTransform.ScaleX = 0.94; ResetConfirmDialogTransform.ScaleY = 0.94; ResetConfirmDialogTransform.TranslateY = 24;
        ResetConfirmDialog.Opacity = 0;
        ResetConfirmScrim.Opacity = 0;
        ResetConfirmOverlay.Visibility = Visibility.Visible;
        var sb = new Storyboard();
        var si = new DoubleAnimation { To = 1, Duration = TimeSpan.FromMilliseconds(250) };
        Storyboard.SetTarget(si, ResetConfirmScrim); Storyboard.SetTargetProperty(si, "Opacity"); sb.Children.Add(si);
        var di = new DoubleAnimation { To = 1, Duration = TimeSpan.FromMilliseconds(300) };
        Storyboard.SetTarget(di, ResetConfirmDialog); Storyboard.SetTargetProperty(di, "Opacity"); sb.Children.Add(di);
        Motion.AddDialogShowTransform(sb, ResetConfirmDialogTransform); // M1: EmphasizedDecelerate (Motion)
        sb.Begin();
    }

    private void WebsiteButton_Click(object sender, RoutedEventArgs e)
    {
        // 4.0: open the official Novara website in the default browser.
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("https://novara.xin") { UseShellExecute = true }); }
        catch { }
    }

    private void ExportViewerButton_Click(object sender, RoutedEventArgs e)
    {
        
        UpdateConnectConfirmState(); 
        _animConnectIntro = false; // M2: show entry resets the hide guard
        ShowOverlay(ConnectIntroOverlay, ConnectIntroDialog, ConnectIntroDialogTransform);
    }

    private void ConnectChk_Changed(object sender, RoutedEventArgs e)
    {
        if (ConnectConfirmButton == null) return; 
        UpdateConnectConfirmState();
    }

    private void UpdateConnectConfirmState()
    {
        
        bool any = ConnectChkMemo.IsChecked == true || ConnectChkFile.IsChecked == true
            || ConnectChkPlan.IsChecked == true || ConnectChkDiary.IsChecked == true;
        ConnectConfirmButton.IsEnabled = any;
    }

    private void ConnectIntroClose_Click(object sender, RoutedEventArgs e)
        => HideConnectIntroDialog();

    private void ConnectIntroScrim_Tapped(object sender, TappedRoutedEventArgs e)
    {
        if (ReferenceEquals(e.OriginalSource, ConnectIntroScrim))
            HideConnectIntroDialog();
    }

    private void HideConnectIntroDialog(Action? after = null)
    {
        if (_animConnectIntro) return; // M2 hide re-entry guard
        _animConnectIntro = true;
        HideOverlay(ConnectIntroOverlay, ConnectIntroDialog, ConnectIntroDialogTransform, () => { _animConnectIntro = false; after?.Invoke(); });
    }

    private const string SnapshotGuideUrl = "https://novara.xin/snapshot-guide.html";

    private void DeployHelp_Click(object sender, RoutedEventArgs e)
    {
        
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(SnapshotGuideUrl) { UseShellExecute = true }); }
        catch { App.ShowToast(App.GetString("Connect_DeployHelp_Fail")); }
    }

    private void ConnectConfirm_Click(object sender, RoutedEventArgs e)
    {
        
        HideConnectIntroDialog(() =>
        {
            if (_isPrivacyLockEnabled) ShowConnectPwdDialog();
            else ShowConnectLockNoticeDialog();
        });
    }

    
    private void ShowConnectPwdDialog()
    {
        ConnectPwdUseLock.IsChecked = false;
        ConnectPwdPasswordBox.Text = "";
        ConnectPwdConfirmBox.Text = "";
        ConnectPwdStrengthText.Visibility = Visibility.Collapsed;
        ConnectPwdConfirmButton.IsEnabled = false; // M5
        _animConnectPwd = false; // M2: show entry resets the hide guard
        ShowOverlay(ConnectPwdOverlay, ConnectPwdDialog, ConnectPwdDialogTransform);
    }

    private void HideConnectPwdDialog()
    {
        if (_animConnectPwd) return; // M2 hide re-entry guard
        _animConnectPwd = true;
        HideOverlay(ConnectPwdOverlay, ConnectPwdDialog, ConnectPwdDialogTransform, () => _animConnectPwd = false);
    }

    private void ConnectPwdClose_Click(object sender, RoutedEventArgs e) => HideConnectPwdDialog();
    private void ConnectPwdCancel_Click(object sender, RoutedEventArgs e) => HideConnectPwdDialog();
    private void ConnectPwdScrim_Tapped(object sender, TappedRoutedEventArgs e)
    { if (ReferenceEquals(e.OriginalSource, ConnectPwdScrim)) HideConnectPwdDialog(); }

    private void ConnectPwdUseLock_Changed(object sender, RoutedEventArgs e)
    {
        
        bool useLock = ConnectPwdUseLock.IsChecked == true;
        ConnectPwdPasswordBox.IsEnabled = !useLock;
        ConnectPwdConfirmBox.IsEnabled = !useLock;
        if (useLock) { ConnectPwdPasswordBox.Text = ""; ConnectPwdConfirmBox.Text = ""; ConnectPwdStrengthText.Visibility = Visibility.Collapsed; }
        UpdateConnectPwdConfirmState();
    }

    private void ConnectPwdPasswordBox_TextChanged(object sender, TextChangedEventArgs e) => UpdateConnectPwdStrengthAndConfirm();
    private void ConnectPwdConfirmBox_TextChanged(object sender, TextChangedEventArgs e) => UpdateConnectPwdStrengthAndConfirm();

    private void UpdateConnectPwdStrengthAndConfirm()
    {
        var pw = ConnectPwdPasswordBox.Text;
        if (pw.Length == 0) { ConnectPwdStrengthText.Visibility = Visibility.Collapsed; }
        else
        {
            var rating = Novara.Services.PasswordStrength.Rate(pw);
            ConnectPwdStrengthText.Visibility = Visibility.Visible;
            switch (rating)
            {
                case Novara.Services.BackupPasswordStrength.Strong:
                    ConnectPwdStrengthText.Text = App.GetString("Setting_EncBackup_StrengthStrong");
                    ConnectPwdStrengthText.Foreground = new SolidColorBrush(Color.FromArgb(0xFF, 0x4C, 0xAF, 0x50));
                    break;
                case Novara.Services.BackupPasswordStrength.Medium:
                    ConnectPwdStrengthText.Text = App.GetString("Setting_EncBackup_StrengthMedium");
                    ConnectPwdStrengthText.Foreground = new SolidColorBrush(Color.FromArgb(0xFF, 0xFF, 0xD7, 0x00));
                    break;
                default:
                    ConnectPwdStrengthText.Text = App.GetString("Setting_EncBackup_StrengthWeak");
                    ConnectPwdStrengthText.Foreground = new SolidColorBrush(Color.FromArgb(0xFF, 0xFF, 0x45, 0x45));
                    break;
            }
        }
        UpdateConnectPwdConfirmState();
    }

    private void UpdateConnectPwdConfirmState()
    {
        
        bool valid = ConnectPwdUseLock.IsChecked == true
            || (ConnectPwdPasswordBox.Text.Length > 0 && ConnectPwdPasswordBox.Text == ConnectPwdConfirmBox.Text);
        ConnectPwdConfirmButton.IsEnabled = valid;
    }

    private async void ConnectPwdConfirm_Click(object sender, RoutedEventArgs e)
    {
        
        if (_pickerFlowBusy) return; // N5T1-05: one FileSavePicker flow at a time - a second concurrent picker throws COM
        _pickerFlowBusy = true;
        try
        {
            HideConnectPwdDialog();
            await ExportSnapshotFlowAsync();
        }
        finally { _pickerFlowBusy = false; }
    }

    private async System.Threading.Tasks.Task ExportSnapshotFlowAsync()
    {
        try 
        {
            
            string? password = ConnectPwdUseLock.IsChecked == true ? App.Store?.Password : ConnectPwdPasswordBox.Text;
            if (string.IsNullOrEmpty(password))
            {
                ShowImportResult(App.GetString("Setting_Export_Fail"), App.GetString("Setting_Export_FailDesc"));
                return;
            }

            bool includeMemo = ConnectChkMemo.IsChecked == true;
            bool includePaths = ConnectChkFile.IsChecked == true;
            bool includePlan = ConnectChkPlan.IsChecked == true;
            bool includeDiary = ConnectChkDiary.IsChecked == true;

            var cipher = App.Store?.ExportSnapshotCipher(password, includeMemo, includePaths, includePlan, includeDiary);
            if (cipher == null)
            {
                ShowImportResult(App.GetString("Setting_Export_Fail"), App.GetString("Setting_Export_FailDesc"));
                return;
            }

            var template = LoadSnapshotTemplate();
            if (template == null)
            {
                ShowImportResult(App.GetString("Setting_Export_Fail"), App.GetString("Setting_Export_FailDesc"));
                return;
            }

            var picker = new FileSavePicker();
            InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(App.MainWindow));
            picker.FileTypeChoices.Add("Novara Snapshot", new List<string> { ".html" });
            picker.SuggestedFileName = string.Format("NovaraSnapshot_{0:yyyyMMdd_HHmmss}", DateTime.Now);
            var file = await picker.PickSaveFileAsync();
            if (file == null) return;

            var exportedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            var version = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "6.2.0";
            var html = template
                .Replace("__CIPHER_BASE64__", cipher)
                .Replace("__EXPORTED_AT__", exportedAt)
                .Replace("__VERSION__", version)
                .Replace("__LANGUAGE__", App.CurrentLanguage);

            await File.WriteAllTextAsync(file.Path, html, new System.Text.UTF8Encoding(false));
            ShowImportResult(App.GetString("Setting_Export_Success"), App.GetString("Connect_Export_SuccessDesc"));
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"导出离线查看器失败: {ex}");
            ShowImportResult(App.GetString("Setting_Export_Fail"), App.GetString("Setting_Export_FailDesc"));
        }
    }

    private string? LoadSnapshotTemplate()
    {
        try
        {
            
            var bundled = Path.Combine(AppContext.BaseDirectory, "snapshot-viewer.html");
            if (File.Exists(bundled)) return File.ReadAllText(bundled);
            var src = Path.Combine(AppContext.BaseDirectory, "SnapshotViewer", "index.html");
            if (File.Exists(src)) return File.ReadAllText(src);
            return null;
        }
        catch { return null; }
    }

    
    private void ShowConnectLockNoticeDialog()
    {
        _animConnectLockNotice = false; // M2: show entry resets the hide guard
        ShowOverlay(ConnectLockNoticeOverlay, ConnectLockNoticeDialog, ConnectLockNoticeDialogTransform);
    }

    private void HideConnectLockNoticeDialog()
    {
        if (_animConnectLockNotice) return; // M2 hide re-entry guard
        _animConnectLockNotice = true;
        HideOverlay(ConnectLockNoticeOverlay, ConnectLockNoticeDialog, ConnectLockNoticeDialogTransform, () => _animConnectLockNotice = false);
    }

    private void ConnectLockNoticeClose_Click(object sender, RoutedEventArgs e) => HideConnectLockNoticeDialog();
    private void ConnectLockNoticeCancel_Click(object sender, RoutedEventArgs e) => HideConnectLockNoticeDialog();
    private void ConnectLockNoticeScrim_Tapped(object sender, TappedRoutedEventArgs e)
    { if (ReferenceEquals(e.OriginalSource, ConnectLockNoticeScrim)) HideConnectLockNoticeDialog(); }

    private void HideResetConfirmDialog()
    {
        if (_animResetConfirm) return; // D3 (Round 5): M2 hide re-entry guard
        _animResetConfirm = true;
        ResetConfirmCd.Reset(); // 9.3: stop the cooldown frame - reopening starts a fresh 30s
        ResetConfirmButton.IsEnabled = true;
        ResetConfirmButton.Opacity = 1;
        var sb = new Storyboard();
        var so = new DoubleAnimation { To = 0, Duration = TimeSpan.FromMilliseconds(200) };
        Storyboard.SetTarget(so, ResetConfirmScrim); Storyboard.SetTargetProperty(so, "Opacity"); sb.Children.Add(so);
        var d = new DoubleAnimation { To = 0, Duration = TimeSpan.FromMilliseconds(200) };
        Storyboard.SetTarget(d, ResetConfirmDialog); Storyboard.SetTargetProperty(d, "Opacity"); sb.Children.Add(d);
        Motion.AddDialogHideTransform(sb, ResetConfirmDialogTransform); // M1: EmphasizedAccelerate (Motion)
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
        
        // extra IsEncrypted check made users type it twice (page wipe verifies once via the same gate).
        // N4-35: the busy flag is NOT reset here - a second click inside the 200-250ms hide window
        // must stay blocked; ShowResetConfirmDialogFlow re-arms it on entry.
        PerformReset();
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
        ResetPasswordDialogTransform.ScaleX = 0.94; ResetPasswordDialogTransform.ScaleY = 0.94; ResetPasswordDialogTransform.TranslateY = 24;
        ResetPasswordDialog.Opacity = 0;
        ResetPasswordScrim.Opacity = 0;
        ResetPasswordOverlay.Visibility = Visibility.Visible;
        var sb = new Storyboard();
        var si = new DoubleAnimation { To = 1, Duration = TimeSpan.FromMilliseconds(250) };
        Storyboard.SetTarget(si, ResetPasswordScrim); Storyboard.SetTargetProperty(si, "Opacity"); sb.Children.Add(si);
        var di = new DoubleAnimation { To = 1, Duration = TimeSpan.FromMilliseconds(300) };
        Storyboard.SetTarget(di, ResetPasswordDialog); Storyboard.SetTargetProperty(di, "Opacity"); sb.Children.Add(di);
        Motion.AddDialogShowTransform(sb, ResetPasswordDialogTransform); // M1: EmphasizedDecelerate (Motion)
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
        Motion.AddDialogHideTransform(sb, ResetPasswordDialogTransform); // M1: EmphasizedAccelerate (Motion)
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
    private bool _animEncExport;      
    private bool _animEncImportPwd;   
    private bool _animConnectPwd;     
    private bool _animConnectIntro;   
    private bool _animConnectLockNotice; 
    private string? _pendingEncImportPath; 

    
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
    private bool _animMcpPerm; 
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
        cts.Dispose(); 
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
        var encExportItem = MakeItem(App.GetString("Setting_Archive_ExportEncrypted")); // 9.2#6: encrypted .novaenc export
        var csvExportItem = MakeItem(App.GetString("Setting_CsvExport"));
        var htmlExportItem = MakeItem(App.GetString("Setting_ExportHtml"));
        var pdfExportItem = MakeItem(App.GetString("Setting_ExportPdf"));
        mdItem.Click += (_, _) => RunPrivacyGated(() => ShowPlainExportWarn(ShowExportMdNoticeDialog)); // N4T-07(A) + N2-13
        nativeExportItem.Click += (_, _) => ExportNativeFlow();
        encExportItem.Click += (_, _) => RunPrivacyGated(ShowEncExportDialog); // N4T-07(A): verify the lock password every time (4.6 - no session exemption)
        csvExportItem.Click += (_, _) => RunPrivacyGated(() => ShowPlainExportWarn(ShowCsvExportNoticeDialog)); // N4T-07(A) + N2-13
        htmlExportItem.Click += (_, _) => RunPrivacyGated(() => ShowPlainExportWarn(() => _ = ExportHtmlFlowAsync())); // N4T-07(A) + N2-13
        pdfExportItem.Click += (_, _) => RunPrivacyGated(() => ShowPlainExportWarn(() => _ = ExportPdfFlowAsync())); // N4T-07(A) + N2-13
        exportSub.Items.Add(mdItem);
        exportSub.Items.Add(nativeExportItem);
        exportSub.Items.Add(encExportItem);
        exportSub.Items.Add(csvExportItem);
        exportSub.Items.Add(htmlExportItem);
        exportSub.Items.Add(pdfExportItem);

        menu.Items.Add(importSub);
        menu.Items.Add(exportSub);
        menu.ShowAt(ImportExportButton, new Windows.Foundation.Point(0, ImportExportButton.ActualHeight + 4));
    }

    private bool _animExportPlainWarn; // N2-13: plaintext-export warning dialog hide guard
    private Action? _pendingPlainExportContinue; // N2-13: the export flow to run after the user acknowledges the warning

    /// <summary>N2-13 (9.2#6 spec): every PLAINTEXT export path (native/CSV/MD/HTML/PDF) must pass
    /// this warning first - the exported file carries all sensitive data unencrypted. The encrypted
    /// .novaenc export does NOT warn (ciphertext). The confirmed action runs in the confirm click,
    /// after the hide animation is merely started (per-overlay generation keeps the two overlays
    /// independent - ImportConfirmContinue_Click is the same-shape precedent).</summary>
    private void ShowPlainExportWarn(Action continueAction)
    {
        _animExportPlainWarn = false; // show re-arms the hide guard (N2-11 parity)
        _pendingPlainExportContinue = continueAction;
        ExportPlainWarnIcon.Data = App.CreateGeometry(IconData.Danger); // N2-13: warning triangle (red, matches title)
        ExportPlainWarnTitle.Text = App.GetString("ExportPlain_WarnTitle");
        ExportPlainWarnBody.Text = App.GetString("ExportPlain_WarnBody");
        ExportPlainWarnCancelText.Text = App.GetString("Common_Button_Cancel");
        ExportPlainWarnConfirmText.Text = App.GetString("ExportPlain_WarnConfirm");
        ShowOverlay(ExportPlainWarnOverlay, ExportPlainWarnDialog, ExportPlainWarnDialogTransform);
    }

    private void HidePlainExportWarnDialog()
    {
        if (_animExportPlainWarn) return;
        _animExportPlainWarn = true;
        HideOverlay(ExportPlainWarnOverlay, ExportPlainWarnDialog, ExportPlainWarnDialogTransform, () => _animExportPlainWarn = false);
    }

    private void ExportPlainWarnConfirm_Click(object sender, RoutedEventArgs e)
    {
        HidePlainExportWarnDialog();
        _pendingPlainExportContinue?.Invoke();
        _pendingPlainExportContinue = null;
    }

    private void ExportPlainWarnCancel_Click(object sender, RoutedEventArgs e) { _pendingPlainExportContinue = null; HidePlainExportWarnDialog(); }
    private void ExportPlainWarnClose_Click(object sender, RoutedEventArgs e) { _pendingPlainExportContinue = null; HidePlainExportWarnDialog(); }
    private void ExportPlainWarnScrim_Tapped(object sender, TappedRoutedEventArgs e)
    {
        if (ReferenceEquals(e.OriginalSource, ExportPlainWarnScrim)) { _pendingPlainExportContinue = null; HidePlainExportWarnDialog(); }
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
            ShowPlainExportWarn(ShowExportOptionsDialog); // N2-13: plaintext native export warns first
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
        picker.FileTypeFilter.Add(".novaenc"); 
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
        if (result != null && result.Status == LoadStatus.NeedPassword)
        {
            // 9.2#6: encrypted export backup (v4) - ask for the backup password, then re-enter.
            // The header-only first pass touched no in-memory state (short-circuit before the swap).
            _pendingEncImportPath = path;
            ShowEncImportPwdDialog();
            return;
        }
        HandleImportResult(result);
    }

    private void HandleImportResult(LoadResult? result)
    {
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
            
            
            _isPrivacyLockEnabled = imported.PrivacyLockEnabled;
            _backupEnabled = imported.BackupEnabled;
            _statsEnabled = imported.StatsEnabled;
            _welcomeOnLaunch = imported.WelcomeOnLaunch;
            _currentTheme = string.IsNullOrEmpty(imported.Theme) ? "跟随系统" : imported.Theme;
            _currentLanguage = imported.AppLanguage ?? "";
            UpdatePrivacyLockUI();
            AutoBackupService.SyncAutoBackupTimer(DispatcherQueue); // N2-12: the imported backup setting must take over the timer NOW - "on" without sync = silently dead, "off" without sync = the old timer keeps snapshotting until the next sync point
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
        ImportExportPasswordDialogTransform.ScaleX = 0.94; ImportExportPasswordDialogTransform.ScaleY = 0.94; ImportExportPasswordDialogTransform.TranslateY = 24;
        ImportExportPasswordDialog.Opacity = 0;
        ImportExportPasswordScrim.Opacity = 0;
        ImportExportPasswordOverlay.Visibility = Visibility.Visible;
        var sb = new Storyboard();
        var si = new DoubleAnimation { To = 1, Duration = TimeSpan.FromMilliseconds(250) };
        Storyboard.SetTarget(si, ImportExportPasswordScrim); Storyboard.SetTargetProperty(si, "Opacity"); sb.Children.Add(si);
        var di = new DoubleAnimation { To = 1, Duration = TimeSpan.FromMilliseconds(300) };
        Storyboard.SetTarget(di, ImportExportPasswordDialog); Storyboard.SetTargetProperty(di, "Opacity"); sb.Children.Add(di);
        Motion.AddDialogShowTransform(sb, ImportExportPasswordDialogTransform); // M1: EmphasizedDecelerate (Motion)
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
        Motion.AddDialogHideTransform(sb, ImportExportPasswordDialogTransform); // M1: EmphasizedAccelerate (Motion)
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
        ExportOptionsDialogTransform.ScaleX = 0.94; ExportOptionsDialogTransform.ScaleY = 0.94; ExportOptionsDialogTransform.TranslateY = 24;
        ExportOptionsDialog.Opacity = 0;
        ExportOptionsScrim.Opacity = 0;
        ExportOptionsOverlay.Visibility = Visibility.Visible;
        var sb = new Storyboard();
        var si = new DoubleAnimation { To = 1, Duration = TimeSpan.FromMilliseconds(250) };
        Storyboard.SetTarget(si, ExportOptionsScrim); Storyboard.SetTargetProperty(si, "Opacity"); sb.Children.Add(si);
        var di = new DoubleAnimation { To = 1, Duration = TimeSpan.FromMilliseconds(300) };
        Storyboard.SetTarget(di, ExportOptionsDialog); Storyboard.SetTargetProperty(di, "Opacity"); sb.Children.Add(di);
        Motion.AddDialogShowTransform(sb, ExportOptionsDialogTransform); // M1: EmphasizedDecelerate (Motion)
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
        Motion.AddDialogHideTransform(sb, ExportOptionsDialogTransform); // M1: EmphasizedAccelerate (Motion)
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

    





    private void ShowEncExportDialog()
    {
        // Text via x:Name + code assignment only (MCP UI pit #1: x:Bind text can be Disabled to empty)
        EncExportTitle.Text = App.GetString("Setting_EncBackup_Title");
        EncExportDesc.Text = App.GetString("Setting_EncBackup_Desc");
        EncUseLockCheckBox.Content = App.GetString("Setting_EncBackup_UseLock");
        EncUseLockWarn.Text = App.GetString("Setting_EncBackup_UseLockWarn");
        EncPasswordBox.PlaceholderText = App.GetString("Setting_EncBackup_PasswordHint");
        EncConfirmBox.PlaceholderText = App.GetString("Setting_EncBackup_ConfirmHint");
        EncStrengthHint.Text = App.GetString("Setting_EncBackup_StrengthHint");
        EncIncludePathsCheckBox.Content = App.GetString("Setting_ExportOptions_IncludePaths");
        EncExportCancelText.Text = App.GetString("Common_Button_Cancel");
        EncExportConfirmText.Text = App.GetString("Setting_EncBackup_ConfirmButton");
        // The lock-password checkbox only makes sense when a privacy-lock password exists
        EncUseLockCheckBox.IsChecked = false;
        EncUseLockCheckBox.Visibility = _isPrivacyLockEnabled ? Visibility.Visible : Visibility.Collapsed;
        EncUseLockWarn.Visibility = Visibility.Collapsed;
        EncPasswordBox.Text = "";
        EncConfirmBox.Text = "";
        EncStrengthText.Visibility = Visibility.Collapsed;
        EncIncludePathsCheckBox.IsChecked = false;
        EncExportConfirmButton.IsEnabled = false; // M5: confirm stays disabled until the form is valid
        _animEncExport = false; // M2: show entry resets the hide guard
        ShowOverlay(EncExportOverlay, EncExportDialog, EncExportDialogTransform);
    }

    private void HideEncExportDialog()
    {
        if (_animEncExport) return; // M2 hide re-entry guard
        _animEncExport = true;
        HideOverlay(EncExportOverlay, EncExportDialog, EncExportDialogTransform, () => _animEncExport = false);
    }

    private void EncExportClose_Click(object sender, RoutedEventArgs e) => HideEncExportDialog();
    private void EncExportCancel_Click(object sender, RoutedEventArgs e) => HideEncExportDialog();
    private void EncExportScrim_Tapped(object sender, TappedRoutedEventArgs e)
    { if (ReferenceEquals(e.OriginalSource, EncExportScrim)) HideEncExportDialog(); }

    private void EncUseLockCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        // Reuse the verified session lock password (same read channel the 5.0 Hello enable flow uses);
        // it never echoes through the password boxes.
        bool useLock = EncUseLockCheckBox.IsChecked == true;
        EncUseLockWarn.Visibility = useLock ? Visibility.Visible : Visibility.Collapsed;
        EncPasswordBox.IsEnabled = !useLock;
        EncConfirmBox.IsEnabled = !useLock;
        if (useLock)
        {
            EncPasswordBox.Text = "";
            EncConfirmBox.Text = "";
            EncStrengthText.Visibility = Visibility.Collapsed;
        }
        UpdateEncExportConfirmState();
    }

    private void EncPasswordBox_TextChanged(object sender, TextChangedEventArgs e) => UpdateEncStrengthAndConfirm();
    private void EncConfirmBox_TextChanged(object sender, TextChangedEventArgs e) => UpdateEncStrengthAndConfirm();

    private void UpdateEncStrengthAndConfirm()
    {
        var pw = EncPasswordBox.Text;
        if (pw.Length == 0)
        {
            EncStrengthText.Visibility = Visibility.Collapsed;
        }
        else
        {
            var rating = Novara.Services.PasswordStrength.Rate(pw);
            EncStrengthText.Visibility = Visibility.Visible;
            switch (rating)
            {
                case Novara.Services.BackupPasswordStrength.Strong:
                    EncStrengthText.Text = App.GetString("Setting_EncBackup_StrengthStrong");
                    EncStrengthText.Foreground = new SolidColorBrush(Color.FromArgb(0xFF, 0x4C, 0xAF, 0x50)); // green (#4CAF50, path-page green)
                    break;
                case Novara.Services.BackupPasswordStrength.Medium:
                    EncStrengthText.Text = App.GetString("Setting_EncBackup_StrengthMedium");
                    EncStrengthText.Foreground = new SolidColorBrush(Color.FromArgb(0xFF, 0xFF, 0xD7, 0x00)); // gold (#FFD700, corner-badge gold)
                    break;
                default:
                    EncStrengthText.Text = App.GetString("Setting_EncBackup_StrengthWeak");
                    EncStrengthText.Foreground = new SolidColorBrush(Color.FromArgb(0xFF, 0xFF, 0x45, 0x45)); // red (#FF4545, danger red)
                    break;
            }
        }
        UpdateEncExportConfirmState();
    }

    private void UpdateEncExportConfirmState()
    {
        // M5: hard checks stay non-empty + two-box match only (audit: no strength interception);
        // the lock-password checkbox replaces the two boxes entirely.
        bool valid = EncUseLockCheckBox.IsChecked == true
            || (EncPasswordBox.Text.Length > 0 && EncPasswordBox.Text == EncConfirmBox.Text);
        EncExportConfirmButton.IsEnabled = valid;
    }

    private async void EncExportConfirm_Click(object sender, RoutedEventArgs e)
    {
        if (_pickerFlowBusy) return; // N5T1-05
        _pickerFlowBusy = true;
        try
        {
            bool includePaths = EncIncludePathsCheckBox.IsChecked == true;
            HideEncExportDialog();
            await ExportEncBackupFlowAsync(includePaths);
        }
        finally { _pickerFlowBusy = false; }
    }

    private async System.Threading.Tasks.Task ExportEncBackupFlowAsync(bool includePaths)
    {
        try // E5-26 pattern: an exception must not escape as an unobserved task fault
        {
            var password = EncUseLockCheckBox.IsChecked == true
                ? App.Store?.Password
                : EncPasswordBox.Text;
            if (string.IsNullOrEmpty(password)) return;
            var picker = new FileSavePicker();
            InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(App.MainWindow));
            picker.FileTypeChoices.Add(App.GetString("Setting_Archive_ExportEncrypted"), new List<string> { ".novaenc" });
            picker.SuggestedFileName = string.Format("{0}_{1:yyyyMMdd_HHmmss}", App.GetString("Setting_Archive_ExportEncrypted"), DateTime.Now);
            var file = await picker.PickSaveFileAsync();
            if (file == null) return;

            bool ok = App.Store?.ExportBackupEncrypted(file.Path, password, includePaths) ?? false;
            if (ok)
            {
                // Audit 2026-08-29: the no-recovery warning deserves a blocking dialog, not a toast.
                ShowImportResult(App.GetString("Setting_EncExport_SuccessTitle"), App.GetString("Setting_EncExport_SuccessDesc"));
            }
            else
            {
                ShowImportResult(App.GetString("Setting_Export_Fail"), App.GetString("Setting_Export_FailDesc"));
            }
        }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"加密导出失败: {ex}"); }
    }

    private void ShowEncImportPwdDialog()
    {
        EncImportTitle.Text = App.GetString("Setting_EncBackup_Title");
        EncImportDesc.Text = App.GetString("Setting_ImportEnc_Desc");
        EncImportPwdBox.PlaceholderText = App.GetString("Setting_EncBackup_PasswordHint");
        EncImportCancelText.Text = App.GetString("Common_Button_Cancel");
        EncImportConfirmText.Text = App.GetString("Common_Button_Confirm");
        EncImportPwdBox.Text = "";
        EncImportConfirmButton.IsEnabled = false; // M5
        _animEncImportPwd = false; // M2: show entry resets the hide guard
        ShowOverlay(EncImportPwdOverlay, EncImportPwdDialog, EncImportPwdDialogTransform);
    }

    private void HideEncImportPwdDialog()
    {
        if (_animEncImportPwd) return; // M2 hide re-entry guard
        _animEncImportPwd = true;
        _pendingEncImportPath = null;
        HideOverlay(EncImportPwdOverlay, EncImportPwdDialog, EncImportPwdDialogTransform, () => _animEncImportPwd = false);
    }

    private void EncImportPwdClose_Click(object sender, RoutedEventArgs e) => HideEncImportPwdDialog();
    private void EncImportPwdCancel_Click(object sender, RoutedEventArgs e) => HideEncImportPwdDialog();
    private void EncImportPwdScrim_Tapped(object sender, TappedRoutedEventArgs e)
    { if (ReferenceEquals(e.OriginalSource, EncImportPwdScrim)) HideEncImportPwdDialog(); }

    private void EncImportPwdBox_TextChanged(object sender, TextChangedEventArgs e)
        => EncImportConfirmButton.IsEnabled = EncImportPwdBox.Text.Length > 0; // M5

    private void EncImportPwdConfirm_Click(object sender, RoutedEventArgs e)
    {
        if (_animEncImportPwd) return; // M2: no confirm during the hide animation
        var pw = EncImportPwdBox.Text;
        if (pw.Length == 0) { FlashTextBox(EncImportPwdBox); return; }
        var path = _pendingEncImportPath;
        if (string.IsNullOrEmpty(path)) { HideEncImportPwdDialog(); return; }
        var result = App.Store?.ImportBackup(path, pw);
        // Wrong backup password = stay in the dialog, flash, retry (auth failure is indistinguishable
        // from corruption by design, but a retry attempt costs nothing and keeps the flow alive).
        if (result != null && result.Status == LoadStatus.Corrupted && result.Detail == Loc.T("Store_Err_BackupAuthFail"))
        {
            FlashTextBox(EncImportPwdBox);
            return;
        }
        HideEncImportPwdDialog();
        HandleImportResult(result);
    }

    



    private void ShowExportMdNoticeDialog()
    {
        _animExportMdNotice = false; // N3-16: show re-arms the hide guard (M2/N2-11 parity)
        ShowOverlay(ExportMdNoticeOverlay, ExportMdNoticeDialog, ExportMdNoticeDialogTransform);
    }
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
            picker.SuggestedFileName = $"Novara_{App.GetString("Setting_Export_FileNameSummary")}_{DateTime.Now:yyyyMMdd_HHmmss}"; // N3-45
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
            picker.SuggestedFileName = $"Novara_{App.GetString("Setting_Export_FileNameCollection")}_{DateTime.Now:yyyyMMdd_HHmmss}"; // N3-45
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
        sb.AppendLine($"<title>Novara {App.GetString("Setting_Export_FileNameCollection")}</title>"); // N3-45: localized collection title
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
        sb.AppendLine("<div class=\"header\">" + logoHtml + "<div><div class=\"brand\">Novara</div><div class=\"tagline\">" + App.GetString("Export_Html_Tagline") + "</div></div></div>"); // N3-45: tagline localized
        sb.AppendLine($"<p class=\"meta\">{string.Format(App.GetString("Export_ExportedAt"), DateTime.Now.ToString("yyyy-MM-dd HH:mm"))}</p>"); // N3-45

        foreach (var d in diaries)
        {
            string title = System.Net.WebUtility.HtmlEncode(string.IsNullOrWhiteSpace(d.Title) ? App.GetString("Export_Untitled") : d.Title); // N3-45: (Untitled) localized
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

        sb.AppendLine($"<div class=\"footer\"><div class=\"slogan\">{App.GetString("Export_Html_Slogan")}</div><div>{App.GetString("Export_Website")}<a href=\"https://novara.xin\">novara.xin</a></div></div>"); // N3-45: slogan/website label localized
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
            picker.SuggestedFileName = $"Novara_{App.GetString("Setting_Export_FileNameCollection")}_{DateTime.Now:yyyyMMdd_HHmmss}"; // N3-45
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
        sb.AppendLine($"# {App.GetString("Export_Summary_Title")}"); // N3-45: summary heading localized (mixed policy - template text localized, body data untouched)
        sb.AppendLine();
        sb.AppendLine($"> {string.Format(App.GetString("Export_ExportedAt"), DateTime.Now.ToString("yyyy-MM-dd HH:mm"))}"); // N3-45
        sb.AppendLine();

        
        var entries = db.MemoEntries.Where(x => !x.IsDeleted).ToList();
        var groups = db.MemoGroups.ToList();
        sb.AppendLine($"## {App.GetString("Export_Section_Memo")}"); // N3-45
        sb.AppendLine();
        foreach (var g in groups)
        {
            sb.AppendLine($"### 📁 {g.Name}");
            var inGroup = entries.Where(e => e.GroupId == g.Id).ToList();
            if (inGroup.Count == 0) { sb.AppendLine(App.GetString("Export_Empty")); sb.AppendLine(); continue; } // N3-45
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
            sb.AppendLine($"### 📁 {App.GetString("Export_Section_Uncategorized")}"); // N3-45
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
            sb.AppendLine($"## {App.GetString("Export_Section_Path")}"); // N3-45
            sb.AppendLine();
            foreach (var p in paths)
            {
                sb.AppendLine($"- **{p.Name}**");
                sb.AppendLine($"  - {App.GetString("Export_Label_Path")}{p.Path}"); // N3-45
                if (!string.IsNullOrWhiteSpace(p.Note)) sb.AppendLine($"  - {App.GetString("Export_Label_Note")}{p.Note}"); // N3-45
            }
            sb.AppendLine();
        }

        
        var todos = db.TodoCards.Where(x => !x.IsDeleted).ToList();
        var notes = db.NoteCards.Where(x => !x.IsDeleted).ToList();
        if (todos.Count > 0 || notes.Count > 0)
        {
            sb.AppendLine($"## {App.GetString("Export_Section_Plan")}"); // N3-45
            sb.AppendLine();
            if (todos.Count > 0)
            {
                sb.AppendLine($"### {App.GetString("Export_Section_Todo")}"); // N3-45
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
                sb.AppendLine($"### {App.GetString("Export_Section_Note")}"); // N3-45
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
            sb.AppendLine($"## {App.GetString("Export_Section_Diary")}"); // N3-45
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
        ShowImportExportPasswordDialog(App.GetString("Setting_Archive_VerifyTitle"));
        // MUST run AFTER Show: its entry clears stale gated flows (N5T1-02) - assigning before Show
        // got the fresh flow wiped too, so every gated convenience channel (MD/CSV/HTML/PDF/encrypted
        // backup export, CSV import) fell through to the native dispatch after the password check
        // (2026-08-29: owner-confirmed - the encrypted-backup dialog never appeared with the lock on).
        _pendingVerifiedAction = flow;
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
        if (export) ShowPlainExportWarn(ShowExportOptionsDialog); // N4-33: the encrypted branch of ExportNativeFlow skipped the N2-13 warning - a .novabak file is plaintext regardless of the library's encryption state (same dialog the unencrypted branch shows)
        else _ = ImportAfterPasswordAsync(null); // E5-28: pre-picked-path flow removed - the file is picked after the password verify (ImportAfterPasswordAsync re-picks when null)
    }

    private void ShowImportConfirmDialog()
    {
        ImportConfirmDialogTransform.ScaleX = 0.94; ImportConfirmDialogTransform.ScaleY = 0.94; ImportConfirmDialogTransform.TranslateY = 24;
        ImportConfirmDialog.Opacity = 0;
        ImportConfirmScrim.Opacity = 0;
        ImportConfirmOverlay.Visibility = Visibility.Visible;
        var sb = new Storyboard();
        var si = new DoubleAnimation { To = 1, Duration = TimeSpan.FromMilliseconds(250) };
        Storyboard.SetTarget(si, ImportConfirmScrim); Storyboard.SetTargetProperty(si, "Opacity"); sb.Children.Add(si);
        var di = new DoubleAnimation { To = 1, Duration = TimeSpan.FromMilliseconds(300) };
        Storyboard.SetTarget(di, ImportConfirmDialog); Storyboard.SetTargetProperty(di, "Opacity"); sb.Children.Add(di);
        Motion.AddDialogShowTransform(sb, ImportConfirmDialogTransform); // M1: EmphasizedDecelerate (Motion)
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
        Motion.AddDialogHideTransform(sb, ImportConfirmDialogTransform); // M1: EmphasizedAccelerate (Motion)
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
        ImportResultDialogTransform.ScaleX = 0.94; ImportResultDialogTransform.ScaleY = 0.94; ImportResultDialogTransform.TranslateY = 24;
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
        Motion.AddDialogShowTransform(sb, ImportResultDialogTransform); // M1: EmphasizedDecelerate (Motion)
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
        Motion.AddDialogHideTransform(sb, ImportResultDialogTransform); // M1: EmphasizedAccelerate (Motion)
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

        CsvImportPreviewDialogTransform.ScaleX = 0.94; CsvImportPreviewDialogTransform.ScaleY = 0.94; CsvImportPreviewDialogTransform.TranslateY = 24;
        CsvImportPreviewDialog.Opacity = 0;
        CsvImportPreviewScrim.Opacity = 0;
        CsvImportPreviewOverlay.Visibility = Visibility.Visible;
        FitDialogScroll(CsvImportPreviewScroll, 270); 
        var sb = new Storyboard();
        var si = new DoubleAnimation { To = 1, Duration = TimeSpan.FromMilliseconds(250) };
        Storyboard.SetTarget(si, CsvImportPreviewScrim); Storyboard.SetTargetProperty(si, "Opacity"); sb.Children.Add(si);
        var di = new DoubleAnimation { To = 1, Duration = TimeSpan.FromMilliseconds(300) };
        Storyboard.SetTarget(di, CsvImportPreviewDialog); Storyboard.SetTargetProperty(di, "Opacity"); sb.Children.Add(di);
        Motion.AddDialogShowTransform(sb, CsvImportPreviewDialogTransform); // M1: EmphasizedDecelerate (Motion)
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
        Motion.AddDialogHideTransform(sb, CsvImportPreviewDialogTransform); // M1: EmphasizedAccelerate (Motion)
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
                Text = $"  {CsvTypeLabel(entry.Type)}  {entry.Name}",
                FontSize = 13,
                Foreground = App.GetBrush("AppTextSecondaryBrush"),
                Margin = new Thickness(0, 0, 0, 6),
                TextWrapping = TextWrapping.NoWrap,
                TextTrimming = TextTrimming.CharacterEllipsis,
            });
        }
        if (result.Entries.Count == 0)
            CsvImportPreviewList.Children.Add(new TextBlock { Text = App.GetString("Setting_CsvImport_EmptyPreview"), FontSize = 13, Foreground = App.GetBrush("AppTextTertiaryBrush") });
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
            entry.WorkspaceId = App.CurrentWorkspaceId; 
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

        CsvExportNoticeDialogTransform.ScaleX = 0.94; CsvExportNoticeDialogTransform.ScaleY = 0.94; CsvExportNoticeDialogTransform.TranslateY = 24;
        CsvExportNoticeDialog.Opacity = 0;
        CsvExportNoticeScrim.Opacity = 0;
        CsvExportNoticeOverlay.Visibility = Visibility.Visible;
        var sb = new Storyboard();
        var si = new DoubleAnimation { To = 1, Duration = TimeSpan.FromMilliseconds(250) };
        Storyboard.SetTarget(si, CsvExportNoticeScrim); Storyboard.SetTargetProperty(si, "Opacity"); sb.Children.Add(si);
        var di = new DoubleAnimation { To = 1, Duration = TimeSpan.FromMilliseconds(300) };
        Storyboard.SetTarget(di, CsvExportNoticeDialog); Storyboard.SetTargetProperty(di, "Opacity"); sb.Children.Add(di);
        Motion.AddDialogShowTransform(sb, CsvExportNoticeDialogTransform); // M1: EmphasizedDecelerate (Motion)
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
        Motion.AddDialogHideTransform(sb, CsvExportNoticeDialogTransform); // M1: EmphasizedAccelerate (Motion)
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

    
    
    private void FitDialogScroll(ScrollViewer scroll, double chrome)
        => scroll.MaxHeight = Math.Max(120, Math.Min(scroll.MaxHeight, SettingsRoot.ActualHeight - 60 - chrome));

    private void ShowOverlay(Grid overlay, Border dialog, CompositeTransform transform)
    {
        _overlayAnimGens[overlay] = System.Collections.Generic.CollectionExtensions.GetValueOrDefault(_overlayAnimGens, overlay) + 1; 
        var scrim = FindOverlayScrim(overlay);
        transform.ScaleX = 0.94; transform.ScaleY = 0.94; transform.TranslateY = 24;
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
        Motion.AddDialogShowTransform(sb, transform); // M1: EmphasizedDecelerate (Motion)
        
        
        dialog.MaxHeight = Math.Max(360, SettingsRoot.ActualHeight - 60);
        _overlayDialogs[overlay] = dialog; 
        sb.Begin();
    }

    
    private void ReclampVisibleDialogs()
    {
        foreach (var (overlay, dialog) in _overlayDialogs)
            if (overlay.Visibility == Visibility.Visible)
                dialog.MaxHeight = Math.Max(360, SettingsRoot.ActualHeight - 60);
    }

    private void HideOverlay(Grid overlay, Border dialog, CompositeTransform transform, Action onCompleted)
    {
        var gen = System.Collections.Generic.CollectionExtensions.GetValueOrDefault(_overlayAnimGens, overlay); // N2-05: capture THIS overlay's generation - an in-flight re-Show of the SAME dialog invalidates it; sibling dialog Shows no longer do
        var scrim = FindOverlayScrim(overlay);
        var sb = new Storyboard();
        if (scrim != null)
        {
            var so = new DoubleAnimation { To = 0, Duration = TimeSpan.FromMilliseconds(200) };
            Storyboard.SetTarget(so, scrim); Storyboard.SetTargetProperty(so, "Opacity"); sb.Children.Add(so);
        }
        var di = new DoubleAnimation { To = 0, Duration = TimeSpan.FromMilliseconds(200) };
        Storyboard.SetTarget(di, dialog); Storyboard.SetTargetProperty(di, "Opacity"); sb.Children.Add(di);
        Motion.AddDialogHideTransform(sb, transform); // M1: EmphasizedAccelerate (Motion)
        sb.Completed += (_, _) =>
        {
            
            
            if (gen == System.Collections.Generic.CollectionExtensions.GetValueOrDefault(_overlayAnimGens, overlay)) overlay.Visibility = Visibility.Collapsed;
            onCompleted();
        };
        sb.Begin();
    }

    
    private void ShowBackupIntroDialog()
    {
        _animBackupIntro = false; // N3-16: show re-arms the hide guard (M2/N2-11 parity)
        ShowOverlay(BackupIntroOverlay, BackupIntroDialog, BackupIntroDialogTransform);
    }
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

    
    private void ShowBackupNowDialog()
    {
        _animBackupNow = false; // N3-16: show re-arms the hide guard (M2/N2-11 parity)
        ShowOverlay(BackupNowOverlay, BackupNowDialog, BackupNowDialogTransform);
    }
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
        _animBackupAuto = false; // N3-16: show re-arms the hide guard (M2/N2-11 parity)
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
        _animBackupRestore = false; // N3-16: show re-arms the hide guard (M2/N2-11 parity)
        BuildBackupList(BackupRestoreList, singleSelect: true);
        ShowOverlay(BackupRestoreOverlay, BackupRestoreDialog, BackupRestoreDialogTransform);
        FitDialogScroll(BackupRestoreScroll, 200); 
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

    
    private void ShowBackupRestoreConfirmDialog()
    {
        _animBackupRestoreConfirm = false; // N3-16: show re-arms the hide guard (M2/N2-11 parity)
        ShowOverlay(BackupRestoreConfirmOverlay, BackupRestoreConfirmDialog, BackupRestoreConfirmDialogTransform);
    }
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
            _pendingRestoreSnapshot = null; 
            App.ShowToast(App.GetString("Store_Err_IoFail"));
        }
    }
    private void BackupRestoreConfirmCancel_Click(object sender, RoutedEventArgs e) => CancelBackupRestoreConfirm();
    private void BackupRestoreConfirmClose_Click(object sender, RoutedEventArgs e) => CancelBackupRestoreConfirm();
    private void BackupRestoreConfirmScrim_Tapped(object sender, TappedRoutedEventArgs e)
    { if (ReferenceEquals(e.OriginalSource, BackupRestoreConfirmScrim)) CancelBackupRestoreConfirm(); }

    
    private void ShowBackupDeleteDialog()
    {
        _animBackupDelete = false; // N3-16: show re-arms the hide guard (M2/N2-11 parity)
        BuildBackupList(BackupDeleteList, singleSelect: false);
        ShowOverlay(BackupDeleteOverlay, BackupDeleteDialog, BackupDeleteDialogTransform);
        FitDialogScroll(BackupDeleteScroll, 210); 
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
        _animBackupDeleteConfirm = false; // N3-16: show re-arms the hide guard (M2/N2-11 parity)
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

    
    private void ShowBackupRestartDialog() => ShowOverlay(BackupRestartOverlay, BackupRestartDialog, BackupRestartDialogTransform); // no hide guard - forced restart, single exit (Ok)
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
        string firstUse = times.Count > 0 ? times.Min().ToString("yyyy-MM-dd HH:mm") : App.GetString("Setting_Stats_Never"); 

        
        var snapshots = Services.AutoBackupService.ListSnapshots();
        var lastBackup = snapshots.Count > 0
            ? snapshots.Max(s => s.Timestamp).ToString("yyyy-MM-dd HH:mm")
            : App.GetString("Setting_Stats_None");
        string integrityValue;
        bool integrityWarn;
        var integrity = Services.DatabaseHealth.VerifyDataFile(Services.NovaraStore.DefaultFilePath); 
        switch (integrity)
        {
            case Novara.Services.DataFileIntegrity.DigestVerified:
                integrityValue = "✓"; integrityWarn = false; break;
            case Novara.Services.DataFileIntegrity.EncryptedStructured:
                integrityValue = App.GetString("Setting_Stats_IntegrityEnc"); integrityWarn = false; break;
            default:
                integrityValue = "✗"; integrityWarn = true; break;
        }
        var orphans = Novara.Services.DatabaseHealth.CountOrphanMemoEntries(db);
        bool orphanWarn = orphans > 0;

        
        bool showMemo = _visibleTabs.Contains("备忘");
        bool showFile = _visibleTabs.Contains("文件");
        bool showPlan = _visibleTabs.Contains("计划");
        bool showDiary = _visibleTabs.Contains("日记");

        var blocks = new List<(string label, string value, bool show, bool warn)>
        {
            (App.GetString("Setting_Stats_MemoEntries"), entries.Count.ToString(), showMemo, false),
            (App.GetString("Setting_Stats_Groups"), db.MemoGroups.Count.ToString(), showMemo, false),
            (App.GetString("Setting_Stats_Paths"), paths.Count.ToString(), showFile, false),
            (App.GetString("Setting_Stats_Todos"), todos.Count.ToString(), showPlan, false),
            (App.GetString("Setting_Stats_Notes"), notes.Count.ToString(), showPlan, false),
            (App.GetString("Setting_Stats_Diaries"), diaries.Count.ToString(), showDiary, false),
            (App.GetString("Setting_Stats_Documents"), documents.Count.ToString(), showDiary, false),
            (App.GetString("Setting_Stats_Completion"), $"{completion:F0}%", showPlan, false),
            (App.GetString("Setting_Stats_Storage"), storage, true, false),
            (App.GetString("Setting_Stats_Starred"),
                (entries.Count(e => e.IsStarred) + paths.Count(p => p.IsStarred) + todos.Count(t => t.IsStarred) + notes.Count(n => n.IsStarred) + diaries.Count(d => d.IsStarred) + documents.Count(d => d.IsStarred)).ToString(), true, false),
            (App.GetString("Setting_Stats_Trash"),
                (db.MemoEntries.Count(x => x.IsDeleted) + db.PathBackupItems.Count(x => x.IsDeleted) + db.TodoCards.Count(x => x.IsDeleted) + db.NoteCards.Count(x => x.IsDeleted) + db.DiaryItems.Count(x => x.IsDeleted)).ToString(), true, false),
            (App.GetString("Setting_Stats_LastActive"), lastActive, true, false),
            (App.GetString("Setting_Stats_FirstUse"), firstUse, true, false),
            (App.GetString("Setting_Stats_Encryption"),
                (App.Store is { IsEncrypted: true } ? App.GetString("Setting_Stats_EncOn") : App.GetString("Setting_Stats_EncOff")), true, false),
            (App.GetString("Setting_Stats_LastBackup"), lastBackup, true, false),
            (App.GetString("Setting_Stats_SnapshotCount"), $"{snapshots.Count}/{Services.AutoBackupService.MaxSnapshots}", true, false),
            (App.GetString("Setting_Stats_Integrity"), integrityValue, true, integrityWarn),
            (App.GetString("Setting_Stats_Orphans"), orphanWarn ? orphans.ToString() : "✓", true, orphanWarn),
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
            var (label, value, _, warn) = visible[i];
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
                Foreground = warn
                    ? new SolidColorBrush(Color.FromArgb(0xFF, 0xFF, 0x45, 0x45)) // danger red (#FF4545) for failed integrity / orphan references
                    : App.GetBrush("AppPrimaryButtonBrush"),
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
            var candidate = Path.Combine(dir, "NovaraMCP", "bin", "x64", "Debug", "net8.0", "win-x64", "NovaraMCP.exe");
            if (File.Exists(candidate)) return candidate;
            candidate = Path.Combine(dir, "NovaraMCP", "bin", "Debug", "net8.0", "win-x64", "NovaraMCP.exe");
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
        // N4-12: report the real outcome (N3-14 pattern) - a locked clipboard must not toast "copied";
        // both callers copy the MCP JSON config containing the 32B token
        try { Clipboard.SetContent(dp); App.ShowToast(App.GetString("Common_Toast_Copied")); }
        catch { App.ShowToast(App.GetString("Common_Toast_CopyFail")); }
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

            // 9.2#5: per-client permission editor entry (D4 - the editor is also auto-opened right
            // after a fresh authorization; this button is the permanent access path).
            var perm = new Button
            {
                Content = new TextBlock { Text = App.GetString("Setting_Mcp_Perm_Title"), FontSize = 12 },
                Height = 28,
                MinWidth = 72,
                Padding = new Thickness(10, 0, 10, 0),
                CornerRadius = new CornerRadius(8),
                Margin = new Thickness(0, 0, 6, 0),
                VerticalAlignment = VerticalAlignment.Center,
                Tag = path,
            };
            // Page-level Resources[...] throws COMException for keys living in the app dictionary
            // (Controls.xaml) - crash log 2026-08-29 20:54 proved it ("Cannot find a resource with
            // the given key"), and the exception killed the expand flow right before the melt anim.
            var permStyle = FindAppStyle("NovaraOutlineButtonStyle");
            if (permStyle != null) perm.Style = permStyle;
            perm.Click += McpPermRowButton_Click;
            Grid.SetColumn(perm, 1);
            row.Children.Add(perm);

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
            Grid.SetColumn(revoke, 2); // col 1 = permission editor button (9.2#5)
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

    
    private void ShowMcpRevokeConfirmDialog()
    {
        _animMcpRevokeConfirm = false; // N3-16: show re-arms the hide guard (M2/N2-11 parity)
        ShowOverlay(McpRevokeConfirmOverlay, McpRevokeConfirmDialog, McpRevokeConfirmDialogTransform);
    }
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

    
    

    private string? _mcpPermPath;
    private string? _pendingMcpPermPath; // D4: authorize dialog landed before Loaded - replay on load
    private bool _mcpPermDeleteGateOff; // delete master switch off -> matrix must never carry delete bits

    private const McpPerm AllDeleteBits =
        McpPerm.MemoDelete | McpPerm.PathDelete | McpPerm.TodoDelete | McpPerm.NoteDelete | McpPerm.DiaryDelete;

    /// <summary>Public entry - MainWindow opens this right after a fresh authorization (D4).
    /// If the page is not loaded yet (fresh navigation), the request is deferred to Loaded.</summary>
    public void OpenMcpPermEditor(string clientPath)
    {
        if (!IsLoaded) { _pendingMcpPermPath = clientPath; return; }
        _mcpPermPath = clientPath;
        // Text via x:Name + code assignment only (MCP UI pit #1)
        McpPermTitle.Text = App.GetString("Setting_Mcp_Perm_Title") + " — " + System.IO.Path.GetFileName(clientPath);
        McpPermZoneMemo.Text = App.GetString("Nav_Tab_Memo");
        McpPermMemoWarn.Text = App.GetString("Setting_Mcp_Perm_MemoWarn");
        McpPermZonePath.Text = App.GetString("Nav_Tab_File");
        McpPermZoneTodo.Text = App.GetString("Setting_Stats_Todos");
        McpPermZoneNote.Text = App.GetString("Setting_Stats_Notes");
        McpPermZoneDiary.Text = App.GetString("Nav_Tab_Diary");
        McpPermColRead.Text = App.GetString("Setting_Mcp_Perm_ColRead");
        McpPermColCreate.Text = App.GetString("Setting_Mcp_Perm_ColCreate");
        McpPermColUpdate.Text = App.GetString("Setting_Mcp_Perm_ColUpdate");
        McpPermColDelete.Text = App.GetString("Setting_Mcp_Perm_ColDelete");
        McpPermSaveText.Text = App.GetString("Common_Button_Confirm");
        McpPermDeleteHint.Text = App.GetString("Setting_Mcp_Perm_DeleteLocked");
        _mcpPermDeleteGateOff = !(App.Store?.Database.AppSettings.McpDeleteEnabled ?? false); // gate state must precede SetMatrix (filter) and ApplyDeleteGate (UI)
        var bits = McpService.ReadClientPermissions(clientPath); // N2-21: read under WhitelistGate
        SetMatrix(bits, filterDelete: false); 
        ApplyDeleteGate(!_mcpPermDeleteGateOff);
        UpdateMcpPermToggleAll(); // left button label tracks the loaded state
        _animMcpPerm = false; // M2: show entry resets the hide guard
        ShowOverlay(McpPermOverlay, McpPermDialog, McpPermDialogTransform);
    }

    private void ApplyDeleteGate(bool masterOn)
    {
        McpPermChkMemoDelete.IsEnabled = masterOn;
        McpPermChkPathDelete.IsEnabled = masterOn;
        McpPermChkTodoDelete.IsEnabled = masterOn;
        McpPermChkNoteDelete.IsEnabled = masterOn;
        McpPermChkDiaryDelete.IsEnabled = masterOn;
        McpPermDeleteHint.Visibility = masterOn ? Visibility.Collapsed : Visibility.Visible;
    }

    private void SetMatrix(McpPerm bits, bool filterDelete = true)
    {
        // 9.2#5 gate: with the delete master switch off, delete bits must never land in the matrix -
        // through ANY path. The owner caught it: IsEnabled=false only blocks clicks, but the
        // toggle-all button assigns IsChecked in code and would still flip the five disabled boxes,
        // letting delete grants be saved past the gate. Filter at the single write choke point.
        
        
        if (filterDelete && _mcpPermDeleteGateOff) bits &= ~AllDeleteBits;
        McpPermChkMemoRead.IsChecked = bits.HasFlag(McpPerm.MemoRead);
        McpPermChkMemoCreate.IsChecked = bits.HasFlag(McpPerm.MemoCreate);
        McpPermChkMemoUpdate.IsChecked = bits.HasFlag(McpPerm.MemoUpdate);
        McpPermChkMemoDelete.IsChecked = bits.HasFlag(McpPerm.MemoDelete);
        McpPermChkPathRead.IsChecked = bits.HasFlag(McpPerm.PathRead);
        McpPermChkPathCreate.IsChecked = bits.HasFlag(McpPerm.PathCreate);
        McpPermChkPathUpdate.IsChecked = bits.HasFlag(McpPerm.PathUpdate);
        McpPermChkPathDelete.IsChecked = bits.HasFlag(McpPerm.PathDelete);
        McpPermChkTodoRead.IsChecked = bits.HasFlag(McpPerm.TodoRead);
        McpPermChkTodoCreate.IsChecked = bits.HasFlag(McpPerm.TodoCreate);
        McpPermChkTodoUpdate.IsChecked = bits.HasFlag(McpPerm.TodoUpdate);
        McpPermChkTodoDelete.IsChecked = bits.HasFlag(McpPerm.TodoDelete);
        McpPermChkNoteRead.IsChecked = bits.HasFlag(McpPerm.NoteRead);
        McpPermChkNoteCreate.IsChecked = bits.HasFlag(McpPerm.NoteCreate);
        McpPermChkNoteUpdate.IsChecked = bits.HasFlag(McpPerm.NoteUpdate);
        McpPermChkNoteDelete.IsChecked = bits.HasFlag(McpPerm.NoteDelete);
        McpPermChkDiaryRead.IsChecked = bits.HasFlag(McpPerm.DiaryRead);
        McpPermChkDiaryCreate.IsChecked = bits.HasFlag(McpPerm.DiaryCreate);
        McpPermChkDiaryUpdate.IsChecked = bits.HasFlag(McpPerm.DiaryUpdate);
        McpPermChkDiaryDelete.IsChecked = bits.HasFlag(McpPerm.DiaryDelete);
    }

    private McpPerm ReadMatrix()
    {
        McpPerm bits = McpPerm.None;
        if (McpPermChkMemoRead.IsChecked == true) bits |= McpPerm.MemoRead;
        if (McpPermChkMemoCreate.IsChecked == true) bits |= McpPerm.MemoCreate;
        if (McpPermChkMemoUpdate.IsChecked == true) bits |= McpPerm.MemoUpdate;
        if (McpPermChkMemoDelete.IsChecked == true) bits |= McpPerm.MemoDelete;
        if (McpPermChkPathRead.IsChecked == true) bits |= McpPerm.PathRead;
        if (McpPermChkPathCreate.IsChecked == true) bits |= McpPerm.PathCreate;
        if (McpPermChkPathUpdate.IsChecked == true) bits |= McpPerm.PathUpdate;
        if (McpPermChkPathDelete.IsChecked == true) bits |= McpPerm.PathDelete;
        if (McpPermChkTodoRead.IsChecked == true) bits |= McpPerm.TodoRead;
        if (McpPermChkTodoCreate.IsChecked == true) bits |= McpPerm.TodoCreate;
        if (McpPermChkTodoUpdate.IsChecked == true) bits |= McpPerm.TodoUpdate;
        if (McpPermChkTodoDelete.IsChecked == true) bits |= McpPerm.TodoDelete;
        if (McpPermChkNoteRead.IsChecked == true) bits |= McpPerm.NoteRead;
        if (McpPermChkNoteCreate.IsChecked == true) bits |= McpPerm.NoteCreate;
        if (McpPermChkNoteUpdate.IsChecked == true) bits |= McpPerm.NoteUpdate;
        if (McpPermChkNoteDelete.IsChecked == true) bits |= McpPerm.NoteDelete;
        if (McpPermChkDiaryRead.IsChecked == true) bits |= McpPerm.DiaryRead;
        if (McpPermChkDiaryCreate.IsChecked == true) bits |= McpPerm.DiaryCreate;
        if (McpPermChkDiaryUpdate.IsChecked == true) bits |= McpPerm.DiaryUpdate;
        if (McpPermChkDiaryDelete.IsChecked == true) bits |= McpPerm.DiaryDelete;
        return bits;
    }

    /// <summary>Left button under the matrix: its label always describes what clicking it does -
    
    private void UpdateMcpPermToggleAll()
        => McpPermToggleAllText.Text = App.GetString(
            ReadMatrix() == McpPerm.None ? "Setting_Mcp_PresetAll" : "Setting_Mcp_PresetNone");

    private void McpPermChk_Changed(object sender, RoutedEventArgs e) => UpdateMcpPermToggleAll();

    private void McpPermToggleAll_Click(object sender, RoutedEventArgs e)
    {
        
        SetMatrix(ReadMatrix() == McpPerm.None ? McpPermissions.LegacyFull : McpPerm.None, filterDelete: false);
        UpdateMcpPermToggleAll();
    }

    private void McpPermSave_Click(object sender, RoutedEventArgs e)
    {
        var path = _mcpPermPath;
        if (string.IsNullOrEmpty(path)) { HideMcpPermDialog(); return; }
        var settings = App.Store?.Database.AppSettings;
        if (settings == null) { HideMcpPermDialog(); return; }
        var bits = ReadMatrix();
        McpService.SaveClientPermissions(path, bits); // N2-21: write under WhitelistGate
        PersistSetting(_ => { }); // empty mutate - the record above is already in place, this just persists
        BuildMcpAuthorizedList();
        HideMcpPermDialog();
    }

    private void McpPermCancel_Click(object sender, RoutedEventArgs e) => HideMcpPermDialog();
    private void McpPermClose_Click(object sender, RoutedEventArgs e) => HideMcpPermDialog();
    private void McpPermScrim_Tapped(object sender, TappedRoutedEventArgs e)
    { if (ReferenceEquals(e.OriginalSource, McpPermScrim)) HideMcpPermDialog(); }

    private void HideMcpPermDialog()
    {
        if (_animMcpPerm) return; // M2 hide re-entry guard
        _animMcpPerm = true;
        HideOverlay(McpPermOverlay, McpPermDialog, McpPermDialogTransform, () => _animMcpPerm = false);
    }

    private void McpPermRowButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string path) OpenMcpPermEditor(path);
    }

    /// <summary>App-level style lookup: TryGetValue does not descend into MergedDictionaries in all
    /// WinUI builds, so the merges are walked explicitly. Never throws (returns null on a miss) -
    /// a page-level Resources[key] index did exactly that (crash log 2026-08-29 20:54).</summary>
    private static Style? FindAppStyle(string key)
    {
        try
        {
            if (Application.Current.Resources.TryGetValue(key, out var v) && v is Style s) return s;
            foreach (var d in Application.Current.Resources.MergedDictionaries)
                if (d.TryGetValue(key, out v) && v is Style s2) return s2;
        }
        catch { }
        return null;
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

    private void ShowMcpAuditDialog()
    {
        _animMcpAudit = false; // N3-16: show re-arms the hide guard (M2/N2-11 parity)
        ShowOverlay(McpAuditOverlay, McpAuditDialog, McpAuditDialogTransform);
        FitDialogScroll(McpAuditScroll, 148); 
    }
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

    
    private void ShowMcpAuditClearConfirmDialog()
    {
        _animMcpAuditClear = false; // N3-16: show re-arms the hide guard (M2/N2-11 parity)
        ShowOverlay(McpAuditClearConfirmOverlay, McpAuditClearConfirmDialog, McpAuditClearConfirmDialogTransform);
    }
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

    

    private void ShowMcpConfigDialog()
    {
        _animMcpConfig = false; // N3-16: show re-arms the hide guard (M2/N2-11 parity)
        ShowOverlay(McpConfigOverlay, McpConfigDialog, McpConfigDialogTransform);
    }
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

    
    private void ShowMcpResetConfirmDialog()
    {
        _animMcpResetConfirm = false; // N3-16: show re-arms the hide guard (M2/N2-11 parity)
        ShowOverlay(McpResetConfirmOverlay, McpResetConfirmDialog, McpResetConfirmDialogTransform);
    }
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
    private void ShowMcpCopyDialog()
    {
        _animMcpCopy = false; // N3-16: show re-arms the hide guard (M2/N2-11 parity)
        ShowOverlay(McpCopyOverlay, McpCopyDialog, McpCopyDialogTransform);
    }
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

    private void ShowMcpDeleteConfirmDialog()
    {
        _animMcpDeleteConfirm = false; // N3-16: show re-arms the hide guard (M2/N2-11 parity)
        ShowOverlay(McpDeleteConfirmOverlay, McpDeleteConfirmDialog, McpDeleteConfirmDialogTransform);
    }
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
