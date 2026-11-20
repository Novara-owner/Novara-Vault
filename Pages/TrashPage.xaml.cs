using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Novara.Models;
using Novara.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Windows.UI;

namespace Novara.Pages;







public sealed partial class TrashPage : Page
{
    private object? _pendingDeleteForever;
    private bool _restoring;
    private bool _clearAllConfirmed;
    private CancellationTokenSource? _renderCts;

    private readonly Dictionary<Grid, int> _dialogGens = new();
    private readonly Dictionary<Grid, Border> _dialogMap = new();
    private readonly DialogUi.DialogWidthFit _dialogWidthFit = new();

    public TrashPage()
    {
        InitializeComponent();
        App.ApplyAutomationNames(this);
        Unloaded += TrashPage_Unloaded;
        Novara.Services.DialogDepth.AttachContainer((Grid)Content, autoVeil: true);
        TrashRoot.SizeChanged += (_, _) => ReclampVisibleDialogs();
        InitIcons();


        KeyDown += (_, e) =>
        {
            if (e.Key != Windows.System.VirtualKey.Escape) return;
            if (DeleteForeverOverlay.Visibility == Visibility.Visible) { HideDeleteForeverDialog(); e.Handled = true; }
            else if (ClearAllOverlay.Visibility == Visibility.Visible) { HideClearAllDialog(); e.Handled = true; }
        };


        Loaded += (_, _) => FocusSink.Focus(FocusState.Programmatic);
    }

    private void InitIcons()
    {
        var cv = Microsoft.UI.Xaml.Markup.XamlBindingHelper.ConvertValue;
        BackPathIcon.Data = (Geometry)cv(typeof(Geometry), IconData.Back);
        ClearAllPathIcon.Data = (Geometry)cv(typeof(Geometry), IconData.Delete);
        RestoreAllPathIcon.Data = (Geometry)cv(typeof(Geometry), IconData.RestoreTrash);
    }


    public void Refresh() => BuildList();



    private void TrashPage_Unloaded(object sender, RoutedEventArgs e)
    {
        DeleteForeverOverlay.Visibility = Visibility.Collapsed;
        ClearAllOverlay.Visibility = Visibility.Collapsed;


    foreach (var key in _dialogGens.Keys.ToList()) _dialogGens[key] = _dialogGens[key] + 1;
    _dialogMap.Clear();
        DeleteForeverCd.Reset();
        ClearAllCd.Reset();
        _pendingDeleteForever = null;
        _renderCts?.Cancel();
    }



    public void StealFocus() => FocusSink.Focus(FocusState.Programmatic);


    public static void CleanupExpired()
    {
        var db = App.Store?.Database;
        if (db == null) return;
        var cutoff = DateTime.Now.AddDays(-7);




        bool changed = false;



        foreach (var e in db.MemoEntries.Where(x => x.IsDeleted && x.PurgedAt == default && x.DeletedAt == default)) { e.DeletedAt = DateTime.Now; changed = true; }
        foreach (var p in db.PathBackupItems.Where(x => x.IsDeleted && x.PurgedAt == default && x.DeletedAt == default)) { p.DeletedAt = DateTime.Now; changed = true; }
        foreach (var t in db.TodoCards.Where(x => x.IsDeleted && x.PurgedAt == default && x.DeletedAt == default)) { t.DeletedAt = DateTime.Now; changed = true; }
        foreach (var n in db.NoteCards.Where(x => x.IsDeleted && x.PurgedAt == default && x.DeletedAt == default)) { n.DeletedAt = DateTime.Now; changed = true; }
        foreach (var d in db.DiaryItems.Where(x => x.IsDeleted && x.PurgedAt == default && x.DeletedAt == default)) { d.DeletedAt = DateTime.Now; changed = true; }
        foreach (var n in db.NoteCards.Where(x => x.IsDeleted && x.PurgedAt == default && x.DeletedAt != default && x.DeletedAt < cutoff))
        {
            Services.StickySync.RemoveNote(n.Id.ToString());
            PurgeNote(n); changed = true;
        }
        foreach (var t in db.TodoCards.Where(x => x.IsDeleted && x.PurgedAt == default && x.DeletedAt != default && x.DeletedAt < cutoff))
        {
            Services.StickySync.RemoveNote(t.Id.ToString());
            PurgeTodo(t); changed = true;
        }
        foreach (var e in db.MemoEntries.Where(x => x.IsDeleted && x.PurgedAt == default && x.DeletedAt != default && x.DeletedAt < cutoff)) { PurgeMemo(e); changed = true; }
        foreach (var p in db.PathBackupItems.Where(x => x.IsDeleted && x.PurgedAt == default && x.DeletedAt != default && x.DeletedAt < cutoff)) { PurgePath(p); changed = true; }
        foreach (var d in db.DiaryItems.Where(x => x.IsDeleted && x.PurgedAt == default && x.DeletedAt != default && x.DeletedAt < cutoff)) { PurgeDiary(d); changed = true; }
        if (changed) App.Store?.SaveAsync();
    }





    private static void PurgeMemo(MemoEntry e)
    {
        e.Type = ""; e.Name = ""; e.KeyInfo = ""; e.Fields = new(); e.Protocol = "";
        e.IconKey = ""; e.WorkspaceId = ""; e.GroupId = null;
        e.IsStarred = false; e.IsPinned = false;
        e.PurgedAt = DateTime.Now;
    }

    private static void PurgePath(FilePathEntry p)
    {
        p.Name = ""; p.Path = ""; p.Note = ""; p.WorkspaceId = "";
        p.IsStarred = false; p.IsPinned = false;
        p.PurgedAt = DateTime.Now;
    }

    private static void PurgeTodo(TodoCard t)
    {
        t.Title = ""; t.MainText = ""; t.SubTexts.Clear(); t.CheckedStates.Clear();
        t.IconKey = ""; t.WorkspaceId = ""; t.ReminderAt = null; t.ReminderSetAt = null;
        t.IsStarred = false; t.IsPinned = false; t.IsExpanded = null;
        t.PurgedAt = DateTime.Now;
    }

    private static void PurgeNote(NoteCard n)
    {
        n.Title = ""; n.Content = ""; n.IconKey = ""; n.WorkspaceId = "";
        n.ReminderAt = null; n.ReminderSetAt = null;
        n.IsStarred = false; n.IsPinned = false;
        n.PurgedAt = DateTime.Now;
    }

    private static void PurgeDiary(DiaryEntry d)
    {
        d.Title = ""; d.Content = ""; d.WorkspaceId = "";
        d.IsStarred = false; d.IsPinned = false;
        d.PurgedAt = DateTime.Now;
    }

    private async void BuildList()
    {




        _renderCts?.Cancel();
        _renderCts?.Dispose();
        _renderCts = new CancellationTokenSource();
        var ct = _renderCts.Token;

        TrashList.Children.Clear();
        var db = App.Store?.Database;
        if (db == null) return;


        var rows = new List<(string Kind, string Title, DateTime DeletedAt, object Entity, string Format)>();

        foreach (var e in db.MemoEntries.Where(x => x.IsDeleted && x.PurgedAt == default))




            rows.Add(("memo", string.IsNullOrEmpty(e.Name) ? MemoFieldMask.MaskedKeyInfo(e.Type, e.KeyInfo) : e.Name, e.DeletedAt, e, ""));
        foreach (var p in db.PathBackupItems.Where(x => x.IsDeleted && x.PurgedAt == default))
            rows.Add(("path", p.Name, p.DeletedAt, p, ""));
        foreach (var t in db.TodoCards.Where(x => x.IsDeleted && x.PurgedAt == default))
            rows.Add(("todo", t.Title, t.DeletedAt, t, ""));
        foreach (var n in db.NoteCards.Where(x => x.IsDeleted && x.PurgedAt == default))
            rows.Add(("note", n.Title, n.DeletedAt, n, ""));
        foreach (var d in db.DiaryItems.Where(x => x.IsDeleted && x.PurgedAt == default))
            rows.Add(("diary", d.Title, d.DeletedAt, d, d.Format));

        rows = rows.OrderByDescending(r => r.DeletedAt).ToList();


        int idx = 0;
        try
        {
            await Services.ChunkedRender.RunAsync(rows.Count, 10, DispatcherQueue, (s2, e2) =>
            {
                for (int i = s2; i < e2; i++)
                {
                    var card = BuildCard(rows[i]);
                    TrashList.Children.Add(card);
                    if (idx < 10) App.PlayCardEntrance(card, idx);
                    idx++;
                }
            }, ct);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (Exception ex)
        {



            System.Diagnostics.Debug.WriteLine($"回收站列表渲染失败（重新打开将重建）: {ex}");
            return;
        }

        if (rows.Count == 0)
        {

            EmptyState.Content = Novara.Services.EmptyHintBuilder.Build(
                Novara.Services.EmptyStateArt.TrashBody, Novara.Services.EmptyStateArt.TrashAccent, 1024, 1024,
                App.GetString("Trash_EmptyTitle"), App.GetString("Trash_EmptyHint"));
            EmptyState.Visibility = Visibility.Visible;
        }
        else EmptyState.Visibility = Visibility.Collapsed;

        RestoreAllButton.Visibility = rows.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        ClearAllButton.Visibility = rows.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private Border BuildCard((string Kind, string Title, DateTime DeletedAt, object Entity, string Format) row)
    {
        var card = new Border
        {
            Background = App.GetBrush("AppSurfaceElevatedBrush"),
            BorderBrush = App.GetBrush("AppBorderBrush"),
            BorderThickness = new Thickness(1, 1, 1, 1),
            CornerRadius = new CornerRadius(16),
            Padding = new Thickness(18, 12, 18, 12),
        };

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2, GridUnitType.Star) });

        var info = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Spacing = 4 };
        var nameRow = new Grid { ColumnSpacing = 6 };
        nameRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        nameRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var name = new TextBlock
        {
            Text = string.IsNullOrEmpty(row.Title) ? App.GetString("Plan_Note_NoContent") : row.Title,
            FontSize = 14,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Foreground = App.GetBrush("AppTextPrimaryBrush"),
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        nameRow.Children.Add(name);
        var tagIcon = new Viewbox
        {
            Width = 16, Height = 16,
            Stretch = Microsoft.UI.Xaml.Media.Stretch.Uniform,
            VerticalAlignment = VerticalAlignment.Top,
            Child = new PathIcon { Data = App.CreateGeometry(IconData.GetTagIcon(row.Kind, row.Format)), Foreground = App.GetBrush("AppPrimaryButtonBrush") },
        };
        Grid.SetColumn(tagIcon, 1);
        nameRow.Children.Add(tagIcon);
        info.Children.Add(nameRow);
        var time = new TextBlock
        {
            Text = string.Format(App.GetString("Trash_DeletedAt"), row.DeletedAt.ToString("yyyy-MM-dd HH:mm")),
            FontSize = 12,
            Foreground = App.GetBrush("AppTextTertiaryBrush"),
        };
        info.Children.Add(time);
        grid.Children.Add(info);


        var separator = new Border
        {
            Width = 2,
            VerticalAlignment = VerticalAlignment.Stretch,
            Margin = new Thickness(12, 8, 12, 8),
            Background = App.GetBrush("AppBorderBrush"),
            CornerRadius = new CornerRadius(1)
        };
        Grid.SetColumn(separator, 1);
        grid.Children.Add(separator);

        var actions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Spacing = 16,
        };

        var brandBlue = App.GetBrush("AppPrimaryButtonBrush");
        var dangerRed = App.GetBrush("AppDangerTextBrush");
        var restoreBtn = MakeIconButton(IconData.RestoreTrash, (_, _) => Restore(row.Entity), App.GetString("Trash_Restore"), brandBlue, brandBlue, bold: true);

        var delBtn = MakeIconButton(IconData.Delete, (_, _) =>
        {
            _pendingDeleteForever = row.Entity;
            ShowDeleteForeverDialog();
        }, App.GetString("Trash_DeleteForever"), dangerRed, dangerRed, style: (Style)Application.Current.Resources["NovaraCardActionButtonDangerStyle"]);
        actions.Children.Add(restoreBtn);
        actions.Children.Add(delBtn);
        Grid.SetColumn(actions, 2);
        grid.Children.Add(actions);

        card.Child = grid;
        return card;
    }

    private Button MakeIconButton(string iconData, RoutedEventHandler click, string a11yName, Brush? foreground = null, Brush? borderBrush = null, bool bold = false, Style? style = null)
    {
        var cv = Microsoft.UI.Xaml.Markup.XamlBindingHelper.ConvertValue;
        var btn = new Button
        {
            Width = 44,
            Height = 44,
            Style = style ?? (Style)Application.Current.Resources["NovaraCardActionButtonStyle"],
            CornerRadius = new CornerRadius(12),
        };


        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(btn, a11yName);
        ToolTipService.SetToolTip(btn, new ToolTip { Content = a11yName, Style = (Style)Application.Current.Resources["BrandToolTipStyle"] });
        if (borderBrush != null) btn.BorderBrush = borderBrush;
        var fg = foreground ?? App.GetBrush("IconForegroundBrush");
        if (bold)
        {


            btn.Content = new Microsoft.UI.Xaml.Shapes.Path
            {
                Width = 20,
                Height = 20,
                Stretch = Microsoft.UI.Xaml.Media.Stretch.Uniform,
                Data = (Geometry)cv(typeof(Geometry), iconData),
                Fill = fg,
                Stroke = fg,
                StrokeThickness = 0.6,
            };
        }
        else
        {
            btn.Content = new Viewbox
            {
                Width = 20,
                Height = 20,
                Stretch = Microsoft.UI.Xaml.Media.Stretch.Uniform,
                Child = new PathIcon
                {
                    Data = (Geometry)cv(typeof(Geometry), iconData),
                    Foreground = fg,
                },
            };
        }
        btn.Click += click;
        return btn;
    }

    private void Restore(object entity)
    {
        if (_restoring) return;




        if (ResolveCurrent(entity) is not { } live) { Refresh(); return; }

        if (!StillInTrash(live))
        {
            Refresh();
            App.ShowToast(App.GetString("Trash_Stale_Changed"));
            return;
        }
        _restoring = true;
        try
        {
            RestoreEntity(live);
            App.Store?.SaveAsync();
            App.MainWindow?.MarkTrashChanged();
            BuildList();
            App.ShowToast(App.GetString("Common_Toast_Restored"));
        }
        finally
        {
            _restoring = false;
        }
    }




    private static object? ResolveCurrent(object entity)
    {
        var db = App.Store?.Database;
        if (db == null) return null;
        return entity switch
        {
            MemoEntry me => db.MemoEntries.FirstOrDefault(x => x.Id == me.Id),
            FilePathEntry pe => db.PathBackupItems.FirstOrDefault(x => x.Id == pe.Id),
            TodoCard tc => db.TodoCards.FirstOrDefault(x => x.Id == tc.Id),
            NoteCard nc => db.NoteCards.FirstOrDefault(x => x.Id == nc.Id),
            DiaryEntry de => db.DiaryItems.FirstOrDefault(x => x.Id == de.Id),
            _ => null,
        };
    }




    private static bool StillInTrash(object live) => live switch
    {
        MemoEntry me => me.IsDeleted && me.PurgedAt == default,
        FilePathEntry pe => pe.IsDeleted && pe.PurgedAt == default,
        TodoCard tc => tc.IsDeleted && tc.PurgedAt == default,
        NoteCard nc => nc.IsDeleted && nc.PurgedAt == default,
        DiaryEntry de => de.IsDeleted && de.PurgedAt == default,
        _ => false,
    };


    private static void RestoreEntity(object entity)
    {
        var db = App.Store?.Database;


        switch (entity)
        {
            case MemoEntry me:
                me.IsDeleted = false; me.DeletedAt = default; me.PurgedAt = default; me.IsStarred = false; me.IsPinned = false;
                if (me.GroupId.HasValue && db != null && db.MemoGroups.All(g => g.Id != me.GroupId.Value)) me.GroupId = null;
                break;
            case FilePathEntry pe:
                pe.IsDeleted = false; pe.DeletedAt = default; pe.PurgedAt = default; pe.IsStarred = false; pe.IsPinned = false;
                break;
            case TodoCard tc:
                tc.IsDeleted = false; tc.DeletedAt = default; tc.PurgedAt = default; tc.IsStarred = false; tc.IsPinned = false;
                break;
            case NoteCard nc:
                nc.IsDeleted = false; nc.DeletedAt = default; nc.PurgedAt = default; nc.IsStarred = false; nc.IsPinned = false;
                break;
            case DiaryEntry de:
                de.IsDeleted = false; de.DeletedAt = default; de.PurgedAt = default; de.IsStarred = false; de.IsPinned = false;
                break;
        }
    }


    private void RestoreAll()
    {
        if (_restoring) return;
        _restoring = true;
        try
        {
            var db = App.Store?.Database;
            if (db == null) return;




            foreach (var e in db.MemoEntries.Where(x => x.IsDeleted && x.PurgedAt == default).ToList()) RestoreEntity(e);
            foreach (var p in db.PathBackupItems.Where(x => x.IsDeleted && x.PurgedAt == default).ToList()) RestoreEntity(p);
            foreach (var t in db.TodoCards.Where(x => x.IsDeleted && x.PurgedAt == default).ToList()) RestoreEntity(t);
            foreach (var n in db.NoteCards.Where(x => x.IsDeleted && x.PurgedAt == default).ToList()) RestoreEntity(n);
            foreach (var d in db.DiaryItems.Where(x => x.IsDeleted && x.PurgedAt == default).ToList()) RestoreEntity(d);

            App.Store?.SaveAsync();
            App.MainWindow?.MarkTrashChanged();
            BuildList();
            App.ShowToast(App.GetString("Common_Toast_Restored"));
        }
        finally
        {
            _restoring = false;
        }
    }

    private void RestoreAllButton_Click(object sender, RoutedEventArgs e) => RestoreAll();

    private void DeleteForever(object entity)
    {
        if (_restoring) return;


        if (ResolveCurrent(entity) is not { } live) { Refresh(); return; }


        if (!StillInTrash(live))
        {
            Refresh();
            App.ShowToast(App.GetString("Trash_Stale_Changed"));
            return;
        }
        _restoring = true;
        try
        {
            var db = App.Store?.Database;
            if (db == null) return;
            switch (live)
            {
                case MemoEntry me: PurgeMemo(me); break;
                case FilePathEntry pe: PurgePath(pe); break;
                case TodoCard tc:
                    PurgeTodo(tc);
                    Services.StickySync.RemoveNote(tc.Id.ToString());
                    break;
                case NoteCard nc:
                    PurgeNote(nc);
                    Services.StickySync.RemoveNote(nc.Id.ToString());
                    break;
                case DiaryEntry de: PurgeDiary(de); break;
            }
            App.Store?.SaveAsync();
            App.MainWindow?.MarkTrashChanged();
            BuildList();
            App.ShowToast(App.GetString("Common_Toast_Deleted"));
        }
        finally
        {
            _restoring = false;
        }
    }

    private void ClearAll()
    {
        if (_restoring) return;
        _restoring = true;
        try
        {
            var db = App.Store?.Database;
            if (db == null) return;




            var removedTodoIds = db.TodoCards.Where(x => x.IsDeleted && x.PurgedAt == default).Select(x => x.Id.ToString()).ToList();
            var removedNoteIds = db.NoteCards.Where(x => x.IsDeleted && x.PurgedAt == default).Select(x => x.Id.ToString()).ToList();
            foreach (var e in db.MemoEntries.Where(x => x.IsDeleted && x.PurgedAt == default)) PurgeMemo(e);
            foreach (var p in db.PathBackupItems.Where(x => x.IsDeleted && x.PurgedAt == default)) PurgePath(p);
            foreach (var t in db.TodoCards.Where(x => x.IsDeleted && x.PurgedAt == default)) PurgeTodo(t);
            foreach (var n in db.NoteCards.Where(x => x.IsDeleted && x.PurgedAt == default)) PurgeNote(n);
            foreach (var d in db.DiaryItems.Where(x => x.IsDeleted && x.PurgedAt == default)) PurgeDiary(d);
            foreach (var id in removedTodoIds) Services.StickySync.RemoveNote(id);
            foreach (var id in removedNoteIds) Services.StickySync.RemoveNote(id);
            App.Store?.SaveAsync();
            App.MainWindow?.MarkTrashChanged();
            BuildList();
            App.ShowToast(App.GetString("Common_Toast_Cleared"));
        }
        finally
        {
            _restoring = false;
        }
    }


    private void ShowDeleteForeverDialog()
    {
        DeleteForeverDangerIcon.Data = App.CreateGeometry(IconData.Danger);

        DialogUi.ShowOverlay(TrashRoot, _dialogGens, _dialogMap, DeleteForeverOverlay, DeleteForeverDialog, DeleteForeverDialogTransform, _dialogWidthFit);

        DeleteForeverConfirmButton.IsEnabled = false;
        DeleteForeverConfirmButton.Opacity = Motion.CooldownDisabledOpacity;
        DeleteForeverCd.Completed -= OnDeleteForeverCdCompleted;
        DeleteForeverCd.Completed += OnDeleteForeverCdCompleted;
        DeleteForeverCd.Start(Motion.CooldownSeconds);
    }

    private void OnDeleteForeverCdCompleted()
    {
        DeleteForeverCd.Completed -= OnDeleteForeverCdCompleted;
        DeleteForeverConfirmButton.IsEnabled = true;
        DeleteForeverConfirmButton.Opacity = 1;
    }

    private void HideDeleteForeverDialog()
    {
        DeleteForeverCd.Stop();
        var cdGen = DeleteForeverCd.Generation;
        _pendingDeleteForever = null;

        DialogUi.HideOverlay(TrashRoot, _dialogGens, _dialogMap, DeleteForeverOverlay, DeleteForeverDialog, DeleteForeverDialogTransform,
            () => DeleteForeverCd.ResetAfterHide(cdGen));
    }

    private void ReclampVisibleDialogs()
        => DialogUi.ReclampVisibleDialogs(TrashRoot, _dialogMap, widthFit: _dialogWidthFit);

    private void ShowClearAllDialog()
    {
        _clearAllConfirmed = false;
        ClearAllDangerIcon.Data = App.CreateGeometry(IconData.Danger);

        DialogUi.ShowOverlay(TrashRoot, _dialogGens, _dialogMap, ClearAllOverlay, ClearAllDialog, ClearAllDialogTransform, _dialogWidthFit);

        ClearAllConfirmButton.IsEnabled = false;
        ClearAllConfirmButton.Opacity = Motion.CooldownDisabledOpacity;
        ClearAllCd.Completed -= OnClearAllCdCompleted;
        ClearAllCd.Completed += OnClearAllCdCompleted;
        ClearAllCd.Start(Motion.CooldownSeconds);
    }

    private void OnClearAllCdCompleted()
    {
        ClearAllCd.Completed -= OnClearAllCdCompleted;
        ClearAllConfirmButton.IsEnabled = true;
        ClearAllConfirmButton.Opacity = 1;
    }

    private void HideClearAllDialog()
    {
        ClearAllCd.Stop();
        var cdGen = ClearAllCd.Generation;

        DialogUi.HideOverlay(TrashRoot, _dialogGens, _dialogMap, ClearAllOverlay, ClearAllDialog, ClearAllDialogTransform,
            () => ClearAllCd.ResetAfterHide(cdGen));
    }

    private void BackButton_Click(object sender, RoutedEventArgs e) => App.MainWindow?.CloseTrashPage();

    private void ClearAllButton_Click(object sender, RoutedEventArgs e) => ShowClearAllDialog();

    private void DeleteForeverConfirm_Click(object sender, RoutedEventArgs e)
    {
        if (_pendingDeleteForever != null) DeleteForever(_pendingDeleteForever);
        HideDeleteForeverDialog();
    }

    private void DeleteForeverCancel_Click(object sender, RoutedEventArgs e) => HideDeleteForeverDialog();
    private void DeleteForeverClose_Click(object sender, RoutedEventArgs e) => HideDeleteForeverDialog();
    private void DeleteForeverScrim_Tapped(object sender, Microsoft.UI.Xaml.Input.TappedRoutedEventArgs e)
    {
        if (ReferenceEquals(e.OriginalSource, DeleteForeverScrim)) HideDeleteForeverDialog();
    }

    private void ClearAllConfirm_Click(object sender, RoutedEventArgs e)
    {

        if (_clearAllConfirmed) return;
        _clearAllConfirmed = true;
        ClearAll();
        HideClearAllDialog();
    }

    private void ClearAllCancel_Click(object sender, RoutedEventArgs e) => HideClearAllDialog();
    private void ClearAllClose_Click(object sender, RoutedEventArgs e) => HideClearAllDialog();
    private void ClearAllScrim_Tapped(object sender, Microsoft.UI.Xaml.Input.TappedRoutedEventArgs e)
    {
        if (ReferenceEquals(e.OriginalSource, ClearAllScrim)) HideClearAllDialog();
    }
}
