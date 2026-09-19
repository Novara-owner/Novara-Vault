using Windows.ApplicationModel;
using Windows.ApplicationModel.Activation;
using Windows.Foundation;
using Windows.Foundation.Collections;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Navigation;
using Microsoft.UI.Xaml.Shapes;
using WinRT.Interop;
using Windows.UI;
using Novara.Services;




namespace Novara;




public partial class App : Application
{

    public static MainWindow? MainWindow { get; private set; }


    public static string? PendingAddPath { get; private set; }


    public static string? PendingImportMd { get; private set; }


    public static Guid? PendingReminderId { get; private set; }


    public static bool McpBackground { get; private set; }


    public static bool LaunchedFromContextMenu { get; private set; }

    private static void ParseLaunchArgs()
    {
        var args = Environment.GetCommandLineArgs();
        for (int i = 1; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--open":
                    LaunchedFromContextMenu = true;
                    break;
                case "--add-path" when i + 1 < args.Length:
                    LaunchedFromContextMenu = true;
                    PendingAddPath = args[i + 1].Trim();
                    i++;
                    break;
                case "--import-md" when i + 1 < args.Length:
                    LaunchedFromContextMenu = true;
                    PendingImportMd = args[i + 1].Trim();
                    i++;
                    break;
                case "--mcp-background":
                    McpBackground = true;
                    break;
                case "--reminder" when i + 1 < args.Length:
                    if (Guid.TryParse(args[i + 1].Trim(), out var rid))
                        PendingReminderId = rid;
                    i++;
                    break;
            }
        }
    }

    public static NovaraStore? Store { get; private set; }


    public static void RelockStore()
    {
        Services.SyncService.OnLocked();
        var old = Store;
        old?.Invalidate();
        Store = new NovaraStore(NovaraStore.DefaultFilePath);
    }

    public static string CurrentTheme { get; private set; } = "跟随系统";

    public static string CurrentLanguage { get; private set; } = "zh-CN";


    public static string CurrentWorkspaceId { get; set; } = "";


    public static string DataDirName =>
#if DEBUG
        "Novara-Dev";
#else
        "Novara";
#endif

    private static string LanguageHintPath => System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), DataDirName, "language.dat");



    public static string? LoadLanguageHint()
    {
        try
        {
            if (System.IO.File.Exists(LanguageHintPath))
            {
                var s = System.IO.File.ReadAllText(LanguageHintPath).Trim();
                return string.IsNullOrEmpty(s) ? null : s;
            }
        }
        catch { }
        return null;
    }

    public static void SaveLanguageHint(string language)
    {
        try
        {
            var dir = System.IO.Path.GetDirectoryName(LanguageHintPath);
            if (!string.IsNullOrEmpty(dir)) System.IO.Directory.CreateDirectory(dir);
            System.IO.File.WriteAllText(LanguageHintPath, language);
        }
        catch { }
    }




    public static void PlayCardEntrance(FrameworkElement fe, int index = 0)
    {


        fe.Opacity = 0;
        if (fe.RenderTransform is not TranslateTransform tt) { tt = new TranslateTransform(); fe.RenderTransform = tt; }
        tt.Y = 18;
        var sb = new Storyboard();
        int delay = index * Services.Motion.Stagger;
        var fadeIn = new DoubleAnimation { To = 1, Duration = TimeSpan.FromMilliseconds(260), BeginTime = TimeSpan.FromMilliseconds(delay), EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
        Storyboard.SetTarget(fadeIn, fe); Storyboard.SetTargetProperty(fadeIn, "Opacity"); sb.Children.Add(fadeIn);
        sb.Children.Add(Services.Motion.Eased(0, 300, Services.Motion.Decelerate, tt, "Y", delay));
        _entranceBoards.Remove(fe); _entranceBoards.Add(fe, sb);
        sb.Begin();
    }


    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<FrameworkElement, Storyboard> _entranceBoards = new();


    public static void StopCardEntrance(FrameworkElement fe)
    {
        if (fe == null) return;
        if (_entranceBoards.TryGetValue(fe, out var sb))
        {
            sb.Stop();
            _entranceBoards.Remove(fe);
        }
    }








    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Microsoft.UI.Xaml.Controls.Border, object> _flashingCards = new();

    public static async void FlashCard(Microsoft.UI.Xaml.Controls.Border card)
    {
        if (card == null) return;


        if (_flashingCards.TryGetValue(card, out _)) return;
        _flashingCards.Add(card, new object());
        try
        {
            var origBg = card.Background;
            var origBorder = card.BorderBrush;
            var accent = App.GetBrush("AppAccentBrush") as SolidColorBrush
                         ?? new SolidColorBrush(Windows.UI.Color.FromArgb(0xFF, 0x72, 0x76, 0xFF));

            var transparent = Windows.UI.Color.FromArgb(0, 0, 0, 0);
            var origBgColor = (origBg as SolidColorBrush)?.Color ?? transparent;
            var origBorderColor = (origBorder as SolidColorBrush)?.Color ?? transparent;
            var highlightBg = Windows.UI.Color.FromArgb(0x40, accent.Color.R, accent.Color.G, accent.Color.B);

            await System.Threading.Tasks.Task.Delay(50);
            if (card.Parent == null) return;

            card.Background = new SolidColorBrush(highlightBg);
            card.BorderBrush = new SolidColorBrush(accent.Color);


            const int rounds = 2;
            const int holdMs = 100;
            const int fadeSteps = 6;
            const int fadeStepMs = 35;
            for (int r = 0; r < rounds; r++)
            {
                card.Background = new SolidColorBrush(highlightBg);
                card.BorderBrush = new SolidColorBrush(accent.Color);
                await System.Threading.Tasks.Task.Delay(holdMs);
                if (card.Parent == null) return;
                for (int i = 1; i <= fadeSteps; i++)
                {
                    await System.Threading.Tasks.Task.Delay(fadeStepMs);
                    if (card.Parent == null) return;
                    float t = (float)i / fadeSteps;
                    card.Background = new SolidColorBrush(LerpColor(highlightBg, origBgColor, t));
                    card.BorderBrush = new SolidColorBrush(LerpColor(accent.Color, origBorderColor, t));
                }
            }

            card.Background = origBg;
            card.BorderBrush = origBorder;
        }
        catch { System.Diagnostics.Debug.WriteLine("FlashCard 异常（卡片可能已卸载）"); }
        finally { _flashingCards.Remove(card); }
    }

    private static Windows.UI.Color LerpColor(Windows.UI.Color a, Windows.UI.Color b, float t)
    {
        byte L(byte x, byte y) => (byte)Math.Round(x + (y - x) * t);
        return Windows.UI.Color.FromArgb(L(a.A, b.A), L(a.R, b.R), L(a.G, b.G), L(a.B, b.B));
    }

















    public static void PlayCardRemoval(Microsoft.UI.Xaml.Controls.Panel container, Microsoft.UI.Xaml.Controls.Border target, bool dragging, Action? onDone = null)
    {
        if (container == null || target == null || target.Parent != container)
        {
            onDone?.Invoke();
            return;
        }
        if (dragging)
        {
            container.Children.Remove(target);
            onDone?.Invoke();
            return;
        }

        StopCardEntrance(target);
        int index = container.Children.IndexOf(target);
        if (index < 0) { container.Children.Remove(target); onDone?.Invoke(); return; }


        double spacing = (container is StackPanel sp) ? sp.Spacing : 0;
        double targetHeight = target.ActualHeight > 0 ? target.ActualHeight : 0;
        if (targetHeight <= 0)
        {
            container.Children.Remove(target);
            onDone?.Invoke();
            return;
        }


        double slideAmount = targetHeight + spacing;

        var survivors = new List<Microsoft.UI.Xaml.Controls.Border>();
        var transforms = new List<TranslateTransform>();
        for (int i = index + 1; i < container.Children.Count; i++)
        {
            if (container.Children[i] is not Microsoft.UI.Xaml.Controls.Border b) continue;
            survivors.Add(b);
            if (b.RenderTransform is not TranslateTransform tt) { tt = new TranslateTransform(); b.RenderTransform = tt; }
            transforms.Add(tt);
            b.IsHitTestVisible = false;
        }

        const double dur = 200;
        var sb = new Storyboard();

        var fade = new DoubleAnimation { To = 0, Duration = TimeSpan.FromMilliseconds(dur), EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn } };
        Storyboard.SetTarget(fade, target); Storyboard.SetTargetProperty(fade, "Opacity");
        sb.Children.Add(fade);


        for (int i = 0; i < survivors.Count; i++)
        {
            var slide = new DoubleAnimation { To = -slideAmount, Duration = TimeSpan.FromMilliseconds(dur), EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
            Storyboard.SetTarget(slide, transforms[i]); Storyboard.SetTargetProperty(slide, "Y");
            sb.Children.Add(slide);
        }

        sb.Completed += (_, _) =>
        {
            try
            {


                if (target.Parent == container) container.Children.Remove(target);
                for (int i = 0; i < survivors.Count; i++)
                {
                    transforms[i].Y = 0;
                    survivors[i].IsHitTestVisible = true;
                }
            }
            catch { }
            onDone?.Invoke();
        };
        sb.Begin();
    }



    public static bool IsDescendantOfButton(Microsoft.UI.Xaml.DependencyObject? d)
    {
        while (d != null)
        {
            if (d is Microsoft.UI.Xaml.Controls.Button) return true;
            d = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetParent(d);
        }
        return false;
    }

    public static Geometry CreateGeometry(string pathData)
    {



        var g = (Geometry)Microsoft.UI.Xaml.Markup.XamlBindingHelper.ConvertValue(typeof(Geometry), pathData);


        if (g is PathGeometry pg) pg.FillRule = FillRule.Nonzero;
        else if (g is GeometryGroup gg) gg.FillRule = FillRule.Nonzero;
        return g;
    }






    public static string GetString(string key)
    {
        var dict = CurrentLanguage switch
        {
            "en-US" => AppResources.En,
            "zh-TW" => AppResources.ZhTw,
            "ko-KR" => AppResources.Ko,
            "ja-JP" => AppResources.Ja,
            _ => AppResources.Zh,
        };
        return dict.TryGetValue(key, out var s) ? s : key;
    }








    public static string DiaryTitleText(string title)
        => string.IsNullOrWhiteSpace(title) ? GetString("DiaryEditor_Untitled") : title;







    public static int GetCharacterSpacing(string text, int cjkValue)
    {
        if (string.IsNullOrEmpty(text)) return 0;
        foreach (var c in text)
        {

            if ((c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z')) return 0;

            if (c >= '\uAC00' && c <= '\uD7AF') return 0;
            if (c >= '\u1100' && c <= '\u11FF') return 0;

            if (c >= '\u3040' && c <= '\u30FF') return 0;
        }
        return cjkValue;
    }


    public static void ShowToast(string message)
    {
        UiQueue?.TryEnqueue(() => MainWindow?.ShowToast(message));
    }





    public static void ApplyLocalizedTexts(DependencyObject root)
    {
        int count = VisualTreeHelper.GetChildrenCount(root);
        for (int i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is TextBlock tb && tb.Tag is string key && key.Length > 0)
                tb.Text = GetString(key);
            else if (child is TextBox box && box.Tag is string pk && pk.Length > 0)
                box.PlaceholderText = GetString(pk);
            ApplyLocalizedTexts(child);
        }
    }






    public static void ApplyLanguage(string language)
    {
        CurrentLanguage = string.IsNullOrEmpty(language) ? ResolveSystemLanguage() : language;
    }






    public static string ResolveSystemLanguage()
    {
        try
        {
            var langs = Windows.System.UserProfile.GlobalizationPreferences.Languages;
            if (langs != null && langs.Count > 0)
            {
                var l = langs[0].ToLowerInvariant();
                if (l.StartsWith("zh"))
                    return l.Contains("tw") || l.Contains("hant") || l.Contains("hk") || l.Contains("mo") ? "zh-TW" : "zh-CN";
                if (l.StartsWith("en")) return "en-US";
                if (l.StartsWith("ko")) return "ko-KR";
                if (l.StartsWith("ja")) return "ja-JP";
            }
        }
        catch { }
        return "en-US";
    }

    public static SolidColorBrush GetBrush(string key)
    {
        string dictKey = CurrentTheme switch
        {
            "浅色模式" => "Light",
            "深色模式" => "Dark",

            _ => IsSystemLight() ? "Light" : "Dark"
        };
        try
        {
            if (Current.Resources.ThemeDictionaries[dictKey] is ResourceDictionary dict && dict[key] is SolidColorBrush b)
                return b;
        }
        catch { }




        try
        {
            var other = dictKey == "Dark" ? "Light" : "Dark";
            if (Current.Resources.ThemeDictionaries[other] is ResourceDictionary od && od[key] is SolidColorBrush ob)
                return ob;
        }
        catch { }
        return null!;
    }

    private static bool IsSystemLight()
    {


        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            if (key?.GetValue("AppsUseLightTheme") is int v)
                return v != 0;
        }
        catch { }


        return true;
    }





    public App()
    {

        InitializeComponent();
    }





    protected override void OnLaunched(Microsoft.UI.Xaml.LaunchActivatedEventArgs args)
    {
        UiQueue = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread();

        Services.CrashLogger.Init();



        Services.AuditWriteHealth.OnFailure = msg => Services.CrashLogger.LogNote("AuditWriteFailure", msg);
        Services.Loc.T = GetString;

        ParseLaunchArgs();





        if (!Novara.MainWindow.TryAcquireSingleInstance())
        {

            if (!string.IsNullOrWhiteSpace(App.PendingAddPath))
                Services.AddPathRequest.Raise(App.PendingAddPath);
            if (!string.IsNullOrWhiteSpace(App.PendingImportMd))
                Services.MdImportRequest.Raise(App.PendingImportMd);
            if (App.PendingReminderId.HasValue)
                Services.ReminderDueRequest.Raise(App.PendingReminderId.Value);
            Novara.MainWindow.WakeExistingInstance();
            Environment.Exit(0);
            return;
        }




        Store = new NovaraStore(NovaraStore.DefaultFilePath);

        if (McpBackground)
        {


            UiQueue.TryEnqueue(() =>
            {
                try
                {

                    Novara.Services.NovaraStore.DiscardOrphanedPasswordFiles(Novara.Services.NovaraStore.DefaultFilePath);
                    var loadResult = Store.Load();
                    System.Diagnostics.Debug.WriteLine($"MCP background load: {loadResult.Status}");


                    if (!Store.Database.AppSettings.McpEnabled)
                    {
                        Environment.Exit(0);
                        return;
                    }


                    if (McpPermissions.EnsureMigrated(Store.Database.AppSettings))
                        _ = Store.SaveAsync();
                    McpService.Start();
                }
                catch (Exception ex)
                {


                    Services.CrashLogger.LogBackgroundStartupFailure(ex);
                    Environment.Exit(0);
                }
            });
            ShowWindowRequest.StartListening(() => UiQueue.TryEnqueue(ShowMainWindowFromBackground));
            return;
        }






        MainWindow = new MainWindow();
        StartRequestListeners();
        MainWindow.Activate();
    }

    private static void StartRequestListeners()
    {



        Services.EditRequest.StartListening(g =>
            MainWindow?.DispatcherQueue?.TryEnqueue(() => MainWindow.HandleEditRequest(g)));
        Services.AddPathRequest.StartListening(p =>
            MainWindow?.DispatcherQueue?.TryEnqueue(() => MainWindow.HandleAddPathRequest(p)));
        Services.MdImportRequest.StartListening(p =>
            MainWindow?.DispatcherQueue?.TryEnqueue(() => MainWindow.HandleImportMdRequest(p)));
        Services.ReminderEditRequest.StartListening(p =>
            MainWindow?.DispatcherQueue?.TryEnqueue(() => MainWindow.HandleReminderEditRequest(p)));
    }


    private static void ShowMainWindowFromBackground()
    {
        if (MainWindow == null)
        {
            MainWindow = new MainWindow();
            StartRequestListeners();
        }
        MainWindow.Activate();
    }







    public static void InitSystemThemeWatcher()
    {
        if (_systemThemeWatcherStarted) return;
        _systemThemeWatcherStarted = true;

        try
        {


            _systemThemeUISettings = new Windows.UI.ViewManagement.UISettings();
            _systemThemeUISettings.ColorValuesChanged += (_, _) =>
            {
                if (CurrentTheme != "跟随系统") return;
                var now = DateTime.Now;
                if ((now - _lastSystemThemeSync).TotalMilliseconds < 500) return;
                _lastSystemThemeSync = now;
                UiQueue?.TryEnqueue(() =>
                {
                    Services.StickySync.UpdateTheme(Services.StickySync.ResolveTheme());
                });
            };
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"系统主题监听初始化失败: {ex.Message}");
        }


        if (CurrentTheme == "跟随系统")
            Services.StickySync.UpdateTheme(Services.StickySync.ResolveTheme());
    }

    public static Microsoft.UI.Dispatching.DispatcherQueue? UiQueue { get; private set; }

    private static bool _systemThemeWatcherStarted;
    private static Windows.UI.ViewManagement.UISettings? _systemThemeUISettings;
    private static DateTime _lastSystemThemeSync = DateTime.MinValue;

    public static void ApplyStoredSettings()
    {
        var settings = Store is { IsLoaded: true } ? Store.Database.AppSettings : null;
        if (settings == null) return;


        if (!string.IsNullOrEmpty(settings.AppLanguage)) ApplyLanguage(settings.AppLanguage);
        if (!string.IsNullOrEmpty(settings.Theme)) SetTheme(settings.Theme);
        if (settings.AutoStart) StartupService.Enable();
        else StartupService.Disable();



        Services.SyncService.OnUnlocked();










        if (Store is { IsLoaded: true, IsSaveSuppressed: false } && Services.AutoBackupService.ConsumeReminderResync())
            _ = System.Threading.Tasks.Task.Run(() => Services.ReminderScheduler.Reconcile(
                new System.Collections.Generic.Dictionary<Guid, DateTime>(),
                Services.ReminderScheduler.Snapshot(Store?.Database)));
    }

    public static void SetTheme(string theme)
    {
        CurrentTheme = theme;

        ElementTheme elementTheme;
        switch (theme)
        {
            case "深色模式":
                elementTheme = ElementTheme.Dark;
                break;
            case "浅色模式":
                elementTheme = ElementTheme.Light;
                break;
            case "跟随系统":
            default:
                elementTheme = ElementTheme.Default;
                break;
        }

        if (elementTheme != ElementTheme.Default)
        {
            try { Current.RequestedTheme = elementTheme == ElementTheme.Dark ? ApplicationTheme.Dark : ApplicationTheme.Light; }
            catch {  }
        }

        if (MainWindow?.Content is FrameworkElement root)
        {
            root.RequestedTheme = elementTheme;
        }

        UpdateTitleBarColors();
    }

    public static void UpdateTitleBarColors()
    {
        try
        {
            if (MainWindow == null) return;

            var hwnd = WindowNative.GetWindowHandle((Microsoft.UI.Xaml.Window)MainWindow);
            if (hwnd == IntPtr.Zero) return;

            var windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hwnd);
            var appWindow = Microsoft.UI.Windowing.AppWindow.GetFromWindowId(windowId);
            if (appWindow == null) return;

            bool isLight = (MainWindow.Content is FrameworkElement root && root.ActualTheme == ElementTheme.Light)
                        || CurrentTheme == "浅色模式";
            appWindow.TitleBar.ButtonForegroundColor = isLight
                ? Windows.UI.Color.FromArgb(0xFF, 0x33, 0x33, 0x33)
                : Windows.UI.Color.FromArgb(0xFF, 0xEE, 0xEE, 0xEE);
            appWindow.TitleBar.ButtonHoverForegroundColor = isLight
                ? Windows.UI.Color.FromArgb(0xFF, 0x11, 0x11, 0x11)
                : Windows.UI.Color.FromArgb(0xFF, 0xFF, 0xFF, 0xFF);
            appWindow.TitleBar.ButtonHoverBackgroundColor = isLight
                ? Windows.UI.Color.FromArgb(0xFF, 0xDD, 0xDD, 0xDD)
                : Windows.UI.Color.FromArgb(0xFF, 0x44, 0x44, 0x44);
            appWindow.TitleBar.ButtonPressedBackgroundColor = isLight
                ? Windows.UI.Color.FromArgb(0xFF, 0xCC, 0xCC, 0xCC)
                : Windows.UI.Color.FromArgb(0xFF, 0x33, 0x33, 0x33);
        }
        catch
        {

        }
    }
}
