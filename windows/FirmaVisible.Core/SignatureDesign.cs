using System.Globalization;

namespace FirmaVisible.Core;

public sealed class SignatureDesign
{
    public const string DefaultText = "Firmado digitalmente\n{nombre}\n{fecha}";
    public string Template { get; }
    public bool Decorated { get; }
    public float TextSize { get; }
    private readonly byte[]? image;
    public byte[]? ImagePng => image?.ToArray();
    public bool HasImage => image is not null;
    public SignatureDesign(string template, byte[]? imagePng, bool decorated, float textSize = 8)
    {
        if (!float.IsFinite(textSize) || textSize < 4 || textSize > 18) throw new ArgumentException("El tamaño del texto debe estar entre 4 y 18 pt.");
        if (template.Length > 600) throw new ArgumentException("El texto admite hasta 600 caracteres.");
        if (imagePng is { Length: > 4 * 1024 * 1024 }) throw new ArgumentException("La imagen normalizada no puede superar 4 MB.");
        if (string.IsNullOrWhiteSpace(template) && (imagePng is null || imagePng.Length == 0)) throw new ArgumentException("Añade un texto o una imagen a la firma.");
        Template = template; image = imagePng is { Length: > 0 } ? imagePng.ToArray() : null; Decorated = decorated; TextSize = textSize;
    }
    public static SignatureDesign Standard() => new(DefaultText, null, false);
    public string Resolve(string name, string issuer, DateTimeOffset time) => Template.Replace("{nombre}", name).Replace("{emisor}", issuer)
        .Replace("{fecha}", time.ToString("dd/MM/yyyy HH:mm:ss zzz", CultureInfo.InvariantCulture)).Replace("\r", "");
}
