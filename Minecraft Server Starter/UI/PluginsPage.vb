Imports System.Globalization
Imports System.Threading

''' <summary>Manage the plugins folder and install plugins from Modrinth.</summary>
Friend Class PluginsPage
    Inherits PageBase

    Private Const DisabledSuffix As String = ".disabled"

    Private ReadOnly warningLabel As New Label() With {
        .AutoSize = False,
        .TextAlign = ContentAlignment.MiddleLeft,
        .Padding = New Padding(14, 0, 14, 0),
        .BackColor = Color.FromArgb(58, 46, 22),
        .ForeColor = Theme.Warning,
        .Font = Theme.BodyFont,
        .Visible = False,
        .Text = "Dein Server ist Vanilla – Vanilla lädt keine Plugins. Lade unter ""Download"" einen Paper-Server, deine Welt bleibt erhalten."
    }

    ' Installed
    Private ReadOnly installedCard As New CardPanel() With {.Title = "Installiert", .Subtitle = "Ziehe .jar-Dateien hierher, um sie hinzuzufügen"}
    Private ReadOnly installedList As New EntryListBox() With {.EmptyText = "Noch keine Plugins installiert", .AllowDrop = True}
    Private ReadOnly addButton As ModernButton = MakeButton("Hinzufügen", ButtonKind.Primary, Theme.IconAdd)
    Private ReadOnly toggleButton As ModernButton = MakeButton("An / Aus", ButtonKind.Secondary)
    Private ReadOnly removeButton As ModernButton = MakeButton("", ButtonKind.Secondary, Theme.IconDelete)
    Private ReadOnly folderButton As ModernButton = MakeButton("", ButtonKind.Secondary, Theme.IconFolder)
    Private ReadOnly restartHint As Label = MakeLabel("Änderungen gelten nach dem nächsten Serverstart.", small:=True)

    ' Modrinth
    Private ReadOnly searchCard As New CardPanel() With {.Title = "Plugins finden", .Subtitle = "Kostenlose Plugins von Modrinth"}
    Private ReadOnly searchBox As New DarkTextBox() With {.Placeholder = "Suchen, z. B. EssentialsX, LuckPerms, WorldEdit"}
    Private ReadOnly versionBox As New DarkTextBox() With {.Placeholder = "MC-Version"}
    Private ReadOnly searchButton As ModernButton = MakeButton("", ButtonKind.Secondary, Theme.IconSearch)
    Private ReadOnly resultList As New EntryListBox() With {.EmptyText = "Suche nach einem Plugin", .ItemHeight = 54}
    Private ReadOnly statusLabel As Label = MakeLabel("", small:=True)
    Private ReadOnly installButton As ModernButton = MakeButton("Installieren", ButtonKind.Primary, Theme.IconDownload)
    Private ReadOnly pageButton As ModernButton = MakeButton("Seite öffnen", ButtonKind.Secondary)

    Private searchVersion As Integer
    Private busy As Boolean
    Private searchedOnce As Boolean

    Public Sub New(owner As MainForm)
        MyBase.New(owner, "Plugins", "Plugins für Paper-, Spigot- und Bukkit-Server verwalten")

        installedCard.Controls.AddRange(New Control() {installedList, addButton, toggleButton, removeButton, folderButton, restartHint})
        searchCard.Controls.AddRange(New Control() {searchBox, versionBox, searchButton, resultList, statusLabel, installButton, pageButton})
        Controls.Add(warningLabel)
        Controls.Add(installedCard)
        Controls.Add(searchCard)

        versionBox.Text = Config.ServerVersion

        AddHandler addButton.Click, AddressOf AddPluginsFromDialog
        AddHandler toggleButton.Click, AddressOf ToggleSelected
        AddHandler removeButton.Click, AddressOf RemoveSelected
        AddHandler folderButton.Click,
            Sub(s, e)
                Directory.CreateDirectory(Config.PluginsFolder)
                OpenInExplorer(Config.PluginsFolder)
            End Sub
        AddHandler installedList.SelectedIndexChanged, Sub(s, e) UpdateButtons()
        AddHandler installedList.DoubleClick, AddressOf ToggleSelected
        AddHandler installedList.DragEnter, AddressOf InstalledList_DragEnter
        AddHandler installedList.DragDrop, AddressOf InstalledList_DragDrop
        AddHandler installedList.KeyDown,
            Sub(s, e)
                If e.KeyCode = Keys.Delete Then RemoveSelected(s, e)
            End Sub

        AddHandler searchButton.Click, Sub(s, e) SearchAsync()
        AddHandler searchBox.KeyDown, AddressOf SearchInput_KeyDown
        AddHandler versionBox.KeyDown, AddressOf SearchInput_KeyDown
        AddHandler resultList.SelectedIndexChanged, Sub(s, e) UpdateButtons()
        AddHandler resultList.DoubleClick, Sub(s, e) InstallSelectedAsync()
        AddHandler installButton.Click, Sub(s, e) InstallSelectedAsync()
        AddHandler pageButton.Click,
            Sub(s, e)
                Dim project = SelectedProject()
                If project IsNot Nothing Then OpenInExplorer("https://modrinth.com/plugin/" & project.Slug)
            End Sub

        ReloadInstalled()
    End Sub

    Friend Overrides Sub OnShown()
        If versionBox.Text.Length = 0 Then versionBox.Text = Config.ServerVersion
        ReloadInstalled()
        If Not searchedOnce Then SearchAsync()
    End Sub

    ' ---------- Layout ----------

    Protected Overrides Sub OnLayout(levent As LayoutEventArgs)
        MyBase.OnLayout(levent)
        If searchCard Is Nothing OrElse pageButton Is Nothing Then Return

        Dim top = HeaderHeight
        If warningLabel.Visible Then
            warningLabel.SetBounds(Pad, top, ClientSize.Width - Pad * 2, 40)
            top += 52
        End If
        Dim cardHeight = Math.Max(300, ClientSize.Height - top - Pad)
        Dim available = ClientSize.Width - Pad * 2 - 16
        Dim leftWidth = Math.Max(320, CInt(available * 0.42))
        installedCard.SetBounds(Pad, top, leftWidth, cardHeight)
        searchCard.SetBounds(Pad + leftWidth + 16, top, Math.Max(300, available - leftWidth), cardHeight)

        ' Installed card
        Dim w = leftWidth - 32
        Dim bottom = cardHeight - 16
        restartHint.SetBounds(16, bottom - 18, w, 18)
        Dim buttonsY = restartHint.Top - 8 - 36
        addButton.SetBounds(16, buttonsY, 130, 36)
        toggleButton.SetBounds(addButton.Right + 8, buttonsY, 100, 36)
        folderButton.SetBounds(16 + w - 36, buttonsY, 36, 36)
        removeButton.SetBounds(folderButton.Left - 8 - 36, buttonsY, 36, 36)
        installedList.SetBounds(16, installedCard.ContentTop, w, buttonsY - 12 - installedCard.ContentTop)

        ' Search card
        Dim cw = searchCard.Width - 32
        searchButton.SetBounds(16 + cw - 36, searchCard.ContentTop, 36, 34)
        versionBox.SetBounds(searchButton.Left - 8 - 110, searchCard.ContentTop, 110, 34)
        searchBox.SetBounds(16, searchCard.ContentTop, versionBox.Left - 8 - 16, 34)
        installButton.SetBounds(16 + cw - 150, bottom - 36, 150, 36)
        pageButton.SetBounds(installButton.Left - 8 - 120, bottom - 36, 120, 36)
        statusLabel.SetBounds(16, bottom - 36, Math.Max(50, pageButton.Left - 24), 36)
        statusLabel.TextAlign = ContentAlignment.MiddleLeft
        resultList.SetBounds(16, searchBox.Bottom + 10, cw, installButton.Top - 12 - searchBox.Bottom - 10)
    End Sub

    ' ---------- Installed plugins ----------

    Friend Sub ReloadInstalled()
        Dim selectedFile = TryCast(TryCast(installedList.SelectedItem, ListEntry)?.Tag, String)

        installedList.BeginUpdate()
        installedList.Items.Clear()
        If Directory.Exists(Config.PluginsFolder) Then
            Dim files = Directory.GetFiles(Config.PluginsFolder).
                Where(Function(f) f.EndsWith(".jar", StringComparison.OrdinalIgnoreCase) OrElse
                                  f.EndsWith(".jar" & DisabledSuffix, StringComparison.OrdinalIgnoreCase)).
                OrderBy(Function(f) Path.GetFileName(f), StringComparer.OrdinalIgnoreCase)
            For Each pluginFile In files
                Dim name = Path.GetFileName(pluginFile)
                Dim disabled = name.EndsWith(DisabledSuffix, StringComparison.OrdinalIgnoreCase)
                Dim jarName = If(disabled, name.Substring(0, name.Length - DisabledSuffix.Length), name)
                Dim entry As New ListEntry With {
                    .Title = Path.GetFileNameWithoutExtension(jarName),
                    .Subtitle = name,
                    .Detail = FormatSize(New FileInfo(pluginFile).Length),
                    .Tag = pluginFile,
                    .Dimmed = disabled
                }
                If disabled Then
                    entry.Badge = "Aus"
                    entry.BadgeColor = Theme.TextMuted
                End If
                installedList.Items.Add(entry)
                If String.Equals(pluginFile, selectedFile, StringComparison.OrdinalIgnoreCase) Then
                    installedList.SelectedIndex = installedList.Items.Count - 1
                End If
            Next
        End If
        installedList.EndUpdate()

        Dim count = installedList.Items.Count
        installedCard.Subtitle = If(count = 0, "Ziehe .jar-Dateien hierher, um sie hinzuzufügen",
                                    $"{count} Plugin{If(count = 1, "", "s")} · Ziehe .jar-Dateien hierher")

        Dim showWarning = (Config.ServerType = "vanilla")
        If warningLabel.Visible <> showWarning Then
            warningLabel.Visible = showWarning
            PerformLayout()
        End If
        UpdateButtons()
    End Sub

    Private Shared Function FormatSize(bytes As Long) As String
        If bytes >= 1024L * 1024L Then Return (bytes / 1024.0 / 1024.0).ToString("0.0", CultureInfo.GetCultureInfo("de-DE")) & " MB"
        Return Math.Max(1, bytes \ 1024).ToString() & " KB"
    End Function

    Private Function SelectedInstalledFile() As String
        Return TryCast(TryCast(installedList.SelectedItem, ListEntry)?.Tag, String)
    End Function

    Private Sub AddPluginsFromDialog(sender As Object, e As EventArgs)
        Using dialog As New OpenFileDialog() With {
            .Title = "Plugins auswählen",
            .Filter = "Plugins (*.jar)|*.jar",
            .Multiselect = True
        }
            If dialog.ShowDialog(Me) <> DialogResult.OK Then Return
            CopyPlugins(dialog.FileNames)
        End Using
    End Sub

    Private Sub CopyPlugins(sourceFiles As IEnumerable(Of String))
        Directory.CreateDirectory(Config.PluginsFolder)
        Dim copied = 0
        For Each source In sourceFiles
            If Not source.EndsWith(".jar", StringComparison.OrdinalIgnoreCase) Then Continue For
            Dim target = Path.Combine(Config.PluginsFolder, Path.GetFileName(source))
            Try
                If String.Equals(Path.GetFullPath(source), Path.GetFullPath(target), StringComparison.OrdinalIgnoreCase) Then Continue For
                If File.Exists(target) AndAlso MessageBox.Show(Me, $"{Path.GetFileName(source)} ist schon installiert. Ersetzen?",
                                                                "Plugin ersetzen", MessageBoxButtons.YesNo, MessageBoxIcon.Question) <> DialogResult.Yes Then
                    Continue For
                End If
                File.Copy(source, target, True)
                copied += 1
            Catch ex As Exception
                ShowError(Me, "Plugin konnte nicht kopiert werden", ex)
            End Try
        Next
        ReloadInstalled()
        If copied > 0 Then SetStatus($"{copied} Plugin{If(copied = 1, "", "s")} hinzugefügt.", Theme.Accent)
    End Sub

    Private Sub ToggleSelected(sender As Object, e As EventArgs)
        Dim pluginFile = SelectedInstalledFile()
        If pluginFile Is Nothing Then Return
        Try
            Dim target = If(pluginFile.EndsWith(DisabledSuffix, StringComparison.OrdinalIgnoreCase),
                            pluginFile.Substring(0, pluginFile.Length - DisabledSuffix.Length),
                            pluginFile & DisabledSuffix)
            File.Move(pluginFile, target)
            ReloadInstalled()
            SelectInstalled(target)
        Catch ex As Exception
            ShowError(Me, "Plugin konnte nicht umgeschaltet werden (läuft der Server?)", ex)
        End Try
    End Sub

    Private Sub SelectInstalled(filePath As String)
        For i = 0 To installedList.Items.Count - 1
            If String.Equals(CStr(DirectCast(installedList.Items(i), ListEntry).Tag), filePath, StringComparison.OrdinalIgnoreCase) Then
                installedList.SelectedIndex = i
                Return
            End If
        Next
    End Sub

    Private Sub RemoveSelected(sender As Object, e As EventArgs)
        Dim pluginFile = SelectedInstalledFile()
        If pluginFile Is Nothing Then Return
        Dim answer = MessageBox.Show(Me, $"{Path.GetFileName(pluginFile)} in den Papierkorb verschieben?" & Environment.NewLine &
                                     "Der Konfigurationsordner des Plugins bleibt erhalten.",
                                     "Plugin entfernen", MessageBoxButtons.YesNo, MessageBoxIcon.Question)
        If answer <> DialogResult.Yes Then Return
        Try
            Microsoft.VisualBasic.FileIO.FileSystem.DeleteFile(pluginFile,
                Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs,
                Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin)
        Catch ex As Exception
            ShowError(Me, "Plugin konnte nicht entfernt werden (läuft der Server?)", ex)
        End Try
        ReloadInstalled()
    End Sub

    Private Sub InstalledList_DragEnter(sender As Object, e As DragEventArgs)
        If e.Data.GetDataPresent(DataFormats.FileDrop) Then
            Dim files = DirectCast(e.Data.GetData(DataFormats.FileDrop), String())
            If files.Any(Function(f) f.EndsWith(".jar", StringComparison.OrdinalIgnoreCase)) Then
                e.Effect = DragDropEffects.Copy
                Return
            End If
        End If
        e.Effect = DragDropEffects.None
    End Sub

    Private Sub InstalledList_DragDrop(sender As Object, e As DragEventArgs)
        If Not e.Data.GetDataPresent(DataFormats.FileDrop) Then Return
        CopyPlugins(DirectCast(e.Data.GetData(DataFormats.FileDrop), String()))
    End Sub

    ' ---------- Modrinth ----------

    Private Function SelectedProject() As PluginProject
        Return TryCast(TryCast(resultList.SelectedItem, ListEntry)?.Tag, PluginProject)
    End Function

    Private Sub SearchInput_KeyDown(sender As Object, e As KeyEventArgs)
        If e.KeyCode = Keys.Enter Then
            e.SuppressKeyPress = True
            SearchAsync()
        End If
    End Sub

    Private Async Sub SearchAsync()
        searchedOnce = True
        searchVersion += 1
        Dim mySearch = searchVersion
        Dim query = searchBox.Text.Trim()
        Dim gameVersion = versionBox.Text.Trim()

        resultList.Items.Clear()
        resultList.EmptyText = "Suche …"
        resultList.Invalidate()
        SetStatus("", Theme.TextMuted)

        Try
            Dim results = Await WebApi.SearchPluginsAsync(query, gameVersion)
            If mySearch <> searchVersion Then Return

            Dim installedNames = installedList.Items.Cast(Of ListEntry)().Select(Function(x) x.Title.ToLowerInvariant()).ToList()
            resultList.BeginUpdate()
            For Each project In results
                Dim entry As New ListEntry With {
                    .Title = project.Title,
                    .Subtitle = If(project.Author.Length > 0, "von " & project.Author & " · ", "") & project.Description,
                    .Detail = FormatDownloads(project.Downloads),
                    .Tag = project
                }
                Dim key = project.Title.ToLowerInvariant().Replace(" ", "")
                If installedNames.Any(Function(n) n.Replace(" ", "").StartsWith(key) OrElse n.StartsWith(project.Slug.ToLowerInvariant())) Then
                    entry.Badge = "Installiert"
                    entry.BadgeColor = Theme.Info
                End If
                resultList.Items.Add(entry)
            Next
            resultList.EndUpdate()
            resultList.EmptyText = If(gameVersion.Length > 0, $"Nichts für Minecraft {gameVersion} gefunden", "Nichts gefunden")
            If query.Length = 0 Then
                SetStatus("Beliebteste Plugins" & If(gameVersion.Length > 0, " für " & gameVersion, ""), Theme.TextMuted)
            End If
        Catch ex As Exception
            If mySearch <> searchVersion Then Return
            resultList.EmptyText = "Suche fehlgeschlagen:" & Environment.NewLine & ex.Message
            resultList.Invalidate()
        End Try
        UpdateButtons()
    End Sub

    Private Shared Function FormatDownloads(count As Long) As String
        Dim de = CultureInfo.GetCultureInfo("de-DE")
        If count >= 1000000 Then Return (count / 1000000.0).ToString("0.0", de) & " Mio. ⬇"
        If count >= 1000 Then Return (count / 1000.0).ToString("0", de) & " Tsd. ⬇"
        Return count.ToString(de) & " ⬇"
    End Function

    Private Async Sub InstallSelectedAsync()
        Dim project = SelectedProject()
        If project Is Nothing OrElse busy Then Return

        busy = True
        UpdateButtons()
        Dim gameVersion = versionBox.Text.Trim()
        SetStatus($"Suche passende Version von {project.Title} …", Theme.TextMuted)
        Try
            Dim remote = Await WebApi.GetPluginFileAsync(project, gameVersion)
            If remote Is Nothing OrElse String.IsNullOrEmpty(remote.Url) Then
                SetStatus($"{project.Title} hat keine Version für Minecraft {If(gameVersion.Length > 0, gameVersion, "?")}.", Theme.Warning)
                Return
            End If

            Dim target = Path.Combine(Config.PluginsFolder, remote.FileName)
            If File.Exists(target) Then
                SetStatus($"{remote.FileName} ist bereits installiert.", Theme.Info)
                Return
            End If

            Dim reporter As New Progress(Of Double)(Sub(percent) SetStatus($"Lade {remote.Label} … {percent:0} %", Theme.TextMuted))
            Await WebApi.DownloadAsync(remote.Url, target, reporter, CancellationToken.None)
            ReloadInstalled()
            SelectInstalled(target)
            SetStatus($"{remote.Label} installiert. Starte den Server neu, um es zu laden.", Theme.Accent)

            Dim entry = TryCast(resultList.SelectedItem, ListEntry)
            If entry IsNot Nothing Then
                entry.Badge = "Installiert"
                entry.BadgeColor = Theme.Info
                resultList.Invalidate()
            End If
        Catch ex As Exception
            SetStatus("Fehler: " & ex.Message, Theme.Danger)
        Finally
            busy = False
            UpdateButtons()
        End Try
    End Sub

    ' ---------- Helpers ----------

    Private Sub SetStatus(text As String, textColor As Color)
        statusLabel.Text = text
        statusLabel.ForeColor = textColor
    End Sub

    Private Sub UpdateButtons()
        Dim hasInstalled = installedList.SelectedItem IsNot Nothing
        toggleButton.Enabled = hasInstalled
        removeButton.Enabled = hasInstalled
        Dim hasResult = resultList.SelectedItem IsNot Nothing
        installButton.Enabled = hasResult AndAlso Not busy
        pageButton.Enabled = hasResult
    End Sub

End Class
