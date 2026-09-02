using Scmos.Api.Rules;

namespace Scmos.Api.Data;

/// <summary>
/// Runs the inline-viewing rule against its awkward cases, with `--check-inline`.
///
/// This is the rule that decides whether a stored file is handed to the browser
/// to display or to save, which makes it a security rule wearing a convenience
/// rule's clothes. Getting it too narrow means a photograph of a damaged pallet
/// downloads instead of appearing, which is annoying. Getting it too wide means
/// a file somebody uploaded runs as script on the site's own origin, holding the
/// session of whoever opened the case.
///
/// The cases that matter are the ones designed to get past a list like this:
/// SVG, which is an image that carries script; a double extension that hides one
/// type behind another; and a name whose extension disagrees with the content
/// type it was uploaded under. Nothing here consults that content type, and
/// these cases are how that stays true.
/// </summary>
public static class InlineCheck
{
    private static readonly (string Why, string FileName, string? Expect)[] Cases =
    [
        ("a photograph of the damage is the ordinary case", "pallet-damage.jpg", "image/jpeg"),
        ("and so is a scan of the signed form", "CAR-26-002-signed.pdf", "application/pdf"),
        ("phones write the extension in capitals", "IMG_20260902.JPG", "image/jpeg"),
        ("a Thai name is a name like any other", "ใบรับสินค้า.png", "image/png"),
        ("a driver's statement pasted into a text file", "statement.txt", "text/plain; charset=utf-8"),

        // The ones that exist to get past a list like this.
        ("SVG is an image that carries script — it downloads", "logo.svg", null),
        ("so does anything that would run as a page", "evidence.html", null),
        ("and anything that would run as a script", "notes.js", null),
        ("a double extension is read by its last one, not its first",
            "report.pdf.html", null),
        ("which is safe the other way round too: it is served as the image it ends in",
            "payload.html.jpg", "image/jpeg"),

        // Things a browser cannot render anyway, so offering to show them would
        // produce a download with the buttons in the wrong place.
        ("a Word document has no inline form", "5W1H.docx", null),
        ("nor does a spreadsheet", "rates.xlsx", null),
        ("nor does an archive", "evidence.zip", null),

        ("a name with no extension says nothing about itself", "scan0001", null),
        ("and neither does one that ends in the dot", "scan0001.", null),
        ("an empty name is not a file", "", null),
    ];

    public static int? Run(string[] args)
    {
        if (!args.Contains("--check-inline")) return null;

        var failed = 0;
        Console.WriteLine("Which stored files may be shown in the browser, and as what.");
        Console.WriteLine("Decided by the name alone — the uploaded content type is never consulted.");
        Console.WriteLine();

        foreach (var (why, fileName, expect) in Cases)
        {
            var got = InlineViewing.TypeFor(fileName);
            var ok = got == expect;
            if (!ok) failed++;
            Console.WriteLine($"{(ok ? "ok  " : "FAIL")}  {Show(fileName),-26} -> {Show(got),-26}"
                + (ok ? "" : $"expected {Show(expect)}  ") + $"({why})");
        }

        // CanShow is what the screen is told; it must never disagree with what
        // the content route would actually do.
        foreach (var (_, fileName, expect) in Cases)
        {
            if (InlineViewing.CanShow(fileName) == (expect is not null)) continue;
            failed++;
            Console.WriteLine($"FAIL  CanShow disagrees with TypeFor for {Show(fileName)}");
        }

        Console.WriteLine();
        Console.WriteLine(failed == 0
            ? $"{Cases.Length} cases, all as intended."
            : $"{failed} wrong.");
        return failed == 0 ? 0 : 1;
    }

    private static string Show(string? value) =>
        value is null ? "download" : value.Length == 0 ? "(empty)" : value;
}
