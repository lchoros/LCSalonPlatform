using System.Windows.Input;
using Microsoft.Maui.Controls.Shapes;
using Path = Microsoft.Maui.Controls.Shapes.Path;

namespace Salon.Mobile.Screens.Parts;

/// <summary>
/// A drawn icon that is pressed: 44 x 44 to the finger whatever it looks like, an optional ring or disc around a
/// 24 x 24 stroke (Styles.xaml, Icon*). Says what it does to a screen reader through <see cref="Description"/>.
/// </summary>
internal sealed class IconButton : ContentView
{
    public static readonly BindableProperty IconProperty =
        BindableProperty.Create(nameof(Icon), typeof(Geometry), typeof(IconButton), propertyChanged: (b, _, v) => ((IconButton)b)._path.Data = (Geometry?)v);

    public static readonly BindableProperty CommandProperty =
        BindableProperty.Create(nameof(Command), typeof(ICommand), typeof(IconButton), propertyChanged: (b, _, v) => ((IconButton)b)._tap.Command = (ICommand?)v);

    public static readonly BindableProperty CommandParameterProperty =
        BindableProperty.Create(nameof(CommandParameter), typeof(object), typeof(IconButton), propertyChanged: (b, _, v) => ((IconButton)b)._tap.CommandParameter = v);

    public static readonly BindableProperty DescriptionProperty =
        BindableProperty.Create(nameof(Description), typeof(string), typeof(IconButton), propertyChanged: (b, _, v) => SemanticProperties.SetDescription((IconButton)b, (string?)v));

    public static readonly BindableProperty ColorProperty =
        BindableProperty.Create(nameof(Color), typeof(Color), typeof(IconButton), Colors.Black, propertyChanged: (b, _, v) => ((IconButton)b)._path.Stroke = new SolidColorBrush((Color)v));

    public static readonly BindableProperty DiscProperty =
        BindableProperty.Create(nameof(Disc), typeof(Color), typeof(IconButton), propertyChanged: (b, _, v) => ((IconButton)b)._disc.Background = v is Color color ? new SolidColorBrush(color) : null);

    public static readonly BindableProperty RingProperty =
        BindableProperty.Create(nameof(Ring), typeof(Color), typeof(IconButton), propertyChanged: (b, _, v) =>
        {
            var button = (IconButton)b;
            button._disc.Stroke = v is Color color ? new SolidColorBrush(color) : null;
            button._disc.StrokeThickness = v is null ? 0 : 1;
        });

    public static readonly BindableProperty DiscSizeProperty =
        BindableProperty.Create(nameof(DiscSize), typeof(double), typeof(IconButton), 36d, propertyChanged: (b, _, v) =>
        {
            var button = (IconButton)b;
            button._disc.WidthRequest = (double)v;
            button._disc.HeightRequest = (double)v;
        });

    private readonly Path _path;
    private readonly Border _disc;
    private readonly TapGestureRecognizer _tap = new();

    public IconButton()
    {
        _path = new Path
        {
            Stroke = new SolidColorBrush(Colors.Black),
            StrokeThickness = 2,
            StrokeLineCap = PenLineCap.Round,
            StrokeLineJoin = PenLineJoin.Round,
            Aspect = Stretch.None,
            WidthRequest = 24,
            HeightRequest = 24,
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions = LayoutOptions.Center,
            InputTransparent = true,
        };
        _disc = new Border
        {
            StrokeShape = new Ellipse(),
            StrokeThickness = 0,
            WidthRequest = 36,
            HeightRequest = 36,
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions = LayoutOptions.Center,
            Content = _path,
            InputTransparent = true,
        };

        WidthRequest = 44;
        HeightRequest = 44;
        Content = _disc;
        GestureRecognizers.Add(_tap);
    }

    public Geometry? Icon
    {
        get => (Geometry?)GetValue(IconProperty);
        set => SetValue(IconProperty, value);
    }

    public ICommand? Command
    {
        get => (ICommand?)GetValue(CommandProperty);
        set => SetValue(CommandProperty, value);
    }

    public object? CommandParameter
    {
        get => GetValue(CommandParameterProperty);
        set => SetValue(CommandParameterProperty, value);
    }

    public string? Description
    {
        get => (string?)GetValue(DescriptionProperty);
        set => SetValue(DescriptionProperty, value);
    }

    public Color Color
    {
        get => (Color)GetValue(ColorProperty);
        set => SetValue(ColorProperty, value);
    }

    /// <summary>A filled circle behind the icon; none by default.</summary>
    public Color? Disc
    {
        get => (Color?)GetValue(DiscProperty);
        set => SetValue(DiscProperty, value);
    }

    /// <summary>A thin circle around the icon; none by default.</summary>
    public Color? Ring
    {
        get => (Color?)GetValue(RingProperty);
        set => SetValue(RingProperty, value);
    }

    public double DiscSize
    {
        get => (double)GetValue(DiscSizeProperty);
        set => SetValue(DiscSizeProperty, value);
    }

    protected override void OnPropertyChanged(string? propertyName = null)
    {
        base.OnPropertyChanged(propertyName);

        // Faint where there is nowhere to go (the earlier/later arrows), as on the web.
        if (propertyName == IsEnabledProperty.PropertyName)
        {
            Opacity = IsEnabled ? 1 : 0.35;
        }
    }
}