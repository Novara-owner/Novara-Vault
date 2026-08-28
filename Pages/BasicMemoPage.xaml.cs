/* ========== BasicMemoPage - Memo Tab ==========
Function: Memo groups & entries management - context menus (create/edit/pin/star/delete), search reparent, API connectivity check, group/entry cards
Corresponding UI: BasicMemoPage.xaml.cs
Logic Range: Whole file business logic of this module
*/
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Media.Imaging;
using System;
using System.Collections.Generic;
using System.Linq;
using Windows.ApplicationModel.DataTransfer;
using Windows.Foundation;
using Windows.UI;
using Novara.Models;
using Novara.Services;

namespace Novara.Pages;

public sealed partial class BasicMemoPage : Page
{
    private readonly MenuFlyout _contextMenu;
    private string _pendingMenuAction = null!;

    private string _selectedIcon = "";
    private List<Border> _iconBorders = new List<Border>();
    private string _entrySelectedIcon = "";           
    private readonly List<Border> _entryIconBorders = new(); 
    private readonly System.Random _iconRandom = new();

    private string _selectedEntryType = "";
    private MenuFlyout _entryTypeMenu = null!;

    private Border _currentGroupCard = null!;
    private Border _pinnedGroupCard = null!;
    private Dictionary<Border, FrameworkElement> _pinIcons = new();
    private HashSet<Border> _starredCards = new();
    private Dictionary<Border, FrameworkElement> _starIcons = new();
    private Dictionary<Border, TextBlock> _groupNameTexts = new();

    private readonly Dictionary<Border, (string name, string iconKey)> _groupData = new();

    private readonly Dictionary<Border, Guid> _groupIds = new();
    private readonly Dictionary<Border, Guid> _entryIds = new();
    private readonly Dictionary<Border, DateTime> _groupCreatedAt = new();
    private readonly Dictionary<Border, DateTime> _entryCreatedAt = new();
    private bool _storeLoaded;
    private Novara.Models.NovaraDatabase? _loadedDb; // N5M-04: db snapshot this UI build was created from
    private List<Border> _standaloneEntries = new();
    private HashSet<Border> _entriesInGroup = new();
    private Border _uncategorizedCard = null!;
    private Border _targetGroupCard = null!;
    private Border _currentEntryCard = null!;
    private Border _pinnedEntryCard = null!;
    private Border _editingEntryCard = null!;
    
    private string _editOrigName = "";
    private string _editOrigIcon = "";
    private List<(string label, string value)> _editOrigFields = new();
    private string _editGroupOrigName = ""; 
    private Border _pendingMoveEntry = null!;
    private Dictionary<Border, FrameworkElement> _entryPinIcons = new();
    private HashSet<Border> _starredEntries = new();
    private Dictionary<Border, FrameworkElement> _entryStarIcons = new();
    private Dictionary<Border, (string type, string name, string keyInfo, List<(string, string, bool)> fields)> _entryData = new();
    private readonly Dictionary<Border, (CancellationTokenSource Cts, bool Expanded)> _entryExpand = new();
    private readonly Dictionary<Border, string> _entryIconKeys = new(); // 4.0: entry card -> custom icon key (empty = legacy jigsaw)

    private readonly Dictionary<TextBox, System.Threading.CancellationTokenSource> _flashCtsMap = new(); // N5M-01: per-box CTS (E5-24 pattern)
    private readonly Dictionary<TextBox, Microsoft.UI.Xaml.Media.Brush> _flashOriginalBgs = new();

    private readonly Dictionary<Border, string> _apiProtocols = new();
    private readonly Dictionary<Border, TextBlock> _groupCountTexts = new();
    private Border? _apiCheckCard;
    private System.Threading.CancellationTokenSource? _apiCheckCts;
    private string _selectedApiProtocol = "Bearer";
    private Border? _apiDiagCard;
    private System.Threading.CancellationTokenSource? _apiDiagCts;
    private System.Action? _apiConfirmProceed;   
    private Border? _relayProbeCard;
    private System.Threading.CancellationTokenSource? _relayProbeCts;
    private Storyboard? _relayProbePulse;         

    
    private Border? _dragCard;
    private Panel? _dragContainer;
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

    public BasicMemoPage()
    {
        this.InitializeComponent();
        Novara.Services.DialogDepth.AttachContainer((Grid)Content); // dialog depth: shadow + chrome veil auto-wiring
        this.Loaded += Page_Loaded;
        this.Unloaded += OnPageUnloaded; // 3.0.5
        this.KeyDown += Page_KeyDown;
        _contextMenu = BuildContextMenu();
        AddInfoIcon.PointerEntered += (_, _) => AddInfoIcon.Opacity = 1.0;
        AddInfoIcon.PointerExited += (_, _) => AddInfoIcon.Opacity = 0.6;
    }

    /* ========== BasicMemoPage Right Click Menus ==========
Function: All context menus for memo groups & entries: create/edit/pin/star/detect/delete; menu item text & icons switch by state
Corresponding UI: BasicMemoPage.xaml.cs
Logic Range: Below methods in this region
*/
private MenuFlyout BuildContextMenu()
    {
        var menu = new MenuFlyout();
        menu.MenuFlyoutPresenterStyle = (Style)Application.Current.Resources["GlassMenuFlyoutPresenterStyle"];

        var newGroupItem = new MenuFlyoutItem
        {
            Style = (Style)Application.Current.Resources["GlassMenuFlyoutItemStyle"],
            Text = App.GetString("Menu_New_Group"),
            Icon = new PathIcon
            {
                Data = App.CreateGeometry(IconData.NewGroup),
                Foreground = App.GetBrush("IconForegroundBrush")
            }
        };
        newGroupItem.Click += (s, e) => { _pendingMenuAction = "new_group"; };

        var newEntryItem = new MenuFlyoutItem
        {
            Style = (Style)Application.Current.Resources["GlassMenuFlyoutItemStyle"],
            Text = App.GetString("Menu_New_Entry"),
            Icon = new PathIcon
            {
                Data = App.CreateGeometry(IconData.NewEntry[0] + " " + IconData.NewEntry[1]),
                Foreground = App.GetBrush("IconForegroundBrush")
            }
        };
        newEntryItem.Click += (s, e) => { _pendingMenuAction = "new_entry"; };

        menu.Items.Add(newGroupItem);
        menu.Items.Add(newEntryItem);

        menu.Closed += (s, e) =>
        {
            if (_pendingMenuAction == "new_group")
                OpenNewGroupDialog();
            else if (_pendingMenuAction == "new_entry")
                OpenNewEntryDialog();
            _pendingMenuAction = null!;
        };

        return menu;
    }

    private MenuFlyout BuildGroupCardContextMenu(bool isPinned, bool isStarred)
    {
        var menu = new MenuFlyout();
        menu.MenuFlyoutPresenterStyle = (Style)Application.Current.Resources["GlassMenuFlyoutPresenterStyle"];

        var editItem = new MenuFlyoutItem
        {
            Style = (Style)Application.Current.Resources["GlassMenuFlyoutItemStyle"],
            Text = App.GetString("Menu_Edit"),
            Icon = new PathIcon { Data = App.CreateGeometry(IconData.Edit[0] + " " + IconData.Edit[1]), Foreground = App.GetBrush("IconForegroundBrush") }
        };
        editItem.Click += (s, e) => OpenEditGroupDialog();

        var newEntryItem = new MenuFlyoutItem
        {
            Style = (Style)Application.Current.Resources["GlassMenuFlyoutItemStyle"],
            Text = App.GetString("Menu_New_Entry"),
            Icon = new PathIcon { Data = App.CreateGeometry(IconData.NewEntry[0] + " " + IconData.NewEntry[1]), Foreground = App.GetBrush("IconForegroundBrush") }
        };
        newEntryItem.Click += (s, e) => { _targetGroupCard = _currentGroupCard; OpenNewEntryDialog(); };

        var pinItem = new MenuFlyoutItem
        {
            Style = (Style)Application.Current.Resources["GlassMenuFlyoutItemStyle"],
            Text = isPinned ? App.GetString("Menu_Unpin") : App.GetString("Menu_Pin"),
            Icon = new PathIcon { Data = App.CreateGeometry(isPinned ? IconData.Unpin : IconData.Pin), Foreground = App.GetBrush("IconForegroundBrush") }
        };
        pinItem.Click += (s, e) => { if (isPinned) UnpinGroupCard(_currentGroupCard); else PinGroupCard(_currentGroupCard); };

        var starItem = new MenuFlyoutItem
        {
            Style = (Style)Application.Current.Resources["GlassMenuFlyoutItemStyle"],
            Text = isStarred ? App.GetString("Menu_Unstar") : App.GetString("Menu_Star"),
            Icon = new PathIcon { Data = App.CreateGeometry(isStarred ? IconData.Unstar : IconData.Star), Foreground = App.GetBrush("IconForegroundBrush") }
        };
        starItem.Click += (s, e) => { if (isStarred) UnstarGroupCard(_currentGroupCard); else StarGroupCard(_currentGroupCard); };

        var separator = new MenuFlyoutSeparator();

        var deleteItem = new MenuFlyoutItem
        {
            Style = (Style)Application.Current.Resources["GlassMenuFlyoutItemStyle"],
            Text = App.GetString("Menu_Delete"),
            Foreground = new SolidColorBrush(Color.FromArgb(0xFF, 0xFF, 0x45, 0x45)),
            Icon = new PathIcon { Data = App.CreateGeometry(IconData.SoftDelete), Foreground = new SolidColorBrush(Color.FromArgb(0xFF, 0xFF, 0x45, 0x45)) }
        };
        deleteItem.Click += (s, e) => OpenDeleteConfirmDialog(isGroup: true);

        menu.Items.Add(pinItem);
        menu.Items.Add(starItem);
        menu.Items.Add(newEntryItem);
        menu.Items.Add(editItem);
        menu.Items.Add(separator);
        menu.Items.Add(deleteItem);

        return menu;
    }

    private MenuFlyout BuildEntryTypeMenu()
    {
        var menu = new MenuFlyout();
        menu.MenuFlyoutPresenterStyle = (Style)Application.Current.Resources["GlassMenuFlyoutPresenterStyle"];

        for (int i = 0; i < EntryTypeLabels.Length; i++)
        {
            var type = EntryTypeLabels[i];
            var item = new MenuFlyoutItem
            {
                Style = (Style)Application.Current.Resources["GlassMenuFlyoutItemStyle"],
                Text = TypeLabel(type),
                Icon = new PathIcon { Data = App.CreateGeometry(type switch { "邮箱" => IconData.Email, "账户" => IconData.Account[0] + " " + IconData.Account[1], "API Key" => "M640.45 480.574c17.496 0 31.713 14.041 31.996 31.47l0.004 0.53V811.05c0 88.338-71.612 159.95-159.95 159.95-87.454 0-158.516-70.187-159.929-157.305l-0.021-2.645v-43.29c0-17.673 14.327-32 32-32 17.496 0 31.713 14.042 31.996 31.47l0.004 0.53v43.29c0 52.991 42.958 95.95 95.95 95.95 52.462 0 95.09-42.104 95.937-94.364l0.013-1.586V512.574c0-17.674 14.327-32 32-32zM256.357 352.55c17.673 0 32 14.327 32 32 0 17.496-14.042 31.713-31.47 31.995l-0.53 0.005h-42.745c-52.786 0-95.612 42.94-95.612 95.95 0 52.48 41.974 95.09 94.031 95.938l1.581 0.012H512.5c17.673 0 32 14.327 32 32 0 17.497-14.042 31.714-31.47 31.996l-0.53 0.004H213.612c-88.17 0-159.612-71.63-159.612-159.95 0-87.436 70.02-158.516 156.972-159.929l2.64-0.021h42.745z m554.454 0C899.268 352.55 971 424.15 971 512.5c0 87.468-70.305 158.517-157.54 159.929l-2.649 0.021h-40.997c-17.673 0-32-14.327-32-32 0-17.496 14.042-31.713 31.47-31.995l0.53-0.005h40.997c53.137 0 96.189-42.971 96.189-95.95 0-52.449-42.195-95.09-94.598-95.937l-1.59-0.013H512.5c-17.673 0-32-14.327-32-32 0-17.497 14.042-31.713 31.47-31.996l0.53-0.004h298.311zM512.5 54c87.454 0 158.516 70.187 159.929 157.305l0.021 2.645v42.772c0 17.673-14.327 32-32 32-17.496 0-31.713-14.042-31.996-31.47l-0.004-0.53V213.95c0-52.992-42.958-95.95-95.95-95.95-52.462 0-95.09 42.104-95.937 94.364l-0.013 1.586v297.212c0 17.673-14.327 32-32 32-17.496 0-31.713-14.042-31.996-31.471l-0.004-0.53v-297.21C352.55 125.611 424.162 54 512.5 54z", "网站" => IconData.Website, "银行卡" => IconData.BankCard, "WiFi" => string.Join(" ", IconData.WiFi), "证件" => string.Join(" ", IconData.IdCard), _ => IconData.Custom }), Foreground = App.GetBrush("IconForegroundBrush") }
            };
            item.Click += (s, e) =>
            {
                _selectedEntryType = type;
                EntryTypeButtonText.Text = TypeLabel(type); // C4 (Round 5): avoid Chinese contract value in EN UI (aligned with edit path)
                EntryTypeButtonText.Foreground = App.GetBrush("AppTextPrimaryBrush");
                ApplyEntryFormLayout(type);
            };
            menu.Items.Add(item);
        }
        return menu;
    }

    /// <summary>N4M-02: B9 insert index for group stacks - a pinned entry stays first; the incoming card lands after it (or at top).</summary>
    private int PinnedFirstInsertIndex(Panel container, Border incoming)
        => (_pinnedEntryCard != null && !ReferenceEquals(_pinnedEntryCard, incoming) && container.Children.Contains(_pinnedEntryCard))
            ? container.Children.IndexOf(_pinnedEntryCard) + 1
            : 0;

    
    private void MoveEntryToGroup(Border card, Border gb)
    {
        // E1-10: moving between group/standalone must NOT clear star/pin (design 4.2 only rescues on group delete)
        (card.Parent as Panel)?.Children.Remove(card);
        _standaloneEntries.Remove(card);
        _entriesInGroup.Add(card);

        if (_entryIds.TryGetValue(card, out var inId) && _groupIds.TryGetValue(gb, out var tGid))
        { var me = FindEntry(inId); if (me != null) me.GroupId = tGid; }
        var targetContent = gb.Child as Grid;
        if (targetContent != null && targetContent.Children.Count > 3 && targetContent.Children[2] is StackPanel entryStack)
        {
            entryStack.Children.Insert(PinnedFirstInsertIndex(entryStack, card), card); // N4M-02: was Add - a pinned entry moved into a group sank to the bottom (B9) and the order persisted
            if (targetContent.Children[3] is Grid emptyHint)
                emptyHint.Visibility = Visibility.Collapsed;
            UpdateGroupCount(gb);
        }
        SyncUncategorizedCard();
        PersistAll();
    }

    
    private void MoveEntryOut(Border card)
    {
        var p = card.Parent as Panel;
        if (p != null) p.Children.Remove(card);

        if (p is StackPanel sp && sp.Parent is Grid g && g.Parent is Border srcGroup)
        {
            UpdateGroupCount(srcGroup);
            if (sp.Children.Count == 0 && g.Children.Count > 3 && g.Children[3] is Grid eh)
                eh.Visibility = Visibility.Visible;
        }
        _entriesInGroup.Remove(card);
        _standaloneEntries.Add(card);

        if (_entryIds.TryGetValue(card, out var outId)) { var me = FindEntry(outId); if (me != null) me.GroupId = null; }
        GroupsContainer.Children.Add(card);
        SyncUncategorizedCard();
        PersistAll();
    }

    private void EntryTypeButton_Click(object sender, RoutedEventArgs e)
    {
        _entryTypeMenu ??= BuildEntryTypeMenu();
        if (sender is Button btn)
            _entryTypeMenu.ShowAt(btn, new Point(0, btn.ActualHeight + 4));
    }

    private void ResetEntryTypeForms()
    {
        _selectedEntryType = "";
        EntryTypeButtonText.Text = App.GetString("Memo_Entry_TypePlaceholder");
        EntryTypeButtonText.Foreground = App.GetBrush("AppTextTertiaryBrush");
        EntryTypeFormsContainer.Visibility = Visibility.Collapsed;
        EntryNameTextBox.Text = "";
        FormField2TextBox.Text = "";
        FormField3TextBox.Text = "";
        FormField4TextBox.Text = "";
        FormField5TextBox.Text = "";
        FormField6TextBox.Text = "";
        FormField7TextBox.Text = "";
        CustomFormContainer.Visibility = Visibility.Collapsed;
        CustomNameTextBox.Text = "";
        CustomInfoTextBox_0.Text = "";
        CustomInfoDynamicPanel.Children.Clear();
        _customInfoCount = 1;
        CustomInfoAddButton.Visibility = Visibility.Visible;
        ClearEntryIconSelection(); 
        UpdateNewEntryConfirmState(); // UI-2: no type selected -> confirm disabled
    }

    // UI-2 (2026-08-11): live validation for the new-entry dialog - the confirm button stays
    // disabled (grey, no hover) until every required field of the selected type is non-blank.
    private void UpdateNewEntryConfirmState()
    {
        string type = _selectedEntryType;
        bool valid = false;
        if (type == "自定义")
        {
            
            valid = !string.IsNullOrWhiteSpace(CustomNameTextBox.Text)
                 && !string.IsNullOrWhiteSpace(CustomInfoTextBox_0.Text);
        }
        else if (!string.IsNullOrWhiteSpace(type)) 
        {
            valid = !string.IsNullOrWhiteSpace(EntryNameTextBox.Text);
            if (type == "API Key") valid = valid && !string.IsNullOrWhiteSpace(FormField2TextBox.Text) && !string.IsNullOrWhiteSpace(FormField3TextBox.Text);
            else if (type == "网站") valid = valid && !string.IsNullOrWhiteSpace(FormField2TextBox.Text);
            else if (type == "邮箱" || type == "账户") valid = valid && !string.IsNullOrWhiteSpace(FormField2TextBox.Text); 
            else if (type == "银行卡") valid = valid && !string.IsNullOrWhiteSpace(FormField2TextBox.Text); 
            else if (type == "WiFi") valid = valid && !string.IsNullOrWhiteSpace(FormField2TextBox.Text); 
            else if (type == "证件") valid = valid && !string.IsNullOrWhiteSpace(FormField2TextBox.Text); 
        }
        
        if (valid && _editingEntryCard != null && !EntryContentChanged(type))
            valid = false;
        NewEntryConfirmButton.IsEnabled = valid;
    }

    private void EntryField_TextChanged(object sender, TextChangedEventArgs e)
    {
        UpdateNewEntryConfirmState();
    }

    private bool _entrancePlayed; // E1-26: play the entrance animation only once (page instances are cached by MainWindow)
    private bool _confirming; // E3-10: one-shot guard for the create confirm button - double-click during the hide animation created duplicate entries
    private bool _renderInProgress; // N4M-01: true while the chunked fill is still adding cards to the UI
    private bool _persistAfterRender; // N4M-01: a persistence request arrived during the fill window - rerun it after the fill

    private void Page_Loaded(object sender, RoutedEventArgs e)
    {

        LoadFromStore(); // E1-26: entrance animation now fires inside LoadFromStore after chunked render completes

        EditGroupClosePathIcon.Data = (Geometry)Microsoft.UI.Xaml.Markup.XamlBindingHelper.ConvertValue(typeof(Geometry), IconData.Close);
    }

    
    public void StealFocus() => FocusSink.Focus(FocusState.Programmatic);


    private async void LoadFromStore()
    {
        if (_storeLoaded) return;
        _loadedDb = App.Store?.Database; // N5M-04: capture the db this UI build belongs to
        _storeLoaded = true;
        HintText.Visibility = Visibility.Collapsed; // P0-1: hide until chunked render decides the real empty state
        var db = App.Store?.Database;
        if (db == null) return;

        var groups = db.MemoGroups.ToList();
        var entries = db.MemoEntries.Where(x => !x.IsDeleted).ToList();

        bool playEntrance = !_entrancePlayed; // P0-1: only the first load plays the cascade
        int entIdx = 0;

        
        
        foreach (var g in groups)
        {
            var card = CreateGroupCard(g.Name, g.IconKey);
            _groupIds[card] = g.Id;
            _groupCreatedAt[card] = g.CreatedAt;
            if (g.IsStarred) { _starredCards.Add(card); if (_starIcons.TryGetValue(card, out var si)) si.Visibility = Visibility.Visible; }
            if (g.IsPinned && _pinnedGroupCard == null) { _pinnedGroupCard = card; if (_pinIcons.TryGetValue(card, out var pi)) pi.Visibility = Visibility.Visible; }
            GroupsContainer.Children.Add(card);
            if (playEntrance && entIdx < 10) App.PlayCardEntrance(card, entIdx); entIdx++;
        }

        
        _renderInProgress = true; // N4M-01: PersistAll during the fill window would rebuild db from the partial UI
        try
        {
            await ChunkedRender.RunAsync(entries.Count, 6, DispatcherQueue, (s, e) =>
            {
            for (int i = s; i < e; i++)
            {
                var en = entries[i];
                var fields = en.Fields.Select(f => (f.Label, f.Value, f.CanCopy)).ToList();
                var card = CreateEntryCard(en.Type, en.Name, en.KeyInfo, fields, en.IconKey);
                _entryIds[card] = en.Id;
                _entryIconKeys[card] = en.IconKey;
                _entryCreatedAt[card] = en.CreatedAt;
                if (!string.IsNullOrEmpty(en.Protocol)) _apiProtocols[card] = en.Protocol;
                if (en.IsStarred) { _starredEntries.Add(card); if (_entryStarIcons.TryGetValue(card, out var si)) si.Visibility = Visibility.Visible; }
                if (en.IsPinned && _pinnedEntryCard == null) { _pinnedEntryCard = card; if (_entryPinIcons.TryGetValue(card, out var pi)) pi.Visibility = Visibility.Visible; }

                if (en.GroupId is Guid gid)
                {
                    Border? groupCard = null;
                    foreach (var kv in _groupIds)
                        if (kv.Value == gid) { groupCard = kv.Key; break; }
                    if (groupCard != null && groupCard.Child is Grid gg && gg.Children.Count > 3 && gg.Children[2] is StackPanel sp)
                    {
                        sp.Children.Add(card);
                        if (gg.Children[3] is Grid eh) eh.Visibility = Visibility.Collapsed;
                        _entriesInGroup.Add(card);
                        UpdateGroupCount(groupCard);
                        continue;
                    }

                }
                _standaloneEntries.Add(card);
                GroupsContainer.Children.Add(card);
                if (playEntrance && entIdx < 10) App.PlayCardEntrance(card, entIdx); entIdx++;
            }
            });
        }
        finally { _renderInProgress = false; }

        _entrancePlayed = true; // P0-1: cascade already played per-card above
        if (_standaloneEntries.Count > 0) SyncUncategorizedCard();
        if (GroupsContainer.Children.Count == 0) FloatInHint();
        if (_persistAfterRender) { _persistAfterRender = false; PersistAll(); } // N4M-01: run the deferred rebuild now that every entry has a card
    }

    private void PersistAll()
    {
        if (_renderInProgress) { _persistAfterRender = true; return; }
        if (_loadedDb != null && !ReferenceEquals(_loadedDb, App.Store?.Database)) return; // N5M-04: stale page after import/reset - never rebuild the new db from old UI // N4M-01: defer - rebuilding db from a partially filled UI would permanently drop not-yet-rendered entries
        var db = App.Store?.Database;
        if (db == null) return;
        var softDeleted = db.MemoEntries.Where(x => x.IsDeleted).ToList(); // keep trash items across the UI-driven rebuild
        var groupById = db.MemoGroups.ToDictionary(x => x.Id);
        var entryById = db.MemoEntries.ToDictionary(x => x.Id);
        var groups = new List<MemoGroup>();
        var entries = new List<MemoEntry>();

        void CollectEntries(Panel panel)
        {
            foreach (var child in panel.Children)
                if (child is Border eb && _entryIds.TryGetValue(eb, out var eid) && entryById.TryGetValue(eid, out var en))
                    entries.Add(en);
        }

        foreach (var child in GroupsContainer.Children)
        {
            if (child is Border b && _groupIds.TryGetValue(b, out var gid) && groupById.TryGetValue(gid, out var g))
            {
                groups.Add(g);
                if (b.Child is Grid g2 && g2.Children.Count > 2 && g2.Children[2] is StackPanel sp)
                    CollectEntries(sp);
            }
            else if (child is Border eb && _entryIds.TryGetValue(eb, out var eid) && entryById.TryGetValue(eid, out var en))
            {
                entries.Add(en);
            }
            else if (child == _uncategorizedCard && _uncategorizedCard?.Child is Grid ug && ug.Children.Count > 2 && ug.Children[2] is Panel cp)
            {
                CollectEntries(cp);
            }
        }
        db.MemoGroups.Clear();
        db.MemoGroups.AddRange(groups);
        db.MemoEntries.Clear();
        db.MemoEntries.AddRange(entries);
        db.MemoEntries.AddRange(softDeleted); // keep soft-deleted items (trash) across the rebuild
        App.Store?.SaveAsync();
    }

    private MemoEntry? FindEntry(Guid id)
        => App.Store?.Database.MemoEntries.FirstOrDefault(x => x.Id == id);

    private MemoGroup? FindGroup(Guid id)
        => App.Store?.Database.MemoGroups.FirstOrDefault(x => x.Id == id);

    private void FloatInHint()
    {
        HintText.Visibility = Visibility.Visible;
        HintText.Opacity = 0;
        if (HintText.RenderTransform is not TranslateTransform tt)
        {
            tt = new TranslateTransform { Y = 20 };
            HintText.RenderTransform = tt;
        }
        else tt.Y = 20;
        var sb = new Storyboard();
        var oa = new DoubleAnimation { To = 0.6, Duration = TimeSpan.FromMilliseconds(400), EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
        Storyboard.SetTarget(oa, HintText); Storyboard.SetTargetProperty(oa, "Opacity");
        sb.Children.Add(oa);
        var ya = new DoubleAnimation { To = 0, Duration = TimeSpan.FromMilliseconds(400), EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
        Storyboard.SetTarget(ya, tt); Storyboard.SetTargetProperty(ya, "Y");
        sb.Children.Add(ya);
        sb.Begin();
    }

    private void ContentGrid_ContextRequested(UIElement sender, ContextRequestedEventArgs args)
    {
        if (args.TryGetPosition(sender, out var point))
            _contextMenu.ShowAt(sender, point);
    }

    private void OpenNewGroupDialog()
    {
        DialogHideAnimation.Stop();
        NewGroupDialogTransform.ScaleX = 0.92; NewGroupDialogTransform.ScaleY = 0.92;
        NewGroupDialogTransform.TranslateY = 20;
        NewGroupDialog.Opacity = 0; DialogScrim.Opacity = 0;
        NewGroupDialogOverlay.Visibility = Visibility.Visible;
        DialogDepth.VeilShow(); 
        DialogShowAnimation.Begin();
        _confirming = false; // E4-17: re-arm the one-shot guard when the dialog reopens
        InitializeIcons();
        LoadUngroupedEntries();
        // UI-2 (2026-08-11): live validation - the confirm button stays disabled until the name is non-blank.
        // Explicit IsEnabled=false is required: assigning "" to an already-empty TextBox does NOT raise
        // TextChanged, so the initial disabled state must not rely on the event.
        GroupNameTextBox.Text = string.Empty;
        ConfirmButton.IsEnabled = false;
    }

    private void GroupNameTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        ConfirmButton.IsEnabled = !string.IsNullOrWhiteSpace(GroupNameTextBox.Text); // UI-2: live validity -> disabled (grey, no hover) until valid
    }

    private void CloseNewGroupDialog()
    {
        DialogHideAnimation.Completed -= OnDialogHideCompleted;
        DialogHideAnimation.Completed += OnDialogHideCompleted;
        DialogDepth.VeilHide(); 
        DialogHideAnimation.Begin();
    }

    private void OnDialogHideCompleted(object? sender, object e)
    {
        DialogHideAnimation.Completed -= OnDialogHideCompleted;
        NewGroupDialogOverlay.Visibility = Visibility.Collapsed;
        _pendingMoveEntry = null!;
    }

    private void CloseDialogButton_Click(object sender, RoutedEventArgs e) => CloseNewGroupDialog();

    private void DialogScrim_Tapped(object sender, TappedRoutedEventArgs e) { if (ReferenceEquals(e.OriginalSource, DialogScrim)) CloseNewGroupDialog(); }

    private void InitializeIcons()
    {
        var iconFiles = new List<string>
        {
            "Group42.png",
            "Group43.png",
            "Group44.png",
            "Group45.png",
            "Group46.png",
            "Group47.png",
            "Group48.png",
            "Group49.png",
            "Group50.png",
            "Group51.png",
            "Group52.png",
            "Group53.png",
            "Group54.png",
            "Group55.png",
            "Group56.png",
            "Group57.png",
            "Group58.png",
            "Group59.png",
            "Group60.png",
            "Group61.png",
            "Group62.png",
            "Group63.png",
            "Group64.png",
            "Group65.png",
            "Group66.png",
            "Group67.png",
            "Group68.png",
            "Group69.png",
            "Group70.png",
            "Group71.png",
            "Group72.png",
            "Group73.png",
            "Group74.png",
            "Group75.png",
            "Group76.png",
            "Group77.png",
            "Group78.png",
            "Group79.png",
            "Group80.png",
            "Group81.png",
            "Group82.png",
            "Group83.png",
            "Group84.png",
            "Group85.png",
            "Group86.png",
            "Group87.png",
            "Group88.png",
            "Group89.png",
            "Group90.png",
            "Group91.png",
            "Group92.png",
            "Group93.png",
            "Group94.png",
            "Group95.png",
            "Group96.png",
            "Group97.png",
            "Group98.png",
            "Group99.png",
            "Group100.png",
            "API 接口_api.png", "VIP_vip.png", "钞票_paper-money.png", "磁带_tape.png",
            "大象_elephant.png", "点击_click.png", "电脑.png", "调色盘_platte.png",
            "发送_send.png", "复制链接.png", "沟通_communication.png", "购物车_shopping.png",
            "广告_ad.png", "广告产品_ad-product.png", "护照_passport-one.png",
            "金融_finance.png", "开心_emotion-happy.png", "两个椭圆_two-ellipses.png",
            "菱形3_diamond-three.png", "魔比斯环_cross-ring-two.png", "钱包_wallet-two.png",
            "趋势_trend.png", "三角形_triangle.png", "生气_angry-face.png",
            "生肖鼠_mouse-zodiac.png", "生肖猪_pig-zodiac.png", "实验_experiment-one.png",
            "世界_world.png", "水费_water-rate.png", "图片1_pic-one.png",
            "图形设计_graphic-design.png", "信息_message.png", "演出_performance.png",
            "衣架_coat-hanger.png", "银行卡_bank-card.png", "应用.png",
            "鹰_eagle.png", "邮件.png", "圆形_round.png", "云端.png",
            "提醒_reminder.png"
        };
        IconPanel.Children.Clear(); _iconBorders.Clear();
        foreach (var file in iconFiles)
        {
            var tag = System.IO.Path.GetFileNameWithoutExtension(file);
            var border = new Border
            {
                Width = 48, Height = 48, CornerRadius = new CornerRadius(12),
                Background = App.GetBrush("AppSurfaceOverlayBrush"),
                Tag = tag,
                Child = IconData.GroupIconMap.TryGetValue(tag, out var key)
                    ? new Viewbox { Width = 24, Height = 24, Stretch = Stretch.Uniform, Child = new PathIcon { Data = App.CreateGeometry(IconData.GetGroupPath(key)), Foreground = App.GetBrush("IconForegroundBrush"), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center } }
                    : null
            };
            border.Tapped += Icon_Tapped;
            IconPanel.Children.Add(border);
            _iconBorders.Add(border);
        }
        if (_iconBorders.Count > 0) { _selectedIcon = ""; } 
    }

    private void Icon_Tapped(object sender, TappedRoutedEventArgs e)
    {
        if (sender is Border border) { _selectedIcon = border.Tag.ToString() ?? ""; HighlightIcon(border); CenterIconInScrollViewer(border); }
    }

    private void HighlightIcon(Border selected)
    {
        foreach (var icon in _iconBorders)
        {
            if (icon == selected)
            {
                icon.Background = App.GetBrush("AppSurfaceBrush");
                icon.BorderBrush = App.GetBrush("AppTextTertiaryBrush");
                icon.BorderThickness = new Thickness(1);
            }
            else
            {
                icon.Background = App.GetBrush("AppSurfaceOverlayBrush");
                icon.BorderBrush = null; icon.BorderThickness = new Thickness(0);
            }
        }
    }

    private void CenterIconInScrollViewer(Border targetIcon)
    {
        var transform = targetIcon.TransformToVisual(IconPanel);
        var position = transform.TransformPoint(new Point(0, 0));
        double targetOffset = position.X - (IconScrollViewer.ViewportWidth / 2) + (targetIcon.ActualWidth / 2);
        targetOffset = Math.Max(0, Math.Min(targetOffset, IconScrollViewer.ScrollableWidth));
        IconScrollViewer.ChangeView(targetOffset, null, null, false);
    }

    
    

    /// <summary>Populate the custom-entry icon selector (idempotent - built once, reuse across opens).</summary>
    private void LoadEntryIconSelector()
    {
        if (EntryIconPanel.Children.Count > 0) return; // already built
        EntryIconPanel.Children.Clear(); _entryIconBorders.Clear();
        var cv = Microsoft.UI.Xaml.Markup.XamlBindingHelper.ConvertValue;
        foreach (var key in IconData.GroupIconKeysInOrder())
        {
            var b = new Border
            {
                Width = 48, Height = 48, CornerRadius = new CornerRadius(12),
                Background = App.GetBrush("AppSurfaceOverlayBrush"),
                Tag = key,
                Child = new Viewbox { Width = 24, Height = 24, Stretch = Stretch.Uniform, Child = new PathIcon { Data = App.CreateGeometry(IconData.GetGroupPath(key)), Foreground = App.GetBrush("IconForegroundBrush") } }
            };
            b.Tapped += EntryIcon_Tapped;
            EntryIconPanel.Children.Add(b);
            _entryIconBorders.Add(b);
        }
        _entrySelectedIcon = ""; // no default selection: empty = legacy jigsaw
    }

    private void EntryIcon_Tapped(object sender, TappedRoutedEventArgs e)
    {
        if (sender is Border border)
        {
            _entrySelectedIcon = border.Tag?.ToString() ?? "";
            HighlightEntryIcon(border);
            CenterEntryIconInScrollViewer(border);
            UpdateNewEntryConfirmState(); 
        }
    }

    private void HighlightEntryIcon(Border selected)
    {
        foreach (var icon in _entryIconBorders)
        {
            if (icon == selected)
            {
                icon.Background = App.GetBrush("AppSurfaceBrush");
                icon.BorderBrush = App.GetBrush("AppTextTertiaryBrush");
                icon.BorderThickness = new Thickness(1);
            }
            else
            {
                icon.Background = App.GetBrush("AppSurfaceOverlayBrush");
                icon.BorderBrush = null; icon.BorderThickness = new Thickness(0);
            }
        }
    }

    private void CenterEntryIconInScrollViewer(Border targetIcon)
    {
        var transform = targetIcon.TransformToVisual(EntryIconPanel);
        var position = transform.TransformPoint(new Point(0, 0));
        double targetOffset = position.X - (EntryIconScrollViewer.ViewportWidth / 2) + (targetIcon.ActualWidth / 2);
        targetOffset = Math.Max(0, Math.Min(targetOffset, EntryIconScrollViewer.ScrollableWidth));
        EntryIconScrollViewer.ChangeView(targetOffset, null, null, false);
    }

    /// <summary>Reset the custom-entry icon selection (empty = legacy jigsaw) and clear any highlight.</summary>
    private void ClearEntryIconSelection()
    {
        _entrySelectedIcon = "";
        foreach (var icon in _entryIconBorders)
        {
            icon.Background = App.GetBrush("AppSurfaceOverlayBrush");
            icon.BorderBrush = null; icon.BorderThickness = new Thickness(0);
        }
    }

    private void LoadUngroupedEntries()
    {
        UngroupedEntriesPanel.Children.Clear();

        
        bool singleMove = _pendingMoveEntry != null;

        
        
        var entries = new List<Border>();
        if (!singleMove)
        {
            entries.AddRange(_standaloneEntries);
            if (_uncategorizedCard?.Child is Grid ug && ug.Children.Count > 2 && ug.Children[2] is StackPanel sp)
                foreach (var child in sp.Children)
                    if (child is Border b && !entries.Contains(b))
                        entries.Add(b);
        }

        foreach (var entry in entries)
        {
            string entryName = App.GetString("Memo_Entry_Untitled");
            if (entry.Child is Grid rootGrid && rootGrid.Children.Count > 0 && rootGrid.Children[0] is Grid mainRow && mainRow.Children.Count > 1 && mainRow.Children[1] is StackPanel sp && sp.Children.Count > 0 && sp.Children[0] is TextBlock tb)
                entryName = tb.Text;

            var checkBox = new CheckBox
            {
                Content = entryName,
                Tag = entry,
                Foreground = App.GetBrush("AppTextPrimaryBrush"),
                FontSize = 13
            };
            UngroupedEntriesPanel.Children.Add(checkBox);
        }

        UngroupedListSection.Visibility = entries.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private Border CreateGroupCard(string groupName, string groupIconKey)
    {
        var card = new Border
        {
            CornerRadius = new CornerRadius(12),
            Background = App.GetBrush("AppSurfaceOverlayBrush"),
            BorderBrush = new SolidColorBrush(App.GetBrush("AppBorderBrush").Color), // E1-09: per-card copy - never mutate the shared theme brush
            BorderThickness = new Thickness(1),
            Padding = new Thickness(16),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Top
        };
        var grid = new Grid();
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(36, GridUnitType.Pixel) });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        var titleGrid = new Grid { Height = 36 };
        titleGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        titleGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        titleGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var icon = new Viewbox { Width = 18, Height = 18, Stretch = Microsoft.UI.Xaml.Media.Stretch.Uniform, VerticalAlignment = VerticalAlignment.Center, Child = new PathIcon { Data = App.CreateGeometry(IconData.GetGroupPath(groupIconKey)), Foreground = App.GetBrush("IconForegroundBrush") } };
        Grid.SetColumn(icon, 0);
        var nameText = new TextBlock { Text = groupName, FontSize = 14, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, Foreground = App.GetBrush("AppTextPrimaryBrush"), VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(8, 0, 0, 0) };
        Grid.SetColumn(nameText, 1);
        var rightPanel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, VerticalAlignment = VerticalAlignment.Center };
        var pinIcon = new Viewbox { Width = 16, Height = 16, Stretch = Microsoft.UI.Xaml.Media.Stretch.Uniform, VerticalAlignment = VerticalAlignment.Center, Visibility = Visibility.Collapsed, Margin = new Thickness(0, 0, 12, 0), Child = new PathIcon { Data = App.CreateGeometry(IconData.CardPin), Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(0xFF, 0x72, 0x76, 0xFF)) } };
        rightPanel.Children.Add(pinIcon);
        var starIcon = new Viewbox { Width = 16, Height = 16, Stretch = Microsoft.UI.Xaml.Media.Stretch.Uniform, VerticalAlignment = VerticalAlignment.Center, Visibility = Visibility.Collapsed, Margin = new Thickness(0, 0, 12, 0), Child = new PathIcon { Data = App.CreateGeometry(IconData.CardStar), Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(0xFF, 0x72, 0x76, 0xFF)) } };
        rightPanel.Children.Add(starIcon);
        var countText = new TextBlock { Text = App.GetString("Memo_Count_Zero"), FontSize = 12, Foreground = App.GetBrush("AppTextTertiaryBrush"), VerticalAlignment = VerticalAlignment.Center };
        var iconText2 = new TextBlock { Text = "\uE70D", FontFamily = new FontFamily("Segoe Fluent Icons"), FontSize = 10, Foreground = App.GetBrush("AppTextTertiaryBrush"), VerticalAlignment = VerticalAlignment.Center };
        rightPanel.Children.Add(countText);
        rightPanel.Children.Add(iconText2);
        Grid.SetColumn(rightPanel, 2);
        _pinIcons[card] = pinIcon; _starIcons[card] = starIcon; _groupNameTexts[card] = nameText;
        _groupCountTexts[card] = countText;
        _groupData[card] = (groupName, groupIconKey);
        titleGrid.Children.Add(icon); titleGrid.Children.Add(nameText); titleGrid.Children.Add(rightPanel);
        Grid.SetRow(titleGrid, 0);
        var divider = new Border { Height = 1, Background = App.GetBrush("AppBorderBrush"), Margin = new Thickness(0, 12, 0, 0), Opacity = 0.3 };
        Grid.SetRow(divider, 1);
        var entryStack = new StackPanel { Spacing = 8, Margin = new Thickness(0, 12, 0, 0) };
        Grid.SetRow(entryStack, 2);

        var emptyHint = new Grid { MinHeight = 100, Margin = new Thickness(0, 12, 0, 0), Visibility = Visibility.Visible };
        var emptyStack = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Spacing = 8 };
        emptyStack.Children.Add(new TextBlock { Text = "\uE721", FontFamily = new FontFamily("Segoe Fluent Icons"), FontSize = 28, Foreground = App.GetBrush("AppTextTertiaryBrush"), Opacity = 0.3, HorizontalAlignment = HorizontalAlignment.Center });
        emptyStack.Children.Add(new TextBlock { Text = App.GetString("Memo_Group_Empty_Tip"), HorizontalAlignment = HorizontalAlignment.Center, FontSize = 13, Foreground = App.GetBrush("AppTextSecondaryBrush"), Opacity = 0.7 });
        emptyStack.Children.Add(new TextBlock { Text = App.GetString("Memo_Group_Empty_Hint"), HorizontalAlignment = HorizontalAlignment.Center, FontSize = 12, Foreground = App.GetBrush("AppTextTertiaryBrush"), Opacity = 0.5 });
        emptyHint.Children.Add(emptyStack);
        Grid.SetRow(emptyHint, 2);

        grid.Children.Add(titleGrid); grid.Children.Add(divider); grid.Children.Add(entryStack); grid.Children.Add(emptyHint);
        card.Child = grid;
        var translate = new TranslateTransform { Y = 0 }; card.RenderTransform = translate;
        var borderBrush = App.GetBrush("AppBorderBrush");
        var hoverBorderColor = Color.FromArgb(
            (byte)Math.Min(borderBrush.Color.A + 0x30, 0xFF),
            borderBrush.Color.R,
            borderBrush.Color.G,
            borderBrush.Color.B);
        var baseBorderColor = borderBrush.Color;
        card.PointerEntered += (s, e) => { if (_dragging) return; if (card.BorderBrush is SolidColorBrush sb) sb.Color = hoverBorderColor; var st = new Storyboard(); var la = new DoubleAnimation { To = -3, Duration = TimeSpan.FromMilliseconds(200), EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } }; Storyboard.SetTarget(la, translate); Storyboard.SetTargetProperty(la, "Y"); st.Children.Add(la); st.Begin(); }; // E2-05: manual color swap (was ColorAnimation - 0xc000027b-sensitive during page switches)
        card.PointerExited += (s, e) => { if (_dragging) return; if (card.BorderBrush is SolidColorBrush sb) sb.Color = baseBorderColor; var st = new Storyboard(); var la = new DoubleAnimation { To = 0, Duration = TimeSpan.FromMilliseconds(300), EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } }; Storyboard.SetTarget(la, translate); Storyboard.SetTargetProperty(la, "Y"); st.Children.Add(la); st.Begin(); };
        card.ContextRequested += (s, e) => { e.Handled = true; _currentGroupCard = card; bool ip = card == _pinnedGroupCard; bool starred = _starredCards.Contains(card); var m = BuildGroupCardContextMenu(ip, starred); if (e.TryGetPosition(card, out var p)) m.ShowAt(card, p); };
        AttachCardDrag(card);
        return card;
    }

    private void UpdateGroupCount(Border groupCard)
    {
        if (groupCard == null || !_groupCountTexts.TryGetValue(groupCard, out var ct)) return;
        int count = 0;
        if (groupCard.Child is Grid g && g.Children.Count > 2 && g.Children[2] is StackPanel entryStack)
            count = entryStack.Children.Count;
        ct.Text = string.Format(App.GetString("Memo_Count_N"), count);
    }


    private static string TypeLabel(string type) => type switch
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

    private static string FieldLabelText(string label) => label switch
    {
        "邮箱地址" => App.GetString("Memo_Field_EmailAddr"),
        "邮箱密码" => App.GetString("Memo_Field_EmailPwd"),
        "账号" => App.GetString("Memo_Field_Account"),
        "密码" => App.GetString("Memo_Field_Password"),
        "API Key" => App.GetString("Memo_Type_ApiKey"),
        "URL" => App.GetString("Memo_Field_Url"),
        "网址" => App.GetString("Memo_Field_Website"),
        "模型 ID" => App.GetString("Memo_Field_ModelIdShort"),
        "备注" => App.GetString("Common_Label_Note"),
        "卡号" => App.GetString("Memo_Field_CardNumber"),
        "持卡人" => App.GetString("Memo_Field_CardHolder"),
        "有效期" => App.GetString("Memo_Field_Expiry"),
        "CVV" => App.GetString("Memo_Field_Cvv"),
        "网络名" => App.GetString("Memo_Field_Ssid"),
        "证件号" => App.GetString("Memo_Field_IdNumber"),
        "姓名" => App.GetString("Memo_Field_FullName"),
        "签发机构" => App.GetString("Memo_Field_Issuer"),
        "信息" => App.GetString("Memo_Entry_InfoLabel"),
        _ => label
    };

    // Maps a stored field label back to the correct form slot for the given entry type.
    // Label-based (not position-based) so editing older entries keeps working after a layout change.
    private TextBox? FieldSlotFor(string type, string label) => type switch
    {
        "邮箱" => label switch { "邮箱地址" => FormField2TextBox, "邮箱密码" => FormField3TextBox, "备注" => FormField4TextBox, _ => null },
        "账户" => label switch { "账号" => FormField2TextBox, "密码" => FormField3TextBox, "网址" => FormField4TextBox, "备注" => FormField5TextBox, _ => null },
        "API Key" => label switch { "API Key" => FormField2TextBox, "URL" => FormField3TextBox, "模型 ID" => FormField4TextBox, "备注" => FormField5TextBox, _ => null },
        "网站" => label switch { "网址" => FormField2TextBox, "账号" => FormField3TextBox, "密码" => FormField4TextBox, "备注" => FormField5TextBox, _ => null },
        "银行卡" => label switch { "卡号" => FormField2TextBox, "持卡人" => FormField3TextBox, "有效期" => FormField4TextBox, "CVV" => FormField5TextBox, "密码" => FormField6TextBox, "备注" => FormField7TextBox, _ => null },
        "WiFi" => label switch { "网络名" => FormField2TextBox, "密码" => FormField3TextBox, "备注" => FormField4TextBox, _ => null },
        "证件" => label switch { "证件号" => FormField2TextBox, "姓名" => FormField3TextBox, "签发机构" => FormField4TextBox, "有效期" => FormField5TextBox, "备注" => FormField6TextBox, _ => null },
        _ => null
    };

    
    private bool EntryContentChanged(string type)
    {
        if (type == "自定义")
        {
            if (CustomNameTextBox.Text.Trim() != _editOrigName) return true;
            if (_entrySelectedIcon != _editOrigIcon) return true;
            var cur = new List<string>();
            if (!string.IsNullOrWhiteSpace(CustomInfoTextBox_0.Text)) cur.Add(CustomInfoTextBox_0.Text.Trim());
            foreach (var child in CustomInfoDynamicPanel.Children)
                if (child is TextBox tb && !string.IsNullOrWhiteSpace(tb.Text)) cur.Add(tb.Text.Trim());
            var orig = _editOrigFields.Select(f => f.value).ToList();
            if (cur.Count != orig.Count) return true;
            for (int i = 0; i < cur.Count; i++) if (cur[i] != orig[i]) return true;
            return false;
        }

        if (EntryTypeFieldLabels.TryGetValue(type, out var labels))
        {
            if (EntryNameTextBox.Text.Trim() != _editOrigName) return true;
            foreach (var label in labels)
            {
                string cur = FieldSlotFor(type, label)?.Text.Trim() ?? "";
                string orig = _editOrigFields.FirstOrDefault(f => f.label == label).value ?? "";
                if (cur != orig) return true;
            }
            return false;
        }

        return true; 
    }

    private Border CreateEntryCard(string type, string name, string keyInfo, List<(string label, string value, bool canCopy)> fields, string iconKey = "")
    {
        
        var typeIconGeom = App.CreateGeometry(type switch { "邮箱" => IconData.Email, "账户" => IconData.Account[0] + " " + IconData.Account[1], "API Key" => "M640.45 480.574c17.496 0 31.713 14.041 31.996 31.47l0.004 0.53V811.05c0 88.338-71.612 159.95-159.95 159.95-87.454 0-158.516-70.187-159.929-157.305l-0.021-2.645v-43.29c0-17.673 14.327-32 32-32 17.496 0 31.713 14.042 31.996 31.47l0.004 0.53v43.29c0 52.991 42.958 95.95 95.95 95.95 52.462 0 95.09-42.104 95.937-94.364l0.013-1.586V512.574c0-17.674 14.327-32 32-32zM256.357 352.55c17.673 0 32 14.327 32 32 0 17.496-14.042 31.713-31.47 31.995l-0.53 0.005h-42.745c-52.786 0-95.612 42.94-95.612 95.95 0 52.48 41.974 95.09 94.031 95.938l1.581 0.012H512.5c17.673 0 32 14.327 32 32 0 17.497-14.042 31.714-31.47 31.996l-0.53 0.004H213.612c-88.17 0-159.612-71.63-159.612-159.95 0-87.436 70.02-158.516 156.972-159.929l2.64-0.021h42.745z m554.454 0C899.268 352.55 971 424.15 971 512.5c0 87.468-70.305 158.517-157.54 159.929l-2.649 0.021h-40.997c-17.673 0-32-14.327-32-32 0-17.496 14.042-31.713 31.47-31.995l0.53-0.005h40.997c53.137 0 96.189-42.971 96.189-95.95 0-52.449-42.195-95.09-94.598-95.937l-1.59-0.013H512.5c-17.673 0-32-14.327-32-32 0-17.497 14.042-31.713 31.47-31.996l0.53-0.004h298.311zM512.5 54c87.454 0 158.516 70.187 159.929 157.305l0.021 2.645v42.772c0 17.673-14.327 32-32 32-17.496 0-31.713-14.042-31.996-31.47l-0.004-0.53V213.95c0-52.992-42.958-95.95-95.95-95.95-52.462 0-95.09 42.104-95.937 94.364l-0.013 1.586v297.212c0 17.673-14.327 32-32 32-17.496 0-31.713-14.042-31.996-31.471l-0.004-0.53v-297.21C352.55 125.611 424.162 54 512.5 54z", "网站" => IconData.Website, "银行卡" => IconData.BankCard, "WiFi" => string.Join(" ", IconData.WiFi), "证件" => string.Join(" ", IconData.IdCard), _ => !string.IsNullOrEmpty(iconKey) ? IconData.GetGroupPath(iconKey) : IconData.Custom });
        var card = new Border { CornerRadius = new CornerRadius(14), Background = App.GetBrush("AppSurfaceOverlayBrush"), BorderBrush = new SolidColorBrush(App.GetBrush("AppBorderBrush").Color), BorderThickness = new Thickness(1), Padding = new Thickness(12, 10, 12, 10), HorizontalAlignment = HorizontalAlignment.Stretch, VerticalAlignment = VerticalAlignment.Top }; // E1-09: per-card copy - never mutate the shared theme brush
        var rootGrid = new Grid(); rootGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); rootGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var mainRow = new Grid(); mainRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); mainRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); mainRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); mainRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); mainRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var typeIconViewbox = new Viewbox { Width = 24, Height = 24, Stretch = Microsoft.UI.Xaml.Media.Stretch.Uniform, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, -2, 10, 0), Child = new PathIcon { Data = typeIconGeom, Foreground = App.GetBrush("IconForegroundBrush") } };
        Grid.SetColumn(typeIconViewbox, 0);
        var infoPanel = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) };
        infoPanel.Children.Add(new TextBlock { Text = name, FontSize = 13, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, Foreground = App.GetBrush("AppTextPrimaryBrush"), TextTrimming = TextTrimming.CharacterEllipsis });
        infoPanel.Children.Add(new TextBlock { Text = keyInfo, FontSize = 11, Foreground = App.GetBrush("AppTextTertiaryBrush"), TextTrimming = TextTrimming.CharacterEllipsis });
        Grid.SetColumn(infoPanel, 1);

        var entryPinIcon = new Viewbox
        {
            Width = 16, Height = 16,
            Stretch = Microsoft.UI.Xaml.Media.Stretch.Uniform,
            VerticalAlignment = VerticalAlignment.Center,
            Visibility = Visibility.Collapsed,
            Margin = new Thickness(0, 0, 12, 0),
            Child = new PathIcon
            {
                Data = App.CreateGeometry(IconData.CardPin),
                Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(0xFF, 0x72, 0x76, 0xFF))
            }
        };
        Grid.SetColumn(entryPinIcon, 2);

        var entryStarIcon = new Viewbox
        {
            Width = 16, Height = 16,
            Stretch = Microsoft.UI.Xaml.Media.Stretch.Uniform,
            VerticalAlignment = VerticalAlignment.Center,
            Visibility = Visibility.Collapsed,
            Margin = new Thickness(0, 0, 12, 0),
            Child = new PathIcon
            {
                Data = App.CreateGeometry(IconData.CardStar),
                Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(0xFF, 0x72, 0x76, 0xFF))
            }
        };
        Grid.SetColumn(entryStarIcon, 3);

        var expandedGrid = new Grid { Visibility = Visibility.Collapsed, Margin = new Thickness(0, 12, 0, 0) }; expandedGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); expandedGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var expandIconPath = App.CreateGeometry(IconData.CardExpand);
        var collapseIconPath = App.CreateGeometry(IconData.CardCollapse);
        var expandIcon = new PathIcon { Data = expandIconPath, Foreground = App.GetBrush("IconForegroundBrush") };
        var expandViewbox = new Viewbox { Width = 16, Height = 16, Stretch = Microsoft.UI.Xaml.Media.Stretch.Uniform, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Child = expandIcon };
        var expandBtn = new Button { Width = 32, Height = 32, Style = (Style)Application.Current.Resources["NovaraIconButtonStyle"], VerticalAlignment = VerticalAlignment.Center, Content = expandViewbox, IsTabStop = false };
        expandBtn.Click += (s, e) =>
        {
            var st = _entryExpand.TryGetValue(card, out var v) ? v : (null, false);
            bool targetExpand = !st.Item2;
            if (st.Cts != null) st.Cts.Cancel();
            var cts = new CancellationTokenSource();
            _entryExpand[card] = (cts, targetExpand);
            expandIcon.Data = targetExpand ? collapseIconPath : expandIconPath;
            expandViewbox.Margin = targetExpand ? new Thickness(0) : new Thickness(-2, -1, 0, 0);
            StartEntryMelt(expandedGrid, targetExpand, cts.Token);
        };
        Grid.SetColumn(expandBtn, 4);
        mainRow.Children.Add(typeIconViewbox); mainRow.Children.Add(infoPanel); mainRow.Children.Add(entryPinIcon); mainRow.Children.Add(entryStarIcon); mainRow.Children.Add(expandBtn); Grid.SetRow(mainRow, 0);
        _entryPinIcons[card] = entryPinIcon;
        _entryStarIcons[card] = entryStarIcon;
        _entryData[card] = (type, name, keyInfo, fields);
        var divider2 = new Border { Height = 1, Background = App.GetBrush("AppBorderBrush"), Opacity = 0.3, Margin = new Thickness(0, 0, 0, 10) }; Grid.SetRow(divider2, 0);
        var fieldsPanel = new StackPanel { Spacing = 14 };
        foreach (var (label, value, canCopy) in fields)
        {
            var row = new Grid { ColumnSpacing = 8 }; row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.Children.Add(new TextBlock { Text = FieldLabelText(label), FontSize = 12, Foreground = App.GetBrush("AppTextSecondaryBrush"), VerticalAlignment = VerticalAlignment.Center });
            var recess = new Border { Background = App.GetBrush("AppSurfaceBrush"), CornerRadius = new CornerRadius(6), Padding = new Thickness(10, 6, 10, 6) }; Grid.SetColumn(recess, 1);
            var innerGrid = new Grid { MinHeight = 24 }; innerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); innerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            innerGrid.Children.Add(new TextBlock { Text = value, FontSize = 12, CharacterSpacing = App.GetCharacterSpacing(value, 150), Foreground = App.GetBrush("AppTextPrimaryBrush"), VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis });
            if (canCopy) { var cb = new Button { Width = 24, Height = 24, Style = (Style)Application.Current.Resources["NovaraIconButtonStyle"], Background = new SolidColorBrush(Color.FromArgb(0, 0, 0, 0)), BorderThickness = new Thickness(0), Padding = new Thickness(4), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0, 0, 0), Tag = value, IsTabStop = false, Content = new Viewbox { Width = 12, Height = 12, Stretch = Microsoft.UI.Xaml.Media.Stretch.Uniform, Child = new PathIcon { Data = App.CreateGeometry(IconData.Copy), Foreground = App.GetBrush("IconForegroundBrush") } } }; cb.Click += CopyToClipboardButton_Click; Grid.SetColumn(cb, 1); innerGrid.Children.Add(cb); }
            recess.Child = innerGrid; row.Children.Add(recess); fieldsPanel.Children.Add(row);
        }
        Grid.SetRow(fieldsPanel, 1); expandedGrid.Children.Add(divider2); expandedGrid.Children.Add(fieldsPanel); Grid.SetRow(expandedGrid, 1);
        rootGrid.Children.Add(mainRow); rootGrid.Children.Add(expandedGrid); card.Child = rootGrid;

        // 4.0: entry-card hover (parity with group cards) - brighten border + lift 3px.
        // e.Handled blocks bubbling so a nested entry doesn't also lift its parent group card.
        var translate = new TranslateTransform { Y = 0 }; card.RenderTransform = translate;
        var borderBrush = App.GetBrush("AppBorderBrush");
        var hoverBorderColor = Color.FromArgb(
            (byte)Math.Min(borderBrush.Color.A + 0x30, 0xFF),
            borderBrush.Color.R,
            borderBrush.Color.G,
            borderBrush.Color.B);
        var baseBorderColor = borderBrush.Color;
        card.PointerEntered += (s, e) => { e.Handled = true; if (_dragging) return; if (card.BorderBrush is SolidColorBrush sb) sb.Color = hoverBorderColor; var st = new Storyboard(); var la = new DoubleAnimation { To = -3, Duration = TimeSpan.FromMilliseconds(200), EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } }; Storyboard.SetTarget(la, translate); Storyboard.SetTargetProperty(la, "Y"); st.Children.Add(la); st.Begin(); };
        card.PointerExited += (s, e) => { e.Handled = true; if (_dragging) return; if (card.BorderBrush is SolidColorBrush sb) sb.Color = baseBorderColor; var st = new Storyboard(); var la = new DoubleAnimation { To = 0, Duration = TimeSpan.FromMilliseconds(300), EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } }; Storyboard.SetTarget(la, translate); Storyboard.SetTargetProperty(la, "Y"); st.Children.Add(la); st.Begin(); };

        card.ContextRequested += (s, e) =>
        {
            e.Handled = true;
            var m = new MenuFlyout();
            m.MenuFlyoutPresenterStyle = (Style)Application.Current.Resources["GlassMenuFlyoutPresenterStyle"];

            var editMi = new MenuFlyoutItem { Style = (Style)Application.Current.Resources["GlassMenuFlyoutItemStyle"], Text = App.GetString("Menu_Edit"), Icon = new PathIcon { Data = App.CreateGeometry(IconData.Edit[0] + " " + IconData.Edit[1]), Foreground = App.GetBrush("IconForegroundBrush") } };
            editMi.Click += (s2, e2) =>
            {
                if (!_entryData.ContainsKey(card)) return;
                _editingEntryCard = card;
                var d = _entryData[card];
                
                _editOrigName = d.name;
                _editOrigIcon = _entryIconKeys.TryGetValue(card, out var oik) ? oik : "";
                _editOrigFields = d.fields.Select(f => (label: f.Item1, value: f.Item2)).ToList();
                _selectedEntryType = d.type;
                NewEntryDialogTitle.Text = App.GetString("Memo_Entry_EditTitle");
                EntryTypeSection.Visibility = Visibility.Collapsed;
                OpenNewEntryDialog();

                _selectedEntryType = d.type;
                EntryTypeButtonText.Text = TypeLabel(d.type);
                EntryTypeButtonText.Foreground = App.GetBrush("AppTextPrimaryBrush");
                ApplyEntryFormLayout(d.type);
                if (d.type == "自定义")
                {
                    CustomNameTextBox.Text = d.name;
                    if (d.fields.Count > 0) CustomInfoTextBox_0.Text = d.fields[0].Item2;

                    
                    ClearEntryIconSelection(); 
                    _entrySelectedIcon = _entryIconKeys.TryGetValue(card, out var ik) ? ik : "";
                    if (!string.IsNullOrEmpty(_entrySelectedIcon))
                    {
                        var match = _entryIconBorders.FirstOrDefault(b => b.Tag?.ToString() == _entrySelectedIcon);
                        if (match != null) HighlightEntryIcon(match);
                    }

                    CustomInfoDynamicPanel.Children.Clear();
                    _customInfoCount = 1;
                    for (int i = 1; i < d.fields.Count; i++)
                    {
                        var dtb = CreateCustomInfoTextBox(_customInfoCount);
                        dtb.Text = d.fields[i].Item2;
                        CustomInfoDynamicPanel.Children.Add(dtb);
                        _customInfoCount++;
                    }
                    UpdateCustomInfoButtonVisibility();
                }
                else
                {
                    EntryNameTextBox.Text = d.name;
                    foreach (var f in d.fields)
                    {
                        var slot = FieldSlotFor(d.type, f.Item1);
                        if (slot != null) slot.Text = f.Item2;
                    }
                }
                
                UpdateNewEntryConfirmState();
            };

            bool entryPinned = _pinnedEntryCard == card;
            var pinMi = new MenuFlyoutItem { Style = (Style)Application.Current.Resources["GlassMenuFlyoutItemStyle"], Text = entryPinned ? App.GetString("Menu_Unpin") : App.GetString("Menu_Pin"), Icon = new PathIcon { Data = App.CreateGeometry(entryPinned ? IconData.Unpin : IconData.Pin), Foreground = App.GetBrush("IconForegroundBrush") } };
            pinMi.Click += (s2, e2) => { if (entryPinned) UnpinEntryCard(card); else PinEntryCard(card); };

            var starMi = new MenuFlyoutItem { Style = (Style)Application.Current.Resources["GlassMenuFlyoutItemStyle"], Text = _starredEntries.Contains(card) ? App.GetString("Menu_Unstar") : App.GetString("Menu_Star"), Icon = new PathIcon { Data = App.CreateGeometry(_starredEntries.Contains(card) ? IconData.Unstar : IconData.Star), Foreground = App.GetBrush("IconForegroundBrush") } };
            starMi.Click += (s2, e2) => { if (_starredEntries.Contains(card)) UnstarEntryCard(card); else StarEntryCard(card); };

            bool inGroup = _entriesInGroup.Contains(card);
            var moveSub = new MenuFlyoutSubItem { Style = (Style)Application.Current.Resources["GlassMenuFlyoutSubItemStyle"], Text = inGroup ? App.GetString("Menu_MoveOut") : App.GetString("Menu_MoveIn"), Icon = new PathIcon { Data = App.CreateGeometry(inGroup ? IconData.MoveOut[0] + " " + IconData.MoveOut[1] : IconData.MoveIn[0] + " " + IconData.MoveIn[1]), Foreground = App.GetBrush("IconForegroundBrush") } };

            if (inGroup)
            {
                
                foreach (var child in GroupsContainer.Children)
                {
                    if (child is Border gb && gb != card && _groupNameTexts.ContainsKey(gb))
                    {
                        var gn = _groupNameTexts[gb].Text;
                        var gIcon = _groupData.TryGetValue(gb, out var gd) ? gd.iconKey : "";
                        var gItem = new MenuFlyoutItem { Style = (Style)Application.Current.Resources["GlassMenuFlyoutItemStyle"], Text = gn, Icon = new PathIcon { Data = App.CreateGeometry(IconData.GetGroupPath(gIcon)), Foreground = App.GetBrush("IconForegroundBrush") } };
                        gItem.Click += (s3, e3) => MoveEntryToGroup(card, gb);
                        moveSub.Items.Add(gItem);
                    }
                }
                var uncatItem = new MenuFlyoutItem { Style = (Style)Application.Current.Resources["GlassMenuFlyoutItemStyle"], Text = App.GetString("Memo_Group_Uncategorized"), Icon = new PathIcon { Data = App.CreateGeometry(IconData.UncategorizedTag), Foreground = App.GetBrush("IconForegroundBrush") } };
                uncatItem.Click += (s3, e3) => MoveEntryOut(card);
                moveSub.Items.Add(uncatItem);
            }
            else
            {
                
                bool hasGroup = false;
                foreach (var child in GroupsContainer.Children)
                {
                    if (child is Border gb && gb != card && _groupNameTexts.ContainsKey(gb))
                    {
                        hasGroup = true;
                        var gn = _groupNameTexts[gb].Text;
                        var gIcon = _groupData.TryGetValue(gb, out var gd) ? gd.iconKey : "";
                        var gItem = new MenuFlyoutItem { Style = (Style)Application.Current.Resources["GlassMenuFlyoutItemStyle"], Text = gn, Icon = new PathIcon { Data = App.CreateGeometry(IconData.GetGroupPath(gIcon)), Foreground = App.GetBrush("IconForegroundBrush") } };
                        gItem.Click += (s3, e3) => MoveEntryToGroup(card, gb);
                        moveSub.Items.Add(gItem);
                    }
                }
                if (!hasGroup)
                {
                    var newGroupItem = new MenuFlyoutItem { Style = (Style)Application.Current.Resources["GlassMenuFlyoutItemStyle"], Text = App.GetString("Menu_NewGroup_More"), Icon = new PathIcon { Data = App.CreateGeometry(IconData.NewGroup), Foreground = App.GetBrush("IconForegroundBrush") } };
                    newGroupItem.Click += (s3, e3) =>
                    {
                        _pendingMoveEntry = card;
                        UngroupedListSection.Visibility = Visibility.Collapsed;
                        OpenNewGroupDialog();
                    };
                    moveSub.Items.Add(newGroupItem);
                }
            }

            var sep = new MenuFlyoutSeparator();
            var delMi = new MenuFlyoutItem { Style = (Style)Application.Current.Resources["GlassMenuFlyoutItemStyle"], Text = App.GetString("Menu_Delete"), Foreground = new SolidColorBrush(Color.FromArgb(0xFF, 0xFF, 0x45, 0x45)), Icon = new PathIcon { Data = App.CreateGeometry(IconData.SoftDelete), Foreground = new SolidColorBrush(Color.FromArgb(0xFF, 0xFF, 0x45, 0x45)) } };
            delMi.Click += (s2, e2) => { _currentEntryCard = card; OpenDeleteConfirmDialog(isGroup: false); };

            m.Items.Add(pinMi);
            m.Items.Add(starMi);
            if (_entryData.TryGetValue(card, out var websiteData) && websiteData.type == "网站")
            {
                // 4.0 #12: website-type entries get a one-click "open in browser" (the URL is the
                // entry's keyInfo; a missing scheme is prepended with https://).
                var openMi = new MenuFlyoutItem { Style = (Style)Application.Current.Resources["GlassMenuFlyoutItemStyle"], Text = App.GetString("Menu_OpenWebsite"), Icon = new PathIcon { Data = App.CreateGeometry(IconData.OpenWebsite), Foreground = App.GetBrush("IconForegroundBrush") } };
                openMi.Click += (s2, e2) => OpenWebsite(websiteData.keyInfo);
                m.Items.Add(openMi);
            }
            if (_entryData.TryGetValue(card, out var apiEntryData) && apiEntryData.type == "API Key")
            {
                var checkMi = new MenuFlyoutItem { Style = (Style)Application.Current.Resources["GlassMenuFlyoutItemStyle"], Text = App.GetString("Menu_Detect"), Icon = new PathIcon { Data = App.CreateGeometry(IconData.ApiDetect), Foreground = App.GetBrush("IconForegroundBrush") } };
                checkMi.Click += (s2, e2) => ShowApiCheckDialog(card);
                m.Items.Add(checkMi);

                
                var statusDiagMi = new MenuFlyoutItem { Style = (Style)Application.Current.Resources["GlassMenuFlyoutItemStyle"], Text = App.GetString("Menu_ApiStatusDiag"), Icon = new PathIcon { Data = App.CreateGeometry(IconData.ApiStatusDiag), Foreground = App.GetBrush("IconForegroundBrush") } };
                statusDiagMi.Click += (s2, e2) => ShowApiDiagConfirm(card);
                m.Items.Add(statusDiagMi);

                
                var relayProbeMi = new MenuFlyoutItem { Style = (Style)Application.Current.Resources["GlassMenuFlyoutItemStyle"], Text = App.GetString("Menu_RelayProbe"), Icon = new PathIcon { Data = App.CreateGeometry(IconData.RelayProbe), Foreground = App.GetBrush("IconForegroundBrush") } };
                relayProbeMi.Click += (s2, e2) => ShowRelayProbeConfirm(card);
                m.Items.Add(relayProbeMi);
            }
            m.Items.Add(moveSub);
            m.Items.Add(editMi);
            m.Items.Add(sep);
            m.Items.Add(delMi);
            if (e.TryGetPosition(card, out var p)) m.ShowAt(card, p);
        };

        AttachCardDrag(card);
        return card;
    }

        
        private void StartEntryMelt(Grid panel, bool expand, CancellationToken token) => Services.MeltAnim.Begin(panel, expand, 12.0);

    private void CopyToClipboardButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string text) { var dp = new DataPackage(); dp.SetText(text); try { Clipboard.SetContent(dp); } catch { } App.ShowToast(App.GetString("Common_Toast_Copied")); } // E1-26: clipboard can be locked by another process
    }

    private void OpenWebsite(string url)
    {
        if (string.IsNullOrWhiteSpace(url)) return;
        var target = url.Trim();
        if (!target.Contains("://")) target = "https://" + target; // no scheme -> default to https (browsers do not open a bare host)
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(target) { UseShellExecute = true }); }
        catch { } // E1-26: opening the default browser must never crash the app
    }

    private async void FlashTextBox(TextBox tb)
    {
        // N5M-01: per-box CTS (E5-24 pattern) - the shared CTS let a second flash cancel/truncate the first.
        if (_flashCtsMap.TryGetValue(tb, out var prev)) { prev.Cancel(); prev.Dispose(); }
        var cts = new System.Threading.CancellationTokenSource();
        _flashCtsMap[tb] = cts;

        var orig = _flashOriginalBgs.TryGetValue(tb, out var existing) ? existing : tb.Background;
        _flashOriginalBgs[tb] = orig;
        tb.Background = new SolidColorBrush(Color.FromArgb(0x33, 0xFF, 0x45, 0x45));
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

    private Border CreateUncategorizedCard()
    {
        var card = new Border
        {
            CornerRadius = new CornerRadius(12),
            Background = App.GetBrush("AppSurfaceOverlayBrush"),
            BorderBrush = new SolidColorBrush(App.GetBrush("AppBorderBrush").Color), // E1-09: per-card copy - never mutate the shared theme brush
            BorderThickness = new Thickness(1),
            Padding = new Thickness(16),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Top
        };

        var grid = new Grid();
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(36, GridUnitType.Pixel) });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        var titleGrid = new Grid { Height = 36 };
        var titleText = new TextBlock
        {
            Text = App.GetString("Memo_Group_Uncategorized"),
            FontSize = 14,
            FontWeight = Microsoft.UI.Text.FontWeights.Bold,
            Foreground = App.GetBrush("AppTextPrimaryBrush"),
            VerticalAlignment = VerticalAlignment.Center
        };
        titleGrid.Children.Add(titleText);
        Grid.SetRow(titleGrid, 0);

        var divider = new Border
        {
            Height = 1,
            Background = App.GetBrush("AppBorderBrush"),
            Margin = new Thickness(0, 12, 0, 0),
            Opacity = 0.3
        };
        Grid.SetRow(divider, 1);

        var contentPanel = new StackPanel
        {
            Margin = new Thickness(0, 12, 0, 0),
            Spacing = 8
        };
        Grid.SetRow(contentPanel, 2);

        grid.Children.Add(titleGrid);
        grid.Children.Add(divider);
        grid.Children.Add(contentPanel);
        card.Child = grid;

        return card;
    }

    private void SyncUncategorizedCard()
    {
        bool hasGroups = false;
        foreach (var child in GroupsContainer.Children)
        {
            if (child is Border b && _groupNameTexts.ContainsKey(b))
            {
                hasGroups = true;
                break;
            }
        }

        if (hasGroups && _standaloneEntries.Count > 0 && _uncategorizedCard == null)
        {

            _uncategorizedCard = CreateUncategorizedCard();
            GroupsContainer.Children.Add(_uncategorizedCard);
        }

        if (_uncategorizedCard != null)
        {
            int countInCard = 0;
            if (_uncategorizedCard.Child is Grid ug2 && ug2.Children.Count > 2 && ug2.Children[2] is Panel cp2)
                countInCard = cp2.Children.Count;

            if (!hasGroups || (_standaloneEntries.Count == 0 && countInCard == 0))
            {

                if (_uncategorizedCard.Child is Grid ug && ug.Children.Count > 2 && ug.Children[2] is StackPanel sp)
                {
                    var toRestore = new List<UIElement>();
                    foreach (var child in sp.Children) toRestore.Add(child);
                    sp.Children.Clear();
                    int idx = GroupsContainer.Children.IndexOf(_uncategorizedCard);
                    GroupsContainer.Children.Remove(_uncategorizedCard);
                    foreach (var entry in toRestore)
                    {
                        if (entry is Border eb)
                        {
                            eb.Margin = new Thickness(0);
                            _standaloneEntries.Add(eb);
                        }
                        GroupsContainer.Children.Insert(idx++, entry);
                    }
                }
                else
                {
                    GroupsContainer.Children.Remove(_uncategorizedCard);
                }
                _uncategorizedCard = null!;
            }
            else if (_standaloneEntries.Count > 0)
            {

                if (_uncategorizedCard.Child is Grid ug && ug.Children.Count > 2 && ug.Children[2] is StackPanel sp)
                {
                    
                    
                    int insertPos = (_pinnedEntryCard != null && sp.Children.Contains(_pinnedEntryCard))
                        ? sp.Children.IndexOf(_pinnedEntryCard) + 1 : 0;
                    foreach (var entry in _standaloneEntries)
                    {
                        GroupsContainer.Children.Remove(entry);
                        entry.Margin = new Thickness(0, 0, 0, 8);
                        sp.Children.Insert(insertPos++, entry);
                    }
                    _standaloneEntries.Clear();
                }
            }
        }
    }

    private string GetSelectedIconKey()
    {
        if (string.IsNullOrEmpty(_selectedIcon))
            return GetRandomIconKey(); 
        return IconData.GroupIconMap.TryGetValue(_selectedIcon, out var key) ? key : GetRandomIconKey();
    }

    private string GetRandomIconKey() => $"Group{_iconRandom.Next(1, IconData.GroupIconCount + 1):D2}";

    private void ConfirmButton_Click(object sender, RoutedEventArgs e)
    {
        if (_confirming) return; _confirming = true; // E4-17: double-click during the button animation created two identical groups
        if (string.IsNullOrWhiteSpace(GroupNameTextBox.Text)) { _confirming = false; FlashTextBox(GroupNameTextBox); return; }
        string gn = GroupNameTextBox.Text.Trim(); string ik = GetSelectedIconKey();
        var nc = CreateGroupCard(gn, ik);

        var newGroup = new MemoGroup { Name = gn, IconKey = ik, CreatedAt = DateTime.Now };
        App.Store?.Database.MemoGroups.Add(newGroup);
        _groupIds[nc] = newGroup.Id;
        _groupCreatedAt[nc] = newGroup.CreatedAt;

        var toMove = new List<Border>();
        foreach (var child in UngroupedEntriesPanel.Children)
        {
            if (child is CheckBox cb && cb.IsChecked == true && cb.Tag is Border entry)
            {
                toMove.Add(entry);
            }
        }
        // N5M-05: iterate newest-first list in REVERSE so each Insert(PinnedFirst...) lands the
        // oldest at the deepest slot - the group stack ends up in the same order as the source panel.
        for (int mi = toMove.Count - 1; mi >= 0; mi--)
        {
            var entry = toMove[mi];
            
            if (_uncategorizedCard?.Child is Grid ugc && ugc.Children.Count > 2 && ugc.Children[2] is StackPanel usp && usp.Children.Contains(entry))
                usp.Children.Remove(entry);
            else
                GroupsContainer.Children.Remove(entry);
            _standaloneEntries.Remove(entry);
            _entriesInGroup.Add(entry);

            if (_entryIds.TryGetValue(entry, out var mvId)) { var me = FindEntry(mvId); if (me != null) me.GroupId = newGroup.Id; }
            if (nc.Child is Grid g && g.Children.Count > 3 && g.Children[2] is StackPanel entryStack)
            {
                entryStack.Children.Insert(PinnedFirstInsertIndex(entryStack, entry), entry); // N4M-02: pin-first ordering, was Add
                if (g.Children[3] is Grid emptyHint)
                    emptyHint.Visibility = Visibility.Collapsed;
                UpdateGroupCount(nc);
            }
        }

        
        int insertIndex = (_pinnedGroupCard != null && GroupsContainer.Children.Contains(_pinnedGroupCard))
            ? GroupsContainer.Children.IndexOf(_pinnedGroupCard) + 1 : 0;
        if (_uncategorizedCard != null && GroupsContainer.Children.Contains(_uncategorizedCard))
        {
            int uncatIdx = GroupsContainer.Children.IndexOf(_uncategorizedCard);
            if (insertIndex > uncatIdx) insertIndex = uncatIdx;
        }
        GroupsContainer.Children.Insert(insertIndex, nc);
        App.PlayCardEntrance(nc);
        HintText.Visibility = Visibility.Collapsed;

        if (_pendingMoveEntry != null)
        {
            
            if (_uncategorizedCard?.Child is Grid ugc2 && ugc2.Children.Count > 2 && ugc2.Children[2] is StackPanel usp2 && usp2.Children.Contains(_pendingMoveEntry))
                usp2.Children.Remove(_pendingMoveEntry);
            else
                GroupsContainer.Children.Remove(_pendingMoveEntry); // E5-08: keep pin/star state when moving into a new group (E1-10 was only fixed for direct move-in/out - this branch still cleared them)
            _standaloneEntries.Remove(_pendingMoveEntry);
            _entriesInGroup.Add(_pendingMoveEntry);

            if (_entryIds.TryGetValue(_pendingMoveEntry, out var pmId)) { var me = FindEntry(pmId); if (me != null) me.GroupId = newGroup.Id; }
            if (nc.Child is Grid ng && ng.Children.Count > 3 && ng.Children[2] is StackPanel entryStack)
            {
                entryStack.Children.Insert(PinnedFirstInsertIndex(entryStack, _pendingMoveEntry), _pendingMoveEntry); // N4M-02: pin-first ordering, was Add
                if (ng.Children[3] is Grid emptyHint)
                    emptyHint.Visibility = Visibility.Collapsed;
                UpdateGroupCount(nc);
            }
            _pendingMoveEntry = null!;
        }

        SyncUncategorizedCard();
        PersistAll();
        App.ShowToast(App.GetString("Common_Toast_Created"));
        var sb = new Storyboard(); var sx = new DoubleAnimation { To = 0.95, Duration = TimeSpan.FromMilliseconds(100) }; Storyboard.SetTarget(sx, ConfirmButtonTransform); Storyboard.SetTargetProperty(sx, "ScaleX"); sb.Children.Add(sx);
        var sy = new DoubleAnimation { To = 0.95, Duration = TimeSpan.FromMilliseconds(100) }; Storyboard.SetTarget(sy, ConfirmButtonTransform); Storyboard.SetTargetProperty(sy, "ScaleY"); sb.Children.Add(sy);
        sb.Completed += (s, ev) => { var sb2 = new Storyboard(); var sx2 = new DoubleAnimation { To = 1.0, Duration = TimeSpan.FromMilliseconds(200) }; Storyboard.SetTarget(sx2, ConfirmButtonTransform); Storyboard.SetTargetProperty(sx2, "ScaleX"); sb2.Children.Add(sx2); var sy2 = new DoubleAnimation { To = 1.0, Duration = TimeSpan.FromMilliseconds(200) }; Storyboard.SetTarget(sy2, ConfirmButtonTransform); Storyboard.SetTargetProperty(sy2, "ScaleY"); sb2.Children.Add(sy2); sb2.Completed += (s2, e2) => CloseNewGroupDialog(); sb2.Begin(); };
        sb.Begin();
    }

    private readonly string[] EntryTypeLabels = { "邮箱", "账户", "API Key", "网站", "银行卡", "WiFi", "证件", "自定义" };
    
    private static readonly Dictionary<string, string[]> EntryTypeFieldLabels = new()
    {
        ["邮箱"] = new[] { "邮箱地址", "邮箱密码", "备注" },
        ["账户"] = new[] { "账号", "密码", "网址", "备注" },
        ["API Key"] = new[] { "API Key", "URL", "模型 ID", "备注" },
        ["网站"] = new[] { "网址", "账号", "密码", "备注" },
        ["银行卡"] = new[] { "卡号", "持卡人", "有效期", "CVV", "密码", "备注" },
        ["WiFi"] = new[] { "网络名", "密码", "备注" },
        ["证件"] = new[] { "证件号", "姓名", "签发机构", "有效期", "备注" },
    };
    private const int MaxCustomInfoCount = 5;
    private int _customInfoCount = 1;

    private void ApplyEntryFormLayout(string type)
    {
        FormField2Label.Text = ""; FormField3Label.Text = ""; FormField4Label.Text = ""; FormField5Label.Text = ""; FormField6Label.Text = ""; FormField7Label.Text = "";
        FormField2TextBox.PlaceholderText = ""; FormField3TextBox.PlaceholderText = ""; FormField4TextBox.PlaceholderText = ""; FormField5TextBox.PlaceholderText = ""; FormField6TextBox.PlaceholderText = ""; FormField7TextBox.PlaceholderText = "";
        FormField2TextBox.Text = ""; FormField3TextBox.Text = ""; FormField4TextBox.Text = ""; FormField5TextBox.Text = ""; FormField6TextBox.Text = ""; FormField7TextBox.Text = "";
        FormField2Section.Visibility = Visibility.Collapsed; FormField3Section.Visibility = Visibility.Collapsed;
        FormField4Section.Visibility = Visibility.Collapsed; FormField5Section.Visibility = Visibility.Collapsed;
        FormField6Section.Visibility = Visibility.Collapsed; FormField7Section.Visibility = Visibility.Collapsed;
        EntryNameTextBox.Text = "";
        switch (type)
        {
            case "邮箱":
                FormField2Label.Text = App.GetString("Memo_Field_EmailAddr"); FormField2TextBox.PlaceholderText = App.GetString("Memo_Field_EmailAddrPh"); FormField2Section.Visibility = Visibility.Visible;
                FormField3Label.Text = App.GetString("Memo_Field_EmailPwd"); FormField3TextBox.PlaceholderText = App.GetString("Memo_Field_EmailPwdPh"); FormField3Section.Visibility = Visibility.Visible;
                FormField4Label.Text = App.GetString("Memo_Field_Note"); FormField4TextBox.PlaceholderText = App.GetString("Memo_Field_NotePh"); FormField4Section.Visibility = Visibility.Visible;
                break;
            case "账户":
                FormField2Label.Text = App.GetString("Memo_Field_Account"); FormField2TextBox.PlaceholderText = App.GetString("Memo_Field_AccountPh"); FormField2Section.Visibility = Visibility.Visible;
                FormField3Label.Text = App.GetString("Memo_Field_Password"); FormField3TextBox.PlaceholderText = App.GetString("Memo_Field_PasswordPh"); FormField3Section.Visibility = Visibility.Visible;
                FormField4Label.Text = App.GetString("Memo_Field_Website"); FormField4TextBox.PlaceholderText = App.GetString("Memo_Field_WebsitePh"); FormField4Section.Visibility = Visibility.Visible;
                FormField5Label.Text = App.GetString("Memo_Field_Note"); FormField5TextBox.PlaceholderText = App.GetString("Memo_Field_NotePh"); FormField5Section.Visibility = Visibility.Visible;
                break;
            case "API Key":
                FormField2Label.Text = App.GetString("Memo_Type_ApiKey"); FormField2TextBox.PlaceholderText = App.GetString("Memo_Field_ApiKeyPh"); FormField2Section.Visibility = Visibility.Visible;
                FormField3Label.Text = App.GetString("Memo_Field_Url"); FormField3TextBox.PlaceholderText = App.GetString("Memo_Field_UrlPh"); FormField3Section.Visibility = Visibility.Visible;
                FormField4Label.Text = App.GetString("Memo_Field_ModelId"); FormField4TextBox.PlaceholderText = App.GetString("Memo_Field_ModelIdPh"); FormField4Section.Visibility = Visibility.Visible;
                FormField5Label.Text = App.GetString("Memo_Field_Note"); FormField5TextBox.PlaceholderText = App.GetString("Memo_Field_NotePh"); FormField5Section.Visibility = Visibility.Visible;
                break;
            case "网站":
                FormField2Label.Text = App.GetString("Memo_Field_Website"); FormField2TextBox.PlaceholderText = App.GetString("Memo_Field_WebsitePh"); FormField2Section.Visibility = Visibility.Visible;
                FormField3Label.Text = App.GetString("Memo_Field_Account"); FormField3TextBox.PlaceholderText = App.GetString("Memo_Field_AccountPh"); FormField3Section.Visibility = Visibility.Visible;
                FormField4Label.Text = App.GetString("Memo_Field_Password"); FormField4TextBox.PlaceholderText = App.GetString("Memo_Field_PasswordPh"); FormField4Section.Visibility = Visibility.Visible;
                FormField5Label.Text = App.GetString("Memo_Field_Note"); FormField5TextBox.PlaceholderText = App.GetString("Memo_Field_NotePh"); FormField5Section.Visibility = Visibility.Visible;
                break;
            case "银行卡":
                FormField2Label.Text = App.GetString("Memo_Field_CardNumber"); FormField2TextBox.PlaceholderText = App.GetString("Memo_Field_CardNumberPh"); FormField2Section.Visibility = Visibility.Visible;
                FormField3Label.Text = App.GetString("Memo_Field_CardHolder"); FormField3TextBox.PlaceholderText = App.GetString("Memo_Field_CardHolderPh"); FormField3Section.Visibility = Visibility.Visible;
                FormField4Label.Text = App.GetString("Memo_Field_Expiry"); FormField4TextBox.PlaceholderText = App.GetString("Memo_Field_ExpiryPh"); FormField4Section.Visibility = Visibility.Visible;
                FormField5Label.Text = App.GetString("Memo_Field_Cvv"); FormField5TextBox.PlaceholderText = App.GetString("Memo_Field_CvvPh"); FormField5Section.Visibility = Visibility.Visible;
                FormField6Label.Text = App.GetString("Memo_Field_Password"); FormField6TextBox.PlaceholderText = App.GetString("Memo_Field_PasswordPh"); FormField6Section.Visibility = Visibility.Visible;
                FormField7Label.Text = App.GetString("Memo_Field_Note"); FormField7TextBox.PlaceholderText = App.GetString("Memo_Field_NotePh"); FormField7Section.Visibility = Visibility.Visible;
                break;
            case "WiFi":
                FormField2Label.Text = App.GetString("Memo_Field_Ssid"); FormField2TextBox.PlaceholderText = App.GetString("Memo_Field_SsidPh"); FormField2Section.Visibility = Visibility.Visible;
                FormField3Label.Text = App.GetString("Memo_Field_Password"); FormField3TextBox.PlaceholderText = App.GetString("Memo_Field_PasswordPh"); FormField3Section.Visibility = Visibility.Visible;
                FormField4Label.Text = App.GetString("Memo_Field_Note"); FormField4TextBox.PlaceholderText = App.GetString("Memo_Field_NotePh"); FormField4Section.Visibility = Visibility.Visible;
                break;
            case "证件":
                FormField2Label.Text = App.GetString("Memo_Field_IdNumber"); FormField2TextBox.PlaceholderText = App.GetString("Memo_Field_IdNumberPh"); FormField2Section.Visibility = Visibility.Visible;
                FormField3Label.Text = App.GetString("Memo_Field_FullName"); FormField3TextBox.PlaceholderText = App.GetString("Memo_Field_FullNamePh"); FormField3Section.Visibility = Visibility.Visible;
                FormField4Label.Text = App.GetString("Memo_Field_Issuer"); FormField4TextBox.PlaceholderText = App.GetString("Memo_Field_IssuerPh"); FormField4Section.Visibility = Visibility.Visible;
                FormField5Label.Text = App.GetString("Memo_Field_Expiry"); FormField5TextBox.PlaceholderText = App.GetString("Memo_Field_ExpiryPh"); FormField5Section.Visibility = Visibility.Visible;
                FormField6Label.Text = App.GetString("Memo_Field_Note"); FormField6TextBox.PlaceholderText = App.GetString("Memo_Field_NotePh"); FormField6Section.Visibility = Visibility.Visible;
                break;
        }
        if (type == "自定义") { EntryTypeFormsContainer.Visibility = Visibility.Collapsed; CustomFormContainer.Visibility = Visibility.Visible; LoadEntryIconSelector(); }
        else { EntryTypeFormsContainer.Visibility = Visibility.Visible; CustomFormContainer.Visibility = Visibility.Collapsed; }
        UpdateNewEntryConfirmState(); // UI-2: fields were just cleared (or re-filled) - recompute the confirm state
    }

    private TextBox CreateCustomInfoTextBox(int index)
    {
        string ph = index == 0 ? App.GetString("Memo_Entry_InfoLabel") : string.Format(App.GetString("Memo_Entry_InfoN"), index);
        var tb = new TextBox { Style = (Style)Application.Current.Resources["NovaraTextBoxStyle"], MaxLength = 500, PlaceholderText = ph, VerticalAlignment = VerticalAlignment.Center };
        tb.TextChanged += EntryField_TextChanged; 
        return tb;
    }

    private void UpdateCustomInfoButtonVisibility() { CustomInfoAddButton.Visibility = (_customInfoCount >= MaxCustomInfoCount) ? Visibility.Collapsed : Visibility.Visible; }

    private void CustomInfoAddButton_Click(object sender, RoutedEventArgs e)
    {
        if (_customInfoCount >= MaxCustomInfoCount) return;
        var tb = CreateCustomInfoTextBox(_customInfoCount); CustomInfoDynamicPanel.Children.Add(tb); _customInfoCount++; UpdateCustomInfoButtonVisibility();
        var ct = new CompositeTransform(); tb.RenderTransform = ct; var sb = new Storyboard(); ct.ScaleX = 0.96; ct.ScaleY = 0.96; tb.Opacity = 0;
        var sx = new DoubleAnimation { To = 1.0, Duration = TimeSpan.FromMilliseconds(250), EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut } }; Storyboard.SetTarget(sx, ct); Storyboard.SetTargetProperty(sx, "ScaleX"); sb.Children.Add(sx);
        var sy = new DoubleAnimation { To = 1.0, Duration = TimeSpan.FromMilliseconds(250), EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut } }; Storyboard.SetTarget(sy, ct); Storyboard.SetTargetProperty(sy, "ScaleY"); sb.Children.Add(sy);
        var oa = new DoubleAnimation { To = 1.0, Duration = TimeSpan.FromMilliseconds(200) }; Storyboard.SetTarget(oa, tb); Storyboard.SetTargetProperty(oa, "Opacity"); sb.Children.Add(oa);
        sb.Begin(); tb.Focus(FocusState.Programmatic);
    }

    private void NewEntryConfirmButton_Click(object sender, RoutedEventArgs e)
    {
        if (_confirming) return; _confirming = true; // E3-10
        bool isEdit = _editingEntryCard != null;
        string type = _selectedEntryType;
        if (string.IsNullOrWhiteSpace(type)) { _confirming = false; return; }

        string name; string keyInfo = ""; var fields = new List<(string, string, bool)>();

        if (type == "自定义")
        {
            name = CustomNameTextBox.Text.Trim();
            if (string.IsNullOrWhiteSpace(name)) { _confirming = false; FlashTextBox(CustomNameTextBox); return; }
            keyInfo = CustomInfoTextBox_0.Text.Trim();
            if (string.IsNullOrWhiteSpace(keyInfo)) { _confirming = false; FlashTextBox(CustomInfoTextBox_0); return; } 
            fields.Add(("信息", keyInfo, true));
            foreach (var child in CustomInfoDynamicPanel.Children)
                if (child is TextBox tb && !string.IsNullOrWhiteSpace(tb.Text))
                    fields.Add(("信息", tb.Text.Trim(), true));
        }
        else
        {
            name = EntryNameTextBox.Text.Trim();
            if (string.IsNullOrWhiteSpace(name)) { _confirming = false; FlashTextBox(EntryNameTextBox); return; }
            switch (type)
            {
                case "邮箱":
                    keyInfo = FormField2TextBox.Text.Trim();
                    if (string.IsNullOrWhiteSpace(keyInfo)) { _confirming = false; FlashTextBox(FormField2TextBox); return; } // UI-3
                    fields.Add(("邮箱地址", keyInfo, true));
                    if (!string.IsNullOrWhiteSpace(FormField3TextBox.Text)) fields.Add(("邮箱密码", FormField3TextBox.Text.Trim(), true)); 
                    if (!string.IsNullOrWhiteSpace(FormField4TextBox.Text)) fields.Add(("备注", FormField4TextBox.Text.Trim(), false));
                    break;
                case "账户":
                    keyInfo = FormField2TextBox.Text.Trim();
                    if (string.IsNullOrWhiteSpace(keyInfo)) { _confirming = false; FlashTextBox(FormField2TextBox); return; } // UI-3
                    fields.Add(("账号", keyInfo, true));
                    if (!string.IsNullOrWhiteSpace(FormField3TextBox.Text)) fields.Add(("密码", FormField3TextBox.Text.Trim(), true)); 
                    if (!string.IsNullOrWhiteSpace(FormField4TextBox.Text)) fields.Add(("网址", FormField4TextBox.Text.Trim(), false));
                    if (!string.IsNullOrWhiteSpace(FormField5TextBox.Text)) fields.Add(("备注", FormField5TextBox.Text.Trim(), false));
                    break;
                case "网站":
                    keyInfo = FormField2TextBox.Text.Trim();
                    if (string.IsNullOrWhiteSpace(keyInfo)) { _confirming = false; FlashTextBox(FormField2TextBox); return; }
                    fields.Add(("网址", keyInfo, true));
                    if (!string.IsNullOrWhiteSpace(FormField3TextBox.Text)) fields.Add(("账号", FormField3TextBox.Text.Trim(), true));
                    if (!string.IsNullOrWhiteSpace(FormField4TextBox.Text)) fields.Add(("密码", FormField4TextBox.Text.Trim(), true));
                    if (!string.IsNullOrWhiteSpace(FormField5TextBox.Text)) fields.Add(("备注", FormField5TextBox.Text.Trim(), false));
                    break;
                case "银行卡":
                    keyInfo = FormField2TextBox.Text.Trim();
                    if (string.IsNullOrWhiteSpace(keyInfo)) { _confirming = false; FlashTextBox(FormField2TextBox); return; } 
                    fields.Add(("卡号", keyInfo, true));
                    if (!string.IsNullOrWhiteSpace(FormField3TextBox.Text)) fields.Add(("持卡人", FormField3TextBox.Text.Trim(), false));
                    if (!string.IsNullOrWhiteSpace(FormField4TextBox.Text)) fields.Add(("有效期", FormField4TextBox.Text.Trim(), false));
                    if (!string.IsNullOrWhiteSpace(FormField5TextBox.Text)) fields.Add(("CVV", FormField5TextBox.Text.Trim(), true));
                    if (!string.IsNullOrWhiteSpace(FormField6TextBox.Text)) fields.Add(("密码", FormField6TextBox.Text.Trim(), true));
                    if (!string.IsNullOrWhiteSpace(FormField7TextBox.Text)) fields.Add(("备注", FormField7TextBox.Text.Trim(), false));
                    break;
                case "WiFi":
                    keyInfo = FormField2TextBox.Text.Trim();
                    if (string.IsNullOrWhiteSpace(keyInfo)) { _confirming = false; FlashTextBox(FormField2TextBox); return; } 
                    fields.Add(("网络名", keyInfo, true));
                    if (!string.IsNullOrWhiteSpace(FormField3TextBox.Text)) fields.Add(("密码", FormField3TextBox.Text.Trim(), true));
                    if (!string.IsNullOrWhiteSpace(FormField4TextBox.Text)) fields.Add(("备注", FormField4TextBox.Text.Trim(), false));
                    break;
                case "证件":
                    keyInfo = FormField2TextBox.Text.Trim();
                    if (string.IsNullOrWhiteSpace(keyInfo)) { _confirming = false; FlashTextBox(FormField2TextBox); return; } 
                    fields.Add(("证件号", keyInfo, true));
                    if (!string.IsNullOrWhiteSpace(FormField3TextBox.Text)) fields.Add(("姓名", FormField3TextBox.Text.Trim(), false));
                    if (!string.IsNullOrWhiteSpace(FormField4TextBox.Text)) fields.Add(("签发机构", FormField4TextBox.Text.Trim(), false));
                    if (!string.IsNullOrWhiteSpace(FormField5TextBox.Text)) fields.Add(("有效期", FormField5TextBox.Text.Trim(), false));
                    if (!string.IsNullOrWhiteSpace(FormField6TextBox.Text)) fields.Add(("备注", FormField6TextBox.Text.Trim(), false));
                    break;
                case "API Key":
                    var apiKeyVal = FormField2TextBox.Text.Trim();
                    if (string.IsNullOrWhiteSpace(apiKeyVal)) { _confirming = false; FlashTextBox(FormField2TextBox); return; }
                    var apiUrlVal = FormField3TextBox.Text.Trim();
                    if (string.IsNullOrWhiteSpace(apiUrlVal)) { _confirming = false; FlashTextBox(FormField3TextBox); return; }
                    keyInfo = apiUrlVal;
                    fields.Add(("API Key", apiKeyVal, true));
                    fields.Add(("URL", apiUrlVal, true));
                    if (!string.IsNullOrWhiteSpace(FormField4TextBox.Text)) fields.Add(("模型 ID", FormField4TextBox.Text.Trim(), true));
                    if (!string.IsNullOrWhiteSpace(FormField5TextBox.Text)) fields.Add(("备注", FormField5TextBox.Text.Trim(), false));
                    break;
            }
        }

        
        var entryIconKey = type == "自定义" && !string.IsNullOrEmpty(_entrySelectedIcon) ? _entrySelectedIcon
                         : type == "自定义" ? GetRandomIconKey() : "";
        var card = CreateEntryCard(type, name, keyInfo, fields, entryIconKey);
        if (type == "自定义") _entryIconKeys[card] = entryIconKey;

        var entryEntity = new MemoEntry
        {
            Type = type,
            Name = name,
            KeyInfo = keyInfo,
            Fields = fields.Select(f => new EntryField { Label = f.Item1, Value = f.Item2, CanCopy = f.Item3 }).ToList(),
            CreatedAt = DateTime.Now,
            IconKey = entryIconKey
        };

        if (_editingEntryCard != null)
        {

            var old = _editingEntryCard;
            var parent = old.Parent as Panel;
            int idx = parent != null ? parent.Children.IndexOf(old) : -1;
            if (idx < 0)
            {

                for (int i = 0; i < GroupsContainer.Children.Count; i++)
                {
                    if (GroupsContainer.Children[i] == old) { idx = i; parent = GroupsContainer; break; }
                    if (GroupsContainer.Children[i] is Border g && g.Child is Grid gg && gg.Children.Count > 2 && gg.Children[2] is Panel cp)
                    {
                        int j = cp.Children.IndexOf(old);
                        if (j >= 0) { idx = j; parent = cp; break; }
                    }
                }
            }
            if (parent != null && idx >= 0)
            {
                parent.Children[idx] = card;
            }
            bool wasStarred = _starredEntries.Contains(old);
            bool wasInGroup = _entriesInGroup.Contains(old);
        
        
        
        bool wasInUncategorized = _uncategorizedCard != null
            && parent is StackPanel usp && usp.Parent is Grid ug && ug.Parent is Border ub && ReferenceEquals(ub, _uncategorizedCard);
            _entriesInGroup.Remove(old);
            int standaloneIdx = _standaloneEntries.IndexOf(old); // N4M-03: preserve the replaced card's list rank for later folds
            _standaloneEntries.Remove(old);
            _entryPinIcons.Remove(old);
            _entryStarIcons.Remove(old);
            _entryData.Remove(old);
            if (_entryExpand.TryGetValue(old, out var eo)) { eo.Cts?.Cancel(); _entryExpand.Remove(old); } // N6-10
            _entryIconKeys.Remove(old);
            _starredEntries.Remove(old);
            _apiProtocols.Remove(old);

            if (_entryIds.TryGetValue(old, out var editId))
            {
                _entryIds[card] = editId;
                _entryCreatedAt[card] = _entryCreatedAt.TryGetValue(old, out var ec) ? ec : DateTime.Now;
                var me = FindEntry(editId);
                if (me != null)
                {
                    me.Type = type; me.Name = name; me.KeyInfo = keyInfo;
                    me.Fields = fields.Select(f => new EntryField { Label = f.Item1, Value = f.Item2, CanCopy = f.Item3 }).ToList();
                    if (type == "自定义") me.IconKey = entryIconKey; 
                    if (!string.IsNullOrEmpty(me.Protocol)) _apiProtocols[card] = me.Protocol;
                }
            }
            _entryIds.Remove(old);
            _entryCreatedAt.Remove(old);
            if (_pinnedEntryCard == old) { _pinnedEntryCard = card; if (_entryPinIcons.TryGetValue(card, out var ep)) ep.Visibility = Visibility.Visible; }
            if (wasStarred) { _starredEntries.Add(card); if (_entryStarIcons.TryGetValue(card, out var es)) es.Visibility = Visibility.Visible; }
            if (wasInGroup) _entriesInGroup.Add(card);
            else if (!wasInUncategorized) _standaloneEntries.Insert(standaloneIdx < 0 ? 0 : System.Math.Min(standaloneIdx, _standaloneEntries.Count), card); 

        }
        else if (_targetGroupCard != null)
        {

            _entriesInGroup.Add(card);

            App.Store?.Database.MemoEntries.Add(entryEntity);
            _entryIds[card] = entryEntity.Id;
            _entryCreatedAt[card] = entryEntity.CreatedAt;
            if (_groupIds.TryGetValue(_targetGroupCard, out var tGid)) entryEntity.GroupId = tGid;
            if (_targetGroupCard.Child is Grid tg && tg.Children.Count > 3 && tg.Children[2] is StackPanel entryStack)
            {
                
                int insIdx = (_pinnedEntryCard != null && entryStack.Children.Contains(_pinnedEntryCard))
                    ? entryStack.Children.IndexOf(_pinnedEntryCard) + 1 : 0;
                entryStack.Children.Insert(insIdx, card);
                if (tg.Children[3] is Grid emptyHint)
                    emptyHint.Visibility = Visibility.Collapsed;
                UpdateGroupCount(_targetGroupCard);
            }
            _targetGroupCard = null!;
        }
        else
        {
            
            int insIdx = 0;
            if (_pinnedGroupCard != null && GroupsContainer.Children.Contains(_pinnedGroupCard))
                insIdx = GroupsContainer.Children.IndexOf(_pinnedGroupCard) + 1;
            if (_pinnedEntryCard != null && GroupsContainer.Children.Contains(_pinnedEntryCard))
            {
                int pe = GroupsContainer.Children.IndexOf(_pinnedEntryCard) + 1;
                if (pe > insIdx) insIdx = pe;
            }
            if (_uncategorizedCard != null && GroupsContainer.Children.Contains(_uncategorizedCard))
            {
                int uc = GroupsContainer.Children.IndexOf(_uncategorizedCard);
                if (insIdx > uc) insIdx = uc;
            }
            GroupsContainer.Children.Insert(insIdx, card);
            _standaloneEntries.Insert(0, card); // N4M-03: list must mirror the UI's newest-first order - Add() made the first mass fold reverse the visual order

            App.Store?.Database.MemoEntries.Add(entryEntity);
            _entryIds[card] = entryEntity.Id;
            _entryCreatedAt[card] = entryEntity.CreatedAt;
        }
        HintText.Visibility = Visibility.Collapsed;
        App.PlayCardEntrance(card);
        if (_editingEntryCard == null) SyncUncategorizedCard();
        PersistAll();
        App.ShowToast(App.GetString(isEdit ? "Common_Toast_Modified" : "Common_Toast_Created"));

        var sb = new Storyboard(); var sx = new DoubleAnimation { To = 0.95, Duration = TimeSpan.FromMilliseconds(100) }; Storyboard.SetTarget(sx, NewEntryConfirmButtonTransform); Storyboard.SetTargetProperty(sx, "ScaleX"); sb.Children.Add(sx);
        var sy = new DoubleAnimation { To = 0.95, Duration = TimeSpan.FromMilliseconds(100) }; Storyboard.SetTarget(sy, NewEntryConfirmButtonTransform); Storyboard.SetTargetProperty(sy, "ScaleY"); sb.Children.Add(sy);
        sb.Begin();
        sb.Completed += (s, ev) => { var sb2 = new Storyboard(); var sx2 = new DoubleAnimation { To = 1.0, Duration = TimeSpan.FromMilliseconds(200) }; Storyboard.SetTarget(sx2, NewEntryConfirmButtonTransform); Storyboard.SetTargetProperty(sx2, "ScaleX"); sb2.Children.Add(sx2); var sy2 = new DoubleAnimation { To = 1.0, Duration = TimeSpan.FromMilliseconds(200) }; Storyboard.SetTarget(sy2, NewEntryConfirmButtonTransform); Storyboard.SetTargetProperty(sy2, "ScaleY"); sb2.Children.Add(sy2); sb2.Completed += (s2, e2) => CloseNewEntryDialog(); sb2.Begin(); };
    }

    /// <summary>Open the entry-edit dialog for a database memo entry id (global search jump).</summary>
    public bool FlashEntryById(Guid id)
    {
        foreach (var (card, cid) in _entryIds)
        {
            if (cid != id) continue;
            try
            {
                var t = card.TransformToVisual(MemoScrollViewer);
                var pt = t.TransformPoint(new Windows.Foundation.Point(0, 0));
                MemoScrollViewer.ChangeView(null, System.Math.Max(0, MemoScrollViewer.VerticalOffset + pt.Y - 20), null, false);
            }
            catch { }
            App.FlashCard(card);
            return true;
        }
        return false;
    }


    private void OpenNewEntryDialog()
    {
        _confirming = false; // E3-10: re-arm the confirm guard when the dialog re-opens
        NewEntryDialogHideAnimation.Stop();
        NewEntryDialogShowAnimation.Stop();
        NewEntryDialogTransform.ScaleX = 0.92; NewEntryDialogTransform.ScaleY = 0.92; NewEntryDialogTransform.TranslateY = 20;
        NewEntryDialog.Opacity = 0; NewEntryDialogScrim.Opacity = 0;
        NewEntryDialogTitle.Text = _editingEntryCard != null ? App.GetString("Memo_Entry_EditTitle") : App.GetString("Memo_Entry_NewTitle");
        EntryTypeSection.Visibility = _editingEntryCard != null ? Visibility.Collapsed : Visibility.Visible;
        ResetEntryTypeForms();
        NewEntryDialogOverlay.Visibility = Visibility.Visible;
        DialogDepth.VeilShow(); 
        NewEntryDialogShowAnimation.Begin();
    }

    private void CloseNewEntryDialog()
    {
        NewEntryDialogHideAnimation.Completed -= OnNewEntryDialogHideCompleted;
        NewEntryDialogHideAnimation.Completed += OnNewEntryDialogHideCompleted;
        DialogDepth.VeilHide(); 
        NewEntryDialogHideAnimation.Begin();
    }

    private void OnNewEntryDialogHideCompleted(object? sender, object e)
    {
        NewEntryDialogHideAnimation.Completed -= OnNewEntryDialogHideCompleted;
        NewEntryDialogOverlay.Visibility = Visibility.Collapsed;
        _editingEntryCard = null!;
        _targetGroupCard = null!;
    }

    private void CloseNewEntryDialogButton_Click(object sender, RoutedEventArgs e) => CloseNewEntryDialog();
    private void NewEntryDialogScrim_Tapped(object sender, TappedRoutedEventArgs e) { if (ReferenceEquals(e.OriginalSource, NewEntryDialogScrim)) CloseNewEntryDialog(); }

    private void PinGroupCard(Border card)
    {
        if (_pinnedGroupCard != null && _pinnedGroupCard != card && _pinIcons.ContainsKey(_pinnedGroupCard)) _pinIcons[_pinnedGroupCard].Visibility = Visibility.Collapsed;
        if (_pinIcons.ContainsKey(card)) _pinIcons[card].Visibility = Visibility.Visible;
        GroupsContainer.Children.Remove(card); GroupsContainer.Children.Insert(0, card);
        _pinnedGroupCard = card;
        SyncGroupPins();
        PersistAll();
    }

    private void SyncGroupPins()
    {
        var db = App.Store?.Database;
        if (db == null) return;
        Guid? pinnedId = _pinnedGroupCard != null && _groupIds.TryGetValue(_pinnedGroupCard, out var pid) ? pid : null;
        foreach (var g in db.MemoGroups) g.IsPinned = g.Id == pinnedId;
    }

    private void UnpinGroupCard(Border card)
    {
        if (_pinIcons.ContainsKey(card)) _pinIcons[card].Visibility = Visibility.Collapsed;
        if (_pinnedGroupCard == card) _pinnedGroupCard = null!;
        SyncGroupPins();
        PersistAll();
    }

    private void PinEntryCard(Border card)
    {
        if (_pinnedEntryCard != null && _pinnedEntryCard != card && _entryPinIcons.ContainsKey(_pinnedEntryCard))
            _entryPinIcons[_pinnedEntryCard].Visibility = Visibility.Collapsed;
        if (_entryPinIcons.ContainsKey(card)) _entryPinIcons[card].Visibility = Visibility.Visible;

        var parent = card.Parent as Panel;
        if (parent != null)
        {
            parent.Children.Remove(card);
            parent.Children.Insert(0, card);
        }

        _pinnedEntryCard = card;
        SyncEntryPins();
        PersistAll();
    }

    private void SyncEntryPins()
    {
        var db = App.Store?.Database;
        if (db == null) return;
        Guid? pinnedId = _pinnedEntryCard != null && _entryIds.TryGetValue(_pinnedEntryCard, out var pid) ? pid : null;
        foreach (var en in db.MemoEntries) en.IsPinned = en.Id == pinnedId;
    }

    private void UnpinEntryCard(Border card)
    {
        if (_entryPinIcons.ContainsKey(card)) _entryPinIcons[card].Visibility = Visibility.Collapsed;
        if (_pinnedEntryCard == card) _pinnedEntryCard = null!;
        SyncEntryPins();
        PersistAll();
    }

    private void StarEntryCard(Border card)
    {
        _starredEntries.Add(card);
        if (_entryStarIcons.ContainsKey(card)) _entryStarIcons[card].Visibility = Visibility.Visible;
        SyncEntryStar(card, true);
    }

    private void UnstarEntryCard(Border card)
    {
        _starredEntries.Remove(card);
        if (_entryStarIcons.ContainsKey(card)) _entryStarIcons[card].Visibility = Visibility.Collapsed;
        SyncEntryStar(card, false);
    }

    private void SyncEntryStar(Border card, bool starred)
    {
        if (_entryIds.TryGetValue(card, out var id)) { var me = FindEntry(id); if (me != null) me.IsStarred = starred; }
        App.Store?.SaveAsync();
    }

    private void StarGroupCard(Border card)
    {
        _starredCards.Add(card);
        if (_starIcons.ContainsKey(card)) _starIcons[card].Visibility = Visibility.Visible;
        SyncGroupStar(card, true);
    }

    private void UnstarGroupCard(Border card)
    {
        _starredCards.Remove(card);
        if (_starIcons.ContainsKey(card)) _starIcons[card].Visibility = Visibility.Collapsed;
        SyncGroupStar(card, false);
    }

    private void SyncGroupStar(Border card, bool starred)
    {
        if (_groupIds.TryGetValue(card, out var id)) { var g = FindGroup(id); if (g != null) g.IsStarred = starred; }
        App.Store?.SaveAsync();
    }

    private void OpenEditGroupDialog()
    {
        if (_currentGroupCard == null || !_groupNameTexts.ContainsKey(_currentGroupCard)) return;
        EditGroupNameTextBox.Text = _groupNameTexts[_currentGroupCard].Text;
        _editGroupOrigName = EditGroupNameTextBox.Text.Trim(); 
        EditGroupDialogTransform.ScaleX = 0.92; EditGroupDialogTransform.ScaleY = 0.92; EditGroupDialogTransform.TranslateY = 20;
        EditGroupDialog.Opacity = 0; EditGroupScrim.Opacity = 0;
        EditGroupOverlay.Visibility = Visibility.Visible;
        DialogDepth.VeilShow(); 
        EditGroupShowAnimation.Begin();
        UpdateEditGroupConfirmState(); 
    }

    
    private void UpdateEditGroupConfirmState()
    {
        string name = EditGroupNameTextBox.Text.Trim();
        bool valid = !string.IsNullOrWhiteSpace(name) && name != _editGroupOrigName;
        EditGroupConfirmButton.IsEnabled = valid;
    }

    private void EditGroupNameTextBox_TextChanged(object sender, TextChangedEventArgs e) => UpdateEditGroupConfirmState();

    private void CloseEditGroupDialog() { EditGroupHideAnimation.Completed -= OnEditGroupHideCompleted; EditGroupHideAnimation.Completed += OnEditGroupHideCompleted; DialogDepth.VeilHide(); EditGroupHideAnimation.Begin(); }
    private void OnEditGroupHideCompleted(object? sender, object e) { EditGroupHideAnimation.Completed -= OnEditGroupHideCompleted; EditGroupOverlay.Visibility = Visibility.Collapsed; }
    private void EditGroupCloseButton_Click(object sender, RoutedEventArgs e) => CloseEditGroupDialog();
    private void EditGroupScrim_Tapped(object sender, TappedRoutedEventArgs e) { if (ReferenceEquals(e.OriginalSource, EditGroupScrim)) CloseEditGroupDialog(); }

    private void EditGroupConfirmButton_Click(object sender, RoutedEventArgs e)
    {
        string nn = EditGroupNameTextBox.Text.Trim(); if (string.IsNullOrWhiteSpace(nn)) return;
        if (_currentGroupCard != null && _groupNameTexts.ContainsKey(_currentGroupCard))
        {
            _groupNameTexts[_currentGroupCard].Text = nn;

            if (_groupData.TryGetValue(_currentGroupCard, out var gd))
                _groupData[_currentGroupCard] = (nn, gd.iconKey);

            if (_groupIds.TryGetValue(_currentGroupCard, out var egid)) { var eg = FindGroup(egid); if (eg != null) eg.Name = nn; }
        }
        PersistAll();
        App.ShowToast(App.GetString("Common_Toast_Modified"));
        CloseEditGroupDialog();
    }

    private void OpenDeleteConfirmDialog(bool isGroup)
    {
        _deleteConfirming = false; // N5M-02: re-arm on every fresh open
        // Group deletion is permanent (groups never enter the trash): separate copy + red button.
        DeleteConfirmMessageText.Text = isGroup
            ? App.GetString("Memo_Group_Delete_ConfirmTip")
            : App.GetString("Memo_Delete_ConfirmTip");
        
        DeleteConfirmTitleText.Foreground = isGroup
            ? new SolidColorBrush(Color.FromArgb(0xFF, 0xFF, 0x45, 0x45))
            : App.GetBrush("AppPrimaryButtonBrush");
        
        DeleteConfirmMessageText.Foreground = isGroup
            ? new SolidColorBrush(Color.FromArgb(0xFF, 0xFF, 0x45, 0x45))
            : App.GetBrush("AppTextPrimaryBrush");
        DeleteConfirmButton.Visibility = isGroup ? Visibility.Collapsed : Visibility.Visible;
        DeleteConfirmRedButton.Visibility = isGroup ? Visibility.Visible : Visibility.Collapsed;
        DeleteConfirmDialogTransform.ScaleX = 0.92; DeleteConfirmDialogTransform.ScaleY = 0.92; DeleteConfirmDialogTransform.TranslateY = 20;
        DeleteConfirmDialog.Opacity = 0; DeleteConfirmScrim.Opacity = 0;
        DeleteConfirmOverlay.Visibility = Visibility.Visible;
        DialogDepth.VeilShow(); 
        DeleteConfirmShowAnimation.Begin();
    }

    private void CloseDeleteConfirmDialog() { DeleteConfirmHideAnimation.Completed -= OnDeleteConfirmHideCompleted; DeleteConfirmHideAnimation.Completed += OnDeleteConfirmHideCompleted; DialogDepth.VeilHide(); DeleteConfirmHideAnimation.Begin(); }
    private void OnDeleteConfirmHideCompleted(object? sender, object e) { DeleteConfirmHideAnimation.Completed -= OnDeleteConfirmHideCompleted; DeleteConfirmOverlay.Visibility = Visibility.Collapsed; _currentEntryCard = null!; _currentGroupCard = null!; _deleteConfirming = false; } // N5M-02: re-arm
    private void DeleteConfirmCloseButton_Click(object sender, RoutedEventArgs e) => CloseDeleteConfirmDialog();
    private void DeleteCancelButton_Click(object sender, RoutedEventArgs e) => CloseDeleteConfirmDialog();
    private void DeleteConfirmScrim_Tapped(object sender, TappedRoutedEventArgs e) { if (ReferenceEquals(e.OriginalSource, DeleteConfirmScrim)) CloseDeleteConfirmDialog(); }

    private bool _deleteConfirming; // N5M-02: one-shot guard - the confirm runs inside a Storyboard.Completed, so a double-click executed the removal twice

    private void DeleteConfirmButton_Click(object sender, RoutedEventArgs e)
    {
        if (_deleteConfirming) return;
        _deleteConfirming = true;
        var sb = new Storyboard(); var sx = new DoubleAnimation { To = 0.95, Duration = TimeSpan.FromMilliseconds(100) }; Storyboard.SetTarget(sx, DeleteConfirmButtonTransform); Storyboard.SetTargetProperty(sx, "ScaleX"); sb.Children.Add(sx);
        var sy = new DoubleAnimation { To = 0.95, Duration = TimeSpan.FromMilliseconds(100) }; Storyboard.SetTarget(sy, DeleteConfirmButtonTransform); Storyboard.SetTargetProperty(sy, "ScaleY"); sb.Children.Add(sy);
        sb.Completed += (s, ev) =>
        {
            if (_currentEntryCard != null)
            {

                if (_entriesInGroup.Contains(_currentEntryCard))
                {
                    var parent = _currentEntryCard.Parent as Panel;
                    var delEntryCard = _currentEntryCard;
                    // Smooth removal inside the group's entry stack. Note: the group card itself is
                    // content-adaptive and re-flows (shrinks) only when this entry is removed at the end of
                    // the animation; the brief bottom gap under the removed entry is a platform limitation.
                    App.PlayCardRemoval(parent!, delEntryCard, _dragging, () =>
                    {
                        if (parent is StackPanel sp && sp.Parent is Grid g && g.Children.Count > 3 && g.Children[3] is Grid eh && sp.Children.Count == 0)
                            eh.Visibility = Visibility.Visible;
                        if (parent is StackPanel sp2 && sp2.Parent is Grid g2 && g2.Parent is Border groupCard)
                            UpdateGroupCount(groupCard);

                        _standaloneEntries.Remove(delEntryCard);
                        _entriesInGroup.Remove(delEntryCard);
                        _entryData.Remove(delEntryCard);
                        if (_entryExpand.TryGetValue(delEntryCard, out var ed)) { ed.Cts?.Cancel(); _entryExpand.Remove(delEntryCard); }
                        _entryIconKeys.Remove(delEntryCard);
                        _entryPinIcons.Remove(delEntryCard);
                        _entryStarIcons.Remove(delEntryCard);
                        _starredEntries.Remove(delEntryCard);
                        _apiProtocols.Remove(delEntryCard);
                        if (_entryIds.Remove(delEntryCard, out var delEid))
                        {
                            var me = App.Store?.Database.MemoEntries.FirstOrDefault(x => x.Id == delEid);
                            if (me != null) { me.IsDeleted = true; me.DeletedAt = DateTime.Now; }
                        }
                        _entryCreatedAt.Remove(delEntryCard);
                        if (_pinnedEntryCard == delEntryCard) _pinnedEntryCard = null!;
                        _currentEntryCard = null!;
                        if (GroupsContainer.Children.Count == 0) FloatInHint();
                        SyncUncategorizedCard();
                        PersistAll();
                        App.ShowToast(App.GetString("Common_Toast_Deleted"));
                        CloseDeleteConfirmDialog();
                    });
                }
                else
                {
                    var delEntryCard = _currentEntryCard;
                    var parent = delEntryCard.Parent as Panel;
                    // Smooth removal in the uncategorized panel (or wherever the standalone entry lives).
                    App.PlayCardRemoval(parent!, delEntryCard, _dragging, () =>
                    {
                        _standaloneEntries.Remove(delEntryCard);
                        _entriesInGroup.Remove(delEntryCard);
                        _entryData.Remove(delEntryCard);
                        _entryIconKeys.Remove(delEntryCard);
                        _entryPinIcons.Remove(delEntryCard);
                        _entryStarIcons.Remove(delEntryCard);
                        _starredEntries.Remove(delEntryCard);
                        _apiProtocols.Remove(delEntryCard);
                        if (_entryIds.Remove(delEntryCard, out var delEid))
                        {
                            var me = App.Store?.Database.MemoEntries.FirstOrDefault(x => x.Id == delEid);
                            if (me != null) { me.IsDeleted = true; me.DeletedAt = DateTime.Now; }
                        }
                        _entryCreatedAt.Remove(delEntryCard);
                        if (_pinnedEntryCard == delEntryCard) _pinnedEntryCard = null!;
                        _currentEntryCard = null!;
                        if (GroupsContainer.Children.Count == 0) FloatInHint();
                        SyncUncategorizedCard();
                        PersistAll();
                        App.ShowToast(App.GetString("Common_Toast_Deleted"));
                        CloseDeleteConfirmDialog();
                    });
                }
            }
            else if (_currentGroupCard != null)
            {

                if (_currentGroupCard.Child is Grid g && g.Children.Count > 3 && g.Children[2] is StackPanel entryStack)
                {
                    var rescued = new List<UIElement>();
                    foreach (var child in entryStack.Children) rescued.Add(child);
                    entryStack.Children.Clear();
                    if (g.Children[3] is Grid emptyHint)
                        emptyHint.Visibility = Visibility.Visible;
                    int idx = GroupsContainer.Children.IndexOf(_currentGroupCard);
                    foreach (var entry in rescued)
                    {
                        if (entry is Border eb)
                        {
                            eb.Margin = new Thickness(0);
                            _standaloneEntries.Add(eb);
                            _entriesInGroup.Remove(eb);

                            if (_entryIds.TryGetValue(eb, out var rid)) { var me = FindEntry(rid); if (me != null) me.GroupId = null; }
                        }
                        GroupsContainer.Children.Insert(idx++, entry);
                    }
                }
                var delGroupCard = _currentGroupCard;
                // Smooth removal of the group card (rescued entries already sit below it and slide up too).
                App.PlayCardRemoval(GroupsContainer, delGroupCard, _dragging, () =>
                {
                    _groupNameTexts.Remove(delGroupCard);
                    _groupData.Remove(delGroupCard);

                    if (_groupIds.Remove(delGroupCard, out var delGid))
                        App.Store?.Database.MemoGroups.RemoveAll(x => x.Id == delGid);
                    _groupCreatedAt.Remove(delGroupCard);
                    _pinIcons.Remove(delGroupCard);
                    _starIcons.Remove(delGroupCard);
                    _starredCards.Remove(delGroupCard);
                    _groupCountTexts.Remove(delGroupCard); // #36: group count text leak
                    if (_pinnedGroupCard == delGroupCard) _pinnedGroupCard = null!;
                    _currentGroupCard = null!;
                    if (GroupsContainer.Children.Count == 0) FloatInHint();
                    SyncUncategorizedCard();
                    PersistAll();
                    App.ShowToast(App.GetString("Common_Toast_Deleted"));
                    CloseDeleteConfirmDialog();
                });
            }
        };
        sb.Begin();
    }

    private void OnPageUnloaded(object sender, RoutedEventArgs e)
    {
        _apiCheckCts?.Cancel();
        _apiCheckCts?.Dispose();
        _apiCheckCts = null;
        _apiDiagCts?.Cancel();
        _apiDiagCts?.Dispose();
        _apiDiagCts = null;
        _relayProbeCts?.Cancel();
        _relayProbeCts?.Dispose();
        _relayProbeCts = null;
        StopRelayProbePulse();
        foreach (var cts in _flashCtsMap.Values) { cts.Cancel(); cts.Dispose(); } // N5M-01
        _flashCtsMap.Clear();

        NewGroupDialogOverlay.Visibility = Visibility.Collapsed;
        NewEntryDialogOverlay.Visibility = Visibility.Collapsed;
        EditGroupOverlay.Visibility = Visibility.Collapsed;
        DeleteConfirmOverlay.Visibility = Visibility.Collapsed;
        ApiCheckOverlay.Visibility = Visibility.Collapsed;
        ApiProtocolOverlay.Visibility = Visibility.Collapsed;
        ApiDiagOverlay.Visibility = Visibility.Collapsed;
        ApiConfirmOverlay.Visibility = Visibility.Collapsed;
        RelayProbeOverlay.Visibility = Visibility.Collapsed;
        DialogDepth.VeilClear(); 
        _targetGroupCard = null!;
        _editingEntryCard = null!;
        _currentEntryCard = null!;
        _currentGroupCard = null!;
        _pendingMoveEntry = null!;
        _apiCheckCard = null;
        _apiDiagCard = null;
        _relayProbeCard = null;
        _apiConfirmProceed = null;
        _pendingMenuAction = null!;
        
        if (_dragCard != null) { _dragCard.BorderBrush = new SolidColorBrush(App.GetBrush("AppBorderBrush").Color); _dragCard.BorderThickness = new Thickness(1); _dragCard.Opacity = 1; }
        _dragging = false;
        if (_dropIndicator != null && _dragContainer != null) { _dragContainer.Children.Remove(_dropIndicator); _dropIndicator = null; }
        if (_dragGhost != null) { DragLayer.Children.Remove(_dragGhost); _dragGhost = null; }
        _dragCard = null; _dragContainer = null;
        StopAutoScroll();
    }

    private void Page_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key != Windows.System.VirtualKey.Escape) return;
        if (ApiProtocolOverlay.Visibility == Visibility.Visible) { HideApiProtocolDialog(); e.Handled = true; return; }
        if (ApiCheckOverlay.Visibility == Visibility.Visible) { HideApiCheckOverlay(); e.Handled = true; return; }
        if (ApiDiagOverlay.Visibility == Visibility.Visible) { HideApiDiagOverlay(); e.Handled = true; return; }
        if (RelayProbeOverlay.Visibility == Visibility.Visible) { HideRelayProbeOverlay(); e.Handled = true; return; }
        if (ApiConfirmOverlay.Visibility == Visibility.Visible) { HideApiConfirmOverlay(); e.Handled = true; return; }
        if (DeleteConfirmOverlay.Visibility == Visibility.Visible) { CloseDeleteConfirmDialog(); e.Handled = true; return; }
        if (NewEntryDialogOverlay.Visibility == Visibility.Visible) { CloseNewEntryDialog(); e.Handled = true; return; }
        if (EditGroupOverlay.Visibility == Visibility.Visible) { CloseEditGroupDialog(); e.Handled = true; return; }
        if (NewGroupDialogOverlay.Visibility == Visibility.Visible) { CloseNewGroupDialog(); e.Handled = true; return; }
    }

    // ================================================================

    // ================================================================

    /* ========== BasicMemoPage API Check ==========
Function: API connectivity check dialog: probing/success/fail states with theme color, advanced config button visible only on fail
Corresponding UI: BasicMemoPage.xaml.cs
Logic Range: Below methods in this region
*/
private void ShowApiCheckDialog(Border card)
    {
        if (!_entryData.TryGetValue(card, out var d)) return;
        string url = ""; string key = "";
        foreach (var f in d.fields)
        {
            if (f.Item1 == "URL") url = f.Item2.Trim();
            else if (f.Item1 == "API Key") key = f.Item2.Trim();
        }
        _apiCheckCard = card;
        // Clear the previous report before showing "probing" - otherwise the last detection's
        // structured rows / detail linger until the async probe finishes (looks like stale history).
        ApiCheckReportRows.Children.Clear();
        ApiCheckReportPanel.Visibility = Visibility.Collapsed;
        ApiCheckStatusDetail.Text = "";
        SetApiCheckState("probing", App.GetString("Memo_Api_Checking"), App.GetString("Memo_Api_CheckingHint"));
        ShowApiCheckOverlay();
        if (string.IsNullOrWhiteSpace(url))
        {
            SetApiCheckState("fail", App.GetString("Memo_Api_MissingUrl_Title"), App.GetString("Memo_Api_MissingUrl_Desc"), true);
            return;
        }
        if (string.IsNullOrWhiteSpace(key))
        {
            SetApiCheckState("fail", App.GetString("Memo_Api_MissingKey_Title"), App.GetString("Memo_Api_MissingKey_Desc"), true);
            return;
        }
        _ = RunAutoProbeAsync(url, key);
    }

    private void SetApiCheckState(string kind, string title, string detail, bool warn = false)
    {
        ApiCheckStatusText.Text = title;
        ApiCheckStatusDetail.Text = detail;
        // Empty detail must be explicitly collapsed: the failure-detail area otherwise keeps its
        // reserved height on success, leaving a large blank stretch (dialog background) between
        // the report block and the footer note. Dialog height stays purely content-driven.
        ApiCheckStatusDetail.Visibility = string.IsNullOrEmpty(detail) ? Visibility.Collapsed : Visibility.Visible;
        ApiCheckStatusText.Foreground = kind switch
        {
            "success" => new SolidColorBrush(Color.FromArgb(0xFF, 0x4C, 0xAF, 0x50)),
            "fail" => new SolidColorBrush(warn ? Color.FromArgb(0xFF, 0xFF, 0xB3, 0x00) : Color.FromArgb(0xFF, 0xFF, 0x45, 0x45)),
            _ => App.GetBrush("AppTextPrimaryBrush")
        };
        ApiCheckAdvancedButton.Visibility = kind == "fail" ? Visibility.Visible : Visibility.Collapsed;
    }

    private void ShowApiCheckOverlay()
    {
        ApiCheckDialogTransform.ScaleX = 0.92; ApiCheckDialogTransform.ScaleY = 0.92; ApiCheckDialogTransform.TranslateY = 20;
        ApiCheckDialog.Opacity = 0; ApiCheckScrim.Opacity = 0;
        ApiCheckOverlay.Visibility = Visibility.Visible;
        DialogDepth.VeilShow(); 
        var sb = new Storyboard();
        var si = new DoubleAnimation { To = 1, Duration = TimeSpan.FromMilliseconds(250) };
        Storyboard.SetTarget(si, ApiCheckScrim); Storyboard.SetTargetProperty(si, "Opacity"); sb.Children.Add(si);
        var di = new DoubleAnimation { To = 1, Duration = TimeSpan.FromMilliseconds(300) };
        Storyboard.SetTarget(di, ApiCheckDialog); Storyboard.SetTargetProperty(di, "Opacity"); sb.Children.Add(di);
        foreach (var (v, p) in new[] { (1.0, "ScaleX"), (1.0, "ScaleY"), (0.0, "TranslateY") })
        {
            var a = new DoubleAnimation { To = v, Duration = TimeSpan.FromMilliseconds(400), EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
            Storyboard.SetTarget(a, ApiCheckDialogTransform); Storyboard.SetTargetProperty(a, p); sb.Children.Add(a);
        }
        sb.Begin();
    }

    private void HideApiCheckOverlay()
    {
        _apiCheckCts?.Cancel();
        _apiCheckCts?.Dispose();
        _apiCheckCts = null;
        var sb = new Storyboard();
        var so = new DoubleAnimation { To = 0, Duration = TimeSpan.FromMilliseconds(200) };
        Storyboard.SetTarget(so, ApiCheckScrim); Storyboard.SetTargetProperty(so, "Opacity"); sb.Children.Add(so);
        var d = new DoubleAnimation { To = 0, Duration = TimeSpan.FromMilliseconds(200) };
        Storyboard.SetTarget(d, ApiCheckDialog); Storyboard.SetTargetProperty(d, "Opacity"); sb.Children.Add(d);
        foreach (var (v, p) in new[] { (0.92, "ScaleX"), (0.92, "ScaleY"), (20.0, "TranslateY") })
        {
            var a = new DoubleAnimation { To = v, Duration = TimeSpan.FromMilliseconds(250), EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
            Storyboard.SetTarget(a, ApiCheckDialogTransform); Storyboard.SetTargetProperty(a, p); sb.Children.Add(a);
        }
        sb.Completed -= OnApiCheckHideCompleted; // E1-26: prevent handler pile-up on rapid repeated hide
        sb.Completed += OnApiCheckHideCompleted;
        DialogDepth.VeilHide(); 
        sb.Begin();
    }

    private void OnApiCheckHideCompleted(object? sender, object e)
    {
        ApiCheckOverlay.Visibility = Visibility.Collapsed;
        _apiCheckCard = null;
    }

    private void ApiCheckDialogCloseButton_Click(object sender, RoutedEventArgs e) => HideApiCheckOverlay();
    private void ApiCheckCloseButton_Click(object sender, RoutedEventArgs e) => HideApiCheckOverlay();
    private void ApiCheckScrim_Tapped(object sender, TappedRoutedEventArgs e) { if (ReferenceEquals(e.OriginalSource, ApiCheckScrim)) HideApiCheckOverlay(); }
    private void ApiCheckAdvancedButton_Click(object sender, RoutedEventArgs e) => ShowApiProtocolDialog();

    private async Task RunAutoProbeAsync(string url, string key)
    {
        try
        {
            _apiCheckCts?.Cancel();
            _apiCheckCts?.Dispose();
            _apiCheckCts = new System.Threading.CancellationTokenSource();
            var ct = _apiCheckCts.Token;

            var report = await Novara.Services.ApiProbeService.ProbeAsync(url, key, ct);
            if (ct.IsCancellationRequested) return;
            if (report.Status == Novara.Services.ApiProbeStatus.Success)
            {
                // Persist the resolved protocol (OpenAI-compatible only; vendor-specific schemes like
                // x-api-key/query re-resolve automatically on the next probe, so no stored value needed).
                if (report.Protocol == "bearer") SaveApiProtocol("Bearer");
                else if (report.Protocol == "raw") SaveApiProtocol("NoBearer");
            }
            ApplyProbeReport(report);
        }
        catch (System.OperationCanceledException) when (_apiCheckCts?.IsCancellationRequested == true)
        {
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"API 自动探测异常: {ex}");
            try { SetApiCheckState("fail", App.GetString("Memo_Api_ProbeFail_Title"), App.GetString("Memo_Api_ProbeException_Desc"), true); }
            catch { }
        }
    }

    private void ApplyProbeReport(Novara.Services.ApiProbeReport report)
    {
        // Structured report card: label/value rows for vendor / protocol / endpoint / latency / models.
        ApiCheckReportRows.Children.Clear();
        void AddRow(string label, string value)
        {
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(64) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.Children.Add(new TextBlock { Text = label, FontSize = 12, Foreground = App.GetBrush("AppTextTertiaryBrush"), VerticalAlignment = VerticalAlignment.Top });
            var val = new TextBlock { Text = value, FontSize = 12, Foreground = App.GetBrush("AppTextPrimaryBrush"), TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Top };
            Grid.SetColumn(val, 1);
            grid.Children.Add(val);
            ApiCheckReportRows.Children.Add(grid);
        }

        AddRow(App.GetString("Memo_Api_DetailVendor"), report.Vendor);
        AddRow(App.GetString("Memo_Api_DetailProtocol"), report.Protocol);
        AddRow(App.GetString("Memo_Api_DetailEndpoint"), report.Endpoint);
        AddRow(App.GetString("Memo_Api_DetailLatency"), report.LatencyMs + " ms");
        if (report.Models.Length > 0)
            AddRow(App.GetString("Memo_Api_DetailModels"), report.Models.Length + "（" + string.Join("、", report.Models.Take(5)) + "）");
        ApiCheckReportPanel.Visibility = Visibility.Visible;

        if (report.Status == Novara.Services.ApiProbeStatus.Success)
        {
            SetApiCheckState("success", App.GetString("Memo_Api_Ok_Title"), "");
            return;
        }

        // Failure: reason (masked server detail) + actionable hint in the detail block.
        var detail = new System.Text.StringBuilder();
        if (!string.IsNullOrEmpty(report.Detail)) detail.Append(report.Detail).Append('\n');
        // Key-prefix mismatch hint: the pasted key clearly belongs to another vendor than the URL does.
        if (!string.IsNullOrEmpty(report.KeyHintVendor) &&
            !string.Equals(report.KeyHintVendor, report.Vendor, StringComparison.OrdinalIgnoreCase))
        {
            detail.Append(string.Format(App.GetString("Memo_Api_KeyMismatch"), report.KeyHintVendor, report.Vendor)).Append('\n');
        }
        string hintKey = report.Status switch
        {
            Novara.Services.ApiProbeStatus.InvalidKey => "Memo_Api_HintKey",
            Novara.Services.ApiProbeStatus.InsufficientQuota => "Memo_Api_HintQuota",
            Novara.Services.ApiProbeStatus.RateLimited => "Memo_Api_HintRate",
            Novara.Services.ApiProbeStatus.NoModels => "Memo_Api_Hint404",
            Novara.Services.ApiProbeStatus.Permission => "Memo_Api_HintPermission",
            Novara.Services.ApiProbeStatus.ServerError => "Memo_Api_HintServer",
            Novara.Services.ApiProbeStatus.NetworkError => "Memo_Api_HintNetwork",
            _ => "Memo_Api_HintGeneric",
        };
        detail.Append(App.GetString(hintKey));

        var (title, warn) = report.Status switch
        {
            Novara.Services.ApiProbeStatus.InvalidKey => ("Memo_Api_AuthFail_Title", false),
            Novara.Services.ApiProbeStatus.InsufficientQuota => ("Memo_Api_Quota_Title", true),
            Novara.Services.ApiProbeStatus.RateLimited => ("Memo_Api_RateLimit_Title", true),
            Novara.Services.ApiProbeStatus.NoModels => ("Memo_Api_NoAuto_Title", true),
            Novara.Services.ApiProbeStatus.Permission => ("Memo_Api_Permission_Title", false),
            Novara.Services.ApiProbeStatus.ServerError => ("Memo_Api_ServerError_Title", false),
            Novara.Services.ApiProbeStatus.NetworkError => ("Memo_Api_NetFail_Title", false),
            _ => ("Memo_Api_ProbeFail_Title", true),
        };
        SetApiCheckState("fail", App.GetString(title), detail.ToString(), warn);
    }

    private void SaveApiProtocol(string mode)
    {
        if (_apiCheckCard != null)
        {
            _apiProtocols[_apiCheckCard] = mode;

            if (_entryIds.TryGetValue(_apiCheckCard, out var pid))
            {
                var me = FindEntry(pid);
                if (me != null) { me.Protocol = mode; App.Store?.SaveAsync(); }
            }
        }
    }

    // ================================================================
    
    // ================================================================

    private (string Url, string Key, string Model) GetApiEntryFields(Border card)
    {
        string url = "", key = "", model = "";
        if (_entryData.TryGetValue(card, out var d))
            foreach (var f in d.fields)
            {
                if (f.Item1 == "URL") url = f.Item2.Trim();
                else if (f.Item1 == "API Key") key = f.Item2.Trim();
                else if (f.Item1 == "模型 ID") model = f.Item2.Trim();
            }
        return (url, key, model);
    }

    private void ShowApiDiagConfirm(Border card)
    {
        var (url, key, model) = GetApiEntryFields(card);
        _apiDiagCard = card;
        
        ApiDiagReportRows.Children.Clear();
        ApiDiagReportPanel.Visibility = Visibility.Collapsed;
        ApiDiagStatusDetail.Text = "";
        if (string.IsNullOrWhiteSpace(url))
        {
            ShowApiDiagOverlay();
            SetApiDiagState("fail", App.GetString("Memo_Api_MissingUrl_Title"), App.GetString("Memo_Api_MissingUrl_Desc"), true);
            return;
        }
        if (string.IsNullOrWhiteSpace(key))
        {
            ShowApiDiagOverlay();
            SetApiDiagState("fail", App.GetString("Memo_Api_MissingKey_Title"), App.GetString("Memo_Api_MissingKey_Desc"), true);
            return;
        }
        if (string.IsNullOrWhiteSpace(model))
        {
            ShowApiDiagOverlay();
            SetApiDiagState("fail", App.GetString("Memo_ApiDiag_MissingModel_Title"), App.GetString("Memo_ApiDiag_MissingModel_Desc"), true);
            return;
        }
        ShowApiConfirmOverlay(() => StartApiDiag(url, key, model));
    }

    private void StartApiDiag(string url, string key, string model)
    {
        ApiDiagReportRows.Children.Clear();
        ApiDiagReportPanel.Visibility = Visibility.Collapsed;
        ApiDiagStatusDetail.Text = "";
        SetApiDiagState("probing", App.GetString("Memo_ApiDiag_Checking"), App.GetString("Memo_ApiDiag_CheckingHint"));
        ShowApiDiagOverlay();
        _ = RunApiDiagAsync(url, key, model);
    }

    private async Task RunApiDiagAsync(string url, string key, string model)
    {
        System.Threading.CancellationTokenSource? myCts = null; // N5A-05: hoisted so the OCE filters below can tell timeout from supersede
        try
        {
            _apiDiagCts?.Cancel();
            _apiDiagCts?.Dispose();
            
            myCts = new System.Threading.CancellationTokenSource(TimeSpan.FromSeconds(180));
            _apiDiagCts = myCts;
            var ct = myCts.Token;

            var report = await Novara.Services.ApiDiagnoseService.DiagnoseAsync(url, key, model, ct);
            if (myCts.IsCancellationRequested)
            {
                // N5A-05: the 180s budget fired - end the flow honestly instead of leaving the
                // overlay stuck on "checking".
                SetApiDiagState("fail", App.GetString("Memo_Api_ProbeFail_Title"), "timeout (180s)", true);
            }
            else ApplyDiagReport(report);
        }
        catch (System.OperationCanceledException) when (myCts != null && myCts.IsCancellationRequested)
        {
            // N5A-05: same timeout semantics for the OCE path.
            SetApiDiagState("fail", App.GetString("Memo_Api_ProbeFail_Title"), "timeout (180s)", true);
        }
        catch (System.OperationCanceledException)
        {
            // superseded by a newer diag run - the newer run owns the overlay now
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"API 诊断异常: {ex}");
            try { SetApiDiagState("fail", App.GetString("Memo_Api_ProbeFail_Title"), App.GetString("Memo_Api_ProbeException_Desc"), true); }
            catch { }
        }
    }

    private void ApplyDiagReport(Novara.Services.ApiDiagnoseReport report)
    {
        ApiDiagReportRows.Children.Clear();
        void AddRow(string label, string value, string status)
        {
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(22) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(96) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var color = status switch
            {
                "pass" => Color.FromArgb(0xFF, 0x4C, 0xAF, 0x50),
                "warn" => Color.FromArgb(0xFF, 0xFF, 0xB3, 0x00),
                "fail" => Color.FromArgb(0xFF, 0xFF, 0x45, 0x45),
                _ => Color.FromArgb(0xFF, 0x90, 0x90, 0x90),
            };
            var glyph = status switch
            {
                "pass" => "\uE73E",
                "warn" => "\uE7BA",
                "fail" => "\uE783",
                _ => "\uE72A",
            };
            grid.Children.Add(new FontIcon { Glyph = glyph, FontSize = 14, Foreground = new SolidColorBrush(color) });
            var lab = new TextBlock { Text = label, FontSize = 12, Foreground = App.GetBrush("AppTextTertiaryBrush"), VerticalAlignment = VerticalAlignment.Top };
            Grid.SetColumn(lab, 1);
            grid.Children.Add(lab);
            var val = new TextBlock { Text = value, FontSize = 12, Foreground = App.GetBrush("AppTextPrimaryBrush"), TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Top };
            Grid.SetColumn(val, 2);
            grid.Children.Add(val);
            ApiDiagReportRows.Children.Add(grid);
        }

        string ItemLabel(string key) => key switch
        {
            "reachability" => App.GetString("Memo_ApiDiag_Item_Reachability"),
            "balance" => App.GetString("Memo_ApiDiag_Item_Balance"),
            "metadata" => App.GetString("Memo_ApiDiag_Item_Metadata"),
            _ => App.GetString("Memo_ApiDiag_Item_Latency"),
        };
        string StatusOf(Novara.Services.ApiDiagItemStatus s) => s switch
        {
            Novara.Services.ApiDiagItemStatus.Pass => "pass",
            Novara.Services.ApiDiagItemStatus.Warn => "warn",
            Novara.Services.ApiDiagItemStatus.Fail => "fail",
            _ => "skip",
        };

        foreach (var item in report.Items)
        {
            string value = item.Key switch
            {
                "reachability" when item.Status == Novara.Services.ApiDiagItemStatus.Pass
                    => App.GetString("Memo_ApiDiag_Reachable") + "（" + report.Vendor + "，" + report.Protocol + "）",
                "reachability" => App.GetString("Memo_ApiDiag_Unreachable") + (string.IsNullOrEmpty(item.Evidence) ? "" : "：" + item.Evidence),
                "balance" when item.Status == Novara.Services.ApiDiagItemStatus.Pass
                    => App.GetString("Memo_ApiDiag_BalanceOk"),
                "balance" when item.Summary == "quota"
                    => string.Format(App.GetString("Memo_ApiDiag_BalanceQuota"), report.BalanceError ?? ""),
                "balance" when item.Summary == "unknown"
                    => string.Format(App.GetString("Memo_ApiDiag_BalanceUnknown"), report.BalanceError ?? ""),
                "balance" => App.GetString("Memo_ApiDiag_Skipped"),
                "metadata" when item.Status == Novara.Services.ApiDiagItemStatus.Pass
                    => string.IsNullOrEmpty(report.ModelReturned) ? (report.MetadataHeaders.Count + " headers") : report.ModelReturned,
                "metadata" => App.GetString("Memo_ApiDiag_MetadataMissing"),
                "latency" when item.Status == Novara.Services.ApiDiagItemStatus.Pass
                    => report.LatencyMs + " ms" + (report.TtftMs.HasValue ? " / TTFT " + report.TtftMs + " ms" : "") + (report.TokensPerSecond.HasValue ? " / " + report.TokensPerSecond.Value.ToString("0.0") + " tok/s" : ""),
                "latency" when item.Summary == "no-ttft"
                    => App.GetString("Memo_ApiDiag_NoTtft") + "（" + report.LatencyMs + " ms）",
                _ => App.GetString("Memo_ApiDiag_Skipped"),
            };
            AddRow(ItemLabel(item.Key), value, StatusOf(item.Status));
        }
        ApiDiagReportPanel.Visibility = Visibility.Visible;

        if (report.Reachable) SetApiDiagState("success", App.GetString("Memo_ApiDiag_Reachable") + "｜" + report.TokensConsumed + " tokens", "");
        else SetApiDiagState("fail", App.GetString("Memo_ApiDiag_Unreachable"), report.ReachabilityDetail, true);
    }

    private void SetApiDiagState(string kind, string title, string detail, bool warn = false)
    {
        ApiDiagStatusText.Text = title;
        ApiDiagStatusDetail.Text = detail;
        ApiDiagStatusDetail.Visibility = string.IsNullOrEmpty(detail) ? Visibility.Collapsed : Visibility.Visible;
        ApiDiagStatusText.Foreground = kind switch
        {
            "success" => new SolidColorBrush(Color.FromArgb(0xFF, 0x4C, 0xAF, 0x50)),
            "fail" => new SolidColorBrush(warn ? Color.FromArgb(0xFF, 0xFF, 0xB3, 0x00) : Color.FromArgb(0xFF, 0xFF, 0x45, 0x45)),
            _ => App.GetBrush("AppTextPrimaryBrush")
        };
    }

    private void ShowApiDiagOverlay()
    {
        ApiDiagDialogTransform.ScaleX = 0.92; ApiDiagDialogTransform.ScaleY = 0.92; ApiDiagDialogTransform.TranslateY = 20;
        ApiDiagDialog.Opacity = 0; ApiDiagScrim.Opacity = 0;
        ApiDiagOverlay.Visibility = Visibility.Visible;
        DialogDepth.VeilShow();
        var sb = new Storyboard();
        var si = new DoubleAnimation { To = 1, Duration = TimeSpan.FromMilliseconds(250) };
        Storyboard.SetTarget(si, ApiDiagScrim); Storyboard.SetTargetProperty(si, "Opacity"); sb.Children.Add(si);
        var di = new DoubleAnimation { To = 1, Duration = TimeSpan.FromMilliseconds(300) };
        Storyboard.SetTarget(di, ApiDiagDialog); Storyboard.SetTargetProperty(di, "Opacity"); sb.Children.Add(di);
        foreach (var (v, p) in new[] { (1.0, "ScaleX"), (1.0, "ScaleY"), (0.0, "TranslateY") })
        {
            var a = new DoubleAnimation { To = v, Duration = TimeSpan.FromMilliseconds(400), EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
            Storyboard.SetTarget(a, ApiDiagDialogTransform); Storyboard.SetTargetProperty(a, p); sb.Children.Add(a);
        }
        sb.Begin();
    }

    private void HideApiDiagOverlay()
    {
        _apiDiagCts?.Cancel();
        _apiDiagCts?.Dispose();
        _apiDiagCts = null;
        var sb = new Storyboard();
        var so = new DoubleAnimation { To = 0, Duration = TimeSpan.FromMilliseconds(200) };
        Storyboard.SetTarget(so, ApiDiagScrim); Storyboard.SetTargetProperty(so, "Opacity"); sb.Children.Add(so);
        var d = new DoubleAnimation { To = 0, Duration = TimeSpan.FromMilliseconds(200) };
        Storyboard.SetTarget(d, ApiDiagDialog); Storyboard.SetTargetProperty(d, "Opacity"); sb.Children.Add(d);
        foreach (var (v, p) in new[] { (0.92, "ScaleX"), (0.92, "ScaleY"), (20.0, "TranslateY") })
        {
            var a = new DoubleAnimation { To = v, Duration = TimeSpan.FromMilliseconds(250), EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
            Storyboard.SetTarget(a, ApiDiagDialogTransform); Storyboard.SetTargetProperty(a, p); sb.Children.Add(a);
        }
        sb.Completed -= OnApiDiagHideCompleted;
        sb.Completed += OnApiDiagHideCompleted;
        DialogDepth.VeilHide();
        sb.Begin();
    }

    private void OnApiDiagHideCompleted(object? sender, object e)
    {
        ApiDiagOverlay.Visibility = Visibility.Collapsed;
        _apiDiagCard = null;
    }

    private void ApiDiagDialogCloseButton_Click(object sender, RoutedEventArgs e) => HideApiDiagOverlay();
    private void ApiDiagCloseButton_Click(object sender, RoutedEventArgs e) => HideApiDiagOverlay();
    private void ApiDiagScrim_Tapped(object sender, TappedRoutedEventArgs e) { if (ReferenceEquals(e.OriginalSource, ApiDiagScrim)) HideApiDiagOverlay(); }

    

    private void ShowApiConfirmOverlay(System.Action onProceed, string? desc = null)
    {
        _apiConfirmProceed = onProceed;
        ApiConfirmDescText.Text = desc ?? App.GetString("Memo_ApiConfirm_Desc");
        ApiConfirmDialogTransform.ScaleX = 0.92; ApiConfirmDialogTransform.ScaleY = 0.92; ApiConfirmDialogTransform.TranslateY = 20;
        ApiConfirmDialog.Opacity = 0; ApiConfirmScrim.Opacity = 0;
        ApiConfirmOverlay.Visibility = Visibility.Visible;
        DialogDepth.VeilShow();
        var sb = new Storyboard();
        var si = new DoubleAnimation { To = 1, Duration = TimeSpan.FromMilliseconds(250) };
        Storyboard.SetTarget(si, ApiConfirmScrim); Storyboard.SetTargetProperty(si, "Opacity"); sb.Children.Add(si);
        var di = new DoubleAnimation { To = 1, Duration = TimeSpan.FromMilliseconds(300) };
        Storyboard.SetTarget(di, ApiConfirmDialog); Storyboard.SetTargetProperty(di, "Opacity"); sb.Children.Add(di);
        foreach (var (v, p) in new[] { (1.0, "ScaleX"), (1.0, "ScaleY"), (0.0, "TranslateY") })
        {
            var a = new DoubleAnimation { To = v, Duration = TimeSpan.FromMilliseconds(400), EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
            Storyboard.SetTarget(a, ApiConfirmDialogTransform); Storyboard.SetTargetProperty(a, p); sb.Children.Add(a);
        }
        sb.Begin();
    }

    private void HideApiConfirmOverlay()
    {
        _apiConfirmProceed = null;
        var sb = new Storyboard();
        var so = new DoubleAnimation { To = 0, Duration = TimeSpan.FromMilliseconds(200) };
        Storyboard.SetTarget(so, ApiConfirmScrim); Storyboard.SetTargetProperty(so, "Opacity"); sb.Children.Add(so);
        var d = new DoubleAnimation { To = 0, Duration = TimeSpan.FromMilliseconds(200) };
        Storyboard.SetTarget(d, ApiConfirmDialog); Storyboard.SetTargetProperty(d, "Opacity"); sb.Children.Add(d);
        foreach (var (v, p) in new[] { (0.92, "ScaleX"), (0.92, "ScaleY"), (20.0, "TranslateY") })
        {
            var a = new DoubleAnimation { To = v, Duration = TimeSpan.FromMilliseconds(250), EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
            Storyboard.SetTarget(a, ApiConfirmDialogTransform); Storyboard.SetTargetProperty(a, p); sb.Children.Add(a);
        }
        sb.Completed -= OnApiConfirmHideCompleted;
        sb.Completed += OnApiConfirmHideCompleted;
        DialogDepth.VeilHide();
        sb.Begin();
    }

    private void OnApiConfirmHideCompleted(object? sender, object e) { ApiConfirmOverlay.Visibility = Visibility.Collapsed; }

    private void ApiConfirmProceedButton_Click(object sender, RoutedEventArgs e)
    {
        var action = _apiConfirmProceed;
        HideApiConfirmOverlay();
        action?.Invoke();
    }
    private void ApiConfirmCancelButton_Click(object sender, RoutedEventArgs e) => HideApiConfirmOverlay();
    private void ApiConfirmCloseButton_Click(object sender, RoutedEventArgs e) => HideApiConfirmOverlay();
    private void ApiConfirmScrim_Tapped(object sender, TappedRoutedEventArgs e) { if (ReferenceEquals(e.OriginalSource, ApiConfirmScrim)) HideApiConfirmOverlay(); }

    // ================================================================
    
    // ================================================================

    private void ShowRelayProbeConfirm(Border card)
    {
        var (url, key, model) = GetApiEntryFields(card);
        _relayProbeCard = card;
        
        RelayProbeReportRows.Children.Clear();
        if (string.IsNullOrWhiteSpace(url))
        {
            ShowRelayProbeOverlay();
            SetRelayProbeState(App.GetString("Memo_Api_MissingUrl_Title"), App.GetString("Memo_Api_MissingUrl_Desc"), "fail");
            return;
        }
        if (string.IsNullOrWhiteSpace(key))
        {
            ShowRelayProbeOverlay();
            SetRelayProbeState(App.GetString("Memo_Api_MissingKey_Title"), App.GetString("Memo_Api_MissingKey_Desc"), "fail");
            return;
        }
        if (string.IsNullOrWhiteSpace(model))
        {
            ShowRelayProbeOverlay();
            SetRelayProbeState(App.GetString("Memo_ApiDiag_MissingModel_Title"), App.GetString("Memo_ApiDiag_MissingModel_Desc"), "fail");
            return;
        }
        bool local = url.Contains("localhost", StringComparison.OrdinalIgnoreCase) || url.Contains("127.0.0.1", StringComparison.OrdinalIgnoreCase);
        ShowApiConfirmOverlay(() => StartRelayProbe(url, key, model), local ? App.GetString("Memo_RelayProbe_Localhost") : null);
    }

    private void StartRelayProbe(string url, string key, string model)
    {
        RelayProbeReportRows.Children.Clear();
        SetRelayProbeState(App.GetString("Memo_RelayProbe_Checking"), App.GetString("Memo_RelayProbe_CheckingHint"), "probing");
        ShowRelayProbeOverlay();
        StartRelayProbePulse();
        _ = RunRelayProbeAsync(url, key, model);
    }

    private async Task RunRelayProbeAsync(string url, string key, string model)
    {
        try
        {
            _relayProbeCts?.Cancel();
            _relayProbeCts?.Dispose();
            _relayProbeCts = new System.Threading.CancellationTokenSource();
            var ct = _relayProbeCts.Token;

            var report = await Novara.Services.RelayProbeService.ProbeAsync(url, key, model, ct);
            if (ct.IsCancellationRequested) return;
            ApplyRelayProbeReport(report);
        }
        catch (System.OperationCanceledException) when (_relayProbeCts?.IsCancellationRequested == true)
        {
            StopRelayProbePulse();
        }
        catch (Exception ex)
        {
            StopRelayProbePulse();
            System.Diagnostics.Debug.WriteLine($"中转站探针异常: {ex}");
            try { SetRelayProbeState(App.GetString("Memo_Api_ProbeFail_Title"), App.GetString("Memo_Api_ProbeException_Desc"), "fail"); }
            catch { }
        }
    }

    private void ApplyRelayProbeReport(Novara.Services.RelayProbeReport report)
    {
        StopRelayProbePulse();
        RelayProbeReportRows.Children.Clear();
        string ItemLabel(string key) => key switch
        {
            "identity" => App.GetString("Memo_RelayProbe_Item_Identity"),
            "benchmark" => App.GetString("Memo_RelayProbe_Item_Benchmark"),
            "format" => App.GetString("Memo_RelayProbe_Item_Format"),
            "billing" => App.GetString("Memo_RelayProbe_Item_Billing"),
            "tool" => App.GetString("Memo_RelayProbe_Item_Tool"),
            "injection" => App.GetString("Memo_RelayProbe_Item_Injection"),
            "poisoning" => App.GetString("Memo_RelayProbe_Item_Poisoning"),
            _ => App.GetString("Memo_RelayProbe_Item_Truncation"),
        };
        void AddProbe(Novara.Services.ProbeResult p)
        {
            var (color, glyph) = p.Verdict switch
            {
                Novara.Services.ProbeVerdict.Pass => (Color.FromArgb(0xFF, 0x4C, 0xAF, 0x50), "\uE73E"),
                Novara.Services.ProbeVerdict.Warn => (Color.FromArgb(0xFF, 0xFF, 0xB3, 0x00), "\uE7BA"),
                Novara.Services.ProbeVerdict.Fail => (Color.FromArgb(0xFF, 0xFF, 0x45, 0x45), "\uE783"),
                _ => (Color.FromArgb(0xFF, 0x90, 0x90, 0x90), "\uE72A"),
            };
            var card = new Border { Background = App.GetBrush("AppSurfaceOverlayBrush"), CornerRadius = new CornerRadius(10), Padding = new Thickness(12, 10, 12, 10) };
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(22) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.Children.Add(new FontIcon { Glyph = glyph, FontSize = 14, Foreground = new SolidColorBrush(color), VerticalAlignment = VerticalAlignment.Center });
            var name = new TextBlock { Text = ItemLabel(p.Key), FontSize = 12, Foreground = App.GetBrush("AppTextPrimaryBrush"), VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(name, 1);
            grid.Children.Add(name);
            if (!string.IsNullOrEmpty(p.Evidence))
            {
                grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                var ev = new TextBlock { Text = p.Evidence, FontSize = 11, Foreground = App.GetBrush("AppTextTertiaryBrush"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 0), MaxLines = 2, TextTrimming = TextTrimming.CharacterEllipsis };
                Grid.SetRow(ev, 1);
                Grid.SetColumn(ev, 1);
                grid.Children.Add(ev);
            }
            card.Child = grid;
            RelayProbeReportRows.Children.Add(card);
        }

        foreach (var p in report.Probes) AddProbe(p);

        string verdictText = report.Verdict switch
        {
            "trusted" => App.GetString("Memo_RelayProbe_Verdict_Trusted"),
            "mostly-trusted" => App.GetString("Memo_RelayProbe_Verdict_MostlyTrusted"),
            "suspicious" => App.GetString("Memo_RelayProbe_Verdict_Suspicious"),
            "high-risk" => App.GetString("Memo_RelayProbe_Verdict_HighRisk"),
            "offline" => App.GetString("Memo_RelayProbe_Verdict_Offline"),
            _ => App.GetString("Memo_RelayProbe_Verdict_Unknown"),
        };
        var verdictColor = report.Verdict switch
        {
            "trusted" => Color.FromArgb(0xFF, 0x4C, 0xAF, 0x50),
            "mostly-trusted" => Color.FromArgb(0xFF, 0x8B, 0xC3, 0x4A),
            "suspicious" => Color.FromArgb(0xFF, 0xFF, 0xB3, 0x00),
            "high-risk" => Color.FromArgb(0xFF, 0xFF, 0x45, 0x45),
            _ => Color.FromArgb(0xFF, 0x90, 0x90, 0x90),
        };
        RelayProbeVerdictText.Text = verdictText;
        RelayProbeVerdictText.Foreground = new SolidColorBrush(verdictColor);

        var statusSb = new System.Text.StringBuilder();
        if (report.Reachable)
            statusSb.Append(App.GetString("Memo_RelayProbe_Score")).Append("：").Append(report.Score.ToString("0"))
                    .Append("　").Append(App.GetString("Memo_RelayProbe_TokenConsumed")).Append("：").Append(report.TokensConsumed);
        if (!string.IsNullOrEmpty(report.SuspectModel))
            statusSb.Append('\n').Append(string.Format(App.GetString("Memo_RelayProbe_Suspect"), report.SuspectModel));
        if (!report.Reachable && !string.IsNullOrEmpty(report.Detail))
            statusSb.Append('\n').Append(report.Detail);
        RelayProbeStatusText.Text = statusSb.ToString();
    }

    private void SetRelayProbeState(string title, string detail, string kind)
    {
        RelayProbeVerdictText.Text = title;
        RelayProbeStatusText.Text = detail;
        RelayProbeVerdictText.Foreground = kind == "fail"
            ? new SolidColorBrush(Color.FromArgb(0xFF, 0xFF, 0x45, 0x45))
            : App.GetBrush("AppTextPrimaryBrush");
    }

    
    private void StartRelayProbePulse()
    {
        _relayProbePulse?.Stop();
        var sb = new Storyboard();
        var anim = new DoubleAnimation
        {
            From = 1.0, To = 0.4,
            Duration = TimeSpan.FromMilliseconds(750),
            AutoReverse = true,
            RepeatBehavior = RepeatBehavior.Forever,
        };
        Storyboard.SetTarget(anim, RelayProbeVerdictText);
        Storyboard.SetTargetProperty(anim, "Opacity");
        sb.Children.Add(anim);
        sb.Begin();
        _relayProbePulse = sb;
    }

    private void StopRelayProbePulse()
    {
        _relayProbePulse?.Stop();
        _relayProbePulse = null;
        RelayProbeVerdictText.Opacity = 1.0; 
    }

    private void ShowRelayProbeOverlay()
    {
        
        
        double viewport = MemoRoot.ActualHeight;
        RelayProbeDialog.MaxHeight = viewport > 0 ? Math.Max(360, viewport - 60) : 720;

        RelayProbeDialogTransform.ScaleX = 0.92; RelayProbeDialogTransform.ScaleY = 0.92; RelayProbeDialogTransform.TranslateY = 20;
        RelayProbeDialog.Opacity = 0; RelayProbeScrim.Opacity = 0;
        RelayProbeOverlay.Visibility = Visibility.Visible;
        DialogDepth.VeilShow();
        var sb = new Storyboard();
        var si = new DoubleAnimation { To = 1, Duration = TimeSpan.FromMilliseconds(250) };
        Storyboard.SetTarget(si, RelayProbeScrim); Storyboard.SetTargetProperty(si, "Opacity"); sb.Children.Add(si);
        var di = new DoubleAnimation { To = 1, Duration = TimeSpan.FromMilliseconds(300) };
        Storyboard.SetTarget(di, RelayProbeDialog); Storyboard.SetTargetProperty(di, "Opacity"); sb.Children.Add(di);
        foreach (var (v, p) in new[] { (1.0, "ScaleX"), (1.0, "ScaleY"), (0.0, "TranslateY") })
        {
            var a = new DoubleAnimation { To = v, Duration = TimeSpan.FromMilliseconds(400), EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
            Storyboard.SetTarget(a, RelayProbeDialogTransform); Storyboard.SetTargetProperty(a, p); sb.Children.Add(a);
        }
        sb.Begin();
    }

    private void HideRelayProbeOverlay()
    {
        StopRelayProbePulse(); 
        _relayProbeCts?.Cancel();
        _relayProbeCts?.Dispose();
        _relayProbeCts = null;
        var sb = new Storyboard();
        var so = new DoubleAnimation { To = 0, Duration = TimeSpan.FromMilliseconds(200) };
        Storyboard.SetTarget(so, RelayProbeScrim); Storyboard.SetTargetProperty(so, "Opacity"); sb.Children.Add(so);
        var d = new DoubleAnimation { To = 0, Duration = TimeSpan.FromMilliseconds(200) };
        Storyboard.SetTarget(d, RelayProbeDialog); Storyboard.SetTargetProperty(d, "Opacity"); sb.Children.Add(d);
        foreach (var (v, p) in new[] { (0.92, "ScaleX"), (0.92, "ScaleY"), (20.0, "TranslateY") })
        {
            var a = new DoubleAnimation { To = v, Duration = TimeSpan.FromMilliseconds(250), EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
            Storyboard.SetTarget(a, RelayProbeDialogTransform); Storyboard.SetTargetProperty(a, p); sb.Children.Add(a);
        }
        sb.Completed -= OnRelayProbeHideCompleted;
        sb.Completed += OnRelayProbeHideCompleted;
        DialogDepth.VeilHide();
        sb.Begin();
    }

    private void OnRelayProbeHideCompleted(object? sender, object e)
    {
        RelayProbeOverlay.Visibility = Visibility.Collapsed;
        _relayProbeCard = null;
    }

    private void RelayProbeDialogCloseButton_Click(object sender, RoutedEventArgs e) => HideRelayProbeOverlay();
    private void RelayProbeCloseButton_Click(object sender, RoutedEventArgs e) => HideRelayProbeOverlay();
    private void RelayProbeScrim_Tapped(object sender, TappedRoutedEventArgs e) { if (ReferenceEquals(e.OriginalSource, RelayProbeScrim)) HideRelayProbeOverlay(); }

    private async void RelayProbeLoadDatasetButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var picker = new Windows.Storage.Pickers.FileOpenPicker();
            WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow));
            picker.FileTypeFilter.Add(".json");
            var file = await picker.PickSingleFileAsync();
            if (file == null) return;

            var json = System.IO.File.ReadAllText(file.Path);
            if (!Novara.Services.ProbeDataSetLoader.TryParse(json, out _, out var error))
            {
                App.ShowToast(App.GetString("Memo_RelayProbe_LoadDataset") + "：" + error);
                return;
            }
            var dest = System.IO.Path.Combine(System.AppContext.BaseDirectory, Novara.Services.ProbeDataSetLoader.DefaultFileName);
            System.IO.File.Copy(file.Path, dest, overwrite: true);
            App.ShowToast(App.GetString("Memo_RelayProbe_LoadDataset"));
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"加载探针数据集失败: {ex}");
            
            try { App.ShowToast(App.GetString("Memo_RelayProbe_LoadDataset") + "：" + ex.Message); }
            catch { }
        }
    }

    private void ShowApiProtocolDialog()
    {
        _selectedApiProtocol = _apiCheckCard != null && _apiProtocols.TryGetValue(_apiCheckCard, out var p) ? p : "Bearer";
        UpdateApiProtocolButton();
        ApiProtocolDialogTransform.ScaleX = 0.92; ApiProtocolDialogTransform.ScaleY = 0.92; ApiProtocolDialogTransform.TranslateY = 20;
        ApiProtocolDialog.Opacity = 0; ApiProtocolScrim.Opacity = 0;
        ApiProtocolOverlay.Visibility = Visibility.Visible;
        DialogDepth.VeilShow(); 
        var sb = new Storyboard();
        var si = new DoubleAnimation { To = 1, Duration = TimeSpan.FromMilliseconds(250) };
        Storyboard.SetTarget(si, ApiProtocolScrim); Storyboard.SetTargetProperty(si, "Opacity"); sb.Children.Add(si);
        var di = new DoubleAnimation { To = 1, Duration = TimeSpan.FromMilliseconds(300) };
        Storyboard.SetTarget(di, ApiProtocolDialog); Storyboard.SetTargetProperty(di, "Opacity"); sb.Children.Add(di);
        foreach (var (val, prop) in new[] { (1.0, "ScaleX"), (1.0, "ScaleY"), (0.0, "TranslateY") })
        {
            var a = new DoubleAnimation { To = val, Duration = TimeSpan.FromMilliseconds(400), EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
            Storyboard.SetTarget(a, ApiProtocolDialogTransform); Storyboard.SetTargetProperty(a, prop); sb.Children.Add(a);
        }
        sb.Begin();
    }

    private void HideApiProtocolDialog()
    {
        var sb = new Storyboard();
        var so = new DoubleAnimation { To = 0, Duration = TimeSpan.FromMilliseconds(200) };
        Storyboard.SetTarget(so, ApiProtocolScrim); Storyboard.SetTargetProperty(so, "Opacity"); sb.Children.Add(so);
        var d = new DoubleAnimation { To = 0, Duration = TimeSpan.FromMilliseconds(200) };
        Storyboard.SetTarget(d, ApiProtocolDialog); Storyboard.SetTargetProperty(d, "Opacity"); sb.Children.Add(d);
        foreach (var (v, p) in new[] { (0.92, "ScaleX"), (0.92, "ScaleY"), (20.0, "TranslateY") })
        {
            var a = new DoubleAnimation { To = v, Duration = TimeSpan.FromMilliseconds(250), EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
            Storyboard.SetTarget(a, ApiProtocolDialogTransform); Storyboard.SetTargetProperty(a, p); sb.Children.Add(a);
        }
        sb.Completed -= OnApiProtocolHideCompleted;
        sb.Completed += OnApiProtocolHideCompleted;
        DialogDepth.VeilHide(); 
        sb.Begin();
    }

    private void OnApiProtocolHideCompleted(object? sender, object e) { ApiProtocolOverlay.Visibility = Visibility.Collapsed; }

    private void ApiProtocolCloseButton_Click(object sender, RoutedEventArgs e) => HideApiProtocolDialog();
    private void ApiCancelButton_Click(object sender, RoutedEventArgs e) => HideApiProtocolDialog();
    private void ApiProtocolScrim_Tapped(object sender, TappedRoutedEventArgs e) { if (ReferenceEquals(e.OriginalSource, ApiProtocolScrim)) HideApiProtocolDialog(); }

    private void ApiProtocolButton_Click(object sender, RoutedEventArgs e)
    {
        var menu = new MenuFlyout { MenuFlyoutPresenterStyle = (Style)Application.Current.Resources["GlassMenuFlyoutPresenterStyle"] };
        void Add(string label, string mode)
        {
            var item = new MenuFlyoutItem
            {
                Style = (Style)Application.Current.Resources["GlassMenuFlyoutItemStyle"],
                Text = label,
                Icon = _selectedApiProtocol == mode ? new FontIcon { Glyph = "\uE73E" } : null
            };
            item.Click += (_, _) => { _selectedApiProtocol = mode; UpdateApiProtocolButton(); };
            menu.Items.Add(item);
        }
        Add(App.GetString("Memo_Api_ProtocolBearer"), "Bearer");
        Add(App.GetString("Memo_Api_ProtocolCompat"), "NoBearer");
        Add(App.GetString("Memo_Api_ProtocolRaw"), "Raw");
        menu.ShowAt(ApiProtocolButton, new Point(0, ApiProtocolButton.ActualHeight + 4));
    }

    private void UpdateApiProtocolButton()
    {
        ApiProtocolButtonText.Text = _selectedApiProtocol switch
        {
            "NoBearer" => App.GetString("Memo_Api_ProtocolCompat"),
            "Raw" => App.GetString("Memo_Api_ProtocolRaw"),
            _ => App.GetString("Memo_Api_ProtocolBearer")
        };
        bool raw = _selectedApiProtocol == "Raw";
        ApiRetryButton.IsEnabled = !raw;
        ApiRawHint.Visibility = raw ? Visibility.Visible : Visibility.Collapsed;
    }

    private async void ApiRetryButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            HideApiProtocolDialog();
            if (_apiCheckCard == null || !_entryData.TryGetValue(_apiCheckCard, out var d)) return;
            string url = ""; string key = "";
            foreach (var f in d.fields)
            {
                if (f.Item1 == "URL") url = f.Item2.Trim();
                else if (f.Item1 == "API Key") key = f.Item2.Trim();
            }
            if (string.IsNullOrWhiteSpace(url) || string.IsNullOrWhiteSpace(key)) return;
            SetApiCheckState("probing", App.GetString("Memo_Api_CheckingManual"), string.Format(App.GetString("Memo_Api_CheckingWith"), ApiProtocolButtonText.Text));
            _apiCheckCts?.Cancel();
            _apiCheckCts?.Dispose();
            _apiCheckCts = new System.Threading.CancellationTokenSource();
            var ct = _apiCheckCts.Token;
            var report = await Novara.Services.ApiProbeService.ProbeWithProtocolAsync(url, key, _selectedApiProtocol, ct);
            if (ct.IsCancellationRequested) return;
            if (report.Status == Novara.Services.ApiProbeStatus.Success) SaveApiProtocol(_selectedApiProtocol);
            ApplyProbeReport(report);
        }
        catch (OperationCanceledException) when (_apiCheckCts?.IsCancellationRequested == true)
        {

        }
        catch (Exception ex)
        {

            System.Diagnostics.Debug.WriteLine($"API 手动重试异常: {ex}");
            try { SetApiCheckState("fail", App.GetString("Memo_Api_ProbeFail_Title"), App.GetString("Memo_Api_ProbeException_Desc"), true); }
            catch { }
        }
    }

    

    private void AttachCardDrag(Border card)
    {
        bool armed = false;
        DispatcherTimer? timer = null;
        Point grabOffset = default;

        card.PointerPressed += (s, e) =>
        {
            if (_dragging) return;
            
            if (ReferenceEquals(card, _pinnedGroupCard) || ReferenceEquals(card, _pinnedEntryCard)) return;
            if (App.IsDescendantOfButton(e.OriginalSource as Microsoft.UI.Xaml.DependencyObject)) return; 
            var pt = e.GetCurrentPoint(card);
            if (pt.Properties.IsRightButtonPressed || !pt.Properties.IsLeftButtonPressed) return;
            grabOffset = pt.Position;
            var pointer = e.Pointer;
            armed = true;
            e.Handled = true; 

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
        _dragContainer = card.Parent as Panel;
        _grabOffset = grabOffset;
        _dropIndex = _dragOriginIndex = _dragContainer != null ? _dragContainer.Children.IndexOf(card) : 0;

        App.StopCardEntrance(card); 
        card.BorderBrush = new SolidColorBrush(Color.FromArgb(0xFF, 0x72, 0x76, 0xFF));
        card.BorderThickness = new Thickness(2);
        card.Opacity = 0.35;
        // N4M-04: reuse the card's own TranslateTransform - replacing it detached the hover
        // Storyboard's target object, permanently killing the hover lift after the first drag.
        if (card.RenderTransform is not TranslateTransform tt) { tt = new TranslateTransform(); card.RenderTransform = tt; }
        tt.X = 0; tt.Y = 0; 

        try
        {
            var ghost = new Image
            {
                Source = rtb,
                Width = card.ActualWidth,
                Height = card.ActualHeight,
                Opacity = 0.92,
            };
            var pos = card.TransformToVisual(MemoRoot).TransformPoint(new Point(0, 0));
            _dragGhost = ghost;
            DragLayer.Children.Add(ghost);
            Canvas.SetLeft(ghost, pos.X);
            Canvas.SetTop(ghost, pos.Y);
        }
        catch { _dragGhost = null; }
    }

    private void UpdateDrag(PointerRoutedEventArgs e)
    {
        if (_dragCard == null || _dragContainer == null) return;
        var pos = e.GetCurrentPoint(MemoRoot).Position;
        _lastPointerInRoot = pos;
        if (_dragGhost != null)
        {
            Canvas.SetLeft(_dragGhost, pos.X - _grabOffset.X);
            Canvas.SetTop(_dragGhost, pos.Y - _grabOffset.Y);
        }
        var ptInList = e.GetCurrentPoint(_dragContainer).Position;
        UpdateDropIndicator(ComputeDropIndex(ptInList));
        HandleAutoScroll(e);
    }

    private int ComputeDropIndex(Point ptInList)
    {
        if (_dragContainer == null) return 0;
        int count = 0;
        foreach (var child in _dragContainer.Children)
        {
            if (ReferenceEquals(child, _dropIndicator)) continue;
            if (child is FrameworkElement fe)
            {
                var top = fe.TransformToVisual(_dragContainer).TransformPoint(new Point(0, 0)).Y;
                if (ptInList.Y < top + fe.ActualHeight / 2) break;
                count++;
            }
        }
        return count;
    }

    private void UpdateDropIndicator(int index)
    {
        if (_dragContainer == null) return;

        
        int minIndex = 0;
        if (ReferenceEquals(_dragContainer, GroupsContainer))
        {
            
            bool topPinned =
                (_pinnedGroupCard != null && _dragContainer.Children.Contains(_pinnedGroupCard)) ||
                (_pinnedEntryCard != null && ReferenceEquals(_pinnedEntryCard.Parent, GroupsContainer));
            if (topPinned) minIndex = 1;
        }
        else
        {
            
            if (_pinnedEntryCard != null && ReferenceEquals(_pinnedEntryCard.Parent, _dragContainer)) minIndex = 1;
        }
        index = Math.Max(index, minIndex);

        
        
        if (ReferenceEquals(_dragContainer, GroupsContainer)
            && _dragCard != null && _groupNameTexts.ContainsKey(_dragCard)
            && _uncategorizedCard != null && GroupsContainer.Children.Contains(_uncategorizedCard))
        {
            int uncatIdx = GroupsContainer.Children.IndexOf(_uncategorizedCard);
            
            
            if (_dropIndicator != null && GroupsContainer.Children.Contains(_dropIndicator)
                && GroupsContainer.Children.IndexOf(_dropIndicator) < uncatIdx)
            {
                uncatIdx--;
            }
            index = Math.Min(index, uncatIdx);
        }

        
        if (index == _dragOriginIndex || index == _dragOriginIndex + 1)
        {
            if (_dropIndicator != null) { _dragContainer.Children.Remove(_dropIndicator); _dropIndicator = null; }
            _dropIndex = _dragOriginIndex;
            return;
        }

        if (_dropIndicator != null) _dragContainer.Children.Remove(_dropIndicator);
        _dropIndicator = CreateDropIndicator();
        _dragContainer.Children.Insert(index, _dropIndicator);
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
        var pt = e.GetCurrentPoint(MemoScrollViewer).Position;
        double topZone = 40, bottomZone = 40;
        int dir = 0;
        if (pt.Y < topZone) dir = -1;
        else if (pt.Y > MemoScrollViewer.ViewportHeight - bottomZone) dir = 1;

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
        if (_dragCard == null || _dragContainer == null || _autoScrollDir == 0) { StopAutoScroll(); return; }
        var newOffset = Math.Clamp(MemoScrollViewer.VerticalOffset + _autoScrollDir * 8, 0, MemoScrollViewer.ScrollableHeight);
        MemoScrollViewer.ChangeView(null, newOffset, null, true);

        if (++_autoScrollFrame < 3) return;
        _autoScrollFrame = 0;
        var ptInList = MemoRoot.TransformToVisual(_dragContainer).TransformPoint(_lastPointerInRoot);
        UpdateDropIndicator(ComputeDropIndex(ptInList));
    }

    private void EndDrag()
    {
        if (!_dragging || _dragCard == null) return;
        var card = _dragCard;
        var container = _dragContainer;
        _dragCard = null;
        _dragContainer = null;
        _dragging = false;

        int dropIndex = _dropIndex;

        if (_dragGhost != null) { DragLayer.Children.Remove(_dragGhost); _dragGhost = null; }
        if (_dropIndicator != null && container != null) { container.Children.Remove(_dropIndicator); _dropIndicator = null; }
        StopAutoScroll();

        card.BorderBrush = new SolidColorBrush(App.GetBrush("AppBorderBrush").Color);
        card.BorderThickness = new Thickness(1);
        card.Opacity = 1;

        if (container == null) return;
        int curIdx = container.Children.IndexOf(card);
        if (curIdx != dropIndex)
        {
            container.Children.Remove(card);
            if (curIdx < dropIndex) dropIndex--;
            container.Children.Insert(dropIndex, card);
        }

        PersistAll(); 
    }
}
