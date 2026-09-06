/* ========== PlanPage - Plan Tab ==========
Function: Todo & note card management - check state linkage (sub-all-checked auto-checks main), collapse/expand rules, edit rebuild migration, star/pin ordering
Corresponding UI: PlanPage.xaml.cs
Logic Range: Whole file business logic of this module
*/
using System.Collections.Generic;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Shapes;
using Windows.Foundation;
using Windows.UI;
using Novara.Models;
using Novara.Services;

namespace Novara.Pages;

public sealed partial class PlanPage : Page
{
    private string? _selectedIconKey;
    private string? _noteSelectedIconKey;
    private readonly System.Random _iconRandom = new();
    private readonly Dictionary<TextBox, System.Threading.CancellationTokenSource> _flashCtsMap = new(); // N5P-02: per-box CTS (E5-24 pattern)
    private readonly Dictionary<TextBox, Microsoft.UI.Xaml.Media.Brush> _flashOriginalBgs = new();
    private readonly HashSet<Border> _starredCards = new();
    private readonly HashSet<Border> _pinnedCards = new();
    private Border? _pendingDeleteCard;
    private readonly Dictionary<Border, DateTime> _createdAt = new();
    private readonly Dictionary<Border, FrameworkElement> _pinIcons = new();
    private readonly Dictionary<Border, FrameworkElement> _starIcons = new();
    private Border? _editingTodoCard;
    private Border? _editingNoteCard;
    
    private string _editTodoOrigName = "";
    private string _editTodoOrigIcon = "";
    private string _editTodoOrigMain = "";
    private List<string> _editTodoOrigSubs = new();
    private string _editNoteOrigName = "";
    private string _editNoteOrigIcon = "";
    private string _editNoteOrigContent = "";
    private Border? _pendingReminderCard; 
    private readonly HashSet<Border> _reminderCards = new(); 
    private readonly Dictionary<Border, (string title, string iconKey, string mainText, List<string> subTexts, List<bool> checkedStates)> _todoData = new();
    private readonly Dictionary<Border, bool> _todoCollapsed = new();
    private readonly Dictionary<Border, Viewbox> _todoCompletedBadges = new(); 
    private readonly Dictionary<Border, (string title, string iconKey, string content)> _noteData = new();

    private readonly Dictionary<Border, bool> _noteExpanded = new();

    private readonly Dictionary<Border, Guid> _cardIds = new();
    private bool _storeLoaded;
    private Novara.Models.NovaraDatabase? _loadedDb; // N5M-04
    private bool _bulkLoading; // #28: skip per-card reorder during bulk load; reorder once at end
    private bool _renderInProgress; // N5P-01: true while the chunked fill is still adding cards to the UI
    private bool _persistAfterRender; // N5P-01: a persistence request arrived during the fill window - rerun it after the fill
    private string _currentFilter = "all"; 
    private bool _confirming; // E3-10: one-shot guard for the create/edit confirm buttons - double-click during the hide animation used to create duplicate items
    private bool _entrancePlayed; // E4-16: play the entrance animation only once (page instances are cached by MainWindow)

    private readonly Dictionary<Border, Button> _cardExpandBtns = new();
    private readonly Dictionary<Border, StackPanel> _todoRowPanels = new();

    
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

    public PlanPage() { InitializeComponent();
        Novara.Services.DialogDepth.AttachContainer((Grid)Content); // dialog depth: shadow + chrome veil auto-wiring
        
        DeleteConfirmTitleText.Text = App.GetString("Dialog_Delete_Title");
        DeleteCancelText.Text = App.GetString("Common_Button_Cancel");
        DeleteConfirmText.Text = App.GetString("Common_Button_Delete");
        KeyDown += Page_KeyDown; // ND1: was accidentally swallowed into the comment line during the snapshot re-insertion - Esc close was dead on this page
        // N4-29: past date/time keeps the reminder dialog's confirm disabled (schtasks would silently fail)
        SetReminderDatePicker.DateChanged += (_, _) => UpdateSetReminderConfirmState();
        SetReminderTimePicker.TimeChanged += (_, _) => UpdateSetReminderConfirmState();
        Loaded += (_, _) => { LoadFromStore(); RefreshAllReminderBorders(); }; // E4-16 + N4P-02: reload is idempotent (_storeLoaded); the explicit refresh restarts the 30s timer Unloaded stopped and surfaces reminders that came due while the page was hidden
        Unloaded += (_, _) => { NewTodoOverlay.Visibility = Visibility.Collapsed; NewNoteOverlay.Visibility = Visibility.Collapsed; DeleteConfirmOverlay.Visibility = Visibility.Collapsed; ReminderOverlay.Visibility = Visibility.Collapsed; // E3-19: reminder dialog was the only overlay not cleaned on tab switch (M4)
        // ND6: fold the remaining three overlays too - an open scrim left behind desynced from the chrome veil after a tab switch.
        SetReminderScrim.Opacity = 0; CancelReminderScrim.Opacity = 0; ReminderDueScrim.Opacity = 0;
        
        // skipping it left the reminder swallowed forever (_dueShown kept the card, fields stayed
        // set, the gradient border froze red).
        if (ReminderDueOverlay.Visibility == Visibility.Visible && _dueReminderCard != null)
        {
            ClearReminderOnCard(_dueReminderCard);
            _dueReminderCard = null;
        }
        SetReminderOverlay.Visibility = Visibility.Collapsed; CancelReminderOverlay.Visibility = Visibility.Collapsed; ReminderDueOverlay.Visibility = Visibility.Collapsed;
        DialogDepth.VeilClear(); 
        _editingTodoCard = null; _editingNoteCard = null; _pendingDeleteCard = null; _editingReminderId = null; foreach (var cts in _flashCtsMap.Values) { cts.Cancel(); cts.Dispose(); } _flashCtsMap.Clear(); if (_dragCard != null) { _dragCard.BorderBrush = new SolidColorBrush(App.GetBrush("AppBorderBrush").Color); _dragCard.BorderThickness = new Thickness(1); _dragCard.Opacity = 1; } _dragging = false; _dragCard = null; if (_dragGhost != null) { DragLayer.Children.Remove(_dragGhost); _dragGhost = null; } if (_dropIndicator != null) { CardList.Children.Remove(_dropIndicator); _dropIndicator = null; } StopAutoScroll();
        _reminderTimer?.Stop(); _reminderTimer = null; // N4P-02: the 30s tick must not run while the page is off-screen - a due reminder would VeilShow the window-level chrome scrim with no visible dialog (reload path restarts it via RefreshAllReminderBorders on Loaded); null so StartReminderRefresh can recreate it
        }; }

    
    public void StealFocus() => FocusSink.Focus(FocusState.Programmatic);

    private void Page_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key != Windows.System.VirtualKey.Escape) return;
        if (DeleteConfirmOverlay.Visibility == Visibility.Visible) { HideDeleteConfirmDialog(); e.Handled = true; return; }
        if (ReminderOverlay.Visibility == Visibility.Visible) { HideReminderDialog(); e.Handled = true; return; }
        if (NewNoteOverlay.Visibility == Visibility.Visible) { HideNewNoteDialog(); e.Handled = true; return; }
        if (NewTodoOverlay.Visibility == Visibility.Visible) { HideNewTodoDialog(); e.Handled = true; return; }
        // N4P-07 (legacy N4 round leftovers): the three reminder-family dialogs never joined the
        // Esc chain. Due-dialog hide carries its own teardown (ClearReminderOnCard), same as close/confirm.
        if (SetReminderOverlay.Visibility == Visibility.Visible) { HideSetReminderDialog(); e.Handled = true; return; }
        if (CancelReminderOverlay.Visibility == Visibility.Visible) { HideCancelReminderDialog(); e.Handled = true; return; }
        if (ReminderDueOverlay.Visibility == Visibility.Visible) { HideReminderDueDialog(); e.Handled = true; return; }
    }

    private async void LoadFromStore()
    {
        if (_storeLoaded) return;
        _loadedDb = App.Store?.Database; // N5M-04: capture the db this UI build belongs to (arms the stale-db persist guard below)
        _storeLoaded = true;
        _bulkLoading = true; // #28: skip full reorder during bulk build (O(N2) -> one reorder at end)
        EmptyHint.Visibility = Visibility.Collapsed; // P0-1: hide until chunked render decides the real empty state
        var db = App.Store?.Database;
        if (db == null) { _bulkLoading = false; return; }

        var todos = db.TodoCards.Where(x => !x.IsDeleted).ToList();
        var notes = db.NoteCards.Where(x => !x.IsDeleted).ToList();

        
        
        
        
        bool manual = db.TodoCards.Any(x => x.Order > 0) || db.NoteCards.Any(x => x.Order > 0); 
        var all = new List<(bool IsTodo, Guid Id, bool IsPinned, int Order, DateTime CreatedAt)>();
        foreach (var t in todos) all.Add((true, t.Id, t.IsPinned, t.Order, t.CreatedAt));
        foreach (var n in notes) all.Add((false, n.Id, n.IsPinned, n.Order, n.CreatedAt));

        var pinnedItems = all.Where(x => x.IsPinned).OrderByDescending(x => x.CreatedAt);
        IOrderedEnumerable<(bool IsTodo, Guid Id, bool IsPinned, int Order, DateTime CreatedAt)> unpinnedItems;
        if (manual)
            unpinnedItems = all.Where(x => !x.IsPinned)
                .OrderBy(x => x.Order > 0 ? x.Order : int.MaxValue)
                .ThenByDescending(x => x.CreatedAt);
        else
            unpinnedItems = all.Where(x => !x.IsPinned).OrderByDescending(x => x.CreatedAt);
        var sorted = pinnedItems.Concat(unpinnedItems).ToList();
        var todoById = todos.ToDictionary(x => x.Id);
        var noteById = notes.ToDictionary(x => x.Id);

        bool playEntrance = !_entrancePlayed; // P0-1: only the first load plays the cascade
        int entIdx = 0;

        // N5P-01: guard the fill window - PersistOrderAndSave during it rebuilds db from the partial UI
        _renderInProgress = true;
        try
        {
            await ChunkedRender.RunAsync(sorted.Count, 8, DispatcherQueue, (s, e) =>
            {
                for (int i = s; i < e; i++)
                {
                    var it = sorted[i];
                    if (it.IsTodo)
                    {
                        var t = todoById[it.Id];
                        var card = BuildTodoCard(t.Title, t.IconKey, t.MainText, t.SubTexts, t.CheckedStates);
                        _cardIds[card] = t.Id;
                        _createdAt[card] = t.CreatedAt;
                        if (t.IsStarred) { _starredCards.Add(card); if (_starIcons.TryGetValue(card, out var si)) si.Visibility = Visibility.Visible; }
                        if (t.IsPinned) { _pinnedCards.Add(card); if (_pinIcons.TryGetValue(card, out var pi)) pi.Visibility = Visibility.Visible; }
                        if (playEntrance && entIdx < 10) App.PlayCardEntrance(card, entIdx);
                    }
                    else
                    {
                        var n = noteById[it.Id];
                        var card = BuildNoteCard(n.Title, n.IconKey, n.Content, n.IsExpanded);
                        _cardIds[card] = n.Id;
                        _createdAt[card] = n.CreatedAt;
                        if (n.IsStarred) { _starredCards.Add(card); if (_starIcons.TryGetValue(card, out var si)) si.Visibility = Visibility.Visible; }
                        if (n.IsPinned) { _pinnedCards.Add(card); if (_pinIcons.TryGetValue(card, out var pi)) pi.Visibility = Visibility.Visible; }
                        if (playEntrance && entIdx < 10) App.PlayCardEntrance(card, entIdx);
                    }
                    entIdx++;
                }
            });
        }
        finally { _renderInProgress = false; }

        _entrancePlayed = true; // P0-1: cascade already played per-card above
        _bulkLoading = false;
        ReorderCards(); 
        ApplyCardFilters(); 
        RefreshAllReminderBorders(); 
        if (_persistAfterRender) { _persistAfterRender = false; PersistOrderAndSave(); } // N5P-01: run the deferred rebuild now that every entry has a card
    }

    private void PersistOrderAndSave()
    {
        if (_renderInProgress) { _persistAfterRender = true; return; }
        if (_loadedDb != null && !ReferenceEquals(_loadedDb, App.Store?.Database)) return; // N5M-04: stale page after import/reset - never rebuild the new db from old UI // N5P-01: defer - rebuilding db from a partially filled UI would permanently drop not-yet-rendered entries
        var db = App.Store?.Database;
        if (db == null) return;
        var softDeletedTodos = db.TodoCards.Where(x => x.IsDeleted).ToList(); // keep trash items across the rebuild
        var softDeletedNotes = db.NoteCards.Where(x => x.IsDeleted).ToList();
        var todoById = db.TodoCards.ToDictionary(x => x.Id);
        var noteById = db.NoteCards.ToDictionary(x => x.Id);
        var todos = new List<TodoCard>();
        var notes = new List<NoteCard>();
        foreach (var child in CardList.Children)
        {
            if (child is Border b && _cardIds.TryGetValue(b, out var id))
            {
                if (todoById.TryGetValue(id, out var t)) todos.Add(t);
                else if (noteById.TryGetValue(id, out var n)) notes.Add(n);
            }
        }
        // N2-01: MCP writes land directly in the db without UI cards - keep db entries that are
        // neither on a card nor soft-deleted across the rebuild (P0, same as the memo page).
        var todoKnownIds = todos.Select(x => x.Id).Concat(softDeletedTodos.Select(x => x.Id)).ToHashSet();
        var todoOrphans = todoById.Values.Where(x => !x.IsDeleted && !todoKnownIds.Contains(x.Id)).ToList();
        var noteKnownIds = notes.Select(x => x.Id).Concat(softDeletedNotes.Select(x => x.Id)).ToHashSet();
        var noteOrphans = noteById.Values.Where(x => !x.IsDeleted && !noteKnownIds.Contains(x.Id)).ToList();
        // N2-16: an entry can be both on a card AND soft-deleted (MCP deleted it without a UI
        // refresh) - it must land in the db exactly once.
        var todoSeen = todos.Select(x => x.Id).ToHashSet();
        var noteSeen = notes.Select(x => x.Id).ToHashSet();
        db.TodoCards.Clear();
        db.TodoCards.AddRange(todos);
        db.TodoCards.AddRange(softDeletedTodos.Where(x => !todoSeen.Contains(x.Id))); // keep soft-deleted items (trash)
        db.TodoCards.AddRange(todoOrphans);
        db.NoteCards.Clear();
        db.NoteCards.AddRange(notes);
        db.NoteCards.AddRange(softDeletedNotes.Where(x => !noteSeen.Contains(x.Id)));
        db.NoteCards.AddRange(noteOrphans);
        App.Store?.SaveAsync();
    }

    
    private void AssignNewCardOrder(TodoCard? todo, NoteCard? note)
    {
        var db = App.Store?.Database;
        if (db == null) return;
        bool manual = db.TodoCards.Any(x => x.Order > 0) || db.NoteCards.Any(x => x.Order > 0);
        if (!manual) return;
        // N2-41 (N1-33 parity): only cards in the ACTIVE workspace and not soft-deleted take part -
        // bumping other workspaces' (and trashed) cards polluted their order space.
        var wsId = App.CurrentWorkspaceId;
        bool inView(string? wid) => string.IsNullOrEmpty(wsId) || wid == wsId;
        foreach (var t in db.TodoCards) if (!t.IsPinned && !t.IsDeleted && inView(t.WorkspaceId) && t.Order > 0) t.Order++;
        foreach (var n in db.NoteCards) if (!n.IsPinned && !n.IsDeleted && inView(n.WorkspaceId) && n.Order > 0) n.Order++;
        if (todo != null) todo.Order = 1;
        if (note != null) note.Order = 1;
    }

    private void SyncCardStar(Border card, bool starred)
    {
        if (!_cardIds.TryGetValue(card, out var id)) return;
        var db = App.Store?.Database;
        if (db == null) return;
        if (card.Tag is string tg && tg == "note")
        {
            var n = db.NoteCards.FirstOrDefault(x => x.Id == id);
            if (n != null) n.IsStarred = starred;
        }
        else
        {
            var t = db.TodoCards.FirstOrDefault(x => x.Id == id);
            if (t != null) t.IsStarred = starred;
        }
        App.Store?.SaveAsync();
    }

    private void SyncCardPin(Border card, bool pinned)
    {
        if (!_cardIds.TryGetValue(card, out var id)) return;
        var db = App.Store?.Database;
        if (db == null) return;
        if (card.Tag is string tg && tg == "note")
        {
            var n = db.NoteCards.FirstOrDefault(x => x.Id == id);
            if (n != null) n.IsPinned = pinned;
        }
        else
        {
            var t = db.TodoCards.FirstOrDefault(x => x.Id == id);
            if (t != null) t.IsPinned = pinned;
        }
        App.Store?.SaveAsync();
    }

    private void UpdateEmptyHint() { bool anyVisible = false; foreach (var c in CardList.Children) if (c.Visibility == Visibility.Visible) { anyVisible = true; break; } if (anyVisible) { EmptyHint.Visibility = Visibility.Collapsed; return; } EmptyHint.Visibility = Visibility.Visible; EmptyHint.Text = !string.IsNullOrEmpty(App.CurrentWorkspaceId) ? App.GetString("Workspace_EmptyHint") : _currentFilter switch { "todo" => App.GetString("Plan_Filter_Todo_Empty"), "note" => App.GetString("Plan_Filter_Note_Empty"), _ => App.GetString("Plan_Empty_Tip") }; FloatInHint(); }

    
    public string GetFilter() => _currentFilter;
    public void SetFilter(string filter)
    {
        _currentFilter = filter;
        ApplyCardFilters();
    }

    
    public void ApplyCardFilters()
    {
        string wsId = App.CurrentWorkspaceId;
        bool wsActive = !string.IsNullOrEmpty(wsId);
        var db = App.Store?.Database;
        // N3-38: pre-index WorkspaceId per card id ONCE - the per-card FirstOrDefault over the whole
        
        // every toggle re-scanned both lists per child.
        Dictionary<Guid, string>? wsById = null;
        if (wsActive && db != null)
        {
            wsById = new Dictionary<Guid, string>();
            foreach (var t in db.TodoCards) wsById[t.Id] = t.WorkspaceId ?? "";
            foreach (var n in db.NoteCards) if (!wsById.ContainsKey(n.Id)) wsById[n.Id] = n.WorkspaceId ?? "";
        }
        foreach (var child in CardList.Children)
        {
            if (child is not Border b) continue;
            bool isNote = b.Tag is string tg && tg == "note";
            bool typeMatch = _currentFilter == "all" || (_currentFilter == "note" && isNote) || (_currentFilter == "todo" && !isNote);
            bool wsMatch = true;
            // N3-38: dict lookup replaces the per-card double FirstOrDefault. Missing id => hidden,
            // matching the original (t == null && n == null -> cw == null -> false under a ws filter).
            if (wsById != null && _cardIds.TryGetValue(b, out var id))
                wsMatch = wsById.TryGetValue(id, out var cw) && cw == wsId;
            b.Visibility = (typeMatch && wsMatch) ? Visibility.Visible : Visibility.Collapsed;
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

    private void RootGrid_ContextRequested(UIElement s, ContextRequestedEventArgs a) { var m = BuildEmptyAreaMenu(); if (a.TryGetPosition(s, out var p)) m.ShowAt(s, p); else m.ShowAt(s, new Point(0, 0)); }
    private MenuFlyout BuildEmptyAreaMenu()
    {
        var m = new MenuFlyout { MenuFlyoutPresenterStyle = (Style)Application.Current.Resources["GlassMenuFlyoutPresenterStyle"] };
        var t = new MenuFlyoutItem { Style = (Style)Application.Current.Resources["GlassMenuFlyoutItemStyle"], Text = App.GetString("Menu_New_Todo"), Icon = new PathIcon { Data = MakeGroup(IconData.Todo), Foreground = App.GetBrush("IconForegroundBrush") } }; t.Click += (_, _) => ShowNewItemDialog(App.GetString("Plan_Todo_NewTitle"));
        var n = new MenuFlyoutItem { Style = (Style)Application.Current.Resources["GlassMenuFlyoutItemStyle"], Text = App.GetString("Menu_New_Note"), Icon = new PathIcon { Data = MakeGroup(IconData.Note), Foreground = App.GetBrush("IconForegroundBrush") } }; n.Click += (_, _) => ShowNewNoteDialog();
        var r = new MenuFlyoutItem { Style = (Style)Application.Current.Resources["GlassMenuFlyoutItemStyle"], Text = App.GetString("Menu_AddReminder"), Icon = new PathIcon { Data = App.CreateGeometry(IconData.Reminder), Foreground = App.GetBrush("IconForegroundBrush") } }; r.Click += (_, _) => ShowNewReminderDialog();
        var closeAll = new MenuFlyoutItem { Style = (Style)Application.Current.Resources["GlassMenuFlyoutItemStyle"], Text = App.GetString("Menu_CloseAllDesktop"), Icon = new PathIcon { Data = App.CreateGeometry(IconData.CancelDesktop), Foreground = App.GetBrush("IconForegroundBrush") } }; closeAll.Click += (_, _) => { Services.StickySync.ClearAllNotes(); App.ShowToast(App.GetString("Common_Toast_Recalled")); };
        m.Items.Add(t); m.Items.Add(n); m.Items.Add(r);
        m.Items.Add(new MenuFlyoutSeparator());
        m.Items.Add(closeAll);
        return m;
    }

    private string? _editingReminderId;

    private void ShowNewReminderDialog()
    {
        _editingReminderId = null;
        ReminderContentBox.Text = "";
        var target = DateTime.Now.AddHours(1); 
        ReminderDatePicker.Date = new DateTimeOffset(target.Date);
        ReminderTimePicker.Time = new TimeSpan(target.Hour, 0, 0);
        ReminderDialogTitle.Text = App.GetString("Reminder_New_Title");
        ShowReminderDialogCore();
    }

    /// <summary>Edit mode (desktop reminder card "modify"): pre-fill content and due time.</summary>
    public void OpenReminderEdit(Novara.Services.PendingReminderEdit p)
    {
        _editingReminderId = p.Id;
        ReminderContentBox.Text = p.Content;
        if (DateTimeOffset.TryParse(p.DueTime, out var due))
        {
            ReminderDatePicker.Date = due;
            ReminderTimePicker.Time = TimeSpan.FromHours(due.Hour).Add(TimeSpan.FromMinutes(due.Minute));
        }
        ReminderDialogTitle.Text = App.GetString("Reminder_Edit_Title");
        ShowReminderDialogCore();
    }

    private void ShowReminderDialogCore()
    {
        _confirming = false; // E3-10: re-arm the confirm guard when the dialog re-opens
        UpdateReminderConfirmState(); // UI-2: initial validity (explicit - empty assignment may not raise TextChanged)
        ReminderDialogTransform.ScaleX = 0.94; ReminderDialogTransform.ScaleY = 0.94; ReminderDialogTransform.TranslateY = 24;
        ReminderDialog.Opacity = 0; ReminderScrim.Opacity = 0;
        ReminderOverlay.Visibility = Visibility.Visible; DialogDepth.VeilShow();
        var sb = new Storyboard();
        var si = new DoubleAnimation { To = 1, Duration = TimeSpan.FromMilliseconds(250) };
        Storyboard.SetTarget(si, ReminderScrim); Storyboard.SetTargetProperty(si, "Opacity"); sb.Children.Add(si);
        var di = new DoubleAnimation { To = 1, Duration = TimeSpan.FromMilliseconds(300) };
        Storyboard.SetTarget(di, ReminderDialog); Storyboard.SetTargetProperty(di, "Opacity"); sb.Children.Add(di);
        Motion.AddDialogShowTransform(sb, ReminderDialogTransform); // M1: EmphasizedDecelerate (Motion)
        sb.Begin();
    }

    private void HideReminderDialog()
    {
        var sb = new Storyboard();
        var so = new DoubleAnimation { To = 0, Duration = TimeSpan.FromMilliseconds(200) };
        Storyboard.SetTarget(so, ReminderScrim); Storyboard.SetTargetProperty(so, "Opacity"); sb.Children.Add(so);
        var d = new DoubleAnimation { To = 0, Duration = TimeSpan.FromMilliseconds(200) };
        Storyboard.SetTarget(d, ReminderDialog); Storyboard.SetTargetProperty(d, "Opacity"); sb.Children.Add(d);
        Motion.AddDialogHideTransform(sb, ReminderDialogTransform); // M1: EmphasizedAccelerate (Motion)
        sb.Completed += (_, _) => ReminderOverlay.Visibility = Visibility.Collapsed;
        DialogDepth.VeilHide();
        sb.Begin();
    }

    private void CloseReminderDialog_Click(object s, RoutedEventArgs e) => HideReminderDialog();
    private void ReminderScrim_Tapped(object s, TappedRoutedEventArgs e) { if (ReferenceEquals(e.OriginalSource, ReminderScrim)) HideReminderDialog(); }

    private void ConfirmReminderDialog_Click(object s, RoutedEventArgs e)
    {
        if (_confirming) return; _confirming = true; // E3-10
        bool isEdit = _editingReminderId != null;
        var content = ReminderContentBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(content)) { _confirming = false; FlashTextBox(ReminderContentBox); return; }
        var d = ReminderDatePicker.Date;
        var t = ReminderTimePicker.Time;
        var due = new DateTimeOffset(d.Year, d.Month, d.Day, t.Hours, t.Minutes, 0, TimeZoneInfo.Local.GetUtcOffset(new DateTime(d.Year, d.Month, d.Day)));
        
        if (due.LocalDateTime <= DateTime.Now) { _confirming = false; App.ShowToast(App.GetString("Reminder_Past_Time")); FlashTextBox(ReminderContentBox); return; }
        if (isEdit)
            Services.StickySync.UpdateReminder(_editingReminderId!, content, due);
        else
            Services.StickySync.AddReminder(content, due);
        App.ShowToast(App.GetString(isEdit ? "Common_Toast_Modified" : "Common_Toast_Created"));
        _editingReminderId = null;
        HideReminderDialog();
    }

    public void ShowNewItemDialog(string title) { _confirming = false; _editingTodoCard = null; NewTodoDialogTitle.Text = title; TodoNameBox.Text = ""; MainTodoBox.Text = ""; UpdateTodoConfirmState(); SubTodoPanel.Children.Clear(); SubTodoPanel.Children.Add(new TextBlock{Text=App.GetString("Plan_Todo_SubLabel"),FontSize=12,FontWeight=Microsoft.UI.Text.FontWeights.Medium,Foreground=App.GetBrush("AppTextSecondaryBrush"),Margin=new Thickness(0,0,0,8)}); SubTodoPanel.Children.Add(CreateSubTodoBox()); LoadIconSelector(); NewTodoDialogTransform.ScaleX = 0.94; NewTodoDialogTransform.ScaleY = 0.94; NewTodoDialogTransform.TranslateY = 24; NewTodoDialog.Opacity = 0; NewTodoScrim.Opacity = 0; NewTodoOverlay.Visibility = Visibility.Visible; DialogDepth.VeilShow(); var sb = new Storyboard(); var si = new DoubleAnimation { To = 1, Duration = TimeSpan.FromMilliseconds(250) }; Storyboard.SetTarget(si, NewTodoScrim); Storyboard.SetTargetProperty(si, "Opacity"); sb.Children.Add(si); var di = new DoubleAnimation { To = 1, Duration = TimeSpan.FromMilliseconds(300) }; Storyboard.SetTarget(di, NewTodoDialog); Storyboard.SetTargetProperty(di, "Opacity"); sb.Children.Add(di); Motion.AddDialogShowTransform(sb, NewTodoDialogTransform); Motion.StaggerReset(NewTodoDialog); Motion.StaggerWire(sb, NewTodoDialog); sb.Begin(); }

    private void HideNewTodoDialog() { var sb = new Storyboard(); var so = new DoubleAnimation { To = 0, Duration = TimeSpan.FromMilliseconds(200) }; Storyboard.SetTarget(so, NewTodoScrim); Storyboard.SetTargetProperty(so, "Opacity"); sb.Children.Add(so); var d = new DoubleAnimation { To = 0, Duration = TimeSpan.FromMilliseconds(200) }; Storyboard.SetTarget(d, NewTodoDialog); Storyboard.SetTargetProperty(d, "Opacity"); sb.Children.Add(d); Motion.AddDialogHideTransform(sb, NewTodoDialogTransform); sb.Completed -= OnNewTodoHideCompleted; sb.Completed += OnNewTodoHideCompleted; DialogDepth.VeilHide(); sb.Begin(); }
    private void OnNewTodoHideCompleted(object? sender, object e) { NewTodoOverlay.Visibility = Visibility.Collapsed; _editingTodoCard = null; }

    private void LoadIconSelector() { _selectedIconKey = null; IconPanel.Children.Clear(); foreach (var key in IconData.GroupIconKeysInOrder()) { var b = new Border { Width = 48, Height = 48, CornerRadius = new CornerRadius(12), Background = App.GetBrush("AppSurfaceOverlayBrush"), Tag = key, Child = new Viewbox { Width = 24, Height = 24, Stretch = Microsoft.UI.Xaml.Media.Stretch.Uniform, Child = new PathIcon { Data = App.CreateGeometry(IconData.GetGroupPath(key)), Foreground = App.GetBrush("IconForegroundBrush") } } }; b.Tapped += IconBorder_Tapped; IconPanel.Children.Add(b); } }
    private void IconBorder_Tapped(object s, TappedRoutedEventArgs e) { if (s is Border b && b.Tag is string k) { SelectIcon(k); CenterIconInScrollViewer(b); UpdateTodoConfirmState(); } }
    private void SelectIcon(string key) { _selectedIconKey = key; var d = App.GetBrush("AppSurfaceBrush"); var n = App.GetBrush("AppSurfaceOverlayBrush"); foreach (var c in IconPanel.Children) if (c is Border b && b.Tag is string k) { bool o = k == key; b.Background = o ? d : n; b.BorderBrush = o ? App.GetBrush("AppBorderBrush") : null; b.BorderThickness = new Thickness(o ? 1 : 0); } }
    private void CenterIconInScrollViewer(Border t) { var r = t.TransformToVisual(IconPanel); var p = r.TransformPoint(new Point(0, 0)); double o = p.X - (IconScrollViewer.ViewportWidth / 2) + (t.ActualWidth / 2); o = Math.Max(0, Math.Min(o, IconScrollViewer.ScrollableWidth)); IconScrollViewer.ChangeView(o, null, null, false); }
    private void CloseNewTodo_Click(object s, RoutedEventArgs e) => HideNewTodoDialog();
    private void NewTodoScrim_Tapped(object s, TappedRoutedEventArgs e) { if (ReferenceEquals(e.OriginalSource, NewTodoScrim)) HideNewTodoDialog(); }
    private void AddSubTodoButton_Click(object s, RoutedEventArgs e) => SubTodoPanel.Children.Add(CreateSubTodoBox());
    private TextBox CreateSubTodoBox() { var tb = new TextBox { Style = (Style)Application.Current.Resources["NovaraTextBoxStyle"], MaxLength = 500, PlaceholderText = App.GetString("Plan_Todo_SubPlaceholder") }; tb.TextChanged += PlanField_TextChanged; return tb; }

    // UI-2 (2026-08-11): live validation - confirm buttons stay disabled (grey, no hover) until
    // every required field of the corresponding dialog is non-blank.
    private void UpdateReminderConfirmState()
    {
        ConfirmReminderDialogButton.IsEnabled = !string.IsNullOrWhiteSpace(ReminderContentBox.Text);
    }

    private void UpdateTodoConfirmState()
    {
        bool valid = !string.IsNullOrWhiteSpace(TodoNameBox.Text) && !string.IsNullOrWhiteSpace(MainTodoBox.Text);
        
        if (valid && _editingTodoCard != null && !TodoContentChanged())
            valid = false;
        NewTodoConfirmButton.IsEnabled = valid;
    }

    
    private bool TodoContentChanged()
    {
        if (TodoNameBox.Text.Trim() != _editTodoOrigName) return true;
        if ((_selectedIconKey ?? "") != _editTodoOrigIcon) return true;
        if (MainTodoBox.Text.Trim() != _editTodoOrigMain) return true;
        var cur = new List<string>();
        foreach (var c in SubTodoPanel.Children)
            if (c is TextBox tb && !string.IsNullOrWhiteSpace(tb.Text)) cur.Add(tb.Text.Trim());
        if (cur.Count != _editTodoOrigSubs.Count) return true;
        for (int i = 0; i < cur.Count; i++) if (cur[i] != _editTodoOrigSubs[i]) return true;
        return false;
    }

    private void UpdateNoteConfirmState()
    {
        bool valid = !string.IsNullOrWhiteSpace(NoteNameBox.Text) && !string.IsNullOrWhiteSpace(NoteContentBox.Text);
        
        if (valid && _editingNoteCard != null && !NoteContentChanged())
            valid = false;
        NewNoteConfirmButton.IsEnabled = valid;
    }

    
    private bool NoteContentChanged()
    {
        if (NoteNameBox.Text.Trim() != _editNoteOrigName) return true;
        if ((_noteSelectedIconKey ?? "") != _editNoteOrigIcon) return true;
        if (NoteContentBox.Text.Trim() != _editNoteOrigContent) return true;
        return false;
    }

    private void PlanField_TextChanged(object sender, TextChangedEventArgs e)
    {
        UpdateReminderConfirmState();
        UpdateTodoConfirmState();
        UpdateNoteConfirmState();
    }

    private void NewTodoConfirm_Click(object s, RoutedEventArgs e)
    {
        if (_confirming) return; _confirming = true; // E3-10
        bool isEdit = _editingTodoCard != null;
        var name = TodoNameBox.Text.Trim(); if (string.IsNullOrWhiteSpace(name)) { _confirming = false; FlashTextBox(TodoNameBox); return; }
        var iconKey = _selectedIconKey ?? GetRandomIconKey();
        var mainText = MainTodoBox.Text.Trim(); if (string.IsNullOrWhiteSpace(mainText)) { _confirming = false; FlashTextBox(MainTodoBox); return; }
        var subTexts = new List<string>();
        for (int i = 0; i < SubTodoPanel.Children.Count; i++) { if (SubTodoPanel.Children[i] is TextBox stb) { var t = stb.Text.Trim(); if (!string.IsNullOrWhiteSpace(t)) subTexts.Add(t); } }
        if (_editingTodoCard != null) { var old = _editingTodoCard; bool wasStar = _starredCards.Contains(old); bool wasPin = _pinnedCards.Contains(old); bool wasReminder = _reminderCards.Contains(old); var oldCreated = _createdAt.TryGetValue(old, out var oc) ? oc : DateTime.Now; var oldStates = _todoData.TryGetValue(old, out var od) ? od.checkedStates : null; var oldSubTexts = od.subTexts; var newStates = new List<bool>(); { bool mainKept = oldStates != null && oldStates.Count > 0 && mainText == od.mainText; newStates.Add(mainKept && oldStates![0]); var used = new bool[oldSubTexts?.Count ?? 0]; for (int si = 0; si < subTexts.Count; si++) { bool orig = false; if (oldSubTexts != null && oldStates != null) { for (int oi = 0; oi < oldSubTexts.Count; oi++) { if (!used[oi] && oldSubTexts[oi] == subTexts[si] && oi + 1 < oldStates.Count) { orig = oldStates[oi + 1]; used[oi] = true; break; } } } newStates.Add(orig); } } _starredCards.Remove(old); _pinnedCards.Remove(old); _pinIcons.Remove(old); _starIcons.Remove(old); _todoData.Remove(old); _todoCollapsed.Remove(old); _todoCompletedBadges.Remove(old); _cardExpandBtns.Remove(old); _todoRowPanels.Remove(old); _createdAt.Remove(old); _reminderCards.Remove(old); _dueShown.Remove(old); CardList.Children.Remove(old); _bulkLoading = true; var nc = BuildTodoCard(name, iconKey, mainText, subTexts, newStates); _bulkLoading = false; _createdAt[nc] = oldCreated; if (wasStar) { _starredCards.Add(nc); if (_starIcons.TryGetValue(nc, out var si)) si.Visibility = Visibility.Visible; } if (wasPin) { _pinnedCards.Add(nc); if (_pinIcons.TryGetValue(nc, out var pi)) pi.Visibility = Visibility.Visible; } if (wasReminder) { _reminderCards.Add(nc); } App.PlayCardEntrance(nc);

            if (_cardIds.TryGetValue(old, out var editTid))
            {
                _cardIds[nc] = editTid;
                var te = App.Store?.Database.TodoCards.FirstOrDefault(x => x.Id == editTid);
                if (te != null)
                {
                    te.Title = name; te.IconKey = iconKey; te.MainText = mainText;
                    te.SubTexts = new List<string>(subTexts);
                    if (_todoData.TryGetValue(nc, out var nd2)) te.CheckedStates = new List<bool>(nd2.checkedStates);
                }
                
                if (Services.StickySync.Contains(editTid.ToString()))
                    Services.StickySync.UpdateNote(editTid.ToString(), "todo", name, "", BuildTodoItems(mainText, subTexts, newStates));
                if (wasReminder) RefreshReminderBorder(nc); // N2-25: AFTER the _cardIds assignment - the call reads the map and early-returned when invoked before it
            }
            _cardIds.Remove(old);
            ReorderCards(); 
            PersistOrderAndSave();
        } else {
            // N4P-03: same as the edit branch - build under _bulkLoading (skips the in-build reorder),
            // map the id, THEN reorder. The old order let BuildTodoCard's tail ReorderCards run while the
            // new card was unmapped: its sort key degraded to int.MaxValue and it sank below every card.
            _bulkLoading = true;
            var nc2 = BuildTodoCard(name, iconKey, mainText, subTexts);
            _bulkLoading = false;
            var te2 = new TodoCard { Title = name, IconKey = iconKey, MainText = mainText, SubTexts = new List<string>(subTexts), CheckedStates = _todoData.TryGetValue(nc2, out var nd3) ? new List<bool>(nd3.checkedStates) : new List<bool>(), CreatedAt = DateTime.Now, WorkspaceId = App.CurrentWorkspaceId };
            AssignNewCardOrder(te2, null);
            App.Store?.Database.TodoCards.Add(te2);
            _cardIds[nc2] = te2.Id;
            App.PlayCardEntrance(nc2);
            ReorderCards(); // N4P-03
            PersistOrderAndSave();
            SetFilter(_currentFilter); // N4P-10: a card created under an active filter must respect it immediately (NH4 parity)
        }
        App.ShowToast(App.GetString(isEdit ? "Common_Toast_Modified" : "Common_Toast_Created"));
        HideNewTodoDialog();
    }

    /* ========== PlanPage Card Building ==========
Function: Todo/note card construction, state dictionaries, collapse/expand buttons, bulk-load reorder skip (#28), edit rebuild migration (C2)
Corresponding UI: PlanPage.xaml.cs
Logic Range: Below methods in this region
*/
private Border BuildTodoCard(string title, string iconKey, string mainText, List<string> subTexts, List<bool>? states = null)
    {
        var cv = Microsoft.UI.Xaml.Markup.XamlBindingHelper.ConvertValue;
        var card = new Border { CornerRadius = new CornerRadius(12), Background = App.GetBrush("AppSurfaceOverlayBrush"), BorderBrush = new SolidColorBrush(App.GetBrush("AppBorderBrush").Color), // E5-20: per-card copy - never mutate the shared theme brush (E1-09 pattern)
            BorderThickness = new Thickness(1), Padding = new Thickness(20, 18, 20, 18), HorizontalAlignment = HorizontalAlignment.Stretch, RenderTransform = new TranslateTransform() };
        var root = new Grid(); root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var tr = new Grid(); tr.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); tr.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); tr.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); tr.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); tr.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); tr.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var ib = new Border { Width = 32, Height = 32, CornerRadius = new CornerRadius(8), Background = App.GetBrush("AppSurfaceOverlayBrush"), VerticalAlignment = VerticalAlignment.Center, Child = new Viewbox { Width = 18, Height = 18, Stretch = Microsoft.UI.Xaml.Media.Stretch.Uniform, Child = new PathIcon { Data = App.CreateGeometry(IconData.GetGroupPath(iconKey)), Foreground = App.GetBrush("IconForegroundBrush") } } };
        Grid.SetColumn(ib, 0); tr.Children.Add(ib);
        var tt = new TextBlock { Text = title, FontSize = 18, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, CharacterSpacing = App.GetCharacterSpacing(title, 200), Foreground = App.GetBrush("AppTextPrimaryBrush"), VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(12, 0, 0, 0) }; Grid.SetColumn(tt, 1); tr.Children.Add(tt);
        var pinIcon = new Viewbox { Width = 16, Height = 16, Stretch = Microsoft.UI.Xaml.Media.Stretch.Uniform, VerticalAlignment = VerticalAlignment.Center, Visibility = Visibility.Collapsed, Margin = new Thickness(0, 0, 12, 0), Child = new PathIcon { Data = App.CreateGeometry(IconData.CardPin), Foreground = new SolidColorBrush(Color.FromArgb(0xFF, 0x72, 0x76, 0xFF)) } };
        Grid.SetColumn(pinIcon, 2); tr.Children.Add(pinIcon);
        var starIcon = new Viewbox { Width = 16, Height = 16, Stretch = Microsoft.UI.Xaml.Media.Stretch.Uniform, VerticalAlignment = VerticalAlignment.Center, Visibility = Visibility.Collapsed, Margin = new Thickness(0, 0, 12, 0), Child = new PathIcon { Data = App.CreateGeometry(IconData.CardStar), Foreground = new SolidColorBrush(Color.FromArgb(0xFF, 0x72, 0x76, 0xFF)) } };
        Grid.SetColumn(starIcon, 3); tr.Children.Add(starIcon);
        _pinIcons[card] = pinIcon; _starIcons[card] = starIcon;
        var expandIcon = new PathIcon { Data = (Geometry)cv(typeof(Geometry), IconData.CardCollapse), Foreground = App.GetBrush("IconForegroundBrush") };
        var expandViewbox = new Viewbox { Width = 18, Height = 18, Stretch = Microsoft.UI.Xaml.Media.Stretch.Uniform, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Child = expandIcon };
        // BuildTodoCard expand
        var expandBtn = new Button { Width = 36, Height = 36, Style = (Style)Application.Current.Resources["NovaraIconButtonStyle"], VerticalAlignment = VerticalAlignment.Center, Content = expandViewbox, Visibility = Visibility.Collapsed, IsTabStop = false };
        Grid.SetColumn(expandBtn, 5); tr.Children.Add(expandBtn);
        var doneBadge = BuildTodoDoneBadge();
        Grid.SetColumn(doneBadge, 4); tr.Children.Add(doneBadge);
        _todoCompletedBadges[card] = doneBadge;

        Grid.SetRow(tr, 0); root.Children.Add(tr);
        var lp = new StackPanel { Margin = new Thickness(32, 16, 0, 0) }; Grid.SetRow(lp, 1); root.Children.Add(lp);
        card.Child = root;

        int total = 1 + subTexts.Count;

        var checkedStates = new List<bool>();
        if (states != null)
            for (int i = 0; i < total; i++) checkedStates.Add(i < states.Count && states[i]);
        else
            for (int i = 0; i < total; i++) checkedStates.Add(false);
        if (subTexts.Count > 0) { expandBtn.Visibility = Visibility.Visible; expandBtn.Click += (_, _) => { bool cur = _todoCollapsed.TryGetValue(card, out var c) && c; SetTodoCardCollapsed(card, !cur); }; _cardExpandBtns[card] = expandBtn; }
        _todoRowPanels[card] = lp; _createdAt[card] = DateTime.Now;

        AddDisplayRow(lp, mainText, false, checkedStates, 0, card);
        for (int i = 0; i < subTexts.Count; i++) AddDisplayRow(lp, subTexts[i], true, checkedStates, i + 1, card);
        RefreshCardState(card, checkedStates); // N4P-09: was subTexts-only - a completed no-sub todo lost its badge after restart/edit-rebuild

        card.Tag = "todo"; _todoData[card] = (title, iconKey, mainText, subTexts, checkedStates);
        AttachCardHoverEffect(card);
        AttachCardDrag(card);
        AttachCardContextMenu(card); CardList.Children.Add(card); if (!_bulkLoading) ReorderCards(); UpdateEmptyHint();
        return card;
    }

    private void SetTodoCardCollapsed(Border card, bool collapsed)
    {
        _todoCollapsed[card] = collapsed;
        if (!_todoRowPanels.TryGetValue(card, out var lp)) return;
        for (int i = 1; i < lp.Children.Count; i++) lp.Children[i].Visibility = collapsed ? Visibility.Collapsed : Visibility.Visible;
        // 4.0 #8: collapsed main-todo shows a single ellipsized line; expanded wraps to show everything.
        if (lp.Children.Count > 0 && lp.Children[0] is Grid mainRow && mainRow.Children.Count > 1 && mainRow.Children[1] is Grid tw && tw.Children.Count > 0 && tw.Children[0] is TextBlock tb)
        {
            tb.TextWrapping = collapsed ? TextWrapping.NoWrap : TextWrapping.Wrap;
            tb.TextTrimming = collapsed ? TextTrimming.CharacterEllipsis : TextTrimming.None;
            tb.MaxLines = collapsed ? 1 : 0;
        }
        if (_cardExpandBtns.TryGetValue(card, out var eb) && eb.Content is Viewbox vb && vb.Child is PathIcon pi)
        {
            pi.Data = collapsed ? (Geometry)Microsoft.UI.Xaml.Markup.XamlBindingHelper.ConvertValue(typeof(Geometry), IconData.CardExpand) : (Geometry)Microsoft.UI.Xaml.Markup.XamlBindingHelper.ConvertValue(typeof(Geometry), IconData.CardCollapse);
            vb.Margin = collapsed ? new Thickness(-2, -1, 0, 0) : new Thickness(0);
        }
    }

    /* ========== PlanPage Check Linkage ==========
Function: Sub-all-checked auto-checks main (P3), uncheck sub unchecks main & expands (P4), manual main uncheck keeps subs (P5)
Corresponding UI: PlanPage.xaml.cs
Logic Range: Below methods in this region
*/
private void OnTodoRowCheckedChanged(Border card, List<bool> states, int changedIndex)
    {
        if (changedIndex > 0)
        {
            bool allSubChecked = true;
            for (int i = 1; i < states.Count; i++) if (!states[i]) { allSubChecked = false; break; }
            states[0] = allSubChecked;
            if (_todoRowPanels.TryGetValue(card, out var lp) && lp.Children.Count > 0 && lp.Children[0] is Grid mainRow)
                UpdateRowVisual(mainRow, states[0]);
        }
        RefreshCardState(card, states);

        if (_cardIds.TryGetValue(card, out var cid))
        {
            var t = App.Store?.Database.TodoCards.FirstOrDefault(x => x.Id == cid);
            if (t != null) { t.CheckedStates = new List<bool>(states); App.Store?.SaveAsync(); }
            
            if (Services.StickySync.Contains(cid.ToString()) && _todoData.TryGetValue(card, out var td))
                Services.StickySync.UpdateNote(cid.ToString(), "todo", td.title ?? "", "", BuildTodoItems(td.mainText, td.subTexts, states));
        }
    }

    /// <summary>Reverse sync entry: the host toggled a desktop todo - refresh this card's visual
    /// check states (the store was already updated by StickySync.ApplyTodoChanges). No-op when the
    /// card is not built yet (page not loaded); LoadFromStore will pick up the new states.</summary>
    public void ApplyExternalTodoState(Guid id, List<bool> states)
    {
        Border? card = null;
        foreach (var (c, cid) in _cardIds) if (cid == id) { card = c; break; }
        if (card == null || !_todoData.TryGetValue(card, out var td)) return;
        var cur = td.checkedStates;
        int n = Math.Min(cur.Count, states.Count);
        for (int i = 0; i < n; i++) cur[i] = states[i];
        RefreshTodoRowsVisual(card, cur);
        RefreshCardState(card, cur);
    }

    private void RefreshCardState(Border card, List<bool> states)
    {
        bool allChecked = true;
        foreach (var s in states) if (!s) { allChecked = false; break; }
        SetTodoCardCollapsed(card, allChecked);
        if (_todoCompletedBadges.TryGetValue(card, out var badge))
            badge.Visibility = allChecked ? Visibility.Visible : Visibility.Collapsed; 
    }

    
    private Viewbox BuildTodoDoneBadge()
    {
        var canvas = new Grid { Width = 1024, Height = 1024 };
        Microsoft.UI.Xaml.Shapes.Path MakePath(string d, Color c) => new()
        {
            Data = (Geometry)Microsoft.UI.Xaml.Markup.XamlBindingHelper.ConvertValue(typeof(Geometry), d),
            Fill = new SolidColorBrush(c),
            Stretch = Stretch.None,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
        };
        canvas.Children.Add(MakePath(IconData.TodoDoneFlagLight, Color.FromArgb(0xFF, 0x8C, 0x93, 0xFF))); 
        canvas.Children.Add(MakePath(IconData.TodoDoneFlagDark, Color.FromArgb(0xFF, 0x72, 0x76, 0xFF))); 
        canvas.Children.Add(MakePath(IconData.TodoDoneCheck, Color.FromArgb(0xFF, 0xFF, 0xFF, 0xFF)));
        return new Viewbox { Width = 16, Height = 16, Stretch = Stretch.Uniform, VerticalAlignment = VerticalAlignment.Center, Visibility = Visibility.Collapsed, Margin = new Thickness(0, 0, 12, 0), Child = canvas };
    }

    private void RefreshTodoRowsVisual(Border card, List<bool> states)
    {
        if (!_todoRowPanels.TryGetValue(card, out var lp)) return;
        for (int i = 0; i < lp.Children.Count && i < states.Count; i++)
            if (lp.Children[i] is Grid row) UpdateRowVisual(row, states[i]);
    }

    private void UpdateRowVisual(Grid row, bool chk)
    {
        if (row.Children.Count < 2) return;
        if (row.Children[0] is Border cb && cb.Child is Border checkBox)
        {
            var check = checkBox.Child as Microsoft.UI.Xaml.Shapes.Path;
            if (check != null) check.Visibility = chk ? Visibility.Visible : Visibility.Collapsed;
            checkBox.Background = chk ? App.GetBrush("AppPrimaryButtonBrush") : new SolidColorBrush(Color.FromArgb(0, 0, 0, 0));
            checkBox.BorderBrush = chk ? App.GetBrush("AppPrimaryButtonBrush") : App.GetBrush("AppBorderBrush");
        }
        if (row.Children[1] is Grid tw && tw.Children.Count >= 1 && tw.Children[0] is TextBlock tb)
        {
            tb.Opacity = chk ? 0.45 : 1;
            // 4.0 #8: strikethrough only over the text itself (a stretched Border line would span the whole column).
            tb.TextDecorations = chk ? Windows.UI.Text.TextDecorations.Strikethrough : Windows.UI.Text.TextDecorations.None;
        }
    }

    private Grid AddDisplayRow(StackPanel list, string text, bool indented, List<bool> checkedStates, int index, Border card)
    {
        var cv = Microsoft.UI.Xaml.Markup.XamlBindingHelper.ConvertValue;
        // 4.0 #8: horizontal StackPanel gave the text infinite width so TextWrapping never kicked in.
        // Two-column Grid gives the text a bounded width -> it wraps and stays fully visible when expanded.
        var row = new Grid { ColumnSpacing = 8, Margin = new Thickness(indented ? 24 : 0, 6, 0, 6) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        // 4.0 #8: checkbox - rounded faint border when unchecked, solid brand-blue + white check when checked.
        var checkBox = new Border { Width = 18, Height = 18, CornerRadius = new CornerRadius(4), Background = new SolidColorBrush(Color.FromArgb(0, 0, 0, 0)), BorderBrush = App.GetBrush("AppBorderBrush"), BorderThickness = new Thickness(1.5), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center };
        var cm = new Microsoft.UI.Xaml.Shapes.Path { Data = (Geometry)cv(typeof(Geometry), "M5 11l4 5 10-8"), Stroke = new SolidColorBrush(Color.FromArgb(0xFF, 0xFF, 0xFF, 0xFF)), StrokeThickness = 2.5, Width = 14, Height = 14, Stretch = Microsoft.UI.Xaml.Media.Stretch.Uniform, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Visibility = Visibility.Collapsed, IsHitTestVisible = false };
        checkBox.Child = cm;
        var cb = new Border { Width = 28, Height = 28, Background = new SolidColorBrush(Color.FromArgb(0, 0, 0, 0)), VerticalAlignment = VerticalAlignment.Center, Child = checkBox };
        Grid.SetColumn(cb, 0); row.Children.Add(cb);
        var tb = new TextBlock { Text = text, FontSize = 15, CharacterSpacing = App.GetCharacterSpacing(text, 120), Foreground = App.GetBrush("AppTextPrimaryBrush"), VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap, TextTrimming = TextTrimming.None };
        var tw = new Grid(); tw.Children.Add(tb);
        Grid.SetColumn(tw, 1); row.Children.Add(tw);
        void UpdateVisual() => UpdateRowVisual(row, checkedStates[index]);
        cb.Tapped += (_, _) => { checkedStates[index] = !checkedStates[index]; UpdateVisual(); OnTodoRowCheckedChanged(card, checkedStates, index); };
        UpdateVisual(); 
        list.Children.Add(row);
        return row;
    }

    private void AttachCardHoverEffect(Border card)
    {

        var baseBorderColor = Color.FromArgb(0x33, 0xFF, 0xFF, 0xFF);
        if (App.GetBrush("AppBorderBrush") is SolidColorBrush baseBrush)
        {
            baseBorderColor = baseBrush.Color;
            card.BorderBrush = new SolidColorBrush(baseBorderColor);
        }
        var hoverBorderColor = Color.FromArgb(
            (byte)Math.Min(baseBorderColor.A + 0x30, 0xFF),
            baseBorderColor.R,
            baseBorderColor.G,
            baseBorderColor.B);
        card.PointerEntered += (_, _) =>
        {
            if (_dragging) return; 
            // E2-05: manual color swap - ColorAnimation on hover is 0xc000027b-sensitive during page
            // switches (DiaryPage crash 2026-08-10); only the TranslateY lift stays animated.
            if (!_reminderCards.Contains(card) && card.BorderBrush is SolidColorBrush sb) sb.Color = hoverBorderColor;
            if (card.RenderTransform is TranslateTransform t) { var la = new DoubleAnimation { To = -3, Duration = TimeSpan.FromMilliseconds(200), EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } }; var st = new Storyboard(); Storyboard.SetTarget(la, t); Storyboard.SetTargetProperty(la, "Y"); st.Children.Add(la); st.Begin(); }
        };
        card.PointerExited += (_, _) =>
        {
            if (_dragging) return; 
            if (!_reminderCards.Contains(card) && card.BorderBrush is SolidColorBrush sb) sb.Color = baseBorderColor;
            if (card.RenderTransform is TranslateTransform t) { var la = new DoubleAnimation { To = 0, Duration = TimeSpan.FromMilliseconds(300), EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } }; var st = new Storyboard(); Storyboard.SetTarget(la, t); Storyboard.SetTargetProperty(la, "Y"); st.Children.Add(la); st.Begin(); }
        };
    }
    private void AttachCardContextMenu(Border c) { c.ContextRequested += (s, e) => { e.Handled = true; var m = BuildCardContextMenu(c); if (e.TryGetPosition(c, out var p)) m.ShowAt(c, p); else m.ShowAt(c, new Point(0, 0)); }; }

    

    private void AttachCardDrag(Border card)
    {
        bool armed = false;
        DispatcherTimer? timer = null;
        Point grabOffset = default;

        card.PointerPressed += (s, e) =>
        {
            if (_dragging) return;
            if (_currentFilter != "all") return; 
            if (_pinnedCards.Contains(card)) return; 
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
        _dropIndex = _dragOriginIndex = CountVisibleBeforeIn(card, CardList); // N2-06: origin in visible-slot space

        
        
        App.StopCardEntrance(card); 
        card.BorderBrush = new SolidColorBrush(Color.FromArgb(0xFF, 0x72, 0x76, 0xFF));
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
            var pos = card.TransformToVisual(RootGrid).TransformPoint(new Point(0, 0));
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
        var pos = e.GetCurrentPoint(RootGrid).Position;
        _lastPointerInRoot = pos;
        if (_dragGhost != null)
        {
            Canvas.SetLeft(_dragGhost, pos.X - _grabOffset.X);
            Canvas.SetTop(_dragGhost, pos.Y - _grabOffset.Y);
        }
        var ptInList = e.GetCurrentPoint(CardList).Position;
        UpdateDropIndicator(ComputeDropIndex(ptInList));
        HandleAutoScroll(e);
    }

    private int ComputeDropIndex(Point ptInList)
    {
        
        
        
        int count = 0;
        foreach (var child in CardList.Children)
        {
            if (ReferenceEquals(child, _dropIndicator)) continue;
            
            if (child is FrameworkElement fe && fe.Visibility == Visibility.Visible)
            {
                var top = fe.TransformToVisual(CardList).TransformPoint(new Point(0, 0)).Y;
                if (ptInList.Y < top + fe.ActualHeight / 2) break;
                count++;
            }
        }
        return count;
    }

    private void UpdateDropIndicator(int index)
    {
        
        
        
        int pinnedVisible = 0;
        foreach (var c in CardList.Children)
            if (c is Border pb && pb.Visibility == Visibility.Visible && _pinnedCards.Contains(pb))
                pinnedVisible++;
        index = Math.Max(index, pinnedVisible);
        
        if (index == _dragOriginIndex || index == _dragOriginIndex + 1)
        {
            if (_dropIndicator != null) { CardList.Children.Remove(_dropIndicator); _dropIndicator = null; }
            _dropIndex = _dragOriginIndex; 
            return;
        }
        if (_dropIndicator != null) CardList.Children.Remove(_dropIndicator);
        _dropIndicator = CreateDropIndicator();
        CardList.Children.Insert(index, _dropIndicator);
        _dropIndex = index;
    }

    private Border CreateDropIndicator()
    {
        
        var b = new Border { Height = 16, Background = new SolidColorBrush(Color.FromArgb(0, 0, 0, 0)) };
        b.Child = new Border
        {
            Height = 2,
            CornerRadius = new CornerRadius(1),
            Background = new SolidColorBrush(Color.FromArgb(0xFF, 0x72, 0x76, 0xFF)),
            VerticalAlignment = VerticalAlignment.Center,
        };
        return b;
    }

    private void HandleAutoScroll(PointerRoutedEventArgs e)
    {
        var pt = e.GetCurrentPoint(CardScrollViewer).Position;
        double topZone = 40, bottomZone = 40;
        int dir = 0;
        if (pt.Y < topZone) dir = -1;
        else if (pt.Y > CardScrollViewer.ViewportHeight - bottomZone) dir = 1;

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
        var newOffset = Math.Clamp(CardScrollViewer.VerticalOffset + _autoScrollDir * 8, 0, CardScrollViewer.ScrollableHeight);
        CardScrollViewer.ChangeView(null, newOffset, null, true);

        
        
        if (++_autoScrollFrame < 3) return;
        _autoScrollFrame = 0;
        var ptInList = RootGrid.TransformToVisual(CardList).TransformPoint(_lastPointerInRoot);
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
        if (_dropIndicator != null) { CardList.Children.Remove(_dropIndicator); _dropIndicator = null; }
        StopAutoScroll();

        
        card.BorderBrush = new SolidColorBrush(App.GetBrush("AppBorderBrush").Color);
        card.BorderThickness = new Thickness(1);
        card.Opacity = 1;
        _ = RefreshReminderBorder(card); 

        
        // N2-06: dropIndex is in "slot space including the dragged card" - convert to the
        // without-self space, then translate back to a physical Children index (hidden cards stay put).
        int curVisible = CountVisibleBeforeIn(card, CardList);
        int dst = dropIndex > curVisible ? dropIndex - 1 : dropIndex;
        if (dst != curVisible)
        {
            CardList.Children.Remove(card);
            CardList.Children.Insert(PhysicalIndexForVisibleSlotIn(dst, CardList), card);
        }

        PersistManualOrder();
    }

    /// <summary>N2-06: number of Visible cards strictly before <paramref name="card"/> in
    /// <paramref name="container"/> (visible-slot space matching ComputeDropIndex; the drop
    /// indicator itself is excluded).</summary>
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

    /// <summary>N2-06: physical Children index where a card must land to become the
    /// <paramref name="visibleSlot"/>-th visible card (call AFTER removing the dragged card).</summary>
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

    
    
    
    private void PersistManualOrder()
    {
        var db = App.Store?.Database;
        if (db == null) return;
        var todoById = db.TodoCards.ToDictionary(x => x.Id);
        var noteById = db.NoteCards.ToDictionary(x => x.Id);
        int order = 0;
        // N2-06 two-pass: visible cards take 1..N in the just-dragged order, then hidden cards
        // (other workspaces/filters) continue in their own relative order.
        foreach (var child in CardList.Children)
        {
            
            
            if (child is Border b && b.Visibility == Visibility.Visible && _cardIds.TryGetValue(b, out var id))
            {
                if (todoById.TryGetValue(id, out var t)) t.Order = ++order;
                else if (noteById.TryGetValue(id, out var n)) n.Order = ++order;
            }
        }
        foreach (var child in CardList.Children)
        {
            if (child is Border b && b.Visibility != Visibility.Visible && _cardIds.TryGetValue(b, out var idH))
            {
                if (todoById.TryGetValue(idH, out var tH)) tH.Order = ++order;
                else if (noteById.TryGetValue(idH, out var nH)) nH.Order = ++order;
            }
        }
        PersistOrderAndSave();
    }
    private MenuFlyout BuildCardContextMenu(Border card)
    {
        var cv = Microsoft.UI.Xaml.Markup.XamlBindingHelper.ConvertValue; var m = new MenuFlyout { MenuFlyoutPresenterStyle = (Style)Application.Current.Resources["GlassMenuFlyoutPresenterStyle"] };
        var si = new MenuFlyoutItem { Style = (Style)Application.Current.Resources["GlassMenuFlyoutItemStyle"], Text = _starredCards.Contains(card) ? App.GetString("Menu_Unstar") : App.GetString("Menu_Star"), Icon = new PathIcon { Data = (Geometry)cv(typeof(Geometry), _starredCards.Contains(card) ? IconData.Unstar : IconData.Star), Foreground = App.GetBrush("IconForegroundBrush") } }; si.Click += (_, _) => { if (_starredCards.Contains(card)) { _starredCards.Remove(card); if (_starIcons.TryGetValue(card, out var sv)) sv.Visibility = Visibility.Collapsed; } else { _starredCards.Add(card); if (_starIcons.TryGetValue(card, out var sv)) sv.Visibility = Visibility.Visible; } SyncCardStar(card, _starredCards.Contains(card)); };
        var pi = new MenuFlyoutItem { Style = (Style)Application.Current.Resources["GlassMenuFlyoutItemStyle"], Text = _pinnedCards.Contains(card) ? App.GetString("Menu_Unpin") : App.GetString("Menu_Pin"), Icon = new PathIcon { Data = (Geometry)cv(typeof(Geometry), _pinnedCards.Contains(card) ? IconData.Unpin : IconData.Pin), Foreground = App.GetBrush("IconForegroundBrush") } }; pi.Click += (_, _) => { bool willPin = !_pinnedCards.Contains(card); if (willPin) { _pinnedCards.Add(card); if (_pinIcons.TryGetValue(card, out var pv)) pv.Visibility = Visibility.Visible; } else { _pinnedCards.Remove(card); if (_pinIcons.TryGetValue(card, out var pv)) pv.Visibility = Visibility.Collapsed; } SyncCardPin(card, willPin); if (willPin) ReorderCards(); 
PersistOrderAndSave(); };
        var ei = new MenuFlyoutItem { Style = (Style)Application.Current.Resources["GlassMenuFlyoutItemStyle"], Text = App.GetString("Menu_Edit"), Icon = new PathIcon { Data = MakeGroup(IconData.Edit), Foreground = App.GetBrush("IconForegroundBrush") } }; ei.Click += (_, _) => { if (card.Tag is string tg && tg == "note") ShowEditNoteDialog(card); else ShowEditTodoDialog(card); };
        m.Items.Add(pi); m.Items.Add(si); 
        
        if (card.Tag is string tg2 && tg2 is "note" or "todo")
        {
            string? cardId = _cardIds.TryGetValue(card, out var g) ? g.ToString() : null;
            bool onDesktop = cardId != null && Services.StickySync.IsOnDesktop(cardId);
            if (onDesktop)
            {
                var cdi = new MenuFlyoutItem { Style = (Style)Application.Current.Resources["GlassMenuFlyoutItemStyle"], Text = App.GetString("Menu_CancelDesktopNote"), Icon = new PathIcon { Data = (Geometry)cv(typeof(Geometry), IconData.CancelDesktop), Foreground = App.GetBrush("IconForegroundBrush") } };
                cdi.Click += (_, _) => { if (cardId != null) Services.StickySync.RemoveNote(cardId); App.ShowToast(App.GetString("Common_Toast_Recalled")); };
                m.Items.Add(cdi);
            }
            else
            {
                var sdi = new MenuFlyoutItem { Style = (Style)Application.Current.Resources["GlassMenuFlyoutItemStyle"], Text = App.GetString("Menu_SendToDesktop"), Icon = new PathIcon { Data = (Geometry)cv(typeof(Geometry), IconData.SendToDesktop), Foreground = App.GetBrush("IconForegroundBrush") } }; sdi.Click += (_, _) => { App.ShowToast(App.GetString(SendCardToDesktop(card) ? "Common_Toast_Sent" : "Common_Toast_LaunchFail")); };
                m.Items.Add(sdi);
            }
        }
        
        if (card.Tag is string tg3 && tg3 is "note" or "todo")
        {
            bool hasReminder = HasReminder(card);
            var remI = hasReminder
                ? new MenuFlyoutItem { Style = (Style)Application.Current.Resources["GlassMenuFlyoutItemStyle"], Text = App.GetString("Menu_CancelReminder"), Icon = new PathIcon { Data = App.CreateGeometry(IconData.CancelReminder[0] + " " + IconData.CancelReminder[1]), Foreground = App.GetBrush("IconForegroundBrush") } }
                : new MenuFlyoutItem { Style = (Style)Application.Current.Resources["GlassMenuFlyoutItemStyle"], Text = App.GetString("Menu_SetReminder"), Icon = new PathIcon { Data = App.CreateGeometry(IconData.Reminder), Foreground = App.GetBrush("IconForegroundBrush") } };
            remI.Click += (_, _) =>
            {
                _pendingReminderCard = card;
                if (hasReminder) ShowCancelReminderDialog();
                else ShowSetReminderDialog();
            };
            m.Items.Add(remI);
        }
        m.Items.Add(ei);
        var di = new MenuFlyoutItem { Style = (Style)Application.Current.Resources["GlassMenuFlyoutItemStyle"], Text = App.GetString("Menu_Delete"), Foreground = new SolidColorBrush(Color.FromArgb(0xFF, 0xFF, 0x45, 0x45)), Icon = new PathIcon { Data = App.CreateGeometry(IconData.SoftDelete), Foreground = new SolidColorBrush(Color.FromArgb(0xFF, 0xFF, 0x45, 0x45)) } }; di.Click += (_, _) => { _pendingDeleteCard = card; ShowDeleteConfirmDialog(); };
        m.Items.Add(new MenuFlyoutSeparator()); m.Items.Add(di); return m;
    }

    

    private bool HasReminder(Border card)
    {
        if (!_cardIds.TryGetValue(card, out var id)) return false;
        var db = App.Store?.Database;
        if (db == null) return false;
        if (card.Tag is string tg && tg == "note")
            return db.NoteCards.FirstOrDefault(n => n.Id == id)?.ReminderAt != null;
        return db.TodoCards.FirstOrDefault(t => t.Id == id)?.ReminderAt != null;
    }

    private void ShowSetReminderDialog()
    {
        if (_pendingReminderCard == null) return;
        SetReminderTitleText.Text = App.GetString("Menu_SetReminder");
        var target = DateTime.Now.AddHours(1);
        SetReminderDatePicker.Date = new DateTimeOffset(target.Date);
        SetReminderTimePicker.Time = new TimeSpan(target.Hour, 0, 0);
        UpdateSetReminderConfirmState(); // N4-29: initial M5 state (default target is Now+1h, enabled)
        SetReminderDialogTransform.ScaleX = 0.94; SetReminderDialogTransform.ScaleY = 0.94; SetReminderDialogTransform.TranslateY = 24;
        SetReminderDialog.Opacity = 0; SetReminderScrim.Opacity = 0;
        SetReminderOverlay.Visibility = Visibility.Visible; DialogDepth.VeilShow();
        var sb = new Storyboard();
        var si = new DoubleAnimation { To = 1, Duration = TimeSpan.FromMilliseconds(250) }; Storyboard.SetTarget(si, SetReminderScrim); Storyboard.SetTargetProperty(si, "Opacity"); sb.Children.Add(si);
        var di = new DoubleAnimation { To = 1, Duration = TimeSpan.FromMilliseconds(300) }; Storyboard.SetTarget(di, SetReminderDialog); Storyboard.SetTargetProperty(di, "Opacity"); sb.Children.Add(di);
        Motion.AddDialogShowTransform(sb, SetReminderDialogTransform); // M1: EmphasizedDecelerate (Motion)
        sb.Begin();
    }

    private void HideSetReminderDialog() { DialogDepth.VeilHideImmediate(); SetReminderOverlay.Visibility = Visibility.Collapsed; _pendingReminderCard = null; }
    private void SetReminderClose_Click(object s, RoutedEventArgs e) => HideSetReminderDialog();
    private void SetReminderScrim_Tapped(object s, TappedRoutedEventArgs e) { if (ReferenceEquals(e.OriginalSource, SetReminderScrim)) HideSetReminderDialog(); }

    // N4-29: schtasks silently fails to register a past moment (exit code ignored) and the system
    // reminder would never fire - M5: the confirm button stays disabled until the picked date+time
    // is in the future. Wired to DateChanged/TimeChanged in the constructor.
    private void UpdateSetReminderConfirmState()
    {
        var due = SetReminderDatePicker.Date.Date + SetReminderTimePicker.Time;
        SetReminderConfirmButton.IsEnabled = due > DateTime.Now;
    }

    private void SetReminderConfirm_Click(object s, RoutedEventArgs e)
    {
        if (_pendingReminderCard == null) return;
        var card = _pendingReminderCard;
        var due = SetReminderDatePicker.Date.Date + SetReminderTimePicker.Time;
        
        if (due <= DateTime.Now) { UpdateSetReminderConfirmState(); SetReminderConfirmButton.IsEnabled = false; return; }
        SetReminderOnCard(card, due);
        HideSetReminderDialog();
    }

    private void ShowCancelReminderDialog()
    {
        if (_pendingReminderCard == null) return;
        CancelReminderTitleText.Text = App.GetString("Menu_CancelReminder");
        CancelReminderMessageText.Text = App.GetString("Reminder_Cancel_ConfirmTip");
        CancelReminderDialogTransform.ScaleX = 0.94; CancelReminderDialogTransform.ScaleY = 0.94; CancelReminderDialogTransform.TranslateY = 24;
        CancelReminderDialog.Opacity = 0; CancelReminderScrim.Opacity = 0;
        CancelReminderOverlay.Visibility = Visibility.Visible; DialogDepth.VeilShow();
        var sb = new Storyboard();
        var si = new DoubleAnimation { To = 1, Duration = TimeSpan.FromMilliseconds(250) }; Storyboard.SetTarget(si, CancelReminderScrim); Storyboard.SetTargetProperty(si, "Opacity"); sb.Children.Add(si);
        var di = new DoubleAnimation { To = 1, Duration = TimeSpan.FromMilliseconds(300) }; Storyboard.SetTarget(di, CancelReminderDialog); Storyboard.SetTargetProperty(di, "Opacity"); sb.Children.Add(di);
        Motion.AddDialogShowTransform(sb, CancelReminderDialogTransform); // M1: EmphasizedDecelerate (Motion)
        sb.Begin();
    }

    private void HideCancelReminderDialog() { DialogDepth.VeilHideImmediate(); CancelReminderOverlay.Visibility = Visibility.Collapsed; _pendingReminderCard = null; }
    private void CancelReminderClose_Click(object s, RoutedEventArgs e) => HideCancelReminderDialog();
    private void CancelReminderScrim_Tapped(object s, TappedRoutedEventArgs e) { if (ReferenceEquals(e.OriginalSource, CancelReminderScrim)) HideCancelReminderDialog(); }

    private void CancelReminderConfirm_Click(object s, RoutedEventArgs e)
    {
        if (_pendingReminderCard == null) return;
        var card = _pendingReminderCard;
        ClearReminderOnCard(card);
        HideCancelReminderDialog();
    }

    
    private void SetReminderOnCard(Border card, DateTime due)
    {
        var db = App.Store?.Database;
        if (db == null || !_cardIds.TryGetValue(card, out var id)) return;
        _dueShown.Remove(card); // N4P-01: reminder is gone (or being re-set) - a future due must fire again
        if (card.Tag is string tg && tg == "note")
        {
            var n = db.NoteCards.FirstOrDefault(x => x.Id == id);
            if (n != null) { n.ReminderAt = due; n.ReminderSetAt = DateTime.Now; }
        }
        else
        {
            var t = db.TodoCards.FirstOrDefault(x => x.Id == id);
            if (t != null) { t.ReminderAt = due; t.ReminderSetAt = DateTime.Now; }
        }
        App.Store?.SaveAsync();
        Services.ReminderScheduler.Schedule(id, due); 
        RefreshReminderBorder(card);
        StartReminderRefresh();
    }

    
    private void ClearReminderOnCard(Border card)
    {
        var db = App.Store?.Database;
        if (db == null || !_cardIds.TryGetValue(card, out var id)) return;
        _dueShown.Remove(card); // N4P-01: reminder cleared - allow a future reminder on this card to fire again
        if (card.Tag is string tg && tg == "note")
        {
            var n = db.NoteCards.FirstOrDefault(x => x.Id == id);
            if (n != null) { n.ReminderAt = null; n.ReminderSetAt = null; }
        }
        else
        {
            var t = db.TodoCards.FirstOrDefault(x => x.Id == id);
            if (t != null) { t.ReminderAt = null; t.ReminderSetAt = null; }
        }
        App.Store?.SaveAsync();
        Services.ReminderScheduler.Cancel(id); 
        _reminderCards.Remove(card); // N4P-05: keep the hover set in sync - PointerEntered skips cards still registered here, so the border hover effect died after cancel/confirm
        ResetCardBorder(card);
    }

    
    private bool RefreshReminderBorder(Border card)
    {
        var db = App.Store?.Database;
        if (db == null || !_cardIds.TryGetValue(card, out var id)) return false;
        DateTime? at = null, setAt = null;
        if (card.Tag is string tg && tg == "note")
        {
            var n = db.NoteCards.FirstOrDefault(x => x.Id == id);
            at = n?.ReminderAt; setAt = n?.ReminderSetAt;
        }
        else
        {
            var t = db.TodoCards.FirstOrDefault(x => x.Id == id);
            at = t?.ReminderAt; setAt = t?.ReminderSetAt;
        }
        if (at == null || setAt == null) { ResetCardBorder(card); _reminderCards.Remove(card); return false; }
        card.BorderBrush = new SolidColorBrush(ReminderColor(setAt.Value, at.Value));
        card.BorderThickness = new Thickness(3);
        _reminderCards.Add(card);
        return true;
    }

    private void ResetCardBorder(Border card)
    {
        card.BorderBrush = new SolidColorBrush(App.GetBrush("AppBorderBrush").Color);
        card.BorderThickness = new Thickness(1);
    }

    
    private static Color ReminderColor(DateTime setAt, DateTime dueAt)
    {
        double total = (dueAt - setAt).TotalSeconds;
        double elapsed = (DateTime.Now - setAt).TotalSeconds;
        double p = total <= 0 ? 1 : Math.Clamp(elapsed / total, 0, 1);
        double h = 130 * (1 - p);        
        double s = 1.0 - 0.154 * p;      
        double v = 0.8 + 0.067 * p;      
        return HsvToRgb(h, s, v);
    }

    private static Color HsvToRgb(double h, double s, double v)
    {
        double c = v * s;
        double x = c * (1 - Math.Abs((h / 60) % 2 - 1));
        double m = v - c;
        double r, g, b;
        if (h < 60) { r = c; g = x; b = 0; }
        else if (h < 120) { r = x; g = c; b = 0; }
        else if (h < 180) { r = 0; g = c; b = x; }
        else if (h < 240) { r = 0; g = x; b = c; }
        else if (h < 300) { r = x; g = 0; b = c; }
        else { r = c; g = 0; b = x; }
        return Color.FromArgb(0xFF, (byte)Math.Round((r + m) * 255), (byte)Math.Round((g + m) * 255), (byte)Math.Round((b + m) * 255));
    }

    private DispatcherTimer? _reminderTimer;
    private readonly HashSet<Border> _dueShown = new(); // N4P-01: cards whose due dialog/toast already fired - the 30s tick must not re-fire them until the reminder is cleared or re-set
    private bool _deleteConfirming; // N4P-11: one-shot guard - double-click during the 200ms hide animation ran the removal twice (double toast, double persist)

    
    private void StartReminderRefresh()
    {
        if (_reminderTimer != null) return;
        _reminderTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
        _reminderTimer.Tick += (_, _) => RefreshAllReminderBorders();
        _reminderTimer.Start();
    }

    private void RefreshAllReminderBorders()
    {
        bool anyReminder = false;
        Border? dueCard = null;
        foreach (var (card, _) in _cardIds)
        {
            if (card.Tag is not string tg || (tg != "note" && tg != "todo")) continue;
            // N4-28: the due card must ALSO get its border refreshed (p=1 = full red) - the old
            // if/else-if chain skipped it because IsReminderDue consumed the branch.
            if (dueCard == null && IsReminderDue(card)) { dueCard = card; if (RefreshReminderBorder(card)) anyReminder = true; }
            else if (RefreshReminderBorder(card)) anyReminder = true;
        }
        if (anyReminder) StartReminderRefresh();
        
        if (dueCard != null && ReminderDueOverlay.Visibility != Visibility.Visible && _dueShown.Add(dueCard)) ShowReminderDueDialog(dueCard); // N4P-01: fire once per reminder - an unconfirmed dialog must not re-toast + re-animate every 30s
    }

    private bool IsReminderDue(Border card)
    {
        var db = App.Store?.Database;
        if (db == null || !_cardIds.TryGetValue(card, out var id)) return false;
        DateTime? at = card.Tag is string tg && tg == "note"
            ? db.NoteCards.FirstOrDefault(n => n.Id == id)?.ReminderAt
            : db.TodoCards.FirstOrDefault(t => t.Id == id)?.ReminderAt;
        return at != null && at.Value <= DateTime.Now;
    }

    private Border? _dueReminderCard;

    private void ShowReminderDueDialog(Border card)
    {
        _dueReminderCard = card;
        ReminderDueTitleText.Text = App.GetString("Reminder_Due_Title");
        string content;
        if (card.Tag is string tg && tg == "note")
        {
            var nd = _noteData.TryGetValue(card, out var d) ? d : default;
            content = string.IsNullOrWhiteSpace(nd.title) ? nd.content : nd.title + "\n" + nd.content;
        }
        else
        {
            var td = _todoData.TryGetValue(card, out var t) ? t : default;
            content = string.IsNullOrWhiteSpace(td.title) ? td.mainText : td.title + "\n" + td.mainText;
        }
        ReminderDueContentText.Text = content;
        ReminderDueConfirmText.Text = App.GetString("Reminder_Due_Confirm");
        var toast = content.Length > 120 ? content.Substring(0, 120) + "…" : content;
        Services.ToastService.Show(App.GetString("Reminder_Due_Title"), toast); 
        ReminderDueDialogTransform.ScaleX = 0.94; ReminderDueDialogTransform.ScaleY = 0.94; ReminderDueDialogTransform.TranslateY = 24;
        ReminderDueDialog.Opacity = 0; ReminderDueScrim.Opacity = 0;
        ReminderDueOverlay.Visibility = Visibility.Visible; DialogDepth.VeilShow();
        var sb = new Storyboard();
        var si = new DoubleAnimation { To = 1, Duration = TimeSpan.FromMilliseconds(250) }; Storyboard.SetTarget(si, ReminderDueScrim); Storyboard.SetTargetProperty(si, "Opacity"); sb.Children.Add(si);
        var di = new DoubleAnimation { To = 1, Duration = TimeSpan.FromMilliseconds(300) }; Storyboard.SetTarget(di, ReminderDueDialog); Storyboard.SetTargetProperty(di, "Opacity"); sb.Children.Add(di);
        Motion.AddDialogShowTransform(sb, ReminderDueDialogTransform); // M1: EmphasizedDecelerate (Motion)
        sb.Begin();
    }

    private void HideReminderDueDialog()
    {
        DialogDepth.VeilHideImmediate();
        ReminderDueOverlay.Visibility = Visibility.Collapsed;
        if (_dueReminderCard != null) { ClearReminderOnCard(_dueReminderCard); _dueReminderCard = null; }
        // N3-37: another card may be due right NOW - fire it immediately instead of letting it sit
        // until the next 30s tick (simultaneous dues used to pop one dialog, then stall the rest
        // for up to 30s of stale border + missed notification timing).
        DispatcherQueue.TryEnqueue(RefreshAllReminderBorders);
    }

    private void ReminderDueClose_Click(object s, RoutedEventArgs e) => HideReminderDueDialog();
    private void ReminderDueScrim_Tapped(object s, TappedRoutedEventArgs e) { if (ReferenceEquals(e.OriginalSource, ReminderDueScrim)) HideReminderDueDialog(); }
    private void ReminderDueConfirm_Click(object s, RoutedEventArgs e) => HideReminderDueDialog();

    
    public bool ShowReminderDueById(Guid id)
    {
        foreach (var (card, cid) in _cardIds)
        {
            if (cid == id && card.Tag is string tg && (tg == "todo" || tg == "note"))
            {
                
                
                if (ReminderDueOverlay.Visibility == Visibility.Visible) return true;
                if (!_dueShown.Add(card)) return true; // N3-08: the 30s tick already fired this card - re-showing would double-dialog/toast and overwrite _dueReminderCard, orphaning the first card's reminder fields + its schtasks task
                ShowReminderDueDialog(card); // N4P-01: system-pull/forward path fires the dialog directly - register it so the 30s tick does not re-fire while unconfirmed
                return true;
            }
        }
        return false;
    }

    private bool SendCardToDesktop(Border card)
    {
        string? id = _cardIds.TryGetValue(card, out var g) ? g.ToString() : null;
        string kind = card.Tag is string tg ? tg : "todo";
        if (id == null) return false;

        string title, content;
        List<StickyTodoItem>? items = null;
        if (kind == "note")
        {
            title = _noteData.TryGetValue(card, out var nd) ? nd.title : "";
            content = _noteData.TryGetValue(card, out var nd2) ? nd2.content : "";
        }
        else
        {
            var td = _todoData.TryGetValue(card, out var t) ? t : default;
            title = td.title ?? "";
            content = "";
            items = BuildTodoItems(td.mainText, td.subTexts, td.checkedStates);
        }
        return Services.StickySync.SendToDesktop(id, kind, title, content, items);
    }

    /// <summary>Build the structured todo rows for stickies.json from plan-card data (index 0 = main todo, 1..n = sub-todos).</summary>
    private static List<StickyTodoItem> BuildTodoItems(string mainText, List<string> subTexts, List<bool> states)
    {
        var items = new List<StickyTodoItem>
        {
            new StickyTodoItem { Label = mainText ?? "", Checked = states is { Count: > 0 } && states[0] }
        };
        if (subTexts != null)
            for (int i = 0; i < subTexts.Count; i++)
                items.Add(new StickyTodoItem { Label = subTexts[i], Checked = states is { Count: > 0 } && i + 1 < states.Count && states[i + 1] });
        return items;
    }

    private void ReorderCards()
    {
        var db = App.Store?.Database;
        var children = CardList.Children.Cast<UIElement>().ToList();
        var pinned = children.Where(c => c is Border b && _pinnedCards.Contains(b)).OrderByDescending(c => _createdAt.GetValueOrDefault((Border)c, DateTime.MinValue)).ToList();
        var unpinned = children.Where(c => c is Border b && !_pinnedCards.Contains(b)).ToList();

        
        bool manual = db != null && (db.TodoCards.Any(x => x.Order > 0) || db.NoteCards.Any(x => x.Order > 0));
        if (manual)
        {
            var orderById = new Dictionary<Guid, int>();
            foreach (var t in db!.TodoCards) orderById[t.Id] = t.Order;
            foreach (var n in db!.NoteCards) orderById[n.Id] = n.Order;
            unpinned = unpinned
                .OrderBy(c => _cardIds.TryGetValue((Border)c, out var id) && orderById.TryGetValue(id, out var o) && o > 0 ? o : int.MaxValue)
                .ThenByDescending(c => _createdAt.GetValueOrDefault((Border)c, DateTime.MinValue))
                .ToList();
        }
        else
        {
            unpinned = unpinned.OrderByDescending(c => _createdAt.GetValueOrDefault((Border)c, DateTime.MinValue)).ToList();
        }

        CardList.Children.Clear();
        foreach (var c in pinned) CardList.Children.Add(c);
        foreach (var c in unpinned) CardList.Children.Add(c);
    }

    private string GetRandomIconKey() => $"Group{_iconRandom.Next(1, IconData.GroupIconCount + 1):D2}";
    private async void FlashTextBox(TextBox tb)
    {
        if (_flashCtsMap.TryGetValue(tb, out var prev)) { prev.Cancel(); prev.Dispose(); } // N5P-02: per-box CTS (E5-24 pattern)
        var cts = new System.Threading.CancellationTokenSource();
        _flashCtsMap[tb] = cts;

        var orig = _flashOriginalBgs.TryGetValue(tb, out var existing) ? existing : tb.Background;
        _flashOriginalBgs[tb] = orig;
        tb.Background = new SolidColorBrush(Windows.UI.Color.FromArgb(0x33, 0xFF, 0x45, 0x45));
        try { await System.Threading.Tasks.Task.Delay(600, cts.Token); }
        catch (System.Threading.Tasks.TaskCanceledException)
        {
            if (_flashCtsMap.TryGetValue(tb, out var cur) && !ReferenceEquals(cur, cts)) return; // N5F-01: superseded - newer flash owns the box now
            tb.Background = orig;
            _flashOriginalBgs.Remove(tb);
            _flashCtsMap.Remove(tb);
            return;
        }
        tb.Background = orig;
        _flashOriginalBgs.Remove(tb);
        _flashCtsMap.Remove(tb);
    }
    private void ShowDeleteConfirmDialog() { DeleteConfirmMessageText.Text = _pendingDeleteCard != null && _pendingDeleteCard.Tag is string tg && tg == "note" ? App.GetString("Plan_Note_Delete_ConfirmTip") : App.GetString("Plan_Todo_Delete_ConfirmTip"); DeleteConfirmDialogTransform.ScaleX = 0.94; DeleteConfirmDialogTransform.ScaleY = 0.94; DeleteConfirmDialogTransform.TranslateY = 24; DeleteConfirmDialog.Opacity = 0; DeleteConfirmScrim.Opacity = 0; DeleteConfirmOverlay.Visibility = Visibility.Visible; DialogDepth.VeilShow(); var sb = new Storyboard(); var si = new DoubleAnimation { To = 1, Duration = TimeSpan.FromMilliseconds(250) }; Storyboard.SetTarget(si, DeleteConfirmScrim); Storyboard.SetTargetProperty(si, "Opacity"); sb.Children.Add(si); var di = new DoubleAnimation { To = 1, Duration = TimeSpan.FromMilliseconds(300) }; Storyboard.SetTarget(di, DeleteConfirmDialog); Storyboard.SetTargetProperty(di, "Opacity"); sb.Children.Add(di); Motion.AddDialogShowTransform(sb, DeleteConfirmDialogTransform); sb.Begin(); }
    private void HideDeleteConfirmDialog() { var sb = new Storyboard(); var so = new DoubleAnimation { To = 0, Duration = TimeSpan.FromMilliseconds(200) }; Storyboard.SetTarget(so, DeleteConfirmScrim); Storyboard.SetTargetProperty(so, "Opacity"); sb.Children.Add(so); var d = new DoubleAnimation { To = 0, Duration = TimeSpan.FromMilliseconds(200) }; Storyboard.SetTarget(d, DeleteConfirmDialog); Storyboard.SetTargetProperty(d, "Opacity"); sb.Children.Add(d); Motion.AddDialogHideTransform(sb, DeleteConfirmDialogTransform); sb.Completed -= OnDeleteConfirmHideCompleted; sb.Completed += OnDeleteConfirmHideCompleted; DialogDepth.VeilHide(); sb.Begin(); }
    private void OnDeleteConfirmHideCompleted(object? sender, object e) { DeleteConfirmOverlay.Visibility = Visibility.Collapsed; _pendingDeleteCard = null; _deleteConfirming = false; } // N4P-11: re-arm after the overlay is truly gone
    private void DeleteConfirmClose_Click(object s, RoutedEventArgs e) => HideDeleteConfirmDialog();
    private void DeleteConfirmScrim_Tapped(object s, TappedRoutedEventArgs e) { if (ReferenceEquals(e.OriginalSource, DeleteConfirmScrim)) HideDeleteConfirmDialog(); }
    private void DeleteConfirmButton_Click(object s, RoutedEventArgs e) { if (_pendingDeleteCard == null) { HideDeleteConfirmDialog(); return; } if (_deleteConfirming) return; _deleteConfirming = true; { bool isNote = _pendingDeleteCard.Tag is string tg && tg == "note"; _starredCards.Remove(_pendingDeleteCard); _pinnedCards.Remove(_pendingDeleteCard); _pinIcons.Remove(_pendingDeleteCard); _starIcons.Remove(_pendingDeleteCard); _todoData.Remove(_pendingDeleteCard); _todoCollapsed.Remove(_pendingDeleteCard); _todoCompletedBadges.Remove(_pendingDeleteCard); _noteData.Remove(_pendingDeleteCard); _noteExpanded.Remove(_pendingDeleteCard); _cardExpandBtns.Remove(_pendingDeleteCard); _todoRowPanels.Remove(_pendingDeleteCard); _createdAt.Remove(_pendingDeleteCard); _reminderCards.Remove(_pendingDeleteCard); _dueShown.Remove(_pendingDeleteCard);

            if (_cardIds.Remove(_pendingDeleteCard, out var delId))
            {
                var db = App.Store?.Database;
                if (isNote)
                {
                    var c = db?.NoteCards.FirstOrDefault(x => x.Id == delId);
                    if (c != null) { c.IsDeleted = true; c.DeletedAt = DateTime.Now; TeardownReminderOnDelete(delId, c.ReminderAt, c); }
                }
                else
                {
                    var c = db?.TodoCards.FirstOrDefault(x => x.Id == delId);
                    if (c != null) { c.IsDeleted = true; c.DeletedAt = DateTime.Now; TeardownReminderOnDelete(delId, c.ReminderAt, c); }
                }
                Services.StickySync.RemoveNote(delId.ToString());
            }
            var delCard = _pendingDeleteCard;
            App.PlayCardRemoval(CardList, delCard, _dragging, () => { PersistOrderAndSave(); UpdateEmptyHint(); App.ShowToast(App.GetString("Common_Toast_Deleted")); }); } HideDeleteConfirmDialog(); }

    /// <summary>N4P-04: a deleted card's schtasks one-shot would still fire later - it drags the app to
    /// the foreground and finds no card (dead popup + orphan task). Cancel the task and clear the fields
    /// so a trash-restore does not resurrect a stale (likely past-due) reminder either.</summary>
    private void TeardownReminderOnDelete(Guid id, DateTime? reminderAt, object entity)
    {
        if (reminderAt == null) return;
        Services.ReminderScheduler.Cancel(id);
        switch (entity)
        {
            case NoteCard n: n.ReminderAt = null; n.ReminderSetAt = null; break;
            case TodoCard t: t.ReminderAt = null; t.ReminderSetAt = null; break;
        }
        App.Store?.SaveAsync();
    }

    /// <summary>Open the note-edit dialog for a database note id (used by desktop-sticky edit jump).</summary>
    public bool OpenEditNoteById(Guid id)
    {
        foreach (var (card, cid) in _cardIds)
        {
            if (cid == id && card.Tag is string tg && tg == "note")
            {
                ShowEditNoteDialog(card);
                return true;
            }
        }
        return false;
    }

    /// <summary>Open the todo-edit dialog for a database todo id (used by desktop-sticky edit jump - todo cards share the host "Edit" bridge).</summary>
    public bool OpenEditTodoById(Guid id)
    {
        foreach (var (card, cid) in _cardIds)
        {
            if (cid == id && card.Tag is string tg && tg == "todo")
            {
                ShowEditTodoDialog(card);
                return true;
            }
        }
        return false;
    }

    /// <summary>Open the todo-edit dialog for a database todo id (global search jump).</summary>
    private bool FlashCardAndScroll(Border card)
    {
        if (card == null) return false;
        try
        {
            var t = card.TransformToVisual(CardScrollViewer);
            var pt = t.TransformPoint(new Windows.Foundation.Point(0, 0));
            CardScrollViewer.ChangeView(null, System.Math.Max(0, CardScrollViewer.VerticalOffset + pt.Y - 20), null, false);
        }
        catch { }
        App.FlashCard(card);
        return true;
    }

    public bool FlashTodoById(Guid id)
    {
        foreach (var (card, cid) in _cardIds)
            if (cid == id && card.Tag is string tg && tg == "todo") return FlashCardAndScroll(card);
        return false;
    }

    public bool FlashNoteById(Guid id)
    {
        foreach (var (card, cid) in _cardIds)
            if (cid == id && card.Tag is string tg && tg == "note") return FlashCardAndScroll(card);
        return false;
    }

    private void ShowEditTodoDialog(Border card)
    {
        _confirming = false; // E3-10: re-arm the confirm guard when the dialog re-opens
        if (!_todoData.TryGetValue(card, out var d)) return;
        _editingTodoCard = card;
        _editTodoOrigName = d.title;   
        _editTodoOrigIcon = d.iconKey;
        _editTodoOrigMain = d.mainText;
        _editTodoOrigSubs = d.subTexts.ToList();
        NewTodoDialogTitle.Text = App.GetString("Plan_Todo_EditTitle");
        TodoNameBox.Text = d.title;
        MainTodoBox.Text = d.mainText;
        SubTodoPanel.Children.Clear();
        SubTodoPanel.Children.Add(new TextBlock { Text = App.GetString("Plan_Todo_SubLabel"), FontSize = 12, FontWeight = Microsoft.UI.Text.FontWeights.Medium, Foreground = App.GetBrush("AppTextSecondaryBrush"), Margin = new Thickness(0, 0, 0, 8) });
        foreach (var st in d.subTexts) { var tb = CreateSubTodoBox(); tb.Text = st; SubTodoPanel.Children.Add(tb); }
        SubTodoPanel.Children.Add(CreateSubTodoBox());
        LoadIconSelector();
        SelectIcon(d.iconKey);
        UpdateTodoConfirmState(); // UI-2: edit-fill -> recompute validity
        NewTodoDialogTransform.ScaleX = 0.94; NewTodoDialogTransform.ScaleY = 0.94; NewTodoDialogTransform.TranslateY = 24; NewTodoDialog.Opacity = 0; NewTodoScrim.Opacity = 0; NewTodoOverlay.Visibility = Visibility.Visible; DialogDepth.VeilShow();
        var sb = new Storyboard(); var si = new DoubleAnimation { To = 1, Duration = TimeSpan.FromMilliseconds(250) }; Storyboard.SetTarget(si, NewTodoScrim); Storyboard.SetTargetProperty(si, "Opacity"); sb.Children.Add(si); var di = new DoubleAnimation { To = 1, Duration = TimeSpan.FromMilliseconds(300) }; Storyboard.SetTarget(di, NewTodoDialog); Storyboard.SetTargetProperty(di, "Opacity"); sb.Children.Add(di); Motion.AddDialogShowTransform(sb, NewTodoDialogTransform); Motion.StaggerReset(NewTodoDialog); Motion.StaggerWire(sb, NewTodoDialog); sb.Begin();
    }
    private void ShowEditNoteDialog(Border card)
    {
        _confirming = false; // E3-10: re-arm the confirm guard when the dialog re-opens
        if (!_noteData.TryGetValue(card, out var d)) return;
        _editingNoteCard = card;
        _editNoteOrigName = d.title;   
        _editNoteOrigIcon = d.iconKey;
        _editNoteOrigContent = d.content;
        NewNoteDialogTitle.Text = App.GetString("Plan_Note_EditTitle");
        NoteNameBox.Text = d.title;
        NoteContentBox.Text = d.content;
        LoadNoteIconSelector();
        SelectNoteIcon(d.iconKey);
        UpdateNoteConfirmState(); // UI-2: edit-fill -> recompute validity
        NewNoteDialogTransform.ScaleX = 0.94; NewNoteDialogTransform.ScaleY = 0.94; NewNoteDialogTransform.TranslateY = 24; NewNoteDialog.Opacity = 0; NewNoteScrim.Opacity = 0; NewNoteOverlay.Visibility = Visibility.Visible; DialogDepth.VeilShow();
        var sb = new Storyboard(); var si = new DoubleAnimation { To = 1, Duration = TimeSpan.FromMilliseconds(250) }; Storyboard.SetTarget(si, NewNoteScrim); Storyboard.SetTargetProperty(si, "Opacity"); sb.Children.Add(si); var di = new DoubleAnimation { To = 1, Duration = TimeSpan.FromMilliseconds(300) }; Storyboard.SetTarget(di, NewNoteDialog); Storyboard.SetTargetProperty(di, "Opacity"); sb.Children.Add(di); Motion.AddDialogShowTransform(sb, NewNoteDialogTransform); Motion.StaggerReset(NewNoteDialog); Motion.StaggerWire(sb, NewNoteDialog); sb.Begin();
    }
    public void ShowNewNoteDialog() { _confirming = false; _editingNoteCard = null; NoteNameBox.Text = ""; NoteContentBox.Text = ""; UpdateNoteConfirmState(); LoadNoteIconSelector(); NewNoteDialogTransform.ScaleX = 0.94; NewNoteDialogTransform.ScaleY = 0.94; NewNoteDialogTransform.TranslateY = 24; NewNoteDialog.Opacity = 0; NewNoteScrim.Opacity = 0; NewNoteOverlay.Visibility = Visibility.Visible; DialogDepth.VeilShow(); var sb = new Storyboard(); var si = new DoubleAnimation { To = 1, Duration = TimeSpan.FromMilliseconds(250) }; Storyboard.SetTarget(si, NewNoteScrim); Storyboard.SetTargetProperty(si, "Opacity"); sb.Children.Add(si); var di = new DoubleAnimation { To = 1, Duration = TimeSpan.FromMilliseconds(300) }; Storyboard.SetTarget(di, NewNoteDialog); Storyboard.SetTargetProperty(di, "Opacity"); sb.Children.Add(di); Motion.AddDialogShowTransform(sb, NewNoteDialogTransform); Motion.StaggerReset(NewNoteDialog); Motion.StaggerWire(sb, NewNoteDialog); sb.Begin(); }
    private void HideNewNoteDialog() { var sb = new Storyboard(); var so = new DoubleAnimation { To = 0, Duration = TimeSpan.FromMilliseconds(200) }; Storyboard.SetTarget(so, NewNoteScrim); Storyboard.SetTargetProperty(so, "Opacity"); sb.Children.Add(so); var d = new DoubleAnimation { To = 0, Duration = TimeSpan.FromMilliseconds(200) }; Storyboard.SetTarget(d, NewNoteDialog); Storyboard.SetTargetProperty(d, "Opacity"); sb.Children.Add(d); Motion.AddDialogHideTransform(sb, NewNoteDialogTransform); sb.Completed -= OnNewNoteHideCompleted; sb.Completed += OnNewNoteHideCompleted; DialogDepth.VeilHide(); sb.Begin(); }
    private void OnNewNoteHideCompleted(object? sender, object e) { NewNoteOverlay.Visibility = Visibility.Collapsed; _editingNoteCard = null; }
    private void CloseNewNote_Click(object s, RoutedEventArgs e) => HideNewNoteDialog();
    private void NewNoteScrim_Tapped(object s, TappedRoutedEventArgs e) { if (ReferenceEquals(e.OriginalSource, NewNoteScrim)) HideNewNoteDialog(); }
    private void LoadNoteIconSelector() { _noteSelectedIconKey = null; NoteIconPanel.Children.Clear(); foreach (var key in IconData.GroupIconKeysInOrder()) { var b = new Border { Width = 48, Height = 48, CornerRadius = new CornerRadius(12), Background = App.GetBrush("AppSurfaceOverlayBrush"), Tag = key, Child = new Viewbox { Width = 24, Height = 24, Stretch = Microsoft.UI.Xaml.Media.Stretch.Uniform, Child = new PathIcon { Data = App.CreateGeometry(IconData.GetGroupPath(key)), Foreground = App.GetBrush("IconForegroundBrush") } } }; b.Tapped += NoteIconBorder_Tapped; NoteIconPanel.Children.Add(b); } }
    private void NoteIconBorder_Tapped(object s, TappedRoutedEventArgs e) { if (s is Border b && b.Tag is string k) { SelectNoteIcon(k); CenterNoteIconInScrollViewer(b); UpdateNoteConfirmState(); } }
    private void SelectNoteIcon(string key) { _noteSelectedIconKey = key; var d = App.GetBrush("AppSurfaceBrush"); var n = App.GetBrush("AppSurfaceOverlayBrush"); foreach (var c in NoteIconPanel.Children) if (c is Border b && b.Tag is string k) { bool o = k == key; b.Background = o ? d : n; b.BorderBrush = o ? App.GetBrush("AppBorderBrush") : null; b.BorderThickness = new Thickness(o ? 1 : 0); } }
    private void CenterNoteIconInScrollViewer(Border t) { var r = t.TransformToVisual(NoteIconPanel); var p = r.TransformPoint(new Point(0, 0)); double o = p.X - (NoteIconScrollViewer.ViewportWidth / 2) + (t.ActualWidth / 2); o = Math.Max(0, Math.Min(o, NoteIconScrollViewer.ScrollableWidth)); NoteIconScrollViewer.ChangeView(o, null, null, false); }
    
    // Single-line captures land as real cards through the same build/map/reorder pipeline as the
    // dialogs. When this page has never been built (hotkey used while another tab is open), the
    // entity goes straight into the database - LoadFromStore renders it on the first visit.

    public void QuickCaptureTodo(string text)
    {
        var t = text.Trim(); if (t.Length == 0) return;
        if (!_storeLoaded)
        {
            var te0 = new TodoCard { Title = t, MainText = t, IconKey = GetRandomIconKey(), CreatedAt = DateTime.Now, WorkspaceId = App.CurrentWorkspaceId };
            AssignNewCardOrder(te0, null);
            App.Store?.Database.TodoCards.Add(te0);
            _ = App.Store?.SaveAsync();
            App.ShowToast(App.GetString("Common_Toast_Created"));
            return;
        }
        var iconKey = GetRandomIconKey();
        _bulkLoading = true;
        var nc = BuildTodoCard(t, iconKey, t, new List<string>()); // title and main text carry the same line: main text is the checkable item
        _bulkLoading = false;
        var te = new TodoCard { Title = t, IconKey = iconKey, MainText = t, SubTexts = new List<string>(), CheckedStates = _todoData.TryGetValue(nc, out var td) ? new List<bool>(td.checkedStates) : new List<bool>(), CreatedAt = DateTime.Now, WorkspaceId = App.CurrentWorkspaceId };
        AssignNewCardOrder(te, null);
        App.Store?.Database.TodoCards.Add(te);
        _cardIds[nc] = te.Id;
        App.PlayCardEntrance(nc);
        ReorderCards(); // N4P-03 parity
        PersistOrderAndSave();
        SetFilter(_currentFilter); // N4P-10 parity: a card created under an active filter must respect it
        App.ShowToast(App.GetString("Common_Toast_Created"));
    }

    public void QuickCaptureNote(string text)
    {
        var t = text.Trim(); if (t.Length == 0) return;
        if (!_storeLoaded)
        {
            var ne0 = new NoteCard { Title = t, Content = t, IconKey = GetRandomIconKey(), CreatedAt = DateTime.Now, WorkspaceId = App.CurrentWorkspaceId };
            AssignNewCardOrder(null, ne0);
            App.Store?.Database.NoteCards.Add(ne0);
            _ = App.Store?.SaveAsync();
            App.ShowToast(App.GetString("Common_Toast_Created"));
            return;
        }
        var iconKey = GetRandomIconKey();
        _bulkLoading = true;
        var nc = BuildNoteCard(t, iconKey, t); // title = card name, content = the same line
        _bulkLoading = false;
        var ne = new NoteCard { Title = t, IconKey = iconKey, Content = t, IsExpanded = _noteExpanded.TryGetValue(nc, out var nexp) && nexp, CreatedAt = DateTime.Now, WorkspaceId = App.CurrentWorkspaceId };
        AssignNewCardOrder(null, ne);
        App.Store?.Database.NoteCards.Add(ne);
        _cardIds[nc] = ne.Id;
        App.PlayCardEntrance(nc);
        ReorderCards();
        PersistOrderAndSave();
        SetFilter(_currentFilter);
        App.ShowToast(App.GetString("Common_Toast_Created"));
    }

    private void NewNoteConfirm_Click(object s, RoutedEventArgs e) { if (_confirming) return; _confirming = true; bool isEdit = _editingNoteCard != null; // E3-10
        var name = NoteNameBox.Text.Trim(); if (string.IsNullOrWhiteSpace(name)) { _confirming = false; FlashTextBox(NoteNameBox); return; } var iconKey = _noteSelectedIconKey ?? GetRandomIconKey(); var content = NoteContentBox.Text.Trim(); if (string.IsNullOrWhiteSpace(content)) { _confirming = false; FlashTextBox(NoteContentBox); return; } if (_editingNoteCard != null) { var old = _editingNoteCard; bool wasStar = _starredCards.Contains(old); bool wasPin = _pinnedCards.Contains(old); bool wasReminder = _reminderCards.Contains(old); var oldCreated = _createdAt.TryGetValue(old, out var oc) ? oc : DateTime.Now; bool wasExpanded = _noteExpanded.TryGetValue(old, out var we) && we; _starredCards.Remove(old); _pinnedCards.Remove(old); _pinIcons.Remove(old); _starIcons.Remove(old); _noteData.Remove(old); _cardExpandBtns.Remove(old); _noteExpanded.Remove(old); _createdAt.Remove(old); _reminderCards.Remove(old); _dueShown.Remove(old); CardList.Children.Remove(old); _bulkLoading = true; var nc = BuildNoteCard(name, iconKey, content, wasExpanded); _bulkLoading = false; _createdAt[nc] = oldCreated; if (wasStar) { _starredCards.Add(nc); if (_starIcons.TryGetValue(nc, out var si)) si.Visibility = Visibility.Visible; } if (wasPin) { _pinnedCards.Add(nc); if (_pinIcons.TryGetValue(nc, out var pi)) pi.Visibility = Visibility.Visible; } if (wasReminder) { _reminderCards.Add(nc); } App.PlayCardEntrance(nc);

            if (_cardIds.TryGetValue(old, out var editNid))
            {
                _cardIds[nc] = editNid;
                var ne = App.Store?.Database.NoteCards.FirstOrDefault(x => x.Id == editNid);
                if (ne != null)
                {
                    ne.Title = name; ne.IconKey = iconKey; ne.Content = content;
                    ne.IsExpanded = _noteExpanded.TryGetValue(nc, out var nexp) && nexp;
                }
                
                if (Services.StickySync.Contains(editNid.ToString()))
                    Services.StickySync.UpdateNote(editNid.ToString(), "note", name, content);
                if (wasReminder) RefreshReminderBorder(nc); // N2-25: AFTER the _cardIds assignment (see todo flow)
            }
            _cardIds.Remove(old);
            ReorderCards(); 
            PersistOrderAndSave();
        } else {
            // N4P-03: mirror the todo branch - map the id before any reorder sees the card.
            _bulkLoading = true;
            var nc2 = BuildNoteCard(name, iconKey, content);
            _bulkLoading = false;
            var ne2 = new NoteCard { Title = name, IconKey = iconKey, Content = content, IsExpanded = _noteExpanded.TryGetValue(nc2, out var nexp2) && nexp2, CreatedAt = DateTime.Now, WorkspaceId = App.CurrentWorkspaceId };
            AssignNewCardOrder(null, ne2);
            App.Store?.Database.NoteCards.Add(ne2);
            _cardIds[nc2] = ne2.Id;
            App.PlayCardEntrance(nc2);
            ReorderCards(); // N4P-03
            PersistOrderAndSave();
            SetFilter(_currentFilter); // N4P-10: respect the active filter (NH4 parity)
        }
        App.ShowToast(App.GetString(isEdit ? "Common_Toast_Modified" : "Common_Toast_Created"));
        HideNewNoteDialog(); }
    private Border BuildNoteCard(string title, string iconKey, string content, bool expanded = false)
    {
        var cv = Microsoft.UI.Xaml.Markup.XamlBindingHelper.ConvertValue;
        var card = new Border { CornerRadius = new CornerRadius(12), Background = App.GetBrush("AppSurfaceOverlayBrush"), BorderBrush = new SolidColorBrush(App.GetBrush("AppBorderBrush").Color), // E5-20: per-card copy - never mutate the shared theme brush (E1-09 pattern)
            BorderThickness = new Thickness(1), Padding = new Thickness(20, 18, 20, 18), HorizontalAlignment = HorizontalAlignment.Stretch, RenderTransform = new TranslateTransform() };
        var root = new Grid(); root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var tr = new Grid(); tr.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); tr.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); tr.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); tr.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); tr.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var ib = new Border { Width = 32, Height = 32, CornerRadius = new CornerRadius(8), Background = App.GetBrush("AppSurfaceOverlayBrush"), VerticalAlignment = VerticalAlignment.Center, Child = new Viewbox { Width = 18, Height = 18, Stretch = Microsoft.UI.Xaml.Media.Stretch.Uniform, Child = new PathIcon { Data = App.CreateGeometry(IconData.GetGroupPath(iconKey)), Foreground = App.GetBrush("IconForegroundBrush") } } };
        Grid.SetColumn(ib, 0); tr.Children.Add(ib);
        var tt = new TextBlock { Text = title, FontSize = 18, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, CharacterSpacing = App.GetCharacterSpacing(title, 200), Foreground = App.GetBrush("AppTextPrimaryBrush"), VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(12, 0, 0, 0) }; Grid.SetColumn(tt, 1); tr.Children.Add(tt);
        var pinIcon = new Viewbox { Width = 16, Height = 16, Stretch = Microsoft.UI.Xaml.Media.Stretch.Uniform, VerticalAlignment = VerticalAlignment.Center, Visibility = Visibility.Collapsed, Margin = new Thickness(0, 0, 12, 0), Child = new PathIcon { Data = App.CreateGeometry(IconData.CardPin), Foreground = new SolidColorBrush(Color.FromArgb(0xFF, 0x72, 0x76, 0xFF)) } };
        Grid.SetColumn(pinIcon, 2); tr.Children.Add(pinIcon);
        var starIcon = new Viewbox { Width = 16, Height = 16, Stretch = Microsoft.UI.Xaml.Media.Stretch.Uniform, VerticalAlignment = VerticalAlignment.Center, Visibility = Visibility.Collapsed, Margin = new Thickness(0, 0, 12, 0), Child = new PathIcon { Data = App.CreateGeometry(IconData.CardStar), Foreground = new SolidColorBrush(Color.FromArgb(0xFF, 0x72, 0x76, 0xFF)) } };
        Grid.SetColumn(starIcon, 3); tr.Children.Add(starIcon);
        _pinIcons[card] = pinIcon; _starIcons[card] = starIcon;
        var expandIcon = new PathIcon { Data = (Geometry)cv(typeof(Geometry), IconData.CardCollapse), Foreground = App.GetBrush("IconForegroundBrush") };
        var expandViewbox = new Viewbox { Width = 18, Height = 18, Stretch = Microsoft.UI.Xaml.Media.Stretch.Uniform, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Child = expandIcon };
        var expandBtn = new Button { Width = 36, Height = 36, Style = (Style)Application.Current.Resources["NovaraIconButtonStyle"], VerticalAlignment = VerticalAlignment.Center, Content = expandViewbox, Visibility = Visibility.Collapsed, IsTabStop = false };
        Grid.SetColumn(expandBtn, 4); tr.Children.Add(expandBtn);
        Grid.SetRow(tr, 0); root.Children.Add(tr);
        var contentBlock = new TextBlock { Text = string.IsNullOrWhiteSpace(content) ? App.GetString("Plan_Note_NoContent") : content, FontSize = 14, CharacterSpacing = App.GetCharacterSpacing(content, 120), Foreground = App.GetBrush("AppTextSecondaryBrush"), TextWrapping = TextWrapping.Wrap, MaxLines = 3, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 12, 0, 0), Opacity = string.IsNullOrWhiteSpace(content) ? 0.45 : 1 };
        Grid.SetRow(contentBlock, 1); root.Children.Add(contentBlock);
        card.Child = root;
        bool needExpand = content.Length > 240 || content.Contains('\n');
        if (needExpand) { expandBtn.Visibility = Visibility.Visible; expandIcon.Data = (Geometry)Microsoft.UI.Xaml.Markup.XamlBindingHelper.ConvertValue(typeof(Geometry), IconData.CardExpand); expandViewbox.Margin = new Thickness(-2, -1, 0, 0); _noteExpanded[card] = expanded; if (expanded) { contentBlock.MaxLines = 0; contentBlock.TextTrimming = TextTrimming.None; expandIcon.Data = (Geometry)cv(typeof(Geometry), IconData.CardCollapse); expandViewbox.Margin = new Thickness(0); } expandBtn.Click += (_, _) => { bool expanded = _noteExpanded.TryGetValue(card, out var ex) && ex; expanded = !expanded; _noteExpanded[card] = expanded; contentBlock.MaxLines = expanded ? 0 : 3; contentBlock.TextTrimming = expanded ? TextTrimming.None : TextTrimming.CharacterEllipsis; expandIcon.Data = expanded ? (Geometry)cv(typeof(Geometry), IconData.CardCollapse) : (Geometry)Microsoft.UI.Xaml.Markup.XamlBindingHelper.ConvertValue(typeof(Geometry), IconData.CardExpand); expandViewbox.Margin = expanded ? new Thickness(0) : new Thickness(-2, -1, 0, 0); SyncNoteExpanded(card, expanded); }; _cardExpandBtns[card] = expandBtn; }
        card.Tag = "note"; _noteData[card] = (title, iconKey, content); _createdAt[card] = DateTime.Now;
        AttachCardHoverEffect(card);
        AttachCardDrag(card);
        AttachCardContextMenu(card); CardList.Children.Add(card); if (!_bulkLoading) ReorderCards(); UpdateEmptyHint();
        return card;
    }

    private void SyncNoteExpanded(Border card, bool expanded)
    {
        if (!_cardIds.TryGetValue(card, out var id)) return;
        var n = App.Store?.Database.NoteCards.FirstOrDefault(x => x.Id == id);
        if (n != null) { n.IsExpanded = expanded; App.Store?.SaveAsync(); }
    }

    private GeometryGroup MakeGroup(string[] p) { var cv = Microsoft.UI.Xaml.Markup.XamlBindingHelper.ConvertValue; var g = new GeometryGroup(); foreach (var x in p) g.Children.Add((Geometry)cv(typeof(Geometry), x)); return g; }
}
