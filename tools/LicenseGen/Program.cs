using System.Security.Cryptography;
using System.Text;
using Cantio.Services;

// Narzędzie AUTORA — wystawia klucze licencyjne pulpitu organisty niewidomego.
//
// Klucz PRYWATNY nigdy nie leży w repozytorium (które jest publiczne). Mieszka
// w %USERPROFILE%\.cantio\license-signing.key i tylko stamtąd jest czytany. Gdyby wyciekł,
// każdy mógłby wystawiać klucze na dowolne nazwisko — czyli cała bariera („program mówi,
// czyj to klucz") przestałaby cokolwiek znaczyć.
//
// Użycie:
//   LicenseGen                       — pokazuje stan (i przy pierwszym uruchomieniu tworzy parę)
//   LicenseGen "Jan Kowalski"        — wypisuje gotowy klucz do wysłania mailem
//   LicenseGen --klucz-publiczny     — sam klucz publiczny (do wklejenia w LicenseKey.cs)
//   LicenseGen --sprawdz "KLUCZ"     — weryfikuje klucz kluczem publicznym z pliku

Console.OutputEncoding = Encoding.UTF8;

var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
var keyDir = Path.Combine(home, ".cantio");
var keyPath = Path.Combine(keyDir, "license-signing.key");

try
{
    return Run(args);
}
catch (Exception ex)
{
    Console.Error.WriteLine($"BŁĄD: {ex.Message}");
    return 2;
}

int Run(string[] argv)
{
    var command = argv.Length > 0 ? argv[0] : string.Empty;

    switch (command)
    {
        case "--pomoc" or "-h" or "--help":
            PrintUsage();
            return 0;

        case "--klucz-publiczny":
            if (!RequireKey(out var pubOnly)) return 1;
            PrintPublicKey(pubOnly);
            return 0;

        case "--sprawdz":
            if (argv.Length < 2) { Console.Error.WriteLine("Podaj klucz do sprawdzenia."); return 1; }
            if (!RequireKey(out var verifier)) return 1;
            return Verify(argv[1], ExportPublicKey(verifier)) ? 0 : 1;

        case "":
            return Status();

        default:
            if (command.StartsWith("--"))
            {
                Console.Error.WriteLine($"Nieznane polecenie: {command}");
                PrintUsage();
                return 1;
            }
            return Issue(command);
    }
}

// ── Klucz prywatny ───────────────────────────────────────────────────────────

/// <summary>Wczytuje klucz autora; przy pierwszym uruchomieniu tworzy parę. NIGDY nie nadpisuje.</summary>
bool RequireKey(out ECDsa key)
{
    if (File.Exists(keyPath))
    {
        key = ECDsa.Create();
        key.ImportPkcs8PrivateKey(Convert.FromBase64String(File.ReadAllText(keyPath).Trim()), out _);
        return true;
    }

    Console.WriteLine("Nie ma jeszcze klucza podpisującego — tworzę nową parę.");
    key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
    Directory.CreateDirectory(keyDir);
    File.WriteAllText(keyPath, Convert.ToBase64String(key.ExportPkcs8PrivateKey()));

    Console.WriteLine($"Klucz PRYWATNY zapisany: {keyPath}");
    Console.WriteLine("Zrób jego kopię w bezpiecznym miejscu (menedżer haseł, pendrive w szufladzie).");
    Console.WriteLine("Bez niego nie wystawisz ANI JEDNEGO nowego klucza dla już wydanej wersji programu,");
    Console.WriteLine("bo klucz publiczny jest wkompilowany w Cantio.");
    Console.WriteLine();
    PrintPublicKey(key);
    return true;
}

string ExportPublicKey(ECDsa key) => Convert.ToBase64String(key.ExportSubjectPublicKeyInfo());

void PrintPublicKey(ECDsa key)
{
    Console.WriteLine("Klucz PUBLICZNY — wklej do Cantio.Core/Services/LicenseKey.cs, stała PublicKeyBase64:");
    Console.WriteLine();
    Console.WriteLine($"        \"{ExportPublicKey(key)}\";");
    Console.WriteLine();
}

// ── Polecenia ────────────────────────────────────────────────────────────────

int Status()
{
    if (File.Exists(keyPath))
    {
        Console.WriteLine($"Klucz podpisujący JEST: {keyPath}");
        Console.WriteLine("UWAGA: nowej pary nie tworzę — nadpisanie unieważniłoby wszystkie");
        Console.WriteLine("dotąd wystawione klucze licencyjne. Aby zrobić nową parę, usuń plik ręcznie.");
        Console.WriteLine();
        if (!RequireKey(out var key)) return 1;
        PrintPublicKey(key);
        PrintUsage();
        return 0;
    }

    if (!RequireKey(out _)) return 1;
    PrintUsage();
    return 0;
}

int Issue(string name)
{
    if (!RequireKey(out var key)) return 1;

    var issued = DateOnly.FromDateTime(DateTime.Now);
    var body = LicenseKey.BuildBody(name, issued, LicenseKey.ProductAccessibleDesk);
    var signature = key.SignData(body, HashAlgorithmName.SHA256);
    var licence = LicenseKey.Compose(body, signature);

    // Kontrola natychmiastowa: klucz, którego nie da się odczytać własnym kluczem publicznym,
    // nie ma prawa wyjść do użytkownika (a odesłanie go = telefon od niewidomego w niedzielę).
    if (!LicenseKey.TryParse(licence, ExportPublicKey(key), out var check) || check.Name != name.Trim())
    {
        Console.Error.WriteLine("BŁĄD: wystawiony klucz nie przechodzi własnej weryfikacji. Nic nie wysyłaj.");
        return 2;
    }

    Console.WriteLine($"Licencja dla: {check.Name}");
    Console.WriteLine($"Produkt:      {check.Product}");
    Console.WriteLine($"Wystawiono:   {check.IssuedAt:yyyy-MM-dd}");
    Console.WriteLine();
    Console.WriteLine("Klucz licencyjny (wklej do maila — w programie F9 w pulpicie niewidomego):");
    Console.WriteLine();
    Console.WriteLine(licence);
    Console.WriteLine();
    Console.WriteLine("Przy przepisywaniu ze słuchu myślniki i wielkość liter nie mają znaczenia,");
    Console.WriteLine("a litery I, L, O są równoważne cyfrom 1, 1 i 0.");
    return 0;
}

bool Verify(string licence, string publicKeyBase64)
{
    if (LicenseKey.TryParse(licence, publicKeyBase64, out var info))
    {
        Console.WriteLine($"WAŻNY — {info.Name}, {info.Product}, {info.IssuedAt:yyyy-MM-dd}");
        return true;
    }
    Console.WriteLine("ODRZUCONY (zły podpis, uszkodzona treść albo klucz z innej pary).");
    return false;
}

void PrintUsage()
{
    Console.WriteLine("Użycie:");
    Console.WriteLine("  LicenseGen \"Jan Kowalski\"     — wystaw klucz licencyjny");
    Console.WriteLine("  LicenseGen --klucz-publiczny   — pokaż klucz publiczny do wklejenia w kod");
    Console.WriteLine("  LicenseGen --sprawdz \"KLUCZ\"   — sprawdź istniejący klucz");
}
