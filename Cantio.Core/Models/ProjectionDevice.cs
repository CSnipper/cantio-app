namespace Cantio.Models;

/// <summary>
/// Urządzenie projekcyjne (TV / projektor) sterowane po sieci LAN.
/// Serializowane jako JSON w ustawieniu <c>projection_devices</c>.
/// </summary>
public class ProjectionDevice
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Name { get; set; } = "";
    /// <summary>
    /// Krótkie oznaczenie (maks. 4 znaki) pokazywane na przycisku paska górnego.
    /// Puste = fallback na numer porządkowy. Pole dodane po v1.55 — starsze wpisy JSON
    /// go nie mają i deserializują się z pustą wartością.
    /// </summary>
    public string Label { get; set; } = "";
    /// <summary>"samsung" | "pjlink" | "wol"</summary>
    public string Type { get; set; } = "";
    public string Ip { get; set; } = "";
    public string Mac { get; set; } = "";
    /// <summary>Samsung: token parowania.</summary>
    public string Token { get; set; } = "";
    /// <summary>PjLink: hasło (może być puste).</summary>
    public string Password { get; set; } = "";
    /// <summary>PjLink: port TCP, domyślnie 4352.</summary>
    public int Port { get; set; } = 4352;
}
