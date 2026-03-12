using Cantio.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using System.Windows;
using System.Windows.Media;
namespace Cantio.ViewModels;

public partial class ProjectionViewModel : ObservableObject
{
    private Slide? _pendingSlide;

    [ObservableProperty] private string _slideText = string.Empty;
    [ObservableProperty] private bool _isBlank = false;
    [ObservableProperty] private FontFamily _fontFamily = new("Segoe UI");
    [ObservableProperty] private double _fontSize = 60;
    [ObservableProperty] private FontWeight _fontWeight = FontWeights.Normal;
    [ObservableProperty] private TextAlignment _textAlignment = TextAlignment.Center;
    [ObservableProperty] private Brush _textBrush = Brushes.White;
    [ObservableProperty] private Brush _backgroundBrush = Brushes.Black;
    [ObservableProperty] private string? _backgroundImagePath;
    [ObservableProperty] private double _backgroundImageOpacity = 1.0;
    [ObservableProperty] private bool _shadowEnabled = true;
    [ObservableProperty] private double _shadowBlur = 8;
    [ObservableProperty] private double _shadowDepth = 2;
    [ObservableProperty] private double _shadowOpacity = 0.8;
    [ObservableProperty] private VerticalAlignment _textVerticalAlignment = VerticalAlignment.Center;
    [ObservableProperty] private Thickness _textMargin = new(80, 60, 80, 60);
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DisplayLineHeight))]
    private double _lineHeightMultiplier = 1.35;
    public double DisplayLineHeight => Math.Max(1, FontSize * LineHeightMultiplier);
    partial void OnFontSizeChanged(double value) => OnPropertyChanged(nameof(DisplayLineHeight));

    // ── API ───────────────────────────────────────────────────────────────
    public void SetSlide(Slide slide)
    {
        _pendingSlide = slide;
        if (!IsBlank)
            ApplyPendingSlide();
    }

    public void SetBlanked(bool blanked)
    {
        IsBlank = blanked;
        if (!blanked)
            ApplyPendingSlide();
    }

    private void ApplyPendingSlide()
    {
        if (_pendingSlide == null) return;
        SlideText = _pendingSlide.Text;
        FontSize = _pendingSlide.FontSize;
    }

    public void ApplySettings(DisplaySettings s)
    {
        FontFamily = new FontFamily(s.FontFamily);
        FontSize = s.FontSize;
        FontWeight = s.FontBold ? FontWeights.Bold : FontWeights.Normal;
        LineHeightMultiplier = s.LineHeightMultiplier;
        TextAlignment = s.TextAlign switch
        {
            "left" => TextAlignment.Left,
            "right" => TextAlignment.Right,
            _ => TextAlignment.Center
        };
        TextBrush = ToBrush(s.TextColor) ?? Brushes.White;
        BackgroundBrush = ToBrush(s.BackgroundColor) ?? Brushes.Black;
        BackgroundImagePath = string.IsNullOrEmpty(s.BackgroundImagePath) ? null : s.BackgroundImagePath;
        BackgroundImageOpacity = s.BackgroundImageOpacity;
        ShadowEnabled = s.ShadowEnabled;
        ShadowBlur = s.ShadowBlur;
        ShadowDepth = s.ShadowDepth;
        ShadowOpacity = s.ShadowOpacity;
        TextVerticalAlignment = s.TextPosition switch
        {
            "top" => VerticalAlignment.Top,
            "bottom" => VerticalAlignment.Bottom,
            _ => VerticalAlignment.Center
        };
        TextMargin = new Thickness(s.TextMarginH, s.TextMarginV, s.TextMarginH, s.TextMarginV);
    }

    private static Brush? ToBrush(string hex)
    {
        try { return new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex)); }
        catch { return null; }
    }
}