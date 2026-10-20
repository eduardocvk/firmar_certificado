using System.IO;
using System.Text.Json;
using FirmaVisible.Core;

namespace FirmaVisible.App;

public sealed record SignaturePreset(string Id, string Name, SignatureDesign Design);
public sealed record UserPreferences(SignatureDesign Design, bool UseTimestamp, string? LastIdentityFingerprint = null, bool ConfirmBeforeSigning = true, bool ShowSuccessDialog = true, IReadOnlyList<SignaturePreset>? SavedDesigns = null, string? SelectedDesignId = null)
{
    public IReadOnlyList<SignaturePreset> Designs => SavedDesigns ?? [new("default","Mi firma",Design)];
    public SignaturePreset SelectedDesign => Designs.FirstOrDefault(d=>d.Id==SelectedDesignId) ?? Designs[0];
    public UserPreferences WithDesigns(IReadOnlyList<SignaturePreset> designs,string selected)
    {
        if(designs.Count is <1 or >20 || designs.Select(d=>d.Id).Distinct().Count()!=designs.Count || designs.Any(d=>string.IsNullOrWhiteSpace(d.Id)||string.IsNullOrWhiteSpace(d.Name)||d.Name.Length>60))
            throw new ArgumentException("Guarda entre 1 y 20 firmas con nombres de hasta 60 caracteres e identificadores distintos.");
        var chosen=designs.FirstOrDefault(d=>d.Id==selected) ?? throw new ArgumentException("Selecciona una firma guardada.");
        return this with { SavedDesigns=designs.ToArray(),SelectedDesignId=selected,Design=chosen.Design };
    }
    public static UserPreferences Default() => new(SignatureDesign.Standard(), true);
}

/// <summary>Stores design, preferences and a public identity fingerprint; never keys, certificates or passwords.</summary>
public sealed class UserPreferencesStore
{
    public string FilePath { get; }
    // Keep the existing profile directory so rebranding to FirmaPDF preserves saved designs.
    public UserPreferencesStore() : this(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FirmaVisible")) { }
    public UserPreferencesStore(string directory) => FilePath = Path.Combine(Path.GetFullPath(directory), "settings.json");
    private sealed record Data(string Template, bool Decorated, string? ImagePng, bool UseTimestamp, int Version = 1, float TextSize = 8, string? LastIdentityFingerprint = null, bool ConfirmBeforeSigning = true, bool ShowSuccessDialog = true);
    private sealed record DesignData(string Id,string Name,string Template,bool Decorated,string? ImagePng,float TextSize);
    private sealed record CollectionData(Data Preferences,DesignData[] Designs,string SelectedDesignId,int Version=4);
    public UserPreferences Load()
    {
        if (!File.Exists(FilePath)) return UserPreferences.Default();
        if (new FileInfo(FilePath).Length > 120 * 1024 * 1024) throw new InvalidDataException("El archivo de preferencias es demasiado grande.");
        byte[] bytes=File.ReadAllBytes(FilePath);
        using var json=JsonDocument.Parse(bytes);
        if(json.RootElement.TryGetProperty("Version",out var version) && version.GetInt32()==4)
        {
            var collection=JsonSerializer.Deserialize<CollectionData>(bytes)??throw new InvalidDataException("Las firmas guardadas no son válidas.");
            if(collection.Preferences is null || collection.Designs is null || collection.Designs.Length is <1 or >20 || collection.Designs.Any(d=>d is null))throw new InvalidDataException("La lista de firmas no es válida.");
            var loaded=LoadData(collection.Preferences);
            var designs=collection.Designs.Select(d=>new SignaturePreset(d.Id,d.Name,DecodeDesign(d.Template,d.ImagePng,d.Decorated,d.TextSize))).ToArray();
            return loaded.WithDesigns(designs,collection.SelectedDesignId);
        }
        var data = JsonSerializer.Deserialize<Data>(bytes) ?? throw new InvalidDataException("El diseño guardado no es válido.");
        return LoadData(data);
    }
    private static SignatureDesign DecodeDesign(string template,string? png,bool decorated,float size)
    {
        byte[]? image=png is null?null:Convert.FromBase64String(png);
        if(image is not null)_=ImageNormalizer.Decode(image);
        return new(template,image,decorated,size);
    }
    private static UserPreferences LoadData(Data data)
    {
        if (data.Version is not (1 or 2 or 3) || data.Template is null) throw new InvalidDataException("Versión de preferencias no compatible.");
        if (data.LastIdentityFingerprint is { } fingerprint && (fingerprint.Length != 64 || !fingerprint.All(Uri.IsHexDigit))) throw new InvalidDataException("La referencia del último certificado no es válida.");
        return new(DecodeDesign(data.Template,data.ImagePng,data.Version>=2 && data.Decorated,data.TextSize), data.UseTimestamp, data.LastIdentityFingerprint, data.ConfirmBeforeSigning, data.ShowSuccessDialog);
    }
    public void Save(UserPreferences preferences)
    {
        string directory = Path.GetDirectoryName(FilePath)!; Directory.CreateDirectory(directory);
        preferences=preferences.WithDesigns(preferences.Designs,preferences.SelectedDesign.Id);
        var design=preferences.SelectedDesign.Design;byte[]? image = design.ImagePng;
        var data = new Data(design.Template, design.Decorated, image is null ? null : Convert.ToBase64String(image), preferences.UseTimestamp, 3, design.TextSize, preferences.LastIdentityFingerprint, preferences.ConfirmBeforeSigning, preferences.ShowSuccessDialog);
        var designs=preferences.Designs.Select(d=>new DesignData(d.Id,d.Name,d.Design.Template,d.Design.Decorated,d.Design.ImagePng is {} png?Convert.ToBase64String(png):null,d.Design.TextSize)).ToArray();
        byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(new CollectionData(data,designs,preferences.SelectedDesign.Id), new JsonSerializerOptions { WriteIndented = true });
        if(bytes.Length>120*1024*1024)throw new InvalidDataException("Las firmas guardadas ocupan demasiado espacio.");
        string temp = Path.Combine(directory, $".settings-{Guid.NewGuid():N}.json");
        try { File.WriteAllBytes(temp, bytes); File.Move(temp, FilePath, overwrite: true); }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
}
