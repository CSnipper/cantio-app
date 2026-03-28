using CommunityToolkit.Mvvm.ComponentModel;

namespace Cantio.ViewModels;

public partial class GroupEditorItem : ObservableObject
{
    public string OriginalName { get; set; } = string.Empty;

    [ObservableProperty] private string _editName = string.Empty;
    [ObservableProperty] private bool _isEditing = false;

    partial void OnIsEditingChanged(bool value)
    {
        if (value) EditName = OriginalName;
    }
}
