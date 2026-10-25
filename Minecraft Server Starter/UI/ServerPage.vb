Imports System.ComponentModel
Imports System.Text
Imports System.Text.RegularExpressions

''' <summary>Start/stop the server, choose jar and RAM, live console.</summary>
Friend Class ServerPage
    Inherits PageBase

    ' Aikar's well-known G1 flags for Minecraft servers (https://docs.papermc.io/paper/aikars-flags)
    Private Const AikarFlags As String =
        "-XX:+UseG1GC -XX:+ParallelRefProcEnabled -XX:MaxGCPauseMillis=200 -XX:+UnlockExperimentalVMOptions " &
        "-XX:+DisableExplicitGC -XX:+AlwaysPreTouch -XX:G1NewSizePercent=30 -XX:G1MaxNewSizePercent=40 " &
        "-XX:G1HeapRegionSize=8M -XX:G1ReservePercent=20 -XX:G1HeapWastePercent=5 -XX:G1MixedGCCountTarget=4 " &
        "-XX:InitiatingHeapOccupancyPercent=15 -XX:G1MixedGCLiveThresholdPercent=90 -XX:G1RSetUpdatingPauseTimePercent=5 " &
        "-XX:SurvivorRatio=32 -XX:+PerfDisableSharedMem -XX:MaxTenuringThreshold=1 " &
        "-Dusing.aikars.flags=https://mcflags.emc.gs -Daikars.new.flags=true"

    Private Shared ReadOnly AnsiPattern As New Regex("\x1B\[[0-9;]*[A-Za-z]")
    Private Shared ReadOnly JavaVersionPattern As New Regex("version ""([^""]+)""")

    Public Event StateChanged As EventHandler

    ' Left card
    Private ReadOnly configCard As New CardPanel() With {.Title = "Konfiguration"}
    Private ReadOnly folderLabel As Label = MakeLabel("Serverordner", small:=True)
    Private ReadOnly folderBox As New DarkTextBox() With {.ReadOnly = True}
    Private ReadOnly changeFolderButton As ModernButton = MakeButton("", ButtonKind.Secondary, Theme.IconFolder)
    Private ReadOnly openFolderButton As ModernButton = MakeButton("Öffnen", ButtonKind.Secondary)
    Private ReadOnly jarLabel As Label = MakeLabel("Server-Datei (.jar)", small:=True)
    Private ReadOnly jarCombo As New DarkComboBox()
    Private ReadOnly refreshJarsButton As ModernButton = MakeButton("", ButtonKind.Secondary, Theme.IconRefresh)
    Private ReadOnly ramLabel As Label = MakeLabel("Arbeitsspeicher", small:=True)
    Private ReadOnly ramValueLabel As Label = MakeLabel("", muted:=False)
    Private ReadOnly ramSlider As New Slider() With {.SmallStep = 512}
    Private ReadOnly ramHint As Label = MakeLabel("", small:=True)
    Private ReadOnly aikarToggle As New ToggleSwitch() With {.Text = "Optimierte Java-Flags (Aikar)"}
    Private ReadOnly eulaToggle As New ToggleSwitch() With {.Text = "Ich akzeptiere die Minecraft-EULA"}
    Private ReadOnly eulaLink As New LinkLabel() With {.Text = "EULA lesen", .AutoSize = True, .Font = Theme.SmallFont}
    Private ReadOnly javaLabel As Label = MakeLabel("Java", small:=True)
    Private ReadOnly javaBox As New DarkTextBox() With {.Placeholder = "java"}
    Private ReadOnly javaBrowseButton As ModernButton = MakeButton("…", ButtonKind.Secondary)
    Private ReadOnly javaInfo As Label = MakeLabel("Java wird geprüft …", small:=True)
    Private ReadOnly argsLabel As Label = MakeLabel("Zusätzliche Java-Argumente", small:=True)
    Private ReadOnly argsBox As New DarkTextBox() With {.Placeholder = "optional, z. B. -Dfile.encoding=UTF-8"}
    Private ReadOnly startButton As ModernButton = MakeButton("Server starten", ButtonKind.Primary, Theme.IconPlay)

    ' Right card
    Private ReadOnly consoleCard As New CardPanel() With {.Title = "Konsole", .Subtitle = "Server gestoppt"}
    Private ReadOnly scriptButton As ModernButton = MakeButton("start.bat erstellen", ButtonKind.Ghost, Theme.IconSave)
    Private ReadOnly clearButton As ModernButton = MakeButton("Leeren", ButtonKind.Ghost, Theme.IconDelete)
    Private ReadOnly logBox As New ConsoleBox()
    Private ReadOnly commandBox As New DarkTextBox() With {.Placeholder = "Befehl eingeben, z. B. op Spielername   (↑/↓ = Verlauf)"}
    Private ReadOnly sendButton As ModernButton = MakeButton("Senden", ButtonKind.Secondary)

    Private serverProcess As Process
    Private _state As ServerState = ServerState.Stopped
    Private ReadOnly history As New List(Of String)()
    Private historyIndex As Integer = -1
    Private loadingConfig As Boolean
    Private javaCheckVersion As Integer

    Public Sub New(owner As MainForm)
        MyBase.New(owner, "Server", "Starten, stoppen und Befehle senden")

        eulaLink.LinkColor = Theme.Accent
        eulaLink.ActiveLinkColor = Theme.AccentHover
        eulaLink.VisitedLinkColor = Theme.Accent
        eulaLink.BackColor = Color.Transparent

        configCard.Controls.AddRange(New Control() {
            folderLabel, folderBox, changeFolderButton, openFolderButton,
            jarLabel, jarCombo, refreshJarsButton,
            ramLabel, ramValueLabel, ramSlider, ramHint,
            aikarToggle, eulaToggle, eulaLink,
            javaLabel, javaBox, javaBrowseButton, javaInfo,
            argsLabel, argsBox, startButton})
        consoleCard.Controls.AddRange(New Control() {scriptButton, clearButton, logBox, commandBox, sendButton})
        Controls.Add(configCard)
        Controls.Add(consoleCard)

        ramValueLabel.TextAlign = ContentAlignment.MiddleRight
        startButton.Height = 42

        ' RAM range depends on the PC
        Dim totalMb = CLng((New Microsoft.VisualBasic.Devices.ComputerInfo()).TotalPhysicalMemory \ (1024UL * 1024UL))
        ramSlider.Minimum = 1024
        ramSlider.Maximum = CInt(Math.Max(2048, ((totalMb - 1024) \ 512) * 512))
        ramHint.Text = $"Dein PC hat {totalMb / 1024.0:0} GB. Für 1–5 Spieler reichen 3–4 GB, Modpacks brauchen mehr."

        LoadConfig()
        WireEvents()
        UpdateStateUi()
        logBox.AppendLine("Bereit. Wähle eine Server-Datei und klicke auf ""Server starten"".", Theme.TextDim)
    End Sub

    Public ReadOnly Property State As ServerState
        Get
            Return _state
        End Get
    End Property

    Friend Overrides Sub OnShown()
        ReloadJars()
        UpdateEulaToggle()
    End Sub

    ' ---------- Setup ----------

    Private Sub LoadConfig()
        loadingConfig = True
        folderBox.Text = Config.ServerFolder
        ramSlider.Value = Config.RamMb
        aikarToggle.Checked = Config.UseAikarFlags
        javaBox.Text = If(Config.JavaPath = "java", "", Config.JavaPath)
        argsBox.Text = Config.ExtraJvmArgs
        UpdateRamLabel()
        ReloadJars()
        UpdateEulaToggle()
        loadingConfig = False
        CheckJavaAsync()
    End Sub

    Private Sub WireEvents()
        AddHandler changeFolderButton.Click, AddressOf ChangeFolder
        AddHandler openFolderButton.Click,
            Sub(s, e)
                Directory.CreateDirectory(Config.ServerFolder)
                OpenInExplorer(Config.ServerFolder)
            End Sub
        AddHandler refreshJarsButton.Click, Sub(s, e) ReloadJars()
        AddHandler jarCombo.SelectedIndexChanged,
            Sub(s, e)
                If loadingConfig OrElse jarCombo.SelectedItem Is Nothing Then Return
                Dim name = jarCombo.SelectedItem.ToString()
                If name = Config.JarName OrElse Not name.EndsWith(".jar", StringComparison.OrdinalIgnoreCase) Then Return
                Config.JarName = name
                ' Type/version are only known for jars downloaded by this app
                Config.ServerType = GuessServerType(name)
                Config.ServerVersion = GuessVersion(name)
                Main.NotifyServerFilesChanged()
                CheckJavaAsync()
            End Sub
        AddHandler ramSlider.ValueChanged,
            Sub(s, e)
                UpdateRamLabel()
                If loadingConfig Then Return
                Config.RamMb = ramSlider.Value
                Config.Save()
            End Sub
        AddHandler aikarToggle.CheckedChanged,
            Sub(s, e)
                If loadingConfig Then Return
                Config.UseAikarFlags = aikarToggle.Checked
                Config.Save()
            End Sub
        AddHandler eulaToggle.CheckedChanged,
            Sub(s, e)
                If loadingConfig Then Return
                WriteEula(eulaToggle.Checked)
            End Sub
        AddHandler eulaLink.LinkClicked, Sub(s, e) OpenInExplorer("https://www.minecraft.net/eula")
        AddHandler javaBox.Leave,
            Sub(s, e)
                Dim value = javaBox.Text.Trim()
                Dim newPath = If(value.Length = 0, "java", value)
                If newPath = Config.JavaPath Then Return
                Config.JavaPath = newPath
                Config.Save()
                CheckJavaAsync()
            End Sub
        AddHandler javaBrowseButton.Click, AddressOf BrowseJava
        AddHandler argsBox.Leave,
            Sub(s, e)
                Config.ExtraJvmArgs = argsBox.Text.Trim()
                Config.Save()
            End Sub
        AddHandler startButton.Click,
            Sub(s, e)
                If _state = ServerState.Stopped Then
                    StartServer()
                Else
                    StopServer()
                End If
            End Sub
        AddHandler scriptButton.Click, AddressOf CreateStartScript
        AddHandler clearButton.Click, Sub(s, e) logBox.Clear()
        AddHandler sendButton.Click, Sub(s, e) SendFromInput()
        AddHandler commandBox.KeyDown, AddressOf CommandBox_KeyDown
    End Sub

    ' ---------- Layout ----------

    Protected Overrides Sub OnLayout(levent As LayoutEventArgs)
        MyBase.OnLayout(levent)
        If consoleCard Is Nothing OrElse startButton Is Nothing Then Return

        Dim top = HeaderHeight
        Dim cardHeight = Math.Max(300, ClientSize.Height - top - Pad)
        Const leftWidth As Integer = 350
        configCard.SetBounds(Pad, top, leftWidth, cardHeight)
        consoleCard.SetBounds(Pad + leftWidth + 16, top, Math.Max(300, ClientSize.Width - leftWidth - 16 - Pad * 2), cardHeight)

        ' Config card
        Dim w = leftWidth - 32
        Dim y = configCard.ContentTop
        folderLabel.SetBounds(16, y, w, 18) : y += 20
        openFolderButton.SetBounds(16 + w - 76, y, 76, 34)
        changeFolderButton.SetBounds(openFolderButton.Left - 42, y, 34, 34)
        folderBox.SetBounds(16, y, changeFolderButton.Left - 24, 34) : y += 44

        jarLabel.SetBounds(16, y, w, 18) : y += 20
        refreshJarsButton.SetBounds(16 + w - 34, y, 34, 34)
        jarCombo.SetBounds(16, y + 1, w - 42, 32) : y += 44

        ramLabel.SetBounds(16, y, w - 80, 18)
        ramValueLabel.SetBounds(16 + w - 80, y - 2, 80, 20) : y += 22
        ramSlider.SetBounds(12, y, w + 8, 24) : y += 26
        ramHint.SetBounds(16, y, w, 32) : y += 38

        aikarToggle.SetBounds(16, y, w, 24) : y += 30
        eulaToggle.SetBounds(16, y, w - 80, 24)
        eulaLink.Location = New Point(16 + w - eulaLink.PreferredWidth, y + 4) : y += 36

        javaLabel.SetBounds(16, y, w, 18) : y += 20
        javaBrowseButton.SetBounds(16 + w - 34, y, 34, 34)
        javaBox.SetBounds(16, y, w - 42, 34) : y += 38
        javaInfo.SetBounds(16, y, w, 18) : y += 26

        argsLabel.SetBounds(16, y, w, 18) : y += 20
        argsBox.SetBounds(16, y, w, 34)

        startButton.SetBounds(16, cardHeight - 16 - 42, w, 42)

        ' Console card
        Dim cw = consoleCard.Width - 32
        clearButton.SetBounds(consoleCard.Width - 16 - 90, 12, 90, 30)
        scriptButton.SetBounds(clearButton.Left - 8 - 160, 12, 160, 30)
        sendButton.SetBounds(16 + cw - 90, cardHeight - 16 - 34, 90, 34)
        commandBox.SetBounds(16, cardHeight - 16 - 34, cw - 98, 34)
        logBox.SetBounds(16, consoleCard.ContentTop + 4, cw, commandBox.Top - consoleCard.ContentTop - 16)
    End Sub

    ' ---------- Folder, jar, Java ----------

    Private Sub ChangeFolder(sender As Object, e As EventArgs)
        Using dialog As New FolderBrowserDialog() With {
            .Description = "Ordner für den Minecraft-Server wählen (Welt, Plugins und Einstellungen landen hier)",
            .SelectedPath = Config.ServerFolder,
            .ShowNewFolderButton = True
        }
            If dialog.ShowDialog(Me) <> DialogResult.OK Then Return
            Config.ServerFolder = dialog.SelectedPath
        End Using
        folderBox.Text = Config.ServerFolder
        Config.JarName = ""
        ReloadJars()
        UpdateEulaToggle()
        Main.NotifyServerFilesChanged()
    End Sub

    Friend Sub ReloadJars()
        loadingConfig = True
        Try
            jarCombo.Items.Clear()
            Dim jars As String() = {}
            If Directory.Exists(Config.ServerFolder) Then
                jars = Directory.GetFiles(Config.ServerFolder, "*.jar").Select(Function(f) Path.GetFileName(f)).OrderBy(Function(n) n).ToArray()
            End If

            If jars.Length = 0 Then
                jarCombo.Items.Add("Keine .jar – unter ""Download"" holen")
                jarCombo.SelectedIndex = 0
                jarCombo.Enabled = False
                Config.JarName = ""
            Else
                jarCombo.Enabled = (_state = ServerState.Stopped)
                jarCombo.Items.AddRange(jars)
                Dim idx = Array.FindIndex(jars, Function(n) String.Equals(n, Config.JarName, StringComparison.OrdinalIgnoreCase))
                If idx < 0 Then
                    idx = 0
                    Config.JarName = jars(0)
                    Config.ServerType = GuessServerType(jars(0))
                    Config.ServerVersion = GuessVersion(jars(0))
                    Config.Save()
                End If
                jarCombo.SelectedIndex = idx
            End If
        Finally
            loadingConfig = False
        End Try
        UpdateStateUi()
    End Sub

    Private Shared Function GuessServerType(jarName As String) As String
        Dim lower = jarName.ToLowerInvariant()
        If lower.StartsWith("paper") OrElse lower.StartsWith("purpur") OrElse lower.StartsWith("spigot") OrElse lower.Contains("bukkit") Then Return "paper"
        If lower.StartsWith("vanilla") OrElse lower = "server.jar" OrElse lower.StartsWith("minecraft_server") Then Return "vanilla"
        Return ""
    End Function

    Private Shared Function GuessVersion(jarName As String) As String
        Dim m = Regex.Match(jarName, "(\d+\.\d+(?:\.\d+)?)")
        Return If(m.Success, m.Groups(1).Value, "")
    End Function

    Private Sub BrowseJava(sender As Object, e As EventArgs)
        Using dialog As New OpenFileDialog() With {
            .Title = "java.exe auswählen",
            .Filter = "Java|java.exe|Alle Programme|*.exe",
            .CheckFileExists = True
        }
            If dialog.ShowDialog(Me) <> DialogResult.OK Then Return
            javaBox.Text = dialog.FileName
            Config.JavaPath = dialog.FileName
            Config.Save()
        End Using
        CheckJavaAsync()
    End Sub

    ''' <summary>Runs "java -version" in the background and shows the result.</summary>
    Private Async Sub CheckJavaAsync()
        javaCheckVersion += 1
        Dim myCheck = javaCheckVersion
        javaInfo.ForeColor = Theme.TextMuted
        javaInfo.Text = "Java wird geprüft …"

        Dim javaPath = Config.JavaPath
        Dim output = Await Task.Run(Function() RunJavaVersion(javaPath))
        If myCheck <> javaCheckVersion OrElse IsDisposed Then Return

        If output Is Nothing Then
            javaInfo.ForeColor = Theme.Danger
            javaInfo.Text = "Java nicht gefunden – installiere Java 21 (adoptium.net)"
            Return
        End If

        Dim m = JavaVersionPattern.Match(output)
        Dim versionText = If(m.Success, m.Groups(1).Value, "?")
        Dim major = JavaMajor(versionText)
        Dim needed = RequiredJava(Config.ServerVersion)

        If major > 0 AndAlso needed > 0 AndAlso major < needed Then
            javaInfo.ForeColor = Theme.Warning
            javaInfo.Text = $"Java {versionText} – Minecraft {Config.ServerVersion} braucht Java {needed}+"
        Else
            javaInfo.ForeColor = Theme.Accent
            javaInfo.Text = $"Java {versionText} gefunden"
        End If
    End Sub

    Private Shared Function RunJavaVersion(javaPath As String) As String
        Try
            Dim psi As New ProcessStartInfo(javaPath, "-version") With {
                .UseShellExecute = False,
                .RedirectStandardError = True,
                .RedirectStandardOutput = True,
                .CreateNoWindow = True
            }
            Using p = Process.Start(psi)
                Dim text = p.StandardError.ReadToEnd() & p.StandardOutput.ReadToEnd()
                p.WaitForExit(5000)
                Return text
            End Using
        Catch
            Return Nothing
        End Try
    End Function

    Private Shared Function JavaMajor(versionText As String) As Integer
        Dim parts = versionText.Split("."c, "_"c, "-"c, "+"c)
        Dim first As Integer
        If parts.Length = 0 OrElse Not Integer.TryParse(parts(0), first) Then Return 0
        If first = 1 AndAlso parts.Length > 1 Then
            Dim second As Integer
            If Integer.TryParse(parts(1), second) Then Return second ' 1.8 -> 8
        End If
        Return first
    End Function

    ''' <summary>Minimum Java version for a Minecraft version (0 = unknown).</summary>
    Private Shared Function RequiredJava(mcVersion As String) As Integer
        If String.IsNullOrEmpty(mcVersion) Then Return 0
        Dim parts = mcVersion.Split("."c)
        Dim major, minor, patch As Integer
        If Not Integer.TryParse(parts(0), major) Then Return 0
        If major <> 1 Then Return 21 ' new version scheme (e.g. 26.1)
        If parts.Length < 2 OrElse Not Integer.TryParse(parts(1), minor) Then Return 0
        If parts.Length > 2 Then Integer.TryParse(parts(2), patch)
        If minor > 20 OrElse (minor = 20 AndAlso patch >= 5) Then Return 21
        If minor >= 18 Then Return 17
        If minor = 17 Then Return 16
        Return 8
    End Function

    ' ---------- RAM / EULA ----------

    Private Sub UpdateRamLabel()
        ramValueLabel.Text = (ramSlider.Value / 1024.0).ToString("0.0") & " GB"
    End Sub

    Private ReadOnly Property EulaPath As String
        Get
            Return Path.Combine(Config.ServerFolder, "eula.txt")
        End Get
    End Property

    Private Sub UpdateEulaToggle()
        Dim accepted = False
        Try
            If File.Exists(EulaPath) Then
                accepted = File.ReadAllLines(EulaPath).Any(Function(l) l.Trim().Equals("eula=true", StringComparison.OrdinalIgnoreCase))
            End If
        Catch
        End Try
        Dim wasLoading = loadingConfig
        loadingConfig = True
        eulaToggle.Checked = accepted
        loadingConfig = wasLoading
    End Sub

    Private Sub WriteEula(accepted As Boolean)
        Try
            Directory.CreateDirectory(Config.ServerFolder)
            File.WriteAllLines(EulaPath, {
                "# Accepted in Minecraft Server Starter: https://aka.ms/MinecraftEULA",
                "#" & DateTime.Now.ToString("ddd MMM dd HH:mm:ss yyyy", System.Globalization.CultureInfo.InvariantCulture),
                "eula=" & If(accepted, "true", "false")})
        Catch ex As Exception
            ShowError(Me, "eula.txt konnte nicht geschrieben werden", ex)
        End Try
    End Sub

    ' ---------- Start / stop ----------

    Private Function BuildJvmArguments(jar As String, forScript As Boolean) As String
        Dim sb As New StringBuilder()
        sb.Append($"-Xms{Config.RamMb}M -Xmx{Config.RamMb}M ")
        If Config.UseAikarFlags Then sb.Append(AikarFlags).Append(" "c)
        If Config.ExtraJvmArgs.Length > 0 Then sb.Append(Config.ExtraJvmArgs).Append(" "c)
        ' Keep umlauts readable in the console window of this app
        If Not forScript Then sb.Append("-Dstdout.encoding=UTF-8 -Dstderr.encoding=UTF-8 ")
        sb.Append($"-jar ""{jar}"" nogui")
        Return sb.ToString()
    End Function

    Private Sub StartServer()
        If _state <> ServerState.Stopped Then Return

        If String.IsNullOrEmpty(Config.JarName) OrElse Not File.Exists(Config.JarPath) Then
            MessageBox.Show(Me, "Wähle zuerst eine Server-Datei aus oder lade unter ""Download"" eine herunter.",
                            "Keine Server-Datei", MessageBoxButtons.OK, MessageBoxIcon.Information)
            Return
        End If
        If Not eulaToggle.Checked Then
            MessageBox.Show(Me, "Ein Minecraft-Server startet nur, wenn du die EULA von Mojang akzeptierst. " &
                            "Lies sie über ""EULA lesen"" und aktiviere dann den Schalter.",
                            "EULA nicht akzeptiert", MessageBoxButtons.OK, MessageBoxIcon.Information)
            Return
        End If

        Directory.CreateDirectory(Config.ServerFolder)
        Dim psi As New ProcessStartInfo(Config.JavaPath, BuildJvmArguments(Config.JarName, False)) With {
            .WorkingDirectory = Config.ServerFolder,
            .UseShellExecute = False,
            .RedirectStandardInput = True,
            .RedirectStandardOutput = True,
            .RedirectStandardError = True,
            .CreateNoWindow = True,
            .StandardOutputEncoding = Encoding.UTF8,
            .StandardErrorEncoding = Encoding.UTF8
        }

        Dim p As New Process() With {.StartInfo = psi, .EnableRaisingEvents = True}
        AddHandler p.OutputDataReceived, AddressOf Process_OutputDataReceived
        AddHandler p.ErrorDataReceived, AddressOf Process_OutputDataReceived
        AddHandler p.Exited, AddressOf Process_Exited

        logBox.Clear()
        logBox.AppendLine($"> {psi.FileName} {psi.Arguments}", Theme.TextDim)
        Try
            p.Start()
        Catch ex As Exception
            p.Dispose()
            logBox.AppendLine("Java konnte nicht gestartet werden: " & ex.Message, Theme.Danger)
            Dim hint = If(TypeOf ex Is Win32Exception AndAlso CType(ex, Win32Exception).NativeErrorCode = 2,
                          "Java wurde nicht gefunden." & Environment.NewLine & Environment.NewLine &
                          "Installiere Java 21 (z. B. von adoptium.net) oder wähle unter ""Java"" die java.exe aus.",
                          ex.Message)
            MessageBox.Show(Me, hint, "Server konnte nicht starten", MessageBoxButtons.OK, MessageBoxIcon.Warning)
            Return
        End Try

        serverProcess = p
        p.StandardInput.AutoFlush = True
        p.BeginOutputReadLine()
        p.BeginErrorReadLine()
        SetState(ServerState.Starting)
    End Sub

    Private Sub StopServer()
        If serverProcess Is Nothing Then Return
        If _state = ServerState.Stopping Then
            If MessageBox.Show(Me, "Der Server reagiert nicht. Prozess hart beenden? Nicht gespeicherte Daten gehen verloren.",
                               "Beenden erzwingen", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) = DialogResult.Yes Then
                KillServer()
            End If
            Return
        End If
        SendCommand("stop")
        SetState(ServerState.Stopping)
    End Sub

    Private Sub KillServer()
        Try
            If serverProcess IsNot Nothing AndAlso Not serverProcess.HasExited Then serverProcess.Kill()
        Catch
        End Try
    End Sub

    ''' <summary>Stops the server and waits up to 60 seconds before killing it.</summary>
    Friend Async Function StopAndWaitAsync() As Task
        If _state = ServerState.Stopped Then Return
        If _state <> ServerState.Stopping Then
            SendCommand("stop")
            SetState(ServerState.Stopping)
        End If
        Dim waited = 0
        While _state <> ServerState.Stopped AndAlso waited < 60000
            Await Task.Delay(250)
            waited += 250
        End While
        If _state <> ServerState.Stopped Then KillServer()
    End Function

    Private Sub Process_OutputDataReceived(sender As Object, e As DataReceivedEventArgs)
        If e.Data Is Nothing OrElse IsDisposed OrElse Not IsHandleCreated Then Return
        Dim line = AnsiPattern.Replace(e.Data, "")
        Try
            BeginInvoke(New Action(Sub() HandleServerLine(line)))
        Catch ex As InvalidOperationException
            ' Window is closing
        End Try
    End Sub

    Private Sub Process_Exited(sender As Object, e As EventArgs)
        If IsDisposed OrElse Not IsHandleCreated Then Return
        Dim p = DirectCast(sender, Process)
        BeginInvoke(New Action(
            Sub()
                Dim code = 0
                Try
                    code = p.ExitCode
                Catch
                End Try
                logBox.AppendLine($"Server beendet (Exit-Code {code}).", If(code = 0, Theme.TextDim, Theme.Danger))
                If code <> 0 AndAlso _state = ServerState.Starting Then
                    logBox.AppendLine("Tipp: Prüfe oben die Fehlermeldung. Häufige Ursachen sind eine zu alte Java-Version oder ein belegter Port.", Theme.Warning)
                End If
                If serverProcess Is p Then serverProcess = Nothing
                p.Dispose()
                SetState(ServerState.Stopped)
                Main.NotifyServerFilesChanged()
            End Sub))
    End Sub

    Private Sub HandleServerLine(line As String)
        logBox.AppendLine(line, ColorForLine(line))
        If _state = ServerState.Starting AndAlso line.Contains("Done (") Then
            SetState(ServerState.Running)
        End If
    End Sub

    Private Shared Function ColorForLine(line As String) As Color
        If line.Contains("ERROR") OrElse line.Contains("SEVERE") OrElse line.Contains("Exception") Then Return Theme.Danger
        If line.Contains("WARN") Then Return Theme.Warning
        If line.Contains("Done (") OrElse line.Contains("joined the game") Then Return Theme.Accent
        If line.Contains("left the game") Then Return Theme.Info
        Return Theme.TextMain
    End Function

    Private Sub SetState(newState As ServerState)
        If _state = newState Then Return
        _state = newState
        UpdateStateUi()
        RaiseEvent StateChanged(Me, EventArgs.Empty)
    End Sub

    Private Sub UpdateStateUi()
        Dim stopped = (_state = ServerState.Stopped)
        Select Case _state
            Case ServerState.Stopped
                startButton.Text = "Server starten"
                startButton.Kind = ButtonKind.Primary
                startButton.IconGlyph = Theme.IconPlay
                consoleCard.Subtitle = "Server gestoppt"
            Case ServerState.Starting
                startButton.Text = "Server stoppen"
                startButton.Kind = ButtonKind.Danger
                startButton.IconGlyph = Theme.IconStop
                consoleCard.Subtitle = "Server startet … (beim ersten Start wird die Welt erzeugt)"
            Case ServerState.Running
                startButton.Text = "Server stoppen"
                startButton.Kind = ButtonKind.Danger
                startButton.IconGlyph = Theme.IconStop
                consoleCard.Subtitle = $"Online · Verbinden mit localhost:{CurrentPort()}"
            Case ServerState.Stopping
                startButton.Text = "Beenden erzwingen"
                startButton.Kind = ButtonKind.Secondary
                startButton.IconGlyph = Theme.IconStop
                consoleCard.Subtitle = "Server wird gestoppt und speichert die Welt …"
        End Select

        changeFolderButton.Enabled = stopped
        jarCombo.Enabled = stopped AndAlso Not String.IsNullOrEmpty(Config.JarName)
        refreshJarsButton.Enabled = stopped
        ramSlider.Enabled = stopped
        aikarToggle.Enabled = stopped
        javaBox.Enabled = stopped
        javaBrowseButton.Enabled = stopped
        argsBox.Enabled = stopped
        sendButton.Enabled = Not stopped
    End Sub

    Private Function CurrentPort() As String
        Try
            Dim props = ServerProperties.Load(Path.Combine(Config.ServerFolder, "server.properties"))
            Dim port As String = Nothing
            If props.TryGetValue("server-port", port) AndAlso port.Length > 0 Then Return port
        Catch
        End Try
        Return "25565"
    End Function

    ' ---------- Commands ----------

    Private Sub SendFromInput()
        Dim command = commandBox.Text.Trim()
        If command.Length = 0 Then Return
        If command.StartsWith("/") Then command = command.Substring(1)
        If _state = ServerState.Stopped Then
            logBox.AppendLine("Der Server läuft nicht.", Theme.Warning)
            Return
        End If
        history.Add(command)
        historyIndex = history.Count
        commandBox.Text = ""
        SendCommand(command)
    End Sub

    Private Sub SendCommand(command As String)
        Try
            If serverProcess Is Nothing OrElse serverProcess.HasExited Then Return
            logBox.AppendLine("> " & command, Theme.Info)
            serverProcess.StandardInput.WriteLine(command)
        Catch ex As Exception
            logBox.AppendLine("Befehl konnte nicht gesendet werden: " & ex.Message, Theme.Danger)
        End Try
    End Sub

    Private Sub CommandBox_KeyDown(sender As Object, e As KeyEventArgs)
        Select Case e.KeyCode
            Case Keys.Enter
                e.SuppressKeyPress = True
                SendFromInput()
            Case Keys.Up
                If history.Count = 0 Then Return
                historyIndex = Math.Max(0, historyIndex - 1)
                commandBox.Text = history(historyIndex)
                commandBox.Inner.SelectionStart = commandBox.Text.Length
                e.Handled = True
            Case Keys.Down
                If history.Count = 0 Then Return
                historyIndex = Math.Min(history.Count, historyIndex + 1)
                commandBox.Text = If(historyIndex < history.Count, history(historyIndex), "")
                commandBox.Inner.SelectionStart = commandBox.Text.Length
                e.Handled = True
        End Select
    End Sub

    ' ---------- start.bat ----------

    Private Sub CreateStartScript(sender As Object, e As EventArgs)
        If String.IsNullOrEmpty(Config.JarName) Then
            MessageBox.Show(Me, "Wähle zuerst eine Server-Datei aus.", "start.bat", MessageBoxButtons.OK, MessageBoxIcon.Information)
            Return
        End If
        Try
            Directory.CreateDirectory(Config.ServerFolder)
            Dim scriptPath = Path.Combine(Config.ServerFolder, "start.bat")
            File.WriteAllLines(scriptPath, {
                "@echo off",
                "title Minecraft Server",
                "cd /d ""%~dp0""",
                $"""{Config.JavaPath}"" {BuildJvmArguments(Config.JarName, True)}",
                "pause"}, New UTF8Encoding(False))
            logBox.AppendLine("start.bat erstellt: " & scriptPath, Theme.Accent)
        Catch ex As Exception
            ShowError(Me, "start.bat konnte nicht erstellt werden", ex)
        End Try
    End Sub

End Class
