using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using ZapperRadio.Core.Playback;

namespace ZapperRadio.Controls;

/// <summary>
/// The hour of one station as a row of sixty bars, one per minute, as tall as how often the station breaks at that
/// minute. The usual break minutes stand out in <see cref="BreakBrush"/>, the others in <see cref="OtherBrush"/>, and a
/// minute that is not known yet is only a line at the bottom. The quarters of the hour are marked underneath, in the
/// control's foreground. The brushes are set from XAML as theme resources, so they follow a switch to dark or light.
/// </summary>
public sealed partial class BreakClockView : UserControl
{
    private const double BarsHeight = 24;

    public static readonly DependencyProperty SharesProperty = Register(nameof(Shares), typeof(double[]));

    public static readonly DependencyProperty BreakBrushProperty = Register(nameof(BreakBrush), typeof(Brush));

    public static readonly DependencyProperty OtherBrushProperty = Register(nameof(OtherBrush), typeof(Brush));

    public static readonly DependencyProperty UnknownBrushProperty = Register(nameof(UnknownBrush), typeof(Brush));

    private readonly Rectangle[] _bars = new Rectangle[BreakClock.MinutesPerHour];

    public BreakClockView()
    {
        var grid = new Grid();
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(BarsHeight) });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        for (var minute = 0; minute < BreakClock.MinutesPerHour; minute++)
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var bar = _bars[minute] = new Rectangle
            {
                VerticalAlignment = VerticalAlignment.Bottom,
                Margin = new Thickness(0.5, 0, 0.5, 0),
                RadiusX = 1,
                RadiusY = 1,
            };
            Grid.SetColumn(bar, minute);
            grid.Children.Add(bar);
        }

        for (var quarter = 0; quarter < 4; quarter++)
        {
            var label = new TextBlock
            {
                Text = $":{quarter * 15:00}",
                Margin = new Thickness(0, 2, 0, 0),
                Style = (Style)Application.Current.Resources["CaptionTextBlockStyle"],
            };
            Grid.SetRow(label, 1);
            Grid.SetColumn(label, quarter * 15);
            Grid.SetColumnSpan(label, 15);
            grid.Children.Add(label);
        }

        Content = grid;
        Update();
    }

    /// <summary>How often the station breaks at each minute of the local hour; NaN where it is not known yet.</summary>
    public double[]? Shares
    {
        get => (double[]?)GetValue(SharesProperty);
        set => SetValue(SharesProperty, value);
    }

    public Brush? BreakBrush
    {
        get => (Brush?)GetValue(BreakBrushProperty);
        set => SetValue(BreakBrushProperty, value);
    }

    public Brush? OtherBrush
    {
        get => (Brush?)GetValue(OtherBrushProperty);
        set => SetValue(OtherBrushProperty, value);
    }

    public Brush? UnknownBrush
    {
        get => (Brush?)GetValue(UnknownBrushProperty);
        set => SetValue(UnknownBrushProperty, value);
    }

    private static DependencyProperty Register(string name, Type type) =>
        DependencyProperty.Register(name, type, typeof(BreakClockView), new PropertyMetadata(null, (d, _) => ((BreakClockView)d).Update()));

    private void Update()
    {
        for (var minute = 0; minute < BreakClock.MinutesPerHour; minute++)
        {
            var share = Shares is { Length: BreakClock.MinutesPerHour } shares ? shares[minute] : double.NaN;
            var bar = _bars[minute];
            if (!double.IsNaN(share))
            {
                // Never quite flat, so a minute without breaks still reads as heard rather than as unknown.
                bar.Height = Math.Max(2, share * BarsHeight);
                bar.Fill = share >= BreakClock.BreakShare ? BreakBrush : OtherBrush;
            }
            else
            {
                bar.Height = 1;
                bar.Fill = UnknownBrush;
            }
        }
    }
}
