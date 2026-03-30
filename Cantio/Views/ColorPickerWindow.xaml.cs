using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

namespace Cantio.Views;

public partial class ColorPickerWindow : Window
{
    public Color SelectedColor { get; private set; }
    public string SelectedHex => $"#{SelectedColor.R:X2}{SelectedColor.G:X2}{SelectedColor.B:X2}";

    private double _hue = 0;   // 0–360
    private double _sat = 1;   // 0–1
    private double _val = 1;   // 0–1

    private bool _updating = false;
    private bool _svDragging = false;
    private bool _hueDragging = false;

    private const double SvW = 248;
    private const double SvH = 200;
    private const double HueW = 248;

    public ColorPickerWindow(Color initial)
    {
        InitializeComponent();
        SelectedColor = initial;
        PreviewOld.Background = new SolidColorBrush(initial);
        ColorToHsv(initial, out _hue, out _sat, out _val);
        UpdateAll();
    }

    // SV canvas

    private void SvGrid_MouseDown(object sender, MouseButtonEventArgs e)
    {
        _svDragging = true;
        ((UIElement)sender).CaptureMouse();
        UpdateSvFromPoint(e.GetPosition(SvGrid));
    }

    private void SvGrid_MouseMove(object sender, MouseEventArgs e)
    {
        if (_svDragging) UpdateSvFromPoint(e.GetPosition(SvGrid));
    }

    private void SvGrid_MouseUp(object sender, MouseButtonEventArgs e)
    {
        _svDragging = false;
        ((UIElement)sender).ReleaseMouseCapture();
    }

    private void UpdateSvFromPoint(Point p)
    {
        _sat = Math.Clamp(p.X / SvW, 0, 1);
        _val = Math.Clamp(1 - p.Y / SvH, 0, 1);
        UpdateAll();
    }

    // Hue bar

    private void HueBar_MouseDown(object sender, MouseButtonEventArgs e)
    {
        _hueDragging = true;
        ((UIElement)sender).CaptureMouse();
        UpdateHueFromPoint(e.GetPosition(HueBar));
    }

    private void HueBar_MouseMove(object sender, MouseEventArgs e)
    {
        if (_hueDragging) UpdateHueFromPoint(e.GetPosition(HueBar));
    }

    private void HueBar_MouseUp(object sender, MouseButtonEventArgs e)
    {
        _hueDragging = false;
        ((UIElement)sender).ReleaseMouseCapture();
    }

    private void UpdateHueFromPoint(Point p)
    {
        _hue = Math.Clamp(p.X / HueW * 360.0, 0, 360);
        UpdateAll();
    }

    // Hex input

    private void HexBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_updating) return;
        var text = HexBox.Text.TrimStart('#');
        if (text.Length == 6)
        {
            try
            {
                var color = (Color)ColorConverter.ConvertFromString("#" + text);
                ColorToHsv(color, out _hue, out _sat, out _val);
                UpdateAllExceptHex();
            }
            catch { }
        }
    }

    // Buttons

    private void Ok_Click(object sender, RoutedEventArgs e) => DialogResult = true;
    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;

    // Update helpers

    private void UpdateAll()
    {
        _updating = true;
        Refresh();
        HexBox.Text = SelectedHex;
        _updating = false;
    }

    private void UpdateAllExceptHex()
    {
        _updating = true;
        Refresh();
        _updating = false;
    }

    private void Refresh()
    {
        SvBase.Fill = new SolidColorBrush(HsvToColor(_hue, 1, 1));

        Canvas.SetLeft(SvThumb, _sat * SvW - 6.5);
        Canvas.SetTop(SvThumb, (1 - _val) * SvH - 6.5);

        Canvas.SetLeft(HueThumb, _hue / 360.0 * HueW - 2.5);

        SelectedColor = HsvToColor(_hue, _sat, _val);
        PreviewNew.Background = new SolidColorBrush(SelectedColor);
    }

    // HSV ↔ RGB

    private static Color HsvToColor(double h, double s, double v)
    {
        h = ((h % 360) + 360) % 360;
        int i = (int)(h / 60) % 6;
        double f = h / 60 - Math.Floor(h / 60);
        double p = v * (1 - s);
        double q = v * (1 - f * s);
        double t = v * (1 - (1 - f) * s);
        double r, g, b;
        switch (i)
        {
            case 0: r = v; g = t; b = p; break;
            case 1: r = q; g = v; b = p; break;
            case 2: r = p; g = v; b = t; break;
            case 3: r = p; g = q; b = v; break;
            case 4: r = t; g = p; b = v; break;
            default: r = v; g = p; b = q; break;
        }
        return Color.FromRgb((byte)(r * 255), (byte)(g * 255), (byte)(b * 255));
    }

    private static void ColorToHsv(Color color, out double h, out double s, out double v)
    {
        double r = color.R / 255.0, g = color.G / 255.0, b = color.B / 255.0;
        double max = Math.Max(r, Math.Max(g, b));
        double min = Math.Min(r, Math.Min(g, b));
        double delta = max - min;
        v = max;
        s = max == 0 ? 0 : delta / max;
        if (delta == 0) { h = 0; return; }
        if (max == r)      h = 60 * ((g - b) / delta % 6);
        else if (max == g) h = 60 * ((b - r) / delta + 2);
        else               h = 60 * ((r - g) / delta + 4);
        if (h < 0) h += 360;
    }
}
