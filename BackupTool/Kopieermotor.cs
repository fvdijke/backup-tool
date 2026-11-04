using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;

namespace BackupTool;

// Eigen kopieermotor: doet wat robocopy deed met /E /XO (bijwerken) of /MIR (spiegelen), zonder robocopy.
//   1. vergelijken: bron en doel map voor map doorlopen en bepalen wat er moet gebeuren
//   2. uitvoeren:   extra's wissen (alleen bij spiegelen), nieuwe mappen maken, bestanden kopiëren,
//                   en tot slot de datums van de mappen zetten
// Een bestand is gelijk als grootte en wijzigingsdatum gelijk zijn (zoals robocopy). Bij bijwerken wordt een
// bestand dat op het doel nieuwer is niet overschreven (/XO); bij spiegelen wel.

public enum Regelsoort { Gewoon, Fout, Kop, Goed }

public sealed class Instellingen
{
    public bool Spiegelen;
    public int Pogingen = 1;
    public int WachtSec = 1;
    public int Threads = 1;
    public bool MapDatum = true;          // /DCOPY:DAT
    public bool Fft;                      // 2 s tijdverschil toestaan (FAT/exFAT/NAS)
    public bool Dst;                      // precies 1 uur verschil toestaan (zomertijd)
    public bool Herstartbaar;             // /Z
    public bool VerborgenOverslaan;       // /XA:SH
    public bool Proef;                    // /L: alleen laten zien wat er zou gebeuren
    public bool ToonBestanden = true;     // elke bestands- en mapregel melden (anders alleen fouten)
    public bool Controleren;              // na elk gekopieerd bestand de SHA-256-hash van kopie en origineel vergelijken
    // controle wijkt af: de motor pauzeert zichzelf en meldt het bestand (null = niet pauzeren, gewoon opnieuw proberen)
    public Action<string>? BijAfwijking;
    public string? TestAfwijking;         // test: bij een bestand dat hierop eindigt één keer een afwijking nabootsen
    // bestand in gebruik: geeft het pad van hetzelfde bestand in een schaduwkopie (momentopname), of null
    public Func<string, string?>? Schaduwpad;
    public List<string> SlaBestanden = new();   // /XF: patronen (*.tmp) of volledige paden
    public List<string> SlaMappen = new();      // /XD: mapnamen (met * en ?) of volledige paden
}

// Eén regel uit de samenvatting: zoals de kolommen van robocopy
public sealed class Telling
{
    public long Totaal, Gekopieerd, Overgeslagen, Verschil, Mislukt, Extra;
}

public sealed class Kopieermotor
{
    readonly string _bron, _doel;
    readonly Instellingen _i;
    readonly Action<string, Regelsoort> _meld;
    readonly CancellationToken _stop;
    readonly ManualResetEventSlim _door = new(true);   // uit = gepauzeerd
    readonly Regex[] _slaBestNaam, _slaMapNaam;
    readonly HashSet<string> _slaBestPad, _slaMapPad;

    public readonly Telling Mappen = new(), Bestanden = new(), Bytes = new();
    public long Verwijderd;                // bij spiegelen gewiste bestanden en mappen
    long _gecontroleerd, _viaSchaduw;
    public long Gecontroleerd => Interlocked.Read(ref _gecontroleerd);   // kopieën die gelijk bleken aan het origineel
    public long ViaSchaduw => Interlocked.Read(ref _viaSchaduw);         // bestanden die in gebruik waren, uit de schaduwkopie
    public volatile int Fase;              // 0 = vergelijken, 1 = uitvoeren, 2 = klaar
    public volatile string Huidig = "";    // waar de motor nu mee bezig is (voor in het venster)

    long _gescand, _teKopieren, _gedaan;
    public long GescandeMappen => Interlocked.Read(ref _gescand);
    public long TeKopierenBytes => Interlocked.Read(ref _teKopieren);
    public long GekopieerdeBytes => Interlocked.Read(ref _gedaan);
    public bool Gepauzeerd => !_door.IsSet;
    public void Pauzeer(bool aan) { if (aan) _door.Reset(); else _door.Set(); }

    sealed record Kopie(string Bron, string Doel, string Rel, long Grootte, string Klasse);
    readonly List<Kopie> _kopieen = new();
    readonly List<string> _nieuweMappen = new();                   // doelmappen die gemaakt moeten worden
    readonly List<(string Bron, string Doel, bool Wortel)> _mappen = new();   // voor de datums aan het eind
    readonly List<(string Pad, string Rel, bool IsMap)> _wissen = new();

    public Kopieermotor(string bron, string doel, Instellingen i, Action<string, Regelsoort> meld, CancellationToken stop)
    {
        _bron = Normaal(bron); _doel = Normaal(doel); _i = i; _meld = meld; _stop = stop;
        static bool IsPad(string s) => s.Contains('\\');
        _slaBestNaam = i.SlaBestanden.Where(s => !IsPad(s)).Select(Patroon).ToArray();
        _slaMapNaam = i.SlaMappen.Where(s => !IsPad(s)).Select(Patroon).ToArray();
        _slaBestPad = new(i.SlaBestanden.Where(IsPad).Select(Normaal), StringComparer.OrdinalIgnoreCase);
        _slaMapPad = new(i.SlaMappen.Where(IsPad).Select(Normaal), StringComparer.OrdinalIgnoreCase);
    }

    // "G:" -> "G:\", "E:\Boeken\" -> "E:\Boeken"
    public static string Normaal(string p)
    {
        p = p.Trim().Trim('"');
        if (p.Length == 2 && p[1] == ':') return p + "\\";
        return p.Length > 3 ? p.TrimEnd('\\') : p;
    }

    static Regex Patroon(string s) =>
        new("^" + Regex.Escape(s).Replace(@"\*", ".*").Replace(@"\?", ".") + "$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    bool SlaBestandOver(string naam, string bronPad) =>
        _slaBestNaam.Any(r => r.IsMatch(naam)) || _slaBestPad.Contains(bronPad);

    bool SlaMapOver(string naam, string bronPad) =>
        _slaMapNaam.Any(r => r.IsMatch(naam)) || _slaMapPad.Contains(bronPad);

    void Meld(string tekst, Regelsoort soort = Regelsoort.Gewoon) => _meld(tekst, soort);
    void MeldRegel(string klasse, long grootte, string rel)
    {
        if (_i.ToonBestanden) Meld($"  {klasse,-12}{(grootte >= 0 ? Opmaak.Kort(grootte) : ""),12}   {rel}");
    }

    // Robocopy-achtige uitkomst: 1 = gekopieerd, 2 = extra's op doel, 4 = verschillen, 8 = mislukt, 16 = ernstige fout
    public int Uitvoeren()
    {
        try
        {
            if (!Directory.Exists(_bron)) { Meld($"FOUT: bron niet gevonden ({_bron})", Regelsoort.Fout); return 16; }
            Vergelijk();
            Fase = 1;
            Uitvoer();
            if (_i.MapDatum && !_i.Proef) ZetMapDatums();
        }
        catch (OperationCanceledException) { Meld("Gestopt.", Regelsoort.Kop); }
        finally { Fase = 2; Huidig = ""; }
        int code = 0;
        if (Bestanden.Gekopieerd > 0 || Mappen.Gekopieerd > 0) code |= 1;
        if (Bestanden.Extra > 0 || Mappen.Extra > 0) code |= 2;
        if (Bestanden.Verschil > 0 || Mappen.Verschil > 0) code |= 4;
        if (Bestanden.Mislukt > 0 || Mappen.Mislukt > 0) code |= 8;
        return code;
    }

    void Wacht()
    {
        _stop.ThrowIfCancellationRequested();
        if (!_door.IsSet) _door.Wait(_stop);
    }

    // ---------------------------------------------------------------- 1. vergelijken
    void Vergelijk()
    {
        var stapel = new Stack<string>();
        stapel.Push("");
        var volgorde = new List<string>();
        while (stapel.Count > 0)
        {
            Wacht();
            var rel = stapel.Pop();
            var bronMap = Path.Combine(_bron, rel);
            var doelMap = Path.Combine(_doel, rel);
            Huidig = bronMap;
            Interlocked.Increment(ref _gescand);
            Mappen.Totaal++;

            bool doelBestaat = Directory.Exists(doelMap);
            if (doelBestaat) Mappen.Overgeslagen++;
            else
            {
                Mappen.Gekopieerd++; _nieuweMappen.Add(doelMap);
                MeldRegel("Nieuwe map", -1, rel.Length == 0 ? _bron : rel + "\\");
            }
            _mappen.Add((bronMap, doelMap, rel.Length == 0));

            FileSystemInfo[] bronItems;
            try { bronItems = Lijst(bronMap); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Mappen.Mislukt++;
                Meld($"FOUT: map niet te lezen ({Win32Tekst(ex)}): {bronMap}", Regelsoort.Fout);
                continue;
            }
            var doelItems = doelBestaat ? LijstVeilig(doelMap) : Array.Empty<FileSystemInfo>();
            var opDoel = new Dictionary<string, FileSystemInfo>(StringComparer.OrdinalIgnoreCase);
            foreach (var d in doelItems) opDoel[d.Name] = d;
            var inBron = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            var submappen = new List<string>();
            foreach (var item in bronItems)
            {
                var itemRel = rel.Length == 0 ? item.Name : rel + "\\" + item.Name;
                if (item is DirectoryInfo m)
                {
                    // uitgesloten mappen tellen mee als overgeslagen (zoals robocopy) en blijven ook op het doel staan
                    if (SlaMapOver(m.Name, m.FullName)) { inBron.Add(m.Name); Mappen.Totaal++; Mappen.Overgeslagen++; continue; }
                    if ((m.Attributes & FileAttributes.ReparsePoint) != 0)
                    {
                        // koppelingen (junctions) niet volgen: dat kan een eindeloze lus geven (zoals robocopy /XJ)
                        inBron.Add(m.Name); Mappen.Totaal++; Mappen.Overgeslagen++;
                        MeldRegel("Koppeling", -1, itemRel + "\\  (overgeslagen)");
                        continue;
                    }
                    inBron.Add(m.Name);
                    if (opDoel.TryGetValue(m.Name, out var dv) && dv is FileInfo)
                    {
                        Mappen.Verschil++;
                        Meld($"Verschil: op het doel is {itemRel} een bestand, in de bron een map", Regelsoort.Fout);
                        continue;
                    }
                    submappen.Add(itemRel);
                }
                else if (item is FileInfo b)
                {
                    inBron.Add(b.Name);
                    Bestanden.Totaal++; Bytes.Totaal += b.Length;
                    if (SlaBestandOver(b.Name, b.FullName)
                        || (_i.VerborgenOverslaan && (b.Attributes & (FileAttributes.Hidden | FileAttributes.System)) != 0))
                    { Bestanden.Overgeslagen++; Bytes.Overgeslagen += b.Length; continue; }   // uitgesloten: telt als overgeslagen
                    string? klasse;
                    if (!opDoel.TryGetValue(b.Name, out var d)) klasse = "Nieuw";
                    else if (d is DirectoryInfo)
                    {
                        Bestanden.Verschil++; Bytes.Verschil += b.Length;
                        Meld($"Verschil: op het doel is {itemRel} een map, in de bron een bestand", Regelsoort.Fout);
                        continue;
                    }
                    else klasse = Vergelijk(b, (FileInfo)d);

                    if (klasse is null) { Bestanden.Overgeslagen++; Bytes.Overgeslagen += b.Length; continue; }
                    _kopieen.Add(new Kopie(b.FullName, Path.Combine(doelMap, b.Name), itemRel, b.Length, klasse));
                    _teKopieren += b.Length;
                }
            }

            // extra's: staat op het doel maar niet (meer) in de bron. Ze tellen altijd mee (zoals bij robocopy);
            // bij spiegelen worden ze gewist, behalve als ze onder een uitsluiting vallen.
            foreach (var d in doelItems)
            {
                if (inBron.Contains(d.Name)) continue;
                var itemRel = rel.Length == 0 ? d.Name : rel + "\\" + d.Name;
                var bronPad = Path.Combine(bronMap, d.Name);
                if (d is DirectoryInfo dm)
                {
                    Mappen.Extra++;
                    MeldRegel("Extra map", -1, itemRel + "\\");
                    if (_i.Spiegelen && !SlaMapOver(d.Name, bronPad))
                    {
                        _wissen.Add((d.FullName, itemRel, true));
                        TelExtraInhoud(dm);                          // robocopy telt bij spiegelen ook alles erin
                    }
                }
                else if (d is FileInfo f)
                {
                    Bestanden.Extra++; Bytes.Extra += f.Length;
                    MeldRegel("Extra", f.Length, itemRel);
                    if (_i.Spiegelen && !SlaBestandOver(d.Name, bronPad)) _wissen.Add((d.FullName, itemRel, false));
                }
            }

            // in omgekeerde volgorde op de stapel, zodat de mappen op alfabet worden doorlopen
            for (int k = submappen.Count - 1; k >= 0; k--) stapel.Push(submappen[k]);
        }
    }

    void TelExtraInhoud(DirectoryInfo map)
    {
        if ((map.Attributes & FileAttributes.ReparsePoint) != 0) return;
        foreach (var i in LijstVeilig(map.FullName))
        {
            if (i is DirectoryInfo m) { Mappen.Extra++; TelExtraInhoud(m); }
            else if (i is FileInfo f) { Bestanden.Extra++; Bytes.Extra += f.Length; }
        }
    }

    // null = gelijk of overslaan; anders de reden om te kopiëren
    string? Vergelijk(FileInfo bron, FileInfo doel)
    {
        long verschil = bron.LastWriteTimeUtc.Ticks - doel.LastWriteTimeUtc.Ticks;
        long marge = _i.Fft ? TimeSpan.TicksPerSecond * 2 : 0;
        bool tijdGelijk = Math.Abs(verschil) <= marge
            || (_i.Dst && Math.Abs(Math.Abs(verschil) - TimeSpan.TicksPerHour) <= Math.Max(marge, TimeSpan.TicksPerSecond * 2));
        if (tijdGelijk) return bron.Length == doel.Length ? null : "Gewijzigd";
        if (verschil > 0) return "Nieuwer";
        return _i.Spiegelen ? "Ouder" : null;                 // bijwerken (/XO): nieuwere versie op het doel laten staan
    }

    static readonly EnumerationOptions AlleItems = new()
    {
        AttributesToSkip = 0, IgnoreInaccessible = false, RecurseSubdirectories = false, ReturnSpecialDirectories = false,
    };

    static FileSystemInfo[] Lijst(string map) =>
        new DirectoryInfo(map).EnumerateFileSystemInfos("*", AlleItems).OrderBy(i => i.Name, StringComparer.OrdinalIgnoreCase).ToArray();

    static FileSystemInfo[] LijstVeilig(string map)
    {
        try { return Lijst(map); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return Array.Empty<FileSystemInfo>(); }
    }

    // ---------------------------------------------------------------- 2. uitvoeren
    void Uitvoer()
    {
        // eerst de extra's wissen (alleen bij spiegelen), dan nieuwe mappen maken, dan kopiëren
        foreach (var (pad, rel, isMap) in _wissen)
        {
            Wacht();
            Huidig = pad;
            if (_i.Proef) { Verwijderd++; continue; }
            try
            {
                if (isMap) WisMap(pad); else WisBestand(pad);
                Verwijderd++;
                MeldRegel(isMap ? "Gewist (map)" : "Gewist", -1, rel);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Meld($"FOUT: niet te wissen ({Win32Tekst(ex)}): {pad}", Regelsoort.Fout);
                if (isMap) Mappen.Mislukt++; else Bestanden.Mislukt++;
            }
        }

        if (!_i.Proef)
            foreach (var m in _nieuweMappen)
            {
                Wacht();
                try { Directory.CreateDirectory(m); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    Mappen.Mislukt++; Mappen.Gekopieerd--;
                    Meld($"FOUT: map niet te maken ({Win32Tekst(ex)}): {m}", Regelsoort.Fout);
                }
            }

        if (_i.Threads > 1)
            Parallel.ForEach(_kopieen, new ParallelOptions { MaxDegreeOfParallelism = _i.Threads, CancellationToken = _stop }, KopieerEen);
        else
            foreach (var k in _kopieen) KopieerEen(k);
    }

    void KopieerEen(Kopie k)
    {
        Wacht();
        Huidig = k.Rel;
        if (_i.Proef)
        {
            MeldRegel(k.Klasse, k.Grootte, k.Rel);
            Tel(true, k.Grootte); Interlocked.Add(ref _gedaan, k.Grootte);
            return;
        }
        string bron = k.Bron;
        bool viaSchaduw = false;
        for (int poging = 0; ; poging++)
        {
            int fout = Kopieer(k, bron);
            // in gebruik (32 = gedeeld, 33 = vergrendeld): dan uit een schaduwkopie van de schijf proberen
            if (fout is 32 or 33 && !viaSchaduw && _i.Schaduwpad?.Invoke(k.Bron) is { } schaduw)
            {
                bron = schaduw; viaSchaduw = true;
                Meld($"  In gebruik, kopie uit de schaduwkopie: {k.Rel}");
                fout = Kopieer(k, bron);
            }
            if (fout == 0 && _i.Controleren)
            {
                // meteen na het kopiëren van dit bestand: hash van het origineel en van de kopie vergelijken
                Huidig = "controleren: " + k.Rel;
                var (gelijk, hBron, hKopie) = Controleer(bron, k.Doel, k.Rel);
                if (gelijk) Interlocked.Increment(ref _gecontroleerd);
                else
                {
                    fout = FoutControle;
                    Interlocked.Add(ref _gedaan, -k.Grootte);             // wordt opnieuw gekopieerd
                    Meld($"CONTROLE WIJKT AF: {k.Rel}  (SHA-256 origineel {hBron}, kopie {hKopie})", Regelsoort.Fout);
                    if (_i.BijAfwijking is { } melden)
                    {
                        // pauzeren tot de gebruiker op Hervatten klikt; daarna wordt het bestand opnieuw gekopieerd
                        Pauzeer(true);
                        melden(k.Rel);
                        Wacht();
                        poging--;                                         // die nieuwe kopie telt niet als poging
                        continue;
                    }
                }
            }
            if (fout == 0)
            {
                if (viaSchaduw) Interlocked.Increment(ref _viaSchaduw);
                MeldRegel(k.Klasse, k.Grootte, k.Rel);
                Tel(true, k.Grootte);
                return;
            }
            if (fout == FoutAfgebroken) throw new OperationCanceledException(_stop);
            var tekst = fout == FoutControle ? "de kopie wijkt af van het origineel" : new Win32Exception(fout).Message.ReplaceLineEndings(" ").Trim();
            if (fout is 32 or 33 && _i.Schaduwpad is null) tekst += "; tip: zet 'Geopende bestanden kopiëren' aan";
            if (poging >= _i.Pogingen)
            {
                Meld($"FOUT {fout} ({tekst}): {k.Rel}", Regelsoort.Fout);
                Tel(false, k.Grootte);
                return;
            }
            Meld($"Opnieuw proberen ({poging + 1}/{_i.Pogingen}) over {_i.WachtSec} s - fout {fout} ({tekst}): {k.Rel}");
            if (_stop.WaitHandle.WaitOne(TimeSpan.FromSeconds(_i.WachtSec))) throw new OperationCanceledException(_stop);
            Wacht();
        }
    }

    readonly object _telSlot = new();
    void Tel(bool gelukt, long grootte)
    {
        lock (_telSlot)
        {
            if (gelukt) { Bestanden.Gekopieerd++; Bytes.Gekopieerd += grootte; }
            else { Bestanden.Mislukt++; Bytes.Mislukt += grootte; }
        }
    }

    // ---- het kopiëren zelf: CopyFileEx (zoals Windows Verkenner), met voortgang per bestand en pauze/stop
    const int FoutAfgebroken = 1235;   // ERROR_REQUEST_ABORTED
    const int FoutControle = -2;       // eigen code: controle na het kopiëren mislukt
    const uint COPY_FILE_RESTARTABLE = 0x2;

    delegate uint VoortgangRoutine(long totaal, long gedaan, long stroomGrootte, long stroomGedaan, uint stroom,
        uint reden, IntPtr bron, IntPtr doel, IntPtr data);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern bool CopyFileEx(string bron, string doel, VoortgangRoutine routine, IntPtr data, ref int annuleer, uint vlaggen);

    // SHA-256-hash van origineel en kopie vergelijken (in blokken van 1 MB; pauze en stoppen werken ook hier).
    // Geeft de twee hashes terug, of een foutmelding als een van beide niet te lezen is.
    (bool Gelijk, string Bron, string Kopie) Controleer(string a, string b, string rel)
    {
        try
        {
            var ha = Hash(a); var hb = Hash(b);
            // test: een afwijking nabootsen (alleen de eerste keer, zodat opnieuw kopiëren daarna lukt)
            if (_i.TestAfwijking is { } t && rel.EndsWith(t, StringComparison.OrdinalIgnoreCase) && Interlocked.Exchange(ref _testGedaan, 1) == 0)
                hb = new string('0', 64);
            return (ha == hb, ha, hb);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return (false, "niet te lezen", ex.Message); }
    }
    int _testGedaan;

    string Hash(string pad)
    {
        using var f = new FileStream(Lang(pad), FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 1 << 16, FileOptions.SequentialScan);
        using var h = System.Security.Cryptography.IncrementalHash.CreateHash(System.Security.Cryptography.HashAlgorithmName.SHA256);
        var buf = new byte[1 << 20];
        int n;
        while ((n = f.Read(buf, 0, buf.Length)) > 0) { Wacht(); h.AppendData(buf, 0, n); }
        return Convert.ToHexString(h.GetHashAndReset());
    }

    int Kopieer(Kopie k, string bronPad)
    {
        try
        {
            // een bestaand doel dat alleen-lezen of verborgen is, kan CopyFileEx niet overschrijven
            if (File.Exists(k.Doel)) File.SetAttributes(k.Doel, FileAttributes.Normal);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return ex.HResult & 0xFFFF; }

        long vorige = 0;
        VoortgangRoutine routine = (totaal, gedaan, _, _, _, _, _, _, _) =>
        {
            Interlocked.Add(ref _gedaan, gedaan - vorige);
            vorige = gedaan;
            if (_stop.IsCancellationRequested) return 1;           // PROGRESS_CANCEL
            if (!_door.IsSet) { try { _door.Wait(_stop); } catch (OperationCanceledException) { return 1; } }
            return 0;                                              // PROGRESS_CONTINUE
        };
        int annuleer = 0;
        bool ok = CopyFileEx(Lang(bronPad), Lang(k.Doel), routine, IntPtr.Zero, ref annuleer, _i.Herstartbaar ? COPY_FILE_RESTARTABLE : 0);
        int fout = ok ? 0 : Marshal.GetLastWin32Error();
        GC.KeepAlive(routine);
        if (!ok) { Interlocked.Add(ref _gedaan, -vorige); return fout == 0 ? 1 : fout; }

        // datums gelijk aan de bron (CopyFileEx neemt de wijzigingsdatum al mee; de aanmaakdatum niet altijd)
        try
        {
            var b = new FileInfo(bronPad);
            File.SetCreationTimeUtc(k.Doel, b.CreationTimeUtc);
            File.SetLastWriteTimeUtc(k.Doel, b.LastWriteTimeUtc);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        return 0;
    }

    static string Lang(string p) =>
        p.StartsWith(@"\\?\") ? p : p.StartsWith(@"\\") ? @"\\?\UNC\" + p[2..] : @"\\?\" + p;

    static void WisBestand(string pad)
    {
        File.SetAttributes(pad, FileAttributes.Normal);
        File.Delete(pad);
    }

    static void WisMap(string pad)
    {
        var info = new DirectoryInfo(pad);
        if ((info.Attributes & FileAttributes.ReparsePoint) == 0)      // inhoud van een koppeling niet aanraken
        {
            foreach (var b in info.EnumerateFiles("*", AlleItems)) WisBestand(b.FullName);
            foreach (var m in info.EnumerateDirectories("*", AlleItems)) WisMap(m.FullName);
        }
        info.Attributes = FileAttributes.Directory;
        info.Delete(false);
    }

    // ---- 3. datums (en kenmerken) van de mappen: pas aan het eind, want kopiëren verandert de map-datum
    void ZetMapDatums()
    {
        for (int k = _mappen.Count - 1; k >= 0; k--)
        {
            var (bron, doel, wortel) = _mappen[k];
            if (!Directory.Exists(doel)) continue;
            try
            {
                var b = new DirectoryInfo(bron);
                var d = new DirectoryInfo(doel);
                // de kenmerken van de bron-hoofdmap niet overnemen: een schijf-root is verborgen+systeem
                if (!wortel) d.Attributes = b.Attributes & ~FileAttributes.ReparsePoint;
                d.CreationTimeUtc = b.CreationTimeUtc;
                d.LastWriteTimeUtc = b.LastWriteTimeUtc;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException) { }
        }
    }

    static string Win32Tekst(Exception ex)
    {
        int code = ex.HResult & 0xFFFF;
        return code != 0 ? $"{code}: {new Win32Exception(code).Message}" : ex.Message;
    }
}
