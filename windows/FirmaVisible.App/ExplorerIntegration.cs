using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace FirmaVisible.App;

/// <summary>Adds a PDF-only verb for the current user; never changes the default PDF reader.</summary>
public sealed class ExplorerIntegration(RegistryKey classes)
{
    public static IReadOnlyList<string> SupportedExtensions { get; }=Array.AsReadOnly(new[]{".pdf",".doc",".docx",".xls",".xlsx",".ppt",".pptx",".odt",".ods",".odp",".xml",".txt",".csv",".jpg",".jpeg",".png",".zip",".7z",".rar",".p7s"});
    public const string VerbPath = @"SystemFileAssociations\.pdf\shell\FirmaPDF.Sign";
    public static string ExecutablePath => Path.Combine(AppContext.BaseDirectory, "FirmaPDF.exe");
    public static ExplorerIntegration ForCurrentUser() => new(Registry.CurrentUser);
    // The injected registry root keeps integration tests isolated from Explorer's real configuration.
    private const string ClassesPrefix = @"Software\Classes\";
    public static string Command(string executable) => $"\"{Path.GetFullPath(executable)}\" --open \"%1\"";
    private static string ShellPath(string extension)=>@"SystemFileAssociations\"+extension+@"\shell\FirmaPDF.Sign";
    private string? RegisteredCommand(string extension=".pdf")
    { using var key = classes.OpenSubKey(ClassesPrefix + ShellPath(extension) + @"\command"); return key?.GetValue(null) as string; }
    public bool IsRegisteredFor(string executable) => string.Equals(RegisteredCommand(), Command(executable), StringComparison.OrdinalIgnoreCase);
    public void Register(string executable)
    {
        if(PackageEnvironment.IsPackaged)throw new InvalidOperationException("Windows gestiona la apertura de PDF de la versión Store. Usa «Abrir con → FirmaPDF».");
        executable = Path.GetFullPath(executable);
        if (!File.Exists(executable) || !string.Equals(Path.GetExtension(executable), ".exe", StringComparison.OrdinalIgnoreCase))
            throw new FileNotFoundException("No se encuentra FirmaPDF.exe. Conserva toda la carpeta de la aplicación.", executable);
        foreach(string extension in SupportedExtensions)
        {
            using var key=classes.CreateSubKey(ClassesPrefix+ShellPath(extension));
            key.SetValue(null,extension==".p7s"?"Comprobar firma con FirmaPDF":"Firmar con FirmaPDF");
            key.SetValue("Icon", $"\"{executable}\",0");
            key.SetValue("MultiSelectModel", "Document");
            using var command = key.CreateSubKey("command"); command.SetValue(null, Command(executable));
        }
        NotifyExplorer();
    }
    public void Remove(string executable)
    {
        if(PackageEnvironment.IsPackaged)throw new InvalidOperationException("Windows gestiona esta integración al instalar o desinstalar FirmaPDF desde la Store.");
        string? command = RegisteredCommand();
        if (command is null) return;
        if (!IsRegisteredFor(executable)) throw new InvalidOperationException("El menú pertenece a otra copia de FirmaPDF. Quítalo desde esa copia o actívalo primero desde esta carpeta.");
        foreach(string extension in SupportedExtensions)
            if(string.Equals(RegisteredCommand(extension),Command(executable),StringComparison.OrdinalIgnoreCase))classes.DeleteSubKeyTree(ClassesPrefix+ShellPath(extension),throwOnMissingSubKey:false);
        NotifyExplorer();
    }
    [DllImport("shell32.dll")]
    private static extern void SHChangeNotify(uint eventId, uint flags, IntPtr item1, IntPtr item2);
    private static void NotifyExplorer() => SHChangeNotify(0x08000000, 0, IntPtr.Zero, IntPtr.Zero);
}
