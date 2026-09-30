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
    private CancellationTokenSource? _renderCts;

    public TrashPage()
    {
        InitializeComponent();
        Unloaded += TrashPage_Unloaded;
        Novara.Services.DialogDepth.AttachContainer((Grid)Content, autoVeil: true);
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


        foreach (var n in db.NoteCards.Where(x => x.IsDeleted && x.DeletedAt < cutoff)) Services.StickySync.RemoveNote(n.Id.ToString());
        foreach (var t in db.TodoCards.Where(x => x.IsDeleted && x.DeletedAt < cutoff)) Services.StickySync.RemoveNote(t.Id.ToString());
        bool changed = false;
        changed |= db.MemoEntries.RemoveAll(x => x.IsDeleted && x.DeletedAt < cutoff) > 0;
        changed |= db.PathBackupItems.RemoveAll(x => x.IsDeleted && x.DeletedAt < cutoff) > 0;
        changed |= db.TodoCards.RemoveAll(x => x.IsDeleted && x.DeletedAt < cutoff) > 0;
        changed |= db.NoteCards.RemoveAll(x => x.IsDeleted && x.DeletedAt < cutoff) > 0;
        changed |= db.DiaryItems.RemoveAll(x => x.IsDeleted && x.DeletedAt < cutoff) > 0;
        if (changed) App.Store?.SaveAsync();
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
        foreach (var e in db.MemoEntries.Where(x => x.IsDeleted))




            rows.Add(("memo", string.IsNullOrEmpty(e.Name) ? MemoFieldMask.MaskedKeyInfo(e.Type, e.KeyInfo) : e.Name, e.DeletedAt, e, ""));
        foreach (var p in db.PathBackupItems.Where(x => x.IsDeleted))
            rows.Add(("path", p.Name, p.DeletedAt, p, ""));
        foreach (var t in db.TodoCards.Where(x => x.IsDeleted))
            rows.Add(("todo", t.Title, t.DeletedAt, t, ""));
        foreach (var n in db.NoteCards.Where(x => x.IsDeleted))
            rows.Add(("note", n.Title, n.DeletedAt, n, ""));
        foreach (var d in db.DiaryItems.Where(x => x.IsDeleted))
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

        EmptyState.Visibility = rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

        RestoreAllButton.Visibility = rows.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        ClearAllButton.Visibility = rows.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        if (rows.Count == 0) PlayEmptyStateEntrance();
    }


    private void PlayEmptyStateEntrance()
    {

        if (!App.IsAnimationsEnabled)
        {
            EmptyState.Opacity = 1;
            if (EmptyState.RenderTransform is TranslateTransform st) st.Y = 0;
            return;
        }
        EmptyState.Opacity = 0;
        if (EmptyState.RenderTransform is not TranslateTransform tt)
        {
            tt = new TranslateTransform { Y = 20 };
            EmptyState.RenderTransform = tt;
        }
        else tt.Y = 20;
        var sb = new Microsoft.UI.Xaml.Media.Animation.Storyboard();
        var oa = new Microsoft.UI.Xaml.Media.Animation.DoubleAnimation { To = 1, Duration = TimeSpan.FromMilliseconds(400), EasingFunction = new Microsoft.UI.Xaml.Media.Animation.CubicEase { EasingMode = Microsoft.UI.Xaml.Media.Animation.EasingMode.EaseOut } };
        Microsoft.UI.Xaml.Media.Animation.Storyboard.SetTarget(oa, EmptyState);
        Microsoft.UI.Xaml.Media.Animation.Storyboard.SetTargetProperty(oa, "Opacity");
        sb.Children.Add(oa);
        var ya = new Microsoft.UI.Xaml.Media.Animation.DoubleAnimation { To = 0, Duration = TimeSpan.FromMilliseconds(400), EasingFunction = new Microsoft.UI.Xaml.Media.Animation.CubicEase { EasingMode = Microsoft.UI.Xaml.Media.Animation.EasingMode.EaseOut } };
        Microsoft.UI.Xaml.Media.Animation.Storyboard.SetTarget(ya, tt);
        Microsoft.UI.Xaml.Media.Animation.Storyboard.SetTargetProperty(ya, "Y");
        sb.Children.Add(ya);
        sb.Begin();
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
        var restoreBtn = MakeIconButton(IconData.RestoreTrash, (_, _) => Restore(row.Entity), brandBlue, brandBlue, bold: true);
        var delBtn = MakeIconButton(IconData.Delete, (_, _) =>
        {
            _pendingDeleteForever = row.Entity;
            ShowDeleteForeverDialog();
        }, dangerRed, dangerRed);
        actions.Children.Add(restoreBtn);
        actions.Children.Add(delBtn);
        Grid.SetColumn(actions, 2);
        grid.Children.Add(actions);

        card.Child = grid;
        return card;
    }

    private Button MakeIconButton(string iconData, RoutedEventHandler click, Brush? foreground = null, Brush? borderBrush = null, bool bold = false)
    {
        var cv = Microsoft.UI.Xaml.Markup.XamlBindingHelper.ConvertValue;
        var btn = new Button
        {
            Width = 44,
            Height = 44,
            Style = (Style)Application.Current.Resources["NovaraCardActionButtonStyle"],
            CornerRadius = new CornerRadius(12),
        };
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
        _restoring = true;
        try
        {
            RestoreEntity(entity);
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


    private static void RestoreEntity(object entity)
    {
        var db = App.Store?.Database;
        switch (entity)
        {
            case MemoEntry me:
                me.IsDeleted = false; me.DeletedAt = default; me.IsStarred = false; me.IsPinned = false;
                if (me.GroupId.HasValue && db != null && db.MemoGroups.All(g => g.Id != me.GroupId.Value)) me.GroupId = null;
                break;
            case FilePathEntry pe:
                pe.IsDeleted = false; pe.DeletedAt = default; pe.IsStarred = false; pe.IsPinned = false;
                break;
            case TodoCard tc:
                tc.IsDeleted = false; tc.DeletedAt = default; tc.IsStarred = false; tc.IsPinned = false;
                break;
            case NoteCard nc:
                nc.IsDeleted = false; nc.DeletedAt = default; nc.IsStarred = false; nc.IsPinned = false;
                break;
            case DiaryEntry de:
                de.IsDeleted = false; de.DeletedAt = default; de.IsStarred = false; de.IsPinned = false;
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

            foreach (var e in db.MemoEntries.Where(x => x.IsDeleted).ToList()) RestoreEntity(e);
            foreach (var p in db.PathBackupItems.Where(x => x.IsDeleted).ToList()) RestoreEntity(p);
            foreach (var t in db.TodoCards.Where(x => x.IsDeleted).ToList()) RestoreEntity(t);
            foreach (var n in db.NoteCards.Where(x => x.IsDeleted).ToList()) RestoreEntity(n);
            foreach (var d in db.DiaryItems.Where(x => x.IsDeleted).ToList()) RestoreEntity(d);

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
        _restoring = true;
        try
        {
            var db = App.Store?.Database;
            if (db == null) return;
            switch (entity)
            {
                case MemoEntry me: db.MemoEntries.Remove(me); break;
                case FilePathEntry pe: db.PathBackupItems.Remove(pe); break;
                case TodoCard tc:
                    db.TodoCards.Remove(tc);
                    Services.StickySync.RemoveNote(tc.Id.ToString());
                    break;
                case NoteCard nc:
                    db.NoteCards.Remove(nc);
                    Services.StickySync.RemoveNote(nc.Id.ToString());
                    break;
                case DiaryEntry de: db.DiaryItems.Remove(de); break;
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



            var removedTodoIds = db.TodoCards.Where(x => x.IsDeleted).Select(x => x.Id.ToString()).ToList();
            var removedNoteIds = db.NoteCards.Where(x => x.IsDeleted).Select(x => x.Id.ToString()).ToList();
            db.MemoEntries.RemoveAll(x => x.IsDeleted);
            db.PathBackupItems.RemoveAll(x => x.IsDeleted);
            db.TodoCards.RemoveAll(x => x.IsDeleted);
            db.NoteCards.RemoveAll(x => x.IsDeleted);
            db.DiaryItems.RemoveAll(x => x.IsDeleted);
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


        DeleteForeverDialog.Opacity = 0;
        DeleteForeverDialogTransform.ScaleX = 0.94; DeleteForeverDialogTransform.ScaleY = 0.94; DeleteForeverDialogTransform.TranslateY = 24;
        DeleteForeverScrim.Opacity = 0;
        DeleteForeverOverlay.Visibility = Visibility.Visible;
        var sb = new Microsoft.UI.Xaml.Media.Animation.Storyboard();
        var sa = new Microsoft.UI.Xaml.Media.Animation.DoubleAnimation { To = 1, Duration = TimeSpan.FromMilliseconds(250) };
        Microsoft.UI.Xaml.Media.Animation.Storyboard.SetTarget(sa, DeleteForeverScrim);
        Microsoft.UI.Xaml.Media.Animation.Storyboard.SetTargetProperty(sa, "Opacity");
        sb.Children.Add(sa);
        var da = new Microsoft.UI.Xaml.Media.Animation.DoubleAnimation { To = 1, Duration = TimeSpan.FromMilliseconds(300) };
        Microsoft.UI.Xaml.Media.Animation.Storyboard.SetTarget(da, DeleteForeverDialog);
        Microsoft.UI.Xaml.Media.Animation.Storyboard.SetTargetProperty(da, "Opacity");
        sb.Children.Add(da);
        Motion.AddDialogShowTransform(sb, DeleteForeverDialogTransform);
        sb.Begin();

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
        DeleteForeverCd.Reset();
        DeleteForeverOverlay.Visibility = Visibility.Collapsed;
        _pendingDeleteForever = null;
    }

    private void ShowClearAllDialog()
    {
        ClearAllDangerIcon.Data = App.CreateGeometry(IconData.Danger);

        ClearAllDialog.Opacity = 0;
        ClearAllDialogTransform.ScaleX = 0.94; ClearAllDialogTransform.ScaleY = 0.94; ClearAllDialogTransform.TranslateY = 24;
        ClearAllScrim.Opacity = 0;
        ClearAllOverlay.Visibility = Visibility.Visible;
        var sb = new Microsoft.UI.Xaml.Media.Animation.Storyboard();
        var sa = new Microsoft.UI.Xaml.Media.Animation.DoubleAnimation { To = 1, Duration = TimeSpan.FromMilliseconds(250) };
        Microsoft.UI.Xaml.Media.Animation.Storyboard.SetTarget(sa, ClearAllScrim);
        Microsoft.UI.Xaml.Media.Animation.Storyboard.SetTargetProperty(sa, "Opacity");
        sb.Children.Add(sa);
        var da = new Microsoft.UI.Xaml.Media.Animation.DoubleAnimation { To = 1, Duration = TimeSpan.FromMilliseconds(300) };
        Microsoft.UI.Xaml.Media.Animation.Storyboard.SetTarget(da, ClearAllDialog);
        Microsoft.UI.Xaml.Media.Animation.Storyboard.SetTargetProperty(da, "Opacity");
        sb.Children.Add(da);
        Motion.AddDialogShowTransform(sb, ClearAllDialogTransform);
        sb.Begin();

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
        ClearAllCd.Reset();
        ClearAllOverlay.Visibility = Visibility.Collapsed;
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
