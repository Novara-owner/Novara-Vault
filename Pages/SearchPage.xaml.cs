using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Novara.Models;
using Novara.Services;
using Windows.UI;
using Windows.UI.Text;

namespace Novara.Pages;









public sealed partial class SearchPage : Page
{
    private readonly List<SearchItem> _items = new();
    private readonly List<SearchItem> _index = new();
    private int _searchGen;
    private int _indexGen;
    private bool _indexBuilding;
    private string? _pendingQuery;
    private System.Threading.CancellationTokenSource? _renderCts;
    private Border? _cancelSearchBtn;
    private Button? _filterButton;
    private PathIcon? _filterButtonIcon;
    private TextBlock? _filterButtonText;
    private string _currentFilter = "all";
    private string _currentKey = "";


    private sealed record PaletteCmd(string Action, string LabelKey, string IconPath);
    private static readonly PaletteCmd[] PaletteCommands =
    {
        new("new_memo", "Memo_Entry_NewTitle", IconData.NewEntry[0] + " " + IconData.NewEntry[1]),
        new("new_todo", "Menu_New_Todo", IconData.Todo[0] + " " + IconData.Todo[1]),
        new("new_note", "Menu_New_Note", IconData.Note[0] + " " + IconData.Note[1]),
        new("new_diary", "Menu_New_Diary", IconData.NewDiary),
        new("new_document", "Diary_New_Document", IconData.Document),
        new("open_settings", "Setting_Title_Page", string.Join(" ", IconData.Settings)),
        new("open_trash", "Trash_Title", IconData.Delete),

        new("open_seal", "Menu_Seal", string.Join(" ", IconData.SealSpace)),
        new("lock_now", "Tray_LockNow", IconData.Lock),
    };
    private readonly List<PaletteCmd> _cmdMatched = new();
    private int _cmdIndex;
    private bool _wasPaletteMode;
    private bool PaletteMode => SearchBox.Text?.StartsWith(">") == true;

    private static readonly string SearchGlyphPath = IconData.Search[0] + " " + IconData.Search[1];

    public SearchPage()
    {
        InitializeComponent();
        App.ApplyAutomationNames(this);
        Novara.Services.DialogDepth.AttachContainer((Grid)Content, autoVeil: true);
        BackPathIcon.Data = (Geometry)Microsoft.UI.Xaml.Markup.XamlBindingHelper.ConvertValue(typeof(Geometry), IconData.Back);
        SearchGlyphIcon.Data = (Geometry)Microsoft.UI.Xaml.Markup.XamlBindingHelper.ConvertValue(typeof(Geometry), SearchGlyphPath);
        BackButton.Click += (_, _) => App.MainWindow?.CloseSearchPage();




        SearchBox.TextChanged += (_, _) =>
        {
            UpdateCancelButtonVisibility();
            UpdateModeIcon();
            if (PaletteMode) { _wasPaletteMode = true; RenderCommands(); return; }


            if (_wasPaletteMode)
            {
                _wasPaletteMode = false;
                ResultPanel.Children.Clear();
                ShowBlankHint();
            }
            if (string.IsNullOrWhiteSpace(SearchBox.Text))
            {
                _pendingQuery = null;
                ShowBlankHint();
            }
        };

        SearchBox.KeyDown += (_, e) =>
        {
            if (!PaletteMode) return;
            int count = _cmdMatched.Count;
            if (count == 0) return;
            if (e.Key == Windows.System.VirtualKey.Down)
            {
                e.Handled = true;
                _cmdIndex = (_cmdIndex + 1) % count;
                RefreshCmdHighlights();
            }
            else if (e.Key == Windows.System.VirtualKey.Up)
            {
                e.Handled = true;
                _cmdIndex = (_cmdIndex - 1 + count) % count;
                RefreshCmdHighlights();
            }
        };
        SearchBox.KeyUp += (_, e) =>
        {
            if (e.Key != Windows.System.VirtualKey.Enter) return;
            if (PaletteMode) { ExecuteHighlightedCommand(); return; }
            RunSearch(SearchBox.Text?.Trim() ?? "");
        };

        CreateCancelButton();
        CreateFilterButton();

        KeyboardAcceleratorPlacementMode = KeyboardAcceleratorPlacementMode.Hidden;
        var esc = new KeyboardAccelerator { Key = Windows.System.VirtualKey.Escape };
        esc.Invoked += (_, _) => App.MainWindow?.CloseSearchPage();
        KeyboardAccelerators.Add(esc);
        ApplyTexts();
        Loaded += (_, _) =>
        {
            FocusSearch();
            ShowBlankHint();
        };
        Unloaded += (_, _) => { _searchGen++; DisposeRenderCts(); };
    }




    private void SearchBox_GotFocus(object sender, RoutedEventArgs e)
        => SearchCapsule.BorderBrush = App.GetBrush("AppPrimaryButtonBrush");

    private void SearchBox_LostFocus(object sender, RoutedEventArgs e)
        => SearchCapsule.BorderBrush = App.GetBrush("AppBorderBrush");


    private void CreateCancelButton()
    {
        _cancelSearchBtn = new Border
        {
            Width = 28, Height = 28,
            Background = new SolidColorBrush(Color.FromArgb(0, 0, 0, 0)),
            CornerRadius = new CornerRadius(14),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 96, 0),
            Visibility = Visibility.Collapsed,
        };
        _cancelSearchBtn.PointerEntered += (_, _) =>
            _cancelSearchBtn.Background = App.GetBrush("AppHoverBrush");
        _cancelSearchBtn.PointerExited += (_, _) =>
            _cancelSearchBtn.Background = new SolidColorBrush(Color.FromArgb(0, 0, 0, 0));
        _cancelSearchBtn.Tapped += (_, _) => ClearSearch();
        _cancelSearchBtn.Child = new Viewbox
        {
            Width = 14, Height = 14,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Stretch = Stretch.Uniform,
            Child = new PathIcon
            {
                Data = App.CreateGeometry(IconData.CancelSearchIcon),
                Foreground = App.GetBrush("IconForegroundBrush"),
            },
        };
        SearchCapsuleGrid.Children.Add(_cancelSearchBtn);
    }


    private void CreateFilterButton()
    {
        var white = new SolidColorBrush(Color.FromArgb(0xFF, 0xFF, 0xFF, 0xFF));
        _filterButton = new Button
        {
            Width = 84,
            Height = 36,
            Style = (Style)Application.Current.Resources["NovaraPrimaryButtonStyle"],
            CornerRadius = new CornerRadius(18),
            Padding = new Thickness(10, 0, 10, 0),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 4, 0),
        };
        var panel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        _filterButtonIcon = new PathIcon { Foreground = white };
        var filterIconBox = new Viewbox { Width = 16, Height = 16, Stretch = Stretch.Uniform, Child = _filterButtonIcon };
        _filterButtonText = new TextBlock { FontSize = 13, Foreground = white, VerticalAlignment = VerticalAlignment.Center };
        panel.Children.Add(filterIconBox);
        panel.Children.Add(_filterButtonText);
        _filterButton.Content = panel;
        _filterButton.Click += FilterButton_Click;
        SearchCapsuleGrid.Children.Add(_filterButton);
        UpdateFilterButton();
    }

    private void UpdateFilterButton()
    {
        if (_filterButtonIcon == null || _filterButtonText == null) return;
        _filterButtonIcon.Data = App.CreateGeometry(GetFilterIcon(_currentFilter));
        _filterButtonText.Text = GetFilterLabel(_currentFilter);
    }

    private static string GetFilterIcon(string filter) => filter switch
    {
        "memo" => IconData.NavMemo,
        "path" => IconData.NavFile,
        "plan" => IconData.NavPlan,
        "diary" => IconData.NavDiary,
        "document" => IconData.Document,
        _ => IconData.Mix
    };

    private static string GetFilterLabel(string filter) => filter switch
    {
        "memo" => App.GetString("Nav_Tab_Memo"),
        "path" => App.GetString("Nav_Tab_File"),
        "plan" => App.GetString("Nav_Tab_Plan"),
        "diary" => App.GetString("Diary_Filter_Diary"),
        "document" => App.GetString("Diary_Filter_Document"),
        _ => App.GetString("Diary_Filter_All")
    };



    private void UpdateModeIcon()
    {

        SearchGlyphIcon.Data = (Geometry)Microsoft.UI.Xaml.Markup.XamlBindingHelper.ConvertValue(typeof(Geometry),
            PaletteMode ? IconData.CmdPrompt : SearchGlyphPath);
    }

    private void RenderCommands()
    {
        _renderCts?.Cancel(); _renderCts = null;
        _cmdMatched.Clear();
        var filter = SearchBox.Text.Length > 1 ? SearchBox.Text.Substring(1).Trim() : "";
        foreach (var c in PaletteCommands)
        {
            if (filter.Length == 0) { _cmdMatched.Add(c); continue; }
            var label = App.GetString(c.LabelKey);
            if (label.StartsWith(filter, StringComparison.OrdinalIgnoreCase) || label.Contains(filter, StringComparison.OrdinalIgnoreCase))
                _cmdMatched.Add(c);
        }
        ResultPanel.Children.Clear();
        if (_cmdMatched.Count == 0)
        {
            _cmdIndex = 0;
            ShowHintText(App.GetString("Cmd_NoMatch"));
            return;
        }
        if (_cmdIndex >= _cmdMatched.Count) _cmdIndex = _cmdMatched.Count - 1;
        if (_cmdIndex < 0) _cmdIndex = 0;
        EmptyHintPanel.Visibility = Visibility.Collapsed;
        for (int i = 0; i < _cmdMatched.Count; i++)
            ResultPanel.Children.Add(BuildCommandCard(_cmdMatched[i], i));
        RefreshCmdHighlights();
    }

    private Border BuildCommandCard(PaletteCmd cmd, int index)
    {
        var card = new Border
        {
            Background = App.GetBrush("AppSurfaceOverlayBrush"),
            BorderBrush = App.GetBrush("AppBorderBrush"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(20, 14, 20, 14),
            Tag = index,
        };
        var panel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12, VerticalAlignment = VerticalAlignment.Center };
        panel.Children.Add(new Viewbox
        {
            Width = 20, Height = 20, Stretch = Stretch.Uniform, VerticalAlignment = VerticalAlignment.Center,
            Child = new PathIcon { Data = App.CreateGeometry(cmd.IconPath), Foreground = App.GetBrush("IconForegroundBrush") },
        });
        panel.Children.Add(new TextBlock
        {
            Text = App.GetString(cmd.LabelKey), FontSize = 14, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Foreground = App.GetBrush("AppTextPrimaryBrush"), VerticalAlignment = VerticalAlignment.Center,
        });
        card.Child = panel;


        card.IsTabStop = true;
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(card, App.GetString(cmd.LabelKey));
        card.KeyDown += (_, e) =>
        {
            if (e.Key is not (Windows.System.VirtualKey.Enter or Windows.System.VirtualKey.Space)) return;
            e.Handled = true;
            ExecuteCommandAt(index);
        };
        card.Tapped += (_, _) => ExecuteCommandAt(index);
        card.PointerEntered += (_, _) => { _cmdIndex = index; RefreshCmdHighlights(); };
        return card;
    }

    private void RefreshCmdHighlights()
    {
        for (int i = 0; i < ResultPanel.Children.Count; i++)
        {
            if (ResultPanel.Children[i] is Border b && b.Tag is int idx)
            {
                bool hot = idx == _cmdIndex;

                b.BorderBrush = hot ? App.GetBrush("AppPrimaryButtonBrush") : App.GetBrush("AppBorderBrush");
            }
        }
    }

    private void ExecuteHighlightedCommand()
    {
        if (_cmdMatched.Count == 0) return;
        ExecuteCommandAt(Math.Clamp(_cmdIndex, 0, _cmdMatched.Count - 1));
    }

    private void ExecuteCommandAt(int index)
    {
        if (index < 0 || index >= _cmdMatched.Count) return;
        App.MainWindow?.RunPaletteCommand(_cmdMatched[index].Action);
    }

    private bool MatchesFilter(SearchItem it) => _currentFilter switch
    {
        "memo" => it.Kind == "memo",
        "path" => it.Kind == "path",
        "plan" => it.Kind == "todo" || it.Kind == "note",
        "diary" => it.Kind == "diary" && it.Format != "markdown",
        "document" => it.Kind == "diary" && it.Format == "markdown",
        _ => true
    };

    private void FilterButton_Click(object sender, RoutedEventArgs e)
    {
        if (PaletteMode) return;
        var menu = new MenuFlyout { MenuFlyoutPresenterStyle = (Style)Application.Current.Resources["GlassMenuFlyoutPresenterStyle"] };
        var activeBrush = App.GetBrush("AppTextPrimaryBrush");
        var normalBrush = App.GetBrush("AppTextSecondaryBrush");
        var iconBrush = App.GetBrush("IconForegroundBrush");

        MenuFlyoutItem MakeItem(string label, string filter, string iconPath)
        {
            bool active = _currentFilter == filter;
            var item = new MenuFlyoutItem { Style = (Style)Application.Current.Resources["GlassMenuFlyoutItemStyle"], Text = label, Foreground = active ? activeBrush : normalBrush };
            item.Icon = active
                ? new FontIcon { Glyph = "\uE73E", FontSize = 12, Foreground = activeBrush }
                : new PathIcon { Data = App.CreateGeometry(iconPath), Foreground = iconBrush };
            item.Click += (_, _) =>
            {
                _currentFilter = filter;
                UpdateFilterButton();
                RefreshResults();
            };
            return item;
        }

        menu.Items.Add(MakeItem(App.GetString("Diary_Filter_All"), "all", IconData.Mix));
        menu.Items.Add(MakeItem(App.GetString("Nav_Tab_Memo"), "memo", IconData.NavMemo));
        menu.Items.Add(MakeItem(App.GetString("Nav_Tab_File"), "path", IconData.NavFile));
        menu.Items.Add(MakeItem(App.GetString("Nav_Tab_Plan"), "plan", IconData.NavPlan));
        menu.Items.Add(MakeItem(App.GetString("Diary_Filter_Diary"), "diary", IconData.NavDiary));
        menu.Items.Add(MakeItem(App.GetString("Diary_Filter_Document"), "document", IconData.Document));
        if (sender is Button btn)
            menu.ShowAt(btn, new Windows.Foundation.Point(0, btn.ActualHeight + 4));
    }

    private void RefreshResults()
    {


        if (string.IsNullOrWhiteSpace(SearchBox.Text)) return;
        if (_items.Count == 0) return;
        Render(_currentKey);
    }

    private void UpdateCancelButtonVisibility()
    {
        if (_cancelSearchBtn == null) return;
        var hasText = !string.IsNullOrWhiteSpace(SearchBox.Text);
        if (!hasText)
            _cancelSearchBtn.Background = new SolidColorBrush(Color.FromArgb(0, 0, 0, 0));
        _cancelSearchBtn.Visibility = hasText ? Visibility.Visible : Visibility.Collapsed;
    }

    private void ClearSearch() => SearchBox.Text = "";


    public void ResetSearch()
    {
        _pendingQuery = null;
        SearchBox.Text = "";


        UpdateCancelButtonVisibility();
        ResultPanel.Children.Clear();
        _items.Clear();

        _currentFilter = "all";
        _currentKey = "";
        _cmdIndex = 0;
        _cmdMatched.Clear();
        _wasPaletteMode = false;

        UpdateModeIcon();
        UpdateFilterButton();
        ShowBlankHint();
        RebuildIndex();
    }

    public void ApplyTexts()
    {
        SearchBox.PlaceholderText = App.GetString("Search_Placeholder");
    }

    public void FocusSearch()
    {
        FocusSink.Focus(FocusState.Programmatic);
        SearchBox.Focus(FocusState.Programmatic);
    }

    private sealed record MemoIndexSource(Guid Id, string Name, string Type, string KeyInfo, DateTime CreatedAt,
        (string Label, string Value)[] Fields);
    private sealed record PathIndexSource(Guid Id, string Name, string Path, string Note, DateTime CreatedAt);
    private sealed record TodoIndexSource(Guid Id, string Title, string MainText, string[] SubTexts, DateTime CreatedAt);
    private sealed record NoteIndexSource(Guid Id, string Title, string Content, DateTime CreatedAt);
    private sealed record DiaryIndexSource(string Id, string Title, string Content, DateTime CreatedAt, DateTime ModifiedAt, string? Format);
    private sealed record SearchIndexSource(List<MemoIndexSource> Memos, List<PathIndexSource> Paths,
        List<TodoIndexSource> Todos, List<NoteIndexSource> Notes, List<DiaryIndexSource> Diaries);



    private SearchIndexSource SnapshotIndexSource()
    {
        var db = App.Store?.Database;
        if (db == null) return new(new(), new(), new(), new(), new());
        return new(
            db.MemoEntries.Where(x => !x.IsDeleted).Select(e => new MemoIndexSource(e.Id, e.Name, e.Type, e.KeyInfo,
                e.CreatedAt, e.Fields.Where(f => f != null).Select(f => (f.Label, f.Value ?? "")).ToArray())).ToList(),
            db.PathBackupItems.Where(x => !x.IsDeleted)
                .Select(p => new PathIndexSource(p.Id, p.Name, p.Path, p.Note, p.CreatedAt)).ToList(),
            db.TodoCards.Where(x => !x.IsDeleted)
                .Select(t => new TodoIndexSource(t.Id, t.Title, t.MainText, t.SubTexts.Where(s => s != null).ToArray(), t.CreatedAt)).ToList(),
            db.NoteCards.Where(x => !x.IsDeleted)
                .Select(n => new NoteIndexSource(n.Id, n.Title, n.Content, n.CreatedAt)).ToList(),
            db.DiaryItems.Where(x => !x.IsDeleted)
                .Select(d => new DiaryIndexSource(d.Id, d.Title, d.Content, d.CreatedAt, d.ModifiedAt, d.Format)).ToList());
    }

    private static List<SearchItem> BuildIndex(SearchIndexSource src)
    {
        var index = new List<SearchItem>(src.Memos.Count + src.Paths.Count + src.Todos.Count + src.Notes.Count + src.Diaries.Count);
        foreach (var e in src.Memos)
        {






            var sub = MemoFieldMask.MaskedKeyInfo(e.Type, e.KeyInfo, e.Fields.Select(f => (f.Label, f.Value)));



            var searchable = new List<string> { e.Name };
            if (!MemoFieldMask.IsMaskedKeyInfo(e.Type)) searchable.Add(e.KeyInfo);
            foreach (var f in e.Fields)
            {
                searchable.Add(f.Label);
                if (!MemoFieldMask.IsMaskedLabel(f.Label)) searchable.Add(f.Value);
            }
            var text = string.Join(" ", searchable).ToLowerInvariant();
            index.Add(new SearchItem("memo", e.Id.ToString(), e.Name, sub, e.CreatedAt, "", text));
        }
        foreach (var p in src.Paths)
        {
            index.Add(new SearchItem("path", p.Id.ToString(), p.Name, p.Path, p.CreatedAt, "", (p.Name + " " + p.Path + " " + p.Note).ToLowerInvariant()));
        }
        foreach (var t in src.Todos)
        {
            index.Add(new SearchItem("todo", t.Id.ToString(), t.Title, t.MainText, t.CreatedAt, "", (t.Title + " " + t.MainText + " " + string.Join(" ", t.SubTexts)).ToLowerInvariant()));
        }
        foreach (var n in src.Notes)
        {
            index.Add(new SearchItem("note", n.Id.ToString(), n.Title, n.Content, n.CreatedAt, "", (n.Title + " " + n.Content).ToLowerInvariant()));
        }
        foreach (var d in src.Diaries)
        {
            string times = $"{App.GetString("Search_Created")} {d.CreatedAt:yyyy-MM-dd HH:mm}  ·  {App.GetString("Search_Modified")} {d.ModifiedAt:yyyy-MM-dd HH:mm}";


            var text = SearchableDiaryText(d.Content);
            if (text.Length > 200_000) text = text[..200_000];
            index.Add(new SearchItem("diary", d.Id, d.Title, times, d.CreatedAt, d.Format ?? "", (d.Title + " " + text).ToLowerInvariant()));
        }
        return index;
    }







    private void RebuildIndex()
    {
        var gen = ++_indexGen;
        _indexBuilding = true;
        var source = SnapshotIndexSource();
        _ = System.Threading.Tasks.Task.Run(() =>
        {
            List<SearchItem> built;
            try { built = BuildIndex(source); }
            catch (Exception ex)
            {
                CrashLogger.LogNote("SearchIndex", ex.GetType().Name);
                built = new List<SearchItem>();
            }
            DispatcherQueue.TryEnqueue(() =>
            {
                if (gen != _indexGen) return;
                _index.Clear();
                _index.AddRange(built);
                _indexBuilding = false;
                var pending = _pendingQuery;
                _pendingQuery = null;


                if (!string.IsNullOrWhiteSpace(pending) && IsLoaded
                    && string.Equals(SearchBox.Text?.Trim(), pending, StringComparison.Ordinal))
                    RunSearch(pending);
            });
        });
    }


    private static string SearchableDiaryText(string? content)
    {
        if (string.IsNullOrEmpty(content)) return "";



        var noData = System.Text.RegularExpressions.Regex.Replace(
            content, @"data:[^""'\s)]+", " ", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        var noTags = System.Text.RegularExpressions.Regex.Replace(noData, "<[^>]+>", " ");
        return System.Net.WebUtility.HtmlDecode(noTags);
    }

    private async void RunSearch(string q)
    {


        var generation = ++_searchGen;
        if (string.IsNullOrWhiteSpace(q)) { _pendingQuery = null; ShowBlankHint(); return; }

        if (_indexBuilding) { _pendingQuery = q; return; }
        string key = q.ToLowerInvariant();



        var source = _index.ToList();
        List<SearchItem> matched;
        try
        {
        matched = await System.Threading.Tasks.Task.Run(() =>
        {
            var list = new List<SearchItem>();
            foreach (var it in source)
                if (SearchFuzzy.MatchesQuery(it.SearchText, key))
                    list.Add(it);


            var sortWords = key.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            list.Sort((a, b) =>
            {

                var titleA = a.Title ?? "";
                var titleB = b.Title ?? "";
                bool ta = sortWords.Any(w => titleA.IndexOf(w, StringComparison.OrdinalIgnoreCase) >= 0);
                bool tb = sortWords.Any(w => titleB.IndexOf(w, StringComparison.OrdinalIgnoreCase) >= 0);
                if (ta != tb) return ta ? -1 : 1;
                return b.Time.CompareTo(a.Time);
            });
            return list;
        });
        }
        catch (Exception ex)
        {

            CrashLogger.LogNote("SearchRun", ex.GetType().Name);
            if (generation == _searchGen && IsLoaded) ShowBlankHint();
            return;
        }
        if (generation != _searchGen || !IsLoaded) return;

        _items.Clear();
        _items.AddRange(matched);
        _currentKey = key;
        Render(key);
    }




    private void DisposeRenderCts()
    {
        var old = _renderCts;
        _renderCts = null;
        old?.Cancel();
        old?.Dispose();
    }

    private async void Render(string key)
    {
        DisposeRenderCts();
        var cts = _renderCts = new System.Threading.CancellationTokenSource();
        var token = cts.Token;

        ResultPanel.Children.Clear();
        var filtered = _items.Where(MatchesFilter).ToList();
        if (filtered.Count == 0) { ShowNoResultHint(); DisposeRenderCts(); return; }
        EmptyHintPanel.Visibility = Visibility.Collapsed;

        try
        {
            await ChunkedRender.RunAsync(filtered.Count, 8, DispatcherQueue, (s, e) =>
            {
                for (int i = s; i < e; i++)
                {
                    if (token.IsCancellationRequested) return;
                    var card = BuildResultCard(filtered[i], key);
                    ResultPanel.Children.Add(card);
                    if (i < 10) App.PlayCardEntrance(card, i);
                }
            }, token);
        }
        catch (OperationCanceledException) { return; }
        catch (Exception ex)
        {




            System.Diagnostics.Debug.WriteLine($"搜索结果渲染失败（下次搜索将重建）: {ex}");
            DisposeRenderCts();
            return;
        }

        if (token.IsCancellationRequested) return;
        DisposeRenderCts();
    }


    private void ShowBlankHint()
    {


        _items.Clear();
        _currentKey = "";
        ResultPanel.Children.Clear();
        EmptyHintPanel.Children.Clear();
        EmptyHintPanel.Children.Add(Novara.Services.EmptyHintBuilder.Build(
            Novara.Services.EmptyStateArt.SearchBody, Novara.Services.EmptyStateArt.SearchAccent, 1024, 1024,
            App.GetString("Search_Empty")));
        EmptyHintPanel.Children.Add(MakeCmdHintText());
        EmptyHintPanel.Visibility = Visibility.Visible;
    }


    private void ShowNoResultHint()
    {
        ResultPanel.Children.Clear();
        EmptyHintPanel.Children.Clear();
        EmptyHintPanel.Children.Add(Novara.Services.EmptyHintBuilder.Build(
            Novara.Services.EmptyStateArt.NoResultBody, Novara.Services.EmptyStateArt.NoResultAccent, 1027, 1024,
            _currentFilter != "all" ? App.GetString("Search_Filter_NoResult") : App.GetString("Search_NoResult"),
            App.GetString("Search_NoResult_Sub")));
        EmptyHintPanel.Visibility = Visibility.Visible;
    }


    private void ShowHintText(string title)
    {
        ResultPanel.Children.Clear();
        EmptyHintPanel.Children.Clear();
        EmptyHintPanel.Children.Add(Novara.Services.EmptyHintBuilder.BuildText(title));
        EmptyHintPanel.Visibility = Visibility.Visible;
    }


    private TextBlock MakeCmdHintText()
    {
        var block = new TextBlock
        {
            Text = App.GetString("Cmd_Mode_Hint"),
            FontSize = 12,
            HorizontalAlignment = HorizontalAlignment.Center,
            TextAlignment = TextAlignment.Center,
        };
        block.Foreground = App.GetBrush("AppTextSecondaryBrush");
        block.Opacity = Novara.Services.EmptyHintBuilder.Dim;
        return block;
    }






    private Border BuildResultCard(SearchItem it, string key)
    {
        var card = new Border
        {
            Background = App.GetBrush("AppSurfaceOverlayBrush"),
            BorderBrush = App.GetBrush("AppBorderBrush"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(20, 18, 20, 18),
        };
        var grid = new Grid { ColumnSpacing = 12 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var content = new StackPanel { Spacing = 3 };
        var titleRow = new Grid { ColumnSpacing = 6 };
        titleRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        titleRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        titleRow.Children.Add(BuildHighlightText(it.Kind == "diary" ? App.DiaryTitleText(it.Title) : it.Title, key, 14, Microsoft.UI.Text.FontWeights.SemiBold, App.GetBrush("AppTextPrimaryBrush")));
        var tagIcon = new Viewbox { Width = 16, Height = 16, Stretch = Stretch.Uniform, VerticalAlignment = VerticalAlignment.Top, Child = new PathIcon { Data = SafeGeometry(IconData.GetTagIcon(it.Kind, it.Format)), Foreground = App.GetBrush("AppPrimaryButtonBrush") } };
        Grid.SetColumn(tagIcon, 1);
        titleRow.Children.Add(tagIcon);
        content.Children.Add(titleRow);
        if (!string.IsNullOrEmpty(it.SubText))
        {
            var sub = BuildHighlightText(it.SubText, key, 12, Microsoft.UI.Text.FontWeights.Normal, App.GetBrush("AppTextSecondaryBrush"));
            if (it.Kind == "note")
            {
                sub.TextTrimming = TextTrimming.CharacterEllipsis;
                sub.TextWrapping = TextWrapping.NoWrap;
                sub.MaxLines = 1;
            }
            content.Children.Add(sub);
        }
        Grid.SetColumn(content, 0);
        grid.Children.Add(content);

        card.Child = grid;

        card.IsTabStop = true;
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(card, string.IsNullOrEmpty(it.Title) ? it.SubText ?? "" : it.Title);
        card.KeyDown += (_, e) =>
        {
            if (e.Key is not (Windows.System.VirtualKey.Enter or Windows.System.VirtualKey.Space)) return;
            e.Handled = true;
            App.MainWindow?.SearchJump(it.Kind, it.Id);
        };
        card.Tapped += (_, _) => App.MainWindow?.SearchJump(it.Kind, it.Id);
        return card;
    }


    private static TextBlock BuildHighlightText(string text, string key, double size, Windows.UI.Text.FontWeight weight, Brush baseBrush)
    {
        var tb = new TextBlock { FontSize = size, TextWrapping = TextWrapping.Wrap };
        text ??= "";
        if (string.IsNullOrEmpty(key))
        {
            tb.Text = text;
            tb.Foreground = baseBrush;
            return tb;
        }

        var words = key.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var hits = new List<(int Start, int Len)>();
        foreach (var w in words)
        {
            int pos = 0;
            while ((pos = text.IndexOf(w, pos, StringComparison.OrdinalIgnoreCase)) >= 0)
            {
                hits.Add((pos, w.Length));
                pos += w.Length;
            }
        }
        if (hits.Count == 0)
        {
            tb.Text = text;
            tb.Foreground = baseBrush;
            return tb;
        }
        hits.Sort((x, y) => x.Start != y.Start ? x.Start.CompareTo(y.Start) : y.Len.CompareTo(x.Len));
        var brand = App.GetBrush("AppTextPrimaryBrush");
        int cur = 0;
        foreach (var h in hits)
        {
            if (h.Start < cur) continue;
            if (h.Start > cur)
                tb.Inlines.Add(new Run { Text = text.Substring(cur, h.Start - cur), Foreground = baseBrush });
            tb.Inlines.Add(new Run { Text = text.Substring(h.Start, Math.Min(h.Len, text.Length - h.Start)), Foreground = brand, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
            cur = h.Start + h.Len;
        }
        if (cur < text.Length)
            tb.Inlines.Add(new Run { Text = text.Substring(cur), Foreground = baseBrush });
        return tb;
    }

    private static Geometry SafeGeometry(string path)
    {
        try { return App.CreateGeometry(path); }
        catch { return App.CreateGeometry(IconData.Custom); }
    }

    private sealed record SearchItem(string Kind, string Id, string Title, string SubText, DateTime Time, string Format = "", string SearchText = "");
}
