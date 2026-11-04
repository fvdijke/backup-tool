using static BackupTool.Stijl;

namespace BackupTool;

// ================================================================ submappen kiezen (naast de lijst)
// Mappenboom met vinkjes voor de geselecteerde bron. Er kan alleen worden overgeslagen, niet gekozen: elke uitgevinkte
// map wordt een uitsluiting (met alles erin). Submappen worden pas geladen als je een map openklapt. Elke klik wordt
// meteen doorgegeven.
public sealed class SubmappenPaneel : Panel
{
    readonly Boom _boom = new();
    readonly Label _kop, _melding;
    readonly Button _bAan, _bUit;
    string _basis = "";
    HashSet<string> _uit = new(StringComparer.OrdinalIgnoreCase);
    Action<List<string>>? _gewijzigd;
    TreeNode? _wortel;
    static readonly string[] GeenMappen = { "$RECYCLE.BIN", "System Volume Information" };
    const string Leeg = "*";

    // TreeView met vinkjes: een dubbelklik op een vinkje laat Windows het vinkje anders tonen dan het is
    sealed class Boom : TreeView
    {
        protected override void WndProc(ref Message m)
        {
            if (m.Msg == 0x203) m.Msg = 0x201;   // WM_LBUTTONDBLCLK -> WM_LBUTTONDOWN
            base.WndProc(ref m);
        }
    }

    public SubmappenPaneel()
    {
        BackColor = Kaart;
        Padding = new Padding(P(12), 0, P(12), P(10));
        _boom.Dock = DockStyle.Fill; _boom.CheckBoxes = true; _boom.BorderStyle = BorderStyle.FixedSingle; _boom.Font = Gewoon;
        _boom.ItemHeight = P(24); _boom.HideSelection = false; _boom.BackColor = Kaart; _boom.ForeColor = Tekst;
        _melding = new Label { Dock = DockStyle.Fill, ForeColor = Zacht, TextAlign = ContentAlignment.MiddleCenter, Visible = false };
        var knoppen = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = P(44), Padding = new Padding(0, P(6), 0, 0) };
        _bAan = Knop("Submappen aan", 135); _bUit = Knop("Submappen uit", 135);
        _bAan.Margin = new Padding(0, 0, P(6), 0);
        knoppen.Controls.AddRange(new Control[] { _bAan, _bUit });
        _kop = new Label
        {
            Dock = DockStyle.Top, Height = P(42), Font = KaartKop, ForeColor = Tekst, TextAlign = ContentAlignment.MiddleLeft,
            AutoEllipsis = true, Text = "Submappen",
        };
        Controls.AddRange(new Control[] { _boom, _melding, knoppen, _kop });

        _boom.BeforeExpand += (s, e) => { Cursor = Cursors.WaitCursor; _boom.BeginUpdate(); Laad(e.Node!); _boom.EndUpdate(); Cursor = Cursors.Default; };
        _boom.BeforeCheck += (s, e) => { if (e.Node!.Parent is null && e.Node.Checked) e.Cancel = true; };   // bron zelf blijft aan
        _boom.AfterCheck += (s, e) =>
        {
            if (e.Action == TreeViewAction.Unknown) return;               // door de code gezet, niet door een klik
            ZetKinderen(e.Node!, e.Node!.Checked);
            // een map aanvinken onder een uitgevinkte map: de mappen erboven moeten dan ook mee
            if (e.Node.Checked) for (var o = e.Node.Parent; o is not null; o = o.Parent) o.Checked = true;
            Meld();
        };
        _bAan.Click += (s, e) => { if (_wortel is not null) { ZetKinderen(_wortel, true); Meld(); } };
        _bUit.Click += (s, e) => { if (_wortel is not null) { ZetKinderen(_wortel, false); Meld(); } };
        Toon(null, "", Array.Empty<string>(), null);
    }

    // de boom van een bron laten zien; null = niets geselecteerd
    public void Toon(string? bron, string naam, IEnumerable<string> overslaan, Action<List<string>>? gewijzigd)
    {
        _gewijzigd = null;
        _boom.BeginUpdate();
        _boom.Nodes.Clear(); _wortel = null;
        _kop.Text = naam.Length > 0 ? $"Submappen van {naam}" : "Submappen";
        bool ok = bron is not null && Directory.Exists(bron);
        _boom.Visible = ok; _bAan.Enabled = _bUit.Enabled = ok; _melding.Visible = !ok;
        _melding.Text = bron is null ? "Kies links een bron om de submappen te zien." : $"Bron niet gevonden:\n{bron}";
        if (ok)
        {
            _basis = bron!.TrimEnd('\\');
            _uit = new HashSet<string>(overslaan, StringComparer.OrdinalIgnoreCase);
            _wortel = Knoop(bron!, bron!, true);
            _boom.Nodes.Add(_wortel);
            Laad(_wortel); _wortel.Expand();
            // bestaande keuzes zichtbaar maken: de mappen boven elke uitgevinkte map openklappen
            foreach (var u in _uit.ToList()) Zoek(u, true);
            _boom.SelectedNode = _wortel;
        }
        _boom.EndUpdate();
        _gewijzigd = gewijzigd;
    }

    // test: een map (pad ten opzichte van de bron) uitvinken alsof erop geklikt is
    public void VinkUit(string rel)
    {
        if (Zoek(rel, true) is { } k) { k.Checked = false; ZetKinderen(k, false); Meld(); }
    }

    TreeNode? Zoek(string rel, bool openklappen)
    {
        TreeNode? k = _wortel;
        foreach (var deel in rel.Split('\\'))
        {
            if (k is null) return null;
            Laad(k); if (openklappen) k.Expand();
            k = k.Nodes.Cast<TreeNode>().FirstOrDefault(n => string.Equals(n.Text, deel, StringComparison.OrdinalIgnoreCase));
        }
        return k;
    }

    void Meld()
    {
        if (_wortel is null || _gewijzigd is null) return;
        var lijst = new List<string>();
        Verzamel(_wortel, lijst);
        _gewijzigd(lijst);
    }

    string Rel(string pad) => pad[_basis.Length..].TrimStart('\\');

    static bool HeeftSubmap(string p)
    {
        try { using var e = Directory.EnumerateDirectories(p).GetEnumerator(); return e.MoveNext(); }
        catch { return false; }
    }

    static TreeNode Knoop(string pad, string naam, bool aan)
    {
        var k = new TreeNode(naam) { Tag = pad, Checked = aan };
        if (HeeftSubmap(pad)) k.Nodes.Add(new TreeNode("...") { Tag = Leeg });   // plekhouder: nog niet geladen
        return k;
    }

    void Laad(TreeNode k)
    {
        if (k.Nodes.Count != 1 || k.Nodes[0].Tag as string != Leeg) return;
        k.Nodes.Clear();
        string[] mappen;
        try { mappen = Directory.GetDirectories((string)k.Tag!); Array.Sort(mappen, StringComparer.OrdinalIgnoreCase); }
        catch { mappen = Array.Empty<string>(); }
        foreach (var m in mappen)
        {
            var naam = Path.GetFileName(m);
            if (GeenMappen.Contains(naam, StringComparer.OrdinalIgnoreCase)) continue;
            k.Nodes.Add(Knoop(m, naam, k.Checked && !_uit.Contains(Rel(m))));
        }
    }

    static void ZetKinderen(TreeNode k, bool aan)
    {
        foreach (TreeNode n in k.Nodes) if (n.Tag as string != Leeg) { n.Checked = aan; ZetKinderen(n, aan); }
    }

    // uitgevinkte map: opnemen en niet verder kijken (alles eronder gaat dan ook niet mee)
    void Verzamel(TreeNode k, List<string> lijst)
    {
        foreach (TreeNode n in k.Nodes)
        {
            if (n.Tag as string == Leeg) continue;
            if (!n.Checked) lijst.Add(Rel((string)n.Tag!)); else Verzamel(n, lijst);
        }
    }
}

// ================================================================ totalen na afloop
// Zo groot als de tabel nodig heeft. Niet-modaal: een geplande afsluiting van de pc loopt gewoon door.
public sealed class TotalenVenster : Form
{
    readonly DataGridView _tab = new();
    readonly Panel _kop, _voet;
    readonly Label _regel;

    public TotalenVenster(string titel, string regel, List<HoofdVenster.OverzichtRegel> o, bool proef, Action logOpenen)
    {
        Text = titel; Font = Gewoon; BackColor = Achter; AutoScaleMode = AutoScaleMode.None;
        FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = MinimizeBox = false; ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;

        _kop = new Panel { Dock = DockStyle.Top, Height = P(74), BackColor = Kop };
        Lbl(titel, 18, 12, KaartKop, _kop).ForeColor = Color.White;
        _regel = Lbl(regel, 19, 42, Gewoon, _kop); _regel.ForeColor = KopTekst;

        var t = _tab;
        t.Dock = DockStyle.Fill; t.BackgroundColor = Kaart; t.BorderStyle = BorderStyle.None; t.ReadOnly = true;
        t.AllowUserToAddRows = false; t.AllowUserToResizeRows = false; t.RowHeadersVisible = false;
        t.CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal; t.GridColor = Lijn; t.ScrollBars = ScrollBars.Both;
        t.EnableHeadersVisualStyles = false; t.ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.None;
        t.ColumnHeadersHeight = P(34); t.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing;
        t.RowTemplate.Height = P(28);
        var ks = t.ColumnHeadersDefaultCellStyle;
        ks.BackColor = Kaart; ks.ForeColor = Zacht; ks.Font = Kolom; ks.SelectionBackColor = Kaart; ks.WrapMode = DataGridViewTriState.False;
        t.DefaultCellStyle.SelectionBackColor = Kaart; t.DefaultCellStyle.SelectionForeColor = Tekst; t.DefaultCellStyle.ForeColor = Tekst;
        t.DefaultCellStyle.Padding = new Padding(P(8), 0, P(8), 0); ks.Padding = t.DefaultCellStyle.Padding;
        foreach (var kopTekst in new[] { "MAP", proef ? "ZOU KOPIËREN" : "GEKOPIEERD", "GROOTTE", "OVERGESLAGEN", "MISLUKT", "EXTRA OP DOEL", "RESULTAAT" })
        {
            var c = new DataGridViewTextBoxColumn { HeaderText = kopTekst, SortMode = DataGridViewColumnSortMode.NotSortable, AutoSizeMode = DataGridViewAutoSizeColumnMode.AllCells };
            if (kopTekst is not ("MAP" or "RESULTAAT")) { c.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleRight; c.HeaderCell.Style.Alignment = DataGridViewContentAlignment.MiddleRight; }
            t.Columns.Add(c);
        }
        t.Columns[0].DefaultCellStyle.Font = Vet;
        foreach (var x in o)
        {
            if (x.Opmerking.Length > 0 && x.Gekopieerd == 0 && x.Overgeslagen == 0)
            {
                int j = t.Rows.Add(x.Naam, "", "", "", "", "", x.Opmerking);
                t.Rows[j].Cells[6].Style.ForeColor = Oranje;
                continue;
            }
            int i = t.Rows.Add(x.Naam, Opmaak.Bestanden(x.Gekopieerd), Opmaak.Kort(x.Bytes), Opmaak.Aantal(x.Overgeslagen), Opmaak.Aantal(x.Mislukt),
                HoofdVenster.Extra(x), x.Opmerking.StartsWith("gestopt") ? x.Opmerking
                    : x.Mislukt > 0 ? $"{Opmaak.Aantal(x.Mislukt)} mislukt" : x.Gecontroleerd > 0 ? "klaar, gecontroleerd" : "klaar");
            t.Rows[i].Cells[6].Style.ForeColor = x.Opmerking == "gestopt" ? Oranje : x.Mislukt > 0 || x.Opmerking.Length > 0 ? Rood : Groen;
        }
        int s = t.Rows.Add("Totaal", Opmaak.Bestanden(o.Sum(x => x.Gekopieerd)), Opmaak.Kort(o.Sum(x => x.Bytes)),
            Opmaak.Aantal(o.Sum(x => x.Overgeslagen)), Opmaak.Aantal(o.Sum(x => x.Mislukt)), "", "");
        t.Rows[s].DefaultCellStyle.Font = Vet; t.Rows[s].DefaultCellStyle.BackColor = Om; t.Rows[s].DefaultCellStyle.SelectionBackColor = Om;

        var midden = new Panel { Dock = DockStyle.Fill, Padding = new Padding(P(18), P(16), P(18), P(6)) };
        var kaart = new Panel { Dock = DockStyle.Fill, BackColor = Kaart, Padding = new Padding(1) };
        kaart.Paint += (s2, e) => { using var pen = new Pen(Lijn); e.Graphics.DrawRectangle(pen, 0, 0, kaart.Width - 1, kaart.Height - 1); };
        kaart.Controls.Add(t); midden.Controls.Add(kaart);

        _voet = new Panel { Dock = DockStyle.Bottom, Height = P(64) };
        var bLog = Knop("Log openen", 120); bLog.Location = new Point(P(18), P(14));
        var bOk = Knop("OK", 100, true); bOk.Anchor = AnchorStyles.Top | AnchorStyles.Right; bOk.Name = "ok";
        _voet.Controls.AddRange(new Control[] { bLog, bOk });
        Controls.AddRange(new Control[] { midden, _voet, _kop });
        AcceptButton = bOk; CancelButton = bOk;
        bOk.Click += (s2, e) => Close();
        bLog.Click += (s2, e) => logOpenen();
    }

    // passend maken: breedte en hoogte uit de tabel (binnen 90% van het scherm), midden boven de eigenaar
    public void ToonBij(Form eigenaar)
    {
        // pas bij het laden rekenen: dan heeft de tabel de kolommen al op de breedte van de inhoud gezet
        Load += (s, e) => Pas(eigenaar);
        Show(eigenaar);
        _voet.Controls["ok"]!.Focus();
    }

    void Pas(Form eigenaar)
    {
        int b = 4 + _tab.Columns.Cast<DataGridViewColumn>().Sum(c => c.Width);
        int h = _tab.ColumnHeadersHeight + 4 + _tab.Rows.Cast<DataGridViewRow>().Sum(r => r.Height);
        var werk = Screen.FromControl(eigenaar).WorkingArea;
        int rand = P(18) * 2 + 2;                                  // binnenrand links en rechts plus kaartrand
        int breed = Math.Max(Math.Max(b, _regel.PreferredWidth + P(20)) + rand, P(420));
        int hoog = _kop.Height + P(16) + P(6) + 2 + h + _voet.Height;
        bool past = breed <= werk.Width * 0.9 && hoog <= werk.Height * 0.9;
        _tab.ScrollBars = past ? ScrollBars.None : ScrollBars.Both;
        ClientSize = new Size(Math.Min(breed, (int)(werk.Width * 0.9)), Math.Min(hoog, (int)(werk.Height * 0.9)));
        var ok = _voet.Controls["ok"]!;
        ok.Location = new Point(ClientSize.Width - P(118), P(14));
        Location = new Point(eigenaar.Left + (eigenaar.Width - Width) / 2, eigenaar.Top + (eigenaar.Height - Height) / 2);
        _tab.ClearSelection(); _tab.CurrentCell = null;
    }
}

// ================================================================ eenvoudige invoervraag
public sealed class InvoerVenster : Form
{
    readonly TextBox _tb = new();

    InvoerVenster(string titel, string vraag)
    {
        Text = titel; Font = Gewoon; BackColor = Achter; AutoScaleMode = AutoScaleMode.None;
        FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = MinimizeBox = false; ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterParent; ClientSize = new Size(P(460), P(150));
        var l = Lbl(vraag, 16, 16, Gewoon, this); l.MaximumSize = new Size(P(430), 0);
        _tb.Location = new Point(P(18), P(62)); _tb.Width = P(424);
        var ok = Knop("OK", 90, true); ok.Location = new Point(P(254), P(102)); ok.DialogResult = DialogResult.OK;
        var weg = Knop("Annuleren", 100); weg.Location = new Point(P(342), P(102)); weg.DialogResult = DialogResult.Cancel;
        Controls.AddRange(new Control[] { _tb, ok, weg });
        AcceptButton = ok; CancelButton = weg;
    }

    public static string? Vraag(IWin32Window eigenaar, string titel, string vraag)
    {
        using var v = new InvoerVenster(titel, vraag);
        return v.ShowDialog(eigenaar) == DialogResult.OK ? v._tb.Text : null;
    }
}

// ================================================================ inplannen
public sealed class PlanVenster : Form
{
    public Planning Resultaat { get; private set; }
    readonly CheckBox _aan;
    readonly RadioButton _dag, _week;
    readonly ComboBox _weekdag;
    readonly DateTimePicker _tijd;

    public PlanVenster(string profiel, Planning p, bool taakBestaat)
    {
        Resultaat = p;
        Text = $"Inplannen - {profiel}"; Font = Gewoon; BackColor = Achter; AutoScaleMode = AutoScaleMode.None;
        FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = MinimizeBox = false; ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterParent; ClientSize = new Size(P(500), P(330));

        _aan = new CheckBox { Text = $"Profiel '{profiel}' automatisch starten", AutoSize = true, Location = new Point(P(20), P(18)), Font = Vet, Checked = p.Aan };
        _dag = new RadioButton { Text = "Elke dag", AutoSize = true, Location = new Point(P(42), P(58)), Checked = p.Frequentie == "dagelijks" };
        _week = new RadioButton { Text = "Elke week op", AutoSize = true, Location = new Point(P(42), P(90)), Checked = p.Frequentie != "dagelijks" };
        _weekdag = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Location = new Point(P(170), P(87)), Width = P(140) };
        _weekdag.Items.AddRange(Planner.Dagen);
        _weekdag.SelectedIndex = Math.Clamp(p.Dag, 0, 6);
        Lbl("om", 44, 128, Gewoon, this);
        _tijd = new DateTimePicker
        {
            Format = DateTimePickerFormat.Custom, CustomFormat = "HH:mm", ShowUpDown = true, Location = new Point(P(82), P(124)), Width = P(90),
            Value = DateTime.Today.Add(TimeSpan.TryParse(p.Tijd, out var t) ? t : TimeSpan.FromHours(2)),
        };
        var uitleg = Lbl("De backup start via de Windows Taakplanner als je bent aangemeld; het venster gaat naar het " +
            "systeemvak en sluit als de backup klaar is. Stond de pc uit, dan wordt de keer ingehaald zodra het kan.", 20, 172, Klein, this);
        uitleg.MaximumSize = new Size(P(460), 0); uitleg.ForeColor = Zacht;
        var status = Lbl(taakBestaat ? "Staat nu in de Taakplanner." : "Staat nu niet in de Taakplanner.", 20, 236, Klein, this);
        status.ForeColor = taakBestaat ? Groen : Zacht;
        var ok = Knop("OK", 90, true); ok.Location = new Point(P(290), P(276)); ok.DialogResult = DialogResult.OK;
        var weg = Knop("Annuleren", 100); weg.Location = new Point(P(388), P(276)); weg.DialogResult = DialogResult.Cancel;
        Controls.AddRange(new Control[] { _aan, _dag, _week, _weekdag, _tijd, ok, weg });
        AcceptButton = ok; CancelButton = weg;

        void Bij() { _dag.Enabled = _week.Enabled = _tijd.Enabled = _aan.Checked; _weekdag.Enabled = _aan.Checked && _week.Checked; }
        _aan.CheckedChanged += (s, e) => Bij(); _week.CheckedChanged += (s, e) => Bij();
        Bij();
        FormClosing += (s, e) =>
        {
            if (DialogResult != DialogResult.OK) return;
            Resultaat = new Planning
            {
                Aan = _aan.Checked, Frequentie = _dag.Checked ? "dagelijks" : "wekelijks", Dag = _weekdag.SelectedIndex, Tijd = _tijd.Value.ToString("HH:mm"),
            };
        };
    }
}

// ================================================================ over / uitleg
// Links de onderdelen, rechts de uitleg (één doorlopende tekst; klikken op een onderdeel springt ernaartoe)
public sealed class OverVenster : Form
{
    readonly RichTextBox _tekst = new();
    readonly ListBox _lijst = new();
    readonly List<int> _begin = new();

    public OverVenster(Icon? icoon)
    {
        Text = "Over Backup"; Font = Gewoon; BackColor = Achter; Icon = icoon; AutoScaleMode = AutoScaleMode.None;
        StartPosition = FormStartPosition.CenterParent; ShowInTaskbar = false; MinimizeBox = false;
        var werk = Screen.PrimaryScreen?.WorkingArea ?? new Rectangle(0, 0, P(1100), P(900));
        ClientSize = new Size(Math.Min(P(1100), werk.Width - P(60)), Math.Min(P(860), werk.Height - P(60)));
        MinimumSize = new Size(P(700), P(500));

        var kop = new Panel { Dock = DockStyle.Top, Height = P(74), BackColor = Kop };
        Lbl("Backup", 18, 10, KaartKop, kop).ForeColor = Color.White;
        var versie = typeof(OverVenster).Assembly.GetName().Version;
        Lbl($"Versie {versie?.ToString(3)}  ·  mappen en schijven backuppen met een eigen kopieermotor", 19, 40, Gewoon, kop).ForeColor = KopTekst;

        _lijst.Dock = DockStyle.Left; _lijst.Width = P(250); _lijst.BorderStyle = BorderStyle.None; _lijst.BackColor = Kaart;
        _lijst.Font = Gewoon; _lijst.ItemHeight = P(30); _lijst.DrawMode = DrawMode.OwnerDrawFixed; _lijst.IntegralHeight = false;
        _lijst.DrawItem += (s, e) =>
        {
            if (e.Index < 0) return;
            bool gekozen = (e.State & DrawItemState.Selected) != 0;
            using (var achter = new SolidBrush(gekozen ? Selectie : Kaart)) e.Graphics.FillRectangle(achter, e.Bounds);
            var r = new Rectangle(e.Bounds.X + P(14), e.Bounds.Y, e.Bounds.Width - P(16), e.Bounds.Height);
            TextRenderer.DrawText(e.Graphics, (string)_lijst.Items[e.Index], gekozen ? Vet : Gewoon, r, Tekst,
                TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis);
        };
        var scheiding = new Panel { Dock = DockStyle.Left, Width = 1, BackColor = Lijn };

        _tekst.Dock = DockStyle.Fill; _tekst.ReadOnly = true; _tekst.BorderStyle = BorderStyle.None; _tekst.BackColor = Kaart;
        _tekst.DetectUrls = false; _tekst.ScrollBars = RichTextBoxScrollBars.Vertical;
        var rand = new Panel { Dock = DockStyle.Fill, BackColor = Kaart, Padding = new Padding(P(24), P(12), P(12), P(12)) };
        rand.Controls.Add(_tekst);

        var voet = new Panel { Dock = DockStyle.Bottom, Height = P(60), Width = ClientSize.Width };   // breedte vóór het verankeren
        var ok = Knop("Sluiten", 110, true); ok.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        ok.Location = new Point(ClientSize.Width - P(130), P(13));
        ok.Click += (s, e) => Close();
        voet.Controls.Add(ok);
        AcceptButton = ok; CancelButton = ok;

        Controls.AddRange(new Control[] { rand, scheiding, _lijst, voet, kop });
        Vul();
        _lijst.SelectedIndexChanged += (s, e) => Spring(_lijst.SelectedIndex);
        Shown += (s, e) => { _lijst.SelectedIndex = 0; _tekst.Select(0, 0); };
    }

    void Vul()
    {
        var titel = new Font("Segoe UI Semibold", 15);
        var tussen = new Font("Segoe UI Semibold", 11);
        var gewoon = new Font("Segoe UI", 10.5f);
        void Schrijf(string s, Font f, Color kleur, int inspring = 0, int hangend = 0, int na = 0)
        {
            _tekst.SelectionStart = _tekst.TextLength; _tekst.SelectionLength = 0;
            _tekst.SelectionFont = f; _tekst.SelectionColor = kleur;
            _tekst.SelectionIndent = inspring; _tekst.SelectionHangingIndent = hangend; _tekst.SelectionBullet = false;
            _tekst.AppendText(s + "\n");
            if (na > 0) { _tekst.SelectionFont = new Font("Segoe UI", na); _tekst.AppendText("\n"); }
        }
        foreach (var (kopTekst, inhoud) in Uitleg.Onderdelen)
        {
            _lijst.Items.Add(kopTekst);
            if (_begin.Count > 0) Schrijf("", gewoon, Tekst, na: 6);
            _begin.Add(_tekst.TextLength);
            Schrijf(kopTekst, titel, Blauw, na: 2);
            foreach (var regel in inhoud.Trim().Split('\n').Select(r => r.TrimEnd('\r')))
            {
                if (regel.StartsWith("## ")) Schrijf(regel[3..], tussen, Tekst);
                else if (regel.StartsWith("• ")) Schrijf("•  " + regel[2..], gewoon, Tekst, P(10), P(16));
                else if (regel.Length == 0) Schrijf("", gewoon, Tekst);
                else Schrijf(regel, gewoon, Tekst);
            }
        }
    }

    // naar het onderdeel springen en het bovenaan zetten
    public void Spring(int i)
    {
        if (i < 0 || i >= _begin.Count) return;
        _tekst.Select(_tekst.TextLength, 0); _tekst.ScrollToCaret();
        _tekst.Select(_begin[i], 0); _tekst.ScrollToCaret();
    }
}
