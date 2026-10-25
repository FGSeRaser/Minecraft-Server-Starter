''' <summary>Every server.properties option with a fitting editor.</summary>
Friend Class PropertiesPage
    Inherits PageBase

    Private Class ChoiceItem
        Public ReadOnly Value As String
        Public ReadOnly Display As String

        Public Sub New(value As String, display As String)
            Me.Value = value
            Me.Display = display
        End Sub

        Public Overrides Function ToString() As String
            Return Display
        End Function
    End Class

    ''' <summary>One editable line: label, description and the editor control.</summary>
    Private Class SettingRow
        Public Key As String
        Public Definition As PropertyDef   ' Nothing for unknown keys
        Public Card As CardPanel
        Public TitleLabel As Label
        Public DescriptionLabel As Label
        Public Editor As Control
        Public Visible As Boolean = True
    End Class

    Private ReadOnly searchBox As New DarkTextBox() With {.Placeholder = "Einstellung suchen, z. B. pvp, Spieler, Port"}
    Private ReadOnly reloadButton As ModernButton = MakeButton("Neu laden", ButtonKind.Secondary, Theme.IconRefresh)
    Private ReadOnly saveButton As ModernButton = MakeButton("Speichern", ButtonKind.Primary, Theme.IconSave)
    Private ReadOnly statusLabel As Label = MakeLabel("", small:=True)
    Private ReadOnly scroller As New ScrollPanel()

    Private ReadOnly cards As New Dictionary(Of String, CardPanel)()
    Private ReadOnly rows As New List(Of SettingRow)()
    Private loadedValues As New Dictionary(Of String, String)()
    Private fileExists As Boolean
    Private loading As Boolean
    Private dirty As Boolean
    Private stale As Boolean = True

    Public Sub New(owner As MainForm)
        MyBase.New(owner, "Einstellungen", "Alle Optionen aus server.properties – ohne Texteditor")

        Controls.Add(searchBox)
        Controls.Add(reloadButton)
        Controls.Add(saveButton)
        Controls.Add(statusLabel)
        Controls.Add(scroller)

        For Each category In ServerProperties.Categories
            AddCard(category)
        Next
        AddCard(ServerProperties.OtherCategory)

        For Each def In ServerProperties.Definitions
            AddRow(def.Key, def, cards(def.Category))
        Next

        AddHandler searchBox.TextChanged, Sub(s, e) ApplyFilter()
        AddHandler reloadButton.Click,
            Sub(s, e)
                If dirty AndAlso MessageBox.Show(Me, "Ungespeicherte Änderungen verwerfen?", "Neu laden",
                                                 MessageBoxButtons.YesNo, MessageBoxIcon.Question) <> DialogResult.Yes Then Return
                LoadValues()
            End Sub
        AddHandler saveButton.Click, Sub(s, e) SaveValues()
        AddHandler scroller.Resize, Sub(s, e) LayoutRows()
        saveButton.Enabled = False
    End Sub

    Private ReadOnly Property PropertiesPath As String
        Get
            Return Path.Combine(Config.ServerFolder, "server.properties")
        End Get
    End Property

    Friend Overrides Sub OnShown()
        If stale AndAlso Not dirty Then LoadValues()
    End Sub

    ''' <summary>The file may have changed (other folder, server restarted).</summary>
    Friend Sub MarkStale()
        stale = True
        If Visible AndAlso Not dirty Then LoadValues()
    End Sub

    ' ---------- Building the editors ----------

    Private Sub AddCard(category As String)
        Dim card As New CardPanel() With {.Title = category}
        cards(category) = card
        scroller.Controls.Add(card)
    End Sub

    Private Function AddRow(key As String, def As PropertyDef, card As CardPanel) As SettingRow
        Dim row As New SettingRow With {.Key = key, .Definition = def, .Card = card}

        row.TitleLabel = MakeLabel(If(def IsNot Nothing, def.Label, key), muted:=False)
        row.TitleLabel.Font = Theme.BodyBoldFont
        Dim description = If(def IsNot Nothing AndAlso def.Description.Length > 0, key & "  ·  " & def.Description, key)
        row.DescriptionLabel = MakeLabel(description, small:=True)
        row.DescriptionLabel.ForeColor = Theme.TextDim

        row.Editor = CreateEditor(def)
        card.Controls.Add(row.TitleLabel)
        card.Controls.Add(row.DescriptionLabel)
        card.Controls.Add(row.Editor)
        rows.Add(row)
        Return row
    End Function

    Private Function CreateEditor(def As PropertyDef) As Control
        Dim kind = If(def Is Nothing, PropertyKind.Text, def.Kind)
        Select Case kind
            Case PropertyKind.Bool
                Dim toggle As New ToggleSwitch() With {.Size = New System.Drawing.Size(46, 24)}
                AddHandler toggle.CheckedChanged, AddressOf Editor_Changed
                Return toggle

            Case PropertyKind.Int
                Dim box As New NumberBox() With {.Minimum = def.Minimum, .Maximum = def.Maximum, .Size = New System.Drawing.Size(150, 34)}
                AddHandler box.TextChanged, AddressOf Editor_Changed
                Return box

            Case PropertyKind.Choice
                Dim combo As New DarkComboBox() With {.Width = 260}
                For Each choice In def.Choices
                    combo.Items.Add(New ChoiceItem(choice.Key, choice.Value))
                Next
                AddHandler combo.SelectedIndexChanged, AddressOf Editor_Changed
                Return combo

            Case Else
                Dim box As New DarkTextBox() With {.Size = New System.Drawing.Size(300, 34)}
                If kind = PropertyKind.Password Then box.UsePassword = True
                AddHandler box.TextChanged, AddressOf Editor_Changed
                Return box
        End Select
    End Function

    Private Sub Editor_Changed(sender As Object, e As EventArgs)
        If loading Then Return
        SetDirty(True)
    End Sub

    Private Sub SetDirty(value As Boolean)
        dirty = value
        saveButton.Enabled = value
        If value Then
            SetStatus("Ungespeicherte Änderungen", Theme.Warning)
        End If
    End Sub

    Private Sub SetStatus(text As String, textColor As Color)
        statusLabel.Text = text
        statusLabel.ForeColor = textColor
    End Sub

    ' ---------- Editor values ----------

    Private Shared Function GetEditorValue(row As SettingRow) As String
        Dim toggle = TryCast(row.Editor, ToggleSwitch)
        If toggle IsNot Nothing Then Return If(toggle.Checked, "true", "false")

        Dim number = TryCast(row.Editor, NumberBox)
        If number IsNot Nothing Then
            If number.Text.Trim().Length = 0 Then Return If(row.Definition IsNot Nothing, row.Definition.DefaultValue, "")
            Return number.Value.ToString()
        End If

        Dim combo = TryCast(row.Editor, DarkComboBox)
        If combo IsNot Nothing Then
            Dim item = TryCast(combo.SelectedItem, ChoiceItem)
            Return If(item Is Nothing, "", item.Value)
        End If

        Return row.Editor.Text
    End Function

    Private Shared Sub SetEditorValue(row As SettingRow, value As String)
        Dim toggle = TryCast(row.Editor, ToggleSwitch)
        If toggle IsNot Nothing Then
            toggle.Checked = String.Equals(value, "true", StringComparison.OrdinalIgnoreCase)
            Return
        End If

        Dim combo = TryCast(row.Editor, DarkComboBox)
        If combo IsNot Nothing Then
            Dim index = -1
            For i = 0 To combo.Items.Count - 1
                If DirectCast(combo.Items(i), ChoiceItem).Value = value Then index = i
            Next
            If index < 0 AndAlso value.Length > 0 Then
                ' Value from a newer/older Minecraft version: keep it selectable
                combo.Items.Add(New ChoiceItem(value, value))
                index = combo.Items.Count - 1
            End If
            combo.SelectedIndex = index
            Return
        End If

        row.Editor.Text = value
    End Sub

    ' ---------- Load / save ----------

    Private Sub LoadValues()
        loading = True
        Try
            fileExists = File.Exists(PropertiesPath)
            Try
                loadedValues = ServerProperties.Load(PropertiesPath)
            Catch ex As Exception
                loadedValues = New Dictionary(Of String, String)()
                ShowError(Me, "server.properties konnte nicht gelesen werden", ex)
            End Try

            ' Rows for keys this app does not know (plugins, newer versions)
            For Each row In rows.Where(Function(r) r.Definition Is Nothing).ToList()
                row.Card.Controls.Remove(row.TitleLabel)
                row.Card.Controls.Remove(row.DescriptionLabel)
                row.Card.Controls.Remove(row.Editor)
                row.TitleLabel.Dispose()
                row.DescriptionLabel.Dispose()
                row.Editor.Dispose()
                rows.Remove(row)
            Next
            For Each key In loadedValues.Keys.OrderBy(Function(k) k)
                If ServerProperties.FindDefinition(key) Is Nothing Then
                    AddRow(key, Nothing, cards(ServerProperties.OtherCategory))
                End If
            Next

            For Each row In rows
                Dim value As String = Nothing
                If Not loadedValues.TryGetValue(row.Key, value) Then
                    value = If(row.Definition IsNot Nothing, row.Definition.DefaultValue, "")
                End If
                SetEditorValue(row, value)
            Next
        Finally
            loading = False
        End Try

        stale = False
        SetDirty(False)
        If fileExists Then
            SetStatus($"Geladen aus {PropertiesPath}", Theme.TextMuted)
        Else
            SetStatus("Noch keine server.properties – du kannst trotzdem alles einstellen, sie wird beim Speichern angelegt.", Theme.Info)
        End If
        ApplyFilter()
    End Sub

    Private Sub SaveValues()
        Dim result As New Dictionary(Of String, String)(loadedValues, StringComparer.Ordinal)
        For Each row In rows
            Dim value = GetEditorValue(row)
            If row.Definition Is Nothing Then
                result(row.Key) = value
            ElseIf loadedValues.ContainsKey(row.Key) OrElse value <> row.Definition.DefaultValue Then
                ' Only write keys that existed or were changed, so the server keeps its own defaults
                result(row.Key) = value
            End If
        Next

        Try
            ServerProperties.Save(PropertiesPath, result)
        Catch ex As Exception
            ShowError(Me, "server.properties konnte nicht gespeichert werden", ex)
            Return
        End Try

        LoadValues()
        If Main.ServerPageView.State <> ServerState.Stopped Then
            SetStatus("Gespeichert. Starte den Server neu, damit die Änderungen gelten.", Theme.Warning)
        Else
            SetStatus("Gespeichert.", Theme.Accent)
        End If
    End Sub

    ' ---------- Filter and layout ----------

    Private Sub ApplyFilter()
        Dim filter = searchBox.Text.Trim()
        For Each row In rows
            row.Visible = filter.Length = 0 OrElse
                          row.Key.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0 OrElse
                          row.TitleLabel.Text.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0 OrElse
                          row.DescriptionLabel.Text.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0 OrElse
                          row.Card.Title.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0
        Next
        LayoutRows()
    End Sub

    Protected Overrides Sub OnLayout(levent As LayoutEventArgs)
        MyBase.OnLayout(levent)
        If scroller Is Nothing OrElse statusLabel Is Nothing Then Return

        Dim top = HeaderHeight
        saveButton.SetBounds(ClientSize.Width - Pad - 130, top, 130, 34)
        reloadButton.SetBounds(saveButton.Left - 8 - 130, top, 130, 34)
        searchBox.SetBounds(Pad, top, Math.Min(380, reloadButton.Left - 16 - Pad), 34)
        statusLabel.SetBounds(Pad, top + 42, ClientSize.Width - Pad * 2, 18)
        scroller.SetBounds(Pad, top + 68, ClientSize.Width - Pad, Math.Max(100, ClientSize.Height - top - 68))
    End Sub

    Private Sub LayoutRows()
        If scroller.Width < 100 Then Return
        scroller.SuspendLayout()
        Dim scrollY = scroller.AutoScrollPosition.Y
        ' Always leave room for the vertical scrollbar, so no horizontal one appears
        Dim cardWidth = scroller.Width - SystemInformation.VerticalScrollBarWidth - 8
        Dim y = scrollY

        For Each pair In cards
            Dim card = pair.Value
            Dim cardRows = rows.Where(Function(r) r.Card Is card).ToList()
            Dim visibleRows = cardRows.Where(Function(r) r.Visible).ToList()

            For Each row In cardRows.Except(visibleRows)
                row.TitleLabel.Visible = False
                row.DescriptionLabel.Visible = False
                row.Editor.Visible = False
            Next

            If visibleRows.Count = 0 Then
                card.Visible = False
                Continue For
            End If

            card.Visible = True
            Dim rowY = card.ContentTop - 4
            For Each row In visibleRows
                Dim editor = row.Editor
                Dim labelWidth = Math.Max(120, cardWidth - 32 - editor.Width - 24)
                Const rowHeight As Integer = 54
                row.TitleLabel.SetBounds(16, rowY + 8, labelWidth, 20)
                row.DescriptionLabel.SetBounds(16, rowY + 29, labelWidth, 18)
                editor.Location = New Point(cardWidth - 16 - editor.Width, rowY + (rowHeight - editor.Height) \ 2)
                row.TitleLabel.Visible = True
                row.DescriptionLabel.Visible = True
                editor.Visible = True
                rowY += rowHeight
            Next

            card.SetBounds(0, y, cardWidth, rowY + 10)
            y += card.Height + 16
        Next

        scroller.AutoScrollMinSize = New System.Drawing.Size(0, y - scrollY)
        scroller.ResumeLayout()
    End Sub

End Class
