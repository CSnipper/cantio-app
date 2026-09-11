using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using Cantio.Helpers;

namespace Cantio.Services;

/// <summary>
/// Obrazki na łączu z Pilotem (v1.69): podgląd (<c>image_get</c>), wysyłka z telefonu
/// (<c>image_put_begin</c>/<c>_chunk</c>/<c>_end</c>) i dołożenie pozycji do BIEŻĄCEGO zestawu
/// (<c>setlist_add_image</c>).
///
/// <para>Powód istnienia: parafie pracują w trybie serwerowym, gdzie tablet jest JEDYNYM pulpitem —
/// bez tej rodziny komend obrazek dawało się dodać wyłącznie przy komputerze, a w zestawie z telefonu
/// ginął.</para>
///
/// <para><b>Podział odpowiedzialności.</b> Ta klasa jest bezgłowa: parsuje, waliduje, składa acki
/// i skleja plik. Dwóch rzeczy NIE robi i robić nie może:</para>
/// <list type="bullet">
/// <item>skalowania obrazu — rdzeń jest czystym <c>net10.0</c> (Linux/Android), a jedyny dostępny
/// koder żyje w WPF; gospodarz wpina go delegatem <see cref="Scaler"/>;</item>
/// <item>mutacji zestawu — bieżąca lista żyje w pamięci <c>DisplayViewModel</c>, więc
/// <c>setlist_add_image</c> wraca z <see cref="Result.AddedImageRef"/>, a dołożenie pozycji wykonuje
/// <c>MainWindow</c> na Dispatcherze przez <c>DisplayViewModel.ApplyImageItem</c> — tę samą ścieżkę,
/// co przycisk 🖼 w oknie (broadcast `setlist` poleci sam z <c>CollectionChanged</c>).</item>
/// </list>
///
/// <para><b>Dlaczego upload jest dzielony na kawałki:</b> jedna koperta WS z całym zdjęciem potrafi
/// mieć kilka MB, a serwer składa wiadomość w pamięci. Telefon skaluje przed wysyłką (dłuższy bok
/// ≤1920, JPEG ~85), więc realnie idzie kilkanaście kawałków po ≤64 kB.</para>
/// </summary>
public static class PilotImages
{
    public const string GetCommand          = "image_get";
    public const string PutBeginCommand     = "image_put_begin";
    public const string PutChunkCommand     = "image_put_chunk";
    public const string PutEndCommand       = "image_put_end";
    public const string AddToSetlistCommand = "setlist_add_image";

    /// <summary>Typ odpowiedzi na <c>image_get</c>.</summary>
    public const string DataType = "image_data";

    /// <summary>Dłuższy bok podglądu, gdy Pilot nie poda <c>maxDim</c>.</summary>
    public const int DefaultMaxDim = 1280;
    /// <summary>Górna granica <c>maxDim</c> — podgląd nie ma być większy niż ekran projekcji.</summary>
    public const int MaxDimLimit = 4096;

    /// <summary>Najwięcej kawałków w jednym uploadzie (×64 kB ≈ 32 MB).</summary>
    public const int MaxChunks = 512;
    /// <summary>Najwięcej równoległych uploadów z JEDNEGO klienta.</summary>
    public const int MaxConcurrentUploads = 2;
    /// <summary>Maksymalny rozmiar pojedynczego kawałka PO zdekodowaniu.</summary>
    public const int MaxChunkBytes = 96 * 1024;
    /// <summary>Upload bez kawałka przez ten czas jest sprzątany.</summary>
    public static readonly TimeSpan UploadTimeout = TimeSpan.FromSeconds(60);

    public const string ReasonNotFound      = "not_found";
    public const string ReasonTooLarge      = "too_large";
    public const string ReasonBusy          = "busy";
    public const string ReasonBadSeq        = "bad_seq";
    public const string ReasonUnknownUpload = "unknown_upload";
    public const string ReasonBadTotal      = "bad_total";
    public const string ReasonBadData       = "bad_data";
    public const string ReasonIncomplete    = "incomplete";
    public const string ReasonReadFailed    = "read_failed";

    /// <summary>
    /// Skalowanie obrazu wpinane przez gospodarza (WPF): pełna ścieżka + dłuższy bok →
    /// bajty JPEG i wymiary wyniku. <c>null</c> = pliku nie da się zdekodować.
    /// </summary>
    public delegate (byte[] Data, int Width, int Height)? Scaler(string fullPath, int maxDim);

    /// <param name="Response">gotowy JSON do NADAWCY (ack albo <c>image_data</c>); null = nie nasza komenda</param>
    /// <param name="AddedImageRef">
    /// niepuste przy udanym <c>setlist_add_image</c> — wołający ma dołożyć pozycję do bieżącego
    /// zestawu. Mutacja nie ma prawa się nie udać, więc ack jest już zbudowany jako <c>ok:true</c>.
    /// </param>
    public readonly record struct Result(string? Response, string? AddedImageRef = null);

    public static bool IsCommand(string? type) =>
        type is GetCommand or PutBeginCommand or PutChunkCommand or PutEndCommand or AddToSetlistCommand;

    // ─── Ścieżki ────────────────────────────────────────────────────────

    /// <summary>
    /// Czy ref wskazuje plik, który desktop realnie ma. JEDYNE miejsce tej decyzji — używa jej
    /// handler <c>setlist_restore</c>, zapis zestawu z Pilota i <c>setlist_add_image</c>.
    /// </summary>
    public static bool RefExists(string? imageRef) =>
        IsSafeRef(imageRef) && File.Exists(ImageStorage.Resolve(imageRef!));

    /// <summary>
    /// Odsiewa ref-y wychodzące poza magazyn obrazków (`..\..\`). Ścieżki ABSOLUTNE przechodzą,
    /// bo tak wyglądają obrazki dodane przed wprowadzeniem magazynu (legacy w bazach parafii)
    /// i to desktop sam je wcześniej rozgłosił.
    /// </summary>
    public static bool IsSafeRef(string? imageRef)
    {
        if (string.IsNullOrWhiteSpace(imageRef)) return false;
        foreach (var part in imageRef.Split('/', '\\'))
            if (part == "..") return false;
        return true;
    }

    // ─── Stan uploadów ──────────────────────────────────────────────────

    /// <summary>
    /// Trwające uploady. Stan jest z natury per-serwer (jedna instancja na aplikację), a limit
    /// równoległości liczy się per KLIENT — stąd klucz właściciela przekazywany z zewnątrz
    /// (w aplikacji: obiekt <c>WebSocket</c>).
    /// </summary>
    public sealed class UploadStore
    {
        private sealed class Upload
        {
            public object Owner = null!;
            public string Name = "";
            public int Total;
            public int Received;
            public readonly MemoryStream Data = new();
            public DateTime LastTouch = DateTime.UtcNow;
        }

        private readonly Dictionary<string, Upload> _uploads = [];
        private readonly System.Threading.Lock _lock = new();

        /// <summary>Zegar do podmiany w testach — inaczej timeout 60 s musiałby czekać 60 s.</summary>
        public Func<DateTime> UtcNow { get; set; } = () => DateTime.UtcNow;

        public int Count { get { lock (_lock) { Prune(); return _uploads.Count; } } }

        private void Prune()
        {
            var now = UtcNow();
            foreach (var key in _uploads.Where(kv => now - kv.Value.LastTouch > UploadTimeout)
                                        .Select(kv => kv.Key).ToList())
            {
                _uploads[key].Data.Dispose();
                _uploads.Remove(key);
            }
        }

        /// <summary>null = klient ma już maksimum równoległych uploadów (<c>busy</c>).</summary>
        public string? Begin(object owner, string name, int totalChunks)
        {
            lock (_lock)
            {
                Prune();
                if (_uploads.Values.Count(u => ReferenceEquals(u.Owner, owner)) >= MaxConcurrentUploads)
                    return null;
                var id = Convert.ToHexString(RandomNumberGenerator.GetBytes(8));
                _uploads[id] = new Upload
                {
                    Owner = owner, Name = name, Total = totalChunks, LastTouch = UtcNow()
                };
                return id;
            }
        }

        /// <summary>Dopisanie kawałka; zwraca powód odmowy albo null przy powodzeniu.</summary>
        public string? Chunk(object owner, string uploadId, int seq, byte[] data)
        {
            lock (_lock)
            {
                Prune();
                if (!_uploads.TryGetValue(uploadId, out var u) || !ReferenceEquals(u.Owner, owner))
                    return ReasonUnknownUpload;
                // Kawałki MUSZĄ iść po kolei — luka znaczyłaby dziurę w pliku, a JPEG z dziurą
                // wygląda jak poprawny plik i wysypuje się dopiero na projektorze.
                if (seq != u.Received) return ReasonBadSeq;
                if (data.Length > MaxChunkBytes || u.Data.Length + data.Length > (long)MaxChunks * 64 * 1024)
                    return ReasonTooLarge;
                u.Data.Write(data, 0, data.Length);
                u.Received++;
                u.LastTouch = UtcNow();
                return null;
            }
        }

        /// <summary>
        /// Domknięcie uploadu. <c>Reason</c> niepuste = odmowa (wtedy nic nie jest zapisane);
        /// przy powodzeniu zwraca komplet bajtów i nazwę pliku, a wpis znika ze słownika.
        /// </summary>
        public (string? Reason, byte[]? Data, string Name) End(object owner, string uploadId)
        {
            lock (_lock)
            {
                Prune();
                if (!_uploads.TryGetValue(uploadId, out var u) || !ReferenceEquals(u.Owner, owner))
                    return (ReasonUnknownUpload, null, "");
                if (u.Received < u.Total) return (ReasonIncomplete, null, u.Name);
                var bytes = u.Data.ToArray();
                u.Data.Dispose();
                _uploads.Remove(uploadId);
                return (null, bytes, u.Name);
            }
        }

        /// <summary>Sprzątanie po rozłączeniu klienta — niedokończone uploady nie mają właściciela.</summary>
        public void DropOwner(object owner)
        {
            lock (_lock)
            {
                foreach (var key in _uploads.Where(kv => ReferenceEquals(kv.Value.Owner, owner))
                                            .Select(kv => kv.Key).ToList())
                {
                    _uploads[key].Data.Dispose();
                    _uploads.Remove(key);
                }
            }
        }
    }

    // ─── Obsługa komend ─────────────────────────────────────────────────

    /// <param name="owner">właściciel uploadów (obiekt połączenia) — nośnik limitu „2 naraz"</param>
    /// <param name="scaler">skalowanie z warstwy WPF; null = <c>image_get</c> odpowiada <c>read_failed</c></param>
    public static Result Handle(string rawJson, object owner, UploadStore uploads, Scaler? scaler)
    {
        JsonElement root;
        try { root = JsonDocument.Parse(rawJson).RootElement.Clone(); }
        catch { return default; }

        if (root.ValueKind != JsonValueKind.Object ||
            !root.TryGetProperty("type", out var typeEl) || typeEl.ValueKind != JsonValueKind.String)
            return default;

        var type = typeEl.GetString();
        return type switch
        {
            GetCommand          => HandleGet(root, scaler),
            PutBeginCommand     => HandleBegin(root, owner, uploads),
            PutChunkCommand     => HandleChunk(root, owner, uploads),
            PutEndCommand       => HandleEnd(root, owner, uploads),
            AddToSetlistCommand => HandleAdd(root),
            _                   => default
        };
    }

    private static Result HandleGet(JsonElement root, Scaler? scaler)
    {
        var imageRef = Str(root, "ref");
        if (!RefExists(imageRef)) return Deny(GetCommand, ReasonNotFound);

        var maxDim = Int(root, "maxDim") is int m && m > 0 ? Math.Min(m, MaxDimLimit) : DefaultMaxDim;
        if (scaler == null) return Deny(GetCommand, ReasonReadFailed);

        (byte[] Data, int Width, int Height)? scaled;
        try { scaled = scaler(ImageStorage.Resolve(imageRef!), maxDim); }
        catch { scaled = null; }
        if (scaled == null) return Deny(GetCommand, ReasonReadFailed);

        return new Result(BuildDataJson(imageRef!, scaled.Value.Width, scaled.Value.Height, scaled.Value.Data));
    }

    /// <summary>Jedyne miejsce składania <c>image_data</c>.</summary>
    public static string BuildDataJson(string imageRef, int width, int height, byte[] jpeg) =>
        JsonSerializer.Serialize(new Dictionary<string, object?>
        {
            ["type"]   = DataType,
            ["ref"]    = imageRef,
            ["w"]      = width,
            ["h"]      = height,
            ["format"] = "jpeg",
            ["data"]   = Convert.ToBase64String(jpeg)
        });

    private static Result HandleBegin(JsonElement root, object owner, UploadStore uploads)
    {
        var total = Int(root, "totalChunks") ?? 0;
        if (total <= 0)        return Deny(PutBeginCommand, ReasonBadTotal);
        if (total > MaxChunks) return Deny(PutBeginCommand, ReasonTooLarge);

        var id = uploads.Begin(owner, SafeFileName(Str(root, "name")), total);
        if (id == null) return Deny(PutBeginCommand, ReasonBusy);

        return new Result(PilotStatus.BuildAckJson(PutBeginCommand, true, ("uploadId", id)));
    }

    private static Result HandleChunk(JsonElement root, object owner, UploadStore uploads)
    {
        var id  = Str(root, "uploadId") ?? "";
        var seq = Int(root, "seq") ?? -1;

        byte[] data;
        try { data = Convert.FromBase64String(Str(root, "data") ?? ""); }
        catch { return Deny(PutChunkCommand, ReasonBadData, ("uploadId", id), ("seq", seq)); }

        var reason = uploads.Chunk(owner, id, seq, data);
        return reason != null
            ? Deny(PutChunkCommand, reason, ("uploadId", id), ("seq", seq))
            : new Result(PilotStatus.BuildAckJson(PutChunkCommand, true, ("uploadId", id), ("seq", seq)));
    }

    private static Result HandleEnd(JsonElement root, object owner, UploadStore uploads)
    {
        var id = Str(root, "uploadId") ?? "";
        var (reason, data, name) = uploads.End(owner, id);
        if (reason != null) return Deny(PutEndCommand, reason, ("uploadId", id));

        string imageRef;
        // Unikalny PODKATALOG zamiast prefiksu w nazwie pliku: ImageStorage.Import bierze nazwę
        // docelową z Path.GetFileName, więc wszystko doklejone do nazwy tymczasowej zostawało
        // użytkownikowi w folderze images (i w tytule pozycji zestawu) na zawsze.
        var tempDir = Directory.CreateDirectory(
            Path.Combine(Path.GetTempPath(), $"cantio_up_{Guid.NewGuid():N}"));
        var temp = Path.Combine(tempDir.FullName, name!);
        try
        {
            File.WriteAllBytes(temp, data!);
            // Ten sam magazyn co przycisk 🖼 w oknie — rozstrzyganie kolizji nazw włącznie.
            imageRef = ImageStorage.Import(temp);
        }
        catch { return Deny(PutEndCommand, ReasonReadFailed, ("uploadId", id)); }
        finally { try { if (Directory.Exists(tempDir.FullName)) Directory.Delete(tempDir.FullName, recursive: true); } catch { } }

        return new Result(PilotStatus.BuildAckJson(PutEndCommand, true, ("uploadId", id), ("ref", imageRef)));
    }

    private static Result HandleAdd(JsonElement root)
    {
        var imageRef = Str(root, "ref");
        if (!RefExists(imageRef)) return Deny(AddToSetlistCommand, ReasonNotFound);
        return new Result(
            PilotStatus.BuildAckJson(AddToSetlistCommand, true, ("ref", imageRef)), imageRef);
    }

    // ─── Pomocnicze ─────────────────────────────────────────────────────

    private static Result Deny(string command, string reason, params (string Key, object? Value)[] extra)
        => new(PilotStatus.BuildAckJson(command, false, [("reason", reason), .. extra]));

    /// <summary>
    /// Nazwa z telefonu służy WYŁĄCZNIE do rozszerzenia i czytelności w folderze images — bierzemy
    /// z niej sam człon pliku i wycinamy znaki, których Windows nie przyjmie.
    /// </summary>
    public static string SafeFileName(string? name)
    {
        var raw = Path.GetFileName(name ?? "") ?? "";
        var clean = new string(raw.Where(ch => !Path.GetInvalidFileNameChars().Contains(ch)).ToArray()).Trim();
        if (clean.Length == 0) return "obrazek.jpg";
        return clean.Length > 80 ? clean[^80..] : clean;
    }

    private static string? Str(JsonElement el, string name)
        => el.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static int? Int(JsonElement el, string name)
        => el.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number
           && v.TryGetInt32(out var i) ? i : null;
}
