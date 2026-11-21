using Application.Catalog;
using Application.Common;
using FluentValidation;
using FluentValidation.Results;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Infrastructure.Catalog;

internal static class CatalogPersistence
{
    /// <summary>Saves and turns database uniqueness and concurrency failures into safe 409 messages.</summary>
    public static async Task SaveAsync(AppDbContext db, string conflictMessage, CancellationToken ct)
    {
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            // Forget the rejected changes so a later save on this context does not retry them.
            db.ChangeTracker.Clear();
            throw new ConflictException("This record was changed by someone else. Reload it and try again.");
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            db.ChangeTracker.Clear();
            throw new ConflictException(conflictMessage);
        }
    }

    public static void ExpectVersion<T>(AppDbContext db, T entity, uint? version) where T : class
    {
        if (version is { } expected) db.Entry(entity).Property("Version").OriginalValue = expected;
    }

    public static ValidationException Invalid(string property, string message) => new([new ValidationFailure(property, message)]);

    public static string LikePattern(string q) =>
        "%" + q.Trim().Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_") + "%";
}

/// <summary>Writes processed image files and removes them again if the database save fails.</summary>
internal sealed class StoredImage(IFileStorage storage, string key, string thumbnailKey, ProcessedImage image)
{
    public string Key => key;
    public string ThumbnailKey => thumbnailKey;
    public ProcessedImage Image => image;

    public static async Task<StoredImage> WriteAsync(IFileStorage storage, IImageProcessor processor, string folder, Stream content, CancellationToken ct)
    {
        var processed = await processor.ProcessAsync(content, ct);
        var name = Guid.NewGuid().ToString("N");
        var stored = new StoredImage(storage, $"{folder}/{name}{ProcessedImage.Extension}", $"{folder}/{name}-thumb{ProcessedImage.Extension}", processed);
        await storage.SaveAsync(stored.Key, new MemoryStream(processed.Full), ProcessedImage.ContentType, ct);
        try
        {
            await storage.SaveAsync(stored.ThumbnailKey, new MemoryStream(processed.Thumbnail), ProcessedImage.ContentType, ct);
        }
        catch
        {
            await storage.DeleteAsync(stored.Key, CancellationToken.None);
            throw;
        }
        return stored;
    }

    public async Task DiscardAsync()
    {
        await storage.DeleteAsync(key, CancellationToken.None);
        await storage.DeleteAsync(thumbnailKey, CancellationToken.None);
    }
}
