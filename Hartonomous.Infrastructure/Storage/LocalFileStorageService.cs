using Microsoft.Extensions.Logging;

namespace Hartonomous.Infrastructure.Storage;

/// <summary>
/// Local file system implementation of blob storage service.
/// Used for development and testing.
/// </summary>
public class LocalFileStorageService : IBlobStorageService
{
    private readonly string _baseStoragePath;
    private readonly ILogger<LocalFileStorageService> _logger;

    public LocalFileStorageService(
        string baseStoragePath,
        ILogger<LocalFileStorageService> logger)
    {
        _baseStoragePath = baseStoragePath;
        _logger = logger;

        if (!Directory.Exists(_baseStoragePath))
        {
            Directory.CreateDirectory(_baseStoragePath);
        }
    }

    public async Task<string> UploadBlobAsync(
        string containerName,
        string blobName,
        Stream content,
        string contentType,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var containerPath = Path.Combine(_baseStoragePath, containerName);
            if (!Directory.Exists(containerPath))
            {
                Directory.CreateDirectory(containerPath);
            }

            var filePath = Path.Combine(containerPath, blobName);
            var directoryPath = Path.GetDirectoryName(filePath);
            if (directoryPath != null && !Directory.Exists(directoryPath))
            {
                Directory.CreateDirectory(directoryPath);
            }

            using var fileStream = new FileStream(filePath, FileMode.Create, FileAccess.Write);
            await content.CopyToAsync(fileStream, cancellationToken);

            _logger.LogInformation("Uploaded blob {BlobName} to local container {ContainerName}", blobName, containerName);
            return $"file://{filePath}";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to upload blob {BlobName} to local container {ContainerName}", blobName, containerName);
            throw;
        }
    }

    public async Task<Stream> DownloadBlobAsync(
        string containerName,
        string blobName,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var filePath = Path.Combine(_baseStoragePath, containerName, blobName);
            if (!File.Exists(filePath))
            {
                throw new FileNotFoundException($"Blob {blobName} not found in container {containerName}");
            }

            var memoryStream = new MemoryStream();
            using var fileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read);
            await fileStream.CopyToAsync(memoryStream, cancellationToken);
            memoryStream.Position = 0;

            return memoryStream;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to download blob {BlobName} from local container {ContainerName}", blobName, containerName);
            throw;
        }
    }

    public Task<bool> DeleteBlobAsync(
        string containerName,
        string blobName,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var filePath = Path.Combine(_baseStoragePath, containerName, blobName);
            if (File.Exists(filePath))
            {
                File.Delete(filePath);
                _logger.LogInformation("Deleted blob {BlobName} from local container {ContainerName}", blobName, containerName);
                return Task.FromResult(true);
            }

            return Task.FromResult(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete blob {BlobName} from local container {ContainerName}", blobName, containerName);
            throw;
        }
    }

    public Task<bool> BlobExistsAsync(
        string containerName,
        string blobName,
        CancellationToken cancellationToken = default)
    {
        var filePath = Path.Combine(_baseStoragePath, containerName, blobName);
        return Task.FromResult(File.Exists(filePath));
    }

    public Task<string?> GetBlobSasUrlAsync(
        string containerName,
        string blobName,
        TimeSpan expiration,
        CancellationToken cancellationToken = default)
    {
        // Local file storage doesn't support SAS URLs
        var filePath = Path.Combine(_baseStoragePath, containerName, blobName);
        return Task.FromResult<string?>(File.Exists(filePath) ? $"file://{filePath}" : null);
    }

    public Task<IEnumerable<string>> ListBlobsAsync(
        string containerName,
        string? prefix = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var containerPath = Path.Combine(_baseStoragePath, containerName);
            if (!Directory.Exists(containerPath))
            {
                return Task.FromResult(Enumerable.Empty<string>());
            }

            var searchPattern = string.IsNullOrEmpty(prefix) ? "*" : $"{prefix}*";
            var files = Directory.GetFiles(containerPath, searchPattern, SearchOption.AllDirectories);
            var relativeNames = files.Select(f => Path.GetRelativePath(containerPath, f));

            return Task.FromResult(relativeNames);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to list blobs in local container {ContainerName} with prefix {Prefix}", containerName, prefix);
            throw;
        }
    }
}
