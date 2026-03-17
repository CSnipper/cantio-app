# Display Tab Improvements Implementation Plan

> **For agentic workers:** REQUIRED: Use superpowers:subagent-driven-development (if subagents available) or superpowers:executing-plans to implement this plan. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Implement four improvements to the Display tab: setlist search popup, inline verse editor, accurate preview, and auto-scrolling verse list without format tags.

**Architecture:** All new logic goes into DisplayViewModel (CommunityToolkit MVVM). Two new Helpers files, one new UserControl extracted from ProjectionWindow, one new method in DatabaseService. MainWindow.xaml receives targeted edits in the PaneShow section only.

**Tech Stack:** C# 12 / .NET 10 / WPF / CommunityToolkit.Mvvm / EF Core + SQLite

---

## Task 1: Feature 4 — StripTagsConverter

**Files:**
- Create: `Cantio/Helpers/StripTagsConverter.cs`
- Modify: `Cantio/MainWindow.xaml` (add resource + change Text binding in SlideList)

- [ ] **Step 1: Create StripTagsConverter**

```csharp
// Cantio/Helpers/StripTagsConverter.cs
using System.Globalization;
using System.Text.RegularExpressions;
using System.Windows.Data;

namespace Cantio.Helpers;

[ValueConversion(typeof(string), typeof(string))]
public partial class StripTagsConverter : IValueConverter
{
    [GeneratedRegex(@"\{/?(\w+)\}")]
    private static partial Regex TagPattern();

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is string s ? TagPattern().Replace(s, string.Empty) : value;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
```

- [ ] **Step 2: Register converter in MainWindow.xaml resources**

In `MainWindow.xaml`, inside `<Window.Resources>` (after line 18, before the closing `</Window.Resources>`), add:

```xml
<helpers:StripTagsConverter x:Key="StripTags"/>
```

- [ ] **Step 3: Apply converter to SlideList Text binding**

In MainWindow.xaml around line 854, change:
```xml
Text="{Binding Text}"
```
to:
```xml
Text="{Binding Text, Converter={StaticResource StripTags}}"
```

- [ ] **Step 4: Build**

```bash
dotnet build Cantio/Cantio.csproj
```
Expected: 0 errors.

- [ ] **Step 5: Commit**

```bash
git add Cantio/Helpers/StripTagsConverter.cs Cantio/MainWindow.xaml
git commit -m "feat: StripTagsConverter — ukryj tagi formatowania w liście zwrotek"
```

---

## Task 2: Feature 4 — ListBoxAutoScrollBehavior

**Files:**
- Create: `Cantio/Helpers/ListBoxAutoScrollBehavior.cs`
- Modify: `Cantio/MainWindow.xaml` (add behavior to SlideList ListBox)

- [ ] **Step 1: Create ListBoxAutoScrollBehavior**

```csharp
// Cantio/Helpers/ListBoxAutoScrollBehavior.cs
using System.Windows;
using System.Windows.Controls;

namespace Cantio.Helpers;

public static class ListBoxAutoScrollBehavior
{
    public static readonly DependencyProperty IsEnabledProperty =
        DependencyProperty.RegisterAttached(
            "IsEnabled",
            typeof(bool),
            typeof(ListBoxAutoScrollBehavior),
            new PropertyMetadata(false, OnIsEnabledChanged));

    public static bool GetIsEnabled(ListBox obj) => (bool)obj.GetValue(IsEnabledProperty);
    public static void SetIsEnabled(ListBox obj, bool value) => obj.SetValue(IsEnabledProperty, value);

    private static void OnIsEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not ListBox listBox) return;
        if ((bool)e.NewValue)
            listBox.SelectionChanged += OnSelectionChanged;
        else
            listBox.SelectionChanged -= OnSelectionChanged;
    }

    private static void OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is not ListBox lb || lb.SelectedItem == null) return;
        lb.UpdateLayout();
        var container = lb.ItemContainerGenerator.ContainerFromItem(lb.SelectedItem) as FrameworkElement;
        container?.BringIntoView();
    }
}
```

- [ ] **Step 2: Add behavior to SlideList ListBox in MainWindow.xaml**

Find the SlideList ListBox opening tag (around line 830), which looks like:
```xml
<ListBox ItemsSource="{Binding SlideList }"
         SelectedIndex="{Binding CurrentSlideIndex}"
```
Add the behavior attribute:
```xml
<ListBox ItemsSource="{Binding SlideList}"
         SelectedIndex="{Binding CurrentSlideIndex}"
         helpers:ListBoxAutoScrollBehavior.IsEnabled="True"
```
(Also clean up the extra space in `SlideList `)

- [ ] **Step 3: Build**

```bash
dotnet build Cantio/Cantio.csproj
```
Expected: 0 errors.

- [ ] **Step 4: Commit**

```bash
git add Cantio/Helpers/ListBoxAutoScrollBehavior.cs Cantio/MainWindow.xaml
git commit -m "feat: automatyczne przewijanie listy zwrotek do aktywnego slajdu"
```

---

## Task 3: Feature 3 — ProjectionView UserControl

**Files:**
- Create: `Cantio/Views/ProjectionView.xaml`
- Create: `Cantio/Views/ProjectionView.xaml.cs`
- Modify: `Cantio/Views/ProjectionWindow.xaml`
- Modify: `Cantio/MainWindow.xaml` (replace preview section)

- [ ] **Step 1: Create ProjectionView.xaml**

```xml
<!-- Cantio/Views/ProjectionView.xaml -->
<UserControl x:Class="Cantio.Views.ProjectionView"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:helpers="clr-namespace:Cantio.Helpers">
    <UserControl.Resources>
        <helpers:BoolToVisibilityConverter x:Key="BoolToVis"/>
    </UserControl.Resources>

    <Grid Background="{Binding BackgroundBrush}" ClipToBounds="True">

        <!-- Tło graficzne -->
        <Image Source="{Binding BackgroundImagePath}"
               Stretch="UniformToFill"
               Opacity="{Binding BackgroundImageOpacity}"/>

        <!-- Tekst -->
        <TextBlock helpers:TextBlockHelper.FormattedText="{Binding SlideText}"
                   helpers:TextBlockHelper.TagDefinitions="{Binding TextTags}"
                   VerticalAlignment="{Binding TextVerticalAlignment}"
                   Margin="{Binding TextMargin}"
                   FontFamily="{Binding FontFamily}"
                   FontSize="{Binding FontSize}"
                   FontWeight="{Binding FontWeight}"
                   Foreground="{Binding TextBrush}"
                   TextAlignment="{Binding TextAlignment}"
                   TextWrapping="Wrap"
                   helpers:TextBlockHelper.LineHeightMultiplier="{Binding LineHeightMultiplier}">
            <TextBlock.Effect>
                <DropShadowEffect Color="Black"
                                  ShadowDepth="{Binding ShadowDepth}"
                                  BlurRadius="{Binding ShadowBlur}"
                                  Opacity="{Binding ShadowOpacity}"/>
            </TextBlock.Effect>
        </TextBlock>

        <!-- Overlay pusty ekran -->
        <Border Background="#000000"
                Visibility="{Binding IsBlank, Converter={StaticResource BoolToVis}}"/>
    </Grid>
</UserControl>
```

- [ ] **Step 2: Create ProjectionView.xaml.cs**

```csharp
// Cantio/Views/ProjectionView.xaml.cs
namespace Cantio.Views;

public partial class ProjectionView : System.Windows.Controls.UserControl
{
    public ProjectionView()
    {
        InitializeComponent();
    }
}
```

- [ ] **Step 3: Update ProjectionWindow.xaml to use ProjectionView**

Replace everything inside `<Window ...>` after `</WindowChrome.WindowChrome>` and `</Window.Resources>` with:

The full ProjectionWindow.xaml should become:

```xml
<Window x:Class="Cantio.Views.ProjectionWindow"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        xmlns:local="clr-namespace:Cantio.Views"
        Title="Cantio — Projekcja"
        WindowStyle="None"
        ResizeMode="NoResize"
        ShowInTaskbar="False"
        Background="{Binding BackgroundBrush}"
        AllowsTransparency="False">
    <WindowChrome.WindowChrome>
        <WindowChrome ResizeBorderThickness="0"
                      CaptionHeight="0"
                      GlassFrameThickness="0"/>
    </WindowChrome.WindowChrome>

    <local:ProjectionView DataContext="{Binding}"/>
</Window>
```

- [ ] **Step 4: Add xmlns:views to MainWindow.xaml and replace preview section**

**4a.** In `MainWindow.xaml` Window opening tag (line 1), add namespace:
```xml
xmlns:views="clr-namespace:Cantio.Views"
```

**4b.** Replace the preview `<Border>` section (lines 877–895, the `<!-- Miniaturka (16:9, 213×120) -->` block):

Old:
```xml
                                <!-- Miniaturka (16:9, 213×120) -->
                                <Border Grid.Column="0"
                                        Background="{Binding Projection.BackgroundBrush}"
                                        BorderBrush="#2a3347" BorderThickness="0,0,1,0"
                                        Width="288" Height="162" ClipToBounds="True">
                                    <Border.Effect>
                                        <DropShadowEffect Color="#000" ShadowDepth="0" BlurRadius="12" Opacity=".5"/>
                                    </Border.Effect>
                                    <Grid>
                                        <TextBlock helpers:TextBlockHelper.FormattedText="{Binding CurrentSlideText}"
                                                   helpers:TextBlockHelper.TagDefinitions="{Binding Projection.TextTags}"
                                                   Foreground="{Binding Projection.TextBrush}"
                                                   FontSize="8" LineHeight="13"
                                                   TextWrapping="Wrap" TextAlignment="Center"
                                                   VerticalAlignment="Center" HorizontalAlignment="Center"
                                                   Padding="4"/>
                                        <Border Background="Black"
                                                Visibility="{Binding ScreenBlanked, Converter={StaticResource BoolToVis}}"/>
                                    </Grid>
                                </Border>
```

New:
```xml
                                <!-- Miniaturka (16:9) — dokładna kopia ekranu projekcji -->
                                <Border Grid.Column="0"
                                        BorderBrush="#2a3347" BorderThickness="0,0,1,0"
                                        Width="288" Height="162" ClipToBounds="True">
                                    <Border.Effect>
                                        <DropShadowEffect Color="#000" ShadowDepth="0" BlurRadius="12" Opacity=".5"/>
                                    </Border.Effect>
                                    <Viewbox Stretch="Uniform">
                                        <views:ProjectionView DataContext="{Binding Projection}"
                                                              Width="1920" Height="1080"/>
                                    </Viewbox>
                                </Border>
```

- [ ] **Step 5: Build**

```bash
dotnet build Cantio/Cantio.csproj
```
Expected: 0 errors.

- [ ] **Step 6: Commit**

```bash
git add Cantio/Views/ProjectionView.xaml Cantio/Views/ProjectionView.xaml.cs Cantio/Views/ProjectionWindow.xaml Cantio/MainWindow.xaml
git commit -m "feat: ProjectionView UserControl — podgląd identyczny z ekranem projekcji"
```

---

## Task 4: Feature 2 — DatabaseService.SaveVerseTextAsync

**Files:**
- Modify: `Cantio/Services/DatabaseService.cs`

- [ ] **Step 1: Add SaveVerseTextAsync after DeleteSongAsync (around line 120)**

```csharp
    public async Task SaveVerseTextAsync(int verseId, string newText)
    {
        await using var db = new CantioDbContext();
        var verse = await db.Verses.FindAsync(verseId);
        if (verse != null)
        {
            verse.Text = newText;
            await db.SaveChangesAsync();
        }
    }
```

- [ ] **Step 2: Build**

```bash
dotnet build Cantio/Cantio.csproj
```
Expected: 0 errors.

- [ ] **Step 3: Commit**

```bash
git add Cantio/Services/DatabaseService.cs
git commit -m "feat: DatabaseService.SaveVerseTextAsync — zapis pojedynczej zwrotki"
```

---

## Task 5: Feature 1 + 2 — DisplayViewModel new properties and commands

**Files:**
- Create: `Cantio/ViewModels/EditableVerse.cs`
- Modify: `Cantio/ViewModels/DisplayViewModel.cs`

- [ ] **Step 1: Create EditableVerse.cs**

```csharp
// Cantio/ViewModels/EditableVerse.cs
using CommunityToolkit.Mvvm.ComponentModel;

namespace Cantio.ViewModels;

public partial class EditableVerse : ObservableObject
{
    public int Id { get; set; }
    public string Type { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;

    [ObservableProperty]
    private string _text = string.Empty;
}
```

- [ ] **Step 2: Add Feature 1 properties and commands to DisplayViewModel**

After the `_pinnedSetlists` field declaration in DisplayViewModel, add a new region:

```csharp
    // ── Wyszukiwarka zestawów ─────────────────────────────────────────────

    [ObservableProperty] private bool _isSetlistSearchOpen;
    [ObservableProperty] private string _setlistSearchText = string.Empty;
    [ObservableProperty] private ObservableCollection<Setlist> _filteredSetlists = [];

    private List<Setlist> _allSetlists = [];

    partial void OnSetlistSearchTextChanged(string value)
    {
        var q = value.Trim().ToLowerInvariant();
        FilteredSetlists = new ObservableCollection<Setlist>(
            string.IsNullOrEmpty(q)
                ? _allSetlists
                : _allSetlists.Where(s => s.Name.ToLowerInvariant().Contains(q)));
    }

    [RelayCommand]
    private async Task OpenSetlistSearchAsync()
    {
        _allSetlists = await _db.GetAllSetlistsAsync();
        SetlistSearchText = string.Empty;
        FilteredSetlists = new ObservableCollection<Setlist>(_allSetlists);
        IsSetlistSearchOpen = true;
    }

    [RelayCommand]
    private async Task LoadSetlistFromSearchAsync(Setlist setlist)
    {
        IsSetlistSearchOpen = false;
        await LoadPinnedSetlistAsync(setlist);
    }
```

- [ ] **Step 3: Add Feature 2 properties and commands to DisplayViewModel**

After the setlist search region, add:

```csharp
    // ── Edytor zwrotek inline ─────────────────────────────────────────────

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanOpenInlineEditor))]
    private bool _isInlineEditorOpen;

    public bool CanOpenInlineEditor => !IsInlineEditorOpen;

    [ObservableProperty] private string _inlineEditorTitle = string.Empty;
    [ObservableProperty] private ObservableCollection<EditableVerse> _editableVerses = [];

    [RelayCommand]
    private async Task OpenInlineEditorAsync(SetlistItem item)
    {
        var song = await _db.GetSongWithVersesAsync(item.SongId);
        if (song == null) return;
        InlineEditorTitle = song.Title;
        var verses = song.Verses.OrderBy(v => v.Position).ToList();
        var counts = new Dictionary<string, int>();
        EditableVerses = new ObservableCollection<EditableVerse>(
            verses.Select(v =>
            {
                counts[v.Type] = counts.GetValueOrDefault(v.Type) + 1;
                var label = v.Type switch
                {
                    "c" => counts[v.Type] == 1 ? "Refren" : $"Refren {counts[v.Type]}",
                    "b" => counts[v.Type] == 1 ? "Bridge" : $"Bridge {counts[v.Type]}",
                    _ => $"Zwrotka {counts[v.Type]}"
                };
                return new EditableVerse { Id = v.Id, Type = v.Type, Label = label, Text = v.Text };
            }));
        IsInlineEditorOpen = true;
    }

    [RelayCommand]
    private async Task SaveInlineEditAsync()
    {
        foreach (var ev in EditableVerses)
            await _db.SaveVerseTextAsync(ev.Id, ev.Text);
        IsInlineEditorOpen = false;
        RebuildSlides();
    }

    [RelayCommand]
    private void CancelInlineEdit()
    {
        IsInlineEditorOpen = false;
        EditableVerses = [];
    }
```

- [ ] **Step 4: Guard inline editor on RemoveFromSetlist**

Find the existing `RemoveFromSetlist` command and update it:

Old:
```csharp
    [RelayCommand]
    private void RemoveFromSetlist(SetlistItem item) => SetlistItems.Remove(item);
```

New:
```csharp
    [RelayCommand]
    private void RemoveFromSetlist(SetlistItem item)
    {
        if (IsInlineEditorOpen) CancelInlineEdit();
        SetlistItems.Remove(item);
    }
```

- [ ] **Step 5: Build**

```bash
dotnet build Cantio/Cantio.csproj
```
Expected: 0 errors.

- [ ] **Step 6: Commit**

```bash
git add Cantio/ViewModels/EditableVerse.cs Cantio/ViewModels/DisplayViewModel.cs
git commit -m "feat: DisplayViewModel — wyszukiwarka zestawów i edytor zwrotek inline"
```

---

## Task 6: Feature 1 — Setlist header XAML (popup wyszukiwania)

**Files:**
- Modify: `Cantio/MainWindow.xaml`

- [ ] **Step 1: Replace setlist column header**

Find the setlist header block (around lines 993–1003):
```xml
                            <Border Grid.Row="0" Background="#161b25" BorderBrush="#2a3347"
                                    BorderThickness="1,0,0,1" Padding="12,11">
                                <StackPanel Orientation="Horizontal">
                                    <TextBlock Text="{DynamicResource Section.Setlist}"
                                               FontFamily="{StaticResource HeaderFont}"
                                               FontSize="20" FontWeight="Light"
                                               Foreground="#c9a84c" Margin="0,0,10,0"/>
                                    <Border Background="#252d3d" Padding="8,2">
                                        <TextBlock Text="{Binding SetlistItems.Count}" Foreground="#9aa3b8" FontSize="10"/>
                                    </Border>
                                </StackPanel>
                            </Border>
```

Replace with:
```xml
                            <Border Grid.Row="0" Background="#161b25" BorderBrush="#2a3347"
                                    BorderThickness="1,0,0,1" Padding="12,8">
                                <Grid>
                                    <Grid.ColumnDefinitions>
                                        <ColumnDefinition Width="Auto"/>
                                        <ColumnDefinition Width="Auto"/>
                                        <ColumnDefinition Width="*"/>
                                        <ColumnDefinition Width="Auto"/>
                                    </Grid.ColumnDefinitions>
                                    <TextBlock Grid.Column="0" Text="ZESTAW"
                                               FontFamily="{StaticResource HeaderFont}"
                                               FontSize="20" FontWeight="Light"
                                               Foreground="#c9a84c" Margin="0,0,10,0"
                                               VerticalAlignment="Center"/>
                                    <Border Grid.Column="1" Background="#252d3d" Padding="8,2" VerticalAlignment="Center">
                                        <TextBlock Text="{Binding SetlistItems.Count}" Foreground="#9aa3b8" FontSize="10"/>
                                    </Border>
                                    <Button Grid.Column="3" x:Name="BtnOpenSetlist"
                                            Content="⊞ Otwórz"
                                            Command="{Binding OpenSetlistSearchAsyncCommand}"
                                            Background="#252d3d" BorderThickness="0"
                                            Foreground="#9aa3b8" FontSize="13" Cursor="Hand"
                                            Padding="10,5" VerticalAlignment="Center">
                                        <Button.Template>
                                            <ControlTemplate TargetType="Button">
                                                <Border Background="{TemplateBinding Background}"
                                                        CornerRadius="4" Padding="{TemplateBinding Padding}">
                                                    <ContentPresenter HorizontalAlignment="Center" VerticalAlignment="Center"/>
                                                </Border>
                                                <ControlTemplate.Triggers>
                                                    <Trigger Property="IsMouseOver" Value="True">
                                                        <Setter Property="Background" Value="#c9a84c"/>
                                                        <Setter Property="Foreground" Value="#0f1117"/>
                                                    </Trigger>
                                                </ControlTemplate.Triggers>
                                            </ControlTemplate>
                                        </Button.Template>
                                    </Button>
                                </Grid>
                            </Border>
```

- [ ] **Step 2: Add Popup after the header Border**

The Popup must be inside the same Grid as the header so it can reference `BtnOpenSetlist` by name. Add it directly after the closing `</Border>` of the header (still inside the setlist column Grid, before `<Border Grid.Row="1" ...>`):

```xml
                            <!-- Popup wyszukiwania zestawów -->
                            <Popup Grid.Row="0"
                                   IsOpen="{Binding IsSetlistSearchOpen, Mode=TwoWay}"
                                   PlacementTarget="{Binding ElementName=BtnOpenSetlist}"
                                   Placement="Bottom"
                                   StaysOpen="False"
                                   AllowsTransparency="True">
                                <Border Background="#1e2535" BorderBrush="#2a3347" BorderThickness="1"
                                        Width="340" MaxHeight="400">
                                    <Grid Margin="8">
                                        <Grid.RowDefinitions>
                                            <RowDefinition Height="Auto"/>
                                            <RowDefinition Height="*"/>
                                        </Grid.RowDefinitions>
                                        <TextBox Grid.Row="0"
                                                 Style="{StaticResource DarkTextBox}"
                                                 Text="{Binding SetlistSearchText, UpdateSourceTrigger=PropertyChanged}"
                                                 FontSize="15" Padding="8,6" Margin="0,0,0,6"/>
                                        <ListBox Grid.Row="1"
                                                 ItemsSource="{Binding FilteredSetlists}"
                                                 Background="Transparent" BorderThickness="0"
                                                 MaxHeight="340"
                                                 ScrollViewer.HorizontalScrollBarVisibility="Disabled"
                                                 ItemContainerStyle="{StaticResource DarkListItem}">
                                            <ListBox.ItemTemplate>
                                                <DataTemplate>
                                                    <Button Background="Transparent" BorderThickness="0"
                                                            Cursor="Hand" HorizontalAlignment="Stretch"
                                                            Command="{Binding DataContext.LoadSetlistFromSearchAsyncCommand, RelativeSource={RelativeSource AncestorType=ListBox}}"
                                                            CommandParameter="{Binding}">
                                                        <Button.Template>
                                                            <ControlTemplate TargetType="Button">
                                                                <Border Background="Transparent" Padding="4,8">
                                                                    <StackPanel>
                                                                        <TextBlock Text="{Binding Name}"
                                                                                   FontSize="16" Foreground="#e8eaf0"
                                                                                   TextTrimming="CharacterEllipsis"/>
                                                                        <TextBlock Text="{Binding CreatedAt, StringFormat='{}{0:dd.MM.yyyy}'}"
                                                                                   FontSize="11" Foreground="#959fb9"/>
                                                                    </StackPanel>
                                                                </Border>
                                                                <ControlTemplate.Triggers>
                                                                    <Trigger Property="IsMouseOver" Value="True">
                                                                        <Setter Property="Background" Value="#252d3d"/>
                                                                    </Trigger>
                                                                </ControlTemplate.Triggers>
                                                            </ControlTemplate>
                                                        </Button.Template>
                                                    </Button>
                                                </DataTemplate>
                                            </ListBox.ItemTemplate>
                                        </ListBox>
                                    </Grid>
                                </Border>
                            </Popup>
```

- [ ] **Step 3: Build**

```bash
dotnet build Cantio/Cantio.csproj
```
Expected: 0 errors.

- [ ] **Step 4: Commit**

```bash
git add Cantio/MainWindow.xaml
git commit -m "feat: nagłówek ZESTAW + popup wyszukiwania zestawów"
```

---

## Task 7: Feature 2 — Inline editor panel XAML

**Files:**
- Modify: `Cantio/MainWindow.xaml`

- [ ] **Step 1: Add ✏ button column to setlist item template**

Find the setlist item DataTemplate Grid column definitions (around line 1017):
```xml
                                                <Grid.ColumnDefinitions>
                                                    <ColumnDefinition Width="12"/>
                                                    <ColumnDefinition Width="30"/>
                                                    <ColumnDefinition Width="22"/>
                                                    <ColumnDefinition Width="*"/>
                                                    <ColumnDefinition Width="30"/>
                                                </Grid.ColumnDefinitions>
```

Replace with (adds a column for ✏ before ×):
```xml
                                                <Grid.ColumnDefinitions>
                                                    <ColumnDefinition Width="12"/>
                                                    <ColumnDefinition Width="30"/>
                                                    <ColumnDefinition Width="22"/>
                                                    <ColumnDefinition Width="*"/>
                                                    <ColumnDefinition Width="34"/>
                                                    <ColumnDefinition Width="30"/>
                                                </Grid.ColumnDefinitions>
```

- [ ] **Step 2: Update Grid.Column on the × button**

Find (around line 1040):
```xml
                                                <Button Grid.Column="4" Content="×"
```
Change to `Grid.Column="5"`.

- [ ] **Step 3: Add ✏ button (Grid.Column="4") before the × button**

Insert after the song title TextBlock (before the × button):
```xml
                                                <Button Grid.Column="4" Content="✏"
                                                        Width="30" Height="30"
                                                        Background="Transparent" BorderThickness="0"
                                                        Foreground="#959fb9" FontSize="16" Cursor="Hand"
                                                        IsEnabled="{Binding DataContext.CanOpenInlineEditor, RelativeSource={RelativeSource AncestorType=ListBox}}"
                                                        Command="{Binding DataContext.OpenInlineEditorAsyncCommand, RelativeSource={RelativeSource AncestorType=ListBox}}"
                                                        CommandParameter="{Binding}">
                                                    <Button.Template>
                                                        <ControlTemplate TargetType="Button">
                                                            <Border Background="Transparent">
                                                                <TextBlock Text="✏"
                                                                           HorizontalAlignment="Center"
                                                                           VerticalAlignment="Center"
                                                                           FontSize="15"
                                                                           Foreground="{TemplateBinding Foreground}"/>
                                                            </Border>
                                                            <ControlTemplate.Triggers>
                                                                <Trigger Property="IsMouseOver" Value="True">
                                                                    <Setter Property="Foreground" Value="#c9a84c"/>
                                                                </Trigger>
                                                                <Trigger Property="IsEnabled" Value="False">
                                                                    <Setter Property="Foreground" Value="#3a4460"/>
                                                                </Trigger>
                                                            </ControlTemplate.Triggers>
                                                        </ControlTemplate>
                                                    </Button.Template>
                                                </Button>
```

- [ ] **Step 4: Add slide-in editor panel**

The setlist column `<Grid Grid.Column="1" Background="#0f1117">` needs `ClipToBounds="True"`. Find that line (around 985):
```xml
                        <Grid Grid.Column="1" Background="#0f1117">
```
Change to:
```xml
                        <Grid Grid.Column="1" Background="#0f1117" ClipToBounds="True">
```

Then add the slide-in panel as the last child inside this Grid (after the last `</Border>` but before the closing `</Grid>`):

```xml
                            <!-- Panel edycji zwrotek — wyjeżdża z prawej -->
                            <Grid Grid.Row="0" Grid.RowSpan="4" Panel.ZIndex="10"
                                  Background="#1e2535">
                                <Grid.RenderTransform>
                                    <TranslateTransform X="350"/>
                                </Grid.RenderTransform>
                                <Grid.Style>
                                    <Style TargetType="Grid">
                                        <Style.Triggers>
                                            <DataTrigger Binding="{Binding IsInlineEditorOpen}" Value="True">
                                                <DataTrigger.EnterActions>
                                                    <BeginStoryboard>
                                                        <Storyboard>
                                                            <DoubleAnimation
                                                                Storyboard.TargetProperty="(UIElement.RenderTransform).(TranslateTransform.X)"
                                                                To="0" Duration="0:0:0.2"/>
                                                        </Storyboard>
                                                    </BeginStoryboard>
                                                </DataTrigger.EnterActions>
                                                <DataTrigger.ExitActions>
                                                    <BeginStoryboard>
                                                        <Storyboard>
                                                            <DoubleAnimation
                                                                Storyboard.TargetProperty="(UIElement.RenderTransform).(TranslateTransform.X)"
                                                                To="350" Duration="0:0:0.15"/>
                                                        </Storyboard>
                                                    </BeginStoryboard>
                                                </DataTrigger.ExitActions>
                                            </DataTrigger>
                                        </Style.Triggers>
                                    </Style>
                                </Grid.Style>

                                <Grid Margin="0">
                                    <Grid.RowDefinitions>
                                        <RowDefinition Height="Auto"/>
                                        <RowDefinition Height="*"/>
                                        <RowDefinition Height="Auto"/>
                                    </Grid.RowDefinitions>

                                    <!-- Nagłówek -->
                                    <Border Grid.Row="0" Background="#161b25" BorderBrush="#2a3347"
                                            BorderThickness="0,0,0,1" Padding="12,10">
                                        <Grid>
                                            <Grid.ColumnDefinitions>
                                                <ColumnDefinition Width="*"/>
                                                <ColumnDefinition Width="Auto"/>
                                            </Grid.ColumnDefinitions>
                                            <TextBlock Grid.Column="0" Text="{Binding InlineEditorTitle}"
                                                       FontSize="15" FontWeight="SemiBold"
                                                       Foreground="#e8eaf0"
                                                       TextTrimming="CharacterEllipsis"
                                                       VerticalAlignment="Center"/>
                                            <Button Grid.Column="1" Content="×"
                                                    Background="Transparent" BorderThickness="0"
                                                    Foreground="#959fb9" FontSize="20" Cursor="Hand"
                                                    Command="{Binding CancelInlineEditCommand}">
                                                <Button.Template>
                                                    <ControlTemplate TargetType="Button">
                                                        <Border Background="Transparent" Padding="8,4">
                                                            <TextBlock Text="×" FontSize="20"
                                                                       Foreground="{TemplateBinding Foreground}"/>
                                                        </Border>
                                                        <ControlTemplate.Triggers>
                                                            <Trigger Property="IsMouseOver" Value="True">
                                                                <Setter Property="Foreground" Value="#d44a4a"/>
                                                            </Trigger>
                                                        </ControlTemplate.Triggers>
                                                    </ControlTemplate>
                                                </Button.Template>
                                            </Button>
                                        </Grid>
                                    </Border>

                                    <!-- Lista zwrotek do edycji -->
                                    <ScrollViewer Grid.Row="1" VerticalScrollBarVisibility="Auto"
                                                  HorizontalScrollBarVisibility="Disabled">
                                        <ItemsControl ItemsSource="{Binding EditableVerses}"
                                                      Margin="8,4">
                                            <ItemsControl.ItemTemplate>
                                                <DataTemplate>
                                                    <StackPanel Margin="0,6">
                                                        <TextBlock Text="{Binding Label}"
                                                                   FontSize="11" Foreground="#c9a84c"
                                                                   Margin="0,0,0,3" FontWeight="SemiBold"/>
                                                        <TextBox Text="{Binding Text, UpdateSourceTrigger=PropertyChanged}"
                                                                 Style="{StaticResource DarkTextBox}"
                                                                 AcceptsReturn="True"
                                                                 TextWrapping="Wrap"
                                                                 MinHeight="60"
                                                                 FontSize="14"
                                                                 Padding="8,6"/>
                                                    </StackPanel>
                                                </DataTemplate>
                                            </ItemsControl.ItemTemplate>
                                        </ItemsControl>
                                    </ScrollViewer>

                                    <!-- Przyciski Zapisz / Anuluj -->
                                    <Border Grid.Row="2" Background="#161b25" BorderBrush="#2a3347"
                                            BorderThickness="0,1,0,0" Padding="8,8">
                                        <Grid>
                                            <Grid.ColumnDefinitions>
                                                <ColumnDefinition Width="*"/>
                                                <ColumnDefinition Width="Auto"/>
                                            </Grid.ColumnDefinitions>
                                            <Button Grid.Column="0"
                                                    Content="Zapisz"
                                                    Style="{StaticResource OutlineBtn}"
                                                    Margin="0,0,7,0"
                                                    Command="{Binding SaveInlineEditAsyncCommand}"/>
                                            <Button Grid.Column="1"
                                                    Content="Anuluj"
                                                    Style="{StaticResource RedBtn}"
                                                    Command="{Binding CancelInlineEditCommand}"/>
                                        </Grid>
                                    </Border>
                                </Grid>
                            </Grid>
```

- [ ] **Step 5: Build**

```bash
dotnet build Cantio/Cantio.csproj
```
Expected: 0 errors.

- [ ] **Step 6: Commit**

```bash
git add Cantio/MainWindow.xaml
git commit -m "feat: panel edycji zwrotek inline — wysuwa się z prawej strony zestawu"
```

---

## Task 8: Final build and smoke test

- [ ] **Step 1: Full clean build**

```bash
dotnet build Cantio/Cantio.csproj
```
Expected: 0 errors, only pre-existing NU1701 warnings.

- [ ] **Step 2: Verify all 4 features in runtime (manual)**

Run: `dotnet run --project Cantio/Cantio.csproj`

Checklist:
- [ ] Verse list shows no `{wk}`, `{/wk}`, `{big}`, `{/big}` etc.
- [ ] Selecting a slide scrolls the list to show it
- [ ] Preview thumbnail (bottom left) matches the projection window content (background, text, blank)
- [ ] Setlist header shows "ZESTAW" and "⊞ Otwórz" button; clicking it opens popup with search
- [ ] Selecting a setlist from popup loads it and closes popup
- [ ] ✏ button appears on each setlist item; clicking opens slide-in editor panel
- [ ] Editing verse text and clicking "Zapisz" persists the change and rebuilds slides
- [ ] "Anuluj" closes panel without saving
- [ ] While panel is open, ✏ on other items is disabled

- [ ] **Step 3: Commit if any last fixes**

```bash
git add -A
git commit -m "fix: poprawki po weryfikacji ręcznej"
```
