namespace Scmos.Api.Rules;

/// <summary>
/// The shared file space (14 Oct 2026, its own menu): folders everybody in the department can see, made and filled
/// by whoever may upload paperwork, moved into a trash a supervisor can restore from. The existing implementation's
/// rules — seen by recognized staff accounts and no carrier, written by the upload right, soft-deleted, 32 MB and
/// office files only — are kept below; nothing here removes a file from storage.
/// </summary>
public static class SharedFiles
{
    /// <summary>The same ceiling as every other upload in the system.</summary>
    public const long MaxBytes = 32L * 1024 * 1024;

    /// <summary>A folder's name is typed, so short; a file's comes from somebody's disk.</summary>
    public const int MaxFolderName = 120;
    public const int MaxFileName = 200;

    /// <summary>Folders inside folders, as deep as anybody files paperwork — a limit, so a path stays readable.</summary>
    public const int MaxDepth = 8;

    /// <summary>
    /// What an office keeps: documents, sheets, slides, PDF, pictures, text and zip. No program, script or page —
    /// a file this space hands back to a colleague's browser must not be something that runs.
    /// </summary>
    public static readonly string[] Extensions =
    [
        ".pdf", ".doc", ".docx", ".xls", ".xlsx", ".xlsm", ".xlsb", ".ppt", ".pptx", ".csv", ".txt",
        ".jpg", ".jpeg", ".png", ".gif", ".webp", ".bmp", ".tif", ".tiff", ".heic", ".zip",
    ];

    public static bool Allowed(string? fileName) =>
        Extensions.Contains(Path.GetExtension(fileName ?? "").ToLowerInvariant());

    private const string Reserved = "\\/:*?\"<>|";

    /// <summary>Why a typed name cannot be used, in the words the screen shows; null when it can.</summary>
    public static string? NameProblem(string? value, int max)
    {
        var name = (value ?? "").Trim();
        if (name.Length == 0) return "ต้องมีชื่อ";
        if (name.Length > max) return $"ชื่อยาวเกิน {max} ตัวอักษร";
        if (name.Any(c => char.IsControl(c) || Reserved.Contains(c)))
            return "ชื่อใช้อักขระเหล่านี้ไม่ได้: \\ / : * ? \" < > |";
        if (name.Trim('.').Length == 0) return "ชื่อเป็นจุดอย่างเดียวไม่ได้";
        return null;
    }

    /// <summary>
    /// An uploaded file's name as kept: the last path segment, reserved characters replaced, held to
    /// <see cref="MaxFileName"/> with its extension intact. A Thai name stays Thai — it is the blob key that is
    /// cleaned (<see cref="BlobPaths.SafeName"/>), not what people read.
    /// </summary>
    public static string UploadName(string? original)
    {
        var raw = (original ?? "").Replace('\\', '/');
        raw = raw[(raw.LastIndexOf('/') + 1)..].Trim();
        var cleaned = new string(raw.Select(c => char.IsControl(c) || Reserved.Contains(c) ? '_' : c).ToArray()).Trim();
        if (cleaned.Trim('.').Length == 0) cleaned = "file";
        if (cleaned.Length <= MaxFileName) return cleaned;
        var extension = Path.GetExtension(cleaned);
        if (extension.Length > 16) extension = "";
        return cleaned[..(MaxFileName - extension.Length)].TrimEnd() + extension;
    }

    /// <summary>
    /// The name a second copy gets, the way Windows names it: "report.pdf" taken → "report (2).pdf". Compared
    /// without regard to case, as the database's collation compares them.
    /// </summary>
    public static string FreeName(string wanted, IEnumerable<string> taken, int maxLength = MaxFileName, bool keepExtension = true)
    {
        var used = new HashSet<string>(taken, StringComparer.OrdinalIgnoreCase);
        if (!used.Contains(wanted)) return wanted;
        var extension = keepExtension ? Path.GetExtension(wanted) : "";
        var stem = wanted[..^extension.Length];
        for (var copy = 2; ; copy++)
        {
            var candidate = $"{stem} ({copy}){extension}";
            if (candidate.Length > maxLength)
                candidate = $"{stem[..Math.Max(1, stem.Length - (candidate.Length - maxLength))]} ({copy}){extension}";
            if (!used.Contains(candidate)) return candidate;
        }
    }
}
