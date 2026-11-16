namespace Application.Catalog;

public interface IImageProcessor
{
    /// <summary>Decodes, validates and re-encodes an uploaded image. Throws FluentValidation.ValidationException when rejected.</summary>
    Task<ProcessedImage> ProcessAsync(Stream input, CancellationToken cancellationToken);
}

public sealed record ProcessedImage(byte[] Full, byte[] Thumbnail, int Width, int Height)
{
    public const string ContentType = "image/webp";
    public const string Extension = ".webp";
}

public static class ImageRules
{
    public const long MaxBytes = 5 * 1024 * 1024;
    public const int MaxDimension = 4000;
    public const int ThumbnailSize = 480;
    public const int Quality = 82;
}
