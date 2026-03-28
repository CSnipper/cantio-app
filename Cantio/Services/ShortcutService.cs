using System.Windows.Input;

namespace Cantio.Services;

/// <summary>
/// Holds the active shortcut map. One shared instance created in MainWindow,
/// passed to DisplayViewModel and ShortcutsViewModel.
/// </summary>
public class ShortcutService
{
    // ── Action ID constants ────────────────────────────────────────────────
    public const string SlideNext   = "slide_next";
    public const string SlidePrev   = "slide_prev";
    public const string SongNext    = "song_next";
    public const string SongPrev    = "song_prev";
    public const string Blank       = "blank";
    public const string TabShow     = "tab_show";
    public const string TabTemplate = "tab_template";
    public const string TabImport   = "tab_import";
    public const string SearchOpen  = "search_open";
    public const string SongSearch  = "song_search";

    public static readonly IReadOnlyList<string> AllActions =
    [
        SlideNext, SlidePrev, SongNext, SongPrev, Blank,
        TabShow, TabTemplate, TabImport, SearchOpen, SongSearch
    ];

    private static readonly Dictionary<string, string> _defaults = new()
    {
        [SlideNext]   = "Right",
        [SlidePrev]   = "Left",
        [SongNext]    = "Down",
        [SongPrev]    = "Up",
        [Blank]       = "Escape",
        [TabShow]     = string.Empty,
        [TabTemplate] = string.Empty,
        [TabImport]   = string.Empty,
        [SearchOpen]  = string.Empty,
        [SongSearch]  = "Ctrl+F",
    };

    public static IReadOnlyDictionary<string, string> Defaults => _defaults;

    // label → (Key, ModifierKeys); missing entry = unassigned
    private Dictionary<string, (Key key, ModifierKeys mods)> _map = new();
    private Dictionary<string, string> _rawLabels = new();

    // ── Loading ────────────────────────────────────────────────────────────

    public async Task LoadWithLabelsAsync(DatabaseService db)
    {
        _map.Clear();
        _rawLabels.Clear();
        foreach (var actionId in AllActions)
        {
            var stored = await db.GetSettingAsync($"shortcut_{actionId}");
            var label  = stored ?? _defaults.GetValueOrDefault(actionId, string.Empty);
            _rawLabels[actionId] = label;
            if (!string.IsNullOrEmpty(label))
                _map[actionId] = ParseLabel(label);
        }
    }

    public string GetLabel(string actionId)
        => _rawLabels.GetValueOrDefault(actionId,
           _defaults.GetValueOrDefault(actionId, string.Empty));

    public void SetLabel(string actionId, string label)
    {
        _rawLabels[actionId] = label;
        _map.Remove(actionId);
        if (!string.IsNullOrEmpty(label))
            _map[actionId] = ParseLabel(label);
    }

    // ── Matching ───────────────────────────────────────────────────────────

    public bool IsMatch(Key key, ModifierKeys modifiers, string actionId)
    {
        if (!_map.TryGetValue(actionId, out var expected)) return false;
        return key == expected.key && modifiers == expected.mods;
    }

    // ── Label ↔ Key conversion ─────────────────────────────────────────────

    /// <summary>Converts a captured key+modifiers to a display label like "Ctrl+F" or "Escape".</summary>
    public static string KeyComboToLabel(Key key, ModifierKeys modifiers)
    {
        var label = KeyToLabel(key);
        if (string.IsNullOrEmpty(label)) return string.Empty;
        bool ctrl = modifiers.HasFlag(ModifierKeys.Control);
        bool isLetter = key >= Key.A && key <= Key.Z;
        return (ctrl || isLetter) ? "Ctrl+" + label : label;
    }

    public static string KeyToLabel(Key key) => key switch
    {
        >= Key.A and <= Key.Z             => key.ToString(),
        >= Key.D0 and <= Key.D9           => ((int)(key - Key.D0)).ToString(),
        >= Key.NumPad0 and <= Key.NumPad9 => $"N{(int)(key - Key.NumPad0)}",
        >= Key.F1 and <= Key.F12          => key.ToString(),
        Key.Space   => "Space",
        Key.Escape  => "Escape",
        Key.Return  => "Return",
        Key.Up      => "Up",
        Key.Down    => "Down",
        Key.Left    => "Left",
        Key.Right   => "Right",
        Key.Prior   => "Prior",
        Key.Next    => "Next",
        Key.Home    => "Home",
        Key.End     => "End",
        Key.OemComma  => ",",
        Key.OemPeriod => ".",
        Key.OemMinus  => "-",
        Key.OemPlus   => "+",
        _ => key.ToString()
    };

    private static (Key key, ModifierKeys mods) ParseLabel(string label)
    {
        if (label.StartsWith("Ctrl+", StringComparison.OrdinalIgnoreCase))
            return (LabelToKey(label[5..]), ModifierKeys.Control);
        return (LabelToKey(label), ModifierKeys.None);
    }

    private static Key LabelToKey(string label)
    {
        if (label.Length == 1 && label[0] is >= 'A' and <= 'Z')
            return Key.A + (label[0] - 'A');
        if (label.Length == 1 && label[0] is >= '0' and <= '9')
            return Key.D0 + (label[0] - '0');
        if (label.Length == 2 && label[0] == 'N' && label[1] is >= '0' and <= '9')
            return Key.NumPad0 + (label[1] - '0');
        if (label.Length >= 2 && label[0] == 'F' && int.TryParse(label[1..], out var fn) && fn >= 1 && fn <= 12)
            return Key.F1 + (fn - 1);
        return label switch
        {
            "Space"   => Key.Space,
            "Escape"  => Key.Escape,
            "Return"  => Key.Return,
            "Up"      => Key.Up,
            "Down"    => Key.Down,
            "Left"    => Key.Left,
            "Right"   => Key.Right,
            "Prior"   => Key.Prior,
            "Next"    => Key.Next,
            "Home"    => Key.Home,
            "End"     => Key.End,
            ","       => Key.OemComma,
            "."       => Key.OemPeriod,
            "-"       => Key.OemMinus,
            "+"       => Key.OemPlus,
            _ => Enum.TryParse<Key>(label, out var k) ? k : Key.None
        };
    }
}
