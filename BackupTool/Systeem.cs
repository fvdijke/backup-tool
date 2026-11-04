using System.Diagnostics;
using System.Security;
using System.Security.Principal;
using System.Text;

namespace BackupTool;

// ================================================================ schaduwkopieën (Volume Shadow Copy)
// Een bestand dat in gebruik is (bijv. een open Outlook-.pst) kan niet worden gelezen. Een schaduwkopie is een
// momentopname van de hele schijf waarin dat bestand wel leesbaar is. Per schijf wordt er pas één gemaakt als het
// nodig is, en na de backup weer gewist. Vraagt beheerdersrechten; werkt niet voor netwerkschijven.
public sealed class Schaduwkopieen : IDisposable
{
    readonly Dictionary<string, (string Id, string Apparaat)?> _per = new(StringComparer.OrdinalIgnoreCase);
    readonly object _slot = new();
    readonly Action<string, Regelsoort> _meld;

    public Schaduwkopieen(Action<string, Regelsoort> meld) => _meld = meld;

    public static bool IsBeheerder =>
        new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator);

    // pad van hetzelfde bestand in de schaduwkopie van zijn schijf, of null
    public string? Pad(string bron)
    {
        var wortel = Path.GetPathRoot(bron);
        if (string.IsNullOrEmpty(wortel) || wortel.StartsWith(@"\\")) return null;
        lock (_slot)
        {
            if (!_per.TryGetValue(wortel, out var s)) { s = Maak(wortel); _per[wortel] = s; }
            return s is { } k ? k.Apparaat.TrimEnd('\\') + "\\" + bron[wortel.Length..] : null;
        }
    }

    (string, string)? Maak(string wortel)
    {
        _meld($"Schaduwkopie maken van {wortel} ...", Regelsoort.Kop);
        var (code, uit) = PowerShell(
            $"$r = ([wmiclass]'root\\cimv2:Win32_ShadowCopy').Create('{wortel}', 'ClientAccessible')\n" +
            "if ($r.ReturnValue -ne 0) { Write-Output \"FOUT $($r.ReturnValue)\"; exit 1 }\n" +
            "$s = Get-WmiObject Win32_ShadowCopy -Filter \"ID='$($r.ShadowID)'\"\n" +
            "Write-Output $s.ID; Write-Output $s.DeviceObject");
        var regels = uit.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (code != 0 || regels.Length < 2)
        {
            _meld($"FOUT: schaduwkopie van {wortel} lukt niet ({(regels.Length > 0 ? regels[0] : "code " + code)})" +
                  (IsBeheerder ? "" : "; start Backup als beheerder"), Regelsoort.Fout);
            return null;
        }
        _meld($"Schaduwkopie van {wortel} klaar", Regelsoort.Kop);
        return (regels[0], regels[1]);
    }

    public void Dispose()
    {
        lock (_slot)
            foreach (var (wortel, s) in _per)
                if (s is { } k)
                {
                    PowerShell($"Get-WmiObject Win32_ShadowCopy -Filter \"ID='{k.Id}'\" | ForEach-Object {{ $_.Delete() }}");
                    _meld($"Schaduwkopie van {wortel} gewist", Regelsoort.Kop);
                }
        _per.Clear();
    }

    static (int, string) PowerShell(string script)
    {
        var psi = new ProcessStartInfo("powershell.exe")
        {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true,
        };
        foreach (var a in new[] { "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-EncodedCommand",
                     Convert.ToBase64String(Encoding.Unicode.GetBytes(script)) }) psi.ArgumentList.Add(a);
        using var p = Process.Start(psi)!;
        var uit = p.StandardOutput.ReadToEnd(); p.StandardError.ReadToEnd();
        p.WaitForExit();
        return (p.ExitCode, uit);
    }
}

// ================================================================ inplannen in de Windows Taakplanner
// Per profiel één taak "\Backup\<profiel>" die Backup.exe start met --profiel <naam> --automatisch.
// Een gemiste keer (pc uit) wordt ingehaald zodra het kan.
public static class Planner
{
    public static readonly string[] Dagen = { "zondag", "maandag", "dinsdag", "woensdag", "donderdag", "vrijdag", "zaterdag" };
    static readonly string[] DagXml = { "Sunday", "Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday" };

    public static string TaakNaam(string profiel)
    {
        var veilig = new string(profiel.Select(c => "\\/:*?\"<>|".Contains(c) ? '_' : c).ToArray());
        return @"\Backup\" + veilig;
    }

    public static bool Bestaat(string profiel) => Schtasks(out _, "/Query", "/TN", TaakNaam(profiel)) == 0;

    // geeft null bij succes, anders de foutmelding
    public static string? Zet(string profiel, Planning plan, bool beheerder)
    {
        if (!plan.Aan) return Bestaat(profiel) && Schtasks(out var f, "/Delete", "/F", "/TN", TaakNaam(profiel)) != 0 ? f : null;
        if (!TimeSpan.TryParse(plan.Tijd, out var tijd)) return $"'{plan.Tijd}' is geen geldige tijd (bijv. 02:00).";
        var exe = Environment.ProcessPath!;
        var start = DateTime.Today.Add(tijd).ToString("yyyy-MM-ddTHH:mm:ss");
        var schema = plan.Frequentie == "dagelijks"
            ? "<ScheduleByDay><DaysInterval>1</DaysInterval></ScheduleByDay>"
            : $"<ScheduleByWeek><DaysOfWeek><{DagXml[Math.Clamp(plan.Dag, 0, 6)]} /></DaysOfWeek><WeeksInterval>1</WeeksInterval></ScheduleByWeek>";
        string X(string s) => SecurityElement.Escape(s);
        var xml = $"""
            <?xml version="1.0" encoding="UTF-16"?>
            <Task version="1.2" xmlns="http://schemas.microsoft.com/windows/2004/02/mit/task">
              <RegistrationInfo><Description>{X($"Backup-profiel '{profiel}' (gemaakt door Backup.exe)")}</Description></RegistrationInfo>
              <Triggers><CalendarTrigger><StartBoundary>{start}</StartBoundary><Enabled>true</Enabled>{schema}</CalendarTrigger></Triggers>
              <Principals><Principal id="Author"><LogonType>InteractiveToken</LogonType><RunLevel>{(beheerder ? "HighestAvailable" : "LeastPrivilege")}</RunLevel></Principal></Principals>
              <Settings>
                <MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>
                <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>
                <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>
                <StartWhenAvailable>true</StartWhenAvailable>
                <ExecutionTimeLimit>PT0S</ExecutionTimeLimit>
                <Enabled>true</Enabled>
              </Settings>
              <Actions Context="Author"><Exec>
                <Command>{X(exe)}</Command>
                <Arguments>{X($"--profiel \"{profiel}\" --automatisch")}</Arguments>
                <WorkingDirectory>{X(Path.GetDirectoryName(exe)!)}</WorkingDirectory>
              </Exec></Actions>
            </Task>
            """;
        var bestand = Path.Combine(Path.GetTempPath(), "backup-taak.xml");
        File.WriteAllText(bestand, xml, Encoding.Unicode);
        try { return Schtasks(out var fout, "/Create", "/F", "/TN", TaakNaam(profiel), "/XML", bestand) == 0 ? null : fout; }
        finally { File.Delete(bestand); }
    }

    public static string Omschrijving(Planning p) =>
        !p.Aan ? "Niet ingepland" : p.Frequentie == "dagelijks" ? $"Elke dag om {p.Tijd}" : $"Elke {Dagen[Math.Clamp(p.Dag, 0, 6)]} om {p.Tijd}";

    static int Schtasks(out string uitvoer, params string[] args)
    {
        var psi = new ProcessStartInfo("schtasks.exe")
        {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true,
            StandardOutputEncoding = Encoding.Default, StandardErrorEncoding = Encoding.Default,
        };
        foreach (var a in args) psi.ArgumentList.Add(a);
        using var p = Process.Start(psi)!;
        var uit = p.StandardOutput.ReadToEnd(); var fout = p.StandardError.ReadToEnd();
        p.WaitForExit();
        uitvoer = (fout.Length > 0 ? fout : uit).Trim();
        return p.ExitCode;
    }
}
