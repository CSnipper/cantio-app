using CommunityToolkit.Mvvm.ComponentModel;

namespace Cantio.ViewModels;

public partial class CategoryEditorItem : ObservableObject
{
    public int Id { get; set; }
    public int Number { get; set; }

    [ObservableProperty] private string _name = string.Empty;
    [ObservableProperty] private string _editName = string.Empty;
    [ObservableProperty] private int _editNumber;
    [ObservableProperty] private bool _isEditing = false;

    partial void OnIsEditingChanged(bool value)
    {
        if (value) { EditName = Name; EditNumber = Number; }
    }
}
