namespace Application.Common;

/// <summary>Stores uploaded files by generated key. Local disk now; Azure Blob later.</summary>
public interface IFileStorage
{
    Task SaveAsync(string key, Stream content, string contentType, CancellationToken cancellationToken);
    Task<Stream?> OpenReadAsync(string key, CancellationToken cancellationToken);
    Task DeleteAsync(string key, CancellationToken cancellationToken);
    string GetPublicUrl(string key);
}
