using Application.Catalog;
using FluentValidation;
using FluentValidation.Results;
using SkiaSharp;

namespace Infrastructure.Imaging;

/// <summary>
/// Accepts only files that actually decode as JPEG, PNG or WebP (the extension and declared type are ignored),
/// enforces size limits, applies camera orientation and re-encodes to WebP. Re-encoding drops metadata such as GPS.
/// </summary>
public sealed class SkiaImageProcessor : IImageProcessor
{
    private static readonly SKEncodedImageFormat[] Allowed = [SKEncodedImageFormat.Jpeg, SKEncodedImageFormat.Png, SKEncodedImageFormat.Webp];

    public async Task<ProcessedImage> ProcessAsync(Stream input, CancellationToken cancellationToken)
    {
        var bytes = await ReadLimitedAsync(input, cancellationToken);
        using var data = SKData.CreateCopy(bytes);
        using var codec = SKCodec.Create(data) ?? throw Reject("The file is not a supported image. Use JPEG, PNG or WebP.");
        if (!Allowed.Contains(codec.EncodedFormat)) throw Reject("The file is not a supported image. Use JPEG, PNG or WebP.");
        if (codec.Info.Width > ImageRules.MaxDimension || codec.Info.Height > ImageRules.MaxDimension)
            throw Reject($"The image must be at most {ImageRules.MaxDimension} x {ImageRules.MaxDimension} pixels.");

        using var decoded = SKBitmap.Decode(codec) ?? throw Reject("The image could not be read.");
        using var oriented = ApplyOrientation(decoded, codec.EncodedOrigin);
        var full = Encode(oriented);
        var scale = Math.Min(1f, (float)ImageRules.ThumbnailSize / Math.Max(oriented.Width, oriented.Height));
        var info = new SKImageInfo(Math.Max(1, (int)Math.Round(oriented.Width * scale)), Math.Max(1, (int)Math.Round(oriented.Height * scale)));
        using var thumb = oriented.Resize(info, new SKSamplingOptions(SKCubicResampler.Mitchell)) ?? throw Reject("The image could not be resized.");
        return new ProcessedImage(full, Encode(thumb), oriented.Width, oriented.Height);
    }

    private static async Task<byte[]> ReadLimitedAsync(Stream input, CancellationToken cancellationToken)
    {
        using var buffer = new MemoryStream();
        var chunk = new byte[81920];
        int read;
        while ((read = await input.ReadAsync(chunk, cancellationToken)) > 0)
        {
            if (buffer.Length + read > ImageRules.MaxBytes) throw Reject($"The image must be {ImageRules.MaxBytes / 1024 / 1024} MB or smaller.");
            buffer.Write(chunk, 0, read);
        }
        if (buffer.Length == 0) throw Reject("The file is empty.");
        return buffer.ToArray();
    }

    private static byte[] Encode(SKBitmap bitmap)
    {
        using var image = SKImage.FromBitmap(bitmap);
        using var encoded = image.Encode(SKEncodedImageFormat.Webp, ImageRules.Quality) ?? throw Reject("The image could not be converted.");
        return encoded.ToArray();
    }

    private static SKBitmap ApplyOrientation(SKBitmap source, SKEncodedOrigin origin)
    {
        var (width, height) = origin is SKEncodedOrigin.RightTop or SKEncodedOrigin.LeftBottom or SKEncodedOrigin.LeftTop or SKEncodedOrigin.RightBottom
            ? (source.Height, source.Width) : (source.Width, source.Height);
        var result = new SKBitmap(width, height, source.ColorType, source.AlphaType);
        using var canvas = new SKCanvas(result);
        switch (origin)
        {
            case SKEncodedOrigin.TopRight: canvas.Scale(-1, 1, width / 2f, 0); break;
            case SKEncodedOrigin.BottomRight: canvas.RotateDegrees(180, width / 2f, height / 2f); break;
            case SKEncodedOrigin.BottomLeft: canvas.Scale(1, -1, 0, height / 2f); break;
            case SKEncodedOrigin.LeftTop: canvas.Translate(width, 0); canvas.RotateDegrees(90); canvas.Scale(1, -1, 0, source.Height / 2f); break;
            case SKEncodedOrigin.RightTop: canvas.Translate(width, 0); canvas.RotateDegrees(90); break;
            case SKEncodedOrigin.RightBottom: canvas.Translate(0, height); canvas.RotateDegrees(-90); canvas.Scale(1, -1, 0, source.Height / 2f); break;
            case SKEncodedOrigin.LeftBottom: canvas.Translate(0, height); canvas.RotateDegrees(-90); break;
        }
        using var image = SKImage.FromBitmap(source);
        canvas.DrawImage(image, 0, 0, SKSamplingOptions.Default);
        return result;
    }

    private static ValidationException Reject(string message) => new([new ValidationFailure("file", message)]);
}
