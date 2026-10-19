using System.Windows;

namespace FirmaVisible.App;
public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        if (e.Args.Contains("--smoke-test"))
        {
            try
            {
                var window = new MainWindow();
                window.Close();
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
        MainWindow = new MainWindow();
        MainWindow.Show();
    }
}
