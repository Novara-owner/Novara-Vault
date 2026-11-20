using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.UI;
using ShapePath = Microsoft.UI.Xaml.Shapes.Path;
using Shape = Microsoft.UI.Xaml.Shapes.Shape;

namespace Novara.Services;







public static class SealIconFactory
{

    public static Viewbox Create(SealGlyphs.Glyph glyph, double size = 32)
        => Create(glyph, size, 1024, 1024);







    public static Viewbox Create(SealGlyphs.Glyph glyph, double size, double canvasWidth, double canvasHeight)
    {
        var canvas = new Grid { Width = canvasWidth, Height = canvasHeight };
        foreach (var layer in glyph.Layers)
        {
            canvas.Children.Add(new ShapePath
            {



                Data = Novara.App.CreateGeometry(layer.Data),
                Fill = new SolidColorBrush(ParseColor(layer.Fill)),
                Stretch = Stretch.None,
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Top,
            });
        }


        return new Viewbox
        {
            Width = size * canvasWidth / canvasHeight,
            Height = size,
            Stretch = Stretch.Uniform,
            Child = canvas,
        };
    }




    public static ShapePath CreateLayerFromResource(string pathData, string brushKey, double opacity = 1.0)
    {
        var path = new ShapePath
        {
            Data = Novara.App.CreateGeometry(pathData),
            Stretch = Stretch.None,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
        };
        if (opacity < 1.0) path.Opacity = opacity;


        path.Fill = App.GetBrush(brushKey);
        return path;
    }


    private static Color ParseColor(string hex)
    {
        if (hex.Length != 6
            || !byte.TryParse(hex.AsSpan(0, 2), System.Globalization.NumberStyles.HexNumber, null, out var r)
            || !byte.TryParse(hex.AsSpan(2, 2), System.Globalization.NumberStyles.HexNumber, null, out var g)
            || !byte.TryParse(hex.AsSpan(4, 2), System.Globalization.NumberStyles.HexNumber, null, out var b))
        {
            return Color.FromArgb(0xFF, 0x9B, 0x9D, 0xA4);
        }

        return Color.FromArgb(0xFF, r, g, b);
    }
}
