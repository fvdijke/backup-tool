namespace BackupTool;

// Backup: kopieert mappen en hele schijven naar een backupschijf, met een eigen kopieermotor (geen robocopy).
// Profielen staan in backup-profielen.json naast Backup.exe (of opgegeven met --profielen).
// Taakplanner:  --profiel <naam> --automatisch   (naar het systeemvak, backup maken, daarna sluiten)
// Testopties: --profielen <json> --afbeelding <png> --start proef|backup --sluitna <s> --teststop <s>
//             --testsubmappen "map1|map2" (submappenvenster van de eerste regel; "geen" = niets uitvinken)
public static class Program
{
    public sealed class Opties
    {
        public string Profielen = Path.Combine(AppContext.BaseDirectory, "backup-profielen.json");
        public string? Afbeelding, Start, TestSubmappen, Profiel, TestAfwijking;
        public bool Automatisch;                 // gestart door de Taakplanner: naar het systeemvak, backup, sluiten
        public double SluitNa, TestStop;
        public int TestKlik = -1, TestOver = -1;
    }

    static double Getal(string s) =>
        double.TryParse(s.Replace(',', '.'), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var d) ? d : 0;

    [STAThread]
    static void Main(string[] args)
    {
        var o = new Opties();
        o.Automatisch = args.Any(a => a.Equals("--automatisch", StringComparison.OrdinalIgnoreCase));
        for (int i = 0; i < args.Length - 1; i++)
            switch (args[i].ToLowerInvariant())
            {
                case "--profiel": o.Profiel = args[++i]; break;
                case "--profielen": o.Profielen = Path.GetFullPath(args[++i]); break;
                case "--afbeelding": o.Afbeelding = Path.GetFullPath(args[++i]); break;
                case "--start": o.Start = args[++i]; break;
                case "--sluitna": o.SluitNa = Getal(args[++i]); break;
                case "--teststop": o.TestStop = Getal(args[++i]); break;
                case "--testsubmappen": o.TestSubmappen = args[++i]; break;
                case "--testklik": o.TestKlik = (int)Getal(args[++i]); break;
                case "--testafwijking": o.TestAfwijking = args[++i]; break;
                case "--testover": o.TestOver = (int)Getal(args[++i]); break;
            }

        ApplicationConfiguration.Initialize();
        using (var g = Graphics.FromHwnd(IntPtr.Zero)) Stijl.Schaal = g.DpiX / 96f;
        Application.ThreadException += (s, e) => Fout(e.Exception);
        AppDomain.CurrentDomain.UnhandledException += (s, e) => Fout(e.ExceptionObject as Exception);
        Application.Run(new HoofdVenster(o));
    }

    static void Fout(Exception? ex) =>
        MessageBox.Show("Er ging iets mis:\n\n" + ex?.Message + "\n\n" + ex?.StackTrace, "Backup", MessageBoxButtons.OK, MessageBoxIcon.Error);
}
