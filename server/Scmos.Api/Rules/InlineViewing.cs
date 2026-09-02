namespace Scmos.Api.Rules;

/// <summary>
/// Which stored files may be shown in the browser, and as what.
///
/// Evidence on a CAR/PAR is mostly photographs of a damaged pallet and scans of
/// a signed form. Downloading each one to look at it is the difference between
/// reading a case and filing it, so the API can serve a file for display rather
/// than for saving.
///
/// <para>The decision is made from the file's extension and nothing else. The
/// content type on the row is whatever the browser said at upload — it is not
/// checked or corrected anywhere, so a file can claim to be anything. Serving a
/// file inline under a type the uploader chose is how a stored .html becomes a
/// script running on the site's own origin, with the session cookie of whoever
/// opened it. Here the extension only selects from a fixed list of types this
/// file decides; a name that is not on the list is not displayed at all.</para>
///
/// <para>SVG is deliberately absent. It renders as an image and carries script
/// like a document, which is the combination that makes it the usual way past a
/// list like this one. It downloads instead.</para>
/// </summary>
public static class InlineViewing
{
    /// <summary>
    /// Extension to the type it will be served as — never the type it claims.
    ///
    /// Images and PDFs because that is what evidence is, and plain text because
    /// a driver's statement is sometimes pasted into a .txt. Office documents
    /// are not here: no browser renders a .docx from a URL, so offering one
    /// inline would produce a download with the buttons in the wrong place.
    /// </summary>
    private static readonly Dictionary<string, string> Displayable = new(StringComparer.OrdinalIgnoreCase)
    {
        [".jpg"] = "image/jpeg",
        [".jpeg"] = "image/jpeg",
        [".png"] = "image/png",
        [".gif"] = "image/gif",
        [".webp"] = "image/webp",
        [".bmp"] = "image/bmp",
        [".heic"] = "image/heic",
        [".pdf"] = "application/pdf",
        [".txt"] = "text/plain; charset=utf-8",
        [".csv"] = "text/plain; charset=utf-8",
    };

    /// <summary>
    /// The type to serve <paramref name="fileName"/> as for display, or null
    /// when it may only be downloaded.
    /// </summary>
    public static string? TypeFor(string fileName)
    {
        var name = (fileName ?? "").Trim();
        var dot = name.LastIndexOf('.');
        if (dot < 0 || dot == name.Length - 1) return null;
        return Displayable.TryGetValue(name[dot..], out var type) ? type : null;
    }

    /// <summary>Whether the screen should offer to open this file rather than save it.</summary>
    public static bool CanShow(string fileName) => TypeFor(fileName) is not null;

    /// <summary>
    /// Sent with every file shown inline.
    ///
    /// Belt and braces over the allow-list above: nosniff stops a browser
    /// deciding for itself that a .txt is really HTML, and the policy denies
    /// script, plugins and embedding by another site even if something one day
    /// reaches this path that should not have.
    /// </summary>
    public const string Policy =
        "default-src 'none'; img-src 'self' data:; object-src 'none'; script-src 'none'; "
        + "style-src 'unsafe-inline'; frame-ancestors 'self'; base-uri 'none'; form-action 'none'";
}
