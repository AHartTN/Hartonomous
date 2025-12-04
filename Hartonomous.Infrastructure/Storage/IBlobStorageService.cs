namespace Hartonomous.Infrastructure.Storage;

/// <summary>
/// Service for storing and retrieving binary content (blobs).
/// Supports Azure Blob Storage or local file system storage.
/// </summary>
public interface IBlobStorageService
{
    /// <summary>
    /// Upload a blob to storage.
    /// </summary>
    /// <param name="containerName">Container/bucket name</param>
    /// <param name="blobName">Blob/file name</param>
    /// <param name="content">Content stream</param>
    /// <param name="contentType">MIME content type</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>URI of the uploaded blob</returns>
    Task<string> UploadBlobAsync(
        string containerName,
        string blobName,
        Stream content,
        string contentType,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Download a blob from storage.
    /// </summary>
    /// <param name="containerName">Container/bucket name</param>
    /// <param name="blobName">Blob/file name</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Content stream</returns>
    Task<Stream> DownloadBlobAsync(
        string containerName,
        string blobName,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Delete a blob from storage.
    /// </summary>
    Task<bool> DeleteBlobAsync(
        string containerName,
        string blobName,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Check if a blob exists.
    /// </summary>
    Task<bool> BlobExistsAsync(
        string containerName,
        string blobName,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Get a SAS URL for temporary access to a blob.
    /// </summary>
    Task<string?> GetBlobSasUrlAsync(
        string containerName,
        string blobName,
        TimeSpan expiration,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// List all blobs in a container with optional prefix.
    /// </summary>
    Task<IEnumerable<string>> ListBlobsAsync(
        string containerName,
        string? prefix = null,
        CancellationToken cancellationToken = default);
}
