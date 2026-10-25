Imports System.Net

Friend Module Program

    <STAThread>
    Sub Main()
        ' Mojang, PaperMC and Modrinth only accept TLS 1.2 or newer
        ServicePointManager.SecurityProtocol = ServicePointManager.SecurityProtocol Or SecurityProtocolType.Tls12

        Application.EnableVisualStyles()
        Application.SetCompatibleTextRenderingDefault(False)
        Application.Run(New MainForm())
    End Sub

End Module
