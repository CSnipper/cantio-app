using Cantio.Helpers;
using Cantio.Models;
using Cantio.Services;
using Cantio.Views;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;
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

    // ── Lista czcionek systemowych ─────────────────────────────────────────

    public static IReadOnlyList<string> SystemFonts { get; } =
        System.Windows.Media.Fonts.SystemFontFamilies
            .Select(f => f.Source)
            .OrderBy(n => n)
            .ToList();

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

    // ── Gradient ──────────────────────────────────────────────────────────

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PreviewBackgroundBrush))]
    private bool _gradientEnabled = false;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PreviewBackgroundBrush))]
    [NotifyPropertyChangedFor(nameof(IsLinearGradient))]
    private string _gradientType = "linear";

    public bool IsLinearGradient => GradientType == "linear";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PreviewBackgroundBrush))]
    [NotifyPropertyChangedFor(nameof(GradientColor1Swatch))]
    private string _gradientColor1 = "#000000";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PreviewBackgroundBrush))]
    [NotifyPropertyChangedFor(nameof(GradientColor2Swatch))]
    private string _gradientColor2 = "#1a1a2e";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PreviewBackgroundBrush))]
    private double _gradientAngle = 180;

    // ── Pozycja ───────────────────────────────────────────────────────────

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PreviewVerticalAlignment))]
    private string _textPosition = "center";

    [ObservableProperty] private double _textMarginH = 80;
    [ObservableProperty] private double _textMarginV = 60;

    // ── Ekran ─────────────────────────────────────────────────────────────

    [ObservableProperty] private List<ScreenOption> _screens = [];
    [ObservableProperty] private ScreenOption? _selectedScreen;

    // ── Język ─────────────────────────────────────────────────────────────

    [ObservableProperty] private string _selectedLanguage = "pl";

    partial void OnSelectedLanguageChanged(string value)
    {
        LocalizationManager.SetLanguage(value);
        OnPropertyChanged(nameof(FontSizeLabel));
    }

    [RelayCommand] private void SetLanguage(string lang) => SelectedLanguage = lang;

    // ── Tagi formatowania ─────────────────────────────────────────────────

    [ObservableProperty] private ObservableCollection<TextFormatTag> _textTags = [];

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
    public Brush PreviewBackgroundBrush => GradientEnabled
        ? BuildGradient(GradientType, GradientColor1, GradientColor2, GradientAngle)
        : ToBrush(BackgroundColor) ?? Brushes.Black;
    public Brush TextColorSwatch => PreviewTextBrush;
    public Brush BgColorSwatch => ToBrush(BackgroundColor) ?? Brushes.Black;
    public Brush GradientColor1Swatch => ToBrush(GradientColor1) ?? Brushes.Black;
    public Brush GradientColor2Swatch => ToBrush(GradientColor2) ?? Brushes.DarkBlue;

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

    [RelayCommand] private void SetGradientType(string t) => GradientType = t;

    [RelayCommand] private void PickTextColor() => PickColor(c => TextColor = c, TextColor);
    [RelayCommand] private void PickBgColor() => PickColor(c => BackgroundColor = c, BackgroundColor);
    [RelayCommand] private void PickGradientColor1() => PickColor(c => GradientColor1 = c, GradientColor1);
    [RelayCommand] private void PickGradientColor2() => PickColor(c => GradientColor2 = c, GradientColor2);

    private void PickColor(Action<string> setter, string current)
    {
        Color initial;
        try { initial = (Color)ColorConverter.ConvertFromString(current); }
        catch { initial = Colors.White; }
        var dlg = new ColorPickerWindow(initial) { Owner = Application.Current.MainWindow };
        if (dlg.ShowDialog() == true) setter(dlg.SelectedHex);
    }

    [RelayCommand] private void AddTag() => TextTags.Add(new TextFormatTag { Name = "tag" });

    [RelayCommand] private void RemoveTag(TextFormatTag tag) => TextTags.Remove(tag);

    [RelayCommand]
    private void PickTagColor(TextFormatTag tag)
    {
        Color initial;
        try { initial = (Color)ColorConverter.ConvertFromString(tag.Color); }
        catch { initial = Colors.White; }
        var dlg = new ColorPickerWindow(initial) { Owner = Application.Current.MainWindow };
        if (dlg.ShowDialog() == true) tag.Color = dlg.SelectedHex;
    }

    private void RebuildCustomTags() => TextBlockHelper.SetCustomTagsFromDefinitions(TextTags);

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
        await _db.SaveSettingAsync("bg_gradient_enabled", GradientEnabled ? "true" : "false");
        await _db.SaveSettingAsync("bg_gradient_type", GradientType);
        await _db.SaveSettingAsync("bg_gradient_color1", GradientColor1);
        await _db.SaveSettingAsync("bg_gradient_color2", GradientColor2);
        await _db.SaveSettingAsync("bg_gradient_angle", GradientAngle.ToString());

        await _db.SaveSettingAsync("language", SelectedLanguage);
        await _db.SaveSettingAsync("load_last_setlist", LoadLastSetlistOnStartup ? "1" : "0");
        await _db.SaveSettingAsync("font_auto_fit", FontAutoFit ? "true" : "false");
        await _db.SaveTextTagsAsync(TextTags.ToList());
        RebuildCustomTags();
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
        BackgroundImageOpacity = 1.0;
        GradientEnabled = false; GradientType = "linear"; GradientColor1 = "#000000"; GradientColor2 = "#1a1a2e"; GradientAngle = 180;
        TextPosition = "center";
        TextMarginH = 80; TextMarginV = 60;
        FontAutoFit = true;
        await SaveAsync();
    }

    // ── Ogólne ────────────────────────────────────────────────────────────

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FontSizeLabel))]
    private bool _fontAutoFit = true;

    public string FontSizeLabel => FontAutoFit
        ? (Application.Current.TryFindResource("Settings.FontSizeMin") as string ?? "Rozmiar minimalny")
        : (Application.Current.TryFindResource("Settings.FontSize") as string ?? "Rozmiar czcionki");

    [ObservableProperty]
    private bool _loadLastSetlistOnStartup;

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
        GradientEnabled = s.GradientEnabled;
        GradientType = s.GradientType;
        GradientColor1 = s.GradientColor1;
        GradientColor2 = s.GradientColor2;
        GradientAngle = s.GradientAngle;
        TextPosition = s.TextPosition;
        TextMarginH = s.TextMarginH;
        TextMarginV = s.TextMarginV;
        TextTags = new ObservableCollection<TextFormatTag>(s.TextTags);
        RebuildCustomTags();

        var screenIdx = await _db.GetSettingAsync("projection_screen");
        SelectedScreen = int.TryParse(screenIdx, out int idx)
            ? Screens.FirstOrDefault(s2 => s2.Index == idx) ?? Screens.FirstOrDefault()
            : Screens.Count > 1 ? Screens[1] : Screens.FirstOrDefault();

        SelectedLanguage = await _db.GetSettingAsync("language") ?? "pl";

        var loadLast = await _db.GetSettingAsync("load_last_setlist");
        LoadLastSetlistOnStartup = loadLast == "1";
        FontAutoFit = s.FontAutoFit;
    }

    private void LoadScreens()
    {
        var screenWord  = Application.Current.TryFindResource("Settings.Screen")  as string ?? "Screen";
        var primaryWord = Application.Current.TryFindResource("Settings.ScreenPrimary") as string ?? "(primary)";
        Screens = Screen.AllScreens
            .Select((s, i) => new ScreenOption
            {
                Index = i,
                Label = $"{screenWord} {i + 1}{(s.Primary ? $" {primaryWord}" : "")}  {(int)s.WpfBounds.Width}×{(int)s.WpfBounds.Height}"
            }).ToList();
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