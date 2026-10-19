using System.IO;

namespace FirmaVisible.App;

public static class LaunchDocument
{
    public static string? Parse(string[] args)
    {
        if (args.Length == 0) return null;
        string path = args.Length == 1 ? args[0] : args.Length == 2 && args[0] == "--open" ? args[1]
            : throw new ArgumentException("Abre un solo PDF cada vez con FirmaPDF.");
        return Validate(path);
    }
    public static string Validate(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("Selecciona un archivo PDF.");
        path = Path.GetFullPath(path);
        if (!string.Equals(Path.GetExtension(path), ".pdf", StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("FirmaPDF solo abre archivos PDF.");
        if (!File.Exists(path)) throw new FileNotFoundException("No se encuentra el PDF seleccionado.", path);
        return path;
    }
}
