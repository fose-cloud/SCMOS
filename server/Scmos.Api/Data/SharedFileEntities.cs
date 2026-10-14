namespace Scmos.Api.Data;

/// <summary>
/// A folder in the shared file space (14 Oct 2026). <see cref="ParentId"/> null is the top level. Never deleted:
/// moving one to the trash sets <see cref="DeletedAt"/>, and everything inside goes out of sight with it until a
/// supervisor restores it — see <see cref="Rules.SharedFiles"/>.
/// </summary>
public class SharedFolder
{
    public long Id { get; set; }
    public long? ParentId { get; set; }
    public string Name { get; set; } = "";

    /// <summary>The account's stable id — whose folder it is when deciding who may rename or delete it.</summary>
    public string CreatedById { get; set; } = "";
    public string CreatedBy { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset? DeletedAt { get; set; }
    public string DeletedBy { get; set; } = "";
}

/// <summary>
/// A file in the shared file space. The bytes are in Blob Storage under <see cref="ObjectKey"/>, a key built by
/// <see cref="Rules.BlobPaths.ForShared"/>; the name people read is <see cref="Name"/>. Never deleted, as above.
/// </summary>
public class SharedFile
{
    public long Id { get; set; }
    public long? FolderId { get; set; }
    public string Name { get; set; } = "";
    public string ContentType { get; set; } = "application/octet-stream";
    public long SizeBytes { get; set; }
    public string ObjectKey { get; set; } = "";

    public string UploadedById { get; set; } = "";
    public string UploadedBy { get; set; } = "";
    public DateTimeOffset UploadedAt { get; set; }

    public DateTimeOffset? DeletedAt { get; set; }
    public string DeletedBy { get; set; } = "";
}
