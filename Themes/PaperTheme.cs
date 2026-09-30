using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;

namespace Novara;

public static class PaperTheme
{

    public static readonly string[] Names =
    {
        "类纸 · 米黄",
        "类纸 · 淡杏",
        "类纸 · 牛皮",
        "类纸 · 冷灰",
    };



    public const string DefaultPaperName = "类纸 · 淡杏";


    public static readonly string[] LabelKeys =
    {
        "Setting_Theme_Paper_Cream",
        "Setting_Theme_Paper_Almond",
        "Setting_Theme_Paper_Kraft",
        "Setting_Theme_Paper_Newsprint",
    };


    public static bool IsActive => App.CurrentTheme != null && App.CurrentTheme.StartsWith("类纸", StringComparison.Ordinal);




    public static Windows.UI.Color BrandColor
    {
        get
        {
            if (IsActive && App.CurrentTheme != null && Palettes.TryGetValue(App.CurrentTheme, out var t)
                && t.TryGetValue("AppAccentBrush", out var hex))
                return ParseColor(hex);
            return Windows.UI.Color.FromArgb(0xFF, 0x72, 0x76, 0xFF);
        }
    }




    public static string? CurrentStickyColorKey
    {
        get
        {
            if (!IsActive) return null;
            return App.CurrentTheme switch
            {
                "类纸 · 米黄" => "theme-cream",
                "类纸 · 淡杏" => "theme-almond",
                "类纸 · 牛皮" => "theme-kraft",
                "类纸 · 冷灰" => "theme-news",
                _ => null,
            };
        }
    }

    private static string HintPath => System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        Services.CoreEnv.DataDirName, "paper.dat");




    public static void ApplyIfNeeded()
    {
        try
        {
            var hint = ReadHint();
            if (hint == null || !IsPaperName(hint)) return;
            if (!Palettes.TryGetValue(hint, out var table)) return;
            var dict = Application.Current.Resources.ThemeDictionaries["Light"] as ResourceDictionary;
            if (dict == null) return;

            foreach (var (key, value) in table)
            {
                try
                {
                    var current = dict[key];
                    switch (current)
                    {
                        case LinearGradientBrush:
                            var parts = value.Split(' ');
                            dict[key] = MakeVerticalGradient(parts[0], parts[1]);
                            break;
                        case Windows.UI.Color:
                            dict[key] = ParseColor(value);
                            break;
                        case SolidColorBrush:
                            dict[key] = new SolidColorBrush(ParseColor(value));
                            break;
                    }
                }
                catch (Exception ex)
                {

                    System.Diagnostics.Debug.WriteLine($"PaperTheme key {key} apply failed: {ex.Message}");
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"PaperTheme apply failed (falls back to plain light): {ex.Message}");
        }
    }






    public static void SeedDefaultHintForFreshInstall()
    {
        try
        {
            if (System.IO.File.Exists(HintPath)) return;
            if (System.IO.File.Exists(Services.NovaraStore.DefaultFilePath)) return;
            WriteHint(DefaultPaperName);
        }
        catch { }
    }



    public static void WriteHint(string theme)
    {
        try
        {
            var dir = System.IO.Path.GetDirectoryName(HintPath);
            if (!string.IsNullOrEmpty(dir) && !System.IO.Directory.Exists(dir)) System.IO.Directory.CreateDirectory(dir);
            System.IO.File.WriteAllText(HintPath, theme ?? "");
        }
        catch { }
    }

    private static string? ReadHint()
    {
        try
        {
            return System.IO.File.Exists(HintPath) ? System.IO.File.ReadAllText(HintPath).Trim() : null;
        }
        catch { return null; }
    }

    public static bool IsPaperName(string? theme)
        => !string.IsNullOrEmpty(theme) && theme.StartsWith("类纸", StringComparison.Ordinal);





    public static bool SyncHintWith(string? theme)
    {
        if (string.IsNullOrEmpty(theme)) return false;
        if (theme == ReadHint()) return false;
        WriteHint(theme);
        return true;
    }


    public static string? PeekHint() => ReadHint();


    public static bool TryGetLabelKey(string? name, out string key)
    {
        var idx = name == null ? -1 : Array.IndexOf(Names, name);
        if (idx >= 0) { key = LabelKeys[idx]; return true; }
        key = "";
        return false;
    }





    public static bool TryGetEditorColors(out string bg, out string text, out string placeholder, out string sep,
        out string selection, out string scrollbar, out string scrollbarHover, out string menuBg, out string link)
    {
        bg = text = placeholder = sep = selection = scrollbar = scrollbarHover = menuBg = link = "";
        if (!IsActive) return false;
        if (App.CurrentTheme == null || !Palettes.TryGetValue(App.CurrentTheme, out var table)) return false;

        bg = ToRgbHex(table["AppSurfaceBrush"]);
        menuBg = ToRgbHex(table["AppSurfaceElevatedBrush"]);
        link = ToRgbHex(table["AppAccentBrush"]);
        var (tr, tg, tb) = Rgb(table["AppTextPrimaryBrush"]);
        text = $"rgba({tr},{tg},{tb},0.87)";
        placeholder = $"rgba({tr},{tg},{tb},0.35)";
        sep = $"rgba({tr},{tg},{tb},0.12)";
        selection = $"rgba({tr},{tg},{tb},0.15)";
        var (sr, sg, sbl) = Rgb(table["AppScrollBarThumbFill"]);
        scrollbar = $"rgba({sr},{sg},{sbl},{AlphaOf(table["AppScrollBarThumbFill"])})";
        scrollbarHover = $"rgba({sr},{sg},{sbl},0.7)";
        return true;
    }


    private static string ToRgbHex(string hex) => "#" + hex.TrimStart('#').Substring(2, 6);


    private static (int r, int g, int b) Rgb(string hex)
    {
        var v = hex.TrimStart('#');
        return (Convert.ToInt32(v.Substring(2, 2), 16), Convert.ToInt32(v.Substring(4, 2), 16), Convert.ToInt32(v.Substring(6, 2), 16));
    }

    private static string AlphaOf(string hex)
    {
        var a = Convert.ToInt32(hex.TrimStart('#').Substring(0, 2), 16) / 255.0;
        return a.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);
    }

    private static Windows.UI.Color ParseColor(string hex)
    {



        var v = hex.TrimStart('#');
        if (v.Length == 6) v = "FF" + v;
        return Windows.UI.Color.FromArgb(
            Convert.ToByte(v.Substring(0, 2), 16),
            Convert.ToByte(v.Substring(2, 2), 16),
            Convert.ToByte(v.Substring(4, 2), 16),
            Convert.ToByte(v.Substring(6, 2), 16));
    }

    private static LinearGradientBrush MakeVerticalGradient(string topHex, string bottomHex)
    {
        var b = new LinearGradientBrush { StartPoint = new global::Windows.Foundation.Point(0, 0), EndPoint = new global::Windows.Foundation.Point(0, 1) };
        b.GradientStops.Add(new GradientStop { Color = ParseColor(topHex), Offset = 0 });
        b.GradientStops.Add(new GradientStop { Color = ParseColor(bottomHex), Offset = 1 });
        return b;
    }







    private static readonly Dictionary<string, Dictionary<string, string>> Palettes = new()
    {
        ["类纸 · 米黄"] = new()
        {
            ["IconForegroundBrush"] = "#DD4A4234",
            ["AppSurfaceBrush"] = "#FFF5EFE0",
            ["AppSurfaceElevatedBrush"] = "#FFFBF7EC",
            ["AppSurfaceOverlayBrush"] = "#FFFCF8EF",
            ["AppHoverBrush"] = "#FFF5EFE0",
            ["AppBorderBrush"] = "#66C4B89C",
            ["NavFaceRaisedBrush"] = "#FFF9F4E9",
            ["AppTextPrimaryBrush"] = "#DD3D3527",
            ["AppTextSecondaryBrush"] = "#997A6F58",
            ["AppTextTertiaryBrush"] = "#8A7A6F58",
            ["AppAccentBrush"] = "#FF6E72B4",
            ["AppAccentPressedBrush"] = "#CC5A5DA0",
            ["AppPrimaryButtonBrush"] = "#FF6E72B4",
            ["AppPrimaryButtonHoverBrush"] = "#FF8588C4",
            ["AppPrimaryButtonPressedBrush"] = "#FF5A5DA0",
            ["AppDialogBorderBrush"] = "#FF6E72B4",
            ["AppDangerTextBrush"] = "#FFA34747",
            ["AppDangerBrush"] = "#CC9E4444",
            ["AppDangerHoverBrush"] = "#FFB65656",
            ["AppDangerPressedBrush"] = "#CC813939",
            ["AppDialogBgBrush"] = "#FFFDFAF2 #FFF3EEDF",
            ["CarouselFadeColor"] = "#FFFAF5E8",
            ["CarouselFadeColorTransparent"] = "#00FAF5E8",
            ["AppScrollBarThumbFill"] = "#736E72B4",
            ["AppScrollBarThumbFillDisabled"] = "#406E72B4",
            ["AppScrollBarTrackFillPointerOver"] = "#0F3D3527",
        },
        ["类纸 · 淡杏"] = new()
        {
            ["IconForegroundBrush"] = "#DD4A4234",
            ["AppSurfaceBrush"] = "#FFF6F1E8",
            ["AppSurfaceElevatedBrush"] = "#FFFCF8F0",
            ["AppSurfaceOverlayBrush"] = "#FFFDF9F2",
            ["AppHoverBrush"] = "#FFF6F1E8",
            ["AppBorderBrush"] = "#66C8BEA8",
            ["NavFaceRaisedBrush"] = "#FFFAF5EC",
            ["AppTextPrimaryBrush"] = "#DD3D3527",
            ["AppTextSecondaryBrush"] = "#997D7360",
            ["AppTextTertiaryBrush"] = "#8A7D7360",
            ["AppAccentBrush"] = "#FF7072BC",
            ["AppAccentPressedBrush"] = "#CC5C5EA8",
            ["AppPrimaryButtonBrush"] = "#FF7072BC",
            ["AppPrimaryButtonHoverBrush"] = "#FF878AC6",
            ["AppPrimaryButtonPressedBrush"] = "#FF5C5EA8",
            ["AppDialogBorderBrush"] = "#FF7072BC",
            ["AppDangerTextBrush"] = "#FFA04545",
            ["AppDangerBrush"] = "#CC9A4242",
            ["AppDangerHoverBrush"] = "#FFB05252",
            ["AppDangerPressedBrush"] = "#CC7D3636",
            ["AppDialogBgBrush"] = "#FFFEFBF5 #FFF4EFE3",
            ["CarouselFadeColor"] = "#FFFAF6EC",
            ["CarouselFadeColorTransparent"] = "#00FAF6EC",
            ["AppScrollBarThumbFill"] = "#737072BC",
            ["AppScrollBarThumbFillDisabled"] = "#407072BC",
            ["AppScrollBarTrackFillPointerOver"] = "#0F3D3527",
        },
        ["类纸 · 牛皮"] = new()
        {
            ["IconForegroundBrush"] = "#DD453D2E",
            ["AppSurfaceBrush"] = "#FFEDE3CE",
            ["AppSurfaceElevatedBrush"] = "#FFF6EFDF",
            ["AppSurfaceOverlayBrush"] = "#FFF8F1E2",
            ["AppHoverBrush"] = "#FFEDE3CE",
            ["AppBorderBrush"] = "#66BBAE92",
            ["NavFaceRaisedBrush"] = "#FFF4ECD9",
            ["AppTextPrimaryBrush"] = "#DD3A3225",
            ["AppTextSecondaryBrush"] = "#99776B52",
            ["AppTextTertiaryBrush"] = "#8A776B52",
            ["AppAccentBrush"] = "#FF6669A8",
            ["AppAccentPressedBrush"] = "#CC525494",
            ["AppPrimaryButtonBrush"] = "#FF6669A8",
            ["AppPrimaryButtonHoverBrush"] = "#FF7B7EB8",
            ["AppPrimaryButtonPressedBrush"] = "#FF525494",
            ["AppDialogBorderBrush"] = "#FF6669A8",
            ["AppDangerTextBrush"] = "#FFAC4B4B",
            ["AppDangerBrush"] = "#CCA54848",
            ["AppDangerHoverBrush"] = "#FFBD5757",
            ["AppDangerPressedBrush"] = "#CC863B3B",
            ["AppDialogBgBrush"] = "#FFFBF6E9 #FFEFE7D2",
            ["CarouselFadeColor"] = "#FFF7F1E0",
            ["CarouselFadeColorTransparent"] = "#00F7F1E0",
            ["AppScrollBarThumbFill"] = "#736669A8",
            ["AppScrollBarThumbFillDisabled"] = "#406669A8",
            ["AppScrollBarTrackFillPointerOver"] = "#0F3A3225",
        },
        ["类纸 · 冷灰"] = new()
        {
            ["IconForegroundBrush"] = "#DD3B3A33",
            ["AppSurfaceBrush"] = "#FFF1EFE6",
            ["AppSurfaceElevatedBrush"] = "#FFF9F8F1",
            ["AppSurfaceOverlayBrush"] = "#FFFAF9F3",
            ["AppHoverBrush"] = "#FFF1EFE6",
            ["AppBorderBrush"] = "#66C1BEAE",
            ["NavFaceRaisedBrush"] = "#FFF6F5EE",
            ["AppTextPrimaryBrush"] = "#DD33322C",
            ["AppTextSecondaryBrush"] = "#9973716A",
            ["AppTextTertiaryBrush"] = "#8A73716A",
            ["AppAccentBrush"] = "#FF6A6EB8",
            ["AppAccentPressedBrush"] = "#CC565AB0",
            ["AppPrimaryButtonBrush"] = "#FF6A6EB8",
            ["AppPrimaryButtonHoverBrush"] = "#FF7F83C8",
            ["AppPrimaryButtonPressedBrush"] = "#FF565AB0",
            ["AppDialogBorderBrush"] = "#FF6A6EB8",
            ["AppDangerTextBrush"] = "#FF9C4242",
            ["AppDangerBrush"] = "#CC944044",
            ["AppDangerHoverBrush"] = "#FFAA4F4F",
            ["AppDangerPressedBrush"] = "#CC773535",
            ["AppDialogBgBrush"] = "#FFFBFAF4 #FFEFEDE1",
            ["CarouselFadeColor"] = "#FFF5F3EA",
            ["CarouselFadeColorTransparent"] = "#00F5F3EA",
            ["AppScrollBarThumbFill"] = "#736A6EB8",
            ["AppScrollBarThumbFillDisabled"] = "#406A6EB8",
            ["AppScrollBarTrackFillPointerOver"] = "#0F33322C",
        },
    };
}
