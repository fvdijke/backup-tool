using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using BackupTool;

// Gebruik: MotorTest <werkmap>
// Bouwt een bron en een (deels gevulde) doelmap, draait robocopy op kopie A en de eigen motor op kopie B, en
// vergelijkt beide uitkomsten: elke map en elk bestand (grootte, datum, kenmerken, inhoud) en de tellingen.
var werk = args.Length > 0 ? args[0] : Path.Combine(Path.GetTempPath(), "motortest");
int fouten = 0;

foreach (var spiegelen in new[] { false, true })
foreach (var threads in new[] { 1, 4 })
{
    var naam = $"{(spiegelen ? "spiegelen" : "bijwerken")}, {threads} thread{(threads > 1 ? "s" : "")}";
    Console.WriteLine($"=== {naam}");
    if (Directory.Exists(werk)) WisAlles(werk);
    var bron = Path.Combine(werk, "bron"); var doelA = Path.Combine(werk, "robocopy"); var doelB = Path.Combine(werk, "motor");
    MaakBron(bron); MaakDoel(doelA); MaakDoel(doelB);

    for (int ronde = 1; ronde <= 2; ronde++)          // ronde 2: alles is al gelijk, er mag niets meer gebeuren
    {
        var rc = Robocopy(bron, doelA, spiegelen, threads);
        var m = Motor(bron, doelB, spiegelen, threads);
        foreach (var (soort, r, e) in new[] { ("Mappen", rc.Mappen, m.Mappen), ("Bestanden", rc.Bestanden, m.Bestanden), ("Bytes", rc.Bytes, m.Bytes) })
        {
            var ok = r.SequenceEqual(e);
            // robocopy /MT telt mappen anders (elke map als "gekopieerd"); dat nemen we bewust niet over
            bool mtMappen = threads > 1 && soort == "Mappen";
            if (!ok && !mtMappen) fouten++;
            Console.WriteLine($"  ronde {ronde} {soort,-10} robocopy [{string.Join(", ", r)}]  motor [{string.Join(", ", e)}]  " +
                (ok ? "gelijk" : mtMappen ? "anders (robocopy /MT telt mappen anders)" : "VERSCHIL"));
        }
        var verschillen = VergelijkMappen(doelA, doelB);
        fouten += verschillen.Count;
        foreach (var v in verschillen.Take(20)) Console.WriteLine("    VERSCHIL " + v);
        Console.WriteLine($"  ronde {ronde} inhoud: {(verschillen.Count == 0 ? "gelijk" : verschillen.Count + " verschillen")}");
    }
}
Console.WriteLine(fouten == 0 ? "ALLES GELIJK" : $"{fouten} VERSCHILLEN");
return fouten == 0 ? 0 : 1;

// ---------------------------------------------------------------- testmappen
static void MaakBron(string b)
{
    var t = new DateTime(2024, 1, 1, 12, 0, 0, DateTimeKind.Utc);
    Bestand(b, "gelijk.txt", "aaaa", t);
    Bestand(b, "nieuwer.txt", "nieuwe versie", t.AddDays(1));
    Bestand(b, "ouder.txt", "oude versie", t);
    Bestand(b, "gewijzigd.txt", "12345", t);
    Bestand(b, "nieuw.txt", "nieuw bestand", t);
    Bestand(b, "alleenlezen.txt", "nieuwe inhoud", t.AddDays(1));
    Bestand(b, "rommel.tmp", "tijdelijk", t);
    Bestand(b, @"Cache\c.txt", "cache", t);
    Bestand(b, @"sub\diep\x\data.bin", Willekeurig(65536, 1), t);
    Bestand(b, @"sub\a.txt", "a", t.AddHours(-5));
    Bestand(b, "groot.bin", Willekeurig(5 * 1024 * 1024, 2), t);
    Bestand(b, "verborgen.txt", "geheim", t, FileAttributes.Hidden);
    Bestand(b, "spatie en ë.txt", "naam met spatie en accent", t);
    Directory.CreateDirectory(Path.Combine(b, "leeg"));
    Directory.CreateDirectory(Path.Combine(b, @"sub\leeg2"));
    foreach (var d in Directory.GetDirectories(b, "*", SearchOption.AllDirectories).Append(b))
    { Directory.SetCreationTimeUtc(d, t.AddDays(-10)); Directory.SetLastWriteTimeUtc(d, t.AddDays(-9)); }
}

static void MaakDoel(string d)
{
    var t = new DateTime(2024, 1, 1, 12, 0, 0, DateTimeKind.Utc);
    Bestand(d, "gelijk.txt", "aaaa", t);
    Bestand(d, "nieuwer.txt", "oud", t);
    Bestand(d, "ouder.txt", "deze is nieuwer", t.AddDays(1));
    Bestand(d, "gewijzigd.txt", "123", t);
    Bestand(d, "alleenlezen.txt", "oud", t, FileAttributes.ReadOnly);
    Bestand(d, "extra.txt", "extra", t);
    Bestand(d, @"extramap\e.txt", "e", t);
    Bestand(d, @"extramap\dieper\f.txt", "f", t, FileAttributes.ReadOnly);
    Bestand(d, @"Cache\bewaar.txt", "uitgesloten map: niet wissen", t);
    Bestand(d, "oud.tmp", "uitgesloten bestand: niet wissen", t);
}

static void Bestand(string map, string rel, object inhoud, DateTime t, FileAttributes extra = 0)
{
    var p = Path.Combine(map, rel);
    Directory.CreateDirectory(Path.GetDirectoryName(p)!);
    if (inhoud is string s) File.WriteAllText(p, s); else File.WriteAllBytes(p, (byte[])inhoud);
    File.SetCreationTimeUtc(p, t.AddDays(-1));
    File.SetLastWriteTimeUtc(p, t);
    if (extra != 0) File.SetAttributes(p, File.GetAttributes(p) | extra);
}

static byte[] Willekeurig(int n, int zaad) { var b = new byte[n]; new Random(zaad).NextBytes(b); return b; }

static void WisAlles(string map)
{
    foreach (var f in Directory.GetFiles(map, "*", SearchOption.AllDirectories)) File.SetAttributes(f, FileAttributes.Normal);
    foreach (var d in Directory.GetDirectories(map, "*", SearchOption.AllDirectories)) new DirectoryInfo(d).Attributes = FileAttributes.Directory;
    Directory.Delete(map, true);
}

// ---------------------------------------------------------------- uitvoeren
static Uitkomst Robocopy(string bron, string doel, bool spiegelen, int threads)
{
    var arg = $"\"{bron}\" \"{doel}\" {(spiegelen ? "/MIR" : "/E /XO")} /R:0 /W:0 /DCOPY:DAT /XF *.tmp /XD Cache /NP /NFL /NDL /NJH /BYTES" +
              (threads > 1 ? $" /MT:{threads}" : "");
    var psi = new ProcessStartInfo("robocopy.exe", arg) { RedirectStandardOutput = true, UseShellExecute = false, CreateNoWindow = true };
    using var p = Process.Start(psi)!;
    var uit = p.StandardOutput.ReadToEnd(); p.WaitForExit();
    long[] Rij(string label)
    {
        var m = Regex.Match(uit, @"^\s*" + label + @"\s*:\s*(.+)$", RegexOptions.Multiline);
        return Regex.Matches(m.Groups[1].Value, @"\d+").Select(x => long.Parse(x.Value)).Take(6).ToArray();
    }
    return new Uitkomst(Rij("Dirs"), Rij("Files"), Rij("Bytes"));
}

static Uitkomst Motor(string bron, string doel, bool spiegelen, int threads)
{
    var i = new Instellingen
    {
        Spiegelen = spiegelen, Pogingen = 0, WachtSec = 0, Threads = threads, MapDatum = true, ToonBestanden = false,
        SlaBestanden = new() { "*.tmp" }, SlaMappen = new() { "Cache" },
    };
    var m = new Kopieermotor(bron, doel, i, (t, s) => { if (s == Regelsoort.Fout) Console.WriteLine("    motor: " + t); }, CancellationToken.None);
    m.Uitvoeren();
    long[] R(Telling t) => new[] { t.Totaal, t.Gekopieerd, t.Overgeslagen, t.Verschil, t.Mislukt, t.Extra };
    return new Uitkomst(R(m.Mappen), R(m.Bestanden), R(m.Bytes));
}

// ---------------------------------------------------------------- vergelijken
static List<string> VergelijkMappen(string a, string b)
{
    var v = new List<string>();
    var lijstA = Inventaris(a); var lijstB = Inventaris(b);
    foreach (var k in lijstA.Keys.Union(lijstB.Keys).OrderBy(x => x))
    {
        lijstA.TryGetValue(k, out var x); lijstB.TryGetValue(k, out var y);
        if (x is null) v.Add($"{k}: alleen bij de motor ({y})");
        else if (y is null) v.Add($"{k}: alleen bij robocopy ({x})");
        else if (x != y) v.Add($"{k}: robocopy {x} | motor {y}");
    }
    return v;
}

static Dictionary<string, string> Inventaris(string map)
{
    var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    const FileAttributes negeer = FileAttributes.Archive | FileAttributes.NotContentIndexed;
    foreach (var p in Directory.GetFileSystemEntries(map, "*", new EnumerationOptions { RecurseSubdirectories = true, AttributesToSkip = 0 }))
    {
        var rel = Path.GetRelativePath(map, p);
        var info = (FileSystemInfo)(Directory.Exists(p) ? new DirectoryInfo(p) : new FileInfo(p));
        var kenmerken = info.Attributes & ~negeer;
        if (info is FileInfo f)
        {
            using var s = File.OpenRead(p);
            var hash = Convert.ToHexString(SHA256.HashData(s))[..12];
            d[rel] = $"bestand {f.Length} b, {f.LastWriteTimeUtc:yyyy-MM-dd HH:mm:ss}, {kenmerken}, {hash}";
        }
        else d[rel] = $"map, {info.LastWriteTimeUtc:yyyy-MM-dd HH:mm:ss}, {kenmerken}";
    }
    return d;
}

record Uitkomst(long[] Mappen, long[] Bestanden, long[] Bytes);
