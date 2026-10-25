# Minecraft Server Starter

von **[FGSeRaser](https://github.com/FGSeRaser)**

Windows-Tool, um einen eigenen Minecraft-Server ohne Texteditor und Kommandozeile aufzusetzen und zu betreiben.

## Funktionen

- **Server**: Serverordner und Server-Datei wählen, Arbeitsspeicher per Regler, optimierte Java-Flags (Aikar), EULA-Bestätigung, Java-Erkennung mit Versionsprüfung, Start/Stopp und eine eingebaute Konsole mit Befehlseingabe und Verlauf. Auf Wunsch wird eine `start.bat` erstellt.
- **Download**: Paper (über die offizielle PaperMC-API) oder Vanilla (über Mojangs Versions-Manifest) in jeder verfügbaren Version, inklusive Vorabversionen und Snapshots. Die Liste ist immer aktuell, es gibt keine fest eingetragenen Links mehr.
- **Plugins**: Installierte Plugins anzeigen, per Dialog oder Drag & Drop hinzufügen, deaktivieren (`.jar.disabled`) und in den Papierkorb verschieben. Plugins von Modrinth suchen und passend zur Minecraft-Version direkt installieren.
- **Einstellungen**: Alle Optionen der `server.properties` mit passendem Bedienelement (Schalter, Zahl, Auswahl, Text), gruppiert und durchsuchbar. Unbekannte Einträge, zum Beispiel aus neueren Versionen, bleiben erhalten und lassen sich ebenfalls bearbeiten.

## Voraussetzungen

- Windows 10 oder 11 mit .NET Framework 4.8
- Java 21 für Minecraft ab 1.20.5 (z. B. von [adoptium.net](https://adoptium.net)), Java 17 für 1.18 bis 1.20.4

## Bauen

`Minecraft Server Starter.sln` in Visual Studio 2022 öffnen und starten. Es werden keine NuGet-Pakete benötigt.

Die App-Einstellungen liegen unter `%AppData%\Minecraft Server Starter\settings.ini`.

## Autor

Entwickelt von **FGSeRaser**. Weitere Projekte gibt es auf GitHub: [github.com/FGSeRaser](https://github.com/FGSeRaser)

Im Programm findest du den Link unten in der Seitenleiste („von FGSeRaser“).
