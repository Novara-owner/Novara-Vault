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
    private Services.IconRing? _iconRing;
    private string _entrySelectedIcon = "";
    private Services.IconRing? _entryIconRing;
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
    private System.Threading.CancellationTokenSource? _renderCts;
    private Novara.Models.NovaraDatabase? _loadedDb;
    private List<Border> _standaloneEntries = new();
    private HashSet<Border> _entriesInGroup = new();
    private Border _uncategorizedCard = null!;
    private Border _targetGroupCard = null!;
    private Border _currentEntryCard = null!;


    private readonly HashSet<Border> _pinnedEntryCards = new();
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



    private readonly Dictionary<Border, List<TextBlock>> _maskedFieldTexts = new();
    private readonly Dictionary<Border, string> _entryIconKeys = new();

    private readonly Dictionary<TextBox, System.Threading.CancellationTokenSource> _flashCtsMap = new();
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
    private List<(FrameworkElement fe, double top, double half)>? _dropCache;
    private bool _dropCacheDirty;
    private int _dragOriginIndex;
    private Point _lastPointerInRoot;
    private int _autoScrollDir;
    private int _autoScrollFrame;
    private bool _autoScrollActive;

    public BasicMemoPage()
    {
        this.InitializeComponent();
        Novara.Services.DialogDepth.AttachContainer((Grid)Content);
        this.Loaded += Page_Loaded;
        this.Unloaded += OnPageUnloaded;
        this.KeyDown += Page_KeyDown;
        GenerateIcon2.Data = App.CreateGeometry(IconData.Dice);
        GenerateIcon3.Data = App.CreateGeometry(IconData.Dice);
        GenerateIcon4.Data = App.CreateGeometry(IconData.Dice);
        GenerateIcon5.Data = App.CreateGeometry(IconData.Dice);
        GenerateIcon6.Data = App.CreateGeometry(IconData.Dice);
        GenerateIcon7.Data = App.CreateGeometry(IconData.Dice);
        _contextMenu = BuildContextMenu();
        AddInfoIcon.PointerEntered += (_, _) => AddInfoIcon.Opacity = 1.0;
        AddInfoIcon.PointerExited += (_, _) => AddInfoIcon.Opacity = 0.6;
    }






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
            Foreground = App.GetBrush("AppDangerTextBrush"),
            Icon = new PathIcon { Data = App.CreateGeometry(IconData.SoftDelete), Foreground = App.GetBrush("AppDangerTextBrush") }
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

        for (int i = 0; i < EntryTypeLabels.Count; i++)
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
                EntryTypeButtonText.Text = TypeLabel(type);
                EntryTypeButtonText.Foreground = App.GetBrush("AppTextPrimaryBrush");
                ApplyEntryFormLayout(type);
            };
            menu.Items.Add(item);
        }
        return menu;
    }


    private int PinnedFirstInsertIndex(Panel container, Border incoming)
    {
        var pinned = RegionPinnedEntryCard(container);
        return (pinned != null && !ReferenceEquals(pinned, incoming))
            ? container.Children.IndexOf(pinned) + 1
            : 0;
    }



    private Border? RegionPinnedEntryCard(Panel container)
        => container.Children.OfType<Border>().FirstOrDefault(b => _pinnedEntryCards.Contains(b));



    private void StripEntryPersonalMarks(Border card)
    {
        bool hadPin = _pinnedEntryCards.Remove(card);
        bool hadStar = _starredEntries.Remove(card);
        if (hadPin && _entryPinIcons.ContainsKey(card)) _entryPinIcons[card].Visibility = Visibility.Collapsed;
        if (hadStar && _entryStarIcons.ContainsKey(card)) _entryStarIcons[card].Visibility = Visibility.Collapsed;
        if ((hadPin || hadStar) && _entryIds.TryGetValue(card, out var id))
        {
            var me = FindEntry(id);
            if (me != null) { me.IsPinned = false; me.IsStarred = false; }
        }
    }


    private void MoveEntryToGroup(Border card, Border gb)
    {

        var srcPanel = card.Parent as Panel;
        if (srcPanel != null) srcPanel.Children.Remove(card);
        StripEntryPersonalMarks(card);

        if (srcPanel is StackPanel srcSp && srcSp.Parent is Grid srcGrid && srcGrid.Parent is Border srcGroup && !ReferenceEquals(srcGroup, gb))
        {
            UpdateGroupCount(srcGroup);
            if (srcSp.Children.Count == 0 && srcGrid.Children.Count > 3 && srcGrid.Children[3] is Grid eh)
                eh.Visibility = Visibility.Visible;
        }
        _standaloneEntries.Remove(card);
        _entriesInGroup.Add(card);

        if (_entryIds.TryGetValue(card, out var inId) && _groupIds.TryGetValue(gb, out var tGid))
        { var me = FindEntry(inId); if (me != null) me.GroupId = tGid; }
        var targetContent = gb.Child as Grid;
        if (targetContent != null && targetContent.Children.Count > 3 && targetContent.Children[2] is StackPanel entryStack)
        {
            entryStack.Children.Insert(PinnedFirstInsertIndex(entryStack, card), card);
            if (targetContent.Children[3] is Grid emptyHint)
                emptyHint.Visibility = Visibility.Collapsed;
            UpdateGroupCount(gb);
        }
        SyncUncategorizedCard();
        PersistAll();
        ApplyCardFilters();
    }


    private Border? FindOwningGroupCard(Border entryCard)
    {
        if (!_entriesInGroup.Contains(entryCard)) return null;
        for (var p = entryCard.Parent as FrameworkElement; p != null; p = p.Parent as FrameworkElement)
            if (p is Border b && _groupNameTexts.ContainsKey(b)) return b;
        return null;
    }


    private void MoveEntryOut(Border card)
    {
        var p = card.Parent as Panel;
        if (p != null) p.Children.Remove(card);
        StripEntryPersonalMarks(card);

        if (p is StackPanel sp && sp.Parent is Grid g && g.Parent is Border srcGroup)
        {
            UpdateGroupCount(srcGroup);
            if (sp.Children.Count == 0 && g.Children.Count > 3 && g.Children[3] is Grid eh)
                eh.Visibility = Visibility.Visible;
        }
        _entriesInGroup.Remove(card);



        int insIdx = 0;
        if (_pinnedGroupCard != null && GroupsContainer.Children.Contains(_pinnedGroupCard))
            insIdx = GroupsContainer.Children.IndexOf(_pinnedGroupCard) + 1;

        if (RegionPinnedEntryCard(GroupsContainer) is Border topPinned)
        {
            int pe = GroupsContainer.Children.IndexOf(topPinned) + 1;
            if (pe > insIdx) insIdx = pe;
        }
        if (_uncategorizedCard != null && GroupsContainer.Children.Contains(_uncategorizedCard))
        {
            int uc = GroupsContainer.Children.IndexOf(_uncategorizedCard);
            if (insIdx > uc) insIdx = uc;
        }
        GroupsContainer.Children.Insert(insIdx, card);
        _standaloneEntries.Insert(0, card);

        if (_entryIds.TryGetValue(card, out var outId)) { var me = FindEntry(outId); if (me != null) me.GroupId = null; }
        SyncUncategorizedCard();
        PersistAll();
        ApplyCardFilters();
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
        TotpTextBox.Text = ""; TotpStatus.Text = ""; TotpStatus.Visibility = Visibility.Collapsed;
        TotpSection.Visibility = Visibility.Collapsed;
        CustomFormContainer.Visibility = Visibility.Collapsed;
        CustomNameTextBox.Text = "";
        CustomInfoTextBox_0.Text = "";
        CustomInfoDynamicPanel.Children.Clear();
        _customInfoCount = 1;
        CustomInfoAddButton.Visibility = Visibility.Visible;
        ClearEntryIconSelection();
        UpdateNewEntryConfirmState();
    }



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

    private bool _entrancePlayed;
    private bool _confirming;
    private bool _renderInProgress;
    private bool _persistAfterRender;

    private void Page_Loaded(object sender, RoutedEventArgs e)
    {

        LoadFromStore();




        try
        {
            ApplyEntryFormLayout("邮箱");
            ResetEntryTypeForms();
            EntryTypeSection.Visibility = Visibility.Collapsed;
        }
        catch { }



        if (_totpTimer != null && !_totpTimer.IsRunning) _totpTimer.Start();

        EditGroupClosePathIcon.Data = (Geometry)Microsoft.UI.Xaml.Markup.XamlBindingHelper.ConvertValue(typeof(Geometry), IconData.Close);
    }


    public void StealFocus() => FocusSink.Focus(FocusState.Programmatic);


    private async void LoadFromStore()
    {
        if (_storeLoaded) return;
        _loadedDb = App.Store?.Database;
        _storeLoaded = true;
        HintText.Visibility = Visibility.Collapsed;
        var db = App.Store?.Database;



        if (db == null) { _storeLoaded = false; return; }

        var groups = db.MemoGroups.ToList();
        var entries = db.MemoEntries.Where(x => !x.IsDeleted).ToList();
        _totpRows.Clear();

        bool playEntrance = !_entrancePlayed;
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


        if (_pinnedGroupCard != null && GroupsContainer.Children.Count > 0 && !ReferenceEquals(GroupsContainer.Children[0], _pinnedGroupCard))
        {
            GroupsContainer.Children.Remove(_pinnedGroupCard);
            GroupsContainer.Children.Insert(0, _pinnedGroupCard);
        }


        _renderInProgress = true;

        _renderCts?.Cancel();
        var renderCts = _renderCts = new System.Threading.CancellationTokenSource();
        try
        {
            await ChunkedRender.RunAsync(entries.Count, 6, DispatcherQueue, (s, e) =>
            {
            for (int i = s; i < e; i++)
            {
                var en = entries[i];
                var fields = en.Fields.Select(f => (f.Label, f.Value, f.CanCopy || f.Label == "备注")).ToList();
                var card = CreateEntryCard(en.Type, en.Name, en.KeyInfo, fields, en.IconKey);
                _entryIds[card] = en.Id;
                _entryIconKeys[card] = en.IconKey;
                _entryCreatedAt[card] = en.CreatedAt;
                if (!string.IsNullOrEmpty(en.Protocol)) _apiProtocols[card] = en.Protocol;
                if (en.IsStarred) { _starredEntries.Add(card); if (_entryStarIcons.TryGetValue(card, out var si)) si.Visibility = Visibility.Visible; }

                if (en.IsPinned) { _pinnedEntryCards.Add(card); if (_entryPinIcons.TryGetValue(card, out var pi)) pi.Visibility = Visibility.Visible; }

                if (en.GroupId is Guid gid)
                {
                    Border? groupCard = null;
                    foreach (var kv in _groupIds)
                        if (kv.Value == gid) { groupCard = kv.Key; break; }
                    if (groupCard != null && groupCard.Child is Grid gg && gg.Children.Count > 3 && gg.Children[2] is StackPanel sp)
                    {
                        if (en.IsPinned) sp.Children.Insert(0, card); else sp.Children.Add(card);
                        if (gg.Children[3] is Grid eh) eh.Visibility = Visibility.Collapsed;
                        _entriesInGroup.Add(card);
                        UpdateGroupCount(groupCard);
                        continue;
                    }

                }
                if (en.IsPinned) { GroupsContainer.Children.Insert(0, card); _standaloneEntries.Insert(0, card); }
                else { _standaloneEntries.Add(card); GroupsContainer.Children.Add(card); }
                if (playEntrance && entIdx < 10) App.PlayCardEntrance(card, entIdx); entIdx++;
            }
            }, renderCts.Token);
        }
        catch (System.OperationCanceledException) { return; }
        catch (Exception ex)
        {





            System.Diagnostics.Debug.WriteLine($"备忘录列表渲染失败（页面将被重建）: {ex}");
            App.MainWindow?.RetireTabPage(typeof(BasicMemoPage));
            return;
        }




        finally
        {
            renderCts.Dispose();
            if (ReferenceEquals(_renderCts, renderCts)) { _renderCts = null; _renderInProgress = false; }
        }

        _entrancePlayed = true;
        if (_standaloneEntries.Count > 0) SyncUncategorizedCard();
        ApplyCardFilters();
        if (_persistAfterRender) { _persistAfterRender = false; PersistAll(); }
    }

    private void PersistAll()
    {
        if (_renderInProgress) { _persistAfterRender = true; return; }
        if (_loadedDb != null && !ReferenceEquals(_loadedDb, App.Store?.Database)) return;
        var db = App.Store?.Database;
        if (db == null) return;
        var softDeleted = db.MemoEntries.Where(x => x.IsDeleted).ToList();
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




        var memoKnownIds = entries.Select(x => x.Id).Concat(softDeleted.Select(x => x.Id)).ToHashSet();
        var memoOrphans = entryById.Values.Where(x => !x.IsDeleted && !memoKnownIds.Contains(x.Id)).ToList();
        db.MemoEntries.Clear();
        db.MemoEntries.AddRange(entries);


        var memoSeen = entries.Select(x => x.Id).ToHashSet();
        db.MemoEntries.AddRange(softDeleted.Where(x => !memoSeen.Contains(x.Id)));
        db.MemoEntries.AddRange(memoOrphans);
        App.Store?.SaveAsync();
    }

    private MemoEntry? FindEntry(Guid id)
        => App.Store?.Database.MemoEntries.FirstOrDefault(x => x.Id == id);

    private MemoGroup? FindGroup(Guid id)
        => App.Store?.Database.MemoGroups.FirstOrDefault(x => x.Id == id);

    private void FloatInHint()
    {
        HintText.Visibility = Visibility.Visible;

        if (!App.IsAnimationsEnabled)
        {
            HintText.Opacity = 1;
            if (HintText.RenderTransform is TranslateTransform st) st.Y = 0;
            return;
        }
        HintText.Opacity = 0;
        if (HintText.RenderTransform is not TranslateTransform tt)
        {
            tt = new TranslateTransform { Y = 20 };
            HintText.RenderTransform = tt;
        }
        else tt.Y = 20;
        var sb = new Storyboard();
        var oa = new DoubleAnimation { To = 1, Duration = TimeSpan.FromMilliseconds(400), EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
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
        NewGroupDialogTransform.ScaleX = 0.94; NewGroupDialogTransform.ScaleY = 0.94; NewGroupDialogTransform.TranslateY = 24;
        NewGroupDialog.Opacity = 0; DialogScrim.Opacity = 0;
        NewGroupDialogOverlay.Visibility = Visibility.Visible;
        DialogDepth.VeilShow();
        DialogShowAnimation.Begin();
        _confirming = false;
        InitializeIcons();
        LoadUngroupedEntries();



        GroupNameTextBox.Text = string.Empty;
        ConfirmButton.IsEnabled = false;
    }

    private void GroupNameTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        ConfirmButton.IsEnabled = !string.IsNullOrWhiteSpace(GroupNameTextBox.Text);
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

        _iconRing ??= new Services.IconRing(IconScrollViewer, IconPanel);
        _iconRing.Tapped -= GroupIconRingTapped;
        _iconRing.Tapped += GroupIconRingTapped;
        _iconRing.Build(iconFiles.Select(f => System.IO.Path.GetFileNameWithoutExtension(f) ?? string.Empty), tag => new Border
        {
            Width = 48, Height = 48, CornerRadius = new CornerRadius(12),
            Background = App.GetBrush("AppSurfaceOverlayBrush"),
            Tag = tag,
            Child = IconData.GroupIconMap.TryGetValue(tag, out var key)
                ? new Viewbox { Width = 24, Height = 24, Stretch = Stretch.Uniform, Child = new PathIcon { Data = App.CreateGeometry(IconData.GetGroupPath(key)), Foreground = App.GetBrush("IconForegroundBrush"), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center } }
                : null
        });
        _selectedIcon = "";
        _iconRing?.Highlight(null);
    }

    private void GroupIconRingTapped(Border border)
    {
        _selectedIcon = border.Tag?.ToString() ?? "";
        _iconRing?.Highlight(_selectedIcon);
        _iconRing?.CenterTo(border);
    }





    private void LoadEntryIconSelector()
    {

        if (EntryIconPanel.Children.Count > 0) return;
        _entryIconRing ??= new Services.IconRing(EntryIconScrollViewer, EntryIconPanel);
        _entryIconRing.Tapped -= EntryIconRingTapped;
        _entryIconRing.Tapped += EntryIconRingTapped;
        _entryIconRing.Build(IconData.GroupIconKeysInOrder(), key => new Border
        {
            Width = 48, Height = 48, CornerRadius = new CornerRadius(12),
            Background = App.GetBrush("AppSurfaceOverlayBrush"),
            Tag = key,
            Child = new Viewbox { Width = 24, Height = 24, Stretch = Stretch.Uniform, Child = new PathIcon { Data = App.CreateGeometry(IconData.GetGroupPath(key)), Foreground = App.GetBrush("IconForegroundBrush") } }
        });
        _entrySelectedIcon = "";
    }

    private void EntryIconRingTapped(Border border)
    {
        _entrySelectedIcon = border.Tag?.ToString() ?? "";
        _entryIconRing?.Highlight(_entrySelectedIcon);
        _entryIconRing?.CenterTo(border);
        UpdateNewEntryConfirmState();
    }


    private void ClearEntryIconSelection()
    {
        _entrySelectedIcon = "";
        _entryIconRing?.Highlight(null);
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





        UngroupedListSection.RowDefinitions[1].Height = new GridLength(Math.Min(84, entries.Count * 26 + 4));
        UngroupedListSection.Visibility = entries.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private Border CreateGroupCard(string groupName, string groupIconKey)
    {
        var card = new Border
        {
            CornerRadius = new CornerRadius(12),
            Background = App.GetBrush("AppSurfaceOverlayBrush"),
            BorderBrush = new SolidColorBrush(App.GetBrush("AppBorderBrush").Color),
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
        var pinIcon = new Viewbox { Width = 16, Height = 16, Stretch = Microsoft.UI.Xaml.Media.Stretch.Uniform, VerticalAlignment = VerticalAlignment.Center, Visibility = Visibility.Collapsed, Margin = new Thickness(0, 0, 12, 0), Child = new PathIcon { Data = App.CreateGeometry(IconData.CardPin), Foreground = new SolidColorBrush(PaperTheme.BrandColor) } };
        rightPanel.Children.Add(pinIcon);
        var starIcon = new Viewbox { Width = 16, Height = 16, Stretch = Microsoft.UI.Xaml.Media.Stretch.Uniform, VerticalAlignment = VerticalAlignment.Center, Visibility = Visibility.Collapsed, Margin = new Thickness(0, 0, 12, 0), Child = new PathIcon { Data = App.CreateGeometry(IconData.CardStar), Foreground = new SolidColorBrush(PaperTheme.BrandColor) } };
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


        emptyStack.Children.Add(new TextBlock { Text = "\uE721", FontFamily = new FontFamily("Segoe Fluent Icons"), FontSize = 28, Foreground = App.GetBrush("AppTextTertiaryBrush"), HorizontalAlignment = HorizontalAlignment.Center });
        emptyStack.Children.Add(new TextBlock { Text = App.GetString("Memo_Group_Empty_Tip"), HorizontalAlignment = HorizontalAlignment.Center, FontSize = 13, Foreground = App.GetBrush("AppTextSecondaryBrush") });
        emptyStack.Children.Add(new TextBlock { Text = App.GetString("Memo_Group_Empty_Hint"), HorizontalAlignment = HorizontalAlignment.Center, FontSize = 12, Foreground = App.GetBrush("AppTextTertiaryBrush") });
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
        card.PointerEntered += (s, e) => { if (_dragging) return; if (card.BorderBrush is SolidColorBrush sb) sb.Color = hoverBorderColor; if (App.IsAnimationsEnabled && card.RenderTransform is TranslateTransform) { var st = new Storyboard(); var la = new DoubleAnimation { To = -3, Duration = TimeSpan.FromMilliseconds(200), EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } }; Storyboard.SetTarget(la, translate); Storyboard.SetTargetProperty(la, "Y"); st.Children.Add(la); st.Begin(); } };
        card.PointerExited += (s, e) => { if (_dragging) return; if (card.BorderBrush is SolidColorBrush sb) sb.Color = baseBorderColor; if (App.IsAnimationsEnabled && card.RenderTransform is TranslateTransform) { var st = new Storyboard(); var la = new DoubleAnimation { To = 0, Duration = TimeSpan.FromMilliseconds(300), EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } }; Storyboard.SetTarget(la, translate); Storyboard.SetTargetProperty(la, "Y"); st.Children.Add(la); st.Begin(); } };
        card.ContextRequested += (s, e) => { e.Handled = true; _currentGroupCard = card; bool ip = card == _pinnedGroupCard; bool starred = _starredCards.Contains(card); var m = BuildGroupCardContextMenu(ip, starred); if (e.TryGetPosition(card, out var p)) m.ShowAt(card, p); };
        AttachCardDrag(card);
        return card;
    }

    private void UpdateGroupCount(Border groupCard)
    {
        if (groupCard == null || !_groupCountTexts.TryGetValue(groupCard, out var ct)) return;
        int count = 0;
        if (groupCard.Child is Grid g && g.Children.Count > 2 && g.Children[2] is StackPanel entryStack)
            foreach (var c in entryStack.Children)
                if (c.Visibility == Visibility.Visible) count++;
        ct.Text = string.Format(App.GetString("Memo_Count_N"), count);
    }



    public void ApplyCardFilters()
    {
        string wsId = App.CurrentWorkspaceId;
        bool wsActive = !string.IsNullOrEmpty(wsId);
        var db = App.Store?.Database;





        foreach (var texts in _maskedFieldTexts.Values) RestoreFieldMasks(texts);

        foreach (var child in GroupsContainer.Children)
        {
            if (child is Border eb && _entryIds.TryGetValue(eb, out var eid))
            {
                eb.Visibility = EntryMatchesWorkspace(eid, wsActive, wsId, db) ? Visibility.Visible : Visibility.Collapsed;
            }
            else if (child is Border gb && _groupIds.ContainsKey(gb))
            {
                int visibleCount = 0;
                if (gb.Child is Grid gg && gg.Children.Count > 3 && gg.Children[2] is StackPanel sp)
                {
                    foreach (var c in sp.Children)
                        if (c is Border sc && _entryIds.TryGetValue(sc, out var sid))
                        {
                            bool vis = EntryMatchesWorkspace(sid, wsActive, wsId, db);
                            sc.Visibility = vis ? Visibility.Visible : Visibility.Collapsed;
                            if (vis) visibleCount++;
                        }
                }


                gb.Visibility = visibleCount > 0 || !wsActive ? Visibility.Visible : Visibility.Collapsed;
                if (gb.Child is Grid gg2 && gg2.Children.Count > 3 && gg2.Children[3] is Grid eh)
                    eh.Visibility = visibleCount > 0 ? Visibility.Collapsed : Visibility.Visible;
                UpdateGroupCount(gb);
            }
            else if (child == _uncategorizedCard && _uncategorizedCard?.Child is Grid ug && ug.Children.Count > 2 && ug.Children[2] is Panel cp)
            {
                int visibleCount = 0;
                foreach (var c in cp.Children)
                    if (c is Border sc && _entryIds.TryGetValue(sc, out var sid))
                    {
                        bool vis = EntryMatchesWorkspace(sid, wsActive, wsId, db);
                        sc.Visibility = vis ? Visibility.Visible : Visibility.Collapsed;
                        if (vis) visibleCount++;
                    }
                _uncategorizedCard.Visibility = visibleCount > 0 ? Visibility.Visible : Visibility.Collapsed;
            }
        }

        UpdateHintVisibility();
    }

    private bool EntryMatchesWorkspace(Guid eid, bool wsActive, string wsId, Novara.Models.NovaraDatabase? db)
    {
        if (!wsActive) return true;
        if (db == null) return true;
        var e = db.MemoEntries.FirstOrDefault(x => x.Id == eid);
        return e != null && e.WorkspaceId == wsId;
    }

    private void UpdateHintVisibility()
    {
        bool anyVisible = false;
        foreach (var c in GroupsContainer.Children)
            if (c.Visibility == Visibility.Visible) { anyVisible = true; break; }
        if (anyVisible) { HintText.Visibility = Visibility.Collapsed; return; }
        HintText.Text = !string.IsNullOrEmpty(App.CurrentWorkspaceId) ? App.GetString("Workspace_EmptyHint") : App.GetString("Memo_Empty_Tip");
        if (HintText.Visibility != Visibility.Visible) FloatInHint();
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












    private const string FieldMask = MemoFieldMask.Mask;

    private static bool IsMaskedLabel(string label) => MemoFieldMask.IsMaskedLabel(label);


    private static void RestoreFieldMasks(List<TextBlock> maskedTexts)
    {
        foreach (var tb in maskedTexts)
            if (tb.Tag is string) { tb.Text = FieldMask; tb.CharacterSpacing = 150; }
    }

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

            string curTotp = TotpTextBox.Text.Trim();
            string origTotp = _editOrigFields.FirstOrDefault(f => f.label == "TOTP").value ?? "";
            if (curTotp != origTotp) return true;
            var cur = new List<string>();
            if (!string.IsNullOrWhiteSpace(CustomInfoTextBox_0.Text)) cur.Add(CustomInfoTextBox_0.Text.Trim());
            foreach (var child in CustomInfoDynamicPanel.Children)
                if (child is TextBox tb && !string.IsNullOrWhiteSpace(tb.Text)) cur.Add(tb.Text.Trim());



            var orig = _editOrigFields.Where(f => f.label != "TOTP").Select(f => f.value).ToList();
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

            string curTotp = TotpTextBox.Text.Trim();
            string origTotp = _editOrigFields.FirstOrDefault(f => f.label == "TOTP").value ?? "";
            if (curTotp != origTotp) return true;
            return false;
        }

        return true;
    }

    private Border CreateEntryCard(string type, string name, string keyInfo, List<(string label, string value, bool canCopy)> fields, string iconKey = "")
    {

        var typeIconGeom = App.CreateGeometry(type switch { "邮箱" => IconData.Email, "账户" => IconData.Account[0] + " " + IconData.Account[1], "API Key" => "M640.45 480.574c17.496 0 31.713 14.041 31.996 31.47l0.004 0.53V811.05c0 88.338-71.612 159.95-159.95 159.95-87.454 0-158.516-70.187-159.929-157.305l-0.021-2.645v-43.29c0-17.673 14.327-32 32-32 17.496 0 31.713 14.042 31.996 31.47l0.004 0.53v43.29c0 52.991 42.958 95.95 95.95 95.95 52.462 0 95.09-42.104 95.937-94.364l0.013-1.586V512.574c0-17.674 14.327-32 32-32zM256.357 352.55c17.673 0 32 14.327 32 32 0 17.496-14.042 31.713-31.47 31.995l-0.53 0.005h-42.745c-52.786 0-95.612 42.94-95.612 95.95 0 52.48 41.974 95.09 94.031 95.938l1.581 0.012H512.5c17.673 0 32 14.327 32 32 0 17.497-14.042 31.714-31.47 31.996l-0.53 0.004H213.612c-88.17 0-159.612-71.63-159.612-159.95 0-87.436 70.02-158.516 156.972-159.929l2.64-0.021h42.745z m554.454 0C899.268 352.55 971 424.15 971 512.5c0 87.468-70.305 158.517-157.54 159.929l-2.649 0.021h-40.997c-17.673 0-32-14.327-32-32 0-17.496 14.042-31.713 31.47-31.995l0.53-0.005h40.997c53.137 0 96.189-42.971 96.189-95.95 0-52.449-42.195-95.09-94.598-95.937l-1.59-0.013H512.5c-17.673 0-32-14.327-32-32 0-17.497 14.042-31.713 31.47-31.996l0.53-0.004h298.311zM512.5 54c87.454 0 158.516 70.187 159.929 157.305l0.021 2.645v42.772c0 17.673-14.327 32-32 32-17.496 0-31.713-14.042-31.996-31.47l-0.004-0.53V213.95c0-52.992-42.958-95.95-95.95-95.95-52.462 0-95.09 42.104-95.937 94.364l-0.013 1.586v297.212c0 17.673-14.327 32-32 32-17.496 0-31.713-14.042-31.996-31.471l-0.004-0.53v-297.21C352.55 125.611 424.162 54 512.5 54z", "网站" => IconData.Website, "银行卡" => IconData.BankCard, "WiFi" => string.Join(" ", IconData.WiFi), "证件" => string.Join(" ", IconData.IdCard), _ => !string.IsNullOrEmpty(iconKey) ? IconData.GetGroupPath(iconKey) : IconData.Custom });
        var card = new Border { CornerRadius = new CornerRadius(14), Background = App.GetBrush("AppSurfaceOverlayBrush"), BorderBrush = new SolidColorBrush(App.GetBrush("AppBorderBrush").Color), BorderThickness = new Thickness(1), Padding = new Thickness(12, 10, 12, 10), HorizontalAlignment = HorizontalAlignment.Stretch, VerticalAlignment = VerticalAlignment.Top };
        var rootGrid = new Grid(); rootGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); rootGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var mainRow = new Grid(); mainRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); mainRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); mainRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); mainRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); mainRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var typeIconViewbox = new Viewbox { Width = 24, Height = 24, Stretch = Microsoft.UI.Xaml.Media.Stretch.Uniform, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, -2, 10, 0), Child = new PathIcon { Data = typeIconGeom, Foreground = App.GetBrush("IconForegroundBrush") } };
        Grid.SetColumn(typeIconViewbox, 0);
        var infoPanel = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) };
        infoPanel.Children.Add(new TextBlock { Text = name, FontSize = 13, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, Foreground = App.GetBrush("AppTextPrimaryBrush"), TextTrimming = TextTrimming.CharacterEllipsis });





        infoPanel.Children.Add(new TextBlock { Text = MemoFieldMask.MaskedKeyInfo(type, keyInfo, fields.Select(f => (f.label, f.value))), FontSize = 11, Foreground = App.GetBrush("AppTextTertiaryBrush"), TextTrimming = TextTrimming.CharacterEllipsis });
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
                Foreground = new SolidColorBrush(PaperTheme.BrandColor)
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
                Foreground = new SolidColorBrush(PaperTheme.BrandColor)
            }
        };
        Grid.SetColumn(entryStarIcon, 3);

        var expandedGrid = new Grid { Visibility = Visibility.Collapsed, Margin = new Thickness(0, 12, 0, 0) }; expandedGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); expandedGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var maskedTexts = new List<TextBlock>();
        _maskedFieldTexts[card] = maskedTexts;
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
            if (!targetExpand) RestoreFieldMasks(maskedTexts);
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
            if (label == "TOTP") { fieldsPanel.Children.Add(BuildTotpRow(value)); continue; }
            var row = new Grid { ColumnSpacing = 8 }; row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.Children.Add(new TextBlock { Text = FieldLabelText(label), FontSize = 12, Foreground = App.GetBrush("AppTextSecondaryBrush"), VerticalAlignment = VerticalAlignment.Center });
            var recess = new Border { Background = App.GetBrush("AppSurfaceBrush"), CornerRadius = new CornerRadius(6), Padding = new Thickness(10, 6, 10, 6) }; Grid.SetColumn(recess, 1);
            var innerGrid = new Grid { MinHeight = 24 }; innerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); innerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            bool maskedField = IsMaskedLabel(label);
            var valueText = new TextBlock { Text = maskedField ? FieldMask : value, FontSize = 12, CharacterSpacing = maskedField ? 150 : App.GetCharacterSpacing(value, 150), Foreground = App.GetBrush("AppTextPrimaryBrush"), VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
            if (maskedField)
            {





                valueText.Tag = value;
                var tapSurface = new Border { Background = new SolidColorBrush(Color.FromArgb(0, 0, 0, 0)), Child = valueText };
                tapSurface.Tapped += (_, _) =>
                {
                    if (valueText.Tag is not string original) return;
                    valueText.Text = original;
                    valueText.CharacterSpacing = App.GetCharacterSpacing(original, 150);
                };
                maskedTexts.Add(valueText);
                innerGrid.Children.Add(tapSurface);
            }
            else innerGrid.Children.Add(valueText);
            if (canCopy) { var cb = new Button { Width = 24, Height = 24, Style = (Style)Application.Current.Resources["NovaraIconButtonStyle"], Background = new SolidColorBrush(Color.FromArgb(0, 0, 0, 0)), BorderThickness = new Thickness(0), Padding = new Thickness(4), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0, 0, 0), Tag = value, IsTabStop = false, Content = new Viewbox { Width = 12, Height = 12, Stretch = Microsoft.UI.Xaml.Media.Stretch.Uniform, Child = new PathIcon { Data = App.CreateGeometry(IconData.Copy), Foreground = App.GetBrush("IconForegroundBrush") } } }; cb.Click += CopyToClipboardButton_Click; Grid.SetColumn(cb, 1); innerGrid.Children.Add(cb); }
            recess.Child = innerGrid; row.Children.Add(recess); fieldsPanel.Children.Add(row);
        }
        Grid.SetRow(fieldsPanel, 1); expandedGrid.Children.Add(divider2); expandedGrid.Children.Add(fieldsPanel); Grid.SetRow(expandedGrid, 1);
        rootGrid.Children.Add(mainRow); rootGrid.Children.Add(expandedGrid); card.Child = rootGrid;



        var translate = new TranslateTransform { Y = 0 }; card.RenderTransform = translate;
        var borderBrush = App.GetBrush("AppBorderBrush");
        var hoverBorderColor = Color.FromArgb(
            (byte)Math.Min(borderBrush.Color.A + 0x30, 0xFF),
            borderBrush.Color.R,
            borderBrush.Color.G,
            borderBrush.Color.B);
        var baseBorderColor = borderBrush.Color;
        card.PointerEntered += (s, e) => { e.Handled = true; if (_dragging) return; if (card.BorderBrush is SolidColorBrush sb) sb.Color = hoverBorderColor; if (App.IsAnimationsEnabled && card.RenderTransform is TranslateTransform) { var st = new Storyboard(); var la = new DoubleAnimation { To = -3, Duration = TimeSpan.FromMilliseconds(200), EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } }; Storyboard.SetTarget(la, translate); Storyboard.SetTargetProperty(la, "Y"); st.Children.Add(la); st.Begin(); } };
        card.PointerExited += (s, e) => { e.Handled = true; if (_dragging) return; if (card.BorderBrush is SolidColorBrush sb) sb.Color = baseBorderColor; if (App.IsAnimationsEnabled && card.RenderTransform is TranslateTransform) { var st = new Storyboard(); var la = new DoubleAnimation { To = 0, Duration = TimeSpan.FromMilliseconds(300), EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } }; Storyboard.SetTarget(la, translate); Storyboard.SetTargetProperty(la, "Y"); st.Children.Add(la); st.Begin(); } };

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
                try { ApplyEntryFormLayout(d.type); }
                catch (System.Runtime.InteropServices.COMException)
                {

                    ApplyEntryFormLayout(d.type);
                }
                if (d.type == "自定义")
                {
                    CustomNameTextBox.Text = d.name;
                    if (d.fields.Count > 0) CustomInfoTextBox_0.Text = d.fields[0].Item2;


                    ClearEntryIconSelection();
                    _entrySelectedIcon = _entryIconKeys.TryGetValue(card, out var ik) ? ik : "";
                    if (!string.IsNullOrEmpty(_entrySelectedIcon) && _entryIconRing != null)
                    {

                        if (_entryIconRing.FindByTag(_entrySelectedIcon) is Border match) _entryIconRing.CenterTo(match);
                        _entryIconRing.Highlight(_entrySelectedIcon);
                    }

                    CustomInfoDynamicPanel.Children.Clear();
                    _customInfoCount = 1;
                    for (int i = 1; i < d.fields.Count; i++)
                    {
                        if (d.fields[i].Item1 == "TOTP") { TotpTextBox.Text = d.fields[i].Item2; continue; }
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
                        if (f.Item1 == "TOTP") { TotpTextBox.Text = f.Item2; continue; }
                        var slot = FieldSlotFor(d.type, f.Item1);
                        if (slot != null) slot.Text = f.Item2;
                    }
                }

                UpdateNewEntryConfirmState();
            };

            bool entryPinned = _pinnedEntryCards.Contains(card);
            var pinMi = new MenuFlyoutItem { Style = (Style)Application.Current.Resources["GlassMenuFlyoutItemStyle"], Text = entryPinned ? App.GetString("Menu_Unpin") : App.GetString("Menu_Pin"), Icon = new PathIcon { Data = App.CreateGeometry(entryPinned ? IconData.Unpin : IconData.Pin), Foreground = App.GetBrush("IconForegroundBrush") } };
            pinMi.Click += (s2, e2) => { if (entryPinned) UnpinEntryCard(card); else PinEntryCard(card); };

            var starMi = new MenuFlyoutItem { Style = (Style)Application.Current.Resources["GlassMenuFlyoutItemStyle"], Text = _starredEntries.Contains(card) ? App.GetString("Menu_Unstar") : App.GetString("Menu_Star"), Icon = new PathIcon { Data = App.CreateGeometry(_starredEntries.Contains(card) ? IconData.Unstar : IconData.Star), Foreground = App.GetBrush("IconForegroundBrush") } };
            starMi.Click += (s2, e2) => { if (_starredEntries.Contains(card)) UnstarEntryCard(card); else StarEntryCard(card); };

            bool inGroup = _entriesInGroup.Contains(card);
            var moveSub = new MenuFlyoutSubItem { Style = (Style)Application.Current.Resources["GlassMenuFlyoutSubItemStyle"], Text = inGroup ? App.GetString("Menu_MoveOut") : App.GetString("Menu_MoveIn"), Icon = new PathIcon { Data = App.CreateGeometry(inGroup ? IconData.MoveOut[0] + " " + IconData.MoveOut[1] : IconData.MoveIn[0] + " " + IconData.MoveIn[1]), Foreground = App.GetBrush("IconForegroundBrush") } };

            if (inGroup)
            {

                var owningGroup = FindOwningGroupCard(card);
                foreach (var child in GroupsContainer.Children)
                {
                    if (child is Border gb && !ReferenceEquals(gb, owningGroup) && _groupNameTexts.ContainsKey(gb))
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
            var delMi = new MenuFlyoutItem { Style = (Style)Application.Current.Resources["GlassMenuFlyoutItemStyle"], Text = App.GetString("Menu_Delete"), Foreground = App.GetBrush("AppDangerTextBrush"), Icon = new PathIcon { Data = App.CreateGeometry(IconData.SoftDelete), Foreground = App.GetBrush("AppDangerTextBrush") } };
            delMi.Click += (s2, e2) => { _currentEntryCard = card; OpenDeleteConfirmDialog(isGroup: false); };

            m.Items.Add(pinMi);
            m.Items.Add(starMi);
            if (_entryData.TryGetValue(card, out var websiteData) && websiteData.type == "网站")
            {


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
        if (sender is Button btn && btn.Tag is string text) { var dp = new DataPackage(); dp.SetText(text); try { Clipboard.SetContent(dp); App.ShowToast(App.GetString("Common_Toast_Copied")); } catch { App.ShowToast(App.GetString("Common_Toast_CopyFail"), ToastTone.Error); } }
    }

    private void OpenWebsite(string url)
    {
        if (string.IsNullOrWhiteSpace(url)) return;
        var target = url.Trim();
        if (!target.Contains("://")) target = "https://" + target;
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(target) { UseShellExecute = true }); }
        catch { }
    }

    private async void FlashTextBox(TextBox tb)
    {

        if (_flashCtsMap.TryGetValue(tb, out var prev)) { prev.Cancel(); prev.Dispose(); }
        var cts = new System.Threading.CancellationTokenSource();
        _flashCtsMap[tb] = cts;

        var orig = _flashOriginalBgs.TryGetValue(tb, out var existing) ? existing : tb.Background;
        _flashOriginalBgs[tb] = orig;
        var dgc = ((SolidColorBrush)App.GetBrush("AppDangerTextBrush")).Color;
        tb.Background = new SolidColorBrush(Color.FromArgb(0x33, dgc.R, dgc.G, dgc.B));
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

    private Border CreateUncategorizedCard()
    {
        var card = new Border
        {
            CornerRadius = new CornerRadius(12),
            Background = App.GetBrush("AppSurfaceOverlayBrush"),
            BorderBrush = new SolidColorBrush(App.GetBrush("AppBorderBrush").Color),
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




                    int insertPos = 0;
                    if (RegionPinnedEntryCard(sp) is Border stackPinned)
                        insertPos = sp.Children.IndexOf(stackPinned) + 1;
                    for (int i = _standaloneEntries.Count - 1; i >= 0; i--)
                    {
                        var entry = _standaloneEntries[i];
                        GroupsContainer.Children.Remove(entry);
                        entry.Margin = new Thickness(0, 0, 0, 8);


                        sp.Children.Insert(insertPos, entry);
                        _standaloneEntries.RemoveAt(i);
                    }
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
        if (_confirming) return; _confirming = true;
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


        for (int mi = toMove.Count - 1; mi >= 0; mi--)
        {
            var entry = toMove[mi];

            if (_uncategorizedCard?.Child is Grid ugc && ugc.Children.Count > 2 && ugc.Children[2] is StackPanel usp && usp.Children.Contains(entry))
                usp.Children.Remove(entry);
            else
                GroupsContainer.Children.Remove(entry);
            StripEntryPersonalMarks(entry);
            _standaloneEntries.Remove(entry);
            _entriesInGroup.Add(entry);

            if (_entryIds.TryGetValue(entry, out var mvId)) { var me = FindEntry(mvId); if (me != null) me.GroupId = newGroup.Id; }
            if (nc.Child is Grid g && g.Children.Count > 3 && g.Children[2] is StackPanel entryStack)
            {
                entryStack.Children.Insert(PinnedFirstInsertIndex(entryStack, entry), entry);
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
                GroupsContainer.Children.Remove(_pendingMoveEntry);
            StripEntryPersonalMarks(_pendingMoveEntry);
            _standaloneEntries.Remove(_pendingMoveEntry);
            _entriesInGroup.Add(_pendingMoveEntry);

            if (_entryIds.TryGetValue(_pendingMoveEntry, out var pmId)) { var me = FindEntry(pmId); if (me != null) me.GroupId = newGroup.Id; }
            if (nc.Child is Grid ng && ng.Children.Count > 3 && ng.Children[2] is StackPanel entryStack)
            {
                entryStack.Children.Insert(PinnedFirstInsertIndex(entryStack, _pendingMoveEntry), _pendingMoveEntry);
                if (ng.Children[3] is Grid emptyHint)
                    emptyHint.Visibility = Visibility.Collapsed;
                UpdateGroupCount(nc);
            }
            _pendingMoveEntry = null!;
        }

        SyncUncategorizedCard();
        PersistAll();
        ApplyCardFilters();


        bool groupHiddenByWorkspace = !string.IsNullOrEmpty(App.CurrentWorkspaceId)
            && toMove.Count == 0 && _pendingMoveEntry == null;
        App.ShowToast(App.GetString(groupHiddenByWorkspace ? "Memo_Toast_Group_Hidden" : "Common_Toast_Created"));
        var sb = new Storyboard(); var sx = new DoubleAnimation { To = 0.95, Duration = TimeSpan.FromMilliseconds(100) }; Storyboard.SetTarget(sx, ConfirmButtonTransform); Storyboard.SetTargetProperty(sx, "ScaleX"); sb.Children.Add(sx);
        var sy = new DoubleAnimation { To = 0.95, Duration = TimeSpan.FromMilliseconds(100) }; Storyboard.SetTarget(sy, ConfirmButtonTransform); Storyboard.SetTargetProperty(sy, "ScaleY"); sb.Children.Add(sy);
        sb.Completed += (s, ev) => { var sb2 = new Storyboard(); var sx2 = new DoubleAnimation { To = 1.0, Duration = TimeSpan.FromMilliseconds(200) }; Storyboard.SetTarget(sx2, ConfirmButtonTransform); Storyboard.SetTargetProperty(sx2, "ScaleX"); sb2.Children.Add(sx2); var sy2 = new DoubleAnimation { To = 1.0, Duration = TimeSpan.FromMilliseconds(200) }; Storyboard.SetTarget(sy2, ConfirmButtonTransform); Storyboard.SetTargetProperty(sy2, "ScaleY"); sb2.Children.Add(sy2); sb2.Completed += (s2, e2) => CloseNewGroupDialog(); sb2.Begin(); };
        sb.Begin();
    }



    private static readonly IReadOnlyList<string> EntryTypeLabels = MemoEntryTypes.All;

    private static IReadOnlyDictionary<string, string[]> EntryTypeFieldLabels => MemoEntryTypes.FieldLabels;
    private const int MaxCustomInfoCount = 5;
    private int _customInfoCount = 1;

    private void ApplyEntryFormLayout(string type)
    {
        FormField2Label.Text = ""; FormField3Label.Text = ""; FormField4Label.Text = ""; FormField5Label.Text = ""; FormField6Label.Text = ""; FormField7Label.Text = "";
        TotpLabel.Text = App.GetString("Totp_Label");
        TotpTextBox.PlaceholderText = App.GetString("Totp_Placeholder");
        FormField2TextBox.PlaceholderText = ""; FormField3TextBox.PlaceholderText = ""; FormField4TextBox.PlaceholderText = ""; FormField5TextBox.PlaceholderText = ""; FormField6TextBox.PlaceholderText = ""; FormField7TextBox.PlaceholderText = "";
        FormField2TextBox.Text = ""; FormField3TextBox.Text = ""; FormField4TextBox.Text = ""; FormField5TextBox.Text = ""; FormField6TextBox.Text = ""; FormField7TextBox.Text = "";
        FormField2Section.Visibility = Visibility.Collapsed; FormField3Section.Visibility = Visibility.Collapsed;
        FormField4Section.Visibility = Visibility.Collapsed; FormField5Section.Visibility = Visibility.Collapsed;
        FormField6Section.Visibility = Visibility.Collapsed; FormField7Section.Visibility = Visibility.Collapsed;
        EntryNameTextBox.Text = "";
        TotpTextBox.Text = ""; TotpStatus.Text = ""; TotpStatus.Visibility = Visibility.Collapsed;
        UpdateGenerateButtons(type);
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


        Panel? totpHost = null;
        if (type == "邮箱" || type == "账户" || type == "WiFi") totpHost = FormField3Section;
        else if (type == "网站") totpHost = FormField4Section;
        if (totpHost != null)
        {
            if (TotpSection.Parent is Panel prev && !ReferenceEquals(prev, totpHost)) prev.Children.Remove(TotpSection);
            Grid.SetRow(TotpSection, 2);
            if (!ReferenceEquals(TotpSection.Parent, totpHost)) totpHost.Children.Add(TotpSection);
            TotpSection.Visibility = Visibility.Visible;
        }
        else
        {
            TotpSection.Visibility = Visibility.Collapsed;
        }
        UpdateNewEntryConfirmState();
    }





    private TextBox? _genTargetSlot;
    private string _genMode = "password";
    private int _genTokenBytes = 32;
    private string _genPreview = "";

    private void UpdateGenerateButtons(string type)
    {



        GenerateBtn2.Visibility = GenerateBtn3.Visibility = GenerateBtn4.Visibility =
            GenerateBtn5.Visibility = GenerateBtn6.Visibility = GenerateBtn7.Visibility = Visibility.Collapsed;
        if (!EntryTypeFieldLabels.TryGetValue(type, out var labels)) return;
        foreach (var label in labels)
        {
            var slot = FieldSlotFor(type, label);
            if (slot == null) continue;
            var btn = slot.Name switch
            {
                "FormField2TextBox" => GenerateBtn2,
                "FormField3TextBox" => GenerateBtn3,
                "FormField4TextBox" => GenerateBtn4,
                "FormField5TextBox" => GenerateBtn5,
                "FormField6TextBox" => GenerateBtn6,
                _ => GenerateBtn7
            };
            btn.Visibility = label.Contains("密码") ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    private void GenerateBtn_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string n }) return;
        var slot = n switch
        {
            "2" => FormField2TextBox, "3" => FormField3TextBox, "4" => FormField4TextBox,
            "5" => FormField5TextBox, "6" => FormField6TextBox, _ => FormField7TextBox
        };
        OpenGenerateDialog(slot);
    }


    private void OpenGenerateDialog(TextBox slot)
    {
        _genTargetSlot = slot;
        _genTokenBytes = 32;
        GenTitle.Text = App.GetString("Gen_Title");
        GenModePassword.Content = App.GetString("Gen_Mode_Password");
        GenModeUuid.Content = App.GetString("Gen_Mode_Uuid");
        GenModeToken.Content = App.GetString("Gen_Mode_Token");
        GenLengthLabel.Text = App.GetString("Gen_Length");
        GenChkUpper.Content = App.GetString("Gen_CharUpper");
        GenChkLower.Content = App.GetString("Gen_CharLower");
        GenChkDigits.Content = App.GetString("Gen_CharDigits");
        GenChkSymbols.Content = App.GetString("Gen_CharSymbols");
        GenChkExcludeAmb.Content = App.GetString("Gen_ExcludeAmbiguous");
        GenRegenerate.Content = App.GetString("Gen_Regenerate");
        GenTokenHint.Text = App.GetString("Gen_TokenHint");
        GenTokenChk16.Content = "16 B"; GenTokenChk32.Content = "32 B"; GenTokenChk48.Content = "48 B";
        GenTokenChk16.IsChecked = false; GenTokenChk48.IsChecked = false; GenTokenChk32.IsChecked = true;
        GenCancelText.Text = App.GetString("Common_Button_Cancel");
        GenFillText.Text = App.GetString("Gen_Fill");
        GenChkUpper.IsChecked = true; GenChkLower.IsChecked = true; GenChkDigits.IsChecked = true;
        GenChkSymbols.IsChecked = true; GenChkExcludeAmb.IsChecked = true;
        GenLengthSlider.Value = 16;
        GenerateHideAnimation.Completed -= OnGenerateHideCompleted;
        GenerateHideAnimation.Completed += OnGenerateHideCompleted;
        SetGenMode("password");
        GenerateHideAnimation.Stop();
        GenerateShowAnimation.Stop();
        GenerateDialogTransform.ScaleX = 0.94; GenerateDialogTransform.ScaleY = 0.94; GenerateDialogTransform.TranslateY = 24;
        GenerateDialog.Opacity = 0; GenerateScrim.Opacity = 0;
        GenerateOverlay.Visibility = Visibility.Visible;
        DialogDepth.VeilShow();
        GenerateShowAnimation.Begin();
    }

    private void SetGenMode(string mode)
    {
        _genMode = mode;
        Style On(string key) => (Style)Application.Current.Resources[key];

        GenModePassword.Style = On(mode == "password" ? "NovaraPrimaryButtonStyle" : "NovaraOutlineButtonStyle");
        GenModeUuid.Style = On(mode == "uuid" ? "NovaraPrimaryButtonStyle" : "NovaraOutlineButtonStyle");
        GenModeToken.Style = On(mode == "token" ? "NovaraPrimaryButtonStyle" : "NovaraOutlineButtonStyle");
        GenModePassword.Opacity = mode == "password" ? 1 : 0.6;
        GenModeUuid.Opacity = mode == "uuid" ? 1 : 0.6;
        GenModeToken.Opacity = mode == "token" ? 1 : 0.6;
        GenPasswordParams.Visibility = mode == "password" ? Visibility.Visible : Visibility.Collapsed;
        GenTokenParams.Visibility = mode == "token" ? Visibility.Visible : Visibility.Collapsed;


        foreach (var b in new[] { GenModePassword, GenModeUuid, GenModeToken })
            Microsoft.UI.Xaml.VisualStateManager.GoToState(b, "Normal", false);
        Regenerate();
    }

    private void Regenerate()
    {
        try
        {
            _genPreview = _genMode switch
            {
                "uuid" => SecretGenerator.GenerateUuid(),
                "token" => SecretGenerator.GenerateToken(_genTokenBytes),
                _ => SecretGenerator.GeneratePassword(new Novara.Services.PasswordOptions(
                    (int)GenLengthSlider.Value,
                    GenChkUpper.IsChecked == true, GenChkLower.IsChecked == true,
                    GenChkDigits.IsChecked == true, GenChkSymbols.IsChecked == true,
                    GenChkExcludeAmb.IsChecked == true))
            };
        }
        catch (ArgumentException) { _genPreview = ""; }
        GenPreviewText.Text = _genPreview;
        GenFillText.Opacity = _genPreview.Length > 0 ? 1.0 : 0.4;
        if (_genMode == "password")
        {
            GenLengthValue.Text = ((int)GenLengthSlider.Value).ToString();
            var bits = (int)Math.Round(Novara.Services.SecretGenerator.EntropyBits(new Novara.Services.PasswordOptions(
                (int)GenLengthSlider.Value,
                GenChkUpper.IsChecked == true, GenChkLower.IsChecked == true,
                GenChkDigits.IsChecked == true, GenChkSymbols.IsChecked == true,
                GenChkExcludeAmb.IsChecked == true)));
            GenEntropyText.Text = string.Format(App.GetString("Gen_Entropy"), bits);
        }
    }

    private void GenModePassword_Click(object sender, RoutedEventArgs e) => SetGenMode("password");
    private void GenModeUuid_Click(object sender, RoutedEventArgs e) => SetGenMode("uuid");
    private void GenModeToken_Click(object sender, RoutedEventArgs e) => SetGenMode("token");
    private void GenChk_Changed(object sender, RoutedEventArgs e) { if (GenerateOverlay.Visibility == Visibility.Visible) Regenerate(); }
    private void GenLengthSlider_ValueChanged(object sender, RoutedEventArgs e) { if (GenerateOverlay.Visibility == Visibility.Visible) Regenerate(); }
    private void GenTokenChk_Click(object sender, RoutedEventArgs e)
    {


        if (sender is CheckBox cb && cb.Tag is string t && int.TryParse(t, out var n))
        {
            bool repeat = _genTokenBytes == n;
            GenTokenChk16.IsChecked = n == 16;
            GenTokenChk32.IsChecked = n == 32;
            GenTokenChk48.IsChecked = n == 48;
            _genTokenBytes = n;
            if (!repeat) Regenerate();
        }
    }
    private void GenRegenerate_Click(object sender, RoutedEventArgs e) => Regenerate();

    private void GenFill_Click(object sender, RoutedEventArgs e)
    {
        if (_genTargetSlot != null && _genPreview.Length > 0)
            _genTargetSlot.Text = _genPreview;
        CloseGenerateDialog();
    }

    private void GenCancel_Click(object sender, RoutedEventArgs e) => CloseGenerateDialog();
    private void GenClose_Click(object sender, RoutedEventArgs e) => CloseGenerateDialog();
    private void GenerateScrim_Tapped(object sender, TappedRoutedEventArgs e)
    { if (ReferenceEquals(e.OriginalSource, GenerateScrim)) CloseGenerateDialog(); }

    private void CloseGenerateDialog()
    {
        GenerateHideAnimation.Completed -= OnGenerateHideCompleted;
        GenerateHideAnimation.Completed += OnGenerateHideCompleted;
        DialogDepth.VeilHide();
        GenerateHideAnimation.Begin();
    }

    private void OnGenerateHideCompleted(object? sender, object e)
    {
        GenerateOverlay.Visibility = Visibility.Collapsed;
        _genTargetSlot = null;
    }











    private sealed class TotpRowRef
    {
        public TotpRowRef(FrameworkElement root, TextBlock code, TextBlock seconds, ScaleTransform fillScale,
            byte[] key, string algorithm, int digits, int period)
        { Root = root; Code = code; Seconds = seconds; FillScale = fillScale; Key = key; Algorithm = algorithm; Digits = digits; Period = period; }
        public FrameworkElement Root; public TextBlock Code; public TextBlock Seconds; public ScaleTransform FillScale;
        public byte[] Key; public string Algorithm; public int Digits; public int Period;
        public int DetachedTicks;
        public Storyboard? FillAnim;
    }
    private readonly List<TotpRowRef> _totpRows = new();
    private Microsoft.UI.Dispatching.DispatcherQueueTimer? _totpTimer;

    private void TotpTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        UpdateNewEntryConfirmState();
        var v = TotpTextBox.Text.Trim();
        if (string.IsNullOrEmpty(v)) { TotpStatus.Visibility = Visibility.Collapsed; return; }
        if (TotpService.TryParse(v, out var cfg))
        {
            var summary = string.IsNullOrEmpty(cfg.Issuer)
                ? $"{cfg.Algorithm} · {cfg.Digits}d/{cfg.Period}s"
                : $"{cfg.Issuer} · {cfg.Algorithm} · {cfg.Digits}d/{cfg.Period}s";
            TotpStatus.Text = string.Format(App.GetString("Totp_Ok"), summary);
            TotpStatus.Foreground = App.GetBrush("AppPrimaryButtonBrush");
        }
        else
        {
            TotpStatus.Text = App.GetString("Totp_Bad");
            TotpStatus.Foreground = App.GetBrush("AppDangerTextBrush");
        }
        TotpStatus.Visibility = Visibility.Visible;
    }

    private FrameworkElement BuildTotpRow(string storedValue)
    {
        var row = new Grid { ColumnSpacing = 8 };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.Children.Add(new TextBlock { Text = App.GetString("Totp_Label"), FontSize = 12, Foreground = App.GetBrush("AppTextSecondaryBrush"), VerticalAlignment = VerticalAlignment.Center });

        var recess = new Border { Background = App.GetBrush("AppSurfaceBrush"), CornerRadius = new CornerRadius(6), Padding = new Thickness(10, 6, 10, 6) };
        Grid.SetColumn(recess, 1);

        var inner = new Grid { MinHeight = 26 };
        inner.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        inner.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        for (int i = 0; i < 3; i++) inner.ColumnDefinitions.Add(new ColumnDefinition { Width = i == 0 ? new GridLength(1, GridUnitType.Star) : GridLength.Auto });

        if (TotpService.TryParse(storedValue, out var cfg))
        {
            var nowUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            var code = new TextBlock { Text = TotpService.ComputeCode(cfg, nowUnix), FontSize = 16, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, Foreground = App.GetBrush("AppTextPrimaryBrush"), VerticalAlignment = VerticalAlignment.Center, CharacterSpacing = 200 };
            var seconds = new TextBlock { Text = TotpService.RemainingSeconds(cfg.Period, nowUnix) + "s", FontSize = 11, Foreground = App.GetBrush("AppTextSecondaryBrush"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) };
            var cb = new Button { Width = 24, Height = 24, Style = (Style)Application.Current.Resources["NovaraIconButtonStyle"], Background = new SolidColorBrush(Color.FromArgb(0, 0, 0, 0)), BorderThickness = new Thickness(0), Padding = new Thickness(4), VerticalAlignment = VerticalAlignment.Center, IsTabStop = false, Content = new Viewbox { Width = 12, Height = 12, Stretch = Microsoft.UI.Xaml.Media.Stretch.Uniform, Child = new PathIcon { Data = App.CreateGeometry(IconData.Copy), Foreground = App.GetBrush("IconForegroundBrush") } } };
            cb.Click += (_, _) => { var dp = new DataPackage(); dp.SetText(TotpService.ComputeCode(cfg)); try { Clipboard.SetContent(dp); App.ShowToast(App.GetString("Common_Toast_Copied")); } catch { App.ShowToast(App.GetString("Common_Toast_CopyFail"), ToastTone.Error); } };
            Grid.SetColumn(code, 0); Grid.SetColumn(seconds, 1); Grid.SetColumn(cb, 2);
            inner.Children.Add(code); inner.Children.Add(seconds); inner.Children.Add(cb);





            int remain0 = TotpService.RemainingSeconds(cfg.Period, DateTimeOffset.UtcNow.ToUnixTimeSeconds());
            var fillScale = new ScaleTransform { ScaleX = Math.Clamp((double)remain0 / cfg.Period, 0, 1) };
            var fillBorder = new Border { Background = App.GetBrush("AppPrimaryButtonBrush"), CornerRadius = new CornerRadius(1.5), RenderTransform = fillScale, RenderTransformOrigin = new Point(0, 0.5) };
            var barTrack = new Border { Height = 3, CornerRadius = new CornerRadius(1.5), Background = App.GetBrush("AppBorderBrush"), Margin = new Thickness(0, 4, 0, 0), Child = fillBorder };
            Grid.SetRow(barTrack, 1); Grid.SetColumn(barTrack, 0); Grid.SetColumnSpan(barTrack, 3);
            inner.Children.Add(barTrack);
            EnsureTotpTimer();
            _totpRows.Add(new TotpRowRef(row, code, seconds, fillScale, cfg.Key, cfg.Algorithm, cfg.Digits, cfg.Period));
        }
        else
        {





            inner.Children.Add(new TextBlock { Text = App.GetString("Totp_InvalidRender"), FontSize = 12, Foreground = App.GetBrush("AppTextSecondaryBrush"), TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center });
        }
        recess.Child = inner;
        row.Children.Add(recess);
        return row;
    }

    private void EnsureTotpTimer()
    {
        _totpTimer ??= DispatcherQueue.CreateTimer();
        if (_totpTimer.IsRunning) return;
        _totpTimer.Interval = TimeSpan.FromSeconds(1);
        _totpTimer.Tick += (_, _) => UpdateTotpRows();
        _totpTimer.Start();
    }

    private void UpdateTotpRows()
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        for (int i = _totpRows.Count - 1; i >= 0; i--)
        {
            var r = _totpRows[i];


            if (!TotpRowAttached(r.Root))
            {
                if (++r.DetachedTicks < 2) continue;
                _totpRows.RemoveAt(i); continue;
            }
            r.DetachedTicks = 0;
            var remain = TotpService.RemainingSeconds(r.Period, now);
            r.Code.Text = TotpService.ComputeCode(r.Key, r.Algorithm, now / r.Period, r.Digits);
            r.Seconds.Text = remain + "s";
            double frac = Math.Clamp((double)remain / r.Period, 0, 1);
            if (remain >= r.Period) SetTotpFill(r, frac);
            else if (App.IsAnimationsEnabled) SlideTotpFill(r, frac);
            else SetTotpFill(r, frac);
        }
    }

    private static void SetTotpFill(TotpRowRef r, double frac)
    {
        r.FillAnim?.Stop(); r.FillAnim = null;
        r.FillScale.ScaleX = frac;
    }

    private static void SlideTotpFill(TotpRowRef r, double fromFrac)
    {
        r.FillAnim?.Stop();
        var sb = new Storyboard();
        var anim = new DoubleAnimation { From = fromFrac, To = Math.Clamp(fromFrac - 1.0 / r.Period, 0, 1), Duration = TimeSpan.FromSeconds(1) };
        Storyboard.SetTarget(anim, r.FillScale);
        Storyboard.SetTargetProperty(anim, "ScaleX");
        sb.Children.Add(anim);
        r.FillAnim = sb;
        sb.Begin();
    }


    private bool TotpRowAttached(Microsoft.UI.Xaml.DependencyObject row)
    {
        var p = (Microsoft.UI.Xaml.DependencyObject?)row;
        for (int guard = 0; p != null && guard < 64; guard++)
        {
            if (ReferenceEquals(p, this)) return true;
            p = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetParent(p);
        }
        return false;
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
        if (_confirming) return; _confirming = true;
        bool isEdit = _editingEntryCard != null;
        string type = _selectedEntryType;
        if (string.IsNullOrWhiteSpace(type)) { _confirming = false; return; }

        string name; string keyInfo = ""; var fields = new List<(string, string, bool)>();

        if (type == "自定义")
        {
            name = CustomNameTextBox.Text.Trim();
            if (string.IsNullOrWhiteSpace(name)) { _confirming = false; FlashTextBox(CustomNameTextBox); return; }
            if (string.IsNullOrWhiteSpace(CustomInfoTextBox_0.Text)) { _confirming = false; FlashTextBox(CustomInfoTextBox_0); return; }
            fields.Add(("信息", CustomInfoTextBox_0.Text.Trim(), true));
            foreach (var child in CustomInfoDynamicPanel.Children)
                if (child is TextBox tb && !string.IsNullOrWhiteSpace(tb.Text))
                    fields.Add(("信息", tb.Text.Trim(), true));



            var origTotpField = _editOrigFields.FirstOrDefault(f => f.Item1 == "TOTP");
            if (!string.IsNullOrEmpty(origTotpField.Item2) && !fields.Any(f => f.Item1 == "TOTP"))
                fields.Add(("TOTP", origTotpField.Item2, false));
        }
        else
        {
            name = EntryNameTextBox.Text.Trim();
            if (string.IsNullOrWhiteSpace(name)) { _confirming = false; FlashTextBox(EntryNameTextBox); return; }
            switch (type)
            {
                case "邮箱":
                    if (string.IsNullOrWhiteSpace(FormField2TextBox.Text)) { _confirming = false; FlashTextBox(FormField2TextBox); return; }
                    fields.Add(("邮箱地址", FormField2TextBox.Text.Trim(), true));
                    if (!string.IsNullOrWhiteSpace(FormField3TextBox.Text)) fields.Add(("邮箱密码", FormField3TextBox.Text.Trim(), true));
                    if (!string.IsNullOrWhiteSpace(FormField4TextBox.Text)) fields.Add(("备注", FormField4TextBox.Text.Trim(), true));
                    break;
                case "账户":
                    if (string.IsNullOrWhiteSpace(FormField2TextBox.Text)) { _confirming = false; FlashTextBox(FormField2TextBox); return; }
                    fields.Add(("账号", FormField2TextBox.Text.Trim(), true));
                    if (!string.IsNullOrWhiteSpace(FormField3TextBox.Text)) fields.Add(("密码", FormField3TextBox.Text.Trim(), true));
                    if (!string.IsNullOrWhiteSpace(FormField4TextBox.Text)) fields.Add(("网址", FormField4TextBox.Text.Trim(), false));
                    if (!string.IsNullOrWhiteSpace(FormField5TextBox.Text)) fields.Add(("备注", FormField5TextBox.Text.Trim(), true));
                    break;
                case "网站":
                    if (string.IsNullOrWhiteSpace(FormField2TextBox.Text)) { _confirming = false; FlashTextBox(FormField2TextBox); return; }
                    fields.Add(("网址", FormField2TextBox.Text.Trim(), true));
                    if (!string.IsNullOrWhiteSpace(FormField3TextBox.Text)) fields.Add(("账号", FormField3TextBox.Text.Trim(), true));
                    if (!string.IsNullOrWhiteSpace(FormField4TextBox.Text)) fields.Add(("密码", FormField4TextBox.Text.Trim(), true));
                    if (!string.IsNullOrWhiteSpace(FormField5TextBox.Text)) fields.Add(("备注", FormField5TextBox.Text.Trim(), true));
                    break;
                case "银行卡":
                    if (string.IsNullOrWhiteSpace(FormField2TextBox.Text)) { _confirming = false; FlashTextBox(FormField2TextBox); return; }
                    fields.Add(("卡号", FormField2TextBox.Text.Trim(), true));
                    if (!string.IsNullOrWhiteSpace(FormField3TextBox.Text)) fields.Add(("持卡人", FormField3TextBox.Text.Trim(), false));
                    if (!string.IsNullOrWhiteSpace(FormField4TextBox.Text)) fields.Add(("有效期", FormField4TextBox.Text.Trim(), false));
                    if (!string.IsNullOrWhiteSpace(FormField5TextBox.Text)) fields.Add(("CVV", FormField5TextBox.Text.Trim(), true));
                    if (!string.IsNullOrWhiteSpace(FormField6TextBox.Text)) fields.Add(("密码", FormField6TextBox.Text.Trim(), true));
                    if (!string.IsNullOrWhiteSpace(FormField7TextBox.Text)) fields.Add(("备注", FormField7TextBox.Text.Trim(), true));
                    break;
                case "WiFi":
                    if (string.IsNullOrWhiteSpace(FormField2TextBox.Text)) { _confirming = false; FlashTextBox(FormField2TextBox); return; }
                    fields.Add(("网络名", FormField2TextBox.Text.Trim(), true));
                    if (!string.IsNullOrWhiteSpace(FormField3TextBox.Text)) fields.Add(("密码", FormField3TextBox.Text.Trim(), true));
                    if (!string.IsNullOrWhiteSpace(FormField4TextBox.Text)) fields.Add(("备注", FormField4TextBox.Text.Trim(), true));
                    break;
                case "证件":
                    if (string.IsNullOrWhiteSpace(FormField2TextBox.Text)) { _confirming = false; FlashTextBox(FormField2TextBox); return; }
                    fields.Add(("证件号", FormField2TextBox.Text.Trim(), true));
                    if (!string.IsNullOrWhiteSpace(FormField3TextBox.Text)) fields.Add(("姓名", FormField3TextBox.Text.Trim(), false));
                    if (!string.IsNullOrWhiteSpace(FormField4TextBox.Text)) fields.Add(("签发机构", FormField4TextBox.Text.Trim(), false));
                    if (!string.IsNullOrWhiteSpace(FormField5TextBox.Text)) fields.Add(("有效期", FormField5TextBox.Text.Trim(), false));
                    if (!string.IsNullOrWhiteSpace(FormField6TextBox.Text)) fields.Add(("备注", FormField6TextBox.Text.Trim(), true));
                    break;
                case "API Key":
                    var apiKeyVal = FormField2TextBox.Text.Trim();
                    if (string.IsNullOrWhiteSpace(apiKeyVal)) { _confirming = false; FlashTextBox(FormField2TextBox); return; }
                    var apiUrlVal = FormField3TextBox.Text.Trim();
                    if (string.IsNullOrWhiteSpace(apiUrlVal)) { _confirming = false; FlashTextBox(FormField3TextBox); return; }
                    fields.Add(("API Key", apiKeyVal, true));
                    fields.Add(("URL", apiUrlVal, true));
                    if (!string.IsNullOrWhiteSpace(FormField4TextBox.Text)) fields.Add(("模型 ID", FormField4TextBox.Text.Trim(), true));
                    if (!string.IsNullOrWhiteSpace(FormField5TextBox.Text)) fields.Add(("备注", FormField5TextBox.Text.Trim(), true));
                    break;
            }
        }






        keyInfo = fields.FirstOrDefault(f => f.Item1 == MemoFieldMask.KeyInfoLabelFor(type)).Item2 ?? "";




        var totpVal = TotpTextBox.Text.Trim();
        if (!string.IsNullOrEmpty(totpVal) && !fields.Any(f => f.Item1 == "TOTP")) fields.Add(("TOTP", totpVal, true));


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
            IconKey = entryIconKey,
            WorkspaceId = App.CurrentWorkspaceId
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
            int standaloneIdx = _standaloneEntries.IndexOf(old);
            _standaloneEntries.Remove(old);
            _entryPinIcons.Remove(old);
            _entryStarIcons.Remove(old);
            _entryData.Remove(old);
            if (_entryExpand.TryGetValue(old, out var eo)) { eo.Cts?.Cancel(); _entryExpand.Remove(old); }
            _maskedFieldTexts.Remove(old);
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
            if (_pinnedEntryCards.Remove(old)) { _pinnedEntryCards.Add(card); if (_entryPinIcons.TryGetValue(card, out var ep)) ep.Visibility = Visibility.Visible; }
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

                int insIdx = RegionPinnedEntryCard(entryStack) is Border rp
                    ? entryStack.Children.IndexOf(rp) + 1 : 0;
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

            if (RegionPinnedEntryCard(GroupsContainer) is Border topPinned)
            {
                int pe = GroupsContainer.Children.IndexOf(topPinned) + 1;
                if (pe > insIdx) insIdx = pe;
            }
            if (_uncategorizedCard != null && GroupsContainer.Children.Contains(_uncategorizedCard))
            {
                int uc = GroupsContainer.Children.IndexOf(_uncategorizedCard);
                if (insIdx > uc) insIdx = uc;
            }
            GroupsContainer.Children.Insert(insIdx, card);
            _standaloneEntries.Insert(0, card);

            App.Store?.Database.MemoEntries.Add(entryEntity);
            _entryIds[card] = entryEntity.Id;
            _entryCreatedAt[card] = entryEntity.CreatedAt;
        }
        HintText.Visibility = Visibility.Collapsed;
        App.PlayCardEntrance(card);
        if (_editingEntryCard == null) SyncUncategorizedCard();
        PersistAll();
        ApplyCardFilters();
        App.ShowToast(App.GetString(isEdit ? "Common_Toast_Modified" : "Common_Toast_Created"));

        var sb = new Storyboard(); var sx = new DoubleAnimation { To = 0.95, Duration = TimeSpan.FromMilliseconds(100) }; Storyboard.SetTarget(sx, NewEntryConfirmButtonTransform); Storyboard.SetTargetProperty(sx, "ScaleX"); sb.Children.Add(sx);
        var sy = new DoubleAnimation { To = 0.95, Duration = TimeSpan.FromMilliseconds(100) }; Storyboard.SetTarget(sy, NewEntryConfirmButtonTransform); Storyboard.SetTargetProperty(sy, "ScaleY"); sb.Children.Add(sy);
        sb.Begin();
        sb.Completed += (s, ev) => { var sb2 = new Storyboard(); var sx2 = new DoubleAnimation { To = 1.0, Duration = TimeSpan.FromMilliseconds(200) }; Storyboard.SetTarget(sx2, NewEntryConfirmButtonTransform); Storyboard.SetTargetProperty(sx2, "ScaleX"); sb2.Children.Add(sx2); var sy2 = new DoubleAnimation { To = 1.0, Duration = TimeSpan.FromMilliseconds(200) }; Storyboard.SetTarget(sy2, NewEntryConfirmButtonTransform); Storyboard.SetTargetProperty(sy2, "ScaleY"); sb2.Children.Add(sy2); sb2.Completed += (s2, e2) => CloseNewEntryDialog(); sb2.Begin(); };
    }


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







    public void QuickCaptureMemo(string text)
    {
        var name = text.Trim(); if (name.Length == 0) return;
        var iconKey = GetRandomIconKey();
        var fields = new List<(string, string, bool)>();
        var entryEntity = new MemoEntry
        {
            Type = "自定义",
            Name = name,
            KeyInfo = "",
            Fields = new List<EntryField>(),
            CreatedAt = DateTime.Now,
            IconKey = iconKey,
            WorkspaceId = App.CurrentWorkspaceId
        };
        if (!_storeLoaded)
        {
            App.Store?.Database.MemoEntries.Add(entryEntity);
            _ = App.Store?.SaveAsync();
            App.ShowToast(App.GetString("Common_Toast_Created"));
            return;
        }
        var card = CreateEntryCard("自定义", name, "", fields, iconKey);
        _entryIconKeys[card] = iconKey;

        int insIdx = 0;
        if (_pinnedGroupCard != null && GroupsContainer.Children.Contains(_pinnedGroupCard))
            insIdx = GroupsContainer.Children.IndexOf(_pinnedGroupCard) + 1;

        if (RegionPinnedEntryCard(GroupsContainer) is Border topPinned)
        {
            int pe = GroupsContainer.Children.IndexOf(topPinned) + 1;
            if (pe > insIdx) insIdx = pe;
        }
        if (_uncategorizedCard != null && GroupsContainer.Children.Contains(_uncategorizedCard))
        {
            int uc = GroupsContainer.Children.IndexOf(_uncategorizedCard);
            if (insIdx > uc) insIdx = uc;
        }
        GroupsContainer.Children.Insert(insIdx, card);
        _standaloneEntries.Insert(0, card);
        App.Store?.Database.MemoEntries.Add(entryEntity);
        _entryIds[card] = entryEntity.Id;
        _entryCreatedAt[card] = entryEntity.CreatedAt;
        HintText.Visibility = Visibility.Collapsed;
        App.PlayCardEntrance(card);
        SyncUncategorizedCard();
        PersistAll();
        ApplyCardFilters();
        App.ShowToast(App.GetString("Common_Toast_Created"));
    }

    public void OpenNewEntryDialog()
    {
        _confirming = false;
        NewEntryDialogHideAnimation.Stop();
        NewEntryDialogShowAnimation.Stop();
        NewEntryDialogTransform.ScaleX = 0.94; NewEntryDialogTransform.ScaleY = 0.94; NewEntryDialogTransform.TranslateY = 24;
        NewEntryDialog.Opacity = 0; NewEntryDialogScrim.Opacity = 0;
        NewEntryDialogTitle.Text = _editingEntryCard != null ? App.GetString("Memo_Entry_EditTitle") : App.GetString("Memo_Entry_NewTitle");
        EntryTypeSection.Visibility = _editingEntryCard != null ? Visibility.Collapsed : Visibility.Visible;
        ResetEntryTypeForms();
        NewEntryDialogOverlay.Visibility = Visibility.Visible;
        DialogDepth.VeilShow();
        Motion.StaggerReset(NewEntryDialog);
        Motion.StaggerWire(NewEntryDialogShowAnimation, NewEntryDialog);
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



        var parent = card.Parent as Panel;
        var oldPinned = parent != null ? RegionPinnedEntryCard(parent) : null;
        if (oldPinned != null && !ReferenceEquals(oldPinned, card) && _entryPinIcons.ContainsKey(oldPinned))
            _entryPinIcons[oldPinned].Visibility = Visibility.Collapsed;
        if (_entryPinIcons.ContainsKey(card)) _entryPinIcons[card].Visibility = Visibility.Visible;

        if (parent != null)
        {
            parent.Children.Remove(card);
            parent.Children.Insert(0, card);
        }



        if (_standaloneEntries.Contains(card))
        {
            _standaloneEntries.Remove(card);
            _standaloneEntries.Insert(0, card);
        }

        var db = App.Store?.Database;
        if (db != null)
        {
            if (oldPinned != null && !ReferenceEquals(oldPinned, card) && _entryIds.TryGetValue(oldPinned, out var oldId))
            {
                var oe = db.MemoEntries.FirstOrDefault(x => x.Id == oldId);
                if (oe != null) oe.IsPinned = false;
                _pinnedEntryCards.Remove(oldPinned);
            }
            if (_entryIds.TryGetValue(card, out var newId))
            {
                var ne = db.MemoEntries.FirstOrDefault(x => x.Id == newId);
                if (ne != null) ne.IsPinned = true;
            }
        }
        _pinnedEntryCards.Add(card);
        PersistAll();
    }

    private void UnpinEntryCard(Border card)
    {
        if (_entryPinIcons.ContainsKey(card)) _entryPinIcons[card].Visibility = Visibility.Collapsed;
        _pinnedEntryCards.Remove(card);
        if (_entryIds.TryGetValue(card, out var uid))
        {
            var me = App.Store?.Database.MemoEntries.FirstOrDefault(x => x.Id == uid);
            if (me != null) me.IsPinned = false;
        }
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
        EditGroupDialogTransform.ScaleX = 0.94; EditGroupDialogTransform.ScaleY = 0.94; EditGroupDialogTransform.TranslateY = 24;
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
        _deleteConfirming = false;

        DeleteConfirmMessageText.Text = isGroup
            ? App.GetString("Memo_Group_Delete_ConfirmTip")
            : App.GetString("Memo_Delete_ConfirmTip");

        DeleteConfirmTitleText.Foreground = isGroup
            ? App.GetBrush("AppDangerTextBrush")
            : App.GetBrush("AppPrimaryButtonBrush");

        DeleteConfirmMessageText.Foreground = isGroup
            ? App.GetBrush("AppDangerTextBrush")
            : App.GetBrush("AppTextPrimaryBrush");
        DeleteConfirmButton.Visibility = isGroup ? Visibility.Collapsed : Visibility.Visible;
        DeleteConfirmRedButton.Visibility = isGroup ? Visibility.Visible : Visibility.Collapsed;
        DeleteConfirmDialogTransform.ScaleX = 0.94; DeleteConfirmDialogTransform.ScaleY = 0.94; DeleteConfirmDialogTransform.TranslateY = 24;
        DeleteConfirmDialog.Opacity = 0; DeleteConfirmScrim.Opacity = 0;
        DeleteConfirmOverlay.Visibility = Visibility.Visible;
        DialogDepth.VeilShow();
        DeleteConfirmShowAnimation.Begin();
    }

    private void CloseDeleteConfirmDialog() { DeleteConfirmHideAnimation.Completed -= OnDeleteConfirmHideCompleted; DeleteConfirmHideAnimation.Completed += OnDeleteConfirmHideCompleted; DialogDepth.VeilHide(); DeleteConfirmHideAnimation.Begin(); }
    private void OnDeleteConfirmHideCompleted(object? sender, object e) { DeleteConfirmHideAnimation.Completed -= OnDeleteConfirmHideCompleted; DeleteConfirmOverlay.Visibility = Visibility.Collapsed; _currentEntryCard = null!; _currentGroupCard = null!; _deleteConfirming = false; }
    private void DeleteConfirmCloseButton_Click(object sender, RoutedEventArgs e) => CloseDeleteConfirmDialog();
    private void DeleteCancelButton_Click(object sender, RoutedEventArgs e) => CloseDeleteConfirmDialog();
    private void DeleteConfirmScrim_Tapped(object sender, TappedRoutedEventArgs e) { if (ReferenceEquals(e.OriginalSource, DeleteConfirmScrim)) CloseDeleteConfirmDialog(); }

    private bool _deleteConfirming;

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
                        _maskedFieldTexts.Remove(delEntryCard);
                        _entryIconKeys.Remove(delEntryCard);
                        _entryPinIcons.Remove(delEntryCard);
                        _entryStarIcons.Remove(delEntryCard);
                        _starredEntries.Remove(delEntryCard);
                        _apiProtocols.Remove(delEntryCard);
                        if (_entryIds.Remove(delEntryCard, out var delEid))
                        {
                            var me = App.Store?.Database.MemoEntries.FirstOrDefault(x => x.Id == delEid);
                            if (me != null) { me.IsDeleted = true; me.DeletedAt = DateTime.Now; me.IsPinned = false; me.IsStarred = false; }
                        }
                        _entryCreatedAt.Remove(delEntryCard);
                        _pinnedEntryCards.Remove(delEntryCard);
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

                    App.PlayCardRemoval(parent!, delEntryCard, _dragging, () =>
                    {
                        _standaloneEntries.Remove(delEntryCard);
                        _entriesInGroup.Remove(delEntryCard);
                        _entryData.Remove(delEntryCard);
                        if (_entryExpand.TryGetValue(delEntryCard, out var ed)) { ed.Cts?.Cancel(); _entryExpand.Remove(delEntryCard); }
                        _maskedFieldTexts.Remove(delEntryCard);
                        _entryIconKeys.Remove(delEntryCard);
                        _entryPinIcons.Remove(delEntryCard);
                        _entryStarIcons.Remove(delEntryCard);
                        _starredEntries.Remove(delEntryCard);
                        _apiProtocols.Remove(delEntryCard);
                        if (_entryIds.Remove(delEntryCard, out var delEid))
                        {
                            var me = App.Store?.Database.MemoEntries.FirstOrDefault(x => x.Id == delEid);
                            if (me != null) { me.IsDeleted = true; me.DeletedAt = DateTime.Now; me.IsPinned = false; me.IsStarred = false; }
                        }
                        _entryCreatedAt.Remove(delEntryCard);
                        _pinnedEntryCards.Remove(delEntryCard);
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
                            StripEntryPersonalMarks(eb);
                        }
                        GroupsContainer.Children.Insert(idx++, entry);
                    }
                }
                var delGroupCard = _currentGroupCard;

                App.PlayCardRemoval(GroupsContainer, delGroupCard, _dragging, () =>
                {
                    _groupNameTexts.Remove(delGroupCard);
                    _groupData.Remove(delGroupCard);

                    if (_groupIds.Remove(delGroupCard, out var delGid))
                    {
                        App.Store?.Database.MemoGroups.RemoveAll(x => x.Id == delGid);

                        foreach (var me in App.Store?.Database.MemoEntries ?? new())
                            if (me.IsDeleted && me.GroupId == delGid) { me.GroupId = null; me.IsPinned = false; me.IsStarred = false; }
                    }
                    _groupCreatedAt.Remove(delGroupCard);
                    _pinIcons.Remove(delGroupCard);
                    _starIcons.Remove(delGroupCard);
                    _starredCards.Remove(delGroupCard);
                    _groupCountTexts.Remove(delGroupCard);
                    if (_pinnedGroupCard == delGroupCard) _pinnedGroupCard = null!;
                    _currentGroupCard = null!;
                    if (GroupsContainer.Children.Count == 0) FloatInHint();
                    SyncUncategorizedCard();
                    PersistAll();
                    ApplyCardFilters();
                    App.ShowToast(App.GetString("Common_Toast_Deleted"));
                    CloseDeleteConfirmDialog();
                });
            }
        };
        sb.Begin();
    }

    private void OnPageUnloaded(object sender, RoutedEventArgs e)
    {
        _totpTimer?.Stop();
        _apiCheckCts?.Cancel();
        _apiCheckCts?.Dispose();
        _apiCheckCts = null;
        _apiDiagCts?.Cancel();
        _apiDiagCts?.Dispose();
        _apiDiagCts = null;
        _relayProbeCts?.Cancel();
        _relayProbeCts?.Dispose();
        _relayProbeCts = null;





        if (_renderInProgress) { _renderCts?.Cancel(); App.MainWindow?.RetireTabPage(typeof(BasicMemoPage)); }
        StopRelayProbePulse();
        foreach (var cts in _flashCtsMap.Values) { cts.Cancel(); cts.Dispose(); }
        _flashCtsMap.Clear();

        foreach (var kv in _flashOriginalBgs) { try { kv.Key.Background = kv.Value; } catch { } }
        _flashOriginalBgs.Clear();

        NewGroupDialogOverlay.Visibility = Visibility.Collapsed;
        NewEntryDialogOverlay.Visibility = Visibility.Collapsed;
        GenerateOverlay.Visibility = Visibility.Collapsed;
        _genTargetSlot = null;
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
        if (GenerateOverlay.Visibility == Visibility.Visible) { CloseGenerateDialog(); _genTargetSlot = null; e.Handled = true; return; }
        if (NewEntryDialogOverlay.Visibility == Visibility.Visible) { CloseNewEntryDialog(); e.Handled = true; return; }
        if (EditGroupOverlay.Visibility == Visibility.Visible) { CloseEditGroupDialog(); e.Handled = true; return; }
        if (NewGroupDialogOverlay.Visibility == Visibility.Visible) { CloseNewGroupDialog(); e.Handled = true; return; }
    }










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



        ApiCheckStatusDetail.Visibility = string.IsNullOrEmpty(detail) ? Visibility.Collapsed : Visibility.Visible;
        ApiCheckStatusText.Foreground = kind switch
        {
            "success" => new SolidColorBrush(Color.FromArgb(0xFF, 0x4C, 0xAF, 0x50)),
            "fail" => new SolidColorBrush(warn ? Color.FromArgb(0xFF, 0xFF, 0xB3, 0x00) : ((SolidColorBrush)App.GetBrush("AppDangerTextBrush")).Color),
            _ => App.GetBrush("AppTextPrimaryBrush")
        };
        ApiCheckAdvancedButton.Visibility = kind == "fail" ? Visibility.Visible : Visibility.Collapsed;
    }

    private void ShowApiCheckOverlay()
    {
        ApiCheckDialogTransform.ScaleX = 0.94; ApiCheckDialogTransform.ScaleY = 0.94; ApiCheckDialogTransform.TranslateY = 24;
        ApiCheckDialog.Opacity = 0; ApiCheckScrim.Opacity = 0;
        ApiCheckOverlay.Visibility = Visibility.Visible;
        DialogDepth.VeilShow();
        var sb = new Storyboard();
        var si = new DoubleAnimation { To = 1, Duration = TimeSpan.FromMilliseconds(250) };
        Storyboard.SetTarget(si, ApiCheckScrim); Storyboard.SetTargetProperty(si, "Opacity"); sb.Children.Add(si);
        var di = new DoubleAnimation { To = 1, Duration = TimeSpan.FromMilliseconds(300) };
        Storyboard.SetTarget(di, ApiCheckDialog); Storyboard.SetTargetProperty(di, "Opacity"); sb.Children.Add(di);
        Motion.AddDialogShowTransform(sb, ApiCheckDialogTransform);
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
        Motion.AddDialogHideTransform(sb, ApiCheckDialogTransform);
        sb.Completed -= OnApiCheckHideCompleted;
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
        System.Threading.CancellationTokenSource? myCts = null;
        try
        {
            _apiCheckCts?.Cancel();
            _apiCheckCts?.Dispose();
            _apiCheckCts = myCts = new System.Threading.CancellationTokenSource();
            var ct = myCts.Token;

            App.ShowToast(App.GetString("NetActivity_Active") + " " + Novara.Services.NetworkActivityService.ExtractHost(url));
            var report = await Novara.Services.ApiProbeService.ProbeAsync(url, key, ct);
            if (myCts.IsCancellationRequested) return;
            if (report.Status == Novara.Services.ApiProbeStatus.Success)
            {


                if (report.Protocol == "bearer") SaveApiProtocol("Bearer");
                else if (report.Protocol == "raw") SaveApiProtocol("NoBearer");
            }
            ApplyProbeReport(report);
        }
        catch (System.OperationCanceledException) when (myCts?.IsCancellationRequested == true)
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


        var detail = new System.Text.StringBuilder();
        if (!string.IsNullOrEmpty(report.Detail)) detail.Append(report.Detail).Append('\n');

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
        System.Threading.CancellationTokenSource? myCts = null;
        try
        {
            _apiDiagCts?.Cancel();
            _apiDiagCts?.Dispose();

            myCts = new System.Threading.CancellationTokenSource(TimeSpan.FromSeconds(180));
            _apiDiagCts = myCts;
            var ct = myCts.Token;

            App.ShowToast(App.GetString("NetActivity_Active") + " " + Novara.Services.NetworkActivityService.ExtractHost(url));
            var report = await Novara.Services.ApiDiagnoseService.DiagnoseAsync(url, key,model, ct);
            if (myCts.IsCancellationRequested)
            {


                SetApiDiagState("fail", App.GetString("Memo_Api_ProbeFail_Title"), App.GetString("Memo_ApiDiag_Timeout"), true);
            }
            else ApplyDiagReport(report);
        }
        catch (System.OperationCanceledException) when (myCts != null && myCts.IsCancellationRequested)
        {

            SetApiDiagState("fail", App.GetString("Memo_Api_ProbeFail_Title"), App.GetString("Memo_ApiDiag_Timeout"), true);
        }
        catch (System.OperationCanceledException)
        {

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
                "fail" => ((SolidColorBrush)App.GetBrush("AppDangerTextBrush")).Color,
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
            "fail" => new SolidColorBrush(warn ? Color.FromArgb(0xFF, 0xFF, 0xB3, 0x00) : ((SolidColorBrush)App.GetBrush("AppDangerTextBrush")).Color),
            _ => App.GetBrush("AppTextPrimaryBrush")
        };
    }

    private void ShowApiDiagOverlay()
    {
        ApiDiagDialogTransform.ScaleX = 0.94; ApiDiagDialogTransform.ScaleY = 0.94; ApiDiagDialogTransform.TranslateY = 24;
        ApiDiagDialog.Opacity = 0; ApiDiagScrim.Opacity = 0;
        ApiDiagOverlay.Visibility = Visibility.Visible;
        DialogDepth.VeilShow();
        var sb = new Storyboard();
        var si = new DoubleAnimation { To = 1, Duration = TimeSpan.FromMilliseconds(250) };
        Storyboard.SetTarget(si, ApiDiagScrim); Storyboard.SetTargetProperty(si, "Opacity"); sb.Children.Add(si);
        var di = new DoubleAnimation { To = 1, Duration = TimeSpan.FromMilliseconds(300) };
        Storyboard.SetTarget(di, ApiDiagDialog); Storyboard.SetTargetProperty(di, "Opacity"); sb.Children.Add(di);
        Motion.AddDialogShowTransform(sb, ApiDiagDialogTransform);
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
        Motion.AddDialogHideTransform(sb, ApiDiagDialogTransform);
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
        ApiConfirmDialogTransform.ScaleX = 0.94; ApiConfirmDialogTransform.ScaleY = 0.94; ApiConfirmDialogTransform.TranslateY = 24;
        ApiConfirmDialog.Opacity = 0; ApiConfirmScrim.Opacity = 0;
        ApiConfirmOverlay.Visibility = Visibility.Visible;
        DialogDepth.VeilShow();
        var sb = new Storyboard();
        var si = new DoubleAnimation { To = 1, Duration = TimeSpan.FromMilliseconds(250) };
        Storyboard.SetTarget(si, ApiConfirmScrim); Storyboard.SetTargetProperty(si, "Opacity"); sb.Children.Add(si);
        var di = new DoubleAnimation { To = 1, Duration = TimeSpan.FromMilliseconds(300) };
        Storyboard.SetTarget(di, ApiConfirmDialog); Storyboard.SetTargetProperty(di, "Opacity"); sb.Children.Add(di);
        Motion.AddDialogShowTransform(sb, ApiConfirmDialogTransform);
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
        Motion.AddDialogHideTransform(sb, ApiConfirmDialogTransform);
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

            App.ShowToast(App.GetString("NetActivity_Active") + " " + Novara.Services.NetworkActivityService.ExtractHost(url));
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
                Novara.Services.ProbeVerdict.Fail => (((SolidColorBrush)App.GetBrush("AppDangerTextBrush")).Color, "\uE783"),
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
            "high-risk" => ((SolidColorBrush)App.GetBrush("AppDangerTextBrush")).Color,
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
            ? App.GetBrush("AppDangerTextBrush")
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
        DialogUi.ClampDialogHeight(RelayProbeDialog, viewport);

        RelayProbeDialogTransform.ScaleX = 0.94; RelayProbeDialogTransform.ScaleY = 0.94; RelayProbeDialogTransform.TranslateY = 24;
        RelayProbeDialog.Opacity = 0; RelayProbeScrim.Opacity = 0;
        RelayProbeOverlay.Visibility = Visibility.Visible;
        DialogDepth.VeilShow();
        var sb = new Storyboard();
        var si = new DoubleAnimation { To = 1, Duration = TimeSpan.FromMilliseconds(250) };
        Storyboard.SetTarget(si, RelayProbeScrim); Storyboard.SetTargetProperty(si, "Opacity"); sb.Children.Add(si);
        var di = new DoubleAnimation { To = 1, Duration = TimeSpan.FromMilliseconds(300) };
        Storyboard.SetTarget(di, RelayProbeDialog); Storyboard.SetTargetProperty(di, "Opacity"); sb.Children.Add(di);
        Motion.AddDialogShowTransform(sb, RelayProbeDialogTransform);
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
        Motion.AddDialogHideTransform(sb, RelayProbeDialogTransform);
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

    private void RelayProbeLoadDatasetButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var path = FilePicker.PickFile("*.json");
            if (path == null) return;

            var json = System.IO.File.ReadAllText(path);
            if (!Novara.Services.ProbeDataSetLoader.TryParse(json, out _, out var error))
            {
                App.ShowToast(App.GetString("Memo_RelayProbe_LoadDataset") + "：" + error, ToastTone.Error);
                return;
            }
            var dest = System.IO.Path.Combine(System.AppContext.BaseDirectory, Novara.Services.ProbeDataSetLoader.DefaultFileName);
            System.IO.File.Copy(path, dest, overwrite: true);
            App.ShowToast(App.GetString("Memo_RelayProbe_LoadDataset"));
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"加载探针数据集失败: {ex}");

            try { App.ShowToast(App.GetString("Memo_RelayProbe_LoadDataset") + "：" + ex.Message, ToastTone.Error); }
            catch { }
        }
    }

    private void ShowApiProtocolDialog()
    {
        _selectedApiProtocol = _apiCheckCard != null && _apiProtocols.TryGetValue(_apiCheckCard, out var p) ? p : "Bearer";
        UpdateApiProtocolButton();
        ApiProtocolDialogTransform.ScaleX = 0.94; ApiProtocolDialogTransform.ScaleY = 0.94; ApiProtocolDialogTransform.TranslateY = 24;
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
            sb.Children.Add(Motion.Eased(val, Motion.DlgIn, Motion.Decelerate, ApiProtocolDialogTransform, prop));
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
        Motion.AddDialogHideTransform(sb, ApiProtocolDialogTransform);
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


            if (ReferenceEquals(card, _pinnedGroupCard) || _pinnedEntryCards.Contains(card)) return;
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
        _dropIndex = _dragOriginIndex = _dragContainer != null ? CountVisibleBeforeIn(card, _dragContainer) : 0;
        _dropCache = null; _dropCacheDirty = true;

        App.StopCardEntrance(card);
        card.BorderBrush = new SolidColorBrush(PaperTheme.BrandColor);
        card.BorderThickness = new Thickness(2);
        card.Opacity = 0.35;


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


        if (_dropCache == null || _dropCacheDirty) RebuildDropCache();
        if (_dropCache == null) return 0;
        int count = 0;
        foreach (var (_, top, half) in _dropCache)
        {
            if (ptInList.Y < top + half) break;
            count++;
        }
        return count;
    }

    private void RebuildDropCache()
    {
        _dropCache = new List<(FrameworkElement fe, double top, double half)>();
        if (_dragContainer == null) { _dropCacheDirty = false; return; }
        foreach (var child in _dragContainer.Children)
        {
            if (ReferenceEquals(child, _dropIndicator)) continue;


            if (child is FrameworkElement fe && fe.Visibility == Visibility.Visible)
            {
                var top = fe.TransformToVisual(_dragContainer).TransformPoint(new Point(0, 0)).Y;
                _dropCache.Add((fe, top, fe.ActualHeight / 2));
            }
        }
        _dropCacheDirty = false;
    }

    private void UpdateDropIndicator(int index)
    {
        if (_dragContainer == null) return;


        int minIndex = 0;
        if (ReferenceEquals(_dragContainer, GroupsContainer))
        {


            bool topPinned =
                (_pinnedGroupCard != null && _dragContainer.Children.Contains(_pinnedGroupCard) && _pinnedGroupCard.Visibility == Visibility.Visible) ||
                (RegionPinnedEntryCard(_dragContainer) is Border tp && tp.Visibility == Visibility.Visible);
            if (topPinned) minIndex = 1;
        }
        else
        {

            if (RegionPinnedEntryCard(_dragContainer) is Border rp && rp.Visibility == Visibility.Visible) minIndex = 1;
        }
        index = Math.Max(index, minIndex);



        if (ReferenceEquals(_dragContainer, GroupsContainer)
            && _dragCard != null && _groupNameTexts.ContainsKey(_dragCard)
            && _uncategorizedCard != null && GroupsContainer.Children.Contains(_uncategorizedCard))
        {



            int uncatIdx = CountVisibleBeforeIn(_uncategorizedCard, GroupsContainer);
            index = Math.Min(index, uncatIdx);
        }


        if (index == _dragOriginIndex || index == _dragOriginIndex + 1)
        {
            if (_dropIndicator != null) { _dragContainer.Children.Remove(_dropIndicator); _dropIndicator = null; }
            _dropIndex = _dragOriginIndex;
            _dropCacheDirty = true;
            return;
        }

        if (_dropIndex == index && _dropIndicator != null) return;
        if (_dropIndicator != null) _dragContainer.Children.Remove(_dropIndicator);
        else _dropIndicator = CreateDropIndicator();
        _dragContainer.Children.Insert(index, _dropIndicator);
        _dropIndex = index;
        _dropCacheDirty = true;
    }

    private Border CreateDropIndicator()
    {
        var b = new Border { Height = 16, Background = new SolidColorBrush(Color.FromArgb(0, 0, 0, 0)) };
        b.Child = new Border
        {
            Height = 2,
            CornerRadius = new CornerRadius(1),
            Background = new SolidColorBrush(PaperTheme.BrandColor),
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
        _dropCacheDirty = true;
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
        _dropCache = null;
        StopAutoScroll();

        card.BorderBrush = new SolidColorBrush(App.GetBrush("AppBorderBrush").Color);
        card.BorderThickness = new Thickness(1);
        card.Opacity = 1;

        if (container == null) return;


        int curVisible = CountVisibleBeforeIn(card, container);
        int dst = dropIndex > curVisible ? dropIndex - 1 : dropIndex;
        if (dst != curVisible)
        {
            container.Children.Remove(card);
            container.Children.Insert(PhysicalIndexForVisibleSlotIn(dst, container), card);
        }

        PersistAll();
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
}
