using System.Security.Cryptography;
using System.Text;

namespace Cantio.Services;

/// <summary>Co niesie ważny klucz licencyjny. Nazwisko jest tu POLEM, nie ozdobą — pulpit je mówi.</summary>
public readonly record struct LicenseInfo(string Name, DateOnly IssuedAt, string Product);

/// <summary>
/// Klucz licencyjny pulpitu organisty niewidomego — podpis ECDSA P-256 nad jawną treścią.
///
/// Model zabezpieczenia jest SPOŁECZNY, nie techniczny: klucz niesie imię i nazwisko właściciela,
/// a pulpit je pokazuje i każe przeczytać czytnikowi ekranu przy każdym starcie. Skopiowany klucz
/// działa, ale ogłasza w cudzym kościele cudze nazwisko — i to jest cała bariera. Dlatego nie ma
/// tu ani serwera aktywacyjnego, ani wiązania ze sprzętem: obie te rzeczy uderzyłyby w uczciwego
/// użytkownika (parafia bez internetu, wymiana komputera), a nieuczciwego i tak nie zatrzymują.
///
/// Podpis chroni JEDYNĄ rzecz, która musi być nie do podmiany: nazwisko w kluczu. Bez podpisu
/// wystarczyłoby podmienić literę w tekście, żeby klucz kolegi ogłaszał własne nazwisko —
/// i społeczna bariera znikałaby w całości.
///
/// FORMAT (wersja 1), zaprojektowany pod DYKTOWANIE PRZEZ TELEFON i wklejanie ze schowka:
///   • treść podpisywana: <c>1|produkt|RRRR-MM-DD|Imię Nazwisko</c> (UTF-8);
///   • blob: [0x01 wersja][1 bajt długości treści][treść][64 bajty podpisu r‖s];
///   • zapis: Crockford base32 (bez I, L, O, U — znaków mylących w odsłuchu i w piśmie),
///     wielkimi literami, w grupach po 5 rozdzielonych myślnikiem.
/// Przy odczycie wszystko, co nie jest znakiem alfabetu, jest pomijane (myślniki, spacje,
/// znaki nowej linii z maila), a I/L→1 oraz O→0 są mapowane jak w Crockfordzie — pomyłka
/// w przepisaniu tych liter nie unieważnia klucza.
/// </summary>
public static class LicenseKey
{
    /// <summary>Identyfikator produktu w kluczu — inny produkt = klucz nie dla tego pulpitu.</summary>
    public const string ProductAccessibleDesk = "pulpit-niewidomego";

    /// <summary>Klucz w tabeli ustawień, pod którym pulpit trzyma klucz licencyjny.</summary>
    public const string SettingKey = "license_key";

    /// <summary>
    /// KLUCZ PUBLICZNY autora (SubjectPublicKeyInfo, base64). Klucz prywatny NIE ISTNIEJE w tym
    /// repozytorium — leży wyłącznie u autora w <c>%USERPROFILE%\.cantio\license-signing.key</c>
    /// i jest w wyłącznej dyspozycji narzędzia <c>tools/LicenseGen</c>.
    /// </summary>
    public const string PublicKeyBase64 =
        "MFkwEwYHKoZIzj0CAQYIKoZIzj0DAQcDQgAEcTiDceDPSCBwF2b9QpB0IN0i1595" +
        "CbxzyEfrh9fbY4c+NobgBqwCvY/8MQFoEY+zlRg0zyS65/wNy43a7fSMhg==";

    private const byte Version = 1;
    private const int SignatureLength = 64;   // ECDSA P-256, surowe r‖s

    // Crockford base32: bez I, L, O, U. Powód jest wprost z tego zastosowania — klucz bywa
    // dyktowany przez telefon niewidomemu użytkownikowi, a „O" i „0" brzmią identycznie.
    private const string Alphabet = "0123456789ABCDEFGHJKMNPQRSTVWXYZ";

    /// <summary>Sprawdza podpis wbudowanym kluczem publicznym autora.</summary>
    public static bool TryParse(string? text, out LicenseInfo info)
        => TryParse(text, PublicKeyBase64, out info);

    /// <summary>
    /// Sprawdza podpis WSKAZANYM kluczem publicznym (SubjectPublicKeyInfo w base64).
    /// Przeciążenie istnieje dla testów: bez niego nie dałoby się udowodnić, że klucz podpisany
    /// CUDZYM kluczem prywatnym jest odrzucany, bez wynoszenia klucza autora do harnessu.
    /// </summary>
    public static bool TryParse(string? text, string publicKeyBase64, out LicenseInfo info)
    {
        info = default;
        if (string.IsNullOrWhiteSpace(text)) return false;

        byte[] blob;
        try { blob = Base32Decode(text); }
        catch { return false; }

        // [wersja][długość treści][treść][podpis]
        if (blob.Length < 2 + SignatureLength) return false;
        if (blob[0] != Version) return false;
        int bodyLength = blob[1];
        if (blob.Length != 2 + bodyLength + SignatureLength) return false;

        var body = new byte[bodyLength];
        Array.Copy(blob, 2, body, 0, bodyLength);
        var signature = new byte[SignatureLength];
        Array.Copy(blob, 2 + bodyLength, signature, 0, SignatureLength);

        if (!VerifySignature(body, signature, publicKeyBase64)) return false;

        return TryReadBody(Encoding.UTF8.GetString(body), out info);
    }

    /// <summary>
    /// Sprawdzenie podpisu — WYDZIELONE, żeby sabotaż w testach miał jeden, oczywisty cel.
    /// Zwrócenie stąd <c>true</c> bez sprawdzenia musi wywrócić testy licencji.
    /// </summary>
    private static bool VerifySignature(byte[] body, byte[] signature, string publicKeyBase64)
    {
        try
        {
            using var ecdsa = ECDsa.Create();
            ecdsa.ImportSubjectPublicKeyInfo(Convert.FromBase64String(publicKeyBase64), out _);
            return ecdsa.VerifyData(body, signature, HashAlgorithmName.SHA256);
        }
        catch { return false; }
    }

    private static bool TryReadBody(string body, out LicenseInfo info)
    {
        info = default;
        // Nazwisko może zawierać spacje i myślniki, ale NIE „|" — separator jest zastrzeżony
        // (Build go odrzuca), więc podział na 4 części jest jednoznaczny.
        var parts = body.Split('|');
        if (parts.Length != 4) return false;
        if (parts[0] != Version.ToString()) return false;
        if (parts[1].Length == 0) return false;
        if (!DateOnly.TryParseExact(parts[2], "yyyy-MM-dd", out var issued)) return false;
        var name = parts[3].Trim();
        if (name.Length == 0) return false;

        info = new LicenseInfo(name, issued, parts[1]);
        return true;
    }

    /// <summary>
    /// Buduje treść do podpisania. Wspólna dla narzędzia autora i dla weryfikacji — gdyby każde
    /// z nich składało tekst po swojemu, pierwsza rozbieżność (np. w formacie daty) unieważniłaby
    /// klucze wystawione wcześniej, a wyszłoby to dopiero u użytkownika.
    /// </summary>
    public static byte[] BuildBody(string name, DateOnly issuedAt, string product)
    {
        name = (name ?? string.Empty).Trim();
        if (name.Length == 0) throw new ArgumentException("Puste imię i nazwisko.", nameof(name));
        if (name.Contains('|')) throw new ArgumentException("Znak | jest zastrzeżony.", nameof(name));
        if (string.IsNullOrWhiteSpace(product) || product.Contains('|'))
            throw new ArgumentException("Nieprawidłowy identyfikator produktu.", nameof(product));

        var body = Encoding.UTF8.GetBytes($"{Version}|{product}|{issuedAt:yyyy-MM-dd}|{name}");
        // Długość treści mieści się w JEDNYM bajcie — nazwisko dłuższe niż ~200 znaków nie istnieje,
        // ale gdyby ktoś je wkleił, klucz ma paść TU, u autora, a nie u użytkownika.
        if (body.Length > 255) throw new ArgumentException("Treść licencji jest za długa.", nameof(name));
        return body;
    }

    /// <summary>Składa gotowy klucz z treści i podpisu (używa go narzędzie autora).</summary>
    public static string Compose(byte[] body, byte[] signature)
    {
        if (signature.Length != SignatureLength)
            throw new ArgumentException($"Podpis musi mieć {SignatureLength} bajtów.", nameof(signature));

        var blob = new byte[2 + body.Length + signature.Length];
        blob[0] = Version;
        blob[1] = (byte)body.Length;
        Array.Copy(body, 0, blob, 2, body.Length);
        Array.Copy(signature, 0, blob, 2 + body.Length, signature.Length);
        return Base32Encode(blob);
    }

    // ── Crockford base32 ─────────────────────────────────────────────────────

    private static string Base32Encode(byte[] data)
    {
        var sb = new StringBuilder();
        int buffer = 0, bits = 0, group = 0;
        foreach (var b in data)
        {
            buffer = (buffer << 8) | b;
            bits += 8;
            while (bits >= 5)
            {
                bits -= 5;
                Append(sb, Alphabet[(buffer >> bits) & 31], ref group);
            }
        }
        if (bits > 0) Append(sb, Alphabet[(buffer << (5 - bits)) & 31], ref group);
        return sb.ToString();

        static void Append(StringBuilder sb, char c, ref int group)
        {
            if (group == 5) { sb.Append('-'); group = 0; }
            sb.Append(c);
            group++;
        }
    }

    /// <summary>
    /// Odczyt tolerancyjny: pomija wszystko, co nie jest znakiem alfabetu (myślniki, spacje,
    /// łamania wiersza z maila), a I/L i O mapuje na 1 i 0 — te trzy litery są w klawiaturze
    /// i w odsłuchu nie do odróżnienia od cyfr, więc nie wolno na nich stracić klucza.
    /// Rzuca, gdy trafi znak, którego NIE DA się zinterpretować (wtedy to nie jest nasz klucz).
    /// </summary>
    private static byte[] Base32Decode(string text)
    {
        var bytes = new List<byte>();
        int buffer = 0, bits = 0;
        foreach (var raw in text)
        {
            if (char.IsWhiteSpace(raw) || raw is '-' or '_' or '.' or ',') continue;
            var c = char.ToUpperInvariant(raw);
            c = c switch { 'I' or 'L' => '1', 'O' => '0', _ => c };
            int v = Alphabet.IndexOf(c);
            if (v < 0) throw new FormatException($"Nieznany znak: {raw}");
            buffer = (buffer << 5) | v;
            bits += 5;
            if (bits >= 8)
            {
                bits -= 8;
                bytes.Add((byte)((buffer >> bits) & 0xFF));
            }
        }

        // Ogon. Poprawny klucz zostawia najwyżej 4 bity dopełnienia i wszystkie są zerami.
        // Bez tego sprawdzenia znak DOKLEJONY na końcu (5 bitów, które nie tworzą pełnego bajtu)
        // byłby po cichu pomijany i klucz z literówką na końcu przechodziłby jako poprawny —
        // dokładnie ten przypadek złapał test (lc-6).
        if (bits >= 5 || (buffer & ((1 << bits) - 1)) != 0)
            throw new FormatException("Klucz ma nadmiarowe znaki na końcu.");

        return bytes.ToArray();
    }
}
