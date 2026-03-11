using System.Windows.Media;

namespace Cantor.Models;

public class ColorPreset
{
    public string Hex { get; }
    public string Label { get; }
    public Brush Brush { get; }

    public ColorPreset(string hex, string label)
    {
        Hex = hex;
        Label = label;
        try { Brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex)); }
        catch { Brush = Brushes.Transparent; }
    }
}