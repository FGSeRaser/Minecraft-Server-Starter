Imports System.Globalization
Imports System.Threading

''' <summary>Downloads Paper or Vanilla server jars in any version.</summary>
Friend Class DownloadPage
    Inherits PageBase

    Private Const TypePaper As String = "paper"
    Private Const TypeVanilla As String = "vanilla"

    Private ReadOnly typeCard As New CardPanel() With {.Title = "Server-Software"}
    Private ReadOnly typeList As New EntryListBox() With {.ItemHeight = 56}
    Private ReadOnly previewToggle As New ToggleSwitch() With {.Text = "Vorabversionen und Snapshots zeigen"}
    Private ReadOnly typeInfo As Label = MakeLabel("", small:=True)

    Private ReadOnly versionCard As New CardPanel() With {.Title = "Version", .Subtitle = "Wähle die Minecraft-Version deines Servers"}
    Private ReadOnly filterBox As New DarkTextBox() With {.Placeholder = "Version suchen, z. B. 1.21"}
    Private ReadOnly versionList As New EntryListBox() With {.ItemHeight = 46, .EmptyText = "Lade Versionen …"}
    Private ReadOnly progressBar As New SlimProgress()
    Private ReadOnly statusLabel As Label = MakeLabel("", small:=True)
    Private ReadOnly downloadButton As ModernButton = MakeButton("Herunterladen", ButtonKind.Primary, Theme.IconDownload)
    Private ReadOnly cancelButton As ModernButton = MakeButton("Abbrechen", ButtonKind.Secondary)
    Private ReadOnly toServerButton As ModernButton = MakeButton("Zum Server", ButtonKind.Secondary, Theme.IconPlay)

    Private ReadOnly cache As New Dictionary(Of String, List(Of GameVersion))()
    Private loadingType As String = ""
    Private downloadCts As CancellationTokenSource

    Public Sub New(owner As MainForm)
        MyBase.New(owner, "Download", "Server-Datei in beliebiger Version herunterladen")

        typeList.Items.Add(New ListEntry With {
            .Title = "Paper", .Badge = "Empfohlen", .Tag = TypePaper,
            .Subtitle = "Schnell und stabil, unterstützt Bukkit- und Spigot-Plugins"})
        typeList.Items.Add(New ListEntry With {
            .Title = "Vanilla", .Tag = TypeVanilla,
            .Subtitle = "Original-Server von Mojang, ohne Plugins"})

        typeInfo.Text = "Die Datei wird in deinen Serverordner geladen und automatisch als Server-Datei ausgewählt." &
                        Environment.NewLine & Environment.NewLine &
                        "Paper ist mit Spigot- und Bukkit-Plugins kompatibel und läuft deutlich flüssiger als Vanilla. " &
                        "Welten lassen sich zwischen beiden einfach weiterverwenden."

        progressBar.Visible = False
        cancelButton.Visible = False
        toServerButton.Visible = False

        typeCard.Controls.AddRange(New Control() {typeList, previewToggle, typeInfo})
        versionCard.Controls.AddRange(New Control() {filterBox, versionList, progressBar, statusLabel, downloadButton, cancelButton, toServerButton})
        Controls.Add(typeCard)
        Controls.Add(versionCard)

        typeList.SelectedIndex = If(Config.ServerType = TypeVanilla, 1, 0)

        AddHandler typeList.SelectedIndexChanged, Sub(s, e) LoadVersions()
        AddHandler previewToggle.CheckedChanged, Sub(s, e) FillVersionList()
        AddHandler filterBox.TextChanged, Sub(s, e) FillVersionList()
        AddHandler versionList.SelectedIndexChanged, Sub(s, e) UpdateButtons()
        AddHandler versionList.DoubleClick, Sub(s, e) StartDownload()
        AddHandler downloadButton.Click, Sub(s, e) StartDownload()
        AddHandler cancelButton.Click, Sub(s, e) downloadCts?.Cancel()
        AddHandler toServerButton.Click, Sub(s, e) Main.ShowPage(Main.ServerPageView)
        UpdateButtons()
    End Sub

    Friend Overrides Sub OnShown()
        LoadVersions()
    End Sub

    Private ReadOnly Property SelectedType As String
        Get
            Dim entry = TryCast(typeList.SelectedItem, ListEntry)
            Return If(entry Is Nothing, TypePaper, CStr(entry.Tag))
        End Get
    End Property

    ' ---------- Layout ----------

    Protected Overrides Sub OnLayout(levent As LayoutEventArgs)
        MyBase.OnLayout(levent)
        If versionCard Is Nothing OrElse toServerButton Is Nothing Then Return

        Dim top = HeaderHeight
        Dim cardHeight = Math.Max(300, ClientSize.Height - top - Pad)
        Const leftWidth As Integer = 340
        typeCard.SetBounds(Pad, top, leftWidth, cardHeight)
        versionCard.SetBounds(Pad + leftWidth + 16, top, Math.Max(300, ClientSize.Width - leftWidth - 16 - Pad * 2), cardHeight)

        Dim w = leftWidth - 32
        Dim y = typeCard.ContentTop
        typeList.SetBounds(16, y, w, typeList.ItemHeight * 2 + 4) : y += typeList.Height + 16
        previewToggle.SetBounds(16, y, w, 24) : y += 40
        typeInfo.SetBounds(16, y, w, cardHeight - y - 16)

        Dim cw = versionCard.Width - 32
        Dim bottom = cardHeight - 16
        downloadButton.SetBounds(16 + cw - 170, bottom - 38, 170, 38)
        cancelButton.SetBounds(downloadButton.Left - 8 - 110, bottom - 38, 110, 38)
        toServerButton.SetBounds(downloadButton.Left - 8 - 130, bottom - 38, 130, 38)
        statusLabel.SetBounds(16, bottom - 38, Math.Max(50, cancelButton.Left - 32), 22)
        progressBar.SetBounds(16, bottom - 12, Math.Max(50, cancelButton.Left - 32), 6)

        filterBox.SetBounds(16, versionCard.ContentTop, cw, 34)
        versionList.SetBounds(16, filterBox.Bottom + 10, cw, bottom - 38 - 16 - filterBox.Bottom - 10)
    End Sub

    ' ---------- Versions ----------

    Private Async Sub LoadVersions()
        Dim serverType = SelectedType
        If cache.ContainsKey(serverType) Then
            FillVersionList()
            Return
        End If
        If loadingType = serverType Then Return

        loadingType = serverType
        versionList.Items.Clear()
        versionList.EmptyText = "Lade Versionen …"
        versionList.Invalidate()
        SetStatus("", Theme.TextMuted)

        Try
            Dim versions = If(serverType = TypePaper, Await WebApi.GetPaperVersionsAsync(), Await WebApi.GetVanillaVersionsAsync())
            cache(serverType) = versions
        Catch ex As Exception
            versionList.EmptyText = "Versionen konnten nicht geladen werden." & Environment.NewLine & ex.Message
            versionList.Invalidate()
            SetStatus("Keine Verbindung? Wechsle den Server-Typ, um es erneut zu versuchen.", Theme.Danger)
        Finally
            loadingType = ""
        End Try

        If serverType = SelectedType AndAlso cache.ContainsKey(serverType) Then FillVersionList()
    End Sub

    Private Sub FillVersionList()
        Dim serverType = SelectedType
        Dim versions As List(Of GameVersion) = Nothing
        If Not cache.TryGetValue(serverType, versions) Then Return

        Dim previous = TryCast(versionList.SelectedItem, ListEntry)
        Dim previousId = If(previous Is Nothing, "", DirectCast(previous.Tag, GameVersion).Id)
        Dim filter = filterBox.Text.Trim()
        Dim newestRelease = versions.FirstOrDefault(Function(v) v.IsRelease)

        versionList.BeginUpdate()
        versionList.Items.Clear()
        For Each v In versions
            If Not previewToggle.Checked AndAlso Not v.IsRelease Then Continue For
            If filter.Length > 0 AndAlso v.Id.IndexOf(filter, StringComparison.OrdinalIgnoreCase) < 0 Then Continue For

            Dim entry As New ListEntry With {.Title = v.Id, .Tag = v, .Dimmed = Not v.IsRelease}
            entry.Subtitle = DescribeVersion(v, serverType)
            If v Is newestRelease Then
                entry.Badge = "Neueste"
            End If
            If Config.ServerType = serverType AndAlso Config.ServerVersion = v.Id AndAlso Not String.IsNullOrEmpty(Config.JarName) Then
                entry.Badge = "Aktuell"
                entry.BadgeColor = Theme.Info
            End If
            versionList.Items.Add(entry)
        Next
        versionList.EndUpdate()
        versionList.EmptyText = "Keine passende Version gefunden"

        ' Keep the selection, otherwise select the newest release
        Dim selectIndex = -1
        For i = 0 To versionList.Items.Count - 1
            Dim id = DirectCast(DirectCast(versionList.Items(i), ListEntry).Tag, GameVersion).Id
            If id = previousId Then selectIndex = i
        Next
        If selectIndex < 0 AndAlso versionList.Items.Count > 0 Then selectIndex = 0
        versionList.SelectedIndex = selectIndex

        versionCard.Subtitle = $"{versionList.Items.Count} Versionen · {If(serverType = TypePaper, "Paper", "Vanilla")}"
        UpdateButtons()
    End Sub

    Private Shared Function DescribeVersion(v As GameVersion, serverType As String) As String
        Dim kind As String
        Select Case v.Type
            Case "release" : kind = If(serverType = TypePaper, "Stabile Version", "Release")
            Case "snapshot" : kind = If(serverType = TypePaper, "Vorabversion", "Snapshot")
            Case "old_beta" : kind = "Beta"
            Case "old_alpha" : kind = "Alpha"
            Case Else : kind = v.Type
        End Select
        If v.ReleaseTime > DateTime.MinValue Then
            kind &= " · " & v.ReleaseTime.ToString("d. MMMM yyyy", CultureInfo.GetCultureInfo("de-DE"))
        End If
        Return kind
    End Function

    ' ---------- Download ----------

    Private Sub UpdateButtons()
        Dim busy = downloadCts IsNot Nothing
        downloadButton.Enabled = Not busy AndAlso versionList.SelectedItem IsNot Nothing
        typeList.Enabled = Not busy
        versionList.Enabled = Not busy
        filterBox.Enabled = Not busy
        previewToggle.Enabled = Not busy
        cancelButton.Visible = busy
        progressBar.Visible = busy
        If busy Then toServerButton.Visible = False
    End Sub

    Private Sub SetStatus(text As String, textColor As Color)
        statusLabel.Text = text
        statusLabel.ForeColor = textColor
    End Sub

    Private Async Sub StartDownload()
        If downloadCts IsNot Nothing Then Return
        Dim entry = TryCast(versionList.SelectedItem, ListEntry)
        If entry Is Nothing Then Return
        Dim version = DirectCast(entry.Tag, GameVersion)
        Dim serverType = SelectedType

        downloadCts = New CancellationTokenSource()
        progressBar.Value = 0
        toServerButton.Visible = False
        UpdateButtons()
        SetStatus("Suche Download …", Theme.TextMuted)

        Try
            Dim remote = If(serverType = TypePaper,
                            Await WebApi.GetPaperServerAsync(version.Id),
                            Await WebApi.GetVanillaServerAsync(version))
            If remote Is Nothing Then
                SetStatus($"Für {version.Id} gibt es keinen Server-Download.", Theme.Warning)
                Return
            End If

            If Main.ServerPageView.State <> ServerState.Stopped AndAlso
               String.Equals(remote.FileName, Config.JarName, StringComparison.OrdinalIgnoreCase) Then
                SetStatus("Diese Datei wird gerade vom laufenden Server benutzt. Stoppe ihn zuerst.", Theme.Warning)
                Return
            End If

            Dim target = Path.Combine(Config.ServerFolder, remote.FileName)
            Dim reporter As New Progress(Of Double)(
                Sub(percent)
                    progressBar.Value = percent
                    SetStatus($"Lade {remote.Label} … {percent:0} %", Theme.TextMuted)
                End Sub)
            Await WebApi.DownloadAsync(remote.Url, target, reporter, downloadCts.Token)

            Config.JarName = remote.FileName
            Config.ServerType = serverType
            Config.ServerVersion = version.Id
            Main.NotifyServerFilesChanged()

            SetStatus($"Fertig: {remote.Label} ist als Server-Datei ausgewählt.", Theme.Accent)
            toServerButton.Visible = True
            FillVersionList()
        Catch ex As OperationCanceledException
            SetStatus("Download abgebrochen.", Theme.TextMuted)
        Catch ex As Exception
            SetStatus("Fehler: " & ex.Message, Theme.Danger)
        Finally
            downloadCts?.Dispose()
            downloadCts = Nothing
            UpdateButtons()
        End Try
    End Sub

End Class
