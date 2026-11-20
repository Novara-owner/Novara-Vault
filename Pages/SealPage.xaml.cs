using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Novara.Models;
using Windows.ApplicationModel.DataTransfer;
using Novara.Services;

namespace Novara.Pages;

public sealed partial class SealPage : Microsoft.UI.Xaml.Controls.Page
{

    private readonly Dictionary<Grid, int> _dialogGens = new();
    private readonly Dictionary<Grid, Border> _dialogMap = new();
    private readonly DialogUi.DialogWidthFit _dialogWidthFit = new();

    private bool _pwdInFlight;
    private Action<bool>? _pwdOnClosed;

    public SealPage()
    {
        InitializeComponent();
        App.ApplyAutomationNames(this);

        DialogDepth.AttachContainer(SealRoot);


        DialogDepth.AttachContainer(SealContentHost);

        SealRoot.SizeChanged += (_, _) =>
            DialogUi.ReclampVisibleDialogs(SealRoot, _dialogMap, null, _dialogWidthFit);
        Unloaded += (_, _) => { _dialogGens.Clear(); _dialogMap.Clear(); _dialogWidthFit.Clear(); _capsuleExpiryTimer?.Stop(); };


        _capsuleExpiryTimer = new DispatcherTimer();
        _capsuleExpiryTimer.Tick += (_, _) =>
        {
            _capsuleExpiryTimer!.Stop();
            if (SealCapsuleView.Visibility == Visibility.Visible) RefreshCapsules();
            if (SealDetailOverlay.Visibility == Visibility.Visible && _detailItem is { } open) ApplyDetailTakeState(open);
            ScheduleCapsuleExpiry();
        };




        SealEmptyState.Content = Novara.Services.SealEmptyStateBuilder.Build(
            App.GetString("Seal_Empty_Title"),
            App.GetString("Seal_Empty_Warning"),
            App.GetString("Seal_Empty_Hint"),
            App.GetString("Seal_Menu_SealFiles"),
            (_, _) => PickFilesToSeal(capsuleByDefault: false));
        SealCapsuleEmptyState.Content = Novara.Services.SealEmptyStateBuilder.Build(
            App.GetString("Seal_Capsule_Empty_Title"),
            App.GetString("Seal_Capsule_Empty_Warning"),
            App.GetString("Seal_Capsule_Empty_Hint"),
            App.GetString("Seal_Menu_SealFiles"),
            (_, _) => PickFilesToSeal(capsuleByDefault: true));


        SealLockedState.Content = Novara.Services.SealEmptyStateBuilder.BuildLocked(
            App.GetString("Seal_Locked_Title"), App.GetString("Seal_Locked_Hint"));
        SealCapsuleLockedState.Content = Novara.Services.SealEmptyStateBuilder.BuildLocked(
            App.GetString("Seal_Locked_Title"), App.GetString("Seal_Locked_Hint"));


        SealNotInitState.Content = Novara.Services.SealEmptyStateBuilder.BuildNotice(
            BuildNotInitTitle(), App.GetString("Seal_NotInit_Hint"),
            App.GetString("Seal_FirstRun_SetPassword"),
            (_, _) => ShowPasswordDialog(_ => { }));
        SealCapsuleNotInitState.Content = Novara.Services.SealEmptyStateBuilder.BuildNotice(
            BuildNotInitTitle(), App.GetString("Seal_NotInit_Hint"),
            App.GetString("Seal_FirstRun_SetPassword"),
            (_, _) => ShowPasswordDialog(_ => { }));


        SealSpaceView.RightTapped += SealSpaceView_RightTapped;


        SealCapsuleView.RightTapped += SealCapsuleView_RightTapped;
        SealSealSegment.SizeChanged += (_, _) => SetSealTarget(_sealCapsule);


        SealFolderSeg.SizeChanged += (_, _) => ApplyFolderMode(_folderTakeArchive);

#if DEBUG


        var devSeed = new KeyboardAccelerator
        {
            Modifiers = Windows.System.VirtualKeyModifiers.Control
                | Windows.System.VirtualKeyModifiers.Menu
                | Windows.System.VirtualKeyModifiers.Shift,
            Key = Windows.System.VirtualKey.D,
        };
        devSeed.Invoked += (sender, args) =>
        {
            args.Handled = true;
            _ = SeedDevItemsAsync();
        };
        SealRoot.KeyboardAccelerators.Add(devSeed);
#endif
    }


    public void StealFocus() => FocusSink.Focus(FocusState.Programmatic);


    private static TextBlock BuildNotInitTitle()
    {
        var title = new TextBlock
        {
            FontSize = 20,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
        };
        title.Foreground = App.GetBrush("AppTextPrimaryBrush");
        var text = App.GetString("Seal_NotInit_Title");
        const string brand = "Novara Space";
        int hit = text.IndexOf(brand, StringComparison.Ordinal);
        if (hit < 0)
        {

            title.Inlines.Add(new Microsoft.UI.Xaml.Documents.Run { Text = text });
            return title;
        }
        if (hit > 0)
            title.Inlines.Add(new Microsoft.UI.Xaml.Documents.Run { Text = text[..hit] });
        title.Inlines.Add(new Microsoft.UI.Xaml.Documents.Run
        {
            Text = brand,
            FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Assets/Fonts/Comfortaa.ttf#Comfortaa"),
        });
        if (hit + brand.Length < text.Length)
            title.Inlines.Add(new Microsoft.UI.Xaml.Documents.Run { Text = text[(hit + brand.Length)..] });
        return title;
    }



    public void ShowView(int index)
    {
        SealSpaceView.Visibility = index == 0 ? Visibility.Visible : Visibility.Collapsed;
        SealCapsuleView.Visibility = index == 1 ? Visibility.Visible : Visibility.Collapsed;
        SealSettingsView.Visibility = index == 2 ? Visibility.Visible : Visibility.Collapsed;
        if (index == 0 && _itemsDirty) RefreshItems();


        if (index == 0 && _longOpInterruptedByLock)
        {
            _longOpInterruptedByLock = false;
            if (_sealInFlight || _takeInFlight)
            {
                SealLongOpCancelButton.Visibility = Visibility.Visible;
                SealLongOpCancelButton.IsEnabled = _longOpCts is { IsCancellationRequested: false };
                App.ShowToast(App.GetString("Seal_Space_Busy"));
            }
        }

        if (index == 1 && (_capsuleDirty || _nextCapsuleExpiry is { } due && DateTimeOffset.UtcNow >= due))
            RefreshCapsules();
        if (index == 2 && _settingsDirty)
        {
            try { RefreshSettings(); }
            catch (Exception ex)
            {

                CrashLogger.LogNote("SealSettings", ex.Message);
                _settingsDirty = true;
            }
        }
    }



    private bool _itemsDirty = true;
    private bool _itemsEntrancePlayed;








    public void RefreshItems()
    {
        _itemsDirty = false;
        _capsuleDirty = true;
        _settingsDirty = true;
        SealSpaceContent.Children.Clear();
        _cardById.Clear();

        var result = SealService.Current.ListItems();
        var built = new List<Border>();
        if (result.Status == SealServiceStatus.Ok && result.Value is { } items)
        {
            foreach (var item in items.Where(i => !i.IsCapsule).OrderByDescending(i => i.SealedAtUtc))
            {
                var card = BuildItemCard(item);
                SealSpaceContent.Children.Add(card);
                built.Add(card);
            }
        }





        if (!_itemsEntrancePlayed && built.Count > 0)
        {
            _itemsEntrancePlayed = true;
            for (int i = 0; i < Math.Min(10, built.Count); i++) App.PlayCardEntrance(built[i], i);
        }

        SyncSpaceState(result.Status);
        RefreshRecovery();
    }


    private void SyncSpaceState(SealServiceStatus status)
    {



        var notInit = !SealService.Current.IsInitialized();
        var failed = !notInit && status is not (SealServiceStatus.Ok or SealServiceStatus.NotUnlocked);
        var locked = !notInit && status == SealServiceStatus.NotUnlocked;
        var empty = SealSpaceContent.Children.Count == 0;

        SealListError.Visibility = failed ? Visibility.Visible : Visibility.Collapsed;
        if (failed) SealListError.Text = ListErrorText(status);



        SealNotInitState.Visibility = !failed && notInit ? Visibility.Visible : Visibility.Collapsed;

        SealLockedState.Visibility = !failed && !notInit && locked ? Visibility.Visible : Visibility.Collapsed;
        SealEmptyState.Visibility = !failed && !notInit && !locked && empty ? Visibility.Visible : Visibility.Collapsed;
        SealSpaceScroll.Visibility = !failed && !notInit && !locked && !empty ? Visibility.Visible : Visibility.Collapsed;
    }


    private static string ListErrorText(SealServiceStatus status)
        => status switch
        {
            SealServiceStatus.Corrupt => App.GetString("Seal_List_Err_Corrupt"),
            SealServiceStatus.VersionUnsupported => App.GetString("Seal_List_Err_Unsupported"),
            _ => App.GetString("Seal_List_Err_Io"),
        };



    private bool _capsuleDirty = true;
    private bool _capsulesEntrancePlayed;
    private DateTimeOffset? _nextCapsuleExpiry;
    private DispatcherTimer? _capsuleExpiryTimer;





    public void RefreshCapsules()
    {
        _capsuleDirty = false;
        _nextCapsuleExpiry = null;
        SealCapsuleContent.Children.Clear();
        _cardById.Clear();

        var result = SealService.Current.ListItems();
        var now = DateTimeOffset.UtcNow;



        var observed = SealService.Current.GetObservedMaxUtc();
        var observedMax = observed.Status == SealServiceStatus.Ok ? observed.Value : 0;
        var built = new List<Border>();
        if (result.Status == SealServiceStatus.Ok && result.Value is { } items)
        {
            var capsules = items
                .Where(i => i.IsCapsule)
                .OrderBy(i => SealTimeLock.Evaluate(i, now, observedMax) == SealTakeVerdict.Expired ? 0 : 1)
                .ThenBy(i => i.ExpiryUtc ?? long.MaxValue)
                .ThenByDescending(i => i.SealedAtUtc)
                .ToList();

            foreach (var item in capsules)
            {
                var verdict = SealTimeLock.Evaluate(item, now, observedMax);
                var card = BuildCapsuleCard(item, now, verdict);
                SealCapsuleContent.Children.Add(card);
                built.Add(card);
                if (item.ExpiryUtc is { } rawExpiry)
                {
                    var expiry = DateTimeOffset.FromUnixTimeSeconds(rawExpiry);
                    if (expiry > now && (_nextCapsuleExpiry is null || expiry < _nextCapsuleExpiry))
                        _nextCapsuleExpiry = expiry;
                }
            }
        }



        if (!_capsulesEntrancePlayed && built.Count > 0)
        {
            _capsulesEntrancePlayed = true;
            for (int i = 0; i < Math.Min(10, built.Count); i++) App.PlayCardEntrance(built[i], i);
        }

        SyncCapsuleState(result.Status);
        ScheduleCapsuleExpiry();
    }



    private void ScheduleCapsuleExpiry()
    {
        var now = DateTimeOffset.UtcNow;
        DateTimeOffset? next = _nextCapsuleExpiry;
        if (_detailItem is { IsCapsule: true, ExpiryUtc: { } raw })
        {
            var detailExpiry = DateTimeOffset.FromUnixTimeSeconds(raw);
            if (detailExpiry > now && (next is null || detailExpiry < next)) next = detailExpiry;
        }

        if (next is null)
        {
            _capsuleExpiryTimer?.Stop();
            return;
        }

        var delay = next.Value - now;
        if (delay < TimeSpan.FromSeconds(1)) delay = TimeSpan.FromSeconds(1);
        _capsuleExpiryTimer ??= new DispatcherTimer();
        _capsuleExpiryTimer.Stop();
        _capsuleExpiryTimer.Interval = delay;
        _capsuleExpiryTimer.Start();
    }


    private void SyncCapsuleState(SealServiceStatus status)
    {

        var notInit = !SealService.Current.IsInitialized();
        var failed = !notInit && status is not (SealServiceStatus.Ok or SealServiceStatus.NotUnlocked);
        var locked = !notInit && status == SealServiceStatus.NotUnlocked;
        var empty = SealCapsuleContent.Children.Count == 0;

        SealCapsuleListError.Visibility = failed ? Visibility.Visible : Visibility.Collapsed;
        if (failed) SealCapsuleListError.Text = ListErrorText(status);


        SealCapsuleNotInitState.Visibility = !failed && notInit ? Visibility.Visible : Visibility.Collapsed;

        SealCapsuleLockedState.Visibility = !failed && !notInit && locked ? Visibility.Visible : Visibility.Collapsed;
        SealCapsuleEmptyState.Visibility = !failed && !notInit && !locked && empty ? Visibility.Visible : Visibility.Collapsed;
        SealCapsuleScroll.Visibility = !failed && !notInit && !locked && !empty ? Visibility.Visible : Visibility.Collapsed;
    }





    private Border BuildCapsuleCard(SealedItem item, DateTimeOffset now, SealTakeVerdict verdict)
    {
        var glyph = SealGlyphs.Detect(item.OriginalName, item.IsFolder);
        var size = PathPropertyFormatter.FormatSize((long)Math.Min(item.Size, long.MaxValue));
        var meta = item.IsFolder
            ? string.Format(App.GetString("Seal_Card_FolderMeta"), PathPropertyFormatter.FormatCount(item.FolderEntryCount ?? 0), size)
            : LabelOf(glyph) + " · " + size;
        var name = string.IsNullOrEmpty(item.DisplayName) ? item.OriginalName : item.DisplayName;
        var expired = verdict == SealTakeVerdict.Expired;




        var card = BuildCardShell(glyph, name, meta, new Thickness(0), GridLength.Auto, out var row, out var textColumn);
        row.Margin = new Thickness(20, 12, 20, 12);
        textColumn.Margin = new Thickness(0, 0, 96, 0);

        var sealedAt = DateTimeOffset.FromUnixTimeSeconds(item.SealedAtUtc).ToLocalTime().ToString("yyyy-MM-dd");
        var timeLine = string.Format(App.GetString("Seal_Card_SealedAt"), sealedAt) + "  ·  " + ExpiryLine(item, now);
        var timeText = MakeCardTime(timeLine, "AppTextTertiaryBrush");
        timeText.VerticalAlignment = VerticalAlignment.Bottom;
        Grid.SetColumn(timeText, 2);
        row.Children.Add(timeText);

        var root = new Grid();
        root.Children.Add(row);
        root.Children.Add(BuildCapsuleBadge(verdict));
        card.Child = root;

        AttachCardActivation(card, name + "，" + meta, () => ShowSealedItemDetail(item));
        card.RightTapped += (_, args) => args.Handled = true;
        AttachCardHover(card);
        _cardById[item.Id] = card;
        return card;
    }






    private static Border BuildCapsuleBadge(SealTakeVerdict verdict)
    {
        var expired = verdict == SealTakeVerdict.Expired;
        var clock = verdict == SealTakeVerdict.RollbackSuspected;
        var brushKey = expired ? "AppPrimaryButtonBrush" : clock ? "AppDangerTextBrush" : "AppTextSecondaryBrush";
        var brush = App.GetBrush(brushKey);

        var capsule = new Border
        {
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, 8, 8, 0),
            Height = 24,
            CornerRadius = new CornerRadius(8),
            Background = App.GetBrush("AppSurfaceBrush"),
            BorderBrush = expired ? brush : App.GetBrush("AppBorderBrush"),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(7, 0, 7, 0),
            IsHitTestVisible = false,
        };

        var stack = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 5,
            VerticalAlignment = VerticalAlignment.Center,
        };

        stack.Children.Add(new Viewbox
        {
            Width = 12,
            Height = 12,
            Stretch = Stretch.Uniform,
            VerticalAlignment = VerticalAlignment.Center,
            Child = new Microsoft.UI.Xaml.Shapes.Path
            {
                Data = expired ? App.CreateGeometry(IconData.Capsule[0])
                    : clock ? App.CreateGeometry(IconData.Danger)
                    : App.CreateGeometry(IconData.Lock),
                Fill = brush,
                Stretch = Stretch.None,
            },
        });
        stack.Children.Add(new TextBlock
        {
            Text = App.GetString(expired ? "Seal_Capsule_Status_Expired"
                : clock ? "Seal_Capsule_Status_Clock"
                : "Seal_Capsule_Status_Locked"),
            FontSize = 11,
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = brush,
        });
        capsule.Child = stack;
        return capsule;
    }


    private static string ExpiryLine(SealedItem item, DateTimeOffset now)
    {
        if (item.ExpiryUtc is not { } rawExpiry) return App.GetString("Seal_Detail_Empty");
        var expiry = DateTimeOffset.FromUnixTimeSeconds(rawExpiry);
        var date = expiry.ToLocalTime().ToString("yyyy-MM-dd");
        var relative = RelativeFrom(now, expiry);
        return relative is null
            ? string.Format(App.GetString("Seal_Card_ExpiryPlain"), date)
            : string.Format(App.GetString("Seal_Card_Expiry"), date, relative);
    }



    private static string? RelativeFrom(DateTimeOffset now, DateTimeOffset expiry)
    {
        var span = expiry - now;
        if (span <= TimeSpan.Zero) return null;

        var days = (int)Math.Round(span.TotalDays);
        if (days >= 365) return string.Format(App.GetString("Seal_Card_RelYears"), (int)Math.Round(days / 365.0));
        if (days >= 30) return string.Format(App.GetString("Seal_Card_RelMonths"), (int)Math.Round(days / 30.0));
        if (days >= 1) return string.Format(App.GetString("Seal_Card_RelDays"), days);
        var hours = (int)Math.Round(span.TotalHours);
        if (hours >= 1) return string.Format(App.GetString("Seal_Card_RelHours"), hours);
        return string.Format(App.GetString("Seal_Card_RelMinutes"), Math.Max(1, (int)Math.Round(span.TotalMinutes)));
    }


    private Border BuildItemCard(SealedItem item)
    {

        var glyph = SealGlyphs.Detect(item.OriginalName, item.IsFolder);
        var size = PathPropertyFormatter.FormatSize((long)Math.Min(item.Size, long.MaxValue));
        var meta = item.IsFolder
            ? string.Format(App.GetString("Seal_Card_FolderMeta"), PathPropertyFormatter.FormatCount(item.FolderEntryCount ?? 0), size)
            : LabelOf(glyph) + " · " + size;
        var sealedAt = DateTimeOffset.FromUnixTimeSeconds(item.SealedAtUtc).ToLocalTime().ToString("yyyy-MM-dd HH:mm");
        var name = string.IsNullOrEmpty(item.DisplayName) ? item.OriginalName : item.DisplayName;
        var card = BuildItemCard(glyph, name, meta, sealedAt);
        AttachCardActivation(card, name + "，" + meta, () => ShowSealedItemDetail(item));


        card.RightTapped += (_, args) => args.Handled = true;
        _cardById[item.Id] = card;
        return card;
    }








    private static void AttachCardActivation(Border card, string accessibilityName, Action activate)
    {
        card.IsTabStop = true;
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(card, accessibilityName);
        card.Tapped += (_, args) =>
        {
            args.Handled = true;
            activate();
        };
        card.KeyDown += (_, args) =>
        {
            if (args.Key is not (Windows.System.VirtualKey.Enter or Windows.System.VirtualKey.Space)) return;
            args.Handled = true;
            activate();
        };
    }


    private static string LabelOf(SealGlyphs.Glyph glyph)
        => glyph.Label switch
        {
            SealGlyphs.FolderLabel => App.GetString("Seal_Type_Folder"),
            SealGlyphs.UnknownLabel => App.GetString("Seal_Type_Generic"),
            _ => glyph.Label,
        };

#if DEBUG
    private bool _devSeedBusy;






    private async Task SeedDevItemsAsync()
    {
        if (_devSeedBusy) return;
        _devSeedBusy = true;
        try
        {
            var root = Path.Combine(Path.GetTempPath(), "novara-seal-seed", Guid.NewGuid().ToString("N")[..8]);
            Directory.CreateDirectory(root);

            var samples = new (string Name, int Bytes)[]
            {
                ("季度报表.pdf", 2048),
                ("风景照片.jpg", 4096),
                ("资料包.zip", 1024),
                ("产品演示.mp4", 8192),
                ("说明.txt", 512),
                ("会议纪要.docx", 1536),
                ("无后缀文件", 700),
            };

            var sources = new List<string>();
            var filler = new byte[256];
            Random.Shared.NextBytes(filler);
            foreach (var (name, bytes) in samples)
            {
                var file = Path.Combine(root, name);
                var buffer = new byte[bytes];
                for (var offset = 0; offset < bytes; offset += filler.Length)
                    Array.Copy(filler, 0, buffer, offset, Math.Min(filler.Length, bytes - offset));
                await File.WriteAllBytesAsync(file, buffer);
                sources.Add(file);
            }


            var folder = Path.Combine(root, "项目文件夹");
            Directory.CreateDirectory(folder);
            await File.WriteAllTextAsync(Path.Combine(folder, "笔记一.txt"), "seed a");
            await File.WriteAllTextAsync(Path.Combine(folder, "笔记二.txt"), "seed b");
            sources.Add(folder);

            var sealedCount = 0;
            foreach (var source in sources)
            {
                var result = await SealService.Current.SealAsync(new List<SealRequest> { new(source) });
                if (result.Status == SealServiceStatus.Ok) sealedCount++;
                await Task.Delay(1100);
            }






            var capsules = new (string Name, long Expiry)[]
            {
                ("两分钟后到期.txt", 120),
                ("一年后到期.txt", 365L * 24 * 3600),
                ("二十年后到期.txt", 20L * 365 * 24 * 3600),
            };
            var capsuleSealed = 0;
            foreach (var (capsuleName, expiry) in capsules)
            {
                var file = Path.Combine(root, capsuleName);
                await File.WriteAllBytesAsync(file, new byte[1024]);
                var result = await SealService.Current.SealAsync(
                    new List<SealRequest> { new(file, null, SealedItemKind.Capsule, expiry, null) });
                if (result.Status == SealServiceStatus.Ok) capsuleSealed++;
                await Task.Delay(1100);
            }

            try { Directory.Delete(root, recursive: true); } catch (IOException) {  }

            RefreshItems();
            RefreshCapsules();
            App.ShowToast($"seed: {sealedCount}/{sources.Count} · capsule {capsuleSealed}/{capsules.Length}");
        }
        finally
        {
            _devSeedBusy = false;
        }
    }
#endif








    private Border BuildCardShell(SealGlyphs.Glyph glyph, string name, string meta, Thickness padding,
        GridLength rightWidth, out Grid row, out StackPanel textColumn)
    {
        var card = new Border
        {
            CornerRadius = new CornerRadius(12),
            Background = App.GetBrush("AppSurfaceOverlayBrush"),
            BorderBrush = new SolidColorBrush(App.GetBrush("AppBorderBrush").Color),
            BorderThickness = new Thickness(1),
            Padding = padding,
            Height = 64,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            RenderTransform = new TranslateTransform { Y = 0 },
        };

        row = new Grid();
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = rightWidth });



        var icon = SealIconFactory.Create(glyph, 32);
        icon.VerticalAlignment = VerticalAlignment.Center;
        icon.Margin = new Thickness(0, 0, 12, 0);
        Grid.SetColumn(icon, 0);
        row.Children.Add(icon);

        textColumn = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        var nameText = new TextBlock
        {
            Text = name,
            FontSize = 14,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            CharacterSpacing = App.GetCharacterSpacing(name, 200),
            Foreground = App.GetBrush("AppTextPrimaryBrush"),
            HorizontalAlignment = HorizontalAlignment.Left,
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxLines = 1,
        };
        var metaText = new TextBlock
        {
            Text = meta,
            FontSize = 12,
            FontWeight = Microsoft.UI.Text.FontWeights.Light,
            CharacterSpacing = App.GetCharacterSpacing(meta, 60),
            Foreground = App.GetBrush("AppTextSecondaryBrush"),
            HorizontalAlignment = HorizontalAlignment.Left,
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxLines = 1,
            Margin = new Thickness(0, 4, 0, 0),
        };
        textColumn.Children.Add(nameText);
        textColumn.Children.Add(metaText);
        Grid.SetColumn(textColumn, 1);
        row.Children.Add(textColumn);

        return card;
    }


    private static TextBlock MakeCardTime(string text, string brushKey)
        => new()
        {
            Text = text,
            FontSize = 12,
            CharacterSpacing = App.GetCharacterSpacing(text, 60),
            Foreground = App.GetBrush(brushKey),
            HorizontalAlignment = HorizontalAlignment.Right,
            TextAlignment = TextAlignment.Right,
        };


    private Border BuildItemCard(SealGlyphs.Glyph glyph, string name, string meta, string sealedAt)
    {
        var card = BuildCardShell(glyph, name, meta, new Thickness(20, 12, 20, 12), new GridLength(120), out var row, out _);

        var timeText = MakeCardTime(sealedAt, "AppTextTertiaryBrush");
        timeText.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(timeText, 2);
        row.Children.Add(timeText);

        card.Child = row;
        AttachCardHover(card);
        return card;
    }



    private void AttachCardHover(Border card)
    {
        if (App.GetBrush("AppBorderBrush") is not SolidColorBrush borderBrush) return;
        var baseBorderColor = borderBrush.Color;
        var current = new SolidColorBrush(baseBorderColor);
        card.BorderBrush = current;

        card.PointerEntered += (_, _) =>
        {
            var hoverColor = Windows.UI.Color.FromArgb(
                (byte)Math.Min(current.Color.A + 0x30, 0xFF), current.Color.R, current.Color.G, current.Color.B);
            current.Color = hoverColor;
            AnimateCardOffset(card, -3, 200);
        };
        card.PointerExited += (_, _) =>
        {
            current.Color = baseBorderColor;
            AnimateCardOffset(card, 0, 300);
            card.Opacity = 1;
        };



        card.PointerPressed += (_, e) =>
        {
            if (!e.GetCurrentPoint(card).Properties.IsLeftButtonPressed) return;
            card.Opacity = 0.9;
        };
        card.PointerReleased += (_, _) => card.Opacity = 1;
        card.PointerCaptureLost += (_, _) => card.Opacity = 1;
    }

    private static void AnimateCardOffset(Border card, double to, int ms)
    {
        if (!App.IsAnimationsEnabled || card.RenderTransform is not TranslateTransform t) return;
        var a = new Microsoft.UI.Xaml.Media.Animation.DoubleAnimation
        {
            To = to,
            Duration = TimeSpan.FromMilliseconds(ms),
            EasingFunction = new Microsoft.UI.Xaml.Media.Animation.CubicEase { EasingMode = Microsoft.UI.Xaml.Media.Animation.EasingMode.EaseOut },
        };
        var sb = new Microsoft.UI.Xaml.Media.Animation.Storyboard();
        Microsoft.UI.Xaml.Media.Animation.Storyboard.SetTarget(a, t);
        Microsoft.UI.Xaml.Media.Animation.Storyboard.SetTargetProperty(a, "Y");
        sb.Children.Add(a);
        sb.Begin();
    }



    private bool _takeInFlight;
    private SealedItem? _detailItem;
    private SealedItem? _takeItem;
    private string? _takeDirectory;
    private string? _receiptPath;
    private Action? _errorRetry;
    private int _leaveGen;


    private async Task BeginTakeAsync()
    {
        if (_recoveryPassInFlight) { RefuseWhileRecovering(); return; }
        if (_sealInFlight) { RefuseWhileSealing(); return; }
        if (_takeInFlight || _destroyInFlight || _detailItem is null) return;

        var item = _detailItem;
        var verdict = SealService.Current.CanTake(item.Id);


        if (verdict.Status != SealServiceStatus.Ok)
        {
            ShowSealTakeError("Seal_Take_Failed", () => _ = BeginTakeAsync());
            return;
        }

        if (!verdict.Value) return;

        var directory = FilePicker.PickFolder(startAtDesktop: true);
        if (string.IsNullOrEmpty(directory)) return;


        var archive = false;
        if (item.IsFolder)
        {
            var mode = await AskFolderTakeModeAsync();
            if (mode is null) return;
            archive = mode.Value;
        }




        var required = item.IsFolder && !archive
            ? (item.Size > ulong.MaxValue / 2 ? ulong.MaxValue : item.Size * 2)
            : item.Size;
        if (item.Size > 0 && !HasRoomFor(directory, required))
        {
            ShowSealTakeError("Seal_Take_NoSpace", () => _ = BeginTakeAsync());
            return;
        }

        var name = OriginNameOf(item) + (archive ? ".zip" : "");
        var target = Path.Combine(directory, name);
        if (File.Exists(target) || Directory.Exists(target))
        {
            ShowSealConflictDialog(item, directory, name, archive);
            return;
        }


        await RunTakeAsync(item, directory, name, SealTakeConflictPolicy.Rename, archive);
    }


    private static string OriginNameOf(SealedItem item)
        => string.IsNullOrEmpty(item.OriginalName) ? item.DisplayName : item.OriginalName;


    private static bool IsAcceptableFileName(string name)
    {
        if (name.Length == 0) return false;
        if (name is "." or "..") return false;
        if (name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0) return false;
        if (name.EndsWith(' ') || name.EndsWith('.')) return false;
        var stem = Path.GetFileNameWithoutExtension(name).ToUpperInvariant();
        return !ReservedFileNames.Contains(stem);
    }

    private static readonly System.Collections.Generic.HashSet<string> ReservedFileNames = new()
    {
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
    };


    private static bool HasRoomFor(string directory, ulong needed)
    {
        try
        {
            var root = Path.GetPathRoot(Path.GetFullPath(directory));
            if (string.IsNullOrEmpty(root)) return true;
            var free = new DriveInfo(root).AvailableFreeSpace;
            return free <= 0 || needed <= (ulong)free;
        }
        catch (Exception)
        {
            return true;
        }
    }



    private CancellationTokenSource? _longOpCts;
    private bool _longOpCancelled;
    private bool _longOpInterruptedByLock;

    private void BeginLongOp()
    {
        _longOpCts?.Dispose();
        _longOpCts = new CancellationTokenSource();
        _longOpCancelled = false;
        SealLongOpCancelButton.Content = App.GetString("Common_Button_Cancel");
        SealLongOpCancelButton.IsEnabled = true;
        SealLongOpCancelButton.Visibility = Visibility.Visible;
    }

    private void EndLongOp()
    {
        SealLongOpCancelButton.Visibility = Visibility.Collapsed;
        _longOpCts?.Dispose();
        _longOpCts = null;
    }

    private void SealLongOpCancel_Click(object sender, RoutedEventArgs e)
    {
        if (_longOpCts is not { IsCancellationRequested: false }) return;
        _longOpCancelled = true;
        SealLongOpCancelButton.IsEnabled = false;
        _longOpCts.Cancel();
    }

    private async Task RunTakeAsync(SealedItem item, string directory, string? targetName,
        SealTakeConflictPolicy policy, bool extractAsArchive = false)
    {
        _takeInFlight = true;
        var gen = _leaveGen;
        BeginLongOp();
        App.ShowToast(App.GetString("Seal_Take_Working"), persistent: true);
        try
        {
            var result = await SealService.Current.TakeAsync(
                new SealTakeRequest(item.Id, directory, policy, targetName, ExtractAsArchive: extractAsArchive),
                _longOpCts?.Token ?? default);




            if (gen != _leaveGen)
            {
                App.ClearToast();
                _itemsDirty = true;
                _capsuleDirty = true;
                if (result.Status == SealServiceStatus.Ok && result.Value?.Status == SealTakeStatus.Taken)
                {
                    var done = string.Format(App.GetString("Seal_Take_Done"), item.DisplayName, result.Value.TargetPath);
                    App.ShowToast(done);
                }
                else if (result.Status == SealServiceStatus.Ok && result.Value?.Status == SealTakeStatus.ExtractFailed)
                {


                    App.ShowToast(string.Format(App.GetString("Seal_Take_ExtractFailed"), result.Value.TargetPath),
                        persistent: true);
                }
                return;
            }

            if (result.Status == SealServiceStatus.Busy)
            {
                ShowSealTakeError("Seal_Take_Busy", () => _ = RunTakeAsync(item, directory, targetName, policy, extractAsArchive));
                return;
            }
            if (result.Status != SealServiceStatus.Ok || result.Value is null)
            {
                ShowSealTakeError("Seal_Take_Failed", () => _ = RunTakeAsync(item, directory, targetName, policy, extractAsArchive));
                return;
            }

            var taken = result.Value;
            switch (taken.Status)
            {
                case SealTakeStatus.Taken:
                    var done = string.Format(App.GetString("Seal_Take_Done"), item.DisplayName, taken.TargetPath);
                    App.ShowToast(done);

                    if (!string.IsNullOrEmpty(taken.LeftoverPath))
                        App.ShowToast(string.Format(App.GetString("Seal_Take_Leftover"), taken.LeftoverPath),
                            persistent: true);
                    RefreshItems();
                    HideSealedItemDetail();
                    ShowSealReceipt(done, taken.TargetPath);


                    if (SealCapsuleView.Visibility == Visibility.Visible) RefreshCapsules();
                    break;
                case SealTakeStatus.Locked:
                    ShowSealTakeError("Seal_Take_Locked", retry: null);
                    break;
                case SealTakeStatus.ClockSuspected:
                    ShowSealTakeError("Seal_Take_ClockSuspected", retry: null);
                    break;
                case SealTakeStatus.ItemMissing:
                    RefreshItems();
                    ShowSealTakeError("Seal_Take_Missing", retry: null);
                    break;
                case SealTakeStatus.Conflict:

                    ShowSealTakeError("Seal_Conflict_Title",
                        () => ShowSealConflictDialog(item, directory, targetName ?? OriginNameOf(item), extractAsArchive));
                    break;
                case SealTakeStatus.TargetUnavailable:
                    ShowSealTakeError("Seal_Take_TargetUnavailable", () => _ = BeginTakeAsync());



                    if (!string.IsNullOrEmpty(taken.TargetPath))
                    {
                        App.ShowToast(string.Format(App.GetString("Seal_Take_PartialLeft"), taken.TargetPath),
                            persistent: true);
                    }
                    break;
                case SealTakeStatus.Busy:
                    ShowSealTakeError("Seal_Take_Busy", () => _ = RunTakeAsync(item, directory, targetName, policy, extractAsArchive));
                    break;
                case SealTakeStatus.Cancelled:




                    App.ClearToast();
                    if (!string.IsNullOrEmpty(taken.TargetPath))
                    {
                        App.ShowToast(string.Format(App.GetString("Seal_Take_PartialLeft"), taken.TargetPath),
                            persistent: true);
                    }
                    break;
                case SealTakeStatus.ExtractFailed:

                    RefreshItems();
                    if (SealCapsuleView.Visibility == Visibility.Visible) RefreshCapsules();
                    ShowSealErrorDialog(
                        App.GetString("Seal_Take_ErrorTitle"),
                        string.Format(App.GetString("Seal_Take_ExtractFailed"), taken.TargetPath),
                        retryText: null, retry: null);
                    break;
                default:
                    ShowSealTakeError("Seal_Take_Failed", () => _ = RunTakeAsync(item, directory, targetName, policy, extractAsArchive));
                    break;
            }
        }
        catch (Exception ex)
        {
            CrashLogger.LogNote("SealTake", ex.Message);
            if (gen != _leaveGen)
            {
                App.ClearToast();
                _itemsDirty = true;
                _capsuleDirty = true;
                return;
            }
            ShowSealTakeError("Seal_Take_Failed", () => _ = RunTakeAsync(item, directory, targetName, policy, extractAsArchive));
        }
        finally
        {
            _takeInFlight = false;
            EndLongOp();
        }
    }




    private void ShowSealTakeError(string reasonKey, Action? retry)
        => ShowSealErrorDialog(
            App.GetString("Seal_Take_ErrorTitle"), App.GetString(reasonKey),
            App.GetString("Seal_Take_Retry"), retry);



    private void ShowSealErrorDialog(string title, string message, string? retryText, Action? retry)
    {
        App.ClearToast();
        SealTakeErrorTitle.Text = title;
        SealTakeErrorText.Text = message;
        SealTakeErrorCloseButton.Content = App.GetString("Common_Button_Close");
        SealTakeErrorRetryButton.Content = retryText ?? App.GetString("Seal_Take_Retry");
        SealTakeErrorRetryButton.Visibility = retry is null ? Visibility.Collapsed : Visibility.Visible;
        _errorRetry = retry;

        DialogDepth.VeilShow();
        DialogUi.ShowOverlay(SealRoot, _dialogGens, _dialogMap,
            SealTakeErrorOverlay, SealTakeErrorDialog, SealTakeErrorTransform, _dialogWidthFit);
        if (retry is null) SealTakeErrorCloseButton.Focus(FocusState.Programmatic);
        else SealTakeErrorRetryButton.Focus(FocusState.Programmatic);
    }

    private void SealTakeErrorScrim_Tapped(object sender, TappedRoutedEventArgs e) => HideSealTakeError();

    private void SealTakeErrorClose_Click(object sender, RoutedEventArgs e) => HideSealTakeError();

    private void SealTakeErrorRetry_Click(object sender, RoutedEventArgs e)
        => HideSealTakeError(_errorRetry);



    private void HideSealTakeError(Action? afterHide = null)
    {
        if (SealTakeErrorOverlay.Visibility != Visibility.Visible)
        {
            afterHide?.Invoke();
            return;
        }

        DialogDepth.VeilHide();
        DialogUi.HideOverlay(SealRoot, _dialogGens, _dialogMap,
            SealTakeErrorOverlay, SealTakeErrorDialog, SealTakeErrorTransform, () =>
            {
                _errorRetry = null;
                afterHide?.Invoke();
            });
    }



    private void ShowSealConflictDialog(SealedItem item, string directory, string name, bool extractAsArchive = false)
    {
        _takeItem = item;
        _takeDirectory = directory;
        _takeArchive = extractAsArchive;

        SealConflictTitle.Text = App.GetString("Seal_Conflict_Title");
        SealConflictNewNameLabel.Text = App.GetString("Seal_Conflict_NewName");
        SealConflictNameBox.Text = name;
        SealConflictCancelButton.Content = App.GetString("Common_Button_Cancel");
        SealConflictAutoButton.Content = App.GetString("Seal_Conflict_AutoNumber");
        SealConflictRenameButton.Content = App.GetString("Seal_Conflict_Rename");

        DialogDepth.VeilShow();
        DialogUi.ShowOverlay(SealRoot, _dialogGens, _dialogMap,
            SealConflictOverlay, SealConflictDialog, SealConflictTransform, _dialogWidthFit);
        SealConflictNameBox.Focus(FocusState.Programmatic);
    }

    private void SealConflictScrim_Tapped(object sender, TappedRoutedEventArgs e) => HideSealConflictDialog();

    private void SealConflictClose_Click(object sender, RoutedEventArgs e) => HideSealConflictDialog();

    private void SealConflictCancel_Click(object sender, RoutedEventArgs e) => HideSealConflictDialog();

    private async void SealConflictAuto_Click(object sender, RoutedEventArgs e)
    {
        if (_takeInFlight) return;
        if (_takeItem is not { } item || _takeDirectory is not { } directory) return;
        HideSealConflictDialog();
        await RunTakeAsync(item, directory, null, SealTakeConflictPolicy.AutoNumber, _takeArchive);
    }

    private async void SealConflictRename_Click(object sender, RoutedEventArgs e)
    {
        if (_takeInFlight) return;
        if (_takeItem is not { } item || _takeDirectory is not { } directory) return;

        var name = SealConflictNameBox.Text?.Trim() ?? string.Empty;
        if (!IsAcceptableFileName(name))
        {
            SealConflictNameBox.Focus(FocusState.Programmatic);
            return;
        }

        HideSealConflictDialog();
        await RunTakeAsync(item, directory, name, SealTakeConflictPolicy.Rename, _takeArchive);
    }

    private void HideSealConflictDialog()
    {
        if (SealConflictOverlay.Visibility != Visibility.Visible) return;
        DialogDepth.VeilHide();
        DialogUi.HideOverlay(SealRoot, _dialogGens, _dialogMap,
            SealConflictOverlay, SealConflictDialog, SealConflictTransform, () =>
            {
                _takeItem = null;
                _takeDirectory = null;
                _takeArchive = false;
            });
    }



    private void ShowSealReceipt(string message, string? path)
    {
        _receiptPath = path;
        SealReceiptTitle.Text = App.GetString("Seal_Receipt_Title");
        SealReceiptText.Text = message;
        SealReceiptCloseButton.Content = App.GetString("Common_Button_Close");
        SealReceiptOpenButton.Content = App.GetString("Seal_Receipt_OpenFolder");
        SealReceiptOpenButton.IsEnabled = !string.IsNullOrEmpty(path);

        DialogDepth.VeilShow();
        DialogUi.ShowOverlay(SealRoot, _dialogGens, _dialogMap,
            SealReceiptOverlay, SealReceiptDialog, SealReceiptTransform, _dialogWidthFit);
        SealReceiptCloseButton.Focus(FocusState.Programmatic);
    }

    private void SealReceiptScrim_Tapped(object sender, TappedRoutedEventArgs e) => HideSealReceipt();

    private void SealReceiptClose_Click(object sender, RoutedEventArgs e) => HideSealReceipt();

    private void SealReceiptOpen_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(_receiptPath)) return;
        OpenInExplorer(_receiptPath);
    }



    private static void OpenInExplorer(string path)
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = $"/select,\"{path}\"",
                UseShellExecute = true,
            });
        }
        catch (Exception ex)
        {
            CrashLogger.LogNote("SealOpenInExplorer", ex.Message);
            App.ShowToast(App.GetString("Common_Toast_Failed"), ToastTone.Error);
        }
    }

    private void HideSealReceipt()
    {
        if (SealReceiptOverlay.Visibility != Visibility.Visible) return;
        DialogDepth.VeilHide();
        DialogUi.HideOverlay(SealRoot, _dialogGens, _dialogMap,
            SealReceiptOverlay, SealReceiptDialog, SealReceiptTransform, () =>
            {
                _receiptPath = null;
            });
    }



    private IReadOnlyList<SealRecoveryEntry> _recoveryEntries = System.Array.Empty<SealRecoveryEntry>();
    private SealRecoveryEntry? _recoverySelected;
    private Button? _recoveryPrimaryButton;
    private SealRecoveryEntry? _recoveryRetryEntry;
    private bool _recoveryBusy;
    private bool _recoveryPrompted;
    private bool _recoveryRan;
    private bool _recoveryPassInFlight;
    private int _recoveryDeferred;
    private bool _recoveryAttention;
    private bool _recoveryMalformed;
    private bool _recoveryUnreadable;
    private Microsoft.UI.Xaml.Media.Animation.Storyboard? _recoveryBreath;





    public void RefreshRecovery()
    {
        if (!_recoveryRan)
        {


            if (!SealService.Current.IsUnlocked) return;
            _recoveryRan = true;
            _ = RunRecoveryPassAsync();
            return;
        }

        RefreshRecoveryEntriesOnly();
        BuildRecoveryBanner();
        PromptRecoveryOnce();
    }


    private async Task RunRecoveryPassAsync()
    {
        _recoveryPassInFlight = true;
        try
        {
            try
            {
                var run = await SealService.Current.RunAutomaticRecoveryAsync();
                if (run.Status == SealServiceStatus.Ok)
                {

                    _recoveryDeferred = run.Value.Deferred.Count;
                    _recoveryMalformed = run.Value.HasMalformedRecords;
                    _recoveryUnreadable = run.Value.JournalStatus != SealJournalStatus.Ok;
                    _recoveryAttention = _recoveryUnreadable || run.Value.HasMalformedRecords;
                }
                else if (run.Status == SealServiceStatus.NotUnlocked)
                {
                    _recoveryRan = false;
                    return;
                }
                else
                {
                    _recoveryAttention = true;
                    _recoveryMalformed = false;
                    _recoveryUnreadable = false;
                }
            }
            catch (Exception ex)
            {
                CrashLogger.LogNote("SealRecoveryPass", ex.GetType().Name);
                _recoveryAttention = true;
                _recoveryMalformed = false;
                _recoveryUnreadable = false;
            }
        }
        finally
        {
            _recoveryPassInFlight = false;
        }

        RefreshRecoveryEntriesOnly();
        BuildRecoveryBanner();
        PromptRecoveryOnce();
    }

    private void PromptRecoveryOnce()
    {
        if (_recoveryPrompted) return;
        if (_recoveryEntries.Count == 0 && !_recoveryAttention && _recoveryDeferred == 0) return;
        if (_takeInFlight || _destroyInFlight) return;
        if (IsRecoveryBlocked) return;

        _recoveryPrompted = true;
        ShowSealRecoveryDialog();
    }


    private bool IsRecoveryBlocked
        => _sealInFlight
            || SealPwdOverlay.Visibility == Visibility.Visible
            || SealDetailOverlay.Visibility == Visibility.Visible
            || SealSealOverlay.Visibility == Visibility.Visible
            || SealTakeErrorOverlay.Visibility == Visibility.Visible
            || SealConflictOverlay.Visibility == Visibility.Visible
            || SealReceiptOverlay.Visibility == Visibility.Visible
            || SealDestroyOverlay.Visibility == Visibility.Visible
            || SealFolderModeOverlay.Visibility == Visibility.Visible
            || SealClockResetOverlay.Visibility == Visibility.Visible
            || IsSettingsDialogOpen;



    private void BuildRecoveryBanner()
    {
        SealRecoveryBannerHost.Children.Clear();
        StopRecoveryBreath();

        var warnOnly = _recoveryEntries.Count == 0 && (_recoveryAttention || _recoveryDeferred > 0);
        if (_recoveryEntries.Count == 0 && !warnOnly)
        {
            SealRecoveryBannerHost.Visibility = Visibility.Collapsed;
            return;
        }

        var card = new Border
        {
            Height = 64,
            CornerRadius = new CornerRadius(12),
            Background = App.GetBrush("AppSurfaceOverlayBrush"),
            BorderBrush = new SolidColorBrush(App.GetBrush("AppBorderBrush").Color),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(20, 12, 20, 12),
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };

        var row = new Grid();
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var icon = BuildRecoveryIcon(32);
        icon.Margin = new Thickness(0, 0, 12, 0);
        row.Children.Add(icon);

        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        text.Children.Add(new TextBlock
        {
            Text = warnOnly
                ? App.GetString(_recoveryAttention ? "Seal_Rec_JournalWarn" : "Seal_Rec_AutoWarn")
                : string.Format(App.GetString("Seal_Rec_BannerTitle"), _recoveryEntries.Count),
            FontSize = 14,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Foreground = App.GetBrush("AppTextPrimaryBrush"),
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxLines = 1,
        });
        text.Children.Add(new TextBlock
        {
            Text = App.GetString("Seal_Rec_BannerSub"),
            FontSize = 12,
            Margin = new Thickness(0, 4, 0, 0),
            Foreground = App.GetBrush("AppTextSecondaryBrush"),
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxLines = 1,
        });
        Grid.SetColumn(text, 1);
        row.Children.Add(text);

        card.Child = row;
        card.Tapped += (_, e) => { e.Handled = true; ShowSealRecoveryDialog(); };
        AttachCardHover(card);


        var glow = new Border
        {
            CornerRadius = new CornerRadius(12),
            BorderBrush = App.GetBrush("AppPrimaryButtonBrush"),
            BorderThickness = new Thickness(1),
            IsHitTestVisible = false,
            Opacity = 0.25,
        };

        var host = new Grid();
        host.Children.Add(card);
        host.Children.Add(glow);
        SealRecoveryBannerHost.Children.Add(host);
        SealRecoveryBannerHost.Visibility = Visibility.Visible;

        if (App.IsAnimationsEnabled)
        {
            var breathe = new Microsoft.UI.Xaml.Media.Animation.DoubleAnimation
            {
                From = 0.25,
                To = 0.6,
                Duration = TimeSpan.FromSeconds(3),
                AutoReverse = true,
                RepeatBehavior = Microsoft.UI.Xaml.Media.Animation.RepeatBehavior.Forever,
            };
            _recoveryBreath = new Microsoft.UI.Xaml.Media.Animation.Storyboard { RepeatBehavior = Microsoft.UI.Xaml.Media.Animation.RepeatBehavior.Forever };
            Microsoft.UI.Xaml.Media.Animation.Storyboard.SetTarget(breathe, glow);
            Microsoft.UI.Xaml.Media.Animation.Storyboard.SetTargetProperty(breathe, "Opacity");
            _recoveryBreath.Children.Add(breathe);
            _recoveryBreath.Begin();
        }
    }

    private void StopRecoveryBreath()
    {
        _recoveryBreath?.Stop();
        _recoveryBreath = null;
    }


    private static Viewbox BuildRecoveryIcon(double size)
    {
        var canvas = new Grid { Width = 1024, Height = 1024 };
        Microsoft.UI.Xaml.Shapes.Path Layer(string d, Windows.UI.Color color) => new()
        {
            Data = App.CreateGeometry(d),
            Fill = new SolidColorBrush(color),
            Stretch = Stretch.None,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
        };
        canvas.Children.Add(Layer(IconData.SealReminderBell, Windows.UI.Color.FromArgb(0xFF, 0x4A, 0x5F, 0xE2)));
        canvas.Children.Add(Layer(IconData.SealReminderClapper, Windows.UI.Color.FromArgb(0xFF, 0xA1, 0xAF, 0xFF)));
        return new Viewbox
        {
            Width = size,
            Height = size,
            Stretch = Stretch.Uniform,
            VerticalAlignment = VerticalAlignment.Center,
            Child = canvas,
        };
    }



    public void ShowSealRecoveryDialog()
    {
        if (SealRecoveryOverlay.Visibility == Visibility.Visible) return;
        if (_takeInFlight || _destroyInFlight) return;
        if (IsRecoveryBlocked) return;

        SealRecoveryTitle.Text = App.GetString("Seal_Rec_Title");
        SealRecoveryIntro.Text = App.GetString("Seal_Rec_Intro");
        RefreshRecoveryEntriesOnly();


        if (_recoveryEntries.Count == 0 && !_recoveryAttention && _recoveryDeferred == 0)
        {
            BuildRecoveryBanner();
            return;
        }

        BuildRecoveryBody();

        DialogDepth.VeilShow();
        DialogUi.ShowOverlay(SealRoot, _dialogGens, _dialogMap,
            SealRecoveryOverlay, SealRecoveryDialog, SealRecoveryTransform, _dialogWidthFit);
    }

    private void RefreshRecoveryEntriesOnly()
    {
        var result = SealService.Current.GetRecoveryEntries();
        _recoveryEntries = result.Status == SealServiceStatus.Ok && result.Value is { } rows
            ? rows.Where(e => e.Action.NeedsUserDecision).ToList()
            : System.Array.Empty<SealRecoveryEntry>();
        EnsureRecoverySelection();
    }


    private void EnsureRecoverySelection()
    {
        if (_recoveryEntries.Count == 0)
        {
            _recoverySelected = null;
            return;
        }

        if (_recoverySelected is { } current
            && _recoveryEntries.Any(e => SameRecoveryEntry(e, current)))
            return;

        _recoverySelected = _recoveryEntries[0];
    }

    private static bool SameRecoveryEntry(SealRecoveryEntry a, SealRecoveryEntry b)
        => a.Action.Kind == b.Action.Kind
            && string.Equals(a.Action.OpId, b.Action.OpId, StringComparison.Ordinal)
            && string.Equals(a.Action.ItemId, b.Action.ItemId, StringComparison.Ordinal);

    private void BuildRecoveryBody()
    {
        EnsureRecoverySelection();
        SealRecoveryBody.Children.Clear();


        void AddWarning(string key)
        {
            SealRecoveryBody.Children.Add(new TextBlock
            {
                Text = App.GetString(key),
                FontSize = 12,
                TextWrapping = TextWrapping.Wrap,
                Foreground = App.GetBrush("AppDangerTextBrush"),
            });
        }

        if (_recoveryAttention) AddWarning("Seal_Rec_JournalWarn");
        if (_recoveryDeferred > 0) AddWarning("Seal_Rec_AutoWarn");
        if (_recoveryMalformed) AddMalformedCleanup();
        if (_recoveryUnreadable) AddUnreadableJournalCleanup();

        AddRecoverySection(SealRecoveryActionKind.OfferRetrySeal, "Seal_Rec_SecSeal");
        AddRecoverySection(SealRecoveryActionKind.StagingOnly, "Seal_Rec_SecStaging");
        AddRecoverySection(SealRecoveryActionKind.SourceLost, "Seal_Rec_SecLost");
        AddRecoverySection(SealRecoveryActionKind.OrphanSourceRemains, "Seal_Rec_SecOrphan");
        AddRecoverySection(SealRecoveryActionKind.BothSidesExist, "Seal_Rec_SecBoth");
        AddRecoverySection(SealRecoveryActionKind.AskCleanupPartialTarget, "Seal_Rec_SecPartial");

        BuildRecoveryFooter();
    }

    private void AddRecoverySection(SealRecoveryActionKind kind, string sectionKey)
    {
        var rows = _recoveryEntries.Where(e => e.Action.Kind == kind).ToList();
        if (rows.Count == 0) return;

        SealRecoveryBody.Children.Add(new TextBlock
        {
            Text = App.GetString(sectionKey),
            FontSize = 14,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Foreground = App.GetBrush("AppTextPrimaryBrush"),
        });

        foreach (var entry in rows) SealRecoveryBody.Children.Add(BuildRecoveryRow(entry));
    }



    private Button BuildRecoveryRow(SealRecoveryEntry entry)
    {
        var selected = _recoverySelected is { } current && SameRecoveryEntry(entry, current);
        var card = new Border
        {
            Padding = new Thickness(12, 10, 12, 10),
            CornerRadius = new CornerRadius(10),
            Background = App.GetBrush("AppSurfaceOverlayBrush"),
            BorderBrush = App.GetBrush(selected ? "AppPrimaryButtonBrush" : "AppBorderBrush"),
            BorderThickness = new Thickness(1),
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };

        var content = new Grid { ColumnSpacing = 8 };
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var text = new StackPanel { Spacing = 3 };
        if (!string.IsNullOrWhiteSpace(entry.DisplayName))
        {
            text.Children.Add(new TextBlock
            {
                Text = entry.DisplayName,
                FontSize = 14,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Foreground = App.GetBrush("AppTextPrimaryBrush"),
                TextTrimming = TextTrimming.CharacterEllipsis,
                MaxLines = 1,
            });
        }

        void AddPath(string? path)
        {
            if (string.IsNullOrWhiteSpace(path)) return;
            text.Children.Add(new TextBlock
            {
                Text = path,
                FontSize = 12,
                TextWrapping = TextWrapping.Wrap,
                Foreground = App.GetBrush("AppTextSecondaryBrush"),
            });
        }

        AddPath(entry.SourcePath);
        AddPath(entry.TargetPath);
        if (entry.Action.Kind == SealRecoveryActionKind.StagingOnly)
        {
            text.Children.Add(new TextBlock
            {
                Text = App.GetString("Seal_Rec_StagingWarn"),
                FontSize = 12,
                TextWrapping = TextWrapping.Wrap,
                Foreground = App.GetBrush("AppDangerTextBrush"),
                Margin = new Thickness(0, 4, 0, 0),
            });
        }
        content.Children.Add(text);

        if (selected)
        {
            var tag = new TextBlock
            {
                Text = App.GetString("Seal_Rec_Selected"),
                FontSize = 11,
                Foreground = App.GetBrush("AppPrimaryButtonBrush"),
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(8, 0, 0, 0),
            };
            Grid.SetColumn(tag, 1);
            content.Children.Add(tag);
        }

        card.Child = content;

        var button = new Button
        {
            Style = (Style)Resources["SealRecoveryRowButtonStyle"],
            Content = card,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            VerticalContentAlignment = VerticalAlignment.Stretch,
            IsTabStop = true,
        };
        var name = string.IsNullOrWhiteSpace(entry.DisplayName)
            ? App.GetString("Seal_Rec_Title")
            : entry.DisplayName;
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(
            button, selected ? name + " · " + App.GetString("Seal_Rec_Selected") : name);
        button.Click += (_, _) => SelectRecoveryEntry(entry);
        return button;
    }

    private void SelectRecoveryEntry(SealRecoveryEntry entry)
    {
        if (_recoveryBusy) return;
        if (_recoverySelected is { } current && SameRecoveryEntry(entry, current)) return;
        _recoverySelected = entry;
        BuildRecoveryBody();


        _recoveryPrimaryButton?.Focus(FocusState.Programmatic);
    }

    private enum RecoveryButtonTone { Primary, Outline, Danger }


    private void BuildRecoveryFooter()
    {
        SealRecoveryFooter.Children.Clear();
        SealRecoveryFooter.ColumnDefinitions.Clear();
        _recoveryPrimaryButton = null;

        if (_recoverySelected is not { } entry)
        {
            SealRecoveryFooter.Visibility = Visibility.Collapsed;
            return;
        }

        var actions = new List<(string Key, RecoveryButtonTone Tone, Action Run)>();
        switch (entry.Action.Kind)
        {
            case SealRecoveryActionKind.OfferRetrySeal:
                actions.Add(("Seal_Rec_DiscardStaging", RecoveryButtonTone.Outline,
                    () => DiscardOfferFragment(entry)));
                actions.Add(("Seal_Rec_RetrySeal", RecoveryButtonTone.Primary, () => RetrySeal(entry)));
                break;
            case SealRecoveryActionKind.StagingOnly:
                actions.Add(("Seal_Rec_DestroyStaging", RecoveryButtonTone.Danger,
                    () => _ = ApplyChoiceAsync(entry, SealRecoveryChoice.DiscardStaging)));


                actions.Add(("Seal_Rec_KeepStaging", RecoveryButtonTone.Primary, () => HideSealRecoveryDialog()));
                break;
            case SealRecoveryActionKind.SourceLost:
                actions.Add(("Seal_Rec_ClearRecord", RecoveryButtonTone.Primary,
                    () => _ = ApplyChoiceAsync(entry, SealRecoveryChoice.DiscardStaging)));
                break;
            case SealRecoveryActionKind.OrphanSourceRemains:
                actions.Add(("Seal_Rec_SourceGone", RecoveryButtonTone.Outline,
                    () => _ = ApplyChoiceAsync(entry, SealRecoveryChoice.SourceAlreadyGone)));
                actions.Add(("Seal_Rec_RetryDelete", RecoveryButtonTone.Primary,
                    () => _ = ApplyChoiceAsync(entry, SealRecoveryChoice.TryDeleteSource)));
                break;
            case SealRecoveryActionKind.BothSidesExist:
                actions.Add(("Seal_Rec_KeepTarget", RecoveryButtonTone.Outline,
                    () => _ = ApplyChoiceAsync(entry, SealRecoveryChoice.KeepTarget)));
                actions.Add(("Seal_Rec_KeepBoth", RecoveryButtonTone.Primary,
                    () => _ = ApplyChoiceAsync(entry, SealRecoveryChoice.KeepBoth)));
                actions.Add(("Seal_Rec_KeepBox", RecoveryButtonTone.Outline,
                    () => _ = ApplyChoiceAsync(entry, SealRecoveryChoice.KeepBoxEntry)));
                break;
            case SealRecoveryActionKind.AskCleanupPartialTarget:
                actions.Add(("Seal_Rec_CleanPartial", RecoveryButtonTone.Outline,
                    () => _ = ApplyChoiceAsync(entry, SealRecoveryChoice.CleanupPartialTarget)));
                actions.Add(("Seal_Rec_KeepPartial", RecoveryButtonTone.Primary,
                    () => _ = ApplyChoiceAsync(entry, SealRecoveryChoice.KeepPartialTarget)));
                break;
        }

        if (actions.Count == 0)
        {
            SealRecoveryFooter.Visibility = Visibility.Collapsed;
            return;
        }

        for (var i = 0; i < actions.Count; i++)
            SealRecoveryFooter.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        for (var i = 0; i < actions.Count; i++)
        {
            var (key, tone, run) = actions[i];
            var button = MakeRecoveryButton(key, tone, run);
            if (tone == RecoveryButtonTone.Primary) _recoveryPrimaryButton = button;
            Grid.SetColumn(button, i);
            button.HorizontalAlignment = actions.Count == 1
                ? HorizontalAlignment.Right
                : i == 0 ? HorizontalAlignment.Left
                : i == actions.Count - 1 ? HorizontalAlignment.Right
                : HorizontalAlignment.Center;
            SealRecoveryFooter.Children.Add(button);
        }

        SealRecoveryFooter.Visibility = Visibility.Visible;
    }

    private static Button MakeRecoveryButton(string textKey, RecoveryButtonTone tone, Action click)
    {
        var styleKey = tone switch
        {
            RecoveryButtonTone.Primary => "NovaraPrimaryButtonStyle",
            RecoveryButtonTone.Danger => "NovaraDangerButtonStyle",
            _ => "NovaraOutlineButtonStyle",
        };
        var button = new Button
        {
            Width = 160,
            Height = 40,
            CornerRadius = new CornerRadius(10),
            FontSize = 14,
            VerticalAlignment = VerticalAlignment.Center,
            Style = (Style)Application.Current.Resources[styleKey],
            Content = new TextBlock
            {
                Text = App.GetString(textKey),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            },
        };
        button.Click += (_, _) => click();
        return button;
    }



    private void DiscardOfferFragment(SealRecoveryEntry entry)
    {
        var source = entry.SourcePath;
        if (string.IsNullOrEmpty(source) || !(File.Exists(source) || Directory.Exists(source)))
        {
            App.ShowToast(App.GetString("Seal_Rec_RetrySourceMissing"), ToastTone.Error);
            RefreshRecoveryAfterChange();
            return;
        }
        _ = ApplyChoiceAsync(entry, SealRecoveryChoice.DiscardStaging);
    }

    private void RefreshRecoveryAfterChange()
    {
        RefreshRecoveryEntriesOnly();
        BuildRecoveryBanner();
        if (_recoveryEntries.Count == 0) HideSealRecoveryDialog();
        else BuildRecoveryBody();
    }



    private void AddMalformedCleanup()
    {
        SealRecoveryBody.Children.Add(new TextBlock
        {
            Text = App.GetString("Seal_Rec_MalformedWarn"),
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap,
            Foreground = App.GetBrush("AppDangerTextBrush"),
        });

        var button = MakeRecoveryButton("Seal_Rec_ClearMalformed", RecoveryButtonTone.Danger,
            () => _ = ClearMalformedRecordsAsync());
        button.HorizontalAlignment = HorizontalAlignment.Left;
        SealRecoveryBody.Children.Add(button);
    }



    private void AddUnreadableJournalCleanup()
    {
        var button = MakeRecoveryButton("Seal_Rec_ClearJournal", RecoveryButtonTone.Danger,
            () => _ = ClearUnreadableJournalAsync());
        button.HorizontalAlignment = HorizontalAlignment.Left;
        SealRecoveryBody.Children.Add(button);
    }

    private async Task ClearUnreadableJournalAsync()
    {
        if (_recoveryBusy) return;
        _recoveryBusy = true;
        try
        {
            var result = await Task.Run(() => SealService.Current.DiscardUnreadableJournal());
            if (result.Status != SealServiceStatus.Ok)
            {
                App.ShowToast(App.GetString("Seal_Rec_Failed"), ToastTone.Error);
                return;
            }

            _recoveryUnreadable = false;
            _recoveryAttention = false;
            App.ShowToast(App.GetString("Seal_Rec_JournalDone"));
            RefreshRecoveryAfterChange();
        }
        catch (Exception ex)
        {
            CrashLogger.LogNote("SealRecoveryJournal", ex.GetType().Name);
            App.ShowToast(App.GetString("Seal_Rec_Failed"), ToastTone.Error);
        }
        finally
        {
            _recoveryBusy = false;
        }
    }

    private async Task ClearMalformedRecordsAsync()
    {
        if (_recoveryBusy) return;
        _recoveryBusy = true;
        try
        {
            var result = await Task.Run(() => SealService.Current.DiscardMalformedJournalRecords());
            if (result.Status != SealServiceStatus.Ok)
            {
                App.ShowToast(App.GetString("Seal_Rec_Failed"), ToastTone.Error);
                return;
            }

            _recoveryMalformed = false;
            _recoveryAttention = false;
            App.ShowToast(App.GetString("Seal_Rec_MalformedDone"));
            RefreshRecoveryAfterChange();
        }
        catch (Exception ex)
        {
            CrashLogger.LogNote("SealRecoveryMalformed", ex.GetType().Name);
            App.ShowToast(App.GetString("Seal_Rec_Failed"), ToastTone.Error);
        }
        finally
        {
            _recoveryBusy = false;
        }
    }

    private async Task ApplyChoiceAsync(SealRecoveryEntry entry, SealRecoveryChoice choice)
    {
        if (_recoveryBusy) return;
        _recoveryBusy = true;
        try
        {
            var status = await SealService.Current.ApplyRecoveryAsync(entry.Action, choice);
            if (status != SealServiceStatus.Ok)
            {
                App.ShowToast(App.GetString("Seal_Rec_Failed"), ToastTone.Error);
                RefreshRecoveryAfterChange();
                return;
            }

            App.ShowToast(App.GetString("Seal_Rec_Done"));
            RefreshRecoveryEntriesOnly();
            RefreshItems();
            BuildRecoveryBanner();

            if (_recoveryEntries.Count == 0) HideSealRecoveryDialog();
            else BuildRecoveryBody();
        }
        catch (Exception ex)
        {
            CrashLogger.LogNote("SealRecovery", ex.GetType().Name);
            App.ShowToast(App.GetString("Seal_Rec_Failed"), ToastTone.Error);
            RefreshRecoveryAfterChange();
        }
        finally
        {
            _recoveryBusy = false;
        }
    }


    private void RetrySeal(SealRecoveryEntry entry)
    {
        var source = entry.SourcePath;
        if (string.IsNullOrEmpty(source) || !(File.Exists(source) || Directory.Exists(source)))
        {

            App.ShowToast(App.GetString("Seal_Rec_RetrySourceMissing"), ToastTone.Error);
            return;
        }

        _recoveryRetryEntry = entry;


        HideSealRecoveryDialog(() =>
        {
            ShowSealConfirmDialog(new[] { source });
            if (SealSealOverlay.Visibility != Visibility.Visible)
            {

                ShowSealRecoveryDialog();
            }
        });
    }

    private void SealRecoveryScrim_Tapped(object sender, TappedRoutedEventArgs e)
    {
        if (ReferenceEquals(e.OriginalSource, SealRecoveryScrim)) HideSealRecoveryDialog();
    }

    private void SealRecoveryClose_Click(object sender, RoutedEventArgs e) => HideSealRecoveryDialog();

    private void HideSealRecoveryDialog(Action? afterHide = null)
    {
        if (SealRecoveryOverlay.Visibility != Visibility.Visible)
        {
            afterHide?.Invoke();
            return;
        }
        DialogDepth.VeilHide();
        DialogUi.HideOverlay(SealRoot, _dialogGens, _dialogMap,
            SealRecoveryOverlay, SealRecoveryDialog, SealRecoveryTransform, () =>
            {
                SealRecoveryBody.Children.Clear();
                afterHide?.Invoke();
            });
    }



    private bool _clockResetInFlight;

    private void ShowSealClockResetDialog()
    {
        if (SealClockResetOverlay.Visibility == Visibility.Visible || _clockResetInFlight) return;
        DialogDepth.VeilShow();
        DialogUi.ShowOverlay(SealRoot, _dialogGens, _dialogMap,
            SealClockResetOverlay, SealClockResetDialog, SealClockResetTransform, _dialogWidthFit);
    }

    private void HideSealClockResetDialog()
    {
        if (SealClockResetOverlay.Visibility != Visibility.Visible) return;
        DialogDepth.VeilHide();
        DialogUi.HideOverlay(SealRoot, _dialogGens, _dialogMap,
            SealClockResetOverlay, SealClockResetDialog, SealClockResetTransform, () => { });
    }

    private void SealClockResetScrim_Tapped(object sender, TappedRoutedEventArgs e)
    {
        if (ReferenceEquals(e.OriginalSource, SealClockResetScrim)) HideSealClockResetDialog();
    }

    private void SealClockResetClose_Click(object sender, RoutedEventArgs e) => HideSealClockResetDialog();

    private void SealClockResetCancel_Click(object sender, RoutedEventArgs e) => HideSealClockResetDialog();

    private async void SealClockResetConfirm_Click(object sender, RoutedEventArgs e)
    {
        if (_clockResetInFlight) return;
        _clockResetInFlight = true;
        SealClockResetConfirm.IsEnabled = false;
        var generation = _leaveGen;
        try
        {
            var result = await Task.Run(() => SealService.Current.ResetClockBaseline());
            if (generation != _leaveGen) return;
            if (result.Status == SealServiceStatus.Ok)
            {
                CrashLogger.LogNote("SealClockReset", "the user reset the clock baseline");
                HideSealClockResetDialog();
                _capsuleDirty = true;
                if (SealCapsuleView.Visibility == Visibility.Visible) RefreshCapsules();
                App.ShowToast(App.GetString("Seal_ClockReset_Done"));
                return;
            }
            CrashLogger.LogNote("SealClockReset", result.Status.ToString());
        }
        catch (Exception ex)
        {
            CrashLogger.LogNote("SealClockReset", ex.GetType().Name);
        }
        finally
        {
            _clockResetInFlight = false;
            SealClockResetConfirm.IsEnabled = true;
        }
        App.ShowToast(App.GetString("Seal_Rec_Failed"), ToastTone.Error);
    }



    private bool _settingsDirty = true;
    private bool _usageEnabled = true;
    private bool _usageExpanded = true;
    private bool _hintExpanded;
    private bool _settingsInFlight;

    private Button _usageToggle = null!;
    private Button _usageExpandButton = null!;
    private Button _hintToggle = null!;
    private Button _hintEditButton = null!;
    private Grid _usagePanel = null!;
    private TextBlock _usageTotalValue = null!, _usageSpaceValue = null!, _usageCapsuleValue = null!, _usageUsedValue = null!;







    public void RefreshSettings()
    {
        _settingsDirty = false;
        SealSettingsContent.Children.Clear();


        var direct = SealService.Current.Mode == SealSessionMode.Direct;

        _usageEnabled = direct || (App.Store?.Database?.AppSettings?.UsageCardEnabled ?? true);


        _usageExpandButton = MakeIconCardButton(IconData.CardCollapse, () =>
        {
            _usageExpanded = !_usageExpanded;
            RefreshUsageVisual(animate: true);
        });
        if (!direct)
        {
            _usageToggle = MakeToggleCardButton(() => _usageEnabled, on =>
            {
                _usageEnabled = on;
                if (on) _usageExpanded = true;
                PersistUsageEnabled(on);
                ApplyToggle(_usageToggle, on);
                RefreshUsageVisual(animate: true);
                App.ShowToast(App.GetString("Common_Toast_Switched"));
            });
        }
        else
        {
            _usageToggle = null!;
        }
        SealSettingsContent.Children.Add(BuildUsageCard());


        if (direct)
        {
            SealSettingsContent.Children.Add(new TextBlock
            {
                Text = App.GetString("Seal_Set_DirectNotice"),
                FontSize = 13,
                Foreground = App.GetBrush("AppTextSecondaryBrush"),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(4, 0, 4, 8),
            });
        }
        else
        {

            SealSettingsContent.Children.Add(BuildSettingCard("Seal_Set_Password",
                MakeCardButton("Seal_Set_Change", () => { if (RequireSpaceInitialized()) ShowSealChpwDialog(); })));
        }


        if (!direct)
        {
            _hintToggle = MakeToggleCardButton(() => _hintExpanded, on =>
            {
                if (!RequireSpaceInitialized()) return;
                if (on) { ShowSealHintDialog(); return; }
                _ = ClearHintFromToggleAsync();
            });
            _hintEditButton = MakeCardButton("Seal_Set_Change",
                () => { if (RequireSpaceInitialized()) ShowSealHintDialog(); });
            _hintEditButton.Visibility = _hintExpanded ? Visibility.Visible : Visibility.Collapsed;

            SealSettingsContent.Children.Add(BuildSettingCard("Seal_Set_Hint", _hintEditButton, _hintToggle));
        }

        if (!direct)
        {

            SealSettingsContent.Children.Add(BuildSettingCard("Seal_Set_Export",
                MakeCardButton("Seal_Set_ExportBtn", () => { if (RequireSpaceInitialized()) ShowSealExportDialog(); })));


            SealSettingsContent.Children.Add(BuildSettingCard("Seal_Set_Import",
                MakeCardButton("Seal_Set_ImportBtn", () => { if (RequireSpaceInitialized()) ShowSealImportDialog(); })));
        }

        if (!direct)
        {


            SealSettingsContent.Children.Add(BuildSettingCard("Seal_ClockReset_Title",
                MakeCardButton("Seal_ClockReset_Open",
                    () => { if (RequireSpaceInitialized()) ShowSealClockResetDialog(); })));
        }


        SealSettingsContent.Children.Add(BuildSettingCard("Seal_Set_About",
            MakeCardButton("Seal_Set_AboutBtn", () => ShowSealAboutDialog())));


        SealSettingsContent.Children.Add(BuildSettingCard("Setting_Website_Title",
            MakePrimaryCardButton("Setting_Website_Open", OpenNovaraWebsite)));

        RefreshUsageVisual();
        RefreshHintState();
    }


    private static void OpenNovaraWebsite()
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("https://novara.xin") { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            CrashLogger.LogNote("SealPage.WebsiteCard", ex.Message);
            App.ShowToast(App.GetString("Connect_DeployHelp_Fail"), ToastTone.Error);
        }
    }


    private Button MakePrimaryCardButton(string textKey, Action click)
    {
        var button = new Button
        {
            Width = 160,
            Height = 40,
            CornerRadius = new CornerRadius(10),
            FontSize = 14,
            VerticalAlignment = VerticalAlignment.Center,
            Style = (Style)Application.Current.Resources["NovaraPrimaryButtonStyle"],
            Content = new TextBlock
            {
                Text = App.GetString(textKey),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            },
        };
        button.Click += (_, _) => click();
        return button;
    }


    private Border BuildSettingCard(string titleKey, params UIElement[] right)
    {
        var card = new Border
        {
            CornerRadius = new CornerRadius(12),
            Background = App.GetBrush("AppSurfaceOverlayBrush"),
            BorderBrush = App.GetBrush("AppBorderBrush"),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(20, 18, 20, 18),
        };

        card.Child = BuildCardHeader(titleKey, right);
        return card;
    }


    private Grid BuildCardHeader(string titleKey, params UIElement[] right)
    {
        var header = new Grid();
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var title = new TextBlock
        {
            Text = App.GetString(titleKey),
            FontSize = 16,
            Margin = new Thickness(4, 0, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = App.GetBrush("AppTextPrimaryBrush"),
        };
        header.Children.Add(title);

        if (right.Length > 0)
        {
            var host = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 12,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(-8, 0, 0, 0),
            };
            foreach (var element in right) host.Children.Add(element);
            Grid.SetColumn(host, 2);
            header.Children.Add(host);
        }

        return header;
    }



    private Button MakeCardButton(string textKey, Action click)
    {
        var button = new Button
        {
            Width = 160,
            Height = 40,
            CornerRadius = new CornerRadius(10),
            FontSize = 14,
            VerticalAlignment = VerticalAlignment.Center,
            Style = (Style)Application.Current.Resources["NovaraSurfaceButtonStyle"],
            Foreground = App.GetBrush("AppTextSecondaryBrush"),
            BorderBrush = App.GetBrush("AppBorderBrush"),
            Content = new TextBlock
            {
                Text = App.GetString(textKey),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            },
        };
        button.Click += (_, _) => click();
        return button;
    }


    private Button MakeIconCardButton(string path, Action click)
    {
        var icon = new PathIcon
        {
            Data = App.CreateGeometry(path),
            Foreground = App.GetBrush("IconForegroundBrush"),
        };
        var button = new Button
        {
            Width = 40,
            Height = 40,
            CornerRadius = new CornerRadius(10),
            VerticalAlignment = VerticalAlignment.Center,
            Style = (Style)Application.Current.Resources["NovaraIconButtonStyle"],
            Content = new Viewbox { Width = 18, Height = 18, Stretch = Stretch.Uniform, Child = icon },
        };

        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(button, App.GetString("A11y_CardToggle"));
        button.Click += (_, _) => click();
        return button;
    }





    private Button MakeToggleCardButton(Func<bool> get, Action<bool> set)
    {
        var button = new Button
        {
            Width = 160,
            Height = 40,
            CornerRadius = new CornerRadius(10),
            FontSize = 14,
            VerticalAlignment = VerticalAlignment.Center,
            BorderBrush = App.GetBrush("AppBorderBrush"),
            Content = new TextBlock { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
        };
        ApplyToggle(button, get());
        button.Click += (_, _) => ShowToggleMenu(button, get, set);
        return button;
    }

    private void ShowToggleMenu(Button button, Func<bool> get, Action<bool> set)
    {
        var current = get();
        var accent = App.GetBrush("AppTextPrimaryBrush");
        var normal = App.GetBrush("AppTextSecondaryBrush");

        MenuFlyoutItem Make(string key, bool active)
        {
            var item = new MenuFlyoutItem
            {
                Style = (Style)Application.Current.Resources["GlassMenuFlyoutItemStyle"],
                Text = App.GetString(key),
                Foreground = active ? accent : normal,
            };
            if (active) item.Icon = new FontIcon { Glyph = "\uE73E", FontSize = 12, Foreground = accent };
            return item;
        }

        var onItem = Make("Setting_Stats_On", current);
        var offItem = Make("Setting_Stats_Off", !current);

        onItem.Click += (_, _) => { if (!get()) set(true); };
        offItem.Click += (_, _) => { if (get()) set(false); };

        var menu = new MenuFlyout
        {
            MenuFlyoutPresenterStyle = (Style)Application.Current.Resources["GlassMenuFlyoutPresenterStyle"],
        };
        menu.Items.Add(onItem);
        menu.Items.Add(offItem);
        menu.ShowAt(button, new Windows.Foundation.Point(0, button.ActualHeight + 4));
    }

    private static Brush TextBrush(string key) => App.GetBrush(key)!;


    private void ApplyToggle(Button button, bool on)
    {
        button.Style = on
            ? (Style)Application.Current.Resources["NovaraPrimaryButtonStyle"]
            : (Style)Application.Current.Resources["NovaraSurfaceButtonStyle"];
        button.Background = on ? App.GetBrush("AppPrimaryButtonBrush") : App.GetBrush("AppSurfaceBrush");
        button.Foreground = on
            ? new SolidColorBrush(Windows.UI.Color.FromArgb(255, 255, 255, 255))
            : App.GetBrush("AppTextSecondaryBrush");
        if (button.Content is TextBlock text) text.Text = App.GetString(on ? "Setting_Stats_On" : "Setting_Stats_Off");
    }


    private Border BuildUsageCard()
    {
        var card = new Border
        {
            CornerRadius = new CornerRadius(12),
            Background = App.GetBrush("AppSurfaceOverlayBrush"),
            BorderBrush = App.GetBrush("AppBorderBrush"),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(20, 18, 20, 18),
        };

        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.Children.Add(BuildCardHeader("Seal_Set_Usage",
            _usageToggle is null ? new UIElement[] { _usageExpandButton } : new UIElement[] { _usageExpandButton, _usageToggle }));

        _usagePanel = new Grid { Margin = new Thickness(0, 16, 0, 0) };
        for (var c = 0; c < 3; c++)
            _usagePanel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        _usagePanel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        _usagePanel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        _usageTotalValue = AddUsageBlock(0, "Seal_Usage_Total");
        _usageSpaceValue = AddUsageBlock(1, "Seal_Usage_Space");
        _usageCapsuleValue = AddUsageBlock(2, "Seal_Usage_Capsule");
        _usageUsedValue = AddUsageBlock(3, "Seal_Usage_Used");

        Grid.SetRow(_usagePanel, 1);
        root.Children.Add(_usagePanel);
        card.Child = root;
        return card;
    }


    private TextBlock AddUsageBlock(int index, string captionKey)
    {
        const int cols = 3;
        var block = new Border
        {
            Padding = new Thickness(16, 14, 16, 14),
            Margin = new Thickness(index % cols == 0 ? 0 : 6, index / cols == 0 ? 0 : 6, index % cols == cols - 1 ? 0 : 6, 0),
            Background = App.GetBrush("AppSurfaceBrush"),
            CornerRadius = new CornerRadius(10),
        };

        var stack = new StackPanel { Spacing = 6 };
        var value = new TextBlock
        {
            Text = "—",
            FontSize = 22,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Foreground = App.GetBrush("AppPrimaryButtonBrush"),
        };
        stack.Children.Add(value);
        stack.Children.Add(new TextBlock
        {
            Text = App.GetString(captionKey),
            FontSize = 12,
            Foreground = App.GetBrush("AppTextSecondaryBrush"),
            TextTrimming = TextTrimming.CharacterEllipsis,
        });
        block.Child = stack;

        Grid.SetColumn(block, index % cols);
        Grid.SetRow(block, index / cols);
        _usagePanel.Children.Add(block);
        return value;
    }

    private void RefreshUsageVisual(bool animate = false)
    {
        if (_usageExpandButton?.Content is Viewbox box && box.Child is PathIcon icon)
            icon.Data = App.CreateGeometry(_usageExpanded ? IconData.CardCollapse : IconData.CardExpand);

        if (_usagePanel is null) return;
        if (_usageExpandButton is not null)
            _usageExpandButton.Visibility = _usageEnabled ? Visibility.Visible : Visibility.Collapsed;



        var show = _usageEnabled && _usageExpanded;
        if (animate) MeltAnim.Begin(_usagePanel, show, 16.0);
        else
        {
            if (show) _usagePanel.Visibility = Visibility.Visible;
            MeltAnim.SetInstant(_usagePanel, show, 16.0);
        }
        if (!show) return;

        var usage = SealService.Current.GetUsage();
        if (usage.Status == SealServiceStatus.Ok)
        {
            _usageTotalValue.Text = PathPropertyFormatter.FormatCount(usage.Value.Total);
            _usageSpaceValue.Text = PathPropertyFormatter.FormatCount(usage.Value.Space);
            _usageCapsuleValue.Text = PathPropertyFormatter.FormatCount(usage.Value.Capsules);
            _usageUsedValue.Text = PathPropertyFormatter.FormatSize(usage.Value.UsedBytes);
        }
        else
        {
            _usageTotalValue.Text = "—";
            _usageSpaceValue.Text = "—";
            _usageCapsuleValue.Text = "—";
            _usageUsedValue.Text = "—";
        }
    }


    private static void PersistUsageEnabled(bool value)
    {
        var store = App.Store;
        if (store is null) return;
        store.Database.AppSettings.UsageCardEnabled = value;
        _ = store.SaveAsync();
    }


    private static string HintWriteError(SealServiceStatus status)
        => App.GetString(status switch
        {
            SealServiceStatus.RequiresFullSession => "Seal_SealErr_FullSession",
            SealServiceStatus.Busy => "Seal_Space_Busy",
            _ => "Seal_Pwd_Err_Io",
        });


    private async Task ClearHintFromToggleAsync()
    {
        var gen = _leaveGen;
        var status = await SealService.Current.WriteHintAsync(null);
        if (gen != _leaveGen) return;
        if (status != SealServiceStatus.Ok)
        {
            App.ShowToast(HintWriteError(status), ToastTone.Error);
            return;
        }
        _hintExpanded = false;
        ApplyToggle(_hintToggle, false);
        _hintEditButton.Visibility = Visibility.Collapsed;
        App.ShowToast(App.GetString("Seal_Hint_Cleared"));
    }


    private void RefreshHintState()
    {
        var hint = SealService.Current.ReadHint();
        _hintExpanded = hint.Status == SealServiceStatus.Ok && !string.IsNullOrWhiteSpace(hint.Value);
        if (_hintToggle is null) return;
        ApplyToggle(_hintToggle, _hintExpanded);
        _hintEditButton.Visibility = _hintExpanded ? Visibility.Visible : Visibility.Collapsed;
    }


    private bool IsSettingsDialogOpen
        => SealChpwOverlay.Visibility == Visibility.Visible
            || SealHintOverlay.Visibility == Visibility.Visible
            || SealAboutOverlay.Visibility == Visibility.Visible
            || SealImportOverlay.Visibility == Visibility.Visible
            || SealExportOverlay.Visibility == Visibility.Visible
            || SealRecoveryOverlay.Visibility == Visibility.Visible;




    private static void SetSealPasswordError(PasswordBox box, bool failed)
    {

        if (failed) PasswordFeedback.ShowError(box);
        else PasswordFeedback.ClearError(box);
    }

    private void ShowSealChpwDialog()
    {
        SealChpwTitle.Text = App.GetString("Seal_Chpw_Title");
        SealChpwOldLabel.Text = App.GetString("Seal_Chpw_Old");
        SealChpwNewLabel.Text = App.GetString("Seal_Chpw_New");
        SealChpwConfirmLabel.Text = App.GetString("Seal_Chpw_Confirm");
        SealChpwCancelButton.Content = App.GetString("Common_Button_Cancel");
        SealChpwConfirmButton.Content = App.GetString("Seal_Pwd_Confirm");
        SealChpwOldBox.Password = string.Empty;
        SealChpwNewBox.Password = string.Empty;
        SealChpwConfirmBox.Password = string.Empty;
        SealChpwError.Visibility = Visibility.Collapsed;
        SetSealPasswordError(SealChpwOldBox, false);
        SealChpwStrength.Visibility = Visibility.Collapsed;
        SealChpwConfirmButton.IsEnabled = false;

        DialogDepth.VeilShow();
        DialogUi.ShowOverlay(SealRoot, _dialogGens, _dialogMap,
            SealChpwOverlay, SealChpwDialog, SealChpwTransform, _dialogWidthFit);
        SealChpwOldBox.Focus(FocusState.Programmatic);
    }

    private void SealChpwScrim_Tapped(object sender, TappedRoutedEventArgs e)
    {
        if (_settingsInFlight) return;
        if (ReferenceEquals(e.OriginalSource, SealChpwScrim)) HideSealChpwDialog();
    }

    private void SealChpwClose_Click(object sender, RoutedEventArgs e)
    {
        if (_settingsInFlight) return;
        HideSealChpwDialog();
    }

    private void SealChpwCancel_Click(object sender, RoutedEventArgs e)
    {
        if (_settingsInFlight) return;
        HideSealChpwDialog();
    }



    private void ClearSealSettingsPlaintext()
    {
        SealChpwOldBox.Password = string.Empty;
        SealChpwNewBox.Password = string.Empty;
        SealChpwConfirmBox.Password = string.Empty;
        SetSealPasswordError(SealChpwOldBox, false);
        SealChpwError.Visibility = Visibility.Collapsed;
        SealHintBox.Text = string.Empty;
    }

    private void HideSealChpwDialog()
    {
        if (SealChpwOverlay.Visibility != Visibility.Visible) return;
        DialogDepth.VeilHide();
        DialogUi.HideOverlay(SealRoot, _dialogGens, _dialogMap,
            SealChpwOverlay, SealChpwDialog, SealChpwTransform, () =>
            {
                SealChpwOldBox.Password = string.Empty;
                SealChpwNewBox.Password = string.Empty;
                SealChpwConfirmBox.Password = string.Empty;
                SetSealPasswordError(SealChpwOldBox, false);
                SealChpwError.Visibility = Visibility.Collapsed;
            });
    }

    private void SealChpwBox_Changed(object sender, RoutedEventArgs e)
    {
        SetSealPasswordError(SealChpwOldBox, false);
        if (SealChpwStrength is null) return;
        var pw = SealChpwNewBox.Password ?? string.Empty;
        SealChpwStrength.Visibility = pw.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
        if (pw.Length > 0)
        {
            switch (SealPasswordPolicy.Rate(pw))
            {
                case BackupPasswordStrength.Strong:
                    SealChpwStrength.Text = App.GetString("Seal_Pwd_Strength_Strong");
                    SealChpwStrength.Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(0xFF, 0x4C, 0xAF, 0x50));
                    break;
                case BackupPasswordStrength.Medium:
                    SealChpwStrength.Text = App.GetString("Seal_Pwd_Strength_Medium");
                    SealChpwStrength.Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(0xFF, 0xFF, 0xD7, 0x00));
                    break;
                default:
                    SealChpwStrength.Text = App.GetString("Seal_Pwd_Strength_Weak");
                    SealChpwStrength.Foreground = TextBrush("AppDangerTextBrush");
                    break;
            }
        }

        var confirm = SealChpwConfirmBox.Password ?? string.Empty;
        var oldPw = SealChpwOldBox.Password ?? string.Empty;
        var (ok, issue) = SealPasswordPolicy.Evaluate(pw);
        var matched = pw.Length > 0 && pw == confirm;


        var sameAsOld = pw.Length > 0 && oldPw.Length > 0 && pw == oldPw;
        string? error = null;
        if (pw.Length > 0 && confirm.Length > 0 && !matched) error = App.GetString("Seal_Pwd_Err_Mismatch");
        else if (sameAsOld) error = App.GetString("Seal_Chpw_Err_SameAsOld");
        else if (pw.Length > 0 && !ok) error = IssueText(issue);
        SealChpwError.Text = error ?? string.Empty;
        SealChpwError.Visibility = error is null ? Visibility.Collapsed : Visibility.Visible;

        SealChpwConfirmButton.IsEnabled = !_settingsInFlight
            && oldPw.Length > 0 && ok && matched && !sameAsOld;
    }

    private async void SealChpwConfirm_Click(object sender, RoutedEventArgs e)
    {
        if (_settingsInFlight) return;
        var gen = _leaveGen;
        var oldPw = SealChpwOldBox.Password ?? string.Empty;
        var newPw = SealChpwNewBox.Password ?? string.Empty;
        if (oldPw.Length == 0 || newPw.Length == 0 || newPw != (SealChpwConfirmBox.Password ?? string.Empty)) return;
        if (oldPw == newPw) return;

        _settingsInFlight = true;
        SealChpwConfirmButton.IsEnabled = false;
        SealChpwCancelButton.IsEnabled = false;
        SealChpwConfirmButton.Content = App.GetString("Seal_Pwd_Busy");

        SealServiceStatus status;
        try
        {
            status = await Task.Run(() =>
            {

                if (PasswordService.Exists() && PasswordService.Verify(newPw))
                    return SealServiceStatus.SameAsMainPassword;
                return SealService.Current.ChangePasswordAsync(oldPw, newPw).GetAwaiter().GetResult();
            });
        }
        catch
        {
            status = SealServiceStatus.IoError;
        }

        _settingsInFlight = false;
        SealChpwCancelButton.IsEnabled = true;
        SealChpwConfirmButton.Content = App.GetString("Seal_Pwd_Confirm");


        if (gen != _leaveGen) return;

        if (status == SealServiceStatus.Ok)
        {
            HideSealChpwDialog();
            App.ShowToast(App.GetString("Seal_Chpw_Done"));
            _settingsDirty = true;
            return;
        }

        SealChpwError.Text = status switch
        {
            SealServiceStatus.WrongPassword => App.GetString("Seal_Chpw_Err_Old"),
            SealServiceStatus.SameAsMainPassword => App.GetString("Seal_Pwd_Err_SameAsMain"),
            SealServiceStatus.WeakPassword => App.GetString("Seal_Pwd_Err_TooFewClasses"),
            SealServiceStatus.VersionUnsupported => App.GetString("Seal_Pwd_Err_Unsupported"),
            SealServiceStatus.Busy => App.GetString("Seal_Space_Busy"),
            _ => App.GetString("Seal_Pwd_Err_Io"),
        };
        SealChpwError.Visibility = Visibility.Visible;
        SetSealPasswordError(SealChpwOldBox, status == SealServiceStatus.WrongPassword);
        SealChpwConfirmButton.IsEnabled = true;
    }



    private void ShowSealHintDialog()
    {
        SealHintTitle.Text = App.GetString("Seal_Hint_Title");
        SealHintBody.Text = App.GetString("Seal_Hint_Body");
        SealHintBody.Foreground = App.GetBrush("AppTextSecondaryBrush");
        SealHintClearButton.Content = App.GetString("Seal_Hint_Clear");
        SealHintSaveButton.Content = App.GetString("Common_Button_Save");
        SealHintBox.Text = SealService.Current.ReadHint().Value ?? string.Empty;

        DialogDepth.VeilShow();
        DialogUi.ShowOverlay(SealRoot, _dialogGens, _dialogMap,
            SealHintOverlay, SealHintDialog, SealHintTransform, _dialogWidthFit);
        SealHintBox.Focus(FocusState.Programmatic);
        SealHintBox.Select(SealHintBox.Text.Length, 0);
    }

    private void SealHintScrim_Tapped(object sender, TappedRoutedEventArgs e)
    {
        if (_settingsInFlight) return;
        if (ReferenceEquals(e.OriginalSource, SealHintScrim)) HideSealHintDialog();
    }

    private void SealHintClose_Click(object sender, RoutedEventArgs e)
    {
        if (_settingsInFlight) return;
        HideSealHintDialog();
    }

    private void HideSealHintDialog()
    {
        if (SealHintOverlay.Visibility != Visibility.Visible) return;
        DialogDepth.VeilHide();
        DialogUi.HideOverlay(SealRoot, _dialogGens, _dialogMap,
            SealHintOverlay, SealHintDialog, SealHintTransform, () =>
            {
                SealHintBox.Text = string.Empty;
            });
    }

    private async void SealHintSave_Click(object sender, RoutedEventArgs e)
    {
        if (_settingsInFlight) return;
        var gen = _leaveGen;

        var text = SealHintBox.Text?.Trim();

        if (text is not null && System.Text.Encoding.UTF8.GetByteCount(text) > SealSpaceStore.MaxHintBytes)
        {
            SealHintBody.Text = App.GetString("Seal_Hint_TooLong");
            SealHintBody.Foreground = App.GetBrush("AppDangerTextBrush");
            return;
        }

        _settingsInFlight = true;
        SealHintBody.Text = App.GetString("Seal_Hint_Body");
        SealHintBody.Foreground = App.GetBrush("AppTextSecondaryBrush");
        var status = await SealService.Current.WriteHintAsync(string.IsNullOrEmpty(text) ? null : text);
        _settingsInFlight = false;
        if (gen != _leaveGen) return;

        if (status != SealServiceStatus.Ok)
        {
            App.ShowToast(HintWriteError(status), ToastTone.Error);
            return;
        }

        HideSealHintDialog();
        App.ShowToast(App.GetString(string.IsNullOrEmpty(text) ? "Seal_Hint_Cleared" : "Seal_Hint_Saved"));
        RefreshHintState();
    }

    private async void SealHintClear_Click(object sender, RoutedEventArgs e)
    {
        if (_settingsInFlight) return;
        var gen = _leaveGen;
        _settingsInFlight = true;
        var status = await SealService.Current.WriteHintAsync(null);
        _settingsInFlight = false;
        if (gen != _leaveGen) return;
        if (status != SealServiceStatus.Ok)
        {
            App.ShowToast(HintWriteError(status), ToastTone.Error);
            return;
        }

        SealHintBox.Text = string.Empty;
        HideSealHintDialog();
        App.ShowToast(App.GetString("Seal_Hint_Cleared"));
        RefreshHintState();
    }



    private void ShowSealAboutDialog()
    {
        SealAboutTitle.Text = App.GetString("Seal_About_Title");

        SealAboutBody.Text = App.GetString("Seal_About_Body") + "\n\n" + App.GetString("Seal_Set_ClearNote");

        DialogDepth.VeilShow();
        DialogUi.ShowOverlay(SealRoot, _dialogGens, _dialogMap,
            SealAboutOverlay, SealAboutDialog, SealAboutTransform, _dialogWidthFit);
    }

    private void SealAboutScrim_Tapped(object sender, TappedRoutedEventArgs e)
    {
        if (ReferenceEquals(e.OriginalSource, SealAboutScrim)) HideSealAboutDialog();
    }

    private void SealAboutClose_Click(object sender, RoutedEventArgs e) => HideSealAboutDialog();

    private void HideSealAboutDialog()
    {
        if (SealAboutOverlay.Visibility != Visibility.Visible) return;
        DialogDepth.VeilHide();
        DialogUi.HideOverlay(SealRoot, _dialogGens, _dialogMap,
            SealAboutOverlay, SealAboutDialog, SealAboutTransform, () => { });
    }









    private enum ImportStep { PickFile, BundlePassword, LocalPassword, Preflight, Progress, Receipt }

    private ImportStep _importStep = ImportStep.PickFile;
    private bool _importInFlight;
    private string? _importPath;
    private SealBundlePeek? _importPeek;
    private string _importBundlePassword = "";
    private string _importLocalPassword = "";
    private string _importLocalPasswordConfirm = "";
    private SealImportResult? _importResult;
    private string? _importBackupExportPath;
    private string? _importFailureText;
    private CancellationTokenSource? _importCts;
    private SizeChangedEventHandler? _importStepsFit;





    public void ShowSealImportDialog()
    {
        if (SealImportOverlay.Visibility == Visibility.Visible) return;
        if (_importInFlight || _sealInFlight || _takeInFlight || _destroyInFlight) return;
        if (_recoveryPassInFlight) { RefuseWhileRecovering(); return; }
        if (IsSettingsDialogOpen || SealDetailOverlay.Visibility == Visibility.Visible
            || SealPwdOverlay.Visibility == Visibility.Visible) return;
        if (SealService.Current.Mode == SealSessionMode.Direct)
        {
            App.ShowToast(App.GetString("Seal_SealErr_FullSession"), ToastTone.Error);
            return;
        }

        _importPath = null;
        _importPeek = null;
        _importBundlePassword = "";
        _importLocalPassword = "";
        _importLocalPasswordConfirm = "";
        _importResult = null;
        _importInFlight = false;



        var plan = SealService.Current.GetRecoveryPlan();
        if (plan.Status == SealServiceStatus.Ok && plan.Value is { HasPendingDecisions: true })
        {
            App.ShowToast(App.GetString("Seal_SealErr_PendingRecovery"), ToastTone.Error);
            ShowSealRecoveryDialog();
            return;
        }

        SealImportTitle.Text = App.GetString("Seal_Imp_Title");
        SealImportCancelButton.Content = App.GetString("Common_Button_Cancel");

        DialogDepth.VeilShow();
        DialogUi.ShowOverlay(SealRoot, _dialogGens, _dialogMap,
            SealImportOverlay, SealImportDialog, SealImportTransform, _dialogWidthFit);

        GoToImportStep(ImportStep.PickFile);
    }

    private void SealImportScrim_Tapped(object sender, TappedRoutedEventArgs e)
    {
        if (_importInFlight) return;
        if (ReferenceEquals(e.OriginalSource, SealImportScrim)) HideSealImportDialog();
    }

    private void SealImportClose_Click(object sender, RoutedEventArgs e)
    {
        if (_importInFlight) return;
        HideSealImportDialog();
    }

    private void SealImportCancel_Click(object sender, RoutedEventArgs e)
    {


        if (_importInFlight)
        {
            _importCts?.Cancel();
            SealImportCancelButton.IsEnabled = false;
            return;
        }
        SealImportBack_Click();
    }

    private void HideSealImportDialog()
    {
        if (SealImportOverlay.Visibility != Visibility.Visible) return;


        var importedSomething = _importResult is { Counts.Imported: > 0 };

        _importCts?.Cancel();
        _importCts?.Dispose();
        _importCts = null;

        DialogDepth.VeilHide();
        DialogUi.HideOverlay(SealRoot, _dialogGens, _dialogMap,
            SealImportOverlay, SealImportDialog, SealImportTransform, () =>
            {

                _importBundlePassword = "";
                _importLocalPassword = "";
                _importLocalPasswordConfirm = "";
                _importPath = null;
                _importPeek = null;
                _importResult = null;
                _importFailureText = null;
                _importBackupExportPath = null;
                _importStep = ImportStep.PickFile;
                SealImportBody.Children.Clear();
                if (importedSomething)
                {
                    RefreshItems();
                    RefreshCapsules();
                    _settingsDirty = true;
                }


                if (!SealService.Current.IsInitialized())
                    App.MainWindow?.ReturnToSealWizardAfterRollback();
            });
    }


    private void GoToImportStep(ImportStep step)
    {
        _importStep = step;
        SealImportBody.Children.Clear();

        try
        {
            BuildImportSteps();

            switch (step)
            {
                case ImportStep.PickFile: BuildImportPickFile(); break;
                case ImportStep.BundlePassword: BuildImportBundlePassword(); break;
                case ImportStep.LocalPassword: BuildImportLocalPassword(); break;
                case ImportStep.Preflight: BuildImportPreflight(); break;
                case ImportStep.Progress: BuildImportProgress(); break;
                case ImportStep.Receipt: BuildImportReceipt(); break;
            }
        }
        catch (Exception ex)
        {




            CrashLogger.LogNote("SealImportStep", $"{ex.GetType().Name}: {ex.Message}");
            SealImportBody.Children.Clear();
            SealImportBody.Children.Add(new TextBlock
            {
                Text = App.GetString("Seal_Imp_StepFailed"),
                FontSize = 13,
                TextWrapping = TextWrapping.Wrap,
                Foreground = App.GetBrush("AppDangerTextBrush"),
            });
        }
    }











    private void BuildImportSteps()
    {
        var host = SealImportSteps;
        host.Children.Clear();
        host.RowDefinitions.Clear();
        host.ColumnDefinitions.Clear();

        var labels = new[]
        {
            App.GetString("Seal_Imp_Step_File"),
            App.GetString("Seal_Imp_Step_Password"),
            App.GetString("Seal_Imp_Step_Preflight"),
            App.GetString("Seal_Imp_Step_Done"),
        };

        var active = _importStep switch
        {
            ImportStep.PickFile => 0,
            ImportStep.BundlePassword or ImportStep.LocalPassword => 1,
            ImportStep.Preflight => 2,
            _ => 3,
        };

        var count = labels.Length;
        host.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        host.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        for (var i = 0; i < count; i++)
            host.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var idle = App.GetBrush("AppTextTertiaryBrush");
        var primary = App.GetBrush("AppTextPrimaryBrush");
        var surface = App.GetBrush("AppSurfaceBrush");



        var failed = _importStep == ImportStep.Receipt && _importFailureText is not null;
        var accent = failed ? App.GetBrush("AppDangerTextBrush") : App.GetBrush("AppPrimaryButtonBrush");
        var muted = failed ? App.GetBrush("AppDangerTextBrush") : idle;





        var track = new Microsoft.UI.Xaml.Shapes.Rectangle
        {
            Height = 2,
            Fill = muted,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        Grid.SetRow(track, 0);
        Grid.SetColumnSpan(track, count);
        host.Children.Add(track);

        var progress = new Microsoft.UI.Xaml.Shapes.Rectangle
        {
            Height = 2,
            Fill = accent,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Left,
            Visibility = active > 0 ? Visibility.Visible : Visibility.Collapsed,
        };
        Grid.SetRow(progress, 0);
        Grid.SetColumnSpan(progress, count);
        host.Children.Add(progress);

        void FitLine()
        {
            var width = host.ActualWidth;
            if (width <= 0) return;

            var column = width / count;
            var half = column / 2;


            track.Margin = new Thickness(half, 0, half, 0);


            progress.Margin = new Thickness(half, 0, 0, 0);
            progress.Width = Math.Max(0, column * active);
            progress.Visibility = active > 0 ? Visibility.Visible : Visibility.Collapsed;
        }


        if (_importStepsFit is { } previous) host.SizeChanged -= previous;
        SizeChangedEventHandler handler = (_, _) => FitLine();
        _importStepsFit = handler;
        host.SizeChanged += handler;

        for (var i = 0; i < count; i++)
        {
            var current = i == active;
            var passedDot = i < active;

            var dot = new Microsoft.UI.Xaml.Shapes.Ellipse
            {
                Width = current ? 14 : 10,
                Height = current ? 14 : 10,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Fill = current || passedDot ? accent : surface,
                Stroke = current || passedDot ? accent : muted,
                StrokeThickness = 2,
            };


            Grid.SetRow(dot, 0);
            Grid.SetColumn(dot, i);
            host.Children.Add(dot);

            var text = new TextBlock
            {
                Text = labels[i],
                FontSize = 12,
                TextAlignment = TextAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(0, 8, 0, 0),
                FontWeight = current ? Microsoft.UI.Text.FontWeights.SemiBold : Microsoft.UI.Text.FontWeights.Normal,
                Foreground = failed ? accent : current ? accent : passedDot ? primary : idle,
                TextWrapping = TextWrapping.NoWrap,
            };

            Grid.SetRow(text, 1);
            Grid.SetColumn(text, i);
            host.Children.Add(text);
        }

        FitLine();
    }






    private void SealImportFooterState(string cancelKey, string nextKey, bool nextEnabled)
    {

        var label = PreviousImportStep() is null ? cancelKey : "Seal_Imp_Prev";
        SealImportCancelButton.Content = App.GetString(label);
        SealImportCancelButton.IsEnabled = !_importInFlight;
        SealImportNextButton.Content = App.GetString(nextKey);
        SealImportNextButton.IsEnabled = nextEnabled && !_importInFlight;
    }


    private ImportStep? PreviousImportStep() => _importStep switch
    {
        ImportStep.BundlePassword => ImportStep.PickFile,
        ImportStep.LocalPassword => ImportStep.BundlePassword,

        ImportStep.Preflight => SealService.Current.IsInitialized()
            ? ImportStep.BundlePassword
            : ImportStep.LocalPassword,

        ImportStep.Receipt => _importFailureText is null ? null : ImportStep.Preflight,
        _ => null,
    };


    private void SealImportBack_Click()
    {
        if (_importInFlight) return;

        var previous = PreviousImportStep();
        if (previous is { } step)
        {

            if (step == ImportStep.Preflight) _importFailureText = null;
            GoToImportStep(step);
            return;
        }

        HideSealImportDialog();
    }


    private static StackPanel ImportSection(string titleKey, params UIElement[] body)    {
        var panel = new StackPanel { Spacing = 6 };

        panel.Children.Add(new TextBlock
        {
            Text = App.GetString(titleKey),
            FontSize = 13,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Foreground = App.GetBrush("AppTextPrimaryBrush"),
            TextWrapping = TextWrapping.Wrap,
        });

        foreach (var element in body) panel.Children.Add(element);

        return panel;
    }

    private static TextBlock ImportNote(string textKey)
        => new()
        {
            Text = App.GetString(textKey),
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap,
            Foreground = App.GetBrush("AppTextSecondaryBrush"),
        };










    private PasswordBox ImportPasswordBox(string placeholder)
    {
        var box = new PasswordBox
        {
            Height = 36,
            PlaceholderText = App.GetString(placeholder),
        };

        if (Application.Current.Resources.TryGetValue("NovaraPasswordBoxStyle", out var style)
            && style is Style passwordStyle)
            box.Style = passwordStyle;

        return box;
    }












    private void BuildImportPickFile()
    {
        SealImportBody.Children.Add(ImportSection("Seal_Imp_PickTitle", ImportNote("Seal_Imp_PickBody")));
        SealImportBody.Children.Add(_importPath is null ? BuildImportDropZone() : BuildImportChosenCard());
    }


    private Border BuildImportDropZone()
    {
        var idle = App.GetBrush("AppTextSecondaryBrush");
        var brand = App.GetBrush("AppPrimaryButtonBrush");


        var dashed = new Microsoft.UI.Xaml.Shapes.Rectangle
        {
            RadiusX = 12,
            RadiusY = 12,
            Stroke = idle,
            StrokeThickness = 2,
            StrokeDashArray = new DoubleCollection { 4, 3 },
        };

        var content = new StackPanel
        {
            Spacing = 8,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };

        content.Children.Add(new FontIcon
        {
            Glyph = "\uE8E5",
            FontSize = 24,
            HorizontalAlignment = HorizontalAlignment.Center,
            Foreground = idle,
        });

        content.Children.Add(new TextBlock
        {
            Text = App.GetString("Seal_Imp_Choose"),
            FontSize = 14,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            TextAlignment = TextAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center,
            Foreground = App.GetBrush("AppTextPrimaryBrush"),
        });


        content.Children.Add(new TextBlock
        {
            Text = App.GetString("Seal_Imp_DropHint"),
            FontSize = 12,
            TextAlignment = TextAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
            MaxWidth = 440,
            Foreground = App.GetBrush("AppTextTertiaryBrush"),
        });

        var layer = new Grid { MinHeight = 132 };
        layer.Children.Add(dashed);
        layer.Children.Add(content);

        var zone = new Border
        {
            CornerRadius = new CornerRadius(12),
            Background = App.GetBrush("AppSurfaceOverlayBrush"),
            Padding = new Thickness(24, 20, 24, 20),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            AllowDrop = true,
            Child = layer,
        };

        zone.Tapped += (_, _) => PickImportBundle();
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(zone, App.GetString("Seal_Imp_Choose"));


        zone.DragOver += (_, e) =>
        {
            var ok = e.DataView.Contains(StandardDataFormats.StorageItems);
            e.AcceptedOperation = ok ? DataPackageOperation.Copy : DataPackageOperation.None;
            e.Handled = true;
            dashed.Stroke = ok ? brand : idle;
            dashed.StrokeThickness = ok ? 3 : 2;
        };

        zone.DragLeave += (_, _) =>
        {
            dashed.Stroke = idle;
            dashed.StrokeThickness = 2;
        };

        zone.Drop += async (_, e) =>
        {
            dashed.Stroke = idle;
            dashed.StrokeThickness = 2;
            e.Handled = true;

            if (!e.DataView.Contains(StandardDataFormats.StorageItems)) return;

            var deferral = e.GetDeferral();
            try
            {
                var items = await e.DataView.GetStorageItemsAsync();
                var paths = items.Select(i => i.Path).Where(p => !string.IsNullOrEmpty(p)).ToList();

                if (paths.Count != 1)
                {

                    App.ShowToast(App.GetString("Seal_Imp_DropOneOnly"), ToastTone.Error);
                    return;
                }

                if (!paths[0].EndsWith(".novabundle", StringComparison.OrdinalIgnoreCase))
                {
                    App.ShowToast(App.GetString("Seal_Imp_DropWrongType"), ToastTone.Error);
                    return;
                }

                AcceptImportBundle(paths[0]);
            }
            catch (Exception ex)
            {
                CrashLogger.LogNote("SealImportDrop", ex.Message);
                App.ShowToast(App.GetString("Common_Toast_Failed"), ToastTone.Error);
            }
            finally
            {
                deferral.Complete();
            }
        };

        SealImportFooterState(cancelKey: "Common_Button_Cancel", nextKey: "Seal_Imp_Next", nextEnabled: false);
        return zone;
    }


    private Border BuildImportChosenCard()
    {
        var icon = new FontIcon
        {
            Glyph = "\uE7C3",
            FontSize = 20,
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = App.GetBrush("AppPrimaryButtonBrush"),
        };

        var name = new TextBlock
        {
            Text = Path.GetFileName(_importPath!),
            FontSize = 14,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Foreground = App.GetBrush("AppTextPrimaryBrush"),
            TextWrapping = TextWrapping.Wrap,
        };

        var summary = new TextBlock
        {
            Text = _importPeek is { } peek
                ? string.Format(
                    App.GetString("Seal_Imp_PickSummary"),
                    peek.ItemCount,
                    App.GetString(peek.HasBackup ? "Seal_Imp_KindTwo" : "Seal_Imp_KindOne"))
                : string.Empty,
            FontSize = 12,
            Margin = new Thickness(0, 3, 0, 0),
            Foreground = App.GetBrush("AppTextSecondaryBrush"),
            TextWrapping = TextWrapping.Wrap,
        };

        var texts = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        texts.Children.Add(name);
        texts.Children.Add(summary);



        var change = new TextBlock
        {
            Text = App.GetString("Seal_Imp_ChangeFile"),
            FontSize = 13,
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = App.GetBrush("AppPrimaryButtonBrush"),
        };
        change.Tapped += (_, _) => PickImportBundle();

        var row = new Grid { ColumnSpacing = 12 };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.Children.Add(icon);
        Grid.SetColumn(texts, 1);
        row.Children.Add(texts);
        Grid.SetColumn(change, 2);
        row.Children.Add(change);

        var card = new Border
        {
            CornerRadius = new CornerRadius(12),
            BorderThickness = new Thickness(1),
            BorderBrush = App.GetBrush("AppBorderBrush"),
            Background = App.GetBrush("AppSurfaceOverlayBrush"),
            Padding = new Thickness(18, 14, 18, 14),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Child = row,
        };

        SealImportFooterState(cancelKey: "Common_Button_Cancel", nextKey: "Seal_Imp_Next", nextEnabled: true);
        return card;
    }



    private void PickImportBundle()
    {
        string? path;
        try
        {
            path = FilePicker.PickFile("*.novabundle", App.GetString("Seal_Imp_Title"));
        }
        catch (Exception ex)
        {
            CrashLogger.LogNote("SealImportPick", ex.Message);
            App.ShowToast(App.GetString("Common_Toast_Failed"), ToastTone.Error);
            return;
        }

        if (string.IsNullOrEmpty(path)) return;

        AcceptImportBundle(path);
    }


    private void AcceptImportBundle(string path)
    {
        var peek = SealService.PeekBundle(path);
        if (peek.Status != SealBundleReadStatus.Ok || peek.Peek is null)
        {

            App.ShowToast(ImportReadErrorText(peek.Status), ToastTone.Error);
            return;
        }

        _importPath = path;
        _importPeek = peek.Peek;
        GoToImportStep(ImportStep.PickFile);
    }

    private static string ImportReadErrorText(SealBundleReadStatus status) => status switch
    {
        SealBundleReadStatus.VersionUnsupported => App.GetString("Seal_Imp_Err_Version"),
        SealBundleReadStatus.IoError => App.GetString("Seal_Imp_Err_Io"),
        _ => App.GetString("Seal_Imp_Err_Corrupt"),
    };

    private void BuildImportBundlePassword()
    {
        SealImportBody.Children.Add(ImportSection("Seal_Imp_PwTitle", ImportNote("Seal_Imp_PwBody")));


        if (_importPeek is { HasBackup: true })
        {
            SealImportBody.Children.Add(ImportNote("Seal_Imp_PwKindTwo"));
        }

        if (_importPeek is { } info)
        {
            SealImportBody.Children.Add(ImportNote(string.Format(
                App.GetString("Seal_Imp_PwMeta"),
                info.ItemCount,
                DateTimeOffset.FromUnixTimeSeconds(info.CreatedUtc).ToLocalTime().ToString("yyyy-MM-dd HH:mm"))));
        }

        var box = ImportPasswordBox("Seal_Imp_PwPlaceholder");
        box.Password = _importBundlePassword;
        box.PasswordChanged += (_, _) =>
        {
            _importBundlePassword = box.Password ?? string.Empty;
            SealImportNextButton.IsEnabled = _importBundlePassword.Length > 0;
        };
        SealImportBody.Children.Add(box);

        SealImportFooterState("Common_Button_Cancel", "Seal_Imp_Next", _importBundlePassword.Length > 0);
    }

    private void BuildImportLocalPassword()
    {
        SealImportBody.Children.Add(ImportSection("Seal_Imp_LocalTitle",
            ImportNote("Seal_Imp_LocalBody"), ImportNote("Seal_Imp_LocalNote")));

        var box = ImportPasswordBox("Seal_Imp_LocalPlaceholder");
        var confirm = ImportPasswordBox("Seal_Imp_LocalConfirm");
        box.Password = _importLocalPassword;
        confirm.Password = _importLocalPasswordConfirm;

        var error = new TextBlock
        {
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap,
            Foreground = App.GetBrush("AppDangerTextBrush"),
            Visibility = Visibility.Collapsed,
        };

        void Refresh()
        {
            var pw = box.Password ?? string.Empty;
            var twice = confirm.Password ?? string.Empty;
            var (ok, issue) = SealPasswordPolicy.Evaluate(pw);

            string? text = null;
            if (twice.Length > 0 && pw != twice) text = App.GetString("Seal_Pwd_Err_Mismatch");
            else if (pw.Length > 0 && !ok) text = IssueText(issue);

            error.Text = text ?? string.Empty;
            error.Visibility = text is null ? Visibility.Collapsed : Visibility.Visible;

            _importLocalPassword = pw;
            _importLocalPasswordConfirm = twice;
            SealImportNextButton.IsEnabled = ok && pw.Length > 0 && pw == twice;
        }

        box.PasswordChanged += (_, _) => Refresh();
        confirm.PasswordChanged += (_, _) => Refresh();

        SealImportBody.Children.Add(box);
        SealImportBody.Children.Add(confirm);
        SealImportBody.Children.Add(error);

        SealImportFooterState("Common_Button_Cancel", "Seal_Imp_Start", false);
        Refresh();

        box.Focus(FocusState.Programmatic);
    }





    private void BuildImportPreflight()
    {
        var initialized = SealService.Current.IsInitialized();
        var peek = _importPeek;

        SealImportBody.Children.Add(ImportSection("Seal_Imp_PreTitle"));

        var rows = new StackPanel { Spacing = 6 };
        void Row(string key, string value)
        {
            var row = new Grid();
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            row.Children.Add(new TextBlock
            {
                Text = App.GetString(key),
                FontSize = 13,
                Foreground = App.GetBrush("AppTextSecondaryBrush"),
                VerticalAlignment = VerticalAlignment.Center,
            });

            var valueText = new TextBlock
            {
                Text = value,
                FontSize = 13,
                Foreground = App.GetBrush("AppTextPrimaryBrush"),
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center,
                TextWrapping = TextWrapping.Wrap,
            };
            Grid.SetColumn(valueText, 1);
            row.Children.Add(valueText);

            rows.Children.Add(row);
        }

        Row("Seal_Imp_PreCount", (peek?.ItemCount ?? 0).ToString());
        Row("Seal_Imp_PreKind", App.GetString(peek is { HasBackup: true } ? "Seal_Imp_KindTwo" : "Seal_Imp_KindOne"));
        Row("Seal_Imp_PreMode", App.GetString(initialized ? "Seal_Imp_ModeExisting" : "Seal_Imp_ModeNew"));

        SealImportBody.Children.Add(rows);

        if (peek is { HasBackup: true })
        {

            SealImportBody.Children.Add(ImportNote("Seal_Imp_PreKindTwoNote"));
        }

        SealImportBody.Children.Add(ImportNote("Seal_Imp_PreNote"));

        SealImportFooterState("Common_Button_Cancel", "Seal_Imp_Confirm", true);
    }

    private void BuildImportProgress()
    {
        SealImportBody.Children.Add(ImportSection("Seal_Imp_ProgTitle"));

        var text = new TextBlock
        {
            Text = App.GetString("Seal_Imp_ProgWorking"),
            FontSize = 13,
            TextWrapping = TextWrapping.Wrap,
            Foreground = App.GetBrush("AppTextSecondaryBrush"),
        };
        SealImportBody.Children.Add(text);


        var bar = new ProgressBar
        {
            Height = 6,
            IsIndeterminate = true,
            Foreground = App.GetBrush("AppPrimaryButtonBrush"),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Margin = new Thickness(0, 8, 0, 0),
        };
        SealImportBody.Children.Add(bar);

        SealImportFooterState("Seal_Imp_Prev", "Seal_Imp_ProgWorking", false);
        SealImportCancelButton.IsEnabled = false;
    }

    private void BuildImportReceipt()
    {
        var result = _importResult;
        var failed = _importFailureText is not null;
        SealImportBody.Children.Add(ImportSection(failed ? "Seal_Imp_FailTitle" : "Seal_Imp_DoneTitle"));

        if (failed)
        {


            SealImportBody.Children.Add(new TextBlock
            {
                Text = _importFailureText,
                FontSize = 13,
                TextWrapping = TextWrapping.Wrap,
                Foreground = App.GetBrush("AppDangerTextBrush"),
            });

            SealImportBody.Children.Add(ImportNote("Seal_Imp_FailHint"));

            SealImportFooterState("Common_Button_Close", "Seal_Imp_ChooseAgain", true);
            return;
        }

        if (result is null) return;


        var counts = result.Counts;
        var rows = new StackPanel { Spacing = 6 };

        void Row(string key, int value, bool danger = false)
        {
            var row = new Grid();
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            row.Children.Add(new TextBlock
            {
                Text = App.GetString(key),
                FontSize = 13,
                Foreground = App.GetBrush("AppTextSecondaryBrush"),
                VerticalAlignment = VerticalAlignment.Center,
            });

            var valueText = new TextBlock
            {
                Text = value.ToString(),
                FontSize = 13,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Foreground = danger && value > 0
                    ? App.GetBrush("AppDangerTextBrush")
                    : App.GetBrush("AppTextPrimaryBrush"),
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center,
            };
            Grid.SetColumn(valueText, 1);
            row.Children.Add(valueText);

            rows.Children.Add(row);
        }

        Row("Seal_Imp_RecImported", counts.Imported);
        Row("Seal_Imp_RecSameName", counts.SameName);
        Row("Seal_Imp_RecSkipped", counts.Skipped);


        Row("Seal_Imp_RecFailed", result.FailedItems.Count, danger: true);
        SealImportBody.Children.Add(rows);


        if (counts.Conflict > 0)
        {
            SealImportBody.Children.Add(new TextBlock
            {
                Text = string.Format(App.GetString("Seal_Imp_RecConflict"), counts.Conflict),
                FontSize = 12,
                TextWrapping = TextWrapping.Wrap,
                Foreground = App.GetBrush("AppDangerTextBrush"),
            });
        }


        if (counts.Expired > 0)
        {
            SealImportBody.Children.Add(new TextBlock
            {
                Text = string.Format(App.GetString("Seal_Imp_RecExpired"), counts.Expired),
                FontSize = 12,
                TextWrapping = TextWrapping.Wrap,
                Foreground = App.GetBrush("AppTextSecondaryBrush"),
            });
        }


        SealImportBody.Children.Add(ImportNote("Seal_Imp_RecIntegrity"));


        if (result.HasBackup)
        {
            SealImportBody.Children.Add(ImportNote("Seal_Imp_RecBackupPending"));

            if (_importBackupExportPath is { Length: > 0 } backupPath)
            {
                SealImportBody.Children.Add(ImportNote("Seal_Imp_RecBackupWhere"));

                var openFolder = new Button
                {
                    Height = 40,
                    MinWidth = 160,
                    CornerRadius = new CornerRadius(10),
                    FontSize = 14,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Margin = new Thickness(0, 4, 0, 0),
                    Style = (Style)Application.Current.Resources["NovaraSurfaceButtonStyle"],
                    Foreground = App.GetBrush("AppTextSecondaryBrush"),
                    BorderBrush = App.GetBrush("AppBorderBrush"),
                    Content = new TextBlock
                    {
                        Text = App.GetString("Seal_Take_OpenFolder"),
                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Center,
                    },
                };
                openFolder.Click += (_, _) => OpenInExplorer(backupPath);
                SealImportBody.Children.Add(openFolder);

                SealImportBody.Children.Add(new TextBlock
                {
                    Text = backupPath,
                    FontSize = 12,
                    TextWrapping = TextWrapping.Wrap,
                    Foreground = App.GetBrush("AppTextTertiaryBrush"),
                });
            }
            else
            {

                SealImportBody.Children.Add(new TextBlock
                {
                    Text = App.GetString("Seal_Imp_RecBackupMissing"),
                    FontSize = 12,
                    TextWrapping = TextWrapping.Wrap,
                    Foreground = App.GetBrush("AppDangerTextBrush"),
                });
            }
        }

        SealImportFooterState("Common_Button_Close", "Seal_Imp_Done", true);
    }


    private static string ImportFailureText(SealImportStatus status) => status switch
    {
        SealImportStatus.WrongPassword => App.GetString("Seal_Imp_Err_WrongPassword"),
        SealImportStatus.Corrupt => App.GetString("Seal_Imp_Err_Corrupt"),
        SealImportStatus.VersionUnsupported => App.GetString("Seal_Imp_Err_Version"),
        SealImportStatus.WeakPassword => App.GetString("Seal_Pwd_Err_TooFewClasses"),
        SealImportStatus.SameAsMainPassword => App.GetString("Seal_Pwd_Err_SameAsMain"),
        SealImportStatus.NotUnlocked => App.GetString("Seal_SealErr_NotUnlocked"),
        _ => App.GetString("Seal_Imp_Err_Io"),
    };

    private void SealImportNext_Click(object sender, RoutedEventArgs e)
    {
        if (_importInFlight) return;

        switch (_importStep)
        {
            case ImportStep.PickFile:
                if (_importPath is null) return;
                GoToImportStep(ImportStep.BundlePassword);
                break;

            case ImportStep.BundlePassword:

                if (SealService.Current.IsInitialized() && !SealService.Current.IsUnlocked)
                {
                    App.ShowToast(App.GetString("Seal_SealErr_NotUnlocked"), ToastTone.Error);
                    return;
                }
                GoToImportStep(SealService.Current.IsInitialized() ? ImportStep.Preflight : ImportStep.LocalPassword);
                break;

            case ImportStep.LocalPassword:
                GoToImportStep(ImportStep.Preflight);
                break;

            case ImportStep.Preflight:
                _ = RunImportAsync();
                break;

            case ImportStep.Receipt:

                if (_importFailureText is not null)
                {
                    _importFailureText = null;
                    _importResult = null;
                    _importBundlePassword = "";
                    _importLocalPassword = "";
                    _importLocalPasswordConfirm = "";
                    _importPath = null;
                    _importPeek = null;
                    _importBackupExportPath = null;
                    GoToImportStep(ImportStep.PickFile);
                    return;
                }
                HideSealImportDialog();
                RefreshItems();
                RefreshCapsules();
                _settingsDirty = true;
                break;
        }
    }











    private async Task RunImportAsync()
    {
        if (_importInFlight || _importPath is null) return;

        _importInFlight = true;
        SealImportNextButton.IsEnabled = false;
        SealImportCancelButton.IsEnabled = true;
        GoToImportStep(ImportStep.Progress);

        var path = _importPath;
        var bundlePassword = _importBundlePassword;
        var localPassword = SealService.Current.IsInitialized() ? null : _importLocalPassword;
        var hasBackup = _importPeek is { HasBackup: true };

        _importCts?.Dispose();
        _importCts = new CancellationTokenSource();
        var token = _importCts.Token;

        SealImportResult? result = null;
        _importFailureText = null;

        try
        {


            var outcome = await Task.Run(
                () => SealService.Current.ImportAsync(path, bundlePassword, localPassword, token)
                    .GetAwaiter().GetResult(),
                token).ConfigureAwait(true);

            result = outcome.Value;

            if (outcome.Status != SealServiceStatus.Ok)
            {
                _importFailureText = outcome.Value is { Status: not SealImportStatus.Ok } failed
                    ? ImportFailureText(failed.Status)
                    : ImportServiceStatusText(outcome.Status);
            }
            else if (result is { Status: not SealImportStatus.Ok } bad)
            {
                _importFailureText = ImportFailureText(bad.Status);
            }


            if (_importFailureText is null && result is { HasBackup: true })
            {
                _importBackupExportPath = await ExportBundleBackupAsync(path, bundlePassword, token)
                    .ConfigureAwait(true);
            }
        }
        catch (OperationCanceledException)
        {
            _importInFlight = false;
            HideSealImportDialog();



            if (!SealService.Current.IsInitialized())
                App.MainWindow?.ReturnToSealWizardAfterRollback();
            return;
        }
        catch (Exception ex)
        {
            CrashLogger.LogNote("SealImport", $"{ex.GetType().Name}: {ex.Message}");
            _importFailureText = App.GetString("Seal_Imp_Err_Io");
            result = null;
        }

        _importInFlight = false;
        SealImportCancelButton.IsEnabled = true;




        if (_importFailureText is not null && !SealService.Current.IsInitialized())
        {
            HideSealImportDialog();
            App.MainWindow?.ReturnToSealWizardAfterRollback();
            return;
        }

        _importResult = result;
        GoToImportStep(ImportStep.Receipt);
    }


    private static string ImportServiceStatusText(SealServiceStatus status) => status switch
    {
        SealServiceStatus.Busy => App.GetString("Seal_Space_Busy"),
        SealServiceStatus.NotUnlocked => App.GetString("Seal_SealErr_NotUnlocked"),
        SealServiceStatus.RequiresFullSession => App.GetString("Seal_SealErr_FullSession"),
        SealServiceStatus.WeakPassword => App.GetString("Seal_Pwd_Err_TooFewClasses"),
        SealServiceStatus.SameAsMainPassword => App.GetString("Seal_Pwd_Err_SameAsMain"),
        SealServiceStatus.VersionUnsupported => App.GetString("Seal_Imp_Err_Version"),
        SealServiceStatus.Corrupt => App.GetString("Seal_Imp_Err_Corrupt"),
        SealServiceStatus.IndexFull => App.GetString("Seal_Imp_Err_IndexFull"),
        _ => App.GetString("Seal_Imp_Err_Io"),
    };



    private async Task<string?> ExportBundleBackupAsync(string bundlePath, string password, CancellationToken token)
    {
        try
        {
            var folder = Path.GetDirectoryName(bundlePath);
            if (string.IsNullOrEmpty(folder)) return null;

            var stem = Path.GetFileNameWithoutExtension(bundlePath);
            var target = Path.Combine(folder, stem + ".novaenc");

            for (var i = 1; File.Exists(target) && i <= 100; i++)
                target = Path.Combine(folder, $"{stem} ({i}).novaenc");

            var status = await SealService.Current
                .ExtractBundleBackupAsync(bundlePath, password, target, token)
                .ConfigureAwait(true);

            return status == SealServiceStatus.Ok ? target : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            CrashLogger.LogNote("SealImportBackup", ex.GetType().Name);
            return null;
        }
    }








    private bool _exportInFlight;

    public void ShowSealExportDialog()
    {
        if (SealExportOverlay.Visibility == Visibility.Visible) return;
        if (_exportInFlight || _sealInFlight || _takeInFlight || _destroyInFlight || _importInFlight) return;
        if (_recoveryPassInFlight) { RefuseWhileRecovering(); return; }
        if (IsSettingsDialogOpen || SealDetailOverlay.Visibility == Visibility.Visible
            || SealPwdOverlay.Visibility == Visibility.Visible) return;
        if (SealService.Current.Mode == SealSessionMode.Direct)
        {
            App.ShowToast(App.GetString("Seal_SealErr_FullSession"), ToastTone.Error);
            return;
        }


        if (!SealService.Current.IsUnlocked)
        {
            App.ShowToast(App.GetString("Seal_Exp_Err_NeedUnlock"), ToastTone.Error);
            return;
        }


        var plan = SealService.Current.GetRecoveryPlan();
        if (plan.Status == SealServiceStatus.Ok && plan.Value is { HasPendingDecisions: true })
        {
            App.ShowToast(App.GetString("Seal_Exp_Err_Pending"), ToastTone.Error);
            ShowSealRecoveryDialog();
            return;
        }

        SealExportTitle.Text = App.GetString("Seal_Exp_Title");
        SealExportBody.Text = App.GetString("Seal_Exp_Body");
        SealExportPasswordLabel.Text = App.GetString("Seal_Exp_PasswordLabel");
        SealExportNote1.Text = App.GetString("Seal_Exp_Note1");
        SealExportNote2.Text = App.GetString("Seal_Exp_Note2");
        SealExportCancelButton.Content = App.GetString("Common_Button_Cancel");
        SealExportConfirmButton.Content = App.GetString("Seal_Exp_Confirm");
        SealExportPasswordBox.Password = string.Empty;
        SealExportError.Visibility = Visibility.Collapsed;
        SetSealPasswordError(SealExportPasswordBox, false);
        SealExportConfirmButton.IsEnabled = false;

        DialogDepth.VeilShow();
        DialogUi.ShowOverlay(SealRoot, _dialogGens, _dialogMap,
            SealExportOverlay, SealExportDialog, SealExportTransform, _dialogWidthFit);
        SealExportPasswordBox.Focus(FocusState.Programmatic);
    }

    private void SealExportScrim_Tapped(object sender, TappedRoutedEventArgs e)
    {
        if (_exportInFlight) return;
        if (ReferenceEquals(e.OriginalSource, SealExportScrim)) HideSealExportDialog();
    }

    private void SealExportClose_Click(object sender, RoutedEventArgs e)
    {
        if (_exportInFlight) return;
        HideSealExportDialog();
    }

    private void SealExportCancel_Click(object sender, RoutedEventArgs e)
    {
        if (_exportInFlight) return;
        HideSealExportDialog();
    }

    private void HideSealExportDialog()
    {
        if (SealExportOverlay.Visibility != Visibility.Visible) return;

        DialogDepth.VeilHide();
        DialogUi.HideOverlay(SealRoot, _dialogGens, _dialogMap,
            SealExportOverlay, SealExportDialog, SealExportTransform, () =>
            {
                SealExportPasswordBox.Password = string.Empty;
                SetSealPasswordError(SealExportPasswordBox, false);
                SealExportError.Visibility = Visibility.Collapsed;
            });
    }

    private void SealExportPassword_Changed(object sender, RoutedEventArgs e)
    {
        SetSealPasswordError(SealExportPasswordBox, false);
        SealExportError.Visibility = Visibility.Collapsed;
        SealExportConfirmButton.IsEnabled = !_exportInFlight
            && (SealExportPasswordBox.Password ?? string.Empty).Length > 0;
    }

    private async void SealExportConfirm_Click(object sender, RoutedEventArgs e)
    {
        if (_exportInFlight) return;

        var password = SealExportPasswordBox.Password ?? string.Empty;
        if (password.Length == 0) return;

        var defaultName = string.Format("{0}_{1:yyyyMMdd}.novabundle",
            App.GetString("Seal_Exp_FilePrefix"), DateTime.Now);
        string? path;
        try
        {
            path = FilePicker.PickSaveFile(defaultName,
                new[] { (App.GetString("Seal_Exp_FilePrefix"), new[] { ".novabundle" }) },
                App.GetString("Seal_Exp_Title"));
        }
        catch (Exception ex)
        {
            CrashLogger.LogNote("SealExportPick", ex.Message);
            App.ShowToast(App.GetString("Common_Toast_Failed"), ToastTone.Error);
            return;
        }

        if (string.IsNullOrEmpty(path)) return;

        _exportInFlight = true;
        SealExportConfirmButton.IsEnabled = false;
        SealExportCancelButton.IsEnabled = false;
        SealExportError.Visibility = Visibility.Collapsed;


        var staging = path + ".partial";

        SealToast.Begin(App.GetString("Seal_Exp_Progress"));
        SealServiceStatus status;
        try
        {
            status = await Task.Run(() => SealService.Current
                .ExportAsync(staging, password, SealBundleWriter.KindItems)
                .GetAwaiter().GetResult()).ConfigureAwait(true);

            if (status == SealServiceStatus.Ok)
            {
                try
                {
                    File.Move(staging, path, overwrite: true);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    CrashLogger.LogNote("SealExportMove", ex.Message);
                    status = SealServiceStatus.IoError;
                }
            }
        }
        catch (Exception ex)
        {
            CrashLogger.LogNote("SealExport", ex.GetType().Name);
            status = SealServiceStatus.IoError;
        }
        finally
        {

            TryDeleteFile(staging);
            TryDeleteFile(staging + ".tmp");
            SealToast.Abort();
            _exportInFlight = false;
            SealExportCancelButton.IsEnabled = true;
        }

        if (status == SealServiceStatus.Ok)
        {
            HideSealExportDialog();
            App.ShowToast(App.GetString("Seal_Exp_Done"));
            return;
        }


        SealExportError.Text = status switch
        {
            SealServiceStatus.WrongPassword => App.GetString("Seal_Exp_Err_SpacePassword"),
            SealServiceStatus.NotUnlocked => App.GetString("Seal_Exp_Err_NeedUnlock"),
            SealServiceStatus.RequiresFullSession => App.GetString("Seal_SealErr_FullSession"),
            SealServiceStatus.Busy => App.GetString("Seal_Space_Busy"),
            SealServiceStatus.VersionUnsupported => App.GetString("Seal_Imp_Err_Version"),
            SealServiceStatus.Corrupt => App.GetString("Seal_Imp_Err_Corrupt"),
            _ => App.GetString("Seal_Imp_Err_Io"),
        };
        SealExportError.Visibility = Visibility.Visible;
        SetSealPasswordError(SealExportPasswordBox, status == SealServiceStatus.WrongPassword);
        SealExportConfirmButton.IsEnabled = (SealExportPasswordBox.Password ?? string.Empty).Length > 0;
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {

        }
    }



    private readonly System.Collections.Generic.Dictionary<string, Border> _cardById = new();
    private SealedItem? _destroyItem;
    private bool _destroyInFlight;
    private bool _folderTakeArchive;
    private bool _takeArchive;
    private TaskCompletionSource<bool?>? _folderModeTcs;



    public void ShowSealDestroyDialog(SealedItem item)
    {
        if (SealDestroyOverlay.Visibility == Visibility.Visible) return;
        if (_recoveryPassInFlight) { RefuseWhileRecovering(); return; }


        if (_sealInFlight) { RefuseWhileSealing(); return; }
        if (_takeInFlight)
        {
            App.ShowToast(App.GetString("Seal_Take_Busy"), ToastTone.Error);
            return;
        }

        _destroyItem = item;
        SealDestroyDangerIcon.Data = App.CreateGeometry(IconData.Danger);
        SealDestroyTitle.Text = App.GetString("Seal_Destroy_Title");
        SealDestroyTip.Text = App.GetString("Seal_Destroy_Tip");
        SealDestroyLockedTip.Text = App.GetString("Seal_Destroy_LockedTip");
        SealDestroyCancelButton.Content = App.GetString("Common_Button_Cancel");
        SealDestroyConfirmText.Text = App.GetString("Seal_Destroy_Title");


        var verdict = SealService.Current.CanTake(item.Id);
        var takeable = verdict.Status == SealServiceStatus.Ok && verdict.Value;
        SealDestroyLockedTip.Visibility = item.IsCapsule && !takeable ? Visibility.Visible : Visibility.Collapsed;

        DialogDepth.VeilShow();
        DialogUi.ShowOverlay(SealRoot, _dialogGens, _dialogMap,
            SealDestroyOverlay, SealDestroyDialog, SealDestroyTransform, _dialogWidthFit);



        SealDestroyConfirmButton.IsEnabled = false;
        SealDestroyConfirmButton.Opacity = Motion.CooldownDisabledOpacity;
        SealDestroyCd.Completed -= OnSealDestroyCdCompleted;
        SealDestroyCd.Completed += OnSealDestroyCdCompleted;
        SealDestroyCd.Start(Motion.CooldownSeconds);
        SealDestroyCancelButton.Focus(FocusState.Programmatic);
    }

    private void OnSealDestroyCdCompleted()
    {
        SealDestroyCd.Completed -= OnSealDestroyCdCompleted;
        SealDestroyConfirmButton.IsEnabled = true;
        SealDestroyConfirmButton.Opacity = 1;
    }

    private void SealDestroyScrim_Tapped(object sender, TappedRoutedEventArgs e)
    {
        if (ReferenceEquals(e.OriginalSource, SealDestroyScrim)) HideSealDestroyDialog();
    }

    private void SealDestroyClose_Click(object sender, RoutedEventArgs e) => HideSealDestroyDialog();

    private void SealDestroyCancel_Click(object sender, RoutedEventArgs e) => HideSealDestroyDialog();

    private async void SealDestroyConfirm_Click(object sender, RoutedEventArgs e)
    {
        if (_destroyInFlight || _destroyItem is not { } item) return;

        _destroyInFlight = true;
        try
        {
            var status = await SealService.Current.DestroyAsync(item.Id);
            if (status != SealServiceStatus.Ok)
            {


                App.ShowToast(App.GetString("Seal_Destroy_Failed"), ToastTone.Error);
                SealDestroyConfirmButton.IsEnabled = true;
                SealDestroyConfirmButton.Opacity = 1;
                return;
            }

            HideSealDestroyDialog();
            HideSealedItemDetail();
            App.ShowToast(App.GetString("Seal_Destroy_Done"));
            PlayCardExit(item.Id);
        }
        catch (Exception ex)
        {
            CrashLogger.LogNote("SealDestroy", ex.Message);


            SealDestroyConfirmButton.IsEnabled = true;
            SealDestroyConfirmButton.Opacity = 1;
            App.ShowToast(App.GetString("Seal_Destroy_Failed"), ToastTone.Error);
        }
        finally
        {
            _destroyInFlight = false;
        }
    }

    private void HideSealDestroyDialog()
    {
        if (SealDestroyOverlay.Visibility != Visibility.Visible) return;
        SealDestroyCd.Stop();
        var cdGen = SealDestroyCd.Generation;
        DialogDepth.VeilHide();
        DialogUi.HideOverlay(SealRoot, _dialogGens, _dialogMap,
            SealDestroyOverlay, SealDestroyDialog, SealDestroyTransform, () =>
            {
                SealDestroyCd.ResetAfterHide(cdGen);
                _destroyItem = null;
            });
    }


    private void PlayCardExit(string itemId)
    {
        if (_cardById.TryGetValue(itemId, out var card))
        {
            var fade = new Microsoft.UI.Xaml.Media.Animation.DoubleAnimation
            {
                To = 0,
                Duration = TimeSpan.FromMilliseconds(220),
                EasingFunction = new Microsoft.UI.Xaml.Media.Animation.CubicEase
                {
                    EasingMode = Microsoft.UI.Xaml.Media.Animation.EasingMode.EaseIn,
                },
            };
            var sb = new Microsoft.UI.Xaml.Media.Animation.Storyboard();
            Microsoft.UI.Xaml.Media.Animation.Storyboard.SetTarget(fade, card);
            Microsoft.UI.Xaml.Media.Animation.Storyboard.SetTargetProperty(fade, "Opacity");
            sb.Children.Add(fade);
            sb.Completed += (_, _) =>
            {
                _cardById.Remove(itemId);
                if (card.Parent is Panel panel) panel.Children.Remove(card);
                _itemsDirty = true;
                RefreshItems();

                if (SealCapsuleView.Visibility == Visibility.Visible) RefreshCapsules();
            };
            sb.Begin();
            return;
        }

        _itemsDirty = true;
        RefreshItems();
    }




    private async Task<bool?> AskFolderTakeModeAsync()
    {
        SealFolderModeTitle.Text = App.GetString("Seal_Folder_Mode_Title");
        SealFolderSegRestoreText.Text = App.GetString("Seal_Folder_Mode_Restore");
        SealFolderSegArchiveText.Text = App.GetString("Seal_Folder_Mode_Archive");
        SealFolderModeCancelButton.Content = App.GetString("Common_Button_Cancel");
        SealFolderModeTakeButton.Content = App.GetString("Seal_Detail_Take");
        ApplyFolderMode(archive: false);

        _folderModeTcs = new TaskCompletionSource<bool?>();
        DialogDepth.VeilShow();
        DialogUi.ShowOverlay(SealRoot, _dialogGens, _dialogMap,
            SealFolderModeOverlay, SealFolderModeDialog, SealFolderModeTransform, _dialogWidthFit);


        SealFolderSeg.UpdateLayout();
        ApplyFolderMode(archive: false);
        SealFolderModeTakeButton.Focus(FocusState.Programmatic);

        return await _folderModeTcs.Task;
    }


    private void ApplyFolderMode(bool archive)
    {
        _folderTakeArchive = archive;

        var width = SealFolderSeg.ActualWidth;
        var half = width > 0 ? Math.Max(0, (width - 6) / 2) : 0;
        SealFolderSegThumb.Width = half;
        SealFolderSegThumbTransform.X = archive ? half : 0;

        var on = new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(0xFF, 0xFF, 0xFF, 0xFF));
        var off = App.GetBrush("AppTextSecondaryBrush");
        SealFolderSegRestoreText.Foreground = archive ? off : on;
        SealFolderSegArchiveText.Foreground = archive ? on : off;
        SealFolderSegRestoreText.FontWeight = archive ? Microsoft.UI.Text.FontWeights.Normal : Microsoft.UI.Text.FontWeights.SemiBold;
        SealFolderSegArchiveText.FontWeight = archive ? Microsoft.UI.Text.FontWeights.SemiBold : Microsoft.UI.Text.FontWeights.Normal;
    }

    private void SealFolderSegRestore_Click(object sender, RoutedEventArgs e) => ApplyFolderMode(archive: false);

    private void SealFolderSegArchive_Click(object sender, RoutedEventArgs e) => ApplyFolderMode(archive: true);

    private void SealFolderModeScrim_Tapped(object sender, TappedRoutedEventArgs e)
    {
        if (ReferenceEquals(e.OriginalSource, SealFolderModeScrim)) ResolveFolderMode(null);
    }

    private void SealFolderModeClose_Click(object sender, RoutedEventArgs e) => ResolveFolderMode(null);

    private void SealFolderModeCancel_Click(object sender, RoutedEventArgs e) => ResolveFolderMode(null);

    private void SealFolderModeTake_Click(object sender, RoutedEventArgs e) => ResolveFolderMode(_folderTakeArchive);

    private void ResolveFolderMode(bool? choice)
    {
        if (SealFolderModeOverlay.Visibility != Visibility.Visible) return;
        var tcs = _folderModeTcs;
        _folderModeTcs = null;
        DialogDepth.VeilHide();
        DialogUi.HideOverlay(SealRoot, _dialogGens, _dialogMap,
            SealFolderModeOverlay, SealFolderModeDialog, SealFolderModeTransform, () =>
            {
                tcs?.TrySetResult(choice);
            });
    }






    private Microsoft.UI.Dispatching.DispatcherQueueTimer? _dropWatchdog;

    private static bool IsStorageDrop(DragEventArgs e)
        => e.DataView.Contains(StandardDataFormats.StorageItems);



    private bool AnySealDialogVisible() => new[]
    {
        SealSealOverlay, SealDetailOverlay, SealPwdOverlay, SealChpwOverlay, SealHintOverlay,
        SealClockResetOverlay, SealDestroyOverlay, SealFolderModeOverlay, SealConflictOverlay,
        SealReceiptOverlay, SealTakeErrorOverlay, SealImportOverlay, SealExportOverlay,
        SealRecoveryOverlay, SealAboutOverlay,
    }.Any(o => o.Visibility == Visibility.Visible);

    private void SealRoot_DragOver(object sender, DragEventArgs e)
    {




        var accept = IsStorageDrop(e) && !_sealInFlight && !_takeInFlight && !_destroyInFlight
                     && !_recoveryPassInFlight
                     && !AnySealDialogVisible()
                     && !IsSettingsDialogOpen;
        e.AcceptedOperation = accept ? DataPackageOperation.Copy : DataPackageOperation.None;
        e.Handled = true;

        if (accept)
        {
            App.MainWindow?.ShowSealDropVeil();
            WatchDropEnds();
        }
        else
        {
            App.MainWindow?.HideSealDropVeil();



            if (IsStorageDrop(e) && (e.AcceptedOperation == DataPackageOperation.None))
                ShowSealDropRefusedOnce();
        }
    }

    private DateTime _dropRefusedShownAt = DateTime.MinValue;

    private void ShowSealDropRefusedOnce()
    {
        var now = DateTime.UtcNow;
        if ((now - _dropRefusedShownAt).TotalSeconds < 2) return;
        _dropRefusedShownAt = now;
        App.ShowToast(App.GetString(_sealInFlight ? "Seal_Seal_Busy" : "Seal_Space_Busy"), ToastTone.Error);
    }

    private void SealRoot_DragLeave(object sender, DragEventArgs e)
    {
        App.MainWindow?.HideSealDropVeil();
        _dropWatchdog?.Stop();
    }


    private void WatchDropEnds()
    {
        if (_dropWatchdog is null)
        {
            _dropWatchdog = DispatcherQueue.CreateTimer();
            _dropWatchdog.Interval = TimeSpan.FromMilliseconds(300);
            _dropWatchdog.IsRepeating = false;
            _dropWatchdog.Tick += (_, _) => App.MainWindow?.HideSealDropVeil();
        }

        _dropWatchdog.Stop();
        _dropWatchdog.Start();
    }

    private async void SealRoot_Drop(object sender, DragEventArgs e)
    {
        App.MainWindow?.HideSealDropVeil();
        _dropWatchdog?.Stop();
        e.Handled = true;

        if (!IsStorageDrop(e)) return;
        if (_recoveryPassInFlight) { RefuseWhileRecovering(); return; }
        if (_sealInFlight) return;
        if (_takeInFlight || _destroyInFlight) return;
        if (SealSealOverlay.Visibility == Visibility.Visible
            || SealDetailOverlay.Visibility == Visibility.Visible
            || SealPwdOverlay.Visibility == Visibility.Visible
            || IsSettingsDialogOpen) return;

        var deferral = e.GetDeferral();
        try
        {
            var items = await e.DataView.GetStorageItemsAsync();
            var paths = items
                .Select(item => item.Path)
                .Where(path => !string.IsNullOrEmpty(path))
                .ToList();

            var capsule = SealCapsuleView.Visibility == Visibility.Visible;
            if (paths.Count > 0) ShowSealConfirmDialog(paths, capsule);
        }
        catch (Exception ex)
        {
            CrashLogger.LogNote("SealDrop", ex.Message);
            App.ShowToast(App.GetString("Common_Toast_Failed"));
        }
        finally
        {
            deferral.Complete();
        }
    }



    private long _sealTotalBytes;
    private readonly List<(string Path, long Bytes, bool IsFolder, bool Measured)> _sealEntries = new();
    private bool _sealCapsule;
    private bool _sealExpiryValid = true;
    private bool _sealPreflightOk = true;
    private bool _sealInFlight;
    private bool _sealDialogOpening;


    private void RefuseWhileSealing()
    {
        App.ShowToast(App.GetString("Seal_Seal_Busy"), ToastTone.Error);
    }


    private void RefuseWhileRecovering()
    {
        App.ShowToast(App.GetString("Seal_Space_Busy"), ToastTone.Error);
    }


    public async void ShowSealConfirmDialog(IReadOnlyList<string> paths, bool capsuleByDefault = false)
    {
        if (paths.Count == 0 || _sealInFlight || _sealDialogOpening || SealSealOverlay.Visibility == Visibility.Visible) return;
        if (_takeInFlight || _destroyInFlight) return;
        if (_recoveryPassInFlight) { RefuseWhileRecovering(); return; }
        if (!EnsureSpaceInitialized()) return;
        if (SealDetailOverlay.Visibility == Visibility.Visible || SealPwdOverlay.Visibility == Visibility.Visible
            || SealReceiptOverlay.Visibility == Visibility.Visible || SealDestroyOverlay.Visibility == Visibility.Visible
            || SealConflictOverlay.Visibility == Visibility.Visible || SealRecoveryOverlay.Visibility == Visibility.Visible
            || SealClockResetOverlay.Visibility == Visibility.Visible || SealFolderModeOverlay.Visibility == Visibility.Visible
            || IsSettingsDialogOpen) return;

        var generation = _leaveGen;
        _sealDialogOpening = true;
        try
        {




            var precheck = await Task.Run(() =>
            {
                var ok = new List<string>();
                var rejected = new List<(string Path, SealRejectReason Reason)>();
                foreach (var path in paths)
                {
                    var (eligible, reason) = SealEligibility.Evaluate(path, SealSpacePaths.Default);
                    if (eligible) ok.Add(path);
                    else rejected.Add((path, reason));
                }
                return (Accepted: ok, Rejected: rejected);
            });
            if (generation != _leaveGen || !IsLoaded) return;

            if (precheck.Rejected.Count > 0)
            {
                var message = string.Join("\n", precheck.Rejected.Select(r =>
                    Path.GetFileName(r.Path.TrimEnd(Path.DirectorySeparatorChar)) + "：" + SealRejectText(r.Reason)));
                ShowSealErrorDialog(App.GetString("Seal_Seal_Title"), message, null, null);
                return;
            }

            var accepted = precheck.Accepted;
            ApplySealTexts();
            await Task.Run(() => MeasureSealPayload(accepted));
            if (generation != _leaveGen || !IsLoaded) return;
            BuildSealFileList(accepted);


            SealSealDisplayNamePanel.Visibility = accepted.Count == 1 ? Visibility.Visible : Visibility.Collapsed;
            SealSealDisplayNameBox.Text = accepted.Count == 1 ? Path.GetFileName(accepted[0]) : string.Empty;
            SealSealNoteBox.Text = string.Empty;
            SealSealExpiryCombo.SelectedIndex = 3;
            SealSealCustomValueBox.Text = string.Empty;
            SealSealCustomPanel.Visibility = Visibility.Collapsed;
            SealSealCustomUnitCombo.SelectedIndex = 2;
            SealSealExpiryError.Visibility = Visibility.Collapsed;
            SealSealExpiryError.Text = string.Empty;
            _sealExpiryValid = true;

            SetSealTarget(capsule: capsuleByDefault);
            UpdateSealPreflight();

            DialogDepth.VeilShow();
            DialogUi.ShowOverlay(SealRoot, _dialogGens, _dialogMap,
                SealSealOverlay, SealSealDialog, SealSealTransform, _dialogWidthFit);
            SealSealCancelButton.Focus(FocusState.Programmatic);
        }
        catch (Exception ex)
        {
            CrashLogger.LogNote("SealConfirm", ex.Message);
            App.ShowToast(App.GetString("Common_Toast_Failed"));
        }
        finally
        {
            _sealDialogOpening = false;
        }
    }

    private void ApplySealTexts()
    {
        SealSealTitle.Text = App.GetString("Seal_Seal_Title");

        SealSealSegSpaceIcon.Data = BuildSegmentGeometry(IconData.SealSpace);
        SealSealSegCapsuleIcon.Data = BuildSegmentGeometry(IconData.Capsule);
        SealSealSegSpaceIcon.Fill = App.GetBrush("AppTextSecondaryBrush");
        SealSealSegCapsuleIcon.Fill = App.GetBrush("AppTextSecondaryBrush");
        SealSealSegSpaceText.Text = App.GetString("Seal_Tab_Space");
        SealSealSegCapsuleText.Text = App.GetString("Seal_Tab_Capsule");
        SealSealDisplayNameLabel.Text = App.GetString("Seal_Seal_DisplayNameLabel");
        SealSealNoteLabel.Text = App.GetString("Seal_Seal_NoteLabel");
        SealSealFilesLabel.Text = App.GetString("Seal_Seal_FilesLabel");
        SealSealExpiryLabel.Text = App.GetString("Seal_Seal_ExpiryLabel");
        SealSealWarningText.Text = App.GetString("Seal_Seal_Warning");
        SealSealCancelButton.Content = App.GetString("Common_Button_Cancel");
        SealSealConfirmButton.Content = App.GetString("Seal_Seal_Confirm");

        var selected = SealSealExpiryCombo.SelectedIndex;
        SealSealExpiryCombo.Items.Clear();
        foreach (var key in new[]
                 {
                     "Seal_Seal_Expiry1M", "Seal_Seal_Expiry1H", "Seal_Seal_Expiry1D", "Seal_Seal_Expiry1Mo",
                     "Seal_Seal_Expiry1Y", "Seal_Seal_Expiry10Y", "Seal_Seal_Expiry30Y", "Seal_Seal_Expiry40Y",
                     "Seal_Seal_ExpiryCustom",
                 })
        {
            SealSealExpiryCombo.Items.Add(new ComboBoxItem { Content = App.GetString(key) });
        }


        SealSealExpiryCombo.SelectedIndex = selected < 0 ? 3 : selected;

        var selectedUnit = SealSealCustomUnitCombo.SelectedIndex;
        SealSealCustomUnitCombo.Items.Clear();
        foreach (var key in new[]
                 {
                     "Seal_Seal_UnitMinute", "Seal_Seal_UnitHour", "Seal_Seal_UnitDay",
                     "Seal_Seal_UnitMonth", "Seal_Seal_UnitYear",
                 })
        {
            SealSealCustomUnitCombo.Items.Add(new ComboBoxItem { Content = App.GetString(key) });
        }
        SealSealCustomUnitCombo.SelectedIndex = selectedUnit < 0 ? 2 : selectedUnit;
    }


    private static bool TryMeasurePath(string path, out long bytes, out bool isFolder)
    {
        bytes = 0;
        isFolder = false;
        try
        {
            if (Directory.Exists(path))
            {
                isFolder = true;
                long sum = 0;
                foreach (var file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
                    sum += new FileInfo(file).Length;
                bytes = sum;
                return true;
            }

            bytes = new FileInfo(path).Length;
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return false;
        }
    }



    private void BuildSealFileList(IReadOnlyList<string> paths)
    {
        SealSealFilesList.Children.Clear();
        const int shown = 3;

        for (var index = 0; index < paths.Count && index < shown; index++)
        {
            var entry = _sealEntries[index];
            var name = Path.GetFileName(entry.Path.TrimEnd(Path.DirectorySeparatorChar));

            var row = new Grid { Height = 36, ColumnSpacing = 10 };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var icon = SealIconFactory.Create(SealGlyphs.Detect(name, entry.IsFolder), 24);
            icon.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(icon, 0);
            row.Children.Add(icon);

            var nameText = new TextBlock
            {
                Text = name,
                FontSize = 13,
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = App.GetBrush("AppTextPrimaryBrush"),
                TextTrimming = TextTrimming.CharacterEllipsis,
                MaxLines = 1,
            };
            Grid.SetColumn(nameText, 1);
            row.Children.Add(nameText);

            var sizeText = new TextBlock
            {
                Text = entry.Measured ? PathPropertyFormatter.FormatSize(entry.Bytes) : EmptyValue,
                FontSize = 12,
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = App.GetBrush("AppTextTertiaryBrush"),
            };
            Grid.SetColumn(sizeText, 2);
            row.Children.Add(sizeText);

            SealSealFilesList.Children.Add(row);
        }

        if (paths.Count > shown)
        {
            SealSealFilesList.Children.Add(new TextBlock
            {
                Text = string.Format(App.GetString("Seal_Seal_MoreFiles"), paths.Count - shown),
                FontSize = 12,
                Foreground = App.GetBrush("AppTextTertiaryBrush"),
                Margin = new Thickness(0, 2, 0, 0),
            });
        }
    }



    private void MeasureSealPayload(IReadOnlyList<string> paths)
    {
        _sealEntries.Clear();
        long total = 0;

        foreach (var path in paths)
        {
            var measured = TryMeasurePath(path, out var bytes, out var isFolder);
            _sealEntries.Add((path, bytes, isFolder, measured));
            if (measured) total += SealPreflight.Estimate(bytes, isFolder);
        }

        _sealTotalBytes = total;
    }

    private void UpdateSealPreflight()
    {
        var result = SealPreflight.Check(SealSpacePaths.Default.Root, _sealTotalBytes);
        _sealPreflightOk = result.Ok;

        var needed = PathPropertyFormatter.FormatSize(result.Needed);
        var available = PathPropertyFormatter.FormatSize(result.Available);
        SealSealPreflightText.Text = string.Format(
            App.GetString(result.Ok ? "Seal_Seal_Preflight" : "Seal_Seal_PreflightTight"), needed, available);
        SealSealPreflightText.Foreground = App.GetBrush(result.Ok ? "AppTextSecondaryBrush" : "AppDangerTextBrush");

        UpdateSealConfirmEnabled();
    }

    private void UpdateSealConfirmEnabled()
        => SealSealConfirmButton.IsEnabled = _sealExpiryValid && _sealPreflightOk;


    private void SetSealTarget(bool capsule)
    {
        _sealCapsule = capsule;
        SealSealExpiryPanel.Visibility = capsule ? Visibility.Visible : Visibility.Collapsed;

        if (!capsule)
        {

            _sealExpiryValid = true;
            SealSealExpiryError.Visibility = Visibility.Collapsed;
            SealSealExpiryError.Text = string.Empty;
            SealSealCustomValueBox.Text = string.Empty;
            SealSealExpiryCombo.SelectedIndex = 3;
            UpdateSealConfirmEnabled();
        }

        var width = SealSealSegment.ActualWidth;
        var half = width > 0 ? Math.Max(0, (width - 6) / 2) : 0;
        var target = capsule ? half : 0;
        SealSealSegmentThumb.Width = half;

        if (App.IsAnimationsEnabled && half > 0)
        {
            var a = new Microsoft.UI.Xaml.Media.Animation.DoubleAnimation
            {
                To = target,
                Duration = TimeSpan.FromMilliseconds(200),
                EasingFunction = new Microsoft.UI.Xaml.Media.Animation.CubicEase
                {
                    EasingMode = Microsoft.UI.Xaml.Media.Animation.EasingMode.EaseOut,
                },
            };
            var sb = new Microsoft.UI.Xaml.Media.Animation.Storyboard();
            Microsoft.UI.Xaml.Media.Animation.Storyboard.SetTarget(a, SealSealSegmentThumbTransform);
            Microsoft.UI.Xaml.Media.Animation.Storyboard.SetTargetProperty(a, "X");
            sb.Children.Add(a);
            sb.Begin();
        }
        else
        {
            SealSealSegmentThumbTransform.X = target;
        }

        StyleSegment(SealSealSegSpaceText, SealSealSegSpaceIcon, !capsule);
        StyleSegment(SealSealSegCapsuleText, SealSealSegCapsuleIcon, capsule);

        if (capsule) UpdateSealExpiry();
    }

    private static void StyleSegment(TextBlock text, Microsoft.UI.Xaml.Shapes.Path icon, bool selected)
    {
        var brush = selected
            ? new SolidColorBrush(Windows.UI.Color.FromArgb(0xFF, 0xFF, 0xFF, 0xFF))
            : App.GetBrush("AppTextSecondaryBrush");
        text.Foreground = brush;
        icon.Fill = brush;
        text.FontWeight = selected
            ? Microsoft.UI.Text.FontWeights.SemiBold
            : Microsoft.UI.Text.FontWeights.Normal;
    }


    private static Geometry BuildSegmentGeometry(string[] paths)
    {
        var cv = Microsoft.UI.Xaml.Markup.XamlBindingHelper.ConvertValue;
        var group = new GeometryGroup { FillRule = FillRule.Nonzero };
        foreach (var path in paths)
            group.Children.Add((Geometry)cv(typeof(Geometry), path));
        return group;
    }

    private void SealSealSegSpace_Click(object sender, RoutedEventArgs e) => SetSealTarget(capsule: false);

    private void SealSealSegCapsule_Click(object sender, RoutedEventArgs e) => SetSealTarget(capsule: true);



    private static readonly long[] SealExpiryPresetSeconds =
    {
        60L,
        3600L,
        86400L,
        30L * 86400,
        365L * 86400,
        10L * 365 * 86400,
        30L * 365 * 86400,
        40L * 365 * 86400,
    };


    private static readonly long[] SealExpiryUnitSeconds =
    {
        60L, 3600L, 86400L, 30L * 86400, 365L * 86400,
    };

    private const long SealExpiryMinSeconds = 60L;
    private const long SealExpiryMaxSeconds = 40L * 365 * 86400;

    private void SealSealExpiry_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (SealSealExpiryCombo.SelectedIndex < 0) return;

        var custom = SealSealExpiryCombo.SelectedIndex == SealExpiryPresetSeconds.Length;
        SealSealCustomPanel.Visibility = custom ? Visibility.Visible : Visibility.Collapsed;
        if (!custom) SealSealCustomValueBox.Text = string.Empty;


        if (custom && string.IsNullOrWhiteSpace(SealSealCustomValueBox.Text))
        {
            _sealExpiryValid = false;
            SealSealExpiryError.Visibility = Visibility.Collapsed;
            SealSealExpiryText.Text = string.Empty;
            UpdateSealConfirmEnabled();
            return;
        }
        UpdateSealExpiry();
    }

    private void SealSealCustomValue_Changed(object sender, TextChangedEventArgs e) => UpdateSealExpiry();

    private void SealSealCustomUnit_Changed(object sender, SelectionChangedEventArgs e) => UpdateSealExpiry();


    private void UpdateSealExpiry()
    {
        if (!TryGetSealExpirySeconds(out var seconds))
        {
            _sealExpiryValid = false;
            SealSealExpiryError.Text = App.GetString("Seal_Seal_ExpiryRangeErr");
            SealSealExpiryError.Visibility = Visibility.Visible;
            SealSealExpiryText.Text = string.Empty;
            UpdateSealConfirmEnabled();
            return;
        }

        _sealExpiryValid = true;
        SealSealExpiryError.Visibility = Visibility.Collapsed;
        SealSealExpiryText.Text = string.Format(
            App.GetString("Seal_Seal_ExpiryAt"),
            DateTimeOffset.UtcNow.AddSeconds(seconds).ToLocalTime().ToString("yyyy-MM-dd HH:mm"));
        UpdateSealConfirmEnabled();
    }


    private bool TryGetSealExpirySeconds(out long seconds)
    {
        seconds = -1;
        var index = SealSealExpiryCombo.SelectedIndex;
        if (index >= 0 && index < SealExpiryPresetSeconds.Length)
        {
            seconds = SealExpiryPresetSeconds[index];
            return true;
        }


        if (!long.TryParse(SealSealCustomValueBox.Text?.Trim(), out var value) || value < 1) return false;
        var unit = SealSealCustomUnitCombo.SelectedIndex;
        if (unit < 0 || unit >= SealExpiryUnitSeconds.Length) unit = 2;
        if (value > SealExpiryMaxSeconds) return false;
        seconds = value * SealExpiryUnitSeconds[unit];
        return seconds is >= SealExpiryMinSeconds and <= SealExpiryMaxSeconds;
    }

    private void SealSealCancel_Click(object sender, RoutedEventArgs e) => HideSealConfirmDialog();

    private void SealSealScrim_Tapped(object sender, TappedRoutedEventArgs e) => HideSealConfirmDialog();

    private void SealSealClose_Click(object sender, RoutedEventArgs e) => HideSealConfirmDialog();


    private async void SealSealConfirm_Click(object sender, RoutedEventArgs e)
    {
        UpdateSealPreflight();
        if (!SealSealConfirmButton.IsEnabled || _sealInFlight) return;

        if (!TryGetSealExpirySeconds(out var seconds))
        {
            UpdateSealExpiry();
            return;
        }

        var requests = BuildSealRequests(seconds);
        if (requests.Count == 0) return;

        var recoveryRetry = _recoveryRetryEntry;
        _recoveryRetryEntry = null;
        HideSealConfirmDialog();
        await RunSealAsync(requests, recoveryRetry);
    }


    private List<SealRequest> BuildSealRequests(long seconds)
    {
        var single = _sealEntries.Count == 1;
        var displayName = SealSealDisplayNameBox.Text?.Trim();
        var note = SealSealNoteBox.Text?.Trim();
        var kind = _sealCapsule ? SealedItemKind.Capsule : SealedItemKind.Normal;


        long? expiry = _sealCapsule ? seconds : null;

        return _sealEntries.Select(entry => new SealRequest(
            entry.Path,
            single && !string.IsNullOrWhiteSpace(displayName) ? displayName : null,
            kind,
            expiry,
            string.IsNullOrWhiteSpace(note) ? null : note)).ToList();
    }


    private async Task RunSealAsync(IReadOnlyList<SealRequest> requests, SealRecoveryEntry? recoveryRetry = null)
    {
        if (requests.Count == 0 || _sealInFlight) return;

        _sealInFlight = true;
        var generation = _leaveGen;
        BeginLongOp();

        SealToast.Begin(string.Format(App.GetString("Seal_Seal_Progress"), 1, requests.Count));

        var progress = new Progress<SealProgress>(p =>
            SealToast.Update(string.Format(App.GetString("Seal_Seal_Progress"), p.Current, p.Total)));

        SealCallResult<IReadOnlyList<SealResult>> outcome;
        try
        {
            outcome = await SealService.Current.SealAsync(requests, progress, _longOpCts?.Token ?? default);
        }
        catch (Exception ex)
        {
            CrashLogger.LogNote("SealSeal", ex.GetType().Name);
            outcome = SealCallResult<IReadOnlyList<SealResult>>.Failed(SealServiceStatus.IoError);
        }
        finally
        {
            _sealInFlight = false;
            EndLongOp();
        }

        if (_longOpCancelled)
        {




            SealToast.Abort();
            var doneNow = outcome.Value?.Count(r => r.Committed && r.Error == SealSealError.None) ?? 0;


            if (generation != _leaveGen) { _itemsDirty = true; _capsuleDirty = true; }
            else RefreshItems();
            App.ShowToast(doneNow > 0
                ? string.Format(App.GetString("Seal_Seal_CancelledPartial"), doneNow)
                : App.GetString("Seal_SealErr_Cancelled"));
            return;
        }

        if (outcome.Status != SealServiceStatus.Ok || outcome.Value is null)
        {
            SealToast.Abort();

            if (generation != _leaveGen)
            {
                App.ShowToast(SealStatusText(outcome.Status), ToastTone.Error);
                return;
            }
            var retryWhole = outcome.Status is SealServiceStatus.NotUnlocked or SealServiceStatus.IoError;
            var resolveRecovery = outcome.Status == SealServiceStatus.Corrupt;
            var action = retryWhole
                ? (Action)(() => _ = RunSealAsync(requests))
                : resolveRecovery
                    ? () => ReopenRecoveryForSealFailure(generation)
                    : null;
            ShowSealErrorDialog(
                App.GetString("Seal_Seal_ErrorTitle"),
                SealStatusText(outcome.Status),
                action is null ? null : App.GetString(retryWhole ? "Seal_Seal_Retry" : "Seal_Seal_Resolve"),
                action);
            return;
        }

        var results = outcome.Value;
        var committed = results.Count(r => r.Committed && r.Error == SealSealError.None);
        var failures = BuildSealFailures(requests, results);
        var orphan = results.Any(r => r.Error == SealSealError.SourceDeleteFailed);
        var retryRequests = BuildRetryRequests(requests, results);

        if (failures.Count == 0)
        {
            var done = requests.Count == 1 && committed == 1
                ? string.Format(App.GetString("Seal_Seal_DoneOne"), SealRequestName(requests[0]))
                : string.Format(App.GetString("Seal_Seal_DoneMany"), committed);
            SealToast.Finish(done);
        }
        else
        {
            SealToast.Abort();
            var lines = new List<string>
            {
                committed > 0
                    ? string.Format(App.GetString("Seal_Seal_Partial"), committed, failures.Count)
                    : string.Format(App.GetString("Seal_Seal_AllFailed"), failures.Count),
            };
            lines.AddRange(failures);

            var retry = retryRequests.Count > 0
                ? (Action)(() => _ = RunSealAsync(retryRequests))
                : orphan
                    ? () => ReopenRecoveryForSealFailure(generation)
                    : null;
            if (generation == _leaveGen)
            {
                ShowSealErrorDialog(
                    App.GetString("Seal_Seal_ErrorTitle"),
                    string.Join("\n", lines),
                    retry is null ? null : App.GetString(retryRequests.Count > 0 ? "Seal_Seal_Retry" : "Seal_Seal_Resolve"),
                    retry);
            }
            else
            {

                App.ShowToast(lines[0], ToastTone.Error);
            }
        }



        if (recoveryRetry is not null && committed > 0)
        {
            try
            {
                var settled = await SealService.Current.ApplyRecoveryAsync(
                    recoveryRetry.Action, SealRecoveryChoice.DiscardStaging);
                if (settled != SealServiceStatus.Ok)
                    CrashLogger.LogNote("SealRecoveryRetry", settled.ToString());
            }
            catch (Exception ex)
            {
                CrashLogger.LogNote("SealRecoveryRetry", ex.GetType().Name);
            }
        }

        var changed = committed > 0 || orphan;
        if (generation == _leaveGen)
        {
            RefreshAfterSeal(changed);
        }
        else if (changed)
        {

            _itemsDirty = true;
            _capsuleDirty = true;
            _settingsDirty = true;
        }
    }


    private void ReopenRecoveryForSealFailure(int generation)
    {
        if (generation != _leaveGen) return;
        RefreshRecovery();
        ShowSealRecoveryDialog();
    }


    private void RefreshAfterSeal(bool changed)
    {
        if (!changed) return;

        if (SealSpaceView.Visibility == Visibility.Visible)
        {
            RefreshItems();
        }
        else if (SealCapsuleView.Visibility == Visibility.Visible)
        {
            RefreshCapsules();
            RefreshRecovery();
        }
        else
        {
            _itemsDirty = true;
            _capsuleDirty = true;
            _settingsDirty = true;
            RefreshRecovery();
        }
    }

    private static List<string> BuildSealFailures(IReadOnlyList<SealRequest> requests, IReadOnlyList<SealResult> results)
    {
        var lines = new List<string>();
        for (var i = 0; i < requests.Count; i++)
        {
            var result = i < results.Count ? results[i] : null;
            if (result is { Committed: true, Error: SealSealError.None }) continue;
            var reason = result is null ? App.GetString("Seal_SealErr_Cancelled") : SealErrorText(result);
            lines.Add(string.Format(App.GetString("Seal_Seal_ErrorItem"), SealRequestName(requests[i]), reason));
        }
        return lines;
    }

    private static List<SealRequest> BuildRetryRequests(
        IReadOnlyList<SealRequest> requests, IReadOnlyList<SealResult> results)
    {
        var list = new List<SealRequest>();
        for (var i = 0; i < requests.Count; i++)
        {
            var result = i < results.Count ? results[i] : null;
            if (result is { Committed: true, Error: SealSealError.None }) continue;
            if (result is null || IsRetryable(result)) list.Add(requests[i]);
        }
        return list;
    }

    private static bool IsRetryable(SealResult result) => result.Error switch
    {
        SealSealError.None => false,
        SealSealError.InvalidRequest or SealSealError.SourceMissing or SealSealError.SourceDeleteFailed => false,
        SealSealError.Rejected => result.Reject is SealRejectReason.InUse or SealRejectReason.Denied
            or SealRejectReason.Unreachable or SealRejectReason.ReadOnly,
        _ => true,
    };

    private static string SealRequestName(SealRequest request)
    {
        if (!string.IsNullOrWhiteSpace(request.DisplayName)) return request.DisplayName!;
        var trimmed = request.SourcePath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var name = Path.GetFileName(trimmed);
        return string.IsNullOrEmpty(name) ? request.SourcePath : name;
    }

    private static string SealErrorText(SealResult result)
    {
        if (result.Error == SealSealError.Rejected) return SealRejectText(result.Reject);
        return result.Error switch
        {
            SealSealError.InvalidRequest => App.GetString("Seal_SealErr_InvalidRequest"),
            SealSealError.SourceMissing => App.GetString("Seal_SealErr_SourceMissing"),
            SealSealError.NotEnoughSpace => App.GetString("Seal_SealErr_NotEnoughSpace"),
            SealSealError.VolumeUnreachable => App.GetString("Seal_SealErr_VolumeUnreachable"),
            SealSealError.PackFailed => App.GetString("Seal_SealErr_PackFailed"),
            SealSealError.WriteFailed => App.GetString("Seal_SealErr_WriteFailed"),
            SealSealError.VerifyFailed => App.GetString("Seal_SealErr_VerifyFailed"),
            SealSealError.ItemIdCollision => App.GetString("Seal_SealErr_ItemIdCollision"),
            SealSealError.IndexFailed => App.GetString("Seal_SealErr_IndexFailed"),
            SealSealError.SourceDeleteFailed => App.GetString("Seal_SealErr_SourceDeleteFailed"),
            SealSealError.Cancelled => App.GetString("Seal_SealErr_Cancelled"),
            _ => App.GetString("Seal_SealErr_WriteFailed"),
        };
    }

    private static string SealRejectText(SealRejectReason? reason) => reason switch
    {
        SealRejectReason.ReparsePoint => App.GetString("Seal_Reject_ReparsePoint"),
        SealRejectReason.CloudPlaceholder => App.GetString("Seal_Reject_CloudPlaceholder"),
        SealRejectReason.PipeOrDevice => App.GetString("Seal_Reject_PipeOrDevice"),
        SealRejectReason.ReservedName => App.GetString("Seal_Reject_ReservedName"),
        SealRejectReason.PathTooLong => App.GetString("Seal_Reject_PathTooLong"),
        SealRejectReason.InsideSealRoot => App.GetString("Seal_Reject_InsideSealRoot"),
        SealRejectReason.ContainsSealRoot => App.GetString("Seal_Reject_ContainsSealRoot"),
        SealRejectReason.NotFound => App.GetString("Seal_Reject_NotFound"),
        SealRejectReason.InUse => App.GetString("Seal_Reject_InUse"),
        SealRejectReason.Denied => App.GetString("Seal_Reject_Denied"),
        SealRejectReason.ReadOnly => App.GetString("Seal_Reject_ReadOnly"),
        SealRejectReason.Unreachable => App.GetString("Seal_Reject_Unreachable"),
        SealRejectReason.Fat32TooLarge => App.GetString("Seal_Reject_Fat32TooLarge"),
        _ => App.GetString("Seal_Reject_Generic"),
    };

    private static string SealStatusText(SealServiceStatus status) => status switch
    {
        SealServiceStatus.NotUnlocked => App.GetString("Seal_SealErr_NotUnlocked"),
        SealServiceStatus.RequiresFullSession => App.GetString("Seal_SealErr_FullSession"),
        SealServiceStatus.NotInitialized => App.GetString("Seal_SealErr_NotInitialized"),
        SealServiceStatus.VersionUnsupported => App.GetString("Seal_List_Err_Unsupported"),
        SealServiceStatus.Corrupt => App.GetString("Seal_SealErr_PendingRecovery"),

        SealServiceStatus.IndexFull => App.GetString("Seal_SealErr_IndexFull"),

        SealServiceStatus.Busy => App.GetString("Seal_Space_Busy"),
        _ => App.GetString("Seal_SealErr_Io"),
    };

    private void HideSealConfirmDialog()
    {
        if (SealSealOverlay.Visibility != Visibility.Visible) return;

        DialogDepth.VeilHide();
        DialogUi.HideOverlay(SealRoot, _dialogGens, _dialogMap,
            SealSealOverlay, SealSealDialog, SealSealTransform, () =>
            {
                SealSealFilesList.Children.Clear();
                SealSealNoteBox.Text = string.Empty;
                SealSealDisplayNameBox.Text = string.Empty;
                _recoveryRetryEntry = null;
            });
    }


    private void SealSpaceView_RightTapped(object sender, RightTappedRoutedEventArgs e)
        => ShowSealPickMenu(SealSpaceView, e, capsuleByDefault: false);

    private void SealCapsuleView_RightTapped(object sender, RightTappedRoutedEventArgs e)
        => ShowSealPickMenu(SealCapsuleView, e, capsuleByDefault: true);

    private void SealCapsulePick_Click(object sender, RoutedEventArgs e) => PickFilesToSeal(capsuleByDefault: true);

    private void ShowSealPickMenu(FrameworkElement target, RightTappedRoutedEventArgs e, bool capsuleByDefault)
    {
        e.Handled = true;
        if (_sealInFlight || _takeInFlight || _destroyInFlight || _recoveryPassInFlight)
        {
            App.ShowToast(App.GetString(_sealInFlight ? "Seal_Seal_Busy" : "Seal_Space_Busy"), ToastTone.Error);
            return;
        }
        try
        {




            var menu = new MenuFlyout
            {
                MenuFlyoutPresenterStyle = (Style)Application.Current.Resources["GlassMenuFlyoutPresenterStyle"],
            };
            var pick = new MenuFlyoutItem
            {
                Style = (Style)Application.Current.Resources["GlassMenuFlyoutItemStyle"],
                Text = App.GetString("Seal_Menu_SealFiles"),
                Icon = new PathIcon
                {
                    Data = (Geometry)Microsoft.UI.Xaml.Markup.XamlBindingHelper.ConvertValue(
                        typeof(Geometry), IconData.PickFile),
                    Foreground = App.GetBrush("IconForegroundBrush"),
                },
            };
            pick.Click += (_, _) => PickFilesToSeal(capsuleByDefault);
            menu.Items.Add(pick);
            menu.ShowAt(target, e.GetPosition(target));
        }
        catch (Exception ex)
        {

            CrashLogger.LogNote("SealMenu", ex.Message);
            App.ShowToast(App.GetString("Common_Toast_Failed"));
        }
    }


    private bool EnsureSpaceInitialized()
    {
        if (SealService.Current.IsInitialized()) return true;
        ShowPasswordDialog(_ => { });
        return false;
    }


    private void ShowSpaceNotInitNotice()
    {
        ShowSealErrorDialog(
            App.GetString("Seal_NotInit_Title"),
            App.GetString("Seal_NotInit_Notice"),
            retryText: App.GetString("Seal_FirstRun_SetPassword"),
            retry: () => ShowPasswordDialog(_ => { }));
    }

    private bool RequireSpaceInitialized()
    {
        if (SealService.Current.IsInitialized()) return true;
        ShowSpaceNotInitNotice();
        return false;
    }


    private void PickFilesToSeal(bool capsuleByDefault = false)
    {
        if (_recoveryPassInFlight) { RefuseWhileRecovering(); return; }
        if (_sealInFlight) { RefuseWhileSealing(); return; }
        if (!EnsureSpaceInitialized()) return;
        try
        {
            var files = FilePicker.PickFiles("*.*", App.GetString("Seal_Menu_SealFiles"));
            if (files.Count > 0) ShowSealConfirmDialog(files, capsuleByDefault);
        }
        catch (Exception ex)
        {
            CrashLogger.LogNote("SealPicker", ex.Message);
            App.ShowToast(App.GetString("Common_Toast_Failed"));
        }
    }





    private void ApplyDetailTakeState(SealedItem item)
    {
        var observed = SealService.Current.GetObservedMaxUtc();
        var verdict = observed.Status == SealServiceStatus.Ok
            ? SealTimeLock.Evaluate(item, DateTimeOffset.UtcNow, observed.Value)
            : SealTakeVerdict.Locked;
        var takeable = verdict is SealTakeVerdict.TakeAllowed or SealTakeVerdict.Expired;
        SealDetailValueStatus.Text = App.GetString(verdict switch
        {
            SealTakeVerdict.TakeAllowed or SealTakeVerdict.Expired => "Seal_Detail_Status_Ready",
            SealTakeVerdict.RollbackSuspected => "Seal_Capsule_Status_Clock",
            _ => "Seal_Detail_Status_Locked",
        });


        SealDetailTakeButton.IsEnabled = takeable;
        ToolTipService.SetToolTip(SealDetailTakeButton, takeable
            ? null
            : verdict == SealTakeVerdict.RollbackSuspected
                ? App.GetString("Seal_Take_ClockSuspected")
                : string.Format(App.GetString("Seal_Detail_TakeLockedTip"), SealDetailValueExpiry.Text));
    }



    public void ShowSealedItemDetail(SealedItem item)
    {
        if (SealDetailOverlay.Visibility == Visibility.Visible) return;

        _detailItem = item;
        ApplyDetailTexts();

        var glyph = SealGlyphs.Detect(item.OriginalName, item.IsFolder);
        SealDetailIconHost.Children.Clear();
        SealDetailIconHost.Children.Add(SealIconFactory.Create(glyph, 40));

        SealDetailName.Text = string.IsNullOrEmpty(item.DisplayName) ? item.OriginalName : item.DisplayName;
        SealDetailValueOriginal.Text = item.OriginalName;
        SealDetailValueType.Text = LabelOf(glyph);
        SealDetailValueSize.Text = PathPropertyFormatter.FormatSize((long)Math.Min(item.Size, long.MaxValue));
        SealDetailValueSource.Text = string.IsNullOrWhiteSpace(item.SourcePath) ? EmptyValue : item.SourcePath;
        SealDetailValueSealedAt.Text = ToLocalText(item.SealedAtUtc);
        SealDetailValueNote.Text = string.IsNullOrWhiteSpace(item.Note) ? EmptyValue : item.Note;

        var capsule = item.IsCapsule;
        SealDetailRowExpiry.Visibility = capsule ? Visibility.Visible : Visibility.Collapsed;
        SealDetailRowStatus.Visibility = capsule ? Visibility.Visible : Visibility.Collapsed;
        SealDetailValueExpiry.Text = item.ExpiryUtc is { } expiry ? ToLocalText(expiry) : EmptyValue;



        ApplyDetailTakeState(item);
        ScheduleCapsuleExpiry();

        DialogDepth.VeilShow();
        DialogUi.ShowOverlay(SealRoot, _dialogGens, _dialogMap,
            SealDetailOverlay, SealDetailDialog, SealDetailTransform, _dialogWidthFit);

        if (SealDetailTakeButton.IsEnabled) SealDetailTakeButton.Focus(FocusState.Programmatic);
        else SealDetailDestroyButton.Focus(FocusState.Programmatic);
    }


    private static string EmptyValue => App.GetString("Seal_Detail_Empty");

    private static string ToLocalText(long epochSeconds)
        => DateTimeOffset.FromUnixTimeSeconds(epochSeconds).ToLocalTime().ToString("yyyy-MM-dd HH:mm");

    private void ApplyDetailTexts()
    {
        SealDetailLabelOriginal.Text = App.GetString("Seal_Detail_OriginalName");
        SealDetailLabelType.Text = App.GetString("Seal_Detail_Type");
        SealDetailLabelSize.Text = App.GetString("Seal_Detail_Size");
        SealDetailLabelSource.Text = App.GetString("Seal_Detail_Source");
        SealDetailLabelSealedAt.Text = App.GetString("Seal_Detail_SealedAt");
        SealDetailLabelExpiry.Text = App.GetString("Seal_Detail_Expiry");
        SealDetailLabelStatus.Text = App.GetString("Seal_Detail_Status");
        SealDetailLabelNote.Text = App.GetString("Common_Label_Note");
        SealDetailDestroyButton.Content = App.GetString("Seal_Detail_Destroy");
        SealDetailTakeButton.Content = App.GetString("Seal_Detail_Take");
    }

    private void SealDetailClose_Click(object sender, RoutedEventArgs e) => HideSealedItemDetail();

    private void SealDetailScrim_Tapped(object sender, TappedRoutedEventArgs e) => HideSealedItemDetail();


    private void SealDetailTake_Click(object sender, RoutedEventArgs e) => _ = BeginTakeAsync();


    private void SealDetailDestroy_Click(object sender, RoutedEventArgs e)
    {
        if (_detailItem is { } item) ShowSealDestroyDialog(item);
    }

    private void HideSealedItemDetail()
    {
        if (SealDetailOverlay.Visibility != Visibility.Visible) return;

        DialogDepth.VeilHide();
        DialogUi.HideOverlay(SealRoot, _dialogGens, _dialogMap,
            SealDetailOverlay, SealDetailDialog, SealDetailTransform, () =>
            {
                SealDetailIconHost.Children.Clear();
                SealDetailTakeButton.IsEnabled = true;
                ToolTipService.SetToolTip(SealDetailTakeButton, null);
                _detailItem = null;
            });
    }





    public void ShowPasswordDialog(Action<bool> onClosed)
    {
        if (SealPwdOverlay.Visibility == Visibility.Visible) return;
        _pwdOnClosed = onClosed;
        _pwdInFlight = false;
        ApplyPasswordDialogTexts();
        SealPwdPasswordBox.Password = "";
        SealPwdConfirmBox.Password = "";
        SealPwdHintBox.Text = "";
        SealPwdErrorText.Text = "";
        SealPwdErrorText.Visibility = Visibility.Collapsed;
        SealPwdStrengthText.Visibility = Visibility.Collapsed;
        SealPwdConfirmButton.Content = App.GetString("Seal_Pwd_Confirm");
        SealPwdConfirmButton.IsEnabled = false;
        SealPwdCancelButton.IsEnabled = true;
        DialogDepth.VeilShow();
        DialogUi.ShowOverlay(SealRoot, _dialogGens, _dialogMap,
            SealPwdOverlay, SealPwdDialog, SealPwdDialogTransform, _dialogWidthFit);
        SealPwdPasswordBox.Focus(FocusState.Programmatic);
    }

    private void ApplyPasswordDialogTexts()
    {
        SealPwdTitle.Text = App.GetString("Seal_Pwd_Title");
        SealPwdPasswordLabel.Text = App.GetString("Seal_Pwd_Label_Password");
        SealPwdPasswordBox.PlaceholderText = App.GetString("Seal_Pwd_Placeholder");
        SealPwdConfirmLabel.Text = App.GetString("Seal_Pwd_Label_Confirm");
        SealPwdConfirmBox.PlaceholderText = App.GetString("Seal_Pwd_Placeholder");
        SealPwdHintLabel.Text = App.GetString("Seal_Pwd_Label_Hint");
        SealPwdHintBox.PlaceholderText = App.GetString("Seal_Pwd_HintPlaceholder");
        SealPwdHintNote.Text = App.GetString("Seal_Pwd_HintNote");
        SealPwdWarningText.Text = App.GetString("Seal_Pwd_Warning");
        SealPwdCancelButton.Content = App.GetString("Common_Button_Cancel");
    }

    private void SealPwdPassword_Changed(object sender, RoutedEventArgs e) => UpdatePasswordDialogState();


    private void UpdatePasswordDialogState()
    {
        var pw = SealPwdPasswordBox.Password ?? string.Empty;
        var confirm = SealPwdConfirmBox.Password ?? string.Empty;

        if (pw.Length == 0)
        {
            SealPwdStrengthText.Visibility = Visibility.Collapsed;
        }
        else
        {
            SealPwdStrengthText.Visibility = Visibility.Visible;
            switch (SealPasswordPolicy.Rate(pw))
            {
                case BackupPasswordStrength.Strong:
                    SealPwdStrengthText.Text = App.GetString("Seal_Pwd_Strength_Strong");
                    SealPwdStrengthText.Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(0xFF, 0x4C, 0xAF, 0x50));
                    break;
                case BackupPasswordStrength.Medium:
                    SealPwdStrengthText.Text = App.GetString("Seal_Pwd_Strength_Medium");
                    SealPwdStrengthText.Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(0xFF, 0xFF, 0xD7, 0x00));
                    break;
                default:
                    SealPwdStrengthText.Text = App.GetString("Seal_Pwd_Strength_Weak");
                    SealPwdStrengthText.Foreground = App.GetBrush("AppDangerTextBrush");
                    break;
            }
        }

        var (ok, issue) = SealPasswordPolicy.Evaluate(pw);
        bool matched = pw.Length > 0 && pw == confirm;


        string? error = null;
        if (pw.Length > 0 && confirm.Length > 0 && !matched) error = App.GetString("Seal_Pwd_Err_Mismatch");
        else if (pw.Length > 0 && !ok) error = IssueText(issue);
        SealPwdErrorText.Text = error ?? string.Empty;
        SealPwdErrorText.Visibility = error is null ? Visibility.Collapsed : Visibility.Visible;

        SealPwdConfirmButton.IsEnabled = !_pwdInFlight && ok && matched;
    }

    private static string IssueText(SealPasswordIssue issue) => issue switch
    {
        SealPasswordIssue.TooShort => App.GetString("Seal_Pwd_Err_TooShort"),
        SealPasswordIssue.WeakFragment => App.GetString("Seal_Pwd_Err_WeakFragment"),
        SealPasswordIssue.TooFewClasses => App.GetString("Seal_Pwd_Err_TooFewClasses"),
        SealPasswordIssue.Sequence => App.GetString("Seal_Pwd_Err_Sequence"),
        _ => App.GetString("Seal_Pwd_Err_TooShort"),
    };

    private async void SealPwdConfirm_Click(object sender, RoutedEventArgs e)
    {
        if (_pwdInFlight) return;
        var gen = _leaveGen;
        var pw = SealPwdPasswordBox.Password ?? string.Empty;
        var (ok, issue) = SealPasswordPolicy.Evaluate(pw);
        if (!ok)
        {
            SealPwdErrorText.Text = IssueText(issue);
            SealPwdErrorText.Visibility = Visibility.Visible;
            return;
        }
        if (pw != (SealPwdConfirmBox.Password ?? string.Empty)) return;

        _pwdInFlight = true;
        SealPwdConfirmButton.IsEnabled = false;
        SealPwdCancelButton.IsEnabled = false;
        SealPwdConfirmButton.Content = App.GetString("Seal_Pwd_Busy");

        var hint = SealPwdHintBox.Text?.Trim();
        SealServiceStatus status;
        try
        {



            status = await Task.Run(() =>
            {
                if (PasswordService.Exists() && PasswordService.Verify(pw))
                    return SealServiceStatus.SameAsMainPassword;
                return SealService.Current.InitializeAsync(pw, string.IsNullOrWhiteSpace(hint) ? null : hint)
                    .GetAwaiter().GetResult();
            });
        }
        catch
        {
            status = SealServiceStatus.IoError;
        }




        if (gen != _leaveGen)
        {
            _pwdInFlight = false;
            return;
        }

        _pwdInFlight = false;
        SealPwdCancelButton.IsEnabled = true;
        SealPwdConfirmButton.Content = App.GetString("Seal_Pwd_Confirm");

        if (status == SealServiceStatus.Ok)
        {


            try
            {
                var (opened, unlockEpoch) = await Task.Run(
                    () => SealService.Current.UnlockWithEpochAsync(pw).GetAwaiter().GetResult());
                if (gen != _leaveGen)
                {


                    SealService.Current.TryLockIfEpoch(unlockEpoch);
                    return;
                }
                if (opened != SealServiceStatus.Ok)
                {
                    SealPwdErrorText.Text = App.GetString("Seal_Pwd_Err_Io");
                    SealPwdErrorText.Visibility = Visibility.Visible;
                    UpdatePasswordDialogState();
                    return;
                }
            }
            catch
            {
                SealPwdErrorText.Text = App.GetString("Seal_Pwd_Err_Io");
                SealPwdErrorText.Visibility = Visibility.Visible;
                UpdatePasswordDialogState();
                return;
            }

            HidePasswordDialog(true);
            RefreshItems();
            RefreshCapsules();
            _settingsDirty = true;
            return;
        }

        SealPwdErrorText.Text = status switch
        {
            SealServiceStatus.AlreadyInitialized => App.GetString("Seal_Pwd_Err_AlreadyInitialized"),
            SealServiceStatus.SameAsMainPassword => App.GetString("Seal_Pwd_Err_SameAsMain"),
            SealServiceStatus.WeakPassword => App.GetString("Seal_Pwd_Err_TooFewClasses"),
            SealServiceStatus.VersionUnsupported => App.GetString("Seal_Pwd_Err_Unsupported"),
            _ => App.GetString("Seal_Pwd_Err_Io"),
        };
        SealPwdErrorText.Visibility = Visibility.Visible;
        UpdatePasswordDialogState();
    }

    private void SealPwdCancel_Click(object sender, RoutedEventArgs e)
    {
        if (_pwdInFlight) return;
        HidePasswordDialog(false);
    }




    public void CloseDialogsOnLeave(bool sessionEnding = false)
    {
        _leaveGen++;
        HideSealedItemDetail();
        HideSealConfirmDialog();
        HideSealConflictDialog();
        HideSealTakeError();
        HideSealDestroyDialog();
        HideSealClockResetDialog();
        if (_folderModeTcs is not null) ResolveFolderMode(null);
        HideSealReceipt();



        if (!_settingsInFlight || sessionEnding)
        {
            HideSealChpwDialog();
            HideSealHintDialog();
        }
        if (sessionEnding) ClearSealSettingsPlaintext();
        HideSealAboutDialog();



        if (!_importInFlight || sessionEnding) HideSealImportDialog();
        if (!_exportInFlight || sessionEnding) HideSealExportDialog();
        if (sessionEnding)
        {
            _importBundlePassword = "";
            _importLocalPassword = "";
            _importLocalPasswordConfirm = "";
            SealExportPasswordBox.Password = string.Empty;
            SetSealPasswordError(SealExportPasswordBox, false);
        }
        HideSealRecoveryDialog();
        StopRecoveryBreath();
        if (SealPwdOverlay.Visibility != Visibility.Visible) return;


        if (_pwdInFlight && !sessionEnding) return;
        HidePasswordDialog(false);
        if (sessionEnding)
        {
            SealPwdPasswordBox.Password = "";
            SealPwdConfirmBox.Password = "";
        }
    }

    private void SealPwdClose_Click(object sender, RoutedEventArgs e)
    {
        if (_pwdInFlight) return;
        HidePasswordDialog(false);
    }

    private void SealPwdScrim_Tapped(object sender, TappedRoutedEventArgs e)
    {
        if (_pwdInFlight) return;
        HidePasswordDialog(false);
    }

    private void HidePasswordDialog(bool succeeded)
    {
        if (SealPwdOverlay.Visibility != Visibility.Visible) return;
        var onClosed = _pwdOnClosed;
        _pwdOnClosed = null;
        DialogDepth.VeilHide();
        DialogUi.HideOverlay(SealRoot, _dialogGens, _dialogMap,
            SealPwdOverlay, SealPwdDialog, SealPwdDialogTransform, () =>
            {

                SealPwdPasswordBox.Password = "";
                SealPwdConfirmBox.Password = "";
                onClosed?.Invoke(succeeded);
            });
    }



    public void ResetAfterSessionLock()
    {
        _capsuleExpiryTimer?.Stop();

        _longOpInterruptedByLock = _sealInFlight || _takeInFlight;
        CloseDialogsOnLeave(sessionEnding: true);
        _recoveryRan = false;
        _recoveryPrompted = false;
        _recoveryPassInFlight = false;
        _recoveryEntries = System.Array.Empty<SealRecoveryEntry>();
        _recoverySelected = null;
        _recoveryDeferred = 0;
        _recoveryAttention = false;
        _itemsDirty = true;
        _capsuleDirty = true;
        _settingsDirty = true;
        _itemsEntrancePlayed = false;
        _capsulesEntrancePlayed = false;
        SealSpaceContent.Children.Clear();
        SealCapsuleContent.Children.Clear();
        SealSettingsContent.Children.Clear();
        SealRecoveryBannerHost.Children.Clear();
        SealRecoveryBannerHost.Visibility = Visibility.Collapsed;
    }
}
