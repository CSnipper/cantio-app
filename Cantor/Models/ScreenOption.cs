namespace Cantor.Models;

public record ScreenOption
{
    public int Index { get; init; }
    public string Label { get; init; } = string.Empty;
}
