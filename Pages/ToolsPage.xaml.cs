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
using System.Security.Cryptography;
using Novara.Models;

namespace Novara.Pages;






public sealed partial class ToolsPage : Page
{

    private readonly System.Collections.Generic.Dictionary<Grid, int> _overlayAnimGens = new();
    private readonly System.Collections.Generic.Dictionary<Grid, Border> _overlayDialogs = new();
    private bool _restarting;





    private readonly DialogUi.DialogScrollFit _dialogScrollFit = new();

    private void FitDialogScroll(ScrollViewer scroll, double fallbackChrome)
        => _dialogScrollFit.Fit(scroll, fallbackChrome, ToolsRoot.ActualHeight);


    private void ShowOverlay(Grid overlay, Border dialog, CompositeTransform transform)
        => DialogUi.ShowOverlay(ToolsRoot, _overlayAnimGens, _overlayDialogs, overlay, dialog, transform);







    private void RegisterDialogClamp(Grid overlay, Border dialog)
        => DialogUi.RegisterDialogClamp(ToolsRoot, _overlayDialogs, overlay, dialog);
    private void ReclampVisibleDialogs()
        => DialogUi.ReclampVisibleDialogs(ToolsRoot, _overlayDialogs, _dialogScrollFit);
    private void HideOverlay(Grid overlay, Border dialog, CompositeTransform transform, Action onCompleted)
        => DialogUi.HideOverlay(ToolsRoot, _overlayAnimGens, _overlayDialogs, overlay, dialog, transform, onCompleted);


    private bool _statsEnabled;
    private bool _statsExpanded;
    private bool _animPageWipe;
    private bool _animPageWipeFinal;
    private bool _animPageGroupLoss;
    private bool _animNetActivity;
    private bool _animWorkspaceManage;
    private bool _animWorkspaceEdit;
    private bool _animWorkspaceDelete;
    private Services.IconRing? _workspaceIconRing;
    private string _workspaceSelectedIcon = "";
    private string? _editingWorkspaceId;
    private string? _pendingDeleteWorkspaceId;
    private bool _animExportOptions;
    private bool _animExportMdNotice;
    private bool _animCsvPreview;
    private bool _animCsvExportNotice;
    private bool _backupEnabled;
    private bool _autoBackupEnabled;
    private int _backupFreqIndex;
    private bool _animBackupIntro, _animBackupNow, _animBackupAuto, _animBackupRestore, _animBackupDelete;
    private bool _animBackupRestoreConfirm, _animBackupDeleteConfirm;
    private bool _animMcpConfig, _animMcpResetConfirm, _animMcpCopy, _animMcpDeleteConfirm;
    private bool _animMcpRevokeConfirm;
    private bool _animMcpAudit, _animMcpAuditClear;
    private bool _animMcpPerm;
    private string? _pendingRevokePath;
    private bool _pendingExport;
    private bool _pendingReloadAfterImport;

    private bool _animEncExport;
    private bool _animEncImportPwd;
    private bool _animConnectPwd;
    private bool _animConnectIntro;
    private string? _pendingEncImportPath;
    private bool _pendingDeleteIsClearAll;
    private string? _pendingRestoreSnapshot;
    private readonly List<string> _pendingDeleteSnapshots = new();

    private bool PrivacyLockEnabledNow => App.Store?.Database.AppSettings.PrivacyLockEnabled == true;
    private bool _animResetConfirm;
    private bool _animResetPwd;
    private bool _animImportPwd;
    private bool _animImportConfirm;
    private bool _animImportResult;
    private Action? _pendingVerifiedAction;

    private readonly Dictionary<TextBox, System.Threading.CancellationTokenSource> _flashCtsMap = new();
    private readonly Dictionary<TextBox, Microsoft.UI.Xaml.Media.Brush> _flashOriginalBgs = new();

    public ToolsPage()
    {
        InitializeComponent();
        ToolsTitleText.Text = App.GetString("Tools_Title_Page");
        FooterBrandText.Text = "Novara";
        FooterVersionText.Text = "v" + (GetType().Assembly.GetName().Version?.ToString(3) ?? "0.0.0");
        Novara.Services.DialogDepth.AttachContainer((Grid)Content, autoVeil: true);
        AssignToolCardTexts();


        foreach (var dangerIcon in new Microsoft.UI.Xaml.Controls.PathIcon[]
        {
            ResetConfirmDangerIcon, BackupDeleteDangerIcon, BackupRestoreDangerIcon,
            PageWipeFinalDangerIcon, PageGroupLossDangerIcon, McpDeleteConfirmDangerIcon,
            WorkspaceDeleteDangerIcon, ImportConfirmDangerIcon, McpRevokeConfirmDangerIcon,
            McpResetConfirmDangerIcon, McpAuditClearDangerIcon,
        })
            dangerIcon.Data = App.CreateGeometry(IconData.Danger);
        KeyDown += Page_KeyDown;
        ToolsRoot.SizeChanged += (_, _) => ReclampVisibleDialogs();
        WorkspaceNameTextBox.TextChanged += (_, _) => UpdateWorkspaceEditConfirmState();
        BackPathIcon.Data = (Geometry)Microsoft.UI.Xaml.Markup.XamlBindingHelper.ConvertValue(typeof(Geometry), IconData.Back);

        Loaded += (_, _) =>
        {

            if (App.IsAnimationsEnabled) ToolsFloatIn.Begin();
            else App.ShowCardsStatically(StatsCard, SyncCard, McpCard, WorkspaceCard, ConnectCard, NetActivityCard, Card4, Card7, Card10);




            if (_pendingMcpPermPath != null)
            {
                var pending = _pendingMcpPermPath;
                _pendingMcpPermPath = null;
                OpenMcpPermEditor(pending);
            }

            if (_pendingWorkspaceManage)
            {
                _pendingWorkspaceManage = false;
                OpenWorkspaceManagement();
            }



            if (App.Store?.IsSaveSuppressed == true && BackupRestartOverlay.Visibility != Visibility.Visible)
                ShowBackupRestartDialog();

            var settings = App.Store?.Database.AppSettings;
            if (settings != null)
            {
                _backupEnabled = settings.BackupEnabled;
                _autoBackupEnabled = settings.AutoBackupEnabled;
                _backupFreqIndex = settings.BackupFreq;
                _statsEnabled = settings.StatsEnabled;
            }
            InitBackupFreqCombo();
            UpdateBackupUI();
            _statsExpanded = true;
            UpdateStatsUI();
            StatsTitleText.Text = App.GetString("Setting_Stats_Title");
            UpdateMcpUI();
            Services.SyncService.RoundCompleted += OnSyncRoundCompleted;
            UpdateSyncUI();
        };

        Unloaded += (_, _) =>
        {
            DialogUi.CancelAllFlashes(_flashCtsMap);
            Services.SyncService.RoundCompleted -= OnSyncRoundCompleted;
            BackupAutoOverlay.Visibility = Visibility.Collapsed;
            BackupDeleteOverlay.Visibility = Visibility.Collapsed;
            BackupDeleteConfirmOverlay.Visibility = Visibility.Collapsed;
            BackupIntroOverlay.Visibility = Visibility.Collapsed;
            BackupNowOverlay.Visibility = Visibility.Collapsed;
            BackupRestartOverlay.Visibility = Visibility.Collapsed;
            BackupRestoreOverlay.Visibility = Visibility.Collapsed;
            BackupRestoreConfirmOverlay.Visibility = Visibility.Collapsed;
            ConnectIntroOverlay.Visibility = Visibility.Collapsed;
            ConnectPwdOverlay.Visibility = Visibility.Collapsed;
            CsvExportNoticeOverlay.Visibility = Visibility.Collapsed;
            CsvImportPreviewOverlay.Visibility = Visibility.Collapsed;
            EncExportOverlay.Visibility = Visibility.Collapsed;
            EncImportPwdOverlay.Visibility = Visibility.Collapsed;
            ExportMdNoticeOverlay.Visibility = Visibility.Collapsed;
            ExportOptionsOverlay.Visibility = Visibility.Collapsed;
            ExportPlainWarnOverlay.Visibility = Visibility.Collapsed;
            ImportConfirmOverlay.Visibility = Visibility.Collapsed;
            ImportExportPasswordOverlay.Visibility = Visibility.Collapsed;
            ImportResultOverlay.Visibility = Visibility.Collapsed;
            McpAuditOverlay.Visibility = Visibility.Collapsed;
            McpAuditClearConfirmOverlay.Visibility = Visibility.Collapsed;
            McpConfigOverlay.Visibility = Visibility.Collapsed;
            McpCopyOverlay.Visibility = Visibility.Collapsed;
            McpDeleteConfirmOverlay.Visibility = Visibility.Collapsed;
            McpPermOverlay.Visibility = Visibility.Collapsed;
            McpResetConfirmOverlay.Visibility = Visibility.Collapsed;
            McpRevokeConfirmOverlay.Visibility = Visibility.Collapsed;
            NetActivityOverlay.Visibility = Visibility.Collapsed;
            PageGroupLossOverlay.Visibility = Visibility.Collapsed;
            PageWipeOverlay.Visibility = Visibility.Collapsed;
            PageWipeFinalOverlay.Visibility = Visibility.Collapsed;



            PageWipeFinalCd.Reset();
            ResetConfirmCd.Reset();
            ResetConfirmOverlay.Visibility = Visibility.Collapsed;
            ResetPasswordOverlay.Visibility = Visibility.Collapsed;
            SyncAdoptOverlay.Visibility = Visibility.Collapsed;
            SyncAuditOverlay.Visibility = Visibility.Collapsed;
            SyncAuditClearConfirmOverlay.Visibility = Visibility.Collapsed;
            SyncConflictOverlay.Visibility = Visibility.Collapsed;
            SyncConnInfoOverlay.Visibility = Visibility.Collapsed;
            SyncDeviceCenterOverlay.Visibility = Visibility.Collapsed;
            SyncDeviceResetConfirmOverlay.Visibility = Visibility.Collapsed;
            SyncDeviceRevokeConfirmOverlay.Visibility = Visibility.Collapsed;
            SyncEditAccessOverlay.Visibility = Visibility.Collapsed;
            SyncEditAccessRevokeConfirmOverlay.Visibility = Visibility.Collapsed;
            SyncKeyImportPwdOverlay.Visibility = Visibility.Collapsed;
            SyncKeyImportResultOverlay.Visibility = Visibility.Collapsed;
            SyncKeySavedOverlay.Visibility = Visibility.Collapsed;
            SyncKeyViewOverlay.Visibility = Visibility.Collapsed;
            SyncPairOverlay.Visibility = Visibility.Collapsed;
            SyncReadAccessOverlay.Visibility = Visibility.Collapsed;
            SyncReadAccessRevokeConfirmOverlay.Visibility = Visibility.Collapsed;
            SyncUnpairOverlay.Visibility = Visibility.Collapsed;
            WorkspaceDeleteOverlay.Visibility = Visibility.Collapsed;
            WorkspaceEditOverlay.Visibility = Visibility.Collapsed;
            WorkspaceManageOverlay.Visibility = Visibility.Collapsed;

            _animResetConfirm = _animResetPwd = _animImportPwd = _animImportConfirm = _animImportResult = false;
            _animBackupIntro = _animBackupNow = _animBackupAuto = _animBackupRestore = _animBackupDelete = _animBackupRestoreConfirm = _animBackupDeleteConfirm = false;
            _animExportMdNotice = _animExportOptions = false;
            _animMcpConfig = _animMcpResetConfirm = _animMcpCopy = _animMcpDeleteConfirm = _animMcpRevokeConfirm = false;
            _animMcpAudit = _animMcpAuditClear = false;
            _animMcpPerm = false;
            _animPageWipe = false; _animPageWipeFinal = false; _animPageGroupLoss = false;
            _animNetActivity = false;
            _animWorkspaceManage = _animWorkspaceEdit = _animWorkspaceDelete = false;
            _pendingExport = false;
            _animCsvPreview = false;
            _animCsvExportNotice = false;
            _readAccessToken = null;
            _pendingImportPath = null;
            _importedSpaceKey = null;
            _animSyncKeyImportPwd = _animSyncKeyImportResult = false;
            _editorAccessToken = null;
            _animSyncDeviceCenter = _animSyncDeviceRevokeConfirm = _animSyncDeviceResetConfirm = false;
            _deviceActionTarget = null;
            _syncConflict = null;
            _syncConflictBusy = false;




            _revealedSpaceKey = null;
            _syncKeyShown = false;




            _pendingVerifiedAction = null;
            _pendingPlainExportContinue = null;
            _pendingDeleteWorkspaceId = null;
            _editingWorkspaceId = null;
            _pendingRestoreSnapshot = null;
            _pendingDeleteSnapshots.Clear();
            _pendingRevokePath = null;
            _pendingEncImportPath = null;
            _pendingDeleteIsClearAll = false;
            _pendingReloadAfterImport = false;




            _animEncExport = _animEncImportPwd = false;
            _animConnectPwd = _animConnectIntro = false;
            _animExportPlainWarn = false;
            _animSyncPair = _animSyncUnpair = _animSyncConflict = _animSyncAdopt = false;
            _animSyncKeySaved = _animSyncKeyView = false;
            _animSyncAudit = _animSyncAuditClear = false;
            _animSyncReadAccess = _animSyncReadAccessRevoke = false;
            _animSyncEditAccess = _animSyncEditAccessRevoke = false;
            _animSyncConnInfo = false;




            _csvImportText = "";
            _csvImportResult = null;
            CsvImportPreviewList.Children.Clear();
        };
    }



    private void AssignToolCardTexts()
    {



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

        ResetDataButtonText.Text = App.GetString("Setting_DataWipe");
        ApplyToggleState(ResetDataButton, false);
        NetActivityTitleText.Text = App.GetString("NetActivity_Panel");
        ApplyToggleState(NetActivityPanelButton, false);
        NetActivityPanelButtonText.Text = App.GetString("NetActivity_Open");
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

    private void BackButton_Click(object sender, RoutedEventArgs e)
    {
        App.MainWindow?.NavigateBackFromSettings();
    }

    private void Page_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key != Windows.System.VirtualKey.Escape) return;
        if (SyncKeyImportResultOverlay.Visibility == Visibility.Visible) { HideSyncKeyImportResultDialog(); e.Handled = true; return; }
        if (SyncKeyImportPwdOverlay.Visibility == Visibility.Visible) { HideSyncKeyImportPwdDialog(); e.Handled = true; return; }
        if (SyncDeviceResetConfirmOverlay.Visibility == Visibility.Visible) { HideSyncDeviceResetConfirmDialog(); e.Handled = true; return; }
        if (SyncDeviceRevokeConfirmOverlay.Visibility == Visibility.Visible) { HideSyncDeviceRevokeConfirmDialog(); e.Handled = true; return; }
        if (SyncDeviceCenterOverlay.Visibility == Visibility.Visible) { HideSyncDeviceCenterDialog(); e.Handled = true; return; }
        if (SyncConnInfoOverlay.Visibility == Visibility.Visible) { HideSyncConnInfoDialog(); e.Handled = true; return; }
        if (SyncReadAccessRevokeConfirmOverlay.Visibility == Visibility.Visible) { HideSyncReadAccessRevokeConfirmDialog(); e.Handled = true; return; }
        if (SyncReadAccessOverlay.Visibility == Visibility.Visible) { HideSyncReadAccessDialog(); e.Handled = true; return; }
        if (SyncEditAccessRevokeConfirmOverlay.Visibility == Visibility.Visible) { HideSyncEditAccessRevokeConfirmDialog(); e.Handled = true; return; }
        if (SyncEditAccessOverlay.Visibility == Visibility.Visible) { HideSyncEditAccessDialog(); e.Handled = true; return; }
        if (SyncAuditClearConfirmOverlay.Visibility == Visibility.Visible) { HideSyncAuditClearConfirmDialog(); e.Handled = true; return; }
        if (SyncAuditOverlay.Visibility == Visibility.Visible) { HideSyncAuditDialog(); e.Handled = true; return; }
        if (SyncKeySavedOverlay.Visibility == Visibility.Visible) { HideSyncKeySavedDialog(); e.Handled = true; return; }
        if (SyncAdoptOverlay.Visibility == Visibility.Visible) { HideSyncAdoptDialog(); e.Handled = true; return; }
        if (SyncConflictOverlay.Visibility == Visibility.Visible) { HideSyncConflictDialog(); e.Handled = true; return; }
        if (SyncKeyViewOverlay.Visibility == Visibility.Visible) { HideSyncKeyViewDialog(); e.Handled = true; return; }
        if (SyncPairOverlay.Visibility == Visibility.Visible) { HideSyncPairDialog(); e.Handled = true; return; }
        if (SyncUnpairOverlay.Visibility == Visibility.Visible) { HideSyncUnpairDialog(); e.Handled = true; return; }
        if (ImportResultOverlay.Visibility == Visibility.Visible)
        {
            HideImportResult();
            if (_pendingReloadAfterImport) { _pendingReloadAfterImport = false; App.MainWindow?.ReloadPages(); }
            e.Handled = true;
            return;
        }
        if (ResetPasswordOverlay.Visibility == Visibility.Visible) { HideResetPasswordDialog(); e.Handled = true; return; }
        if (ExportPlainWarnOverlay.Visibility == Visibility.Visible) { _pendingPlainExportContinue = null; HidePlainExportWarnDialog(); e.Handled = true; return; }
        if (ExportOptionsOverlay.Visibility == Visibility.Visible) { HideExportOptionsDialog(); e.Handled = true; return; }
        if (ImportExportPasswordOverlay.Visibility == Visibility.Visible) { HideImportExportPasswordDialog(); e.Handled = true; return; }
        if (ResetConfirmOverlay.Visibility == Visibility.Visible) { HideResetConfirmDialog(); e.Handled = true; return; }
        if (ImportConfirmOverlay.Visibility == Visibility.Visible) { HideImportConfirmDialog(); e.Handled = true; return; }
        if (BackupIntroOverlay.Visibility == Visibility.Visible) { HideBackupIntroDialog(); e.Handled = true; return; }
        if (BackupNowOverlay.Visibility == Visibility.Visible) { HideBackupNowDialog(); e.Handled = true; return; }
        if (BackupAutoOverlay.Visibility == Visibility.Visible) { RevertBackupAutoDraft(); HideBackupAutoDialog(); UpdateBackupUI(); e.Handled = true; return; }
        if (BackupRestoreOverlay.Visibility == Visibility.Visible) { HideBackupRestoreDialog(); e.Handled = true; return; }
        if (BackupDeleteOverlay.Visibility == Visibility.Visible) { HideBackupDeleteDialog(); e.Handled = true; return; }
        if (BackupRestoreConfirmOverlay.Visibility == Visibility.Visible) { CancelBackupRestoreConfirm(); e.Handled = true; return; }
        if (BackupDeleteConfirmOverlay.Visibility == Visibility.Visible) { HideBackupDeleteConfirmDialog(); e.Handled = true; return; }
        if (BackupRestartOverlay.Visibility == Visibility.Visible) { e.Handled = true; RestartApp(rollbackOnFail: true); return; }
        if (ExportMdNoticeOverlay.Visibility == Visibility.Visible) { HideExportMdNoticeDialog(); e.Handled = true; return; }
        if (McpPermOverlay.Visibility == Visibility.Visible) { HideMcpPermDialog(); e.Handled = true; return; }
        if (McpAuditClearConfirmOverlay.Visibility == Visibility.Visible) { HideMcpAuditClearConfirmDialog(); e.Handled = true; return; }
        if (McpAuditOverlay.Visibility == Visibility.Visible) { HideMcpAuditDialog(); e.Handled = true; return; }
        if (McpCopyOverlay.Visibility == Visibility.Visible) { HideMcpCopyDialog(); e.Handled = true; return; }
        if (McpDeleteConfirmOverlay.Visibility == Visibility.Visible) { HideMcpDeleteConfirmDialog(); e.Handled = true; return; }
        if (McpRevokeConfirmOverlay.Visibility == Visibility.Visible) { HideMcpRevokeConfirmDialog(); e.Handled = true; return; }
        if (McpResetConfirmOverlay.Visibility == Visibility.Visible) { HideMcpResetConfirmDialog(); e.Handled = true; return; }
        if (McpConfigOverlay.Visibility == Visibility.Visible) { HideMcpConfigDialog(); e.Handled = true; return; }
        if (CsvImportPreviewOverlay.Visibility == Visibility.Visible) { HideCsvImportPreview(); e.Handled = true; return; }
        if (CsvExportNoticeOverlay.Visibility == Visibility.Visible) { HideCsvExportNoticeDialog(); e.Handled = true; return; }
        if (WorkspaceDeleteOverlay.Visibility == Visibility.Visible) { HideWorkspaceDelete(); e.Handled = true; return; }
        if (WorkspaceEditOverlay.Visibility == Visibility.Visible) { HideWorkspaceEdit(); e.Handled = true; return; }
        if (WorkspaceManageOverlay.Visibility == Visibility.Visible) { HideWorkspaceManage(); e.Handled = true; return; }
        if (NetActivityOverlay.Visibility == Visibility.Visible) { HideNetActivityPanel(); e.Handled = true; return; }
        if (PageGroupLossOverlay.Visibility == Visibility.Visible) { HidePageGroupLossDialog(); e.Handled = true; return; }
        if (PageWipeFinalOverlay.Visibility == Visibility.Visible) { HidePageWipeFinalDialog(); e.Handled = true; return; }
        if (PageWipeOverlay.Visibility == Visibility.Visible) { HidePageWipeDialog(); e.Handled = true; return; }
        if (ConnectPwdOverlay.Visibility == Visibility.Visible) { HideConnectPwdDialog(); e.Handled = true; return; }
        if (ConnectIntroOverlay.Visibility == Visibility.Visible) { HideConnectIntroDialog(); e.Handled = true; return; }
    }


    private void NetActivityPanelButton_Click(object sender, RoutedEventArgs e)
    {



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
                Foreground = it.Success ? App.GetBrush("AppTextTertiaryBrush") : App.GetBrush("AppDangerTextBrush"),
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



    private bool _pendingWorkspaceManage;

    public void OpenWorkspaceManagement()
    {




        if (!IsLoaded) { _pendingWorkspaceManage = true; return; }
        _animWorkspaceManage = false;
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
            delBtn.Click += (_, _) => { _pendingDeleteWorkspaceId = wid; _animWorkspaceDelete = false; ShowOverlay(WorkspaceDeleteOverlay, WorkspaceDeleteDialog, WorkspaceDeleteDialogTransform); };

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
        _animWorkspaceEdit = false;
        _editingWorkspaceId = id;
        BuildWorkspaceIconSelector();
        var ws = App.Store?.Database.AppSettings.Workspaces;
        var w = id == null ? null : ws?.FirstOrDefault(x => x.Id == id);
        WorkspaceEditTitle.Text = App.GetString(id == null ? "Workspace_New" : "Workspace_Edit");
        WorkspaceNameTextBox.Text = w?.Name ?? "";
        _workspaceSelectedIcon = w?.IconKey ?? "";
        HighlightWorkspaceIcon();
        UpdateWorkspaceEditConfirmState();
        ShowOverlay(WorkspaceEditOverlay, WorkspaceEditDialog, WorkspaceEditDialogTransform);
    }


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


    private void ResetDataButton_Click(object sender, RoutedEventArgs e)
    {
        var menu = new MenuFlyout { MenuFlyoutPresenterStyle = (Style)Application.Current.Resources["GlassMenuFlyoutPresenterStyle"] };
        var dangerBrush = App.GetBrush("AppDangerTextBrush");
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

        _resetConfirmBusy = false;
        ResetConfirmCd.Completed -= OnResetConfirmCdCompleted;
        ResetConfirmCd.Completed += OnResetConfirmCdCompleted;
        ResetConfirmButton.IsEnabled = false;
        ResetConfirmButton.Opacity = Motion.CooldownDisabledOpacity;
        ShowResetConfirmDialog();
        ResetConfirmCd.Start(Motion.CooldownSeconds);
    }

    private void OnResetConfirmCdCompleted()
    {
        ResetConfirmCd.Completed -= OnResetConfirmCdCompleted;
        ResetConfirmButton.IsEnabled = true;
        ResetConfirmButton.Opacity = 1;
    }

    private int _pageWipePage = 0;

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
        _animPageWipe = false;
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
            ShowPageGroupLossDialog();
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
        PageWipeFinalOkButton.Opacity = Motion.CooldownDisabledOpacity;
        _animPageWipeFinal = false;
        ShowOverlay(PageWipeFinalOverlay, PageWipeFinalDialog, PageWipeFinalDialogTransform);
        PageWipeFinalCd.Start(Motion.CooldownSeconds);
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
        _animPageGroupLoss = false;
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



    private void PerformPageWipe(int page, bool soft)
    {
        var store = App.Store; if (store == null) return;
        var db = store.Database;
        var now = DateTime.Now;
        if (page == 0)
        {
            foreach (var x in db.MemoEntries.Where(x => !x.IsDeleted)) { if (soft) { x.IsDeleted = true; x.DeletedAt = now; x.IsPinned = false; x.IsStarred = false; } }

            db.MemoGroups.Clear();
            if (!soft) db.MemoEntries.RemoveAll(x => true);
        }
        else if (page == 1)
        {
            foreach (var x in db.PathBackupItems.Where(x => !x.IsDeleted)) { if (soft) { x.IsDeleted = true; x.DeletedAt = now; x.IsPinned = false; x.IsStarred = false; } }
            if (!soft) db.PathBackupItems.RemoveAll(x => true);
        }
        else if (page == 2)
        {
            foreach (var t in db.TodoCards.Where(x => !x.IsDeleted).ToList())
            {
                if (soft) { t.IsDeleted = true; t.DeletedAt = now; t.IsPinned = false; t.IsStarred = false; }
                Services.ReminderScheduler.Teardown(t.Id, t.ReminderAt, t);
                if (Services.StickySync.Contains(t.Id.ToString())) Services.StickySync.RemoveNote(t.Id.ToString());
            }
            foreach (var n in db.NoteCards.Where(x => !x.IsDeleted).ToList())
            {
                if (soft) { n.IsDeleted = true; n.DeletedAt = now; n.IsPinned = false; n.IsStarred = false; }
                Services.ReminderScheduler.Teardown(n.Id, n.ReminderAt, n);
                if (Services.StickySync.Contains(n.Id.ToString())) Services.StickySync.RemoveNote(n.Id.ToString());
            }
            if (!soft) { db.TodoCards.RemoveAll(x => true); db.NoteCards.RemoveAll(x => true); }
        }
        else
        {
            foreach (var x in db.DiaryItems.Where(x => !x.IsDeleted)) { if (soft) { x.IsDeleted = true; x.DeletedAt = now; x.IsPinned = false; x.PinnedAt = null; x.IsStarred = false; } }
            if (!soft) db.DiaryItems.RemoveAll(x => true);
        }
        _ = store.SaveAsync();
        App.MainWindow?.ReloadPages();
        App.ShowToast(App.GetString(soft ? "Common_Toast_Deleted" : "DataWipe_Done_Hard"));
    }

    private void ShowResetConfirmDialog()
    {
        _animResetConfirm = false;
        ResetConfirmDialogTransform.ScaleX = 0.94; ResetConfirmDialogTransform.ScaleY = 0.94; ResetConfirmDialogTransform.TranslateY = 24;
        ResetConfirmDialog.Opacity = 0;
        ResetConfirmScrim.Opacity = 0;
        RegisterDialogClamp(ResetConfirmOverlay, ResetConfirmDialog);
        ResetConfirmOverlay.Visibility = Visibility.Visible;
        var sb = new Storyboard();
        var si = new DoubleAnimation { To = 1, Duration = TimeSpan.FromMilliseconds(250) };
        Storyboard.SetTarget(si, ResetConfirmScrim); Storyboard.SetTargetProperty(si, "Opacity"); sb.Children.Add(si);
        var di = new DoubleAnimation { To = 1, Duration = TimeSpan.FromMilliseconds(300) };
        Storyboard.SetTarget(di, ResetConfirmDialog); Storyboard.SetTargetProperty(di, "Opacity"); sb.Children.Add(di);
        Motion.AddDialogShowTransform(sb, ResetConfirmDialogTransform);
        sb.Begin();
    }


    private void ExportViewerButton_Click(object sender, RoutedEventArgs e)
    {

        UpdateConnectConfirmState();
        _animConnectIntro = false;
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
        if (_animConnectIntro) return;
        _animConnectIntro = true;
        HideOverlay(ConnectIntroOverlay, ConnectIntroDialog, ConnectIntroDialogTransform, () => { _animConnectIntro = false; after?.Invoke(); });
    }




    private const string SnapshotGuideUrl = "https://novara.xin/help/snapshot.html";





    private const string SyncGuideUrl = "https://novara.xin/help/deploy.html";


    private const string EasterEggUrl = "https://novara.xin/easteregg.html";


    private void Footer_Tapped(object sender, Microsoft.UI.Xaml.Input.TappedRoutedEventArgs e)
    {
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(EasterEggUrl) { UseShellExecute = true }); }
        catch { }
    }

    private void DeployHelp_Click(object sender, RoutedEventArgs e)
    {

        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(SnapshotGuideUrl) { UseShellExecute = true }); }
        catch { App.ShowToast(App.GetString("Connect_DeployHelp_Fail"), ToastTone.Error); }
    }

    private void ConnectConfirm_Click(object sender, RoutedEventArgs e)
    {




        HideConnectIntroDialog(() => ShowConnectPwdDialog());
    }


    private void ShowConnectPwdDialog()
    {
        ConnectPwdUseLock.IsChecked = false;



        ConnectPwdUseLock.Visibility = PrivacyLockEnabledNow ? Visibility.Visible : Visibility.Collapsed;
        ConnectPwdPasswordBox.Text = "";
        ConnectPwdConfirmBox.Text = "";
        ConnectPwdStrengthText.Visibility = Visibility.Collapsed;
        ConnectPwdConfirmButton.IsEnabled = false;
        _animConnectPwd = false;
        ShowOverlay(ConnectPwdOverlay, ConnectPwdDialog, ConnectPwdDialogTransform);
    }

    private void HideConnectPwdDialog()
    {
        if (_animConnectPwd) return;
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
                    ConnectPwdStrengthText.Foreground = App.GetBrush("AppDangerTextBrush");
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

        if (_pickerFlowBusy) return;
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

            var path = FilePicker.PickSaveFile("index", new[] { ("Novara Snapshot", new[] { ".html" }) });
            if (path == null) return;

            var exportedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            var version = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "10.2.0";
            var html = template
                .Replace("__CIPHER_BASE64__", cipher)
                .Replace("__EXPORTED_AT__", exportedAt)
                .Replace("__VERSION__", version)
                .Replace("__LANGUAGE__", App.CurrentLanguage);

            await File.WriteAllTextAsync(path, html, new System.Text.UTF8Encoding(false));
            ShowImportResult(App.GetString("Setting_Export_Success"), App.GetString("Connect_Export_SuccessDesc"));
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"导出离线查看器失败: {ex}");
            ShowImportResult(App.GetString("Setting_Export_Fail"), App.GetString("Setting_Export_FailDesc"));
        }
    }


    private const string ViewerCssLink = "<link rel=\"stylesheet\" href=\"viewer.css\">";
    private const string ViewerScriptTag = "<script src=\"viewer.js\"></script>";









    private string? LoadSnapshotTemplate()
    {
        try
        {

            var candidates = new[]
            {
                (Template: Path.Combine(AppContext.BaseDirectory, "snapshot-viewer.html"),
                 Css: Path.Combine(AppContext.BaseDirectory, "viewer.css"),
                 Script: Path.Combine(AppContext.BaseDirectory, "viewer.js")),
                (Template: Path.Combine(AppContext.BaseDirectory, "SnapshotViewer", "index.html"),
                 Css: Path.Combine(AppContext.BaseDirectory, "SnapshotViewer", "viewer.css"),
                 Script: Path.Combine(AppContext.BaseDirectory, "SnapshotViewer", "viewer.js")),
            };

            foreach (var candidate in candidates)
            {
                if (!File.Exists(candidate.Template)) continue;

                var html = InlineAsset(File.ReadAllText(candidate.Template), ViewerCssLink, candidate.Css, "<style>", "</style>");
                if (html is null) return null;

                return InlineAsset(html, ViewerScriptTag, candidate.Script, "<script>", "</script>");
            }

            return null;
        }
        catch { return null; }
    }






    private static string? InlineAsset(string template, string marker, string assetPath, string openTag, string closeTag)
    {
        var at = template.IndexOf(marker, StringComparison.Ordinal);
        if (at < 0) return template;
        if (!File.Exists(assetPath)) return null;

        return template.Substring(0, at)
            + openTag + "\n" + File.ReadAllText(assetPath) + "\n  " + closeTag
            + template.Substring(at + marker.Length);
    }

    private void HideResetConfirmDialog()
    {
        if (_animResetConfirm) return;
        _animResetConfirm = true;
        ResetConfirmCd.Reset();
        ResetConfirmButton.IsEnabled = true;
        ResetConfirmButton.Opacity = 1;
        var sb = new Storyboard();
        var so = new DoubleAnimation { To = 0, Duration = TimeSpan.FromMilliseconds(200) };
        Storyboard.SetTarget(so, ResetConfirmScrim); Storyboard.SetTargetProperty(so, "Opacity"); sb.Children.Add(so);
        var d = new DoubleAnimation { To = 0, Duration = TimeSpan.FromMilliseconds(200) };
        Storyboard.SetTarget(d, ResetConfirmDialog); Storyboard.SetTargetProperty(d, "Opacity"); sb.Children.Add(d);
        Motion.AddDialogHideTransform(sb, ResetConfirmDialogTransform);
        sb.Completed += (_, _) => { ResetConfirmOverlay.Visibility = Visibility.Collapsed; _animResetConfirm = false; };
        sb.Begin();
    }

    private void ResetConfirmClose_Click(object sender, RoutedEventArgs e) => HideResetConfirmDialog();
    private void ResetCancel_Click(object sender, RoutedEventArgs e) => HideResetConfirmDialog();

    private void ResetConfirmScrim_Tapped(object sender, TappedRoutedEventArgs e)
    {
        if (ReferenceEquals(e.OriginalSource, ResetConfirmScrim)) HideResetConfirmDialog();
    }

    private bool _resetConfirmBusy;
    private bool _pickerFlowBusy;

    private void ResetConfirmButton_Click(object sender, RoutedEventArgs e)
    {
        if (_resetConfirmBusy) return;
        _resetConfirmBusy = true;

        HideResetConfirmDialog();




        PerformReset();
    }

    private void PerformReset()
    {



        var remindersBefore = Services.ReminderScheduler.Snapshot(App.Store?.Database);
        var resetResult = App.Store?.ResetDatabase();
        if (resetResult == null || resetResult.Status != LoadStatus.EmptyCreated)
        {


            ShowImportResult(App.GetString("Setting_Reset_FailTitle"), App.GetString("Setting_Reset_FailDesc"));
            return;
        }
        PasswordService.Delete();
        PasswordService.DeleteLockout();
        WindowsHelloService.Disable();
        Services.StickySync.ClearAllNotes();

        if (remindersBefore is not null)
            Services.ReminderScheduler.Reconcile(remindersBefore, new Dictionary<Guid, DateTime>());





        Services.SyncService.MarkRestorePendingConfirm();
        App.ShowToast(App.GetString("Common_Toast_Reset"));
        App.MainWindow?.ReloadPages();
    }






private void ShowResetPasswordDialog()
    {
        ResetPasswordBox.Text = "";
        ResetPasswordDialogTransform.ScaleX = 0.94; ResetPasswordDialogTransform.ScaleY = 0.94; ResetPasswordDialogTransform.TranslateY = 24;
        ResetPasswordDialog.Opacity = 0;
        ResetPasswordScrim.Opacity = 0;
        RegisterDialogClamp(ResetPasswordOverlay, ResetPasswordDialog);
        ResetPasswordOverlay.Visibility = Visibility.Visible;
        _animResetPwd = false;
        var sb = new Storyboard();
        var si = new DoubleAnimation { To = 1, Duration = TimeSpan.FromMilliseconds(250) };
        Storyboard.SetTarget(si, ResetPasswordScrim); Storyboard.SetTargetProperty(si, "Opacity"); sb.Children.Add(si);
        var di = new DoubleAnimation { To = 1, Duration = TimeSpan.FromMilliseconds(300) };
        Storyboard.SetTarget(di, ResetPasswordDialog); Storyboard.SetTargetProperty(di, "Opacity"); sb.Children.Add(di);
        Motion.AddDialogShowTransform(sb, ResetPasswordDialogTransform);
        sb.Begin();
    }

    private void HideResetPasswordDialog()
    {
        if (_animResetPwd) return;
        _animResetPwd = true;
        var sb = new Storyboard();
        var so = new DoubleAnimation { To = 0, Duration = TimeSpan.FromMilliseconds(200) };
        Storyboard.SetTarget(so, ResetPasswordScrim); Storyboard.SetTargetProperty(so, "Opacity"); sb.Children.Add(so);
        var d = new DoubleAnimation { To = 0, Duration = TimeSpan.FromMilliseconds(200) };
        Storyboard.SetTarget(d, ResetPasswordDialog); Storyboard.SetTargetProperty(d, "Opacity"); sb.Children.Add(d);
        Motion.AddDialogHideTransform(sb, ResetPasswordDialogTransform);
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
        csvImportItem.Click += (_, _) => RunPrivacyGated(PickAndShowCsvImport);
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
        var encExportItem = MakeItem(App.GetString("Setting_Archive_ExportEncrypted"));
        var csvExportItem = MakeItem(App.GetString("Setting_CsvExport"));
        var htmlExportItem = MakeItem(App.GetString("Setting_ExportHtml"));
        var pdfExportItem = MakeItem(App.GetString("Setting_ExportPdf"));
        var imageExportItem = MakeItem(App.GetString("Setting_ExportImage"));
        mdItem.Click += (_, _) => RunPrivacyGated(() => ShowPlainExportWarn(ShowExportMdNoticeDialog));
        nativeExportItem.Click += (_, _) => ExportNativeFlow();
        encExportItem.Click += (_, _) => RunPrivacyGated(ShowEncExportDialog);
        csvExportItem.Click += (_, _) => RunPrivacyGated(() => ShowPlainExportWarn(ShowCsvExportNoticeDialog));
        htmlExportItem.Click += (_, _) => RunPrivacyGated(() => ShowPlainExportWarn(() => _ = ExportHtmlFlowAsync()));
        pdfExportItem.Click += (_, _) => RunPrivacyGated(() => ShowPlainExportWarn(() => _ = ExportPdfFlowAsync()));
        imageExportItem.Click += (_, _) => RunPrivacyGated(() => ShowPlainExportWarn(() => _ = ExportImageCollectionFlowAsync()));
        exportSub.Items.Add(mdItem);
        exportSub.Items.Add(nativeExportItem);
        exportSub.Items.Add(encExportItem);
        exportSub.Items.Add(csvExportItem);
        exportSub.Items.Add(htmlExportItem);
        exportSub.Items.Add(pdfExportItem);
        exportSub.Items.Add(imageExportItem);

        menu.Items.Add(importSub);
        menu.Items.Add(exportSub);
        menu.ShowAt(ImportExportButton, new Windows.Foundation.Point(0, ImportExportButton.ActualHeight + 4));
    }

    private bool _animExportPlainWarn;
    private Action? _pendingPlainExportContinue;






    private void ShowPlainExportWarn(Action continueAction)
    {
        _animExportPlainWarn = false;
        _pendingPlainExportContinue = continueAction;
        ExportPlainWarnIcon.Data = App.CreateGeometry(IconData.Danger);
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
            ShowPlainExportWarn(ShowExportOptionsDialog);
        }
    }

    private async void ImportConfirmContinue_Click(object sender, RoutedEventArgs e)
    {
        if (_animImportConfirm) return;
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

    private System.Threading.Tasks.Task<string?> PickImportFileAsync()
    {
        return System.Threading.Tasks.Task.FromResult(FilePicker.PickFile("*.novabak;*.novaenc"));
    }

    private async System.Threading.Tasks.Task ImportAfterPasswordAsync(string? prePickedPath)
    {
        try
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
        try
        {
            var path = FilePicker.PickSaveFile(string.Format("{0}_{1:yyyyMMdd_HHmmss}", App.GetString("Setting_Backup_FilePrefix"), DateTime.Now), new[] { (App.GetString("Setting_Backup_FilePrefix"), new[] { ".novabak" }) });
            if (path == null) return;

            bool ok = App.Store?.ExportBackup(path, includePaths) ?? false;
            ShowImportResult(ok ? App.GetString("Setting_Export_Success") : App.GetString("Setting_Export_Fail"),
                ok ? App.GetString("Setting_Export_SuccessDesc") : App.GetString("Setting_Export_FailDesc"));
        }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"导出失败: {ex}"); }
    }

    private void ImportBackupFlow(string path)
    {





        if (AutoBackupService.CreateSnapshot(isManual: true) == null)
        {
            ShowImportResult(App.GetString("Setting_Import_Fail"), App.GetString("Setting_Import_BackupFail"));
            return;
        }


        var remindersBefore = Services.ReminderScheduler.Snapshot(App.Store?.Database);
        var result = App.Store?.ImportBackup(path);
        if (result != null && result.Status == LoadStatus.NeedPassword)
        {


            _pendingEncImportPath = path;
            ShowEncImportPwdDialog();
            return;
        }
        HandleImportResult(result, remindersBefore);
    }

    private void HandleImportResult(LoadResult? result, Dictionary<Guid, DateTime>? remindersBefore = null)
    {
        if (result == null || result.Status != LoadStatus.Ok)
        {
            ShowImportResult(App.GetString("Setting_Import_Fail"), result?.Detail ?? App.GetString("Setting_Import_FailDesc"));
            return;
        }

        _pendingReloadAfterImport = true;



        Services.SyncService.MarkRestorePendingConfirm();
        WindowsHelloService.Disable();
        Services.StickySync.ClearAllNotes();


        if (remindersBefore is not null)
            Services.ReminderScheduler.Reconcile(remindersBefore, Services.ReminderScheduler.Snapshot(App.Store?.Database));

        var imported = App.Store?.Database.AppSettings;
        if (imported != null)
        {
            if (App.MainWindow is { } mw)
                mw.CloseBehavior = string.IsNullOrEmpty(imported.CloseBehavior) ? "直接退出" : imported.CloseBehavior;
            if (imported.AutoStart) StartupService.Enable(); else StartupService.Disable();
            if (imported.VisibleTabs is { Count: > 0 } tabs)
                App.MainWindow?.ApplyVisibleTabs(tabs.ToHashSet());


            if (imported.ContextMenu) Services.ContextMenuService.RegisterAll();
            else Services.ContextMenuService.UnregisterAll();




            _backupEnabled = imported.BackupEnabled;
            _statsEnabled = imported.StatsEnabled;
            AutoBackupService.SyncAutoBackupTimer(DispatcherQueue);
        }
        var theme = App.Store?.Database.AppSettings.Theme;
        var lang = App.Store?.Database.AppSettings.AppLanguage;




        bool langChanged = !string.IsNullOrEmpty(lang) && lang != App.CurrentLanguage;
        if (!string.IsNullOrEmpty(lang))
        {
            if (langChanged) App.ApplyLanguage(lang);
            App.SaveLanguageHint(lang);
        }
        PaperTheme.SyncHintWith(theme);
        if ((!string.IsNullOrEmpty(theme) && theme != App.CurrentTheme) || langChanged)
        {
            ShowImportResult(App.GetString("Setting_Import_Done"), App.GetString("Setting_Import_Done_ThemeDesc"));
        }
        else
        {
            ShowImportResult(App.GetString("Setting_Import_Done"), App.GetString("Setting_Import_Done_Desc"));
        }
    }






    private void ShowImportExportPasswordDialog(string title)
    {
        _pendingVerifiedAction = null;
        ImportExportPasswordTitle.Text = title;
        ImportExportPasswordBox.Text = "";
        ImportExportPasswordDialogTransform.ScaleX = 0.94; ImportExportPasswordDialogTransform.ScaleY = 0.94; ImportExportPasswordDialogTransform.TranslateY = 24;
        ImportExportPasswordDialog.Opacity = 0;
        ImportExportPasswordScrim.Opacity = 0;
        RegisterDialogClamp(ImportExportPasswordOverlay, ImportExportPasswordDialog);
        ImportExportPasswordOverlay.Visibility = Visibility.Visible;
        _animImportPwd = false;
        var sb = new Storyboard();
        var si = new DoubleAnimation { To = 1, Duration = TimeSpan.FromMilliseconds(250) };
        Storyboard.SetTarget(si, ImportExportPasswordScrim); Storyboard.SetTargetProperty(si, "Opacity"); sb.Children.Add(si);
        var di = new DoubleAnimation { To = 1, Duration = TimeSpan.FromMilliseconds(300) };
        Storyboard.SetTarget(di, ImportExportPasswordDialog); Storyboard.SetTargetProperty(di, "Opacity"); sb.Children.Add(di);
        Motion.AddDialogShowTransform(sb, ImportExportPasswordDialogTransform);
        sb.Begin();
    }

    private void HideImportExportPasswordDialog()
    {
        _pendingVerifiedAction = null;
        if (_animImportPwd) return;
        _animImportPwd = true;
        var sb = new Storyboard();
        var so = new DoubleAnimation { To = 0, Duration = TimeSpan.FromMilliseconds(200) };
        Storyboard.SetTarget(so, ImportExportPasswordScrim); Storyboard.SetTargetProperty(so, "Opacity"); sb.Children.Add(so);
        var d = new DoubleAnimation { To = 0, Duration = TimeSpan.FromMilliseconds(200) };
        Storyboard.SetTarget(d, ImportExportPasswordDialog); Storyboard.SetTargetProperty(d, "Opacity"); sb.Children.Add(d);
        Motion.AddDialogHideTransform(sb, ImportExportPasswordDialogTransform);
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




        ExportOptionsTitle.Text = App.GetString("Setting_ExportOptions_Title");
        ExportOptionsDesc.Text = App.GetString("Setting_ExportOptions_Desc");
        ExportIncludePathsCheckBox.Content = App.GetString("Setting_ExportOptions_IncludePaths");
        ExportOptionsDialogTransform.ScaleX = 0.94; ExportOptionsDialogTransform.ScaleY = 0.94; ExportOptionsDialogTransform.TranslateY = 24;
        ExportOptionsDialog.Opacity = 0;
        ExportOptionsScrim.Opacity = 0;
        RegisterDialogClamp(ExportOptionsOverlay, ExportOptionsDialog);
        ExportOptionsOverlay.Visibility = Visibility.Visible;
        _animExportOptions = false;
        var sb = new Storyboard();
        var si = new DoubleAnimation { To = 1, Duration = TimeSpan.FromMilliseconds(250) };
        Storyboard.SetTarget(si, ExportOptionsScrim); Storyboard.SetTargetProperty(si, "Opacity"); sb.Children.Add(si);
        var di = new DoubleAnimation { To = 1, Duration = TimeSpan.FromMilliseconds(300) };
        Storyboard.SetTarget(di, ExportOptionsDialog); Storyboard.SetTargetProperty(di, "Opacity"); sb.Children.Add(di);
        Motion.AddDialogShowTransform(sb, ExportOptionsDialogTransform);
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
        Motion.AddDialogHideTransform(sb, ExportOptionsDialogTransform);
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
        if (_pickerFlowBusy) return;
        _pickerFlowBusy = true;
        try
        {
            bool includePaths = ExportIncludePathsCheckBox.IsChecked == true;
            HideExportOptionsDialog();
            await ExportBackupFlowAsync(includePaths);
        }
        finally { _pickerFlowBusy = false; }
    }







    private void ShowEncExportDialog()
    {

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

        EncUseLockCheckBox.IsChecked = false;
        EncUseLockCheckBox.Visibility = PrivacyLockEnabledNow ? Visibility.Visible : Visibility.Collapsed;
        EncUseLockWarn.Visibility = Visibility.Collapsed;
        EncPasswordBox.Text = "";
        EncConfirmBox.Text = "";
        EncStrengthText.Visibility = Visibility.Collapsed;
        EncIncludePathsCheckBox.IsChecked = false;
        EncExportConfirmButton.IsEnabled = false;
        _animEncExport = false;
        ShowOverlay(EncExportOverlay, EncExportDialog, EncExportDialogTransform);
    }

    private void HideEncExportDialog()
    {
        if (_animEncExport) return;
        _animEncExport = true;
        HideOverlay(EncExportOverlay, EncExportDialog, EncExportDialogTransform, () => _animEncExport = false);
    }

    private void EncExportClose_Click(object sender, RoutedEventArgs e) => HideEncExportDialog();
    private void EncExportCancel_Click(object sender, RoutedEventArgs e) => HideEncExportDialog();
    private void EncExportScrim_Tapped(object sender, TappedRoutedEventArgs e)
    { if (ReferenceEquals(e.OriginalSource, EncExportScrim)) HideEncExportDialog(); }

    private void EncUseLockCheckBox_Changed(object sender, RoutedEventArgs e)
    {


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
                    EncStrengthText.Foreground = new SolidColorBrush(Color.FromArgb(0xFF, 0x4C, 0xAF, 0x50));
                    break;
                case Novara.Services.BackupPasswordStrength.Medium:
                    EncStrengthText.Text = App.GetString("Setting_EncBackup_StrengthMedium");
                    EncStrengthText.Foreground = new SolidColorBrush(Color.FromArgb(0xFF, 0xFF, 0xD7, 0x00));
                    break;
                default:
                    EncStrengthText.Text = App.GetString("Setting_EncBackup_StrengthWeak");
                    EncStrengthText.Foreground = App.GetBrush("AppDangerTextBrush");
                    break;
            }
        }
        UpdateEncExportConfirmState();
    }

    private void UpdateEncExportConfirmState()
    {


        bool valid = EncUseLockCheckBox.IsChecked == true
            || (EncPasswordBox.Text.Length > 0 && EncPasswordBox.Text == EncConfirmBox.Text);
        EncExportConfirmButton.IsEnabled = valid;
    }

    private async void EncExportConfirm_Click(object sender, RoutedEventArgs e)
    {
        if (_pickerFlowBusy) return;
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
        try
        {
            var password = EncUseLockCheckBox.IsChecked == true
                ? App.Store?.Password
                : EncPasswordBox.Text;
            if (string.IsNullOrEmpty(password)) return;
            var path = FilePicker.PickSaveFile(string.Format("{0}_{1:yyyyMMdd_HHmmss}", App.GetString("Setting_Archive_ExportEncrypted"), DateTime.Now), new[] { (App.GetString("Setting_Archive_ExportEncrypted"), new[] { ".novaenc" }) });
            if (path == null) return;

            bool ok = App.Store?.ExportBackupEncrypted(path, password, includePaths) ?? false;
            if (ok)
            {

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
        EncImportConfirmButton.IsEnabled = false;
        _animEncImportPwd = false;
        ShowOverlay(EncImportPwdOverlay, EncImportPwdDialog, EncImportPwdDialogTransform);
    }

    private void HideEncImportPwdDialog()
    {
        if (_animEncImportPwd) return;
        _animEncImportPwd = true;
        _pendingEncImportPath = null;
        HideOverlay(EncImportPwdOverlay, EncImportPwdDialog, EncImportPwdDialogTransform, () => _animEncImportPwd = false);
    }

    private void EncImportPwdClose_Click(object sender, RoutedEventArgs e) => HideEncImportPwdDialog();
    private void EncImportPwdCancel_Click(object sender, RoutedEventArgs e) => HideEncImportPwdDialog();
    private void EncImportPwdScrim_Tapped(object sender, TappedRoutedEventArgs e)
    { if (ReferenceEquals(e.OriginalSource, EncImportPwdScrim)) HideEncImportPwdDialog(); }

    private void EncImportPwdBox_TextChanged(object sender, TextChangedEventArgs e)
        => EncImportConfirmButton.IsEnabled = EncImportPwdBox.Text.Length > 0;

    private void EncImportPwdConfirm_Click(object sender, RoutedEventArgs e)
    {
        if (_animEncImportPwd) return;
        var pw = EncImportPwdBox.Text;
        if (pw.Length == 0) { FlashTextBox(EncImportPwdBox); return; }
        var path = _pendingEncImportPath;
        if (string.IsNullOrEmpty(path)) { HideEncImportPwdDialog(); return; }
        var remindersBefore = Services.ReminderScheduler.Snapshot(App.Store?.Database);
        var result = App.Store?.ImportBackup(path, pw);


        if (result != null && result.Status == LoadStatus.Corrupted && result.Detail == Loc.T("Store_Err_BackupAuthFail"))
        {
            FlashTextBox(EncImportPwdBox);
            return;
        }
        HideEncImportPwdDialog();
        HandleImportResult(result, remindersBefore);
    }





    private void ShowExportMdNoticeDialog()
    {
        _animExportMdNotice = false;
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
        if (_pickerFlowBusy) return;
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
            var path = FilePicker.PickSaveFile($"Novara_{App.GetString("Setting_Export_FileNameSummary")}_{DateTime.Now:yyyyMMdd_HHmmss}", new[] { ("Markdown", new[] { ".md" }) });
            if (path == null) return;

            var md = BuildSummaryMarkdown();
            System.IO.File.WriteAllText(path, md, new System.Text.UTF8Encoding(false));
            App.ShowToast(App.GetString("Common_Toast_Exported"));
        }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"导出 Markdown 失败: {ex.Message}"); App.ShowToast(App.GetString("Common_Toast_Failed"), ToastTone.Error); }
    }





    private async System.Threading.Tasks.Task ExportHtmlFlowAsync()
    {
        try
        {
            var path = FilePicker.PickSaveFile($"Novara_{App.GetString("Setting_Export_FileNameCollection")}_{DateTime.Now:yyyyMMdd_HHmmss}", new[] { ("HTML", new[] { ".html" }) });
            if (path == null) return;

            var html = BuildHtmlCollection();
            System.IO.File.WriteAllText(path, html, new System.Text.UTF8Encoding(false));
            App.ShowToast(App.GetString("Common_Toast_Exported"));
        }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"导出 HTML 合集失败: {ex.Message}"); App.ShowToast(App.GetString("Common_Toast_Failed"), ToastTone.Error); }
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



        sb.AppendLine("<meta http-equiv=\"Content-Security-Policy\" content=\"default-src 'none'; style-src 'unsafe-inline'; img-src * data:;\">");
        sb.AppendLine("<meta charset=\"UTF-8\">");
        sb.AppendLine("<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">");
        sb.AppendLine($"<title>Novara {App.GetString("Setting_Export_FileNameCollection")}</title>");
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
        sb.AppendLine("<div class=\"header\">" + logoHtml + "<div><div class=\"brand\">Novara</div><div class=\"tagline\">" + App.GetString("Export_Html_Tagline") + "</div></div></div>");
        sb.AppendLine($"<p class=\"meta\">{string.Format(App.GetString("Export_ExportedAt"), DateTime.Now.ToString("yyyy-MM-dd HH:mm"))}</p>");

        foreach (var d in diaries)
        {
            string title = System.Net.WebUtility.HtmlEncode(string.IsNullOrWhiteSpace(d.Title) ? App.GetString("Export_Untitled") : d.Title);
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

        sb.AppendLine($"<div class=\"footer\"><div class=\"slogan\">{App.GetString("Export_Html_Slogan")}</div><div>{App.GetString("Export_Website")}<a href=\"https://novara.xin\">novara.xin</a></div></div>");
        sb.AppendLine("</body>");
        sb.AppendLine("</html>");
        return sb.ToString();
    }




    private async System.Threading.Tasks.Task ExportPdfFlowAsync()
    {
        try
        {
            var path = FilePicker.PickSaveFile($"Novara_{App.GetString("Setting_Export_FileNameCollection")}_{DateTime.Now:yyyyMMdd_HHmmss}", new[] { ("PDF", new[] { ".pdf" }) });
            if (path == null) return;

            bool pdfOk = await ExportPdfViaWebView2Async(BuildHtmlCollection(), path);
            App.ShowToast(App.GetString(pdfOk ? "Common_Toast_Exported" : "Common_Toast_Failed"), pdfOk ? ToastTone.Success : ToastTone.Error);
        }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"导出 PDF 合集失败: {ex.Message}"); }
    }


    private async System.Threading.Tasks.Task<bool> ExportPdfViaWebView2Async(string html, string pdfPath)
    {
        var webView = new Microsoft.UI.Xaml.Controls.WebView2
        {
            Visibility = Visibility.Collapsed,
            Width = 794,
            Height = 1123,
        };
        ToolsRoot.Children.Add(webView);
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
            webView.NavigationCompleted += (_, e) => navTcs.TrySetResult(e.IsSuccess);
            webView.NavigateToString(html);

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
            ToolsRoot.Children.Remove(webView);
            try { webView.Close(); } catch { }
        }
    }







    private async System.Threading.Tasks.Task ExportImageCollectionFlowAsync()
    {
        string? picked = null;
        try
        {
            var db = App.Store?.Database;
            if (db == null) return;
            var path = picked = FilePicker.PickSaveFile($"Novara_{App.GetString("Export_Section_Memo")}_{DateTime.Now:yyyyMMdd_HHmmss}", new[] { ("PNG", new[] { ".png" }) });
            if (path == null) return;

            var logo = ImageExportService.TryReadLogoSvg();
            var exportedAt = DateTime.Now;
            string wide = ImageExportTemplates.BuildMemoCollectionHtml(db.MemoGroups, db.MemoEntries,
                ImageExportTemplates.WideWidth, exportedAt, logo);
            string narrow = ImageExportTemplates.BuildMemoCollectionHtml(db.MemoGroups, db.MemoEntries,
                ImageExportTemplates.NarrowWidth, exportedAt, logo);

            var narrowPath = ImageExportService.SiblingPath(path, "_mobile");
            int wideParts = 0, narrowParts = 0;
            bool wideDone = false, narrowDone = false;
            var session = await ImageExportSession.BeginAsync(wide, ImageExportTemplates.WideWidth, ToolsRoot);
            if (session != null)
            {
                try
                {
                    (wideParts, wideDone) = await session.ScreenshotToFileAsync(path);
                    if (wideDone && await session.LoadAsync(narrow, ImageExportTemplates.NarrowWidth))
                        (narrowParts, narrowDone) = await session.ScreenshotToFileAsync(narrowPath);
                }
                finally { session.Close(); }
            }
            if (wideDone && narrowDone)
            {
                int parts = Math.Max(wideParts, narrowParts);
                App.ShowToast(parts > 1
                    ? string.Format(App.GetString("Export_Image_MultiPart"), parts)
                    : App.GetString("Common_Toast_Exported"));
            }
            else
            {
                ImageExportService.DeleteGroupFiles(path, wideParts);
                ImageExportService.DeleteGroupFiles(narrowPath, narrowParts);
                App.ShowToast(App.GetString("Common_Toast_Failed"), ToastTone.Error);
            }
        }
        catch (Exception ex)
        {

            System.Diagnostics.Debug.WriteLine($"导出图片合集失败: {ex.Message}");
            if (picked != null)
            {
                ImageExportService.DeleteGroupFiles(picked, 0);
                ImageExportService.DeleteGroupFiles(ImageExportService.SiblingPath(picked, "_mobile"), 0);
            }
            App.ShowToast(App.GetString("Common_Toast_Failed"), ToastTone.Error);
        }
    }

    private string BuildSummaryMarkdown()
    {
        var db = App.Store?.Database;
        if (db == null) return "";
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"# {App.GetString("Export_Summary_Title")}");
        sb.AppendLine();
        sb.AppendLine($"> {string.Format(App.GetString("Export_ExportedAt"), DateTime.Now.ToString("yyyy-MM-dd HH:mm"))}");
        sb.AppendLine();


        var entries = db.MemoEntries.Where(x => !x.IsDeleted).ToList();
        var groups = db.MemoGroups.ToList();
        sb.AppendLine($"## {App.GetString("Export_Section_Memo")}");
        sb.AppendLine();
        foreach (var g in groups)
        {
            sb.AppendLine($"### 📁 {g.Name}");
            var inGroup = entries.Where(e => e.GroupId == g.Id).ToList();
            if (inGroup.Count == 0) { sb.AppendLine(App.GetString("Export_Empty")); sb.AppendLine(); continue; }
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
            sb.AppendLine($"### 📁 {App.GetString("Export_Section_Uncategorized")}");
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
            sb.AppendLine($"## {App.GetString("Export_Section_Path")}");
            sb.AppendLine();
            foreach (var p in paths)
            {
                sb.AppendLine($"- **{p.Name}**");
                sb.AppendLine($"  - {App.GetString("Export_Label_Path")}{p.Path}");
                if (!string.IsNullOrWhiteSpace(p.Note)) sb.AppendLine($"  - {App.GetString("Export_Label_Note")}{p.Note}");
            }
            sb.AppendLine();
        }


        var todos = db.TodoCards.Where(x => !x.IsDeleted).ToList();
        var notes = db.NoteCards.Where(x => !x.IsDeleted).ToList();
        if (todos.Count > 0 || notes.Count > 0)
        {
            sb.AppendLine($"## {App.GetString("Export_Section_Plan")}");
            sb.AppendLine();
            if (todos.Count > 0)
            {
                sb.AppendLine($"### {App.GetString("Export_Section_Todo")}");
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
                sb.AppendLine($"### {App.GetString("Export_Section_Note")}");
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
            sb.AppendLine($"## {App.GetString("Export_Section_Diary")}");
            sb.AppendLine();
            foreach (var d in diaries)
            {
                sb.AppendLine($"### {App.DiaryTitleText(d.Title)}");
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



    private void RunPrivacyGated(Action flow)
    {
        if (!PrivacyLockEnabledNow) { flow(); return; }
        _pendingExport = false;
        ShowImportExportPasswordDialog(App.GetString("Setting_Archive_VerifyTitle"));




        _pendingVerifiedAction = flow;
    }

    private void ImportExportPasswordConfirm_Click(object sender, RoutedEventArgs e)
    {
        if (_animImportPwd) return;
        var pw = ImportExportPasswordBox.Text.Trim();
        if (pw.Length < 6) { FlashTextBox(ImportExportPasswordBox); return; }
        if (!PasswordService.Verify(pw)) { FlashTextBox(ImportExportPasswordBox); return; }

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
        if (export) ShowPlainExportWarn(ShowExportOptionsDialog);
        else _ = ImportAfterPasswordAsync(null);
    }

    private void ShowImportConfirmDialog()
    {
        ImportConfirmDialogTransform.ScaleX = 0.94; ImportConfirmDialogTransform.ScaleY = 0.94; ImportConfirmDialogTransform.TranslateY = 24;
        ImportConfirmDialog.Opacity = 0;
        ImportConfirmScrim.Opacity = 0;
        RegisterDialogClamp(ImportConfirmOverlay, ImportConfirmDialog);
        ImportConfirmOverlay.Visibility = Visibility.Visible;
        _animImportConfirm = false;
        var sb = new Storyboard();
        var si = new DoubleAnimation { To = 1, Duration = TimeSpan.FromMilliseconds(250) };
        Storyboard.SetTarget(si, ImportConfirmScrim); Storyboard.SetTargetProperty(si, "Opacity"); sb.Children.Add(si);
        var di = new DoubleAnimation { To = 1, Duration = TimeSpan.FromMilliseconds(300) };
        Storyboard.SetTarget(di, ImportConfirmDialog); Storyboard.SetTargetProperty(di, "Opacity"); sb.Children.Add(di);
        Motion.AddDialogShowTransform(sb, ImportConfirmDialogTransform);
        sb.Begin();
    }

    private void HideImportConfirmDialog()
    {
        if (_animImportConfirm) return;
        _animImportConfirm = true;
        var sb = new Storyboard();
        var so = new DoubleAnimation { To = 0, Duration = TimeSpan.FromMilliseconds(200) };
        Storyboard.SetTarget(so, ImportConfirmScrim); Storyboard.SetTargetProperty(so, "Opacity"); sb.Children.Add(so);
        var d = new DoubleAnimation { To = 0, Duration = TimeSpan.FromMilliseconds(200) };
        Storyboard.SetTarget(d, ImportConfirmDialog); Storyboard.SetTargetProperty(d, "Opacity"); sb.Children.Add(d);
        Motion.AddDialogHideTransform(sb, ImportConfirmDialogTransform);
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
        if (scrim != null) scrim.Opacity = 0;
        ImportResultDialog.Opacity = 0;
        RegisterDialogClamp(ImportResultOverlay, ImportResultDialog);
        ImportResultOverlay.Visibility = Visibility.Visible;
        _animImportResult = false;
        var sb = new Storyboard();
        if (scrim != null)
        {
            var si = new DoubleAnimation { To = 1, Duration = TimeSpan.FromMilliseconds(250) };
            Storyboard.SetTarget(si, scrim); Storyboard.SetTargetProperty(si, "Opacity"); sb.Children.Add(si);
        }
        var di = new DoubleAnimation { To = 1, Duration = TimeSpan.FromMilliseconds(300) };
        Storyboard.SetTarget(di, ImportResultDialog); Storyboard.SetTargetProperty(di, "Opacity"); sb.Children.Add(di);
        Motion.AddDialogShowTransform(sb, ImportResultDialogTransform);
        sb.Begin();
    }

    private void HideImportResult()
    {
        if (_animImportResult) return;
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
        Motion.AddDialogHideTransform(sb, ImportResultDialogTransform);
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



            CsvImportPreviewConfirmBtn.Width = 160;
        }

        CsvImportPreviewDialogTransform.ScaleX = 0.94; CsvImportPreviewDialogTransform.ScaleY = 0.94; CsvImportPreviewDialogTransform.TranslateY = 24;
        CsvImportPreviewDialog.Opacity = 0;
        CsvImportPreviewScrim.Opacity = 0;
        RegisterDialogClamp(CsvImportPreviewOverlay, CsvImportPreviewDialog);
        CsvImportPreviewOverlay.Visibility = Visibility.Visible;
        _animCsvPreview = false;
        FitDialogScroll(CsvImportPreviewScroll, 270);
        var sb = new Storyboard();
        var si = new DoubleAnimation { To = 1, Duration = TimeSpan.FromMilliseconds(250) };
        Storyboard.SetTarget(si, CsvImportPreviewScrim); Storyboard.SetTargetProperty(si, "Opacity"); sb.Children.Add(si);
        var di = new DoubleAnimation { To = 1, Duration = TimeSpan.FromMilliseconds(300) };
        Storyboard.SetTarget(di, CsvImportPreviewDialog); Storyboard.SetTargetProperty(di, "Opacity"); sb.Children.Add(di);
        Motion.AddDialogShowTransform(sb, CsvImportPreviewDialogTransform);
        sb.Begin();
    }

    private void HideCsvImportPreview()
    {
        if (_animCsvPreview) return;





        _csvImportText = "";
        _csvImportResult = null;
        CsvImportPreviewList.Children.Clear();
        _animCsvPreview = true;
        var sb = new Storyboard();
        var so = new DoubleAnimation { To = 0, Duration = TimeSpan.FromMilliseconds(200) };
        Storyboard.SetTarget(so, CsvImportPreviewScrim); Storyboard.SetTargetProperty(so, "Opacity"); sb.Children.Add(so);
        var d = new DoubleAnimation { To = 0, Duration = TimeSpan.FromMilliseconds(200) };
        Storyboard.SetTarget(d, CsvImportPreviewDialog); Storyboard.SetTargetProperty(d, "Opacity"); sb.Children.Add(d);
        Motion.AddDialogHideTransform(sb, CsvImportPreviewDialogTransform);
        sb.Completed += (_, _) => { CsvImportPreviewOverlay.Visibility = Visibility.Collapsed; _animCsvPreview = false; };
        sb.Begin();
    }

    private void CsvImportPreviewScrim_Tapped(object sender, TappedRoutedEventArgs e) { if (ReferenceEquals(e.OriginalSource, CsvImportPreviewScrim)) HideCsvImportPreview(); }
    private void CsvImportPreviewClose_Click(object sender, RoutedEventArgs e) => HideCsvImportPreview();
    private void CsvImportPreviewCancel_Click(object sender, RoutedEventArgs e) => HideCsvImportPreview();
    private CsvImportResult? _csvImportResult;
    private string _csvImportText = "";
    private string _csvImportType = "账户";

    private static readonly IReadOnlyList<string> CsvImportTypeOptions = MemoEntryTypes.All;
    private MenuFlyout? _csvTypeMenu;

    private void PickAndShowCsvImport()
    {
        try
        {
            var path = FilePicker.PickFile("*.csv");
            if (path == null) return;
            _csvImportText = System.IO.File.ReadAllText(path);


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



        var text = _csvImportText;
        var parsed = _csvImportResult;
        var finalResult = parsed != null && parsed.IsNovaraFormat
            ? parsed
            : CsvImportExportService.ParseCsv(text, _csvImportType);
        HideCsvImportPreview();
        var db = App.Store?.Database;
        if (db == null) return;
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
        App.MainWindow?.ReloadPages();
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


    private void ShowCsvExportNoticeDialog()
    {
        CsvExportNoticeTitle.Text = App.GetString("Setting_CsvExport_NoticeTitle");
        CsvExportNoticeDesc.Text = App.GetString("Setting_CsvExport_NoticeDesc");
        CsvExportNoticeCancelText.Text = App.GetString("Common_Button_Cancel");
        CsvExportNoticeConfirmText.Text = App.GetString("Setting_Archive_Export");

        CsvExportNoticeDialogTransform.ScaleX = 0.94; CsvExportNoticeDialogTransform.ScaleY = 0.94; CsvExportNoticeDialogTransform.TranslateY = 24;
        CsvExportNoticeDialog.Opacity = 0;
        CsvExportNoticeScrim.Opacity = 0;
        RegisterDialogClamp(CsvExportNoticeOverlay, CsvExportNoticeDialog);
        CsvExportNoticeOverlay.Visibility = Visibility.Visible;
        _animCsvExportNotice = false;
        var sb = new Storyboard();
        var si = new DoubleAnimation { To = 1, Duration = TimeSpan.FromMilliseconds(250) };
        Storyboard.SetTarget(si, CsvExportNoticeScrim); Storyboard.SetTargetProperty(si, "Opacity"); sb.Children.Add(si);
        var di = new DoubleAnimation { To = 1, Duration = TimeSpan.FromMilliseconds(300) };
        Storyboard.SetTarget(di, CsvExportNoticeDialog); Storyboard.SetTargetProperty(di, "Opacity"); sb.Children.Add(di);
        Motion.AddDialogShowTransform(sb, CsvExportNoticeDialogTransform);
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
        Motion.AddDialogHideTransform(sb, CsvExportNoticeDialogTransform);
        sb.Completed += (_, _) => { CsvExportNoticeOverlay.Visibility = Visibility.Collapsed; _animCsvExportNotice = false; };
        sb.Begin();
    }

    private void CsvExportNoticeScrim_Tapped(object sender, TappedRoutedEventArgs e) { if (ReferenceEquals(e.OriginalSource, CsvExportNoticeScrim)) HideCsvExportNoticeDialog(); }
    private void CsvExportNoticeClose_Click(object sender, RoutedEventArgs e) => HideCsvExportNoticeDialog();
    private void CsvExportNoticeCancel_Click(object sender, RoutedEventArgs e) => HideCsvExportNoticeDialog();
    private async void CsvExportNoticeConfirm_Click(object sender, RoutedEventArgs e)
    {
        if (_pickerFlowBusy) return;
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
            var path = FilePicker.PickSaveFile($"Novara_{App.GetString("Setting_CsvExport_FileName")}_{DateTime.Now:yyyyMMdd_HHmmss}", new[] { ("CSV", new[] { ".csv" }) });
            if (path == null) return;

            var entries = App.Store?.Database.MemoEntries ?? new();
            var groups = App.Store?.Database.MemoGroups ?? new();
            var csv = CsvImportExportService.ExportMemoEntriesToCsv(entries, groups);
            System.IO.File.WriteAllText(path, csv, new System.Text.UTF8Encoding(true));
            App.ShowToast(App.GetString("Common_Toast_Exported"));
        }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"CSV 导出失败: {ex.Message}"); App.ShowToast(App.GetString("Common_Toast_Failed"), ToastTone.Error); }
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




    private void ShowBackupIntroDialog()
    {
        _animBackupIntro = false;
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
        _animBackupNow = false;
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
        if (snapshot == null) { App.ShowToast(App.GetString("Common_Toast_Failed"), ToastTone.Error); return; }
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
        _animBackupAuto = false;
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
        _animBackupRestore = false;
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
        _animBackupRestoreConfirm = false;
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
            App.ShowToast(App.GetString("Store_Err_IoFail"), ToastTone.Error);
        }
    }
    private void BackupRestoreConfirmCancel_Click(object sender, RoutedEventArgs e) => CancelBackupRestoreConfirm();
    private void BackupRestoreConfirmClose_Click(object sender, RoutedEventArgs e) => CancelBackupRestoreConfirm();
    private void BackupRestoreConfirmScrim_Tapped(object sender, TappedRoutedEventArgs e)
    { if (ReferenceEquals(e.OriginalSource, BackupRestoreConfirmScrim)) CancelBackupRestoreConfirm(); }


    private void ShowBackupDeleteDialog()
    {
        _animBackupDelete = false;
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
        _animBackupDeleteConfirm = false;
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
            App.ShowToast(App.GetString("Setting_Backup_RestartFail_Rollback"), ToastTone.Error);
        }
        else
        {
            App.ShowToast(App.GetString("Setting_Backup_RestartFail_RollbackFail"), ToastTone.Error);
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
            UpdateStatsUI(animate: true);
            App.ShowToast(App.GetString("Common_Toast_Switched"));
        };
        offItem.Click += (_, _) =>
        {
            if (!_statsEnabled) return;
            _statsEnabled = false;
            PersistSetting(s => s.StatsEnabled = false);
            UpdateStatsUI(animate: true);
            App.ShowToast(App.GetString("Common_Toast_Switched"));
        };

        menu.Items.Add(onItem); menu.Items.Add(offItem);
        menu.ShowAt(StatsToggleButton, new Windows.Foundation.Point(0, StatsToggleButton.ActualHeight + 4));
    }


    private void StatsExpandButton_Click(object sender, RoutedEventArgs e)
    {
        _statsExpanded = !_statsExpanded;
        UpdateStatsUI(animate: true);
    }

    private void UpdateStatsUI(bool animate = false)
    {

        StatsToggleText.Text = _statsEnabled
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
            if (animate) Services.MeltAnim.Begin(StatsDetailPanel, true, 16.0);
            else Services.MeltAnim.SetInstant(StatsDetailPanel, true, 16.0);
        }
        else if (_statsEnabled)
        {
            if (animate) Services.MeltAnim.Begin(StatsDetailPanel, false, 16.0);
            else Services.MeltAnim.SetInstant(StatsDetailPanel, false, 16.0);
        }
        else
        {

            if (animate) Services.MeltAnim.Begin(StatsDetailPanel, false, 16.0);
            else { Services.MeltAnim.SetInstant(StatsDetailPanel, false, 16.0); StatsGrid.Children.Clear(); }
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



            int done = todos.Count(t => t.CheckedStates is { Count: > 0 } cs && cs.Count == 1 + t.SubTexts.Count && cs.All(c => c));
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




        var syncState = Services.SyncService.State;
        bool syncPaired = IsSyncPaired(syncState);
        long cloudVersion = Math.Max(syncState.BaseVersion, syncState.LastSeenVersion);
        string cloudVersionText = cloudVersion > 0 ? cloudVersion.ToString() : App.GetString("Setting_Stats_None");
        string lastSyncText = syncState.LastSyncAt is { } lastSyncAt
            ? lastSyncAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm")
            : App.GetString("Sync_Label_Never");


        var visibleTabs = App.Store?.Database.AppSettings.VisibleTabs ?? new List<string>();
        bool showMemo = visibleTabs.Contains("备忘");
        bool showFile = visibleTabs.Contains("文件");
        bool showPlan = visibleTabs.Contains("计划");
        bool showDiary = visibleTabs.Contains("日记");

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


            (App.GetString("Setting_Stats_SyncStatus"),
                App.GetString(syncPaired ? "Sync_Status_Ready" : "Sync_Status_Unpaired"), true, false),
            (App.GetString("Setting_Stats_CloudVersion"), cloudVersionText, true, false),
            (App.GetString("Setting_Stats_LastSync"), lastSyncText, true, false),
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
                    ? App.GetBrush("AppDangerTextBrush")
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




    private void UpdateMcpUI(bool animate = false)
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


        if (enabled && expanded) { BuildMcpAuthorizedList(); if (animate) Services.MeltAnim.Begin(McpDetailPanel, true, 16.0); else Services.MeltAnim.SetInstant(McpDetailPanel, true, 16.0); }
        else if (enabled) { if (animate) Services.MeltAnim.Begin(McpDetailPanel, false, 16.0); else Services.MeltAnim.SetInstant(McpDetailPanel, false, 16.0); }
        else { if (animate) Services.MeltAnim.Begin(McpDetailPanel, false, 16.0); else Services.MeltAnim.SetInstant(McpDetailPanel, false, 16.0); }


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
        UpdateMcpUI(animate: true);
        App.ShowToast(App.GetString("Common_Toast_Switched"));
    }

    private void McpExpandButton_Click(object sender, RoutedEventArgs e)
    {
        var settings = App.Store?.Database.AppSettings;
        if (settings == null) return;
        settings.McpDetailExpanded = !settings.McpDetailExpanded;
        App.Store?.SaveAsync();
        UpdateMcpUI(animate: true);
    }

    private void McpConfigButton_Click(object sender, RoutedEventArgs e)
    {
        UpdateMcpUI();
        ShowMcpConfigDialog();
    }

    private static string GenerateToken()
    {
        var bytes = RandomNumberGenerator.GetBytes(32);
        return Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
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


        try { Clipboard.SetContent(dp); App.ShowToast(App.GetString("Common_Toast_Copied")); }
        catch { App.ShowToast(App.GetString("Common_Toast_CopyFail")); }
    }

    private void BuildMcpAuthorizedList()
    {
        McpAuthorizedList.Children.Clear();
        var list = Novara.Services.McpService.GetAuthorizedSnapshot();
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
            Grid.SetColumn(revoke, 2);
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
        _animMcpRevokeConfirm = false;
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
            if (Novara.Services.McpService.RevokeAuthorizedProcess(_pendingRevokePath))
                App.Store?.SaveAsync();
            BuildMcpAuthorizedList();
        }
        _pendingRevokePath = null;
        HideMcpRevokeConfirmDialog();
    }




    private string? _mcpPermPath;
    private string? _pendingMcpPermPath;
    private bool _mcpPermDeleteGateOff;



    public void OpenMcpPermEditor(string clientPath)
    {
        if (!IsLoaded) { _pendingMcpPermPath = clientPath; return; }
        _mcpPermPath = clientPath;

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
        _mcpPermDeleteGateOff = !(App.Store?.Database.AppSettings.McpDeleteEnabled ?? false);
        var bits = McpService.ReadClientPermissions(clientPath);
        SetMatrix(bits);
        ApplyDeleteGate(!_mcpPermDeleteGateOff);
        UpdateMcpPermToggleAll();
        _animMcpPerm = false;
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

    private void SetMatrix(McpPerm bits)
    {






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



    private void UpdateMcpPermToggleAll()
        => McpPermToggleAllText.Text = App.GetString(
            ReadMatrix() == McpPerm.None ? "Setting_Mcp_PresetAll" : "Setting_Mcp_PresetNone");

    private void McpPermChk_Changed(object sender, RoutedEventArgs e) => UpdateMcpPermToggleAll();

    private void McpPermToggleAll_Click(object sender, RoutedEventArgs e)
    {


        SetMatrix(ReadMatrix() == McpPerm.None ? McpPermissions.LegacyFull : McpPerm.None);
        UpdateMcpPermToggleAll();
    }

    private void McpPermSave_Click(object sender, RoutedEventArgs e)
    {
        var path = _mcpPermPath;
        if (string.IsNullOrEmpty(path)) { HideMcpPermDialog(); return; }
        var settings = App.Store?.Database.AppSettings;
        if (settings == null) { HideMcpPermDialog(); return; }
        var bits = ReadMatrix();
        McpService.SaveClientPermissions(path, bits);
        PersistSetting(_ => { });
        BuildMcpAuthorizedList();
        HideMcpPermDialog();
    }

    private void McpPermCancel_Click(object sender, RoutedEventArgs e) => HideMcpPermDialog();
    private void McpPermClose_Click(object sender, RoutedEventArgs e) => HideMcpPermDialog();
    private void McpPermScrim_Tapped(object sender, TappedRoutedEventArgs e)
    { if (ReferenceEquals(e.OriginalSource, McpPermScrim)) HideMcpPermDialog(); }

    private void HideMcpPermDialog()
    {
        if (_animMcpPerm) return;
        _animMcpPerm = true;
        HideOverlay(McpPermOverlay, McpPermDialog, McpPermDialogTransform, () => _animMcpPerm = false);
    }

    private void McpPermRowButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string path) OpenMcpPermEditor(path);
    }




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
            "audit_cleared" => App.GetString("Setting_Mcp_Audit_Ev_Cleared"),
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
        _animMcpAudit = false;
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
        _animMcpAuditClear = false;
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



        Novara.Services.McpAuditLog.ClearAllWithMarker();
        BuildMcpAuditList();
        HideMcpAuditClearConfirmDialog();
    }



    private void ShowMcpConfigDialog()
    {
        _animMcpConfig = false;
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
        _animMcpResetConfirm = false;
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
        _animMcpCopy = false;
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
            ? App.GetBrush("AppDangerBrush")
            : new SolidColorBrush(Color.FromArgb(0, 0, 0, 0));
        McpDeletePermissionButton.Foreground = on
            ? new SolidColorBrush(Color.FromArgb(255, 255, 255, 255))
            : App.GetBrush("AppTextPrimaryBrush");
        McpDeletePermissionText.Text = App.GetString(on ? "Setting_Autostart_On" : "Setting_Autostart_Off");
    }

    private void ShowMcpDeleteConfirmDialog()
    {
        _animMcpDeleteConfirm = false;
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
                    env = new { NOVARA_MCP_TOKEN = token }
                }
            }
        };
        return System.Text.Json.JsonSerializer.Serialize(config, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
    }

    private string BuildMcpHint() => App.GetString("Setting_Mcp_Hint").Replace("{JSON}", BuildMcpJson());





    private bool _animSyncPair;
    private bool _animSyncUnpair;
    private bool _animSyncConflict;
    private bool _animSyncAdopt;
    private bool _animSyncKeySaved;
    private bool _animSyncKeyView;
    private bool _animSyncAudit;
    private bool _animSyncAuditClear;
    private bool _animSyncReadAccess;
    private bool _animSyncReadAccessRevoke;
    private bool _animSyncEditAccess;
    private bool _animSyncEditAccessRevoke;
    private bool _readAccessConfigured;
    private string? _readAccessToken;
    private bool _editorAccessConfigured;
    private string? _editorAccessToken;
    private Services.SyncRoundOutcome? _lastSyncOutcome;
    private Services.SyncConflictInspection? _syncConflict;
    private bool _syncConflictBusy;
    private string? _revealedSpaceKey;
    private bool _syncKeyShown;
    private string? _pendingImportPath;
    private string? _importedSpaceKey;
    private bool _animSyncKeyImportPwd;
    private bool _animSyncKeyImportResult;


    private void OnSyncRoundCompleted(Services.SyncRoundOutcome outcome)
    {

        App.UiQueue?.TryEnqueue(() =>
        {
            _lastSyncOutcome = outcome;
            RefreshSyncStatus();
        });
    }

    private void UpdateSyncUI(bool animate = false)
    {

        SyncTitleText.Text = App.GetString("Sync_Card_Title");
        SyncPairButtonText.Text = App.GetString("Sync_Button_Pair");

        SyncLabelServer.Text = App.GetString("Sync_Label_Server");
        SyncLabelSpace.Text = App.GetString("Sync_Label_Space");
        SyncLabelDevice.Text = App.GetString("Sync_Label_Device");
        SyncLabelLastSync.Text = App.GetString("Sync_Label_LastSync");
        SyncLabelState.Text = App.GetString("Sync_Label_State");
        SyncLabelFrequency.Text = App.GetString("Sync_Label_Frequency");
        SyncKeyLabel.Text = App.GetString("Sync_Key_View_Label");
        SyncDetailHint.Text = App.GetString("Sync_Detail_Hint");


        SyncConflictTitleText.Text = App.GetString("Sync_Conflict_Title");
        SyncConflictIntroText.Text = App.GetString("Sync_Conflict_Intro");
        SyncConflictLoadingText.Text = App.GetString("Sync_Conflict_Loading");
        SyncConflictColLocalText.Text = App.GetString("Sync_Conflict_ColLocalOnly");
        SyncConflictColRemoteText.Text = App.GetString("Sync_Conflict_ColRemoteOnly");
        SyncConflictColBothText.Text = App.GetString("Sync_Conflict_ColBothChanged");
        SyncConflictOnlySettingsText.Text = App.GetString("Sync_Conflict_OnlySettings");
        SyncConflictExportHintText.Text = App.GetString("Sync_Conflict_ExportHint");
        SyncConflictExportKeyHintText.Text = App.GetString("Sync_Conflict_ExportKeyHint");
        SyncExportRemoteText.Text = App.GetString("Sync_Conflict_ExportRemote");
        SyncExportLocalText.Text = App.GetString("Sync_Conflict_ExportLocal");
        SyncConflictConsequencesText.Text = App.GetString("Sync_Conflict_Consequences");
        SyncKeepLocalText.Text = App.GetString("Sync_Conflict_KeepLocal");
        SyncTakeRemoteText.Text = App.GetString("Sync_Conflict_TakeRemote");
        SyncConflictWarnIcon.Data = App.CreateGeometry(IconData.Danger);


        SyncAdoptTitleText.Text = App.GetString("Sync_Conflict_Adopt_Title");
        SyncAdoptMessageText.Text = App.GetString("Sync_Conflict_Adopt_Message");
        SyncAdoptConfirmText.Text = App.GetString("Sync_Conflict_Adopt_Confirm");
        SyncAdoptCancelText.Text = App.GetString("Common_Button_Cancel");
        SyncAdoptDangerIcon.Data = App.CreateGeometry(IconData.Danger);


        SyncKeySavedTitleText.Text = App.GetString("Sync_Key_Saved_Title");
        SyncKeySavedMessageText.Text = App.GetString("Sync_Key_Saved_Message");
        SyncKeySavedConfirmText.Text = App.GetString("Sync_Key_Saved_Confirm");
        SyncKeySavedFootnoteText.Text = App.GetString("Sync_Key_Saved_Footnote");
        SyncKeySavedIcon.Data = App.CreateGeometry(IconData.Lock);


        SyncKeyViewTitleText.Text = App.GetString("Sync_Key_View_Title");
        SyncKeyViewDescText.Text = App.GetString("Sync_Key_View_Desc");
        SyncKeyViewErrorText.Text = App.GetString("Sync_Key_View_Error");
        SyncKeyViewCancelText.Text = App.GetString("Common_Button_Cancel");
        SyncKeyViewConfirmText.Text = App.GetString("Common_Button_Confirm");


        SyncKeyExportLabel.Text = App.GetString("Sync_Label_KeyExport");
        SyncKeyExportButtonText.Text = App.GetString("Sync_KeyBackup_ExportButton");
        SyncKeyImportLabel.Text = App.GetString("Sync_Label_KeyImport");
        SyncKeyImportButtonText.Text = App.GetString("Sync_KeyBackup_ImportButton");
        SyncKeyImportPwdTitleText.Text = App.GetString("Sync_KeyBackup_ImportTitle");
        SyncKeyImportPwdDescText.Text = App.GetString("Sync_KeyBackup_ImportDesc");
        SyncKeyImportPwdErrorText.Text = App.GetString("Sync_KeyBackup_OpenFailed");
        SyncKeyImportPwdCancelText.Text = App.GetString("Common_Button_Cancel");
        SyncKeyImportPwdConfirmText.Text = App.GetString("Common_Button_Confirm");
        SyncKeyImportResultTitleText.Text = App.GetString("Sync_KeyBackup_ResultTitle");
        SyncKeyImportResultDescText.Text = App.GetString("Sync_KeyBackup_ResultDesc");
        SyncKeyImportResultCopyText.Text = App.GetString("Sync_ReadAccess_Copy");
        SyncKeyImportResultCloseButtonText.Text = App.GetString("Common_Button_Close");


        SyncAuditLabel.Text = App.GetString("Sync_Label_Audit");
        SyncAuditButtonText.Text = App.GetString("Sync_Audit_Button");
        SyncAuditTitleText.Text = App.GetString("Sync_Audit_Title");

        SyncReadAccessLabel.Text = App.GetString("Sync_Label_ReadAccess");
        SyncReadAccessButtonText.Text = App.GetString("Sync_ReadAccess_Button");
        SyncReadAccessTitleText.Text = App.GetString("Sync_ReadAccess_Title");
        SyncReadAccessDescText.Text = App.GetString("Sync_ReadAccess_Desc");
        SyncReadAccessTokenLabel.Text = App.GetString("Sync_ReadAccess_TokenLabel");
        SyncReadAccessTokenWarning.Text = App.GetString("Sync_ReadAccess_TokenWarning");
        SyncReadAccessLinkLabel.Text = App.GetString("Sync_ReadAccess_LinkLabel");
        SyncReadAccessTokenCopyText.Text = App.GetString("Sync_ReadAccess_Copy");
        SyncReadAccessLinkCopyText.Text = App.GetString("Sync_ReadAccess_Copy");
        SyncReadAccessRevokeText.Text = App.GetString("Sync_ReadAccess_Revoke");
        SyncReadAccessRevokeConfirmTitleText.Text = App.GetString("Sync_ReadAccess_RevokeTitle");
        SyncReadAccessRevokeConfirmMessageText.Text = App.GetString("Sync_ReadAccess_RevokeMessage");
        SyncReadAccessRevokeConfirmCancelText.Text = App.GetString("Common_Button_Cancel");
        SyncReadAccessRevokeConfirmOkText.Text = App.GetString("Sync_ReadAccess_RevokeOk");
        SyncReadAccessRevokeDangerIcon.Data = App.CreateGeometry(IconData.Danger);
        SyncEditAccessLabel.Text = App.GetString("Sync_Label_EditAccess");
        SyncEditAccessButtonText.Text = App.GetString("Sync_EditAccess_Button");
        SyncEditAccessTitleText.Text = App.GetString("Sync_EditAccess_Title");
        SyncEditAccessDescText.Text = App.GetString("Sync_EditAccess_Desc");
        SyncEditAccessTokenLabel.Text = App.GetString("Sync_EditAccess_TokenLabel");
        SyncEditAccessTokenWarning.Text = App.GetString("Sync_EditAccess_TokenWarning");
        SyncEditAccessTokenCopyText.Text = App.GetString("Sync_ReadAccess_Copy");
        SyncEditAccessRevokeText.Text = App.GetString("Sync_EditAccess_Revoke");
        SyncEditAccessRevokeConfirmTitleText.Text = App.GetString("Sync_EditAccess_RevokeTitle");
        SyncEditAccessRevokeConfirmMessageText.Text = App.GetString("Sync_EditAccess_RevokeMessage");
        SyncEditAccessRevokeConfirmCancelText.Text = App.GetString("Common_Button_Cancel");
        SyncEditAccessRevokeConfirmOkText.Text = App.GetString("Sync_EditAccess_RevokeOk");
        SyncEditAccessRevokeDangerIcon.Data = App.CreateGeometry(IconData.Danger);
        SyncDeviceCenterLabel.Text = App.GetString("Sync_Label_DeviceCenter");
        SyncDeviceCenterButtonText.Text = App.GetString("Sync_DeviceCenter_Button");
        SyncDeviceCenterTitleText.Text = App.GetString("Sync_DeviceCenter_Title");
        SyncDeviceCenterDescText.Text = App.GetString("Sync_DeviceCenter_Desc");
        SyncDeviceCenterRefreshText.Text = App.GetString("Sync_DeviceCenter_Refresh");
        SyncDeviceCenterCloseButtonText.Text = App.GetString("Common_Button_Close");

        SyncConnInfoLabel.Text = App.GetString("Sync_Label_ConnInfo");
        SyncConnInfoButtonText.Text = App.GetString("Sync_ConnInfo_Button");
        SyncConnInfoTitleText.Text = App.GetString("Sync_ConnInfo_Title");
        SyncConnInfoDescText.Text = App.GetString("Sync_ConnInfo_Desc");
        SyncDeviceRevokeConfirmCancelText.Text = App.GetString("Common_Button_Cancel");
        SyncDeviceResetConfirmCancelText.Text = App.GetString("Common_Button_Cancel");
        SyncDeviceRevokeConfirmTitleText.Text = App.GetString("Sync_DeviceCenter_RevokeTitle");
        SyncDeviceResetConfirmTitleText.Text = App.GetString("Sync_DeviceCenter_ResetTitle");
        SyncDeviceRevokeConfirmOkText.Text = App.GetString("Sync_DeviceCenter_RevokeOk");
        SyncDeviceResetConfirmOkText.Text = App.GetString("Sync_DeviceCenter_ResetOk");

        SyncDeviceRevokeConfirmDangerIcon.Data = App.CreateGeometry(IconData.Danger);
        SyncDeviceResetConfirmDangerIcon.Data = App.CreateGeometry(IconData.Danger);
        SyncAuditClearButtonText.Text = App.GetString("Sync_Audit_Clear");
        SyncAuditCloseButtonText.Text = App.GetString("Sync_Audit_Close");
        SyncAuditEmptyText.Text = App.GetString("Sync_Audit_Empty");
        SyncAuditClearConfirmTitleText.Text = App.GetString("Sync_Audit_ClearConfirm_Title");
        SyncAuditClearConfirmMessageText.Text = App.GetString("Sync_Audit_ClearConfirm_Message");
        SyncAuditClearConfirmCancelText.Text = App.GetString("Common_Button_Cancel");
        SyncAuditClearConfirmOkText.Text = App.GetString("Sync_Audit_ClearConfirm_Ok");
        SyncAuditClearDangerIcon.Data = App.CreateGeometry(IconData.Danger);

        SyncPairTitleText.Text = App.GetString("Sync_Pair_Title");
        SyncPairDescText.Text = App.GetString("Sync_Pair_Desc");
        SyncPairServerLabel.Text = App.GetString("Sync_Pair_Server");
        SyncPairSpaceLabel.Text = App.GetString("Sync_Pair_SpaceId");
        SyncPairEnrollmentLabel.Text = App.GetString("Sync_Pair_Enrollment");
        SyncPairKeyLabel.Text = App.GetString("Sync_Pair_SpaceKey");
        SyncPairDeviceLabel.Text = App.GetString("Sync_Pair_DeviceName");
        SyncPairConfirmText.Text = App.GetString("Sync_Pair_Confirm");
        SyncPairDeployHelpText.Text = App.GetString("Connect_DeployHelp");
        SyncPairServerBox.PlaceholderText = "https://novara.example.com";

        SyncUnpairTitleText.Text = App.GetString("Sync_Unpair_Title");
        SyncUnpairMessageText.Text = App.GetString("Sync_Unpair_Message");
        SyncUnpairConfirmText.Text = App.GetString("Sync_Unpair_Confirm");
        SyncUnpairCancelText.Text = App.GetString("Common_Button_Cancel");
        SyncUnpairDangerIcon.Data = App.CreateGeometry(IconData.Danger);

        var state = Services.SyncService.State;
        var paired = IsSyncPaired(state);

        SyncPairButton.Visibility = paired ? Visibility.Collapsed : Visibility.Visible;
        SyncNowButton.Visibility = paired ? Visibility.Visible : Visibility.Collapsed;
        SyncToggleButton.Visibility = paired ? Visibility.Visible : Visibility.Collapsed;
        SyncExpandButton.Visibility = paired ? Visibility.Visible : Visibility.Collapsed;
        SyncToggleText.Text = App.GetString(state.Enabled ? "Setting_Autostart_On" : "Setting_Autostart_Off");
        ApplyToggleState(SyncToggleButton, paired && state.Enabled);


        var detailExpanded = App.Store?.Database.AppSettings.SyncDetailExpanded ?? true;
        SyncExpandIcon.Data = detailExpanded
            ? App.CreateGeometry(IconData.CardCollapse)
            : App.CreateGeometry(IconData.CardExpand);

        if (paired)
        {
            BuildSyncDetail(state);

            if (detailExpanded)
            {
                if (animate) Services.MeltAnim.Begin(SyncDetailPanel, true, 16.0);
                else Services.MeltAnim.SetInstant(SyncDetailPanel, true, 16.0);
            }
            else
            {
                if (animate) Services.MeltAnim.Begin(SyncDetailPanel, false, 16.0);
                else Services.MeltAnim.SetInstant(SyncDetailPanel, false, 16.0);
            }
        }
        else
        {

            if (animate) Services.MeltAnim.Begin(SyncDetailPanel, false, 16.0);
            else Services.MeltAnim.SetInstant(SyncDetailPanel, false, 16.0);
        }

        RefreshSyncStatus();
    }

    private static bool IsSyncPaired(Models.SyncState state)
        => !string.IsNullOrEmpty(state.SpaceId) && !string.IsNullOrEmpty(state.DeviceId);






    private static string SyncOutcomeStatusKey(Services.SyncRoundOutcome? outcome, string fallback)
    {
        if (outcome is null) return fallback;
        return outcome.Status switch
        {
            Services.SyncRoundStatus.Conflict => "Sync_Status_Conflict",
            Services.SyncRoundStatus.RollbackRejected => "Sync_Status_Rollback",
            Services.SyncRoundStatus.TransportFailure => "Sync_Status_Offline",
            Services.SyncRoundStatus.Error when outcome.ProtocolError == Models.SyncErrorCode.RateLimited
                => "Sync_Status_RateLimited",
            Services.SyncRoundStatus.Error when outcome.ProtocolError is Models.SyncErrorCode.Unauthenticated
                    or Models.SyncErrorCode.Forbidden
                => "Sync_Status_Credentials",
            Services.SyncRoundStatus.Error when outcome.ProtocolError == Models.SyncErrorCode.ServerError
                => "Sync_Status_ServerError",
            Services.SyncRoundStatus.Error => "Sync_Status_Error",

            _ => fallback,
        };
    }





    private void RefreshSyncStatus()
    {
        var state = Services.SyncService.State;

        string key;
        if (!IsSyncPaired(state)) key = "Sync_Status_Unpaired";



        else if (Services.SyncService.KeyWrapStale) key = "Sync_Status_KeyWrapStale";



        else if (Services.SyncService.CredentialUnavailable) key = "Sync_Status_CredentialUnavailable";
        else if (Services.SyncService.IsSyncing) key = "Sync_Status_Syncing";
        else key = SyncOutcomeStatusKey(_lastSyncOutcome, fallback: state.Enabled ? "Sync_Status_Ready" : "Sync_Status_PairedOff");
        SyncDetailState.Text = App.GetString(key);

        var attention = IsSyncPaired(state) && (Services.SyncService.KeyWrapStale
            || Services.SyncService.CredentialUnavailable
            || _lastSyncOutcome?.Status is
            Services.SyncRoundStatus.Conflict or
            Services.SyncRoundStatus.RollbackRejected or
            Services.SyncRoundStatus.TransportFailure or
            Services.SyncRoundStatus.Error);



        var resolvable = IsSyncPaired(state) && _lastSyncOutcome?.Status == Services.SyncRoundStatus.Conflict;
        SyncNowButtonText.Text = App.GetString(resolvable ? "Sync_Button_Resolve" : "Sync_Button_SyncNow");

        SyncAttentionDot.Visibility = attention ? Visibility.Visible : Visibility.Collapsed;
    }

    private void BuildSyncDetail(Models.SyncState state)
    {
        var version = string.Format(App.GetString("Sync_Status_Version"), Math.Max(state.BaseVersion, state.LastSeenVersion));
        SyncDetailServer.Text = state.ServerUrl;

        SyncDetailSpace.Text = string.IsNullOrEmpty(state.SpaceId) ? "" : ShortId(state.SpaceId) + " · " + version;
        SyncDetailDevice.Text = state.DeviceName;
        SyncDetailLastSync.Text = state.LastSyncAt is { } at
            ? at.ToLocalTime().ToString("yyyy-MM-dd HH:mm")
            : App.GetString("Sync_Label_Never");
        UpdateSyncFreqText();
        RefreshSyncKeyRow();
    }






    private void RefreshSyncKeyRow()
    {
        var hasWrap = !string.IsNullOrEmpty(Services.SyncService.State.KeyWrapJson);
        SyncDetailKey.Text = _syncKeyShown && _revealedSpaceKey is { } revealed
            ? revealed
            : new string('•', 12);
        SyncKeyViewButton.Visibility = hasWrap ? Visibility.Visible : Visibility.Collapsed;
    }

    private static string ShortId(string id) => id.Length <= 8 ? id : id.Substring(0, 8);

    private void UpdateSyncFreqText()
        => SyncFreqText.Text = App.GetString(Services.SyncService.State.FrequencyMinutes switch
        {
            Models.SyncState.FrequencyEverySave => "Sync_Freq_EverySave",
            5 => "Sync_Freq_Every5",
            15 => "Sync_Freq_Every15",
            _ => "Sync_Freq_Manual",
        });


    private void SyncExpandButton_Click(object sender, RoutedEventArgs e)
    {
        var settings = App.Store?.Database.AppSettings;
        if (settings == null) return;
        settings.SyncDetailExpanded = !settings.SyncDetailExpanded;
        App.Store?.SaveAsync();
        UpdateSyncUI(animate: true);
    }

    private async void SyncNowButton_Click(object sender, RoutedEventArgs e)
    {


        if (IsSyncResolvable())
        {
            await ShowSyncConflictDialogAsync();
            return;
        }

        if (Services.SyncService.IsSyncing) return;
        SyncNowButton.IsEnabled = false;
        try
        {
            var outcome = await Services.SyncService.SyncNowAsync();
            _lastSyncOutcome = outcome;
            UpdateSyncUI();
            ShowSyncOutcomeToast(outcome);



            if (outcome.Status == Services.SyncRoundStatus.Conflict) await ShowSyncConflictDialogAsync();
        }
        catch (Exception ex)
        {




            _lastSyncOutcome = new Services.SyncRoundOutcome(Services.SyncRoundStatus.Error, ex.Message);
            UpdateSyncUI();
        }
        finally { SyncNowButton.IsEnabled = true; }
    }

    private bool IsSyncResolvable()
        => IsSyncPaired(Services.SyncService.State) &&
           _lastSyncOutcome?.Status == Services.SyncRoundStatus.Conflict;






    private void SyncToggleButton_Click(object sender, RoutedEventArgs e)
    {
        var menu = new MenuFlyout { MenuFlyoutPresenterStyle = (Style)Application.Current.Resources["GlassMenuFlyoutPresenterStyle"] };
        var accentBrush = App.GetBrush("AppTextPrimaryBrush");
        var normalBrush = App.GetBrush("AppTextSecondaryBrush");
        var dangerBrush = App.GetBrush("AppDangerTextBrush");
        var enabled = Services.SyncService.State.Enabled;

        MenuFlyoutItem MakeItem(string text, bool isCurrent, Action action, bool danger = false)
        {
            var item = new MenuFlyoutItem
            {
                Style = (Style)Application.Current.Resources["GlassMenuFlyoutItemStyle"],
                Text = text,
                Foreground = danger ? dangerBrush : isCurrent ? accentBrush : normalBrush,
            };
            if (isCurrent) item.Icon = new FontIcon { Glyph = "\uE73E", FontSize = 12, Foreground = accentBrush };
            item.Click += (_, _) => action();
            return item;
        }

        menu.Items.Add(MakeItem(App.GetString("Setting_Autostart_On"), enabled,
            () => { Services.SyncService.SetEnabled(true); UpdateSyncUI(animate: true); }));
        menu.Items.Add(MakeItem(App.GetString("Setting_Autostart_Off"), !enabled,
            () => { Services.SyncService.SetEnabled(false); UpdateSyncUI(animate: true); }));
        menu.Items.Add(new MenuFlyoutSeparator());
        menu.Items.Add(MakeItem(App.GetString("Sync_Button_Unpair"), false, ShowSyncUnpairDialog, danger: true));
        menu.ShowAt(SyncToggleButton, new Windows.Foundation.Point(0, SyncToggleButton.ActualHeight + 4));
    }

    private void SyncFreqButton_Click(object sender, RoutedEventArgs e)
    {
        var menu = new MenuFlyout { MenuFlyoutPresenterStyle = (Style)Application.Current.Resources["GlassMenuFlyoutPresenterStyle"] };
        var accentBrush = App.GetBrush("AppTextPrimaryBrush");
        var normalBrush = App.GetBrush("AppTextSecondaryBrush");
        var current = Services.SyncService.State.FrequencyMinutes;

        MenuFlyoutItem MakeItem(string text, int minutes)
        {
            var item = new MenuFlyoutItem
            {
                Style = (Style)Application.Current.Resources["GlassMenuFlyoutItemStyle"],
                Text = text,
                Foreground = minutes == current ? accentBrush : normalBrush,
            };
            if (minutes == current) item.Icon = new FontIcon { Glyph = "\uE73E", FontSize = 12, Foreground = accentBrush };
            item.Click += (_, _) => { Services.SyncService.SetFrequency(minutes); UpdateSyncUI(); };
            return item;
        }

        menu.Items.Add(MakeItem(App.GetString("Sync_Freq_Manual"), Models.SyncState.FrequencyManual));

        menu.Items.Add(MakeItem(App.GetString("Sync_Freq_EverySave"), Models.SyncState.FrequencyEverySave));
        menu.Items.Add(MakeItem(App.GetString("Sync_Freq_Every5"), 5));
        menu.Items.Add(MakeItem(App.GetString("Sync_Freq_Every15"), 15));
        menu.ShowAt(SyncFreqButton, new Windows.Foundation.Point(0, SyncFreqButton.ActualHeight + 4));
    }


    private static void ShowSyncOutcomeToast(Services.SyncRoundOutcome outcome)
    {
        var key = outcome.Status switch
        {
            Services.SyncRoundStatus.Push => "Sync_Toast_Pushed",
            Services.SyncRoundStatus.Pull => "Sync_Toast_Pulled",
            Services.SyncRoundStatus.AutoRebased => "Sync_Toast_AutoRebased",
            Services.SyncRoundStatus.NoOp => "Sync_Toast_Done",
            Services.SyncRoundStatus.Conflict => "Sync_Toast_Conflict",
            Services.SyncRoundStatus.RollbackRejected => "Sync_Toast_Rollback",
            Services.SyncRoundStatus.TransportFailure => "Sync_Status_Offline",

            Services.SyncRoundStatus.Error => SyncOutcomeStatusKey(outcome, fallback: "Sync_Status_Error"),
            _ => "",
        };
        if (key.Length > 0) App.ShowToast(App.GetString(key));
    }



    private void SyncPairButton_Click(object sender, RoutedEventArgs e)
    {
        _animSyncPair = false;
        SyncPairErrorBlock.Visibility = Visibility.Collapsed;
        SyncPairServerBox.Text = Services.SyncService.State.ServerUrl;
        SyncPairSpaceBox.Text = "";
        SyncPairEnrollmentBox.Text = "";
        SyncPairKeyBox.Text = "";
        SyncPairDeviceBox.Text = Environment.MachineName;
        UpdateSyncPairConfirmState();
        ShowOverlay(SyncPairOverlay, SyncPairDialog, SyncPairDialogTransform);
        SyncPairServerBox.Focus(FocusState.Programmatic);
    }







    private void UpdateSyncPairConfirmState()
        => SyncPairConfirmButton.IsEnabled =
            SyncPairServerBox.Text.Trim().Length > 0 &&
            SyncPairSpaceBox.Text.Trim().Length > 0 &&
            SyncPairEnrollmentBox.Text.Trim().Length > 0;

    private void SyncPairField_TextChanged(object sender, TextChangedEventArgs e) => UpdateSyncPairConfirmState();

    private void HideSyncPairDialog()
    {
        if (_animSyncPair) return;
        _animSyncPair = true;
        HideOverlay(SyncPairOverlay, SyncPairDialog, SyncPairDialogTransform, () => _animSyncPair = false);
    }




    private void SyncPairDeployHelp_Click(object sender, RoutedEventArgs e)
    {


        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(SyncGuideUrl) { UseShellExecute = true }); }
        catch { App.ShowToast(App.GetString("Connect_DeployHelp_Fail"), ToastTone.Error); }
    }

    private void SyncPairClose_Click(object sender, RoutedEventArgs e) => HideSyncPairDialog();
    private void SyncPairScrim_Tapped(object sender, TappedRoutedEventArgs e)
    { if (ReferenceEquals(e.OriginalSource, SyncPairScrim)) HideSyncPairDialog(); }

    private async void SyncPairConfirm_Click(object sender, RoutedEventArgs e)
    {
        SyncPairConfirmButton.IsEnabled = false;
        try
        {
            var deviceName = string.IsNullOrWhiteSpace(SyncPairDeviceBox.Text)
                ? Environment.MachineName
                : SyncPairDeviceBox.Text.Trim();

            var result = await Services.SyncService.PairAsync(
                SyncPairServerBox.Text.Trim(),
                SyncPairSpaceBox.Text.Trim(),
                SyncPairEnrollmentBox.Text.Trim(),
                SyncPairKeyBox.Text.Trim(),
                deviceName);

            if (!result.Ok)
            {



                SyncPairErrorText.Text = result.ReasonKey.Length > 0
                    ? App.GetString(result.ReasonKey)
                    : App.GetString("Sync_Pair_Failed") + "：" + result.ServerMessage;
                SyncPairErrorHintText.Text = App.GetString("Sync_Pair_RetryHint");
                SyncPairErrorBlock.Visibility = Visibility.Visible;
                return;
            }

            HideSyncPairDialog();
            UpdateSyncUI(animate: true);

            ShowSyncKeySavedDialog(result.SpaceKeyBase64);
        }

        finally { UpdateSyncPairConfirmState(); }
    }



    private void SyncUnpairButton_Click(object sender, RoutedEventArgs e) => ShowSyncUnpairDialog();


    private void ShowSyncUnpairDialog()
    {
        _animSyncUnpair = false;
        ShowOverlay(SyncUnpairOverlay, SyncUnpairDialog, SyncUnpairDialogTransform);
    }

    private void HideSyncUnpairDialog()
    {
        if (_animSyncUnpair) return;
        _animSyncUnpair = true;
        HideOverlay(SyncUnpairOverlay, SyncUnpairDialog, SyncUnpairDialogTransform, () => _animSyncUnpair = false);
    }

    private void SyncUnpairCancel_Click(object sender, RoutedEventArgs e) => HideSyncUnpairDialog();
    private void SyncUnpairClose_Click(object sender, RoutedEventArgs e) => HideSyncUnpairDialog();
    private void SyncUnpairScrim_Tapped(object sender, TappedRoutedEventArgs e)
    { if (ReferenceEquals(e.OriginalSource, SyncUnpairScrim)) HideSyncUnpairDialog(); }

    private void SyncUnpairConfirm_Click(object sender, RoutedEventArgs e)
    {
        Services.SyncService.Unpair();
        _lastSyncOutcome = null;
        _revealedSpaceKey = null;
        _syncKeyShown = false;
        HideSyncUnpairDialog();
        UpdateSyncUI(animate: true);
    }









    private async Task ShowSyncConflictDialogAsync()
    {
        if (_syncConflictBusy) return;
        _syncConflictBusy = true;
        try
        {
            _animSyncConflict = false;
            _syncConflict = null;
            SyncDiffRows.Children.Clear();
            SyncConflictTable.Visibility = Visibility.Collapsed;
            SyncConflictOnlySettingsText.Visibility = Visibility.Collapsed;
            SyncConflictLoadingText.Visibility = Visibility.Visible;
            SyncExportRemoteButton.IsEnabled = false;
            SyncExportLocalButton.IsEnabled = false;
            ShowOverlay(SyncConflictOverlay, SyncConflictDialog, SyncConflictDialogTransform);

            var inspection = await Services.SyncService.InspectConflictAsync();
            _syncConflict = inspection;

            if (!inspection.Success)
            {
                HideSyncConflictDialog();
                App.ShowToast(App.GetString("Sync_Conflict_InspectFailed"), ToastTone.Error);
                return;
            }

            BuildConflictDiff(inspection.Diff);
            SyncConflictLoadingText.Visibility = Visibility.Collapsed;
            SyncConflictTable.Visibility = Visibility.Visible;
            SyncExportRemoteButton.IsEnabled = true;
            SyncExportLocalButton.IsEnabled = true;
        }
        finally { _syncConflictBusy = false; }
    }


    private static string PartitionKey(Services.SyncPartition partition) => partition switch
    {
        Services.SyncPartition.Memo => "Export_Section_Memo",
        Services.SyncPartition.Diary => "Export_Section_Diary",
        Services.SyncPartition.Todo => "Export_Section_Todo",
        Services.SyncPartition.Note => "Export_Section_Note",
        Services.SyncPartition.FilePath => "Export_Section_Path",
        _ => "Setting_Stats_Groups",
    };





    private void BuildConflictDiff(Services.SyncDiffSummary? diff)
    {
        SyncDiffRows.Children.Clear();
        if (diff is null) return;

        var strong = App.GetBrush("AppTextPrimaryBrush");
        var muted = App.GetBrush("AppTextTertiaryBrush");

        foreach (var partition in diff.Dirty)
        {
            var row = new Grid { ColumnSpacing = 8 };
            row.ColumnDefinitions.Add(new ColumnDefinition());
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(86) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(86) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(86) });

            var label = new TextBlock
            {
                Text = App.GetString(PartitionKey(partition.Partition)),
                FontSize = 14,
                Foreground = strong,
                VerticalAlignment = VerticalAlignment.Center,
            };
            Grid.SetColumn(label, 0);
            row.Children.Add(label);

            AddDiffCount(row, 1, partition.OnlyLocal, strong, muted);
            AddDiffCount(row, 2, partition.OnlyRemote, strong, muted);
            AddDiffCount(row, 3, partition.BothChanged, strong, muted);
            SyncDiffRows.Children.Add(row);
        }


        SyncConflictOnlySettingsText.Visibility =
            diff.Dirty.Count == 0 && diff.RoamingSettingsDiffer ? Visibility.Visible : Visibility.Collapsed;
    }

    private static void AddDiffCount(Grid row, int column, int value, Brush strong, Brush muted)
    {
        var text = new TextBlock
        {
            Text = value.ToString(),
            FontSize = 14,
            Foreground = value == 0 ? muted : strong,
            TextAlignment = TextAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
        };
        Grid.SetColumn(text, column);
        row.Children.Add(text);
    }

    private void HideSyncConflictDialog()
    {
        if (_animSyncConflict) return;
        _animSyncConflict = true;
        HideOverlay(SyncConflictOverlay, SyncConflictDialog, SyncConflictDialogTransform, () => _animSyncConflict = false);
    }

    private void SyncConflictClose_Click(object sender, RoutedEventArgs e) => HideSyncConflictDialog();
    private void SyncConflictScrim_Tapped(object sender, TappedRoutedEventArgs e)
    { if (ReferenceEquals(e.OriginalSource, SyncConflictScrim)) HideSyncConflictDialog(); }



    private async void SyncExportRemote_Click(object sender, RoutedEventArgs e)
    {
        if (_syncConflict is { Success: true } inspection)
            await ExportConflictSideAsync(remote: true, inspection.RemoteContainer, inspection.RemoteVersion);
    }

    private async void SyncExportLocal_Click(object sender, RoutedEventArgs e)
    {
        if (_syncConflict is { Success: true } inspection)
            await ExportConflictSideAsync(remote: false, Array.Empty<byte>(), inspection.LocalVersion);
    }





    private async Task ExportConflictSideAsync(bool remote, byte[] remoteContainer, long version)
    {
        if (_pickerFlowBusy) return;
        _pickerFlowBusy = true;
        try
        {
            var suggested = Services.SyncConflictResolver.SuggestFileName(remote, version, DateTime.Now);
            var path = FilePicker.PickSaveFile(System.IO.Path.GetFileNameWithoutExtension(suggested), new[] { ("Novara", new[] { ".novaenc" }) });
            if (path == null) return;

            var outcome = remote
                ? Services.SyncConflictResolver.ExportRemoteVersion(path, remoteContainer)
                : Services.SyncService.ExportLocalVersion(path);

            if (outcome.Success) Services.SyncService.RecordExport(remote, version);
            App.ShowToast(App.GetString(outcome.Success ? "Sync_Conflict_Exported" : "Sync_Conflict_ExportFailed"), outcome.Success ? ToastTone.Success : ToastTone.Error);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"导出同步版本失败: {ex.Message}");
            App.ShowToast(App.GetString("Sync_Conflict_ExportFailed"), ToastTone.Error);
        }
        finally { _pickerFlowBusy = false; }
    }




    private async void SyncKeepLocal_Click(object sender, RoutedEventArgs e)
    {
        if (_syncConflictBusy) return;
        _syncConflictBusy = true;
        SyncKeepLocalButton.IsEnabled = false;
        try
        {
            HideSyncConflictDialog();
            var outcome = await Services.SyncService.ResolveKeepLocalAsync();
            _lastSyncOutcome = outcome;
            UpdateSyncUI();
            App.ShowToast(App.GetString(outcome.Ok
                ? "Sync_Conflict_Resolved_KeptLocal"
                : SyncOutcomeStatusKey(outcome, fallback: "Sync_Status_Error")), outcome.Ok ? ToastTone.Success : ToastTone.Error);
        }
        catch (Exception ex)
        {
            _lastSyncOutcome = new Services.SyncRoundOutcome(Services.SyncRoundStatus.Error, ex.Message);
            UpdateSyncUI();
        }
        finally { SyncKeepLocalButton.IsEnabled = true; _syncConflictBusy = false; }
    }


    private void SyncTakeRemote_Click(object sender, RoutedEventArgs e)
    {
        _animSyncAdopt = false;
        ShowOverlay(SyncAdoptOverlay, SyncAdoptDialog, SyncAdoptDialogTransform);
    }

    private void HideSyncAdoptDialog()
    {
        if (_animSyncAdopt) return;
        _animSyncAdopt = true;
        HideOverlay(SyncAdoptOverlay, SyncAdoptDialog, SyncAdoptDialogTransform, () => _animSyncAdopt = false);
    }

    private void SyncAdoptClose_Click(object sender, RoutedEventArgs e) => HideSyncAdoptDialog();
    private void SyncAdoptCancel_Click(object sender, RoutedEventArgs e) => HideSyncAdoptDialog();
    private void SyncAdoptScrim_Tapped(object sender, TappedRoutedEventArgs e)
    { if (ReferenceEquals(e.OriginalSource, SyncAdoptScrim)) HideSyncAdoptDialog(); }

    private async void SyncAdoptConfirm_Click(object sender, RoutedEventArgs e)
    {
        if (_syncConflictBusy) return;
        _syncConflictBusy = true;
        SyncAdoptConfirmButton.IsEnabled = false;
        try
        {
            HideSyncAdoptDialog();
            HideSyncConflictDialog();

            var outcome = await Services.SyncService.ResolveTakeRemoteAsync();
            _lastSyncOutcome = outcome;

            if (outcome.Changed)
            {



                App.ShowToast(App.GetString("Sync_Conflict_Resolved_TookRemote"));
                return;
            }

            UpdateSyncUI();
            App.ShowToast(App.GetString(outcome.Status == Services.SyncRoundStatus.RollbackRejected
                ? "Sync_Toast_Rollback"
                : SyncOutcomeStatusKey(outcome, fallback: "Sync_Status_Error")), outcome.Status == Services.SyncRoundStatus.RollbackRejected ? ToastTone.Success : ToastTone.Error);
        }
        catch (Exception ex)
        {
            _lastSyncOutcome = new Services.SyncRoundOutcome(Services.SyncRoundStatus.Error, ex.Message);
            UpdateSyncUI();
        }
        finally { SyncAdoptConfirmButton.IsEnabled = true; _syncConflictBusy = false; }
    }



    private void ShowSyncKeySavedDialog(string spaceKeyBase64)
    {
        _animSyncKeySaved = false;
        SyncKeySavedBox.Text = spaceKeyBase64;
        ShowOverlay(SyncKeySavedOverlay, SyncKeySavedDialog, SyncKeySavedDialogTransform);
    }

    private void HideSyncKeySavedDialog()
    {
        if (_animSyncKeySaved) return;
        _animSyncKeySaved = true;
        HideOverlay(SyncKeySavedOverlay, SyncKeySavedDialog, SyncKeySavedDialogTransform, () => _animSyncKeySaved = false);
    }


    private void SyncKeySavedConfirm_Click(object sender, RoutedEventArgs e)
    {
        CopyToClipboard(SyncKeySavedBox.Text);
        HideSyncKeySavedDialog();
    }

    private void SyncKeySavedScrim_Tapped(object sender, TappedRoutedEventArgs e) => HideSyncKeySavedDialog();



    private void SyncKeyViewButton_Click(object sender, RoutedEventArgs e)
    {


        if (_revealedSpaceKey is not null)
        {
            _syncKeyShown = !_syncKeyShown;
            RefreshSyncKeyRow();
            return;
        }

        _animSyncKeyView = false;
        SyncKeyViewPasswordBox.Text = "";
        SyncKeyViewErrorText.Visibility = Visibility.Collapsed;
        ShowOverlay(SyncKeyViewOverlay, SyncKeyViewDialog, SyncKeyViewDialogTransform);
        SyncKeyViewPasswordBox.Focus(FocusState.Programmatic);
    }

    private void HideSyncKeyViewDialog()
    {
        if (_animSyncKeyView) return;
        _animSyncKeyView = true;
        HideOverlay(SyncKeyViewOverlay, SyncKeyViewDialog, SyncKeyViewDialogTransform, () => _animSyncKeyView = false);
    }

    private void SyncKeyViewClose_Click(object sender, RoutedEventArgs e) => HideSyncKeyViewDialog();
    private void SyncKeyViewCancel_Click(object sender, RoutedEventArgs e) => HideSyncKeyViewDialog();
    private void SyncKeyViewScrim_Tapped(object sender, TappedRoutedEventArgs e)
    { if (ReferenceEquals(e.OriginalSource, SyncKeyViewScrim)) HideSyncKeyViewDialog(); }

    private async void SyncKeyViewConfirm_Click(object sender, RoutedEventArgs e)
    {
        SyncKeyViewConfirmButton.IsEnabled = false;
        try
        {
            var password = SyncKeyViewPasswordBox.Text;

            var revealed = await Task.Run(() => Services.SyncService.RevealSpaceKey(password));
            if (revealed is null)
            {

                SyncKeyViewErrorText.Visibility = Visibility.Visible;
                FlashTextBox(SyncKeyViewPasswordBox);
                return;
            }

            _revealedSpaceKey = revealed;
            _syncKeyShown = true;
            HideSyncKeyViewDialog();
            RefreshSyncKeyRow();
            Services.SyncService.RecordKeyViewed();
        }
        finally { SyncKeyViewConfirmButton.IsEnabled = true; }
    }




    private async void SyncKeyExportButton_Click(object sender, RoutedEventArgs e)
    {
        if (_pickerFlowBusy) return;
        _pickerFlowBusy = true;
        try
        {
            var path = FilePicker.PickSaveFile(SuggestKeyBackupName(), new[] { ("Novara", new[] { ".novakey" }) });
            if (path == null) return;

            var outcome = Services.SyncService.ExportSpaceKeyFile(path);
            App.ShowToast(App.GetString(outcome.Ok ? "Sync_KeyBackup_Exported" : outcome.Message), outcome.Ok ? ToastTone.Success : ToastTone.Error);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"导出空间密钥失败: {ex.Message}");
            App.ShowToast(App.GetString("Sync_KeyBackup_WriteFailed"), ToastTone.Error);
        }
        finally { _pickerFlowBusy = false; }
    }


    private static string SuggestKeyBackupName()
    {
        var space = Services.SyncService.State.SpaceId;
        var tag = string.IsNullOrEmpty(space) ? "" : "-" + (space.Length > 8 ? space[..8] : space);
        return $"Novara-space-key{tag}-{DateTime.Now:yyyyMMdd}";
    }

    private void SyncKeyImportButton_Click(object sender, RoutedEventArgs e)
    {
        if (_pickerFlowBusy) return;
        _pickerFlowBusy = true;
        try
        {
            var path = FilePicker.PickFile("*.novakey");
            if (path == null) return;

            _pendingImportPath = path;
            _animSyncKeyImportPwd = false;
            SyncKeyImportPwdBox.Text = "";
            SyncKeyImportPwdErrorText.Visibility = Visibility.Collapsed;
            ShowOverlay(SyncKeyImportPwdOverlay, SyncKeyImportPwdDialog, SyncKeyImportPwdDialogTransform);
            SyncKeyImportPwdBox.Focus(FocusState.Programmatic);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"选择空间密钥备份失败: {ex.Message}");
            App.ShowToast(App.GetString("Sync_KeyBackup_ReadFailed"), ToastTone.Error);
        }
        finally { _pickerFlowBusy = false; }
    }

    private void HideSyncKeyImportPwdDialog()
    {
        if (_animSyncKeyImportPwd) return;
        _animSyncKeyImportPwd = true;
        SyncKeyImportPwdBox.Text = "";
        _pendingImportPath = null;
        HideOverlay(SyncKeyImportPwdOverlay, SyncKeyImportPwdDialog, SyncKeyImportPwdDialogTransform, () => _animSyncKeyImportPwd = false);
    }

    private void SyncKeyImportPwdClose_Click(object sender, RoutedEventArgs e) => HideSyncKeyImportPwdDialog();
    private void SyncKeyImportPwdCancel_Click(object sender, RoutedEventArgs e) => HideSyncKeyImportPwdDialog();
    private void SyncKeyImportPwdScrim_Tapped(object sender, TappedRoutedEventArgs e)
    { if (ReferenceEquals(e.OriginalSource, SyncKeyImportPwdScrim)) HideSyncKeyImportPwdDialog(); }

    private async void SyncKeyImportPwdConfirm_Click(object sender, RoutedEventArgs e)
    {
        var path = _pendingImportPath;
        if (string.IsNullOrEmpty(path)) return;

        SyncKeyImportPwdConfirmButton.IsEnabled = false;
        try
        {
            var password = SyncKeyImportPwdBox.Text;

            var outcome = await Task.Run(() => Services.SyncService.ImportSpaceKeyFile(path, password));
            if (!outcome.Ok)
            {

                SyncKeyImportPwdErrorText.Text = App.GetString(outcome.Message);
                SyncKeyImportPwdErrorText.Visibility = Visibility.Visible;
                FlashTextBox(SyncKeyImportPwdBox);
                return;
            }

            HideSyncKeyImportPwdDialog();
            _importedSpaceKey = outcome.SpaceKeyBase64;
            SyncKeyImportResultBox.Text = outcome.SpaceKeyBase64;
            _animSyncKeyImportResult = false;
            ShowOverlay(SyncKeyImportResultOverlay, SyncKeyImportResultDialog, SyncKeyImportResultDialogTransform);
        }
        finally { SyncKeyImportPwdConfirmButton.IsEnabled = true; }
    }

    private void HideSyncKeyImportResultDialog()
    {
        if (_animSyncKeyImportResult) return;
        _animSyncKeyImportResult = true;
        _importedSpaceKey = null;
        SyncKeyImportResultBox.Text = "";
        HideOverlay(SyncKeyImportResultOverlay, SyncKeyImportResultDialog, SyncKeyImportResultDialogTransform, () => _animSyncKeyImportResult = false);
    }

    private void SyncKeyImportResultClose_Click(object sender, RoutedEventArgs e) => HideSyncKeyImportResultDialog();
    private void SyncKeyImportResultScrim_Tapped(object sender, TappedRoutedEventArgs e)
    { if (ReferenceEquals(e.OriginalSource, SyncKeyImportResultScrim)) HideSyncKeyImportResultDialog(); }
    private void SyncKeyImportResultCopy_Click(object sender, RoutedEventArgs e)
    { if (_importedSpaceKey is { Length: > 0 } key) CopyToClipboard(key); }





    private void SyncAuditButton_Click(object sender, RoutedEventArgs e)
    {
        BuildSyncAuditList();
        ShowSyncAuditDialog();
    }

    private void BuildSyncAuditList()
    {
        SyncAuditList.Children.Clear();
        var events = Services.SyncAuditLog.ReadLatest(200);
        if (events.Count == 0)
        {
            SyncAuditEmptyText.Text = App.GetString("Sync_Audit_Empty");
            SyncAuditEmptyText.Visibility = Visibility.Visible;
            return;
        }

        SyncAuditEmptyText.Visibility = Visibility.Collapsed;
        foreach (var evt in events) SyncAuditList.Children.Add(BuildSyncAuditRow(evt));
    }





    private static FrameworkElement BuildSyncAuditRow(Services.SyncAuditEvent evt)
    {

        var row = new Grid { Margin = new Thickness(0, 0, 14, 10) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var name = new TextBlock
        {
            Text = EventLabel(evt.Ev),
            FontSize = 13,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Foreground = App.GetBrush("AppPrimaryButtonBrush"),
            VerticalAlignment = VerticalAlignment.Top,
            MaxWidth = 120,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Margin = new Thickness(0, 0, 12, 0),
        };
        Grid.SetColumn(name, 0);
        row.Children.Add(name);

        var info = new StackPanel { Spacing = 3, VerticalAlignment = VerticalAlignment.Center };
        var line1 = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        line1.Children.Add(new TextBlock
        {
            Text = evt.Ts, FontSize = 11,
            Foreground = App.GetBrush("AppTextSecondaryBrush"), VerticalAlignment = VerticalAlignment.Center,
        });
        if (evt.Ver > 0)
            line1.Children.Add(new TextBlock
            {
                Text = "v" + evt.Ver, FontSize = 11,
                Foreground = App.GetBrush("AppTextSecondaryBrush"), VerticalAlignment = VerticalAlignment.Center,
            });
        info.Children.Add(line1);

        var desc = DescribeEvent(evt);
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


    private static string EventLabel(string ev)
    {
        var key = ev switch
        {
            Services.SyncAuditEvents.Pair => "Sync_Audit_Ev_Pair",
            Services.SyncAuditEvents.Unpair => "Sync_Audit_Ev_Unpair",
            Services.SyncAuditEvents.Push => "Sync_Audit_Ev_Push",
            Services.SyncAuditEvents.Pull => "Sync_Audit_Ev_Pull",
            Services.SyncAuditEvents.Conflict => "Sync_Audit_Ev_Conflict",
            Services.SyncAuditEvents.KeepLocal => "Sync_Audit_Ev_KeepLocal",
            Services.SyncAuditEvents.TakeRemote => "Sync_Audit_Ev_TakeRemote",
            Services.SyncAuditEvents.Export => "Sync_Audit_Ev_Export",
            Services.SyncAuditEvents.KeyView => "Sync_Audit_Ev_KeyView",
            Services.SyncAuditEvents.ReadTokenCreate => "Sync_Audit_Ev_ReadTokenCreate",
            Services.SyncAuditEvents.ReadTokenRevoke => "Sync_Audit_Ev_ReadTokenRevoke",
            Services.SyncAuditEvents.EditorDeviceCreate => "Sync_Audit_Ev_EditorDeviceCreate",
            Services.SyncAuditEvents.EditorDeviceRevoke => "Sync_Audit_Ev_EditorDeviceRevoke",
            Services.SyncAuditEvents.DeviceRevoke => "Sync_Audit_Ev_DeviceRevoke",
            Services.SyncAuditEvents.DeviceReset => "Sync_Audit_Ev_DeviceReset",
            Services.SyncAuditEvents.KeyExport => "Sync_Audit_Ev_KeyExport",
            Services.SyncAuditEvents.KeyImport => "Sync_Audit_Ev_KeyImport",
            Services.SyncAuditEvents.Rollback => "Sync_Audit_Ev_Rollback",
            Services.SyncAuditEvents.Error => "Sync_Audit_Ev_Error",
            _ => "",
        };
        if (key.Length == 0) return ev;
        var label = App.GetString(key);
        return string.IsNullOrEmpty(label) || label == key ? ev : label;
    }





    private static string DescribeEvent(Services.SyncAuditEvent evt)
    {
        if (evt.Ev == Services.SyncAuditEvents.Export)
        {
            var side = App.GetString(evt.Detail == "remote" ? "Sync_Conflict_ExportRemote" : "Sync_Conflict_ExportLocal");
            return evt.Device.Length > 0 ? $"{evt.Device} · {side}" : side;
        }

        var parts = new List<string>();
        if (evt.Device.Length > 0) parts.Add(evt.Device);
        if (evt.Server.Length > 0) parts.Add(evt.Server);
        if (evt.Detail.Length > 0) parts.Add(evt.Detail);
        return string.Join(" · ", parts);
    }

    private void ShowSyncAuditDialog()
    {
        _animSyncAudit = false;
        ShowOverlay(SyncAuditOverlay, SyncAuditDialog, SyncAuditDialogTransform);
        FitDialogScroll(SyncAuditScroll, 148);
    }

    private void HideSyncAuditDialog()
    {
        if (_animSyncAudit) return;
        _animSyncAudit = true;
        HideOverlay(SyncAuditOverlay, SyncAuditDialog, SyncAuditDialogTransform, () => _animSyncAudit = false);
    }

    private void SyncAuditClose_Click(object sender, RoutedEventArgs e) => HideSyncAuditDialog();
    private void SyncAuditCloseBottom_Click(object sender, RoutedEventArgs e) => HideSyncAuditDialog();
    private void SyncAuditScrim_Tapped(object sender, TappedRoutedEventArgs e)
    { if (ReferenceEquals(e.OriginalSource, SyncAuditScrim)) HideSyncAuditDialog(); }

    private void SyncAuditClear_Click(object sender, RoutedEventArgs e)
    {
        _animSyncAuditClear = false;
        ShowOverlay(SyncAuditClearConfirmOverlay, SyncAuditClearConfirmDialog, SyncAuditClearConfirmDialogTransform);
    }

    private void HideSyncAuditClearConfirmDialog()
    {
        if (_animSyncAuditClear) return;
        _animSyncAuditClear = true;
        HideOverlay(SyncAuditClearConfirmOverlay, SyncAuditClearConfirmDialog, SyncAuditClearConfirmDialogTransform, () => _animSyncAuditClear = false);
    }

    private void SyncAuditClearConfirmCancel_Click(object sender, RoutedEventArgs e) => HideSyncAuditClearConfirmDialog();
    private void SyncAuditClearConfirmClose_Click(object sender, RoutedEventArgs e) => HideSyncAuditClearConfirmDialog();
    private void SyncAuditClearConfirmScrim_Tapped(object sender, TappedRoutedEventArgs e)
    { if (ReferenceEquals(e.OriginalSource, SyncAuditClearConfirmScrim)) HideSyncAuditClearConfirmDialog(); }

    private void SyncAuditClearConfirmOk_Click(object sender, RoutedEventArgs e)
    {


        Services.SyncAuditLog.ClearAllWithMarker();
        BuildSyncAuditList();
        HideSyncAuditClearConfirmDialog();
    }



    private void SyncReadAccessButton_Click(object sender, RoutedEventArgs e)
    {
        _readAccessToken = null;
        SyncReadAccessTokenBox.Text = "";
        SyncReadAccessTokenPanel.Visibility = Visibility.Collapsed;
        ShowReadAccessError("");


        SyncReadAccessIssueButton.IsEnabled = false;
        SyncReadAccessRevokeButton.IsEnabled = false;
        SyncReadAccessStateText.Text = App.GetString("Sync_ReadAccess_StateUnknown");




        SyncReadAccessIssueText.Text = App.GetString("Sync_ReadAccess_Generate");

        ApplyReadAccessLink();
        _animSyncReadAccess = false;
        ShowOverlay(SyncReadAccessOverlay, SyncReadAccessDialog, SyncReadAccessDialogTransform);


        FitDialogScroll(SyncReadAccessScroll, 190);
        _ = SafeRefreshReadAccessStateAsync();
    }

    private void HideSyncReadAccessDialog()
    {
        if (_animSyncReadAccess) return;
        _animSyncReadAccess = true;
        _readAccessToken = null;
        SyncReadAccessTokenBox.Text = "";
        SyncReadAccessTokenPanel.Visibility = Visibility.Collapsed;
        HideOverlay(SyncReadAccessOverlay, SyncReadAccessDialog, SyncReadAccessDialogTransform, () => _animSyncReadAccess = false);
    }




    private async Task SafeRefreshReadAccessStateAsync()
    {
        try { await RefreshReadAccessStateAsync(); }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"刷新只读访问状态失败（已忽略）: {ex}"); }
    }


    private async Task RefreshReadAccessStateAsync()
    {
        var (ok, hasToken, message) = await Services.SyncService.QueryReadTokenStateAsync();
        if (SyncReadAccessOverlay.Visibility != Visibility.Visible) return;

        if (!ok)
        {
            SyncReadAccessStateText.Text = App.GetString("Sync_ReadAccess_StateUnknown");
            SyncReadAccessIssueButton.IsEnabled = false;
            SyncReadAccessRevokeButton.IsEnabled = false;
            ShowReadAccessError(ReadAccessMessage(message));
            return;
        }

        _readAccessConfigured = hasToken;
        ApplyReadAccessState();
    }

    private void ApplyReadAccessState()
    {
        SyncReadAccessIssueButton.IsEnabled = true;
        SyncReadAccessRevokeButton.IsEnabled = _readAccessConfigured;
        SyncReadAccessStateText.Text = App.GetString(_readAccessConfigured
            ? "Sync_ReadAccess_StateOn" : "Sync_ReadAccess_StateOff");

        SyncReadAccessIssueText.Text = App.GetString(_readAccessConfigured
            ? "Sync_ReadAccess_Regenerate" : "Sync_ReadAccess_Generate");
    }

    private void ApplyReadAccessLink()
    {
        var link = Services.SyncService.BuildReadOnlyLink();
        if (link is null)
        {
            SyncReadAccessLinkBox.Text = "";
            SyncReadAccessLinkCopyButton.IsEnabled = false;
            SyncReadAccessLinkNote.Text = "";
            return;
        }

        SyncReadAccessLinkBox.Text = link.Url;
        SyncReadAccessLinkCopyButton.IsEnabled = true;

        SyncReadAccessLinkNote.Text = App.GetString(link.Secure
            ? "Sync_ReadAccess_LinkNote" : "Sync_ReadAccess_LinkNoteInsecure");
    }


    private static string ReadAccessMessage(string message)
        => message.StartsWith("Sync_", StringComparison.Ordinal) ? App.GetString(message) : message;

    private void ShowReadAccessError(string text)
    {
        SyncReadAccessErrorText.Text = text;
        SyncReadAccessErrorText.Visibility = string.IsNullOrEmpty(text) ? Visibility.Collapsed : Visibility.Visible;
    }

    private async void SyncReadAccessIssue_Click(object sender, RoutedEventArgs e)
    {
        SyncReadAccessIssueButton.IsEnabled = false;
        ShowReadAccessError("");
        try
        {
            var outcome = await Services.SyncService.IssueReadTokenAsync();
            if (!outcome.Ok)
            {
                ShowReadAccessError(outcome.Unreachable
                    ? App.GetString("Sync_ReadAccess_Unreachable")
                    : ReadAccessMessage(outcome.Message));
                return;
            }

            _readAccessToken = outcome.Token;
            SyncReadAccessTokenBox.Text = Novara.Services.ReadTokenRules.Display(outcome.Token);
            SyncReadAccessTokenPanel.Visibility = Visibility.Visible;
            _readAccessConfigured = true;
            ApplyReadAccessState();
        }
        finally { SyncReadAccessIssueButton.IsEnabled = true; }
    }

    private void SyncReadAccessTokenCopy_Click(object sender, RoutedEventArgs e)
    {
        if (_readAccessToken is { Length: > 0 } token) CopyToClipboard(token);
    }

    private void SyncReadAccessLinkCopy_Click(object sender, RoutedEventArgs e)
        => CopyToClipboard(SyncReadAccessLinkBox.Text);

    private void SyncReadAccessRevoke_Click(object sender, RoutedEventArgs e)
    {
        _animSyncReadAccessRevoke = false;
        ShowOverlay(SyncReadAccessRevokeConfirmOverlay, SyncReadAccessRevokeConfirmDialog, SyncReadAccessRevokeConfirmDialogTransform);
    }

    private void HideSyncReadAccessRevokeConfirmDialog()
    {
        if (_animSyncReadAccessRevoke) return;
        _animSyncReadAccessRevoke = true;
        HideOverlay(SyncReadAccessRevokeConfirmOverlay, SyncReadAccessRevokeConfirmDialog, SyncReadAccessRevokeConfirmDialogTransform, () => _animSyncReadAccessRevoke = false);
    }

    private void SyncReadAccessRevokeConfirmCancel_Click(object sender, RoutedEventArgs e) => HideSyncReadAccessRevokeConfirmDialog();
    private void SyncReadAccessRevokeConfirmClose_Click(object sender, RoutedEventArgs e) => HideSyncReadAccessRevokeConfirmDialog();
    private void SyncReadAccessRevokeConfirmScrim_Tapped(object sender, TappedRoutedEventArgs e)
    { if (ReferenceEquals(e.OriginalSource, SyncReadAccessRevokeConfirmScrim)) HideSyncReadAccessRevokeConfirmDialog(); }

    private void SyncReadAccessRevokeConfirmOk_Click(object sender, RoutedEventArgs e) => _ = RevokeReadAccessAsync();

    private async Task RevokeReadAccessAsync()
    {
        HideSyncReadAccessRevokeConfirmDialog();

        var (ok, message) = await Services.SyncService.RevokeReadTokenAsync();
        if (!ok) { ShowReadAccessError(ReadAccessMessage(message)); return; }

        _readAccessConfigured = false;
        _readAccessToken = null;
        SyncReadAccessTokenBox.Text = "";
        SyncReadAccessTokenPanel.Visibility = Visibility.Collapsed;
        ApplyReadAccessState();
    }

    private void SyncReadAccessClose_Click(object sender, RoutedEventArgs e) => HideSyncReadAccessDialog();
    private void SyncReadAccessScrim_Tapped(object sender, TappedRoutedEventArgs e)
    { if (ReferenceEquals(e.OriginalSource, SyncReadAccessScrim)) HideSyncReadAccessDialog(); }




    private void SyncEditAccessButton_Click(object sender, RoutedEventArgs e)
    {
        _editorAccessToken = null;
        SyncEditAccessTokenBox.Text = "";
        SyncEditAccessTokenPanel.Visibility = Visibility.Collapsed;
        ShowEditAccessError("");

        SyncEditAccessIssueButton.IsEnabled = false;
        SyncEditAccessRevokeButton.IsEnabled = false;
        SyncEditAccessStateText.Text = App.GetString("Sync_EditAccess_StateUnknown");

        SyncEditAccessIssueText.Text = App.GetString("Sync_EditAccess_Generate");

        _animSyncEditAccess = false;
        ShowOverlay(SyncEditAccessOverlay, SyncEditAccessDialog, SyncEditAccessDialogTransform);

        FitDialogScroll(SyncEditAccessScroll, 190);
        _ = RefreshEditAccessStateAsync();
    }

    private void HideSyncEditAccessDialog()
    {
        if (_animSyncEditAccess) return;
        _animSyncEditAccess = true;
        _editorAccessToken = null;
        SyncEditAccessTokenBox.Text = "";
        SyncEditAccessTokenPanel.Visibility = Visibility.Collapsed;
        HideOverlay(SyncEditAccessOverlay, SyncEditAccessDialog, SyncEditAccessDialogTransform, () => _animSyncEditAccess = false);
    }

    private async Task RefreshEditAccessStateAsync()
    {
        var (ok, hasDevice, message) = await Services.SyncService.QueryEditorDeviceStateAsync();
        if (SyncEditAccessOverlay.Visibility != Visibility.Visible) return;

        if (!ok)
        {
            SyncEditAccessStateText.Text = App.GetString("Sync_EditAccess_StateUnknown");
            SyncEditAccessIssueButton.IsEnabled = false;
            SyncEditAccessRevokeButton.IsEnabled = false;
            ShowEditAccessError(EditAccessMessage(message));
            return;
        }

        _editorAccessConfigured = hasDevice;
        ApplyEditAccessState();
    }

    private void ApplyEditAccessState()
    {
        SyncEditAccessIssueButton.IsEnabled = true;
        SyncEditAccessRevokeButton.IsEnabled = _editorAccessConfigured;
        SyncEditAccessStateText.Text = App.GetString(_editorAccessConfigured
            ? "Sync_EditAccess_StateOn" : "Sync_EditAccess_StateOff");

        SyncEditAccessIssueText.Text = App.GetString(_editorAccessConfigured
            ? "Sync_EditAccess_Regenerate" : "Sync_EditAccess_Generate");
    }


    private static string EditAccessMessage(string message)
        => message.StartsWith("Sync_", StringComparison.Ordinal) ? App.GetString(message) : message;

    private void ShowEditAccessError(string text)
    {
        SyncEditAccessErrorText.Text = text;
        SyncEditAccessErrorText.Visibility = string.IsNullOrEmpty(text) ? Visibility.Collapsed : Visibility.Visible;
    }

    private async void SyncEditAccessIssue_Click(object sender, RoutedEventArgs e)
    {
        SyncEditAccessIssueButton.IsEnabled = false;
        ShowEditAccessError("");
        try
        {
            var outcome = await Services.SyncService.IssueEditorDeviceAsync();
            if (!outcome.Ok)
            {
                ShowEditAccessError(outcome.Unreachable
                    ? App.GetString("Sync_EditAccess_Unreachable")
                    : EditAccessMessage(outcome.Message));
                return;
            }

            _editorAccessToken = outcome.Token;

            SyncEditAccessTokenBox.Text = outcome.Token;
            SyncEditAccessTokenPanel.Visibility = Visibility.Visible;
            _editorAccessConfigured = true;
            ApplyEditAccessState();
        }
        finally { SyncEditAccessIssueButton.IsEnabled = true; }
    }

    private void SyncEditAccessTokenCopy_Click(object sender, RoutedEventArgs e)
    {
        if (_editorAccessToken is { Length: > 0 } token) CopyToClipboard(token);
    }

    private void SyncEditAccessRevoke_Click(object sender, RoutedEventArgs e)
    {
        _animSyncEditAccessRevoke = false;
        ShowOverlay(SyncEditAccessRevokeConfirmOverlay, SyncEditAccessRevokeConfirmDialog, SyncEditAccessRevokeConfirmDialogTransform);
    }

    private void HideSyncEditAccessRevokeConfirmDialog()
    {
        if (_animSyncEditAccessRevoke) return;
        _animSyncEditAccessRevoke = true;
        HideOverlay(SyncEditAccessRevokeConfirmOverlay, SyncEditAccessRevokeConfirmDialog, SyncEditAccessRevokeConfirmDialogTransform, () => _animSyncEditAccessRevoke = false);
    }

    private void SyncEditAccessRevokeConfirmCancel_Click(object sender, RoutedEventArgs e) => HideSyncEditAccessRevokeConfirmDialog();
    private void SyncEditAccessRevokeConfirmClose_Click(object sender, RoutedEventArgs e) => HideSyncEditAccessRevokeConfirmDialog();
    private void SyncEditAccessRevokeConfirmScrim_Tapped(object sender, TappedRoutedEventArgs e)
    { if (ReferenceEquals(e.OriginalSource, SyncEditAccessRevokeConfirmScrim)) HideSyncEditAccessRevokeConfirmDialog(); }

    private void SyncEditAccessRevokeConfirmOk_Click(object sender, RoutedEventArgs e) => _ = RevokeEditAccessAsync();

    private async Task RevokeEditAccessAsync()
    {
        HideSyncEditAccessRevokeConfirmDialog();

        var (ok, message) = await Services.SyncService.RevokeEditorDeviceAsync();
        if (!ok) { ShowEditAccessError(EditAccessMessage(message)); return; }

        _editorAccessConfigured = false;
        _editorAccessToken = null;
        SyncEditAccessTokenBox.Text = "";
        SyncEditAccessTokenPanel.Visibility = Visibility.Collapsed;
        ApplyEditAccessState();
    }

    private void SyncEditAccessClose_Click(object sender, RoutedEventArgs e) => HideSyncEditAccessDialog();
    private void SyncEditAccessScrim_Tapped(object sender, TappedRoutedEventArgs e)
    { if (ReferenceEquals(e.OriginalSource, SyncEditAccessScrim)) HideSyncEditAccessDialog(); }




    private bool _animSyncConnInfo;

    private void SyncConnInfoButton_Click(object sender, RoutedEventArgs e)
    {
        _animSyncConnInfo = false;
        ShowOverlay(SyncConnInfoOverlay, SyncConnInfoDialog, SyncConnInfoDialogTransform);
    }

    private void HideSyncConnInfoDialog()
    {
        if (_animSyncConnInfo) return;
        _animSyncConnInfo = true;
        HideOverlay(SyncConnInfoOverlay, SyncConnInfoDialog, SyncConnInfoDialogTransform, () => _animSyncConnInfo = false);
    }

    private void SyncConnInfoClose_Click(object sender, RoutedEventArgs e) => HideSyncConnInfoDialog();
    private void SyncConnInfoScrim_Tapped(object sender, TappedRoutedEventArgs e)
    { if (ReferenceEquals(e.OriginalSource, SyncConnInfoScrim)) HideSyncConnInfoDialog(); }



    private string? _deviceActionTarget;
    private bool _animSyncDeviceCenter;
    private bool _animSyncDeviceRevokeConfirm;
    private bool _animSyncDeviceResetConfirm;

    private void SyncDeviceCenterButton_Click(object sender, RoutedEventArgs e)
    {
        _animSyncDeviceCenter = false;
        ShowOverlay(SyncDeviceCenterOverlay, SyncDeviceCenterDialog, SyncDeviceCenterDialogTransform);

        FitDialogScroll(SyncDeviceCenterScroll, 190);
        _ = RefreshDeviceListAsync();
    }

    private void HideSyncDeviceCenterDialog()
    {
        if (_animSyncDeviceCenter) return;
        _animSyncDeviceCenter = true;
        _deviceActionTarget = null;
        SyncDeviceCenterList.Children.Clear();
        HideOverlay(SyncDeviceCenterOverlay, SyncDeviceCenterDialog, SyncDeviceCenterDialogTransform, () => _animSyncDeviceCenter = false);
    }

    private void SyncDeviceCenterClose_Click(object sender, RoutedEventArgs e) => HideSyncDeviceCenterDialog();
    private void SyncDeviceCenterScrim_Tapped(object sender, TappedRoutedEventArgs e)
    { if (ReferenceEquals(e.OriginalSource, SyncDeviceCenterScrim)) HideSyncDeviceCenterDialog(); }

    private void SyncDeviceCenterRefresh_Click(object sender, RoutedEventArgs e) => _ = RefreshDeviceListAsync();

    private async Task RefreshDeviceListAsync()
    {
        ShowDeviceCenterError("");
        try
        {
            var outcome = await Services.SyncService.ListDevicesAsync();
            if (SyncDeviceCenterOverlay.Visibility != Visibility.Visible) return;
            if (!outcome.Ok)
            {
                ShowDeviceCenterError(outcome.Unreachable
                    ? App.GetString("Sync_DeviceCenter_Unreachable")
                    : DeviceCenterMessage(outcome.Message));
                return;
            }
            RenderDeviceList(outcome.Devices!);
        }
        catch (Exception)
        {




            ShowDeviceCenterError(App.GetString("Sync_DeviceCenter_LoadFailed"));
        }
        finally { if (SyncDeviceCenterOverlay.Visibility == Visibility.Visible) SyncDeviceCenterScroll.ChangeView(null, null, null, true); }
    }

    private void RenderDeviceList(IEnumerable<Novara.Models.DeviceInfo> devices)
    {
        SyncDeviceCenterList.Children.Clear();
        foreach (var device in devices)
        {
            var name = string.IsNullOrEmpty(device.Name) ? device.DeviceId : device.Name;

            var kindTag = device.Kind == "editor"
                ? App.GetString("Sync_DeviceCenter_KindEditor")
                : App.GetString("Sync_DeviceCenter_KindStandard");
            var revokedTag = device.Revoked
                ? App.GetString("Sync_DeviceCenter_StatusRevoked")
                : (device.Current
                    ? App.GetString("Sync_DeviceCenter_StatusCurrent")
                    : App.GetString("Sync_DeviceCenter_StatusActive"));
            var lastSeen = device.LastSeenAt.HasValue
                ? device.LastSeenAt.Value.ToLocalTime().ToString("yyyy-MM-dd HH:mm")
                : App.GetString("Sync_DeviceCenter_NeverSeen");

            var titleText = new TextBlock
            {
                Text = $"{name}",
                FontSize = 14,
                FontWeight = device.Current ? Microsoft.UI.Text.FontWeights.SemiBold : Microsoft.UI.Text.FontWeights.Normal,
                Foreground = App.GetBrush("AppTextPrimaryBrush"),
                TextTrimming = TextTrimming.CharacterEllipsis,
            };
            var metaText = new TextBlock
            {
                Text = $"{kindTag} · {lastSeen} · {revokedTag}",
                FontSize = 12,
                Foreground = App.GetBrush("AppTextSecondaryBrush"),
            };

            var revokeButton = new Button
            {
                Content = new TextBlock { Text = App.GetString("Sync_DeviceCenter_Revoke"), FontSize = 12, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
                Width = 72, Height = 30,
                CornerRadius = new CornerRadius(8),




                Style = (Style)Resources["McpDangerButtonStyle"],
            };
            var resetButton = new Button
            {
                Content = new TextBlock { Text = App.GetString("Sync_DeviceCenter_ResetToken"), FontSize = 12, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
                Width = 72, Height = 30,
                CornerRadius = new CornerRadius(8),
                Style = (Style)Application.Current.Resources["NovaraOutlineButtonStyle"],
            };

            if (device.Current || device.Revoked)
                revokeButton.IsEnabled = false;
            if (device.Revoked)


                resetButton.IsEnabled = false;

            revokeButton.Click += (_, _) =>
            {
                _deviceActionTarget = device.DeviceId;
                SyncDeviceRevokeConfirmMessageText.Text = App.GetString("Sync_DeviceCenter_RevokeMessage")
                    .Replace("{device}", device.Name ?? device.DeviceId);
                _animSyncDeviceRevokeConfirm = false;
                ShowOverlay(SyncDeviceRevokeConfirmOverlay, SyncDeviceRevokeConfirmDialog, SyncDeviceRevokeConfirmDialogTransform);
            };
            resetButton.Click += (_, _) =>
            {
                _deviceActionTarget = device.DeviceId;
                SyncDeviceResetConfirmMessageText.Text = App.GetString("Sync_DeviceCenter_ResetMessage")
                    .Replace("{device}", device.Name ?? device.DeviceId);
                _animSyncDeviceResetConfirm = false;
                ShowOverlay(SyncDeviceResetConfirmOverlay, SyncDeviceResetConfirmDialog, SyncDeviceResetConfirmDialogTransform);
            };

            var actionPanel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            actionPanel.Children.Add(resetButton);
            actionPanel.Children.Add(revokeButton);
            actionPanel.HorizontalAlignment = HorizontalAlignment.Right;
            actionPanel.VerticalAlignment = VerticalAlignment.Top;

            var infoPanel = new StackPanel { Spacing = 2, VerticalAlignment = VerticalAlignment.Center };
            infoPanel.Children.Add(titleText);
            infoPanel.Children.Add(metaText);

            var row = new Grid { ColumnSpacing = 12, Padding = new Thickness(0, 4, 0, 4) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            Grid.SetColumn(infoPanel, 0);
            Grid.SetColumn(actionPanel, 1);
            row.Children.Add(infoPanel);
            row.Children.Add(actionPanel);

            SyncDeviceCenterList.Children.Add(row);
        }
    }


    private static string DeviceCenterMessage(string message)
        => message.StartsWith("Sync_", StringComparison.Ordinal) ? App.GetString(message) : message;

    private void ShowDeviceCenterError(string text)
    {
        SyncDeviceCenterErrorText.Text = text;
        SyncDeviceCenterErrorText.Visibility = string.IsNullOrEmpty(text) ? Visibility.Collapsed : Visibility.Visible;
    }


    private void HideSyncDeviceRevokeConfirmDialog()
    {
        if (_animSyncDeviceRevokeConfirm) return;
        _animSyncDeviceRevokeConfirm = true;
        HideOverlay(SyncDeviceRevokeConfirmOverlay, SyncDeviceRevokeConfirmDialog, SyncDeviceRevokeConfirmDialogTransform, () => _animSyncDeviceRevokeConfirm = false);
    }
    private void SyncDeviceRevokeConfirmClose_Click(object sender, RoutedEventArgs e) => HideSyncDeviceRevokeConfirmDialog();
    private void SyncDeviceRevokeConfirmScrim_Tapped(object sender, TappedRoutedEventArgs e)
    { if (ReferenceEquals(e.OriginalSource, SyncDeviceRevokeConfirmScrim)) HideSyncDeviceRevokeConfirmDialog(); }
    private void SyncDeviceRevokeConfirmCancel_Click(object sender, RoutedEventArgs e) => HideSyncDeviceRevokeConfirmDialog();
    private void SyncDeviceRevokeConfirmOk_Click(object sender, RoutedEventArgs e) => _ = DoRevokeDeviceAsync();

    private async Task DoRevokeDeviceAsync()
    {
        var target = _deviceActionTarget;
        HideSyncDeviceRevokeConfirmDialog();
        if (string.IsNullOrEmpty(target)) return;

        var (ok, message) = await Services.SyncService.RevokeDeviceAsync(target);
        if (!ok) { ShowDeviceCenterError(DeviceCenterMessage(message)); return; }

        _ = RefreshDeviceListAsync();
    }


    private void HideSyncDeviceResetConfirmDialog()
    {
        if (_animSyncDeviceResetConfirm) return;
        _animSyncDeviceResetConfirm = true;
        HideOverlay(SyncDeviceResetConfirmOverlay, SyncDeviceResetConfirmDialog, SyncDeviceResetConfirmDialogTransform, () => _animSyncDeviceResetConfirm = false);
    }
    private void SyncDeviceResetConfirmClose_Click(object sender, RoutedEventArgs e) => HideSyncDeviceResetConfirmDialog();
    private void SyncDeviceResetConfirmScrim_Tapped(object sender, TappedRoutedEventArgs e)
    { if (ReferenceEquals(e.OriginalSource, SyncDeviceResetConfirmScrim)) HideSyncDeviceResetConfirmDialog(); }
    private void SyncDeviceResetConfirmCancel_Click(object sender, RoutedEventArgs e) => HideSyncDeviceResetConfirmDialog();
    private void SyncDeviceResetConfirmOk_Click(object sender, RoutedEventArgs e) => _ = DoResetDeviceTokenAsync();

    private async Task DoResetDeviceTokenAsync()
    {
        var target = _deviceActionTarget;
        HideSyncDeviceResetConfirmDialog();
        if (string.IsNullOrEmpty(target)) return;

        var (ok, message) = await Services.SyncService.ResetTokenAsync(target);
        if (!ok) { ShowDeviceCenterError(DeviceCenterMessage(message)); return; }

        _ = RefreshDeviceListAsync();
    }
}
