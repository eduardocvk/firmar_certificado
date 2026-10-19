using System.Windows;

namespace FirmaVisible.App;
public partial class App : Application
{
    private readonly bool startWindow = true;
    public App() { }
    // Hosts can load the app's resources without handling the host's command line.
    public App(bool startWindow) { this.startWindow = startWindow; }
    protected override void OnStartup(StartupEventArgs e)
    {
        if (!startWindow) return;
        if (e.Args.Length == 1 && e.Args[0] is "--register-context-menu" or "--unregister-context-menu")
        {
            try
            {
                var integration = ExplorerIntegration.ForCurrentUser();
                if (e.Args[0] == "--register-context-menu") integration.Register(ExplorerIntegration.ExecutablePath);
                else integration.Remove(ExplorerIntegration.ExecutablePath);
                Shutdown(0);
            }
            catch (Exception ex) { MessageBox.Show(ex.Message, "Menú de FirmaPDF"); Shutdown(1); }
            return;
        }
        if (e.Args.Length == 1 && e.Args[0] == "--smoke-test")
        {
            try
            {
                var smokeWindow = new MainWindow();
                smokeWindow.Close();
                Shutdown(0);
            }
            catch (Exception ex)
            {
                System.IO.File.WriteAllText(System.IO.Path.Combine(AppContext.BaseDirectory, "smoke-error.txt"), ex.ToString());
                Shutdown(1);
            }
            return;
        }
        base.OnStartup(e);
        var window = new MainWindow(); MainWindow = window;
        string? path = null;
        try
        {
            path = LaunchDocument.Parse(e.Args);
        }
        catch (Exception ex) { MessageBox.Show(ex.Message, "No se pudo abrir el PDF"); }
        window.Loaded += async (_, _) =>
        {
            await window.RestoreLastIdentityAsync();
            if (path is not null) await window.OpenDocumentAsync(path);
        };
        window.Show();
    }
}
