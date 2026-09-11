namespace SpaceRemote;

static class Program
{
    [STAThread]
    static void Main()
    {
        using var mutex = new Mutex(true, "SpaceRemote_Finanzinstitut_SingleInstance", out bool isNew);
        if (!isNew)
        {
            MessageBox.Show("Space Remote läuft bereits (Symbol unten rechts im Infobereich der Taskleiste).",
                "Space Remote", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        var config = Config.Load();
        var server = new RemoteServer(config);
        try
        {
            server.Start();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Port {config.Port} konnte nicht geöffnet werden:\n{ex.Message}",
                "Space Remote", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        Application.Run(new TrayContext(config, server));
        server.Stop();
    }
}
