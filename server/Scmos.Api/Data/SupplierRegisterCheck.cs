using Scmos.Api.Rules;

namespace Scmos.Api.Data;

/// <summary>
/// Reading the ASL/BSL list, with <c>--check-register</c>.
///
/// <para>
/// The import puts 642 companies into the table that decides who a job may be
/// given to and whose score the department reads. Everything it decides before
/// touching the database is decided here, so it can be checked without the file
/// and without a database — which matters, because the file holds the contact
/// details of 642 real companies and is not in the repository.
/// </para>
/// </summary>
public static class SupplierRegisterCheck
{
    public static int? Run(string[] args)
    {
        if (!args.Contains("--check-register")) return null;

        var failed = 0;

        void Check(bool ok, string why)
        {
            if (!ok) failed++;
            Console.WriteLine($"  {(ok ? "ok  " : "FAIL")}  {why}");
        }

        Console.WriteLine();
        Console.WriteLine("Reading a cell.");
        Console.WriteLine();

        Check(SupplierRegister.Tidy("  Dubey trading  ") == "Dubey trading",
            "a value is trimmed");
        Check(SupplierRegister.Tidy("02-294-3150   #   127") == "02-294-3150 # 127",
            "and runs of spaces inside it are collapsed");
        Check(SupplierRegister.Tidy(null) == "", "nothing reads as nothing");

        // The file writes these where somebody had nothing to put. Stored as
        // they are, they become 300 fax numbers of "-".
        Check(SupplierRegister.Tidy("-") == "", "a dash is not a fax number");
        Check(SupplierRegister.Tidy("N/A") == "", "nor is N/A a credit term");
        Check(SupplierRegister.Tidy("n/a") == "", "however it is cased");
        Check(SupplierRegister.Tidy("None") == "", "nor None");
        Check(SupplierRegister.Tidy("0") == "0", "but a real zero survives, because 0 days is a term");

        Console.WriteLine();
        Console.WriteLine("The ABS number, however Excel hands it over.");
        Console.WriteLine();

        Check(SupplierRegister.AbsNo(" 331817 ") == "331817", "as text with spaces");
        Check(SupplierRegister.AbsNo(331817) == "331817", "as a number");
        // Excel stores integers as doubles, and ToString on one of those is
        // where "331817" quietly becomes "331817.0" and stops joining to ABS.
        Check(SupplierRegister.AbsNo(331817.0) == "331817", "and as a double, without gaining a point-zero");
        Check(SupplierRegister.AbsNo("") == "", "a blank stays blank");
        Check(SupplierRegister.AbsNo("A-1234") == "A-1234", "one that is not a number is left as written");

        Console.WriteLine();
        Console.WriteLine("Which list a company is on.");
        Console.WriteLine();

        Check(SupplierRegister.ListOf("ASL") == "ASL", "ASL");
        Check(SupplierRegister.ListOf(" bsl ") == "BSL", "BSL, however cased and padded");
        // A third value would be a change to how the company classifies its
        // suppliers, and accepting one quietly would put a value in the column
        // that no screen knows how to filter on.
        Check(SupplierRegister.ListOf("CSL") == "", "and nothing else is accepted");
        Check(SupplierRegister.ListOf(null) == "", "nor an empty cell");

        Console.WriteLine();
        Console.WriteLine("Is the register's short name this company's long one?");
        Console.WriteLine();

        Check(SupplierRegister.CouldBeTheSame("KANIN", "Kanin Transport Co., Ltd."),
            "KANIN could be Kanin Transport Co., Ltd.");
        Check(SupplierRegister.CouldBeTheSame("A.C.N", "A C N Transport Co., Ltd."),
            "and punctuation on either side is ignored");
        Check(!SupplierRegister.CouldBeTheSame("SANGJA", "Kanin Transport Co., Ltd."),
            "but an unrelated name is not a candidate");
        Check(!SupplierRegister.CouldBeTheSame("SJ", "SJ Logistics Co., Ltd."),
            $"and nothing shorter than {SupplierRegister.ShortestPrefix} characters is offered at all");
        Check(!SupplierRegister.CouldBeTheSame("KANIN", "Kanin"),
            "a name identical to itself is not a prefix match — that is the exact-key path");

        Console.WriteLine();
        Console.WriteLine("And does it stop where a word stops?");
        Console.WriteLine();

        Check(SupplierRegister.EndsOnAWord("KANIN", "Kanin Transport Co., Ltd."),
            "KANIN stops where Kanin stops");
        Check(SupplierRegister.EndsOnAWord("9ISARA", "9 Isara Transport Co., Ltd."),
            "9ISARA spans two words and stops at the end of the second");
        Check(SupplierRegister.EndsOnAWord("A.C.N", "A C N Transport Co., Ltd."),
            "A.C.N spans three");
        Check(SupplierRegister.EndsOnAWord("THREETRANS", "Three Trans (1995) Co., Ltd."),
            "and THREETRANS spans two that were written as one");

        /*
         * The regression this test exists for.
         *
         * Prefix-and-unique matched W.A.K to Wako Logistics (Thailand) Co.,
         * Ltd. — an initialism that happens to begin somebody else's name. It
         * was the only company in 642 starting WAK, so uniqueness said yes.
         * Merging them would have put one company's work on another company's
         * scorecard, quietly, and the department would have found out from an
         * invoice.
         */
        Check(!SupplierRegister.EndsOnAWord("W.A.K", "Wako Logistics (Thailand) Co., Ltd."),
            "W.A.K does NOT stop where a word stops in Wako Logistics — this is the one that mattered");
        Check(SupplierRegister.CouldBeTheSame("W.A.K", "Wako Logistics (Thailand) Co., Ltd."),
            "  (it does pass the weaker test, which is why the weaker test is not enough)");

        // Probably the same company. Still not something to settle from a
        // spreadsheet, so it is reported rather than merged.
        Check(!SupplierRegister.EndsOnAWord("THAIKOT", "Thaikotchasarn Logistics Service Co., Ltd."),
            "and THAIKOT stops inside Thaikotchasarn, so it is left for a person too");

        Check(!SupplierRegister.EndsOnAWord("", "Kanin Transport Co., Ltd."),
            "an empty name matches nothing");

        Console.WriteLine();
        Console.WriteLine("Who moves cargo by road.");
        Console.WriteLine();

        Check(SupplierRegister.LooksLikeCarrier("Transportation Services"), "Transportation Services");
        Check(SupplierRegister.LooksLikeCarrier("Logistic Agent; Transportation Services"),
            "and one of several services counts");
        Check(SupplierRegister.LooksLikeCarrier("Transportation Services; ISO Tank"), "so does ISO Tank");
        Check(SupplierRegister.LooksLikeCarrier("TRANSPORTATION SERVICES"), "however it is cased");

        // The 560 that must not turn up in the scorecard or the job pool.
        Check(!SupplierRegister.LooksLikeCarrier("Logistic Agent"), "a logistics agent does not");
        Check(!SupplierRegister.LooksLikeCarrier("Sea Freight Agents/Liner"), "nor a liner");
        Check(!SupplierRegister.LooksLikeCarrier("Customs Clearance"), "nor a customs broker");
        Check(!SupplierRegister.LooksLikeCarrier("Deposit Container; Sea Freight Co-Loaders"),
            "nor a container depot");
        Check(!SupplierRegister.LooksLikeCarrier(""), "and an empty description is not a claim to be one");

        // The column has 115 spellings of about twenty things, so the words are
        // matched inside the text rather than the whole value being listed.
        Check(SupplierRegister.LooksLikeCarrier("Sea Freight Agents / Liner; Transportation Services")
            && SupplierRegister.LooksLikeCarrier("Sea Freight Agents/Liner; Transportation Services"),
            "and a stray space in the spelling changes nothing");

        Console.WriteLine();
        Console.WriteLine("What is worth storing.");
        Console.WriteLine();

        Check(SupplierRegister.Usable("Kanin Transport Co., Ltd."), "a company with a name is a supplier");
        Check(!SupplierRegister.Usable(""), "a blank line at the bottom of a sheet is not");
        Check(!SupplierRegister.Usable("   "), "nor is a row of spaces");
        Check(!SupplierRegister.Usable(",. -"), "nor one of punctuation, which would key to nothing");

        // On procurement's list is not the same as approved by this department.
        Check(SupplierRegister.NewStatus == "pending-audit",
            "an imported company starts pending-audit, not approved");

        Console.WriteLine();
        Console.WriteLine(failed == 0
            ? "All supplier register checks passed."
            : $"{failed} supplier register check(s) failed.");
        return failed == 0 ? 0 : 1;
    }
}
