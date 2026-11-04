using System.Drawing.Drawing2D;

namespace BackupTool;

// Kleuren, lettertypen en bouwstenen van het venster (zelfde uiterlijk als de PowerShell-versie)
public static class Stijl
{
    static Color K(string hex) => ColorTranslator.FromHtml(hex);

    public static readonly Color Achter = K("#F3F5F8"), Kaart = K("#FFFFFF"), Kop = K("#1E2532"), KopTekst = K("#AEB7C6"),
        Lijn = K("#E5E8ED"), Tekst = K("#1F2937"), Zacht = K("#6B7280"), Om = K("#F1F4F9"),
        Blauw = K("#2D6FD6"), BlauwDonker = K("#2560BF"), BlauwLicht = K("#E8F0FC"), BlauwUit = K("#9DB8E6"), Selectie = K("#D3E3FB"),
        Rood = K("#B42323"), Groen = K("#1E7B3C"), Oranje = K("#A15C00"), Grijs = K("#7A828E"), KnopRand = K("#CBD2DC"),
        Uitvoer = K("#F8F9FB"), Spoor = K("#E6EAF0"),
        BalkGroen = K("#2EAD5B"), BalkOranje = K("#D9861C"), BalkRood = K("#D64545"), BalkGrijs = K("#9AA3AE"),
        KnopRood = K("#D13B3B"), KnopRoodDonker = K("#B42F2F");

    public static readonly Font Gewoon = new("Segoe UI", 10), Vet = new("Segoe UI Semibold", 10), Klein = new("Segoe UI", 9),
        Titel = new("Segoe UI Semibold", 17), Kolom = new("Segoe UI Semibold", 9), KaartKop = new("Segoe UI Semibold", 11),
        Mono = new("Consolas", 9);

    public static float Schaal = 1f;
    public static int P(double n) => (int)Math.Round(n * Schaal);

    public static readonly string Stip = "●";
    public static readonly string Punt = " · ";

    public static Button Knop(string tekst, int breedte, bool primair = false)
    {
        var b = new Button
        {
            Text = tekst, Size = new Size(P(breedte), P(34)), FlatStyle = FlatStyle.Flat, Cursor = Cursors.Hand, Font = Vet,
            BackColor = primair ? Blauw : Kaart, ForeColor = primair ? Color.White : Tekst, UseVisualStyleBackColor = false,
        };
        if (primair) b.FlatAppearance.BorderSize = 0; else b.FlatAppearance.BorderColor = KnopRand;
        b.FlatAppearance.MouseOverBackColor = primair ? BlauwDonker : BlauwLicht;
        return b;
    }

    public static Label Lbl(string tekst, int x, int y, Font font, Control ouder)
    {
        var l = new Label { Text = tekst, AutoSize = true, Font = font, ForeColor = Tekst, Location = new Point(P(x), P(y)) };
        ouder.Controls.Add(l);
        return l;
    }

    // kaart met dunne rand; $boven = ruimte erboven. Geeft het binnenste paneel terug.
    public static Panel KaartPaneel(Control ouder, DockStyle dock, int hoogte, int boven)
    {
        var w = new Panel { Dock = dock, Padding = new Padding(0, P(boven), 0, 0), BackColor = Achter };
        if (hoogte > 0) w.Height = P(hoogte);
        var k = new Panel { Dock = DockStyle.Fill, BackColor = Kaart };
        k.Paint += (s, e) => { using var pen = new Pen(Lijn); e.Graphics.DrawRectangle(pen, 0, 0, k.Width - 1, k.Height - 1); };
        w.Controls.Add(k);
        ouder.Controls.Add(w);
        return k;
    }

    public static GraphicsPath Afgerond(Rectangle r, int straal)
    {
        var p = new GraphicsPath();
        int d = Math.Min(straal * 2, r.Height);
        p.AddArc(r.X, r.Y, d, d, 180, 90); p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90); p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        p.CloseFigure();
        return p;
    }

    public static void DubbelBuffer(Control c) =>
        typeof(Control).GetProperty("DoubleBuffered", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .SetValue(c, true);

    public static Icon? AppIcoon()
    {
        try
        {
            using var s = typeof(Stijl).Assembly.GetManifestResourceStream("BackupTool.backup.ico");
            return s is null ? null : new Icon(s);
        }
        catch { return null; }
    }

    // test: een venster als plaatje bewaren
    public static void Schermafdruk(Form f, string pad)
    {
        using var bmp = new Bitmap(f.Width, f.Height);
        using (var g = Graphics.FromImage(bmp)) g.CopyFromScreen(f.Location, Point.Empty, f.Size);
        bmp.Save(pad, System.Drawing.Imaging.ImageFormat.Png);
    }

    public static bool IsSchijf(string pad) => System.Text.RegularExpressions.Regex.IsMatch(pad.Trim(), @"^[A-Za-z]:\\?$");

    // paden aan elkaar plakken zonder te struikelen over een niet-aangesloten schijf
    public static string Pad(string a, string b) => a.TrimEnd('\\') + "\\" + b.TrimStart('\\');
}
