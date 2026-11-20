using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace Novara.Services;








public static class EmptyStateArt
{

    public const string ClickBody = "M572.586667 982.357333a383.488 383.488 0 0 1-135.168-27.648C324.266667 912.554667 233.130667 771.754667 187.733333 691.712a104.106667 104.106667 0 0 1 27.818667-132.096 109.909333 109.909333 0 0 1 138.069333 0V366.08a81.066667 81.066667 0 0 1 161.962667 0v57.514667a81.066667 81.066667 0 0 1 105.472 46.592 79.701333 79.701333 0 0 1 34.133333-7.850667 80.896 80.896 0 0 1 75.605334 52.224 80.896 80.896 0 0 1 116.053333 72.874667v174.250666a139.264 139.264 0 0 1-24.234667 81.237334c-26.794667 37.888-69.973333 88.064-124.245333 111.274666a316.757333 316.757333 0 0 1-125.781333 28.16z m-117.248-75.605333c86.698667 32.426667 149.674667 32.426667 224.426666 0 45.397333-19.456 83.797333-67.072 102.4-93.696a88.917333 88.917333 0 0 0 14.848-51.2V587.434667a29.866667 29.866667 0 0 0-59.562666 0 25.6 25.6 0 0 1-51.2 0v-44.202667a29.696 29.696 0 0 0-59.392 0 25.6 25.6 0 0 1-51.2 0v-44.202667a29.866667 29.866667 0 1 0-59.562667 0 25.6 25.6 0 0 1-51.2 0v-132.949333a29.866667 29.866667 0 0 0-59.562667 0v254.634667a25.6 25.6 0 0 1-43.690666 18.090666l-34.133334-34.133333a58.88 58.88 0 0 0-78.848-4.437333 53.248 53.248 0 0 0-15.018666 67.242666C274.432 740.693333 358.4 870.4 455.338667 906.752z";


    public const string ClickAccent = "M313.514667 269.994667a25.770667 25.770667 0 0 1-18.090667-7.509334l-66.389333-66.389333a25.6 25.6 0 0 1 36.181333-36.181333l66.389333 66.389333a25.6 25.6 0 0 1-18.090666 43.690667z M424.277333 225.621333A25.6 25.6 0 0 1 399.018667 204.8l-22.186667-132.778667a25.6 25.6 0 1 1 51.2-8.533333l22.186667 132.949333a25.6 25.6 0 0 1-21.845334 28.842667 17.066667 17.066667 0 0 1-4.096 0.341333z M534.869333 247.808a25.941333 25.941333 0 0 1-14.165333-4.266667 25.6 25.6 0 0 1-6.997333-35.498666l44.202666-66.389334a25.6 25.6 0 0 1 42.666667 29.013334l-44.373333 66.389333a25.258667 25.258667 0 0 1-21.333334 10.752z";


    public const string TrashBody = "M992 192H32c-19.2 0-32 19.2-32 32s12.8 32 32 32h140.8v652.8c0 57.6 44.8 102.4 102.4 102.4h480c57.6 0 102.4-44.8 102.4-102.4V441.6c0-19.2-12.8-32-32-32s-32 12.8-32 32v467.2c0 19.2-19.2 38.4-38.4 38.4H614.4V640c0-19.2-12.8-32-32-32s-32 12.8-32 32v313.6H441.6V435.2c0-19.2-12.8-32-32-32s-32 12.8-32 32v518.4H268.8c-19.2 0-38.4-19.2-38.4-38.4V256h755.2c25.6 0 38.4-12.8 38.4-32s-12.8-32-32-32z";


    public const string TrashAccent = "M409.6 76.8h204.8c19.2 0 32-12.8 32-32S633.6 12.8 614.4 12.8H409.6c-19.2 0-32 12.8-32 32s12.8 32 32 32z";


    public const string SearchBody = "M490.666667 48c244.48 0 442.666667 198.186667 442.666666 442.666667 0 109.44-39.722667 209.621333-105.536 286.890666l135.274667 135.317334a37.333333 37.333333 0 0 1-50.133333 55.210666l-2.666667-2.410666-135.509333-135.509334A440.874667 440.874667 0 0 1 490.666667 933.333333C246.186667 933.333333 48 735.146667 48 490.666667S246.186667 48 490.666667 48z m0 74.666667C287.424 122.666667 122.666667 287.424 122.666667 490.666667c0 203.242667 164.757333 368 368 368 203.242667 0 368-164.757333 368-368 0-203.242667-164.757333-368-368-368z";


    public const string SearchAccent = "M517.76 213.034667a29.866667 29.866667 0 0 1 35.285333-23.232 307.136 307.136 0 0 1 178.368 110.037333 29.866667 29.866667 0 0 1-46.784 37.12 247.402667 247.402667 0 0 0-143.658666-88.64 29.866667 29.866667 0 0 1-23.210667-35.285333z";


    public const string NoResultBody = "M878.0032 1000.727273h-726.109091c-60.509091 0-107.054545-23.272727-134.981818-65.163637-23.272727-41.890909-23.272727-97.745455 4.654545-148.945454l372.363637-656.290909C421.857745 79.127273 463.748655 46.545455 514.948655 46.545455s93.090909 27.927273 121.018181 83.781818l367.709091 656.290909c27.927273 51.2 32.581818 107.054545 4.654546 148.945454-23.272727 41.890909-69.818182 65.163636-130.327273 65.163637z M514.948655 107.054545c-27.927273 0-51.2 18.618182-69.818182 51.2l-367.709091 656.29091c-18.618182 32.581818-18.618182 65.163636-4.654546 88.436363s41.890909 37.236364 83.781819 37.236364H882.657745c37.236364 0 65.163636-13.963636 83.781819-37.236364 13.963636-23.272727 9.309091-55.854545-4.654546-88.436363l-377.018182-656.29091c-18.618182-32.581818-41.890909-51.2-69.818181-51.2z M463.748655 330.472727c0-9.309091 4.654545-23.272727 13.963636-32.581818s23.272727-13.963636 37.236364-13.963636 27.927273 4.654545 37.236363 13.963636c9.309091 9.309091 13.963636 23.272727 13.963637 32.581818v9.309091c0 9.309091-4.654545 23.272727-13.963637 32.581818-9.309091 9.309091-23.272727 13.963636-37.236363 13.963637s-27.927273-4.654545-37.236364-13.963637c-9.309091-9.309091-13.963636-18.618182-13.963636-32.581818v-9.309091c0 4.654545 0 0 0 0z";


    public const string NoResultAccent = "M547.530473 795.927273c0 18.618182-13.963636 32.581818-32.581818 32.581818s-32.581818-13.963636-32.581819-32.581818v-307.2c0-18.618182 13.963636-32.581818 32.581819-32.581818 18.618182 0 32.581818 13.963636 32.581818 32.581818v307.2z";

}







public static class EmptyHintBuilder
{

    public const double ArtSize = 100;


    public const double Dim = 0.6;





    public static StackPanel Build(string bodyPath, string accentPath, double canvasWidth, double canvasHeight,
        string title, string? subtitle = null)
    {
        var root = new StackPanel
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Spacing = 14,
            MaxWidth = 460,
        };

        var canvas = new Grid { Width = canvasWidth, Height = canvasHeight };
        var body = SealIconFactory.CreateLayerFromResource(bodyPath, "AppTextSecondaryBrush");
        body.Opacity = Dim;
        canvas.Children.Add(body);

        canvas.Children.Add(SealIconFactory.CreateLayerFromResource(accentPath, "AppPrimaryButtonBrush"));
        root.Children.Add(new Viewbox
        {
            Height = ArtSize,
            Width = ArtSize * canvasWidth / canvasHeight,
            Stretch = Stretch.Uniform,
            HorizontalAlignment = HorizontalAlignment.Center,
            Child = canvas,
        });

        root.Children.Add(MakeText(title, 15, "AppTextPrimaryBrush",
            Microsoft.UI.Text.FontWeights.SemiBold, null));
        if (!string.IsNullOrWhiteSpace(subtitle))
            root.Children.Add(MakeText(subtitle, 12, "AppTextSecondaryBrush", null, new Thickness(0, 2, 0, 0)));
        return root;
    }


    public static StackPanel BuildText(string title, string? subtitle = null)
    {
        var root = new StackPanel
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Spacing = 14,
            MaxWidth = 460,
        };
        root.Children.Add(MakeText(title, 15, "AppTextPrimaryBrush",
            Microsoft.UI.Text.FontWeights.SemiBold, null));
        if (!string.IsNullOrWhiteSpace(subtitle))
            root.Children.Add(MakeText(subtitle, 12, "AppTextSecondaryBrush", null, new Thickness(0, 2, 0, 0)));
        return root;
    }


    private static TextBlock MakeText(string text, double fontSize, string brushKey,
        Windows.UI.Text.FontWeight? weight, Thickness? margin)
    {
        var block = new TextBlock
        {
            Text = text,
            FontSize = fontSize,
            TextAlignment = TextAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
            MaxWidth = 420,
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        if (weight is { } w) block.FontWeight = w;
        if (margin is { } m) block.Margin = m;
        block.Foreground = App.GetBrush(brushKey);



        block.Opacity = 1;
        return block;
    }
}
