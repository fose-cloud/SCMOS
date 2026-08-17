using Azure.Identity;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Microsoft.Extensions.Options;

namespace Scmos.Api.Services;

public class StorageOptions
{
    public const string Section = "Storage";

    /// <summary>Blob service endpoint, e.g. https://scmosfiles.blob.core.windows.net.</summary>
    public string ServiceUri { get; set; } = "";

    /// <summary>Only for local development; production authenticates with the managed identity.</summary>
    public string ConnectionString { get; set; } = "";

    public string Container { get; set; } = "operation-files";
}

/// <summary>
/// Where uploaded paperwork goes.
///
/// Azure Blob Storage in place of the R2 bucket that was never switched on. The
/// container is created on first use so a fresh environment needs no manual
/// step, and the tier is left at Hot — Cool and Archive are applied by lifecycle
/// policy on the storage account rather than per upload, so a rule change does
/// not need a deployment.
/// </summary>
public interface IFileStore
{
    bool Configured { get; }
    Task<string> PutAsync(string objectKey, Stream content, string contentType, IDictionary<string, string> metadata, CancellationToken token);

    /// <summary>
    /// Opens a stored file for reading. Reading goes through the API rather than
    /// the blob URL because the container is private and has to stay that way —
    /// a URL that works without a sign-in is a URL that will end up in an email.
    /// </summary>
    Task<Stream?> OpenAsync(string objectKey, CancellationToken token);
}

public class BlobFileStore : IFileStore
{
    private readonly BlobContainerClient? _container;
    private bool _ensured;

    public BlobFileStore(IOptions<StorageOptions> options, ILogger<BlobFileStore> log)
    {
        var settings = options.Value;
        if (settings.ConnectionString.Length > 0)
        {
            _container = new BlobServiceClient(settings.ConnectionString).GetBlobContainerClient(settings.Container);
        }
        else if (settings.ServiceUri.Length > 0)
        {
            _container = new BlobServiceClient(new Uri(settings.ServiceUri), new DefaultAzureCredential())
                .GetBlobContainerClient(settings.Container);
        }
        else
        {
            log.LogWarning("No Storage:ServiceUri or Storage:ConnectionString — file upload will answer 503.");
        }
    }

    public bool Configured => _container is not null;

    public async Task<string> PutAsync(string objectKey, Stream content, string contentType,
        IDictionary<string, string> metadata, CancellationToken token)
    {
        if (_container is null) throw new InvalidOperationException("File storage is not configured.");

        if (!_ensured)
        {
            await _container.CreateIfNotExistsAsync(PublicAccessType.None, cancellationToken: token);
            _ensured = true;
        }

        var blob = _container.GetBlobClient(objectKey);
        // No overwrite flag: the key carries a timestamp and a short id, so two
        // uploads never collide, and a call that would replace an existing blob
        // is a bug worth hearing about rather than a file quietly destroyed.
        await blob.UploadAsync(content, new BlobUploadOptions
        {
            HttpHeaders = new BlobHttpHeaders { ContentType = contentType },
            Metadata = metadata,
        }, token);

        return blob.Uri.ToString();
    }

    public async Task<Stream?> OpenAsync(string objectKey, CancellationToken token)
    {
        if (_container is null) throw new InvalidOperationException("File storage is not configured.");

        var blob = _container.GetBlobClient(objectKey);
        if (!await blob.ExistsAsync(token)) return null;
        return await blob.OpenReadAsync(cancellationToken: token);
    }
}
