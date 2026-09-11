# Space Remote

Knopf am Handy drücken → PC fährt per **Wake-on-LAN** hoch → sobald Windows läuft, siehst du den PC-Bildschirm auf dem Handy und steuerst ihn per Touch.

| Teil | Technik | Ordner |
|---|---|---|
| Windows-App (Tray) | C# / .NET 8, WinForms, SendInput, GDI-Capture | `windows/SpaceRemote` |
| Android-App | Kotlin, Jetpack Compose | `android` |
| Build | GitHub Actions (kein lokales Setup nötig) | `.github/workflows/build.yml` |

## Bauen

Einfach in ein GitHub-Repo pushen. Unter **Actions → Build → Artifacts** liegen danach:

- `SpaceRemote-Windows` → `SpaceRemote.exe` (eine Datei, .NET muss nicht installiert sein)
- `SpaceRemote-Android` → `SpaceRemote.apk`

Mit einem Tag wie `v1.0.0` wird zusätzlich automatisch ein GitHub-Release mit beiden Dateien erstellt.

## Einrichtung am PC (einmalig)

1. `SpaceRemote.exe` an einen festen Ort legen (z. B. `C:\Tools\SpaceRemote\`) und starten.
   SmartScreen meldet sich, weil die Datei nicht signiert ist → „Weitere Informationen“ → „Trotzdem ausführen“.
2. Beim ersten Start erscheinen **IP, Port, Passwort und MAC-Adresse**. Später jederzeit: Rechtsklick aufs Tray-Symbol → „Verbindungsdaten anzeigen“.
3. **Firewall:** Den Windows-Dialog mit „Zugriff zulassen“ (private Netzwerke) bestätigen, oder im Tray-Menü „Firewall freigeben (Admin)“ wählen.
   Dein Heimnetz muss unter *Einstellungen → Netzwerk → Eigenschaften* auf **Privat** stehen.
4. **Autostart** ist automatisch aktiv („Mit Windows starten“ im Tray-Menü).

### Wake-on-LAN aktivieren

Ohne diese Schritte kann das Handy den PC nicht einschalten – das ist Hardware/BIOS, keine App-Sache:

- **BIOS/UEFI:** „Wake on LAN“, „Power On by PCI-E“ oder „Resume by LAN“ aktivieren. „ErP“ / „Deep Sleep“ deaktivieren.
- **Geräte-Manager → Netzwerkadapter (Ethernet) → Eigenschaften**
  - *Energieverwaltung:* „Gerät kann den Computer aus dem Ruhezustand aktivieren“ + „Nur Magic Packet …“
  - *Erweitert:* „Wake on Magic Packet“ = Aktiviert, „Shutdown Wake-On-LAN“ = Aktiviert (falls vorhanden), „Energieeffizientes Ethernet / Green Ethernet“ = Deaktiviert
- **Schnellstart aus:** Systemsteuerung → Energieoptionen → „Auswählen, was beim Drücken des Netzschalters geschehen soll“ → „Schnellstart aktivieren“ abhaken.
- **LAN-Kabel** angeschlossen lassen. Über WLAN funktioniert Wake-on-LAN fast nie.
- **Laptops:** Netzteil dranlassen. Viele Laptops lassen sich nur aus dem *Energiesparmodus* wecken, nicht aus dem ausgeschalteten Zustand. Dann in der App über ⏻ → „Energiesparen“ statt „Herunterfahren“ verwenden.

### Damit sofort ein Bild kommt: automatische Anmeldung

Space Remote startet erst, wenn du in Windows angemeldet bist. Damit nach dem Hochfahren ohne Passworteingabe direkt das Bild erscheint:

1. `Win + R` → `netplwiz` → Haken bei „Benutzer müssen Benutzernamen und Kennwort eingeben“ entfernen → Kennwort bestätigen.
2. Fehlt der Haken: *Einstellungen → Konten → Anmeldeoptionen* → „Für mehr Sicherheit nur die Windows Hello-Anmeldung zulassen“ ausschalten und `netplwiz` erneut öffnen.

Achtung: Dann kann jeder, der den PC einschaltet, ihn benutzen.

## Einrichtung am Handy

1. `SpaceRemote.apk` installieren (Installation aus unbekannten Quellen erlauben).
2. IP-Adresse, Port, Passwort und die **MAC-Adresse des LAN-Adapters** eintragen.
3. Das Handy muss im **selben WLAN** sein wie der PC.

Tipp: Im Router dem PC eine feste IP geben (FRITZ!Box: *Heimnetz → Netzwerk → Gerät bearbeiten → „Immer die gleiche IPv4-Adresse zuweisen“*).

## Bedienung

| Geste | Aktion |
|---|---|
| Tippen | Linksklick |
| Zweimal tippen | Doppelklick |
| Lange drücken | Rechtsklick |
| Ziehen | Maus gedrückt ziehen (Fenster verschieben, markieren) |
| Zwei Finger hoch/runter | Scrollen |
| ⌨ | Handy-Tastatur ein/aus |
| Fn | Sondertasten (Esc, Win, Alt+Tab, Strg+C/V, Pfeile …) |
| ⏻ | Herunterfahren, Neustart, Energiesparen, Sperren |
| ✕ | Verbindung trennen |

## Feinabstimmung

`%AppData%\SpaceRemote\config.json` (Tray → „Einstellungsordner öffnen“), danach Space Remote neu starten:

```json
{
  "Port": 47800,
  "Password": "ABCD2345",
  "MaxWidth": 1280,
  "JpegQuality": 60,
  "Fps": 25
}
```

Ruckelt es: `MaxWidth` auf 960 oder `JpegQuality` auf 45. Zu unscharf: `MaxWidth` 1600–1920.

## Fehlerbehebung

- **„Falsches Passwort“** – exakt das Passwort aus „Verbindungsdaten anzeigen“ eintragen.
- **Wartet ewig, PC geht nicht an** – Wake-on-LAN-Schritte oben prüfen; MAC-Adresse vom *LAN*-Adapter verwenden, nicht WLAN.
- **PC ist an, aber keine Verbindung** – Firewall/Netzwerkprofil „Privat“ prüfen; läuft das Tray-Symbol?
- **„Kein Bild – PC gesperrt?“** – Sperrbildschirm und UAC-Dialoge kann eine normale App nicht aufnehmen.
- **Klicks in Admin-Fenstern (z. B. Task-Manager) wirken nicht** – Windows blockiert das; Space Remote dafür „Als Administrator ausführen“.
- **Schwarzes Bild bei Spielen im exklusiven Vollbild** – Spiel auf „Randloses Fenster“ stellen.

## Einschränkungen

- Nur im Heimnetz. Von unterwegs geht es nur per VPN (z. B. Tailscale) und einem Gerät im Heimnetz, das das Wake-on-LAN-Paket sendet.
- Nur der Hauptmonitor, kein Ton.

## Sicherheit

Das Passwort wird nie im Klartext übertragen (HMAC-SHA256 Challenge-Response), das **Bild aber unverschlüsselt**. Deshalb: nur im eigenen Netz nutzen und den Port **niemals** im Router freigeben.

---
Finanzinstitut
