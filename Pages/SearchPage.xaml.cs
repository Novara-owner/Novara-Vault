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

/// <summary>
/// Global search page (3.0-4.5): Ctrl+K opens this page. The search box is an exact copy of
/// the memo/path page one (capsule + magnifier + cancel-x + Enter-to-search). Results are
/// matched entries from all four tabs rendered as uniform cards (kind icon on the right):
/// memo shows name + first info (+ group name on the right), path shows name + path,
/// todo shows name + main todo, note shows name + single-line content, diary shows
/// name + created/modified times. Clicking a card jumps to its page and flashes the target card.
/// </summary>
public sealed partial class SearchPage : Page
{
    private readonly List<SearchItem> _items = new();
    private readonly List<SearchItem> _index = new(); 
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
        new("open_settings", "Setting_Title_Page", string.Join(" ", IconData.Settings)), // N5-S15-02: full 4-path glyph, same as MainWindow (2 paths left the center hollow)
        new("open_trash", "Trash_Title", IconData.Delete),
        new("lock_now", "Tray_LockNow", IconData.Lock), // 9.3: owner-provided padlock glyph (icon-bbox-check passed)
    };
    private readonly List<PaletteCmd> _cmdMatched = new(); // currently filtered command rows (keyboard navigation source)
    private int _cmdIndex; // highlighted row index within _cmdMatched
    private bool _wasPaletteMode; // N2-48: last TextChanged pass rendered palette rows (stale-card cleanup on leave)
    private bool PaletteMode => SearchBox.Text?.StartsWith(">") == true;

    private static readonly string SearchGlyphPath = IconData.Search[0] + " " + IconData.Search[1]; // full magnifier = circle + handle

    public SearchPage()
    {
        InitializeComponent();
        Novara.Services.DialogDepth.AttachContainer((Grid)Content, autoVeil: true); 
        BackPathIcon.Data = (Geometry)Microsoft.UI.Xaml.Markup.XamlBindingHelper.ConvertValue(typeof(Geometry), IconData.Back);
        SearchGlyphIcon.Data = (Geometry)Microsoft.UI.Xaml.Markup.XamlBindingHelper.ConvertValue(typeof(Geometry), SearchGlyphPath);
        BackButton.Click += (_, _) => App.MainWindow?.CloseSearchPage();

        // Same search-box behavior as the tab pages: TextChanged only toggles the x button and
        // restores the blank hint when empty; Enter actually runs the search.
        // 9.3: a leading ">" switches the box into command-palette mode (live filtered command list).
        SearchBox.TextChanged += (_, _) =>
        {
            UpdateCancelButtonVisibility();
            UpdateModeIcon();
            if (PaletteMode) { _wasPaletteMode = true; RenderCommands(); return; }
            // N2-48: leaving palette mode (backspacing past ">") must clear the stale command cards -
            // they lingered until the next Enter-run search otherwise.
            if (_wasPaletteMode)
            {
                _wasPaletteMode = false;
                ResultPanel.Children.Clear();
                ShowBlankHint();
            }
            if (string.IsNullOrWhiteSpace(SearchBox.Text))
                ShowBlankHint();
        };
        // 9.3: Up/Down move the palette highlight; intercepted so the caret never wanders.
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
            if (PaletteMode) { ExecuteHighlightedCommand(); return; } // 9.3: run the highlighted command
            RunSearch(SearchBox.Text?.Trim() ?? "");
        };

        CreateCancelButton();
        CreateFilterButton();

        KeyboardAcceleratorPlacementMode = KeyboardAcceleratorPlacementMode.Hidden; // no ctrl/esc tooltips on this page
        var esc = new KeyboardAccelerator { Key = Windows.System.VirtualKey.Escape };
        esc.Invoked += (_, _) => App.MainWindow?.CloseSearchPage();
        KeyboardAccelerators.Add(esc);
        ApplyTexts();
        Loaded += (_, _) =>
        {
            FocusSearch(); // focus after layout so the back button never gets it
            ShowBlankHint(); // blank hint visible on entry, exactly like the four tab pages
        };
        Unloaded += (_, _) => _renderCts?.Cancel(); 
    }

    /// <summary>Cancel (x) button, built in code exactly like BasicMemoPage/FilePathPage.</summary>
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
        // magnifier in search mode, "</>" prompt mark in palette mode
        SearchGlyphIcon.Data = (Geometry)Microsoft.UI.Xaml.Markup.XamlBindingHelper.ConvertValue(typeof(Geometry),
            PaletteMode ? IconData.CmdPrompt : SearchGlyphPath);
    }

    private void RenderCommands()
    {
        _renderCts?.Cancel(); _renderCts = null; // palette rows render synchronously - kill any in-flight search render
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
            ShowNoResultHint();
            EmptyHintText.Text = App.GetString("Cmd_NoMatch");
            EmptyHintIconBox.Visibility = Visibility.Collapsed; 
            return;
        }
        if (_cmdIndex >= _cmdMatched.Count) _cmdIndex = _cmdMatched.Count - 1; // keep the highlight in range while filtering
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
            BorderBrush = App.GetBrush("AppBorderBrush"), // same card border as the result cards
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(20, 14, 20, 14),
            Tag = index, // row index for hover-synced highlight
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
        card.Tapped += (_, _) => ExecuteCommandAt(index);
        card.PointerEntered += (_, _) => { _cmdIndex = index; RefreshCmdHighlights(); }; // mouse hover drives the same highlight
        return card;
    }

    private void RefreshCmdHighlights()
    {
        for (int i = 0; i < ResultPanel.Children.Count; i++)
        {
            if (ResultPanel.Children[i] is Border b && b.Tag is int idx)
            {
                bool hot = idx == _cmdIndex;
                // assign fresh brush references, never mutate shared instances (E1-09)
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
        App.MainWindow?.RunPaletteCommand(_cmdMatched[index].Action); // RunPaletteCommand closes this page itself
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

    /// <summary>Reset the search state so a fresh entry starts blank (the page instance is cached).</summary>
    public void ResetSearch()
    {
        SearchBox.Text = "";
        // TextChanged does NOT fire while the page is detached from the visual tree (ResetSearch runs
        // before RootFrame.Content switches) - so hide the x / show the blank hint explicitly instead.
        UpdateCancelButtonVisibility();
        ResultPanel.Children.Clear();
        _items.Clear(); // N4-10: RefreshResults treats a non-empty _items as "already searched" - stale cards
        // from the previous session would render under a changed filter on the next visit (page instance is cached)
        _currentFilter = "all";
        _currentKey = "";
        _cmdIndex = 0;
        _cmdMatched.Clear();
        _wasPaletteMode = false; // N3-55: leave palette mode on reset - otherwise the next TextChanged pass
        // with a non-">" first char redundantly re-clears an already-empty panel (harmless, but stale state)
        UpdateModeIcon(); // TextChanged may not fire while detached - restore the magnifier explicitly
        UpdateFilterButton();
        ShowBlankHint();
        RebuildIndex(); 
    }

    public void ApplyTexts()
    {
        SearchBox.PlaceholderText = App.GetString("Search_Placeholder");
        EmptyHintText.Text = App.GetString("Search_Empty");
        CmdHintText.Text = App.GetString("Cmd_Mode_Hint");
    }

    public void FocusSearch()
    {
        FocusSink.Focus(FocusState.Programmatic); // steal focus first - no focus border on the invisible button (LockScreenPage fix)
        SearchBox.Focus(FocusState.Programmatic); // then the search box takes input
    }

    /// <summary>
    
    
    /// </summary>
    private void RebuildIndex()
    {
        _index.Clear();
        var db = App.Store?.Database;
        if (db == null) return;

        foreach (var e in db.MemoEntries.Where(x => !x.IsDeleted))
        {
            var sub = !string.IsNullOrWhiteSpace(e.KeyInfo) ? e.KeyInfo : (e.Fields.Count > 0 ? (e.Fields[0]?.Value ?? "") : ""); 
            var text = (e.Name + " " + e.KeyInfo + " " + string.Join(" ", e.Fields.Where(f => f != null).Select(f => f.Label + " " + f.Value))).ToLowerInvariant();
            _index.Add(new SearchItem("memo", e.Id.ToString(), e.Name, sub, e.CreatedAt, "", text));
        }
        foreach (var p in db.PathBackupItems.Where(x => !x.IsDeleted))
        {
            _index.Add(new SearchItem("path", p.Id.ToString(), p.Name, p.Path, p.CreatedAt, "", (p.Name + " " + p.Path + " " + p.Note).ToLowerInvariant()));
        }
        foreach (var t in db.TodoCards.Where(x => !x.IsDeleted))
        {
            _index.Add(new SearchItem("todo", t.Id.ToString(), t.Title, t.MainText, t.CreatedAt, "", (t.Title + " " + t.MainText + " " + string.Join(" ", t.SubTexts.Where(s => s != null))).ToLowerInvariant()));
        }
        foreach (var n in db.NoteCards.Where(x => !x.IsDeleted))
        {
            _index.Add(new SearchItem("note", n.Id.ToString(), n.Title, n.Content, n.CreatedAt, "", (n.Title + " " + n.Content).ToLowerInvariant()));
        }
        foreach (var d in db.DiaryItems.Where(x => !x.IsDeleted))
        {
            string times = $"{App.GetString("Search_Created")} {d.CreatedAt:yyyy-MM-dd HH:mm}  ·  {App.GetString("Search_Modified")} {d.ModifiedAt:yyyy-MM-dd HH:mm}";
            _index.Add(new SearchItem("diary", d.Id, d.Title, times, d.CreatedAt, d.Format, (d.Title + " " + d.Content).ToLowerInvariant()));
        }
    }

    private void RunSearch(string q)
    {
        if (string.IsNullOrWhiteSpace(q)) { ShowBlankHint(); return; }
        _items.Clear();
        string key = q.ToLowerInvariant();

        foreach (var it in _index)
            if (SearchFuzzy.MatchesQuery(it.SearchText, key))
                _items.Add(it);

        
        var sortWords = key.Split(' ', StringSplitOptions.RemoveEmptyEntries); 
        _items.Sort((a, b) =>
        {
            
            var titleA = a.Title ?? "";
            var titleB = b.Title ?? "";
            bool ta = sortWords.Any(w => titleA.IndexOf(w, StringComparison.OrdinalIgnoreCase) >= 0);
            bool tb = sortWords.Any(w => titleB.IndexOf(w, StringComparison.OrdinalIgnoreCase) >= 0);
            if (ta != tb) return ta ? -1 : 1;
            return b.Time.CompareTo(a.Time);
        });
        _currentKey = key;
        Render(key);
    }

    private async void Render(string key)
    {
        _renderCts?.Cancel();
        var cts = _renderCts = new System.Threading.CancellationTokenSource();
        var token = cts.Token;

        ResultPanel.Children.Clear();
        var filtered = _items.Where(MatchesFilter).ToList();
        if (filtered.Count == 0) { ShowNoResultHint(); _renderCts = null; return; }
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
                    if (i < 10) App.PlayCardEntrance(card, i); // staggered entrance (N5F-04: capped like the four tab pages)
                }
            }, token);
        }
        catch (OperationCanceledException) { return; }

        if (token.IsCancellationRequested) return;
        _renderCts = null;
    }

    /// <summary>Blank hint: single line of text, no icon (same as the four tab pages).</summary>
    private void ShowBlankHint()
    {
        ResultPanel.Children.Clear();
        EmptyHintText.Text = App.GetString("Search_Empty");
        EmptyHintIconBox.Visibility = Visibility.Collapsed;
        CmdHintText.Visibility = Visibility.Visible; // 9.3: palette entry hint under the blank text
        EmptyHintPanel.Visibility = Visibility.Visible;
        FloatInHint();
    }

    /// <summary>No-result hint: icon + text (icon only on the no-result state).</summary>
    private void ShowNoResultHint()
    {
        ResultPanel.Children.Clear();
        EmptyHintText.Text = _currentFilter != "all" ? App.GetString("Search_Filter_NoResult") : App.GetString("Search_NoResult");
        CmdHintText.Visibility = Visibility.Collapsed;
        EmptyHintIconBox.Visibility = Visibility.Visible;
        EmptyHintIcon.Data = (Geometry)Microsoft.UI.Xaml.Markup.XamlBindingHelper.ConvertValue(typeof(Geometry), IconData.SearchNoResult);
        EmptyHintPanel.Visibility = Visibility.Visible;
        FloatInHint();
    }

    private void FloatInHint()
    {
        EmptyHintPanel.Opacity = 0;
        if (EmptyHintPanel.RenderTransform is not TranslateTransform tt)
        {
            tt = new TranslateTransform { Y = 20 };
            EmptyHintPanel.RenderTransform = tt;
        }
        else tt.Y = 20;
        var sb = new Storyboard();
        var oa = new DoubleAnimation { To = 0.6, Duration = TimeSpan.FromMilliseconds(400), EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
        Storyboard.SetTarget(oa, EmptyHintPanel); Storyboard.SetTargetProperty(oa, "Opacity");
        sb.Children.Add(oa);
        var ya = new DoubleAnimation { To = 0, Duration = TimeSpan.FromMilliseconds(400), EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
        Storyboard.SetTarget(ya, tt); Storyboard.SetTargetProperty(ya, "Y");
        sb.Children.Add(ya);
        sb.Begin();
    }

    /// <summary>
    /// Uniform result card: row1 = kind icon (right, AppPrimaryButtonBrush) + name (highlighted),
    /// row2 = kind-specific info (memo first info, path, main todo,
    /// single-line note content, diary created+modified times); right side = memo group name + star/pin badges.
    /// </summary>
    private Border BuildResultCard(SearchItem it, string key)
    {
        var card = new Border
        {
            Background = App.GetBrush("AppSurfaceOverlayBrush"),
            BorderBrush = App.GetBrush("AppBorderBrush"), // same card border as the tab pages
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(20, 18, 20, 18), // same padding as the tab pages
        };
        var grid = new Grid { ColumnSpacing = 12 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var content = new StackPanel { Spacing = 3 };
        var titleRow = new Grid { ColumnSpacing = 6 };
        titleRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        titleRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        titleRow.Children.Add(BuildHighlightText(it.Title, key, 14, Microsoft.UI.Text.FontWeights.SemiBold, App.GetBrush("AppTextPrimaryBrush")));
        var tagIcon = new Viewbox { Width = 16, Height = 16, Stretch = Stretch.Uniform, VerticalAlignment = VerticalAlignment.Top, Child = new PathIcon { Data = SafeGeometry(IconData.GetTagIcon(it.Kind, it.Format)), Foreground = App.GetBrush("AppPrimaryButtonBrush") } };
        Grid.SetColumn(tagIcon, 1);
        titleRow.Children.Add(tagIcon);
        content.Children.Add(titleRow);
        if (!string.IsNullOrEmpty(it.SubText))
        {
            var sub = BuildHighlightText(it.SubText, key, 12, Microsoft.UI.Text.FontWeights.Normal, App.GetBrush("AppTextSecondaryBrush"));
            if (it.Kind == "note") // single-line content with ellipsis for notes
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
        card.Tapped += (_, _) => App.MainWindow?.SearchJump(it.Kind, it.Id);
        return card;
    }

    /// <summary>Build a TextBlock whose matching segment is emphasized (theme-aware).</summary>
    private static TextBlock BuildHighlightText(string text, string key, double size, Windows.UI.Text.FontWeight weight, Brush baseBrush)
    {
        var tb = new TextBlock { FontSize = size, TextWrapping = TextWrapping.Wrap };
        text ??= ""; // N4-11: explicit "Name":null in a hand-edited/import file deserializes to null (store normalization does not cover Name/Title) - IndexOf would NRE mid-render
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
