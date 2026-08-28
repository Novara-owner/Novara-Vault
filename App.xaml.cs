/* ========== Novara Application Entry (App.xaml.cs) ==========
Function: Application bootstrap - startup theme/language, GetString i18n accessor, single-instance acquisition, store creation
Corresponding UI: App.xaml.cs
Logic Range: Whole file business logic of this module
*/
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

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace Novara;

/// <summary>
/// Provides application-specific behavior to supplement the default Application class.
/// </summary>
public partial class App : Application
{

    public static MainWindow? MainWindow { get; private set; }

    /// <summary>Pending file/folder path from right-click menu (--add-path); consumed after UI is ready (stage 3).</summary>
    public static string? PendingAddPath { get; private set; }

    /// <summary>Pending markdown file path from the .md right-click menu (--import-md); consumed after UI is ready (stage 3).</summary>
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
        var old = Store;
        old?.Invalidate(); 
        Store = new NovaraStore(NovaraStore.DefaultFilePath);
    }

    public static string CurrentTheme { get; private set; } = "跟随系统";

    public static string CurrentLanguage { get; private set; } = "zh-CN"; // zh-CN / en-US / zh-TW / ko-KR / ja-JP (i18n, default Chinese)

    /// <summary>Data folder name: Debug builds (dev/test) use "Novara-Dev" to keep test data isolated from the installed release; Release builds use "Novara".</summary>
    public static string DataDirName =>
#if DEBUG
        "Novara-Dev";
#else
        "Novara";
#endif

    private static string LanguageHintPath => System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), DataDirName, "language.dat");

    // Plain-text language hint for encrypted startup: AppSettings is unreadable until unlock,
    // so the last chosen language is persisted separately and applied before the lock screen shows.
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

    // N4W-06: PlayCardsEntrance removed - zero references (pages play the cascade per-card inside
    // their chunked render loops instead).

    public static void PlayCardEntrance(FrameworkElement fe, int index = 0)
    {
        fe.Opacity = 0;
        if (fe.RenderTransform is not TranslateTransform tt) { tt = new TranslateTransform(); fe.RenderTransform = tt; }
        tt.Y = 30;
        var sb = new Storyboard();
        var delay = TimeSpan.FromMilliseconds(index * 120);
        var fadeIn = new DoubleAnimation { To = 1, Duration = TimeSpan.FromMilliseconds(350), BeginTime = delay, EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
        Storyboard.SetTarget(fadeIn, fe); Storyboard.SetTargetProperty(fadeIn, "Opacity"); sb.Children.Add(fadeIn);
        var slideUp = new DoubleAnimation { To = 0, Duration = TimeSpan.FromMilliseconds(400), BeginTime = delay, EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
        Storyboard.SetTarget(slideUp, tt); Storyboard.SetTargetProperty(slideUp, "Y"); sb.Children.Add(slideUp);
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

    /// <summary>
    /// Flash a card for the search-jump highlight: the card background is tinted brand-blue and then
    /// fades back to its original background over ~0.8s, with a matching brand-blue border. The card
    /// is never scaled (the old scale pulse read as a cheap "jump") and its RenderTransform is left
    /// untouched, so the hover lift keeps working. Every fade step allocates a fresh brush so the
    /// shared theme brush is never mutated (E1-09); no ColorAnimation (E2-05); no Storyboard.
    /// </summary>
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Microsoft.UI.Xaml.Controls.Border, object> _flashingCards = new(); // E5-17: same-card re-entry guard (weak keys - no leak)

    public static async void FlashCard(Microsoft.UI.Xaml.Controls.Border card)
    {
        if (card == null) return;
        // E5-17: ignore a re-flash of the same card while one is still running - two concurrent
        // flashes would capture different "original" brushes and interleave their restores.
        if (_flashingCards.TryGetValue(card, out _)) return;
        _flashingCards.Add(card, new object());
        try
        {
            var origBg = card.Background;   // may be null for transparent cards
            var origBorder = card.BorderBrush; // capture first - path-page green/red borders must come back
            var accent = App.GetBrush("AppAccentBrush") as SolidColorBrush
                         ?? new SolidColorBrush(Windows.UI.Color.FromArgb(0xFF, 0x72, 0x76, 0xFF));

            var transparent = Windows.UI.Color.FromArgb(0, 0, 0, 0);
            var origBgColor = (origBg as SolidColorBrush)?.Color ?? transparent;
            var origBorderColor = (origBorder as SolidColorBrush)?.Color ?? transparent;
            var highlightBg = Windows.UI.Color.FromArgb(0x40, accent.Color.R, accent.Color.G, accent.Color.B);

            await System.Threading.Tasks.Task.Delay(50); // let the target page finish its post-switch layout
            if (card.Parent == null) return; // card was removed from the tree during the wait

            card.Background = new SolidColorBrush(highlightBg);
            card.BorderBrush = new SolidColorBrush(accent.Color);

            // Two quick highlight pulses: flash fully lit, then fade fast, twice.
            const int rounds = 2;
            const int holdMs = 100;      // how long each pulse stays fully lit
            const int fadeSteps = 6;     // fast fade per pulse (~210ms)
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

            card.Background = origBg;   // restore exact original (null included)
            card.BorderBrush = origBorder; // restored - green/red validity state is intact
        }
        catch { System.Diagnostics.Debug.WriteLine("FlashCard 异常（卡片可能已卸载）"); }
        finally { _flashingCards.Remove(card); }
    }

    private static Windows.UI.Color LerpColor(Windows.UI.Color a, Windows.UI.Color b, float t)
    {
        byte L(byte x, byte y) => (byte)Math.Round(x + (y - x) * t);
        return Windows.UI.Color.FromArgb(L(a.A, b.A), L(a.R, b.R), L(a.G, b.G), L(a.B, b.B));
    }

    /// <summary>
    /// Smooth card-removal animation. The target card fades out while every card below it in the same
    /// container slides up (TranslateTransform) to fill the gap; when the slide finishes the target is
    /// removed and the survivors' transforms are reset.
    ///
    /// Why survivor slide + placeholder works (rather than removing first, which leaves a blank frame):
    ///  - Keeping the target as a placeholder means the StackPanel layout never re-flows mid-animation,
    ///    so the survivors translate smoothly with no gap.
    ///  - At the end, Remove + reset happen in the SAME Completed callback: the layout re-flow that Remove
    ///    triggers (survivors move up by one card height) is exactly canceled by resetting each survivor's
    ///    translate to 0, so there is no visible jump.
    ///  - Survivors are IsHitTestVisible=false during the animation so hover (-3) cannot fight the slide.
    ///
    /// Conflict guards: if dragging, fall back to immediate removal; the target's entrance animation is
    /// stopped first so it cannot keep overriding Opacity/transform.
    /// </summary>
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

        // Collect survivors below the target and freeze them (no hover) while the slide runs.
        double spacing = (container is StackPanel sp) ? sp.Spacing : 0;
        double targetHeight = target.ActualHeight > 0 ? target.ActualHeight : 0;
        if (targetHeight <= 0)
        {
            container.Children.Remove(target);
            onDone?.Invoke();
            return;
        }
        // After the placeholder is removed, EVERY survivor moves up by the placeholder's slot:
        // its own height plus the inter-card spacing (StackPanel Spacing).
        double slideAmount = targetHeight + spacing;

        var survivors = new List<Microsoft.UI.Xaml.Controls.Border>();
        var transforms = new List<TranslateTransform>();
        for (int i = index + 1; i < container.Children.Count; i++)
        {
            if (container.Children[i] is not Microsoft.UI.Xaml.Controls.Border b) continue;
            survivors.Add(b);
            if (b.RenderTransform is not TranslateTransform tt) { tt = new TranslateTransform(); b.RenderTransform = tt; }
            transforms.Add(tt);
            b.IsHitTestVisible = false; // freeze hover so it cannot race the slide (hover edits RenderTransform.Y)
        }

        const double dur = 200; // all animations aligned so none ends mid-way
        var sb = new Storyboard();
        // Fade the target out (placeholder stays in layout -> no mid-animation re-flow).
        var fade = new DoubleAnimation { To = 0, Duration = TimeSpan.FromMilliseconds(dur), EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn } };
        Storyboard.SetTarget(fade, target); Storyboard.SetTargetProperty(fade, "Opacity");
        sb.Children.Add(fade);

        // Slide survivors up by the placeholder's slot (height + spacing), parallel with the fade.
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
                // Remove placeholder + reset survivors' transforms in the SAME callback: the layout re-flow
                // (survivors move up one card height) cancels the translate reset -> no visible jump.
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

    /// <summary>Whether the pointer press originated from an interactive child control (open/copy/expand button)
    /// of a draggable card. Used by the drag-reorder gesture to avoid arming drag when the user clicks a button.</summary>
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

    /// <summary>
    /// Unified string accessor for localized UI text. Backed by AppResources (C# dictionaries):
    /// WinUI3 ResourceDictionary.TryGetValue breaks with SEHException after runtime MergedDictionaries.Add
    /// (0x80004005), so language dictionaries are plain C# maps. Missing key returns the key itself.
    /// </summary>
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

    /// <summary>
    /// Content-aware character spacing (4.0 #6). Per-glyph spacing only looks right for CJK square
    /// glyphs; Latin/Korean/Japanese content is narrow and spacing pushes words apart. The decision
    /// is therefore made from the TEXT ITSELF, not the UI language: a user on the Chinese UI can still
    /// type English/Korean/Japanese entries, and those must not get CJK spacing.
    /// </summary>
    public static int GetCharacterSpacing(string text, int cjkValue)
    {
        if (string.IsNullOrEmpty(text)) return 0;
        foreach (var c in text)
        {
            // Latin letters (A-Z / a-z)
            if ((c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z')) return 0;
            // Hangul syllables + jamo (Korean)
            if (c >= '\uAC00' && c <= '\uD7AF') return 0;
            if (c >= '\u1100' && c <= '\u11FF') return 0;
            // Hiragana + Katakana (Japanese)
            if (c >= '\u3040' && c <= '\u30FF') return 0;
        }
        return cjkValue; // pure CJK / digits / punctuation keep the CJK spacing
    }

    
    public static void ShowToast(string message)
    {
        UiQueue?.TryEnqueue(() => MainWindow?.ShowToast(message));
    }

    /// <summary>
    /// Assign localized text for controls carrying Tag="Key" (used by MainWindow, where x:Bind is
    /// unsupported because Window is not a Page). TextBlock.Tag -> Text, TextBox.Tag -> PlaceholderText.
    /// </summary>
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

    /// <summary>
    /// Apply UI language. Empty string means "follow the Windows display language"
    /// (AppLanguage contract: empty = follow system, stored as such; effective language
    /// is resolved here). Language switch follows the theme restart loop (B-8).
    /// </summary>
    public static void ApplyLanguage(string language)
    {
        CurrentLanguage = string.IsNullOrEmpty(language) ? ResolveSystemLanguage() : language;
    }

    /// <summary>
    /// Resolve the UI language from the Windows display language when AppLanguage is empty:
    /// zh-Hant/zh-TW/zh-HK/zh-MO map to zh-TW, zh-Hans/zh-CN to zh-CN, en-* to en-US,
    /// ko-* to ko-KR, ja-* to ja-JP; any other locale falls back to en-US (foreign users).
    /// </summary>
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

        // E1-25: never fall back to the global (system-theme) index (2.6 forbids it - it would resolve
        // by the OS theme, decoupled from the app theme). Try the other theme dictionary, else null.
        
        try
        {
            var other = dictKey == "Dark" ? "Light" : "Dark";
            if (Current.Resources.ThemeDictionaries[other] is ResourceDictionary od && od[key] is SolidColorBrush ob)
                return ob;
        }
        catch { }
        return null!; // missing key - surfaces immediately in dev (caller NRE), better than silently wrong theme
    }

    private static bool IsSystemLight()
    {
        // Registry is the authoritative source WinUI uses for ActualTheme; GetSysColor(COLOR_WINDOW)
        // stays white under Windows dark mode and misjudges dark as light (historical bug).
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

    /// <summary>
    /// Initializes the singleton application object.  This is the first line of authored code
    /// executed, and as such is the logical equivalent of main() or WinMain().
    /// </summary>
    public App()
    {

        InitializeComponent();
    }

    /// <summary>
    /// Invoked when the application is launched.
    /// </summary>
    /// <param name="args">Details about the launch request and process.</param>
    protected override void OnLaunched(Microsoft.UI.Xaml.LaunchActivatedEventArgs args)
    {
        UiQueue = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread(); // capture the UI-thread queue for background callbacks (theme watcher etc.)

        Services.CrashLogger.Init(); 
        Services.Loc.T = GetString; 

        ParseLaunchArgs();

        if (!Novara.MainWindow.AcquireSingleInstance())
        {
            // Pass any pending right-click action to the running instance before exiting.
            if (!string.IsNullOrWhiteSpace(App.PendingAddPath))
                Services.AddPathRequest.Raise(App.PendingAddPath);
            if (!string.IsNullOrWhiteSpace(App.PendingImportMd))
                Services.MdImportRequest.Raise(App.PendingImportMd);
            if (App.PendingReminderId.HasValue)
                Services.ReminderDueRequest.Raise(App.PendingReminderId.Value);
            Environment.Exit(0);
            return;
        }

        // Store singleton: construct only, defer all IO (file read / MD5 / decrypt / registry / tray / Win32)
        // to the MainWindow root-element Loaded event. Calling native APIs during early CLR init crashes
        // the runtime on Win10 19041 with .NET10 preview (0x80131506, CET compat defect, CLR killed by OS).
        Store = new NovaraStore(NovaraStore.DefaultFilePath);

        if (McpBackground)
        {
            
            
            UiQueue.TryEnqueue(() =>
            {
                try
                {
                    var loadResult = Store.Load();
                    System.Diagnostics.Debug.WriteLine($"MCP background load: {loadResult.Status}"); // N5W2-02: surface the reason when the gate below exits
                    // N4C-03: respect the master switch - a leftover agent config must not silently spawn
                    // an invisible resident process when MCP is disabled in settings.
                    if (!Store.Database.AppSettings.McpEnabled)
                    {
                        Environment.Exit(0);
                        return;
                    }
                    McpService.Start();
                }
                catch (Exception ex)
                {
                    // N5W2-02: a background instance with no window/tray/service is an unkillable-by-UI
                    // zombie - persist a diagnostic log, then exit instead of lingering.
                    Services.CrashLogger.LogBackgroundStartupFailure(ex);
                    Environment.Exit(0);
                }
            });
            ShowWindowRequest.StartListening(() => UiQueue.TryEnqueue(ShowMainWindowFromBackground));
            return;
        }

        StartRequestListeners();
        MainWindow = new MainWindow();
        MainWindow.Activate();
    }

    private static void StartRequestListeners()
    {
        // Stage-2 edit jump: single-instance bridge. The running instance listens for
        // requests from StickNoteHost; a request raised while the app was closed is
        // consumed on the main window Loaded event (data is ready by then).
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

    /// <summary>
    /// Sticky-note theme source: when the app theme is "follow system", keep stickies.json
    /// in sync with the OS theme. Cold-start push + runtime watch (UISettings.ColorValuesChanged,
    /// fires on system theme/accent changes). Manual themes (light/dark) are never touched.
    /// Idempotent - safe to call again after unlock.
    /// </summary>
    public static void InitSystemThemeWatcher()
    {
        if (_systemThemeWatcherStarted) return;
        _systemThemeWatcherStarted = true;

        try
        {
            
            
            _systemThemeUISettings = new Windows.UI.ViewManagement.UISettings();
            _systemThemeUISettings.ColorValuesChanged += (_, _) =>
            {
                if (CurrentTheme != "跟随系统") return; // manual themes ignore the OS
                var now = DateTime.Now;
                if ((now - _lastSystemThemeSync).TotalMilliseconds < 500) return; // debounce
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

        // Cold start: the stickies.json snapshot may be stale (OS theme changed while Novara was off).
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
        // Apply stored language only when present; an empty value must not override the
        // plain-text language hint applied earlier (language.dat survives encrypted startup).
        if (!string.IsNullOrEmpty(settings.AppLanguage)) ApplyLanguage(settings.AppLanguage); // i18n: apply UI language (plain startup / after unlock unified entry)
        if (!string.IsNullOrEmpty(settings.Theme)) SetTheme(settings.Theme);
        if (settings.AutoStart) StartupService.Enable();
        else StartupService.Disable();
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
