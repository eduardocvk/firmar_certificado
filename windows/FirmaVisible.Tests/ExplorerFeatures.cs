using System.IO;
using System.Windows.Controls;
using System.Windows.Threading;
using FirmaVisible.App;
using Microsoft.Win32;

internal static class ExplorerFeatures
{
    public static void Run(string directory, Action<bool,string> check, Action<Action,string> refuses)
    {
        string folder = Path.Combine(directory,"PDF con espacios y acentos á & compañía"); Directory.CreateDirectory(folder);
        string pdf = Path.Combine(folder,"Documento para firmar.PDF"); File.Copy(Path.Combine(directory,"original-0.pdf"),pdf,true);
        byte[] original = File.ReadAllBytes(pdf);
        check(LaunchDocument.Parse([]) is null,"El arranque sin archivo debe mostrar la ventana vacía.");
        check(LaunchDocument.Parse(["--open",pdf]) == pdf,"No se interpreta el PDF enviado desde el menú.");
        check(LaunchDocument.Parse([pdf]) == pdf,"No se interpreta la apertura directa del PDF.");
        refuses(()=>LaunchDocument.Parse([pdf,pdf]),"Se aceptan varios PDF sin control.");
        refuses(()=>LaunchDocument.Parse(["--open",Path.Combine(folder,"missing.pdf")]),"Se acepta un PDF inexistente.");
        refuses(()=>LaunchDocument.Parse(["--open",Path.Combine(folder,"file.exe")]),"Se acepta un archivo que no es PDF.");
        var window = new MainWindow(new UserPreferencesStore(Path.Combine(directory,"explorer-preferences")));
        var previousContext = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(window.Dispatcher));
        try
        {
            var task = window.OpenDocumentAsync(LaunchDocument.Parse(["--open",pdf])!);
            var frame = new DispatcherFrame();
            _ = task.ContinueWith(_ => window.Dispatcher.BeginInvoke(() => frame.Continue = false));
            Dispatcher.PushFrame(frame); task.GetAwaiter().GetResult();
            check(((TextBlock)window.FindName("DocumentLabel")).Text == Path.GetFileName(pdf),"No se abre el PDF indicado en la interfaz.");
            check(((TextBlock)window.FindName("PageLabel")).Text == "Página 1 de 2","No se muestra la primera página del PDF abierto.");
            check(original.SequenceEqual(File.ReadAllBytes(pdf)),"La apertura desde Explorer modifica el PDF.");
        }
        finally { window.Close(); SynchronizationContext.SetSynchronizationContext(previousContext); }
        string executable=Path.Combine(folder,"FirmaPDF.exe"); File.WriteAllText(executable,"Test fixture, not an executable");
        string testRoot=@"Software\FirmaPDF.Tests\"+Guid.NewGuid().ToString("N");
        using var registry=Registry.CurrentUser.CreateSubKey(testRoot);
        try
        {
            using(var defaults=registry.CreateSubKey(@"Software\Classes\.pdf")) defaults.SetValue(null,"Reader.Unchanged");
            var integration=new ExplorerIntegration(registry);
            integration.Register(executable);
            check(integration.IsRegisteredFor(executable),"El menú no se registra para el ejecutable elegido.");
            integration.Register(executable);
            using(var command=registry.OpenSubKey(@"Software\Classes\"+ExplorerIntegration.VerbPath+@"\command"))
                check((string?)command?.GetValue(null)==$"\"{executable}\" --open \"%1\"","La orden no protege las rutas con espacios.");
            using(var defaults=registry.OpenSubKey(@"Software\Classes\.pdf")) check((string?)defaults?.GetValue(null)=="Reader.Unchanged","Se cambia el lector PDF predeterminado.");
            refuses(()=>integration.Remove(Path.Combine(folder,"other.exe")),"Se elimina el menú de otra instalación.");
            check(integration.IsRegisteredFor(executable),"Se borra un menú que pertenece a otra copia.");
            integration.Remove(executable); integration.Remove(executable);
            using(var removed=registry.OpenSubKey(@"Software\Classes\"+ExplorerIntegration.VerbPath)) check(removed is null,"No se quita el menú registrado.");
            using(var defaults=registry.OpenSubKey(@"Software\Classes\.pdf")) check((string?)defaults?.GetValue(null)=="Reader.Unchanged","Quitar el menú altera el lector PDF.");
        }
        finally { Registry.CurrentUser.DeleteSubKeyTree(testRoot,throwOnMissingSubKey:false); }
        Console.WriteLine("Apertura de PDF por argumentos y menú de Explorer: correctos (registro aislado).");
    }
}
