using System.IO;

namespace FirmaVisible.App;

public static class LaunchDocument
{
    public static string[] ParseMany(string[] args)
    {
        var paths=args.Length>0&&args[0]=="--open"?args.Skip(1).ToArray():args;
        if(args.Length>0&&paths.Length==0)throw new ArgumentException("Indica uno o varios archivos PDF.");
        if(paths.Length>1000)throw new ArgumentException("Abre hasta 1000 PDF cada vez.");
        return paths.Select(ValidateAny).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }
    public static string? Parse(string[] args)
    {
        if (args.Length == 0) return null;
        string path = args.Length == 1 ? args[0] : args.Length == 2 && args[0] == "--open" ? args[1]
            : throw new ArgumentException("Abre un solo PDF cada vez con FirmaPDF.");
        return Validate(path);
    }
    public static string Validate(string path)
    {
        path=ValidateAny(path);
        if (!string.Equals(Path.GetExtension(path), ".pdf", StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("Esta acción requiere un PDF.");
        return path;
    }
    public static string ValidateAny(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("Selecciona un archivo.");
        path = Path.GetFullPath(path);
        if (!File.Exists(path)) throw new FileNotFoundException("No se encuentra el archivo seleccionado.", path);
        return path;
    }
}
