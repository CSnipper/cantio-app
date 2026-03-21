using Cantio.Models;

namespace Cantio.Services;

public class DisplaySettings
{
    public string FontFamily { get; set; } = "Segoe UI";
    public double FontSize { get; set; } = 60;
    public bool FontBold { get; set; } = false;
    public double LineHeightMultiplier { get; set; } = 1.35;
    public string TextAlign { get; set; } = "center";
    public string TextColor { get; set; } = "#FFFFFF";
    public bool ShadowEnabled { get; set; } = true;
    public double ShadowBlur { get; set; } = 8;
    public double ShadowDepth { get; set; } = 2;
    public double ShadowOpacity { get; set; } = 0.8;
    public string BackgroundColor { get; set; } = "#000000";
    public string? BackgroundImagePath { get; set; }
    public double BackgroundImageOpacity { get; set; } = 1.0;
    public string TextPosition { get; set; } = "center";
    public double TextMarginH { get; set; } = 80;
    public double TextMarginV { get; set; } = 60;
    public bool GradientEnabled { get; set; } = false;
    public string GradientType { get; set; } = "linear";
    public string GradientColor1 { get; set; } = "#000000";
    public string GradientColor2 { get; set; } = "#1a1a2e";
    public double GradientAngle { get; set; } = 180;
    public List<TextFormatTag> TextTags { get; set; } = [];
    public bool FontAutoFit { get; set; } = true;
}