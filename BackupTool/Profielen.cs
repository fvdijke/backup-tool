using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace BackupTool;

// Profielbestand (backup-profielen.json). Zelfde opbouw als bij de PowerShell-versie, zodat beide hetzelfde
// bestand kunnen lezen. Ontbrekende velden krijgen hun standaardwaarde.
public sealed class ProfielBestand
{
    [JsonPropertyName("laatste")] public string Laatste { get; set; } = "Standaard";
    [JsonPropertyName("profielen")] public List<Profiel> Profielen { get; set; } = new();
}

public sealed class Profiel
{
    [JsonPropertyName("naam")] public string Naam { get; set; } = "Standaard";
    [JsonPropertyName("doel")] public string Doel { get; set; } = "";
    [JsonPropertyName("submap")] public string Submap { get; set; } = "";
    [JsonPropertyName("log")] public string Log { get; set; } = @"G:\Backup-Logs\robocopy-log.txt";
    [JsonPropertyName("modus")] public string Modus { get; set; } = "bijwerken";      // of "spiegelen"
    [JsonPropertyName("pogingen")] public int Pogingen { get; set; } = 1;
    [JsonPropertyName("wacht")] public int Wacht { get; set; } = 1;
    [JsonPropertyName("threads")] public int Threads { get; set; } = 1;
    [JsonPropertyName("alleRegels")] public bool AlleRegels { get; set; } = true;
    [JsonPropertyName("afsluiten")] public bool Afsluiten { get; set; }
    [JsonPropertyName("tijdelijk")] public bool Tijdelijk { get; set; } = true;
    [JsonPropertyName("verborgen")] public bool Verborgen { get; set; }
    [JsonPropertyName("mapDatum")] public bool MapDatum { get; set; } = true;
    [JsonPropertyName("fft")] public bool Fft { get; set; }
    [JsonPropertyName("dst")] public bool Dst { get; set; }
    [JsonPropertyName("herstart")] public bool Herstart { get; set; }
    [JsonPropertyName("controleren")] public bool Controleren { get; set; }           // na elk bestand de hash van kopie en origineel vergelijken
    [JsonPropertyName("pauzeBijAfwijking")] public bool PauzeBijAfwijking { get; set; } = true;   // wijkt de controle af: backup op pauze
    [JsonPropertyName("schaduwkopie")] public bool Schaduwkopie { get; set; }         // geopende bestanden via een momentopname
    [JsonPropertyName("plan")] public Planning Plan { get; set; } = new();
    // uit de PowerShell-versie; niet meer in het venster, blijft alleen bewaard
    [JsonPropertyName("slaBestanden")] public string SlaBestanden { get; set; } = "";
    [JsonPropertyName("slaMappen")] public string SlaMappen { get; set; } = "";
    [JsonPropertyName("taken")] public List<Taak> Taken { get; set; } = new();

    public Profiel Kopie(string naam)
    {
        var p = JsonSerializer.Deserialize<Profiel>(JsonSerializer.Serialize(this, Profielen.Opties), Profielen.Opties)!;
        p.Naam = naam;
        return p;
    }
}

// automatisch starten via Windows Taakplanner
public sealed class Planning
{
    [JsonPropertyName("aan")] public bool Aan { get; set; }
    [JsonPropertyName("frequentie")] public string Frequentie { get; set; } = "wekelijks";   // dagelijks of wekelijks
    [JsonPropertyName("dag")] public int Dag { get; set; }                                    // 0 = zondag ... 6 = zaterdag
    [JsonPropertyName("tijd")] public string Tijd { get; set; } = "02:00";
}

public sealed class Taak
{
    [JsonPropertyName("aan")] public bool Aan { get; set; } = true;
    [JsonPropertyName("naam")] public string Naam { get; set; } = "";
    [JsonPropertyName("bron")] public string Bron { get; set; } = "";
    [JsonPropertyName("doel")] public string Doel { get; set; } = "";
    // aantal mappen bij de vorige backup: voor een percentage tijdens het vergelijken
    [JsonPropertyName("mappen")] public long Mappen { get; set; }
    // uitgevinkte submappen, als pad ten opzichte van de bron
    [JsonPropertyName("overslaan"), JsonConverter(typeof(TekstLijstConverter))]
    public List<string> Overslaan { get; set; } = new();
}

public static class Profielen
{
    public static readonly JsonSerializerOptions Opties = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        NumberHandling = JsonNumberHandling.AllowReadingFromString,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,   // "ë" en "\" leesbaar laten
    };

    public static ProfielBestand Lees(string pad)
    {
        ProfielBestand? d = null;
        if (File.Exists(pad)) d = JsonSerializer.Deserialize<ProfielBestand>(File.ReadAllText(pad, Encoding.UTF8), Opties);
        d ??= new ProfielBestand();
        d.Profielen.RemoveAll(p => p is null);
        if (d.Profielen.Count == 0) d.Profielen.Add(new Profiel());
        foreach (var p in d.Profielen) { p.Taken.RemoveAll(t => t is null); p.Plan ??= new(); }
        return d;
    }

    public static void Schrijf(string pad, ProfielBestand d)
    {
        // eerst naar een tijdelijk bestand, dan vervangen: zo raakt het profiel nooit half geschreven
        var tijdelijk = pad + ".nieuw";
        File.WriteAllText(tijdelijk, JsonSerializer.Serialize(d, Opties), new UTF8Encoding(false));
        File.Move(tijdelijk, pad, true);
    }
}

// PowerShell schrijft een lijst met één element soms als losse tekst, en een lege lijst soms als null
public sealed class TekstLijstConverter : JsonConverter<List<string>>
{
    public override List<string> Read(ref Utf8JsonReader r, Type t, JsonSerializerOptions o)
    {
        var lijst = new List<string>();
        if (r.TokenType == JsonTokenType.Null) return lijst;
        if (r.TokenType == JsonTokenType.String) { if (r.GetString() is { Length: > 0 } s) lijst.Add(s); return lijst; }
        if (r.TokenType != JsonTokenType.StartArray) throw new JsonException("lijst verwacht");
        while (r.Read() && r.TokenType != JsonTokenType.EndArray)
            if (r.TokenType == JsonTokenType.String && r.GetString() is { Length: > 0 } s) lijst.Add(s);
        return lijst;
    }

    public override void Write(Utf8JsonWriter w, List<string> v, JsonSerializerOptions o)
    {
        w.WriteStartArray();
        foreach (var s in v) w.WriteStringValue(s);
        w.WriteEndArray();
    }
}
