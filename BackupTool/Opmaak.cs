using System.Globalization;

namespace BackupTool;

// Getallen en groottes op z'n Nederlands: 1.234 bestanden, 1,5 GB
public static class Opmaak
{
    public static readonly CultureInfo NL = CultureInfo.GetCultureInfo("nl-NL");

    public static string Aantal(long n) => n.ToString("N0", NL);

    public static string Bestanden(long n) => $"{Aantal(n)} {(n == 1 ? "bestand" : "bestanden")}";

    public static string Mappen(long n) => $"{Aantal(n)} {(n == 1 ? "map" : "mappen")}";

    // korte grootte voor regels en tabellen
    public static string Kort(double b)
    {
        if (b >= 1L << 40) return (b / (1L << 40)).ToString("N2", NL) + " TB";
        if (b >= 1 << 30) return (b / (1 << 30)).ToString("N1", NL) + " GB";
        if (b >= 1 << 20) return (b / (1 << 20)).ToString("N0", NL) + " MB";
        if (b >= 1 << 10) return (b / (1 << 10)).ToString("N0", NL) + " kB";
        return $"{(long)b} bytes";
    }

    // grootte van een schijf (vrij / totaal)
    public static string Schijf(long b)
    {
        if (b >= 1L << 40) return (b / (double)(1L << 40)).ToString("N2", NL) + " TB";
        if (b >= 1 << 30) return (b / (double)(1 << 30)).ToString("N0", NL) + " GB";
        return (b / (double)(1 << 20)).ToString("N0", NL) + " MB";
    }

    public static string Tijd(TimeSpan t) => t.ToString(@"hh\:mm\:ss");

    public static string CodeTekst(int c)
    {
        if (c >= 16) return "ernstige fout, niets gekopieerd";
        var t = new List<string>();
        if ((c & 8) != 0) t.Add("sommige bestanden mislukt");
        if ((c & 1) != 0) t.Add("bestanden gekopieerd");
        if ((c & 2) != 0) t.Add("extra bestanden op doel");
        if ((c & 4) != 0) t.Add("verschillen gevonden");
        return t.Count == 0 ? "niets te doen, alles was al bijgewerkt" : string.Join(", ", t);
    }
}
