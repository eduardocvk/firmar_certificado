using System.IO;
using System.Text.Json;
using FirmaVisible.Core;

namespace FirmaVisible.App;

public sealed record UserPreferences(SignatureDesign Design, bool UseTimestamp)
{ public static UserPreferences Default() => new(SignatureDesign.Standard(), true); }

/// <summary>Stores the visual design and timestamp switch only; never keys, certificates or passwords.</summary>
public sealed class UserPreferencesStore
{
    public string FilePath { get; }
    // Keep the existing profile directory so rebranding to FirmaPDF preserves saved designs.
    public UserPreferencesStore() : this(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FirmaVisible")) { }
    public UserPreferencesStore(string directory) => FilePath = Path.Combine(Path.GetFullPath(directory), "settings.json");
    private sealed record Data(string Template, bool Decorated, string? ImagePng, bool UseTimestamp, int Version = 1);
    public UserPreferences Load()
    {
        if (!File.Exists(FilePath)) return UserPreferences.Default();
        if (new FileInfo(FilePath).Length > 6 * 1024 * 1024) throw new InvalidDataException("El archivo de preferencias es demasiado grande.");
        var data = JsonSerializer.Deserialize<Data>(File.ReadAllBytes(FilePath)) ?? throw new InvalidDataException("El diseño guardado no es válido.");
        if (data.Version != 1 || data.Template is null) throw new InvalidDataException("Versión de preferencias no compatible.");
        byte[]? image = data.ImagePng is null ? null : Convert.FromBase64String(data.ImagePng);
        if (image is not null) _ = ImageNormalizer.Decode(image);
        return new(new SignatureDesign(data.Template, image, data.Decorated), data.UseTimestamp);
    }
    public void Save(UserPreferences preferences)
    {
        string directory = Path.GetDirectoryName(FilePath)!; Directory.CreateDirectory(directory);
        byte[]? image = preferences.Design.ImagePng;
        var data = new Data(preferences.Design.Template, preferences.Design.Decorated, image is null ? null : Convert.ToBase64String(image), preferences.UseTimestamp);
        byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(data, new JsonSerializerOptions { WriteIndented = true });
        string temp = Path.Combine(directory, $".settings-{Guid.NewGuid():N}.json");
        try { File.WriteAllBytes(temp, bytes); File.Move(temp, FilePath, overwrite: true); }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
}
