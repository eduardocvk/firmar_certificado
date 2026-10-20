using System.Windows;

namespace FirmaVisible.App;
public partial class App : Application
{
    private DocumentLaunchBroker? broker;
    private readonly bool startWindow = true;
    public App() { }
    // Hosts can load the app's resources without handling the host's command line.
    public App(bool startWindow) { this.startWindow = startWindow; }
    protected override async void OnStartup(StartupEventArgs e)
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
        string[] paths=[];
        try
        {
            paths = LaunchDocument.ParseMany(e.Args);
        }
        catch (Exception ex) { MessageBox.Show(ex.Message, "No se pudo abrir el PDF"); }
        broker=new DocumentLaunchBroker(System.IO.Path.GetFullPath(AppContext.BaseDirectory).ToUpperInvariant());
        if(!broker.IsPrimary)
        {
            ShutdownMode=ShutdownMode.OnExplicitShutdown;
            try { await broker.SendAsync(paths);Shutdown(0); }
            catch(Exception ex){MessageBox.Show("No se pudieron enviar los PDF a la ventana abierta: "+ex.Message,"FirmaPDF");Shutdown(1);}
            return;
        }
        base.OnStartup(e);
        var window = new MainWindow(); MainWindow = window;
        bool ready=false;var waiting=new List<string>();
        broker.Listen(files=>window.Dispatcher.BeginInvoke(()=>
        {if(ready)window.QueueOpenDocuments(files);else waiting.AddRange(files);if(window.WindowState==WindowState.Minimized)window.WindowState=WindowState.Normal;window.Activate();}));
        window.Loaded += async (_, _) =>
        {
            await window.RestoreLastIdentityAsync();
            ready=true;window.QueueOpenDocuments(paths.Concat(waiting));waiting.Clear();
        };
        window.Show();
    }
    protected override void OnExit(ExitEventArgs e){broker?.Dispose();base.OnExit(e);}
}
