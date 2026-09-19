using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Foundation;
using Novara.Models;
using Novara.Services;
using WinRT.Interop;
using Windows.Storage.Pickers;

namespace Novara.Pages;

public sealed partial class FilePathPage : Page
{
    private readonly HashSet<Border> _starredCards = new();
    private Border? _pinnedCard;
    private readonly Dictionary<Border, Viewbox> _pinIcons = new();
    private readonly Dictionary<Border, Viewbox> _starIcons = new();
    private readonly Dictionary<Border, Windows.UI.Color> _cardBaseBorderColor = new();
    private Border? _pendingDeleteCard;
    private Border? _editingPathCard;

    private string _editPathOrigName = "";
    private string _editPathOrigPath = "";
    private string _editPathOrigNote = "";
    private readonly Dictionary<Border, (string name, string path, string note)> _pathCardData = new();

    private readonly Dictionary<Border, DateTime> _createdAt = new();

    private readonly Dictionary<Border, Guid> _cardIds = new();
    private bool _storeLoaded;
    private Novara.Models.NovaraDatabase? _loadedDb;
    private bool _confirming;
    private bool _entrancePlayed;
    private bool _renderInProgress;
    private System.Threading.CancellationTokenSource? _renderCts;
    private bool _persistAfterRender;
    private DispatcherTimer? _autoCheckTimer;
    private readonly Dictionary<TextBox, System.Threading.CancellationTokenSource> _flashCtsMap = new();
    private readonly Dictionary<TextBox, Microsoft.UI.Xaml.Media.Brush> _flashOriginalBgs = new();
    private readonly Dictionary<Border, Button> _openButtons = new();
    private readonly Dictionary<Border, Button> _copyButtons = new();


    private Border? _dragCard;
    private bool _dragging;
    private Point _grabOffset;
        private Image? _dragGhost;
        private Border? _dropIndicator;
        private int _dropIndex;
        private int _dragOriginIndex;
    private Point _lastPointerInRoot;
    private int _autoScrollDir;
    private int _autoScrollFrame;
    private bool _autoScrollActive;

    public FilePathPage()
    {
        InitializeComponent();
        Novara.Services.DialogDepth.AttachContainer((Grid)Content);
        PickFilePathIcon.Data = App.CreateGeometry(IconData.PickFile);
        PickFolderPathIcon.Data = App.CreateGeometry(IconData.PickFolder);
        PickFileIcon.PointerEntered += (_, _) => PickFileIcon.Opacity = 1.0;
        PickFileIcon.PointerExited += (_, _) => PickFileIcon.Opacity = 0.6;
        PickFolderIcon.PointerEntered += (_, _) => PickFolderIcon.Opacity = 1.0;
        PickFolderIcon.PointerExited += (_, _) => PickFolderIcon.Opacity = 0.6;
        KeyDown += Page_KeyDown;
        ContentRoot.SizeChanged += (_, _) => { if (NewPathOverlay.Visibility == Visibility.Visible) DialogUi.ClampDialogHeight(NewPathDialog, ContentRoot); };
        Loaded += (_, _) => { LoadFromStore();
        CheckAllPaths(); if (_autoCheckTimer == null) { _autoCheckTimer = new DispatcherTimer { Interval = TimeSpan.FromMinutes(30) }; _autoCheckTimer.Tick += (_, _) => CheckAllPaths(); _autoCheckTimer.Start(); } };
        Unloaded += (_, _) => { _pathCheckCts?.Cancel();





            _renderCts?.Cancel();
            _autoCheckTimer?.Stop(); _autoCheckTimer = null; NewPathOverlay.Visibility = Visibility.Collapsed; DeleteConfirmOverlay.Visibility = Visibility.Collapsed; _pendingDeleteCard = null; _editingPathCard = null; _deleteConfirming = false;
         foreach (var cts in _flashCtsMap.Values) { cts.Cancel(); cts.Dispose(); } _flashCtsMap.Clear();

            if (_dragCard != null && _cardBaseBorderColor.TryGetValue(_dragCard, out var baseColor)) { _dragCard.BorderBrush = new SolidColorBrush(baseColor); _dragCard.BorderThickness = new Thickness(1); _dragCard.Opacity = 1; _dragCard.RenderTransform = new TranslateTransform(); }
            _dragging = false; _dragCard = null; if (_dragGhost != null) { DragLayer.Children.Remove(_dragGhost); _dragGhost = null; } if (_dropIndicator != null) { PathList.Children.Remove(_dropIndicator); _dropIndicator = null; } StopAutoScroll(); DialogDepth.VeilClear(); };
    }


    public void StealFocus() => FocusSink.Focus(FocusState.Programmatic);

    private async void LoadFromStore()
    {


        if (_storeLoaded) return;
        _loadedDb = App.Store?.Database;
        EmptyHint.Visibility = Visibility.Collapsed;
        var db = App.Store?.Database;
        if (db == null) return;

        var entries = db.PathBackupItems.Where(x => !x.IsDeleted).ToList();




        var pinnedRows = entries.Where(x => x.IsPinned).ToList();
        if (pinnedRows.Count > 1)
        {
            for (int i = 1; i < pinnedRows.Count; i++) pinnedRows[i].IsPinned = false;
            _ = App.Store?.SaveAsync();
        }

        bool playEntrance = !_entrancePlayed;
        int entIdx = 0;


        _renderInProgress = true;



        ResetCardState();
        _renderCts?.Cancel();
        var renderCts = _renderCts = new System.Threading.CancellationTokenSource();
        try
        {
            await ChunkedRender.RunAsync(entries.Count, 8, DispatcherQueue, (s, e) =>
            {
                for (int i = s; i < e; i++)
                {
                    var entry = entries[i];
                    var card = BuildPathCard(entry.Name, entry.Path, entry.Note, i);
                    _cardIds[card] = entry.Id;
                    _createdAt[card] = entry.CreatedAt;
                    if (entry.IsStarred) { _starredCards.Add(card); if (_starIcons.TryGetValue(card, out var si)) si.Visibility = Visibility.Visible; }
                    if (entry.IsPinned && _pinnedCard == null) { _pinnedCard = card; if (_pinIcons.TryGetValue(card, out var pi)) pi.Visibility = Visibility.Visible; }
                    PathList.Children.Add(card);
                    if (playEntrance && entIdx < 10) App.PlayCardEntrance(card, entIdx); entIdx++;
                }
            }, renderCts.Token);
        }
        catch (OperationCanceledException) { return; }
        catch (Exception ex)
        {




            System.Diagnostics.Debug.WriteLine($"路径页列表渲染失败（页面将被重建）: {ex}");
            App.MainWindow?.RetireTabPage(typeof(FilePathPage));
            return;
        }





        finally
        {
            renderCts.Dispose();
            if (ReferenceEquals(_renderCts, renderCts)) { _renderCts = null; _renderInProgress = false; }
        }

        _storeLoaded = true;
        _entrancePlayed = true;
        ReorderCards();
        ApplyCardFilters();
        CheckAllPaths();
        if (_persistAfterRender) { _persistAfterRender = false; PersistOrderAndSave(); }
    }





    private void ResetCardState()
    {
        PathList.Children.Clear();
        _cardIds.Clear();
        _createdAt.Clear();
        _pathCardData.Clear();
        _cardBaseBorderColor.Clear();
        _starIcons.Clear();
        _pinIcons.Clear();
        _openButtons.Clear();
        _copyButtons.Clear();
        _pathStatusGens.Clear();
        _starredCards.Clear();
        _pinnedCard = null;
    }

    private void PersistOrderAndSave()
    {
        if (_renderInProgress) { _persistAfterRender = true; return; }
        if (_loadedDb != null && !ReferenceEquals(_loadedDb, App.Store?.Database)) return;
        var db = App.Store?.Database;
        if (db == null) return;
        var softDeleted = db.PathBackupItems.Where(x => x.IsDeleted).ToList();
        var byId = db.PathBackupItems.ToDictionary(x => x.Id);
        var ordered = new List<FilePathEntry>();
        foreach (var child in PathList.Children)
            if (child is Border b && _cardIds.TryGetValue(b, out var id) && byId.TryGetValue(id, out var e))
                ordered.Add(e);


        var pathKnownIds = ordered.Select(x => x.Id).Concat(softDeleted.Select(x => x.Id)).ToHashSet();
        var pathOrphans = byId.Values.Where(x => !x.IsDeleted && !pathKnownIds.Contains(x.Id)).ToList();


        var pathSeen = ordered.Select(x => x.Id).ToHashSet();
        db.PathBackupItems.Clear();
        db.PathBackupItems.AddRange(ordered);
        db.PathBackupItems.AddRange(softDeleted.Where(x => !pathSeen.Contains(x.Id)));
        db.PathBackupItems.AddRange(pathOrphans);
        App.Store?.SaveAsync();
    }

    private void SyncPinEntity()
    {
        var db = App.Store?.Database;
        if (db == null) return;
        Guid? pinnedId = _pinnedCard != null && _cardIds.TryGetValue(_pinnedCard, out var pid) ? pid : null;
        foreach (var e in db.PathBackupItems) e.IsPinned = e.Id == pinnedId;
    }

    private void SyncStarEntity(Border card)
    {
        if (!_cardIds.TryGetValue(card, out var id)) return;
        var e = App.Store?.Database.PathBackupItems.FirstOrDefault(x => x.Id == id);
        if (e != null) e.IsStarred = _starredCards.Contains(card);
    }

    private void UpdateEmptyHint()
    {
        bool anyVisible = false;
        foreach (var c in PathList.Children) if (c.Visibility == Visibility.Visible) { anyVisible = true; break; }
        if (anyVisible) { EmptyHint.Visibility = Visibility.Collapsed; return; }
        EmptyHint.Text = !string.IsNullOrEmpty(App.CurrentWorkspaceId) ? App.GetString("Workspace_EmptyHint") : App.GetString("Path_Empty_Tip");
        EmptyHint.Visibility = Visibility.Visible;
        FloatInHint();
    }


    public void ApplyCardFilters()
    {
        string wsId = App.CurrentWorkspaceId;
        bool wsActive = !string.IsNullOrEmpty(wsId);
        var db = App.Store?.Database;


        Dictionary<Guid, string>? wsById = null;
        if (wsActive && db != null)
        {
            wsById = new Dictionary<Guid, string>();
            foreach (var p in db.PathBackupItems) wsById[p.Id] = p.WorkspaceId ?? "";
        }
        foreach (var child in PathList.Children)
        {
            if (child is not Border b) continue;
            bool wsMatch = true;

            if (wsById != null && _cardIds.TryGetValue(b, out var id))
                wsMatch = wsById.TryGetValue(id, out var cw) && cw == wsId;
            b.Visibility = wsMatch ? Visibility.Visible : Visibility.Collapsed;
        }
        UpdateEmptyHint();
    }

    private void FloatInHint()
    {
        EmptyHint.Opacity = 0;
        if (EmptyHint.RenderTransform is not TranslateTransform tt)
        {
            tt = new TranslateTransform { Y = 20 };
            EmptyHint.RenderTransform = tt;
        }
        else tt.Y = 20;
        var sb = new Storyboard();
        var oa = new DoubleAnimation { To = 0.6, Duration = TimeSpan.FromMilliseconds(400), EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
        Storyboard.SetTarget(oa, EmptyHint); Storyboard.SetTargetProperty(oa, "Opacity");
        sb.Children.Add(oa);
        var ya = new DoubleAnimation { To = 0, Duration = TimeSpan.FromMilliseconds(400), EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
        Storyboard.SetTarget(ya, tt); Storyboard.SetTargetProperty(ya, "Y");
        sb.Children.Add(ya);
        sb.Begin();
    }






    public bool FlashPathById(Guid id)
    {
        foreach (var (card, cid) in _cardIds)
        {
            if (cid != id) continue;
            try
            {
                var t = card.TransformToVisual(PathScrollViewer);
                var pt = t.TransformPoint(new Windows.Foundation.Point(0, 0));
                PathScrollViewer.ChangeView(null, System.Math.Max(0, PathScrollViewer.VerticalOffset + pt.Y - 20), null, false);
            }
            catch { }
            App.FlashCard(card);
            return true;
        }
        return false;
    }


    public void OpenNewPathDialogWith(string path)
    {
        ShowNewPathDialog();
        NewPathInputBox.Text = path;
    }

    private void ShowNewPathDialog(bool isEdit = false)
    {        _confirming = false;
        if (!isEdit)
        {
            _editingPathCard = null;
            NewPathNameBox.Text = "";
            NewPathInputBox.Text = "";
            NewPathNoteBox.Text = "";
            NewPathDialogTitle.Text = App.GetString("Path_New_Title");
        }
        NewPathDialogTransform.ScaleX = 0.94; NewPathDialogTransform.ScaleY = 0.94; NewPathDialogTransform.TranslateY = 24;
        NewPathDialog.Opacity = 0;
        NewPathScrim.Opacity = 0;
        NewPathOverlay.Visibility = Visibility.Visible;

        DialogUi.ClampDialogHeight(NewPathDialog, ContentRoot);
        DialogDepth.VeilShow();
        UpdateNewPathConfirmState();

        var sb = new Storyboard();
        var scrimIn = new DoubleAnimation { To = 1, Duration = TimeSpan.FromMilliseconds(250) };
        Storyboard.SetTarget(scrimIn, NewPathScrim);
        Storyboard.SetTargetProperty(scrimIn, "Opacity");
        sb.Children.Add(scrimIn);
        var dialogIn = new DoubleAnimation { To = 1, Duration = TimeSpan.FromMilliseconds(300) };
        Storyboard.SetTarget(dialogIn, NewPathDialog);
        Storyboard.SetTargetProperty(dialogIn, "Opacity");
        sb.Children.Add(dialogIn);
        var sx = new DoubleAnimation { To = 1, Duration = TimeSpan.FromMilliseconds(400), EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
        Storyboard.SetTarget(sx, NewPathDialogTransform);
        Storyboard.SetTargetProperty(sx, "ScaleX");
        sb.Children.Add(sx);
        var sy = new DoubleAnimation { To = 1, Duration = TimeSpan.FromMilliseconds(400), EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
        Storyboard.SetTarget(sy, NewPathDialogTransform);
        Storyboard.SetTargetProperty(sy, "ScaleY");
        sb.Children.Add(sy);
        var su = new DoubleAnimation { To = 0, Duration = TimeSpan.FromMilliseconds(400), EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
        Storyboard.SetTarget(su, NewPathDialogTransform);
        Storyboard.SetTargetProperty(su, "TranslateY");
        sb.Children.Add(su);
        sb.Begin();
    }

    private void HideNewPathDialog()
    {
        _editingPathCard = null;
        var sb = new Storyboard();
        var scrimOut = new DoubleAnimation { To = 0, Duration = TimeSpan.FromMilliseconds(200) };
        Storyboard.SetTarget(scrimOut, NewPathScrim);
        Storyboard.SetTargetProperty(scrimOut, "Opacity");
        sb.Children.Add(scrimOut);
        var dialogOut = new DoubleAnimation { To = 0, Duration = TimeSpan.FromMilliseconds(200) };
        Storyboard.SetTarget(dialogOut, NewPathDialog);
        Storyboard.SetTargetProperty(dialogOut, "Opacity");
        sb.Children.Add(dialogOut);
        var sx = new DoubleAnimation { To = 0.92, Duration = TimeSpan.FromMilliseconds(250), EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
        Storyboard.SetTarget(sx, NewPathDialogTransform);
        Storyboard.SetTargetProperty(sx, "ScaleX");
        sb.Children.Add(sx);
        var sy = new DoubleAnimation { To = 0.92, Duration = TimeSpan.FromMilliseconds(250), EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
        Storyboard.SetTarget(sy, NewPathDialogTransform);
        Storyboard.SetTargetProperty(sy, "ScaleY");
        sb.Children.Add(sy);
        var sd = new DoubleAnimation { To = 20, Duration = TimeSpan.FromMilliseconds(250), EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
        Storyboard.SetTarget(sd, NewPathDialogTransform);
        Storyboard.SetTargetProperty(sd, "TranslateY");
        sb.Children.Add(sd);
        sb.Completed -= OnNewPathHideCompleted;
        sb.Completed += OnNewPathHideCompleted;
        DialogDepth.VeilHide();
        sb.Begin();
    }

    private void OnNewPathHideCompleted(object? sender, object e) { NewPathOverlay.Visibility = Visibility.Collapsed; }

    private void CloseNewPathDialog_Click(object sender, RoutedEventArgs e) => HideNewPathDialog();
    private void NewPathScrim_Tapped(object sender, TappedRoutedEventArgs e)
    {
        if (ReferenceEquals(e.OriginalSource, NewPathScrim)) HideNewPathDialog();
    }

    private async void PickFileButton_Click(object sender, RoutedEventArgs e)
    {
        var picker = new FileOpenPicker();
        var hwnd = WindowNative.GetWindowHandle(App.MainWindow);
        InitializeWithWindow.Initialize(picker, hwnd);
        picker.FileTypeFilter.Add("*");
        var file = await picker.PickSingleFileAsync();
        if (file != null) NewPathInputBox.Text = file.Path;
    }

    private async void PickFolderButton_Click(object sender, RoutedEventArgs e)
    {
        var picker = new FolderPicker();
        var hwnd = WindowNative.GetWindowHandle(App.MainWindow);
        InitializeWithWindow.Initialize(picker, hwnd);
        picker.SuggestedStartLocation = PickerLocationId.Desktop;
        var folder = await picker.PickSingleFolderAsync();
        if (folder != null) NewPathInputBox.Text = folder.Path;
    }



    private System.Threading.CancellationTokenSource? _npCheckCts;

    private async void UpdateNewPathConfirmState()
    {
        string name = NewPathNameBox.Text.Trim();
        string path = NewPathInputBox.Text.Trim().Trim('"');
        bool valid = !string.IsNullOrWhiteSpace(name) && !string.IsNullOrWhiteSpace(path);






        _npCheckCts?.Cancel();
        _npCheckCts?.Dispose();
        _npCheckCts = null;
        if (_editingPathCard == null && valid)
        {
            var cts = _npCheckCts = new System.Threading.CancellationTokenSource();
            var exists = false;
            try
            {
                exists = await System.Threading.Tasks.Task.Run(() =>
                {
                    try { return Directory.Exists(path) || File.Exists(path); }
                    catch { return false; }
                });
            }
            catch (System.OperationCanceledException) { return; }
            if (cts.IsCancellationRequested) return;
            valid = exists;
        }

        if (valid && _editingPathCard != null && !PathContentChanged())
            valid = false;
        NewPathConfirmButton.IsEnabled = valid;
    }


    private bool PathContentChanged()
    {
        if (NewPathNameBox.Text.Trim() != _editPathOrigName) return true;
        if (NewPathInputBox.Text.Trim().Trim('"') != _editPathOrigPath) return true;
        if (NewPathNoteBox.Text.Trim() != _editPathOrigNote) return true;
        return false;
    }

    private void NewPathField_TextChanged(object sender, TextChangedEventArgs e)
    {
        UpdateNewPathConfirmState();
    }

    private void NewPathConfirm_Click(object sender, RoutedEventArgs e)
    {
        if (_confirming) return; _confirming = true;
        bool isEdit = _editingPathCard != null;
        var name = NewPathNameBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            _confirming = false;
            FlashTextBox(NewPathNameBox);
            return;
        }

        var path = NewPathInputBox.Text.Trim().Trim('"');


        if (string.IsNullOrWhiteSpace(path) || (!isEdit && !Directory.Exists(path) && !File.Exists(path)))
        {
            _confirming = false;
            FlashTextBox(NewPathInputBox);
            return;
        }
        var note = NewPathNoteBox.Text.Trim();
        Border card;
        if (_editingPathCard != null)
        {
            var old = _editingPathCard;
            var oldCreated = _createdAt.TryGetValue(old, out var oc) ? oc : DateTime.Now;
            var idx = PathList.Children.IndexOf(old);
            card = BuildPathCard(name, path, note, idx >= 0 ? Math.Min(idx, 9) : 0);
            _createdAt[card] = oldCreated;
            if (idx >= 0)
                PathList.Children[idx] = card;
            else
                PathList.Children.Insert(0, card);
            App.PlayCardEntrance(card);

            bool wasStarred = _starredCards.Remove(old);
            if (wasStarred) { _starredCards.Add(card); if (_starIcons.TryGetValue(card, out var si)) si.Visibility = Visibility.Visible; }
            if (_pinnedCard == old) { _pinnedCard = card; if (_pinIcons.TryGetValue(card, out var np)) np.Visibility = Visibility.Visible; }

            if (_cardIds.TryGetValue(old, out var editId))
            {
                _cardIds[card] = editId;
                var editEntry = App.Store?.Database.PathBackupItems.FirstOrDefault(x => x.Id == editId);
                if (editEntry != null)
                {
                    editEntry.Name = name;
                    editEntry.Path = path;
                    editEntry.Note = note;
                }
            }
            _cardIds.Remove(old);
            _pinIcons.Remove(old);
            _starIcons.Remove(old);
            _pathCardData.Remove(old);
            _pathStatusGens.Remove(old);
            _cardBaseBorderColor.Remove(old);
            _openButtons.Remove(old);
            _copyButtons.Remove(old);
            _createdAt.Remove(old);
            _editingPathCard = null;
        }
        else
        {
            card = BuildPathCard(name, path, note, PathList.Children.Count);

            var entry = new FilePathEntry { Name = name, Path = path, Note = note, CreatedAt = DateTime.Now, WorkspaceId = App.CurrentWorkspaceId };
            App.Store?.Database.PathBackupItems.Add(entry);
            _cardIds[card] = entry.Id;
            PathList.Children.Insert(0, card);
            App.PlayCardEntrance(card);
            ReorderCards();
        }
        ApplyCardFilters();

        if (!string.IsNullOrWhiteSpace(path)) { ClaimPathStatusGen(card); SetPathStatus(card, Directory.Exists(path) || File.Exists(path)); }
        PersistOrderAndSave();
        App.ShowToast(App.GetString(isEdit ? "Common_Toast_Modified" : "Common_Toast_Created"));
        HideNewPathDialog();
    }

    private Border BuildPathCard(string name, string path, string note, int index)
    {
        var cv = Microsoft.UI.Xaml.Markup.XamlBindingHelper.ConvertValue;

        var card = new Border
        {
            CornerRadius = new CornerRadius(12),
            Background = App.GetBrush("AppSurfaceOverlayBrush"),
            BorderBrush = new SolidColorBrush(App.GetBrush("AppBorderBrush").Color),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(20, 12, 20, 12),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            RenderTransform = new TranslateTransform { Y = 30 }
        };

        var mainGrid = new Grid();
        mainGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8, GridUnitType.Star) });
        mainGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        mainGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2, GridUnitType.Star) });

        var leftGrid = new Grid();
        leftGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        leftGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        leftGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        leftGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        var titleRow = new Grid();
        titleRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        titleRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var titleText = new TextBlock
        {
            Text = name,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Center,
            FontSize = 15,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            CharacterSpacing = App.GetCharacterSpacing(name, 180),
            Foreground = App.GetBrush("AppTextPrimaryBrush"),
            TextTrimming = TextTrimming.CharacterEllipsis
        };
        Grid.SetColumn(titleText, 0);
        titleRow.Children.Add(titleText);

        var iconStack = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center,
            Spacing = 8,
            Margin = new Thickness(0, 0, 16, 0)
        };
        Grid.SetColumn(iconStack, 1);
        titleRow.Children.Add(iconStack);

        var pinIcon = new Viewbox
        {
            Width = 16, Height = 16,
            Stretch = Microsoft.UI.Xaml.Media.Stretch.Uniform,
            VerticalAlignment = VerticalAlignment.Center,
            Visibility = Visibility.Collapsed,
            Child = new PathIcon
            {
                Data = (Geometry)cv(typeof(Geometry), IconData.CardPin),
                Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(0xFF, 0x72, 0x76, 0xFF))
            }
        };
        var starIcon = new Viewbox
        {
            Width = 16, Height = 16,
            Stretch = Microsoft.UI.Xaml.Media.Stretch.Uniform,
            VerticalAlignment = VerticalAlignment.Center,
            Visibility = Visibility.Collapsed,
            Child = new PathIcon
            {
                Data = (Geometry)cv(typeof(Geometry), IconData.CardStar),
                Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(0xFF, 0x72, 0x76, 0xFF))
            }
        };
        iconStack.Children.Add(starIcon);
        iconStack.Children.Add(pinIcon);
        _pinIcons[card] = pinIcon;
        _starIcons[card] = starIcon;

        Grid.SetRow(titleRow, 0);
        leftGrid.Children.Add(titleRow);

        var pathText = new TextBlock
        {
            Text = path,
            HorizontalAlignment = HorizontalAlignment.Left,
            FontSize = 12,
            FontWeight = Microsoft.UI.Text.FontWeights.Light,
            CharacterSpacing = App.GetCharacterSpacing(path, 60),
            Foreground = App.GetBrush("AppTextSecondaryBrush"),
            TextTrimming = TextTrimming.CharacterEllipsis,

            Margin = new Thickness(0, 4, 0, 0)
        };
        Grid.SetRow(pathText, 1);
        leftGrid.Children.Add(pathText);

        var noteText = new TextBlock
        {
            Text = note,
            HorizontalAlignment = HorizontalAlignment.Left,
            FontSize = 12,
            FontWeight = Microsoft.UI.Text.FontWeights.Light,
            CharacterSpacing = App.GetCharacterSpacing(note, 60),
            Foreground = App.GetBrush("AppTextTertiaryBrush"),
            TextTrimming = TextTrimming.CharacterEllipsis,

            Margin = new Thickness(0, 4, 0, 0)
        };
        Grid.SetRow(noteText, 2);
        leftGrid.Children.Add(noteText);

        Grid.SetColumn(leftGrid, 0);
        mainGrid.Children.Add(leftGrid);

        var separator = new Border
        {
            Width = 2,
            VerticalAlignment = VerticalAlignment.Stretch,
            Margin = new Thickness(12, 16, 12, 16),
            Background = App.GetBrush("AppBorderBrush"),
            CornerRadius = new CornerRadius(1)
        };
        Grid.SetColumn(separator, 1);
        mainGrid.Children.Add(separator);

        var rightPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Spacing = 10
        };

        var openBtn = new Button
        {
            Width = 44, Height = 44,
            Style = (Style)Application.Current.Resources["NovaraCardActionButtonStyle"],
            CornerRadius = new CornerRadius(12),
            IsTabStop = false
        };
        openBtn.Content = new Viewbox
        {
            Width = 20, Height = 20,
            Stretch = Microsoft.UI.Xaml.Media.Stretch.Uniform,
            Child = new PathIcon
            {
                Data = (Geometry)cv(typeof(Geometry), IconData.OpenFolder),
                Foreground = App.GetBrush("IconForegroundBrush")
            }
        };
        openBtn.Click += (_, _) =>
        {
            if (Directory.Exists(path))
            {
                try
                {
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo { FileName = path, UseShellExecute = true });
                }
                catch (System.ComponentModel.Win32Exception)
                {


                    App.ShowToast(App.GetString("OpenFile_Fail"));
                }
            }
            else
            {
                try
                {
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo { FileName = "explorer.exe", Arguments = $"/select,\"{path}\"", UseShellExecute = true });
                }
                catch (System.ComponentModel.Win32Exception ex)
                {
                    App.ShowToast(App.GetString("OpenFile_Fail"));
                    System.Diagnostics.Debug.WriteLine($"explorer /select failed: {ex.Message}");
                }
            }
        };
        rightPanel.Children.Add(openBtn);

        var copyBtn = new Button
        {
            Width = 44, Height = 44,
            Style = (Style)Application.Current.Resources["NovaraCardActionButtonStyle"],
            CornerRadius = new CornerRadius(12),
            IsTabStop = false
        };
        copyBtn.Content = new Viewbox
        {
            Width = 20, Height = 20,
            Stretch = Microsoft.UI.Xaml.Media.Stretch.Uniform,
            Child = new PathIcon
            {
                Data = (Geometry)cv(typeof(Geometry), IconData.CopyPath),
                Foreground = App.GetBrush("IconForegroundBrush")
            }
        };
        copyBtn.Click += (_, _) =>
        {
            var pkg = new Windows.ApplicationModel.DataTransfer.DataPackage();
            pkg.SetText(path);


            try { Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(pkg); App.ShowToast(App.GetString("Common_Toast_Copied")); }
            catch { App.ShowToast(App.GetString("Common_Toast_CopyFail")); }
        };
        rightPanel.Children.Add(copyBtn);


        _openButtons[card] = openBtn;
        _copyButtons[card] = copyBtn;
        HookCardActionButtonHover(openBtn, card);
        HookCardActionButtonHover(copyBtn, card);

        Grid.SetColumn(rightPanel, 2);
        mainGrid.Children.Add(rightPanel);

        card.Child = mainGrid;
        AttachCardHoverEffect(card);
        AttachCardDrag(card);
        AttachCardContextMenu(card, pinIcon, starIcon);

        _pathCardData[card] = (name, path, note);
        _createdAt[card] = DateTime.Now;
        return card;
    }

    private async void FlashTextBox(TextBox tb)
    {
        if (_flashCtsMap.TryGetValue(tb, out var prev)) { prev.Cancel(); prev.Dispose(); }
        var cts = new System.Threading.CancellationTokenSource();
        _flashCtsMap[tb] = cts;

        var orig = _flashOriginalBgs.TryGetValue(tb, out var existing) ? existing : tb.Background;
        _flashOriginalBgs[tb] = orig;
        tb.Background = new SolidColorBrush(Windows.UI.Color.FromArgb(0x33, 0xFF, 0x45, 0x45));
        try { await System.Threading.Tasks.Task.Delay(600, cts.Token); }
        catch (System.Threading.Tasks.TaskCanceledException)
        {


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





    private void ShowDeleteConfirmDialog()
    {
        DeleteConfirmDialogTransform.ScaleX = 0.94; DeleteConfirmDialogTransform.ScaleY = 0.94; DeleteConfirmDialogTransform.TranslateY = 24;
        DeleteConfirmDialog.Opacity = 0;
        DeleteConfirmScrim.Opacity = 0;
        DeleteConfirmOverlay.Visibility = Visibility.Visible;
        DialogDepth.VeilShow();

        var sb = new Storyboard();
        var scrimIn = new DoubleAnimation { To = 1, Duration = TimeSpan.FromMilliseconds(250) };
        Storyboard.SetTarget(scrimIn, DeleteConfirmScrim);
        Storyboard.SetTargetProperty(scrimIn, "Opacity");
        sb.Children.Add(scrimIn);
        var dialogIn = new DoubleAnimation { To = 1, Duration = TimeSpan.FromMilliseconds(300) };
        Storyboard.SetTarget(dialogIn, DeleteConfirmDialog);
        Storyboard.SetTargetProperty(dialogIn, "Opacity");
        sb.Children.Add(dialogIn);
        var sx = new DoubleAnimation { To = 1, Duration = TimeSpan.FromMilliseconds(400), EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
        Storyboard.SetTarget(sx, DeleteConfirmDialogTransform);
        Storyboard.SetTargetProperty(sx, "ScaleX");
        sb.Children.Add(sx);
        var sy = new DoubleAnimation { To = 1, Duration = TimeSpan.FromMilliseconds(400), EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
        Storyboard.SetTarget(sy, DeleteConfirmDialogTransform);
        Storyboard.SetTargetProperty(sy, "ScaleY");
        sb.Children.Add(sy);
        var su = new DoubleAnimation { To = 0, Duration = TimeSpan.FromMilliseconds(400), EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
        Storyboard.SetTarget(su, DeleteConfirmDialogTransform);
        Storyboard.SetTargetProperty(su, "TranslateY");
        sb.Children.Add(su);
        sb.Begin();
    }

    private void HideDeleteConfirmDialog()
    {
        var sb = new Storyboard();
        var scrimOut = new DoubleAnimation { To = 0, Duration = TimeSpan.FromMilliseconds(200) };
        Storyboard.SetTarget(scrimOut, DeleteConfirmScrim);
        Storyboard.SetTargetProperty(scrimOut, "Opacity");
        sb.Children.Add(scrimOut);
        var dialogOut = new DoubleAnimation { To = 0, Duration = TimeSpan.FromMilliseconds(200) };
        Storyboard.SetTarget(dialogOut, DeleteConfirmDialog);
        Storyboard.SetTargetProperty(dialogOut, "Opacity");
        sb.Children.Add(dialogOut);
        var sx = new DoubleAnimation { To = 0.92, Duration = TimeSpan.FromMilliseconds(250), EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
        Storyboard.SetTarget(sx, DeleteConfirmDialogTransform);
        Storyboard.SetTargetProperty(sx, "ScaleX");
        sb.Children.Add(sx);
        var sy = new DoubleAnimation { To = 0.92, Duration = TimeSpan.FromMilliseconds(250), EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
        Storyboard.SetTarget(sy, DeleteConfirmDialogTransform);
        Storyboard.SetTargetProperty(sy, "ScaleY");
        sb.Children.Add(sy);
        var sd = new DoubleAnimation { To = 20, Duration = TimeSpan.FromMilliseconds(250), EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
        Storyboard.SetTarget(sd, DeleteConfirmDialogTransform);
        Storyboard.SetTargetProperty(sd, "TranslateY");
        sb.Children.Add(sd);
        sb.Completed -= OnDeleteConfirmHideCompleted;
        sb.Completed += OnDeleteConfirmHideCompleted;
        DialogDepth.VeilHide();
        sb.Begin();
    }

    private void OnDeleteConfirmHideCompleted(object? sender, object e) { DeleteConfirmOverlay.Visibility = Visibility.Collapsed; _pendingDeleteCard = null; _deleteConfirming = false; }

    private void DeleteConfirmClose_Click(object sender, RoutedEventArgs e) => HideDeleteConfirmDialog();
    private void DeleteCancel_Click(object sender, RoutedEventArgs e) => HideDeleteConfirmDialog();

    private void DeleteConfirmScrim_Tapped(object sender, TappedRoutedEventArgs e)
    {
        if (ReferenceEquals(e.OriginalSource, DeleteConfirmScrim)) HideDeleteConfirmDialog();
    }

    private bool _deleteConfirming;

    private void DeleteConfirmButton_Click(object sender, RoutedEventArgs e)
    {
        if (_pendingDeleteCard == null) return;
        if (_deleteConfirming) return;
        _deleteConfirming = true;
        {
            _starredCards.Remove(_pendingDeleteCard);
            if (_pinnedCard == _pendingDeleteCard) { _pinnedCard = null; SyncPinEntity(); }
            _pinIcons.Remove(_pendingDeleteCard);
            _starIcons.Remove(_pendingDeleteCard);
            _pathCardData.Remove(_pendingDeleteCard);
            _pathStatusGens.Remove(_pendingDeleteCard);
            _cardBaseBorderColor.Remove(_pendingDeleteCard);
            _openButtons.Remove(_pendingDeleteCard);
            _copyButtons.Remove(_pendingDeleteCard);
            _createdAt.Remove(_pendingDeleteCard);

            if (_cardIds.Remove(_pendingDeleteCard, out var delId))
            {
                var pe = App.Store?.Database.PathBackupItems.FirstOrDefault(x => x.Id == delId);
                if (pe != null) { pe.IsDeleted = true; pe.DeletedAt = DateTime.Now; pe.IsPinned = false; pe.IsStarred = false; }
            }
            var delCard = _pendingDeleteCard;

            App.PlayCardRemoval(PathList, delCard, _dragging, () =>
            {
                PersistOrderAndSave();
                ApplyCardFilters();
                App.ShowToast(App.GetString("Common_Toast_Deleted"));
            });
        }
        HideDeleteConfirmDialog();
    }

    private void ReorderCards()
    {
        var children = PathList.Children.Cast<UIElement>().ToList();
        PathList.Children.Clear();
        if (_pinnedCard != null && children.Contains(_pinnedCard))
        {
            children.Remove(_pinnedCard);
            PathList.Children.Add(_pinnedCard);
        }
        foreach (var c in children) PathList.Children.Add(c);
    }





    private void AttachCardHoverEffect(Border card)
    {
        if (App.GetBrush("AppBorderBrush") is not SolidColorBrush borderBrush) return;
        var baseBorderColor = borderBrush.Color;
        _cardBaseBorderColor[card] = baseBorderColor;

        card.BorderBrush = new SolidColorBrush(baseBorderColor);
        card.PointerEntered += (_, _) =>
        {
            if (_dragging) return;

            var currentBase = _cardBaseBorderColor.TryGetValue(card, out var c) ? c : baseBorderColor;
            var hoverBorderColor = Windows.UI.Color.FromArgb(
                (byte)Math.Min(currentBase.A + 0x30, 0xFF),
                currentBase.R,
                currentBase.G,
                currentBase.B);
            if (card.BorderBrush is SolidColorBrush sb) sb.Color = hoverBorderColor;
            if (card.RenderTransform is TranslateTransform t) { var la = new DoubleAnimation { To = -3, Duration = TimeSpan.FromMilliseconds(200), EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } }; var st = new Storyboard(); Storyboard.SetTarget(la, t); Storyboard.SetTargetProperty(la, "Y"); st.Children.Add(la); st.Begin(); }
        };
        card.PointerExited += (_, _) =>
        {
            if (_dragging) return;
            var currentBase = _cardBaseBorderColor.TryGetValue(card, out var c) ? c : baseBorderColor;
            if (card.BorderBrush is SolidColorBrush sb) sb.Color = currentBase;
            if (card.RenderTransform is TranslateTransform t) { var la = new DoubleAnimation { To = 0, Duration = TimeSpan.FromMilliseconds(300), EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } }; var st = new Storyboard(); Storyboard.SetTarget(la, t); Storyboard.SetTargetProperty(la, "Y"); st.Children.Add(la); st.Begin(); }
        };
    }



    private void AttachCardDrag(Border card)
    {
        bool armed = false;
        DispatcherTimer? timer = null;
        Point grabOffset = default;

        card.PointerPressed += (s, e) =>
        {
            if (_dragging) return;
            if (ReferenceEquals(_pinnedCard, card)) return;
            if (App.IsDescendantOfButton(e.OriginalSource as Microsoft.UI.Xaml.DependencyObject)) return;
            var pt = e.GetCurrentPoint(card);
            if (pt.Properties.IsRightButtonPressed || !pt.Properties.IsLeftButtonPressed) return;
            grabOffset = pt.Position;
            var pointer = e.Pointer;
            armed = true;

            var rtb = new RenderTargetBitmap();
            var renderOp = rtb.RenderAsync(card);

            timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
            timer.Tick += async (_, _) =>
            {
                timer.Stop(); timer = null;
                if (!armed || _dragging || !card.IsLoaded) { armed = false; return; }
                try { await renderOp; } catch { }
                if (!armed || _dragging || !card.IsLoaded) { armed = false; return; }
                BeginDrag(card, grabOffset, rtb);
                try { card.CapturePointer(pointer); } catch { }
            };
            timer.Start();
        };

        card.PointerMoved += (s, e) =>
        {
            if (_dragging)
            {
                if (ReferenceEquals(_dragCard, card)) UpdateDrag(e);
                return;
            }
            if (armed)
            {
                var pt = e.GetCurrentPoint(card);
                double dx = pt.Position.X - grabOffset.X, dy = pt.Position.Y - grabOffset.Y;
                if (dx * dx + dy * dy > 64) { armed = false; timer?.Stop(); timer = null; }
            }
        };

        card.PointerReleased += (s, e) =>
        {
            if (_dragging && ReferenceEquals(_dragCard, card)) EndDrag();
            armed = false; timer?.Stop(); timer = null;
        };
        card.PointerCanceled += (s, e) =>
        {
            if (_dragging && ReferenceEquals(_dragCard, card)) EndDrag();
            armed = false; timer?.Stop(); timer = null;
        };
        card.PointerCaptureLost += (s, e) =>
        {
            if (_dragging && ReferenceEquals(_dragCard, card)) EndDrag();
            armed = false; timer?.Stop(); timer = null;
        };
    }

    private void BeginDrag(Border card, Point grabOffset, RenderTargetBitmap rtb)
    {
        _dragging = true;
        _dragCard = card;
        _grabOffset = grabOffset;
        _dropIndex = _dragOriginIndex = CountVisibleBeforeIn(card, PathList);

        App.StopCardEntrance(card);
        card.BorderBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(0xFF, 0x72, 0x76, 0xFF));
        card.BorderThickness = new Thickness(2);
        card.Opacity = 0.35;
        card.RenderTransform = new TranslateTransform();

        try
        {
            var ghost = new Image
            {
                Source = rtb,
                Width = card.ActualWidth,
                Height = card.ActualHeight,
                Opacity = 0.92,
            };
            var pos = card.TransformToVisual(ContentRoot).TransformPoint(new Point(0, 0));
            _dragGhost = ghost;
            DragLayer.Children.Add(ghost);
            Canvas.SetLeft(ghost, pos.X);
            Canvas.SetTop(ghost, pos.Y);
        }
        catch { _dragGhost = null; }
    }

    private void UpdateDrag(PointerRoutedEventArgs e)
    {
        if (_dragCard == null) return;
        var pos = e.GetCurrentPoint(ContentRoot).Position;
        _lastPointerInRoot = pos;
        if (_dragGhost != null)
        {
            Canvas.SetLeft(_dragGhost, pos.X - _grabOffset.X);
            Canvas.SetTop(_dragGhost, pos.Y - _grabOffset.Y);
        }
        var ptInList = e.GetCurrentPoint(PathList).Position;
        UpdateDropIndicator(ComputeDropIndex(ptInList));
        HandleAutoScroll(e);
    }

    private int ComputeDropIndex(Point ptInList)
    {
        int count = 0;
        foreach (var child in PathList.Children)
        {
            if (ReferenceEquals(child, _dropIndicator)) continue;

            if (child is FrameworkElement fe && fe.Visibility == Visibility.Visible)
            {
                var top = fe.TransformToVisual(PathList).TransformPoint(new Point(0, 0)).Y;
                if (ptInList.Y < top + fe.ActualHeight / 2) break;
                count++;
            }
        }
        return count;
    }

    private void UpdateDropIndicator(int index)
    {


        index = Math.Max(index, _pinnedCard != null && _pinnedCard.Visibility == Visibility.Visible ? 1 : 0);

        if (index == _dragOriginIndex || index == _dragOriginIndex + 1)
        {
            if (_dropIndicator != null) { PathList.Children.Remove(_dropIndicator); _dropIndicator = null; }
            _dropIndex = _dragOriginIndex;
            return;
        }
        if (_dropIndicator != null) PathList.Children.Remove(_dropIndicator);
        _dropIndicator = CreateDropIndicator();
        PathList.Children.Insert(index, _dropIndicator);
        _dropIndex = index;
    }

    private Border CreateDropIndicator()
    {
        var b = new Border { Height = 16, Background = new SolidColorBrush(Windows.UI.Color.FromArgb(0, 0, 0, 0)) };
        b.Child = new Border
        {
            Height = 2,
            CornerRadius = new CornerRadius(1),
            Background = new SolidColorBrush(Windows.UI.Color.FromArgb(0xFF, 0x72, 0x76, 0xFF)),
            VerticalAlignment = VerticalAlignment.Center,
        };
        return b;
    }

    private void HandleAutoScroll(PointerRoutedEventArgs e)
    {
        var pt = e.GetCurrentPoint(PathScrollViewer).Position;
        double topZone = 40, bottomZone = 40;
        int dir = 0;
        if (pt.Y < topZone) dir = -1;
        else if (pt.Y > PathScrollViewer.ViewportHeight - bottomZone) dir = 1;

        if (dir == 0) { StopAutoScroll(); return; }
        _autoScrollDir = dir;
        StartAutoScroll();
    }

    private void StartAutoScroll()
    {
        if (_autoScrollActive) return;
        _autoScrollActive = true;
        _autoScrollFrame = 0;
        Microsoft.UI.Xaml.Media.CompositionTarget.Rendering += OnAutoScrollRendering;
    }

    private void StopAutoScroll()
    {
        if (!_autoScrollActive) return;
        _autoScrollActive = false;
        Microsoft.UI.Xaml.Media.CompositionTarget.Rendering -= OnAutoScrollRendering;
        _autoScrollDir = 0;
    }

    private void OnAutoScrollRendering(object? sender, object e)
    {
        if (_dragCard == null || _autoScrollDir == 0) { StopAutoScroll(); return; }
        var newOffset = Math.Clamp(PathScrollViewer.VerticalOffset + _autoScrollDir * 8, 0, PathScrollViewer.ScrollableHeight);
        PathScrollViewer.ChangeView(null, newOffset, null, true);

        if (++_autoScrollFrame < 3) return;
        _autoScrollFrame = 0;
        var ptInList = ContentRoot.TransformToVisual(PathList).TransformPoint(_lastPointerInRoot);
        UpdateDropIndicator(ComputeDropIndex(ptInList));
    }

    private void EndDrag()
    {
        if (!_dragging || _dragCard == null) return;
        var card = _dragCard;
        _dragCard = null;
        _dragging = false;

        int dropIndex = _dropIndex;

        if (_dragGhost != null) { DragLayer.Children.Remove(_dragGhost); _dragGhost = null; }
        if (_dropIndicator != null) { PathList.Children.Remove(_dropIndicator); _dropIndicator = null; }
        StopAutoScroll();


        var baseColor = _cardBaseBorderColor.TryGetValue(card, out var bc)
            ? bc
            : (App.GetBrush("AppBorderBrush") is SolidColorBrush ab ? ab.Color : Windows.UI.Color.FromArgb(0x33, 0xFF, 0xFF, 0xFF));
        card.BorderBrush = new SolidColorBrush(baseColor);
        card.BorderThickness = new Thickness(1);
        card.Opacity = 1;



        int curVisible = CountVisibleBeforeIn(card, PathList);
        int dst = dropIndex > curVisible ? dropIndex - 1 : dropIndex;
        if (dst != curVisible)
        {
            PathList.Children.Remove(card);
            PathList.Children.Insert(PhysicalIndexForVisibleSlotIn(dst, PathList), card);

            PersistOrderAndSave();
        }
    }




    private int CountVisibleBeforeIn(FrameworkElement card, Panel container)
    {
        int n = 0;
        foreach (var child in container.Children)
        {
            if (ReferenceEquals(child, card)) break;
            if (ReferenceEquals(child, _dropIndicator)) continue;
            if (child is FrameworkElement fe && fe.Visibility == Visibility.Visible) n++;
        }
        return n;
    }



    private int PhysicalIndexForVisibleSlotIn(int visibleSlot, Panel container)
    {
        int seen = 0;
        var children = container.Children;
        for (int i = 0; i < children.Count; i++)
        {
            if (children[i] is FrameworkElement fe && fe.Visibility == Visibility.Visible)
            {
                if (seen == visibleSlot) return i;
                seen++;
            }
        }
        return children.Count;
    }

    private void SetPathStatus(Border card, bool isValid)
    {
        var color = isValid
            ? Windows.UI.Color.FromArgb(0xFF, 0x4C, 0xAF, 0x50)
            : Windows.UI.Color.FromArgb(0xFF, 0xFF, 0x45, 0x45);
        _cardBaseBorderColor[card] = color;
        card.BorderBrush = new SolidColorBrush(color);


        var btnColor = Windows.UI.Color.FromArgb(0x99, color.R, color.G, color.B);
        if (_openButtons.TryGetValue(card, out var ob))
        {
            ob.BorderBrush = new SolidColorBrush(btnColor);
            ob.IsEnabled = isValid;
        }
        if (_copyButtons.TryGetValue(card, out var cb))
            cb.BorderBrush = new SolidColorBrush(btnColor);
    }


    private void HookCardActionButtonHover(Button btn, Border card)
    {
        btn.PointerEntered += (_, _) =>
        {
            if (!btn.IsEnabled) return;
            if (_cardBaseBorderColor.TryGetValue(card, out var c))
                btn.BorderBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(0xCC, c.R, c.G, c.B));
        };
        btn.PointerExited += (_, _) =>
        {
            if (!btn.IsEnabled) return;
            if (_cardBaseBorderColor.TryGetValue(card, out var c))
                btn.BorderBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(0x99, c.R, c.G, c.B));
        };
    }

    private void AttachCardContextMenu(Border card, Viewbox pinIcon, Viewbox starIcon)
    {
        card.ContextRequested += (s, e) =>
        {
            e.Handled = true;
            var menu = BuildPathCardContextMenu(card, pinIcon, starIcon);
            if (e.TryGetPosition(card, out var pos))
                menu.ShowAt(card, pos);
            else
                menu.ShowAt(card, new Windows.Foundation.Point(0, 0));
        };
    }

    private MenuFlyout BuildPathCardContextMenu(Border card, Viewbox pinIcon, Viewbox starIcon)
    {
        var cv = Microsoft.UI.Xaml.Markup.XamlBindingHelper.ConvertValue;
        var menu = new MenuFlyout();
        menu.MenuFlyoutPresenterStyle = (Style)Application.Current.Resources["GlassMenuFlyoutPresenterStyle"];

        var starItem = new MenuFlyoutItem
        {
            Style = (Style)Application.Current.Resources["GlassMenuFlyoutItemStyle"],
            Text = _starredCards.Contains(card) ? App.GetString("Menu_Unstar") : App.GetString("Menu_Star"),
            Icon = new PathIcon
            {
                Data = (Geometry)cv(typeof(Geometry), _starredCards.Contains(card) ? IconData.Unstar : IconData.Star),
                Foreground = App.GetBrush("IconForegroundBrush")
            }
        };
        starItem.Click += (_, _) =>
        {
            if (_starredCards.Contains(card)) { _starredCards.Remove(card); starIcon.Visibility = Visibility.Collapsed; }
            else { _starredCards.Add(card); starIcon.Visibility = Visibility.Visible; }
            SyncStarEntity(card);
            App.Store?.SaveAsync();
        };

        var pinItem = new MenuFlyoutItem
        {
            Style = (Style)Application.Current.Resources["GlassMenuFlyoutItemStyle"],
            Text = _pinnedCard == card ? App.GetString("Menu_Unpin") : App.GetString("Menu_Pin"),
            Icon = new PathIcon
            {
                Data = (Geometry)cv(typeof(Geometry), _pinnedCard == card ? IconData.Unpin : IconData.Pin),
                Foreground = App.GetBrush("IconForegroundBrush")
            }
        };
        pinItem.Click += (_, _) =>
        {
            if (_pinnedCard == card) { _pinnedCard = null; pinIcon.Visibility = Visibility.Collapsed; }
            else { if (_pinnedCard != null && _pinIcons.TryGetValue(_pinnedCard, out var oldPi)) oldPi.Visibility = Visibility.Collapsed; _pinnedCard = card; pinIcon.Visibility = Visibility.Visible; }
            SyncPinEntity();
            ReorderCards();
            PersistOrderAndSave();
        };

        var checkItem = new MenuFlyoutItem
        {
            Style = (Style)Application.Current.Resources["GlassMenuFlyoutItemStyle"],
            Text = App.GetString("Menu_DetectPath"),
            Icon = new PathIcon
            {
                Data = (Geometry)cv(typeof(Geometry), IconData.RefreshPaths),
                Foreground = App.GetBrush("IconForegroundBrush")
            }
        };
        checkItem.Click += (_, _) =>
        {
            if (!_pathCardData.TryGetValue(card, out var data) || string.IsNullOrWhiteSpace(data.path)) return;
            var isValid = Directory.Exists(data.path) || File.Exists(data.path);
            ClaimPathStatusGen(card);
            SetPathStatus(card, isValid);
            App.ShowToast(App.GetString("Common_Toast_Detected"));
        };

        var editItem = new MenuFlyoutItem
        {
            Style = (Style)Application.Current.Resources["GlassMenuFlyoutItemStyle"],
            Text = App.GetString("Menu_Edit"),
            Icon = new PathIcon
            {
                Data = MakeGroup(IconData.Edit),
                Foreground = App.GetBrush("IconForegroundBrush")
            }
        };
        editItem.Click += (_, _) =>
        {
            if (!_pathCardData.TryGetValue(card, out var data)) return;
            _editingPathCard = card;
            NewPathDialogTitle.Text = App.GetString("Path_Edit_Title");
            NewPathNameBox.Text = data.name;
            NewPathInputBox.Text = data.path;
            NewPathNoteBox.Text = data.note;
            _editPathOrigName = data.name;
            _editPathOrigPath = data.path.Trim('"');
            _editPathOrigNote = data.note;
            ShowNewPathDialog(true);
        };

        var sep = new MenuFlyoutSeparator();

        var deleteItem = new MenuFlyoutItem
        {
            Style = (Style)Application.Current.Resources["GlassMenuFlyoutItemStyle"],
            Text = App.GetString("Menu_Delete"),
            Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(0xFF, 0xFF, 0x45, 0x45)),
            Icon = new PathIcon
            {
                Data = App.CreateGeometry(IconData.SoftDelete),
                Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(0xFF, 0xFF, 0x45, 0x45))
            }
        };
        deleteItem.Click += (_, _) =>
        {
            _pendingDeleteCard = card;
            ShowDeleteConfirmDialog();
        };

        menu.Items.Add(pinItem);
        menu.Items.Add(starItem);
        menu.Items.Add(checkItem);
        menu.Items.Add(editItem);
        menu.Items.Add(sep);
        menu.Items.Add(deleteItem);
        return menu;
    }





    private MenuFlyout BuildEmptyAreaMenu()
    {
        var cv = Microsoft.UI.Xaml.Markup.XamlBindingHelper.ConvertValue;
        var menu = new MenuFlyout();
        menu.MenuFlyoutPresenterStyle = (Style)Application.Current.Resources["GlassMenuFlyoutPresenterStyle"];

        var newItem = new MenuFlyoutItem
        {
            Style = (Style)Application.Current.Resources["GlassMenuFlyoutItemStyle"],
            Text = App.GetString("Menu_New_Path"),
            Icon = new PathIcon
            {
                Data = App.CreateGeometry(IconData.NavFile),
                Foreground = App.GetBrush("IconForegroundBrush")
            }
        };
        newItem.Click += (_, _) => ShowNewPathDialog();

        var checkItem = new MenuFlyoutItem
        {
            Style = (Style)Application.Current.Resources["GlassMenuFlyoutItemStyle"],
            Text = App.GetString("Menu_DetectPath"),
            Icon = new PathIcon
            {
                Data = (Geometry)cv(typeof(Geometry), IconData.RefreshPaths),
                Foreground = App.GetBrush("IconForegroundBrush")
            }
        };

        checkItem.Click += (_, _) => { CheckAllPaths(); App.ShowToast(App.GetString("Common_Toast_Detected")); };

        menu.Items.Add(newItem);
        menu.Items.Add(checkItem);
        return menu;
    }

    private System.Threading.CancellationTokenSource? _pathCheckCts;




    private long _pathStatusGenSeq;
    private readonly Dictionary<Border, long> _pathStatusGens = new();

    private long ClaimPathStatusGen(Border card)
    {
        var gen = ++_pathStatusGenSeq;
        _pathStatusGens[card] = gen;
        return gen;
    }

    private bool IsPathStatusCurrent(Border card, long gen)
        => _pathStatusGens.TryGetValue(card, out var current) && current == gen;

    private async void CheckAllPaths()
    {


        _pathCheckCts?.Cancel();
        _pathCheckCts?.Dispose();
        var cts = _pathCheckCts = new System.Threading.CancellationTokenSource();
        var ct = cts.Token;

        var targets = new List<(Border Card, string Path, long Gen)>();
        foreach (var child in PathList.Children)
            if (child is Border card && card.Visibility == Visibility.Visible
                && _pathCardData.TryGetValue(card, out var data) && !string.IsNullOrWhiteSpace(data.path))
                targets.Add((card, data.path, ClaimPathStatusGen(card)));
        if (targets.Count == 0) return;

        var results = new List<(Border Card, bool IsValid, long Gen)>();
        try
        {
            await System.Threading.Tasks.Task.Run(() =>
            {
                foreach (var (card, path, gen) in targets)
                {
                    if (ct.IsCancellationRequested) return;
                    bool isValid;
                    try { isValid = Directory.Exists(path) || File.Exists(path); }
                    catch { isValid = false; }
                    results.Add((card, isValid, gen));
                }
            });
        }
        catch (System.OperationCanceledException) { return; }
        if (ct.IsCancellationRequested) return;
        foreach (var (card, isValid, gen) in results)

            if (PathList.Children.Contains(card) && IsPathStatusCurrent(card, gen)) SetPathStatus(card, isValid);
    }

    private GeometryGroup MakeGroup(string[] paths)
    {
        var cv = Microsoft.UI.Xaml.Markup.XamlBindingHelper.ConvertValue;
        var group = new GeometryGroup();
        foreach (var p in paths)
            group.Children.Add((Geometry)cv(typeof(Geometry), p));
        return group;
    }

    private void Page_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key != Windows.System.VirtualKey.Escape) return;
        if (DeleteConfirmOverlay.Visibility == Visibility.Visible) { HideDeleteConfirmDialog(); e.Handled = true; return; }
        if (NewPathOverlay.Visibility == Visibility.Visible) { HideNewPathDialog(); e.Handled = true; return; }
    }

    private void RootGrid_ContextRequested(UIElement sender, ContextRequestedEventArgs args)
    {
        var menu = BuildEmptyAreaMenu();

        if (!args.TryGetPosition(sender, out var point)) point = new Windows.Foundation.Point(0, 0);
        menu.ShowAt(sender, point);
    }
}
