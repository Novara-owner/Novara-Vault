using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;

namespace Novara.Services;










public static class SealEmptyStateBuilder
{

    public const double FrameWidth = 560;
    public const double FrameHeight = 300;


    public const double Dim = 0.6;


    public const double ArtSize = 100;







    public static StackPanel BuildLocked(string title, string hint)
    {
        var frame = BuildFrame();
        var content = BuildContentStack();

        var icon = new FontIcon
        {
            Glyph = "\uE72E",
            FontSize = 28,
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        icon.Foreground = App.GetBrush("AppTextSecondaryBrush");
        icon.Opacity = Dim;
        content.Children.Add(icon);

        AddDimmedText(content, title, 15, "AppTextPrimaryBrush",
            Microsoft.UI.Text.FontWeights.SemiBold);
        AddDimmedText(content, hint, 13, "AppTextSecondaryBrush", null, new Thickness(0, 2, 0, 0));

        frame.Children.Add(content);

        var root = new StackPanel
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Spacing = 16,
        };
        root.Children.Add(frame);
        return root;
    }






    public static StackPanel Build(string title, string warning, string hint,
        string actionText, RoutedEventHandler onAction)
    {
        var frame = BuildFrame();
        var content = BuildContentStack();


        var artCanvas = new Grid { Width = SealEmptyArt.CanvasWidth, Height = SealEmptyArt.CanvasHeight };
        var hand = SealIconFactory.CreateLayerFromResource(SealEmptyArt.HandPath, "AppTextSecondaryBrush");
        hand.Opacity = Dim;
        artCanvas.Children.Add(hand);
        artCanvas.Children.Add(SealIconFactory.CreateLayerFromResource(SealEmptyArt.ArrowPath, "AppPrimaryButtonBrush"));
        var art = new Viewbox
        {
            Height = ArtSize,
            Width = ArtSize * SealEmptyArt.CanvasWidth / SealEmptyArt.CanvasHeight,
            Stretch = Stretch.Uniform,
            HorizontalAlignment = HorizontalAlignment.Center,
            Child = artCanvas,
        };
        content.Children.Add(art);


        AddDimmedText(content, title, 15, "AppTextPrimaryBrush",
            Microsoft.UI.Text.FontWeights.SemiBold);

        if (!string.IsNullOrWhiteSpace(warning))
            AddDimmedText(content, warning, 12, "AppTextSecondaryBrush", null, new Thickness(0, 4, 0, 0));

        frame.Children.Add(content);

        var root = new StackPanel
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Spacing = 16,
        };
        root.Children.Add(frame);

        if (!string.IsNullOrWhiteSpace(hint))
        {
            var hintText = new TextBlock
            {
                Text = hint,
                FontSize = 13,
                TextAlignment = TextAlignment.Center,
                TextWrapping = TextWrapping.Wrap,
                MaxWidth = FrameWidth,
                HorizontalAlignment = HorizontalAlignment.Center,
            };
            hintText.Foreground = App.GetBrush("AppTextSecondaryBrush");
            root.Children.Add(hintText);
        }

        var action = new Button
        {
            Content = actionText,
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        action.Style = (Style)Application.Current.Resources["NovaraTextButtonStyle"];
        action.Click += onAction;
        root.Children.Add(action);

        return root;
    }





    public static StackPanel BuildNotice(TextBlock title, string hint, string actionText, RoutedEventHandler onAction)
    {
        var root = new StackPanel
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Spacing = 16,
        };
        title.TextAlignment = TextAlignment.Center;
        title.TextWrapping = TextWrapping.Wrap;
        title.MaxWidth = FrameWidth - 80;
        title.HorizontalAlignment = HorizontalAlignment.Center;
        root.Children.Add(title);

        if (!string.IsNullOrWhiteSpace(hint))
        {
            var hintText = new TextBlock
            {
                Text = hint,
                FontSize = 13,
                TextAlignment = TextAlignment.Center,
                TextWrapping = TextWrapping.Wrap,
                MaxWidth = FrameWidth - 80,
                HorizontalAlignment = HorizontalAlignment.Center,
            };
            hintText.Foreground = App.GetBrush("AppTextSecondaryBrush");
            root.Children.Add(hintText);
        }

        var action = new Button
        {
            Content = actionText,
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        action.Style = (Style)Application.Current.Resources["NovaraTextButtonStyle"];
        action.Click += onAction;
        root.Children.Add(action);
        return root;
    }


    private static Grid BuildFrame()
    {
        var frame = new Grid
        {
            Width = FrameWidth,
            Height = FrameHeight,
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        frame.Children.Add(new Rectangle
        {
            RadiusX = 12,
            RadiusY = 12,
            Stroke = App.GetBrush("AppTextSecondaryBrush"),
            StrokeThickness = 1,
            StrokeDashArray = new DoubleCollection { 6, 4 },
        });
        return frame;
    }

    private static StackPanel BuildContentStack() => new()
    {
        HorizontalAlignment = HorizontalAlignment.Center,
        VerticalAlignment = VerticalAlignment.Center,
        Spacing = 14,
        Padding = new Thickness(28, 24, 28, 24),
    };


    private static void AddDimmedText(StackPanel content, string text, double fontSize, string brushKey,
        Windows.UI.Text.FontWeight? weight = null, Thickness? margin = null)
    {
        var block = new TextBlock
        {
            Text = text,
            FontSize = fontSize,
            TextAlignment = TextAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
            MaxWidth = FrameWidth - 80,
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        if (weight is { } w) block.FontWeight = w;
        if (margin is { } m) block.Margin = m;
        block.Foreground = App.GetBrush(brushKey);
        block.Opacity = Dim;
        content.Children.Add(block);
    }
}
