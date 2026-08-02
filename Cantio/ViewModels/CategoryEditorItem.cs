using CommunityToolkit.Mvvm.ComponentModel;

namespace Cantio.ViewModels;

public partial class CategoryEditorItem : ObservableObject
{
    /// <summary>Id wirtualnej pozycji „Bez kategorii" — nie istnieje w bazie.</summary>
    public const int UncategorizedId = -1;

    public int Id { get; set; }
    public int Number { get; set; }

    /// <summary>
    /// true = wirtualna pozycja „Bez kategorii" (pieśni z <c>CategoryId == NULL</c>).
    /// Nie jest prawdziwą kategorią: bez ▲▼✎✕, nie da się jej edytować ani skasować.
    /// </summary>
    public bool IsVirtual { get; init; }

    /// <summary>Odwrotność <see cref="IsVirtual"/> — do bindowania widoczności przycisków.</summary>
    public bool IsRealCategory => !IsVirtual;

    [ObservableProperty] private string _name = string.Empty;
    [ObservableProperty] private string _editName = string.Empty;
    [ObservableProperty] private int _editNumber;
    [ObservableProperty] private bool _isEditing = false;
    [ObservableProperty] private bool _canMoveUp = true;
    [ObservableProperty] private bool _canMoveDown = true;

    partial void OnIsEditingChanged(bool value)
    {
        if (value) { EditName = Name; EditNumber = Number; }
    }
}
