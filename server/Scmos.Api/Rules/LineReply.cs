namespace Scmos.Api.Rules;

/// <summary>
/// What the bot says back into a haulier's room about a message it just read.
///
/// <para>
/// Asked for on 16 Sep 2026: a driver who types "TXGU8142057 ถึงโรงงาน 12:40"
/// and hears nothing does not know whether anybody got it. So every message
/// the queue takes gets one short line back — what was read, and that a
/// person still has to confirm — and an approval gets one more, that it was
/// written.
/// </para>
///
/// <para>
/// A photo is never answered: four photos of one delivery drew five replies
/// on 17 Sep 2026, and the department asked for silence. What a photo says
/// — a box number, or the truck at the site — waits in the queue like any
/// message, unspoken.
/// </para>
///
/// <para>
/// <b>Two things it never says.</b> Whether a number exists but belongs to
/// another haulier: "not your job" and "no such job" read the same from the
/// room, because telling a stranger a number is real is telling them
/// something about somebody else's work (the note on <see cref="LineAuthority.Decide"/>).
/// And anything at all into a room nobody has bound — an unbound room is not
/// spoken to, so a bot added to the wrong group stays silent.
/// </para>
///
/// <para>Pure: the reading and the verdict in, the sentence out — or null for silence.</para>
/// </summary>
public static class LineReply
{
    /// <summary>What the message pointed at, for the sentence: the container, the job number, the booking, the truck, the seal.</summary>
    public static string Reference(LineParser.Parsed read) =>
        read.Container ?? read.JobNumber ?? (read.References is { Count: > 0 } refs ? refs[0]
            : read.Plates is { Count: > 0 } plates ? $"รถ {plates[0]}"
            : read.SealNumber is not null ? $"ซีล {read.SealNumber}" : "");

    /// <summary>What the message said, in the words that will be written, for the acknowledgement.</summary>
    public static string Said(LineParser.Parsed read, string resolvedTo)
    {
        var parts = new List<string>();
        if (resolvedTo.Length > 0) parts.Add(resolvedTo);
        // An arrival taken from the send time says so, so a driver who typed
        // "ถึงโรงงาน" an hour after the fact knows to send the clock.
        if (read.ArrivalTime is { } at) parts.Add(read.ArrivalAtSend ? $"ถึง {at:HH:mm} (เวลาที่ส่งข้อความ)" : $"ถึง {at:HH:mm}");
        if (read.Plates is { Count: > 0 } plates) parts.Add($"ทะเบียน {plates[0]}");
        if (read.Driver is not null) parts.Add($"คนขับ {read.Driver}");
        if (read.Phone is not null) parts.Add($"เบอร์ {read.Phone}");
        if (read.SealNumber is not null) parts.Add($"ซีล {read.SealNumber}");
        if (read.Eta is { } eta && read.ArrivalTime is null) parts.Add($"คาดถึง {eta:HH:mm}");
        if (read.Delayed && read.DelayCategory is { } why) parts.Add($"ล่าช้า ({why})");
        return string.Join(" · ", parts);
    }

    /// <summary>
    /// The line back for a text message the worker just filed, or null for
    /// silence. <paramref name="outcome"/> is the row's error code — the
    /// parser's warning or the rule's verdict.
    /// </summary>
    public static string? ForMessage(LineParser.Parsed read, string outcome, string resolvedTo)
    {
        if (read.Question) return null;
        var reference = Reference(read);
        // Nothing that names a job — no job number, booking, container, plate
        // or seal — nothing said back. The department's rule, 17 Sep 2026: the
        // room talks about many things, and the bot is not one of its members.
        if (reference.Length == 0) return null;
        var said = Said(read, resolvedTo);
        return outcome switch
        {
            "ready-to-apply" => $"รับทราบ {reference}{(said.Length > 0 ? " — " + said : "")} · รอเจ้าหน้าที่ยืนยันครับ",
            LineAuthority.Outcome.ManyJobs => $"รับทราบ {reference} — ตรงกับหลายงาน เจ้าหน้าที่จะเลือกงานให้ครับ",
            LineAuthority.Outcome.AlreadyThere => $"รับทราบ {reference} — งานอยู่ที่สถานะนี้แล้วครับ",
            // The same sentence for a number that is nobody's and one that is
            // somebody else's — see the note above.
            LineAuthority.Outcome.NoSuchJob or LineAuthority.Outcome.NotYourJob =>
                $"ไม่พบ {reference} ในงานของท่านช่วงนี้ — ช่วยตรวจเลขตู้ / Job No. อีกครั้งครับ",
            // Unreachable now that a message with no reference is never
            // answered; kept as the outcome's name for a row that stores it.
            "no-reference" => null,
            // A greeting, a "รับทราบ": not about a job, and not answered.
            Services.LineEventWorker.NotAboutAJob => null,
            "nothing-understood" => $"รับ {reference} แล้ว แต่ไม่พบสถานะ — ส่ง \"ถึงโรงงาน HH:MM\" หรือ \"ลงเสร็จ\" มาด้วยครับ",
            "many-job-numbers" or "many-containers" => "ข้อความมีหลายงานปนกัน — ช่วยส่งทีละตู้ครับ",
            LineAuthority.Outcome.NoStatus => null,
            LineAuthority.Outcome.UnknownGroup or LineAuthority.Outcome.GroupInactive or LineAuthority.Outcome.GroupNotVendor => null,
            "" => null,
            _ => $"รับทราบ {reference} — เจ้าหน้าที่จะตรวจสอบครับ",
        };
    }

}
