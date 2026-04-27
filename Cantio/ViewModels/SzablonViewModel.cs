using Cantio.Helpers;
using Cantio.Models;
using Cantio.Services;
using Cantio.Views;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Windows;
using System.Windows.Media;
using WpfScreenHelper;
using WinReg = Microsoft.Win32.Registry;

namespace Cantio.ViewModels;

public partial class SzablonViewModel : ObservableObject
{
    private readonly DatabaseService _db;
    private readonly ProjectionViewModel _projection;
    private readonly DisplayViewModel _display;

    public event Action? Saved;

    public ProjectionViewModel PreviewProjection { get; } = new();

    public SzablonViewModel(DatabaseService db, ProjectionViewModel projection, DisplayViewModel display)
    {
        _db = db;
        _projection = projection;
        _display = display;
        LoadScreens();
        _ = LoadAsync().ContinueWith(
            t => System.Diagnostics.Debug.WriteLine($"[SzablonViewModel] LoadAsync: {t.Exception}"),
            TaskContinuationOptions.OnlyOnFaulted);
        PropertyChanged += (_, _) => SyncPreview();
    }

    private void SyncPreview()
    {
        var settings = new Services.DisplaySettings
        {
            FontFamily            = FontFamily,
            FontSize              = FontSize,
            FontBold              = FontBold,
            LineHeightMultiplier  = LineHeightMultiplier,
            TextAlign             = TextAlign,
            TextColor             = TextColor,
            ShadowEnabled         = ShadowEnabled,
            ShadowBlur            = ShadowBlur,
            ShadowDepth           = ShadowDepth,
            ShadowOpacity         = ShadowOpacity,
            BackgroundColor       = BackgroundColor,
            BackgroundImagePath   = BackgroundImagePath,
            BackgroundImageOpacity = BackgroundImageOpacity,
            GradientEnabled       = GradientEnabled,
            GradientType          = GradientType,
            GradientColor1        = GradientColor1,
            GradientColor2        = GradientColor2,
            GradientAngle         = GradientAngle,
            TextPosition          = TextPosition,
            TextMarginH           = TextMarginH,
            TextMarginV           = TextMarginV,
            TextTags              = TextTags.ToList(),
            FontAutoFit           = FontAutoFit,
        };
        PreviewProjection.ApplySettings(settings);

        var w = _display.ProjectionScreenWidth  > 0 ? _display.ProjectionScreenWidth  : 1920;
        var h = _display.ProjectionScreenHeight > 0 ? _display.ProjectionScreenHeight : 1080;
        var layout = new Services.SlideLayoutSettings
        {
            FontFamily           = FontFamily,
            FontBold             = FontBold,
            FontSize             = FontSize,
            LineHeightMultiplier = LineHeightMultiplier,
            SlideWidth           = w,
            SlideHeight          = h,
            MarginH              = TextMarginH,
            MarginV              = TextMarginV,
            AutoFit              = FontAutoFit,
        };
        var parts = Services.SlideLayoutService.SplitVerse(PreviewText, layout);
        var firstSlide = parts.Count > 0 ? parts[0] : PreviewText;
        PreviewProjection.FontSize = FontAutoFit
            ? Services.SlideLayoutService.ComputeFitFontSize(firstSlide, layout)
            : FontSize;
        PreviewProjection.SlideText = firstSlide;
    }

    // Lista czcionek systemowych

    public static IReadOnlyList<string> SystemFonts { get; } =
        System.Windows.Media.Fonts.SystemFontFamilies
            .Select(f => f.Source)
            .OrderBy(n => n)
            .ToList();

    // Czcionka

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

    // Kolor tekstu

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PreviewTextBrush))]
    [NotifyPropertyChangedFor(nameof(TextColorSwatch))]
    private string _textColor = "#FFFFFF";

    // Cień

    [ObservableProperty] private bool _shadowEnabled = true;
    [ObservableProperty] private double _shadowBlur = 8;
    [ObservableProperty] private double _shadowDepth = 2;
    [ObservableProperty] private double _shadowOpacity = 0.8;

    // Tło

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PreviewBackgroundBrush))]
    [NotifyPropertyChangedFor(nameof(BgColorSwatch))]
    private string _backgroundColor = "#000000";

    [ObservableProperty] private string? _backgroundImagePath;
    [ObservableProperty] private double _backgroundImageOpacity = 1.0;

    // Gradient

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

    // Pozycja

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PreviewVerticalAlignment))]
    private string _textPosition = "center";

    [ObservableProperty] private double _textMarginH = 80;
    [ObservableProperty] private double _textMarginV = 60;

    // Ekran

    [ObservableProperty] private List<ScreenOption> _screens = [];
    [ObservableProperty] private ScreenOption? _selectedScreen;

    // Język

    [ObservableProperty] private string _selectedLanguage = "pl";

    partial void OnSelectedLanguageChanged(string value)
    {
        LocalizationManager.SetLanguage(value);
        OnPropertyChanged(nameof(FontSizeLabel));
    }

    [RelayCommand] private void SetLanguage(string lang) => SelectedLanguage = lang;

    // Tagi formatowania

    [ObservableProperty] private ObservableCollection<TextFormatTag> _textTags = [];

    // Preview

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

    // Presets

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

    // Commands

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
        await _db.SaveSettingAsync("psalm_category_id", (SelectedPsalmCategory?.Id ?? 0).ToString());
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

    // Ogólne

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FontSizeLabel))]
    private bool _fontAutoFit = true;

    // Autostart

    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

    [ObservableProperty] private bool _runOnStartup;

    partial void OnRunOnStartupChanged(bool value)
    {
        using var key = WinReg.CurrentUser.OpenSubKey(RunKey, writable: true);
        if (key == null) return;
        if (value) key.SetValue("Cantio", $"\"{Environment.ProcessPath}\"");
        else key.DeleteValue("Cantio", throwOnMissingValue: false);
    }

    // Baza danych

    private static string DbPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Cantio", "cantio.db");

    private static string AppDataFolder => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Cantio");

    [RelayCommand]
    private void BackupDatabase()
    {
        var dlg = new SaveFileDialog
        {
            Title = LocalizationManager.Get("Settings.BackupDb"),
            Filter = "SQLite (*.db)|*.db",
            FileName = $"cantio_backup_{DateTime.Now:yyyyMMdd_HHmm}.db"
        };
        if (dlg.ShowDialog() != true) return;
        File.Copy(DbPath, dlg.FileName, overwrite: true);
    }

    [RelayCommand]
    private void RestoreDatabase()
    {
        var dlg = new OpenFileDialog
        {
            Title = LocalizationManager.Get("Settings.RestoreDb"),
            Filter = "SQLite (*.db)|*.db"
        };
        if (dlg.ShowDialog() != true) return;
        if (MessageBox.Show(LocalizationManager.Get("Msg.RestoreDbConfirm"), "Cantio",
            MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        File.Copy(dlg.FileName, DbPath, overwrite: true);
        RestartApp();
    }

    [RelayCommand]
    private async Task ClearDatabaseAsync()
    {
        if (MessageBox.Show(LocalizationManager.Get("Msg.ClearDbConfirm"), "Cantio",
            MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        await _db.ClearAllDataAsync();
        RestartApp();
    }

    [RelayCommand]
    private void ExportZip()
    {
        var dlg = new SaveFileDialog
        {
            Title = LocalizationManager.Get("Settings.ExportZip"),
            Filter = "ZIP (*.zip)|*.zip",
            FileName = $"cantio_export_{DateTime.Now:yyyyMMdd_HHmm}.zip"
        };
        if (dlg.ShowDialog() != true) return;
        if (File.Exists(dlg.FileName)) File.Delete(dlg.FileName);
        using var zip = ZipFile.Open(dlg.FileName, ZipArchiveMode.Create);
        zip.CreateEntryFromFile(DbPath, "cantio.db");
        var imagesDir = Path.Combine(AppDataFolder, "images");
        if (Directory.Exists(imagesDir))
            foreach (var f in Directory.EnumerateFiles(imagesDir))
                zip.CreateEntryFromFile(f, "images/" + Path.GetFileName(f));
    }

    [RelayCommand]
    private void ImportZip()
    {
        var dlg = new OpenFileDialog
        {
            Title = LocalizationManager.Get("Settings.ImportZip"),
            Filter = "ZIP (*.zip)|*.zip"
        };
        if (dlg.ShowDialog() != true) return;
        if (MessageBox.Show(LocalizationManager.Get("Msg.ImportZipConfirm"), "Cantio",
            MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        using (var zip = ZipFile.OpenRead(dlg.FileName))
            foreach (var entry in zip.Entries)
            {
                var dest = Path.Combine(AppDataFolder, entry.FullName.Replace('/', Path.DirectorySeparatorChar));
                Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
                entry.ExtractToFile(dest, overwrite: true);
            }
        RestartApp();
    }

    private static void RestartApp()
    {
        Process.Start(new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = true });
        Application.Current.Shutdown();
    }

    [ObservableProperty] private bool _isImportingPsalmy = false;

    [RelayCommand(CanExecute = nameof(CanImportPsalmy))]
    private async Task ImportPsalmyAsync()
    {
        IsImportingPsalmy = true;
        try
        {
            int count = await _db.ImportPsalmySeedAsync();
            if (count == -1)
                MessageBox.Show(
                    LocalizationManager.Get("Msg.PsalmyCategoryMissing"),
                    "Cantio", MessageBoxButton.OK, MessageBoxImage.Warning);
            else
                MessageBox.Show(
                    string.Format(LocalizationManager.Get("Msg.PsalmyImported"), count),
                    "Cantio", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        finally { IsImportingPsalmy = false; }
    }

    private bool CanImportPsalmy() => !IsImportingPsalmy;

    partial void OnIsImportingPsalmyChanged(bool value) => ImportPsalmyCommand.NotifyCanExecuteChanged();

    public string FontSizeLabel => FontAutoFit
        ? (Application.Current.TryFindResource("Settings.FontSizeMin") as string ?? "Rozmiar minimalny")
        : (Application.Current.TryFindResource("Settings.FontSize") as string ?? "Rozmiar czcionki");

    [ObservableProperty]
    private bool _loadLastSetlistOnStartup;

    [ObservableProperty] private ObservableCollection<Category> _psalmCategories = [];
    [ObservableProperty] private Category? _selectedPsalmCategory;

    // Load

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
        RunOnStartup = WinReg.CurrentUser.OpenSubKey(RunKey)?.GetValue("Cantio") != null;

        var loadLast = await _db.GetSettingAsync("load_last_setlist");
        LoadLastSetlistOnStartup = loadLast == "1";
        FontAutoFit = s.FontAutoFit;

        var allCategories = await _db.GetCategoriesAsync();
        var noneCategory = new Category { Id = 0, Name = "(brak)" };
        PsalmCategories = new ObservableCollection<Category>(
            new[] { noneCategory }.Concat(allCategories.OrderBy(c => c.Number))
        );
        SelectedPsalmCategory = PsalmCategories.FirstOrDefault(c => c.Id == s.PsalmCategoryId) ?? noneCategory;
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