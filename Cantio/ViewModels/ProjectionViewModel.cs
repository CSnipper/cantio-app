using Cantio.Helpers;
using Cantio.Models;
using Cantio.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Media;
namespace Cantio.ViewModels;

public partial class ProjectionViewModel : ObservableObject
{
    private Slide? _pendingSlide;
    private string? _pendingImagePath;

    // Slajd obrazkowy (pełnoekranowy obraz w zestawie)
    [ObservableProperty] private bool _isImageSlide = false;
    [ObservableProperty] private string? _imageSlidePath;

    [ObservableProperty] private ObservableCollection<TextFormatTag> _textTags = [];
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

    // Operator override (psalm mode: verse shown in preview, refrain on projector)
    [ObservableProperty] private string _operatorSlideText = string.Empty;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(OperatorDisplayLineHeight))]
    private double _operatorFontSize = 40;
    [ObservableProperty] private bool _isOperatorOverride = false;
    public double OperatorDisplayLineHeight => Math.Max(1, OperatorFontSize * LineHeightMultiplier);

    public void SetOperatorSlide(Slide slide)
    {
        OperatorSlideText = slide.Text;
        OperatorFontSize = slide.FontSize;
        IsOperatorOverride = true;
    }

    public void ClearOperatorSlide()
    {
        IsOperatorOverride = false;
    }

    // API
    public void SetSlide(Slide slide)
    {
        ClearImageSlide();
        _pendingSlide = slide;
        ApplyPendingSlide();
    }

    public void SetImageSlide(string path)
    {
        _pendingSlide = null;
        _pendingImagePath = ImageStorage.Resolve(path);
        ApplyPendingImage();
    }

    public void ClearImageSlide()
    {
        _pendingImagePath = null;
        IsImageSlide = false;
        ImageSlidePath = null;
    }

    private void ApplyPendingImage()
    {
        if (_pendingImagePath == null) return;
        IsImageSlide = true;
        ImageSlidePath = _pendingImagePath;
        SlideText = string.Empty;
    }

    public void SetBlanked(bool blanked)
    {
        IsBlank = blanked;
        if (!blanked)
        {
            if (_pendingImagePath != null) ApplyPendingImage();
            else ApplyPendingSlide();
        }
    }

    private void ApplyPendingSlide()
    {
        if (_pendingSlide == null) return;
        FontSize = _pendingSlide.FontSize;
        SlideText = _pendingSlide.Text;
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
        BackgroundBrush = s.GradientEnabled
            ? BuildGradient(s.GradientType, s.GradientColor1, s.GradientColor2, s.GradientAngle)
            : ToBrush(s.BackgroundColor) ?? Brushes.Black;
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
        TextTags = new ObservableCollection<TextFormatTag>(s.TextTags);
    }

    private static Brush? ToBrush(string hex)
    {
        try { return new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex)); }
        catch { return null; }
    }

    private static Brush BuildGradient(string type, string hex1, string hex2, double angleDeg)
    {
        Color c1, c2;
        try { c1 = (Color)ColorConverter.ConvertFromString(hex1); } catch { c1 = Colors.Black; }
        try { c2 = (Color)ColorConverter.ConvertFromString(hex2); } catch { c2 = Colors.Black; }

        if (type == "radial")
            return new RadialGradientBrush(c1, c2);

        var rad = angleDeg * Math.PI / 180.0;
        return new LinearGradientBrush(c1, c2,
            new Point(0.5 - Math.Cos(rad) / 2, 0.5 - Math.Sin(rad) / 2),
            new Point(0.5 + Math.Cos(rad) / 2, 0.5 + Math.Sin(rad) / 2));
    }
}