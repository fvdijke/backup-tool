using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using static BackupTool.Stijl;

namespace BackupTool;

public sealed class HoofdVenster : Form
{
    // ---- gegevens per regel in de lijst (Row.Tag)
    sealed class RijInfo
    {
        public long Mappen;                         // aantal mappen bij de vorige backup (percentage tijdens vergelijken)
        public List<string> Overslaan = new();      // uitgevinkte submappen, ten opzichte van de bron
        public string Bron = "";                    // om te zien of de bron is gewijzigd
    }
    sealed record Balkje(double Deel, Color Kleur);   // Tag van de voortgangscel; Deel < 0 = geen vulling

    public sealed class OverzichtRegel
    {
        public string Naam = "", Opmerking = "";
        public long Gekopieerd, Overgeslagen, Mislukt, ExtraBest, ExtraMap, Gewist, Gecontroleerd, ViaSchaduw;
        public double Bytes;
    }

    sealed class Run
    {
        public readonly List<DataGridViewRow> Taken = new();
        public int I = -1;
        public bool Proef, Gestopt, Pauze, AlleRegels, Klaar;
        public readonly Stopwatch Klok = new(), KopieerKlok = new();
        public string Log = "";
        public int LogFout;
        public long Fouten;
        public readonly List<OverzichtRegel> Overzicht = new();
        public readonly CancellationTokenSource Stop = new();
        public Kopieermotor? Motor;
        public Schaduwkopieen? Schaduw;
        public readonly ConcurrentQueue<(string Tekst, Regelsoort Soort)> Regels = new();
    }

    static readonly Dictionary<string, (Color Kleur, string Tekst)> Status = new()
    {
        [""] = (Zacht, ""), ["wacht"] = (Grijs, "Wacht"), ["bezig"] = (Blauw, "Bezig"), ["klaar"] = (Groen, "Klaar"),
        ["proef"] = (Groen, "Proef klaar"), ["fouten"] = (Oranje, "Klaar met fouten"), ["mislukt"] = (Rood, "Mislukt"),
        ["mist"] = (Oranje, "Bron mist"), ["gestopt"] = (Grijs, "Gestopt"), ["pauze"] = (Oranje, "Gepauzeerd"),
    };

    const string Hoofdmap = "(hoofdmap, 1-op-1)";
    static readonly string[] GeenMappen = { "$RECYCLE.BIN", "System Volume Information" };
    static readonly string[] TijdelijkePatronen = { "*.tmp", "~$*", "Thumbs.db", ".DS_Store" };

    readonly Program.Opties _o;
    ProfielBestand _data = new();
    Profiel? _huidig;
    bool _laden, _sluitNaStop, _sluitZonderVragen;
    Run? _run;
    readonly object _logSlot = new();
    readonly List<string> _doelen = new();     // pad per regel in cbDoel; "*" = andere map kiezen
    int _vorigDoel = -1;
    double _vrijDeel;

    // ---- onderdelen van het venster
    readonly ToolTip tt = new() { AutoPopDelay = 15000 };
    ComboBox cbProfiel = null!, cbDoel = null!;
    Button bNieuw = null!, bWeg = null!, bPlan = null!, bMap = null!, bSchijf = null!, bVerwijder = null!, bOp = null!, bNeer = null!,
        bAllesAan = null!, bAllesUit = null!, bVernieuw = null!, bKiesLog = null!, bOpenLog = null!,
        bStart = null!, bPauze = null!, bProef = null!, bAfsluiten = null!;
    DataGridView grid = null!;
    FlowLayoutPanel balkBron = null!;
    SubmappenPaneel paneel = null!;
    readonly ContextMenuStrip menuSchijf = new() { Font = Gewoon };
    Panel kInst = null!, vrijBalk = null!;
    TextBox tbSubmap = null!, tbLog = null!;
    Label lVrij = null!, lBestand = null!, lTaak = null!, lTijd = null!, lStatus = null!, lPlan = null!;
    RadioButton rbBij = null!, rbSpiegel = null!;
    CheckBox cbAlle = null!, cbAfsluiten = null!, cbTijdelijk = null!, cbVerborgen = null!, cbMapDatum = null!, cbFft = null!, cbDst = null!, cbHerstart = null!,
        cbControle = null!, cbSchaduw = null!, cbPauzeAfwijking = null!;
    NotifyIcon _klok = null!;
    ToolStripMenuItem _klokPauze = null!;
    NumericUpDown nPogingen = null!, nWacht = null!, nThreads = null!;
    RichTextBox rtb = null!;
    ProgressBar balk = null!;

    public HoofdVenster(Program.Opties o)
    {
        _o = o;
        Text = "Backup";
        AutoScaleMode = AutoScaleMode.None;
        // breed genoeg voor de lijst plus de mappenboom ernaast, maar nooit breder dan het scherm
        var werk = Screen.PrimaryScreen?.WorkingArea ?? new Rectangle(0, 0, P(1500), P(1100));
        ClientSize = new Size(Math.Min(P(1500), werk.Width - P(40)), Math.Min(P(1040), werk.Height - P(40)));
        MinimumSize = new Size(P(1180), P(760));
        StartPosition = FormStartPosition.CenterScreen;
        Font = Gewoon; BackColor = Achter;
        Icon = AppIcoon();
        BouwVenster();
        BouwKlok();

        _data = Profielen.Lees(_o.Profielen);
        var eerste = _data.Profielen.FirstOrDefault(p => p.Naam == (_o.Profiel ?? _data.Laatste)) ?? _data.Profielen[0];
        VulProfielen(eerste.Naam);
        LaadProfiel(eerste);
        if (!File.Exists(_o.Profielen)) BewaarProfiel();

        Shown += BijTonen;
        FormClosing += BijSluiten;
    }

    // ================================================================ venster opbouwen
    void BouwVenster()
    {
        // ---- kop met profielkeuze
        var kop = new Panel { Dock = DockStyle.Top, Height = P(88), BackColor = Kop, Width = ClientSize.Width };
        Lbl("Backup", 22, 14, Titel, kop).ForeColor = Color.White;
        Lbl("Kies wat je wilt backuppen en waarheen. Alles wordt automatisch bewaard in het profiel.", 24, 54, Gewoon, kop).ForeColor = KopTekst;
        var lProfiel = Lbl("Profiel", 0, 34, Vet, kop); lProfiel.ForeColor = KopTekst;
        cbProfiel = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = P(220) };
        bNieuw = Knop("Nieuw", 80); bWeg = Knop("Verwijderen", 110); bPlan = Knop("Inplannen...", 120);
        var bOver = Knop("Over", 80);
        tt.SetToolTip(bOver, "Uitleg van alle onderdelen en opties");
        bOver.Click += (s, e) => { using var v = new OverVenster(Icon) { TopMost = TopMost }; v.ShowDialog(this); };
        tt.SetToolTip(bPlan, "Dit profiel automatisch laten starten via de Windows Taakplanner");
        int x = ClientSize.Width - P(20);
        foreach (Control c in new Control[] { bOver, bPlan, bWeg, bNieuw, cbProfiel, lProfiel })
        {
            x -= c.Width; c.Left = x; x -= P(8); c.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            if (c != lProfiel) c.Top = P(14);
        }
        cbProfiel.Top = P(18); lProfiel.Top = P(21);
        lPlan = new Label
        {
            AutoSize = false, TextAlign = ContentAlignment.MiddleRight, ForeColor = KopTekst, Font = Klein,
            Size = new Size(P(520), P(22)), Location = new Point(ClientSize.Width - P(540), P(56)), Anchor = AnchorStyles.Top | AnchorStyles.Right,
        };
        kop.Controls.AddRange(new Control[] { cbProfiel, bNieuw, bWeg, bPlan, bOver, lPlan });

        // ---- midden
        var rand = new Panel { Dock = DockStyle.Fill, Padding = new Padding(P(20), P(16), P(20), 0), BackColor = Achter };

        // kaart 1: bronnen
        var kBron = KaartPaneel(rand, DockStyle.Fill, 0, 0);
        kBron.Padding = new Padding(P(1), 0, P(1), P(1));
        BouwLijst(kBron);

        // kaart 2: doel, logbestand en opties
        kInst = KaartPaneel(rand, DockStyle.Bottom, 320, 24);
        BouwInstellingen(kInst);
        // scheiding tussen bron (boven) en doel (hier): een dunne lijn over de hele breedte, midden in de tussenruimte
        var tussen = kInst.Parent!;
        tussen.Paint += (s, e) =>
        {
            using var pen = new Pen(KnopRand);
            int y = P(24) / 2;
            e.Graphics.DrawLine(pen, 0, y, tussen.Width, y);
        };

        // kaart 3: voortgang en uitvoer
        var kVoort = KaartPaneel(rand, DockStyle.Bottom, 220, 14);
        kVoort.Padding = new Padding(P(16), P(12), P(16), P(14));
        rtb = new RichTextBox
        {
            Dock = DockStyle.Fill, ReadOnly = true, BorderStyle = BorderStyle.None, BackColor = Stijl.Uitvoer, Font = Mono,
            WordWrap = false, DetectUrls = false, HideSelection = false,
        };
        kVoort.Controls.Add(rtb);
        lBestand = new Label { Dock = DockStyle.Top, Height = P(28), Font = Klein, ForeColor = Zacht, AutoEllipsis = true, TextAlign = ContentAlignment.MiddleLeft };
        kVoort.Controls.Add(lBestand);
        balk = new ProgressBar { Dock = DockStyle.Top, Height = P(10), Style = ProgressBarStyle.Continuous };
        kVoort.Controls.Add(balk);
        var regelTaak = new Panel { Dock = DockStyle.Top, Height = P(30) };
        lTijd = new Label { Dock = DockStyle.Right, Width = P(480), TextAlign = ContentAlignment.MiddleRight, ForeColor = Zacht };
        lTaak = new Label { Dock = DockStyle.Fill, Font = KaartKop, TextAlign = ContentAlignment.MiddleLeft, Text = "Klaar om te starten" };
        regelTaak.Controls.AddRange(new Control[] { lTaak, lTijd });
        kVoort.Controls.Add(regelTaak);

        // ---- voet: links starten, pauze, stoppen, proefdraai; dan de statusregel; rechts afsluiten
        var voet = new Panel { Dock = DockStyle.Bottom, Height = P(70), BackColor = Achter, Width = ClientSize.Width };
        // de startknop wordt tijdens een backup de stopknop
        bStart = Knop("Backup starten", 170, true); bPauze = Knop("Pauze", 110); bProef = Knop("Proefdraai", 120);
        tt.SetToolTip(bProef, "Laat zien wat er gekopieerd zou worden, zonder iets te kopiëren");
        tt.SetToolTip(bPauze, "De backup even stilzetten en later op dezelfde plek verder laten gaan");
        x = P(20);
        foreach (var b in new[] { bStart, bPauze, bProef }) { b.Left = x; b.Top = P(17); x += b.Width + P(10); }
        bAfsluiten = Knop("Afsluiten", 110);
        bAfsluiten.BackColor = KnopRood; bAfsluiten.ForeColor = Color.White;
        bAfsluiten.FlatAppearance.BorderSize = 0; bAfsluiten.FlatAppearance.MouseOverBackColor = KnopRoodDonker;
        bAfsluiten.Location = new Point(ClientSize.Width - P(130), P(17)); bAfsluiten.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        lStatus = new Label
        {
            ForeColor = Zacht, AutoSize = false, AutoEllipsis = true, TextAlign = ContentAlignment.MiddleLeft,
            Location = new Point(x + P(10), P(16)), Size = new Size(bAfsluiten.Left - x - P(30), P(36)),
            Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Top,
        };
        voet.Controls.AddRange(new Control[] { lStatus, bStart, bPauze, bProef, bAfsluiten });
        bPauze.Enabled = false;

        Controls.Add(rand); Controls.Add(voet); Controls.Add(kop);

        // ---- gebeurtenissen
        cbProfiel.SelectedIndexChanged += (s, e) => WisselProfiel();
        bNieuw.Click += (s, e) => NieuwProfiel();
        bWeg.Click += (s, e) => VerwijderProfiel();
        bPlan.Click += (s, e) => Inplannen();
        bStart.Click += async (s, e) => { if (_run is { Klaar: false }) Stoppen(); else await StartRun(false); };
        bProef.Click += async (s, e) => await StartRun(true);
        bPauze.Click += (s, e) => { if (_run is { Klaar: false }) ZetPauze(!_run.Pauze); };
        bAfsluiten.Click += (s, e) => Close();
        // minimaliseren = naar het systeemvak (icoon bij de klok)
        Resize += (s, e) => { if (WindowState == FormWindowState.Minimized) { Hide(); ShowInTaskbar = false; } };
    }

    // ---- icoon bij de klok: dubbelklik opent het venster; meldingen als een backup klaar is
    void BouwKlok()
    {
        var menu = new ContextMenuStrip { Font = Gewoon };
        menu.Items.Add("Openen", null, (s, e) => ToonVenster());
        _klokPauze = (ToolStripMenuItem)menu.Items.Add("Pauze", null, (s, e) => { if (_run is { Klaar: false }) ZetPauze(!_run.Pauze); });
        menu.Items.Add("Stoppen", null, (s, e) => Stoppen());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Afsluiten", null, (s, e) => { ToonVenster(); Close(); });
        menu.Opening += (s, e) =>
        {
            bool bezig = _run is { Klaar: false };
            _klokPauze.Enabled = menu.Items[2].Enabled = bezig;
            _klokPauze.Text = _run is { Pauze: true } ? "Hervatten" : "Pauze";
        };
        _klok = new NotifyIcon { Icon = Icon, Text = "Backup", Visible = true, ContextMenuStrip = menu };
        _klok.DoubleClick += (s, e) => ToonVenster();
        _klok.BalloonTipClicked += (s, e) => ToonVenster();
    }

    void ToonVenster()
    {
        ShowInTaskbar = true; Show();
        if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
        Activate();
    }

    void KlokTekst(string tekst) => _klok.Text = tekst.Length > 120 ? tekst[..120] : tekst;

    // ---- inplannen in de Windows Taakplanner
    void Inplannen()
    {
        if (_huidig is null) return;
        BewaarProfiel();
        using var v = new PlanVenster(_huidig.Naam, _huidig.Plan, Planner.Bestaat(_huidig.Naam));
        if (v.ShowDialog(this) != DialogResult.OK) return;
        var fout = Planner.Zet(_huidig.Naam, v.Resultaat, beheerder: cbSchaduw.Checked);
        if (fout is not null)
        {
            MessageBox.Show(this, "Inplannen in de Taakplanner lukte niet:\n\n" + fout +
                (cbSchaduw.Checked && !Schaduwkopieen.IsBeheerder
                    ? "\n\n'Geopende bestanden kopiëren' staat aan: daarvoor moet de taak met beheerdersrechten draaien. Start Backup als beheerder en plan opnieuw in."
                    : ""), "Inplannen", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        _huidig.Plan = v.Resultaat;
        BewaarProfiel();
        ToonPlan();
    }

    void ToonPlan()
    {
        var p = _huidig?.Plan ?? new Planning();
        lPlan.Text = p.Aan ? "Ingepland: " + Planner.Omschrijving(p).ToLowerInvariant() : "";
    }

    void BouwLijst(Panel kBron)
    {
        grid = new DataGridView
        {
            Dock = DockStyle.Fill, BackgroundColor = Kaart, BorderStyle = BorderStyle.None,
            AllowUserToAddRows = false, AllowUserToDeleteRows = false, AllowUserToResizeRows = false,
            RowHeadersVisible = false, SelectionMode = DataGridViewSelectionMode.FullRowSelect, MultiSelect = false,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill, CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal,
            GridColor = Lijn, ColumnHeadersHeight = P(36), ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing,
            ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.None, EnableHeadersVisualStyles = false,
            EditMode = DataGridViewEditMode.EditOnKeystrokeOrF2,
        };
        grid.RowTemplate.Height = P(31);
        var kopStijl = grid.ColumnHeadersDefaultCellStyle;
        kopStijl.BackColor = Kaart; kopStijl.ForeColor = Zacht; kopStijl.Font = Stijl.Kolom; kopStijl.SelectionBackColor = Kaart;
        kopStijl.Padding = new Padding(P(6), 0, 0, 0);
        grid.DefaultCellStyle.ForeColor = Tekst; grid.DefaultCellStyle.SelectionBackColor = Selectie; grid.DefaultCellStyle.SelectionForeColor = Tekst;
        grid.DefaultCellStyle.Padding = new Padding(P(6), 0, P(6), 0);
        DubbelBuffer(grid);

        DataGridViewColumn Kolom(DataGridViewColumn c, string naam, string kopTekst, float breedte, bool alleenLezen, string tip)
        {
            c.Name = naam; c.HeaderText = kopTekst; c.FillWeight = breedte; c.ReadOnly = alleenLezen; c.SortMode = DataGridViewColumnSortMode.NotSortable;
            c.ToolTipText = tip; grid.Columns.Add(c); return c;
        }
        var cAan = Kolom(new DataGridViewCheckBoxColumn(), "aan", "", 4, false, "Aangevinkt = meenemen in de backup");
        cAan.AutoSizeMode = DataGridViewAutoSizeColumnMode.None; cAan.Width = P(44);
        Kolom(new DataGridViewTextBoxColumn(), "naam", "NAAM", 18, false, "Dubbelklik of F2 om te wijzigen").DefaultCellStyle.Font = Vet;
        Kolom(new DataGridViewTextBoxColumn(), "bron", "BRON", 34, false, "Map of schijf die gekopieerd wordt. Dubbelklik of F2 om te wijzigen.").DefaultCellStyle.Font = Mono;
        Kolom(new DataGridViewTextBoxColumn(), "doel", "DOELMAP (op de backupschijf)", 24, false,
            "Submap op de doelschijf. Leeg = rechtstreeks in de hoofdmap (1-op-1, zoals bij een hele schijf). Dubbelklik of F2 om te wijzigen.").DefaultCellStyle.Font = Mono;
        Kolom(new DataGridViewTextBoxColumn(), "voortgang", "VOORTGANG", 17, true, "Voortgang van deze map tijdens de backup");
        Kolom(new DataGridViewTextBoxColumn(), "status", "STATUS", 14, true, "").DefaultCellStyle.Font = Vet;
        kBron.Controls.Add(grid);

        balkBron = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = P(54), BackColor = Kaart, Padding = new Padding(P(12), P(9), P(12), 0) };
        bMap = Knop("Map toevoegen", 140); bSchijf = Knop("Hele schijf toevoegen", 180);
        bVerwijder = Knop("Verwijderen", 115); bOp = Knop("Omhoog", 90); bNeer = Knop("Omlaag", 90);
        bAllesAan = Knop("Alles aan", 95); bAllesUit = Knop("Alles uit", 95);
        bOp.Margin = new Padding(P(18), 3, 3, 3); bAllesAan.Margin = new Padding(P(18), 3, 3, 3);
        balkBron.Controls.AddRange(new Control[] { bMap, bSchijf, bVerwijder, bOp, bNeer, bAllesAan, bAllesUit });
        kBron.Controls.Add(balkBron);
        tt.SetToolTip(bMap, "Eén of meer mappen kiezen (Ctrl of Shift ingedrukt houden om er meer te kiezen)");

        kBron.Controls.Add(new Label
        {
            Text = "Wat wil je backuppen?", Font = KaartKop, ForeColor = Tekst, Dock = DockStyle.Top, Height = P(42),
            TextAlign = ContentAlignment.MiddleLeft, Padding = new Padding(P(14), 0, 0, 0),
        });

        // rechts: de submappen van de geselecteerde bron, met vinkjes (als laatste toegevoegd: krijgt de volle hoogte)
        paneel = new SubmappenPaneel { Dock = DockStyle.Right, Width = P(400) };
        paneel.Paint += (s, e) => { using var pen = new Pen(Lijn); e.Graphics.DrawLine(pen, 0, 0, 0, paneel.Height); };
        kBron.Controls.Add(paneel);
        // bij een klik meldt de lijst "selectie gewijzigd" soms vóórdat de huidige regel is bijgewerkt: op beide reageren
        grid.SelectionChanged += (s, e) => ToonSubmappen();
        grid.CurrentCellChanged += (s, e) => ToonSubmappen();

        grid.CellPainting += TekenVoortgang;
        grid.CellFormatting += (s, e) =>
        {
            // lege doelmap tonen als uitleg (alleen weergave; de waarde blijft leeg)
            if (e.RowIndex < 0 || grid.Columns[e.ColumnIndex].Name != "doel" || !string.IsNullOrEmpty(e.Value as string)) return;
            var cc = grid.CurrentCell;
            if (grid.IsCurrentCellInEditMode && cc?.RowIndex == e.RowIndex && cc.ColumnIndex == e.ColumnIndex) return;
            e.Value = Hoofdmap; e.CellStyle!.ForeColor = Zacht; e.CellStyle.SelectionForeColor = Zacht; e.FormattingApplied = true;
        };
        grid.EditingControlShowing += (s, e) => { if (e.Control is { Text: Hoofdmap } c) c.Text = ""; };
        grid.CurrentCellDirtyStateChanged += (s, e) =>
        {
            if (grid.IsCurrentCellDirty && grid.CurrentCell?.OwningColumn?.Name == "aan") grid.CommitEdit(DataGridViewDataErrorContexts.Commit);
        };
        grid.CellEndEdit += (s, e) =>
        {
            var rij = grid.Rows[e.RowIndex]; var info = (RijInfo)rij.Tag!;
            switch (grid.Columns[e.ColumnIndex].Name)
            {
                case "bron":
                    var nieuw = (rij.Cells["bron"].Value as string ?? "").Trim().Trim('"');
                    bool anders = !string.Equals(nieuw, info.Bron, StringComparison.OrdinalIgnoreCase);
                    if (anders) { info.Overslaan.Clear(); info.Mappen = 0; }   // andere bron: oude keuzes gelden niet meer
                    info.Bron = nieuw; rij.Cells["bron"].Value = nieuw; ControleerRij(rij);
                    if (anders) ToonSubmappen();
                    break;
                case "naam":
                    ToonSubmappen();
                    break;
                case "doel":
                    var d = (rij.Cells["doel"].Value as string ?? "").Trim().Trim('\\').Replace(":", "");
                    rij.Cells["doel"].Value = d == Hoofdmap ? "" : d;
                    break;
            }
            ToonVrij();
        };
        grid.CellDoubleClick += (s, e) => { if (e.RowIndex >= 0 && !grid.ReadOnly) grid.BeginEdit(true); };

        bMap.Click += (s, e) =>
        {
            using var d = new FolderBrowserDialog
            {
                Description = "Kies een of meer mappen om te backuppen (Ctrl of Shift voor meer tegelijk)", UseDescriptionForTitle = true,
                ShowNewFolderButton = false, Multiselect = true,
            };
            if (d.ShowDialog(this) != DialogResult.OK) return;
            int laatste = -1;
            foreach (var p in d.SelectedPaths)
            {
                if (IsSchijf(p)) { VoegSchijfToe(p); continue; }
                var n = Path.GetFileName(p.TrimEnd('\\'));
                laatste = VoegRijToe(true, n, p, n);
            }
            if (laatste >= 0) grid.CurrentCell = grid.Rows[laatste].Cells["naam"];
            ToonSubmappen();
        };
        bSchijf.Click += (s, e) =>
        {
            menuSchijf.Items.Clear();
            foreach (var d in DriveInfo.GetDrives())
            {
                string t;
                try { if (!d.IsReady) continue; t = $"{d.Name}   {d.VolumeLabel}   ({Opmaak.Schijf(d.TotalSize - d.AvailableFreeSpace)} in gebruik)"; }
                catch { continue; }
                var naam = d.Name;
                menuSchijf.Items.Add(t, null, (s2, e2) => VoegSchijfToe(naam));
            }
            menuSchijf.Show(bSchijf, 0, bSchijf.Height);
        };
        bVerwijder.Click += (s, e) =>
        {
            if (grid.CurrentRow is not { } rij) return;
            if (MessageBox.Show(this, $"'{rij.Cells["naam"].Value}' uit de lijst halen?\n(Er wordt niets gewist, alleen de regel verdwijnt.)",
                    "Verwijderen", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes) grid.Rows.Remove(rij);
        };
        bOp.Click += (s, e) => Verplaats(-1);
        bNeer.Click += (s, e) => Verplaats(1);
        bAllesAan.Click += (s, e) => { grid.EndEdit(); foreach (DataGridViewRow r in grid.Rows) r.Cells["aan"].Value = true; };
        bAllesUit.Click += (s, e) => { grid.EndEdit(); foreach (DataGridViewRow r in grid.Rows) r.Cells["aan"].Value = false; };
    }

    void BouwInstellingen(Panel kaart)
    {
        // twee even brede helften: links Waarheen, rechts Opties, met een dunne lijn ertussen
        kaart.Padding = new Padding(1);                          // de rand van de kaart zichtbaar laten
        var tabel = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, BackColor = Kaart, Margin = Padding.Empty };
        tabel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        tabel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        tabel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        // beginbreedtes waarop de posities hieronder zijn gemaakt; de tabel rekt ze daarna op tot de helft
        var k = new Panel { Dock = DockStyle.Fill, Margin = Padding.Empty, BackColor = Kaart, Size = new Size(P(592), P(300)) };
        var r = new Panel { Dock = DockStyle.Fill, Margin = Padding.Empty, BackColor = Kaart, Size = new Size(P(620), P(300)) };
        k.Paint += (s, e) => { using var pen = new Pen(KnopRand); e.Graphics.DrawLine(pen, k.Width - 1, P(14), k.Width - 1, k.Height - P(14)); };

        Lbl("Waarheen?", 18, 14, KaartKop, k);
        cbDoel = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Location = new Point(P(20), P(46)), Width = P(440), Font = Gewoon };
        bVernieuw = Knop("Vernieuwen", 104); bVernieuw.Location = new Point(P(468), P(44));
        Lbl("Submap", 20, 92, Gewoon, k);
        tbSubmap = new TextBox { Location = new Point(P(112), P(89)), Width = P(372) };
        var bKiesSub = Knop("Kies...", 80); bKiesSub.Location = new Point(P(492), P(86));
        tt.SetToolTip(bKiesSub, "Een map op de doelschijf kiezen (of maken) waarin de backup komt");
        k.Controls.Add(bKiesSub);
        bKiesSub.Click += (s, e) => KiesSubmap();
        vrijBalk = new Panel { Location = new Point(P(20), P(126)), Size = new Size(P(552), P(8)) };
        vrijBalk.Paint += (s, e) =>
        {
            e.Graphics.Clear(Om);
            int w = (int)(vrijBalk.Width * _vrijDeel);
            if (w > 0) { using var b = new SolidBrush(_vrijDeel > 0.9 ? Rood : Blauw); e.Graphics.FillRectangle(b, 0, 0, w, vrijBalk.Height); }
        };
        lVrij = Lbl("", 18, 138, Klein, k); lVrij.ForeColor = Zacht;
        lVrij.AutoSize = false; lVrij.AutoEllipsis = true; lVrij.Size = new Size(P(556), P(22));
        Lbl("Logmap", 20, 184, Gewoon, k);
        tbLog = new TextBox { Location = new Point(P(112), P(181)), Width = P(290) };
        bKiesLog = Knop("Kies...", 80); bKiesLog.Location = new Point(P(408), P(178));
        bOpenLog = Knop("Openen", 80); bOpenLog.Location = new Point(P(492), P(178));
        var logUitleg = Lbl("Elke backup krijgt een eigen logbestand met datum en tijd in de naam.", 110, 218, Klein, k);
        logUitleg.ForeColor = Zacht;
        tt.SetToolTip(tbLog, "Map waarin de logbestanden komen, bijv. 'backup Standaard 2026-11-04 02.00.15.txt'");
        tt.SetToolTip(bOpenLog, "Het nieuwste logbestand openen");
        k.Controls.AddRange(new Control[] { cbDoel, bVernieuw, tbSubmap, vrijBalk, tbLog, bKiesLog, bOpenLog });

        // rechterhelft: posities ten opzichte van die helft
        const int xo = 24, xo2 = xo + 300;
        Lbl("Opties", xo - 2, 14, KaartKop, r);
        T Keuze<T>(string tekst, int x, int y, string tip) where T : ButtonBase, new()
        {
            var c = new T { Text = tekst, AutoSize = true, Location = new Point(P(x), P(y)), ForeColor = Tekst };
            if (tip.Length > 0) tt.SetToolTip(c, tip);
            r.Controls.Add(c);
            return c;
        }
        rbBij = Keuze<RadioButton>("Bijwerken: nieuwe en gewijzigde bestanden kopiëren", xo, 42,
            "Kopieert wat nieuw of gewijzigd is. Een bestand dat op de backup nieuwer is, blijft staan. Er wordt niets gewist.");
        rbSpiegel = Keuze<RadioButton>("Spiegelen: ook wissen wat niet meer in de bron staat", xo, 66,
            "Let op: bestanden die op de backup staan maar niet meer in de bron, worden op de backup verwijderd.");
        rbSpiegel.ForeColor = Rood;
        cbAlle = Keuze<CheckBox>("Elk bestand tonen", xo, 98, "Elk bestand en elke map in de uitvoer tonen. Uit = alleen fouten en de samenvatting per map.");
        cbAfsluiten = Keuze<CheckBox>("Pc afsluiten als klaar", xo, 122, "De pc wordt 2 minuten na de backup afgesloten; dat kun je dan nog annuleren.");
        cbTijdelijk = Keuze<CheckBox>("Tijdelijke bestanden overslaan", xo, 146, "Slaat *.tmp, ~$* (Office-tijdelijk), Thumbs.db en .DS_Store over.");
        cbVerborgen = Keuze<CheckBox>("Verborgen bestanden overslaan", xo, 170, "Slaat bestanden met het kenmerk verborgen of systeem over.");
        cbMapDatum = Keuze<CheckBox>("Datums van mappen meekopiëren", xo2, 98, "Mappen op de backup krijgen dezelfde datum als in de bron.");
        cbFft = Keuze<CheckBox>("Tijdverschil FAT/exFAT/NAS negeren", xo2, 122,
            "Gebruik dit als de backupschijf exFAT of FAT32 is, of een NAS: die bewaren tijden op 2 seconden nauwkeurig. Anders worden bestanden telkens opnieuw gekopieerd.");
        cbDst = Keuze<CheckBox>("Zomertijdverschil negeren", xo2, 146, "Bestanden die precies 1 uur verschillen (zomer-/wintertijd) niet opnieuw kopiëren.");
        cbHerstart = Keuze<CheckBox>("Herstartbaar kopiëren (netwerk)", xo2, 170,
            "Grote bestanden over het netwerk gaan verder waar ze gebleven waren na een onderbreking. Iets trager.");
        cbControle = Keuze<CheckBox>("Kopie controleren (hash)", xo, 194,
            "Meteen na elk gekopieerd bestand worden de SHA-256-hashes van origineel en kopie vergeleken. Zeker, maar duurt langer (alles wordt nog eens gelezen).");
        cbPauzeAfwijking = Keuze<CheckBox>("Pauzeren als de controle afwijkt", xo + 20, 218,
            "Wijkt een kopie af, dan gaat de backup op pauze en zie je welk bestand het is. Met Hervatten wordt dat bestand opnieuw gekopieerd. " +
            "Uit = meteen opnieuw proberen (zie Pogingen). Bij een geplande backup wordt nooit gepauzeerd.");
        cbControle.CheckedChanged += (s, e) => cbPauzeAfwijking.Enabled = cbControle.Checked;
        cbSchaduw = Keuze<CheckBox>("Geopende bestanden kopiëren", xo2, 194,
            "Bestanden die in gebruik zijn (bijv. een open Outlook-.pst) worden uit een schaduwkopie (momentopname) van de schijf gekopieerd. " +
            "Vraagt beheerdersrechten; werkt niet voor netwerkschijven.");
        NumericUpDown Getal(string tekst, int x, int min, int max, string tip)
        {
            var l = Lbl(tekst, x, 255, Gewoon, r);
            var n = new NumericUpDown { Minimum = min, Maximum = max, Width = P(56), Location = new Point(l.Right + P(6), P(252)) };
            r.Controls.Add(n); tt.SetToolTip(l, tip); tt.SetToolTip(n, tip);
            return n;
        }
        nPogingen = Getal("Pogingen", xo, 0, 99, "Hoe vaak een mislukt bestand opnieuw wordt geprobeerd");
        nWacht = Getal("Wacht (s)", xo + 140, 0, 600, "Seconden wachten tussen pogingen");
        nThreads = Getal("Threads", xo + 290, 1, 32, "1 = één bestand tegelijk. Meer = sneller bij veel kleine bestanden.");

        // linkerhelft: velden rekken mee met de breedte, knoppen blijven rechts (zelf uitgerekend: betrouwbaarder dan
        // verankeren, want de helft is tijdens het opbouwen eerst nog heel smal)
        void Schik()
        {
            int rechts = k.Width - P(20);
            bVernieuw.Left = rechts - bVernieuw.Width; cbDoel.Width = Math.Max(P(100), bVernieuw.Left - P(8) - cbDoel.Left);
            bKiesSub.Left = rechts - bKiesSub.Width; tbSubmap.Width = Math.Max(P(60), bKiesSub.Left - P(8) - tbSubmap.Left);
            vrijBalk.Width = Math.Max(P(60), rechts - vrijBalk.Left); lVrij.Width = Math.Max(P(60), rechts - lVrij.Left);
            bOpenLog.Left = rechts - bOpenLog.Width; bKiesLog.Left = bOpenLog.Left - P(4) - bKiesLog.Width;
            tbLog.Width = Math.Max(P(60), bKiesLog.Left - P(6) - tbLog.Left);
        }
        k.Resize += (s, e) => Schik();
        tabel.Controls.Add(k, 0, 0); tabel.Controls.Add(r, 1, 0);
        kaart.Controls.Add(tabel);

        cbDoel.SelectedIndexChanged += (s, e) =>
        {
            if (_laden) return;
            int i = cbDoel.SelectedIndex;
            if (i >= 0 && _doelen[i] == "*")
            {
                var p = KiesMap("Kies de map waarin de backup komt", "");
                if (p is not null) VulDoelen(p);
                else { _laden = true; cbDoel.SelectedIndex = _vorigDoel; _laden = false; }
                return;
            }
            _vorigDoel = i; ToonVrij();
        };
        bVernieuw.Click += (s, e) =>
        {
            int i = cbDoel.SelectedIndex;
            VulDoelen(i >= 0 ? _doelen[i] : "");
            foreach (DataGridViewRow r in grid.Rows) ControleerRij(r);
        };
        tbSubmap.TextChanged += (s, e) => ToonVrij();
        bKiesLog.Click += (s, e) =>
        {
            var p = KiesMap("Kies de map voor de logbestanden", tbLog.Text.Trim());
            if (p is not null) tbLog.Text = p;
        };
        bOpenLog.Click += (s, e) => OpenLog();
    }

    // ================================================================ lijst met bronnen
    int VoegRijToe(bool aan, string naam, string bron, string doel, long mappen = 0, IEnumerable<string>? overslaan = null)
    {
        int i = grid.Rows.Add(aan, naam, bron, doel, "", "");
        grid.Rows[i].Tag = new RijInfo { Mappen = mappen, Overslaan = (overslaan ?? Array.Empty<string>()).Where(s => s.Length > 0).ToList(), Bron = bron };
        ControleerRij(grid.Rows[i]);
        // Rows.Add selecteert de nieuwe regel al vóórdat de gegevens erin staan: de mappenboom daarna opnieuw vullen
        if (grid.CurrentRow?.Index == i) { _paneelRij = null; ToonSubmappen(); }
        return i;
    }

    // hele schijf: lege doelmap = 1-op-1 in de hoofdmap van het doel (ook de losse bestanden in de root)
    void VoegSchijfToe(string pad)
    {
        var letter = pad[..1].ToUpperInvariant();
        int i = VoegRijToe(true, "Schijf " + letter, letter + ":\\", "");
        grid.CurrentCell = grid.Rows[i].Cells["naam"];
        ToonSubmappen();
    }

    void ZetStatus(DataGridViewRow rij, string sleutel)
    {
        var (kleur, tekst) = Status[sleutel];
        var c = rij.Cells["status"];
        c.Value = tekst.Length > 0 ? $"{Stip}  {tekst}" : "";
        c.Style.ForeColor = kleur; c.Style.SelectionForeColor = kleur;
    }

    void ControleerRij(DataGridViewRow rij)
    {
        var bron = rij.Cells["bron"].Value as string ?? "";
        ZetStatus(rij, bron.Length > 0 && !Directory.Exists(bron) ? "mist" : "");
    }

    void ZetBalkje(DataGridViewRow rij, double deel, string tekst, Color kleur)
    {
        var c = rij.Cells["voortgang"];
        c.Tag = new Balkje(deel, kleur); c.Value = tekst;
        grid.InvalidateCell(c);
    }

    void WisBalkje(DataGridViewRow rij)
    {
        var c = rij.Cells["voortgang"];
        c.Tag = null; c.Value = "";
        grid.InvalidateCell(c);
    }

    void Verplaats(int stap)
    {
        if (grid.CurrentRow is not { } rij) return;
        int i = rij.Index, j = i + stap;
        if (j < 0 || j >= grid.Rows.Count) return;
        grid.Rows.RemoveAt(i); grid.Rows.Insert(j, rij);
        grid.CurrentCell = grid.Rows[j].Cells["naam"];
    }

    // voortgangscel helemaal zelf tekenen: achtergrond (of selectiekleur), lijn onderaan, balkje met tekst
    void TekenVoortgang(object? s, DataGridViewCellPaintingEventArgs e)
    {
        if (e.RowIndex < 0 || grid.Columns[e.ColumnIndex].Name != "voortgang" || e.Graphics is null) return;
        e.Handled = true;
        var g = e.Graphics; var b = e.CellBounds;
        var staat = g.Save();
        var rij = grid.Rows[e.RowIndex];
        using (var achter = new SolidBrush(rij.Selected ? Selectie : Kaart)) g.FillRectangle(achter, b);
        using (var lijn = new Pen(Lijn)) g.DrawLine(lijn, b.Left, b.Bottom - 1, b.Right, b.Bottom - 1);

        if (rij.Cells[e.ColumnIndex].Tag is not Balkje t)
        {
            // geen backup bezig: laten zien of er submappen uit staan
            int n = (rij.Tag as RijInfo)?.Overslaan.Count ?? 0;
            if (n > 0)
            {
                using var sf = new StringFormat { LineAlignment = StringAlignment.Center, Trimming = StringTrimming.EllipsisCharacter };
                using var br = new SolidBrush(Oranje);
                g.DrawString($"{n} submap{(n == 1 ? "" : "pen")} overgeslagen", Klein, br, new RectangleF(b.X + P(8), b.Y, b.Width - P(12), b.Height), sf);
            }
            g.Restore(staat);
            return;
        }
        var tekst = e.Value as string ?? "";
        int h = P(20);
        var r = new Rectangle(b.X + P(6), b.Y + (b.Height - h) / 2, b.Width - P(16), h);
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        using (var spoor = new SolidBrush(Spoor)) using (var pad = Afgerond(r, P(5))) g.FillPath(spoor, pad);
        Rectangle? vol = null;
        if (t.Deel > 0)
        {
            int w = (int)Math.Max(P(10), r.Width * Math.Min(1.0, t.Deel));
            vol = new Rectangle(r.X, r.Y, w, r.Height);
            using var vul = new SolidBrush(t.Kleur); using var pad = Afgerond(vol.Value, P(5));
            g.FillPath(vul, pad);
        }
        using var sf2 = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center, Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap };
        using (var tb = new SolidBrush(Tekst)) g.DrawString(tekst, Kolom, tb, r, sf2);
        if (vol is { } v)                                    // tekst op de vulling in wit
        {
            g.SetClip(v, System.Drawing.Drawing2D.CombineMode.Intersect);
            g.DrawString(tekst, Kolom, Brushes.White, r, sf2);
        }
        g.Restore(staat);
    }

    string? KiesMap(string omschrijving, string start)
    {
        using var d = new FolderBrowserDialog { Description = omschrijving, UseDescriptionForTitle = true, ShowNewFolderButton = true };
        // InitialDirectory opent ín de map; SelectedPath zou de map erboven openen, en een schijf als H:\ heeft er geen
        // (dan toont Windows de laatst gebruikte plek)
        if (start.Length > 0 && Directory.Exists(start)) d.InitialDirectory = start;
        return d.ShowDialog(this) == DialogResult.OK ? d.SelectedPath : null;
    }

    // de mappenboom naast de lijst laten zien voor de geselecteerde bron; elke klik wordt meteen bewaard
    DataGridViewRow? _paneelRij;
    void ToonSubmappen()
    {
        // de geselecteerde regel (FullRowSelect); CurrentRow loopt bij een klik soms een stap achter
        var rij = grid.SelectedRows.Count > 0 ? grid.SelectedRows[0] : grid.CurrentRow;
        if (rij == _paneelRij && rij?.Tag is RijInfo i0 && string.Equals(i0.Bron, rij.Cells["bron"].Value as string, StringComparison.OrdinalIgnoreCase)
            && paneel.Tag as string == (rij.Cells["naam"].Value as string)) return;
        if (rij?.Tag is not RijInfo info) { _paneelRij = null; paneel.Toon(null, "", Array.Empty<string>(), null); return; }
        _paneelRij = rij;
        var bron = rij.Cells["bron"].Value as string ?? "";
        var naam = rij.Cells["naam"].Value as string ?? "";
        paneel.Tag = naam;
        paneel.Toon(bron.Length > 0 ? Kopieermotor.Normaal(bron) : null, naam, info.Overslaan, lijst =>
        {
            info.Overslaan = lijst;
            grid.InvalidateRow(rij.Index);
        });
    }

    // logbestanden: een map; oude profielen hebben nog een bestandsnaam, dan de map daarvan
    static string LogMap(string s)
    {
        s = s.Trim().Trim('"');
        var ext = Path.GetExtension(s);
        return ext.Equals(".txt", StringComparison.OrdinalIgnoreCase) || ext.Equals(".log", StringComparison.OrdinalIgnoreCase)
            ? Path.GetDirectoryName(s) ?? s : s;
    }

    // ================================================================ doel
    void VulDoelen(string gewenst)
    {
        _laden = true;
        cbDoel.Items.Clear(); _doelen.Clear();
        foreach (var d in DriveInfo.GetDrives())
        {
            if (d.DriveType is not (DriveType.Fixed or DriveType.Removable or DriveType.Network)) continue;
            string t;
            try { if (!d.IsReady) continue; t = $"{d.Name}    {d.VolumeLabel}    {Opmaak.Schijf(d.AvailableFreeSpace)} vrij van {Opmaak.Schijf(d.TotalSize)}"; }
            catch { continue; }
            cbDoel.Items.Add(t); _doelen.Add(d.Name);
        }
        if (gewenst.Length > 0 && !_doelen.Contains(gewenst, StringComparer.OrdinalIgnoreCase))
        {
            cbDoel.Items.Add(IsSchijf(gewenst) ? $"{gewenst}    (niet aangesloten)" : $"{gewenst}    (map)"); _doelen.Add(gewenst);
        }
        cbDoel.Items.Add("Andere map kiezen..."); _doelen.Add("*");
        int i = _doelen.FindIndex(x => string.Equals(x, gewenst, StringComparison.OrdinalIgnoreCase));
        cbDoel.SelectedIndex = i; _vorigDoel = i;
        _laden = false;
        ToonVrij();
    }

    // submap kiezen in een venster: alleen een map binnen het doel dat bij "Waarheen?" is gekozen
    void KiesSubmap()
    {
        int i = cbDoel.SelectedIndex;
        var wortel = i >= 0 && _doelen[i] != "*" ? _doelen[i] : "";
        if (wortel.Length == 0 || !Directory.Exists(wortel)) { Melding("Kies eerst bij 'Waarheen?' een doelschijf die aangesloten is."); return; }
        var start = DoelBasis();
        if (!Directory.Exists(start)) start = wortel;
        while (true)
        {
            var p = KiesMap($"Kies een map op {wortel} waarin de backup komt", start);
            if (p is null) return;
            var w = wortel.TrimEnd('\\') + "\\";
            if ((p.TrimEnd('\\') + "\\").StartsWith(w, StringComparison.OrdinalIgnoreCase))
            {
                tbSubmap.Text = p.Length > w.Length ? p[w.Length..].Trim('\\') : "";
                return;
            }
            // een map buiten de gekozen doelschijf: opnieuw laten kiezen
            if (MessageBox.Show(this, $"{p}\nligt niet op {wortel}.\n\nDe submap moet op de doelschijf staan die bij 'Waarheen?' is gekozen.",
                    "Submap", MessageBoxButtons.RetryCancel, MessageBoxIcon.Warning) != DialogResult.Retry) return;
            start = wortel;
        }
    }

    string DoelBasis()
    {
        int i = cbDoel.SelectedIndex;
        if (i < 0 || _doelen[i] == "*") return "";
        var s = tbSubmap.Text.Trim().Trim('\\');
        return s.Length > 0 ? Pad(_doelen[i], s) : _doelen[i];
    }

    string DoelVoor(DataGridViewRow rij)
    {
        var d = (rij.Cells["doel"].Value as string ?? "").Trim('\\');
        var b = DoelBasis();
        return d.Length > 0 ? Pad(b, d) : b;
    }

    void ToonVrij()
    {
        var b = DoelBasis();
        if (b.Length == 0) { _vrijDeel = 0; vrijBalk.Invalidate(); lVrij.ForeColor = Oranje; lVrij.Text = "Kies een doelschijf of -map"; return; }
        var wortel = Path.GetPathRoot(b) ?? b;
        try
        {
            var d = new DriveInfo(wortel);
            if (!d.IsReady) throw new IOException();
            _vrijDeel = 1 - d.AvailableFreeSpace / (double)d.TotalSize;
            lVrij.ForeColor = Zacht;
            lVrij.Text = $"{Opmaak.Schijf(d.AvailableFreeSpace)} vrij van {Opmaak.Schijf(d.TotalSize)}{Punt}Elke map komt in {Pad(b, "<doelmap>")}";
        }
        catch
        {
            _vrijDeel = 0; lVrij.ForeColor = Rood;
            lVrij.Text = $"Schijf {wortel} is niet gevonden. Sluit hem aan en klik op Vernieuwen.";
        }
        vrijBalk.Invalidate();
    }

    // het logbestand van de laatste run openen, anders het nieuwste in de logmap
    void OpenLog()
    {
        var p = _run?.Log is { Length: > 0 } l && File.Exists(l) ? l : null;
        var map = LogMap(tbLog.Text);
        if (p is null && Directory.Exists(map))
            p = new DirectoryInfo(map).EnumerateFiles("backup *.txt").OrderByDescending(f => f.LastWriteTimeUtc).FirstOrDefault()?.FullName;
        if (p is not null) Process.Start(new ProcessStartInfo("notepad.exe", $"\"{p}\"") { UseShellExecute = false });
        else MessageBox.Show(this, $"Er staat nog geen logbestand in:\n{map}\n\nBij elke backup komt er een bij.", "Logbestand",
            MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    // ================================================================ profielen
    void LaadProfiel(Profiel p)
    {
        _huidig = p;
        grid.Rows.Clear();
        foreach (var t in p.Taken) VoegRijToe(t.Aan, t.Naam, t.Bron, t.Doel, t.Mappen, t.Overslaan);
        tbSubmap.Text = p.Submap; tbLog.Text = LogMap(p.Log);
        if (p.Modus == "spiegelen") rbSpiegel.Checked = true; else rbBij.Checked = true;
        cbAlle.Checked = p.AlleRegels; cbAfsluiten.Checked = p.Afsluiten;
        cbTijdelijk.Checked = p.Tijdelijk; cbVerborgen.Checked = p.Verborgen; cbMapDatum.Checked = p.MapDatum;
        cbFft.Checked = p.Fft; cbDst.Checked = p.Dst; cbHerstart.Checked = p.Herstart;
        cbControle.Checked = p.Controleren; cbSchaduw.Checked = p.Schaduwkopie;
        cbPauzeAfwijking.Checked = p.PauzeBijAfwijking; cbPauzeAfwijking.Enabled = p.Controleren;
        nPogingen.Value = Math.Clamp(p.Pogingen, 0, 99); nWacht.Value = Math.Clamp(p.Wacht, 0, 600); nThreads.Value = Math.Clamp(p.Threads, 1, 32);
        VulDoelen(p.Doel);
        ToonPlan();
        _paneelRij = null; ToonSubmappen();
    }

    void BewaarProfiel()
    {
        var p = _huidig; if (p is null) return;
        grid.EndEdit();
        p.Taken = grid.Rows.Cast<DataGridViewRow>().Select(r =>
        {
            var info = (RijInfo)r.Tag!;
            return new Taak
            {
                Aan = r.Cells["aan"].Value is true, Naam = r.Cells["naam"].Value as string ?? "", Bron = r.Cells["bron"].Value as string ?? "",
                Doel = r.Cells["doel"].Value as string ?? "", Mappen = info.Mappen, Overslaan = info.Overslaan.ToList(),
            };
        }).ToList();
        int i = cbDoel.SelectedIndex;
        if (i >= 0 && _doelen[i] != "*") p.Doel = _doelen[i];
        p.Submap = tbSubmap.Text.Trim(); p.Log = LogMap(tbLog.Text);
        p.Controleren = cbControle.Checked; p.Schaduwkopie = cbSchaduw.Checked; p.PauzeBijAfwijking = cbPauzeAfwijking.Checked;
        p.Modus = rbSpiegel.Checked ? "spiegelen" : "bijwerken";
        p.AlleRegels = cbAlle.Checked; p.Afsluiten = cbAfsluiten.Checked;
        p.Tijdelijk = cbTijdelijk.Checked; p.Verborgen = cbVerborgen.Checked; p.MapDatum = cbMapDatum.Checked;
        p.Fft = cbFft.Checked; p.Dst = cbDst.Checked; p.Herstart = cbHerstart.Checked;
        p.Pogingen = (int)nPogingen.Value; p.Wacht = (int)nWacht.Value; p.Threads = (int)nThreads.Value;
        _data.Laatste = p.Naam;
        try { Profielen.Schrijf(_o.Profielen, _data); }
        catch (Exception ex) { lStatus.ForeColor = Rood; lStatus.Text = "Profiel niet bewaard: " + ex.Message; }
    }

    void VulProfielen(string naam)
    {
        _laden = true;
        cbProfiel.Items.Clear();
        foreach (var p in _data.Profielen) cbProfiel.Items.Add(p.Naam);
        cbProfiel.SelectedItem = naam;
        _laden = false;
    }

    void WisselProfiel()
    {
        if (_laden) return;
        BewaarProfiel();
        var p = _data.Profielen.FirstOrDefault(x => x.Naam == cbProfiel.SelectedItem as string);
        if (p is not null) { LaadProfiel(p); BewaarProfiel(); }
    }

    void NieuwProfiel()
    {
        var naam = InvoerVenster.Vraag(this, "Nieuw profiel", "Naam van het nieuwe profiel (het begint als kopie van het huidige):")?.Trim();
        if (string.IsNullOrEmpty(naam)) return;
        if (_data.Profielen.Any(p => p.Naam.Equals(naam, StringComparison.OrdinalIgnoreCase))) { Melding($"Er is al een profiel '{naam}'."); return; }
        BewaarProfiel();
        var kopie = _huidig!.Kopie(naam);
        kopie.Plan = new Planning();                             // inplannen geldt per profiel; de kopie staat nog niet in de Taakplanner
        _data.Profielen.Add(kopie);
        VulProfielen(naam); LaadProfiel(kopie); BewaarProfiel();
    }

    void VerwijderProfiel()
    {
        if (_data.Profielen.Count <= 1) { Melding("Het laatste profiel kan niet weg."); return; }
        var naam = _huidig!.Naam;
        if (MessageBox.Show(this, $"Profiel '{naam}' verwijderen?", "Verwijderen", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
        if (_huidig.Plan.Aan) Planner.Zet(naam, new Planning(), false);   // ook uit de Taakplanner halen
        _data.Profielen.Remove(_huidig);
        _huidig = null;
        VulProfielen(_data.Profielen[0].Naam); LaadProfiel(_data.Profielen[0]); BewaarProfiel();
    }

    // ================================================================ uitvoer en log
    void VoegToe(string tekst, Color kleur)
    {
        if (rtb.TextLength > 3_000_000)                       // niet eindeloos laten groeien
        {
            rtb.ReadOnly = false; rtb.Select(0, 1_500_000); rtb.SelectedText = "[... oudere regels weggelaten ...]\n"; rtb.ReadOnly = true;
        }
        rtb.SelectionStart = rtb.TextLength; rtb.SelectionLength = 0; rtb.SelectionColor = kleur;
        rtb.AppendText(tekst);
    }

    static Color KleurVan(Regelsoort s) => s switch { Regelsoort.Fout => Rood, Regelsoort.Kop => Blauw, Regelsoort.Goed => Groen, _ => Tekst };

    void Uitvoer(string regel, Regelsoort soort = Regelsoort.Gewoon)
    {
        VoegToe(regel + "\n", KleurVan(soort));
        rtb.ScrollToCaret();
    }

    // regels van de motor op het scherm zetten; hooguit $max per keer, zodat het venster blijft reageren
    void ToonRegels(int max)
    {
        if (_run is null) return;
        var stuk = new StringBuilder(); Color? kleur = null; int n = 0;
        while (n < max && _run.Regels.TryDequeue(out var r))
        {
            var k = KleurVan(r.Soort);
            if (kleur is { } kk && kk != k && stuk.Length > 0) { VoegToe(stuk.ToString(), kk); stuk.Clear(); }
            kleur = k; stuk.Append(r.Tekst).Append('\n'); n++;
        }
        if (stuk.Length > 0) { VoegToe(stuk.ToString(), kleur!.Value); rtb.ScrollToCaret(); }
    }

    void LogSchrijf(params string[] regels)
    {
        if (_run is null) return;
        try { lock (_logSlot) File.AppendAllText(_run.Log, string.Join("\r\n", regels) + "\r\n", new UTF8Encoding(false)); }
        catch (Exception ex)
        {
            if (Interlocked.Exchange(ref _run.LogFout, 1) == 0) _run.Regels.Enqueue(($"Kan niet naar het logbestand schrijven: {ex.Message}", Regelsoort.Fout));
        }
    }

    void LogKop(string tekst)
    {
        var t = $"{tekst}:   {DateTime.Now:dd-MM-yyyy HH:mm:ss}";
        var streep = new string('=', 49);
        LogSchrijf("", streep, t, streep);
        Uitvoer(t, Regelsoort.Kop);
    }

    // samenvatting van één map, in de opmaak van robocopy
    static string[] Samenvatting(Kopieermotor m, TimeSpan duur, int code)
    {
        static string Rij(string label, Telling t, Func<long, string> f) =>
            $"{label,12} :{f(t.Totaal),12}{f(t.Gekopieerd),12}{f(t.Overgeslagen),13}{f(t.Verschil),10}{f(t.Mislukt),10}{f(t.Extra),10}";
        var regels = new List<string>
        {
            new string('-', 78),
            "",
            $"{"",14}{"Totaal",12}{"Gekopieerd",12}{"Overgeslagen",13}{"Verschil",10}{"Mislukt",10}{"Extra",10}",
            Rij("Mappen", m.Mappen, Opmaak.Aantal),
            Rij("Bestanden", m.Bestanden, Opmaak.Aantal),
            Rij("Bytes", m.Bytes, b => Opmaak.Kort(b)),
            $"{"Tijd",12} :{Opmaak.Tijd(duur),12}",
        };
        if (m.Verwijderd > 0) regels.Add($"{"Gewist",12} :{Opmaak.Aantal(m.Verwijderd),12}");
        if (m.Gecontroleerd > 0) regels.Add($"{"Controle",12} :{Opmaak.Aantal(m.Gecontroleerd),12}   kopieën gelijk aan het origineel");
        if (m.ViaSchaduw > 0) regels.Add($"{"Schaduwkopie",12} :{Opmaak.Aantal(m.ViaSchaduw),12}   bestanden in gebruik, uit de schaduwkopie");
        regels.Add($"{"Klaar",12} : {DateTime.Now.ToString("dddd d MMMM yyyy HH:mm:ss", Opmaak.NL)}");
        regels.Add("");
        regels.Add($"Resultaat {code} ({Opmaak.CodeTekst(code)})");
        return regels.ToArray();
    }

    // ================================================================ backup uitvoeren
    void ZetBezig(bool bezig)
    {
        kInst.Enabled = !bezig; balkBron.Enabled = !bezig; paneel.Enabled = !bezig;
        cbProfiel.Enabled = bNieuw.Enabled = bWeg.Enabled = bPlan.Enabled = !bezig;
        grid.ReadOnly = bezig;
        bProef.Enabled = !bezig; bPauze.Enabled = bezig; bPauze.Text = "Pauze";
        // tijdens een backup (of proefdraai) is de startknop de rode stopknop
        bStart.Text = !bezig ? "Backup starten" : _run?.Proef == true ? "Proefdraai stoppen" : "Backup stoppen";
        bStart.BackColor = bezig ? KnopRood : Blauw;
        bStart.FlatAppearance.MouseOverBackColor = bezig ? KnopRoodDonker : BlauwDonker;
        tt.SetToolTip(bStart, bezig ? "De backup stoppen; wat al gekopieerd is, blijft staan" : "");
    }

    void Melding(string tekst)
    {
        if (_o.Automatisch) _klok.ShowBalloonTip(15000, "Backup niet gestart", tekst, ToolTipIcon.Warning);
        MessageBox.Show(this, tekst, "Backup", MessageBoxButtons.OK, MessageBoxIcon.Warning);
    }

    // geeft false als de backup niet is begonnen (iets klopte niet, of de gebruiker zei nee)
    async Task<bool> StartRun(bool proef)
    {
        if (_run is { Klaar: false }) return false;
        grid.EndEdit();
        var taken = grid.Rows.Cast<DataGridViewRow>().Where(r => r.Cells["aan"].Value is true).ToList();
        if (taken.Count == 0) { Melding("Er staat niets aangevinkt om te backuppen."); return false; }
        var basis = DoelBasis();
        if (basis.Length == 0) { Melding("Kies eerst waar de backup heen moet."); return false; }
        var wortel = Path.GetPathRoot(basis) ?? basis;
        if (!Directory.Exists(wortel)) { Melding($"Doelschijf {wortel} is niet gevonden."); return false; }
        foreach (var r in taken)
        {
            var bronTekst = r.Cells["bron"].Value as string ?? "";
            if (bronTekst.Length == 0) { Melding($"'{r.Cells["naam"].Value}' heeft geen bron."); return false; }
            var b = bronTekst.TrimEnd('\\') + "\\"; var d = DoelVoor(r).TrimEnd('\\') + "\\";
            if (d.StartsWith(b, StringComparison.OrdinalIgnoreCase) || b.StartsWith(d, StringComparison.OrdinalIgnoreCase))
            { Melding($"'{r.Cells["naam"].Value}': bron en doel liggen in elkaar ({b} en {d}). Kies een ander doel."); return false; }
        }
        // elke run een eigen logbestand met datum en tijd in de naam
        var logMap = LogMap(tbLog.Text);
        if (logMap.Length == 0) { Melding("Kies eerst een map voor de logbestanden."); return false; }
        try { Directory.CreateDirectory(logMap); }
        catch { Melding($"De map voor de logbestanden kan niet gemaakt worden:\n{logMap}\n\nKies een andere map."); return false; }
        var veiligeNaam = string.Concat((_huidig?.Naam ?? "backup").Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));
        var log = Path.Combine(logMap, $"backup {veiligeNaam} {DateTime.Now:yyyy-MM-dd HH.mm.ss}{(proef ? " proefdraai" : "")}.txt");

        if (rbSpiegel.Checked && !proef && !_o.Automatisch)
        {
            var tekst = "Spiegelen wist op de backup alles wat niet (meer) in de bron staat.";
            var recht = taken.FirstOrDefault(r => (r.Cells["doel"].Value as string ?? "").Trim('\\').Length == 0);
            if (recht is not null)
                tekst += $"\n\nLet op: '{recht.Cells["naam"].Value}' gaat rechtstreeks naar {basis}. Alles op {basis} dat niet op die bron staat (ook andere backups daar) wordt dan gewist.";
            if (MessageBox.Show(this, tekst + "\n\nWeet je het zeker? Tip: doe eerst een proefdraai.", "Spiegelen",
                    MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes) return false;
        }
        // geopende bestanden kopiëren kan alleen met beheerdersrechten: dan als beheerder opnieuw starten
        bool schaduw = cbSchaduw.Checked && !proef;
        if (schaduw && !Schaduwkopieen.IsBeheerder && !_o.Automatisch)
        {
            var a = MessageBox.Show(this, "'Geopende bestanden kopiëren' vraagt beheerdersrechten.\n\n" +
                "Ja = Backup opnieuw starten als beheerder en de backup daar beginnen.\nNee = nu doorgaan zonder; bestanden in gebruik worden dan overgeslagen.",
                "Beheerdersrechten", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
            if (a == DialogResult.Cancel) return false;
            if (a == DialogResult.Yes)
            {
                BewaarProfiel();
                try
                {
                    var psi = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = true, Verb = "runas" };
                    foreach (var x in new[] { "--profielen", _o.Profielen, "--profiel", _huidig!.Naam, "--start", "backup" }) psi.ArgumentList.Add(x);
                    Process.Start(psi);
                    _sluitZonderVragen = true; Close();
                }
                catch (System.ComponentModel.Win32Exception) { }                 // gebruiker zei nee op de UAC-vraag
                return false;
            }
            schaduw = false;
        }
        if (schaduw && !Schaduwkopieen.IsBeheerder) schaduw = false;         // automatisch zonder rechten: zonder schaduwkopie
        try { Directory.CreateDirectory(basis); } catch { Melding($"Doelmap {basis} kan niet gemaakt worden."); return false; }
        BewaarProfiel();

        var run = _run = new Run { Proef = proef, Log = log, AlleRegels = cbAlle.Checked };
        if (schaduw) run.Schaduw = new Schaduwkopieen((t, s) => { run.Regels.Enqueue((t, s)); if (s == Regelsoort.Fout) LogSchrijf(t); });
        run.Taken.AddRange(taken);
        run.Klok.Start();
        foreach (DataGridViewRow r in grid.Rows) { WisBalkje(r); if (taken.Contains(r)) ZetStatus(r, "wacht"); else ControleerRij(r); }
        rtb.Clear(); balk.Value = 0;
        lStatus.ForeColor = Zacht; lStatus.Text = proef ? "Proefdraai: er wordt niets gekopieerd." : $"Backup naar {basis}";
        ZetBezig(true);
        LogKop(proef ? "PROEFDRAAI GESTART" : "BACKUP RUN GESTART");

        if (cbSchaduw.Checked && !proef && run.Schaduw is null)
            Uitvoer("Geopende bestanden kopiëren staat aan, maar Backup draait zonder beheerdersrechten: bestanden in gebruik worden overgeslagen.", Regelsoort.Fout);

        for (run.I = 0; run.I < run.Taken.Count && !run.Gestopt; run.I++)
            await VoerTaakUit(run, run.Taken[run.I]);

        if (run.Schaduw is { } sk) { lTaak.Text = "Schaduwkopieën opruimen..."; await Task.Run(sk.Dispose); ToonRegels(int.MaxValue); }
        KlaarRun(run);
        return true;
    }

    Instellingen MaakInstellingen(Run run, string bron, RijInfo info)
    {
        var i = new Instellingen
        {
            Spiegelen = rbSpiegel.Checked, Pogingen = (int)nPogingen.Value, WachtSec = (int)nWacht.Value, Threads = (int)nThreads.Value,
            MapDatum = cbMapDatum.Checked, Fft = cbFft.Checked, Dst = cbDst.Checked, Herstartbaar = cbHerstart.Checked,
            VerborgenOverslaan = cbVerborgen.Checked, Proef = run.Proef, ToonBestanden = cbAlle.Checked,
            Controleren = cbControle.Checked, Schaduwpad = run.Schaduw is { } s ? s.Pad : null,
            // bij een geplande backup zit er niemand achter de pc: dan niet pauzeren
            BijAfwijking = cbControle.Checked && cbPauzeAfwijking.Checked && !_o.Automatisch ? rel => BeginInvoke(() => BijAfwijking(run, rel)) : null,
            TestAfwijking = _o.TestAfwijking,
        };
        if (cbTijdelijk.Checked) i.SlaBestanden.AddRange(TijdelijkePatronen);
        if (IsSchijf(bron)) i.SlaMappen.AddRange(GeenMappen);
        foreach (var u in info.Overslaan) i.SlaMappen.Add(Pad(Kopieermotor.Normaal(bron), u));   // uitgevinkte submappen
        return i;
    }

    async Task VoerTaakUit(Run run, DataGridViewRow rij)
    {
        var info = (RijInfo)rij.Tag!;
        var naam = rij.Cells["naam"].Value as string ?? "";
        var bron = rij.Cells["bron"].Value as string ?? "";
        var doel = DoelVoor(rij);
        lTaak.Text = $"Taak {run.I + 1} van {run.Taken.Count}{Punt}{naam}";
        balk.Value = (int)(100.0 * run.I / run.Taken.Count);
        grid.FirstDisplayedScrollingRowIndex = Math.Max(0, rij.Index - 2);
        var kopRegel = $"--- [{run.I + 1}/{run.Taken.Count}] {naam} - gestart {DateTime.Now:dd-MM-yyyy HH:mm:ss} ---";
        Uitvoer(""); Uitvoer(kopRegel, Regelsoort.Kop);
        Uitvoer($"{bron}  ->  {doel}");
        if (!Directory.Exists(bron))
        {
            ZetStatus(rij, "mist");
            Uitvoer($"Bron niet gevonden: {bron}  -  overgeslagen", Regelsoort.Fout);
            LogSchrijf("", kopRegel, $"Overgeslagen: bron niet gevonden ({bron})");
            Noteer(run, naam, "overgeslagen: bron niet gevonden");
            return;
        }

        LogSchrijf("", kopRegel, $"{bron}  ->  {doel}");
        var motor = new Kopieermotor(bron, doel, MaakInstellingen(run, bron, info), (tekst, soort) =>
        {
            run.Regels.Enqueue((tekst, soort));
            if (soort == Regelsoort.Fout) { Interlocked.Increment(ref run.Fouten); LogSchrijf(tekst); }
        }, run.Stop.Token);
        run.Motor = motor;
        if (run.Pauze) motor.Pauzeer(true);
        ZetStatus(rij, run.Pauze ? "pauze" : "bezig");
        run.KopieerKlok.Reset();
        var start = DateTime.Now;

        var taak = Task.Run(motor.Uitvoeren);
        while (!taak.IsCompleted)
        {
            await Task.WhenAny(taak, Task.Delay(200));
            ToonVoortgang(run, rij, motor);
            ToonRegels(4000);
        }
        int code = await taak;
        // na Stoppen de rest van de regels niet meer tonen (dat kan lang duren); anders alles
        if (run.Gestopt) run.Regels.Clear(); else ToonRegels(int.MaxValue);
        run.KopieerKlok.Stop();
        var duur = DateTime.Now - start;

        if (run.Gestopt)
        {
            ZetBalkje(rij, (rij.Cells["voortgang"].Tag as Balkje)?.Deel ?? -1, "gestopt", BalkGrijs);
            ZetStatus(rij, "gestopt");
            LogSchrijf("Gestopt door de gebruiker.");
            Noteer(run, naam, "gestopt");
            return;
        }
        var sleutel = code >= 16 ? "mislukt" : code >= 8 ? "fouten" : run.Proef ? "proef" : "klaar";
        ZetStatus(rij, sleutel);
        Uitvoer($"Resultaat {code} ({Opmaak.CodeTekst(code)})", code >= 8 ? Regelsoort.Fout : Regelsoort.Goed);
        LogSchrijf(Samenvatting(motor, duur, code));
        if (code < 16) info.Mappen = motor.Mappen.Totaal;                 // voor het percentage bij de volgende keer
        run.Overzicht.Add(new OverzichtRegel
        {
            Naam = naam, Gekopieerd = motor.Bestanden.Gekopieerd, Bytes = motor.Bytes.Gekopieerd, Overgeslagen = motor.Bestanden.Overgeslagen,
            Mislukt = motor.Bestanden.Mislukt + motor.Mappen.Mislukt, ExtraBest = motor.Bestanden.Extra, ExtraMap = motor.Mappen.Extra,
            Gewist = motor.Verwijderd, Opmerking = code >= 16 ? "mislukt" : "", Gecontroleerd = motor.Gecontroleerd, ViaSchaduw = motor.ViaSchaduw,
        });
        var kleur = sleutel switch { "mislukt" => BalkRood, "fouten" => BalkOranje, _ => BalkGroen };
        ZetBalkje(rij, 1, motor.Bytes.Gekopieerd > 0 && !run.Proef ? $"100%{Punt}{Opmaak.Kort(motor.Bytes.Gekopieerd)}" : "100%", kleur);
    }

    void ToonVoortgang(Run run, DataGridViewRow rij, Kopieermotor m)
    {
        var info = (RijInfo)rij.Tag!;
        double deelTaak = 0;
        string tijd = Opmaak.Tijd(run.Klok.Elapsed);
        if (m.Fase == 0)
        {
            long n = m.GescandeMappen;
            if (info.Mappen > 0) ZetBalkje(rij, Math.Min(0.99, n / (double)info.Mappen), $"vergelijken {Math.Min(99, 100 * n / info.Mappen)}%", BalkGrijs);
            else ZetBalkje(rij, -1, $"vergelijken{Punt}{Opmaak.Mappen(n)}", BalkGrijs);
        }
        else
        {
            if (!run.KopieerKlok.IsRunning && !run.Pauze) run.KopieerKlok.Start();
            long te = m.TeKopierenBytes, gedaan = m.GekopieerdeBytes;
            deelTaak = te > 0 ? Math.Min(1.0, gedaan / (double)te) : 1;
            var tekst = te > 0 ? $"{Math.Floor(deelTaak * 100)}%{Punt}{Opmaak.Kort(gedaan)} van {Opmaak.Kort(te)}" : "afronden";
            ZetBalkje(rij, Math.Min(0.99, deelTaak), tekst, BalkGroen);
            // geschatte resterende tijd op basis van de snelheid tot nu toe
            var sec = run.KopieerKlok.Elapsed.TotalSeconds;
            if (te > 0 && gedaan > 0 && sec > 3 && gedaan < te)
                tijd += $"{Punt}nog ~{Opmaak.Tijd(TimeSpan.FromSeconds(sec * (te - gedaan) / gedaan))}{Punt}{Opmaak.Kort(gedaan / sec)}/s";
        }
        lTijd.Text = tijd + Punt + $"{run.Fouten} fout{(run.Fouten == 1 ? "" : "en")}" + (run.Pauze ? Punt + "gepauzeerd" : "");
        balk.Value = (int)Math.Min(100, 100.0 * (run.I + deelTaak) / run.Taken.Count);
        KlokTekst($"Backup {balk.Value}%{(run.Pauze ? " (gepauzeerd)" : "")} - {rij.Cells["naam"].Value}");
        var huidig = m.Huidig;
        if (huidig.Length > 0) lBestand.Text = huidig;
    }

    static void Noteer(Run run, string naam, string opmerking) =>
        run.Overzicht.Add(new OverzichtRegel { Naam = naam, Opmerking = opmerking });

    // de controle van een kopie wijkt af: de motor staat al stil; venster en log bijwerken en de gebruiker laten kiezen
    void BijAfwijking(Run run, string rel)
    {
        if (run.Klaar || run.Gestopt) return;
        ToonRegels(int.MaxValue);                                // eerst de regel met de hashes, dan de pauze
        ZetPauze(true);
        var tekst = $"Backup gepauzeerd: de kopie van {rel} wijkt af van het origineel. Klik op Hervatten om dit bestand opnieuw te kopiëren, of stop de backup.";
        Uitvoer(tekst, Regelsoort.Fout); LogSchrijf(tekst);
        lStatus.ForeColor = Rood; lStatus.Text = $"Gepauzeerd: controle van {rel} wijkt af";
        _klok.ShowBalloonTip(15000, "Backup gepauzeerd", $"De kopie van {rel} wijkt af van het origineel.", ToolTipIcon.Warning);
        System.Media.SystemSounds.Exclamation.Play();
        if (_o.TestAfwijking is not null && _o.Afbeelding is not null)   // test: plaatje van de pauze, dan hervatten
            Na(1.5, () => { Schermafdruk(this, _o.Afbeelding.Replace(".png", "-afwijking.png")); ZetPauze(false); });
    }

    void ZetPauze(bool aan)
    {
        var run = _run; if (run is null || run.Klaar || run.Pauze == aan) return;
        run.Pauze = aan;
        run.Motor?.Pauzeer(aan);
        var rij = run.I < run.Taken.Count ? run.Taken[run.I] : null;
        var nu = DateTime.Now.ToString("HH:mm:ss");
        if (aan)
        {
            run.Klok.Stop(); run.KopieerKlok.Stop(); bPauze.Text = "Hervatten";
            if (rij is not null) ZetStatus(rij, "pauze");
            Uitvoer($"Gepauzeerd om {nu}", Regelsoort.Kop); LogSchrijf($"Gepauzeerd om {nu}");
        }
        else
        {
            run.Klok.Start(); bPauze.Text = "Pauze";
            if (rij is not null) ZetStatus(rij, "bezig");
            Uitvoer($"Hervat om {nu}", Regelsoort.Kop); LogSchrijf($"Hervat om {nu}");
        }
        lTijd.Text = Opmaak.Tijd(run.Klok.Elapsed) + (aan ? Punt + "gepauzeerd" : "");
    }

    void Stoppen()
    {
        var run = _run; if (run is null || run.Klaar) return;
        run.Gestopt = true;
        run.Pauze = false;
        run.Stop.Cancel();
        run.Motor?.Pauzeer(false);
        bPauze.Text = "Pauze";
        foreach (var r in run.Taken) if ((r.Cells["status"].Value as string ?? "").EndsWith("Wacht")) ZetStatus(r, "gestopt");
    }

    void KlaarRun(Run run)
    {
        run.Klaar = true; run.Klok.Stop();
        if (!run.Gestopt) balk.Value = 100;
        if (run.Gestopt)                                         // mappen die niet meer aan de beurt kwamen
            // de lus in StartRun heeft run.I al opgehoogd: vanaf run.I is niets meer gestart
            for (int j = run.I; j < run.Taken.Count; j++)
                Noteer(run, run.Taken[j].Cells["naam"].Value as string ?? "", "niet uitgevoerd (gestopt)");
        ToonOverzicht(run);
        LogKop(run.Gestopt ? "BACKUP RUN AFGEBROKEN" : run.Proef ? "PROEFDRAAI VOLTOOID" : "BACKUP RUN VOLTOOID");

        var tel = run.Taken.Select(r => (r.Cells["status"].Value as string ?? "").Replace(Stip, "").Trim())
            .GroupBy(s => s).Select(g => $"{g.Count()} {g.Key.ToLower()}");
        bool slecht = run.Gestopt || run.Taken.Any(r => (r.Cells["status"].Value as string ?? "") is var s
            && (s.Contains("Mislukt") || s.Contains("fouten") || s.Contains("Bron mist")));
        lStatus.ForeColor = slecht ? Oranje : Groen;
        lStatus.Text = $"{(run.Proef ? "Proefdraai" : "Backup")} {(run.Gestopt ? "gestopt" : "klaar")} na {Opmaak.Tijd(run.Klok.Elapsed)}{Punt}{string.Join(", ", tel)}";
        lTaak.Text = run.Gestopt ? "Gestopt" : run.Proef ? "Proefdraai klaar" : "Backup klaar";
        lTijd.Text = Opmaak.Tijd(run.Klok.Elapsed) + Punt + $"{run.Fouten} fout{(run.Fouten == 1 ? "" : "en")}";
        lBestand.Text = "Log: " + run.Log;
        ZetBezig(false);
        BewaarProfiel();                                         // aantal mappen per bron bewaren
        System.Media.SystemSounds.Asterisk.Play();
        KlokTekst("Backup");
        _klok.ShowBalloonTip(15000, lTaak.Text, lStatus.Text, slecht ? ToolTipIcon.Warning : ToolTipIcon.Info);

        if (_sluitNaStop) { Close(); return; }
        bool afsluiten = cbAfsluiten.Checked && !run.Proef && !run.Gestopt && _o.Afbeelding is null;
        if (afsluiten)
            Process.Start(new ProcessStartInfo("shutdown.exe", "/s /t 120 /c \"De backup is klaar. De pc wordt over 2 minuten afgesloten.\"") { UseShellExecute = false, CreateNoWindow = true });
        // automatisch gestart en niemand heeft het venster geopend: na de melding vanzelf sluiten (het log heeft alles)
        if (_o.Automatisch && !Visible) { Na(20, () => { _sluitZonderVragen = true; Close(); }); return; }
        var pop = new TotalenVenster(lTaak.Text, lStatus.Text, run.Overzicht, run.Proef, OpenLog) { Icon = Icon, TopMost = TopMost };
        pop.ToonBij(this);
        if (afsluiten && MessageBox.Show(this, "De backup is klaar. De pc wordt over 2 minuten afgesloten.\n\nKlik op Annuleren om dat tegen te houden.", "Afsluiten",
                MessageBoxButtons.OKCancel, MessageBoxIcon.Information) == DialogResult.Cancel)
            Process.Start(new ProcessStartInfo("shutdown.exe", "/a") { UseShellExecute = false, CreateNoWindow = true });
        if (_o.Afbeelding is not null)                           // test: plaatjes bewaren en sluiten
        {
            var t = new System.Windows.Forms.Timer { Interval = 900 };
            t.Tick += (s, e) =>
            {
                t.Stop();
                if (!pop.IsDisposed) Schermafdruk(pop, _o.Afbeelding.Replace(".png", "-totalen.png"));
                Schermafdruk(this, _o.Afbeelding);
                Close();
            };
            t.Start();
        }
    }

    // overzicht van de hele run, op het scherm en in het log
    void ToonOverzicht(Run run)
    {
        var o = run.Overzicht; if (o.Count == 0) return;
        var rijen = new List<string[]> { new[] { "Map", run.Proef ? "Zou kopiëren" : "Gekopieerd", "Overgeslagen", "Mislukt", "Extra op doel" } };
        foreach (var x in o)
        {
            if (x.Opmerking.Length > 0 && x.Gekopieerd == 0 && x.Overgeslagen == 0) { rijen.Add(new[] { x.Naam, x.Opmerking, "", "", "" }); continue; }
            rijen.Add(new[]
            {
                x.Naam, x.Gekopieerd > 0 ? $"{Opmaak.Bestanden(x.Gekopieerd)}, {Opmaak.Kort(x.Bytes)}" : "0 (niets veranderd)",
                Opmaak.Aantal(x.Overgeslagen), Opmaak.Aantal(x.Mislukt), Extra(x),
            });
        }
        rijen.Add(new[] { "Totaal", $"{Opmaak.Bestanden(o.Sum(x => x.Gekopieerd))}, {Opmaak.Kort(o.Sum(x => x.Bytes))}", "", Opmaak.Aantal(o.Sum(x => x.Mislukt)), "" });
        var breed = Enumerable.Range(0, 5).Select(c => rijen.Max(r => r[c].Length)).ToArray();
        var regels = rijen.Select(r => string.Join("   ", r.Select((v, c) => v.PadRight(breed[c]))).TrimEnd()).ToList();
        var streep = new string('-', breed.Sum() + 12);
        var alles = new List<string> { "OVERZICHT", streep, regels[0], streep };
        alles.AddRange(regels.Skip(1).Take(regels.Count - 2));
        alles.Add(streep); alles.Add(regels[^1]);
        Uitvoer("");
        foreach (var r in alles) Uitvoer(r, r == "OVERZICHT" ? Regelsoort.Kop : Regelsoort.Gewoon);
        LogSchrijf(new[] { "" }.Concat(alles).ToArray());
    }

    public static string Extra(OverzichtRegel x)
    {
        var d = new List<string>();
        if (x.ExtraBest > 0) d.Add(Opmaak.Bestanden(x.ExtraBest));
        if (x.ExtraMap > 0) d.Add(Opmaak.Mappen(x.ExtraMap));
        return d.Count > 0 ? string.Join(", ", d) : "-";
    }

    // ================================================================ openen en sluiten
    void BijTonen(object? s, EventArgs e)
    {
        if (_o.Afbeelding is not null) { TopMost = true; Activate(); }
        if (_o.TestSubmappen is not null && grid.Rows.Count > 0)              // test: submappen van de eerste regel uitvinken
        {
            grid.CurrentCell = grid.Rows[0].Cells["naam"]; ToonSubmappen();
            foreach (var u in _o.TestSubmappen.Split('|').Where(x => x.Length > 0 && x != "geen")) paneel.VinkUit(u);
        }
        if (_o.TestKlik >= 0 && _o.TestKlik < grid.Rows.Count)                // test: echte muisklik op een regel
            Na(0.5, () =>
            {
                var r = grid.GetCellDisplayRectangle(grid.Columns["naam"]!.Index, _o.TestKlik, false);
                nint xy = (r.Top + r.Height / 2) << 16 | (r.Left + 10);
                SendMessage(grid.Handle, 0x201, 1, xy); SendMessage(grid.Handle, 0x202, 0, xy);   // WM_LBUTTONDOWN / UP
            });
        if (_o.TestStop > 0) Na(_o.TestStop, Stoppen);
        if (_o.TestOver >= 0 && _o.Afbeelding is not null)               // test: het venster Over bij een onderdeel vastleggen
        {
            var v = new OverVenster(Icon) { TopMost = true };
            v.Show(this);
            Na(1.2, () =>
            {
                v.Spring(_o.TestOver);
                Na(0.5, () => { Schermafdruk(v, _o.Afbeelding.Replace(".png", $"-over{_o.TestOver}.png")); v.Close(); Close(); });
            });
            return;
        }
        if (_o.Automatisch)
        {
            // vanuit de Taakplanner: meteen naar het systeemvak en de backup starten
            WindowState = FormWindowState.Minimized;
            BeginInvoke(async () => { if (!await StartRun(false)) { _sluitZonderVragen = true; Close(); } });
        }
        else if (_o.Start is "proef" or "backup") BeginInvoke(async () => await StartRun(_o.Start == "proef"));
        else if (_o.Afbeelding is not null && _o.SluitNa == 0) Na(1.5, () => { Schermafdruk(this, _o.Afbeelding); Close(); });
        if (_o.SluitNa > 0) Na(_o.SluitNa, () => { if (_o.Afbeelding is not null) Schermafdruk(this, _o.Afbeelding); Close(); });
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    static extern nint SendMessage(nint hWnd, int msg, nint wParam, nint lParam);

    static void Na(double sec, Action actie)
    {
        var t = new System.Windows.Forms.Timer { Interval = (int)(sec * 1000) };
        t.Tick += (s, e) => { t.Stop(); t.Dispose(); actie(); };
        t.Start();
    }

    void BijSluiten(object? s, FormClosingEventArgs e)
    {
        if (_run is { Klaar: false })
        {
            if (_o.Afbeelding is null && !_sluitZonderVragen && MessageBox.Show(this, "De backup loopt nog. Stoppen en afsluiten?", "Backup",
                    MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) { e.Cancel = true; return; }
            // eerst netjes stoppen; als de motor klaar is, sluit KlaarRun het venster
            _sluitNaStop = true;
            Stoppen();
            e.Cancel = true;
            return;
        }
        try { BewaarProfiel(); } catch { }
        _klok.Visible = false; _klok.Dispose();                  // anders blijft er een spookicoon bij de klok staan
    }
}
