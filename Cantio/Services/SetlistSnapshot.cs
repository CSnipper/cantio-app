using Cantio.Models;

namespace Cantio.Services;

/// <summary>
/// Jedyne miejsce, w którym przepisujemy pola <see cref="SetlistItem"/> przy zapisie zestawu.
/// Zapis idzie przez „usuń stare pozycje + wstaw nowe", więc pole POMINIĘTE w tym przepisaniu
/// cicho ginie przy każdym ponownym zapisie zestawu (tak przepadały notatki „Uwagi").
/// Dodając kolumnę do SetlistItem — dopisz ją TUTAJ.
/// </summary>
public static class SetlistSnapshot
{
    /// <param name="items">Pozycje w kolejności wyświetlania.</param>
    /// <param name="setlistId">Zestaw docelowy; 0 = kopia robocza jeszcze bez przypisania.</param>
    /// <returns>
    /// Nowe obiekty BEZ nav property <see cref="SetlistItem.Song"/> — inaczej EF próbuje wstawić
    /// istniejące pieśni jeszcze raz — z pozycjami przenumerowanymi 1..N.
    /// </returns>
    public static List<SetlistItem> ForSave(IEnumerable<SetlistItem> items, int setlistId = 0) =>
        items.Select((item, i) => new SetlistItem
        {
            SetlistId = setlistId,
            SongId = item.SongId,
            Position = i + 1,
            Type = item.Type,
            SelectedVerses = item.SelectedVerses,
            ImagePath = item.ImagePath,
            Notes = item.Notes,
            CustomTitle = item.CustomTitle,
            CustomText = item.CustomText
        }).ToList();
}
