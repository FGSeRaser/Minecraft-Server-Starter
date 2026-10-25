Imports System.Net.Http
Imports System.Threading

Friend Class GameVersion
    Public Property Id As String
    Public Property Type As String          ' release, snapshot, old_beta, old_alpha
    Public Property MetaUrl As String       ' Mojang only: URL of the version JSON
    Public Property ReleaseTime As DateTime

    Public ReadOnly Property IsRelease As Boolean
        Get
            Return Type = "release"
        End Get
    End Property
End Class

Friend Class PluginProject
    Public Property Id As String
    Public Property Slug As String
    Public Property Title As String
    Public Property Description As String
    Public Property Author As String
    Public Property Downloads As Long
End Class

Friend Class RemoteFile
    Public Property Url As String
    Public Property FileName As String
    Public Property Label As String
End Class

''' <summary>
''' Online sources:
'''  - Mojang version manifest (vanilla server jars)
'''  - PaperMC Fill API v3 (Paper server jars)
'''  - Modrinth API v2 (plugins)
''' </summary>
Friend NotInheritable Class WebApi

    Private Sub New()
    End Sub

    ' PaperMC and Modrinth require a User-Agent that names the app and a contact
    Private Const UserAgent As String = "MinecraftServerStarter/2.0 (https://github.com/FGSeRaser)"

    Private Const MojangManifest As String = "https://piston-meta.mojang.com/mc/game/version_manifest_v2.json"
    Private Const PaperApi As String = "https://fill.papermc.io/v3/projects/paper"
    Private Const ModrinthApi As String = "https://api.modrinth.com/v2"

    ''' <summary>Plugin loaders whose plugins run on Paper.</summary>
    Private Const PluginLoaders As String = "[""paper"",""spigot"",""bukkit"",""purpur""]"

    Private Shared ReadOnly client As HttpClient = CreateClient()

    Private Shared Function CreateClient() As HttpClient
        Dim c As New HttpClient() With {.Timeout = TimeSpan.FromSeconds(60)}
        c.DefaultRequestHeaders.UserAgent.ParseAdd(UserAgent)
        Return c
    End Function

    Private Shared Async Function GetJsonAsync(url As String) As Task(Of Object)
        Using response = Await client.GetAsync(url)
            If Not response.IsSuccessStatusCode Then
                Throw New HttpRequestException($"Server antwortet mit {CInt(response.StatusCode)} {response.ReasonPhrase} ({New Uri(url).Host})")
            End If
            Dim text = Await response.Content.ReadAsStringAsync()
            Return Json.Parse(text)
        End Using
    End Function

    ' ---------- Vanilla ----------

    Public Shared Async Function GetVanillaVersionsAsync() As Task(Of List(Of GameVersion))
        Dim root = Await GetJsonAsync(MojangManifest)
        Dim result As New List(Of GameVersion)()
        For Each item In Json.GetArray(root, "versions")
            Dim released As DateTime
            DateTime.TryParse(Json.GetString(item, "releaseTime"), Globalization.CultureInfo.InvariantCulture,
                              Globalization.DateTimeStyles.RoundtripKind, released)
            result.Add(New GameVersion With {
                .Id = Json.GetString(item, "id"),
                .Type = Json.GetString(item, "type"),
                .MetaUrl = Json.GetString(item, "url"),
                .ReleaseTime = released
            })
        Next
        Return result
    End Function

    ''' <summary>Download of the vanilla server jar, or Nothing if Mojang offers none for this version.</summary>
    Public Shared Async Function GetVanillaServerAsync(version As GameVersion) As Task(Of RemoteFile)
        Dim meta = Await GetJsonAsync(version.MetaUrl)
        Dim server = Json.GetObject(Json.GetObject(meta, "downloads"), "server")
        Dim url = Json.GetString(server, "url")
        If url.Length = 0 Then Return Nothing
        Return New RemoteFile With {.Url = url, .FileName = "vanilla-" & version.Id & ".jar", .Label = "Vanilla " & version.Id}
    End Function

    ' ---------- Paper ----------

    ''' <summary>All Paper versions, newest first.</summary>
    Public Shared Async Function GetPaperVersionsAsync() As Task(Of List(Of GameVersion))
        Dim root = Await GetJsonAsync(PaperApi)
        Dim result As New List(Of GameVersion)()
        Dim groups = Json.GetObject(root, "versions")
        If groups Is Nothing Then Return result

        For Each versionGroup In groups.Values
            Dim versions = TryCast(versionGroup, List(Of Object))
            If versions Is Nothing Then Continue For
            For Each v In versions
                Dim id = If(v, "").ToString()
                Dim isStable = id.IndexOf("-pre", StringComparison.OrdinalIgnoreCase) < 0 AndAlso
                               id.IndexOf("-rc", StringComparison.OrdinalIgnoreCase) < 0
                result.Add(New GameVersion With {.Id = id, .Type = If(isStable, "release", "snapshot")})
            Next
        Next
        Return result
    End Function

    ''' <summary>Newest stable build (or newest build at all) of a Paper version.</summary>
    Public Shared Async Function GetPaperServerAsync(version As String) As Task(Of RemoteFile)
        Dim root = Await GetJsonAsync(PaperApi & "/versions/" & Uri.EscapeDataString(version) & "/builds")
        Dim builds = TryCast(root, List(Of Object))
        If builds Is Nothing Then builds = Json.GetArray(root, "builds")
        If builds.Count = 0 Then Return Nothing

        Dim chosen = builds.FirstOrDefault(Function(b) Json.GetString(b, "channel").Equals("STABLE", StringComparison.OrdinalIgnoreCase))
        Dim isExperimental = chosen Is Nothing
        If isExperimental Then chosen = builds(0)

        Dim download = Json.GetObject(Json.GetObject(chosen, "downloads"), "server:default")
        Dim url = Json.GetString(download, "url")
        If url.Length = 0 Then Return Nothing

        Dim build = Json.GetString(chosen, "id")
        Dim name = Json.GetString(download, "name", "paper-" & version & "-" & build & ".jar")
        Return New RemoteFile With {
            .Url = url,
            .FileName = name,
            .Label = "Paper " & version & " Build " & build & If(isExperimental, " (experimentell)", "")
        }
    End Function

    ' ---------- Modrinth plugins ----------

    Public Shared Async Function SearchPluginsAsync(query As String, gameVersion As String) As Task(Of List(Of PluginProject))
        Dim facets = "[[""categories:paper"",""categories:spigot"",""categories:bukkit"",""categories:purpur""]"
        If Not String.IsNullOrWhiteSpace(gameVersion) Then facets &= ",[""versions:" & gameVersion.Trim() & """]"
        facets &= "]"

        Dim url = ModrinthApi & "/search?limit=30" &
                  "&index=" & If(String.IsNullOrWhiteSpace(query), "downloads", "relevance") &
                  "&query=" & Uri.EscapeDataString(If(query, "").Trim()) &
                  "&facets=" & Uri.EscapeDataString(facets)

        Dim root = Await GetJsonAsync(url)
        Dim result As New List(Of PluginProject)()
        For Each hit In Json.GetArray(root, "hits")
            result.Add(New PluginProject With {
                .Id = Json.GetString(hit, "project_id"),
                .Slug = Json.GetString(hit, "slug"),
                .Title = Json.GetString(hit, "title"),
                .Description = Json.GetString(hit, "description"),
                .Author = Json.GetString(hit, "author"),
                .Downloads = CLng(Json.GetNumber(hit, "downloads"))
            })
        Next
        Return result
    End Function

    ''' <summary>Newest plugin file that fits the game version (release builds preferred).</summary>
    Public Shared Async Function GetPluginFileAsync(project As PluginProject, gameVersion As String) As Task(Of RemoteFile)
        Dim url = ModrinthApi & "/project/" & Uri.EscapeDataString(project.Id) & "/version?loaders=" & Uri.EscapeDataString(PluginLoaders)
        If Not String.IsNullOrWhiteSpace(gameVersion) Then
            url &= "&game_versions=" & Uri.EscapeDataString("[""" & gameVersion.Trim() & """]")
        End If

        Dim versions = TryCast(Await GetJsonAsync(url), List(Of Object))
        If versions Is Nothing OrElse versions.Count = 0 Then Return Nothing

        Dim chosen = versions.FirstOrDefault(Function(v) Json.GetString(v, "version_type") = "release")
        If chosen Is Nothing Then chosen = versions(0)

        Dim files = Json.GetArray(chosen, "files")
        If files.Count = 0 Then Return Nothing
        Dim primary = files.FirstOrDefault(Function(f) TypeOf Json.GetValue(f, "primary") Is Boolean AndAlso CBool(Json.GetValue(f, "primary")))
        If primary Is Nothing Then primary = files(0)

        Return New RemoteFile With {
            .Url = Json.GetString(primary, "url"),
            .FileName = Json.GetString(primary, "filename"),
            .Label = project.Title & " " & Json.GetString(chosen, "version_number")
        }
    End Function

    ' ---------- Download with progress ----------

    ''' <summary>Downloads to a temp file first, so a broken download never replaces a working jar.</summary>
    Public Shared Async Function DownloadAsync(url As String, targetPath As String, progress As IProgress(Of Double), token As CancellationToken) As Task
        Directory.CreateDirectory(Path.GetDirectoryName(targetPath))
        Dim tempPath = targetPath & ".part"

        Using response = Await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, token)
            If Not response.IsSuccessStatusCode Then
                Throw New HttpRequestException($"Download fehlgeschlagen: {CInt(response.StatusCode)} {response.ReasonPhrase}")
            End If

            Dim total = response.Content.Headers.ContentLength
            Using input = Await response.Content.ReadAsStreamAsync()
                Using output As New FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, True)
                    Dim buffer(81919) As Byte
                    Dim done As Long = 0
                    Do
                        Dim read = Await input.ReadAsync(buffer, 0, buffer.Length, token)
                        If read = 0 Then Exit Do
                        Await output.WriteAsync(buffer, 0, read, token)
                        done += read
                        If total.HasValue AndAlso total.Value > 0 AndAlso progress IsNot Nothing Then
                            progress.Report(done * 100.0 / total.Value)
                        End If
                    Loop
                End Using
            End Using
        End Using

        If File.Exists(targetPath) Then File.Delete(targetPath)
        File.Move(tempPath, targetPath)
        progress?.Report(100)
    End Function

End Class
