Imports System.Globalization
Imports System.Text

Friend Enum PropertyKind
    Text
    Bool
    Int
    Choice
    Password
End Enum

''' <summary>Describes one known server.properties entry and how to edit it.</summary>
Friend Class PropertyDef
    Public Property Key As String
    Public Property Label As String
    Public Property Description As String = ""
    Public Property Category As String
    Public Property Kind As PropertyKind
    Public Property DefaultValue As String = ""
    Public Property Minimum As Long = 0
    Public Property Maximum As Long = Integer.MaxValue
    ''' <summary>For Choice: pairs of (stored value, display text).</summary>
    Public Property Choices As New List(Of KeyValuePair(Of String, String))()
End Class

''' <summary>Reads and writes server.properties (Java properties format).</summary>
Friend NotInheritable Class ServerProperties

    Private Sub New()
    End Sub

    Public Shared ReadOnly Categories As String() = {
        "Allgemein", "Welt", "Gameplay", "Spieler & Sicherheit", "Leistung", "Netzwerk & RCON", "Ressourcenpaket"
    }

    Public Const OtherCategory As String = "Weitere"

    Public Shared ReadOnly Definitions As List(Of PropertyDef) = BuildDefinitions()

    Public Shared Function FindDefinition(key As String) As PropertyDef
        Return Definitions.FirstOrDefault(Function(d) d.Key = key)
    End Function

    ' ---------- File access ----------

    ''' <summary>Loads all key/value pairs. Returns an empty dictionary if the file does not exist.</summary>
    Public Shared Function Load(filePath As String) As Dictionary(Of String, String)
        Dim result As New Dictionary(Of String, String)(StringComparer.Ordinal)
        If Not File.Exists(filePath) Then Return result

        ' Escaped files are pure ASCII; UTF-8 also covers files edited by hand
        Dim lines = File.ReadAllLines(filePath, Encoding.UTF8)
        Dim i = 0
        While i < lines.Length
            Dim line = lines(i).TrimStart()
            i += 1
            If line.Length = 0 OrElse line.StartsWith("#") OrElse line.StartsWith("!") Then Continue While

            ' Continuation lines end with an odd number of backslashes
            While EndsWithContinuation(line) AndAlso i < lines.Length
                line = line.Substring(0, line.Length - 1) & lines(i).TrimStart()
                i += 1
            End While

            Dim sep = FindSeparator(line)
            Dim rawKey = If(sep < 0, line, line.Substring(0, sep)).TrimEnd()
            Dim rawValue = If(sep < 0, "", line.Substring(sep + 1)).TrimStart()
            result(Unescape(rawKey)) = Unescape(rawValue)
        End While
        Return result
    End Function

    Public Shared Sub Save(filePath As String, values As IDictionary(Of String, String))
        Dim sb As New StringBuilder()
        sb.Append("#Minecraft server properties").Append(vbLf)
        sb.Append("#").Append(DateTime.Now.ToString("ddd MMM dd HH:mm:ss yyyy", CultureInfo.InvariantCulture)).Append(vbLf)
        For Each key In values.Keys.OrderBy(Function(k) k, StringComparer.Ordinal)
            sb.Append(Escape(key, True)).Append("="c).Append(Escape(values(key), False)).Append(vbLf)
        Next
        Directory.CreateDirectory(Path.GetDirectoryName(filePath))
        File.WriteAllText(filePath, sb.ToString(), New UTF8Encoding(False))
    End Sub

    Private Shared Function EndsWithContinuation(line As String) As Boolean
        Dim count = 0
        Dim j = line.Length - 1
        While j >= 0 AndAlso line(j) = "\"c
            count += 1
            j -= 1
        End While
        Return count Mod 2 = 1
    End Function

    Private Shared Function FindSeparator(line As String) As Integer
        Dim j = 0
        While j < line.Length
            Dim c = line(j)
            If c = "\"c Then
                j += 2
                Continue While
            End If
            If c = "="c OrElse c = ":"c Then Return j
            j += 1
        End While
        Return -1
    End Function

    Private Shared Function Unescape(value As String) As String
        Dim sb As New StringBuilder(value.Length)
        Dim j = 0
        While j < value.Length
            Dim c = value(j)
            If c <> "\"c OrElse j = value.Length - 1 Then
                sb.Append(c)
                j += 1
                Continue While
            End If
            Dim nextChar = value(j + 1)
            Select Case nextChar
                Case "n"c : sb.Append(vbLf)
                Case "r"c : sb.Append(vbCr)
                Case "t"c : sb.Append(vbTab)
                Case "f"c : sb.Append(ChrW(12))
                Case "u"c
                    Dim code As Integer
                    If j + 6 <= value.Length AndAlso Integer.TryParse(value.Substring(j + 2, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture, code) Then
                        sb.Append(ChrW(code))
                        j += 4
                    Else
                        sb.Append("u"c)
                    End If
                Case Else : sb.Append(nextChar)
            End Select
            j += 2
        End While
        Return sb.ToString()
    End Function

    Private Shared Function Escape(value As String, isKey As Boolean) As String
        Dim sb As New StringBuilder(value.Length + 8)
        For idx = 0 To value.Length - 1
            Dim c = value(idx)
            Select Case c
                Case "\"c : sb.Append("\\")
                Case ChrW(10) : sb.Append("\n")
                Case ChrW(13) : sb.Append("\r")
                Case ChrW(9) : sb.Append("\t")
                Case "="c, ":"c, "#"c, "!"c : sb.Append("\"c).Append(c)
                Case " "c
                    If isKey OrElse idx = 0 Then sb.Append("\ ") Else sb.Append(" ")
                Case Else
                    If AscW(c) < 32 OrElse AscW(c) > 126 Then
                        sb.Append("\u").Append(AscW(c).ToString("X4"))
                    Else
                        sb.Append(c)
                    End If
            End Select
        Next
        Return sb.ToString()
    End Function

    ' ---------- Known settings (vanilla 1.21) ----------

    Private Shared Function BuildDefinitions() As List(Of PropertyDef)
        Dim list As New List(Of PropertyDef)()

        Dim add = Sub(category As String, key As String, label As String, kind As PropertyKind, def As String, desc As String)
                      list.Add(New PropertyDef With {.Category = category, .Key = key, .Label = label, .Kind = kind, .DefaultValue = def, .Description = desc})
                  End Sub
        Dim addInt = Sub(category As String, key As String, label As String, def As String, min As Long, max As Long, desc As String)
                         list.Add(New PropertyDef With {.Category = category, .Key = key, .Label = label, .Kind = PropertyKind.Int, .DefaultValue = def, .Minimum = min, .Maximum = max, .Description = desc})
                     End Sub
        Dim addChoice = Sub(category As String, key As String, label As String, def As String, desc As String, choices As String())
                            Dim d As New PropertyDef With {.Category = category, .Key = key, .Label = label, .Kind = PropertyKind.Choice, .DefaultValue = def, .Description = desc}
                            For n = 0 To choices.Length - 2 Step 2
                                d.Choices.Add(New KeyValuePair(Of String, String)(choices(n), choices(n + 1)))
                            Next
                            list.Add(d)
                        End Sub

        ' Allgemein
        add("Allgemein", "motd", "Beschreibung (MOTD)", PropertyKind.Text, "A Minecraft Server", "Text unter dem Servernamen in der Serverliste. Farben mit § (z. B. §a grün).")
        addInt("Allgemein", "max-players", "Max. Spieler", "20", 1, 10000, "Wie viele Spieler gleichzeitig online sein dürfen.")
        addInt("Allgemein", "server-port", "Port", "25565", 1, 65535, "Port, über den Spieler verbinden. Muss im Router freigegeben sein.")
        add("Allgemein", "server-ip", "Server-IP", PropertyKind.Text, "", "Leer lassen, außer der Server soll nur an eine bestimmte IP binden.")
        add("Allgemein", "online-mode", "Online-Modus", PropertyKind.Bool, "true", "Prüft jeden Account bei Mojang. Nur hinter einem Proxy wie Velocity ausschalten.")
        add("Allgemein", "white-list", "Whitelist", PropertyKind.Bool, "false", "Nur Spieler auf der Whitelist dürfen joinen.")
        add("Allgemein", "enforce-whitelist", "Whitelist erzwingen", PropertyKind.Bool, "false", "Kickt Spieler sofort, wenn sie von der Whitelist entfernt werden.")

        ' Welt
        add("Welt", "level-name", "Weltname", PropertyKind.Text, "world", "Name des Weltordners. Ein neuer Name erzeugt eine neue Welt.")
        add("Welt", "level-seed", "Seed", PropertyKind.Text, "", "Leer = zufällig. Gilt nur beim Erstellen einer neuen Welt.")
        addChoice("Welt", "level-type", "Welttyp", "minecraft:normal", "Gilt nur beim Erstellen einer neuen Welt.",
                  {"minecraft:normal", "Normal", "minecraft:flat", "Flachland", "minecraft:large_biomes", "Große Biome",
                   "minecraft:amplified", "Zerklüftet (Amplified)", "minecraft:single_biome_surface", "Einzelnes Biom"})
        add("Welt", "generator-settings", "Generator-Einstellungen", PropertyKind.Text, "{}", "JSON für Flachland- und Einzelbiom-Welten.")
        add("Welt", "generate-structures", "Strukturen generieren", PropertyKind.Bool, "true", "Dörfer, Tempel, Festungen usw.")
        add("Welt", "allow-nether", "Nether erlauben", PropertyKind.Bool, "true", "")
        addInt("Welt", "spawn-protection", "Spawnschutz (Radius)", "16", 0, 10000, "Blöcke um den Spawn, die nur OPs verändern dürfen. 0 = aus.")
        addInt("Welt", "max-world-size", "Max. Weltgröße (Radius)", "29999984", 1, 29999984, "Radius der Weltgrenze in Blöcken.")

        ' Gameplay
        addChoice("Gameplay", "gamemode", "Spielmodus", "survival", "",
                  {"survival", "Überleben", "creative", "Kreativ", "adventure", "Abenteuer", "spectator", "Zuschauer"})
        add("Gameplay", "force-gamemode", "Spielmodus erzwingen", PropertyKind.Bool, "false", "Setzt Spieler beim Joinen immer auf den Standard-Spielmodus.")
        addChoice("Gameplay", "difficulty", "Schwierigkeit", "easy", "",
                  {"peaceful", "Friedlich", "easy", "Einfach", "normal", "Normal", "hard", "Schwer"})
        add("Gameplay", "hardcore", "Hardcore", PropertyKind.Bool, "false", "Spieler werden nach dem Tod Zuschauer.")
        add("Gameplay", "pvp", "PvP", PropertyKind.Bool, "true", "Spieler können sich gegenseitig Schaden zufügen.")
        add("Gameplay", "allow-flight", "Fliegen erlauben", PropertyKind.Bool, "false", "Verhindert Kicks wegen Fliegens (wichtig bei Plugins wie Jetpacks).")
        add("Gameplay", "spawn-monsters", "Monster spawnen", PropertyKind.Bool, "true", "")
        add("Gameplay", "enable-command-block", "Befehlsblöcke", PropertyKind.Bool, "false", "")
        addInt("Gameplay", "player-idle-timeout", "AFK-Kick (Minuten)", "0", 0, 100000, "0 = Spieler werden nie wegen Inaktivität gekickt.")

        ' Spieler & Sicherheit
        addChoice("Spieler & Sicherheit", "op-permission-level", "OP-Rechtestufe", "4", "Welche Befehle OPs standardmäßig nutzen dürfen.",
                  {"1", "1 – Spawnschutz umgehen", "2", "2 – Cheat-Befehle", "3", "3 – Spieler verwalten (kick, ban)", "4", "4 – Alles inkl. stop"})
        addChoice("Spieler & Sicherheit", "function-permission-level", "Rechtestufe für Funktionen", "2", "Rechte von Datapack-Funktionen.",
                  {"1", "1", "2", "2", "3", "3", "4", "4"})
        add("Spieler & Sicherheit", "enforce-secure-profile", "Signierten Chat erzwingen", PropertyKind.Bool, "true", "Spieler ohne Mojang-signierten Chat-Schlüssel können nicht joinen.")
        add("Spieler & Sicherheit", "hide-online-players", "Spielerliste verbergen", PropertyKind.Bool, "false", "Zeigt in der Serverliste keine Namen an.")
        add("Spieler & Sicherheit", "prevent-proxy-connections", "VPN/Proxy-Verbindungen blockieren", PropertyKind.Bool, "false", "")
        add("Spieler & Sicherheit", "log-ips", "IP-Adressen loggen", PropertyKind.Bool, "true", "")
        add("Spieler & Sicherheit", "broadcast-console-to-ops", "Konsolenbefehle an OPs senden", PropertyKind.Bool, "true", "")

        ' Leistung
        addInt("Leistung", "view-distance", "Sichtweite (Chunks)", "10", 3, 32, "Weniger = weniger RAM und CPU.")
        addInt("Leistung", "simulation-distance", "Simulationsweite (Chunks)", "10", 3, 32, "In diesem Radius laufen Mobs, Farmen und Redstone.")
        addInt("Leistung", "entity-broadcast-range-percentage", "Entity-Sichtweite (%)", "100", 10, 1000, "")
        addInt("Leistung", "max-tick-time", "Watchdog (ms)", "60000", -1, Integer.MaxValue, "Server stoppt, wenn ein Tick länger dauert. -1 = aus.")
        addInt("Leistung", "network-compression-threshold", "Kompression ab (Bytes)", "256", -1, 100000, "-1 = keine Kompression.")
        addInt("Leistung", "max-chained-neighbor-updates", "Max. verkettete Block-Updates", "1000000", -1, Integer.MaxValue, "")
        add("Leistung", "sync-chunk-writes", "Chunks synchron speichern", PropertyKind.Bool, "true", "Sicherer bei Abstürzen, aber etwas langsamer.")
        add("Leistung", "use-native-transport", "Native Netzwerk-Optimierung", PropertyKind.Bool, "true", "Nur unter Linux wirksam.")

        ' Netzwerk & RCON
        add("Netzwerk & RCON", "enable-status", "In Serverliste anzeigen", PropertyKind.Bool, "true", "Aus = Server erscheint offline, man kann aber trotzdem joinen.")
        addInt("Netzwerk & RCON", "rate-limit", "Paket-Limit pro Sekunde", "0", 0, 100000, "0 = kein Limit.")
        add("Netzwerk & RCON", "accepts-transfers", "Transfers annehmen", PropertyKind.Bool, "false", "Erlaubt /transfer von anderen Servern (ab 1.20.5).")
        add("Netzwerk & RCON", "enable-query", "Query aktivieren", PropertyKind.Bool, "false", "Für Server-Listen und Status-Tools.")
        addInt("Netzwerk & RCON", "query.port", "Query-Port", "25565", 1, 65535, "")
        add("Netzwerk & RCON", "enable-rcon", "RCON aktivieren", PropertyKind.Bool, "false", "Fernsteuerung per RCON-Client. Nur mit starkem Passwort!")
        addInt("Netzwerk & RCON", "rcon.port", "RCON-Port", "25575", 1, 65535, "")
        add("Netzwerk & RCON", "rcon.password", "RCON-Passwort", PropertyKind.Password, "", "")
        add("Netzwerk & RCON", "broadcast-rcon-to-ops", "RCON-Befehle an OPs senden", PropertyKind.Bool, "true", "")

        ' Ressourcenpaket
        add("Ressourcenpaket", "resource-pack", "Download-Link", PropertyKind.Text, "", "Direkter Link zur .zip-Datei.")
        add("Ressourcenpaket", "resource-pack-sha1", "SHA-1-Prüfsumme", PropertyKind.Text, "", "Optional, damit Clients Updates erkennen.")
        add("Ressourcenpaket", "resource-pack-id", "Paket-ID (UUID)", PropertyKind.Text, "", "")
        add("Ressourcenpaket", "require-resource-pack", "Paket verpflichtend", PropertyKind.Bool, "false", "Spieler, die ablehnen, werden getrennt.")
        add("Ressourcenpaket", "resource-pack-prompt", "Hinweistext", PropertyKind.Text, "", "Text im Annahme-Dialog.")
        add("Ressourcenpaket", "initial-enabled-packs", "Aktive Datapacks (neue Welt)", PropertyKind.Text, "vanilla", "Kommagetrennt.")
        add("Ressourcenpaket", "initial-disabled-packs", "Deaktivierte Datapacks (neue Welt)", PropertyKind.Text, "", "Kommagetrennt.")

        Return list
    End Function

End Class
