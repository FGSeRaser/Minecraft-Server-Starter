Friend Enum ServerState
    Stopped
    Starting
    Running
    Stopping
End Enum

''' <summary>Main window: navigation on the left, one page at a time on the right.</summary>
Friend Class MainForm
    Inherits Form

    Friend ReadOnly Config As AppConfig = AppConfig.Load()

    Friend ReadOnly ServerPageView As ServerPage
    Friend ReadOnly DownloadPageView As DownloadPage
    Friend ReadOnly PluginsPageView As PluginsPage
    Friend ReadOnly PropertiesPageView As PropertiesPage

    Private ReadOnly sidebar As Panel
    Private ReadOnly authorLink As New GitHubLink()
    Private ReadOnly content As Panel
    Private ReadOnly navButtons As New List(Of NavButton)()
    Private ReadOnly pages As New List(Of PageBase)()
    Private closingAllowed As Boolean

    Public Sub New()
        Text = "Minecraft Server Starter"
        StartPosition = FormStartPosition.CenterScreen
        AutoScaleMode = AutoScaleMode.Dpi
        ClientSize = New System.Drawing.Size(1120, 720)
        MinimumSize = New System.Drawing.Size(1000, 700)
        BackColor = Theme.Back
        ForeColor = Theme.TextMain
        Font = Theme.BodyFont
        Try
            Icon = System.Drawing.Icon.ExtractAssociatedIcon(Application.ExecutablePath)
        Catch
        End Try

        sidebar = New DoubleBufferedPanel() With {.Dock = DockStyle.Left, .Width = 230, .BackColor = Theme.Sidebar}
        AddHandler sidebar.Paint, AddressOf Sidebar_Paint
        sidebar.Controls.Add(authorLink)
        AddHandler sidebar.Resize, Sub(s, e) authorLink.SetBounds(16, sidebar.Height - 92 - 54, sidebar.Width - 32, 46)
        content = New Panel() With {.Dock = DockStyle.Fill, .BackColor = Theme.Back}

        ServerPageView = New ServerPage(Me)
        DownloadPageView = New DownloadPage(Me)
        PluginsPageView = New PluginsPage(Me)
        PropertiesPageView = New PropertiesPage(Me)

        AddPage(ServerPageView, "Server", Theme.IconPlay)
        AddPage(DownloadPageView, "Download", Theme.IconDownload)
        AddPage(PluginsPageView, "Plugins", Theme.IconPlugins)
        AddPage(PropertiesPageView, "Einstellungen", Theme.IconSettings)

        Controls.Add(content)
        Controls.Add(sidebar)

        AddHandler ServerPageView.StateChanged, Sub(s, e) sidebar.Invalidate()
        ShowPage(ServerPageView)
    End Sub

    Protected Overrides Sub OnHandleCreated(e As EventArgs)
        MyBase.OnHandleCreated(e)
        Theme.UseDarkTitleBar(Me)
    End Sub

    Private Sub AddPage(page As PageBase, title As String, icon As String)
        page.Dock = DockStyle.Fill
        page.Visible = False
        content.Controls.Add(page)
        pages.Add(page)

        Dim nav As New NavButton() With {
            .Text = title,
            .IconGlyph = icon,
            .Tag = page,
            .Location = New Point(16, 120 + navButtons.Count * 46),
            .Size = New System.Drawing.Size(198, 40)
        }
        AddHandler nav.Click, Sub(s, e) ShowPage(DirectCast(DirectCast(s, NavButton).Tag, PageBase))
        sidebar.Controls.Add(nav)
        navButtons.Add(nav)
    End Sub

    Friend Sub ShowPage(page As PageBase)
        For Each p As PageBase In pages
            p.Visible = p Is page
        Next
        For Each nav As NavButton In navButtons
            nav.Selected = nav.Tag Is page
        Next
        page.OnShown()
    End Sub

    ''' <summary>Called when jars, plugins or the server folder changed, so every page shows fresh data.</summary>
    Friend Sub NotifyServerFilesChanged()
        Config.Save()
        ServerPageView.ReloadJars()
        PluginsPageView.ReloadInstalled()
        PropertiesPageView.MarkStale()
        sidebar.Invalidate()
    End Sub

    ' ---------- Sidebar painting (logo + status) ----------

    Private Sub Sidebar_Paint(sender As Object, e As PaintEventArgs)
        Dim g = e.Graphics

        ' Little grass block as logo
        Dim logo As New Rectangle(22, 30, 40, 40)
        Theme.FillRounded(g, logo, 8, Color.FromArgb(121, 85, 58))
        Theme.FillRounded(g, New RectangleF(logo.X, logo.Y, logo.Width, 15), 8, Theme.Accent)
        Using b As New SolidBrush(Theme.Accent)
            g.FillRectangle(b, logo.X, logo.Y + 8, logo.Width, 7)
            g.FillRectangle(b, logo.X + 8, logo.Y + 15, 6, 5)
            g.FillRectangle(b, logo.X + 24, logo.Y + 15, 5, 7)
        End Using

        TextRenderer.DrawText(g, "Minecraft", Theme.HeadingFont, New Point(74, 30), Theme.TextMain, TextFormatFlags.NoPadding)
        TextRenderer.DrawText(g, "Server Starter", Theme.SmallFont, New Point(75, 52), Theme.TextMuted, TextFormatFlags.NoPadding)

        ' Status box at the bottom
        Dim state = ServerPageView.State
        Dim stateText As String
        Dim stateColor As Color
        Select Case state
            Case ServerState.Running
                stateText = "Online"
                stateColor = Theme.Accent
            Case ServerState.Starting
                stateText = "Startet …"
                stateColor = Theme.Warning
            Case ServerState.Stopping
                stateText = "Stoppt …"
                stateColor = Theme.Warning
            Case Else
                stateText = "Gestoppt"
                stateColor = Theme.TextDim
        End Select

        Dim box As New RectangleF(16, sidebar.Height - 92, sidebar.Width - 32, 74)
        Theme.FillRounded(g, box, 8, Theme.Surface, Theme.Border)
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias
        Using dot As New SolidBrush(stateColor)
            g.FillEllipse(dot, box.X + 14, box.Y + 18, 9, 9)
        End Using
        TextRenderer.DrawText(g, stateText, Theme.BodyBoldFont, New Point(CInt(box.X) + 30, CInt(box.Y) + 13), Theme.TextMain, TextFormatFlags.NoPadding)

        Dim info As String
        If String.IsNullOrEmpty(Config.JarName) Then
            info = "Keine Server-Datei gewählt"
        ElseIf Config.ServerVersion.Length > 0 Then
            info = If(Config.ServerType = "paper", "Paper ", If(Config.ServerType = "vanilla", "Vanilla ", "")) & Config.ServerVersion
        Else
            info = Config.JarName
        End If
        TextRenderer.DrawText(g, info, Theme.SmallFont, New Rectangle(CInt(box.X) + 14, CInt(box.Y) + 40, CInt(box.Width) - 24, 20),
                              Theme.TextMuted, TextFormatFlags.NoPadding Or TextFormatFlags.EndEllipsis)
    End Sub

    Protected Overrides Sub OnResize(e As EventArgs)
        MyBase.OnResize(e)
        sidebar?.Invalidate()
    End Sub

    ' ---------- Closing ----------

    Protected Overrides Async Sub OnFormClosing(e As FormClosingEventArgs)
        If closingAllowed OrElse ServerPageView.State = ServerState.Stopped Then
            Config.Save()
            MyBase.OnFormClosing(e)
            Return
        End If

        e.Cancel = True
        Dim answer = MessageBox.Show(Me, "Der Server läuft noch. Soll er gestoppt und das Programm beendet werden?",
                                     "Server läuft", MessageBoxButtons.YesNo, MessageBoxIcon.Question)
        If answer <> DialogResult.Yes Then Return

        Await ServerPageView.StopAndWaitAsync()
        closingAllowed = True
        Close()
    End Sub

End Class


''' <summary>Panel without flicker for custom-painted areas.</summary>
Friend Class DoubleBufferedPanel
    Inherits Panel

    Public Sub New()
        DoubleBuffered = True
        ResizeRedraw = True
    End Sub
End Class


''' <summary>Base class for pages: draws the page title and offers helpers.</summary>
Friend Class PageBase
    Inherits Panel

    Protected ReadOnly Main As MainForm
    Private ReadOnly _title As String
    Private ReadOnly _subtitle As String

    Protected Const Pad As Integer = 24
    Protected Const HeaderHeight As Integer = 84

    Public Sub New(owner As MainForm, title As String, subtitle As String)
        Main = owner
        _title = title
        _subtitle = subtitle
        BackColor = Theme.Back
        ForeColor = Theme.TextMain
        Font = Theme.BodyFont
        DoubleBuffered = True
        ResizeRedraw = True
    End Sub

    Protected ReadOnly Property Config As AppConfig
        Get
            Return Main.Config
        End Get
    End Property

    ''' <summary>Called every time the page becomes visible.</summary>
    Friend Overridable Sub OnShown()
    End Sub

    Protected Overrides Sub OnPaint(e As PaintEventArgs)
        MyBase.OnPaint(e)
        TextRenderer.DrawText(e.Graphics, _title, Theme.TitleFont, New Point(Pad - 2, 20), Theme.TextMain, TextFormatFlags.NoPadding)
        TextRenderer.DrawText(e.Graphics, _subtitle, Theme.BodyFont, New Point(Pad, 54), Theme.TextMuted, TextFormatFlags.NoPadding)
    End Sub

    ' ---------- Small factory helpers used by all pages ----------

    Protected Shared Function MakeLabel(text As String, Optional muted As Boolean = True, Optional small As Boolean = False) As Label
        Return New Label() With {
            .Text = text,
            .AutoSize = False,
            .AutoEllipsis = True,
            .ForeColor = If(muted, Theme.TextMuted, Theme.TextMain),
            .Font = If(small, Theme.SmallFont, Theme.BodyFont),
            .BackColor = Color.Transparent,
            .Height = If(small, 18, 20)
        }
    End Function

    Protected Shared Function MakeButton(text As String, kind As ButtonKind, Optional icon As String = "") As ModernButton
        Return New ModernButton() With {.Text = text, .Kind = kind, .IconGlyph = icon}
    End Function

    Protected Shared Sub ShowError(owner As IWin32Window, title As String, ex As Exception)
        MessageBox.Show(owner, ex.Message, title, MessageBoxButtons.OK, MessageBoxIcon.Error)
    End Sub

    Protected Shared Sub OpenInExplorer(target As String)
        Try
            Process.Start(New ProcessStartInfo(target) With {.UseShellExecute = True})
        Catch
        End Try
    End Sub

End Class
