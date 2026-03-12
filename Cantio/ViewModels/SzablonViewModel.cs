using Cantio.Models;
using Cantio.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Windows;
using System.Windows.Media;
using WpfScreenHelper;

namespace Cantio.ViewModels;

public partial class SzablonViewModel : ObservableObject
{
    private readonly DatabaseService _db;
    private readonly ProjectionViewModel _projection;

    public event Action? Saved;

    public SzablonViewModel(DatabaseService db, ProjectionViewModel projection)
    {
        _db = db;
        _projection = projection;
        LoadScreens();
        _ = LoadAsync();
    }

    // ── Czcionka ──────────────────────────────────────────────────────────

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PreviewFontFamily))]
    [NotifyPropertyChangedFor(nameof(PreviewFontSize))]
    [NotifyPropertyChangedFor(nameof(PreviewLineHeight))]
    private string _fontFamily = "Segoe UI";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PreviewFontSize))]
    [NotifyPropertyChangedFor(nameof(PreviewLineHeight))]
    private double _fontSize = 60;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PreviewFontWeight))]
    private bool _fontBold = false;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PreviewTextAlignment))]
    private string _textAlign = "center";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PreviewLineHeight))]
    private double _lineHeightMultiplier = 1;

    // ── Kolor tekstu ──────────────────────────────────────────────────────

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PreviewTextBrush))]
    [NotifyPropertyChangedFor(nameof(TextColorSwatch))]
    private string _textColor = "#FFFFFF";

    // ── Cień ──────────────────────────────────────────────────────────────

    [ObservableProperty] private bool _shadowEnabled = true;
    [ObservableProperty] private double _shadowBlur = 8;
    [ObservableProperty] private double _shadowDepth = 2;
    [ObservableProperty] private double _shadowOpacity = 0.8;

    // ── Tło ───────────────────────────────────────────────────────────────

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PreviewBackgroundBrush))]
    [NotifyPropertyChangedFor(nameof(BgColorSwatch))]
    private string _backgroundColor = "#000000";

    [ObservableProperty] private string? _backgroundImagePath;
    [ObservableProperty] private double _backgroundImageOpacity = 1.0;

    // ── Pozycja ───────────────────────────────────────────────────────────

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PreviewVerticalAlignment))]
    private string _textPosition = "center";

    [ObservableProperty] private double _textMarginH = 80;
    [ObservableProperty] private double _textMarginV = 60;

    // ── Ekran ─────────────────────────────────────────────────────────────

    [ObservableProperty] private List<ScreenOption> _screens = [];
    [ObservableProperty] private ScreenOption? _selectedScreen;

    // ── Preview ───────────────────────────────────────────────────────────

    private static readonly string[] _sampleLines =
    [
        "Bóg jest miłością i kto trwa w miłości,",
        "trwa w Bogu, a Bóg trwa w nim.",
        "Miejcie odwagę żyć dla miłości,",
        "bo miłość nigdy nie ustaje."
    ];

    public string PreviewText => string.Join("\n", _sampleLines);

    public FontFamily PreviewFontFamily => new(FontFamily);
    public FontWeight PreviewFontWeight => FontBold ? FontWeights.Bold : FontWeights.Normal;
    public TextAlignment PreviewTextAlignment => TextAlign switch
    {
        "left" => TextAlignment.Left,
        "right" => TextAlignment.Right,
        _ => TextAlignment.Center
    };
    public Brush PreviewTextBrush => ToBrush(TextColor) ?? Brushes.White;
    public Brush PreviewBackgroundBrush => ToBrush(BackgroundColor) ?? Brushes.Black;
    public Brush TextColorSwatch => PreviewTextBrush;
    public Brush BgColorSwatch => PreviewBackgroundBrush;

    public VerticalAlignment PreviewVerticalAlignment => TextPosition switch
    {
        "top" => VerticalAlignment.Top,
        "bottom" => VerticalAlignment.Bottom,
        _ => VerticalAlignment.Center
    };

    public double PreviewFontSize
    {
        get
        {
            const double previewWidth = 960;
            const double slideWidth = 1920;
            double scale = previewWidth / slideWidth;
            return FontSize * scale;
        }
    }

    public double PreviewLineHeight => PreviewFontSize * LineHeightMultiplier;

    // ── Presets ───────────────────────────────────────────────────────────

    public List<ColorPreset> BgPresets { get; } =
    [
        new("#000000", "Czarny"),
        new("#0a0a1a", "Granat"),
        new("#1a0a00", "Brąz"),
        new("#001400", "Zieleń"),
        new("#1a1a2e", "Granat 2"),
        new("#0d0d0d", "Prawie czarny"),
    ];

    public List<ColorPreset> TextPresets { get; } =
    [
        new("#FFFFFF", "Biały"),
        new("#FFFDE7", "Kremowy"),
        new("#FFF9C4", "Żółtawy"),
        new("#E3F2FD", "Błękitny"),
        new("#c9a84c", "Złoty"),
    ];

    // ── Commands ──────────────────────────────────────────────────────────

    [RelayCommand] private void SetTextAlign(string a) => TextAlign = a;
    [RelayCommand] private void SetTextPosition(string p) => TextPosition = p;
    [RelayCommand] private void SetBgColor(string c) => BackgroundColor = c;
    [RelayCommand] private void SetTextColor(string c) => TextColor = c;
    [RelayCommand] private void SelectScreen(ScreenOption screen) => SelectedScreen = screen;

    [RelayCommand]
    private void BrowseBackground()
    {
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Wybierz obraz tła",
            Filter = "Obrazy (*.jpg;*.jpeg;*.png;*.bmp)|*.jpg;*.jpeg;*.png;*.bmp"
        };
        if (dlg.ShowDialog() == true)
            BackgroundImagePath = dlg.FileName;
    }

    [RelayCommand] private void ClearBackground() => BackgroundImagePath = null;

    [RelayCommand]
    private async Task SaveAsync()
    {
        await _db.SaveSettingAsync("font_family", FontFamily);
        await _db.SaveSettingAsync("font_size", FontSize.ToString());
        await _db.SaveSettingAsync("font_bold", FontBold ? "true" : "false");
        await _db.SaveSettingAsync("text_align", TextAlign);
        await _db.SaveSettingAsync("line_height", LineHeightMultiplier.ToString());
        await _db.SaveSettingAsync("text_color", TextColor);
        await _db.SaveSettingAsync("shadow_enabled", ShadowEnabled ? "true" : "false");
        await _db.SaveSettingAsync("shadow_blur", ShadowBlur.ToString());
        await _db.SaveSettingAsync("shadow_depth", ShadowDepth.ToString());
        await _db.SaveSettingAsync("shadow_opacity", ShadowOpacity.ToString());
        await _db.SaveSettingAsync("bg_color", BackgroundColor);
        await _db.SaveSettingAsync("bg_image", BackgroundImagePath ?? "");
        await _db.SaveSettingAsync("bg_image_opacity", BackgroundImageOpacity.ToString());
        await _db.SaveSettingAsync("text_position", TextPosition);
        await _db.SaveSettingAsync("text_margin_h", TextMarginH.ToString());
        await _db.SaveSettingAsync("text_margin_v", TextMarginV.ToString());
        if (SelectedScreen != null)
            await _db.SaveSettingAsync("projection_screen", SelectedScreen.Index.ToString());

        _projection.ApplySettings(_db.GetSettings());
        Saved?.Invoke();
    }

    [RelayCommand]
    private async Task ResetAsync()
    {
        FontFamily = "Segoe UI"; FontSize = 60; FontBold = false;
        TextAlign = "center"; LineHeightMultiplier = 1;
        TextColor = "#FFFFFF"; ShadowEnabled = true;
        ShadowBlur = 8; ShadowDepth = 2; ShadowOpacity = 0.8;
        BackgroundColor = "#000000"; BackgroundImagePath = null;
        BackgroundImageOpacity = 1.0; TextPosition = "center";
        TextMarginH = 80; TextMarginV = 60;
        await SaveAsync();
    }

    // ── Load ──────────────────────────────────────────────────────────────

    private async Task LoadAsync()
    {
        var s = _db.GetSettings();
        FontFamily = s.FontFamily;
        FontSize = s.FontSize;
        FontBold = s.FontBold;
        TextAlign = s.TextAlign;
        LineHeightMultiplier = s.LineHeightMultiplier;
        TextColor = s.TextColor;
        ShadowEnabled = s.ShadowEnabled;
        ShadowBlur = s.ShadowBlur;
        ShadowDepth = s.ShadowDepth;
        ShadowOpacity = s.ShadowOpacity;
        BackgroundColor = s.BackgroundColor;
        BackgroundImagePath = s.BackgroundImagePath;
        BackgroundImageOpacity = s.BackgroundImageOpacity;
        TextPosition = s.TextPosition;
        TextMarginH = s.TextMarginH;
        TextMarginV = s.TextMarginV;

        var screenIdx = await _db.GetSettingAsync("projection_screen");
        SelectedScreen = int.TryParse(screenIdx, out int idx)
            ? Screens.FirstOrDefault(s2 => s2.Index == idx) ?? Screens.FirstOrDefault()
            : Screens.Count > 1 ? Screens[1] : Screens.FirstOrDefault();
    }

    private void LoadScreens()
    {
        Screens = Screen.AllScreens
            .Select((s, i) => new ScreenOption
            {
                Index = i,
                Label = $"Ekran {i + 1}{(s.Primary ? " (główny)" : "")}  {(int)s.WpfBounds.Width}×{(int)s.WpfBounds.Height}"
            }).ToList();
    }

    private static Brush? ToBrush(string hex)
    {
        try { return new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex)); }
        catch { return null; }
    }
}