Imports System.Text

''' <summary>App settings, stored as key=value in %AppData%\Minecraft Server Starter\settings.ini.</summary>
Friend Class AppConfig

    Public Property ServerFolder As String = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Minecraft Server")
    Public Property JarName As String = ""
    Public Property RamMb As Integer = 4096
    Public Property JavaPath As String = "java"
    Public Property ExtraJvmArgs As String = ""
    Public Property UseAikarFlags As Boolean = True
    Public Property ServerType As String = ""      ' "paper" or "vanilla", empty if unknown
    Public Property ServerVersion As String = ""   ' Minecraft version of the downloaded jar

    Private Shared ReadOnly ConfigPath As String =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Minecraft Server Starter", "settings.ini")

    Public ReadOnly Property PluginsFolder As String
        Get
            Return Path.Combine(ServerFolder, "plugins")
        End Get
    End Property

    Public ReadOnly Property JarPath As String
        Get
            If String.IsNullOrEmpty(JarName) Then Return ""
            Return Path.Combine(ServerFolder, JarName)
        End Get
    End Property

    Public Shared Function Load() As AppConfig
        Dim cfg As New AppConfig()
        Try
            If Not File.Exists(ConfigPath) Then Return cfg
            For Each line In File.ReadAllLines(ConfigPath, Encoding.UTF8)
                Dim idx = line.IndexOf("="c)
                If idx <= 0 Then Continue For
                Dim key = line.Substring(0, idx).Trim()
                Dim value = line.Substring(idx + 1).Trim()
                Select Case key
                    Case "ServerFolder"
                        If value.Length > 0 Then cfg.ServerFolder = value
                    Case "JarName" : cfg.JarName = value
                    Case "RamMb"
                        Dim ram As Integer
                        If Integer.TryParse(value, ram) AndAlso ram >= 512 Then cfg.RamMb = ram
                    Case "JavaPath"
                        If value.Length > 0 Then cfg.JavaPath = value
                    Case "ExtraJvmArgs" : cfg.ExtraJvmArgs = value
                    Case "UseAikarFlags" : cfg.UseAikarFlags = (value = "1")
                    Case "ServerType" : cfg.ServerType = value
                    Case "ServerVersion" : cfg.ServerVersion = value
                End Select
            Next
        Catch
            ' Broken file: start with defaults
        End Try
        Return cfg
    End Function

    Public Sub Save()
        Try
            Directory.CreateDirectory(Path.GetDirectoryName(ConfigPath))
            Dim lines = New String() {
                "ServerFolder=" & ServerFolder,
                "JarName=" & JarName,
                "RamMb=" & RamMb,
                "JavaPath=" & JavaPath,
                "ExtraJvmArgs=" & ExtraJvmArgs,
                "UseAikarFlags=" & If(UseAikarFlags, "1", "0"),
                "ServerType=" & ServerType,
                "ServerVersion=" & ServerVersion
            }
            File.WriteAllLines(ConfigPath, lines, Encoding.UTF8)
        Catch
            ' Not critical
        End Try
    End Sub

End Class
