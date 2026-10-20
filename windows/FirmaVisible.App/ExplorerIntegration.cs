using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace FirmaVisible.App;

/// <summary>Adds a PDF-only verb for the current user; never changes the default PDF reader.</summary>
public sealed class ExplorerIntegration(RegistryKey classes)
{
    public const string VerbPath = @"SystemFileAssociations\.pdf\shell\FirmaPDF.Sign";
    public static string ExecutablePath => Path.Combine(AppContext.BaseDirectory, "FirmaPDF.exe");
    public static ExplorerIntegration ForCurrentUser() => new(Registry.CurrentUser);
    // The injected registry root keeps integration tests isolated from Explorer's real configuration.
    private const string ClassesPrefix = @"Software\Classes\";
    public static string Command(string executable) => $"\"{Path.GetFullPath(executable)}\" --open \"%1\"";
    private string? RegisteredCommand()
    { using var key = classes.OpenSubKey(ClassesPrefix + VerbPath + @"\command"); return key?.GetValue(null) as string; }
    public bool IsRegisteredFor(string executable) => string.Equals(RegisteredCommand(), Command(executable), StringComparison.OrdinalIgnoreCase);
    public void Register(string executable)
    {
        executable = Path.GetFullPath(executable);
        if (!File.Exists(executable) || !string.Equals(Path.GetExtension(executable), ".exe", StringComparison.OrdinalIgnoreCase))
            throw new FileNotFoundException("No se encuentra FirmaPDF.exe. Conserva toda la carpeta de la aplicación.", executable);
        using (var key = classes.CreateSubKey(ClassesPrefix + VerbPath))
        {
            key.SetValue(null, "Firmar con FirmaPDF");
            key.SetValue("Icon", $"\"{executable}\",0");
            key.SetValue("MultiSelectModel", "Document");
            using var command = key.CreateSubKey("command"); command.SetValue(null, Command(executable));
        }
        NotifyExplorer();
    }
    public void Remove(string executable)
    {
        string? command = RegisteredCommand();
        if (command is null) return;
        if (!IsRegisteredFor(executable)) throw new InvalidOperationException("El menú pertenece a otra copia de FirmaPDF. Quítalo desde esa copia o actívalo primero desde esta carpeta.");
        classes.DeleteSubKeyTree(ClassesPrefix + VerbPath, throwOnMissingSubKey: false);
        NotifyExplorer();
    }
    [DllImport("shell32.dll")]
    private static extern void SHChangeNotify(uint eventId, uint flags, IntPtr item1, IntPtr item2);
    private static void NotifyExplorer() => SHChangeNotify(0x08000000, 0, IntPtr.Zero, IntPtr.Zero);
}
