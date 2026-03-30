using Cantio.Models;
using Cantio.Services;
using Cantio.Services.Import;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using Microsoft.WindowsAPICodePack.Dialogs;
using System.Collections.ObjectModel;
using System.IO;
using System.Windows;

namespace Cantio.ViewModels;

public partial class ImportViewModel : ObservableObject
{
    private readonly DatabaseService _db;

    public ImportViewModel(DatabaseService db)
    {
        _db = db;
        _ = LoadOszGroupsAsync();
        _ = LoadCategoriesAsync();
    }

    // Format

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsOpenLPSqlite))]
    [NotifyPropertyChangedFor(nameof(IsOpenLPXml))]
    [NotifyPropertyChangedFor(nameof(IsOpenSongFolder))]
    [NotifyPropertyChangedFor(nameof(IsOpenSongXml))]
    [NotifyCanExecuteChangedFor(nameof(BrowseCommand))]
    private ImportFormat _selectedFormat = ImportFormat.OpenLPSqlite;

    public bool IsOpenLPSqlite => SelectedFormat == ImportFormat.OpenLPSqlite;
    public bool IsOpenLPXml => SelectedFormat == ImportFormat.OpenLPXml;
    public bool IsOpenSongFolder => SelectedFormat == ImportFormat.OpenSongFolder;
    public bool IsOpenSongXml => SelectedFormat == ImportFormat.OpenSongXml;

    [RelayCommand]
    private void SelectFormat(string format)
    {
        SelectedFormat = format switch
        {
            "OpenLPSqlite" => ImportFormat.OpenLPSqlite,
            "OpenLPXml" => ImportFormat.OpenLPXml,
            "OpenSongFolder" => ImportFormat.OpenSongFolder,
            "OpenSongXml" => ImportFormat.OpenSongXml,
            _ => ImportFormat.OpenLPSqlite
        };
        FilePath = string.Empty;
        PreviewReady = false;
        PreviewText = string.Empty;
        LogEntries.Clear();
    }

    // Ścieżka

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(PreviewCommand))]
    [NotifyCanExecuteChangedFor(nameof(ImportCommand))]
    private string _filePath = string.Empty;

    public string FilePathLabel => string.IsNullOrEmpty(FilePath)
        ? SelectedFormat switch
        {
            ImportFormat.OpenLPSqlite => "Nie wybrano pliku...",
            ImportFormat.OpenLPXml => "Nie wybrano pliku/folderu...",
            ImportFormat.OpenSongFolder => "Nie wybrano folderu...",
            ImportFormat.OpenSongXml => "Nie wybrano pliku/folderu...",
            _ => "Nie wybrano..."
        }
        : Directory.Exists(FilePath)
            ? Path.GetFileName(FilePath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
            : Path.GetFileName(FilePath);

    // Opcje

    [ObservableProperty] private bool _overwriteExisting = false;
    [ObservableProperty] private bool _importCategories = true;

    // Podgląd

    [ObservableProperty] private string _previewText = string.Empty;
    [ObservableProperty] private bool _previewReady = false;

    // Postęp

    [ObservableProperty] private bool _isImporting = false;
    [ObservableProperty] private int _progressValue = 0;
    [ObservableProperty] private int _progressMax = 100;
    [ObservableProperty] private string _progressText = string.Empty;

    // Log

    [ObservableProperty]
    private ObservableCollection<LogEntry> _logEntries = [];

    // Commands

    [RelayCommand]
    private void Browse()
    {
        switch (SelectedFormat)
        {
            case ImportFormat.OpenLPSqlite:
                {
                    var dlg = new Microsoft.Win32.OpenFileDialog
                    {
                        Title = "Wybierz bazę OpenLP",
                        Filter = "Baza OpenLP (*.sqlite)|*.sqlite|Wszystkie pliki (*.*)|*.*"
                    };
                    if (dlg.ShowDialog() == true)
                        FilePath = dlg.FileName;
                    break;
                }

            case ImportFormat.OpenLPXml:
                BrowseXmlOrFolder("OpenLyrics XML (*.xml)|*.xml|Wszystkie pliki (*.*)|*.*");
                break;

            case ImportFormat.OpenSongFolder:
                {
                    using var folderDlg = new CommonOpenFileDialog
                    {
                        Title = "Wybierz folder z plikami OpenSong",
                        IsFolderPicker = true
                    };
                    if (folderDlg.ShowDialog() == CommonFileDialogResult.Ok)
                        FilePath = folderDlg.FileName;
                    break;
                }

            case ImportFormat.OpenSongXml:
                BrowseXmlOrFolder("Pliki OpenSong (bez rozszerzenia)|*|Pliki XML (*.xml)|*.xml|Wszystkie pliki (*.*)|*.*");
                break;
        }

        OnPropertyChanged(nameof(FilePathLabel));
        PreviewReady = false;
        PreviewText = string.Empty;
        LogEntries.Clear();
    }

    [RelayCommand(CanExecute = nameof(CanPreview))]
    private async Task PreviewAsync()
    {
        try
        {
            LogEntries.Clear();
            var importer = ImporterFactory.Create(SelectedFormat, FilePath);
            var preview = await importer.GetPreviewAsync();
            PreviewText = preview.Summary;
            PreviewReady = true;
            AddLog("info", PreviewText);
        }
        catch (Exception ex)
        {
            PreviewText = $"Błąd odczytu: {ex.Message}";
            PreviewReady = false;
            AddLog("error", PreviewText);
        }
    }

    private bool CanAccessPath() =>
        !string.IsNullOrEmpty(FilePath) &&
        (File.Exists(FilePath) || Directory.Exists(FilePath));

    private bool CanPreview() => CanAccessPath();

    private bool CanImport() => CanAccessPath() && !IsImporting;

    [RelayCommand(CanExecute = nameof(CanImport))]
    private async Task ImportAsync()
    {
        IsImporting = true;
        ProgressValue = 0;
        LogEntries.Clear();

        try
        {
            var importer = ImporterFactory.Create(SelectedFormat, FilePath);
            var options = new ImportOptions
            {
                OverwriteExisting = OverwriteExisting,
                ImportCategories = ImportCategories,
                FallbackCategoryId = FallbackCategory?.Id
            };

            var progress = new Progress<ImportProgress>(p =>
            {
                ProgressMax = p.Total;
                ProgressValue = p.Current;
                ProgressText = p.Message;
                AddLog(p.IsError ? "error" : "info", p.Message);
            });

            var result = await importer.ImportAsync(_db, options, progress);

            AddLog("success",
                $"Import zakończony — zaimportowano: {result.Imported}, " +
                $"pominięto: {result.Skipped}, błędy: {result.Errors}");
        }
        catch (Exception ex)
        {
            AddLog("error", $"Krytyczny błąd: {ex.Message}");
        }
        finally
        {
            IsImporting = false;
            ProgressText = string.Empty;
        }
    }

    // Kategoria domyślna (fallback gdy plik nie zawiera info o kategorii)

    [ObservableProperty] private ObservableCollection<CategoryOption> _categoriesForImport = [];
    [ObservableProperty] private CategoryOption? _fallbackCategory;

    private async Task LoadCategoriesAsync()
    {
        var cats = await _db.GetCategoriesAsync();
        var autoLabel = Application.Current.TryFindResource("Import.FallbackCategoryAuto") as string ?? "(z pliku / domyślna)";
        var options = new ObservableCollection<CategoryOption>
        {
            new CategoryOption(null, autoLabel)
        };
        foreach (var c in cats.OrderBy(c => c.Name))
            options.Add(new CategoryOption(c.Id, c.Name));
        CategoriesForImport = options;
        FallbackCategory = options[0];
    }

    // OSZ Import

    [ObservableProperty] private ObservableCollection<string> _oszGroups = [];
    [ObservableProperty] private string _oszSelectedGroup = string.Empty;
    [ObservableProperty] private string? _oszStatus;

    private static string NoGroupLabel =>
        Application.Current.TryFindResource("Import.NoGroup") as string ?? "(no group)";

    private async Task LoadOszGroupsAsync()
    {
        var setting = await _db.GetSettingAsync("setlist_groups");
        var groups = string.IsNullOrWhiteSpace(setting)
            ? new List<string>()
            : setting.Split(',').Select(g => g.Trim()).Where(g => !string.IsNullOrWhiteSpace(g)).OrderBy(g => g).ToList();
        var noGroup = NoGroupLabel;
        OszGroups = new ObservableCollection<string>(
            new[] { noGroup }.Concat(groups));
        OszSelectedGroup = noGroup;
    }

    public event Action? SetlistsImported;


    [RelayCommand]
    private async Task ImportOszAsync()
    {
        var dialog = new OpenFileDialog
            {
                Title = Application.Current.FindResource("Import.OszDialogTitle") as string,
                Filter = Application.Current.FindResource("Import.OszDialogFilter") as string,
                Multiselect = true
            };
            if (dialog.ShowDialog() != true) return;

            OszStatus = "Importuję...";

            var group = OszSelectedGroup == NoGroupLabel ? null : OszSelectedGroup;
            var importer = new OszImporter(_db, group);
            var progress = new Progress<ImportProgress>(p =>
            {
                OszStatus = $"[{p.Current}/{p.Total}] {p.Message}";
            });

            var result = await importer.ImportFilesAsync(dialog.FileNames, progress);
            SetlistsImported?.Invoke();

            OszStatus = result.Errors.Count == 0
                ? $"✓ Zaimportowano {result.ImportedSetlists} zestawów."
                : $"✓ Zestawy: {result.ImportedSetlists}   ✗ Błędy: {result.Errors.Count}\n"
                  + string.Join("\n", result.Errors);
    }
   

    private void BrowseXmlOrFolder(string fileFilter)
    {
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Wybierz plik",
            Filter = fileFilter
        };
        if (dlg.ShowDialog() == true)
            FilePath = dlg.FileName;
    }

    private void AddLog(string type, string message)
    {
        Application.Current.Dispatcher.Invoke(() =>
            LogEntries.Add(new LogEntry(type, message)));
    }
}

// Helper classes

public record CategoryOption(int? Id, string Name)
{
    public override string ToString() => Name;
}

public record LogEntry(string Type, string Message)
{
    public string Icon => Type switch
    {
        "success" => "✓",
        "error" => "✗",
        _ => "•"
    };
    public string Color => Type switch
    {
        "success" => "#4ad47a",
        "error" => "#d44a4a",
        _ => "#9aa3b8"
    };
}