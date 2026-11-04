namespace BackupTool;

// Teksten voor het venster "Over". Per onderdeel een titel en een tekst.
// Opmaak in de tekst: een regel die begint met "## " is een tussenkop, "• " is een opsommingsteken.
public static class Uitleg
{
    public static readonly (string Titel, string Tekst)[] Onderdelen =
    {
        ("Over Backup", """
Backup kopieert mappen en hele schijven naar een backupschijf of -map. De tool heeft een eigen kopieermotor en heeft robocopy niet meer nodig; de regels zijn wel dezelfde als die van robocopy (getest: dezelfde uitkomst, bestand voor bestand).

## Wat staat waar
• Backup.exe: het programma zelf. Het icoon zit erin.
• backup-profielen.json: naast Backup.exe. Hierin staan al je profielen met hun bronnen, doel en opties. Alles wat je in het venster verandert, wordt hier automatisch in bewaard.
• Logbestanden: in de logmap die je kiest. Elke backup krijgt een eigen bestand.

## De opbouw van het venster
• Bovenaan: het profiel, met Nieuw, Verwijderen, Inplannen en Over.
• Links: "Wat wil je backuppen?", de lijst met bronnen.
• Rechts: de submappen van de bron die je in de lijst hebt aangeklikt.
• Midden: "Waarheen?" (doel en logmap) en de opties.
• Onder: de voortgang en de uitvoer van de lopende backup.
• Helemaal onder: Backup starten (tijdens een backup: Backup stoppen), Pauze, Proefdraai en Afsluiten.
"""),

        ("Profielen", """
Een profiel is een complete set instellingen: welke bronnen, welk doel, welke logmap en welke opties. Je kunt meerdere profielen hebben, bijvoorbeeld "Alles" en "Alleen muziek".

• Kiezen: met de keuzelijst bovenaan. Het vorige profiel wordt eerst bewaard.
• Nieuw: maakt een kopie van het huidige profiel onder een nieuwe naam. Een eventuele planning gaat niet mee; die stel je per profiel in.
• Verwijderen: haalt het profiel weg, en ook de geplande taak in de Taakplanner als die er was. Het laatste profiel kan niet weg.

Alles wordt automatisch bewaard: bij elke wijziging van profiel, bij het starten van een backup en bij het afsluiten. Een opslaan-knop is er daarom niet.
"""),

        ("Wat wil je backuppen?", """
De lijst met bronnen. Elke regel is een map of een hele schijf die gekopieerd wordt.

## Kolommen
• Vinkje: alleen aangevinkte regels gaan mee met de backup.
• Naam: alleen voor jezelf; komt terug in de uitvoer, het log en het overzicht. Dubbelklik of F2 om te wijzigen.
• Bron: de map of schijf die gekopieerd wordt. Te wijzigen met dubbelklik of F2. Bestaat de bron niet (bijvoorbeeld een schijf die niet is aangesloten), dan staat er bij Status "Bron mist" en wordt die regel bij de backup overgeslagen.
• Doelmap: de map op de backupschijf waarin deze bron terechtkomt, bijvoorbeeld "Documenten" geeft H:\Documenten. Leeg betekent: rechtstreeks in de hoofdmap van het doel, 1-op-1. Dat zie je als "(hoofdmap, 1-op-1)".
• Voortgang: tijdens een backup een balkje met percentage (zie "Voortgang"). Daarbuiten zie je hier hoeveel submappen er zijn uitgevinkt.
• Status: Wacht, Bezig, Gepauzeerd, Klaar, Klaar met fouten, Mislukt, Bron mist of Gestopt.

## Knoppen
• Map toevoegen: één of meer mappen kiezen. Houd Ctrl ingedrukt om er meer te kiezen (in het rechterdeel van het venster), of Shift voor een reeks. De doelmap krijgt dezelfde naam als de map.
• Hele schijf toevoegen: kies een schijf uit het menu. De schijf wordt 1-op-1 gekopieerd naar de hoofdmap van het doel: de mappen én de losse bestanden in de root. De systeemmappen $RECYCLE.BIN en System Volume Information gaan nooit mee.
• Verwijderen: haalt de regel uit de lijst. Er wordt niets gewist op schijf.
• Omhoog / Omlaag: de volgorde waarin de bronnen worden gekopieerd.
• Alles aan / Alles uit: alle vinkjes in de lijst aan of uit.

Wijzig je de bron van een regel, dan vervallen de uitgevinkte submappen van die regel (ze horen bij de oude bron).
"""),

        ("Submappen", """
Rechts naast de lijst staat de mappenboom van de bron die je in de lijst aanklikt. Hiermee kies je per bron welke submappen wél en niet mee moeten.

• Klik op + om een map open te klappen. Submappen worden pas ingelezen als je ze openklapt, zodat ook een hele schijf snel opent.
• Een map uitvinken: die map gaat met alles erin niet mee.
• Een map aanvinken die onder een uitgevinkte map ligt: de mappen erboven worden vanzelf weer aangevinkt (anders kan de backup er niet bij). Hun andere submappen blijven uit.
• Submappen aan / Submappen uit: alles in de boom aan of uit. De bron zelf blijft altijd aan.
• Losse bestanden die direct in de bron staan, gaan altijd mee.

Elke klik wordt meteen onthouden en bij het profiel bewaard. Als je de boom later opnieuw opent, worden de mappen boven elke uitgevinkte map vanzelf opengeklapt, zodat je je keuzes terugziet.

## Hoe het werkt
De backup kan mappen alleen overslaan, niet kiezen. Elke uitgevinkte map wordt daarom een uitzondering (met het volledige pad). Een uitgesloten map wordt ook bij Spiegelen nooit gewist op het doel.
"""),

        ("Waarheen?", """
Het doel van de backup.

• Doelschijf: de keuzelijst toont alle aangesloten schijven met hun naam en vrije ruimte. Met "Andere map kiezen..." kies je een map in plaats van een schijf. Een schijf die in het profiel staat maar niet is aangesloten, zie je als "(niet aangesloten)".
• Vernieuwen: de lijst met schijven opnieuw inlezen, bijvoorbeeld nadat je een USB-schijf hebt aangesloten. Ook de status "Bron mist" in de lijst wordt dan opnieuw bekeken.
• Submap: een map op de doelschijf waarin alles komt, bijvoorbeeld "Backups\Pc". Leeg = de hoofdmap van de schijf. Met Kies... kies je een map op de gekozen doelschijf; een map op een andere schijf wordt geweigerd.
• De blauwe balk toont hoe vol de doelschijf is (rood boven 90%). Daaronder staat waar elke map terechtkomt.

## Bron en doel in elkaar
Ligt het doel in de bron of de bron in het doel (bijvoorbeeld E:\ backuppen naar E:\Backup), dan weigert de tool te starten: anders zou de backup zichzelf kopiëren.
"""),

        ("Logbestanden", """
• Logmap: de map waarin de logbestanden komen. Met Kies... kies je de map; bestaat hij nog niet, dan wordt hij bij de eerste backup gemaakt.
• Elke backup krijgt een eigen bestand met profiel, datum en tijd in de naam, bijvoorbeeld "backup Standaard 2026-11-04 02.00.15.txt". Een proefdraai krijgt "proefdraai" achter de naam.
• Openen: opent het logbestand van de laatste backup, of anders het nieuwste in de logmap.

## Wat staat er in een logbestand
• Begin en einde van de backup, met datum en tijd.
• Per bron: een kopregel, van welke bron naar welk doel, alle fouten, en een samenvatting in de vorm van robocopy (Totaal, Gekopieerd, Overgeslagen, Verschil, Mislukt, Extra; voor mappen, bestanden en bytes), plus de duur, het aantal gewiste bestanden, gecontroleerde kopieën en bestanden uit een schaduwkopie.
• Pauze en hervatten, met tijdstip.
• Aan het eind een overzicht van alle bronnen en een totaal.
Elk bestand en elke map staat er niet in (dat zou het log enorm maken); die zie je op het scherm als "Elk bestand tonen" aan staat.
"""),

        ("Bijwerken of spiegelen", """
## Bijwerken (standaard)
Kopieert wat nieuw of gewijzigd is. Er wordt nooit iets gewist op het doel.
• Nieuw: staat in de bron maar nog niet op het doel: wordt gekopieerd.
• Nieuwer: de bron is nieuwer dan het doel: wordt gekopieerd.
• Gewijzigd: zelfde datum maar andere grootte: wordt gekopieerd.
• Ouder: het bestand op het doel is nieuwer dan de bron: blijft staan (net als robocopy /XO).
• Gelijk: zelfde grootte en zelfde wijzigingsdatum: wordt overgeslagen.
• Extra: staat op het doel maar niet (meer) in de bron: blijft staan, en wordt geteld als "extra".

## Spiegelen
Maakt het doel precies gelijk aan de bron (net als robocopy /MIR):
• Alles wat bij Bijwerken gekopieerd wordt, wordt ook hier gekopieerd, en daarnaast ook "Ouder": het doel wordt teruggezet naar de versie van de bron.
• Extra bestanden en mappen op het doel worden gewist.
• Uitgesloten bestanden en mappen (tijdelijke bestanden, uitgevinkte submappen) worden nooit gewist.
Let op: gaat een bron rechtstreeks naar de hoofdmap van een schijf (doelmap leeg), dan wist Spiegelen alles op die schijf dat niet in de bron staat, ook andere backups. De tool waarschuwt hiervoor voordat hij begint.
"""),

        ("Opties", """
## Elk bestand tonen
Elke gekopieerde, nieuwe, extra of gewiste regel verschijnt in de uitvoer onderin. Uit: alleen fouten en de samenvatting per bron. Het log bevat in beide gevallen alleen fouten en samenvattingen.

## Pc afsluiten als klaar
Na een backup (niet na een proefdraai of na Stoppen) wordt de pc over 2 minuten afgesloten. Je krijgt een melding met Annuleren om dat tegen te houden.

## Tijdelijke bestanden overslaan
Slaat *.tmp, ~$* (tijdelijke Office-bestanden), Thumbs.db en .DS_Store over. Ze tellen mee als "overgeslagen" en worden ook bij Spiegelen niet gewist.

## Verborgen bestanden overslaan
Slaat bestanden met het kenmerk Verborgen of Systeem over (alleen bestanden, geen mappen).

## Datums van mappen meekopiëren
Mappen op het doel krijgen dezelfde aanmaak- en wijzigingsdatum en dezelfde kenmerken als in de bron. Dit gebeurt aan het eind, omdat het kopiëren zelf de datum van een map verandert. De kenmerken van de hoofdmap van een schijf worden niet overgenomen (die is verborgen + systeem).

## Tijdverschil FAT/exFAT/NAS negeren
FAT32, exFAT en veel NAS-systemen bewaren tijden op 2 seconden nauwkeurig. Zonder deze optie lijkt elk bestand daarna "gewijzigd" en wordt alles telkens opnieuw gekopieerd. Met deze optie gelden tijden die hooguit 2 seconden verschillen als gelijk. Op NTFS-schijven niet nodig.

## Zomertijdverschil negeren
Sommige systemen (vooral FAT en oudere NAS) verschuiven tijden een uur bij zomer-/wintertijd. Met deze optie gelden bestanden die precies 1 uur verschillen (met 2 seconden marge) als gelijk.

## Herstartbaar kopiëren (netwerk)
Een groot bestand dat halverwege wordt onderbroken (bijvoorbeeld door een haperend netwerk), gaat bij een nieuwe poging verder waar het gebleven was in plaats van opnieuw te beginnen. Iets trager; vooral nuttig voor een NAS of netwerkschijf.

## Stoppen bij een fout
Met deze optie stopt de hele backup bij de eerste fout die definitief mislukt: een bestand dat na alle pogingen (zie hieronder) nog steeds niet lukt, of een map die niet te lezen, te maken of te wissen is. De bron waar het misging krijgt de status Mislukt, bronnen die nog niet aan de beurt waren staan in het overzicht als "niet uitgevoerd". Je krijgt een melding, ook bij de klok, en de fout staat in het log.
• Een afwijkende hash-controle telt hier niet als fout: die volgt de eigen regeling (pauzeren of opnieuw proberen).
• Een bron die niet gevonden wordt ("Bron mist") wordt gewoon overgeslagen.
• Uit (standaard): de fout wordt gemeld en de backup gaat door met de rest.

## Pogingen en Wacht
Lukt een bestand niet (bijvoorbeeld even in gebruik), dan wordt het zo vaak opnieuw geprobeerd als bij Pogingen staat, met het aantal seconden van Wacht ertussen. 0 pogingen = meteen opgeven en doorgaan met het volgende bestand.

## Threads
1 = één bestand tegelijk (aanbevolen voor gewone harde schijven). Meer = meerdere bestanden tegelijk; sneller bij veel kleine bestanden op een SSD of over het netwerk. Met meer dan 1 thread toont het balkje tijdens het vergelijken geen percentage.
"""),

        ("Controle (hash)", """
Met "Kopie controleren (hash)" wordt elk bestand meteen nadat het gekopieerd is gecontroleerd, dus niet pas aan het eind van de backup.

## Hoe het werkt
• Van het origineel en van de kopie wordt een SHA-256-hash berekend. Dat is een "vingerafdruk" van 64 tekens die volledig afhangt van de inhoud: verandert er ook maar één bit in het bestand, dan is de hash compleet anders.
• Zijn beide hashes gelijk, dan is de kopie gegarandeerd gelijk aan het origineel.
• Daarvoor worden beide bestanden helemaal gelezen, in blokken van 1 MB. Pauze en Stoppen werken ook tijdens het controleren.
• Het kost extra tijd: alles wat gekopieerd wordt, wordt daarna nog twee keer gelezen. Alleen gekopieerde bestanden worden gecontroleerd, niet de bestanden die al gelijk waren.
• In het log staat per bron hoeveel kopieën gecontroleerd en gelijk bevonden zijn, en in de pop-up met totalen staat "klaar, gecontroleerd".

## Als de controle afwijkt
In de uitvoer en het log komt een rode regel "CONTROLE WIJKT AF" met beide hashes. De kopie telt dan als mislukt en wordt opnieuw gemaakt:
• Met "Pauzeren als de controle afwijkt" aan: de backup gaat meteen op pauze. Je krijgt een melding (ook bij de klok) met het bestand. Met Hervatten wordt dat bestand opnieuw gekopieerd en opnieuw gecontroleerd; met Backup stoppen stop je de backup. Zo kun je eerst kijken wat er aan de hand is (een slechte schijf, een kabel, een bestand dat net veranderde).
• Met die optie uit: het bestand wordt meteen opnieuw geprobeerd, zo vaak als bij Pogingen staat. Lukt het dan nog niet, dan telt het als mislukt en gaat de backup door.
• Bij een geplande backup wordt nooit gepauzeerd (er zit dan niemand achter de pc); dan wordt opnieuw geprobeerd.

Een afwijking ontstaat in de praktijk zelden. Komt het vaker voor, controleer dan de backupschijf (bijvoorbeeld met chkdsk) en de kabel of USB-poort.
"""),

        ("Geopende bestanden kopiëren", """
Een bestand dat door een ander programma is geopend en vergrendeld (bijvoorbeeld een open Outlook-.pst of een database) kan normaal niet gelezen worden. Zonder deze optie meldt de backup dan "FOUT 32: in gebruik" en gaat hij door met het volgende bestand.

## Hoe het werkt
• Lukt een bestand niet omdat het in gebruik is, dan maakt de tool een schaduwkopie (Volume Shadow Copy) van die schijf: een momentopname waarin het bestand wél leesbaar is, zoals het op dat moment was.
• Per schijf wordt er hooguit één gemaakt, en alleen als het nodig is.
• Het bestand wordt uit die momentopname gekopieerd. In het log telt het mee bij "Schaduwkopie".
• Na de backup wordt de schaduwkopie weer gewist.

## Voorwaarden
• Beheerdersrechten. Start je een backup zonder, dan vraagt de tool of hij als beheerder opnieuw mag starten (Ja), of zonder deze optie door moet gaan (Nee).
• Werkt alleen voor schijven in de pc zelf, niet voor netwerkschijven.
• Bij een geplande backup moet de taak met beheerdersrechten draaien: plan dan in terwijl Backup als beheerder draait.
"""),

        ("Starten, pauze, stoppen, proefdraai", """
• Backup starten: begint met de aangevinkte bronnen, in de volgorde van de lijst. Eerst controleert de tool of alles klopt (iets aangevinkt, doel aangesloten, logmap te maken, bron en doel niet in elkaar). Bij Spiegelen vraagt hij eerst om bevestiging.
• Tijdens een backup heet dezelfde knop "Backup stoppen" (rood). Stoppen breekt de lopende kopie netjes af; wat al gekopieerd is, blijft staan. Bronnen die nog niet aan de beurt waren, staan in het overzicht als "niet uitgevoerd".
• Pauze: zet de backup stil, ook midden in een groot bestand. De knop heet dan "Hervatten". De tijd telt tijdens de pauze niet door.
• Proefdraai: doet alles behalve kopiëren en wissen. Je ziet wat er zou gebeuren en hoeveel; er verandert niets op het doel. Handig vóór een eerste backup of voordat je Spiegelen gebruikt.
• Afsluiten (rood): sluit de tool. Loopt er nog een backup, dan wordt eerst gevraagd of die moet stoppen.
"""),

        ("Voortgang", """
Per bron loopt een backup in twee stappen, en dat zie je in het balkje bij die regel:

## 1. Vergelijken (grijs balkje)
Bron en doel worden map voor map doorlopen om te bepalen wat er moet gebeuren. Het percentage is gebaseerd op het aantal mappen bij de vorige backup van deze bron. De eerste keer is dat nog onbekend; dan zie je hoeveel mappen er al bekeken zijn.

## 2. Kopiëren (groen balkje)
Nu is precies bekend hoeveel er gekopieerd moet worden, dus het percentage is exact: "64% · 1,2 GB van 1,9 GB". Rechtsboven bij de voortgang staan de verstreken tijd, de geschatte resterende tijd, de snelheid en het aantal fouten.

## Na afloop
• Groen 100%: klaar zonder fouten.
• Oranje: klaar, maar sommige bestanden lukten niet.
• Rood: mislukt (bijvoorbeeld de bron was niet leesbaar).
• Grijs: gestopt.
De grote balk onder de titel toont de voortgang van de hele backup (alle bronnen samen). Daaronder staat waar de motor nu mee bezig is.
"""),

        ("Na afloop", """
• Pop-up met totalen: per bron hoeveel er gekopieerd is (aantal en grootte), overgeslagen, mislukt en extra op het doel, met het resultaat, en een totaal. "Log openen" opent het logbestand van deze backup.
• Het overzicht staat ook onderaan in de uitvoer en in het logbestand.
• Een melding bij de klok.
• Het aantal mappen per bron wordt onthouden voor het percentage bij de volgende keer.

## Resultaatcodes
Per bron staat in het log een code, zoals bij robocopy. De getallen worden opgeteld:
• 0: niets te doen, alles was al bijgewerkt.
• 1: er zijn bestanden gekopieerd.
• 2: er staan extra bestanden of mappen op het doel.
• 4: er zijn verschillen gevonden (bijvoorbeeld een bestand in de bron is een map op het doel).
• 8: sommige bestanden zijn mislukt.
• 16: ernstige fout, er is niets gekopieerd (bijvoorbeeld de bron was niet leesbaar).
Code 3 betekent dus: gekopieerd (1) en extra's op het doel (2).
"""),

        ("Icoon bij de klok", """
Zolang de tool draait, staat er een icoon in het systeemvak naast de klok.

• Minimaliseren: het venster verdwijnt van de taakbalk en de tool draait door naast de klok.
• Dubbelklik op het icoon: het venster komt terug.
• Rechtermuisknop: Openen, Pauze/Hervatten, Stoppen en Afsluiten.
• Houd de muis op het icoon om de voortgang te zien, bijvoorbeeld "Backup 45% - Documenten".
• Na elke backup verschijnt een Windows-melding met het resultaat. Klik erop om het venster te openen. Ook bij een afwijkende controle krijg je een melding.
"""),

        ("Inplannen", """
Met "Inplannen..." laat je het huidige profiel automatisch starten via de Windows Taakplanner.

• Kies elke dag, of elke week op een dag, en een tijd.
• De taak heet "\Backup\<profielnaam>" in de Taakplanner. Naast de knop staat wanneer het profiel is ingepland.
• De backup start alleen als je bent aangemeld. Stond de pc op dat moment uit, dan wordt de backup ingehaald zodra het kan.
• Bij het starten gaat de tool meteen naar het systeemvak, maakt de backup, geeft een melding en sluit 20 seconden later vanzelf. Open je het venster tussendoor, dan blijft het open en zie je de pop-up met totalen.
• Er worden geen vragen gesteld (ook niet bij Spiegelen), en bij een afwijkende controle wordt niet gepauzeerd.
• Lukt het starten niet (bijvoorbeeld de doelschijf is niet aangesloten), dan krijg je een melding.
• Uitzetten: open Inplannen en haal het vinkje weg. Bij het verwijderen van een profiel wordt de taak ook verwijderd.
"""),

        ("Hoe de kopieermotor werkt", """
## Vergelijken
De motor loopt bron en doel map voor map door (op alfabet). Per bestand bepaalt hij of het nieuw, nieuwer, ouder, gewijzigd of gelijk is (zie "Bijwerken of spiegelen"). Twee bestanden zijn gelijk als grootte en wijzigingsdatum gelijk zijn, net als bij robocopy. Er wordt in deze stap nog niets veranderd.

## Uitvoeren
1. Bij Spiegelen: de extra bestanden en mappen op het doel wissen.
2. Nieuwe mappen maken.
3. Bestanden kopiëren met de kopieerfunctie van Windows zelf (dezelfde als in Verkenner). Kenmerken, wijzigingsdatum en aanmaakdatum gaan mee. Een alleen-lezen of verborgen bestand op het doel wordt gewoon overschreven.
4. Eventueel elke kopie controleren met een hash.
5. Tot slot de datums van de mappen zetten.

## Goed om te weten
• Koppelingen (junctions en symbolische koppelingen naar mappen) worden niet gevolgd, zodat de backup nooit in een eindeloze lus kan raken.
• Lange paden (meer dan 260 tekens) worden ondersteund.
• Mappen die Windows niet laat lezen (bijvoorbeeld WindowsApps), worden gemeld als fout en overgeslagen; de rest gaat door.
• De tellingen (Totaal, Gekopieerd, Overgeslagen, Verschil, Mislukt, Extra) worden geteld zoals robocopy dat doet.
"""),
    };
}
